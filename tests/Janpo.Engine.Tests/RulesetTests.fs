namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 规则集：座位数与牌山构成的唯一出处。这里断言的是「构成对不对」，
/// 洗牌与发牌见 WallTests。
module RulesetTests =

    let private countOf (notation: string) (tiles: Tile list) =
        tiles |> List.filter (fun tile -> Tile.toMjai tile = notation) |> List.length

    [<Fact>]
    let ``四麻默认是 4 家 136 张`` () =
        Assert.Equal(4, Ruleset.yonma.SeatCount)
        Assert.Equal(136, Ruleset.wallSize Ruleset.yonma)
        Assert.Equal(136, Ruleset.wallTiles Ruleset.yonma |> List.length)

    [<Fact>]
    let ``牌山每种正牌各 4 张`` () =
        let counts =
            Ruleset.wallTiles Ruleset.yonma
            |> List.countBy Tile.deaka
            |> List.map snd
            |> List.distinct

        Assert.Equal<int list>([ 4 ], counts)
        Assert.Equal(34, Ruleset.wallTiles Ruleset.yonma |> List.distinctBy Tile.kindIndex |> List.length)

    [<Fact>]
    let ``红宝牌各一张且换掉对应正牌而不是凭空多出`` () =
        let tiles = Ruleset.wallTiles Ruleset.yonma

        for aka, normal in [ "5mr", "5m"; "5pr", "5p"; "5sr", "5s" ] do
            Assert.Equal(1, countOf aka tiles)
            Assert.Equal(3, countOf normal tiles)

        Assert.Equal(136, List.length tiles)

    [<Fact>]
    let ``关掉红宝牌后牌山里没有红宝牌`` () =
        let ruleset = Ruleset.withoutAkadora Ruleset.yonma
        let tiles = Ruleset.wallTiles ruleset

        Assert.Equal(136, List.length tiles)
        Assert.Empty(tiles |> List.filter Tile.isAkadora)
        Assert.Equal(4, countOf "5m" tiles)

    [<Fact>]
    let ``洗牌前的牌山是升序规范形`` () =
        let tiles = Ruleset.wallTiles Ruleset.yonma
        Assert.Equal<Tile list>(Tile.sort tiles, tiles)

    [<Fact>]
    let ``符与点数的规则项是字段不是写死的，默认值照天凤`` () =
        // 切上满贯 **默认关**：天凤与雀魂段位战都不采用（DECISIONS 提案 S-A）。
        Assert.False(Ruleset.yonma.KiriageMangan)
        Assert.True((Ruleset.withKiriageMangan Ruleset.yonma).KiriageMangan)

        // 连风牌雀头天凤是 4 符；岭上自摸天凤加自摸符。
        Assert.Equal(4, Ruleset.yonma.DoubleKazeJantouFu)
        Assert.True(Ruleset.yonma.RinshanTsumoFu)

        // 一本场 300 点、一根立直棒 1000 点。
        Assert.Equal(300, Ruleset.yonma.HonbaPoints)
        Assert.Equal(1000, Ruleset.yonma.RiichiBou)

    [<Fact>]
    let ``配牌总张数由座位数与配牌张数算出`` () =
        Assert.Equal(52, Ruleset.haipaiTotal Ruleset.yonma)

    [<Fact>]
    let ``牌山张数随牌种与张数变化而不是写死的 136`` () =
        let manzuOnly =
            { Ruleset.yonma with
                TileKinds = Tile.kinds |> List.filter (fun tile -> Tile.suit tile = Manzu)
                Akadora = []
            }

        Assert.Equal(36, Ruleset.wallSize manzuOnly)
        Assert.Equal(36, Ruleset.wallTiles manzuOnly |> List.length)

    [<Fact>]
    let ``对应正牌不在牌山里的红宝牌不会凭空加牌`` () =
        let withoutManzu =
            { Ruleset.yonma with
                TileKinds = Tile.kinds |> List.filter (fun tile -> Tile.suit tile <> Manzu)
            }

        let tiles = Ruleset.wallTiles withoutManzu
        Assert.Equal(Ruleset.wallSize withoutManzu, List.length tiles)
        Assert.Equal(0, countOf "5mr" tiles)
        Assert.Equal(1, countOf "5pr" tiles)
