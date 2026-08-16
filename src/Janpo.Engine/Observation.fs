namespace Janpo

open Thoth.Json.Core

/// 河里的一张：牌本身，以及它是不是摸切（CONTEXT.md 的 Tsumogiri / Tedashi）。
///
/// **摸切与手切是公开信息**，因此遮蔽之后仍然留着。`PlayerState.Kawa` 只存牌，
/// 摸切与否只有事件流知道（mjai `dahai` 的 `tsumogiri` 字段），投影从那里读。
type KawaEntry =
    {
        /// 打出的那张。**被别人鸣走的那张仍留在河里**（CONTEXT.md 的 Kawa）。
        Pai: Tile
        /// 打的是不是刚摸进的那张。
        Tsumogiri: bool
    }

/// 遮蔽之后的一家：**这个类型里没有暗牌的位置**。
///
/// 「他家的暗牌看不见」因此在**类型层面**成立，而不是靠运行时过滤或者调用方的纪律——
/// 投影函数想漏也漏不出去，它没有地方放。振听同理：那是自家才知道的事，不在这里。
type MaskedSeat =
    {
        /// 这是哪一家。
        Seat: Seat
        /// 手里有几张（不含副露，含刚摸进那张）。**张数是公开信息**，牌牌都看得见，
        /// 因此遮蔽之后仍然给得出；但它只是个数，牌是哪几张这个类型里仍无处可放。
        ///
        /// **这仍然是遮蔽不是计算**：读的就是那一家实际的张数。消费方自己拿副露数推
        /// （`13 - 3 × 副露数`）会在摸牌那一手差一张——那是把规则搬到渲染层去。
        HandCount: int
        /// 相对观测者第几家：1 下家、2 对家（四麻才有）、3 上家。
        /// 座位算术全在 `Seat.distanceFrom`，消费方不必自己取模。
        Relative: int
        /// 自风。由座位与亲推出，不是座位本身的属性（CONTEXT.md）。
        Jikaze: Kaze
        /// 巡目（CONTEXT.md 的 Junme）：这家自己摸过几次牌。
        Junme: int
        /// 点数。
        Score: int
        /// 河，按打出顺序，含手切 / 摸切。
        Kawa: KawaEntry list
        /// 副露，按鸣的先后。公开信息。
        Naki: Naki list
        /// 立直状态：宣言与成立是两段（`RiichiState`）。
        Riichi: RiichiState
        /// 一发还亮着没有。没立直时恒为 false。
        Ippatsu: bool
    }

/// 亮着的一家：公开部分之外还带暗牌、刚摸进那张与振听。
///
/// **两处用它，各是各的投影**：观测里的自家（`Observation.Self`），
/// 以及上帝视角里的每一家（`GodView.Seats`）。它不带 `Relative`——
/// 自家相对自己恒为 0，上帝视角压根没有观测者。
type RevealedSeat =
    {
        /// 这是哪一家。
        Seat: Seat
        /// 自风。
        Jikaze: Kaze
        /// 巡目。
        Junme: int
        /// 点数。
        Score: int
        /// 暗牌，mjai 顺序升序；轮到自己打牌时**含**刚摸进的那张，不含副露。
        Hand: Tile list
        /// 刚摸进、还没打出去的那张；摸切打的就是它。
        Drawn: Tile option
        /// 河，按打出顺序，含手切 / 摸切。
        Kawa: KawaEntry list
        /// 副露，按鸣的先后。
        Naki: Naki list
        /// 立直状态。
        Riichi: RiichiState
        /// 一发还亮着没有。
        Ippatsu: bool
        /// 振听：永久与同巡分别维护（CONTEXT.md 的 Furiten）。只挡荣和。
        Furiten: Furiten
    }

/// 某座位的合法观测（CONTEXT.md 的 Observation Projection）：局面里这个座位
/// **能合法看到的一切**，一张不多。LLM 的 prompt 与真人坐席的 UI 共用它，
/// 隐藏信息的保护因此在结构上成立而不靠纪律。
///
/// **它是遮蔽，不是计算**：不做任何规则判定，也不算 Shanten / Ukeire / Danger
/// （那些是 24 / 25 号票往决策包里加的脚手架，不在观测里）。
type Observation =
    {
        /// 观测者。
        Seat: Seat
        /// 场风。
        Bakaze: Kaze
        /// 局数，1 起。「东 1 局」= `Bakaze = Ton`、`Kyoku = 1`。
        Kyoku: int
        /// 本场。
        Honba: int
        /// 供托：**此刻场上堆着的**立直棒根数，含这一局里已经成立的立直。
        Kyotaku: int
        /// 已翻开的表宝牌指示牌，翻开顺序。**里宝牌不在这里**——它没翻开，谁也看不见。
        DoraMarkers: Tile list
        /// 可摸区剩余张数。
        WallRemaining: int
        /// 自家。
        Self: RevealedSeat
        /// 他家，按**下家、对家、上家**的顺序（座位序即相对位置序）。
        Others: MaskedSeat list
    }

