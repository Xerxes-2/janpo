namespace Janpo.Web.Tests

open Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// 真人的**信息辅助**与**思考时限**（票 89；spec 的 story 33 / 32）。
///
/// 这一层钉的是四条判据：
///
/// 1. **Bare 什么都不给**：辅助渲染的唯一入口（`TableState.humanScaffold`）与危险度那一块
///    读的是同一条判据（`TableState.assists`）——两处各判一遍就是两处判据，
///    而两处判据迟早漂到「向听藏了、危险度还摆着」那一步；
/// 2. **同一份数**：他看到的那几个数与**模型跨界拿到的那一份 JSON** 逐字段相同。
///    左边是页面这一侧渲出来的那几行（解析回数），右边是 `DecisionPackage.encoder`
///    编出去的字节——**两侧是两份互相独立的转录，同一个数据源**（引擎的 `Scaffold.calculate`
///    随包算的那一次）。它证的是**转录忠实**：没有为 UI 另算一遍、没有把甲的危险度挂到乙头上。
///    「这个数算得对不对」由引擎自己的用例守着，不在这一层（判据 18 的口径）；
/// 3. **倒计时只在轮到自己时走**：判据挂在 `humanTurn` 上，不挂在「牌桌停着」上
///    （票 88 之后他在想的时候模型席照问照答，牌桌并没停）；
/// 4. **默认不限时那条路一个行为都不变**：不设时限时连一个效果体都不多发。
///
/// 画出来长什么样（Bare 那一屏整页没有一个数、时限到点那一下真的打了）在浏览器那一侧
/// （`web/scripts/verify-assist.mjs`）——这里一行 Feliz 都不 open。
module HumanAssistTests =

    let private seat (index: int) : Seat =
        match Seat.ofIndex index with
        | Some seat -> seat
        | None -> failwith $"{index} 应当是合法座位"

    let private human = seat 0

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    /// 这条消息发出了几个效果体（`Cmd` 就是一串效果体）。
    /// **「不限时那条路一个都不多发」那条断言的执行者就是它。**
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

    let private turnOf (model: TableModel) : DecisionPackage =
        match TablePage.humanTurn model with
        | Some package -> package
        | None -> failwith "这一刻该轮到真人"

    /// 真人坐第 `index` 席、其余三家均匀随机的那一桌（`?table=1` 的默认种子）。
    let private humanAt (index: int) : TableModel =
        SeatingPlan.initial Ruleset.yonma
        |> SeatingPlan.bind (seat index) SeatChoice.Human
        |> TablePage.initial RulesetDraft.initial
        |> fst

    /// 四家 bot 那一桌（**阳性对照**：下面每一条「他看不见」都要在这一桌上照旧看得见）。
    let private botTable: TableModel =
        SeatingPlan.initial Ruleset.yonma
        |> TablePage.initial RulesetDraft.initial
        |> fst

    /// 把真人那一席的某一格拨一下（面板上那一下就是这条消息）。
    let private edited (field: SeatField) (value: string) (model: TableModel) : TableModel =
        step (SeatEdited(human, field, value)) model

    let private tiered (tier: ScaffoldTier) (model: TableModel) : TableModel =
        edited SeatField.Tier (ScaffoldTier.toWire tier) model

    /// 真人坐座位 0、拨到某一档、时限 `seconds` 秒（0 = 不限时）的那一桌。
    let private seated (tier: ScaffoldTier) (seconds: int) : TableModel =
        humanAt 0 |> tiered tier |> edited SeatField.Clock (string seconds)

    let private scaffoldOf (model: TableModel) : Scaffold =
        match TablePage.humanScaffold model with
        | Some scaffold -> scaffold
        | None -> failwith "这一档该给得出那几个算好的数"

    let private clockOf (model: TableModel) : HumanClock =
        match TablePage.humanClock model with
        | Some clock -> clock
        | None -> failwith "这一刻该有一记倒计时在走"

    /// 没轮到他的那一手：**这一局终了就开下一局，否则只走一手**。
    ///
    /// 提成具名助手是为了给**换局那一支一个执行者**（票 114）：它原本写在下面那条
    /// 走手循环里，而那一趟六次到点全发生在同一局里，于是 `KyokuAdvanced` 那一支
    /// 一趟都没被求值过（票 113 §3.1）。分支留着（局真终了时它是对的），
    /// 执行者是本文件末尾那条具名用例。
    let private advancedOrNext (model: TableModel) : TableModel =
        if Table.isKyokuEnded (tableOf model) then
            step KyokuAdvanced model
        else
            step Advanced model

    // ---- 第三个锚点：模型跨界拿到的那一份 JSON ----

    /// 决策包**编出去的那一份**里，逐条试打的那几个数（票 24 起 Agent 层读的就是它）。
    ///
    /// **它是第三个锚点**：页面这一侧渲的是 F# 的 `Scaffold` 记录，这一侧是同一份记录
    /// 经 `Scaffold.encoder` 变成的字节——拿 `HumanScaffold.lines` 去对 `DecisionPackage.scaffold`
    /// 等于拿同一个表达式对它自己（判据 6 那一族）。
    let private wireTrials (package: DecisionPackage) : (int * int * int * (int * int) option * int option) list =
        let trial =
            Decode.object (fun get ->
                get.Required.Field "action_ids" (Decode.list Decode.int),
                get.Required.At [ "shanten"; "value" ] Decode.int,
                get.Required.Field "shanten_delta" Decode.int,
                // **`Optional.Field` 而不是 `Required.Field`**：这两格编出去时可能是 `null`
                // （算不出来的有效牌、没有威胁时的危险度），而 `null` 与「没有」在这里是同一件事。
                get.Optional.Field
                    "ukeire"
                    (Decode.object (fun each ->
                        each.Required.Field "total" Decode.int, each.Required.Field "kinds" Decode.int)),
                get.Optional.Field "danger" (Decode.field "rank" Decode.int))

        let text = DecisionPackage.encoder package |> Encode.toString 0

        match Decode.fromString (Decode.at [ "scaffold"; "dahai" ] (Decode.list trial)) text with
        | Error message -> failwith $"决策包编出去的那一份里读不出脚手架：{message}"
        | Ok trials ->
            trials
            |> List.collect (fun (ids, shanten, delta, ukeire, rank) ->
                ids |> List.map (fun id -> id, shanten, delta, ukeire, rank))
            |> List.sortBy (fun (id, _, _, _, _) -> id)

    /// 页面这一侧那几行，摊成同一个形状（**同一件事的另一份转录**）。
    let private shownTrials (package: DecisionPackage) : (int * int * int * (int * int) option * int option) list =
        HumanScaffold.lines package
        |> List.map (fun line ->
            line.Id,
            Shanten.value line.Trial.Shanten,
            line.Trial.ShantenDelta,
            line.Trial.Ukeire
            |> Option.map (fun ukeire -> Ukeire.total ukeire, Ukeire.kindCount ukeire),
            line.Trial.Danger |> Option.map (fun danger -> danger.Rank))

    // ---- 代点：把这一桌往前推 ----

    /// 一步：轮到真人就点包里的头一条打牌（响应阶段按「过」），否则单步（这一局终了就开下一局）。
    /// 与 `HumanSeatTests.playedOut` 同一套走法。
    let private moved (model: TableModel) : TableModel =
        match TablePage.humanTurn model with
        | Some package ->
            match HumanSeat.dahaiOptions package, HumanSeat.pass package with
            | (id, _, _, _) :: _, _ -> step (HumanPlayed id) model
            | [], Some pass -> step (HumanPlayed pass.Id) model
            | [], None -> failwith "轮到真人，却既没牌可打也没有「过」可按"
        | None ->
            let table = tableOf model

            if Table.isKyokuEnded table then
                step KyokuAdvanced model
            else
                step Advanced model

    /// 把这一桌推到「轮到他、而且这一手有得打 / 或者正在响应」的那一刻。
    let private walkedTo (wanted: DecisionPackage -> bool) (model: TableModel) : TableModel =
        let rec walk (moves: int) (model: TableModel) : TableModel =
            if moves > 3000 then
                failwith "这一场里一次都没走到那一刻：种子或代点变了"
            else
                match TablePage.humanTurn model with
                | Some package when wanted package -> model
                | _ ->
                    if Option.isSome (Table.result (tableOf model)) then
                        failwith "这一场打完了也没走到那一刻"
                    else
                        walk (moves + 1) (moved model)

        walk 0 model

    let private responding (package: DecisionPackage) : bool = HumanSeat.pass package |> Option.isSome

    // ---- Bare：什么都不给 ----

    [<Fact>]
    let ``Bare 什么都不给：向听 / 有效牌 / 危险度在页面这一侧连值都取不出来`` () =
        let bare = seated ScaffoldTier.Bare 0

        // 轮到他（他是东 1 局的亲，牌已经在手上）——**防空转**：不轮到他时下面这几条恒成立。
        Assert.True(Option.isSome (TablePage.humanTurn bare))
        Assert.Equal(Some ScaffoldTier.Bare, TablePage.humanTier bare)

        // ① 辅助渲染的唯一入口给不出东西；② 危险度那一块（面板上那一枚 + 牌桌上那一块）
        // 读的是同一条判据。**两条都挂在 `assists` 上，因此不可能一个藏了另一个还摆着。**
        Assert.Equal(None, TablePage.humanScaffold bare)
        Assert.False(TablePage.assists bare)

        // **拨到危险度那一枚也不给**：`DangerToggled` 收得下，但那一块仍旧不摆
        // （灰掉不算数，这里连判据都不成立）。
        Assert.False(TablePage.assists (step DangerToggled bare))

        // **阳性对照 ①**：同一桌拨到信息辅助，那几个数当场就在。
        let assisted = tiered ScaffoldTier.Assisted bare
        Assert.True(TablePage.assists assisted)
        Assert.True(Option.isSome (TablePage.humanScaffold assisted))

        // **阳性对照 ②**：没有真人的那一桌照旧全给（四家模型那一桌的行为一个字没变）。
        Assert.True(TablePage.assists botTable)
        Assert.Equal(None, TablePage.humanTier botTable)

    [<Fact>]
    let ``Bare 一整局都不给，而同一局面拨到信息辅助就有：不是只有开局那一手`` () =
        // **走一整局**（判据 3：一条只在开局那一手成立的断言等于没有断言）。
        let rec walk (moves: int) (checks: int) (model: TableModel) : int =
            if moves > 600 || Option.isSome (Table.result (tableOf model)) then
                checks
            else
                let checks =
                    match TablePage.humanTurn model with
                    | None -> checks
                    | Some _ ->
                        Assert.Equal(None, TablePage.humanScaffold model)
                        Assert.False(TablePage.assists model)
                        // 同一刻拨到信息辅助：**这一手真的有数可给**——
                        // 否则上面那条「给不出」量的只是「这一手本来就没有」。
                        Assert.True(Option.isSome (TablePage.humanScaffold (tiered ScaffoldTier.Assisted model)))
                        checks + 1

                walk (moves + 1) checks (moved model)

        let checks = walk 0 0 (seated ScaffoldTier.Bare 0)
        Assert.True(checks > 10, $"这一局里只核了 {checks} 次，那几条断言基本没开过口")

    // ---- Assisted：同一份数 ----

    [<Fact>]
    let ``Assisted 那几个数与模型跨界拿到的那一份逐字段相同：向听 / 进退向 / 有效牌 / 危险度名次`` () =
        let model = seated ScaffoldTier.Assisted 0
        let package = turnOf model

        let wire = wireTrials package
        let shown = shownTrials package

        // 防空转：这一手真有得打。
        Assert.NotEmpty wire
        Assert.Equal<(int * int * int * (int * int) option * int option) list>(wire, shown)

        // **那一份就是引擎算的那一份**（`DecisionPackage.scaffold` 随包过界，与档位无关）：
        // 页面上那句「现在几向听」读的是它，而不是自己再算一遍。
        let scaffold = scaffoldOf model

        Assert.Equal(
            DecisionPackage.scaffold package
            |> Option.map (fun each -> Shanten.value each.Shanten),
            Some(Shanten.value scaffold.Shanten)
        )

    [<Fact>]
    let ``一整局逐手对拍：他看到的与模型拿到的是同一份，一手都不许岔开`` () =
        let rec walk (moves: int) (checks: int, lines: int) (model: TableModel) : int * int =
            if moves > 600 || Option.isSome (Table.result (tableOf model)) then
                checks, lines
            else
                let counted =
                    match TablePage.humanTurn model with
                    | None -> checks, lines
                    | Some package ->
                        let shown = shownTrials package

                        Assert.Equal<(int * int * int * (int * int) option * int option) list>(
                            wireTrials package,
                            shown
                        )

                        checks + 1, lines + List.length shown

                walk (moves + 1) counted (moved model)

        let checks, lines = walk 0 (0, 0) (seated ScaffoldTier.Assisted 0)

        // 执行次数（判据 3）：一整局他出手几十次，逐手几十行。
        Assert.True(checks > 10, $"这一局里只对拍了 {checks} 手")
        Assert.True(lines > 100, $"这一局里只对拍了 {lines} 行")

    [<Fact>]
    let ``辅助那几行与他点得动的那几张一一对应：多一行少一行都不许`` () =
        // **这一条是「摆到真人这一侧」的要害**：出现一行「打 3s 会怎样」而 3s 根本点不动，
        // 或者点得动的某一张没有那一行，人就会照着一份对不上的表出牌。
        let rec walk (moves: int) (checks: int) (model: TableModel) : int =
            if moves > 600 || Option.isSome (Table.result (tableOf model)) then
                checks
            else
                let checks =
                    match TablePage.humanTurn model with
                    | None -> checks
                    | Some package ->
                        let playable =
                            HumanSeat.dahaiOptions package
                            |> List.map (fun (id, _, _, _) -> id)
                            |> List.sort

                        let lines = HumanScaffold.lines package |> List.map (fun line -> line.Id)

                        Assert.Equal<int list>(playable, lines)

                        // 每一行那个中文 label 就是那一条动作的 label（不是这一层自己拼的）。
                        for line in HumanScaffold.lines package do
                            let label =
                                DecisionPackage.options package
                                |> List.tryFind (fun option -> ActionOption.id option = line.Id)
                                |> Option.map ActionOption.label

                            Assert.Equal(label, Some line.Label)

                        checks + 1

                walk (moves + 1) checks (moved model)

        let checks = walk 0 0 (seated ScaffoldTier.Assisted 0)
        Assert.True(checks > 10, $"这一局里只核了 {checks} 手")

    [<Fact>]
    let ``ToolSearch 按信息辅助处理：那几行与 Assisted 逐条相同，页面也说得出这件事`` () =
        // 票面原话：这一票不给真人做查询面板（那是票 94 给模型做的那一档）。
        // **按 Assisted 处理而不是按 Bare**：那一档的模型手里那几个数一样拿得到
        // （它自己问得出来），把真人降到裸奔反而是另一种意外。
        let assisted = seated ScaffoldTier.Assisted 0
        let tools = seated ScaffoldTier.ToolSearch 0

        Assert.True(HumanScaffold.shows ScaffoldTier.ToolSearch)
        Assert.False(HumanScaffold.shows ScaffoldTier.Bare)
        Assert.True(TablePage.assists tools)
        Assert.Equal(Some ScaffoldTier.ToolSearch, TablePage.humanTier tools)

        Assert.Equal<(int * int * int * (int * int) option * int option) list>(
            shownTrials (turnOf assisted),
            shownTrials (turnOf tools)
        )

    [<Fact>]
    let ``危险度那一块在真人这一侧与辅助同进同出：立直之后也不许单独漏出来`` () =
        // 危险度是「要算才有的量」（术语表的「感知 vs 计算」），因此它跟着 `assists` 走。
        // **走到真有威胁的那一刻**：没人立直也没人副露时引擎本来就不给排序，
        // 那时候量它等于没量（判据 3）。
        let model =
            seated ScaffoldTier.Assisted 0
            |> walkedTo (fun package ->
                DecisionPackage.scaffold package
                |> Option.map (fun scaffold -> not (List.isEmpty scaffold.Threats))
                |> Option.defaultValue false)

        let scaffold = scaffoldOf model
        Assert.NotEmpty scaffold.Threats
        Assert.NotEqual<string>("", HumanScaffold.threats scaffold)

        // 同一刻拨回裸奔：整块判据当场翻面（页面上那一块与面板上那一枚都读它）。
        Assert.False(TablePage.assists (tiered ScaffoldTier.Bare model))
        Assert.Equal(None, TablePage.humanScaffold (tiered ScaffoldTier.Bare model))

    // ---- 时限：默认不限时 ----

    [<Fact>]
    let ``默认不限时：一记倒计时都不发，那条链也推不动牌桌`` () =
        let model = humanAt 0

        Assert.Equal(None, TablePage.humanLimit model)
        Assert.Equal(None, TablePage.humanClock model)

        // **一个效果体都不多发**（票 87 那条「真人在想的时候整桌等着」数的就是它）：
        // 不设时限那条路上，这一票一行行为都没改。
        Assert.Equal(0, effects Advanced model)
        Assert.Equal(0, effects PlayToggled model)

        // 那条链的消息就算凭空来一条也什么都不做（页面上根本没有钟）。
        let turns = (tableOf model).Turns
        let ticked = step (HumanTicked turns) model
        Assert.Equal(turns, (tableOf ticked).Turns)
        Assert.Equal(None, TablePage.humanClock ticked)

    [<Fact>]
    let ``没有真人那一桌一记倒计时都不发：时限只管坐着的那个人`` () =
        // 四家 bot：`SeatEdited` 把某一席的时限拨到 1 秒也不该长出一记钟来。
        let model = step (SeatEdited(human, SeatField.Clock, "1")) botTable

        Assert.Equal(None, TablePage.humanClock model)
        Assert.Equal(None, TablePage.humanLimit model)
        Assert.Equal(0, effects Advanced model)

    // ---- 时限：倒计时只在轮到自己时走 ----

    [<Fact>]
    let ``倒计时挂在「轮到他了吗」上：轮到才走，不轮到当场停`` () =
        // 座位 0 是东 1 局的亲：一打开就轮到他，钟从那一刻起走。
        let mine = seated ScaffoldTier.Bare 30
        Assert.Equal(Some 30, TablePage.humanLimit mine)
        Assert.Equal(30, (clockOf mine).Limit)
        Assert.Equal(0, (clockOf mine).Elapsed)
        Assert.Equal(30, HumanClock.remaining (clockOf mine))

        // 座位 1：开局轮到座位 0（bot），**不轮到他的时候一格都不走**。
        let theirs = humanAt 1 |> step (SeatEdited(seat 1, SeatField.Clock, "30"))

        Assert.Equal(None, TablePage.humanTurn theirs)
        Assert.Equal(None, TablePage.humanClock theirs)
        Assert.Equal(Some 30, TablePage.humanLimit theirs)

        // 走掉一秒：那一格往前走一下，牌桌一根汗毛都不动。
        let ticked = step (HumanTicked((tableOf mine).Turns)) mine
        Assert.Equal(1, (clockOf ticked).Elapsed)
        Assert.Equal((tableOf mine).Turns, (tableOf ticked).Turns)

        // 他一出手，**这一手的钟当场没了**（轮到别人了，`handOf` 说了算）。
        let played = moved ticked
        Assert.Equal(None, TablePage.humanTurn played)
        Assert.Equal(None, TablePage.humanClock played)

        // 推到他的下一手：**换成新的一记**（不是接着上一手的秒数往下走）。
        let again = walkedTo (fun _ -> true) played
        Assert.NotEqual((clockOf ticked).Turn, (clockOf again).Turn)
        Assert.Equal(0, (clockOf again).Elapsed)

        // **拨回不限时：那一格当场空掉**（判据挂在配置上，不是「等这一手走完」）。
        let loosened = edited SeatField.Clock "0" mine
        Assert.Equal(None, TablePage.humanClock loosened)
        Assert.Equal(None, TablePage.humanLimit loosened)

    [<Fact>]
    let ``过期的那一记丢掉：链自己断，牌桌一手都不动`` () =
        let model = seated ScaffoldTier.Bare 30
        let turns = (tableOf model).Turns

        // 上一手的票号（他早就出手了）：什么都不该发生。
        let stale = step (HumanTicked(turns - 1)) model
        Assert.Equal(turns, (tableOf stale).Turns)
        Assert.Equal(0, (clockOf stale).Elapsed)
        Assert.Equal(0, effects (HumanTicked(turns - 1)) model)

    // ---- 时限：到点自动打一手 ----

    [<Fact>]
    let ``时限到点自动摸切：打的就是刚摸那一张，而牌局接着走`` () =
        let model = seated ScaffoldTier.Bare 1
        let package = turnOf model
        let table = tableOf model

        // 这一手摸切那一条（引擎给的合法动作集里那一条）：**到点打的必须是它**。
        let tsumogiri =
            HumanSeat.dahaiOptions package
            |> List.tryFind (fun (_, _, giri, _) -> giri)
            |> Option.map (fun (_, pai, _, _) -> pai)

        Assert.True(Option.isSome tsumogiri, "开局那一手该有摸切可打")

        let expired = step (HumanTicked table.Turns) model

        // ① 真的打出去了：手数 +1，而那一手就是摸切那一张。
        Assert.Equal(table.Turns + 1, (tableOf expired).Turns)

        match (tableOf expired).Latest with
        | Some latest ->
            match latest.Action with
            | Action.Dahai(who, pai, giri) ->
                Assert.Equal(human, who)
                Assert.True(giri, "到点该摸切（`Fallback` 的 Bare 那一支）")
                Assert.Equal(tsumogiri, Some pai)
            | other -> failwith $"到点该打出一张牌，实际是「{Action.toDisplay other}」"
        | None -> failwith "到点之后该有落定的一手"

        // ② **牌局必须继续**：那一手没有卡死，往下照样推得动（下一家真的走了一手）。
        Assert.True(TablePage.canAdvance expired)
        Assert.Equal(table.Turns + 2, (tableOf (step Advanced expired)).Turns)

        // ③ 那一手记了一笔，而且说得出「不是他按的」。
        match TablePage.passes expired with
        | [ one ] ->
            Assert.False(HumanPass.pressed one)
            Assert.Equal(Some(Action.toDisplay (Action.Dahai(human, Option.get tsumogiri, true))), one.AutoPlayed)
            Assert.Contains("时限到点", HumanPass.toDisplay one)
        | other -> failwith $"到点那一手该记一笔，实际记了 {List.length other} 笔"

    [<Fact>]
    let ``时限到点、响应阶段自动过：与他自己按的那一次在数据里分得开`` () =
        // 走到「他家打了牌、正等他要不要鸣」的那一刻。
        let model = seated ScaffoldTier.Bare 1 |> walkedTo responding

        let package = turnOf model
        let lost = HumanSeat.buttons package |> List.map (fun button -> button.Label)
        Assert.NotEmpty lost

        // 他自己按的那一次（票 88 的语义）。
        let pressed = step (HumanPlayed (Option.get (HumanSeat.pass package)).Id) model

        match TablePage.passes pressed with
        | latest :: _ ->
            Assert.True(HumanPass.pressed latest)
            Assert.Equal(None, latest.AutoPlayed)
            Assert.Equal<string list>(lost, latest.Skipped)
            Assert.Contains("你按了「过」", HumanPass.toDisplay latest)
        | [] -> failwith "他按了一次「过」，那本账却是空的"

        // 同一刻不动手：到点由平台代过——**同一本账，两种下场分得开**。
        let expired = step (HumanTicked((tableOf model).Turns)) model

        match TablePage.passes expired with
        | latest :: _ ->
            Assert.False(HumanPass.pressed latest)
            Assert.Equal(Some "过", latest.AutoPlayed)
            Assert.Equal<string list>(lost, latest.Skipped)
            Assert.Contains("时限到点", HumanPass.toDisplay latest)
        | [] -> failwith "到点替他过了一次，那本账却是空的"

        // 两条路落定的**牌局是同一个**：过就是过，谁按的不改裁决。
        Assert.Equal((tableOf pressed).Turns, (tableOf expired).Turns)
        Assert.Equal<Event list>(GameState.events (tableOf pressed).State, GameState.events (tableOf expired).State)

    [<Fact>]
    let ``超时那一手在牌谱里与手动那一手同形：回放重建得出逐条相同的事件流`` () =
        // **锚点是回放**（同票 88 那一条）：`Table.replay` 重建时根本不知道谁坐哪一席，
        // 更不知道哪一手是到点代打的——重建得出来就说明那一手在牌谱里与别人的一模一样。
        let expired =
            let model = seated ScaffoldTier.Bare 1

            // 走几手，路上每一次轮到他都让时限吃掉那一手；**停在一次到点摸切刚落定的那一刻**。
            //
            // 停的位置有讲究：牌谱是**事件**流，而「过」不产生事件（`Action.None`）——
            // 停在一轮响应中间的话，这一桌比牌谱多走了一个动作，而牌谱末尾那一记摸牌
            // 后面没有动作跟着（票 85 那条截断牌谱的老账）。**这与到点那一手无关**，
            // 因此把边界挑在一手打牌刚落定处：两边在同一个边界上比才算数。
            let rec walk (moves: int) (fired: int) (model: TableModel) : int * TableModel =
                // **上限出口是失败支不是兜底**（票 114）：从这里出去就意味着这一趟没停在
                // 「一次到点摸切刚落定」那个边界上，而下面拿牌谱对拍的前提就是那个边界；
                // 静静把 `fired, model` 交出去的话，`fired >= 6` 那一句照样能绿（到点够了、
                // 只是最后一手不是打牌），而两边比的已经不是同一个边界了。
                if moves > 300 || Option.isSome (Table.result (tableOf model)) then
                    failwith $"走了 {moves} 手还没停在一次到点摸切刚落定处（到点 {fired} 次）：这一趟走飞了"
                else
                    match TablePage.humanTurn model with
                    | Some package ->
                        let played = step (HumanTicked((tableOf model).Turns)) model
                        let discarded = not (List.isEmpty (HumanSeat.dahaiOptions package))

                        if fired + 1 >= 6 && discarded then
                            fired + 1, played
                        else
                            walk (moves + 1) (fired + 1) played
                    | None -> walk (moves + 1) fired (advancedOrNext model)

            let fired, model = walk 0 0 model
            Assert.True(fired >= 6, $"这一段里只到点了 {fired} 次")

            // **到点那几手真的都记在那本账上**（不是走了一趟什么都没发生）。
            Assert.Equal(fired, TablePage.passes model |> List.filter (HumanPass.pressed >> not) |> List.length)
            Assert.Empty(TablePage.passes model |> List.filter HumanPass.pressed)
            model

        let table = tableOf expired

        let roster =
            match TablePage.rosterOf expired with
            | Some roster -> roster
            | None -> failwith "Live 那一桌必然有配桌"

        match Table.replay (Table.paifu roster table) with
        | Error reason -> failwith $"这一份牌谱该回放得回去，却得到「{reason}」"
        | Ok frames ->
            match List.tryLast frames with
            | Some last ->
                Assert.Equal<Event list>(Table.events roster table, Table.events roster last)
                Assert.Equal(table.Turns, last.Turns)
            | None -> failwith "回放该至少有一帧"

        // 超时那几手**一条决策记录都不留**（他与 bot 席同级，票 87 定的）。
        Assert.Empty table.Decisions

    /// `advancedOrNext` **换局那一支的执行者**（票 114）：上面那条走手循环六次到点全落在同一局里，
    /// 于是它一趟都没被求值过（票 113 §3.1 甲档那一行）。这一条直接把牌桌推到「这一局真的终了」
    /// 那一刻——**量点停在这儿**（判据 20）：局中抓一把只会走另一支。
    [<Fact>]
    let ``没轮到他的那一手：局中只走一手，这一局终了就开下一局`` () =
        // 他坐亲，开局第一手就轮到他：先让时限吃掉那一手，牌桌这才轮得到别人
        // （`Advanced` 在他那一手上本来就不动，拿它做阳性对照会量到一个假的 0）。
        let rec untilOthers (moves: int) (model: TableModel) : TableModel =
            if Option.isNone (TablePage.humanTurn model) then
                model
            elif moves > 20 then
                failwith "连走 20 手还没轮到别家"
            else
                untilOthers (moves + 1) (step (HumanTicked((tableOf model).Turns)) model)

        let model = seated ScaffoldTier.Bare 1 |> untilOthers 0

        // 阳性对照（局中那一支）：走一手就只走一手，局面还是这一局。
        let before = tableOf model
        Assert.False(Table.isKyokuEnded before)
        let stepped = tableOf (advancedOrNext model)
        Assert.Equal(before.Turns + 1, stepped.Turns)
        Assert.Equal((GameState.context before.State).Kyoku, (GameState.context stepped.State).Kyoku)

        // 推到这一局终了：轮到他就让时限吃掉那一手，其余交给同一个 `advancedOrNext`。
        let rec played (moves: int) (model: TableModel) : TableModel =
            if Table.isKyokuEnded (tableOf model) then
                model
            elif moves > 400 then
                failwith "这一局在预算内没打完"
            elif Option.isSome (TablePage.humanTurn model) then
                played (moves + 1) (step (HumanTicked((tableOf model).Turns)) model)
            else
                played (moves + 1) (advancedOrNext model)

        let ended = played 0 model
        let closed = tableOf ended
        Assert.True(Table.isKyokuEnded closed)

        // 换局那一支：下一局真的开了——**这一局不再是终了状态**，而结算面板那几条读法清了空。
        let opened = tableOf (advancedOrNext ended)
        Assert.False(Table.isKyokuEnded opened)
        Assert.Empty opened.Readings
        Assert.True(Option.isNone opened.Latest)
        // 场况往前走了：不连庄就换局数，连庄就多一本场——两条路都不许原地不动。
        let context = GameState.context closed.State
        let next = GameState.context opened.State
        Assert.True((next.Kyoku, next.Honba) <> (context.Kyoku, context.Honba), "开了下一局，场况却一个字没动")
