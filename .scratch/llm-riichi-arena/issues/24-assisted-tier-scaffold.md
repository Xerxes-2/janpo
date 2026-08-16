# 24 — Assisted 档脚手架

**What to build:** 把引擎算得出、LLM 算不明白的数值喂给它：向听数、有效牌（牌种与剩余枚数）、
以及**逐张试打的进退向标注**。主持人可以在座位上切 Bare / Assisted 两档，同一个局面下两档的 prompt
肉眼可比 —— 脚手架强度从此是个可对照的实验变量，而不是写死的行为。

**Blocked by:** 20, 23

**Status:** ready-for-agent

- [ ] `ScaffoldTier` 是座位级配置，三个 case（Bare / Assisted / ToolSearch）；M1 只实现前两档，
      ToolSearch 在配置面板里灰掉（它是 M3 的事，但类型现在就该完整）
- [ ] Assisted 档的决策包带：Shanten、Ukeire（牌种 + 剩余枚数）、每一张可打之牌的 ShantenDelta
- [ ] 数值一律**复用引擎既有实现**，不在 Agent 层重算、也不新写一份
- [ ] 措辞照 `CONTEXT.md`：向听数、有效牌、进退向、退向（向听戻し），中文只在渲染层出现
- [ ] 两档的 prompt 都能导出查看（调试出口），同局面对照不用改代码
- [ ] Agent 层的录制回放测试补上 Assisted 档的用例，兜底策略仍然生效

**为什么不含 Danger**：危险度是引擎的一个新分析模块，块头够单开一票（25 号）。这一票把
「脚手架数值进决策包、进 prompt」的通路铺好，25 号只往里加一节。
