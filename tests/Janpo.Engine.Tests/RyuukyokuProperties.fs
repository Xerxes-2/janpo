namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 流局的不变量。**七种形态各出一条轨迹**：随机取样跑不出途中流局（备注 N-8），
/// 因此这里的「全部形态」是摊好的剧本，而不是碰运气碰出来的。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |])>]
module RyuukyokuProperties =

    let private pai (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"{notation} 应当是合法记法，却得到 {error}"

    let private stepped (state: GameState) (action: Action) =
        match GameState.step state action with
        | Ok(next, _) -> next
        | Error illegal -> failwith $"这个动作应当合法，却被拒：{IllegalAction.toDisplay illegal}"

    let private atTheTripleRon () =
        startScripted tripleRonScript
        |> driveUntil passive (fun state ->
            match GameState.phase state with
            | AwaitingResponse phase -> phase.Pai = pai "4p"
            | AwaitingDahai _
            | Ended _ -> false)

    /// 七种形态各一条**跑到终局**的轨迹。
    let private endedKyokus: (RyuukyokuReason * GameState) list =
        [
            Fanpai, runWith tenpaiSeeking 3

            NagashiMangan,
            (startFromWall ruleset context (nagashiManganWall ruleset None)
             |> driveUntil passive GameState.isEnded)

            KyuushuKyuuhai, stepped (startScripted kyuushuScript) (Action.Ryuukyoku(seat 0))

            SuufonRenda,
            ([ 1; 2; 3 ]
             |> List.fold
                 (fun current index -> stepped current (Action.Dahai(seat index, pai "1z", false)))
                 (stepped (startScripted suufonRendaScript) (Action.Dahai(seat 0, pai "1z", true))))

            Suukaikan,
            (startScriptedRinshan "2m 3m 9p 3z" suukaikanScript
             |> driveUntil kanSeeking GameState.isEnded)

            SuuchaRiichi, (startScripted suuchaRiichiScript |> driveUntil riichiSeeking GameState.isEnded)

            SanchaHora,
            (atTheTripleRon ()
             |> fun state -> stepped state (Action.Hora(seat 1, seat 0, pai "4p"))
             |> fun state -> stepped state (Action.Hora(seat 2, seat 0, pai "4p"))
             |> fun state -> stepped state (Action.Hora(seat 3, seat 0, pai "4p")))
        ]

    [<Fact>]
    let ``七种流局形态各有一条轨迹跑到终局，一种不漏`` () =
        Assert.Equal<RyuukyokuReason list>(RyuukyokuReason.all, endedKyokus |> List.map fst)

        for expected, state in endedKyokus do
            Assert.Equal<RyuukyokuReason>(expected, (ryuukyokuOf state).Reason)

    [<Fact>]
    let ``任何形态结束后，四家点数与供托之和不变`` () =
        let initial = List.sum context.Scores + Ruleset.kyotakuScore ruleset context.Kyotaku

        for reason, state in endedKyokus do
            let total =
                List.sum (GameState.scores state)
                + Ruleset.kyotakuScore ruleset (GameState.kyotaku state)

            Assert.Equal(initial, total)
            // 授受本身也是零和：供托只在和了那一步换手，流局时一根都不动。
            Assert.Equal(0, List.sum (ryuukyokuOf state).Deltas)

            Assert.True(
                GameState.scores state = (ryuukyokuOf state).Scores,
                $"{RyuukyokuReason.toMjai reason} 的 scores 应当就是终局点数"
            )

    [<Fact>]
    let ``途中流局一律不授受，荒牌与流し満貫才动点数`` () =
        for reason, state in endedKyokus do
            let deltas = (ryuukyokuOf state).Deltas

            if RyuukyokuReason.isAbortive reason then
                Assert.Equal<int list>([ 0; 0; 0; 0 ], deltas)

                // 点数只可能因为立直棒变过（四家立直那一局四根都出了），授受本身一分不动。
                Assert.Equal(
                    List.sum context.Scores - acceptedRiichiCount state * ruleset.RiichiBou,
                    List.sum (GameState.scores state)
                )

    [<Fact>]
    let ``每条轨迹的事件流都以 ryukyoku 收尾，且 JSON 往返不变`` () =
        for _, state in endedKyokus do
            let events = GameState.events state

            match List.last events with
            | Ryuukyoku _ -> ()
            | other -> failwith $"一局应当以 ryukyoku 收尾，实际是 {other}"

            for event in events do
                Assert.Equal<Result<Event, string>>(
                    Ok event,
                    Decode.fromString Event.decoder (Encode.toString 0 (Event.encoder event))
                )

    [<Property>]
    let ``九种九牌的判据只看第一巡与幺九牌种数`` (state: GameState) =
        match GameState.phase state with
        | AwaitingDahai phase ->
            let hand =
                GameState.player phase.Actor state
                |> Option.map PlayerState.hand
                |> Option.defaultValue []

            // 进了合法动作集 ⟹ 幺九牌够种数（反过来还要第一巡，属性只验必要条件这一半）。
            phase.Actions
            |> List.forall (fun action ->
                match action with
                | Action.Ryuukyoku _ -> Ryuukyoku.yaochuuKinds hand >= ruleset.KyuushuKinds
                | Action.Dahai _
                | Action.Hora _
                | Action.Pon _
                | Action.Chi _
                | Action.Ankan _
                | Action.Kakan _
                | Action.Minkan _
                | Action.Riichi _
                | Action.None _ -> true)
        | AwaitingResponse phase ->
            // 九种九牌不是响应：响应阶段的动作集里没有它。
            phase.Responses
            |> List.collect (fun choice -> choice.Actions)
            |> List.forall (fun action ->
                match action with
                | Action.Ryuukyoku _ -> false
                | Action.Dahai _
                | Action.Hora _
                | Action.Pon _
                | Action.Chi _
                | Action.Ankan _
                | Action.Kakan _
                | Action.Minkan _
                | Action.Riichi _
                | Action.None _ -> true)
        | Ended _ -> true
