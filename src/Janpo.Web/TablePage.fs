namespace Janpo.Web

open Elmish
open Fable.Core
open Feliz
open Feliz.UseElmish
open Thoth.Json.JavaScript
open Janpo

/// 在等哪一次问话的回执（票 23）。
///
/// **票号与播放控制的世代号是两件事**：那个管定时器，这个管在飞的请求。
/// 重开一桌之后旧回执才回来是常事（一次请求动辄几秒），而它的 id 是按另一份
/// 决策包编的号——拿它去 `tryAction` 会拿到一个语义完全不同的动作。
type Awaiting = {
    /// 这一次问话的票号。回执带的票号对不上就丢掉。
    Ticket: int
    /// 问的那一手的决策包。id 往回换动作（`tryAction`）与兜底（`Fallback.action`）都要它。
    Package: DecisionPackage
    /// 那个座位的配置（兜底策略按它的档位走）。
    Config: LlmSeat
}

/// Agent 层此刻处在哪一步。**页面上要看得见**：断电演习（故意配一把坏 key）时
/// 对局照样打得完，但不能静惄惄地打——人得知道模型早就不说话了。
[<RequireQualifiedAccess>]
type AgentStatus =
    /// 没有 LLM 座位，或者还没轮到它。
    | Idle
    /// 正在等这个座位的回执。
    | Asking of seat: Seat
    /// 上一次模型自己选出了动作。
    | Spoke of seat: Seat * reason: string option * latencyMs: int
    /// 上一次是兜底代打的。**粘着不掉**，直到模型又能好好说话为止。
    | Troubled of seat: Seat * reason: string

