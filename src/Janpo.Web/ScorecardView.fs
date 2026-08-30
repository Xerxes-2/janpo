namespace Janpo.Web

open Janpo

/// 记分卡上「选手 · 档」那一格**后半段**说得出什么（票 133）。
///
/// **三种各是一个 case**（判据 12）：它们在页面上是三句不同的话，压成一个字符串
/// （或者一个 option）之后，「这一席没有档位」与「这份牌谱没记档位」就分不开了——
/// 而前者是**这一席本来就不走 prompt**，后者是**记录缺了一样**。
[<RequireQualifiedAccess>]
type ScorecardTier =
    /// 这一席此刻拨在哪一档。**只有 Live 那一桌答得出**，而且与名牌那半句同源
    /// （`SeatingPlan.tiers`，不许第二份判据）。
    | Set of said: string
    /// **这一席没有档位**：自带 bot 与强 AI 基线不走 prompt，引的档案被删了的那一席
    /// 真的退回了 bot——写上去只会让人以为它在生效（与名牌同一条判据）。
    | NotApplicable
    /// **牌谱没记**（回放那一屏）：`ScaffoldTier` 牌谱里一个字都没有，
    /// 而它是本机配桌的事。不许留白、不许猜。
    | Unrecorded

/// 记分卡上「选手 · 档」那一格**说得出什么**（票 133；边界由调度器在票外松过一次）。
///
/// **身份与档位是两件事，各自可缺**：牌谱**记得下身份**（`start_game` 的 `names`，
/// 恒是 `provider/model`，回放那一屏的名牌画的就是它），**记不下档位**。
/// 因此「回放：有身份没档位」与「老牌谱：连身份都没有」在页面上是两句不同的话，
/// 各是一个 case（判据 12）。
[<RequireQualifiedAccess>]
type ScorecardPlayer =
    /// 牌谱记下了这一席是谁（`start_game.names`）。档位见 `tier`。
    | Named of name: string * tier: ScorecardTier
    /// **连身份都没有**：v1 老牌谱，或那一列 `names` 短了一截 / 那一格是空串。
    /// 留白会被读成「这一席是 bot」——那是句假话，而假话比空着更贵。
    | Unknown

/// 「选手 · 档」那一格的三个出口。
[<RequireQualifiedAccess>]
module ScorecardPlayer =

    /// 给机器看的那一半（`data-player-source`）：**闸门读它**，四态各一个值。
    /// 措辞怎么改都不动它——那正是它存在的理由（判据 24）。
    let toWire (player: ScorecardPlayer) : string =
        match player with
        | ScorecardPlayer.Named(_, ScorecardTier.Set _) -> "tiered"
        | ScorecardPlayer.Named(_, ScorecardTier.NotApplicable) -> "no-tier"
        | ScorecardPlayer.Named(_, ScorecardTier.Unrecorded) -> "tier-unrecorded"
        | ScorecardPlayer.Unknown -> "unrecorded"

    /// **身份那半格**（`data-player-name`）：回放那一屏它与名牌上那一句**逐字相同**
    /// ——两处画的都是 `start_game` 那一列 `names`。身份也没有时是空串。
    let nameSaid (player: ScorecardPlayer) : string =
        match player with
        | ScorecardPlayer.Named(name, _) -> name
        | ScorecardPlayer.Unknown -> ""

    /// **档位那半格**（`data-player-tier`）。这一席没有档位时是空串。
    let tierSaid (player: ScorecardPlayer) : string =
        match player with
        | ScorecardPlayer.Named(_, ScorecardTier.Set said) -> said
        | ScorecardPlayer.Named(_, ScorecardTier.Unrecorded) -> "档位牌谱没记"
        | ScorecardPlayer.Named(_, ScorecardTier.NotApplicable)
        | ScorecardPlayer.Unknown -> ""

    /// **渲染层的单向出口**（ADR-0001）：人读的那句话。
    let toDisplay (player: ScorecardPlayer) : string =
        match player with
        | ScorecardPlayer.Unknown -> "牌谱没记"
        | ScorecardPlayer.Named(name, _) ->
            match tierSaid player with
            | "" -> name
            | tier -> $"{name}・{tier}"

/// 终局记分卡上的**一行**（票 133）：一席一行，四家逐列可比。
///
/// **它是三处的汇合，不是第四份数**：
///
/// - 席位来自牌桌那个投影（`BoardView.Seats`）；**只取座位号，不取风**（票 145）；
/// - 顺位 · 终点来自终局精算（`GameResult`，`Board.final` 已经取好）；
/// - 和 · 铳 / 兜底 / 重试 / tok 来自引擎那份逐席聚合（`Scorecard.tally`，判据 11）；
/// - **选手 · 档**：身份来自牌谱开头那一列 `names`（回放的名牌画的就是它），
///   档位来自名牌那半句（`SeatingPlan.tiers`）——两半都不许第二份判据，见
///   `TableState.scorecardPlayers`。
type ScorecardRow = {
    /// 这一行是哪一席。
    ///
    /// **这一行只认座位号，不带风**（票 145）：风每一局都在转，而这张表是**整场**的结论。
    /// 133 那一版在这里还带着末局自风，于是东风战打完之后最左那一格写着「座位 0 南」
    /// ——而座位 0 是起家、开局的东家。**要方位的人看牌桌，不看记分卡。**
    Seat: Seat
    /// 「选手 · 档」那一格：身份取牌谱的 `start_game.names`，档位只有 Live 答得出。
    Player: ScorecardPlayer
    /// 顺位，1 起。
    Juni: int
    /// 精算后的终点。
    Score: int
    /// 牌谱本身答得出的那几格。
    Tally: SeatTally
}

