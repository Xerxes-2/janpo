// **断言执行次数普查（dotnet 侧）**——票 113。判据 3 问的是「它在真语料上执行过几次」，
// 这份脚本把那个数**逐条量出来**：一趟 `dotnet test` 里，测试工程里的每一个断言点被求值了多少次。
//
// **量的是「断言被求值」，不是「代码行被执行」的行覆盖率**。区别在于粒度：
// 这里的单位不是文件、不是方法，而是**序列点那一行**——F# 把 `&&` 的每一支、`match` 的每一臂
// 各留一个序列点，因此
// `| ResponseCause.Kan kan -> Naki.isKan kan && …` 这一支跑了几次是**独立数得出来**的。
// 票 98 那条「抢杠那一支等价于 `fun _ -> true`」正是这个数为 0。
//
// **这件工具的取景框有两处会骗人**（票 114 撞见，写在这儿免得下一个复量的人误读）：
//
//   1. **模块级私有助手的行不在任何成员行段里**，因此**不出现在普查表上**——
//      它不是「没被求值」，是这份工具收不到它。票 114 第 ⑦ 条（`advancedOrNext` 的换局支）
//      就是这种：普查表上看不见，原始 coverlet 数据里看得见（`runs/*-*.json`）。
//   2. **写进闭包里的断言，只有在成员自己先有一个序列点时才落进行段**——
//      否则**整段从表上消失**，看起来像「问题没了」。
//      改测试的写法之后复量，如果某几行凭空不见了，先怀疑这一条，别当成修好了。
//
// 两条都不是 bug，是「按行段取景」的固有边界。要越过它得换一套取景（例如按 IL 方法收），
// 那是另一件工具的事——**判据 4：抓不住的写出来。**
//
// 三档单位（`kind` 那一列）：
//
//   * `member`：一条 `[<Fact>]` / `[<Theory>]` / `[<Property>]` 成员整体跑了几次
//     （Fact 是 1，Property 是 FsCheck 的样本数）
//   * `assert`：一次 `Assert.*` 调用（具名用例逐字钉住的那种断言）
//   * `branch`：属性 / 用例体内一条**有序列点的判断行**——`&&` 的一支、`match` 的一臂。
//     **零次的那些几乎全在这一档**：成员每趟都跑，可它里头真做判断的那一支一次也没进去。
//
// ## 工具
//
// 计数器是 **coverlet 的逐行命中数**（`--include-test-assembly`，只插桩测试工程，
// 引擎按原速跑）。它给的是每个方法里每个序列点的命中次数，不是「覆盖到没有」的布尔。
//
// **必须用 Debug 配置量**（实测，报告 §2）：Release 下 F# 优化器会把小的 `let private` 助手
// **内联**进调用点，原函数体从此没人进——于是 79 行会报成 0 次而它们其实每趟都在跑。
// Debug 关优化，这 79 条假零全部消失。Release 与 Debug 的其余数逐行对得上。
//
// **一趟不够**：FsCheck 每趟自换种子，稀疏分支这趟 3 次下趟 0 次（实测 18 行是这样）。
// 因此默认跑 3 趟，报 `min` 与 `max`：**`max = 0` 才算「零次」，`min = 0 < max` 是「有趟空转」**。
//
// ## 跑法
//
//   dotnet fsi --exec scripts/fsi/assertion-census.fsx                 # 3 趟，两个测试工程
//   dotnet fsi --exec scripts/fsi/assertion-census.fsx --runs 1        # 快看一眼
//   dotnet fsi --exec scripts/fsi/assertion-census.fsx --project Janpo.Engine.Tests
//   dotnet fsi --exec scripts/fsi/assertion-census.fsx --json /tmp/census.json   # 机器可读的全表
//   dotnet fsi --exec scripts/fsi/assertion-census.fsx --from /tmp/janpo-assertion-census/runs
//                                                                      # 拿上一次的覆盖数据重算，不重跑
//
// 头一次跑会往 `$TMPDIR/janpo-assertion-census/tools` 装一份 `coverlet.console`
// （dotnet tool，约 2 MB，之后走 NuGet 缓存）。装不上就用 `--from` 拿现成的覆盖数据重算。
//
// **它不进 `ci.sh`**：一趟 3× 的 `dotnet test` 加插桩要一分半，而它答的是「断言够不够硬」——
// 那是收尾时问一次的问题，不是每次提交都要问的。账算在报告 `113-assertion-census.md` §7。

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

