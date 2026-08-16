namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 鸣牌路径的后半：三种杠与它们的连带效果——补摸岭上牌、翻新宝牌、岭上开花、抢杠、
/// 大明杠与大三元的责任支付。连带密度最高，因此用例按**组合**写：一个剧本跑到底，
/// 沿路把「杠成立之后发生了什么」一条条按住。
///
/// 牌山与**岭上牌**都是摊出来的（`startScriptedRinshan`），因此「补摸的是哪张」是确定的事实。
/// 不变量（牌数守恒、王牌张数、可摸区张数）见 KanProperties。
module KanTests =

    let private pai (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"{notation} 应当是合法记法，却得到 {error}"

    let private stepped (state: GameState) (action: Action) =
        match GameState.step state action with
        | Ok(next, events) -> next, events
        | Error illegal -> failwith $"这个动作应当合法，却被拒：{IllegalAction.toDisplay illegal}"

    let private rejected (state: GameState) (action: Action) =
        match GameState.step state action with
        | Error illegal -> illegal
        | Ok _ -> failwith "这个动作应当被拒，却被接受了"

    /// 现在在等哪些座位。
    let private waiting (state: GameState) : Seat list =
        GameState.legalActions state |> List.map (fun choice -> choice.Seat)

    let private actionsOf (seat: Seat) (state: GameState) : Action list =
        GameState.legalActions state
        |> List.tryFind (fun choice -> choice.Seat = seat)
        |> Option.map (fun choice -> choice.Actions)
        |> Option.defaultValue []

    let private handOf (seat: Seat) (state: GameState) : Tile list =
        GameState.player seat state
        |> Option.map PlayerState.hand
        |> Option.defaultValue []

    let private ippatsuOf (seat: Seat) (state: GameState) : bool =
        GameState.player seat state
        |> Option.map PlayerState.ippatsu
        |> Option.defaultValue false

    /// 某座位此刻和了的话成立哪些役。
    let private yakuOf (seat: Seat) (state: GameState) : Yaku list =
        match GameState.horaOf seat state with
        | Ok reading -> YakuTally.yaku reading.Tally
        | Error error -> failwith $"座位 {seat} 此刻应当和得了，却得到 {YakuError.toDisplay error}"

    let private theHora (state: GameState) : Hora =
        match GameState.horas state with
        | [ hora ] -> hora
        | other -> failwith $"这一局应当恰有一次和了，实际是 {List.length other} 次"

    // ---- 合法动作集 ----

    [<Fact>]
    let ``暗杠进摸牌后阶段的动作集，亮出的是手里那四张`` () =
        let state = startScriptedRinshan "5z" ankanScript

        Assert.Contains(Action.Ankan(0, tilesOf "9s 9s 9s 9s"), actionsOf 0 state)

        // 杠不是响应：他家打牌之前谈不上大明杠，摸牌后阶段也没有「刚打出的那张」。
        Assert.Equal<IllegalAction>(
            NothingToRespond 0,
            rejected state (Action.Minkan(0, 1, pai "9s", tilesOf "9s 9s 9s"))
        )

    [<Fact>]
    let ``加杠进摸牌后阶段的动作集，consumed 是底下那组碰的三张`` () =
        let state = startScripted kakanScript
        let discarded, _ = stepped state (Action.Dahai(0, pai "5s", true))
        let ponned, _ = stepped discarded (Action.Pon(1, 0, pai "5s", tilesOf "5s 5sr"))
        let ponned, _ = stepped ponned (Action.None 2)

        // 刚碰完那一手没摸牌，因此加不了杠（真实牌谱里每条 kakan 都紧跟在自家的 tsumo 之后）。
        Assert.DoesNotContain(Action.Kakan(1, pai "5s", tilesOf "5s 5s 5sr"), actionsOf 1 ponned)

        let discarded, _ = stepped ponned (Action.Dahai(1, pai "1z", false))
        let drawn = driveUntil passive (fun state -> junmeOf 1 state = 1) discarded

        Assert.Contains(Action.Kakan(1, pai "5s", tilesOf "5s 5s 5sr"), actionsOf 1 drawn)

    [<Fact>]
    let ``大明杠与碰并列进响应阶段的动作集，排在吃前面`` () =
        let state = startScripted kanRaceScript
        let discarded, _ = stepped state (Action.Dahai(0, pai "5s", true))

        // 座位 1 只吃得了、座位 2 碰得了也杠得了、座位 3 荣和得了。
        Assert.Equal<Seat list>([ 1; 2; 3 ], waiting discarded)

        // 碰有两种亮法（红 5 亮不亮），大明杠只有一种（三张全上），且排在碰之后吃之前。
        Assert.Equal<Action list>(
            [
                Action.Pon(2, 0, pai "5s", tilesOf "5s 5s")
                Action.Pon(2, 0, pai "5s", tilesOf "5s 5sr")
                Action.Minkan(2, 0, pai "5s", tilesOf "5s 5s 5sr")
                Action.None 2
            ],
            actionsOf 2 discarded
        )

    [<Fact>]
    let ``立直后暗杠：判据过得了就进动作集，一发被自己这一杠打掉`` () =
        let state = startScripted riichiAnkanScript
        let declared, _ = stepped state (Action.Riichi 0)
        let discarded, _ = stepped declared (Action.Dahai(0, pai "1z", false))
        let drawn = driveUntil passive (fun state -> junmeOf 0 state = 2) discarded

        // 立直成立之后一发亮着，这一手只剩自摸和、暗杠与摸切。
        Assert.True(ippatsuOf 0 drawn)
        Assert.Contains(Action.Ankan(0, tilesOf "5z 5z 5z 5z"), actionsOf 0 drawn)

        let kanned, _ = stepped drawn (Action.Ankan(0, tilesOf "5z 5z 5z 5z"))

        // 任何鸣牌都打断全场一发，**自己的暗杠也算**。
        Assert.False(ippatsuOf 0 kanned)

    [<Fact>]
    let ``禁送り杠：立直时就握着的那四张，之后摸进别的牌也杠不得`` () =
        let state = startScripted okuriKanScript
        let declared, _ = stepped state (Action.Riichi 0)
        let discarded, _ = stepped declared (Action.Dahai(0, pai "3z", true))
        let drawn = driveUntil passive (fun state -> junmeOf 0 state = 2) discarded

        // 手里确实有四张 1p，但刚摸进的是 5z：杠了就是送り杠，判据（09 的 `allowsAnkan`）不许。
        Assert.Equal(4, handOf 0 drawn |> List.filter (fun tile -> tile = pai "1p") |> List.length)
        Assert.DoesNotContain(Action.Ankan(0, tilesOf "1p 1p 1p 1p"), actionsOf 0 drawn)

        Assert.Equal<IllegalAction>(
            CannotKan(0, NakiKind.Ankan, tilesOf "1p 1p 1p 1p"),
            rejected drawn (Action.Ankan(0, tilesOf "1p 1p 1p 1p"))
        )

    // ---- 杠成立之后：补摸与新宝牌的时机 ----

    [<Fact>]
    let ``暗杠：先翻新宝牌再补摸岭上牌，副露是暗的、不破门清`` () =
        let state = startScriptedRinshan "5z" ankanScript
        let before = GameState.wall state |> Wall.doraIndicators
        let kanned, events = stepped state (Action.Ankan(0, tilesOf "9s 9s 9s 9s"))
        let after = GameState.wall kanned |> Wall.doraIndicators

        // 天凤的时机：暗杠是 `ankan` → `dora` → `tsumo`（先翻再补摸）。
        match events with
        | [ Ankan(actor, consumed); Dora marker; Tsumo(drawer, drawn) ] ->
            Assert.Equal(0, actor)
            Assert.Equal<Tile list>(tilesOf "9s 9s 9s 9s", consumed)
            Assert.Equal<Tile list>(before @ [ marker ], after)
            Assert.Equal(0, drawer)
            Assert.Equal<Tile>(pai "5z", drawn)
        | other -> failwith $"暗杠应当产出 ankan → dora → tsumo，实际是 {other}"

        match nakiOf 0 kanned with
        | [ naki ] ->
            Assert.Equal<NakiKind>(NakiKind.Ankan, Naki.kind naki)
            Assert.Equal<Tile option>(None, Naki.taken naki)
            Assert.Equal<Seat option>(None, Naki.target naki)
            // 暗杠不破门清：它是唯一「暗」的副露。
            Assert.True(Naki.isConcealed naki)
        | other -> failwith $"座位 0 应当恰有一组副露，实际是 {other}"

        // 补摸之后仍是这家出手，且摸切打的是那张岭上牌。
        Assert.Equal<Seat list>([ 0 ], waiting kanned)
        Assert.Equal<Tile option>(Some(pai "5z"), GameState.player 0 kanned |> Option.bind PlayerState.drawn)

    [<Fact>]
    let ``大明杠：先补摸岭上牌再翻新宝牌`` () =
        let state = startScriptedRinshan "1z" minkanScript
        let discarded, _ = stepped state (Action.Dahai(0, pai "5s", true))

        let kanned, events =
            stepped discarded (Action.Minkan(1, 0, pai "5s", tilesOf "5s 5s 5sr"))

        // 天凤的时机：明杠是 `daiminkan` → `tsumo` → `dora`（先补摸再翻）。
        match events with
        | [ Minkan(actor, target, taken, consumed); Tsumo(drawer, drawn); Dora _ ] ->
            Assert.Equal(1, actor)
            Assert.Equal(0, target)
            Assert.Equal<Tile>(pai "5s", taken)
            Assert.Equal<Tile list>(tilesOf "5s 5s 5sr", consumed)
            Assert.Equal(1, drawer)
            Assert.Equal<Tile>(pai "1z", drawn)
        | other -> failwith $"大明杠应当产出 daiminkan → tsumo → dora，实际是 {other}"

        Assert.Equal<NakiKind list>([ NakiKind.Minkan ], nakiOf 1 kanned |> List.map Naki.kind)
        Assert.Equal<Seat list>([ 1 ], waiting kanned)

    [<Fact>]
    let ``加杠：原来那组碰**原地**升成杠，副露数不变`` () =
        let state = startScripted kakanScript
        let discarded, _ = stepped state (Action.Dahai(0, pai "5s", true))
        let ponned, _ = stepped discarded (Action.Pon(1, 0, pai "5s", tilesOf "5s 5sr"))
        let ponned, _ = stepped ponned (Action.None 2)
        let discarded, _ = stepped ponned (Action.Dahai(1, pai "1z", false))
        let drawn = driveUntil passive (fun state -> junmeOf 1 state = 1) discarded

        let declared, declaring =
            stepped drawn (Action.Kakan(1, pai "5s", tilesOf "5s 5s 5sr"))

        // 加杠先播出去，再问抢杠——座位 2 此刻正等着这张 5s。
        Assert.Equal<Event list>([ Kakan(1, pai "5s", tilesOf "5s 5s 5sr") ], declaring)
        Assert.Equal<Seat list>([ 2 ], waiting declared)

        let kanned, events = stepped declared (Action.None 2)

        match events with
        | [ Tsumo(1, _); Dora _ ] -> ()
        | other -> failwith $"没人抢杠时应当补摸并翻新宝牌，实际是 {other}"

        match nakiOf 1 kanned with
        | [ naki ] ->
            Assert.Equal<NakiKind>(NakiKind.Kakan, Naki.kind naki)
            // 加上去的那张是 `taken`，来自他家河的那一张仍是当初被碰的那张。
            Assert.Equal<Tile option>(Some(pai "5s"), Naki.taken naki)
            Assert.Equal<Tile option>(Some(pai "5s"), Naki.fromKawa naki)
            Assert.Equal<Seat option>(Some 0, Naki.target naki)
        | other -> failwith $"座位 1 应当仍只有一组副露，实际是 {other}"

        Assert.Equal(1, GameState.kanCount kanned)

    [<Fact>]
    let ``一局里连着三个杠：可摸区每杠少一张，王牌不变，指示牌每杠多一张`` () =
        let state = startScriptedRinshan "2m 3m 7z" threeKanScript
        let opening = Wall.remaining (GameState.wall state)

        let after (state: GameState) (consumed: string) =
            fst (stepped state (Action.Ankan(0, tilesOf consumed)))

        let three = after (after (after state "1m 1m 1m 1m") "2m 2m 2m 2m") "3m 3m 3m 3m"

        Assert.Equal(3, GameState.kanCount three)

        Assert.Equal(
            3,
            GameState.player 0 three
            |> Option.map PlayerState.kanCount
            |> Option.defaultValue 0
        )

        // 每杠：可摸区少一张（最后一张补进王牌），王牌恒是 `DeadWallSize` 张，多翻一张指示牌。
        Assert.Equal(opening - 3, Wall.remaining (GameState.wall three))
        Assert.Equal(ruleset.DeadWallSize, GameState.wall three |> Wall.deadWall |> List.length)
        Assert.Equal(4, GameState.wall three |> Wall.doraIndicators |> List.length)
        Assert.Equal(4, GameState.wall three |> Wall.uraIndicators |> List.length)

        // 第四个杠还杠得起（四杠散了是 12 票的事，本票只守王牌的物理上限）。
        Assert.True(GameState.kanCount three < ruleset.RinshanCount)

    [<Fact>]
    let ``岭上牌只有规则集给的那么多：杠满了就不再进动作集`` () =
        // `RinshanCount = 1` 的规则集下一局只杠得了一次，第二个杠不再出现在动作集里。
        let onlyOne = { ruleset with RinshanCount = 1 }

        let state =
            startScriptedRinshanIn onlyOne "2m 3m 7z" threeKanScript
            |> fun state -> fst (stepped state (Action.Ankan(0, tilesOf "1m 1m 1m 1m")))

        Assert.Equal(1, GameState.kanCount state)
        Assert.Equal(4, handOf 0 state |> List.filter (fun tile -> tile = pai "2m") |> List.length)
        Assert.DoesNotContain(Action.Ankan(0, tilesOf "2m 2m 2m 2m"), actionsOf 0 state)

    // ---- 岭上开花 ----

    [<Fact>]
    let ``岭上开花：补摸的那张和了，岭上标志只对这一摸生效`` () =
        let state = startScriptedRinshan "5z" ankanScript
        let kanned, _ = stepped state (Action.Ankan(0, tilesOf "9s 9s 9s 9s"))

        Assert.True((GameState.flags kanned).Rinshan)
        Assert.Contains(Yaku.Rinshan, yakuOf 0 kanned)

        // 暗杠不破门清：门前清自摸和照样成立。
        Assert.Contains(Yaku.MenzenTsumo, yakuOf 0 kanned)

        let ended, _ = stepped kanned (Action.Hora(0, 0, pai "5z"))
        let hora = theHora ended

        Assert.Equal(0, hora.Actor)
        Assert.Equal(0, hora.Target)
        Assert.True(hora.HoraPoints > 0)

    // ---- 抢杠 ----

    [<Fact>]
    let ``抢杠：荣和优先于杠的完成，那个杠没有发生`` () =
        let state = startScripted kakanScript
        let discarded, _ = stepped state (Action.Dahai(0, pai "5s", true))
        let ponned, _ = stepped discarded (Action.Pon(1, 0, pai "5s", tilesOf "5s 5sr"))
        // 座位 2 见逃了这张 5s：只是同巡振听，摸过一张就解除。
        let ponned, _ = stepped ponned (Action.None 2)
        let discarded, _ = stepped ponned (Action.Dahai(1, pai "1z", false))
        let drawn = driveUntil passive (fun state -> junmeOf 1 state = 1) discarded
        let declared, _ = stepped drawn (Action.Kakan(1, pai "5s", tilesOf "5s 5s 5sr"))

        // 抢杠这一轮只有荣和与「过」：杠上鸣不了牌。
        Assert.Equal<Action list>([ Action.Hora(2, 1, pai "5s"); Action.None 2 ], actionsOf 2 declared)
        Assert.Contains(Yaku.Chankan, yakuOf 2 declared)

        let ended, events = stepped declared (Action.Hora(2, 1, pai "5s"))
        let hora = theHora ended

        Assert.Equal<Event list>([ Event.Hora hora ], events)
        Assert.Equal(2, hora.Actor)
        // 荣和的对象是宣言杠的那家。
        Assert.Equal(1, hora.Target)
        Assert.Equal<Tile>(pai "5s", hora.Pai)

        // **那个杠没有发生**：座位 1 手里那组仍是碰，全局杠数仍是 0。
        Assert.Equal<NakiKind list>([ NakiKind.Pon ], nakiOf 1 ended |> List.map Naki.kind)
        Assert.Equal(0, GameState.kanCount ended)

    [<Fact>]
    let ``国士抢暗杠：默认（天凤）禁止，规则集打开（雀魂）才成立`` () =
        let toTheAnkan (state: GameState) =
            let discarded, _ = stepped state (Action.Dahai(0, pai "3z", true))
            discarded

        // 天凤：暗杠抢不得，因此杠当场成立，座位 2 连问都不被问。
        let tenhou = toTheAnkan (startScripted kokushiChankanScript)
        let kanned, events = stepped tenhou (Action.Ankan(1, tilesOf "7z 7z 7z 7z"))

        match events with
        | [ Ankan(1, _); Dora _; Tsumo(1, _) ] -> ()
        | other -> failwith $"天凤规则集下暗杠应当当场成立，实际是 {other}"

        Assert.Equal<Seat list>([ 1 ], waiting kanned)
        Assert.Equal(1, GameState.kanCount kanned)

        // 雀魂：国士抢得了暗杠。
        let soul = Ruleset.withKokushiAnkanChankan ruleset

        let declared, _ =
            stepped (toTheAnkan (startScriptedWith soul kokushiChankanScript)) (Action.Ankan(1, tilesOf "7z 7z 7z 7z"))

        Assert.Equal<Action list>([ Action.Hora(2, 1, pai "7z"); Action.None 2 ], actionsOf 2 declared)

        let ended, _ = stepped declared (Action.Hora(2, 1, pai "7z"))
        let hora = theHora ended

        Assert.Equal(2, hora.Actor)
        Assert.Equal(1, hora.Target)
        Assert.Equal(0, GameState.kanCount ended)
        // 役满：国士无双十三面待以外的国士按单倍役满计（13 番）。
        Assert.True(hora.Fan >= 13)

    // ---- 责任支付（Sekinin Barai） ----

    [<Fact>]
    let ``大明杠后的岭上开花：喂杠的那家一家付光`` () =
        let state = startScriptedRinshan "1z" minkanScript
        let discarded, _ = stepped state (Action.Dahai(0, pai "5s", true))

        let kanned, _ =
            stepped discarded (Action.Minkan(1, 0, pai "5s", tilesOf "5s 5s 5sr"))

        Assert.Contains(Yaku.Rinshan, yakuOf 1 kanned)

        let ended, _ = stepped kanned (Action.Hora(1, 1, pai "1z"))
        let hora = theHora ended

        // 自摸，但**三家平摊变成喂杠那家一家付**：这就是大明杠的责任支付。
        Assert.Equal(1, hora.Actor)
        Assert.Equal(1, hora.Target)
        Assert.Equal<int list>([ -hora.HoraPoints; hora.HoraPoints; 0; 0 ], hora.Deltas)
        Assert.Equal<int list>(hora.Scores, GameState.scores ended)

    [<Fact>]
    let ``大三元由副露凑齐：点出第三组的那家一家付光`` () =
        let state = startScripted daisangenScript

        let call (state: GameState) (discard: Action) (naki: Action) =
            let discarded, _ = stepped state discard
            fst (stepped discarded naki)

        let ponned =
            call state (Action.Dahai(0, pai "5z", true)) (Action.Pon(1, 0, pai "5z", tilesOf "5z 5z"))

        let ponned =
            call
                (fst (stepped ponned (Action.Dahai(1, pai "1z", false))))
                (Action.Dahai(2, pai "6z", true))
                (Action.Pon(1, 2, pai "6z", tilesOf "6z 6z"))

        // 第三组（中）是座位 2 点出来的：包就落在它头上。
        let ponned =
            call
                (fst (stepped ponned (Action.Dahai(1, pai "2z", false))))
                (Action.Dahai(2, pai "7z", true))
                (Action.Pon(1, 2, pai "7z", tilesOf "7z 7z"))

        let discarded, _ = stepped ponned (Action.Dahai(1, pai "3z", false))
        let drawn = driveUntil passive (fun state -> junmeOf 1 state = 1) discarded

        Assert.Contains(Yaku.Daisangen, yakuOf 1 drawn)

        let ended, _ = stepped drawn (Action.Hora(1, 1, pai "9p"))
        let hora = theHora ended

        // 子的役满自摸本该是「亲 16000 + 两子各 8000」，包之后全由座位 2 付。
        Assert.Equal<int list>([ 0; 32000; -32000; 0 ], hora.Deltas)
        Assert.Equal(32000, hora.HoraPoints)

    // ---- 撞车时谁赢：Ron > Pon / Minkan > Chi ----

    [<Fact>]
    let ``大明杠压过吃，荣和压过大明杠`` () =
        let state = startScripted kanRaceScript
        let discarded, _ = stepped state (Action.Dahai(0, pai "5s", true))

        let answered = fst (stepped discarded (Action.Chi(1, 0, pai "5s", tilesOf "6s 7s")))

        let answered =
            fst (stepped answered (Action.Minkan(2, 0, pai "5s", tilesOf "5s 5s 5sr")))

        let kanned, events = stepped answered (Action.None 3)

        // 大明杠与碰同级，都压过吃：吃的那家什么也没发生。
        match events with
        | Minkan(2, 0, _, _) :: _ -> ()
        | other -> failwith $"大明杠应当压过吃，实际是 {other}"

        Assert.Empty(nakiOf 1 kanned)
        Assert.Equal(1, GameState.kanCount kanned)

        // 换成座位 3 宣言荣和：Ron 压过大明杠，那个杠同样没有发生。
        let answered = fst (stepped discarded (Action.Chi(1, 0, pai "5s", tilesOf "6s 7s")))

        let answered =
            fst (stepped answered (Action.Minkan(2, 0, pai "5s", tilesOf "5s 5s 5sr")))

        let ended, _ = stepped answered (Action.Hora(3, 0, pai "5s"))

        Assert.Equal<Seat list>([ 3 ], GameState.horas ended |> List.map (fun hora -> hora.Actor))
        Assert.Equal(0, GameState.kanCount ended)
        Assert.Empty(nakiOf 2 ended)

    // ---- 属性的覆盖率证据 ----

    /// KanProperties 的局面取自两类轨迹：摊好的杠剧本，以及「见杠就杠」选手的随机对局。
    /// **没断言过覆盖率的属性只证明了没崩，没证明跑到过**（备注 N-8），
    /// 因此这里把「那些轨迹里真的有杠」钉成一条用例。
    [<Fact>]
    let ``属性取样的轨迹里真的有杠`` () =
        let scripted =
            [
                kanTrace "2m 3m 7z" threeKanScript
                kanTrace "1z" minkanScript
                kanTrace "5z" ankanScript
            ]
            |> List.map (fun trace -> trace |> List.map GameState.kanCount |> List.max)

        // 摊好的那几局必然杠得成（一局三个，另两局各一个）。
        Assert.Equal<int list>([ 3; 1; 1 ], scripted)

        // 随机对局里杠很稀：四个种子里杠得成的不多，但不能一局都没有。
        let random =
            [ 1; 42; 777; 31337 ]
            |> List.map (fun seed -> trace kanSeeking seed |> List.last |> GameState.kanCount)

        Assert.True(List.sum random > 0, $"见杠就杠的选手一局都没杠成：{random}")

    // ---- 拒绝的理由 ----

    [<Fact>]
    let ``杠不成时的拒绝理由有中文说明`` () =
        Assert.Equal(
            "座位 0 此刻用 1筒1筒1筒1筒 暗杠不了",
            IllegalAction.toDisplay (CannotKan(0, NakiKind.Ankan, tilesOf "1p 1p 1p 1p"))
        )

        Assert.Equal(
            "座位 1 用 5索5索赤5索 大明杠不了 5索",
            IllegalAction.toDisplay (CannotNaki(1, NakiKind.Minkan, pai "5s", tilesOf "5s 5s 5sr"))
        )

    [<Fact>]
    let ``手里没有那四张就暗杠不了，没碰过那种牌就加杠不了`` () =
        let state = startScriptedRinshan "5z" ankanScript

        Assert.Equal<IllegalAction>(
            CannotKan(0, NakiKind.Ankan, tilesOf "1m 1m 1m 1m"),
            rejected state (Action.Ankan(0, tilesOf "1m 1m 1m 1m"))
        )

        Assert.Equal<IllegalAction>(
            CannotKan(0, NakiKind.Kakan, tilesOf "9s 9s 9s"),
            rejected state (Action.Kakan(0, pai "9s", tilesOf "9s 9s 9s"))
        )
