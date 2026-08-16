namespace Janpo.Web

open Janpo

/// 牌桌消费的是哪一份投影（20 票落地的那两个）。
///
/// **上帝视角开关切的是「消费哪个投影」，不是「渲染时要不要画手牌」**：
/// 关掉时消费某座位自己的 `Observation`（他家是 `MaskedSeat`，那个类型里没有手牌字段），
/// 打开时消费 `GodView`（独立投影，四家全亮）。于是「关掉后 DOM 里也不该留着他家的牌」
/// 是结构性的——渲染层拿不到那些牌，不靠纪律。M3 的真人坐席复用同一条路径。
[<RequireQualifiedAccess>]
type Viewpoint =
    /// 坐在某个座位上看：自家亮着，他家只有公开信息。
    | Seated of seat: Seat
    /// 上帝视角（CONTEXT.md 的 God View）：四家全亮，里宝牌也亮着。
    | God

/// 牌桌上一家的暗牌部分：亮着的那几张，还是只有张数。
///
/// **`MaskedSeat` 只映得到 `Concealed`**——它没有手牌字段，映射函数想漏也没地方漏。
[<RequireQualifiedAccess>]
type HandView =
    /// 亮着：暗牌与刚摸进那张（摸切打的就是它）。
    | Revealed of hand: Tile list * drawn: Tile option
    /// 扣着：只知道有几张。
    | Concealed of count: int

/// 牌桌上的一家。字段是两个投影的**交集**加上一个 `HandView`——
/// 除了暗牌，两个投影给的东西一模一样。
type SeatView = {
    /// 这是哪一家。
    Seat: Seat
    /// 自风。**自风是东的那家就是亲**（自风由座位与亲推出）。
    Jikaze: Kaze
    /// 巡目。
    Junme: int
    /// 点数。
    Score: int
    /// 暗牌：亮着的那几张，或者只有张数。
    Hand: HandView
    /// 河，按打出顺序，含手切 / 摸切。
    Kawa: KawaEntry list
    /// 副露，按鸣的先后。
    Naki: Naki list
    /// 立直状态。
    Riichi: RiichiState
    /// 一发还亮着没有。
    Ippatsu: bool
}

/// 副露里一张牌该怎么画。
///
/// **`FromOther` 严格对应 `Naki.fromKawa`**：这张牌是从**别人**那儿来的——碰 / 吃 / 大明杠是
/// 被鸣的那张，加杠是**当初碰来的**那张（不是加上去的），暗杠一张也没有。牌桌上它横放。
/// 加杠后加上去的那张出自**自家手里**，所以它不横放，另记 `Added`。
/// 两个标志因此互斥，且一组里各至多一张。
type NakiTileView = {
    /// 牌面；`None` 是扣着的那张（暗杠两端）。**扣着的牌没有牌面可写**，与手牌那边同一条。
    Pai: Tile option
    /// 从他家那儿来的那一张（`Naki.fromKawa`）。
    FromOther: bool
    /// 加杠后加上去的那张（`Naki.taken`，来自自家手里）。
    Added: bool
}

/// 一组副露画出来的样子：种类、来源、逐张。**这一层不做规则判定**，只把 `Naki` 摆成
/// 「哪一张横放、来自谁」这个形状——牌桌与它的无头验收读的都是它。
type NakiView = {
    /// 种类。
    Kind: NakiKind
    /// 被鸣的那家（`Naki.target`）：暗杠没有，加杠记的是**当初碰的**来源。
    Target: Seat option
    /// 那一家相对**副露方**是第几家：1 下家、2 对家、3 上家（编号同 `MaskedSeat.Relative`）。
    ///
    /// **参照系是副露方，不是观测者**：牌谱上「吃只能吃上家」是副露自己的属性，
    /// 换成观测者的参照系，别人吃来的那一组会写成「来自对家」——那在日麻里根本不成立。
    /// （prompt 尾部的 `words.who` 是另一回事：它给的是**观测者**对每个座位的称呼。）
    Relative: int option
    /// 逐张，从左到右。
    Tiles: NakiTileView list
}

/// 一张牌桌该画的全部东西。**它只是两个投影的换装**，不含任何规则判定。
type BoardView = {
    /// 场风。
    Bakaze: Kaze
    /// 局数，1 起。
    Kyoku: int
    /// 本场。
    Honba: int
    /// 供托：场上堆着的立直棒根数。
    Kyotaku: int
    /// 已翻开的表宝牌指示牌。
    DoraMarkers: Tile list
    /// 里宝牌指示牌。**只有上帝视角看得见**，坐着看时恒为空表。
    UraMarkers: Tile list
    /// 可摸区剩余张数。
    WallRemaining: int
    /// 各家，**按座位升序**（画出来的位置不随视角跳）。
    Seats: SeatView list
    /// 观测者；上帝视角没有观测者。
    Viewer: Seat option
}

