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
    /// 各座位的**掩蔽事件流**，按座位升序（票 29a）。
    ///
    /// **它不是第二份局面，是同一条事件流的逐座位投影**：牌桌本来就在 fold 引擎吐出来
    /// 的事件（`GameState.step` 的第二个返回值，以前直接丢掉），接上去而已。
    /// 拿它而不是每帧 `Observation.ofState` 的理由是代价：一局 95 手逐手取观测，
    /// 每帧重头 fold 全流是 29 ms（O(n²)），增量维护是 0.56 ms。
    Views: SeatStream list
    /// 刚落定的那一手。给牌桌显示「上一手是谁做了什么」用（`Action.toDisplay`），
    /// 并且看得出它是不是兜底代打的（`Turn.Fallback`）。
    Latest: Turn option
    /// 这一桌至今落定了几手（CONTEXT.md 的 Turn）。**手序编号的唯一出处**（票 26）：
    /// 跨局累计，随机座位的手也占号——它们不产生决策记录，但不能因此把编号弄断。
    Turns: int
    /// 这一桌至今的决策记录，按手序（票 26）。**只有问过模型的那几手在里面**：
    /// 随机选手没有可审计的推理，写一条空记录只会把牌谱撑胖。
    Decisions: DecisionRecord list
    /// prompt 的前置（票 31）：各座位的固定 preamble 与工具定义形状，**整场各存一次**。
    ///
    /// **它不是第二份 prompt**：每手的尾部在各自的记录里，前缀由这一段加事件流重算得出来。
    Prompting: Prompting
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
    /// 一局开头那几条事件（`start_kyoku` 与 Oya 的 `tsumo`）先喂给各座位的掩蔽流。
    /// **一局一重置**：`start_kyoku` 在 fold 里本来就是重置，这里不必另外清场。
    let private viewsOf (ruleset: Ruleset) (state: GameState) : SeatStream list =
        let events = GameState.events state

        Seat.all ruleset
        |> List.map (fun seat -> SeatStream.start ruleset seat |> SeatStream.advanceAll events)

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
                Views = viewsOf ruleset state
                Readings = []
                Latest = None
                Turns = 0
                Decisions = []
                Prompting = Prompting.empty
                Fault = None
            })

    // ---- 拆解 ----

    /// 这一桌兜底代打了几手。**从决策记录数出来而不另存一份**（票 26）：
    /// 兜底只发生在问过模型的那几手上，而那几手恒有记录，两份计数只会漂。
    /// **跨局累计**：断电演习（key 故意配坏）时它就是走了几手兜底的证据。
    let fallbacks (table: Table) : int =
        table.Decisions
        |> List.sumBy (fun record -> if Option.isSome record.Fallback then 1 else 0)

    /// 这一桌到此刻为止的 token 账单（票 29b）：**逐条决策记录累加**，不另存一份。
    /// **跨局累计**，与 `fallbacks` 同一个做法（两份计数只会漂，裁决 26-6）。
    ///
    /// 它是「前缀可缓存的 prompt 真的省下钱了没有」的记账：`CacheRead` 占输入侧的比例
    /// 就是缓存命中率（`Usage.cacheHitPercent`）。
    let usage (table: Table) : Usage =
        table.Decisions
        |> List.choose (fun record -> record.Usage)
        |> List.fold Usage.add Usage.zero

    /// 某座位此刻的观测（票 29a）：**取自增量维护的那条掩蔽流**，不重头 fold。
    /// 座位不在这个规则集里时是 None。
    let observation (seat: Seat) (table: Table) : Observation option =
        Seat.tryItem seat table.Views |> Option.bind SeatStream.observation

    /// 某座位看得见的那条历史（掩蔽事件流）。**观测就是它的 fold**，
    /// 因此两种形态不可能对不上。
    let history (seat: Seat) (table: Table) : MaskedEvent list =
        Seat.tryItem seat table.Views
        |> Option.map SeatStream.events
        |> Option.defaultValue []

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

    /// 问该出手的那家要一个动作。**按座位分派**（票 23）：bot 座位当场就给得出，
    /// LLM 座位只给得出一份决策包——动作要发一趟请求、由后来的一条 Msg 带回来。
    ///
    /// 异步就分岔在这里，**而不在 `apply`**：落子那一半与决策者是谁无关。
    let decide (roster: Roster) (table: Table) : Demand option =
        pending table
        |> Option.map (fun choice ->
            let bot (kind: Bot) =
                Bot.player kind table.Players table.State choice |> Demand.Ready

            match Roster.playerAt choice.Seat roster with
            | SeatPlayer.Bot kind -> bot kind
            | SeatPlayer.Llm config ->
                // 包里就是这一手的合法动作集，因此 `forSeat` 必然给得出一份；
                // 万一给不出（座位越界这类不该发生的事）就退回均匀随机选手，牌桌照样推得动。
                match DecisionPackage.forSeat choice.Seat table.State with
                | Some package -> Demand.Asked(package, config)
                | None -> bot Bot.Uniform)

    /// 把一个动作落进引擎。**决策者是谁与这一半无关**——`record` 只是审计数据：
    /// 它里的兜底原因记在 `Latest` 上给牌桌看，引擎那边一分待遇都不变。
    ///
    /// 和了那一手先把引擎的读法捞下来再 `step`：役种只有这一刻问得到（见 `Readings`）。
    let private played (record: DecisionRecord option) (action: Action) (table: Table) : Table =
        let fallback = record |> Option.bind (fun record -> record.Fallback)

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
        | Ok(next, produced) -> {
            table with
                State = next
                // 引擎刚吐出来的那几条事件直接接进各座位的掩蔽流（票 29a）。
                Views = table.Views |> List.map (SeatStream.advanceAll produced)
                // 一局终了的那一步就把它收进这场对局：连庄、本场与供托的结转全在 `Game.after`，
                // 牌桌一条规则都不自己判。还没终时 `Game.advance` 原样返回。
                Game = Game.advance next table.Game
                Readings = readings
                Latest = Some { Action = action; Fallback = fallback }
                // 手序只在真的落定了一手时往前走（被引擎拒掉的那一条走上面那支）。
                Turns = table.Turns + 1
                Decisions = table.Decisions @ Option.toList record
          }

    /// 选手自己决出来的一手，**没有可审计的推理**（随机座位）。
    let apply (action: Action) (table: Table) : Table = played None action table

    /// 问过模型的那一手：动作加它的决策记录（票 26），以及那一手带来的 prompt 前置（票 31）。
    ///
    /// **兜底与否写在记录里**（`DecisionRecord.Fallback`），牌桌上那句「兜底：……」与
    /// 兜底计数都取自同一处。**与 `apply` 同一条路**：代打的动作同样取自合法动作集，
    /// 引擎不会因此放宽任何判定。
    ///
    /// 前置按「座位 + 渲染版本」去重：一场里换了人格就多一条，没换就整场只有一条。
    let applyRecorded (record: DecisionRecord) (prompting: Prompting) (action: Action) (table: Table) : Table =
        let played = played (Some record) action table

        {
            played with
                Prompting = Prompting.add prompting played.Prompting
        }

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
                    Views = viewsOf (Game.ruleset table.Game) state
                    Readings = []
                    Latest = None
              }
        | _ -> table

    // ---- 牌谱 ----

    /// 这一桌到此刻为止的 mjai 事件流，开头那条 `start_game` 的 `names` 来自配桌。
    ///
    /// **打到一半也取得出来**：已经打完的局在 `Game` 里，正在打的那一局在 `State` 里。
    /// 这一局刚终了时它已经被 `Game.advance` 收进去了，因此不能再拼一遍。
    let events (roster: Roster) (table: Table) : Event list =
        let current =
            if GameState.isEnded table.State then
                []
            else
                GameState.events table.State

        StartGame(Roster.names roster) :: (Game.events table.Game @ current)

    /// 这一桌到此刻为止的牌谱（ADR-0002：**唯一的可分享物**）：
    /// 规则集 + 事件流 + 决策记录 + 版本号。
    ///
    /// **局面不在里面**：它是对事件流 fold 出来的（`Replay.ofPaifu`），不存第二份。
    let paifu (roster: Roster) (table: Table) : Paifu =
        Paifu.create (Game.ruleset table.Game) (events roster table) table.Decisions table.Prompting
