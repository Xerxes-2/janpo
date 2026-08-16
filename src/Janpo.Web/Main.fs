module Janpo.Web.Main

open Browser.Dom
open Feliz

/// 页面外壳：**牌桌在上，19 票的曳光弹在下，两个都挂着**。
///
/// 曳光弹不是可以切走的一页——`web/scripts/verify-tracer.mjs` 打开 `/` 就直接按 testId
/// 找它的输入框与那几行数（双目标语义对拍的那道 CI 关卡）。做成标签页的话它会拿不到，
/// 因此两页共存；牌桌自己的种子输入框另有一个 testId（`table-seed`），两边不打架。
[<ReactComponent>]
let Shell () =
    Html.div [
        prop.className "shell"
        prop.children [ TablePage.Page(); Html.hr []; App.TracerPage() ]
    ]

/// 浏览器入口。**F# 只暴露这一个函数给 JS**（`mount`），JS 侧一行 import 就够：
///
/// ```ts
/// import { mount } from "./generated/Main.js";
/// mount("janpo-root");
/// ```
///
/// 方向是有意的（ADR-0005）：跨界只往「F# 调 TS」这一边走，反过来要给每个类型写
/// codec 或 `.d.ts`。所以这里收的是一个元素 id 字符串，不是任何引擎类型。
let mount (elementId: string) : unit =
    match document.getElementById elementId with
    | null -> console.error $"找不到挂载点 #{elementId}"
    | container -> ReactDOM.createRoot(container).render (Shell())
