namespace Janpo

open Thoth.Json.Core

/// 牌的花色。对应 mjai 记法的后缀：Manzu=`m`、Pinzu=`p`、Souzu=`s`、Jihai=`z`。
type Suit =
    | Manzu
    | Pinzu
    | Souzu
    | Jihai

/// 单张牌记法解析失败的原因。是值，不是异常（与 IllegalAction 同一方针）。
type TileParseError =
    /// 空串。
    | EmptyNotation
    /// 花色后缀不是 m / p / s / z。
    | UnknownSuit of suit: char
    /// 序数超出该花色的范围（数牌 1-9、字牌 1-7）。
    | NumberOutOfRange of number: int * suit: Suit
    /// 只有 5m / 5p / 5s 有红宝牌。
    | AkadoraNotAllowed of number: int * suit: Suit
    /// 整体不成记法（长度不对、首字符不是数字等）。
    | MalformedNotation of notation: string

/// 记法列（如一副手牌）解析失败：第几个记法、是哪个、错在哪。
type TileListParseError =
    {
        TokenIndex: int
        Token: string
        Reason: TileParseError
    }

/// 牌：34 种正牌与 3 种红宝牌（`5mr` / `5pr` / `5sr`）。
///
/// 唯一的正式记法是 mjai 记法（ADR-0001）：`1m-9m` / `1p-9p` / `1s-9s` / `1z-7z`。
/// 内部表示是私有的——牌只能经 `Tile.parse`、`Tile.tryCreate`、`Tile.ofKindIndex`
/// 构造，因此「8z」「3mr」这类不存在的牌在类型层面不可表示。
/// 人类可读形式（「东」「白」）只在 `Tile.toDisplay` 出现，不进入事件流、牌谱与测试固件。
[<Struct>]
type Tile =
    private
        {
            /// 0-33：1m-9m、1p-9p、1s-9s、1z-7z。红宝牌与对应正牌同 index。
            Kind: int
            Akadora: bool
        }

