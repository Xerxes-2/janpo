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

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (length: GameLength) : string =
        match length with
        | Tonpuusen -> "东风战"
        | Hanchan -> "半庄战"
