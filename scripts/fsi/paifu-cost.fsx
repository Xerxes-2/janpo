// 牌谱对拍的**成本剖面**（票 62 第一节：先量再改）。把「一局对拍多少毫秒」拆成六段，
// 每一段都是真跑一遍同一份实现测出来的，不估、不猜。
//
//     dotnet fsi --exec scripts/fsi/paifu-cost.fsx -- <语料目录> [场数] [重复次数]
//
// 六段与它们各自「一趟做几次」（判据 13 的补强：先问次数，再问单价）：
//
//   mjson 解析      每行一次（一局约 90 行）      `PaifuGame.parse`
//   天凤 JSON 解析  每场一次                      `PaifuOracle.parse`
//   牌山重建        每局一次                      `PaifuReplay.wall`
//   引擎重放        每个动作一次                  `PaifuReplay.kyoku` 减掉牌山与事件流对拍
//   比对            每局一次（事件流逐条 + oracle）`PaifuReplay.eventDiffs` + `PaifuDifferential.game` 减重放
//   其余            每场一次                      读文件、载入、对齐、汇总
//
// **不含任何规则逻辑**：只调测试工程里那几个 CI 也在调的 API。

#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Newtonsoft.Json.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Thoth.Json.Core.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Thoth.Json.Newtonsoft.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Janpo.Engine.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Janpo.Engine.Tests.dll"

open System
open System.Diagnostics
open System.IO
open Janpo
open Janpo.Engine.Tests

let args = fsi.CommandLineArgs |> Array.toList |> List.skip 1

let directory =
    args
    |> List.tryItem 0
    |> Option.defaultValue "tests/Janpo.Engine.Tests/bin/Release/net10.0/fixtures/paifu"

let limit = args |> List.tryItem 1 |> Option.map int |> Option.defaultValue 87
let repeats = args |> List.tryItem 2 |> Option.map int |> Option.defaultValue 3

let ruleset = PaifuDifferential.ruleset

/// 跑一趟预热（JIT 与首次分配不进账），再跑 `repeats` 趟取**最小值**——
/// 最小值最接近「没被别的进程打扰的那一趟」，而这台机器是共享的。
let sample (action: unit -> 'a) : float * 'a =
    let warmed = action ()
    GC.Collect()

    let best =
        [ 1..repeats ]
        |> List.map (fun _ ->
            let clock = Stopwatch.StartNew()
            action () |> ignore
            clock.Elapsed.TotalMilliseconds)
        |> List.min

    best, warmed

let paths =
    Directory.GetFiles(Path.Combine(directory, "mjai"), "*.mjson")
    |> Array.sort
    |> Array.truncate limit

eprintfn $"剖面样本：{paths.Length} 场 ← {directory}（每段预热 1 趟 + 取 {repeats} 趟最小值）"

// ---- 一段一段量 ----

let ioMs, texts = sample (fun () -> paths |> Array.map File.ReadAllLines)

let mjsonMs, games =
    sample (fun () ->
        Array.zip paths texts
        |> Array.choose (fun (path, lines) ->
            PaifuGame.parse (Path.GetFileNameWithoutExtension path) lines |> Result.toOption))

let oraclePaths =
    paths
    |> Array.map (fun path ->
        Path.GetFileNameWithoutExtension path
        |> fun id -> Path.Combine(directory, "tenhou", id + ".json"))
    |> Array.filter File.Exists

let oracleTexts = oraclePaths |> Array.map File.ReadAllText

let oracleMs, oracles =
    sample (fun () ->
        Array.zip oraclePaths oracleTexts
        |> Array.choose (fun (path, text) ->
            PaifuOracle.parse ruleset.SeatCount (Path.GetFileNameWithoutExtension path) text
            |> Result.toOption))

let kyokus = games |> Array.collect (fun game -> game.Kyokus |> List.toArray)

let wallMs, _ = sample (fun () -> kyokus |> Array.map (PaifuReplay.wall ruleset))

let replayMs, replayed =
    sample (fun () -> kyokus |> Array.map (PaifuReplay.kyoku ruleset "剖面"))

let replays =
    Array.zip kyokus replayed
    |> Array.choose (fun (kyoku, outcome) ->
        match outcome with
        | Replayed replay -> Some(kyoku, replay.Events)
        | Skipped _ -> None)

let eventsMs, _ =
    sample (fun () ->
        replays
        |> Array.map (fun (kyoku, events) -> PaifuReplay.eventDiffs ruleset.SeatCount "剖面" kyoku events))

let oracleOf (game: PaifuGame) =
    oracles |> Array.tryFind (fun each -> each.Id = game.Id)

let gameMs, compared =
    sample (fun () -> games |> Array.map (fun game -> PaifuDifferential.game game (oracleOf game)))

// ---- 汇总 ----

let kyokuCount = kyokus.Length
let totalMs = ioMs + mjsonMs + oracleMs + gameMs
let engineMs = replayMs - wallMs - eventsMs
let compareMs = eventsMs + (gameMs - replayMs)
let restMs = totalMs - mjsonMs - oracleMs - wallMs - engineMs - compareMs

let diffs =
    compared
    |> Array.sumBy (fun (kyokus, _) -> kyokus |> List.sumBy (fun each -> List.length each.Diffs))

let row (name: string) (times: string) (ms: float) =
    let share = 100.0 * ms / totalMs
    printfn $"%-14s{name}\t%-22s{times}\t%8.3f{ms / float kyokuCount} ms/局\t%5.1f{share}%%"

printfn ""
printfn $"样本：{games.Length} 场 / {kyokuCount} 局，其中 {oracles.Length} 场有 oracle；差异 {diffs} 处"
printfn $"合计：{totalMs / float kyokuCount:F3} ms/局（{totalMs / 1000.0:F2} s / {kyokuCount} 局）"
printfn ""
row "mjson 解析" "每行一次" mjsonMs
row "天凤 JSON 解析" "每场一次" oracleMs
row "牌山重建" "每局一次" wallMs
row "引擎重放" "每动作一次" engineMs
row "比对" "每局一次" compareMs
row "其余（含读盘）" $"读盘 {ioMs / float kyokuCount:F3} ms/局" restMs
printfn ""
printfn $"参考：整场 `PaifuDifferential.game` {gameMs / float kyokuCount:F3} ms/局，其中重放 {replayMs / float kyokuCount:F3}"
