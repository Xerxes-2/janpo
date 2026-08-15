namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// Shanten 的具名用例。随机对拍见 ShantenOracleTests，不变量见 ShantenProperties。
module ShantenTests =

    let private fourPlayer = TileKindSet.fourPlayer

    let private hand = HandFixture.hand

    /// 四麻牌种集合下的向听数。
    let private shanten (nakiCount: int) (notations: string) : int =
        Shanten.value (Shanten.calculate fourPlayer (hand nakiCount notations))

    // ---- 数值约定 ----

    [<Fact>]
    let ``听牌是 0 和了型是 -1`` () =
        Assert.Equal(0, Shanten.value Shanten.tenpai)
        Assert.Equal(-1, Shanten.value Shanten.agari)
        Assert.True(Shanten.isTenpai Shanten.tenpai)
        Assert.False(Shanten.isTenpai Shanten.agari)
        Assert.True(Shanten.isAgari Shanten.agari)
        Assert.False(Shanten.isAgari Shanten.tenpai)

    [<Fact>]
    let ``向听数的中文渲染`` () =
        Assert.Equal("和了", Shanten.toDisplay Shanten.agari)
        Assert.Equal("听牌", Shanten.toDisplay Shanten.tenpai)

        Assert.Equal(
            "3 向听",
            Shanten.toDisplay (Shanten.calculate fourPlayer (hand 0 "1m 1m 3m 5m 7m 9m 1p 3p 5p 7p 9p 1s 3s"))
        )

    // ---- 一般型 ----

    [<Fact>]
    let ``四面子一雀头是和了型`` () =
        Assert.Equal(-1, shanten 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z 5z")
        Assert.Equal(-1, shanten 0 "1m 1m 1m 2m 3m 4m 5m 6m 7m 8m 9m 9m 9m 9m")

    [<Fact>]
    let ``差一张单骑是听牌`` () =
        Assert.Equal(0, shanten 0 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z")

    [<Fact>]
    let ``一般型的最坏情况是 8 向听`` () =
        let scattered = hand 0 "1m 4m 7m 1p 4p 7p 1s 4s 7s 1z 2z 3z 4z"
        Assert.Equal(8, Shanten.value (Shanten.standard fourPlayer scattered))

    [<Fact>]
    let ``嵌张要靠中间那张补上`` () =
        // 1m3m 是嵌张：四麻里 2m 存在，它算一组搭子，配上三副顺子与雀头就是听牌。
        Assert.Equal(0, shanten 0 "1m 3m 1p 2p 3p 4p 5p 6p 7p 8p 9p 1s 1s")

    // ---- 七对子与国士 ----

    [<Fact>]
    let ``七对子听牌与和了`` () =
        Assert.Equal(0, shanten 0 "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 7z")
        Assert.Equal(-1, shanten 0 "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 7z 7z")

    [<Fact>]
    let ``七对子不把四张同种算成两对`` () =
        // 1m 四张只能算一对，另外两张对七对子毫无用处：三对七种，三向听。
        let fourCopies = hand 0 "1m 1m 1m 1m 2p 2p 3s 3s 5z 6z 7z 1z 2z"

        match Shanten.chiitoitsu fourPlayer fourCopies with
        | Some shanten -> Assert.Equal(3, Shanten.value shanten)
        | None -> failwith "门清手牌应当算得出七对子向听"

    [<Fact>]
    let ``国士无双十三面听与和了`` () =
        Assert.Equal(0, shanten 0 "1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z")
        Assert.Equal(-1, shanten 0 "1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z 7z")

    [<Fact>]
    let ``三者取最小`` () =
        let sevenPairs = hand 0 "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 7z"
        Assert.Equal(3, Shanten.value (Shanten.standard fourPlayer sevenPairs))
        Assert.Equal(Some 0, Shanten.chiitoitsu fourPlayer sevenPairs |> Option.map Shanten.value)
        Assert.Equal(Some 10, Shanten.kokushi fourPlayer sevenPairs |> Option.map Shanten.value)
        Assert.Equal(0, Shanten.value (Shanten.calculate fourPlayer sevenPairs))

    [<Fact>]
    let ``副露过就没有七对子与国士`` () =
        let naki = hand 1 "1m 9m 1p 9p 1s 9s 1z 2z 3z 4z"
        Assert.Equal(None, Shanten.chiitoitsu fourPlayer naki)
        Assert.Equal(None, Shanten.kokushi fourPlayer naki)

    // ---- 副露 ----

    [<Fact>]
    let ``副露按已成面子计入`` () =
        Assert.Equal(0, shanten 1 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p")
        Assert.Equal(-1, shanten 1 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 1p")
        Assert.Equal(0, shanten 2 "1m 2m 3m 4m 5m 6m 7m")
        Assert.Equal(-1, shanten 3 "1m 2m 3m 1p 1p")
        Assert.Equal(-1, shanten 4 "1m 1m")
        Assert.Equal(0, shanten 4 "1m 5p")

    [<Fact>]
    let ``四副露只剩单骑`` () =
        // 两张不成对就是单骑听牌，成对就是和了型。
        Assert.Equal(0, shanten 4 "1z 2z")
        Assert.Equal(-1, shanten 4 "1z 1z")

    // ---- 摸不到的牌：四张全在自己手里 ----

    [<Fact>]
    let ``四张全在手里的单骑不算听牌`` () =
        // 4 面子 + 第 4 张 6s 单骑：第 5 张 6s 不存在，永远和不了，
        // 要先打掉它换一张才谈得上听牌，所以是一向听而不是听牌。
        Assert.Equal(1, shanten 0 "3m 4m 5m 6m 7m 8m 1p 2p 3p 6s 6s 6s 6s")
        // 同一副牌多摸进一张 9s：打掉 6s 就是 9s 单骑听牌。
        Assert.Equal(0, shanten 0 "3m 4m 5m 6m 7m 8m 1p 2p 3p 6s 6s 6s 6s 9s")

    [<Fact>]
    let ``四张全在手里的字牌是死张`` () =
        // 1z 与 2z 各四张：各出一个刻子后剩下的两张既凑不出刻子也进不了顺子，
        // 只能打掉。三副面子加雀头已经齐了，仍要两次替换才够第四组，所以是二向听。
        Assert.Equal(2, shanten 0 "1z 1z 1z 1z 2z 2z 2z 2z 2m 3m 4m 5s 5s")

    // ---- 合法牌种集合是显式入参 ----

    /// 只把 2m 抠掉的假想规则集：用来验「搭子补不补得上」确实看的是入参，
    /// 而不是写死的「34 种全存在」。本票只做四麻，这条只守住接缝。
    let private without2m =
        Tile.kinds
        |> List.filter (fun tile -> not (Tile.suit tile = Manzu && Tile.number tile = 2))
        |> TileKindSet.ofKinds

    [<Fact>]
    let ``牌种集合里没有 2m 时 1m3m 就不是搭子`` () =
        let kanchan = hand 0 "1m 3m 1p 2p 3p 4p 5p 6p 7p 8p 9p 1s 1s"
        Assert.Equal(0, Shanten.value (Shanten.calculate fourPlayer kanchan))
        Assert.Equal(1, Shanten.value (Shanten.calculate without2m kanchan))

    [<Fact>]
    let ``牌种集合不影响用得上的搭子`` () =
        // 3m5m 的嵌张要 4m，4m 还在，所以两个规则集算出来一样。
        let kanchan = hand 0 "3m 5m 1p 2p 3p 4p 5p 6p 7p 8p 9p 1s 1s"
        Assert.Equal(0, Shanten.value (Shanten.calculate fourPlayer kanchan))
        Assert.Equal(0, Shanten.value (Shanten.calculate without2m kanchan))

    [<Fact>]
    let ``牌种不足七种时没有七对子`` () =
        let tinyRuleset = Tile.kinds |> List.truncate 6 |> TileKindSet.ofKinds
        let pairs = hand 0 "1m 1m 2m 2m 3m 3m 4m 4m 5m 5m 6m 6m 1m"
        Assert.Equal(None, Shanten.chiitoitsu tinyRuleset pairs)
        // 六对齐了但只有六种牌，还差一种：牌种数不足同样要算进去。
        Assert.Equal(Some 1, Shanten.chiitoitsu fourPlayer pairs |> Option.map Shanten.value)

    [<Fact>]
    let ``牌种集合缺幺九牌时没有国士`` () =
        let without1m =
            Tile.kinds
            |> List.filter (fun tile -> not (Tile.suit tile = Manzu && Tile.number tile = 1))
            |> TileKindSet.ofKinds

        let thirteen = hand 0 "9m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z"
        Assert.Equal(None, Shanten.kokushi without1m thirteen)
        Assert.Equal(Some 0, Shanten.kokushi fourPlayer thirteen |> Option.map Shanten.value)
