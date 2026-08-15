namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// HandShape 的构造与拆解：具名用例。
module HandShapeTests =

    let private hand = HandFixture.hand

    let private error (nakiCount: int) (notations: string) : HandShapeError =
        match HandShape.create nakiCount (HandFixture.tiles notations) with
        | Ok _ -> failwith "手牌本应被拒绝"
        | Error error -> error

    [<Fact>]
    let ``门清 13 张与 14 张都收`` () =
        Assert.Equal(13, HandShape.concealedCount (hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"))
        Assert.Equal(14, HandShape.concealedCount (hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 5z"))

    [<Fact>]
    let ``副露数决定暗牌张数`` () =
        Assert.Equal(10, HandShape.concealedCount (hand 1 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p"))
        Assert.Equal(2, HandShape.concealedCount (hand 4 "1m 1m"))

    [<Fact>]
    let ``暗牌张数与副露数对不上就是错误`` () =
        Assert.Equal(ConcealedCountMismatch(13, 1), error 1 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z")
        Assert.Equal(ConcealedCountMismatch(0, 0), error 0 "")

    [<Fact>]
    let ``副露数只能是 0 到 4`` () =
        Assert.Equal(NakiCountOutOfRange 5, error 5 "1m")
        Assert.Equal(NakiCountOutOfRange -1, error -1 "1m")

    [<Fact>]
    let ``同一种牌超过 4 张就是错误`` () =
        match error 0 "1m 1m 1m 1m 1m 2p 3p 4p 5p 6p 7p 8p 9p" with
        | TileKindOverflow(tile, count) ->
            Assert.Equal("1m", Tile.toMjai tile)
            Assert.Equal(5, count)
        | other -> failwith $"期望 TileKindOverflow，得到 {other}"

    [<Fact>]
    let ``红宝牌与对应正牌合并成同一牌种`` () =
        let withAkadora = hand 0 "5m 5mr 5m 5m 1p 2p 3p 4p 5p 6p 7p 8p 9p"
        Assert.Equal(4, HandShape.countOf (List.item 4 Tile.kinds) withAkadora)
        Assert.Equal("5m 5m 5m 5m 1p 2p 3p 4p 5p 6p 7p 8p 9p", HandShape.tiles withAkadora |> Tile.toMjaiMany)

    [<Fact>]
    let ``暗牌按 mjai 顺序升序取出`` () =
        Assert.Equal(
            "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z",
            hand 0 "5z 3p 2p 1p 9m 8m 7m 6m 5m 4m 3m 2m 1m"
            |> HandShape.tiles
            |> Tile.toMjaiMany
        )

    [<Fact>]
    let ``等摸的手牌是 3n 加 1 张`` () =
        Assert.True(HandShape.isAwaitingDraw (hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"))
        Assert.False(HandShape.isAwaitingDraw (hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 5z"))
        Assert.True(HandShape.isAwaitingDraw (hand 2 "1m 2m 3m 4m 5m 6m 7m"))

    [<Fact>]
    let ``摸一张再打掉同一张回到原形态`` () =
        let before = hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
        let drawn = List.head Tile.kinds

        match HandShape.add drawn before |> Result.bind (HandShape.remove drawn) with
        | Ok after -> Assert.Equal<Tile list>(HandShape.tiles before, HandShape.tiles after)
        | Error error -> failwith $"摸打往返本应成立：{HandShapeError.toDisplay error}"

    [<Fact>]
    let ``往已摸进的手牌里再摸一张会被拒绝`` () =
        let drawn = hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 5z"

        match HandShape.add (List.head Tile.kinds) drawn with
        | Error(ConcealedCountMismatch(15, 0)) -> ()
        | other -> failwith $"期望张数错误，得到 {other}"

    [<Fact>]
    let ``打掉手里没有的牌会被拒绝`` () =
        let drawn = hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 5z"

        match HandShape.remove (List.item 33 Tile.kinds) drawn with
        | Error(TileNotInHand tile) -> Assert.Equal("7z", Tile.toMjai tile)
        | other -> failwith $"期望 TileNotInHand，得到 {other}"

    [<Fact>]
    let ``错误的中文说明`` () =
        Assert.Equal("副露数只能是 0-4，得到 5", HandShapeError.toDisplay (NakiCountOutOfRange 5))
        Assert.Equal("副露 1 组时暗牌应为 10 或 11 张，得到 13 张", HandShapeError.toDisplay (ConcealedCountMismatch(13, 1)))
        Assert.Equal("手里没有中", HandShapeError.toDisplay (TileNotInHand(List.item 33 Tile.kinds)))

    [<Fact>]
    let ``手牌的中文渲染带副露组数`` () =
        Assert.Equal("1万 2万 3万 4万 5万 6万 7万（副露 2 组）", HandShape.toDisplay (hand 2 "1m 2m 3m 4m 5m 6m 7m"))

        Assert.Equal(
            "1万 2万 3万 4万 5万 6万 7万 8万 9万 1筒 2筒 3筒 白",
            HandShape.toDisplay (hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z")
        )

        Assert.Equal(
            "1万 2万 3万 4万 5万 6万 7万 8万 9万 1筒（副露 1 组）",
            HandShape.toDisplay (hand 1 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p")
        )
