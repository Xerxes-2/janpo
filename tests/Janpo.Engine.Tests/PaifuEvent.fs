namespace Janpo.Engine.Tests

open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo

/// 牌谱侧的记法适配。上游 mjai 数据集把字牌写成字母（`E` `S` `W` `N` `P` `F` `C`，
/// `start_kyoku.bakaze` 亦然），而 ADR-0001 定的内部规范形只有 `1z`-`7z`。
///
/// **映射只此一处**，而且方向是单向的：把牌谱记法改写成 mjai 记法，再交给引擎的
/// `Tile.parse` / `Kaze.parse`。放宽 `Tile.parse` 是另一条路，但那会让第二种记法
/// 从边界渗回事件流与牌谱——ADR-0001 定的是「内部单一规范形、边界处适配」。
[<RequireQualifiedAccess>]
module PaifuNotation =

    /// 字牌的字母记法 → mjai 记法，按 `1z`-`7z` 的顺序（东 南 西 北 白 發 中）。
    let private jihai =
        [ "E", "1z"; "S", "2z"; "W", "3z"; "N", "4z"; "P", "5z"; "F", "6z"; "C", "7z" ]

    /// 牌谱记法 → mjai 记法。数牌与赤 5（`5mr` / `5pr` / `5sr`）两边本来就一致，原样返回。
    let toMjai (notation: string) : string =
        jihai
        |> List.tryFind (fun (letter, _) -> letter = notation)
        |> Option.map snd
        |> Option.defaultValue notation

/// 牌谱里的 `start_kyoku`：与引擎的 `StartKyoku` 逐字段同形（只差记法）。
type PaifuStartKyoku =
    {
        Bakaze: Kaze
        Kyoku: int
        Honba: int
        Kyotaku: int
        Oya: Seat
        DoraMarker: Tile
        Scores: int list
        Tehais: Tile list list
    }

/// 牌谱里的 `hora`。**上游数据集是瘦身版**：只有这四项，
/// 没有 `pai` / `yakus` / `fu` / `fan` / `hora_points` / `scores`。
/// 役 / 符 / 番 / 点数要另找 oracle（`PaifuOracle`）。
type PaifuHora =
    {
        Actor: Seat
        Target: Seat
        /// 本次授受的点数增减，按座位升序。**含本场与供托**。
        /// 它是天凤自己的钱流，与我们的实现无因果关系——对拍的独立锚点就是它。
        Deltas: int list
        /// 里宝牌指示牌；和了者没立直时是空表。
        UraDoraMarkers: Tile list
    }

/// 牌谱里的一条事件。**只读，且与引擎的 `Event` 分属两个类型**：
/// 牌谱的 `hora` / `ryukyoku` 缺我们的必填字段，把它们塞进 `Event`
/// 会让引擎自己产出的事件也失去「必然完整」这条不变量。
///
/// `[<RequireQualifiedAccess>]`：case 名与 `Event` 的一一撞车（裁决 D-3 的教训），
/// 一律写 `PaifuEvent.Tsumo`。
[<RequireQualifiedAccess>]
type PaifuEvent =
    /// `start_game`。数据集另有 `kyoku_first` / `aka_flag` 两个字段，本类型不带。
    | StartGame of names: string list
    | StartKyoku of startKyoku: PaifuStartKyoku
    | Tsumo of actor: Seat * pai: Tile
    | Dahai of actor: Seat * pai: Tile * tsumogiri: bool
    | Pon of actor: Seat * target: Seat * pai: Tile * consumed: Tile list
    | Chi of actor: Seat * target: Seat * pai: Tile * consumed: Tile list
    /// `ankan`：只有 `actor` 与四张 `consumed`。
    | Ankan of actor: Seat * consumed: Tile list
    /// `kakan`：`actor`、加上去的 `pai` 与原碰的三张 `consumed`，没有 `target`。
    | Kakan of actor: Seat * pai: Tile * consumed: Tile list
    /// wire 上叫 `daiminkan`；标识符按术语表拼作 `Minkan`（裁决 D-1）。
    | Minkan of actor: Seat * target: Seat * pai: Tile * consumed: Tile list
    | Dora of doraMarker: Tile
    /// wire 上叫 `reach`。
    | Riichi of actor: Seat
    /// wire 上叫 `reach_accepted`。数据集里它**不带** deltas / scores。
    | RiichiAccepted of actor: Seat
    | Hora of hora: PaifuHora
    /// `ryukyoku`。**上游数据集是瘦身版**：只有 `deltas`，
    /// 没有 `reason` / `tenpais` / `scores`——形态要另找 oracle。
    | Ryuukyoku of deltas: int list
    | EndKyoku
    | EndGame

