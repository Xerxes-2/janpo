namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 和了型判定的具名用例：只判型，不含役与点数。
module AgariShapeTests =

    let private fourPlayer = TileKindSet.fourPlayer

    let private hand = HandFixture.hand

    let private classify (nakiCount: int) (notations: string) : AgariShape list =
        AgariShape.classify fourPlayer (hand nakiCount notations)

    [<Fact>]
    let ``四面子一雀头是一般型`` () =
        Assert.Equal<AgariShape list>([ Standard ], classify 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 5z")

    [<Fact>]
    let ``七组对子是七对子`` () =
        Assert.Equal<AgariShape list>([ Chiitoitsu ], classify 0 "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 7z 7z")

    [<Fact>]
    let ``二盃口同时是一般型与七对子`` () =
        Assert.Equal<AgariShape list>([ Standard; Chiitoitsu ], classify 0 "1p 1p 2p 2p 3p 3p 5p 5p 6p 6p 7p 7p 9p 9p")

    [<Fact>]
    let ``十三种幺九牌加一对是国士`` () =
        Assert.Equal<AgariShape list>([ Kokushi ], classify 0 "1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z 7z")

    [<Fact>]
    let ``副露的手也能成一般型`` () =
        Assert.Equal<AgariShape list>([ Standard ], classify 1 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 1p")
        Assert.Equal<AgariShape list>([ Standard ], classify 4 "1z 1z")

    [<Fact>]
    let ``差一张的手牌不是和了型`` () =
        Assert.Empty(classify 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z")
        Assert.Empty(classify 0 "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 7z")
        Assert.Empty(classify 0 "1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z")

    [<Fact>]
    let ``四面子加一组搭子不是和了型`` () =
        // 4 面子齐了但第五组是搭子而不是雀头：还没和。
        Assert.Empty(classify 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 4p 5p")

    [<Fact>]
    let ``副露过就不可能是七对子或国士`` () =
        Assert.Empty(classify 1 "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s")

    [<Fact>]
    let ``isAgari 与 classify 一致`` () =
        Assert.True(AgariShape.isAgari fourPlayer (hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 5z"))
        Assert.False(AgariShape.isAgari fourPlayer (hand 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"))

    [<Fact>]
    let ``和了型的中文渲染`` () =
        Assert.Equal("一般型", AgariShape.toDisplay Standard)
        Assert.Equal("七对子", AgariShape.toDisplay Chiitoitsu)
        Assert.Equal("国士无双", AgariShape.toDisplay Kokushi)
