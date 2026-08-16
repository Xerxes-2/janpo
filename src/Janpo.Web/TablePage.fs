namespace Janpo.Web

open Elmish
open Fable.Core
open Feliz
open Feliz.UseElmish
open Janpo

/// 牌桌页面的全部状态。**没有第二份牌局状态**（ADR-0002）：牌局在 `Table` 里，
/// 而 `Table` 里的局面是引擎的那一份；页面自己只多三样东西——种子输入框的文本、
/// 播放控制、以及看哪一份投影。
type TableModel = {
    /// 这一桌的规则集。M1 只跑四麻默认预设，配桌是后面的票。
    Ruleset: Ruleset
    /// 输入框里的文本。**没解析过**——解析在「重开」那一步做，因此打字不会重开一桌。
    SeedText: string
    /// 牌桌；开不了局时是中文错误文案。
    Table: Result<Table, string>
    /// 播放控制。
    Playback: Playback
    /// 看哪一份投影。
    Viewpoint: Viewpoint
}

/// 牌桌上能发生的事。**一步一 Msg**：`Advanced` 与 `Ticked` 各推进一手，
/// 驱动循环就是 Elmish 的 update，页面里没有第二个 loop。
type TableMsg =
    /// 改种子输入框。
    | SeedEdited of seed: string
    /// 按当前种子重开一桌。
    | Restarted
    /// 单步：推进一手，并暂停。
    | Advanced
    /// 播 / 暂停。
    | PlayToggled
    /// 换倍速。
    | SpeedPicked of speed: Speed
    /// 定时器回来了。`generation` 不是当前世代就丢掉（见 `Playback.accepts`）。
    | Ticked of generation: int
    /// 换视角（坐到某个座位 / 上帝视角）。
    | ViewpointPicked of viewpoint: Viewpoint
    /// 这一局看完了，开下一局。
    | KyokuAdvanced

