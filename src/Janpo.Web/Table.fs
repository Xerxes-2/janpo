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

/// 一次问话**为什么**作废（票 108 立了头一条，票 109 补上另外两条）。
///
/// **三种各是一个 case，不落进一个万能分支**（判据 12：错的诊断比没有诊断更贵）：
/// 它们的起因、能不能避免、以及「这一席还该不该被重新问」都不一样，
/// 而人在账单上看到一笔没落子的花销时问的第一句话就是「为什么」。
///
/// **它不带 token、不带时刻**：那两样在 `VoidedAsk` 上；这里只回答「为什么」。
[<RequireQualifiedAccess>]
type VoidCause =
    /// **问出去之后这一手翻篇了**（票 108）：引擎此刻给这一席的合法动作集与包里那一列
    /// 对不上，包里的 id 因此指着另一条动作。这一条是**兜底**——牌桌绕过了那份问话往前走,
    /// 而没有任何一处语义说过要撤它。
    | Expired
    /// **这一席在问话在飞时换了人**（票 109）：人在面板上把它拨给了自己、拨给了别的模型，
    /// 或者拨回了 bot。`taker` 是**现在坐这儿的那一位**（名牌上那句话）。
    ///
    /// **它是语义而不是兜底**：回执是**上一份配置**答的（provider / key / 人格都可能换了），
    /// 它答的不是这一席此刻的那个人——哪怕合法动作集一个字节都没变，它也不算这一席的答复。
    /// 不撤的下场票 108 §⑦ 第 4 条写着：回执赶在他出手之前回来时，**模型替他打了一手**。
    | Rebound of taker: string
    /// **开下一局**（票 109）：这一局连同它在飞的那几次问话一起翻篇，票号从此对不上。
    /// 与前两条同一本账——**同一张牌桌、同一本账**（`Table.usage` 跨局累计）。
    | NextKyoku

/// 一次**花了钱、没落子**的问话（票 108）。
///
/// **它刻意不是一条 `DecisionRecord`**：那一手根本没有发生。包里的 id 是按**问出去那一刻**
/// 的合法动作集编的号，而这一席此刻的合法动作集已经换了一份——拿它落子要么落错一手、
/// 要么当场被引擎拒（`Table.Fault`，牌桌就此停住）。留一条声称落了子的记录是句假话：
/// 气泡会替它编一句理由，回放会把它按手序摆到别人那一手上。
///
/// **但钱是真花掉的**：provider 调过、token 计过费。因此它记在这里而不是被抹掉——
/// 账单（`Table.usage`）报的是**花掉的总额**，不是「落了子的那几手的总额」。
/// 「花了钱、没落子」是一种真实情形，这个类型就是说得出它的那个记录。
type VoidedAsk = {
    /// 那一次问话的票号（`LiveTable.Ticket` 那一本账，跨席唯一）。
    ///
    /// **它是「回执晚回来时该把账补在哪一条」的键**：作废那一刻回执常常还在飞，
    /// token 那几个数要等它回来才知道（`Table.creditVoid`）。
    Ticket: int
    /// 作废时这一桌已经落定了几手（`Table.Turns`）。**它不是「第几手」**——
    /// 这一次问话没有落成一手，只是发生在这一手之后。`DecisionRecord.Turn` 那个号
    /// 是手序的锚，这一个只是给人定位用的时刻。
    Turn: int
    /// 被问的那一席。
    Seat: Seat
    /// **为什么**作废。**不许静默作废**（同票 23 给兜底定的那条规矩）——
    /// 给人看的那句中文由它加座位算出来（`VoidedAsk.reason`），**不另存一份**：
    /// 存一句现成的话，改措辞的人就得记得同时改历史里那几条。
    Cause: VoidCause
    /// 这一次问话的 token 账单。回执还没回来、provider 不报用量、
    /// 或者被问的根本不是模型席（强 AI 基线在本机跑，一分钱都不花）时是 None。
    Usage: Usage option
}

