namespace Janpo

open Thoth.Json.Core

/// 掩蔽之后的 `start_kyoku`：**他家的配牌在这个记录里没有位置**。
///
/// 与 `MaskedSeat` 同源（DECISIONS 20-1）：看不见的东西不给它字段，
/// 掩蔽函数想漏也漏不出去。其余字段与 mjai 的 `start_kyoku` 逐字段相同——
/// 场风、局数、本场、供托、亲、首张表宝牌指示牌与各家点数都是牌桌上摆着的。
type MaskedStartKyoku =
    {
        /// 场风。
        Bakaze: Kaze
        /// 局数，1 起。
        Kyoku: int
        /// 本场。
        Honba: int
        /// 供托：场上堆着的立直棒数。
        Kyotaku: int
        /// 亲。
        Oya: Seat
        /// 开局翻开的那张表宝牌指示牌。
        DoraMarker: Tile
        /// 各家点数，按座位升序。
        Scores: int list
        /// **自家**的配牌。他家的配牌不在这里——它们没有位置。
        Tehai: Tile list
    }

/// 一条事件在某座位眼里的样子（票 29a）：**掩蔽只定义在这里一处**。
///
/// mjai 的十七条事件里只有两条带着看不见的东西——配牌（只见自家）与摸牌（他家摸的
/// 牌面看不见）——其余每一条都是牌桌上公开发生的，因此原样带着（`Public`）。
/// 加一条 mjai 事件不必动这个 DU，但**得在 `MaskedEvent.forSeat` 的 match 里表态**：
/// 那个 match 穷举 `Event` 的每一个 case，漏掉哪个编译器会当面指出来。
///
/// **暗杠不在掩蔽之列**：日麻的暗杠亮着两张，牌种是公开信息（国士抢暗杠这条规则的前提
/// 就是它看得见），20 号票的投影也一直把他家的暗杠原样给出来。见 DECISIONS 29a-2。
///
/// case 强制限定名（`MaskedEvent.Tsumo`）：`Event` 里有同名 case，不限定名的话后定义的
/// 会把先定义的遮住，读代码的人分不出手里的是事实还是那个座位眼里的事实（裁决 D-3）。
[<RequireQualifiedAccess>]
type MaskedEvent =
    /// 一局开始：只看得见自家的配牌。
    | StartKyoku of startKyoku: MaskedStartKyoku
    /// 某家摸了一张。**他家摸的是什么看不见**（`pai` 为 None），但「他摸了」看得见——
    /// 巡目与「手切摸切的时序」全靠这一条撑着。
    | Tsumo of actor: Seat * pai: Tile option
    /// 其余每一条 mjai 事件：打牌、鸣牌、宝牌、立直、和了、流局与两条收尾事件。
    /// 它们逐字段都是公开信息，掩蔽之后一字不改。
    | Public of event: Event

