// 免 oracle 的整年包扫描（票 62）：从年度 zip 里**流式**读 mjai 牌谱做对拍，不落解包件。
// 磁盘净增长只有差异行与断点行（判据：接近零；压缩包本身与 /tmp 里的索引不算）。
//
//     python3 scripts/paifu/zip-index.py <zip> > /tmp/janpo-scan/index.tsv
//     nice -n 19 dotnet fsi --exec scripts/fsi/paifu-scan-zip.fsx -- <zip> <索引> <输出目录> [分片序号] [分片数]
//
// 与 `paifu-scan.fsx`（解包目录版）调同一个 API：`PaifuDifferential.game`，脚本不含规则逻辑。
// 差别有三，都是为「一个整年包 190–290 万场」这个量级：
//
//   1. **流式**：按索引里的坐标逐个成员解压到内存，处理完即丢；
//   2. **可续**：每 500 场把累计计数追加成一条 `CK` 行，重启从最后一条接着跑。
//      分片按索引行号取模，顺序确定，「已处理场数」就是断点；接续时校验断点场的 id，
//      分片参数拿错当场报错。被重做的 ≤500 场会在 diffs 文件里留重复行，汇总侧按整行去重；
//   3. **只落差异**：逐局的覆盖行不写盘（整年包写出来是 GB 级），只留累计计数与 D/S 行。
//      可疑场的原始牌谱**证据在压缩包里**（压缩包本来就留着），按 id 提取即可复现。
//
// 输出（在 <输出目录> 下，文件名带分片号）：
//   progress-<i>-of-<n>.tsv   CK 行：场数、末场 id、局数等计数、流局形态/差异类/役种计数表；
//                             跑完再加一条 DONE
//   diffs-<i>-of-<n>.tsv      D 行（差异）与 S 行（跳过），形状同 paifu-scan.fsx

#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Newtonsoft.Json.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Thoth.Json.Core.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Thoth.Json.Newtonsoft.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Janpo.Engine.dll"
#r @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0/Janpo.Engine.Tests.dll"

open System
open System.IO
open System.IO.Compression
open Janpo
open Janpo.Engine.Tests

let args = fsi.CommandLineArgs |> Array.toList |> List.skip 1

let zipPath =
    args
    |> List.tryItem 0
    |> Option.defaultValue "/home/xerxes2/janpo-corpus/2026.zip"

let indexPath =
    args |> List.tryItem 1 |> Option.defaultValue "/tmp/janpo-scan/index.tsv"

let outDir =
    args |> List.tryItem 2 |> Option.defaultValue "/home/xerxes2/janpo-corpus/scan"

let shard = args |> List.tryItem 3 |> Option.map int |> Option.defaultValue 0
let shards = args |> List.tryItem 4 |> Option.map int |> Option.defaultValue 1

Directory.CreateDirectory outDir |> ignore

let progressPath = Path.Combine(outDir, $"progress-{shard}-of-{shards}.tsv")
let diffsPath = Path.Combine(outDir, $"diffs-{shard}-of-{shards}.tsv")

// ---- 累计计数（一条 CK 行放得下的全部状态；断点续跑只靠它） ----

type Tally =
    {
        Processed: int
        LastId: string
        Kyokus: int
        Horas: int
        SettledSeats: int
        ScoreChecks: int
        DiffKyokus: int
        DiffGames: int
        Diffs: int
        Skips: int
        /// 之前各次运行累计的秒数；本次的钟另算，写 CK 行时相加。
        BaseElapsedS: float
        Reasons: Map<string, int>
        Kinds: Map<string, int>
        Yaku: Map<string, int>
    }

let emptyTally =
    {
        Processed = 0
        LastId = "-"
        Kyokus = 0
        Horas = 0
        SettledSeats = 0
        ScoreChecks = 0
        DiffKyokus = 0
        DiffGames = 0
        Diffs = 0
        Skips = 0
        BaseElapsedS = 0.0
        Reasons = Map.empty
        Kinds = Map.empty
        Yaku = Map.empty
    }

let bump (key: string) (counts: Map<string, int>) : Map<string, int> =
    counts |> Map.change key (Option.defaultValue 0 >> (+) 1 >> Some)

let renderCounts (counts: Map<string, int>) : string =
    if Map.isEmpty counts then
        "-"
    else
        counts
        |> Map.toList
        |> List.map (fun (key, count) -> $"{key}:{count}")
        |> String.concat ","

let parseCounts (text: string) : Map<string, int> =
    if text = "-" then
        Map.empty
    else
        text.Split ','
        |> Array.map (fun pair ->
            let cut = pair.LastIndexOf ':'
            pair.Substring(0, cut), int (pair.Substring(cut + 1)))
        |> Map.ofArray

