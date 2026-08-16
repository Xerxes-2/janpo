namespace Janpo.Golden

open Thoth.Json.Core

/// 一处对不上的形态。**每一种都指得出是哪个字段**——「漂了」这句话不带位置就没有用。
[<RequireQualifiedAccess>]
type GoldenDrift =
    /// 这条用例还没有任何期望。空期望不该静静通过：先跑 `janpo golden write`。
    | NoExpectation
    /// 期望里有这个字段，跑出来没有。
    | MissingField of field: string
    /// 跑出来多了这个字段，期望里没有——用例数据过期了。
    | UnexpectedField of field: string
    /// 行数不同。
    | LineCount of field: string * expected: int * actual: int
    /// 第 `index` 行不同（0 起）。
    | Line of field: string * index: int * expected: string * actual: string
    /// 这个 id 在文件里出现了不止一次。报错要指得出用例，id 就不能有两个。
    | DuplicateId of count: int

/// 一条对不上：哪个用例、怎么对不上。
type GoldenMismatch = { Case: string; Drift: GoldenDrift }

/// 一份用例文件跑下来的结论。
type GoldenReport =
    {
        /// 跑了几条用例。
        Cases: int
        /// 对照了几个字段。
        Fields: int
        /// 对照了几行。一整场对局的事件流是九百多行，数字大是正常的。
        Lines: int
        /// 对不上的地方，按用例与字段的顺序。空表 = 两侧一致。
        Mismatches: GoldenMismatch list
    }

/// 对不上的渲染。
[<RequireQualifiedAccess>]
module GoldenDrift =

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人看的中文形式。
    let toDisplay (drift: GoldenDrift) : string =
        match drift with
        | GoldenDrift.NoExpectation -> "没有任何期望字段——先跑 `janpo golden write` 把它写进用例文件"
        | GoldenDrift.MissingField field -> $"字段 {field} 期望里有，这一侧没跑出来"
        | GoldenDrift.UnexpectedField field -> $"字段 {field} 跑出来了，期望里没有（用例数据过期，跑 `janpo golden write`）"
        | GoldenDrift.LineCount(field, expected, actual) -> $"字段 {field} 期望 {expected} 行，实际 {actual} 行"
        | GoldenDrift.Line(field, index, expected, actual) -> $"字段 {field} 第 {index} 行：期望「{expected}」，实际「{actual}」"
        | GoldenDrift.DuplicateId count -> $"这个 id 在文件里出现了 {count} 次，id 在文件内必须唯一"

[<RequireQualifiedAccess>]
module GoldenMismatch =

    // ---- 渲染层出口（ADR-0001） ----

    let toDisplay (mismatch: GoldenMismatch) : string =
        $"用例 {mismatch.Case}：{GoldenDrift.toDisplay mismatch.Drift}"

/// 跑一份用例文件：逐条跑、逐字段逐行对照。
///
/// **两侧跑的是同一个函数、读的是同一份数据**，因此「dotnet 侧绿、JS 侧红」这句话
/// 只能有一个意思：那条用例的那个字段在两个目标上算出来不一样。
[<RequireQualifiedAccess>]
module GoldenCheck =

    // ---- 一条用例 ----

    let private compareLines (field: string) (expected: string list) (actual: string list) : GoldenDrift list =
        if List.length expected <> List.length actual then
            [ GoldenDrift.LineCount(field, List.length expected, List.length actual) ]
        else
            List.zip expected actual
            |> List.mapi (fun index (expected, actual) -> index, expected, actual)
            |> List.choose (fun (index, expected, actual) ->
                if expected = actual then
                    None
                else
                    Some(GoldenDrift.Line(field, index, expected, actual)))

    /// 一条用例跑出来的字段与期望的对照。
    let case (toText: IEncodable -> string) (case: GoldenCase) : GoldenMismatch list =
        let observed = GoldenObservation.run toText case

        let drifts =
            if List.isEmpty case.Expect then
                [ GoldenDrift.NoExpectation ]
            else
                let expectedNames = case.Expect |> List.map fst

                let compared =
                    case.Expect
                    |> List.collect (fun (name, expected) ->
                        match observed |> List.tryFind (fun field -> field.Name = name) with
                        | None -> [ GoldenDrift.MissingField name ]
                        | Some field -> compareLines name expected field.Lines)

                let extra =
                    observed
                    |> List.filter (fun field -> expectedNames |> List.contains field.Name |> not)
                    |> List.map (fun field -> GoldenDrift.UnexpectedField field.Name)

                compared @ extra

        drifts |> List.map (fun drift -> { Case = case.Id; Drift = drift })

    // ---- 一份文件 ----

    /// 跑整份文件。**id 重复也是一处对不上**：报错要指得出用例，id 就不能有两个。
    let suite (toText: IEncodable -> string) (suite: GoldenSuite) : GoldenReport =
        let duplicated =
            suite.Cases
            |> List.countBy (fun case -> case.Id)
            |> List.filter (fun (_, count) -> count > 1)
            |> List.map (fun (id, count) ->
                {
                    Case = id
                    Drift = GoldenDrift.DuplicateId count
                })

        let mismatches = suite.Cases |> List.collect (case toText)

        {
            Cases = List.length suite.Cases
            Fields = suite.Cases |> List.sumBy (fun case -> List.length case.Expect)
            Lines =
                suite.Cases
                |> List.sumBy (fun case -> case.Expect |> List.sumBy (snd >> List.length))
            Mismatches = duplicated @ mismatches
        }

    /// 整份文件按当前引擎重跑一遍，把期望换成跑出来的值。**`janpo golden write` 就是它**。
    /// 它不碰 `run`——用例的**输入**只能人写。
    let refresh (toText: IEncodable -> string) (suite: GoldenSuite) : GoldenSuite =
        let observed (case: GoldenCase) =
            { case with
                Expect =
                    GoldenObservation.run toText case
                    |> List.map (fun field -> field.Name, field.Lines)
            }

        { suite with
            Cases = suite.Cases |> List.map observed
        }

/// 结论的渲染与 JSON。
[<RequireQualifiedAccess>]
module GoldenReport =

    /// 报告里最多印几条对不上。事件流漂一行就是九百多条，全印出来没人看得下去；
    /// 完整清单仍在 `Mismatches` 里。
    [<Literal>]
    let private shown = 20

    // ---- 拆解 ----

    let isClean (report: GoldenReport) : bool = List.isEmpty report.Mismatches

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人看的中文形式，CLI 直接印它。
    let toDisplay (report: GoldenReport) : string =
        let summary = $"{report.Cases} 条用例、{report.Fields} 个字段、{report.Lines} 行"

        match report.Mismatches with
        | [] -> $"{summary}：全部一致 ✓"
        | mismatches ->
            let head = mismatches |> List.truncate shown |> List.map GoldenMismatch.toDisplay

            let rest =
                if List.length mismatches > shown then
                    [ $"…… 还有 {List.length mismatches - shown} 处" ]
                else
                    []

            [ $"{summary}：{List.length mismatches} 处对不上" ] @ head @ rest
            |> String.concat "\n"

    // ---- JSON ----

    /// 给 JS 侧的形状：无头闸门把 `mismatches` 原样印出来就是给人看的报错。
    let encoder: Encoder<GoldenReport> =
        fun report ->
            Encode.object
                [
                    "cases", Encode.int report.Cases
                    "fields", Encode.int report.Fields
                    "lines", Encode.int report.Lines
                    "mismatches",
                    report.Mismatches
                    |> List.map (GoldenMismatch.toDisplay >> Encode.string)
                    |> Encode.list
                ]