/// 一局（kyoku）的牌谱：开局条件加上此后到 `end_kyoku` 为止的全部事件。
type PaifuKyoku =
    {
        /// 这一局在整场里的序号，0 起。与天凤 JSON 的 `log` 下标一一对应。
        Index: int
        Start: PaifuStartKyoku
        /// `start_kyoku` 之后、`end_kyoku` 之前的全部事件，按产出顺序。
        Moves: PaifuEvent list
    }

/// 一整场对局的牌谱。
type PaifuGame =
    {
        /// 天凤牌谱 id，也就是固件的文件名。报差异时定位用。
        Id: string
        Kyokus: PaifuKyoku list
    }

/// 牌谱事件的解码与切局。**宽松**：不认识的字段一律忽略，缺的可选字段给默认值——
/// 它读的是别人的数据，不是我们自己的产出。
[<RequireQualifiedAccess>]
module PaifuEvent =

    // ---- JSON（mjai wire，瘦身版） ----

    /// 诊断文案用英文（ADR-0001）。
    let private tileDecoder: Decoder<Tile> =
        Decode.string
        |> Decode.andThen (fun notation ->
            match notation |> PaifuNotation.toMjai |> Tile.parse with
            | Ok tile -> Decode.succeed tile
            | Error _ -> Decode.fail ("not a paifu tile notation: " + notation))

    let private tilesDecoder: Decoder<Tile list> = Decode.list tileDecoder

    let private kazeDecoder: Decoder<Kaze> =
        Decode.string
        |> Decode.andThen (fun notation ->
            match notation |> PaifuNotation.toMjai |> Kaze.parse with
            | Some kaze -> Decode.succeed kaze
            | None -> Decode.fail ("not a paifu bakaze notation: " + notation))

    let private startKyokuDecoder: Decoder<PaifuEvent> =
        Decode.object (fun get ->
            PaifuEvent.StartKyoku
                {
                    Bakaze = get.Required.Field "bakaze" kazeDecoder
                    Kyoku = get.Required.Field "kyoku" Decode.int
                    Honba = get.Required.Field "honba" Decode.int
                    Kyotaku = get.Required.Field "kyotaku" Decode.int
                    Oya = get.Required.Field "oya" Seat.decoder
                    DoraMarker = get.Required.Field "dora_marker" tileDecoder
                    Scores = get.Required.Field "scores" (Decode.list Decode.int)
                    Tehais = get.Required.Field "tehais" (Decode.list tilesDecoder)
                })

    /// 碰 / 吃 / 大明杠的 wire 形状一模一样，只差一个 `type`。
    let private nakiDecoder (build: Seat -> Seat -> Tile -> Tile list -> PaifuEvent) : Decoder<PaifuEvent> =
        Decode.object (fun get ->
            build
                (get.Required.Field "actor" Seat.decoder)
                (get.Required.Field "target" Seat.decoder)
                (get.Required.Field "pai" tileDecoder)
                (get.Required.Field "consumed" tilesDecoder))

    /// 里宝牌的字段名：mjai 官方规格是 `uradora_markers`，这个数据集写 `ura_markers`。
    /// 两个名字都认，都没有就是空表。
    let private horaDecoder: Decoder<PaifuEvent> =
        Decode.object (fun get ->
            let ura =
                [ "ura_markers"; "uradora_markers" ]
                |> List.tryPick (fun field -> get.Optional.Field field tilesDecoder)

            PaifuEvent.Hora
                {
                    Actor = get.Required.Field "actor" Seat.decoder
                    Target = get.Required.Field "target" Seat.decoder
                    Deltas = get.Required.Field "deltas" (Decode.list Decode.int)
                    UraDoraMarkers = Option.defaultValue [] ura
                })

    /// 解码失败是 Decoder 的错误值，不抛异常。
    let decoder: Decoder<PaifuEvent> =
        Decode.field "type" Decode.string
        |> Decode.andThen (fun eventType ->
            match eventType with
            | "start_game" ->
                Decode.field "names" (Decode.list Decode.string)
                |> Decode.map PaifuEvent.StartGame
            | "start_kyoku" -> startKyokuDecoder
            | "tsumo" ->
                Decode.map2
                    (fun actor pai -> PaifuEvent.Tsumo(actor, pai))
                    (Decode.field "actor" Seat.decoder)
                    (Decode.field "pai" tileDecoder)
            | "dahai" ->
                Decode.map3
                    (fun actor pai tsumogiri -> PaifuEvent.Dahai(actor, pai, tsumogiri))
                    (Decode.field "actor" Seat.decoder)
                    (Decode.field "pai" tileDecoder)
                    (Decode.field "tsumogiri" Decode.bool)
            | "pon" -> nakiDecoder (fun actor target pai consumed -> PaifuEvent.Pon(actor, target, pai, consumed))
            | "chi" -> nakiDecoder (fun actor target pai consumed -> PaifuEvent.Chi(actor, target, pai, consumed))
            | "daiminkan" ->
                nakiDecoder (fun actor target pai consumed -> PaifuEvent.Minkan(actor, target, pai, consumed))
            | "ankan" ->
                Decode.map2
                    (fun actor consumed -> PaifuEvent.Ankan(actor, consumed))
                    (Decode.field "actor" Seat.decoder)
                    (Decode.field "consumed" tilesDecoder)
            | "kakan" ->
                Decode.map3
                    (fun actor pai consumed -> PaifuEvent.Kakan(actor, pai, consumed))
                    (Decode.field "actor" Seat.decoder)
                    (Decode.field "pai" tileDecoder)
                    (Decode.field "consumed" tilesDecoder)
            | "dora" -> Decode.field "dora_marker" tileDecoder |> Decode.map PaifuEvent.Dora
            | "reach" -> Decode.field "actor" Seat.decoder |> Decode.map PaifuEvent.Riichi
            | "reach_accepted" -> Decode.field "actor" Seat.decoder |> Decode.map PaifuEvent.RiichiAccepted
            | "hora" -> horaDecoder
            | "ryukyoku" ->
                Decode.field "deltas" (Decode.list Decode.int)
                |> Decode.map PaifuEvent.Ryuukyoku
            | "end_kyoku" -> Decode.succeed PaifuEvent.EndKyoku
            | "end_game" -> Decode.succeed PaifuEvent.EndGame
            // 三麻的 `nukidora`（拔北）落在这里：本票只跑四麻，认出来就算不支持的变体。
            | other -> Decode.fail ("unsupported paifu event type: " + other))

    // ---- 拆解 ----

    /// 这条事件是谁做的；`dora` 与两条 game 级事件没有主语。
    let actor (event: PaifuEvent) : Seat option =
        match event with
        | PaifuEvent.Tsumo(actor, _)
        | PaifuEvent.Dahai(actor, _, _)
        | PaifuEvent.Pon(actor, _, _, _)
        | PaifuEvent.Chi(actor, _, _, _)
        | PaifuEvent.Ankan(actor, _)
        | PaifuEvent.Kakan(actor, _, _)
        | PaifuEvent.Minkan(actor, _, _, _)
        | PaifuEvent.Riichi actor
        | PaifuEvent.RiichiAccepted actor -> Some actor
        | PaifuEvent.Hora hora -> Some hora.Actor
        | PaifuEvent.StartKyoku start -> Some start.Oya
        | PaifuEvent.StartGame _
        | PaifuEvent.Dora _
        | PaifuEvent.Ryuukyoku _
        | PaifuEvent.EndKyoku
        | PaifuEvent.EndGame -> None

    /// 这条事件翻开的表宝牌指示牌；不是 `dora` 事件则为 None。
    let doraMarker (event: PaifuEvent) : Tile option =
        match event with
        | PaifuEvent.Dora marker -> Some marker
        | PaifuEvent.StartGame _
        | PaifuEvent.StartKyoku _
        | PaifuEvent.Tsumo _
        | PaifuEvent.Dahai _
        | PaifuEvent.Pon _
        | PaifuEvent.Chi _
        | PaifuEvent.Ankan _
        | PaifuEvent.Kakan _
        | PaifuEvent.Minkan _
        | PaifuEvent.Riichi _
        | PaifuEvent.RiichiAccepted _
        | PaifuEvent.Hora _
        | PaifuEvent.Ryuukyoku _
        | PaifuEvent.EndKyoku
        | PaifuEvent.EndGame -> None

    /// 这条事件里的和了；不是 `hora` 事件则为 None。
    let hora (event: PaifuEvent) : PaifuHora option =
        match event with
        | PaifuEvent.Hora hora -> Some hora
        | PaifuEvent.StartGame _
        | PaifuEvent.StartKyoku _
        | PaifuEvent.Tsumo _
        | PaifuEvent.Dahai _
        | PaifuEvent.Pon _
        | PaifuEvent.Chi _
        | PaifuEvent.Ankan _
        | PaifuEvent.Kakan _
        | PaifuEvent.Minkan _
        | PaifuEvent.Dora _
        | PaifuEvent.Riichi _
        | PaifuEvent.RiichiAccepted _
        | PaifuEvent.Ryuukyoku _
        | PaifuEvent.EndKyoku
        | PaifuEvent.EndGame -> None

    /// 这条事件里立直**成立**的座位；不是 `reach_accepted` 事件则为 None。
    /// 里宝牌只在和了者立了直时才翻，判据就是它。
    let riichiAccepted (event: PaifuEvent) : Seat option =
        match event with
        | PaifuEvent.RiichiAccepted actor -> Some actor
        | PaifuEvent.StartGame _
        | PaifuEvent.StartKyoku _
        | PaifuEvent.Tsumo _
        | PaifuEvent.Dahai _
        | PaifuEvent.Pon _
        | PaifuEvent.Chi _
        | PaifuEvent.Ankan _
        | PaifuEvent.Kakan _
        | PaifuEvent.Minkan _
        | PaifuEvent.Dora _
        | PaifuEvent.Riichi _
        | PaifuEvent.Hora _
        | PaifuEvent.Ryuukyoku _
        | PaifuEvent.EndKyoku
        | PaifuEvent.EndGame -> None

    /// 这条事件里的流局授受；不是 `ryukyoku` 事件则为 None。
    let ryuukyokuDeltas (event: PaifuEvent) : int list option =
        match event with
        | PaifuEvent.Ryuukyoku deltas -> Some deltas
        | PaifuEvent.StartGame _
        | PaifuEvent.StartKyoku _
        | PaifuEvent.Tsumo _
        | PaifuEvent.Dahai _
        | PaifuEvent.Pon _
        | PaifuEvent.Chi _
        | PaifuEvent.Ankan _
        | PaifuEvent.Kakan _
        | PaifuEvent.Minkan _
        | PaifuEvent.Dora _
        | PaifuEvent.Riichi _
        | PaifuEvent.RiichiAccepted _
        | PaifuEvent.Hora _
        | PaifuEvent.EndKyoku
        | PaifuEvent.EndGame -> None

    /// 三种杠。杠之后紧跟着的那次自摸是岭上牌，重建牌山要认出它。
    let isKan (event: PaifuEvent) : bool =
        match event with
        | PaifuEvent.Ankan _
        | PaifuEvent.Kakan _
        | PaifuEvent.Minkan _ -> true
        | PaifuEvent.StartGame _
        | PaifuEvent.StartKyoku _
        | PaifuEvent.Tsumo _
        | PaifuEvent.Dahai _
        | PaifuEvent.Pon _
        | PaifuEvent.Chi _
        | PaifuEvent.Dora _
        | PaifuEvent.Riichi _
        | PaifuEvent.RiichiAccepted _
        | PaifuEvent.Hora _
        | PaifuEvent.Ryuukyoku _
        | PaifuEvent.EndKyoku
        | PaifuEvent.EndGame -> false

