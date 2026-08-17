// 离线扫大语料的牌谱对拍（票 57）。**CI 只跑固件**，这份是把同一套对拍开到几千场用的。
//
//     nice -n 19 dotnet fsi --exec scripts/fsi/paifu-scan.fsx -- <语料目录> [分片序号] [分片数]
//
// 语料目录的形状与 `JANPO_PAIFU_DIR` 同一份：`mjai/<id>.mjson` 必需，`tenhou/<id>.json` 有就当 oracle。
// 分片是为了并行（RUNBOOK：并行进程 ≤ 4），各分片输出可直接 `cat` 到一起。
//
// **它不含任何规则逻辑**：只调 `PaifuDifferential.game` 这个测试里跑的同一个 API，
// 把返回值打成机器可读的 TSV。差异与覆盖标记都由下游脚本汇总（见 57 号票报告）。
//
// 输出（TSV，首列是行类型）：
//   K  <场id> <局序号> <覆盖标记，逗号分隔，可重复计数>
//   C  <场id> <局标签> <和了数> <比符数> <比役行数> <清算座位数> <终局点数对拍数> <流局形态> <有无oracle> <役名…>
//   D  <场id> <局标签> <差异类> <差异详情>
//   S  <场id> <跳过原因>

// 引测试工程的输出目录：牌谱适配器（`PaifuGame` / `PaifuOracle` / `PaifuDifferential`）在那里，
// 而且它是可执行测试工程，NuGet 依赖都已复制过去（`scripts/fsi/README.md` 记的那个 FS0078 坑）。
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Newtonsoft.Json.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Thoth.Json.Core.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Thoth.Json.Newtonsoft.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Janpo.Engine.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Janpo.Engine.Tests.dll"

open System
open System.IO
open Janpo
open Janpo.Engine.Tests

let args = fsi.CommandLineArgs |> Array.toList |> List.skip 1

let directory = args |> List.tryItem 0 |> Option.defaultValue "data/paifu/full"

let shard = args |> List.tryItem 1 |> Option.map int |> Option.defaultValue 0
let shards = args |> List.tryItem 2 |> Option.map int |> Option.defaultValue 1

/// 一局的覆盖标记，全部从**牌谱自己的事件流**读出来（不问引擎，避免拿被测物当期望值）。
/// 标记可重复：出现几次就发几次，下游按出现次数汇总成覆盖表。
let private kanTags (moves: PaifuEvent list) : string list =
    let isDora (event: PaifuEvent) =
        match event with
        | PaifuEvent.Dora _ -> true
        | _ -> false

    let rec walk (events: PaifuEvent list) (acc: string list) : string list =
        match events with
        | PaifuEvent.Ankan _ :: rest -> afterKan "ankan" rest ("ankan" :: acc)
        | PaifuEvent.Kakan _ :: rest -> afterKan "kakan" rest ("kakan" :: acc)
        | PaifuEvent.Minkan _ :: rest -> afterKan "daiminkan" rest ("daiminkan" :: acc)
        | _ :: rest -> walk rest acc
        | [] -> acc

    and afterKan (kind: string) (events: PaifuEvent list) (acc: string list) : string list =
        match List.skipWhile isDora events with
        // 杠 → 补摸 → 当场和了：正是票 16 那条「明杠的宝牌指示牌翻早了」的路径。
        | PaifuEvent.Tsumo _ :: rest ->
            match List.skipWhile isDora rest with
            | PaifuEvent.Hora _ :: _ -> walk rest ($"{kind}-rinshan-hora" :: acc)
            // **打牌之前又杠一次**：明杠欠着的那张指示牌的翻牌时机就卡在这里（票 59）。
            // 按「前一杠是哪种 + 后一杠是哪种」分标：四种组合的事件顺序不同。
            | PaifuEvent.Ankan _ :: _ -> walk rest ($"{kind}-then-ankan" :: acc)
            | PaifuEvent.Kakan _ :: _ -> walk rest ($"{kind}-then-kakan" :: acc)
            | PaifuEvent.Minkan _ :: _ -> walk rest ($"{kind}-then-daiminkan" :: acc)
            | _ -> walk rest acc
        // 加杠之后没补摸就有人和了：抢杠。
        | PaifuEvent.Hora _ :: rest -> walk rest ("chankan" :: acc)
        | rest -> walk rest acc

    walk moves [] |> List.rev

/// 立直后的自家暗杠（票 63 E 族的形）：`reach_accepted` 之后同一家的 `ankan`。
let private riichiAnkanTags (moves: PaifuEvent list) : string list =
    let folder (accepted: Set<int>, acc: string list) (event: PaifuEvent) =
        match event with
        | PaifuEvent.RiichiAccepted actor -> Set.add (Seat.index actor) accepted, acc
        | PaifuEvent.Ankan(actor, _) when Set.contains (Seat.index actor) accepted -> accepted, "riichi-ankan" :: acc
        | _ -> accepted, acc

    ((Set.empty, []), moves) ||> List.fold folder |> snd |> List.rev

