namespace Janpo

/// 立直（CONTEXT.md）在一局之内的三段状态。
///
/// **宣言与成立是分开的两步**：mjai 把它们记成 `reach` 与 `reach_accepted` 两个事件，
/// 真实牌谱里也确实是两条（60 局样本：491 次 `reach`、479 次 `reach_accepted`）。
/// 差的那几次就是**宣言牌被荣和**——那时立直不成立、立直棒不出。`Declared` 就是
/// 这两步之间的那一段。
///
/// 判役读的是 `RiichiDeclaration`（`declaration`），**只有成立了的才算**：
/// 宣言与成立之间那一手里这家除了打牌什么也做不了，不可能和了。
///
/// case 强制限定名（`RiichiState.None`）：`Option.None` 与 `RiichiDeclaration.None`
/// 都在同一个命名空间里打转，不限定名读代码的人分不出是哪个（裁决 D-3 的教训）。
[<RequireQualifiedAccess>]
type RiichiState =
    /// 没立直。
    | None
    /// 宣言了立直，宣言牌还没落定。
    | Declared of declared: RiichiDeclaration
    /// 宣言牌落定，立直成立：立直棒已经进了供托。
    | Accepted of accepted: RiichiDeclaration

/// 立直状态的构造、迁移与那几条只有立直才有的判据。
///
/// **这个模块只出判据，不出动作**：立直宣言这个动作在 `Action` 与 `GameState` 里，
/// 立直后的暗杠这个动作是 11 票的（裁决 D-8），它调这里的 `allowsAnkan`。
[<RequireQualifiedAccess>]
module RiichiState =

    // ---- 构造与迁移 ----

    /// 没立直。
    let none: RiichiState = RiichiState.None

    /// 宣言立直。第一巡且此前无人鸣牌时是两立直，那一条由调用方判（它手里才有牌局历史）。
    let declare (declaration: RiichiDeclaration) : RiichiState = RiichiState.Declared declaration

    /// 宣言牌落定，立直成立。不是「宣言了还没落定」时原样返回。
    let accept (state: RiichiState) : RiichiState =
        match state with
        | RiichiState.Declared declaration -> RiichiState.Accepted declaration
        | RiichiState.None
        | RiichiState.Accepted _ -> state

    /// 宣言牌被荣和：立直不成立，退回没立直。已经成立的不受影响。
    let cancel (state: RiichiState) : RiichiState =
        match state with
        | RiichiState.Declared _ -> RiichiState.None
        | RiichiState.None
        | RiichiState.Accepted _ -> state

    // ---- 拆解 ----

    /// 判役读的宣言状态：**只有成立了的立直才算**，宣言还没落定时是 `None`。
    let declaration (state: RiichiState) : RiichiDeclaration =
        match state with
        | RiichiState.Accepted declaration -> declaration
        | RiichiState.Declared _
        | RiichiState.None -> RiichiDeclaration.None

    /// 宣言了还没落定。
    let isDeclared (state: RiichiState) : bool =
        match state with
        | RiichiState.Declared _ -> true
        | RiichiState.None
        | RiichiState.Accepted _ -> false

    /// 立直已经成立。
    let isAccepted (state: RiichiState) : bool =
        match state with
        | RiichiState.Accepted _ -> true
        | RiichiState.None
        | RiichiState.Declared _ -> false

    /// 立直中：宣言了（不管落没落定）。**「不能鸣牌」「见逃变永久振听」读的是它**——
    /// 这两条从宣言那一刻起就生效，不等立直棒出。
    let isActive (state: RiichiState) : bool =
        match state with
        | RiichiState.Declared _
        | RiichiState.Accepted _ -> true
        | RiichiState.None -> false

    // ---- 判据 ----

    /// 打出哪几种牌之后仍然听牌（去红后的牌种，按 mjai 顺序升序）。
    ///
    /// **立直的两条判据共用这一份**：宣言合法性要它非空（有打得出去的听牌形），
    /// 宣言牌的限制就是它本身（宣言牌只能从这里挑）。听牌判定走 `Shanten`，不另写一份。
    let tenpaiDahai (kindSet: TileKindSet) (nakiCount: int) (hand: Tile list) : Tile list =
        match HandShape.create nakiCount hand with
        // 张数不成形态（还没摸牌那 13 张）时打不出听牌形：立直无从谈起。
        | Error _ -> []
        | Ok shape ->
            HandShape.tiles shape
            |> List.distinct
            |> List.filter (fun kind ->
                match HandShape.remove kind shape with
                | Error _ -> false
                | Ok rest -> Shanten.value (Shanten.calculate kindSet rest) = 0)

    /// 立直宣言的合法性（spec 的规则清单）：**门清、听牌、点数够一根立直棒、牌山还摸得完一圈**。
    ///
    /// - 门清：只有暗杠不破门清（`Naki.isConcealed`）；
    /// - 听牌：打得出至少一张仍然听牌的牌（`tenpaiDahai`）——它同时定死了宣言牌能打哪几张；
    /// - 点数：`Ruleset.RiichiBou` 那么多（裁决 D-6：一根立直棒的点数只有这一个字段）；
    /// - 牌山：可摸区剩的牌够全场再摸一圈，因此下界是**座位数**而不是写死的 4。
    ///
    /// 已经立直的家不在此列——调用方按 `RiichiState` 分派，压根不会问到这里。
    let canDeclare
        (ruleset: Ruleset)
        (kindSet: TileKindSet)
        (remaining: int)
        (score: int)
        (naki: Naki list)
        (hand: Tile list)
        : bool =
        List.forall Naki.isConcealed naki
        && score >= ruleset.RiichiBou
        && remaining >= ruleset.SeatCount
        && (tenpaiDahai kindSet (List.length naki) hand |> List.isEmpty |> not)

    /// 从牌列里去掉**一张**给定的牌（按实例；红 5 与正牌各算各的）。
    let private withoutOne (tile: Tile) (tiles: Tile list) : Tile list =
        match List.tryFindIndex ((=) tile) tiles with
        | Some index -> List.take index tiles @ List.skip (index + 1) tiles
        | Option.None -> tiles

    /// 去掉某牌种的**全部**实例（红宝牌与正牌同种）。
    let private withoutKind (kind: Tile) (tiles: Tile list) : Tile list =
        tiles |> List.filter (fun tile -> Tile.kindIndex tile <> Tile.kindIndex kind)

    /// 这手等摸的牌听什么。牌种集合与和了型都走已有的那一份（`AgariShape.waits`）。
    let private waitsOf (kindSet: TileKindSet) (nakiCount: int) (concealed: Tile list) : Tile list =
        match HandShape.create nakiCount concealed with
        | Error _ -> []
        | Ok shape -> AgariShape.waits kindSet shape

    /// 立直时的手牌补上这张和了牌之后，那个牌种是不是**每一种读法**里都作暗刻使用。
    ///
    /// 和了牌本身就是这个牌种时直接不算：那时手里的三张有一张得挪去顺子或雀头
    /// （`1112 3m` 和了 `1m` 只能读成 `111m + 123m`），杠下去面子构成就变了。
    /// 用自摸构造牌姿只是为了拿分解：荣和会把那个刻子改成明刻，与「构成变没变」无关。
    let private alwaysAnkou (target: Tile) (naki: Naki list) (concealed: Tile list) (winning: Tile) : bool =
        if Tile.deaka winning = target then
            false
        else
            match AgariHand.tsumo naki (winning :: concealed) winning with
            | Error _ -> false
            | Ok agari ->
                match MentsuBreakdown.enumerate agari with
                // 一般型之外的读法（七对子 / 国士）里没有刻子可言，杠不得。
                | [] -> false
                | breakdowns ->
                    breakdowns
                    |> List.forall (fun breakdown ->
                        MentsuBreakdown.mentsu breakdown
                        |> List.exists (fun mentsu ->
                            Mentsu.tile mentsu = target
                            && Mentsu.isConcealed mentsu
                            && (match Mentsu.kind mentsu with
                                | Koutsu -> true
                                | Shuntsu
                                | Kantsu -> false)))

    /// **立直后能不能暗杠这一种牌**（裁决 D-8 切给 09 的那条判据）。
    ///
    /// 暗杠这个**动作**完全属于 11 票——它的其余条件（手里真有四张、岭上牌还有、
    /// 四杠散了……）也都在那边；这里只回答立直独有的那一层：
    ///
    /// 1. **禁送り杠**：杠的必须是刚摸进的那张（`drawn`），手里早就攒着的四张杠不得；
    /// 2. **听牌不变**：杠掉那四张之后听的牌种与立直时完全一样；
    /// 3. **面子构成不变**：立直时的手牌在每一种和了读法里都把那个牌种当暗刻用。
    ///
    /// 没立直的家返回 `true`（这一层限制对它不存在）；宣言了还没落定的返回 `false`
    /// （那一手只能打宣言牌）。
    let allowsAnkan
        (kindSet: TileKindSet)
        (state: RiichiState)
        (naki: Naki list)
        (hand: Tile list)
        (drawn: Tile option)
        (kind: Tile)
        : bool =
        match state with
        | RiichiState.None -> true
        | RiichiState.Declared _ -> false
        | RiichiState.Accepted _ ->
            match drawn with
            | Option.None -> false
            | Some tsumo ->
                let target = Tile.deaka kind
                let nakiCount = List.length naki
                // 立直时的那 13 张：现在的手牌去掉刚摸进的那一张。
                let declared = withoutOne tsumo hand
                let held = hand |> List.filter (fun tile -> Tile.deaka tile = target)
                let waits = waitsOf kindSet nakiCount declared

                Tile.deaka tsumo = target
                && List.length held = 4
                && not (List.isEmpty waits)
                && waits = waitsOf kindSet (nakiCount + 1) (withoutKind target hand)
                && waits |> List.forall (alwaysAnkou target naki declared)

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (state: RiichiState) : string =
        match state with
        | RiichiState.None -> "没立直"
        | RiichiState.Declared RiichiDeclaration.DoubleRiichi -> "两立直（宣言牌未落定）"
        | RiichiState.Declared _ -> "立直（宣言牌未落定）"
        | RiichiState.Accepted RiichiDeclaration.DoubleRiichi -> "两立直"
        | RiichiState.Accepted _ -> "立直"