let renderCheckpoint (elapsed: float) (tally: Tally) : string =
    [
        "CK"
        string tally.Processed
        tally.LastId
        string tally.Kyokus
        string tally.Horas
        string tally.SettledSeats
        string tally.ScoreChecks
        string tally.DiffKyokus
        string tally.DiffGames
        string tally.Diffs
        string tally.Skips
        $"%.0f{tally.BaseElapsedS + elapsed}"
        renderCounts tally.Reasons
        renderCounts tally.Kinds
        renderCounts tally.Yaku
    ]
    |> String.concat "\t"

let parseCheckpoint (line: string) : Tally =
    match line.Split '\t' with
    | [| "CK"
         processed
         lastId
         kyokus
         horas
         seats
         scores
         diffKyokus
         diffGames
         diffs
         skips
         elapsed
         reasons
         kinds
         yaku |] ->
        {
            Processed = int processed
            LastId = lastId
            Kyokus = int kyokus
            Horas = int horas
            SettledSeats = int seats
            ScoreChecks = int scores
            DiffKyokus = int diffKyokus
            DiffGames = int diffGames
            Diffs = int diffs
            Skips = int skips
            BaseElapsedS = float elapsed
            Reasons = parseCounts reasons
            Kinds = parseCounts kinds
            Yaku = parseCounts yaku
        }
    | _ -> failwith $"CK 行解析不了：{line}"

// ---- zip 成员的流式读取（坐标来自索引，`ZipArchive` 那条路会把 250 万个成员实体化） ----

/// 索引里的一行：成员名、本地头偏移、压缩后字节数、压缩方法。
type Member =
    {
        Name: string
        Offset: int64
        CompressedSize: int
        Method: int
    }

