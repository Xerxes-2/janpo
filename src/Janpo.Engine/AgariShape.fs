namespace Janpo

/// 和了型：手牌成和的牌型骨架。**只判型，不含役与点数**。
///
/// 一副牌可以同时成立多种型（二盃口的手同时是一般型与七对子），
/// 因此判定的结果是一组型而不是一个。
type AgariShape =
    /// 一般型：四面子一雀头（副露按已成面子计）。
    | Standard
    /// 七对子：门清七组不同的对子。
    | Chiitoitsu
    /// 国士无双：十三种幺九牌齐全，其中一种成对。
    | Kokushi

/// 和了型判定。
[<RequireQualifiedAccess>]
module AgariShape =

    // ---- 判定 ----

    /// `classify` 的真实实现。`search` 是一般型搜索的 34 长缓冲，由调用方持有（批内共用）。
    let internal classifyIn (search: int array) (kindSet: TileKindSet) (hand: HandShape) : AgariShape list =
        [
            if Shanten.isAgari (Shanten.standardIn search kindSet hand) then
                Standard

            match Shanten.chiitoitsu kindSet hand with
            | Some shanten when Shanten.isAgari shanten -> Chiitoitsu
            | _ -> ()

            match Shanten.kokushi kindSet hand with
            | Some shanten when Shanten.isAgari shanten -> Kokushi
            | _ -> ()
        ]

    /// 这副牌同时成立的全部和了型，按 `Standard` `Chiitoitsu` `Kokushi` 顺序；
    /// 空表示不是和了型。和了型即「Shanten 为 -1」，与 `Shanten` 同一套分解，不另立一份规则。
    let classify (kindSet: TileKindSet) (hand: HandShape) : AgariShape list =
        classifyIn (Array.zeroCreate Tile.KindCount) kindSet hand

    /// 是否已成和了型（任意一种）。
    let isAgari (kindSet: TileKindSet) (hand: HandShape) : bool =
        classify kindSet hand |> List.isEmpty |> not

    /// 这手**等摸**的牌听什么：补上它就成和了型的那些牌种，按 mjai 顺序升序。
    /// 张数不对（已经摸进那张）时是空表。
    ///
    /// **听什么只有这一份**：振听（06 经 `PlayerState.waits`）与立直后的暗杠判据
    /// （09 的 `RiichiState`）读的都是它。
    ///
    /// **逐种试摸是一批 34 次形态判定**：缓冲在进批之前建一个，批内共用。原来每一种要新建
    /// 两个 34 长数组（`HandShape.add` 一个、一般型搜索的副本一个），一次 `waits` 就是 68 个；
    /// 而永久振听每手重算一次，它是 `Observation` 重放里最贵的一段。
    ///
    /// **进批之前先花一次向听计算把不听牌的手挡掉**（票 66，研究见
    /// `docs/research/step-cost-on-replay-path.md` §5）：重放实测 83.6% 的打牌后手牌不听牌，
    /// 那些手的 34 次试摸全部返回空——等摸手上 `向听 = 0 ⟺ ∃ 牌种补上成和了型`
    /// （ShantenProperties 的等价性属性守着，票 64 另有 20 万手采样零反例；
    /// `Shanten.standardIn` 的 `deadQuadKinds` 修正正是它在「听自己抓完了的牌」上仍成立的原因）。
    /// 剪枝判据是 `> 0` 不是 `<> 0`：取最保守的一侧，向听 ≤ 0 的手一律照常试摸
    /// （等摸形向听不会是 −1，但这一条不在这里赌）。
    let waits (kindSet: TileKindSet) (hand: HandShape) : Tile list =
        // 不是等摸形的手牌再摸一张就超了张数：原来由 `HandShape.add` 的全量校验逐种拒掉，
        // 这里一次问清楚，结果同为空表。
        if not (HandShape.isAwaitingDraw hand) then
            []
        else
            let scratch = ShantenScratch.create ()

            if Shanten.value (Shanten.calculateWith scratch kindSet hand) > 0 then
                []
            else
                let drawn = scratch.Tsumo
                HandShape.countsInto drawn hand
                let trial = HandShape.ofScratch (HandShape.nakiCount hand) drawn

                TileKindSet.kinds kindSet
                |> List.filter (fun kind ->
                    let index = Tile.kindIndex kind

                    // 手里已有 4 张的牌种摸不到第 5 张（原来是 `HandShape.add` 的 `TileKindOverflow`）。
                    if drawn.[index] >= 4 then
                        false
                    else
                        drawn.[index] <- drawn.[index] + 1
                        let agari = classifyIn scratch.Search kindSet trial |> List.isEmpty |> not
                        drawn.[index] <- drawn.[index] - 1
                        agari)

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (shape: AgariShape) : string =
        match shape with
        | Standard -> "一般型"
        | Chiitoitsu -> "七对子"
        | Kokushi -> "国士无双"
