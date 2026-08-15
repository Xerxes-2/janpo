namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// Tile 的 mjai 记法：具名用例。属性见 TileProperties。
module TileNotationTests =

    let private parseError (expected: TileParseError) (notation: string) =
        Assert.Equal<Result<Tile, TileParseError>>(Error expected, Tile.parse notation)

    let private parsed (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"记法 {notation} 应当合法，却得到 {error}"

    [<Fact>]
    let ``34 种正牌按 mjai 顺序排列`` () =
        let expected =
            [
                for suit in [ "m"; "p"; "s" ] do
                    for number in 1..9 -> string number + suit
                for number in 1..7 -> string number + "z"
            ]

        Assert.Equal<string list>(expected, Tile.kinds |> List.map Tile.toMjai)

    [<Fact>]
    let ``3 种红宝牌是 5mr 5pr 5sr`` () =
        Assert.Equal<string list>([ "5mr"; "5pr"; "5sr" ], Tile.akadoraKinds |> List.map Tile.toMjai)

    [<Fact>]
    let ``Tile 全集为 34 正牌加 3 红宝牌且互不相同`` () =
        Assert.Equal(37, List.length Tile.all)
        Assert.Equal(37, Tile.all |> List.distinct |> List.length)

    [<Fact>]
    let ``红宝牌去红后是对应的正牌`` () =
        Assert.True(Tile.isAkadora (parsed "5mr"))
        Assert.False(Tile.isAkadora (parsed "5m"))
        Assert.Equal("5m", Tile.toMjai (Tile.deaka (parsed "5mr")))
        Assert.Equal("5p", Tile.toMjai (Tile.deaka (parsed "5pr")))
        Assert.Equal("5s", Tile.toMjai (Tile.deaka (parsed "5sr")))

    [<Fact>]
    let ``红宝牌与对应正牌的 kindIndex 相同`` () =
        Assert.Equal(Tile.kindIndex (parsed "5m"), Tile.kindIndex (parsed "5mr"))
        Assert.Equal(0, Tile.kindIndex (parsed "1m"))
        Assert.Equal(33, Tile.kindIndex (parsed "7z"))

    [<Fact>]
    let ``kindIndex 越界返回 None`` () =
        Assert.Equal(Some(parsed "1m"), Tile.ofKindIndex 0)
        Assert.Equal(Some(parsed "7z"), Tile.ofKindIndex 33)
        Assert.Equal(None, Tile.ofKindIndex 34)
        Assert.Equal(None, Tile.ofKindIndex -1)

    [<Fact>]
    let ``tryCreate 拒绝不存在的牌`` () =
        Assert.Equal(Some(parsed "5mr"), Tile.tryCreate Manzu 5 true)
        Assert.Equal(Some(parsed "7z"), Tile.tryCreate Jihai 7 false)
        Assert.Equal(None, Tile.tryCreate Jihai 8 false)
        Assert.Equal(None, Tile.tryCreate Jihai 5 true)
        Assert.Equal(None, Tile.tryCreate Pinzu 3 true)
        Assert.Equal(None, Tile.tryCreate Manzu 0 false)
        Assert.Equal(None, Tile.tryCreate Souzu 10 false)

    [<Fact>]
    let ``牌的花色与序数可读出`` () =
        Assert.Equal(Manzu, Tile.suit (parsed "1m"))
        Assert.Equal(Pinzu, Tile.suit (parsed "9p"))
        Assert.Equal(Souzu, Tile.suit (parsed "5sr"))
        Assert.Equal(Jihai, Tile.suit (parsed "3z"))
        Assert.Equal(1, Tile.number (parsed "1m"))
        Assert.Equal(9, Tile.number (parsed "9p"))
        Assert.Equal(5, Tile.number (parsed "5sr"))
        Assert.Equal(3, Tile.number (parsed "3z"))

    [<Fact>]
    let ``非法记法返回错误值`` () =
        parseError EmptyNotation ""
        parseError (NumberOutOfRange(0, Manzu)) "0m"
        parseError (NumberOutOfRange(8, Jihai)) "8z"
        parseError (NumberOutOfRange(9, Jihai)) "9z"
        parseError (UnknownSuit 'x') "5x"
        parseError (UnknownSuit 'M') "5M"
        parseError (AkadoraNotAllowed(3, Manzu)) "3mr"
        parseError (AkadoraNotAllowed(1, Jihai)) "1zr"
        parseError (MalformedNotation "5") "5"
        parseError (MalformedNotation "m5") "m5"
        parseError (MalformedNotation "5mm") "5mm"
        parseError (MalformedNotation "5m ") "5m "
        parseError (MalformedNotation " 5m") " 5m"
        parseError (MalformedNotation "５m") "５m"
        parseError (MalformedNotation "5m5m") "5m5m"

    [<Fact>]
    let ``天凤式的 0m 不是合法 mjai 记法`` () =
        parseError (NumberOutOfRange(0, Manzu)) "0m"
        parseError (NumberOutOfRange(0, Pinzu)) "0p"
        parseError (NumberOutOfRange(0, Souzu)) "0s"

    [<Fact>]
    let ``记法列可用空白或逗号分隔`` () =
        Assert.Equal<Result<string, TileListParseError>>(Ok "1m 2m 3m", Tile.canonicalize "1m 2m 3m")
        Assert.Equal<Result<string, TileListParseError>>(Ok "1m 2m 3m", Tile.canonicalize "1m,2m, 3m")
        Assert.Equal<Result<string, TileListParseError>>(Ok "1m 2m 3m", Tile.canonicalize "  1m\t2m\n3m  ")
        Assert.Equal<Result<string, TileListParseError>>(Ok "", Tile.canonicalize "")

    [<Fact>]
    let ``规范形按 mjai 顺序升序且红宝牌紧跟同种正牌`` () =
        Assert.Equal<Result<string, TileListParseError>>(
            Ok "5m 5mr 5p 5pr 5s 5sr 9s 1z",
            Tile.canonicalize "1z 5sr 5s 9s 5pr 5p 5mr 5m"
        )

    [<Fact>]
    let ``记法列的错误带上出错的位置与记法`` () =
        Assert.Equal<Result<string, TileListParseError>>(
            Error
                {
                    TokenIndex = 1
                    Token = "9z"
                    Reason = NumberOutOfRange(9, Jihai)
                },
            Tile.canonicalize "1m 9z 3m"
        )

    [<Fact>]
    let ``人类可读形式只出现在 toDisplay`` () =
        Assert.Equal("1万", Tile.toDisplay (parsed "1m"))
        Assert.Equal("9筒", Tile.toDisplay (parsed "9p"))
        Assert.Equal("5索", Tile.toDisplay (parsed "5s"))
        Assert.Equal("赤5万", Tile.toDisplay (parsed "5mr"))
        Assert.Equal("赤5索", Tile.toDisplay (parsed "5sr"))
        Assert.Equal("东", Tile.toDisplay (parsed "1z"))
        Assert.Equal("南", Tile.toDisplay (parsed "2z"))
        Assert.Equal("西", Tile.toDisplay (parsed "3z"))
        Assert.Equal("北", Tile.toDisplay (parsed "4z"))
        Assert.Equal("白", Tile.toDisplay (parsed "5z"))
        Assert.Equal("发", Tile.toDisplay (parsed "6z"))
        Assert.Equal("中", Tile.toDisplay (parsed "7z"))
