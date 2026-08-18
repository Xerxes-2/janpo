namespace Janpo.Web.Tests

open System
open System.IO
open Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// 牌谱从外面进来的两条路（票 78）：打开带载荷的地址（分享链接）与导入牌谱 JSON。
///
/// 判据与票 71 同一条：**回放这一侧不许有第二份实现**——两条新来源都落进同一个
/// `ReplayTable`，因此这里钉的是「入口对不对」（自动播、规则集换、三种失法的话说没说清、
/// 旧回放轰没轰掉、定时器的世代换没换），fold 本身的对错归 `ReplayTableTests`。
module ImportShareTests =

    // ---- 语料：首页那份 Demo 资产（bot 牌谱），外加拌一条决策记录的「全量」版 ----

    let private assetText =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "demo-paifu.json"))

    let private demo: Paifu =
        match Decode.fromString Paifu.decoder assetText with
        | Ok paifu -> paifu
        | Error message -> failwith $"Demo 牌谱读不动：{message}"

    /// 一条决策记录（拌进 Demo 里当「全量牌谱」的阳性对照——Demo 本身一条都没有）。
    let private record: DecisionRecord =
        {
            Turn = 3
            Seat = Seat.first
            PromptTail = "【现在】……"
            RenderVersion = "janpo-default@aaaaaaaa.bbbbbbbb"
            ActionIds = [ 0 ]
            Output = "{}"
            Reason = Some "就它了"
            Thinking = Some "先数向听——IMPORT-THINKING-MARK"
            Attempts = 1
            LatencyMs = 640
            Applied = Some 0
            Fallback = None
            Usage = None
        }

    /// 带一条决策记录的「全量」牌谱与它的 JSON 原文（导入走的就是原文）。
    let private full: Paifu = { demo with Decisions = [ record ] }

    let private text (paifu: Paifu) : string =
        Paifu.encoder paifu |> Encode.toString 0

    /// 与 `TableState.importCmd` 同一条映射（那边是 JS 后端、只在浏览器里跑）：
    /// 真解码器的诊断原文过一遍 update，而不是手捐一句像样的错。
    let private decoded (raw: string) : Result<Paifu, string> =
        Decode.fromString Paifu.decoder raw
        |> Result.mapError (fun message -> $"牌谱读不动：{message}")

    /// 把头一局收尾前的三条事件抽掉：那一局断在中间，后面的局还跟着。
    let private stranded (events: Event list) : Event list =
        let second =
            events
            |> List.indexed
            |> List.choose (fun (index, event) ->
                match event with
                | StartKyoku _ -> Some index
                | _ -> None)
            |> List.item 1

        events
        |> List.indexed
        |> List.filter (fun (index, _) -> index < second - 3 || index >= second)
        |> List.map snd

    // ---- 驱动 ----

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    let private loaded () : TableModel =
        TablePage.home () |> fst |> step (DemoLoaded(Ok demo))

    let private host () : TableModel =
        TablePage.initial RulesetDraft.initial (SeatingPlan.initial Ruleset.yonma)
        |> fst

    let private shownTable (model: TableModel) : Table =
        match TablePage.shown model with
        | Shown.Board table -> table
        | other -> failwith $"这一刻该有一桌，却是 {other}"

    let private sharedOf (model: TableModel) : ShareOutcome option =
        match TablePage.live model with
        | Some live -> live.Shared
        | None -> failwith "这一刻该是 Live"

    // ---- 分享链接那条路 ----

    [<Fact>]
    let ``打开带载荷的地址与首页同一屏起步：还在解、暂停着、上帝视角、没有配桌`` () =
        // **逐字段同一份模型**：两屏各拼各的话，「上帝视角」这类默认就会各自漂。
        Assert.Equal(TablePage.home () |> fst, TablePage.shared "abc" |> fst)

    [<Fact>]
    let ``载荷解回来就自动播，规则集换成牌谱自带的那一份`` () =
        let model = TablePage.shared "abc" |> fst |> step (SharedLoaded(Ok demo))

        Assert.True(model.Playback.Playing, "带载荷打开的卖点就是直接看回放")
        Assert.Equal(demo.Ruleset, model.Ruleset)
        Assert.Equal(0, (shownTable model).Turns)
        Assert.True(TablePage.canAdvance model)
        // 时间轴白拿（票 75）：分享链接那份回放拖得动。
        Assert.True(TablePage.timeline model |> Option.isSome)

    [<Fact>]
    let ``载荷读不动就是那句中文，页面不白屏`` () =
        let model =
            TablePage.shared "abc" |> fst |> step (SharedLoaded(Error "载荷读不动：分享链接里没有载荷"))

        match TablePage.shown model with
        | Shown.Fault reason -> Assert.StartsWith("载荷读不动：", reason)
        | other -> failwith $"读不动时该说一句原因，却是 {other}"

    [<Fact>]
    let ``载荷里那份牌谱回放不动：一句中文，前缀说得清是第三层`` () =
        // 只有 start_game 的牌谱：读得动、回放不动（`Replay` 的 NoKyoku）。
        let empty =
            Paifu.create Ruleset.yonma [ StartGame [ "p0"; "p1"; "p2"; "p3" ] ] [] Prompting.empty

        let model = TablePage.shared "abc" |> fst |> step (SharedLoaded(Ok empty))

        match TablePage.shown model with
        | Shown.Fault reason -> Assert.StartsWith("载荷里那份牌谱回放不动：", reason)
        | other -> failwith $"回放不动时该说一句原因，却是 {other}"

    [<Fact>]
    let ``SharedLoaded 在 Live 那一页一律无事发生：过期的载荷回执不许轰掉正打着的一桌`` () =
        let model = host ()
        let after = model |> step (SharedLoaded(Ok demo))

        Assert.True(TablePage.live after |> Option.isSome, "Live 不许被换成回放")
        Assert.Equal((shownTable model).Turns, (shownTable after).Turns)

    // ---- 导入 JSON 那条路 ----

    [<Fact>]
    let ``导入一份牌谱：换上它、自动播、上一次失败的话撤掉`` () =
        let noted = loaded () |> step (ImportLoaded(Error "这个文件读不进来：NotReadableError"))

        Assert.Equal(Some "这个文件读不进来：NotReadableError", noted.ImportFault)

        let imported = noted |> step (ImportLoaded(decoded (text full)))

        Assert.True(imported.Playback.Playing, "导入的下场就是一份自动播的回放")
        Assert.Equal(full.Ruleset, imported.Ruleset)
        Assert.Equal(0, (shownTable imported).Turns)
        Assert.Equal(None, imported.ImportFault)

    [<Fact>]
    let ``分享链接那份没有决策记录、导入的全量那份有：气泡的差别就在这里`` () =
        // 同一场对局的两份形态：URL 走 `stripAudit`（棋谱），JSON 文件是全量。
        let linked =
            TablePage.shared "abc" |> fst |> step (SharedLoaded(Ok(Paifu.stripAudit full)))

        Assert.True(TablePage.recordless linked, "分享链接那份不带推理，页面要说得出为什么没气泡")

        let imported = loaded () |> step (ImportLoaded(decoded (text full)))
        Assert.False(TablePage.recordless imported, "全量牌谱带决策记录，导入的那一份气泡有话")

        match imported.Source with
        | Source.Replay(ReplayTable.Ready(frames, _)) ->
            Assert.Equal<DecisionRecord list>([ record ], (List.last frames).Decisions)
        | other -> failwith $"导入之后该是逐帧的回放，却是 {other}"

    [<Fact>]
    let ``导入的三种失法各有中文原因，而正在播的那份回放不受影响`` () =
        let before = loaded ()
        let turns = (shownTable before).Turns

        let broken =
            [
                // 不是 JSON：真解码器的诊断跟在「牌谱读不动：」后面。
                "不是 JSON", decoded "这不是 JSON", "牌谱读不动："
                // 缺字段的牌谱：同一层、另一句诊断。
                "缺字段", decoded """{"version":3}""", "牌谱读不动："
                // 中间某局断掉的事件流：读得动、回放推不下去。**断在中间而不是剪掉尾巴**：
                // 干净的前缀是合法的（ADR-0002，分享没打完的对局走的就是它），
                // 把头一局收尾那几条抽掉、后面的局照旧跟着，才是「推不下去」。
                "中间断掉",
                decoded (
                    text
                        { demo with
                            Events = stranded demo.Events
                        }
                ),
                "牌谱回放不动："
                // 文件本身读不进来（浏览器的 reject，`importCmd` 包的那句）。
                "读不进来", Error "这个文件读不进来：NotReadableError", "这个文件读不进来："
            ]

        for label, bad, prefix in broken do
            let after = before |> step (ImportLoaded bad)

            match after.ImportFault with
            | Some reason -> Assert.StartsWith(prefix, reason)
            | None -> failwith $"导入「{label}」那一份竟然没红"

            // 页面活着：原来那份回放一帧没动、还推得动。
            Assert.Equal(turns, (shownTable after).Turns)
            Assert.Equal(before.Source, after.Source)
            Assert.True(TablePage.canAdvance after)

    [<Fact>]
    let ``ImportLoaded 在 Live 那一页一律无事发生`` () =
        let model = host ()
        let after = model |> step (ImportLoaded(decoded (text full)))

        Assert.True(TablePage.live after |> Option.isSome)
        Assert.Equal(None, after.ImportFault)
        Assert.Equal((shownTable model).Turns, (shownTable after).Turns)

    // ---- 换一份牌谱要把在飞的定时器作废（双倍速那个坑） ----

    [<Fact>]
    let ``导入把在飞的那记定时器作废：旧世代的 Ticked 不许再推新回放`` () =
        let model = loaded ()
        let stale = model.Playback.Generation
        let imported = model |> step (ImportLoaded(decoded (text full)))

        // 世代必须换：不换的话旧定时器与新发的一起被认下，牌桌双倍速走。
        Assert.True(imported.Playback.Generation > stale, "导入之后世代号必须往前走")
        Assert.Equal(0, (shownTable (imported |> step (Ticked stale))).Turns)
        // 新世代的定时器照常推：作废的只是旧的那一记。
        Assert.Equal(1, (shownTable (imported |> step (Ticked imported.Playback.Generation))).Turns)

    [<Fact>]
    let ``「从头再放」也把在飞的那记定时器作废：正播着时再按不许双倍速`` () =
        let model = loaded ()
        let stale = model.Playback.Generation
        let again = model |> step (Ticked stale) |> step Restarted

        Assert.True(again.Playback.Playing)
        Assert.True(again.Playback.Generation > stale, "从头再放之后世代号必须往前走")
        Assert.Equal(0, (shownTable (again |> step (Ticked stale))).Turns)
        Assert.Equal(1, (shownTable (again |> step (Ticked again.Playback.Generation))).Turns)

    // ---- 复制分享链接的阈值与三态 ----

    [<Fact>]
    let ``阈值：一整场半庄（实测 7,720）够发，8,000 以内算复制成，超过就当场劝人改用 JSON`` () =
        let at (result: Result<int, string>) =
            host () |> step (ShareSettled result) |> sharedOf

        Assert.Equal(Some(ShareOutcome.Copied 7720), at (Ok 7720))
        Assert.Equal(Some(ShareOutcome.Copied 8000), at (Ok 8000))
        Assert.Equal(Some(ShareOutcome.Oversized 8001), at (Ok 8001))
        Assert.Equal(Some(ShareOutcome.Failed "浏览器不让写剪贴板（x）"), at (Error "浏览器不让写剪贴板（x）"))

    [<Fact>]
    let ``分享下场那句话：字符数与「导出牌谱」都得在——人要能自己核超了多少`` () =
        let oversized = ShareOutcome.toDisplay (ShareOutcome.Oversized 9123)

        Assert.Contains("9123", oversized)
        Assert.Contains(string ShareOutcome.threshold, oversized)
        Assert.Contains("导出牌谱", oversized)
        Assert.Contains("4842", ShareOutcome.toDisplay (ShareOutcome.Copied 4842))
        Assert.Contains("不让写", ShareOutcome.toDisplay (ShareOutcome.Failed "浏览器不让写剪贴板（x）"))
        // 给机器看的那一半：闸门读 `data-share`，与三态一一对应。
        Assert.Equal("copied", ShareOutcome.toWire (ShareOutcome.Copied 1))
        Assert.Equal("oversized", ShareOutcome.toWire (ShareOutcome.Oversized 1))
        Assert.Equal("failed", ShareOutcome.toWire (ShareOutcome.Failed ""))

    [<Fact>]
    let ``点「复制分享链接」先把上一次的下场撤下来：新的一次正在路上`` () =
        let model = host () |> step (ShareSettled(Ok 42))
        Assert.Equal(Some(ShareOutcome.Copied 42), sharedOf model)
        Assert.Equal(None, sharedOf (model |> step Shared))

    [<Fact>]
    let ``回放那一页没有分享这回事：Shared 与 ShareSettled 一律无事发生`` () =
        let model = loaded ()

        for message in [ TableMsg.Shared; TableMsg.ShareSettled(Ok 42) ] do
            let after = model |> step message
            Assert.True(TablePage.live after |> Option.isNone, $"{message} 不该把回放变成 Live")
            Assert.Equal((shownTable model).Turns, (shownTable after).Turns)
