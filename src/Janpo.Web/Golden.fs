/// 黄金用例的**浏览器侧入口**（票 21）。
///
/// 跨界只传字符串（ADR-0005）：进去是用例文件的原文，出来是报告的 JSON 文本。
/// **比较逻辑不在这里**——它在 `Janpo.Golden`，两个目标编的是同一段 F#；
/// 这个文件只负责把 JS 侧的 JSON 后端接上去，与 CLI 的 `janpo golden check` 一一对应。
///
/// 无头闸门 `web/scripts/verify-golden.mjs` 在页面里 `import` 它并调一次 `check`。
module Janpo.Web.Golden

open Thoth.Json.Core
open Thoth.Json.JavaScript
open Janpo.Golden

/// mjai 事件在 JS 侧的序列化。**它是被测对象之一**：
/// 与 dotnet 侧的 Newtonsoft 后端逐字节相同，这份用例文件才对得上。
/// （决策包不走这条路：票 28 起它由 `GoldenJson.fields` 摊成逐字段，
/// 不经过宿主的 `toString`——对照的是 encoder 的结构与值，不是某个后端的排版。）
let private toText: IEncodable -> string = Encode.toString 0

/// 跑一份用例文件，回一份报告 JSON（`{cases, fields, lines, mismatches}`）。
/// 文件本身读不动时回 `{error}`——两种失败在闸门那侧都是红，但要分得清是谁的错。
let check (text: string) : string =
    match Decode.fromString GoldenSuite.decoder text with
    | Error message -> Encode.object [ "error", Encode.string message ] |> Encode.toString 0
    | Ok suite -> GoldenCheck.suite toText suite |> GoldenReport.encoder |> Encode.toString 0