// ---- 参数 ----

type Options =
    {
        /// 跑几趟（FsCheck 每趟换种子，稀疏分支要多趟才分得清「零次」与「有趟空转」）。
        Runs: int
        /// 只量这几个测试工程（空表示全量）。
        Projects: string list
        /// 拿这个目录里现成的覆盖数据重算，不重跑测试。
        From: string option
        /// 把全表写成 JSON。
        Json: string option
        /// 非零那一段印前几行。
        Top: int
    }

let defaults =
    {
        Runs = 3
        Projects = []
        From = None
        Json = None
        Top = 20
    }

let rec parseArgs (options: Options) (args: string list) : Options =
    match args with
    | [] -> options
    | "--runs" :: value :: rest -> parseArgs { options with Runs = int value } rest
    | "--project" :: value :: rest ->
        parseArgs
            { options with
                Projects = options.Projects @ [ value ]
            }
            rest
    | "--from" :: value :: rest -> parseArgs { options with From = Some value } rest
    | "--json" :: value :: rest -> parseArgs { options with Json = Some value } rest
    | "--top" :: value :: rest -> parseArgs { options with Top = int value } rest
    | unknown :: _ -> failwithf "不认识的参数 %s（用法见文件头）" unknown

let options = fsi.CommandLineArgs |> Array.toList |> List.tail |> parseArgs defaults

let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", ".."))

let workRoot = Path.Combine(Path.GetTempPath(), "janpo-assertion-census")

let runsRoot = defaultArg options.From (Path.Combine(workRoot, "runs"))

/// 两个测试工程：目录与它的程序集名（coverlet 的模块键）。
let allProjects =
    [
        "tests/Janpo.Engine.Tests", "Janpo.Engine.Tests"
        "tests/Janpo.Web.Tests", "Janpo.Web.Tests"
    ]

let projects =
    match options.Projects with
    | [] -> allProjects
    | picked -> allProjects |> List.filter (fun (_, name) -> List.contains name picked)

// ---- 跑测试、收覆盖数据 ----

/// 起一个进程，把它的输出收下来（只在失败时全印，否则只印挑出来的那几行）。
let execute (fileName: string) (arguments: string) : int * string =
    let info = ProcessStartInfo(fileName, arguments)
    info.WorkingDirectory <- repoRoot
    info.RedirectStandardOutput <- true
    info.RedirectStandardError <- true
    use proc = Process.Start info
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    proc.ExitCode, stdout + stderr

/// **延迟取**：`--from` 那一路根本不需要 coverlet，取得早一点就会在没网的机器上白白红一次。
let coverletPath =
    lazy
        (let tools = Path.Combine(workRoot, "tools")
         let local = Path.Combine(tools, "coverlet")

         if File.Exists local then
             local
         else
             printfn "装 coverlet.console 到 %s（只装这一次）…" tools

             let code, output =
                 execute "dotnet" $"tool install --tool-path {tools} coverlet.console"

             if code <> 0 then
                 failwithf "装不上 coverlet.console（%d）：\n%s\n没网的话用 --from 拿现成的覆盖数据重算。" code output

             local)

/// 一趟：把一个测试工程插桩跑一遍，覆盖数据落到 `runs/<run>-<project>.json`。
let measure (run: int) (directory: string, assembly: string) : string =
    let output = Path.Combine(runsRoot, $"{run}-{assembly}.json")
    let binaries = Path.Combine(directory, "bin", "Debug", "net10.0")

    let arguments =
        String.concat
            " "
            [
                binaries
                "--target dotnet"
                $"--targetargs \"test {directory} --configuration Debug --no-build\""
                "--include-test-assembly"
                $"--include \"[{assembly}]*\""
                "--format json"
                $"--output {output}"
            ]

    let started = Stopwatch.StartNew()
    let code, log = execute coverletPath.Value arguments

    if code <> 0 then
        failwithf "第 %d 趟 %s 没跑绿（%d）：\n%s" run assembly code log

    let passed =
        log.Split '\n'
        |> Array.filter (fun line -> line.Contains "已通过" || line.Contains "Passed!")
        |> Array.map (fun line -> line.Trim())
        |> Array.tryHead
        |> Option.defaultValue "（没找到测试汇总行）"

    printfn "  第 %d 趟 %-20s %5.1f 秒  %s" run assembly started.Elapsed.TotalSeconds passed
    output