/// 牌的构造、拆解与 mjai 记法。
[<RequireQualifiedAccess>]
module Tile =

    /// 正牌的种数。
    [<Literal>]
    let KindCount = 34

    let private suitOffset suit =
        match suit with
        | Manzu -> 0
        | Pinzu -> 9
        | Souzu -> 18
        | Jihai -> 27

    let private maxNumber suit =
        match suit with
        | Jihai -> 7
        | Manzu
        | Pinzu
        | Souzu -> 9

    let private suitOfKind kind =
        if kind < 9 then Manzu
        elif kind < 18 then Pinzu
        elif kind < 27 then Souzu
        else Jihai

    // ---- 拆解 ----

    /// 牌的花色。
    let suit (tile: Tile) : Suit = suitOfKind tile.Kind

    /// 牌的序数：数牌 1-9，字牌 1-7（1z 东 … 7z 中）。
    let number (tile: Tile) : int =
        tile.Kind - suitOffset (suitOfKind tile.Kind) + 1

    /// 是否红宝牌。
    let isAkadora (tile: Tile) : bool = tile.Akadora

    /// 去红：`5mr` → `5m`，其余牌不变。手牌形态判定一律先去红。
    let deaka (tile: Tile) : Tile = { tile with Akadora = false }

    /// 牌种索引 0-33（红宝牌与对应正牌相同），用于 34 长度的计数数组。
    let kindIndex (tile: Tile) : int = tile.Kind

    // ---- 构造 ----

    /// 「哪些牌存在」的唯一定义处：数牌 1-9、字牌 1-7、红宝牌只有 5m / 5p / 5s。
    /// `tryCreate` 与 `parse` 都从这里构造，这条规则不存在第二份。
    let private create (suit: Suit) (number: int) (akadora: bool) : Result<Tile, TileParseError> =
        if number < 1 || number > maxNumber suit then
            Error(NumberOutOfRange(number, suit))
        elif akadora && (suit = Jihai || number <> 5) then
            Error(AkadoraNotAllowed(number, suit))
        else
            Ok
                {
                    Kind = suitOffset suit + number - 1
                    Akadora = akadora
                }

    /// 由花色与序数构造正牌或红宝牌；不存在的组合返回 None。
    let tryCreate (suit: Suit) (number: int) (akadora: bool) : Tile option =
        match create suit number akadora with
        | Ok tile -> Some tile
        | Error _ -> None

    /// 由牌种索引 0-33 构造正牌；越界返回 None。
    let ofKindIndex (index: int) : Tile option =
        if index < 0 || index >= KindCount then
            None
        else
            Some { Kind = index; Akadora = false }

    /// 34 种正牌，按 mjai 顺序：1m-9m、1p-9p、1s-9s、1z-7z。
    let kinds: Tile list =
        [ for index in 0 .. KindCount - 1 -> { Kind = index; Akadora = false } ]

    /// 3 种红宝牌：5mr、5pr、5sr。
    let akadoraKinds: Tile list =
        [
            for suit in [ Manzu; Pinzu; Souzu ] ->
                {
                    Kind = suitOffset suit + 4
                    Akadora = true
                }
        ]

    /// 全部 37 个 Tile 值（34 正牌 + 3 红宝牌），按 mjai 顺序升序。
    let all: Tile list = kinds @ akadoraKinds |> List.sort

    // ---- mjai 记法 ----

    let private suitSuffix suit =
        match suit with
        | Manzu -> "m"
        | Pinzu -> "p"
        | Souzu -> "s"
        | Jihai -> "z"

    let private digitOf (character: char) =
        match character with
        | '0' -> Some 0
        | '1' -> Some 1
        | '2' -> Some 2
        | '3' -> Some 3
        | '4' -> Some 4
        | '5' -> Some 5
        | '6' -> Some 6
        | '7' -> Some 7
        | '8' -> Some 8
        | '9' -> Some 9
        | _ -> None

    let private suitOfSuffix (character: char) =
        match character with
        | 'm' -> Ok Manzu
        | 'p' -> Ok Pinzu
        | 's' -> Ok Souzu
        | 'z' -> Ok Jihai
        | other -> Error(UnknownSuit other)

    /// 打印为 mjai 记法。这是牌进入事件流、牌谱与测试固件的唯一形式。
    let toMjai (tile: Tile) : string =
        let body = string (number tile) + suitSuffix (suit tile)
        if tile.Akadora then body + "r" else body

    /// 解析单张牌的 mjai 记法。严格：不接受大写、空白、天凤式的 `0m`。
    let parse (notation: string) : Result<Tile, TileParseError> =
        if System.String.IsNullOrEmpty notation then
            Error EmptyNotation
        else

            let akadora = notation.Length = 3 && notation.[2] = 'r'

            if notation.Length <> 2 && not akadora then
                Error(MalformedNotation notation)
            else
                match digitOf notation.[0] with
                | None -> Error(MalformedNotation notation)
                | Some number ->
                    match suitOfSuffix notation.[1] with
                    | Error error -> Error error
                    | Ok suit -> create suit number akadora

    /// 按 mjai 顺序升序排序；红宝牌紧跟在同种正牌之后（`5m` `5mr` `6m`）。
    let sort (tiles: Tile list) : Tile list = List.sort tiles

    /// 打印一串牌，空格分隔，保持给定顺序。
    let toMjaiMany (tiles: Tile list) : string =
        tiles |> List.map toMjai |> String.concat " "

    let private separators = [| ' '; '\t'; '\n'; '\r'; ',' |]

    /// 解析一串牌的记法，空白或逗号分隔；空串得空列表。
    /// 出错时报告第几个记法出的错。
    let parseMany (notations: string) : Result<Tile list, TileListParseError> =
        let tokens =
            if System.String.IsNullOrEmpty notations then
                []
            else
                notations.Split(separators)
                |> Array.filter (fun token -> token <> "")
                |> Array.toList

        (Ok [], List.indexed tokens)
        ||> List.fold (fun state (index, token) ->
            match state with
            | Error _ -> state
            | Ok tiles ->
                match parse token with
                | Ok tile -> Ok(tile :: tiles)
                | Error reason ->
                    Error
                        {
                            TokenIndex = index
                            Token = token
                            Reason = reason
                        })
        |> Result.map List.rev

    /// 记法列的规范形：mjai 记法、升序、单空格分隔。解析 + 规范化的唯一入口。
    let canonicalize (notations: string) : Result<string, TileListParseError> =
        parseMany notations |> Result.map (sort >> toMjaiMany)

    // ---- JSON（mjai wire） ----

    /// mjai wire 上牌就是一个 JSON 字符串。
    let encoder: Encoder<Tile> = fun tile -> Encode.string (toMjai tile)

    /// 解码失败是 Decoder 的错误值，不抛异常。
    /// 诊断文案用英文——中文只属于渲染层（ADR-0001）。
    let decoder: Decoder<Tile> =
        Decode.string
        |> Decode.andThen (fun notation ->
            match parse notation with
            | Ok tile -> Decode.succeed tile
            | Error _ -> Decode.fail ("not a valid mjai tile notation: " + notation))

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    /// 引擎判定、事件流、牌谱与测试固件都不得消费它的输出。
    let toDisplay (tile: Tile) : string =
        let body =
            match suit tile with
            | Manzu -> string (number tile) + "万"
            | Pinzu -> string (number tile) + "筒"
            | Souzu -> string (number tile) + "索"
            | Jihai -> [ "东"; "南"; "西"; "北"; "白"; "发"; "中" ] |> List.item (number tile - 1)

        if tile.Akadora then "赤" + body else body

/// 解析错误的渲染。
[<RequireQualifiedAccess>]
module TileParseError =

    let private suitDisplay suit =
        match suit with
        | Manzu -> "万子"
        | Pinzu -> "筒子"
        | Souzu -> "索子"
        | Jihai -> "字牌"

    /// **渲染层的单向出口**：错误原因的中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: TileParseError) : string =
        match error with
        | EmptyNotation -> "记法为空"
        | UnknownSuit suit -> $"未知的花色后缀「{suit}」，只有 m / p / s / z"
        | NumberOutOfRange(number, Jihai) -> $"字牌只有 1z-7z，没有 {number}z"
        | NumberOutOfRange(number, suit) -> $"{suitDisplay suit}只有 1-9，没有 {number}"
        | AkadoraNotAllowed(_, Jihai) -> "字牌没有红宝牌，红宝牌只有 5mr / 5pr / 5sr"
        | AkadoraNotAllowed(number, suit) -> $"{suitDisplay suit}的 {number} 不是红宝牌，红宝牌只有 5mr / 5pr / 5sr"
        | MalformedNotation notation -> $"「{notation}」不成记法，应形如 1m / 9p / 7z / 5mr"

/// 记法列解析错误的渲染。
[<RequireQualifiedAccess>]
module TileListParseError =

    /// **渲染层的单向出口**：中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: TileListParseError) : string =
        $"第 {error.TokenIndex + 1} 个记法「{error.Token}」无效：{TileParseError.toDisplay error.Reason}"
