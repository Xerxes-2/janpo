namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.Xunit
open Janpo

/// 种子化发生器的不变量：确定性、取值范围、洗牌是排列。具名用例见 RngTests。
module RngProperties =

    [<Property>]
    let ``同一种子产出同一序列`` (seed: int) (PositiveInt bound) =
        Rng.nextBelow bound (Rng.ofSeed seed) = Rng.nextBelow bound (Rng.ofSeed seed)

    [<Property>]
    let ``取数落在 0 到 bound 之间`` (seed: int) (PositiveInt bound) =
        let value, _ = Rng.nextBelow bound (Rng.ofSeed seed)
        value >= 0 && value < bound

    [<Property>]
    let ``bound 不为正时恒取 0 且不推进发生器`` (seed: int) (NonNegativeInt excess) =
        let rng = Rng.ofSeed seed
        Rng.nextBelow -excess rng = (0, rng)

    [<Property>]
    let ``洗牌是输入的一个排列`` (seed: int) (items: int list) =
        let shuffled, _ = Rng.shuffle items (Rng.ofSeed seed)
        List.sort shuffled = List.sort items

    [<Property>]
    let ``同一种子洗出同一副牌`` (seed: int) (items: int list) =
        let first, firstRng = Rng.shuffle items (Rng.ofSeed seed)
        let second, secondRng = Rng.shuffle items (Rng.ofSeed seed)
        first = second && firstRng = secondRng

    [<Property>]
    let ``洗牌推进发生器，续洗得到不同结果`` (seed: int) =
        let deck = [ 1..64 ]
        let first, advanced = Rng.shuffle deck (Rng.ofSeed seed)
        let second, _ = Rng.shuffle deck advanced
        first <> second
