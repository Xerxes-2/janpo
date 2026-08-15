namespace Janpo

/// 终局精算（CONTEXT.md 的 Game：「从起家决定到终局精算为止」）：一场对局打完之后的
/// 最终点数与顺位。
type GameResult =
    {
        /// 最终各家点数，按座位升序。**场上剩下的供托已经归属完毕**，
        /// 因此它们之和恒为 `Ruleset.startingTotal`。
        Scores: int list
        /// 各家顺位，按座位升序，1 起（1 是头名）。同点时**起家方向在前**的那家名次高
        /// （起家是座位 0，因此就是座位号小的在前）。
        Juni: int list
    }

/// 一场对局的进程：要么还有下一局，要么局数序列已经走完。
///
/// case 强制限定名（`GameProgress.Ended`）：`Phase` 里有个同名的 `Ended`（一局的阶段），
/// 不限定名的话后定义的那个会把先定义的遮住（裁决 D-3 的教训）。
[<RequireQualifiedAccess>]
type GameProgress =
    /// 还有下一局，附它的场况：场风、局数、本场、供托、亲与局初点数。
    | NextKyoku of context: KyokuContext
    /// 局数序列走完了，附终局精算。
    | Ended of result: GameResult

/// 一场对局（CONTEXT.md）：从起家决定到终局精算为止。
///
/// **它是 `GameState` 之上的一层**：「一局」的边界仍然完整地留在 `GameState` 里，
/// 本类型只管「一局的结局 → 下一局的场况」这一段，以及局数序列走完之后的精算。
/// 连庄判定不在这里——那是 `KyokuEnd.isRenchan`（06 落地的那一份），本层只读它的布尔值。
type Game =
    private
        {
            Ruleset: Ruleset
            /// 已经打完的局，**倒序**（最新的在头）。每项是那一局的终局局面，
            /// 自带那一局的事件流。对外经 `Game.played` 取正序。
            Played: GameState list
            Progress: GameProgress
        }