/// 鸣完打完、下一次摸牌之前就荣和（票 63 F 族的形）：和了者最近一次进张是碰 / 吃
/// （三种杠都会补摸岭上牌，走不进这一形）。
let private ronAfterNakiTags (moves: PaifuEvent list) : string list =
    let folder (fromNaki: Set<int>, acc: string list) (event: PaifuEvent) =
        match event with
        | PaifuEvent.Tsumo(actor, _) -> Set.remove (Seat.index actor) fromNaki, acc
        | PaifuEvent.Pon(actor, _, _, _)
        | PaifuEvent.Chi(actor, _, _, _) -> Set.add (Seat.index actor) fromNaki, acc
        | PaifuEvent.Hora hora when hora.Actor <> hora.Target && Set.contains (Seat.index hora.Actor) fromNaki ->
            fromNaki, "ron-after-own-naki" :: acc
        | _ -> fromNaki, acc

    ((Set.empty, []), moves) ||> List.fold folder |> snd |> List.rev

let private tags (kyoku: PaifuKyoku) : string list =
    let start = kyoku.Start
    let horas = kyoku.Moves |> List.choose PaifuEvent.hora

    let counted (name: string) (pick: PaifuEvent -> bool) =
        kyoku.Moves |> List.filter pick |> List.map (fun _ -> name)

    [
        yield $"bakaze-{Kaze.toMjai start.Bakaze}"
        if start.Honba > 0 then
            yield "honba"
        if start.Honba >= 3 then
            yield "honba3"
        if start.Kyotaku > 0 then
            yield "kyotaku"
        if start.Kyotaku >= 2 then
            yield "kyotaku2"
        yield!
            counted "riichi" (fun event ->
                match event with
                | PaifuEvent.Riichi _ -> true
                | _ -> false)
        yield!
            counted "pon" (fun event ->
                match event with
                | PaifuEvent.Pon _ -> true
                | _ -> false)
        yield!
            counted "chi" (fun event ->
                match event with
                | PaifuEvent.Chi _ -> true
                | _ -> false)
        yield! kanTags kyoku.Moves
        yield! riichiAnkanTags kyoku.Moves
        yield! ronAfterNakiTags kyoku.Moves
        match List.length horas with
        | 0 -> ()
        | 1 -> yield "hora"
        | 2 -> yield "double-ron"
        | _ -> yield "triple-ron"
        yield!
            horas
            |> List.filter (fun hora -> hora.Actor = hora.Target)
            |> List.map (fun _ -> "tsumo-hora")
        yield!
            horas
            |> List.filter (fun hora -> hora.Actor <> hora.Target)
            |> List.map (fun _ -> "ron-hora")
        if horas |> List.exists (fun hora -> List.length hora.UraDoraMarkers >= 2) then
            yield "ura2"
        if
            kyoku.Moves
            |> List.exists (fun event -> Option.isSome (PaifuEvent.ryuukyokuDeltas event))
        then
            yield "ryukyoku"
        if
            kyoku.Moves
            |> List.exists (fun event ->
                PaifuEvent.ryuukyokuDeltas event
                |> Option.map PaifuOracle.isNagashiDeltas
                |> Option.defaultValue false)
        then
            yield "nagashi-deltas"
    ]

let private render (values: string list) : string =
    match values with
    | [] -> "-"
    | _ -> String.concat "," values

let private scan (path: string) =
    let id = Path.GetFileNameWithoutExtension path

    match PaifuGame.parse id (File.ReadLines path) with
    | Error reason -> printfn "S\t%s\t%s" id reason
    | Ok paifu ->
        let oraclePath = Path.Combine(directory, "tenhou", id + ".json")

        let oracle =
            if not (File.Exists oraclePath) then
                None
            else
                match PaifuOracle.parse PaifuDifferential.ruleset.SeatCount id (File.ReadAllText oraclePath) with
                | Ok parsed -> Some parsed
                | Error reason ->
                    printfn "S\t%s\toracle 解析失败：%s" id reason
                    None

        for kyoku in paifu.Kyokus do
            printfn "K\t%s\t%d\t%s" id kyoku.Index (render (tags kyoku))

        let compared, skipped = PaifuDifferential.game paifu oracle

        for where, reason in skipped do
            printfn "S\t%s\t%s：%s" id where reason

        for kyoku in compared do
            let reason =
                kyoku.Reason |> Option.map RyuukyokuReason.toMjai |> Option.defaultValue "hora"

            printfn
                "C\t%s\t%s\t%d\t%d\t%d\t%d\t%d\t%s\t%b\t%s"
                id
                kyoku.Label
                kyoku.Horas
                kyoku.FuChecks
                kyoku.YakuChecks
                kyoku.SettledSeats
                kyoku.ScoreChecks
                reason
                kyoku.HasOracle
                (render (kyoku.YakuSeen |> List.map OracleYaku.japanese))

            for each in kyoku.Diffs do
                printfn "D\t%s\t%s\t%s\t%s" id each.Where each.Kind each.Detail

let files =
    Directory.GetFiles(Path.Combine(directory, "mjai"), "*.mjson")
    |> Array.sort
    |> Array.indexed
    |> Array.filter (fun (index, _) -> index % shards = shard)
    |> Array.map snd

eprintfn $"分片 {shard}/{shards}：{files.Length} 场 ← {directory}"

let clock = Diagnostics.Stopwatch.StartNew()

files
|> Array.iteri (fun index path ->
    scan path

    if (index + 1) % 200 = 0 then
        eprintfn $"  {index + 1}/{files.Length}（{clock.Elapsed.TotalSeconds:F0}s）")

eprintfn $"分片 {shard} 完成：{files.Length} 场，{clock.Elapsed.TotalSeconds:F1}s"
