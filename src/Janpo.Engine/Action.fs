namespace Janpo

open Thoth.Json.Core

/// Player 提交给引擎的**意图**，可以是非法的（CONTEXT.md）。与 `Event`（既成事实）是两个
/// 类型，允许 case 同名——未验证的意图不应当能被误当成事实写进 Paifu。
/// case 名与字段贴 mjai 的动作消息，不自创。
///
/// **这个 DU 还会被后续的票加 case**（三麻的拔北就是一个）。
/// 加一个 case 的代价固定为五处，漏掉哪处编译器都会
/// 指出来（`--warnaserror` 下不完整 match 是错误）：
///
/// 1. 这里加一个 case；
/// 2. `GameState` 里产出合法动作集的那个函数（各阶段各自一份）加一支；
/// 3. `GameState.step` 里执行动作的那支 match 加一支；
/// 4. `Action.encoder`（决策包给 Agent 层看的 mjai 结构化字段）；
/// 5. `Action.toDisplay`（决策包的中文 label）。
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
    /// mjai `ankan`：暗杠。`consumed` 是自家的四张同种牌（含红 5）。**只在自己摸完牌那一手**
    /// 宣言得了（真实牌谱里 18/18 条 `ankan` 都紧跟在自己的 `tsumo` 之后）。
    /// 立直后能不能暗杠由 `RiichiState.allowsAnkan` 判（裁决 D-8）。
    | Ankan of actor: Seat * consumed: Tile list
    /// mjai `kakan`：加杠。`pai` 是从手里加上去的那张、`consumed` 是原本那组碰的三张。
    /// **没有 `target`**（mjai 的 `kakan` 就不带它）——原碰的来源座位记在 `Naki` 里，责任支付要它。
    /// 同样只在自己摸完牌那一手宣言得了；立直中加不了杠。
    | Kakan of actor: Seat * pai: Tile * consumed: Tile list
    /// mjai `daiminkan`：大明杠。字段与 `Pon` 同形，`consumed` 是自家亮出的三张同种牌。
    /// 它是**响应阶段**的动作，与碰同级（裁决顺序 Ron > Pon / Minkan > Chi）。
    ///
    /// 标识符按术语表拼作 `Minkan`（CONTEXT.md 的 Naki 条目），wire 上仍是 mjai 的
    /// `daiminkan`——与 `Riichi` / `reach` 同一处理（裁决 D-1）。
    | Minkan of actor: Seat * target: Seat * pai: Tile * consumed: Tile list
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
    /// mjai `ryukyoku`：宣言**九种九牌**。六种流局里只有它是选手宣言的（其余五种由引擎自己判），
    /// 也只有它可以不宣言——接着打是合法的。
    ///
    /// **只在第一巡、此前无人鸣牌的那一次自摸之后宣言得了**，且手里的幺九牌够
    /// `Ruleset.KyuushuKinds` 种（判据在 `Ryuukyoku.canDeclareKyuushuKyuuhai`）。
    /// mjai 的这条动作消息只带 `actor`，形态写在随后那条 `ryukyoku` 事件的 `reason` 里。
    /// 标识符按术语表拼作 `Ryuukyoku`，wire 仍是 mjai 的 `ryukyoku`（裁决 D-1）。
    | Ryuukyoku of actor: Seat

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
        | Action.Ankan(actor, _) -> actor
        | Action.Kakan(actor, _, _) -> actor
        | Action.Minkan(actor, _, _, _) -> actor
        | Action.Riichi actor -> actor
        | Action.None actor -> actor
        | Action.Ryuukyoku actor -> actor

    // ---- JSON（mjai wire） ----

    /// 一条动作的 mjai 动作消息形态：带 `type` 的对象，字段名与 `Event.encoder` 同名同形
    /// （mjai 把意图与事实归为同一种 message）。决策包的动作列表用的就是它。
    ///
    /// **只有 encoder，没有 decoder：这是单向出口。** 跨 F#/TS 边界回来的只有一个动作 id
    /// （`DecisionPackage.tryAction`）——外面构造不出 `Action`，非法动作因此在结构上不可能。
    /// 事件才是牌谱里的东西（ADR-0002），意图不上牌谱。
    let encoder: Encoder<Action> =
        let tiles (values: Tile list) =
            values |> List.map Tile.encoder |> Encode.list

        let message (actionType: string) (fields: (string * IEncodable) list) =
            Encode.object (("type", Encode.string actionType) :: fields)

        let naki (actionType: string) (actor: Seat) (target: Seat) (pai: Tile) (consumed: Tile list) =
            message
                actionType
                [
                    "actor", Seat.encoder actor
                    "target", Seat.encoder target
                    "pai", Tile.encoder pai
                    "consumed", tiles consumed
                ]

        fun action ->
            match action with
            | Action.Dahai(actor, pai, tsumogiri) ->
                message
                    "dahai"
                    [
                        "actor", Seat.encoder actor
                        "pai", Tile.encoder pai
                        "tsumogiri", Encode.bool tsumogiri
                    ]
            | Action.Hora(actor, target, pai) ->
                message
                    "hora"
                    [
                        "actor", Seat.encoder actor
                        "target", Seat.encoder target
                        "pai", Tile.encoder pai
                    ]
            | Action.Pon(actor, target, pai, consumed) -> naki "pon" actor target pai consumed
            | Action.Chi(actor, target, pai, consumed) -> naki "chi" actor target pai consumed
            // 标识符按术语表拼作 Minkan，wire 仍是 mjai 的 daiminkan（裁决 D-1）。
            | Action.Minkan(actor, target, pai, consumed) -> naki "daiminkan" actor target pai consumed
            | Action.Ankan(actor, consumed) ->
                message "ankan" [ "actor", Seat.encoder actor; "consumed", tiles consumed ]
            | Action.Kakan(actor, pai, consumed) ->
                message
                    "kakan"
                    [
                        "actor", Seat.encoder actor
                        "pai", Tile.encoder pai
                        "consumed", tiles consumed
                    ]
            // 标识符按术语表拼作 Riichi / Ryuukyoku，wire 仍是 mjai 的 reach / ryukyoku。
            // 九种九牌的形态写在随后那条 `ryukyoku` 事件的 `reason` 里，动作消息不带。
            | Action.Riichi actor -> message "reach" [ "actor", Seat.encoder actor ]
            | Action.None actor -> message "none" [ "actor", Seat.encoder actor ]
            | Action.Ryuukyoku actor -> message "ryukyoku" [ "actor", Seat.encoder actor ]

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式，也就是决策包里的 label。
    /// 引擎判定、事件流、牌谱与测试固件都不得消费它的输出。
    ///
    /// **同一包里两条动作的 label 不得相同**：亮哪几张是选手的决策（手里 `5m 5m 5mr`
    /// 碰 `5m` 有两种亮法，宝牌数不同），因此亮法会变的那几条把 `consumed` 一并写出来。
    let toDisplay (action: Action) : string =
        let tiles (values: Tile list) =
            values |> List.map Tile.toDisplay |> String.concat " "

        match action with
        | Action.Dahai(_, pai, true) -> $"摸切{Tile.toDisplay pai}"
        | Action.Dahai(_, pai, false) -> $"手切{Tile.toDisplay pai}"
        | Action.Hora(actor, target, pai) when actor = target -> $"自摸{Tile.toDisplay pai}"
        | Action.Hora(_, _, pai) -> $"荣和{Tile.toDisplay pai}"
        | Action.Pon(_, _, pai, consumed) -> $"碰{Tile.toDisplay pai}（亮{tiles consumed}）"
        | Action.Chi(_, _, pai, consumed) -> $"吃{Tile.toDisplay pai}（亮{tiles consumed}）"
        | Action.Ankan(_, consumed) -> $"暗杠（亮{tiles consumed}）"
        // 加杠只有一种亮法（底下那组碰已经在牌桌上），写加上去的那张就够了。
        | Action.Kakan(_, pai, _) -> $"加杠{Tile.toDisplay pai}"
        | Action.Minkan(_, _, pai, consumed) -> $"大明杠{Tile.toDisplay pai}（亮{tiles consumed}）"
        | Action.Riichi _ -> "立直宣言"
        | Action.None _ -> "过"
        | Action.Ryuukyoku _ -> "九种九牌"
