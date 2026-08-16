namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 危险度的不变量（票 25）。具名用例钉的是判据（哪张成筋、哪张成壁），
/// 这里钉的是**排序本身**：名次的形状、现物的位置，以及「没人做牌就不给排序」。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |], Parallelism = 4)>]
module DangerProperties =

    /// 正在被问的每个座位：它的观测与它这一手打得出去的那几个牌种。
    let private askedSeats (state: GameState) : (Observation * Tile list) list =
        GameState.legalActions state
        |> List.choose (fun choice ->
            Observation.ofState choice.Seat state
            |> Option.map (fun observation ->
                let dahai =
                    choice.Actions
                    |> List.choose (fun action ->
                        match action with
                        | Action.Dahai(_, pai, _) -> Some(Tile.deaka pai)
                        | _ -> None)

                observation, List.distinct dahai))

    let private rankings (state: GameState) : (Observation * Danger list) list =
        askedSeats state
        |> List.map (fun (observation, dahai) -> observation, Danger.rank observation dahai)

    [<Property>]
    let ``任意局面，有威胁的家才有排序，且每张可打之牌恰好一条`` (state: GameState) =
        askedSeats state
        |> List.forall (fun (observation, tiles) ->
            let ranking = Danger.rank observation tiles

            if List.isEmpty (Danger.threats observation) then
                List.isEmpty ranking
            else
                let ranked = ranking |> List.map (fun danger -> danger.Pai) |> List.sort
                ranked = List.sort tiles)

    [<Property>]
    let ``任意局面，名次从 1 起、不下降，并列的共用一个名次`` (state: GameState) =
        rankings state
        |> List.forall (fun (_, ranking) ->
            match ranking with
            | [] -> true
            | first :: _ ->
                let ranks = ranking |> List.map (fun danger -> danger.Rank)

                // 排序是从安全到危险，因此名次沿着列表不下降；名次是「排在前面的张数 + 1」，
                // 所以它不会超过总张数。
                first.Rank = 1
                && ranks = List.sort ranks
                && List.max ranks <= List.length ranking)

    [<Property>]
    let ``任意局面，现物排在非现物之前`` (state: GameState) =
        // CONTEXT.md：现物对那一家绝对安全。**宝牌也不越这条线**——宝牌只在同档内破平。
        rankings state
        |> List.forall (fun (_, ranking) ->
            let genbutsu, rest =
                ranking |> List.partition (fun danger -> danger.Tier = DangerTier.Genbutsu)

            match genbutsu, rest with
            | [], _
            | _, [] -> true
            | _, _ ->
                let last = genbutsu |> List.map (fun danger -> danger.Rank) |> List.max
                let first = rest |> List.map (fun danger -> danger.Rank) |> List.min
                last < first)

    [<Property>]
    let ``任意局面，现物档意味着对每一家都是现物`` (state: GameState) =
        // 档位取最危险的那一家：只要有一家没打过这张牌，它就不是现物档。
        askedSeats state
        |> List.forall (fun (observation, tiles) ->
            let threats = Danger.threats observation |> List.length

            Danger.rank observation tiles
            |> List.forall (fun danger ->
                let genbutsu =
                    danger.Reasons
                    |> List.sumBy (fun reason ->
                        match reason with
                        | DangerReason.Genbutsu _ -> 1
                        | _ -> 0)

                match danger.Tier with
                | DangerTier.Genbutsu -> genbutsu = threats
                | DangerTier.Suji
                | DangerTier.Kabe
                | DangerTier.NoEvidence -> genbutsu < threats))

    [<Property>]
    let ``任意局面，筋与壁两档都说得出理由`` (state: GameState) =
        rankings state
        |> List.forall (fun (_, ranking) ->
            ranking
            |> List.forall (fun danger ->
                let says (kind: string) =
                    danger.Reasons |> List.exists (fun reason -> DangerReason.toWire reason = kind)

                match danger.Tier with
                | DangerTier.Suji -> says "suji"
                | DangerTier.Kabe -> says "kabe"
                | DangerTier.Genbutsu -> says "genbutsu"
                | DangerTier.NoEvidence -> true))
