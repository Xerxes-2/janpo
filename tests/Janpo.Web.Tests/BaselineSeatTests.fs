namespace Janpo.Web.Tests

open Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// **强 AI 基线坐一席**（票 92；ADR-0006，spec 的 story 35）。
///
/// 这一层钉的是**不必开浏览器就钉得住的那几条**：
///
/// 1. **它是第四种选手**：`SeatChoice.Baseline` → `SeatPlayer.Baseline` → 牌谱里那一列写
///    `baseline`，与 bot / 模型 / 真人都分得开，四席怎么混都行；
/// 2. **一个字节都不许拉**（ADR-0006 边界 1）：不选它的那一桌**连那条拉资产的 Cmd 都不发**
///    ——这一层量的是「有没有发出去」，浏览器那一侧量的是「网络请求计数为 0」；
/// 3. **降级不是 try/catch**（边界 2）：资产拉不动时那一席当场退回自带 bot、页面说得出原因，
///    而**其余席照常把一整场打完**（这一条在这里真打一场，不是断言一个标志位）；
/// 4. **它不会说话**：它出的每一手都**不留决策记录**，因此气泡与 token 账单在结构上就为空
///    ——不是显示一个空气泡或者「0 tok」（票 92 的要害）。
///
/// 真跑那份 wasm 在浏览器那一侧、且**只在本机演习那一档**
/// （`web/scripts/verify-baseline.mjs --asset`；那几 MB 不入版本控制）。
/// CI 因此覆盖不到「它真出的那一手对不对」——写在报告 92 里。
module BaselineSeatTests =

    let private seat (index: int) : Seat =
        match Seat.ofIndex index with
        | Some seat -> seat
        | None -> failwith $"{index} 应当是合法座位"

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    let private effects (message: TableMsg) (model: TableModel) : int =
        TablePage.update message model |> snd |> List.length

    /// 强 AI 基线坐第 `index` 席、其余三家均匀随机的那一桌（`?table=1` 的默认种子）。
    let private baselineAt (index: int) =
        SeatingPlan.initial Ruleset.yonma
        |> SeatingPlan.bind (seat index) SeatChoice.Baseline
        |> TablePage.initial RulesetDraft.initial

    let private liveOf (model: TableModel) : LiveTable =
        match TablePage.live model with
        | Some live -> live
        | None -> failwith "这几条用例跑的是 `?table=1` 那一页，它必然是 Live"

    let private tableOf (model: TableModel) : Table =
        match (liveOf model).Table with
        | Ok table -> table
        | Error error -> failwith $"这一桌应当开得起来，却得到「{error}」"

    /// 拉到了资产、并往前推一步（于是那一席被问出去了）。
    /// **开桌那一下在这里是 `Advanced`**：页面默认暂停（`Playback.initial`），
    /// 真浏览器里是人按「单步」或者「播放」那一下。
    let private askedAt (index: int) : TableModel =
        baselineAt index |> fst |> step (BaselineLoaded(Ok 6039832)) |> step Advanced

    /// 此刻正在等它那一手的那一份。
    let private consultOf (model: TableModel) : Consult =
        match (liveOf model).Consulting with
        | [ consult ] -> consult
        | other -> failwith $"该正好在等它那一手，却有 {List.length other} 份"

    // ---- 它是第四种选手 ----

    [<Fact>]
    let ``强 AI 基线是第四种选手：配桌里是 Baseline，牌谱里那一列写 baseline，与另外三种都分得开`` () =
        let model, _ = baselineAt 1

        let roster =
            match TablePage.rosterOf model with
            | Some roster -> roster
            | None -> failwith "Live 那一桌必然有配桌"

        Assert.Equal(SeatPlayer.Baseline, Roster.playerAt (seat 1) roster)
        Assert.Equal<string list>([ "random"; "baseline"; "random"; "random" ], Roster.names roster)
        Assert.Equal<Seat list>([ seat 1 ], Roster.baselineSeats roster)

        // **与另外三种分得开**：模型席恒带一道斜杠（`provider/model`）。
        Assert.DoesNotContain("/", Roster.baselineName)
        Assert.NotEqual<string>(Roster.baselineName, Roster.humanName)
        Assert.NotEqual<string>(Roster.baselineName, Bot.toWire Bot.Uniform)
        Assert.NotEqual<string>(Roster.baselineName, Bot.toWire Bot.Opinionated)
        // localStorage 那一层认得出它（配置从那儿读回来，什么都可能）。
        Assert.Equal("baseline", SeatChoice.toWire SeatChoice.Baseline)
        Assert.Equal(Some SeatChoice.Baseline, SeatChoice.ofWire "baseline")

    [<Fact>]
    let ``四席怎么混都行：真人 + 强 AI + 两个 bot 同桌，四种选手各在各的位置上`` () =
        let seating =
            SeatingPlan.initial Ruleset.yonma
            |> SeatingPlan.bind (seat 0) SeatChoice.Human
            |> SeatingPlan.bind (seat 1) SeatChoice.Baseline
            |> SeatingPlan.bind (seat 2) SeatChoice.Baseline
            |> SeatingPlan.bind (seat 3) (SeatChoice.Bot Bot.Opinionated)

        // **强 AI 不限一席**（与真人的差别）：那几 MB 整桌只拉一份。
        Assert.Equal<Seat list>([ seat 1; seat 2 ], SeatingPlan.baselineSeats seating)
        Assert.Equal<Seat list>([ seat 0 ], SeatingPlan.humanSeats seating)

        Assert.Equal<string list>([ "human"; "baseline"; "baseline"; "opinionated" ], SeatingPlan.names seating)

        // 坐上第二席真人会把头一席腾空（票 87），**而强 AI 席不受影响**。
        let moved = seating |> SeatingPlan.bind (seat 3) SeatChoice.Human
        Assert.Equal<Seat list>([ seat 3 ], SeatingPlan.humanSeats moved)
        Assert.Equal<Seat list>([ seat 1; seat 2 ], SeatingPlan.baselineSeats moved)

    [<Fact>]
    let ``引擎问它要动作时给的是同一份决策包：它与模型席、真人席消费同一个投影`` () =
        let model, _ = baselineAt 0
        let table = tableOf model

        let roster =
            match TablePage.rosterOf model with
            | Some roster -> roster
            | None -> failwith "Live 那一桌必然有配桌"

        // 东 1 局的亲就是座位 0：一开局就轮到它。
        match Table.decideFor (seat 0) roster table with
        | Some(Demand.Baseline package) ->
            Assert.Equal(seat 0, DecisionPackage.seat package)
            Assert.NotEmpty(DecisionPackage.options package)
            // **它看得见的与真人席、模型席一字不差**：掩蔽事件流 + 那一席的观测。
            // 强度参照系必须与被参照的那几席看同一张牌桌。
            Assert.NotEmpty(DecisionPackage.history package)
        | other -> failwith $"该问强 AI 基线要一手，却得到 {other}"

    // ---- 一个字节都不许拉 ----

    [<Fact>]
    let ``不选那一席就一个字节都不拉：默认那一桌连拉资产的 Cmd 都不发`` () =
        // 四家均匀随机（`?table=1` 的默认桌）。
        let model, cmd =
            TablePage.initial RulesetDraft.initial (SeatingPlan.initial Ruleset.yonma)

        Assert.Equal(BaselineStatus.Absent, TablePage.baseline model)
        Assert.Empty(cmd)

        // 拨到别的选手也一样：拉那一步只由「拨到强 AI 基线」触发。
        // **`Absent` 就是「一个字节都没拉」那条不变量的表示**（浏览器那一侧另有一道
        // 「网络请求计数为 0」的闸门），而副作用那一条只剩落 localStorage 的那一记。
        for choice in [ SeatChoice.Human; SeatChoice.Bot Bot.Opinionated; SeatChoice.Profile "档案 1" ] do
            let bound = SeatBound(seat 0, choice)
            Assert.Equal(BaselineStatus.Absent, TablePage.baseline (step bound model))
            Assert.Equal(1, effects bound model)

        // 阳性对照：同一条消息换成强 AI 基线，副作用当场多出拉资产那一记。
        Assert.Equal(2, effects (SeatBound(seat 0, SeatChoice.Baseline)) model)

    [<Fact>]
    let ``拨到强 AI 基线的那一下就去拉，而且整桌只拉一次`` () =
        let model, _ =
            TablePage.initial RulesetDraft.initial (SeatingPlan.initial Ruleset.yonma)

        // 拨上它：状态线当场说「正在取」，并且真发了一条副作用（拉资产 + 落 localStorage）。
        let bound = step (SeatBound(seat 0, SeatChoice.Baseline)) model
        Assert.Equal(BaselineStatus.Loading, TablePage.baseline bound)

        // 已经在拉了：第二席拨上它不再多拉一次（整桌共用一份资产）。
        let ready = step (BaselineLoaded(Ok 6039832)) bound
        Assert.Equal(BaselineStatus.Ready 6039832, TablePage.baseline ready)

        let second = TablePage.update (SeatBound(seat 2, SeatChoice.Baseline)) ready

        Assert.Equal(BaselineStatus.Ready 6039832, TablePage.baseline (fst second))
        // 只剩落 localStorage 那一条；拉资产那一条没有再发。
        Assert.Equal(1, List.length (snd second))

    [<Fact>]
    let ``上一次就把某一席拨给了它：页面一打开就去拉`` () =
        let model, cmd = baselineAt 3

        Assert.Equal(BaselineStatus.Loading, TablePage.baseline model)
        Assert.Equal(1, List.length cmd)

    [<Fact>]
    let ``首页那一屏没有配桌，因此恒是「一个字节都没拉」`` () =
        let model, _ = TablePage.home ()
        Assert.Equal(BaselineStatus.Absent, TablePage.baseline model)
        Assert.Empty(TablePage.baselineTroubles model)

    // ---- 降级不是 try/catch ----

    [<Fact>]
    let ``资产拉不动：页面说得出原因，那一席退回自带 bot，名牌跟着说实话`` () =
        let model, _ = baselineAt 1
        let stuck = step (BaselineLoaded(Error "强 AI 基线拉不动：… 回了 HTTP 404")) model

        match TablePage.baseline stuck with
        | BaselineStatus.Unavailable reason ->
            // **原因要是那一句中文原话**，不是一个布尔量。
            Assert.Contains("404", reason)
        | other -> failwith $"该是「拉不动」，却得到 {other}"

        // 那一席当场退回「有主见」的自带 bot——**换在配桌那一层**，
        // 因此牌谱里那一列说的是实话：真正把这几手打出来的就是它。
        let roster =
            match TablePage.rosterOf stuck with
            | Some roster -> roster
            | None -> failwith "Live 那一桌必然有配桌"

        Assert.Equal(SeatPlayer.Bot Bot.Opinionated, Roster.playerAt (seat 1) roster)
        Assert.Equal<string list>([ "random"; "opinionated"; "random"; "random" ], Roster.names roster)
        // 名牌上写着强 AI 而实际在打的是 bot，那就是句假话。
        Assert.Equal<string list>([ "均匀随机"; Bot.toDisplay Bot.Opinionated; "均匀随机"; "均匀随机" ], TablePage.nameplates stuck)

    [<Fact>]
    let ``资产拉不动，其余席照常把一整场打完：它是可选依赖，不是单点`` () =
        let model, _ = baselineAt 1
        let stuck = step (BaselineLoaded(Error "强 AI 基线拉不动：断网了")) model

        // **真打一场**（不是断言一个标志位）：一步一步推到终局。
        let rec play (moves: int) (model: TableModel) : TableModel =
            if Option.isSome (Table.result (tableOf model)) then
                model
            elif moves >= 4000 then
                failwith "这一场该在 4000 步之内打完，却还没终局"
            else
                let table = tableOf model

                let next =
                    if Table.isKyokuEnded table then
                        step KyokuAdvanced model
                    else
                        step Advanced model

                if (tableOf next).Turns = table.Turns && not (Table.isKyokuEnded table) then
                    failwith $"第 {table.Turns} 手推不动，而这一局还没终"

                play (moves + 1) next

        let ended = play 0 stuck

        match Table.result (tableOf ended) with
        | Some result -> Assert.Equal(100000, List.sum result.Scores)
        | None -> failwith "该终局了"

        // 这一整场里它一条决策记录都没有（那一席根本不是模型）。
        Assert.Empty((tableOf ended).Decisions)

    [<Fact>]
    let ``资产还在路上时那一桌停下来等：定时器不空转，拉完那一刻自己接着走`` () =
        // 播着（不是暂停）：这一条量的正是「定时器续不续」。
        let playing = baselineAt 0 |> fst |> step PlayToggled
        Assert.Equal(BaselineStatus.Loading, TablePage.baseline playing)

        // 东 1 局的亲是座位 0：这一刻正轮到它，而资产还没到——**牌桌推不动，定时器也不续**
        // （续了也只会把牌桌空转一遍）。
        let stepped = TablePage.update (Ticked playing.Playback.Generation) playing
        Assert.Equal(0, (tableOf (fst stepped)).Turns)
        Assert.Empty(snd stepped)

        // 拉到了：`BaselineLoaded` 自己把牌桌重新开动（那一记定时器续上了）。
        let ready = TablePage.update (BaselineLoaded(Ok 6039832)) playing
        Assert.Equal(BaselineStatus.Ready 6039832, TablePage.baseline (fst ready))
        Assert.NotEmpty(snd ready)

    // ---- 它不会说话 ----

    [<Fact>]
    let ``它出的那一手不留决策记录：没有气泡、没有 token 账单、也不是「0 tok」`` () =
        let ready = askedAt 0
        // 它被问了一手（`step` 那一趟把它问出去了），回执带回一个 id。
        let consult = consultOf ready

        let id =
            match DecisionPackage.options consult.Package with
            | option :: _ -> ActionOption.id option
            | [] -> failwith "这一包里该有动作"

        let played =
            step
                (BaselineDecided(
                    seat 0,
                    consult.Ticket,
                    {
                        ActionId = Some id
                        Failure = None
                        LatencyMs = 1
                        Candidates = []
                        CandidatesTotal = 0
                    }
                ))
                ready

        let table = tableOf played
        // 落定了一手（它真的出了手）。
        Assert.Equal(1, table.Turns)
        // **一条决策记录都没有**：于是气泡（读 `Table.Decisions`）与账单（读 `Table.usage`）
        // 在结构上就为空——不是显示一个空气泡或者「0 tok」。
        Assert.Empty(table.Decisions)
        Assert.Equal(0, Usage.promptTokens (Table.usage table))
        Assert.Equal(0, Table.fallbacks table)
        // 它那一席的气泡取值器给的是 None（**上帝视角下**，因此不是被视角挡掉的）。
        match TablePage.shown played with
        | Shown.Board board -> Assert.Equal(None, TablePage.bubbles played board (seat 0))
        | other -> failwith $"该有一张牌桌，却得到 {other}"

    [<Fact>]
    let ``它交不出那一手：兜底代打，并且把原因说出来——不许静默替换`` () =
        let ready = askedAt 0
        let consult = consultOf ready

        let played =
            step
                (BaselineDecided(
                    seat 0,
                    consult.Ticket,
                    {
                        ActionId = None
                        Failure = Some "它出的那一手不在这一包里"
                        LatencyMs = 2
                        Candidates = []
                        CandidatesTotal = 0
                    }
                ))
                ready

        // 兜底代打的那一手同样取自这一包（合法性一分没放宽），牌桌照旧往前走。
        Assert.Equal(1, (tableOf played).Turns)
        Assert.Equal<string list>([ "它出的那一手不在这一包里" ], TablePage.baselineTroubles played)

    [<Fact>]
    let ``过期或错位的回执一律丢掉：它的票号与座位要与在飞的那一份对上`` () =
        let ready = askedAt 0
        let consult = consultOf ready

        let answer: BaselineAnswer =
            {
                ActionId = Some 0
                Failure = None
                LatencyMs = 1
                Candidates = []
                CandidatesTotal = 0
            }

        // 票号对不上、座位对不上：两条都没有事情发生。
        Assert.Equal(0, (tableOf (step (BaselineDecided(seat 0, consult.Ticket + 7, answer)) ready)).Turns)
        Assert.Equal(0, (tableOf (step (BaselineDecided(seat 2, consult.Ticket, answer)) ready)).Turns)

    // ---- 对局中它照旧不说话（票 103 的边界：确定度只进复盘那一行） ----

    [<Fact>]
    let ``回执里带着候选分布，对局中的牌桌上仍旧一个数都不出现`` () =
        let ready = askedAt 0
        let consult = consultOf ready

        let ids =
            DecisionPackage.options consult.Package
            |> List.truncate 3
            |> List.map ActionOption.id

        // 票 103 之后跨界回来的多了两格（它那一次前向给的候选与概率）。
        // **它们只服务终局之后的复盘那一行**：对局中给出「它有多确定」，
        // 与对局中给出「换打会怎样」是同一种作弊（ADR-0003 管终局前的可见性，真人在座时更不许）。
        let answer: BaselineAnswer =
            {
                ActionId = List.tryHead ids
                Failure = None
                LatencyMs = 1
                Candidates =
                    ids
                    |> List.mapi (fun index id ->
                        {
                            ActionId = id
                            P = 0.6961328 / float (index + 1)
                        })
                CandidatesTotal = List.length ids
            }

        let played = step (BaselineDecided(seat 0, consult.Ticket, answer)) ready
        let table = tableOf played

        // 它真出了手，而**这一桌的事实里没有多出任何一格**：没有决策记录（于是没有气泡、
        // 没有 token 账单），牌谱里也找不到那几个数——它们从来没有进过 `Table`。
        Assert.Equal(1, table.Turns)
        Assert.Empty(table.Decisions)
        Assert.Equal(0, Usage.promptTokens (Table.usage table))

        let paifu =
            match TablePage.rosterOf played with
            | Some roster -> Table.paifu roster table |> Paifu.encoder |> Encode.toString 0
            | None -> failwith "Live 那一桌必然有配桌"

        // **阳性对照**：这一串数字确实是我们刚喂进去的那一个（否则「牌谱里没有它」
        // 可能只是因为我们找的是一串本来就不存在的字符）。
        Assert.Equal("0.6961328", ReviewStrong.probabilityToWire 0.6961328)
        Assert.DoesNotContain("0.6961328", paifu)
        Assert.DoesNotContain("candidates", paifu)

        match TablePage.shown played with
        | Shown.Board board -> Assert.Equal(None, TablePage.bubbles played board (seat 0))
        | other -> failwith $"该有一张牌桌，却得到 {other}"
