module Janpo.Web.Main

open Browser.Dom
open Feliz

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
    | container -> ReactDOM.createRoot(container).render (App.TracerPage())
