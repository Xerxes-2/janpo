namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 摸打循环的不变量：合法动作集非空、牌数守恒、回放确定性、非法动作是值不是异常。
/// 局面取自**可达**局面（随机开一局再随机走若干步），具名用例见 GameStateTests。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |])>]
module GameStateProperties =

    let private allActions (state: GameState) =
        GameState.legalActions state |> List.collect (fun choice -> choice.Actions)

    [<Property>]
    let ``任意时刻合法动作集非空，或这一局已终`` (state: GameState) =
        if GameState.isEnded state then
            List.isEmpty (GameState.legalActions state)
        else
            let choices = GameState.legalActions state

            not (List.isEmpty choices)
            && choices |> List.forall (fun choice -> not (List.isEmpty choice.Actions))

    [<Property>]
    let ``牌数守恒：各家手牌与河加上山上的牌恒为完整一副`` (state: GameState) =
        let everything =
            (GameState.players state
             |> List.collect (fun player -> PlayerState.hand player @ PlayerState.kawa player))
            @ Wall.tiles (GameState.wall state)

        List.length everything = Ruleset.wallSize ruleset
        && Tile.sort everything = Tile.sort (Ruleset.wallTiles ruleset)

    [<Property>]
    let ``等着打牌的那家 14 张，自摸和了的那家 14 张，其余各家 13 张`` (state: GameState) =
        let holdingFourteen =
            match GameState.phase state with
            | AwaitingDahai phase -> Some phase.Actor
            // 荣和的那张留在放铳者的河里，和了的那家仍是 13 张（牌数守恒也靠这条）。
            | Ended(KyokuEnd.Hora horas) ->
                horas
                |> List.tryPick (fun hora -> if hora.Actor = hora.Target then Some hora.Actor else None)
            | AwaitingResponse _
            | Ended(KyokuEnd.Ryuukyoku _) -> None

        GameState.players state
        |> List.mapi (fun seat player ->
            let expected =
                if Some seat = holdingFourteen then
                    ruleset.HaipaiSize + 1
                else
                    ruleset.HaipaiSize

            List.length (PlayerState.hand player) = expected)
        |> List.forall id

    [<Property>]
    let ``step 接受一个动作，当且仅当它在合法动作集里`` (state: GameState) (action: Action) =
        let legal = allActions state |> List.contains action

        match GameState.step state action with
        | Ok _ -> legal
        | Error _ -> not legal

    [<Property>]
    let ``合法动作集里的每个动作都推得动局面`` (state: GameState) =
        allActions state
        |> List.forall (fun action ->
            match GameState.step state action with
            | Ok(next, events) ->
                let movedOn =
                    next <> state && GameState.events next = GameState.events state @ events

                // 响应阶段的答复（宣言荣和或「过」）本身都不是既成事实：还有别家没答复时
                // 一个事件都不产出，**收齐最后一份答复的那一步**才裁决、才产出事件。
                let resolvesNow =
                    match GameState.phase state with
                    | AwaitingResponse phase -> List.length phase.Responses = 1
                    | AwaitingDahai _
                    | Ended _ -> true

                movedOn && (not (List.isEmpty events)) = resolvesNow
            | Error _ -> false)

    [<Property>]
    let ``响应阶段等的每一家都不振听、和了型成立、有役，且都能「过」`` (state: GameState) =
        match GameState.phase state with
        | AwaitingResponse phase ->
            phase.Responses
            |> List.forall (fun choice ->
                let canRon =
                    match GameState.player choice.Seat state with
                    | Some player ->
                        not (Furiten.blocksRon (PlayerState.furiten player))
                        && PlayerState.isAgariWith kindSet phase.Pai player
                    | None -> false

                // 无役不可和：被问到的每一家都算得出符与番。
                let hasYaku =
                    match GameState.horaOf choice.Seat state with
                    | Ok _ -> true
                    | Error _ -> false

                canRon
                && hasYaku
                && choice.Seat <> phase.Target
                && List.contains (Action.Hora(choice.Seat, phase.Target, phase.Pai)) choice.Actions
                && List.contains (Action.None choice.Seat) choice.Actions)
        | AwaitingDahai _
        | Ended _ -> true

    [<Property>]
    let ``和了收尾时授受把点数与供托一起守恒`` (state: GameState) =
        match GameState.horas state with
        | [] -> true
        | horas ->
            let kyotaku = context.Kyotaku * ruleset.RiichiStick

            // 一次和了的增减之和 = 它收走的供托；全部和了加起来正好把供托收干净。
            (horas |> List.sumBy (fun hora -> List.sum hora.Deltas)) = kyotaku
            // 逐条累加：最后一条就是这一局的最终点数。
            && (List.last horas).Scores = GameState.scores state
            && List.sum (GameState.scores state) = List.sum context.Scores + kyotaku
            // 和了者收、放铳者（或付家）付；自摸时其余三家都付。
            && horas
               |> List.forall (fun hora ->
                   List.item hora.Actor hora.Deltas > 0
                   && hora.Fu > 0
                   && hora.Fan > 0
                   && hora.HoraPoints > 0)

    [<Property>]
    let ``回放确定性：同一串动作重放出同一局面与同一事件流`` (seed: int) =
        let final, actions = record Kyoku.randomPlayer seed

        match replay actions (fst (start seed)) with
        | Error _ -> false
        | Ok replayed -> replayed = final && GameState.events replayed = GameState.events final

    [<Property>]
    let ``同一种子跑两次得到同一局`` (seed: int) =
        let first = runWith Kyoku.randomPlayer seed
        let second = runWith Kyoku.randomPlayer seed

        first = second && GameState.events first = GameState.events second

    [<Property>]
    let ``事件流的 JSON 往返不变`` (state: GameState) =
        GameState.events state
        |> List.forall (fun event ->
            Decode.fromString Event.decoder (Encode.toString 0 (Event.encoder event)) = Ok event)

    [<Property>]
    let ``荒牌流局的授受和为零，结算后点数就是局初点数加上增减`` (seed: int) =
        let final = runWith tenpaiSeeking seed
        let result = ryuukyokuOf final

        List.sum result.Deltas = 0
        && result.Scores = List.map2 (+) context.Scores result.Deltas
        && GameState.scores final = result.Scores

    [<Property>]
    let ``听牌的家收听牌料，不听的家付；全听或全不听时不授受`` (seed: int) =
        let result = ryuukyokuOf (runWith tenpaiSeeking seed)
        let tenpaiCount = result.Tenpais |> List.filter id |> List.length

        if tenpaiCount = 0 || tenpaiCount = List.length result.Tenpais then
            result.Deltas |> List.forall (fun delta -> delta = 0)
        else
            List.forall2 (fun tenpai delta -> if tenpai then delta > 0 else delta < 0) result.Tenpais result.Deltas
