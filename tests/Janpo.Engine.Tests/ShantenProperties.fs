namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.Xunit
open Janpo

/// Shanten 的不变量。具名用例见 ShantenTests，与现成实现的对拍见 ShantenOracleTests。
[<Properties(Arbitrary = [| typeof<TileArbitraries>; typeof<HandShapeArbitraries> |], MaxTest = 300)>]
module ShantenProperties =

    let private fourPlayer = TileKindSet.fourPlayer

    let private shanten (hand: HandShape) =
        Shanten.value (Shanten.calculate fourPlayer hand)

    // ---- 值域 ----

    [<Property>]
    let ``向听数落在 -1 与 8 之间`` (AnyHand hand) =
        let value = shanten hand
        value >= -1 && value <= 8

    [<Property>]
    let ``等摸的手牌不可能已经是和了型`` (AwaitingHand hand) = shanten hand >= 0

    [<Property>]
    let ``牌种集合与副露数不变时向听数是确定的`` (AnyHand hand) = shanten hand = shanten hand

    // ---- 摸打 ----

    [<Property>]
    let ``摸 X 打 X 后向听数不变`` (AwaitingHand hand) (tile: Tile) =
        match HandShape.add tile hand |> Result.bind (HandShape.remove tile) with
        | Ok restored -> shanten restored = shanten hand
        | Error _ -> HandShape.countOf tile hand = 4 // 手里已有四张，摸不进第五张

    [<Property>]
    let ``打 X 摸 X 后向听数不变`` (DrawnHand hand) (tile: Tile) =
        match HandShape.remove tile hand |> Result.bind (HandShape.add tile) with
        | Ok restored -> shanten restored = shanten hand
        | Error _ -> HandShape.countOf tile hand = 0 // 手里没有这张牌

    [<Property>]
    let ``摸一张之后向听数只会不变或减一`` (AwaitingHand hand) (tile: Tile) =
        match HandShape.add tile hand with
        | Error _ -> true
        | Ok drawn ->
            let before = shanten hand
            let after = shanten drawn
            after = before || after = before - 1

    [<Property>]
    let ``打一张之后向听数只会不变或加一`` (DrawnHand hand) (tile: Tile) =
        match HandShape.remove tile hand with
        | Error _ -> true
        | Ok discarded ->
            let before = shanten hand
            let after = shanten discarded
            after = before || after = before + 1

    [<Property>]
    let ``一次摸打之后向听数变化不超过一`` (AwaitingHand hand) (drawn: Tile) (discarded: Tile) =
        match HandShape.add drawn hand |> Result.bind (HandShape.remove discarded) with
        | Error _ -> true
        | Ok after -> abs (shanten after - shanten hand) <= 1

    // ---- 与和了型、有效牌的一致性 ----

    [<Property>]
    let ``搭出来的四面子一雀头一定是一般型和了牌`` (CompleteHand hand) =
        shanten hand = -1
        && List.contains Standard (AgariShape.classify fourPlayer hand)

    [<Property>]
    let ``和了型等价于向听数为 -1`` (AnyHand hand) =
        AgariShape.isAgari fourPlayer hand = (shanten hand = -1)

    [<Property>]
    let ``听牌的手牌一定有有效牌`` (AwaitingHand hand) =
        match Ukeire.calculate fourPlayer [] hand with
        | Error _ -> false
        | Ok ukeire -> not (Shanten.isTenpai ukeire.Shanten) || not (List.isEmpty ukeire.Tiles)

    [<Property>]
    let ``有效牌摸进来之后向听数确实下降`` (AwaitingHand hand) =
        match Ukeire.calculate fourPlayer [] hand with
        | Error _ -> false
        | Ok ukeire ->
            ukeire.Tiles
            |> List.forall (fun (tile, _) ->
                match HandShape.add tile hand with
                | Ok drawn -> shanten drawn = shanten hand - 1
                | Error _ -> false)

    [<Property>]
    let ``不在有效牌里的牌摸进来向听数不变`` (AwaitingHand hand) (tile: Tile) =
        match Ukeire.calculate fourPlayer [] hand, HandShape.add tile hand with
        | Ok ukeire, Ok drawn ->
            let listed =
                ukeire.Tiles |> List.exists (fun (candidate, _) -> candidate = Tile.deaka tile)

            listed || shanten drawn = shanten hand
        | _ -> true

    [<Property>]
    let ``听牌时的有效牌就是和了牌`` (AwaitingHand hand) =
        match Ukeire.calculate fourPlayer [] hand with
        | Error _ -> false
        | Ok ukeire ->
            not (Shanten.isTenpai ukeire.Shanten)
            || ukeire.Tiles
               |> List.forall (fun (tile, _) ->
                   match HandShape.add tile hand with
                   | Ok drawn -> AgariShape.isAgari fourPlayer drawn
                   | Error _ -> false)

    [<Property>]
    let ``有效牌的剩余枚数是四减去可见张数`` (AwaitingHand hand) =
        match Ukeire.calculate fourPlayer [] hand with
        | Error _ -> false
        | Ok ukeire ->
            ukeire.Tiles
            |> List.forall (fun (tile, remaining) -> remaining = 4 - HandShape.countOf tile hand)
