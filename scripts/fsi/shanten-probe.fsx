// 引擎行为探针：直接调编译好的引擎 API（真代码、真不变量、5.8 µs/手）。
// 用法见同目录 README.md。
#load "load-engine.fsx"

open Janpo
open System.Diagnostics

let kindSet = TileKindSet.fourPlayer

let parse (s: string) =
    match Tile.parseMany s with
    | Ok t -> t
    | Error e -> failwithf "%A" e

// 争议手牌：手里握满 4 张 6m + 一对 2p（bigscan 日志里滚出来的那类）
let mk (h: string) =
    let tiles = parse h
    let naki = (13 - List.length tiles) / 3

    HandShape.create naki tiles
    |> Result.defaultWith (fun e -> failwithf "%s -> %A" h e),
    naki

for h in
    [
        "6m 6m 6m 6m 2p 2p 2p"
        "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
        "1m 1m 1m 1m 2m 3m 4m 9p 9p 9p 5z 5z 5z"
    ] do
    let hand, naki = mk h
    printfn "%-45s naki=%d shanten=%d" h naki (Shanten.calculate kindSet hand |> Shanten.value)

// 吞吐量：直接调 API，不经文本管道
let hands =
    System.IO.File.ReadLines "/tmp/bench-hands.txt"
    |> Seq.truncate 50000
    |> Seq.map (fun l ->
        HandShape.create 0 (parse (l.Substring 2))
        |> Result.defaultWith (fun e -> failwithf "%A" e))
    |> Seq.toArray

let sw = Stopwatch.StartNew()
let mutable acc = 0

for h in hands do
    acc <- acc + (Shanten.calculate kindSet h |> Shanten.value)

sw.Stop()
printfn "50000 手纯计算 %.2f 秒 = %.1f µs/手（校验和 %d）" sw.Elapsed.TotalSeconds (sw.Elapsed.TotalMicroseconds / 50000.0) acc
