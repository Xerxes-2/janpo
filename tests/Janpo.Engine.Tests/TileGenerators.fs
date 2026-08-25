namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 合法的单张牌 mjai 记法（FsCheck 包装类型）。
type ValidTileNotation = | ValidTileNotation of string

/// 合法的记法列：牌的顺序与分隔符都随机，未必是规范形。
type ValidTileListNotation = | ValidTileListNotation of string

/// 「看着像一张牌」的候选记法：合法记法、近似记法与纯随机串掺在一起。
///
/// **它是「解析任意字符串」那条属性的执行者**（票 114）：原来喂的是 `NonNull<string>`，
/// 随机串一辈子解析不出一张牌，于是「能解析出来的，原串本身就是规范形」那一支
/// 300 个样本一次也没进过——整条属性等价于 `fun _ -> true`（票 113 §3.1）。
///
/// 三份原料各有各的活：**合法记法**让 `Ok` 那一支每趟都开口；**近似记法**是真正咬人的那一份
/// （天凤式的 `0m`、大写 `1M`、带空白的 ` 1m`、后缀写反的 `5rm`——`Tile.parse` 一旦收下
/// 其中任何一条，`Ok` 那一支当场红）；**纯随机串**守着原来那半句「什么都不抛」。
type TileNotationCandidate = | TileNotationCandidate of string

/// 候选记法的原料表。**摊在这里而不是埋进生成器**：`TileNotationTests` 里那条
/// 「近似记法逐条都不是规范形」拿的就是这两张表——它们要是混进了一条规范形，
/// 近似那一份就咬不动 `Ok` 那一支了（票 114）。
module TileNotationSamples =

    /// 37 张牌的规范记法。
    let canonical: string list = Tile.all |> List.map Tile.toMjai

    /// 与规范形只差一点点的那些串。**一条都不许是规范形**，判据由具名用例守着；
    /// 它们今天全被 `Tile.parse` 拒掉（ADR-0001：不接受大写、空白、天凤式的 `0m`），
    /// 而这份表**不拿 `Tile.parse` 自己筛**——拿被测物筛输入，放宽了它就自己把证据丢掉。
    let nearMisses: string list =
        [
            for notation in canonical do
                // 大写：`1M` / `5MR`
                notation.ToUpperInvariant()
                // 前后带空白
                " " + notation
                notation + " "
                // 多一个后缀（大写 R，免得 `5m` 加上去正好成了合法的 `5mr`）
                notation + "R"
            // 天凤式的赤 5 与不存在的序数
            yield! [ "0m"; "0p"; "0s"; "0z"; "8z"; "9z"; "10m" ]
            // 后缀写反、少一半、整个反过来
            yield! [ "5rm"; "5rp"; "5rs"; "m1"; "z1"; "m"; "5"; "" ]
        ]

/// 本工程的 FsCheck 生成器集合。测试模块用
/// `[<Properties(Arbitrary = [| typeof<TileArbitraries> |])>]` 注册。
type TileArbitraries =

    static member Tile() : Arbitrary<Tile> = Gen.elements Tile.all |> Arb.fromGen

    static member ValidTileNotation() : Arbitrary<ValidTileNotation> =
        Tile.all
        |> List.map (Tile.toMjai >> ValidTileNotation)
        |> Gen.elements
        |> Arb.fromGen

    static member TileNotationCandidate() : Arbitrary<TileNotationCandidate> =
        // 纯随机串那一份仍旧是 FsCheck 自己的字符串生成器（`NonNull` 排掉 null，
        // 与这条属性原来收的入参逐字相同）。
        let junk =
            ArbMap.defaults
            |> ArbMap.generate<NonNull<string>>
            |> Gen.map (fun (NonNull text) -> text)

        Gen.frequency
            [
                2, Gen.elements TileNotationSamples.canonical
                2, Gen.elements TileNotationSamples.nearMisses
                1, junk
            ]
        |> Gen.map TileNotationCandidate
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