/// 作废那一笔的拆解。
[<RequireQualifiedAccess>]
module VoidedAsk =

    /// 给人看的那句话：**为什么这一笔花销没有对应的一手**。
    ///
    /// **三种起因各说各的**（`VoidCause`），措辞的唯一出处就是这里。
    let reason (ask: VoidedAsk) : string =
        let seat = Seat.index ask.Seat

        match ask.Cause with
        | VoidCause.Expired -> $"问出去之后座位 {seat} 这一手已经翻篇（引擎此刻给它的合法动作集与包里那一列对不上）：这一次问话作废，没有落子。"
        | VoidCause.Rebound taker -> $"座位 {seat} 在这一次问话在飞时换了人（现在是{taker}）：这一次问话作废，没有落子——回执是上一份配置答的，它答的不是这一席此刻的那个人。"
        | VoidCause.NextKyoku -> $"开下一局时座位 {seat} 这一次问话还在飞：这一局连同它一起翻篇，这一次问话作废，没有落子。"

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
    /// 作废掉的那几次问话，**按作废顺序**（票 108）。
    ///
    /// **它与 `Decisions` 是一本账的两半**：那边是「花了钱、落了子」，这边是
    /// 「花了钱、没落子」。**两边都进 `usage`**（账单报的是花掉的总额），
    /// **只有那边进牌谱**（牌谱记的是这一场真的发生过什么）。
    ///
    /// **跨局累计**（同 `Decisions` / `fallbacks`），只在重开一桌时随整张牌桌一起没了。
    Voided: VoidedAsk list
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
    /// 要问**坐在这台浏览器前的那个人**（票 87）：同样只给得出一份决策包。
    ///
    /// **它与 `Asked` 只差在“问谁”**：一个发一趟跨网请求、一个把包摆在页面上等一次点击，
    /// 拿回来的同样是一个 **id**（`DecisionPackage.tryAction`）——
    /// 真人因此与模型坐同一条路：**他构造不出一个非法动作**。
    /// 没有 `config`：真人不需要 provider、key 与超时（时限是票 89）。
    | Human of package: DecisionPackage
    /// 要问**浏览器里那个 WASM 网络**（票 92；ADR-0006）：同样只给得出一份决策包。
    ///
    /// **三者差的只是“问谁”**：发一趟跨网请求 / 摆在页面上等一次点击 / 交给本机的
    /// wasm 跑一次前向，拿回来的同样是一个 **id**。没有 `config`：它没有 provider、
    /// 没有 key，而那份资产整桌只有一份（拉没拉到是 `BaselineStatus` 的事）。
    ///
    /// **它不是 `Ready`**：抨理本身只要 0.7 ms，但资产是异步拉的、wasm 是异步起的，
    /// 而这一层要在 dotnet 上编得过（页面逻辑的用例跑在那边）——因此它走与 `Asked`
    /// 逐字相同的那条缝：Elmish 的一条 Msg。
    | Baseline of package: DecisionPackage

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
                Voided = []
                Fault = None
            })

    // ---- 拆解 ----

    /// 这一桌兜底代打了几手。**从决策记录数出来而不另存一份**（票 26）：
    /// 兜底只发生在问过模型的那几手上，而那几手恒有记录，两份计数只会漂。
    /// **跨局累计**：断电演习（key 故意配坏）时它就是走了几手兜底的证据。
    let fallbacks (table: Table) : int =
        table.Decisions
        |> List.sumBy (fun record -> if Option.isSome record.Fallback then 1 else 0)

    /// 这一桌到此刻为止的 token 账单（票 29b）：**逐条累加，不另存一份**。
    /// **跨局累计**，与 `fallbacks` 同一个做法（两份计数只会漂，裁决 26-6）。
    ///
    /// 它是「前缀可缓存的 prompt 真的省下钱了没有」的记账：`CacheRead` 占输入侧的比例
    /// 就是缓存命中率（`Usage.cacheHitPercent`）。
    ///
    /// **作废掉的那几次问话也在里面**（票 108 的 `Voided`）：那几次真的调了 provider、
    /// 真的计了费，只是没落成一手。账单报的是**花掉的总额**——把它们抹掉，
    /// 页面上那几个数就成了「落了子的那几手的总额」，而人付的是前一个。
    let usage (table: Table) : Usage =
        (table.Decisions |> List.choose (fun record -> record.Usage))
        @ (table.Voided |> List.choose (fun ask -> ask.Usage))
        |> List.fold Usage.add Usage.zero

    /// **花了钱、没落子**的那几次问话（票 108）：作废的问话里带着账单的那些。
    ///
    /// **不是每一条作废都花过钱**：强 AI 基线那一席在本机跑（一分钱都不花），
    /// 回执还没回来的那几条也还不知道花了多少。这个取值器答的是
    /// 「账单上那几个数里，有几笔是没落子的」。
    let paidVoids (table: Table) : VoidedAsk list =
        table.Voided |> List.filter (fun ask -> Option.isSome ask.Usage)

    /// **撤票**：因为这一席换了人而撤下来的那几票（票 109）。
    ///
    /// **它与「剪枝」（`VoidCause.Expired`）是两件事**，因此数得分开：剪枝是兜底
    /// （牌桌已经绕过那份问话往前走了，谁也没说要撤它），撤票是语义（人把这一席交给了别人）。
    /// 阴性对照量的就是这一个数——**没换人的一局里它必须是 0**（判据 20）。
    ///
    /// **逐 case 穷举**：`VoidCause` 加了新的一种时，编译器会把这里指出来。
    let revoked (table: Table) : VoidedAsk list =
        table.Voided
        |> List.filter (fun ask ->
            match ask.Cause with
            | VoidCause.Rebound _ -> true
            | VoidCause.Expired
            | VoidCause.NextKyoku -> false)

    /// 账单里那几笔没落子的花销里，**因为换人而撤下来的**那几笔（票 110）。
    ///
    /// **它是账单行上第二个数的唯一来源**（票 107 的逐数溯源：印上去的每一个数
    /// 都要指得回一处具名的来源），第一个数是 `paidVoids`。**两处不是同一处**：
    /// 这一个只数「人拨座位撤下来的」（`VoidCause.Rebound`），那一个数全部三种起因。
    ///
    /// **它是 `revoked` 与 `paidVoids` 的交**，不另写一道谓词：账单行拆的是**钱**，
    /// 因此两个数都只数已经上了账的那几笔——回执还在飞的那几笔还不知道花了多少。
    let paidRevoked (table: Table) : VoidedAsk list =
        revoked table |> List.filter (fun ask -> Option.isSome ask.Usage)

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

    /// 现在等着答复的**每一家**、各自能提交什么；这一局已终或出过错则为空。
    ///
    /// **响应阶段可能同时有好几家**（票 74 把它们一次全问出去）；摸牌后恒只有一家。
    /// 引擎收齐才按优先级裁决，因此这里的顺序只是「问的顺序」，不是裁决顺序。
    let pendings (table: Table) : LegalActions list =
        match table.Fault with
        | Some _ -> []
        | None -> GameState.legalActions table.State

    /// 现在等着答复的**头一家**；这一局已终或出过错则为 None。
    ///
    /// 它是「还推不推得动」的判据与**落子顺序的锚**：回放重建响应阶段的提交时
    /// 就是按「每次取头一家」重建的（`Replay.stepResponse`），因此并发问话之后的**落子**
    /// 也得沿它的顺序走（`TableState.drain`），手序号才与回放逐帧对得上。
    let pending (table: Table) : LegalActions option = pendings table |> List.tryHead

    /// 这一局终了了吗。
    let isKyokuEnded (table: Table) : bool = GameState.isEnded table.State

    /// 整场的终局精算；局数序列还没走完则为 None。
    let result (table: Table) : GameResult option = Game.result table.Game

    // ---- 推进 ----

    /// 问**点名的那一家**要一个动作；它此刻不在待答之列时是 None。**按座位分派**（票 23）：
    /// bot 座位当场就给得出，LLM 座位只给得出一份决策包——动作要发一趟请求、
    /// 由后来的一条 Msg 带回来。响应阶段同时等多家时，票 74 对每一家各调一次。
    ///
    /// 异步就分岔在这里，**而不在 `apply`**：落子那一半与决策者是谁无关。
    let decideFor (seat: Seat) (roster: Roster) (table: Table) : Demand option =
        pendings table
        |> List.tryFind (fun choice -> choice.Seat = seat)
        |> Option.map (fun choice ->
            let bot (kind: Bot) =
                Bot.player kind table.Players table.State choice |> Demand.Ready

            // 包里就是这一手的合法动作集，因此 `forSeat` 必然给得出一份；
            // 万一给不出（座位越界这类不该发生的事）就退回均匀随机选手，牌桌照样推得动。
            let packaged (demand: DecisionPackage -> Demand) =
                match DecisionPackage.forSeat choice.Seat table.State with
                | Some package -> demand package
                | None -> bot Bot.Uniform

            match Roster.playerAt choice.Seat roster with
            | SeatPlayer.Bot kind -> bot kind
            | SeatPlayer.Llm config -> packaged (fun package -> Demand.Asked(package, config))
            // 真人坐席消费的就是同一份投影（术语表的 `Observation Projection`）：
            // 隐藏信息的保护因此在**结构上**成立，不靠渲染层的纪律。
            | SeatPlayer.Human -> packaged Demand.Human
            // 强 AI 基线消费的也是同一份投影（票 92）：它看得见的东西与模型席、真人席
            // 一字不差——**强度参照系必须与被参照的那几席看同一张牌桌**，
            // 否则它强在哪里就说不清了。
            | SeatPlayer.Baseline -> packaged Demand.Baseline)

    /// 问待答头一家要一个动作（`decideFor` 的头一家特例，纯 bot 那条路还在走它）。
    let decide (roster: Roster) (table: Table) : Demand option =
        pending table |> Option.bind (fun choice -> decideFor choice.Seat roster table)

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

    /// 作废一次问话（票 108）：**花了钱、没落子**。
    ///
    /// **它不走 `played`**：那一手没有发生，因此手序不动、事件流不动、
    /// 一条决策记录都不留——留下的只是这一笔花销与它的原因。
    let voidAsk (ask: VoidedAsk) (table: Table) : Table = {
        table with
            Voided = table.Voided @ [ ask ]
    }

    /// 作废掉那一票的回执晚回来了：把它的 token 补进账（票 108）。
    ///
    /// **座位与票号都要对上**（票 74 那条规矩，作废之后照样成立）；对不上就原样返回
    /// ——重开过一桌、开过下一局的那几份回执本来就不属于这张牌桌。
    /// **重复补同一票是幂等的**（覆盖那一格，不是累加）。
    let creditVoid (ticket: int) (seat: Seat) (usage: Usage option) (table: Table) : Table = {
        table with
            Voided =
                table.Voided
                |> List.map (fun ask ->
                    if ask.Ticket = ticket && ask.Seat = seat then
                        { ask with Usage = usage }
                    else
                        ask)
    }

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
        | Some(Demand.Asked _)
        // 真人那一手同样推不动（票 87）：它要等一次点击，而那条路在 Elmish 的 update 里。
        | Some(Demand.Human _)
        // 强 AI 基线同理（票 92）：它要等 wasm 跑完那一次前向。
        | Some(Demand.Baseline _) -> table
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

    /// 这一桌的**逐席记分**（票 133）：终局记分卡上那几格里，牌谱本身答得出的那些。
    ///
    /// **不问配桌**（因此回放那一侧也调得动，Live 与回放同一条路）：记分卡上唯一要问配桌的
    /// 是「选手 · 档」那一列，而那一列在渲染层（`TableState.scorecardPlayers`）。
    /// 这里因此不拼 `start_game`——那条事件的 `names` 才是要配桌的那一样，
    /// 而记分卡一个字都不读它。
    ///
    /// **它就是 `Scorecard.ofPaifu` 的同一段代码**（`Scorecard.tally`），不是第二份实现。
    let scorecard (table: Table) : SeatTally list =
        let current =
            if GameState.isEnded table.State then
                []
            else
                GameState.events table.State

        Scorecard.tally (Game.ruleset table.Game) (Game.events table.Game @ current) table.Decisions

    // ---- 回放（票 71） ----

    /// 牌谱开头那条 `start_game` 里那一列名字（mjai 的 `names`），按座位升序。
    ///
    /// **回放里它是「这一席是谁在打」的唯一来源**（票 82 的名牌）：回放没有配桌
    /// （`TableState.rosterOf` 恒是 None），而牌谱里那几个名字是**当时录下来的**。
    /// **档案名不在里面**（`Roster.playerName`）：那是本机的私人叫法，牌谱是可分享物。
    /// 一条 `start_game` 都没有的牌谱进不了回放（`Replay.trace` 拦住了），这里回空表。
    let names (paifu: Paifu) : string list =
        paifu.Events
        |> List.tryPick (fun event ->
            match event with
            | StartGame names -> Some names
            | _ -> None)
        |> Option.defaultValue []

    /// 把一局的**开局局面**摆上这张牌桌（回放专用）。
    ///
    /// **不走 `nextKyoku`**：那一条从随机流开局，而回放的牌山是从事件流重建的
    /// （`Replay.trace`）。除了局面本身，重置的三样与 `nextKyoku` 逐字相同：
    /// 各座位的掩蔽流、这一局的读法、上一手。
    let private opened (ruleset: Ruleset) (state: GameState) (table: Table) : Table = {
        table with
            State = state
            Views = viewsOf ruleset state
            Readings = []
            Latest = None
    }

    /// 一份牌谱 → **逐帧的牌桌**（ADR-0002：回放就是对事件前缀做 fold）。
    ///
    /// 一帧就是「这一手落定之后的那一桌」，头一帧是第一局的开局；每开一局多一帧开局。
    /// 页面拿它当胶片放（`TableState` 的 `Source.Replay`），**手里只拿一个帧号**。
    ///
    /// **推进用的是 `apply`，与 Live 逐字同一条路**：役种在宣言那一刻捞下来（`Readings`，
    /// `GameState.horaOf` 只有那一刻答得出来）、掩蔽流跟着引擎吐出来的事件长（`Views`）、
    /// 上一手写在 `Latest` 上——三样都不是回放这一侧另写一份，因此也漂不了。
    ///
    /// **它不问牌谱里那几个 `names` 是谁**：回放不需要配桌（没人要出手），
    /// `Players` 那一格因此只是个占位的发生器——`decide` 在回放里一次也不会被调到。
    let replay (paifu: Paifu) : Result<Table list, string> =
        let ruleset = paifu.Ruleset

        // 到这一手为止的决策记录（票 26：`Turn` 是跨局累计的手序）。
        // **不把整份牌谱的记录摆到每一帧上**：那样第 0 帧就能看到末手的思考，
        // 而牌桌上那几行（账单、兜底计数）读的就是它。
        let recordedBy (turns: int) : DecisionRecord list =
            paifu.Decisions |> List.filter (fun record -> record.Turn < turns)

        // 那一手的决策记录（问过模型的那几手才有）。**拿它而不是 `apply`**（票 76）：
        // `Turn.Fallback` 是牌桌上那句「上一手：……（兜底：……）」与 `data-fallback` 的来源，
        // 回放里丢掉它等于把「兜底不许静默替换」（票 23）在回放那一侧静默地破掉——
        // 气泡说兜底、牌桌却一声不响。记录本身仍由下面那一行按手序切（不重复收）。
        let recordAt (turns: int) : DecisionRecord option =
            paifu.Decisions |> List.tryFind (fun record -> record.Turn = turns)

        let played (table: Table) (action: Action) : Table =
            let next = played (recordAt table.Turns) action table

            {
                next with
                    Decisions = recordedBy next.Turns
            }

        let kyoku (frames: Table list, table: Table) (each: ReplayKyoku) : Table list * Table =
            let start = opened ruleset each.Opening table

            ((start :: frames, start), each.Actions)
            ||> List.fold (fun (frames, table) action ->
                let next = played table action
                next :: frames, next)

        Replay.traceOfPaifu paifu
        |> Result.mapError ReplayError.toDisplay
        |> Result.bind (fun kyokus ->
            match kyokus with
            // 走不到：`Replay.trace` 已经拿 `ReplayError.NoKyoku` 拦住了空事件流。
            | [] -> Error "这份牌谱里一局都没有"
            | first :: _ ->
                // 摆一张空桌当 fold 的起点：第一局的 `opened` 会把同一份局面再盖一遍（幂等）。
                let blank: Table = {
                    Game = Game.start ruleset
                    State = first.Opening
                    // 回放里没有选手（没人要出手），这一格只是占位。
                    Players = Rng.ofSeed 0
                    Views = viewsOf ruleset first.Opening
                    Readings = []
                    Latest = None
                    Turns = 0
                    Decisions = []
                    Prompting = paifu.Prompting
                    // 回放里没有作废的问话（票 108）：动作全在牌谱里，没人要出手，
                    // 也因此没有任何一次问话可作废。
                    Voided = []
                    Fault = None
                }

                let frames, _ = (([], blank), kyokus) ||> List.fold kyoku
                Ok(List.rev frames))