/// 记分卡的装配与它那段纯文本。
///
/// **这一层不算任何一个数**：算数的在 `Scorecard`（引擎），这里只把三处摆成一张表，
/// 再把同一张表写成一段能贴进 issue 的文字。
[<RequireQualifiedAccess>]
module ScorecardView =

    // ---- 装配 ----

    /// 摆出四行。**按座位升序**，与牌桌上那几个 `seat-N` 钩子同一个次序。
    ///
    /// 四个列表都按座位升序（`BoardView.Seats` / `GameResult` 的两列 / `Scorecard.tally`），
    /// 按位置对齐；某一处短了就少几行，**不拿默认值凑**——凑出来的行是一句假话。
    let rows
        (seats: SeatView list)
        (players: ScorecardPlayer list)
        (result: GameResult)
        (tallies: SeatTally list)
        : ScorecardRow list =
        let rowOf (index: int) (view: SeatView) : ScorecardRow option =
            match
                List.tryItem index players,
                List.tryItem index result.Juni,
                List.tryItem index result.Scores,
                List.tryItem index tallies
            with
            | Some player, Some juni, Some score, Some tally ->
                Some {
                    Seat = view.Seat
                    Player = player
                    Juni = juni
                    Score = score
                    Tally = tally
                }
            | _ -> None

        seats |> List.mapi rowOf |> List.choose id

    // ---- 账单上那笔对不上的差额（票 108/110） ----

    /// 这张表**没算进去**的那笔 token：整桌账单减掉四行相加。
    ///
    /// **它恒 ≥ 0，而且恰好是那几次「花了钱、没落子」的问话**（`Table.paidVoids`）：
    /// 记分卡每一格都是**牌谱**的聚合，而作废掉的问话不在牌谱里（裁决 110）。
    /// 页面拿它把差额当场说出来——同一屏上不许两个 tok 数并排站着不解释（票 39）。
    ///
    /// **它在这里而不是在视图里**：写成视图里的一句话，就没有任何东西执行得了
    /// 「四行相加 ≤ 账单行」这条不变量（判据 2）；摆成纯函数，dotnet 侧的用例才钉得住。
    let voidedGap (total: Usage) (rows: ScorecardRow list) : Usage =
        let listed = rows |> List.map (fun row -> row.Tally) |> Scorecard.totalUsage

        {
            Input = total.Input - listed.Input
            Output = total.Output - listed.Output
            CacheRead = total.CacheRead - listed.CacheRead
            CacheWrite = total.CacheWrite - listed.CacheWrite
        }

    // ---- 渲染层的单向出口（ADR-0001） ----

    /// 「席位」那一格。**一个风字都没有**（票 145）：这张表的四行是**四个选手**，
    /// 不是四个方位；而它是要被贴出去的东西，贴出去之后没人能纠正。
    let seatSaid (row: ScorecardRow) : string = $"座位 {Seat.index row.Seat}"

    /// 「顺位 · 终点」那一格。
    let placeSaid (row: ScorecardRow) : string = $"{row.Juni} 位 · {row.Score}"

    /// 「和 · 铳」那一格。
    let horaSaid (row: ScorecardRow) : string =
        $"{row.Tally.Hora} · {row.Tally.HoraTargeted}"

    /// 「输入 · 输出 tok」那一格。**输入侧走 `Usage.promptTokens`**（付全价的 + 命中缓存的
    /// + 写缓存的），与牌桌上那条账单行同一口径——同一屏上不许有两种 tok 的算法。
    let tokenSaid (row: ScorecardRow) : string =
        $"{Usage.promptTokens row.Tally.Usage} · {row.Tally.Usage.Output}"

    /// 那句「另有几次问话花了钱、没落子」。**只在真有那几笔时才说**（`counted = 0` 时不画）。
    let voidedSaid (counted: int) (gap: Usage) : string =
        $"另有 {counted} 次问话花了钱、没落子（{Usage.promptTokens gap} 输入 / {gap.Output} 输出 tok）"
        + "——它们不在牌谱里，因此也不在这张表里；牌桌那条账单行报的是花掉的总额。"

    /// 表头那几格，与 `cells` 一一对应。
    let headers: string list = [ "席位"; "选手 · 档"; "顺位 · 终点"; "和 · 铳"; "兜底"; "重试"; "输入 · 输出 tok" ]

    /// 一行那几格，与 `headers` 一一对应。**纯文本与 DOM 读的是同一份**：
    /// 两处各拼一遍就会漂，而「复制出来的那段与屏幕上那张表说的是同一件事」正是这一票要的。
    let cells (row: ScorecardRow) : string list = [
        seatSaid row
        ScorecardPlayer.toDisplay row.Player
        placeSaid row
        horaSaid row
        string row.Tally.Fallbacks
        string row.Tally.Retries
        tokenSaid row
    ]

    /// 整张表的那一段纯文本（贴 issue / 贴群那种）。**Markdown 的表**：
    /// issue 里渲染成表，聊天窗里退化成对得齐的几行，两处都读得下去。
    ///
    /// **key 不可能出现在里面**：每一格都来自牌谱、终局精算与名牌，
    /// 而这三样里没有一样装得下 key（`Roster.playerName` 那条注释）。
    /// 闸门照票 34 那道检查的形状守着这一句。
    let toText (rows: ScorecardRow list) : string =
        let line (values: string list) =
            "| " + String.concat " | " values + " |"

        [ "janpo 记分卡"; line headers; line (headers |> List.map (fun _ -> "---")) ]
        @ (rows |> List.map (cells >> line))
        |> String.concat "\n"