/// 牌桌页面的全部状态。**没有第二份牌局状态**（ADR-0002）：牌局在 `Table` 里，
/// 而 `Table` 里的局面是引擎的那一份；页面自己只多种子输入框的文本、播放控制、
/// 看哪一份投影，以及配桌与 Agent 层的那几样。
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
    /// 牌桌上要不要把危险度排序显示出来（票 25）。**默认关**：
    /// 它是围观者想看的东西，不是牌桌本来就该摆着的。
    ShowDanger: bool
    /// 哪个座位交给 LLM；None = 四家都是随机选手。
    LlmAt: Seat option
    /// 那个座位的配置（也就是配置面板里填的那份，同时落在 localStorage）。
    Llm: LlmSeat
    /// 在等回执吗。**等着的时候不续定时器**，否则牌桌会空转。
    Awaiting: Awaiting option
    /// 问话的票号，每问一次 +1。
    Ticket: int
    /// Agent 层的状态线。
    Agent: AgentStatus
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
    /// 开 / 关牌桌上的危险度排序（票 25）。
    | DangerToggled
    /// 这一局看完了，开下一局。
    | KyokuAdvanced
    /// 把哪个座位交给 LLM。
    | LlmSeatPicked of seat: Seat option
    /// 改配置面板里的一个字段。
    | LlmEdited of field: LlmField * value: string
    /// 把这一桌到此刻为止的牌谱存成一个 JSON 文件（票 26）。
    | Exported
    /// Agent 层的回执回来了。`ticket` 不是在等的那一张就丢掉（见 `Awaiting`）。
    ///
    /// **它不会不来**：超时与 provider 报错在 Agent 层都是值，最后也会变成一条回执
    /// （`Failure` 带着原因）——对局因此永不卡死。
    | Answered of ticket: int * answer: AgentAnswer

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

    /// 按这一桌的**配桌**开局：牌桌的规则集就是 `Roster` 里的那一份（CONTEXT.md 的 Roster），
    /// 因此不会出现「四家的配桌配上三麻的牌桌」。
    let private openTable (roster: Roster) (seedText: string) : Result<Table, string> =
        parseSeed seedText |> Result.bind (Table.start roster.Ruleset)

    /// 定时器：播着的时候续下一记，暂停时什么也不发。
    /// **世代号一并带上**——暂停期间在飞的那一记回来时会被 `Playback.accepts` 丢掉。
    let private schedule (playback: Playback) : Cmd<TableMsg> =
        if playback.Playing then
            Cmd.ofEffect (fun dispatch ->
                JS.setTimeout (fun () -> dispatch (Ticked playback.Generation)) (Speed.interval playback.Speed)
                |> ignore)
        else
            Cmd.none

    /// 把配置写进 localStorage。**走 Cmd 而不是在 update 里直接写**：MVU 的 update 是纯的，
    /// 副作用一律由 Cmd 发——顺带让页面逻辑的用例在 dotnet 上跑得起来（那边没有 localStorage）。
    let private save (write: unit -> unit) : Cmd<TableMsg> = Cmd.ofEffect (fun _ -> write ())

    /// 续一记定时器——**除非正在等回执**。等着的时候定时器只会把牌桌空转一遍；
    /// 那一手由 `Answered` 接着开动。
    let private tick (model: TableModel) (playback: Playback) : Cmd<TableMsg> =
        if Option.isSome model.Awaiting then
            Cmd.none
        else
            schedule playback

    /// 这一桌的配桌：一席交给 LLM（选了的话），其余随机。
    /// **推导出来而不存下来**：配置只有 `LlmAt` 与 `Llm` 这一份，不会与第二份对不上。
    let private rosterOf (model: TableModel) : Roster =
        Roster.withLlm model.Ruleset model.LlmAt model.Llm

    /// 导出文件的名字。**种子只有解析得出来才进文件名**：输入框里是人随手填的文本，
    /// 原样拼进文件名会把斜杠之类的东西带进去。
    let private exportName (seedText: string) : string =
        match parseSeed seedText with
        | Ok seed -> $"janpo-paifu-{seed}.json"
        | Error _ -> "janpo-paifu.json"

    /// 导出这一桌的牌谱。**编码在这里，落盘交给浏览器**（`Download`）：
    /// 本平台没有后端，文件是浏览器自己在本地生成的（ADR-0003）。
    /// 走 Cmd 而不是在 update 里直接写：副作用一律由 Cmd 发，update 保持纯的。
    let private exportCmd (roster: Roster) (fileName: string) (table: Table) : Cmd<TableMsg> =
        Cmd.ofEffect (fun _ ->
            Table.paifu roster table
            |> Paifu.encoder
            |> Encode.toString 0
            |> Download.json fileName)

    /// 发一次问话。**不用 `Cmd.OfPromise`**：它整段包在 `#if FABLE_COMPILER` 里，
    /// 而这个文件要在 dotnet 上编得过（页面逻辑的用例跑在那边）。
    /// 效果体只在浏览器里执行，dotnet 侧只把它编出来、不跑。
    let private askCmd (ticket: int) (request: AgentRequest) : Cmd<TableMsg> =
        Cmd.ofEffect (fun dispatch ->
            let answered (answer: AgentAnswer) = dispatch (Answered(ticket, answer))
            (Agent.ask request).``then`` answered |> ignore)

    /// 推进一手。**这就是驱动循环的一步**：问该出手那家要一个动作。
    /// 随机座位当场落子；LLM 座位发一个请求出去，这一手到 `Answered` 才落子。
    let private step (model: TableModel) : TableModel * Cmd<TableMsg> =
        match model.Awaiting, model.Table with
        // 上一次问话还没回来：不再问第二次（同一手会有两个请求在飞，而只有一个算数）。
        | Some _, _ -> model, Cmd.none
        | None, Error _ -> model, Cmd.none
        | None, Ok table ->
            match Table.decide (rosterOf model) table with
            | None -> model, Cmd.none
            | Some(Demand.Ready(action, players)) ->
                {
                    model with
                        Table = Ok(Table.apply action { table with Players = players })
                },
                Cmd.none
            | Some(Demand.Asked(package, config)) ->
                let ticket = model.Ticket + 1

                {
                    model with
                        Ticket = ticket
                        Awaiting =
                            Some {
                                Ticket = ticket
                                Package = package
                                Config = config
                            }
                        Agent = AgentStatus.Asking(DecisionPackage.seat package)
                },
                askCmd ticket {
                    Package = package
                    Seat = config
                    RetryLimit = Agent.retryLimit
                }

    /// 还推得动吗（这一局没终、也没出错）。
    let private canAdvance (model: TableModel) : bool =
        match model.Table with
        | Ok table -> Table.pending table |> Option.isSome
        | Error _ -> false

    /// 落完一手之后：接着播还是停下来。
    ///
    /// **等回执的那段不续定时器**（但仍然是 `Playing`）：定时器只会把牌桌空转一遍，
    /// 真正把它接着开动的是那条 `Answered`。一局终了也停下来：结算面板正摆在那里。
    let private resume (cmd: Cmd<TableMsg>) (model: TableModel) : TableModel * Cmd<TableMsg> =
        if Option.isSome model.Awaiting then
            model, cmd
        elif canAdvance model then
            model, Cmd.batch [ cmd; schedule model.Playback ]
        else
            {
                model with
                    Playback = Playback.pause model.Playback
            },
            cmd

    /// 回执 → 落子，并留下这一手的决策记录。**兜底就在这里**：id 换不回动作
    /// （模型没给、给的越界、超时、provider 报错）就由 `Fallback.action` 代打。
    ///
    /// **DecisionRecord 只在这一处组装**（票 26）：只有这里同时拿得到那一手的决策包、
    /// Agent 层的全部回执、最终落定的动作与「是不是兜底」。`turn` 是它在这一场里的手序号，
    /// 取自牌桌（`Table.Turns`）而不是记录数——随机座位的手同样占号。
    let private settle (awaiting: Awaiting) (answer: AgentAnswer) (table: Table) : Table * AgentStatus =
        let seat = DecisionPackage.seat awaiting.Package

        let action, fallback =
            match
                answer.ActionId
                |> Option.bind (fun id -> DecisionPackage.tryAction id awaiting.Package)
            with
            | Some action -> action, None
            | None ->
                // Agent 层没给原因就只能是「id 不在这一包里」：它自己校过一道，能走到这里
                // 说明两边对 id 的看法分了岔（例：回执延到了下一份包）。
                let reason =
                    answer.Failure |> Option.defaultValue $"模型给回的动作 id 不在这一包里（{answer.ActionId}）"

                Fallback.action awaiting.Config.Tier awaiting.Package, Some reason

        let record: DecisionRecord = {
            Turn = table.Turns
            Seat = seat
            // **只存尾部**（票 31）：前缀是事件流的派生物，而事件流就在同一份牌谱里。
            PromptTail = answer.PromptTail
            RenderVersion = answer.RenderVersion
            ActionIds = answer.ActionIds
            Output = answer.Output
            Reason = answer.Reason
            Thinking = answer.Thinking
            Attempts = answer.Attempts
            LatencyMs = answer.LatencyMs
            // 兜底代打挑的那条也取自这一包（`Fallback.action`），因此 id 恒找得回。
            Applied = DecisionPackage.tryId action awaiting.Package
            Fallback = fallback
            Usage = answer.Usage
        }

        // 那一手带来的前置：固定 preamble 与工具定义形状。**牌桌按「座位 + 渲染版本」去重**，
        // 因此整场只存一份（中途换了人格就多一份）。
        let prompting: Prompting = {
            Tools = answer.Tools
            Preambles = [
                {
                    Seat = seat
                    RenderVersion = answer.RenderVersion
                    Text = answer.Preamble
                }
            ]
        }

        let played = Table.applyRecorded record prompting action table

        match fallback with
        | None -> played, AgentStatus.Spoke(seat, answer.Reason, answer.LatencyMs)
        | Some reason -> played, AgentStatus.Troubled(seat, reason)

    /// 初次摆的那一桌，**配置从外面给**。拆出来是为了它是纯的：
    /// 读 localStorage 在 `init` 那一层，因此页面逻辑的用例（dotnet 侧）用得上这个入口。
    let initial (llmAt: Seat option) (config: LlmSeat) : TableModel * Cmd<TableMsg> =
        let ruleset = Ruleset.yonma
        let seedText = string defaultSeed

        {
            Ruleset = ruleset
            SeedText = seedText
            Table = openTable (Roster.withLlm ruleset llmAt config) seedText
            Playback = Playback.initial
            Viewpoint = Viewpoint.Seated Seat.first
            // 危险度默认关（票 25）。
            ShowDanger = false
            LlmAt = llmAt
            Llm = config
            Awaiting = None
            Ticket = 0
            Agent = AgentStatus.Idle
        },
        Cmd.none

    /// 页面初次打开。上一次填的配置（含 key）从 localStorage 里读回来。
    let init () : TableModel * Cmd<TableMsg> =
        initial (Store.readSeat Ruleset.yonma) (Store.readSeatConfig ())

    let update (message: TableMsg) (model: TableModel) : TableModel * Cmd<TableMsg> =
        match message with
        | SeedEdited seed -> { model with SeedText = seed }, Cmd.none
        | Restarted ->
            // 在飞的那一次问话作废：它的 id 是按旧那桌的决策包编的号。
            {
                model with
                    Table = openTable (rosterOf model) model.SeedText
                    Playback = Playback.pause model.Playback
                    Awaiting = None
                    Agent = AgentStatus.Idle
            },
            Cmd.none
        | Advanced ->
            {
                model with
                    Playback = Playback.pause model.Playback
            }
            |> step
        | PlayToggled ->
            let playback = Playback.toggle model.Playback
            { model with Playback = playback }, tick model playback
        | SpeedPicked speed ->
            let playback = Playback.setSpeed speed model.Playback
            { model with Playback = playback }, tick model playback
        | Ticked generation when not (Playback.accepts generation model.Playback) -> model, Cmd.none
        | Ticked _ ->
            let advanced, cmd = step model
            resume cmd advanced
        | ViewpointPicked viewpoint -> { model with Viewpoint = viewpoint }, Cmd.none
        | DangerToggled ->
            {
                model with
                    ShowDanger = not model.ShowDanger
            },
            Cmd.none
        | Exported ->
            match model.Table with
            // 牌桌都开不起来时没有牌谱可导（按钮那时也是灰的）。
            | Error _ -> model, Cmd.none
            | Ok table -> model, exportCmd (rosterOf model) (exportName model.SeedText) table
        | KyokuAdvanced ->
            {
                model with
                    Table = Result.map Table.nextKyoku model.Table
                    Awaiting = None
            },
            Cmd.none
        | LlmSeatPicked seat ->
            {
                model with
                    LlmAt = seat
                    Agent = AgentStatus.Idle
            },
            save (fun () -> Store.writeSeat seat)
        | LlmEdited(field, value) ->
            let config = LlmSeat.edit field value model.Llm
            { model with Llm = config }, save (fun () -> Store.writeSeatConfig config)
        | Answered(ticket, answer) ->
            match model.Awaiting, model.Table with
            | Some awaiting, Ok table when awaiting.Ticket = ticket ->
                let played, status = settle awaiting answer table

                {
                    model with
                        Table = Ok played
                        Awaiting = None
                        Agent = status
                }
                |> resume Cmd.none
            // 过期的回执（重开过一桌、开过下一局，或者票号对不上）：丢掉。
            | _ -> model, Cmd.none

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
                @ bou
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
                    // 牌谱随时导得出来，不必等终局：打到一半的事件流同样 fold 得回去。
                    button "table-export" (Result.isError model.Table) "导出牌谱" Exported dispatch
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
                    // 危险度（票 25）：围观者想看就拨开，**默认关**。
                    picker "table-danger" model.ShowDanger "危险度" DangerToggled dispatch
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
    let private dangerPanels (model: TableModel) (table: Table) (viewer: Seat option) =
        if model.ShowDanger then
            dangerSeats viewer table.State
            |> List.collect (fun seat -> dangerPanel seat table.State)
        else
            []

    // ---- 视图：配置面板 ----

    /// 一个文本输入框。`kind` 是 `password` 时人看不见自己填的 key。
    let private textField
        (testId: string)
        (kind: string)
        (label: string)
        (which: LlmField)
        (model: TableModel)
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
                    prop.value (LlmSeat.field which model.Llm)
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
        (model: TableModel)
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
                    prop.value (LlmSeat.field which model.Llm)
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
        (model: TableModel)
        (dispatch: TableMsg -> unit)
        =
        Html.label [
            prop.key testId
            prop.className "field"
            prop.children [
                Html.span [ prop.className "label"; prop.text label ]
                Html.select [
                    prop.testId testId
                    prop.value (LlmSeat.field which model.Llm)
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

    /// 配桌：哪个座位交给 LLM，以及那个座位的 provider / 模型 / key / 超时 / 思考预算。
    ///
    /// **key 只进 localStorage**（`Store`），不外发到本平台——本平台根本没有后端。
    /// **不提供订阅制登录**：pi-ai 的 OAuth 流程是 Node-only（票 18），浏览器里只有 API key
    /// 这一条路；Bedrock 同理不在 provider 列表里。
    ///
    /// **baseUrl 那一格只在选了自定义端点时出现**（票 30）：官方八家根本不看它，
    /// 摆在那里只会让人以为能把 DeepSeek 改道。
    let private llmPanel (model: TableModel) (dispatch: TableMsg -> unit) =
        let seats =
            picker "table-llm-none" (Option.isNone model.LlmAt) "无" (LlmSeatPicked None) dispatch
            :: [
                for seat in Seat.all model.Ruleset ->
                    picker
                        $"table-llm-{Seat.index seat}"
                        (model.LlmAt = Some seat)
                        $"座位 {Seat.index seat}"
                        (LlmSeatPicked(Some seat))
                        dispatch
            ]

        Html.section [
            prop.className "llm-panel"
            prop.testId "table-llm-panel"
            prop.children [
                Html.div [
                    prop.key "seat"
                    prop.className "controls"
                    prop.children (
                        Html.span [ prop.key "llm-label"; prop.className "label"; prop.text "模型坐席" ]
                        :: seats
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
                            model
                            dispatch
                        // 模型名一直是自由文本：本地模型叫 `qwen3:8b` 的叫 `gpt-oss-20b@q4` 的都有，
                        // 下拉框只会挡路。
                        textField "table-llm-model" "text" "模型" LlmField.Model model dispatch
                        if LlmSeat.isCustom model.Llm then
                            textField "table-llm-base-url" "text" "baseUrl" LlmField.BaseUrl model dispatch
                        textField "table-llm-key" "password" "API key" LlmField.ApiKey model dispatch
                        textField "table-llm-timeout" "number" "超时 (ms)" LlmField.TimeoutMs model dispatch
                        selectField
                            "table-llm-thinking"
                            "思考预算"
                            LlmField.Thinking
                            (Thinking.all
                             |> List.map (fun level -> Thinking.toWire level, Thinking.toDisplay level, true))
                            model
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
                            model
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
                            "人格"
                            "留空就没有人格。例：你是一位以防守见长的雀士，宁可少和一把，也不点炮。"
                            LlmField.Persona
                            model
                            dispatch
                        areaField
                            "table-llm-template"
                            "prompt 模板"
                            "留空就用默认模板。一段 JSON，给几项换几项：{\"id\":\"我的模板\",\"labels\":{\"history\":\"【战况回放】\"}}"
                            LlmField.Template
                            model
                            dispatch
                    ]
                ]
                Html.p [
                    prop.key "note"
                    prop.className "intro"
                    prop.text
                        "key 只存在这台浏览器的 localStorage 里，请求由浏览器直发 provider，不经本平台（它没有后端）。订阅制的 OAuth 登录在浏览器里用不了，只能填 API key。脚手架换成信息辅助后，prompt 里会多一节引擎算好的向听数、有效牌与逐张试打的进退向。人格与模板是另一个维度：它们只换措辞，不换给不给那几个算好的数。模型超时、报错或给不出合法动作时，重试两次仍不行就兜底代打（裸奔档摸切，信息辅助档打一张不退向听的），对局不会卡住。"
                ]
                // 自定义端点那段话只在选中它时出现：两个坑（CORS、mixed content）说清楚要一整段，
                // 而它们与官方八家无关。完整配法在 `docs/host/custom-endpoint.md`。
                if LlmSeat.isCustom model.Llm then
                    Html.p [
                        prop.key "custom-note"
                        prop.className "intro"
                        prop.testId "table-llm-custom-note"
                        prop.text
                            "自定义端点：baseUrl 填到含 /v1 那一层（Ollama 是 http://localhost:11434/v1，LM Studio 是 http://localhost:1234/v1），本地端点通常不用填 key，模型名照端点里的实名填。两个坑要先踩平：端点默认不放行浏览器跨域（Ollama 设 OLLAMA_ORIGINS，LM Studio 在设置里开 CORS），且 https 页面调 http 端点会被浏览器拦掉——配法见 docs/host/custom-endpoint.md。接不上时上面那行会直说「连不上自定义端点」，那不是模型不肯选。"
                    ]
            ]
        ]

    // ---- 视图：Agent 层的状态 ----

    /// 刚落定的那一手是不是兜底代打的（`data-*` 只能是字符串）。
    let private fallenBack (latest: Turn option) : string =
        match latest |> Option.bind (fun turn -> turn.Fallback) with
        | Some _ -> "true"
        | None -> "false"

    /// Agent 层此刻在干什么，以及这一桌兜底代打了几手。
    ///
    /// **断电演习看的就是这一行**：key 配坏了的时候对局照样打得完，但这里会一直红着
    /// 说模型怎么了。`data-agent` 给无头验收读。
    let private agentLine (model: TableModel) (table: Table) =
        let state, text =
            match model.Agent with
            | AgentStatus.Idle when Option.isNone model.LlmAt -> "idle", "四家都是随机选手"
            | AgentStatus.Idle -> "idle", "模型座位已就位，还没轮到它"
            | AgentStatus.Asking seat -> "asking", $"正在等座位 {Seat.index seat} 的模型回话……"
            | AgentStatus.Spoke(seat, reason, latency) ->
                let said =
                    match reason with
                    | Some reason -> $"：{reason}"
                    | None -> ""

                "spoke", $"座位 {Seat.index seat} 的模型选完了（{latency} ms）{said}"
            | AgentStatus.Troubled(seat, reason) -> "troubled", $"座位 {Seat.index seat} 兜底代打：{reason}"

        let fallbacks = Table.fallbacks table

        let tally = if fallbacks = 0 then "" else $"　这一桌已兜底 {fallbacks} 手"

        Html.p [
            prop.key "agent"
            prop.className (if state = "troubled" then "agent error" else "agent")
            prop.testId "table-agent"
            prop.custom ("data-agent", state)
            prop.custom ("data-fallbacks", string fallbacks)
            prop.text (text + tally)
        ]

    /// 这一桌的 token 账单（票 29b）。**「缓存真的命中了」在页面上看得见**：
    /// prompt 翻成「固定 preamble + append-only 历史 + 尾部现况」之后，
    /// 同一局里越往后打，命中的那一段越长。
    ///
    /// 一个 token 都还没花掉时不占位（四家随机选手的那一桌永远不长出这一行）。
    /// `data-*` 那几项给无头验收读。
    let private usageLine (table: Table) =
        let usage = Table.usage table

        if Usage.promptTokens usage = 0 then
            []
        else
            [
                Html.p [
                    prop.key "usage"
                    prop.className "agent"
                    prop.testId "table-usage"
                    prop.custom ("data-prompt-tokens", string (Usage.promptTokens usage))
                    prop.custom ("data-cache-read", string usage.CacheRead)
                    prop.custom ("data-cache-write", string usage.CacheWrite)
                    prop.custom ("data-cache-percent", string (Usage.cacheHitPercent usage))
                    prop.text ("这一桌累计：" + Usage.toDisplay usage)
                ]
            ]

    // ---- 视图：整页 ----

    let private tableBody (model: TableModel) (table: Table) =
        match Board.ofTable model.Viewpoint table with
        | None -> Html.p [ prop.className "error"; prop.text "这个视角没有牌桌" ]
        | Some board ->
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

            let danger = dangerPanels model table board.Viewer

            Html.div [
                prop.testId "table-board"
                prop.children (
                    [
                        boardHead board
                        Html.p [
                            prop.key "latest"
                            prop.className "latest"
                            prop.testId "table-latest"
                            prop.custom ("data-fallback", fallenBack table.Latest)
                            prop.text latest
                        ]
                        agentLine model table
                    ]
                    @ usageLine table
                    @ [
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
                    @ danger
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
                    prop.text "默认四家随机选手，可以把一席交给 LLM。牌桌上的一切都是引擎局面的投影：坐在某个座位上看时，他家的暗牌在类型层面就不存在；上帝视角是另一份独立投影。虚线的牌是摸切。"
                ]
                controls model dispatch
                viewpoints model dispatch
                llmPanel model dispatch
                match model.Table with
                | Error message -> Html.p [ prop.className "error"; prop.testId "table-error"; prop.text message ]
                | Ok table -> tableBody model table
            ]
        ]
