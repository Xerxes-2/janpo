namespace Janpo

/// 上下文标志：需要「在某个时机置位、由判役时读取」的那几个标志（spec 的规则清单）。
/// **只置位，不判役**——役是 07 的事，四个字段与 `YakuContext` 的同名字段一一对应。
/// **每一次迁移都把四个标志重算一遍**：记录不能漏字段，因此忘了置位的地方编译器会当面指出来。
///
/// 一发**不在这里**：它是**逐座位**的（只有立直的那家有一发），因此记在 `PlayerState.Ippatsu`；
/// 这里只放全场共享的那几个。
type KyokuFlags =
    {
        /// 岭上：刚摸进的那张是杠之后从王牌补摸的岭上牌（岭上开花）。
        Rinshan: bool
        /// 海底：刚摸进的是可摸区的最后一张（海底摸月）。**岭上牌不是海底牌**。
        Haitei: bool
        /// 河底：刚打出的那张之后已经没有可摸的牌了（河底捞鱼）。
        Houtei: bool
        /// 抢杠：现在等的是对某家宣言的杠的响应（只能荣和）。
        Chankan: bool
    }

/// 摸牌后阶段：`Actor` 刚摸进一张，等它打牌（也可以自摸和、宣言立直、暗杠与加杠）。
/// **鸣牌之后也是这个阶段**：鸣牌跳过摸牌，直接由鸣的那家打牌；
/// **杠之后同样是这个阶段**，只不过杠要从王牌补摸一张，因此 `Tsumo` 非 None。
type AwaitingDahai =
    {
        /// 等这个座位出手。
        Actor: Seat
        /// 刚摸进的那张，摸切打的就是它；**鸣牌之后为 None**（没摸牌，因此那一手只能手切）。
        Tsumo: Tile option
        /// 这个阶段的合法动作集，非空。
        Actions: Action list
    }

/// 响应阶段是对什么的响应。**它同时决定两件事**：这一轮能做什么（`responsesTo`），
/// 以及没人荣和时接下去干什么（`applyResponse`）。
///
/// case 强制限定名（`ResponseCause.Dahai`）：`Action` / `Event` / `Phase` 里都有同名的东西（裁决 D-3）。
[<RequireQualifiedAccess>]
type ResponseCause =
    /// 他家打出了一张：可以荣和 / 碰 / 吃 / 大明杠；都不要就轮下家摸牌。
    | Dahai
    /// 某家宣言了杠，等抢杠：**只能荣和**（杠上鸣不了牌）；没人抢则这个杠成立。
    ///
    /// 带着的就是那个还没成立的副露——**宣言时不改局面**，因此被抢时无需回滚。
    | Kan of kan: Naki

/// 他家打牌后的响应阶段：`Target` 刚打出 `Pai`，等其他座位响应（Ron / Pon / Chi / Minkan）；
/// 抢杠时 `Target` 是宣言杠的那家、`Pai` 是被抢的那张，而能做的只剩荣和。
///
/// **多家是逐个答复、收齐后才裁决的**：`Responses` 是还没答复的那几家（答一家少一项），
/// `Declared` 收着已经宣言的那些动作。`Responses` 空了就裁决：先看 Ron（按 Atamahane），
/// 没人荣和才看鸣牌（`nakiRank`）——合起来就是 **Ron > Pon / Minkan > Chi**。
/// 抢杠那一轮只有 Ron：没人抢则那个杠成立（`ResponseCause.Kan`）。
type AwaitingResponse =
    {
        /// 打出这张牌（或宣言那个杠）的座位。荣和的 `target` 就是它。
        Target: Seat
        /// 刚打出的那张；抢杠时是被抢的那张（加杠是加上去的那张，暗杠是那四张里的一张）。
        Pai: Tile
        /// 这一轮响应是对什么的响应。
        Cause: ResponseCause
        /// 还没答复的座位各自的合法动作集，每项非空且必含一条「过」（`Action.None`），
        /// 按打牌者下家优先的顺序排列。
        Responses: LegalActions list
        /// 已经宣言的响应，按答复顺序；「过」不进这里。裁决时重新按优先级排，
        /// 因此「先被问到」不等于「优先」。
        Declared: Action list
    }

/// 一局怎么结束的。04 只有荒牌流局，06 把它换成了 DU；12 票补完其余流局形态时
/// 果然只给 `RyuukyokuReason` 加了 case，这个 DU 一字未动。
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
            /// 规则集。**牌种全集也在它身上**（`Ruleset.TileKinds : TileKindSet`，
            /// ADR-0004 决定 4），因此局面不再另存一份派生的牌种集合。
            Ruleset: Ruleset
            /// 开局时由上层给定的既定条件：场风、局数、本场、供托、亲与**局初**点数。
            Context: KyokuContext
            Wall: Wall
            /// 各家状态，按座位升序。
            Players: PlayerState list
            /// 场上堆着的立直棒**根数**：局初的供托加上这一局里成立的立直。
            /// 和了那一步归零（它进了和了者的 `Deltas`），流局时原样留着由 05 结转。
            Kyotaku: int
            /// **明杠欠着还没翻的新宝牌指示牌张数**（大明杠 / 加杠）。
            /// 暗杠当场翻，因此暗杠一张也不欠——两种时机的差别只在这一个字段上。
            ///
            /// **`completeKan` 置位、`applyDahai` 消费**：杠成立时加一，杠的那家打牌那一刻
            /// 一次翻光并归零。天凤就是这个时机（13 票在 218 局真实牌谱上实证），
            /// 于是「明杠之后当场岭上开花」压根翻不到这一张。
            PendingKanDora: int
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
    /// 食替：牌就在手里，但鸣完的那一手不允许打它——鸣进来的那张（现物），
    /// 以及吃的两张是两面搭子时顺子的另一端（筋）。
    | Kuikae of actor: Seat * pai: Tile
    /// 宣言和了的来源与牌与当前局面不符：自摸和的 `target` 必须是自己、`pai` 必须是刚摸进那张；
    /// 荣和的 `target` 必须是刚打牌那家、`pai` 必须是刚打出那张。
    | HoraTileMismatch of actor: Seat * target: Seat * pai: Tile
    /// 手牌加上这张不成和了型。**振听不走这里**，走 `RonWhileFuriten`。
    | NotAgari of actor: Seat * pai: Tile
    /// 振听：型成立、也有役，但这家现在不能荣和。
    ///
    /// 振听座位的 Ron 不进合法动作集，但**振听只挡荣和，不挡鸣牌**：
    /// 能碰能吃的振听座位照样被问到，它在那里宣言荣和就落在这一条。
    /// （压根没被问到的座位宣言荣和，得到的仍是 `NotYourTurn`。）
    | RonWhileFuriten of actor: Seat * pai: Tile
    /// 型成立但一个役都没有：无役不可和。宝牌不是役，救不了。
    ///
    /// 与 `YakuError.NoYaku` 是同一件事在两层的投影。`YakuError` 已限定（`RequireQualifiedAccess`），
    /// 因此不限定的 `NoYaku` 恒指本 case——它与 `NotYourTurn` / `NotInHand` 一样是**拒绝理由**。
    | NoYaku of actor: Seat * pai: Tile
    /// 宣言鸣牌的来源与牌与当前局面不符：`target` 必须是刚打牌那家、`pai` 必须是刚打出那张。
    | NakiTileMismatch of actor: Seat * target: Seat * pai: Tile
    /// 这几张牌此刻鸣不成：手里没这几张、凑不成刻子 / 顺子、吃的不是上家打的、
    /// 河底牌不能鸣，或者鸣完之后没牌可打（禁食替把唯一的那几张都封了）。
    /// 大明杠也落在这一条：它与碰同是对别家打出那张的响应。
    | CannotNaki of actor: Seat * kind: NakiKind * pai: Tile * consumed: Tile list
    /// 此刻杠不了（**暗杠与加杠**这两种自家宣言的杠）：手里不够四张 / 没碰过那种牌、
    /// 不是刚摸完牌的那一手、牌山空了、岭上牌用完了，或者立直把它封住了
    /// （送り杠 / 听牌变了 / 那一定不是暗刻，判据在 `RiichiState.allowsAnkan`）。
    ///
    /// 与 `CannotNaki` 分开：自家宣言的杠没有「被鸣的那张」也没有对方座位，
    /// 强塑进 `CannotNaki` 得编一个不存在的 `pai` 出来。
    | CannotKan of actor: Seat * kind: NakiKind * consumed: Tile list
    /// 此刻立直不了：不门清、没听牌（打不出保持听牌的那张）、点数不够一根立直棒、
    /// 牌山剩的牌不够再摸一圈，或者已经立过直了。四条判据见 `RiichiState.canDeclare`。
    | CannotRiichi of actor: Seat
    /// 立直把这一手封住了，三条各封一面：**立直后只能摸切**；宣言牌那一手只能打
    /// 「打完仍听牌」的那几张；而宣言与打出宣言牌之间**连自摸和也不许**（要和就别宣言）。
    ///
    /// 与 `Kuikae` 分开：两者都是「牌就在手里却动不得」，但原因不同，
    /// 合成一条会把立直报成食替。
    | RiichiRestricted of actor: Seat * pai: Tile
    /// 此刻宣言不了九种九牌：不是第一巡、此前有人鸣牌、手里的幺九牌不够
    /// `Ruleset.KyuushuKinds` 种，或者立直宣言之后又回头宣它。判据在 `Ryuukyoku.canDeclareKyuushuKyuuhai`。
    ///
    /// **其余五种流局没有对应的拒绝理由**：它们不是选手提交的意图，由引擎自己判。
    | CannotRyuukyoku of actor: Seat
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
    ///
    /// **途中流局一律连庄**（亲不流，通行规则）：那几种压根不验听牌，`Tenpais` 记的是
    /// 「谁亮了手牌」而不是「谁听牌」，拿它判连庄会把九种九牌判成进局。
    /// 荒牌流局与流し満貫仍然看 Oya 听不听牌（票 12：流し満貫时 Oya 听牌则连庄）。
    let isRenchan (oya: Seat) (kyokuEnd: KyokuEnd) : bool =
        match kyokuEnd with
        | KyokuEnd.Hora horas -> horas |> List.exists (fun hora -> hora.Actor = oya)
        | KyokuEnd.Ryuukyoku result ->
            RyuukyokuReason.isAbortive result.Reason
            || Seat.tryItem oya result.Tenpais |> Option.defaultValue false

