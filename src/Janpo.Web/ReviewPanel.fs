namespace Janpo.Web

open Feliz
open Janpo

/// 复盘那一块（票 90，spec 的 story 34）：**终局之后，逐手对照标注**。
///
/// 与 `HumanLine` / `AgentLine` 同一个形状——**这里只画，一条判据都不判**：
/// 给不给看、对着哪一席、每一条上那几个数，全在 `Review` 里，而 `Review` 又只是
/// 把那一手落定之前那一帧交给引擎的 `Scaffold.calculate` 现算一遍。
///
/// **点某一手走的是票 76 那条 `RecordOpened`**：轴只有票 75 那一根（ADR-0002），
/// 复盘不另开一条时间轴——跳过去与回得来因此与思考气泡共用同一套机制（票 86 的回程）。
[<RequireQualifiedAccess>]
module ReviewPanel =

    /// 强 AI 那一行此刻走到哪一步（票 93）。**四态是四个 case**（同 `BaselineStatus`，判据 12）：
    /// 「还没问」与「问了但它用不了」在页面上是两句完全不同的话。
    ///
    /// **它仍旧不上 `TableModel`**（票 93 定的）：强 AI 的对照是终局之后现算的一份投影，
    /// 不是这一场对局的事实——它不进牌谱（ADR-0002），也不该占住共用类型上的一格。
    /// **票 105 把它从面板那个组件里提到了 `useReview` 这一个钩子里**：
    /// 时间轴上那几枚标记与复盘那一列读的必须是**同一叠回执**，
    /// 各存一份就是两处可以各自漂的状态（而这一票要铉的正是「两处逐手对得上号」）。
    [<RequireQualifiedAccess>]
    type private Consulted =
        /// 还没人按那一枚：**一个字节都没拉**（ADR-0006 边界 1）。
        | Untouched
        /// 正在拉那几 MB / 逐手问。
        | Asking of hands: int
        /// 问完了：哪几手有那一行，以及这一趟的代价。
        | Ready of rows: Map<int, ReviewStrong> * bytes: int * loadMs: int * askMs: int
        /// 拉不动 / 起不来：**页面明说原因**（ADR-0006 边界 2），而复盘其余那几栏照常在。
        | Unavailable of reason: string

    /// 四态给机器看的那一半（`data-review-strong-state`）；人读的是旁边那一句中文。
    let private stateWire (consulted: Consulted) : string =
        match consulted with
        | Consulted.Untouched -> "untouched"
        | Consulted.Asking _ -> "asking"
        | Consulted.Ready _ -> "ready"
        | Consulted.Unavailable _ -> "unavailable"

    /// 一个「有就写数、没有就写空串」的钩子。**空串与 0 必须分得开**：
    /// 「这一手没有危险度」（桌上没有立直也没有副露）与「危险度排第 0」是两件事。
    let private mark (name: string) (value: int option) =
        prop.custom (name, value |> Option.map string |> Option.defaultValue "")

    /// 这一屏的复盘（票 105）：**一处算、两处消费**。
    ///
    /// 复盘那一块本身在牌桌**下面**，而时间轴在牌桌**上面**（票 75 那一根），
    /// 两处要标的却是同一批手。因此这一格把两件东西一起交出去：
    /// 画好的那一块，与时间轴要标的那几枚——**它们出自同一次 `Review.focused`**。
    ///
    /// `NoComparison` 是 `ReactElement` 逼出来的（同 `TableMsg`）：浏览器对象比不了大小，
    /// 而这一格本来就没有谁在比较。
    [<NoComparison>]
    type ReviewView = {
        /// 时间轴上要标出来的那几手（**就是筛选开着时复盘摆的那几条**）。
        ///
        /// **它不跟着那一枚开关变**：拨回「看全部」只是把其余那几百条也摆出来，
        /// 值得看的仍旧是那几手——轴上那几枚因此在两种状态下逐枚相同。
        Marks: ReviewMark list
        /// 复盘那一块（这一场还没打完时是空表：**整块不在 DOM 里**）。
        Panel: ReactElement list
    }

    /// 一条标注那一行。
    ///
    /// **抬头是一枚 `button`**（与手牌那几张、那一排鸣牌按钮同一个理由，票 87/88）：
    /// 键盘走得到、读屏念得出。点它就是 `RecordOpened (Some turn)`——
    /// 牌桌摆出那一手落定那一刻的快照，回放里游标跟着跳到那一帧。
    ///
    /// `data-*` 给无头闸门读、那几句中文给人读，**两头同源**（都出自这一条 `ReviewNote`）：
    /// 闸门再拿它们与引擎直接算的那份对拍，对不上就是错。
    let private noteRow
        (opened: int option)
        (dispatch: TableMsg -> unit)
        (strong: ReviewStrong option)
        (note: ReviewNote)
        =
        let trial = note.Trial
        let ukeire = trial |> Option.bind (fun trial -> trial.Ukeire)
        let danger = trial |> Option.bind (fun trial -> trial.Danger)

        let advice =
            ReviewNote.advice note
            |> Option.map (fun said ->
                Html.p [
                    prop.key "advice"
                    prop.className "review-advice"
                    prop.custom ("data-review-advice", if List.isEmpty note.Better then "best" else "better")
                    prop.custom ("data-review-better", List.length note.Better)
                    // 候选那几张的 mjai 记法与各自多出来的枚数：闸门按它对拍，人读的是同一句话。
                    prop.custom (
                        "data-review-candidates",
                        note.Better |> List.map (fun each -> Tile.toMjai each.Pai) |> String.concat " "
                    )
                    prop.custom (
                        "data-review-gains",
                        note.Better
                        |> List.map (fun each -> each.UkeireGain |> Option.map string |> Option.defaultValue "")
                        |> String.concat " "
                    )
                    prop.text said
                ])
            |> Option.toList

        // 强 AI 那一行（票 93）。**算不动就整行不出现**：没问过、它交不出来、
        // 或者它出的那一手不在这一包里时，这里**一个元素都没有**（与票 92 同一个规矩）。
        let baseline =
            strong
            |> Option.map (fun strong ->
                // 候选那几格（票 103）：三串空格分隔的值逐位对齐（id / 概率 / 那一条叫什么）。
                // **概率搬的是完整的那一串**（`probabilityToWire`）：闸门要拿它与 wasm 直接印出来的
                // 那一串逐位对拍，而页面上那句中文里是两位小数——**两者各有各的用处，不能合成一份**。
                let joined (pick: ReviewChoice -> string) =
                    strong.Candidates |> List.map pick |> String.concat " "

                Html.p [
                    prop.key "strong"
                    prop.className "review-strong"
                    // 闸门拿 `data-review-strong-id` 与它自己重建的那一份投影问出来的 id 逐手对拍；
                    // `data-review-strong` 那一串只是给人读的那一手叫什么。
                    prop.custom ("data-review-strong", strong.Key)
                    prop.custom ("data-review-strong-id", strong.ActionId)
                    prop.custom ("data-review-strong-diff", if strong.Differs then "1" else "")
                    prop.custom ("data-review-strong-ids", joined (fun choice -> string choice.ActionId))
                    prop.custom (
                        "data-review-strong-ps",
                        joined (fun choice -> ReviewStrong.probabilityToWire choice.P)
                    )
                    prop.custom ("data-review-strong-keys", joined (fun choice -> choice.Key))
                    // 上游一共给了几条（**不是上面那几串的长度**）：两者不相等就是「我们又扔了一条」。
                    prop.custom ("data-review-strong-total", strong.CandidatesTotal)
                    // 你那一手在它的候选里排第几、多少；**不在里面时两格都是空串**
                    // （写 0 的话就把「没抬到它」读成了「它给了零」）。
                    prop.custom (
                        "data-review-strong-rank",
                        strong.Yours
                        |> Option.map (fun choice -> string choice.Rank)
                        |> Option.defaultValue ""
                    )
                    prop.custom (
                        "data-review-strong-yours-p",
                        strong.Yours
                        |> Option.map (fun choice -> ReviewStrong.probabilityToWire choice.P)
                        |> Option.defaultValue ""
                    )
                    prop.text (ReviewStrong.toDisplay strong)
                ])
            |> Option.toList

        Html.li [
            prop.key note.Turn
            // 分歧那几手在这一列里**一眼扫得到**（票面：分歧点要跳出来）：
            // 只换一条左边线的颜色，**不打叉也不标红**——它不是裁判，是参照系。
            prop.className (
                match strong with
                | Some strong when strong.Differs -> "review-note review-note-diff"
                | Some _
                | None -> "review-note"
            )
            prop.custom ("data-review-turn", note.Turn)
            prop.custom ("data-review-frame", note.Frame)
            prop.custom ("data-review-kind", note.Kind)
            // 这一手为什么值得看（票 105）：`better` / `strong` / `both`，都不占就是空串
            // （拨到「看全部」时这一列里就会有空串的那几条）。**闸门逐手读它**：
            // 两条判据里哪一条点亮了这一手，不能只看并集（并集会把单条判据的漂盖住）。
            prop.custom ("data-review-worth", Review.worth strong note)
            prop.custom ("data-review-open", if opened = Some note.Turn then "1" else "")
            mark "data-review-shanten" (note.Scaffold |> Option.map (fun each -> Shanten.value each.Shanten))
            mark "data-review-shanten-after" (trial |> Option.map (fun each -> Shanten.value each.Shanten))
            mark "data-review-delta" (trial |> Option.map (fun each -> each.ShantenDelta))
            mark "data-review-ukeire" (ukeire |> Option.map Ukeire.total)
            mark "data-review-ukeire-kinds" (ukeire |> Option.map Ukeire.kindCount)
            prop.custom (
                "data-review-danger",
                danger
                |> Option.map (fun each -> DangerTier.toWire each.Tier)
                |> Option.defaultValue ""
            )
            mark "data-review-danger-rank" (danger |> Option.map (fun each -> each.Rank))
            prop.children (
                [
                    Html.button [
                        prop.key "jump"
                        prop.className "review-jump"
                        prop.testId $"review-turn-{note.Turn}"
                        prop.onClick (fun _ -> dispatch (RecordOpened(Some note.Turn)))
                        prop.text (ReviewNote.headline note)
                    ]
                    Html.p [
                        prop.key "figures"
                        prop.className "review-figures"
                        prop.text (ReviewNote.figures note)
                    ]
                ]
                @ advice
                @ baseline
            )
        ]

    /// 面板抬头那一排：说清这是谁的复盘、**这一列摆几条**（票 105），
    /// 以及**跳走了怎么回来**（票 86 的回程）。
    ///
    /// 「回到原处」只在真跳走了之后才画：没跳走时它是一枚点了什么也不会发生的按钮，
    /// 而那种按钮会让人以为自己刚才做错了什么。
    /// **筛选那一枚反过来恒在**：它是这一列的开关，一直得有得拨。
    let private head (seat: Seat) (count: int) (filtered: bool) (opened: int option) (dispatch: TableMsg -> unit) =
        let back =
            opened
            |> Option.map (fun turn ->
                Html.button [
                    prop.key "return"
                    prop.testId "review-return"
                    prop.onClick (fun _ -> dispatch (RecordOpened None))
                    prop.text $"回到原处（正在看第 {turn} 手）"
                ])
            |> Option.toList

        Html.div [
            prop.key "head"
            prop.className "bubble-head"
            prop.children (
                [
                    Html.h3 [
                        prop.key "title"
                        prop.testId "review-at"
                        prop.text $"复盘：座位 {Seat.index seat} 的逐手对照（{count} 手）"
                    ]
                    Html.button [
                        prop.key "filter"
                        prop.className "review-filter-toggle"
                        prop.testId "review-filter-toggle"
                        prop.onClick (fun _ -> dispatch ReviewFilterToggled)
                        prop.text (ReviewFilter.toggle filtered)
                    ]
                ]
                @ back
            )
        ]

    /// 强 AI 那一条抬头（票 93）：那一枚按钮、这一趟的代价、以及分歧几手。
    ///
    /// **它得有人按**（ADR-0006 边界 1）：那几 MB 只在按下去那一刻才拉。
    /// 自动拉的话，任何一局打完都会闷不作声地多下六兆字节——而复盘的头一层
    /// （向听 / 有效牌 / 危险度）本来就是零依赖的，不该为了叠一行把它拖下水。
    ///
    /// **不造总分**（票面边界）：这里只数得出「几手不同」，不算百分比、不排名次、
    /// 也不说「你比它差多少」——一写成百分比，下一步就是拿它当分数。
    let private strongHead (consulted: Consulted) (rows: ReviewStrong list) (hands: int) (ask: unit -> unit) =
        let button (label: string) =
            Html.button [
                prop.key "ask"
                prop.className "review-strong-ask"
                prop.testId "review-strong-ask"
                prop.onClick (fun _ -> ask ())
                prop.text label
            ]

        // **每一句自带一把 key**：同一层里两句话共用一把 key 会让 React 当场警告，
        // 而那句警告只在控制台里，页面上看不出来。
        let said (key: string) (text: string) =
            Html.p [ prop.key key; prop.className "review-strong-said"; prop.text text ]

        let children =
            match consulted with
            | Consulted.Untouched -> [
                button $"让强 AI 把这 {hands} 手也看一遍"
                said "how" "它在浏览器里跑，按下去那一刻才去取它那几 MB；它只看得见你当时看得见的那一份牌面，而且它不讲理由。"
              ]
            | Consulted.Asking asking -> [ said "asking" $"正在逐手问它（{asking} 手）……" ]
            | Consulted.Ready(_, bytes, loadMs, askMs) -> [
                said
                    "cost"
                    ($"强 AI 逐手看过了：{List.length rows} 手里有 {Review.disagreements rows} 手与你不同。"
                     + $"（{Baseline.bytesToDisplay bytes}，取它 {loadMs} ms、逐手重建局面并推理 {askMs} ms）")
                // **多出来的那半句只交代那几个数是什么**（票 103 的硬边界：概率不是理由）：
                // 「它只给前几条」这一句是必要的——不写的话，读者会把 0.62+0.21+0.11 不足 1 当成漏算。
                said "what" "它不是裁判：不同只是不同，它也说不出自己为什么那么打。每一行后面那几个数是它那一次前向给这几条候选的概率，照抄：它只给前几条，因此和不必是 1。"
              ]
            | Consulted.Unavailable reason -> [ said "why" reason; button "再试一次" ]

        Html.div [
            prop.key "strong-head"
            prop.className "review-strong-head"
            prop.testId "review-strong"
            prop.custom ("data-review-strong-state", stateWire consulted)
            prop.custom ("data-review-strong-rows", List.length rows)
            prop.custom ("data-review-strong-diffs", Review.disagreements rows)
            prop.custom (
                "data-review-strong-ms",
                match consulted with
                | Consulted.Ready(_, _, loadMs, askMs) -> string (loadMs + askMs)
                | Consulted.Untouched
                | Consulted.Asking _
                | Consulted.Unavailable _ -> ""
            )
            prop.children children
        ]

    /// 复盘那一块本身。`focused` 是**值得看的那几手**（票 105），`shown` 是这一列真摆出来的那几条
    /// （筛选开着时就是前者，关掉时是全部）。
    let private body
        (seat: Seat)
        (notes: ReviewNote list)
        (focused: ReviewNote list)
        (filtered: bool)
        (opened: int option)
        (consulted: Consulted)
        (rows: Map<int, ReviewStrong>)
        (ask: unit -> unit)
        (dispatch: TableMsg -> unit)
        =
        let shown = if filtered then focused else notes

        Html.section [
            prop.key "review"
            prop.className "settlement review"
            prop.testId "table-review"
            prop.custom ("data-review-seat", Seat.index seat)
            prop.custom ("data-review-notes", List.length notes)
            // 值得看的有几手（= 时间轴上那几枚标记）与这一枚开关拨在哪边：
            // **两格各管各的**——拨回「看全部」只改摆几条，不改哪几手值得看。
            prop.custom ("data-review-kept", List.length focused)
            prop.custom ("data-review-filter", ReviewFilter.toWire filtered)
            prop.custom ("data-review-open", opened |> Option.map string |> Option.defaultValue "")
            prop.children [
                head seat (List.length notes) filtered opened dispatch
                Html.p [
                    prop.key "intro"
                    prop.className "intro"
                    prop.testId "review-intro"
                    prop.text
                        "这几行是引擎按你当时看得见的牌现算的（向听、有效牌、危险度），不是打分——「更好的候选」只列在这几个数上不比你差、至少一项更好的那几张。点某一手：牌桌摆出那一刻的快照（回放里时间轴跟着跳过去），按「回到原处」就回来。"
                ]
                // **筛掉了多少当场说出来**（票 105 票面：不许静静地少显示）。
                // 那一句里只有两个数，且两态同一个顺序（`ReviewFilter.toDisplay`）。
                Html.p [
                    prop.key "filter"
                    prop.className "intro review-filter"
                    prop.testId "review-filter"
                    prop.custom ("data-review-filter", ReviewFilter.toWire filtered)
                    prop.custom ("data-review-shown", List.length shown)
                    // **没问过强 AI 时那一句只说得出第一条判据**：那几 MB 没拉之前，
                    // 第二条一手都筛不到，写出来就是声称一件此刻没有执行体的事（判据 2）。
                    prop.text (
                        ReviewFilter.toDisplay
                            filtered
                            (not (Map.isEmpty rows))
                            (List.length notes)
                            (List.length focused)
                    )
                ]
                strongHead consulted (rows |> Map.toList |> List.map snd) (List.length notes) ask
                Html.ol [
                    prop.key "notes"
                    prop.className "review-notes"
                    prop.children (
                        shown
                        |> List.map (fun note -> noteRow opened dispatch (Map.tryFind note.Turn rows) note)
                    )
                ]
            ]
        ]

    /// 打完了、但这一屏没有主语（上帝视角）时的那一句话。
    ///
    /// **它不是「面板的空态」**：复盘的第一个字是「你」，四家一起复盘不是这一票的东西。
    /// 用另一个 testId 是有意的——「对局中复盘面板不在 DOM 里」那道闸门认的是 `table-review`，
    /// 而这一句在对局中同样不存在（`ReviewShown.Hidden` 什么都不画）。
    let private hint =
        Html.p [
            prop.key "review-hint"
            prop.className "intro"
            prop.testId "table-review-hint"
            prop.text "这一场打完了：在上面那排视角里坐到某一席，就看得到那一席的逐手复盘（每一手的向听、有效牌、危险度，以及换打会怎样）。"
        ]

    /// 这一屏的复盘：**画好的那一块，加时间轴要标的那几枚**（票 105）。
    ///
    /// **贵的那一步包在 `React.useMemo` 里**：每一条标注都要给那一手现搭一份
    /// 决策包（`DecisionPackage.forSeat` 要一次从头 fold，再逐张试打），一整场东风战约
    /// 一百多手——不 memo 的话，回放每走一帧就重算一整场。
    ///
    /// **依赖只用整数**（`Review.settled` / 座位号 / `Review.signature`）：`Option` 与元组
    /// 每次渲染都是新对象，拿它们当依赖等于没有 memo。三样都不变时，摊开的是哪一手
    /// （`Review.opened`）与筛选拨在哪边（`model.ReviewFiltered`）照旧每次现读
    /// ——那两格不进 memo，它们每点一下就该变。
    ///
    /// **它是一个钩子而不是一个组件**（票 105 改的）：时间轴在牌桌上面、复盘在牌桌下面，
    /// 而两处要标的是同一批手。把那一叠回执关在面板自己的组件里的话，轴上那几枚就只能
    /// 另问一遍——那就是第二条可以自己漂的算路（判据 9 同一族）。
    /// 因此 `TablePage.Page` 调它一次，把 `Marks` 给控制条、`Panel` 摆在牌桌下面。
    [<Hook>]
    let useReview (model: TableModel, dispatch: TableMsg -> unit) : ReviewView =
        let seated =
            Review.addressed model |> Option.map Seat.index |> Option.defaultValue -1

        let shown =
            React.useMemo (
                (fun () -> Review.shown model),
                [| box (Review.settled model); box seated; box (Review.signature model) |]
            )

        // 强 AI 那一行的一叠回执（票 93）。**它不进 `TableModel`**（见 `Consulted` 那段）。
        let consulted, setConsulted = React.useState Consulted.Untouched

        // 换了一席 / 换了一份牌谱就丢掉：上一席的那一叠按手序对得上号，却是别人的牌
        // ——那是一屏逐手都对得上号的假话。
        React.useEffect ((fun () -> setConsulted Consulted.Untouched), [| box seated; box (Review.signature model) |])

        let ask (notes: ReviewNote list) () =
            let turns = Review.requests notes
            setConsulted (Consulted.Asking(List.length turns))

            (Baseline.askAll turns)
                .``then`` (fun answered ->
                    match answered with
                    | Ok review ->
                        // **算不动那几手根本不进这张表**（`strongOf` 回 None）：
                        // 没有键就没有那一行，页面上因此不会出现一个空壳。
                        let rows =
                            review.Answers
                            |> List.choose (fun (turn, answer) ->
                                notes
                                |> List.tryFind (fun note -> note.Turn = turn)
                                |> Option.bind (fun note -> Review.strongOf note answer)
                                |> Option.map (fun row -> row.Turn, row))

                        setConsulted (Consulted.Ready(Map.ofList rows, review.Bytes, review.LoadMs, review.AskMs))
                    | Error reason -> setConsulted (Consulted.Unavailable reason))
            |> ignore

        match shown with
        | ReviewShown.Hidden -> { Marks = []; Panel = [] }
        | ReviewShown.Unaddressed -> { Marks = []; Panel = [ hint ] }
        | ReviewShown.Notes(seat, notes) ->
            let rows =
                match consulted with
                | Consulted.Ready(rows, _, _, _) -> rows
                | Consulted.Untouched
                | Consulted.Asking _
                | Consulted.Unavailable _ -> Map.empty

            // **一处算、两处消费**：这一列摆哪几条与轴上标哪几枚，出自同一次 `Review.focused`。
            let focused = Review.focused rows notes

            {
                Marks = Review.marks focused
                Panel = [
                    body
                        seat
                        notes
                        focused
                        model.ReviewFiltered
                        (Review.opened model)
                        consulted
                        rows
                        (ask notes)
                        dispatch
                ]
            }
