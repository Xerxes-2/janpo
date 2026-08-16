namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 副露的构造与拆解。它是 07（役）、10（碰吃）、11（杠）共用的类型。
module NakiTests =

    let private tiles = AgariFixture.tiles
    let private tile = AgariFixture.tile

    [<Fact>]
    let ``碰要两张同种牌`` () =
        let naki = AgariFixture.pon (seat 1) "5z" "5z 5z"
        Assert.Equal(NakiKind.Pon, Naki.kind naki)
        Assert.Equal<Tile list>(tiles "5z 5z 5z", Naki.tiles naki)
        Assert.Equal(Some(tile "5z"), Naki.taken naki)
        Assert.Equal(Some(seat 1), Naki.target naki)
        Assert.False(Naki.isConcealed naki)
        Assert.False(Naki.isKan naki)

        Assert.Equal(Error(NakiTilesNotSameKind(tiles "5z 5z 6z")), Naki.pon (seat 1) (tile "5z") (tiles "5z 6z"))
        Assert.Equal(Error(NakiConsumedCountMismatch(NakiKind.Pon, 1)), Naki.pon (seat 1) (tile "5z") (tiles "5z"))

    [<Fact>]
    let ``吃要三张连号的同花色牌`` () =
        let naki = AgariFixture.chi (seat 3) "3m" "4m 5m"
        Assert.Equal(NakiKind.Chi, Naki.kind naki)
        Assert.Equal<Tile list>(tiles "3m 4m 5m", Naki.tiles naki)

        Assert.Equal(Error(NakiTilesNotRun(tiles "3m 4m 6m")), Naki.chi (seat 3) (tile "3m") (tiles "4m 6m"))
        Assert.Equal(Error(NakiTilesNotRun(tiles "3m 4m 5p")), Naki.chi (seat 3) (tile "3m") (tiles "4m 5p"))
        Assert.Equal(Error(NakiTilesNotRun(tiles "1z 2z 3z")), Naki.chi (seat 3) (tile "1z") (tiles "2z 3z"))
        // 跨花色的「连号」：8s 9s 1z 的牌种索引连着，但不成顺子。
        Assert.Equal(Error(NakiTilesNotRun(tiles "8s 9s 1z")), Naki.chi (seat 3) (tile "8s") (tiles "9s 1z"))

    [<Fact>]
    let ``三种杠`` () =
        let ankan = AgariFixture.ankan "1z 1z 1z 1z"
        Assert.True(Naki.isKan ankan)
        Assert.True(Naki.isConcealed ankan)
        Assert.Equal(None, Naki.taken ankan)
        Assert.Equal(None, Naki.target ankan)

        let minkan = AgariFixture.minkan (seat 2) "9m" "9m 9m 9m"
        Assert.True(Naki.isKan minkan)
        Assert.False(Naki.isConcealed minkan)
        Assert.Equal(Some(seat 2), Naki.target minkan)

        let kakan = AgariFixture.kakan "5z" (AgariFixture.pon (seat 1) "5z" "5z 5z")
        Assert.Equal(NakiKind.Kakan, Naki.kind kakan)
        Assert.True(Naki.isKan kakan)
        Assert.False(Naki.isConcealed kakan)
        // 加杠记着原本碰的来源，责任支付要用（11 票）。
        Assert.Equal(Some(seat 1), Naki.target kakan)
        Assert.Equal<Tile list>(tiles "5z 5z 5z 5z", Naki.tiles kakan)

    [<Fact>]
    let ``加杠的底必须是同种牌的碰`` () =
        let pon = AgariFixture.pon (seat 1) "5z" "5z 5z"
        Assert.Equal(Error(KakanBaseMismatch(tiles "6z 5z 5z 5z")), Naki.kakan (tile "6z") pon)

        let chi = AgariFixture.chi (seat 3) "3m" "4m 5m"
        Assert.Equal(Error(KakanBaseMismatch(tiles "3m 3m 4m 5m")), Naki.kakan (tile "3m") chi)

    [<Fact>]
    let ``副露对应的面子只有暗杠算暗`` () =
        Assert.Equal(Mentsu.koutsu false (tile "5z"), Naki.mentsu (AgariFixture.pon (seat 1) "5z" "5z 5z"))

        Assert.Equal(Mentsu.shuntsu false (tile "3m"), Some(Naki.mentsu (AgariFixture.chi (seat 3) "5m" "3m 4m")))

        Assert.Equal(Mentsu.kantsu true (tile "1z"), Naki.mentsu (AgariFixture.ankan "1z 1z 1z 1z"))
        Assert.Equal(Mentsu.kantsu false (tile "9m"), Naki.mentsu (AgariFixture.minkan (seat 2) "9m" "9m 9m 9m"))

    [<Fact>]
    let ``红宝牌在副露里留着`` () =
        let naki = AgariFixture.pon (seat 1) "5p" "5pr 5p"
        Assert.Equal<Tile list>(tiles "5p 5p 5pr", Naki.tiles naki)
        Assert.Equal(1, Dora.akadoraCount (Naki.tiles naki))
        // 面子的代表牌一律去红。
        Assert.Equal(Mentsu.koutsu false (tile "5p"), Naki.mentsu naki)

    [<Fact>]
    let ``副露的中文渲染`` () =
        Assert.Equal("碰中中中", Naki.toDisplay (AgariFixture.pon (seat 1) "7z" "7z 7z"))
        Assert.Equal("吃3万4万5万", Naki.toDisplay (AgariFixture.chi (seat 3) "3m" "4m 5m"))
        Assert.Equal("暗杠东东东东", Naki.toDisplay (AgariFixture.ankan "1z 1z 1z 1z"))
