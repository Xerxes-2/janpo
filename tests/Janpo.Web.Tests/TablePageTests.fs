namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// LLM 座位的驱动（票 23）：**牌桌怎么被一条异步回执推着走**。
///
/// 这一层完全不碰网络与 TS——Agent 层的回执在这里是一个值（`AgentAnswer`），
/// 因此「兜底闭环」与「对局永不卡死」在 dotnet 上就验得完。
module TablePageTests =

    let private config: LlmSeat =
        {
            Provider = "deepseek"
            Model = "deepseek-v4-flash"
            ApiKey = "sk-测试用的假 key"
            TimeoutMs = 12000
            Thinking = Thinking.Off
            Tier = ScaffoldTier.Bare
        }

    /// 座位 0 交给 LLM 的一桌（开局第一手就是它：亲摸完牌等着打）。
    let private llmTable () : TableModel =
        TablePage.initial (Some Seat.first) config |> fst

    let private tableOf (model: TableModel) : Table =
        match model.Table with
        | Ok table -> table
        | Error error -> failwith $"这一桌应当开得起来，却得到「{error}」"

    let private awaitingOf (model: TableModel) : Awaiting =
        match model.Awaiting with
        | Some awaiting -> awaiting
        | None -> failwith "这一手应当在等 Agent 层的回执"

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    /// 模型好好答话：选 `id`。
    let private chose (id: int) : AgentAnswer =
        {
            ActionId = Some id
            Reason = Some "就它了"
            Failure = None
            Attempts = 1
            LatencyMs = 640
            Prompt = "东1局 0 本场……（prompt 全文）"
            Tools = """{"name":"choose_action"}"""
            Output = """{"stop_reason":"toolUse"}"""
            Thinking = Some "先数向听……"
        }

    /// 模型交不出来（超时 / provider 报错 / 格式跑偏，走的都是这一条）。
    let private refused: AgentAnswer =
        {
            ActionId = None
            Reason = None
            Failure = Some "模型超时（重试 2 次仍无结果）"
            Attempts = 3
            LatencyMs = 91000
            Prompt = "东1局 0 本场……（prompt 全文）"
            Tools = """{"name":"choose_action"}"""
            Output = ""
            Thinking = None
        }

    // ---- 分派 ----

    [<Fact>]
    let ``随机座位当场落子，LLM 座位改成发一个请求出去`` () =
        let random = TablePage.initial None config |> fst |> step Advanced
        let llm = llmTable () |> step Advanced

        Assert.True((tableOf random).Latest |> Option.isSome)
        Assert.True(Option.isNone random.Awaiting)

        // LLM 那一桌：这一手还没落，牌桌停在原地等回执。
        Assert.True((tableOf llm).Latest |> Option.isNone)
        Assert.Equal(AgentStatus.Asking Seat.first, llm.Agent)
        Assert.Equal(Seat.first, DecisionPackage.seat (awaitingOf llm).Package)

    [<Fact>]
    let ``等回执的那段不再问第二次：同一手不许有两个请求在飞`` () =
        let asked = llmTable () |> step Advanced
        let again = asked |> step Advanced |> step (Ticked asked.Playback.Generation)

        Assert.Equal(asked.Ticket, again.Ticket)
        Assert.Equal((awaitingOf asked).Ticket, (awaitingOf again).Ticket)

    // ---- 回执 → 落子 ----

    [<Fact>]
    let ``模型选的 id 换回那个动作，落进引擎`` () =
        let asked = llmTable () |> step Advanced
        let awaiting = awaitingOf asked
        let expected = DecisionPackage.tryAction 1 awaiting.Package

        let played = asked |> step (Answered(awaiting.Ticket, chose 1))
        let table = tableOf played

        Assert.Equal(expected, table.Latest |> Option.map (fun turn -> turn.Action))
        // 自己决出来的一手不带兜底记号。
        Assert.Equal(None, table.Latest |> Option.bind (fun turn -> turn.Fallback))
        Assert.Equal(0, Table.fallbacks table)
        Assert.True(Option.isNone played.Awaiting)
        Assert.Equal(AgentStatus.Spoke(Seat.first, Some "就它了", 640), played.Agent)

    [<Fact>]
    let ``交不出来就兜底代打，而且牌桌上看得出来`` () =
        let asked = llmTable () |> step Advanced
        let awaiting = awaitingOf asked
        let expected = Fallback.action ScaffoldTier.Bare awaiting.Package

        let played = asked |> step (Answered(awaiting.Ticket, refused))
        let table = tableOf played

        Assert.Equal(Some expected, table.Latest |> Option.map (fun turn -> turn.Action))
        Assert.Equal(refused.Failure, table.Latest |> Option.bind (fun turn -> turn.Fallback))
        Assert.Equal(1, Table.fallbacks table)
        Assert.Equal(AgentStatus.Troubled(Seat.first, "模型超时（重试 2 次仍无结果）"), played.Agent)

    [<Fact>]
    let ``兜底按座位自己那一档代打`` () =
        // 档位是座位级配置（票 24），它一路跟到兜底。
        // **种子 42 的开局第一手两档不同**：刚摸进的 7p 让手牌进了一步，摸切就是退向，
        // 因此 Bare 摸切而 Assisted 改打一张不退向听的——档位传错了这条就红。
        let start (tier: ScaffoldTier) =
            TablePage.initial (Some Seat.first) { config with Tier = tier }
            |> fst
            |> step (TableMsg.SeedEdited "42")
            |> step TableMsg.Restarted
            |> step Advanced

        let asked = start ScaffoldTier.Assisted
        let awaiting = awaitingOf asked
        Assert.Equal(ScaffoldTier.Assisted, awaiting.Config.Tier)

        let played = asked |> step (Answered(awaiting.Ticket, refused))
        let action = (tableOf played).Latest |> Option.map (fun turn -> turn.Action)

        Assert.Equal(Some(Fallback.action ScaffoldTier.Assisted awaiting.Package), action)

        let bare = start ScaffoldTier.Bare
        let bareAwaiting = awaitingOf bare
        let barePlayed = bare |> step (Answered(bareAwaiting.Ticket, refused))

        Assert.NotEqual(action, (tableOf barePlayed).Latest |> Option.map (fun turn -> turn.Action))

    [<Fact>]
    let ``越界的 id 走同一条兜底路：tryAction 是第二道闸`` () =
        let asked = llmTable () |> step Advanced
        let awaiting = awaitingOf asked
        let expected = Fallback.action ScaffoldTier.Bare awaiting.Package

        // Agent 层自己校过一道 id，这条是「两边看法分了岔」时的兜底。
        let played = asked |> step (Answered(awaiting.Ticket, chose 9999))
        let table = tableOf played

        Assert.Equal(Some expected, table.Latest |> Option.map (fun turn -> turn.Action))
        Assert.True(table.Latest |> Option.bind (fun turn -> turn.Fallback) |> Option.isSome)
        Assert.Equal(1, Table.fallbacks table)

    // ---- 过期的回执 ----

    [<Fact>]
    let ``票号对不上的回执一律丢掉`` () =
        let asked = llmTable () |> step Advanced
        let stale = asked |> step (Answered((awaitingOf asked).Ticket + 1, chose 0))

        Assert.True((tableOf stale).Latest |> Option.isNone)
        Assert.True(Option.isSome stale.Awaiting)

    [<Fact>]
    let ``重开一桌之后，旧回执落不进新牌桌`` () =
        let asked = llmTable () |> step Advanced
        let awaiting = awaitingOf asked
        let restarted = asked |> step Restarted

        Assert.True(Option.isNone restarted.Awaiting)
        Assert.Equal(AgentStatus.Idle, restarted.Agent)

        let late = restarted |> step (Answered(awaiting.Ticket, chose 0))
        Assert.True((tableOf late).Latest |> Option.isNone)

    // ---- 断电演习 ----

    [<Fact>]
    let ``模型一次都答不上话，这一局照样打得完（全程兜底）`` () =
        // 页面上的断电演习（故意配坏 key）在浏览器里跑；这里把「每一次都交不出来」
        // 当成值喂进 update，验的是同一件事：**对局永不卡死**。
        let rec loop (left: int) (model: TableModel) =
            match model.Awaiting with
            | Some awaiting when left > 0 -> loop (left - 1) (model |> step (Answered(awaiting.Ticket, refused)))
            | _ when left <= 0 -> failwith "这一局在预算内没打完"
            | _ ->
                match Table.pending (tableOf model) with
                | Some _ -> loop (left - 1) (model |> step Advanced)
                | None -> model

        let ended = loop 800 (llmTable ())
        let table = tableOf ended

        Assert.True(Table.isKyokuEnded table)
        Assert.True(Option.isNone table.Fault)
        // 座位 0 的每一手都是兜底代打的，因此这个数必然大于 0。
        Assert.True(Table.fallbacks table > 0, "断电演习里必然有兜底代打的手")

        match ended.Agent with
        | AgentStatus.Troubled(seat, _) -> Assert.Equal(Seat.first, seat)
        | other -> failwith $"全程兜底之后状态该是 Troubled，却是 {other}"

    // ---- 配置 ----

    [<Fact>]
    let ``改配置面板不动牌桌`` () =
        let asked = llmTable () |> step Advanced
        let edited = asked |> step (LlmEdited(LlmField.Model, "deepseek-v4"))

        Assert.Equal("deepseek-v4", edited.Llm.Model)
        Assert.Equal<Event list>(GameState.events (tableOf asked).State, GameState.events (tableOf edited).State)
