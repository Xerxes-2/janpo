namespace Janpo

/// 跑批（soak）数到的一种事实：mjai 的事件类型，流局再按形态细分。
///
/// **它是覆盖率的计量单位**。跑批只证明「没崩 + 自洽」，而「跑到过」得由它来证——
/// 一批种子跑完之后每种至少出现一次，否则那句「随机 Player 覆盖全部动作类型」
/// 就只是个口号（备注 N-8 的教训：没断言过覆盖率的跑批只证明了没崩）。
///
/// `Coverage.ofEvent` 是**穷尽的 match**：`Event` 加 case 时编译器会在这里顶出来。
[<RequireQualifiedAccess>]
type Coverage =
    | StartGame
    | StartKyoku
    | Tsumo
    | Dahai
    | Pon
    | Chi
    | Ankan
    | Kakan
    | Minkan
    | Dora
    | Riichi
    | RiichiAccepted
    | Hora
    /// 流局按形态各计各的：`RyuukyokuReason` 的七种在覆盖率上是七件不同的事。
    | Ryuukyoku of reason: RyuukyokuReason
    | EndKyoku
    | EndGame

/// 跑批在一手上发现的问题。**是值，不是异常**——跑批要把全部问题收齐再报，
/// 不是遇到第一条就炸；而且每一条都得说得出怎么复现（见 `SoakIssue.Seed`）。
[<RequireQualifiedAccess>]
type SoakFault =
    /// 开不了局。
    | CannotStart of start: KyokuStartError
    /// **合法动作集里的动作被 `step` 拒了**：动作集与执行两处对不上。
    | Rejected of action: Action * illegal: IllegalAction
    /// 这一局没终，合法动作集却是空的——局面推不动了。
    | Stuck
    /// 手数或局数超过上限：跑不完（死循环或连庄停不下来）。
    | Runaway of counted: int * limit: int
    /// 牌数不守恒：暗牌 + 河 + 副露里自家出的那几张 + 山，张数与整副牌对不上。
    | TileCount of counted: int * expected: int
    /// 张数对得上，但牌不是那一副（多重集不符）。
    | TileMultiset
    /// 四家点数与供托之和变了。
    | ScoreTotal of counted: int * expected: int
    /// 同一串动作重放不出同一局面或同一事件流。
    | ReplayDiverged

/// 跑批里的一条问题，**必带种子**：跑批的命根子是能复现。
type SoakIssue =
    {
        /// 复现这一条要的种子：`janpo soak <seed> <seed>` 重跑的就是这一场。
        Seed: int
        /// 这一场里的第几局，1 起。
        Kyoku: int
        /// 这一局里的第几手，1 起；不属于某一手的问题记 0。
        Turn: int
        Fault: SoakFault
    }

/// 一批种子跑下来的结果：规模、覆盖率与问题清单。
type SoakReport =
    {
        /// 跑了哪几个种子，按给定顺序。
        Seeds: int list
        /// 一共打了几局（连庄的每一本场各算一局）。
        Kyokus: int
        /// 一共提交了多少手（CONTEXT.md 的 Turn）。
        Turns: int
        /// 每种事实出现了几次。**没出现的牌种压根不在表里**（次数恒 ≥ 1）。
        Counts: Map<Coverage, int>
        /// 出的问题，按发现顺序。空表示这批种子零异常。
        Issues: SoakIssue list
    }

