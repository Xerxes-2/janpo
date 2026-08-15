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
/// **04 票里没有任何可响应的动作**，`Responses` 恒为空，因此引擎不会停在这个阶段——
/// 打完牌直接推进到下家摸牌或荒牌流局。06 / 10 / 11 会往 `Responses` 里填东西，
/// 那时引擎才会真的停下来等响应。
type AwaitingResponse =
    {
        /// 打出这张牌的座位。
        Target: Seat
        /// 刚打出的那张。
        Pai: Tile
        /// 各座位各自的合法动作集，每项非空。
        Responses: LegalActions list
    }

/// 阶段：摸牌后与他家打牌后是**不同的类型**，各自携带各自的合法动作集（spec）。
/// 阶段只能由 `GameState` 内部构造，因此「合法动作集与阶段对不上」这种状态表示不出来。
type Phase =
    /// 摸牌后：等 `Actor` 打牌。
    | AwaitingDahai of awaitingDahai: AwaitingDahai
    /// 他家打牌后：等各座位响应。
    | AwaitingResponse of awaitingResponse: AwaitingResponse
    /// 一局已终，不再接受任何动作。04 票只有荒牌流局一种终局形态；
    /// 06 的和了与 12 的其余流局形态会把这里换成一个 DU，本票不提前抽象。
    | Ended of ryuukyoku: Ryuukyoku

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
    /// 一局已终，不再接受任何动作。
    | KyokuAlreadyEnded

/// 局面的构造、拆解与迁移。`step` 是引擎唯一的入口：
/// `GameState -> Action -> Result<GameState * Event list, IllegalAction>`。
[<RequireQualifiedAccess>]
module GameState =

    // ---- 合法动作集 ----

    /// 摸牌后阶段的合法动作集。04 票只有 Dahai：手切每一种牌各一条（同种牌只出现一次，
    /// 但 `5m` 与 `5mr` 各算各的），再加摸切那一条。顺序固定：手切按 mjai 顺序升序，摸切在最后。
    /// 09 的立直、11 的暗杠与加杠、06 的自摸和都加在这里。
    let private awaitingDahaiActions (actor: Seat) (player: PlayerState) : Action list =
        let tedashi =
            PlayerState.tedashi player
            |> Tile.sort
            |> List.distinct
            |> List.map (fun pai -> Action.Dahai(actor, pai, false))

        match PlayerState.drawn player with
        | None -> tedashi
        | Some drawn -> tedashi @ [ Action.Dahai(actor, drawn, true) ]

    /// 他家打牌后各座位的响应。06 的 Ron、10 的 Pon / Chi、11 的 Daiminkan 从这里进合法动作集。
    /// **04 票没有任何可响应的动作**，所以恒为空——引擎因此不会停在响应阶段。
    let private responsesTo (_state: GameState) (_target: Seat) (_pai: Tile) : LegalActions list = []

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

    /// 终局的流局结果；还没终则为 None。05 的连庄判定读它的 `Tenpais`。
    let ryuukyoku (state: GameState) : Ryuukyoku option =
        match state.Phase with
        | Ended result -> Some result
        | AwaitingDahai _
        | AwaitingResponse _ -> None

    // ---- 构造 ----

    let private updatePlayer (seat: Seat) (change: PlayerState -> PlayerState) (players: PlayerState list) =
        players
        |> List.mapi (fun index player -> if index = seat then change player else player)

    let private awaitDahai (actor: Seat) (drawn: Tile) (players: PlayerState list) : Phase =
        let actions =
            match List.tryItem actor players with
            | None -> []
            | Some player -> awaitingDahaiActions actor player

        AwaitingDahai
            {
                Actor = actor
                Tsumo = drawn
                Actions = actions
            }

    /// 开一局：洗牌、配牌、Oya 摸第一张，得到一个等 Oya 打牌的局面。
    /// 同一种子、同一规则集、同一条件必然开出同一局（`KyokuStart.create` 的保证）。
    let start (ruleset: Ruleset) (context: KyokuContext) (rng: Rng) : Result<GameState * Rng, KyokuStartError> =
        KyokuStart.create ruleset context rng
        |> Result.map (fun (started, advanced) ->
            // Oya 的配牌里已经含它摸进的第一张，只需补上「刚摸进的是哪张」。
            let players =
                List.mapi2
                    (fun seat score hand ->
                        let drawn = if seat = context.Oya then Some started.Tsumo else None

                        PlayerState.ofHaipai score drawn hand)
                    context.Scores
                    started.Hands

            let state =
                {
                    Ruleset = ruleset
                    KindSet = TileKindSet.ofKinds ruleset.TileKinds
                    Context = context
                    Wall = started.Wall
                    Players = players
                    Flags =
                        {
                            Haitei = Wall.remaining started.Wall = 0
                            Houtei = false
                        }
                    Phase = awaitDahai context.Oya started.Tsumo players
                    Log = List.rev started.Events
                }

            state, advanced)

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
            Phase = Ended result
        },
        [ Event.Ryuukyoku result ]

    /// 打完牌之后：下家摸一张进入摸牌后阶段；可摸区空了就是荒牌流局。
    let private drawNext (state: GameState) (from: Seat) : GameState * Event list =
        match Wall.draw state.Wall with
        | None -> exhaustiveDraw state
        | Some(drawn, rest) ->
            let next = Seat.next state.Ruleset from
            let players = updatePlayer next (PlayerState.draw drawn) state.Players

            { state with
                Wall = rest
                Players = players
                Flags =
                    {
                        Haitei = Wall.remaining rest = 0
                        Houtei = false
                    }
                Phase = awaitDahai next drawn players
            },
            [ Event.Tsumo(next, drawn) ]

    let private applyDahai (state: GameState) (actor: Seat) (pai: Tile) (tsumogiri: bool) : GameState * Event list =
        let discarded =
            { state with
                Players = updatePlayer actor (PlayerState.discard pai) state.Players
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
                            }
                },
                []

        advanced, Event.Dahai(actor, pai, tsumogiri) :: events

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

    /// 引擎的唯一入口：给局面提交一个动作，得到新局面与本步产出的事件。
    /// 动作不合法时返回 `IllegalAction` 值（**不抛异常**），局面原样不动。
    ///
    /// 产出的事件同时已经追加进新局面的事件流（`GameState.events`）——两者是同一份事实。
    let step (state: GameState) (action: Action) : Result<GameState * Event list, IllegalAction> =
        let outcome =
            match state.Phase with
            | Ended _ -> Error KyokuAlreadyEnded
            | AwaitingResponse waiting ->
                // 04 票不会停在响应阶段（`responsesTo` 恒为空）。真停在这里时，
                // 等的是 `Responses` 里那些座位。
                Error(NotYourTurn(Action.actor action, waiting.Responses |> List.map (fun each -> each.Seat)))
            | AwaitingDahai waiting ->
                match action with
                | Action.Dahai(actor, pai, tsumogiri) ->
                    if not (Seat.isValid state.Ruleset actor) then
                        Error(SeatOutOfRange(actor, state.Ruleset.SeatCount))
                    elif actor <> waiting.Actor then
                        Error(NotYourTurn(actor, [ waiting.Actor ]))
                    else
                        match List.tryItem actor state.Players with
                        | None -> Error(SeatOutOfRange(actor, state.Ruleset.SeatCount))
                        | Some player ->
                            match rejectDahai actor player pai tsumogiri with
                            | Some illegal -> Error illegal
                            | None -> Ok(applyDahai state actor pai tsumogiri)

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
        | KyokuAlreadyEnded -> "这一局已经结束了，不再接受任何动作"
