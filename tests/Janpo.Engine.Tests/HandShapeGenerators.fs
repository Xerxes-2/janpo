namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 具名用例共用的手牌构造：记法 → HandShape，构造不出来就直接失败。
/// 固件一律用 mjai 记法（ADR-0001）。
module HandFixture =

    let tiles (notations: string) : Tile list =
        match Tile.parseMany notations with
        | Ok tiles -> tiles
        | Error error -> failwith $"记法应当合法：{TileListParseError.toDisplay error}"

    let hand (nakiCount: int) (notations: string) : HandShape =
        match HandShape.create nakiCount (tiles notations) with
        | Ok hand -> hand
        | Error error -> failwith $"手牌应当合法：{HandShapeError.toDisplay error}"

/// 手牌形态生成器的零件。测试模块用下面的 `HandShapeArbitraries` 注册，不直接用这里的东西。
module private HandShapeGeneration =

    let tileOf suit number =
        match Tile.tryCreate suit number false with
        | Some tile -> tile
        | None -> failwith $"生成器造了不存在的牌: {suit} {number}"

    /// 一副 136 张的牌山（不含红宝牌——红宝牌不影响形态）。
    let wall = Tile.kinds |> List.collect (List.replicate 4)

    let shuffled (items: 'a list) : Gen<'a list> =
        gen {
            let! keys = Gen.listOfLength (List.length items) (Gen.choose (0, 1 <<< 29))
            return List.zip keys items |> List.sortBy fst |> List.map snd
        }

    /// 按「每种最多 4 张」从候选里收够 size 张，收不满就到此为止。
    let fill (size: int) (candidates: Tile list) : Tile list =
        let counts = Array.zeroCreate Tile.KindCount
        let mutable taken = []
        let mutable count = 0

        for tile in candidates do
            let index = Tile.kindIndex tile

            if count < size && counts.[index] < 4 then
                counts.[index] <- counts.[index] + 1
                taken <- tile :: taken
                count <- count + 1

        taken

    let create nakiCount tiles =
        match HandShape.create nakiCount tiles with
        | Ok hand -> hand
        | Error error -> failwith $"生成器造出了非法手牌: {HandShapeError.toDisplay error}"

    let nakiGen =
        Gen.frequency
            [
                60, Gen.constant 0
                15, Gen.constant 1
                12, Gen.constant 2
                8, Gen.constant 3
                5, Gen.constant 4
            ]

    let awaitingSize nakiCount = 13 - 3 * nakiCount

    let sizeGen nakiCount =
        Gen.elements [ awaitingSize nakiCount; awaitingSize nakiCount + 1 ]

    /// 一个面子：顺子或刻子。
    let blockGen: Gen<Tile list> =
        Gen.oneof
            [
                gen {
                    let! suit = Gen.elements [ Manzu; Pinzu; Souzu ]
                    let! start = Gen.choose (1, 7)
                    return [ for number in start .. start + 2 -> tileOf suit number ]
                }
                gen {
                    let! tile = Gen.elements Tile.kinds
                    return List.replicate 3 tile
                }
            ]

    /// 均匀随机手牌：铺开状态空间，多数落在 3-4 向听。
    let uniformHand (nakiGen: Gen<int>) (sizeOf: int -> Gen<int>) : Gen<HandShape> =
        gen {
            let! naki = nakiGen
            let! size = sizeOf naki
            let! tiles = shuffled wall
            return create naki (List.truncate size tiles)
        }

    /// 结构化手牌：先搭出面子与雀头，再随机扰动几张。样本集中在听牌与和了型附近。
    let structuredHand (nakiGen: Gen<int>) (sizeOf: int -> Gen<int>) : Gen<HandShape> =
        gen {
            let! naki = nakiGen
            let! size = sizeOf naki
            let! blocks = Gen.listOfLength 4 blockGen
            let! pair = Gen.elements Tile.kinds
            let! noise = Gen.choose (0, 4)
            let! filler = shuffled wall

            let structured =
                (List.concat blocks @ [ pair; pair ]) |> List.truncate (max 0 (size - noise))

            return create naki (fill size (structured @ filler))
        }

    let handGen nakiGen sizeOf =
        Gen.frequency [ 1, uniformHand nakiGen sizeOf; 2, structuredHand nakiGen sizeOf ]

    /// 一定成立的一般型和了牌：`4 - 副露数` 个面子加一个雀头，且每种不超过 4 张。
    /// 它是和了型判定的独立参照——不经过分解搜索就知道答案。
    let completeHand: Gen<HandShape> =
        gen {
            let! naki = nakiGen
            let! blocks = Gen.listOfLength (4 - naki) blockGen
            let! pair = Gen.elements Tile.kinds
            return naki, List.concat blocks @ [ pair; pair ]
        }
        |> Gen.where (fun (_, tiles) ->
            tiles
            |> List.countBy Tile.kindIndex
            |> List.forall (fun (_, count) -> count <= 4))
        |> Gen.map (fun (naki, tiles) -> create naki tiles)

/// 任意合法手牌形态：副露数与张数都随机。
type AnyHand =
    | AnyHand of HandShape

    override this.ToString() =
        let (AnyHand hand) = this
        $"naki={HandShape.nakiCount hand} {HandShape.tiles hand |> Tile.toMjaiMany}"

/// 等摸的手牌（暗牌 `13 - 3 * 副露数` 张）。
type AwaitingHand =
    | AwaitingHand of HandShape

    override this.ToString() =
        let (AwaitingHand hand) = this
        $"naki={HandShape.nakiCount hand} {HandShape.tiles hand |> Tile.toMjaiMany}"

/// 已摸进的手牌（暗牌 `14 - 3 * 副露数` 张）。
type DrawnHand =
    | DrawnHand of HandShape

    override this.ToString() =
        let (DrawnHand hand) = this
        $"naki={HandShape.nakiCount hand} {HandShape.tiles hand |> Tile.toMjaiMany}"

/// 一定成立的一般型和了牌。
type CompleteHand =
    | CompleteHand of HandShape

    override this.ToString() =
        let (CompleteHand hand) = this
        $"naki={HandShape.nakiCount hand} {HandShape.tiles hand |> Tile.toMjaiMany}"

/// 手牌形态的 FsCheck 生成器集合。测试模块用
/// `[<Properties(Arbitrary = [| typeof<HandShapeArbitraries> |])>]` 注册。
type HandShapeArbitraries =

    static member AnyHand() : Arbitrary<AnyHand> =
        HandShapeGeneration.handGen HandShapeGeneration.nakiGen HandShapeGeneration.sizeGen
        |> Gen.map AnyHand
        |> Arb.fromGen

    static member AwaitingHand() : Arbitrary<AwaitingHand> =
        HandShapeGeneration.handGen HandShapeGeneration.nakiGen (fun naki ->
            Gen.constant (HandShapeGeneration.awaitingSize naki))
        |> Gen.map AwaitingHand
        |> Arb.fromGen

    static member DrawnHand() : Arbitrary<DrawnHand> =
        HandShapeGeneration.handGen HandShapeGeneration.nakiGen (fun naki ->
            Gen.constant (HandShapeGeneration.awaitingSize naki + 1))
        |> Gen.map DrawnHand
        |> Arb.fromGen

    static member CompleteHand() : Arbitrary<CompleteHand> =
        HandShapeGeneration.completeHand |> Gen.map CompleteHand |> Arb.fromGen
