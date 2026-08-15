module Janpo.Cli.Program

open Thoth.Json.Newtonsoft
open Janpo

/// 无头驱动入口。规则与算法都在 Janpo.Engine 里，这里只做参数解析与打印；
/// 后续票新增子命令时，在 `main` 的 match 上加一支，逻辑仍写进引擎库。
let private usage =
    """janpo —— 无头驱动入口

用法:
  janpo tile <记法>...           读入一串 mjai 牌记法，打印规范形、牌数与人类可读形式
  janpo deal <种子> [--no-akadora]
                                 用给定种子开一局，每行打印一个 mjai JSON 事件
  janpo --help                   打印本帮助

示例:
  janpo tile "1z 5sr 5s 9s 3m"
  janpo tile 1z 5sr 5s 9s 3m
  janpo deal 42
  janpo deal 42 --no-akadora"""

/// `janpo tile <记法>...`：多个参数按空格拼接后当作一串记法。
let private runTile (arguments: string list) : int =
    match Tile.parseMany (String.concat " " arguments) with
    | Error error ->
        eprintfn "%s" (TileListParseError.toDisplay error)
        1
    | Ok tiles ->
        let sorted = Tile.sort tiles
        printfn "%s" (Tile.toMjaiMany sorted)
        printfn "count: %d" (List.length sorted)
        printfn "display: %s" (sorted |> List.map Tile.toDisplay |> String.concat " ")
        0

/// `janpo deal <种子> [--no-akadora]`：同一种子必然开出同一局。
/// 输出是每行一个 mjai JSON 事件：`start_game`、`start_kyoku`、Oya 的 `tsumo`。
let private runDeal (arguments: string list) : int =
    let rec parse (seed: int option) (akadora: bool) (rest: string list) =
        match rest with
        | [] -> Ok(seed, akadora)
        | "--no-akadora" :: tail -> parse seed akadora tail |> Result.map (fun (seed, _) -> seed, false)
        | token :: tail ->
            match System.Int32.TryParse token, seed with
            | (true, value), None -> parse (Some value) akadora tail
            | _ -> Error token

    match parse None true arguments with
    | Error token ->
        eprintfn "deal 只认一个整数种子与可选的 --no-akadora，不认「%s」" token
        2
    | Ok(None, _) ->
        eprintfn "deal 需要一个整数种子，例如: janpo deal 42"
        2
    | Ok(Some seed, akadora) ->
        let ruleset =
            if akadora then
                Ruleset.yonma
            else
                Ruleset.withoutAkadora Ruleset.yonma

        let context = KyokuContext.initial ruleset
        let names = Seat.all ruleset |> List.map (fun seat -> "p" + string seat)

        match KyokuStart.create ruleset context (Rng.ofSeed seed) with
        | Error error ->
            eprintfn "%s" (KyokuStartError.toDisplay error)
            1
        | Ok(start, _) ->
            for event in StartGame names :: start.Events do
                printfn "%s" (Encode.toString 0 (Event.encoder event))

            0

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | []
    | [ "--help" ]
    | [ "-h" ]
    | [ "help" ] ->
        printfn "%s" usage
        0
    | "tile" :: arguments -> runTile arguments
    | "deal" :: arguments -> runDeal arguments
    | unknown ->
        eprintfn "未知命令: %s" (String.concat " " unknown)
        eprintfn "%s" usage
        2
