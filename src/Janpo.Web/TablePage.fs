namespace Janpo.Web

open Elmish
open Feliz
open Feliz.UseElmish
open Janpo

/// 牌桌页面：把拆开的那四块装回一屏，并且**继续当这一页对外的那一个名字**。
///
/// 票 70 把原来 1647 行的 `TablePage.fs` 按「哪张票会改它」拆成四块：状态与 MVU 在
/// `TableState`、牌桌与结算在 `TableBoard`、配桌与模型面板在 `TablePanel`、
/// Agent 层那两行状态在 `AgentLine`。
///
/// **这一层还转出二十多个入口**：F# 不许同一个模块分在两个文件里（FS0248），而 `Main.fs` 调的是
/// `TablePage.Page`、dotnet 侧的用例（`tests/Janpo.Web.Tests`）调的是 `TablePage.initial` / `home` /
/// `shared` / `init` / `update` / `rosterOf` / `seatConfigOf` / `nameplates` / `renderingPending` /
/// `rulesPending` / `live` / `shown` / `canAdvance` / `timeline` / `reveals` / `bubbles` / `detail` /
/// `recordless`。转出来之后
/// **这个程序集的公开面只多了这几个名字**：那几块里跨文件用的助手一律 `internal`，
/// 出不了 `Janpo.Web`。
///
/// **这一页只有一套装配**（票 83；票 71 那两个布局已合成 `layout`）：
/// **抬头 → 配这一桌 → 操作这一桌 → 牌桌**。`/` 是首页的 Demo 回放（自动播，没有配桌），
/// `?table=1` 是主持人自己开的一桌（东西一样不少）。**牌桌与结算的渲染只有一份**
/// （`board`），播放、视角与危险度也只有一份（`TablePanel.ops`）——
/// 两屏的分岔只剩下抬头下面那几句话，以及「配桌那一块只有 Live 有」。
[<RequireQualifiedAccess>]
module TablePage =

    /// `?table=1` 初次摆的那一桌，配桌那三项与**坐法**（档案库 + 四席绑定）都从外面给。
    /// 实现与理由见 `TableState.initial`。
    let initial (rules: RulesetDraft) (seating: SeatingPlan) : TableModel * Cmd<TableMsg> =
        TableState.initial rules seating

    /// 首页（`/`）初次摆的那一屏：一份还没拉回来的 Demo 回放。实现见 `TableState.home`。
    let home () : TableModel * Cmd<TableMsg> = TableState.home ()

    /// 打开带载荷的地址（票 78）：与首页同一屏起步，牌谱从 hash 里解。
    /// 实现与理由见 `TableState.shared`。
    let shared (payload: string) : TableModel * Cmd<TableMsg> = TableState.shared payload

    /// 页面初次打开（地址说了算）。实现见 `TableState.init`。
    let init () : TableModel * Cmd<TableMsg> = TableState.init ()

    /// 一条消息推一步。实现见 `TableState.update`。
    let update (message: TableMsg) (model: TableModel) : TableModel * Cmd<TableMsg> = TableState.update message model

    /// 这一桌是 Live 吗（回放时是 None）。实现与理由见 `TableState.live`。
    let live (model: TableModel) : LiveTable option = TableState.live model

    /// 牌桌那一格里此刻该画什么。实现与理由见 `TableState.shown`。
    let shown (model: TableModel) : Shown = TableState.shown model

    /// 这一桌的配桌（谁坐哪里）；回放没有配桌。实现与理由见 `TableState.rosterOf`。
    let rosterOf (model: TableModel) : Roster option = TableState.rosterOf model

    /// 这一席此刻真正会用的那份配置（票 73）。实现与理由见 `TableState.seatConfigOf`。
    let seatConfigOf (seat: Seat) (model: TableModel) : LlmSeat option = TableState.seatConfigOf seat model

    /// 名牌上那一句「这一席是谁在打」（票 82）。实现与理由见 `TableState.nameplates`。
    let nameplates (model: TableModel) : string list = TableState.nameplates model

    /// 终局记分卡那几行（票 133）；还没终局就是空表。实现与理由见 `TableState.scorecard`。
    let scorecard (model: TableModel) (table: Table) : ScorecardRow list = TableState.scorecard model table

    /// 人格与模板改过了、但要等下一局才发得出去吗。实现与理由见 `TableState.renderingPending`。
    let renderingPending (model: TableModel) : bool = TableState.renderingPending model

    /// 配桌那三项拨过了、但要按「重开」才生效吗。实现与理由见 `TableState.rulesPending`。
    let rulesPending (model: TableModel) : bool = TableState.rulesPending model

    /// 还推得动吗（播放那一枚按钮灰不灰）。实现与理由见 `TableState.canAdvance`。
    let canAdvance (model: TableModel) : bool = TableState.canAdvance model

    /// 回放的时间轴（票 75）；Live 时是 None。实现与理由见 `TableState.timeline`。
    let timeline (model: TableModel) : Timeline option = TableState.timeline model

    /// 这一桌坐着真人的是哪一席（票 87）。实现与理由见 `TableState.humanSeat`。
    let humanSeat (model: TableModel) : Seat option = TableState.humanSeat model

    /// 强 AI 基线那几 MB 此刻在哪一步（票 92）。实现与理由见 `TableState.baseline`。
    let baseline (model: TableModel) : BaselineStatus = TableState.baseline model

    /// 强 AI 基线被兵底代打的那几手（票 92）。实现与理由见 `TableState.baselineTroubles`。
    let baselineTroubles (model: TableModel) : string list = TableState.baselineTroubles model

    /// 此刻视角锁在哪一席上（票 87）。实现与理由见 `TableState.lockedSeat`。
    let lockedSeat (model: TableModel) : Seat option = TableState.lockedSeat model

    /// 这一屏真正在用的那份投影（票 87）。实现与理由见 `TableState.viewpoint`。
    let viewpoint (model: TableModel) : Viewpoint = TableState.viewpoint model

    /// 曳光弹那一块给不给开（票 87 堵 22-A）。实现与理由见 `TableState.devSurfaceAllowed`。
    let devSurfaceAllowed (model: TableModel) : bool = TableState.devSurfaceAllowed model

    /// 轮到真人出牌了吗（票 87）。实现与理由见 `TableState.humanTurn`。
    let humanTurn (model: TableModel) : DecisionPackage option = TableState.humanTurn model

    /// 真人自己按「过」的那几次（票 87 开账、票 88 换了语义）。实现与理由见 `TableState.passes`。
    let passes (model: TableModel) : HumanPass list = TableState.passes model

    /// 真人那一席拨到的脚手架档位（票 89）。实现与理由见 `TableState.humanTier`。
    let humanTier (model: TableModel) : ScaffoldTier option = TableState.humanTier model

    /// 这一屏此刻给不给得出「要算才有的那几个数」（票 89）。实现与理由见 `TableState.assists`。
    let assists (model: TableModel) : bool = TableState.assists model

    /// 真人这一手的信息辅助（票 89）。实现与理由见 `TableState.humanScaffold`。
    let humanScaffold (model: TableModel) : Scaffold option = TableState.humanScaffold model

    /// 真人这一手的倒计时（票 89）。实现与理由见 `TableState.humanClock`。
    let humanClock (model: TableModel) : HumanClock option = TableState.humanClock model

    /// 真人那一席设的思考时限（票 89）。实现与理由见 `TableState.humanLimit`。
    let humanLimit (model: TableModel) : int option = TableState.humanLimit model

    /// 这一席的推理此刻看不看得见（票 81）。实现与理由见 `TableState.reveals`。
    ///
    /// **气泡与 Agent 那条状态线读的就是它**：转出来是为了用例能直接钉这条规则本身，
    /// 而不必逐个消费点各钉一遍。
    let reveals (model: TableModel) (seat: Seat) : bool = TableState.reveals model seat

    /// 这一桌每一席此刻的思考气泡（票 76）。实现与理由见 `TableState.bubbles`。
    let bubbles (model: TableModel) (table: Table) : Seat -> Bubble option = TableState.bubbles model table

    /// 全文面板此刻摊开的那一手（票 76）。实现与理由见 `TableState.detail`。
    let detail (model: TableModel) : BubbleDetail option = TableState.detail model

    /// 这份牌谱一条决策记录都没有吗（票 76）。实现与理由见 `TableState.recordless`。
    let recordless (model: TableModel) : bool = TableState.recordless model

    /// 账单行那一句（票 110）。实现与理由见 `TableState.usageSaid`。
    let usageSaid (model: TableModel) (table: Table) : string = TableState.usageSaid model table

    // ---- 视图 ----

    /// 牌桌那一格。**两种来源共用这一份**（票 71）：Live 画正在打的那一桌，
    /// 回放画那份牌谱 fold 出来的第 N 帧，往下（`TableBoard`）一行都不分岔。
    ///
    /// 三种状态各有各的说法：还在拉、出了事、以及真有一桌。**白屏不在其列**——
    /// 首页拉不到资产时人得看见一句原因。
    let private board (model: TableModel) (dispatch: TableMsg -> unit) =
        match TableState.shown model with
        | Shown.Loading ->
            Html.p [
                prop.className "intro"
                prop.testId "table-loading"
                prop.text "正在取那一局录下来的对局……"
            ]
        | Shown.Fault reason -> Html.p [ prop.className "error"; prop.testId "table-error"; prop.text reason ]
        | Shown.Board table -> TableBoard.tableBody model dispatch table

    /// 一页只有一套装配（票 83）：**抬头 → 配这一桌 → 操作这一桌 → 牌桌**。
    ///
    /// 首页（回放）与 `?table=1`（Live）走的是这同一条规则，分岔只有一处——
    /// **配桌那一块只有 Live 有**（回放没有配桌：牌是录下来的）。控件摆哪几个由 `TablePanel`
    /// 自己按 `Source` 分，页面这一层不再有两个布局。
    ///
    /// **操作控件紧贴牌桌上沿**（票 83 的第一条）：按一下就能看见结果，视线不必甩回页面顶部。
    /// **不做视口吸底**：吸底那一条会盖住牌桌下沿，而那正是自家手牌那一排。
    let private layout
        (model: TableModel)
        (review: ReviewPanel.ReviewView)
        (live: LiveTable option)
        (dispatch: TableMsg -> unit)
        (intro: ReactElement list)
        =
        let setup =
            live
            |> Option.toList
            // **两块**（票 138）：一行式开桌那一行 + 配桌那一枚折叠，两块都是 `.page` 的直接孩子。
            |> List.collect (fun live -> TablePanel.setup model live dispatch)

        Html.div [
            prop.className "page table-page"
            prop.children (
                // h1 抄的是 `web/index.html` 那个 `<title>`（票 33 定的稿）：
                // 标签页上与页面上不该各说各的。改品牌语先改那一行，再抄过来。
                // **站名不再顶在正题前面**（票 120，主人裁的）：页脚那一行
                // 「源码…GitHub 上的 Xerxes-2/janpo」已经承着身份与许可，
                // 而这一页真正的正题是牌桌。两页各省约 3 rem 纵向。
                // 「配这一桌」摆在左轨顶上（票 129，主人裁的：「配桌我仔细想了下左轨更好看统一」，
                // 也正是设计稿 1a 的画法）。**它仍在页面这一层、不进轨道那一列**：
                // 展开的那一屏是四席绑定加模型档案库，13rem 宽的轨道里摆不下；
                // 做成浮层的话它会盖住轨道与牌桌，底下的按钮全点不到（闸门当场红）。
                // 样式上那一行与左轨同宽同底色、接成一条（`styles.css` 的 `.page > .setup`），
                // 展开时照旧在流里把下面推开。
                intro
                @ setup
                // 时间轴上那几枚标记（票 105）跟着控制条一起下去：**它们与复盘那一列同源**
                // （同一次 `Review.focused`），因此不会出现「轴上标一批、列里摆另一批」。
                // **三列**（票 118）：操作在左轨、牌桌居中、复盘在右轨。
                //
                // 从前这三块是竖着堆的，于是页面成了纵向长条——票 119 装上固定舞台之后
                // 那条长条直接换成了牌的尺寸（牌 28→22.8 px），而横向大片空着。
                // 摆成三列同时办两件事：**把两侧的空白填上**，并且**把纵向堆叠变短**，
                // 根字号跟着涨、牌就大回去。
                //
                // 复盘仍与牌桌同屏（票 83 那条标准）——只是从「下面」改成了「右边」。
                @ [
                    Html.div [
                        prop.className "table-frame"
                        prop.children [
                            Html.div [
                                prop.key "rail-left"
                                prop.className "rail rail-left"
                                prop.children [
                                    TablePanel.ops model review.Marks dispatch
                                    // 副露那一句参照系（票 51/82）摆在左轨最下（票 118）：
                                    // 票 117 把它移出桌心时挂在牌桌左边距上、绝对定位，
                                    // 三列之后那个位置正压在页脚上。左轨本来就是它该在的地方——
                                    // 它是「怎么读这张牌桌」的说明，与操作同属一列。
                                    TableBoard.nakiLegendLine ()
                                ]
                            ]
                            board model dispatch
                            // 右轨 = **状态 + 复盘**（票 123，主人裁的，照设计稿 1a 的第三栏）：
                            // 「轮不轮到你 / 四席在做什么 / 上一手走了什么 / 账单」那几行
                            // 从前排在牌桌那一格的最前面，于是它们贴着中栏左边线、
                            // 在宽屏上离牌桌几百像素——而它们说的正是「这一桌此刻怎么了」，
                            // 与复盘同属「怎么看」这一栏。
                            Html.div [
                                prop.key "rail-right"
                                prop.className "rail rail-right"
                                prop.children (TableBoard.statusRail model @ review.Panel)
                            ]
                        ]
                    ]
                ]
            )
        ]

    /// 首页（`/`）：**访客的第一眼是一桌牌在走**（spec 的 story 1，ADR-0003 由 Demo Paifu 兑现）。
    ///
    /// **没有配桌与模型面板**：第一眼不该是一张表单（票 35 的「默认视图只该有牌桌」同一条标准）。
    /// 想自己开一桌的人走那条链接去 `?table=1`——Host 那一侧访客得摸得到。
    let private homePage (model: TableModel) (review: ReviewPanel.ReviewView) (dispatch: TableMsg -> unit) =
        layout model review None dispatch [
            Html.p [
                prop.className "intro"
                prop.testId "home-intro"
                prop.text
                    // 149 字压成一句（票 120）。剩下那几件——上帝视角、摸切虚线、
                    // 坐下去看暗牌就没了、拖时间轴——都是**看一次就够**的图例，
                    // 归到左轨那个点开的「怎么读这张牌桌」里。
                    "录好的一局，正在自动回放：打开就看得见牌怎么走。"
            ]
            Html.p [
                prop.className "intro"
                prop.children [
                    Html.a [ prop.testId "home-host-link"; prop.href Route.tableHref; prop.text "自己开一桌 →" ]
                    Html.span [ prop.text "　自带 API key，把一个座位交给模型，看它一手一手打。" ]
                ]
            ]
        ]

    /// 主持人那一页（`?table=1`）：配桌、模型面板、种子、单步 / 播放 / 倍速、导出、下一局、
    /// 视角、危险度——**东西一样不少**（票 83 只换了它们的先后：配桌收到上面，操作贴着牌桌）。
    ///
    /// **默认暂停**（`Playback.initial`）：要点、要读牌桌的那几道无头闸门全靠这一条。
    let private hostPage
        (model: TableModel)
        (review: ReviewPanel.ReviewView)
        (live: LiveTable)
        (dispatch: TableMsg -> unit)
        =
        layout model review (Some live) dispatch [
            // **一行就够**（票 83 交给票 82 的那件）：这一段的读者是**主持人自己**，
            // 他每开一次这一页都要从它头上跳过去——而一整屏只有 800px，四行说明占了 82px。
            // **首页那两段不动**（`homePage`）：那一段是写给头一回来的访客的。
            // 剪掉的那几句（key 存哪、虚线是摸切、模型看到的和你一样多）各自还在：
            // 前一句在配桌那一块的「这几格都是什么意思？」里（票 83），后两句在首页。
            // 「这一页是你自己开的一桌…」整句删（票 120，主人裁的）：
            // 「把任一席交给一份模型档案」就写在下面那四行席位上，「按播放」就是那枚按钮。
            // testId 留着（空壳），闸门那份「首页不该有的 testId」名单不受影响。
            Html.p [ prop.className "intro"; prop.testId "table-intro"; prop.text "" ]
        ]

    /// 曳光弹那一块（票 35）：**地址带 `?dev=1`、而且这一桌允许**时才挂在牌桌下面。
    ///
    /// **两个判据各管各的**：地址那一半在 `Route`（页面侧认地址只有那一处），
    /// “这一桌允不允许”那一半在 `TableState.devSurfaceAllowed`（纯的，dotnet 侧铉得住）。
    ///
    /// **它从 `Main.Shell` 搬到了这里**（票 87）：要看牌桌的 model 才答得出“桌边坐没坐人”，
    /// 而 model 就在这一层（`useElmish`）。搬到 `Shell` 去读 localStorage 是**第二份判据**，
    /// 而且人在面板上刚把自己摆上座位时 `Shell` 根本不重画——那正是 22-A 要堵的那一条缝。
    /// **用 fragment 而不是包一层 div**：fragment 不生成 DOM 节点，
    /// 于是 `div.shell` 下那几个孩子与从前逐个相同（`verify-tracer` 那一趟一条断言不必改）。
    let private devSurface (model: TableModel) : ReactElement list = [
        if Route.devSurfaceRequested () && TableState.devSurfaceAllowed model then
            Html.hr [ prop.key "dev-rule" ]
            App.TracerPage()
    ]

    [<ReactComponent>]
    let Page () =
        let model, dispatch = React.useElmish (init, update, [||])

        // 复盘那一块与时间轴上那几枚标记（票 105）：**一处算、两处消费**。
        // 这一句摆在这里（而不是面板自己的组件里），是因为它两个消费点一个在牌桌上面
        // （`TablePanel.ops` 的时间轴）、一个在牌桌下面（复盘），而它们要标的是同一批手。
        let review = ReviewPanel.useReview (model, dispatch)

        let page =
            match TableState.live model with
            | Some live -> hostPage model review live dispatch
            | None -> homePage model review dispatch

        React.Fragment(page :: devSurface model)
