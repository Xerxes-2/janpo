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

/// **Live**：主持人自己开的那一桌（`?table=1`）——种子、配桌与 Agent 层全在这里。
///
/// **没有第二份牌局状态**（ADR-0002）：牌局在 `Table` 里，而 `Table` 里的局面是引擎的那一份；
/// 这里只多种子输入框的文本、配桌与 Agent 层的那几样。
///
/// 播放控制、视角与危险度**不在这里**（票 71）：它们与「牌从哪来」无关，
/// 两种来源共用同一份实现，因此留在 `TableModel` 上。
type LiveTable = {
    /// 输入框里的文本。**没解析过**——解析在「重开」那一步做，因此打字不会重开一桌。
    SeedText: string
    /// 配桌上拨到的那三项规则（票 72）。**它不是这一桌正在按的那一份**
    /// （那是 `TableModel.Ruleset`）：与种子同一条路——拨完要按「重开」才开出新的一桌。
    /// **不许半场换规则**：同一份牌谱前后按两套规则算的话，回放就重现不了它。
    Rules: RulesetDraft
    /// 牌桌；开不了局时是中文错误文案。
    Table: Result<Table, string>
    /// 哪个座位交给 LLM；None = 四家都是自带 bot。
    LlmAt: Seat option
    /// 不是 LLM 的那几家由哪种自带 bot 坐（票 42）。**默认均匀随机**：
    /// `?table=1` 上那几道闸门（牌桌八项、牌谱导出、副露来源）量的都是它跑出来的那几局。
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

/// **回放**（CONTEXT.md 的 `Replay`）：一份牌谱 fold 出来的逐帧牌桌，加「播到第几帧」。
///
/// **三段各是一个 case**：页面上要说得出「在拉」「拉不动（原因）」「在播」，
/// 混成一个带 option 的记录就分不清了（判据 12：拒绝理由各有各的 case）。
///
/// **逐帧一次 fold 好**而不是边播边算：回放就是对前缀做 fold（ADR-0002），
/// 帧是值之后「播一手」就是 `cursor + 1`（纯的、测得了的），
/// 「从头再放」就是 `cursor := 0`。
[<RequireQualifiedAccess>]
type ReplayTable =
    /// 还在拉那份牌谱（首页刚打开那一瞬）。
    | Loading
    /// 拉不动 / 读不动 / 回放不动：一句中文原因。**不许白屏**。
    | Failed of reason: string
    /// fold 好了：逐帧的牌桌与播到第几帧。
    | Ready of frames: Table list * cursor: int

/// **这一桌的牌从哪来**（票 71）。地址上的两页就是这两个 case（`Route.Landing`）。
///
/// **播放控制、视角、危险度与牌桌和结算的渲染都在联合之外**：两种来源共用一份实现。
/// 写成两个 Model 各带一套播放与视角就是做错了——那两套会各自漂。
[<RequireQualifiedAccess>]
type Source =
    /// `?table=1`：主持人在自己浏览器里推的那一桌（ADR-0003：只有他推得动对局）。
    | Live of live: LiveTable
    /// `/`：随应用分发的 Demo Paifu 回放（ADR-0003：访客的第一眼）。
    | Replay of replay: ReplayTable

/// 时间轴上的一个局边界（票 75）：这一局的开局落在第几帧，以及轴上写着的那几个字。
///
/// **它不是第二份局面**：两格都从那一帧的牌桌上读出来（`GameState.context`），
/// 局边界的落点因此只有这一处，跳过去就是把游标挪到 `Frame`。
type KyokuMark = {
    /// 这一局开局那一帧的帧号。
    Frame: int
    /// 按钮上那几个字：场风 + 局数，连庄时带本场（同一个「东1」会出现好几次）。
    Label: string
}

/// 回放的时间轴（票 75）。**只有回放有**：Live 那一侧点历史某一手是票 76。
///
/// **游标是权威，其余每一格都是它的函数**（ADR-0002）：帧在载入时一次 fold 好
/// （`Table.replay`），拖动因此是 O(1) 取帧——**这里没有第二份状态，也没有缓存**。
///
/// **公开的**：视图与页面逻辑的用例读同一处推导（同 `canAdvance` / `renderingPending`），
/// 在用例里拄一份同样的算法只会与这里漂。
type Timeline = {
    /// 现在停在第几帧（0 起）。滑块的 `value` 就是它。
    Cursor: int
    /// 末帧的帧号（= 帧数 − 1）。滑块的上界。
    Last: int
    /// 这一帧落定了几手（跨局累计，CONTEXT.md 的 Turn）。轴上那句「第 N 手」读的是它。
    Turns: int
    /// 各局的开局帧，按帧号升序。
    Marks: KyokuMark list
    /// 现在停在第几局（`Marks` 里的序号，0 起）。
    Kyoku: int
    /// **刚落定那一手**的决策记录；那一手没问过模型、或停在某一局的开局帧时是 None。
    ///
    /// **不是「这一帧看得见的全部记录」**（那是 `Table.Decisions`）：时间轴要显示的是
    /// 「上一手是谁想了什么」，与牌桌上那句「上一手：……」说的是同一手。
    Record: DecisionRecord option
}

/// 牌桌那一格里此刻该画什么。**两种来源共用这一个出口**，因此牌桌与结算的渲染只有一份。
[<RequireQualifiedAccess>]
type Shown =
    /// 还在拉那份 Demo 牌谱（只可能出现在首页）。
    | Loading
    /// 开不了局 / 牌谱拉不动：一句中文。
    | Fault of reason: string
    /// 这一桌。
    | Board of table: Table

/// 牌桌页面的全部状态。**一页一 Model，模式是联合类型**（票 71）：
/// 「牌从哪来」在 `Source` 里分岔，其余四格两种来源共用。
type TableModel = {
    /// 这一桌的规则集。Live 取默认预设，回放**取牌谱自带的那一份**
    /// （ADR-0004：规则集是一等输入，回放照的是**这一场**的规则）。
    Ruleset: Ruleset
    /// 牌从哪来。
    Source: Source
    /// 播放控制。
    Playback: Playback
    /// 看哪一份投影。
    Viewpoint: Viewpoint
    /// 牌桌上要不要把危险度排序显示出来（票 25）。**默认关**：
    /// 它是围观者想看的东西，不是牌桌本来就该摆着的。
    ShowDanger: bool
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
    /// 回放的时间轴拖到了第几帧（票 75）。逐事件步进与跳局边界发的也是它
    /// ——**游标怂一条路**，而不是每种走法各一条消息。越界的帧号夹回 [0, 末帧]。
    | CursorMoved of frame: int
    /// 开 / 关牌桌上的危险度排序（票 25）。
    | DangerToggled
    /// 这一局看完了，开下一局。
    | KyokuAdvanced
    /// 把哪个座位交给 LLM。
    | LlmSeatPicked of seat: Seat option
    /// 其余座位换成哪种自带 bot（票 42）。
    | BotPicked of kind: Bot
    /// 拨配桌上那三项规则开关（票 72）。**拨完不当场生效**：
    /// 它只改「下一桌」那一份，要按「重开」才换得掉规则（与种子同一条路）。
    | RulePicked of rule: RuleChoice
    /// 改配置面板里的一个字段。
    | LlmEdited of field: LlmField * value: string
    /// 把这一桌到此刻为止的牌谱存成一个 JSON 文件（票 26）。
    | Exported
    /// 首页那份 Demo Paifu 拉回来了（票 71）。**它不会不来**：拉不动也是一个值
    /// （`Error` 带着一句中文原因）——首页因此永不白屏。
    | DemoLoaded of paifu: Result<Paifu, string>
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

    /// `?table=1` 初次打开时摆的那一桌。挑它两个理由：东 1 局里既有碰吃也有杠（副露的形态看得到），
    /// 且以和了终（结算面板才有役种与番符可看）。挑种子的探针见报告 22。
    ///
    /// **刻意不用曳光弹那个种子**：`?dev=1` 把曳光弹挂出来时，它把原始 mjai 事件
    /// 打在同一张文档里，而 `start_kyoku` 带着四家配牌——两边同种子的话，
    /// 牌桌遮起来的那几家手牌就在下面躺着。
    let private defaultSeed = 2088

    /// 首页那份 Demo 回放**自动播的档速**（票 71）。
    ///
    /// **不是 1×**：现在那份资产是 256 帧（东风战四局、252 手），
    /// `Speed.X1` 的 600ms 一手要播 **2 分 34 秒**，2× 是 **1 分 17 秒**
    /// ——后者既跟得上，也不至于让访客等到不耐烦。人一按倍速就照他的走。
    let private demoSpeed = Speed.X2

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

    // ---- 两种来源共用的拆解 ----

    /// 这一桌是 Live 吗；回放时是 None。**只有这一处从联合里取 Live**，
    /// 视图与页面逻辑的用例读的都是它。
    let live (model: TableModel) : LiveTable option =
        match model.Source with
        | Source.Live live -> Some live
        | Source.Replay _ -> None

    /// 只对 Live 那一半起作用的一条消息。**回放那一侧原样返回**：
    /// 没有事情发生，不是错误（回放页上根本没有那几个按钮）。
    let private onLive (change: Ruleset -> LiveTable -> LiveTable * Cmd<TableMsg>) (model: TableModel) =
        match model.Source with
        | Source.Replay _ -> model, Cmd.none
        | Source.Live live ->
            let next, cmd = change model.Ruleset live
            { model with Source = Source.Live next }, cmd

    /// 牌桌那一格里此刻该画什么。**两种来源共用这一个出口**（票 71）：
    /// 回放取的是第 `cursor` 帧，Live 取的是正在打的那一桌，往下的渲染只有一份。
    let shown (model: TableModel) : Shown =
        match model.Source with
        | Source.Live live ->
            match live.Table with
            | Ok table -> Shown.Board table
            | Error message -> Shown.Fault message
        | Source.Replay ReplayTable.Loading -> Shown.Loading
        | Source.Replay(ReplayTable.Failed reason) -> Shown.Fault reason
        | Source.Replay(ReplayTable.Ready(frames, cursor)) ->
            match List.tryItem cursor frames with
            | Some table -> Shown.Board table
            // 走不到：帧号只由 `replayTick` 与「从头再放」动，两处都夹在 [0, 末帧] 之间。
            | None -> Shown.Fault $"回放的第 {cursor} 帧不在这份牌谱里（共 {List.length frames} 帧）"

    /// 还推得动吗。Live 是「这一局没终、也没出错」，回放是「还有没播到的帧」。
    /// 播放与单步那两个按钮灰不灰读的就是它。
    ///
    /// **公开的**：视图与页面逻辑的用例读同一个判据（同 `renderingPending`）。
    let canAdvance (model: TableModel) : bool =
        match model.Source with
        | Source.Live live ->
            match live.Table with
            | Ok table -> Table.pending table |> Option.isSome
            | Error _ -> false
        | Source.Replay(ReplayTable.Ready(frames, cursor)) -> cursor < List.length frames - 1
        | Source.Replay ReplayTable.Loading
        | Source.Replay(ReplayTable.Failed _) -> false

    // ---- Live ----

    /// 续一记定时器——**除非正在等回执**。等着的时候定时器只会把牌桌空转一遍；
    /// 那一手由 `Answered` 接着开动。
    let private tick (model: TableModel) (playback: Playback) : Cmd<TableMsg> =
        match live model |> Option.bind (fun live -> live.Awaiting) with
        | Some _ -> Cmd.none
        | None -> schedule playback

    /// 这一手真正发出去的那份座位配置（票 46）：人格与模板取**本局定型的那一版**，
    /// 其余字段取面板现在的值。还没定型（这一局一次都没问过）时就是面板上那份。
    let private effective (live: LiveTable) : LlmSeat =
        match live.Pinned with
        | None -> live.Llm
        | Some pinned -> live.Llm |> Rendering.applyTo pinned

    /// 这一桌的配桌：一席交给 LLM（选了的话），其余交给选中的那种自带 bot。
    /// **推导出来而不存下来**：配置只有 `Bot` / `LlmAt` / `Llm` 这一份，不会与第二份对不上。
    let private rosterFor (ruleset: Ruleset) (live: LiveTable) : Roster =
        Roster.withLlm ruleset live.Bot live.LlmAt (effective live)

    /// 这一桌的配桌（谁坐哪里）；**回放没有配桌**，那时是 None。
    ///
    /// 回放里没人要出手（动作全在牌谱里），编一份配桌出来只会被人当真——
    /// 牌谱开头那条 `start_kyoku` 前面的 `names` 是**录下来的**，不是这一桌推导出来的。
    ///
    /// **公开的**：页面逻辑的用例要问「这一桌到底谁坐哪里」，在用例里拄一份同样的推导
    /// 只会与这里漂（票 42 前它真的漂过一份，在 `PaifuExportTests`）。
    let rosterOf (model: TableModel) : Roster option =
        live model |> Option.map (rosterFor model.Ruleset)

    /// 配桌那三项拨过了、但要按「重开」才生效吗（票 72）。
    ///
    /// **它就是页面上那句「按重开才开出新的一桌」的判据**：拨得动，但绝不半场换规则
    /// （同一份牌谱前后按两套规则算就回放不了）。回放那一侧恒为 false：它根本没有配桌。
    /// **公开的**：视图与页面逻辑的用例读同一个判据（同 `renderingPending`）。
    let rulesPending (model: TableModel) : bool =
        match live model with
        | None -> false
        | Some live -> RulesetDraft.ruleset live.Rules <> model.Ruleset

    /// 面板上那两格改过了、但要等下一局才发得出去吗（票 46）。
    ///
    /// **它就是页面上那句「下一局生效」的判据**：不锁那两格，但也绝不静默地半局换掉。
    /// **公开的**：视图与页面逻辑的用例读同一个判据，拄一份同样的推导只会漂。
    let renderingPending (model: TableModel) : bool =
        match live model with
        | None -> false
        | Some live ->
            match live.Pinned with
            | None -> false
            | Some pinned -> pinned <> Rendering.ofSeat live.Llm

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
    let private step (ruleset: Ruleset) (live: LiveTable) : LiveTable * Cmd<TableMsg> =
        match live.Awaiting, live.Table with
        // 上一次问话还没回来：不再问第二次（同一手会有两个请求在飞，而只有一个算数）。
        | Some _, _ -> live, Cmd.none
        | None, Error _ -> live, Cmd.none
        | None, Ok table ->
            match Table.decide (rosterFor ruleset live) table with
            | None -> live, Cmd.none
            | Some(Demand.Ready(action, players)) ->
                {
                    live with
                        Table = Ok(Table.apply action { table with Players = players })
                },
                Cmd.none
            | Some(Demand.Asked(package, config)) ->
                let ticket = live.Ticket + 1

                {
                    live with
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

    /// 落完一手之后：接着播还是停下来。
    ///
    /// **等回执的那段不续定时器**（但仍然是 `Playing`）：定时器只会把牌桌空转一遍，
    /// 真正把它接着开动的是那条 `Answered`。一局终了也停下来：结算面板正摆在那里。
    let private resume (cmd: Cmd<TableMsg>) (model: TableModel) : TableModel * Cmd<TableMsg> =
        match live model |> Option.bind (fun live -> live.Awaiting) with
        | Some _ -> model, cmd
        | None when canAdvance model -> model, Cmd.batch [ cmd; schedule model.Playback ]
        | None ->
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

    // ---- 回放（票 71） ----

    /// 播一帧：帧号 +1，**播到末帧就停在那儿**（结算面板与终局精算正摆在上面）。
    ///
    /// **它不 fold、不判规则**：帧早在 `DemoLoaded` 那一刻一次 fold 好了（`Table.replay`），
    /// 这里只动一个整数。时间轴（拖动与逐事件步进）是票 75 的活，本票只顺着播。
    let private replayTick (frames: Table list) (cursor: int) (model: TableModel) : TableModel * Cmd<TableMsg> =
        let last = List.length frames - 1

        if cursor >= last then
            {
                model with
                    Playback = Playback.pause model.Playback
            },
            Cmd.none
        else
            let played = {
                model with
                    Source = Source.Replay(ReplayTable.Ready(frames, cursor + 1))
            }

            if cursor + 1 >= last then
                {
                    played with
                        Playback = Playback.pause played.Playback
                },
                Cmd.none
            else
                played, schedule played.Playback

    /// 那份 Demo 牌谱拉回来之后：fold 成逐帧的牌桌，换上牌谱自己的规则集，**当场开播**。
    ///
    /// 拉不动、读不动、回放不动三种失法各留一句中文（`ReplayTable.Failed`）：
    /// 首页不许白屏，人得知道是站点的资产没部署全还是那份牌谱太新／太旧。
    let private demoLoaded (paifu: Result<Paifu, string>) (model: TableModel) : TableModel * Cmd<TableMsg> =
        let failed (reason: string) =
            {
                model with
                    Source = Source.Replay(ReplayTable.Failed reason)
            },
            Cmd.none

        match paifu with
        | Error reason -> failed reason
        | Ok paifu ->
            match Table.replay paifu with
            | Error reason -> failed $"Demo 牌谱回放不动：{reason}"
            | Ok frames ->
                let playback = Playback.playing demoSpeed

                {
                    model with
                        Ruleset = paifu.Ruleset
                        Source = Source.Replay(ReplayTable.Ready(frames, 0))
                        Playback = playback
                },
                schedule playback

    /// 一帧是某一局的**开局**吗（票 75）。**判据就是 `Table.opened` 干的那件事**：
    /// 它把「上一手」清空，而落定的每一手都会写上一手（`Table.played`）。
    /// **不拿 `Game.played` 的长度当局号**：那一格在一局终了那一帧就已经 +1，
    /// 拿它划局会把结算那一屏划给下一局。
    let private isOpening (table: Table) : bool = Option.isNone table.Latest

    /// 轴上那一格写着的字：「东1」、连庄时「东1·1」。**取自这一帧的场况**，
    /// 与牌桌中央那句「东 1 局 0 本场」同一个源（`GameState.context`）。
    let private kyokuLabel (table: Table) : string =
        let context = GameState.context table.State
        let honba = if context.Honba > 0 then $"·{context.Honba}" else ""

        $"{Kaze.toDisplay context.Bakaze}{context.Kyoku}{honba}"

    /// 各局的开局帧，按帧号升序。**现扫而不存下来**：帧是值，扫一遍是 O(帧数)，
    /// 而多存一份就多一份会漂的东西（判据 9）。半庄约 800 帧，实测见报告 75。
    let private marksOf (frames: Table list) : KyokuMark list =
        frames
        |> List.indexed
        |> List.filter (snd >> isOpening)
        |> List.map (fun (frame, table) -> {
            Frame = frame
            Label = kyokuLabel table
        })

    /// **刚落定那一手**的决策记录（票 75）。帧上那几条记录是「手序 < 这一帧手数」
    /// 的全部（`Table.replay` 切的），因此最后一条只要手序正好是 `Turns - 1` 就是它。
    ///
    /// **开局那几帧一律 None**：它们没落定新的一手（`Latest = None`），
    /// 而手数沿用着上一局的，不拦的话会把上一局末手的思考摆到新局的开局屏上。
    let private recordOf (table: Table) : DecisionRecord option =
        match table.Latest with
        | None -> None
        | Some _ ->
            table.Decisions
            |> List.tryLast
            |> Option.filter (fun record -> record.Turn = table.Turns - 1)

    /// 回放的时间轴（票 75）；Live 与还没 fold 好的那两段都是 None。
    ///
    /// **公开的**：`TablePanel` 画滑块与局边界读它，dotnet 侧的用例读的也是它。
    let timeline (model: TableModel) : Timeline option =
        match model.Source with
        | Source.Live _
        | Source.Replay ReplayTable.Loading
        | Source.Replay(ReplayTable.Failed _) -> None
        | Source.Replay(ReplayTable.Ready(frames, cursor)) ->
            // 取不到那一帧走不到：帧号只由 `replayTick` / `moveCursor` /「从头再放」动，
            // 三处都夹在 [0, 末帧] 之间。真走到了就当没有时间轴，`shown` 那边会把话说出来。
            List.tryItem cursor frames
            |> Option.map (fun table ->
                let marks = marksOf frames

                {
                    Cursor = cursor
                    Last = List.length frames - 1
                    Turns = table.Turns
                    Marks = marks
                    // 停在第几局 = 开局帧不晚于游标的那几局里的最后一个。
                    Kyoku = (marks |> List.sumBy (fun mark -> if mark.Frame <= cursor then 1 else 0)) - 1
                    Record = recordOf table
                })

    /// 拖到第几帧（票 75）。**它不 fold、不判规则**：帧早在 `DemoLoaded` 那一刻一次 fold
    /// 好了（`Table.replay`），这里只夹一个整数——拖动因此是 O(1) 取帧。
    ///
    /// **一拖就暂停**：手搭在时间轴上的人显然不想让定时器接着跑（与 Live 的「单步」同一个做法）。
    /// `Playback.pause` 顺带换世代，在飞的那记定时器因此作废（`Playback.accepts`）。
    let private moveCursor (frame: int) (frames: Table list) (model: TableModel) : TableModel =
        let last = List.length frames - 1

        {
            model with
                Source = Source.Replay(ReplayTable.Ready(frames, frame |> max 0 |> min last))
                Playback = Playback.pause model.Playback
        }

    /// 去拉那份 Demo 牌谱。**副作用一律由 Cmd 发**（同 `askCmd`）：
    /// 效果体只在浏览器里执行，dotnet 侧只把它编出来、不跑。
    let private demoCmd: Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch ->
            let loaded (paifu: Result<Paifu, string>) = dispatch (DemoLoaded paifu)
            (Demo.paifu ()).``then`` loaded |> ignore)

    // ---- 入口 ----

    /// `?table=1` 初次摆的那一桌，**配置从外面给**。拆出来是为了它是纯的：
    /// 读 localStorage 在 `init` 那一层，因此页面逻辑的用例（dotnet 侧）用得上这个入口。
    ///
    /// **配桌那三项也从外面给**（票 72）：上一次拨到哪儿同样存在 localStorage 里，
    /// 而这一桌开出来就是按它开的——牌桌的规则集恒由它推（`RulesetDraft.ruleset`）。
    let initial (rules: RulesetDraft) (llmAt: Seat option) (config: LlmSeat) : TableModel * Cmd<TableMsg> =
        let ruleset = RulesetDraft.ruleset rules
        let seedText = string defaultSeed

        {
            Ruleset = ruleset
            Source =
                Source.Live {
                    SeedText = seedText
                    Rules = rules
                    Table = openTable (Roster.withLlm ruleset Bot.Uniform llmAt config) seedText
                    // 自带 bot 默认均匀随机（票 42）：它是黄金用例与闸门的基准。
                    Bot = Bot.Uniform
                    LlmAt = llmAt
                    Llm = config
                    // 还没问过话：这一局的人格与模板还改得动（票 46）。
                    Pinned = None
                    Awaiting = None
                    Ticket = 0
                    Agent = AgentStatus.Idle
                }
            // **默认暂停**（`Playback.initial`）：`?table=1` 是最安静的一页，
            // 要点、要读牌桌的那几道无头闸门全靠这一条。
            Playback = Playback.initial
            Viewpoint = Viewpoint.Seated Seat.first
            // 危险度默认关（票 25）。
            ShowDanger = false
        },
        Cmd.none

    /// 首页（`/`）初次摆的那一屏：一份还没拉回来的 Demo 回放（票 71；ADR-0003）。
    ///
    /// **规则集先摆默认那一份**：拉回牌谱之后换成它自带的那一份（`demoLoaded`）。
    /// 这一刻页面上只有一句「正在取那份牌谱」，牌桌还没有。
    let home () : TableModel * Cmd<TableMsg> =
        {
            Ruleset = Ruleset.yonma
            Source = Source.Replay ReplayTable.Loading
            // 还没开播：牌谱拉回来那一刻才 `Playback.playing`（否则定时器空转）。
            Playback = Playback.initial
            // **回放默认上帝视角**（裁决 71-8，票 75 执行）：这份牌谱已经打完了，
            // 没有人还在对局，因此不存在「提前看到他家手牌」那件事（票 22 那条泄露挂账
            // 针对的是真人坐席在场）；而复盘的价值全在看得见四家。
            // **Live 那一页不动**（`initial` 仍是坐到座位 0）：那一页牌还在打。
            Viewpoint = Viewpoint.God
            ShowDanger = false
        },
        demoCmd

    /// 页面初次打开。**地址说了算**（票 71 的 `Route.landing`）：
    /// `/` 是首页的 Demo 回放，`?table=1` 是主持人自己开的一桌（上一次填的配置从
    /// localStorage 里读回来）。**hash 不当路由用**：带 hash 打开退回首页 Demo，
    /// 解码它是票 78 的活。
    let init () : TableModel * Cmd<TableMsg> =
        match Route.landing () with
        | Landing.Home -> home ()
        | Landing.Table ->
            let rules = Store.readRules ()
            initial rules (Store.readSeat (RulesetDraft.ruleset rules)) (Store.readSeatConfig ())

    let update (message: TableMsg) (model: TableModel) : TableModel * Cmd<TableMsg> =
        match message with
        | SeedEdited seed -> model |> onLive (fun _ live -> { live with SeedText = seed }, Cmd.none)
        | Restarted ->
            match model.Source with
            // 回放的「从头再放」：回到第 0 帧，接着自动播。**帧不必重算**——它们是值。
            | Source.Replay(ReplayTable.Ready(frames, _)) ->
                let playback = Playback.playing model.Playback.Speed

                {
                    model with
                        Source = Source.Replay(ReplayTable.Ready(frames, 0))
                        Playback = playback
                },
                schedule playback
            | Source.Replay _ -> model, Cmd.none
            | Source.Live live ->
                // **配桌拨到的那三项在这一刻才生效**（票 72）：与种子同一条路。
                // 半场换规则会让同一份牌谱前后按两套规则算，而回放只读得到牌谱里那一份。
                let ruleset = RulesetDraft.ruleset live.Rules

                // 在飞的那一次问话作废：它的 id 是按旧那桌的决策包编的号。
                {
                    model with
                        Ruleset = ruleset
                        Source =
                            Source.Live {
                                live with
                                    Table = openTable (rosterFor ruleset live) live.SeedText
                                    // 重开一桌就是回到第一局：人格与模板跟着松开（票 46）。
                                    Pinned = None
                                    Awaiting = None
                                    Agent = AgentStatus.Idle
                            }
                        Playback = Playback.pause model.Playback
                },
                Cmd.none
        | Advanced ->
            {
                model with
                    Playback = Playback.pause model.Playback
            }
            |> onLive step
        | PlayToggled ->
            let playback = Playback.toggle model.Playback
            { model with Playback = playback }, tick model playback
        | SpeedPicked speed ->
            let playback = Playback.setSpeed speed model.Playback
            { model with Playback = playback }, tick model playback
        | Ticked generation when not (Playback.accepts generation model.Playback) -> model, Cmd.none
        | Ticked _ ->
            match model.Source with
            | Source.Replay(ReplayTable.Ready(frames, cursor)) -> model |> replayTick frames cursor
            | Source.Replay _ -> model, Cmd.none
            | Source.Live live ->
                let advanced, cmd = step model.Ruleset live

                {
                    model with
                        Source = Source.Live advanced
                }
                |> resume cmd
        | CursorMoved frame ->
            match model.Source with
            | Source.Replay(ReplayTable.Ready(frames, _)) -> moveCursor frame frames model, Cmd.none
            // 还没 fold 好的那两段没有帧可拖；Live 那一侧根本没有时间轴
            // （在 Live 里点历史某一手是票 76）。两条都是「没有事情发生」，不是错误。
            | Source.Replay _
            | Source.Live _ -> model, Cmd.none
        | ViewpointPicked viewpoint -> { model with Viewpoint = viewpoint }, Cmd.none
        | DangerToggled ->
            {
                model with
                    ShowDanger = not model.ShowDanger
            },
            Cmd.none
        | DemoLoaded paifu -> model |> demoLoaded paifu
        | Exported ->
            model
            |> onLive (fun ruleset live ->
                match live.Table with
                // 牌桌都开不起来时没有牌谱可导（按钮那时也是灰的）。
                | Error _ -> live, Cmd.none
                | Ok table -> live, exportCmd (rosterFor ruleset live) (exportName live.SeedText) table)
        | KyokuAdvanced ->
            model
            |> onLive (fun _ live ->
                {
                    live with
                        Table = Result.map Table.nextKyoku live.Table
                        // 一局一定型（票 46）：开下一局时面板上改过的人格与模板在这里生效。
                        Pinned = None
                        Awaiting = None
                },
                Cmd.none)
        | LlmSeatPicked seat ->
            model
            |> onLive (fun _ live ->
                {
                    live with
                        LlmAt = seat
                        Agent = AgentStatus.Idle
                },
                save (fun () -> Store.writeSeat seat))
        // 不重开一桌：配桌是每推一手现推导的，换了从下一手起生效（与换模型坐席同一个做法）。
        | BotPicked kind -> model |> onLive (fun _ live -> { live with Bot = kind }, Cmd.none)
        // **只拨下一桌那一份**（票 72）：牌桌正在按的那份规则集（`model.Ruleset`）
        // 只有 `Restarted` 动得了。拨到的值当场落 localStorage，下次打开还在。
        | RulePicked rule ->
            model
            |> onLive (fun _ live ->
                let rules = RulesetDraft.pick rule live.Rules
                { live with Rules = rules }, save (fun () -> Store.writeRules rules))
        | LlmEdited(field, value) ->
            model
            |> onLive (fun _ live ->
                let config = LlmSeat.edit field value live.Llm
                { live with Llm = config }, save (fun () -> Store.writeSeatConfig config))
        | Answered(ticket, answer) ->
            match model.Source with
            | Source.Replay _ -> model, Cmd.none
            | Source.Live live ->
                match live.Awaiting, live.Table with
                | Some awaiting, Ok table when awaiting.Ticket = ticket ->
                    let played, status = settle awaiting answer table

                    {
                        model with
                            Source =
                                Source.Live {
                                    live with
                                        Table = Ok played
                                        Awaiting = None
                                        Agent = status
                                }
                    }
                    |> resume Cmd.none
                // 过期的回执（重开过一桌、开过下一局，或者票号对不上）：丢掉。
                | _ -> model, Cmd.none
