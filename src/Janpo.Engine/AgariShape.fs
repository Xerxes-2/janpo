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

    /// 这副牌同时成立的全部和了型，按 `Standard` `Chiitoitsu` `Kokushi` 顺序；
    /// 空表示不是和了型。和了型即「Shanten 为 -1」，与 `Shanten` 同一套分解，不另立一份规则。
    let classify (kindSet: TileKindSet) (hand: HandShape) : AgariShape list =
        [
            if Shanten.isAgari (Shanten.standard kindSet hand) then
                Standard

            match Shanten.chiitoitsu kindSet hand with
            | Some shanten when Shanten.isAgari shanten -> Chiitoitsu
            | _ -> ()

            match Shanten.kokushi kindSet hand with
            | Some shanten when Shanten.isAgari shanten -> Kokushi
            | _ -> ()
        ]

    /// 是否已成和了型（任意一种）。
    let isAgari (kindSet: TileKindSet) (hand: HandShape) : bool =
        not (List.isEmpty (classify kindSet hand))

    /// 这手**等摸**的牌听什么：补上它就成和了型的那些牌种，按 mjai 顺序升序。
    /// 张数不对（已经摸进那张）时是空表。
    ///
    /// **听什么只有这一份**：振听（06 经 `PlayerState.waits`）与立直后的暗杠判据
    /// （09 的 `RiichiState`）读的都是它。
    let waits (kindSet: TileKindSet) (hand: HandShape) : Tile list =
        TileKindSet.kinds kindSet
        |> List.filter (fun kind ->
            match HandShape.add kind hand with
            | Ok drawn -> isAgari kindSet drawn
            | Error _ -> false)

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (shape: AgariShape) : string =
        match shape with
        | Standard -> "一般型"
        | Chiitoitsu -> "七对子"
        | Kokushi -> "国士无双"
