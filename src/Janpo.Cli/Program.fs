module Janpo.Cli.Program

open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Golden

/// 无头驱动入口。规则与算法都在 Janpo.Engine 里，这里只做参数解析与打印；
/// 后续票新增子命令时，在 `main` 的 match 上加一支，逻辑仍写进引擎库。
let private usage =
    """janpo —— 无头驱动入口

用法:
  janpo tile <记法>...           读入一串 mjai 牌记法，打印规范形、牌数与人类可读形式
  janpo deal <种子> [--no-akadora]
                                 用给定种子开一局，每行打印一个 mjai JSON 事件
  janpo kyoku <种子> [--no-akadora]
                                 用给定种子让四个随机选手打完一局，打印完整事件流、
                                 结算后点数与按该点数排出的顺位
  janpo game <种子> [--no-akadora] [--hanchan] [--uniform] [--covering] [--opinionated]
                                 用给定种子让四个随机选手打完一整场（默认东风战），
                                 打印完整事件流、终局点数与顺位。
                                 三个选手开关任选其一，跑出来的就是 `janpo soak` 带同一开关
                                 在这个种子上看到的那一场（选手与牌山分两条随机流）：
                                 --uniform 均匀随机、--covering 跑批用的覆盖型偏好、
                                 --opinionated 有主见的那个（能和就和、听牌就立直、有役才鸣）。
                                 一个开关也不写时牌山与选手共用同一条随机流（黄金用例认的那一种）
  janpo decide <种子> [--seat N] [--steps N] [--no-akadora] [--god] [--sequence]
                                 用给定种子开一局、让随机选手先走 --steps 手（默认 0），
                                 再把那一手的**决策包 JSON** 打出来：某座位的合法观测
                                 加带 id 与中文 label 的动作列表。不给 --seat 就取正在被问的那家；
                                 --god 改打上帝视角（全部暗牌与里宝牌，围观与复盘用）。
                                 走的选手与 `janpo kyoku <种子>` 同一个，因此第 N 手就是那一局的第 N 手。
                                 --sequence 改打**一个 JSON 数组**：那一局里 --seat 每一次被问的决策包，
                                 按手序（--steps 这时是「最多收几份」，0 表示收满一局）。
                                 prompt 的前缀稳定性（票 29b）要的正是连续的几手
  janpo golden check <文件>          跑一份黄金用例文件，逐条逐字段逐行对照期望。
                                 对不上就退出码 1，每条报错指得出是哪条用例的哪个字段的第几行。
                                 **同一份文件浏览器里也要跑**（web/scripts/verify-golden.mjs）
  janpo golden write <文件>          把文件里每条用例按**当前引擎**重跑一遍，用跑出来的值换掉期望。
                                 加一条用例：先写 id / note / ruleset / run，再跑它，
                                 然后**逐行看 diff**——这份文件的对与错靠人看 diff 把关
  janpo soak <种子起> [<种子止>] [--no-akadora] [--hanchan] [--uniform] [--opinionated]
                                 用给定种子区间连续跑 N 场，**逐手验不变量、逐场数覆盖率**：
                                 牌数守恒、点数与供托之和恒定、合法动作集非空或局已终、
                                 回放确定性。打印覆盖率、未覆盖的形态与问题清单；
                                 有问题时退出码 1，每条都带能复现的种子。
                                 选手默认是覆盖型偏好，--uniform / --opinionated 换成另两个
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
  janpo decide 42
  janpo decide 42 --steps 8 --seat 1
  janpo decide 42 --steps 8 --god
  janpo decide 2088 --seat 1 --sequence
  janpo game 42
  janpo game 42 --hanchan
  janpo game 42 --covering
  janpo game 42 --opinionated
  janpo golden check tests/fixtures/golden/dual-target.json
  janpo golden write tests/fixtures/golden/dual-target.json
  janpo soak 1 60
  janpo soak 1 200 --uniform
  janpo soak 1 200 --opinionated
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
    StartGame(Seat.all ruleset |> List.map (fun seat -> "p" + string (Seat.index seat)))

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
/// 输出是每行一个 mjai JSON 事件，随后是结算后的点数与顺位。同一种子必然跑出同一局。
///
/// `juni` 那一行是**按这一局终了时的点数**排的名次，不是终局精算：一局打完时场上可能
/// 还剩立直棒，而它要到终局才归属，所以借 `Game.settle` 排名次时供托传 0（点数不动）。
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
            let scores = GameState.scores state
            let juni = (Game.settle ruleset 0 scores).Juni
            printEvents (startGame ruleset :: GameState.events state)
            printfn "scores: %s" (scores |> List.map string |> String.concat " ")
            printfn "juni: %s" (juni |> List.map string |> String.concat " ")
            0

/// CLI 选得了的自带选手。**`janpo game` 与 `janpo soak` 共用这一份**：
/// 两条命令的开关名一样，跑批报出问题时那条复现命令才对得上。
///
/// 牌桌上只有前两种（`Janpo.Web` 的 `Bot`）：`Covering` 是跑批的仪器
/// （见和就和、九种九牌权重 500），不是给人看的对手。
[<RequireQualifiedAccess>]
type private BotChoice =
    /// 均匀随机（`Kyoku.randomPlayer`）：黄金用例与双目标对拍的基准。
    | Uniform
    /// 跑批用的覆盖型偏好（票 14）。
    | Covering
    /// 有主见的随机选手（票 42）：能和就和、听牌就立直、有役才鸣。
    | Opinionated

[<RequireQualifiedAccess>]
module private BotChoice =

    let player (choice: BotChoice) : Player<Rng> =
        match choice with
        | BotChoice.Uniform -> RandomPlayer.uniform
        | BotChoice.Covering -> RandomPlayer.covering
        | BotChoice.Opinionated -> OpinionatedPlayer.player

    /// 复现这一场要带的开关（`janpo game <种子> <这个>`）。**均匀那一档也要写出来**：
    /// 跑批给选手的发生器与牌山分两条流（`Soak.playerRng`），不写开关的 `janpo game`
    /// 走的却是两者共用一条流的那种——同一种子跑出来不是同一场。
    let flag (choice: BotChoice) : string =
        match choice with
        | BotChoice.Uniform -> " --uniform"
        | BotChoice.Covering -> " --covering"
        | BotChoice.Opinionated -> " --opinionated"

/// `<种子> [--no-akadora] [--hanchan] [--uniform] [--covering] [--opinionated]`：game 的参数形态，
/// 比 deal / kyoku 多一个对局长度与一个选手开关。**不给选手开关是另一回事**（`None`）：
/// 那是牌山与选手共用一条随机流的 `Game.runRandom`，黄金用例与曳光弹对拍认的就是它。
let private parseGameArguments
    (arguments: string list)
    : Result<int option * bool * GameLength * BotChoice option, string> =
    let rec parse (seed: int option) (akadora: bool) (length: GameLength) (bot: BotChoice option) (rest: string list) =
        match rest with
        | [] -> Ok(seed, akadora, length, bot)
        | "--no-akadora" :: tail -> parse seed false length bot tail
        | "--hanchan" :: tail -> parse seed akadora Hanchan bot tail
        | "--tonpuusen" :: tail -> parse seed akadora Tonpuusen bot tail
        | "--uniform" :: tail -> parse seed akadora length (Some BotChoice.Uniform) tail
        | "--covering" :: tail -> parse seed akadora length (Some BotChoice.Covering) tail
        | "--opinionated" :: tail -> parse seed akadora length (Some BotChoice.Opinionated) tail
        | token :: tail ->
            match System.Int32.TryParse token, seed with
            | (true, value), None -> parse (Some value) akadora length bot tail
            | _ -> Error token

    parse None true Ruleset.yonma.Length None arguments

/// `janpo game <种子> [--no-akadora] [--hanchan] [--uniform] [--covering] [--opinionated]`：
/// 四个选手把一整场对局打到终局精算。
/// 输出是每行一个 mjai JSON 事件（每局之间有 `end_kyoku`，最后是 `end_game`），
/// 随后是终局点数与顺位。同一种子必然跑出同一场对局——**14 票的 soak 从这里进**。
let private runGame (arguments: string list) : int =
    match parseGameArguments arguments with
    | Error token ->
        eprintfn
            "game 只认一个整数种子与可选的 --no-akadora / --hanchan / --tonpuusen / --uniform / --covering / --opinionated，不认「%s」"
            token

        2
    | Ok(None, _, _, _) ->
        eprintfn "game 需要一个整数种子，例如: janpo game 42"
        2
    | Ok(Some seed, akadora, length, bot) ->
        let ruleset = rulesetOf akadora |> Ruleset.withLength length

        // 写了选手开关跑的就是 **`janpo soak` 在这个种子上看到的那一场**：同一个选手、
        // 同一对发生器（选手与牌山分两条流）。跑批报出问题时拿它看完整事件流。
        let played =
            match bot with
            | None -> Game.runRandom ruleset (Rng.ofSeed seed) |> Result.map fst
            | Some bot ->
                Game.run (BotChoice.player bot) (Soak.playerRng seed) (Rng.ofSeed seed) (Game.start ruleset)
                |> Result.map (fun (game, _, _) -> game)

        match played with
        | Error error ->
            eprintfn "%s" (KyokuError.toDisplay error)
            1
        | Ok game ->
            printEvents (startGame ruleset :: Game.events game)
            printfn "kyokus: %d" (Game.played game |> List.length)
            printfn "scores: %s" (Game.scores game |> List.map string |> String.concat " ")

            match Game.result game with
            | Some result ->
                printfn "juni: %s" (result.Juni |> List.map string |> String.concat " ")
                printfn "display: %s（%s）" (GameResult.toDisplay result) (GameLength.toDisplay length)
            | None -> ()

            0

/// `<种子起> [<种子止>] [--no-akadora] [--hanchan] [--uniform] [--opinionated]`：soak 的参数形态，
/// 比 game 多一个种子。两个整数按出现顺序当区间的两端。
type private SoakArguments =
    {
        Seeds: int list
        Akadora: bool
        Length: GameLength
        /// 哪一个选手。默认是覆盖型偏好（跑批要的是走遍全部动作类型）；
        /// `--uniform` 拿来对照「不加偏好就跑不到哪几类动作」，
        /// `--opinionated` 是票 42 那组数字的量具。
        Bot: BotChoice
    }

let private parseSoakArguments (arguments: string list) : Result<SoakArguments, string> =
    let rec parse (seeds: int list) (parsed: SoakArguments) (rest: string list) =
        match rest with
        | [] -> Ok { parsed with Seeds = List.rev seeds }
        | "--no-akadora" :: tail -> parse seeds { parsed with Akadora = false } tail
        | "--hanchan" :: tail -> parse seeds { parsed with Length = Hanchan } tail
        | "--tonpuusen" :: tail -> parse seeds { parsed with Length = Tonpuusen } tail
        | "--uniform" :: tail -> parse seeds { parsed with Bot = BotChoice.Uniform } tail
        | "--opinionated" :: tail ->
            parse
                seeds
                { parsed with
                    Bot = BotChoice.Opinionated
                }
                tail
        | token :: tail ->
            match System.Int32.TryParse token, seeds with
            | (true, value), [] -> parse [ value ] parsed tail
            | (true, value), [ first ] -> parse [ value; first ] parsed tail
            | _ -> Error token

    parse
        []
        {
            Seeds = []
            Akadora = true
            Length = Ruleset.yonma.Length
            Bot = BotChoice.Covering
        }
        arguments

/// `janpo soak <种子起> [<种子止>] ...`：一批种子连着跑，逐手验不变量、逐场数覆盖率。
/// **退出码只看问题清单**：覆盖率只打印不卡（跑一两场当然覆盖不全）。
/// 把覆盖率当闸门的是 CI 里的 soak 用例，它跑的是已验证过的默认规模。
let private runSoak (arguments: string list) : int =
    match parseSoakArguments arguments with
    | Error token ->
        eprintfn "soak 只认一到两个整数种子与可选的 --no-akadora / --hanchan / --tonpuusen / --uniform / --opinionated，不认「%s」" token

        2
    | Ok { Seeds = [] } ->
        eprintfn "soak 需要种子区间，例如: janpo soak 1 60"
        2
    | Ok parsed ->
        let ruleset = rulesetOf parsed.Akadora |> Ruleset.withLength parsed.Length
        let player = BotChoice.player parsed.Bot

        let seeds =
            match parsed.Seeds with
            | [ first ] -> [ first ]
            | first :: last :: _ -> [ min first last .. max first last ]
            | [] -> []

        let report = Soak.run ruleset player seeds

        printfn "%s" (SoakReport.toDisplay report)

        match report.Issues with
        | [] -> 0
        | issues ->
            // 种子本身就是事件流的指针：同一种子必然跑出同一场对局，
            // `janpo game <种子> --covering` 把它整条事件流打出来（同一选手、同一对发生器）。
            eprintfn "复现："

            for seed in issues |> List.map (fun issue -> issue.Seed) |> List.distinct do
                eprintfn "  janpo soak %d %d%s          # 重跑这一场并重验不变量" seed seed (BotChoice.flag parsed.Bot)
                eprintfn "  janpo game %d%s  # 打印同一场的完整事件流" seed (BotChoice.flag parsed.Bot)

            1

/// 黄金用例文件的读写。**用例本身是数据**，不在任一侧的代码里：
/// dotnet 侧由这里与测试工程读它，JS 侧由 `web/scripts/verify-golden.mjs` 读同一份。
///
/// JSON 的具体后端由宿主带（这里是 Newtonsoft，浏览器那侧是 Thoth.Json.JavaScript）：
/// `Janpo.Golden` 只依赖 `Thoth.Json.Core` 这层抽象，因此它能被 Fable 编。
let private goldenText: IEncodable -> string = Encode.toString 0

let private readSuite (path: string) : Result<GoldenSuite, string> =
    if System.IO.File.Exists path then
        System.IO.File.ReadAllText path |> Decode.fromString GoldenSuite.decoder
    else
        Error $"用例文件不存在：{path}"

/// `janpo golden check <文件>`：逐条跑、逐字段逐行对照。对不上退出码 1。
let private runGoldenCheck (path: string) : int =
    match readSuite path with
    | Error message ->
        eprintfn "%s" message
        1
    | Ok suite ->
        let report = GoldenCheck.suite goldenText suite
        printfn "%s" (GoldenReport.toDisplay report)
        if GoldenReport.isClean report then 0 else 1

/// `janpo golden write <文件>`：把期望换成当前引擎跑出来的值。
///
/// 它不会碰 `run`（用例的**输入**只能人写），只重写 `expect`，并把省略掉的
/// 规则集开关补齐写回去——ADR-0004 要求每条用例都注明所依据的规则集。
let private runGoldenWrite (path: string) : int =
    match readSuite path with
    | Error message ->
        eprintfn "%s" message
        1
    | Ok suite ->
        let refreshed = GoldenCheck.refresh goldenText suite
        let text = refreshed |> GoldenSuite.encoder |> Encode.toString 2
        System.IO.File.WriteAllText(path, text + "\n")
        printfn "%s" (GoldenReport.toDisplay (GoldenCheck.suite goldenText refreshed))
        printfn "已写回 %s——**逐行看 diff**再提交" path
        0

let private runGolden (arguments: string list) : int =
    match arguments with
    | [ "check"; path ] -> runGoldenCheck path
    | [ "write"; path ] -> runGoldenWrite path
    | _ ->
        eprintfn "golden 只认 `check <文件>` 与 `write <文件>`"
        2

/// `<种子> [--seat N] [--steps N] [--no-akadora] [--god]`：decide 的参数形态。
type private DecideArguments =
    {
        Seed: int option
        Akadora: bool
        /// 要看哪家的决策包；不给就取正在被问的那家。
        Seat: int option
        /// 先让随机选手走几手（中局的决策包才有意思）。
        Steps: int
        /// 改打上帝视角。
        God: bool
        /// 收**同一座位在这一局里连续的每一份**决策包（票 29b）。
        Sequence: bool
    }

let private parseDecideArguments (arguments: string list) : Result<DecideArguments, string> =
    let rec parse (parsed: DecideArguments) (rest: string list) =
        match rest with
        | [] -> Ok parsed
        | "--no-akadora" :: tail -> parse { parsed with Akadora = false } tail
        | "--god" :: tail -> parse { parsed with God = true } tail
        | "--sequence" :: tail -> parse { parsed with Sequence = true } tail
        | "--seat" :: value :: tail ->
            match System.Int32.TryParse value with
            | true, index -> parse { parsed with Seat = Some index } tail
            | false, _ -> Error value
        | "--steps" :: value :: tail ->
            match System.Int32.TryParse value with
            | true, steps -> parse { parsed with Steps = steps } tail
            | false, _ -> Error value
        | token :: tail ->
            match System.Int32.TryParse token, parsed.Seed with
            | (true, value), None -> parse { parsed with Seed = Some value } tail
            | _ -> Error token

    parse
        {
            Seed = None
            Akadora = true
            Seat = None
            Steps = 0
            God = false
            Sequence = false
        }
        arguments

/// `--sequence`：把这一局从头打到底（四家随机选手），把**点名那一座位每一次被问**的
/// 决策包按手序收成一串。`limit` 为正时收满就停。
///
/// **前缀可缓存的 prompt（票 29b）只有连续的几手才验得了**：一手一份包看不出前缀在不在长。
/// 每一份包与 `janpo decide <种子> --steps N` 打出来的那一份是同一个 encoder 的产物。
let private decideSequence
    (seat: Seat)
    (limit: int)
    (rng: Rng)
    (state: GameState)
    : Result<DecisionPackage list, string> =
    let rec loop (collected: DecisionPackage list) (rng: Rng) (state: GameState) =
        if limit > 0 && List.length collected >= limit then
            Ok(List.rev collected)
        else
            match GameState.legalActions state with
            | [] -> Ok(List.rev collected)
            | choice :: _ ->
                // **只收「正在被问的那一手」**：牌桌问的也是合法动作集的头一家
                // （`Table.pending`），因此这一串与真跑起来那一串逐份相同。
                let asked =
                    if choice.Seat = seat then
                        DecisionPackage.forSeat seat state |> Option.toList
                    else
                        []

                let action, advanced = Kyoku.randomPlayer rng state choice

                match GameState.step state action with
                | Error illegal -> Error(IllegalAction.toDisplay illegal)
                | Ok(next, _) -> loop (asked @ collected) advanced next

    loop [] rng state

/// `janpo decide <种子> [--seat N] [--steps N] [--no-akadora] [--god] [--sequence]`：
/// 把一局推到第 N 手，打出那一手的决策包 JSON（或上帝视角，或连续几手的那一串）。
/// **这就是跨 F#/TS 边界的那个包**：肉眼查它有没有多给一张牌。
let private runDecide (arguments: string list) : int =
    match parseDecideArguments arguments with
    | Error token ->
        eprintfn "decide 只认一个整数种子与可选的 --seat / --steps / --no-akadora / --god，不认「%s」" token
        2
    | Ok { Seed = None } ->
        eprintfn "decide 需要一个整数种子，例如: janpo decide 42"
        2
    | Ok parsed ->
        let ruleset = rulesetOf parsed.Akadora
        let context = KyokuContext.initial ruleset
        let seed = Option.defaultValue 0 parsed.Seed

        let opened =
            GameState.start ruleset context (Rng.ofSeed seed)
            |> Result.mapError KyokuStartError.toDisplay

        // 走的选手与 `janpo kyoku` 同一个，因此同一种子的第 N 手两边对得上。
        let advanced =
            opened
            |> Result.bind (fun (state, rng) ->
                Kyoku.runSteps parsed.Steps Kyoku.randomPlayer rng state
                |> Result.map fst
                |> Result.mapError IllegalAction.toDisplay)

        // `--sequence` 是另一条出口：它要的是**这一局从头到底**，不是某一手的局面。
        if parsed.Sequence then
            match parsed.Seat with
            | None ->
                eprintfn "--sequence 要点名一个座位：连续的几手是某一家的几手，例如 janpo decide 2088 --seat 1 --sequence"
                2
            | Some index ->
                let collected =
                    opened
                    |> Result.bind (fun (state, rng) ->
                        match Seat.ofIndex index with
                        | None -> Error $"这个规则集里没有座位 {index}"
                        | Some seat -> decideSequence seat parsed.Steps rng state)

                match collected with
                | Error message ->
                    eprintfn "%s" message
                    1
                | Ok packages ->
                    printfn "%s" (Encode.toString 2 (packages |> List.map DecisionPackage.encoder |> Encode.list))
                    0
        else

            match advanced with
            | Error message ->
                eprintfn "%s" message
                1
            | Ok state when parsed.God ->
                printfn "%s" (Encode.toString 2 (GodView.encoder (GodView.ofState state)))
                0
            | Ok state ->
                let asked = GameState.legalActions state |> List.map (fun choice -> choice.Seat)

                let wanted =
                    match parsed.Seat with
                    | Some index -> Seat.ofIndex index
                    | None -> List.tryHead asked

                match wanted |> Option.bind (fun seat -> DecisionPackage.forSeat seat state) with
                | Some package ->
                    printfn "%s" (Encode.toString 2 (DecisionPackage.encoder package))
                    0
                | None ->
                    let waiting = asked |> List.map (Seat.index >> string) |> String.concat " "

                    if List.isEmpty asked then
                        eprintfn "这一局已终（走了 %d 手），没有人在被问；--god 仍然看得了" parsed.Steps
                    else
                        eprintfn "这一手等的是座位 %s，不是你要的那家" waiting

                    1

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

/// CLI 的示例座位：`janpo naki` / `janpo score` 手里没有真实牌局，座位只是占位。
/// 约定宣言者坐起家，被鸣 / 放铳的是它的下家，吃只吃上家打的（`Naki.chi` 的不变量）。
let private declarer = Seat.first
let private declarerShimocha = Seat.shimocha Ruleset.yonma Seat.first
let private declarerKamicha = Seat.kamicha Ruleset.yonma Seat.first

/// 副露的记法：`<种类> <牌>...`，第一张是鸣来的那张（加杠是加上去的那张）。
/// 座位只影响 mjai 事件与责任支付，判役用不上，这里填示例座位。
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
            | "pon", taken :: consumed -> described (Naki.pon declarerShimocha taken consumed)
            | "chi", taken :: consumed -> described (Naki.chi declarerKamicha taken consumed)
            | "ankan", _ -> described (Naki.ankan tiles)
            | "minkan", taken :: consumed -> described (Naki.minkan declarerShimocha taken consumed)
            | "kakan", added :: taken :: consumed ->
                described (Naki.pon declarerShimocha taken consumed |> Result.bind (Naki.kakan added))
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
                            Actor = declarer
                            Target = (if parsed.Tsumo then declarer else declarerShimocha)
                            Oya =
                                (if parsed.Context.Jikaze = Ton then
                                     declarer
                                 else
                                     declarerShimocha)
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
    | "decide" :: arguments -> runDecide arguments
    | "soak" :: arguments -> runSoak arguments
    | "golden" :: arguments -> runGolden arguments
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
