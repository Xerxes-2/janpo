namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Newtonsoft
open Janpo

/// 与**真实牌谱**的对拍：用天凤鳳凰卓的牌谱重放引擎，逐局比动作序列、
/// 每次和了的役种集合 / 符 / 番 / 和了点，以及流局的形态与逐座位清算。
///
/// 固件 `fixtures/paifu/`（**96 场 / 987 kyoku**，票 57 按覆盖挑出来、票 59 又补上两种杠的时机）随测试工程复制到输出目录，
/// 因此这个测试**离线跑**；扩样本只需把 `JANPO_PAIFU_DIR` 指向更大的语料，不改代码
/// （票 57 用同一套 API 扫过全量 12,188 场 / 129,179 kyoku，跑法见
/// `.scratch/llm-riichi-arena/run/reports/57-wider-paifu-differential.md`）。
///
/// **两个数据源各管一半**：动作序列从 mjai 牌谱取（上游把役 / 符 / 点数删了），
/// 役 / 符 / 番 / 点数 / 流局形态从天凤官方 JSON 取作 oracle。
///
/// **独立锚点在最后一条**：荒牌流局的听牌判定逐座位对拍天凤自己的钱流。
/// 其余各条的期望值多少还经过天凤的显示逻辑，而钱流是外部事实——
/// 它与我们的实现无因果关系，因此它证明的东西比「实现与自己一致」多（备注 N-10）。
module PaifuDifferentialTests =

    let private corpus = lazy (PaifuCorpus.load ())

    let private differential = lazy (corpus.Value |> fst |> PaifuDifferential.run)

    let private diffs =
        lazy (differential.Value.Kyokus |> List.collect (fun kyoku -> kyoku.Diffs))

    /// 只看某几类的差异。分类见 `PaifuDiff.Kind`。
    let private ofKinds (kinds: string list) : string array =
        diffs.Value
        |> List.filter (fun each -> List.contains each.Kind kinds)
        |> List.map (fun each -> $"{each.Where} [{each.Kind}] {each.Detail}")
        |> List.toArray

    /// 差异照可读形式报出来，先给前 10 条，再断言总数为 0。
    let private assertNoDiff (kinds: string list) =
        let reported = ofKinds kinds
        Assert.Equal<string array>([||], Array.truncate 10 reported)
        Assert.Equal(0, Array.length reported)

    let private sumBy (pick: KyokuDifferential -> int) : int =
        differential.Value.Kyokus |> List.sumBy pick

    [<Fact>]
    let ``固件挂进了测试工程，且一局都没有被跳过`` () =
        let loaded, skipped = corpus.Value

        let rendered =
            (skipped @ differential.Value.Skipped)
            |> List.map (fun (where, reason) -> $"{where}：{reason}")
            |> List.toArray

        Assert.Equal<string array>([||], Array.truncate 10 rendered)
        Assert.True(List.length loaded >= 96, $"语料只有 {List.length loaded} 场对局，对拍要有量")
        Assert.True(List.length differential.Value.Kyokus >= 980, $"语料只有 {List.length differential.Value.Kyokus} 局")
        Assert.True(loaded |> List.forall (snd >> Option.isSome), "固件里每一场都该配着天凤 JSON oracle")

    [<Fact>]
    let ``默认规则集就是牌谱那一套天凤规则`` () =
        // ADR-0004 决定 3 与晨间裁决 R-1 已经把默认值对齐天凤。**这条断言验它，不假定**：
        // 默认值一旦被改回去，对拍会以「实现错」的面目失败，那时分不清是哪一头错了。
        let ruleset = PaifuDifferential.ruleset

        Assert.False(ruleset.Atamahane, "牌谱里双响成立，头跳必须关")
        Assert.False(ruleset.KiriageMangan, "牌谱里 30符4飜 给 7700 点，切上满贯必须关")
        Assert.True(ruleset.SanchaHoraRyuukyoku, "天凤把三家和了判成途中流局")
        Assert.False(ruleset.KokushiAnkanChankan, "天凤禁止国士抢暗杠")
        Assert.True(ruleset.RinshanTsumoFu, "天凤给岭上自摸加自摸符")
        Assert.True(ruleset.Kuitan, "`鳳南喰赤` 的「喰」")
        Assert.Equal(4, ruleset.DoubleKazeJantouFu)
        Assert.Equal(3, List.length ruleset.Akadora)
        Assert.Equal(25000, ruleset.StartingScore)
        Assert.Equal(1000, ruleset.RiichiBou)
        Assert.Equal(3000, ruleset.NotenBappu)
        Assert.Equal(300, ruleset.HonbaPoints)
        Assert.Equal(Hanchan, ruleset.Length)

        // 固件全部是同一套规则，牌谱自己说了算。
        let rules =
            corpus.Value
            |> fst
            |> List.choose (snd >> Option.map (fun oracle -> oracle.Rule))
            |> List.distinct

        Assert.Equal<string list>([ "鳳南喰赤" ], rules)

    [<Fact>]
    let ``日文役名对照表覆盖 Yaku 的每一个 case`` () =
        // `OracleYaku.japanese` 是穷尽 match，**「表漏了某个役」由编译器保证不可能发生**；
        // 这里补两件编译器管不了的事：名字不能撞，且反向解析要回到同一个役。
        let names = OracleYaku.all |> List.map OracleYaku.japanese

        Assert.Equal(List.length names, names |> List.distinct |> List.length)
        Assert.Equal(51, List.length names)

        let roundTrips =
            OracleYaku.all
            |> List.filter (fun yaku -> OracleYaku.parse (OracleYaku.japanese yaku) <> Some(OracleYaku.Yaku yaku))
            |> List.map OracleYaku.japanese
            |> List.toArray

        Assert.Equal<string array>([||], roundTrips)

        // 三种宝牌在天凤是役行，在我们的 `Yaku` 里不是，因此单列。
        Assert.Equal(3, List.length OracleYaku.doraNames)

    [<Fact>]
    let ``重放产出的事件流与牌谱零差异`` () = assertNoDiff [ "重放"; "事件流" ]

    [<Fact>]
    let ``和了的役种集合、符、番与和了点与天凤零差异`` () =
        assertNoDiff [ "役"; "符"; "番"; "和了点"; "宝牌"; "和了者" ]

    [<Fact>]
    let ``流局形态与天凤零差异`` () = assertNoDiff [ "流局形态" ]

    [<Fact>]
    let ``独立锚点：荒牌流局的听牌判定逐座位对拍天凤清算`` () =
        assertNoDiff [ "清算" ]

        // 「零差异」若没有覆盖率佐证，只说明没跑到（备注 N-8）。
        let seats = sumBy (fun kyoku -> kyoku.SettledSeats)
        Assert.True(seats >= 490, $"只对拍了 {seats} 个座位的清算，锚点太细了")

    [<Fact>]
    let ``对拍的覆盖率够得着长尾`` () =
        let horas = sumBy (fun kyoku -> kyoku.Horas)
        let fu = sumBy (fun kyoku -> kyoku.FuChecks)
        let yaku = sumBy (fun kyoku -> kyoku.YakuChecks)

        Assert.True(horas >= 840, $"只对拍了 {horas} 次和了")
        Assert.True(fu >= 615, $"只比到了 {fu} 次符（満貫以上天凤不写符）")
        Assert.True(yaku >= 1520, $"只比到了 {yaku} 行役")

        // 役名对照表里真正被牌谱走到的那几种。剩下的靠黄金用例，不靠对拍。
        let seen =
            differential.Value.Kyokus
            |> List.collect (fun kyoku -> kyoku.YakuSeen)
            |> List.distinct

        // 票 59 把三槓子那一场（全量语料里仅 1 次）从差异场变回了干净场，因此牌谱里出现过的
        // 38 种役现在**一种不漏**都在固件里。
        Assert.True(List.length seen >= 38, $"牌谱里只出现了 {List.length seen} 种役")

        // 每局终局点数：拿牌谱下一局的开局点数对拍（一场的最后一局没下局，比不了）。
        let carried = sumBy (fun kyoku -> kyoku.ScoreChecks)
        Assert.True(carried >= 880, $"只对拍了 {carried} 局的终局点数")

    [<Fact>]
    let ``七种流局形态里，真牌谱守得住六种；三家和了守不住，且理由不是罕见`` () =
        // **判据 3：闸门要报「它在真语料上执行过几次」。** 下面每一种都带着次数——
        // 数字是票 57 从 12,188 场里按覆盖挑固件时定下的，少一次就说明固件被换稀了。
        let counted =
            differential.Value.Kyokus
            |> List.choose (fun kyoku -> kyoku.Reason)
            |> List.countBy id
            |> Map.ofList

        let times (reason: RyuukyokuReason) : int =
            counted |> Map.tryFind reason |> Option.defaultValue 0

        let rendered =
            counted
            |> Map.toList
            |> List.map (fun (reason, count) -> $"{RyuukyokuReason.toMjai reason}×{count}")

        for reason, atLeast in
            [
                Fanpai, 120
                KyuushuKyuuhai, 12
                SuufonRenda, 4
                NagashiMangan, 3
                Suukaikan, 2
                SuuchaRiichi, 2
            ] do
            Assert.True(
                times reason >= atLeast,
                $"{RyuukyokuReason.toMjai reason} 在固件里只走到 {times reason} 次（要 {atLeast} 次）：{rendered}"
            )

        // **到不了的那一种要写成一个值**（判据 4），别留在注释里当旁白。
        // **三家和了不是罕见**：全量语料里有 6 局（天凤 JSON 逐条确认写的就是 `三家和了`），
        // 是**上游转换器把三条 `hora` 宣言删成了一条裸 `ryukyoku`**，mjai 流里那三家荣和的事实
        // 已经没了，重放喂不出来（票 57 报告第三节 D）。换一份保留宣言的牌谱源就守得住。
        //
        // 这条断言只会变硬：`RyuukyokuReason` 新增一个 case 而固件没跟上，它当场红。
        let unreachable = [ SanchaHora ]

        let missing =
            RyuukyokuReason.all
            |> List.filter (fun reason -> times reason = 0)
            |> List.except unreachable
            |> List.map RyuukyokuReason.toMjai

        Assert.Equal<string list>([], missing)

    /// 牌谱事件流里两种「杠的时机」各出现了几次，**从牌谱自己数**（不问引擎，
    /// 避免拿被测物当期望值）：`<前一杠>-then-<后一杠>` 是「打牌之前又杠一次」，
    /// `<杠>-rinshan-hora` 是「补摸的那张当场和了」。逻辑与 `scripts/fsi/paifu-scan.fsx`
    /// 的 `kanTags` 同一份（那一份持着全量语料扫，这一份持着固件守 CI）。
    let private kanTimings (moves: PaifuEvent list) : string list =
        let isDora (event: PaifuEvent) =
            match event with
            | PaifuEvent.Dora _ -> true
            | _ -> false

        let rec walk (events: PaifuEvent list) (acc: string list) : string list =
            match events with
            | PaifuEvent.Ankan _ :: rest -> afterKan "ankan" rest acc
            | PaifuEvent.Kakan _ :: rest -> afterKan "kakan" rest acc
            | PaifuEvent.Minkan _ :: rest -> afterKan "daiminkan" rest acc
            | _ :: rest -> walk rest acc
            | [] -> acc

        and afterKan (kind: string) (events: PaifuEvent list) (acc: string list) : string list =
            match List.skipWhile isDora events with
            | PaifuEvent.Tsumo _ :: rest ->
                match List.skipWhile isDora rest with
                | PaifuEvent.Hora _ :: _ -> walk rest ($"{kind}-rinshan-hora" :: acc)
                | PaifuEvent.Ankan _ :: _ -> walk rest ($"{kind}-then-ankan" :: acc)
                | PaifuEvent.Kakan _ :: _ -> walk rest ($"{kind}-then-kakan" :: acc)
                | PaifuEvent.Minkan _ :: _ -> walk rest ($"{kind}-then-daiminkan" :: acc)
                | _ -> walk rest acc
            // 加杠之后没补摸就有人和了（抢杠），或者牌谱到此为止。
            | rest -> walk rest acc

        walk moves []

    [<Fact>]
    let ``两处杠的时机各自在固件里走到几次`` () =
        // **判据 3：闸门要报「它在真语料上执行过几次」。** 票 57 挑固件时把带差异的场一律排除了，
        // 而这两类恰恰局局带差异，于是它们在 CI 里的执行次数是 **0**（本票之前）。
        // 下面每一种都带着次数，少一次就说明固件被换稀了。
        let counted =
            corpus.Value
            |> fst
            |> List.collect (fun (paifu, _) -> paifu.Kyokus)
            |> List.collect (fun kyoku -> kanTimings kyoku.Moves)
            |> List.countBy id
            |> Map.ofList

        let times (shape: string) : int =
            counted |> Map.tryFind shape |> Option.defaultValue 0

        let rendered =
            counted |> Map.toList |> List.map (fun (shape, count) -> $"{shape}×{count}")

        for shape, atLeast in
            [
                // 票 59 第一处：打牌之前连着两次杠。**前一杠是明杠的那四种就是那 28 局反例**，
                // 四种的事件顺序两两不同，因此四种都要有（全量语料里各 19 / 6 / 3 / 1 局）。
                "kakan-then-kakan", 2
                "daiminkan-then-kakan", 2
                "kakan-then-ankan", 1
                "daiminkan-then-ankan", 1
                // 暗杠打头的那两种两边本来就一致，一并守着（它们证明补翻没多翻）。
                "ankan-then-ankan", 1
                // 票 59 第二处：大明杠 → 岭上开花（责任支付的口径，全量 24 局 24 局全错）。
                "daiminkan-rinshan-hora", 3
                "kakan-rinshan-hora", 3
                "ankan-rinshan-hora", 3
            ] do
            Assert.True(times shape >= atLeast, $"{shape} 在固件里只走到 {times shape} 次（要 {atLeast} 次）：{rendered}")

        // `ankan-then-kakan` 与 `daiminkan-then-daiminkan` 不在名单里：前者全量语料里有 10 局但
        // 两边本来就一致（暗杠不欠账），后者**规则上就不存在**（大明杠要他家打出一张，
        // 而杠完没打牌）——判据 4：到不了的要写成代码里的一个值。
        Assert.Equal(0, times "daiminkan-then-daiminkan")

    // ---- 牌谱适配器本身 ----

    /// 一行牌谱的解码。
    let private readPaifu (line: string) =
        Decode.fromString PaifuEvent.decoder line

    [<Fact>]
    let ``字牌的字母记法只在适配器里映射，引擎的 Tile.parse 不放宽`` () =
        // 牌谱写 `E`/`S`/`W`/`N`/`P`/`F`/`C`，ADR-0001 定的内部规范形只有 `1z`-`7z`。
        let mapped = [ "E"; "S"; "W"; "N"; "P"; "F"; "C" ] |> List.map PaifuNotation.toMjai

        Assert.Equal<string list>([ "1z"; "2z"; "3z"; "4z"; "5z"; "6z"; "7z" ], mapped)
        // 数牌与赤 5 两边本来一致，原样返回。
        Assert.Equal("5mr", PaifuNotation.toMjai "5mr")

        // **引擎一侧一字未动**：字母记法在 `Tile.parse` 那里仍然是非法的。
        match Tile.parse "P" with
        | Ok tile -> failwith $"`Tile.parse` 不该认字母记法，却读出了 {Tile.toMjai tile}"
        | Error _ -> ()

        match readPaifu """{"type":"tsumo","actor":2,"pai":"F"}""" with
        | Ok(PaifuEvent.Tsumo(_, pai)) -> Assert.Equal("6z", Tile.toMjai pai)
        | other -> failwith $"应当读成 6z 的自摸，却得到 {other}"

    [<Fact>]
    let ``瓦身版的 hora / ryukyoku 只有只读的 PaifuEvent 读得了`` () =
        // 这正是另立一个类型的理由：牌谱缺役 / 符 / 番 / 点数 / 流局形态，
        // 强塞进 `Event` 会把「引擎自己产出的事件必然完整」这条不变量弄脏。
        let hora =
            """{"type":"hora","actor":1,"target":3,"deltas":[0,8000,0,-8000],"ura_markers":["P"]}"""

        let ryuukyoku = """{"type":"ryukyoku","deltas":[1000,1000,1000,-3000]}"""

        match readPaifu hora, readPaifu ryuukyoku with
        | Ok(PaifuEvent.Hora fields), Ok(PaifuEvent.Ryuukyoku deltas) ->
            Assert.Equal<int list>([ 0; 8000; 0; -8000 ], fields.Deltas)
            Assert.Equal<string list>([ "5z" ], fields.UraDoraMarkers |> List.map Tile.toMjai)
            Assert.Equal<int list>([ 1000; 1000; 1000; -3000 ], deltas)
        | other -> failwith $"瓦身版应当读得了，却得到 {other}"

        Assert.True(Result.isError (Decode.fromString Event.decoder hora), "`Event.decoder` 不该读得了瓦身版的 hora")

        Assert.True(Result.isError (Decode.fromString Event.decoder ryuukyoku), "`Event.decoder` 不该读得了瓦身版的 ryukyoku")

    [<Fact>]
    let ``三麻的 nukidora 认得出来，整局显式跳过`` () =
        // 本票只跑四麻。上游数据集里零出现，但混进来的话不能静默当没看见。
        match readPaifu """{"type":"nukidora","actor":0,"pai":"N"}""" with
        | Ok event -> failwith $"三麻的拔北不该读得了，却得到 {event}"
        | Error _ -> ()

        let game =
            PaifuGame.parse
                "三麻样本"
                [
                    """{"type":"start_game","names":["a","b","c"]}"""
                    """{"type":"nukidora","actor":0,"pai":"N"}"""
                ]

        Assert.True(Result.isError game, "含不支持事件的样本该整局报错，由调用方计数地跳过")

    [<Fact>]
    let ``只有 mjai 流时，流し満貫由 deltas 的档位认出来`` () =
        // 两批语料（218 局）里流し満貫**零出现**，因此它的判据只能靠这条守着。
        // 听牌料只可能是 {0, ±1000, ±1500, ±3000}，流し満貫是 {±12000, ±8000, ±4000, ±2000}。
        Assert.False(PaifuOracle.isNagashiDeltas [ 3000; -1000; -1000; -1000 ])
        Assert.False(PaifuOracle.isNagashiDeltas [ 1500; 1500; -1500; -1500 ])
        Assert.True(PaifuOracle.isNagashiDeltas [ 8000; -4000; -2000; -2000 ])
        Assert.True(PaifuOracle.isNagashiDeltas [ -12000; 12000; 0; 0 ])

        // 听牌料的钱流反推得出逐座位听牌；全 0 与流し満貫都反推不出来。
        Assert.Equal<bool list option>(
            Some [ true; false; false; false ],
            PaifuOracle.tenpaisFromDeltas [ 3000; -1000; -1000; -1000 ]
        )

        Assert.Equal<bool list option>(None, PaifuOracle.tenpaisFromDeltas [ 0; 0; 0; 0 ])
        Assert.Equal<bool list option>(None, PaifuOracle.tenpaisFromDeltas [ 8000; -4000; -2000; -2000 ])
