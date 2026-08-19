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

/// 上帝视角的取值，与两个投影共用的编码。
///
/// **这里已经没有掩蔽了**（票 29a）：座席看得见什么只由 `MaskedEvent.forSeat` 回答，
/// `Observation` 是那条流的 fold。`revealed` 只服务上帝视角——它一张也不蔽，
/// 因此不是第二条掩蔽法则；而且里宝牌本就不在事件流里（它没翻开），
/// 上帝视角本来就只能从局面读。
module private SeatProjection =

    /// 某座位打出去的那些牌，含手切 / 摸切。**顺序与 `PlayerState.kawa` 一致**：
    /// 每打一张必产一条 `dahai` 事件，被鸣走的那张两边都留着。
    let kawa (seat: Seat) (state: GameState) : KawaEntry list =
        GameState.events state
        |> List.choose (fun event ->
            match event with
            | Dahai(actor, pai, tsumogiri) when actor = seat -> Some { Pai = pai; Tsumogiri = tsumogiri }
            | _ -> None)

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

/// 某座位的**掩蔽事件流与它 fold 到此刻的结果**（票 29a）。
///
/// **座席的唯一投影就是它**：历史是 `SeatStream.events`（掩蔽流本身，29b 的 prompt 前缀读它），
/// 局面是 `SeatStream.observation`（同一条流的 fold）。两种形态因此**构造性地一致**——
/// 尾部场况就是前缀那条流的 fold，不可能对不上（主人 8/16 第四次裁决第 5 条）。
///
/// **增量维护**：`advance` 吃一条事件、改 O(1) 个字段，因此牌桌不必每帧重头 fold 全流。
type SeatStream =
    private
        {
            Ruleset: Ruleset
            /// 看的那个座位。它不在规则集里时这条流没有意义，`observation` 恒为 None。
            Viewer: Seat
            /// 掩蔽之后的历史，**倒序**（最新的在头）。对外经 `SeatStream.events` 取正序——
            /// 与 `GameState.Log` 同一个做法，追加才是 O(1)。
            Log: MaskedEvent list
            /// 这一局开局时的既定条件（`start_kyoku` 的搬运）；还没见到那条事件时是 None。
            /// **判役上下文读它**（场风与亲），与局面那边喂给 `GameState.yakuContextFor` 的是同一个记录。
            Opening: KyokuContext option
            /// 自家。**就是引擎那个 `PlayerState`**：自家的一切都看得见，因此这一侧用
            /// `PlayerState` 自己的迁移函数推（`draw` / `discard` / `addNaki` / `refreshFuriten` ……），
            /// 不另写一份。
            Self: PlayerState option
            /// 自家的河，含手切 / 摸切。`PlayerState.Kawa` 只存牌，摸切与否只有事件流知道（DECISIONS 20-2）。
            SelfKawa: KawaEntry list
            /// 自家的巡目（自己摸过几次牌）。
            SelfJunme: int
            /// 他家，按**下家、对家、上家**的顺序（投影要的就是这个顺序）。
            Others: MaskedSeat list
            /// **宣言了、还没成立的那个杠**（抢杠那个窗口）：谁宣言的，与拼出来的那组副露。
            ///
            /// 引擎的 `declareKan` 在这一段**不改局面**（宣言不改局面，因此被抢时无需回滚），
            /// 因此 fold 这一侧也得等：`ankan` / `kakan` 一播出去只把它记在这里，
            /// 下一条事件才宣告结局——是荣和（或三家抢同一个杠判成的途中流局）就原地作废，
            /// 其余每一条都意味着那个杠成立了，这时才升那组副露、才打断一发。
            ///
            /// **票 99 之前这里没有等**：宣言那一刻 fold 就把碰升成了杠、把那张牌从手里拿走，
            /// 于是抢杠那一轮两边差一张牌、差一个副露种类；而被抢之后那个杠永远不成立，
            /// fold 那份再也没回滚——座席的历史、浏览器的回放与 Agent 的观测因此都会显示
            /// **一个从未成立的杠**。
            DeclaredKan: (Seat * Naki) option
            Kyotaku: int
            DoraMarkers: Tile list
            WallRemaining: int
            /// 刚被打出（或宣言的杠亮出）的那一张，且**这个座位荣和得了它**。
            ///
            /// 见逃し（同巡振听）就是它的结局：挂着的这一张最后没被自己荣和，那就是放过了。
            /// **它挂着的那一刻恰好就是引擎在问这个座位的那一刻**，因此决策那一手看到的振听
            /// 与引擎的一致：两边都是「还没放过」。
            PendingRon: bool
            /// 这一局已经被荣和收了尾。**挂着的那一张靠它落地**：双响时自己的 `hora`
            /// 排在优先的那家之后，因此看见别家和了的那一刻还不能断定自己是放过了——
            /// 要等这一局真的没有自己那一条了才算。
            HoraEnded: bool
        }

