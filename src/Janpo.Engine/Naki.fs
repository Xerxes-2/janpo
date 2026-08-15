namespace Janpo

/// 副露的种类（CONTEXT.md 的 Naki）：碰、吃、暗杠、大明杠、加杠。
///
/// `[<RequireQualifiedAccess>]`：`Event` 后面会加同名的 mjai 事件 case（`Pon` / `Chi` / …），
/// 两者是不同的东西（事件是既成事实，副露是牌桌上的一组牌），名字不该在同一个命名空间里打架。
[<RequireQualifiedAccess>]
type NakiKind =
    | Pon
    | Chi
    | Ankan
    | Minkan
    | Kakan

/// 构造副露失败的原因。是值，不是异常。
type NakiError =
    /// 自家出的牌张数不对：碰与吃要 2 张、大明杠要 3 张、暗杠要 4 张、加杠的底要 3 张。
    | NakiConsumedCountMismatch of kind: NakiKind * count: int
    /// 碰 / 杠的牌种不一致。
    | NakiTilesNotSameKind of tiles: Tile list
    /// 吃的三张不成顺子（同花色、三张连号、无重复）。
    | NakiTilesNotRun of tiles: Tile list
    /// 加杠的底不是碰，或加的那张与碰的牌种不同。
    | KakanBaseMismatch of tiles: Tile list

/// 副露：鸣牌形成的公开组合，或暗杠。
///
/// 字段贴 mjai wire：`Taken` 是 `pai`（被鸣的那张，暗杠没有）、`Consumed` 是 `consumed`
/// （自家亮出的牌）、`Target` 是 `target`（被鸣的座位；暗杠没有，加杠记原本碰的来源）。
/// 红宝牌**保留**——副露里的红 5 照样计番。
///
/// 这个类型是 07（役）、10（碰吃）、11（杠）共用的：07 只读它的面子形态与牌，
/// 10 / 11 还要读 `Taken` / `Target` 去产事件与算责任支付。
type Naki =
    private
        {
            Kind: NakiKind
            /// 被鸣的那张（暗杠为 None）。加杠时是加上去的那张。
            Taken: Tile option
            /// 自家亮出的牌。
            Consumed: Tile list
            /// 被鸣的座位（暗杠为 None）。
            Target: Seat option
        }

/// 副露种类的渲染。
[<RequireQualifiedAccess>]
module NakiKind =

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (kind: NakiKind) : string =
        match kind with
        | NakiKind.Pon -> "碰"
        | NakiKind.Chi -> "吃"
        | NakiKind.Ankan -> "暗杠"
        | NakiKind.Minkan -> "大明杠"
        | NakiKind.Kakan -> "加杠"

