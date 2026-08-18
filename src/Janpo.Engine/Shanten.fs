namespace Janpo

/// 向听数：手牌到听牌的距离，还需几次有效替换才能听牌。
///
/// 数值约定（CONTEXT.md）：**0 表示 Tenpai（听牌）**；-1 表示已成和了型；
/// 正数是离听牌的距离，13 张手牌的上界是 8。
[<Struct>]
type Shanten = private | Shanten of value: int

/// 一批形态判定共用的暂存缓冲。**它是显式入参，不是全局状态**：
/// 各线程各自的批各自的缓冲，因此公开函数的纯度与可并发性不变
/// （属性测试开着 `Parallelism = 4/8`，共享一份可变数组会真的坏掉）。
///
/// 存在的理由：**产品从来不「调一次」向听，它按批调**。一次信息辅助档的决策要
/// 跑 ~400 次形态判定，每次都新建一份 34 长数组时，光这一项就是 .NET 上约 70 µs/决策、
/// 浏览器上约 0.7 ms/决策（`docs/research/engine-perf-caller-and-browser.md` §2.1 / §3.3）。
///
/// **四个格各有各的主**，不能互相借用——它们在同一条调用链上同时活着：
/// `Scaffold` 的试打结果（`Dahai`）要活到 `Ukeire` 算完，而 `Ukeire` 在它上面
/// 又要自己的试摸（`Tsumo`）与可见张数（`Seen`），最里层才是搜索（`Search`）。
type internal ShantenScratch =
    {
        /// 面子分解搜索原地增删的那一份副本。
        Search: int array
        /// 「试打一张之后」的手牌计数：`Scaffold` 的逐张试打、`RiichiState.tenpaiDahai`、
        /// `RandomPlayer` 的打牌评分。
        Dahai: int array
        /// 「试摸一张之后」的手牌计数：`Ukeire` 的逐种试摸、`AgariShape.waits`。
        Tsumo: int array
        /// 可见张数（手牌自己的 + 传入的）：`Ukeire`。
        Seen: int array
    }

/// 暂存缓冲的构造。**一批建一个**：进批之前建好，批内所有形态判定共用。
[<RequireQualifiedAccess>]
module internal ShantenScratch =

    /// 新建一份。**不要在循环里建**——那正是这个类型要消灭的那件事。
    let create () : ShantenScratch =
        {
            Search = Array.zeroCreate Tile.KindCount
            Dahai = Array.zeroCreate Tile.KindCount
            Tsumo = Array.zeroCreate Tile.KindCount
            Seen = Array.zeroCreate Tile.KindCount
        }

