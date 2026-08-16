namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 立直全链路：宣言合法性、两个事件（`reach` / `reach_accepted`）、立直棒与供托、
/// 一发、立直后的行动限制、立直后的永久振听，以及切给 11 票的暗杠判据（裁决 D-8）。
///
/// 牌山一律是**摊出来的**（`startScripted`），因此「谁在第几巡立直、谁和了什么」是确定的事实。
module RiichiTests =

    let private pai (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"{notation} 应当是合法记法，却得到 {TileParseError.toDisplay error}"

    let private tiles (notations: string) : Tile list = tilesOf notations

    let private stepped (state: GameState) (action: Action) =
        match GameState.step state action with
        | Ok(next, events) -> next, events
        | Error illegal -> failwith $"这个动作应当合法，却被拒：{IllegalAction.toDisplay illegal}"

    let private rejected (state: GameState) (action: Action) =
        match GameState.step state action with
        | Error illegal -> illegal
        | Ok _ -> failwith "这个动作应当被拒，却被接受了"

    let private actionsOf (seat: Seat) (state: GameState) : Action list =
        GameState.legalActions state
        |> List.tryFind (fun choice -> choice.Seat = seat)
        |> Option.map (fun choice -> choice.Actions)
        |> Option.defaultValue []

    let private playerOf (seat: Seat) (state: GameState) : PlayerState =
        match GameState.player seat state with
        | Some player -> player
        | None -> failwith $"座位 {seat} 应当存在"

    let private riichiOf (seat: Seat) (state: GameState) : RiichiState =
        PlayerState.riichi (playerOf seat state)

    /// 这一局的和了读法（役、宝牌与分解）。**一定要在和了那一步之前问**：
    /// 局终之后阶段已经不是「此刻能和什么」了。
    let private yakuOf (seat: Seat) (state: GameState) : Yaku list =
        match GameState.horaOf seat state with
        | Ok reading -> reading.Tally.Yaku |> List.map fst
        | Error error -> failwith $"座位 {seat} 此刻应当和得了，却得到 {YakuError.toDisplay error}"

    /// 事件流里的立直两条：`(reach 的座位, reach_accepted 的座位)`。
    let private riichiEvents (state: GameState) : Seat list * Seat list =
        let declared =
            GameState.events state
            |> List.choose (fun event ->
                match event with
                | Riichi actor -> Some actor
                | RiichiAccepted _
                | StartGame _
                | StartKyoku _
                | Tsumo _
                | Dahai _
                | Pon _
                | Chi _
                | Hora _
                | Ryuukyoku _
                | EndKyoku
                | Ankan _
                | Kakan _
                | Minkan _
                | Dora _
                | EndGame -> None)

        let accepted =
            GameState.events state
            |> List.choose (fun event ->
                match event with
                | RiichiAccepted actor -> Some actor
                | Riichi _
                | StartGame _
                | StartKyoku _
                | Tsumo _
                | Dahai _
                | Pon _
                | Chi _
                | Hora _
                | Ryuukyoku _
                | EndKyoku
                | Ankan _
                | Kakan _
                | Minkan _
                | Dora _
                | EndGame -> None)

        declared, accepted

    // ---- 选手 ----

    /// 立直的那家由 `riichiSeeking` 出手，其余各家摸切、响应「过」（`passive`）。
    let private riichiSeat: Seat = Seat.first

    let private riichiThenPassive: Player<Rng> =
        fun rng state choice ->
            if choice.Seat = riichiSeat then
                riichiSeeking rng state choice
            else
                passive rng state choice

    /// 立直的那家**从不宣言荣和**：见逃是它的默认行为（立直后见逃 → 永久振听要它）。
    let private riichiNeverRon: Player<Rng> =
        fun rng state choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            if choice.Seat <> riichiSeat then
                passive rng state choice
            else
                let chosen =
                    pick (fun action ->
                        match action with
                        | Action.Riichi _ -> true
                        | Action.Dahai _
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false)
                    |> Option.orElseWith (fun () ->
                        pick (fun action ->
                            match action with
                            | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                            | Action.Hora _
                            | Action.Pon _
                            | Action.Chi _
                            | Action.Riichi _
                            | Action.Ankan _
                            | Action.Kakan _
                            | Action.Minkan _
                            | Action.Ryuukyoku _
                            | Action.None _ -> false))
                    |> Option.orElseWith (fun () ->
                        pick (fun action ->
                            match action with
                            | Action.None _ -> true
                            | Action.Dahai _
                            | Action.Pon _
                            | Action.Chi _
                            | Action.Riichi _
                            | Action.Ankan _
                            | Action.Kakan _
                            | Action.Minkan _
                            | Action.Ryuukyoku _
                            | Action.Hora _ -> false))
                    |> Option.defaultValue (List.head choice.Actions)

                chosen, rng

    /// 立直的那家出手，其余各家**能碰就碰**、否则摸切：一发被鸣牌打断要它。
    let private riichiThenPon: Player<Rng> =
        fun rng state choice ->
            if choice.Seat = riichiSeat then
                riichiSeeking rng state choice
            else
                let pon =
                    choice.Actions
                    |> List.tryFind (fun action ->
                        match action with
                        | Action.Pon _ -> true
                        | Action.Dahai _
                        | Action.Hora _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false)

                match pon with
                | Some action -> action, rng
                | None -> passive rng state choice

    // ---- 剧本 ----

    /// 一发自摸（也是两立直）的剧本：Oya 听 5z 单骑，第 1 巡摸进 1z 就立直、摸切宣言牌，
    /// 其余三家摸切没用的字牌（都只有一张，碰不成），Oya 第 2 巡摸进 5z 自摸。
    ///
    /// 与 `tsumoHoraScript` 是同一座牌山：那条用例不立直，这条立直——同一局面下多的正是立直。
    let private ippatsuTsumoScript = tsumoHoraScript

    /// 一发被碰打断的剧本：Oya 第 1 巡立直，座位 1 摸切 9p 被座位 2 碰走，
    /// Oya 第 2 巡照样摸到 5z 自摸——但一发已经没了。
    let private ippatsuBrokenScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 4z 6z 7z 9m"
                    "9p 9p 2p 5p 8p 2s 5s 8s 1z 2z 3z 4z 6z"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 4z 6z 7z 9m"
                ]
            Draws = "1z 9p 4z 5z"
        }

    /// 立直宣言牌被荣和的剧本：Oya 第 1 巡立直、摸切宣言牌 1z，
    /// 座位 1 拿 1z（场风东）荣和它。立直不成立，立直棒不出。
    let private declarationRonnedScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
                    "1z 1z 2z 2z 2z 5m 6m 7m 3p 4p 5p 8s 8s"
                    "9p 9p 9p 2s 5s 8s 3z 4z 6z 7z 9m 9m 4s"
                    "3p 6p 9p 3s 6s 9s 3z 4z 6z 7z 9m 2p 7p"
                ]
            Draws = "1z"
        }

    /// 立直后见逃的剧本：Oya 听 5z 单骑，第 1 巡立直；座位 1 第 1 巡就摸切 5z，
    /// Oya 放过它（永久振听），第 2 巡摸进 2z 只能摸切。
    let private minogashiScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 4z 6z 7z 9m"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 4z 6z 7z 9m"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 4z 6z 7z 9m"
                ]
            Draws = "1z 5z 3z 4z 2z"
        }

    // ---- 宣言 ----

    [<Fact>]
    let ``听牌就能立直，宣言排在自摸和之后、打牌之前`` () =
        let state = startScripted ippatsuTsumoScript

        // Oya 的第一手：123m 456m 789m 123p 5z 摸进 1z，打 1z 或 5z 都还听牌。
        Assert.Contains(Action.Riichi(seat 0), actionsOf (seat 0) state)

        let riichiIndex =
            actionsOf (seat 0) state
            |> List.findIndex (fun action -> action = Action.Riichi(seat 0))

        let firstDahai =
            actionsOf (seat 0) state
            |> List.findIndex (fun action ->
                match action with
                | Action.Dahai _ -> true
                | Action.Hora _
                | Action.Pon _
                | Action.Chi _
                | Action.Riichi _
                | Action.Ankan _
                | Action.Kakan _
                | Action.Minkan _
                | Action.Ryuukyoku _
                | Action.None _ -> false)

        Assert.True(riichiIndex < firstDahai)

    [<Fact>]
    let ``宣言立直产出 reach，此时立直棒还没出`` () =
        let state = startScripted ippatsuTsumoScript
        let declared, events = stepped state (Action.Riichi(seat 0))

        Assert.Equal<Event list>([ Riichi(seat 0) ], events)
        Assert.Equal(RiichiState.Declared RiichiDeclaration.DoubleRiichi, riichiOf (seat 0) declared)
        // 宣言牌还没落定：点数与供托一动不动。
        Assert.Equal<int list>(context.Scores, GameState.scores declared)
        Assert.Equal(context.Kyotaku, GameState.kyotaku declared)

    [<Fact>]
    let ``宣言之后只能打「打完仍听牌」的那几张，自摸和与再宣言都没有了`` () =
        let state = startScripted ippatsuTsumoScript
        let declared, _ = stepped state (Action.Riichi(seat 0))

        // 手里 123m 456m 789m 123p 5z + 摸进的 1z：打 5z 留 1z 单骑、摸切 1z 留 5z 单骑，
        // 两条都仍听牌，其余每一张打下去都不听。
        Assert.Equal<Action list>(
            [ Action.Dahai(seat 0, pai "5z", false); Action.Dahai(seat 0, pai "1z", true) ],
            actionsOf (seat 0) declared
        )

        Assert.Equal(CannotRiichi(seat 0), rejected declared (Action.Riichi(seat 0)))
        // 牌就在手里，但打了就不听牌了——报的是立直而不是食替。
        Assert.Equal(RiichiRestricted(seat 0, pai "1m"), rejected declared (Action.Dahai(seat 0, pai "1m", false)))

    [<Fact>]
    let ``宣言牌落定才产出 reach_accepted：立直棒出、供托进一根`` () =
        let state = startScripted ippatsuTsumoScript
        let declared, _ = stepped state (Action.Riichi(seat 0))
        let accepted, events = stepped declared (Action.Dahai(seat 0, pai "1z", true))

        // 没人能响应这张 1z，因此宣言牌当场落定：dahai → reach_accepted → tsumo。
        Assert.Equal<Event list>(
            [
                Dahai(seat 0, pai "1z", true)
                RiichiAccepted(seat 0)
                Tsumo(seat 1, pai "2z")
            ],
            events
        )

        Assert.Equal(RiichiState.Accepted RiichiDeclaration.DoubleRiichi, riichiOf (seat 0) accepted)
        Assert.Equal(1, GameState.kyotaku accepted)
        Assert.Equal<int list>([ 24000; 25000; 25000; 25000 ], GameState.scores accepted)

    [<Fact>]
    let ``宣言了立直就不能回头自摸和`` () =
        // 第 2 巡摸进 5z 就和了，但打 5z 留 5z 单骑也仍听牌——因此自摸和与立直同时在动作集里。
        let state =
            startScripted ippatsuTsumoScript
            |> driveUntil passive (fun state -> junmeOf (seat 0) state = 2)

        Assert.Contains(Action.Hora(seat 0, seat 0, pai "5z"), actionsOf (seat 0) state)
        Assert.Contains(Action.Riichi(seat 0), actionsOf (seat 0) state)

        // 选了立直就得把宣言牌打出去：这一手不能反悔去和。
        let declared, _ = stepped state (Action.Riichi(seat 0))

        Assert.Equal(RiichiRestricted(seat 0, pai "5z"), rejected declared (Action.Hora(seat 0, seat 0, pai "5z")))

        // 宣言之后剩下的全是打牌（这一手的手牌四面八方都听，因此打得出去的不只一张），
        // 自摸和与再宣言一条不剩。
        Assert.All(
            actionsOf (seat 0) declared,
            fun action ->
                match action with
                | Action.Dahai _ -> ()
                | Action.Hora _
                | Action.Pon _
                | Action.Chi _
                | Action.Riichi _
                | Action.Ankan _
                | Action.Kakan _
                | Action.Minkan _
                | Action.Ryuukyoku _
                | Action.None _ -> failwith $"宣言之后这一手只剩打牌，却有 {action}"
        )

        Assert.Contains(Action.Dahai(seat 0, pai "5z", true), actionsOf (seat 0) declared)

    [<Fact>]
    let ``不门清、点数不够、牌山不够都立直不了`` () =
        let hand = tiles "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 1z"
        let ruleset = Ruleset.yonma

        let pon =
            match Naki.pon (seat 1) (pai "2z") [ pai "2z"; pai "2z" ] with
            | Ok naki -> naki
            | Error error -> failwith $"应当碰得成，却得到 {NakiError.toDisplay error}"

        let ankan =
            match Naki.ankan [ pai "2z"; pai "2z"; pai "2z"; pai "2z" ] with
            | Ok naki -> naki
            | Error error -> failwith $"应当杠得成，却得到 {NakiError.toDisplay error}"

        Assert.True(RiichiState.canDeclare ruleset kindSet 4 1000 [] hand)
        // 暗杠不破门清（那一手只有 11 张暗牌，这里只验门清那一条）。
        Assert.True(RiichiState.canDeclare ruleset kindSet 4 1000 [ ankan ] (tiles "1m 2m 3m 4m 5m 6m 7m 8m 9m 5z 1z"))
        // 碰破门清。
        Assert.False(RiichiState.canDeclare ruleset kindSet 4 1000 [ pon ] (tiles "1m 2m 3m 4m 5m 6m 7m 8m 9m 5z 1z"))
        // 点数不够一根立直棒。
        Assert.False(RiichiState.canDeclare ruleset kindSet 4 999 [] hand)
        // 可摸区剩的牌不够全场再摸一圈。
        Assert.False(RiichiState.canDeclare ruleset kindSet 3 1000 [] hand)
        // 没听牌：打哪张都不听。
        Assert.False(
            RiichiState.canDeclare ruleset kindSet 4 1000 [] (tiles "1m 3m 5m 7m 9m 1p 3p 5p 7p 9p 1s 3s 5s 7s")
        )

    [<Fact>]
    let ``立直宣言牌只能从「打完仍听牌」的那几张里挑`` () =
        // 123m 456m 789m 123p 5z 摸进 1z：打 1z 或 5z 都留一个单骑，其余都不听。
        Assert.Equal<Tile list>(
            [ pai "1z"; pai "5z" ],
            RiichiState.tenpaiDahai kindSet 0 (tiles "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 1z")
            |> Tile.sort
        )

    // ---- 一发 ----

    [<Fact>]
    let ``一发自摸：立直后一巡内自摸，两立直与一发都成立`` () =
        let state =
            startScripted ippatsuTsumoScript
            |> driveUntil riichiThenPassive (fun state -> junmeOf (seat 0) state = 2)

        let yaku = yakuOf (seat 0) state

        Assert.Contains(Yaku.DoubleRiichi, yaku)
        Assert.Contains(Yaku.Ippatsu, yaku)
        Assert.Contains(Yaku.MenzenTsumo, yaku)
        Assert.DoesNotContain(Yaku.Riichi, yaku)

        let ended, _ = stepped state (Action.Hora(seat 0, seat 0, pai "5z"))
        let declared, accepted = riichiEvents ended

        Assert.Equal<Seat list>(seats [ 0 ], declared)
        Assert.Equal<Seat list>(seats [ 0 ], accepted)

        // 立直棒进了供托，又被自己收回：这一局的和了增减之和 = 那一根立直棒。
        Assert.Equal(0, GameState.kyotaku ended)

        match GameState.horas ended with
        | [ hora ] ->
            Assert.Equal(Ruleset.kyotakuScore ruleset 1, List.sum hora.Deltas)
            // 里宝牌只在立直时翻：这条用例里它非空。
            Assert.NotEmpty(hora.UraDoraMarkers)
            Assert.Equal<Tile list>(Wall.uraIndicators (GameState.wall ended), hora.UraDoraMarkers)
        | horas -> failwith $"应当只有一条和了，却有 {List.length horas} 条"

        // 四家点数与供托之和守恒（立直棒从自家扣出、又随和了收回）。
        Assert.Equal(List.sum context.Scores, List.sum (GameState.scores ended))

    [<Fact>]
    let ``一发荣和：立直后一巡内荣和他家打出的那张`` () =
        let state =
            startScripted minogashiScript
            |> driveUntil riichiThenPassive (fun state ->
                actionsOf (seat 0) state
                |> List.contains (Action.Hora(seat 0, seat 1, pai "5z")))

        let yaku = yakuOf (seat 0) state

        Assert.Contains(Yaku.DoubleRiichi, yaku)
        Assert.Contains(Yaku.Ippatsu, yaku)
        // 荣和不是自摸：门前清自摸和不成立。
        Assert.DoesNotContain(Yaku.MenzenTsumo, yaku)

        let ended, _ = stepped state (Action.Hora(seat 0, seat 1, pai "5z"))

        match GameState.horas ended with
        | [ hora ] ->
            Assert.Equal(seat 0, hora.Actor)
            Assert.Equal(seat 1, hora.Target)
            // 自己那一根立直棒随和了回到手里：授受的增减之和就是它。
            Assert.Equal(Ruleset.kyotakuScore ruleset 1, List.sum hora.Deltas)
            Assert.NotEmpty(hora.UraDoraMarkers)
        | horas -> failwith $"应当只有一条和了，却有 {List.length horas} 条"

        Assert.Equal(0, GameState.kyotaku ended)
        Assert.Equal(List.sum context.Scores, List.sum (GameState.scores ended))

    [<Fact>]
    let ``一发被碰打断：同一座牌山、同一次和了，只是一发没了`` () =
        let state =
            startScripted ippatsuBrokenScript
            |> driveUntil riichiThenPon (fun state -> junmeOf (seat 0) state = 2)

        // 座位 2 碰走了座位 1 打的 9p：一发到此为止。
        Assert.Equal(1, PlayerState.nakiCount (playerOf (seat 2) state))

        let yaku = yakuOf (seat 0) state

        Assert.Contains(Yaku.DoubleRiichi, yaku)
        Assert.DoesNotContain(Yaku.Ippatsu, yaku)

        let ended, _ = stepped state (Action.Hora(seat 0, seat 0, pai "5z"))
        Assert.True(GameState.isEnded ended)

    [<Fact>]
    let ``立直宣言牌被荣和：只有 reach 没有 reach_accepted，立直棒不出`` () =
        let state =
            startScripted declarationRonnedScript
            |> driveUntil riichiThenPassive (fun state ->
                actionsOf (seat 1) state
                |> List.contains (Action.Hora(seat 1, seat 0, pai "1z")))

        // 宣言了，但宣言牌还悬着：立直棒没出，点数一动不动。
        Assert.Equal(RiichiState.Declared RiichiDeclaration.DoubleRiichi, riichiOf (seat 0) state)
        Assert.Equal<int list>(context.Scores, GameState.scores state)

        let ended, _ = stepped state (Action.Hora(seat 1, seat 0, pai "1z"))
        let declared, accepted = riichiEvents ended

        Assert.Equal<Seat list>(seats [ 0 ], declared)
        Assert.Empty(accepted)

        // 立直不成立：状态退回没立直，那 1000 点还在座位 0 手里。
        Assert.Equal(RiichiState.none, riichiOf (seat 0) ended)
        Assert.Equal(context.Kyotaku, GameState.kyotaku ended)

        match GameState.horas ended with
        | [ hora ] ->
            Assert.Equal(seat 1, hora.Actor)
            Assert.Equal(seat 0, hora.Target)
            // 供托是空的，因此这一次授受的增减之和为 0。
            Assert.Equal(0, List.sum hora.Deltas)
            // 放铳的那家没立直，和了的那家也没有：里宝牌不翻。
            Assert.Empty(hora.UraDoraMarkers)
        | horas -> failwith $"应当只有一条和了，却有 {List.length horas} 条"

    // ---- 立直后的行动限制 ----

    [<Fact>]
    let ``立直后只能摸切`` () =
        let state =
            startScripted minogashiScript
            |> driveUntil riichiNeverRon (fun state -> junmeOf (seat 0) state = 2)

        // 第 2 巡摸进 2z：手里那 13 张一张都动不了，只剩摸切这一条。
        Assert.Equal<Action list>([ Action.Dahai(seat 0, pai "2z", true) ], actionsOf (seat 0) state)
        Assert.Equal(RiichiRestricted(seat 0, pai "5z"), rejected state (Action.Dahai(seat 0, pai "5z", false)))

    [<Fact>]
    let ``立直中的座位不进鸣牌的合法动作集，但仍然能荣和`` () =
        let state =
            startScripted minogashiScript
            |> driveUntil riichiNeverRon (fun state ->
                match GameState.phase state with
                | AwaitingResponse phase -> phase.Target = seat 1
                | AwaitingDahai _
                | Ended _ -> false)

        // 座位 1 摸切的是 5z：立直中的座位 0 单骑等它，因此被问到的只有荣和与「过」。
        Assert.Equal<Action list>(
            [ Action.Hora(seat 0, seat 1, pai "5z"); Action.None(seat 0) ],
            actionsOf (seat 0) state
        )

    [<Fact>]
    let ``立直后见逃是永久振听，自己再摸打也解不开`` () =
        let state =
            startScripted minogashiScript
            |> driveUntil riichiNeverRon (fun state ->
                match GameState.phase state with
                | AwaitingResponse phase -> phase.Target = seat 1
                | AwaitingDahai _
                | Ended _ -> false)

        Assert.False(Furiten.blocksRon (PlayerState.furiten (playerOf (seat 0) state)))

        let passed, _ = stepped state (Action.None(seat 0))

        // 见逃的那张 5z 在座位 1 的河里，不在自己的河里——不立直的话这只是同巡振听。
        let furiten = PlayerState.furiten (playerOf (seat 0) passed)
        Assert.True(furiten.Permanent)

        // 自己再摸一张、再打一张（重算永久振听的那一步）之后，它仍然闩着。
        let later =
            passed |> driveUntil riichiNeverRon (fun state -> junmeOf (seat 0) state = 3)

        Assert.True((PlayerState.furiten (playerOf (seat 0) later)).Permanent)
        Assert.True(Furiten.blocksRon (PlayerState.furiten (playerOf (seat 0) later)))

    // ---- 立直棒与供托 ----

    [<Fact>]
    let ``立直棒随和了归和了者，跨局结转的是局终时场上的那几根`` () =
        let ended =
            startScripted ippatsuTsumoScript
            |> driveUntil riichiThenPassive GameState.isEnded

        Assert.Equal(0, GameState.kyotaku ended)

        // 流局收尾的那一局：立直棒留在场上，等下一局的和了者来收。
        let ryuukyoku =
            startScripted minogashiScript |> driveUntil riichiNeverRon GameState.isEnded

        Assert.True(GameState.isEnded ryuukyoku)
        Assert.Equal(1, GameState.kyotaku ryuukyoku)
        Assert.Equal(List.sum context.Scores, List.sum (GameState.scores ryuukyoku) + Ruleset.kyotakuScore ruleset 1)

    // ---- 给 11 票的暗杠判据（裁决 D-8） ----

    [<Fact>]
    let ``立直后的暗杠判据：只有刚摸进的那张才杠得成`` () =
        let hand = tiles "1m 2m 3m 4m 4m 4m 5m 6m 7m 5p 5p 9s 9s 4m"
        let riichi = RiichiState.Accepted RiichiDeclaration.Riichi

        // 123m 444m 567m + 55p + 99s 双碰听 5p / 9s，摸进第四张 4m：杠掉不动听牌，
        // 而且 444m 在每一种读法里都是暗刻——三条都过。
        Assert.True(RiichiState.allowsAnkan kindSet riichi [] hand (Some(pai "4m")) (pai "4m"))

        // 送り杠：杠的不是刚摸进的那张。
        Assert.False(RiichiState.allowsAnkan kindSet riichi [] hand (Some(pai "5p")) (pai "4m"))
        // 压根没摸牌（鸣牌之后那一手）。
        Assert.False(RiichiState.allowsAnkan kindSet riichi [] hand None (pai "4m"))
        // 手里只有三张，杠不成。
        Assert.False(
            RiichiState.allowsAnkan
                kindSet
                riichi
                []
                (tiles "1m 2m 3m 4m 4m 4m 5m 6m 7m 5p 5p 9s 9s 5p")
                (Some(pai "5p"))
                (pai "5p")
        )

    /// 13 张听牌手与杠掉那四张之后各听什么——暗杠判据的第二条比的就是这两个集合。
    let private waitsBeforeAfter (target: string) (declared: string) : Tile list * Tile list =
        let waitsOf (nakiCount: int) (concealed: Tile list) =
            match HandShape.create nakiCount concealed with
            | Ok shape -> AgariShape.waits kindSet shape
            | Error error -> failwith $"应当构造得出手牌形态，却得到 {HandShapeError.toDisplay error}"

        let hand = tiles declared

        let afterKan = hand |> List.filter (fun tile -> Tile.deaka tile <> pai target)

        waitsOf 0 hand, waitsOf 1 afterKan

    [<Fact>]
    let ``立直后的暗杠判据：听牌没变但面子构成变了，照样杠不得`` () =
        // 123m 66m + 333s 44s 555s：和 6m 时索子只能读成 333s + 555s + 44s，
        // 而和 4s 时它又能读成 345s + 345s + 345s——**后一种读法里 3s 不是暗刻**。
        let declared = "1m 2m 3m 6m 6m 3s 3s 3s 4s 4s 5s 5s 5s"
        let before, after = waitsBeforeAfter "3s" declared

        // 杠掉四张 3s 之后仍然是 6m / 4s 两面——**听牌一模一样**，挡住这一杠的是第三条。
        Assert.Equal<Tile list>(before, after)
        Assert.Equal<Tile list>([ pai "6m"; pai "4s" ], before)

        Assert.False(
            RiichiState.allowsAnkan
                kindSet
                (RiichiState.Accepted RiichiDeclaration.Riichi)
                []
                (tiles declared @ [ pai "3s" ])
                (Some(pai "3s"))
                (pai "3s")
        )

    [<Fact>]
    let ``立直后的暗杠判据：听牌变了就杠不得`` () =
        // 234m + 33m + 666m + 456s + 89s 单等 7s；杠掉四张 3m 之后 2m4m 成了死牌，直接不听。
        let declared = "2m 3m 3m 3m 4m 6m 6m 6m 4s 5s 6s 8s 9s"
        let before, after = waitsBeforeAfter "3m" declared

        Assert.Equal<Tile list>([ pai "7s" ], before)
        Assert.Empty(after)

        Assert.False(
            RiichiState.allowsAnkan
                kindSet
                (RiichiState.Accepted RiichiDeclaration.Riichi)
                []
                (tiles declared @ [ pai "3m" ])
                (Some(pai "3m"))
                (pai "3m")
        )

    [<Fact>]
    let ``立直后的暗杠判据：没立直不受这一层限制，宣言牌还没落定则一律不许`` () =
        let hand = tiles "1m 2m 3m 4m 4m 4m 5m 6m 7m 5p 5p 9s 9s 4m"

        // 没立直：这条判据放行（能不能杠由 11 票的其余条件说了算）。
        Assert.True(RiichiState.allowsAnkan kindSet RiichiState.none [] hand (Some(pai "4m")) (pai "4m"))
        // 宣言了还没落定：那一手只能打宣言牌。
        Assert.False(
            RiichiState.allowsAnkan
                kindSet
                (RiichiState.Declared RiichiDeclaration.Riichi)
                []
                hand
                (Some(pai "4m"))
                (pai "4m")
        )
