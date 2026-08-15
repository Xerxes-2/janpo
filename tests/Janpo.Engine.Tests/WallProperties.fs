namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Janpo

/// 牌山的不变量：牌数守恒、构成与规则集一致、种子决定一切。具名用例见 WallTests。
[<Properties(Arbitrary = [| typeof<RulesetArbitraries> |])>]
module WallProperties =

    [<Property>]
    let ``任意种子建出的牌山就是规则集给的那副牌`` (ruleset: Ruleset) (seed: int) =
        let wall, _ = Wall.build ruleset (Rng.ofSeed seed)
        Tile.sort (Wall.tiles wall) = Tile.sort (Ruleset.wallTiles ruleset)

    [<Property>]
    let ``任意种子：可摸区加王牌等于牌山总张数`` (ruleset: Ruleset) (seed: int) =
        let wall, _ = Wall.build ruleset (Rng.ofSeed seed)
        Wall.remaining wall + List.length (Wall.deadWall wall) = Ruleset.wallSize ruleset

    [<Property>]
    let ``任意种子：配牌之后手牌与山上的牌合起来仍是完整一副`` (ruleset: Ruleset) (seed: int) =
        let wall, _ = Wall.build ruleset (Rng.ofSeed seed)

        match Wall.deal ruleset 0 wall with
        | None -> false
        | Some(hands, rest) -> Tile.sort (List.concat hands @ Wall.tiles rest) = Tile.sort (Ruleset.wallTiles ruleset)

    [<Property>]
    let ``任意种子：每家配牌张数相同且互不重叠`` (ruleset: Ruleset) (seed: int) =
        let wall, _ = Wall.build ruleset (Rng.ofSeed seed)

        match Wall.deal ruleset 0 wall with
        | None -> false
        | Some(hands, _) ->
            List.length hands = ruleset.SeatCount
            && hands |> List.forall (fun hand -> List.length hand = ruleset.HaipaiSize)
            && List.length (List.concat hands) = Ruleset.haipaiTotal ruleset

    [<Property>]
    let ``任意种子：每家配牌都按 mjai 顺序排好`` (ruleset: Ruleset) (seed: int) =
        let wall, _ = Wall.build ruleset (Rng.ofSeed seed)

        match Wall.deal ruleset 0 wall with
        | None -> false
        | Some(hands, _) -> hands |> List.forall (fun hand -> hand = Tile.sort hand)

    [<Property>]
    let ``同一种子建出同一座牌山`` (ruleset: Ruleset) (seed: int) =
        let first, firstRng = Wall.build ruleset (Rng.ofSeed seed)
        let second, secondRng = Wall.build ruleset (Rng.ofSeed seed)
        first = second && firstRng = secondRng

    [<Property>]
    let ``任意种子：摸牌只动可摸区，不动王牌`` (ruleset: Ruleset) (seed: int) =
        let wall, _ = Wall.build ruleset (Rng.ofSeed seed)

        match Wall.draw wall with
        | None -> false
        | Some(tile, rest) ->
            Wall.remaining rest = Wall.remaining wall - 1
            && Wall.deadWall rest = Wall.deadWall wall
            && Tile.sort (tile :: Wall.tiles rest) = Tile.sort (Wall.tiles wall)