let coverageFiles =
    match options.From with
    | Some _ ->
        if Directory.Exists runsRoot |> not then
            failwithf "%s 不在（`--from` 要一个装着覆盖数据的目录）" runsRoot

        let existing = Directory.GetFiles(runsRoot, "*.json") |> Array.toList

        if List.isEmpty existing then
            failwithf "%s 里没有覆盖数据" runsRoot

        printfn "拿 %s 里现成的 %d 份覆盖数据重算（没重跑测试）" runsRoot (List.length existing)
        existing
    | None ->
        Directory.CreateDirectory runsRoot |> ignore
        printfn "Debug 构建…"
        let code, log = execute "dotnet" "build janpo.slnx --configuration Debug"

        if code <> 0 then
            failwithf "Debug 构建失败：\n%s" log

        printfn "插桩跑 %d 趟 × %d 个测试工程：" options.Runs (List.length projects)

        [
            for run in 1 .. options.Runs do
                for project in projects -> measure run project
        ]

// ---- 解析覆盖数据 ----

/// 一份覆盖数据：`源文件 → 行号 → 命中次数`。同一行落在多个方法里时取最大值
/// （F# 的闭包会与外层方法共用行号；两边的数只可能是同一件事数了两遍）。
let readCoverage (path: string) : Map<string, Map<int, int>> =
    use document = JsonDocument.Parse(File.ReadAllText path)

    let lines =
        [
            for modules in document.RootElement.EnumerateObject() do
                for file in modules.Value.EnumerateObject() do
                    for typ in file.Value.EnumerateObject() do
                        for method in typ.Value.EnumerateObject() do
                            match method.Value.TryGetProperty "Lines" with
                            | true, element ->
                                for line in element.EnumerateObject() -> file.Name, int line.Name, line.Value.GetInt32()
                            | false, _ -> ()
        ]

    lines
    |> List.groupBy (fun (file, _, _) -> file)
    |> List.map (fun (file, entries) ->
        let byLine =
            entries
            |> List.groupBy (fun (_, line, _) -> line)
            |> List.map (fun (line, hits) -> line, hits |> List.map (fun (_, _, hit) -> hit) |> List.max)
            |> Map.ofList

        file, byLine)
    |> Map.ofList

/// 一个方法在源码里占的行段（`名字 → 起止行`）。方法名形如
/// `System.Boolean Janpo.Engine.Tests.KanProperties::抢杠那一轮…(Janpo.GameState)`。
let readMethodRanges (path: string) : Map<string, (string * int * int) list> =
    use document = JsonDocument.Parse(File.ReadAllText path)

    [
        for modules in document.RootElement.EnumerateObject() do
            for file in modules.Value.EnumerateObject() do
                for typ in file.Value.EnumerateObject() do
                    for method in typ.Value.EnumerateObject() do
                        match method.Value.TryGetProperty "Lines" with
                        | true, element ->
                            let numbers = [ for line in element.EnumerateObject() -> int line.Name ]

                            match numbers with
                            | [] -> ()
                            | _ -> file.Name, method.Name, List.min numbers, List.max numbers
                        | false, _ -> ()
    ]
    |> List.groupBy (fun (file, _, _, _) -> file)
    |> List.map (fun (file, entries) -> file, entries |> List.map (fun (_, name, first, last) -> name, first, last))
    |> Map.ofList

/// 两份覆盖数据合成一份（同一趟里两个测试工程各一份，覆盖的源文件互不相交）。
let mergeCoverage (left: Map<string, Map<int, int>>) (right: Map<string, Map<int, int>>) : Map<string, Map<int, int>> =
    right
    |> Map.fold
        (fun (merged: Map<string, Map<int, int>>) file lines ->
            match Map.tryFind file merged with
            | None -> Map.add file lines merged
            | Some existing ->
                let combined =
                    lines
                    |> Map.fold
                        (fun (into: Map<int, int>) line hits ->
                            Map.add line (max hits (defaultArg (Map.tryFind line into) 0)) into)
                        existing

                Map.add file combined merged)
        left

/// 文件名形如 `<趟>-<程序集>.json`：**同一趟的两个工程要合成一趟**，
/// 否则每个断言点都会在「另一个工程那份」里读到 0，「有趟空转」那一栏立刻变成噪音。
let runOf (path: string) : string =
    let name = Path.GetFileNameWithoutExtension path

    match name.IndexOf '-' with
    | -1 -> name
    | at -> name.Substring(0, at)

