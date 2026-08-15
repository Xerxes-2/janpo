# 10 — Pon 与 Chi

**What to build:** 鸣牌路径的前半：他家 Dahai 后的 Pon 与 Chi，副露后的打牌阶段，以及鸣牌对巡目与标志的影响。

**Blocked by:** 04

**Status:** ready-for-human

- [x] 他家 Dahai 后 Pon / Chi 进入相应座位的合法动作集（Chi 仅下家）
- [x] 鸣牌优先级正确：Ron > Pon / Kan > Chi（Kan 的位置已在 `nakiRank` 里留好，11 填）
- [x] Naki 后跳过摸牌直接进入打牌阶段，且禁止食替（现物与筋都禁）
- [x] Naki 组合作为公开信息进入 PlayerState，并产出对应 mjai 事件
- [x] Junme 与一发标志在 Naki 时更新正确
      （Junme：新增 `GameState.junme`，鸣牌与被跳过的座位都不涨；
      一发：立直与一发是 09 的事，本票只留下唯一的打断入口 `GameState.interruptIppatsu`）
- [x] 记录每家的河是否被鸣走过（Nagashi Mangan 的前提，由 12 票消费）：`PlayerState.kawaTaken`
- [x] 属性：Naki 后牌数守恒（暗牌 + Naki 组合张数恒定）
- [x] 黄金用例：吃碰后打牌、连续鸣牌、食替被拒
      （「Naki 打断一发」要等 09 的立直落地才写得出，钩子与位置见报告）
