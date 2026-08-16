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

/// 一局的结算显示：怎么结束的、亲连不连庄。
type Settlement = {
    /// 怎么结束的。
    Outcome: Outcome
    /// 亲。
    Oya: Seat
    /// 连庄与否。**判据只有一处**（`KyokuEnd.isRenchan`），这里只读它的布尔值。
    Renchan: bool
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

    /// 局面 + 视角 → 牌桌。坐的那个座位不在这个规则集里时是 None（上帝视角不会失败）。
    let ofState (viewpoint: Viewpoint) (state: GameState) : BoardView option =
        match viewpoint with
        | Viewpoint.God -> GodView.ofState state |> ofGodView |> Some
        | Viewpoint.Seated seat -> Observation.ofState seat state |> Option.map ofObservation

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
            })