/// 掩蔽函数与掩蔽流。
///
/// **这是全项目唯一的一条掩蔽法则**（票 29a）：`Observation` 是这条流的 fold
/// （`SeatStream`），29b 的 prompt 前缀是这条流本身。没有第二处判断「某座位能看见什么」。
[<RequireQualifiedAccess>]
module MaskedEvent =

    // ---- 掩蔽 ----

    /// 一条事件在某座位眼里的样子；这个座位压根看不见的事件是 None。
    ///
    /// **返回 option 是因为掩蔽的定义域是「可见与否」**，不是因为现在有哪条看不见：
    /// mjai 的每条事件都至少让人看得见「发生了什么」（他家摸牌看得见他摸了、
    /// 只是看不见摸的是什么），因此本项目现在恒为 Some。往后真出现「某座位不该知道
    /// 这件事发生过」的事件时，它落在这里，不必再改一次签名。
    let forSeat (viewer: Seat) (event: Event) : MaskedEvent option =
        match event with
        | Event.StartKyoku fields ->
            Some(
                MaskedEvent.StartKyoku
                    {
                        Bakaze = fields.Bakaze
                        Kyoku = fields.Kyoku
                        Honba = fields.Honba
                        Kyotaku = fields.Kyotaku
                        Oya = fields.Oya
                        DoraMarker = fields.DoraMarker
                        Scores = fields.Scores
                        // 座位越界时是空表：那条流本来就没有意义，`SeatStream` 在入口挡掉。
                        Tehai = Seat.tryItem viewer fields.Tehais |> Option.defaultValue []
                    }
            )
        | Event.Tsumo(actor, pai) -> Some(MaskedEvent.Tsumo(actor, (if actor = viewer then Some pai else None)))
        // 以下每一条逐字段都是公开信息。**这个 match 故意穷举**：新增 mjai 事件时
        // 编译器会在这里报不完整，逼着加的人回答「这条事件有没有看不见的部分」。
        | Event.StartGame _
        | Event.Dahai _
        | Event.Pon _
        | Event.Chi _
        | Event.Ankan _
        | Event.Kakan _
        | Event.Minkan _
        | Event.Dora _
        | Event.Riichi _
        | Event.RiichiAccepted _
        | Event.Hora _
        | Event.Ryuukyoku _
        | Event.EndKyoku
        | Event.EndGame -> Some(MaskedEvent.Public event)

    /// 一整条流在某座位眼里的样子。**座席的历史就是它**。
    let stream (viewer: Seat) (events: Event list) : MaskedEvent list = events |> List.choose (forSeat viewer)

    // ---- 拆解 ----

    /// 掩蔽之后仍是原样的那条 mjai 事件；配牌与摸牌没有原样（它们被掩蔽了）。
    ///
    /// **它让「只读公开事件」的判据不必写两遍**：`GameState.firstTurnFor`
    /// （天和 / 地和、两立直、九种九牌共用的那条）看的是打牌与鸣牌，两者都是 `Public`，
    /// 因此掩蔽流经这里滤一道就能喂给引擎里的那一份。
    let publicEvent (masked: MaskedEvent) : Event option =
        match masked with
        | MaskedEvent.Public event -> Some event
        | MaskedEvent.StartKyoku _
        | MaskedEvent.Tsumo _ -> None

    /// 这条掩蔽事件里出现的每一张牌。**「不泄露」那条属性测试读它**：
    /// 掩蔽流里的每一张都必须是这个座位看得见的。
    let tiles (masked: MaskedEvent) : Tile list =
        match masked with
        | MaskedEvent.StartKyoku fields -> fields.DoraMarker :: fields.Tehai
        | MaskedEvent.Tsumo(_, pai) -> Option.toList pai
        | MaskedEvent.Public event ->
            match event with
            | Event.Dahai(_, pai, _) -> [ pai ]
            | Event.Pon(_, _, pai, consumed)
            | Event.Chi(_, _, pai, consumed)
            | Event.Minkan(_, _, pai, consumed)
            | Event.Kakan(_, pai, consumed) -> pai :: consumed
            | Event.Ankan(_, consumed) -> consumed
            | Event.Dora doraMarker -> [ doraMarker ]
            | Event.Hora fields -> fields.Pai :: fields.UraDoraMarkers
            | Event.StartGame _
            | Event.StartKyoku _
            | Event.Tsumo _
            | Event.Riichi _
            | Event.RiichiAccepted _
            | Event.Ryuukyoku _
            | Event.EndKyoku
            | Event.EndGame -> []

    // ---- JSON（单向出口） ----

    /// mjai 里「这一格看不见」的写法。上游的 mjai 服务端发给某一家的事件流就是这么写的：
    /// 他家摸的那张是 `"pai": "?"`，他家的配牌是一串 `"?"`。
    /// **掩蔽流因此不需要第二种记法**，读的人拿 mjai 的眼睛就看得懂。
    [<Literal>]
    let private hidden = "?"

    /// 掩蔽事件的 wire 形态。**只有 encoder，没有 decoder**（票 29b）：
    /// 历史只往外走一个方向（引擎 → prompt 的前缀），回来的仍然只有一个动作 id。
    ///
    /// **`Public` 那一半原样复用 `Event.encoder`**（裁决：第六次裁决的约束）：
    /// 十四条公开事件在 wire 上与 mjai 逐字段相同，掩蔽流里不新造第二种事件记法。
    /// 掩蔽掉了东西的那两条也仍然是 mjai 的形状，只是看不见的那一格写 `"?"`：
    /// - `tsumo`：他家摸的那张是 `"?"`（自家那条照旧是牌面）
    /// - `start_kyoku`：mjai 的 `tehais` 是四家的配牌，掩蔽之后他家那三格**没有内容可放**
    ///   （`MaskedStartKyoku` 里没有字段装得下），因此写作 `tehai` —— 自家那一手。
    let encoder: Encoder<MaskedEvent> =
        fun masked ->
            match masked with
            | MaskedEvent.StartKyoku fields ->
                Encode.object
                    [
                        "type", Encode.string "start_kyoku"
                        "bakaze", Kaze.encoder fields.Bakaze
                        "dora_marker", Tile.encoder fields.DoraMarker
                        "kyoku", Encode.int fields.Kyoku
                        "honba", Encode.int fields.Honba
                        "kyotaku", Encode.int fields.Kyotaku
                        "oya", Seat.encoder fields.Oya
                        "scores", fields.Scores |> List.map Encode.int |> Encode.list
                        "tehai", fields.Tehai |> List.map Tile.encoder |> Encode.list
                    ]
            | MaskedEvent.Tsumo(actor, pai) ->
                Encode.object
                    [
                        "type", Encode.string "tsumo"
                        "actor", Seat.encoder actor
                        "pai", pai |> Option.map Tile.toMjai |> Option.defaultValue hidden |> Encode.string
                    ]
            | MaskedEvent.Public event -> Event.encoder event