let coverages =
    coverageFiles
    |> List.groupBy runOf
    |> List.sortBy fst
    |> List.map (fun (_, files) -> files |> List.map readCoverage |> List.reduce mergeCoverage)

let methodRanges =
    coverageFiles
    |> List.map readMethodRanges
    |> List.reduce (fun left right ->
        right
        |> Map.fold
            (fun (merged: Map<string, (string * int * int) list>) file ranges -> Map.add file ranges merged)
            left)

/// 那一行在各趟里的命中次数。
let hitsOf (file: string) (line: int) : int list =
    coverages
    |> List.map (fun coverage ->
        coverage
        |> Map.tryFind file
        |> Option.bind (Map.tryFind line)
        |> Option.defaultValue 0)

// ---- 从源码认出断言点 ----

let sourceCache = Dictionary<string, string array>()

let sourceOf (file: string) : string array =
    match sourceCache.TryGetValue file with
    | true, lines -> lines
    | false, _ ->
        let lines = File.ReadAllLines file
        sourceCache[file] <- lines
        lines

let textAt (file: string) (line: int) : string =
    let lines = sourceOf file

    if line >= 1 && line <= lines.Length then
        lines[line - 1].Trim()
    else
        ""

let attributeLine = Regex @"^\s*\[<(Fact|Theory|Property)[(>]"

let declarationLine = Regex @"^\s*let\s+``([^`]+)``"

let parameterName = Regex @"\(\s*([a-z][\w']*)\s*:"

type Kind =
    | Member
    | Assert
    | Branch

/// 一个断言点零次意味着什么，**三档差得很远**（这是这份普查的统计口径，报告 §3 逐档解释）：
///
///   * `Check`：真判断点。零次 = **判据 3 那件事**：这条判断从来没被求值过。
///   * `Negative`：断言的失败支（`failwith …` / `-> false`）。**绿的时候它本来就是 0**，
///     与页面侧那句 `problems.push(…)` 同理——零次是好消息，不是问题。
///   * `Open`：放行支（`-> true`）。零次 = 那一类局面一次也没出现（判据 4 的「谁也到不了」），
///     是提示不是缺陷：它本来就什么也不守。
type Grade =
    | Check
    | Negative
    | Open

type Site =
    {
        File: string
        Line: int
        Kind: Kind
        /// 它属于哪条用例 / 属性。
        Owner: string
        /// 用例是 `Fact` / `Theory`，属性是 `Property`。
        OwnerKind: string
        Text: string
        Grade: Grade
        Hits: int list
    }

/// 那一行是判断、是失败支、还是放行支。**只看那一行的文本**：
/// 断言的失败支在这个仓库里只有两种写法（`failwith` 那族与 `-> false`），因此零假阳性。
let gradeOf (text: string) : Grade =
    // 行尾的 `// 注释` 先去掉再判：`| Error _ -> false // 换个写法必须构造得回来` 是失败支，
    // 带着注释判就会被错当成真判断点（实测两条）。行内字符串里含 `//` 的极少，认了这点粗。
    let stripped =
        match text.IndexOf "//" with
        | -1 -> text
        | at -> text.Substring(0, at)

    let body = stripped.TrimEnd(')', ' ')

    if stripped.Contains "failwith" || stripped.Contains "Assert.Fail" then
        Negative
    elif body.EndsWith "-> false" || body.EndsWith "-> None" then
        Negative
    elif body.EndsWith "-> true" || body = "true" then
        Open
    else
        Check

/// 一条被测成员：属性 / 用例的名字、种类、以及它在源码里的行段（行段取自覆盖数据里那个方法）。
/// 字段名与 `Site` 岔开（`Source` / `Attribute`）：F# 按最后声明的记录解析字段名，同名会串。
type TestMember =
    {
        Source: string
        Attribute: string
        Name: string
        First: int
        Last: int
        Declaration: int
    }

