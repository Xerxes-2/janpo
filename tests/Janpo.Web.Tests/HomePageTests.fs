namespace Janpo.Web.Tests

open System
open System.IO
open Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// 首页那一屏（票 71）：**访客打开 `/` 什么都不用配，第一眼就是一桌牌在走**
/// （spec 的 story 1，ADR-0003 由 Demo Paifu 兑现）。
///
/// 两件事在这里钉住：
///
/// 1. **那份资产真的合格**——东风战、有立直、有副露、以和了终、体积不失控。
///    它是**产品资产不是测试固件**（ADR-0003），因此这几条断言钉的是「够不够格当门面」，
///    不是「哪一张牌打在哪一巡」。票 79 换成真的四席对局时照它换，不合格当场红。
/// 2. **那一屏的状态机**——拉、播、播到终局停、从头再放，以及 Live 那几条消息在回放里无事发生。
module HomePageTests =

    // ---- 那份资产（`web/public/demo-paifu.json`） ----

    /// 资产由 `Janpo.Web.Tests.fsproj` 拷进输出目录（与黄金用例同一种做法）。
    let private assetPath = Path.Combine(AppContext.BaseDirectory, "demo-paifu.json")

    let private assetText = File.ReadAllText assetPath

    let private demo: Paifu =
        match Decode.fromString Paifu.decoder assetText with
        | Ok paifu -> paifu
        | Error message -> failwith $"首页那份 Demo 牌谱读不动（{assetPath}）：{message}"

    /// 体积上限。**首页要 `fetch` 它**，因此它是首屏的一部分。
    /// 票 79 会换成带 thinking 的真对局（实测约 10 KB/手），那一版仍得挤进这个数；
    /// 挤不进就该先剪 thinking，而不是默默把首页拖慢。
    let private sizeBudget = 512 * 1024

    let private eventKinds (paifu: Paifu) : Event list = paifu.Events

    [<Fact>]
    let ``Demo 是东风战：首页不该让人等半小时`` () =
        Assert.Equal(Tonpuusen, demo.Ruleset.Length)

    [<Fact>]
    let ``Demo 里有立直也有副露：挑的是一局看得懂的牌`` () =
        let riichi =
            eventKinds demo
            |> List.filter (fun event ->
                match event with
                | RiichiAccepted _ -> true
                | _ -> false)

        let naki =
            eventKinds demo
            |> List.filter (fun event ->
                match event with
                | Pon _
                | Chi _
                | Minkan _
                | Ankan _
                | Kakan _ -> true
                | _ -> false)

        Assert.NotEmpty riichi
        Assert.NotEmpty naki

    [<Fact>]
    let ``Demo 以和了终：停下来那一屏有役与符番可看`` () =
        // 「以和了终」= 最后一条结局事件是和了（而不是流局）：首页停在那一刻，
        // 结算面板上得有役、符与番，否则访客的最后一眼是一句「流局」。
        let outcomes =
            eventKinds demo
            |> List.filter (fun event ->
                match event with
                | Hora _
                | Ryuukyoku _ -> true
                | _ -> false)

        match List.tryLast outcomes with
        | Some(Hora _) -> ()
        | other -> failwith $"首页那份 Demo 该以和了终，末尾却是 {other}"

    [<Fact>]
    let ``Demo 的体积在预算内：首屏要 fetch 它`` () =
        Assert.True(assetText.Length <= sizeBudget, $"Demo 牌谱 {assetText.Length} 字节，超出 {sizeBudget} 字节的预算")

    [<Fact>]
    let ``Demo 回放得动，且打到了终局精算`` () =
        match Table.replay demo with
        | Error reason -> failwith $"首页那份 Demo 应当回放得动，却得到「{reason}」"
        | Ok frames ->
            let last = List.last frames

            Assert.True(List.length frames > 100, "一场东风战不该只有几十帧")
            Assert.True(Option.isNone last.Fault)
            // 终局精算：末帧上「这一场打完了」这句话才有得说。
            Assert.True(Table.result last |> Option.isSome)
            Assert.True(Board.final last |> Option.isSome)
            Assert.True(Board.settlement last |> Option.isSome)

    // ---- 那一屏的状态机 ----

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    /// 首页初次打开，随后把那份 Demo 牌谱喂给它（浏览器里这一步是 `fetch` 回来的）。
    let private loaded () : TableModel =
        TablePage.home () |> fst |> step (DemoLoaded(Ok demo))

    let private shownTable (model: TableModel) : Table =
        match TablePage.shown model with
        | Shown.Board table -> table
        | other -> failwith $"这一刻该有一桌，却是 {other}"

    [<Fact>]
    let ``首页初次打开：没有配桌，牌桌那一格说「在拉」`` () =
        let model, _ = TablePage.home ()

        // **没有 Live 那一半**：配桌与模型面板因此在类型上就摆不出来。
        Assert.True(Option.isNone (TablePage.live model))
        Assert.True(Option.isNone (TablePage.rosterOf model))
        Assert.False(TablePage.renderingPending model)
        Assert.Equal(Shown.Loading, TablePage.shown model)
        // 还没开播：牌谱没回来之前定时器空转没有意义。
        Assert.False(model.Playback.Playing)

    [<Fact>]
    let ``牌谱回来就自动播，且规则集换成牌谱自带的那一份`` () =
        let model = loaded ()

        Assert.True(model.Playback.Playing, "首页的卖点就是自动播")
        Assert.Equal(demo.Ruleset, model.Ruleset)
        Assert.Equal(0, (shownTable model).Turns)
        Assert.True(TablePage.canAdvance model)

    [<Fact>]
    let ``一记定时器推一帧：牌桌真的在走`` () =
        let model = loaded ()
        let ticked = model |> step (Ticked model.Playback.Generation)

        Assert.Equal(1, (shownTable ticked).Turns)
        Assert.True(ticked.Playback.Playing)

        // 过期的定时器照样丢掉（与 Live 同一条判据）。
        let stale = ticked |> step (Ticked(model.Playback.Generation - 1))
        Assert.Equal(1, (shownTable stale).Turns)

    [<Fact>]
    let ``播到终局就停在结算面板上`` () =
        let rec loop (left: int) (model: TableModel) =
            if not model.Playback.Playing then
                model
            elif left <= 0 then
                failwith "这一场在预算内没播完"
            else
                loop (left - 1) (model |> step (Ticked model.Playback.Generation))

        let ended = loop 2000 (loaded ())
        let table = shownTable ended

        Assert.False(ended.Playback.Playing, "播完了就该停，而不是空转")
        Assert.False(TablePage.canAdvance ended)
        Assert.True(Table.result table |> Option.isSome)
        Assert.True(Board.settlement table |> Option.isSome)

    [<Fact>]
    let ``「从头再放」回到第 0 帧并接着播`` () =
        let walked =
            loaded ()
            |> fun model -> model |> step (Ticked model.Playback.Generation)
            |> fun model -> model |> step (Ticked model.Playback.Generation)

        Assert.Equal(2, (shownTable walked).Turns)

        let again = walked |> step Restarted

        Assert.Equal(0, (shownTable again).Turns)
        Assert.True(again.Playback.Playing)

    [<Fact>]
    let ``拉不到那份牌谱时说一句原因，不白屏`` () =
        let failed =
            TablePage.home ()
            |> fst
            |> step (DemoLoaded(Error "Demo 牌谱拉不到：/demo-paifu.json 回了 HTTP 404"))

        match TablePage.shown failed with
        | Shown.Fault reason -> Assert.Contains("HTTP 404", reason)
        | other -> failwith $"拉不到时该说一句原因，却是 {other}"

    [<Fact>]
    let ``回放里视角切得动，座位视角要的掩蔽流也在`` () =
        let model = loaded () |> step (ViewpointPicked Viewpoint.God)

        Assert.Equal(Viewpoint.God, model.Viewpoint)
        Assert.True(Board.ofTable Viewpoint.God (shownTable model) |> Option.isSome)

        let seated = model |> step (ViewpointPicked(Viewpoint.Seated Seat.first))
        Assert.True(Board.ofTable seated.Viewpoint (shownTable seated) |> Option.isSome)

    /// 一条**过期的**回执（重开过、或者压根不是这一页发出去的）。
    let private staleAnswer: AgentAnswer =
        {
            ActionId = Some 0
            Reason = None
            Failure = None
            Attempts = 1
            LatencyMs = 1
            PromptTail = ""
            Preamble = ""
            RenderVersion = ""
            Tools = ""
            ActionIds = [ 0 ]
            Output = ""
            Thinking = None
            Usage = None
        }

    [<Fact>]
    let ``Live 那几条消息在回放里一律无事发生`` () =
        // 回放页上根本没有那几个控件，但 update 是纯的：喂进去也不许把那一屏弄坏。
        let model = loaded ()

        // 一律限定 `TableMsg.`：曳光弹那一页也有一个 `SeedEdited`（`App.Msg`），不限定会挑错。
        let messages =
            [
                TableMsg.Advanced
                TableMsg.KyokuAdvanced
                TableMsg.Exported
                TableMsg.SeedEdited "42"
                TableMsg.BotPicked Bot.Opinionated
                TableMsg.LlmSeatPicked(Some Seat.first)
                TableMsg.LlmEdited(LlmField.Model, "deepseek-v4")
                TableMsg.Answered(1, staleAnswer)
            ]

        for message in messages do
            let after = model |> step message
            Assert.True(Option.isNone (TablePage.live after), $"{message} 不该把回放变成 Live")
            Assert.Equal((shownTable model).Turns, (shownTable after).Turns)

    [<Fact>]
    let ``主持人那一页仍然默认暂停：要点、要读牌桌的闸门全靠这一条`` () =
        let config: LlmSeat =
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

        let model, _ = TablePage.initial None config

        Assert.False(model.Playback.Playing)
        Assert.True(Option.isSome (TablePage.live model))
