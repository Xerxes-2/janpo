namespace Janpo.Web

open Feliz
open Janpo

/// 画一家牌时要的那点桌面上下文（票 44）。**四样东西总是一起出现**，
/// 收成一个记录是为了 `seatPanel` 不用收五个位置参数。
///
/// **它不是第二份牌局状态**（ADR-0002）：四个字段全是 `Table` 与 `BoardView` 里现有的那份，
/// 只在渲染那一瞬拼在一起。
type Seating = {
    /// 这一桌的规则集：方位与副露来源的座位算术都要它。
    Ruleset: Ruleset
    /// 观测者；上帝视角没有（那枚「视角」标签与方位文字读的就是它）。
    Viewer: Seat option
    /// 方位的**参照系**（`Board.anchor` 算好的：坐着看是观测者，上帝视角是起家）。
    Anchor: Seat
    /// 亲：那枚「亲」标签。
    Oya: Seat
    /// 此刻轮到谁（`Board.teban`）；没人该动时是 None（刚打完、下家还没摸的那一瞬）。
    /// **名牌上那圈朱红读的就是它**（票 122 顶掉票 121 那四盏灯）。
    Teban: Seat option
    /// 名牌上那一句「这一席是谁在打」（票 82），按座位升序：
    /// Live 是档案名 + 脚手架档位（bot 席是「均匀随机」/「有主见」），回放是牌谱里的 `provider/model`。
    /// **判据不在这一层**（`TableState.nameplates`）：这里只把算好的那一句画到名牌上。
    Nameplates: string list
}