/// 扫一个测试工程的源码，把 `[<Fact>]` / `[<Theory>]` / `[<Property>]` 的成员逐条认出来。
let membersIn (directory: string) : TestMember list =
    let files =
        Directory.GetFiles(Path.Combine(repoRoot, directory), "*.fs")
        |> Array.filter (fun file -> file.Contains "/obj/" |> not)
        |> Array.sort
        |> Array.toList

    [
        for file in files do
            let lines = sourceOf file
            let ranges = methodRanges |> Map.tryFind file |> Option.defaultValue []

            for index in 0 .. lines.Length - 1 do
                let attribute = attributeLine.Match lines[index]

                if attribute.Success then
                    // 往下找那一行 `let ``名字`` …`。窗口开到 24 行是因为 `[<Theory>]` 后面
                    // 可能跟着十几行 `[<InlineData(…)>]`（实测：窗口开 6 行时漏掉了那唯一一条 Theory）。
                    let following =
                        [ index + 1 .. min (index + 24) (lines.Length - 1) ]
                        |> List.tryPick (fun next ->
                            let declaration = declarationLine.Match lines[next]

                            if declaration.Success then
                                Some(next + 1, declaration.Groups[1].Value)
                            else
                                None)

                    match following with
                    | None -> ()
                    | Some(declaration, name) ->
                        match ranges |> List.tryFind (fun (method, _, _) -> method.Contains $"::{name}(") with
                        | None -> ()
                        | Some(_, first, last) ->
                            yield
                                {
                                    Source = file
                                    Attribute = attribute.Groups[1].Value
                                    Name = name
                                    First = first
                                    Last = last
                                    Declaration = declaration
                                }
    ]

let members = projects |> List.collect (fst >> membersIn)

/// 一条成员里的断言点：成员本身一条，体内每一个有序列点的行各一条
/// （带 `Assert.` 的算 `assert`，其余算 `branch`）。
let sitesOf (test: TestMember) : Site list =
    let inside =
        [ test.First .. test.Last ]
        |> List.filter (fun line ->
            coverages
            |> List.exists (fun coverage ->
                coverage |> Map.tryFind test.Source |> Option.exists (Map.containsKey line)))

    [
        for line in inside do
            let text = textAt test.Source line

            let kind =
                if line = test.First then Member
                elif text.Contains "Assert." then Assert
                else Branch

            {
                File = test.Source
                Line = line
                Kind = kind
                Owner = test.Name
                OwnerKind = test.Attribute
                Text = text
                Grade = gradeOf text
                Hits = hitsOf test.Source line
            }
    ]

let sites = members |> List.collect sitesOf

// ---- 报数 ----

let relative (file: string) : string = Path.GetRelativePath(repoRoot, file)

let kindName (kind: Kind) : string =
    match kind with
    | Member -> "member"
    | Assert -> "assert"
    | Branch -> "branch"

let maxHits (site: Site) : int = List.max site.Hits

let minHits (site: Site) : int = List.min site.Hits

let never = sites |> List.filter (fun site -> maxHits site = 0)

let sometimes =
    sites |> List.filter (fun site -> minHits site = 0 && maxHits site > 0)

printfn ""
printfn "== 断言执行次数普查（dotnet 侧）=="

printfn
    "工程 %s；%d 趟 Debug 插桩；断言点 %d 条（member %d / assert %d / branch %d），成员 %d 条"
    (projects |> List.map snd |> String.concat " + ")
    (List.length coverages)
    (List.length sites)
    (sites |> List.filter (fun site -> site.Kind = Member) |> List.length)
    (sites |> List.filter (fun site -> site.Kind = Assert) |> List.length)
    (sites |> List.filter (fun site -> site.Kind = Branch) |> List.length)
    (List.length members)

/// 相邻的零次行归成一块（同一条成员、行号相差 ≤ 2）。一条属性的整段判断体空转时，
/// 逐行列出来是十几行噪音，归成一行才读得出「哪一支没进去过」。
let blocksOf (sites: Site list) : (Site * int * int) list =
    let folder (blocks: (Site * int * int) list) (site: Site) =
        match blocks with
        | (head, first, last) :: rest when head.Owner = site.Owner && head.File = site.File && site.Line - last <= 2 ->
            (head, first, site.Line) :: rest
        | _ -> (site, site.Line, site.Line) :: blocks

    sites
    |> List.sortBy (fun site -> site.File, site.Line)
    |> List.fold folder []
    |> List.rev

let graded (grade: Grade) (sites: Site list) : Site list =
    sites |> List.filter (fun site -> site.Grade = grade)

let printBlocks (sites: Site list) : unit =
    for site, first, last in blocksOf sites do
        let where =
            if first = last then
                $"{relative site.File}:{first}"
            else
                $"{relative site.File}:{first}-{last}"

        printfn "%-9s %-42s %s" (kindName site.Kind) where $"[{site.Owner}] {site.Text}"

