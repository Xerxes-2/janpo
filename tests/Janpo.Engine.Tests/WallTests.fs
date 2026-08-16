namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 牌山的构成、王牌的留出与配牌手顺。不变量见 WallProperties。
module WallTests =

    let private ruleset = Ruleset.yonma

    let private built (seed: int) =
        Wall.build ruleset (Rng.ofSeed seed) |> fst

    /// 洗好之后、发牌之前的可摸区，按摸牌顺序。配牌手顺的断言拿它当基准。
    let private liveWall (wall: Wall) =
        Wall.tiles wall |> List.truncate (Wall.remaining wall)

    let private dealt (oya: Seat) (wall: Wall) =
        match Wall.deal ruleset oya wall with
        | Some result -> result
        | None -> failwith $"座位 {Seat.index oya} 为亲时应当发得出配牌"

    [<Fact>]
    let ``可摸区与王牌的张数取自规则集`` () =
        let wall = built 1
        Assert.Equal(Ruleset.wallSize ruleset - ruleset.DeadWallSize, Wall.remaining wall)
        Assert.Equal(ruleset.DeadWallSize, Wall.deadWall wall |> List.length)
        Assert.Equal(Ruleset.wallSize ruleset, Wall.tiles wall |> List.length)

    [<Fact>]
    let ``王牌里先是岭上牌再是成叠的表里指示牌`` () =
        let wall = built 1
        Assert.Equal(ruleset.RinshanCount, Wall.rinshan wall |> List.length)

        let indicatorArea = (ruleset.DeadWallSize - ruleset.RinshanCount) / 2
        Assert.Equal(5, indicatorArea)

        let allRevealed =
            [ 1..indicatorArea ]
            |> List.fold (fun state _ -> Wall.reveal state |> Option.map snd |> Option.defaultValue state) wall

        Assert.Equal(indicatorArea, Wall.doraIndicators allRevealed |> List.length)
        Assert.Equal(indicatorArea, Wall.uraIndicators allRevealed |> List.length)

    [<Fact>]
    let ``开局只翻开一张表宝牌指示牌`` () =
        let wall = built 1
        Assert.Equal(1, Wall.doraIndicators wall |> List.length)
        Assert.Equal(1, Wall.uraIndicators wall |> List.length)

    [<Fact>]
    let ``每翻一次多一张指示牌，翻满就不再增加`` () =
        let wall = built 1

        let counts =
            [ 1..7 ]
            |> List.scan (fun state _ -> Wall.reveal state |> Option.map snd |> Option.defaultValue state) wall
            |> List.map (Wall.doraIndicators >> List.length)

        Assert.Equal<int list>([ 1; 2; 3; 4; 5; 5; 5; 5 ], counts)

    [<Fact>]
    let ``指示牌与岭上牌都留在王牌里，不进可摸区`` () =
        let wall = built 1
        let live = liveWall wall
        let dead = Wall.deadWall wall

        Assert.Equal(Ruleset.wallSize ruleset, List.length live + List.length dead)
        Assert.Equal<Tile list>(Tile.sort (Ruleset.wallTiles ruleset), Tile.sort (live @ dead))

    [<Fact>]
    let ``配牌从亲起按四四四一的手顺取牌`` () =
        let wall = built 3

        for oya in Seat.all ruleset do
            let live = liveWall wall
            let hands, _ = dealt oya wall
            let order = Seat.orderFrom ruleset oya

            for seat in Seat.all ruleset do
                let position = order |> List.findIndex (fun candidate -> candidate = seat)

                let expected =
                    [
                        for round in 0..2 do
                            for offset in 0..3 -> round * 16 + position * 4 + offset
                        yield 48 + position
                    ]
                    |> List.map (fun index -> List.item index live)
                    |> Tile.sort

                Assert.Equal<Tile list>(expected, Seat.tryItem seat hands |> Option.defaultValue [])

    [<Fact>]
    let ``配牌后可摸区少掉发出去的张数`` () =
        let wall = built 3
        let hands, afterDeal = dealt Seat.first wall

        Assert.Equal(ruleset.SeatCount, List.length hands)

        for hand in hands do
            Assert.Equal(ruleset.HaipaiSize, List.length hand)

        Assert.Equal(Wall.remaining wall - Ruleset.haipaiTotal ruleset, Wall.remaining afterDeal)
        Assert.Equal(ruleset.DeadWallSize, Wall.deadWall afterDeal |> List.length)

    [<Fact>]
    let ``亲不是合法座位时发不出配牌`` () =
        Assert.Equal<(Tile list list * Wall) option>(None, Wall.deal ruleset (seat 4) (built 1))

        // 负数**在类型层就不是座位**（`Seat` 只能经 `Seat.ofIndex` 从裸整数来，晨间裁决 R-5），
        // 所以这条判据从运行期挪到了构造处：过去这里写的是 `Wall.deal ruleset -1`。
        Assert.Equal<Seat option>(None, Seat.ofIndex -1)

    [<Fact>]
    let ``牌不够发时配牌返回 None`` () =
        let tiny =
            { ruleset with
                TileKinds = Tile.kinds |> List.truncate 4 |> TileKindSet.ofKinds
                Akadora = []
            }

        let wall, _ = Wall.build tiny (Rng.ofSeed 1)
        Assert.Equal<(Tile list list * Wall) option>(None, Wall.deal tiny Seat.first wall)

    [<Fact>]
    let ``摸光可摸区之后再摸返回 None，王牌纹丝不动`` () =
        let wall = built 5

        let emptied =
            [ 1 .. Wall.remaining wall ]
            |> List.fold
                (fun state _ ->
                    match Wall.draw state with
                    | Some(_, rest) -> rest
                    | None -> failwith "可摸区不该提前摸空")
                wall

        Assert.Equal(0, Wall.remaining emptied)
        Assert.True((Wall.draw emptied).IsNone)
        Assert.Equal(ruleset.DeadWallSize, Wall.deadWall emptied |> List.length)

    [<Fact>]
    let ``同一种子建出同一座牌山，不同种子建出不同牌山`` () =
        Assert.Equal<Tile list>(Wall.tiles (built 42), Wall.tiles (built 42))
        Assert.NotEqual<Tile list>(Wall.tiles (built 42), Wall.tiles (built 43))

    /// 跨目标复现的黄金用例：M1 把同一份 F# 编译成 JS 之后，这一行必须一字不差。
    [<Fact>]
    let ``固定种子建出固定的牌山`` () =
        Assert.Equal("7z 5z 5m 9p 2p 8s", built 42 |> liveWall |> List.truncate 6 |> Tile.toMjaiMany)
