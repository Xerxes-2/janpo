namespace Janpo.Web

open Feliz
open Janpo

/// 配桌与模型面板（票 70 从 `TablePage.fs` 拆出来的第二块）：播放控制、
/// **配桌那三项规则**（对局长度 / 赤宝牌 / 食断，票 72）、视角与种子那一排、
/// 模型坐席与 provider / 模型 / key / 超时 / 思考预算 / 脚手架 / 人格 / 模板那一整屏。
///
/// **控件工厂也在这里**（`button` / `picker` / `textField` / `areaField` / `selectField`）：
/// 它们只被这一屏用。牌桌本体在 `TableBoard`，页面状态在 `TableState`。
[<RequireQualifiedAccess>]
module TablePanel =

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

    /// 倍速那一排。**两种来源共用**：回放与 Live 的「一手」是同一个粒度（一次提交）。
    let private speeds (model: TableModel) (dispatch: TableMsg -> unit) =
        Speed.all
        |> List.map (fun speed ->
            picker
                $"table-speed-{Speed.toDisplay speed}"
                (model.Playback.Speed = speed)
                (Speed.toDisplay speed)
                (SpeedPicked speed)
                dispatch)

    /// 播 / 暂停那一枚。两种来源共用（testId 也同一个）。
    let private playButton (model: TableModel) (dispatch: TableMsg -> unit) =
        button
            "table-play"
            (not (TableState.canAdvance model))
            (if model.Playback.Playing then "暂停" else "播放")
            PlayToggled
            dispatch

    /// 主持人那一页的控制条（`?table=1`）：单步 / 下一局 / 导出牌谱都只属于 Live。
    let private hostControls (model: TableModel) (live: LiveTable) (dispatch: TableMsg -> unit) =
        let running = TableState.canAdvance model

        let ended =
            match live.Table with
            | Ok table -> Table.isKyokuEnded table && (Table.result table |> Option.isNone)
            | Error _ -> false

        Html.div [
            prop.className "controls"
            prop.children (
                [
                    playButton model dispatch
                    button "table-step" (not running) "单步" Advanced dispatch
                    button "table-next" (not ended) "下一局" KyokuAdvanced dispatch
                    // 牌谱随时导得出来，不必等终局：打到一半的事件流同样 fold 得回去。
                    button "table-export" (Result.isError live.Table) "导出牌谱" Exported dispatch
                    Html.span [ prop.key "speed-label"; prop.className "label"; prop.text "倍速" ]
                ]
                @ speeds model dispatch
            )
        ]

    /// 首页回放的控制条（票 71）：只有播 / 暂停、「从头再放」与倍速。
    ///
    /// **没有单步**：时间轴（拖动与逐事件步进）是票 75 的活，本票的回放只顺着播。
    /// **没有「下一局」**：局间那一步就写在牌谱里，回放自己走过去。
    let private replayControls (model: TableModel) (dispatch: TableMsg -> unit) =
        Html.div [
            prop.className "controls"
            prop.children (
                [
                    playButton model dispatch
                    button "table-restart" false "从头再放" Restarted dispatch
                    Html.span [ prop.key "speed-label"; prop.className "label"; prop.text "倍速" ]
                ]
                @ speeds model dispatch
            )
        ]

    /// 控制条。**牌从哪来决定摆哪几个按钮**（票 71），而播放本身两边共用一份实现。
    let internal controls (model: TableModel) (dispatch: TableMsg -> unit) =
        match TableState.live model with
        | Some live -> hostControls model live dispatch
        | None -> replayControls model dispatch

    /// 配桌那一排（票 72）：**对局长度 / 赤宝牌 / 食断**，加一句「什么时候生效」。
    ///
    /// **只属于 Live**：回放那一侧的规则集是牌谱自带的那一份（ADR-0004），拨不动。
    ///
    /// **拨完不当场生效**：与种子同一条路，要按下面那枚「重开」。半场换规则会让同一份
    /// 牌谱前后按两套规则算，回放就重现不了。因此这一排末尾那一格把两件事都印出来：
    /// **这一桌真在按的那一份**（`data-rules`，与导出牌谱里的 `ruleset` 逐项对得上），
    /// 以及拨到的那三项是不是已经与它不同了（`data-rules-pending`，就是 `TableState.rulesPending`）。
    let internal setup (model: TableModel) (live: LiveTable) (dispatch: TableMsg -> unit) =
        let lengths =
            GameLength.all
            |> List.map (fun length ->
                picker
                    $"table-length-{GameLength.toWire length}"
                    (live.Rules.Length = length)
                    (GameLength.toDisplay length)
                    (RulePicked(RuleChoice.Length length))
                    dispatch)

        // 一根轴两枚按钮（有 / 无），与 bot 那一排同一个形状。**择一而不是打勾**：
        // 摆出来的两枚里哪一枚亮着，一眼就看得出现在拨在哪边。
        let axis (name: string) (chosen: bool) (choose: bool -> RuleChoice) =
            [ true; false ]
            |> List.map (fun on ->
                picker
                    $"table-{name}-{RulesetDraft.switchToWire on}"
                    (chosen = on)
                    (RulesetDraft.switchToDisplay on)
                    (RulePicked(choose on))
                    dispatch)

        let pending = TableState.rulesPending model

        let said =
            if pending then
                "拨好了：按「重开」才开出新的一桌（不半场换规则）。"
            else
                "这一桌就是按这三项开的。"

        let label (key: string) (text: string) =
            Html.span [ prop.key key; prop.className "label"; prop.text text ]

        Html.div [
            prop.className "controls"
            prop.children (
                (label "length-label" "对局长度" :: lengths)
                @ (label "akadora-label" "赤宝牌"
                   :: axis "akadora" live.Rules.Akadora RuleChoice.Akadora)
                @ (label "kuitan-label" "食断" :: axis "kuitan" live.Rules.Kuitan RuleChoice.Kuitan)
                @ [
                    Html.span [
                        prop.key "rules"
                        prop.className (if pending then "rendering pending" else "rendering")
                        prop.testId "table-rules"
                        prop.custom ("data-rules", RulesetDraft.ofRuleset model.Ruleset |> RulesetDraft.toWire)
                        prop.custom ("data-rules-pending", (if pending then "true" else "false"))
                        prop.text said
                    ]
                ]
            )
        ]

    /// 视角那一排。**两种来源共用**（回放里视角照旧切得动）；
    /// 种子与「重开」只属于 Live——回放的牌是**录下来的**，没有种子可换。
    let internal viewpoints (model: TableModel) (dispatch: TableMsg -> unit) =
        let seats =
            Seat.all model.Ruleset
            |> List.map (fun seat ->
                picker
                    $"table-view-{Seat.index seat}"
                    (model.Viewpoint = Viewpoint.Seated seat)
                    $"座位 {Seat.index seat}"
                    (ViewpointPicked(Viewpoint.Seated seat))
                    dispatch)

        let seeding =
            match TableState.live model with
            | None -> []
            | Some live -> [
                Html.span [ prop.key "seed-label"; prop.className "label"; prop.text "种子" ]
                Html.input [
                    prop.key "seed-input"
                    prop.testId "table-seed"
                    prop.value live.SeedText
                    prop.onChange (SeedEdited >> dispatch)
                ]
                button "table-restart" false "重开" Restarted dispatch
              ]

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
                ]
                @ seeding
            )
        ]

    // ---- 视图：配置面板 ----

    /// 一个文本输入框。`kind` 是 `password` 时人看不见自己填的 key。
    let private textField
        (testId: string)
        (kind: string)
        (label: string)
        (which: LlmField)
        (live: LiveTable)
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
                    prop.value (LlmSeat.field which live.Llm)
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
        (live: LiveTable)
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
                    prop.value (LlmSeat.field which live.Llm)
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
        (live: LiveTable)
        (dispatch: TableMsg -> unit)
        =
        Html.label [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.select [
                    prop.testId testId
                    prop.value (LlmSeat.field which live.Llm)
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
    let private renderingLine (model: TableModel) (live: LiveTable) =
        // 空串也当「还没有」：没填 key 那几手连 prompt 都没渲染过（记录仍然留一条，
        // 内容就是那句原因），印一个空版本号比不印更难读。
        let version =
            match live.Table with
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

        let pending = TableState.renderingPending model

        let note = if pending then "　人格 / 模板改过了：本局仍用定型那一版，下一局生效。" else ""

        Html.p [
            prop.key "rendering"
            prop.className (if pending then "rendering pending" else "rendering")
            prop.testId "table-render-version"
            prop.custom ("data-render-version", Option.defaultValue "" version)
            prop.custom ("data-rendering-pending", (if pending then "true" else "false"))
            prop.text (said + note)
        ]

    /// 模型面板底下那一段说明。
    ///
    /// **超时那一句里的数字插值而不手写**（票 72）：默认值只有 `LlmSeat.initial` 一个真源，
    /// 下一个人改它时面板上那句话不会静默地过期（从前写在注释里的「30 秒」就过了一年才被发现）。
    let private panelNote =
        "key 只存在这台浏览器的 localStorage 里，请求由浏览器直发 provider，不经本平台（它没有后端）。订阅制的 OAuth 登录在浏览器里用不了，只能填 API key。脚手架换成信息辅助后，prompt 里会多一节引擎算好的向听数、有效牌与逐张试打的进退向。人格与模板是另一个维度：它们只换措辞，不换给不给那几个算好的数；两格都在可缓存的前缀里，因此一局之内不会变——开局后再改照样存住，但要下一局才发得出去（上面那行会直说）。模型超时、报错或给不出合法动作时，重试两次仍不行就兜底代打（裸奔档摸切，信息辅助档打一张不退向听的）；认证失败这类再问一遍还是一样的错不重试，直接兜底。对局不会卡住。"
        + $"超时那一格默认 {LlmSeat.initial.TimeoutMs} ms（{LlmSeat.initial.TimeoutMs / 1000} 秒）：开着思考预算的模型单手实测要 17–180 秒，调得太短会把正在想的那一手提前掐成兜底；本地模型首次加载更慢，该调大就调大。"

    /// 配桌：哪个座位交给 LLM，以及那个座位的 provider / 模型 / key / 超时 / 思考预算。
    ///
    /// **key 只进 localStorage**（`Store`），不外发到本平台——本平台根本没有后端。
    /// **不提供订阅制登录**：pi-ai 的 OAuth 流程是 Node-only（票 18），浏览器里只有 API key
    /// 这一条路；Bedrock 同理不在 provider 列表里。
    ///
    /// **baseUrl 那一格只在选了自定义端点时出现**（票 30）：官方八家根本不看它，
    /// 摆在那里只会让人以为能把 DeepSeek 改道。
    let internal llmPanel (model: TableModel) (live: LiveTable) (dispatch: TableMsg -> unit) =
        let seats =
            picker "table-llm-none" (Option.isNone live.LlmAt) "无" (LlmSeatPicked None) dispatch
            :: [
                for seat in Seat.all model.Ruleset ->
                    picker
                        $"table-llm-{Seat.index seat}"
                        (live.LlmAt = Some seat)
                        $"座位 {Seat.index seat}"
                        (LlmSeatPicked(Some seat))
                        dispatch
            ]

        // 剩下那几家由哪种自带 bot 坐（票 42）。**均匀随机是默认**：它是对拍与闸门的基准；
        // 有主见的那个能和就和、听牌就立直，立直与供托那几条路径靠它才真的走得到。
        let bots =
            Bot.all
            |> List.map (fun kind ->
                picker $"table-bot-{Bot.toWire kind}" (live.Bot = kind) (Bot.toDisplay kind) (BotPicked kind) dispatch)

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
                            live
                            dispatch
                        // 模型名一直是自由文本：本地模型叫 `qwen3:8b` 的叫 `gpt-oss-20b@q4` 的都有，
                        // 下拉框只会挡路。
                        textField "table-llm-model" "text" "模型" LlmField.Model live dispatch
                        if LlmSeat.isCustom live.Llm then
                            textField "table-llm-base-url" "text" "baseUrl" LlmField.BaseUrl live dispatch
                        textField "table-llm-key" "password" "API key" LlmField.ApiKey live dispatch
                        textField "table-llm-timeout" "number" "超时 (ms)" LlmField.TimeoutMs live dispatch
                        selectField
                            "table-llm-thinking"
                            "思考预算"
                            LlmField.Thinking
                            (Thinking.all
                             |> List.map (fun level -> Thinking.toWire level, Thinking.toDisplay level, true))
                            live
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
                            live
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
                            live
                            dispatch
                        areaField
                            "table-llm-template"
                            "prompt 模板（一局内不变）"
                            "留空就用默认模板。一段 JSON，给几项换几项：{\"id\":\"我的模板\",\"labels\":{\"history\":\"【战况回放】\"}}"
                            LlmField.Template
                            live
                            dispatch
                    ]
                ]
                renderingLine model live
                Html.p [ prop.key "note"; prop.className "intro"; prop.text panelNote ]
                // 自定义端点那段话只在选中它时出现：两个坑（CORS、mixed content）说清楚要一整段，
                // 而它们与官方八家无关。完整配法在 `docs/host/custom-endpoint.md`。
                if LlmSeat.isCustom live.Llm then
                    Html.p [
                        prop.key "custom-note"
                        prop.className "intro"
                        prop.testId "table-llm-custom-note"
                        prop.text
                            "自定义端点：baseUrl 填到含 /v1 那一层（Ollama 是 http://localhost:11434/v1，LM Studio 是 http://localhost:1234/v1），本地端点通常不用填 key，模型名照端点里的实名填。两个坑要先踩平：端点默认不放行浏览器跨域（Ollama 设 OLLAMA_ORIGINS，LM Studio 在设置里开 CORS），且 https 页面调 http 端点会被浏览器拦掉——配法见 docs/host/custom-endpoint.md。接不上时上面那行会直说「连不上自定义端点」，那不是模型不肯选。"
                    ]
            ]
        ]
