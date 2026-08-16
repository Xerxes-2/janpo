namespace Janpo.Web

open Elmish
open Feliz
open Feliz.UseElmish
open Janpo

/// 同一种子的两跑：一局与一整场。两跑分别对着 dotnet 侧的 `janpo kyoku` 与 `janpo game`，
/// 验收要的「逐项相同」比的就是它们的 `Scores` 与 `Juni`。
type Traces = { Kyoku: Trace; Game: Trace }

/// 曳光弹页面的全部状态。**UI 状态 = fold(引擎输出)**：页面自己不算任何牌局的东西，
/// 连顺位都由引擎给（`Tracer.juniOf` 借的是 `Game.settle`）。
type Model = {
    /// 这一页跑的规则集。19 票只跑四麻默认预设，配桌是 22 票之后的事。
    Ruleset: Ruleset
    /// 输入框里的文本。**没解析过**——解析在 `Rerun` 那一步做，因此打字不会触发跑批。
    SeedText: string
    /// 上一次 `Rerun` 的结果。种子不是整数、或引擎报错，都落在 `Error` 那一支。
    Traces: Result<Traces, string>
}

/// 页面上能发生的事只有两件：改种子、重跑。
type Msg =
    | SeedEdited of seed: string
    | Rerun

/// MVU 的三件套加视图。update 循环与引擎的事件溯源同构（ADR-0005 选 B 的理由之一）。
[<RequireQualifiedAccess>]
module App =

    // ---- 跑 ----

    /// 把输入框里的文本跑成两条 `Trace`。种子不是整数时不去麻烦引擎，直接给错误文案。
    let private run (ruleset: Ruleset) (seedText: string) : Result<Traces, string> =
        match System.Int32.TryParse(seedText.Trim()) with
        | false, _ -> Error $"种子要是一个整数，得到「{seedText}」"
        | true, seed ->
            Tracer.kyoku ruleset seed
            |> Result.bind (fun kyoku ->
                Tracer.game ruleset seed
                |> Result.map (fun game -> { Kyoku = kyoku; Game = game }))

    // ---- MVU ----

    let init () : Model * Cmd<Msg> =
        let ruleset = Ruleset.yonma
        let seedText = string Tracer.defaultSeed

        {
            Ruleset = ruleset
            SeedText = seedText
            Traces = run ruleset seedText
        },
        Cmd.none

    let update (message: Msg) (model: Model) : Model * Cmd<Msg> =
        match message with
        | SeedEdited seed -> { model with SeedText = seed }, Cmd.none
        | Rerun ->
            {
                model with
                    Traces = run model.Ruleset model.SeedText
            },
            Cmd.none

    // ---- 视图 ----

    /// 一行「标题 + 一串数」。`testId` 是无头验收脚本读的钩子，
    /// 内容一律是空格分隔的整数，与 CLI 的 `scores:` / `juni:` 那两行同形，好逐项对照。
    let private numbers (testId: string) (label: string) (values: int list) =
        Html.div [
            prop.className "row"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.span [
                    prop.className "values"
                    prop.testId testId
                    prop.text (values |> List.map string |> String.concat " ")
                ]
            ]
        ]

    /// 四家一行的小表：座位、点数、顺位。给人看的那一份，数与上面的 `numbers` 同源。
    let private seatTable (trace: Trace) =
        Html.table [
            prop.className "seats"
            prop.children [
                Html.thead [ Html.tr [ Html.th "座位"; Html.th "点数"; Html.th "顺位" ] ]
                Html.tbody [
                    for seat, score, juni in List.zip3 [ 0 .. List.length trace.Scores - 1 ] trace.Scores trace.Juni ->
                        Html.tr [
                            prop.key seat
                            prop.children [ Html.td (string seat); Html.td (string score); Html.td $"{juni}位" ]
                        ]
                ]
            ]
        ]

    /// 事件流的头尾各三条 JSON。它证明的是 **Thoth 的 JS 后端也通了**，
    /// 不是给人读牌谱用的——读牌谱是 22 票之后的事。
    let private eventPeek (testId: string) (events: Event list) =
        let count = List.length events

        let sample =
            if count <= 6 then
                events
            else
                List.truncate 3 events @ List.skip (count - 3) events

        Html.details [
            prop.className "events"
            prop.children [
                Html.summary $"mjai 事件 {count} 条（头尾各三条）"
                Html.pre [
                    prop.testId testId
                    prop.text (sample |> List.map Tracer.eventJson |> String.concat "\n")
                ]
            ]
        ]

    /// 一跑的卡片。`prefix` 决定 testId 的前缀（`kyoku` / `game`），与 CLI 的子命令同名。
    let private card (prefix: string) (title: string) (command: string) (trace: Trace) =
        Html.section [
            prop.className "card"
            prop.children [
                Html.h2 title
                Html.p [ prop.className "command"; prop.testId $"{prefix}-command"; prop.text command ]
                numbers $"{prefix}-scores" "scores" trace.Scores
                numbers $"{prefix}-juni" "juni" trace.Juni
                Html.div [
                    prop.className "row"
                    prop.children [
                        Html.span [ prop.className "label"; prop.text "kyokus" ]
                        Html.span [
                            prop.className "values"
                            prop.testId $"{prefix}-kyokus"
                            prop.text (string trace.Kyokus)
                        ]
                    ]
                ]
                seatTable trace
                eventPeek $"{prefix}-events" trace.Events
            ]
        ]

    [<ReactComponent>]
    let TracerPage () =
        let model, dispatch = React.useElmish (init, update, [||])

        Html.div [
            prop.className "page"
            prop.children [
                Html.h1 "janpo —— 浏览器里的第一颗曳光弹"
                Html.p [
                    prop.className "intro"
                    prop.text "同一套 F# 引擎源码，经 Fable 编成 JS 在这里跑。下面的点数与顺位由浏览器算出，应与 dotnet 侧同种子的 CLI 输出逐项相同。"
                ]
                Html.div [
                    prop.className "controls"
                    prop.children [
                        Html.label [ prop.htmlFor "seed"; prop.text "种子" ]
                        Html.input [
                            prop.id "seed"
                            prop.testId "seed-input"
                            prop.value model.SeedText
                            prop.onChange (SeedEdited >> dispatch)
                        ]
                        Html.button [ prop.testId "rerun"; prop.onClick (fun _ -> dispatch Rerun); prop.text "重跑" ]
                    ]
                ]
                match model.Traces with
                | Error message -> Html.p [ prop.className "error"; prop.testId "error"; prop.text message ]
                | Ok traces ->
                    Html.div [
                        prop.className "cards"
                        prop.testId "traces"
                        prop.children [
                            card "kyoku" "一局（Kyoku）" $"janpo kyoku {traces.Kyoku.Seed}" traces.Kyoku
                            card "game" "一整场（东风战）" $"janpo game {traces.Game.Seed}" traces.Game
                        ]
                    ]
            ]
        ]