/// 牌桌页面：MVU 三件套加视图。
[<RequireQualifiedAccess>]
module TablePage =

    // ---- MVU ----

    /// 页面初次打开时摆的那一桌。挑它两个理由：东 1 局里既有碰吃也有杠（副露的形态看得到），
    /// 且以和了终（结算面板才有役种与番符可看）。挑种子的探针见报告 22。
    ///
    /// **刻意不用曳光弹那个种子**：曳光弹把原始 mjai 事件打在同一张文档里，
    /// 而 `start_kyoku` 带着四家配牌——两边同种子的话，牌桌遮起来的那几家手牌就在下面躺着。
    let private defaultSeed = 2088

    let private parseSeed (text: string) : Result<int, string> =
        match System.Int32.TryParse(text.Trim()) with
        | true, seed -> Ok seed
        | false, _ -> Error $"种子要是一个整数，得到「{text}」"

    let private openTable (ruleset: Ruleset) (seedText: string) : Result<Table, string> =
        parseSeed seedText |> Result.bind (Table.start ruleset)

    /// 定时器：播着的时候续下一记，暂停时什么也不发。
    /// **世代号一并带上**——暂停期间在飞的那一记回来时会被 `Playback.accepts` 丢掉。
    let private schedule (playback: Playback) : Cmd<TableMsg> =
        if playback.Playing then
            Cmd.ofEffect (fun dispatch ->
                JS.setTimeout (fun () -> dispatch (Ticked playback.Generation)) (Speed.interval playback.Speed)
                |> ignore)
        else
            Cmd.none

    /// 推进一手。**这就是驱动循环的一步**：`Table.advance` 问该出手那家要一个动作再落进引擎。
    let private advance (model: TableModel) : TableModel = {
        model with
            Table = Result.map Table.advance model.Table
    }

    /// 还推得动吗（这一局没终、也没出错）。
    let private canAdvance (model: TableModel) : bool =
        match model.Table with
        | Ok table -> Table.pending table |> Option.isSome
        | Error _ -> false

    let init () : TableModel * Cmd<TableMsg> =
        let ruleset = Ruleset.yonma
        let seedText = string defaultSeed

        {
            Ruleset = ruleset
            SeedText = seedText
            Table = openTable ruleset seedText
            Playback = Playback.initial
            Viewpoint = Viewpoint.Seated Seat.first
        },
        Cmd.none

    let update (message: TableMsg) (model: TableModel) : TableModel * Cmd<TableMsg> =
        match message with
        | SeedEdited seed -> { model with SeedText = seed }, Cmd.none
        | Restarted ->
            {
                model with
                    Table = openTable model.Ruleset model.SeedText
                    Playback = Playback.pause model.Playback
            },
            Cmd.none
        | Advanced ->
            {
                advance model with
                    Playback = Playback.pause model.Playback
            },
            Cmd.none
        | PlayToggled ->
            let playback = Playback.toggle model.Playback
            { model with Playback = playback }, schedule playback
        | SpeedPicked speed ->
            let playback = Playback.setSpeed speed model.Playback
            { model with Playback = playback }, schedule playback
        | Ticked generation when not (Playback.accepts generation model.Playback) -> model, Cmd.none
        | Ticked _ ->
            let advanced = advance model

            // 一局终了就自己停下来：结算面板正摆在那里，接着播会把它冲掉。
            if canAdvance advanced then
                advanced, schedule advanced.Playback
            else
                {
                    advanced with
                        Playback = Playback.pause advanced.Playback
                },
                Cmd.none
        | ViewpointPicked viewpoint -> { model with Viewpoint = viewpoint }, Cmd.none
        | KyokuAdvanced ->
            {
                model with
                    Table = Result.map Table.nextKyoku model.Table
            },
            Cmd.none

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

    /// 一张扣着的牌。**它没有 `data-pai`**——投影里压根没有那张牌，渲染层无从写起。
    let private back (key: int) =
        Html.span [ prop.key key; prop.className "tile back"; prop.text "背" ]

    let private faces (extra: string) (tiles: Tile list) =
        tiles |> List.mapi (fun index pai -> face index extra pai)

    // ---- 视图：一家 ----

    /// 暗牌摆成「手里的 + 刚摸进的那张单独摆在右边」。刚摸进那张**本来就在手牌里**
    /// （`RevealedSeat.Hand` 含它），这里只是把它拎出来摆开——摸切打的就是它。
    let private handTiles (hand: HandView) =
        match hand with
        | HandView.Concealed count -> [ for index in 1..count -> back index ]
        | HandView.Revealed(tiles, drawn) ->
            match drawn |> Option.bind (fun pai -> tiles |> List.tryFindIndex ((=) pai)) with
            | None -> faces "" tiles
            | Some index ->
                let held = List.take index tiles @ List.skip (index + 1) tiles
                faces "" held @ [ face (List.length held) " drawn" (List.item index tiles) ]

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

    /// 一组副露。**杠看得出形态**：四张一组，暗杠两端扣着（牌桌上就是这个摆法），
    /// 种类另有一枚中文标签（`NakiKind.toDisplay`）。
    let private nakiGroup (key: int) (naki: Naki) =
        let kind = Naki.kind naki

        let tiles =
            if kind = NakiKind.Ankan then
                Naki.tiles naki
                |> List.mapi (fun index pai ->
                    if index = 0 || index = 3 then
                        back index
                    else
                        face index "" pai)
            else
                faces "" (Naki.tiles naki)

        Html.span [
            prop.key key
            prop.className "naki"
            prop.custom ("data-naki", NakiKind.toDisplay kind)
            prop.children (
                Html.span [ prop.className "naki-kind"; prop.text (NakiKind.toDisplay kind) ]
                :: tiles
            )
        ]

    /// 一排牌：小标题加那几张。**张数写在标题里**——「各家手牌数」那条验收看的就是它，
    /// 而他家的张数来自投影（`MaskedSeat.HandCount`），不是渲染层拿副露数推的。
    let private tileRow (testId: string) (extra: string) (label: string) (tiles: ReactElement list) =
        Html.div [
            prop.key testId
            prop.className $"tiles {extra}"
            prop.testId testId
            prop.children (
                Html.span [ prop.key "label"; prop.className "row-label"; prop.text label ]
                :: tiles
            )
        ]

    /// 一家的牌与标记。`oya` 只用来画那枚「亲」标签。
    let private seatPanel (viewer: Seat option) (oya: Seat) (view: SeatView) =
        let index = Seat.index view.Seat

        let handCount =
            match view.Hand with
            | HandView.Revealed(hand, _) -> List.length hand
            | HandView.Concealed count -> count

        let marks =
            [
                if view.Seat = oya then
                    "亲"
                if Some view.Seat = viewer then
                    "视角"
                if RiichiState.isActive view.Riichi then
                    RiichiState.toDisplay view.Riichi
                if view.Ippatsu then
                    "一发"
            ]
            |> List.map (fun mark -> Html.span [ prop.key mark; prop.className "mark"; prop.text mark ])

        Html.section [
            prop.key index
            prop.className "seat"
            prop.testId $"seat-{index}"
            prop.children [
                Html.div [
                    prop.className "seat-head"
                    prop.children (
                        [
                            Html.span [
                                prop.key "name"
                                prop.className "seat-name"
                                prop.text $"座位 {index}・{Kaze.toDisplay view.Jikaze}家"
                            ]
                            Html.span [
                                prop.key "score"
                                prop.className "seat-score"
                                prop.testId $"seat-{index}-score"
                                prop.text (string view.Score)
                            ]
                            Html.span [ prop.key "junme"; prop.className "seat-junme"; prop.text $"第 {view.Junme} 巡" ]
                        ]
                        @ marks
                    )
                ]
                tileRow $"seat-{index}-hand" "hand" $"手牌 {handCount}" (handTiles view.Hand)
                tileRow $"seat-{index}-naki" "naki-row" "副露" (view.Naki |> List.mapi nakiGroup)
                tileRow $"seat-{index}-kawa" "kawa" $"河 {List.length view.Kawa}" (kawaTiles view.Kawa)
            ]
        ]

    // ---- 视图：场况与结算 ----

    let private field (testId: string) (label: string) (value: string) =
        Html.span [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.span [ prop.className "values"; prop.testId testId; prop.text value ]
            ]
        ]

    /// 一排牌，带标题与钩子。宝牌与里宝牌指示牌都是它。
    let private tileField (testId: string) (label: string) (tiles: Tile list) =
        Html.span [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.span [ prop.className "tiles"; prop.testId testId; prop.children (faces "" tiles) ]
            ]
        ]

    let private boardHead (board: BoardView) =
        let bou =
            Html.span [
                prop.key "bou"
                prop.className "field"
                prop.children [
                    Html.span [ prop.className "label"; prop.text "立直棒" ]
                    Html.span [
                        prop.className "bou-row"
                        prop.testId "table-bou"
                        prop.children [
                            for index in 1 .. board.Kyotaku -> Html.span [ prop.key index; prop.className "bou" ]
                        ]
                    ]
                ]
            ]

        // 里宝牌只有上帝视角有（坐着看时投影里就是空表）。
        let ura =
            if List.isEmpty board.UraMarkers then
                []
            else
                [ tileField "table-uradora" "里宝牌指示牌" board.UraMarkers ]

        Html.div [
            prop.className "board-head"
            prop.children (
                [
                    field "table-kyoku" "场况" $"{Kaze.toDisplay board.Bakaze}{board.Kyoku}局 {board.Honba} 本场"
                    field "table-kyotaku" "供托" $"{board.Kyotaku} 根"
                    field "table-wall" "剩余摸牌" $"{board.WallRemaining} 张"
                    tileField "table-dora" "宝牌指示牌" board.DoraMarkers
                ]
                @ ura
                @ [ bou ]
            )
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
                        prop.text (if settlement.Renchan then "亲连庄" else "亲流局，进下一局")
                    ]
                ]
            )
        ]

    let private resultPanel (result: GameResult) =
        Html.section [
            prop.key "result"
            prop.className "settlement"
            prop.testId "table-result"
            prop.children [ Html.h3 "终局精算"; Html.p (GameResult.toDisplay result) ]
        ]

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

    let private controls (model: TableModel) (dispatch: TableMsg -> unit) =
        let running = canAdvance model

        let ended =
            match model.Table with
            | Ok table -> Table.isKyokuEnded table && (Table.result table |> Option.isNone)
            | Error _ -> false

        let speeds =
            Speed.all
            |> List.map (fun speed ->
                picker
                    $"table-speed-{Speed.toDisplay speed}"
                    (model.Playback.Speed = speed)
                    (Speed.toDisplay speed)
                    (SpeedPicked speed)
                    dispatch)

        Html.div [
            prop.className "controls"
            prop.children (
                [
                    button
                        "table-play"
                        (not running)
                        (if model.Playback.Playing then "暂停" else "播放")
                        PlayToggled
                        dispatch
                    button "table-step" (not running) "单步" Advanced dispatch
                    button "table-next" (not ended) "下一局" KyokuAdvanced dispatch
                    Html.span [ prop.key "speed-label"; prop.className "label"; prop.text "倍速" ]
                ]
                @ speeds
            )
        ]

    let private viewpoints (model: TableModel) (dispatch: TableMsg -> unit) =
        let seats =
            Seat.all model.Ruleset
            |> List.map (fun seat ->
                picker
                    $"table-view-{Seat.index seat}"
                    (model.Viewpoint = Viewpoint.Seated seat)
                    $"座位 {Seat.index seat}"
                    (ViewpointPicked(Viewpoint.Seated seat))
                    dispatch)

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
                    Html.span [ prop.key "seed-label"; prop.className "label"; prop.text "种子" ]
                    Html.input [
                        prop.key "seed-input"
                        prop.testId "table-seed"
                        prop.value model.SeedText
                        prop.onChange (SeedEdited >> dispatch)
                    ]
                    button "table-restart" false "重开" Restarted dispatch
                ]
            )
        ]

    // ---- 视图：整页 ----

    let private tableBody (model: TableModel) (table: Table) =
        match Board.ofState model.Viewpoint table.State with
        | None -> Html.p [ prop.className "error"; prop.text "这个视角没有牌桌" ]
        | Some board ->
            let latest =
                match table.Latest with
                | None -> "还没走一手"
                | Some action -> $"上一手：座位 {Seat.index (Action.actor action)} {Action.toDisplay action}"

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

            let settlement = Board.settlement table |> Option.toList |> List.map settlementPanel

            let result = Table.result table |> Option.toList |> List.map resultPanel

            Html.div [
                prop.testId "table-board"
                prop.children (
                    [
                        boardHead board
                        Html.p [
                            prop.key "latest"
                            prop.className "latest"
                            prop.testId "table-latest"
                            prop.text latest
                        ]
                        Html.div [
                            prop.key "seats"
                            prop.className "seats-board"
                            prop.children [
                                for view in board.Seats ->
                                    seatPanel board.Viewer (GameState.context table.State).Oya view
                            ]
                        ]
                    ]
                    @ fault
                    @ settlement
                    @ result
                )
            ]

    [<ReactComponent>]
    let Page () =
        let model, dispatch = React.useElmish (init, update, [||])

        Html.div [
            prop.className "page table-page"
            prop.children [
                Html.h1 "janpo —— 最小牌桌"
                Html.p [
                    prop.className "intro"
                    prop.text "四家都是随机选手。牌桌上的一切都是引擎局面的投影：坐在某个座位上看时，他家的暗牌在类型层面就不存在；上帝视角是另一份独立投影。虚线的牌是摸切。"
                ]
                controls model dispatch
                viewpoints model dispatch
                match model.Table with
                | Error message -> Html.p [ prop.className "error"; prop.testId "table-error"; prop.text message ]
                | Ok table -> tableBody model table
            ]
        ]
