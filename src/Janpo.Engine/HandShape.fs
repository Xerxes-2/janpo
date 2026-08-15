namespace Janpo

/// 构造手牌形态失败的原因。是值，不是异常。
type HandShapeError =
    /// 副露数不在 0-4。
    | NakiCountOutOfRange of nakiCount: int
    /// 暗牌张数与副露数对不上：应为 `13 - 3 * nakiCount`（等摸）或 `14 - 3 * nakiCount`（已摸）。
    | ConcealedCountMismatch of concealed: int * nakiCount: int
    /// 同一牌种超过 4 张。
    | TileKindOverflow of tile: Tile * count: int
    /// 要打掉的牌不在手里。
    | TileNotInHand of tile: Tile

/// 形态判定的输入：去红后的暗牌 + 已成型的副露数。
///
/// 副露对形态只贡献「已成面子数」——它的牌面不影响 Shanten、Ukeire 与和了型
/// （役与符要看牌面，那是另外的票）。杠也按一个面子算。
/// 红宝牌在构造时一律 `deaka`：`5mr` 与 `5m` 是同一个牌种。
///
/// 张数是受约束的：暗牌恰为 `13 - 3 * nakiCount`（等摸）或 `14 - 3 * nakiCount`（已摸）。
type HandShape =
    private
        {
            /// 34 长度的牌种计数。
            Counts: int array
            /// 已成型的副露数 0-4。
            Naki: int
        }

/// 手牌形态的构造与拆解。
[<RequireQualifiedAccess>]
module HandShape =

    // ---- 构造 ----

    let private ofCounts (nakiCount: int) (counts: int array) : Result<HandShape, HandShapeError> =
        if nakiCount < 0 || nakiCount > 4 then
            Error(NakiCountOutOfRange nakiCount)
        else
            let overflow =
                counts
                |> Array.tryFindIndex (fun count -> count > 4)
                |> Option.bind (fun index ->
                    Tile.ofKindIndex index
                    |> Option.map (fun tile -> TileKindOverflow(tile, counts.[index])))

            match overflow with
            | Some error -> Error error
            | None ->
                let total = Array.sum counts
                let awaiting = 13 - 3 * nakiCount

                if total <> awaiting && total <> awaiting + 1 then
                    Error(ConcealedCountMismatch(total, nakiCount))
                else
                    Ok { Counts = counts; Naki = nakiCount }

    /// 由暗牌与副露数构造。暗牌顺序无关，红宝牌自动去红。
    let create (nakiCount: int) (concealed: Tile list) : Result<HandShape, HandShapeError> =
        let counts = Array.zeroCreate Tile.KindCount

        for tile in concealed do
            let index = Tile.kindIndex tile
            counts.[index] <- counts.[index] + 1

        ofCounts nakiCount counts

    /// 门清手牌：副露数为 0。
    let ofConcealed (concealed: Tile list) : Result<HandShape, HandShapeError> = create 0 concealed

    /// 摸进一张。张数越界（往已摸的手牌里再摸）返回 `ConcealedCountMismatch`。
    let add (tile: Tile) (hand: HandShape) : Result<HandShape, HandShapeError> =
        let counts = Array.copy hand.Counts
        let index = Tile.kindIndex tile
        counts.[index] <- counts.[index] + 1
        ofCounts hand.Naki counts

    /// 打出一张。不在手里返回 `TileNotInHand`。
    let remove (tile: Tile) (hand: HandShape) : Result<HandShape, HandShapeError> =
        let index = Tile.kindIndex tile

        if hand.Counts.[index] = 0 then
            Error(TileNotInHand(Tile.deaka tile))
        else
            let counts = Array.copy hand.Counts
            counts.[index] <- counts.[index] - 1
            ofCounts hand.Naki counts

    // ---- 拆解 ----

    /// 已成型的副露数。
    let nakiCount (hand: HandShape) : int = hand.Naki

    /// 暗牌张数。
    let concealedCount (hand: HandShape) : int = Array.sum hand.Counts

    /// 某牌种在暗牌里的张数（红宝牌与正牌合并计）。
    let countOf (tile: Tile) (hand: HandShape) : int = hand.Counts.[Tile.kindIndex tile]

    /// 暗牌，去红后按 mjai 顺序升序。
    let tiles (hand: HandShape) : Tile list =
        [
            for index in 0 .. Tile.KindCount - 1 do
                match Tile.ofKindIndex index with
                | Some tile -> for _ in 1 .. hand.Counts.[index] -> tile
                | None -> ()
        ]

    /// 是否「等摸」的手牌（暗牌 `13 - 3 * nakiCount` 张）。
    /// 有效牌只对它有定义；已摸进的手牌要先试打一张。
    let isAwaitingDraw (hand: HandShape) : bool =
        concealedCount hand = 13 - 3 * hand.Naki

    /// 引擎内部的快路径：34 长度的牌种计数。**调用方必须自己复制后再改**——
    /// 形态判定按牌种索引高频读写，每次复制的代价比判定本身还大。库外拿不到（`internal`）。
    let internal counts (hand: HandShape) : int array = hand.Counts

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (hand: HandShape) : string =
        let concealed = tiles hand |> List.map Tile.toDisplay |> String.concat " "

        if hand.Naki = 0 then
            concealed
        else
            $"{concealed}（副露 {hand.Naki} 组）"

/// 手牌形态构造错误的渲染。
[<RequireQualifiedAccess>]
module HandShapeError =

    /// **渲染层的单向出口**：错误原因的中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: HandShapeError) : string =
        match error with
        | NakiCountOutOfRange nakiCount -> $"副露数只能是 0-4，得到 {nakiCount}"
        | ConcealedCountMismatch(concealed, nakiCount) ->
            let awaiting = 13 - 3 * nakiCount
            $"副露 {nakiCount} 组时暗牌应为 {awaiting} 或 {awaiting + 1} 张，得到 {concealed} 张"
        | TileKindOverflow(tile, count) -> $"{Tile.toDisplay tile}有 {count} 张，同一种牌最多 4 张"
        | TileNotInHand tile -> $"手里没有{Tile.toDisplay tile}"