/// 掩蔽流的 fold：一条事件推一步，随时取得历史与观测。
///
/// **全项目只有这一条掩蔽法则**（票 29a）：除了 `MaskedEvent.forSeat`，再没有第二处
/// 回答「某座位能看见什么」。`Observation` 不再从 `GameState` 直算。
[<RequireQualifiedAccess>]
module SeatStream =

    // ---- 构造 ----

    /// 还一条事件都没吃过的空流。
    let start (ruleset: Ruleset) (viewer: Seat) : SeatStream =
        {
            Ruleset = ruleset
            Viewer = viewer
            Log = []
            Opening = None
            Self = None
            SelfKawa = []
            SelfJunme = 0
            Others = []
            DeclaredKan = None
            Kyotaku = 0
            DoraMarkers = []
            WallRemaining = 0
            PendingRon = false
            HoraEnded = false
        }

    // ---- fold ----

    /// 这一局开局那一刻：前一局的一切归零。**一条流里多局相接**（牌桌的那条流就是），
    /// 因此 `start_kyoku` 是重置而不是叠加。
    let private opened (fields: MaskedStartKyoku) (stream: SeatStream) : SeatStream =
        let ruleset = stream.Ruleset
        let viewer = stream.Viewer

        let scoreOf (seat: Seat) =
            Seat.tryItem seat fields.Scores |> Option.defaultValue ruleset.StartingScore

        let others =
            // `orderFrom` 从自己起按下家方向绕一圈，去掉自己就是「下家、对家、上家」。
            Seat.orderFrom ruleset viewer
            |> List.filter (fun other -> other <> viewer)
            |> List.map (fun other ->
                {
                    Seat = other
                    // 配牌发几张是规则集定的，不靠看他家的牌（那几张在掩蔽流里没有位置）。
                    HandCount = ruleset.HaipaiSize
                    Relative = Seat.distanceFrom ruleset viewer other
                    Jikaze = Seat.jikaze ruleset fields.Oya other
                    Junme = 0
                    Score = scoreOf other
                    Kawa = []
                    Naki = []
                    Riichi = RiichiState.none
                    Ippatsu = false
                })

        { stream with
            Opening =
                Some
                    {
                        Bakaze = fields.Bakaze
                        Kyoku = fields.Kyoku
                        Honba = fields.Honba
                        Kyotaku = fields.Kyotaku
                        Oya = fields.Oya
                        Scores = fields.Scores
                    }
            Self = Some(PlayerState.ofHaipai (scoreOf viewer) None fields.Tehai)
            SelfKawa = []
            SelfJunme = 0
            Others = others
            // 一条流里多局相接，前一局要是停在抢杠那一轮上（走不到：那一轮必然当场收场），
            // 宣言中的那个杠也不该跨到这一局来。
            DeclaredKan = None
            Kyotaku = fields.Kyotaku
            DoraMarkers = [ fields.DoraMarker ]
            // 可摸区的张数是规则集算得出来的：整副牌减王牌再减配牌。
            // 之后每一条 `tsumo` 少一张（岭上牌也是：杠一次可摸区的最后一张补进王牌）。
            WallRemaining = Ruleset.wallSize ruleset - ruleset.DeadWallSize - Ruleset.haipaiTotal ruleset
            PendingRon = false
            HoraEnded = false
        }

    /// 改一家他家；不是他家（自家或越界）时原样返回。
    let private mapOther (seat: Seat) (change: MaskedSeat -> MaskedSeat) (stream: SeatStream) : SeatStream =
        { stream with
            Others =
                stream.Others
                |> List.map (fun other -> if other.Seat = seat then change other else other)
        }

    /// 改自家。
    let private mapSelf (change: PlayerState -> PlayerState) (stream: SeatStream) : SeatStream =
        { stream with
            Self = stream.Self |> Option.map change
        }

    /// 改那一家，不必关心它是自家还是他家。
    let private mapSeat
        (seat: Seat)
        (self: PlayerState -> PlayerState)
        (other: MaskedSeat -> MaskedSeat)
        (stream: SeatStream)
        : SeatStream =
        if seat = stream.Viewer then
            mapSelf self stream
        else
            mapOther seat other stream

    /// 鸣牌后他家手里少了几张：亮出去的那几张离手；加杠只离手加上去的那一张
    /// （底下那组碰早就在副露里）——与 `PlayerState.addNaki` / `addKakan` 拿掉的那几张逐张对应。
    let private nakiHandCost (naki: Naki) : int =
        match Naki.kind naki with
        | NakiKind.Kakan -> 1
        | NakiKind.Pon
        | NakiKind.Chi
        | NakiKind.Ankan
        | NakiKind.Minkan -> List.length (Naki.consumed naki)

    /// 他家鸣了一组：加杠是把原来那组碰**原地换成杠**（副露数不变），其余四种追在末尾。
    let private withNaki (naki: Naki) (seat: MaskedSeat) : MaskedSeat =
        let melds =
            match Naki.kind naki, Naki.taken naki with
            | NakiKind.Kakan, Some added ->
                seat.Naki
                |> List.map (fun each ->
                    if
                        Naki.kind each = NakiKind.Pon
                        && Naki.tiles each
                           |> List.forall (fun tile -> Tile.kindIndex tile = Tile.kindIndex added)
                    then
                        naki
                    else
                        each)
            | _ -> seat.Naki @ [ naki ]

        { seat with
            HandCount = seat.HandCount - nakiHandCost naki
            Naki = melds
            Ippatsu = false
        }

    /// 鸣了一组落到自家身上：加杠是把原来那组碰**原地换成杠**（`PlayerState.addKakan`），
    /// 其余四种追在末尾。与引擎那侧的 `applyKan` / `applyNaki` 逐字同形。
    let private addNakiTo (naki: Naki) (player: PlayerState) : PlayerState =
        match Naki.kind naki, Naki.taken naki with
        | NakiKind.Kakan, Some added -> PlayerState.addKakan added naki player
        | _ -> PlayerState.addNaki naki player

    /// 一发被打断：**任何鸣牌（碰 / 吃与三种杠）都把全场的一发打掉**，鸣的那家自己也算在内
    /// （与 `GameState.interruptIppatsu` 同一条规则）。
    ///
    /// **杠在成立那一刻才打断它**（引擎里这一句在 `applyKan` 里，不在 `declareKan` 里）：
    /// 被抢的那个杠没有发生，因此它不打断一发——抢杠和了的那家拿得到「立直 + 一发 + 抢杠」。
    let private interruptIppatsu (stream: SeatStream) : SeatStream =
        { stream with
            Self = stream.Self |> Option.map PlayerState.clearIppatsu
            Others = stream.Others |> List.map (fun other -> { other with Ippatsu = false })
        }

    /// 从事件里把副露拼回来。拼不出来（走不到：事件是既成事实，必然合法）时是 None。
    ///
    /// 加杠要底下那组碰（责任支付要知道原碰的来源座位），因此要拿那一家现有的副露表。
    let private nakiOf (melds: Naki list) (event: Event) : Naki option =
        let ofResult (result: Result<Naki, NakiError>) =
            match result with
            | Ok naki -> Some naki
            | Error _ -> None

        match event with
        | Pon(_, target, pai, consumed) -> Naki.pon target pai consumed |> ofResult
        | Chi(_, target, pai, consumed) -> Naki.chi target pai consumed |> ofResult
        | Minkan(_, target, pai, consumed) -> Naki.minkan target pai consumed |> ofResult
        | Ankan(_, consumed) -> Naki.ankan consumed |> ofResult
        | Kakan(_, added, _) ->
            melds
            |> List.tryFind (fun each ->
                Naki.kind each = NakiKind.Pon
                && Naki.tiles each
                   |> List.forall (fun tile -> Tile.kindIndex tile = Tile.kindIndex added))
            |> Option.bind (fun pon -> Naki.kakan added pon |> ofResult)
        | _ -> None

    /// 那一家现有的副露表（加杠要在里面找底下那组碰）。
    let private meldsOf (seat: Seat) (stream: SeatStream) : Naki list =
        if seat = stream.Viewer then
            stream.Self |> Option.map PlayerState.naki |> Option.defaultValue []
        else
            stream.Others
            |> List.tryFind (fun other -> other.Seat = seat)
            |> Option.map (fun other -> other.Naki)
            |> Option.defaultValue []

    /// 作废还没落定的立直宣言（`RiichiState.cancel` 对已经成立的立直无效）。
    /// **和了与三家和了共用**：宣言牌被荣和 ⟹ 立直不成立、立直棒不出。
    let private cancelDeclaredRiichi (stream: SeatStream) : SeatStream =
        { stream with
            Self = stream.Self |> Option.map PlayerState.cancelRiichi
            Others =
                stream.Others
                |> List.map (fun other ->
                    { other with
                        Riichi = RiichiState.cancel other.Riichi
                    })
        }

    /// 各家点数按座位升序的增减（和了与流局的 `deltas`）。
    let private settle (deltas: int list) (stream: SeatStream) : SeatStream =
        let deltaOf (seat: Seat) =
            Seat.tryItem seat deltas |> Option.defaultValue 0

        { stream with
            Self = stream.Self |> Option.map (PlayerState.addScore (deltaOf stream.Viewer))
            Others =
                stream.Others
                |> List.map (fun other ->
                    { other with
                        Score = other.Score + deltaOf other.Seat
                    })
        }

    /// 判役要的上下文，**拿看得见的那几样拼**：拼法与局面那边共用 `GameState.yakuContextFor`。
    ///
    /// 里宝牌没翻开——这个座位看不见，因此传空表；它只改番数不改「有没有役」，
    /// 而这里只拿它判后者。海底与岭上同理不填：`Yaku` 里那两条都带着自摸守卫，
    /// 而这里判的是荣和。
    let private yakuContext (chankan: bool) (player: PlayerState) (stream: SeatStream) : YakuContext option =
        stream.Opening
        |> Option.map (fun opening ->
            let firstTurn =
                stream.Log
                |> List.choose MaskedEvent.publicEvent
                |> GameState.firstTurnFor stream.Viewer

            GameState.yakuContextFor
                stream.Ruleset
                opening
                stream.DoraMarkers
                []
                {
                    Rinshan = false
                    Haitei = false
                    Houtei = stream.WallRemaining = 0
                    Chankan = chankan
                }
                firstTurn
                (Some player)
                stream.Viewer)

    /// 这一张自己荣和得了吗。**判据与引擎共用一份**（`GameState.canRon`）：
    /// 引擎拿它产合法动作集，这里拿它判见逃し。
    let private canRon (robbing: NakiKind option) (pai: Tile) (stream: SeatStream) : bool =
        match stream.Self with
        | None -> false
        | Some player ->
            match yakuContext (Option.isSome robbing) player stream with
            | None -> false
            | Some context -> GameState.canRon stream.Ruleset context robbing player pai

    /// 挂着的那一张落定了：不是自己荣和的——就是见逃し，同巡振听成立（`PlayerState.minogashi`）。
    ///
    /// **三家和了不算见逃**：那三家宣的就是荣和，只是规则把它判成了途中流局。
    /// 谁宣了写在 `ryukyoku` 的 `tenpais` 里（`Ryuukyoku.revealedBy`），因此流里读得出来。
    let private minogashiOn (next: MaskedEvent) (viewer: Seat) : bool =
        match MaskedEvent.publicEvent next with
        | Some(Ryuukyoku fields) when fields.Reason = SanchaHora ->
            Seat.tryItem viewer fields.Tenpais |> Option.defaultValue false |> not
        | _ -> true

    /// 挂着的那一张遇上下一条事件：要么落地，要么接着挂着。
    ///
    /// **自己和了就不是见逃**；而看见别家和了时不能当场断定（双响里自己的那条 `hora`
    /// 排在优先的那家之后），挂到这一局真的没有自己那一条为止——那一刻由 `observation` 结算。
    let private resolvePendingRon (next: MaskedEvent) (stream: SeatStream) : SeatStream =
        if not stream.PendingRon then
            stream
        else
            match MaskedEvent.publicEvent next with
            | Some(Hora fields) when fields.Actor = stream.Viewer -> { stream with PendingRon = false }
            | Some(Hora _) -> { stream with HoraEnded = true }
            | _ ->
                let cleared = { stream with PendingRon = false }

                if minogashiOn next stream.Viewer then
                    mapSelf PlayerState.minogashi cleared
                else
                    cleared

    /// 下一条事件宣告那个宣言中的杠**没有发生**吗：抢杠的荣和，或三家抢同一个杠
    /// 被判成的途中流局（`SanchaHora`）——两条路引擎都不跑 `applyKan`。
    ///
    /// 其余每一条都意味着那个杠成立了：成立那一刻引擎当场接上翻宝牌与补摸岭上牌
    /// （`completeKan`），因此宣言之后不可能什么事也不发生。
    /// 四杠散了不在此列：它要等杠的那家打完一张才判（`Ryuukyoku.afterDahai`），
    /// 那时这个杠早已落定。
    let private robsDeclaredKan (next: MaskedEvent) : bool =
        match MaskedEvent.publicEvent next with
        | Some(Hora _) -> true
        | Some(Ryuukyoku fields) -> fields.Reason = SanchaHora
        | _ -> false

    /// 宣言中的那个杠遇上下一条事件：要么成立（副露升级、手里那几张离手、全场一发打断），
    /// 要么**原地作废**（被抢掉了，那个杠从来没有发生，因此也无需回滚）。
    ///
    /// **它与引擎同时动**：引擎那侧是响应收齐后的 `applyKan`，而 `applyKan` 当场就会
    /// 产出下一条事件（`dora` / `tsumo`）——fold 这一侧看到那条事件就是看到了那一刻。
    let private settleDeclaredKan (next: MaskedEvent) (stream: SeatStream) : SeatStream =
        match stream.DeclaredKan with
        | None -> stream
        | Some(actor, naki) ->
            let cleared = { stream with DeclaredKan = None }

            if robsDeclaredKan next then
                cleared
            else
                cleared |> mapSeat actor (addNakiTo naki) (withNaki naki) |> interruptIppatsu

    /// 吃一条**已经掩蔽好的**事件。
    ///
    /// **fold 的全部输入就是它**：这个函数看不到未掩蔽的事件，也看不到 `GameState`——
    /// 「观测是掩蔽流的 fold」因此是类型上的事实而不是约定。
    ///
    /// **先把上一条宣言的杠结了**（`settleDeclaredKan`）：那个杠成不成立，答案就写在
    /// 手里这一条事件上。两步互不相干：一步改副露与一发，另一步（挂着的那张荣和）改振听。
    let absorb (masked: MaskedEvent) (stream: SeatStream) : SeatStream =
        let viewer = stream.Viewer
        let settled = settleDeclaredKan masked stream
        let resolved = resolvePendingRon masked settled

        let advanced =
            match masked with
            | MaskedEvent.StartKyoku fields -> opened fields resolved
            | MaskedEvent.Tsumo(actor, pai) ->
                let drawn =
                    { resolved with
                        WallRemaining = resolved.WallRemaining - 1
                    }
                    |> mapSeat
                        actor
                        // 摸到的那张看不见时（走不到：自家那条恒有牌面）只当同巡振听解除。
                        (fun player ->
                            match pai with
                            | Some tile -> PlayerState.draw tile player
                            | None -> player)
                        (fun other ->
                            { other with
                                HandCount = other.HandCount + 1
                                Junme = other.Junme + 1
                            })

                if actor = viewer then
                    { drawn with
                        SelfJunme = drawn.SelfJunme + 1
                    }
                else
                    drawn
            | MaskedEvent.Public event ->
                match event with
                | Dahai(actor, pai, tsumogiri) ->
                    let discarded =
                        resolved
                        |> mapSeat
                            actor
                            // 打完这张，自家的听牌就变了，永久振听要按新的听牌与自己的河重算
                            // （与 `GameState.applyDahai` 同一时机、同一个函数）。
                            (PlayerState.discard pai >> PlayerState.refreshFuriten resolved.Ruleset.TileKinds)
                            (fun other ->
                                { other with
                                    HandCount = other.HandCount - 1
                                    Kawa = other.Kawa @ [ { Pai = pai; Tsumogiri = tsumogiri } ]
                                    Ippatsu = false
                                })

                    let withKawa =
                        if actor = viewer then
                            { discarded with
                                SelfKawa = discarded.SelfKawa @ [ { Pai = pai; Tsumogiri = tsumogiri } ]
                            }
                        else
                            discarded

                    { withKawa with
                        PendingRon = actor <> viewer && canRon None pai withKawa
                    }
                // 碰 / 吃 / 大明杠都是**既成事实**：事件播出去的那一刻引擎早已落定（它们是对打牌的
                // 响应，而响应收齐才产事件），因此这一侧当场就改。
                | Pon(actor, _, _, _)
                | Chi(actor, _, _, _)
                | Minkan(actor, _, _, _) ->
                    match nakiOf (meldsOf actor resolved) event with
                    | None -> resolved
                    | Some naki -> resolved |> mapSeat actor (addNakiTo naki) (withNaki naki) |> interruptIppatsu
                // 暗杠 / 加杠只是**宣言**：引擎要先问抢杠，都不要这个杠它才成立（`declareKan`）。
                // **宣言不改局面**，因此这里只把它记下来，等下一条事件宣告结局（`settleDeclaredKan`）：
                // 升副露、拿牌、打断一发都得等到那一刻。不等的后果是票 99：
                // 被抢之后那个杠永远不成立，而牌桌上一直摆着它。
                | Ankan(actor, _)
                | Kakan(actor, _, _) ->
                    let declared =
                        match nakiOf (meldsOf actor resolved) event with
                        | None -> resolved
                        | Some naki ->
                            { resolved with
                                DeclaredKan = Some(actor, naki)
                            }

                    // 抢杠：宣言的那个杠可以被荣和（加杠任何牌型、暗杠只有国士）。
                    // 判据里的手牌就是当下这份——它本来就没变。
                    let robbed =
                        match event with
                        | Kakan(_, pai, _) -> Some(NakiKind.Kakan, pai)
                        // 暗杠被抢的那张：四张同种里的任一张（只有国士抢得了）。
                        | Ankan(_, consumed) -> consumed |> List.tryHead |> Option.map (fun pai -> NakiKind.Ankan, pai)
                        | _ -> None

                    match robbed with
                    | Some(kind, pai) when actor <> viewer ->
                        { declared with
                            PendingRon = canRon (Some kind) pai declared
                        }
                    | _ -> declared
                | Dora doraMarker ->
                    { resolved with
                        DoraMarkers = resolved.DoraMarkers @ [ doraMarker ]
                    }
                | Riichi actor ->
                    // 两立直的判据与局面那边共用一份（`GameState.firstTurnFor`）。
                    let declaration =
                        if
                            resolved.Log
                            |> List.choose MaskedEvent.publicEvent
                            |> GameState.firstTurnFor actor
                        then
                            RiichiDeclaration.DoubleRiichi
                        else
                            RiichiDeclaration.Riichi

                    resolved
                    |> mapSeat actor (PlayerState.declareRiichi declaration) (fun other ->
                        { other with
                            Riichi = RiichiState.declare declaration
                        })
                | RiichiAccepted actor ->
                    // 立直棒这时才出：扣点、供托 +1、一发亮起。
                    { resolved with
                        Kyotaku = resolved.Kyotaku + 1
                    }
                    |> mapSeat
                        actor
                        (PlayerState.acceptRiichi >> PlayerState.addScore (-resolved.Ruleset.RiichiBou))
                        (fun other ->
                            { other with
                                Riichi = RiichiState.accept other.Riichi
                                Ippatsu = true
                                Score = other.Score - resolved.Ruleset.RiichiBou
                            })
                | Hora fields ->
                    // 宣言牌被荣和的那家立直不成立（立直棒也就不出）；
                    // 供托已经进了和了者的 `deltas`，场上不再剩立直棒。
                    let settled = settle fields.Deltas resolved |> cancelDeclaredRiichi

                    { settled with Kyotaku = 0 }
                | Ryuukyoku fields ->
                    let settled = settle fields.Deltas resolved

                    match fields.Reason with
                    // 三家和了：打牌那家的立直宣言与被荣和同理不成立（引擎那边同一条）。
                    | SanchaHora -> cancelDeclaredRiichi settled
                    | Fanpai
                    | NagashiMangan
                    | KyuushuKyuuhai
                    | SuufonRenda
                    | Suukaikan
                    | SuuchaRiichi -> settled
                | StartGame _
                | EndKyoku
                | EndGame -> resolved
                // 走不到：这两条在掩蔽之后不是 `Public`（它们各自有 case）。
                | StartKyoku _
                | Tsumo _ -> resolved

        { advanced with
            Log = masked :: advanced.Log
        }

    /// 吃一条**未掩蔽的**事件：先掩蔽，再 fold。牌桌与引擎那边拿到的是未掩蔽的事件流，
    /// 因此入口在这里；掩蔽之后的那一步走 `absorb`，它看不到未掩蔽的任何东西。
    let advance (event: Event) (stream: SeatStream) : SeatStream =
        match MaskedEvent.forSeat stream.Viewer event with
        | Some masked -> absorb masked stream
        | None -> stream

    /// 吃一串未掩蔽的事件。
    let advanceAll (events: Event list) (stream: SeatStream) : SeatStream =
        (stream, events) ||> List.fold (fun current event -> advance event current)

    // ---- 拆解 ----

    /// **座席的历史**：掩蔽之后的事件流，按发生顺序。29b 的 prompt 前缀读它。
    let events (stream: SeatStream) : MaskedEvent list = List.rev stream.Log

    /// 这条流 fold 到此刻的观测。座位不在规则集里、或还没见到 `start_kyoku` 时是 None。
    let observation (stream: SeatStream) : Observation option =
        // 荣和收尾时还挂着的那一张就是见逃し：这一局已经没有自己那条 `hora` 了。
        // 一轮响应还没收齐时它恒不落地（`HoraEnded` 为假）——那正是引擎在问这一家的时候，
        // 而引擎那边同样要等这一轮收齐才落（`AwaitingResponse.Minogashi`）。
        let settled =
            if stream.PendingRon && stream.HoraEnded then
                mapSelf PlayerState.minogashi stream
            else
                stream

        match settled.Opening, settled.Self with
        | Some opening, Some player when Seat.isValid settled.Ruleset settled.Viewer ->
            Some
                {
                    Seat = settled.Viewer
                    Bakaze = opening.Bakaze
                    Kyoku = opening.Kyoku
                    Honba = opening.Honba
                    Kyotaku = settled.Kyotaku
                    DoraMarkers = settled.DoraMarkers
                    WallRemaining = settled.WallRemaining
                    Self =
                        {
                            Seat = settled.Viewer
                            Jikaze = Seat.jikaze settled.Ruleset opening.Oya settled.Viewer
                            Junme = settled.SelfJunme
                            Score = PlayerState.score player
                            Hand = PlayerState.hand player
                            Drawn = PlayerState.drawn player
                            Kawa = settled.SelfKawa
                            Naki = PlayerState.naki player
                            Riichi = PlayerState.riichi player
                            Ippatsu = PlayerState.ippatsu player
                            Furiten = PlayerState.furiten player
                        }
                    Others = settled.Others
                }
        | _ -> None

