namespace Janpo

/// 构造和了牌姿失败的原因。是值，不是异常。
type AgariHandError =
    /// 副露数不在 0-4。
    | AgariNakiCountOutOfRange of nakiCount: int
    /// 暗牌张数与副露数对不上：和了时应为 `14 - 3 * 副露数`（杠也算一组）。
    | AgariConcealedCountMismatch of concealed: int * nakiCount: int
    /// 同一牌种（暗牌加副露）超过 4 张。
    | AgariKindOverflow of tile: Tile * count: int
    /// 和了牌不在暗牌里。荣和时和了牌也要算进暗牌。
    | AgariTileNotInHand of tile: Tile

/// 和了的牌姿：暗牌（含和了牌）+ 副露 + 和了牌 + 自摸还是荣和。
///
/// 它比 `HandShape` 多的正是役与符要看的东西——`HandShape` 只有副露**数**，
/// 而三色同刻、一气通贯、対々和、门清限定役与副露降番都要副露的**内容**。
/// 形态判定仍然走 `HandShape`（`toHandShape`），这里不重复一份分解规则。
///
/// 红宝牌**保留**：宝牌番数要数它。牌种一律经 `Tile.deaka` 归并。
type AgariHand =
    private
        {
            /// 暗牌，含和了牌，保留红宝牌，按 mjai 顺序升序。
            Concealed: Tile list
            /// 副露，按鸣的先后。
            Naki: Naki list
            /// 和了牌（保留红宝牌）。
            WinningTile: Tile
            /// 自摸为 true，荣和为 false。
            Tsumo: bool
            /// 形态判定用的手牌形态，构造时就算好——它一定成立，下游不必再接一个 Result。
            Shape: HandShape
        }

/// 和了牌姿的构造与拆解。
[<RequireQualifiedAccess>]
module AgariHand =

    // ---- 构造 ----

    let private create (tsumo: bool) (naki: Naki list) (concealed: Tile list) (winningTile: Tile) =
        let nakiCount = List.length naki

        if nakiCount > 4 then
            Error(AgariNakiCountOutOfRange nakiCount)
        elif List.length concealed <> 14 - 3 * nakiCount then
            Error(AgariConcealedCountMismatch(List.length concealed, nakiCount))
        elif not (concealed |> List.exists (fun tile -> Tile.deaka tile = Tile.deaka winningTile)) then
            Error(AgariTileNotInHand(Tile.deaka winningTile))
        else
            let counts = Array.zeroCreate Tile.KindCount

            for tile in concealed @ List.collect Naki.tiles naki do
                let index = Tile.kindIndex tile
                counts.[index] <- counts.[index] + 1

            let overflow =
                counts
                |> Array.tryFindIndex (fun count -> count > 4)
                |> Option.bind (fun index ->
                    Tile.ofKindIndex index
                    |> Option.map (fun tile -> AgariKindOverflow(tile, counts.[index])))

            match overflow with
            | Some error -> Error error
            | None ->
                // 张数与牌种上限都验过了，这里不会失败；仍然把错误当值传下去，不抛异常。
                match HandShape.create nakiCount concealed with
                | Error(NakiCountOutOfRange count) -> Error(AgariNakiCountOutOfRange count)
                | Error(ConcealedCountMismatch(count, naki)) -> Error(AgariConcealedCountMismatch(count, naki))
                | Error(TileKindOverflow(tile, count)) -> Error(AgariKindOverflow(tile, count))
                | Error(TileNotInHand tile) -> Error(AgariTileNotInHand tile)
                | Ok shape ->
                    Ok
                        {
                            Concealed = Tile.sort concealed
                            Naki = naki
                            WinningTile = winningTile
                            Tsumo = tsumo
                            Shape = shape
                        }

    /// 自摸和了。`concealed` 含摸进的那张，张数恒为 `14 - 3 * 副露数`。
    let tsumo (naki: Naki list) (concealed: Tile list) (winningTile: Tile) : Result<AgariHand, AgariHandError> =
        create true naki concealed winningTile

    /// 荣和。`concealed` 含点出来的那张——和了牌在牌型上就是手牌的一部分。
    let ron (naki: Naki list) (concealed: Tile list) (winningTile: Tile) : Result<AgariHand, AgariHandError> =
        create false naki concealed winningTile

    // ---- 拆解 ----

    /// 暗牌，含和了牌，保留红宝牌，升序。
    let concealed (hand: AgariHand) : Tile list = hand.Concealed

    /// 副露，按鸣的先后。
    let naki (hand: AgariHand) : Naki list = hand.Naki

    /// 和了牌（保留红宝牌）。
    let winningTile (hand: AgariHand) : Tile = hand.WinningTile

    /// 自摸为 true，荣和为 false。
    let isTsumo (hand: AgariHand) : bool = hand.Tsumo

    /// 门清：一组副露都没有，或只有暗杠。门清限定役与门清荣和符看的就是它。
    let isMenzen (hand: AgariHand) : bool =
        hand.Naki |> List.forall Naki.isConcealed

    /// 手上的全部牌：暗牌加副露的牌，保留红宝牌。杠是 4 张，所以总数是 `14 + 杠数`。
    let tiles (hand: AgariHand) : Tile list =
        hand.Concealed @ List.collect Naki.tiles hand.Naki

    /// 形态判定用的手牌形态（去红、副露只留个数）。和了型仍由 `AgariShape.classify` 判。
    let toHandShape (hand: AgariHand) : HandShape = hand.Shape

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (hand: AgariHand) : string =
        let concealed = hand.Concealed |> List.map Tile.toDisplay |> String.concat " "
        let naki = hand.Naki |> List.map Naki.toDisplay |> String.concat " "
        let how = if hand.Tsumo then "自摸" else "荣和"
        let winning = Tile.toDisplay hand.WinningTile

        if List.isEmpty hand.Naki then
            $"{concealed}（{how}{winning}）"
        else
            $"{concealed} {naki}（{how}{winning}）"

/// 和了牌姿构造错误的渲染。
[<RequireQualifiedAccess>]
module AgariHandError =

    /// **渲染层的单向出口**：错误原因的中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: AgariHandError) : string =
        match error with
        | AgariNakiCountOutOfRange nakiCount -> $"副露数只能是 0-4，得到 {nakiCount}"
        | AgariConcealedCountMismatch(concealed, nakiCount) ->
            $"副露 {nakiCount} 组时和了的暗牌应为 {14 - 3 * nakiCount} 张，得到 {concealed} 张"
        | AgariKindOverflow(tile, count) -> $"{Tile.toDisplay tile}有 {count} 张，同一种牌最多 4 张"
        | AgariTileNotInHand tile -> $"和了牌{Tile.toDisplay tile}不在暗牌里"
