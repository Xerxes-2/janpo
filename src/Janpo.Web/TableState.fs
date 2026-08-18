namespace Janpo.Web

open Elmish
open Fable.Core
open Thoth.Json.JavaScript
open Janpo

/// 在等哪一次问话的回执（票 23）。
///
/// **票号与播放控制的世代号是两件事**：那个管定时器，这个管在飞的请求。
/// 重开一桌之后旧回执才回来是常事（一次请求动辄几秒），而它的 id 是按另一份
/// 决策包编的号——拿它去 `tryAction` 会拿到一个语义完全不同的动作。
type Awaiting = {
    /// 这一次问话的票号。回执带的票号对不上就丢掉。
    Ticket: int
    /// 问的那一手的决策包。id 往回换动作（`tryAction`）与兜底（`Fallback.action`）都要它。
    Package: DecisionPackage
    /// 那个座位的配置（兜底策略按它的档位走）。
    Config: LlmSeat
}

/// 这一局定型的那两格：**人格与 prompt 模板**（CONTEXT.md 的 `Persona` / `PromptTemplate`）。
///
/// 术语表把 `Persona` 定成**一局内不变**：它俩都在可缓存前缀里，打到一半换等于把这一局攒下的
/// provider 缓存全废，还让同一局面的对照多出一个自变量。这个类型就是那条不变量的执行者
/// ——一局的**头一次问话**把它俩定住，改动落到面板与 localStorage，但要等下一局才发得出去。
///
/// **边界取「局」不是「场」**：局间更换仍然支持，牌谱按「座位 + 渲染版本」记得住每一版
/// （那正是 `Paifu.Preamble` 是一个列表的理由，见 `src/Janpo.Engine/Paifu.fs` 那段注释）。
///
/// **只有这两格**：provider / 模型 / key / 超时 / 思考预算换了不动前缀的字节，
/// 脚手架档位只动尾部——它们照旧下一手就生效。
type Rendering = { Persona: string; Template: string }

/// 定型那两格的取与用。
[<RequireQualifiedAccess>]
module Rendering =

    /// 这份配置此刻的人格与模板。
    let ofSeat (seat: LlmSeat) : Rendering = {
        Persona = seat.Persona
        Template = seat.Template
    }

    /// 把定型的那两格盖回配置里。**其余字段照面板现在的样子**：
    /// 它们不在可缓存前缀里（provider / 模型 / key / 超时 / 思考预算），
    /// 或者只动尾部（脚手架档位），换了下一手就该生效。
    let applyTo (rendering: Rendering) (seat: LlmSeat) : LlmSeat = {
        seat with
            Persona = rendering.Persona
            Template = rendering.Template
    }

/// Agent 层此刻处在哪一步。**页面上要看得见**：断电演习（故意配一把坏 key）时
/// 对局照样打得完，但不能静惄惄地打——人得知道模型早就不说话了。
[<RequireQualifiedAccess>]
type AgentStatus =
    /// 没有 LLM 座位，或者还没轮到它。
    | Idle
    /// 正在等这个座位的回执。
    | Asking of seat: Seat
    /// 上一次模型自己选出了动作。
    | Spoke of seat: Seat * reason: string option * latencyMs: int
    /// 上一次是兜底代打的。**粘着不掉**，直到模型又能好好说话为止。
    | Troubled of seat: Seat * reason: string

/// 牌桌页面的全部状态。**没有第二份牌局状态**（ADR-0002）：牌局在 `Table` 里，
/// 而 `Table` 里的局面是引擎的那一份；页面自己只多种子输入框的文本、播放控制、
/// 看哪一份投影，以及配桌与 Agent 层的那几样。
type TableModel = {
    /// 这一桌的规则集。M1 只跑四麻默认预设，配桌是后面的票。
    Ruleset: Ruleset
    /// 输入框里的文本。**没解析过**——解析在「重开」那一步做，因此打字不会重开一桌。
    SeedText: string
    /// 牌桌；开不了局时是中文错误文案。
    Table: Result<Table, string>
    /// 播放控制。
    Playback: Playback
    /// 看哪一份投影。
    Viewpoint: Viewpoint
    /// 牌桌上要不要把危险度排序显示出来（票 25）。**默认关**：
    /// 它是围观者想看的东西，不是牌桌本来就该摆着的。
    ShowDanger: bool
    /// 哪个座位交给 LLM；None = 四家都是自带 bot。
    LlmAt: Seat option
    /// 不是 LLM 的那几家由哪种自带 bot 坐（票 42）。**默认均匀随机**：
    /// 默认视图上那几道闸门（曳光弹对拍、牌谱导出、副露来源）量的都是它跑出来的那几局。
    Bot: Bot
    /// 那个座位的配置（也就是配置面板里填的那份，同时落在 localStorage）。
    ///
    /// **它不一定就是这一手发出去的那份**：人格与模板在一局之内定住（见 `Rendering`），
    /// 真正发出去的那份由 `TablePage.rosterOf` 推导。
    Llm: LlmSeat
    /// 这一局已经定型的人格与模板；`None` = 这一局还没问过话，改了当场生效。
    ///
    /// **在一局的头一次问话时定住**，`Restarted` 与 `KyokuAdvanced` 时松开。
    /// 定住之后面板照收编辑（`Llm` 会变），但发出去的仍是这一份——页面上那行
    /// 「下一局生效」说的就是它俩不一致（`TablePage.renderingPending`）。
    Pinned: Rendering option
    /// 在等回执吗。**等着的时候不续定时器**，否则牌桌会空转。
    Awaiting: Awaiting option
    /// 问话的票号，每问一次 +1。
    Ticket: int
    /// Agent 层的状态线。
    Agent: AgentStatus
}

