namespace Janpo

/// 有效牌：能让 Shanten 下降的牌种与各自的剩余枚数。
type Ukeire =
    {
        /// 计算时手牌的 Shanten。
        Shanten: Shanten
        /// 牌种 → 剩余枚数，按 mjai 顺序升序。只含摸到后 Shanten 会下降的牌种。
        /// 枚数基于可见信息，**可能为 0**（该牌种已经全部可见）——它仍是形态上的
        /// 有效牌，只是摸不到了。要按「摸得到的进张」排序就自己滤掉 0。
        Tiles: (Tile * int) list
    }

/// 有效牌计算失败的原因。是值，不是异常。
type UkeireError =
    /// 有效牌只对「等摸」的手牌（暗牌 `13 - 3 * nakiCount` 张）有定义；
    /// 已摸进的手牌要先试打一张。
    | HandNotAwaitingDraw of concealed: int * nakiCount: int
    /// 某牌种的可见张数（手牌 + 传入的可见牌）超过 4 张。
    | VisibleKindOverflow of tile: Tile * count: int

/// 有效牌的计算与渲染。
[<RequireQualifiedAccess>]
module Ukeire =

    // ---- 计算 ----

    /// 有效牌的真实实现：缓冲由调用方持有（批内共用），并且收一个「已知的当前向听」。
    ///
    /// **`known` 只允许是 `Shanten.calculate kindSet hand` 的值**（没有就给 None）：
    /// `Scaffold` 的逐张试打上一行刚算过同一副手牌，重算一次是每决策白花的 2.8%
    /// （`docs/research/engine-perf-caller-and-browser.md` §2.1）。给错了就是错的有效牌，
    /// 因此它库外拿不到（`internal`）。
    let internal calculateWith
        (scratch: ShantenScratch)
        (kindSet: TileKindSet)
        (known: Shanten option)
        (visible: Tile list)
        (hand: HandShape)
        : Result<Ukeire, UkeireError> =
        if not (HandShape.isAwaitingDraw hand) then
            Error(HandNotAwaitingDraw(HandShape.concealedCount hand, HandShape.nakiCount hand))
        else
            // 可见张数 = 手牌自己的 + 传入的。`HandShape.counts` 是内部数组改不得，
            // 拷进缓冲里再改（原来是每次调用新建一份）。
            let seen = scratch.Seen
            HandShape.countsInto seen hand

            for tile in visible do
                let index = Tile.kindIndex tile
                seen.[index] <- seen.[index] + 1

            let overflow =
                seen
                |> Array.tryFindIndex (fun count -> count > 4)
                |> Option.bind (fun index ->
                    Tile.ofKindIndex index
                    |> Option.map (fun tile -> VisibleKindOverflow(tile, seen.[index])))

            match overflow with
            | Some error -> Error error
            | None ->
                let current =
                    match known with
                    | Some shanten -> shanten
                    | None -> Shanten.calculateWith scratch kindSet hand

                // 逐种试摸都在同一份缓冲上做：加一张、算、再减回去。
                // 原来每一种要新建两个 34 长数组（`HandShape.add` 一个、搜索副本一个）。
                let drawn = scratch.Tsumo
                HandShape.countsInto drawn hand
                let trial = HandShape.ofScratch (HandShape.nakiCount hand) drawn

                let improving =
                    TileKindSet.kinds kindSet
                    |> List.choose (fun tile ->
                        let index = Tile.kindIndex tile

                        // 手里已有 4 张的牌种摸不到第 5 张，不是有效牌。
                        if drawn.[index] >= 4 then
                            None
                        else
                            drawn.[index] <- drawn.[index] + 1
                            let after = Shanten.calculateWith scratch kindSet trial
                            drawn.[index] <- drawn.[index] - 1

                            if Shanten.value after < Shanten.value current then
                                Some(tile, 4 - seen.[index])
                            else
                                None)

                Ok { Shanten = current; Tiles = improving }

    /// 手牌的有效牌。
    ///
    /// `visible` 是**手牌之外**的可见牌：四家牌河、所有副露（含自己的）、宝牌指示牌。
    /// 手牌自己的张数由 `hand` 扣除，不要在 `visible` 里重复给。
    /// 剩余枚数 = `4 - 手牌张数 - visible 张数`。
    let calculate (kindSet: TileKindSet) (visible: Tile list) (hand: HandShape) : Result<Ukeire, UkeireError> =
        calculateWith (ShantenScratch.create ()) kindSet None visible hand

    // ---- 拆解 ----

    /// 有效牌的总枚数（基于可见信息）。
    let total (ukeire: Ukeire) : int = ukeire.Tiles |> List.sumBy snd

    /// 有效牌的牌种数，含已经摸不到的（剩余 0 枚）。
    let kindCount (ukeire: Ukeire) : int = List.length ukeire.Tiles

    /// 只保留还摸得到的牌种。
    let live (ukeire: Ukeire) : (Tile * int) list =
        ukeire.Tiles |> List.filter (fun (_, remaining) -> remaining > 0)

    /// 有效牌的 mjai 记法列，按牌种升序、单空格分隔。
    let toMjai (ukeire: Ukeire) : string =
        ukeire.Tiles |> List.map fst |> Tile.toMjaiMany

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (ukeire: Ukeire) : string =
        if List.isEmpty ukeire.Tiles then
            $"{Shanten.toDisplay ukeire.Shanten}，无有效牌"
        else
            let tiles =
                ukeire.Tiles
                |> List.map (fun (tile, remaining) -> $"{Tile.toDisplay tile}({remaining})")
                |> String.concat " "

            $"{Shanten.toDisplay ukeire.Shanten}，有效牌 {total ukeire} 枚：{tiles}"

/// 有效牌计算错误的渲染。
[<RequireQualifiedAccess>]
module UkeireError =

    /// **渲染层的单向出口**：错误原因的中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: UkeireError) : string =
        match error with
        | HandNotAwaitingDraw(concealed, nakiCount) ->
            $"有效牌只对等摸的手牌有定义：副露 {nakiCount} 组时暗牌应为 {13 - 3 * nakiCount} 张，得到 {concealed} 张"
        | VisibleKindOverflow(tile, count) -> $"{Tile.toDisplay tile}的可见张数是 {count}，最多只有 4 张"