/// 副露的构造与拆解。
[<RequireQualifiedAccess>]
module Naki =

    // ---- 构造 ----

    let private sameKind (tiles: Tile list) : bool =
        match tiles |> List.map Tile.kindIndex with
        | [] -> true
        | head :: tail -> tail |> List.forall (fun index -> index = head)

    /// 碰：从 `target` 那里拿一张，配自家两张同种牌。
    let pon (target: Seat) (taken: Tile) (consumed: Tile list) : Result<Naki, NakiError> =
        if List.length consumed <> 2 then
            Error(NakiConsumedCountMismatch(NakiKind.Pon, List.length consumed))
        elif not (sameKind (taken :: consumed)) then
            Error(NakiTilesNotSameKind(taken :: consumed))
        else
            Ok
                {
                    Kind = NakiKind.Pon
                    Taken = Some taken
                    Consumed = consumed
                    Target = Some target
                }

    /// 吃：从上家拿一张，配自家两张凑成顺子。
    let chi (target: Seat) (taken: Tile) (consumed: Tile list) : Result<Naki, NakiError> =
        let all = taken :: consumed

        if List.length consumed <> 2 then
            Error(NakiConsumedCountMismatch(NakiKind.Chi, List.length consumed))
        else
            let indexes = all |> List.map Tile.kindIndex |> List.sort
            let lowest = List.head indexes

            // 三张连号且都落在同一个数牌花色里：`lowest < 27` 排掉字牌，`lowest % 9 <= 6` 保证不跨花色。
            let isRun =
                lowest < 27 && lowest % 9 <= 6 && indexes = [ lowest; lowest + 1; lowest + 2 ]

            if isRun then
                Ok
                    {
                        Kind = NakiKind.Chi
                        Taken = Some taken
                        Consumed = consumed
                        Target = Some target
                    }
            else
                Error(NakiTilesNotRun all)

    /// 暗杠：自家四张同种牌。它**不破门清**。
    let ankan (consumed: Tile list) : Result<Naki, NakiError> =
        if List.length consumed <> 4 then
            Error(NakiConsumedCountMismatch(NakiKind.Ankan, List.length consumed))
        elif not (sameKind consumed) then
            Error(NakiTilesNotSameKind consumed)
        else
            Ok
                {
                    Kind = NakiKind.Ankan
                    Taken = None
                    Consumed = consumed
                    Target = None
                }

    /// 大明杠：从 `target` 那里拿一张，配自家三张同种牌。
    let minkan (target: Seat) (taken: Tile) (consumed: Tile list) : Result<Naki, NakiError> =
        if List.length consumed <> 3 then
            Error(NakiConsumedCountMismatch(NakiKind.Minkan, List.length consumed))
        elif not (sameKind (taken :: consumed)) then
            Error(NakiTilesNotSameKind(taken :: consumed))
        else
            Ok
                {
                    Kind = NakiKind.Minkan
                    Taken = Some taken
                    Consumed = consumed
                    Target = Some target
                }

    /// 加杠：往自己碰出来的那组上再加一张。底必须是碰，且牌种相同。
    let kakan (added: Tile) (pon: Naki) : Result<Naki, NakiError> =
        let baseTiles =
            match pon.Taken with
            | Some taken -> taken :: pon.Consumed
            | None -> pon.Consumed

        if pon.Kind <> NakiKind.Pon || not (sameKind (added :: baseTiles)) then
            Error(KakanBaseMismatch(added :: baseTiles))
        else
            Ok
                {
                    Kind = NakiKind.Kakan
                    Taken = Some added
                    Consumed = baseTiles
                    Target = pon.Target
                }

    // ---- 拆解 ----

    /// 副露的种类。
    let kind (naki: Naki) : NakiKind = naki.Kind

    /// 被鸣的那张（暗杠为 None；加杠是加上去的那张）。mjai 的 `pai`。
    let taken (naki: Naki) : Tile option = naki.Taken

    /// 自家亮出的牌。mjai 的 `consumed`。
    let consumed (naki: Naki) : Tile list = naki.Consumed

    /// 被鸣的座位（暗杠为 None）。mjai 的 `target`。
    let target (naki: Naki) : Seat option = naki.Target

    /// 组合里的全部牌，保留红宝牌，按 mjai 顺序升序。碰吃 3 张、杠 4 张。
    let tiles (naki: Naki) : Tile list =
        match naki.Taken with
        | Some taken -> Tile.sort (taken :: naki.Consumed)
        | None -> Tile.sort naki.Consumed

    /// 是不是杠（三种都算）。
    let isKan (naki: Naki) : bool =
        match naki.Kind with
        | NakiKind.Ankan
        | NakiKind.Minkan
        | NakiKind.Kakan -> true
        | NakiKind.Pon
        | NakiKind.Chi -> false

    /// 是不是暗的。**只有暗杠是暗的**——门清与否看的就是它。
    let isConcealed (naki: Naki) : bool = naki.Kind = NakiKind.Ankan

    /// 这组副露对应的面子。吃是顺子、碰是刻子、三种杠是杠子；只有暗杠算暗。
    let mentsu (naki: Naki) : Mentsu =
        let lowest = tiles naki |> List.map Tile.deaka |> List.min
        let concealed = isConcealed naki

        match naki.Kind with
        | NakiKind.Chi ->
            match Mentsu.shuntsu concealed lowest with
            | Some mentsu -> mentsu
            // 构造时已经验过是顺子，这条走不到；不抛异常（错误是值）。
            | None -> Mentsu.koutsu concealed lowest
        | NakiKind.Pon -> Mentsu.koutsu concealed lowest
        | NakiKind.Ankan
        | NakiKind.Minkan
        | NakiKind.Kakan -> Mentsu.kantsu concealed lowest

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (naki: Naki) : string =
        let body = tiles naki |> List.map Tile.toDisplay |> String.concat ""
        $"{NakiKind.toDisplay naki.Kind}{body}"

/// 副露构造错误的渲染。
[<RequireQualifiedAccess>]
module NakiError =

    let private tilesDisplay (tiles: Tile list) : string =
        tiles |> List.map Tile.toDisplay |> String.concat " "

    /// **渲染层的单向出口**：错误原因的中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: NakiError) : string =
        match error with
        | NakiConsumedCountMismatch(kind, count) -> $"{NakiKind.toDisplay kind}亮出的牌张数不对，得到 {count} 张"
        | NakiTilesNotSameKind tiles -> $"这几张不是同一种牌：{tilesDisplay tiles}"
        | NakiTilesNotRun tiles -> $"这几张不成顺子：{tilesDisplay tiles}"
        | KakanBaseMismatch tiles -> $"加杠的底必须是同一种牌的碰：{tilesDisplay tiles}"
