namespace Janpo.Web

open Janpo

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
    /// 四家选手共用的选手状态（`Player<Rng>` 里的 `'player`）。M1 四家都是随机选手，
    /// 因此它就是一个随机发生器。
    ///
    /// **23 票要换的是它**：那一票把某个座位换成 LLM，选手状态里得能装下
    /// 「这个座位的决策要 await」这件事，而引擎侧的同步 `Player<'player>` 装不下——
    /// 换法见 `decide` 的注释。
    Players: Rng
    /// 和了那一手引擎算出的读法，按宣言顺序（双响会有两条）。
    ///
    /// **它不是第二份局面，是捞下来的引擎输出**：`Event.Hora` 上没有役种字段
    /// （mjai wire 就没有），而 `GameState.horaOf` 只在宣言的那一刻答得出来——
    /// 一局终了之后阶段已是 `Ended`，再问就是 `NoAgariShape`。结算要显示的役种只有这一个来源。
    Readings: (Seat * HoraReading) list
    /// 刚落定的那一手。给牌桌显示「上一手是谁做了什么」用（`Action.toDisplay`）。
    Latest: Action option
    /// 引擎拒绝了某个动作。**不该发生**（提交的动作都取自合法动作集），
    /// 落在这里就停住不再推进，把话说给人看，而不是静静地卡住。
    Fault: string option
}

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

    /// 问该出手的那家要一个动作，附推进后的选手状态。
    ///
    /// **23 票的异步座位从这条缝里分岔**：那一票按 `LegalActions.Seat` 分派——随机座位仍走
    /// 这里，LLM 座位改成「发一个请求，动作由后来的一条 Msg 带回来」，回来之后交给 `apply`。
    /// 落子那一半与决策者是谁无关，因此那一票不必动 `apply`。
    let decide (table: Table) : (Action * Rng) option =
        pending table |> Option.map (Kyoku.randomPlayer table.Players table.State)

    /// 把一个动作落进引擎。**决策者是谁与这一半无关。**
    ///
    /// 和了那一手先把引擎的读法捞下来再 `step`：役种只有这一刻问得到（见 `Readings`）。
    let apply (action: Action) (table: Table) : Table =
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
                Latest = Some action
          }

    /// 推进一手：决策 + 落子。已终或出过错则原样返回（没有事情发生，不是错误）。
    /// **Elmish 的 update 直接调它**，不另写一个驱动循环。
    let advance (table: Table) : Table =
        match decide table with
        | None -> table
        | Some(action, players) -> apply action { table with Players = players }

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
