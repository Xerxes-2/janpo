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
  janpo kyoku <种子> [--no-akadora]
                                 用给定种子让四个随机选手打完一局，打印完整事件流与结算后点数
  janpo game <种子> [--no-akadora] [--hanchan]
                                 用给定种子让四个随机选手打完一整场（默认东风战），
                                 打印完整事件流、终局点数与顺位
  janpo shanten [--naki N] <记法>...  打印向听数、和了型与有效牌（四麻全 34 牌种）
  janpo shanten --batch               从 stdin 逐行读「<副露数> <记法>...」，每行打印一个向听数
  janpo yaku --win <记法> [选项] <暗牌记法>...
                                 判定役种、番数、符与点数授受
  janpo --help                        打印本帮助

yaku 的选项:
  --win <记法>        和了牌（必填，且要包含在暗牌里）
  --tsumo / --ron     自摸或荣和（默认荣和）
  --naki "<种类> <记法>..."
                      副露，可重复：pon / chi / ankan / minkan / kakan，
                      第一张是鸣来的那张（加杠第一张是加上去的那张）
  --bakaze / --jikaze <1z-4z>
                      场风与自风（默认都是 1z）
  --riichi / --double-riichi / --ippatsu
  --rinshan / --haitei / --houtei / --chankan / --tenhou / --chiihou
  --dora <记法> / --ura <记法>
                      宝牌指示牌，可重复
  --no-kuitan         关掉食断
  --honba <N> / --kyotaku <N>
                      本场数与供托（立直棒根数），计入授受
  --kiriage           打开切上满贯（默认关：天凤与雀魂段位战都不采用）

授受的座位约定（deltas 那一行）：和了者固定在座位 0；自风为 `1z` 时它就是亲，
否则亲是座位 1；荣和的放铳者取座位 1。

示例:
  janpo tile "1z 5sr 5s 9s 3m"
  janpo tile 1z 5sr 5s 9s 3m
  janpo deal 42
  janpo deal 42 --no-akadora
  janpo kyoku 42
  janpo game 42
  janpo game 42 --hanchan
  janpo shanten "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
  janpo shanten --naki 1 "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p"
  echo "0 1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z" | janpo shanten --batch
  janpo yaku --win 9s --riichi "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z"
  janpo yaku --win 4m --naki "pon 5z 5z 5z" --dora 1m "2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z"
