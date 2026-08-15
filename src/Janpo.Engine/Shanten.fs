namespace Janpo

/// 向听数：手牌到听牌的距离，还需几次有效替换才能听牌。
///
/// 数值约定（CONTEXT.md）：**0 表示 Tenpai（听牌）**；-1 表示已成和了型；
/// 正数是离听牌的距离，13 张手牌的上界是 8。
[<Struct>]
type Shanten = private | Shanten of value: int

/// Shanten 的构造、计算与渲染。
///
/// 一般型走面子分解搜索（`8 - 2 * 面子 - 搭子 - 雀头`，面子加搭子上限 4 组），
/// 七对子与国士各有闭式公式，`calculate` 取三者最小值。
///
/// 搜索为了速度直接在 34 长计数数组上原地增删，但每次调用都用自己的副本，
/// 公开函数仍是纯的、可并发调用。
[<RequireQualifiedAccess>]
module Shanten =

    // ---- 构造 ----

    /// 已成和了型：-1。
    let agari: Shanten = Shanten -1

    /// 听牌：0。
    let tenpai: Shanten = Shanten 0

    /// 数值：-1 和了，0 听牌，正数为离听牌的距离。
    let value (shanten: Shanten) : int =
        let (Shanten value) = shanten
        value

    /// 是否已成和了型。
    let isAgari (shanten: Shanten) : bool = value shanten <= -1

    /// 是否听牌。注意和了型不算听牌。
    let isTenpai (shanten: Shanten) : bool = value shanten = 0

    // ---- 一般型 ----

    /// 各花色的段：0-8 万子、9-17 筒子、18-26 索子、27-33 字牌。
    let private isSuupai (index: int) = index < Tile.KindCount - 7

    /// 数牌在花色内的序数 0-8；字牌返回 -1。
    let private numberInSuit (index: int) =
        if isSuupai index then index % 9 else -1

    /// 4 张全在自己手里的牌种：摸不到第 5 张。
    let private isQuad (original: int array) (index: int) = original.[index] = 4

    /// 这个牌种进不进得了顺子——取决于规则集里存不存在包含它的三连号。
    /// 四麻里字牌不行、数牌都行；三麻缺 2m-8m 时 1m / 9m 同样不行。
    let private canJoinRun (legal: bool array) (index: int) : bool =
        if not (isSuupai index) then
            false
        else
            let number = numberInSuit index
            let suitStart = index - number

            [ max 0 (number - 2) .. min 6 number ]
            |> List.exists (fun start ->
                legal.[suitStart + start]
                && legal.[suitStart + start + 1]
                && legal.[suitStart + start + 2])

    /// 「死张」的种数：手里握满 4 张、且在这个规则集下永远进不了顺子的牌种。
    /// 它的第 4 张既凑不出需要第 5 张的刻子，也进不了顺子，只能打掉——每有一种就至少
    /// 多一次替换，因此它是向听数的下界。四麻里这就是字牌。
    let private deadQuadKinds (legal: bool array) (original: int array) : int =
        let mutable dead = 0

        for index in 0 .. Tile.KindCount - 1 do
            if original.[index] = 4 && not (canJoinRun legal index) then
                dead <- dead + 1

        dead

    /// 面子分解搜索。`legal` 是规则集的牌种存在标志，`original` 是手牌的原始计数
    /// （判「4 张全在手里」用），`counts` 是可以原地增删的副本。
    ///
    /// 搭子只有补得上才算搭子：补齐它的牌种必须存在于规则集。三麻缺 2m-8m 时
    /// `1m3m` 不是搭子，这个判断就是四麻与三麻的分界。
    let private searchStandard (legal: bool array) (original: int array) (counts: int array) (nakiCount: int) : int =
        let mutable best = 8

        // anyFloater / allFloatersAreQuads：孤张的牌种是不是全都握满了 4 张。
        // 没有雀头时雀头只能靠某张孤张凑对，孤张全是「4 张全在手」的牌种就凑不出来，多一次替换。
        let rec search
            (index: int)
            (melds: int)
            (partials: int)
            (hasHead: bool)
            (anyFloater: bool)
            (allFloatersAreQuads: bool)
            =
            if index >= Tile.KindCount then
                let unpairable = not hasHead && anyFloater && allFloatersAreQuads

                let candidate =
                    8 - 2 * melds - partials - (if hasHead then 1 else 0)
                    + (if unpairable then 1 else 0)

                if candidate < best then
                    best <- candidate
            elif counts.[index] = 0 then
                search (index + 1) melds partials hasHead anyFloater allFloatersAreQuads
            else
                let count = counts.[index]
                let number = numberInSuit index
                // 面子与搭子加起来最多 4 组（雀头另算）。这个上限对面子同样生效：
                // 先拆搭子再拆面子也不能凑出 5 组，否则「4 面子 + 1 搭子」会被当成和了型。
                let blocks = melds + partials

                // 刻子
                if count >= 3 && blocks < 4 then
                    counts.[index] <- count - 3
                    search index (melds + 1) partials hasHead anyFloater allFloatersAreQuads
                    counts.[index] <- count

                // 顺子
                if
                    number >= 0
                    && number <= 6
                    && blocks < 4
                    && counts.[index + 1] > 0
                    && counts.[index + 2] > 0
                then
                    counts.[index] <- count - 1
                    counts.[index + 1] <- counts.[index + 1] - 1
                    counts.[index + 2] <- counts.[index + 2] - 1
                    search index (melds + 1) partials hasHead anyFloater allFloatersAreQuads
                    counts.[index] <- count
                    counts.[index + 1] <- counts.[index + 1] + 1
                    counts.[index + 2] <- counts.[index + 2] + 1

                // 雀头：已经成对，不必再摸
                if count >= 2 && not hasHead then
                    counts.[index] <- count - 2
                    search index melds partials true anyFloater allFloatersAreQuads
                    counts.[index] <- count

                // 对子搭子：要变刻子还得再摸一张，4 张全在手里就永远变不成
                if count >= 2 && blocks < 4 && not (isQuad original index) then
                    counts.[index] <- count - 2
                    search index melds (partials + 1) hasHead anyFloater allFloatersAreQuads
                    counts.[index] <- count

                // 两面 / 边张：补齐它的是 index-1 或 index+2，至少一个牌种存在才算搭子
                if number >= 0 && number <= 7 && blocks < 4 && counts.[index + 1] > 0 then
                    let completable =
                        (number >= 1 && legal.[index - 1]) || (number <= 6 && legal.[index + 2])

                    if completable then
                        counts.[index] <- count - 1
                        counts.[index + 1] <- counts.[index + 1] - 1
                        search index melds (partials + 1) hasHead anyFloater allFloatersAreQuads
                        counts.[index] <- count
                        counts.[index + 1] <- counts.[index + 1] + 1

                // 嵌张：补齐它的只有 index+1
                if
                    number >= 0
                    && number <= 6
                    && blocks < 4
                    && counts.[index + 2] > 0
                    && legal.[index + 1]
                then
                    counts.[index] <- count - 1
                    counts.[index + 2] <- counts.[index + 2] - 1
                    search index melds (partials + 1) hasHead anyFloater allFloatersAreQuads
                    counts.[index] <- count
                    counts.[index + 2] <- counts.[index + 2] + 1

                // 孤张：这一张谁也不搭
                counts.[index] <- count - 1
                search index melds partials hasHead true (allFloatersAreQuads && isQuad original index)
                counts.[index] <- count

        search 0 nakiCount 0 false false true
        best

    /// 一般型（四面子一雀头）的向听数。副露按已成面子计入。
    let standard (kindSet: TileKindSet) (hand: HandShape) : Shanten =
        let legal = TileKindSet.legalFlags kindSet
        let original = HandShape.counts hand

        let searched =
            searchStandard legal original (Array.copy original) (HandShape.nakiCount hand)

        if searched <= -1 then
            agari
        else
            // 死张每种至少吃掉一次替换；已摸进的手牌（3n+2）本来就要打一张，白送一次。
            let spare = if HandShape.isAwaitingDraw hand then 0 else 1
            Shanten(max searched (max 0 (deadQuadKinds legal original - spare)))

    // ---- 七对子 ----

    /// 七对子的向听数。副露过就不成立（返回 None），牌种不足 7 种的规则集同理。
    let chiitoitsu (kindSet: TileKindSet) (hand: HandShape) : Shanten option =
        if HandShape.nakiCount hand > 0 || TileKindSet.count kindSet < 7 then
            None
        else
            let counts = HandShape.counts hand
            let mutable pairs = 0
            let mutable kinds = 0

            for index in 0 .. Tile.KindCount - 1 do
                if counts.[index] >= 1 then
                    kinds <- kinds + 1

                if counts.[index] >= 2 then
                    pairs <- pairs + 1

            // 牌种数不足 7 时，每缺一种就多要一次替换。
            Some(Shanten(6 - pairs + max 0 (7 - kinds)))

    // ---- 国士无双 ----

    /// 幺九牌的牌种索引：1m 9m 1p 9p 1s 9s 与七种字牌。
    let private yaochuuIndexes = [| 0; 8; 9; 17; 18; 26; 27; 28; 29; 30; 31; 32; 33 |]

    /// 国士无双的向听数。副露过就不成立（返回 None），规则集缺任一幺九牌种同理。
    let kokushi (kindSet: TileKindSet) (hand: HandShape) : Shanten option =
        let legal = TileKindSet.legalFlags kindSet

        if
            HandShape.nakiCount hand > 0
            || yaochuuIndexes |> Array.exists (fun index -> not legal.[index])
        then
            None
        else
            let counts = HandShape.counts hand
            let mutable kinds = 0
            let mutable hasPair = false

            for index in yaochuuIndexes do
                if counts.[index] >= 1 then
                    kinds <- kinds + 1

                if counts.[index] >= 2 then
                    hasPair <- true

            Some(Shanten(13 - kinds - (if hasPair then 1 else 0)))

    // ---- 三者取最小 ----

    /// 手牌的向听数：一般型、七对子、国士三者的最小值。
    let calculate (kindSet: TileKindSet) (hand: HandShape) : Shanten =
        let lower (candidate: Shanten option) (best: Shanten) =
            match candidate with
            | Some shanten when value shanten < value best -> shanten
            | _ -> best

        standard kindSet hand
        |> lower (chiitoitsu kindSet hand)
        |> lower (kokushi kindSet hand)

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    /// 引擎判定不得消费它的输出——要数值就用 `value`。
    let toDisplay (shanten: Shanten) : string =
        match value shanten with
        | v when v <= -1 -> "和了"
        | 0 -> "听牌"
        | v -> $"{v} 向听"
