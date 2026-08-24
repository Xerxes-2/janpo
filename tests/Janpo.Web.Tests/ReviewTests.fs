namespace Janpo.Web.Tests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// 复盘：逐手对照标注（票 90，spec 的 story 34）。
///
/// 这一层钉的是**五件事**：
///
/// 1. **只在终局后**：对局中复盘整块不存在（`ReviewShown.Hidden`）——对局中给出
///    「换打会怎样」就是作弊，那属于 Assisted 档（票 89）；
/// 2. **每一手都有一条**：那一席在牌谱里落定了几手，就有几条标注，手序逐个对得上
///    （锚点是**另一条路重走一遍事件流**：`Replay.traceOfPaifu` + `GameState.step`，
///    不拿页面那份帧对它自己）；
/// 3. **每一个数都是引擎算的**：向听、有效牌、危险度逐字等于 `Scaffold.calculate` 那一份，
///    而有效牌再拿 `Ukeire.calculate`（**第三个锚点**，绕开脚手架）核一遍；
/// 4. **更好的候选**：故意每一手都打最差的那张，它真的把更好的那几张列出来；
///    反过来每一手都打帕累托最优的那张时，那一栏恒是「这一手是当时的最优之一」；
/// 5. **点某一手跳过去、收起来回得来**（票 86 的回程）：轴只有票 75 那一根。
///
/// 画出来长什么样（面板在不在 DOM 里、那几个数与引擎对不对得上）在浏览器那一侧
/// （`web/scripts/verify-review.mjs`）——这里一行 Feliz 都不 open。
module ReviewTests =

    // ---- 语料 ----

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

    let private liveOf (model: TableModel) : LiveTable =
        match TablePage.live model with
        | Some live -> live
        | None -> failwith "这几条用例跑的是 `?table=1` 那一页，它必然是 Live"

    let private tableOf (model: TableModel) : Table =
        match (liveOf model).Table with
        | Ok table -> table
        | Error error -> failwith $"这一桌应当开得起来，却得到「{error}」"

    /// 一次回执，**不带分布**（票 93 那一行今天的样子，也是老产物上的样子）。
    let private answered (actionId: int option) (latencyMs: int) : BaselineAnswer =
        {
            ActionId = actionId
            Failure = None
            LatencyMs = latencyMs
            Candidates = []
            CandidatesTotal = 0
        }

    /// 一次回执，**带上它那一次前向给的候选**（票 103）：
    /// `total` 是上游一共给了几条（可能比认得出来的多）。
    let private answeredWith (actionId: int option) (candidates: (int * float) list) (total: int) : BaselineAnswer =
        { answered actionId 5 with
            Candidates = candidates |> List.map (fun (id, p) -> { ActionId = id; P = p })
            CandidatesTotal = total
        }

    /// 强 AI 那一行上**一个都不允许出现**的词。
    ///
    /// 前六个是票 92/93 的（别为它编一句理由、别造总分）；
    /// **后六个是票 103 的**：概率不是理由，把 `p=0.95` 写成「它很确定」
    /// 是替一个不会说话的网络编话，**而且编得越顺口越像真的**。
    let private forbidden =
        [ "因为"; "理由"; "评分"; "总分"; "暂无"; "错"; "确定"; "犹豫"; "认为"; "建议"; "把握"; "自信" ]

    let private notesOf (index: int) (model: TableModel) : ReviewNote list =
        match Review.shown model with
        | ReviewShown.Notes(shownSeat, notes) ->
            Assert.Equal(seat index, shownSeat)
            notes
        | other -> failwith $"这一刻该有座位 {index} 的复盘，却是 {other}"

    // ---- 代点：轮到真人时点哪一条 ----

    /// 一条试打的三个量，**按「越前面越好」排**（向听不退向 > 有效牌多 > 危险度低）。
    ///
    /// **它是用例自己写的判据，不是 `Review` 里那一份**：`List.maxBy` 取到的那条
    /// 在这三项上按字典序最大，因此**不可能被别的候选帕累托占优**
    /// （占优要求三项都不差、至少一项更好，那样它的字典序就更大了）。
    /// 于是「打字典序最大的那张 ⇒ 更好的候选恒为空」是一条从**定义**推出来的期望，
    /// 而不是拿 `Review` 的实现对它自己（判据 6）。
    let private rank (trial: DahaiScaffold) : int * int * int =
        -trial.ShantenDelta,
        trial.Ukeire |> Option.map Ukeire.total |> Option.defaultValue 0,
        -(trial.Danger
          |> Option.map (fun danger -> DangerTier.order danger.Tier)
          |> Option.defaultValue 0)

    /// 这一手点哪一条：`choose` 从脚手架里挑一张牌，挑不出来就点包里头一条打牌。
    let private picked (choose: Scaffold -> DahaiScaffold option) (package: DecisionPackage) : int option =
        let byScaffold =
            DecisionPackage.scaffold package
            |> Option.bind choose
            |> Option.bind (fun trial -> List.tryHead trial.ActionIds)

        match byScaffold with
        | Some id -> Some id
        | None ->
            HumanSeat.dahaiOptions package
            |> List.tryHead
            |> Option.map (fun (id, _, _, _) -> id)

    /// **打最好的那张**（字典序最大，见 `rank`）。
    let private bestPick (package: DecisionPackage) : int option =
        picked (fun scaffold -> scaffold.Dahai |> List.sortByDescending rank |> List.tryHead) package

    /// **打最差的那张**（字典序最小）：这就是「构造一个明显打错的局面」——一整场都在明显打错。
    let private worstPick (package: DecisionPackage) : int option =
        picked (fun scaffold -> scaffold.Dahai |> List.sortBy rank |> List.tryHead) package

    /// 真人坐座位 0、其余三家均匀随机的那一桌（`?table=1` 的默认种子）。
    let private humanTable () : TableModel =
        SeatingPlan.initial Ruleset.yonma
        |> SeatingPlan.bind (seat 0) SeatChoice.Human
        |> TablePage.initial RulesetDraft.initial
        |> fst

    /// 把这一桌往前推：轮到真人就按 `pick` 点一条（响应阶段按「过」），否则单步。
    /// 走到终局、或者走满 `moves` 步就停（`moves` 用来取一个**对局中**的中间态）。
    ///
    /// 形状照 `HumanSeatTests.playedOut`（那一份是票 87/88 的），这里只多一个「点哪一条」的钩子。
    let private drive (pick: DecisionPackage -> int option) (moves: int) (model: TableModel) : TableModel =
        let moved (model: TableModel) : TableModel =
            match TablePage.humanTurn model with
            | Some package ->
                match pick package, HumanSeat.pass package with
                | Some id, _ -> step (HumanPlayed id) model
                | None, Some pass -> step (HumanPlayed pass.Id) model
                | None, None -> failwith "轮到真人，却既没牌可打也没有「过」可按"
            | None ->
                let table = tableOf model

                if Table.isKyokuEnded table then
                    step KyokuAdvanced model
                else
                    step Advanced model

        let rec play (count: int) (model: TableModel) : TableModel =
            if count >= moves || Option.isSome (Table.result (tableOf model)) then
                model
            else
                play (count + 1) (moved model)

        play 0 model

    /// 一整场东风战约 440 步（票 87 探针实测 363 步 + 74 次点击）。
    let private wholeGame = 3000

    let private settledWith (pick: DecisionPackage -> int option) : TableModel =
        let model = humanTable () |> drive pick wholeGame

        if Option.isNone (Table.result (tableOf model)) then
            failwith $"这一场该在 {wholeGame} 步之内打完，却还没终局"

        model

    /// 三种代点各打一整场（贵：每一场都要跑完 + 逐手现搭决策包，因此一整个模块共用）。
    let private settledFirst = lazy settledWith bestPick
    let private settledWorst = lazy settledWith worstPick

    // ---- 锚点：另一条路重走一遍这份牌谱 ----

    /// 这份牌谱里**这一席落定的每一手**：手序、那个动作、以及**落定之前**那一刻的局面。
    ///
    /// **它不走 `Table.replay`**（页面那一侧的帧就是它 fold 出来的）：这里从
    /// `Replay.traceOfPaifu` 拿到逐局的开局局面与动作序列，自己 `GameState.step` 一遍。
    /// 手序按引擎的口径跨局累计（`Table.Turns` 那个号）。
    let private walked (target: Seat) (paifu: Paifu) : (int * Action * GameState) list =
        let kyokus =
            match Replay.traceOfPaifu paifu with
            | Ok kyokus -> kyokus
            | Error error -> failwith $"这份牌谱回放不动：{ReplayError.toDisplay error}"

        let played (turns: int, found: (int * Action * GameState) list, state: GameState) (action: Action) =
            let next =
                match GameState.step state action with
                | Ok(next, _) -> next
                | Error illegal -> failwith $"第 {turns} 手引擎拒了：{IllegalAction.toDisplay illegal}"

            let taken =
                if Action.actor action = target then
                    (turns, action, state) :: found
                else
                    found

            turns + 1, taken, next

        let kyoku (turns: int, found: (int * Action * GameState) list) (each: ReplayKyoku) =
            let turns, found, _ =
                ((turns, found, each.Opening), each.Actions) ||> List.fold played

            turns, found

        let _, found = ((0, []), kyokus) ||> List.fold kyoku
        List.rev found

    /// 真人那一桌到此刻为止的牌谱（Live 侧的复盘读的就是它 fold 出来的帧）。
    let private paifuOf (model: TableModel) : Paifu =
        match TablePage.rosterOf model with
        | Some roster -> Table.paifu roster (tableOf model)
        | None -> failwith "Live 那一桌必然有配桌"

    /// **第三个锚点**：绕开 `Scaffold`，拿 `Ukeire.calculate` 直接算「打完这一张之后的有效牌」。
    ///
    /// 算法与 `Scaffold.trials` 那一段同形（打出去那张要进可见集：它马上就落到自己河里），
    /// 但走的是引擎的公开 API 而不是脚手架——同一个数由两条路各算一遍。
    let private ukeireAfter (ruleset: Ruleset) (observation: Observation) (pai: Tile) : int option =
        let self = observation.Self

        HandShape.create (List.length self.Naki) self.Hand
        |> Result.bind (HandShape.remove pai)
        |> Result.toOption
        |> Option.bind (fun after ->
            Ukeire.calculate ruleset.TileKinds (pai :: Observation.visible observation) after
            |> Result.toOption)
        |> Option.map Ukeire.total

    // ---- 只在终局后 ----

    [<Fact>]
    let ``对局中一条标注都没有：复盘整块不在这一屏上，终局之后才出现`` () =
        let midway = humanTable () |> drive bestPick 120

        // 阳性对照：这一桌真的在打（真人已经出过手），而不是一桌没开起来的空局。
        Assert.True((tableOf midway).Turns > 20, "这一刻该已经打了二十几手")
        Assert.True(Option.isNone (Table.result (tableOf midway)), "这一刻这一场还没打完")
        Assert.False(Review.settled midway)
        Assert.Equal(None, Review.addressed midway)
        Assert.Equal(ReviewShown.Hidden, Review.shown midway)

        // 打完之后：同一桌、同一席，标注全出来了。
        let settled = settledFirst.Force()
        Assert.True(Review.settled settled)
        Assert.Equal(Some(seat 0), Review.addressed settled)
        Assert.NotEmpty(notesOf 0 settled)

    // ---- 每一手都有一条 ----

    [<Fact>]
    let ``真人那一席的每一手都有一条标注：手序与牌谱里逐个相同`` () =
        let model = settledFirst.Force()
        let notes = notesOf 0 model

        // 锚点：另一条路重走一遍这份牌谱（`Replay` + `GameState.step`），数它落定了哪几手。
        let walk = walked (seat 0) (paifuOf model)

        Assert.NotEmpty walk
        Assert.Equal<int list>(walk |> List.map (fun (turn, _, _) -> turn), notes |> List.map (fun note -> note.Turn))

        // 那一手做的是什么，两边说的也得是同一件事（label 与 mjai 动作名各对一遍）。
        for (_, action, _), note in List.zip walk notes do
            Assert.Equal(Action.toDisplay action, note.Label)
            Assert.Equal(HumanSeat.kind action, note.Kind)

        // 判据 3：这几种情形在真语料上各执行了几次——为 0 的那一种，这条用例什么都没证明。
        let dahai = notes |> List.filter (fun note -> note.Kind = "dahai")
        let others = notes |> List.filter (fun note -> note.Kind <> "dahai")
        Assert.True(List.length dahai > 40, $"一整场该有几十手打牌，实得 {List.length dahai}")
        Assert.NotEmpty others // 「过」与鸣牌同样占一手，复盘不许漏
        Assert.All(notes, (fun note -> Assert.True(Option.isSome note.Scaffold, $"第 {note.Turn} 手连向听都没算出来")))
        Assert.All(dahai, (fun note -> Assert.True(Option.isSome note.Trial, $"第 {note.Turn} 手打了牌却没有那一条试打")))
        Assert.All(others, (fun note -> Assert.Equal(None, note.Trial)))

    // ---- 每一个数都是引擎算的 ----

    [<Fact>]
    let ``每一条上那几个数与引擎直接算的逐字相同（有效牌再由第三个锚点核一遍）`` () =
        let model = settledFirst.Force()
        let notes = notesOf 0 model
        let ruleset = (tableOf model).Game |> Game.ruleset
        let walk = walked (seat 0) (paifuOf model)

        // 逐手核完之后再数「这几条断言各开口了几次」（判据 3）。**不用可变累加器**：
        // 每一手核出来的那两个 bool 收成一张表，末尾 `List.sumBy` 数一遍。
        let opened =
            List.zip walk notes
            |> List.choose (fun ((turn, action, state), note) ->
                // 引擎在**那一刻**给这一席的那份决策包（与当时真的问出去的那一份同一个构造子）。
                let package =
                    match DecisionPackage.forSeat (seat 0) state with
                    | Some package -> package
                    | None -> failwith $"第 {turn} 手引擎该给得出一份决策包"

                let scaffold =
                    match DecisionPackage.scaffold package with
                    | Some scaffold -> scaffold
                    | None -> failwith $"第 {turn} 手引擎该给得出一份脚手架"

                Assert.Equal(Some scaffold.Shanten, note.Scaffold |> Option.map (fun each -> each.Shanten))

                match action with
                | Action.Dahai(_, pai, _) ->
                    let trial =
                        match note.Trial with
                        | Some trial -> trial
                        | None -> failwith $"第 {turn} 手打了牌，标注上却没有那一条试打"

                    let expected =
                        match scaffold.Dahai |> List.tryFind (fun each -> each.Pai = Tile.deaka pai) with
                        | Some expected -> expected
                        | None -> failwith $"第 {turn} 手打的那张不在引擎的试打里"

                    Assert.Equal(expected.Shanten, trial.Shanten)
                    Assert.Equal(expected.ShantenDelta, trial.ShantenDelta)
                    Assert.Equal(expected.Ukeire |> Option.map Ukeire.total, trial.Ukeire |> Option.map Ukeire.total)
                    Assert.Equal(expected.Danger, trial.Danger)

                    // 第三个锚点：同一个数由 `Ukeire.calculate` 再算一遍（绕开脚手架）。
                    let another =
                        ukeireAfter ruleset (DecisionPackage.observation package) (Tile.deaka pai)

                    Assert.Equal(another, trial.Ukeire |> Option.map Ukeire.total)
                    Some(Option.isSome trial.Ukeire, Option.isSome trial.Danger)
                | _ -> None)

        let counted (which: bool * bool -> bool) : int =
            opened |> List.sumBy (fun each -> if which each then 1 else 0)

        // 判据 3：一条永远执行不到的断言与一条从不失败的断言，危害相同。
        Assert.True(counted fst > 40, $"有效牌那一条只执行了 {counted fst} 次")
        Assert.True(counted snd > 0, $"危险度那一条执行了 {counted snd} 次：这一场里没人立直也没人副露？")

    // ---- 更好的候选 ----

    [<Fact>]
    let ``故意每一手都打最差的那张：更好的候选真的列得出来，头一条就是引擎算出的最优`` () =
        let model = settledWorst.Force()
        let notes = notesOf 0 model

        let advised =
            notes
            |> List.filter (fun note -> Option.isSome note.Trial && not (List.isEmpty note.Better))

        Assert.True(List.length advised > 20, $"一整场都在明显打错，却只有 {List.length advised} 手列得出更好的候选")

        // 差得最多的那一手：把它逐项摊开核一遍（这就是票面要的那个「明显打错的局面」）。
        let worst =
            advised
            |> List.maxBy (fun note ->
                note.Better
                |> List.map (fun each -> each.UkeireGain |> Option.defaultValue 0)
                |> List.fold max 0)

        let played =
            match worst.Trial with
            | Some trial -> trial
            | None -> failwith "这一条必然是打牌那一手"

        let scaffold =
            match worst.Scaffold with
            | Some scaffold -> scaffold
            | None -> failwith "这一条必然算得出脚手架"

        let total (trial: DahaiScaffold) =
            trial.Ukeire |> Option.map Ukeire.total |> Option.defaultValue 0

        // 期望值由**引擎那份试打表**独立算出来：向听不比你差的那几张里，有效牌最多的那一张。
        let expected =
            scaffold.Dahai
            |> List.filter (fun trial -> trial.ShantenDelta <= played.ShantenDelta)
            |> List.maxBy total

        let head = List.head worst.Better
        Assert.Equal(expected.Pai, head.Pai)
        Assert.Equal(Some(total expected), head.Ukeire)
        Assert.Equal(Some(total expected - total played), head.UkeireGain)
        Assert.True(total expected - total played >= 10, $"这一手该差一大截，实差 {total expected - total played} 枚")

        // 那一栏说出来的话里，牌与枚数都在。
        let said = ReviewNote.advice worst
        Assert.True(said |> Option.exists (fun said -> said.StartsWith "更好的候选："), $"那一栏写的是 {said}")
        Assert.True(said |> Option.exists (fun said -> said.Contains(Tile.toDisplay expected.Pai)))
        Assert.True(said |> Option.exists (fun said -> said.Contains(string (total expected))))

        // 至多三条（复盘要的是挑出来的那几张，不是把 13 张重排一遍）。
        Assert.All(notes, (fun note -> Assert.True(List.length note.Better <= 3)))

    [<Fact>]
    let ``每一手都打帕累托最优的那张：那一栏恒是「这一手是当时的最优之一」`` () =
        let notes = settledFirst.Force() |> notesOf 0

        let dahai = notes |> List.filter (fun note -> Option.isSome note.Trial)
        Assert.True(List.length dahai > 40, "阳性对照：这一场真的打了几十手牌")

        // `bestPick` 取的是三项上字典序最大的那一张，它**不可能被别的候选帕累托占优**
        // （见 `rank` 那段注释）。于是这一栏必须一条都列不出来，而且要明说。
        for note in dahai do
            Assert.True(List.isEmpty note.Better, $"第 {note.Turn} 手打的是字典序最优的那张，却列出了 {List.length note.Better} 个更好的候选")

            Assert.Equal(Some "这一手是当时的最优之一。", ReviewNote.advice note)

        // 「这一手没打牌」那几条根本没有这一栏（不是空字符串——那两件事在页面上分得开）。
        for note in notes |> List.filter (fun note -> Option.isNone note.Trial) do
            Assert.Equal(None, ReviewNote.advice note)

    // ---- 主语是谁 ----

    [<Fact>]
    let ``真人在座时复盘的主语恒是他：切到上帝视角也不变`` () =
        let model = settledFirst.Force()

        // 终局之后视角是松开的（票 87 的 `unlocked`），因此这一条真的走得到上帝视角。
        let god = model |> step (ViewpointPicked Viewpoint.God)
        Assert.Equal(Viewpoint.God, TablePage.viewpoint god)
        Assert.Equal(Some(seat 0), Review.addressed god)

        let elsewhere = model |> step (ViewpointPicked(Viewpoint.Seated(seat 2)))
        Assert.Equal(Some(seat 0), Review.addressed elsewhere)

    [<Fact>]
    let ``模型席也能看：回放里坐到某一席就有那一席的逐手复盘，上帝视角没有主语`` () =
        let replay =
            TablePage.home () |> fst |> step (DemoLoaded(Ok demo)) |> step (CursorMoved 60)

        // 首页默认上帝视角（裁决 71-8）：打完了，但这一屏没有主语。
        Assert.True(Review.settled replay)
        Assert.Equal(Viewpoint.God, TablePage.viewpoint replay)
        Assert.Equal(None, Review.addressed replay)
        Assert.Equal(ReviewShown.Unaddressed, Review.shown replay)

        // 坐到座位 1：那一席的逐手复盘就出来了（**复盘不是真人专属**）。
        let seated = replay |> step (ViewpointPicked(Viewpoint.Seated(seat 1)))
        let notes = notesOf 1 seated
        let walk = walked (seat 1) demo

        Assert.NotEmpty notes
        Assert.Equal<int list>(walk |> List.map (fun (turn, _, _) -> turn), notes |> List.map (fun note -> note.Turn))
        Assert.All(notes, (fun note -> Assert.Equal(seat 1, note.Seat)))

    // ---- 点某一手：跳过去、回得来（票 86 的回程） ----

    [<Fact>]
    let ``点某一手：游标跳到那一帧，收起来回到点开之前那一处`` () =
        let model =
            TablePage.home ()
            |> fst
            |> step (DemoLoaded(Ok demo))
            |> step (ViewpointPicked(Viewpoint.Seated(seat 1)))
            |> step (CursorMoved 200)

        let timelineOf (model: TableModel) : Timeline =
            match TablePage.timeline model with
            | Some timeline -> timeline
            | None -> failwith "回放这一刻该有一根时间轴"

        Assert.Equal(200, (timelineOf model).Cursor)
        Assert.Equal(None, Review.opened model)

        // 挑一条落在游标**前面**的标注：跳过去才看得出游标真的动了。
        let note =
            notesOf 1 model
            |> List.filter (fun note -> note.Frame < 200 && note.Kind = "dahai")
            |> List.last

        let opened = model |> step (RecordOpened(Some note.Turn))

        // 轴只有一根（ADR-0002）：点一条标注就是把这根轴搬到那一帧，不另开一条。
        Assert.Equal(note.Frame, (timelineOf opened).Cursor)
        Assert.Equal(Some note.Turn, Review.opened opened)
        Assert.Equal(note.Turn, (timelineOf opened).Turns - 1)

        // 面板还在（复盘读的是整份牌谱，不是游标停在哪儿）：那几条标注一条不少。
        Assert.Equal<int list>(
            notesOf 1 model |> List.map (fun note -> note.Turn),
            notesOf 1 opened |> List.map (fun note -> note.Turn)
        )

        // 收起来：游标回到点开之前那一处（票 86 立的回程规矩）。
        let closed = opened |> step (RecordOpened None)
        Assert.Equal(200, (timelineOf closed).Cursor)
        Assert.Equal(None, Review.opened closed)

    // ---- 强 AI 那一行：问之前一行都没有 ----

    [<Fact>]
    let ``没问之前强 AI 那一行整行不出现：一条标注只说得出三句话，里面没有「暂无」`` () =
        let notes = settledWorst.Force() |> notesOf 0

        for note in notes do
            let said =
                [
                    Some(ReviewNote.headline note)
                    Some(ReviewNote.figures note)
                    ReviewNote.advice note
                ]
                |> List.choose id

            Assert.InRange(List.length said, 2, 3)

            for line in said do
                Assert.DoesNotContain("暂无", line)
                // 那几 MB 没拉、没问过之前，引擎自算那几句里一个字都不提强 AI（票 93）。
                Assert.DoesNotContain("强 AI", line)
                // **不造总分**（票 90 与票 93 同一条边界）：一条标注里不许出现「分」这种口径。
                Assert.DoesNotContain("评分", line)

    // ---- 强 AI 那一行：拿哪一份观测去问它（票 93 的全部难点） ----

    [<Fact>]
    let ``问强 AI 时交出去的就是那一手当时喂给该席的那一份投影（上帝视角那一份真的不同）`` () =
        let model = settledFirst.Force()
        let notes = notesOf 0 model
        let walk = walked (seat 0) (paifuOf model)

        // 每一手都问得出去：这一席落定了几手，就有几份投影（一份都不允许漏）。
        Assert.Equal(List.length notes, List.length (Review.requests notes))
        Assert.Equal<int list>(notes |> List.map (fun note -> note.Turn), Review.requests notes |> List.map fst)

        // 逐手与**另一条路**重建的那一份逐字对拍：页面那侧走 `Table.replay` 的帧，
        // 这里走 `Replay.traceOfPaifu` + `GameState.step`（判据 6）。
        // 对的是 **wire 上那一串字节**：真正交给强 AI 的就是它。
        let encoded (package: DecisionPackage) : string =
            DecisionPackage.encoder package |> Encode.toString 0

        // 对的是 `Review.requests`（**真正交出去的那一叠**）而不是 `note.Package`：
        // 两者今天是同一个值，但这一条要钉的是前者——喂错东西的地方在那一步。
        let leaked =
            List.zip3 walk notes (Review.requests notes)
            |> List.map (fun ((turn, _, state), note, (asked, mine)) ->
                Assert.Equal(turn, asked)

                let theirs =
                    match DecisionPackage.forSeat (seat 0) state with
                    | Some package -> package
                    | None -> failwith $"第 {turn} 手引擎该给得出一份决策包"

                Assert.Equal(encoded theirs, encoded mine)
                Assert.Equal(Some(encoded mine), note.Package |> Option.map encoded)

                // **结构上漏不出去**（票 29a 的那条唯一掩蔽法则）：交出去的那条流里，
                // 他家摸的那张牌面根本没有位置。两头一起数（判据 3）：
                //   `hidden` = 投影里被遮着的他家摸牌；`godly` = 上帝视角那条流里同一批摸牌。
                // godly > 0 是**阳性对照**：没有它，「一张都没漏」也可能只是这一局压根没人摸牌。
                let history = DecisionPackage.history mine

                let seen =
                    history
                    |> List.sumBy (fun event ->
                        match event with
                        | MaskedEvent.Tsumo(actor, Some _) when actor <> seat 0 -> 1
                        | _ -> 0)

                let hidden =
                    history
                    |> List.sumBy (fun event ->
                        match event with
                        | MaskedEvent.Tsumo(actor, None) when actor <> seat 0 -> 1
                        | _ -> 0)

                let godly =
                    GodView.stream state
                    |> List.sumBy (fun event ->
                        match event with
                        | Event.Tsumo(actor, _) when actor <> seat 0 -> 1
                        | _ -> 0)

                Assert.Equal(0, seen)
                Assert.Equal(godly, hidden)
                godly)

        // 阳性对照：上帝视角那条流里真的摆着一大批他家摸的牌，而交出去的那一份一张都没有。
        Assert.True(List.sum leaked > 200, $"上帝视角那侧只多出 {List.sum leaked} 张牌：这一条什么都没证明")

    [<Fact>]
    let ``它交回来的那个 id → 那一行：交不出来、认不出来的那几手整行不出现`` () =
        let notes = settledFirst.Force() |> notesOf 0

        let note =
            match notes |> List.tryFind (fun note -> note.Kind = "dahai") with
            | Some note -> note
            | None -> failwith "一整场总该有一手打牌"

        let package =
            match note.Package with
            | Some package -> package
            | None -> failwith "这一手必然有投影"

        let options = DecisionPackage.options package

        let played =
            match DecisionPackage.tryId note.Played package with
            | Some id -> id
            | None -> failwith "他打的那一手必然在那一包里"

        // **算不动就整行不出现**：交不出来（None）与说了一个不存在的 id 都是同一个下场。
        Assert.Equal(None, Review.strongOf note (answered None 3))
        Assert.Equal(None, Review.strongOf note (answered (Some(List.length options)) 3))
        Assert.Equal(None, Review.strongOf note (answered (Some -1) 3))

        // 它正好选了你那一手：不是分歧。
        let same =
            match Review.strongOf note (answered (Some played) 7) with
            | Some row -> row
            | None -> failwith "包里那一条 id 该换得出一行"

        Assert.False(same.Differs)
        Assert.Equal(note.Label, same.Label)
        Assert.Equal(note.Turn, same.Turn)
        Assert.Equal(7, same.LatencyMs)
        Assert.StartsWith("dahai:", same.Key)
        Assert.Contains("与你相同", ReviewStrong.toDisplay same)

        // 它选了别的：标「不同」——**而不是「你错了」**（票面边界：不造总分）。
        let elsewhere =
            match options |> List.tryFind (fun option -> ActionOption.id option <> played) with
            | Some option -> ActionOption.id option
            | None -> failwith "这一手只有一条可选，换一手来量分歧"

        let differs =
            match Review.strongOf note (answered (Some elsewhere) 1) with
            | Some row -> row
            | None -> failwith "包里那一条 id 该换得出一行"

        Assert.True(differs.Differs)
        Assert.Equal(1, Review.disagreements [ same; differs ])

        let said = ReviewStrong.toDisplay differs
        Assert.Contains("〔强 AI〕", said)
        Assert.Contains("与你不同", said)

        // **它不给理由，所以这一行不得凭空长出一句话**（票 92 的要害），
        // 也不得长出一个分数（票 93 的边界）。
        for word in forbidden do
            Assert.DoesNotContain(word, said)

        // **上游没给分布时逐字退回票 93 那一句**（票 103）：不是空白、也不是「暂无」。
        Assert.Equal($"〔强 AI〕{differs.Label}（与你不同）", said)
        Assert.Empty(differs.Candidates)
        Assert.Equal(0, differs.CandidatesTotal)
        Assert.Equal(None, differs.Yours)

    // ---- 强 AI 那一行：它有多确定（票 103） ----

    /// 一手打牌，连同它那一包里可挑的几条 id（**至少四条**：要摆得下三条候选加一条别的）。
    let private handWithOptions () : ReviewNote * DecisionPackage * int list =
        let notes = settledFirst.Force() |> notesOf 0

        let picked =
            notes
            |> List.tryPick (fun note ->
                match note.Package with
                | Some package when List.length (DecisionPackage.options package) >= 4 ->
                    Some(note, package, DecisionPackage.options package |> List.map ActionOption.id)
                | _ -> None)

        match picked with
        | Some found -> found
        | None -> failwith "一整场里总该有一手可挑的动作在四条以上"

    [<Fact>]
    let ``它那一次前向给的候选照原样带过来：几条、各自多少、排第几都是上游那几个数`` () =
        let note, package, ids = handWithOptions ()

        let labelOf (id: int) =
            DecisionPackage.options package
            |> List.tryFind (fun option -> ActionOption.id option = id)
            |> Option.map ActionOption.label
            |> Option.defaultValue "?"

        // 上游给的就是这三条（`SHOW_TOP_N = 3`，实测形状见报告 103）。
        let candidates = [ ids[0], 0.5961328; ids[1], 0.40268; ids[2], 0.0010278977 ]

        let row =
            match Review.strongOf note (answeredWith (Some ids[0]) candidates 3) with
            | Some row -> row
            | None -> failwith "包里那一条 id 该换得出一行"

        Assert.Equal(3, row.CandidatesTotal)
        Assert.Equal<int list>(ids |> List.truncate 3, row.Candidates |> List.map (fun each -> each.ActionId))
        // 序号是**上游那一列**里的位置，不是我们重排出来的。
        Assert.Equal<int list>([ 1; 2; 3 ], row.Candidates |> List.map (fun each -> each.Rank))
        // 概率**一位都不动**：这一层不归一化、不四舍五入（页面上那句中文才舍到两位）。
        Assert.Equal<float list>([ 0.5961328; 0.40268; 0.0010278977 ], row.Candidates |> List.map (fun each -> each.P))
        // 那一条叫什么由引擎说了算（`ActionOption.label`），不是我们拼的。
        Assert.Equal<string list>(
            ids |> List.truncate 3 |> List.map labelOf,
            row.Candidates |> List.map (fun each -> each.Label)
        )

        let said = ReviewStrong.toDisplay row

        Assert.Contains("它给了 3 条", said)
        Assert.Contains($"{labelOf ids[0]} 0.60", said)
        Assert.Contains($"{labelOf ids[1]} 0.40", said)
        // 末一条小到两位小数写不出来时给的是一个**界**，不是一个假的 0.00（报告 103 §3）。
        Assert.Contains($"{labelOf ids[2]} <0.01", said)

        // **一句人话都没有**（这一票的全部风险）：既没有理由，也没有「很确定 / 犹豫」这类度量词。
        for word in forbidden do
            Assert.DoesNotContain(word, said)

    [<Fact>]
    let ``你打的那一手在它的候选里排第几：它排第 2 时那一栏说的就是第 2`` () =
        let note, package, ids = handWithOptions ()

        let played =
            match DecisionPackage.tryId note.Played package with
            | Some id -> id
            | None -> failwith "他打的那一手必然在那一包里"

        let elsewhere = ids |> List.filter (fun id -> id <> played)

        // 它打的是别的（第 1 条），而**你打的那一手是它的第 2 条**——票面点名的那种局面。
        let candidates = [ elsewhere[0], 0.62; played, 0.35; elsewhere[1], 0.03 ]

        let row =
            match Review.strongOf note (answeredWith (Some elsewhere[0]) candidates 3) with
            | Some row -> row
            | None -> failwith "包里那一条 id 该换得出一行"

        Assert.True(row.Differs)

        match row.Yours with
        | Some yours ->
            Assert.Equal(played, yours.ActionId)
            Assert.Equal(2, yours.Rank)
            Assert.Equal(0.35, yours.P)
        | None -> failwith "你打的那一手就在它给的那三条里，这一格不该是空的"

        let said = ReviewStrong.toDisplay row
        Assert.Contains($"你打的{note.Label} 0.35（第 2）", said)

        for word in forbidden do
            Assert.DoesNotContain(word, said)

    [<Fact>]
    let ``你打的那一手不在它给的那几条里：说「不在这几条里」，不说「它给了 0」`` () =
        let note, package, ids = handWithOptions ()

        let played =
            match DecisionPackage.tryId note.Played package with
            | Some id -> id
            | None -> failwith "他打的那一手必然在那一包里"

        let elsewhere = ids |> List.filter (fun id -> id <> played) |> List.truncate 3

        let row =
            match
                Review.strongOf
                    note
                    (answeredWith (Some elsewhere[0]) (elsewhere |> List.mapi (fun i id -> id, 0.5 / float (i + 1))) 3)
            with
            | Some row -> row
            | None -> failwith "包里那一条 id 该换得出一行"

        Assert.Equal(None, row.Yours)

        let said = ReviewStrong.toDisplay row
        Assert.Contains($"你打的{note.Label}：不在这 3 条里", said)
        // **上游只抬前几条**：没抬到不等于它给了零，因此这一栏一个数都不许出现。
        Assert.DoesNotContain("你打的", said.Replace($"你打的{note.Label}：不在这 3 条里", ""))
        Assert.DoesNotContain("0.00", said)

        for word in forbidden do
            Assert.DoesNotContain(word, said)

    [<Fact>]
    let ``这一包里认不出的那一条不占位，但上游给了几条照实说，而且序号不重排`` () =
        let note, _, ids = handWithOptions ()
        let missing = (ids |> List.max) + 7

        // 上游给了三条，中间那一条这一包里根本没有（例如上游把两张牌归并到了同一条）。
        let row =
            match Review.strongOf note (answeredWith (Some ids[0]) [ ids[0], 0.7; missing, 0.2; ids[1], 0.1 ] 3) with
            | Some row -> row
            | None -> failwith "包里那一条 id 该换得出一行"

        Assert.Equal(2, List.length row.Candidates)
        Assert.Equal(3, row.CandidatesTotal)
        // **序号不重排**：第三条仍旧是第 3 条——重排的话「第 2」就不再是上游那张表里的第 2 了。
        Assert.Equal<int list>([ 1; 3 ], row.Candidates |> List.map (fun each -> each.Rank))

        let said = ReviewStrong.toDisplay row
        Assert.Contains("它给了 3 条（这一包里认得出 2 条）", said)

        for word in forbidden do
            Assert.DoesNotContain(word, said)

    [<Fact>]
    let ``两位小数只出现在给人看的那一句里；data-* 那一串一位都不舍`` () =
        // 给人看的那一半：两位小数，两头各拦一道（写成 0.00 就成了「它给了零」，
        // 写成 1.00 就成了「它给了全部」——两者都是把事实舍成了另一件事）。
        Assert.Equal("0.60", ReviewStrong.probabilityToDisplay 0.5961328)
        Assert.Equal("0.35", ReviewStrong.probabilityToDisplay 0.35)
        Assert.Equal("<0.01", ReviewStrong.probabilityToDisplay 0.0010278977)
        Assert.Equal(">0.99", ReviewStrong.probabilityToDisplay 0.9999)
        Assert.Equal("1.00", ReviewStrong.probabilityToDisplay 1.0)
        Assert.Equal("0.00", ReviewStrong.probabilityToDisplay 0.0)

        // 给机器看的那一半：**最短往返表示**，闸门拿它与 wasm 印出来的那一串逐位对拍。
        for p in [ 0.5961328; 0.40268; 0.0010278977; 1.0; 0.0 ] do
            let wire = ReviewStrong.probabilityToWire p
            Assert.Equal(p, Double.Parse(wire, Globalization.CultureInfo.InvariantCulture))
            Assert.DoesNotContain("E", wire)

        Assert.Equal("0.5961328", ReviewStrong.probabilityToWire 0.5961328)

    // ---- 复盘上一个自造的合成数都不出（票 107） ----
    //
    // 旧那几条逐项对拍是逐**字段**比（向听 / 进退向 / 有效牌 / 危险度各一条 `Assert.Equal`）：
    // 它们证明「这几个数没被改」，**不证明「没有第五个数被凭空加上去」**——票 104 往每一条标注
    // 末尾拼了一个「总分 82」，整套 `ci.sh` 全绿（报告 104 的红-7b）；当时守着它的只有
    // 一张**词的黑名单**（`forbidden` 与那一条「里面没有『暂无』」），换个词就穿过去了。
    //
    // 这一族换一个问法：**这个数是谁的**。黑名单留着当第二道（便宜、拦得住措辞），
    // 这里守的是溯源。**浏览器那一侧同形的一份在 `web/scripts/verify-review.mjs` 的⑥**
    // （量的是真画出来那一刻的 DOM，判据 20）；两份各写各的，**强 AI 那一行只有这一侧有执行者**
    // ——CI 里那份 6 MB 不在场，页面上那一行根本画不出来（判据 3）。

    /// 一句话里的**每一个数**（含小数与正负号）。
    ///
    /// **这一族用例认的是「数」不是「词」**：措辞怎么改都不动它，而凭空多印一个数
    /// ——不论它叫「总分」「期望打点」还是根本没有名字（行尾一个光秃秃的 `82`）——就多出一项。
    /// 从前守这件事的只有一张词的黑名单（`forbidden`），换个词就穿过去了（票 104 的红-7b）。
    let private numerals (said: string) : string list =
        Regex.Matches(said, @"[+-]?\d+(?:\.\d+)?")
        |> Seq.map (fun each -> each.Value)
        |> List.ofSeq

    /// 一个印在复盘上的数：它是引擎（或上游）那一份里的**哪一格**，以及那一格印出来长什么样。
    type private Sourced = { Where: string; Said: string }

    let private fromValue (where: string) (said: string) : Sourced = { Where = where; Said = said }

    let private fromInt (where: string) (value: int) : Sourced = { Where = where; Said = string value }

    /// 向听那一格：**听牌与和了印出来一个数都没有**（`0 向听` / `-1 向听` 都不是人话）。
    let private fromShanten (where: string) (shanten: Shanten) : Sourced =
        let value = Shanten.value shanten

        {
            Where = where
            Said = (if value >= 1 then string value else "")
        }

    /// 概率那一格（票 103）：两位小数，两头各一句界。
    ///
    /// **用例自己写的一份**（判据 6）：拿 `ReviewStrong.probabilityToDisplay` 当期望，
    /// 等于用同一份实现证明它自己——那样它里面凭空多印一个数也照样绿。
    let private fromProbability (where: string) (p: float) : Sourced =
        let said =
            if p > 0.0 && p < 0.005 then
                "<0.01"
            elif p < 1.0 && p >= 0.995 then
                ">0.99"
            else
                p.ToString("F2", Globalization.CultureInfo.InvariantCulture)

        { Where = where; Said = said }

    /// **逐数溯源**：这一句话里印出来的每一个数，逐个等于那张来源表里的那一格。
    ///
    /// 多一个数、少一个数、哪一格印错了值，都在这里当场红；**它不问那个数叫什么**
    /// ——「总分」「期望打点」与一个光秃秃的 `82` 在这条判据下是同一件事。
    /// 返回这一句核过几个数（判据 3：闸门要报得出自己开了几次口）。
    let private traced (where: string) (sources: Sourced list) (said: string) : int =
        let expected = sources |> List.collect (fun each -> numerals each.Said)
        let printed = numerals said

        if expected <> printed then
            let table =
                sources
                |> List.map (fun each -> $"{each.Where} = 「{each.Said}」")
                |> String.concat "；"

            let left = String.concat "，" printed
            let right = String.concat "，" expected

            failwith $"{where}「{said}」印出来的数是 [{left}]，而指得回引擎那一份的只有 [{right}]（{table}）"

        List.length printed

    /// 抬头那一句上那几个数的来源：手序，与这一手本身（牌名里那个数字）。
    let private headSources (note: ReviewNote) : Sourced list =
        [
            fromInt "这一手的手序（ReviewNote.Turn ← Table.Turns）" note.Turn
            fromValue "这一手本身（Action.toDisplay note.Played）" (Action.toDisplay note.Played)
        ]

    /// 第二句上那几个数的来源：**逐格都是 `DecisionPackage.scaffold` 那一份**。
    let private figureSources (note: ReviewNote) : Sourced list =
        let ukeire (trial: DahaiScaffold) =
            trial.Ukeire
            |> Option.map (fun ukeire ->
                [
                    fromInt "有效牌枚数（Ukeire.total）" (Ukeire.total ukeire)
                    fromInt "有效牌种数（Ukeire.kindCount）" (Ukeire.kindCount ukeire)
                ])
            |> Option.defaultValue []

        let danger (trial: DahaiScaffold) =
            trial.Danger
            |> Option.map (fun danger -> [ fromInt "这一手第几安全（Danger.Rank）" danger.Rank ])
            |> Option.defaultValue []

        match note.Scaffold, note.Trial with
        | None, _ -> []
        | Some scaffold, None -> [ fromShanten "打之前的向听（Scaffold.Shanten）" scaffold.Shanten ]
        | Some scaffold, Some trial ->
            [
                fromShanten "打之前的向听（Scaffold.Shanten）" scaffold.Shanten
                fromShanten "打之后的向听（DahaiScaffold.Shanten）" trial.Shanten
            ]
            @ ukeire trial
            @ danger trial

    /// 第三句上那几个数的来源：**每一条候选逐格都是引擎那一条试打**。
    let private candidateSources (candidate: ReviewCandidate) : Sourced list =
        let total =
            candidate.Ukeire
            |> Option.map (fun total -> [ fromInt "那一张的有效牌枚数（Ukeire.total）" total ])
            |> Option.defaultValue []

        // **多出来的枚数才写那个号**（`ReviewCandidate.toDisplay` 那三支）：一边算不出来、
        // 或者两边一样多时，那一格根本不印。
        let gain =
            match candidate.Ukeire, candidate.UkeireGain with
            | Some _, Some gain when gain > 0 -> [ fromValue "比你打的那张多几枚（UkeireGain）" $"+{gain}" ]
            | Some _, Some gain when gain < 0 -> [ fromInt "比你打的那张多几枚（UkeireGain）" gain ]
            | _, _ -> []

        let advances =
            if candidate.Advances then
                [ fromShanten "那一张打完之后的向听（DahaiScaffold.Shanten）" candidate.Shanten ]
            else
                []

        [ fromValue "换打的那一张（ReviewCandidate.Pai）" (Tile.toDisplay candidate.Pai) ]
        @ total
        @ gain
        @ advances

    /// 第三句整句：那几条候选按页面上的顺序一条接一条。
    let private adviceSourcesOf (note: ReviewNote) : Sourced list =
        note.Better |> List.collect candidateSources

    /// 强 AI 那一行上那几个数的来源（票 93/103）：**逐格都是上游那一份，这一层照抄**。
    ///
    /// 一旦有人把几条候选加权成一个「确定度分」印上去，那个数在这张表里没有一格认领它，
    /// 这一条当场红——**概率不是理由**，加权出来的更不是（票 103 的硬边界）。
    let private strongSources (row: ReviewStrong) : Sourced list =
        let head = [ fromValue "它选的那一条叫什么（ActionOption.label）" row.Label ]

        let counted =
            if List.length row.Candidates = row.CandidatesTotal then
                [ fromInt "上游一共给了几条（BaselineAnswer.CandidatesTotal）" row.CandidatesTotal ]
            else
                [
                    fromInt "上游一共给了几条（BaselineAnswer.CandidatesTotal）" row.CandidatesTotal
                    fromInt "这一包里认得出几条（Candidates 的条数）" (List.length row.Candidates)
                ]

        let listed =
            row.Candidates
            |> List.collect (fun choice ->
                [
                    fromValue "那一条叫什么（ActionOption.label）" choice.Label
                    fromProbability "上游给它的那个数（BaselineChoice.P）" choice.P
                ])

        let yours =
            match row.Yours with
            | Some choice ->
                [
                    fromValue "你那一手叫什么（ReviewNote.Label）" row.PlayedLabel
                    fromProbability "上游给你那一手的那个数（BaselineChoice.P）" choice.P
                    fromInt "你那一手在上游那一列里排第几（ReviewChoice.Rank）" choice.Rank
                ]
            | None ->
                [
                    fromValue "你那一手叫什么（ReviewNote.Label）" row.PlayedLabel
                    fromInt "上游一共给了几条（BaselineAnswer.CandidatesTotal）" row.CandidatesTotal
                ]

        // **分布为空时整句退回票 93 那一句**：那时后面三段一格都没有。
        match row.Candidates with
        | [] -> head
        | _ -> head @ counted @ listed @ yours

    [<Fact>]
    let ``复盘上一个自造的合成数都不出：那三句话里的每一个数都指得回引擎那一份的一格`` () =
        // 三桌凑齐三种情形：每手打最优（那一栏恒是「最优之一」）、每手打最差
        // （候选那几个数才有得核）、首页那份回放（副露与立直那几种不打牌的手）。
        let replay =
            TablePage.home ()
            |> fst
            |> step (DemoLoaded(Ok demo))
            |> step (ViewpointPicked(Viewpoint.Seated(seat 1)))

        let batches =
            [
                "真人打最优那一桌", settledFirst.Force() |> notesOf 0
                "真人打最差那一桌", settledWorst.Force() |> notesOf 0
                "首页那份回放的座位 1", replay |> notesOf 1
            ]

        let tracedNote (where: string) (note: ReviewNote) : int =
            let at = $"{where}第 {note.Turn} 手"

            let advice =
                ReviewNote.advice note
                |> Option.map (traced $"{at}的第三句" (adviceSourcesOf note))
                |> Option.defaultValue 0

            traced $"{at}的抬头" (headSources note) (ReviewNote.headline note)
            + traced $"{at}的第二句" (figureSources note) (ReviewNote.figures note)
            + advice

        let numbers =
            batches
            |> List.sumBy (fun (where, notes) -> notes |> List.sumBy (tracedNote where))

        // 判据 3：这几种情形各真的走到过几次？为 0 的那一种，这一条等于没跑。
        let notes = batches |> List.collect snd

        let counted (predicate: ReviewNote -> bool) : int =
            notes |> List.filter predicate |> List.length

        let withUkeire =
            counted (fun note -> note.Trial |> Option.exists (fun trial -> Option.isSome trial.Ukeire))

        let withDanger =
            counted (fun note -> note.Trial |> Option.exists (fun trial -> Option.isSome trial.Danger))

        let withBetter = counted (fun note -> not (List.isEmpty note.Better))

        let withGain =
            counted (fun note ->
                note.Better
                |> List.exists (fun each -> each.UkeireGain |> Option.exists (fun gain -> gain <> 0)))

        let withAdvances =
            counted (fun note -> note.Better |> List.exists (fun each -> each.Advances))

        let notDahai = counted (fun note -> Option.isNone note.Trial)

        // 听牌与和了那几手：那一格印出来一个数都没有（`fromShanten` 的空串那一支）。
        let noShantenNumber =
            counted (fun note -> note.Scaffold |> Option.exists (fun each -> Shanten.value each.Shanten <= 0))

        Assert.True(List.length notes > 200, $"这三桌该有两百多手，实到 {List.length notes} 手")
        Assert.True(numbers > 600, $"这一条该核过几百个数，实到 {numbers} 个")
        Assert.True(withUkeire > 0, "一手带有效牌的都没有：那两格等于没核")
        Assert.True(withDanger > 0, "一手带危险度的都没有：那一格等于没核")
        Assert.True(withBetter > 0, "一条「更好的候选」都没列出来：第三句那几个数等于没核")
        Assert.True(withGain > 0, "一条带「多几枚」的候选都没有：那一格等于没核")
        Assert.True(withAdvances > 0, "一条「向听更好」的候选都没有：那一格等于没核")
        Assert.True(notDahai > 0, "一手不打牌的都没有：第二句那另一支等于没核")
        Assert.True(noShantenNumber > 0, "一手听牌或和了形的都没有：向听那一格的空串那一支等于没核")

    [<Fact>]
    let ``强 AI 那一行同样一个自造的数都不出：条数、概率、排第几逐个指得回上游那一份`` () =
        let note, package, ids = handWithOptions ()

        let played =
            match DecisionPackage.tryId note.Played package with
            | Some id -> id
            | None -> failwith "他打的那一手必然在那一包里"

        let elsewhere = ids |> List.filter (fun id -> id <> played)

        let rowOf (answer: BaselineAnswer) : ReviewStrong =
            match Review.strongOf note answer with
            | Some row -> row
            | None -> failwith "包里那一条 id 该换得出一行"

        let outside =
            elsewhere
            |> List.truncate 3
            |> List.mapi (fun index id -> id, 0.5 / float (index + 1))

        let rows =
            [
                "你打的排第 2",
                rowOf (answeredWith (Some elsewhere[0]) [ elsewhere[0], 0.62; played, 0.35; elsewhere[1], 0.03 ] 3)
                "你打的不在那几条里", rowOf (answeredWith (Some elsewhere[0]) outside 3)
                "中间一条这一包里认不出来", rowOf (answeredWith (Some ids[0]) [ ids[0], 0.7; List.max ids + 7, 0.2; ids[1], 0.1 ] 3)
                "末一条小到两位小数写不出来",
                rowOf (answeredWith (Some ids[0]) [ ids[0], 0.5961328; ids[1], 0.40268; ids[2], 0.0010278977 ] 3)
                "上游没给分布（逐字退回票 93 那一句）", rowOf (answered (Some elsewhere[0]) 5)
            ]

        let numbers =
            rows
            |> List.sumBy (fun (which, row) ->
                traced $"强 AI 那一行（{which}）" (strongSources row) (ReviewStrong.toDisplay row))

        // 阳性对照：这五种情形真的各印出了一串数（空表那一种除外，它本来就只有一句话）。
        Assert.True(numbers > 15, $"这一条该核过十几个数，实到 {numbers} 个")

    // ---- 值得看的那几手：筛选那一格与时间轴上那几枚标记（票 105） ----
    //
    // **阈值不是拍的**（判据 14）：`Review` 里那个常数是在真人牌谱语料上量出来的
    // （111 份 × 四席 × 69,318 个决策点，报告 105 §1）。这一族用例因此**把它钉住**：
    // 0.79 与 0.80 两侧各一条，改动那个常数就有人当场喊。
    //
    // **CI 里强 AI 那一行画不出来**（那份 6 MB 不入版本控制，ADR-0006 边界 6），
    // 因此「它很确定而你打了别的」这一条判据的执行者**只有这一侧**——浏览器那一趟
    // 在 CI 里只跑得到「引擎的试打表里还有更好的换法」那一半（报告 107 §1 记的同一条）。

    /// 给某一手造一行强 AI：`its` 是它选的那一条，`candidates` 是上游那一列。
    let private strongFor (note: ReviewNote) (its: int) (candidates: (int * float) list) : ReviewStrong =
        match Review.strongOf note (answeredWith (Some its) candidates 3) with
        | Some row -> row
        | None -> failwith $"第 {note.Turn} 手该换得出一行强 AI"

    /// 这一手那一包里的几条 id，与他自己打的那一条（两样缺一样就跳过这一手）。
    let private optionsOf (note: ReviewNote) : (int list * int) option =
        note.Package
        |> Option.bind (fun package ->
            DecisionPackage.tryId note.Played package
            |> Option.map (fun played -> DecisionPackage.options package |> List.map ActionOption.id, played))

    [<Fact>]
    let ``它很确定而你打的排在后面：阈值两侧与排第几四种，各断言一次`` () =
        let note, package, ids = handWithOptions ()

        let played =
            match DecisionPackage.tryId note.Played package with
            | Some id -> id
            | None -> failwith "他打的那一手必然在那一包里"

        let elsewhere = ids |> List.filter (fun id -> id <> played)

        // 六种情形，逐条摆明白（第三列是期望）。**0.79 / 0.80 两条钉的就是那个阈值本身**。
        let cases =
            [
                "上游没给分布", Review.strongOf note (answered (Some elsewhere[0]) 5), false
                "你就是它挑的那一条（排第 1）",
                Review.strongOf note (answeredWith (Some played) [ played, 0.95; elsewhere[0], 0.03 ] 2),
                false
                "你排第 2（它很确定，但你就在它后一条）",
                Review.strongOf note (answeredWith (Some elsewhere[0]) [ elsewhere[0], 0.95; played, 0.03 ] 2),
                false
                "你排第 3",
                Review.strongOf
                    note
                    (answeredWith (Some elsewhere[0]) [ elsewhere[0], 0.95; elsewhere[1], 0.03; played, 0.01 ] 3),
                true
                "你不在它那几条里，而它刚好没过阈值（0.79）",
                Review.strongOf note (answeredWith (Some elsewhere[0]) [ elsewhere[0], 0.79; elsewhere[1], 0.2 ] 2),
                false
                "你不在它那几条里，而它刚好过了阈值（0.80）",
                Review.strongOf note (answeredWith (Some elsewhere[0]) [ elsewhere[0], 0.80; elsewhere[1], 0.2 ] 2),
                true
            ]

        for which, row, expected in cases do
            match row with
            | Some row ->
                Assert.Equal(expected, Review.notable row)

                // 那一格上 DOM 的词（`data-review-worth`）与这一条判据是同一件事：
                // 用例手里这一手没有更好的换法，因此只有 strong 与空串两种。
                let bare = { note with Better = [] }
                Assert.Equal((if expected then "strong" else ""), Review.worth (Some row) bare)
                Assert.Equal(expected, Review.worthwhile (Some row) bare)

                // **`notable` 蕴含 `Differs`**：排第三或不在候选里的那一手，必然不是它挑的那一条。
                if Review.notable row then
                    Assert.True(row.Differs, $"{which}：这一行既然值得看，它与你打的就该是两手")
            | None -> failwith $"{which}：包里那一条 id 该换得出一行"

        // 判据 3：两侧各真的走到过（六种里两真四假）。
        let counted (want: bool) =
            cases |> List.filter (fun (_, _, expected) -> expected = want) |> List.length

        Assert.Equal(2, counted true)
        Assert.Equal(4, counted false)

    [<Fact>]
    let ``筛选真的筛掉了东西：那几百条里剩下的每一条都过得了判据，被筛掉的每一条都过不了`` () =
        let replay =
            TablePage.home ()
            |> fst
            |> step (DemoLoaded(Ok demo))
            |> step (ViewpointPicked(Viewpoint.Seated(seat 1)))

        // 两桌：首页那份真牌谱（四席都是模型），与故意每一手都打最差的那一桌。
        let batches =
            [
                "首页那份回放的座位 1", replay |> notesOf 1
                "真人打最差那一桌", settledWorst.Force() |> notesOf 0
            ]

        for where, notes in batches do
            // **强 AI 那一叠不在场时只剩第一条判据**（CI 里就是这一档）。
            let focused = Review.focused Map.empty notes
            let dropped = notes |> List.filter (fun note -> not (List.contains note focused))

            Assert.NotEmpty focused
            Assert.NotEmpty dropped

            // 剩下的每一条都有更好的换法；被筛掉的每一条都没有。
            for note in focused do
                Assert.False(List.isEmpty note.Better, $"{where}第 {note.Turn} 手留下来了，却一条更好的换法都没有")
                // 那几 MB 不在场时，点亮它们的只可能是引擎那一半判据。
                Assert.Equal("better", Review.worth None note)

            for note in dropped do
                Assert.True(List.isEmpty note.Better, $"{where}第 {note.Turn} 手被筛掉了，却列得出更好的换法")
                Assert.Equal("", Review.worth None note)

            // 顺序与手序照旧（筛选只是少摆几条，不重排）。
            Assert.Equal<int list>(
                focused |> List.map (fun note -> note.Turn) |> List.sort,
                focused |> List.map (fun note -> note.Turn)
            )

            // 时间轴上那几枚标记 = 这一列（一处算、两处消费）。
            let marks = Review.marks focused

            Assert.Equal<int list>(
                focused |> List.map (fun note -> note.Turn),
                marks |> List.map (fun mark -> mark.Turn)
            )

            Assert.Equal<int list>(
                focused |> List.map (fun note -> note.Frame),
                marks |> List.map (fun mark -> mark.Frame)
            )

        // **筛选得有意义**：首页那一席一整场一百多手，留下来的不到一半
        // （量出来是 122 手里 22 手，报告 105 §2）——否则「精选」只是换个说法把整列再摆一遍。
        let notes = replay |> notesOf 1
        let kept = Review.focused Map.empty notes |> List.length
        Assert.True(kept * 2 < List.length notes, $"一整场 {List.length notes} 手里留下了 {kept} 手：这不叫筛选")

    [<Fact>]
    let ``时间轴上那几枚标记与分歧那几手逐手对齐：由强 AI 点亮的那几枚一枚不多、一枚不少`` () =
        let notes =
            TablePage.home ()
            |> fst
            |> step (DemoLoaded(Ok demo))
            |> step (ViewpointPicked(Viewpoint.Seated(seat 1)))
            |> notesOf 1

        // 造一叠回执：**四种情形轮着来**（你排第 1 / 第 2 / 第 3 / 不在它那几条里），
        // 于是「分歧」这一族里既有值得看的，也有不值得看的——两边的计数都不会是 0。
        let rows =
            notes
            |> List.indexed
            |> List.choose (fun (index, note) ->
                optionsOf note
                |> Option.bind (fun (ids, played) ->
                    match ids |> List.filter (fun id -> id <> played) with
                    | first :: second :: _ ->
                        let row =
                            match index % 4 with
                            | 0 -> strongFor note played [ played, 0.9; first, 0.06; second, 0.02 ]
                            | 1 -> strongFor note first [ first, 0.9; played, 0.06; second, 0.02 ]
                            | 2 -> strongFor note first [ first, 0.9; second, 0.06; played, 0.02 ]
                            | _ -> strongFor note first [ first, 0.5; second, 0.3 ]

                        Some(note.Turn, row)
                    | _ -> None))

        let table = Map.ofList rows
        let focused = Review.focused table notes
        let marks = Review.marks focused
        let disagreeing = Review.disagreeing (rows |> List.map snd) |> Set.ofList

        // 那两个数说的是同一件事（面板抬头读的是后者）。
        Assert.Equal(Set.count disagreeing, Review.disagreements (rows |> List.map snd))

        let better =
            notes
            |> List.filter (fun note -> not (List.isEmpty note.Better))
            |> List.map (fun note -> note.Turn)
            |> Set.ofList

        // **逐枚对齐**：每一枚标记要么是「引擎的数上有更好的换法」，要么落在分歧那几手里。
        for mark in marks do
            Assert.True(
                Set.contains mark.Turn better || Set.contains mark.Turn disagreeing,
                $"第 {mark.Turn} 手被标在时间轴上，却既没有更好的换法、也不是分歧"
            )

        // **由强 AI 点亮的那几枚 = {分歧 ∧ 它很确定而你排在后面}**，一枚不多、一枚不少。
        let lit =
            marks
            |> List.map (fun mark -> mark.Turn)
            |> Set.ofList
            |> Set.filter (fun turn -> not (Set.contains turn better))

        let expected =
            rows
            |> List.filter (fun (_, row) -> Review.notable row)
            |> List.map fst
            |> Set.ofList
            |> Set.filter (fun turn -> not (Set.contains turn better))

        Assert.Equal<Set<int>>(expected, lit)

        // 那一格上 DOM 的词与两条判据逐手对得上：四种情形（both / better / strong / 空串）
        // 在这一整场里各真的出现过（判据 3），而且「非空串」那几条恰好是这一列。
        let worths =
            notes |> List.map (fun note -> Review.worth (Map.tryFind note.Turn table) note)

        for word in [ "both"; "better"; "strong"; "" ] do
            Assert.True(List.contains word worths, $"一整场里一手「{word}」都没有：那一支等于没跑")

        Assert.Equal<int list>(
            focused |> List.map (fun note -> note.Turn),
            List.zip notes worths
            |> List.filter (fun (_, worth) -> worth <> "")
            |> List.map (fun (note, _) -> note.Turn)
        )

        // 判据 3：这几种情形各真的走到过几次——为 0 的那一种，这一条什么都没证明。
        let notable =
            rows |> List.filter (fun (_, row) -> Review.notable row) |> List.length

        let quiet = Set.count disagreeing - notable

        Assert.True(Set.count better > 0, "一手「有更好的换法」都没有：那一半判据等于没跑")
        Assert.True(notable > 0, "一手「它很确定而你排在后面」都没有：那一半判据等于没跑")
        Assert.True(quiet > 0, "分歧那几手全被点亮了：那条判据没有收紧任何东西")
        // **收紧是这一票的全部意义**：分歧那一族里被点亮的应当是少数。
        Assert.True(notable * 2 < Set.count disagreeing, $"分歧 {Set.count disagreeing} 手里点亮了 {notable} 手：没收紧")

    [<Fact>]
    let ``筛选那一格默认开着，拨得动，而且只改这一列摆几条`` () =
        let model =
            TablePage.home ()
            |> fst
            |> step (DemoLoaded(Ok demo))
            |> step (ViewpointPicked(Viewpoint.Seated(seat 1)))
            |> step (CursorMoved 200)

        // 默认只看值得看的那几手（票面：一整场一百多条排成一列，人得自己一条条扫）。
        Assert.True(model.ReviewFiltered)

        let all = model |> step ReviewFilterToggled
        Assert.False(all.ReviewFiltered)
        Assert.True((all |> step ReviewFilterToggled).ReviewFiltered)

        // **它不碰别的**：游标没动、摊开的那一手没变、标注一条不少。
        Assert.Equal(
            TablePage.timeline model |> Option.map (fun timeline -> timeline.Cursor),
            TablePage.timeline all |> Option.map (fun timeline -> timeline.Cursor)
        )

        Assert.Equal(Review.opened model, Review.opened all)

        Assert.Equal<int list>(
            notesOf 1 model |> List.map (fun note -> note.Turn),
            notesOf 1 all |> List.map (fun note -> note.Turn)
        )

    /// 筛选那一句上那两个数的来源（票 105）：**只有这两格**。
    ///
    /// 阈值那个数**不印在页面上**，因此这张表里没有它的位置——真要印，就得在这里
    /// 与 `verify-review.mjs` 那一侧各写一格来源（票 107 立的白名单）。
    let private filterSources (total: int) (kept: int) : Sourced list =
        [
            fromInt "这一席落定了几手（ReviewNote 的条数）" total
            fromInt "值得看的有几手（Review.focused 的条数）" kept
        ]

    [<Fact>]
    let ``筛选那一句同样一个自造的数都不出：四种措辞里都只有「几手」与「显示几手」`` () =
        // 四种措辞 × 两组数：筛选开着 / 关掉 × 问过强 AI / 没问过，外加「一手都没剩」那一支。
        let cases =
            [
                "筛选开着、没问过强 AI", ReviewFilter.toDisplay true false 122 22, 122, 22
                "筛选开着、问过强 AI", ReviewFilter.toDisplay true true 122 25, 122, 25
                "筛选关掉", ReviewFilter.toDisplay false true 122 25, 122, 25
                "一手都没剩", ReviewFilter.toDisplay true true 7 0, 7, 0
            ]

        let numbers =
            cases
            |> List.sumBy (fun (which, said, total, kept) -> traced $"筛选那一句（{which}）" (filterSources total kept) said)

        Assert.Equal(8, numbers)

        for which, said, _, _ in cases do
            // 那一句里同样不许出现度量词与「评分」这类口径（票 90/93/103 的老边界）。
            for word in forbidden do
                Assert.DoesNotContain(word, said)

            Assert.DoesNotContain("暂无", said)
            Assert.False(said.Contains "%", $"{which}：筛选那一句里出现了百分比——一写成百分比，下一步就是拿它当分数")

        // 那一枚按钮上**一个数都没有**（同一件事不在两处各写一个数）。
        for filtered in [ true; false ] do
            Assert.Empty(numerals (ReviewFilter.toggle filtered))
