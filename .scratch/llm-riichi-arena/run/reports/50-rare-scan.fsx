// 50 号票的频率依据：逐种 `Soak.rare` 形态，两种选手各扫一批种子，数「多少场里出过」。
//
// 走的就是跑批本身（`Soak.run`，逐手验不变量 + 回放确定性），一场一调，因此
// **数出来的频率与 CI 那道闸门量的是同一件事**，不是另一条近似的驱动。
// 用 fsi 直调编译好的真实 API，零移植（AGENTS.md）。
//
// 跑法：dotnet fsi --exec .scratch/llm-riichi-arena/run/reports/50-rare-scan.fsx <选手> <场数> [起始种子]
//   选手 ∈ covering | opinionated | uniform
#load "/home/xerxes2/janpo-ws-d/scripts/fsi/load-engine.fsx"

open System.Diagnostics
open Janpo

let ruleset = Ruleset.yonma

let args = fsi.CommandLineArgs |> Array.toList |> List.tail

let playerName = args |> List.tryItem 0 |> Option.defaultValue "covering"

let games = args |> List.tryItem 1 |> Option.map int |> Option.defaultValue 60

let first = args |> List.tryItem 2 |> Option.map int |> Option.defaultValue 1

let player: Player<Rng> =
    match playerName with
    | "covering" -> RandomPlayer.covering
    | "opinionated" -> OpinionatedPlayer.player
    | "uniform" -> RandomPlayer.uniform
    | other -> failwith $"不认得的选手 {other}"

/// 一批种子跑下来的累计：逐形态的「出过的场数」与「一共出了几次」。
type Tally =
    {
        Kyokus: int
        Turns: int
        Issues: int
        /// 形态 -> (出过它的场数, 一共几次)
        Rare: Map<string, int * int>
    }

let empty =
    {
        Kyokus = 0
        Turns = 0
        Issues = 0
        Rare = Map.empty
    }

let watch = Stopwatch.StartNew()

let final =
    (empty, [ first .. first + games - 1 ])
    ||> List.fold (fun tally seed ->
        let report = Soak.run ruleset player [ seed ]

        let rare =
            (tally.Rare, RyuukyokuReason.all)
            ||> List.fold (fun rare reason ->
                let count = Soak.count (Coverage.Ryuukyoku reason) report
                let name = RyuukyokuReason.toMjai reason
                let games, times = rare |> Map.tryFind name |> Option.defaultValue (0, 0)

                if count = 0 then
                    Map.add name (games, times) rare
                else
                    // 头一次碰到时把种子印出来：复现要它。
                    if games = 0 then
                        printfn $"  头一次 {name}：种子 {seed}（{count} 次）"

                    Map.add name (games + 1, times + count) rare)

        {
            Kyokus = tally.Kyokus + report.Kyokus
            Turns = tally.Turns + report.Turns
            Issues = tally.Issues + List.length report.Issues
            Rare = rare
        })

watch.Stop()

let seconds = watch.Elapsed.TotalSeconds

printfn
    $"== {playerName}（种子 {first}..{first + games - 1}，{games} 场，{final.Kyokus} 局，{final.Turns} 手，{seconds:F1} 秒，{seconds / float games * 1000.0:F1} 毫秒/场）"

printfn $"   问题：{final.Issues}"

for reason in RyuukyokuReason.all do
    let name = RyuukyokuReason.toMjai reason
    let games', times = final.Rare |> Map.tryFind name |> Option.defaultValue (0, 0)
    printfn $"   {name}: 场次 {games'} / {games}　次数 {times}"
