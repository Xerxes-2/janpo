namespace Janpo.Golden

open Thoth.Json.Core

/// 把一份**已经编好的 JSON** 摊平成「路径 → 逐行的值」。
///
/// **这就是裁决 21-c 说的那个「决策包的 decoder」，落成了它唯一被需要的形状。**
/// 它读回来的是逐字段的文本，**不是 `DecisionPackage`，更不是 `Action`**：
/// 产品边界因此仍然是单向的（票 20）——包出去、一个动作 id 回来，TS 侧构造不出动作，
/// 这里同样构造不出。「它只服务测试」在这个形状下是**结构上的**，不靠注释守着。
///
/// 存在的理由只有一个：黄金用例此前把决策包按**整行**钉住（约 8 KB 一行），
/// 加一个字段就印两条长行、只能写脚本比对，这个代价已经付了三次（`tehai_count` /
/// `scaffold` / Danger）。摊平之后一个叶子一个字段，报错落到
/// 「用例 decide-42-first-hand 字段 package.observation.self.tehai 第 3 行」。
///
/// **它是测试脚手架，因此在 `Janpo.Golden` 而不在引擎里**（21-2）。
[<RequireQualifiedAccess>]
module GoldenJson =

    /// 编好的 JSON 的树形态。
    ///
    /// **数字与 true / false / null 在构造那一刻就渲染成文本**（`Literal`）：
    /// 渲染成什么样由这里说了算，两个目标编的是同一段 F#，因此
    /// 「dotnet 印 `69`、浏览器印 `69.0`」这类差异在类型层面就不存在。
    type private Node =
        /// 字符串。**原样落进用例文件**（不带引号）：这份文件要给人逐行核对，
        /// `"\"5m\""` 三层引号没人读得下去。
        | Text of string
        /// 数字 / true / false / null：已经渲染好的 JSON 字面量。
        | Literal of string
        /// 数组。
        | Items of Node list
        /// 对象，字段按 encoder 里写的先后。
        | Fields of (string * Node) list

    /// 往 `Node` 上编码的后端。**宿主的 JSON 后端（Newtonsoft / Thoth.Json.JavaScript）
    /// 在这条路上不出现**：摊平不经过文本，因此也不受某个后端的缩进与转义影响。
    let private helpers =
        { new IEncoderHelpers<Node> with
            member _.encodeString(value: string) = Text value
            member _.encodeChar(value: char) = Text(string value)
            member _.encodeDecimalNumber(value: float) = Literal(string value)

            member _.encodeBool(value: bool) =
                Literal(if value then "true" else "false")

            member _.encodeNull() = Literal "null"
            member _.encodeObject(fields: (string * Node) seq) = Fields(List.ofSeq fields)
            member _.encodeArray(items: Node array) = Items(List.ofArray items)
            member _.encodeList(items: Node list) = Items items
            member _.encodeSeq(items: Node seq) = Items(List.ofSeq items)
            member _.encodeResizeArray(items: ResizeArray<Node>) = Items(List.ofSeq items)
            member _.encodeSignedIntegralNumber(value: int32) = Literal(string value)
            member _.encodeUnsignedIntegralNumber(value: uint32) = Literal(string value)
        }

    // ---- 摊平 ----

    let private leaf (node: Node) : string option =
        match node with
        | Text value -> Some value
        | Literal value -> Some value
        | Items _
        | Fields _ -> None

    /// 一个叶子一个字段，字段名就是它在 JSON 里的路径（`a.b.0.c`）。
    ///
    /// 两处不逐个摊开，因为摊开了反而更难读：
    /// **全是标量的数组**是一个字段的多行（手牌漂一张，报错指得出第几张）；
    /// **空表与空对象**是一个零行的字段（「一个都没有」也要有位置被钉住，
    /// 否则它从字段表里消失就等于没被测）。
    let rec private flatten (path: string) (node: Node) : (string * string list) list =
        match node with
        | Text value -> [ path, [ value ] ]
        | Literal value -> [ path, [ value ] ]
        | Items [] -> [ path, [] ]
        | Items items ->
            let leaves = items |> List.choose leaf

            if List.length leaves = List.length items then
                [ path, leaves ]
            else
                items
                |> List.indexed
                |> List.collect (fun (index, item) -> flatten $"{path}.{index}" item)
        | Fields [] -> [ path, [] ]
        | Fields fields -> fields |> List.collect (fun (name, node) -> flatten $"{path}.{name}" node)

    /// 编一份 JSON 并摊平。`name` 是路径的头一段（黄金用例里就是 `package`）。
    let fields (name: string) (encodable: IEncodable) : (string * string list) list =
        encodable.Encode helpers |> flatten name