/// 覆盖率计量单位的构造与渲染。
[<RequireQualifiedAccess>]
module Coverage =

    // ---- 构造 ----

    /// 事件对应的计量单位。**穷尽的 match**：`Event` 加 case 时这里编译不过。
    let ofEvent (event: Event) : Coverage =
        match event with
        | Event.StartGame _ -> Coverage.StartGame
        | Event.StartKyoku _ -> Coverage.StartKyoku
        | Event.Tsumo _ -> Coverage.Tsumo
        | Event.Dahai _ -> Coverage.Dahai
        | Event.Pon _ -> Coverage.Pon
        | Event.Chi _ -> Coverage.Chi
        | Event.Ankan _ -> Coverage.Ankan
        | Event.Kakan _ -> Coverage.Kakan
        | Event.Minkan _ -> Coverage.Minkan
        | Event.Dora _ -> Coverage.Dora
        | Event.Riichi _ -> Coverage.Riichi
        | Event.RiichiAccepted _ -> Coverage.RiichiAccepted
        | Event.Hora _ -> Coverage.Hora
        | Event.Ryuukyoku result -> Coverage.Ryuukyoku result.Reason
        | Event.EndKyoku -> Coverage.EndKyoku
        | Event.EndGame -> Coverage.EndGame

    // ---- mjai 记法 ----

    /// 报告里的名字：**就是 mjai 的事件类型**（`daiminkan` / `reach` 那一套，不是罗马字
    /// 标识符），流局再缀上 `:<reason>`。跑批的输出因此与牌谱对得上。
    let toMjai (coverage: Coverage) : string =
        match coverage with
        | Coverage.StartGame -> "start_game"
        | Coverage.StartKyoku -> "start_kyoku"
        | Coverage.Tsumo -> "tsumo"
        | Coverage.Dahai -> "dahai"
        | Coverage.Pon -> "pon"
        | Coverage.Chi -> "chi"
        | Coverage.Ankan -> "ankan"
        | Coverage.Kakan -> "kakan"
        | Coverage.Minkan -> "daiminkan"
        | Coverage.Dora -> "dora"
        | Coverage.Riichi -> "reach"
        | Coverage.RiichiAccepted -> "reach_accepted"
        | Coverage.Hora -> "hora"
        | Coverage.Ryuukyoku reason -> "ryukyoku:" + RyuukyokuReason.toMjai reason
        | Coverage.EndKyoku -> "end_kyoku"
        | Coverage.EndGame -> "end_game"

