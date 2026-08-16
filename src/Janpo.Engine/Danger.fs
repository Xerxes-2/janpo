namespace Janpo

open Thoth.Json.Core

/// 有威胁的一家（CONTEXT.md 的 Danger）：**立直了或者有副露**的他家。
///
/// 危险度是「对谁危险」的排序，因此得先说清对谁算。判据取得保守——立直与副露是
/// 观测里**看得见的**两种「这家在做牌」，不猜牌河的形状、不数他打得快不快
/// （那要统计模型，第一版明确不做）。
type Threat =
    {
        /// 哪一家。
        Seat: Seat
        /// 相对观测者第几家：1 下家、2 对家、3 上家（`MaskedSeat.Relative`）。
        Relative: int
        /// 立直中（宣言了，不管落没落定）。
        Riichi: bool
        /// 手上有副露（含暗杠）。
        Naki: bool
    }

/// 危险度档位（CONTEXT.md 的 Danger）：**顺序即安全度**，`Genbutsu` 最安全、
/// `NoEvidence` 最危险。三个有名字的档位就是术语表里的三条判据，
/// 第四档是「以上三条都不成立」，**它不是「危险」的度量**——它是没有依据。
///
/// **这是启发式，不是概率**：档位之间没有倍率，也不该被读成放铳率。
[<RequireQualifiedAccess>]
type DangerTier =
    /// 现物：这张牌对全部有威胁的家都是他自己打过的（CONTEXT.md 的 Genbutsu）。
    | Genbutsu
    /// 筋：两面听全被现物排除（CONTEXT.md 的 Suji）。
    | Suji
    /// 壁：两面听全被排除，其中至少一处靠「四张全见」（CONTEXT.md 的 Kabe）。
    | Kabe
    /// 以上三条都不成立：**没有安全依据**，不是「一定危险」。
    | NoEvidence

/// 危险度的依据，也就是进 prompt 的那几个理由标签。
///
/// **一条依据是一个事实**：某家打过这张牌、某家打过排掉两面听的那几张、
/// 某牌四张全见、这张是宝牌或在宝牌旁边。判据之外不编造。
[<RequireQualifiedAccess>]
type DangerReason =
    /// 对这一家是现物：它自己打过这张牌。
    | Genbutsu of threat: Threat
    /// 对这一家成筋：`blockers` 是它打过的、把两面听逐个排掉的那一两张。
    /// **中张要两侧都打过**（4s 要 1s 与 7s 都在河里），片筋不算筋。
    | Suji of threat: Threat * blockers: Tile list
    /// 壁：`blockers` 是四张全见、因而挡掉这张牌某个两面听的那几张。
    /// 壁对全桌同时成立（它数的是可见牌），因此这条依据不挂在某一家上。
    | Kabe of blockers: Tile list
    /// 对这一家没有任何依据。**只在别的家有依据时才写出来**——
    /// 三家都没依据时整条排序已经说明了这件事，再逐条重复只是噪音。
    | NoEvidence of threat: Threat
    /// 这张牌本身是宝牌。
    | Dora
    /// 这张牌在某张宝牌旁边（同花色、序数差两张之内）。
    | NearDora of dora: Tile

/// 一张牌的危险度：档位、理由与这一手里的名次。
///
/// **它是分析附件**：引擎的规则判定路径一个字节都不读它（`Danger.fs` 排在全部判定文件
/// 之后，那些文件引用不到它），拿掉它对局照跑。
type Danger =
    {
        /// 这一张，去红后的牌种。
        Pai: Tile
        /// 档位。
        Tier: DangerTier
        /// 理由标签，按「逐家、壁、宝牌」的顺序。
        Reasons: DangerReason list
        /// 这一手可打之牌里的名次，1 最安全。**并列名次**：同档同宝牌权重的牌共用一个名次，
        /// 下一档从「已排在前面的张数 + 1」起（两张并列第一之后是第三）。
        Rank: int
    }

