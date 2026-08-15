namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Janpo

/// 役种判定的不变量。具名用例见 YakuTests。
[<Properties(Arbitrary = [| typeof<YakuArbitraries> |])>]
module YakuProperties =

    let private ruleset = Ruleset.yonma

    let private isYakuman (value: YakuValue) =
        match value with
        | YakuValue.Yakuman _ -> true
        | YakuValue.Han _ -> false

    [<Property>]
    let ``能和的手牌至少有一番或一倍役满`` (case: AgariCase) =
        let (AgariCase(hand, context)) = case

        match Yaku.detect ruleset context hand with
        | Error _ -> true
        | Ok tally -> YakuTally.han tally >= 1 || YakuTally.yakuman tally >= 1

    [<Property>]
    let ``门清限定役不会出现在副露的手牌里`` (case: AgariCase) =
        let (AgariCase(hand, context)) = case

        AgariHand.isMenzen hand
        || (match Yaku.detect ruleset context hand with
            | Error _ -> true
            | Ok tally -> YakuTally.yaku tally |> List.forall (Yaku.isMenzenOnly >> not))

    [<Property>]
    let ``选中的读法就是番数最高的那一个`` (case: AgariCase) =
        let (AgariCase(hand, context)) = case
        let all = Yaku.candidates ruleset context hand

        match Yaku.detect ruleset context hand, all with
        | Error _, [] -> true
        | Error _, _ -> false
        | Ok _, [] -> false
        | Ok tally, _ ->
            let best = all |> List.map YakuTally.yakuman |> List.max

            let bestHan =
                all
                |> List.filter (fun one -> YakuTally.yakuman one = best)
                |> List.map YakuTally.han
                |> List.max

            YakuTally.yakuman tally = best && YakuTally.han tally = bestHan

    [<Property>]
    let ``宝牌指示牌不影响役的成立`` (case: AgariCase) =
        let (AgariCase(hand, context)) = case

        let without =
            { context with
                DoraMarkers = []
                UraDoraMarkers = []
            }

        let yakuOf (context: YakuContext) =
            match Yaku.detect ruleset context hand with
            | Error _ -> []
            | Ok tally -> YakuTally.yaku tally

        yakuOf context = yakuOf without

    [<Property>]
    let ``役满成立时役表里全是役满`` (case: AgariCase) =
        let (AgariCase(hand, context)) = case

        match Yaku.detect ruleset context hand with
        | Error _ -> true
        | Ok tally ->
            YakuTally.yakuman tally = 0
            || tally.Yaku |> List.forall (fun (_, value) -> isYakuman value)

    [<Property>]
    let ``每一种面子分解都用光手上的牌`` (case: AgariCase) =
        let (AgariCase(hand, _)) = case
        let expected = AgariHand.tiles hand |> List.map Tile.deaka |> Tile.sort

        MentsuBreakdown.enumerate hand
        |> List.forall (fun breakdown ->
            let tiles =
                (MentsuBreakdown.mentsu breakdown |> List.collect Mentsu.tiles)
                @ List.replicate 2 (MentsuBreakdown.jantou breakdown)

            Tile.sort tiles = expected)

    [<Property>]
    let ``一般型和了必然有面子分解，其它型必然没有`` (case: AgariCase) =
        let (AgariCase(hand, context)) = case

        match Yaku.detect ruleset context hand with
        | Error _ -> true
        | Ok tally ->
            match tally.Shape, tally.Breakdown with
            | Standard, Some _ -> true
            | Standard, None -> false
            | _, Some _ -> false
            | _, None -> true
