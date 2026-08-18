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

    /// 时间轴那一根滑块（票 75）。**滑块的 `value` 就是游标**：拖到哪一帧牌桌就是那一帧
    /// （O(1) 取帧，帧在载入时一次 fold 好）。旁边那句话说的是「第几手 / 第几局」。
    ///
    /// `prop.onChange` 收 `int` 那一条重载读的是 `valueAsNumber`，正是 range 输入框该用的那一个。
    /// `data-*` 给无头闸门读：人读的是那句中文，机器读的是它们，两边对不上就是错。
    let private timelineRow (timeline: Timeline) (dispatch: TableMsg -> unit) =
        let at =
            timeline.Marks
            |> List.tryItem timeline.Kyoku
            |> Option.map (fun mark -> mark.Label)
            |> Option.defaultValue "—"

        Html.div [
            prop.key "timeline"
            prop.className "controls timeline-row"
            prop.children [
                button "table-back" (timeline.Cursor <= 0) "上一步" (CursorMoved(timeline.Cursor - 1)) dispatch
                button
                    "table-forward"
                    (timeline.Cursor >= timeline.Last)
                    "下一步"
                    (CursorMoved(timeline.Cursor + 1))
                    dispatch
                Html.input [
                    prop.key "slider"
                    prop.testId "table-timeline"
                    prop.className "timeline"
                    prop.type' "range"
                    prop.min 0
                    prop.max timeline.Last
                    prop.value timeline.Cursor
                    prop.custom ("data-cursor", timeline.Cursor)
                    prop.custom ("data-last", timeline.Last)
                    prop.onChange (fun (frame: int) -> dispatch (CursorMoved frame))
                ]
                Html.span [
                    prop.key "at"
                    // `timeline-at` 把它的宽度钉住：「第 3 手・东1局」与「第 191 手・南4·2局」不一样宽，
                    // 不钉的话滑块会在拖动中途自己伸缩——拖到一半手底下的东西会跑。
                    prop.className "values timeline-at"
                    prop.testId "table-timeline-at"
                    prop.custom ("data-turns", timeline.Turns)
                    prop.custom ("data-kyoku", timeline.Kyoku)
                    prop.text $"第 {timeline.Turns} 手・{at}局"
                ]
            ]
        ]

    /// 局边界那一排（票 75）：一局一枚，点下去就是把游标挪到那一局的**开局帧**。
    ///
    /// **不另立一条消息**：跳局就是拖到那一帧（`CursorMoved`），游标怂一条路。
    let private kyokuRow (timeline: Timeline) (dispatch: TableMsg -> unit) =
        Html.div [
            prop.key "kyoku"
            prop.className "controls kyoku-row"
            prop.children (
                Html.span [ prop.key "kyoku-label"; prop.className "label"; prop.text "跳到" ]
                :: (timeline.Marks
                    |> List.mapi (fun index mark ->
                        picker
                            $"table-kyoku-{index}"
                            (index = timeline.Kyoku)
                            mark.Label
                            (CursorMoved mark.Frame)
                            dispatch))
            )
        ]

    /// 首页回放的控制条（票 71）与它的时间轴（票 75）：
    /// 播 / 暂停、「从头再放」与倍速一排，步进与滑块一排，局边界一排。
    ///
    /// **没有「下一局」那枚按钮**：局间那一步就写在牌谱里，回放自己走过去；
    /// 想直接去某一局的走局边界那一排（它拖的仍然是同一个游标）。
    /// **暂停与倍速照旧**：回放与 Live 共用同一份 `Playback`，没有第二套定时器。
    let private replayControls (model: TableModel) (dispatch: TableMsg -> unit) =
        let playRow =
            Html.div [
                prop.key "play"
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

        // 牌谱还没拉回来（或者拉不动）时根本没有帧，那时只摆控制条。
        let rails =
            match TableState.timeline model with
            | None -> []
            // 票 75 那段「把刚落定那一手的记录原文印出来」的应急形态（`table-replay-record`）
            // **已经换成牌桌上的思考气泡**（票 76）：记录本来就该贴在说那句话的那一席旁边，
            // 而不是堆在控制条下面。`Timeline.Record` 还在（票 75 的用例钉着它）。
            | Some timeline -> [ timelineRow timeline dispatch; kyokuRow timeline dispatch ]

        Html.div [ prop.className "replay-controls"; prop.children (playRow :: rails) ]

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

    // ---- 视图：四席绑定与模型档案（票 73） ----

    /// 一格文本输入。`kind` 是 `password` 时人看不见自己填的 key。
    ///
    /// **三个控件工厂收的都是「现在的值 + 改成什么」**（而不是一个字段枚举加一份配置）：
    /// 档案编辑处与四席那几行填的是两种东西（`ModelProfile` 与 `SeatBinding`），
    /// 共用同一批控件的唯一办法就是让它们只认字符串。
    let private textField
        (testId: string)
        (kind: string)
        (label: string)
        (value: string)
        (edited: string -> TableMsg)
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
                    prop.value value
                    prop.onChange (edited >> dispatch)
                ]
            ]
        ]

    /// 一格多行文本（票 31）：人格与模板都是整段文字，单行输入框里根本读不下来。
    /// **措辞与人格就是从这两格注入的**：改它们不用改代码、不用重编。
    let private areaField
        (testId: string)
        (label: string)
        (hint: string)
        (value: string)
        (edited: string -> TableMsg)
        (dispatch: TableMsg -> unit)
        =
        Html.label [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.textarea [
                    prop.testId testId
                    prop.rows 2
                    prop.placeholder hint
                    prop.value value
                    prop.onChange (edited >> dispatch)
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
        (value: string)
        (options: (string * string * bool) list)
        (edited: string -> TableMsg)
        (dispatch: TableMsg -> unit)
        =
        Html.label [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.select [
                    prop.testId testId
                    prop.value value
                    prop.onChange (edited >> dispatch)
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

    /// 一席那一行（票 73）：**交给谁**（均匀随机 / 有主见 / 库里每份档案各一枚），
    /// 加上这一席自己的脚手架档位、人格与 prompt 模板。
    ///
    /// **四席各一行、全摆在面上**（票面的硬判据）：别让人点四次才知道谁坐哪。
    /// **这一行里没有 key**：key 只出现在下面的档案编辑处，一把 key 坐三席也只填一次。
    let private seatRow (live: LiveTable) (seat: Seat) (dispatch: TableMsg -> unit) =
        let index = Seat.index seat
        let binding = SeatingPlan.bindingAt seat live.Seating

        let bots =
            Bot.all
            |> List.map (fun kind ->
                picker
                    $"table-seat-{index}-{Bot.toWire kind}"
                    (binding.Choice = SeatChoice.Bot kind)
                    (Bot.toDisplay kind)
                    (SeatBound(seat, SeatChoice.Bot kind))
                    dispatch)

        let profiles =
            live.Seating.Profiles
            |> List.mapi (fun each profile ->
                picker
                    $"table-seat-{index}-profile-{each}"
                    (binding.Choice = SeatChoice.Profile profile.Name)
                    profile.Name
                    (SeatBound(seat, SeatChoice.Profile profile.Name))
                    dispatch)

        Html.div [
            prop.key $"seat-{index}"
            prop.className "controls"
            prop.testId $"table-seat-{index}"
            // 这一席拨到了哪儿（给闸门看；人看的是哪一枚按钮亮着）。
            prop.custom ("data-seat-choice", SeatChoice.toWire binding.Choice)
            // 这一席在牌谱里叫什么（`Roster.names` 那一份）：**档案的名字不在里面**。
            prop.custom (
                "data-seat-name",
                SeatingPlan.names live.Seating |> List.tryItem index |> Option.defaultValue ""
            )
            prop.children (
                (Html.span [ prop.key "seat-label"; prop.className "label"; prop.text $"座位 {index}" ]
                 :: bots)
                @ profiles
                @ [
                    // 脚手架档位：**它是实验变量**，主持人在座位上现拨，不用改代码。
                    // 工具搜索档是 M3 的，灰着；它真被选上也不会坏事（prompt 与兜底都退回 Bare）。
                    selectField
                        $"table-seat-{index}-tier"
                        "脚手架"
                        (SeatBinding.field SeatField.Tier binding)
                        (ScaffoldTier.all
                         |> List.map (fun tier ->
                             ScaffoldTier.toWire tier, ScaffoldTier.toDisplay tier, tier <> ScaffoldTier.ToolSearch))
                        (fun value -> SeatEdited(seat, SeatField.Tier, value))
                        dispatch
                    areaField
                        $"table-seat-{index}-persona"
                        "人格（一局内不变）"
                        "留空＝没有人格。例：你是一位以防守见长的雀士，宁可少和一把也不点炮。"
                        (SeatBinding.field SeatField.Persona binding)
                        (fun value -> SeatEdited(seat, SeatField.Persona, value))
                        dispatch
                    areaField
                        $"table-seat-{index}-template"
                        "模板（一局内不变）"
                        "留空＝默认模板。一段 JSON：{\"id\":\"我的\",\"labels\":{\"history\":\"【回放】\"}}"
                        (SeatBinding.field SeatField.Template binding)
                        (fun value -> SeatEdited(seat, SeatField.Template, value))
                        dispatch
                ]
            )
        ]

    /// 档案编辑处（票 73）：库里那几份各一枚、新建与删除，以及**开着那一份**的六格。
    ///
    /// **key 在界面上只出现在这里**（票面的硬判据）：座位那几行不重复填 key，
    /// 因此「同一把 key 坐三席」不必把它填三遍。
    ///
    /// **baseUrl 那一格只在选了自定义端点时出现**（票 30）：官方八家根本不看它，
    /// 摆在那里只会让人以为能把 DeepSeek 改道。
    let private profileEditor (live: LiveTable) (dispatch: TableMsg -> unit) =
        let tabs =
            live.Seating.Profiles
            |> List.mapi (fun index profile ->
                picker $"table-profile-{index}" (index = live.Editing) profile.Name (ProfileOpened index) dispatch)

        let fields =
            match SeatingPlan.profileAt live.Editing live.Seating with
            // 一份档案都没有（全删光了）：只剩「新建」那一枚，没有格子可填。
            | None -> []
            | Some profile ->
                let edited (field: ProfileField) =
                    fun (value: string) -> ProfileEdited(field, value)

                [
                    Html.div [
                        prop.key "profile-fields"
                        prop.className "controls"
                        prop.children [
                            textField "table-profile-name" "text" "档案名" profile.Name (edited ProfileField.Name) dispatch
                            selectField
                                "table-profile-provider"
                                "provider"
                                profile.Provider
                                (ModelProfile.providers
                                 |> List.map (fun name -> name, ModelProfile.providerToDisplay name, true))
                                (edited ProfileField.Provider)
                                dispatch
                            // 模型名一直是自由文本：本地模型叫 `qwen3:8b` 的叫 `gpt-oss-20b@q4` 的都有，
                            // 下拉框只会挡路。
                            textField
                                "table-profile-model"
                                "text"
                                "模型"
                                profile.Model
                                (edited ProfileField.Model)
                                dispatch
                            if ModelProfile.isCustom profile then
                                textField
                                    "table-profile-base-url"
                                    "text"
                                    "baseUrl"
                                    profile.BaseUrl
                                    (edited ProfileField.BaseUrl)
                                    dispatch
                            textField
                                "table-profile-key"
                                "password"
                                "API key"
                                profile.ApiKey
                                (edited ProfileField.ApiKey)
                                dispatch
                            textField
                                "table-profile-timeout"
                                "number"
                                "超时 (ms)"
                                (ModelProfile.field ProfileField.TimeoutMs profile)
                                (edited ProfileField.TimeoutMs)
                                dispatch
                            selectField
                                "table-profile-thinking"
                                "思考预算"
                                (ModelProfile.field ProfileField.Thinking profile)
                                (Thinking.all
                                 |> List.map (fun level -> Thinking.toWire level, Thinking.toDisplay level, true))
                                (edited ProfileField.Thinking)
                                dispatch
                        ]
                    ]
                ]

        // 删掉一份还被座位引用的档案，那几席会退回 bot——**页面把这件事说出来**，
        // 不许静静地变成「没有选手」。这一行只在真发生过之后才出现。
        let notice =
            live.Notice
            |> Option.toList
            |> List.map (fun said ->
                Html.p [
                    prop.key "profile-notice"
                    prop.className "rendering pending"
                    prop.testId "table-profile-notice"
                    prop.text said
                ])

        Html.div [
            prop.key "profiles"
            prop.className "controls"
            prop.children [
                Html.span [ prop.key "profiles-label"; prop.className "label"; prop.text "模型档案" ]
                yield! tabs
                button "table-profile-new" false "新建档案" ProfileAdded dispatch
                button
                    "table-profile-delete"
                    (Option.isNone (SeatingPlan.profileAt live.Editing live.Seating))
                    "删掉这一份"
                    (ProfileDeleted live.Editing)
                    dispatch
            ]
        ]
        :: (fields @ notice)

    /// 四席那几行下面那一行（票 46 的 31-D 与「一局内不变」）。**两件事是一件事的两面**：
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
    /// **超时那一句里的数字插值而不手写**（票 72）：默认值只有 `ModelProfile.initial` 一个真源，
    /// 下一个人改它时面板上那句话不会静默地过期（从前写在注释里的「30 秒」就过了一年才被发现）。
    let private panelNote =
        "一份「模型档案」就是「怎么问这个模型」（provider・模型・key・baseUrl・超时・思考预算），key 只在这里填一次；四席各自挑一份档案（或者自带 bot），并各带自己的脚手架档位、人格与模板——同一份档案坐两席、两席两种人格，那正是对照实验要的形态。key 只存在这台浏览器的 localStorage 里，请求由浏览器直发 provider，不经本平台（它没有后端）。订阅制的 OAuth 登录在浏览器里用不了，只能填 API key。脚手架换成信息辅助后，prompt 里会多一节引擎算好的向听数、有效牌与逐张试打的进退向。人格与模板是另一个维度：它们只换措辞，不换给不给那几个算好的数；两格都在可缓存的前缀里，因此一局之内不会变——开局后再改照样存住，但要下一局才发得出去（上面那行会直说），而且它按座位各算各的。模型超时、报错或给不出合法动作时，重试两次仍不行就兜底代打（裸奔档摸切，信息辅助档打一张不退向听的）；认证失败这类再问一遍还是一样的错不重试，直接兜底。对局不会卡住。"
        + $"超时那一格默认 {ModelProfile.initial.TimeoutMs} ms（{ModelProfile.initial.TimeoutMs / 1000} 秒）：开着思考预算的模型单手实测要 17–180 秒，调得太短会把正在想的那一手提前掐成兜底；本地模型首次加载更慢，该调大就调大。"

    /// 四席绑定 + 模型档案库（票 73）：**四 LLM 同桌**就是这一屏。
    ///
    /// **key 只进 localStorage**（`Store`），不外发到本平台——本平台根本没有后端。
    /// **不提供订阅制登录**：pi-ai 的 OAuth 流程是 Node-only（票 18），浏览器里只有 API key
    /// 这一条路；Bedrock 同理不在 provider 列表里。
    let internal llmPanel (model: TableModel) (live: LiveTable) (dispatch: TableMsg -> unit) =
        let custom = live.Seating.Profiles |> List.exists ModelProfile.isCustom

        Html.section [
            prop.className "llm-panel"
            prop.testId "table-llm-panel"
            prop.children [
                yield! Seat.all model.Ruleset |> List.map (fun seat -> seatRow live seat dispatch)
                yield! profileEditor live dispatch
                renderingLine model live
                Html.p [ prop.key "note"; prop.className "intro"; prop.text panelNote ]
                // 自定义端点那段话只在库里有那么一份档案时出现：两个坑（CORS、mixed content）
                // 说清楚要一整段，而它们与官方八家无关。完整配法在 `docs/host/custom-endpoint.md`。
                if custom then
                    Html.p [
                        prop.key "custom-note"
                        prop.className "intro"
                        prop.testId "table-llm-custom-note"
                        prop.text
                            "自定义端点：baseUrl 填到含 /v1 那一层（Ollama 是 http://localhost:11434/v1，LM Studio 是 http://localhost:1234/v1），本地端点通常不用填 key，模型名照端点里的实名填。两个坑要先踩平：端点默认不放行浏览器跨域（Ollama 设 OLLAMA_ORIGINS，LM Studio 在设置里开 CORS），且 https 页面调 http 端点会被浏览器拦掉——配法见 docs/host/custom-endpoint.md。接不上时上面那行会直说「连不上自定义端点」，那不是模型不肯选。"
                    ]
            ]
        ]