printfn ""
printfn "== 零次：%d 趟一次也没被求值 —— %d 条 ==" (List.length coverages) (List.length never)

printfn
    "   甲档 真判断点 %d 条（⇐ 判据 3 要的就是这些）；乙档 失败支 %d 条（绿的时候本来就是 0）；丙档 放行支 %d 条（那一类局面没出现）"
    (never |> graded Check |> List.length)
    (never |> graded Negative |> List.length)
    (never |> graded Open |> List.length)

printfn ""
printfn "-- 甲档：真判断点零次（相邻行已归块）--"
printfn "%-9s %-42s %s" "kind" "位置" "所属成员 / 那一行"
never |> graded Check |> printBlocks

printfn ""
printfn "-- 丙档：放行支零次（那一类局面一次也没出现，判据 4）--"
never |> graded Open |> printBlocks

printfn ""
printfn "-- 乙档：失败支零次（逐文件计数；它们零次是绿的必然结果）--"

never
|> graded Negative
|> List.countBy (fun site -> relative site.File)
|> List.sortByDescending snd
|> List.iter (fun (file, count) -> printfn "   %4d %s" count file)

printfn ""
printfn "== 有趟空转：某趟 0 次、某趟不是 —— %d 条（判据 3 的「十趟九空转」那一族）==" (List.length sometimes)
printfn "%-9s %-42s %-14s %s" "kind" "位置" "逐趟" "所属成员 / 那一行"

for site in sometimes |> graded Check |> List.sortBy maxHits do
    let counts = site.Hits |> List.map string |> String.concat " "

    printfn
        "%-9s %-42s %-14s %s"
        (kindName site.Kind)
        $"{relative site.File}:{site.Line}"
        counts
        $"[{site.Owner}] {site.Text}"

printfn ""
printfn "== 非零那一段最靠前的 %d 条（升序）==" options.Top

let ranked =
    sites
    |> List.filter (fun site -> minHits site > 0)
    |> List.sortBy (fun site -> minHits site, maxHits site)

for site in ranked |> List.truncate options.Top do
    let counts = site.Hits |> List.map string |> String.concat " "

    printfn
        "%-9s %-46s %-14s %s"
        (kindName site.Kind)
        $"{relative site.File}:{site.Line}"
        counts
        $"[{site.Owner}] {site.Text}"

// ---- 恒真式：三条便宜的判据 ----
//
// **它们只抓得住形状，不抓语义**：一条把两侧写成不同表达式、却在所有可达输入上恒等的断言，
// 这里一条也抓不住（报告 §5 写明了这一层）。

printfn ""
printfn "== 恒真式嫌疑 =="

/// 甲：**成员每趟都跑，可它体内真做判断的行一次也没进去**——票 98 那条抢杠属性就是这个形状
/// （三支里两支直接 `true`，唯一做事的那支采样到不了，于是整条等价于 `fun _ -> true`）。
let vacuous =
    members
    |> List.filter (fun test ->
        let inside = sitesOf test

        let entry =
            inside
            |> List.tryFind (fun site -> site.Kind = Member)
            |> Option.map maxHits
            |> Option.defaultValue 0

        let judging =
            inside |> List.filter (fun site -> site.Kind <> Member && site.Grade = Check)

        entry > 0
        && List.isEmpty judging |> not
        && judging |> List.forall (fun site -> maxHits site = 0))

printfn "甲 · 成员跑得到、体内做判断的行全零（等价于 `fun _ -> true`）：%d 条" (List.length vacuous)

for test in vacuous do
    printfn "   %s:%d [%s] %s" (relative test.Source) test.Declaration test.Attribute test.Name

/// 乙：**属性的入参在函数体里一次都没出现**——`fun _ -> true` 的静态形状。
let unusedParameter =
    members
    |> List.choose (fun test ->
        let declaration = textAt test.Source test.Declaration

        let parameters =
            parameterName.Matches declaration
            |> Seq.map (fun each -> each.Groups[1].Value)
            |> Seq.toList

        // **声明行里 `=` 右边那截也算体**：单行属性（`let … (tile: Tile) = Tile.parse … tile`）
        // 整个体就在那一行上，不算进来会把它们全报成「入参没用到」（实测三条假阳性）。
        let inlineBody =
            match declaration.IndexOf " = " with
            | -1 -> ""
            | at -> declaration.Substring(at + 3)

        let body =
            [ test.Declaration + 1 .. test.Last ]
            |> List.map (textAt test.Source)
            |> List.append [ inlineBody ]
            |> String.concat "\n"

        let missing =
            parameters
            |> List.filter (fun name -> Regex.IsMatch(body, $@"(?<![\w']){Regex.Escape name}(?![\w'])") |> not)

        match missing with
        | [] -> None
        | _ -> Some(test, missing))

