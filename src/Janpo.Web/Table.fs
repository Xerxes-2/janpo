namespace Janpo.Web

open Janpo

/// 落定的一手（CONTEXT.md 的 Turn）：动作本身，以及**它是不是兜底代打的**（票 23）。
///
/// 兜底不许静默替换：牌桌上要看得出某一手不是那家自己决的，因此「来路」跟着动作一起存。
/// 引擎那边分不出这件事（代打的动作同样取自合法动作集），所以它在牌桌这一层。
type Turn = {
    /// 落进引擎的那个动作。
    Action: Action
    /// 兜底代打时是那条中文原因；选手自己决出来的时候是 None。
    Fallback: string option
}

/// 牌桌的推进（票 22）。**局面只有一份，就是引擎的那份**：手牌、河、副露、点数、供托
/// 全部由投影从 `State` 读出来（ADR-0002：状态是 fold 出来的，不是存下来的），
/// 这里一个牌局字段都不复制。
///
/// 除了引擎的局面，它只多带三样东西：选手自己的状态、这一场的进程，
/// 以及和了那一手引擎给出的读法。
type Table = {
    /// 这一场（连庄、进局、本场与供托的结转、终局精算全在它身上）。
    /// **一局打完的那一步就把这一局收进去**，因此它随时能回答「还有没有下一局」。
    Game: Game
    /// 当前这一局的局面。
    State: GameState
    /// **随机选手**共用的选手状态（`Player<Rng>` 里的 `'player`），就是一个随机发生器。
    ///
    /// **它不是「四家的选手」**：谁坐哪个座位写在 `Roster` 里，与牌桌分开（票 23）。
    /// 异步座位的等待也不在这里：引擎侧的同步 `Player<'player>` 装不下它，
    /// 拆法见 `Demand`。
    Players: Rng
    /// 和了那一手引擎算出的读法，按宣言顺序（双响会有两条）。
    ///
    /// **它不是第二份局面，是捞下来的引擎输出**：`Event.Hora` 上没有役种字段
    /// （mjai wire 就没有），而 `GameState.horaOf` 只在宣言的那一刻答得出来——
    /// 一局终了之后阶段已是 `Ended`，再问就是 `NoAgariShape`。结算要显示的役种只有这一个来源。
    Readings: (Seat * HoraReading) list
    /// 刚落定的那一手。给牌桌显示「上一手是谁做了什么」用（`Action.toDisplay`），
    /// 并且看得出它是不是兜底代打的（`Turn.Fallback`）。
    Latest: Turn option
    /// 这一桌至今兜底代打了几手。**跨局累计**：断电演习（key 故意配坏）时
    /// 它就是那一局到底走了几手兜底的证据。
    Fallbacks: int
    /// 引擎拒绝了某个动作。**不该发生**（提交的动作都取自合法动作集），
    /// 落在这里就停住不再推进，把话说给人看，而不是静静地卡住。
    Fault: string option
}

/// 问该出手那家要动作的两种去向（票 23）。
///
/// **异步座位就从这里分岔**：引擎的 `Player<'player>` 是同步的（给局面返动作），
/// 装不下「这一手要等一趟跨网请求」；牌桌这一层把它拆成两个 case，
/// 让等待变成 Elmish 的一条 Msg 而不是引擎里的一个回调。
[<RequireQualifiedAccess>]
type Demand =
    /// 当场就有动作（随机选手），附推进后的选手状态。
    | Ready of action: Action * players: Rng
    /// 要问外面（LLM 座位）：这是那一手的决策包与座位配置。
    /// 拿回来的 id 用 `DecisionPackage.tryAction` 换成动作，换不出来就 `Fallback.action`。
    | Asked of package: DecisionPackage * config: LlmSeat

