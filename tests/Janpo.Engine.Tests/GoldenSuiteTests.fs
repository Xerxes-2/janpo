namespace Janpo.Engine.Tests

open System.IO
open Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo.Golden

/// 双目标黄金用例的 **dotnet 侧那一跑**（票 21）。
///
/// JS 侧的那一跑是 `web/scripts/verify-golden.mjs`（浏览器里跑 Fable 编出来的同一段代码），
/// **两侧读的是同一份 `tests/fixtures/golden/dual-target.json`**。于是「dotnet 绿、JS 红」
/// 只有一个意思：那条用例的那个字段在两个目标上算出来不一样。
///
/// 这里除了跑用例，还验**闸门本身**：期望被改坏一行必须红，且报错指得出用例、字段与行号——
/// 一个不会红的闸门比没有闸门更糟。
module GoldenSuiteTests =

    /// JSON 的具体后端由宿主带（dotnet 侧 Newtonsoft），`Janpo.Golden` 只依赖 `Thoth.Json.Core`。
    let private toText: IEncodable -> string = Encode.toString 0

    let private path = Path.Combine("fixtures", "golden", "dual-target.json")

    let private suite =
        match File.ReadAllText path |> Decode.fromString GoldenSuite.decoder with
        | Ok suite -> suite
        | Error message -> failwith $"读不动黄金用例文件 {path}：{message}"

    let private caseNamed (id: string) : GoldenCase =
        suite.Cases |> List.find (fun case -> case.Id = id)

    /// 把第一个字段的第一行改坏。用来验闸门会不会红。
    let private corrupt (case: GoldenCase) : GoldenCase =
        let drifted =
            case.Expect
            |> List.mapi (fun index (name, lines) ->
                if index = 0 then
                    name, lines |> List.mapi (fun line text -> if line = 0 then "漂了" else text)
                else
                    name, lines)

        { case with Expect = drifted }

    // ---- 用例本身 ----

    [<Fact>]
    let ``黄金用例在 dotnet 侧逐字段逐行一致`` () =
        let report = GoldenCheck.suite toText suite
        Assert.True(GoldenReport.isClean report, GoldenReport.toDisplay report)

    /// 票 21 点名的重点：数值（Rng / 整数除法与取整）、集合与 Map 的遍历顺序、
    /// 字符串与牌记法的往返、至少一场完整对局。**删掉任何一类都要红**。
    [<Fact>]
    let ``用例覆盖票里点名的每一类`` () =
        let kinds =
            suite.Cases |> List.map (fun case -> GoldenRun.kind case.Run) |> List.distinct

        let required =
            [
                "rng"
                "wall"
                "notation"
                "collection"
                "shanten"
                "hora"
                "points"
                "decide"
                "kyoku"
                "game"
            ]

        Assert.Equal<string list>([], required |> List.filter (fun kind -> kinds |> List.contains kind |> not))

    [<Fact>]
    let ``每条用例都有 id、说明与至少一个期望字段`` () =
        let incomplete =
            suite.Cases
            |> List.filter (fun case -> case.Id = "" || case.Note = "" || List.isEmpty case.Expect)
            |> List.map (fun case -> case.Id)

        Assert.Equal<string list>([], incomplete)

    [<Fact>]
    let ``至少一条用例是一整场对局，且事件流逐条钉住`` () =
        let games =
            suite.Cases
            |> List.filter (fun case -> GoldenRun.kind case.Run = "game")
            |> List.map (fun case -> case.Expect |> List.map fst)

        Assert.NotEmpty(games)

        for fields in games do
            Assert.Contains("events", fields)
            Assert.Contains("scores", fields)
            Assert.Contains("juni", fields)

    // ---- 闸门本身 ----

    [<Fact>]
    let ``期望被改坏一行就红，且指得出用例、字段与行号`` () =
        match caseNamed "rng-seed-0" |> corrupt |> GoldenCheck.case toText with
        | [ {
                Case = id
                Drift = GoldenDrift.Line(field, line, expected, actual)
            } ] ->
            Assert.Equal("rng-seed-0", id)
            Assert.Equal("draws", field)
            Assert.Equal(0, line)
            Assert.Equal("漂了", expected)
            Assert.Equal("21 51 31 12 31 65 68 14", actual)
        | drifts -> failwith $"""应当只报一处行不同，得到：{drifts |> List.map GoldenMismatch.toDisplay}"""

    [<Fact>]
    let ``一整场对局漂在第几条事件指得出来`` () =
        let game = caseNamed "game-1177"

        let drifted =
            game.Expect
            |> List.map (fun (name, lines) ->
                if name = "events" then
                    name, lines |> List.mapi (fun line text -> if line = 137 then "漂了" else text)
                else
                    name, lines)

        match GoldenCheck.case toText { game with Expect = drifted } with
        | [ {
                Drift = GoldenDrift.Line("events", line, "漂了", _)
            } ] -> Assert.Equal(137, line)
        | drifts -> failwith $"""应当只报第 137 条事件不同，得到：{drifts |> List.map GoldenMismatch.toDisplay}"""

    [<Fact>]
    let ``没有期望的用例不算通过`` () =
        let empty =
            { caseNamed "rng-seed-0" with
                Expect = []
            }

        match GoldenCheck.case toText empty with
        | [ { Drift = GoldenDrift.NoExpectation } ] -> ()
        | drifts -> failwith $"""空期望应当报 NoExpectation，得到：{drifts |> List.map GoldenMismatch.toDisplay}"""

    [<Fact>]
    let ``引擎多产出一个字段算用例数据过期`` () =
        let case = caseNamed "rng-seed-0"

        let partial =
            { case with
                Expect = case.Expect |> List.filter (fun (name, _) -> name <> "shuffle")
            }

        match GoldenCheck.case toText partial with
        | [ {
                Drift = GoldenDrift.UnexpectedField "shuffle"
            } ] -> ()
        | drifts -> failwith $"""应当报多出来的字段，得到：{drifts |> List.map GoldenMismatch.toDisplay}"""

    [<Fact>]
    let ``期望里有而引擎不产出的字段算漂`` () =
        let case = caseNamed "rng-seed-0"

        let extra =
            { case with
                Expect = case.Expect @ [ "不存在的字段", [ "0" ] ]
            }

        match GoldenCheck.case toText extra with
        | [ {
                Drift = GoldenDrift.MissingField "不存在的字段"
            } ] -> ()
        | drifts -> failwith $"""应当报缺字段，得到：{drifts |> List.map GoldenMismatch.toDisplay}"""

    [<Fact>]
    let ``id 重复要报出来`` () =
        let case = caseNamed "rng-seed-0"

        let doubled = { Note = ""; Cases = [ case; case ] }

        let report = GoldenCheck.suite toText doubled

        Assert.Contains(
            {
                Case = case.Id
                Drift = GoldenDrift.DuplicateId 2
            },
            report.Mismatches
        )

    [<Fact>]
    let ``refresh 之后必然一致`` () =
        let refreshed =
            GoldenCheck.refresh
                toText
                { suite with
                    Cases = suite.Cases |> List.map corrupt
                }

        Assert.True(GoldenReport.isClean (GoldenCheck.suite toText refreshed))

    [<Fact>]
    let ``用例文件的编解码往返`` () =
        let text = suite |> GoldenSuite.encoder |> Encode.toString 0

        match Decode.fromString GoldenSuite.decoder text with
        | Error message -> failwith $"重新解码失败：{message}"
        | Ok decoded -> Assert.Equal<GoldenSuite>(suite, decoded)

    // ---- 决策包逐字段（票 28，裁决 21-c） ----

    /// 决策包里随便挑的一个字段。挑他家的手牌张数是因为它埋得够深
    /// （对象 → 数组 → 下标 → 字段），能同时验下标进不进得了路径。
    [<Literal>]
    let private packageField = "package.observation.others.0.tehai_count"

    /// 这一票存在的理由：决策包不再按**整行**钉住（约 8 KB 一行）。
    [<Fact>]
    let ``decide 用例逐字段钉住，不再有那条整包的长行`` () =
        let names =
            suite.Cases
            |> List.filter (fun case -> GoldenRun.kind case.Run = "decide")
            |> List.collect (fun case -> case.Expect |> List.map fst)

        Assert.DoesNotContain("package", names)
        Assert.Contains("package.seat", names)
        Assert.Contains(packageField, names)

    /// 票面点名的反向测试：**改坏决策包里的一个字段必须红，且报错点名那个字段**。
    [<Fact>]
    let ``决策包漂一个字段就红，且报错点名那个字段`` () =
        let case = caseNamed "decide-42-first-hand"

        let drifted =
            case.Expect
            |> List.map (fun (name, lines) -> if name = packageField then name, [ "12" ] else name, lines)

        match GoldenCheck.case toText { case with Expect = drifted } with
        | [ {
                Case = id
                Drift = GoldenDrift.Line(field, line, expected, actual)
            } ] ->
            Assert.Equal("decide-42-first-hand", id)
            Assert.Equal(packageField, field)
            Assert.Equal(0, line)
            Assert.Equal("12", expected)
            Assert.Equal("13", actual)
        | drifts -> failwith $"""应当只报那一个字段不同，得到：{drifts |> List.map GoldenMismatch.toDisplay}"""

    /// **29 号票的考验**：往投影里加一个字段时，报错只多一条、指着新字段的路径，
    /// 而不是把整包重印一遍。这里用「期望里少一个字段」模拟那件事。
    [<Fact>]
    let ``决策包多一个字段只多一条报错`` () =
        let case = caseNamed "decide-42-first-hand"

        let dropped =
            { case with
                Expect = case.Expect |> List.filter (fun (name, _) -> name <> packageField)
            }

        match GoldenCheck.case toText dropped with
        | [ {
                Drift = GoldenDrift.UnexpectedField field
            } ] -> Assert.Equal(packageField, field)
        | drifts -> failwith $"""应当只报多出来的那一个字段，得到：{drifts |> List.map GoldenMismatch.toDisplay}"""
