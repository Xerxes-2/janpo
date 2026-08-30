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
    /// 这一次问话的票号（全局递增，因此也是跨席唯一的）。回执带的**座位与票号**要与
    /// 同一份 `Awaiting` 对上，对不上就丢掉（票 74：四席各判各的）。
    Ticket: int
    /// 问的那一手的决策包。id 往回换动作（`tryAction`）与兜底（`Fallback.action`）都要它；
    /// **问的是哪一席也写在它身上**（`DecisionPackage.seat`），不另存一份。
    Package: DecisionPackage
    /// 那个座位的配置（兜底策略按它的档位走；「在想」那一态的上限秒数也读它）。
    Config: LlmSeat
    /// 已经回来的、还没轮到落的那份回执（票 74）。落子要沿引擎问答的正序走
    /// （`drain`，理由见 `Table.pending`），因此先回来的回执可能要在这里等前席一会儿；
    /// **引擎收齐才裁决，这一等不改墙钟**（整轮的墙钟恒是最慢那一席）。
    Answer: AgentAnswer option
    /// 这一次问话已经等了几秒（`Waited` 每秒 +1，回执到了就停）。页面上「在想」那一态
    /// 显示的就是它：72-3 把超时放到了 240 秒加重试，一席卡住会拖住整手，
    /// 人要看得出还要等多久（72-3 裁决里明写的代价）。
    WaitedSeconds: int
}

/// 在飞那几份问话的拆解。
[<RequireQualifiedAccess>]
module Awaiting =

    /// 这一次问话问的是哪一席（就写在决策包上，不另存一份）。
    let seat (awaiting: Awaiting) : Seat = DecisionPackage.seat awaiting.Package

    /// 「在想」那一态显示的上限（秒）：单次请求的超时。重试会让总等待更长，
    /// 但那是另一次计时的事——这里报的是「最迟什么时候会有下一条消息」那个数。
    let limitSeconds (awaiting: Awaiting) : int = awaiting.Config.TimeoutMs / 1000

/// 真人这一手的倒计时（票 89 的 story 32）。
///
/// **三格里没有「轮到他了」那一格**（票 87 的判据）：轮不轮到他现问 `handOf`。
/// `Turn` 在这里只回答一件事：**这还是同一手吗**——牌桌每落定一个动作 `Table.Turns` 就 +1，
/// 因此他的每一次决策各占一个不同的手序号，它天然就是这一记倒计时的票号。
type HumanClock = {
    /// 这一手的手序号（`Table.Turns`）。`HumanTicked` 带回来的就是它，对不上的那一记丢掉。
    Turn: int
    /// 这一手已经想掉几秒。
    Elapsed: int
    /// 这一手的上限秒数（座位配置拄下来的那一份，恒 > 0；不限时就没有这一整个值）。
    Limit: int
}

/// 倒计时的两个出口。
[<RequireQualifiedAccess>]
module HumanClock =

    /// 还剩几秒（**不会负**：到点那一刻就代他打了，这一格随即换成下一手的）。
    let remaining (clock: HumanClock) : int = max 0 (clock.Limit - clock.Elapsed)

/// 在等强 AI 基线那一手（票 92）。**它与 `Awaiting` 刻意是两个类型**：
///
/// - 没有 `Config`：它没有 provider / key / 超时预算，那几 MB 的资产整桌只有一份；
/// - 没有 `WaitedSeconds`：单手 0.7 ms（ADR-0006 量出来的数），
///   「已等 N 秒」对它恒是 0——写一个恒为 0 的计时器只会让人以为它在想。
///
/// 把它塞进 `Awaiting` 就得给它造一份假 `LlmSeat`，而那份假配置会从气泡、
/// 状态线一直漏到牌谱里去——**这一席不会说话，就不该有一个能说话的表示**。
type Consult = {
    /// 这一次问话的票号（与 `Awaiting` 共用 `LiveTable.Ticket` 那一本账，因此跨席唯一）。
    Ticket: int
    /// 问的那一手的决策包。id 往回换动作与兵底都要它；**问的是哪一席也写在它身上**。
    Package: DecisionPackage
    /// 已经回来、还没轮到落的那份回执（同 `Awaiting.Answer`）：
    /// 落子要沿引擎问答的正序走（`drain`）。
    Answer: BaselineAnswer option
}

/// 在飞那几次问话的拆解。
[<RequireQualifiedAccess>]
module Consult =

    /// 这一次问话问的是哪一席（就写在决策包上，不另存一份）。
    let seat (consult: Consult) : Seat = DecisionPackage.seat consult.Package

/// 这一局定型的那两格：**人格与 prompt 模板**（CONTEXT.md 的 `Persona` / `PromptTemplate`）。
///
/// 术语表把 `Persona` 定成**一局内不变**：它俩都在可缓存前缀里，打到一半换等于把这一局攒下的
/// provider 缓存全废，还让同一局面的对照多出一个自变量。这个类型就是那条不变量的执行者
/// ——一局的**头一次问话**把它俩定住，改动落到面板与 localStorage，但要等下一局才发得出去。
///
/// **一席一份**（票 73）：四家同桌时这条不变量按**座位各自**成立——座位 1 被问过话不该把
/// 座位 2 的人格一并定住（那一席本局可能还没开过口），因此 `LiveTable.Pinned` 是一个
/// 「每家一项」的列表而不是一份。
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

    /// 这份配置此刻的人格与模板（发出去的那一份）。
    let ofSeat (seat: LlmSeat) : Rendering = {
        Persona = seat.Persona
        Template = seat.Template
    }

    /// 面板上那一席此刻的人格与模板（拨到的那一份）。
    /// **两个入口取的是同两格**，只是一个从已经合成好的 `LlmSeat` 上取、
    /// 一个从座位绑定上取——“定型的那一版”与“面板现在那一版”比的就是它俩。
    let ofBinding (binding: SeatBinding) : Rendering = {
        Persona = binding.Persona
        Template = binding.Template
    }

    /// 把定型的那两格盖回这一席的绑定里。**其余字段照面板现在的样子**：
    /// 档案那六格不在可缓存前缀里（provider / 模型 / key / 超时 / 思考预算），
    /// 脚手架档位只动尾部——它们换了下一手就该生效。
    let applyTo (rendering: Rendering) (binding: SeatBinding) : SeatBinding = {
        binding with
            Persona = rendering.Persona
            Template = rendering.Template
    }

/// 一席的 Agent 层此刻处在哪一步（票 74 起**按座位各一份**，`LiveTable.Agent` 是每席一项）。
/// **页面上要看得见**：断电演习（故意配一把坏 key）时对局照样打得完，
/// 但不能静惄惄地打——人得知道模型早就不说话了。
///
/// **case 里不再带座位**：哪一席由它在列表里的位置说了算（与 `Pinned` 同一个做法），
/// 再带一份就多一份会对不上的东西（第 1 项写着「座位 2」这种状态不该表示得出来）。
[<RequireQualifiedAccess>]
type AgentStatus =
    /// 这一席不是模型，或者还没轮到它。
    | Idle
    /// 正在等这一席的回执（已等秒数与上限在 `Awaiting` 那份上，不存第二份）。
    | Asking
    /// 上一次模型自己选出了动作。
    | Spoke of reason: string option * latencyMs: int
    /// 上一次是兜底代打的。**粘着不掉**，直到模型又能好好说话为止。
    | Troubled of reason: string

/// 「复制分享链接」那一下的下场（票 78）。**三态刻意是三个 case**（判据 12）：
/// 进了剪贴板、进了但载荷长过阈值（该勝人改用 JSON 文件）、剪贴板不让写。
/// 前两态都带字符数：长度是人判断「发链接还是发文件」的依据，页面上要印出来。
[<RequireQualifiedAccess>]
type ShareOutcome =
    /// 写进剪贴板了，载荷这么多字符。
    | Copied of chars: int
    /// 也写进剪贴板了，但载荷超了阈值——页面当场勝人改用「导出牌谱」的 JSON 文件。
    /// **仍然复制**：链接在浏览器地址栏里照样能用（普遍收 32K 以上），会截断它的是
    /// 聊天工具——拦着不给比复制了再勝一句更霸道。
    | Oversized of chars: int
    /// 剪贴板不让写（权限被拒、页面不在焦点上这类），附一句中文原因。
    | Failed of reason: string

/// 分享下场的阈值与两个渲染出口。
[<RequireQualifiedAccess>]
module ShareOutcome =

    /// 载荷长过这个数就当场勝人改用 JSON 文件（单位：base64url 之后的字符数）。
    ///
    /// **8,000 是判断不是实测**（票 77 §9 的建议，裁决记在 DECISIONS 78 段）：
    /// 票 77 实测一整场半庄 7,720 字符、东风战 4,842——阈值取在半庄之上一点，
    /// 好让「一整场标准对局」永远够发；浏览器地址栏普遍收 32K 以上，先截断的是
    /// 聊天工具，而哪一家在几千字符截没实测过。超过它的那种场（连庄爆长）本来就该走文件。
    let threshold: int = 8000

    /// 三态给机器看的那一半（`data-share`），闸门读它；人读的是 `toDisplay` 那句话。
    let toWire (outcome: ShareOutcome) : string =
        match outcome with
        | ShareOutcome.Copied _ -> "copied"
        | ShareOutcome.Oversized _ -> "oversized"
        | ShareOutcome.Failed _ -> "failed"

    /// 页面上那句话。超阈值那态把两个数都印出来：人要能自己核「超了多少」，
    /// 而不是只听一句「太长了」。
    let toDisplay (outcome: ShareOutcome) : string =
        match outcome with
        | ShareOutcome.Copied chars -> $"已复制（载荷 {chars} 字符）。"
        | ShareOutcome.Oversized chars -> $"已复制，但载荷有 {chars} 字符（超过 {threshold}）——聊天工具常把太长的地址截断，这一场改用「导出牌谱」的 JSON 文件更稳。"
        | ShareOutcome.Failed reason -> $"分享链接没写进剪贴板：{reason}"

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
    /// 这一桌的坐法（票 73）：**模型档案库 + 四席各自的绑定**，同时落在 localStorage。
    ///
    /// **它不一定就是这一手发出去的那份**：各席的人格与模板在一局之内定住（见 `Rendering`），
    /// 真正发出去的那份由 `rosterOf` 推导。
    Seating: SeatingPlan
    /// 档案编辑处此刻开着的是第几份（0 起）；越界 = 一份都没开着（档案库空的那一刻）。
    ///
    /// **key 在界面上只出现在这一处**（票 73 的硬判据）：座位那几行一律不重填 key。
    Editing: int
    /// 刚刚发生、页面必须说出来的那件事（票 73）：删掉的那份档案还被几席引用着。
    ///
    /// **不许静静地变成「没有选手」**：那几席当场退回 bot，而人得知道这件事发生过。
    Notice: string option
    /// **每席一项**（按座位升序）：这一局已经定型的人格与模板；
    /// `None` = 这一席这一局还没被问过话，改了当场生效。
    ///
    /// **在那一席这一局的头一次问话时定住**，`Restarted` 与 `KyokuAdvanced` 时四席一起松开。
    /// 定住之后面板照收编辑（`SeatingPlan` 会变），但发出去的仍是这一份——页面上那行
    /// 「下一局生效」说的就是它俩不一致（`renderingPending`）。
    Pinned: Rendering option list
    /// 在飞的那几次问话（票 74：响应阶段里待答的几席**一起问**，一席一份）。
    /// **还有在飞的就不续定时器**（否则牌桌会空转），但不再因为一席在飞就不问第二席。
    Awaiting: Awaiting list
    /// **强 AI 基线那几 MB 资产此刻在哪一步**（票 92；ADR-0006）。
    ///
    /// **整桌只有一份**（不是每席一份）：四席都拨到它也只下载一次。
    /// **它不在 `SeatingPlan` 里**：那一份是“人拨到哪儿”（落 localStorage），
    /// 而这一格是“这一次打开页面拉到了没有”——存进 localStorage 只会让下次打开时
    /// 页面写着「就绪」而内存里一个字节都没有。
    Baseline: BaselineStatus
    /// 强 AI 基线席在飞的那几次问话（票 92）；**与 `Awaiting` 平行而不合并**（见 `Consult`）。
    Consulting: Consult list
    /// 强 AI 基线交不出那一手、由兵底代打的那几次，**新的在前**（同 `Passed`）。
    ///
    /// **它不进牌谱**：那一席一条决策记录都不留（票面边界），因此兵底计数也只能
    /// 记在这里——而「这一手不是它自己决的」不许静默替换（票 23 的那条规矩）。
    BaselineTroubles: string list
    /// 这一桌至此刻真人**没有自己打出去的那几手**，**新的在前**
    /// （票 87 开账、票 88 换了语义、票 89 拓宽了一格）。
    ///
    /// 票 87 时这里记的是「平台替他过掉了什么」（那时还没有按钮）；票 88 把按钮做了出来，
    /// 于是它记的是**他自己放掉了什么**；票 89 的时限到点代他打的那几手同样记在这一本上，
    /// 两种靠 `HumanPass.AutoPlayed` 分开（分开成两本账的话，「这一局他有几手不是自己决的」
    /// 就要从两处各数一遍再相加）。
    /// 跨局累计（同 `Table.fallbacks`），只在重开一桌时清空。
    Passed: HumanPass list
    /// 真人**这一手**的倒计时（票 89 的 story 32）；没设时限、或者此刻不轮到他时是 None。
    ///
    /// **它不是「轮到他了吗」的第二份判据**（票 87 那条：那件事现问 `handOf`）：
    /// 这一格只记「哪一手、走掉几秒」，而它自己每条消息都由 `wound` 按 `handOf` 重新推一遍
    /// ——“倒计时只在轮到自己时走”因此是结构上的事实，不是几处记得设一下的纪律。
    Clock: HumanClock option
    /// 问话的票号，每问一次 +1（全局递增，四席共用一本账）。
    Ticket: int
    /// Agent 层的状态线，**每席一项**（按座位升序，票 74）。
    Agent: AgentStatus list
    /// 「复制分享链接」最近一次的下场（票 78）；一次都没点过、刚点下去还在编、
    /// 或重开过一桌之后是 None。
    Shared: ShareOutcome option
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
    /// fold 好了：逐帧的牌桌、播到第几帧，以及牌谱开头那一列 `names`。
    ///
    /// **名字跟着胶片走**（票 82）：帧是对事件流 fold 出来的，而 `names` 在 `start_game`
    /// 那一条事件上——它不属于任何一局，因此 fold 不出来，只能在这一层带着。
    /// 名牌上写的就是它（`provider/model`）：回放没有配桌，编一份出来只会被人当真。
    | Ready of frames: Table list * cursor: int * names: string list

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

/// 一席此刻的思考气泡（票 76；CONTEXT.md 的 `Thinking Bubble`）。
///
/// **它是「展示某个 DecisionRecord 的 UI 部件」，不是数据**（术语表原话）：两个 case 直接
/// 抱着那条记录本身，气泡这一侧一个字段都不复制——数据源只有 `Table.Decisions` /
/// `Paifu.Decisions` 一处。
///
/// **三态刻意是三个 case，而不是一个带 option 的记录**（判据 12：拒绝理由各有各的 case）：
/// 「在想」根本没有记录可读，「兜底」必然有一句原因。混成一个记录的话，视图里每一处都得
/// 重判一遍「这一格有没有值」——那正是三态看不出区别的开头。
[<RequireQualifiedAccess>]
type Bubble =
    /// 正在等这一席的回执。**只有 Live 有**：回放里没有在飞的问话。
    /// 带着**已等秒数与上限秒数**（票 74；72-3 裁决明写的代价）：240 秒超时加重试，
    /// 一席卡住会拖住整手，人要看得出还要等多久，而不是干等一个不动的气泡。
    | Thinking of waitedSeconds: int * limitSeconds: int
    /// 上一次它自己说了什么（thinking 优先，没有思考预算时退回那一句理由）。
    | Spoke of record: DecisionRecord
    /// 上一次是兜底代打的，附原因。**粘着不掉**：那条记录就在那儿，直到它又能好好说话
    /// ——下一条记录不带 `Fallback` 时这一席自己就退回 `Spoke`。
    | Troubled of record: DecisionRecord * reason: string

