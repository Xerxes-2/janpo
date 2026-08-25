namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.Xunit
open Janpo

/// Tile 记法的不变量。具名用例见 TileNotationTests。
[<Properties(Arbitrary = [| typeof<TileArbitraries> |])>]
module TileProperties =

    [<Property>]
    let ``任意 Tile 打印后再解析得回自身`` (tile: Tile) = Tile.parse (Tile.toMjai tile) = Ok tile

    [<Property>]
    let ``任意合法记法解析再打印得回原串`` (ValidTileNotation notation) =
        Tile.parse notation |> Result.map Tile.toMjai = Ok notation

    /// 两句话，**各有各的执行者**（票 114）：前半句「什么串都不抛异常」由纯随机串与近似记法守着，
    /// 后半句「能解析出来的，原串本身就是规范形」由掺进来的合法记法守着。
    ///
    /// 喂 `NonNull<string>` 时后半句没人守：随机串解析不出一张牌，`Ok` 那一支 300 个样本
    /// 一次也没进过，整条属性等价于 `fun _ -> true`（票 113 §3.1 量出来的那一条）。
    [<Property>]
    let ``解析任意字符串都返回值而不抛异常`` (TileNotationCandidate text) =
        match Tile.parse text with
        | Ok tile -> Tile.toMjai tile = text // 能解析出来的，原串本身就是规范形
        | Error _ -> true

    [<Property>]
    let ``任意合法记法列的规范形是幂等的`` (ValidTileListNotation text) =
        match Tile.canonicalize text with
        | Ok canonical -> Tile.canonicalize canonical = Ok canonical
        | Error _ -> false

    [<Property>]
    let ``规范化不增删牌`` (ValidTileListNotation text) =
        match Tile.parseMany text, Tile.canonicalize text |> Result.bind Tile.parseMany with
        | Ok before, Ok after -> List.sort before = List.sort after
        | _ -> false

    [<Property>]
    let ``记法列往返不变`` (tiles: Tile list) =
        Tile.parseMany (Tile.toMjaiMany tiles) = Ok tiles

    [<Property>]
    let ``kindIndex 与 ofKindIndex 在去红后互逆`` (tile: Tile) =
        Tile.ofKindIndex (Tile.kindIndex tile) = Some(Tile.deaka tile)

    [<Property>]
    let ``tryCreate 与花色序数红宝牌三件套互逆`` (tile: Tile) =
        Tile.tryCreate (Tile.suit tile) (Tile.number tile) (Tile.isAkadora tile) = Some tile

    [<Property>]
    let ``规范形升序`` (ValidTileListNotation text) =
        match Tile.canonicalize text |> Result.bind Tile.parseMany with
        | Ok tiles -> tiles = List.sort tiles
        | Error _ -> false
