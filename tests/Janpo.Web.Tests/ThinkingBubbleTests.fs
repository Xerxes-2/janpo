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
/// 与**全文面板**（`TableState.detail`）的判据，六件事：
///
/// 1. 三态各是什么、一眼分不分得开（在想 / 说了什么 / 兜底代打）；
/// 2. **数据源只有 `Table.Decisions` 一处**：改那条记录，气泡跟着变；
/// 3. **按座位取**：bot 席没有气泡，四席都有记录时四个气泡都在；
/// 4. Live 与回放**两边都要**：回放沿游标取那一手，Live 里点历史某一手走
///    「导成牌谱 → `Table.replay` → 取那一帧」（只读，`live.Table` 一手都不退回去）；
/// 5. **可见性是两根正交的轴，AND 关系**（票 81）：视角掩蔽（`reveals`）×
///    挂在对局配置与终局状态上的那一根（ADR-0003 的 consequence）；
/// 6. **气泡只放一句话**（票 81）：reason 优先、太长就截且截了会说，全文在面板里。
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
    let ``说了什么：reason 优先，只有 thinking 时取它的头一段并三点号收尾`` () =
        // **与票 76 那一版相反**（那时是 thinking 优先）：主人在票 81 里重裁了一次
        // ——气泡里只放**一句话**，thinking 全文是点开那块面板的活（CONTEXT.md 的 `Thinking Bubble`）。
        // 真语料上旧那条优先级就是把一整段思考原文塞进一个气泡（79 §8 报的那条病）。
        let full = recorded 7 0
        let noReason = { full with Reason = None }

        let multi =
            { full with
                Reason = None
                Thinking = Some "先数向听：这一手是 2 向听。\n再看安全度：对家立直了，九筒是现物。\n所以打九筒。"
            }

        let mute =
            { full with
                Thinking = None
                Reason = None
            }

        let bubbleOf (record: DecisionRecord) =
            loadedWith [ record ] |> atTurn record.Turn |> bubbleAt 0

        let saidAt (record: DecisionRecord) =
            bubbleOf record |> Option.map Bubble.toDisplay

        // 两样都有时取 reason（**不是** thinking），而且没截过：短理由不挂那枚招子。
        Assert.Equal(Some "第 7 手的一句话理由（座位 0）", saidAt full)
        Assert.Equal(Some false, bubbleOf full |> Option.map Bubble.clipped)

        // 只有 thinking：取**头一段**，后面还有——于是三点号收尾且挂招子。
        Assert.Equal(Some "先数向听：这一手是 2 向听。……", saidAt multi)
        Assert.Equal(Some true, bubbleOf multi |> Option.map Bubble.clipped)

        // 只有 thinking 且它本来就只有一段：没东西被丢掉，**就不允许装作截过**。
        Assert.Equal(Some "第 7 手的思考原文（座位 0）", saidAt noReason)
        Assert.Equal(Some false, bubbleOf noReason |> Option.map Bubble.clipped)

        // 两样都没有时也要说一句：空气泡与「没有气泡」在页面上分不出来。
        Assert.Equal(Some "（这一手没留下理由与思考原文）", saidAt mute)

    /// 真语料里**最长那句理由**（票 79 换上去的那份 Demo，464 条：中位 48 字、最长 260）。
    ///
    /// **拿真语料而不是手捣的长串**：这一票治的就是真语料把气泡撑爆的那条病（79 §8），
    /// 用固件验等于自己定一个刚好超线的长度再去验它。
    let private longestReason: string =
        demo.Decisions
        |> List.choose (fun record -> record.Reason)
        |> List.sortByDescending String.length
        |> List.head

    [<Fact>]
    let ``气泡只放一句话：真语料里的长理由截到上限并说一声，面板里仍是全文`` () =
        // 防空转（判据 3）：语料里得真有一条长到截得动的理由，否则下面整段验的是别的事。
        Assert.True(String.length longestReason > 100, $"真语料里最长那条理由只有 {String.length longestReason} 字")

        let record =
            { recorded 7 0 with
                Reason = Some longestReason
            }

        let model = loadedWith [ record ] |> atTurn 7

        match bubbleAt 0 model with
        | None -> failwith "这一席该有一个气泡"
        | Some bubble ->
            let shown = Bubble.toDisplay bubble
            // 截了，而且**截了会说**（气泡上那枚「点开看全文」读的就是 `clipped`）。
            Assert.True(Bubble.clipped bubble, "真语料里最长那条理由在气泡里应当是截过的")
            Assert.True(String.length shown < String.length longestReason, $"气泡里写的仍是全文（{String.length shown} 字）")
            Assert.EndsWith("……", shown)
            // 上限是一句话的量：真语料的中位是 48 字，一个气泡不该比两句话还长。
            Assert.True(String.length shown <= 80, $"气泡里那句话 {String.length shown} 字，不再是一句话")
            // 头上那一截字不允许被改写：截的是尾巴，不是重写一句。
            Assert.StartsWith(longestReason.Substring(0, 20), shown)

        // **面板里仍是全文**：气泡只是一个窗口，记录一字未动。
        match TablePage.detail (model |> step (RecordOpened(Some 7))) with
        | None -> failwith "第 7 手该摊得开"
        | Some detail -> Assert.Equal(Some longestReason, detail.Record.Reason)

    [<Fact>]
    let ``气泡里的字来自那一手的决策记录：改一个字，气泡跟着变`` () =
        let record = recorded 7 0

        let edited = { record with Reason = Some "改过的一句话理由" }

        // thinking 改了但 reason 没改：气泡上那句话不变（reason 优先），
        // **而背后那条记录仍旧跟着变**——数据源只有 `Table.Decisions` 一处。
        let rethought =
            { record with
                Thinking = Some "改过的思考原文"
            }

        let saidAt (record: DecisionRecord) =
            loadedWith [ record ] |> atTurn record.Turn |> bubbleAt 0

        Assert.Equal(Some(Bubble.Spoke record), saidAt record)
        Assert.Equal(Some(Bubble.Spoke edited), saidAt edited)
        Assert.NotEqual(saidAt record |> Option.map Bubble.toDisplay, saidAt edited |> Option.map Bubble.toDisplay)

        Assert.Equal(Some(Bubble.Spoke rethought), saidAt rethought)
        Assert.Equal(saidAt record |> Option.map Bubble.toDisplay, saidAt rethought |> Option.map Bubble.toDisplay)

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
        // **得先切到上帝视角**（票 81）：`?table=1` 默认坐在座位 0 上，不切的话
        // 下面那句「其余三席一个气泡都没有」会因为视角掩蔽而恒真——那就是空转（判据 3）。
        let asked = llmTable () |> askedOnce |> step (ViewpointPicked Viewpoint.God)

        Assert.Equal(Seat.first, DecisionPackage.seat (awaitingOf asked).Package)
        // 带着已等秒数与上限（票 74）：刚问出去是 0 秒，上限 = 档案超时 240000 ms → 240 秒。
        Assert.Equal(Some(Bubble.Thinking(0, 240)), bubbleAt 0 asked)

        // 其余三席是自带 bot：**一条记录都没有，因此一个气泡都没有**（上帝视角下也没有）。
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

    // ---- 视角是一道信息闸门（票 81）× ADR-0003 那一根 ----

    /// 四席各说过一手那一帧上，某个视角下四席的气泡。
    let private bubblesUnder (viewpoint: Viewpoint) (model: TableModel) : Bubble option list =
        let model = model |> step (ViewpointPicked viewpoint)
        let table = shownTable model
        [ for index in 0..3 -> TablePage.bubbles model table (seat index) ]

    [<Fact>]
    let ``视角是一道信息闸门：坐座位 N 只看得见自家，上帝视角四家全开`` () =
        // 主人在票 81 里裁的那条：**与手牌同一条规则**。票 76 那一版反过来
        // （五个视角下四席的气泡逐个相同），于是坐到座位 0 上也读得到另外三家在想什么。
        let model = loadedWith fourSeats |> atTurn 10

        // 上帝视角：四家都在，**而且四句话互不相同**（阳性对照：不是全藏了，也不是四个同一句）。
        let god = bubblesUnder Viewpoint.God model
        Assert.Equal(4, god |> List.sumBy (fun bubble -> if Option.isSome bubble then 1 else 0))

        Assert.Equal(4, god |> List.choose (Option.map Bubble.toDisplay) |> List.distinct |> List.length)

        // 坐座位 N：**只剩那一家**，而且剩下的那一个与上帝视角下它自己那一个逐字相同
        // （否则「只剩一家」可以靠把四家都换成同一个空壳混过去）。
        for index in 0..3 do
            let seated = bubblesUnder (Viewpoint.Seated(seat index)) model
            Assert.Equal(1, seated |> List.sumBy (fun bubble -> if Option.isSome bubble then 1 else 0))
            Assert.Equal(List.item index god, List.item index seated)

            for other in 0..3 do
                if other <> index then
                    Assert.Equal(None, List.item other seated)

    [<Fact>]
    let ``回放里终局也不放开：escape hatch 是上帝视角那一按，不是时间`` () =
        // 回放本来就全是终局之后的事，“打完了就放开”等于让坐座视角在回放里形同虚设。
        let model = loadedWith fourSeats |> step (CursorMoved 100000)

        // 防空转：这一帧真的是终局之后（`unlocked` 那一根在这一帧上是开着的）。
        Assert.True(Option.isSome (Table.result (shownTable model)), "末帧该是终局之后那一屏")

        Assert.Equal(
            4,
            bubblesUnder Viewpoint.God model
            |> List.sumBy (fun each -> if Option.isSome each then 1 else 0)
        )

        for index in 0..3 do
            let seated = bubblesUnder (Viewpoint.Seated(seat index)) model
            Assert.Equal(1, seated |> List.sumBy (fun each -> if Option.isSome each then 1 else 0))

    [<Fact>]
    let ``两根轴正交：视角那一根只读视角，ADR-0003 那一根只读对局配置与终局状态`` () =
        // ADR-0003：「围观者」不是权限级别，只是视角——所以可见性不挂在「用户是谁」上。
        // 票 81 加的这一根同样不挂在身份上：它只读 `model.Viewpoint`，而那一排按钮谁都按得了。
        // **这一条钉的就是两根轴各自只读自己那一份输入**（合起来是 AND，在 `bubbles` 一处）。
        let model = loadedWith fourSeats |> atTurn 10

        // 视角那一根：不读牌桌、不读终局、不读有没有记录——只读视角。
        for index in 0..3 do
            let seated = model |> step (ViewpointPicked(Viewpoint.Seated(seat index)))

            for other in 0..3 do
                Assert.Equal((index = other), TablePage.reveals seated (seat other))

            Assert.True(TablePage.reveals (model |> step (ViewpointPicked Viewpoint.God)) (seat index))

        // ADR-0003 那一根：今天没有真人坐席（`SeatPlayer` 就两个 case，M3 才改），
        // 因此它恒为“解锁”；它不该因为换了视角而变——上帝视角下四席全在就是它没被动过的证据。
        Assert.Equal(
            4,
            bubblesUnder Viewpoint.God model
            |> List.sumBy (fun each -> if Option.isSome each then 1 else 0)
        )

    // ---- 全文面板（story 5） ----

    [<Fact>]
    let ``点开气泡：全文面板给的是那一手的记录与当时的局面快照`` () =
        let model = loadedWith fourSeats |> atTurn 10
        Assert.True(Option.isNone (TablePage.detail model), "没点开之前没有全文面板")

        let opened = model |> step (RecordOpened(Some 8))

        // 第 8 手是座位 1 的：**坐到座位 0 上就摊不开**（票 81：面板同受视角那道闸门管，
        // 否则气泡藏了而全文还摊得开，闸门就只是个摆设）。
        Assert.True(Option.isNone (TablePage.detail (opened |> step (ViewpointPicked(Viewpoint.Seated(seat 0))))))
        Assert.True(Option.isSome (TablePage.detail (opened |> step (ViewpointPicked(Viewpoint.Seated(seat 1))))))

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
