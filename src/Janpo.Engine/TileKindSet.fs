namespace Janpo

/// 规则集里「哪些牌种存在」。四麻是全 34 种（`TileKindSet.fourPlayer`）。
///
/// 形态判定（Shanten / Ukeire / 和了型）把它当**显式入参**，不把「34 种全存在」
/// 写进算法内部假设：三麻缺 2m-8m，`1m3m` 这样的嵌张永远补不上，同一副牌的
/// Shanten 与四麻不同。牌种集合是这条差异唯一正确的接缝。
///
/// 红宝牌与对应正牌同属一个牌种（`Tile.kindIndex` 相同）。
type TileKindSet =
    private
        {
            /// 34 长度：该牌种是否存在。
            Legal: bool array
        }

/// 牌种集合的构造与拆解。
[<RequireQualifiedAccess>]
module TileKindSet =

    // ---- 构造 ----

    /// 由一列牌构造牌种集合；重复与红宝牌都归到同一牌种。
    let ofKinds (tiles: Tile list) : TileKindSet =
        let legal = Array.zeroCreate Tile.KindCount

        for tile in tiles do
            legal.[Tile.kindIndex tile] <- true

        { Legal = legal }

    /// 四麻：全 34 种牌都存在。
    let fourPlayer: TileKindSet = ofKinds Tile.kinds

    // ---- 拆解 ----

    /// 该牌种是否存在于此规则集。
    let contains (tile: Tile) (kindSet: TileKindSet) : bool = kindSet.Legal.[Tile.kindIndex tile]

    /// 集合内的牌种，按 mjai 顺序升序。
    let kinds (kindSet: TileKindSet) : Tile list =
        Tile.kinds |> List.filter (fun tile -> contains tile kindSet)

    /// 集合内的牌种数。
    let count (kindSet: TileKindSet) : int =
        kindSet.Legal |> Array.sumBy (fun legal -> if legal then 1 else 0)

    /// 引擎内部的快路径：34 长度的存在标志数组。**不要写它**——形态判定按牌种索引
    /// 高频读取，复制一份的代价比判定本身还大。库外拿不到（`internal`）。
    let internal legalFlags (kindSet: TileKindSet) : bool array = kindSet.Legal
