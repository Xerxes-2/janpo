namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 有效牌的具名用例。不变量见 ShantenProperties。
module UkeireTests =

    let private fourPlayer = TileKindSet.fourPlayer

    let private tiles = HandFixture.tiles
    let private hand = HandFixture.hand

    let private ukeire (visible: string) (nakiCount: int) (notations: string) : Ukeire =
        match Ukeire.calculate fourPlayer (tiles visible) (hand nakiCount notations) with
        | Ok ukeire -> ukeire
        | Error error -> failwith $"有效牌应当算得出：{UkeireError.toDisplay error}"

    [<Fact>]
    let ``单骑听牌的有效牌是那一张`` () =
        let result = ukeire "" 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
        Assert.Equal("5z", Ukeire.toMjai result)
        Assert.Equal(3, Ukeire.total result)
        Assert.Equal(0, Shanten.value result.Shanten)

    [<Fact>]
    let ``边张听只听得到一种`` () =
        // 1p2p 是边张：只听 3p，没有 0p 可补。
        let result = ukeire "" 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 5z 5z"
        Assert.Equal("3p", Ukeire.toMjai result)
        Assert.Equal(4, Ukeire.total result)

    [<Fact>]
    let ``两面听的有效牌是两种`` () =
        let result = ukeire "" 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 3p 4p 5z 5z"
        Assert.Equal("2p 5p", Ukeire.toMjai result)
        Assert.Equal(8, Ukeire.total result)

    [<Fact>]
    let ``有效牌按 mjai 顺序升序`` () =
        // 三副顺子 + 1p2p + 5s6s：只能组四组，缺雀头。补齐任一搭子或给它们撑出雀头都能进张。
        let result = ukeire "" 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 5s 6s"
        Assert.Equal(1, Shanten.value result.Shanten)
        Assert.Equal("1p 2p 3p 4s 5s 6s 7s", Ukeire.toMjai result)
        Assert.Equal(3 + 3 + 4 + 4 + 3 + 3 + 4, Ukeire.total result)

    [<Fact>]
    let ``剩余枚数扣掉手里与可见的张数`` () =
        // 5z 手里有一张、河里再见到两张，只剩一张。
        let result = ukeire "5z 5z" 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
        Assert.Equal<(Tile * int) list>([ List.item 31 Tile.kinds, 1 ], result.Tiles)
        Assert.Equal(1, Ukeire.total result)

    [<Fact>]
    let ``牌种已经全部可见时仍是有效牌但剩余零枚`` () =
        // 形态上还是听 5z，只是摸不到了：牌种照列，枚数为 0，`live` 会把它滤掉。
        let result = ukeire "5z 5z 5z" 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
        Assert.Equal("5z", Ukeire.toMjai result)
        Assert.Equal(0, Ukeire.total result)
        Assert.Equal(1, Ukeire.kindCount result)
        Assert.Empty(Ukeire.live result)

    [<Fact>]
    let ``一向听的有效牌覆盖所有能进的牌`` () =
        let result = ukeire "" 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 5z 6z"
        Assert.Equal("3p 5z 6z", Ukeire.toMjai result)
        Assert.Equal(1, Shanten.value result.Shanten)
        Assert.Equal(4 + 3 + 3, Ukeire.total result)

    [<Fact>]
    let ``副露手的有效牌`` () =
        let result = ukeire "" 1 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p"
        Assert.Equal("1p", Ukeire.toMjai result)
        Assert.Equal(3, Ukeire.total result)

    [<Fact>]
    let ``七对子听牌的有效牌`` () =
        let result = ukeire "" 0 "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 7z"
        Assert.Equal("7z", Ukeire.toMjai result)
        Assert.Equal(3, Ukeire.total result)

    [<Fact>]
    let ``国士十三面听的有效牌是十三种`` () =
        let result = ukeire "" 0 "1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z"
        Assert.Equal("1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z", Ukeire.toMjai result)
        Assert.Equal(13 * 3, Ukeire.total result)

    [<Fact>]
    let ``已摸进的手牌没有有效牌可言`` () =
        match Ukeire.calculate fourPlayer [] (hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 5z") with
        | Error(HandNotAwaitingDraw(14, 0)) -> ()
        | other -> failwith $"期望 HandNotAwaitingDraw，得到 {other}"

    [<Fact>]
    let ``可见张数超过四张是错误`` () =
        match Ukeire.calculate fourPlayer (tiles "5z 5z 5z 5z") (hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z") with
        | Error(VisibleKindOverflow(tile, 5)) -> Assert.Equal("5z", Tile.toMjai tile)
        | other -> failwith $"期望 VisibleKindOverflow，得到 {other}"

    [<Fact>]
    let ``和了型也没有有效牌可言`` () =
        // 14 张的和了牌不是「等摸」的手牌。
        match Ukeire.calculate fourPlayer [] (hand 0 "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 7z 7z") with
        | Error(HandNotAwaitingDraw(14, 0)) -> ()
        | other -> failwith $"期望 HandNotAwaitingDraw，得到 {other}"

    [<Fact>]
    let ``四张全在手里的牌不算有效牌`` () =
        // 6s 已经四张全在自己手里，摸不到第五张，它不能出现在有效牌里。
        let result = ukeire "" 0 "3m 4m 5m 6m 7m 8m 1p 2p 3p 6s 6s 6s 6s"
        Assert.Equal(1, Shanten.value result.Shanten)
        Assert.DoesNotContain("6s", Ukeire.toMjai result)

    [<Fact>]
    let ``有效牌的中文渲染`` () =
        let result = ukeire "" 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
        Assert.Equal("听牌，有效牌 3 枚：白(3)", Ukeire.toDisplay result)

    [<Fact>]
    let ``错误的中文说明`` () =
        Assert.Equal("有效牌只对等摸的手牌有定义：副露 1 组时暗牌应为 10 张，得到 11 张", UkeireError.toDisplay (HandNotAwaitingDraw(11, 1)))