/// 一场对局的构造、拆解、推进与驱动。
[<RequireQualifiedAccess>]
module Game =

    // ---- 终局精算 ----

    /// 各家顺位：点数高的名次靠前，同点时起家方向在前的那家名次高。
    /// 名次 = 排在自己前面的家数 + 1，因此它必然是 `1 .. SeatCount` 的一个排列。
    let private juniOf (scores: int list) : int list =
        scores
        |> List.mapi (fun seat score ->
            let ahead (other: Seat) (otherScore: int) =
                otherScore > score || (otherScore = score && other < seat)

            1 + (scores |> List.mapi ahead |> List.filter id |> List.length))

    /// 终局精算：场上剩下的供托归**点数最高**的那家（同点取起家方向在前的），
    /// 再按最终点数排顺位。供托在这里只换归属不凭空增减，因此
    /// 「四家点数与供托之和」在精算前后同样守恒。
    ///
    /// 顺位按精算**前**的点数排：把供托给头名不可能改变名次顺序，因此两种算法同解。
    let settle (ruleset: Ruleset) (kyotaku: int) (scores: int list) : GameResult =
        let juni = juniOf scores
        let awarded = Ruleset.kyotakuScore ruleset kyotaku

        {
            Scores = List.map2 (fun score rank -> if rank = 1 then score + awarded else score) scores juni
            Juni = juni
        }

    // ---- 局与局之间 ----

    /// 局数序列里排在这一局后面的那一局；这一局是最后一局（或压根不在序列里）则为 None。
    let private following (ruleset: Ruleset) (bakaze: Kaze) (kyoku: int) : (Kaze * int) option =
        let sequence = Ruleset.kyokus ruleset

        sequence
        |> List.tryFindIndex (fun (eachBakaze, eachKyoku) -> eachBakaze = bakaze && eachKyoku = kyoku)
        |> Option.bind (fun index -> List.tryItem (index + 1) sequence)

    /// **一局的结局 → 下一局的场况**，本票的主干。给它这一局的场况、结局与终了时的点数，
    /// 它给出下一局的场况，或者「局数序列走完了」加终局精算。
    ///
    /// 三条结转规则：
    /// - **连庄**（`KyokuEnd.isRenchan`：Oya 和了，或流局时 Oya 听牌）局数与亲不变，Honba +1；
    ///   **进局**则亲移到下家、局数走到序列的下一项。
    /// - **Honba** 在连庄与流局时递增；只有「和了进局」（Ko 和了）才归零——
    ///   流局进局（亲不听）照样递增，这是通行规则。
    /// - **Kyotaku** 流局时原样结转到下一局；和了时清零（它归和了者，
    ///   **把点数真的加给和了者是 08 票的授受**，本层只管场上还剩几根）。
    ///
    /// 局数序列走完就终局，**连庄也不延长**：东风战在东 4 局结束后终局，不做西入 / 延长
    /// （05 票的 Out of Scope）。
    ///
    /// `scores` 是这一局终了时的各家点数（`GameState.scores`）。和了的点数授受、本场与供托的
    /// 计入都是 08 票的事，本层只把结果原样搬进下一局的场况。
    ///
    /// **传进来的 `context.Kyotaku` 必须是「这一局终了时场上还剩几根」**（`GameState.kyotaku`），
    /// 而不是局初那几根：立直棒是局内产生的，两者在有人立直的局里不相等。
    /// 正常路径是 `Game.advance`，它已经接好了。
    let after (ruleset: Ruleset) (context: KyokuContext) (kyokuEnd: KyokuEnd) (scores: int list) : GameProgress =
        let renchan = KyokuEnd.isRenchan context.Oya kyokuEnd

        let honba, kyotaku =
            match kyokuEnd with
            | KyokuEnd.Ryuukyoku _ -> context.Honba + 1, context.Kyotaku
            | KyokuEnd.Hora _ -> (if renchan then context.Honba + 1 else 0), 0

        match following ruleset context.Bakaze context.Kyoku, renchan with
        | None, _ -> GameProgress.Ended(settle ruleset kyotaku scores)
        | Some _, true ->
            GameProgress.NextKyoku
                { context with
                    Honba = honba
                    Kyotaku = kyotaku
                    Scores = scores
                }
        | Some(bakaze, kyoku), false ->
            GameProgress.NextKyoku
                {
                    Bakaze = bakaze
                    Kyoku = kyoku
                    Honba = honba
                    Kyotaku = kyotaku
                    Oya = Seat.next ruleset context.Oya
                    Scores = scores
                }

    // ---- 构造 ----

    /// 开一场对局：第一局是东 1 局 0 本场，各家起手点数取自规则集。
    /// 此时一张牌都还没摸——开局是 `Game.play` / `GameState.start` 的事。
    let start (ruleset: Ruleset) : Game =
        {
            Ruleset = ruleset
            Played = []
            Progress = GameProgress.NextKyoku(KyokuContext.initial ruleset)
        }

    // ---- 拆解 ----

    /// 这场对局的规则集。
    let ruleset (game: Game) : Ruleset = game.Ruleset

    /// 进程：还有下一局，还是已经终局。
    let progress (game: Game) : GameProgress = game.Progress

    /// 已经打完的局，按顺序。连庄的每一本场各算一局。
    let played (game: Game) : GameState list = List.rev game.Played

    /// 下一局的场况；已经终局则为 None。
    let nextKyoku (game: Game) : KyokuContext option =
        match game.Progress with
        | GameProgress.NextKyoku context -> Some context
        | GameProgress.Ended _ -> None

    /// 终局精算；还没终局则为 None。
    let result (game: Game) : GameResult option =
        match game.Progress with
        | GameProgress.Ended result -> Some result
        | GameProgress.NextKyoku _ -> None

    /// 这场对局是否已终。
    let isEnded (game: Game) : bool =
        match game.Progress with
        | GameProgress.Ended _ -> true
        | GameProgress.NextKyoku _ -> false

    /// 当前各家点数，按座位升序：还没终局就是下一局的**局初**点数，
    /// 已终局就是精算后的最终点数。
    let scores (game: Game) : int list =
        match game.Progress with
        | GameProgress.NextKyoku context -> context.Scores
        | GameProgress.Ended result -> result.Scores

    /// 场上还堆着几根供托；已终局则为 0（精算时已经归属完毕）。
    let kyotaku (game: Game) : int =
        match game.Progress with
        | GameProgress.NextKyoku context -> context.Kyotaku
        | GameProgress.Ended _ -> 0

    /// 整场对局的 mjai 事件流：每一局的事件后跟一条 `end_kyoku`，终局再加一条 `end_game`。
    /// 这就是这场对局的 Paifu（ADR-0002）。
    ///
    /// **不含 `start_game`**：它的 `names` 来自配桌，不是引擎算得出来的（02 票的决定），
    /// 由上层拼在最前面。
    let events (game: Game) : Event list =
        let kyokus =
            played game |> List.collect (fun state -> GameState.events state @ [ EndKyoku ])

        match game.Progress with
        | GameProgress.Ended _ -> kyokus @ [ EndGame ]
        | GameProgress.NextKyoku _ -> kyokus

    // ---- 迁移 ----

    /// 把打完的一局收进这场对局：记下这一局，再按它的结局定下一局的场况（`Game.after`）。
    /// 用的是**这一局自己的**场况（`GameState.context`）——它才是这一局在什么条件下打的权威。
    ///
    /// 这一局还没终、或这场对局已经终了，都原样返回：没有事情发生，不是错误。
    /// 正常路径是 `Game.play`，它开局、打完、再调这里。
    let advance (state: GameState) (game: Game) : Game =
        match game.Progress, GameState.kyokuEnd state with
        | GameProgress.Ended _, _
        | _, None -> game
        | GameProgress.NextKyoku _, Some kyokuEnd ->
            { game with
                Played = state :: game.Played
                Progress =
                    after
                        game.Ruleset
                        // 结转的是这一局**终了时**场上剩的供托：局初那几根不含局内打出去的立直棒。
                        { GameState.context state with
                            Kyotaku = GameState.kyotaku state
                        }
                        kyokuEnd
                        (GameState.scores state)
            }

    // ---- 驱动 ----

    /// 打下一局：开局、让选手把它打到终、收进这场对局。已经终局则原样返回。
    /// `rng` 只用来开局（洗牌）；选手自己的状态经 `'player` 穿进穿出。
    let play
        (player: Player<'player>)
        (initial: 'player)
        (rng: Rng)
        (game: Game)
        : Result<Game * 'player * Rng, KyokuError> =
        match game.Progress with
        | GameProgress.Ended _ -> Ok(game, initial, rng)
        | GameProgress.NextKyoku context ->
            match GameState.start game.Ruleset context rng with
            | Error error -> Error(CannotStart error)
            | Ok(state, advanced) ->
                Kyoku.run player initial state
                |> Result.mapError Illegal
                |> Result.map (fun (final, current) -> advance final game, current, advanced)

    /// 把一整场对局打完：一局接一局，直到局数序列走完。
    let run
        (player: Player<'player>)
        (initial: 'player)
        (rng: Rng)
        (game: Game)
        : Result<Game * 'player * Rng, KyokuError> =
        let rec loop (game: Game) (current: 'player) (rng: Rng) =
            match game.Progress with
            | GameProgress.Ended _ -> Ok(game, current, rng)
            | GameProgress.NextKyoku _ ->
                match play player current rng game with
                | Error error -> Error error
                | Ok(next, advanced, advancedRng) -> loop next advanced advancedRng

        loop game initial rng

    /// 开一场对局，再让随机选手把它打完。同一种子必然跑出同一场对局
    /// （牌山与选手共用同一个发生器，与 `Kyoku.runRandom` 同一条流）。
    let runRandom (ruleset: Ruleset) (rng: Rng) : Result<Game * Rng, KyokuError> =
        let rec loop (game: Game) (rng: Rng) =
            match game.Progress with
            | GameProgress.Ended _ -> Ok(game, rng)
            | GameProgress.NextKyoku context ->
                match Kyoku.runRandom game.Ruleset context rng with
                | Error error -> Error error
                | Ok(final, advanced) -> loop (advance final game) advanced

        loop (start ruleset) rng

/// 终局精算的渲染。
[<RequireQualifiedAccess>]
module GameResult =

    // ---- 拆解 ----

    /// 按名次排好的「顺位、座位、点数」，头名在前。
    let ranking (result: GameResult) : (int * Seat * int) list =
        List.mapi2 (fun seat score juni -> juni, seat, score) result.Scores result.Juni
        |> List.sortBy (fun (juni, _, _) -> juni)

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (result: GameResult) : string =
        ranking result
        |> List.map (fun (juni, seat, score) -> $"{juni}位 座位{seat} {score}")
        |> String.concat "  "
