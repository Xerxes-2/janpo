namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 种子化发生器的具名用例。不变量见 RngProperties。
module RngTests =

    let private take (count: int) (bound: int) (rng: Rng) =
        (([], rng), [ 1..count ])
        ||> List.fold (fun (drawn, current) _ ->
            let value, advanced = Rng.nextBelow bound current
            value :: drawn, advanced)
        |> fst
        |> List.rev

    /// 固定的取数序列。具体数值本身没有含义（伪随机流），钉住它是为了
    /// **跨目标复现**：M1 把同一份 F# 编译成 JS 之后，这几行必须一字不差，
    /// 否则黄金用例与 soak 的种子在两侧就不是同一件事了。
    [<Fact>]
    let ``固定种子产出固定的取数序列`` () =
        Assert.Equal<int list>([ 21; 51; 31; 12; 31; 65; 68; 14 ], Rng.ofSeed 0 |> take 8 100)
        Assert.Equal<int list>([ 13; 3; 45; 0; 1; 70; 71; 76 ], Rng.ofSeed 42 |> take 8 100)
        Assert.Equal<int list>([ 87; 46; 35; 0; 88; 35; 27; 9 ], Rng.ofSeed -1 |> take 8 100)

    [<Fact>]
    let ``种子 0 不会退化成常数序列`` () =
        let drawn = Rng.ofSeed 0 |> take 20 1000
        Assert.True(drawn |> List.distinct |> List.length > 1, $"种子 0 的取数退化了：{drawn}")

    [<Fact>]
    let ``不同种子产出不同的洗牌结果`` () =
        let deck = [ 1..136 ]

        let shuffles =
            [ for seed in -200 .. 200 -> Rng.ofSeed seed |> Rng.shuffle deck |> fst ]

        Assert.Equal(List.length shuffles, shuffles |> List.distinct |> List.length)

    [<Fact>]
    let ``取数落在区间内且区间为 1 时不消耗随机数`` () =
        let rng = Rng.ofSeed 7
        Assert.Equal<int * Rng>((0, rng), Rng.nextBelow 1 rng)
        Assert.Equal<int * Rng>((0, rng), Rng.nextBelow 0 rng)
        Assert.Equal<int * Rng>((0, rng), Rng.nextBelow -5 rng)

    [<Fact>]
    let ``取数没有明显的取模偏差`` () =
        let counts =
            (([], Rng.ofSeed 7), [ 1..40000 ])
            ||> List.fold (fun (drawn, current) _ ->
                let value, advanced = Rng.nextBelow 4 current
                value :: drawn, advanced)
            |> fst
            |> List.countBy id
            |> List.sortBy fst

        Assert.Equal<int list>([ 0; 1; 2; 3 ], counts |> List.map fst)

        for _, count in counts do
            Assert.InRange(count, 9500, 10500)

    [<Fact>]
    let ``洗牌不增删元素`` () =
        let deck = [ 1..136 ]
        let shuffled, _ = Rng.ofSeed 42 |> Rng.shuffle deck
        Assert.Equal<int list>(deck, List.sort shuffled)
        Assert.NotEqual<int list>(deck, shuffled)
