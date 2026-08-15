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

/// mjai `hora` 的载荷：一次和了公开的全部事实。
///
/// **06 票不算点数**：`Fu` / `Fan` / `HoraPoints` 一律记 0，`Deltas` 全 0，`Scores` 就是和了前的
/// 各家点数——字段在这里立好，08 票把值填正确。役种（`yakus`）与里宝牌指示牌
/// （`uradora_markers`）分别是 07 与 09 的事，那两票给这个记录加字段。
type Hora =
    {
        /// 和了的座位。
        Actor: Seat
        /// 放铳的座位；自摸时等于 `Actor`（mjai 的约定）。
        Target: Seat
        /// 和了的那张牌：自摸是刚摸进那张，荣和是 `Target` 刚打出那张。
        Pai: Tile
        /// 符。06 记 0，08 填。
        Fu: int
        /// 番。06 记 0，07 判役、08 填。
        Fan: int
        /// 和了点（不含本场与供托）。06 记 0，08 填。
        HoraPoints: int
        /// 本次授受的点数增减，按座位升序，和为 0。06 全记 0。
        Deltas: int list
        /// 授受后的各家点数，按座位升序。06 就是和了前的点数。
        Scores: int list
    }

/// 流局的形态，mjai `ryukyoku` 的 `reason` 字段。04 只做荒牌流局，12 票补齐其余五种
/// （九种九牌、四风连打、四杠散了、四家立直、三家和了）与流し満貫。
type RyuukyokuReason =
    /// 荒牌流局：可摸区摸完，按听牌家数授受听牌料。
    /// mjai wire 上写作 `fanpai`（荒牌的音读），不是「翻牌 / 飜牌」。
    | Fanpai

/// mjai `ryukyoku` 的载荷：一局在无人和了的情况下结束时公开的全部事实。
///
/// mjai 原始规格里还有一个 `tehais`（听牌家亮手、其余家写 `?`）。本项目不带它：
/// `Tile` 表示不了「未知牌」，而听牌与否已经由 `Tenpais` 说清楚了。
type Ryuukyoku =
    {
        /// 流局的形态。
        Reason: RyuukyokuReason
        /// 各家是否听牌，按座位升序。听牌料的授受与 05 的连庄判定都读它。
        Tenpais: bool list
        /// 本次授受的点数增减，按座位升序，和为 0。
        Deltas: int list
        /// 授受后的各家点数，按座位升序。
        Scores: int list
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
    /// mjai `dahai`：某家打出一张。`tsumogiri` 为真表示打的就是刚摸进的那张（摸切）。
    | Dahai of actor: Seat * pai: Tile * tsumogiri: bool
    /// mjai `hora`：某家和了。同巡双响时会有两条（头跳关掉时才可能出现）。
    | Hora of hora: Hora
    /// mjai `ryukyoku`：一局在无人和了的情况下结束。
    ///
    /// F# 这侧的标识符按 CONTEXT.md 的术语表拼作 `Ryuukyoku`，wire 上仍是 mjai 的
    /// `ryukyoku`——与 `Kaze` 同一处理（记法/拼法归 ADR-0001 与术语表，wire 归 mjai）。
    | Ryuukyoku of ryuukyoku: Ryuukyoku

/// 流局形态的 mjai 记法与 JSON 编解码。
[<RequireQualifiedAccess>]
module RyuukyokuReason =

    // ---- mjai 记法 ----

    /// 全部形态。12 票加 case 时这里跟着加，`parse` 不必改。
    let all: RyuukyokuReason list = [ Fanpai ]

    /// mjai `reason` 字段的取值。
    let toMjai (reason: RyuukyokuReason) : string =
        match reason with
        | Fanpai -> "fanpai"

    /// 解析 mjai `reason`；不认识的形态返回 None。
    let parse (text: string) : RyuukyokuReason option =
        all |> List.tryFind (fun reason -> toMjai reason = text)

    // ---- JSON（mjai wire） ----

    let encoder: Encoder<RyuukyokuReason> = fun reason -> Encode.string (toMjai reason)

    /// 解码失败是 Decoder 的错误值，不抛异常。诊断文案用英文（ADR-0001）。
    let decoder: Decoder<RyuukyokuReason> =
        Decode.string
        |> Decode.andThen (fun text ->
            match parse text with
            | Some reason -> Decode.succeed reason
            | None -> Decode.fail ("unknown mjai ryukyoku reason: " + text))

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
            | Dahai(actor, pai, tsumogiri) ->
                mjaiEvent
                    "dahai"
                    [
                        "actor", Encode.int actor
                        "pai", Tile.encoder pai
                        "tsumogiri", Encode.bool tsumogiri
                    ]
            | Hora fields ->
                mjaiEvent
                    "hora"
                    [
                        "actor", Encode.int fields.Actor
                        "target", Encode.int fields.Target
                        "pai", Tile.encoder fields.Pai
                        "fu", Encode.int fields.Fu
                        "fan", Encode.int fields.Fan
                        "hora_points", Encode.int fields.HoraPoints
                        "deltas", fields.Deltas |> List.map Encode.int |> Encode.list
                        "scores", fields.Scores |> List.map Encode.int |> Encode.list
                    ]
            | Ryuukyoku fields ->
                mjaiEvent
                    "ryukyoku"
                    [
                        "reason", RyuukyokuReason.encoder fields.Reason
                        "tenpais", fields.Tenpais |> List.map Encode.bool |> Encode.list
                        "deltas", fields.Deltas |> List.map Encode.int |> Encode.list
                        "scores", fields.Scores |> List.map Encode.int |> Encode.list
                    ]

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

    let private horaDecoder: Decoder<Event> =
        Decode.object (fun get ->
            Hora
                {
                    Actor = get.Required.Field "actor" Decode.int
                    Target = get.Required.Field "target" Decode.int
                    Pai = get.Required.Field "pai" Tile.decoder
                    Fu = get.Required.Field "fu" Decode.int
                    Fan = get.Required.Field "fan" Decode.int
                    HoraPoints = get.Required.Field "hora_points" Decode.int
                    Deltas = get.Required.Field "deltas" (Decode.list Decode.int)
                    Scores = get.Required.Field "scores" (Decode.list Decode.int)
                })

    let private ryuukyokuDecoder: Decoder<Event> =
        Decode.object (fun get ->
            Ryuukyoku
                {
                    Reason = get.Required.Field "reason" RyuukyokuReason.decoder
                    Tenpais = get.Required.Field "tenpais" (Decode.list Decode.bool)
                    Deltas = get.Required.Field "deltas" (Decode.list Decode.int)
                    Scores = get.Required.Field "scores" (Decode.list Decode.int)
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
            | "dahai" ->
                Decode.map3
                    (fun actor pai tsumogiri -> Dahai(actor, pai, tsumogiri))
                    (Decode.field "actor" Decode.int)
                    (Decode.field "pai" Tile.decoder)
                    (Decode.field "tsumogiri" Decode.bool)
            | "hora" -> horaDecoder
            | "ryukyoku" -> ryuukyokuDecoder
            | other -> Decode.fail ("unknown mjai event type: " + other))
