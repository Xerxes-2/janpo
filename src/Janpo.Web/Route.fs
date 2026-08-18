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
///   而 hash 与将来的锚点抢同一根位置。**hash 里只有载荷这一样东西**（票 78）：
///   落在哪一页仍由 query 说了算，载荷只决定首页那一屏放的是哪一场。
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

    /// hash 里那一段分享载荷；没有就是 None（票 78）。
    ///
    /// **`#` 后面直接就是 base64url 那一串**，没有 `p=` 这类键名（裁决 35-1：hash 只装载荷，
    /// 不当路由）：键名是在暗示「hash 里还会有别人」，而这里永远只有这一样东西。
    /// 载荷读不读得动不在这里判（那是 `Share.ofPayload` 的事）：这里只答「hash 里有没有东西」。
    let payload () : string option =
        match window.location.hash.TrimStart('#') with
        | "" -> None
        | payload -> Some payload

    /// 一段载荷 → 分享链接（票 78）：当前页的地址去掉 query，hash 里只有载荷。
    ///
    /// **不带 `?table=1` 也不带 `?dev=1`**：分享链接是给访客看的回放，
    /// 不该把配桌面板与曳光弹也摆出来。`pathname` 照抄当前页：站点部署在子路径下
    /// （GitHub Pages 是 `/janpo/`）时，写死斜杠开头会把人指到别的站去。
    let shareUrl (payload: string) : string =
        window.location.origin + window.location.pathname + "#" + payload
