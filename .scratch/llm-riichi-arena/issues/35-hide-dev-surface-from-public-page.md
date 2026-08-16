# 35 — 线上页面把开发向内容藏起来

**What to build:** Pages 已经上线（https://xerxes-2.github.io/janpo/ ），调度器打开线上页面看到：
牌桌下面紧接着就是 19 号票的调试页「janpo —— 浏览器里的第一颗曳光弹」，正文写着
「同一套 F# 引擎源码，经 Fable 编成 JS 在这里跑……应与 dotnet 侧同种子的 CLI 输出逐项相同」。
**这是开发向内容，现在对所有访客可见。**

主人对 README 的裁决是「单纯面向用户，不需要别人了解怎么开发」——**同一条标准适用于页面本身**。
曳光弹**删不掉**（`web/scripts/verify-tracer.mjs` 的浏览器内对拍闸门依赖它），所以是藏，不是删。

**Blocked by:** None — can start immediately.

**Status:** ready-for-human

- [x] 曳光弹整块藏到一个开关后面（query 参数或 hash，例如 `?dev=1`），默认访客看不到
      —— `?dev=1`，判据只在 `src/Janpo.Web/Main.fs` 的 `devSurfaceRequested` 一处
- [x] `web/scripts/verify-tracer.mjs` 跟着改成带开关的地址；**那道对拍闸门必须继续绿**
      （`./scripts/ci.sh` 全绿是这一票的硬条件）—— `./scripts/ci.sh` EXIT=0；
      那一道另加了一段反向自证：先开不带开关的地址，确认开发向内容一样不在
- [x] 页面上的开发向措辞改成用户向：h1 现在是「最小牌桌」，说明里有「类型层面」「投影」这类词。
      **保住实质、换掉行话**——「他家的暗牌在类型层面就不存在」这件事对用户是卖点
      （凭什么信它不作弊），但要用人话说 —— 措辞对照见报告 §3
- [x] `<title>` 与 `meta description` 已由票 33 改成用户向，**别动** —— `web/index.html` 一字未改
- [x] 自己用无头浏览器打开**线上以外**的本地构建产物截图**并亲眼看**：
      默认视图里没有任何开发向内容；带上开关后曳光弹还在 —— 两张图都真看了，见报告 §2

## 边界

- [x] **不碰** `README.md`（票 33 已定稿）、`web/src/agent/`（票 31 在跑）、
      `web/scripts/verify-export.mjs`（票 34 在跑）、引擎、`CONTEXT.md`
- [x] 不做牌桌布局改造（M2 的活）。这一票只管**哪些东西该给访客看**，不管好不好看
- [x] 不碰 `.github/workflows/`（调度器刚修过 pages.yml）