/// 牌桌上能发生的事。**一步一 Msg**：`Advanced` 与 `Ticked` 各推进一手，
/// 驱动循环就是 Elmish 的 update，页面里没有第二个 loop。
type TableMsg =
    /// 改种子输入框。
    | SeedEdited of seed: string
    /// 按当前种子重开一桌。
    | Restarted
    /// 单步：推进一手，并暂停。
    | Advanced
    /// 播 / 暂停。
    | PlayToggled
    /// 换倍速。
    | SpeedPicked of speed: Speed
    /// 定时器回来了。`generation` 不是当前世代就丢掉（见 `Playback.accepts`）。
    | Ticked of generation: int
    /// 换视角（坐到某个座位 / 上帝视角）。
    | ViewpointPicked of viewpoint: Viewpoint
    /// 开 / 关牌桌上的危险度排序（票 25）。
    | DangerToggled
    /// 这一局看完了，开下一局。
    | KyokuAdvanced
    /// 把哪个座位交给 LLM。
    | LlmSeatPicked of seat: Seat option
    /// 其余座位换成哪种自带 bot（票 42）。
    | BotPicked of kind: Bot
    /// 改配置面板里的一个字段。
    | LlmEdited of field: LlmField * value: string
    /// 把这一桌到此刻为止的牌谱存成一个 JSON 文件（票 26）。
    | Exported
    /// Agent 层的回执回来了。`ticket` 不是在等的那一张就丢掉（见 `Awaiting`）。
    ///
    /// **它不会不来**：超时与 provider 报错在 Agent 层都是值，最后也会变成一条回执
    /// （`Failure` 带着原因）——对局因此永不卡死。
    | Answered of ticket: int * answer: AgentAnswer

