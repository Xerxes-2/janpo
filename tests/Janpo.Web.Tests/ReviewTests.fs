namespace Janpo.Web.Tests

open System
open System.IO
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

    // ---- 强 AI 那一行留位置但不占位 ----

    [<Fact>]
    let ``强 AI 那一行今天整行不出现：一条标注只说得出三句话，里面没有「暂无」`` () =
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
                Assert.DoesNotContain("强 AI", line)
                // 这一票不造总分（票 93 才有模型）：一条标注里不许出现「分」这种口径。
                Assert.DoesNotContain("评分", line)
