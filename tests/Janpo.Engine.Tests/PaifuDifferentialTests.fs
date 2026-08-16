namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Newtonsoft
open Janpo

/// 与**真实牌谱**的对拍：用天凤鳳凰卓的牌谱重放引擎，逐局比动作序列、
/// 每次和了的役种集合 / 符 / 番 / 和了点，以及流局的形态与逐座位清算。
///
/// 固件 `fixtures/paifu/`（18 局 / 177 kyoku）随测试工程复制到输出目录，因此这个测试
/// **离线跑**；扩样本只需把 `JANPO_PAIFU_DIR` 指向更大的语料，不改代码。
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
        Assert.True(List.length loaded >= 18, $"语料只有 {List.length loaded} 局对局，对拍要有量")
        Assert.True(List.length differential.Value.Kyokus >= 177, $"语料只有 {List.length differential.Value.Kyokus} 局")
        Assert.True(loaded |> List.forall (snd >> Option.isSome), "固件里每一局都该配着天凤 JSON oracle")

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
        Assert.True(seats >= 40, $"只对拍了 {seats} 个座位的清算，锚点太细了")

    [<Fact>]
    let ``对拍的覆盖率够得着长尾`` () =
        let horas = sumBy (fun kyoku -> kyoku.Horas)
        let fu = sumBy (fun kyoku -> kyoku.FuChecks)
        let yaku = sumBy (fun kyoku -> kyoku.YakuChecks)

        Assert.True(horas >= 150, $"只对拍了 {horas} 次和了")
        Assert.True(fu >= 100, $"只比到了 {fu} 次符（満貫以上天凤不写符）")
        Assert.True(yaku >= 200, $"只比到了 {yaku} 行役")

        // 役名对照表里真正被牌谱走到的那几种。剩下的靠黄金用例，不靠对拍。
        let seen =
            differential.Value.Kyokus
            |> List.collect (fun kyoku -> kyoku.YakuSeen)
            |> List.distinct

        Assert.True(List.length seen >= 20, $"牌谱里只出现了 {List.length seen} 种役")

        // 每局终局点数：拿牌谱下一局的开局点数对拍（一场的最后一局没下局，比不了）。
        let carried = sumBy (fun kyoku -> kyoku.ScoreChecks)
        Assert.True(carried >= 150, $"只对拍了 {carried} 局的终局点数")

        // 流局形态：荒牌流局与九种九牌两种在固件里都有。
        let reasons =
            differential.Value.Kyokus
            |> List.choose (fun kyoku -> kyoku.Reason)
            |> List.distinct

        Assert.Contains(Fanpai, reasons)
        Assert.Contains(KyuushuKyuuhai, reasons)

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
