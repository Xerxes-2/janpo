namespace Janpo.Web

open Feliz
open Janpo

/// 思考气泡与它点开之后那一屏（票 76；CONTEXT.md 的 `Thinking Bubble`）。
///
/// **这里只画，不判**：三态与「哪一席此刻该有哪一个」全在 `TableState.bubbles` 那个取值器里，
/// 全文面板摊开的那一手在 `TableState.detail` 里——两处都是纯的，dotnet 侧的用例读的就是它们。
///
/// **气泡是座位面板里的一行，不做绝对定位**：票面那条「气泡不许挡住牌与河」因此是**结构上的事实**
/// 而不是纪律——它与三排牌各占各的行，压不到任何一张牌上。闸门另外核两者的矩形不相交
/// （`web/scripts/verify-bubbles.mjs`），因为「结构上不会」这句话也得有人执行。
[<RequireQualifiedAccess>]
module ThinkingBubble =

    // ---- 视图：一席旁边那个气泡 ----

    /// 一席的气泡。`data-bubble` 是三态给机器看的那一半，画法（虚线 / 实线 / 红）是给人看的那一半。
    ///
    /// **「在想」那一态点不开**：它还没有记录（`Bubble.record` 是这件事的唯一判据），
    /// 点开一个空面板比点不开更难懂。
    let internal bubble (dispatch: TableMsg -> unit) (seat: Seat) (state: Bubble) =
        let turn = Bubble.record state |> Option.map (fun record -> record.Turn)
        let wire = Bubble.toWire state

        let hint =
            match turn with
            | Some turn -> $"点开看第 {turn} 手的全文与当时的局面"
            | None -> "正在等这一席回话"

        // 「在想」那一态的已等秒数与上限（票 74；72-3 裁决明写的代价）：
        // 人读的是气泡里那句「已等 N 秒 / 上限 M 秒」，闸门读的是这两个 `data-*`。
        let waiting =
            match state with
            | Bubble.Thinking(waited, limit) -> [
                prop.custom ("data-waited", string waited)
                prop.custom ("data-wait-limit", string limit)
              ]
            | Bubble.Spoke _
            | Bubble.Troubled _ -> []

        Html.button (
            [
                prop.key "bubble"
                prop.className $"bubble {wire}"
                prop.testId $"seat-{Seat.index seat}-bubble"
                prop.custom ("data-bubble", wire)
                // 那一手的手序：闸门拿它与牌谱里那一条记录对得上（人读的是气泡里的字）。
                prop.custom ("data-bubble-turn", turn |> Option.map string |> Option.defaultValue "")
                prop.disabled (Option.isNone turn)
                prop.title hint
                prop.onClick (fun _ -> turn |> Option.iter (fun turn -> dispatch (RecordOpened(Some turn))))
            ]
            @ waiting
            @ [
                prop.children [
                    Html.span [
                        prop.key "who"
                        prop.className "bubble-who"
                        prop.text (Bubble.toLabel state)
                    ]
                    Html.span [
                        prop.key "said"
                        prop.className "bubble-said"
                        prop.text (Bubble.toDisplay state)
                    ]
                ]
            ]
        )

    /// 这一席此刻的气泡，没有就是一行都不画（bot 席、分享链接那种棋谱）。
    let internal at (dispatch: TableMsg -> unit) (seat: Seat) (state: Bubble option) : ReactElement list =
        state |> Option.toList |> List.map (bubble dispatch seat)

    // ---- 视图：点开之后那一屏（spec 的 story 5） ----

    /// 面板里的一行。`value` 是整段文字时按行断开（thinking 与 prompt 尾部都是整段）。
    let private row (testId: string) (label: string) (value: string) =
        Html.div [
            prop.key testId
            prop.className "bubble-row"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.span [ prop.className "bubble-text"; prop.testId testId; prop.text value ]
            ]
        ]

    /// 可缺省的那几格：交不出来的时候**说一句「没有」而不是留一格空白**
    /// （空白与「这一格没画出来」在页面上分不开，票 32 扫同类隐形时收的那条）。
    let private said (missing: string) (value: string option) : string = value |> Option.defaultValue missing

    /// 全文面板（票 76）：thinking 全文、那一句理由、prompt 尾部、动作 id 集、最终落定的动作、
    /// 延迟、问了几次、Usage、渲染版本——**九样都在这一条记录与那一帧上**，不必再去别处取。
    ///
    /// **牌桌上摆的就是这一手落定那一刻**（`TableState.shown`）：story 5 的「局面快照」不是
    /// 另画一张牌桌，而是把同一份渲染指到那一帧上。因此这里只用说一句「你正在看哪一刻」。
    let internal detail (model: TableModel) (dispatch: TableMsg -> unit) : ReactElement list =
        match TableState.detail model with
        | None -> []
        | Some detail ->
            let record = detail.Record
            let context = GameState.context detail.Snapshot.State

            // 最终落定的那个动作。**牌谱里存的是包内 id**（26-3：意图不上牌谱），
            // 而这一帧的 `Latest` 就是那一手真落进引擎的动作——快照因此顺带把它答出来了。
            let appliedId = record.Applied |> Option.map string |> said "—"

            let applied =
                detail.Snapshot.Latest
                |> Option.map (fun turn -> $"{Action.toDisplay turn.Action}（包内 id {appliedId}）")
                |> said "（这一帧没落定动作）"

            let usage = record.Usage |> Option.map Usage.toDisplay |> said "（这一手没有账单）"

            // 空串也当「没有」：没填 key 那几手连 prompt 都没渲染过（同 `TablePanel.renderingLine`）。
            let version =
                if record.RenderVersion = "" then
                    "（这一手没渲染过 prompt）"
                else
                    record.RenderVersion

            let head =
                $"第 {record.Turn} 手・座位 {Seat.index record.Seat}・"
                + $"{Kaze.toDisplay context.Bakaze}{context.Kyoku}局 {context.Honba} 本场"

            [
                Html.section [
                    prop.key "bubble-detail"
                    prop.className "settlement bubble-detail"
                    prop.testId "table-bubble-detail"
                    prop.custom ("data-bubble-turn", record.Turn)
                    prop.custom ("data-bubble-seat", Seat.index record.Seat)
                    prop.children [
                        Html.div [
                            prop.key "head"
                            prop.className "bubble-head"
                            prop.children [
                                Html.h3 [ prop.key "title"; prop.testId "bubble-at"; prop.text head ]
                                Html.button [
                                    prop.key "close"
                                    prop.testId "bubble-close"
                                    prop.onClick (fun _ -> dispatch (RecordOpened None))
                                    prop.text "收起"
                                ]
                            ]
                        ]
                        Html.p [
                            prop.key "snapshot"
                            prop.className "intro"
                            prop.testId "bubble-snapshot"
                            prop.text "上面那张牌桌就是这一手落定那一刻的局面快照（只读；这一桌该怎么走还怎么走）。"
                        ]
                        row "bubble-applied" "最终落定" applied
                        row "bubble-fallback" "兜底" (said "（不是兜底：它自己决的）" record.Fallback)
                        row "bubble-reason" "一句话理由" (said "（这一手没给理由）" record.Reason)
                        row "bubble-thinking" "thinking 全文" (said "（这一手没有思考原文：多半关着思考预算）" record.Thinking)
                        row "bubble-prompt" "prompt 尾部" record.PromptTail
                        row "bubble-actions" "动作 id 集" (record.ActionIds |> List.map string |> String.concat "、")
                        row "bubble-meta" "这一次问话" $"延迟 {record.LatencyMs} ms・问了 {record.Attempts} 次・{usage}"
                        row "bubble-version" "渲染版本" version
                    ]
                ]
            ]

    // ---- 视图：没有记录时的那一句话 ----

    /// 这份牌谱一条决策记录都没有时，**页面上要说清楚为什么没有气泡**（票面第四条）。
    ///
    /// 不说的话，「这份牌谱本来就不带推理」与「气泡坏了」在页面上长得一模一样。
    let internal note (model: TableModel) : ReactElement list =
        if TableState.recordless model then
            [
                Html.p [
                    prop.key "no-bubbles"
                    prop.className "intro"
                    prop.testId "table-no-bubbles"
                    prop.text "这一局没有思考气泡：牌谱里一条决策记录都没有——要么四家都是自带 bot，要么这是一条只带棋谱的分享链接（推理不上 URL，完整版得让对方把 JSON 给你）。"
                ]
            ]
        else
            []
