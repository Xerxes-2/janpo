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
    let ``等着打牌的那家 14 张，其余各家 13 张`` (state: GameState) =
        let awaiting =
            match GameState.phase state with
            | AwaitingDahai phase -> Some phase.Actor
            | AwaitingResponse _
            | Ended _ -> None

        GameState.players state
        |> List.mapi (fun seat player ->
            let expected =
                if Some seat = awaiting then
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
                not (List.isEmpty events)
                && GameState.events next = GameState.events state @ events
            | Error _ -> false)

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