/// 跑批：给一批种子，让四个随机选手把每一场东风战打完，逐手验不变量、逐场数覆盖率。
///
/// **它证明的是「不崩 + 自洽」，不是正确性**（备注 N-23）：13 票修的那个振听 bug
/// 表现为「引擎默默不给荣和」，跑批只会看到和了偏少，看不出错。正确性靠真实牌谱对拍，
/// 两者互补——牌谱一次走到 32 种役、双响、抢杠与役满，那些随机选手走不到；
/// 跑批走到的是随机探索下的不变量。
[<RequireQualifiedAccess>]
module Soak =

    // ---- 覆盖率的期望 ----

    /// **跑批断言「至少出现一次」的那些**。默认规模（见 `Soak.defaultGames`）跑下来
    /// 每一种都有富余，因此它是回归闸门而不是运气。
    ///
    /// **`start_game` 不在其中**：它的 `names` 来自配桌，不是引擎算得出来的事件（02 票的决定），
    /// 跑批压根产不出它。数一条自己造的事件就是「假装覆盖了」。
    ///
    /// **`Soak.rare` 那几种流局形态也不在其中**，理由见那里。
    let required: Coverage list =
        [
            Coverage.StartKyoku
            Coverage.Tsumo
            Coverage.Dahai
            Coverage.Pon
            Coverage.Chi
            Coverage.Ankan
            Coverage.Kakan
            Coverage.Minkan
            Coverage.Dora
            Coverage.Riichi
            Coverage.RiichiAccepted
            Coverage.Hora
            Coverage.Ryuukyoku Fanpai
            Coverage.Ryuukyoku KyuushuKyuuhai
            Coverage.EndKyoku
            Coverage.EndGame
        ]

    /// 跑批里**稀到不该当闸门**的那几种流局形态。断言它们等于自找一种噪声式的红：
    /// 任何一处改动挪动了随机流，稀有事件就可能从 1 次变成 0 次。
    ///
    /// - **四杠散了**：默认规模上碰得到（60 场 2 次、1500 场 9 次），但两次不足以当闸门；
    /// - **四风连打 / 四家立直 / 三家和了 / 流し満貫**：1500 场 7536 局 63 万手跑批里
    ///   **一次都没碰到**。它们各要一组联合条件（四家都立直成立、三家听同一张、
    ///   第一巡四张同风且无人鸣、一家整局只打幺九牌且一张不被鸣），随机探索走不到。
    ///   它们的覆盖靠 12 票摊好的剧本用例，那才是它们该待的地方。
    ///   （四家立直是个例外：把 `Riichi` 权重推到极端、几乎不鸣牌时，400 场 1993 局里
    ///   出了 3 次——引擎那条路走得通，只是覆盖型偏好的立直密度到不了。）
    ///
    /// 数字与推导见 14 票的报告。`Soak.uncovered` 会把这一批里没走到的如实列出来，
    /// 因此「没覆盖」是一句显式的话而不是一处沉默。
    let rare: Coverage list =
        [
            Coverage.Ryuukyoku Suukaikan
            Coverage.Ryuukyoku SuufonRenda
            Coverage.Ryuukyoku SuuchaRiichi
            Coverage.Ryuukyoku SanchaHora
            Coverage.Ryuukyoku NagashiMangan
        ]

    /// 跑批**可能**走到的全部形态：断言的那些加上稀有的那些。
    /// `RyuukyokuReason` 的七种在这里必须一种不缺（有条用例钉着）。
    /// （`start_game` 不在其中，理由见 `required`。）
    let all: Coverage list = required @ rare

    // ---- 规模 ----

    /// CI 里跑几场。**规模可配置**（CLI 收种子区间，测试读环境变量 `JANPO_SOAK_GAMES`），
    /// 这个数字只是默认值：实测每场约 0.16 秒，60 场约 10 秒 CPU（16 核上落在属性测试的
    /// 阴影里，wall-clock 只多约 1 秒；核少的 runner 上就是实打实的 10 秒），
    /// 而覆盖率在这个规模上每一种都有富余。夜里想跑大的就把种子区间放大。
    let defaultGames = 60

    // ---- 内部：不变量 ----

    /// 跑批给选手的发生器。**与牌山那一条流分开**：同一种子跑出同一串数，
    /// 共用一条流的话选牌与洗牌就相关了。
    ///
    /// **它是公开的，因为复现要用它**：
    /// `Game.run player (Soak.playerRng seed) (Rng.ofSeed seed) (Game.start ruleset)`
    /// 跑出来的就是跑批在这个种子上看到的那一场（有用例钉着）——
    /// 跑批报出问题时，拿它把完整事件流打出来。
    let playerRng (seed: int) : Rng = Rng.ofSeed (seed + 9973)

    /// 一局最多几手。上限取「可摸区张数 × 座位数」：每次打牌至多问其余三家一轮，
    /// 因此真实手数比它小一个量级，够宽松到只在真死循环时才响。
    let private turnLimit (ruleset: Ruleset) : int =
        Ruleset.wallSize ruleset * ruleset.SeatCount

    /// 一场最多几局。连庄不进局，因此局数原则上没有上界（东 1 局可以一直连下去）；
    /// 取局数序列的 25 倍，同样是「只在跑不完时才响」的量级。
    let private kyokuLimit (ruleset: Ruleset) : int =
        List.length (Ruleset.kyokus ruleset) * 25

    /// 局面里的全部牌：暗牌、河、副露里**自家出的**那几张，加上山。
    /// 副露里来自他家河的那张仍算在打牌者的河里，因此只数 `Naki.fromHand`，不然重复数一张。
    let private tilesIn (state: GameState) : Tile list =
        (GameState.players state
         |> List.collect (fun player ->
             PlayerState.hand player
             @ PlayerState.kawa player
             @ (PlayerState.naki player |> List.collect Naki.fromHand)))
        @ Wall.tiles (GameState.wall state)

    /// 四家点数与供托之和。**一场对局里任何一刻它都该等于起手总点**：
    /// 立直棒是从点数里扣到供托上的、和了是从供托挪进和了者点数里的、
    /// 听牌料与本场在四家之间转手，三处都只换位置不增减。
    ///
    /// 拿起手总点当基准而不是拿「这一局开局时的总额」：后者在局与局之间漏了一笔也看不出来。
    let private wealthIn (ruleset: Ruleset) (state: GameState) : int =
        List.sum (GameState.scores state)
        + Ruleset.kyotakuScore ruleset (GameState.kyotaku state)

    /// 一手之前该成立的全部不变量。空表示这一刻自洽。
    let private faultsIn (ruleset: Ruleset) (deck: Tile list) (wealth: int) (state: GameState) : SoakFault list =
        let tiles = tilesIn state

        let tileFaults =
            if List.length tiles <> List.length deck then
                [ SoakFault.TileCount(List.length tiles, List.length deck) ]
            elif Tile.sort tiles <> deck then
                [ SoakFault.TileMultiset ]
            else
                []

        let counted = wealthIn ruleset state

        if counted = wealth then
            tileFaults
        else
            tileFaults @ [ SoakFault.ScoreTotal(counted, wealth) ]

    // ---- 内部：一局 ----

    /// 一局跑下来的结果：终局的局面、走过的手数、提交过的动作与发现的问题。
    type private KyokuSoak =
        {
            Final: GameState
            Rng: Rng
            Turns: int
            /// 逐手提交过的动作，按顺序。回放确定性这条不变量要它。
            Actions: Action list
            /// `(第几手, 问题)`，按发现顺序。
            Faults: (int * SoakFault) list
        }

    /// 把一局跑到终：每一手先验不变量，再让选手挑一个动作、提交给引擎。
    ///
    /// **不走 `Kyoku.run`**：跑批要在每一手上插手（验不变量、记动作、数手数、防卡死），
    /// 那是驱动之外的事，塞进 `Kyoku.run` 会让正常路径为跑批背一层回调。
    let private playKyoku (ruleset: Ruleset) (player: Player<Rng>) (rng: Rng) (start: GameState) : KyokuSoak =
        let deck = Tile.sort (Ruleset.wallTiles ruleset)
        let wealth = Ruleset.startingTotal ruleset
        let limit = turnLimit ruleset

        let rec loop (rng: Rng) (state: GameState) (turn: int) (actions: Action list) (faults: (int * SoakFault) list) =
            let faults =
                faultsIn ruleset deck wealth state
                |> List.map (fun fault -> turn, fault)
                |> List.append faults

            // 手数就是**提交成功的动作条数**：`turn` 是「正要提交第几手」，因此比它少一。
            let stopAt (rng: Rng) (extra: (int * SoakFault) list) =
                {
                    Final = state
                    Rng = rng
                    Turns = turn - 1
                    Actions = List.rev actions
                    Faults = faults @ extra
                }

            if turn > limit then
                stopAt rng [ turn, SoakFault.Runaway(turn, limit) ]
            else
                match GameState.legalActions state with
                | [] ->
                    if GameState.isEnded state then
                        stopAt rng []
                    else
                        stopAt rng [ turn, SoakFault.Stuck ]
                | choice :: _ ->
                    let action, advanced = player rng state choice

                    match GameState.step state action with
                    | Error illegal -> stopAt advanced [ turn, SoakFault.Rejected(action, illegal) ]
                    | Ok(next, _) -> loop advanced next (turn + 1) (action :: actions) faults

        loop rng start 1 [] []

    /// 回放确定性：把这一局提交过的动作从**同一个开局局面**重新走一遍，
    /// 应当得到同一个局面与同一串事件。局面是不可变值，因此开局那一份原样还在手里。
    let private replayFaults (start: GameState) (result: KyokuSoak) : (int * SoakFault) list =
        let replayed =
            (Ok start, result.Actions)
            ||> List.fold (fun current action ->
                current
                |> Result.bind (fun state -> GameState.step state action |> Result.map fst))

        match replayed with
        | Ok state when state = result.Final && GameState.events state = GameState.events result.Final -> []
        | Ok _
        | Error _ -> [ result.Turns, SoakFault.ReplayDiverged ]

    // ---- 内部：一场 ----

    /// 一场对局跑下来的结果。
    type private GameSoak =
        {
            Kyokus: int
            Turns: int
            Counts: Map<Coverage, int>
            Issues: SoakIssue list
        }

    let private tally (counts: Map<Coverage, int>) (events: Event list) : Map<Coverage, int> =
        (counts, events)
        ||> List.fold (fun counts event ->
            let coverage = Coverage.ofEvent event
            let seen = counts |> Map.tryFind coverage |> Option.defaultValue 0
            Map.add coverage (seen + 1) counts)

    /// 把一场东风战跑完。四个座位共用同一个选手实现（M0 的验收就是「四个随机 Player」），
    /// 局与局之间的推进走 `Game.advance`，与 `Game.run` 同一条路。
    let private playGame (ruleset: Ruleset) (player: Player<Rng>) (seed: int) : GameSoak =
        let limit = kyokuLimit ruleset

        let rec loop (game: Game) (current: Rng) (rng: Rng) (soak: GameSoak) =
            let issue (kyoku: int) (turn: int) (fault: SoakFault) =
                {
                    Seed = seed
                    Kyoku = kyoku
                    Turn = turn
                    Fault = fault
                }

            let kyoku = soak.Kyokus + 1

            match Game.progress game with
            | GameProgress.Ended _ ->
                // 终局精算后再验一次：剩下的供托归头名，只换归属不凭空增减。
                let wealth =
                    List.sum (Game.scores game) + Ruleset.kyotakuScore ruleset (Game.kyotaku game)

                let expected = Ruleset.startingTotal ruleset

                { soak with
                    Counts = tally soak.Counts (Game.events game)
                    Issues =
                        if wealth = expected then
                            soak.Issues
                        else
                            soak.Issues @ [ issue soak.Kyokus 0 (SoakFault.ScoreTotal(wealth, expected)) ]
                }
            | GameProgress.NextKyoku _ when kyoku > limit ->
                { soak with
                    Counts = tally soak.Counts (Game.events game)
                    Issues = soak.Issues @ [ issue kyoku 0 (SoakFault.Runaway(kyoku, limit)) ]
                }
            | GameProgress.NextKyoku context ->
                match GameState.start ruleset context rng with
                | Error error ->
                    { soak with
                        Counts = tally soak.Counts (Game.events game)
                        Issues = soak.Issues @ [ issue kyoku 0 (SoakFault.CannotStart error) ]
                    }
                | Ok(start, advancedRng) ->
                    let result = playKyoku ruleset player current start

                    let faults =
                        result.Faults @ replayFaults start result
                        |> List.map (fun (turn, fault) -> issue kyoku turn fault)

                    let played =
                        { soak with
                            Kyokus = kyoku
                            Turns = soak.Turns + result.Turns
                            Issues = soak.Issues @ faults
                        }

                    // 这一局没终就别往下打：局面推不动了，再开一局只会把同一条问题刷满屏。
                    if GameState.isEnded result.Final then
                        loop (Game.advance result.Final game) result.Rng advancedRng played
                    else
                        { played with
                            Counts = tally played.Counts (Game.events game @ GameState.events result.Final)
                        }

        let empty =
            {
                Kyokus = 0
                Turns = 0
                Counts = Map.empty
                Issues = []
            }

        loop (Game.start ruleset) (playerRng seed) (Rng.ofSeed seed) empty

    // ---- 跑批 ----

    /// 给一批种子，逐场跑完并汇总。**一个种子跑不下去不影响别的种子**：
    /// 问题进清单，跑批继续——一次跑批要把能发现的都发现掉。
    let run (ruleset: Ruleset) (player: Player<Rng>) (seeds: int list) : SoakReport =
        let empty =
            {
                Seeds = seeds
                Kyokus = 0
                Turns = 0
                Counts = Map.empty
                Issues = []
            }

        (empty, seeds)
        ||> List.fold (fun report seed ->
            let game = playGame ruleset player seed

            { report with
                Kyokus = report.Kyokus + game.Kyokus
                Turns = report.Turns + game.Turns
                Counts =
                    (report.Counts, game.Counts)
                    ||> Map.fold (fun counts coverage count ->
                        let seen = counts |> Map.tryFind coverage |> Option.defaultValue 0
                        Map.add coverage (seen + count) counts)
                Issues = report.Issues @ game.Issues
            })

    // ---- 拆解 ----

    /// 某种事实出现了几次；一次都没有则为 0。
    let count (coverage: Coverage) (report: SoakReport) : int =
        report.Counts |> Map.tryFind coverage |> Option.defaultValue 0

    /// 全部出现过的事实与次数，按 mjai 名字升序。
    let frequencies (report: SoakReport) : (Coverage * int) list =
        report.Counts |> Map.toList |> List.sortBy (fst >> Coverage.toMjai)

    /// `required` 里一次都没出现的那些。**跑批的核心断言就是它为空**。
    let missing (report: SoakReport) : Coverage list =
        required |> List.filter (fun coverage -> count coverage report = 0)

    /// `all` 里一次都没出现的那些——含那几种随机碰不到的流局形态。
    /// 报告把它印出来，因此「没覆盖到」是一句显式的话。
    let uncovered (report: SoakReport) : Coverage list =
        all |> List.filter (fun coverage -> count coverage report = 0)