/// 气泡的三个出口：背后那条记录、给机器看的那一半、以及气泡上写的那句话。
[<RequireQualifiedAccess>]
module Bubble =

    /// 这个气泡背后的那条决策记录；「在想」那一态还没有记录（**它因此点不开**）。
    let record (bubble: Bubble) : DecisionRecord option =
        match bubble with
        | Bubble.Thinking _ -> None
        | Bubble.Spoke record -> Some record
        | Bubble.Troubled(record, _) -> Some record

    /// 三态给机器看的那一半（`data-bubble`）。人看的是下面那句话与画法（虚线 / 实线 / 红），
    /// 闸门读的是它——两头对不上就是错。
    let toWire (bubble: Bubble) : string =
        match bubble with
        | Bubble.Thinking _ -> "thinking"
        | Bubble.Spoke _ -> "spoke"
        | Bubble.Troubled _ -> "troubled"

    /// 气泡头上那两个字：这一席此刻是在想、说了话，还是被代打了。
    let toLabel (bubble: Bubble) : string =
        match bubble with
        | Bubble.Thinking _ -> "在想"
        | Bubble.Spoke _ -> "说"
        | Bubble.Troubled _ -> "兜底"

    /// 气泡上那句话最多几个字（票 81）。**这个数是从真语料上量的**：票 79 换上去那份 Demo
    /// 的 464 条理由里，中位 48 字、p75 62、p90 77、最长 260——72 字让 **87%** 的理由整句放得下，
    /// 余下那 13% 截断并挂一句「点开看全文」。
    ///
    /// 它同时是**气泡不把牌桌撑变形**的判据：最窄那一席（左右两家）一行约 25 个汉字，
    /// 72 字 ≈ 3 行，正好是票 76 那个 `max-height: 4.2rem` 的高度。于是 CSS 那条硬裁拿掉了
    /// ——**长度在这里就有界，而且截了会说**（硬裁是无声的，79 §8 报的病就是它）。
    [<Literal>]
    let private sentenceLimit = 72

    /// thinking 的头一段：第一个换行之前那一截。thinking 是没结构的整段文字，
    /// 而气泡只放一句话——**取头一段**是「不重排模型的话、又只取一句」最省的一种取法。
    let private firstParagraph (thinking: string) : string =
        let trimmed = thinking.Trim()

        match trimmed.IndexOf '\n' with
        | -1 -> trimmed
        | index -> trimmed.Substring(0, index).Trim()

    /// 截到上限并以「……」收尾；没超上限就原样交出去。`more` 是「这句话后面还有被丢掉的东西」
    /// （thinking 取了头一段那一路）。
    ///
    /// **一个函数同时给出「写什么」与「截没截」**：两者是同一次判断。分成两个函数各算一遍的话，
    /// 气泡上那枚「点开看全文」与那三个点就成了两处判据，迟早会漂到一边有、另一边没。
    let private said (bubble: Bubble) : string * bool =
        let clip (more: bool) (text: string) : string * bool =
            if String.length text <= sentenceLimit then
                (if more then text + "……" else text), more
            else
                text.Substring(0, sentenceLimit) + "……", true

        match bubble with
        | Bubble.Thinking(waited, limit) -> $"正在等它回话……已等 {waited} 秒 / 上限 {limit} 秒", false
        | Bubble.Troubled(_, reason) -> clip false reason
        | Bubble.Spoke record ->
            // **`reason` 优先，只有 thinking 没有 reason 时才退回它的头一段**（票 81 主人裁的）：
            // 气泡放的是**一句话理由**，thinking 全文是点开那一屏的活（术语表的 `Thinking Bubble`）。
            // 票 76 的优先级正好相反（thinking 优先），真语料上那就是一整段文字塞进一个气泡。
            match record.Reason, record.Thinking with
            | Some reason, _ -> clip false reason
            | None, Some thinking ->
                let head = firstParagraph thinking
                clip (head <> thinking.Trim()) head
            // 两样都没有时也要说一句——空气泡与「没有气泡」在页面上分不出来。
            | None, None -> "（这一手没留下理由与思考原文）", false

    /// 气泡上写的那句话（**一句话，不是 thinking 全文**：CONTEXT.md 的 `Thinking Bubble`）。
    ///
    /// 兜底那一态写的是**原因**（与牌桌上那句「上一手：……（兜底：……）」、`data-fallback`
    /// 同一个来源：`DecisionRecord.Fallback`）。它的 thinking 与理由仍在全文面板里。
    let toDisplay (bubble: Bubble) : string = said bubble |> fst

    /// 这句话被截过吗——**截了就要在气泡上说一句**（票 81：溢出要有看得出来的提示）。
    /// 全文在点开那一屏里（`BubbleDetail`），因此这只是「还有更多」的招子，不是另一份数据。
    let clipped (bubble: Bubble) : bool = said bubble |> snd

/// 气泡点开之后那一屏（票 76 的全文面板，spec 的 story 5）：那一手的**决策记录**与
/// **当时的局面快照**。
///
/// **两格都从同一帧上读出来**：`Record` 就是这一帧刚落定那一手的那条（`recordOf`，与时间轴
/// 上那一格同一处推导），`Snapshot` 就是那一帧本身。**记录不存第二份**。
///
/// 「最终落定的动作」也在这里：牌谱里存的是**包内 id**（26-3：意图不上牌谱），
/// 而这一帧的 `Latest` 就是那一手真落进引擎的动作。
type BubbleDetail = {
    Record: DecisionRecord
    Snapshot: Table
    /// 点开这一手**之前**游标停在第几帧（票 86）；Live 那一侧没有游标，是 None。
    ///
    /// **面板要说得出人被搬到了哪儿**：点开会把时间轴挪走（票 76：轴只有一根），
    /// 而从前只有 `data-bubble-turn` 给机器看。这一格就是那句话的判据（`BubbleDetail.toDisplay`），
    /// 也是「收起来回得去」这件事在面板上的证据。
    Origin: int option
}

/// 全文面板上那句「正在看第 N 手」。
[<RequireQualifiedAccess>]
module BubbleDetail =

    /// 面板头一句话（票 86）：**正在看的是第几手，以及收起来会怎么样**。
    ///
    /// 两种来源两句话，因为它们真的不是同一件事：回放里点开**把时间轴搬走了**（票 76），
    /// 收起来要说清它会搬回来；Live 那一页根本没有时间轴（`timeline` 在那边恒是 None），
    /// 说「时间轴跳到了这一手」就是在说一个页面上不存在的东西。
    let toDisplay (detail: BubbleDetail) : string =
        match detail.Origin with
        | Some _ -> $"正在看第 {detail.Record.Turn} 手：时间轴跟着跳到了这一手，收起（或按「播放」）就回到点开之前那一处。"
        | None -> $"正在看第 {detail.Record.Turn} 手：牌桌上摆的是那一刻的快照，这一桌照旧停在现在那一手。"

/// 点开全文面板**之前**停在哪儿（票 86）。
///
/// **只有回放有**：那一侧点开会把游标搬到那一手（`openAt`，票 76 的「轴只有一根」），
/// 因此非记下原处不可；Live 那一侧 `openAt` 只摆一张快照，游标与 `live.Table` 都没动过，
/// 没有可回的地方（那一侧的 `Origin` 因此恒是 None）。
///
/// **不存整份 `Playback`**：那份值里的世代号在点开那一刻就已经过期（`openAt` 的 `pause` 换掉了它），
/// 存着它就早晚有人原样放回去。回程要的只有「点开之前在播吗」这一个 bool（见 `Playback.resumed`）。
type Origin = { Cursor: int; Playing: bool }

/// 气泡点开的那一手：**摊开的那一帧**，加上**点开之前停在哪儿**（票 86）。
///
/// **两格在同一个记录里而不是模型上的两格**：有摊开的面板才有回得去的原处，
/// 拆成两格就表示得出「有原处却没摊开」这种对不上的状态——而这一票治的正是
/// 「状态被改了却没人管改回来」。
type Opened = {
    /// 那一手落定之后的那一帧。摊开的那一刻牌桌上摆的就是它（`shown`）。
    Snapshot: Table
    /// 点开之前停在哪儿；Live 那一侧没有可回的地方（见 `Origin`）。
    Origin: Origin option
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
    /// 气泡点开的是哪一手（票 76 的全文面板）：**存的是那一手落定之后的那一帧**，
    /// 不是那条记录——记录就挂在这一帧的 `Decisions` 末条上（`recordOf`），存第二份只会漂。
    ///
    /// 摊开的那一刻牌桌上摆的就是它（`shown`）：story 5 的「局面快照」不是另画一张牌桌，
    /// 而是把同一份渲染指到那一帧上。`None` = 没点开任何一手。
    ///
    /// **它同时抱着「点开之前停在哪儿」**（票 86 的 `Origin`）：回放里点开会把游标搬走，
    /// 没人记下原处的话就只能停在跳过去那一处——主人试玩时报的就是它。
    Opened: Opened option
    /// 复盘那一列此刻是不是**只摆值得看的那几手**（票 105）。**默认是**：
    /// 一整场一百多条排成一列，人得自己一条条扫（票 90 与票 93 各记过一次这条 nitpick）。
    ///
    /// **它是一个开关，不是一份名单**：哪几手值得看是 `Review.worthwhile` 现算的（引擎那几个数
    /// 加强 AI 那一行），把那份名单存在这里就是第二条算路。筛掉了多少由
    /// `ReviewFilter.toDisplay` 当场说出来（票面：不许静静地少显示）。
    ReviewFiltered: bool
    /// 「导入牌谱 JSON」最近一次失败的原因（票 78）；没失败过、或后来导成了一次是 None。
    ///
    /// **不落进 `ReplayTable.Failed`**：导入失败不该把正在播的那份回放轰掉——
    /// 人挑错文件是常事，原样播着、旁边说一句原因就够（`Failed` 是给「除它没有
    /// 别的可摆」的首页 Demo 与分享链接留的）。导入入口只在回放那一页，Live 恒为 None。
    ImportFault: string option
}

