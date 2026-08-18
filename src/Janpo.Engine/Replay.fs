namespace Janpo

/// 回放推不下去的原因。**是值，不是异常**（与引擎其余部分同一风格）。
/// `kyoku` 是事件流里的第几段（0 起），定位用。
[<RequireQualifiedAccess>]
type ReplayError =
    /// 事件流里一局都没有：没有一条 `start_kyoku`，或仅有的那一条后面连 Oya 的第一次自摸
    /// 都不在（那样连开局那一刻都摆不出来，见本模块的 `kyoku`）。
    | NoKyoku
    /// 牌山重建不出来：事件流里露过面的牌凑不成这个规则集的一座山。
    | CannotBuildWall of kyoku: int * detail: string
    /// 开不出局（重建出来的牌山不合这个规则集）。
    | CannotStart of kyoku: int * error: KyokuStartError
    /// 引擎拒绝了事件流里的某个动作。**不该发生**：事件是既成事实。
    | Rejected of kyoku: int * illegal: IllegalAction
    /// 这一局的事件流读不下去了，后面却还有别的局。
    ///
    /// **最后一局没走完不在其列**：回放就是对事件流的**前缀**做 fold（ADR-0002），
    /// 分享一场还没打完的对局是常事。
    | Stranded of kyoku: int * detail: string

/// 一份事件流 fold 出来的东西：已经打完的那几局（收进了 `Game`）加上还没打完的那一局。
///
/// **形状与牌桌一样**（`Table` 也是 `Game` + `GameState`）不是巧合：回放出来的就是一桌对局，
/// M2 的导入回放直接拿它摆牌桌。
type Replayed =
    {
        /// 已经打完并收进这一场的那几局。
        Game: Game
        /// 还没打完的那一局；事件流正好在某一局收尾处结束时是 None。
        Current: GameState option
    }

/// 一局回放的**逐手轨迹**（票 71）：开局那一刻的局面，与之后按事件流逐条交回引擎的那些动作。
///
/// **它是 fold 的旁白，不是第二条路**：`Replay.game` 与 `Replay.trace` 走的是同一段 fold，
/// 因此照着 `Actions` 一条条 `GameState.step` 出来的局面与 `Replayed` 里那一份必然相同。
///
/// **为什么要它**：回放要一帧一帧摆上牌桌（首页的 Demo Paifu 自动播），而牌桌那一层要在
/// **宣言那一刻**捞得下役种（`GameState.horaOf`）、跟得上各座位的掩蔽流。
/// 只给它一份 `Replayed`（末尾那一刻）答不了这两件事，而把它们搬进引擎就多一份实现
/// ——交出动作序列之后，牌桌那一层用的是它与 Live **逐字同一条**的落子路径。
type ReplayKyoku =
    {
        /// 这一局开局那一刻的局面（牌山由事件流重建，不走随机流）。
        Opening: GameState
        /// 之后逐条交回引擎的动作，按提交顺序。
        ///
        /// **「过」（`Action.None`）也在里面**：它不产出事件，因此事件流里看不见，
        /// 而牌桌那一层的一手就是一次提交（Live 那边同理）——两边的「一手」因此是同一个粒度。
        Actions: Action list
    }

/// 回放产物的拆解。
[<RequireQualifiedAccess>]
module Replayed =

    /// 回放出来的事件流。**它恒是喂进去那份的前缀**（除了开头的 `start_game`
    /// ——那条的 `names` 来自配桌，不属于任何一局）：喂进去的那份完整时逐条相同，
    /// 而末尾截断的那份可能短一截——回放宁可少走一步，也不产出牌谱交代不了的事件
    /// （票 85，见 `Driving.Backing`）。
    let events (replayed: Replayed) : Event list =
        Game.events replayed.Game
        @ (replayed.Current |> Option.map GameState.events |> Option.defaultValue [])

    /// 终局精算；这一场还没走完则为 None。
    let result (replayed: Replayed) : GameResult option = Game.result replayed.Game

/// 回放的诊断。
[<RequireQualifiedAccess>]
module ReplayError =

    /// **渲染层的单向出口**（ADR-0001）：中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: ReplayError) : string =
        match error with
        | ReplayError.NoKyoku -> "这份事件流里一局都没有"
        | ReplayError.CannotBuildWall(kyoku, detail) -> $"第 {kyoku} 局的牌山重建不出来：{detail}"
        | ReplayError.CannotStart(kyoku, error) -> $"第 {kyoku} 局开不出来：{KyokuStartError.toDisplay error}"
        | ReplayError.Rejected(kyoku, illegal) -> $"第 {kyoku} 局：引擎拒绝了事件流里的动作（{IllegalAction.toDisplay illegal}）"
        | ReplayError.Stranded(kyoku, detail) -> $"第 {kyoku} 局：{detail}"