/// 观测投影：局面 + 座位 → 那个座位的合法观测。
[<RequireQualifiedAccess>]
module Observation =

    // ---- 掩蔽流 ----

    /// 某座位的**掩蔽事件流**：这一局里它亲眼看得见的那条历史（票 29a）。
    /// **观测就是它的 fold**，因此两者不可能对不上。
    let stream (seat: Seat) (state: GameState) : MaskedEvent list =
        GameState.events state |> MaskedEvent.stream seat

    // ---- 投影 ----

    /// 一条掩蔽流 → 那个座位的合法观测。流里还没有 `start_kyoku`（或座位越界）时是 None。
    let ofMasked (ruleset: Ruleset) (seat: Seat) (masked: MaskedEvent list) : Observation option =
        (SeatStream.start ruleset seat, masked)
        ||> List.fold (fun stream event -> SeatStream.absorb event stream)
        |> SeatStream.observation

    /// 一条未掩蔽的事件流 → 那个座位的合法观测（先掩蔽再 fold）。
    let ofEvents (ruleset: Ruleset) (seat: Seat) (events: Event list) : Observation option =
        SeatStream.start ruleset seat
        |> SeatStream.advanceAll events
        |> SeatStream.observation

    /// 局面 → 某座位的合法观测。**它不再从 `GameState` 直算**（票 29a）：
    /// 局面先出事件流，事件流经唯一那条掩蔽法则变成座席的历史，历史 fold 成观测。
    /// 座位不在这个规则集里时是 None。
    ///
    /// **一次性全流 fold**：逐手推进的牌桌别每帧调它，拿 `SeatStream` 增量维护。
    let ofState (seat: Seat) (state: GameState) : Observation option =
        ofEvents (GameState.ruleset state) seat (GameState.events state)

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

    // ---- 未掩蔽流 ----

    /// 上帝视角那条流：**一条不掩、一张不隐**，就是这一局的事件流本身（ADR-0002 的 Paifu）。
    /// 围观与 M2 复盘读它——没有第二种「全都看得见」的表示。
    ///
    /// **它不叫掩蔽**：`MaskedEvent.forSeat` 那条法则的定义域是「某个座位」，
    /// 而上帝视角压根没有观测者（20-1 的同一条理由）。
    let stream (state: GameState) : Event list = GameState.events state

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
