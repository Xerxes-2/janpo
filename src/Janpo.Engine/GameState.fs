namespace Janpo

/// 上下文标志：需要「在某个时机置位、由判役时读取」的那几个标志（spec 的规则清单）。
/// **本票只置位，不判役**——役是 07 的事。一发（09）、岭上与抢杠（11）由后续票加字段。
type KyokuFlags =
    {
        /// 海底：刚摸进的是可摸区的最后一张（海底摸月）。
        Haitei: bool
        /// 河底：刚打出的那张之后已经没有可摸的牌了（河底捞鱼）。
        Houtei: bool
    }

/// 摸牌后阶段：`Actor` 刚摸进一张，等它打牌（后续票里还可以自摸和、立直、暗杠）。
type AwaitingDahai =
    {
        /// 等这个座位出手。
        Actor: Seat
        /// 刚摸进的那张。摸切打的就是它。
        Tsumo: Tile
        /// 这个阶段的合法动作集，非空。
        Actions: Action list
    }

/// 他家打牌后的响应阶段：`Target` 刚打出 `Pai`，等其他座位响应（Ron / Pon / Chi / Kan）。
///
/// **多家是逐个答复、收齐后才裁决的**：`Responses` 是还没答复的那几家（答一家少一项），
/// `Declared` 收着已经宣言的那些动作。`Responses` 空了就裁决：06 只有 Ron（按 Atamahane），
/// 10 的 Pon / Chi 与 11 的大明杠在同一处排优先级（Ron > Pon / Kan > Chi）。
type AwaitingResponse =
    {
        /// 打出这张牌的座位。
        Target: Seat
        /// 刚打出的那张。
        Pai: Tile
        /// 还没答复的座位各自的合法动作集，每项非空且必含一条「过」（`Action.None`），
        /// 按打牌者下家优先的顺序排列。
        Responses: LegalActions list
        /// 已经宣言的响应，按答复顺序；「过」不进这里。裁决时重新按优先级排，
        /// 因此「先被问到」不等于「优先」。
        Declared: Action list
    }

/// 一局怎么结束的。04 只有荒牌流局，06 把它换成了 DU；12 票补其余流局形态时
/// 只需给 `RyuukyokuReason` 加 case，不必再动这里。
///
/// case 强制限定名（`KyokuEnd.Hora`）：`Event` 里有同名 case，不限定名的话
/// 后定义的那个会把先定义的遮住，读代码时分不出是「终局形态」还是「事件」。
[<RequireQualifiedAccess>]
type KyokuEnd =
    /// 和了收尾。头跳关掉时同巡双响 / 三响会有不止一条，按打牌者下家优先排序。
    | Hora of horas: Hora list
    /// 无人和了，流局收尾。
    | Ryuukyoku of ryuukyoku: Ryuukyoku

/// 阶段：摸牌后与他家打牌后是**不同的类型**，各自携带各自的合法动作集（spec）。
/// 阶段只能由 `GameState` 内部构造，因此「合法动作集与阶段对不上」这种状态表示不出来。
type Phase =
    /// 摸牌后：等 `Actor` 打牌。
    | AwaitingDahai of awaitingDahai: AwaitingDahai
    /// 他家打牌后：等各座位响应。
    | AwaitingResponse of awaitingResponse: AwaitingResponse
    /// 一局已终，不再接受任何动作。
    | Ended of kyokuEnd: KyokuEnd

/// 不可变的引擎权威状态（CONTEXT.md），含所有座位的暗牌。一局一个。
/// 局与局之间的推进（连庄、进局、供托结转）是 05 的事。
type GameState =
    private
        {
            Ruleset: Ruleset
            /// 由 `Ruleset.TileKinds` 派生的牌种集合，形态判定要它。
            KindSet: TileKindSet
            /// 开局时由上层给定的既定条件：场风、局数、本场、供托、亲与**局初**点数。
            Context: KyokuContext
            Wall: Wall
            /// 各家状态，按座位升序。
            Players: PlayerState list
            Flags: KyokuFlags
            Phase: Phase
            /// 本局已产出的事件流，**倒序**（最新的在头）。对外经 `GameState.events` 取正序。
            Log: Event list
        }

