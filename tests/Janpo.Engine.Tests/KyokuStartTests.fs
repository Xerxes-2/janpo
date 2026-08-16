namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Newtonsoft
open Janpo

/// 开局：事件流的形状、配牌张数与开不了局时的错误值。不变量见 KyokuStartProperties。
module KyokuStartTests =

    let private ruleset = Ruleset.yonma

    let private context = KyokuContext.initial ruleset

    let private started (ruleset: Ruleset) (context: KyokuContext) (seed: int) =
        match KyokuStart.create ruleset context (Rng.ofSeed seed) with
        | Ok(start, _) -> start
        | Error error -> failwith $"应当开得出局，却得到 {KyokuStartError.toDisplay error}"

    let private startKyokuOf (start: KyokuStart) =
        match start.Events with
        | StartKyoku fields :: _ -> fields
        | events -> failwith $"事件流应以 start_kyoku 开头，实际是 {events}"

    [<Fact>]
    let ``第一局的条件是东 1 局 0 本场，各家起手点数取自规则集`` () =
        Assert.Equal<KyokuContext>(
            {
                Bakaze = Ton
                Kyoku = 1
                Honba = 0
                Kyotaku = 0
                Oya = seat 0
                Scores = [ 25000; 25000; 25000; 25000 ]
            },
            context
        )

    [<Fact>]
    let ``开局产出 start_kyoku 与 Oya 的 tsumo，顺序固定`` () =
        let start = started ruleset { context with Oya = seat 2 } 11

        match start.Events with
        | [ StartKyoku fields; Tsumo(actor, pai) ] ->
            Assert.Equal(seat 2, fields.Oya)
            Assert.Equal(seat 2, actor)
            Assert.Contains(pai, Seat.tryItem (seat 2) start.Hands |> Option.defaultValue [])
        | events -> failwith $"开局应当是 start_kyoku 后跟一条 tsumo，实际是 {events}"

    [<Fact>]
    let ``start_kyoku 照抄给定的场风局数本场供托与点数`` () =
        let given =
            {
                Bakaze = Nan
                Kyoku = 3
                Honba = 2
                Kyotaku = 1
                Oya = seat 1
                Scores = [ 24000; 26000; 25000; 24000 ]
            }

        let fields = started ruleset given 5 |> startKyokuOf

        Assert.Equal(Nan, fields.Bakaze)
        Assert.Equal(3, fields.Kyoku)
        Assert.Equal(2, fields.Honba)
        Assert.Equal(1, fields.Kyotaku)
        Assert.Equal(seat 1, fields.Oya)
        Assert.Equal<int list>([ 24000; 26000; 25000; 24000 ], fields.Scores)

    [<Fact>]
    let ``start_kyoku 里四家各 13 张配牌`` () =
        let fields = started ruleset context 7 |> startKyokuOf

        Assert.Equal(ruleset.SeatCount, List.length fields.Tehais)

        for tehai in fields.Tehais do
            Assert.Equal(ruleset.HaipaiSize, List.length tehai)

    [<Fact>]
    let ``Oya 摸过第一张后是 14 张，其余各家 13 张`` () =
        let start = started ruleset { context with Oya = seat 3 } 7

        let handAt (index: int) =
            Seat.tryItem (seat index) start.Hands |> Option.defaultValue []

        Assert.Equal(ruleset.HaipaiSize + 1, handAt 3 |> List.length)

        for index in [ 0; 1; 2 ] do
            Assert.Equal(ruleset.HaipaiSize, handAt index |> List.length)

    [<Fact>]
    let ``开局翻开的宝牌指示牌来自王牌，且只翻一张`` () =
        let start = started ruleset context 7
        let fields = startKyokuOf start

        Assert.Equal<Tile list>([ fields.DoraMarker ], Wall.doraIndicators start.Wall)
        Assert.Contains(fields.DoraMarker, Wall.deadWall start.Wall)
        Assert.Equal(1, Wall.uraIndicators start.Wall |> List.length)

    [<Fact>]
    let ``开局后可摸区剩下的张数由规则集算出`` () =
        let start = started ruleset context 7

        let expected =
            Ruleset.wallSize ruleset
            - ruleset.DeadWallSize
            - Ruleset.haipaiTotal ruleset
            - 1

        Assert.Equal(69, expected)
        Assert.Equal(expected, Wall.remaining start.Wall)

    [<Fact>]
    let ``同一种子开出同一局，不同种子开出不同配牌`` () =
        Assert.Equal<Event list>((started ruleset context 42).Events, (started ruleset context 42).Events)
        Assert.NotEqual<Event list>((started ruleset context 42).Events, (started ruleset context 43).Events)

    [<Fact>]
    let ``关掉红宝牌后开出来的局里没有红宝牌`` () =
        let start = started (Ruleset.withoutAkadora ruleset) context 42
        let everything = List.concat start.Hands @ Wall.tiles start.Wall

        Assert.Equal(Ruleset.wallSize ruleset, List.length everything)
        Assert.Empty(everything |> List.filter Tile.isAkadora)

    [<Fact>]
    let ``开局事件能编成每行一个 JSON 并原样解回`` () =
        let start = started ruleset context 42

        let lines =
            start.Events |> List.map (fun event -> Encode.toString 0 (Event.encoder event))

        Assert.Equal(2, List.length lines)

        for line in lines do
            Assert.DoesNotContain("\n", line)

        let decoded = lines |> List.map (Decode.fromString Event.decoder)
        Assert.Equal<Result<Event, string> list>(start.Events |> List.map Ok, decoded)

    [<Fact>]
    let ``点数项数与座位数不符时开不了局`` () =
        let given =
            { context with
                Scores = [ 25000; 25000 ]
            }

        Assert.Equal<Result<KyokuStart * Rng, KyokuStartError>>(
            Error(ScoreCountMismatch(4, 2)),
            KyokuStart.create ruleset given (Rng.ofSeed 1)
        )

    [<Fact>]
    let ``亲不是合法座位时开不了局`` () =
        Assert.Equal<Result<KyokuStart * Rng, KyokuStartError>>(
            Error(OyaOutOfRange(seat 4, 4)),
            KyokuStart.create ruleset { context with Oya = seat 4 } (Rng.ofSeed 1)
        )

    [<Fact>]
    let ``牌不够发配牌时开不了局`` () =
        let tiny =
            { ruleset with
                TileKinds = Tile.kinds |> List.truncate 4 |> TileKindSet.ofKinds
                Akadora = []
            }

        Assert.Equal<Result<KyokuStart * Rng, KyokuStartError>>(
            Error(LiveWallTooSmall(53, 2)),
            KyokuStart.create tiny context (Rng.ofSeed 1)
        )

    [<Fact>]
    let ``配牌发得出但 Oya 摸不到第一张时也开不了局`` () =
        let exact =
            { ruleset with
                TileKinds = Tile.kinds |> List.truncate 17 |> TileKindSet.ofKinds
                Akadora = []
                DeadWallSize = 16
            }

        Assert.Equal(52, Ruleset.wallSize exact - exact.DeadWallSize)

        Assert.Equal<Result<KyokuStart * Rng, KyokuStartError>>(
            Error(LiveWallTooSmall(53, 52)),
            KyokuStart.create exact context (Rng.ofSeed 1)
        )

    [<Fact>]
    let ``王牌凑不出一叠指示牌时开不了局`` () =
        let noIndicator =
            { ruleset with
                DeadWallSize = ruleset.RinshanCount
            }

        Assert.Equal<Result<KyokuStart * Rng, KyokuStartError>>(
            Error(NoDoraIndicator 4),
            KyokuStart.create noIndicator context (Rng.ofSeed 1)
        )

    /// 本版**不做三麻**（没有拔北、没有自摸损、向听数在 corner case 上也不同）。
    /// 这条只证明「座位数与牌山构成没有焊死」：换一个规则集值，开局这条路径照样走得通。
    [<Fact>]
    let ``三麻形状的规则集也开得出局`` () =
        let sanma =
            { ruleset with
                SeatCount = 3
                TileKinds =
                    Tile.kinds
                    |> List.filter (fun tile -> Tile.suit tile <> Manzu || Tile.number tile = 1 || Tile.number tile = 9)
                    |> TileKindSet.ofKinds
                Akadora = Tile.akadoraKinds |> List.filter (fun tile -> Tile.suit tile <> Manzu)
            }

        Assert.Equal(108, Ruleset.wallSize sanma)

        let start =
            started
                sanma
                { context with
                    Scores = [ 35000; 35000; 35000 ]
                }
                42

        let everything = List.concat start.Hands @ Wall.tiles start.Wall

        Assert.Equal(3, List.length start.Hands)
        Assert.Equal(108, List.length everything)
        Assert.Equal<Tile list>(Tile.sort (Ruleset.wallTiles sanma), Tile.sort everything)

        Assert.Empty(
            everything
            |> List.filter (fun tile -> Tile.suit tile = Manzu && Tile.number tile = 5)
        )

    [<Fact>]
    let ``开不了局的原因有中文说明`` () =
        Assert.Equal("牌山发不出配牌：需要 53 张，可摸区只有 2 张", KyokuStartError.toDisplay (LiveWallTooSmall(53, 2)))
        Assert.Equal("王牌只有 4 张，凑不出一叠宝牌指示牌", KyokuStartError.toDisplay (NoDoraIndicator 4))
        Assert.Equal("点数列表应有 4 项（座位数），实际 2 项", KyokuStartError.toDisplay (ScoreCountMismatch(4, 2)))
        Assert.Equal("亲的座位 4 不合法，座位只有 0-3", KyokuStartError.toDisplay (OyaOutOfRange(seat 4, 4)))