printfn "乙 · 入参在体内一次都没出现：%d 条" (List.length unusedParameter)

for test, missing in unusedParameter do
    printfn
        "   %s:%d [%s] %s —— 没用到 %s"
        (relative test.Source)
        test.Declaration
        test.Attribute
        test.Name
        (String.concat " " missing)

/// 丙：**断言两侧是同一个表达式**（`Assert.Equal(x, x)`、`x = x`）。逐字符扫括号深度，
/// 只在最外层那个逗号 / 等号上切，因此 `Assert.Equal(f (a, b), f (a, b))` 也切得对。
let splitTop (separator: char) (text: string) : string list =
    let folder (depth: int, current: string, parts: string list) (character: char) =
        match character with
        | '('
        | '['
        | '<' -> depth + 1, current + string character, parts
        | ')'
        | ']'
        | '>' -> depth - 1, current + string character, parts
        | _ when character = separator && depth = 0 -> depth, "", parts @ [ current ]
        | _ -> depth, current + string character, parts

    let depth, last, parts = text |> Seq.fold folder (0, "", [])
    ignore depth
    parts @ [ last ] |> List.map (fun part -> part.Trim())

let identicalSides (text: string) : bool =
    // 行尾注释先去掉；`let ``名字`` (x: T) = <体>` 这种单行属性要先把绑定那半截去掉，
    // 否则 `a = a` 会被当成 `let … = a = a` 三段而漏掉（阴性对照丙就是这样漏过一次的）。
    let stripped =
        match text.IndexOf "//" with
        | -1 -> text
        | at -> text.Substring(0, at)

    let body =
        if
            stripped.TrimStart().StartsWith "let "
            || stripped.TrimStart().StartsWith "member "
        then
            match stripped.IndexOf " = " with
            | -1 -> stripped
            | at -> stripped.Substring(at + 3)
        else
            stripped

    let insideCall =
        let opening = body.IndexOf '('

        if opening < 0 || body.TrimEnd().EndsWith ")" |> not then
            None
        else
            Some(body.Trim().Substring(opening + 1, body.Trim().Length - opening - 2))

    let pairEqual (parts: string list) =
        match parts with
        | [ left; right ] -> left = right && left <> ""
        | _ -> false

    let calls =
        match insideCall with
        | Some inner when body.TrimStart().StartsWith "Assert." -> splitTop ',' inner |> pairEqual
        | Some _
        | None -> false

    let comparisons =
        [ " = "; " <> " ]
        |> List.exists (fun operator ->
            body.Split(operator, StringSplitOptions.None)
            |> Array.toList
            |> List.map (fun part -> part.Trim())
            |> pairEqual)

    calls || comparisons

// **`member` 那一档也要查**：单行属性（`let … (x: T) = f x = f x`）整个体就在那一行上。
let mirrored = sites |> List.filter (fun site -> identicalSides site.Text)

printfn "丙 · 两侧同一个表达式：%d 条" (List.length mirrored)

for site in mirrored do
    printfn "   %s:%d [%s] %s" (relative site.File) site.Line site.Owner site.Text

// ---- 机器可读的全表 ----

match options.Json with
| None -> ()
| Some path ->
    let rows =
        sites
        |> List.map (fun site ->
            let counts = site.Hits |> List.map string |> String.concat ","

            $"""{{"file":{JsonSerializer.Serialize(relative site.File)},"line":{site.Line},"kind":"{kindName site.Kind}","grade":"{site.Grade}","owner":{JsonSerializer.Serialize site.Owner},"ownerKind":"{site.OwnerKind}","hits":[{counts}],"text":{JsonSerializer.Serialize site.Text}}}""")

    File.WriteAllText(path, "[\n" + String.concat ",\n" rows + "\n]\n")
    printfn ""
    printfn "全表写到 %s（%d 行）" path (List.length rows)