let parseMember (line: string) : Member =
    match line.Split '\t' with
    | [| name; offset; csize; method' |] ->
        {
            Name = name
            Offset = int64 offset
            CompressedSize = int csize
            Method = int method'
        }
    | _ -> failwith $"索引行解析不了：{line}"

/// 读一个成员的全文。本地头 30 字节里只用得到文件名与 extra 的长度（26–29 字节），
/// 压缩尺寸取中央目录（索引）那份——带 data descriptor 的包本地头里是 0。
///
/// 成员正文可能**还套着一层 gzip**（票 68 实测：2009–2024 的包把 `<id>.mjson.gz`
/// 原样塞进 zip、方法 0；2025/2026 是裸 JSON、方法 8）。这是打包方式差异不是牌谱格式差异，
/// 在这里按 gzip 魔数剥掉——裸 JSON 以 `{` 开头，不会撞上 `1F 8B`。
let readMember (file: FileStream) (member': Member) : string =
    let header = Array.zeroCreate 30
    file.Seek(member'.Offset, SeekOrigin.Begin) |> ignore
    file.ReadExactly(header, 0, 30)

    if
        header[0] <> 0x50uy
        || header[1] <> 0x4Buy
        || header[2] <> 3uy
        || header[3] <> 4uy
    then
        failwith $"{member'.Name}：偏移 {member'.Offset} 处不是本地文件头"

    let nameLength = int header[26] ||| (int header[27] <<< 8)
    let extraLength = int header[28] ||| (int header[29] <<< 8)

    file.Seek(member'.Offset + 30L + int64 (nameLength + extraLength), SeekOrigin.Begin)
    |> ignore

    let compressed = Array.zeroCreate member'.CompressedSize
    file.ReadExactly(compressed, 0, member'.CompressedSize)

    let raw =
        match member'.Method with
        | 0 -> compressed
        | 8 ->
            use inflate =
                new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress)

            use buffer = new MemoryStream()
            inflate.CopyTo buffer
            buffer.ToArray()
        | other -> failwith $"{member'.Name}：不认识的压缩方法 {other}"

    if raw.Length >= 2 && raw[0] = 0x1Fuy && raw[1] = 0x8Buy then
        use gunzip = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress)

        use reader = new StreamReader(gunzip, Text.Encoding.UTF8)
        reader.ReadToEnd()
    else
        Text.Encoding.UTF8.GetString raw

// ---- 分片与断点 ----

let mine =
    File.ReadLines indexPath
    |> Seq.indexed
    |> Seq.filter (fun (index, _) -> index % shards = shard)
    |> Seq.map (snd >> parseMember)
    |> Seq.toArray

let resumed =
    if File.Exists progressPath then
        let lines = File.ReadAllLines progressPath

        if lines |> Array.exists (fun line -> line.StartsWith "DONE\t") then
            eprintfn $"分片 {shard}/{shards} 已跑完（{progressPath} 里有 DONE），什么也不做"
            exit 0

        lines
        |> Array.filter (fun line -> line.StartsWith "CK\t")
        |> Array.tryLast
        |> Option.map parseCheckpoint
        |> Option.defaultValue emptyTally
    else
        emptyTally

if resumed.Processed > mine.Length then
    failwith $"断点说已处理 {resumed.Processed} 场，但这个分片只有 {mine.Length} 场——分片参数或索引对不上"

if
    resumed.Processed > 0
    && Path.GetFileNameWithoutExtension mine[resumed.Processed - 1].Name
       <> resumed.LastId
then
    failwith $"断点的末场 id（{resumed.LastId}）与索引第 {resumed.Processed} 场对不上——分片参数或索引对不上"

let remaining = mine |> Array.skip resumed.Processed

eprintfn $"分片 {shard}/{shards}：共 {mine.Length} 场，已处理 {resumed.Processed}，本次 {remaining.Length} 场 ← {zipPath}"

// ---- 扫描 ----

let clock = Diagnostics.Stopwatch.StartNew()
let file = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read)
let sink = new StreamWriter(diffsPath, append = true)

let checkpoint (tally: Tally) : unit =
    sink.Flush()
    File.AppendAllText(progressPath, renderCheckpoint clock.Elapsed.TotalSeconds tally + "\n")

    let rate = float (tally.Processed - resumed.Processed) / clock.Elapsed.TotalSeconds

    let left = float (mine.Length - tally.Processed) / rate / 60.0
    eprintfn $"  分片 {shard}：{tally.Processed}/{mine.Length} 场（{rate:F1} 场/s，剩约 {left:F0} 分钟）"

/// 一场的对拍结果并进累计。D/S 行就地写盘（**抓到差异就落盘**）。
let scanGame (tally: Tally) (member': Member) : Tally =
    let id = Path.GetFileNameWithoutExtension member'.Name

    let scanned =
        match PaifuGame.parse id ((readMember file member').Split '\n') with
        | Error reason ->
            sink.WriteLine $"S\t{id}\t{reason}"

            { tally with
                Skips = tally.Skips + 1
                DiffGames = tally.DiffGames + 1
            }
        | Ok paifu ->
            let compared, skipped = PaifuDifferential.game paifu None

            for where, reason in skipped do
                sink.WriteLine $"S\t{id}\t{where}：{reason}"

            let accumulated =
                (tally, compared)
                ||> List.fold (fun tally kyoku ->
                    let reason =
                        kyoku.Reason |> Option.map RyuukyokuReason.toMjai |> Option.defaultValue "hora"

                    for each in kyoku.Diffs do
                        sink.WriteLine $"D\t{id}\t{each.Where}\t{each.Kind}\t{each.Detail}"

                    { tally with
                        Kyokus = tally.Kyokus + 1
                        Horas = tally.Horas + kyoku.Horas
                        SettledSeats = tally.SettledSeats + kyoku.SettledSeats
                        ScoreChecks = tally.ScoreChecks + kyoku.ScoreChecks
                        DiffKyokus = tally.DiffKyokus + (if List.isEmpty kyoku.Diffs then 0 else 1)
                        Diffs = tally.Diffs + List.length kyoku.Diffs
                        Reasons = bump reason tally.Reasons
                        Kinds =
                            (tally.Kinds, kyoku.Diffs)
                            ||> List.fold (fun kinds each -> bump each.Kind kinds)
                        Yaku =
                            (tally.Yaku, kyoku.YakuSeen)
                            ||> List.fold (fun yaku each -> bump (OracleYaku.japanese each) yaku)
                    })

            let dirty =
                not (List.isEmpty skipped)
                || compared |> List.exists (fun kyoku -> not (List.isEmpty kyoku.Diffs))

            { accumulated with
                Skips = accumulated.Skips + List.length skipped
                DiffGames = accumulated.DiffGames + (if dirty then 1 else 0)
            }

    let advanced =
        { scanned with
            Processed = scanned.Processed + 1
            LastId = id
        }

    if advanced.Processed % 500 = 0 then
        checkpoint advanced

    advanced

let final = (resumed, remaining) ||> Array.fold scanGame

checkpoint final
File.AppendAllText(progressPath, $"DONE\t{final.Processed}\n")
sink.Flush()
sink.Dispose()
file.Dispose()

eprintfn
    $"分片 {shard} 完成：{final.Processed} 场 / {final.Kyokus} 局，差异 {final.Diffs} 处（{final.DiffKyokus} 局 / {final.DiffGames} 场），跳过 {final.Skips}，本次 {clock.Elapsed.TotalMinutes:F1} 分钟"
