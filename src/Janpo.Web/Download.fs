namespace Janpo.Web

open Fable.Core

/// 把一段文本交给浏览器下载。**整个工程里唯一碰下载 API 的地方**（票 26）。
///
/// 为什么是一段 `Emit` 而不是 Fable 的 DOM 封装：`URL.createObjectURL` 与 `Blob`
/// 在 `Fable.Browser.Dom` 里没有绑定，为一次下载引一个新包不值当。这段 JS 是标准做法
/// （造 Blob → 造对象 URL → 点一个隐藏的 `<a download>` → 立刻回收 URL），
/// **没有后端参与**：文件是浏览器自己在本地生成的（ADR-0003：本平台没有后端）。
///
/// dotnet 侧编得过、跑不了——与 `Store` 的 localStorage 同一处理：页面逻辑的用例
/// 在 dotnet 上跑，而它们不走这条路（副作用一律经 Cmd 发）。
[<RequireQualifiedAccess>]
module Download =

    [<Emit("(() => { const url = URL.createObjectURL(new Blob([$1], { type: $2 })); const anchor = document.createElement('a'); anchor.href = url; anchor.download = $0; document.body.appendChild(anchor); anchor.click(); anchor.remove(); URL.revokeObjectURL(url); })()")>]
    let private save (_fileName: string) (_content: string) (_mime: string) : unit = jsNative

    /// 下载一份 JSON 文件。
    let json (fileName: string) (content: string) : unit =
        save fileName content "application/json"
