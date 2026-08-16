namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 具名用例共用的和了牌姿构造：记法 → `Naki` / `AgariHand`，构造不出来就直接失败。
/// 固件一律用 mjai 记法（ADR-0001）。
module AgariFixture =

    let tiles (notations: string) : Tile list = HandFixture.tiles notations

    let tile (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"记法应当合法：{TileParseError.toDisplay error}"

    let private ok (result: Result<Naki, NakiError>) : Naki =
        match result with
        | Ok naki -> naki
        | Error error -> failwith $"副露应当合法：{NakiError.toDisplay error}"

    /// 碰：`pon (seat 1) "5z" "5z 5z"`。
    let pon (target: Seat) (taken: string) (consumed: string) : Naki =
        Naki.pon target (tile taken) (tiles consumed) |> ok

    /// 吃：`chi (seat 3) "3m" "4m 5m"`。
    let chi (target: Seat) (taken: string) (consumed: string) : Naki =
        Naki.chi target (tile taken) (tiles consumed) |> ok

    /// 暗杠：`ankan "1z 1z 1z 1z"`。
    let ankan (consumed: string) : Naki = Naki.ankan (tiles consumed) |> ok

    /// 大明杠：`minkan (seat 2) "1z" "1z 1z 1z"`。
    let minkan (target: Seat) (taken: string) (consumed: string) : Naki =
        Naki.minkan target (tile taken) (tiles consumed) |> ok

    /// 加杠：`kakan "5z" (pon (seat 1) "5z" "5z 5z")`。
    let kakan (added: string) (basePon: Naki) : Naki = Naki.kakan (tile added) basePon |> ok

    let private hand (result: Result<AgariHand, AgariHandError>) : AgariHand =
        match result with
        | Ok hand -> hand
        | Error error -> failwith $"和了牌姿应当合法：{AgariHandError.toDisplay error}"

    /// 荣和。`concealed` 含点出来的那张。
    let ron (naki: Naki list) (concealed: string) (winning: string) : AgariHand =
        AgariHand.ron naki (tiles concealed) (tile winning) |> hand

    /// 自摸。`concealed` 含摸进的那张。
    let tsumo (naki: Naki list) (concealed: string) (winning: string) : AgariHand =
        AgariHand.tsumo naki (tiles concealed) (tile winning) |> hand

/// 和了牌姿生成器的零件。测试模块用下面的 `YakuArbitraries` 注册，不直接用这里的东西。
module private AgariGeneration =

    let tileOf (suit: Suit) (number: int) : Tile =
        match Tile.tryCreate suit number false with
        | Some tile -> tile
        | None -> failwith $"生成器造了不存在的牌: {suit} {number}"

    /// 一个面子的牌：顺子或刻子。
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

    /// 把一组面子的牌做成副露：顺子做成吃，刻子做成碰。
    let toNaki (block: Tile list) : Naki option =
        match block with
        | [ first; second; third ] ->
            let result =
                if first = second then
                    Naki.pon (seat 1) first [ second; third ]
                else
                    Naki.chi (seat 3) first [ second; third ]

            match result with
            | Ok naki -> Some naki
            | Error _ -> None
        | _ -> None

    let kazeGen: Gen<Kaze> = Gen.elements Kaze.all

    let contextGen: Gen<YakuContext> =
        gen {
            let! bakaze = kazeGen
            let! jikaze = kazeGen

            let! riichi =
                Gen.elements
                    [
                        RiichiDeclaration.None
                        RiichiDeclaration.Riichi
                        RiichiDeclaration.DoubleRiichi
                    ]

            let! ippatsu = Gen.elements [ true; false ]
            let! markers = Gen.listOfLength 2 (Gen.elements Tile.kinds)

            return
                { YakuContext.create bakaze jikaze with
                    Riichi = riichi
                    Ippatsu = ippatsu
                    DoraMarkers = markers
                    UraDoraMarkers = markers
                }
        }

    /// 一定成立的一般型和了牌姿：`4 - 副露数` 个暗面子 + 雀头 + 若干副露。
    let agariGen: Gen<AgariHand * YakuContext> =
        gen {
            let! nakiCount = Gen.frequency [ 5, Gen.constant 0; 2, Gen.constant 1; 1, Gen.constant 2 ]
            let! blocks = Gen.listOfLength 4 blockGen
            let! pair = Gen.elements Tile.kinds
            let! tsumo = Gen.elements [ true; false ]
            let! winningIndex = Gen.choose (0, 13)
            let! context = contextGen
            return nakiCount, blocks, pair, tsumo, winningIndex, context
        }
        |> Gen.map (fun (nakiCount, blocks, pair, tsumo, winningIndex, context) ->
            let naki = blocks |> List.truncate nakiCount |> List.choose toNaki

            let concealed =
                (blocks |> List.skip (List.length naki) |> List.concat) @ [ pair; pair ]

            let winning = List.item (winningIndex % List.length concealed) (Tile.sort concealed)

            let build = if tsumo then AgariHand.tsumo else AgariHand.ron
            build naki concealed winning, context)
        |> Gen.where (fun (hand, _) ->
            match hand with
            | Ok _ -> true
            | Error _ -> false)
        |> Gen.map (fun (hand, context) ->
            match hand with
            | Ok hand -> hand, context
            | Error _ -> failwith "已经过滤过了")

/// 一副一定成立的一般型和了牌姿，配一个随机上下文。
type AgariCase =
    | AgariCase of AgariHand * YakuContext

    override this.ToString() =
        let (AgariCase(hand, context)) = this
        $"{AgariHand.toDisplay hand} 场风{Kaze.toDisplay context.Bakaze} 自风{Kaze.toDisplay context.Jikaze}"

/// 役种判定的 FsCheck 生成器集合。测试模块用
/// `[<Properties(Arbitrary = [| typeof<YakuArbitraries> |])>]` 注册。
type YakuArbitraries =

    static member AgariCase() : Arbitrary<AgariCase> =
        AgariGeneration.agariGen |> Gen.map AgariCase |> Arb.fromGen