/// 牌桌的构造与推进。**没有驱动循环**：一次调用推进一手，循环是 Elmish 的 update
/// （ADR-0005 选 B 的理由之一——MVU 的 update 与引擎的 `step` 同构）。
[<RequireQualifiedAccess>]
module Table =

    // ---- 构造 ----

    /// 开一场对局的第一局。同一种子必然跑出同一场：牌山与选手共用同一条随机流，
    /// 与 `Game.runRandom` / CLI 的 `janpo game` 一致。
    let start (ruleset: Ruleset) (seed: int) : Result<Table, string> =
        let game = Game.start ruleset

        match Game.nextKyoku game with
        | None -> Error "这个规则集一局都不打"
        | Some context ->
            Rng.ofSeed seed
            |> GameState.start ruleset context
            |> Result.mapError KyokuStartError.toDisplay
            |> Result.map (fun (state, players) -> {
                Game = game
                State = state
                Players = players
                Readings = []
                Latest = None
                Fallbacks = 0
                Fault = None
            })

    // ---- 拆解 ----

    /// 现在等哪一家、它能提交什么；这一局已终或出过错则为 None。
    ///
    /// **响应阶段同时等多家时给的是第一家**——`Kyoku.run` 也是这么问的：每收一家答复就
    /// 少一家，收齐之后引擎自己按优先级裁决，因此「先被问到」不等于「优先」。
    let pending (table: Table) : LegalActions option =
        match table.Fault with
        | Some _ -> None
        | None -> GameState.legalActions table.State |> List.tryHead

    /// 这一局终了了吗。
    let isKyokuEnded (table: Table) : bool = GameState.isEnded table.State

    /// 整场的终局精算；局数序列还没走完则为 None。
    let result (table: Table) : GameResult option = Game.result table.Game

    // ---- 推进 ----

    /// 问该出手的那家要一个动作。**按座位分派**（票 23）：随机座位当场就给得出，
    /// LLM 座位只给得出一份决策包——动作要发一趟请求、由后来的一条 Msg 带回来。
    ///
    /// 异步就分岔在这里，**而不在 `apply`**：落子那一半与决策者是谁无关。
    let decide (roster: Roster) (table: Table) : Demand option =
        pending table
        |> Option.map (fun choice ->
            match Roster.playerAt choice.Seat roster with
            | SeatPlayer.Random -> Kyoku.randomPlayer table.Players table.State choice |> Demand.Ready
            | SeatPlayer.Llm config ->
                // 包里就是这一手的合法动作集，因此 `forSeat` 必然给得出一份；
                // 万一给不出（座位越界这类不该发生的事）就退回随机选手，牌桌照样推得动。
                match DecisionPackage.forSeat choice.Seat table.State with
                | Some package -> Demand.Asked(package, config)
                | None -> Kyoku.randomPlayer table.Players table.State choice |> Demand.Ready)

    /// 把一个动作落进引擎。**决策者是谁与这一半无关**——`fallback` 只是记在
    /// `Latest` 上给牌桌看的一句话，引擎那边一分待遇都不变。
    ///
    /// 和了那一手先把引擎的读法捞下来再 `step`：役种只有这一刻问得到（见 `Readings`）。
    let private played (fallback: string option) (action: Action) (table: Table) : Table =
        let readings =
            match action with
            | Action.Hora(actor, _, _) ->
                match GameState.horaOf actor table.State with
                | Ok reading -> table.Readings @ [ actor, reading ]
                // 型不成 / 无役的 Hora 压根不在合法动作集里；真走到这里就让 `step` 去拒。
                | Error _ -> table.Readings
            | _ -> table.Readings

        match GameState.step table.State action with
        | Error illegal -> {
            table with
                Fault = Some(IllegalAction.toDisplay illegal)
          }
        | Ok(next, _) -> {
            table with
                State = next
                // 一局终了的那一步就把它收进这场对局：连庄、本场与供托的结转全在 `Game.after`，
                // 牌桌一条规则都不自己判。还没终时 `Game.advance` 原样返回。
                Game = Game.advance next table.Game
                Readings = readings
                Latest = Some { Action = action; Fallback = fallback }
                Fallbacks = table.Fallbacks + (if Option.isSome fallback then 1 else 0)
          }

    /// 选手自己决出来的一手。
    let apply (action: Action) (table: Table) : Table = played None action table

    /// 兜底代打的一手（`Fallback.action` 挑的那个），`reason` 是为什么代打。
    /// **与 `apply` 同一条路**：代打的动作同样取自合法动作集，引擎不会因此放宽任何判定。
    let applyFallback (reason: string) (action: Action) (table: Table) : Table = played (Some reason) action table

    /// 推进一手：决策 + 落子。已终或出过错则原样返回（没有事情发生，不是错误）。
    ///
    /// **只推得动当场就给得出动作的那几家**：轮到 LLM 座位时它原样返回，
    /// 因为那一手要等一趟跨网请求——那条路在 Elmish 的 update 里（`TablePage`），
    /// 回执回来之后仍然交给 `apply` / `applyFallback`。
    let advance (roster: Roster) (table: Table) : Table =
        match decide roster table with
        | None
        | Some(Demand.Asked _) -> table
        | Some(Demand.Ready(action, players)) -> apply action { table with Players = players }

    /// 开下一局。这一局还没终、或局数序列已经走完，都原样返回。
    ///
    /// **不自动接着开**：一局终了时结算面板正摆在那里，自己开下一局会把它冲掉。
    let nextKyoku (table: Table) : Table =
        match GameState.kyokuEnd table.State, Game.nextKyoku table.Game with
        | Some _, Some context ->
            match GameState.start (Game.ruleset table.Game) context table.Players with
            | Error error -> {
                table with
                    Fault = Some(KyokuStartError.toDisplay error)
              }
            | Ok(state, players) -> {
                table with
                    State = state
                    Players = players
                    Readings = []
                    Latest = None
              }
        | _ -> table