/// 档位的排序与两个出口。
///
/// **没有 `all`**：`ScaffoldTier` 那个是配置面板要列选项才有的，
/// 档位不是配置项，没人需要把它们枚举一遍。
[<RequireQualifiedAccess>]
module DangerTier =

    /// 安全度序：0 最安全。排序键的第一项就是它。
    ///
    /// **筋排在壁前面**：两者都只排两面听，而筋的依据是那一家的振听（对它绝对成立），
    /// 壁的依据是可见张数（对全桌成立但只挡含那张的形）。术语表列判据的顺序也是这个。
    let order (tier: DangerTier) : int =
        match tier with
        | DangerTier.Genbutsu -> 0
        | DangerTier.Suji -> 1
        | DangerTier.Kabe -> 2
        | DangerTier.NoEvidence -> 3

    /// wire 上的名字。**Agent 层按它分组渲染**，不许跟着显示文案一起改。
    let toWire (tier: DangerTier) : string =
        match tier with
        | DangerTier.Genbutsu -> "genbutsu"
        | DangerTier.Suji -> "suji"
        | DangerTier.Kabe -> "kabe"
        | DangerTier.NoEvidence -> "no_evidence"

    /// **渲染层的单向出口**（ADR-0001）：给人与 LLM 看的那个词，措辞照 `CONTEXT.md`。
    let toDisplay (tier: DangerTier) : string =
        match tier with
        | DangerTier.Genbutsu -> "现物"
        | DangerTier.Suji -> "筋"
        | DangerTier.Kabe -> "壁"
        | DangerTier.NoEvidence -> "无依据"

/// 有威胁的一家的渲染与编码。
[<RequireQualifiedAccess>]
module Threat =

    /// 相对位置的中文（CONTEXT.md 的 Shimocha / Toimen / Kamicha）。
    /// 座位数不是 4 时对家不存在，那时按座位号说。
    let who (threat: Threat) : string =
        match threat.Relative with
        | 1 -> "下家"
        | 2 -> "对家"
        | 3 -> "上家"
        | _ -> $"座位 {Seat.index threat.Seat}"

    /// **渲染层的单向出口**：`下家已立直` / `对家有副露`。
    let toDisplay (threat: Threat) : string =
        let marks =
            [
                if threat.Naki then
                    "有副露"
                if threat.Riichi then
                    "已立直"
            ]

        who threat + String.concat "且" marks

    /// wire 形态。`display` 是引擎渲染好的中文（ADR-0005 第 2 条）。
    let encoder: Encoder<Threat> =
        fun threat ->
            Encode.object
                [
                    "seat", Seat.encoder threat.Seat
                    "relative", Encode.int threat.Relative
                    "riichi", Encode.bool threat.Riichi
                    "naki", Encode.bool threat.Naki
                    "display", Encode.string (toDisplay threat)
                ]

/// 理由标签的渲染与编码。
[<RequireQualifiedAccess>]
module DangerReason =

    /// wire 上的名字（哪一类依据）。
    let toWire (reason: DangerReason) : string =
        match reason with
        | DangerReason.Genbutsu _ -> "genbutsu"
        | DangerReason.Suji _ -> "suji"
        | DangerReason.Kabe _ -> "kabe"
        | DangerReason.NoEvidence _ -> "no_evidence"
        | DangerReason.Dora -> "dora"
        | DangerReason.NearDora _ -> "near_dora"

    /// **渲染层的单向出口**：直接进 prompt 的那个标签。牌一律 mjai 记法
    /// （拼起来不必查术语表，ADR-0001），措辞照 `CONTEXT.md` 的四条词条。
    let toDisplay (reason: DangerReason) : string =
        match reason with
        | DangerReason.Genbutsu threat -> Threat.who threat + "现物"
        | DangerReason.Suji(threat, blockers) -> $"{Threat.who threat} {Tile.toMjaiMany blockers} 筋"
        | DangerReason.Kabe blockers -> $"{Tile.toMjaiMany blockers} 四张全见"
        | DangerReason.NoEvidence threat -> Threat.who threat + "无依据"
        | DangerReason.Dora -> "宝牌"
        | DangerReason.NearDora dora -> $"宝牌 {Tile.toMjai dora} 周边"

    /// wire 形态：类别 + 引擎渲染好的中文。
    let encoder: Encoder<DangerReason> =
        fun reason ->
            Encode.object
                [
                    "value", Encode.string (toWire reason)
                    "display", Encode.string (toDisplay reason)
                ]