/// 牌桌页面的状态与 MVU 三件套（票 70 从 `TablePage.fs` 拆出来的第一块）。
///
/// **视图一行都不在这里**：牌桌与结算在 `TableBoard`、配桌与模型面板在 `TablePanel`、
/// Agent 层那两行状态在 `AgentLine`，把它们装回一页的外壳在 `TablePage`。
/// 拆的判据是「哪张票会改它」而不是「代码看着像一类」——这个文件是**改状态与消息**的那些票的落点。
///
/// 页面逻辑的用例（`tests/Janpo.Web.Tests`）走的是 `TablePage` 那一层的同名入口。
[<RequireQualifiedAccess>]
module TableState =

    // ---- MVU ----

    /// 页面初次打开时摆的那一桌。挑它两个理由：东 1 局里既有碰吃也有杠（副露的形态看得到），
    /// 且以和了终（结算面板才有役种与番符可看）。挑种子的探针见报告 22。
    ///
    /// **刻意不用曳光弹那个种子**：`?dev=1` 把曳光弹挂出来时，它把原始 mjai 事件
    /// 打在同一张文档里，而 `start_kyoku` 带着四家配牌——两边同种子的话，
    /// 牌桌遮起来的那几家手牌就在下面躺着。
    let private defaultSeed = 2088

    let private parseSeed (text: string) : Result<int, string> =
        match System.Int32.TryParse(text.Trim()) with
        | true, seed -> Ok seed
        | false, _ -> Error $"种子要是一个整数，得到「{text}」"

    /// 按这一桌的**配桌**开局：牌桌的规则集就是 `Roster` 里的那一份（CONTEXT.md 的 Roster），
    /// 因此不会出现「四家的配桌配上三麻的牌桌」。
    let private openTable (roster: Roster) (seedText: string) : Result<Table, string> =
        parseSeed seedText |> Result.bind (Table.start roster.Ruleset)

    /// 定时器：播着的时候续下一记，暂停时什么也不发。
    /// **世代号一并带上**——暂停期间在飞的那一记回来时会被 `Playback.accepts` 丢掉。
    let private schedule (playback: Playback) : Cmd<TableMsg> =
        if playback.Playing then
            Cmd.ofEffect (fun dispatch ->
                JS.setTimeout (fun () -> dispatch (Ticked playback.Generation)) (Speed.interval playback.Speed)
                |> ignore)
        else
            Cmd.none

    /// 把配置写进 localStorage。**走 Cmd 而不是在 update 里直接写**：MVU 的 update 是纯的，
    /// 副作用一律由 Cmd 发——顺带让页面逻辑的用例在 dotnet 上跑得起来（那边没有 localStorage）。
    let private save (write: unit -> unit) : Cmd<TableMsg> = Cmd.ofEffect (fun _ -> write ())

    /// 续一记定时器——**除非正在等回执**。等着的时候定时器只会把牌桌空转一遍；
    /// 那一手由 `Answered` 接着开动。
    let private tick (model: TableModel) (playback: Playback) : Cmd<TableMsg> =
        if Option.isSome model.Awaiting then
            Cmd.none
        else
            schedule playback

    /// 这一手真正发出去的那份座位配置（票 46）：人格与模板取**本局定型的那一版**，
    /// 其余字段取面板现在的值。还没定型（这一局一次都没问过）时就是面板上那份。
    let private effective (model: TableModel) : LlmSeat =
        match model.Pinned with
        | None -> model.Llm
        | Some pinned -> model.Llm |> Rendering.applyTo pinned

    /// 这一桌的配桌：一席交给 LLM（选了的话），其余交给选中的那种自带 bot。
    /// **推导出来而不存下来**：配置只有 `Bot` / `LlmAt` / `Llm` 这一份，不会与第二份对不上。
    ///
    /// **公开的**：页面逻辑的用例要问「这一桌到底谁坐哪里」，在用例里拄一份同样的推导
    /// 只会与这里漂（票 42 前它真的漂过一份，在 `PaifuExportTests`）。
    let rosterOf (model: TableModel) : Roster =
        Roster.withLlm model.Ruleset model.Bot model.LlmAt (effective model)

    /// 面板上那两格改过了、但要等下一局才发得出去吗（票 46）。
    ///
    /// **它就是页面上那句「下一局生效」的判据**：不锁那两格，但也绝不静默地半局换掉。
    /// **公开的**：视图与页面逻辑的用例读同一个判据，拄一份同样的推导只会漂。
    let renderingPending (model: TableModel) : bool =
        match model.Pinned with
        | None -> false
        | Some pinned -> pinned <> Rendering.ofSeat model.Llm

    /// 导出文件的名字。**种子只有解析得出来才进文件名**：输入框里是人随手填的文本，
    /// 原样拼进文件名会把斜杠之类的东西带进去。
    let private exportName (seedText: string) : string =
        match parseSeed seedText with
        | Ok seed -> $"janpo-paifu-{seed}.json"
        | Error _ -> "janpo-paifu.json"

    /// 导出这一桌的牌谱。**编码在这里，落盘交给浏览器**（`Download`）：
    /// 本平台没有后端，文件是浏览器自己在本地生成的（ADR-0003）。
    /// 走 Cmd 而不是在 update 里直接写：副作用一律由 Cmd 发，update 保持纯的。
    let private exportCmd (roster: Roster) (fileName: string) (table: Table) : Cmd<TableMsg> =
        Cmd.ofEffect (fun _ ->
            Table.paifu roster table
            |> Paifu.encoder
            |> Encode.toString 0
            |> Download.json fileName)

    /// 发一次问话。**不用 `Cmd.OfPromise`**：它整段包在 `#if FABLE_COMPILER` 里，
    /// 而这个文件要在 dotnet 上编得过（页面逻辑的用例跑在那边）。
    /// 效果体只在浏览器里执行，dotnet 侧只把它编出来、不跑。
    let private askCmd (ticket: int) (request: AgentRequest) : Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch ->
            let answered (answer: AgentAnswer) = dispatch (Answered(ticket, answer))
            (Agent.ask request).``then`` answered |> ignore)

    /// 推进一手。**这就是驱动循环的一步**：问该出手那家要一个动作。
    /// 随机座位当场落子；LLM 座位发一个请求出去，这一手到 `Answered` 才落子。
    let private step (model: TableModel) : TableModel * Cmd<TableMsg> =
        match model.Awaiting, model.Table with
        // 上一次问话还没回来：不再问第二次（同一手会有两个请求在飞，而只有一个算数）。
        | Some _, _ -> model, Cmd.none
        | None, Error _ -> model, Cmd.none
        | None, Ok table ->
            match Table.decide (rosterOf model) table with
            | None -> model, Cmd.none
            | Some(Demand.Ready(action, players)) ->
                {
                    model with
                        Table = Ok(Table.apply action { table with Players = players })
                },
                Cmd.none
            | Some(Demand.Asked(package, config)) ->
                let ticket = model.Ticket + 1

                {
                    model with
                        Ticket = ticket
                        // 这一局的头一次问话把人格与模板定住（票 46）：之后再改只落到面板，
                        // 本局发出去的字节不再变。已经定型的局里重盖同一份，无影响。
                        Pinned = Some(Rendering.ofSeat config)
                        Awaiting =
                            Some {
                                Ticket = ticket
                                Package = package
                                Config = config
                            }
                        Agent = AgentStatus.Asking(DecisionPackage.seat package)
                },
                askCmd ticket {
                    Package = package
                    Seat = config
                    RetryLimit = Agent.retryLimit
                }

    /// 还推得动吗（这一局没终、也没出错）。
    let internal canAdvance (model: TableModel) : bool =
        match model.Table with
        | Ok table -> Table.pending table |> Option.isSome
        | Error _ -> false

    /// 落完一手之后：接着播还是停下来。
    ///
    /// **等回执的那段不续定时器**（但仍然是 `Playing`）：定时器只会把牌桌空转一遍，
    /// 真正把它接着开动的是那条 `Answered`。一局终了也停下来：结算面板正摆在那里。
    let private resume (cmd: Cmd<TableMsg>) (model: TableModel) : TableModel * Cmd<TableMsg> =
        if Option.isSome model.Awaiting then
            model, cmd
        elif canAdvance model then
            model, Cmd.batch [ cmd; schedule model.Playback ]
        else
            {
                model with
                    Playback = Playback.pause model.Playback
            },
            cmd

    /// 回执 → 落子，并留下这一手的决策记录。**兜底就在这里**：id 换不回动作
    /// （模型没给、给的越界、超时、provider 报错）就由 `Fallback.action` 代打。
    ///
    /// **DecisionRecord 只在这一处组装**（票 26）：只有这里同时拿得到那一手的决策包、
    /// Agent 层的全部回执、最终落定的动作与「是不是兜底」。`turn` 是它在这一场里的手序号，
    /// 取自牌桌（`Table.Turns`）而不是记录数——随机座位的手同样占号。
    let private settle (awaiting: Awaiting) (answer: AgentAnswer) (table: Table) : Table * AgentStatus =
        let seat = DecisionPackage.seat awaiting.Package

        let action, fallback =
            match
                answer.ActionId
                |> Option.bind (fun id -> DecisionPackage.tryAction id awaiting.Package)
            with
            | Some action -> action, None
            | None ->
                // Agent 层没给原因就只能是「id 不在这一包里」：它自己校过一道，能走到这里
                // 说明两边对 id 的看法分了岔（例：回执延到了下一份包）。
                let reason =
                    answer.Failure |> Option.defaultValue $"模型给回的动作 id 不在这一包里（{answer.ActionId}）"

                Fallback.action awaiting.Config.Tier awaiting.Package, Some reason

        let record: DecisionRecord = {
            Turn = table.Turns
            Seat = seat
            // **只存尾部**（票 31）：前缀是事件流的派生物，而事件流就在同一份牌谱里。
            PromptTail = answer.PromptTail
            RenderVersion = answer.RenderVersion
            ActionIds = answer.ActionIds
            Output = answer.Output
            Reason = answer.Reason
            Thinking = answer.Thinking
            Attempts = answer.Attempts
            LatencyMs = answer.LatencyMs
            // 兜底代打挑的那条也取自这一包（`Fallback.action`），因此 id 恒找得回。
            Applied = DecisionPackage.tryId action awaiting.Package
            Fallback = fallback
            Usage = answer.Usage
        }

        // 那一手带来的前置：固定 preamble 与工具定义形状。**牌桌按「座位 + 渲染版本」去重**，
        // 因此整场只存一份（局间换了人格就多一份；一局之内换不了，见 `Rendering`）。
        let prompting: Prompting = {
            Tools = answer.Tools
            Preambles = [
                {
                    Seat = seat
                    RenderVersion = answer.RenderVersion
                    Text = answer.Preamble
                }
            ]
        }

        let played = Table.applyRecorded record prompting action table

        match fallback with
        | None -> played, AgentStatus.Spoke(seat, answer.Reason, answer.LatencyMs)
        | Some reason -> played, AgentStatus.Troubled(seat, reason)

    /// 初次摆的那一桌，**配置从外面给**。拆出来是为了它是纯的：
    /// 读 localStorage 在 `init` 那一层，因此页面逻辑的用例（dotnet 侧）用得上这个入口。
    let initial (llmAt: Seat option) (config: LlmSeat) : TableModel * Cmd<TableMsg> =
        let ruleset = Ruleset.yonma
        let seedText = string defaultSeed

        {
            Ruleset = ruleset
            SeedText = seedText
            Table = openTable (Roster.withLlm ruleset Bot.Uniform llmAt config) seedText
            Playback = Playback.initial
            Viewpoint = Viewpoint.Seated Seat.first
            // 危险度默认关（票 25）。
            ShowDanger = false
            // 自带 bot 默认均匀随机（票 42）：它是黄金用例与闸门的基准。
            Bot = Bot.Uniform
            LlmAt = llmAt
            Llm = config
            // 还没问过话：这一局的人格与模板还改得动（票 46）。
            Pinned = None
            Awaiting = None
            Ticket = 0
            Agent = AgentStatus.Idle
        },
        Cmd.none

    /// 页面初次打开。上一次填的配置（含 key）从 localStorage 里读回来。
    let init () : TableModel * Cmd<TableMsg> =
        initial (Store.readSeat Ruleset.yonma) (Store.readSeatConfig ())

    let update (message: TableMsg) (model: TableModel) : TableModel * Cmd<TableMsg> =
        match message with
        | SeedEdited seed -> { model with SeedText = seed }, Cmd.none
        | Restarted ->
            // 在飞的那一次问话作废：它的 id 是按旧那桌的决策包编的号。
            {
                model with
                    Table = openTable (rosterOf model) model.SeedText
                    Playback = Playback.pause model.Playback
                    // 重开一桌就是回到第一局：人格与模板跟着松开（票 46）。
                    Pinned = None
                    Awaiting = None
                    Agent = AgentStatus.Idle
            },
            Cmd.none
        | Advanced ->
            {
                model with
                    Playback = Playback.pause model.Playback
            }
            |> step
        | PlayToggled ->
            let playback = Playback.toggle model.Playback
            { model with Playback = playback }, tick model playback
        | SpeedPicked speed ->
            let playback = Playback.setSpeed speed model.Playback
            { model with Playback = playback }, tick model playback
        | Ticked generation when not (Playback.accepts generation model.Playback) -> model, Cmd.none
        | Ticked _ ->
            let advanced, cmd = step model
            resume cmd advanced
        | ViewpointPicked viewpoint -> { model with Viewpoint = viewpoint }, Cmd.none
        | DangerToggled ->
            {
                model with
                    ShowDanger = not model.ShowDanger
            },
            Cmd.none
        | Exported ->
            match model.Table with
            // 牌桌都开不起来时没有牌谱可导（按钮那时也是灰的）。
            | Error _ -> model, Cmd.none
            | Ok table -> model, exportCmd (rosterOf model) (exportName model.SeedText) table
        | KyokuAdvanced ->
            {
                model with
                    Table = Result.map Table.nextKyoku model.Table
                    // 一局一定型（票 46）：开下一局时面板上改过的人格与模板在这里生效。
                    Pinned = None
                    Awaiting = None
            },
            Cmd.none
        | LlmSeatPicked seat ->
            {
                model with
                    LlmAt = seat
                    Agent = AgentStatus.Idle
            },
            save (fun () -> Store.writeSeat seat)
        // 不重开一桌：配桌是每推一手现推导的，换了从下一手起生效（与换模型坐席同一个做法）。
        | BotPicked kind -> { model with Bot = kind }, Cmd.none
        | LlmEdited(field, value) ->
            let config = LlmSeat.edit field value model.Llm
            { model with Llm = config }, save (fun () -> Store.writeSeatConfig config)
        | Answered(ticket, answer) ->
            match model.Awaiting, model.Table with
            | Some awaiting, Ok table when awaiting.Ticket = ticket ->
                let played, status = settle awaiting answer table

                {
                    model with
                        Table = Ok played
                        Awaiting = None
                        Agent = status
                }
                |> resume Cmd.none
            // 过期的回执（重开过一桌、开过下一局，或者票号对不上）：丢掉。
            | _ -> model, Cmd.none