/// 引擎拒绝一个 Action 的原因（CONTEXT.md）。**是值，不是异常**——`step` 对任何输入都返回
/// `Result`，不抛。后续票加动作时在这里加原因，与 `KyokuStartError` 同一方针。
type IllegalAction =
    /// 座位号不在这个规则集里。
    | SeatOutOfRange of actor: Seat * seatCount: int
    /// 现在不等这个座位出手；等的是 `awaiting`（响应阶段可能同时等多家）。
    | NotYourTurn of actor: Seat * awaiting: Seat list
    /// 手里没有这张牌。
    | NotInHand of actor: Seat * pai: Tile
    /// 摸切标志与实际不符：`tsumogiri` 为真时打的必须是刚摸进的那张，为假时手里必须
    /// 另有一张同样的牌。
    | TsumogiriMismatch of actor: Seat * pai: Tile * tsumogiri: bool
    /// 宣言和了的来源与牌与当前局面不符：自摸和的 `target` 必须是自己、`pai` 必须是刚摸进那张；
    /// 荣和的 `target` 必须是刚打牌那家、`pai` 必须是刚打出那张。
    | HoraTileMismatch of actor: Seat * target: Seat * pai: Tile
    /// 手牌加上这张不成和了型。
    /// **振听不走这里**：振听座位的 Ron 压根不会进合法动作集，因此它们得到的是 `NotYourTurn`。
    | NotAgari of actor: Seat * pai: Tile
    /// 型成立但一个役都没有：无役不可和。宝牌不是役，救不了。
    ///
    /// **与 `YakuError.NoYaku` 同名**（两层对同一件事的投影），因此这个 case 的使用点
    /// 一律写全 `IllegalAction.NoYaku`；`YakuError` 那侧同样限定（裁决 D-3 的处置）。
    | NoYaku of actor: Seat * pai: Tile
    /// 现在没有可响应的牌，「过」无从谈起。
    | NothingToRespond of actor: Seat
    /// 一局已终，不再接受任何动作。
    | KyokuAlreadyEnded

/// 终局形态的拆解。
[<RequireQualifiedAccess>]
module KyokuEnd =

    // ---- 拆解 ----

    /// 是否连庄：Oya 和了则连庄（双响里只要有 Oya 就算）；流局时 Oya 听牌则连庄。
    /// 局与局之间怎么推进（Honba 递增、Kyotaku 结转、局数序列）是 05 的事，它读这一个布尔值。
    let isRenchan (oya: Seat) (kyokuEnd: KyokuEnd) : bool =
        match kyokuEnd with
        | KyokuEnd.Hora horas -> horas |> List.exists (fun hora -> hora.Actor = oya)
        | KyokuEnd.Ryuukyoku result -> List.tryItem oya result.Tenpais |> Option.defaultValue false

