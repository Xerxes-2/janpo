module Janpo.Web.Main

open Browser.Dom
open Feliz

/// 页面外壳：**牌桌永远在，19 票的曳光弹只在 `?dev=1` 时跟在下面**。
///
/// 曳光弹删不掉也不能做成标签页——`web/scripts/verify-tracer.mjs` 打开页面就直接按 testId
/// 找它的输入框与那几行数（双目标语义对拍的那道 CI 关卡），那是「同一套 F# 源码编到两个
/// 目标之后语义没漂」的第一份证据。所以是**藏**：那道闸门跟着开 `?dev=1` 的地址。
/// 牌桌自己的种子输入框另有一个 testId（`table-seed`），两边不打架。
///
/// 页脚（票 37）在**开关之外**：它是给访客的那条回仓库的路，任何视图下都该在，
/// 因此排在最后一行而不是跟着 `Route.devSurfaceRequested ()` 走。
///
/// **开关的判据搬去了 `Route`**（票 71）：页面侧认地址的地方只该有一处——
/// `?dev=1`、`?table=1` 与 hash 三者正交，三份解析写在三处只会各自漂。行为一字未改。
[<ReactComponent>]
let Shell () =
    Html.div [
        prop.className "shell"
        prop.children [
            TablePage.Page()
            if Route.devSurfaceRequested () then
                Html.hr []
                App.TracerPage()
            Footer.Bar()
        ]
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
