namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 宝牌的指示牌换算与计数。宝牌只计番，不是役——役的部分见 YakuTests。
module DoraTests =

    let private tile = AgariFixture.tile
    let private tiles = AgariFixture.tiles

    [<Fact>]
    let ``数牌的指示牌指向下一号，9 回到 1`` () =
        Assert.Equal(tile "2m", Dora.ofMarker (tile "1m"))
        Assert.Equal(tile "1m", Dora.ofMarker (tile "9m"))
        Assert.Equal(tile "6p", Dora.ofMarker (tile "5p"))
        Assert.Equal(tile "1s", Dora.ofMarker (tile "9s"))

    [<Fact>]
    let ``风牌与三元牌各自绕圈`` () =
        Assert.Equal(tile "2z", Dora.ofMarker (tile "1z"))
        Assert.Equal(tile "1z", Dora.ofMarker (tile "4z"))
        Assert.Equal(tile "6z", Dora.ofMarker (tile "5z"))
        Assert.Equal(tile "5z", Dora.ofMarker (tile "7z"))

    [<Fact>]
    let ``红宝牌的指示牌按对应正牌算`` () =
        Assert.Equal(tile "6m", Dora.ofMarker (tile "5mr"))

    [<Fact>]
    let ``一张牌被两个指示牌指到就算两番`` () =
        Assert.Equal(2, Dora.count (tiles "1m 1m") (tiles "2m 5p"))
        Assert.Equal(3, Dora.count (tiles "1m 4z") (tiles "2m 2m 1z 9s"))
        Assert.Equal(0, Dora.count [] (tiles "2m 5p"))

    [<Fact>]
    let ``红宝牌单独数`` () =
        Assert.Equal(2, Dora.akadoraCount (tiles "5mr 5pr 5s 5m"))
        Assert.Equal(0, Dora.akadoraCount (tiles "5m 5p"))

    [<Fact>]
    let ``红宝牌也吃表宝牌`` () =
        // 5pr 与 5p 是同一牌种：指示牌 4p 指到它，红与宝牌各算一番。
        Assert.Equal(1, Dora.count (tiles "4p") (tiles "5pr"))
        Assert.Equal(1, Dora.akadoraCount (tiles "5pr"))