/// 牌桌与结算的视图（票 70 从 `TablePage.fs` 拆出来的第三块）：一家的三排牌、牌桌中央、
/// 结算与终局精算、危险度面板，以及把它们摆成一屏的 `tableBody`。
///
/// **只画不判**：这里读的全是 `Board` 算好的投影与引擎给的值（ADR-0002 的那份局面），
/// 页面自己的状态在 `TableState`，配桌与模型面板在 `TablePanel`。
[<RequireQualifiedAccess>]
module TableBoard =

    // ---- 视图：牌 ----

    /// 一张亮着的牌。**人类可读形式只在这里出现**（ADR-0001）；`data-pai` 上仍是 mjai 记法，
    /// 无头验收数「这一家的牌露没露出来」靠的就是它。
    /// `extras` 给河那边添摸切标记，其余处传空表。
    let private paiSpan (key: int) (extra: string) (extras: IReactProperty list) (pai: Tile) =
        let akadora = if Tile.isAkadora pai then " aka" else ""

        Html.span (
            [
                prop.key key
                prop.className $"tile{extra}{akadora}"
                prop.custom ("data-pai", Tile.toMjai pai)
            ]
            @ extras
            @ [ prop.text (Tile.toDisplay pai) ]
        )

    let private face (key: int) (extra: string) (pai: Tile) = paiSpan key extra [] pai

    /// 一张**点得出去**的牌（票 87）：轮到真人打牌时，他手里凡在**合法动作集**里的那几张
    /// 各是一枚按钮。**这一层一条规则都不判**（spec 的 UI 决策：合法性驱动 UI）——
    /// `id` 与 `label` 都是引擎给的（`HumanSeat.dahai` 现问那一份决策包），
    /// 于是立直之后只剩摸切、食替不许打回去这些事，在页面上是「那张牌点不动」的自然后果。
    ///
    /// **它是 `button` 而不是加了 onClick 的 `span`**：键盘走得到、读屏念得出、`:focus-visible`
    /// 那圈靛青自然就有——「点得动」不该只对拿鼠标的人成立。
    /// `data-pai` 与普通那张逐字相同（闸门原来那几条一条不必改），另加一个 `data-dahai-id`。
    let private playableFace
        (dispatch: TableMsg -> unit)
        (key: int)
        (extra: string)
        (id: int)
        (label: string)
        (pai: Tile)
        =
        let akadora = if Tile.isAkadora pai then " aka" else ""

        Html.button [
            prop.key key
            prop.className $"tile playable{extra}{akadora}"
            prop.custom ("data-pai", Tile.toMjai pai)
            // 点它就是提交这一条。**闸门读它、页面也只用它**：手切与摸切是两条不同的动作，
            // 而「这一张对应哪一条」只有引擎答得出（`Action.Dahai` 的 `tsumogiri`）。
            prop.custom ("data-dahai-id", id)
            prop.title $"点它就打出去：{label}"
            prop.onClick (fun _ -> dispatch (HumanPlayed id))
            prop.text (Tile.toDisplay pai)
        ]

    /// 一张扣着的牌。**它没有 `data-pai`**——投影里压根没有那张牌，渲染层无从写起。
    let private back (key: int) =
        Html.span [ prop.key key; prop.className "tile back"; prop.text "背" ]

    let private faces (extra: string) (tiles: Tile list) =
        tiles |> List.mapi (fun index pai -> face index extra pai)

    // ---- 视图：一家 ----

    /// 暗牌摆成「手里的 + 刚摸进的那张单独摆在右边」。刚摸进那张**本来就在手牌里**
    /// （`RevealedSeat.Hand` 含它），这里只是把它拎出来摆开——摸切打的就是它。
    ///
    /// **摸切与手切在这里分峔**（票 87）：拎出来那张点下去是摸切（`tsumogiri = true`），
    /// 手里那几张是手切。它们本来就是两个不同的动作（河上的手切信息是公开信息），
    /// 而牌桌本来就把刚摸那张摆开了——**两样东西不必各做一份 UI**。
    ///
    /// `play` 给不出 id 的那几张就是普通牌（不轮到他、或者那一条不合法）。
    let private handTiles (play: Tile -> bool -> (int * string) option) (dispatch: TableMsg -> unit) (hand: HandView) =
        let one (key: int) (extra: string) (tsumogiri: bool) (pai: Tile) =
            match play pai tsumogiri with
            | Some(id, label) -> playableFace dispatch key extra id label pai
            | None -> face key extra pai

        match hand with
        | HandView.Concealed count -> [ for index in 1..count -> back index ]
        | HandView.Revealed(tiles, drawn) ->
            match drawn |> Option.bind (fun pai -> tiles |> List.tryFindIndex ((=) pai)) with
            | None -> tiles |> List.mapi (fun index pai -> one index "" false pai)
            | Some index ->
                let held = List.take index tiles @ List.skip (index + 1) tiles

                (held |> List.mapi (fun key pai -> one key "" false pai))
                @ [ one (List.length held) " drawn" true (List.item index tiles) ]

    /// 河。**手切与摸切画得不一样**（`tsumogiri` 那一位是公开信息），
    /// 摸切那几张画成虚线加淡色，`data-tsumogiri` 上也写着，无头验收读得到。
    let private kawaTiles (kawa: KawaEntry list) =
        kawa
        |> List.mapi (fun index entry ->
            let marks = [
                prop.custom ("data-tsumogiri", (if entry.Tsumogiri then "true" else "false"))
                prop.title (if entry.Tsumogiri then "摸切" else "手切")
            ]

            paiSpan index (if entry.Tsumogiri then " tsumogiri" else "") marks entry.Pai)

    /// 副露的来源怎么称呼：**相对副露方**的中文说法（CONTEXT.md 的 Shimocha / Toimen / Kamicha）。
    /// 措辞与 prompt 尾部那头同一套词（`web/src/agent/wording.ts` 的 `relative`），
    /// 形状照 `Threat.who`：座位数不是 4 时没有对家，那时按座位号说。
    let private nakiFrom (view: NakiView) : string option =
        match view.Relative, view.Target with
        | Some 1, _ -> Some "下家"
        | Some 2, _ -> Some "对家"
        | Some 3, _ -> Some "上家"
        | Some _, Some target -> Some $"座位 {Seat.index target}"
        | _, _ -> None

    /// 副露里的一张。**横放的那张是从他家那儿来的**（`NakiTileView.FromOther`）：
    /// 碰 / 吃 / 大明杠是被鸣的那张，加杠是当初碰来的那张，暗杠一张也没有。
    /// 记号挑**朝向**而不是描边或换色：虚线加淡色是摸切、45° 斜纹是牌背、红字是赤牌、
    /// 额外间距是刚摸那张——四条都占了，而横放又正是牌谱的标准画法。
    ///
    /// 加杠**加上去的那张**出自自家手里，不是鸣来的，所以**不横放**（真牌桌上它也是侧着的，
    /// 但那样一来「横放 = 从他家来的」这条记号就得重定义，票 38 占的那一维不动）；
    /// 它前面摆一枚「＋」（牌谱文字记法里的 `中中中＋中`），并且**叠在横放那张上**（见 `nakiSlot`）。
    /// `data-naki-taken` / `data-naki-added` 给无头闸门读（票 38）。
    let private nakiTile (takenTitle: string) (key: int) (tile: NakiTileView) : ReactElement list =
        match tile.Pai with
        | None -> [ back key ]
        | Some pai ->
            let marks = [
                if tile.FromOther then
                    prop.custom ("data-naki-taken", "true")
                    prop.title takenTitle
                if tile.Added then
                    prop.custom ("data-naki-added", "true")
                    prop.title "加杠加上去的那张（自家手里的第四张）"
            ]

            let extra =
                (if tile.FromOther then " taken" else "")
                + (if tile.Added then " added" else "")

            let plus = [
                if tile.Added then
                    Html.span [
                        prop.key $"add-{key}"
                        prop.className "naki-add"
                        prop.title "加杠加上去的那张（自家手里的第四张）"
                        prop.text "＋"
                    ]
            ]

            plus @ [ paiSpan key extra marks pai ]

    /// 副露里的一个**槽位**（票 51）：一格一张，只有加杠那一格是两张。
    ///
    /// DOM 里是**从下往上**（先写底下那张），样式拿 `column-reverse` 把它竖起来——
    /// 于是「当初碰来的那张」仍在底下且仍在它当初那一格，加上去的那张在上面。
    let private nakiSlot (takenTitle: string) (key: int) (slot: NakiSlot) =
        Html.span [
            prop.key key
            prop.className "naki-slot"
            prop.children (slot |> List.mapi (nakiTile takenTitle) |> List.concat)
        ]

    /// 一组副露。三件事要看得出来：**种类**、**被鸣的是哪一张**（横放）、**来自谁**。
    /// 杠仍看得出形态：四张一组，暗杠两端扣着（牌桌上就是这个摆法）且**没有来源**
    /// ——它不是鸣来的。
    ///
    /// **来源走位置编码**（票 51）：横放那张落在第几格就是它来自谁，牌桌上因此
    /// **不再写那行「来自X」**（票 38 当初否决位置编码的理由是「四家是竖排面板、方位无锚」，
    /// 票 44 把牌桌改成四家围坐之后那条理由就没了）。但那句中文**仍然写给读屏用户**
    /// （`sr-only`），`data-naki-from` 也原样留着——两道闸门读的就是它。
    let private nakiGroup (ruleset: Ruleset) (owner: Seat) (key: int) (naki: Naki) =
        let view = Board.nakiView ruleset owner naki
        let kind = NakiKind.toDisplay view.Kind
        let from = nakiFrom view

        let takenTitle =
            let who =
                from |> Option.map (fun each -> $"来自{each}") |> Option.defaultValue "从他家鸣来"

            if view.Kind = NakiKind.Kakan then
                $"当初碰来的那张（{who}）"
            else
                $"被鸣的那张（{who}）"

        // 来源那枚标签与它的 `data-`：暗杠一样都没有。绝对座位另写一份（`data-naki-from-seat`），
        // 闸门拿它与副露方的座位号对得上才算数——参照系要是漂到观测者那边，这一条当场就红。
        let sourceMarks =
            match from, view.Target with
            | Some who, Some target -> [
                prop.custom ("data-naki-from", who)
                prop.custom ("data-naki-from-seat", Seat.index target)
              ]
            | _, _ -> []

        // 来源那句话在牌桌上**看不见了**（主人裁定：位置为主、文字不留），但它仍然写在 DOM 里
        // 给**读屏用户**：位置读屏读不出来，删干净就是把来源对他们藏了。
        // `sr-only` 是那一半的画法；闸门两头都核：文字必须在、且必须看不见。
        let sourceLabel =
            match from, view.Target with
            | Some who, Some target -> [
                Html.span [
                    prop.key "from"
                    prop.className "naki-from sr-only"
                    prop.title $"这一组是从座位 {Seat.index target} 那儿鸣来的"
                    prop.text $"来自{who}"
                ]
              ]
            | _, _ -> []

        Html.span (
            [ prop.key key; prop.className "naki"; prop.custom ("data-naki", kind) ]
            @ sourceMarks
            @ [
                prop.children (
                    Html.span [ prop.key "kind"; prop.className "naki-kind"; prop.text kind ]
                    :: sourceLabel
                    @ (view.Slots |> List.mapi (nakiSlot takenTitle))
                )
            ]
        )

    /// 一排牌：小标题加那几张。**张数写在标题里**——「各家手牌数」那条验收看的就是它，
    /// 而他家的张数来自投影（`MaskedSeat.HandCount`），不是渲染层拿副露数推的。
    ///
    /// `marks` 是这一排的 `data-`（票 44）：标题里的那个数字是给人看的，它们是同一件事
    /// 给机器看的那一半——闸门拿两者与真画出来的张数三头对。
    let private tileRow
        (testId: string)
        (extra: string)
        (marks: IReactProperty list)
        (label: string)
        (tiles: ReactElement list)
        =
        Html.div (
            [ prop.key testId; prop.className $"tiles {extra}"; prop.testId testId ]
            @ marks
            @ [
                prop.children (
                    Html.span [ prop.key "label"; prop.className "row-label"; prop.text label ]
                    :: tiles
                )
            ]
        )

    /// 立直状态给机器看的那一半（票 44）。人看的是头上那枚「立直」标签
    /// （`RiichiState.toDisplay`）；两者对不上时闸门报错。
    /// **宣言与成立是两步**（`RiichiState` 那段注释），因此不能只写一个布尔：
    /// 供托里那一根只跟着**成立**的那几家走。
    let private riichiWire (state: RiichiState) : string =
        if RiichiState.isAccepted state then "accepted"
        elif RiichiState.isDeclared state then "declared"
        else "none"

    /// 一家的牌与标记。
    ///
    /// **三排牌从外往内摆**：副露、手牌、河——真牌桌上就是这个顺序（副露在身侧最外，
    /// 河摆在桌心）。四个方位各自把这三排**朝中心那一边翻**，翻法全在 `styles.css`（按
    /// `data-seat-position` 选），这一层只负责把顺序摆对。
    ///
    /// `data-seat-position` 是方位给机器看的那一半，**样式读的也是它**：
    /// 属性写着下家却画在左边这种事因此不存在（闸门仍然两样都核：
    /// 属性对不对、以及画出来的坐标对不对）。
    /// **一席分成两半（票 117）**：牌在转的那一半（`seatZone`），
    /// 名牌与气泡在不转的那一半（`seatPlate`）。
    ///
    /// 四席绕盘心 `rotate(0/90/180/270)` 之后，**摆在旋转区里的字会跟着倒过来**。
    /// 牌面转了照旧认得（真牌桌上就是这么摆的），而名字、点数、气泡里那段推理
    /// 倒着就没法读——屏幕前只有**一个**读者，不是四个围坐的人。
    ///
    /// 拆法受一条现成约束钉死：`verify-bubbles` 靠 `bubble.closest(".seat")`
    /// 数「气泡在不在它那一席的框里」。∴ **名牌那张卡片仍带 `.seat` 类、
    /// 气泡仍在它里面**，那三条断言（矩形非空 / 气泡在框内 / 不压牌与中央）
    /// 在新几何下原样成立，**一个字没改**。
    let private seatPlate (seating: Seating) (bubble: ReactElement list) (view: SeatView) =
        let index = Seat.index view.Seat
        let position = Board.position seating.Ruleset seating.Anchor view.Seat

        // 立直与一发那两枚多一个 class（票 80）：配色分工里「朱红 = 立直」，
        // 样式表才选得中它们——CSS 选不了文字内容。语义与文字一个都没动。
        let marks =
            [
                if view.Seat = seating.Oya then
                    "亲", "mark"
                if Some view.Seat = seating.Viewer then
                    "视角", "mark"
                if RiichiState.isActive view.Riichi then
                    RiichiState.toDisplay view.Riichi, "mark riichi"
                if view.Ippatsu then
                    "一发", "mark riichi"
            ]
            |> List.map (fun (mark, className) -> Html.span [ prop.key mark; prop.className className; prop.text mark ])

        // 名牌上的选手（票 82）：一眼看得出这一席是哪份档案、哪一档在打（或者是自带 bot）。
        // 没话可说时整个不画（字数对不上的牌谱）——空牌子比没牌子更让人以为掉了东西（票 32 同一条）。
        let playerLabel =
            seating.Nameplates
            |> List.tryItem index
            |> Option.filter (fun player -> player <> "")
            |> Option.map (fun player ->
                Html.span [
                    prop.key "player"
                    prop.className "seat-player"
                    prop.testId $"seat-{index}-player"
                    prop.custom ("data-player", player)
                    prop.text player
                ])
            |> Option.toList

        // 方位的文字只在**坐着看**时写：上帝视角根本没有观测者，写「自家」会指向一个不存在的人。
        // 那一档的参照系改由牌桌中央那一句话声明（`tableCenter`）。
        let positionLabel = [
            if Option.isSome seating.Viewer then
                Html.span [
                    prop.key "position"
                    prop.className "seat-position"
                    prop.text (Position.toDisplay position)
                ]
        ]

        Html.section [
            prop.key index
            prop.className "seat"
            prop.testId $"seat-{index}"
            prop.custom ("data-seat", index)
            prop.custom ("data-seat-position", Position.toWire position)
            prop.custom ("data-riichi", riichiWire view.Riichi)
            // 轮到谁（票 122）：**框住那张名牌**，而不是另点一枚记号。
            // 名牌上本来就写着这一席是谁，圈住它就把「轮到谁」说完了；
            // 票 121 那四盏灯是同一件事的第二套编号（同票 86 记过的那一族）。
            // 人读的是那圈朱红，闸门读这一格——两头对不上就是错。
            prop.custom ("data-teban", (if seating.Teban = Some view.Seat then "on" else "off"))
            prop.children (
                [
                    Html.div [
                        prop.className "seat-head"
                        prop.children (
                            [
                                Html.span [
                                    prop.key "name"
                                    prop.className "seat-name"
                                    prop.text $"座位 {index}・{Kaze.toDisplay view.Jikaze}家"
                                ]
                            ]
                            @ playerLabel
                            @ positionLabel
                            @ [
                                Html.span [
                                    prop.key "score"
                                    prop.className "seat-score"
                                    prop.testId $"seat-{index}-score"
                                    prop.custom ("data-score", view.Score)
                                    prop.text (string view.Score)
                                ]
                                Html.span [
                                    prop.key "junme"
                                    prop.className "seat-junme"
                                    prop.testId $"seat-{index}-junme"
                                    prop.custom ("data-junme", view.Junme)
                                    prop.text $"第 {view.Junme} 巡"
                                ]
                            ]
                            @ marks
                        )
                    ]
                ]
                // 思考气泡（票 76）跟着名牌走，不进旋转区（票 117）：
                // 它里面是一段要读的推理，跟着席区转 90°/180°/270° 就没法读了。
                @ bubble
            )
        ]

    /// 一席的**牌**（票 117）：副露、手牌、河。这一层绕盘心转四向。
    ///
    /// **四席同一份 DOM**：里面一律按「坐在下方那一家」摆，画到哪个方位
    /// 只由 `data-seat-position` 定。∴ 方位不是四份布局，是**一份布局的四个角度**——
    /// 旧那套三列 grid（左右两家竖着摆）靠的是四套不同的排法，
    /// 那才是那一段「左→右在屏幕上就是上→下」说明字的来处。
    let private seatZone
        (seating: Seating)
        (play: Tile -> bool -> (int * string) option)
        (dispatch: TableMsg -> unit)
        (view: SeatView)
        =
        let index = Seat.index view.Seat
        let position = Board.position seating.Ruleset seating.Anchor view.Seat

        let handCount =
            match view.Hand with
            | HandView.Revealed(hand, _) -> List.length hand
            | HandView.Concealed count -> count

        let hidden =
            match view.Hand with
            | HandView.Revealed _ -> "false"
            | HandView.Concealed _ -> "true"

        Html.div [
            prop.key $"zone-{index}"
            prop.className "zone"
            prop.custom ("data-seat", index)
            prop.custom ("data-seat-position", Position.toWire position)
            prop.custom ("data-riichi", riichiWire view.Riichi)
            prop.children [
                tileRow
                    $"seat-{index}-naki"
                    "naki-row"
                    [ prop.custom ("data-naki-count", List.length view.Naki) ]
                    "副露"
                    (view.Naki |> List.mapi (nakiGroup seating.Ruleset view.Seat))
                tileRow
                    $"seat-{index}-hand"
                    "hand"
                    [
                        prop.custom ("data-hand-count", handCount)
                        // 他家的手牌是扣着的——**这不是渲染纪律而是投影的形状**（`HandView.Concealed`
                        // 里根本没有牌面）。写出来是让闸门能拿它与“画出来几张牌背”对得上。
                        prop.custom ("data-hand-hidden", hidden)
                    ]
                    $"手牌 {handCount}"
                    (handTiles play dispatch view.Hand)
                tileRow
                    $"seat-{index}-kawa"
                    "kawa"
                    [ prop.custom ("data-kawa-count", List.length view.Kawa) ]
                    $"河 {List.length view.Kawa}"
                    (kawaTiles view.Kawa)
            ]
        ]

    // ---- 视图：场况与结算 ----

    /// 副露那一行的参照系（票 51）。它与牌桌布局那一句不是同一个参照系，因此必须各说各的：
    /// 牌桌布局以**看牌桌的那个人**为准，副露里的左中右以**副露方自己**为准。
    /// M1 传下来的第六条（相对方位必须显式声明参照系）在这里就是这一句。
    /// 票 82 给它接了后半句：左右两家的那一排在屏幕上是竖的——**票 117 之后仍然成立**。
    let private nakiLegend =
        "副露：横放那张的位置就是来源，按副露方自己的左右算——最左＝上家、中间＝对家、最右＝下家（暗杠无源）；左右两家的牌侧着摆，那条「左→右」在屏幕上就是「上→下」"

    /// 场况里的一项。`marks` 是它给机器看的那一半（票 44），挂在带 testId 的那个元素上：
    /// 人读的是「东1局 2 本场」这句中文，闸门读的是 `data-honba`，两者对不上就报错。
    let private field (marks: IReactProperty list) (testId: string) (label: string) (value: string) =
        Html.span [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.span ([ prop.className "values"; prop.testId testId ] @ marks @ [ prop.text value ])
            ]
        ]

    /// 一排牌，带标题与钩子。宝牌与里宝牌指示牌都是它。
    let private tileField (testId: string) (label: string) (tiles: Tile list) =
        Html.span [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.span [
                    prop.className "tiles"
                    prop.testId testId
                    prop.custom ("data-tile-count", List.length tiles)
                    prop.children (faces "" tiles)
                ]
            ]
        ]

    /// 牌桌中央（票 44）：场况、供托与立直棒、剩余摸牌、宝牌指示牌——真牌桌上它们就摆在中间，
    /// 四家围着它坐。里宝牌只有上帝视角有，**而且要等这一局结算**（`settled`，见下）。
    ///
    /// **方位的参照系写在这里**（M1 传下来的第六条：相对方位必须显式声明参照系）：
    /// 牌桌上写着「下家」的那家到底是谁的下家，读者不必自己猜。
    ///
    /// **桌心那四盏灯撤了**（票 122，主人裁的：「用名字框红圈的方式表示到谁了
    /// 比现在用红点优雅」）。它们与名牌说的是同一件事，而名牌上本来就写着这一席是谁
    /// ——框住它就说完了，另点一枚红点是同一件事的第二套编号（票 86 记过同一族）。
    /// 「轮到谁」这条语义一分没丢：它挪到了名牌自己的 `data-teban` 上（`seatPlate`）。
    let private tableCenter (settled: bool) (board: BoardView) =
        // 立直棒是「供托 N 根」那个数字的实物画法，一根都没时**整个字段不画**（同下面的里宝牌）：
        // 否则只剩一枚「立直棒」标签后面空着，看着像掉了东西（票 32 扫同类隐形时收的）。
        let bou =
            if board.Kyotaku = 0 then
                []
            else
                [
                    Html.span [
                        prop.key "bou"
                        prop.className "field"
                        prop.children [
                            Html.span [ prop.className "label"; prop.text "立直棒" ]
                            Html.span [
                                prop.className "bou-row"
                                prop.testId "table-bou"
                                prop.children [
                                    for index in 1 .. board.Kyotaku ->
                                        Html.span [ prop.key index; prop.className "bou" ]
                                ]
                            ]
                        ]
                    ]
                ]

        // 里宝牌只有上帝视角有（坐着看时投影里就是空表），**而且未结算时一律不摆**（票 76）。
        //
        // 里宝牌只在有人立直和了的那一刻翻开、才算番；开局就摆在桌心的话，
        // 「宝牌指示牌」下面紧跟一行「里宝牌指示牌」会让人以为这一局有两个宝牌在生效
        // ——**问题不是剧透而是误导**（裁决「71-8 的余波」）。结算那一屏照旧摆：
        // 那一刻它真的翻开了，而结算面板上的「里宝牌 N 番」正需要它摆在旁边对。
        let ura =
            if List.isEmpty board.UraMarkers || not settled then
                []
            else
                [ tileField "table-uradora" "里宝牌指示牌" board.UraMarkers ]

        // 参照系那一句。`data-anchor` 是它给机器看的那一半：闸门拿它与四家真画在哪个格子里对。
        let anchor = Board.anchor board

        // 「坐在座位 0：自家在下、下家在右、对家在上、上家在左」**删了**（票 121，主人裁的）。
        //
        // 那是麻将的普适约定，等于印一句「上方是上」。真正有信息量的只有「谁在下方」，
        // 而**四张名牌自己就写着**：下方那张写着「座位 0・东家」。
        //
        // M1 第六条（相对方位必须显式声明参照系）没被绕过——**名牌逐个标注位置，
        // 比一句概括更强**：读者不必把「下家在右」在脑子里套一遍，直接读那一张就是。
        // 参照系那一格给机器看的那一半（`data-anchor`）一格没动。
        //
        // 副露里的左中右是**另一个**参照系（以副露方自己为准），它仍要显式声明——
        // 那一句在「怎么读这张牌桌」那个抽屉里（票 120）。
        Html.div [
            prop.className "table-center"
            prop.testId "table-center"
            prop.custom ("data-anchor", Seat.index anchor)
            prop.children (
                [
                    field
                        [
                            prop.custom ("data-bakaze", Kaze.toDisplay board.Bakaze)
                            prop.custom ("data-kyoku", board.Kyoku)
                            prop.custom ("data-honba", board.Honba)
                        ]
                        "table-kyoku"
                        "场况"
                        $"{Kaze.toDisplay board.Bakaze}{board.Kyoku}局 {board.Honba} 本场"
                    field [ prop.custom ("data-kyotaku", board.Kyotaku) ] "table-kyotaku" "供托" $"{board.Kyotaku} 根"
                    field
                        [ prop.custom ("data-wall", board.WallRemaining) ]
                        "table-wall"
                        "剩余摸牌"
                        $"{board.WallRemaining} 张"
                    tileField "table-dora" "宝牌指示牌" board.DoraMarkers
                ]
                @ ura
                @ bou

            )
        ]

    /// 副露那一行的参照系（票 51 / 82），**摆在牌桌外沿而不是桌心**（票 117）。
    ///
    /// 它是**图例**，不是场况：桌心那一格写的是这一局此刻的事实（第几局、几本场、
    /// 供托、剩余摸牌、宝牌指示牌），而这一句是「怎么读副露」的说明，读一次就够。
    /// 摆在桌心会把那一格撑到压住四家的河。
    ///
    /// **没有删掉它**。票 117 的票面写着「标准几何自证方位，那段话就该消失」——
    /// 那个前提是错的：后半句在**任何**四家围坐的布局里都成立（上家那一层转了 90°，
    /// 他的副露行在屏幕上就是竖的），几何不会让它自证；前半句是 M1 第六条要求的
    /// 参照系声明（相对方位必须显式声明参照系），更不能删。
    /// 「怎么读这张牌桌」——**收进一个点开的抽屉**（票 120，主人裁的）。
    ///
    /// 里面这几件都是**看一次就够**的图例：副露方位的参照系（票 51／M1 第六条要求
    /// 显式声明，∴ 只能收不能删）、摸切的虚线、坐下去之后他家暗牌就不存在。
    /// 常驻在版面上时它们是噪音——每一局都在那里，而人只读第一次。
    let internal nakiLegendLine () =
        Html.details [
            prop.className "board-legend"
            prop.testId "table-legend"
            prop.children [
                Html.summary [ prop.testId "table-legend-open"; prop.text "怎么读这张牌桌" ]
                Html.p [
                    prop.className "naki-legend"
                    prop.testId "table-naki-legend"
                    prop.text nakiLegend
                ]
                Html.p [
                    prop.className "naki-legend"
                    prop.text "虚线描边的牌是摸切。坐到某一席之后，他家的暗牌在页面拿到的数据里根本不存在——「模型看到的和你一样多」就是这么验的。"
                ]
            ]
        ]

    /// 点数授受一行：按座位升序的增减。
    let private deltas (values: int list) =
        values
        |> Seat.indexed
        |> List.map (fun (seat, delta) ->
            let sign = if delta > 0 then "+" else ""
            $"座位 {Seat.index seat} {sign}{delta}")
        |> String.concat "　"

    let private horaLines (hora: HoraView) =
        let yaku =
            hora.Yaku
            |> List.map (fun (yaku, value) ->
                match value with
                | YakuValue.Han han -> $"{Yaku.toDisplay yaku} {han} 番"
                | YakuValue.Yakuman multiplier -> $"{Yaku.toDisplay yaku} 役满×{multiplier}")

        let dora = [
            if hora.Dora > 0 then
                $"宝牌 {hora.Dora} 番"
            if hora.Uradora > 0 then
                $"里宝牌 {hora.Uradora} 番"
            if hora.Akadora > 0 then
                $"红宝牌 {hora.Akadora} 番"
        ]

        let limit =
            match Limit.toDisplay hora.Limit with
            | "" -> ""
            | level -> $"（{level}）"

        [
            if hora.Actor = hora.Target then
                $"座位 {Seat.index hora.Actor} 自摸 {Tile.toDisplay hora.Pai}"
            else
                $"座位 {Seat.index hora.Actor} 荣和 座位 {Seat.index hora.Target} 打出的 {Tile.toDisplay hora.Pai}"
            "役：" + (yaku @ dora |> String.concat "、")
            $"{hora.Fu} 符 {hora.Fan} 番 {hora.HoraPoints} 点{limit}"
            "点数授受：" + deltas hora.Deltas
        ]

    let private ryuukyokuLines (ryuukyoku: Ryuukyoku) =
        let tenpai =
            ryuukyoku.Tenpais
            |> Seat.indexed
            |> List.filter snd
            |> List.map (fun (seat, _) -> $"座位 {Seat.index seat}")

        // 听牌家一家都没有也要写出来（途中流局就是这样），空白一行分不出「没人听」与「没画」。
        let tenpaiText =
            if List.isEmpty tenpai then
                "无"
            else
                String.concat "、" tenpai

        [
            RyuukyokuReason.toDisplay ryuukyoku.Reason
            "听牌：" + tenpaiText
            "点数授受：" + deltas ryuukyoku.Deltas
        ]

    let private settlementPanel (settlement: Settlement) =
        let lines =
            match settlement.Outcome with
            | Outcome.Hora horas -> horas |> List.collect horaLines
            | Outcome.Ryuukyoku ryuukyoku -> ryuukyokuLines ryuukyoku

        let title =
            match settlement.Outcome with
            | Outcome.Hora _ -> "和了"
            | Outcome.Ryuukyoku _ -> "流局"

        // 这一局之后往哪走。**末局不该邀人「进下一局」**：局数序列走完就终局，
        // 连庄也不延长，因此终局那一行压过连庄与否（票 39）。
        // `data-progress` 给无头验收读，不必去分析中文。
        let progress, text =
            match settlement.Ended, settlement.Renchan with
            | true, _ -> "ended", "终局：这一场到此打完，下面是终局精算"
            | false, true -> "renchan", "亲连庄"
            | false, false -> "next", "亲流局，进下一局"

        Html.section [
            prop.key "settlement"
            prop.className "settlement"
            prop.testId "table-settlement"
            prop.children (
                [ Html.h3 [ prop.key "title"; prop.text title ] ]
                @ (lines |> List.mapi (fun index line -> Html.p [ prop.key index; prop.text line ]))
                @ [
                    Html.p [
                        prop.key "renchan"
                        prop.testId "table-renchan"
                        prop.custom ("data-progress", progress)
                        prop.text text
                    ]
                ]
            )
        ]

    /// 终局精算。**座位卡读的是同一份数**（`Board.ofTable` 在终局那一刻换成精算后的点数），
    /// 因此这一屏上只有一种说法。供托那几根去了哪要写出来：场况行已经归零、
    /// 桌上那根立直棒也收走了，不说一句就成了「凭空不见」。
    let private resultPanel (final: FinalView) =
        let kyotaku =
            if final.Kyotaku = 0 then
                []
            else
                [
                    Html.p [
                        prop.key "kyotaku"
                        prop.testId "table-result-kyotaku"
                        prop.text $"场上剩下的供托 {final.Kyotaku} 根（{final.KyotakuScore} 点）已归 1 位"
                    ]
                ]

        Html.section [
            prop.key "result"
            prop.className "settlement"
            prop.testId "table-result"
            prop.children (
                [
                    Html.h3 [ prop.key "title"; prop.text "终局精算" ]
                    Html.p [
                        prop.key "ranking"
                        prop.testId "table-result-ranking"
                        prop.text (GameResult.toDisplay final.Result)
                    ]
                ]
                @ kyotaku
            )
        ]

    // ---- 视图：终局记分卡（票 133） ----

    /// 记分卡上一行那几个 `data-*`。**闸门读的就是它们**：无头闸门拿每一格与引擎
    /// 直接算的那一份（`ScorecardCheck.tally`）逐格对拍，同 `verify-review` 的形状。
    ///
    /// **搬的是数与 wire 值，不是那句中文**：中文是渲染（ADR-0001），措辞一改闸门就红，
    /// 而闸门该守的是「这一格说的数对不对」（判据 24）。因此「选手 · 档」那一格
    /// **四样都上**：`data-player-source` 是四态的 wire 值（`tiered` / `no-tier` /
    /// `tier-unrecorded` / `unrecorded`，措辞怎么改都不动它）、`data-player-name` 与
    /// `data-player-tier` 是两半各自的字（前者与名牌逐字相同），`data-player` 是人读的那句话。
    let private scorecardHooks (row: ScorecardRow) = [
        prop.custom ("data-seat", string (Seat.index row.Seat))
        prop.custom ("data-player-source", ScorecardPlayer.toWire row.Player)
        // **身份与档位各上一格**：回放那一屏身份格与名牌上那一句逐字相同（两处画的都是
        // `start_game` 那一列 `names`），而档位那半段只有 Live 答得出。
        prop.custom ("data-player-name", ScorecardPlayer.nameSaid row.Player)
        prop.custom ("data-player-tier", ScorecardPlayer.tierSaid row.Player)
        prop.custom ("data-player", ScorecardPlayer.toDisplay row.Player)
        prop.custom ("data-juni", string row.Juni)
        prop.custom ("data-score", string row.Score)
        prop.custom ("data-hora", string row.Tally.Hora)
        // **`hora-targeted` 而不是 `houjuu`**：票 145 已经把 `Houjuu`（放铳）收进 `CONTEXT.md`，
        // 但那一次授权**只到词条本身**——把这个钩子连同 `SeatTally.HoraTargeted` 一起改名
        // 是另一张票的事（提案见 `DECISIONS.md` 145-2）。
        prop.custom ("data-hora-targeted", string row.Tally.HoraTargeted)
        prop.custom ("data-fallbacks", string row.Tally.Fallbacks)
        prop.custom ("data-retries", string row.Tally.Retries)
        prop.custom ("data-asked", string row.Tally.Asked)
        prop.custom ("data-input", string (Usage.promptTokens row.Tally.Usage))
        prop.custom ("data-output", string row.Tally.Usage.Output)
    ]

    /// 账单上那几笔**花了钱、没落子**的问话（票 108/110）：它们**不在这张表里**。
    ///
    /// 理由是那几次问话根本不在牌谱里（裁决 110：那笔账不进牌谱），而这张表的每一格
    /// 都是牌谱的聚合。于是四行相加会小于牌桌那条账单行——**差额要当场说出来**，
    /// 别让同一屏上两个 tok 数并排站着不解释（票 39）。回放那一侧恒是空表。
    /// **差额与那句话都在 `ScorecardView` 里算**（`voidedGap` / `voidedSaid`）：
    /// 摆在这里的话，「四行相加 ≤ 账单行」这条不变量就没有任何东西执行得了（判据 2）。
    let private scorecardVoids (table: Table) (rows: ScorecardRow list) =
        let counted = table |> Table.paidVoids |> List.length
        let gap = ScorecardView.voidedGap (Table.usage table) rows

        if counted = 0 then
            []
        else
            [
                Html.p [
                    prop.key "voids"
                    prop.className "intro"
                    prop.testId "table-scorecard-voids"
                    prop.custom ("data-void-asks", string counted)
                    prop.custom ("data-void-input", string (Usage.promptTokens gap))
                    prop.custom ("data-void-output", string gap.Output)
                    prop.text (ScorecardView.voidedSaid counted gap)
                ]
            ]

    /// 「复制记分卡」那一下的下场（票 133）。**不许静静地没复制上**（同票 78 那条）。
    let private scorecardNote (model: TableModel) =
        match model.ScorecardCopy with
        | None -> []
        | Some outcome ->
            let wire, said =
                match outcome with
                | Ok chars -> "copied", $"已复制（{chars} 字符）。"
                | Error reason -> "failed", $"记分卡没写进剪贴板：{reason}"

            [
                Html.p [
                    prop.key "copy-note"
                    prop.className "intro"
                    prop.testId "table-scorecard-note"
                    prop.custom ("data-scorecard-copy", wire)
                    prop.text said
                ]
            ]

    /// 终局记分卡：一席一行，四家逐列可比（票 133）。
    ///
    /// **还没终局时整块不在 DOM 里**（`TableState.scorecard` 那时是空表）：
    /// 一张空表比没有更糟——它声称有结论而每一格都是 0。
    let private scorecardPanel (model: TableModel) (dispatch: TableMsg -> unit) (table: Table) =
        match TableState.scorecard model table with
        | [] -> []
        | rows ->
            let head =
                Html.tr [
                    prop.key "head"
                    prop.children [
                        for header in ScorecardView.headers -> Html.th [ prop.key header; prop.text header ]
                    ]
                ]

            let body =
                rows
                |> List.map (fun row ->
                    Html.tr [
                        prop.key $"seat-{Seat.index row.Seat}"
                        prop.testId $"scorecard-{Seat.index row.Seat}"
                        yield! scorecardHooks row
                        // key 用列号：那几格的字重得厉害（四家的兜底都是「0」），
                        // 拿内容当 key 会撞。
                        prop.children (
                            ScorecardView.cells row
                            |> List.mapi (fun column cell -> Html.td [ prop.key (string column); prop.text cell ])
                        )
                    ])

            [
                Html.section [
                    prop.key "scorecard"
                    prop.className "settlement scorecard"
                    prop.testId "table-scorecard"
                    prop.custom ("data-rows", string (List.length rows))
                    prop.children (
                        [
                            Html.h3 [ prop.key "title"; prop.text "记分卡" ]
                            Html.table [
                                prop.key "table"
                                prop.children [
                                    Html.thead [ prop.key "thead"; prop.children [ head ] ]
                                    Html.tbody [ prop.key "tbody"; prop.children body ]
                                ]
                            ]
                        ]
                        @ scorecardVoids table rows
                        @ [
                            Html.button [
                                prop.key "copy"
                                prop.testId "table-scorecard-copy"
                                prop.onClick (fun _ -> dispatch ScorecardCopied)
                                prop.text "复制记分卡"
                            ]
                        ]
                        @ scorecardNote model
                    )
                ]
            ]

    // ---- 视图：危险度（票 25） ----

    /// 这一手能把谁的危险度摆出来：**只摆手牌本来就看得见的那家**。
    ///
    /// 危险度的候选牌就是那家的手牌，因此坐在座位上看时只显示自己那一手（显示别家的
    /// 等于把他的暗牌摊开）；上帝视角本来就全亮着，正在被问的那家都显示得了。
    let private dangerSeats (viewer: Seat option) (state: GameState) : Seat list =
        let asked = GameState.legalActions state |> List.map (fun choice -> choice.Seat)

        match viewer with
        | Some seat -> asked |> List.filter (fun other -> other = seat)
        | None -> asked

    /// 一家的危险度排序。**一个判据也不在这里算**：档位、名次与理由全是引擎的
    /// `Danger` 算好的，这里只排行（与 prompt 那一节同一份数）。
    let private dangerPanel (seat: Seat) (state: GameState) =
        let scaffold =
            DecisionPackage.forSeat seat state |> Option.bind DecisionPackage.scaffold

        match scaffold with
        | None -> []
        | Some scaffold ->
            let ranked =
                scaffold.Dahai
                |> List.choose (fun trial -> trial.Danger)
                |> List.sortBy (fun danger -> danger.Rank)

            if List.isEmpty ranked then
                []
            else
                let threats = scaffold.Threats |> List.map Threat.toDisplay |> String.concat "、"

                [
                    Html.section [
                        prop.key $"danger-{Seat.index seat}"
                        prop.className "settlement"
                        prop.testId $"table-danger-{Seat.index seat}"
                        prop.children [
                            Html.h3 $"座位 {Seat.index seat} 的危险度（有威胁的家：{threats}）"
                            Html.p [
                                prop.key "note"
                                prop.className "intro"
                                prop.text "现物 / 筋 / 壁 / 宝牌周边四条规则算出来的启发式，不是概率；排在前面的更安全，同级并列。"
                            ]
                            Html.div [
                                prop.key "ranking"
                                prop.children [
                                    for danger in ranked ->
                                        Html.p [
                                            prop.key (Tile.toMjai danger.Pai)
                                            prop.text $"第{danger.Rank}位 {Danger.toDisplay danger}"
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]

    /// 牌桌上的危险度：**默认关**，没人立直也没人副露时开了也没东西看
    /// （那时引擎本来就不给排序）。
    ///
    /// **裸奔档的真人坐在桌边时连拨都拨不出来**（票 89）：危险度是「要算才有的量」
    /// （术语表那条「感知 vs 计算」：现物与筋都得从河里推），拨得出来的话
    /// 「裸奔」这个对照组靠的就只是他自觉不按那一枚。判据不在这里：
    /// **辅助给不给只有 `TableState.assists` 一条**（面板上那一枚按钮读的也是它）。
    let private dangerPanels (model: TableModel) (table: Table) (viewer: Seat option) =
        if model.ShowDanger && TableState.assists model then
            dangerSeats viewer table.State
            |> List.collect (fun seat -> dangerPanel seat table.State)
        else
            []

    // ---- 视图：整页 ----

    /// 右轨那一块「状态」（票 123，主人裁的：「按照设计稿这些内容不应该塞右栏里面吗」；
    /// 设计稿 1a 的第三栏本来就叫状态）：**轮不轮到你 / 四席在做什么 / 上一手走了什么 / 账单**。
    ///
    /// 这几行从前排在 `tableBody` 的最前面，于是它们贴着中栏的左边线——
    /// 宽屏上离牌桌几百像素（实测 1920×1080 隔着 400 多），读起来与牌桌不像一件事。
    ///
    /// **一条判据都没搬**：这里仍旧只把 `TableState` 算好的那几样画出来，
    /// 视角那道闸门（`TableState.reveals`）与从前是同一个，`data-*` 一格没动。
    /// 牌桌没摆出来（还在拉 / 出了事）时它一行都不画：那两态由中栏那一格自己说。
    let internal statusRail (model: TableModel) : ReactElement list =
        match TableState.shown model with
        | Shown.Loading
        | Shown.Fault _ -> []
        | Shown.Board table ->
            // 兜底代打的那一手要看得出来（票 23）：不许静默替换。
            // `data-fallback` 给无头验收读（断电演习数的就是它）。
            let latest =
                match table.Latest with
                | None -> "还没走一手"
                | Some turn ->
                    let who = Action.actor turn.Action |> Seat.index

                    let mark =
                        match turn.Fallback with
                        | Some reason -> $"（兜底：{reason}）"
                        | None -> ""

                    $"上一手：座位 {who} {Action.toDisplay turn.Action}{mark}"

            // Agent 层那一行只属于 Live（票 71）：回放里没有在飞的问话，
            // 而那一局的选手是当时坐那一桌的人。后一行（token 账单）两种来源共用。
            //
            // **视角那道闸门从这里传进去**（票 81）：与气泡读的是同一个 `TableState.reveals`，
            // 视图这一层不再写第二份判据。
            let agent =
                TableState.live model
                |> Option.toList
                |> List.map (fun live ->
                    AgentLine.agentLine (TableState.reveals model) (TableState.seatNames model) live table)

            // 席位 = **记分板**（票 124，主人点的：「那个四行除了席位表还兼具记分板的作用吧」）。
            //
            // 四家的点数是拿来**互相比**的：谁领先、谁掉到三万以下、谁被击飞过一次，
            // 全是「四个数摆在一起才读得出」的事——而四张名牌散在牌桌四角，
            // 要比就得把视线在四个角之间来回甩。∴ 这一列纵向对齐、等宽数字。
            //
            // **它不是名牌的副本**：名牌答「这一席是谁、坐在屏幕的哪一边」（方位是它的要害），
            // 这一列答「四家此刻各有多少分」（对齐是它的要害）。两处的数同源
            // （`board.Seats` 的 `Score`，与名牌上那个 `data-score` 是同一个字段），
            // 闸门逐行核两处相等——对不上就是错。
            let roster =
                match Board.ofTable (TableState.viewpoint model) table with
                | None -> []
                | Some board ->
                    let names = TableState.nameplates model

                    [
                        Html.div [
                            prop.key "roster"
                            prop.className "roster"
                            prop.testId "table-roster"
                            // 四家合起来那一句（票 124 从状态行收过来）：
                            // 「四家都是均匀随机的选手」这类概括**是查阅用的**，
                            // 四行逐行写着的才是要看的。回放没有配桌，那时不挂。
                            match TableState.live model with
                            | Some live -> prop.title (SeatingPlan.botsToDisplay live.Seating)
                            | None -> prop.title ""
                            prop.children (
                                Html.div [ prop.key "title"; prop.className "rail-title"; prop.text "席位" ]
                                :: [
                                    for view in board.Seats do
                                        let index = Seat.index view.Seat

                                        let who = names |> List.tryItem index |> Option.defaultValue ""

                                        Html.div [
                                            prop.key index
                                            prop.className (
                                                if Some view.Seat = board.Viewer then
                                                    "roster-row roster-self"
                                                else
                                                    "roster-row"
                                            )
                                            prop.testId $"roster-{index}"
                                            prop.custom ("data-roster-seat", index)
                                            prop.custom ("data-roster-name", who)
                                            prop.custom ("data-roster-score", view.Score)
                                            prop.children [
                                                Html.span [
                                                    prop.key "kaze"
                                                    prop.className "roster-kaze"
                                                    prop.text (Kaze.toDisplay view.Jikaze)
                                                ]
                                                Html.span [
                                                    prop.key "who"
                                                    prop.className "roster-name"
                                                    prop.text who
                                                ]
                                                Html.span [
                                                    prop.key "score"
                                                    prop.className "roster-score"
                                                    prop.text (string view.Score)
                                                ]
                                            ]
                                        ]
                                ]
                            )
                        ]
                    ]

            // 真人坐席那一行（票 87）**排在最前面**：坐在桌边的人最先要知道的就是
            // 「轮不轮到我」与「平台刚才替我过了什么」。没有真人时它一行都不画。
            HumanLine.at model
            @ roster
            @ agent
            // 强 AI 基线那一行（票 92）：接在 Agent 那一行后面——它说的是**资产**，
            // 不是那一席在想什么（它不会说话）。这一桌没选它时一行都不画。
            @ BaselineLine.at model
            @ AgentLine.usageLine model table
            // 一条决策记录都没有的牌谱：**说一句为什么没有气泡**（票 76）。
            @ ThinkingBubble.note model
            @ [
                Html.p [
                    prop.key "latest"
                    prop.className "latest"
                    prop.testId "table-latest"
                    prop.custom ("data-fallback", AgentLine.fallenBack table.Latest)
                    prop.text latest
                ]
            ]

    let internal tableBody (model: TableModel) (dispatch: TableMsg -> unit) (table: Table) =
        // **读的是 `TableState.viewpoint` 而不是 `model.Viewpoint`**（票 87）：
        // 真人在座、对局还没打完时，这一屏锁死他自家那一席——于是他家的暗牌
        // **在拿到的数据里就不存在**（`MaskedSeat` 里没有手牌字段），而不是渲染时不画。
        match Board.ofTable (TableState.viewpoint model) table with
        | None -> Html.p [ prop.className "error"; prop.text "这个视角没有牌桌" ]
        | Some board ->
            let fault =
                table.Fault
                |> Option.toList
                |> List.map (fun message ->
                    Html.p [
                        prop.key "fault"
                        prop.className "error"
                        prop.testId "table-fault"
                        prop.text message
                    ])

            // 这一局结算了吗。**里宝牌摆不摆读的就是它**（票 76）：结算面板摆着的那一屏
            // 才是里宝牌真翻开的那一刻。
            let settled = Board.settlement table

            let settlement = settled |> Option.toList |> List.map settlementPanel

            let result = Board.final table |> Option.toList |> List.map resultPanel

            let danger = dangerPanels model table board.Viewer

            let seating: Seating = {
                Ruleset = GameState.ruleset table.State
                Viewer = board.Viewer
                Anchor = Board.anchor board
                Oya = (GameState.context table.State).Oya
                Teban = Board.teban board
                Nameplates = TableState.nameplates model
            }

            // 那几行状态字**搬去了右轨**（票 123，主人裁的：「按照设计稿这些内容
            // 不应该塞右栏里面吗」）。它们从前浮在牌桌左上角、贴着中栏的左边线，
            // 在宽屏上离牌桌几百像素——而设计稿 1a 的右轨本来就是「状态」那一栏。
            // **这一层因此只剩牌桌本身与紧贴它的那几块**（那一排响应按钮、辅助、结算）。
            // 那一块的装配在 `statusRail`。

            Html.div [
                prop.testId "table-board"
                prop.children (
                    [
                        // 真牌桌（票 44）：四家围着中央坐。**DOM 仍然按座位升序**（`seat-N` 那几个
                        // 钩子与既有闸门因此稳定），画到哪个方位由 `data-seat-position` 定——
                        // 它跟着观测视角转，而不是跟着座位号走。
                        Html.div [
                            prop.key "seats"
                            prop.className "seats-board"
                            prop.testId "table-seats"
                            prop.children (
                                // 气泡的取值器**按座位取**（票 76）：这里只取一次，四家各问一遍。
                                let bubbleAt = TableState.bubbles model table

                                // 真人那一席此刻能点哪几张（票 87）：**只问那一份决策包**，
                                // 其余三家恒是「一张都点不动」——能点的那几张与“轮到谁”是同一件事，
                                // 因此不存在“不轮到你却点得动”这种状态。
                                let humanTurn = TableState.humanTurn model

                                let playAt (seat: Seat) : Tile -> bool -> (int * string) option =
                                    match humanTurn with
                                    | Some package when HumanSeat.seat package = seat ->
                                        fun pai tsumogiri -> HumanSeat.dahai pai tsumogiri package
                                    | Some _
                                    | None -> fun _ _ -> None

                                // 牌在转的那一层（票 117）：四席同一份 DOM，绕盘心转四向。
                                [
                                    Html.div [
                                        prop.key "zones"
                                        prop.className "zones"
                                        prop.children (
                                            [
                                                for view in board.Seats ->
                                                    seatZone seating (playAt view.Seat) dispatch view
                                            ]
                                            @ [ tableCenter (Option.isSome settled) board ]
                                        )
                                    ]
                                ]
                                // 名牌与气泡在不转的那一层：屏幕前只有一个读者。
                                @ [
                                    Html.div [
                                        prop.key "plates"
                                        prop.className "plates"
                                        prop.children [
                                            for view in board.Seats ->
                                                seatPlate
                                                    seating
                                                    (ThinkingBubble.at dispatch view.Seat (bubbleAt view.Seat))
                                                    view
                                        ]
                                    ]
                                ]
                            )
                        ]
                    ]
                    // 真人那一排按钮（票 88）：**紧贴牌桌下沿**——自家手牌就在牌桌的下一排，
                    // 而「碰不碰」与「打哪张」是同一件事，两样东西不该隔着半屏。
                    // 不轮到他、或者这一手一条宣言都没有时它一行都不画。
                    @ HumanLine.calls model dispatch
                    // 新手辅助轮那一块（票 89）：**接在那一排按钮后面**——他要做的选择就在上面，
                    // 向听与危险度是为那一下服务的；裸奔档与不轮到他时一行都不画。
                    @ HumanLine.assist model
                    @ fault
                    // 气泡点开的那一手（票 76）：紧挨着牌桌——上面那张牌桌就是它说的那一刻。
                    @ ThinkingBubble.detail model dispatch
                    @ danger
                    @ settlement
                    @ result
                    // 终局记分卡（票 133）：紧接在精算那一句后面——那一句说的是顺位，
                    // 这张表说的是「这一场谁打得怎么样」，两者是同一个结论的两半。
                    @ scorecardPanel model dispatch table
                )
            ]
