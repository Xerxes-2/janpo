module Janpo.Web.Footer

open Feliz

/// 仓库地址在页面侧**只写在这一处**，仓库改名时只改这一行。
///
/// README 那头同样只写一处（文件末尾那条 `[play]` 链接定义，旁边有一行注释说着同一件事）；
/// 站点地址与仓库地址各有各的唯一真源，两边不互相抄。
let private repoUrl = "https://github.com/Xerxes-2/janpo"

/// 许可正文由仓库地址派生，不另写一份地址。
///
/// `blob/HEAD/` 而不是 `blob/main/`：HEAD 由 GitHub 解析成当前默认分支，
/// 哪天默认分支改了名（main/master/trunk）这条链接也不会烂。
let private licenseUrl = $"{repoUrl}/blob/HEAD/LICENSE"

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
/// 2. **措辞与页面其余部分同一个语感**：只说访客关心的三件事（能读到源码、现在做到哪一步、
///    按什么许可放出），不提工具链——那些话属于仓库里的开发手册。
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
            Html.span "放出。"
        ]
    ]