/// Shanten 的构造、计算与渲染。
///
/// 一般型走面子分解搜索（`8 - 2 * 面子 - 搭子 - 雀头`，面子加搭子上限 4 组），
/// 七对子与国士各有闭式公式，`calculate` 取三者最小值。
///
/// 搜索为了速度直接在 34 长计数数组上原地增删，但那份数组永远是调用方的（公开入口
/// 自己新建一份，批处理的入口收 `ShantenScratch`），手牌本身一个字节不动，
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
            let last = min 6 number

            // 原来这里是 `[ max 0 (number - 2) .. last ] |> List.exists ...`：每次调用分配一个小 list，
            // 而 `deadQuadKinds` 每碰上一种握满 4 张的牌就要问它一次。尾递归编成循环，不分配。
            let rec anyRun (start: int) =
                if start > last then
                    false
                elif
                    legal.[suitStart + start]
                    && legal.[suitStart + start + 1]
                    && legal.[suitStart + start + 2]
                then
                    true
                else
                    anyRun (start + 1)

            anyRun (max 0 (number - 2))

    /// 「死张」的种数：手里握满 4 张、且在这个规则集下永远进不了顺子的牌种。
    /// 它的第 4 张既凑不出需要第 5 张的刻子，也进不了顺子，只能打掉——每有一种就至少
    /// 多一次替换，因此它是向听数的下界。四麻里这就是字牌。
    ///
    /// 每次 `standard` 只调一次（不在递归里）。一道尾递归扫描：既不分配索引源，
    /// 也不付 34 次委托调用的钱（原来是预算好的索引数组 + `Array.sumBy` 闭包）。
    /// 它仍然是纯的，也不占可变绑定的预算（风格规则 5）。
    let private deadQuadKinds (legal: bool array) (original: int array) : int =
        let rec scan (index: int) (dead: int) =
            if index >= Tile.KindCount then
                dead
            elif original.[index] = 4 && not (canJoinRun legal index) then
                scan (index + 1) (dead + 1)
            else
                scan (index + 1) dead

        scan 0 0

    /// 面子分解搜索。`legal` 是规则集的牌种存在标志，`original` 是手牌的原始计数
    /// （判「4 张全在手里」用），`counts` 是可以原地增删的副本。
    ///
    /// 搭子只有补得上才算搭子：补齐它的牌种必须存在于规则集。三麻缺 2m-8m 时
    /// `1m3m` 不是搭子，这个判断就是四麻与三麻的分界。
    let private searchStandard (legal: bool array) (original: int array) (counts: int array) (nakiCount: int) : int =
        // `best` 是可变累加器而不是返回值，这是量过的取舍，不是懒（风格规则 5 要求注明理由）：
        // 它是**分支限界的上界**，要跨分支、跨子树一直活着——纯函数写法得把它当参数传下去再传回来，
        // 那是另一种算法形状。剪枝之前量过一次「每个分支返回子树最优、末尾 min 汇总」的纯写法：
        // 11.98–12.42 → 13.11–13.25 µs/次（约 +10%，超出噪声带）。那是**无剪枝**版本的数，
        // 剪枝之后没有重测，想试纯函数写法先拿 `scripts/fsi/` 的探针重新量一遍，别沿用旧数。
        let mutable best = 8

        // anyFloater / allFloatersAreQuads：孤张的牌种是不是全都握满了 4 张。
        // 没有雀头时雀头只能靠某张孤张凑对，孤张全是「4 张全在手」的牌种就凑不出来，多一次替换。
        //
        // `rem` 是 counts.[index..] 还剩几张牌：每个分支按吃掉的张数减（面子 -3、雀头与搭子 -2、
        // 孤张 -1），跳过空牌种时不变。它只用来算子树的下界。
        let rec search
            (index: int)
            (melds: int)
            (partials: int)
            (hasHead: bool)
            (anyFloater: bool)
            (allFloatersAreQuads: bool)
            (rem: int)
            =
            let headBonus = if hasHead then 1 else 0
            let current = 8 - 2 * melds - partials - headBonus
            // 子树里还能长出多少收益（面子 +2、搭子 +1、雀头 +1），两条独立上界取小者仍是上界：
            //   按组数：面子与搭子合计最多 4 组、雀头最多再一个，即 2 * (4 - melds - partials) + 1 - headBonus
            //   按张数：面子 3 张换 2、搭子与雀头 2 张换 1，每张最多贡献 2/3，即 2 * rem / 3
            // 叶子值 = current - 实际收益 + (unpairable ? 1 : 0)，而 unpairable 只会把叶子抬高，
            // 所以 current - maxGain 是整棵子树的下界：它 >= best 时子树里出不了更小的值，直接剪掉。
            let maxGain = min (2 * (4 - melds - partials) + 1 - headBonus) (2 * rem / 3)

            if current - maxGain >= best then
                ()
            elif maxGain = 0 && hasHead then
                // 提前收敛：再也长不出收益，且有雀头（unpairable 要求无雀头，这里必为 false），
                // 于是子树里每片叶子都恰等于 current，不必走到 index = 34。叶子数就是被这一支砍光的。
                //
                // `hasHead` 不能省：无雀头时 rem <= 1 同样让 2 * rem / 3 = 0，而那剩下的一张若是
                // 握满 4 张的牌种，叶子是 current + 1（孤张凑不出雀头，unpairable 成立），少记这个 1 就是错的。
                best <- current
            elif index >= Tile.KindCount then
                let unpairable = not hasHead && anyFloater && allFloatersAreQuads
                let candidate = current + (if unpairable then 1 else 0)

                if candidate < best then
                    best <- candidate
            elif counts.[index] = 0 then
                search (index + 1) melds partials hasHead anyFloater allFloatersAreQuads rem
            else
                let count = counts.[index]
                let number = numberInSuit index
                // 面子与搭子加起来最多 4 组（雀头另算）。这个上限对面子同样生效：
                // 先拆搭子再拆面子也不能凑出 5 组，否则「4 面子 + 1 搭子」会被当成和了型。
                let blocks = melds + partials

                // 刻子
                if count >= 3 && blocks < 4 then
                    counts.[index] <- count - 3
                    search index (melds + 1) partials hasHead anyFloater allFloatersAreQuads (rem - 3)
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
                    search index (melds + 1) partials hasHead anyFloater allFloatersAreQuads (rem - 3)
                    counts.[index] <- count
                    counts.[index + 1] <- counts.[index + 1] + 1
                    counts.[index + 2] <- counts.[index + 2] + 1

                // 雀头：已经成对，不必再摸
                if count >= 2 && not hasHead then
                    counts.[index] <- count - 2
                    search index melds partials true anyFloater allFloatersAreQuads (rem - 2)
                    counts.[index] <- count

                // 对子搭子：要变刻子还得再摸一张，4 张全在手里就永远变不成
                if count >= 2 && blocks < 4 && not (isQuad original index) then
                    counts.[index] <- count - 2
                    search index melds (partials + 1) hasHead anyFloater allFloatersAreQuads (rem - 2)
                    counts.[index] <- count

                // 两面 / 边张：补齐它的是 index-1 或 index+2，至少一个牌种存在才算搭子
                if number >= 0 && number <= 7 && blocks < 4 && counts.[index + 1] > 0 then
                    let completable =
                        (number >= 1 && legal.[index - 1]) || (number <= 6 && legal.[index + 2])

                    if completable then
                        counts.[index] <- count - 1
                        counts.[index + 1] <- counts.[index + 1] - 1
                        search index melds (partials + 1) hasHead anyFloater allFloatersAreQuads (rem - 2)
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
                    search index melds (partials + 1) hasHead anyFloater allFloatersAreQuads (rem - 2)
                    counts.[index] <- count
                    counts.[index + 2] <- counts.[index + 2] + 1

                // 孤张：这一张谁也不搭
                counts.[index] <- count - 1

                search index melds partials hasHead true (allFloatersAreQuads && isQuad original index) (rem - 1)

                counts.[index] <- count

        search 0 nakiCount 0 false false true (Array.sum counts)
        best

    /// `standard` 的真实实现，搜索用调用方给的 34 长缓冲（里面原来装的什么不要紧，这里先覆写）。
    /// 库外拿不到（`internal`）：它与 `HandShape.counts` 同一个性质的快路径。
    let internal standardIn (search: int array) (kindSet: TileKindSet) (hand: HandShape) : Shanten =
        let legal = TileKindSet.legalFlags kindSet
        let original = HandShape.counts hand
        HandShape.countsInto search hand
        let searched = searchStandard legal original search (HandShape.nakiCount hand)

        if searched <= -1 then
            agari
        else
            // 死张每种至少吃掉一次替换；已摸进的手牌（3n+2）本来就要打一张，白送一次。
            let spare = if HandShape.isAwaitingDraw hand then 0 else 1
            Shanten(max searched (max 0 (deadQuadKinds legal original - spare)))

    /// 一般型（四面子一雀头）的向听数。副露按已成面子计入。
    let standard (kindSet: TileKindSet) (hand: HandShape) : Shanten =
        standardIn (Array.zeroCreate Tile.KindCount) kindSet hand

    // ---- 七对子 ----

    /// 「这一型在这副手牌上不成立」的哨兵值：比任何真向听数都大（上限是国士的 13），
    /// 于是 `calculateIn` 三者取最小时它自动落选，不必拿 `Shanten option` 表达「没有」。
    [<Literal>]
    let private Unavailable = 99

    let private chiitoitsuValue (kindSet: TileKindSet) (hand: HandShape) : int =
        if HandShape.nakiCount hand > 0 || TileKindSet.count kindSet < 7 then
            Unavailable
        else
            let counts = HandShape.counts hand

            // 有牌的牌种数与成对的牌种数一遍扫描取齐（原来是两遍 `Array.sumBy`）。
            // 省的是遍历本身，不是闭包：两个平台上都成立（研究文档 §3.3(c)）。
            let rec scan (index: int) (kinds: int) (pairs: int) =
                if index >= Tile.KindCount then
                    // 牌种数不足 7 时，每缺一种就多要一次替换。
                    6 - pairs + max 0 (7 - kinds)
                else
                    let count = counts.[index]

                    scan (index + 1) (if count >= 1 then kinds + 1 else kinds) (if count >= 2 then pairs + 1 else pairs)

            scan 0 0 0

    /// 七对子的向听数。副露过就不成立（返回 None），牌种不足 7 种的规则集同理。
    let chiitoitsu (kindSet: TileKindSet) (hand: HandShape) : Shanten option =
        match chiitoitsuValue kindSet hand with
        | Unavailable -> None
        | shanten -> Some(Shanten shanten)

    // ---- 国士无双 ----

    /// 幺九牌的牌种索引：1m 9m 1p 9p 1s 9s 与七种字牌。
    let private yaochuuIndexes = [| 0; 8; 9; 17; 18; 26; 27; 28; 29; 30; 31; 32; 33 |]

    let private kokushiValue (kindSet: TileKindSet) (hand: HandShape) : int =
        let legal = TileKindSet.legalFlags kindSet

        if
            HandShape.nakiCount hand > 0
            || yaochuuIndexes |> Array.exists (fun index -> not legal.[index])
        then
            Unavailable
        else
            let counts = HandShape.counts hand

            // 幺九牌里有牌的牌种数与有没有对子，同样一遍扫描取齐（原来是 `sumBy` + `exists` 两遍）。
            let rec scan (position: int) (kinds: int) (hasPair: bool) =
                if position >= Array.length yaochuuIndexes then
                    13 - kinds - (if hasPair then 1 else 0)
                else
                    let count = counts.[yaochuuIndexes.[position]]
                    scan (position + 1) (if count >= 1 then kinds + 1 else kinds) (hasPair || count >= 2)

            scan 0 0 false

    /// 国士无双的向听数。副露过就不成立（返回 None），规则集缺任一幺九牌种同理。
    let kokushi (kindSet: TileKindSet) (hand: HandShape) : Shanten option =
        match kokushiValue kindSet hand with
        | Unavailable -> None
        | shanten -> Some(Shanten shanten)

    // ---- 三者取最小 ----

    /// 三型取最小。**内部走 int 不走 `Shanten option`**：公开的 `chiitoitsu` / `kokushi`
    /// 签名照旧，但它俩在这条路径上每手都要走一遍，拿 option 表达「不成立」
    /// 就是每手两次装箱加旧 `lower` 的两次偏应用。
    ///
    /// 实测（5 万手随机手牌）：.NET 上 `calculateWith` 96 → 24 B/手；
    /// **两个平台的时间都在噪声带内**（.NET 1.42–1.47 µs/手不变；node 26 / V8 上
    /// `Ukeire.calculate` 改前 149.9–152.3、改后 149.0–152.8 µs/决策）。
    /// 它省的是跑批的 GC 压力，不是时间——别拿它当快路径引。
    let private calculateIn (search: int array) (kindSet: TileKindSet) (hand: HandShape) : Shanten =
        standardIn search kindSet hand
        |> value
        |> min (chiitoitsuValue kindSet hand)
        |> min (kokushiValue kindSet hand)
        |> Shanten

    /// 手牌的向听数：一般型、七对子、国士三者的最小值。
    let calculate (kindSet: TileKindSet) (hand: HandShape) : Shanten =
        calculateIn (Array.zeroCreate Tile.KindCount) kindSet hand

    /// 同上，但搜索缓冲由调用方持有（批内共用）。**数值与 `calculate` 逐个相同**：
    /// 它们走的是同一份 `calculateIn`，差别只在那 34 个 int 从哪来。
    let internal calculateWith (scratch: ShantenScratch) (kindSet: TileKindSet) (hand: HandShape) : Shanten =
        calculateIn scratch.Search kindSet hand

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    /// 引擎判定不得消费它的输出——要数值就用 `value`。
    let toDisplay (shanten: Shanten) : string =
        match value shanten with
        | v when v <= -1 -> "和了"
        | 0 -> "听牌"
        | v -> $"{v} 向听"
