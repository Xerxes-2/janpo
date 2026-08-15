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
  janpo shanten [--naki N] <记法>...  打印向听数、和了型与有效牌（四麻全 34 牌种）
  janpo shanten --batch               从 stdin 逐行读「<副露数> <记法>...」，每行打印一个向听数
  janpo --help                        打印本帮助

示例:
  janpo tile "1z 5sr 5s 9s 3m"
  janpo tile 1z 5sr 5s 9s 3m
  janpo deal 42
  janpo deal 42 --no-akadora
  janpo shanten "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
  janpo shanten --naki 1 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p"
  echo "0 1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z" | janpo shanten --batch"""

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

/// 四麻：全 34 牌种。三麻的牌种集合是另一张票的事，这里只把接缝留出来。
let private kindSet = TileKindSet.fourPlayer

/// 「<副露数> <记法>...」→ 手牌形态。CLI 与批量模式共用一个解析。
let private parseHand (nakiCount: int) (notations: string) : Result<HandShape, string> =
    match Tile.parseMany notations with
    | Error error -> Error(TileListParseError.toDisplay error)
    | Ok tiles ->
        match HandShape.create nakiCount tiles with
        | Error error -> Error(HandShapeError.toDisplay error)
        | Ok hand -> Ok hand

/// `janpo shanten [--naki N] <记法>...`
let private runShanten (nakiCount: int) (arguments: string list) : int =
    match parseHand nakiCount (String.concat " " arguments) with
    | Error message ->
        eprintfn "%s" message
        1
    | Ok hand ->
        let shanten = Shanten.calculate kindSet hand
        printfn "shanten: %d" (Shanten.value shanten)

        let shapes = AgariShape.classify kindSet hand

        printfn
            "agari: %s"
            (if List.isEmpty shapes then
                 "-"
             else
                 shapes |> List.map AgariShape.toDisplay |> String.concat " ")

        match Ukeire.calculate kindSet [] hand with
        | Error _ -> ()
        | Ok ukeire ->
            printfn "ukeire: %s" (Ukeire.toMjai ukeire)
            printfn "ukeire count: %d" (Ukeire.total ukeire)

        printfn "display: %s" (Shanten.toDisplay shanten)
        0

/// `janpo shanten --batch`：向听 oracle 对拍的入口。每行「<副露数> <记法>...」，
/// 每行打印一个向听数；顺序与输入一一对应。
let private runShantenBatch () : int =
    // 对拍一跑就是十万行，这里自己接管缓冲，不走逐行 flush 的 printfn。
    use input = new System.IO.StreamReader(System.Console.OpenStandardInput())

    use output =
        new System.IO.StreamWriter(System.Console.OpenStandardOutput(), AutoFlush = false)

    let mutable exitCode = 0
    let mutable line = input.ReadLine()

    while exitCode = 0 && not (isNull line) do
        if line.Trim() <> "" then
            match line.Split(' ', 2) with
            | [| naki; notations |] ->
                match System.Int32.TryParse naki with
                | false, _ ->
                    eprintfn "每行应形如「<副露数> <记法>...」，副露数不是整数: %s" line
                    exitCode <- 1
                | true, nakiCount ->
                    match parseHand nakiCount notations with
                    | Error message ->
                        eprintfn "%s（行: %s）" message line
                        exitCode <- 1
                    | Ok hand -> output.WriteLine(Shanten.value (Shanten.calculate kindSet hand))
            | _ ->
                eprintfn "每行应形如「<副露数> <记法>...」: %s" line
                exitCode <- 1

        if exitCode = 0 then
            line <- input.ReadLine()

    output.Flush()
    exitCode

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
    | [ "shanten"; "--batch" ] -> runShantenBatch ()
    | "shanten" :: "--naki" :: naki :: arguments ->
        match System.Int32.TryParse naki with
        | true, nakiCount -> runShanten nakiCount arguments
        | false, _ ->
            eprintfn "--naki 要一个整数，得到: %s" naki
            2
    | "shanten" :: arguments -> runShanten 0 arguments
    | unknown ->
        eprintfn "未知命令: %s" (String.concat " " unknown)
        eprintfn "%s" usage
        2
