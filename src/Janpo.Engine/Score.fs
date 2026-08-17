namespace Janpo

/// 和了点的级别。役满以下按番数封顶，役满按倍数。
///
/// `[<RequireQualifiedAccess>]`：`Yakuman` 与 `YakuValue.Yakuman`、`Mangan` 与
/// 规则集开关 `KiriageMangan` 都在同一个命名空间里打转，一律写 `Limit.Mangan`。
[<RequireQualifiedAccess>]
type Limit =
    /// 满贯以下：点数由符与番按 `符 × 2^(2+番)` 算出。
    | Normal
    /// 满贯：5 番，或基本点封顶到 2000（4 番 40 符以上；开着切上满贯时含 4 番 30 符）。
    | Mangan
    /// 跳满：6-7 番。
    | Haneman
    /// 倍满：8-10 番。
    | Baiman
    /// 三倍满：11-12 番。
    | Sanbaiman
    /// 数え役满：13 番以上。
    | Kazoe
    /// 役满：役满倍数 ≥ 1。
    | Yakuman

/// 点数表的输入：符、番与役满倍数。**只有这三项决定基本点**，
/// 谁和了、谁点的、几本场都不影响它（那些是 `HoraTransfer` 的事）。
type HoraValue =
    {
        /// 符（已切上）。役满时不参与算点，仍照结构算出来备查。
        Fu: int
        /// 番：役 + 宝牌。役满时为 0——役满不看番，也不加宝牌。
        Han: int
        /// 役满倍数。0 表示不是役满；役满复合时是各役倍数之和。
        Yakuman: int
    }

/// 一次和了的授受条件：谁和了、点了谁、亲是谁、本场与供托。
///
/// 同巡双响时**本场与供托只归排在最前的那一家**（头跳的顺序，即打牌者下家优先），
/// 因此靠后那家的 `Honba` / `Kyotaku` 由调用方填 0。
type HoraTransfer =
    {
        /// 和了的座位。
        Actor: Seat
        /// 放铳的座位；**自摸时等于 `Actor`**（mjai 的约定，也是这里判自摸的依据）。
        Target: Seat
        /// 亲。授受按 Oya / Ko 分两套。
        Oya: Seat
        /// 本场。
        Honba: int
        /// 供托：场上堆着的立直棒**根数**，由和了者取得。
        Kyotaku: int
        /// Sekinin Barai（责任支付，CONTEXT.md）：要为这一和负责的那家，没有则为 None。
        ///
        /// **它只改「谁付」，不改「付多少」**：符、番、基本点与级别一律照算（`Fu` /
        /// `basePoints` / `limit` 都不受影响），只把账改挂到责任者头上：
        ///
        /// - 自摸：责任者一家付全额（大明杠后的岭上开花就是这条）；
        /// - 荣和且放铳者就是责任者：照常付；
        /// - 荣和且放铳者另有其人：两家各付一半（包牌的通行算法）。
        Sekinin: Seat option
    }

/// 一次和了的点数与授受。
type HoraScore =
    {
        /// 符、番与役满倍数。
        Value: HoraValue
        /// 级别。
        Limit: Limit
        /// 和了点：**不含**本场与供托的授受合计（mjai 的 `hora_points`）。
        HoraPoints: int
        /// 本次授受，按座位升序。**含**本场与供托，因此各项之和 = 供托点数
        /// （没有供托时为 0）；这是「四家点数与供托之和不变」那条不变量的另一种写法。
        Deltas: int list
    }

/// 一副和了牌姿的点数结论：选中的读法与它的符番。高点法在全部读法上比的就是它。
type HoraReading =
    {
        /// 选中的读法（役、宝牌与面子分解）。
        Tally: YakuTally
        /// 这一种读法的符、番与役满倍数。
        Value: HoraValue
    }

/// 点数表的输入的构造。
[<RequireQualifiedAccess>]
module HoraValue =

    // ---- 构造 ----

    /// 由一种读法算出符与番。役满时番记 0——役满不看番，宝牌也不加。
    let ofTally (ruleset: Ruleset) (context: YakuContext) (hand: AgariHand) (tally: YakuTally) : HoraValue =
        let yakuman = YakuTally.yakuman tally

        {
            Fu = Fu.value (Fu.calculate ruleset context hand tally)
            Han =
                if yakuman > 0 then
                    0
                else
                    YakuTally.han tally + YakuTally.doraTotal tally
            Yakuman = yakuman
        }

    // ---- 拆解 ----

    /// mjai `hora` 的 `fan` 字段：役满按 13 番一倍记（与数え役满同值），其余就是番数。
    /// 点数**不**由它反推（役满与数え役满的点数相同，因此这个折算不丢信息）。
    let fan (value: HoraValue) : int =
        if value.Yakuman > 0 then 13 * value.Yakuman else value.Han

