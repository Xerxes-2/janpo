namespace Janpo.Web

open Browser.Dom

/// 打开的是哪一页（票 71）。**只有两种**：首页与主持人那一页。
///
/// 「上帝视角」「危险度」「倍速」这类不在这里——它们是页面内的开关，不是地址。
[<RequireQualifiedAccess>]
type Landing =
    /// `/`：访客的第一眼。随应用分发的 Demo Paifu 自动播（ADR-0003），没有配桌与模型面板。
    | Home
    /// `?table=1`：主持人自己开一桌（配桌 + Live），**默认暂停**。
    | Table

/// 地址栏读出来的那点东西（票 71）。**页面侧认地址的地方只有这一个模块。**
///
/// ## 三者正交，各占地址的一格
///
/// - **query `table=1`** → 主持人那一页；不带就是首页。
/// - **query `dev=1`** → 曳光弹那一块（票 35，本票一字未改，只是从 `Main.fs` 搬到这里，
///   好让「认地址」这件事只有一处）。两个开关同时带也成立：`?table=1&dev=1`。
/// - **hash** → 分享载荷（票 77 编解码、票 78 接地址栏）。**hash 不当路由用**：
///   票 35 当年把 dev 开关放在 query 上就是为这件事——`base` 可配（`JANPO_BASE`），
///   而 hash 与将来的锚点抢同一根位置。**本票不解码 hash**：带 hash 打开退回首页 Demo，
///   不许白屏。
[<RequireQualifiedAccess>]
module Route =

    /// 地址里那一串 `a=1&b=2` 拆成一格一格。**一处解析**：两个开关读的是同一份。
    let private flags () : string array =
        window.location.search.TrimStart('?').Split('&')

    /// 开发向内容的开关（票 35）：地址里带 `?dev=1` 才把曳光弹挂出来。
    ///
    /// **访客看到的只该是牌桌**——README 那条「单纯面向用户」的标准同样管页面本身。
    /// 判据只有这一处，加新的开发向部件时挂到它后面即可。
    let devSurfaceRequested () : bool = flags () |> Array.contains "dev=1"

    /// 打开的是哪一页。**认不出来的 query 一律当首页**：陌生人手里那条链接可能带着
    /// 各种统计参数（`?utm_source=…`），它们不该把人踢到配桌页去。
    let landing () : Landing =
        if flags () |> Array.contains "table=1" then
            Landing.Table
        else
            Landing.Home

    /// 「自己开一桌」那条路指向哪儿。**只写在这一处**：页面上的链接与
    /// 无头闸门读的是同一个字符串，改地址不必两头找。
    let tableHref: string = "?table=1"
