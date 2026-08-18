namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// 真人坐下，把一局打完（票 87；CONTEXT.md 的 `Human Seat`，spec 的 story 28 / 29 / 30）。
///
/// 这一层钉的是**真人这一侧的四条判据**：
///
/// 1. **真人是第四种选手**：`SeatChoice.Human` → `SeatPlayer.Human` → 牌谱里那一列写 `human`，
///    引擎与编排层不区分它与 AI（同一条 `Demand`、同一份决策包、同一个「回一个 id」）；
/// 2. **合法性驱动 UI**：能点哪几张恒等于**引擎给的合法动作集**里那几条 `Dahai`，
///    摸切与手切各占一条；包外的 id 一律没有事情发生；
/// 3. **真人在想的时候整桌等着**（不限时），他一出手这一桌照旧往下走；
///    响应阶段一律自动过，而**过掉了什么记得住**；
/// 4. **可见性**：`humanSeated` 从此说真话——对局中气泡一个都没有、视角锁死自家、
///    曳光弹不给开；**终局后三样一起松开**。
///
/// 画出来长什么样（手牌真点得动、他家的手牌一张都不在页面里、视角按钮不在 DOM 里）
/// 在浏览器那一侧（`web/scripts/verify-human.mjs`）——这里一行 Feliz 都不 open。
module HumanSeatTests =

    let private seat (index: int) : Seat =
        match Seat.ofIndex index with
        | Some seat -> seat
        | None -> failwith $"{index} 应当是合法座位"

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    /// 这一条消息发出了副作用吗（`Cmd` 就是一串效果体，`Cmd.none` 是空表）。
    ///
    /// **它就是「续没续定时器」那一条的执行者**（判据 2）：等真人那一下点击时，
    /// 定时器再转也只会把牌桌空转一遍——而那正是票 74 给在飞的回执定下的那条规矩。
    let private effects (message: TableMsg) (model: TableModel) : int =
        TablePage.update message model |> snd |> List.length

    /// 真人坐第 `index` 席、其余三家均匀随机的那一桌（`?table=1` 的默认种子）。
    let private humanAt (index: int) : TableModel =
        SeatingPlan.initial Ruleset.yonma
        |> SeatingPlan.bind (seat index) SeatChoice.Human
        |> TablePage.initial RulesetDraft.initial
        |> fst

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
        | None -> failwith "这一刻该轮到真人出牌"

    /// 引擎此刻给这一席的合法动作集（**第三个锚点**）：`HumanSeat` 那几个出口拆的是决策包，
    /// 而决策包是从这一份编号来的——拿包去对包就是拿同一个表达式对它自己（判据 6 那一族）。
    let private legalFor (index: int) (model: TableModel) : Action list =
        Table.pendings (tableOf model)
        |> List.tryFind (fun choice -> choice.Seat = seat index)
        |> Option.map (fun choice -> choice.Actions)
        |> Option.defaultValue []

    /// 这一席此刻看得见的那份观测（河上的手切 / 摸切标记读它）。
    let private observationOf (index: int) (model: TableModel) : Observation =
        match Table.observation (seat index) (tableOf model) with
        | Some observation -> observation
        | None -> failwith $"座位 {index} 该有一份观测"

    /// 把这一桌推到**终局**：轮到真人就点包里的头一条打牌（响应阶段按「过」），
    /// 否则单步（一局终了就开下一局）。
    ///
    /// **它就是闸门那句「点手牌→打出→等三家→再点」在 dotnet 上的那一份**；
    /// `limit` 是防死循环的闸，正常一整场东风战约 440 步（探针实测 363 步 + 74 次点击）。
    ///
    /// **响应阶段不再自动过了**（票 88）：那一手同样停下来等他，因此这里要替他按「过」。
    /// **两支都不允许落空**：一包里要么有打牌、要么有「过」（`Action.None` 那段注释）。
    let private playedOut (limit: int) (model: TableModel) : TableModel =
        // 一步：轮到真人就点包里的头一条，否则单步（这一局终了就开下一局）。
        let moved (model: TableModel) : TableModel =
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
                    let next = step Advanced model

                    // 防死循环：一步下去手数没动，而它既不轮到真人、这一局也没终。
                    if
                        (tableOf next).Turns = table.Turns
                        && Option.isNone (TablePage.humanTurn next)
                        && not (Table.isKyokuEnded (tableOf next))
                    then
                        failwith $"第 {table.Turns} 手推不动，而它既不轮到真人也没终局"

                    next

        let rec play (moves: int) (model: TableModel) : TableModel =
            if Option.isSome (Table.result (tableOf model)) then
                model
            elif moves >= limit then
                failwith $"这一场该在 {limit} 步之内打完，却还没终局"
            else
                play (moves + 1) (moved model)

        play 0 model

    // ---- 真人是第四种选手 ----

    [<Fact>]
    let ``真人是第四种选手：配桌里是 Human，牌谱里那一列写 human，与 bot 和模型都分得开`` () =
        let model = humanAt 0

        let roster =
            match TablePage.rosterOf model with
            | Some roster -> roster
            | None -> failwith "Live 那一桌必然有配桌"

        Assert.Equal(SeatPlayer.Human, Roster.playerAt (seat 0) roster)
        Assert.Equal<string list>([ "human"; "random"; "random"; "random" ], Roster.names roster)

        // **与另外两种分得开**：模型席恒带一道斜杠（`provider/model`），bot 席是那两个词。
        Assert.DoesNotContain("/", Roster.humanName)
        Assert.NotEqual<string>(Roster.humanName, Bot.toWire Bot.Uniform)
        Assert.NotEqual<string>(Roster.humanName, Bot.toWire Bot.Opinionated)
        // **里面没有私人信息**：它是一个写死的词，不是昵称、不是档案名。
        Assert.Equal("human", SeatChoice.toWire SeatChoice.Human)
        Assert.Equal(Some SeatChoice.Human, SeatChoice.ofWire "human")

    [<Fact>]
    let ``真人只坐得下一席：坐上第二席时，原来那一席退回均匀随机`` () =
        let moved =
            SeatingPlan.initial Ruleset.yonma
            |> SeatingPlan.bind (seat 0) SeatChoice.Human
            |> SeatingPlan.bind (seat 3) SeatChoice.Human

        // **刚拨的那一席赢**：人刚把自己摆到座位 3，结果坐到了座位 0 那才叫话都不说。
        Assert.Equal<Seat list>([ seat 3 ], SeatingPlan.humanSeats moved)
        Assert.Equal(SeatPlayer.Bot Bot.Uniform, SeatingPlan.playerAt (seat 0) moved)

        // localStorage 被手改过（两席都写着 `human`）时由 `fit` 掰正：留头一席。
        let crooked =
            { SeatingPlan.initial Ruleset.yonma with
                Seats =
                    SeatingPlan.initial Ruleset.yonma
                    |> fun plan ->
                        plan.Seats
                        |> List.map (fun binding ->
                            { binding with
                                Choice = SeatChoice.Human
                            })
            }

        Assert.Equal<Seat list>([ seat 0 ], SeatingPlan.humanSeats (SeatingPlan.fit Ruleset.yonma crooked))

    // ---- 合法性驱动 UI ----

    [<Fact>]
    let ``能点哪几张由引擎给的合法动作集定：多一张少一张都不许`` () =
        // 座位 0 是东 1 局的亲，摸完牌等着打——开局第一手就轮到他。
        let model = humanAt 0
        let package = turnOf model

        let legal =
            legalFor 0 model
            |> List.choose (fun action ->
                match action with
                | Action.Dahai(_, pai, tsumogiri) -> Some(pai, tsumogiri)
                | _ -> None)

        // 防空转：这一手真有得打（14 张手牌，摸切那一条也在）。
        Assert.NotEmpty legal
        Assert.Contains(true, legal |> List.map snd)

        let offered =
            HumanSeat.dahaiOptions package
            |> List.map (fun (_, pai, tsumogiri, _) -> pai, tsumogiri)

        // **逐条相同**：`legal` 来自引擎的合法动作集，`offered` 是页面照着渲染的那一份。
        Assert.Equal<(Tile * bool) list>(legal, offered)

        // 反过来也要成立：合法动作集里没有的组合，页面上一律给不出 id
        // （手里没有的那张牌、以及不许摸切的那几张）。
        for pai in Tile.all do
            for tsumogiri in [ true; false ] do
                let expected = legal |> List.contains (pai, tsumogiri)
                let given = HumanSeat.dahai pai tsumogiri package |> Option.isSome

                Assert.Equal(expected, given)

        // 每一条都换得回引擎那条动作（跨界回来的只有一个 id，与模型席逐字同一条路）。
        for id, pai, tsumogiri, _ in HumanSeat.dahaiOptions package do
            Assert.Equal(Some(Action.Dahai(seat 0, pai, tsumogiri)), DecisionPackage.tryAction id package)

    [<Fact>]
    let ``摸切与手切各占一条：点哪一条，河上那一格就写着哪一种`` () =
        let model = humanAt 0
        let package = turnOf model

        let tsumogiri =
            HumanSeat.dahaiOptions package |> List.filter (fun (_, _, giri, _) -> giri)

        // 摸切**恰好一条**（刚摸进的那一张），而手切有一串。
        Assert.Single tsumogiri |> ignore
        Assert.NotEmpty(HumanSeat.dahaiOptions package |> List.filter (fun (_, _, giri, _) -> not giri))

        let played (giri: bool) =
            let id, _, _, _ =
                HumanSeat.dahaiOptions package |> List.find (fun (_, _, each, _) -> each = giri)

            let after = step (HumanPlayed id) model

            match (observationOf 0 after).Self.Kawa |> List.tryLast with
            | Some entry -> entry.Tsumogiri
            | None -> failwith "打出去那一张该在自家河里"

        Assert.True(played true, "点摸切那一条，河上该写着摸切")
        Assert.False(played false, "点手切那一条，河上该写着手切")

    [<Fact>]
    let ``点一条不在这一包里的 id：没有事情发生（真人也不可能犯规）`` () =
        let model = humanAt 0
        let before = tableOf model

        // 9999 与「负一」都不在包里：牌桌一手都不动，那一份包也还摆在页面上等他重点。
        for id in [ 9999; -1 ] do
            let after = step (HumanPlayed id) model
            Assert.Equal(before.Turns, (tableOf after).Turns)
            Assert.True(Option.isSome (TablePage.humanTurn after), "包外的一下不该把那一手收走")

    // ---- 整桌等着他，他一出手就接着走 ----

    [<Fact>]
    let ``真人在想的时候整桌等着：单步与定时器都推不动，他点一下才走`` () =
        let model = humanAt 0
        let before = tableOf model
        Assert.True(Option.isSome (TablePage.humanTurn model))

        // 「单步」与一记定时器都推不动这一手——不限时（时限是票 89）。
        for message in [ Advanced; Ticked 0; Ticked 1 ] do
            let after = step message model
            Assert.Equal(before.Turns, (tableOf after).Turns)

        // **定时器也不续**（与票 74 给在飞回执定的那条规矩同一句）：按下「播放」一个效果体都不发。
        // **阳性对照就在旁边**：同一条消息在四家 bot 那一桌上必须真发出一记定时器——
        // 没有它的话，一个从来不发 Cmd 的实现也能让上面那一句变绿。
        Assert.Equal(0, effects PlayToggled model)

        let bots =
            TablePage.initial RulesetDraft.initial (SeatingPlan.initial Ruleset.yonma)
            |> fst

        Assert.True(effects PlayToggled bots > 0, "四家 bot 那一桌按下「播放」该真发一记定时器")

        // 他点一下：这一手落下去，那一份包收走。
        let id, _, _, _ = HumanSeat.dahaiOptions (turnOf model) |> List.head
        let played = step (HumanPlayed id) model

        Assert.Equal(before.Turns + 1, (tableOf played).Turns)
        Assert.Equal(None, TablePage.humanTurn played)
        Assert.Equal(1, (observationOf 0 played).Self.Kawa |> List.length)

    [<Fact>]
    let ``真人那一手不留决策记录：他与 bot 在牌谱里同级`` () =
        let model = humanAt 0
        let id, _, _, _ = HumanSeat.dahaiOptions (turnOf model) |> List.head
        let played = step (HumanPlayed id) model

        Assert.Empty (tableOf played).Decisions
        Assert.Equal(0, Table.fallbacks (tableOf played))

    // ---- 响应阶段一律自动过，而且说得出过掉了什么 ----

    [<Fact>]
    let ``响应阶段停下来等他：他自己按那一条「过」，而放掉了什么记得住（票 88 换掉了票 87 的自动过）`` () =
        let played = humanAt 0 |> playedOut 2000
        let passes = TablePage.passes played

        // 防空转（判据 3）：这一整场里他真的按过「过」。
        Assert.NotEmpty passes

        for pass in passes do
            Assert.Equal(seat 0, pass.Seat)
            // **放掉了什么必须记下来**：一条空的「你按了过」等于没说。
            Assert.NotEmpty pass.Skipped
            // 「过」本身不在里面：它是他按下去的那一条，不是被放掉的那几条。
            Assert.DoesNotContain("过", String.concat "、" pass.Skipped)
            Assert.Contains(string pass.Turn, HumanPass.toDisplay pass)

    [<Fact>]
    let ``真人坐一席，把一整场东风战打完：终局点数四家和为定值`` () =
        let played = humanAt 0 |> playedOut 2000

        let result =
            match Table.result (tableOf played) with
            | Some result -> result
            | None -> failwith "这一场该打到终局"

        Assert.Equal(4, List.length result.Scores)
        Assert.Equal(100000, List.sum result.Scores)
        // 一手都没兜底：兜底只发生在模型席上，这一桌根本没有模型。
        Assert.Equal(0, Table.fallbacks (tableOf played))

    // ---- 可见性：humanSeated 说真话了 ----

    /// 把这几条决策记录拌进 Live 那一桌（浏览器里这几条是模型席答出来的）。
    let private withRecords (records: DecisionRecord list) (model: TableModel) : TableModel =
        let live = liveOf model

        { model with
            Source =
                Source.Live
                    { live with
                        Table = Result.map (fun table -> { table with Decisions = records }) live.Table
                    }
        }

    let private recorded (index: int) : DecisionRecord =
        {
            Turn = index
            Seat = seat index
            PromptTail = "【现在】东1局 0 本场……"
            RenderVersion = "janpo-default@aaaaaaaa.bbbbbbbb"
            ActionIds = [ 0 ]
            Output = """{"stop_reason":"toolUse"}"""
            Reason = Some $"座位 {index} 的一句话理由"
            Thinking = None
            Attempts = 1
            LatencyMs = 640
            Applied = Some 0
            Fallback = None
            Usage = None
        }

    [<Fact>]
    let ``有真人在座：对局中一个气泡都没有，终局后四家的都回来`` () =
        let records = [ for index in 1..3 -> recorded index ]

        // 阳性对照先立起来：**同一份记录**，没有真人的那一桌四席的气泡该在。
        let bots =
            TablePage.initial RulesetDraft.initial (SeatingPlan.initial Ruleset.yonma)
            |> fst
            |> withRecords records

        for index in 1..3 do
            Assert.True(Option.isSome (TablePage.bubbles bots (tableOf bots) (seat index)), $"座位 {index} 该有气泡")

        // 真人坐下：对局中一个都没有（`unlocked` 那一根，ADR-0003）。
        let human = humanAt 0 |> withRecords records

        for index in 0..3 do
            Assert.Equal(None, TablePage.bubbles human (tableOf human) (seat index))

        // 终局之后它们回来——escape hatch 是「这一场打完了」，不是切一下视角。
        let settled = humanAt 0 |> playedOut 2000 |> withRecords records

        for index in 1..3 do
            Assert.True(
                Option.isSome (TablePage.bubbles settled (tableOf settled) (seat index)),
                $"终局后座位 {index} 的气泡该回来"
            )

    [<Fact>]
    let ``对局中视角锁死自家：上帝视角与别席视角连值都给不出来，终局后松开`` () =
        let model = humanAt 2

        Assert.Equal(Some(seat 2), TablePage.lockedSeat model)
        Assert.Equal(Viewpoint.Seated(seat 2), TablePage.viewpoint model)

        // **发一条消息进来也改不动**：按钮不在 DOM 里是一道，这里是另一道。
        for picked in [ Viewpoint.God; Viewpoint.Seated(seat 0); Viewpoint.Seated(seat 3) ] do
            let switched = step (ViewpointPicked picked) model
            Assert.Equal(Viewpoint.Seated(seat 2), TablePage.viewpoint switched)
            // 他家的手牌因此在投影里根本不存在（`MaskedSeat` 没有手牌字段）。
            Assert.True(TablePage.reveals switched (seat 2))
            Assert.False(TablePage.reveals switched (seat 0))

        // 没有真人的那一桌照旧（阳性对照：锁不是恒成立的）。
        let bots =
            TablePage.initial RulesetDraft.initial (SeatingPlan.initial Ruleset.yonma)
            |> fst

        Assert.Equal(None, TablePage.lockedSeat bots)
        Assert.Equal(Viewpoint.God, TablePage.viewpoint (step (ViewpointPicked Viewpoint.God) bots))

        // 终局之后锁松开，那几枚按钮也就回来了（判据与气泡同一条）。
        let settled = humanAt 2 |> playedOut 2000
        Assert.Equal(None, TablePage.lockedSeat settled)
        Assert.Equal(Viewpoint.God, TablePage.viewpoint (step (ViewpointPicked Viewpoint.God) settled))

    [<Fact>]
    let ``真人在座时曳光弹不给开（22-A），没有真人时照旧开得了`` () =
        // 阴性对照：四家 bot 的那一桌与首页回放照旧允许（挂账堵的是「真人在座」这一种）。
        Assert.True(
            TablePage.devSurfaceAllowed (
                TablePage.initial RulesetDraft.initial (SeatingPlan.initial Ruleset.yonma)
                |> fst
            )
        )

        Assert.True(TablePage.devSurfaceAllowed (TablePage.home () |> fst))

        // 真人在座、这一场还没打完：不给开——那一块把 `start_kyoku`（四家配牌）
        // 印在同一张文档里，而它的种子输入框是任填的。
        Assert.False(TablePage.devSurfaceAllowed (humanAt 0))

        // 终局之后回来（判据与视角锁、气泡同一条）。
        Assert.True(TablePage.devSurfaceAllowed (humanAt 0 |> playedOut 2000))
