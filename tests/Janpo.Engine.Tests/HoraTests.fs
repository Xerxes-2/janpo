namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 和了的成立路径：自摸和、荣和、振听与头跳。
///
/// 牌山是**摊出来的**（`startScripted`），因此「指定的和了在指定 Junme 发生」是确定的事实，
/// 不必去碰运气找种子。本票**不算点数**：事件里的符 / 番 / 和了点与授受一律为 0（08 票填）。
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

    let private horasOf (state: GameState) : Hora list = GameState.horas state

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

        let expected =
            {
                Actor = 0
                Target = 0
                Pai = pai "5z"
                Fu = 0
                Fan = 0
                HoraPoints = 0
                Deltas = [ 0; 0; 0; 0 ]
                Scores = [ 25000; 25000; 25000; 25000 ]
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

    /// 跑到座位 0 打出 4p、等人响应的那一步（剧本见 `GameStateFixtures.ronFuritenScript`）。
    let private atTheRon () =
        startScripted ronFuritenScript |> driveUntil passive inResponsePhase

    [<Fact>]
    let ``他家打出和了牌时 Ron 与「过」一起进入对应座位的合法动作集`` () =
        let state = atTheRon ()

        Assert.Equal<Seat list>([ 2 ], responding state)

        Assert.Equal<Action list>([ Action.Hora(2, 0, pai "4p"); Action.None 2 ], actionsOf 2 state)

    [<Fact>]
    let ``振听座位的 Ron 不出现在合法动作集里，它照样是听牌的`` () =
        let state = atTheRon ()

        // 座位 1 听 4p / 7p，但它自己打过 7p —— 永久振听，因此压根不在被问之列。
        Assert.True(isTenpai 1 state)
        Assert.Equal<Furiten>({ Permanent = true; Doujun = false }, furitenOf 1 state)
        Assert.DoesNotContain(1, responding state)
        Assert.Empty(actionsOf 1 state)

        // 不在被问之列的座位提交 Ron，得到的是「现在不轮到你」，而不是「和了不成立」。
        Assert.Equal<IllegalAction>(NotYourTurn(1, [ 2 ]), rejected state (Action.Hora(1, 0, pai "4p")))

    [<Fact>]
    let ``荣和结束这一局，Ko 和了则不连庄`` () =
        let state = atTheRon ()
        let ended, events = stepped state (Action.Hora(2, 0, pai "4p"))

        let expected =
            {
                Actor = 2
                Target = 0
                Pai = pai "4p"
                Fu = 0
                Fan = 0
                HoraPoints = 0
                Deltas = [ 0; 0; 0; 0 ]
                Scores = [ 25000; 25000; 25000; 25000 ]
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
        let state = atTheRon ()
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
        let state = atTheRon ()
        let passed, _ = stepped state (Action.None 2)

        // 座位 1 第 2 巡摸进 1p 又摸切出去 —— 那是座位 2 的另一张和了牌，
        // 但座位 2 同巡振听，因此引擎压根不停下来问，直接让座位 2 摸牌。
        let discarded, events = stepped passed (Action.Dahai(1, pai "1p", true))

        Assert.False(inResponsePhase discarded)
        Assert.Equal<Event list>([ Dahai(1, pai "1p", true); Tsumo(2, pai "3z") ], events)

    [<Fact>]
    let ``同巡振听解除之后，再打出的和了牌又能荣和`` () =
        let state = atTheRon ()
        let passed, _ = stepped state (Action.None 2)

        // 座位 2 自己摸过一巡（同巡振听解除），随后座位 3 打出 1p。
        let again = passed |> driveUntil passive inResponsePhase

        Assert.Equal<Seat list>([ 2 ], responding again)
        Assert.Equal<Action list>([ Action.Hora(2, 3, pai "1p"); Action.None 2 ], actionsOf 2 again)

    // ---- 头跳与双响 ----

    /// 跑到座位 0 打出 4p、座位 2 与座位 3 都能荣和的那一步
    /// （剧本见 `GameStateFixtures.doubleRonScript`）。
    let private atTheDoubleRon (ruleset: Ruleset) =
        startScriptedWith ruleset doubleRonScript |> driveUntil passive inResponsePhase

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
            startScriptedWith doubleRon tripleRonScript
            |> driveUntil passive inResponsePhase

        Assert.Equal<Seat list>([ 1; 2; 3 ], responding state)

        let first, _ = stepped state (Action.Hora(1, 0, pai "4p"))
        let second, _ = stepped first (Action.Hora(2, 0, pai "4p"))
        let ended, _ = stepped second (Action.Hora(3, 0, pai "4p"))

        Assert.Equal<Seat list>([ 1; 2; 3 ], horasOf ended |> List.map (fun hora -> hora.Actor))

    [<Fact>]
    let ``头跳开着时同巡三响只成立最靠前的一家`` () =
        let state =
            startScriptedWith ruleset tripleRonScript |> driveUntil passive inResponsePhase

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

        for state in responsePhases do
            for choice in GameState.legalActions state do
                Assert.False(Furiten.blocksRon (furitenOf choice.Seat state))
                Assert.Contains(Action.None choice.Seat, choice.Actions)

    [<Fact>]
    let ``摊好的两局各自以自摸和与荣和收尾`` () =
        let tsumo = horaTrace tsumoHoraScript |> List.last
        let ron = horaTrace doubleRonScript |> List.last

        Assert.Equal<Seat list>([ 0 ], horasOf tsumo |> List.map (fun hora -> hora.Target))
        Assert.Equal<Seat list>([ 0 ], horasOf ron |> List.map (fun hora -> hora.Target))
        Assert.Equal<Seat list>([ 0 ], horasOf tsumo |> List.map (fun hora -> hora.Actor))
        Assert.Equal<Seat list>([ 2 ], horasOf ron |> List.map (fun hora -> hora.Actor))

    // ---- 渲染 ----

    [<Fact>]
    let ``和了相关的拒绝理由有中文说明`` () =
        Assert.Equal("座位 1 声称自摸和 3索，但刚摸进的不是这张", IllegalAction.toDisplay (HoraTileMismatch(1, 1, pai "3s")))

        Assert.Equal("座位 1 声称荣和座位 2 打出的 3索，但刚打出的不是这张", IllegalAction.toDisplay (HoraTileMismatch(1, 2, pai "3s")))

        Assert.Equal("座位 1 的手牌加上 3索 不成和了型", IllegalAction.toDisplay (IllegalAction.NotAgari(1, pai "3s")))
        Assert.Equal("现在没有可响应的牌，座位 1 无从「过」起", IllegalAction.toDisplay (NothingToRespond 1))

    [<Fact>]
    let ``振听两种状态各有中文说明`` () =
        Assert.Equal("非振听", Furiten.toDisplay Furiten.none)
        Assert.Equal("振听（永久）", Furiten.toDisplay { Permanent = true; Doujun = false })
        Assert.Equal("振听（同巡）", Furiten.toDisplay { Permanent = false; Doujun = true })
        Assert.Equal("振听（永久 + 同巡）", Furiten.toDisplay { Permanent = true; Doujun = true })
