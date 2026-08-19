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

    /// 一个「有就写数、没有就写空串」的钩子。**空串与 0 必须分得开**：
    /// 「这一手没有危险度」（桌上没有立直也没有副露）与「危险度排第 0」是两件事。
    let private mark (name: string) (value: int option) =
        prop.custom (name, value |> Option.map string |> Option.defaultValue "")

    /// 一条标注那一行。
    ///
    /// **抬头是一枚 `button`**（与手牌那几张、那一排鸣牌按钮同一个理由，票 87/88）：
    /// 键盘走得到、读屏念得出。点它就是 `RecordOpened (Some turn)`——
    /// 牌桌摆出那一手落定那一刻的快照，回放里游标跟着跳到那一帧。
    ///
    /// `data-*` 给无头闸门读、那几句中文给人读，**两头同源**（都出自这一条 `ReviewNote`）：
    /// 闸门再拿它们与引擎直接算的那份对拍，对不上就是错。
    let private noteRow (opened: int option) (dispatch: TableMsg -> unit) (note: ReviewNote) =
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

        Html.li [
            prop.key note.Turn
            prop.className "review-note"
            prop.custom ("data-review-turn", note.Turn)
            prop.custom ("data-review-frame", note.Frame)
            prop.custom ("data-review-kind", note.Kind)
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
            )
        // **强 AI 那一行今天不在这里**（票 93）：`advice` 下面**一个元素都没有**，
        // 也不写「暂无」——占位的那一行比不显示更糟（它看着像坏了）。
        // 93 接上时多的是：`ReviewNote` 上一格、这里一行、闸门里那条「一个都没有」翻面。
        ]

    /// 面板抬头那一排：说清这是谁的复盘，以及**跳走了怎么回来**（票 86 的回程）。
    ///
    /// 「回到原处」只在真跳走了之后才画：没跳走时它是一枚点了什么也不会发生的按钮，
    /// 而那种按钮会让人以为自己刚才做错了什么。
    let private head (seat: Seat) (count: int) (opened: int option) (dispatch: TableMsg -> unit) =
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
                Html.h3 [
                    prop.key "title"
                    prop.testId "review-at"
                    prop.text $"复盘：座位 {Seat.index seat} 的逐手对照（{count} 手）"
                ]
                :: back
            )
        ]

    /// 复盘那一块本身。
    let private body (seat: Seat) (notes: ReviewNote list) (opened: int option) (dispatch: TableMsg -> unit) =
        Html.section [
            prop.key "review"
            prop.className "settlement review"
            prop.testId "table-review"
            prop.custom ("data-review-seat", Seat.index seat)
            prop.custom ("data-review-notes", List.length notes)
            prop.custom ("data-review-open", opened |> Option.map string |> Option.defaultValue "")
            prop.children [
                head seat (List.length notes) opened dispatch
                Html.p [
                    prop.key "intro"
                    prop.className "intro"
                    prop.testId "review-intro"
                    prop.text
                        "这几行是引擎按你当时看得见的牌现算的（向听、有效牌、危险度），不是打分——「更好的候选」只列在这几个数上不比你差、至少一项更好的那几张。点某一手：牌桌摆出那一刻的快照（回放里时间轴跟着跳过去），按「回到原处」就回来。"
                ]
                Html.ol [
                    prop.key "notes"
                    prop.className "review-notes"
                    prop.children (notes |> List.map (noteRow opened dispatch))
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

    /// 复盘那一块。**贵的那一步包在 `React.useMemo` 里**：每一条标注都要给那一手现搭一份
    /// 决策包（`DecisionPackage.forSeat` 要一次从头 fold，再逐张试打），一整场东风战约
    /// 一百多手——不 memo 的话，回放每走一帧就重算一整场。
    ///
    /// **依赖只用整数**（`Review.settled` / 座位号 / `Review.signature`）：`Option` 与元组
    /// 每次渲染都是新对象，拿它们当依赖等于没有 memo。三样都不变时，摊开的是哪一手
    /// （`Review.opened`）照旧每次现读——那一格不进 memo，它每点一下就该变。
    [<ReactComponent>]
    let Panel (model: TableModel, dispatch: TableMsg -> unit) =
        let seated =
            Review.addressed model |> Option.map Seat.index |> Option.defaultValue -1

        let shown =
            React.useMemo (
                (fun () -> Review.shown model),
                [| box (Review.settled model); box seated; box (Review.signature model) |]
            )

        match shown with
        | ReviewShown.Hidden -> Html.none
        | ReviewShown.Unaddressed -> hint
        | ReviewShown.Notes(seat, notes) -> body seat notes (Review.opened model) dispatch

    /// 挂载点：**页面那一层只留这一行**（票 90 的边界）。
    let internal at (model: TableModel) (dispatch: TableMsg -> unit) : ReactElement list = [ Panel(model, dispatch) ]
