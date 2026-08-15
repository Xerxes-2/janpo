namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 和了的成立路径：自摸和、荣和、振听、头跳，以及和了事件里的真实点数。
///
/// 牌山是**摊出来的**（`startScripted`），因此「指定的和了在指定 Junme 发生」是确定的事实，
/// 不必去碰运气找种子；连带的，表宝牌指示牌也是剧本定死的（每条用例写明它是哪张）。
///
/// **本文件的点数用例一律依据 `Ruleset.yonma`**（= 天凤：连风牌雀头 4 符、岭上自摸 +2 符、
/// 切上满贯关、一本场 300 点、立直棒 1000 点）；符与点数本身的边界用例在 FuTests / ScoreTests。
module HoraTests =

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

    let private furitenOf (seat: Seat) (state: GameState) : Furiten =
        match GameState.player seat state with
        | Some player -> PlayerState.furiten player
        | None -> failwith $"座位 {seat} 应当存在"

    let private isTenpai (seat: Seat) (state: GameState) : bool =
        match GameState.player seat state with
        | Some player -> PlayerState.isTenpai kindSet player
        | None -> failwith $"座位 {seat} 应当存在"

    /// 现在在等哪些座位响应。
    let private responding (state: GameState) : Seat list =
        GameState.legalActions state |> List.map (fun choice -> choice.Seat)

    let private actionsOf (seat: Seat) (state: GameState) : Action list =
        GameState.legalActions state
        |> List.tryFind (fun choice -> choice.Seat = seat)
        |> Option.map (fun choice -> choice.Actions)
        |> Option.defaultValue []

    /// 刚打出的那张牌摆在响应阶段上。
    let private inResponsePhase (state: GameState) : bool =
        match GameState.phase state with
        | AwaitingResponse _ -> true
        | AwaitingDahai _
        | Ended _ -> false

    /// 响应阶段且**有人能荣和**。鸣牌（10 票）落地之后一局里会出现一堆只能碰 / 吃的
    /// 响应阶段，本文件的黄金用例要的是其中能荣和的那一步。
    let private inRonPhase (state: GameState) : bool =
        inResponsePhase state
        && GameState.legalActions state
           |> List.exists (fun choice ->
               choice.Actions
               |> List.exists (fun action ->
                   match action with
                   | Action.Hora _ -> true
                   | Action.Dahai _
                   | Action.Pon _
                   | Action.Chi _
                   | Action.None _ -> false))

    let private horasOf (state: GameState) : Hora list = GameState.horas state

    /// 这一局翻开的表宝牌指示牌。点数用例把它写成断言：宝牌几番是剧本的**输入**，
    /// 不是算出来的结果，读用例的人不必去反推牌山。
    let private doraMarkers (state: GameState) : string list =
        GameState.wall state |> Wall.doraIndicators |> List.map Tile.toMjai

    // ---- 自摸和 ----

    [<Fact>]
    let ``摸进和了牌后 Hora 进入合法动作集，排在打牌之前`` () =
        let state =
            startScripted tsumoHoraScript
            |> driveUntil passive (fun state -> junmeOf context.Oya state = 2)

        Assert.Equal<Action list>(
            [ Action.Hora(context.Oya, context.Oya, pai "5z") ],
            List.truncate 1 (actionsOf 0 state)
        )

        // 自摸和不看振听：Oya 第 1 巡打掉的 1z 与它无关，能不能和只看和了型。
        Assert.Contains(Action.Hora(context.Oya, context.Oya, pai "5z"), actionsOf 0 state)

    [<Fact>]
    let ``指定的自摸和在指定的 Junme 发生，Oya 和了则连庄`` () =
        let state =
            startScripted tsumoHoraScript
            |> driveUntil passive (fun state -> junmeOf context.Oya state = 2)

        let ended, events = stepped state (Action.Hora(context.Oya, context.Oya, pai "5z"))

        Assert.Equal(2, junmeOf context.Oya ended)
        Assert.True(GameState.isEnded ended)
        Assert.Empty(GameState.legalActions ended)

        // 规则集：`Ruleset.yonma`。手牌 123m 456m 789m 123p + 5z，单骑自摸 5z：
        // 门前清自摸和 1 番 + 一气通贯（门清）2 番 = 3 番；表宝牌指示牌 8s（宝牌 9s）
        // 手里一张都没有，宝牌 0 番。
        // 符 = 底 20 + 雀头（白）2 + 单骑 2 + 自摸 2 = 26 → 切上 30 符。
        // 亲 30 符 3 番自摸：基本点 960，三家各付 ceil100(1920) = 2000，合计 6000。
        Assert.Equal<string list>([ "8s" ], doraMarkers ended)

        let expected =
            {
                Actor = 0
                Target = 0
                Pai = pai "5z"
                Fu = 30
                Fan = 3
                HoraPoints = 6000
                Deltas = [ 6000; -2000; -2000; -2000 ]
                Scores = [ 31000; 23000; 23000; 23000 ]
            }

        Assert.Equal<Event list>([ Hora expected ], events)
        Assert.Equal<Hora list>([ expected ], horasOf ended)
        Assert.Equal<Event list>(GameState.events state @ events, GameState.events ended)

        match GameState.kyokuEnd ended with
        | Some kyokuEnd -> Assert.True(KyokuEnd.isRenchan context.Oya kyokuEnd)
        | None -> failwith "这一局应当已经终了"

    [<Fact>]
    let ``和了收尾时没有流局结果`` () =
        let state =
            startScripted tsumoHoraScript
            |> driveUntil passive (fun state -> junmeOf context.Oya state = 2)

        let ended, _ = stepped state (Action.Hora(context.Oya, context.Oya, pai "5z"))

        Assert.Equal<Ryuukyoku option>(None, GameState.ryuukyoku ended)

    [<Fact>]
    let ``手牌不成和了型时宣言自摸和被拒`` () =
        let state = startScripted tsumoHoraScript

        Assert.Equal<IllegalAction>(
            IllegalAction.NotAgari(context.Oya, pai "1z"),
            rejected state (Action.Hora(context.Oya, context.Oya, pai "1z"))
        )

    [<Fact>]
    let ``自摸和的来源必须是自己、牌必须是刚摸进那张`` () =
        let state =
            startScripted tsumoHoraScript
            |> driveUntil passive (fun state -> junmeOf context.Oya state = 2)

        Assert.Equal<IllegalAction>(
            HoraTileMismatch(context.Oya, 2, pai "5z"),
            rejected state (Action.Hora(context.Oya, 2, pai "5z"))
        )

        Assert.Equal<IllegalAction>(
            HoraTileMismatch(context.Oya, context.Oya, pai "1m"),
            rejected state (Action.Hora(context.Oya, context.Oya, pai "1m"))
        )

    [<Fact>]
    let ``摸牌后阶段没有可响应的牌，「过」被拒`` () =
        let state = startScripted tsumoHoraScript

        Assert.Equal<IllegalAction>(NothingToRespond context.Oya, rejected state (Action.None context.Oya))

    // ---- 荣和与振听 ----

    /// 让除 `keep` 之外的待答座位一律「过」。鸣牌（10 票）落地之后，一张牌打出去常常
    /// 同时问好几家（能碰能吃的也被问），而本文件关心的只是其中能和的那一家。
    let private othersPass (keep: Seat) (state: GameState) : GameState =
        let pending = responding state |> List.filter (fun seat -> seat <> keep)

        (state, pending)
        ||> List.fold (fun state seat -> fst (stepped state (Action.None seat)))

    /// 跑到座位 0 打出 4p、等人响应的那一步（剧本见 `GameStateFixtures.ronFuritenScript`）。
    /// 座位 1 与座位 2 都被问到：座位 2 能荣和，座位 1 振听但能吃。
    let private atTheRon () =
        startScripted ronFuritenScript |> driveUntil passive inRonPhase

    /// 同一步，但能鸣牌的那几家已经「过」了：只剩座位 2 没答复，它的下一手立刻裁决。
    let private atTheRonAlone () = atTheRon () |> othersPass 2

    [<Fact>]
    let ``他家打出和了牌时 Ron 与「过」一起进入对应座位的合法动作集`` () =
        let state = atTheRon ()

        // 座位 1 也在被问之列，但那是因为它能吃 4p（见下一条用例）。
        Assert.Equal<Seat list>([ 1; 2 ], responding state)
        Assert.Equal<Action list>([ Action.Hora(2, 0, pai "4p"); Action.None 2 ], actionsOf 2 state)

    [<Fact>]
    let ``振听座位的 Ron 不出现在合法动作集里，它照样是听牌的`` () =
        let state = atTheRon ()

        // 座位 1 听 4p / 7p，但它自己打过 7p —— 永久振听。
        Assert.True(isTenpai 1 state)
        Assert.Equal<Furiten>({ Permanent = true; Doujun = false }, furitenOf 1 state)

        // **振听只挡荣和，不挡鸣牌**：座位 1 能吃 4p，因此照样被问，
        // 只是它的那份里没有 Ron。
        Assert.Contains(1, responding state)
        Assert.DoesNotContain(Action.Hora(1, 0, pai "4p"), actionsOf 1 state)
        Assert.Contains(Action.Chi(1, 0, pai "4p", tilesOf "5p 6p"), actionsOf 1 state)

        // 它硬要宣言荣和：型成立、也有役（平和），挡住它的是振听。
        Assert.Equal<IllegalAction>(RonWhileFuriten(1, pai "4p"), rejected state (Action.Hora(1, 0, pai "4p")))

        // 已经答复过的座位再提交，得到的才是「现在不轮到你」。
        Assert.Equal<IllegalAction>(NotYourTurn(1, [ 2 ]), rejected (atTheRonAlone ()) (Action.Hora(1, 0, pai "4p")))

    [<Fact>]
    let ``荣和结束这一局，Ko 和了则不连庄`` () =
        let state = atTheRonAlone ()
        let ended, events = stepped state (Action.Hora(2, 0, pai "4p"))

        // 规则集：`Ruleset.yonma`。座位 2 荣和 4p：234p 234s 678s 345m + 99m 雀头，
        // 四顺子 + 非役牌雀头 + 两面听 = 平和 1 番；表宝牌指示牌 2z（宝牌 3z）手里没有，宝牌 0 番。
        // 符 = 底 20 + 门清荣和 10 = 30 符（平和荣和就是 30 符）。
        // 子 30 符 1 番荣和：基本点 240，放铳者付 ceil100(960) = 1000。
        Assert.Equal<string list>([ "2z" ], doraMarkers ended)

        let expected =
            {
                Actor = 2
                Target = 0
                Pai = pai "4p"
                Fu = 30
                Fan = 1
                HoraPoints = 1000
                Deltas = [ -1000; 0; 1000; 0 ]
                Scores = [ 24000; 25000; 26000; 25000 ]
            }

        Assert.Equal<Event list>([ Hora expected ], events)
        Assert.Equal<Hora list>([ expected ], horasOf ended)
        Assert.True(GameState.isEnded ended)

        match GameState.kyokuEnd ended with
        | Some kyokuEnd -> Assert.False(KyokuEnd.isRenchan context.Oya kyokuEnd)
        | None -> failwith "这一局应当已经终了"

    [<Fact>]
    let ``荣和的来源必须是刚打牌那家、牌必须是刚打出那张`` () =
        let state = atTheRon ()

        Assert.Equal<IllegalAction>(HoraTileMismatch(2, 3, pai "4p"), rejected state (Action.Hora(2, 3, pai "4p")))
        Assert.Equal<IllegalAction>(HoraTileMismatch(2, 0, pai "1p"), rejected state (Action.Hora(2, 0, pai "1p")))

    [<Fact>]
    let ``见逃得到同巡振听，自己下次摸牌时解除`` () =
        let state = atTheRonAlone ()
        let passed, events = stepped state (Action.None 2)

        // 「过」自己不产出事件（mjai 的 none 是一次答复，不是既成事实）；
        // 收齐答复之后这一局接着打，这一步产出的只有下家的 tsumo。
        Assert.Equal<Event list>([ Tsumo(1, pai "1p") ], events)
        Assert.False(GameState.isEnded passed)
        Assert.Equal<Furiten>({ Permanent = false; Doujun = true }, furitenOf 2 passed)

        // 同巡振听到自己下次摸牌为止：摸完就解除，永久振听则始终没成立过。
        let afterDraw = passed |> driveUntil passive (fun state -> junmeOf 2 state = 2)

        Assert.Equal<Furiten>(Furiten.none, furitenOf 2 afterDraw)

    [<Fact>]
    let ``同巡振听期间，他家再打出和了牌也不进合法动作集`` () =
        let state = atTheRonAlone ()
        let passed, _ = stepped state (Action.None 2)

        // 座位 1 第 2 巡摸进 1p 又摸切出去 —— 那是座位 2 的另一张和了牌，
        // 但座位 2 同巡振听，因此它那份里只有吃，没有 Ron。
        let discarded, events = stepped passed (Action.Dahai(1, pai "1p", true))

        Assert.Equal<Event list>([ Dahai(1, pai "1p", true) ], events)
        Assert.DoesNotContain(Action.Hora(2, 1, pai "1p"), actionsOf 2 discarded)
        Assert.Contains(Action.Chi(2, 1, pai "1p", tilesOf "2p 3p"), actionsOf 2 discarded)

        // 全部「过」之后这一局接着打，轮座位 2 摸牌。
        let passedAgain, drawn = stepped (othersPass 2 discarded) (Action.None 2)

        Assert.Equal<Event list>([ Tsumo(2, pai "3z") ], drawn)
        Assert.False(GameState.isEnded passedAgain)

    [<Fact>]
    let ``同巡振听解除之后，再打出的和了牌又能荣和`` () =
        let state = atTheRonAlone ()
        let passed, _ = stepped state (Action.None 2)

        // 座位 2 自己摸过一巡（同巡振听解除），随后座位 3 打出 1p。
        let again = passed |> driveUntil passive inRonPhase

        Assert.Equal<Seat list>([ 2 ], responding again)
        Assert.Equal<Action list>([ Action.Hora(2, 3, pai "1p"); Action.None 2 ], actionsOf 2 again)

    // ---- 头跳与双响 ----

    /// 跑到座位 0 打出 4p、座位 2 与座位 3 都能荣和的那一步
    /// （剧本见 `GameStateFixtures.doubleRonScript`）。
    let private atTheDoubleRon (ruleset: Ruleset) =
        startScriptedWith ruleset doubleRonScript |> driveUntil passive inRonPhase

    [<Fact>]
    let ``同巡两家都能荣和时，两家都进合法动作集`` () =
        let state = atTheDoubleRon ruleset

        Assert.Equal<Seat list>([ 2; 3 ], responding state)
        Assert.Equal<Action list>([ Action.Hora(2, 0, pai "4p"); Action.None 2 ], actionsOf 2 state)
        Assert.Equal<Action list>([ Action.Hora(3, 0, pai "4p"); Action.None 3 ], actionsOf 3 state)

    [<Fact>]
    let ``头跳开着时同巡双响只成立打牌者下家优先的那一家`` () =
        Assert.True(ruleset.Atamahane)

        let state = atTheDoubleRon ruleset
        let first, events = stepped state (Action.Hora(2, 0, pai "4p"))

        // 第一家宣言之后不立刻结束：还要等另一家答复，收齐了才裁决。
        Assert.Empty(events)
        Assert.False(GameState.isEnded first)
        Assert.Equal<Seat list>([ 3 ], responding first)

        let ended, events = stepped first (Action.Hora(3, 0, pai "4p"))

        Assert.Equal(1, List.length events)
        Assert.Equal<Seat list>([ 2 ], horasOf ended |> List.map (fun hora -> hora.Actor))

    [<Fact>]
    let ``头跳关掉时同巡双响都成立，按打牌者下家优先排序`` () =
        let doubleRon = Ruleset.withoutAtamahane ruleset
        let state = atTheDoubleRon doubleRon

        let first, _ = stepped state (Action.Hora(2, 0, pai "4p"))
        let ended, events = stepped first (Action.Hora(3, 0, pai "4p"))

        Assert.Equal<Seat list>([ 2; 3 ], horasOf ended |> List.map (fun hora -> hora.Actor))
        Assert.Equal<Event list>(horasOf ended |> List.map Hora, events)
        Assert.True(GameState.isEnded ended)

    [<Fact>]
    let ``头跳关掉时同巡三响也都成立`` () =
        let doubleRon = Ruleset.withoutAtamahane ruleset

        let state =
            startScriptedWith doubleRon tripleRonScript |> driveUntil passive inRonPhase

        Assert.Equal<Seat list>([ 1; 2; 3 ], responding state)

        let first, _ = stepped state (Action.Hora(1, 0, pai "4p"))
        let second, _ = stepped first (Action.Hora(2, 0, pai "4p"))
        let ended, _ = stepped second (Action.Hora(3, 0, pai "4p"))

        Assert.Equal<Seat list>([ 1; 2; 3 ], horasOf ended |> List.map (fun hora -> hora.Actor))

    [<Fact>]
    let ``头跳开着时同巡三响只成立最靠前的一家`` () =
        let state =
            startScriptedWith ruleset tripleRonScript |> driveUntil passive inRonPhase

        let first, _ = stepped state (Action.Hora(1, 0, pai "4p"))
        let second, _ = stepped first (Action.Hora(2, 0, pai "4p"))
        let ended, _ = stepped second (Action.Hora(3, 0, pai "4p"))

        Assert.Equal<Seat list>([ 1 ], horasOf ended |> List.map (fun hora -> hora.Actor))

    [<Fact>]
    let ``头跳裁决的是实际宣言：优先的那家见逃时，靠后的那家成立`` () =
        let state = atTheDoubleRon ruleset
        let passed, _ = stepped state (Action.None 2)
        let ended, _ = stepped passed (Action.Hora(3, 0, pai "4p"))

        Assert.Equal<Seat list>([ 3 ], horasOf ended |> List.map (fun hora -> hora.Actor))
        Assert.Equal<Furiten>({ Permanent = false; Doujun = true }, furitenOf 2 ended)

    [<Fact>]
    let ``两家都见逃时这一局接着打，下家照常摸牌`` () =
        let state = atTheDoubleRon ruleset
        let passed, passEvents = stepped state (Action.None 2)

        // 还有一家没答复，这一步只是收下一个答复，什么事实都没发生。
        Assert.Empty(passEvents)

        let continued, events = stepped passed (Action.None 3)

        Assert.False(GameState.isEnded continued)
        Assert.Equal(1, List.length events)
        Assert.Equal(2, junmeOf 1 continued)
        Assert.Equal<Seat list>([ 1 ], responding continued)

    [<Fact>]
    let ``答复过的座位不能再答复`` () =
        let state = atTheDoubleRon ruleset
        let passed, _ = stepped state (Action.None 2)

        Assert.Equal<IllegalAction>(NotYourTurn(2, [ 3 ]), rejected passed (Action.None 2))
        Assert.Equal<IllegalAction>(NotYourTurn(2, [ 3 ]), rejected passed (Action.Hora(2, 0, pai "4p")))

    [<Fact>]
    let ``响应阶段不接受打牌`` () =
        let state = atTheDoubleRon ruleset

        let hand =
            GameState.player 2 state
            |> Option.map PlayerState.hand
            |> Option.defaultValue []

        Assert.Equal<IllegalAction>(NotYourTurn(2, [ 2; 3 ]), rejected state (Action.Dahai(2, List.head hand, false)))

    // ---- 响应阶段不是摆设 ----

    [<Fact>]
    let ``保持听牌的选手互相点炮：随机开的局里确实会走到响应阶段`` () =
        // 「响应阶段的合法动作集」那条属性靠随机局面验，这里钉住它不是空转的：
        // 随机选手几乎永远听不了牌，保听选手则常常互相点炮。
        let responsePhases =
            [ 1..40 ] |> List.collect (trace tenpaiSeeking) |> List.filter inResponsePhase

        Assert.NotEmpty(responsePhases)
        Assert.NotEmpty(responsePhases |> List.filter inRonPhase)

        for state in responsePhases do
            for choice in GameState.legalActions state do
                // 任何被问到的座位都能「过」；能荣和的那几家另外必定不振听
                // （振听的座位照样可以被问——振听只挡荣和，不挡鸣牌）。
                Assert.Contains(Action.None choice.Seat, choice.Actions)

                let offersRon =
                    choice.Actions
                    |> List.exists (fun action ->
                        match action with
                        | Action.Hora _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.None _ -> false)

                if offersRon then
                    Assert.False(Furiten.blocksRon (furitenOf choice.Seat state))

    [<Fact>]
    let ``摊好的两局各自以自摸和与荣和收尾`` () =
        let tsumo = horaTrace tsumoHoraScript |> List.last
        let ron = horaTrace doubleRonScript |> List.last

        Assert.Equal<Seat list>([ 0 ], horasOf tsumo |> List.map (fun hora -> hora.Target))
        Assert.Equal<Seat list>([ 0 ], horasOf ron |> List.map (fun hora -> hora.Target))
        Assert.Equal<Seat list>([ 0 ], horasOf tsumo |> List.map (fun hora -> hora.Actor))
        Assert.Equal<Seat list>([ 2 ], horasOf ron |> List.map (fun hora -> hora.Actor))

    // ---- 无役不可和 ----

    /// 跑到座位 0 第 2 巡摸进 2m、还没打出去的那一步（剧本见 `noYakuRonScript`）。
    let private beforeTheNoYakuDiscard () =
        startScripted noYakuRonScript
        |> driveUntil passive (fun state -> junmeOf 0 state = 2)

    [<Fact>]
    let ``型成立但一个役都没有时，Ron 不出现在合法动作集里`` () =
        let state = beforeTheNoYakuDiscard ()
        let discarded, events = stepped state (Action.Dahai(0, pai "2m", true))

        // 座位 2 确实听牌、确实不振听，手牌加上 2m 也确实成和了型——
        // 挡住它的只有「无役」这一条。
        match GameState.player 2 state with
        | Some player ->
            Assert.True(PlayerState.isTenpai kindSet player)
            Assert.True(PlayerState.isAgariWith kindSet (pai "2m") player)
            Assert.Equal<Furiten>(Furiten.none, PlayerState.furiten player)
        | None -> failwith "座位 2 应当存在"

        // 于是响应阶段压根不开：打完这张直接轮下家摸牌。
        Assert.False(inResponsePhase discarded)
        Assert.Empty(actionsOf 2 discarded)
        Assert.Equal<Event list>([ Dahai(0, pai "2m", true); Tsumo(1, pai "9p") ], events)

        // 宣言也没用：引擎压根不在等它答复。
        Assert.Equal<IllegalAction>(NotYourTurn(2, [ 1 ]), rejected discarded (Action.Hora(2, 0, pai "2m")))

    [<Fact>]
    let ``同一手牌无役不能荣和，自摸却有门前清自摸和`` () =
        let state =
            beforeTheNoYakuDiscard ()
            |> driveUntil passive (fun state -> junmeOf 2 state = 2)

        // 座位 2 第 2 巡摸进的正是刚才和不了的那张 2m：同一个型，自摸就有役。
        match GameState.player 2 state with
        | Some player -> Assert.True(PlayerState.isAgari kindSet player)
        | None -> failwith "座位 2 应当存在"

        Assert.Contains(Action.Hora(2, 2, pai "2m"), actionsOf 2 state)

        let ended, _ = stepped state (Action.Hora(2, 2, pai "2m"))

        // 规则集：`Ruleset.yonma`。123m 567p 345s 789s + 1z 雀头，嵌张自摸 2m：
        // 门前清自摸和 1 番（平和不成——嵌张听）；表宝牌指示牌 2z（宝牌 3z）手里没有。
        // 符 = 底 20 + 雀头（场风东）2 + 嵌张 2 + 自摸 2 = 26 → 切上 30 符。
        // 子 30 符 1 番自摸：基本点 240，子各付 300、亲付 500，合计 1100。
        Assert.Equal<string list>([ "2z" ], doraMarkers ended)

        Assert.Equal<Hora list>(
            [
                {
                    Actor = 2
                    Target = 2
                    Pai = pai "2m"
                    Fu = 30
                    Fan = 1
                    HoraPoints = 1100
                    Deltas = [ -500; -300; 1100; -300 ]
                    Scores = [ 24500; 24700; 26100; 24700 ]
                }
            ],
            horasOf ended
        )

    // ---- 本场与供托 ----

    /// 带本场与供托的开局条件。
    let private withSticks (honba: int) (kyotaku: int) : KyokuContext =
        { context with
            Honba = honba
            Kyotaku = kyotaku
        }

    [<Fact>]
    let ``自摸时本场由付家平摊，供托归和了者`` () =
        // 规则集：`Ruleset.yonma`（一本场 300 点、立直棒 1000 点）。3 本场且场上有 2 根立直棒：
        // 亲 30 符 3 番自摸 6000，三家各多付 300，和了者另收 2000 供托。
        let state =
            startScriptedIn ruleset (withSticks 3 2) tsumoHoraScript
            |> driveUntil passive (fun state -> junmeOf context.Oya state = 2)

        let ended, _ = stepped state (Action.Hora(context.Oya, context.Oya, pai "5z"))

        Assert.Equal<Hora list>(
            [
                {
                    Actor = 0
                    Target = 0
                    Pai = pai "5z"
                    Fu = 30
                    Fan = 3
                    HoraPoints = 6000
                    Deltas = [ 8900; -2300; -2300; -2300 ]
                    Scores = [ 33900; 22700; 22700; 22700 ]
                }
            ],
            horasOf ended
        )

        // 四家点数与供托之和不变：供托被收走了，它那 2000 点落到了四家点数里。
        Assert.Equal(List.sum context.Scores + 2 * ruleset.RiichiBou, List.sum (GameState.scores ended))

    [<Fact>]
    let ``荣和时本场由放铳者一家付`` () =
        // 规则集：`Ruleset.yonma`。2 本场且 1 根立直棒：子 30 符 1 番荣和 1000，
        // 放铳者另付 600 本场，和了者另收 1000 供托。
        let state =
            startScriptedIn ruleset (withSticks 2 1) ronFuritenScript
            |> driveUntil passive inRonPhase
            |> othersPass 2

        let ended, _ = stepped state (Action.Hora(2, 0, pai "4p"))

        Assert.Equal<Hora list>(
            [
                {
                    Actor = 2
                    Target = 0
                    Pai = pai "4p"
                    Fu = 30
                    Fan = 1
                    HoraPoints = 1000
                    Deltas = [ -1600; 0; 2600; 0 ]
                    Scores = [ 23400; 25000; 27600; 25000 ]
                }
            ],
            horasOf ended
        )

        Assert.Equal(List.sum context.Scores + ruleset.RiichiBou, List.sum (GameState.scores ended))

    [<Fact>]
    let ``双响时本场与供托只归排在最前的那一家`` () =
        // 规则集：`Ruleset.yonma` 关掉头跳。1 本场且 1 根立直棒。
        // 座位 2（排在前）：平和 30 符 1 番 1000 + 本场 300 + 供托 1000；
        // 座位 3：平和断幺九 30 符 2 番 2000，不再分本场与供托。
        let doubleRon = Ruleset.withoutAtamahane ruleset

        let state =
            startScriptedIn doubleRon (withSticks 1 1) doubleRonScript
            |> driveUntil passive inRonPhase

        let first, _ = stepped state (Action.Hora(2, 0, pai "4p"))
        let ended, _ = stepped first (Action.Hora(3, 0, pai "4p"))

        Assert.Equal<Hora list>(
            [
                {
                    Actor = 2
                    Target = 0
                    Pai = pai "4p"
                    Fu = 30
                    Fan = 1
                    HoraPoints = 1000
                    Deltas = [ -1300; 0; 2300; 0 ]
                    Scores = [ 23700; 25000; 27300; 25000 ]
                }
                {
                    Actor = 3
                    Target = 0
                    Pai = pai "4p"
                    Fu = 30
                    Fan = 2
                    HoraPoints = 2000
                    Deltas = [ -2000; 0; 0; 2000 ]
                    Scores = [ 21700; 25000; 27300; 27000 ]
                }
            ],
            horasOf ended
        )

        Assert.Equal(List.sum context.Scores + ruleset.RiichiBou, List.sum (GameState.scores ended))

    // ---- 渲染 ----

    [<Fact>]
    let ``和了相关的拒绝理由有中文说明`` () =
        Assert.Equal("座位 1 声称自摸和 3索，但刚摸进的不是这张", IllegalAction.toDisplay (HoraTileMismatch(1, 1, pai "3s")))

        Assert.Equal("座位 1 声称荣和座位 2 打出的 3索，但刚打出的不是这张", IllegalAction.toDisplay (HoraTileMismatch(1, 2, pai "3s")))

        Assert.Equal("座位 1 的手牌加上 3索 不成和了型", IllegalAction.toDisplay (IllegalAction.NotAgari(1, pai "3s")))

        Assert.Equal("座位 1 的手牌加上 3索 一个役都没有，不能和", IllegalAction.toDisplay (IllegalAction.NoYaku(1, pai "3s")))

        Assert.Equal("现在没有可响应的牌，座位 1 无从响应起", IllegalAction.toDisplay (NothingToRespond 1))

    [<Fact>]
    let ``振听两种状态各有中文说明`` () =
        Assert.Equal("非振听", Furiten.toDisplay Furiten.none)
        Assert.Equal("振听（永久）", Furiten.toDisplay { Permanent = true; Doujun = false })
        Assert.Equal("振听（同巡）", Furiten.toDisplay { Permanent = false; Doujun = true })
        Assert.Equal("振听（永久 + 同巡）", Furiten.toDisplay { Permanent = true; Doujun = true })