/// 牌桌上能发生的事。**一步一 Msg**：`Advanced` 与 `Ticked` 各推进一手，
/// 驱动循环就是 Elmish 的 update，页面里没有第二个 loop。
///
/// `NoComparison` 是 `ImportPicked` 里那个 `File` 逼出来的（浏览器对象比不了大小）；
/// 消息本来就没有谁在比较，结构相等照旧在（用例里的 `Assert.Equal` 读的是它）。
[<NoComparison>]
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
    /// 点开 / 收起某一手的全文面板（票 76）。`turn` 是那一手的手序（`Table.Turns` 那个号，
    /// CONTEXT.md 的 Turn）；`None` = 收起来。
    ///
    /// **回放里它顺带把游标挪到那一帧**：轴只有票 75 那一根，不给全文面板另开一条时间轴。
    | RecordOpened of turn: int option
    /// 开 / 关牌桌上的危险度排序（票 25）。
    | DangerToggled
    /// 复盘那一列：只看值得看的那几手 / 看全部（票 105）。
    /// **不另存一份名单**：它只拨一个开关（`TableModel.ReviewFiltered`）。
    | ReviewFilterToggled
    /// 这一局看完了，开下一局。
    | KyokuAdvanced
    /// 把某一席交给谁（票 73）：均匀随机 / 有主见 / 某份模型档案。
    /// **四席各管各的**，因此四家都可以是模型。
    | SeatBound of seat: Seat * choice: SeatChoice
    /// 改某一席的脚手架档位 / 人格 / prompt 模板（票 73：这三项是座位级的）。
    | SeatEdited of seat: Seat * field: SeatField * value: string
    /// 档案编辑处换一份档案来编。
    | ProfileOpened of index: int
    /// 新建一份档案（摆在库尾，当场开在编辑处）。
    | ProfileAdded
    /// 删掉第几份档案。引用它的那几席退回 bot，**页面把这件事说出来**。
    | ProfileDeleted of index: int
    /// 改开着那一份档案的一个字段。
    | ProfileEdited of field: ProfileField * value: string
    /// 一行式开桌那一行上改一格（票 138）：provider / 模型 / key。
    ///
    /// **它改的就是编辑处开着的那一份档案**（`ProfileEdited` 改的同一份），
    /// 不是另存一份草稿——两处填的必须是同一个值，否则「key 在界面上只出现在两处」
    /// 就成了「同一件事有两个说法」（票 73 的硬判据）。
    /// 与 `ProfileEdited` 的唯一分别：**库空着时它先补一份档案**
    /// （那一行在配桌收着时也点得到，而那时人看不见「新建档案」那一枚）。
    | QuickEdited of field: ProfileField * value: string
    /// 一行式开桌那一枚〔开打〕（票 138）：把编辑处那份档案绑到座位 0（库空着就先补一份），
    /// 其余三席原样留着（默认是自带 bot），**并当场开播**。
    | QuickStarted
    /// 拨配桌上那三项规则开关（票 72）。**拨完不当场生效**：
    /// 它只改「下一桌」那一份，要按「重开」才换得掉规则（与种子同一条路）。
    | RulePicked of rule: RuleChoice
    /// 把这一桌到此刻为止的牌谱存成一个 JSON 文件（票 26）。
    | Exported
    /// 把这一桌到此刻为止的**棋谱**装进一条分享链接并写进剪贴板（票 78）。
    /// 推理不上 URL（`Share.toPayload` 里走 `Paifu.stripAudit`）；全量那条路是 `Exported`。
    | Shared
    /// 剪贴板那一趟回来了（票 78）：载荷多少字符，或者为什么没写上。
    /// 超不超阈值在 update 里判（纯的，dotnet 侧的用例才够得着），Cmd 那侧只报字符数。
    | ShareSettled of result: Result<int, string>
    /// hash 里那段分享载荷解完了（票 78）。与 `DemoLoaded` 同形：**它不会不来**，
    /// 读不动也是一个值（`Error` 带着「载荷读不动：」或「牌谱读不动：」打头的
    /// 中文原因，票 77 分好的两层）——带载荷打开的那一屏因此永不白屏。
    | SharedLoaded of paifu: Result<Paifu, string>
    /// 人在「导入牌谱 JSON」处挑了一个文件（票 78）。读文件是异步的，
    /// 结果由 `ImportLoaded` 带回。
    | ImportPicked of file: Browser.Types.File
    /// 挑中的那份牌谱读完了（票 78）。与 `DemoLoaded` / `SharedLoaded` 同形：
    /// 三个来源都把「拿到一份牌谱或一句中文原因」交回同一张牌桌。
    /// 解码在 Cmd 那侧（`Decode.fromString` 的 JS 后端只在浏览器里跑得动），
    /// `Error` 的两种前缀：「这个文件读不进来：」与「牌谱读不动：」。
    | ImportLoaded of paifu: Result<Paifu, string>
    /// 首页那份 Demo Paifu 拉回来了（票 71）。**它不会不来**：拉不动也是一个值
    /// （`Error` 带着一句中文原因）——首页因此永不白屏。
    | DemoLoaded of paifu: Result<Paifu, string>
    /// Agent 层的回执回来了。**座位与票号**要与在飞的一份 `Awaiting` 对上，
    /// 对不上（重开过一桌、开过下一局、或回执错位）就丢掉——四席各判各的（票 74）。
    ///
    /// **它不会不来**：超时与 provider 报错在 Agent 层都是值，最后也会变成一条回执
    /// （`Failure` 带着原因）——对局因此永不卡死。
    | Answered of seat: Seat * ticket: int * answer: AgentAnswer
    /// 那一次问话又等过了一秒（票 74）。**它只让页面上「已等 N 秒」往前走**，
    /// 牌桌一根汗毛都不动；那一票已经落定/作废时它静默地停。
    | Waited of ticket: int
    /// 强 AI 基线那几 MB 拉完了（票 92）。**它不会不来**：404、离线、超时、
    /// 编不动与初始化失败在 TS 那侧都是值（`Error` 带着一句中文原因）
    /// ——选了那一席的那桌因此永不卡在“正在拉”上。
    | BaselineLoaded of loaded: Result<int, string>
    /// 强 AI 基线那一手回来了（票 92）。**座位与票号**要与在飞的一份 `Consult` 对上，
    /// 对不上（重开过一桌、开过下一局）就丢掉——与 `Answered` 逐字同一条规矩。
    | BaselineDecided of seat: Seat * ticket: int * answer: BaselineAnswer
    /// **真人点了一下**（票 87）：他选的是这一包里第 `id` 条动作。
    ///
    /// **跨界回来的同样只有一个 id**（与 `Answered` 那条逆向入口逐字相同）：
    /// 页面构造不出一个 `Action`，id 不在这一包里就没有事情发生
    /// ——于是“真人不可能犯规”（spec 的 story 30）在**结构上**成立。
    | HumanPlayed of id: int
    /// **真人这一手的倒计时又走了一秒**（票 89）。`turn` 是那一手的手序号（`Table.Turns`），
    /// 对不上（他已经出手了、重开过一桌、或者时限拨成了不限）就丢掉，链自己断
    /// ——与 `Waited` 逐字同一条规矩。
    ///
    /// **到点那一记不是「把牌桌停下来」而是代他打一手**：牌局因此继续走，
    /// 代打那一手走的是与 `HumanPlayed` 同一条路（引擎的 `Table.apply`）。
    | HumanTicked of turn: int

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

    // ---- 浏览器 API 的两个小口（票 78）。与 `Download` 同一个理由用 Emit：
    // 为一次调用引一个绑定包不值当；dotnet 侧编得过、跑不了（效果体只在浏览器里执行）。

    /// 挑中的那个文件的正文。`File.text()` 是标准 API（Chrome 76+）。
    [<Emit("$0.text()")>]
    let private fileText (_file: Browser.Types.File) : JS.Promise<string> = jsNative

    /// 把一段文本写进剪贴板。安全上下文（https 或 localhost）才有 `navigator.clipboard`；
    /// 写不进去是 reject，调用点接住它变成一句中文。
    [<Emit("navigator.clipboard.writeText($0)")>]
    let private writeClipboard (_text: string) : JS.Promise<unit> = jsNative

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

    /// 牌从哪来那一层此刻该画什么（票 71）：回放取第 `cursor` 帧，Live 取正在打的那一桌。
    let private board (source: Source) : Shown =
        match source with
        | Source.Live live ->
            match live.Table with
            | Ok table -> Shown.Board table
            | Error message -> Shown.Fault message
        | Source.Replay ReplayTable.Loading -> Shown.Loading
        | Source.Replay(ReplayTable.Failed reason) -> Shown.Fault reason
        | Source.Replay(ReplayTable.Ready(frames, cursor, _)) ->
            match List.tryItem cursor frames with
            | Some table -> Shown.Board table
            // 走不到：帧号只由 `replayTick` 与「从头再放」动，两处都夹在 [0, 末帧] 之间。
            | None -> Shown.Fault $"回放的第 {cursor} 帧不在这份牌谱里（共 {List.length frames} 帧）"

    /// 牌桌那一格里此刻该画什么。**两种来源共用这一个出口**（票 71），往下的渲染只有一份。
    ///
    /// **气泡点开的那一手压过牌从哪来**（票 76 的 story 5）：摊开全文面板时牌桌上摆的就是
    /// **那一手落定那一刻的快照**。回放里两者本来就是同一帧（`RecordOpened` 顺带挪了游标）；
    /// Live 里它是导成牌谱重 fold 出来的那一帧，**而 `live.Table` 一手都没退回去**（只读）。
    let shown (model: TableModel) : Shown =
        match model.Opened with
        | Some opened -> Shown.Board opened.Snapshot
        | None -> board model.Source

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
        | Source.Replay(ReplayTable.Ready(frames, cursor, _)) -> cursor < List.length frames - 1
        | Source.Replay ReplayTable.Loading
        | Source.Replay(ReplayTable.Failed _) -> false

    // ---- Live ----

    /// **轮到真人出牌了吗**（票 87）：那一席此刻的决策包；不轮到他时是 None。
    ///
    /// **现问这一刻的局面，不在 model 上存第二份**（判据 9）：存一份的话，
    /// 页面刚打开那一瞬（他就是亲、牌已经摸到手上）那一格还是空的，
    /// 页面会说「轮到别人」——而那是句假话。现问就不会有这种“存了但还没填”的中间态。
    ///
    /// **两道前置都很便宜，贵的那一步只在真轮到他时才走**：
    /// 先看引擎待答的**头一家**是不是他，是才去搭那份决策包
    /// （`DecisionPackage.forSeat` 要一次从头 fold）。
    ///
    /// **票 87 那道「响应阶段不算轮到他」的前置在票 88 拆掉了**：吃碰杠、荣和与「过」
    /// 现在各是一枚真按钮，因此响应阶段同样停下来等他。
    ///
    /// **仍旧只看头一家（`Table.pending`）而不是「他在不在待答之列」**：响应阶段可能
    /// 同时等好几家，而**落子顺序必须沿引擎待答的那个顺序走**（`drain` 那段注释：
    /// 回放重建响应阶段就是按「每次取头一家」重建的）。让他提前插队会把同一轮里
    /// 模型席的决策记录手序号弄偏一位，于是牌谱与逐帧回放对不上。
    /// **他因此与模型席完全同级**：都是轮到自己答那一刻才拿到包。
    let private handOf (live: LiveTable) : DecisionPackage option =
        match SeatingPlan.humanSeats live.Seating |> List.tryHead, live.Table with
        | Some seat, Ok table ->
            Table.pending table
            |> Option.filter (fun choice -> choice.Seat = seat)
            |> Option.bind (fun _ -> DecisionPackage.forSeat seat table.State)
        | Some _, Error _
        | None, _ -> None

    /// 真人那一席的绑定（票 89）；这一桌没有真人时是 None。**取真人那一席的配置只有这一处**：
    /// 档位与时限各自找一遍座位的话，「一桌只坐得下一席」那条不变量就要被相信两次。
    ///
    /// **读的是 `live.Seating` 而不是 `effective live`**（同名牌）：定型（`Rendering`）
    /// 定的是人格与模板（那两样在可缓存前缀里，一局内不变），而真人根本不走 prompt
    /// ——他拨一下档位或时限当场就该变（新手辅助轮是给人用的，不是对照实验的自变量）。
    let private humanBinding (live: LiveTable) : SeatBinding option =
        SeatingPlan.humanSeats live.Seating
        |> List.tryHead
        |> Option.map (fun seat -> SeatingPlan.bindingAt seat live.Seating)

    /// 真人那一席拨到的脚手架档位（票 89）；这一桌没有真人时是 None。
    let private tierOf (live: LiveTable) : ScaffoldTier option =
        humanBinding live |> Option.map (fun binding -> binding.Tier)

    /// 真人那一席设的思考时限（秒）；**默认不限时就是 None**（票 89 的 story 32）。
    let private limitOf (live: LiveTable) : int option =
        humanBinding live |> Option.bind SeatBinding.limit

    /// 倒计时那一记钟（票 89）：一秒后发一条 `HumanTicked`。**一手一条链**
    /// ——他出手（或到点代他出手）之后手序号就变了，旧那条链下一记回来时自己断。
    /// 与 `waitCmd` 逐字同一个形状。
    let private clockCmd (turn: int) : Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch -> JS.setTimeout (fun () -> dispatch (HumanTicked turn)) 1000 |> ignore)

    /// 倒计时该不该在走，以及它是不是还在计同一手（票 89）。
    ///
    /// **判据只有一条：`handOf` 说轮到他、而且这一席设了时限**。因此它挂在「轮到他了吗」
    /// 上而不是「牌桌停着吗」上（票 88 之后他在想的时候模型席照问照答，牌桌并没停）。
    ///
    /// **每条消息都重新推一遍**（`clocked`）：于是“他中途坐下来”、“他把时限拨成不限”、
    /// “重开一桌”这几种都不必各写一次启停——那正是判据会漂的开头。
    /// **只有开新一记时才发定时器**（已经在走的那一手原样返回），因此一手只有一条链。
    let private wound (live: LiveTable) : LiveTable * Cmd<TableMsg> =
        match handOf live, limitOf live, live.Table with
        | Some _, Some limit, Ok table ->
            match live.Clock with
            // 同一手、同一个上限：链还在走，什么都不必做。
            // （人在他想的当口改了上限：重新计时——拿旧秒数去卡新上限会当场到点）
            | Some clock when clock.Turn = table.Turns && clock.Limit = limit -> live, Cmd.none
            | _ ->
                {
                    live with
                        Clock =
                            Some {
                                Turn = table.Turns
                                Elapsed = 0
                                Limit = limit
                            }
                },
                clockCmd table.Turns
        // 不轮到他 / 不限时 / 牌桌开不起来：那一格该是空的。
        // **不限时那条路上一个效果体都不发**（票 87/88 那几条数效果体的用例读的就是它）。
        | _ ->
            match live.Clock with
            | None -> live, Cmd.none
            | Some _ -> { live with Clock = None }, Cmd.none

    /// 这一刻真正发得出去的那份坐法（票 46 的定型，票 73 改成按座位各自成立）：
    /// 每一席的人格与模板取**那一席本局定型的那一版**，其余字段取面板现在的值。
    /// 某一席还没定型（这一局一次都没被问过）时就是面板上那一条。
    let private effective (live: LiveTable) : SeatingPlan = {
        live.Seating with
            Seats =
                live.Seating.Seats
                |> List.mapi (fun index binding ->
                    match List.tryItem index live.Pinned |> Option.flatten with
                    | None -> binding
                    | Some pinned -> binding |> Rendering.applyTo pinned)
    }

    /// 四席的定型一起松开（重开一桌 / 开下一局）。**长度跟着座位数走**：
    /// 写死 `[ None; None; None; None ]` 会在三麻上多出一席。
    let private loosened (live: LiveTable) : Rendering option list =
        live.Seating.Seats |> List.map (fun _ -> None)

    /// 四席的状态线一起归零（重开一桌）。**长度跟着座位数走**，与 `loosened` 同一个理由。
    let private idled (live: LiveTable) : AgentStatus list =
        live.Seating.Seats |> List.map (fun _ -> AgentStatus.Idle)


    /// 强 AI 基线拉不动时那一席退回的自带 bot（票 92；ADR-0006 边界 2）。
    ///
    /// **取「有主见」而不是均匀随机**：退回一个能和就和、听牌就立直的选手，
    /// 那一席才还像个对手；均匀随机会把整桌牌的手感改掉，而人只是没下载到一份资产。
    let private baselineFallbackBot = Bot.Opinionated

    /// 强 AI 基线拉不动时，把那几席换成自带 bot（ADR-0006 边界 2：**它是可选依赖，不是单点**）。
    ///
    /// **换在配桌这一层而不是“每手兵底”**：牌谱里那一列 `names` 因此说的是实话
    /// ——真正把这几手打出来的就是 `opinionated`。写着 `baseline` 而实际在打的是 bot，
    /// 就是把一份可分享物写成了假话（同 `SeatingPlan.nameplates` 那条判据）。
    let private degraded (status: BaselineStatus) (player: SeatPlayer) : SeatPlayer =
        match status, player with
        | BaselineStatus.Unavailable _, SeatPlayer.Baseline -> SeatPlayer.Bot baselineFallbackBot
        | _ -> player

    /// 这一桌的配桌：四席各自绑定的那个选手（票 73）。
    /// **推导出来而不存下来**：坐法只有 `SeatingPlan` 这一份，不会与第二份对不上。
    ///
    /// **强 AI 基线拉不动时就在这一步退回 bot**（票 92）：往下每一处读配桌的地方
    /// （推进、导出牌谱、分享链接）因此一行都不必知道这件事。
    let private rosterFor (ruleset: Ruleset) (live: LiveTable) : Roster =
        let roster = SeatingPlan.roster ruleset (effective live)

        {
            roster with
                Seats = roster.Seats |> List.map (degraded live.Baseline)
        }

    /// 此刻还有**待答而没问出去**的模型席吗（票 88）。
    ///
    /// **真人在想的时候，别席的问话照发**：同一轮响应里他与模型席各答各的，
    /// 引擎收齐才按优先级裁决（`drain`）。不这么分的话，「等他点那一下」会把别席的问话
    /// 堵在他后面，一轮的墙钟变成「他想的时间 + 最慢那一席」——而那正是票 74 要治的病。
    ///
    /// **只数模型席**：bot 席与真人席本来就只在轮到头一家时才落（`drain` / `decide`），
    /// 把它们数进来会让定时器在他头上空转。
    let private unasked (roster: Roster) (live: LiveTable) : bool =
        match live.Table with
        | Error _ -> false
        | Ok table ->
            let flying = live.Awaiting |> List.map Awaiting.seat
            let llm = Roster.llmSeats roster |> List.map fst

            // 强 AI 基线席同理（票 92）：资产已经拉到、而那一席还没问出去，就不算干等。
            // **还在拉的那段不算**：那时 `step` 什么也做不了，定时器只会空转
            // ——把牌桌重新开动的是 `BaselineLoaded`。
            let consulting = live.Consulting |> List.map Consult.seat

            let baseline =
                match live.Baseline with
                | BaselineStatus.Ready _ -> Roster.baselineSeats roster
                | BaselineStatus.Absent
                | BaselineStatus.Loading
                | BaselineStatus.Unavailable _ -> []

            Table.pendings table
            |> List.exists (fun choice ->
                (List.contains choice.Seat llm && not (List.contains choice.Seat flying))
                || (List.contains choice.Seat baseline && not (List.contains choice.Seat consulting)))

    /// 这一桌此刻**只能干等**人（票 74 的回执、票 87 的那一下点击、票 92 的那几 MB）。
    ///
    /// **几种等待在这一处合成一条判据**：定时器不续、牌桌不再往下推。
    /// 不合在一处的话，「等真人」会漏掉其中一处而让牌桌在他头上空转。
    ///
    /// **还有没问出去的模型席就不算干等**（票 88）：那一记定时器还得转，
    /// 把该问的问出去（`step`）；问完下一记就停了，因此不会空转。
    /// 轮到强 AI 基线，而那几 MB 还在路上（票 92）。**它也是干等**：定时器再转也
    /// 无事可做，重新把牌桌开动的是 `BaselineLoaded`。
    let private stalled (roster: Roster) (live: LiveTable) : bool =
        match live.Baseline, live.Table with
        | BaselineStatus.Loading, Ok table ->
            let seats = Roster.baselineSeats roster

            Table.pendings table
            |> List.exists (fun choice -> List.contains choice.Seat seats)
        | _ -> false

    let private waiting (roster: Roster) (live: LiveTable) : bool =
        (not (List.isEmpty live.Awaiting)
         || not (List.isEmpty live.Consulting)
         || Option.isSome (handOf live)
         || stalled roster live)
        && not (unasked roster live)

    /// 续一记定时器——**除非这一桌只能干等**（在飞的回执，或者正等真人点那一下）。
    /// 等着的时候定时器只会把牌桌空转一遍；
    /// 那一手由 `Answered`（模型）或 `HumanPlayed`（真人）接着开动。
    /// **「还有在飞的就不续」只管定时器**：该问的席照问（票 74，见 `step`）。
    ///
    /// **拨座位那一下也走这里**（票 111 的 `roused`）：换人当场撤票之后，
    /// 那一席就又该被问了，而在那之前没有任何一记定时器去问它。
    let private tick (model: TableModel) (playback: Playback) : Cmd<TableMsg> =
        match live model with
        | Some live when waiting (rosterFor model.Ruleset live) live -> Cmd.none
        | Some _
        | None -> schedule playback

    /// 这一桌的配桌（谁坐哪里）；**回放没有配桌**，那时是 None。
    ///
    /// 回放里没人要出手（动作全在牌谱里），编一份配桌出来只会被人当真——
    /// 牌谱开头那条 `start_kyoku` 前面的 `names` 是**录下来的**，不是这一桌推导出来的。
    ///
    /// **公开的**：页面逻辑的用例要问「这一桌到底谁坐哪里」，在用例里拄一份同样的推导
    /// 只会与这里漂（票 42 前它真的漂过一份，在 `PaifuExportTests`）。
    let rosterOf (model: TableModel) : Roster option =
        live model |> Option.map (rosterFor model.Ruleset)

    /// 这一席此刻真正会用的那份配置（票 73）；坐的是自带 bot（或者回放）时是 None。
    ///
    /// **公开的**：“这一席到底拿哪份档案、哪一档脚手架在打”只有这一处推导，
    /// 票 74（并发）与票 76（四家气泡）与用例读的都是它。
    let seatConfigOf (seat: Seat) (model: TableModel) : LlmSeat option =
        rosterOf model
        |> Option.bind (fun roster ->
            match Roster.playerAt seat roster with
            | SeatPlayer.Llm config -> Some config
            | SeatPlayer.Bot _
            | SeatPlayer.Human
            | SeatPlayer.Baseline -> None)

    /// 名牌上那一句「这一席是谁在打」（票 82），按座位升序；没话可说的那几席是空表。
    ///
    /// **两种来源各有各的真源，因此它在 `Source` 上分岔**：
    /// Live 读配桌（档案名 + 脚手架档位；bot 席是那两档的中文），
    /// 回放读牌谱开头那条 `start_game` 的 `names`（`provider/model`）。
    /// **回放里不编档案名**：那是本机的私人叫法，牌谱里根本没有。
    ///
    /// 读 `live.Seating` 而不是 `effective live`：定型（`Rendering`）定的是人格与模板，
    /// 名牌上那两样（交给谁、哪一档）不在定型里——拨一下当场就该变。
    ///
    /// **公开的**：牌桌上那行与用例读同一个推导。
    /// 各席**在牌谱里那个名字**（`Roster.names` 那一份），按座位升序；回放那一侧是空表。
    ///
    /// **它读的是配桌而不是坐法**（票 92）：强 AI 基线拉不动时那一席已经退回自带 bot
    /// （`degraded`），而牌谱里写的就是**真正把这几手打出来的那一个**。
    /// 页面上那几处 `data-*` 因此与导出的牌谱是同一份真源——两份写法只会漂。
    let seatNames (model: TableModel) : string list =
        rosterOf model |> Option.map Roster.names |> Option.defaultValue []

    /// 强 AI 基线那几 MB 此刻在哪一步（票 92）；回放那一侧恒是 `Absent`（那一页没有配桌）。
    ///
    /// **公开的**：状态线、面板上那枚按钮旁的那句话与页面逻辑的用例读的都是它。
    let baseline (model: TableModel) : BaselineStatus =
        live model
        |> Option.map (fun live -> live.Baseline)
        |> Option.defaultValue BaselineStatus.Absent

    /// 强 AI 基线被兵底代打的那几手（新的在前，票 92）；没有那一席时是空表。
    let baselineTroubles (model: TableModel) : string list =
        live model
        |> Option.map (fun live -> live.BaselineTroubles)
        |> Option.defaultValue []

    let nameplates (model: TableModel) : string list =
        match model.Source with
        // 强 AI 基线拉不动时名牌要说实话（票 92，与「引的那份档案被删了」同一条判据）：
        // 写着强 AI 而实际在打的是自带 bot，那就是句假话。
        | Source.Live live ->
            let plates = SeatingPlan.nameplates live.Seating

            match live.Baseline with
            | BaselineStatus.Unavailable _ ->
                let fallen = SeatingPlan.baselineSeats live.Seating |> List.map Seat.index

                plates
                |> List.mapi (fun index plate ->
                    if List.contains index fallen then
                        Bot.toDisplay baselineFallbackBot
                    else
                        plate)
            | BaselineStatus.Absent
            | BaselineStatus.Loading
            | BaselineStatus.Ready _ -> plates
        | Source.Replay(ReplayTable.Ready(_, _, names)) -> names
        // 还在拉 / 拉不动那两段根本没有牌桌，也就没有名牌。
        | Source.Replay _ -> []

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
    /// **四席里只要有一席欠着就算**（票 73）：那句话说的是“你刚才改的东西本局还没生效”，
    /// 而它对哪一席成立都是同一件事。
    let renderingPending (model: TableModel) : bool =
        match live model with
        | None -> false
        | Some live ->
            live.Pinned
            |> List.mapi (fun index pinned ->
                match pinned, List.tryItem index live.Seating.Seats with
                | Some pinned, Some binding -> pinned <> Rendering.ofBinding binding
                | _ -> false)
            |> List.exists id

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

    /// 复制分享链接（票 78）：这一桌到此刻为止的**棋谱**（`Share.toPayload` 里走
    /// `Paifu.stripAudit`）→ base64url 载荷 → 装进 hash（`Route.shareUrl`）→ 写剪贴板。
    ///
    /// **写完才算数**：两步都是异步的，哪一步砸了都经 `ShareSettled` 回一句中文——
    /// 静静地没复制上，人会把一条不存在的链接发给别人。
    let private shareCmd (roster: Roster) (table: Table) : Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch ->
            let copied (payload: string) : unit =
                (writeClipboard (Route.shareUrl payload))
                    .``then``(fun () -> dispatch (ShareSettled(Ok payload.Length)))
                    .catch (fun error -> dispatch (ShareSettled(Error $"浏览器不让写剪贴板（{error}）")))
                |> ignore

            (Table.paifu roster table |> Share.toPayload)
                .``then``(copied)
                .catch (fun error -> dispatch (ShareSettled(Error $"载荷编不出来（{error}）")))
            |> ignore)

    /// 剪贴板那一趟的回执 → 页面上那句话的三态（票 78）。**阈值判在这里**（纯的），
    /// 因此「8,000 以内算复制成、超过就勝人换 JSON」这条在 dotnet 侧有用例钉着。
    let private settledShare (result: Result<int, string>) : ShareOutcome =
        match result with
        | Error reason -> ShareOutcome.Failed reason
        | Ok chars when chars > ShareOutcome.threshold -> ShareOutcome.Oversized chars
        | Ok chars -> ShareOutcome.Copied chars

    /// 发一次问话。**不用 `Cmd.OfPromise`**：它整段包在 `#if FABLE_COMPILER` 里，
    /// 而这个文件要在 dotnet 上编得过（页面逻辑的用例跑在那边）。
    /// 效果体只在浏览器里执行，dotnet 侧只把它编出来、不跑。
    ///
    /// 回执带上**座位与票号**（票 74）：四席同时在飞时各对各的账，错位的丢。
    let private askCmd (seat: Seat) (ticket: int) (request: AgentRequest) : Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch ->
            let answered (answer: AgentAnswer) =
                dispatch (Answered(seat, ticket, answer))

            (Agent.ask request).``then`` answered |> ignore)

    /// 去拉强 AI 基线那几 MB（票 92；ADR-0006 边界 1）。
    ///
    /// **它只从三处发得出去**：页面初次打开时坐法里就有那一席（`initial`）、
    /// 人把某一席拨到它（`SeatBound`）、以及驱动循环发现轮到它而还没开始拉（`step`）。
    /// 首页与普通对局因此**一个字节都不拉**——那条边界的执行者就是这三个调用点。
    let private baselineCmd: Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch ->
            let loaded (result: Result<int, string>) = dispatch (BaselineLoaded result)
            (Baseline.load ()).``then`` loaded |> ignore)

    /// 问强 AI 基线这一手打什么（票 92）。与 `askCmd` 同形：效果体只在浏览器里执行，
    /// dotnet 侧只把它编出来、不跑。回执带上**座位与票号**，错位的丢。
    let private consultCmd (seat: Seat) (ticket: int) (package: DecisionPackage) : Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch ->
            let decided (answer: BaselineAnswer) =
                dispatch (BaselineDecided(seat, ticket, answer))

            (Baseline.ask package).``then`` decided |> ignore)

    /// 这一桌里真有强 AI 基线席时去拉那几 MB（票 92；ADR-0006 边界 1 的执行者）。
    ///
    /// **没有那一席就一个字节都不拉**：首页根本没有 `LiveTable`，普通对局这一表是空的
    /// ——两种情形都停在 `Absent`，而闸门量的正是它（网络请求计数为 0）。
    ///
    /// **已经在拉 / 已经拉到的不重拉**：整桌共用一份资产，四席都拨到它也只下一次。
    /// **拉不动那一档重拉**：人重新拨一下那枚按钮就是「再试一次」，
    /// 而自动重试不在这里（`step` 只从 `Absent` 进得去，因此不会变成重试风暴）。
    let private started (live: LiveTable) : LiveTable * Cmd<TableMsg> =
        if List.isEmpty (SeatingPlan.baselineSeats live.Seating) then
            live, Cmd.none
        else
            match live.Baseline with
            | BaselineStatus.Loading
            | BaselineStatus.Ready _ -> live, Cmd.none
            | BaselineStatus.Absent
            | BaselineStatus.Unavailable _ ->
                {
                    live with
                        Baseline = BaselineStatus.Loading
                },
                baselineCmd

    /// 「已等一秒」的钟（票 74）：一秒后发一条 `Waited`，那一票还在飞就再续一记。
    /// **一票一条链**：回执落定（或整桌重开）之后 `Waited` 找不到那一票，链就自己断了，
    /// 不会像 `setInterval` 那样越积越多。
    let private waitCmd (ticket: int) : Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch -> JS.setTimeout (fun () -> dispatch (Waited ticket)) 1000 |> ignore)

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
        | None -> played, AgentStatus.Spoke(answer.Reason, answer.LatencyMs)
        | Some reason -> played, AgentStatus.Troubled reason

    /// 这一份问话还对得上此刻的局面吗（票 92）。
    ///
    /// **判据是「引擎此刻给这一席的合法动作集与包里那一列逐条相同」**：包里的 id 就是
    /// 合法动作集的下标（`DecisionPackage.forSeat`），动作集变了同一个 id 就是另一条动作。
    /// 拿「座位还在不在待答之列」当判据是不够的——响应阶段散了又因为别的缘故轮回到它，
    /// 那时它在待答之列，而包里的号早已不是这一份。
    let private stillCurrent (table: Table) (package: DecisionPackage) : bool =
        Table.pendings table
        |> List.tryFind (fun choice -> choice.Seat = DecisionPackage.seat package)
        |> Option.map (fun choice -> choice.Actions = (DecisionPackage.options package |> List.map ActionOption.action))
        |> Option.defaultValue false

    /// 强 AI 基线的回执 → 落子（票 92）。**不留决策记录**（走 `Table.apply` 而不是
    /// `applyRecorded`）：它没有可审计的推理——没有 thinking、没有一句话理由、
    /// 没有 token 账单。于是气泡（`bubbles` 读 `Table.Decisions`）与账单行（`Table.usage`）
    /// **在结构上就不会为它长出一行**——不是显示一个空气泡或者「0 tok」（票 92 的要害）。
    ///
    /// **牌谱格式因此一个字段都不必加**：它在牌谱里就是 `names` 里那一个 `baseline`。
    ///
    /// 交不出来那一手（id 不在包里、翻译不动、wasm 抛了）就由 `Fallback.action` 代打，
    /// 并把原因记在 `BaselineTroubles` 上：**兵底不许静默替换**（票 23 那条规矩）。
    let private settleBaseline (consult: Consult) (answer: BaselineAnswer) (table: Table) : Table * string option =
        match
            answer.ActionId
            |> Option.bind (fun id -> DecisionPackage.tryAction id consult.Package)
        with
        | Some action -> Table.apply action table, None
        | None ->
            let reason =
                answer.Failure
                |> Option.defaultValue $"强 AI 基线给回的动作 id 不在这一包里（{answer.ActionId}）"

            // 裸奔档的兵底（摸切 / 「过」）：它没有脚手架档位，而 Assisted 档那一套是给
            // 「看不懂牌的模型」准备的——替一个强 AI 代打时宁可取最保守的那一手。
            Table.apply (Fallback.action ScaffoldTier.Bare consult.Package) table, Some reason

    /// **把在飞的那几次问话从表上摘下来记进账**——三条路共用的那一段
    /// （票 108 的剪枝、票 109 的**撤票**与**开下一局**）。
    ///
    /// **三条路只差两个参数**：挑哪几份（`doomed`）、为什么（`cause`）。各写一遍的话，
    /// 「回执已经到了的那几份当场就知道花了多少」与「强 AI 基线那几份没有账单」
    /// 这两件事就要在三处各记得一次。
    ///
    /// **摘下来的不是丢掉，是记一笔账**（`Table.voidAsk`）：那一次问话真的调了 provider、
    /// 真的计了费。**但它不留一条声称落了子的 `DecisionRecord`**——那一手没有发生；
    /// 手序不动、事件流不动。回执还在飞时 token 那几个数还不知道，
    /// 等它回来在 `Answered` 里补（`Table.creditVoid`）。
    ///
    /// **那几席就此重新可问**：`step` 看的是 `Awaiting` / `Consulting`（`flying`）。
    let private harvested
        (cause: VoidCause)
        (doomed: DecisionPackage -> bool)
        (table: Table)
        (live: LiveTable)
        : LiveTable * Table =
        let asking, deadAsks =
            live.Awaiting |> List.partition (fun each -> not (doomed each.Package))

        let consulting, deadConsults =
            live.Consulting |> List.partition (fun each -> not (doomed each.Package))

        // 作废发生在哪一手之后：`voidAsk` 只往账上追加，手序不动，因此这一格整批同一个数。
        // **跨局时它就是「补到哪一局的账上」那个锚**（票 109）：`Table.Turns` 跨局累计，
        // 开下一局不把它归零，于是一笔作废永远记在**它真的发生的那一刻**上。
        let turn = table.Turns

        let voided (ticket: int) (seat: Seat) (usage: Usage option) : VoidedAsk = {
            Ticket = ticket
            Turn = turn
            Seat = seat
            Cause = cause
            Usage = usage
        }

        // 回执已经回来了的那几份，当场就知道花了多少；还在飞的那几份等它回来再补（`Answered`）。
        //
        // 强 AI 基线那几份记在**同一本账**上，**只是它没有账单**（那一席在本机跑）：
        // 分成两本的话，「这一桌剪掉过几次问话」就要从两处各数一遍再相加，而那两份计数会漂。
        let harvest =
            (deadAsks
             |> List.map (fun each ->
                 let usage = each.Answer |> Option.bind (fun answer -> answer.Usage)
                 voided each.Ticket (Awaiting.seat each) usage))
            @ (deadConsults
               |> List.map (fun each -> voided each.Ticket (Consult.seat each) None))

        let table =
            (table, harvest) ||> List.fold (fun table ask -> Table.voidAsk ask table)

        {
            live with
                Table = Ok table
                Awaiting = asking
                Consulting = consulting
        },
        table

    /// **把过期的那几份问话剪掉**（票 92 立在强 AI 基线那一侧，票 108 补上模型席这一侧）。
    ///
    /// **一份在飞的问话会过期**，是因为牌桌会绕过它往前走：人在它在飞时把那一席
    /// 拨给了自己（`SeatBound`），于是那几手由真人打了出去（`HumanPlayed` / `HumanTicked`
    /// 走的是 `handOf`，不经 `drain` 那条顺序）。留着它有两个下场，**两个都真演过**：
    ///
    /// - `drain` **按座位**找在飞的那一份，于是这一席下一次被问到时拿**旧包**落子：
    ///   同一个 id 指的已经是另一条动作，引擎当场拒（`Table.Fault`），牌桌就此停死；
    /// - 在那之前 `waiting` 一直为真（`Awaiting` 非空），定时器不续，牌桌停在那儿不动。
    ///
    /// **剔下来的不是丢掉，是记一笔账**（`Table.voidAsk`）：那一次问话真的调了 provider、
    /// 真的计了费。**但它不留一条声称落了子的 `DecisionRecord`**——那一手没有发生。
    /// 回执还在飞时 token 那几个数还不知道，等它回来在 `Answered` 里补（`Table.creditVoid`）。
    ///
    /// **那一席就此重新可问**：`step` 看的是 `Awaiting` / `Consulting`（`flying`），
    /// 剔掉之后轮到它就再问一次，拿的是此刻那份新包。
    ///
    /// **它是兜底，不是语义**（票 109）：它只看得见「合法动作集变了没有」，
    /// 而**拨座位那一刻动作集往往没变**——那一条要由 `rebound` 当场撤下来。
    let private swept (table: Table) (live: LiveTable) : LiveTable * Table =
        let expired (package: DecisionPackage) = not (stillCurrent table package)
        harvested VoidCause.Expired expired table live

    /// **现在坐这一席的是谁**（名牌上那句话）。
    ///
    /// **取自坐法那一份唯一的出处**（`SeatingPlan.nameplates`），不在这里另拼一句：
    /// 两份写法迟早对不上，而账上那句话与牌桌上那枚名牌说的应当是同一件事。
    let private taker (seat: Seat) (seating: SeatingPlan) : string =
        SeatingPlan.nameplates seating
        |> Seat.tryItem seat
        // 座位越界（不该发生）：宁可只报座位号，也不编一个选手名出来。
        |> Option.defaultValue $"座位 {Seat.index seat}"

    /// **人把某一席拨给了别人：那一席在飞的那一票当场撤回**（票 109）。
    ///
    /// **这是语义，不是等它回来再剪**：`swept` 那道按「合法动作集是不是还是当下」判，
    /// 而**拨座位那一刻牌桌根本没有往前走**（动作集一个字节都没变），它因此剪不掉；
    /// 于是回执赶在他出手之前回来时，**模型替坐在桌边的那个人打了一手**（票 108 §⑦ 第 4 条）。
    ///
    /// **拨给别的模型也一样作废**：provider / key / 人格都可能换了，
    /// 旧回执是**上一份配置**答的——它答的不是这一席此刻的那个人，
    /// 而牌谱开头那一列 `names` 是导出那一刻由**此刻的配桌**推导出来的
    /// （`Table.events` → `Roster.names`，不是逐手录下的）：那一手因此会记在新那一位名下。
    ///
    /// `taker` 是现在坐这儿的那一位（名牌上那句话）：**不许静默作废**，
    /// 人要从账上那句话里读得出「拨给了谁」。
    let private rebound (seat: Seat) (taker: string) (live: LiveTable) : LiveTable =
        match live.Table with
        // 牌桌都开不起来时一份在飞的问话都没有（`step` 要 `Ok` 才问得出去）：没有事情发生。
        | Error _ -> live
        | Ok table ->
            let his (package: DecisionPackage) = DecisionPackage.seat package = seat
            harvested (VoidCause.Rebound taker) his table live |> fst

    /// **开下一局：在飞的那几次问话作废，而不是从账上消失**（票 109）。
    ///
    /// **名字取自「翻篇」**（同 `swept` 是「剪」、`rebound` 是「换人」）：
    /// 这一局连同它在飞的那几次问话一起翻过去了。
    ///
    /// 票 108 把这一处交了回来：那时它写的是 `Awaiting = []` / `Consulting = []`，
    /// **同一张牌桌、同一本账**（`Table.usage` 跨局累计），那一票的钱就此蒸发。
    ///
    /// **口径（票 109 判的）**：开局把在飞的问话作废，**算花掉的钱**——
    /// 钱真的付了（provider 调过、token 计过费），而账单报的是**花掉的总额**（票 79 / 108 的口径）。
    /// 否则同一笔花销算不算数，取决于「哪一条路先发现它过期」——而人看不见那件事。
    ///
    /// **无条件作废，不问 `stillCurrent`**：开下一局就是「牌桌把在飞的一切都走过去了」。
    /// 改成有条件的话会多出一条路：一份旧包活过局界、在下一局里落了子。
    let private turned (live: LiveTable) : LiveTable =
        match live.Table with
        // 同 `rebound`：牌桌开不起来时一份在飞的问话都没有。
        | Error _ -> live
        | Ok table -> harvested VoidCause.NextKyoku (fun _ -> true) table live |> fst

    /// 已经答上来的回执，按引擎问答的正序落下去（票 74）。
    ///
    /// **落子顺序是引擎待答的头一家，不是回执到达的先后**：回放重建响应阶段就是按
    /// 「每次取头一家」重建的（`Table.pending` 那段注释），到达顺序落子会让决策记录的
    /// 手序号与回放逐帧对不上——而到达顺序不归任何人管。先回来的回执在 `Awaiting`
    /// 里等一会儿；**引擎收齐才裁决，这一等不改整轮的墙钟**（恒是最慢那一席）。
    ///
    /// 排在头一家的 bot 席也在这里落（`Demand.Ready`）：它「当场就答得出」，
    /// 但**落**同样要守这个顺序。**`Awaiting` 空了就停**——余下的待答席（若有）
    /// 由下一记定时器接着问，一步的粒度因此与从前相同。
    ///
    /// **头一家是真人时停在这里**（票 87 开的那道缝，票 88 接上）：票 87 这里还有一支
    /// 「响应阶段替他过」（`handed`），票 88 把吃碰杠、荣和与「过」都做成了真按钮，
    /// 那一支因此**整个没了**——两种轮到合成一条路：把包摆在页面上等一次点击。
    /// 于是这一处只剩一句「原样返回」，不再单独立一个函数：
    /// **“停下来等他”不写进状态**（`handOf` 现问这一刻的局面就知道轮到他了，
    /// 于是 `waiting` / `step` 都停住），而他真按下去的那一下走 `HumanPlayed`。
    let rec private drain (roster: Roster) (live: LiveTable) : LiveTable =
        if List.isEmpty live.Awaiting && List.isEmpty live.Consulting then
            live
        else
            match live.Table with
            | Error _ -> live
            | Ok current ->
                // **先把过期的那几份问话剪掉**（票 92 / 108，两侧逐字同一条判据）：
                // 剔下来的记成一笔「花了钱、没落子」，而不是丢掉（理由写在 `swept` 上）。
                let live, table = swept current live

                match Table.pending table with
                // 走不到：在飞的都是这一轮的待答席，收齐之前引擎不会翻篇。真走到就停下。
                | None -> live
                | Some choice ->
                    match live.Awaiting |> List.tryFind (fun each -> Awaiting.seat each = choice.Seat) with
                    | Some entry ->
                        match entry.Answer with
                        // 头一家的回执还在飞：停在这里等它（后面各家的回执在各自的 `Answer` 里躺着）。
                        | None -> live
                        | Some answer ->
                            let played, status = settle entry answer table

                            drain roster {
                                live with
                                    Table = Ok played
                                    Awaiting = live.Awaiting |> List.filter (fun each -> each.Ticket <> entry.Ticket)
                                    Agent = live.Agent |> Seat.mapAt choice.Seat (fun _ -> status)
                            }
                    | None ->
                        match Table.decideFor choice.Seat roster table with
                        | Some(Demand.Ready(action, players)) ->
                            drain roster {
                                live with
                                    Table = Ok(Table.apply action { table with Players = players })
                            }
                        // 头一家是强 AI 基线（票 92）：回执回来了就落下去，还在飞就停在这里。
                        // **与模型席逐字同一条规矩**：落子沿引擎问答的正序，不按到达先后。
                        | Some(Demand.Baseline _) ->
                            match live.Consulting |> List.tryFind (fun each -> Consult.seat each = choice.Seat) with
                            | Some({ Answer = Some answer } as entry) ->
                                let played, trouble = settleBaseline entry answer table

                                drain roster {
                                    live with
                                        Table = Ok played
                                        Consulting =
                                            live.Consulting |> List.filter (fun each -> each.Ticket <> entry.Ticket)
                                        BaselineTroubles = Option.toList trouble @ live.BaselineTroubles
                                }
                            // 回执还在飞，或者还没问出去（`step` 会把它问出去）：停在这里。
                            | Some _
                            | None -> live
                        // 头一家是真人（票 87/88）：**停在这里等那一下点击**。
                        // 后面那几席已经答上来的回执就在 `Awaiting` 里躺着，
                        // 他一点完这一句接着把它们按引擎的顺序落下去——**谁先答不改裁决**。
                        | Some(Demand.Human _) -> live
                        // 头一家是还没被问出去的模型席：停下，`step` 会把它问出去。
                        | Some(Demand.Asked _)
                        | None -> live

    /// 推进一手。**这就是驱动循环的一步**：把此刻**所有**待答而还没在飞的席问出去（票 74）。
    ///
    /// 摸牌后只有一家，与从前无异；**响应阶段可能同时有好几家**——模型席各发一趟请求，
    /// bot 席的答复按引擎的顺序落（`drain`）。**不再因为一席在飞就不问第二席**，
    /// 但同一席在飞时绝不问第二次（票 23 那条用例仍然钉着）。
    ///
    /// 决策包全部取自这一刻的局面：响应阶段里某席的合法动作集与决策包**不因别席先答而变**
    /// （`GameStateTests` 那条地基断言钉着），因此这些包在别席的答复落定之后仍然换得回动作。
    let private step (ruleset: Ruleset) (live: LiveTable) : LiveTable * Cmd<TableMsg> =
        match live.Table with
        | Error _ -> live, Cmd.none
        | Ok table ->
            let roster = rosterFor ruleset live

            // 已经问出去、还没答上来的那几席（模型席与强 AI 基线席各一本账，但**同一张表**）：
            // **同一席在飞时绝不问第二次**（票 23 那条用例钉着模型席这一半）。
            // 漏掉强 AI 基线那一半的下场是：同一手被问两次、两份回执各落一个动作，
            // 第二个当场被引擎拒（「现在没有可响应的牌」）——票 92 真踩到过。
            let flying =
                (live.Awaiting |> List.map Awaiting.seat)
                @ (live.Consulting |> List.map Consult.seat)

            let asked, cmds =
                ((live, []), Table.pendings table)
                ||> List.fold (fun (live, cmds) choice ->
                    if List.contains choice.Seat flying then
                        live, cmds
                    else
                        match Table.decideFor choice.Seat roster table with
                        // 强 AI 基线席（票 92）：与模型席同一趟问出去——同一轮响应里它们各答各的，
                        // 引擎收齐才按优先级裁决（`drain`）。
                        | Some(Demand.Baseline package) ->
                            match live.Baseline with
                            | BaselineStatus.Ready _ ->
                                let ticket = live.Ticket + 1

                                {
                                    live with
                                        Ticket = ticket
                                        Consulting =
                                            live.Consulting
                                            @ [
                                                {
                                                    Ticket = ticket
                                                    Package = package
                                                    Answer = None
                                                }
                                            ]
                                },
                                consultCmd choice.Seat ticket package :: cmds
                            // 走不到（`initial` / `SeatBound` 都已经把它推到 `Loading`）；真走到了就当场去拉，
                            // **而不是在这里干等一个永远不来的资产**。`Absent` 只进得一次 `Loading`，
                            // 因此它不会变成一条重试风暴。
                            | BaselineStatus.Absent ->
                                {
                                    live with
                                        Baseline = BaselineStatus.Loading
                                },
                                baselineCmd :: cmds
                            // 还在路上：什么都不做（`waiting` 把定时器停了，`BaselineLoaded` 重新开动）。
                            // 拉不动那一档走不到：配桌那一层已经把这一席换成自带 bot 了（`degraded`）。
                            | BaselineStatus.Loading
                            | BaselineStatus.Unavailable _ -> live, cmds
                        | Some(Demand.Asked(package, config)) ->
                            let ticket = live.Ticket + 1
                            let seat = choice.Seat

                            {
                                live with
                                    Ticket = ticket
                                    // 这一席这一局的头一次问话把它的人格与模板定住（票 46；票 73 改成一席一份）：
                                    // 之后再改只落到面板，本局这一席发出去的字节不再变。
                                    // **只定住被问的那一席**：别家本局可能还没开过口，那几席的两格仍改得动。
                                    // 盖回去的就是 `config` 里那两格（它本来就是定型后的那一份），因此重盖幂等。
                                    Pinned = live.Pinned |> Seat.mapAt seat (fun _ -> Some(Rendering.ofSeat config))
                                    Awaiting =
                                        live.Awaiting
                                        @ [
                                            {
                                                Ticket = ticket
                                                Package = package
                                                Config = config
                                                Answer = None
                                                WaitedSeconds = 0
                                            }
                                        ]
                                    Agent = live.Agent |> Seat.mapAt seat (fun _ -> AgentStatus.Asking)
                            },
                            askCmd seat ticket {
                                Package = package
                                Seat = config
                                RetryLimit = Agent.retryLimit
                            }
                            :: waitCmd ticket
                            :: cmds
                        // bot 席与真人席都不在这一趟里落（那一下得守引擎的顺序）：交给下面那一段。
                        | Some(Demand.Ready _)
                        | Some(Demand.Human _)
                        | None -> live, cmds)

            if List.isEmpty asked.Awaiting && List.isEmpty asked.Consulting then
                // 没有在飞的问话：与从前一样一步落一手（不许一步把整轮乃至整局跑完）。
                match Table.decide roster table with
                | Some(Demand.Ready(action, players)) ->
                    {
                        asked with
                            Table = Ok(Table.apply action { table with Players = players })
                    },
                    Cmd.none
                // 头一家是真人（票 87/88）：**这一步推不动**——包就摆在页面上（`handOf`），
                // 吃碰杠、荣和与「过」各是一枚按钮，它们都走 `HumanPlayed`。
                // **但上面那一趟照旧跑完了**：同一轮里待答的模型席已经问出去了
                // （票 88 的并发要求：真人在想时让模型席先答）。
                | Some(Demand.Human _) -> asked, Cmd.none
                // 走不到（待答的模型席与强 AI 基线席在上面那趟已经问出去了）；
                // 这一局已终、以及资产还在路上那段也落在这里。
                | Some(Demand.Asked _)
                | Some(Demand.Baseline _)
                | None -> asked, Cmd.none
            else
                drain roster asked, Cmd.batch cmds

    /// **真人那一手落进引擎**（票 87/88 他自己点的那一下、票 89 时限到点代他打的那一手
    /// 走的是这同一条）。**两条路合在这里而不是各写一遍**：牌谱里那一手因此逐字同形
    /// （都是 `Table.apply`，都不留决策记录）——超时那一手在回放里与手动那一手分不出来，
    /// 而那正是票面要的（他就是那一席的选手，平台只是代他按了一下）。
    ///
    /// `note` 是这一手要不要记一笔：他按的那几次「过」与时限代打的那几手记在**同一本账**上
    /// （`HumanPass.AutoPlayed` 分开两种）；他自己打出去的那几手不记（那就是对局本身）。
    let private landed (action: Action) (note: HumanPass option) (table: Table) (live: LiveTable) : LiveTable = {
        live with
            Table = Ok(Table.apply action table)
            Passed = Option.toList note @ live.Passed
    }

    /// 落完一手之后：接着播还是停下来。
    ///
    /// **等回执（或等真人点那一下）的那段不续定时器**（但仍然是 `Playing`）：
    /// 定时器只会把牌桌空转一遍，真正把它接着开动的是 `Answered` / `HumanPlayed`
    /// （票 111 起还有第三种：拨座位当场撤票之后由 `roused` 接着推）。
    /// **真人因此与模型席完全同级**：他出完手，这一桌照旧按播放状态往下走（票 87）。
    /// 一局终了也停下来：结算面板正摆在那里。
    let private resume (cmd: Cmd<TableMsg>) (model: TableModel) : TableModel * Cmd<TableMsg> =
        match live model with
        | Some live when waiting (rosterFor model.Ruleset live) live -> model, cmd
        | _ when canAdvance model -> model, Cmd.batch [ cmd; schedule model.Playback ]
        | _ ->
            {
                model with
                    Playback = Playback.pause model.Playback
            },
            cmd

    /// **拨完座位之后，把牌桌按它此刻的播放状态重新推一记**（票 111）。
    ///
    /// 票 109 让「换人」当场撤票（`rebound`），可 `SeatBound` **从来不发定时器**：
    /// 撤完之后没有任何东西去重新问那一席，于是**人在自动播放中途拨一下座位，
    /// 这一桌就停在那儿等他再按一下**（票 109 报告 §⑦ 第 5 条；浏览器那一趟当时是靠
    /// 「单步 1 下」绕过去的）——「拨一下座位」不该变成「顺手把牌桌按停了」。
    ///
    /// **它不改播放状态**（`Playback.resumed` 收的就是此刻那个 bool）：停着的桌拨完还是停着
    /// （`schedule` 在暂停时什么都不发）——**「撤票要重开动」不等于「拨座位就开始播放」**，
    /// 阴性对照量的正是这一条。
    ///
    /// **非换一个世代不可**：拨那一下时在飞的那记定时器（若有）必须作废，
    /// 否则它与这里新发的一记一起被认下，牌桌从此**双倍速**走（票 78 按红过一次的那个坑）。
    /// 世代号只有 `Playback` 那几条迁移换得了，而这一下要的正是「播放状态原样、世代换新」。
    ///
    /// **轮到真人时它一记都不发**（`tick` 里那道 `waiting`）：轮到他就等他，
    /// 不许替他打（票 87/88/89）——接着开动这一桌的是他按下去的那一下（`HumanPlayed`）。
    let private roused (model: TableModel) (cmd: Cmd<TableMsg>) : TableModel * Cmd<TableMsg> =
        match model.Source with
        // 回放那一侧根本没有配桌可拨（`SeatBound` 在那边本来就没有事情发生）：
        // 在那一侧续定时器反而会把正在自动播的那份回放推成双倍速。
        | Source.Replay _ -> model, cmd
        | Source.Live _ ->
            let playback = Playback.resumed model.Playback.Playing model.Playback
            let woken = { model with Playback = playback }
            woken, Cmd.batch [ cmd; tick woken playback ]

    // ---- 回放（票 71） ----

    /// 播一帧：帧号 +1，**播到末帧就停在那儿**（结算面板与终局精算正摆在上面）。
    ///
    /// **它不 fold、不判规则**：帧早在 `DemoLoaded` 那一刻一次 fold 好了（`Table.replay`），
    /// 这里只动一个整数。时间轴（拖动与逐事件步进）是票 75 的活，本票只顺着播。
    let private replayTick
        (frames: Table list)
        (names: string list)
        (cursor: int)
        (model: TableModel)
        : TableModel * Cmd<TableMsg> =
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
                    Source = Source.Replay(ReplayTable.Ready(frames, cursor + 1, names))
            }

            if cursor + 1 >= last then
                {
                    played with
                        Playback = Playback.pause played.Playback
                },
                Cmd.none
            else
                played, schedule played.Playback

    /// 一份牌谱 fold 成逐帧并当场开播——**Demo、分享链接、导入 JSON 三个来源共用这一段**
    /// （票 71/78）：fold 只有 `Table.replay` 一条路，三个来源只差「回放不动」那句话的前缀。
    /// 规则集换成牌谱自带的那一份（ADR-0004），档速回到 Demo 那一档（新牌谱从默认节奏起播）。
    ///
    /// **播放接着当前世代往下换**（`Playback.restart`）：导入发生在旧回放还在自动播的时候，
    /// 世代号回到 0 会让在飞的那记定时器与新发的一起被认下——牌桌从此双倍速走。
    let private replayStarted
        (stuck: string -> string)
        (paifu: Paifu)
        (model: TableModel)
        : Result<TableModel * Cmd<TableMsg>, string> =
        match Table.replay paifu with
        | Error reason -> Error(stuck reason)
        | Ok frames ->
            let playback = Playback.restart demoSpeed model.Playback

            Ok(
                {
                    model with
                        Ruleset = paifu.Ruleset
                        // 名字从牌谱里读（`Table.names`）：回放没有配桌，这是唯一来源（票 82）。
                        Source = Source.Replay(ReplayTable.Ready(frames, 0, Table.names paifu))
                        Playback = playback
                },
                schedule playback
            )

    /// 那份 Demo 牌谱拉回来之后：fold 成逐帧的牌桌，换上牌谱自己的规则集，**当场开播**。
    ///
    /// 拉不动、读不动、回放不动三种失法各留一句中文（`ReplayTable.Failed`）：
    /// 首页不许白屏，人得知道是站点的资产没部署全还是那份牌谱太新／太旧。
    let private demoLoaded (paifu: Result<Paifu, string>) (model: TableModel) : TableModel * Cmd<TableMsg> =
        let started =
            paifu
            |> Result.bind (fun paifu -> replayStarted (fun reason -> $"Demo 牌谱回放不动：{reason}") paifu model)

        match started with
        | Ok next -> next
        | Error reason ->
            {
                model with
                    Source = Source.Replay(ReplayTable.Failed reason)
            },
            Cmd.none

    /// 分享链接里那份牌谱解完之后（票 78）：与 Demo 同一条路。三层失法各有各的中文
    /// （「载荷读不动：」「牌谱读不动：」是 `Share.ofPayload` 分好的两层，
    /// 回放不动的第三层在这里接上），都落在 `ReplayTable.Failed`——带载荷打开的那一屏
    /// 除这份牌谱没有别的可摆，但绝不白屏。
    ///
    /// **Live 那一侧一律无事发生**：过期的载荷回执不许把主持人正打着的一桌轰掉。
    let private sharedLoaded (paifu: Result<Paifu, string>) (model: TableModel) : TableModel * Cmd<TableMsg> =
        match model.Source with
        | Source.Live _ -> model, Cmd.none
        | Source.Replay _ ->
            let started =
                paifu
                |> Result.bind (fun paifu -> replayStarted (fun reason -> $"载荷里那份牌谱回放不动：{reason}") paifu model)

            match started with
            | Ok next -> next
            | Error reason ->
                {
                    model with
                        Source = Source.Replay(ReplayTable.Failed reason)
                },
                Cmd.none

    /// 去读并解挑中的那个文件（票 78）。效果体只在浏览器里执行（同 `askCmd`）；
    /// **解码也在这里而不在 update 里**：`Decode.fromString` 用的是 JS 后端，
    /// 而 update 要在 dotnet 上跑页面逻辑的用例——wire 层的事一律留在边界上
    /// （`Share.ofPayload` / `Demo.paifu` 同一个分工）。
    /// 「牌谱读不动：」前缀照票 77 分好的那层，不另发明；不是 JSON、缺字段
    /// 都落在它后面（引擎的英文诊断，ADR-0001）。
    let private importCmd (file: Browser.Types.File) : Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch ->
            let read (text: string) =
                Decode.fromString Paifu.decoder text
                |> Result.mapError (fun message -> $"牌谱读不动：{message}")
                |> ImportLoaded
                |> dispatch

            let failed (error: obj) =
                dispatch (ImportLoaded(Error $"这个文件读不进来：{error}"))

            (fileText file).``then``(read).catch (failed) |> ignore)

    /// 挑中的那份牌谱读完之后（票 78）。**失败不轰掉正在播的那份回放**：
    /// 原因落在 `ImportFault`（页面旁边说一句），牌桌照旧。三种失法：文件读不进来 /
    /// 牌谱读不动（两种都在 `importCmd` 那侧变成 `Error`）/ 回放推不下去（这里判）。
    let private importLoaded (paifu: Result<Paifu, string>) (model: TableModel) : TableModel * Cmd<TableMsg> =
        match model.Source with
        // 导入入口只在回放那一页上；这条消息到不了 Live，真到了也无事发生。
        | Source.Live _ -> model, Cmd.none
        | Source.Replay _ ->
            let started =
                paifu
                |> Result.bind (fun paifu -> replayStarted (fun reason -> $"牌谱回放不动：{reason}") paifu model)

            match started with
            | Ok(next, cmd) -> { next with ImportFault = None }, cmd
            | Error reason -> { model with ImportFault = Some reason }, Cmd.none

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
        | Source.Replay(ReplayTable.Ready(frames, cursor, _)) ->
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
    let private moveCursor (frame: int) (frames: Table list) (names: string list) (model: TableModel) : TableModel =
        let last = List.length frames - 1

        {
            model with
                Source = Source.Replay(ReplayTable.Ready(frames, frame |> max 0 |> min last, names))
                Playback = Playback.pause model.Playback
        }

    // ---- 思考气泡（票 76） ----

    /// 这一桌坐着真人的是哪一席（票 87）；没人坐、或者这是回放时是 None。
    ///
    /// **至多一席**，而那条不变量的执行者是 `SeatingPlan.soloHuman`（判据 2），不是这里的 `tryHead`。
    ///
    /// 读 `live.Seating` 而不是 `effective live`：定型（`Rendering`）定的是人格与模板，
    /// “谁坐哪里”不在定型里（同 `nameplates`）。
    let humanSeat (model: TableModel) : Seat option =
        live model
        |> Option.bind (fun live -> SeatingPlan.humanSeats live.Seating |> List.tryHead)

    /// 这一桌**配置上**有没有真人坐席（ADR-0003 的 consequence：可见性判据挂在
    /// **对局配置与终局状态**上，不挂在「用户是谁」上——围观者不是权限级别，只是视角）。
    ///
    /// **票 76 埋下它时恒 false，票 87 让它说真话**：从此术语表那句
    /// 「有真人参与时终局前隐藏」（`Thinking Bubble` 词条）真的有人执行了。
    /// **取值器（`bubbles`）与视图一行都没改**——票 76 那句预言坑坑满谷。
    let private humanSeated (model: TableModel) : bool = humanSeat model |> Option.isSome

    /// 气泡此刻解不解锁。有真人在场时**终局前一律不出**，复盘时解锁（spec 的 story 31）：
    /// 两个引数就是那两样判据——**对局配置**（有没有真人）与**终局状态**（这一场打完了没）。
    /// 视角不在其列：切到哪个座位看都不改变它（用例里钉着这一条）。
    ///
    /// **规则本身票 87 一个字没改**（票面明令）：变的只是 `humanSeated` 从恒 false 变成了真的。
    let private unlocked (model: TableModel) (table: Table) : bool =
        not (humanSeated model) || Table.result table |> Option.isSome

    /// 此刻页面**锁在哪一席**上（票 87）；没锁时是 None。
    ///
    /// **判据就是 `unlocked` 的反面**，这里直接读它：气泡与视角锁的本来就是同一件事
    /// ——“桌边坐着一个人，而这一场还没打完”。各写一份就是两处判据，
    /// 而两处判据迟早会漂到“气泡藏了、上帝视角还开着”那一步。
    let private lockedTo (model: TableModel) (table: Table) : Seat option =
        if unlocked model table then None else humanSeat model

    /// 此刻页面锁在哪一席上（票 87）；没锁时是 None。**公开的**：
    /// 视角那一排（`TablePanel.viewpoints`）、曳光弹那一块（`devSurfaceAllowed`）
    /// 与页面逻辑的用例读的都是它。
    let lockedSeat (model: TableModel) : Seat option =
        match shown model with
        | Shown.Board table -> lockedTo model table
        // 还在拉 / 开不了局：根本没有牌桌，也就没有可泄露的东西。
        | Shown.Loading
        | Shown.Fault _ -> None

    /// 这一屏此刻真正在用的那份投影（票 87）。
    ///
    /// **对局中有真人在座时锁死他自家那一席**：上帝视角与别席视角在这一页上
    /// **连值都给不出来**——按钮不在 DOM 里是一道（`TablePanel.viewpoints`），
    /// 这里是另一道：就算有人发一条 `ViewpointPicked God` 进来，牌桌也不会换投影。
    /// **两道锁而不是一道礼貌**：票 81 把视角定成了信息闸门，而闸门不能只靠“页面上没画那枚按钮”。
    ///
    /// **终局后它自己松开**（`lockedTo` 读的就是 `unlocked`）：复盘本来就该看得见四家。
    ///
    /// **公开的，而且这一页上只有它**：牌桌（`Board.ofTable`）、视角那一排与 `reveals`
    /// 读的都是它，而不是 `model.Viewpoint`——后者只是“人上一次拨到哪儿”。
    let viewpoint (model: TableModel) : Viewpoint =
        match lockedSeat model with
        | Some seat -> Viewpoint.Seated seat
        | None -> model.Viewpoint

    /// 曳光弹那一块（`?dev=1`，票 35）此刻给不给开（票 87 堵 22-A）。
    ///
    /// **真人在座、对局还没打完时一律不给**：那一块把原始 mjai 事件印在**同一张文档**里，
    /// 而 `start_kyoku` 带着四家配牌；它的种子输入框又是任填的——把牌桌那个种子敲进去
    /// 就是一条绕过投影的旁路。挂账 22-A 从 M1 记到现在，**受害者今天才出现**。
    ///
    /// **没有真人时照旧开得了**（阴性对照），终局后也回来：判据与视角锁同一条。
    ///
    /// **它不问地址里带没带 `?dev=1`**（那是 `Route.devSurfaceRequested`）：
    /// 这里只答“这一桌允不允许”，因此它是纯的、dotnet 侧的用例铉得住。
    let devSurfaceAllowed (model: TableModel) : bool = lockedSeat model |> Option.isNone

    /// **轮到真人了吗**（票 87 出牌那一手、票 88 加上响应那一手）：那一席此刻的决策包；
    /// 不轮到他时是 None。**签名一个字没改**——响应阶段同样是「一份决策包」。
    ///
    /// **公开的，而且只有这一份**：牌桌（哪几张牌点得动）、牌桌下面那一排按钮、
    /// 真人那一行、驱动循环（`waiting` / `step`）与用例读的是同一个 `handOf`。
    let humanTurn (model: TableModel) : DecisionPackage option = live model |> Option.bind handOf

    /// 这一桌至此刻真人**没有自己打出去的那几手**（新的在前；票 87 开账、票 88 换了语义、
    /// 票 89 把时限代打的那几手也记在里面）；没有真人时是空表。
    ///
    /// **两种靠 `HumanPass.pressed` 分**（不是两个取值器）：页面上那两个计数钩子
    /// （`data-human-passes` / `data-human-expired`）与用例读的都是同一本账的两个滤镜。
    let passes (model: TableModel) : HumanPass list =
        live model |> Option.map (fun live -> live.Passed) |> Option.defaultValue []

    /// 真人那一席拨到的脚手架档位（票 89）；这一桌没有真人时是 None。
    ///
    /// **公开的**：页面上那句「你这一席是哪一档」与用例读同一处推导。
    let humanTier (model: TableModel) : ScaffoldTier option = live model |> Option.bind tierOf

    /// **这一屏此刻给不给得出「要算才有的那几个数」**（票 89）。
    ///
    /// **判据只有这一条**，两个消费点（真人那几行辅助、牌桌上的危险度面板与那一枚开关）
    /// 读的都是它——各写一份就是两处判据，而两处判据迟早漂到「辅助藏了、危险度还摆着」
    /// 那一步（同票 87 把气泡 / 视角 / 曳光弹合成 `unlocked` 一条的理由）。
    ///
    /// **危险度也在里面**：术语表那条「感知 vs 计算」把 Danger 归在 Assisted 一侧
    /// （现物与筋都得从河里推），因此 Bare 坐着一个人时那一块不能拨得出来
    /// ——否则「裸奔」这个对照组靠的只是他自觉不按那一枚。
    ///
    /// **没有真人（或已终局）时恒真**：四家模型那一桌与回放与从前逐字相同
    /// （判据跟着 `lockedSeat` 走：它就是 `unlocked` 的反面）。
    let assists (model: TableModel) : bool =
        match lockedSeat model with
        | None -> true
        | Some _ -> humanTier model |> Option.map HumanScaffold.shows |> Option.defaultValue true

    /// **真人这一手的信息辅助**（票 89 的 story 33）：引擎给这一包算好的那份脚手架；
    /// 裸奔档、不轮到他、或手牌形态读不出来时是 None。
    ///
    /// **辅助渲染的唯一入口**：页面上一行向听 / 有效牌 / 危险度都从它来，
    /// 因此「Bare 什么都不给」是一句只有一处执行人的话（判据 2）。
    ///
    /// **它就是模型看到的那一份**：`DecisionPackage.scaffold` 随包而来，
    /// 而 prompt 尾部那一节读的是同一份包的同一个字段（跨界时由 `Scaffold.encoder` 编出去）
    /// ——**不是两处各算一遍，是同一次调用**。
    let humanScaffold (model: TableModel) : Scaffold option =
        if not (assists model) then
            None
        else
            humanTurn model |> Option.bind DecisionPackage.scaffold

    /// 真人这一手的倒计时（票 89）；不限时或不轮到他时是 None。
    ///
    /// **公开的**：真人那一行上那句「还剩 N 秒」与用例读同一格。
    let humanClock (model: TableModel) : HumanClock option =
        live model |> Option.bind (fun live -> live.Clock)

    /// 真人那一席设的思考时限（秒）；**默认不限时就是 None**（票 89）。
    ///
    /// 它与 `humanClock` 是两件事：这一个是「这一席设了几秒」（拨了就有），
    /// 那一个是「这一手走到哪儿了」（轮到他才有）。
    let humanLimit (model: TableModel) : int option = live model |> Option.bind limitOf

    /// **视角是一道信息闸门**（票 81）：坐在座位 N 上只看得见**那一席**在想什么，
    /// 上帝视角四家全开——与手牌同一条规则（`Board`：坐座视角消费那一席的 `Observation`）。
    ///
    /// 它与 `unlocked` 是**两根正交的轴，AND 关系**：`unlocked` 挂在对局配置与终局状态上
    /// （ADR-0003，M3 真人坐席的地基），这一根挂在**此刻看的是哪份投影**上，两条都满足才显示。
    ///
    /// **回放里终局也不放开**：escape hatch 是上帝视角那一按，不是时间——
    /// 否则「坐到座位 0 看这一场」在终局那一刻自己失效，而回放本来就全是终局之后的事。
    ///
    /// **它不是权限**（ADR-0003：「围观者」不是权限级别，只是视角）：谁都按得了那一下，
    /// 因此判据仍旧不挂在「用户是谁」上——它挂在「你此刻选着用谁的眼睛看」上。
    ///
    /// **公开的，而且只有这一份**：气泡（`bubbles`）与 Agent 那条状态线（`AgentLine`）读的是
    /// 同一个函数。两处各判一遍就是两处判据，而这一票治的正是「状态线漏了气泡治好的那件事」。
    let reveals (model: TableModel) (seat: Seat) : bool =
        // **读的是 `viewpoint model` 而不是 `model.Viewpoint`**（票 87）：后者只是“人上一次拨到哪儿”，
        // 真人在座时它根本不是这一屏在用的那份投影。**规则本身一个字没改。**
        match viewpoint model with
        | Viewpoint.God -> true
        | Viewpoint.Seated viewer -> viewer = seat

    /// 这一桌每一席此刻的气泡（票 76）。**交出去的是一个取值器**（`Seat -> Bubble option`）：
    /// 「在想」那一态按座位各取各的（票 74）：在飞的那几份 `Awaiting` 一席一份，
    /// 已等秒数与上限（72-3 裁决明写的代价）就从那一份上读，不存第二份。
    ///
    /// **数据源只有一处**：「说了什么」与「兜底代打」读的都是这一帧的 `Table.Decisions`
    /// （回放那一侧已经按手序切好，票 71 的 `recordedBy`）——气泡不存第二份。
    /// **不读 `AgentStatus.Spoke` 里那句理由**：那只是同一条记录的另一份拄件，而且它只有最新一手。
    ///
    /// **此刻看得见哪几席由 `reveals` 说了算**（票 81）：坐座视角只剩自家一家。
    /// 看不见就是 `None`，于是**DOM 上根本没有那个气泡元素**（`ThinkingBubble.at`），
    /// 不是拿 CSS 藏起来——与他家手牌同一种做法（投影里根本没那些牌）。
    ///
    /// 一条记录都没有的那几席（bot 席、或分享链接那种棋谱）**恒是 None**：不出气泡。
    let bubbles (model: TableModel) (table: Table) : Seat -> Bubble option =
        // 这一席的问话还在飞吗（回来了还没轮到落也算：这一手还没落定，它仍在「想」那一态里）。
        let asking (seat: Seat) : Bubble option =
            live model
            |> Option.bind (fun live -> live.Awaiting |> List.tryFind (fun each -> Awaiting.seat each = seat))
            |> Option.map (fun each -> Bubble.Thinking(each.WaitedSeconds, Awaiting.limitSeconds each))

        fun seat ->
            // **两根轴是 AND**：视角掩蔽（票 81）× `unlocked`（ADR-0003）。
            // 只在这一处合起来：视图与 17 条用例读的都是这个取值器，在视图里再滤一遍就会长出第二处判据。
            if not (reveals model seat) || not (unlocked model table) then
                None
            else
                // 「在想」压过上一条记录：正在等回执那一刻，旧的理由已经不是它此刻在想的事。
                match asking seat with
                | Some thinking -> Some thinking
                | None ->
                    table.Decisions
                    |> List.tryFindBack (fun record -> record.Seat = seat)
                    |> Option.map (fun record ->
                        match record.Fallback with
                        | Some reason -> Bubble.Troubled(record, reason)
                        | None -> Bubble.Spoke record)

    /// 这份牌谱一条决策记录都没有吗（票 76）。**判据落在整份牌谱上而不是这一帧上**：
    /// 带推理的牌谱第 0 帧同样一条记录都没有，拿帧当判据的话那句话会在开局闪一下。
    ///
    /// **Live 那一侧恒为 false**：那一桌还在打，模型随时可能开口，而「四家都是自带选手」
    /// 这件事 Agent 那一行已经在说了（`AgentLine`）。
    let recordless (model: TableModel) : bool =
        match model.Source with
        | Source.Live _
        | Source.Replay ReplayTable.Loading
        | Source.Replay(ReplayTable.Failed _) -> false
        | Source.Replay(ReplayTable.Ready(frames, _, _)) ->
            // 末帧看得见整份牌谱的记录（`recordedBy` 切的是「手序 < 这一帧手数」）。
            frames
            |> List.tryLast
            |> Option.map (fun frame -> List.isEmpty frame.Decisions)
            |> Option.defaultValue false

    /// 账单行那一句（票 110）。**这一行的数在两种来源下说的是两件事，因此这里分两支**：
    ///
    /// - **Live**：`Table.usage` 报的是**花掉的总额**（票 79 长出那一行账单，「花掉的总额」这句口径是票 108 / 109 立的），
    ///   里面含着那几笔「花了钱、没落子」的作废问话。人是在这一行上按下「导出牌谱」的，
    ///   因此**导出之后账会少一块这件事要在这里说出来**，连同那两个数一起。
    /// - **回放 / 导入**：作废的问话**不进牌谱**（票 110 的判断：它是这一次会话的事实，
    ///   不是这一桌的事实），于是这一行报的是「落了子的那几手的合计」。
    ///   **没说出来的缺失就是骗人**，所以它自己得说出来；而它**说不出有几笔**
    ///   ——牌谱压根没告诉它——因此这一支一个「其中 N 笔」都不编。
    ///
    /// **一笔付了钱的作废都没有时，Live 那一支逐字还是票 108 之前那一句**：没有缺口就不必解释。
    let usageSaid (model: TableModel) (table: Table) : string =
        let said = "这一桌累计：" + Usage.toDisplay (Table.usage table)

        match model.Source with
        | Source.Live _ ->
            match table |> Table.paidVoids |> List.length with
            // 一笔付了钱的作废都没有：这一行的数就是那几手的合计，导出之后一分不少。
            | 0 -> said
            | paid ->
                // 两个数各取自一处具名的取值器（票 107 的逐数溯源），不在这里现数一遍。
                let rebound = table |> Table.paidRevoked |> List.length
                said + $"——其中 {paid} 笔花了钱没落子（{rebound} 笔是换人撤的），导出的牌谱不带这几笔。"
        // **牌谱没告诉它有几笔作废**（那正是本票的判断），因此这一支不编任何数：
        // 它只说得出「这个数是什么的合计」，以及「当时花掉的可能比它多」。
        | Source.Replay _ -> said + "——牌谱只带得走落了子的那几手的账，当时花掉的只多不少。"

    /// 第 `turn` 手**落定之后**那一帧的帧号（票 76）。
    ///
    /// 判据两条缺一不可：手数正好是 `turn + 1`，**且它真落定了一手**（`Latest` 不是 None）
    /// ——下一局的开局帧手数沿用着上一局（票 75 的红-7 就是这一条）。
    let private frameOfTurn (turn: int) (frames: Table list) : int option =
        frames
        |> List.tryFindIndex (fun frame -> frame.Turns = turn + 1 && Option.isSome frame.Latest)

    /// Live 那一桌的逐帧牌桌：**导成牌谱再 fold 一遍**（`Table.paifu` → `Table.replay`，
    /// 与「导出牌谱」走的是同一条路）。
    ///
    /// **Live 侧不常驻一份帧数组**（票面明令）：点一下算一次。实测一次 fold，256 帧 46–74 ms、
    /// 741 帧约 200 ms（报告 75/76）——而帧数常驻着就得每落一手重算一遍。
    ///
    /// **`internal` 而不是 `private`**（票 90）：复盘（`Review`）要的是同一份帧，
    /// 而它的判据「这一场打完了没有」与这里逐字同源。再写一份「导成牌谱再 fold」就是第二条算路。
    let internal liveFrames (model: TableModel) : Table list =
        match live model, rosterOf model with
        | Some { Table = Ok table }, Some roster ->
            match Table.paifu roster table |> Table.replay with
            | Ok frames -> frames
            // 自己刚导出的牌谱回放不动：走不到（导出那一条路每次 CI 都在跑）。
            // 真走到了就当没有快照，面板不摊开（不白屏、也不假装有）。
            | Error _ -> []
        | _, _ -> []

    /// 点开第 `turn` 手（票 76）。
    ///
    /// **回放里它顺带把游标挪到那一帧**：轴只有票 75 那一根，全文面板不另开一条时间轴；
    /// 于是牌桌自己就是那一手的快照，两边只有一份渲染。
    /// **Live 里只摆快照**（`shown`），`live.Table` 一字不动——只读，不影响推进。
    ///
    /// **一点就暂停**：与「一拖就暂停」同一条判据——牌桌上摆着一张快照时定时器推得再快，
    /// 人也看不见。再按「播放」就把面板收了（`moves`）。
    ///
    /// **挑走的两样东西都记下来**（票 86 的 `Origin`）：游标停在哪一帧、那一刻在不在播。
    /// 不记的话「读一条理由」就变成了「时间轴被永久搬走」——主人试玩时报的就是它。
    let private openAt (turn: int) (model: TableModel) : TableModel =
        // **原处只记头一次**：连点两家气泡时，第二下是从「已经跳过去那一处」出发的；
        // 把它当原处的话，关掉只回到上一次跳之前。人要回的是**最初**那一处。
        let origin =
            match model.Opened with
            | Some opened -> opened.Origin
            | None ->
                match model.Source with
                | Source.Replay(ReplayTable.Ready(_, cursor, _)) ->
                    Some {
                        Cursor = cursor
                        Playing = model.Playback.Playing
                    }
                // Live 那一侧没有游标（下面那一段也不动它的 `Source`）：没有可回的地方。
                | Source.Replay _
                | Source.Live _ -> None

        let opened (frames: Table list) (frame: int) = {
            model with
                Source =
                    match model.Source with
                    | Source.Replay(ReplayTable.Ready(_, _, names)) ->
                        Source.Replay(ReplayTable.Ready(frames, frame, names))
                    // Live 那一侧没有游标：牌桌那一桌原样留着。
                    | source -> source
                Opened =
                    List.tryItem frame frames
                    |> Option.map (fun snapshot -> { Snapshot = snapshot; Origin = origin })
                Playback = Playback.pause model.Playback
        }

        let frames =
            match model.Source with
            | Source.Replay(ReplayTable.Ready(frames, _, _)) -> frames
            | Source.Replay _
            | Source.Live _ -> liveFrames model

        match frameOfTurn turn frames with
        // 那一手不在这份牌谱里（重开过一桌、或者根本导不出牌谱）：没有事情发生。
        | None -> model
        | Some frame -> opened frames frame

    /// 点开之前停在哪儿（票 86）；没点开、或者 Live 那一侧（`openAt` 什么也没搬走）时是 None。
    let private originOf (model: TableModel) : Origin option =
        model.Opened |> Option.bind (fun opened -> opened.Origin)

    /// 把游标搬回点开之前那一帧并把面板收了（票 86）。**只搬游标**：
    /// 推进牌桌的那几条消息（`moves`）自己就在说播放该怎么样，后面那一步让它们说了算。
    ///
    /// 没点开、Live、帧还没 fold 好的那几种：只把面板收了（与票 86 之前一模一样）。
    let private rewound (model: TableModel) : TableModel =
        let closed = { model with Opened = None }

        match originOf model, model.Source with
        | Some origin, Source.Replay(ReplayTable.Ready(frames, _, names)) -> {
            closed with
                // 票 82 之后 `Ready` 多了第三格（回放的名字只有牌谱这一个真源）：原样带过去。
                Source = Source.Replay(ReplayTable.Ready(frames, origin.Cursor, names))
          }
        | _ -> closed

    /// 收起面板（`bubble-close`）：**游标与播放状态一起回到点开之前那一刻**（票 86）。
    ///
    /// 点开那一下改了两样（游标 + 一点就暂停），回程就要把两样都还回去；
    /// 只还一样的话，看完一条理由回来会发现牌桌自己停了。
    /// **在播就得真推得动**：恢复成「在播」却不续上一记定时器，那只是一个写着「在播」的空壳。
    let private returned (model: TableModel) : TableModel * Cmd<TableMsg> =
        match originOf model with
        // Live 那一侧没有可回的地方（`openAt` 什么也没搬走）：收起来就只是收起来。
        | None -> { model with Opened = None }, Cmd.none
        | Some origin ->
            let back = rewound model
            let playback = back.Playback |> Playback.resumed origin.Playing
            { back with Playback = playback }, schedule playback

    /// 全文面板此刻摊开的那一手（票 76）；没点开时是 None。
    ///
    /// **公开的**：视图与页面逻辑的用例读同一处推导（同 `timeline` / `canAdvance`）。
    /// 记录从那一帧上现读（`recordOf`）：快照与记录因此不可能对不上。
    ///
    /// **视角同样拦它**（票 81）：面板今天只从气泡点得开，而那一席的气泡在别席视角下根本不存在；
    /// 但「今天点不到」不是一道闸门（切视角不会把摊开着的面板收起来）——于是这里再问一句
    /// **同一个 `reveals`**。不是第二处判据：判据仍只有 `reveals` 那一份，这里只是多一个消费点。
    let detail (model: TableModel) : BubbleDetail option =
        model.Opened
        |> Option.bind (fun opened ->
            recordOf opened.Snapshot
            |> Option.map (fun record -> {
                Record = record
                Snapshot = opened.Snapshot
                // 点开之前停在第几帧（票 86）：面板上那句「正在看第 N 手」读的就是它在不在。
                Origin = opened.Origin |> Option.map (fun origin -> origin.Cursor)
            }))
        |> Option.filter (fun detail -> reveals model detail.Record.Seat)

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
    ///
    /// **坐法是一整份 `SeatingPlan`**（票 73）：档案库 + 四席绑定。从前那两个参
    /// （`Seat option` + 单份 `LlmSeat`）只装得下一席模型，而 M2 要的是四 LLM 同桌。
    let initial (rules: RulesetDraft) (seating: SeatingPlan) : TableModel * Cmd<TableMsg> =
        let ruleset = RulesetDraft.ruleset rules
        let seedText = string defaultSeed
        // 绑定的条数对齐到座位数：它从 localStorage 来，什么长度都可能。
        let seating = SeatingPlan.fit ruleset seating

        let live: LiveTable = {
            SeedText = seedText
            Rules = rules
            Table = openTable (SeatingPlan.roster ruleset seating) seedText
            Seating = seating
            // 档案库非空时开着头一份；空库时这个 0 就是越界（编辑处不摆）。
            Editing = 0
            Notice = None
            // 还没问过话：四席的人格与模板都还改得动（票 46）。
            Pinned = seating.Seats |> List.map (fun _ -> None)
            Awaiting = []
            // **一个字节都还没拉**（票 92；ADR-0006 边界 1）：下面那一行 `started`
            // 只在坐法里真有强 AI 基线席时才把它推到 `Loading`。
            Baseline = BaselineStatus.Absent
            Consulting = []
            BaselineTroubles = []
            // 他还没按过一次「过」（票 88）。「轮不轮到他」没有存储：现问局面（`handOf`）。
            Passed = []
            // 倒计时同样不在这里启动（票 89）：下面那一行 `clocked` 按 `handOf` 推一遍，
            // 轮到他且设了时限才上发条——页面一打开就轮到他的那一局因此也计得上。
            Clock = None
            Ticket = 0
            Agent = seating.Seats |> List.map (fun _ -> AgentStatus.Idle)
            // 还没点过「复制分享链接」（票 78）。
            Shared = None
        }

        // 上一次就把某一席拨给了强 AI 基线（localStorage 里存着）：那就现在开始拉。
        let live, loading = started live
        // 真人坐在这一桌上、而且一打开就轮到他（他是东 1 局的亲）：倒计时从这一刻起走。
        let live, winding = wound live

        {
            Ruleset = ruleset
            Source = Source.Live live
            // **默认暂停**（`Playback.initial`）：`?table=1` 是最安静的一页，
            // 要点、要读牌桌的那几道无头闸门全靠这一条。
            Playback = Playback.initial
            // **默认上帝视角**（票 81 交给票 82 的那一件；理由见 `DECISIONS.md` 81-2 那一段）：
            // 票 81 之后视角是气泡的闸门，坐座位 0 的话**把模型摆在 1–3 席的主持人
            // 第一眼看不到自家模型说话**。`unlocked`（ADR-0003）管的是「有真人在对局中」，
            // 而今天 `humanSeated` 恒 false——没有可泄露的对象；
            // 「模型看到的和你一样多」那条演示离一次点击（座位 N 那枚按钮）就够。
            // **`reveals` 与 `unlocked` 的规则一个字没动**：改的只有这一处默认值。
            Viewpoint = Viewpoint.God
            // 危险度默认关（票 25）。
            ShowDanger = false
            // 还没点开任何一手的全文面板（票 76）。
            Opened = None
            // 复盘默认只摆值得看的那几手（票 105）；它只在终局之后看得见。
            ReviewFiltered = true
            // 导入入口不在这一页（票 78），这一格恒为 None。
            ImportFault = None
        },
        Cmd.batch [ loading; winding ]

    /// 首页（`/`）初次摆的那一屏：一份还没拉回来的 Demo 回放（票 71；ADR-0003）。
    ///
    /// **规则集先摆默认那一份**：拉回牌谱之后换成它自带的那一份（`demoLoaded`）。
    /// 这一刻页面上只有一句「正在取那份牌谱」，牌桌还没有。
    let home () : TableModel * Cmd<TableMsg> =
        {
            Ruleset = Ruleset.yonma
            Source = Source.Replay ReplayTable.Loading
            // 还没开播：牌谱拉回来那一刻才 `Playback.restart`（否则定时器空转）。
            Playback = Playback.initial
            // **回放默认上帝视角**（裁决 71-8，票 75 执行）：这份牌谱已经打完了，
            // 没有人还在对局，因此不存在「提前看到他家手牌」那件事（票 22 那条泄露挂账
            // 针对的是真人坐席在场）；而复盘的价值全在看得见四家。
            // **Live 那一页从票 82 起也是上帝视角**（见 `initial`）：两页因此同一个默认。
            Viewpoint = Viewpoint.God
            ShowDanger = false
            Opened = None
            ReviewFiltered = true
            // 还没导过任何东西（票 78）。
            ImportFault = None
        },
        demoCmd

    /// 打开带载荷的地址（票 78）：起步那一屏与首页同一个（还在解、暂停着、上帝视角），
    /// 差别只在牌谱从 hash 里来（`Share.ofPayload`）而不是 fetch Demo 资产。
    ///
    /// **拿 `home ()` 的模型起步而不是另拼一份**：两屏若各拼各的，
    /// 「回放默认上帝视角」这类默认就会各自漂。
    let shared (payload: string) : TableModel * Cmd<TableMsg> =
        let model, _ = home ()

        model,
        Cmd.ofEffect (fun dispatch ->
            let loaded (paifu: Result<Paifu, string>) = dispatch (SharedLoaded paifu)
            (Share.ofPayload payload).``then`` loaded |> ignore)

    /// 页面初次打开。**地址说了算**（票 71 的 `Route.landing`）：
    /// `/` 是首页的 Demo 回放，`?table=1` 是主持人自己开的一桌（上一次填的配置从
    /// localStorage 里读回来）。**hash 只装载荷、不当路由**（35-1，票 78）：落在哪一页
    /// 仍由 query 说了算，载荷只决定首页那一屏放的是哪一场；`?table=1` 上带着 hash
    /// 照旧是主持人那一页（三者正交）。
    let init () : TableModel * Cmd<TableMsg> =
        match Route.landing () with
        | Landing.Home ->
            match Route.payload () with
            | Some payload -> shared payload
            | None -> home ()
        | Landing.Table ->
            let rules = Store.readRules ()
            initial rules (Store.readSeating (RulesetDraft.ruleset rules))

    /// 这条消息会不会把牌桌挪一挪（票 76）。**全文面板摊开时牌桌上摆的是那一手的快照**，
    /// 因此凡是挪动牌桌的消息都先把它收起来——否则牌局在走而画面冻着，人会以为卡住了。
    /// **收起来之前先把游标搬回原处**（票 86 的 `rewound`）：否则按一下「播放」
    /// 就从跳过去那一处接着往下走，时间轴就永久被搬走了。
    ///
    /// **`PlayToggled` 也算**：一点开就暂停（`openAt`），而重新按播放就是「接着看牌」。
    /// 于是面板摊着的时候不可能有被接受的 `Ticked`，那一条因此不必列在这里
    /// （列了反而会让一记**过期**的定时器把面板关掉）。
    ///
    /// **逐 case 穷举而不用 `| _ ->`**：加了新消息时编译器会把这里指出来。
    let private moves (message: TableMsg) : bool =
        match message with
        | Advanced
        | PlayToggled
        | Restarted
        | CursorMoved _
        | KyokuAdvanced
        | DemoLoaded _
        | SharedLoaded _
        | ImportLoaded _
        // 一行式开桌那一枚（票 138）：它绑完座位当场开播，牌桌因此挪了一下。
        | QuickStarted
        | Answered _ -> true
        | HumanPlayed _
        // 强 AI 基线那一手同样把牌桌挪了一下（票 92）；拉完那一刻也会接着推（`resume`）。
        | BaselineDecided _
        | BaselineLoaded _ -> true
        | SeedEdited _
        | SpeedPicked _
        | Ticked _
        | Waited _
        // 倒计时那一记（票 89）：大多数只把页面上那个数往前走（同 `Waited`）。
        // **到点那一记确实把牌桌挪了一下**，但那一刻桌边坐着真人——气泡与全文面板
        // 本来就一个都不在（`reveals`），因此不会出现「牌局在走而画面冻着」那一幕。
        | HumanTicked _
        | ViewpointPicked _
        | RecordOpened _
        | DangerToggled
        | ReviewFilterToggled
        | SeatBound _
        | SeatEdited _
        | ProfileOpened _
        | ProfileAdded
        | ProfileDeleted _
        | ProfileEdited _
        | QuickEdited _
        | RulePicked _
        | Exported
        | Shared
        | ShareSettled _
        | ImportPicked _ -> false

    /// **每一条消息之后把倒计时重新推一遍**（票 89）。
    ///
    /// **只有这一处**：他出了手、模型席答上来了、人中途把自己摆上座位、把时限拨成了不限、
    /// 重开一桌——十几种情形各写一次启停就是十几份会漂的判据。`wound` 现问 `handOf`，
    /// 于是“倒计时只在轮到自己时走”与“轮不轮到他不存状态”（票 87）是同一件事。
    ///
    /// **不限时那条路上它一个效果体都不多发**（`wound` 那一支返回 `Cmd.none`）：
    /// 票 87/88 那几条数效果体的用例因此逐条照旧。
    let private clocked (model: TableModel) (cmd: Cmd<TableMsg>) : TableModel * Cmd<TableMsg> =
        match model.Source with
        | Source.Replay _ -> model, cmd
        | Source.Live live ->
            let winded, winding = wound live

            {
                model with
                    Source = Source.Live winded
            },
            Cmd.batch [ cmd; winding ]

    /// 一行式开桌那一行改的是**哪一份档案**（票 138）：编辑处开着的那一份。
    ///
    /// **只有一种情形它不是原样返回**：库空了（人把档案全删了）。那时先补一份
    /// ——那一行在配桌收着时也点得到，而「新建档案」那一枚在收着的配桌里，人看不见。
    /// 因此这一行永远有一份可填的档案，而它与档案编辑处填的**是同一份**。
    let private quickTarget (live: LiveTable) : SeatingPlan * int =
        match SeatingPlan.profileAt live.Editing live.Seating with
        | Some _ -> live.Seating, live.Editing
        | None ->
            match SeatingPlan.profileAt 0 live.Seating with
            | Some _ -> live.Seating, 0
            | None -> SeatingPlan.addProfile live.Seating, 0

    /// 一条消息推一步。**倒计时不在这里启停**（票 89）：它由外面那层 `update`
    /// 每条消息重新推一遍（`clocked`）——这一层因此一条新分支都不必知道时限的存在。
    let rec private stepped (message: TableMsg) (model: TableModel) : TableModel * Cmd<TableMsg> =
        match message with
        | SeedEdited seed -> model |> onLive (fun _ live -> { live with SeedText = seed }, Cmd.none)
        | Restarted ->
            match model.Source with
            // 回放的「从头再放」：回到第 0 帧，接着自动播。**帧不必重算**——它们是值。
            // `Playback.restart` 顺带换世代：正播着时再按，在飞的那记定时器必须作废，
            // 否则它与新发的那记一起被认下，牌桌从此双倍速走（票 78 按红过一次）。
            | Source.Replay(ReplayTable.Ready(frames, _, names)) ->
                let playback = Playback.restart model.Playback.Speed model.Playback

                {
                    model with
                        Source = Source.Replay(ReplayTable.Ready(frames, 0, names))
                        Playback = playback
                },
                schedule playback
            | Source.Replay _ -> model, Cmd.none
            | Source.Live live ->
                // **配桌拨到的那三项在这一刻才生效**（票 72）：与种子同一条路。
                // 半场换规则会让同一份牌谱前后按两套规则算，而回放只读得到牌谱里那一份。
                let ruleset = RulesetDraft.ruleset live.Rules

                // 在飞的那几次问话作废：它们的 id 是按旧那桌的决策包编的号。
                //
                // **那几 MB 不重拉**（票 92）：资产与哪一桌无关，拉过一次就摆在那儿；
                // 上一桌拉不动那一档倒是要再试一次（`started`）——重开就是人在说「再来一次」。
                let restarted, loading =
                    started {
                        live with
                            Table = openTable (rosterFor ruleset live) live.SeedText
                            // 重开一桌就是回到第一局：四席的人格与模板一起松开（票 46/73）。
                            Pinned = loosened live
                            Awaiting = []
                            Consulting = []
                            BaselineTroubles = []
                            // 「你按了几次过」与「超时代打了几手」都是旧那桌的事（票 87/88/89）。
                            Passed = []
                            // 旧那桌那一手的倒计时一并作废；新那桌轮不轮到他由 `clocked` 重新推。
                            Clock = None
                            Agent = idled live
                            // 旧桌的分享回执也撤下来：那句话说的是已经不存在的一桌（票 78）。
                            Shared = None
                    }

                {
                    model with
                        Ruleset = ruleset
                        Source = Source.Live restarted
                        Playback = Playback.pause model.Playback
                },
                loading
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
            | Source.Replay(ReplayTable.Ready(frames, cursor, names)) -> model |> replayTick frames names cursor
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
            | Source.Replay(ReplayTable.Ready(frames, _, names)) -> moveCursor frame frames names model, Cmd.none
            // 还没 fold 好的那两段没有帧可拖；Live 那一侧根本没有时间轴
            // （在 Live 里点历史某一手是票 76）。两条都是「没有事情发生」，不是错误。
            | Source.Replay _
            | Source.Live _ -> model, Cmd.none
        | ViewpointPicked viewpoint -> { model with Viewpoint = viewpoint }, Cmd.none
        // 收起来：游标与播放状态一起回到点开之前那一刻（票 86；`moves` 不管 `RecordOpened`）。
        | RecordOpened None -> returned model
        | RecordOpened(Some turn) -> openAt turn model, Cmd.none
        | DangerToggled ->
            {
                model with
                    ShowDanger = not model.ShowDanger
            },
            Cmd.none
        // 复盘那一列摆几条（票 105）。**它不碰游标、不碰摄开的那一手**：
        // 筛选改的是「这一列摆几条」，不是「你正在看哪一手」。
        | ReviewFilterToggled ->
            {
                model with
                    ReviewFiltered = not model.ReviewFiltered
            },
            Cmd.none
        | DemoLoaded paifu -> model |> demoLoaded paifu
        | SharedLoaded paifu -> model |> sharedLoaded paifu
        | ImportPicked file ->
            (match model.Source with
             // 导入入口只在回放那一页上（票 78）。
             | Source.Live _ -> model, Cmd.none
             | Source.Replay _ -> model, importCmd file)
        | ImportLoaded paifu -> model |> importLoaded paifu
        | Shared ->
            model
            |> onLive (fun ruleset live ->
                match live.Table with
                // 牌桌都开不起来时没有棋谱可装（按钮那时也是灰的）。
                | Error _ -> live, Cmd.none
                // 上一次的下场先撤下来：新的一次正在路上，旧话留着会两头打架。
                | Ok table -> { live with Shared = None }, shareCmd (rosterFor ruleset live) table)
        | ShareSettled result ->
            model
            |> onLive (fun _ live ->
                {
                    live with
                        Shared = Some(settledShare result)
                },
                Cmd.none)
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
                // **在飞的那几次问话作废，而不是从账上消失**（票 109，详理由写在 `turned` 上）：
                // 从前这两格写的是 `Awaiting = []` / `Consulting = []`，而开下一局是**同一张牌桌**
                // （`Table.usage` 跨局累计），那一票的钱就此蒸发。
                let live = turned live

                {
                    live with
                        Table = Result.map Table.nextKyoku live.Table
                        // 一局一定型（票 46）：开下一局时面板上改过的人格与模板在这里生效。
                        Pinned = loosened live
                        // 「替你过了几次」跨局累计（同 `Table.fallbacks`），因此开下一局时不清。
                        // 在飞的问话已经作废（票号从此对不上），别让「在想」挂成孤儿；
                        // 说过话 / 兜底那两态**粘着不掉**（那是上一局末手的事实，人还想看）。
                        Agent =
                            live.Agent
                            |> List.map (fun status ->
                                match status with
                                | AgentStatus.Asking -> AgentStatus.Idle
                                | other -> other)
                },
                Cmd.none)
        // 不重开一桌：配桌是每推一手现推导的，换了从下一手起生效（票 73 之前那两条消息同理）。
        | SeatBound(seat, choice) ->
            model
            |> onLive (fun _ live ->
                let before = (SeatingPlan.bindingAt seat live.Seating).Choice
                let seating = live.Seating |> SeatingPlan.bind seat choice

                // **换人就撤票**（票 109，理由写在 `rebound` 上）：那一席在飞的那一票当场作废，
                // 不等它回来再剪——`swept` 那道按「合法动作集是不是还是当下」判，
                // 而拨座位那一刻动作集往往没变，因此它剪不掉。
                //
                // **拨到它已经绑着的那一项不算换人**：面板上点一下当前那一项同样发一条 `SeatBound`，
                // 而那一下什么都没改——误撤一票就是白花一次钱、白等一趟。
                let revoked =
                    if before = choice then
                        live
                    else
                        rebound seat (taker seat seating) live

                // 拨到强 AI 基线的那一下就是去拉那几 MB（票 92；ADR-0006 边界 1）：
                // **不预取、不按重开才拉**——人拨完下一步就是开桌，那 208 ms（本机）
                // 藏得进那一次点击的等待里。
                let bound, loading =
                    started {
                        revoked with
                            Seating = seating
                            // 只把换了人的这一席归零：别席的状态是别席的事实（票 74 按座位各一份）。
                            Agent = live.Agent |> Seat.mapAt seat (fun _ -> AgentStatus.Idle)
                            Notice = None
                    }

                bound, Cmd.batch [ save (fun () -> Store.writeSeating seating); loading ])
            // **拨完这一下，牌桌按它此刻的播放状态接着走**（票 111，理由写在 `roused` 上）：
            // 撤完票那一席就又该被问了，而票 109 那一版没有任何一记定时器去问它。
            ||> roused
        | SeatEdited(seat, field, value) ->
            model
            |> onLive (fun _ live ->
                let seating = live.Seating |> SeatingPlan.editSeat seat field value
                { live with Seating = seating }, save (fun () -> Store.writeSeating seating))
        | ProfileOpened index -> model |> onLive (fun _ live -> { live with Editing = index }, Cmd.none)
        | ProfileAdded ->
            model
            |> onLive (fun _ live ->
                let seating = SeatingPlan.addProfile live.Seating

                {
                    live with
                        Seating = seating
                        // 新建完就把编辑处开在它上面：人下一步要填的正是它。
                        Editing = List.length live.Seating.Profiles
                        Notice = None
                },
                save (fun () -> Store.writeSeating seating))
        // 删掉一份还被座位引用的档案：那几席退回 bot，**页面把这件事说出来**（票 73）。
        | ProfileDeleted index ->
            model
            |> onLive (fun _ live ->
                match SeatingPlan.profileAt index live.Seating with
                | None -> live, Cmd.none
                | Some doomed ->
                    let seating, orphans = SeatingPlan.removeProfile index live.Seating

                    let notice =
                        if List.isEmpty orphans then
                            $"删掉了档案「{doomed.Name}」。"
                        else
                            let seats = orphans |> List.map (Seat.index >> string) |> String.concat "、"

                            $"删掉了档案「{doomed.Name}」：座位 {seats} 本来引用着它，已退回{Bot.toDisplay Bot.Uniform}。"

                    {
                        live with
                            Seating = seating
                            // 编辑处退到库里还在的那一份（删的是最后一份时退到前一份）。
                            Editing = min index (List.length seating.Profiles - 1)
                            Notice = Some notice
                    },
                    save (fun () -> Store.writeSeating seating))
        | ProfileEdited(field, value) ->
            model
            |> onLive (fun _ live ->
                let seating = live.Seating |> SeatingPlan.editProfile live.Editing field value
                { live with Seating = seating }, save (fun () -> Store.writeSeating seating))
        // 一行式开桌那一行上的一格（票 138）。**它与 `ProfileEdited` 写的是同一份档案**：
        // 两处只有一个值，因此不存在「谁覆盖谁」。
        | QuickEdited(field, value) ->
            model
            |> onLive (fun _ live ->
                let target, editing = quickTarget live
                let seating = target |> SeatingPlan.editProfile editing field value

                {
                    live with
                        Seating = seating
                        Editing = editing
                },
                save (fun () -> Store.writeSeating seating))
        // 〔开打〕（票 138）：**七步压成一步**——建档案（库空着才建）、绑座位 0、开播。
        //
        // **绑座位那一步不另写一遍**（复用 `SeatBound` 那一支）：撤票、拉基线资产、
        // 归零那一席的 Agent 状态、落 localStorage——那几件事只许有一份实现，
        // 在这里抄第二遍就是又一份会漂的判据。
        //
        // **先把播放状态拨成「在播」再绑**：`SeatBound` 末尾那道 `roused`
        // 按 `model.Playback.Playing` 续定时器（票 111 的阴性对照量的正是「停着的桌不许凭空开动」），
        // 因此这一票要的「按下去就开始走」由这里显式拨，而不是去松那条判据。
        | QuickStarted ->
            match model.Source with
            | Source.Replay _ -> model, Cmd.none
            | Source.Live live ->
                let target, editing = quickTarget live

                match SeatingPlan.profileAt editing target with
                | None -> model, Cmd.none
                | Some profile ->
                    let primed = {
                        model with
                            Source =
                                Source.Live {
                                    live with
                                        Seating = target
                                        Editing = editing
                                }
                            Playback = Playback.resumed true model.Playback
                    }

                    let bound, cmd =
                        stepped (SeatBound(Seat.first, SeatChoice.Profile profile.Name)) primed

                    bound, Cmd.batch [ save (fun () -> Store.writeSeating target); cmd ]
        // **只拨下一桌那一份**（票 72）：牌桌正在按的那份规则集（`model.Ruleset`）
        // 只有 `Restarted` 动得了。拨到的值当场落 localStorage，下次打开还在。
        | RulePicked rule ->
            model
            |> onLive (fun _ live ->
                let rules = RulesetDraft.pick rule live.Rules
                { live with Rules = rules }, save (fun () -> Store.writeRules rules))
        | Answered(seat, ticket, answer) ->
            match model.Source with
            | Source.Replay _ -> model, Cmd.none
            | Source.Live live ->
                let expected =
                    live.Awaiting
                    |> List.exists (fun each -> each.Ticket = ticket && Awaiting.seat each = seat)

                if not expected then
                    // 过期或错位的回执（重开过一桌、开过下一局、座位与票号对不上）：**不落子**。
                    // **四席各判各的**（票 74）：座位与票号要与同一份 `Awaiting` 对上。
                    //
                    // **但钱不能从账上消失**（票 108）：这一票如果是 `drain` 剔下来的那一种（过期作废），
                    // 它真的调过 provider、真的计过费，只是那一手没发生——把 token 补到那一条作废记录上
                    // （`Table.creditVoid`，座位与票号同样要对上）。其余对不上的那几种在这张牌桌上
                    // 压根没有那一条记录，于是这一句对它们恰好是个空操作。
                    model
                    |> onLive (fun _ live ->
                        {
                            live with
                                Table = live.Table |> Result.map (Table.creditVoid ticket seat answer.Usage)
                        },
                        Cmd.none)
                else
                    // 先把回执记在那一票上，再按引擎的顺序落（`drain` 的注释说了为什么不按到达顺序）。
                    let noted = {
                        live with
                            Awaiting =
                                live.Awaiting
                                |> List.map (fun each ->
                                    if each.Ticket = ticket then
                                        { each with Answer = Some answer }
                                    else
                                        each)
                    }

                    {
                        model with
                            Source = Source.Live(drain (rosterFor model.Ruleset noted) noted)
                    }
                    |> resume Cmd.none
        // 真人按了一下（票 87 的手牌、票 88 的吃碰杠 / 立直 / 荣和 / 自摸 / 「过」）。
        // **与 `Answered` 同一条路**：id 换回动作、落进引擎、然后 `resume` 按播放状态接着走
        // ——引擎与编排层不区分真人与 AI（spec 的 story 28）。**一种动作一条 case 都不必加**：
        // 这一条消息里只有一个 id，吃的左中右与碰的赤 5 取舍在包里早就各占一条。
        //
        // **真人那一手不留决策记录**（走的是 `Table.apply` 而不是 `applyRecorded`）：
        // 他没有可审计的推理，与 bot 席同级——牌谱格式因此一个字段都不必加（票面边界）。
        | HumanPlayed id ->
            match model.Source with
            // 回放里没有人要出手（动作全在牌谱里）：没有事情发生。
            | Source.Replay _ -> model, Cmd.none
            | Source.Live live ->
                match handOf live, live.Table with
                | Some package, Ok table ->
                    match DecisionPackage.tryAction id package with
                    // 点了一条不在这一包里的（页面上根本点不到，只可能是过期的一下）：
                    // **没有事情发生**——绝不在这里放宽合法性（真人不可能犯规）。
                    | None -> model, Cmd.none
                    | Some action ->
                        // **他自己按的那几次「过」记下来**（票 88 接票 87 那本账）：
                        // 放掉的是碰还是荣和，是复盘（票 90）第一件要问的事。
                        // `AutoPlayed = None` 说的就是「这一下是他按的」（票 89 那一格的另一面）。
                        let pressed =
                            match action with
                            | Action.None _ ->
                                Some {
                                    Turn = table.Turns
                                    Seat = HumanSeat.seat package
                                    Skipped = HumanSeat.buttons package |> List.map (fun button -> button.Label)
                                    AutoPlayed = None
                                }
                            | _ -> None

                        let played = landed action pressed table live

                        {
                            model with
                                Source = Source.Live(drain (rosterFor model.Ruleset played) played)
                        }
                        |> resume Cmd.none
                // 不轮到他（或者牌桌开不起来）：同上，没有事情发生。
                | _, _ -> model, Cmd.none
        // **真人这一手的倒计时又走了一秒**（票 89 的 story 32）。
        //
        // 三道前置全对得上才算数：倒计时还在、现在仍旧轮到他、而且还是当时那一手。
        // 对不上就丢掉（他已经出手了 / 重开过一桌 / 时限拨成了不限），链自己断
        // ——与 `Waited` 逐字同一条规矩。
        | HumanTicked turn ->
            match live model with
            | None -> model, Cmd.none
            | Some live ->
                match live.Clock, handOf live, live.Table with
                | Some clock, Some package, Ok table when clock.Turn = turn && table.Turns = turn ->
                    if HumanClock.remaining clock > 1 then
                        // 还有时间：只把页面上那个数往前走一格，**牌桌一根汗毛都不动**（同 `Waited`）。
                        {
                            model with
                                Source =
                                    Source.Live {
                                        live with
                                            Clock =
                                                Some {
                                                    clock with
                                                        Elapsed = clock.Elapsed + 1
                                                }
                                    }
                        },
                        clockCmd turn
                    else
                        // **到点：代他打一手，牌局接着走**（票面那句「不许卡死」）。
                        //
                        // **代打那一手向引擎要**（`Fallback.action`，判据 11：要读规则才做得出的决定归引擎）：
                        // 它的 Bare 那一支就是「摸切 → 过 → 合法动作集的第一条」，正是票面要的
                        // 「超时自动摸切，响应阶段自动过」；而碰吃之后要打牌那一手根本没有「刚摸进的那张」，
                        // 第三级因此不是凑数。
                        //
                        // **恒拿 Bare 那一支，不看他自己拨的档位**：到点那一手不是他打的，
                        // 平台不该替他用一遍辅助（Assisted 那一支会挑「不退向听的安全打」）；
                        // 而且那样的话「时限」会把档位这个自变量也搬进来。
                        let action = Fallback.action ScaffoldTier.Bare package

                        let expired = {
                            Turn = table.Turns
                            Seat = HumanSeat.seat package
                            // 他没来得及宣言的那几条（该他出牌那一手常常是空的）。
                            Skipped = HumanSeat.buttons package |> List.map (fun button -> button.Label)
                            // **代打了什么要说出来**（不许静默替换，票 23）；
                            // 它同时就是「这一次不是他按的」那一格。
                            AutoPlayed = Some(Action.toDisplay action)
                        }

                        let played = landed action (Some expired) table live

                        {
                            model with
                                Source = Source.Live(drain (rosterFor model.Ruleset played) played)
                        }
                        |> resume Cmd.none
                | _ -> model, Cmd.none
        // 那几 MB 拉完了（票 92）。**两种下场都要把牌桌重新开动**（`resume`）：
        // 拉到了就接着问它这一手，拉不动则那一席已经退回自带 bot（`rosterFor` 里的 `degraded`）
        // ——**其余席照常打完一局**（ADR-0006 边界 2：它是可选依赖，不是单点）。
        | BaselineLoaded loaded ->
            match model.Source with
            | Source.Replay _ -> model, Cmd.none
            | Source.Live live ->
                let status =
                    match loaded with
                    | Ok bytes -> BaselineStatus.Ready bytes
                    | Error reason -> BaselineStatus.Unavailable reason

                {
                    model with
                        Source = Source.Live { live with Baseline = status }
                }
                |> resume Cmd.none
        // 强 AI 基线那一手回来了（票 92）。**与 `Answered` 逐字同一条路**：先把回执记在
        // 那一票上，再按引擎的顺序落（`drain`）——谁先答不改裁决。
        | BaselineDecided(seat, ticket, answer) ->
            match model.Source with
            | Source.Replay _ -> model, Cmd.none
            | Source.Live live ->
                let expected =
                    live.Consulting
                    |> List.exists (fun each -> each.Ticket = ticket && Consult.seat each = seat)

                if not expected then
                    // 过期或错位的回执（重开过一桌、开过下一局）：丢掉。
                    model, Cmd.none
                else
                    let noted = {
                        live with
                            Consulting =
                                live.Consulting
                                |> List.map (fun each ->
                                    if each.Ticket = ticket then
                                        { each with Answer = Some answer }
                                    else
                                        each)
                    }

                    {
                        model with
                            Source = Source.Live(drain (rosterFor model.Ruleset noted) noted)
                    }
                    |> resume Cmd.none
        | Waited ticket ->
            model
            |> onLive (fun _ live ->
                match live.Awaiting |> List.tryFind (fun each -> each.Ticket = ticket) with
                // 那一票已经落定 / 作废，或回执已经到了：钟就停在这里，链自己断。
                | None
                | Some { Answer = Some _ } -> live, Cmd.none
                | Some _ ->
                    {
                        live with
                            Awaiting =
                                live.Awaiting
                                |> List.map (fun each ->
                                    if each.Ticket = ticket then
                                        {
                                            each with
                                                WaitedSeconds = each.WaitedSeconds + 1
                                        }
                                    else
                                        each)
                    },
                    waitCmd ticket)

    /// 一条消息推一步，**再把真人那一手的倒计时按此刻的局面重新推一遍**（票 89）。
    ///
    /// 两层而不是一层：倒计时要在十几种消息之后各自重算（他出了手、人拨了座位、
    /// 时限改成了不限、重开一桌……），而那几处各写一次启停就是十几份会漂的判据。
    let update (message: TableMsg) (model: TableModel) : TableModel * Cmd<TableMsg> =
        let model = if moves message then rewound model else model
        let stepped, cmd = stepped message model
        clocked stepped cmd
