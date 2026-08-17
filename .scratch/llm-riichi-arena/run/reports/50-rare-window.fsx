// 50 号票的窗口证据：稀有流局形态在**每一段连续种子**里出没出现过。
//
// 走 `Game.run`（引擎自己的驱动，不验不变量），因此比 `Soak.run` 快一个数量级，
// 能扫到上万场；两者数出来的覆盖率逐字相同（`SoakTests` 有一条钉着）。
// 用 fsi 直调编译好的真实 API，零移植（AGENTS.md）。
//
// 跑法：dotnet fsi --exec .scratch/llm-riichi-arena/run/reports/50-rare-window.fsx <选手> <场数> <窗口>
#load "/home/xerxes2/janpo-ws-d/scripts/fsi/load-engine.fsx"

open System.Diagnostics
open Janpo

let ruleset = Ruleset.yonma

let args = fsi.CommandLineArgs |> Array.toList |> List.tail

let playerName = args |> List.tryItem 0 |> Option.defaultValue "opinionated"

let games = args |> List.tryItem 1 |> Option.map int |> Option.defaultValue 2000

let window = args |> List.tryItem 2 |> Option.map int |> Option.defaultValue 500

let player: Player<Rng> =
    match playerName with
    | "covering" -> RandomPlayer.covering
    | "opinionated" -> OpinionatedPlayer.player
    | "uniform" -> RandomPlayer.uniform
    | other -> failwith $"不认得的选手 {other}"

/// 一场的事件流：与跑批同一对发生器（牌山与选手各一条流）。
let eventsOf (seed: int) : Event list =
    match Game.run player (Soak.playerRng seed) (Rng.ofSeed seed) (Game.start ruleset) with
    | Ok(game, _, _) -> Game.events game
    | Error error -> failwith $"种子 {seed} 应当跑得完，却得到「{KyokuError.toDisplay error}」"

let watch = Stopwatch.StartNew()

/// 逐场：这一场里打了几局、每种流局形态出了几次。
let perGame =
    [ 1..games ]
    |> List.map (fun seed ->
        let events = eventsOf seed

        let counts =
            events
            |> List.choose (fun event ->
                match event with
                | Event.Ryuukyoku result -> Some(RyuukyokuReason.toMjai result.Reason)
                | _ -> None)
            |> List.countBy id
            |> Map.ofList

        let kyokus =
            events
            |> List.sumBy (fun event ->
                match event with
                | Event.EndKyoku -> 1
                | _ -> 0)

        seed, kyokus, counts)

watch.Stop()

let seconds = watch.Elapsed.TotalSeconds

let kyokus = perGame |> List.sumBy (fun (_, kyokus, _) -> kyokus)

printfn
    $"== {playerName}（种子 1..{games}，{kyokus} 局，{seconds:F1} 秒，{seconds / float games * 1000.0:F2} 毫秒/场）窗口 {window} 场"

for reason in RyuukyokuReason.all do
    let name = RyuukyokuReason.toMjai reason

    let hits =
        perGame
        |> List.filter (fun (_, _, counts) -> Map.containsKey name counts)
        |> List.map (fun (seed, _, _) -> seed)

    let times =
        perGame
        |> List.sumBy (fun (_, _, counts) -> counts |> Map.tryFind name |> Option.defaultValue 0)

    let windows =
        [ 0 .. (games / window) - 1 ]
        |> List.map (fun index ->
            let lo = index * window + 1
            let hi = lo + window - 1
            hits |> List.filter (fun seed -> seed >= lo && seed <= hi) |> List.length)

    let empties = windows |> List.filter ((=) 0) |> List.length

    printfn $"   {name}: 场次 {List.length hits} / {games}　次数 {times}"

    let joined (values: int list) : string =
        values |> List.map string |> String.concat " "

    if not (List.isEmpty hits) && List.length hits <= 60 then
        printfn $"      种子：{joined hits}"

    if not (List.isEmpty hits) then
        printfn $"      每 {window} 场一窗（共 {List.length windows} 窗）：{joined windows}　空窗 {empties}"
