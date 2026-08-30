namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// 终局记分卡在页面这一侧（票 133）：把三处摆成一张表，再把同一张表写成一段纯文本。
///
/// **算数的那一半不在这里**（`Janpo.Engine.Tests.ScorecardTests` 钉着 `Scorecard.tally`）。
/// 这里钉的是页面这一层真正会做错的三件事：
/// 牌桌那条路与牌谱那条路给出同一份逐席记分、四行按座位对得上号、
/// 以及**屏幕上那张表与复制出去那段文字是同一份数**。
module ScorecardViewTests =

    let private ruleset = Ruleset.yonma

    let private roster = Roster.allRandom ruleset

    let private seat (index: int) : Seat =
        match Seat.ofIndex index with
        | Some seat -> seat
        | None -> failwith $"{index} 应当是合法座位"

    /// 四家随机把整场打完的那一桌（与 `ReplayTableTests` 同一个种子、同一条路）。
    let private played (seed: int) : Table =
        let rec toEnd (left: int) (current: Table) =
            match Table.pending current with
            | None -> current
            | Some _ when left <= 0 -> failwith "这一局在预算内没打完"
            | Some _ -> toEnd (left - 1) (Table.advance roster current)

        let rec whole (left: int) (current: Table) =
            match Table.result current with
            | Some _ -> current
            | None when left <= 0 -> failwith "这一场在预算内没打完"
            | None -> whole (left - 1) (current |> toEnd 400 |> Table.nextKyoku)

        match Table.start ruleset seed with
        | Ok table -> whole 40 table
        | Error error -> failwith $"这个种子应当开得了局，却得到「{error}」"

    [<Fact>]
    let ``牌桌那条路与牌谱那条路给出同一份逐席记分`` () =
        // **两个入口，一段实现**：牌桌不必先拼一份连名字都是假的牌谱，
        // 但它算出来的必须与「导出牌谱再聚合」逐字相同——否则页面上那张表
        // 与人分享出去的那份牌谱说的就是两件事。
        let table = played 1177
        let mine = Table.scorecard table

        // **先证明它不是在比两张空表**（判据 3）：种子 1177 这一场真有人和了。
        Assert.True((mine |> List.sumBy (fun tally -> tally.Hora)) > 0, "种子 1177 这一场应当有人和了，否则下面那条等式在比两张空表")
        Assert.Equal<SeatTally list>(mine, Scorecard.ofPaifu (Table.paifu roster table))

    [<Fact>]
    let ``四家随机对局：一手都没问过模型，因此那几列恒是 0`` () =
        // 阴性对照的分母（`Asked`）：0 与「一手都没兜底」不是一回事，这一条把它钉住。
        let tallies = played 1177 |> Table.scorecard

        Assert.Equal(4, List.length tallies)

        for tally in tallies do
            Assert.Equal(0, tally.Asked)
            Assert.Equal(0, tally.Fallbacks)
            Assert.Equal(0, tally.Retries)
            Assert.Equal(0, Usage.promptTokens tally.Usage)

    /// 四席的「选手 · 档」。**档位那半句走 `ScaffoldTier.toDisplay`**（不手打）：
    /// 档位改名时这份 fixture 要跟着变，而不是悄悄钉住一个过期的写法。
    let private players () : ScorecardPlayer list =
        [
            ScaffoldTier.Bare
            ScaffoldTier.Assisted
            ScaffoldTier.Bare
            ScaffoldTier.ToolSearch
        ]
        |> List.mapi (fun index tier ->
            ScorecardPlayer.Named($"deepseek/model-{index}", ScorecardTier.Set(ScaffoldTier.toDisplay tier)))

    /// 一份手捏的记分卡：四席、四个不同的顺位与终点。
    let private rows () : ScorecardRow list =
        let table = played 1177

        let seats =
            match Board.ofTable Viewpoint.God table with
            | Some board -> board.Seats
            | None -> failwith "上帝视角应当摆得出牌桌"

        let result =
            match Table.result table with
            | Some result -> result
            | None -> failwith "这一场应当已经终局"

        ScorecardView.rows seats (players ()) result (Table.scorecard table)

    [<Fact>]
    let ``四行按座位对得上号，顺位与终点取自终局精算`` () =
        let table = played 1177

        let result =
            match Table.result table with
            | Some result -> result
            | None -> failwith "这一场应当已经终局"

        let rows = rows ()

        Assert.Equal(4, List.length rows)
        Assert.Equal<int list>([ 0; 1; 2; 3 ], rows |> List.map (fun row -> Seat.index row.Seat))
        Assert.Equal<int list>(result.Juni, rows |> List.map (fun row -> row.Juni))
        Assert.Equal<int list>(result.Scores, rows |> List.map (fun row -> row.Score))
        Assert.Equal<ScorecardPlayer list>(players (), rows |> List.map (fun row -> row.Player))

    [<Fact>]
    let ``某一处短了就少几行，不拿默认值凑`` () =
        // 凑出来的那一行是一句假话：它会写着某一席的顺位，而那个顺位根本没算出来。
        let table = played 1177

        let seats =
            match Board.ofTable Viewpoint.God table with
            | Some board -> board.Seats
            | None -> failwith "上帝视角应当摆得出牌桌"

        let result =
            match Table.result table with
            | Some result -> result
            | None -> failwith "这一场应当已经终局"

        let short =
            ScorecardView.rows seats (players () |> List.truncate 2) result (Table.scorecard table)

        Assert.Equal(2, List.length short)

    [<Fact>]
    let ``复制出去那段文字与屏幕上那张表逐格相同`` () =
        // **两处读同一份 `ScorecardRow`**：这一条就是那句话的执行体。
        let rows = rows ()
        let text = ScorecardView.toText rows
        let lines = text.Split('\n') |> List.ofArray

        // 抬头一行 + 表头一行 + 分隔一行 + 四行。
        Assert.Equal(7, List.length lines)

        // **逐列切开比，不是「这一行里含这个字串」**：「0」这种格在任何一行里都命中得了，
        // 那样的比法逮不住串列与错位。
        let split (line: string) =
            line.Split('|')
            |> List.ofArray
            |> List.map (fun cell -> cell.Trim())
            |> List.filter (fun cell -> cell <> "")

        Assert.Equal<string list>(ScorecardView.headers, split (List.item 1 lines))

        for index, row in List.indexed rows do
            Assert.Equal<string list>(ScorecardView.cells row, split (List.item (index + 3) lines))

    [<Fact>]
    let ``表头与每一行的格数相同`` () =
        // 表头与格子各写一份的话，加一列时只改一处就会错位——而错位的表每一格都在说别人的事。
        let headers = List.length ScorecardView.headers

        for row in rows () do
            Assert.Equal(headers, List.length (ScorecardView.cells row))

    // ---- 账单上那笔对不上的差额（票 108/110；DECISIONS 133-1 那条自裁的执行体） ----

    /// 一笔花掉了、却没落成一手的问话（`VoidCause.Expired`：问出去之后这一手翻篇了）。
    let private paidVoid (ticket: int) (index: int) (usage: Usage) (table: Table) : Table =
        table
        |> Table.voidAsk
            {
                Ticket = ticket
                Turn = table.Turns
                Seat = seat index
                Cause = VoidCause.Expired
                Usage = None
            }
        |> Table.creditVoid ticket (seat index) (Some usage)

    [<Fact>]
    let ``四行相加加上那笔差额，恰好是牌桌那条账单行`` () =
        // 这一条就是 `Scorecard.fs` 那句「四行相加**小于等于**账单行上的总额」的执行体
        // （判据 2：写下不变量先问谁执行它）。**回放那一侧碰不到它**（`Voided` 恒空），
        // 因此浏览器闸门永远不会替它开口——只有这里能。
        let table =
            played 1177
            |> paidVoid
                7
                1
                {
                    Input = 300
                    Output = 40
                    CacheRead = 100
                    CacheWrite = 0
                }
            |> paidVoid
                9
                2
                {
                    Input = 11
                    Output = 2
                    CacheRead = 0
                    CacheWrite = 5
                }

        Assert.Equal(2, table |> Table.paidVoids |> List.length)

        let seats =
            match Board.ofTable Viewpoint.God table with
            | Some board -> board.Seats
            | None -> failwith "上帝视角应当摆得出牌桌"

        let result =
            match Table.result table with
            | Some result -> result
            | None -> failwith "这一场应当已经终局"

        let rows = ScorecardView.rows seats (players ()) result (Table.scorecard table)
        let gap = ScorecardView.voidedGap (Table.usage table) rows

        // 差额恰好是那两笔（四家随机对局一条决策记录都没有，因此四行相加恒是 0）。
        Assert.Equal(416, Usage.promptTokens gap)
        Assert.Equal(42, gap.Output)
        // 而它恒 ≥ 0：作废那几笔只会让账单行更大，不会让它更小。
        Assert.True(Usage.promptTokens gap >= 0, "四行相加竟然大于账单行：那两个数的口径漂了")
        // 页面上那句话把两个数都印出来——人要能自己核「差了多少」。
        let said = ScorecardView.voidedSaid 2 gap
        Assert.Contains("2 次", said)
        Assert.Contains("416", said)
        Assert.Contains("42", said)

    [<Fact>]
    let ``没有那几笔时差额恰好是 0`` () =
        // 阴性对照：**回放与不花钱的那几桌上它必须是 0**，否则上面那条什么都没证明。
        let table = played 1177
        let rows = rows ()

        Assert.Empty(Table.paidVoids table)
        Assert.Equal(Usage.zero, ScorecardView.voidedGap (Table.usage table) rows)

    // ---- 「复制记分卡」那两条消息（票 133） ----

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    let private shownTable (model: TableModel) : Table =
        match TablePage.shown model with
        | Shown.Board table -> table
        | other -> failwith $"这一刻该有一桌，却是 {other}"

    let private host () : TableModel =
        TablePage.initial RulesetDraft.initial (SeatingPlan.initial ruleset) |> fst

    /// **真把 Live 那一桌打到终局**（四家自带 bot，一个请求都不发）：
    /// 只有那时记分卡才在，「复制记分卡」那一枚才点得着。
    let private hostToEnd () : TableModel =
        let rec loop (left: int) (model: TableModel) : TableModel =
            match TablePage.shown model with
            | Shown.Loading
            | Shown.Fault _ -> failwith "这一桌应当摆得出来"
            | Shown.Board table when Option.isSome (Table.result table) -> model
            | Shown.Board _ when left <= 0 -> failwith "这一场在预算内没打完"
            | Shown.Board table ->
                let next =
                    if Option.isSome (Table.pending table) then
                        step Advanced model
                    else
                        step KyokuAdvanced model

                loop (left - 1) next

        loop 3000 (host ())

    [<Fact>]
    let ``复制那一趟的三态：写进去了、写不进去、还没点过`` () =
        // `data-scorecard-copy` 那两个 wire 值各有一条真走得到的路（判据 3：
        // 「写不进去」那一支闸门里到不了，只有这里到得了）。
        let fresh = host ()
        Assert.Equal(None, fresh.ScorecardCopy)
        Assert.Equal(Some(Ok 375), (fresh |> step (ScorecardCopySettled(Ok 375))).ScorecardCopy)

        Assert.Equal(
            Some(Error "浏览器不让写剪贴板（x）"),
            (fresh |> step (ScorecardCopySettled(Error "浏览器不让写剪贴板（x）"))).ScorecardCopy
        )

    [<Fact>]
    let ``再点一次先把上一次的下场撤下来：新的一次正在路上`` () =
        // 旧话留着会两头打架——人会以为刚点那一下成了，而它可能还在飞。
        // **要真打到终局才点得着那一枚**：还没终局时它连 DOM 都不在（下一条钉的就是那半句）。
        let copied = hostToEnd () |> step (ScorecardCopySettled(Ok 42))

        Assert.Equal(4, TablePage.scorecard copied (shownTable copied) |> List.length)
        Assert.Equal(Some(Ok 42), copied.ScorecardCopy)
        Assert.Equal(None, (copied |> step ScorecardCopied).ScorecardCopy)

    [<Fact>]
    let ``还没终局时点它一律无事发生：那时按钮根本不在 DOM 里`` () =
        // 阴性对照与它的**阳性对照**钉在同一处（判据 21）：**同一桌**开局那一刻记分卡是空表、
        // 点了什么都不发生；打完之后同一个取值器给得出四行，而那一下真的把旧话撤了。
        let opening = host ()
        let ended = hostToEnd () |> step (ScorecardCopySettled(Ok 42))

        Assert.Empty(TablePage.scorecard opening (shownTable opening))
        // 还没终局时那一枚不在 DOM 里，因此这条消息**什么都不做**——旧话原样留着。
        let stale = opening |> step (ScorecardCopySettled(Ok 42))
        Assert.Equal(Some(Ok 42), (stale |> step ScorecardCopied).ScorecardCopy)

        // 阳性：同一个取值器在终局那一桌上给得出四行，而 `ScorecardCopied` 真的动了模型。
        Assert.Equal(4, TablePage.scorecard ended (shownTable ended) |> List.length)
        Assert.Equal(Some(Ok 42), ended.ScorecardCopy)
        Assert.Equal(None, (ended |> step ScorecardCopied).ScorecardCopy)

    // ---- 「选手 · 档」那一格的四态（票 133，边界由调度器在票外松过一次） ----

    [<Fact>]
    let ``四态各说各的话，wire 值也各不相同`` () =
        // **不许压成同一个 case**（判据 12）：「这一席没有档位」（bot 不走 prompt）与
        // 「这份牌谱没记档位」（回放）在页面上是两句不同的话；
        // 「连身份都没有」（v1 老牌谱）又是第三句。
        let tiered =
            ScorecardPlayer.Named("deepseek/deepseek-v4-flash", ScorecardTier.Set "完整")

        let botless = ScorecardPlayer.Named("random", ScorecardTier.NotApplicable)

        let replayed =
            ScorecardPlayer.Named("deepseek/deepseek-v4-flash", ScorecardTier.Unrecorded)

        Assert.Equal("deepseek/deepseek-v4-flash・完整", ScorecardPlayer.toDisplay tiered)
        Assert.Equal("random", ScorecardPlayer.toDisplay botless)
        Assert.Equal("deepseek/deepseek-v4-flash・档位牌谱没记", ScorecardPlayer.toDisplay replayed)
        Assert.Equal("牌谱没记", ScorecardPlayer.toDisplay ScorecardPlayer.Unknown)

        let wires =
            [ tiered; botless; replayed; ScorecardPlayer.Unknown ]
            |> List.map ScorecardPlayer.toWire

        Assert.Equal<string list>([ "tiered"; "no-tier"; "tier-unrecorded"; "unrecorded" ], wires)
        // 四个 wire 值**两两不同**：撞了的话闸门就分不出是哪一态。
        Assert.Equal(4, wires |> List.distinct |> List.length)

    [<Fact>]
    let ``身份那半格恒是牌谱里那一列 names，档位那半格另算`` () =
        let replayed =
            ScorecardPlayer.Named("deepseek/deepseek-v4-flash", ScorecardTier.Unrecorded)

        Assert.Equal("deepseek/deepseek-v4-flash", ScorecardPlayer.nameSaid replayed)
        Assert.Equal("档位牌谱没记", ScorecardPlayer.tierSaid replayed)
        // 连身份都没有那一态：两半都是空串，整格那句话才是「牌谱没记」。
        Assert.Equal("", ScorecardPlayer.nameSaid ScorecardPlayer.Unknown)
        Assert.Equal("", ScorecardPlayer.tierSaid ScorecardPlayer.Unknown)
        // 这一席没有档位那一态：档位那半格是空串，而身份照样在。
        Assert.Equal("", ScorecardPlayer.tierSaid (ScorecardPlayer.Named("random", ScorecardTier.NotApplicable)))

    [<Fact>]
    let ``档位那半段与名牌上那半句同源：一处判据，不许两份`` () =
        // `SeatingPlan.tiers` 与 `SeatingPlan.nameplates` 都由同一个 `plateTier` 出，
        // 因此「这一席写不写档位」在两处不可能漂——这一条就是那句话的执行体。
        let retier (at: Seat) (tier: ScaffoldTier) (plan: SeatingPlan) =
            SeatingPlan.editSeat at SeatField.Tier (ScaffoldTier.toWire tier) plan

        let seating =
            SeatingPlan.initial ruleset
            |> SeatingPlan.addProfile
            |> SeatingPlan.bind (seat 1) (SeatChoice.Profile "档案 1")
            |> retier (seat 1) ScaffoldTier.Assisted
            |> SeatingPlan.bind (seat 2) SeatChoice.Human
            |> retier (seat 2) ScaffoldTier.ToolSearch

        let plates = SeatingPlan.nameplates seating
        let tiers = SeatingPlan.tiers seating

        Assert.Equal(List.length plates, List.length tiers)

        for plate, tier in List.zip plates tiers do
            match tier with
            // 写档位的那几席：名牌那一句必须以「・<那一档>」收尾。
            | Some said -> Assert.EndsWith($"・{said}", plate)
            // 不写档位的那几席（bot / 强 AI 基线）：名牌那一句里连那个分隔符都没有。
            | None -> Assert.DoesNotContain("・", plate)

        // 阳性对照：这份坐法里**真的**两种都有，否则上面那个循环有一半在空转。
        Assert.Contains(tiers, Option.isSome)
        Assert.Contains(tiers, Option.isNone)

    [<Fact>]
    let ``回放那一屏：身份取牌谱那一列 names，档位写「档位牌谱没记」`` () =
        // 同一屏上不许两个说法：名牌画的是 `Table.names`，记分卡的身份格必须逐字相同。
        let table = played 1177
        let paifu = Table.paifu roster table

        let replay =
            match Table.replay paifu with
            | Ok frames -> frames
            | Error error -> failwith $"这份牌谱应当摆得出牌桌，却得到「{error}」"

        let last =
            match List.tryLast replay with
            | Some table -> table
            | None -> failwith "逐帧的牌桌不该是空的"

        let model = TablePage.home () |> fst |> step (DemoLoaded(Ok paifu))
        let names = TablePage.nameplates model
        let rows = TablePage.scorecard model last

        Assert.Equal(4, List.length rows)
        Assert.Equal<string list>(Table.names paifu, names)

        for name, row in List.zip names rows do
            Assert.Equal("tier-unrecorded", ScorecardPlayer.toWire row.Player)
            Assert.Equal(name, ScorecardPlayer.nameSaid row.Player)
            Assert.Equal("档位牌谱没记", ScorecardPlayer.tierSaid row.Player)

    [<Fact>]
    let ``牌谱那一列 names 是空串时，整格才退回「牌谱没记」`` () =
        // v1 老牌谱与「名字压根没写」的那一种：**只有这一种**才留不下身份。
        let table = played 1177
        let blank = Roster.allRandom ruleset |> Roster.names |> List.map (fun _ -> "")

        let paifu = Table.paifu roster table

        let anonymous =
            { paifu with
                Events =
                    paifu.Events
                    |> List.map (fun event ->
                        match event with
                        | StartGame _ -> StartGame blank
                        | other -> other)
            }

        let framesOf (paifu: Paifu) =
            match Table.replay paifu with
            | Ok frames -> List.last frames
            | Error error -> failwith $"这份牌谱应当摆得出牌桌，却得到「{error}」"

        let model = TablePage.home () |> fst |> step (DemoLoaded(Ok anonymous))

        for row in TablePage.scorecard model (framesOf anonymous) do
            Assert.Equal("unrecorded", ScorecardPlayer.toWire row.Player)
            Assert.Equal("牌谱没记", ScorecardPlayer.toDisplay row.Player)

        // **阳性对照**：同一条路上、名字没被抹掉的那份牌谱，四行都写得出身份。
        let named = TablePage.home () |> fst |> step (DemoLoaded(Ok paifu))

        for row in TablePage.scorecard named (framesOf paifu) do
            Assert.Equal("tier-unrecorded", ScorecardPlayer.toWire row.Player)

    [<Fact>]
    let ``Live 那一桌：身份是牌谱里那一列 names，不是本机那个私人档案名`` () =
        // 记分卡是**要被带走的东西**（贴 issue / 贴群）：本机档案名是私人叫法
        // （`ModelProfile.Name` 那条术语），带出去谁也认不出是哪个模型。
        // 四家自带 bot 那一桌因此写 `random`，而名牌上写的是「均匀随机」。
        let ended = hostToEnd ()
        let rows = TablePage.scorecard ended (shownTable ended)

        Assert.Equal(4, List.length rows)

        let roster =
            match TablePage.rosterOf ended with
            | Some roster -> roster
            | None -> failwith "Live 那一桌应当有配桌"

        for name, row in List.zip (Roster.names roster) rows do
            Assert.Equal(name, ScorecardPlayer.nameSaid row.Player)
            // bot 席不写档位（与名牌同一条判据）：那一格是空串，wire 值另有一个。
            Assert.Equal("no-tier", ScorecardPlayer.toWire row.Player)
            Assert.Equal("", ScorecardPlayer.tierSaid row.Player)
