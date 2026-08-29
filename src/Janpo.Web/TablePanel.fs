namespace Janpo.Web

open Feliz
open Janpo

/// 配桌与模型面板（票 70 从 `TablePage.fs` 拆出来的第二块）：播放控制、
/// **配桌那三项规则**（对局长度 / 赤宝牌 / 食断，票 72）、视角与种子那一排、
/// 模型坐席与 provider / 模型 / key / 超时 / 思考预算 / 脚手架 / 人格 / 模板那一整屏。
///
/// **这一屏只有两个出口**（票 83 划的那条分界线）：
///
/// - `setup`（「配这一桌」）：对局长度·赤宝牌·食断、种子与重开、四席绑定、模型档案库。
///   开局前用一次，因此收在页面上半。**只属于 Live**：回放没有配桌。
/// - `ops`（「操作这一桌」）：播放 / 暂停 / 单步 / 下一局 / 倍速 / 从头再放 / 时间轴 /
///   跳到局号 / 导出牌谱 / 复制分享链接 / 导入牌谱 / 视角 / 危险度。开局后一直在用，
///   因此**紧贴牌桌上沿**（`TablePage` 把它摆在 `board` 正上方）。
///
/// 判据是「这个控件作用于什么」：改这一桌**怎么开**的归 `setup`，改这一桌**怎么走 / 怎么看**
/// 的归 `ops`。视角与危险度改的是牌桌的呈现，因此在后者；种子改的是下一桌怎么开，因此在前者。
/// **两页共用同一套装配**（首页回放与 `?table=1`），分岔只在「摆哪几个按钮」上。
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

    /// 分享链接那一行：分工说明永远在，点过之后追一句下场（票 78）。
    ///
    /// **「不含推理」要说在明处**（票面原话）：分享链接与「导出牌谱」的分工不写出来，
    /// 拿到链接的人会以为那就是全部。`data-share` / `data-share-chars` 给无头闸门读，
    /// 人读的是那句中文——两头对不上就是错。
    let private shareLine (live: LiveTable) =
        let chars =
            match live.Shared with
            | Some(ShareOutcome.Copied chars)
            | Some(ShareOutcome.Oversized chars) -> string chars
            | Some(ShareOutcome.Failed _)
            | None -> ""

        let said =
            live.Shared
            |> Option.map (fun outcome -> "　" + ShareOutcome.toDisplay outcome)
            |> Option.defaultValue ""

        Html.p [
            prop.key "share-note"
            prop.className "intro"
            prop.testId "table-share-note"
            prop.custom ("data-share", live.Shared |> Option.map ShareOutcome.toWire |> Option.defaultValue "")
            prop.custom ("data-share-chars", chars)
            prop.text (
                "「复制分享链接」出的地址只带棋谱——模型的推理与 prompt 不上 URL；要给人完整推理，用「导出牌谱」的 JSON 文件（对方从首页的「导入牌谱 JSON」能看）。"
                + said
            )
        ]

    /// 主持人那一页的控制条（`?table=1`）：单步 / 下一局 / 导出牌谱 / 复制分享链接
    /// 都只属于 Live。
    let private hostControls (model: TableModel) (live: LiveTable) (dispatch: TableMsg -> unit) =
        let running = TableState.canAdvance model

        let ended =
            match live.Table with
            | Ok table -> Table.isKyokuEnded table && (Table.result table |> Option.isNone)
            | Error _ -> false

        Html.div [
            prop.children [
                Html.div [
                    prop.key "host-row"
                    prop.className "controls"
                    prop.children (
                        [
                            playButton model dispatch
                            button "table-step" (not running) "单步" Advanced dispatch
                            button "table-next" (not ended) "下一局" KyokuAdvanced dispatch
                            // 牌谱随时导得出来，不必等终局：打到一半的事件流同样 fold 得回去。
                            button "table-export" (Result.isError live.Table) "导出牌谱" Exported dispatch
                            // 导出与分享是一对（票 78）：一个带全量，一个只带棋谱进 hash。
                            button "table-share" (Result.isError live.Table) "复制分享链接" Shared dispatch
                            Html.span [ prop.key "speed-label"; prop.className "label"; prop.text "倍速" ]
                        ]
                        @ speeds model dispatch
                    )
                ]
                shareLine live
            ]
        ]

    /// 时间轴上那几枚**复盘标记**（票 105）：值得看的那几手各落在轴上哪一点。
    ///
    /// **它不判任何事**：标哪几枚由 `Review.focused` 说了算（与复盘那一列同一次调用），
    /// 这里只把帧号换成百分比。**坐标系与滑块完全同一根**：分母就是滑块的上界
    /// （`timeline.Last`），因此一枚标记的位置就是拖到那一帧时滑块停的位置。
    ///
    /// **不是按钮**：一整场二三十枚标记挤在一根轴上，每枚实宽不到两个像素
    /// ——做成点得动的东西就是在邀人点不中（票 87 记过同一族：点不中的按钮比没有更坏）。
    /// 要跳到那一手的路子只有一条：复盘那一列里点它（票 76 的 `RecordOpened`）。
    /// 因此它们对读屏整个隐起来（`aria-hidden`）：同一件事已经在复盘那一列里说过一遍。
    let private reviewMarks (timeline: Timeline) (marks: ReviewMark list) =
        // 末帧是 0 只有一种情形（一份只有一帧的牌谱），那时滑块本身也拖不动；
        // 除以 0 在 JS 里会变成 Infinity，而一个 `left: Infinity%` 是静静地画错。
        let span = max 1 timeline.Last

        Html.div [
            prop.key "review-marks"
            prop.className "timeline-marks"
            prop.testId "table-timeline-marks"
            prop.ariaHidden true
            prop.custom ("data-marks", List.length marks)
            prop.children (
                marks
                |> List.map (fun mark ->
                    Html.span [
                        prop.key mark.Turn
                        prop.className "timeline-mark"
                        prop.custom ("data-timeline-mark", mark.Turn)
                        prop.custom ("data-timeline-mark-frame", mark.Frame)
                        prop.style [ style.left (length.percent (100.0 * float mark.Frame / float span)) ]
                    ])
            )
        ]

    /// 时间轴那一根滑块（票 75）。**滑块的 `value` 就是游标**：拖到哪一帧牌桌就是那一帧
    /// （O(1) 取帧，帧在载入时一次 fold 好）。旁边那句话说的是「第几手 / 第几局」。
    ///
    /// `prop.onChange` 收 `int` 那一条重载读的是 `valueAsNumber`，正是 range 输入框该用的那一个。
    /// `data-*` 给无头闸门读：人读的是那句中文，机器读的是它们，两边对不上就是错。
    let private timelineRow (timeline: Timeline) (marks: ReviewMark list) (dispatch: TableMsg -> unit) =
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
                Html.div [
                    prop.key "slider"
                    prop.className "timeline-track"
                    prop.children [
                        Html.input [
                            prop.key "input"
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
                        reviewMarks timeline marks
                    ]
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
    /// 它与上面那几枚复盘标记（票 105）是两件事：这一排是**牌谱自带的结构**（几局），
    /// 那几枚是**这一席值得看的那几手**；前者点得动，后者只是招子。
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
    let private replayControls (model: TableModel) (marks: ReviewMark list) (dispatch: TableMsg -> unit) =
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
            | Some timeline -> [ timelineRow timeline marks dispatch; kyokuRow timeline dispatch ]

        // 导入牌谱 JSON（票 78）：牌谱从外面进来的第二条路。**挂在回放这一页**——
        // 导入的下场就是一份回放，而 `table-no-bubbles` 那句话指的就是这里。
        // **Demo 拉不动、分享链接读不动时这一排也在**（rails 空着它不空）：
        // 那正是人最需要换一份牌谱的时刻。
        let importRow =
            Html.div [
                prop.key "import"
                prop.className "controls"
                prop.children (
                    Html.label [
                        prop.key "import-field"
                        prop.className "field"
                        prop.children [
                            Html.span [ prop.className "label"; prop.text "导入牌谱 JSON" ]
                            Html.input [
                                prop.testId "table-import"
                                prop.type' "file"
                                prop.accept "application/json,.json"
                                // 挑中就开读；读文件是异步的，结果由 `ImportLoaded` 带回。
                                prop.onChange (ImportPicked >> dispatch)
                            ]
                        ]
                    ]
                    :: (model.ImportFault
                        |> Option.toList
                        |> List.map (fun reason ->
                            Html.p [
                                prop.key "import-fault"
                                prop.className "error"
                                prop.testId "table-import-fault"
                                prop.text reason
                            ]))
                )
            ]

        Html.div [
            prop.className "replay-controls"
            prop.children ((playRow :: rails) @ [ importRow ])
        ]

    /// 控制条。**牌从哪来决定摆哪几个按钮**（票 71），而播放本身两边共用一份实现。
    ///
    /// **复盘那几枚标记只给回放**（票 105）：Live 那一页根本没有时间轴
    /// （`TableState.timeline` 在那边恒是 None，票 75 定的），没有轴就没有地方标。
    let private controls (model: TableModel) (marks: ReviewMark list) (dispatch: TableMsg -> unit) =
        match TableState.live model with
        | Some live -> hostControls model live dispatch
        | None -> replayControls model marks dispatch

    /// 配桌那一排（票 72）：**对局长度 / 赤宝牌 / 食断**，加上**种子与重开**（票 83 收过来的）。
    ///
    /// **只属于 Live**：回放那一侧的规则集是牌谱自带的那一份（ADR-0004），拨不动；
    /// 种子同理（回放的牌是录下来的）。
    ///
    /// **四样拨完都要按那一枚「重开」**，因此票 83 把它们并成一排（票 72 报告里记的
    /// 那条 nitpick：它们语义上本来就是一组）。半场换规则会让同一份牌谱前后按两套规则算，
    /// 回放就重现不了。因此这一排末尾那一格把两件事都印出来：
    /// **这一桌真在按的那一份**（`data-rules`，与导出牌谱里的 `ruleset` 逐项对得上），
    /// 以及拨到的那三项是不是已经与它不同了（`data-rules-pending`，就是 `TableState.rulesPending`）。
    let private rulesRow (model: TableModel) (live: LiveTable) (dispatch: TableMsg -> unit) =
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

        // 种子与重开（票 83 从视角那一排收过来）：它们与上面三项一样是「下一桌怎么开」，
        // 而视角改的是「这一桌怎么看」。「重开」就在同一排上，拨完不用再找它。
        let seeding = [
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
            prop.className "controls rules-row"
            prop.children (
                (label "length-label" "对局长度" :: lengths)
                @ (label "akadora-label" "赤宝牌"
                   :: axis "akadora" live.Rules.Akadora RuleChoice.Akadora)
                @ (label "kuitan-label" "食断" :: axis "kuitan" live.Rules.Kuitan RuleChoice.Kuitan)
                @ seeding
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

    /// 视角那一排。**两种来源共用**（回放里视角照旧切得动）。
    ///
    /// **它属于「操作这一桌」**（票 83）：视角与危险度改的是牌桌的呈现，拨一下当场就要看见
    /// 牌桌变样，因此跟着牌桌走；种子与「重开」改的是下一桌怎么开，已经挤到 `rulesRow` 上去了。
    /// **真人在座时它只剩一枚**（票 87）：上帝视角与别席视角的按钮**不在 DOM 里**，
    /// 而不是灰掉——票 81 把视角定成了一道信息闸门，而 `disabled` 只是 UI 层的礼貌
    /// （一行 DevTools 就平了）。真正拦住它的是 `TableState.viewpoint`：那边连值都给不出来。
    ///
    /// **自家那一枚留着**：一排按钮整排消失会让人以为页面坏了，而它同时是「你在这儿」的标记。
    /// **终局后它们都回来**（判据与气泡同一条：`TableState.lockedSeat`）：复盘本来就该看得见四家。
    let private viewpoints (model: TableModel) (dispatch: TableMsg -> unit) =
        let locked = TableState.lockedSeat model
        let viewpoint = TableState.viewpoint model

        let offered =
            match locked with
            | Some seat -> [ seat ]
            | None -> Seat.all model.Ruleset

        let seats =
            offered
            |> List.map (fun seat ->
                picker
                    $"table-view-{Seat.index seat}"
                    (viewpoint = Viewpoint.Seated seat)
                    $"座位 {Seat.index seat}"
                    (ViewpointPicked(Viewpoint.Seated seat))
                    dispatch)

        let god = [
            if Option.isNone locked then
                picker "table-view-god" (viewpoint = Viewpoint.God) "上帝视角" (ViewpointPicked Viewpoint.God) dispatch
        ]

        // 锁着的时候要说一句为什么（票 87）：“那几枚按钮本来就没有”与“页面坏了”
        // 在屏幕上长得一模一样（同票 81 那句「另 N 席被这个视角挡着」的理由）。
        let note = [
            match locked with
            | None -> ()
            | Some seat ->
                Html.span [
                    prop.key "view-locked"
                    prop.className "rendering pending"
                    prop.testId "table-view-locked"
                    prop.custom ("data-view-locked", Seat.index seat)
                    prop.text $"视角锁在座位 {Seat.index seat}（你自己）：桌边坐着真人，上帝视角与别席视角在这一页上不存在——终局后它们回来。"
                ]
        ]

        Html.div [
            prop.className "controls"
            prop.children (
                [ Html.span [ prop.key "view-label"; prop.className "label"; prop.text "视角" ] ]
                @ seats
                @ god
                // 危险度（票 25）：围观者想看就拨开，**默认关**。
                // 它只摆得出**观测者自家**那一手（`TableBoard.dangerSeats`），因此锁着也不泄。
                //
                // **裸奔档的真人坐在桌边时这一枚根本不在 DOM 里**（票 89）：危险度是
                // 「要算才有的量」（术语表那条「感知 vs 计算」），灰掉不算数——一行 DevTools
                // 就把 disabled 平了（票 81 对视角说过同一句话）。判据只有 `TableState.assists`
                // 一条，牌桌那一块（`TableBoard.dangerPanels`）读的是同一个。
                @ [
                    if TableState.assists model then
                        picker "table-danger" model.ShowDanger "危险度" DangerToggled dispatch
                ]
                @ note
            )
        ]

    /// 「操作这一桌」那一整块（票 83）：控制条 + 视角那一排。
    ///
    /// **它紧贴牌桌上沿**，不做视口吸底：吸底会盖住牌桌下沿，而那正是自家手牌那一排
    /// ——「按一下、看结果」看的就是它。**两屏共用这一个出口**（首页回放与 `?table=1`），
    /// 分岔在它里面（`controls`）而不在页面装配上。
    let internal ops (model: TableModel) (marks: ReviewMark list) (dispatch: TableMsg -> unit) =
        Html.div [
            prop.className "ops"
            prop.testId "table-ops"
            prop.children [ controls model marks dispatch; viewpoints model dispatch ]
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
    ///
    /// **人格与模板那两块大文本收进一个 `details`**（票 83）：一席一行才看得完四席。
    ///
    /// - **收起时看得出有没有内容**：摘要上写着哪一格填了（人读那几个字，闸门读 `data-seat-custom`）。
    ///   只画一枚小圆点不行——看得出「有东西」却看不出「哪一格有」。
    /// - **展开一席不把另外三席顶出屏外**：敲开之后它独占本行的下一行（CSS 里的
    ///   `.seat-detail[open]`），只长高一排文本框的高度，不把四席拆成两屏。
    ///   闸门量的就是这一条（`verify-seats`：展开座位 0 之后四行仍在同一屏里）。
    let private seatRow (names: string list) (live: LiveTable) (seat: Seat) (dispatch: TableMsg -> unit) =
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

        // 「我自己」（票 87）：**第三种选手，与另外两种并排**——引擎与编排层不区分它与 AI
        // （spec 的 story 28），页面上就不该把它摆成另一个开关。
        // 拨上它的那一下同时把原来那一席腾空（`SeatingPlan.bind`：本地只坐得下一席）。
        let human =
            picker
                $"table-seat-{index}-human"
                (binding.Choice = SeatChoice.Human)
                SeatingPlan.humanToDisplay
                (SeatBound(seat, SeatChoice.Human))
                dispatch

        // 「强 AI 基线」（票 92；ADR-0006）：**第四种选手，与另外三种并排**——
        // 四席怎么混都行（三模型 + 一强 AI、真人 + 强 AI……）。
        // **拨上它的那一下就是去拉那几 MB**（`TableState.started`）：
        // 首页与不选它的对局一个字节都不拉（边界 1）。
        let baseline =
            picker
                $"table-seat-{index}-baseline"
                (binding.Choice = SeatChoice.Baseline)
                SeatingPlan.baselineToDisplay
                (SeatBound(seat, SeatChoice.Baseline))
                dispatch

        let profiles =
            live.Seating.Profiles
            |> List.mapi (fun each profile ->
                picker
                    $"table-seat-{index}-profile-{each}"
                    (binding.Choice = SeatChoice.Profile profile.Name)
                    profile.Name
                    (SeatBound(seat, SeatChoice.Profile profile.Name))
                    dispatch)

        // 这一席自己填过的那两格（收起来之后就靠这两个字认）。**人读的与闸门读的同一个判据**：
        // 两份各自取一遍的话，改其中一份就会让图上写着一回事、`data-*` 写着另一回事。
        let hasPersona = SeatBinding.field SeatField.Persona binding <> ""
        let hasTemplate = SeatBinding.field SeatField.Template binding <> ""

        let filled = [
            if hasPersona then
                "人格"
            if hasTemplate then
                "模板"
        ]

        let mark =
            match filled with
            | [] -> "人格·模板〇默认"
            | some -> "人格·模板●" + String.concat "·" some

        // `data-seat-custom` 给闸门读：空串 / `persona` / `template` / `persona,template`。
        let wire = [
            if hasPersona then
                "persona"
            if hasTemplate then
                "template"
        ]

        let custom =
            Html.details [
                prop.key "seat-detail"
                prop.className "seat-detail"
                prop.children [
                    Html.summary [
                        prop.testId $"table-seat-{index}-detail"
                        prop.className (
                            if List.isEmpty filled then
                                "seat-mark"
                            else
                                "seat-mark filled"
                        )
                        prop.custom ("data-seat-custom", String.concat "," wire)
                        prop.text mark
                    ]
                    Html.div [
                        prop.className "controls seat-fields"
                        prop.children [
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
                    ]
                ]
            ]

        Html.div [
            prop.key $"seat-{index}"
            prop.className "controls seat-row"
            prop.testId $"table-seat-{index}"
            // 这一席拨到了哪儿（给闸门看；人看的是哪一枚按钮亮着）。
            prop.custom ("data-seat-choice", SeatChoice.toWire binding.Choice)
            // 这一席在牌谱里叫什么（`Roster.names` 那一份）：**档案的名字不在里面**。
            prop.custom ("data-seat-name", names |> List.tryItem index |> Option.defaultValue "")
            prop.children (
                (Html.span [ prop.key "seat-label"; prop.className "label"; prop.text $"座位 {index}" ]
                 :: bots)
                @ [ human; baseline ]
                @ profiles
                @ [
                    // 脚手架档位：**它是实验变量**，主持人在座位上现拨，不用改代码。
                    // **三档都选得到**（票 89 放开了工具搜索档）：那一档在票 94 做完了
                    // （`what_if` 工具、上限与账单都落地），从前那句 `tier <> ToolSearch` 是
                    // 「M3 还没做」的临时状态，而 M3 已经到了。真人席选到它时按信息辅助处理
                    // （`HumanScaffold.shows`，页面上说得出这件事）——这一票不给真人做查询面板。
                    selectField
                        $"table-seat-{index}-tier"
                        "脚手架"
                        (SeatBinding.field SeatField.Tier binding)
                        (ScaffoldTier.all
                         |> List.map (fun tier -> ScaffoldTier.toWire tier, ScaffoldTier.toDisplay tier, true))
                        (fun value -> SeatEdited(seat, SeatField.Tier, value))
                        dispatch
                    custom
                ]
                // **思考时限只画在真人那一行**（票 89 的 story 32）：模型席「想多久」是那份档案的
                // `TimeoutMs`（一次跨网请求的上限），两者量的不是一件事，摆两格只会让人以为
                // 拨哪一格都行。**默认不限时**，因此空着的时候它就该写着 0。
                @ [
                    match binding.Choice with
                    | SeatChoice.Human ->
                        textField
                            $"table-seat-{index}-clock"
                            "number"
                            "思考时限（秒，0＝不限时）"
                            (SeatBinding.field SeatField.Clock binding)
                            (fun value -> SeatEdited(seat, SeatField.Clock, value))
                            dispatch
                    | SeatChoice.Bot _
                    | SeatChoice.Baseline
                    | SeatChoice.Profile _ -> ()
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
                Html.span [ prop.key "profiles-label"; prop.className "label"; prop.text "库里的" ]
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

    /// **拨到强 AI 基线那一席时多出来的那一句**（票 102；主人的要求：
    /// 「在网页和 README 都说明这个强 AI 基线是什么、来自哪里」）。
    ///
    /// **署名要落在人遇到它的那一刻**：页脚那条链接是分发件的法律义务（Apache-2.0 §4(d)），
    /// 但它替不了「用户看得懂这是什么」——人就是在这一行上把一席拨给了它的。
    ///
    /// **拨上了才画**（与 `BaselineLine` 同一条判据：`SeatingPlan.baselineSeats` 空不空）：
    /// 四家模型那一桌不需要读它。**这不与页脚那一条重复也不代替它**：页脚那一条
    /// 不挂在任何条件后面（`Footer.fs` 里那条判断），这一句只在人拨到它时出现。
    ///
    /// **四席拨了几席就只出一句**：它说的是「那个选手是什么」，与坐几席无关，
    /// 逐席各印一遭只是噪声（坐哪几席写在牌桌上那一行里）。
    let private baselineCredit (live: LiveTable) =
        match SeatingPlan.baselineSeats live.Seating with
        | [] -> []
        | _ -> [
            Html.p [
                prop.key "baseline-credit"
                prop.className "intro"
                prop.testId "table-baseline-credit"
                prop.children [
                    Html.span Credit.baselineIntroHead
                    // **另开一个标签页**（同 `Footer.fs` 里那条理由）：正在配的这一桌只活在当前页面的内存里，
                    // 在原地跳走等于把人拨了一半的桌子扔掉。
                    Html.a [
                        prop.href (Credit.thirdPartyUrl ())
                        prop.target "_blank"
                        prop.rel "noopener noreferrer"
                        prop.text Credit.thirdPartyText
                    ]
                    Html.span Credit.baselineIntroTail
                ]
            ]
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
    /// **两块分开**（票 83）：上面那块是**谁坐哪、给多少信息**（坐位 → 选手 → 档位），
    /// 下面那块是**怎么问模型**（provider·模型·key·超时·思考）。它们是两种东西：
    /// 一份档案坐三席时下面那块只填一遍，上面那块要拨三次——混在一片读不出这件事。
    ///
    /// **key 只进 localStorage**（`Store`），不外发到本平台——本平台根本没有后端。
    /// **不提供订阅制登录**：pi-ai 的 OAuth 流程是 Node-only（票 18），浏览器里只有 API key
    /// 这一条路；Bedrock 同理不在 provider 列表里。
    let private llmPanel (model: TableModel) (live: LiveTable) (dispatch: TableMsg -> unit) =
        let custom = live.Seating.Profiles |> List.exists ModelProfile.isCustom

        let heading (key: string) (text: string) =
            Html.h2 [ prop.key key; prop.className "block-title"; prop.text text ]

        Html.section [
            prop.className "llm-panel"
            prop.testId "table-llm-panel"
            prop.children [
                Html.div [
                    prop.key "seating"
                    prop.className "panel-block"
                    prop.testId "table-seating"
                    prop.children [
                        heading "seating-title" "谁坐哪一席：选手·档位·（人格·模板）"
                        let names = TableState.seatNames model

                        yield!
                            Seat.all model.Ruleset
                            |> List.map (fun seat -> seatRow names live seat dispatch)

                        yield! baselineCredit live
                    ]
                ]
                Html.div [
                    prop.key "profiles-block"
                    prop.className "panel-block"
                    prop.testId "table-profiles"
                    prop.children [
                        heading "profiles-title" "模型档案库：怎么问这个模型（key 只在这里填）"
                        yield! profileEditor live dispatch
                        renderingLine model live
                    ]
                ]
                // 那一大段说明收起来（票 83）：**它是查阅用的**，不是每次都要读的。
                // 一字未删，只是不再占着视线中心；摘要上写清楚里面是什么，人才知道该不该点开。
                Html.details [
                    prop.key "note"
                    prop.className "panel-note"
                    prop.children [
                        Html.summary [
                            prop.testId "table-panel-note"
                            prop.text "这几格都是什么意思？key 存在哪釿？模型不说话怎么办？（点开看）"
                        ]
                        Html.p [ prop.className "intro"; prop.text panelNote ]
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
            ]
        ]

    /// 「配这一桌」那一整块（票 83）：规则三项 + 种子与重开一排，四席绑定与模型档案库一块。
    ///
    /// **只属于 Live**（回放没有配桌，`TableState.rosterOf` 就是这么说的）。
    ///
    /// **默认收起（票 116）**。票 83 把它收到页面上半、并量出了病：
    /// 收起前 `?table=1` 打开那一刻牌桌顶边在 810 px、视口 800
    /// ⇒ **一像素牌桌都看不见**，而 810 里 528 px（65%）是这一块。
    /// 它**开局前用一次**，不该占着整个第一屏。
    ///
    /// 折叠用的是票 83 给 `panelNote` 立的**同一副写法**（`Html.details`）：
    /// **不进 model、不添消息、不碰 localStorage**。代价是折叠状态不持久
    /// （刷新回到收起）——同票 83 §9 待审 ② 那笔账，仍不还。
    ///
    /// **摘要行写的是那四项的值**，不是「配桌」两个字：只画一枚点会让人
    /// 看得出「有东西」却看不出「是什么」（同票 83 §2.1 那条判据的形状）。
    /// 摘要里摆的是**拨到的那一份**（与里面那三排按钮一致），
    /// 因此得把 `rulesPending` 也带出来：拨好了还没按「重开」时，
    /// 摘要上那几个值并不是这一桌真在按的（同 `table-rules` 那一格的判据）。
    let internal setup (model: TableModel) (live: LiveTable) (dispatch: TableMsg -> unit) =
        let pending = TableState.rulesPending model

        let digest =
            let seed = if live.SeedText = "" then "随机" else live.SeedText

            String.concat "・" [
                GameLength.toDisplay live.Rules.Length
                $"赤宝牌{RulesetDraft.switchToDisplay live.Rules.Akadora}"
                $"食断{RulesetDraft.switchToDisplay live.Rules.Kuitan}"
                $"种子 {seed}"
            ]

        Html.details [
            prop.className "setup"
            prop.testId "table-setup"
            prop.children [
                Html.summary [
                    prop.testId "table-setup-summary"
                    prop.children [
                        Html.span [ prop.key "label"; prop.className "label"; prop.text "配桌" ]
                        Html.span [
                            prop.key "digest"
                            prop.className "setup-digest"
                            prop.testId "table-setup-digest"
                            prop.custom ("data-rules-pending", (if pending then "true" else "false"))
                            prop.text (if pending then $"{digest}（拨好了，未重开）" else digest)
                        ]
                    ]
                ]
                rulesRow model live dispatch
                llmPanel model live dispatch
            ]
        ]
