namespace Janpo.Web

open Feliz
open Janpo

/// 真人坐席那一行与他那一排按钮（票 87 立行、票 88 立按钮）：
/// **轮不轮到你、你此刻能做什么、你自己放掉过什么**。
///
/// 与 `AgentLine` 同一个形状：这里只把 `TableState` 算好的那几样画出来，
/// 判据（轮到谁、能点哪几张、能吃哪几种）在 `TableState.humanTurn` 与 `HumanSeat` 里
/// ——而那两处又只是**现问那一份决策包**。**这一层一条日麻规则都不判**：
/// 吃的左中右、碰要不要亮赤 5、立直宣言之后还能打哪几张，在这里全是「包里的第 N 条」。
///
/// **没有真人坐席时它一行都不画**：一句「这一桌没有真人」对四家模型那一桌是噪声。
[<RequireQualifiedAccess>]
module HumanLine =

    /// 你自己按掉的那几次「过」与时限到点替你打的那几手（票 88 开账、票 89 拓宽）。
    /// **最近一次逐条列出来 + 这一桌总共几次**：只报个数说不清放掉的是碰还是荣和，
    /// 只报最近一次又看不出这件事一直在发生。
    ///
    /// **两种各报各的数**（票 89）：「你按了几次」与「时限替你打了几手」是两件事，
    /// 揉成一个数的话，人看到的就只是「我好像过了很多次」。
    let private passed (passes: HumanPass list) : string =
        match passes with
        | [] -> ""
        | latest :: _ ->
            let count (which: HumanPass -> bool) (word: string) : string list =
                match passes |> List.filter which |> List.length with
                | 0 -> []
                | times -> [ $"{word} {times} 次" ]

            let tally =
                count HumanPass.pressed "你按「过」" @ count (HumanPass.pressed >> not) "时限代打"
                |> String.concat "、"

            $"　{HumanPass.toDisplay latest}（这一桌{tally}）。"

    /// 这一刻他能做的那三态各是一句话，附带给机器看的那个状态名。
    ///
    /// **五态**（`data-human`）：`respond` 他家打了牌等他要不要鸣 / `reach` 立直宣言了正在选宣言牌 /
    /// `waiting` 该他出牌 / `watching` 轮到别人 / `settled` 这一场打完了。
    /// **终局那一屏不能再说「轮到别人」**（那时谁的回合都不是），而且那一刻正是视角与气泡
    /// 一起松开的时候——这一句要把它说出来，否则人不知道刚才藏着的那几样现在看得了。
    /// 判据直接读 `lockedSeat`（它就是 `unlocked` 的反面），不在这一层再判一遍。
    /// 倒计时那半句话（票 89 的 story 32）。**不限时那句话一字未改**（票 87/88 的默认）：
    /// 不设时限那条路上一个行为都不变是这一票的硬判据。
    ///
    /// **到点会发生什么也写出来**：人要能预知平台会替他做什么，
    /// 而不是等牌自己飞出去了才回头猜。
    let private ticking (clock: HumanClock option) (responding: bool) : string =
        match clock with
        | None -> "不限时，整桌等着你。"
        | Some clock ->
            let deadline = if responding then "自动过" else "自动摸切"
            $"还剩 {HumanClock.remaining clock} 秒（共 {clock.Limit} 秒，到点{deadline}）。"

    let private said (model: TableModel) (seat: Seat) (turn: DecisionPackage option) : string * string =
        let index = Seat.index seat
        let clock = TableState.humanClock model

        match turn, TableState.lockedSeat model with
        | Some package, _ ->
            let playable = HumanSeat.dahaiOptions package |> List.length
            let calls = HumanSeat.buttons package |> List.length

            match HumanSeat.pass package with
            // 有「过」= 他家打出了一张，正在等他要不要鸣 / 荣和（`Action.None` 那段注释）。
            | Some _ ->
                "respond",
                $"他家打出了一张，等你（座位 {index}）：牌桌下面那一排就是你此刻做得了的（{calls} 条），不要就按「过」。"
                + ticking clock true
            | None when HumanSeat.declaringRiichi package ->
                // 立直是两段（`Action.Riichi` 那段注释）：宣言之后这一手还要选宣言牌，
                // 而**能打哪几张仍旧只由合法动作集说了算**——引擎那一集里只剩「打完仍听牌」的。
                "reach",
                $"立直宣言了（座位 {index}）：现在选宣言牌——点得动的那 {playable} 张是引擎给的那一集（打完仍听牌的才在里面）。"
                + ticking clock false
            | None ->
                let also =
                    match calls with
                    | 0 -> ""
                    | count -> $"　这一手还能宣言 {count} 条，按钮在牌桌下面。"

                "waiting",
                $"轮到你出牌了（座位 {index}）：点自己手里的一张就打出去，能点的那几张由引擎给的合法动作集定（此刻 {playable} 条）。"
                + ticking clock false
                + also
        | None, Some _ -> "watching", $"你坐在座位 {index}：轮到别人，看着就好。"
        | None, None -> "settled", $"这一场打完了（你坐的是座位 {index}）：视角与思考气泡都解锁了，四家的牌与推理现在都看得了。"

    /// 真人坐席那一行；这一桌没有真人时是空表。
    ///
    /// `data-*` 给无头闸门读，人读的是那句中文——两头对不上就是错：
    /// `data-human-seat`（坐哪一席）、`data-human`（上面那五态）、
    /// `data-human-playable`（此刻点得出去几张牌）、`data-human-calls`（牌桌下面那一排几枚，不含「过」）、
    /// `data-human-options`（引擎这一手一共给了几条）、`data-human-passes`（他自己按过几次「过」）。
    ///
    /// 票 89 又挂上三个：`data-human-tier`（这一席拨到哪一档）、
    /// `data-human-clock`（这一手还剩几秒，**不限时或不轮到他时是空串**）、
    /// `data-human-expired`（时限到点替他打了几手）。
    /// **`data-human-passes` 的意思一字未改**（票 88 定的：他自己按下去几次）：
    /// 超时那几手另占一个数，两者相加才是那本账的长度。
    ///
    /// **`data-human-options` 是「按钮与合法动作集一一对应」那道闸门的锚**（票 88 的要害）：
    /// 页面上点得到的那些 id 合起来必须正好是 `0 … options-1`——多一枚是凭空造的，
    /// 少一枚是引擎给了他却点不到。
    let internal at (model: TableModel) : ReactElement list =
        match TableState.humanSeat model with
        | None -> []
        | Some seat ->
            let turn = TableState.humanTurn model
            let passes = TableState.passes model

            let counted (count: DecisionPackage -> int) : int =
                turn |> Option.map count |> Option.defaultValue 0

            let state, sentence = said model seat turn

            let pressed = passes |> List.filter HumanPass.pressed |> List.length

            [
                Html.p [
                    prop.key "human"
                    prop.className "agent human-line"
                    prop.testId "table-human"
                    prop.custom ("data-human", state)
                    prop.custom ("data-human-seat", Seat.index seat)
                    prop.custom ("data-human-playable", counted (HumanSeat.dahaiOptions >> List.length))
                    prop.custom ("data-human-calls", counted (HumanSeat.buttons >> List.length))
                    prop.custom ("data-human-options", counted (DecisionPackage.options >> List.length))
                    prop.custom ("data-human-passes", pressed)
                    prop.custom ("data-human-expired", List.length passes - pressed)
                    prop.custom (
                        "data-human-tier",
                        TableState.humanTier model
                        |> Option.map ScaffoldTier.toWire
                        |> Option.defaultValue ""
                    )
                    prop.custom (
                        "data-human-clock",
                        TableState.humanClock model
                        |> Option.map (HumanClock.remaining >> string)
                        |> Option.defaultValue ""
                    )
                    prop.text (sentence + passed passes)
                ]
            ]

    /// **新手辅助轮那一块**（票 89 的 story 33）：向听、有效牌与危险度，逐条对应他点得动的那几张。
    ///
    /// **裸奔档它一行都不画**，而且那一条判据不在这里：`TableState.humanScaffold` 是
    /// 辅助渲染的**唯一入口**（它同时管着牌桌上那块危险度）。这一层只负责把它给的数画出来
    /// ——**一个数都不算**（同 `HumanSeat` 一条规则都不判）。
    ///
    /// **每一行带着那一条的包内 id**（`data-scaffold-id`）：与手牌上那张牌的 `data-dahai-id`
    /// 是同一个号，人读完这一行就知道该点哪一张，闸门也照它把两处对起来。
    let internal assist (model: TableModel) : ReactElement list =
        match TableState.humanScaffold model, TableState.humanTurn model with
        | Some scaffold, Some package ->
            let lines = HumanScaffold.lines package
            let threats = HumanScaffold.threats scaffold

            let heading =
                match threats with
                | "" -> $"信息辅助（引擎算的事实，不是建议）：{HumanScaffold.summary scaffold}"
                | who -> $"信息辅助（引擎算的事实，不是建议）：{HumanScaffold.summary scaffold}。有威胁的家：{who}"

            [
                Html.section [
                    prop.key "human-scaffold"
                    prop.className "settlement human-scaffold"
                    prop.testId "table-human-scaffold"
                    // 这一块一共摆了几行（闸门拿它对「能点的那几张」），以及现在几向听。
                    prop.custom ("data-scaffold-lines", List.length lines)
                    prop.custom ("data-scaffold-shanten", Shanten.value scaffold.Shanten)
                    prop.children [
                        Html.h3 heading
                        Html.p [
                            prop.key "note"
                            prop.className "intro"
                            prop.text "这几个数与同桌模型拿到的是同一份（同一次引擎计算）。拨回「裸奔」就一个都不给，那才是一个坐在牌桌前的人本来看得见的。"
                        ]
                        Html.div [
                            prop.key "lines"
                            prop.children [
                                for line in lines ->
                                    Html.p [
                                        prop.key line.Id
                                        prop.testId $"human-scaffold-{line.Id}"
                                        prop.custom ("data-scaffold-id", line.Id)
                                        prop.custom ("data-scaffold-shanten", Shanten.value line.Trial.Shanten)
                                        prop.custom ("data-scaffold-delta", line.Trial.ShantenDelta)
                                        prop.custom (
                                            "data-scaffold-ukeire",
                                            line.Trial.Ukeire
                                            |> Option.map (Ukeire.total >> string)
                                            |> Option.defaultValue ""
                                        )
                                        prop.custom (
                                            "data-scaffold-kinds",
                                            line.Trial.Ukeire
                                            |> Option.map (Ukeire.kindCount >> string)
                                            |> Option.defaultValue ""
                                        )
                                        prop.custom (
                                            "data-scaffold-danger",
                                            line.Trial.Danger
                                            |> Option.map (fun danger -> string danger.Rank)
                                            |> Option.defaultValue ""
                                        )
                                        prop.text (HumanScaffold.toDisplay line)
                                    ]
                            ]
                        ]
                    ]
                ]
            ]
        // 裸奔档 / 不轮到他 / 响应阶段（没牌可打）：**一行都不画**。
        | _, _ -> []

    /// 一枚按钮。**`button` 而不是加了 onClick 的 `span`**（与手牌那几张同一个理由，票 87）：
    /// 键盘走得到、读屏念得出、`:focus-visible` 那圈靛青自然就有。
    ///
    /// `data-human-action` 是 mjai 的动作名（给机器看的），中文 label 是引擎给的（给人看的）；
    /// **点它就是提交这一条 id**，页面构造不出一个动作。
    let private button (dispatch: TableMsg -> unit) (extra: string) (each: ActionButton) =
        Html.button [
            prop.key each.Id
            prop.className $"call{extra}"
            prop.testId $"human-action-{each.Id}"
            prop.custom ("data-human-action-id", each.Id)
            prop.custom ("data-human-action", each.Kind)
            prop.onClick (fun _ -> dispatch (HumanPlayed each.Id))
            prop.text each.Label
        ]

    /// 牌桌**下面**那一排按钮（票 88）：吃 / 碰 / 杠 / 立直 / 和了 / 九种九牌，以及「过」。
    ///
    /// **摆在牌桌下面**：自家那一排手牌就在牌桌下沿，鸣不鸣的那一下与点哪张牌是同一件事，
    /// 视线不该在一屏之内来回甩（票 83 那条「按一下就能看见结果」同一个标准）。
    ///
    /// **每一枚背后都是一条引擎给的 id**（票面的要害判据）：这一层不算组合、不判听牌，
    /// 「吃有左中右三种」在这里只是包里的三条各自带 id 的动作。
    ///
    /// **「过」单独拎到最后**：它是「什么都不做」的那一条，混在吃碰杠中间容易误点；
    /// 而它同样只是包里的一条 id（`HumanSeat.pass`），不是页面自己加的一枚按钮。
    /// **响应阶段它永远在**——不点就卡住是最难受的死法。
    let internal calls (model: TableModel) (dispatch: TableMsg -> unit) : ReactElement list =
        match TableState.humanTurn model with
        | None -> []
        | Some package ->
            let offered = HumanSeat.buttons package
            let pass = HumanSeat.pass package

            match offered, pass with
            // 该他出牌、又一条宣言都没有（最常见的一手）：不画空条。
            | [], None -> []
            | offered, pass -> [
                Html.div [
                    prop.key "human-calls"
                    prop.className "controls human-calls"
                    prop.testId "table-human-calls"
                    prop.custom ("data-human-calls", List.length offered)
                    prop.children (
                        (offered |> List.map (button dispatch ""))
                        @ (pass |> Option.toList |> List.map (button dispatch " pass"))
                    )
                ]
              ]