/// 牌谱文件的载入与切局。
[<RequireQualifiedAccess>]
module PaifuGame =

    /// NDJSON 的每一行是一条事件。空行跳过；任何一行解不出来整局就算不支持的样本
    /// （**显式跳过并计数**，不静默丢弃）。
    let private parseEvents (lines: string seq) : Result<PaifuEvent list, string> =
        let parseLine (index: int, line: string) =
            match Decode.fromString PaifuEvent.decoder line with
            | Ok event -> Ok event
            | Error error -> Error $"第 {index + 1} 行解不开：{error}"

        lines
        |> Seq.indexed
        |> Seq.filter (fun (_, line) -> line.Trim() <> "")
        |> Seq.map parseLine
        |> Seq.fold
            (fun acc parsed ->
                match acc, parsed with
                | Error error, _ -> Error error
                | Ok _, Error error -> Error error
                | Ok events, Ok event -> Ok(event :: events))
            (Ok [])
        |> Result.map List.rev

    /// 把事件流切成一局一局：`start_kyoku` 起、`end_kyoku` 止。
    /// 落在任何一局之外的事件（`start_game` / `end_game`）不属于任何一局。
    let private splitKyokus (events: PaifuEvent list) : PaifuKyoku list =
        let fold (closed: PaifuKyoku list, opening: (PaifuStartKyoku * PaifuEvent list) option) (event: PaifuEvent) =
            match event, opening with
            | PaifuEvent.StartKyoku start, _ -> closed, Some(start, [])
            | PaifuEvent.EndKyoku, Some(start, moves) ->
                closed
                @ [
                    {
                        Index = List.length closed
                        Start = start
                        Moves = List.rev moves
                    }
                ],
                None
            | _, Some(start, moves) -> closed, Some(start, event :: moves)
            | _, None -> closed, None

        events |> List.fold fold ([], None) |> fst

    /// 载入一份 mjai NDJSON 牌谱。`id` 就是天凤牌谱 id（固件的文件名）。
    let parse (id: string) (lines: string seq) : Result<PaifuGame, string> =
        parseEvents lines
        |> Result.map (fun events -> { Id = id; Kyokus = splitKyokus events })