/// 回放（CONTEXT.md 的 Replay）：对 Paifu 事件流做 fold 得到局面。
///
/// **回放不是另一套代码路径，就是引擎本身**（ADR-0002）：这里做的全部事情是
/// ① 按事件流把牌山摆回原样、② 把事件流里的动作原样交回 `GameState.step`。
/// 符、点数、连庄、终局精算一律由引擎重算——本模块一条规则都不自己判，
/// 因此「导出的牌谱回放出同一个终局」不靠两份实现对齐，靠的是只有一份实现。
///
/// 事件流里没有的两样东西由这里补回来：
/// - 「过」（`Action.None`）不产出事件，因此没被宣言的座位一律交「过」；
/// - 三家和了那三家的荣和宣言也不产出 `hora`（一家都没成立），宣言者记在
///   `ryukyoku` 的 `tenpais` 上（`Ryuukyoku.revealedBy`），照它交回去。
///
/// **反过来，事件流没交代的东西一样都不能造**（票 85）：末尾截断的事件流停在
/// 「下一步该摸牌了」那种相位上时，引擎会从 `buildWall` 推断出来的牌山里自己摸一张
/// （摸牌不是 `Action`）——那一张牌谱里根本没有。因此本模块记着一本账（`Driving.Backing`）：
/// **引擎产出的事件一条都不允许超出事件流**，超出的那一步整步退掉。
/// 于是回放交出来的局面恒是喂进去那份事件流某个**前缀**的 fold，
/// 手里的每一张都在事件流里找得到出处（起手或某次自摸）。
[<RequireQualifiedAccess>]
module Replay =

    // ---- 分段 ----

    /// 这条事件是一局的边界吗（`start_kyoku` / `end_kyoku`）。
    let private isBoundary (event: Event) : bool =
        match event with
        | StartKyoku _
        | EndKyoku -> true
        | _ -> false

    /// 事件流 → 一局一段：`start_kyoku` 的载荷，加上它之后到这一局收尾为止的那些事件。
    /// `start_game` / `end_game` 不属于任何一局，丢掉。
    let rec private kyokus (events: Event list) : (StartKyoku * Event list) list =
        match events |> List.skipWhile (isBoundary >> not) with
        | StartKyoku start :: rest ->
            let moves = rest |> List.takeWhile (isBoundary >> not)
            (start, moves) :: kyokus (List.skip (List.length moves) rest)
        | _ :: rest -> kyokus rest
        | [] -> []

    // ---- 牌山的重建 ----

    /// 从一列牌里拿掉第一张与 `tile` 相同的；没有就原样返回。
    let private removeOne (tile: Tile) (tiles: Tile list) : Tile list =
        match List.tryFindIndex ((=) tile) tiles with
        | Some index -> List.take index tiles @ List.skip (index + 1) tiles
        | None -> tiles

    /// 配牌的取牌手顺（4-4-4-1），与 `Wall.deal` 同一份，只是反过来用：
    /// 已知四家的配牌，倒推它们在牌山里的先后。
    let private haipaiOrder (ruleset: Ruleset) (oya: Seat) (tehais: Tile list list) : Tile list =
        let rounds = ruleset.HaipaiSize / 4
        let rest = ruleset.HaipaiSize % 4
        let chunks = [ for _ in 1..rounds -> 4 ] @ (if rest > 0 then [ rest ] else [])

        let plan =
            [
                for chunk in chunks do
                    for seat in Seat.orderFrom ruleset oya -> seat, chunk
            ]

        (([], tehais), plan)
        ||> List.fold (fun (taken, remaining) (seat, chunk) ->
            let hand = Seat.tryItem seat remaining |> Option.defaultValue []
            let size = min chunk (List.length hand)

            taken @ List.truncate size hand, remaining |> Seat.mapAt seat (fun _ -> List.skip size hand))
        |> fst

    /// 摸进的牌分两路：从可摸区摸的，与杠之后从王牌补摸的岭上牌。
    /// **紧跟在杠后面的那次自摸是岭上牌**；中间可能夹一条 `dora`（暗杠正是那个顺序）。
    let private drawnTiles (moves: Event list) : Tile list * Tile list =
        let step (live, rinshan, afterKan) event =
            match event with
            | Tsumo(_, pai) ->
                if afterKan then
                    live, rinshan @ [ pai ], false
                else
                    live @ [ pai ], rinshan, false
            // 翻宝牌不打断「杠 → 补摸」：暗杠的顺序就是 `ankan` → `dora` → `tsumo`。
            | Dora _ -> live, rinshan, afterKan
            | Ankan _
            | Kakan _
            | Minkan _ -> live, rinshan, true
            | StartGame _
            | StartKyoku _
            | Dahai _
            | Pon _
            | Chi _
            | Riichi _
            | RiichiAccepted _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | EndGame -> live, rinshan, false

        let live, rinshan, _ = moves |> List.fold step ([], [], false)
        live, rinshan

    /// 这一局露过面的表宝牌指示牌（开局那张加每次杠翻的那张）与里宝牌指示牌
    /// （只有和了者立了直才公开；双响时取最长的那条）。
    let private indicatorsIn (start: StartKyoku) (moves: Event list) : Tile list * Tile list =
        let dora =
            moves
            |> List.choose (fun event ->
                match event with
                | Dora marker -> Some marker
                | _ -> None)

        let ura =
            moves
            |> List.choose (fun event ->
                match event with
                | Hora hora -> Some hora.UraDoraMarkers
                | _ -> None)
            |> List.sortByDescending List.length
            |> List.tryHead
            |> Option.defaultValue []

        start.DoraMarker :: dora, ura

    /// 从一局的事件流重建牌山：配牌与摸牌按事件流摆好，表里宝牌指示牌按事件流摆进王牌，
    /// 其余位置由整副牌里**没露过面的**牌补满（那些牌这一局根本没被碰过，摆哪都一样）。
    ///
    /// 摊出来的一列牌交给 `Wall.ofOrdered`：可摸区在前、末尾 `DeadWallSize` 张是王牌，
    /// 王牌里岭上牌在前、其后每两张是一叠（表宝牌指示牌，里宝牌指示牌）。
    let private buildWall (ruleset: Ruleset) (start: StartKyoku) (moves: Event list) : Result<Wall, string> =
        let haipai = haipaiOrder ruleset start.Oya start.Tehais
        let live, rinshan = drawnTiles moves
        let dora, ura = indicatorsIn start moves
        let known = haipai @ live @ rinshan @ dora @ ura

        let filler =
            (Ruleset.wallTiles ruleset, known)
            ||> List.fold (fun rest tile -> removeOne tile rest)

        let liveSize = Ruleset.wallSize ruleset - ruleset.DeadWallSize
        let liveFillerCount = liveSize - List.length haipai - List.length live
        let rinshanFillerCount = ruleset.RinshanCount - List.length rinshan

        if List.length filler <> Ruleset.wallSize ruleset - List.length known then
            Error "事件流里露过面的牌凑不进整副牌（同一种牌最多四张）"
        elif liveFillerCount < 0 then
            Error $"事件流的摸牌数超出了可摸区（多 {-liveFillerCount} 张）"
        elif rinshanFillerCount < 0 then
            Error $"事件流的杠数超出了岭上牌（多 {-rinshanFillerCount} 次）"
        else
            let liveFiller, afterLive = List.splitAt liveFillerCount filler
            let rinshanFiller, indicatorFiller = List.splitAt rinshanFillerCount afterLive

            // 指示牌区按「表, 里」成叠排；事件流没公开的那几张用余牌顶上。
            let slots =
                [
                    for index in 0 .. (ruleset.DeadWallSize - ruleset.RinshanCount) / 2 - 1 do
                        yield List.tryItem index dora
                        yield List.tryItem index ura
                ]

            let indicators, _ =
                (([], indicatorFiller), slots)
                ||> List.fold (fun (placed, pool) slot ->
                    match slot, pool with
                    | Some tile, _ -> placed @ [ tile ], pool
                    | None, head :: rest -> placed @ [ head ], rest
                    | None, [] -> placed, [])

            let tiles = haipai @ live @ liveFiller @ rinshan @ rinshanFiller @ indicators

            if List.length tiles <> Ruleset.wallSize ruleset then
                Error $"重建出来的牌山有 {List.length tiles} 张，应当是 {Ruleset.wallSize ruleset} 张"
            else
                Ok(Wall.ofOrdered ruleset tiles)

    // ---- 重放 ----

    /// 重放过程中的游标：局面、还没喂进去的事件，与**已经交回去的那几个动作**（倒序）。
    ///
    /// `Played` 是票 71 加的一项**输出**：回放本来就逐条在提交动作，只是从前提完就丢。
    /// 倒序累加是为了每手 O(1)（一局百来手，`@` 会变成 O(n²)），出口处再 `List.rev`。
    type private Driving =
        {
            State: GameState
            Queue: Event list
            Played: Action list
            /// 这一局的事件流**还能给引擎背书几条事件**（票 85）。
            ///
            /// 开局那一刻是「事件流交代的条数 − 引擎开局产出的条数」，此后每走一步减掉
            /// `GameState.step` 这一步产出的条数。一份完整的牌谱里引擎产出的每一条都对得上
            /// 事件流里的一条，因此这本账只会减到 0；**减到负数就是这一步越过了牌谱**。
            ///
            /// **为什么会越过**：摸牌不是 `Action`——没人能鸣的相位引擎自己就摸一张
            /// （`applyDahai` → `afterDahai`，杠之后的岭上牌同理），而那一张取自
            /// `buildWall` 推断出来的牌山。事件流在这种相位上截断时，那一张是余牌里
            /// 凑数的一张，**牌谱里根本没有它**。
            Backing: int
        }

    /// 引擎自己产出的事件，不必提交：摸牌、翻宝牌、立直成立与三条 game / kyoku 级事件。
    let private isEngineProduced (event: Event) : bool =
        match event with
        | Tsumo _
        | Dora _
        | RiichiAccepted _
        | StartGame _
        | StartKyoku _
        | EndKyoku
        | EndGame -> true
        | Dahai _
        | Pon _
        | Chi _
        | Ankan _
        | Kakan _
        | Minkan _
        | Riichi _
        | Hora _
        | Ryuukyoku _ -> false

    /// 队列里下一条要喂进去的事件：引擎自己产出的先跳过。
    let private pendingMoves (queue: Event list) : Event list = List.skipWhile isEngineProduced queue

    /// 从队列里拿掉第一条与 `event` 相同的事件。
    let private removeFirst (event: Event) (queue: Event list) : Event list =
        match List.tryFindIndex ((=) event) queue with
        | Some index -> List.take index queue @ List.skip (index + 1) queue
        | None -> queue

    /// 把一个动作交给引擎。被拒绝就是回放到此为止——事件是既成事实，引擎不该拒绝它。
    ///
    /// **引擎这一步产出了几条事件就从账上减几条**（`Backing`）：那几条里可能夹着
    /// 一次引擎自己摸的牌，而截断的事件流交代不了它。
    let private submit (kyoku: int) (action: Action) (driving: Driving) : Result<Driving, ReplayError> =
        match GameState.step driving.State action with
        | Ok(next, produced) ->
            Ok
                { driving with
                    State = next
                    Played = action :: driving.Played
                    Backing = driving.Backing - List.length produced
                }
        | Error illegal -> Error(ReplayError.Rejected(kyoku, illegal))

    /// 这条事件是不是对当前这一轮响应的宣言。
    /// 抢杠那一轮只可能是荣和（`ResponseCause.Kan`：杠上鸣不了牌）。
    let private isResponseTo (waiting: AwaitingResponse) (event: Event) : bool =
        let toDiscard (target: Seat) (pai: Tile) =
            match waiting.Cause with
            | ResponseCause.Dahai -> target = waiting.Target && pai = waiting.Pai
            | ResponseCause.Kan _ -> false

        match event with
        | Hora hora -> hora.Target = waiting.Target && hora.Actor <> hora.Target
        | Pon(_, target, pai, _)
        | Chi(_, target, pai, _)
        | Minkan(_, target, pai, _) -> toDiscard target pai
        | StartGame _
        | StartKyoku _
        | Tsumo _
        | Dahai _
        | Ankan _
        | Kakan _
        | Dora _
        | Riichi _
        | RiichiAccepted _
        | Ryuukyoku _
        | EndKyoku
        | EndGame -> false

    /// 宣言这条事件的座位。**只有响应阶段那四种用得上**：其余事件要么不在响应阶段出现，
    /// 要么压根不是某家宣言的（`Event.actor` 引擎里没有这个函数，也不必为本模块加一个）。
    let private declarer (event: Event) : Seat option =
        match event with
        | Hora hora -> Some hora.Actor
        | Pon(actor, _, _, _)
        | Chi(actor, _, _, _)
        | Minkan(actor, _, _, _) -> Some actor
        | StartGame _
        | StartKyoku _
        | Tsumo _
        | Dahai _
        | Ankan _
        | Kakan _
        | Dora _
        | Riichi _
        | RiichiAccepted _
        | Ryuukyoku _
        | EndKyoku
        | EndGame -> None

    /// 三家和了时这一家宣言过荣和吗。那三家的宣言**在事件流里没有各自的 `hora`**
    /// （一家也没成立），只有一条 `ryukyoku`，宣言者记在它的 `tenpais` 上
    /// （`Ryuukyoku.revealedBy`：三家和了那一支记的正是宣言者）。
    let private declaredSanchaHora (seat: Seat) (queue: Event list) : bool =
        match pendingMoves queue with
        | Ryuukyoku result :: _ when result.Reason = SanchaHora ->
            Seat.tryItem seat result.Tenpais |> Option.defaultValue false
        | _ -> false

    /// 响应阶段的一步：按引擎等答复的顺序，逐座位交出事件流里的宣言，没宣言的交「过」。
    let private stepResponse
        (kyoku: int)
        (waiting: AwaitingResponse)
        (driving: Driving)
        : Result<Driving, ReplayError> =
        match waiting.Responses with
        // 走不到：响应阶段必然还等着至少一家。
        | [] -> Error(ReplayError.Stranded(kyoku, "引擎停在响应阶段却没有等答复的座位"))
        | pending :: _ ->
            let seat = pending.Seat

            let declared =
                pendingMoves driving.Queue
                |> List.takeWhile (isResponseTo waiting)
                |> List.tryFind (fun event -> declarer event = Some seat)

            match declared with
            | Some event ->
                let dropped =
                    { driving with
                        Queue = removeFirst event driving.Queue
                    }

                match event with
                | Hora hora -> submit kyoku (Action.Hora(hora.Actor, hora.Target, hora.Pai)) dropped
                | Pon(actor, target, pai, consumed) ->
                    submit kyoku (Action.Pon(actor, target, pai, Tile.sort consumed)) dropped
                | Chi(actor, target, pai, consumed) ->
                    submit kyoku (Action.Chi(actor, target, pai, Tile.sort consumed)) dropped
                | Minkan(actor, target, pai, consumed) ->
                    submit kyoku (Action.Minkan(actor, target, pai, Tile.sort consumed)) dropped
                // 走不到：`isResponseTo` 只放行上面那四种。
                | StartGame _
                | StartKyoku _
                | Tsumo _
                | Dahai _
                | Ankan _
                | Kakan _
                | Dora _
                | Riichi _
                | RiichiAccepted _
                | Ryuukyoku _
                | EndKyoku
                | EndGame -> Error(ReplayError.Stranded(kyoku, "响应阶段收到了不是响应的事件"))
            | None when declaredSanchaHora seat driving.Queue ->
                submit kyoku (Action.Hora(seat, waiting.Target, waiting.Pai)) driving
            // 没宣言就是「过」——它不产出事件，因此事件流里根本看不见它。
            | None -> submit kyoku (Action.None seat) driving

    /// 摸牌后阶段的一步：打牌、宣言立直、自家宣言的杠、自摸和或九种九牌。
    let private stepDahai (kyoku: int) (waiting: AwaitingDahai) (driving: Driving) : Result<Driving, ReplayError> =
        match pendingMoves driving.Queue with
        | [] -> Error(ReplayError.Stranded(kyoku, "事件流走完了，引擎还等着有人出手"))
        | event :: rest ->
            let advanced = { driving with Queue = rest }

            match event with
            | Dahai(actor, pai, tsumogiri) -> submit kyoku (Action.Dahai(actor, pai, tsumogiri)) advanced
            | Riichi actor -> submit kyoku (Action.Riichi actor) advanced
            | Ankan(actor, consumed) -> submit kyoku (Action.Ankan(actor, Tile.sort consumed)) advanced
            | Kakan(actor, pai, consumed) -> submit kyoku (Action.Kakan(actor, pai, Tile.sort consumed)) advanced
            | Hora hora -> submit kyoku (Action.Hora(hora.Actor, hora.Target, hora.Pai)) advanced
            // 摸牌后阶段的 `ryukyoku` 只可能是九种九牌：其余形态由引擎自己判，不必提交。
            | Ryuukyoku result when result.Reason = KyuushuKyuuhai ->
                submit kyoku (Action.Ryuukyoku waiting.Actor) advanced
            | Ryuukyoku result ->
                let reason = RyuukyokuReason.toMjai result.Reason
                Error(ReplayError.Stranded(kyoku, $"引擎等着有人出手，事件流却是一条 {reason} 流局"))
            | Pon _
            | Chi _
            | Minkan _ -> Error(ReplayError.Stranded(kyoku, "事件流里有人响应上一张，引擎却没进响应阶段"))
            // 走不到：`pendingMoves` 已经跳过这几种。
            | StartGame _
            | StartKyoku _
            | Tsumo _
            | Dora _
            | RiichiAccepted _
            | EndKyoku
            | EndGame -> Error(ReplayError.Stranded(kyoku, "引擎自己产出的事件混进了要提交的队列"))

    let rec private drive (kyoku: int) (driving: Driving) : Result<Driving, ReplayError> =
        // 要喂的事件喂完了：**回放就是对前缀做 fold**（ADR-0002），停在这里不是错误。
        // 取得出来的就是那一刻的局面（分享一场还没打完的对局走的就是这条路）。
        if List.isEmpty (pendingMoves driving.Queue) then
            Ok driving
        else
            match GameState.phase driving.State with
            // 一局已终：队列里剩下的（`end_kyoku` 之类）不必再喂。
            | Ended _ -> Ok driving
            | AwaitingResponse waiting -> stepResponse kyoku waiting driving |> Result.bind (continuing kyoku driving)
            | AwaitingDahai waiting -> stepDahai kyoku waiting driving |> Result.bind (continuing kyoku driving)

    /// 接着往下走，**除非刚走的那一步越过了事件流**（票 85）。
    ///
    /// **透支了就回到步前那一刻收摊**：那一步里引擎自己摸了一张（或翻了一张指示牌），
    /// 而截断的事件流里没有它——那一张只能从推断出来的牌山里取，是张假牌。
    /// **摸牌与打牌在引擎里是同一步**（`applyDahai` 没人能响应时当场就摸），拆不开；
    /// 因此这里把整一步退掉，宁可少一帧，也不给人看一张牌谱里不存在的牌。
    ///
    /// 于是回放交出来的局面恒是喂进去那份事件流的**某个前缀** fold 出来的：
    /// 手里的每一张要么来自 `start_kyoku` 的起手，要么来自某条 `tsumo`。
    and private continuing (kyoku: int) (before: Driving) (after: Driving) : Result<Driving, ReplayError> =
        if after.Backing < 0 then Ok before else drive kyoku after

    // ---- 入口 ----

    /// 由这一局的 `start_kyoku` 摆出它的场况。**点数取事件流的**：每一局各自从
    /// 它自己记下来的条件开起，因此某一局重建不出来也不会把后面几局全带歪。
    let private contextOf (start: StartKyoku) : KyokuContext =
        {
            Bakaze = start.Bakaze
            Kyoku = start.Kyoku
            Honba = start.Honba
            Kyotaku = start.Kyotaku
            Oya = start.Oya
            Scores = start.Scores
        }

    /// 重放一局：按事件流摆回牌山、开局、把动作原样交回引擎。给出开局那一刻与 fold 完的游标。
    ///
    /// **一帧都交不出来时是 None**：这一局只有 `start_kyoku` 那一条，连 Oya 的第一次自摸
    /// 都没交代（引擎开局必摸那一张，而牌山是推断出来的，那就是张假牌）。
    let private kyoku
        (ruleset: Ruleset)
        (index: int)
        (start: StartKyoku)
        (moves: Event list)
        : Result<(GameState * Driving) option, ReplayError> =
        buildWall ruleset start moves
        |> Result.mapError (fun detail -> ReplayError.CannotBuildWall(index, detail))
        |> Result.bind (fun wall ->
            GameState.startFrom ruleset (contextOf start) wall
            |> Result.mapError (fun error -> ReplayError.CannotStart(index, error)))
        |> Result.bind (fun opening ->
            // 事件流交代的是 `start_kyoku` 那一条加 `moves`；开局那一刻引擎已经花掉了其中几条。
            let backing = 1 + List.length moves - List.length (GameState.events opening)

            if backing < 0 then
                Ok None
            else
                drive
                    index
                    {
                        State = opening
                        Queue = moves
                        Played = []
                        Backing = backing
                    }
                |> Result.map (fun driving -> Some(opening, driving)))

    /// 一局一局 fold 回去，每一局给出「开局那一刻」与「fold 完的游标」。
    ///
    /// **两个出口都从这里走**（`game` 与 `trace`）：一份事件流只有一条 fold 路径，
    /// 因此两边不可能对不上，报错也必然报在同一处。
    ///
    /// 还没打完的局只允许是**最后一局**（事件流到此为止）；中间某一局没走完
    /// 说明这份事件流本身不自洽。
    /// 一局 fold 完之后的取舍。**两种「没走完」都只允许出现在最后一局**（事件流到此为止）：
    /// 局面停在半途，或连开局那一刻都交代不出来（`kyoku` 给 None）。
    let private collected
        (last: int)
        (index: int)
        (before: (GameState * Driving) list)
        (opened: (GameState * Driving) option)
        : Result<(GameState * Driving) list, ReplayError> =
        match opened with
        | Some(_, driving) when index <> last && not (GameState.isEnded driving.State) ->
            Error(ReplayError.Stranded(index, "这一局的事件流没走完，后面却还有别的局"))
        | Some segment -> Ok(before @ [ segment ])
        | None when index = last -> Ok before
        | None -> Error(ReplayError.Stranded(index, "这一局只有一条 start_kyoku，后面却还有别的局"))

    let private folded (ruleset: Ruleset) (events: Event list) : Result<(GameState * Driving) list, ReplayError> =
        match kyokus events with
        | [] -> Error ReplayError.NoKyoku
        | segments ->
            let last = List.length segments - 1

            (Ok [], List.indexed segments)
            ||> List.fold (fun replayed (index, (start, moves)) ->
                replayed
                |> Result.bind (fun before ->
                    kyoku ruleset index start moves |> Result.bind (collected last index before)))
            // 仅有的那一局连开局那一刻都交代不出来：与「一局都没有」同一个处境。
            |> Result.bind (fun replayed ->
                if List.isEmpty replayed then
                    Error ReplayError.NoKyoku
                else
                    Ok replayed)

    /// **事件流 → 一场对局**：一局一局 fold 回去，连庄、结转与终局精算全由 `Game` 重算。
    ///
    /// 得到的东西与当初打出这份事件流的那一场逐项相同——终局点数、顺位，
    /// 乃至 `Replayed.events` 逐条相同。「回放出同一个终局」这件事因此不是靠对齐两份实现，
    /// 而是**只有一份实现**。
    let game (ruleset: Ruleset) (events: Event list) : Result<Replayed, ReplayError> =
        folded ruleset events
        |> Result.map (fun segments ->
            (({
                Game = Game.start ruleset
                Current = None
             }),
             segments)
            ||> List.fold (fun replayed (_, driving) ->
                if GameState.isEnded driving.State then
                    { replayed with
                        Game = Game.advance driving.State replayed.Game
                    }
                else
                    { replayed with
                        Current = Some driving.State
                    }))

    /// **事件流 → 逐手轨迹**（票 71）：一局一段，每段是开局局面加那一局逐条提交的动作。
    ///
    /// **只多一项输出，一条规则也不自己判**：它与 `game` 共用 `folded`，拿到的就是
    /// fold 本来就在做的那几次 `GameState.step`。页面拿它把一份牌谱摆成逐帧的牌桌
    /// （`Janpo.Web` 的 `Table.replay`），因此回放与 Live 用的是同一条落子路径。
    let trace (ruleset: Ruleset) (events: Event list) : Result<ReplayKyoku list, ReplayError> =
        folded ruleset events
        |> Result.map (
            List.map (fun (opening, driving) ->
                {
                    Opening = opening
                    Actions = List.rev driving.Played
                })
        )

    /// 一份牌谱 → 它记的那一场对局。规则集也取自牌谱：回放照的是**这一场**的规则。
    let ofPaifu (paifu: Paifu) : Result<Replayed, ReplayError> = game paifu.Ruleset paifu.Events

    /// 一份牌谱 → 它的逐手轨迹（票 71）。与 `ofPaifu` 同一份规则集、同一条 fold。
    let traceOfPaifu (paifu: Paifu) : Result<ReplayKyoku list, ReplayError> = trace paifu.Ruleset paifu.Events
