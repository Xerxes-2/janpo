namespace Janpo

/// 选手（CONTEXT.md）：给定局面与**自己的**合法动作集，挑一个动作。
///
/// 是纯函数：选手自己的状态（随机发生器、剩余重试次数……）经 `'player` 显式穿进穿出，
/// 引擎里不出现可变状态，因此「同一种子跑两次结果相同」在类型层面就成立。
/// LLM 适配器与真人坐席是 Agent 层的事（它们的决策要 await），引擎侧只有这个同步形态。
type Player<'player> = 'player -> GameState -> LegalActions -> Action * 'player

/// 一局跑不下去的原因。是值，不是异常。
type KyokuError =
    /// 开不了局：规则集或上层给的条件不自洽。
    | CannotStart of start: KyokuStartError
    /// 有选手提交了非法动作。引擎不替选手兜底——兜底是 Agent 层的策略（spec）。
    | Illegal of illegal: IllegalAction

/// 一局的驱动：反复「取合法动作集 → 让选手挑一个 → step」，直到这一局终。
/// 局与局之间的推进（连庄、进局、终局精算）是 05 的事。
[<RequireQualifiedAccess>]
module Kyoku =

    // ---- 驱动 ----

    /// 把一局跑到终。选手给出非法动作时立刻返回 `IllegalAction`。
    ///
    /// 每步只问合法动作集里的**第一**个座位。响应阶段同时等多家时这样也成立：
    /// 每收一家的答复就从待答列表里去掉一家，收齐之后引擎自己按优先级裁决
    /// （`GameState` 把已宣言的响应收在阶段里）——因此「先被问到」不等于「优先」。
    let run
        (player: Player<'player>)
        (initial: 'player)
        (state: GameState)
        : Result<GameState * 'player, IllegalAction> =
        let rec loop (current: 'player) (state: GameState) =
            match GameState.legalActions state with
            | [] -> Ok(state, current)
            | choice :: _ ->
                let action, advanced = player current state choice

                match GameState.step state action with
                | Error illegal -> Error illegal
                | Ok(next, _) -> loop advanced next

        loop initial state

    /// 把一局往前推**最多** `steps` 手；不到那么多手这一局就终了则提前停。
    /// `steps` 非正时原样返回。
    ///
    /// **要看某个中局的局面就得用它**（决策包的 CLI 子命令）：`run` 只给终局，
    /// 而中局那一手才是决策包有意思的地方。
    let runSteps
        (steps: int)
        (player: Player<'player>)
        (initial: 'player)
        (state: GameState)
        : Result<GameState * 'player, IllegalAction> =
        let rec loop (left: int) (current: 'player) (state: GameState) =
            if left <= 0 then
                Ok(state, current)
            else
                match GameState.legalActions state with
                | [] -> Ok(state, current)
                | choice :: _ ->
                    let action, advanced = player current state choice

                    match GameState.step state action with
                    | Error illegal -> Error illegal
                    | Ok(next, _) -> loop (left - 1) advanced next

        loop steps initial state

    /// 随机选手：从自己的合法动作集里等概率挑一个。M0 的四家都是它。
    let randomPlayer: Player<Rng> =
        fun rng _ choice ->
            let index, advanced = Rng.nextBelow (List.length choice.Actions) rng
            List.item index choice.Actions, advanced

    /// 开一局，再让随机选手把它打完。同一种子必然产出同一串事件与同一个终局。
    let runRandom (ruleset: Ruleset) (context: KyokuContext) (rng: Rng) : Result<GameState * Rng, KyokuError> =
        match GameState.start ruleset context rng with
        | Error error -> Error(CannotStart error)
        | Ok(state, advanced) -> run randomPlayer advanced state |> Result.mapError Illegal

/// 跑不下去的原因的渲染。
[<RequireQualifiedAccess>]
module KyokuError =

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: KyokuError) : string =
        match error with
        | CannotStart start -> KyokuStartError.toDisplay start
        | Illegal illegal -> IllegalAction.toDisplay illegal
