namespace Janpo.Web.Tests

open System
open System.IO
open Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// 思考气泡（票 76；CONTEXT.md 的 `Thinking Bubble`）：**展示某个 `DecisionRecord` 的 UI 部件**。
///
/// 这一层钉的是**取值器**（`TableState.bubbles : TableModel -> Table -> Seat -> Bubble option`）
/// 与**全文面板**（`TableState.detail`）的判据，五件事：
///
/// 1. 三态各是什么、一眼分不分得开（在想 / 说了什么 / 兜底代打）；
/// 2. **数据源只有 `Table.Decisions` 一处**：改那条记录，气泡跟着变；
/// 3. **按座位取**：bot 席没有气泡，四席都有记录时四个气泡都在；
/// 4. Live 与回放**两边都要**：回放沿游标取那一手，Live 里点历史某一手走
///    「导成牌谱 → `Table.replay` → 取那一帧」（只读，`live.Table` 一手都不退回去）；
/// 5. **可见性判据挂在对局配置与终局状态上**（ADR-0003 的 consequence），不挂在「谁在看」上。
///
/// 画出来长什么样（气泡挡不挡得住牌、三态看不看得出区别）在浏览器那一侧
/// （`web/scripts/verify-bubbles.mjs`）——这里一行 Feliz 都不 open。
module ThinkingBubbleTests =

    // ---- 语料：首页那份 Demo（一条决策记录都没有，因此阳性对照要自己拌进去） ----

    let private assetPath = Path.Combine(AppContext.BaseDirectory, "demo-paifu.json")

    let private demo: Paifu =
        match Decode.fromString Paifu.decoder (File.ReadAllText assetPath) with
        | Ok paifu -> paifu
        | Error message -> failwith $"首页那份 Demo 牌谱读不动（{assetPath}）：{message}"

    let private seat (index: int) : Seat =
        match Seat.ofIndex index with
        | Some seat -> seat
        | None -> failwith $"{index} 应当是合法座位"

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    let private shownTable (model: TableModel) : Table =
        match TablePage.shown model with
        | Shown.Board table -> table
        | other -> failwith $"这一刻该有一桌，却是 {other}"

    let private timelineOf (model: TableModel) : Timeline =
        match TablePage.timeline model with
        | Some timeline -> timeline
        | None -> failwith "回放这一刻该有一根时间轴"

    /// 这一席此刻的气泡。**用例与视图读的是同一个取值器**（拄一份同样的推导只会漂）。
    let private bubbleAt (index: int) (model: TableModel) : Bubble option =
        TablePage.bubbles model (shownTable model) (seat index)

    /// 一条决策记录：手序与座位由用例给，其余是一份看得出来路的样板。
    let private recorded (turn: int) (index: int) : DecisionRecord =
        {
            Turn = turn
            Seat = seat index
            PromptTail = $"【现在】东1局 0 本场……\n【可选动作】只能从下面这些 id 里选一个：\n- id=0：摸切1索（第 {turn} 手）"
            RenderVersion = "janpo-default@aaaaaaaa.bbbbbbbb"
            ActionIds = [ 0; 1 ]
            Output = """{"stop_reason":"toolUse"}"""
            Reason = Some $"第 {turn} 手的一句话理由（座位 {index}）"
            Thinking = Some $"第 {turn} 手的思考原文（座位 {index}）"
            Attempts = 1
            LatencyMs = 640 + turn
            Applied = Some 0
            Fallback = None
            Usage =
                Some
                    {
                        Input = 812
                        Output = 96
                        CacheRead = 1344
                        CacheWrite = 0
                    }
        }

    /// 首页那一屏，拌进这几条记录（浏览器里这一步是 `fetch` 回来的那份牌谱）。
    let private loadedWith (records: DecisionRecord list) : TableModel =
        TablePage.home ()
        |> fst
        |> step (DemoLoaded(Ok { demo with Decisions = records }))

    /// 四席各说过一手：手序连着四手，因此第 10 手落定那一帧上四条都看得见。
    let private fourSeats = [ for index in 0..3 -> recorded (7 + index) index ]

    /// 第 `turn` 手落定之后那一帧（帧号 = 手数 + 局数，第 0 帧是开局）。
    let private atTurn (turn: int) (model: TableModel) : TableModel = model |> step (CursorMoved(turn + 1))

    // ---- 没有记录就不出气泡 ----

    [<Fact>]
    let ``牌谱里一条决策记录都没有：四席一个气泡都不出，而且页面上说得出为什么`` () =
        // 首页那份 Demo 从票 79 起是**真的四席对局**，自带几百条决策记录；
        // 这一条要的是「一条记录都没有」的那一份，因此它把记录换成空的（`loadedWith []`）。
        // **这一句是那一步的阳性对照**：资产本身要是空的，`loadedWith []` 就什么都没换，
        // 下面整段也就证不了「没记录就不出气泡」。
        Assert.NotEmpty demo.Decisions

        let model = loadedWith [] |> step (CursorMoved 60)

        for index in 0..3 do
            Assert.Equal(None, bubbleAt index model)

        // 页面上那句话的判据（「这份分享不含推理」）。**判据落在整份牌谱上**：
        // 带推理的牌谱在第 0 帧同样一条记录都没有，拿帧当判据的话那句话会在开局闪一下。
        Assert.True(TablePage.recordless model, "一条记录都没有的牌谱该说清楚为什么没有气泡")
        Assert.False(TablePage.recordless (loadedWith fourSeats |> step (CursorMoved 0)))

    [<Fact>]
    let ``Live 那一桌不说那句话：模型随时可能开口`` () =
        // 「这份牌谱不含推理」是回放才成立的话。Live 那一侧四家都是 bot 时，
        // 说话的是 Agent 那一行（「四家都是均匀随机的选手」）。
        let live =
            TablePage.initial RulesetDraft.initial (SeatingPlan.initial Ruleset.yonma)
            |> fst

        Assert.False(TablePage.recordless live)

    // ---- 三态 ----

    [<Fact>]
    let ``说了什么：thinking 优先，没有思考预算时退回那一句理由`` () =
        let full = recorded 7 0
        let noThinking = { full with Thinking = None }

        let mute =
            { full with
                Thinking = None
                Reason = None
            }

        let saidAt (record: DecisionRecord) =
            loadedWith [ record ]
            |> atTurn record.Turn
            |> bubbleAt 0
            |> Option.map Bubble.toDisplay

        Assert.Equal(Some "第 7 手的思考原文（座位 0）", saidAt full)
        Assert.Equal(Some "第 7 手的一句话理由（座位 0）", saidAt noThinking)
        // 两样都没有时也要说一句：空气泡与「没有气泡」在页面上分不出来。
        Assert.Equal(Some "（这一手没留下理由与思考原文）", saidAt mute)

    [<Fact>]
    let ``气泡里的字来自那一手的决策记录：改一个字，气泡跟着变`` () =
        let record = recorded 7 0

        let edited =
            { record with
                Thinking = Some "改过的思考原文"
            }

        let saidAt (record: DecisionRecord) =
            loadedWith [ record ] |> atTurn record.Turn |> bubbleAt 0

        Assert.Equal(Some(Bubble.Spoke record), saidAt record)
        Assert.Equal(Some(Bubble.Spoke edited), saidAt edited)
        Assert.NotEqual(saidAt record |> Option.map Bubble.toDisplay, saidAt edited |> Option.map Bubble.toDisplay)

    [<Fact>]
    let ``兜底那一手：气泡是兜底态、写着原因，与 data-fallback 同源`` () =
        let reason = "模型超时（60001 ms 没答完）（重试 2 次仍无结果）"

        let record =
            { recorded 7 0 with
                Fallback = Some reason
                Reason = None
            }

        let model = loadedWith [ record ] |> atTurn record.Turn

        Assert.Equal(Some(Bubble.Troubled(record, reason)), bubbleAt 0 model)
        Assert.Equal(Some reason, bubbleAt 0 model |> Option.map Bubble.toDisplay)
        Assert.Equal("troubled", bubbleAt 0 model |> Option.map Bubble.toWire |> Option.defaultValue "")

        // 牌桌上那句「上一手：……（兜底：……）」与 `data-fallback` 读的是同一条记录的同一格。
        Assert.Equal(record.Fallback, (shownTable model).Latest |> Option.bind (fun turn -> turn.Fallback))

    [<Fact>]
    let ``三态给机器看的那一半各不相同：一眼分得开的另一半`` () =
        let spoke = recorded 7 0
        let troubled = { spoke with Fallback = Some "端点连不上" }

        Assert.Equal<string list>(
            [ "thinking"; "spoke"; "troubled" ],
            [
                Bubble.Thinking(0, 240)
                Bubble.Spoke spoke
                Bubble.Troubled(troubled, "端点连不上")
            ]
            |> List.map Bubble.toWire
        )

    // ---- 按座位取 ----

    [<Fact>]
    let ``四席都有记录时四个气泡都在，各说各的那一条`` () =
        let model = loadedWith fourSeats |> atTurn 10

        for index in 0..3 do
            Assert.Equal(Some(Bubble.Spoke(recorded (7 + index) index)), bubbleAt index model)

    [<Fact>]
    let ``一席一条：同一席说了两次，气泡上是新的那一条`` () =
        let older = recorded 7 0
        let newer = recorded 9 0
        let model = loadedWith [ older; newer ] |> atTurn 9

        Assert.Equal(Some(Bubble.Spoke newer), bubbleAt 0 model)
        // 别人还是没有（这份牌谱里只有座位 0 说过话）。
        for index in 1..3 do
            Assert.Equal(None, bubbleAt index model)

    // ---- 在想（Agent 层那一份，票 74 只换取值器的实现） ----

    /// 座位 0 交给模型的一桌（开局第一手就轮到它：亲摸完牌等着打）。
    ///
    /// 票 73 之后「哪一席交给模型」不再是 `Seat option` + 一份配置，而是**档案库 + 座位绑定**：
    /// 库里摆一份档案，座位 0 引用它。
    let private llmTable () : TableModel =
        let profile =
            { ModelProfile.initial with
                ApiKey = "sk-测试用的假 key"
            }

        let seating =
            { SeatingPlan.initial Ruleset.yonma with
                Profiles = [ profile ]
            }
            |> SeatingPlan.bind Seat.first (SeatChoice.Profile profile.Name)

        TablePage.initial RulesetDraft.initial seating |> fst

    let private liveOf (model: TableModel) : LiveTable =
        match TablePage.live model with
        | Some live -> live
        | None -> failwith "这几条用例跑的是 `?table=1` 那一页，它必然是 Live"

    let private awaitingOf (model: TableModel) : Awaiting =
        match (liveOf model).Awaiting with
        | [ awaiting ] -> awaiting
        | [] -> failwith "这一手应当在等 Agent 层的回执"
        | many -> failwith $"这几条用例只该有一份在飞的问话，却有 {List.length many} 份"

    /// 模型好好答话。
    let private chose: AgentAnswer =
        {
            ActionId = Some 0
            Reason = Some "就它了"
            Failure = None
            Attempts = 2
            LatencyMs = 640
            PromptTail = "【现在】东1局 0 本场……\n【可选动作】只能从下面这些 id 里选一个：\n- id=0：摸切1索"
            Preamble = "你在打日本立直麻将（天凤规则，四人东）……"
            RenderVersion = "janpo-default@08fcaec3.4b9e57c0"
            Tools = """[{"name":"choose_action"}]"""
            ActionIds = [ 0; 1 ]
            Output = """{"stop_reason":"toolUse"}"""
            Thinking = Some "先数向听……"
            Usage =
                Some
                    {
                        Input = 812
                        Output = 96
                        CacheRead = 1344
                        CacheWrite = 0
                    }
        }

    /// 推到模型被问到那一手就停（还没答话）。
    let private askedOnce (model: TableModel) : TableModel =
        let rec loop (left: int) (model: TableModel) =
            match (liveOf model).Awaiting with
            | _ :: _ -> model
            | [] when left <= 0 -> failwith "这一段里模型应当被问到一次"
            | [] -> loop (left - 1) (model |> step Advanced)

        loop 200 model

    [<Fact>]
    let ``在想那一态是按座位取的：正在等谁的回执，谁头上就是它`` () =
        let asked = llmTable () |> askedOnce

        Assert.Equal(Seat.first, DecisionPackage.seat (awaitingOf asked).Package)
        // 带着已等秒数与上限（票 74）：刚问出去是 0 秒，上限 = 档案超时 240000 ms → 240 秒。
        Assert.Equal(Some(Bubble.Thinking(0, 240)), bubbleAt 0 asked)

        // 其余三席是自带 bot：**一条记录都没有，因此一个气泡都没有**。
        for index in 1..3 do
            Assert.Equal(None, bubbleAt index asked)

    [<Fact>]
    let ``在想压过上一条记录：这一席上一手说过话也一样`` () =
        let spoke = llmTable () |> askedOnce

        let answered =
            spoke
            |> step (Answered(Awaiting.seat (awaitingOf spoke), (awaitingOf spoke).Ticket, chose))

        match bubbleAt 0 answered with
        | Some(Bubble.Spoke record) -> Assert.Equal(Some "先数向听……", record.Thinking)
        | other -> failwith $"答过话之后该是「说了什么」，却是 {other}"

        // 再问一次：旧的理由已经不是它此刻在想的事了。
        Assert.Equal(Some(Bubble.Thinking(0, 240)), bubbleAt 0 (answered |> askedOnce))

    // ---- 回放：沿游标取那一手 ----

    [<Fact>]
    let ``回放里拖动游标：气泡跟着换成那一手的记录，拖到还没有记录的那几帧就消失`` () =
        let model = loadedWith fourSeats

        // 第 7 手落定之前，四席一个都没有。
        for index in 0..3 do
            Assert.Equal(None, bubbleAt index (model |> atTurn 6))

        // 一手一手往后拖：每落定一手，那一席就多一个气泡（而且只多那一席的）。
        for index in 0..3 do
            let at = model |> atTurn (7 + index)
            Assert.Equal(Some(Bubble.Spoke(recorded (7 + index) index)), bubbleAt index at)

            for later in (index + 1) .. 3 do
                Assert.Equal(None, bubbleAt later at)

        // 拖回开局：那几条记录在这一帧上根本不存在（票 71 的 `recordedBy` 切的）。
        let head = model |> step (CursorMoved 0)
        Assert.Empty (shownTable head).Decisions

        for index in 0..3 do
            Assert.Equal(None, bubbleAt index head)

    // ---- 可见性判据（ADR-0003 的 consequence） ----

    [<Fact>]
    let ``可见性判据不看谁在看：五个视角下四席的气泡逐个相同`` () =
        // ADR-0003：「围观者」不是权限级别，只是视角；因此 UI 的可见性规则挂在
        // **对局配置与终局状态**上。切视角改不动气泡这件事，就是那条 consequence 的执行体。
        let model = loadedWith fourSeats |> atTurn 10
        let table = shownTable model

        let bubblesUnder (viewpoint: Viewpoint) =
            let model = model |> step (ViewpointPicked viewpoint)
            [ for index in 0..3 -> TablePage.bubbles model table (seat index) ]

        let god = bubblesUnder Viewpoint.God
        Assert.Equal(4, god |> List.sumBy (fun bubble -> if Option.isSome bubble then 1 else 0))

        for index in 0..3 do
            Assert.Equal<Bubble option list>(god, bubblesUnder (Viewpoint.Seated(seat index)))

    // ---- 全文面板（story 5） ----

    [<Fact>]
    let ``点开气泡：全文面板给的是那一手的记录与当时的局面快照`` () =
        let model = loadedWith fourSeats |> atTurn 10
        Assert.True(Option.isNone (TablePage.detail model), "没点开之前没有全文面板")

        let opened = model |> step (RecordOpened(Some 8))

        match TablePage.detail opened with
        | None -> failwith "点开第 8 手之后该有一份全文面板"
        | Some detail ->
            // 那一手的记录（全文面板要的九样都在这条记录与那一帧上）。
            Assert.Equal(recorded 8 1, detail.Record)
            Assert.Equal(Some "第 8 手的思考原文（座位 1）", detail.Record.Thinking)
            Assert.Equal(Some "第 8 手的一句话理由（座位 1）", detail.Record.Reason)
            Assert.Equal<int list>([ 0; 1 ], detail.Record.ActionIds)
            Assert.Equal("janpo-default@aaaaaaaa.bbbbbbbb", detail.Record.RenderVersion)

            // 局面快照：**那一手落定之后的那一帧**，因此「最终落定的动作」就在它的 `Latest` 上
            // （牌谱里存的是包内 id，动作本身不上牌谱——26-3）。
            Assert.Equal(9, detail.Snapshot.Turns)
            Assert.True(Option.isSome detail.Snapshot.Latest, "快照那一帧该刚落定一手")

    /// 第一局最后那一手的手序。**它是跨局边界那一条的语料**：下一局的开局帧手数**沿用着上一局**
    /// （票 75 的红-7 就是这一条），因此「第 N 手落定之后那一帧」在这里有两个候选，
    /// 而只有前一个真落定了一手。
    let private lastTurnOfFirstKyoku: int =
        match Table.replay demo with
        | Error reason -> failwith $"首页那份 Demo 应当回放得动，却得到「{reason}」"
        | Ok frames ->
            let openings =
                frames
                |> List.indexed
                |> List.filter (fun (_, frame) -> Option.isNone frame.Latest)
                |> List.map fst

            match openings with
            | _ :: second :: _ -> (List.item (second - 1) frames).Turns - 1
            | _ -> failwith "这份 Demo 该有两局以上"

    [<Fact>]
    let ``跨局边界也点得开：快照是那一手落定之后那一帧，不是下一局的开局帧`` () =
        // 下一局的开局帧手数沿用着上一局，因此「手数 = turn + 1」在局边界上认得出两帧；
        // 摊开的必须是**真落定了那一手**的那一帧（`recordOf` 在开局帧上恒是 None）。
        let turn = lastTurnOfFirstKyoku
        let record = recorded turn 0
        let opened = loadedWith [ record ] |> atTurn turn |> step (RecordOpened(Some turn))

        match TablePage.detail opened with
        | None -> failwith $"第 {turn} 手（第一局最后一手）该摊得开"
        | Some detail ->
            Assert.Equal(record, detail.Record)
            Assert.Equal(turn + 1, detail.Snapshot.Turns)
            Assert.True(Option.isSome detail.Snapshot.Latest, "摊开的是下一局的开局帧，不是那一手落定之后那一帧")
            Assert.True(GameState.isEnded detail.Snapshot.State, "第一局最后一手落定之后，那一局就终了了")

    [<Fact>]
    let ``回放里点开某一手：游标跟着挪到那一帧，轴只有一根`` () =
        let model = loadedWith fourSeats |> atTurn 10
        Assert.Equal(11, (timelineOf model).Cursor)

        let opened = model |> step (RecordOpened(Some 8))

        // 牌桌上摆的就是那一手的快照，而时间轴上的游标跟着挪过去了（不另开第二根轴）。
        Assert.Equal(9, (timelineOf opened).Cursor)
        Assert.Equal(9, (shownTable opened).Turns)
        Assert.Equal(Some 8, (timelineOf opened).Record |> Option.map (fun record -> record.Turn))

        // 收起来：面板没了，牌桌仍停在那一帧（收起不是「跳回去」）。
        let closed = opened |> step (RecordOpened None)
        Assert.True(Option.isNone (TablePage.detail closed))
        Assert.Equal(9, (timelineOf closed).Cursor)

    [<Fact>]
    let ``拖一下时间轴就把全文面板收起来：牌桌走了，面板不许留在原地`` () =
        let opened = loadedWith fourSeats |> atTurn 10 |> step (RecordOpened(Some 8))
        Assert.True(Option.isSome (TablePage.detail opened))

        Assert.True(Option.isNone (TablePage.detail (opened |> step (CursorMoved 40))))
        Assert.True(Option.isNone (TablePage.detail (opened |> step PlayToggled)))
        Assert.True(Option.isNone (TablePage.detail (opened |> step Restarted)))

    [<Fact>]
    let ``Live 里点历史某一手：牌桌摆的是当时的快照，而这一桌一手都没退回去`` () =
        // story 5 在 Live 那一侧走的是「导成牌谱 → `Table.replay` → 取那一帧」这条现成路
        // （票面明令：**不要在 Live 侧常驻一份帧数组**）。
        let rec play (left: int) (model: TableModel) =
            if left <= 0 then
                model
            else
                let asked = model |> askedOnce

                play
                    (left - 1)
                    (asked
                     |> step (Answered(Awaiting.seat (awaitingOf asked), (awaitingOf asked).Ticket, chose)))

        let played = llmTable () |> play 3
        let live = shownTable played
        let record = List.head live.Decisions

        Assert.True(live.Turns > record.Turn + 1, "这一桌该已经走过那一手了")

        let opened = played |> step (RecordOpened(Some record.Turn))

        match TablePage.detail opened with
        | None -> failwith "Live 里点历史某一手该摊得开"
        | Some detail ->
            Assert.Equal(record, detail.Record)
            Assert.Equal(record.Turn + 1, detail.Snapshot.Turns)
            // 牌桌上摆的就是那一刻（`shown` 两种来源共用一个出口）。
            Assert.Equal(record.Turn + 1, (shownTable opened).Turns)

        // **只读**：这一桌自己一手都没退回去，牌谱与事件流一字未动。
        let stillLive =
            match (liveOf opened).Table with
            | Ok table -> table
            | Error error -> failwith $"这一桌应当还开着，却得到「{error}」"

        Assert.Equal(live.Turns, stillLive.Turns)
        Assert.Equal<Event list>(GameState.events live.State, GameState.events stillLive.State)
        // 一点开就暂停：牌桌上摆着一张快照时，定时器推得再快人也看不见。
        Assert.False(opened.Playback.Playing)

        // 收起来就回到现在。
        let closed = opened |> step (RecordOpened None)
        Assert.Equal(live.Turns, (shownTable closed).Turns)