/// 点数表与授受。**纯函数**：符 + 番 + 授受条件 → 各家点数增减。
[<RequireQualifiedAccess>]
module Score =

    // ---- 点数表 ----

    /// 每一笔支付各自切上到 100 的倍数（不是最后合计切上）。
    let private ceil100 (points: int) : int = (points + 99) / 100 * 100

    /// 满贯的基本点。跳满 1.5 倍、倍满 2 倍、三倍满 3 倍、役满 4 倍，都由它推出。
    let private manganBase = 2000

    /// 基本点：`符 × 2^(2+番)`，满贯以上按级别封顶。子荣和是它的 4 倍、亲荣和 6 倍。
    ///
    /// 切上满贯（4 番 30 符 / 3 番 60 符，基本点 1920）**默认关**：
    /// 天凤与雀魂段位战都不采用（DECISIONS 提案 S-A）。
    let basePoints (ruleset: Ruleset) (value: HoraValue) : int =
        if value.Yakuman > 0 then
            manganBase * 4 * value.Yakuman
        elif value.Han >= 13 then
            manganBase * 4
        elif value.Han >= 11 then
            manganBase * 3
        elif value.Han >= 8 then
            manganBase * 2
        elif value.Han >= 6 then
            manganBase * 3 / 2
        elif value.Han >= 5 then
            manganBase
        else
            // 番数封顶在 4 以下，位移最多 6 位，不会溢出。
            let raw = max 0 value.Fu * (1 <<< (2 + max 0 value.Han))

            if raw >= manganBase then manganBase
            elif ruleset.KiriageMangan && raw >= 1920 then manganBase
            else raw

    /// 级别。满贯以下的 4 番 40 符这类「基本点封顶」也算满贯。
    let limit (ruleset: Ruleset) (value: HoraValue) : Limit =
        if value.Yakuman > 0 then
            Limit.Yakuman
        elif value.Han >= 13 then
            Limit.Kazoe
        elif value.Han >= 11 then
            Limit.Sanbaiman
        elif value.Han >= 8 then
            Limit.Baiman
        elif value.Han >= 6 then
            Limit.Haneman
        elif value.Han >= 5 then
            Limit.Mangan
        elif basePoints ruleset value >= manganBase then
            Limit.Mangan
        else
            Limit.Normal

    // ---- 授受 ----

    /// 一次和了的点数与授受。
    ///
    /// 子自摸「子付 1 份、亲付 2 份」，亲自摸「三家各付 2 份」；
    /// 荣和由放铳者一家付子 4 份 / 亲 6 份。每一笔各自切上到 100。
    /// 本场按 `HonbaPoints` 计：荣和由放铳者付（**有包时改由责任者付**，见 `honbaPayers`），
    /// 自摸由付家平摊。供托归和了者，有包没包都一样。
    ///
    /// **责任支付（`transfer.Sekinin`）只换付钱的人**：和了点一分不多一分不少，
    /// `HoraPoints` 因此与没包时完全一样，只是 `Deltas` 里负的那几项换了座位。
    let hora (ruleset: Ruleset) (transfer: HoraTransfer) (value: HoraValue) : HoraScore =
        let seats = Seat.all ruleset
        let basis = basePoints ruleset value
        let tsumo = transfer.Actor = transfer.Target
        let actorIsOya = transfer.Actor = transfer.Oya

        let payers =
            if tsumo then
                seats |> List.filter (fun seat -> seat <> transfer.Actor)
            else
                seats |> List.filter (fun seat -> seat = transfer.Target)

        /// 一家付的和了点（不含本场）。
        let payment (seat: Seat) : int =
            if tsumo then
                ceil100 (basis * (if actorIsOya || seat = transfer.Oya then 2 else 1))
            else
                ceil100 (basis * (if actorIsOya then 6 else 4))

        let horaPoints = payers |> List.sumBy payment

        // 和了者自己包不了自己，座位不合法的也不算：那两种情形当作没包（错误不能把点数弄丢）。
        let sekinin =
            transfer.Sekinin
            |> Option.filter (fun liable -> liable <> transfer.Actor && Seat.isValid ruleset liable)

        /// 和了点的分担：没包时就是各付各的；有包时改挂到责任者头上（见 `HoraTransfer.Sekinin`）。
        /// 两家平分时先切上到 100 再把剩下的给放铳者，因此合计恒等于 `horaPoints`。
        let charges: (Seat * int) list =
            match sekinin with
            | Some liable when tsumo -> [ liable, horaPoints ]
            | Some liable when liable <> transfer.Target ->
                let half = ceil100 (horaPoints / 2)
                [ liable, half; transfer.Target, horaPoints - half ]
            | Some _
            | None -> payers |> List.map (fun seat -> seat, payment seat)

        /// 本场的付家：自摸由付和了点的那几家平摊（包了就只剩责任者一家）；
        /// 荣和没包时由**放铳者**一家付，**有包时改由责任者**一家付——
        /// 票 65：2025 整年语料里大三元包牌荣和 4 场（各 1 本场），4/4 天凤都把本场
        /// 记在包牌家头上（包牌家 -16300、放铳家 -16000）。
        let honbaPayers =
            if tsumo then
                charges |> List.map fst
            else
                match sekinin with
                | Some liable -> [ liable ]
                | None -> [ transfer.Target ]

        let honba =
            let total = max 0 ruleset.HonbaPoints * max 0 transfer.Honba

            if List.isEmpty honbaPayers then
                0
            else
                total / List.length honbaPayers

        let charged (seat: Seat) : int =
            (charges |> List.sumBy (fun (each, points) -> if each = seat then points else 0))
            + (honbaPayers |> List.sumBy (fun each -> if each = seat then honba else 0))

        let received =
            seats |> List.filter (fun seat -> seat <> transfer.Actor) |> List.sumBy charged

        let kyotaku = max 0 ruleset.RiichiBou * max 0 transfer.Kyotaku

        let deltas =
            seats
            |> List.map (fun seat ->
                if seat = transfer.Actor then
                    received + kyotaku
                else
                    -(charged seat))

        {
            Value = value
            Limit = limit ruleset value
            HoraPoints = horaPoints
            Deltas = deltas
        }

    // ---- 高点法 ----

    /// 这副和了牌姿按**点数最高**的那一种读法算出的符与番。
    ///
    /// 07 的 `Yaku.candidates` 已经把全部读法按「役满 → 番 → 近似符」排好，但它算不了
    /// 规则集相关的符（连风牌雀头），也不含门清荣和与自摸这类常数项——**真符要在这里再选一次**。
    /// 排序键是基本点而不是番：`3 番 70 符`（封顶满贯）比 `4 番 30 符` 高，高点法比的是点数。
    /// 同键时取 `candidates` 里靠前的那一种，因此结果是确定的。
    let best (ruleset: Ruleset) (context: YakuContext) (hand: AgariHand) : Result<HoraReading, YakuError> =
        let read (tally: YakuTally) : HoraReading =
            {
                Tally = tally
                Value = HoraValue.ofTally ruleset context hand tally
            }

        match Yaku.candidates ruleset context hand with
        // 一个读法都没有：不成和了型还是无役，由 `Yaku.detect` 分辨，这里不重复一份判据。
        | [] -> Yaku.detect ruleset context hand |> Result.map read
        | candidates ->
            candidates
            |> List.map read
            |> List.maxBy (fun reading -> basePoints ruleset reading.Value, reading.Value.Han, reading.Value.Fu)
            |> Ok

/// 级别与授受结果的渲染。
[<RequireQualifiedAccess>]
module Limit =

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。满贯以下没有专门的说法，为空串。
    let toDisplay (limit: Limit) : string =
        match limit with
        | Limit.Normal -> ""
        | Limit.Mangan -> "满贯"
        | Limit.Haneman -> "跳满"
        | Limit.Baiman -> "倍满"
        | Limit.Sanbaiman -> "三倍满"
        | Limit.Kazoe -> "数え役满"
        | Limit.Yakuman -> "役满"

/// 授受结果的渲染。
[<RequireQualifiedAccess>]
module HoraScore =

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式，如「40 符 3 番 5200 点」。
    let toDisplay (score: HoraScore) : string =
        let level = Limit.toDisplay score.Limit

        let body =
            if score.Value.Yakuman > 0 then
                if score.Value.Yakuman = 1 then
                    level
                else
                    $"{score.Value.Yakuman} 倍{level}"
            elif level = "" then
                $"{score.Value.Fu} 符 {score.Value.Han} 番"
            else
                $"{score.Value.Fu} 符 {score.Value.Han} 番 {level}"

        $"{body} {score.HoraPoints} 点"