/// 一次和了的结算显示。
type HoraView = {
    /// 和了的座位。
    Actor: Seat
    /// 放铳的座位；**自摸时等于 `Actor`**（mjai 的约定）。
    Target: Seat
    /// 和了的那张。
    Pai: Tile
    /// 役种与各自的价值，按引擎给的顺序。**宝牌不是役**，另记在下面三项。
    Yaku: (Yaku * YakuValue) list
    /// 表宝牌的番数。
    Dora: int
    /// 里宝牌的番数；没立直时恒为 0。
    Uradora: int
    /// 红宝牌的番数。
    Akadora: int
    /// 符（已切上）。
    Fu: int
    /// 番。役满按 13 番一倍记（`HoraValue.fan`）。
    Fan: int
    /// 满贯档；满贯以下是 `Limit.Normal`。
    Limit: Limit
    /// 和了点，不含本场与供托的授受。
    HoraPoints: int
    /// 本次授受，按座位升序，含本场与供托。
    Deltas: int list
    /// 授受后的各家点数，按座位升序。
    Scores: int list
    /// 翻开的里宝牌指示牌；和了者没立直时是空表。
    UraMarkers: Tile list
}

/// 一局是怎么结束的（`KyokuEnd` 的显示版）。
[<RequireQualifiedAccess>]
type Outcome =
    /// 和了收尾。头跳关掉时的双响会有两条。
    | Hora of horas: HoraView list
    /// 流局收尾。听牌家与授受都在引擎给的这份载荷里。
    | Ryuukyoku of ryuukyoku: Ryuukyoku

/// 一局的结算显示：怎么结束的、亲连不连庄、这一局之后往哪走。
type Settlement = {
    /// 怎么结束的。
    Outcome: Outcome
    /// 亲。
    Oya: Seat
    /// 连庄与否。**判据只有一处**（`KyokuEnd.isRenchan`），这里只读它的布尔值。
    Renchan: bool
    /// 这一局打完就终局了吗（CONTEXT.md 的 `GameProgress`：`NextKyoku` 还是 `Ended`）。
    ///
    /// **它不是连庄的反义**：局数序列走完就终局，连庄也不延长（东风战东 4 局的亲连庄
    /// 照样终局，05 票的 Out of Scope）。结算面板那句「进下一局」读的就是它：
    /// 末局那一刻根本没有下一局（票 39）。
    Ended: bool
}

/// 终局那一屏（CONTEXT.md 的 `GameResult`）：精算本身，加上「场上剩的那几根供托归了谁」。
///
/// **座位卡读的是同一份数**（`Board.ofTable` 的 `settled`）：同一屏上不允许两个点数并排
/// 站着不解释（票 39）。
type FinalView = {
    /// 终局精算：精算后的各家点数与顺位。
    Result: GameResult
    /// 精算**前**场上还剩几根供托（Kyotaku 记的是根数不是点数）。它们已经归了头名；
    /// 0 就没有这回事。
    Kyotaku: int
    /// 那几根值多少点（`Ruleset.kyotakuScore`）。头名的点数比局末多出来的就是它。
    KyotakuScore: int
}

