namespace Janpo

open Thoth.Json.Core

/// mjai `start_kyoku` 的载荷：一局开始时公开的全部事实。
/// 字段名与 mjai wire 1:1（`bakaze` / `dora_marker` / `kyoku` / `honba` / `kyotaku` /
/// `oya` / `scores` / `tehais`）。
type StartKyoku =
    {
        /// 场风。
        Bakaze: Kaze
        /// 局数，1 起。「东 1 局」= `Bakaze = Ton`、`Kyoku = 1`。
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
        /// 各家配牌，按座位升序；Oya 的第一张自摸是随后的 `Tsumo` 事件，不含在这里。
        Tehais: Tile list list
    }

/// 引擎产出的**既成事实**，必然合法（CONTEXT.md）。case 名与字段贴 mjai wire 事件 1:1，
/// 与 `Action`（Player 提交的意图，可以非法）是两个类型，允许 case 同名。
///
/// **这个 DU 会被后续的票反复加 case**（`dahai` / `reach` / `pon` / `chi` / `kan` /
/// `hora` / `ryukyoku` / `end_kyoku` / `end_game` …）。加一个 case 的代价固定为三处，
/// 漏掉哪处编译器都会指出来（`--warnaserror` 下不完整 match 是错误）：
///
/// 1. 这里加一个 case——字段少的直接内联具名字段（`Tsumo of actor: Seat * pai: Tile`）；
///    字段多、或含多个同类型标量（几个 int 排在一起容易传错位）的，另立一个记录载荷，
///    像 `StartKyoku` 那样。case 名用 mjai 的事件名转 PascalCase，不自创。
/// 2. `Event.encoder` 加一支：`mjaiEvent "<mjai 事件名>" [ 字段名, 编码 … ]`。
/// 3. `Event.decoder` 的 `type` 分派加一支，与第 2 步逐字段对称。
///
/// 事件本身不带渲染出口：它是 wire 数据，中文由 UI 按结构自己拼（ADR-0001）。
type Event =
    /// mjai `start_game`：一场对局开始。`names` 按座位升序。
    | StartGame of names: string list
    /// mjai `start_kyoku`：一局开始，含配牌、各家点数与首张表宝牌指示牌。
    | StartKyoku of startKyoku: StartKyoku
    /// mjai `tsumo`：某家从牌山摸进一张。
    | Tsumo of actor: Seat * pai: Tile

/// 事件的 JSON 编解码（mjai wire）。
[<RequireQualifiedAccess>]
module Event =

    // ---- JSON（mjai wire） ----

    /// 每个 mjai 事件都是一个带 `type` 的 JSON 对象。
    let private mjaiEvent (eventType: string) (fields: (string * IEncodable) list) : IEncodable =
        Encode.object (("type", Encode.string eventType) :: fields)

    let private encodeTiles (tiles: Tile list) : IEncodable =
        tiles |> List.map Tile.encoder |> Encode.list

    let encoder: Encoder<Event> =
        fun event ->
            match event with
            | StartGame names -> mjaiEvent "start_game" [ "names", names |> List.map Encode.string |> Encode.list ]
            | StartKyoku fields ->
                mjaiEvent
                    "start_kyoku"
                    [
                        "bakaze", Kaze.encoder fields.Bakaze
                        "dora_marker", Tile.encoder fields.DoraMarker
                        "kyoku", Encode.int fields.Kyoku
                        "honba", Encode.int fields.Honba
                        "kyotaku", Encode.int fields.Kyotaku
                        "oya", Encode.int fields.Oya
                        "scores", fields.Scores |> List.map Encode.int |> Encode.list
                        "tehais", fields.Tehais |> List.map encodeTiles |> Encode.list
                    ]
            | Tsumo(actor, pai) -> mjaiEvent "tsumo" [ "actor", Encode.int actor; "pai", Tile.encoder pai ]

    let private tilesDecoder: Decoder<Tile list> = Decode.list Tile.decoder

    let private startKyokuDecoder: Decoder<Event> =
        Decode.object (fun get ->
            StartKyoku
                {
                    Bakaze = get.Required.Field "bakaze" Kaze.decoder
                    Kyoku = get.Required.Field "kyoku" Decode.int
                    Honba = get.Required.Field "honba" Decode.int
                    Kyotaku = get.Required.Field "kyotaku" Decode.int
                    Oya = get.Required.Field "oya" Decode.int
                    DoraMarker = get.Required.Field "dora_marker" Tile.decoder
                    Scores = get.Required.Field "scores" (Decode.list Decode.int)
                    Tehais = get.Required.Field "tehais" (Decode.list tilesDecoder)
                })

    /// 解码失败是 Decoder 的错误值，不抛异常。诊断文案用英文（ADR-0001）。
    let decoder: Decoder<Event> =
        Decode.field "type" Decode.string
        |> Decode.andThen (fun eventType ->
            match eventType with
            | "start_game" -> Decode.field "names" (Decode.list Decode.string) |> Decode.map StartGame
            | "start_kyoku" -> startKyokuDecoder
            | "tsumo" ->
                Decode.map2
                    (fun actor pai -> Tsumo(actor, pai))
                    (Decode.field "actor" Decode.int)
                    (Decode.field "pai" Tile.decoder)
            | other -> Decode.fail ("unknown mjai event type: " + other))
