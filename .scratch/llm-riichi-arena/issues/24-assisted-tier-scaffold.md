# 24 — Assisted 档脚手架

**What to build:** 把引擎算得出、LLM 算不明白的数值喂给它：向听数、有效牌（牌种与剩余枚数）、
以及**逐张试打的进退向标注**。主持人可以在座位上切 Bare / Assisted 两档，同一个局面下两档的 prompt
肉眼可比 —— 脚手架强度从此是个可对照的实验变量，而不是写死的行为。

**Blocked by:** 20, 23

**Status:** ready-for-human

- [x] `ScaffoldTier` 是座位级配置，三个 case（Bare / Assisted / ToolSearch）；M1 只实现前两档，
      ToolSearch 在配置面板里灰掉（它是 M3 的事，但类型现在就该完整）
      —— `LlmField.Tier` + `selectField`（三元组的第三项就是“选不选得了”），`AgentTests` 钉住三个 case
- [x] Assisted 档的决策包带：Shanten、Ukeire（牌种 + 剩余枚数）、每一张可打之牌的 ShantenDelta
      —— `Scaffold.fs`；包**恒**带它，档位只决定 prompt 渲不渲染（DECISIONS 24-1）
- [x] 数值一律**复用引擎既有实现**，不在 Agent 层重算、也不新写一份
      —— `Shanten.calculate` / `Ukeire.calculate` / `Ukeire.total`；TS 侧只把数排成行
- [x] 措辞照 `CONTEXT.md`：向听数、有效牌、进退向、退向（向听戻し），中文只在渲染层出现
      —— 向听数的中文由引擎 `toDisplay` 携带过界（ADR-0005 第 2 条），牌一律 mjai 记法
- [x] 两档的 prompt 都能导出查看（调试出口），同局面对照不用改代码
      —— `pnpm run prompt`（`--tier` / `--diff` / `--package`）；报告里贴了同一手牌两档的全文
- [x] Agent 层的录制回放测试补上 Assisted 档的用例，兜底策略仍然生效
      —— `ask-assisted.json`（真问过 DeepSeek，理由里引了有效牌与退向）+ 两条 `loop.test.ts` 用例；
      兜底本身在引擎：`Fallback` 的 Assisted 分支改成了不退向听的那一手

报告：`.scratch/llm-riichi-arena/run/reports/24-assisted-tier-scaffold.md`

**为什么不含 Danger**：危险度是引擎的一个新分析模块，块头够单开一票（25 号）。这一票把
「脚手架数值进决策包、进 prompt」的通路铺好，25 号只往里加一节。