/// 投影 → 牌桌视图。**这一层不做规则判定**，只把两个投影摆成同一个形状。
///
/// 与 `Table` 的分工：`Table` 是**在打的那一桌**（引擎的局面加选手），
/// `Board` 是那一桌**看上去的样子**。前者推进，后者只读。
[<RequireQualifiedAccess>]
module Board =

    // ---- 换装 ----

    /// 亮着的一家（观测里的自家、上帝视角里的每一家）。
    let private ofRevealed (seat: RevealedSeat) : SeatView = {
        Seat = seat.Seat
        Jikaze = seat.Jikaze
        Junme = seat.Junme
        Score = seat.Score
        Hand = HandView.Revealed(seat.Hand, seat.Drawn)
        Kawa = seat.Kawa
        Naki = seat.Naki
        Riichi = seat.Riichi
        Ippatsu = seat.Ippatsu
    }

    /// 遮蔽之后的一家。**这里只可能产出 `Concealed`**：源类型里没有手牌。
    let private ofMasked (seat: MaskedSeat) : SeatView = {
        Seat = seat.Seat
        Jikaze = seat.Jikaze
        Junme = seat.Junme
        Score = seat.Score
        Hand = HandView.Concealed seat.HandCount
        Kawa = seat.Kawa
        Naki = seat.Naki
        Riichi = seat.Riichi
        Ippatsu = seat.Ippatsu
    }

    // ---- 副露 ----

    /// 亮着的一张。
    let private nakiTile (fromOther: bool) (added: bool) (pai: Tile) : NakiTileView = {
        Pai = Some pai
        FromOther = fromOther
        Added = added
    }

    /// 扣着的一张（暗杠两端）。
    let private nakiBack: NakiTileView = {
        Pai = None
        FromOther = false
        Added = false
    }

    /// 暗杠：四张升序，两端扣着（牌桌上就是这个摆法）。没有来源，也没有横放的那张。
    let private ankanTiles (naki: Naki) : NakiTileView list =
        Naki.tiles naki
        |> List.mapi (fun index pai ->
            if index = 0 || index = 3 then
                nakiBack
            else
                nakiTile false false pai)

    /// 加杠：底是原来那副碰，**加上去的那张摆在末尾**。
    ///
    /// 这一组**不重排**：`Naki.consumed` 的第一张恒是当初被碰的那张（`Naki.kakan` 的注释写着），
    /// 而四张同种牌一排序谁是谁就没了——那正是「加上去的那张看不出来」的成因。
    let private kakanTiles (naki: Naki) : NakiTileView list =
        let bottom =
            Naki.consumed naki
            |> List.mapi (fun index pai -> nakiTile (index = 0) false pai)

        let added = Naki.taken naki |> Option.map (nakiTile false true) |> Option.toList

        bottom @ added

    /// 碰 / 吃 / 大明杠：**升序摆**（`Naki.tiles`），被鸣的那张就地横放，不挪位。
    ///
    /// 吃尤其要看这个顺序：`2p [3p] 4p` 与 `[2p] 3p 4p` 是两种不同的吃，
    /// 挪位（把被鸣那张一律搬到最左）会把「2 3 4 是个顺子」这件事读没了。
    let private calledTiles (naki: Naki) : NakiTileView list =
        let all = Naki.tiles naki

        // 同种牌互换无碍，取第一个对得上的位置即可（红 5 与正 5 是两个不同的值，不会认错）。
        let fromOtherAt =
            Naki.fromKawa naki
            |> Option.bind (fun taken -> all |> List.tryFindIndex ((=) taken))

        all
        |> List.mapi (fun index pai -> nakiTile (Some index = fromOtherAt) false pai)

    /// 一组副露画出来的样子。`owner` 是**副露方**（相对位置的参照系）。
    let nakiView (ruleset: Ruleset) (owner: Seat) (naki: Naki) : NakiView =
        let kind = Naki.kind naki
        let target = Naki.target naki

        {
            Kind = kind
            Target = target
            Relative = target |> Option.map (Seat.distanceFrom ruleset owner)
            Tiles =
                match kind with
                | NakiKind.Ankan -> ankanTiles naki
                | NakiKind.Kakan -> kakanTiles naki
                | NakiKind.Pon
                | NakiKind.Chi
                | NakiKind.Minkan -> calledTiles naki
        }

    // ---- 一桌 ----

    /// 座位升序。观测给的是「自家 + 下家对家上家」，上帝视角给的已经是升序；
    /// 一律排一遍，画出来的位置就不随视角跳。
    let private bySeat (seats: SeatView list) : SeatView list =
        seats |> List.sortBy (fun view -> Seat.index view.Seat)

    let private ofObservation (observation: Observation) : BoardView = {
        Bakaze = observation.Bakaze
        Kyoku = observation.Kyoku
        Honba = observation.Honba
        Kyotaku = observation.Kyotaku
        DoraMarkers = observation.DoraMarkers
        // 里宝牌没翻开，观测者看不见——这不是「不画」，是投影里根本没有。
        UraMarkers = []
        WallRemaining = observation.WallRemaining
        Seats = ofRevealed observation.Self :: List.map ofMasked observation.Others |> bySeat
        Viewer = Some observation.Seat
    }

    let private ofGodView (view: GodView) : BoardView = {
        Bakaze = view.Bakaze
        Kyoku = view.Kyoku
        Honba = view.Honba
        Kyotaku = view.Kyotaku
        DoraMarkers = view.DoraMarkers
        UraMarkers = view.UraMarkers
        WallRemaining = view.WallRemaining
        Seats = view.Seats |> List.map ofRevealed |> bySeat
        Viewer = None
    }

    // ---- 投影选择 ----

    /// 终局那一刻的点数与供托：**换成精算之后的那一份**。
    ///
    /// **谁是权威**：局中是 `GameState`（两个投影就是它的换装）；终局是 `Game` ——
    /// `Game.scores` 在终局返回精算后的点数、`Game.kyotaku` 返回 0（供托已经归属完毕），
    /// 两行注释就写在 `Game.fs` 里。投影只认最后一局的局面，它不知道精算这回事，
    /// 于是同一屏上座位卡与终局精算各说一个数（票 39 的病根）。
    ///
    /// 立直棒那排图元跟着 `Kyotaku` 走，因此归零之后桌上自然不再画着它。
    let private settled (result: GameResult) (board: BoardView) : BoardView = {
        board with
            Kyotaku = 0
            Seats =
                board.Seats
                |> List.map (fun view -> {
                    view with
                        Score = Seat.tryItem view.Seat result.Scores |> Option.defaultValue view.Score
                })
    }

    /// 牌桌 + 视角 → 画出来的那张牌桌。坐的那个座位不在这个规则集里时是 None
    /// （上帝视角不会失败）。
    ///
    /// **坐席那一侧取的是增量维护的掩蔽流**（票 29a 的 `Table.observation`），
    /// 不是每帧重头 fold 全流；上帝视角另走一条投影——它一张也不蔽，因此不在掩蔽法则之列
    /// （里宝牌压根不在事件流里）。
    ///
    /// **终局那一屏另过一道 `settled`**：那时牌桌该画的不再是最后一局的局末局面。
    let ofTable (viewpoint: Viewpoint) (table: Table) : BoardView option =
        let projected =
            match viewpoint with
            | Viewpoint.God -> GodView.ofState table.State |> ofGodView |> Some
            | Viewpoint.Seated seat -> Table.observation seat table |> Option.map ofObservation

        match Table.result table with
        | None -> projected
        | Some result -> projected |> Option.map (settled result)

    // ---- 结算 ----

    /// 一条和了事件 + 那一手捞下来的读法 → 结算显示。读法缺失时役种为空表
    /// （走不到：`apply` 在提交 Hora 的那一刻捞，那时引擎必然答得出来）。
    let private ofHora (ruleset: Ruleset) (readings: (Seat * HoraReading) list) (hora: Hora) : HoraView =
        let reading =
            readings
            |> List.tryFind (fun (actor, _) -> actor = hora.Actor)
            |> Option.map snd

        let tally = reading |> Option.map (fun each -> each.Tally)

        {
            Actor = hora.Actor
            Target = hora.Target
            Pai = hora.Pai
            Yaku = tally |> Option.map (fun each -> each.Yaku) |> Option.defaultValue []
            Dora = tally |> Option.map (fun each -> each.Dora) |> Option.defaultValue 0
            Uradora = tally |> Option.map (fun each -> each.Uradora) |> Option.defaultValue 0
            Akadora = tally |> Option.map (fun each -> each.Akadora) |> Option.defaultValue 0
            Fu = hora.Fu
            Fan = hora.Fan
            Limit =
                reading
                |> Option.map (fun each -> Score.limit ruleset each.Value)
                |> Option.defaultValue Limit.Normal
            HoraPoints = hora.HoraPoints
            Deltas = hora.Deltas
            Scores = hora.Scores
            UraMarkers = hora.UraDoraMarkers
        }

    /// 这一局的结算显示；还没终则为 None。
    let settlement (table: Table) : Settlement option =
        let oya = (GameState.context table.State).Oya

        GameState.kyokuEnd table.State
        |> Option.map (fun kyokuEnd ->
            let outcome =
                match kyokuEnd with
                | KyokuEnd.Hora horas ->
                    horas
                    |> List.map (ofHora (GameState.ruleset table.State) table.Readings)
                    |> Outcome.Hora
                | KyokuEnd.Ryuukyoku ryuukyoku -> Outcome.Ryuukyoku ryuukyoku

            {
                Outcome = outcome
                Oya = oya
                Renchan = KyokuEnd.isRenchan oya kyokuEnd
                // 这一局已经被 `Game.advance` 收进这一场了，因此现在就答得出有没有下一局。
                Ended = Table.result table |> Option.isSome
            })

    /// 终局那一屏；这一场还没走完则为 None。
    ///
    /// 供托取的是**最后一局终了时场上剩的那几根**（`GameState.kyotaku`）：`Game` 那边精算
    /// 一做完就归零了，而「归了谁」这句话需要知道归之前有几根。
    let final (table: Table) : FinalView option =
        let kyotaku = GameState.kyotaku table.State

        Table.result table
        |> Option.map (fun result -> {
            Result = result
            Kyotaku = kyotaku
            KyotakuScore = Ruleset.kyotakuScore (GameState.ruleset table.State) kyotaku
        })
