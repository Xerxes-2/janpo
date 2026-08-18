namespace Janpo.Web.Tests

open System
open System.IO
open Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// 回放的时间轴（票 75）：**游标是权威，局面是它的函数**（ADR-0002）。
///
/// 帧在载入那一刻一次 fold 好（`Table.replay`，票 71 的既成事实），因此这里钉的
/// 不是「fold 对不对」（那在 `ReplayTableTests`），而是**游标动得对不对**：
///
/// 1. 拖到某一处 = 直接 fold 同一前缀（**第三个锚点**：`Replay.game` 另起一次 fold，
///    重建的是另一座牌山，与页面那条 `Replay.trace` → `Table.apply` 的路不是同一条）；
/// 2. 幂等：同一个游标来回到达两次，渲染读的每一样逐字段相同；
/// 3. 两头：拖回 0 是开局那一瞬，拖到末尾是票 71 今天那一屏（含结算与终局精算）；
/// 4. 局边界与逐事件步进落在该落的帧上；
/// 5. 71-8：回放默认**上帝视角**，Live 那一页不动。
module ReplayTimelineTests =

    // ---- 语料：首页那份 Demo（页面上真跑的就是它） ----

    /// 与 `HomePageTests` 同一份资产（由 fsproj 拷进输出目录）。
    /// **拿它而不是现打一场**：时间轴是首页那一屏的控件，量的就该是那一屏的语料。
    let private assetPath = Path.Combine(AppContext.BaseDirectory, "demo-paifu.json")

    let private demo: Paifu =
        match Decode.fromString Paifu.decoder (File.ReadAllText assetPath) with
        | Ok paifu -> paifu
        | Error message -> failwith $"首页那份 Demo 牌谱读不动（{assetPath}）：{message}"

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    /// 首页打开、牌谱喂进去（浏览器里这一步是 `fetch` 回来的）。
    let private loaded () : TableModel =
        TablePage.home () |> fst |> step (DemoLoaded(Ok demo))

    let private shownTable (model: TableModel) : Table =
        match TablePage.shown model with
        | Shown.Board table -> table
        | other -> failwith $"这一刻该有一桌，却是 {other}"

    let private timelineOf (model: TableModel) : Timeline =
        match TablePage.timeline model with
        | Some timeline -> timeline
        | None -> failwith "回放这一刻该有一根时间轴"

    /// 主持人那一页要的那份座位配置（`?table=1` 的默认值，一把 key 都不带）。
    let private hostConfig: LlmSeat =
        {
            Provider = "deepseek"
            Model = "deepseek-v4-flash"
            BaseUrl = ""
            ApiKey = ""
            TimeoutMs = 30000
            Thinking = Thinking.Off
            Tier = ScaffoldTier.Bare
            Persona = ""
            Template = ""
        }

    // ---- 第三个锚点：直接 fold 同一前缀 ----

    /// 一帧上**看得见的那几样**：四家的手牌、河、副露与点数，加上场上那两个数。
    ///
    /// **刻意不比整个 `GameState`**：把事件流截断之后，引擎停在「等这一张的响应」那一刻，
    /// 而帧那一侧已经把没宣言的那几家的「过」交回去了（`Action.None` 不产出事件，
    /// 因此截断的流里根本看不见它）。**阶段会差一步，看得见的东西一张都不许差。**
    type private Visible =
        {
            Hand: Tile list
            Kawa: Tile list
            Naki: Naki list
            Score: int
        }

    let private visible (state: GameState) : Visible list * int * int =
        let seats =
            GameState.players state
            |> List.map (fun player ->
                {
                    Hand = PlayerState.hand player
                    Kawa = PlayerState.kawa player
                    Naki = PlayerState.naki player
                    Score = PlayerState.score player
                })

        seats, GameState.kyotaku state, Wall.remaining (GameState.wall state)

    /// 把这份牌谱的事件流截到第 `count` 条，**另起一次 fold**，给出那一刻的局面与它对应的游标。
    ///
    /// **它与页面那一侧不是同一条路**：页面拿的是 `Replay.trace` 交出来的动作序列、由
    /// `Table.apply` 一手一手落出来的帧；这里让 `Replay.game` 从截断的事件流重建另一座牌山、
    /// 重跑一次 fold。游标由截断后的轨迹算出来：**帧数 = 手数 + 局数**（票 71 的形态）。
    let private refold (count: int) : Visible list * int * int * int =
        let events = List.truncate count demo.Events

        let state =
            match Replay.game demo.Ruleset events with
            | Error error -> failwith $"截到第 {count} 条事件该 fold 得动，却得到「{ReplayError.toDisplay error}」"
            | Ok replayed ->
                match replayed.Current with
                | Some state -> state
                // 正好截在某一局收尾处：那一局已经收进 `Game` 了。
                | None -> Game.played replayed.Game |> List.last

        let cursor =
            match Replay.trace demo.Ruleset events with
            | Error error -> failwith $"截到第 {count} 条事件该走得出轨迹，却得到「{ReplayError.toDisplay error}」"
            | Ok kyokus ->
                (kyokus |> List.sumBy (fun each -> List.length each.Actions))
                + List.length kyokus
                - 1

        let seats, kyotaku, remaining = visible state
        seats, kyotaku, remaining, cursor

    [<Fact>]
    let ``拖到中间那几个游标：手牌 / 河 / 点数与直接 fold 同一前缀得到的一致`` () =
        let total = List.length demo.Events
        let cuts = [ total / 8; total / 4; total / 2; total * 3 / 4; total - 1 ]

        for cut in cuts do
            let seats, kyotaku, remaining, cursor = refold cut
            let dragged = loaded () |> step (CursorMoved cursor)
            let table = shownTable dragged
            let atCursor, atCursorKyotaku, atCursorRemaining = visible table.State

            Assert.Equal(cursor, (timelineOf dragged).Cursor)
            Assert.Equal<Visible list>(seats, atCursor)
            Assert.Equal(kyotaku, atCursorKyotaku)
            Assert.Equal(remaining, atCursorRemaining)

    // ---- 幂等 ----

    [<Fact>]
    let ``同一个游标来回到达两次，渲染逐字段相同`` () =
        let model = loaded ()
        let last = (timelineOf model).Last
        let target = last / 2

        let once = model |> step (CursorMoved target)

        // **最后一跳故意从另一处来**（`once` 是从第 0 帧跳过去的）：两次都从同一处过来的话，
        // 「目标帧沿用了来处那一帧的东西」这类漂会两边一起漂，这条断言就白给了。
        let again =
            once
            |> step (CursorMoved 3)
            |> step (CursorMoved 0)
            |> step (CursorMoved last)
            |> step (CursorMoved target)

        // 渲染读的那几样逐字段相同：五个视角的投影、各座位的掩蔽流、结算与终局精算，
        // 以及时间轴自己（「第几手 / 第几局 / 上一手的记录」）。
        for viewpoint in Viewpoint.God :: [ for seat in Seat.all demo.Ruleset -> Viewpoint.Seated seat ] do
            Assert.Equal(Board.ofTable viewpoint (shownTable once), Board.ofTable viewpoint (shownTable again))

        for seat in Seat.all demo.Ruleset do
            Assert.Equal(Table.observation seat (shownTable once), Table.observation seat (shownTable again))

        Assert.Equal(Board.settlement (shownTable once), Board.settlement (shownTable again))
        Assert.Equal(Board.final (shownTable once), Board.final (shownTable again))
        Assert.Equal(TablePage.timeline once, TablePage.timeline again)

        // 帧是值：来回到达两次拿到的必须是**同一张牌桌**，不是「长得一样的另一张」。
        // 摆在最后：它一红就抄整张 `Table`，而上面那几条的红读得出是哪一格漂了。
        Assert.True(System.Object.ReferenceEquals(shownTable once, shownTable again), "同一个游标来回到达两次，拿到的不是同一张牌桌（帧被重算过？）")

    // ---- 两头 ----

    [<Fact>]
    let ``拖回 0 就是开局那一瞬`` () =
        let walked = loaded () |> step (CursorMoved 88) |> step (CursorMoved -5)
        let table = shownTable walked

        Assert.Equal(0, (timelineOf walked).Cursor)
        Assert.Equal(0, table.Turns)
        Assert.True(Option.isNone table.Latest, "开局那一瞬还没走一手")
        Assert.Empty(table.Decisions)
        // 四家的河都是空的：真的回到了开局。
        Assert.All(GameState.players table.State, PlayerState.kawa >> Assert.Empty)

    [<Fact>]
    let ``拖到末尾就是票 71 今天那一屏：结算面板与终局精算都在`` () =
        let ended = loaded () |> step (CursorMoved 100000)
        let table = shownTable ended
        let timeline = timelineOf ended

        // 先核那一屏上真有的东西（票 71 今天停下来那一屏），再核游标：
        // 掉到倭数第二帧的话，先红的应当是「结算面板没了」而不是一个光秃的帧号。
        Assert.True(Board.settlement table |> Option.isSome, "末帧该有结算面板")
        Assert.True(Table.result table |> Option.isSome, "末帧该有终局精算")
        Assert.True(Board.final table |> Option.isSome)
        Assert.False(TablePage.canAdvance ended, "末帧再没有下一帧可播")
        Assert.Equal(timeline.Last, timeline.Cursor)

    [<Fact>]
    let ``越界的帧号夹回 [0, 末帧]，不许把牌桌弄丢`` () =
        let model = loaded ()
        let last = (timelineOf model).Last

        for frame, expected in [ -1, 0; -100000, 0; last + 1, last; 100000, last ] do
            let clamped = model |> step (CursorMoved frame)
            Assert.Equal(expected, (timelineOf clamped).Cursor)
            // 夹回来之后仍然摆得出牌桌（`Shown.Fault` 那一支走不到）。
            Assert.Equal(expected, (timelineOf (model |> step (CursorMoved frame))).Cursor)
            Assert.True(Option.isNone (shownTable clamped).Fault)

    // ---- 逐事件步进与局边界 ----

    [<Fact>]
    let ``逐事件步进：一步一帧，走 N 步与一拖到位落在同一帧`` () =
        let model = loaded () |> step (CursorMoved 60)

        let forward (current: TableModel) =
            current |> step (CursorMoved((timelineOf current).Cursor + 1))

        let back (current: TableModel) =
            current |> step (CursorMoved((timelineOf current).Cursor - 1))

        // 一步就是一帧，手数要么持平（跨到下一局的开局帧）要么 +1。
        let one = forward model
        Assert.Equal(61, (timelineOf one).Cursor)
        let walkedTurns = (shownTable one).Turns - (shownTable model).Turns
        Assert.True(walkedTurns >= 0 && walkedTurns <= 1, "一步最多走一手")

        // 前进一步再后退一步 = 原地那一张牌桌。
        Assert.True(System.Object.ReferenceEquals(shownTable model, shownTable (back one)), "前进一步再后退一步没回到原地那一张牌桌")

        // 走五步与一拖到位落在同一帧。
        let stepped = model |> forward |> forward |> forward |> forward |> forward

        Assert.True(
            System.Object.ReferenceEquals(shownTable (model |> step (CursorMoved 65)), shownTable stepped),
            "逐步走五步与一拖到位没落在同一帧"
        )

    [<Fact>]
    let ``局边界：一局一枚，跳过去就落在那一局的开局帧`` () =
        let model = loaded ()
        let marks = (timelineOf model).Marks

        // 局数取自事件流里的 `start_kyoku` 条数——**不是从帧那边数出来的**。
        let kyokus =
            demo.Events
            |> List.sumBy (fun event ->
                match event with
                | StartKyoku _ -> 1
                | _ -> 0)

        Assert.Equal(kyokus, List.length marks)

        Assert.Equal<int list>(
            marks |> List.map (fun mark -> mark.Frame) |> List.sort,
            marks |> List.map (fun mark -> mark.Frame)
        )

        for index, mark in List.indexed marks do
            let jumped = model |> step (CursorMoved mark.Frame)
            let table = shownTable jumped

            Assert.Equal(index, (timelineOf jumped).Kyoku)
            Assert.True(Option.isNone table.Latest, $"第 {index} 局的开局帧还没走一手")
            Assert.False(GameState.isEnded table.State, $"第 {index} 局的开局帧不该是终了的")
            // 轴上那几个字说的就是这一帧的场况（人读的与机器读的同一个来源）。
            let context = GameState.context table.State
            Assert.StartsWith(Kaze.toDisplay context.Bakaze + string context.Kyoku, mark.Label)

    // ---- 播放控制：复用 `Playback`，没有第二套定时器 ----

    [<Fact>]
    let ``一拖就暂停，再按播放是从新游标往下走`` () =
        let playing = loaded ()
        Assert.True(playing.Playback.Playing, "首页的卖点就是自动播")

        let dragged = playing |> step (CursorMoved 30)
        Assert.False(dragged.Playback.Playing, "手搭上时间轴就该停下来")
        // 在飞的那记定时器作废：世代号换了（与 Live 的「单步」同一条判据）。
        Assert.False(Playback.accepts playing.Playback.Generation dragged.Playback)

        let resumed = dragged |> step PlayToggled
        let advanced = resumed |> step (Ticked resumed.Playback.Generation)

        Assert.True(advanced.Playback.Playing)
        Assert.Equal(31, (timelineOf advanced).Cursor)

    // ---- 那一手的决策记录 ----

    [<Fact>]
    let ``游标动时刚落定那一手的决策记录跟着变`` () =
        // Demo 是 bot 牌谱（一条记录都没有），因此这一条自带阳性对照：先拌一条进去。
        Assert.Empty demo.Decisions

        let record: DecisionRecord =
            {
                Turn = 7
                Seat = Seat.first
                PromptTail = "【现在】……"
                RenderVersion = "janpo-default@aaaaaaaa.bbbbbbbb"
                ActionIds = [ 0 ]
                Output = "{}"
                Reason = Some "就它了"
                Thinking = Some "想了想"
                Attempts = 1
                LatencyMs = 640
                Applied = Some 0
                Fallback = None
                Usage = None
            }

        let model =
            TablePage.home ()
            |> fst
            |> step (DemoLoaded(Ok { demo with Decisions = [ record ] }))

        let recordAt (frame: int) =
            (model |> step (CursorMoved frame) |> timelineOf).Record

        // 第 7 手落定的那一帧是第 8 帧（第 0 帧是开局），记录挂在**它**上面。
        Assert.Equal(Some record, recordAt 8)
        // 之前那一帧还看不见（票 71 的切法：`record.Turn < table.Turns`）。
        Assert.Equal(None, recordAt 7)
        // 之后那一帧是别人的手：记录不许粘着不掉。
        Assert.Equal(None, recordAt 9)
        // 开局那一帧一律没有（它没落定新的一手，手数沿用着上一局的）。
        Assert.Equal(None, recordAt 0)

    // ---- 71-8：回放默认上帝视角，Live 不动 ----

    [<Fact>]
    let ``回放默认上帝视角：四家的牌都摊着`` () =
        let model = loaded ()
        Assert.Equal(Viewpoint.God, model.Viewpoint)

        // 「摊着」不是渲染纪律而是投影的形状：上帝视角那一份里没有一个 `HandView.Concealed`。
        let played = model |> step (CursorMoved 60)

        match Board.ofTable played.Viewpoint (shownTable played) with
        | None -> failwith "上帝视角该摆得出牌桌"
        | Some board ->
            Assert.Equal(4, List.length board.Seats)
            Assert.True(Option.isNone board.Viewer, "上帝视角没有观测者")

            Assert.All(
                board.Seats,
                fun seat ->
                    match seat.Hand with
                    | HandView.Revealed _ -> ()
                    | HandView.Concealed count -> failwith $"座位 {Seat.index seat.Seat} 的手牌扣着 {count} 张"
            )

        // 座位视角的按钮留着：切回去仍然是「他家的暗牌根本不在数据里」。
        let seated = played |> step (ViewpointPicked(Viewpoint.Seated Seat.first))

        match Board.ofTable seated.Viewpoint (shownTable seated) with
        | None -> failwith "座位视角该摆得出牌桌"
        | Some board ->
            Assert.Equal(Some Seat.first, board.Viewer)

            Assert.Contains(
                board.Seats,
                fun seat ->
                    match seat.Hand with
                    | HandView.Concealed _ -> true
                    | HandView.Revealed _ -> false
            )

    [<Fact>]
    let ``Live 那一页的默认视角不动，也没有时间轴`` () =
        let model, _ = TablePage.initial RulesetDraft.initial None hostConfig

        Assert.Equal(Viewpoint.Seated Seat.first, model.Viewpoint)
        Assert.True(Option.isNone (TablePage.timeline model), "Live 里点历史某一手是票 76")

        // 拖它一律无事发生（页面上根本没有那根轴，但 update 是纯的，喂进去也不许把它弄坏）。
        let dragged = model |> step (CursorMoved 5)

        Assert.True(Option.isSome (TablePage.live dragged))
        Assert.Equal((shownTable model).Turns, (shownTable dragged).Turns)
        Assert.Equal(model.Playback, dragged.Playback)
