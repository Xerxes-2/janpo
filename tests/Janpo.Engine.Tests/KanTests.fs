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

    /// 某座位此刻和了的话选中的那个读法。
    let private readingOf (seat: Seat) (state: GameState) : HoraReading =
        match GameState.horaOf seat state with
        | Ok reading -> reading
        | Error error -> failwith $"座位 {seat} 此刻应当和得了，却得到 {YakuError.toDisplay error}"

    /// 某座位此刻和了的话成立哪些役。
    let private yakuOf (seat: Seat) (state: GameState) : Yaku list =
        (readingOf seat state).Tally |> YakuTally.yaku

    /// 某座位此刻和了的话算得到几番表宝牌。翻牌时机的用例读它：
    /// 宝牌指示牌多一张少一张，当下这一手的番数当场就不同。
    let private doraCountAt (seat: Seat) (state: GameState) : int = (readingOf seat state).Tally.Dora

    let private theHora (state: GameState) : Hora =
        match GameState.horas state with
        | [ hora ] -> hora
        | other -> failwith $"这一局应当恰有一次和了，实际是 {List.length other} 次"

    // ---- 合法动作集 ----

    [<Fact>]
    let ``暗杠进摸牌后阶段的动作集，亮出的是手里那四张`` () =
        let state = startScriptedRinshan "5z" ankanScript

        Assert.Contains(Action.Ankan(seat 0, tilesOf "9s 9s 9s 9s"), actionsOf (seat 0) state)

        // 杠不是响应：他家打牌之前谈不上大明杠，摸牌后阶段也没有「刚打出的那张」。
        Assert.Equal<IllegalAction>(
            NothingToRespond(seat 0),
            rejected state (Action.Minkan(seat 0, seat 1, pai "9s", tilesOf "9s 9s 9s"))
        )

    [<Fact>]
    let ``加杠进摸牌后阶段的动作集，consumed 是底下那组碰的三张`` () =
        let state = startScripted kakanScript
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5s", true))

        let ponned, _ =
            stepped discarded (Action.Pon(seat 1, seat 0, pai "5s", tilesOf "5s 5sr"))

        let ponned, _ = stepped ponned (Action.None(seat 2))

        // 刚碰完那一手没摸牌，因此加不了杠（真实牌谱里每条 kakan 都紧跟在自家的 tsumo 之后）。
        Assert.DoesNotContain(Action.Kakan(seat 1, pai "5s", tilesOf "5s 5s 5sr"), actionsOf (seat 1) ponned)

        let discarded, _ = stepped ponned (Action.Dahai(seat 1, pai "1z", false))
        let drawn = driveUntil passive (fun state -> junmeOf (seat 1) state = 1) discarded

        Assert.Contains(Action.Kakan(seat 1, pai "5s", tilesOf "5s 5s 5sr"), actionsOf (seat 1) drawn)

    [<Fact>]
    let ``大明杠与碰并列进响应阶段的动作集，排在吃前面`` () =
        let state = startScripted kanRaceScript
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5s", true))

        // 座位 1 只吃得了、座位 2 碰得了也杠得了、座位 3 荣和得了。
        Assert.Equal<Seat list>(seats [ 1; 2; 3 ], waiting discarded)

        // 碰有两种亮法（红 5 亮不亮），大明杠只有一种（三张全上），且排在碰之后吃之前。
        Assert.Equal<Action list>(
            [
                Action.Pon(seat 2, seat 0, pai "5s", tilesOf "5s 5s")
                Action.Pon(seat 2, seat 0, pai "5s", tilesOf "5s 5sr")
                Action.Minkan(seat 2, seat 0, pai "5s", tilesOf "5s 5s 5sr")
                Action.None(seat 2)
            ],
            actionsOf (seat 2) discarded
        )

    [<Fact>]
    let ``立直后暗杠：判据过得了就进动作集，一发被自己这一杠打掉`` () =
        let state = startScripted riichiAnkanScript
        let declared, _ = stepped state (Action.Riichi(seat 0))
        let discarded, _ = stepped declared (Action.Dahai(seat 0, pai "1z", false))
        let drawn = driveUntil passive (fun state -> junmeOf (seat 0) state = 2) discarded

        // 立直成立之后一发亮着，这一手只剩自摸和、暗杠与摸切。
        Assert.True(ippatsuOf (seat 0) drawn)
        Assert.Contains(Action.Ankan(seat 0, tilesOf "5z 5z 5z 5z"), actionsOf (seat 0) drawn)

        let kanned, _ = stepped drawn (Action.Ankan(seat 0, tilesOf "5z 5z 5z 5z"))

        // 任何鸣牌都打断全场一发，**自己的暗杠也算**。
        Assert.False(ippatsuOf (seat 0) kanned)

    [<Fact>]
    let ``禁送り杠：立直时就握着的那四张，之后摸进别的牌也杠不得`` () =
        let state = startScripted okuriKanScript
        let declared, _ = stepped state (Action.Riichi(seat 0))
        let discarded, _ = stepped declared (Action.Dahai(seat 0, pai "3z", true))
        let drawn = driveUntil passive (fun state -> junmeOf (seat 0) state = 2) discarded

        // 手里确实有四张 1p，但刚摸进的是 5z：杠了就是送り杠，判据（09 的 `allowsAnkan`）不许。
        Assert.Equal(
            4,
            handOf (seat 0) drawn
            |> List.filter (fun tile -> tile = pai "1p")
            |> List.length
        )

        Assert.DoesNotContain(Action.Ankan(seat 0, tilesOf "1p 1p 1p 1p"), actionsOf (seat 0) drawn)

        Assert.Equal<IllegalAction>(
            CannotKan(seat 0, NakiKind.Ankan, tilesOf "1p 1p 1p 1p"),
            rejected drawn (Action.Ankan(seat 0, tilesOf "1p 1p 1p 1p"))
        )

    // ---- 杠成立之后：补摸与新宝牌的时机 ----

    [<Fact>]
    let ``暗杠：先翻新宝牌再补摸岭上牌，副露是暗的、不破门清`` () =
        let state = startScriptedRinshan "5z" ankanScript
        let before = GameState.wall state |> Wall.doraIndicators
        let kanned, events = stepped state (Action.Ankan(seat 0, tilesOf "9s 9s 9s 9s"))
        let after = GameState.wall kanned |> Wall.doraIndicators

        // 天凤的时机：暗杠是 `ankan` → `dora` → `tsumo`（先翻再补摸）。
        match events with
        | [ Ankan(actor, consumed); Dora marker; Tsumo(drawer, drawn) ] ->
            Assert.Equal(seat 0, actor)
            Assert.Equal<Tile list>(tilesOf "9s 9s 9s 9s", consumed)
            Assert.Equal<Tile list>(before @ [ marker ], after)
            Assert.Equal(seat 0, drawer)
            Assert.Equal<Tile>(pai "5z", drawn)
        | other -> failwith $"暗杠应当产出 ankan → dora → tsumo，实际是 {other}"

        match nakiOf (seat 0) kanned with
        | [ naki ] ->
            Assert.Equal<NakiKind>(NakiKind.Ankan, Naki.kind naki)
            Assert.Equal<Tile option>(None, Naki.taken naki)
            Assert.Equal<Seat option>(None, Naki.target naki)
            // 暗杠不破门清：它是唯一「暗」的副露。
            Assert.True(Naki.isConcealed naki)
        | other -> failwith $"座位 0 应当恰有一组副露，实际是 {other}"

        // 补摸之后仍是这家出手，且摸切打的是那张岭上牌。
        Assert.Equal<Seat list>(seats [ 0 ], waiting kanned)
        Assert.Equal<Tile option>(Some(pai "5z"), GameState.player (seat 0) kanned |> Option.bind PlayerState.drawn)

    [<Fact>]
    let ``大明杠：先补摸岭上牌，新宝牌欠到打牌那一刻才翻`` () =
        let state = startScriptedRinshan "1z" minkanScript
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5s", true))
        let before = GameState.wall discarded |> Wall.doraIndicators

        let kanned, events =
            stepped discarded (Action.Minkan(seat 1, seat 0, pai "5s", tilesOf "5s 5s 5sr"))

        // 天凤的时机：明杠这一步只有 `daiminkan` → `tsumo`，新宝牌**欠着**。
        match events with
        | [ Minkan(actor, target, taken, consumed); Tsumo(drawer, drawn) ] ->
            Assert.Equal(seat 1, actor)
            Assert.Equal(seat 0, target)
            Assert.Equal<Tile>(pai "5s", taken)
            Assert.Equal<Tile list>(tilesOf "5s 5s 5sr", consumed)
            Assert.Equal(seat 1, drawer)
            Assert.Equal<Tile>(pai "1z", drawn)
        | other -> failwith $"大明杠应当只产出 daiminkan → tsumo（新宝牌欠着），实际是 {other}"

        Assert.Equal<Tile list>(before, GameState.wall kanned |> Wall.doraIndicators)
        Assert.Equal<NakiKind list>([ NakiKind.Minkan ], nakiOf (seat 1) kanned |> List.map Naki.kind)
        Assert.Equal<Seat list>(seats [ 1 ], waiting kanned)

        // 杠的那家打牌那一刻才翻，且 `dora` 仍排在 `dahai` 之前——**整条事件流的顺序不变**，
        // 变的只是哪一次 `step` 吐出它。
        let played, playing = stepped kanned (Action.Dahai(seat 1, pai "1z", true))

        match playing with
        | Dora marker :: Dahai(actor, _, _) :: _ ->
            Assert.Equal(seat 1, actor)
            Assert.Equal<Tile list>(before @ [ marker ], GameState.wall played |> Wall.doraIndicators)
        | other -> failwith $"打牌那一步应当先翻新宝牌再打出去，实际是 {other}"

    [<Fact>]
    let ``加杠后当场岭上开花：新宝牌一张不翻，那一手吃不到它`` () =
        // 13 票在 218 局真实牌谱上实证的那一条：加杠后当场岭上开花的局，
        // 牌谱里根本没有 `dora` 事件（没有那一次打牌，就没有那一张）。
        let state = startScriptedRinshan kakanRinshan kakanRinshanScript
        let opening = GameState.wall state |> Wall.doraIndicators
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5s", true))

        let ponned, _ =
            stepped discarded (Action.Pon(seat 1, seat 0, pai "5s", tilesOf "5s 5sr"))

        let discarded, _ = stepped ponned (Action.Dahai(seat 1, pai "1z", false))
        let drawn = driveUntil passive (fun state -> junmeOf (seat 1) state = 1) discarded

        // 没人抢得了这个杠：它当场成立，而这一步只有 `kakan` → `tsumo`。
        let kanned, events =
            stepped drawn (Action.Kakan(seat 1, pai "5s", tilesOf "5s 5s 5sr"))

        match events with
        | [ Kakan(actor, _, _); Tsumo(drawer, drawn) ] ->
            Assert.Equal(seat 1, actor)
            Assert.Equal(seat 1, drawer)
            Assert.Equal<Tile>(pai "1z", drawn)
        | other -> failwith $"加杠应当只产出 kakan → tsumo（新宝牌欠着），实际是 {other}"

        Assert.Equal<Tile list>(opening, GameState.wall kanned |> Wall.doraIndicators)

        // 岭上开花那一手只看得到开局那一张指示牌（`2z` → 宝牌 3z，它一张没有）。
        // 欠着的那张是 `4s` → 宝牌 5s，而它刚杠了四张 5s：多翻一张就多四番。
        Assert.Contains(Yaku.Rinshan, yakuOf (seat 1) kanned)
        Assert.Equal(0, doraCountAt (seat 1) kanned)

        let ended, _ = stepped kanned (Action.Hora(seat 1, seat 1, pai "1z"))
        let hora = theHora ended

        // 岭上开花 1 番 + 一气通贯（副露降一番）1 番 + 赤宝牌 1 番 = **40符3飕 5200 点**。
        // 多翻那一张就是 7 番的跳满 12000 点——真牌谱里 2700 算成 8000 是同一个错。
        Assert.Equal(3, hora.Fan)
        Assert.Equal(40, hora.Fu)
        Assert.Equal(5200, hora.HoraPoints)
        Assert.Equal<int list>([ -2600; 5200; -1300; -1300 ], hora.Deltas)
        Assert.Equal(1, GameState.wall ended |> Wall.doraIndicators |> List.length)
        Assert.Equal(1, GameState.kanCount ended)

    [<Fact>]
    let ``加杠：原来那组碰**原地**升成杠，副露数不变`` () =
        let state = startScripted kakanScript
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5s", true))

        let ponned, _ =
            stepped discarded (Action.Pon(seat 1, seat 0, pai "5s", tilesOf "5s 5sr"))

        let ponned, _ = stepped ponned (Action.None(seat 2))
        let discarded, _ = stepped ponned (Action.Dahai(seat 1, pai "1z", false))
        let drawn = driveUntil passive (fun state -> junmeOf (seat 1) state = 1) discarded

        let declared, declaring =
            stepped drawn (Action.Kakan(seat 1, pai "5s", tilesOf "5s 5s 5sr"))

        // 加杠先播出去，再问抢杠——座位 2 此刻正等着这张 5s。
        Assert.Equal<Event list>([ Kakan(seat 1, pai "5s", tilesOf "5s 5s 5sr") ], declaring)
        Assert.Equal<Seat list>(seats [ 2 ], waiting declared)

        let kanned, events = stepped declared (Action.None(seat 2))

        // 没人抢杠 ⇒ 那个杠成立，这一步只补摸：新宝牌欠到它打牌那一刻（见上一条用例）。
        match events with
        | [ Tsumo(actor, _) ] when actor = seat 1 -> ()
        | other -> failwith $"没人抢杠时应当补摸岭上牌（新宝牌欠着），实际是 {other}"

        match nakiOf (seat 1) kanned with
        | [ naki ] ->
            Assert.Equal<NakiKind>(NakiKind.Kakan, Naki.kind naki)
            // 加上去的那张是 `taken`，来自他家河的那一张仍是当初被碰的那张。
            Assert.Equal<Tile option>(Some(pai "5s"), Naki.taken naki)
            Assert.Equal<Tile option>(Some(pai "5s"), Naki.fromKawa naki)
            Assert.Equal<Seat option>(Some(seat 0), Naki.target naki)
        | other -> failwith $"座位 1 应当仍只有一组副露，实际是 {other}"

        Assert.Equal(1, GameState.kanCount kanned)

    [<Fact>]
    let ``大明杠之后没打牌就又暗杠：暗杠那张当场翻，大明杠那张仍欠到打牌`` () =
        // 两种时机同时在场的唯一形态。**两批语料 2110 局里一次都没出现过**（牌谱作证不了它），
        // 因此取最保守的一种：按杠的种类各算各的，欠账累加、打牌那一刻一次翻光（DECISIONS 16-B）。
        let state = startScriptedRinshan "9m 7m" minkanAnkanScript
        let opening = GameState.wall state |> Wall.doraIndicators
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5s", true))

        let kanned, _ =
            stepped discarded (Action.Minkan(seat 1, seat 0, pai "5s", tilesOf "5s 5s 5sr"))

        // 大明杠那一张欠着；补摸的岭上牌是第四张 9m，它接着暗杠。
        Assert.Equal<Tile list>(opening, GameState.wall kanned |> Wall.doraIndicators)

        let twice, events = stepped kanned (Action.Ankan(seat 1, tilesOf "9m 9m 9m 9m"))

        match events with
        | [ Ankan(actor, _); Dora _; Tsumo(drawer, drawn) ] ->
            Assert.Equal(seat 1, actor)
            Assert.Equal(seat 1, drawer)
            Assert.Equal<Tile>(pai "7m", drawn)
        | other -> failwith $"暗杠应当当场翻新宝牌，实际是 {other}"

        // 暗杠翻了它自己那张，大明杠那张仍欠着：两个杠而此刻只翻开两张（开局 1 + 暗杠 1）。
        Assert.Equal(2, GameState.kanCount twice)
        Assert.Equal(2, GameState.wall twice |> Wall.doraIndicators |> List.length)

        // 打牌那一刻把欠的那张补翻出来：一个杠仍旧对应一张指示牌，一张不多一张不少。
        let played, playing = stepped twice (Action.Dahai(seat 1, pai "7m", true))

        match playing with
        | Dora _ :: Dahai(actor, _, _) :: _ -> Assert.Equal(seat 1, actor)
        | other -> failwith $"打牌那一步应当补翻欠着的那张，实际是 {other}"

        Assert.Equal(3, GameState.wall played |> Wall.doraIndicators |> List.length)
        Assert.Equal(3, GameState.wall played |> Wall.uraIndicators |> List.length)

    [<Fact>]
    let ``一局里连着三个杠：可摸区每杠少一张，王牌不变，指示牌每杠多一张`` () =
        let state = startScriptedRinshan "2m 3m 7z" threeKanScript
        let opening = Wall.remaining (GameState.wall state)

        let after (state: GameState) (consumed: string) =
            fst (stepped state (Action.Ankan(seat 0, tilesOf consumed)))

        let three = after (after (after state "1m 1m 1m 1m") "2m 2m 2m 2m") "3m 3m 3m 3m"

        Assert.Equal(3, GameState.kanCount three)

        // 三个都是暗杠：三张新宝牌当场就翻完了，一张也没欠着。
        Assert.Equal(4, GameState.wall three |> Wall.doraIndicators |> List.length)

        Assert.Equal(
            3,
            GameState.player (seat 0) three
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
            |> fun state -> fst (stepped state (Action.Ankan(seat 0, tilesOf "1m 1m 1m 1m")))

        Assert.Equal(1, GameState.kanCount state)

        Assert.Equal(
            4,
            handOf (seat 0) state
            |> List.filter (fun tile -> tile = pai "2m")
            |> List.length
        )

        Assert.DoesNotContain(Action.Ankan(seat 0, tilesOf "2m 2m 2m 2m"), actionsOf (seat 0) state)

    // ---- 岭上开花 ----

    [<Fact>]
    let ``岭上开花：补摸的那张和了，岭上标志只对这一摸生效`` () =
        let state = startScriptedRinshan "5z" ankanScript
        let kanned, _ = stepped state (Action.Ankan(seat 0, tilesOf "9s 9s 9s 9s"))

        Assert.True((GameState.flags kanned).Rinshan)

        // **暗杠是当场翻**：岭上开花那一手照样吃得到这张新宝牌。
        // （明杠反过来：没有那一次打牌就一张不翻，见「加杠后当场岭上开花」那一条。）
        Assert.Equal(2, GameState.wall kanned |> Wall.doraIndicators |> List.length)
        Assert.Contains(Yaku.Rinshan, yakuOf (seat 0) kanned)

        // 暗杠不破门清：门前清自摸和照样成立。
        Assert.Contains(Yaku.MenzenTsumo, yakuOf (seat 0) kanned)

        let ended, _ = stepped kanned (Action.Hora(seat 0, seat 0, pai "5z"))
        let hora = theHora ended

        Assert.Equal(seat 0, hora.Actor)
        Assert.Equal(seat 0, hora.Target)
        Assert.True(hora.HoraPoints > 0)

    // ---- 抢杠 ----

    [<Fact>]
    let ``抢杠：荣和优先于杠的完成，那个杠没有发生`` () =
        let state = startScripted kakanScript
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5s", true))

        let ponned, _ =
            stepped discarded (Action.Pon(seat 1, seat 0, pai "5s", tilesOf "5s 5sr"))
        // 座位 2 见逃了这张 5s：只是同巡振听，摸过一张就解除。
        let ponned, _ = stepped ponned (Action.None(seat 2))
        let discarded, _ = stepped ponned (Action.Dahai(seat 1, pai "1z", false))
        let drawn = driveUntil passive (fun state -> junmeOf (seat 1) state = 1) discarded

        let declared, _ =
            stepped drawn (Action.Kakan(seat 1, pai "5s", tilesOf "5s 5s 5sr"))

        // 抢杠这一轮只有荣和与「过」：杠上鸣不了牌。
        Assert.Equal<Action list>(
            [ Action.Hora(seat 2, seat 1, pai "5s"); Action.None(seat 2) ],
            actionsOf (seat 2) declared
        )

        Assert.Contains(Yaku.Chankan, yakuOf (seat 2) declared)

        let ended, events = stepped declared (Action.Hora(seat 2, seat 1, pai "5s"))
        let hora = theHora ended

        Assert.Equal<Event list>([ Event.Hora hora ], events)
        Assert.Equal(seat 2, hora.Actor)
        // 荣和的对象是宣言杠的那家。
        Assert.Equal(seat 1, hora.Target)
        Assert.Equal<Tile>(pai "5s", hora.Pai)

        // **那个杠没有发生**：座位 1 手里那组仍是碰，全局杠数仍是 0。
        Assert.Equal<NakiKind list>([ NakiKind.Pon ], nakiOf (seat 1) ended |> List.map Naki.kind)
        Assert.Equal(0, GameState.kanCount ended)
        // 杠没成立，新宝牌自然也不翻（连欠账都没记上）：指示牌仍是开局那一张。
        Assert.Equal(1, GameState.wall ended |> Wall.doraIndicators |> List.length)

    [<Fact>]
    let ``国士抢暗杠：默认（天凤）禁止，规则集打开（雀魂）才成立`` () =
        let toTheAnkan (state: GameState) =
            let discarded, _ = stepped state (Action.Dahai(seat 0, pai "3z", true))
            discarded

        // 天凤：暗杠抢不得，因此杠当场成立，座位 2 连问都不被问。
        let tenhou = toTheAnkan (startScripted kokushiChankanScript)
        let kanned, events = stepped tenhou (Action.Ankan(seat 1, tilesOf "7z 7z 7z 7z"))

        match events with
        | [ Ankan(kanner, _); Dora _; Tsumo(drawer, _) ] when kanner = seat 1 && drawer = seat 1 -> ()
        | other -> failwith $"天凤规则集下暗杠应当当场成立，实际是 {other}"

        Assert.Equal<Seat list>(seats [ 1 ], waiting kanned)
        Assert.Equal(1, GameState.kanCount kanned)

        // 雀魂：国士抢得了暗杠。
        let soul = Ruleset.withKokushiAnkanChankan ruleset

        let declared, _ =
            stepped
                (toTheAnkan (startScriptedWith soul kokushiChankanScript))
                (Action.Ankan(seat 1, tilesOf "7z 7z 7z 7z"))

        Assert.Equal<Action list>(
            [ Action.Hora(seat 2, seat 1, pai "7z"); Action.None(seat 2) ],
            actionsOf (seat 2) declared
        )

        let ended, _ = stepped declared (Action.Hora(seat 2, seat 1, pai "7z"))
        let hora = theHora ended

        Assert.Equal(seat 2, hora.Actor)
        Assert.Equal(seat 1, hora.Target)
        Assert.Equal(0, GameState.kanCount ended)
        // 役满：国士无双十三面待以外的国士按单倍役满计（13 番）。
        Assert.True(hora.Fan >= 13)

    // ---- 责任支付（Sekinin Barai） ----

    [<Fact>]
    let ``大明杠后的岭上开花：喂杠的那家一家付光`` () =
        let state = startScriptedRinshan "1z" minkanScript
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5s", true))

        let kanned, _ =
            stepped discarded (Action.Minkan(seat 1, seat 0, pai "5s", tilesOf "5s 5s 5sr"))

        Assert.Contains(Yaku.Rinshan, yakuOf (seat 1) kanned)
        // 大明杠的新宝牌欠着：岭上开花那一手看不到它。
        Assert.Equal(1, GameState.wall kanned |> Wall.doraIndicators |> List.length)

        let ended, _ = stepped kanned (Action.Hora(seat 1, seat 1, pai "1z"))
        let hora = theHora ended

        // 自摸，但**三家平摊变成喂杠那家一家付**：这就是大明杠的责任支付。
        Assert.Equal(seat 1, hora.Actor)
        Assert.Equal(seat 1, hora.Target)
        Assert.Equal<int list>([ -hora.HoraPoints; hora.HoraPoints; 0; 0 ], hora.Deltas)
        Assert.Equal<int list>(hora.Scores, GameState.scores ended)

    [<Fact>]
    let ``大三元由副露凑齐：点出第三组的那家一家付光`` () =
        let state = startScripted daisangenScript

        let call (state: GameState) (discard: Action) (naki: Action) =
            let discarded, _ = stepped state discard
            fst (stepped discarded naki)

        let ponned =
            call state (Action.Dahai(seat 0, pai "5z", true)) (Action.Pon(seat 1, seat 0, pai "5z", tilesOf "5z 5z"))

        let ponned =
            call
                (fst (stepped ponned (Action.Dahai(seat 1, pai "1z", false))))
                (Action.Dahai(seat 2, pai "6z", true))
                (Action.Pon(seat 1, seat 2, pai "6z", tilesOf "6z 6z"))

        // 第三组（中）是座位 2 点出来的：包就落在它头上。
        let ponned =
            call
                (fst (stepped ponned (Action.Dahai(seat 1, pai "2z", false))))
                (Action.Dahai(seat 2, pai "7z", true))
                (Action.Pon(seat 1, seat 2, pai "7z", tilesOf "7z 7z"))

        let discarded, _ = stepped ponned (Action.Dahai(seat 1, pai "3z", false))
        let drawn = driveUntil passive (fun state -> junmeOf (seat 1) state = 1) discarded

        Assert.Contains(Yaku.Daisangen, yakuOf (seat 1) drawn)

        let ended, _ = stepped drawn (Action.Hora(seat 1, seat 1, pai "9p"))
        let hora = theHora ended

        // 子的役满自摸本该是「亲 16000 + 两子各 8000」，包之后全由座位 2 付。
        Assert.Equal<int list>([ 0; 32000; -32000; 0 ], hora.Deltas)
        Assert.Equal(32000, hora.HoraPoints)

    // ---- 撞车时谁赢：Ron > Pon / Minkan > Chi ----

    [<Fact>]
    let ``大明杠压过吃，荣和压过大明杠`` () =
        let state = startScripted kanRaceScript
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5s", true))

        let answered =
            fst (stepped discarded (Action.Chi(seat 1, seat 0, pai "5s", tilesOf "6s 7s")))

        let answered =
            fst (stepped answered (Action.Minkan(seat 2, seat 0, pai "5s", tilesOf "5s 5s 5sr")))

        let kanned, events = stepped answered (Action.None(seat 3))

        // 大明杠与碰同级，都压过吃：吃的那家什么也没发生。
        match events with
        | Minkan(actor, target, _, _) :: _ when actor = seat 2 && target = seat 0 -> ()
        | other -> failwith $"大明杠应当压过吃，实际是 {other}"

        Assert.Empty(nakiOf (seat 1) kanned)
        Assert.Equal(1, GameState.kanCount kanned)

        // 换成座位 3 宣言荣和：Ron 压过大明杠，那个杠同样没有发生。
        let answered =
            fst (stepped discarded (Action.Chi(seat 1, seat 0, pai "5s", tilesOf "6s 7s")))

        let answered =
            fst (stepped answered (Action.Minkan(seat 2, seat 0, pai "5s", tilesOf "5s 5s 5sr")))

        let ended, _ = stepped answered (Action.Hora(seat 3, seat 0, pai "5s"))

        Assert.Equal<Seat list>(seats [ 3 ], GameState.horas ended |> List.map (fun hora -> hora.Actor))
        Assert.Equal(0, GameState.kanCount ended)
        Assert.Empty(nakiOf (seat 2) ended)

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
            IllegalAction.toDisplay (CannotKan(seat 0, NakiKind.Ankan, tilesOf "1p 1p 1p 1p"))
        )

        Assert.Equal(
            "座位 1 用 5索5索赤5索 大明杠不了 5索",
            IllegalAction.toDisplay (CannotNaki(seat 1, NakiKind.Minkan, pai "5s", tilesOf "5s 5s 5sr"))
        )

    [<Fact>]
    let ``手里没有那四张就暗杠不了，没碰过那种牌就加杠不了`` () =
        let state = startScriptedRinshan "5z" ankanScript

        Assert.Equal<IllegalAction>(
            CannotKan(seat 0, NakiKind.Ankan, tilesOf "1m 1m 1m 1m"),
            rejected state (Action.Ankan(seat 0, tilesOf "1m 1m 1m 1m"))
        )

        Assert.Equal<IllegalAction>(
            CannotKan(seat 0, NakiKind.Kakan, tilesOf "9s 9s 9s"),
            rejected state (Action.Kakan(seat 0, pai "9s", tilesOf "9s 9s 9s"))
        )
