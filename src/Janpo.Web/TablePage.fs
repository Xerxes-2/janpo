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
/// **这一层还转出十个入口**：F# 不许同一个模块分在两个文件里（FS0248），而 `Main.fs` 调的是
/// `TablePage.Page`、dotnet 侧的用例（`tests/Janpo.Web.Tests`）调的是 `TablePage.initial` /
/// `init` / `update` / `rosterOf` / `seatConfigOf` / `renderingPending` / `rulesPending` / `live` /
/// `shown` / `canAdvance` / `timeline`。转出来之后**这个程序集的公开面只多了这几个名字**：那四块里
/// 跨文件用的助手一律 `internal`，出不了 `Janpo.Web`。
///
/// **这一页现在有两个布局**（票 71）：`/` 是首页的 Demo 回放（自动播，没有配桌与模型面板），
/// `?table=1` 是主持人自己开的一桌（今天那一页一字不少）。**牌桌与结算的渲染只有一份**
/// （`board`），播放、视角与危险度那两排也只有一份——分岔只在「摆哪几个按钮」上。
[<RequireQualifiedAccess>]
module TablePage =

    /// `?table=1` 初次摆的那一桌，配桌那三项与**坐法**（档案库 + 四席绑定）都从外面给。
    /// 实现与理由见 `TableState.initial`。
    let initial (rules: RulesetDraft) (seating: SeatingPlan) : TableModel * Cmd<TableMsg> =
        TableState.initial rules seating

    /// 首页（`/`）初次摆的那一屏：一份还没拉回来的 Demo 回放。实现见 `TableState.home`。
    let home () : TableModel * Cmd<TableMsg> = TableState.home ()

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

    /// 人格与模板改过了、但要等下一局才发得出去吗。实现与理由见 `TableState.renderingPending`。
    let renderingPending (model: TableModel) : bool = TableState.renderingPending model

    /// 配桌那三项拨过了、但要按「重开」才生效吗。实现与理由见 `TableState.rulesPending`。
    let rulesPending (model: TableModel) : bool = TableState.rulesPending model

    /// 还推得动吗（播放那一枚按钮灰不灰）。实现与理由见 `TableState.canAdvance`。
    let canAdvance (model: TableModel) : bool = TableState.canAdvance model

    /// 回放的时间轴（票 75）；Live 时是 None。实现与理由见 `TableState.timeline`。
    let timeline (model: TableModel) : Timeline option = TableState.timeline model

    // ---- 视图 ----

    /// 牌桌那一格。**两种来源共用这一份**（票 71）：Live 画正在打的那一桌，
    /// 回放画那份牌谱 fold 出来的第 N 帧，往下（`TableBoard`）一行都不分岔。
    ///
    /// 三种状态各有各的说法：还在拉、出了事、以及真有一桌。**白屏不在其列**——
    /// 首页拉不到资产时人得看见一句原因。
    let private board (model: TableModel) =
        match TableState.shown model with
        | Shown.Loading ->
            Html.p [
                prop.className "intro"
                prop.testId "table-loading"
                prop.text "正在取那一局录下来的对局……"
            ]
        | Shown.Fault reason -> Html.p [ prop.className "error"; prop.testId "table-error"; prop.text reason ]
        | Shown.Board table -> TableBoard.tableBody model table

    /// 首页（`/`）：**访客的第一眼是一桌牌在走**（spec 的 story 1，ADR-0003 由 Demo Paifu 兑现）。
    ///
    /// **没有配桌与模型面板**：第一眼不该是一张表单（票 35 的「默认视图只该有牌桌」同一条标准）。
    /// 想自己开一桌的人走那条链接去 `?table=1`——Host 那一侧访客得摸得到。
    let private homePage (model: TableModel) (dispatch: TableMsg -> unit) =
        Html.div [
            prop.className "page table-page"
            prop.children [
                // h1 抄的是 `web/index.html` 那个 `<title>`（票 33 定的稿）：
                // 标签页上与页面上不该各说各的。改品牌语先改那一行，再抄过来。
                Html.h1 "janpo —— 浏览器里的 LLM 日麻竞技场"
                Html.p [
                    prop.className "intro"
                    prop.testId "home-intro"
                    prop.text
                        "下面这一局是录下来的，正在自动回放——不用配置、不用 API key，打开就看得见牌怎么走。这是上帝视角，四家的牌都摊着——牌谱已经打完了，复盘本来就该看得见四家；想验「模型看到的和你一样多」就按一下坐到某个座位，那时他家的暗牌在页面拿到的数据里根本不存在。虚线的牌是摸切。拖时间轴回看任意一手，或者点局号跳到那一局的开局。"
                ]
                Html.p [
                    prop.className "intro"
                    prop.children [
                        Html.a [ prop.testId "home-host-link"; prop.href Route.tableHref; prop.text "自己开一桌 →" ]
                        Html.span [ prop.text "　自带 API key，把一个座位交给模型，看它一手一手打。" ]
                    ]
                ]
                TablePanel.controls model dispatch
                TablePanel.viewpoints model dispatch
                board model
            ]
        ]

    /// 主持人那一页（`?table=1`）：配桌、模型面板、种子、单步 / 播放 / 倍速、导出、下一局、
    /// 视角、危险度——**今天那一页一字不少**（票 71 只换了它的地址）。
    ///
    /// **默认暂停**（`Playback.initial`）：要点、要读牌桌的那几道无头闸门全靠这一条。
    let private hostPage (model: TableModel) (live: LiveTable) (dispatch: TableMsg -> unit) =
        Html.div [
            prop.className "page table-page"
            prop.children [
                Html.h1 "janpo —— 浏览器里的 LLM 日麻竞技场"
                Html.p [
                    prop.className "intro"
                    prop.text
                        "默认四家自带选手（均匀随机）；下面四行是四个座位各自的绑定——每一席可以换成「有主见」，也可以交给一份模型档案（key 在档案里只填一次，一把 key 坐几席都行，四家全是模型也行）。按「播放」看它们一手一手打。他家的手牌看不到牌面——模型看到的和你一样多，别人的暗牌在页面拿到的数据里根本不存在；想复盘就按一下切到上帝视角。虚线的牌是摸切。"
                ]
                TablePanel.controls model dispatch
                // 配桌那三项（票 72）摆在种子与「重开」那一排上面：它们走的是同一条路
                // ——拨完都要按那一枚「重开」才开出新的一桌。
                TablePanel.setup model live dispatch
                TablePanel.viewpoints model dispatch
                TablePanel.llmPanel model live dispatch
                board model
            ]
        ]

    [<ReactComponent>]
    let Page () =
        let model, dispatch = React.useElmish (init, update, [||])

        match TableState.live model with
        | Some live -> hostPage model live dispatch
        | None -> homePage model dispatch
