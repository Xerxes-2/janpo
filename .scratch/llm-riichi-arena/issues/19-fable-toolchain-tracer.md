# 19 — Fable 工具链与浏览器里的第一颗曳光弹

**What to build:** 让 M0 的引擎**在浏览器里跑起来**。一条从 F# 源码到浏览器像素的完整细线：
`pnpm dev` 打开页面，固定种子让四个随机 Player 在浏览器内打完一局，页面上出现终局点数与顺位。
牌桌长什么样是 22 号票的事，这一票只要求「数字对，且是浏览器算出来的」。

同时把 spec 悬着的 **UI 形态 A/B 定夺**落成 ADR：选 **B（Feliz + useElmish）**。理由记在 ADR 里，
核心一条是 **F# 调 TS 便宜、TS 调 F# 贵** —— Feliz 侧 `import` 一个返回 Promise 的 TS 函数即可，
反过来要给每个跨界类型写 codec 或手写 `.d.ts`。

**Blocked by:** None — can start immediately.

**Status:** ready-for-human

- [x] Fable 工程（Feliz）引用引擎工程编译出 JS；引擎源码**一行不改**地被两个目标共用
- [x] Vite + pnpm 起来：`pnpm dev` 有 HMR，`pnpm build` 出可静态托管的产物
- [x] 浏览器内跑固定种子的一局，页面显示终局点数与顺位，与 dotnet 侧同种子的 `janpo kyoku` 结果**逐项相同**
      （这一条就是双目标语义没漂的第一份证据，系统化的版本是 21 号票）
- [x] CI 增加 JS 侧关卡：Fable 编译 + `pnpm build` 必须绿；nix dev shell 里有 node 与 pnpm
- [x] 引擎依赖白名单闸门仍然绿（Fable 侧的运行时后端 `Thoth.Json.JavaScript` 属于 Web 工程，不得进引擎工程）
- [x] ADR-0005 记下 UI 形态 B 的决定与被否决的 A，以及「TS 只剩 Agent 层」的边界

**风险与纪律**：引擎从未被 Fable 编译过，踩坑是预期内的（`[<Struct>]`、`System.String.IsNullOrEmpty`、
整数除法与溢出语义、`List.item` 的复杂度）。原则：**不许为 Fable 分叉引擎逻辑**。真的必须分叉时用
`#if FABLE_COMPILER` 并在 `DECISIONS.md` 记一条，说明分叉的语义差异是什么。

**布局建议**（实现者可改，改了记一条）：Feliz 工程放 `src/Janpo.Web`，Vite 应用放 `web/`，
Fable 输出目录 gitignore 掉；新工程记得进 `janpo.slnx`。