/// 局面的构造、拆解与迁移。`step` 是引擎唯一的入口：
/// `GameState -> Action -> Result<GameState * Event list, IllegalAction>`。
[<RequireQualifiedAccess>]
module GameState =

    // ---- 判役与算点的上下文 ----

    /// 这家还没打过牌、且此前无人鸣牌。**三处读它**：天和 / 地和（`yakuContextOf`）、
    /// 两立直（`applyRiichi`）与九种九牌（`awaitDahaiIn`）——「无人鸣牌的第一巡」
    /// 是同一条判据，不写三份。
    /// （事件流的顺序与这个判断无关，因此传倒序的也行。）
    let private firstTurnFor (seat: Seat) (log: Event list) : bool =
        log
        |> List.forall (fun event ->
            match event with
            | Dahai(actor, _, _) -> actor <> seat
            // 任何人鸣牌都把天和 / 地和与两立直打掉：它们要求「无人鸣牌的第一巡」。
            // **三种杠都算鸣牌**，暗杠也不例外（通行规则）。
            | Pon _
            | Chi _
            | Ankan _
            | Kakan _
            | Minkan _ -> false
            // 别家宣言立直不打断第一巡（它自己的那条 `dahai` 才打断它自己的）；
            // 翻新宝牌更不是谁的手。
            | Dora _
            | Riichi _
            | RiichiAccepted _
            | StartGame _
            | StartKyoku _
            | Tsumo _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | EndGame -> true)

    /// 判役要的上下文，由局面自己填：场风取自开局条件，自风由座位与亲推出，
    /// 宝牌指示牌取自牌山，海底 / 河底取自标志，立直与一发取自那一家的 `PlayerState`，
    /// 天和 / 地和由「这家还没打过牌」推出——都是牌局历史的产物，
    /// 因此**调用方不必也不该自己拼一份**。
    ///
    /// 这里填的标志都还要过判役那关：海底只对自摸生效、河底只对荣和生效、
    /// 一发要门清且真立了直、天和地和还要门清自摸（`Yaku` 自己会判），
    /// 所以这里不按自摸 / 荣和分两套。岭上与抢杠同理：它们只是 `KyokuFlags` 的搬运。
    let private yakuContextOf
        (ruleset: Ruleset)
        (kyokuContext: KyokuContext)
        (wall: Wall)
        (flags: KyokuFlags)
        (players: PlayerState list)
        (log: Event list)
        (seat: Seat)
        : YakuContext =
        let firstTurn = firstTurnFor seat log
        let player = Seat.tryItem seat players

        { YakuContext.create kyokuContext.Bakaze (Seat.jikaze ruleset kyokuContext.Oya seat) with
            // 立直与一发都是那一家自己的状态（`PlayerState`），局面照搬，不复算。
            Riichi =
                player
                |> Option.map (PlayerState.riichi >> RiichiState.declaration)
                |> Option.defaultValue RiichiDeclaration.None
            Ippatsu = player |> Option.map PlayerState.ippatsu |> Option.defaultValue false
            Rinshan = flags.Rinshan
            Haitei = flags.Haitei
            Houtei = flags.Houtei
            Chankan = flags.Chankan
            Tenhou = firstTurn && seat = kyokuContext.Oya
            Chiihou = firstTurn && seat <> kyokuContext.Oya
            DoraMarkers = Wall.doraIndicators wall
            UraDoraMarkers = Wall.uraIndicators wall
        }

    let private yakuContext (state: GameState) (seat: Seat) : YakuContext =
        yakuContextOf state.Ruleset state.Context state.Wall state.Flags state.Players state.Log seat

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
        | Error _ -> Error YakuError.NoAgariShape
        | Ok hand -> Score.best ruleset context hand

    // ---- 合法动作集 ----

    // ---- 食替（鸣完那一手的禁手） ----

    /// 鸣完之后**不能马上打出的牌种**（去红），又叫食替：
    ///
    /// - 现物食替：鸣进来的那张立刻又打出去，碰与吃都禁；
    /// - 筋食替：吃的两张是两面搭子时，另一端也禁（`4m 5m` 吃 `3m` 之后不能打 `6m`）。
    ///
    /// 两者都禁是天凤与雀魂的做法，本项目从严且不可配（DECISIONS 10-A）。
    /// 碰的两张同种，自然凑不成两面搭子，因此碰与吃共用这一份判据。
    let private kuikaeKinds (taken: Tile) (consumed: Tile list) : Tile list =
        let index = Tile.kindIndex taken

        let otherEnd =
            match consumed |> List.map Tile.kindIndex |> List.sort with
            // 两张连号且同花色（同一个 9 张区间）才是两面搭子；字牌的 `kindIndex` 在 27 以上。
            | [ low; high ] when high = low + 1 && low < 27 && low / 9 = high / 9 ->
                if index = low - 1 then Tile.ofKindIndex (high + 1)
                elif index = high + 1 then Tile.ofKindIndex (low - 1)
                else None
                // 另一端跌出花色（`8m 9m` 吃 `7m`）时没有筋食替。
                |> Option.filter (fun tile -> Tile.kindIndex tile / 9 = low / 9)
            | _ -> None

        Tile.deaka taken :: Option.toList otherEnd

    /// 打得出去的牌种：手里除去被食替封住的那几种。没鸣牌时 `forbidden` 为空，整手牌都能打。
    let private allowedDahai (forbidden: Tile list) (tiles: Tile list) : Tile list =
        tiles
        |> List.filter (fun tile -> not (List.contains (Tile.deaka tile) forbidden))

    /// 从牌列里逐张拿掉给定的那几张（按实例；红 5 与正牌各算各的）。拿不到的那张跳过。
    let private removeEach (tiles: Tile list) (from: Tile list) : Tile list =
        (from, tiles)
        ||> List.fold (fun rest tile ->
            match List.tryFindIndex ((=) tile) rest with
            | Some index -> List.take index rest @ List.skip (index + 1) rest
            | None -> rest)

    // ---- 合法动作集 ----

    /// 这一局已经成立的杠数（拆解器 `kanCount` 读的是同一份）。
    let private kanCountIn (players: PlayerState list) : int =
        players |> List.sumBy PlayerState.kanCount

    /// 此刻杠不杠得起：岭上牌还没用完（`Ruleset.RinshanCount` 就是一局最多能杠几次），
    /// 且可摸区还有牌能补进王牌（河底牌杠不得，见 `Wall.drawRinshan`）。
    ///
    /// **四杠散了是 12 票的**：本票只守住王牌的物理上限，不判流局。
    let private canKan (state: GameState) : bool =
        Wall.remaining state.Wall > 0
        && kanCountIn state.Players < state.Ruleset.RinshanCount

    /// 手里暗杠得了的那几种牌：（去红后的牌种，要亮出的那四张），按 mjai 顺序升序。
    /// 四张里含红 5 时红 5 也一并亮出去（四张全上，没有另一种亮法）。
    let private ankanCandidates (hand: Tile list) : (Tile * Tile list) list =
        hand
        |> List.map Tile.deaka
        |> List.distinct
        |> Tile.sort
        |> List.choose (fun kind ->
            match hand |> List.filter (fun tile -> Tile.deaka tile = kind) with
            | [ _; _; _; _ ] as four -> Some(kind, Tile.sort four)
            | _ -> None)

    /// 加得了杠的那几条：（从手里加上去的那张，底下那组碰）。
    /// 一种牌只有四张、碰用掉三张，因此**每组碰至多对应一条加杠**，不存在两种亮法。
    let private kakanCandidates (naki: Naki list) (hand: Tile list) : (Tile * Naki) list =
        naki
        |> List.filter (fun each -> Naki.kind each = NakiKind.Pon)
        |> List.choose (fun pon ->
            match Naki.tiles pon with
            | [] -> Option.None
            | head :: _ ->
                hand
                |> List.tryFind (fun tile -> Tile.kindIndex tile = Tile.kindIndex head)
                |> Option.map (fun added -> added, pon))

    /// 摸牌后阶段的合法动作集：自摸和（和了型成立**且有役**时）在最前，接着是立直宣言与两种杠，
    /// 随后是手切每一种牌各一条（同种牌只出现一次，但 `5m` 与 `5mr` 各算各的），
    /// 最后是摸切那一条。手切按 mjai 顺序升序。
    ///
    /// **暗杠与加杠只在自己摸完牌那一手宣言得了**（`drawn` 非 None）：鸣完那一手没摸牌，
    /// 杠不了——真实牌谱里 18/18 条 `ankan` / `kakan` 都紧跟在自家的 `tsumo` 之后。
    ///
    /// `forbidden` 是食替封住的牌种（`kuikaeKinds`），**只有鸣完的那一手非空**；
    /// 鸣牌不摸牌，因此那一手既没有摸切也没有自摸和。
    ///
    /// **自摸和不看振听**（振听只挡荣和），但**看役**：无役不可和。
    ///
    /// **立直把这份收窄两次**：
    /// - 宣言了还没落定（`Declared`）：只剩「打完仍听牌」的那几张，自摸和、杠与再宣言都没了；
    /// - 立直已成立（`Accepted`）：**只能摸切**，只剩自摸和、摸切与那一个例外——暗杠
    ///   （三条判据在 `RiichiState.allowsAnkan`，裁决 D-8）。
    ///   立直中的座位鸣不了牌（`responsesTo`），因此那两支里 `drawn` 恒非 None。
    let private awaitingDahaiActions
        (ruleset: Ruleset)
        (context: YakuContext)
        (remaining: int)
        (canKan: bool)
        (firstTurn: bool)
        (actor: Seat)
        (forbidden: Tile list)
        (player: PlayerState)
        : Action list =
        /// 打得出去的那几条：手切各牌种一条在前，摸切那一条在后，`allowed` 再筛一遍。
        let dahai (allowed: Tile -> bool) : Action list * Action list =
            let tedashi =
                PlayerState.tedashi player
                |> allowedDahai forbidden
                |> List.filter allowed
                |> Tile.sort
                |> List.distinct
                |> List.map (fun pai -> Action.Dahai(actor, pai, false))

            let tsumogiri =
                match PlayerState.drawn player with
                | Some drawn when allowed drawn -> [ Action.Dahai(actor, drawn, true) ]
                | Some _
                | None -> []

            tedashi, tsumogiri

        let hora =
            match PlayerState.drawn player with
            | None -> []
            | Some drawn ->
                match horaWith ruleset context player drawn true with
                | Ok _ -> [ Action.Hora(actor, actor, drawn) ]
                | Error _ -> []

        /// 暗杠：手里四张同种，且立直没把它封住。**立直后那三条判据在 `RiichiState.allowsAnkan`**
        /// （禁送り杠、听牌不变、每种读法里都作暗刻），本票只调用不重写（裁决 D-8）；
        /// 没立直时它恒为 true，因此「摸进的那张才能杠」这一层得在这里守（`drawn` 非 None）。
        let ankan =
            if not canKan || Option.isNone (PlayerState.drawn player) then
                []
            else
                ankanCandidates (PlayerState.hand player)
                |> List.filter (fun (kind, _) ->
                    RiichiState.allowsAnkan
                        ruleset.TileKinds
                        (PlayerState.riichi player)
                        (PlayerState.naki player)
                        (PlayerState.hand player)
                        (PlayerState.drawn player)
                        kind)
                |> List.map (fun (_, consumed) -> Action.Ankan(actor, consumed))

        /// 加杠：自己碰过的那种牌又摸进了第四张。**立直中加不了杠**（只有暗杠那一个例外）。
        let kakan =
            if
                not canKan
                || RiichiState.isActive (PlayerState.riichi player)
                || Option.isNone (PlayerState.drawn player)
            then
                []
            else
                kakanCandidates (PlayerState.naki player) (PlayerState.hand player)
                |> List.map (fun (added, pon) -> Action.Kakan(actor, added, Naki.tiles pon))

        /// 九种九牌：第一巡、此前无人鸣牌的那一次自摸，手里的幺九牌够种数。
        /// **它只出现在没立直那一支**：立直宣言之后那一手只能打宣言牌，
        /// 而立直成立之后早就不是第一巡了。`drawn` 为 None（鸣完那一手）时
        /// `firstTurn` 必定为 false，因此不必再判一次。
        let kyuushu =
            if Ryuukyoku.canDeclareKyuushuKyuuhai ruleset firstTurn (PlayerState.hand player) then
                [ Action.Ryuukyoku actor ]
            else
                []

        match PlayerState.riichi player with
        | RiichiState.Declared _ ->
            let keeps =
                RiichiState.tenpaiDahai ruleset.TileKinds (PlayerState.nakiCount player) (PlayerState.hand player)

            let tedashi, tsumogiri = dahai (fun pai -> List.contains (Tile.deaka pai) keeps)
            tedashi @ tsumogiri
        | RiichiState.Accepted _ ->
            let _, tsumogiri = dahai (fun _ -> true)
            hora @ ankan @ tsumogiri
        | RiichiState.None ->
            let riichi =
                if
                    RiichiState.canDeclare
                        ruleset
                        ruleset.TileKinds
                        remaining
                        (PlayerState.score player)
                        (PlayerState.naki player)
                        (PlayerState.hand player)
                then
                    [ Action.Riichi actor ]
                else
                    []

            let tedashi, tsumogiri = dahai (fun _ -> true)
            hora @ kyuushu @ riichi @ ankan @ kakan @ tedashi @ tsumogiri

    /// 从一堆牌里取两张的全部拿法（按实例，不去重）。
    let rec private pairsOf (tiles: Tile list) : Tile list list =
        match tiles with
        | [] -> []
        | head :: tail -> (tail |> List.map (fun other -> [ head; other ])) @ pairsOf tail

    /// 碰的全部亮法：手里同种的牌里任取两张。红 5 与正牌是不同的牌，
    /// 手里 `5m 5m 5mr` 碰 `5m` 因此有两种亮法；亮哪两张影响宝牌数，是选手的决策。
    let private ponConsumed (pai: Tile) (hand: Tile list) : Tile list list =
        hand
        |> List.filter (fun tile -> Tile.kindIndex tile = Tile.kindIndex pai)
        |> pairsOf
        |> List.map Tile.sort
        |> List.distinct

    /// 吃的全部亮法：`pai` 在顺子里占低 / 中 / 高位三种，每种再乘上红 5 的拿法。
    /// 字牌与跌出花色的组合自然排掉。
    let private chiConsumed (pai: Tile) (hand: Tile list) : Tile list list =
        let index = Tile.kindIndex pai

        if index >= 27 then
            []
        else
            [ [ 1; 2 ]; [ -1; 1 ]; [ -2; -1 ] ]
            |> List.collect (fun offsets ->
                let indexes = offsets |> List.map (fun offset -> index + offset)

                if
                    indexes
                    |> List.forall (fun each -> each >= 0 && each < 27 && each / 9 = index / 9)
                then
                    let picks =
                        indexes
                        |> List.map (fun each ->
                            hand |> List.filter (fun tile -> Tile.kindIndex tile = each) |> List.distinct)

                    match picks with
                    | [ lower; upper ] ->
                        [
                            for first in lower do
                                for second in upper -> Tile.sort [ first; second ]
                        ]
                    | _ -> []
                else
                    [])
            |> List.distinct

    /// 某家对刚打出的那张能做的鸣牌：碰与大明杠任何他家，吃只能吃上家。
    /// 碰与大明杠在吃前面，与裁决的优先级一致（`nakiRank`：两者同级）。
    ///
    /// **鸣完没牌可打的那些亮法不进合法动作集**：食替把手里剩的那几张全封住时（四副露
    /// 只剩两张时理论上碰得到），这一鸣就不合法——否则响应阶段会推进一个没有合法动作的局面。
    /// 大明杠不用过这一关：四张同种全进了副露，食替封不到手里，而且杠完还要补摸一张。
    let private nakiActionsFor
        (ruleset: Ruleset)
        (canKan: bool)
        (target: Seat)
        (pai: Tile)
        (seat: Seat)
        (player: PlayerState)
        : Action list =
        let hand = PlayerState.hand player

        let usable (consumed: Tile list) =
            hand
            |> removeEach consumed
            |> allowedDahai (kuikaeKinds pai consumed)
            |> List.isEmpty
            |> not

        let pon =
            ponConsumed pai hand
            |> List.filter usable
            |> List.map (fun consumed -> Action.Pon(seat, target, pai, consumed))

        /// 大明杠：手里三张同种全亮出去（含红 5）。得了三张就只有这一种亮法。
        let minkan =
            match hand |> List.filter (fun tile -> Tile.kindIndex tile = Tile.kindIndex pai) with
            | [ _; _; _ ] as three when canKan -> [ Action.Minkan(seat, target, pai, Tile.sort three) ]
            | _ -> []

        let chi =
            // 只有下家能吃，因此被吃那家恒是宣言者的上家。
            if Seat.kamicha ruleset seat = target then
                chiConsumed pai hand
                |> List.filter usable
                |> List.map (fun consumed -> Action.Chi(seat, target, pai, consumed))
            else
                []

        pon @ minkan @ chi

    /// 他家打牌（或宣言杠）后各座位的响应，按**打牌者下家优先**的座位顺序排列；
    /// 同一座位的多条按 Ron → Pon / Minkan → Chi →「过」排，与裁决的优先级一致。
    ///
    /// 五条纪律：
    /// 1. **振听的座位拿不到 Ron**（而不是宣言之后再报错）——但振听只挡荣和，
    ///    能碰能吃的振听座位照样进这个列表，只是它那份里没有 Ron；
    /// 2. **无役的座位同样拿不到 Ron**：型成立不等于能和（`horaWith` 就是那条判据）；
    /// 3. **河底牌不能鸣**：可摸区空了之后只剩荣和（鸣完无牌可摸，这一局到此为止）；
    /// 4. **有任何响应就必须同时给一条「过」**，否则响应阶段停在那里没人推得走；
    /// 5. **抢杠只能荣和**（`ResponseCause.Kan`）：杠上鸣不了牌，而且**暗杠只有国士抢得了**，
    ///    还得规则集开着 `KokushiAnkanChankan`（天凤禁、雀魂允）。
    ///
    /// **立直中的座位鸣不了牌**（立直后不能碰不能吃，大明杠同理），但它仍然能荣和（含抢杠）。
    let private responsesTo (state: GameState) (cause: ResponseCause) (target: Seat) (pai: Tile) : LegalActions list =
        // 河底牌（可摸区已空）只能荣和，鸣不得。
        let canNaki = Wall.remaining state.Wall > 0

        /// 抢得了这个杠吗：加杠任何牌型都抢得，暗杠只有国士且规则集允许时抢得。
        let robbable (reading: HoraReading) =
            match cause with
            | ResponseCause.Dahai -> true
            | ResponseCause.Kan kan ->
                match Naki.kind kan with
                | NakiKind.Ankan -> state.Ruleset.KokushiAnkanChankan && reading.Tally.Shape = Kokushi
                | NakiKind.Pon
                | NakiKind.Chi
                | NakiKind.Minkan
                | NakiKind.Kakan -> true

        Seat.orderAfter state.Ruleset target
        |> List.filter (fun seat -> seat <> target)
        |> List.choose (fun seat ->
            match Seat.tryItem seat state.Players with
            | Option.None -> Option.None
            | Some player ->
                let canRon =
                    not (Furiten.blocksRon (PlayerState.furiten player))
                    && (match horaWith state.Ruleset (yakuContext state seat) player pai false with
                        | Ok reading -> robbable reading
                        | Error _ -> false)

                let ron = if canRon then [ Action.Hora(seat, target, pai) ] else []

                let naki =
                    match cause with
                    // 杠上鸣不了牌：抢杠那一轮只有荣和与「过」。
                    | ResponseCause.Kan _ -> []
                    | ResponseCause.Dahai ->
                        if canNaki && not (RiichiState.isActive (PlayerState.riichi player)) then
                            nakiActionsFor state.Ruleset (canKan state) target pai seat player
                        else
                            []

                match ron @ naki with
                | [] -> Option.None
                | actions ->
                    Some
                        {
                            Seat = seat
                            Actions = actions @ [ Action.None seat ]
                        })

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
    let player (seat: Seat) (state: GameState) : PlayerState option = Seat.tryItem seat state.Players

    /// 本局已经成立的杠数（三种杠合计）。上限是 `Ruleset.RinshanCount`：岭上牌有几张
    /// 就最多杠几次，合法动作集读的就是这一条。
    ///
    /// **12 票的四杠散了读它**：全场四个杠且不是同一家杠的就流局，
    /// 「是不是同一家」读逐家的那一份（`PlayerState.kanCount`）。本票不判流局。
    let kanCount (state: GameState) : int = kanCountIn state.Players

    /// 当前各家点数，按座位升序。荒牌流局授受之后这里就是授受后的点数。
    let scores (state: GameState) : int list =
        state.Players |> List.map PlayerState.score

    /// 场上堆着的立直棒**根数**（点数要乘 `Ruleset.RiichiBou`）：
    /// 局初的供托加上这一局里成立的立直，和了后为 0（都归了和了者）。
    ///
    /// **跨局结转读的是它**（`Game.advance`）：局初的 `KyokuContext.Kyotaku` 不含
    /// 这一局里打出去的立直棒。
    let kyotaku (state: GameState) : int = state.Kyotaku

    /// 当前阶段。
    let phase (state: GameState) : Phase = state.Phase

    /// 上下文标志（海底 / 河底）。判役的票读它，本票只负责置位。
    let flags (state: GameState) : KyokuFlags = state.Flags

    /// 本局已产出的事件流，按产出顺序。这就是这一局的 Paifu（ADR-0002）。
    let events (state: GameState) : Event list = List.rev state.Log

    /// 某座位打到第几巡（CONTEXT.md 的 Junme）：它自己摸过几次牌。
    ///
    /// 有副露的局里「四家各打一张为一巡」不再齐步，因此巡只能按各家自己的摸牌数算：
    /// **鸣牌不摸牌，鸣的那家 Junme 不涨**（它只是把出手的时机提前了），
    /// **被跳过的那几家更不涨**（它们连牌都没摸到）。
    let junme (seat: Seat) (state: GameState) : int =
        state.Log
        |> List.filter (fun event ->
            match event with
            | Tsumo(actor, _) -> actor = seat
            | StartGame _
            | StartKyoku _
            | Dahai _
            | Pon _
            | Chi _
            | Ankan _
            | Kakan _
            | Minkan _
            | Dora _
            | Riichi _
            | RiichiAccepted _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | EndGame -> false)
        |> List.length

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
    /// 反过来不恒成立：立直宣言了还没打宣言牌的那一手，这里照算，但 Hora 不在动作集里。
    let horaOf (seat: Seat) (state: GameState) : Result<HoraReading, YakuError> =
        match Seat.tryItem seat state.Players, state.Phase with
        | Some player, AwaitingDahai phase when phase.Actor = seat ->
            match PlayerState.drawn player with
            | Some drawn -> horaWith state.Ruleset (yakuContext state seat) player drawn true
            | Option.None -> Error YakuError.NoAgariShape
        | Some player, AwaitingResponse phase when phase.Target <> seat ->
            horaWith state.Ruleset (yakuContext state seat) player phase.Pai false
        | _ -> Error YakuError.NoAgariShape

    /// 终局的和了；不是和了收尾（或还没终）则为空列表。头跳关掉时的双响会有两条。
    let horas (state: GameState) : Hora list =
        match kyokuEnd state with
        | Some(KyokuEnd.Hora horas) -> horas
        | Some(KyokuEnd.Ryuukyoku _)
        | None -> []

    // ---- 构造 ----

    let private updatePlayer (seat: Seat) (change: PlayerState -> PlayerState) (players: PlayerState list) =
        Seat.mapAt seat change players

    /// 构造摸牌后阶段。`drawn` 为 None 就是鸣牌后那一手（不摸牌），
    /// `forbidden` 是那一手被食替封住的牌种，其余时候恒为空。
    ///
    /// 它读的是**已经立起来的局面**：合法动作集要看点数（立直得起 1000）、
    /// 牌山剩多少（立直要够再摸一圈）与立直状态，散着传参早晚漏一项。
    let private awaitDahaiIn (state: GameState) (actor: Seat) (drawn: Tile option) (forbidden: Tile list) : Phase =
        let actions =
            match Seat.tryItem actor state.Players with
            | None -> []
            | Some player ->
                awaitingDahaiActions
                    state.Ruleset
                    (yakuContext state actor)
                    (Wall.remaining state.Wall)
                    (canKan state)
                    (firstTurnFor actor state.Log)
                    actor
                    forbidden
                    player

        AwaitingDahai
            {
                Actor = actor
                Tsumo = drawn
                Actions = actions
            }

    let private ofStarted (ruleset: Ruleset) (context: KyokuContext) (started: KyokuStart) : GameState =
        // Oya 的配牌里已经含它摸进的第一张，只需补上「刚摸进的是哪张」。
        let players =
            List.map2
                (fun (seat, score) hand ->
                    let drawn = if seat = context.Oya then Some started.Tsumo else None

                    PlayerState.ofHaipai score drawn hand)
                (Seat.indexed context.Scores)
                started.Hands

        let flags =
            {
                Rinshan = false
                Haitei = Wall.remaining started.Wall = 0
                Houtei = false
                Chankan = false
            }

        // 先摆一个空动作集的同形阶段，再由 `awaitDahaiIn` 用完整的局面把它算出来——
        // 合法动作集要读点数、牌山与立直状态，非得等局面立起来不可。
        let opening =
            {
                Ruleset = ruleset
                Context = context
                Wall = started.Wall
                Players = players
                Kyotaku = context.Kyotaku
                PendingKanDora = 0
                Flags = flags
                Phase =
                    AwaitingDahai
                        {
                            Actor = context.Oya
                            Tsumo = Some started.Tsumo
                            Actions = []
                        }
                Log = List.rev started.Events
            }

        { opening with
            Phase = awaitDahaiIn opening context.Oya (Some started.Tsumo) []
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

    /// 流局收尾：授受、记下结果、一局告终。**七种形态共用这一处**（荒牌、流し満貫与五种途中）：
    /// 形态不同的只有 `reason` / `tenpais` / `deltas` 三个值，怎么结束是同一件事。
    ///
    /// **供托原样留在场上**（`Kyotaku` 不动）：无人和了就没人收立直棒，结转到下一局是 05 的事。
    let private endWithRyuukyoku
        (state: GameState)
        (reason: RyuukyokuReason)
        (tenpais: bool list)
        (deltas: int list)
        : GameState * Event list =
        let settled = List.map2 PlayerState.addScore deltas state.Players

        let result =
            {
                Reason = reason
                Tenpais = tenpais
                Deltas = deltas
                Scores = settled |> List.map PlayerState.score
            }

        { state with
            Players = settled
            Phase = Ended(KyokuEnd.Ryuukyoku result)
        },
        [ Event.Ryuukyoku result ]

    /// 途中流局收尾：**一律不授受**（听牌料不存在），`tenpais` 只记亮了手牌的那几家
    /// （`Ryuukyoku.revealedBy`）。`declarers` 只有三家和了用得上，其余形态传空表。
    let private endWithAbortive
        (state: GameState)
        (reason: RyuukyokuReason)
        (declarers: Seat list)
        : GameState * Event list =
        let revealed = Ryuukyoku.revealedBy state.Ruleset reason declarers

        let tenpais =
            Seat.all state.Ruleset |> List.map (fun seat -> List.contains seat revealed)

        endWithRyuukyoku state reason tenpais (Ryuukyoku.noDeltas state.Ruleset)

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
    ///
    /// **流し満貫成立时它的清算替代听牌料清算**（不叠加，票 12），形态也跟着从
    /// `Fanpai` 变成 `NagashiMangan`。**听牌与否照算**：它进 `tenpais` 上 wire，
    /// 且 05 的连庄判定（`KyokuEnd.isRenchan`）读的就是它——Oya 听牌则连庄。
    let private exhaustiveDraw (state: GameState) : GameState * Event list =
        let tenpais =
            state.Players |> List.map (PlayerState.isTenpai state.Ruleset.TileKinds)

        let reason, deltas =
            match Ryuukyoku.nagashiSeats state.Players with
            | [] -> Fanpai, notenBappu state.Ruleset tenpais
            | nagashi -> NagashiMangan, Ryuukyoku.nagashiDeltas state.Ruleset state.Context.Oya nagashi

        endWithRyuukyoku state reason tenpais deltas

    /// 三元牌（白发中）与风牌（东南西北）。`Yaku` 与 `Fu` 里各有一份同样的私有谓词：
    /// 这几个一行函数不值得让一层去依赖另一层的内部（见 DECISIONS 08 的同一取舍）。
    let private isSangenpai (tile: Tile) : bool =
        Tile.suit tile = Jihai && Tile.number tile >= 5

    let private isKazehai (tile: Tile) : bool =
        Tile.suit tile = Jihai && Tile.number tile <= 4

    /// 这次和了的责任支付者（Sekinin Barai，CONTEXT.md）；没人负责则为 None。
    /// 范围按天凤：**大明杠后的岭上开花**与**大三元 / 大四喜由副露凑齐**，四杠子没有。
    ///
    /// - 大明杠：喂杠的那家为这一杠补摸的岭上牌负责，只在**杠完立刻自摸和**那一下成立
    ///   （`Rinshan` 亮着且最后一组副露就是那个大明杠）；打出一张之后这份责任就没了。
    /// - 大三元 / 大四喜：让第三组三元牌 / 第四组风牌**在牌桌上亮出来**的那一家负责。
    ///   手里的暗刻不算（它没亮出来），自己摸来的暗杠也不算（`Naki.target` 为 None）。
    let private sekininOf (state: GameState) (actor: Seat) (tsumo: bool) (reading: HoraReading) : Seat option =
        let naki =
            Seat.tryItem actor state.Players
            |> Option.map PlayerState.naki
            |> Option.defaultValue []

        let yaku = reading.Tally.Yaku |> List.map fst

        /// 让第 `count` 组这一类牌亮出来的那家。副露按鸣的先后排，加杠是原地换掉那组碰，
        /// 因此这个序列就是「第几组亮出来的」的先后。
        let completedBy (count: int) (predicate: Tile -> bool) : Seat option =
            naki
            |> List.filter (fun each -> Naki.tiles each |> List.forall predicate)
            |> List.tryItem (count - 1)
            |> Option.bind Naki.target

        /// 这一摸的岭上牌是**哪个杠**补来的：倒序事件流里最近的那条杠事件就是它。
        /// **不能拿「最后一组副露」代替**：加杠是原地换掉那组碰，排在它后面的副露仍旧靠后。
        let lastKan =
            state.Log
            |> List.tryPick (fun event ->
                match event with
                | Ankan _
                | Kakan _
                | Minkan _ -> Some event
                | StartGame _
                | StartKyoku _
                | Tsumo _
                | Dahai _
                | Pon _
                | Chi _
                | Dora _
                | Riichi _
                | RiichiAccepted _
                | Hora _
                | Ryuukyoku _
                | EndKyoku
                | EndGame -> Option.None)

        let rinshan =
            if tsumo && state.Flags.Rinshan then
                match lastKan with
                | Some(Minkan(kanActor, target, _, _)) when kanActor = actor -> Some target
                // 暗杠 / 加杠的那张是自己摸来的，不算谁的责任。
                | Some _
                | Option.None -> Option.None
            else
                Option.None

        match rinshan with
        | Some liable -> Some liable
        | Option.None ->
            if List.contains Yaku.Daisangen yaku then
                completedBy 3 isSangenpai
            elif List.contains Yaku.Daisuushii yaku then
                completedBy 4 isKazehai
            else
                Option.None

    /// 和了：算符与点数、按 Oya / Ko 与自摸 / 荣和授受，一局告终。
    ///
    /// `wins` 已经按打牌者下家优先排好（`ronWinners`），**本场与供托只归排在最前的那一家**；
    /// 双响时两条 `Hora` 的 `Scores` 是**逐条累加**的，最后一条就是这一局的最终点数。
    ///
    /// 供托进了和了者的 `Deltas`，因此一次和了的增减之和 = 供托点数而不是 0；
    /// 收走的是**此刻场上的**供托（`state.Kyotaku`），含这一局里刚打出去的立直棒。
    let private applyHora (state: GameState) (target: Seat) (wins: (Seat * Tile) list) : GameState * Event list =
        let settle (players: PlayerState list, horas: Hora list) (index: int, (actor: Seat, pai: Tile)) =
            let value, sekinin =
                match horaOf actor state with
                | Ok reading -> reading.Value, sekininOf state actor (actor = target) reading
                // 走不到：Hora 进得了合法动作集就一定有役。不抛异常，记 0 符 0 番。
                | Error _ -> { Fu = 0; Han = 0; Yakuman = 0 }, Option.None

            let score =
                Score.hora
                    state.Ruleset
                    {
                        Actor = actor
                        Target = target
                        Oya = state.Context.Oya
                        Honba = if index = 0 then state.Context.Honba else 0
                        Kyotaku = if index = 0 then state.Kyotaku else 0
                        Sekinin = sekinin
                    }
                    value

            let settled = List.map2 PlayerState.addScore score.Deltas players

            // 里宝牌只在和了者立了直时翻（`Yaku` 也只在那时计它的番）。
            let uradora =
                match Seat.tryItem actor state.Players with
                | Some player when RiichiState.isAccepted (PlayerState.riichi player) -> Wall.uraIndicators state.Wall
                | Some _
                | None -> []

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
                    UraDoraMarkers = uradora
                }
            ]

        let settled, horas = List.indexed wins |> List.fold settle (state.Players, [])

        { state with
            // 宣言牌被荣和的那家立直不成立（立直棒不出）：这里把还没落定的宣言作废。
            Players = settled |> List.map PlayerState.cancelRiichi
            // 供托已经进了和了者的 `Deltas`，场上不再剩立直棒。
            Kyotaku = 0
            Phase = Ended(KyokuEnd.Hora horas)
        },
        horas |> List.map Event.Hora

    /// **一发的打断入口，全局只有这一处**：任何鸣牌（碰 / 吃与三种杠）都把全场的一发打掉。
    /// 鸣的那家自己也算在内：碰吃的那家既然能鸣就没立直，而**立直后自己暗杠同样把自己的一发打掉**。
    ///
    /// 时机在**鸣牌成立**而不是宣言：没鸣成的宣言（被优先级刷掉的那几家）不打断一发。
    /// 立直宣言牌被碰的那一手也在这里：先 `acceptRiichi` 亮起一发，再被这一手打掉。
    let private interruptIppatsu (state: GameState) : GameState =
        { state with
            Players = state.Players |> List.map PlayerState.clearIppatsu
        }

    /// **立直宣言牌落定**：没人荣和它，立直这时才成立——立直棒出、供托 +1、一发亮起，
    /// 产出 mjai 的 `reach_accepted`。
    ///
    /// 宣言牌被荣和时走不到这里（那条路是 `applyHora`）：立直不成立、立直棒不出，
    /// 与真实牌谱里「只有 `reach` 没有 `reach_accepted`」的那几局同形。
    let private acceptRiichi (state: GameState) : GameState * Event list =
        let declared =
            state.Players
            |> Seat.indexed
            |> List.filter (fun (_, player) -> RiichiState.isDeclared (PlayerState.riichi player))
            |> List.map fst

        if List.isEmpty declared then
            state, []
        else
            let settled =
                (state.Players, declared)
                ||> List.fold (fun players seat ->
                    players
                    |> updatePlayer seat (PlayerState.acceptRiichi >> PlayerState.addScore (-state.Ruleset.RiichiBou)))

            { state with
                Players = settled
                Kyotaku = state.Kyotaku + List.length declared
            },
            declared |> List.map Event.RiichiAccepted

    /// 打完牌之后：下家摸一张进入摸牌后阶段；可摸区空了就是荒牌流局。
    let private drawNext (state: GameState) (from: Seat) : GameState * Event list =
        match Wall.draw state.Wall with
        | None -> exhaustiveDraw state
        | Some(drawn, rest) ->
            let next = Seat.shimocha state.Ruleset from
            let players = updatePlayer next (PlayerState.draw drawn) state.Players

            let advanced =
                { state with
                    Wall = rest
                    Players = players
                    Flags =
                        {
                            Rinshan = false
                            Haitei = Wall.remaining rest = 0
                            Houtei = false
                            Chankan = false
                        }
                }

            { advanced with
                Phase = awaitDahaiIn advanced next (Some drawn) []
            },
            [ Event.Tsumo(next, drawn) ]

    /// **打牌落定之后的那一刻**：无人荣和、无人鸣走，本该轮下家摸牌。
    /// 先看三种途中流局（`Ryuukyoku.afterDahai`：四家立直 / 四风连打 / 四杠散了），
    /// 都不成立才摸牌（可摸区空了就是荒牌流局）。
    ///
    /// **时机就只能在这里**：三种都要等那一张牌没被荣和也没被鸣走（荣和优先于流局），
    /// 而四家立直还要等宣言牌落定（`acceptRiichi` 已经在两个调用方里跑过了）。
    let private afterDahai (state: GameState) (from: Seat) : GameState * Event list =
        match Ryuukyoku.afterDahai state.Ruleset state.Players with
        | Some reason -> endWithAbortive state reason []
        | None -> drawNext state from

    /// 把明杠欠着的那几张新宝牌指示牌翻出来，并把欠账归零。**只有打牌那一刻调得到**：
    /// 它就是「明杠等打牌才翻」这条规则本身（`PendingKanDora`）。
    ///
    /// 欠着不止一张只可能是「明杠之后没打牌就又杠一次」（岭上那张又成了杠）：
    /// 那时逐张翻出来，一个杠仍旧对应一张指示牌。
    let private revealPendingKanDora (state: GameState) : GameState * Event list =
        let flushed, revealing =
            ((state, []), [ 1 .. state.PendingKanDora ])
            ||> List.fold (fun (current, revealing) _ ->
                match Wall.reveal current.Wall with
                | Some(marker, wall) -> { current with Wall = wall }, revealing @ [ Event.Dora marker ]
                // 指示牌的叠数用完了：不产出 `dora` 事件（常规规则集里走不到，同 `completeKan`）。
                | Option.None -> current, revealing)

        { flushed with PendingKanDora = 0 }, revealing

    let private applyDahai (state: GameState) (actor: Seat) (pai: Tile) (tsumogiri: bool) : GameState * Event list =
        // 明杠欠着的新宝牌在**打牌这一刻**翻，且 `dora` 排在 `dahai` 之前（与牌谱同形）：
        // 这张牌被荣和时新宝牌照算，而杠完当场岭上开花时压根没走到这里。
        let flushed, revealing = revealPendingKanDora state

        let discarded =
            { flushed with
                // 打完这张，自家的听牌就变了，永久振听要按新的听牌与自己的河重算。
                Players =
                    flushed.Players
                    |> updatePlayer
                        actor
                        (PlayerState.discard pai >> PlayerState.refreshFuriten flushed.Ruleset.TileKinds)
                Flags =
                    {
                        Rinshan = false
                        Haitei = false
                        // 打出的这张之后再没有可摸的牌了，它就是河底那张。（翻指示牌不动可摸区。）
                        Houtei = Wall.remaining flushed.Wall = 0
                        Chankan = false
                    }
            }

        let advanced, events =
            match responsesTo discarded ResponseCause.Dahai actor pai with
            // 没人可响应 ⟹ 也不可能有人荣和这张：宣言牌当场落定，事件顺序是
            // `dahai` → `reach_accepted` → `tsumo`（真实牌谱就是这个顺序）。
            | [] ->
                let accepted, accepting = acceptRiichi discarded
                let advanced, drawing = afterDahai accepted actor
                advanced, accepting @ drawing
            | responses ->
                { discarded with
                    Phase =
                        AwaitingResponse
                            {
                                Target = actor
                                Pai = pai
                                Cause = ResponseCause.Dahai
                                Responses = responses
                                Declared = []
                            }
                },
                []

        advanced, revealing @ (Event.Dahai(actor, pai, tsumogiri) :: events)

    /// 还等着答复的座位。
    let private pendingSeats (waiting: AwaitingResponse) : Seat list =
        waiting.Responses |> List.map (fun each -> each.Seat)

    /// 某座位此刻能提交的响应。不在等它答复时是空列表。
    let private responseActions (waiting: AwaitingResponse) (actor: Seat) : Action list =
        waiting.Responses
        |> List.tryFind (fun each -> each.Seat = actor)
        |> Option.map (fun each -> each.Actions)
        |> Option.defaultValue []

    /// 鸣牌宣言的种类优先级，**小的优先**；不是鸣牌宣言时是 None。
    /// 碰与大明杠优于吃，且**两者同级**（规则上 Pon 与 Minkan 平级）——
    /// 同一张牌上碰与大明杠合起来至多一家（各家手里的同种牌加起来不够两组），
    /// 因此同级里不会撞上。暗杠与加杠不在这里：它们不是对别家那张牌的响应。
    let private nakiRank (action: Action) : int option =
        match action with
        | Action.Pon _
        | Action.Minkan _ -> Some 0
        | Action.Chi _ -> Some 1
        | Action.Dahai _
        | Action.Hora _
        | Action.Ankan _
        | Action.Kakan _
        | Action.Riichi _
        | Action.Ryuukyoku _
        | Action.None _ -> Option.None

    /// 多家响应的裁决顺序：**打牌者的下家优先**，与「先被问到」无关。
    /// Ron 先看（这里），没人荣和才轮得到鸣牌（`nakiWinner`）——合起来就是
    /// **Ron > Pon / Kan > Chi**。头跳开着时只取排在最前的那一家，关掉则双响 / 三响都成立。
    let private ronWinners (state: GameState) (target: Seat) (declared: Action list) : (Seat * Tile) list =
        let declarers =
            declared
            |> List.choose (fun action ->
                match action with
                | Action.Hora(actor, _, pai) -> Some(actor, pai)
                | Action.Dahai _
                | Action.Pon _
                | Action.Chi _
                | Action.Ankan _
                | Action.Kakan _
                | Action.Minkan _
                | Action.Riichi _
                | Action.Ryuukyoku _
                | Action.None _ -> Option.None)

        let ordered =
            Seat.orderAfter state.Ruleset target
            |> List.choose (fun seat -> declarers |> List.tryFind (fun (actor, _) -> actor = seat))

        if state.Ruleset.Atamahane then
            List.truncate 1 ordered
        else
            ordered

    /// 鸣牌的裁决：先按种类（Pon / Kan > Chi），同种类再按打牌者下家优先。
    /// **只有一家能鸣成**：那张牌就一张。
    let private nakiWinner (state: GameState) (target: Seat) (declared: Action list) : Action option =
        // 打牌者的下家排 0、再下家排 1……打牌者自己排最后（座位算术全在 `Seat`）。
        let seatRank (seat: Seat) =
            Seat.distanceFrom state.Ruleset (Seat.shimocha state.Ruleset target) seat

        declared
        |> List.choose (fun action ->
            nakiRank action
            |> Option.map (fun rank -> (rank, seatRank (Action.actor action)), action))
        |> List.sortBy fst
        |> List.tryHead
        |> Option.map snd

    /// 自家宣言的那个杠：暗杠 / 加杠对应的副露、**被抢的那张**与那条 mjai 事件。
    /// 不是自家宣言的杠（或副露构造不出来、找不到底下那组碰）时是 None：
    /// 走不到——进得了合法动作集的宣言必然构造得出。
    let private selfKanOf (player: PlayerState) (action: Action) : (Naki * Tile * Event) option =
        match action with
        | Action.Ankan(actor, consumed) ->
            match Naki.ankan consumed, consumed with
            // 暗杠被抢的那张：四张同种里的任一张（只有国士抢得了，而国士不含红 5）。
            | Ok naki, robbed :: _ -> Some(naki, robbed, Ankan(actor, consumed))
            | Ok _, []
            | Error _, _ -> Option.None
        | Action.Kakan(actor, pai, consumed) ->
            // 底下那组碰：加杠是把它原地换成杠，原碰的来源座位（责任支付要它）也跟着进去。
            PlayerState.naki player
            |> List.tryFind (fun each ->
                Naki.kind each = NakiKind.Pon
                && Naki.tiles each
                   |> List.forall (fun tile -> Tile.kindIndex tile = Tile.kindIndex pai))
            |> Option.bind (fun pon ->
                match Naki.kakan pai pon with
                | Ok naki -> Some(naki, pai, Kakan(actor, pai, consumed))
                | Error _ -> Option.None)
        | Action.Dahai _
        | Action.Hora _
        | Action.Pon _
        | Action.Chi _
        | Action.Minkan _
        | Action.Riichi _
        | Action.Ryuukyoku _
        | Action.None _ -> Option.None

    /// 一条鸣牌宣言里的：谁鸣的、鸣出的副露、以及对应的 mjai 事件。
    /// 不是鸣牌宣言时是 None；副露构造不出来时也是 None（走不到：进得了合法动作集的
    /// 宣言必然构造得出——`NakiError` 是值，这里不抛）。
    let private nakiOf (action: Action) : (Seat * Naki * Event) option =
        let built =
            match action with
            | Action.Pon(actor, target, pai, consumed) ->
                Some(actor, Naki.pon target pai consumed, Pon(actor, target, pai, consumed))
            | Action.Chi(actor, target, pai, consumed) ->
                Some(actor, Naki.chi target pai consumed, Chi(actor, target, pai, consumed))
            | Action.Minkan(actor, target, pai, consumed) ->
                Some(actor, Naki.minkan target pai consumed, Minkan(actor, target, pai, consumed))
            | Action.Dahai _
            | Action.Hora _
            | Action.Ankan _
            | Action.Kakan _
            | Action.Riichi _
            | Action.Ryuukyoku _
            | Action.None _ -> Option.None

        match built with
        | Some(actor, Ok naki, event) -> Some(actor, naki, event)
        | Some(_, Error _, _)
        | Option.None -> Option.None

    /// 杠成立之后：翻新宝牌指示牌与从王牌补摸一张岭上牌，随后由杠的那家打牌（杠完不禁食替：
    /// 那种牌四张全进了副露，根本打不出来）。
    ///
    /// **翻开的时机区分明杠与暗杠**（按天凤，13 票在 218 局真实牌谱上实证）：
    ///
    /// - 暗杠：`ankan` → `dora` → `tsumo`（**当场翻**，先翻再补摸）；
    /// - 明杠：`daiminkan` / `kakan` → `tsumo` → …… → `dora` → `dahai`
    ///   （**欠着**，等打牌那一刻才翻，见 `PendingKanDora` 与 `revealPendingKanDora`）。
    ///
    /// 事件流的顺序两者相同（`dora` 仍在 `dahai` 之前），差别在**哪一次 `step` 吐出它**：
    /// 明杠之后当场岭上开花时没有那一次打牌，因此新宝牌压根不翻。
    ///
    /// 补摸的那张亮起 `Rinshan` 标志（岭上开花）。**岭上牌不是海底牌**：海底往前挪一张，
    /// 由 `Wall.drawRinshan` 把可摸区的最后一张补进王牌来体现。
    let private completeKan (state: GameState) (actor: Seat) (concealed: bool) : GameState * Event list =
        let revealed, revealing =
            if concealed then
                match Wall.reveal state.Wall with
                | Some(marker, wall) -> { state with Wall = wall }, [ Event.Dora marker ]
                // 指示牌的叠数用完了：不产出 `dora` 事件，杠照样成立（可杠次数比叠数少，常规规则集里走不到）。
                | Option.None -> state, []
            else
                { state with
                    PendingKanDora = state.PendingKanDora + 1
                },
                []

        match Wall.drawRinshan revealed.Wall with
        // 补不到岭上牌：`canKan` 已经把这条路挡住了，走不到。
        // 仍然给一条推得动的出路（荒牌流局）而不抛异常。
        | Option.None -> exhaustiveDraw revealed
        | Some(drawn, wall) ->
            let drawing =
                { revealed with
                    Wall = wall
                    Players = updatePlayer actor (PlayerState.draw drawn) revealed.Players
                    Flags =
                        {
                            Rinshan = true
                            Haitei = false
                            Houtei = false
                            Chankan = false
                        }
                }

            { drawing with
                Phase = awaitDahaiIn drawing actor (Some drawn) []
            },
            // 明杠的 `revealing` 恒为空（那一张欠着），因此两种杠共用这一条。
            revealing @ [ Event.Tsumo(actor, drawn) ]

    /// 鸣牌成立：亮出的那几张离开暗牌进副露，被鸣的那张**仍留在打牌者的河里**
    /// （振听要看它），只给那家打上「河被鸣走过」的记号（Nagashi Mangan 的前提，12 票消费）。
    ///
    /// **鸣完跳过摸牌，直接由鸣的那家打牌**，且那一手禁食替（`kuikaeKinds`）。
    /// 宣言不是鸣牌（或副露构造不出来，走不到）时是 None，由调用方退回「没人鸣」那条路。
    let private applyNaki (state: GameState) (target: Seat) (declaration: Action) : (GameState * Event list) option =
        nakiOf declaration
        |> Option.map (fun (actor, naki, event) ->
            let players =
                state.Players
                |> updatePlayer actor (PlayerState.addNaki naki)
                |> updatePlayer target PlayerState.markKawaTaken

            let forbidden =
                match Naki.taken naki with
                | Some taken -> kuikaeKinds taken (Naki.consumed naki)
                // 暗杠（11）没有被鸣的那张，也就无食替可言。
                | Option.None -> []

            let called =
                interruptIppatsu
                    { state with
                        Players = players
                        // 鸣牌不摸牌：海底不成立；河底牌压根鸣不成（`responsesTo`），因此也是 false。
                        // 大明杠接下去要补摸岭上牌，`completeKan` 会把这几个标志重算一遍。
                        Flags =
                            {
                                Rinshan = false
                                Haitei = false
                                Houtei = false
                                Chankan = false
                            }
                    }

            // 大明杠走的是同一条路，只是鸣完之后多一步补摸（与暗杠 / 加杠共用 `completeKan`）。
            if Naki.isKan naki then
                let advanced, drawing = completeKan called actor (Naki.isConcealed naki)
                advanced, event :: drawing
            else
                { called with
                    Phase = awaitDahaiIn called actor Option.None forbidden
                },
                [ event ])

    /// 杠成立（**暗杠与加杠**这两种自家宣言的杠）：副露入手、一发打断，随后 `completeKan`。
    /// 加杠是把原来那组碰**原地换成杠**（副露数不变），因此不走 `addNaki`。
    ///
    /// 大明杠不在这里——它是对他家打牌的响应，走 `applyNaki`（那条路还要给打牌者的河记上一笔）。
    let private applyKan (state: GameState) (actor: Seat) (naki: Naki) : GameState * Event list =
        let players =
            match Naki.kind naki with
            | NakiKind.Kakan ->
                match Naki.taken naki with
                | Some added -> state.Players |> updatePlayer actor (PlayerState.addKakan added naki)
                // 走不到：加杠必然带着加上去的那张（`Naki.kakan`）。
                | Option.None -> state.Players
            | NakiKind.Ankan
            | NakiKind.Minkan
            | NakiKind.Pon
            | NakiKind.Chi -> state.Players |> updatePlayer actor (PlayerState.addNaki naki)

        let called =
            interruptIppatsu
                { state with
                    Players = players
                    Flags =
                        {
                            Rinshan = false
                            Haitei = false
                            Houtei = false
                            Chankan = false
                        }
                }

        completeKan called actor (Naki.isConcealed naki)

    /// 宣言暗杠 / 加杠：**先问抢杠**——能荣和这张的家先答复，都不要这个杠才成立。
    ///
    /// mjai 的事件顺序也是这样：`ankan` / `kakan` 先播出去，随后才是抢杠的 `hora`，
    /// 或者杠成立的 `dora` / `tsumo`。**宣言这一步不改手牌与副露**，
    /// 因此被抢时无需回滚——那个杠只是没有发生而已。
    let private declareKan
        (state: GameState)
        (actor: Seat)
        (naki: Naki)
        (pai: Tile)
        (event: Event)
        : GameState * Event list =
        // 抢杠这个役在这时就要亮起来：能不能荣和与算不算得成读的是同一份标志。
        let declaring =
            { state with
                Flags = { state.Flags with Chankan = true }
            }

        match responsesTo declaring (ResponseCause.Kan naki) actor pai with
        // 没人抢得了：当场成立，不经响应阶段。
        | [] ->
            let advanced, events = applyKan state actor naki
            advanced, event :: events
        | responses ->
            { declaring with
                Phase =
                    AwaitingResponse
                        {
                            Target = actor
                            Pai = pai
                            Cause = ResponseCause.Kan naki
                            Responses = responses
                            Declared = []
                        }
            },
            [ event ]

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
                | Action.Pon _
                | Action.Chi _
                | Action.Ankan _
                | Action.Kakan _
                | Action.Minkan _
                | Action.Riichi _
                | Action.Ryuukyoku _
                | Action.None _ -> false)

        // 宣言了荣和以外的任何东西（「过」或鸣牌），都是把这张能荣和的牌放过了。
        let minogashi =
            match declaration with
            | Some(Action.Hora _) -> false
            | Some(Action.Dahai _)
            | Some(Action.Pon _)
            | Some(Action.Chi _)
            | Some(Action.Ankan _)
            | Some(Action.Kakan _)
            | Some(Action.Minkan _)
            | Some(Action.Riichi _)
            | Some(Action.Ryuukyoku _)
            | Some(Action.None _)
            | Option.None -> couldRon

        let answered =
            // 见逃一次可以荣和的牌 → 同巡振听，到自己下次摸牌为止不能荣和。
            // 鸣走它也算见逃：鸣牌不摸牌，因此这份同巡振听要到鸣的那家下次真正摸牌才解除。
            if minogashi then
                { state with
                    Players = updatePlayer actor PlayerState.minogashi state.Players
                }
            else
                state

        let remaining = waiting.Responses |> List.filter (fun each -> each.Seat <> actor)

        let declared = waiting.Declared @ Option.toList declaration

        if List.isEmpty remaining then
            match ronWinners answered waiting.Target declared with
            | [] ->
                match waiting.Cause with
                // 没人抢杠：这个杠成立。宣言杠的那家不可能同时挂着没落定的立直宣言
                // （`allowsAnkan` 对 `Declared` 恒为 false），因此这条路没有 `acceptRiichi`。
                | ResponseCause.Kan kan -> applyKan answered waiting.Target kan
                | ResponseCause.Dahai ->
                    // 没人荣和：立直宣言牌就此落定（`reach_accepted` 排在鸣牌与下一条 `tsumo` 之前，
                    // 与真实牌谱同形）。宣言牌被碰时一发先亮后灭：`applyNaki` 里那一步把它打断。
                    let accepted, accepting = acceptRiichi answered

                    // 没人荣和才轮得到鸣牌（Ron > Pon / Minkan > Chi）；也没人鸣就轮下家摸牌。
                    let advanced, events =
                        nakiWinner accepted waiting.Target declared
                        |> Option.bind (applyNaki accepted waiting.Target)
                        |> Option.defaultWith (fun () -> afterDahai accepted waiting.Target)

                    advanced, accepting @ events
            | wins when Ryuukyoku.isSanchaHora answered.Ruleset (wins |> List.map fst) ->
                // 三家和了（天凤）：三家同时荣和同一张牌⇒ 途中流局，一家也不成立。
                // 打牌那家的立直宣言因此**不成立**（与被荣和同理，立直棒不出）：
                // 走到这里时 `acceptRiichi` 还没跑过，把挂着的宣言作废即可。
                let cancelled =
                    { answered with
                        Players = answered.Players |> List.map PlayerState.cancelRiichi
                    }

                endWithAbortive cancelled SanchaHora (wins |> List.map fst)
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

    /// 立直宣言：这一步只改状态与动作集（宣言牌还没打），产出 mjai 的 `reach`。
    /// 阶段仍是同一家的摸牌后阶段，只是合法动作集收窄成「打完仍听牌的那几张」。
    ///
    /// **立直棒不在这时出**：要等宣言牌落定（`acceptRiichi`）。
    /// 第一巡且此前无人鸣牌就是两立直，与天和 / 地和共用 `firstTurnFor` 那一条判据。
    let private applyRiichi (state: GameState) (actor: Seat) : GameState * Event list =
        let declaration =
            if firstTurnFor actor state.Log then
                RiichiDeclaration.DoubleRiichi
            else
                RiichiDeclaration.Riichi

        let players =
            updatePlayer actor (PlayerState.declareRiichi declaration) state.Players

        let drawn = Seat.tryItem actor players |> Option.bind PlayerState.drawn

        let declared = { state with Players = players }

        { declared with
            Phase = awaitDahaiIn declared actor drawn []
        },
        [ Event.Riichi actor ]

    /// 摸牌后阶段的一步：打牌、自摸和、宣言立直；「过」在这里无从谈起。
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
            match Seat.tryItem actor state.Players with
            | Option.None -> Error(SeatOutOfRange(actor, state.Ruleset.SeatCount))
            | Some player ->
                /// 宣言暗杠 / 加杠：合法性就是「在不在合法动作集里」，不在就是 `CannotKan`。
                let declareSelfKan (kind: NakiKind) (consumed: Tile list) =
                    if List.contains action waiting.Actions then
                        match selfKanOf player action with
                        | Some(naki, robbed, event) -> Ok(declareKan state actor naki robbed event)
                        // 走不到：进得了合法动作集的杠必然构造得出。
                        | Option.None -> Error(CannotKan(actor, kind, consumed))
                    else
                        Error(CannotKan(actor, kind, consumed))

                match action with
                | Action.Dahai(_, pai, tsumogiri) ->
                    match rejectDahai actor player pai tsumogiri with
                    | Some illegal -> Error illegal
                    | Option.None ->
                        // 牌在手里、摸切标志也对，却不在合法动作集里：不是食替封住了它，
                        // 就是立直封住了它（只能摸切 / 宣言牌要保持听牌）。
                        if List.contains action waiting.Actions then
                            Ok(applyDahai state actor pai tsumogiri)
                        elif RiichiState.isActive (PlayerState.riichi player) then
                            Error(RiichiRestricted(actor, pai))
                        else
                            Error(Kuikae(actor, pai))
                | Action.Hora(_, target, pai) ->
                    // 自摸和：来源是自己，和的是刚摸进的那张，且型成立、有役。
                    if target <> actor || PlayerState.drawn player <> Some pai then
                        Error(HoraTileMismatch(actor, target, pai))
                    elif RiichiState.isDeclared (PlayerState.riichi player) then
                        // 宣言了立直又回头自摸和：那一手只能打宣言牌（型成不成立都一样）。
                        Error(RiichiRestricted(actor, pai))
                    else
                        match horaOf actor state with
                        | Error YakuError.NoAgariShape -> Error(IllegalAction.NotAgari(actor, pai))
                        | Error YakuError.NoYaku -> Error(IllegalAction.NoYaku(actor, pai))
                        | Ok _ -> Ok(applyHora state actor [ actor, pai ])
                | Action.Riichi _ ->
                    // 不在合法动作集里只能是宣言合法性不过（不门清 / 没听牌 / 点数不够 /
                    // 牌山不够 / 已经立过直）。
                    if List.contains action waiting.Actions then
                        Ok(applyRiichi state actor)
                    else
                        Error(CannotRiichi actor)
                | Action.Ankan(_, consumed) -> declareSelfKan NakiKind.Ankan consumed
                | Action.Kakan(_, _, consumed) -> declareSelfKan NakiKind.Kakan consumed
                | Action.Ryuukyoku _ ->
                    // 不在合法动作集里只能是九种九牌的宣言条件不过（不是第一巡 / 有人鸣过牌 /
                    // 幺九牌不够种数 / 立直宣言之后又回头宣它）。
                    if List.contains action waiting.Actions then
                        Ok(endWithAbortive state KyuushuKyuuhai [])
                    else
                        Error(CannotRyuukyoku actor)
                // 摸牌后阶段没有「刚打出的那张」，鸣（含大明杠）与「过」都无从谈起。
                | Action.Pon _
                | Action.Chi _
                | Action.Minkan _
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
                     | Action.Pon _
                     | Action.Chi _
                     | Action.Ankan _
                     | Action.Kakan _
                     | Action.Minkan _
                     | Action.Riichi _
                     | Action.Ryuukyoku _
                     | Action.Hora _ -> Some action)
            )
        else
            // 鸣牌宣言没进合法动作集：先分出「鸣的不是刚打出的那张」与「这几张鸣不成」。
            let rejectNaki (kind: NakiKind) (target: Seat) (pai: Tile) (consumed: Tile list) =
                if target <> waiting.Target || pai <> waiting.Pai then
                    Error(NakiTileMismatch(actor, target, pai))
                else
                    Error(CannotNaki(actor, kind, pai, consumed))

            match action with
            | Action.Hora(_, target, pai) ->
                if target <> waiting.Target || pai <> waiting.Pai then
                    Error(HoraTileMismatch(actor, target, pai))
                else
                    // 牌对得上却没进合法动作集，只能是三个原因之一：型不成、无役、振听。
                    match horaOf actor state with
                    | Error YakuError.NoAgariShape -> Error(IllegalAction.NotAgari(actor, pai))
                    | Error YakuError.NoYaku -> Error(IllegalAction.NoYaku(actor, pai))
                    | Ok _ -> Error(RonWhileFuriten(actor, pai))
            | Action.Pon(_, target, pai, consumed) -> rejectNaki NakiKind.Pon target pai consumed
            | Action.Chi(_, target, pai, consumed) -> rejectNaki NakiKind.Chi target pai consumed
            | Action.Minkan(_, target, pai, consumed) -> rejectNaki NakiKind.Minkan target pai consumed
            // 打牌、立直宣言、自家宣言的杠与九种九牌都不是响应：现在等的是别家对那张牌的答复。
            | Action.Dahai _
            | Action.Ankan _
            | Action.Kakan _
            | Action.Riichi _
            | Action.Ryuukyoku _
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
        | SeatOutOfRange(actor, seatCount) -> $"座位 {Seat.index actor} 不合法，座位只有 0-{seatCount - 1}"
        | NotYourTurn(actor, awaiting) ->
            let waiting = awaiting |> List.map (Seat.index >> string) |> String.concat "、"

            if List.isEmpty awaiting then
                $"现在不轮到座位 {Seat.index actor} 出手"
            else
                $"现在不轮到座位 {Seat.index actor} 出手，等的是座位 {waiting}"
        | NotInHand(actor, pai) -> $"座位 {Seat.index actor} 手里没有 {Tile.toDisplay pai}"
        | TsumogiriMismatch(actor, pai, tsumogiri) ->
            if tsumogiri then
                $"座位 {Seat.index actor} 声称摸切 {Tile.toDisplay pai}，但刚摸进的不是这张"
            else
                $"座位 {Seat.index actor} 声称手切 {Tile.toDisplay pai}，但手里只有刚摸进的那一张"
        | Kuikae(actor, pai) -> $"座位 {Seat.index actor} 刚鸣完，这一手不能打 {Tile.toDisplay pai}（禁食替）"
        | HoraTileMismatch(actor, target, pai) ->
            if actor = target then
                $"座位 {Seat.index actor} 声称自摸和 {Tile.toDisplay pai}，但刚摸进的不是这张"
            else
                $"座位 {Seat.index actor} 声称荣和座位 {Seat.index target} 打出的 {Tile.toDisplay pai}，但刚打出的不是这张"
        | IllegalAction.NotAgari(actor, pai) -> $"座位 {Seat.index actor} 的手牌加上 {Tile.toDisplay pai} 不成和了型"
        | RonWhileFuriten(actor, pai) -> $"座位 {Seat.index actor} 振听，不能荣和 {Tile.toDisplay pai}"
        | IllegalAction.NoYaku(actor, pai) -> $"座位 {Seat.index actor} 的手牌加上 {Tile.toDisplay pai} 一个役都没有，不能和"
        | NakiTileMismatch(actor, target, pai) ->
            $"座位 {Seat.index actor} 声称鸣座位 {Seat.index target} 打出的 {Tile.toDisplay pai}，但刚打出的不是这张"
        | CannotNaki(actor, kind, pai, consumed) ->
            let tiles = consumed |> List.map Tile.toDisplay |> String.concat ""
            $"座位 {Seat.index actor} 用 {tiles} {NakiKind.toDisplay kind}不了 {Tile.toDisplay pai}"
        | CannotKan(actor, kind, consumed) ->
            let tiles = consumed |> List.map Tile.toDisplay |> String.concat ""
            $"座位 {Seat.index actor} 此刻用 {tiles} {NakiKind.toDisplay kind}不了"
        | CannotRiichi actor -> $"座位 {Seat.index actor} 此刻立直不了（不门清、没听牌、点数不够或牌山不够再摸一圈）"
        | CannotRyuukyoku actor -> $"座位 {Seat.index actor} 此刻宣言不了九种九牌（不是第一巡、有人鸣过牌或幺九牌种数不够）"
        | RiichiRestricted(actor, pai) -> $"座位 {Seat.index actor} 立直中，这一手动不了 {Tile.toDisplay pai}"
        | NothingToRespond actor -> $"现在没有可响应的牌，座位 {Seat.index actor} 无从响应起"
        | KyokuAlreadyEnded -> "这一局已经结束了，不再接受任何动作"
