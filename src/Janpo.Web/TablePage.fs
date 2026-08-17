namespace Janpo.Web

open Elmish
open Fable.Core
open Feliz
open Feliz.UseElmish
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

/// 画一家牌时要的那点桌面上下文（票 44）。**四样东西总是一起出现**，
/// 收成一个记录是为了 `seatPanel` 不用收五个位置参数。
///
/// **它不是第二份牌局状态**（ADR-0002）：四个字段全是 `Table` 与 `BoardView` 里现有的那份，
/// 只在渲染那一瞬拼在一起。
type Seating = {
    /// 这一桌的规则集：方位与副露来源的座位算术都要它。
    Ruleset: Ruleset
    /// 观测者；上帝视角没有（那枚「视角」标签与方位文字读的就是它）。
    Viewer: Seat option
    /// 方位的**参照系**（`Board.anchor` 算好的：坐着看是观测者，上帝视角是起家）。
    Anchor: Seat
    /// 亲：那枚「亲」标签。
    Oya: Seat
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

/// 牌桌页面：MVU 三件套加视图。
[<RequireQualifiedAccess>]
module TablePage =

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
    let private canAdvance (model: TableModel) : bool =
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

    // ---- 视图：牌 ----

    /// 一张亮着的牌。**人类可读形式只在这里出现**（ADR-0001）；`data-pai` 上仍是 mjai 记法，
    /// 无头验收数「这一家的牌露没露出来」靠的就是它。
    /// `extras` 给河那边添摸切标记，其余处传空表。
    let private paiSpan (key: int) (extra: string) (extras: IReactProperty list) (pai: Tile) =
        let akadora = if Tile.isAkadora pai then " aka" else ""

        Html.span (
            [
                prop.key key
                prop.className $"tile{extra}{akadora}"
                prop.custom ("data-pai", Tile.toMjai pai)
            ]
            @ extras
            @ [ prop.text (Tile.toDisplay pai) ]
        )

    let private face (key: int) (extra: string) (pai: Tile) = paiSpan key extra [] pai

    /// 一张扣着的牌。**它没有 `data-pai`**——投影里压根没有那张牌，渲染层无从写起。
    let private back (key: int) =
        Html.span [ prop.key key; prop.className "tile back"; prop.text "背" ]

    let private faces (extra: string) (tiles: Tile list) =
        tiles |> List.mapi (fun index pai -> face index extra pai)

    // ---- 视图：一家 ----

    /// 暗牌摆成「手里的 + 刚摸进的那张单独摆在右边」。刚摸进那张**本来就在手牌里**
    /// （`RevealedSeat.Hand` 含它），这里只是把它拎出来摆开——摸切打的就是它。
    let private handTiles (hand: HandView) =
        match hand with
        | HandView.Concealed count -> [ for index in 1..count -> back index ]
        | HandView.Revealed(tiles, drawn) ->
            match drawn |> Option.bind (fun pai -> tiles |> List.tryFindIndex ((=) pai)) with
            | None -> faces "" tiles
            | Some index ->
                let held = List.take index tiles @ List.skip (index + 1) tiles
                faces "" held @ [ face (List.length held) " drawn" (List.item index tiles) ]

    /// 河。**手切与摸切画得不一样**（`tsumogiri` 那一位是公开信息），
    /// 摸切那几张画成虚线加淡色，`data-tsumogiri` 上也写着，无头验收读得到。
    let private kawaTiles (kawa: KawaEntry list) =
        kawa
        |> List.mapi (fun index entry ->
            let marks = [
                prop.custom ("data-tsumogiri", (if entry.Tsumogiri then "true" else "false"))
                prop.title (if entry.Tsumogiri then "摸切" else "手切")
            ]

            paiSpan index (if entry.Tsumogiri then " tsumogiri" else "") marks entry.Pai)

    /// 副露的来源怎么称呼：**相对副露方**的中文说法（CONTEXT.md 的 Shimocha / Toimen / Kamicha）。
    /// 措辞与 prompt 尾部那头同一套词（`web/src/agent/wording.ts` 的 `relative`），
    /// 形状照 `Threat.who`：座位数不是 4 时没有对家，那时按座位号说。
    let private nakiFrom (view: NakiView) : string option =
        match view.Relative, view.Target with
        | Some 1, _ -> Some "下家"
        | Some 2, _ -> Some "对家"
        | Some 3, _ -> Some "上家"
        | Some _, Some target -> Some $"座位 {Seat.index target}"
        | _, _ -> None

    /// 副露里的一张。**横放的那张是从他家那儿来的**（`NakiTileView.FromOther`）：
    /// 碰 / 吃 / 大明杠是被鸣的那张，加杠是当初碰来的那张，暗杠一张也没有。
    /// 记号挑**朝向**而不是描边或换色：虚线加淡色是摸切、45° 斜纹是牌背、红字是赤牌、
    /// 额外间距是刚摸那张——四条都占了，而横放又正是牌谱的标准画法。
    ///
    /// 加杠**加上去的那张**出自自家手里，不是鸣来的，所以**不横放**（真牌桌上它也是侧着的，
    /// 但那样一来「横放 = 从他家来的」这条记号就得重定义，票 38 占的那一维不动）；
    /// 它前面摆一枚「＋」（牌谱文字记法里的 `中中中＋中`），并且**叠在横放那张上**（见 `nakiSlot`）。
    /// `data-naki-taken` / `data-naki-added` 给无头闸门读（票 38）。
    let private nakiTile (takenTitle: string) (key: int) (tile: NakiTileView) : ReactElement list =
        match tile.Pai with
        | None -> [ back key ]
        | Some pai ->
            let marks = [
                if tile.FromOther then
                    prop.custom ("data-naki-taken", "true")
                    prop.title takenTitle
                if tile.Added then
                    prop.custom ("data-naki-added", "true")
                    prop.title "加杠加上去的那张（自家手里的第四张）"
            ]

            let extra =
                (if tile.FromOther then " taken" else "")
                + (if tile.Added then " added" else "")

            let plus = [
                if tile.Added then
                    Html.span [
                        prop.key $"add-{key}"
                        prop.className "naki-add"
                        prop.title "加杠加上去的那张（自家手里的第四张）"
                        prop.text "＋"
                    ]
            ]

            plus @ [ paiSpan key extra marks pai ]

    /// 副露里的一个**槽位**（票 51）：一格一张，只有加杠那一格是两张。
    ///
    /// DOM 里是**从下往上**（先写底下那张），样式拿 `column-reverse` 把它竖起来——
    /// 于是「当初碰来的那张」仍在底下且仍在它当初那一格，加上去的那张在上面。
    let private nakiSlot (takenTitle: string) (key: int) (slot: NakiSlot) =
        Html.span [
            prop.key key
            prop.className "naki-slot"
            prop.children (slot |> List.mapi (nakiTile takenTitle) |> List.concat)
        ]

    /// 一组副露。三件事要看得出来：**种类**、**被鸣的是哪一张**（横放）、**来自谁**。
    /// 杠仍看得出形态：四张一组，暗杠两端扣着（牌桌上就是这个摆法）且**没有来源**
    /// ——它不是鸣来的。
    ///
    /// **来源走位置编码**（票 51）：横放那张落在第几格就是它来自谁，牌桌上因此
    /// **不再写那行「来自X」**（票 38 当初否决位置编码的理由是「四家是竖排面板、方位无锚」，
    /// 票 44 把牌桌改成四家围坐之后那条理由就没了）。但那句中文**仍然写给读屏用户**
    /// （`sr-only`），`data-naki-from` 也原样留着——两道闸门读的就是它。
    let private nakiGroup (ruleset: Ruleset) (owner: Seat) (key: int) (naki: Naki) =
        let view = Board.nakiView ruleset owner naki
        let kind = NakiKind.toDisplay view.Kind
        let from = nakiFrom view

        let takenTitle =
            let who =
                from |> Option.map (fun each -> $"来自{each}") |> Option.defaultValue "从他家鸣来"

            if view.Kind = NakiKind.Kakan then
                $"当初碰来的那张（{who}）"
            else
                $"被鸣的那张（{who}）"

        // 来源那枚标签与它的 `data-`：暗杠一样都没有。绝对座位另写一份（`data-naki-from-seat`），
        // 闸门拿它与副露方的座位号对得上才算数——参照系要是漂到观测者那边，这一条当场就红。
        let sourceMarks =
            match from, view.Target with
            | Some who, Some target -> [
                prop.custom ("data-naki-from", who)
                prop.custom ("data-naki-from-seat", Seat.index target)
              ]
            | _, _ -> []

        // 来源那句话在牌桌上**看不见了**（主人裁定：位置为主、文字不留），但它仍然写在 DOM 里
        // 给**读屏用户**：位置读屏读不出来，删干净就是把来源对他们藏了。
        // `sr-only` 是那一半的画法；闸门两头都核：文字必须在、且必须看不见。
        let sourceLabel =
            match from, view.Target with
            | Some who, Some target -> [
                Html.span [
                    prop.key "from"
                    prop.className "naki-from sr-only"
                    prop.title $"这一组是从座位 {Seat.index target} 那儿鸣来的"
                    prop.text $"来自{who}"
                ]
              ]
            | _, _ -> []

        Html.span (
            [ prop.key key; prop.className "naki"; prop.custom ("data-naki", kind) ]
            @ sourceMarks
            @ [
                prop.children (
                    Html.span [ prop.key "kind"; prop.className "naki-kind"; prop.text kind ]
                    :: sourceLabel
                    @ (view.Slots |> List.mapi (nakiSlot takenTitle))
                )
            ]
        )

    /// 一排牌：小标题加那几张。**张数写在标题里**——「各家手牌数」那条验收看的就是它，
    /// 而他家的张数来自投影（`MaskedSeat.HandCount`），不是渲染层拿副露数推的。
    ///
    /// `marks` 是这一排的 `data-`（票 44）：标题里的那个数字是给人看的，它们是同一件事
    /// 给机器看的那一半——闸门拿两者与真画出来的张数三头对。
    let private tileRow
        (testId: string)
        (extra: string)
        (marks: IReactProperty list)
        (label: string)
        (tiles: ReactElement list)
        =
        Html.div (
            [ prop.key testId; prop.className $"tiles {extra}"; prop.testId testId ]
            @ marks
            @ [
                prop.children (
                    Html.span [ prop.key "label"; prop.className "row-label"; prop.text label ]
                    :: tiles
                )
            ]
        )

    /// 立直状态给机器看的那一半（票 44）。人看的是头上那枚「立直」标签
    /// （`RiichiState.toDisplay`）；两者对不上时闸门报错。
    /// **宣言与成立是两步**（`RiichiState` 那段注释），因此不能只写一个布尔：
    /// 供托里那一根只跟着**成立**的那几家走。
    let private riichiWire (state: RiichiState) : string =
        if RiichiState.isAccepted state then "accepted"
        elif RiichiState.isDeclared state then "declared"
        else "none"

    /// 一家的牌与标记。
    ///
    /// **三排牌从外往内摆**：副露、手牌、河——真牌桌上就是这个顺序（副露在身侧最外，
    /// 河摆在桌心）。四个方位各自把这三排**朝中心那一边翻**，翻法全在 `styles.css`（按
    /// `data-seat-position` 选），这一层只负责把顺序摆对。
    ///
    /// `data-seat-position` 是方位给机器看的那一半，**样式读的也是它**：
    /// 属性写着下家却画在左边这种事因此不存在（闸门仍然两样都核：
    /// 属性对不对、以及画出来的坐标对不对）。
    let private seatPanel (seating: Seating) (view: SeatView) =
        let index = Seat.index view.Seat
        let position = Board.position seating.Ruleset seating.Anchor view.Seat

        let handCount =
            match view.Hand with
            | HandView.Revealed(hand, _) -> List.length hand
            | HandView.Concealed count -> count

        let hidden =
            match view.Hand with
            | HandView.Revealed _ -> "false"
            | HandView.Concealed _ -> "true"

        let marks =
            [
                if view.Seat = seating.Oya then
                    "亲"
                if Some view.Seat = seating.Viewer then
                    "视角"
                if RiichiState.isActive view.Riichi then
                    RiichiState.toDisplay view.Riichi
                if view.Ippatsu then
                    "一发"
            ]
            |> List.map (fun mark -> Html.span [ prop.key mark; prop.className "mark"; prop.text mark ])

        // 方位的文字只在**坐着看**时写：上帝视角根本没有观测者，写「自家」会指向一个不存在的人。
        // 那一档的参照系改由牌桌中央那一句话声明（`tableCenter`）。
        let positionLabel = [
            if Option.isSome seating.Viewer then
                Html.span [
                    prop.key "position"
                    prop.className "seat-position"
                    prop.text (Position.toDisplay position)
                ]
        ]

        Html.section [
            prop.key index
            prop.className "seat"
            prop.testId $"seat-{index}"
            prop.custom ("data-seat", index)
            prop.custom ("data-seat-position", Position.toWire position)
            prop.custom ("data-riichi", riichiWire view.Riichi)
            prop.children [
                Html.div [
                    prop.className "seat-head"
                    prop.children (
                        [
                            Html.span [
                                prop.key "name"
                                prop.className "seat-name"
                                prop.text $"座位 {index}・{Kaze.toDisplay view.Jikaze}家"
                            ]
                        ]
                        @ positionLabel
                        @ [
                            Html.span [
                                prop.key "score"
                                prop.className "seat-score"
                                prop.testId $"seat-{index}-score"
                                prop.custom ("data-score", view.Score)
                                prop.text (string view.Score)
                            ]
                            Html.span [
                                prop.key "junme"
                                prop.className "seat-junme"
                                prop.testId $"seat-{index}-junme"
                                prop.custom ("data-junme", view.Junme)
                                prop.text $"第 {view.Junme} 巡"
                            ]
                        ]
                        @ marks
                    )
                ]
                tileRow
                    $"seat-{index}-naki"
                    "naki-row"
                    [ prop.custom ("data-naki-count", List.length view.Naki) ]
                    "副露"
                    (view.Naki |> List.mapi (nakiGroup seating.Ruleset view.Seat))
                tileRow
                    $"seat-{index}-hand"
                    "hand"
                    [
                        prop.custom ("data-hand-count", handCount)
                        // 他家的手牌是扣着的——**这不是渲染纪律而是投影的形状**（`HandView.Concealed`
                        // 里根本没有牌面）。写出来是让闸门能拿它与“画出来几张牌背”对得上。
                        prop.custom ("data-hand-hidden", hidden)
                    ]
                    $"手牌 {handCount}"
                    (handTiles view.Hand)
                tileRow
                    $"seat-{index}-kawa"
                    "kawa"
                    [ prop.custom ("data-kawa-count", List.length view.Kawa) ]
                    $"河 {List.length view.Kawa}"
                    (kawaTiles view.Kawa)
            ]
        ]

    // ---- 视图：场况与结算 ----

    /// 场况里的一项。`marks` 是它给机器看的那一半（票 44），挂在带 testId 的那个元素上：
    /// 人读的是「东1局 2 本场」这句中文，闸门读的是 `data-honba`，两者对不上就报错。
    let private field (marks: IReactProperty list) (testId: string) (label: string) (value: string) =
        Html.span [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.span ([ prop.className "values"; prop.testId testId ] @ marks @ [ prop.text value ])
            ]
        ]

    /// 一排牌，带标题与钩子。宝牌与里宝牌指示牌都是它。
    let private tileField (testId: string) (label: string) (tiles: Tile list) =
        Html.span [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.span [
                    prop.className "tiles"
                    prop.testId testId
                    prop.custom ("data-tile-count", List.length tiles)
                    prop.children (faces "" tiles)
                ]
            ]
        ]

    /// 牌桌中央（票 44）：场况、供托与立直棒、剩余摸牌、宝牌指示牌——真牌桌上它们就摆在中间，
    /// 四家围着它坐。里宝牌只有上帝视角有。
    ///
    /// **方位的参照系写在这里**（M1 传下来的第六条：相对方位必须显式声明参照系）：
    /// 牌桌上写着「下家」的那家到底是谁的下家，读者不必自己猜。
    let private tableCenter (board: BoardView) =
        // 立直棒是「供托 N 根」那个数字的实物画法，一根都没时**整个字段不画**（同下面的里宝牌）：
        // 否则只剩一枚「立直棒」标签后面空着，看着像掉了东西（票 32 扫同类隐形时收的）。
        let bou =
            if board.Kyotaku = 0 then
                []
            else
                [
                    Html.span [
                        prop.key "bou"
                        prop.className "field"
                        prop.children [
                            Html.span [ prop.className "label"; prop.text "立直棒" ]
                            Html.span [
                                prop.className "bou-row"
                                prop.testId "table-bou"
                                prop.children [
                                    for index in 1 .. board.Kyotaku ->
                                        Html.span [ prop.key index; prop.className "bou" ]
                                ]
                            ]
                        ]
                    ]
                ]

        // 里宝牌只有上帝视角有（坐着看时投影里就是空表）。
        let ura =
            if List.isEmpty board.UraMarkers then
                []
            else
                [ tileField "table-uradora" "里宝牌指示牌" board.UraMarkers ]

        // 参照系那一句。`data-anchor` 是它给机器看的那一半：闸门拿它与四家真画在哪个格子里对。
        let anchor = Board.anchor board

        // 副露那一行的参照系（票 51）。它与上面那句不是同一个参照系，因此必须各说各的：
        // 牌桌布局以**看牌桌的那个人**为准，副露里的左中右以**副露方自己**为准。
        // M1 传下来的第六条（相对方位必须显式声明参照系）在这里就是这一句——
        // 牌桌上不再写「来自X」之后，读者靠它才知道横放那张的位置该怎么读。
        let nakiLegend = "副露：横放那张的位置就是来源，按副露方自己的左右算——最左＝上家、中间＝对家、最右＝下家（暗杠无源）"

        let frame =
            match board.Viewer with
            | Some seat -> $"坐在座位 {Seat.index seat}：自家在下、下家在右、对家在上、上家在左"
            | None -> $"上帝视角：以起家（座位 {Seat.index anchor}）为下方"

        Html.div [
            prop.className "table-center"
            prop.testId "table-center"
            prop.custom ("data-anchor", Seat.index anchor)
            prop.children (
                [
                    field
                        [
                            prop.custom ("data-bakaze", Kaze.toDisplay board.Bakaze)
                            prop.custom ("data-kyoku", board.Kyoku)
                            prop.custom ("data-honba", board.Honba)
                        ]
                        "table-kyoku"
                        "场况"
                        $"{Kaze.toDisplay board.Bakaze}{board.Kyoku}局 {board.Honba} 本场"
                    field [ prop.custom ("data-kyotaku", board.Kyotaku) ] "table-kyotaku" "供托" $"{board.Kyotaku} 根"
                    field
                        [ prop.custom ("data-wall", board.WallRemaining) ]
                        "table-wall"
                        "剩余摸牌"
                        $"{board.WallRemaining} 张"
                    tileField "table-dora" "宝牌指示牌" board.DoraMarkers
                ]
                @ ura
                @ bou
                @ [
                    Html.p [
                        prop.key "anchor"
                        prop.className "table-anchor"
                        prop.testId "table-anchor"
                        prop.text frame
                    ]
                    Html.p [
                        prop.key "naki-legend"
                        prop.className "table-anchor"
                        prop.testId "table-naki-legend"
                        prop.text nakiLegend
                    ]
                ]
            )
        ]

    /// 点数授受一行：按座位升序的增减。
    let private deltas (values: int list) =
        values
        |> Seat.indexed
        |> List.map (fun (seat, delta) ->
            let sign = if delta > 0 then "+" else ""
            $"座位 {Seat.index seat} {sign}{delta}")
        |> String.concat "　"

    let private horaLines (hora: HoraView) =
        let yaku =
            hora.Yaku
            |> List.map (fun (yaku, value) ->
                match value with
                | YakuValue.Han han -> $"{Yaku.toDisplay yaku} {han} 番"
                | YakuValue.Yakuman multiplier -> $"{Yaku.toDisplay yaku} 役满×{multiplier}")

        let dora = [
            if hora.Dora > 0 then
                $"宝牌 {hora.Dora} 番"
            if hora.Uradora > 0 then
                $"里宝牌 {hora.Uradora} 番"
            if hora.Akadora > 0 then
                $"红宝牌 {hora.Akadora} 番"
        ]

        let limit =
            match Limit.toDisplay hora.Limit with
            | "" -> ""
            | level -> $"（{level}）"

        [
            if hora.Actor = hora.Target then
                $"座位 {Seat.index hora.Actor} 自摸 {Tile.toDisplay hora.Pai}"
            else
                $"座位 {Seat.index hora.Actor} 荣和 座位 {Seat.index hora.Target} 打出的 {Tile.toDisplay hora.Pai}"
            "役：" + (yaku @ dora |> String.concat "、")
            $"{hora.Fu} 符 {hora.Fan} 番 {hora.HoraPoints} 点{limit}"
            "点数授受：" + deltas hora.Deltas
        ]

    let private ryuukyokuLines (ryuukyoku: Ryuukyoku) =
        let tenpai =
            ryuukyoku.Tenpais
            |> Seat.indexed
            |> List.filter snd
            |> List.map (fun (seat, _) -> $"座位 {Seat.index seat}")

        // 听牌家一家都没有也要写出来（途中流局就是这样），空白一行分不出「没人听」与「没画」。
        let tenpaiText =
            if List.isEmpty tenpai then
                "无"
            else
                String.concat "、" tenpai

        [
            RyuukyokuReason.toDisplay ryuukyoku.Reason
            "听牌：" + tenpaiText
            "点数授受：" + deltas ryuukyoku.Deltas
        ]

    let private settlementPanel (settlement: Settlement) =
        let lines =
            match settlement.Outcome with
            | Outcome.Hora horas -> horas |> List.collect horaLines
            | Outcome.Ryuukyoku ryuukyoku -> ryuukyokuLines ryuukyoku

        let title =
            match settlement.Outcome with
            | Outcome.Hora _ -> "和了"
            | Outcome.Ryuukyoku _ -> "流局"

        // 这一局之后往哪走。**末局不该邀人「进下一局」**：局数序列走完就终局，
        // 连庄也不延长，因此终局那一行压过连庄与否（票 39）。
        // `data-progress` 给无头验收读，不必去分析中文。
        let progress, text =
            match settlement.Ended, settlement.Renchan with
            | true, _ -> "ended", "终局：这一场到此打完，下面是终局精算"
            | false, true -> "renchan", "亲连庄"
            | false, false -> "next", "亲流局，进下一局"

        Html.section [
            prop.key "settlement"
            prop.className "settlement"
            prop.testId "table-settlement"
            prop.children (
                [ Html.h3 [ prop.key "title"; prop.text title ] ]
                @ (lines |> List.mapi (fun index line -> Html.p [ prop.key index; prop.text line ]))
                @ [
                    Html.p [
                        prop.key "renchan"
                        prop.testId "table-renchan"
                        prop.custom ("data-progress", progress)
                        prop.text text
                    ]
                ]
            )
        ]

    /// 终局精算。**座位卡读的是同一份数**（`Board.ofTable` 在终局那一刻换成精算后的点数），
    /// 因此这一屏上只有一种说法。供托那几根去了哪要写出来：场况行已经归零、
    /// 桌上那根立直棒也收走了，不说一句就成了「凭空不见」。
    let private resultPanel (final: FinalView) =
        let kyotaku =
            if final.Kyotaku = 0 then
                []
            else
                [
                    Html.p [
                        prop.key "kyotaku"
                        prop.testId "table-result-kyotaku"
                        prop.text $"场上剩下的供托 {final.Kyotaku} 根（{final.KyotakuScore} 点）已归 1 位"
                    ]
                ]

        Html.section [
            prop.key "result"
            prop.className "settlement"
            prop.testId "table-result"
            prop.children (
                [
                    Html.h3 [ prop.key "title"; prop.text "终局精算" ]
                    Html.p [
                        prop.key "ranking"
                        prop.testId "table-result-ranking"
                        prop.text (GameResult.toDisplay final.Result)
                    ]
                ]
                @ kyotaku
            )
        ]

    // ---- 视图：控制 ----

    let private button
        (testId: string)
        (disabled: bool)
        (label: string)
        (message: TableMsg)
        (dispatch: TableMsg -> unit)
        =
        Html.button [
            prop.key testId
            prop.testId testId
            prop.disabled disabled
            prop.onClick (fun _ -> dispatch message)
            prop.text label
        ]

    let private picker
        (testId: string)
        (selected: bool)
        (label: string)
        (message: TableMsg)
        (dispatch: TableMsg -> unit)
        =
        Html.button [
            prop.key testId
            prop.testId testId
            prop.className (if selected then "picked" else "")
            prop.onClick (fun _ -> dispatch message)
            prop.text label
        ]

    let private controls (model: TableModel) (dispatch: TableMsg -> unit) =
        let running = canAdvance model

        let ended =
            match model.Table with
            | Ok table -> Table.isKyokuEnded table && (Table.result table |> Option.isNone)
            | Error _ -> false

        let speeds =
            Speed.all
            |> List.map (fun speed ->
                picker
                    $"table-speed-{Speed.toDisplay speed}"
                    (model.Playback.Speed = speed)
                    (Speed.toDisplay speed)
                    (SpeedPicked speed)
                    dispatch)

        Html.div [
            prop.className "controls"
            prop.children (
                [
                    button
                        "table-play"
                        (not running)
                        (if model.Playback.Playing then "暂停" else "播放")
                        PlayToggled
                        dispatch
                    button "table-step" (not running) "单步" Advanced dispatch
                    button "table-next" (not ended) "下一局" KyokuAdvanced dispatch
                    // 牌谱随时导得出来，不必等终局：打到一半的事件流同样 fold 得回去。
                    button "table-export" (Result.isError model.Table) "导出牌谱" Exported dispatch
                    Html.span [ prop.key "speed-label"; prop.className "label"; prop.text "倍速" ]
                ]
                @ speeds
            )
        ]

    let private viewpoints (model: TableModel) (dispatch: TableMsg -> unit) =
        let seats =
            Seat.all model.Ruleset
            |> List.map (fun seat ->
                picker
                    $"table-view-{Seat.index seat}"
                    (model.Viewpoint = Viewpoint.Seated seat)
                    $"座位 {Seat.index seat}"
                    (ViewpointPicked(Viewpoint.Seated seat))
                    dispatch)

        Html.div [
            prop.className "controls"
            prop.children (
                [ Html.span [ prop.key "view-label"; prop.className "label"; prop.text "视角" ] ]
                @ seats
                @ [
                    picker
                        "table-view-god"
                        (model.Viewpoint = Viewpoint.God)
                        "上帝视角"
                        (ViewpointPicked Viewpoint.God)
                        dispatch
                    // 危险度（票 25）：围观者想看就拨开，**默认关**。
                    picker "table-danger" model.ShowDanger "危险度" DangerToggled dispatch
                    Html.span [ prop.key "seed-label"; prop.className "label"; prop.text "种子" ]
                    Html.input [
                        prop.key "seed-input"
                        prop.testId "table-seed"
                        prop.value model.SeedText
                        prop.onChange (SeedEdited >> dispatch)
                    ]
                    button "table-restart" false "重开" Restarted dispatch
                ]
            )
        ]

    // ---- 视图：危险度（票 25） ----

    /// 这一手能把谁的危险度摆出来：**只摆手牌本来就看得见的那家**。
    ///
    /// 危险度的候选牌就是那家的手牌，因此坐在座位上看时只显示自己那一手（显示别家的
    /// 等于把他的暗牌摊开）；上帝视角本来就全亮着，正在被问的那家都显示得了。
    let private dangerSeats (viewer: Seat option) (state: GameState) : Seat list =
        let asked = GameState.legalActions state |> List.map (fun choice -> choice.Seat)

        match viewer with
        | Some seat -> asked |> List.filter (fun other -> other = seat)
        | None -> asked

    /// 一家的危险度排序。**一个判据也不在这里算**：档位、名次与理由全是引擎的
    /// `Danger` 算好的，这里只排行（与 prompt 那一节同一份数）。
    let private dangerPanel (seat: Seat) (state: GameState) =
        let scaffold =
            DecisionPackage.forSeat seat state |> Option.bind DecisionPackage.scaffold

        match scaffold with
        | None -> []
        | Some scaffold ->
            let ranked =
                scaffold.Dahai
                |> List.choose (fun trial -> trial.Danger)
                |> List.sortBy (fun danger -> danger.Rank)

            if List.isEmpty ranked then
                []
            else
                let threats = scaffold.Threats |> List.map Threat.toDisplay |> String.concat "、"

                [
                    Html.section [
                        prop.key $"danger-{Seat.index seat}"
                        prop.className "settlement"
                        prop.testId $"table-danger-{Seat.index seat}"
                        prop.children [
                            Html.h3 $"座位 {Seat.index seat} 的危险度（有威胁的家：{threats}）"
                            Html.p [
                                prop.key "note"
                                prop.className "intro"
                                prop.text "现物 / 筋 / 壁 / 宝牌周边四条规则算出来的启发式，不是概率；排在前面的更安全，同级并列。"
                            ]
                            Html.div [
                                prop.key "ranking"
                                prop.children [
                                    for danger in ranked ->
                                        Html.p [
                                            prop.key (Tile.toMjai danger.Pai)
                                            prop.text $"第{danger.Rank}位 {Danger.toDisplay danger}"
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]

    /// 牌桌上的危险度：**默认关**，没人立直也没人副露时开了也没东西看
    /// （那时引擎本来就不给排序）。
    let private dangerPanels (model: TableModel) (table: Table) (viewer: Seat option) =
        if model.ShowDanger then
            dangerSeats viewer table.State
            |> List.collect (fun seat -> dangerPanel seat table.State)
        else
            []

    // ---- 视图：配置面板 ----

    /// 一个文本输入框。`kind` 是 `password` 时人看不见自己填的 key。
    let private textField
        (testId: string)
        (kind: string)
        (label: string)
        (which: LlmField)
        (model: TableModel)
        (dispatch: TableMsg -> unit)
        =
        Html.label [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.input [
                    prop.testId testId
                    prop.type' kind
                    prop.value (LlmSeat.field which model.Llm)
                    prop.onChange (fun (value: string) -> dispatch (LlmEdited(which, value)))
                ]
            ]
        ]

    /// 一格多行文本（票 31）：人格与模板都是整段文字，单行输入框里根本读不下来。
    /// **措辞与人格就是从这两格注入的**：改它们不用改代码、不用重编。
    let private areaField
        (testId: string)
        (label: string)
        (hint: string)
        (which: LlmField)
        (model: TableModel)
        (dispatch: TableMsg -> unit)
        =
        Html.label [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.textarea [
                    prop.testId testId
                    prop.rows 3
                    prop.placeholder hint
                    prop.value (LlmSeat.field which model.Llm)
                    prop.onChange (fun (value: string) -> dispatch (LlmEdited(which, value)))
                ]
            ]
        ]

    /// 一个下拉框。`options` 是（值, 显示, 选不选得了）三元组。
    ///
    /// **选不了的选项仍然列出来**：脚手架的工具搜索档是 M3 的事，列着它并灰掉
    /// 比藏起来诚实——人看得见「还有一档，还没做」。
    let private selectField
        (testId: string)
        (label: string)
        (which: LlmField)
        (options: (string * string * bool) list)
        (model: TableModel)
        (dispatch: TableMsg -> unit)
        =
        Html.label [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.select [
                    prop.testId testId
                    prop.value (LlmSeat.field which model.Llm)
                    prop.onChange (fun (value: string) -> dispatch (LlmEdited(which, value)))
                    prop.children [
                        for value, display, enabled in options ->
                            Html.option [
                                prop.key value
                                prop.value value
                                prop.text display
                                prop.disabled (not enabled)
                            ]
                    ]
                ]
            ]
        ]

    /// 人格与模板那两格下面那一行（票 46 的 31-D 与「一局内不变」）。**两件事是一件事的两面**：
    /// 渲染版号说「发出去的是哪一份」，后半句说「你刚才改的那一份什么时候生效」。
    ///
    /// 版号取自**最近一条决策记录**，不在这一层重算：它是 `模板 id@模板哈希.渲染器摘要`
    /// （票 43：后一截让「改了排版的代码」也看得出来），两截都在 Agent 层算
    /// （`web/src/agent/render-version.ts`），F# 这边再算一份就是第二份权威。
    /// 因此这行说的是**真发出去过的那一份**，不是推测。`data-*` 给无头验收读。
    let private renderingLine (model: TableModel) =
        // 空串也当「还没有」：没填 key 那几手连 prompt 都没渲染过（记录仍然留一条，
        // 内容就是那句原因），印一个空版本号比不印更难读。
        let version =
            match model.Table with
            | Ok table ->
                table.Decisions
                |> List.tryLast
                |> Option.map (fun record -> record.RenderVersion)
                |> Option.filter (fun version -> version <> "")
            | Error _ -> None

        let said =
            match version with
            | Some version -> $"最近一手的渲染版本：{version}"
            | None -> "渲染版本：这一桌还没发出去过一次问话"

        let pending = renderingPending model

        let note = if pending then "　人格 / 模板改过了：本局仍用定型那一版，下一局生效。" else ""

        Html.p [
            prop.key "rendering"
            prop.className (if pending then "rendering pending" else "rendering")
            prop.testId "table-render-version"
            prop.custom ("data-render-version", Option.defaultValue "" version)
            prop.custom ("data-rendering-pending", (if pending then "true" else "false"))
            prop.text (said + note)
        ]

    /// 配桌：哪个座位交给 LLM，以及那个座位的 provider / 模型 / key / 超时 / 思考预算。
    ///
    /// **key 只进 localStorage**（`Store`），不外发到本平台——本平台根本没有后端。
    /// **不提供订阅制登录**：pi-ai 的 OAuth 流程是 Node-only（票 18），浏览器里只有 API key
    /// 这一条路；Bedrock 同理不在 provider 列表里。
    ///
    /// **baseUrl 那一格只在选了自定义端点时出现**（票 30）：官方八家根本不看它，
    /// 摆在那里只会让人以为能把 DeepSeek 改道。
    let private llmPanel (model: TableModel) (dispatch: TableMsg -> unit) =
        let seats =
            picker "table-llm-none" (Option.isNone model.LlmAt) "无" (LlmSeatPicked None) dispatch
            :: [
                for seat in Seat.all model.Ruleset ->
                    picker
                        $"table-llm-{Seat.index seat}"
                        (model.LlmAt = Some seat)
                        $"座位 {Seat.index seat}"
                        (LlmSeatPicked(Some seat))
                        dispatch
            ]

        // 剩下那几家由哪种自带 bot 坐（票 42）。**均匀随机是默认**：它是对拍与闸门的基准；
        // 有主见的那个能和就和、听牌就立直，立直与供托那几条路径靠它才真的走得到。
        let bots =
            Bot.all
            |> List.map (fun kind ->
                picker $"table-bot-{Bot.toWire kind}" (model.Bot = kind) (Bot.toDisplay kind) (BotPicked kind) dispatch)

        Html.section [
            prop.className "llm-panel"
            prop.testId "table-llm-panel"
            prop.children [
                Html.div [
                    prop.key "seat"
                    prop.className "controls"
                    prop.children (
                        (Html.span [ prop.key "llm-label"; prop.className "label"; prop.text "模型坐席" ]
                         :: seats)
                        @ (Html.span [ prop.key "bot-label"; prop.className "label"; prop.text "其余座位" ]
                           :: bots)
                    )
                ]
                Html.div [
                    prop.key "config"
                    prop.className "controls"
                    prop.children [
                        selectField
                            "table-llm-provider"
                            "provider"
                            LlmField.Provider
                            (LlmSeat.providers
                             |> List.map (fun name -> name, LlmSeat.providerToDisplay name, true))
                            model
                            dispatch
                        // 模型名一直是自由文本：本地模型叫 `qwen3:8b` 的叫 `gpt-oss-20b@q4` 的都有，
                        // 下拉框只会挡路。
                        textField "table-llm-model" "text" "模型" LlmField.Model model dispatch
                        if LlmSeat.isCustom model.Llm then
                            textField "table-llm-base-url" "text" "baseUrl" LlmField.BaseUrl model dispatch
                        textField "table-llm-key" "password" "API key" LlmField.ApiKey model dispatch
                        textField "table-llm-timeout" "number" "超时 (ms)" LlmField.TimeoutMs model dispatch
                        selectField
                            "table-llm-thinking"
                            "思考预算"
                            LlmField.Thinking
                            (Thinking.all
                             |> List.map (fun level -> Thinking.toWire level, Thinking.toDisplay level, true))
                            model
                            dispatch
                        // 脚手架档位：**它是实验变量**，主持人在座位上现拨，不用改代码。
                        // 工具搜索档是 M3 的，灰着；它真被选上也不会坏事（prompt 与兜底都退回 Bare）。
                        selectField
                            "table-llm-tier"
                            "脚手架"
                            LlmField.Tier
                            (ScaffoldTier.all
                             |> List.map (fun tier ->
                                 ScaffoldTier.toWire tier, ScaffoldTier.toDisplay tier, tier <> ScaffoldTier.ToolSearch))
                            model
                            dispatch
                    ]
                ]
                // 人格与模板（票 31）：**与脚手架档位是两个维度**，因此另起一行。
                // 两格都进可缓存的前缀，因此改它们 = 废掉那一局的缓存（渲染版本号会跟着变）。
                Html.div [
                    prop.key "prompt"
                    prop.className "controls"
                    prop.children [
                        areaField
                            "table-llm-persona"
                            "人格（一局内不变）"
                            "留空就没有人格。例：你是一位以防守见长的雀士，宁可少和一把，也不点炮。"
                            LlmField.Persona
                            model
                            dispatch
                        areaField
                            "table-llm-template"
                            "prompt 模板（一局内不变）"
                            "留空就用默认模板。一段 JSON，给几项换几项：{\"id\":\"我的模板\",\"labels\":{\"history\":\"【战况回放】\"}}"
                            LlmField.Template
                            model
                            dispatch
                    ]
                ]
                renderingLine model
                Html.p [
                    prop.key "note"
                    prop.className "intro"
                    prop.text
                        "key 只存在这台浏览器的 localStorage 里，请求由浏览器直发 provider，不经本平台（它没有后端）。订阅制的 OAuth 登录在浏览器里用不了，只能填 API key。脚手架换成信息辅助后，prompt 里会多一节引擎算好的向听数、有效牌与逐张试打的进退向。人格与模板是另一个维度：它们只换措辞，不换给不给那几个算好的数；两格都在可缓存的前缀里，因此一局之内不会变——开局后再改照样存住，但要下一局才发得出去（上面那行会直说）。模型超时、报错或给不出合法动作时，重试两次仍不行就兜底代打（裸奔档摸切，信息辅助档打一张不退向听的）；认证失败这类再问一遍还是一样的错不重试，直接兜底。对局不会卡住。"
                ]
                // 自定义端点那段话只在选中它时出现：两个坑（CORS、mixed content）说清楚要一整段，
                // 而它们与官方八家无关。完整配法在 `docs/host/custom-endpoint.md`。
                if LlmSeat.isCustom model.Llm then
                    Html.p [
                        prop.key "custom-note"
                        prop.className "intro"
                        prop.testId "table-llm-custom-note"
                        prop.text
                            "自定义端点：baseUrl 填到含 /v1 那一层（Ollama 是 http://localhost:11434/v1，LM Studio 是 http://localhost:1234/v1），本地端点通常不用填 key，模型名照端点里的实名填。两个坑要先踩平：端点默认不放行浏览器跨域（Ollama 设 OLLAMA_ORIGINS，LM Studio 在设置里开 CORS），且 https 页面调 http 端点会被浏览器拦掉——配法见 docs/host/custom-endpoint.md。接不上时上面那行会直说「连不上自定义端点」，那不是模型不肯选。"
                    ]
            ]
        ]

    // ---- 视图：Agent 层的状态 ----

    /// 刚落定的那一手是不是兜底代打的（`data-*` 只能是字符串）。
    let private fallenBack (latest: Turn option) : string =
        match latest |> Option.bind (fun turn -> turn.Fallback) with
        | Some _ -> "true"
        | None -> "false"

    /// Agent 层此刻在干什么，以及这一桌兜底代打了几手。
    ///
    /// **断电演习看的就是这一行**：key 配坏了的时候对局照样打得完，但这里会一直红着
    /// 说模型怎么了。`data-agent` 给无头验收读。
    ///
    /// 没有模型坐席时那一句要说清楚**是哪一种自带 bot**（票 42 之后就不只一种了）：
    /// 杆子拨到「有主见」时牌桌上会出立直与供托，而行里还写着「四家都是随机选手」就是句错话。
    /// 措辞只有一份真源（`Bot.toDisplay`，与面板上那个控件同字）。
    let private agentLine (model: TableModel) (table: Table) =
        let state, text =
            match model.Agent with
            | AgentStatus.Idle when Option.isNone model.LlmAt -> "idle", $"四家都是{Bot.toDisplay model.Bot}的选手"
            | AgentStatus.Idle -> "idle", "模型座位已就位，还没轮到它"
            | AgentStatus.Asking seat -> "asking", $"正在等座位 {Seat.index seat} 的模型回话……"
            | AgentStatus.Spoke(seat, reason, latency) ->
                let said =
                    match reason with
                    | Some reason -> $"：{reason}"
                    | None -> ""

                "spoke", $"座位 {Seat.index seat} 的模型选完了（{latency} ms）{said}"
            | AgentStatus.Troubled(seat, reason) -> "troubled", $"座位 {Seat.index seat} 兜底代打：{reason}"

        let fallbacks = Table.fallbacks table

        let tally = if fallbacks = 0 then "" else $"　这一桌已兜底 {fallbacks} 手"

        Html.p [
            prop.key "agent"
            prop.className (if state = "troubled" then "agent error" else "agent")
            prop.testId "table-agent"
            prop.custom ("data-agent", state)
            // 坐着的是哪种自带 bot（票 44）：上面那句中文给人看，这一条给闸门看。
            prop.custom ("data-bot", Bot.toWire model.Bot)
            prop.custom ("data-fallbacks", string fallbacks)
            prop.text (text + tally)
        ]

    /// 这一桌的 token 账单（票 29b）。**「缓存真的命中了」在页面上看得见**：
    /// prompt 翻成「固定 preamble + append-only 历史 + 尾部现况」之后，
    /// 同一局里越往后打，命中的那一段越长。
    ///
    /// 一个 token 都还没花掉时不占位（四家随机选手的那一桌永远不长出这一行）。
    /// `data-*` 那几项给无头验收读。
    let private usageLine (table: Table) =
        let usage = Table.usage table

        if Usage.promptTokens usage = 0 then
            []
        else
            [
                Html.p [
                    prop.key "usage"
                    prop.className "agent"
                    prop.testId "table-usage"
                    prop.custom ("data-prompt-tokens", string (Usage.promptTokens usage))
                    prop.custom ("data-cache-read", string usage.CacheRead)
                    prop.custom ("data-cache-write", string usage.CacheWrite)
                    prop.custom ("data-cache-percent", string (Usage.cacheHitPercent usage))
                    prop.text ("这一桌累计：" + Usage.toDisplay usage)
                ]
            ]

    // ---- 视图：整页 ----

    let private tableBody (model: TableModel) (table: Table) =
        match Board.ofTable model.Viewpoint table with
        | None -> Html.p [ prop.className "error"; prop.text "这个视角没有牌桌" ]
        | Some board ->
            // 兜底代打的那一手要看得出来（票 23）：不许静默替换。
            // `data-fallback` 给无头验收读（断电演习数的就是它）。
            let latest =
                match table.Latest with
                | None -> "还没走一手"
                | Some turn ->
                    let who = Action.actor turn.Action |> Seat.index

                    let mark =
                        match turn.Fallback with
                        | Some reason -> $"（兜底：{reason}）"
                        | None -> ""

                    $"上一手：座位 {who} {Action.toDisplay turn.Action}{mark}"

            let fault =
                table.Fault
                |> Option.toList
                |> List.map (fun message ->
                    Html.p [
                        prop.key "fault"
                        prop.className "error"
                        prop.testId "table-fault"
                        prop.text message
                    ])

            let settlement = Board.settlement table |> Option.toList |> List.map settlementPanel

            let result = Board.final table |> Option.toList |> List.map resultPanel

            let danger = dangerPanels model table board.Viewer

            let seating: Seating = {
                Ruleset = GameState.ruleset table.State
                Viewer = board.Viewer
                Anchor = Board.anchor board
                Oya = (GameState.context table.State).Oya
            }

            Html.div [
                prop.testId "table-board"
                prop.children (
                    [
                        Html.p [
                            prop.key "latest"
                            prop.className "latest"
                            prop.testId "table-latest"
                            prop.custom ("data-fallback", fallenBack table.Latest)
                            prop.text latest
                        ]
                        agentLine model table
                    ]
                    @ usageLine table
                    @ [
                        // 真牌桌（票 44）：四家围着中央坐。**DOM 仍然按座位升序**（`seat-N` 那几个
                        // 钩子与既有闸门因此稳定），画到哪个方位由 `data-seat-position` 定——
                        // 它跟着观测视角转，而不是跟着座位号走。
                        Html.div [
                            prop.key "seats"
                            prop.className "seats-board"
                            prop.testId "table-seats"
                            prop.children (
                                [ for view in board.Seats -> seatPanel seating view ] @ [ tableCenter board ]
                            )
                        ]
                    ]
                    @ fault
                    @ danger
                    @ settlement
                    @ result
                )
            ]

    [<ReactComponent>]
    let Page () =
        let model, dispatch = React.useElmish (init, update, [||])

        Html.div [
            prop.className "page table-page"
            prop.children [
                // h1 抄的是 `web/index.html` 那个 `<title>`（票 33 定的稿）：
                // 标签页上与页面上不该各说各的。改品牌语先改那一行，再抄过来。
                Html.h1 "janpo —— 浏览器里的 LLM 日麻竞技场"
                Html.p [
                    prop.className "intro"
                    prop.text
                        "默认四家自带选手（下面可切均匀随机 / 有主见）；挑一个座位交给模型，按「播放」看它一手一手打。他家的手牌看不到牌面——模型看到的和你一样多，别人的暗牌在页面拿到的数据里根本不存在；想复盘就按一下切到上帝视角。虚线的牌是摸切。"
                ]
                controls model dispatch
                viewpoints model dispatch
                llmPanel model dispatch
                match model.Table with
                | Error message -> Html.p [ prop.className "error"; prop.testId "table-error"; prop.text message ]
                | Ok table -> tableBody model table
            ]
        ]
