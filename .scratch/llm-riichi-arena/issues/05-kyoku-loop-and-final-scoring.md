# 05 — Kyoku 循环、连庄与终局精算

**What to build:** 第一次无头跑完一整场东风战：Kyoku 之间的推进、连庄、Honba 与 Kyotaku 的结转、终局精算与顺位。此时随机 Player 仍只会摸打，但「打完一场对局且点数正确」这条主干在这里贯通。

**Blocked by:** 04

**Status:** ready-for-human

- [x] 无人和了时按规则决定进局或连庄（Oya 听牌时连庄）
- [x] Honba 在连庄与流局时递增，进局且非连庄时归零
- [x] Kyotaku 跨 Kyoku 结转，终局时归属正确
- [x] 东风战在东4局结束后终局；不做西入/延长（Out of Scope）
- [x] 局数序列从规则配置读（东风战 4 局、半庄 8 局；三麻半庄是 6 局），不写死
- [x] 终局精算产出顺位与最终点数
- [x] 属性：任意时刻四家点数与 Kyotaku 之和恒为初始总点
- [x] CLI 能用一个种子跑完一整场东风战，打印终局点数与顺位

实现报告：`.scratch/llm-riichi-arena/run/reports/05-kyoku-loop-and-final-scoring.md`