/// 跑批结果的渲染。
[<RequireQualifiedAccess>]
module SoakFault =

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：中文说明，只供 CLI 与报告使用。
    let toDisplay (fault: SoakFault) : string =
        match fault with
        | SoakFault.CannotStart start -> "开不了局：" + KyokuStartError.toDisplay start
        | SoakFault.Rejected(action, illegal) ->
            $"合法动作集里的 {Action.actor action |> Seat.index} 号座位的动作被拒：{IllegalAction.toDisplay illegal}"
        | SoakFault.Stuck -> "这一局没终，合法动作集却是空的"
        | SoakFault.Runaway(counted, limit) -> $"跑不完：{counted} 超过上限 {limit}"
        | SoakFault.TileCount(counted, expected) -> $"牌数不守恒：数到 {counted} 张，应为 {expected} 张"
        | SoakFault.TileMultiset -> "牌数对得上但不是那一副牌（多重集不符）"
        | SoakFault.ScoreTotal(counted, expected) -> $"点数与供托之和变了：{counted}，应为 {expected}"
        | SoakFault.ReplayDiverged -> "回放不确定：同一串动作重放出了另一个局面"

/// 一条问题的渲染。
[<RequireQualifiedAccess>]
module SoakIssue =

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：中文说明。种子排在最前——复现是跑批的命根子。
    let toDisplay (issue: SoakIssue) : string =
        $"种子 {issue.Seed} 第 {issue.Kyoku} 局第 {issue.Turn} 手：{SoakFault.toDisplay issue.Fault}"

/// 跑批报告的渲染。
[<RequireQualifiedAccess>]
module SoakReport =

    // ---- 渲染层出口（ADR-0001） ----

    /// 覆盖率那几行：`<mjai 名字>=<次数>`，按名字升序。
    let coverageLines (report: SoakReport) : string list =
        Soak.frequencies report
        |> List.map (fun (coverage, count) -> $"{Coverage.toMjai coverage}={count}")

    /// **渲染层的单向出口**：中文说明，只供 CLI 与报告使用。
    let toDisplay (report: SoakReport) : string =
        let uncovered =
            match Soak.uncovered report with
            | [] -> "无"
            | missing -> missing |> List.map Coverage.toMjai |> String.concat " "

        let issues =
            match report.Issues with
            | [] -> [ "问题: 无" ]
            | found -> "问题:" :: (found |> List.map (fun issue -> "  " + SoakIssue.toDisplay issue))

        [
            $"场数: {List.length report.Seeds}  局数: {report.Kyokus}  手数: {report.Turns}"
            "覆盖: " + String.concat " " (coverageLines report)
            "未覆盖: " + uncovered
        ]
        @ issues
        |> String.concat "\n"
