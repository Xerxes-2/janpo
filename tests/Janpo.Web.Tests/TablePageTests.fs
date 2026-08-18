namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// LLM 座位的驱动（票 23）：**牌桌怎么被一条异步回执推着走**。
///
/// 这一层完全不碰网络与 TS——Agent 层的回执在这里是一个值（`AgentAnswer`），
/// 因此「兜底闭环」与「对局永不卡死」在 dotnet 上就验得完。
module TablePageTests =

    /// 库里那一份档案（key 是假的，一眼看得出）。**「怎么问这个模型」那六格全在它里面**，
    /// 座位级那三项（脚手架 / 人格 / 模板）在绑定上（票 73）。
    let private profile: ModelProfile =
        {
            Name = "凶狠的老张"
            Provider = "deepseek"
            Model = "deepseek-v4-flash"
            BaseUrl = ""
            ApiKey = "sk-测试用的假 key"
            TimeoutMs = 12000
            Thinking = Thinking.Off
        }

    /// 四家自带 bot、库里摆着那一份档案的坐法。
    let private bots: SeatingPlan =
        { SeatingPlan.initial Ruleset.yonma with
            Profiles = [ profile ]
        }

    /// 把这几席交给那份档案。
    let private llmAt (seats: Seat list) (plan: SeatingPlan) : SeatingPlan =
        (plan, seats)
        ||> List.fold (fun plan seat -> plan |> SeatingPlan.bind seat (SeatChoice.Profile profile.Name))

    /// 座位 0 交给 LLM 的一桌（开局第一手就是它：亲摸完牌等着打）。
    let private llmTable () : TableModel =
        TablePage.initial RulesetDraft.initial (bots |> llmAt [ Seat.first ]) |> fst

    /// 这一桌的 Live 那一半（票 71）。**这一整个模块跑的都是 `?table=1` 那一页**：
    /// 配桌、模型座席与 Agent 层的驱动只属于 Live，回放那一侧根本没有它们。
    let private liveOf (model: TableModel) : LiveTable =
        match TablePage.live model with
        | Some live -> live
        | None -> failwith "这几条用例跑的是 `?table=1` 那一页，它必然是 Live"

    let private tableOf (model: TableModel) : Table =
        match (liveOf model).Table with
        | Ok table -> table
        | Error error -> failwith $"这一桌应当开得起来，却得到「{error}」"

    let private awaitingOf (model: TableModel) : Awaiting =
        match (liveOf model).Awaiting with
        | Some awaiting -> awaiting
        | None -> failwith "这一手应当在等 Agent 层的回执"

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    let private rosterOf (model: TableModel) : Roster =
        match TablePage.rosterOf model with
        | Some roster -> roster
        | None -> failwith "Live 那一桌必然有配桌"

    /// 这一桌那一席模型此刻**真正会用的**那份配置（配桌是每一手现推导的）。
    let private llmConfigOf (model: TableModel) : LlmSeat =
        match Roster.llmSeats (rosterOf model) with
        | [ (_, config) ] -> config
        | other -> failwith $"这一桌应当正好坐着一席模型，却有 {List.length other} 席"

    /// 模型好好答话：选 `id`。
    let private chose (id: int) : AgentAnswer =
        {
            ActionId = Some id
            Reason = Some "就它了"
            Failure = None
            Attempts = 1
            LatencyMs = 640
            PromptTail = "【现在】东1局 0 本场……\n【可选动作】只能从下面这些 id 里选一个：\n- id=0：摸切1索"
            Preamble = "你在打日本立直麻将（天凤规则，四人东）……"
            RenderVersion = "janpo-default@08fcaec3.4b9e57c0"
            Tools = """[{"name":"choose_action","parameters":{"properties":{"action_id":{"enum":[]}}}}]"""
            ActionIds = [ 0 ]
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

    /// 模型交不出来（超时 / provider 报错 / 格式跑偏，走的都是这一条）。
    let private refused: AgentAnswer =
        {
            ActionId = None
            Reason = None
            Failure = Some "模型超时（重试 2 次仍无结果）"
            Attempts = 3
            LatencyMs = 91000
            PromptTail = "【现在】东1局 0 本场……\n【可选动作】只能从下面这些 id 里选一个：\n- id=0：摸切1索"
            Preamble = "你在打日本立直麻将（天凤规则，四人东）……"
            RenderVersion = "janpo-default@08fcaec3.4b9e57c0"
            Tools = """[{"name":"choose_action","parameters":{"properties":{"action_id":{"enum":[]}}}}]"""
            ActionIds = [ 0 ]
            Output = ""
            Thinking = None
            // 一次都没问成：没有账单可记。
            Usage = None
        }

    /// 把这一局打完：轮到模型就喂 `answer`，其余座位随机。
    let private playKyoku (answer: AgentAnswer) (model: TableModel) : TableModel =
        let rec loop (left: int) (model: TableModel) =
            match (liveOf model).Awaiting with
            | Some awaiting when left > 0 -> loop (left - 1) (model |> step (Answered(awaiting.Ticket, answer)))
            | _ when left <= 0 -> failwith "这一局在预算内没打完"
            | _ ->
                match Table.pending (tableOf model) with
                | Some _ -> loop (left - 1) (model |> step Advanced)
                | None -> model

        loop 800 model

    /// 推到模型被问到那一手，喂一条回执就停。
    let private askOnce (answer: AgentAnswer) (model: TableModel) : TableModel =
        let rec loop (left: int) (model: TableModel) =
            match (liveOf model).Awaiting with
            | Some awaiting -> model |> step (Answered(awaiting.Ticket, answer))
            | None when left <= 0 -> failwith "这一段里模型应当被问到一次"
            | None -> loop (left - 1) (model |> step Advanced)

        loop 200 model

    // ---- 分派 ----

    [<Fact>]
    let ``随机座位当场落子，LLM 座位改成发一个请求出去`` () =
        let random = TablePage.initial RulesetDraft.initial bots |> fst |> step Advanced

        let llm = llmTable () |> step Advanced

        Assert.True((tableOf random).Latest |> Option.isSome)
        Assert.True(Option.isNone (liveOf random).Awaiting)

        // LLM 那一桌：这一手还没落，牌桌停在原地等回执。
        Assert.True((tableOf llm).Latest |> Option.isNone)
        Assert.Equal(AgentStatus.Asking Seat.first, (liveOf llm).Agent)
        Assert.Equal(Seat.first, DecisionPackage.seat (awaitingOf llm).Package)

    [<Fact>]
    let ``等回执的那段不再问第二次：同一手不许有两个请求在飞`` () =
        let asked = llmTable () |> step Advanced
        let again = asked |> step Advanced |> step (Ticked asked.Playback.Generation)

        Assert.Equal((liveOf asked).Ticket, (liveOf again).Ticket)
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
        Assert.True(Option.isNone (liveOf played).Awaiting)
        Assert.Equal(AgentStatus.Spoke(Seat.first, Some "就它了", 640), (liveOf played).Agent)

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
        Assert.Equal(AgentStatus.Troubled(Seat.first, "模型超时（重试 2 次仍无结果）"), (liveOf played).Agent)

    [<Fact>]
    let ``兜底按座位自己那一档代打`` () =
        // 档位是座位级配置（票 24），它一路跟到兜底。
        // **种子 42 的开局第一手两档不同**：刚摸进的 7p 让手牌进了一步，摸切就是退向，
        // 因此 Bare 摸切而 Assisted 改打一张不退向听的——档位传错了这条就红。
        let start (tier: ScaffoldTier) =
            TablePage.initial
                RulesetDraft.initial
                (bots
                 |> llmAt [ Seat.first ]
                 |> SeatingPlan.editSeat Seat.first SeatField.Tier (ScaffoldTier.toWire tier))
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
        Assert.True(Option.isSome (liveOf stale).Awaiting)

    [<Fact>]
    let ``重开一桌之后，旧回执落不进新牌桌`` () =
        let asked = llmTable () |> step Advanced
        let awaiting = awaitingOf asked
        let restarted = asked |> step Restarted

        Assert.True(Option.isNone (liveOf restarted).Awaiting)
        Assert.Equal(AgentStatus.Idle, (liveOf restarted).Agent)

        let late = restarted |> step (Answered(awaiting.Ticket, chose 0))
        Assert.True((tableOf late).Latest |> Option.isNone)

    // ---- 断电演习 ----

    [<Fact>]
    let ``模型一次都答不上话，这一局照样打得完（全程兜底）`` () =
        // 页面上的断电演习（故意配坏 key）在浏览器里跑；这里把「每一次都交不出来」
        // 当成值喂进 update，验的是同一件事：**对局永不卡死**。
        let ended = llmTable () |> playKyoku refused
        let table = tableOf ended

        Assert.True(Table.isKyokuEnded table)
        Assert.True(Option.isNone table.Fault)
        // 座位 0 的每一手都是兜底代打的，因此这个数必然大于 0。
        Assert.True(Table.fallbacks table > 0, "断电演习里必然有兜底代打的手")

        match (liveOf ended).Agent with
        | AgentStatus.Troubled(seat, _) -> Assert.Equal(Seat.first, seat)
        | other -> failwith $"全程兜底之后状态该是 Troubled，却是 {other}"

    // ---- 配置 ----

    [<Fact>]
    let ``改配置面板不动牌桌`` () =
        let asked = llmTable () |> step Advanced
        let edited = asked |> step (ProfileEdited(ProfileField.Model, "deepseek-v4"))

        Assert.Equal("deepseek-v4", (SeatingPlan.profileAt 0 (liveOf edited).Seating).Value.Model)
        Assert.Equal<Event list>(GameState.events (tableOf asked).State, GameState.events (tableOf edited).State)

    // ---- 人格与模板：一局内不变（票 46；术语表的 `Persona` 词条） ----

    /// 拿它当新人格：两条用例共用同一句，断言里不再各写各的。
    let private newPersona = "你是一位以防守见长的雀士，宁可少和一把，也不点炮。"

    /// 换了人格之后 Agent 层给回来的就是另一份 preamble 与另一个渲染版本。
    let private rendered (preamble: string) (version: string) (answer: AgentAnswer) : AgentAnswer =
        { answer with
            Preamble = preamble
            RenderVersion = version
        }

    [<Fact>]
    let ``一局问过话之后再改人格，本局仍然发定型那一版`` () =
        // 术语表：`Persona` **一局内不变**（否则废掉可缓存前缀，还让同一局面的对照多出一个变量）。
        let asked = llmTable () |> step Advanced
        let edited = asked |> step (SeatEdited(Seat.first, SeatField.Persona, newPersona))

        // 面板上照收（localStorage 也照存）——这一条不是把那两格锁死。
        Assert.Equal(newPersona, (SeatingPlan.bindingAt Seat.first (liveOf edited).Seating).Persona)
        // 但本局发出去的仍是定型那一版。
        Assert.Equal("", (llmConfigOf edited).Persona)
        Assert.Equal("", (awaitingOf edited).Config.Persona)

        // 下一手也一样：一局之内每一手都是同一份前缀。
        let nextHand =
            edited
            |> step (Answered((awaitingOf edited).Ticket, refused))
            |> askOnce refused

        Assert.Equal("", (llmConfigOf nextHand).Persona)

    [<Fact>]
    let ``模板同理：一局内改不动，开下一局才生效`` () =
        let overrides = """{"id":"我的模板"}"""
        let asked = llmTable () |> step Advanced
        let edited = asked |> step (SeatEdited(Seat.first, SeatField.Template, overrides))

        Assert.Equal("", (llmConfigOf edited).Template)

        let nextKyoku = edited |> playKyoku refused |> step KyokuAdvanced

        Assert.Equal(overrides, (llmConfigOf nextKyoku).Template)

    [<Fact>]
    let ``改过的人格在面板上看得见“下一局生效”`` () =
        let asked = llmTable () |> step Advanced

        // 还没改之前没有这句话。
        Assert.False(TablePage.renderingPending asked)

        let edited = asked |> step (SeatEdited(Seat.first, SeatField.Persona, newPersona))
        Assert.True(TablePage.renderingPending edited)

        // 开了下一局就不欠着了。
        let nextKyoku = edited |> playKyoku refused |> step KyokuAdvanced
        Assert.False(TablePage.renderingPending nextKyoku)
        Assert.Equal(newPersona, (llmConfigOf nextKyoku).Persona)

    [<Fact>]
    let ``局间换人格：牌谱里两版 preamble 都在，各自记着自己的渲染版本`` () =
        // `Paifu.Preamble` 本来就是为这件事准备的（按「座位 + 渲染版本」去重）：
        // 一局内不得变，但局间换得动，而换了之后两版都要在牌谱里找得回来。
        let firstKyoku =
            llmTable ()
            |> playKyoku (refused |> rendered "第一版人格：你在打日本立直麻将……" "janpo-default@aaaaaaaa")

        let secondKyoku =
            firstKyoku
            |> step (SeatEdited(Seat.first, SeatField.Persona, newPersona))
            |> step KyokuAdvanced
            |> askOnce (refused |> rendered "第二版人格：你在打日本立直麻将……" "janpo-default@bbbbbbbb")

        let preambles = (tableOf secondKyoku).Prompting.Preambles

        Assert.Equal(2, List.length preambles)
        Assert.All(preambles, fun preamble -> Assert.Equal(Seat.first, preamble.Seat))

        Assert.Equal<string list>(
            [ "janpo-default@aaaaaaaa"; "janpo-default@bbbbbbbb" ],
            preambles |> List.map (fun preamble -> preamble.RenderVersion)
        )

        Assert.Equal(
            Some "第一版人格：你在打日本立直麻将……",
            Prompting.preambleFor Seat.first "janpo-default@aaaaaaaa" (tableOf secondKyoku).Prompting
        )

        Assert.Equal(
            Some "第二版人格：你在打日本立直麻将……",
            Prompting.preambleFor Seat.first "janpo-default@bbbbbbbb" (tableOf secondKyoku).Prompting
        )

    // ---- 危险度的显示开关（票 25） ----

    [<Fact>]
    let ``牌桌上的危险度默认关，拨一下就开`` () =
        // 「围观者也想看」，但它不是牌桌本来就该摆着的东西——票里写死了默认关。
        let closed = llmTable ()
        Assert.False(closed.ShowDanger)

        let opened = closed |> step DangerToggled
        Assert.True(opened.ShowDanger)
        Assert.False((opened |> step DangerToggled).ShowDanger)

    [<Fact>]
    let ``拨危险度不动牌局：它只是个显示开关`` () =
        let asked = llmTable () |> step Advanced
        let toggled = asked |> step DangerToggled

        Assert.Equal<Event list>(GameState.events (tableOf asked).State, GameState.events (tableOf toggled).State)
        Assert.Equal((liveOf asked).Ticket, (liveOf toggled).Ticket)

        Assert.Equal(
            (liveOf asked).Awaiting |> Option.map (fun awaiting -> awaiting.Ticket),
            (liveOf toggled).Awaiting |> Option.map (fun awaiting -> awaiting.Ticket)
        )

    // ---- 自带 bot 的选择（票 42） ----

    [<Fact>]
    let ``自带 bot 默认是均匀随机：默认视图那几道闸门量的仍是它`` () =
        // 曳光弹对拍、牌谱导出、副露来源那几道闸门跑的都是默认那一桌
        // （票 42 的边界：换默认值会让它们量到另一个选手）。
        let model = TablePage.initial RulesetDraft.initial bots |> fst

        Assert.Equal<string list>(
            [
                for _ in Seat.all Ruleset.yonma -> SeatChoice.toWire (SeatChoice.Bot Bot.Uniform)
            ],
            (liveOf model).Seating.Seats
            |> List.map (fun binding -> SeatChoice.toWire binding.Choice)
        )

        Assert.Equal<string list>([ "random"; "random"; "random"; "random" ], Roster.names (rosterOf model))

    [<Fact>]
    let ``拨成有主见的：配桌换人，牌谱里的名字跟着换`` () =
        let picked =
            (TablePage.initial RulesetDraft.initial bots |> fst, Seat.all Ruleset.yonma)
            ||> List.fold (fun model seat -> model |> step (SeatBound(seat, SeatChoice.Bot Bot.Opinionated)))

        Assert.Equal<string list>(
            [ "opinionated"; "opinionated"; "opinionated"; "opinionated" ],
            Roster.names (rosterOf picked)
        )

    [<Fact>]
    let ``模型坐席与自带 bot 是两个维度：一席仍是模型，其余三席换人`` () =
        let picked =
            (llmTable (), [ 1; 2; 3 ])
            ||> List.fold (fun model index ->
                match Seat.ofIndex index with
                | Some seat -> model |> step (SeatBound(seat, SeatChoice.Bot Bot.Opinionated))
                | None -> failwith "1/2/3 都是合法座位")

        Assert.Equal<string list>(
            [ "deepseek/deepseek-v4-flash"; "opinionated"; "opinionated"; "opinionated" ],
            Roster.names (rosterOf picked)
        )

        // 换 bot 不动牌局：它只改「下一手谁来决策」。
        let asked = llmTable () |> step Advanced

        let toggled =
            match Seat.ofIndex 1 with
            | Some seat -> asked |> step (SeatBound(seat, SeatChoice.Bot Bot.Opinionated))
            | None -> failwith "1 是合法座位"

        Assert.Equal<Event list>(GameState.events (tableOf asked).State, GameState.events (tableOf toggled).State)

    [<Fact>]
    let ``有主见的那一桌照样打得完一局，且真的走出了立直`` () =
        // 它坐得上牌桌这件事要有证据：一局打到底、零卡死，而且那一局里出了立直
        // ——牌桌那条路与 `Soak` 那条路走的是同一个选手。
        let rec loop (left: int) (model: TableModel) =
            match Table.pending (tableOf model) with
            | None -> model
            | Some _ when left <= 0 -> failwith "这一局在预算内没打完"
            | Some _ -> loop (left - 1) (model |> step Advanced)

        let played =
            ((TablePage.initial RulesetDraft.initial bots |> fst), Seat.all Ruleset.yonma)
            ||> List.fold (fun model seat -> model |> step (SeatBound(seat, SeatChoice.Bot Bot.Opinionated)))
            |> loop 400

        let table = tableOf played

        Assert.True(Table.isKyokuEnded table)
        Assert.True(Option.isNone table.Fault)

        let riichi =
            GameState.events table.State
            |> List.filter (fun event ->
                match event with
                | RiichiAccepted _ -> true
                | _ -> false)

        Assert.NotEmpty riichi

    // ---- 四 LLM 同桌（票 73） ----

    /// 四席全交给同一份档案的一桌。
    let private fourLlm () : TableModel =
        TablePage.initial RulesetDraft.initial (bots |> llmAt (Seat.all Ruleset.yonma))
        |> fst

    /// 把这一局打完：轮到谁就按 `answerFor` 给的那条回执答。
    let private playKyokuBy (answerFor: Seat -> AgentAnswer) (model: TableModel) : TableModel =
        let rec loop (left: int) (model: TableModel) =
            match (liveOf model).Awaiting with
            | Some awaiting when left > 0 ->
                let seat = DecisionPackage.seat awaiting.Package
                loop (left - 1) (model |> step (Answered(awaiting.Ticket, answerFor seat)))
            | _ when left <= 0 -> failwith "这一局在预算内没打完"
            | _ ->
                match Table.pending (tableOf model) with
                | Some _ -> loop (left - 1) (model |> step Advanced)
                | None -> model

        loop 1200 model

    /// 这一席在这一局里兜底代打了几手。
    let private fallbacksAt (seat: Seat) (table: Table) : int =
        table.Decisions
        |> List.sumBy (fun record ->
            if record.Seat = seat && Option.isSome record.Fallback then
                1
            else
                0)

    [<Fact>]
    let ``四家都可以是模型，牌谱里四个名字都是它`` () =
        let model = fourLlm ()

        Assert.Equal<string list>(
            [ for _ in Seat.all Ruleset.yonma -> "deepseek/deepseek-v4-flash" ],
            Roster.names (rosterOf model)
        )

        // 「这一席拿的是哪一份配置」只有一处推导（票 74 与 76 读的也是它）。
        for seat in Seat.all Ruleset.yonma do
            match TablePage.seatConfigOf seat model with
            | Some config -> Assert.Equal(profile.ApiKey, config.ApiKey)
            | None -> failwith $"座位 {Seat.index seat} 该是模型"

    [<Fact>]
    let ``四家都是模型时仍旧一次只问一席：并发是票 74`` () =
        // 这一票不许改「一次一份」那个形态：`Awaiting` 恒是一条，回执落定之后才问下一家。
        let asked = fourLlm () |> step Advanced
        let first = awaitingOf asked

        // 再单步也不会多出第二个在飞的请求。
        let again = asked |> step Advanced
        Assert.Equal(first.Ticket, (awaitingOf again).Ticket)
        Assert.Equal((liveOf asked).Ticket, (liveOf again).Ticket)

        // 答完这一手才轮到下一家（这一局里四家都被问到过）。
        let played = fourLlm () |> playKyokuBy (fun _ -> chose 0)

        let asked =
            (tableOf played).Decisions
            |> List.map (fun record -> record.Seat)
            |> List.distinct

        Assert.Equal<Seat list>(Seat.all Ruleset.yonma, List.sortBy Seat.index asked)

    [<Fact>]
    let ``人格一局内不变按座位各自成立：定住一席不定住别席`` () =
        // 术语表那条不变量（`Persona` 一局内不变）在四席同桌之后要**按席**成立：
        // 座位 0 被问过话，不该把座位 1 的人格一并定死——那一席本局可能还没开过口。
        let asked = fourLlm () |> step Advanced
        let spoke = DecisionPackage.seat (awaitingOf asked).Package
        let silent = Seat.shimocha Ruleset.yonma spoke

        let edited =
            asked
            |> step (SeatEdited(spoke, SeatField.Persona, "改过的：说话那一席"))
            |> step (SeatEdited(silent, SeatField.Persona, "改过的：还没说话那一席"))

        // 说过话那一席：本局仍发定型那一版（空人格）。
        Assert.Equal(Some "", TablePage.seatConfigOf spoke edited |> Option.map (fun config -> config.Persona))
        // 还没说话那一席：改了当场生效。
        Assert.Equal(
            Some "改过的：还没说话那一席",
            TablePage.seatConfigOf silent edited
            |> Option.map (fun config -> config.Persona)
        )

        // 开下一局，两席一起松开。
        let nextKyoku = edited |> playKyokuBy (fun _ -> refused) |> step KyokuAdvanced

        Assert.Equal(
            Some "改过的：说话那一席",
            TablePage.seatConfigOf spoke nextKyoku
            |> Option.map (fun config -> config.Persona)
        )

    [<Fact>]
    let ``断电演习扩到多席：一席全兜底，其余席照样把这一局打完`` () =
        // 页面上的断电演习（那一席配一把坏 key）在浏览器里跑；这里把「那一席每次都交不出来」
        // 当成值喂进 update，验的是同一件事：**兜底计数只涨在那一席，而对局照样走到底**。
        let broken = Seat.shimocha Ruleset.yonma Seat.first

        let ended =
            fourLlm ()
            |> playKyokuBy (fun seat -> if seat = broken then refused else chose 0)

        let table = tableOf ended

        Assert.True(Table.isKyokuEnded table)
        Assert.True(Option.isNone table.Fault)
        Assert.True(fallbacksAt broken table > 0, "配坏那一席必然有兜底代打的手")

        for seat in Seat.all Ruleset.yonma do
            if seat <> broken then
                Assert.Equal(0, fallbacksAt seat table)

        // 其余三席真的被问到过（否则「只涨在那一席」是空转的）。
        for seat in Seat.all Ruleset.yonma do
            if seat <> broken then
                Assert.Contains(table.Decisions, fun record -> record.Seat = seat)

    [<Fact>]
    let ``删掉一份还被座位引用的档案：那几席退回 bot，页面把这件事说出来`` () =
        let model = fourLlm () |> step (ProfileDeleted 0)
        let live = liveOf model

        Assert.Equal<string list>([ "random"; "random"; "random"; "random" ], Roster.names (rosterOf model))

        match live.Notice with
        | None -> failwith "删掉一份还被引用的档案，页面必须说出来（不许静静地变成「没有选手」）"
        | Some said ->
            Assert.Contains(profile.Name, said)
            Assert.Contains("座位", said)
