namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// **模型席那条路上的过期问话**（票 108，接票 92 §⑧ 第 2 条挂在报告里的那一条）。
///
/// 一份问话在飞的时候，牌桌可能绕过它往前走：人在面板上把那一席**拨给了自己**
/// （`SeatBound`，对局中随时点得到），于是那几手由真人打出去——真人那条路走 `handOf`，
/// 不经 `drain` 那条「沿引擎顺序落子」的顺序。**那一刻起，包里的 id 就不是这一手的号了**
/// （id 是合法动作集的下标，`DecisionPackage.forSeat`）。
///
/// 留着它有两个下场，**这一族用例把两个都钉住**：
///
/// 1. **牌桌停住**：`Awaiting` 非空 ⇒ `waiting` 恒真 ⇒ 定时器不续，牌桌停在那儿不动；
/// 2. **落错一手**：`drain` **按座位**找在飞的那一份，于是这一席下一次被问到时拿**旧包**落子
///    ——同一个 id 指的已是另一条动作，引擎当场拒（`Table.Fault`），牌桌就此停死。
///
/// 修法与票 92 给强 AI 基线那一侧的**逐字同一条判据**（`stillCurrent`）：
/// 合法动作集逐条相同才算还在当下。**剪掉的那一份不是丢掉，是记一笔账**——
/// 它真的调了 provider、真的计了费（`Table.Voided`），但**不留一条声称落了子的决策记录**。
module StaleAskTests =

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

    /// **另一份档案**（票 109）：拨座位时拨给它。**provider / 模型 / key 三格都与上一份不同**
    /// ——「旧回执是上一份配置答的」这件事得在值上看得见，否则那条断言无从开口。
    let private rival: ModelProfile =
        {
            Name = "谨慎的老李"
            Provider = "anthropic"
            Model = "claude-测试用的假型号"
            BaseUrl = ""
            ApiKey = "sk-另一把测试用的假 key"
            TimeoutMs = 12000
            Thinking = Thinking.Off
        }

    /// 座位 0 交给模型、其余三家自带 bot 的一桌（开局第一手就是它：亲摸完牌等着打）。
    ///
    /// **其余三席刻意是 bot**：这一族用例要数「这一桌有几条决策记录」与「账单上那几个数」，
    /// 而 bot 席一条记录都不留、一个 token 都不花——于是数出来的那几个数只属于被验的那一席。
    let private llmSeat: SeatingPlan =
        { SeatingPlan.initial Ruleset.yonma with
            Profiles = [ profile ]
        }
        |> SeatingPlan.bind Seat.first (SeatChoice.Profile profile.Name)

    /// 同上，但档案库里摊着**两份**（票 109：拨给别的模型那几条要拨得动）。
    let private twoProfiles: SeatingPlan =
        { llmSeat with
            Profiles = [ profile; rival ]
        }

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    /// 这条消息发出了几个效果体（`Cmd` 就是一串效果体）。
    /// **「牌桌还转不转得动」那一条的执行者就是它**：定时器不续 = 一个效果体都不发。
    let private effects (message: TableMsg) (model: TableModel) : int =
        TablePage.update message model |> snd |> List.length

    let private liveOf (model: TableModel) : LiveTable =
        match TablePage.live model with
        | Some live -> live
        | None -> failwith "这几条用例跑的是 `?table=1` 那一页，它必然是 Live"

    let private tableOf (model: TableModel) : Table =
        match (liveOf model).Table with
        | Ok table -> table
        | Error error -> failwith $"这一桌应当开得起来，却得到「{error}」"

    /// 这一桌的配桌（导出牌谱要它）。
    let private rosterOf (model: TableModel) : Roster =
        match TablePage.rosterOf model with
        | Some roster -> roster
        | None -> failwith "Live 那一桌必然有配桌"

    let private awaitingOf (model: TableModel) : Awaiting =
        match (liveOf model).Awaiting with
        | [ awaiting ] -> awaiting
        | [] -> failwith "这一手应当在等 Agent 层的回执"
        | many -> failwith $"这里只该有一份在飞的问话，却有 {List.length many} 份"

    /// 那一席此刻能提交的头一条动作的 id（真人点哪一下与这一族用例无关，取第一条即可）。
    let private firstId (package: DecisionPackage) : int =
        DecisionPackage.options package |> List.head |> ActionOption.id

    /// 模型好好答话：选 `id`，并报回一份 token 账单。
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
            ActionIds = [ id ]
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

    /// 那一次问话真的花掉的 token（假回执里写死的那一份）。
    let private billed: Usage =
        match (chose 0).Usage with
        | Some usage -> usage
        | None -> failwith "这份假回执该带一份账单"

    /// 推到**那一席又被问出去**为止（预算内没问到就当场红）。
    let private untilAsked (model: TableModel) : TableModel =
        let rec loop (left: int) (model: TableModel) =
            match (liveOf model).Awaiting with
            | _ :: _ -> model
            | [] when left <= 0 -> failwith "预算内那一席再没被问到过"
            | [] -> loop (left - 1) (model |> step Advanced)

        loop 200 model

    /// **从这一桌造一只鬼**：问出去 → 人把那一席拨给自己 → 他自己打了一手。
    /// 三步都是页面上真点得到的；第三步之后牌桌已经翻篇，而那份问话还在飞
    /// ——**它包里的号从此不是这一手的号**。返回那份问话与走到这一刻的牌桌。
    let private ghostFrom (model: TableModel) : Awaiting * TableModel =
        let asked = untilAsked model
        let ghost = awaitingOf asked
        let seat = Awaiting.seat ghost
        let bound = asked |> step (SeatBound(seat, SeatChoice.Human))

        match TablePage.humanTurn bound with
        | None -> failwith "把在飞那一席拨给自己之后，这一手就该轮到他了"
        | Some package -> ghost, bound |> step (HumanPlayed(firstId package))

    /// **把牌桌开到「一份在飞的问话已经过期」那一刻**（不靠时序碰运气）。
    let private ghosted () : Awaiting * TableModel =
        TablePage.initial RulesetDraft.initial llmSeat |> fst |> ghostFrom

    /// 推到那一席**又轮到出手**那一刻就停——那正是旧包拿得去落子的那一刻。
    /// **一步都不许多走**：走过头就量不到「旧包被当成这一手的包」那一下（判据 20）。
    let private untilHisTurn (model: TableModel) : TableModel =
        let rec loop (left: int) (model: TableModel) =
            if Option.isSome (TablePage.humanTurn model) then model
            elif left <= 0 then failwith "预算内没轮回到那一席"
            else loop (left - 1) (model |> step Advanced)

        loop 200 model

    // ---- 复现：过期的问话回来时 ----

    [<Fact>]
    let ``过期的回执落不下去：牌桌没停、那一手仍旧是他的`` () =
        let ghost, model = ghosted ()
        let seat = Awaiting.seat ghost

        // ① **牌桌停住**那一种坏：`Awaiting` 里挂着一份过期问话时 `waiting` 恒真、
        // 定时器不续（而 `step` 又因为 `flying` 里有它而不重问），牌桌就停在那儿不动。
        // 剔掉之后按下「播放」必须真发一记定时器。
        Assert.True(effects PlayToggled model > 0, "牌桌停住了：挂着一份过期问话时按下「播放」一个效果体都没发——定时器不续，而那一席又因为「在飞」而不会被重问")

        Assert.Equal<int list>([], (liveOf model).Awaiting |> List.map (fun each -> each.Ticket))

        // 推到那一席又轮到出手：这正是旧包能被当成「这一手的包」的那一刻。
        let his = untilHisTurn model
        let before = tableOf his
        Assert.Equal(Some seat, Table.pending before |> Option.map (fun choice -> choice.Seat))

        // 鬼回执回来了。**它一手都不许落**：那一手没有发生。
        let late = his |> step (Answered(seat, ghost.Ticket, chose (firstId ghost.Package)))
        let table = tableOf late

        // ② **落错一手**那一种坏：旧包里同一个 id 指的已经是另一条动作。
        // 引擎拒得掉那一条时牌桌当场停死（`Fault`），拒不掉的时候更坏：
        // 它真的替这一席打了一手。
        Assert.True(Option.isNone table.Fault, $"过期的回执拿旧包落了子，引擎当场拒了它，牌桌就此停死：{table.Fault}")
        Assert.Equal(before.Turns, table.Turns)
        Assert.Equal<Event list>(GameState.events before.State, GameState.events table.State)

        // **那一手仍旧是他的**：包还摆在页面上等他点那一下（旧包没有替他出手）。
        Assert.True(Option.isSome (TablePage.humanTurn late), "那一手该还等着他点，而不是被一份旧包替他打了")

    // ---- 账：花了钱、没落子 ----

    [<Fact>]
    let ``作废的问话：token 还在账上，而那一手一条决策记录都没有`` () =
        let ghost, model = ghosted ()
        let seat = Awaiting.seat ghost

        // 剪掉的那一份记在账上，而不是丢掉：座位、时刻与一句中文原因都在。
        match (tableOf model).Voided with
        | [ voided ] ->
            Assert.Equal(ghost.Ticket, voided.Ticket)
            Assert.Equal(seat, voided.Seat)
            Assert.Contains("作废", VoidedAsk.reason voided)
            // **票 109 把这一笔的起因往前挪了一步**：从前它是他打完一手之后被**剪枝**
            // 剔下来的（`Expired`），现在是人拨那一下当场**撤**下来的（`Rebound`）。
            // 这一条用例钉的那两件事（钱还在、没有假记录）一字未变。
            match voided.Cause with
            | VoidCause.Rebound _ -> ()
            | other -> failwith $"拨座位那一下该当场撤票，实际记成了 {other}"
            // 回执还在飞：花了多少还不知道，因此这一刻账上还没有它那几个数。
            Assert.Equal(None, voided.Usage)
            Assert.Equal(0, Usage.promptTokens (Table.usage (tableOf model)))
        | many -> failwith $"该正好作废一次问话，实际 {List.length many} 次"

        // 回执回来了：**钱是真花掉的**（provider 调过、token 计过费），账上要有它。
        let late =
            model |> step (Answered(seat, ghost.Ticket, chose (firstId ghost.Package)))

        let table = tableOf late

        Assert.Equal(billed, Table.usage table)
        Assert.Equal(1, List.length (Table.paidVoids table))

        // **但它不许留下一条声称落了子的记录**：那一手没有发生。
        Assert.Empty table.Decisions
        Assert.Equal(0, Table.fallbacks table)

        // 牌谱里因此一条决策记录都没有，而牌桌上那笔花销还在——两句话都是实话。
        let paifu = Table.paifu (rosterOf late) table
        Assert.Empty paifu.Decisions

        // 同一份回执再回来一次（重试链上的重复投递）：账上仍旧只有那一笔。
        let again =
            late |> step (Answered(seat, ghost.Ticket, chose (firstId ghost.Package)))

        Assert.Equal(billed, Table.usage (tableOf again))

    // ---- 那一席要被重新问 ----

    [<Fact>]
    let ``作废之后那一席重新可问：新的一份问话落得下去`` () =
        let ghost, model = ghosted ()
        let seat = Awaiting.seat ghost

        // 人把那一席还给模型（他只是替它打了一手）。
        let back = model |> step (SeatBound(seat, SeatChoice.Profile profile.Name))

        let asked = untilAsked back
        let fresh = awaitingOf asked

        // 新的一票、新的一包（旧那一票的号不会被重用）。
        Assert.True(fresh.Ticket <> ghost.Ticket, $"这一席根本没被重新问过：在飞的仍是作废那一票（{ghost.Ticket}），它挂在 `flying` 里把重问堵死了")

        Assert.Equal(seat, Awaiting.seat fresh)

        // **落得下去才算真的重新问了**：这一份包换得回一个此刻合法的动作，
        // 引擎收下、手序往前走一格、留下这一席的决策记录。
        let played =
            asked |> step (Answered(seat, fresh.Ticket, chose (firstId fresh.Package)))

        let table = tableOf played

        Assert.True(Option.isNone table.Fault, $"新问的这一手该落得下去，却得到「{table.Fault}」")
        Assert.Equal((tableOf asked).Turns + 1, table.Turns)
        Assert.Equal(1, List.length table.Decisions)

        // 账上是两笔：作废那一次（等它的回执）与刚落下去这一手。
        Assert.Equal(1, List.length table.Voided)

    // ---- 阴性对照：正常一场里一次都不剪 ----

    /// 四席都交给同一份档案的一桌。
    let private fourLlm () : TableModel =
        (Seat.all Ruleset.yonma, llmSeat)
        ||> List.foldBack (fun seat plan -> plan |> SeatingPlan.bind seat (SeatChoice.Profile profile.Name))
        |> TablePage.initial RulesetDraft.initial
        |> fst

    /// 把**一整场**打完，回执按 `reverse` 指定的到达顺序回来（true = 末一家先回，
    /// 并发最坏的错位到达），而每一次都挑**最能把局面搅乱的那一条**动作：
    /// 荣和 > 杠 > 碰 > 吃 > 立直。**这正是票面担心的那种局面**——响应阶段一轮里几席同问、
    /// 有人碰、有人杠。返回打完的那一桌与同时在飞的最大席数（执行证据，判据 3）。
    ///
    /// `poke` 是**回执落下去之前人在面板上戳的那一下**（票 109 的阴性对照要它）：
    /// 不戳就传 `id`。**它只能往里加动作，不能把断言放松**。
    let private playGame (poke: TableModel -> TableModel) (reverse: bool) (model: TableModel) : TableModel * int =
        let rank (option: ActionOption) =
            match ActionOption.action option with
            | Action.Hora _ -> 0
            | Action.Minkan _
            | Action.Ankan _
            | Action.Kakan _ -> 1
            | Action.Pon _ -> 2
            | Action.Chi _ -> 3
            | Action.Riichi _ -> 4
            // 九种九牌那一条摆在最后：它把这一局当场收了，验不到响应阶段。
            | Action.Ryuukyoku _ -> 6
            | Action.Dahai _
            | Action.None _ -> 5

        let disruptive (package: DecisionPackage) : int =
            DecisionPackage.options package
            |> List.sortBy rank
            |> List.head
            |> ActionOption.id

        let rec loop (left: int) (most: int) (model: TableModel) =
            if left <= 0 then
                failwith "这一场在预算内没打完"
            else

                match (liveOf model).Awaiting with
                | [] ->
                    match Table.pending (tableOf model) with
                    | Some _ -> loop (left - 1) most (model |> step Advanced)
                    | None when Option.isSome (Table.result (tableOf model)) -> model, most
                    | None -> loop (left - 1) most (model |> step KyokuAdvanced)
                | entries ->
                    let most = max most (List.length entries)
                    // 面板上那一下先戳（默认什么都不戳），再从**戳完之后**那张表里挑该答的那一份。
                    let model = poke model

                    match
                        (liveOf model).Awaiting
                        |> List.filter (fun each -> Option.isNone each.Answer)
                        |> (if reverse then List.rev else id)
                    with
                    | [] -> failwith "在飞的都答过了却没落下去：drain 卡住了"
                    | next :: _ ->
                        let id = disruptive next.Package
                        loop (left - 1) most (model |> step (Answered(Awaiting.seat next, next.Ticket, chose id)))

        loop 6000 0 model

    [<Fact>]
    let ``阴性对照：四席模型打完一整场，一次都没剪`` () =
        // **量点停在那一场真的打完之后**（判据 20）：中途量到的 0 只说明还没轮到那一刻。
        // 两种到达顺序各打一场：正序（等价于串行）与倒序（并发最坏的错位到达）。
        for reverse in [ false; true ] do
            let played, most = fourLlm () |> playGame id reverse
            let table = tableOf played

            Assert.True(Option.isSome (Table.result table), "这一场该打到了终局精算")
            Assert.True(most >= 2, $"该遇到过多席同时在飞的响应阶段，实际最多 {most} 席")
            Assert.True(Option.isNone table.Fault, $"这一场不该出错：{table.Fault}")

            // **一次都没剪**：剪枝只该发生在牌桌绕过一份在飞问话的时候，而正常一场里没有那种事。
            Assert.Equal<VoidedAsk list>([], table.Voided)

            // 账单因此就是那几手决策记录的合计（没有第二笔来源）。
            let recorded =
                table.Decisions
                |> List.choose (fun record -> record.Usage)
                |> List.fold Usage.add Usage.zero

            Assert.Equal(recorded, Table.usage table)

    // ================= 票 109：在飞的问话遇上「换人」 =================
    //
    // 票 108 交回来两件同族的、它没修的（它的 §⑦ 第 1 与第 4 条）：
    //
    // 1. **开下一局把在飞的问话整表清空**，token 跟着从账上消失——同一张牌桌、同一本账
    //    （`Table.usage` 跨局累计），那一票的钱就此蒸发；
    // 2. **拨座位没有「撤回在飞那一票」的语义**：票 108 那道剪枝按「合法动作集是否还是当下」判，
    //    而**拨座位那一刻动作集往往没变**，因此它剪不掉——于是回执赶在他出手之前回来时，
    //    **模型替坐在桌边的那个人打了一手**。
    //
    // **撤票是语义，剪枝是兜底，两者都要有**：`VoidCause` 因此分成三个 case，
    // 而不是把三种起因塞进同一句中文里（判据 12）。

    /// 那一席在飞的那一票（这几条用例都只在座位 0 上造鬼）。
    let private flyingTickets (model: TableModel) : int list =
        (liveOf model).Awaiting |> List.map (fun each -> each.Ticket)

    /// 账上那几笔作废里、**因为换人而撤下来的**那几笔（`Table.revoked` 的取值器就是它的执行体）。
    let private revocations (model: TableModel) : int =
        model |> tableOf |> Table.revoked |> List.length

    /// 打到**这一局真的终了**为止：轮到真人就替他点头一下，其余交给 `Advanced`。
    /// **量点要停在这儿**（判据 20）——中途抓一把只说明还没轮到那一刻。
    let private playKyoku (model: TableModel) : TableModel =
        let rec loop (left: int) (model: TableModel) =
            if Table.isKyokuEnded (tableOf model) then
                model
            elif left <= 0 then
                failwith "这一局在预算内没打完"
            else
                match TablePage.humanTurn model with
                | Some package -> loop (left - 1) (model |> step (HumanPlayed(firstId package)))
                | None -> loop (left - 1) (model |> step Advanced)

        loop 2000 model

    // ---- 第二件：拨座位要撤票 ----

    [<Fact>]
    let ``拨座位当场撤票：在飞的那一票立刻作废，不等它回来再剪`` () =
        let asked = TablePage.initial RulesetDraft.initial llmSeat |> fst |> step Advanced
        let ghost = awaitingOf asked
        let seat = Awaiting.seat ghost
        let before = tableOf asked

        // **拨那一刻牌桌一手都没走**：`swept` 那道剪枝按「合法动作集是否还是当下」判，
        // 因此它此刻剪不掉这一份——撤票只能是语义。这一句是阳性对照，防的是
        // 「其实是剪枝顺手剪掉的」那种假绿。
        let bound = asked |> step (SeatBound(seat, SeatChoice.Human))
        Assert.Equal(before.Turns, (tableOf bound).Turns)

        Assert.Equal<int list>([], flyingTickets bound)

        match (tableOf bound).Voided with
        | [ voided ] ->
            Assert.Equal(ghost.Ticket, voided.Ticket)
            Assert.Equal(seat, voided.Seat)
            // 作废发生在**这一手之后**（手序不动，`voidAsk` 只往账上追加）。
            Assert.Equal(before.Turns, voided.Turn)
            // **拨给了谁要说得出来**（不许静默作废，同票 23 给兜底定的那条规矩）。
            match voided.Cause with
            | VoidCause.Rebound taker -> Assert.Contains("我自己", taker)
            | other -> failwith $"该记成一次「换人撤票」，实际记成了 {other}"

            Assert.Contains("换了人", VoidedAsk.reason voided)
            // 回执还在飞：花了多少还不知道。
            Assert.Equal(None, voided.Usage)
        | many -> failwith $"拨那一下该正好撤回一票，实际 {List.length many} 次"

        Assert.Equal(1, revocations bound)

    [<Fact>]
    let ``撤票之后：回执赶在他出手之前回来，那一手仍旧是他的`` () =
        let asked = TablePage.initial RulesetDraft.initial llmSeat |> fst |> step Advanced
        let ghost = awaitingOf asked
        let seat = Awaiting.seat ghost
        let bound = asked |> step (SeatBound(seat, SeatChoice.Human))
        let before = tableOf bound

        // 拨完那一刻**他还没出手**：包就摆在页面上等他点那一下。
        Assert.True(Option.isSome (TablePage.humanTurn bound), "把那一席拨给自己之后，这一手就该轮到他了")

        // **鬼回执赶在他出手之前回来**——票 108 §⑦ 第 4 条留下的那个洞：
        // 那时包还对得上（`stillCurrent` 为真），因此剪枝剪不掉它。
        let late =
            bound |> step (Answered(seat, ghost.Ticket, chose (firstId ghost.Package)))

        let table = tableOf late

        Assert.True(Option.isSome (TablePage.humanTurn late), "回执赶在他出手之前回来，模型替他打了一手：那一席已经是他的了，牌谱里那一手却记在模型名下")

        Assert.Equal(before.Turns, table.Turns)
        Assert.Empty table.Decisions
        Assert.True(Option.isNone table.Fault, $"这一下不该让引擎拒什么：{table.Fault}")
        // 事件流一条都没多：那一手真的没有发生。
        Assert.Equal<Event list>(GameState.events before.State, GameState.events table.State)

        // **钱还在账上**：那一次问话真的调过 provider，只是那一手没发生。
        Assert.Equal(billed, Table.usage table)
        Assert.Equal(1, List.length (Table.paidVoids table))

        // 他接着自己打那一手：牌桌照常往前走（撤票没有把这一桌卡住）。
        match TablePage.humanTurn late with
        | None -> failwith "这一手该还等着他点"
        | Some package ->
            let his = late |> step (HumanPlayed(firstId package))
            Assert.Equal(before.Turns + 1, (tableOf his).Turns)
            Assert.Empty (tableOf his).Decisions

    [<Fact>]
    let ``拨给别的模型同样撤票：旧回执不算这一席的答复，那一席按新配置重问`` () =
        let asked =
            TablePage.initial RulesetDraft.initial twoProfiles |> fst |> step Advanced

        let ghost = awaitingOf asked
        let seat = Awaiting.seat ghost
        Assert.Equal(profile.Model, ghost.Config.Model)

        let before = tableOf asked
        let bound = asked |> step (SeatBound(seat, SeatChoice.Profile rival.Name))

        // **拨给别的模型也作废**：provider / key / 人格都可能换了，
        // 旧回执是**上一份配置**答的——它答的不是这一席此刻的那个人。
        Assert.Equal<int list>([], flyingTickets bound)
        Assert.Equal(1, revocations bound)

        match (tableOf bound).Voided with
        | [ voided ] ->
            match voided.Cause with
            | VoidCause.Rebound taker -> Assert.Contains(rival.Name, taker)
            | other -> failwith $"该记成一次「换人撤票」，实际记成了 {other}"
        | many -> failwith $"拨那一下该正好撤回一票，实际 {List.length many} 次"

        // 旧回执回来：**一手都不许落**，但钱要落在账上。
        let late =
            bound |> step (Answered(seat, ghost.Ticket, chose (firstId ghost.Package)))

        Assert.Equal(before.Turns, (tableOf late).Turns)
        Assert.Empty (tableOf late).Decisions
        Assert.Equal(billed, Table.usage (tableOf late))

        // **那一席按新那一份配置重问**：这就是「凭什么旧回执不算这一席的答复」的执行体
        // ——同一手牌换了一个选手来答，而账上分得出哪一笔是谁花的。
        let again = untilAsked late
        let fresh = awaitingOf again
        Assert.True(fresh.Ticket <> ghost.Ticket, "这一席根本没被重新问过")
        Assert.Equal(rival.Model, fresh.Config.Model)
        Assert.Equal(rival.Provider, fresh.Config.Provider)

    [<Fact>]
    let ``阴性对照：没换人的一整场，撤票 0 次`` () =
        // **拨到它已经绑着的那一项不算换人**：面板上点一下当前那一项同样发一条 `SeatBound`，
        // 而那一下什么都没改——误撤一票就是白花一次钱、白等一趟。
        //
        // **量点停在那一场真的打完之后**（判据 20）：中途量到的 0 只说明还没轮到那一刻。
        let pokes = ref 0

        let poke (model: TableModel) : TableModel =
            match (liveOf model).Awaiting with
            | [] -> model
            | entry :: _ ->
                let seat = Awaiting.seat entry
                let choice = (SeatingPlan.bindingAt seat (liveOf model).Seating).Choice
                pokes.Value <- pokes.Value + 1
                model |> step (SeatBound(seat, choice))

        for reverse in [ false; true ] do
            let played, most = fourLlm () |> playGame poke reverse
            let table = tableOf played

            // 三条阳性对照防空转（判据 3）：这一场真打到了终局精算、真遇到过多席同时在飞、
            // 而且**那几下真的在问话在飞时戳出去了**。
            Assert.True(Option.isSome (Table.result table), "这一场该打到了终局精算")
            Assert.True(most >= 2, $"该遇到过多席同时在飞的响应阶段，实际最多 {most} 席")
            Assert.True(pokes.Value > 0, "一次都没在问话在飞时戳过面板：这一条阴性对照在空转")
            Assert.True(Option.isNone table.Fault, $"这一场不该出错：{table.Fault}")

            // **撤票 0 次**，而且一笔作废都没有（剪枝那一半是票 108 钉的，这里一并守住）。
            Assert.Equal<VoidedAsk list>([], Table.revoked table)
            Assert.Equal<VoidedAsk list>([], table.Voided)

            // 账单因此就是那几手决策记录的合计（没有第二笔来源）。
            let recorded =
                table.Decisions
                |> List.choose (fun record -> record.Usage)
                |> List.fold Usage.add Usage.zero

            Assert.Equal(recorded, Table.usage table)

    // ---- 第一件：开下一局，那几笔账去哪了 ----

    [<Fact>]
    let ``开下一局：在飞的问话作废而不是从账上消失`` () =
        // **这一条走的是消息本身**：页面上那枚「下一局」只在这一局终了时才点得动，
        // 而这一局终了那一刻在飞的问话恒是 0 笔（`drain` 在每一条让局面翻篇的路上都先剪过一道，
        // 报告 §① 量的就是它）。留着这一条的理由是判据 2：
        // **`KyokuAdvanced` 在结构上仍旧是一条会丢账的路**，而说得出这件事的执行体只能是它。
        let asked = TablePage.initial RulesetDraft.initial llmSeat |> fst |> step Advanced
        let ghost = awaitingOf asked
        let seat = Awaiting.seat ghost
        let before = tableOf asked

        let paid (table: Table) =
            table |> Table.usage |> Usage.promptTokens

        Assert.Equal(0, paid before)

        let advanced = asked |> step KyokuAdvanced
        Assert.Equal<int list>([], flyingTickets advanced)

        match (tableOf advanced).Voided with
        | [ voided ] ->
            Assert.Equal(ghost.Ticket, voided.Ticket)
            Assert.Equal(seat, voided.Seat)
            Assert.Equal(VoidCause.NextKyoku, voided.Cause)
            Assert.Contains("开下一局", VoidedAsk.reason voided)
            Assert.Equal(None, voided.Usage)
        | many -> failwith $"开下一局该正好作废一次问话，实际 {List.length many} 次"

        // **开局清表不许让账单变小**（票面那条闸门）：回执晚回来时把 token 补上去，
        // 而不是让它从账上消失。812 + 1344 = 2156 tok 是那份假回执写死的那一份。
        let late =
            advanced |> step (Answered(seat, ghost.Ticket, chose (firstId ghost.Package)))

        Assert.Equal(billed, Table.usage (tableOf late))
        Assert.Equal(2156, paid (tableOf late))

        Assert.True(paid (tableOf late) >= paid before, "开下一局把账单弄小了：`Table.usage` 报的是花掉的总额，不是还没被清掉的那几笔")

        // 那一手仍旧没有发生：一条决策记录都没有。
        Assert.Empty (tableOf late).Decisions

    [<Fact>]
    let ``跨局补账：两局各撤一票，回执倒序回来也各归各的账`` () =
        // **跨局的票号会不会撞**：`LiveTable.Ticket` 是全局递增的一本账，
        // `KyokuAdvanced` 一个字都不动它——因此第二局那一票的号必然大于第一局那一票。
        // 撞号的下场是「补到别人那一笔上」，而那正是这一条要钉死的东西。
        let ghost1, first = ghosted ()
        let seat = Awaiting.seat ghost1

        // 第一局打完，开下一局。
        let advanced = playKyoku first |> step KyokuAdvanced
        let opened = tableOf advanced
        Assert.True(opened.Turns > 0, "第一局该真的打过牌")

        // 第二局：那一席还给模型，再造一只鬼。
        let ghost2, second =
            advanced |> step (SeatBound(seat, SeatChoice.Profile profile.Name)) |> ghostFrom

        Assert.True(ghost2.Ticket > ghost1.Ticket, $"跨局的票号撞了：{ghost1.Ticket} 与 {ghost2.Ticket}")

        // 两笔都在同一本账上，而且各记在**它真的发生的那一刻**（`Turn` 是那个锚）。
        match (tableOf second).Voided with
        | [ one; two ] ->
            Assert.Equal(ghost1.Ticket, one.Ticket)
            Assert.Equal(ghost2.Ticket, two.Ticket)
            Assert.True(one.Turn < opened.Turns, $"第一局那一笔该记在第一局里（{one.Turn} < {opened.Turns}）")
            Assert.True(two.Turn >= opened.Turns, $"第二局那一笔该记在第二局里（{two.Turn} >= {opened.Turns}）")
        | many -> failwith $"两局该各撤一票，实际 {List.length many} 笔"

        // **回执倒序回来**（并发最坏的错位到达）：后一票先回，先一票后回。
        let credited =
            second
            |> step (Answered(seat, ghost2.Ticket, chose (firstId ghost2.Package)))
            |> step (Answered(seat, ghost1.Ticket, chose (firstId ghost1.Package)))

        let table = tableOf credited

        // 各归各的账：两笔各自拿到自己那一份，谁也没被补到别人头上。
        match table.Voided with
        | [ one; two ] ->
            Assert.Equal(Some billed, one.Usage)
            Assert.Equal(Some billed, two.Usage)
        | many -> failwith $"该还是那两笔，实际 {List.length many} 笔"

        Assert.Equal(Usage.add billed billed, Table.usage table)
        Assert.Equal(2, List.length (Table.paidVoids table))
        // 两票都是「换人撤票」，一手都没落成。
        Assert.Equal(2, revocations credited)
        Assert.Empty table.Decisions