/// 危险度的判定、排序与编码（CONTEXT.md 的 Danger）。
///
/// **规则化启发式，不做统计模型**：判据只有现物、筋、壁与宝牌周边四条，
/// 每条都是观测里看得见的事实。输出是排序与理由标签，**不是概率**——
/// 「放铳率 12%」这类说法会被读成统计结论，这里一个字都不出。
///
/// **算的是遮蔽后的观测**：他家的暗牌在 `MaskedSeat` 里根本没有位置，
/// 因此「危险度多看了一张牌」在结构上不可能。
[<RequireQualifiedAccess>]
module Danger =

    // ---- 威胁判据 ----

    /// 有威胁的家 + 它打过的那些牌（去红后的牌种，也就是它的现物）。
    let private threatened (observation: Observation) : (Threat * Tile list) list =
        observation.Others
        |> List.choose (fun other ->
            let riichi = RiichiState.isActive other.Riichi
            let naki = other.Naki |> List.isEmpty |> not

            if riichi || naki then
                let threat =
                    {
                        Seat = other.Seat
                        Relative = other.Relative
                        Riichi = riichi
                        Naki = naki
                    }

                Some(threat, other.Kawa |> List.map (fun entry -> Tile.deaka entry.Pai))
            else
                None)

    /// 这一手有威胁的家。**一家都没有时是空表**，那时整份危险度不给
    /// （对着没人在做牌的桌子排安全度，排出来的名次没有被评价的对象）。
    let threats (observation: Observation) : Threat list = threatened observation |> List.map fst

    // ---- 逐张判定 ----

    /// 每个牌种可见几张：自家手牌 + 观测里手牌之外的可见牌。壁只认「四张全见」。
    ///
    /// 34 长度的计数数组按牌种索引原地累加：与 `Ukeire` / `HandShape` 同一套写法
    /// （`docs/agents/fsharp-style.md` 规则 5），外面看不见它。
    let private visibleCounts (observation: Observation) : int array =
        let counts = Array.zeroCreate Tile.KindCount

        for tile in observation.Self.Hand @ Observation.visible observation do
            let index = Tile.kindIndex tile
            counts.[index] <- counts.[index] + 1

        counts

    /// 这张牌的两面听：`(排掉它的那张, 组成两面的那两张)`。
    ///
    /// 一张牌只被两种两面听等着：`n+1 n+2`（另一头是 `n+3`）与 `n-2 n-1`（另一头是 `n-3`）。
    /// 另一头出了花色就不是两面（`8s 9s` 等 `7s` 是边张，不在此列），**字牌一种也没有**。
    let private ryanmen (pai: Tile) : (Tile * Tile list) list =
        let suit = Tile.suit pai
        let number = Tile.number pai

        let at (value: int) : Tile option = Tile.tryCreate suit value false

        let shape (other: int) (low: int) (high: int) : (Tile * Tile list) option =
            match at other, at low, at high with
            | Some other, Some low, Some high -> Some(other, [ low; high ])
            | _ -> None

        if suit = Jihai then
            []
        else
            [
                if number <= 6 then
                    yield shape (number + 3) (number + 1) (number + 2)
                if number >= 4 then
                    yield shape (number - 3) (number - 2) (number - 1)
            ]
            |> List.choose id

    /// 宝牌：表宝牌指示牌的下一张（`Dora.ofMarker`）。里宝牌谁也看不见，不在这里。
    let private doraKinds (observation: Observation) : Tile list =
        observation.DoraMarkers |> List.map Dora.ofMarker |> List.distinct

    /// 同花色、序数差两张之内——搭子搭得上宝牌的那个范围。字牌没有周边。
    let private nearDora (dora: Tile) (pai: Tile) : bool =
        Tile.suit dora <> Jihai
        && Tile.suit dora = Tile.suit pai
        && dora <> pai
        && abs (Tile.number dora - Tile.number pai) <= 2

    /// 宝牌权重：**同档之内**的破平项。宝牌本身 2、宝牌周边 1、无关 0。
    /// 现物不受它影响（现物对那一家绝对安全，CONTEXT.md），因此排序键里它只在非现物档生效。
    let private doraWeight (dora: Tile list) (pai: Tile) : int =
        if List.contains pai dora then
            2
        elif dora |> List.exists (fun kind -> nearDora kind pai) then
            1
        else
            0

    /// 宝牌那几条理由：是宝牌就一条，否则挨着几张宝牌就几条。
    let private doraReasons (dora: Tile list) (pai: Tile) : DangerReason list =
        if List.contains pai dora then
            [ DangerReason.Dora ]
        else
            dora
            |> List.filter (fun kind -> nearDora kind pai)
            |> List.map DangerReason.NearDora

    /// 一张牌对某一家的判定：现物 → 筋 → 壁 → 无依据。
    /// 第二个返回值是这一家给得出的理由（壁是全桌的，不挂在某一家上，因此是 None）。
    let private against
        (kabe: Tile -> bool)
        (threat: Threat, genbutsu: Tile list)
        (pai: Tile)
        : DangerTier * DangerReason option =
        let shapes = ryanmen pai

        if List.contains pai genbutsu then
            DangerTier.Genbutsu, Some(DangerReason.Genbutsu threat)
        elif List.isEmpty shapes then
            // 字牌：没有两面听可排，筋与壁都无从谈起。
            DangerTier.NoEvidence, None
        else
            let bySuji, rest =
                shapes |> List.partition (fun (other, _) -> List.contains other genbutsu)

            let unblocked =
                rest |> List.filter (fun (_, tiles) -> tiles |> List.exists kabe |> not)

            if not (List.isEmpty unblocked) then
                DangerTier.NoEvidence, None
            elif List.isEmpty rest then
                let blockers = bySuji |> List.map fst |> Tile.sort
                DangerTier.Suji, Some(DangerReason.Suji(threat, blockers))
            else
                DangerTier.Kabe, None

    /// 一张牌的档位与理由。**档位取最危险的那一家**：对下家是现物、对已立直的对家没依据，
    /// 那它就不是一张安全牌。
    let private assess
        (kabe: Tile -> bool)
        (dora: Tile list)
        (seats: (Threat * Tile list) list)
        (pai: Tile)
        : DangerTier * DangerReason list =
        let perSeat =
            seats
            |> List.map (fun seat ->
                let tier, reason = against kabe seat pai
                fst seat, tier, reason)

        let kabeBlockers =
            ryanmen pai
            |> List.collect snd
            |> List.filter kabe
            |> List.distinct
            |> Tile.sort

        let tier =
            perSeat |> List.map (fun (_, tier, _) -> tier) |> List.maxBy DangerTier.order

        // 「无依据」只在别的家有依据时才写出来：全无依据时档位已经说完了，逐条重复是噪音。
        let anyEvidence =
            perSeat |> List.exists (fun (_, tier, _) -> tier <> DangerTier.NoEvidence)
            || not (List.isEmpty kabeBlockers)

        let seatReasons =
            perSeat
            |> List.choose (fun (threat, tier, reason) ->
                match reason with
                | Some reason -> Some reason
                | None when anyEvidence && tier = DangerTier.NoEvidence -> Some(DangerReason.NoEvidence threat)
                | None -> None)

        let kabeReasons =
            if List.isEmpty kabeBlockers then
                []
            else
                [ DangerReason.Kabe kabeBlockers ]

        tier, seatReasons @ kabeReasons @ doraReasons dora pai

    /// 这一手每张可打之牌的危险度，**从安全到危险**。没有有威胁的家时是空表。
    ///
    /// `candidates` 是那几张打得出去的牌（去红后的牌种，重复的只算一条）。
    let rank (observation: Observation) (candidates: Tile list) : Danger list =
        let seats = threatened observation

        if List.isEmpty seats then
            []
        else
            let counts = visibleCounts observation
            let kabe (tile: Tile) : bool = counts.[Tile.kindIndex tile] >= 4
            let dora = doraKinds observation

            let assessed =
                candidates
                |> List.map Tile.deaka
                |> List.distinct
                |> List.map (fun pai ->
                    let tier, reasons = assess kabe dora seats pai
                    pai, tier, reasons)

            // 排序键：档位在前，宝牌只在同档内破平。`List.sortBy` 是稳定的，
            // 因此同键的那几张保持传进来的顺序（也就是动作列表的顺序）。
            let key (pai, tier, _) =
                DangerTier.order tier,
                (if tier = DangerTier.Genbutsu then
                     0
                 else
                     doraWeight dora pai)

            let sorted = assessed |> List.sortBy key

            sorted
            |> List.map (fun item ->
                let pai, tier, reasons = item

                {
                    Pai = pai
                    Tier = tier
                    Reasons = reasons
                    // 并列名次：排在它前面的张数 + 1。张数最多 14，逐张数一遍够用。
                    Rank = 1 + (sorted |> List.sumBy (fun other -> if key other < key item then 1 else 0))
                })

    // ---- 出口 ----

    /// **渲染层的单向出口**：`4s 现物（下家现物）`。牌桌与报告里的一行就是它。
    let toDisplay (danger: Danger) : string =
        let reasons = danger.Reasons |> List.map DangerReason.toDisplay |> String.concat "、"
        let tier = DangerTier.toDisplay danger.Tier

        if reasons = "" then
            $"{Tile.toMjai danger.Pai} {tier}"
        else
            $"{Tile.toMjai danger.Pai} {tier}（{reasons}）"

    /// wire 形态。**只有 encoder**：跟决策包一样只往外走。
    /// 档位与理由都带 `display`（ADR-0005 第 2 条：渲染层要中文时由包携带）。
    let encoder: Encoder<Danger> =
        fun danger ->
            Encode.object
                [
                    "tier",
                    Encode.object
                        [
                            "value", Encode.string (DangerTier.toWire danger.Tier)
                            "display", Encode.string (DangerTier.toDisplay danger.Tier)
                        ]
                    "rank", Encode.int danger.Rank
                    "reasons", danger.Reasons |> List.map DangerReason.encoder |> Encode.list
                ]
