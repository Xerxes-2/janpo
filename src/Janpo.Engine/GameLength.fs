namespace Janpo

/// 对局长度（CONTEXT.md 的 Tonpuusen / Hanchan）：一场对局打到哪个场风为止。
///
/// 局数序列由它与**座位数**一起推出（`Ruleset.kyokus`），因此四麻半庄是 8 局、
/// 三麻半庄是 6 局——两个数都不写死。西入与延长本版不做（05 票的 Out of Scope）。
type GameLength =
    /// 东风战：只打东场。
    | Tonpuusen
    /// 半庄战：东场加南场。
    | Hanchan

/// 对局长度的枚举、拆解与渲染。
[<RequireQualifiedAccess>]
module GameLength =

    // ---- 构造 ----

    /// 两种长度。CLI 的选项与 UI 的下拉都从这里取。
    let all: GameLength list = [ Tonpuusen; Hanchan ]

    // ---- 拆解 ----

    /// 要打的场风，按顺序。局数序列就是「每个场风各打 `SeatCount` 局」（`Ruleset.kyokus`）。
    let bakazes (length: GameLength) : Kaze list =
        match length with
        | Tonpuusen -> [ Ton ]
        | Hanchan -> [ Ton; Nan ]

    // ---- wire 记法 ----

    /// wire 上的名字（牌谱里的规则集写的就是它）。**不是渲染出口**：
    /// 它进 JSON、进牌谱，中文形态在 `toDisplay`（ADR-0001）。
    let toWire (length: GameLength) : string =
        match length with
        | Tonpuusen -> "tonpuusen"
        | Hanchan -> "hanchan"

    /// wire 名回到长度；不认识的是 None（牌谱是外面来的，什么都可能）。
    let ofWire (wire: string) : GameLength option =
        all |> List.tryFind (fun length -> toWire length = wire)

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (length: GameLength) : string =
        match length with
        | Tonpuusen -> "东风战"
        | Hanchan -> "半庄战"
