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
/// **这一层还转出五个入口**：F# 不许同一个模块分在两个文件里（FS0248），而 `Main.fs` 调的是
/// `TablePage.Page`、dotnet 侧的用例（`tests/Janpo.Web.Tests`）调的是 `TablePage.initial` /
/// `init` / `update` / `rosterOf` / `renderingPending`。转出来之后**这个程序集的公开面
/// 与拆之前逐字相同**：那四块里跨文件用的助手一律 `internal`，出不了 `Janpo.Web`。
[<RequireQualifiedAccess>]
module TablePage =

    /// 初次摆的那一桌，配置从外面给。实现与理由见 `TableState.initial`。
    let initial (llmAt: Seat option) (config: LlmSeat) : TableModel * Cmd<TableMsg> = TableState.initial llmAt config

    /// 页面初次打开（上一次填的配置从 localStorage 读回来）。实现见 `TableState.init`。
    let init () : TableModel * Cmd<TableMsg> = TableState.init ()

    /// 一条消息推一步。实现见 `TableState.update`。
    let update (message: TableMsg) (model: TableModel) : TableModel * Cmd<TableMsg> = TableState.update message model

    /// 这一桌的配桌（谁坐哪里）。实现与理由见 `TableState.rosterOf`。
    let rosterOf (model: TableModel) : Roster = TableState.rosterOf model

    /// 人格与模板改过了、但要等下一局才发得出去吗。实现与理由见 `TableState.renderingPending`。
    let renderingPending (model: TableModel) : bool = TableState.renderingPending model

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
                TablePanel.controls model dispatch
                TablePanel.viewpoints model dispatch
                TablePanel.llmPanel model dispatch
                match model.Table with
                | Error message -> Html.p [ prop.className "error"; prop.testId "table-error"; prop.text message ]
                | Ok table -> TableBoard.tableBody model table
            ]
        ]