/// 局面的构造、拆解与迁移。`step` 是引擎唯一的入口：
/// `GameState -> Action -> Result<GameState * Event list, IllegalAction>`。
[<RequireQualifiedAccess>]
module GameState =

    // ---- 判役与算点的上下文 ----

    /// 判役要的上下文，由局面自己填：场风取自开局条件，自风由座位与亲推出，
    /// 宝牌指示牌取自牌山，海底 / 河底取自标志，天和 / 地和由「这家还没打过牌」推出——
    /// 都是牌局历史的产物，因此**调用方不必也不该自己拼一份**。
    ///
    /// 这里填的标志都还要过判役那关：海底只对自摸生效、河底只对荣和生效、
    /// 天和地和还要门清自摸（`Yaku` 自己会判），所以这里不按自摸 / 荣和分两套。
    /// 立直与一发（09）、岭上与抢杠（11）是那两票的标志，**在这里接上**；
    /// 地和还要求此前无人鸣牌，副露（10 / 11）落地时这里要跟着加一条。
    let private yakuContextOf
        (ruleset: Ruleset)
        (kyokuContext: KyokuContext)
        (wall: Wall)
        (flags: KyokuFlags)
        (log: Event list)
        (seat: Seat)
        : YakuContext =
        // 这家还没打过牌 ⟺ 现在是它的第一巡。事件流的顺序与这个判断无关，因此传倒序的也行。
        let firstTurn =
            log
            |> List.forall (fun event ->
                match event with
                | Dahai(actor, _, _) -> actor <> seat
                | StartGame _
                | StartKyoku _
                | Tsumo _
                | Hora _
                | Ryuukyoku _
                | EndKyoku
                | EndGame -> true)

        { YakuContext.create kyokuContext.Bakaze (Seat.jikaze ruleset kyokuContext.Oya seat) with
            Haitei = flags.Haitei
            Houtei = flags.Houtei
            Tenhou = firstTurn && seat = kyokuContext.Oya
            Chiihou = firstTurn && seat <> kyokuContext.Oya
            DoraMarkers = Wall.doraIndicators wall
            UraDoraMarkers = Wall.uraIndicators wall
        }

    let private yakuContext (state: GameState) (seat: Seat) : YakuContext =
        yakuContextOf state.Ruleset state.Context state.Wall state.Flags state.Log seat

    /// 某家拿这张牌和了的话能得到什么：选中的读法与它的符番（`Score.best` 的高点法）。
    ///
    /// **「无役不可和」这条判据只有这一份**：合法动作集、`step` 的校验与和了结算读的都是它。
    let private horaWith
        (ruleset: Ruleset)
        (context: YakuContext)
        (player: PlayerState)
        (winning: Tile)
        (tsumo: bool)
        : Result<HoraReading, YakuError> =
        match PlayerState.agari tsumo winning player with
        // 张数凑不成和了牌姿：连和了型都谈不上。
        | Error _ -> Error YakuError.NotAgari
        | Ok hand -> Score.best ruleset context hand

    // ---- 合法动作集 ----

    /// 摸牌后阶段的合法动作集：自摸和（和了型成立**且有役**时）在最前，随后是手切每一种牌各一条
    /// （同种牌只出现一次，但 `5m` 与 `5mr` 各算各的），最后是摸切那一条。
    /// 手切按 mjai 顺序升序。09 的立直、11 的暗杠与加杠也加在这里。
    ///
    /// **自摸和不看振听**（振听只挡荣和），但**看役**：无役不可和。
    let private awaitingDahaiActions
        (ruleset: Ruleset)
        (context: YakuContext)
        (actor: Seat)
        (player: PlayerState)
        : Action list =
        let tedashi =
            PlayerState.tedashi player
            |> Tile.sort
            |> List.distinct
            |> List.map (fun pai -> Action.Dahai(actor, pai, false))

        match PlayerState.drawn player with
        | None -> tedashi
        | Some drawn ->
            let hora =
                match horaWith ruleset context player drawn true with
                | Ok _ -> [ Action.Hora(actor, actor, drawn) ]
                | Error _ -> []

            hora @ tedashi @ [ Action.Dahai(actor, drawn, true) ]

    /// 他家打牌后各座位的响应，按**打牌者下家优先**的座位顺序排列。
    /// 06 只有 Ron；10 的 Pon / Chi 与 11 的 Daiminkan 也从这里进合法动作集。
    ///
    /// 三条纪律：
    /// 1. **振听的座位压根不出现在这里**（而不是宣言之后再报错）；
    /// 2. **无役的座位同样不出现**：型成立不等于能和（`horaWith` 就是那条判据）；
    /// 3. **有任何响应就必须同时给一条「过」**，否则响应阶段停在那里没人推得走。
    let private responsesTo (state: GameState) (target: Seat) (pai: Tile) : LegalActions list =
        Seat.orderFrom state.Ruleset (Seat.next state.Ruleset target)
        |> List.filter (fun seat -> seat <> target)
        |> List.choose (fun seat ->
            match List.tryItem seat state.Players with
            | Option.None -> Option.None
            | Some player ->
                let canRon =
                    not (Furiten.blocksRon (PlayerState.furiten player))
                    && (match horaWith state.Ruleset (yakuContext state seat) player pai false with
                        | Ok _ -> true
                        | Error _ -> false)

                if canRon then
                    Some
                        {
                            Seat = seat
                            Actions = [ Action.Hora(seat, target, pai); Action.None seat ]
                        }
                else
                    Option.None)

    /// 当前的合法动作集：等谁、能做什么。**空列表 ⟺ 这一局已终**（属性测试钉住了这条）。
    /// 真人 UI 的按钮与 LLM 的工具 schema 都由它驱动，两边都不自己判断合法性。
    let legalActions (state: GameState) : LegalActions list =
        match state.Phase with
        | AwaitingDahai phase ->
            [
                {
                    Seat = phase.Actor
                    Actions = phase.Actions
                }
            ]
        | AwaitingResponse phase -> phase.Responses
        | Ended _ -> []

    // ---- 拆解 ----

    /// 这一局的规则集。
    let ruleset (state: GameState) : Ruleset = state.Ruleset

    /// 开局时由上层给定的既定条件。里面的 `Scores` 是**局初**点数，当前点数用 `scores`。
    let context (state: GameState) : KyokuContext = state.Context

    /// 牌山。
    let wall (state: GameState) : Wall = state.Wall

    /// 各家状态，按座位升序。
    let players (state: GameState) : PlayerState list = state.Players

    /// 某座位的家状态；座位不合法返回 None。
    let player (seat: Seat) (state: GameState) : PlayerState option = List.tryItem seat state.Players

    /// 当前各家点数，按座位升序。荒牌流局授受之后这里就是授受后的点数。
    let scores (state: GameState) : int list =
        state.Players |> List.map PlayerState.score

    /// 当前阶段。
    let phase (state: GameState) : Phase = state.Phase

    /// 上下文标志（海底 / 河底）。判役的票读它，本票只负责置位。
    let flags (state: GameState) : KyokuFlags = state.Flags

    /// 本局已产出的事件流，按产出顺序。这就是这一局的 Paifu（ADR-0002）。
    let events (state: GameState) : Event list = List.rev state.Log

    /// 这一局是否已终。
    let isEnded (state: GameState) : bool =
        match state.Phase with
        | Ended _ -> true
        | AwaitingDahai _
        | AwaitingResponse _ -> false

    /// 这一局是怎么结束的；还没终则为 None。05 的进局 / 连庄判定读它（`KyokuEnd.isRenchan`）。
    let kyokuEnd (state: GameState) : KyokuEnd option =
        match state.Phase with
        | Ended result -> Some result
        | AwaitingDahai _
        | AwaitingResponse _ -> None

    /// 终局的流局结果；不是流局收尾（或还没终）则为 None。
    let ryuukyoku (state: GameState) : Ryuukyoku option =
        match kyokuEnd state with
        | Some(KyokuEnd.Ryuukyoku result) -> Some result
        | Some(KyokuEnd.Hora _)
        | None -> None

    /// 某座位此刻和了的话能得到什么：选中的读法、符与番。和的是哪张、自摸还是荣和都由
    /// **当前阶段**决定（摸牌后阶段是刚摸进那张的自摸，响应阶段是刚打出那张的荣和），
    /// 因此问不出没意义的问题。
    ///
    /// 型不成或无役时是 `YakuError`。**合法动作集读的就是它**——这个函数给 Error 的座位，
    /// Hora 压根不在它的动作集里（振听另算，那是荣和才有的事）。
    let horaOf (seat: Seat) (state: GameState) : Result<HoraReading, YakuError> =
        match List.tryItem seat state.Players, state.Phase with
        | Some player, AwaitingDahai phase when phase.Actor = seat ->
            match PlayerState.drawn player with
            | Some drawn -> horaWith state.Ruleset (yakuContext state seat) player drawn true
            | Option.None -> Error YakuError.NotAgari
        | Some player, AwaitingResponse phase when phase.Target <> seat ->
            horaWith state.Ruleset (yakuContext state seat) player phase.Pai false
        | _ -> Error YakuError.NotAgari

    /// 终局的和了；不是和了收尾（或还没终）则为空列表。头跳关掉时的双响会有两条。
    let horas (state: GameState) : Hora list =
        match kyokuEnd state with
        | Some(KyokuEnd.Hora horas) -> horas
        | Some(KyokuEnd.Ryuukyoku _)
        | None -> []

    // ---- 构造 ----

    let private updatePlayer (seat: Seat) (change: PlayerState -> PlayerState) (players: PlayerState list) =
        players
        |> List.mapi (fun index player -> if index = seat then change player else player)

    let private awaitDahai
        (ruleset: Ruleset)
        (context: YakuContext)
        (actor: Seat)
        (drawn: Tile)
        (players: PlayerState list)
        : Phase =
        let actions =
            match List.tryItem actor players with
            | None -> []
            | Some player -> awaitingDahaiActions ruleset context actor player

        AwaitingDahai
            {
                Actor = actor
                Tsumo = drawn
                Actions = actions
            }

    let private ofStarted (ruleset: Ruleset) (context: KyokuContext) (started: KyokuStart) : GameState =
        // Oya 的配牌里已经含它摸进的第一张，只需补上「刚摸进的是哪张」。
        let players =
            List.mapi2
                (fun seat score hand ->
                    let drawn = if seat = context.Oya then Some started.Tsumo else None

                    PlayerState.ofHaipai score drawn hand)
                context.Scores
                started.Hands

        let kindSet = TileKindSet.ofKinds ruleset.TileKinds

        let flags =
            {
                Haitei = Wall.remaining started.Wall = 0
                Houtei = false
            }

        {
            Ruleset = ruleset
            KindSet = kindSet
            Context = context
            Wall = started.Wall
            Players = players
            Flags = flags
            Phase =
                awaitDahai
                    ruleset
                    (yakuContextOf ruleset context started.Wall flags started.Events context.Oya)
                    context.Oya
                    started.Tsumo
                    players
            Log = List.rev started.Events
        }

    /// 开一局：洗牌、配牌、Oya 摸第一张，得到一个等 Oya 打牌的局面。
    /// 同一种子、同一规则集、同一条件必然开出同一局（`KyokuStart.create` 的保证）。
    let start (ruleset: Ruleset) (context: KyokuContext) (rng: Rng) : Result<GameState * Rng, KyokuStartError> =
        KyokuStart.create ruleset context rng
        |> Result.map (fun (started, advanced) -> ofStarted ruleset context started, advanced)

    /// 从一座**已经摊好的**牌山开一局（`Wall.ofOrdered`）。库外拿不到（`internal`）：
    /// 它是黄金用例「让指定的和了在指定 Junme 发生」的构造入口，生产代码一律用 `start`。
    let internal startFrom
        (ruleset: Ruleset)
        (context: KyokuContext)
        (wall: Wall)
        : Result<GameState, KyokuStartError> =
        KyokuStart.createFrom ruleset context wall
        |> Result.map (ofStarted ruleset context)

    // ---- 迁移 ----

    /// 荒牌流局的授受：不听牌的家合计付 `NotenBappu`，听牌的家平分；全听或全不听时不授受。
    let private notenBappu (ruleset: Ruleset) (tenpais: bool list) : int list =
        let tenpaiCount = tenpais |> List.filter id |> List.length
        let notenCount = List.length tenpais - tenpaiCount

        if tenpaiCount = 0 || notenCount = 0 then
            tenpais |> List.map (fun _ -> 0)
        else
            let gain = ruleset.NotenBappu / tenpaiCount
            let loss = ruleset.NotenBappu / notenCount
            tenpais |> List.map (fun tenpai -> if tenpai then gain else -loss)

    /// 荒牌流局：可摸区摸完，按听牌家数授受听牌料，一局告终。
    let private exhaustiveDraw (state: GameState) : GameState * Event list =
        let tenpais = state.Players |> List.map (PlayerState.isTenpai state.KindSet)
        let deltas = notenBappu state.Ruleset tenpais
        let settled = List.map2 PlayerState.addScore deltas state.Players

        let result =
            {
                Reason = Fanpai
                Tenpais = tenpais
                Deltas = deltas
                Scores = settled |> List.map PlayerState.score
            }

        { state with
            Players = settled
            Phase = Ended(KyokuEnd.Ryuukyoku result)
        },
        [ Event.Ryuukyoku result ]

    /// 和了：算符与点数、按 Oya / Ko 与自摸 / 荣和授受，一局告终。
    ///
    /// `wins` 已经按打牌者下家优先排好（`ronWinners`），**本场与供托只归排在最前的那一家**；
    /// 双响时两条 `Hora` 的 `Scores` 是**逐条累加**的，最后一条就是这一局的最终点数。
    ///
    /// 供托进了和了者的 `Deltas`，因此一次和了的增减之和 = 供托点数而不是 0；
    /// 局与局之间把 Kyotaku 归零是 05 的事（本票只管一局之内的授受）。
    let private applyHora (state: GameState) (target: Seat) (wins: (Seat * Tile) list) : GameState * Event list =
        let settle (players: PlayerState list, horas: Hora list) (index: int, (actor: Seat, pai: Tile)) =
            let value =
                match horaOf actor state with
                | Ok reading -> reading.Value
                // 走不到：Hora 进得了合法动作集就一定有役。不抛异常，记 0 符 0 番。
                | Error _ -> { Fu = 0; Han = 0; Yakuman = 0 }

            let score =
                Score.hora
                    state.Ruleset
                    {
                        Actor = actor
                        Target = target
                        Oya = state.Context.Oya
                        Honba = if index = 0 then state.Context.Honba else 0
                        Kyotaku = if index = 0 then state.Context.Kyotaku else 0
                    }
                    value

            let settled = List.map2 PlayerState.addScore score.Deltas players

            settled,
            horas
            @ [
                {
                    Actor = actor
                    Target = target
                    Pai = pai
                    Fu = value.Fu
                    Fan = HoraValue.fan value
                    HoraPoints = score.HoraPoints
                    Deltas = score.Deltas
                    Scores = settled |> List.map PlayerState.score
                }
            ]

        let settled, horas = List.indexed wins |> List.fold settle (state.Players, [])

        { state with
            Players = settled
            Phase = Ended(KyokuEnd.Hora horas)
        },
        horas |> List.map Event.Hora

    /// 打完牌之后：下家摸一张进入摸牌后阶段；可摸区空了就是荒牌流局。
    let private drawNext (state: GameState) (from: Seat) : GameState * Event list =
        match Wall.draw state.Wall with
        | None -> exhaustiveDraw state
        | Some(drawn, rest) ->
            let next = Seat.next state.Ruleset from
            let players = updatePlayer next (PlayerState.draw drawn) state.Players

            let advanced =
                { state with
                    Wall = rest
                    Players = players
                    Flags =
                        {
                            Haitei = Wall.remaining rest = 0
                            Houtei = false
                        }
                }

            { advanced with
                Phase = awaitDahai advanced.Ruleset (yakuContext advanced next) next drawn players
            },
            [ Event.Tsumo(next, drawn) ]

    let private applyDahai (state: GameState) (actor: Seat) (pai: Tile) (tsumogiri: bool) : GameState * Event list =
        let discarded =
            { state with
                // 打完这张，自家的听牌就变了，永久振听要按新的听牌与自己的河重算。
                Players =
                    state.Players
                    |> updatePlayer actor (PlayerState.discard pai >> PlayerState.refreshFuriten state.KindSet)
                Flags =
                    {
                        Haitei = false
                        // 打出的这张之后再没有可摸的牌了，它就是河底那张。
                        Houtei = Wall.remaining state.Wall = 0
                    }
            }

        let advanced, events =
            match responsesTo discarded actor pai with
            | [] -> drawNext discarded actor
            | responses ->
                { discarded with
                    Phase =
                        AwaitingResponse
                            {
                                Target = actor
                                Pai = pai
                                Responses = responses
                                Declared = []
                            }
                },
                []

        advanced, Event.Dahai(actor, pai, tsumogiri) :: events

    /// 还等着答复的座位。
    let private pendingSeats (waiting: AwaitingResponse) : Seat list =
        waiting.Responses |> List.map (fun each -> each.Seat)

    /// 某座位此刻能提交的响应。不在等它答复时是空列表。
    let private responseActions (waiting: AwaitingResponse) (actor: Seat) : Action list =
        waiting.Responses
        |> List.tryFind (fun each -> each.Seat = actor)
        |> Option.map (fun each -> each.Actions)
        |> Option.defaultValue []

    /// 多家响应的裁决顺序：**打牌者的下家优先**，与「先被问到」无关。
    /// 06 只有 Ron：头跳开着时只取排在最前的那一家，关掉则双响 / 三响都成立。
    /// 10 的 Pon / Chi 与 11 的大明杠也在这里排（先按动作种类 Ron > Pon / Kan > Chi，再按座位）。
    let private ronWinners (state: GameState) (target: Seat) (declared: Action list) : (Seat * Tile) list =
        let declarers =
            declared
            |> List.choose (fun action ->
                match action with
                | Action.Hora(actor, _, pai) -> Some(actor, pai)
                | Action.Dahai _
                | Action.None _ -> Option.None)

        let ordered =
            Seat.orderFrom state.Ruleset (Seat.next state.Ruleset target)
            |> List.choose (fun seat -> declarers |> List.tryFind (fun (actor, _) -> actor = seat))

        if state.Ruleset.Atamahane then
            List.truncate 1 ordered
        else
            ordered

    /// 响应阶段收下一家的答复：`declaration` 为 None 就是「过」。
    /// 收齐之后才裁决——因此裁决与「谁先答」无关，而只与优先级有关。
    let private applyResponse
        (state: GameState)
        (waiting: AwaitingResponse)
        (actor: Seat)
        (declaration: Action option)
        : GameState * Event list =
        let couldRon =
            responseActions waiting actor
            |> List.exists (fun action ->
                match action with
                | Action.Hora _ -> true
                | Action.Dahai _
                | Action.None _ -> false)

        let answered =
            match declaration with
            | Some _ -> state
            | Option.None ->
                // 见逃一次可以荣和的牌 → 同巡振听，到自己下次摸牌为止不能荣和。
                if couldRon then
                    { state with
                        Players = updatePlayer actor PlayerState.minogashi state.Players
                    }
                else
                    state

        let remaining = waiting.Responses |> List.filter (fun each -> each.Seat <> actor)

        let declared = waiting.Declared @ Option.toList declaration

        if List.isEmpty remaining then
            match ronWinners answered waiting.Target declared with
            | [] -> drawNext answered waiting.Target
            | wins -> applyHora answered waiting.Target wins
        else
            { answered with
                Phase =
                    AwaitingResponse
                        { waiting with
                            Responses = remaining
                            Declared = declared
                        }
            },
            []

    /// 打牌的合法性：摸切打的必须是刚摸进的那张；手切打的必须是手里另有的那张。
    let private rejectDahai (actor: Seat) (player: PlayerState) (pai: Tile) (tsumogiri: bool) : IllegalAction option =
        if tsumogiri then
            if PlayerState.drawn player = Some pai then
                None
            else
                Some(TsumogiriMismatch(actor, pai, tsumogiri))
        elif List.contains pai (PlayerState.tedashi player) then
            None
        elif List.contains pai (PlayerState.hand player) then
            // 手里只有刚摸进的那一张，那就只能是摸切。
            Some(TsumogiriMismatch(actor, pai, tsumogiri))
        else
            Some(NotInHand(actor, pai))

    /// 摸牌后阶段的一步：打牌、自摸和；「过」在这里无从谈起。
    let private stepAwaitingDahai
        (state: GameState)
        (waiting: AwaitingDahai)
        (action: Action)
        : Result<GameState * Event list, IllegalAction> =
        let actor = Action.actor action

        if not (Seat.isValid state.Ruleset actor) then
            Error(SeatOutOfRange(actor, state.Ruleset.SeatCount))
        elif actor <> waiting.Actor then
            Error(NotYourTurn(actor, [ waiting.Actor ]))
        else
            match List.tryItem actor state.Players with
            | Option.None -> Error(SeatOutOfRange(actor, state.Ruleset.SeatCount))
            | Some player ->
                match action with
                | Action.Dahai(_, pai, tsumogiri) ->
                    match rejectDahai actor player pai tsumogiri with
                    | Some illegal -> Error illegal
                    | Option.None -> Ok(applyDahai state actor pai tsumogiri)
                | Action.Hora(_, target, pai) ->
                    // 自摸和：来源是自己，和的是刚摸进的那张，且型成立、有役。
                    if target <> actor || PlayerState.drawn player <> Some pai then
                        Error(HoraTileMismatch(actor, target, pai))
                    else
                        match horaOf actor state with
                        | Error YakuError.NotAgari -> Error(IllegalAction.NotAgari(actor, pai))
                        | Error YakuError.NoYaku -> Error(IllegalAction.NoYaku(actor, pai))
                        | Ok _ -> Ok(applyHora state actor [ actor, pai ])
                | Action.None _ -> Error(NothingToRespond actor)

    /// 响应阶段的一步：宣言荣和或「过」。收齐全部答复之后才裁决（见 `applyResponse`）。
    let private stepAwaitingResponse
        (state: GameState)
        (waiting: AwaitingResponse)
        (action: Action)
        : Result<GameState * Event list, IllegalAction> =
        let actor = Action.actor action

        if not (Seat.isValid state.Ruleset actor) then
            Error(SeatOutOfRange(actor, state.Ruleset.SeatCount))
        elif not (List.contains actor (pendingSeats waiting)) then
            // 振听的座位与答复过的座位都落在这里：它们不在等答复之列。
            Error(NotYourTurn(actor, pendingSeats waiting))
        elif List.contains action (responseActions waiting actor) then
            Ok(
                applyResponse
                    state
                    waiting
                    actor
                    (match action with
                     | Action.None _ -> Option.None
                     | Action.Dahai _
                     | Action.Hora _ -> Some action)
            )
        else
            match action with
            | Action.Hora(_, target, pai) ->
                if target <> waiting.Target || pai <> waiting.Pai then
                    Error(HoraTileMismatch(actor, target, pai))
                else
                    Error(IllegalAction.NotAgari(actor, pai))
            | Action.Dahai _
            | Action.None _ -> Error(NotYourTurn(actor, pendingSeats waiting))

    /// 引擎的唯一入口：给局面提交一个动作，得到新局面与本步产出的事件。
    /// 动作不合法时返回 `IllegalAction` 值（**不抛异常**），局面原样不动。
    ///
    /// 产出的事件同时已经追加进新局面的事件流（`GameState.events`）——两者是同一份事实。
    /// 唯一不产出事件的动作是响应阶段的「过」（mjai 的 `none` 不是事件，只是一次答复）。
    let step (state: GameState) (action: Action) : Result<GameState * Event list, IllegalAction> =
        let outcome =
            match state.Phase with
            | Ended _ -> Error KyokuAlreadyEnded
            | AwaitingResponse waiting -> stepAwaitingResponse state waiting action
            | AwaitingDahai waiting -> stepAwaitingDahai state waiting action

        outcome
        |> Result.map (fun (next, events) ->
            { next with
                Log = List.rev events @ next.Log
            },
            events)