/// 上帝视角（CONTEXT.md 的 God View）：全部座位的暗牌都亮着的**独立投影**，
/// 供围观与复盘使用；有真人参与的对局默认禁用（那是 UI 层的开关，不是这里的字段）。
///
/// **它不是「带 flag 的 Observation」**：没有观测者，因此没有自家与他家之分，
/// 也没有相对位置；里宝牌也一并亮着。两个投影各走各的类型，混不到一起去。
type GodView =
    {
        /// 场风。
        Bakaze: Kaze
        /// 局数，1 起。
        Kyoku: int
        /// 本场。
        Honba: int
        /// 供托：此刻场上堆着的立直棒根数。
        Kyotaku: int
        /// 已翻开的表宝牌指示牌。
        DoraMarkers: Tile list
        /// 已翻开的表宝牌指示牌对应的**里宝牌**指示牌。只有上帝视角看得见。
        UraMarkers: Tile list
        /// 可摸区剩余张数。
        WallRemaining: int
        /// 各家，按座位升序，全部亮着。
        Seats: RevealedSeat list
    }

/// 两个投影共用的取值与编码。`Observation` 与 `GodView` 各自组装自己的记录，
/// 共用的只有「怎么从局面里读一家的公开部分」与「一家怎么上 wire」。
module private SeatProjection =

    /// 某座位打出去的那些牌，含手切 / 摸切。**顺序与 `PlayerState.kawa` 一致**：
    /// 每打一张必产一条 `dahai` 事件，被鸣走的那张两边都留着。
    let kawa (seat: Seat) (state: GameState) : KawaEntry list =
        GameState.events state
        |> List.choose (fun event ->
            match event with
            | Dahai(actor, pai, tsumogiri) when actor = seat -> Some { Pai = pai; Tsumogiri = tsumogiri }
            | _ -> None)

    /// 遮蔽之后的一家：只读公开的那几项。**暗牌读都读不到**（`MaskedSeat` 没有那个字段）。
    let masked (state: GameState) (viewer: Seat) (seat: Seat) (player: PlayerState) : MaskedSeat =
        let ruleset = GameState.ruleset state

        {
            Seat = seat
            HandCount = PlayerState.hand player |> List.length
            Relative = Seat.distanceFrom ruleset viewer seat
            Jikaze = Seat.jikaze ruleset (GameState.context state).Oya seat
            Junme = GameState.junme seat state
            Score = PlayerState.score player
            Kawa = kawa seat state
            Naki = PlayerState.naki player
            Riichi = PlayerState.riichi player
            Ippatsu = PlayerState.ippatsu player
        }

    /// 亮着的一家：公开部分加暗牌、刚摸进那张与振听。
    let revealed (state: GameState) (seat: Seat) (player: PlayerState) : RevealedSeat =
        let ruleset = GameState.ruleset state

        {
            Seat = seat
            Jikaze = Seat.jikaze ruleset (GameState.context state).Oya seat
            Junme = GameState.junme seat state
            Score = PlayerState.score player
            Hand = PlayerState.hand player
            Drawn = PlayerState.drawn player
            Kawa = kawa seat state
            Naki = PlayerState.naki player
            Riichi = PlayerState.riichi player
            Ippatsu = PlayerState.ippatsu player
            Furiten = PlayerState.furiten player
        }

    // ---- JSON（单向；字段名沿用 mjai wire 的习惯） ----

    let tiles: Encoder<Tile list> = List.map Tile.encoder >> Encode.list

    /// 「有就编，没有就写 null」。Thoth.Json.Core 的 `lossyOption` 也做这件事，
    /// 但它的语义要读文档才知道，这里三行写清楚。
    let optional (encode: Encoder<'a>) : Encoder<'a option> =
        fun value ->
            match value with
            | Some item -> encode item
            | None -> Encode.nil

    let kawaEntry: Encoder<KawaEntry> =
        fun entry -> Encode.object [ "pai", Tile.encoder entry.Pai; "tsumogiri", Encode.bool entry.Tsumogiri ]

    /// 副露：mjai 没有「手上这组副露」的独立形态（它那边只有 `pon` / `chi` / … 事件），
    /// 因此这个形状是本项目的，字段名照那几条事件（`type` / `target` / `pai` / `consumed`）。
    /// 暗杠没有 `target` 也没有 `pai`，两项都写 null——mjai 的 `ankan` 事件就没有这两个字段。
    let naki: Encoder<Naki> =
        fun value ->
            let kind =
                match Naki.kind value with
                | NakiKind.Pon -> "pon"
                | NakiKind.Chi -> "chi"
                | NakiKind.Ankan -> "ankan"
                // 标识符按术语表拼作 Minkan，wire 仍是 mjai 的 daiminkan（ADR-0001）。
                | NakiKind.Minkan -> "daiminkan"
                | NakiKind.Kakan -> "kakan"

            Encode.object
                [
                    "type", Encode.string kind
                    "target", Naki.target value |> optional Seat.encoder
                    "pai", Naki.taken value |> optional Tile.encoder
                    "consumed", tiles (Naki.consumed value)
                ]

    /// 立直状态：mjai 的两条事件（`reach` / `reach_accepted`）在这里塌成一个三值字段。
    /// 两立直与立直的分别不上 wire——它是算番的事，观测方用不上。
    let riichi: Encoder<RiichiState> =
        let text (state: RiichiState) =
            match state with
            | RiichiState.None -> "none"
            | RiichiState.Declared _ -> "declared"
            | RiichiState.Accepted _ -> "accepted"

        text >> Encode.string

    let furiten: Encoder<Furiten> =
        fun value -> Encode.object [ "permanent", Encode.bool value.Permanent; "doujun", Encode.bool value.Doujun ]

    /// 一家的公开部分。`Observation` 的他家与两个投影的共用部分都从这里出。
    let private openFields (seat: Seat) (jikaze: Kaze) (junme: int) (score: int) : (string * IEncodable) list =
        [
            "seat", Seat.encoder seat
            "jikaze", Kaze.encoder jikaze
            "junme", Encode.int junme
            "score", Encode.int score
        ]

    let maskedSeat: Encoder<MaskedSeat> =
        fun value ->
            Encode.object (
                openFields value.Seat value.Jikaze value.Junme value.Score
                @ [
                    // mjai 没有「他家手里几张」这个字段（它发的是事件不是局面），
                    // 名字照 `revealedSeat` 那边的 `tehai` 拼。
                    "tehai_count", Encode.int value.HandCount
                    "relative", Encode.int value.Relative
                    "kawa", value.Kawa |> List.map kawaEntry |> Encode.list
                    "naki", value.Naki |> List.map naki |> Encode.list
                    "riichi", riichi value.Riichi
                    "ippatsu", Encode.bool value.Ippatsu
                ]
            )

    let revealedSeat: Encoder<RevealedSeat> =
        fun value ->
            Encode.object (
                openFields value.Seat value.Jikaze value.Junme value.Score
                @ [
                    // mjai 的 `start_kyoku` 把各家配牌叫 `tehais`，单独一家就是 `tehai`。
                    "tehai", tiles value.Hand
                    "tsumo", value.Drawn |> optional Tile.encoder
                    "kawa", value.Kawa |> List.map kawaEntry |> Encode.list
                    "naki", value.Naki |> List.map naki |> Encode.list
                    "riichi", riichi value.Riichi
                    "ippatsu", Encode.bool value.Ippatsu
                    "furiten", furiten value.Furiten
                ]
            )

/// 观测投影：局面 + 座位 → 那个座位的合法观测。
[<RequireQualifiedAccess>]
module Observation =

    // ---- 投影 ----

    /// 局面 → 某座位的合法观测。**纯遮蔽**：不做规则判定、不算数值，只把看不见的东西
    /// 挡在类型外面。座位不在这个规则集里时是 None。
    let ofState (seat: Seat) (state: GameState) : Observation option =
        let ruleset = GameState.ruleset state
        let wall = GameState.wall state

        GameState.player seat state
        |> Option.map (fun player ->
            let context = GameState.context state

            let others =
                // `orderFrom` 从自己起按下家方向绕一圈，去掉自己就是「下家、对家、上家」。
                Seat.orderFrom ruleset seat
                |> List.filter (fun other -> other <> seat)
                |> List.choose (fun other ->
                    GameState.player other state
                    |> Option.map (SeatProjection.masked state seat other))

            {
                Seat = seat
                Bakaze = context.Bakaze
                Kyoku = context.Kyoku
                Honba = context.Honba
                Kyotaku = GameState.kyotaku state
                DoraMarkers = Wall.doraIndicators wall
                WallRemaining = Wall.remaining wall
                Self = SeatProjection.revealed state seat player
                Others = others
            })

    // ---- 观测里看得见的牌 ----

    /// 手牌之外**这个座位看得见的每一张牌**：四家的河、全部副露里各家亮出的那几张、
    /// 宝牌指示牌。自家手牌不在里面（它在 `Self.Hand`，要不要算由读的人定）。
    ///
    /// **这仍然是遮蔽不是计算**：每一张都已经写在观测里，这里只是汇到一处。
    /// 副露只数 `Naki.fromHand`：被鸣走的那张仍留在打牌者的河里（CONTEXT.md 的 Kawa），
    /// 连 `Naki.taken` 一起数就把同一张数了两遍——`Ukeire` 会因此判可见张数越界。
    ///
    /// 两个消费方读的是同一份：`Scaffold` 的有效牌剩余枚数与 `Danger` 的「四张全见」。
    /// 各数一遍必然漂。
    let visible (observation: Observation) : Tile list =
        let kawa (entries: KawaEntry list) =
            entries |> List.map (fun entry -> entry.Pai)

        let others =
            observation.Others
            |> List.collect (fun other -> kawa other.Kawa @ List.collect Naki.fromHand other.Naki)

        kawa observation.Self.Kawa
        @ List.collect Naki.fromHand observation.Self.Naki
        @ others
        @ observation.DoraMarkers

    // ---- JSON（单向出口） ----

    /// 观测的 wire 形态。**只有 encoder，没有 decoder**：决策包是单向出口，
    /// 回来的只有一个动作 id（ADR-0002：局面没有快照格式，别拿它当序列化的局面用）。
    let encoder: Encoder<Observation> =
        fun observation ->
            Encode.object
                [
                    "seat", Seat.encoder observation.Seat
                    "bakaze", Kaze.encoder observation.Bakaze
                    "kyoku", Encode.int observation.Kyoku
                    "honba", Encode.int observation.Honba
                    "kyotaku", Encode.int observation.Kyotaku
                    "dora_markers", SeatProjection.tiles observation.DoraMarkers
                    "wall_remaining", Encode.int observation.WallRemaining
                    "self", SeatProjection.revealedSeat observation.Self
                    "others", observation.Others |> List.map SeatProjection.maskedSeat |> Encode.list
                ]

/// 上帝视角：**独立**投影，全部座位的暗牌与里宝牌都亮着。
[<RequireQualifiedAccess>]
module GodView =

    // ---- 投影 ----

    /// 局面 → 上帝视角。没有观测者，因此不会失败。
    let ofState (state: GameState) : GodView =
        let context = GameState.context state
        let wall = GameState.wall state

        {
            Bakaze = context.Bakaze
            Kyoku = context.Kyoku
            Honba = context.Honba
            Kyotaku = GameState.kyotaku state
            DoraMarkers = Wall.doraIndicators wall
            UraMarkers = Wall.uraIndicators wall
            WallRemaining = Wall.remaining wall
            Seats =
                GameState.players state
                |> Seat.indexed
                |> List.map (fun (seat, player) -> SeatProjection.revealed state seat player)
        }

    // ---- JSON（单向出口） ----

    let encoder: Encoder<GodView> =
        fun view ->
            Encode.object
                [
                    "bakaze", Kaze.encoder view.Bakaze
                    "kyoku", Encode.int view.Kyoku
                    "honba", Encode.int view.Honba
                    "kyotaku", Encode.int view.Kyotaku
                    "dora_markers", SeatProjection.tiles view.DoraMarkers
                    "uradora_markers", SeatProjection.tiles view.UraMarkers
                    "wall_remaining", Encode.int view.WallRemaining
                    "seats", view.Seats |> List.map SeatProjection.revealedSeat |> Encode.list
                ]
