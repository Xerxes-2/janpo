namespace Janpo.Web.Tests

open Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// 牌谱导出（票 26）：**一桌打过的对局 → 一份牌谱 → 回放出同一个终局**。
///
/// 这一层不碰网络也不碰浏览器：Agent 层的回执在这里是一个值，导出的文本由 dotnet 侧的
/// JSON 后端编解码（浏览器里是同一份 codec，只换后端）。它验的是 27 号票要的那条链
/// ——导出的事件流交回引擎 fold，逐条事件与终局精算都必须与原来那一场相同。
module PaifuExportTests =

    let private config: LlmSeat =
        {
            Provider = "deepseek"
            Model = "deepseek-v4-flash"
            BaseUrl = ""
            ApiKey = "sk-测试用的假 key"
            TimeoutMs = 12000
            Thinking = Thinking.Medium
            Tier = ScaffoldTier.Bare
            Persona = ""
            Template = ""
        }

    let private seat (index: int) : Seat =
        match Seat.ofIndex index with
        | Some seat -> seat
        | None -> failwith $"{index} 应当是合法座位"

    /// 座位 1 交给 LLM 的一桌。
    let private llmTable () : TableModel =
        TablePage.initial (Some(seat 1)) config |> fst

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    let private tableOf (model: TableModel) : Table =
        match model.Table with
        | Ok table -> table
        | Error error -> failwith $"这一桌应当开得起来，却得到「{error}」"

    /// 模型好好答话：挑这一包的第 0 条，附一段思考。
    let private spoke: AgentAnswer =
        {
            ActionId = Some 0
            Reason = Some "就它了"
            Failure = None
            Attempts = 1
            LatencyMs = 640
            PromptTail = "【现在】东1局 0 本场……\n【可选动作】只能从下面这些 id 里选一个：\n- id=0：摸切1索"
            Preamble = "你在打日本立直麻将（天凤规则，四人东）……"
            RenderVersion = "janpo-default@08fcaec3.4b9e57c0"
            Tools = """[{"name":"choose_action","parameters":{"properties":{"action_id":{"enum":[]}}}}]"""
            ActionIds = [ 0 ]
            Output = """{"stop_reason":"toolUse","content":[{"type":"toolCall","name":"choose_action"}]}"""
            Thinking = Some "先数向听：现在是 2 向听……"
            Usage =
                Some
                    {
                        Input = 812
                        Output = 96
                        CacheRead = 1344
                        CacheWrite = 0
                    }
        }

    /// 模型交不出来（超时 / provider 报错 / 格式跑偏走的都是这一条）。
    let private refused: AgentAnswer =
        {
            ActionId = None
            Reason = None
            Failure = Some "provider 报错：401（重试 2 次仍无结果）"
            Attempts = 3
            LatencyMs = 91000
            PromptTail = "【现在】东1局 0 本场……"
            Preamble = "你在打日本立直麻将（天凤规则，四人东）……"
            RenderVersion = "janpo-default@08fcaec3.4b9e57c0"
            Tools = """[{"name":"choose_action","parameters":{"properties":{"action_id":{"enum":[]}}}}]"""
            ActionIds = [ 0 ]
            Output = ""
            Thinking = None
            Usage = None
        }

    /// 把一整场对局打完：轮到模型就喂 `answer`，其余座位随机，一局终了就开下一局。
    let private playOut (answer: AgentAnswer) (model: TableModel) : TableModel =
        let rec loop (left: int) (model: TableModel) =
            if left <= 0 then
                failwith "这一场在预算内没打完"
            else
                match model.Awaiting with
                | Some awaiting -> loop (left - 1) (model |> step (Answered(awaiting.Ticket, answer)))
                | None ->
                    let table = tableOf model

                    if Option.isSome (Table.result table) then
                        model
                    elif Table.isKyokuEnded table then
                        loop (left - 1) (model |> step KyokuAdvanced)
                    else
                        loop (left - 1) (model |> step Advanced)

        loop 4000 model

    let private paifuOf (model: TableModel) : Paifu =
        Table.paifu (TablePage.rosterOf model) (tableOf model)

    let private roundTrip (paifu: Paifu) : Paifu =
        match Paifu.encoder paifu |> Encode.toString 0 |> Decode.fromString Paifu.decoder with
        | Ok decoded -> decoded
        | Error message -> failwith $"导出的牌谱应当读得动，却得到「{message}」"

    /// 事件流去掉开头那条 `start_game`——`Game.events` 里没有它（它的 names 来自配桌）。
    let private afterStartGame (events: Event list) : Event list =
        match events with
        | StartGame _ :: rest -> rest
        | other -> failwith $"牌谱的第一条应当是 start_game，却是 {List.tryHead other}"

    // ---- 决策记录 ----

    [<Fact>]
    let ``问过模型的每一手都留下一条记录，记全了输入、输出、思考与延迟`` () =
        let played = llmTable () |> playOut spoke
        let table = tableOf played

        Assert.NotEmpty(table.Decisions)

        for record in table.Decisions do
            Assert.Equal(seat 1, record.Seat)
            // **只存尾部**（票 31）：前缀在 `Prompting` 里整场一份，下一条用例盯它。
            Assert.Equal(spoke.PromptTail, record.PromptTail)
            Assert.Equal(spoke.RenderVersion, record.RenderVersion)
            Assert.Equal<int list>(spoke.ActionIds, record.ActionIds)
            Assert.Equal(spoke.Output, record.Output)
            Assert.Equal(spoke.Reason, record.Reason)
            Assert.Equal(spoke.Thinking, record.Thinking)
            Assert.Equal(spoke.Attempts, record.Attempts)
            Assert.Equal(spoke.LatencyMs, record.LatencyMs)
            // 模型自己决出来的一手不是兜底，采用的就是它选的那条。
            Assert.Equal(None, record.Fallback)
            Assert.Equal(spoke.ActionId, record.Applied)

    [<Fact>]
    let ``随机座位的手不产生记录，但手序编号不因此断裂`` () =
        let played = llmTable () |> playOut spoke
        let table = tableOf played
        let turns = table.Decisions |> List.map (fun record -> record.Turn)

        // 随机座位打了大半场：记录数远少于手数。
        Assert.True(List.length table.Decisions < table.Turns)
        // 编号严格递增、各不相同，且每一条都指得回这一场里的某一手。
        Assert.Equal<int list>(List.sort turns, turns)
        Assert.Equal(List.length turns, List.length (List.distinct turns))
        Assert.All(turns, fun turn -> Assert.InRange(turn, 0, table.Turns - 1))
        // 有跳号才说明「随机座位的手也占号」：连号意味着编号是按记录数发的。
        Assert.True(List.last turns > List.length turns - 1, "手序编号该按落定的手数走，不是按记录数")

    [<Fact>]
    let ``记录里的 applied 换得回那一手真正落定的动作`` () =
        // 一手一手走，每落定一手就拿刚记下的那条与牌桌上的「上一手」对照。
        // **牌谱里存的是 id 不是动作**，因此「id 换得回动作」这件事得有一条用例钉着。
        let rec loop (left: int) (checked': int) (model: TableModel) =
            if left <= 0 then
                checked'
            else
                match model.Awaiting with
                | None -> loop (left - 1) checked' (model |> step Advanced)
                | Some awaiting ->
                    let played = model |> step (Answered(awaiting.Ticket, spoke))
                    let table = tableOf played
                    let record = List.last table.Decisions

                    Assert.Equal(
                        table.Latest |> Option.map (fun turn -> turn.Action),
                        record.Applied
                        |> Option.bind (fun id -> DecisionPackage.tryAction id awaiting.Package)
                    )

                    loop (left - 1) (checked' + 1) played

        Assert.True(loop 120 0 (llmTable ()) >= 5, "这一段里模型该被问到好几次")

    [<Fact>]
    let ``兜底代打的那一手记着原因，也记着代打的是哪一条`` () =
        let asked = llmTable () |> step Advanced |> step Advanced

        let awaiting =
            match asked.Awaiting with
            | Some awaiting -> awaiting
            | None -> failwith "座位 1 这一手应当在等回执"

        let played = asked |> step (Answered(awaiting.Ticket, refused))
        let table = tableOf played
        let record = List.last table.Decisions

        Assert.Equal(refused.Failure, record.Fallback)
        Assert.Equal(1, Table.fallbacks table)
        // 代打的那条取自这一包，因此记录里的 id 换得回那个动作。
        Assert.Equal(
            Some(Fallback.action ScaffoldTier.Bare awaiting.Package),
            record.Applied
            |> Option.bind (fun id -> DecisionPackage.tryAction id awaiting.Package)
        )

    // ---- 导出与回放 ----

    [<Fact>]
    let ``导出的牌谱编码再解码，逐字段与原来相同`` () =
        let paifu = llmTable () |> playOut spoke |> paifuOf

        Assert.Equal(Paifu.version, paifu.Version)
        Assert.Equal(Ruleset.yonma, paifu.Ruleset)
        Assert.Equal(paifu, roundTrip paifu)

    [<Fact>]
    let ``牌谱第一条是 start_game，名字里有模型座位，没有 key`` () =
        let paifu = llmTable () |> playOut spoke |> paifuOf

        match List.tryHead paifu.Events with
        | Some(StartGame names) ->
            Assert.Equal<string list>([ "random"; "deepseek/deepseek-v4-flash"; "random"; "random" ], names)
        | other -> failwith $"第一条应当是 start_game，却是 {other}"

        // 可分享物里永远不能出现 API key（ADR-0003）。
        Assert.DoesNotContain(config.ApiKey, Paifu.encoder paifu |> Encode.toString 0)

    [<Fact>]
    let ``导出的事件流回放出同一个终局，逐条事件都相同`` () =
        let played = llmTable () |> playOut spoke
        let table = tableOf played
        let paifu = paifuOf played |> roundTrip

        match Replay.ofPaifu paifu with
        | Ok replayed ->
            Assert.Equal<Event list>(afterStartGame paifu.Events, Replayed.events replayed)

            match Table.result table, Replayed.result replayed with
            | Some original, Some folded ->
                Assert.Equal<int list>(original.Scores, folded.Scores)
                Assert.Equal<int list>(original.Juni, folded.Juni)
            | _ -> failwith "这一场该已经终局"
        | Error error -> failwith $"导出的牌谱应当回放得动，却得到「{ReplayError.toDisplay error}」"

    [<Fact>]
    let ``打到一半也导得出牌谱，照样回放得回去`` () =
        // 走 40 手就导出：分享一场没打完的对局是常事（M2 的 URL 分享）。
        let rec advance (left: int) (model: TableModel) =
            if left <= 0 then
                model
            else
                match model.Awaiting with
                | Some awaiting -> advance (left - 1) (model |> step (Answered(awaiting.Ticket, spoke)))
                | None -> advance (left - 1) (model |> step Advanced)

        let midway = llmTable () |> advance 40
        let paifu = paifuOf midway |> roundTrip

        Assert.False(Table.isKyokuEnded (tableOf midway), "这一局还没打完")
        Assert.NotEmpty(paifu.Decisions)

        match Replay.ofPaifu paifu with
        | Ok replayed -> Assert.Equal<Event list>(afterStartGame paifu.Events, Replayed.events replayed)
        | Error error -> failwith $"半场牌谱应当回放得动，却得到「{ReplayError.toDisplay error}」"

    [<Fact>]
    let ``全程兜底的那一场同样导得出、回放得回去`` () =
        let played = llmTable () |> playOut refused
        let table = tableOf played
        let paifu = paifuOf played |> roundTrip

        Assert.True(Table.fallbacks table > 0, "断电演习里必然有兜底代打的手")
        Assert.All(paifu.Decisions, fun record -> Assert.True(Option.isSome record.Fallback))

        match Replay.ofPaifu paifu with
        | Ok replayed -> Assert.Equal<Event list>(afterStartGame paifu.Events, Replayed.events replayed)
        | Error error -> failwith $"这份牌谱应当回放得动，却得到「{ReplayError.toDisplay error}」"

    // ---- 分享路径：thinking 省得掉 ----

    [<Fact>]
    let ``URL 分享省掉 thinking，其余照旧，且由同一个解码器读回来`` () =
        let paifu = llmTable () |> playOut spoke |> paifuOf
        let shared = Paifu.stripThinking paifu

        Assert.Contains("先数向听", Paifu.encoder paifu |> Encode.toString 0)
        Assert.DoesNotContain("先数向听", Paifu.encoder shared |> Encode.toString 0)
        // 同一个解码器：省掉 thinking 的牌谱不是另一个类型。
        Assert.Equal(shared, roundTrip shared)
        Assert.Equal<Event list>(paifu.Events, (roundTrip shared).Events)
