module Janpo.Web.Footer

open Feliz

/// 仓库地址在页面侧**只写在这一处**，仓库改名时只改这一行。
///
/// README 那头同样只写一处（文件末尾那条 `[play]` 链接定义，旁边有一行注释说着同一件事）；
/// 站点地址与仓库地址各有各的唯一真源，两边不互相抄。
let private repoUrl = "https://github.com/Xerxes-2/janpo"

/// 版权行（票 46/37-A）。**与 `LICENSE` 及 README 末尾那一行同一个说法**
/// （`MIT © 2026 Xerxes-2`）：页面上只写这一处，改年份时连同那两份一起改。
/// 年份**不拿 `DateTime.Now` 算**：版权年是一件法律上的事实，不是看页那一天的日历。
let private copyright = "© 2026 Xerxes-2"

/// 许可正文由仓库地址派生，不另写一份地址。
///
/// `blob/HEAD/` 而不是 `blob/main/`：HEAD 由 GitHub 解析成当前默认分支，
/// 哪天默认分支改了名（main/master/trunk）这条链接也不会烂。
let private licenseUrl = $"{repoUrl}/blob/HEAD/LICENSE"

/// **第三方组件那一条**（票 92；ADR-0006 边界 4，Apache-2.0 §4 的分发义务）。
///
/// 强 AI 基线那份 `.wasm` 里静态链着第三方的 Apache-2.0 代码与内嵌权重，
/// 因此站点上必须带上那份 `LICENSE` 与 `NOTICE`（§4(a)/(d)），
/// 并在人看得见的地方给出归属。三份文件都在 `web/public/third-party/`，
/// 清单逐条写在 `probe/akagi-wasm/NOTICE-upstream.md`。
///
/// **它不挂在「选了那一席才显示」后面**：署名义务是分发件的义务，
/// 而那份产物随站点一起发出去——藏到一个条件后面就等于没有（同页脚那条判断 1）。
/// 票 102 在配桌页与牌桌上又添了两处署名（人**遇到它**的那一刻），
/// **那两处不代替这一条**：它们各自挂在「拨到了那一席」后面，而这一条不挂在任何条件后面。
///
/// **地址与那几个字在 `Credit` 里**（票 102）：配桌页那一句也要链到同一份声明，
/// 两处各写一份路径就是下一个 bug。那一行同时交代了为何按 `document.baseURI` 解析。
let private thirdPartyUrl () : string = Credit.thirdPartyUrl ()

/// 页脚里的一条外链。**另开一个标签页**：这个平台没有后端也不存档，
/// 正在看的那一局只活在当前页面的内存里（README「没有实时观战」那节说的就是这件事），
/// 在原地跳走等于把人打了一半的牌局扔掉。`rel` 是 `target="_blank"` 的例行伴随项。
let private link (url: string) (text: string) =
    Html.a [
        prop.href url
        prop.target "_blank"
        prop.rel "noopener noreferrer"
        prop.text text
    ]

/// 站点页脚（票 37）：访客从别处的链接直接落到站点上时，**这是回到源码、许可与项目进度的唯一一条路**。
///
/// 两条判断写在这里备查：
///
/// 1. **它在默认视图里，不挂在 `?dev=1` 后面**（那个开关是票 35 给开发向内容用的）。
///    这一行正是给普通访客看的，藏起来就等于没有。
/// 2. **措辞与页面其余部分同一个语感**：只说访客关心的那几件事（能读到源码、现在做到哪一步、
///    按什么许可放出、是谁哪一年的），不提工具链——那些话属于仓库里的开发手册。
///
/// 样式见 `web/src/styles.css` 的 `.site-footer`：小字、淡色、一条细线隔开，
/// 不与牌桌抢注意力。
[<ReactComponent>]
let Bar () =
    Html.footer [
        prop.className "site-footer"
        prop.testId "site-footer"
        prop.children [
            Html.span "源码、现在做到哪一步、以及页面里提到的那几份文档，都在 "
            link repoUrl "GitHub 上的 Xerxes-2/janpo"
            Html.span "。按 "
            link licenseUrl "MIT 许可"
            Html.span $"放出。{copyright}。强 AI 基线那份产物含第三方 Apache-2.0 代码与内嵌权重，归属与许可见 "
            link (thirdPartyUrl ()) Credit.thirdPartyText
            Html.span "。"
        ]
    ]
