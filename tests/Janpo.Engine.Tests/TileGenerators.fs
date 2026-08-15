namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 合法的单张牌 mjai 记法（FsCheck 包装类型）。
type ValidTileNotation = | ValidTileNotation of string

/// 合法的记法列：牌的顺序与分隔符都随机，未必是规范形。
type ValidTileListNotation = | ValidTileListNotation of string

/// 本工程的 FsCheck 生成器集合。测试模块用
/// `[<Properties(Arbitrary = [| typeof<TileArbitraries> |])>]` 注册。
type TileArbitraries =

    static member Tile() : Arbitrary<Tile> = Gen.elements Tile.all |> Arb.fromGen

    static member ValidTileNotation() : Arbitrary<ValidTileNotation> =
        Tile.all
        |> List.map (Tile.toMjai >> ValidTileNotation)
        |> Gen.elements
        |> Arb.fromGen

    static member ValidTileListNotation() : Arbitrary<ValidTileListNotation> =
        gen {
            let! tiles = Gen.listOf (Gen.elements Tile.all)
            let notations = tiles |> List.map Tile.toMjai

            let! separators =
                Gen.listOfLength (max 0 (List.length notations - 1)) (Gen.elements [ " "; "  "; ", "; ","; "\t" ])

            let text =
                match notations with
                | [] -> ""
                | head :: tail ->
                    List.fold2 (fun acc separator notation -> acc + separator + notation) head separators tail

            return ValidTileListNotation text
        }
        |> Arb.fromGen