"""

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

/// `<种子> [--no-akadora]`：deal 与 kyoku 共用的参数形态。
let private parseSeedArguments (arguments: string list) : Result<int option * bool, string> =
    let rec parse (seed: int option) (akadora: bool) (rest: string list) =
        match rest with
        | [] -> Ok(seed, akadora)
        | "--no-akadora" :: tail -> parse seed false tail
        | token :: tail ->
            match System.Int32.TryParse token, seed with
            | (true, value), None -> parse (Some value) akadora tail
            | _ -> Error token

    parse None true arguments

let private rulesetOf (akadora: bool) : Ruleset =
    if akadora then
        Ruleset.yonma
    else
        Ruleset.withoutAkadora Ruleset.yonma

/// 一场对局开始的 `start_game`：`names` 来自配桌，不是引擎算出来的（02 票的决定）。
let private startGame (ruleset: Ruleset) : Event =
    StartGame(Seat.all ruleset |> List.map (fun seat -> "p" + string seat))

let private printEvents (events: Event list) : unit =
    for event in events do
        printfn "%s" (Encode.toString 0 (Event.encoder event))

/// `janpo deal <种子> [--no-akadora]`：同一种子必然开出同一局。
/// 输出是每行一个 mjai JSON 事件：`start_game`、`start_kyoku`、Oya 的 `tsumo`。
let private runDeal (arguments: string list) : int =
    match parseSeedArguments arguments with
    | Error token ->
        eprintfn "deal 只认一个整数种子与可选的 --no-akadora，不认「%s」" token
        2
    | Ok(None, _) ->
        eprintfn "deal 需要一个整数种子，例如: janpo deal 42"
        2
    | Ok(Some seed, akadora) ->
        let ruleset = rulesetOf akadora
        let context = KyokuContext.initial ruleset

        match KyokuStart.create ruleset context (Rng.ofSeed seed) with
        | Error error ->
            eprintfn "%s" (KyokuStartError.toDisplay error)
            1
        | Ok(start, _) ->
            printEvents (startGame ruleset :: start.Events)
            0

/// `janpo kyoku <种子> [--no-akadora]`：四个随机选手把一局打到终
/// （随机选手几乎总是打成荒牌流局，和了也是一种终局形态）。
/// 输出是每行一个 mjai JSON 事件，最后一行是结算后的点数。同一种子必然跑出同一局。
let private runKyoku (arguments: string list) : int =
    match parseSeedArguments arguments with
    | Error token ->
        eprintfn "kyoku 只认一个整数种子与可选的 --no-akadora，不认「%s」" token
        2
    | Ok(None, _) ->
        eprintfn "kyoku 需要一个整数种子，例如: janpo kyoku 42"
        2
    | Ok(Some seed, akadora) ->
        let ruleset = rulesetOf akadora
        let context = KyokuContext.initial ruleset

        match Kyoku.runRandom ruleset context (Rng.ofSeed seed) with
        | Error error ->
            eprintfn "%s" (KyokuError.toDisplay error)
            1
        | Ok(state, _) ->
            printEvents (startGame ruleset :: GameState.events state)
            printfn "scores: %s" (GameState.scores state |> List.map string |> String.concat " ")
            0

/// `<种子> [--no-akadora] [--hanchan]`：game 的参数形态，比 deal / kyoku 多一个对局长度。
let private parseGameArguments (arguments: string list) : Result<int option * bool * GameLength, string> =
    let rec parse (seed: int option) (akadora: bool) (length: GameLength) (rest: string list) =
        match rest with
        | [] -> Ok(seed, akadora, length)
        | "--no-akadora" :: tail -> parse seed false length tail
        | "--hanchan" :: tail -> parse seed akadora Hanchan tail
        | "--tonpuusen" :: tail -> parse seed akadora Tonpuusen tail
        | token :: tail ->
            match System.Int32.TryParse token, seed with
            | (true, value), None -> parse (Some value) akadora length tail
            | _ -> Error token

    parse None true Ruleset.yonma.Length arguments

/// `janpo game <种子> [--no-akadora] [--hanchan]`：四个随机选手把一整场对局打到终局精算。
/// 输出是每行一个 mjai JSON 事件（每局之间有 `end_kyoku`，最后是 `end_game`），
/// 随后是终局点数与顺位。同一种子必然跑出同一场对局——**14 票的 soak 从这里进**。
let private runGame (arguments: string list) : int =
    match parseGameArguments arguments with
    | Error token ->
        eprintfn "game 只认一个整数种子与可选的 --no-akadora / --hanchan / --tonpuusen，不认「%s」" token
        2
    | Ok(None, _, _) ->
        eprintfn "game 需要一个整数种子，例如: janpo game 42"
        2
    | Ok(Some seed, akadora, length) ->
        let ruleset = rulesetOf akadora |> Ruleset.withLength length

        match Game.runRandom ruleset (Rng.ofSeed seed) with
        | Error error ->
            eprintfn "%s" (KyokuError.toDisplay error)
            1
        | Ok(game, _) ->
            printEvents (startGame ruleset :: Game.events game)
            printfn "kyokus: %d" (Game.played game |> List.length)
            printfn "scores: %s" (Game.scores game |> List.map string |> String.concat " ")

            match Game.result game with
            | Some result ->
                printfn "juni: %s" (result.Juni |> List.map string |> String.concat " ")
                printfn "display: %s（%s）" (GameResult.toDisplay result) (GameLength.toDisplay length)
            | None -> ()

            0

/// 四麻：全 34 牌种，**从规则集读**（ADR-0004 决定 4：牌种全集由 `Ruleset` 携带）。
/// 三麻的牌种集合是另一张票的事，这里只把接缝留出来。
let private kindSet = Ruleset.yonma.TileKinds

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

/// `janpo yaku` 的参数：暗牌、副露与上下文标志。
type private YakuArguments =
    {
        Concealed: string list
        Naki: Naki list
        Winning: string option
        Tsumo: bool
        Context: YakuContext
        Kuitan: bool
        Honba: int
        Kyotaku: int
        Kiriage: bool
    }

/// 副露的记法：`<种类> <牌>...`，第一张是鸣来的那张（加杠是加上去的那张）。
/// 座位只影响 mjai 事件与责任支付，判役用不上，这里填一个合法值。
let private parseNakiSpec (text: string) : Result<Naki, string> =
    let tokens =
        text.Split([| ' '; '\t'; ',' |])
        |> Array.filter (fun token -> token <> "")
        |> List.ofArray

    let described (result: Result<Naki, NakiError>) =
        result |> Result.mapError NakiError.toDisplay

    match tokens with
    | [] -> Error "副露不能为空"
    | kind :: rest ->
        match Tile.parseMany (String.concat " " rest) with
        | Error error -> Error(TileListParseError.toDisplay error)
        | Ok tiles ->
            match kind, tiles with
            | "pon", taken :: consumed -> described (Naki.pon 1 taken consumed)
            | "chi", taken :: consumed -> described (Naki.chi 3 taken consumed)
            | "ankan", _ -> described (Naki.ankan tiles)
            | "minkan", taken :: consumed -> described (Naki.minkan 1 taken consumed)
            | "kakan", added :: taken :: consumed ->
                described (Naki.pon 1 taken consumed |> Result.bind (Naki.kakan added))
            | ("pon" | "chi" | "minkan" | "kakan"), _ -> Error $"「{kind}」后面牌太少"
            | _ -> Error $"未知的副露种类「{kind}」，只有 pon / chi / ankan / minkan / kakan"

/// `janpo yaku`：判定役种与番数。符与点数是 08 票的事，这里不算。
let private runYaku (arguments: string list) : int =
    let initial =
        {
            Concealed = []
            Naki = []
            Winning = None
            Tsumo = false
            Context = YakuContext.create Ton Ton
            Kuitan = true
            Honba = 0
            Kyotaku = 0
            Kiriage = false
        }

    let withContext (arguments: YakuArguments) (context: YakuContext) = { arguments with Context = context }

    let rec parse (arguments: YakuArguments) (rest: string list) : Result<YakuArguments, string> =
        let context = arguments.Context

        match rest with
        | [] -> Ok arguments
        | "--win" :: value :: tail -> parse { arguments with Winning = Some value } tail
        | "--tsumo" :: tail -> parse { arguments with Tsumo = true } tail
        | "--ron" :: tail -> parse { arguments with Tsumo = false } tail
        | "--no-kuitan" :: tail -> parse { arguments with Kuitan = false } tail
        | "--kiriage" :: tail -> parse { arguments with Kiriage = true } tail
        | "--honba" :: value :: tail ->
            match System.Int32.TryParse value with
            | true, honba -> parse { arguments with Honba = honba } tail
            | false, _ -> Error $"--honba 要一个整数，得到「{value}」"
        | "--kyotaku" :: value :: tail ->
            match System.Int32.TryParse value with
            | true, kyotaku -> parse { arguments with Kyotaku = kyotaku } tail
            | false, _ -> Error $"--kyotaku 要一个整数，得到「{value}」"
        | "--naki" :: value :: tail ->
            match parseNakiSpec value with
            | Error message -> Error message
            | Ok naki ->
                parse
                    { arguments with
                        Naki = arguments.Naki @ [ naki ]
                    }
                    tail
        | "--bakaze" :: value :: tail ->
            match Kaze.parse value with
            | Some kaze -> parse (withContext arguments { context with Bakaze = kaze }) tail
            | None -> Error $"场风要写成 1z-4z，得到「{value}」"
        | "--jikaze" :: value :: tail ->
            match Kaze.parse value with
            | Some kaze -> parse (withContext arguments { context with Jikaze = kaze }) tail
            | None -> Error $"自风要写成 1z-4z，得到「{value}」"
        | "--dora" :: value :: tail ->
            match Tile.parse value with
            | Error error -> Error(TileParseError.toDisplay error)
            | Ok marker ->
                parse
                    (withContext
                        arguments
                        { context with
                            DoraMarkers = context.DoraMarkers @ [ marker ]
                        })
                    tail
        | "--ura" :: value :: tail ->
            match Tile.parse value with
            | Error error -> Error(TileParseError.toDisplay error)
            | Ok marker ->
                parse
                    (withContext
                        arguments
                        { context with
                            UraDoraMarkers = context.UraDoraMarkers @ [ marker ]
                        })
                    tail
        | "--riichi" :: tail ->
            parse
                (withContext
                    arguments
                    { context with
                        Riichi = RiichiDeclaration.Riichi
                    })
                tail
        | "--double-riichi" :: tail ->
            parse
                (withContext
                    arguments
                    { context with
                        Riichi = RiichiDeclaration.DoubleRiichi
                    })
                tail
        | "--ippatsu" :: tail -> parse (withContext arguments { context with Ippatsu = true }) tail
        | "--rinshan" :: tail -> parse (withContext arguments { context with Rinshan = true }) tail
        | "--haitei" :: tail -> parse (withContext arguments { context with Haitei = true }) tail
        | "--houtei" :: tail -> parse (withContext arguments { context with Houtei = true }) tail
        | "--chankan" :: tail -> parse (withContext arguments { context with Chankan = true }) tail
        | "--tenhou" :: tail -> parse (withContext arguments { context with Tenhou = true }) tail
        | "--chiihou" :: tail -> parse (withContext arguments { context with Chiihou = true }) tail
        | token :: _ when token.StartsWith "--" -> Error $"未知选项「{token}」"
        | token :: tail ->
            parse
                { arguments with
                    Concealed = arguments.Concealed @ [ token ]
                }
                tail

    match parse initial arguments with
    | Error message ->
        eprintfn "%s" message
        2
    | Ok parsed ->
        match parsed.Winning with
        | None ->
            eprintfn "yaku 需要 --win <和了牌记法>"
            2
        | Some winning ->
            let ruleset =
                Ruleset.yonma
                |> (if parsed.Kuitan then id else Ruleset.withoutKuitan)
                |> (if parsed.Kiriage then Ruleset.withKiriageMangan else id)

            let build = if parsed.Tsumo then AgariHand.tsumo else AgariHand.ron

            let hand =
                match Tile.parse winning, Tile.parseMany (String.concat " " parsed.Concealed) with
                | Error error, _ -> Error(TileParseError.toDisplay error)
                | _, Error error -> Error(TileListParseError.toDisplay error)
                | Ok winning, Ok concealed ->
                    build parsed.Naki concealed winning |> Result.mapError AgariHandError.toDisplay

            match hand with
            | Error message ->
                eprintfn "%s" message
                1
            | Ok hand ->
                // 高点法：`Score.best` 把全部读法按**真符**各算一遍，取点数最高的那一种。
                match Score.best ruleset parsed.Context hand with
                | Error error ->
                    eprintfn "%s" (YakuError.toDisplay error)
                    1
                | Ok reading ->
                    let tally = reading.Tally

                    // 座位约定见 usage：和了者固定在座位 0，自风为东时它就是亲。
                    let transfer =
                        {
                            Actor = 0
                            Target = (if parsed.Tsumo then 0 else 1)
                            Oya = (if parsed.Context.Jikaze = Ton then 0 else 1)
                            Honba = parsed.Honba
                            Kyotaku = parsed.Kyotaku
                            // 单手牌算点没有牌局历史，谈不上包（责任支付）。
                            Sekinin = None
                        }

                    let score = Score.hora ruleset transfer reading.Value

                    printfn "shape: %s" (AgariShape.toDisplay tally.Shape)
                    printfn "yaku: %s" (YakuTally.yaku tally |> List.map Yaku.name |> String.concat " ")
                    printfn "han: %d" (YakuTally.han tally)
                    printfn "yakuman: %d" (YakuTally.yakuman tally)
                    printfn "dora: %d ura: %d aka: %d" tally.Dora tally.Uradora tally.Akadora
                    printfn "fu: %d" reading.Value.Fu
                    printfn "points: %d" score.HoraPoints
                    printfn "deltas: %s" (score.Deltas |> List.map string |> String.concat " ")
                    printfn "display: %s" (YakuTally.toDisplay tally)
                    printfn "score: %s" (HoraScore.toDisplay score)
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
    | "kyoku" :: arguments -> runKyoku arguments
    | "game" :: arguments -> runGame arguments
    | [ "shanten"; "--batch" ] -> runShantenBatch ()
    | "shanten" :: "--naki" :: naki :: arguments ->
        match System.Int32.TryParse naki with
        | true, nakiCount -> runShanten nakiCount arguments
        | false, _ ->
            eprintfn "--naki 要一个整数，得到: %s" naki
            2
    | "shanten" :: arguments -> runShanten 0 arguments
    | "yaku" :: arguments -> runYaku arguments
    | unknown ->
        eprintfn "未知命令: %s" (String.concat " " unknown)
        eprintfn "%s" usage
        2