/// 非法动作的渲染。
[<RequireQualifiedAccess>]
module IllegalAction =

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (illegal: IllegalAction) : string =
        match illegal with
        | SeatOutOfRange(actor, seatCount) -> $"座位 {actor} 不合法，座位只有 0-{seatCount - 1}"
        | NotYourTurn(actor, awaiting) ->
            let waiting = awaiting |> List.map string |> String.concat "、"

            if List.isEmpty awaiting then
                $"现在不轮到座位 {actor} 出手"
            else
                $"现在不轮到座位 {actor} 出手，等的是座位 {waiting}"
        | NotInHand(actor, pai) -> $"座位 {actor} 手里没有 {Tile.toDisplay pai}"
        | TsumogiriMismatch(actor, pai, tsumogiri) ->
            if tsumogiri then
                $"座位 {actor} 声称摸切 {Tile.toDisplay pai}，但刚摸进的不是这张"
            else
                $"座位 {actor} 声称手切 {Tile.toDisplay pai}，但手里只有刚摸进的那一张"
        | HoraTileMismatch(actor, target, pai) ->
            if actor = target then
                $"座位 {actor} 声称自摸和 {Tile.toDisplay pai}，但刚摸进的不是这张"
            else
                $"座位 {actor} 声称荣和座位 {target} 打出的 {Tile.toDisplay pai}，但刚打出的不是这张"
        | IllegalAction.NotAgari(actor, pai) -> $"座位 {actor} 的手牌加上 {Tile.toDisplay pai} 不成和了型"
        | IllegalAction.NoYaku(actor, pai) -> $"座位 {actor} 的手牌加上 {Tile.toDisplay pai} 一个役都没有，不能和"
        | NothingToRespond actor -> $"现在没有可响应的牌，座位 {actor} 无从「过」起"
        | KyokuAlreadyEnded -> "这一局已经结束了，不再接受任何动作"
