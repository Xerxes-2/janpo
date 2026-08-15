namespace Janpo

/// Player 提交给引擎的**意图**，可以是非法的（CONTEXT.md）。与 `Event`（既成事实）是两个
/// 类型，允许 case 同名——未验证的意图不应当能被误当成事实写进 Paifu。
/// case 名与字段贴 mjai 的动作消息，不自创。
///
/// **这个 DU 会被后续的票反复加 case**：
/// `Ankan` / `Kakan` / `Daiminkan`（11）、`Ryuukyoku`（九种九牌，12）。
/// 加一个 case 的代价固定为三处，漏掉哪处编译器都会
/// 指出来（`--warnaserror` 下不完整 match 是错误）：
///
/// 1. 这里加一个 case；
/// 2. `GameState` 里产出合法动作集的那个函数（各阶段各自一份）加一支；
/// 3. `GameState.step` 里执行动作的那支 match 加一支。
///
/// case 强制限定名（`Action.Dahai`）：`Event` 里有同名 case，不限定名的话
/// 后定义的那个会把先定义的遮住，读代码时分不出「意图」还是「事实」。
[<RequireQualifiedAccess>]
type Action =
    /// mjai `dahai`：打出一张。`tsumogiri` 为真表示打的就是刚摸进的那张（摸切）。
    /// 摸切与手切是两个不同的动作，即使打出去的牌一样——牌河的手切信息是公开信息。
    | Dahai of actor: Seat * pai: Tile * tsumogiri: bool
    /// mjai `hora`：宣言和了。自摸和时 `target` 等于 `actor`、`pai` 是刚摸进的那张；
    /// 荣和时 `target` 是打出这张的座位、`pai` 是刚打出的那张（mjai 的约定）。
    | Hora of actor: Seat * target: Seat * pai: Tile
    /// mjai `pon`：碰。`target` 是打出这张的座位、`pai` 是被碰的那张、`consumed` 是自家亮出的
    /// 两张同种牌（mjai 的约定）。红 5 与正牌是不同的牌，因此手里 `5m 5m 5mr` 碰 `5m`
    /// 是**两个不同的动作**（亮 `5m 5m` 与亮 `5m 5mr`），合法动作集里各占一条。
    | Pon of actor: Seat * target: Seat * pai: Tile * consumed: Tile list
    /// mjai `chi`：吃。字段与 `Pon` 同形，`consumed` 是自家亮出的、与 `pai` 凑成顺子的两张。
    /// **只有下家能吃**，因此 `target` 恒是 `actor` 的上家。
    | Chi of actor: Seat * target: Seat * pai: Tile * consumed: Tile list
    /// mjai `reach`：宣言立直。**宣言与宣言牌是两步**——提交这一条只是宣言，
    /// 紧接着的那一手仍要提交一条 `Dahai`（宣言牌），且只能打「打完仍听牌」的那几张。
    /// wire 上是 mjai 的 `reach`，标识符按术语表拼作 `Riichi`（裁决 D-1）。
    | Riichi of actor: Seat
    /// mjai `none`：响应阶段的「过」——他家打出的这张我不要。
    ///
    /// **响应阶段一定有它**：合法动作集里只要出现了 Ron（06）或 Pon / Chi / Kan（10 / 11），
    /// 就必须同时有一条「过」，否则响应阶段停在那里没人推得走。mjai 的 `none` 消息没有
    /// `actor` 字段（服务端知道在等谁），这里带上是因为引擎的每个动作都要能说出是谁提交的。
    | None of actor: Seat

/// 合法动作集（CONTEXT.md）：当前阶段某座位可提交的全部 Action。
/// 真人 UI 的按钮与 LLM 的工具 schema 都由它驱动，两边都不自己判断合法性。
///
/// 一个阶段可以同时等多个座位（他家打牌后的响应阶段：Ron / Pon / Chi 各在不同座位），
/// 因此引擎给出的是一列 `LegalActions`，每项对应一个座位。
type LegalActions =
    {
        /// 等这个座位提交动作。
        Seat: Seat
        /// 这个座位此刻能提交的全部动作，非空。
        Actions: Action list
    }

/// 动作的拆解。
[<RequireQualifiedAccess>]
module Action =

    // ---- 拆解 ----

    /// 提交这个动作的座位。
    let actor (action: Action) : Seat =
        match action with
        | Action.Dahai(actor, _, _) -> actor
        | Action.Hora(actor, _, _) -> actor
        | Action.Pon(actor, _, _, _) -> actor
        | Action.Chi(actor, _, _, _) -> actor
        | Action.Riichi actor -> actor
        | Action.None actor -> actor
