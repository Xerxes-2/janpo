namespace Janpo

/// 宝牌：由宝牌指示牌决定的加符牌，含里宝牌与红宝牌（CONTEXT.md）。
///
/// **宝牌只计番，不是役**——无役的手牌不会因为有宝牌就能和。因此这个模块只数数，
/// 役的成立与否由 `Yaku` 判。
[<RequireQualifiedAccess>]
module Dora =

    /// 指示牌的下一张就是宝牌：数牌 9 回到 1，风牌 北 回到 东，三元牌 中 回到 白。
    ///
    /// 这里写死的是四麻的循环。三麻缺 2m-8m 时 `1m` 的下一张是 `9m`，
    /// 那需要按规则集的牌种集合绕，本版不做（三麻不在范围内）。
    let ofMarker (marker: Tile) : Tile =
        let index = Tile.kindIndex marker

        let next =
            if index < 27 then
                // 数牌：花色内 0-8 循环。
                index - index % 9 + (index % 9 + 1) % 9
            elif index < 31 then
                // 风牌：东南西北循环。
                27 + (index - 27 + 1) % 4
            else
                // 三元牌：白发中循环。
                31 + (index - 31 + 1) % 3

        match Tile.ofKindIndex next with
        | Some tile -> tile
        // 索引由取模算出，恒在 0-33；走不到这里。
        | None -> Tile.deaka marker

    /// 这一列牌里有几张是宝牌。指示牌可以有多张（杠宝牌），逐张累加；
    /// 同一张牌被两个指示牌指到就算两番。红宝牌另算，见 `akadoraCount`。
    let count (markers: Tile list) (tiles: Tile list) : int =
        let doraKinds = markers |> List.map (ofMarker >> Tile.kindIndex)

        tiles
        |> List.sumBy (fun tile ->
            let index = Tile.kindIndex tile
            doraKinds |> List.filter (fun dora -> dora = index) |> List.length)

    /// 这一列牌里有几张红宝牌。
    let akadoraCount (tiles: Tile list) : int =
        tiles |> List.filter Tile.isAkadora |> List.length
