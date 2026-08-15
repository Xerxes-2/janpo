# 10 — Pon 与 Chi

**What to build:** 鸣牌路径的前半：他家 Dahai 后的 Pon 与 Chi，副露后的打牌阶段，以及鸣牌对巡目与标志的影响。

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] 他家 Dahai 后 Pon / Chi 进入相应座位的合法动作集（Chi 仅下家）
- [ ] 鸣牌优先级正确：Ron > Pon / Kan > Chi
- [ ] Naki 后跳过摸牌直接进入打牌阶段，且禁止食替
- [ ] Naki 组合作为公开信息进入 PlayerState，并产出对应 mjai 事件
- [ ] Junme 与一发标志在 Naki 时更新正确
- [ ] 属性：Naki 后牌数守恒（暗牌 + Naki 组合张数恒定）
- [ ] 黄金用例：吃碰后打牌、连续鸣牌、Naki 打断一发、食替被拒
