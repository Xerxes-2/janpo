# 03 — Shanten、Ukeire 与和了型判定

**What to build:** 三个纯函数：Shanten、Ukeire、和了型判定，覆盖一般型、七对子与国士无双。这是整个引擎里风险最高的模块（立直合法性、Furiten、听牌料、和了判定、后续脚手架与复盘标注全部依赖它），因此正确性用现成实现作 oracle 随机对拍来锁死。允许实现得「丑但快」，但必须包在纯接口后面。

oracle 可直接用现成实现，不必自写；Python 生态里有现成的向听库，允许用 uv 直接跑（`uv run --with <pkg>`）作为**测试期的外部工具**，不得成为引擎或 CLI 的运行时依赖。

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] Shanten 支持一般型、七对子、国士无双，取三者最小值
- [ ] Ukeire 返回能让 Shanten 下降的牌种与各自剩余枚数（枚数基于可见信息）
- [ ] 和了型判定覆盖一般型、七对子、国士（只判型，不含役与点数）
- [ ] 含 Naki 组合的手牌 Shanten 正确
- [ ] 与 oracle 实现随机对拍大批量手牌，Shanten 差异为 0；oracle 的获取与调用方式可从零复现
- [ ] 属性：打 X 摸 X 后 Shanten 不变
- [ ] 属性：任意单次摸打后 Shanten 变化幅度 ≤ 1
- [ ] Tenpai 的数值约定（0 表示 Tenpai）在 CONTEXT.md 已定，实现与之一致
