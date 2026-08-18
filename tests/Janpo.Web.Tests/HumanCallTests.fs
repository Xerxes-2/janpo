namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// 真人的**响应动作**：吃碰杠、立直、荣和、自摸（票 88；spec 的 story 30
/// 「这些按钮只在动作合法时出现，所以我不可能犯规」）。
///
/// 这一层钉的是四条判据：
///
/// 1. **一一对应**：页面那三个出口（手牌上那几张 + 牌桌下面那一排 + 「过」）
///    合起来**正好是引擎给的那一包**，一条不多一条不少——而那一包又与引擎的合法动作集
///    逐条相同（**第三个锚点**：拿包对包等于拿同一个表达式对它自己，判据 6 那一族）；
/// 2. **点下去牌局真的变了**：固定种子把他推到「能碰 / 能吃 / 能立直 / 能荣和 / 能自摸 /
///    能杠 / 能九种九牌」的那一刻，各点一次，断言**事件流里真出现了他那一手**
///    ——不是断言按钮存在；
/// 3. **立直是两段**：宣言之后那一集逐张对拍引擎给的那一集（只剩「打完仍听牌」的那几张）；
/// 4. **并发**：真人在想的时候模型席照问、照答，而**谁先答不改裁决**——
///    同一轮里两种到达顺序跑出逐条相同的事件流。
///
/// 画出来长什么样（按钮在不在 DOM 里、不合法的那几种不在）在浏览器那一侧
/// （`web/scripts/verify-human.mjs`）——这里一行 Feliz 都不 open。
module HumanCallTests =

    let private seat (index: int) : Seat =
        match Seat.ofIndex index with
        | Some seat -> seat
        | None -> failwith $"{index} 应当是合法座位"

    let private human = seat 0

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    let private liveOf (model: TableModel) : LiveTable =
        match TablePage.live model with
        | Some live -> live
        | None -> failwith "这几条用例跑的是 `?table=1` 那一页，它必然是 Live"

    let private tableOf (model: TableModel) : Table =
        match (liveOf model).Table with
        | Ok table -> table
        | Error error -> failwith $"这一桌应当开得起来，却得到「{error}」"

    /// 真人坐座位 0、其余三家均匀随机、**种子写死**的那一桌。
    ///
    /// 种子是探针扫出来的（一次性脚本，没进仓库）：同一颗种子 + 同一套代点策略
    /// 必然跑出同一场，因此「第 N 手他能碰」这件事是可复现的。
    let private humanAt (seed: int) : TableModel =
        SeatingPlan.initial Ruleset.yonma
        |> SeatingPlan.bind human SeatChoice.Human
        |> TablePage.initial RulesetDraft.initial
        |> fst
        // 一律限定 `TableMsg.`：曳光弹那一页也有一个 `SeedEdited`（`App.Msg`），不限定会挑错。
        |> step (TableMsg.SeedEdited(string seed))
        |> step Restarted

    let private turnOf (model: TableModel) : DecisionPackage =
        match TablePage.humanTurn model with
        | Some package -> package
        | None -> failwith "这一刻该轮到真人"

    /// 引擎此刻给这一席的合法动作集（**第三个锚点**，与 `HumanSeatTests` 同一个理由）。
    let private legalFor (model: TableModel) : Action list =
        Table.pendings (tableOf model)
        |> List.tryFind (fun choice -> choice.Seat = human)
        |> Option.map (fun choice -> choice.Actions)
        |> Option.defaultValue []

    /// 页面上点得到的**全部 id**：自家手牌那几张 + 牌桌下面那一排 + 「过」。
    let private offered (package: DecisionPackage) : int list =
        (HumanSeat.dahaiOptions package |> List.map (fun (id, _, _, _) -> id))
        @ (HumanSeat.buttons package |> List.map (fun button -> button.Id))
        @ (HumanSeat.pass package |> Option.toList |> List.map (fun button -> button.Id))

    // ---- 代点：把牌局推到那一刻 ----

    /// 真人这一席在用例里的**那只手**：`OpinionatedPlayer` 选什么，就当他点了那一枚按钮。
    ///
    /// **它不是产品代码，也不是被测物**：被测的是「点下去之后引擎里发生了什么」。
    /// 拿有主见那档而不是均匀随机，是因为立直与和了这两条路要有人真的听牌
    /// （票 41 与 49 那次教训：均匀随机的语料里立直几乎不出现，闸门会变成永远执行不到的断言）。
    let private policy (rng: Rng) (model: TableModel) : Action * Rng =
        let table = tableOf model

        match Table.pendings table |> List.tryFind (fun choice -> choice.Seat = human) with
        | Some choice -> Bot.player Bot.Opinionated rng table.State choice
        | None -> failwith "这一刻该轮到真人"

    /// 一步：轮到真人就按 `pick` 给的那一条点下去，否则单步（这一局终了就开下一局）。
    let private moved (pick: Rng -> TableModel -> Action * Rng) (rng: Rng, model: TableModel) : Rng * TableModel =
        match TablePage.humanTurn model with
        | Some package ->
            let action, next = pick rng model

            match DecisionPackage.tryId action package with
            | Some id -> next, step (HumanPlayed id) model
            | None -> failwith $"代点选的那一条不在这一包里：{Action.toDisplay action}"
        | None ->
            let table = tableOf model

            if Table.isKyokuEnded table then
                rng, step KyokuAdvanced model
            else
                let played = step Advanced model

                if
                    (tableOf played).Turns = table.Turns
                    && Option.isNone (TablePage.humanTurn played)
                    && not (Table.isKyokuEnded (tableOf played))
                then
                    failwith $"第 {table.Turns} 手推不动，而它既不轮到真人也没终局"

                rng, played

    /// 路上怎么代点（两套，与探针扫种子时那两套逐字相同）：
    ///
    /// - 默认：**响应阶段一律「过」**，该他出牌照有主见那档打；
    /// - `greedy`：**碰得了就碰**——加杠要先有一组碰摆在牌桌上，
    ///   一路「过」的路上它永远不会出现（扫了 1400 颗种子一次都没碰上）。
    let private walking (greedy: bool) (rng: Rng) (model: TableModel) : Action * Rng =
        let package = turnOf model

        let taken (kind: string) =
            HumanSeat.buttons package
            |> List.tryFind (fun button -> button.Kind = kind)
            |> Option.map (fun button -> button.Id)

        let chosen =
            match (if greedy then taken "pon" else None) with
            | Some id -> Some id
            | None -> HumanSeat.pass package |> Option.map (fun pass -> pass.Id)

        match chosen with
        | Some id ->
            match DecisionPackage.tryAction id package with
            | Some action -> action, rng
            | None -> failwith "代点选的那一条换不回动作"
        | None -> policy rng model

    let private passing = walking false

    /// 把这一桌推到「他这一手的合法动作集里出现了 `kind`」的那一刻，并把那一条交出来。
    ///
    /// `pick` 是路上怎么代点（默认一路「过」）；`Rng` 的种子与探针那一份逐字相同，
    /// 因此扫出来的「第 N 手他能碰」在这里原样重现。
    let private hunted (greedy: bool) (kind: string) (seed: int) : TableModel * ActionButton =
        let pick = walking greedy

        let rec walk (moves: int) (rng: Rng, model: TableModel) : TableModel * ActionButton =
            if moves > 3000 then
                failwith $"这一场里他一次都没碰上「{kind}」：种子该重扫了"
            else
                match TablePage.humanTurn model with
                | Some package ->
                    match HumanSeat.buttons package |> List.tryFind (fun button -> button.Kind = kind) with
                    | Some button -> model, button
                    | None -> walk (moves + 1) (moved pick (rng, model))
                | None ->
                    if Option.isSome (Table.result (tableOf model)) then
                        failwith $"这一场打完了，他一次都没碰上「{kind}」：种子该重扫了"
                    else
                        walk (moves + 1) (moved pick (rng, model))

        walk 0 (Rng.ofSeed (seed * 7919 + 13), humanAt seed)

    let private until = hunted false

    /// 点下去之后**把这一轮走完**（响应阶段要四家都答了才裁决），再看事件流。
    /// 一手都推不动时就停下——终局那一手（荣和 / 自摸 / 九种九牌）本来就到此为止。
    let private settled (model: TableModel) : TableModel =
        let rec walk (moves: int) (model: TableModel) : TableModel =
            if moves > 8 || Option.isSome (TablePage.humanTurn model) then
                model
            else
                let table = tableOf model
                let played = step Advanced model

                if (tableOf played).Turns = table.Turns then
                    played
                else
                    walk (moves + 1) played

        walk 0 model

    /// 这一局到此刻的事件流（引擎吐出来的那一份，**不是页面自己记的**）。
    let private events (model: TableModel) : Event list = GameState.events (tableOf model).State

    // ---- 一一对应：每一枚按钮背后都是一条引擎给的 id ----

    [<Fact>]
    let ``页面上点得到的那几条 = 引擎给的那一包：一条不多一条不少，而那一包 = 合法动作集`` () =
        // 一整场东风战里**每一手**都核一遍（他出手 70 次上下），不是只核开局那一手。
        // 第二个返回值是「同一种动作最多同时摆了几枚」——吃的左中右、碰的赤 5 取舍
        // 各占一条 id，因此它必须真的 > 1（票面点名的那一条），否则下面那些断言
        // 在「一种动作只画一枚」的实现上也会全绿。
        let rec walk (moves: int) (checks: int, widest: int) (rng: Rng, model: TableModel) : int * int =
            if moves > 3000 then
                failwith "这一场该在 3000 步之内打完"
            elif Option.isSome (Table.result (tableOf model)) then
                checks, widest
            else
                match TablePage.humanTurn model with
                | None -> walk (moves + 1) (checks, widest) (moved passing (rng, model))
                | Some package ->
                    let ids = offered package
                    let all = DecisionPackage.options package |> List.map ActionOption.id

                    // ① 三个出口合起来正好是这一包：**多一条是凭空造的，少一条是他点不到**。
                    Assert.Equal<int list>(List.sort all, List.sort (List.distinct ids))
                    // ② 同一条不许在两个出口里各出现一次（页面上会是两枚点下去一样的按钮）。
                    Assert.Equal(List.length (List.distinct ids), List.length ids)

                    // ③ 那一包本身与**引擎的合法动作集**逐条相同（第三个锚点）。
                    let legal = legalFor model

                    Assert.Equal<Action list>(legal, DecisionPackage.options package |> List.map ActionOption.action)

                    // ④ 每一枚按钮换得回引擎那条动作，而且**它既不是打牌也不是「过」**
                    //    （打牌画在手牌上、「过」单独一枚）。
                    for button in HumanSeat.buttons package do
                        match DecisionPackage.tryAction button.Id package with
                        | Some(Action.Dahai _)
                        | Some(Action.None _) -> failwith $"第 {button.Id} 条不该出现在那一排按钮里"
                        | Some action ->
                            Assert.Equal(HumanSeat.kind action, button.Kind)
                            Assert.Equal(Action.toDisplay action, button.Label)
                        | None -> failwith $"第 {button.Id} 条换不回动作"

                    let sameKind =
                        HumanSeat.buttons package
                        |> List.countBy (fun button -> button.Kind)
                        |> List.map snd
                        |> List.fold max 0

                    walk (moves + 1) (checks + 1, max widest sameKind) (moved passing (rng, model))

        let checks, widest = walk 0 (0, 0) (Rng.ofSeed (1 * 7919 + 13), humanAt 1)
        // 防空转（判据 3）：这一条真的开过口，而且开的次数是一整场的量级。
        Assert.True(checks > 40, $"这一场只核了 {checks} 手，太少了")
        // **同一种动作的几种做法各占一枚**（吃的左中右、碰亮不亮赤 5）。
        Assert.True(widest > 1, $"这一场里同一种动作最多只摆过 {widest} 枚：那几种做法的按钮没分开")

    [<Fact>]
    let ``不合法就点不着：该他出牌那一手没有「过」，响应那一手一张牌都打不出去`` () =
        // 该他出牌（开局第一手，他是亲）：没有「过」，也没有吃 / 碰 / 大明杠。
        let dahai = turnOf (humanAt 1)
        Assert.Equal(None, HumanSeat.pass dahai)
        Assert.NotEmpty(HumanSeat.dahaiOptions dahai)

        for button in HumanSeat.buttons dahai do
            Assert.DoesNotContain(button.Kind, [ "chi"; "pon"; "daiminkan"; "none" ])

        // 响应那一手：一张牌都打不出去（打牌不在合法动作集里），而「过」必在。
        let model, _ = until "pon" 1
        let respond = turnOf model
        Assert.Empty(HumanSeat.dahaiOptions respond)
        Assert.True(Option.isSome (HumanSeat.pass respond), "响应阶段「过」永远在")

        for button in HumanSeat.buttons respond do
            Assert.DoesNotContain(button.Kind, [ "dahai"; "reach"; "ankan"; "kakan"; "ryukyoku"; "none" ])

        // 全部 34 种牌 × 手切 / 摸切**双向**核一遍：响应那一手一条都给不出 id。
        for pai in Tile.all do
            for tsumogiri in [ true; false ] do
                Assert.Equal(None, HumanSeat.dahai pai tsumogiri respond)

    [<Fact>]
    let ``响应阶段整桌等着他：单步与定时器都推不动，他按了那一条「过」才走`` () =
        // **票 87 那条「整桌等着」只验过他出牌那一手**，而自动过恰恰只发生在响应阶段：
        // 不在这儿再钉一遍的话，「平台替他过」偷偷回来了也没人看得见（闸门真漏过一次，见报告 88）。
        let model, _ = until "pon" 1
        let before = tableOf model
        let package = turnOf model

        for message in [ Advanced; Ticked 0; Ticked 1 ] do
            let after = step message model
            Assert.Equal(before.Turns, (tableOf after).Turns)
            Assert.True(Option.isSome (TablePage.humanTurn after), $"发了 {message} 之后仍该轮到他")

        // 定时器也不续：这一桌只等他一个人（在飞的问话一份都没有）。
        Assert.Equal(0, TablePage.update PlayToggled model |> snd |> List.length)

        // 他自己按那一条「过」：这一手才落下去。
        let pass = HumanSeat.pass package |> Option.get
        let passed = step (HumanPlayed pass.Id) model
        Assert.Equal(before.Turns + 1, (tableOf passed).Turns)

        // **放掉了什么记得住**，而且逐条就是那一排按钮上的label（「过」自己不在里面）。
        match TablePage.passes passed with
        | latest :: _ ->
            Assert.Equal(before.Turns, latest.Turn)
            Assert.Equal(human, latest.Seat)

            Assert.Equal<string list>(
                HumanSeat.buttons package |> List.map (fun button -> button.Label),
                latest.Skipped
            )
        | [] -> failwith "他按了「过」，这本账上却一条都没有"

    // ---- 点下去，牌局真的变了 ----

    /// 「他能做这一手」的那一刻点下去，事件流里就该有他那一手。
    ///
    /// **种子是探针扫出来的**（`pon` / `chi` 在同一颗种子上，`ron` 与 `reach` 也是）：
    /// 同一颗种子 + 同一套代点策略必然跑出同一场。
    [<Theory>]
    [<InlineData("pon", 1, false)>]
    [<InlineData("chi", 1, false)>]
    [<InlineData("reach", 1, false)>]
    [<InlineData("daiminkan", 4, false)>]
    [<InlineData("ankan", 2, false)>]
    [<InlineData("ryukyoku", 3, false)>]
    // 加杠要先有一组碰：这一行走的是「碰得了就碰」那套代点。
    [<InlineData("kakan", 2, true)>]
    let ``他点一枚按钮，事件流里就真出现了他那一手`` (kind: string) (seed: int) (greedy: bool) =
        let model, button = hunted greedy kind seed
        let before = events model

        let played = step (HumanPlayed button.Id) model |> settled
        let after = events played

        // 防空转：这一手之前事件流里还没有他这一种（否则断言的是别人早先那一手）。
        let his (stream: Event list) =
            stream
            |> List.filter (fun event ->
                match event, kind with
                | Event.Pon(actor, _, _, _), "pon" -> actor = human
                | Event.Chi(actor, _, _, _), "chi" -> actor = human
                | Event.Minkan(actor, _, _, _), "daiminkan" -> actor = human
                | Event.Ankan(actor, _), "ankan" -> actor = human
                | Event.Kakan(actor, _, _), "kakan" -> actor = human
                | Event.Riichi actor, "reach" -> actor = human
                | Event.Ryuukyoku _, "ryukyoku" -> true
                | _, _ -> false)

        Assert.Equal(List.length (his before) + 1, List.length (his after))

    [<Fact>]
    let ``他鸣的那一手进牌谱与 bot 的同形：一条决策记录都不多，回放照样重建得出来`` () =
        // 票面那一条「真人做完动作，牌谱里与 bot / 模型的同一种动作**逐字段同形**」。
        // **不拿他那条事件去对它自己**（判据 6）：锚点是**回放**——牌谱重建得出同一串局面，
        // 就说明那一手在牌谱里与别家的同一种动作没有任何分别（回放根本不知道谁坐哪一席）。
        let model, button = until "pon" 1
        let played = step (HumanPlayed button.Id) model |> settled
        let table = tableOf played

        let roster =
            match TablePage.rosterOf played with
            | Some roster -> roster
            | None -> failwith "Live 那一桌必然有配桌"

        // 他那一手在事件流里，而**这一桌一条决策记录都没有**（他与 bot 同级，票 87）。
        Assert.Contains(
            human,
            Table.events roster table
            |> List.choose (fun event ->
                match event with
                | Event.Pon(actor, _, _, _) -> Some actor
                | _ -> None)
        )

        Assert.Empty table.Decisions
        Assert.Equal(0, Table.fallbacks table)

        // 牌谱重建：帧数与这一桌走过的手数对得上，末帧的事件流逐条相同。
        match Table.replay (Table.paifu roster table) with
        | Error error -> failwith $"他鸣过一手的牌谱该回放得了，却得到「{error}」"
        | Ok frames ->
            match List.tryLast frames with
            | None -> failwith "回放该至少有一帧"
            | Some last ->
                Assert.Equal(table.Turns, last.Turns)
                Assert.Equal<Event list>(Table.events roster table, Table.events roster last)

    [<Fact>]
    let ``他点荣和：这一局就此收在他的和了上`` () =
        let model, button = until "hora" 1
        // 响应阶段的和了就是荣和（自摸那一条在他自己摸完牌那一手）。
        Assert.True(Option.isSome (HumanSeat.pass (turnOf model)), "荣和是响应阶段那一条")

        let played = step (HumanPlayed button.Id) model |> settled

        match GameState.kyokuEnd (tableOf played).State with
        | Some(KyokuEnd.Hora horas) -> Assert.Contains(human, horas |> List.map (fun hora -> hora.Actor))
        | Some(KyokuEnd.Ryuukyoku _) -> failwith "他点了荣和，这一局却流局了"
        | None -> failwith "他点了荣和，这一局却还没结束"

    [<Fact>]
    let ``他点自摸：这一局就此收在他的和了上`` () =
        let model, button = until "hora" 2
        // 自摸那一条在他自己摸完牌那一手：没有「过」，手里还有牌打得出去。
        Assert.Equal(None, HumanSeat.pass (turnOf model))
        Assert.NotEmpty(HumanSeat.dahaiOptions (turnOf model))

        let played = step (HumanPlayed button.Id) model |> settled

        match GameState.kyokuEnd (tableOf played).State with
        | Some(KyokuEnd.Hora horas) ->
            Assert.Contains(human, horas |> List.map (fun hora -> hora.Actor))
            // **自摸**：和了那一条的 target 就是他自己。
            Assert.Contains(human, horas |> List.map (fun hora -> hora.Target))
        | Some(KyokuEnd.Ryuukyoku _) -> failwith "他点了自摸，这一局却流局了"
        | None -> failwith "他点了自摸，这一局却还没结束"

    // ---- 并发：真人在想，模型席照问照答，而谁先答不改裁决 ----

    /// 库里那份档案（key 是假的，一眼看得出）。**一字节都不出网**：
    /// 这一层根本不跑 Agent 层，回执在这里就是一个值（`AgentAnswer`）。
    let private profile: ModelProfile =
        {
            Name = "坐在他对面的那一份模型"
            Provider = "deepseek"
            Model = "deepseek-v4-flash"
            BaseUrl = ""
            ApiKey = "sk-测试用的假 key"
            TimeoutMs = 12000
            Thinking = Thinking.Off
        }

    /// 真人坐座位 0、座位 1 交给那份档案、座位 2/3 是 bot 的那一桌。
    let private mixedAt (seed: int) : TableModel =
        { SeatingPlan.initial Ruleset.yonma with
            Profiles = [ profile ]
        }
        |> SeatingPlan.bind human SeatChoice.Human
        |> SeatingPlan.bind (seat 1) (SeatChoice.Profile profile.Name)
        |> TablePage.initial RulesetDraft.initial
        |> fst
        |> step (TableMsg.SeedEdited(string seed))
        |> step Restarted

    /// 模型席好好答话：恒选包里第一条（确定性的一套答法，同一颗种子必得同一场）。
    let private chose: AgentAnswer =
        {
            ActionId = Some 0
            Reason = Some "就它了"
            Failure = None
            Attempts = 1
            LatencyMs = 640
            PromptTail = "【现在】东1局 0 本场……"
            Preamble = "你在打日本立直麻将……"
            RenderVersion = "janpo-default@aaaaaaaa.bbbbbbbb"
            Tools = """[{"name":"choose_action"}]"""
            ActionIds = [ 0 ]
            Output = """{"stop_reason":"toolUse"}"""
            Thinking = None
            Usage = None
        }

    /// 把在飞的那几份问话一次性答完。
    let private answerAll (model: TableModel) : TableModel =
        ((liveOf model).Awaiting, model)
        ||> List.foldBack (fun each model -> step (Answered(Awaiting.seat each, each.Ticket, chose)) model)

    /// 推到「**头一家是真人，而同一轮里还等着那一席模型**」的那一刻。
    let private crowded (seed: int) : TableModel =
        let rec walk (moves: int) (rng: Rng, model: TableModel) : TableModel =
            let pending = Table.pendings (tableOf model) |> List.map (fun choice -> choice.Seat)

            if
                Option.isSome (TablePage.humanTurn model)
                && List.contains (seat 1) pending
                && List.isEmpty (liveOf model).Awaiting
            then
                model
            elif moves > 3000 || Option.isSome (Table.result (tableOf model)) then
                failwith "这一场里没出现过「他与那一席模型同轮待答」：种子该重扫了"
            elif not (List.isEmpty (liveOf model).Awaiting) then
                walk (moves + 1) (rng, answerAll model)
            elif Option.isSome (TablePage.humanTurn model) then
                walk (moves + 1) (moved passing (rng, model))
            elif Table.isKyokuEnded (tableOf model) then
                walk (moves + 1) (rng, step KyokuAdvanced model)
            else
                // **这里不能用 `moved` 的防死循环闸**：混着模型席的那一桌上，
                // 一记「单步」可能只是把问话发出去（手数本来就不动）。
                walk (moves + 1) (rng, step Advanced model)

        walk 0 (Rng.ofSeed (seed * 7919 + 13), mixedAt seed)

    [<Fact>]
    let ``真人在想的时候模型席照问照答，而谁先答不改裁决`` () =
        // 种子 13：第 10 手上他与座位 1 那一席模型同轮待答（探针扫的，扫法见报告）。
        let crowd = crowded 13
        let before = (tableOf crowd).Turns

        // **这一桌不算「只能干等」**：还有一席模型待答而没问出去，那一记定时器还得转。
        Assert.Empty (liveOf crowd).Awaiting
        Assert.True(TablePage.update PlayToggled crowd |> snd |> List.isEmpty |> not, "该问的还没问出去，定时器就得续")

        // 单步：**问话发出去了，而牌桌一手没动**（他还没点）。
        let asked = step Advanced crowd
        Assert.Equal(before, (tableOf asked).Turns)
        Assert.True(Option.isSome (TablePage.humanTurn asked), "这一刻仍旧轮到他答")
        Assert.Equal<Seat list>([ seat 1 ], (liveOf asked).Awaiting |> List.map Awaiting.seat)
        // 问完就真的只剩干等了：定时器不再续（否则牌桌在他头上空转）。
        Assert.Equal(0, TablePage.update PlayToggled asked |> snd |> List.length)

        // 模型先答：**它的回执在 `Awaiting` 里等着**，牌桌仍旧一手没动——
        // 落子顺序沿引擎待答的顺序走，不按回执到达的先后（`drain`）。
        let heard =
            step (Answered(seat 1, ((liveOf asked).Awaiting |> List.head).Ticket, chose)) asked

        Assert.Equal(before, (tableOf heard).Turns)
        Assert.True(Option.isSome (TablePage.humanTurn heard), "模型先答了，仍旧轮到他")

        // 他随后按「过」：两家的答复这才按引擎的顺序一起落下去。
        let pass = HumanSeat.pass (turnOf heard) |> Option.get
        let modelFirst = step (HumanPlayed pass.Id) heard |> settled

        // 另一种到达顺序：**他先点，模型后答**。
        let humanFirst =
            let pass = HumanSeat.pass (turnOf crowd) |> Option.get
            let played = step (HumanPlayed pass.Id) crowd
            let asked = step Advanced played

            match (liveOf asked).Awaiting with
            | [] -> asked
            | _ -> answerAll asked
            |> settled

        // **头跳 / 双响的顺序不因谁先答而变**：同一轮里同样两份答复，两种到达顺序
        // 跑出来的事件流逐条相同。
        Assert.Equal<Event list>(events modelFirst, events humanFirst)
        Assert.Equal((tableOf modelFirst).Turns, (tableOf humanFirst).Turns)
        // 防空转：这一轮真的往前走了（两边都卡在原地也会让上面那一条绿）。
        Assert.True((tableOf modelFirst).Turns > before, "两家都答完了，这一轮该往前走")

    // ---- 立直是两段 ----

    [<Fact>]
    let ``立直两段：宣言之后能点的那一集 = 引擎给的那一集，逐张对拍`` () =
        let model, riichi = until "reach" 1
        let before = turnOf model

        // 宣言之前：手里那几张都打得出去，「立直宣言」是牌桌下面那一排里的一枚。
        let widest =
            HumanSeat.dahaiOptions before |> List.map (fun (_, pai, giri, _) -> pai, giri)

        Assert.NotEmpty widest

        let declared = step (HumanPlayed riichi.Id) model

        // **宣言之后仍旧是他这一手**（立直是两段：宣言 → 选宣言牌），而这一手只剩打牌。
        let second = turnOf declared
        Assert.True(HumanSeat.declaringRiichi second, "引擎说他此刻是「宣言了还没落定」")
        Assert.Empty(HumanSeat.buttons second)
        Assert.Equal(None, HumanSeat.pass second)

        // **逐张对拍引擎给的那一集**（第三个锚点：合法动作集，不是拿包对包）。
        let legal =
            legalFor declared
            |> List.choose (fun action ->
                match action with
                | Action.Dahai(_, pai, giri) -> Some(pai, giri)
                | _ -> None)

        let keeps =
            HumanSeat.dahaiOptions second |> List.map (fun (_, pai, giri, _) -> pai, giri)

        Assert.NotEmpty legal
        Assert.Equal<(Tile * bool) list>(legal, keeps)

        // **确实收窄了**：立直宣言之后打得出去的比宣言之前少（这一手若不收窄，
        // 上面那条「逐张相同」在一个什么都不判的实现上也会绿）。
        Assert.True(
            List.length keeps < List.length widest,
            $"宣言前 {List.length widest} 条、宣言后 {List.length keeps} 条：这一手没收窄，钉不住「只有保持听牌的那几张」"
        )

        // 全部 34 种牌 × 手切 / 摸切双向核一遍：那一集之外一条 id 都给不出来。
        for pai in Tile.all do
            for tsumogiri in [ true; false ] do
                let expected = legal |> List.contains (pai, tsumogiri)
                Assert.Equal(expected, HumanSeat.dahai pai tsumogiri second |> Option.isSome)

        // 点下去：立直宣言与宣言牌各落一手，事件流里两条都在。
        let id, _, _, _ = HumanSeat.dahaiOptions second |> List.head
        let played = step (HumanPlayed id) declared

        Assert.Contains(Event.Riichi human, events played)

        match (tableOf played).Latest with
        | Some { Action = Action.Dahai(actor, _, _) } -> Assert.Equal(human, actor)
        | _ -> failwith "宣言牌该是他刚打出去的那一手"
