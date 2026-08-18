namespace Janpo.Web

open Browser.WebStorage
open Janpo

/// 浏览器本地存储：**API key 唯一的落脚点**。
///
/// spec 与 ADR-0003 的那条硬约束落在这里——key 只存在这台浏览器里，请求由它直发 provider，
/// 本平台没有后端可以收它，牌谱与任何可分享物里也不会出现它（`Paifu` 里根本没有这个字段）。
///
/// **一个字段一个键，不存 JSON**：字段少、都是字符串，JSON 只会多一层「读不动怎么办」。
/// 读不出来 / 读到垃圾一律退回默认值（`ModelProfile.edit` 本来就拒绝读不懂的值）。
/// 档案库是变长的，因此它的键带序号（`janpo.profiles.<i>.<字段>`），
/// 仍旧**不把一整份 JSON 塞进一个键**：那样读不动一格就丢整库。
[<RequireQualifiedAccess>]
module Store =

    /// **上一版**模型坐席那几格的键前缀（票 23）。它现在只在迁移路上读得到，
    /// 见 `readSeating`。
    let private seatPrefix = "janpo.llm."

    /// 配桌那三项规则开关的键前缀（票 72）。**另起一个前缀**：
    /// 它们不是模型坐席的配置，而是这一桌按什么规则打（ADR-0004）。
    let private rulesPrefix = "janpo.rules."

    let private read (prefix: string) (key: string) : string option =
        match localStorage.getItem (prefix + key) with
        | null -> None
        | value -> Some value

    let private write (prefix: string) (key: string) (value: string) : unit =
        localStorage.setItem (prefix + key, value)

    // ---- 配桌那三项规则（票 72） ----

    /// 配桌上拨到的那三项（对局长度 / 赤宝牌 / 食断）。
    /// 没存过、存了但读不懂的那一项用默认值（`RulesetDraft.initial`），
    /// **不把另外两项一并丢掉**——一个字段一个键，与模型坐席那几格同一个做法。
    let readRules () : RulesetDraft =
        let switch (key: string) (fallback: bool) : bool =
            read rulesPrefix key
            |> Option.bind RulesetDraft.switchOfWire
            |> Option.defaultValue fallback

        {
            Length =
                read rulesPrefix "length"
                |> Option.bind GameLength.ofWire
                |> Option.defaultValue RulesetDraft.initial.Length
            Akadora = switch "akadora" RulesetDraft.initial.Akadora
            Kuitan = switch "kuitan" RulesetDraft.initial.Kuitan
        }

    let writeRules (draft: RulesetDraft) : unit =
        write rulesPrefix "length" (GameLength.toWire draft.Length)
        write rulesPrefix "akadora" (RulesetDraft.switchToWire draft.Akadora)
        write rulesPrefix "kuitan" (RulesetDraft.switchToWire draft.Kuitan)

    // ---- 档案库与四席绑定（票 73） ----

    /// 档案库那几格的键前缀：`janpo.profiles.<i>.<字段>`，外加一个 `janpo.profiles.count`。
    let private profilesPrefix = "janpo.profiles."

    /// 四席绑定那几格的键前缀：`janpo.seats.<座位>.<字段>`。
    let private seatsPrefix = "janpo.seats."

    /// 档案库有几份。**它同时是「新格式写过没有」的判据**（见 `readSeating` 的迁移那一段）：
    /// 一份档案都没有时它是 `0`，仍旧存在——因此「全删光」不会被当成「还没迁过」。
    let private countKey = "count"

    let private remove (prefix: string) (key: string) : unit = localStorage.removeItem (prefix + key)

    /// 老 `janpo.llm.*` 那一份单席配置（票 23–46）。**只读不删**：迁移万一有 bug，
    /// 主人那把真 key 还在原地；判据不靠它在不在，靠新格式的 `count` 键（见下）。
    let private readLegacy (ruleset: Ruleset) : SeatingPlan option =
        SeatingPlan.ofLegacy ruleset (read seatPrefix)

    let private readProfile (index: int) : ModelProfile =
        (ModelProfile.initial, ProfileField.all)
        ||> List.fold (fun profile field ->
            match read profilesPrefix $"{index}.{ProfileField.key field}" with
            | Some value -> ModelProfile.edit field value profile
            | None -> profile)

    let private readBinding (seat: Seat) : SeatBinding =
        let index = Seat.index seat

        let bound =
            (SeatBinding.initial, SeatField.all)
            ||> List.fold (fun binding field ->
                match read seatsPrefix $"{index}.{SeatField.key field}" with
                | Some value -> SeatBinding.edit field value binding
                | None -> binding)

        match read seatsPrefix $"{index}.choice" |> Option.bind SeatChoice.ofWire with
        | Some choice -> { bound with Choice = choice }
        | None -> bound

    /// 档案库有几份（新格式写过没有）。
    let private profileCount () : int option =
        read profilesPrefix countKey
        |> Option.bind (fun value ->
            match System.Int32.TryParse value with
            | true, count -> Some count
            | false, _ -> None)

    /// 落盘。**整份写**（档案数会变），并把上一次多出来的那几份档案的键删干净——
    /// 否则删掉一份档案之后，那一份的 key 还躺在 localStorage 里。
    let writeSeating (seating: SeatingPlan) : unit =
        let previous = profileCount () |> Option.defaultValue 0
        let count = List.length seating.Profiles
        write profilesPrefix countKey (string count)

        seating.Profiles
        |> List.iteri (fun index profile ->
            for field in ProfileField.all do
                write profilesPrefix $"{index}.{ProfileField.key field}" (ModelProfile.field field profile))

        for index in count .. previous - 1 do
            for field in ProfileField.all do
                remove profilesPrefix $"{index}.{ProfileField.key field}"

        seating.Seats
        |> List.iteri (fun index binding ->
            write seatsPrefix $"{index}.choice" (SeatChoice.toWire binding.Choice)

            for field in SeatField.all do
                write seatsPrefix $"{index}.{SeatField.key field}" (SeatBinding.field field binding))

    /// 档案库 + 四席绑定（票 73）。三条路，按顺序试：
    ///
    /// 1. 新格式写过（`janpo.profiles.count` 在）→ 照它读；
    /// 2. 没写过、但老 `janpo.llm.*` 有东西 → **迁一次**（`SeatingPlan.ofLegacy`），
    ///    并**当场把新格式写回去**，于是下一次打开走第 1 条——**迁移只做一次**，
    ///    人后来改的东西不会被老键盖回来。老键**只读不删**：迁移万一有 bug，
    ///    主人那把真 key 还在原地（判据不靠它在不在，靠 `count` 键在不在）；
    /// 3. 两样都没有（这台浏览器头一次打开）→ 默认那一份（一份空档案 + 四家均匀随机）。
    ///
    /// 每一格读不懂就退回那一格的默认值，**不把别的格一并丢掉**（与票 72 那三项同一个做法）。
    ///
    /// **读的时候写盘只有这一处**，理由是迁移必须是一次性的：留给下一条消息去写的话，
    /// 「人一次都没动过面板」这条路上老键会永远赢。
    let readSeating (ruleset: Ruleset) : SeatingPlan =
        match profileCount () with
        | Some count ->
            {
                Profiles = [ for index in 0 .. count - 1 -> readProfile index ]
                Seats = Seat.all ruleset |> List.map readBinding
            }
            |> SeatingPlan.fit ruleset
        | None ->
            match readLegacy ruleset with
            | Some legacy ->
                let migrated = SeatingPlan.fit ruleset legacy
                writeSeating migrated
                migrated
            | None -> SeatingPlan.initial ruleset
