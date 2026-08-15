# 04 — 摸打循环跑到荒牌 Ryuukyoku

**What to build:** 第一次无头跑通一个完整 Kyoku：四个随机 Player 摸牌、打牌，直到牌山耗尽形成荒牌 Ryuukyoku，并按听牌家数授受听牌料。此票内合法动作集只有 Dahai——不含 Naki、立直与和了，但循环骨架、错误路径与不变量在这里一次性立住。

**Blocked by:** 02, 03

**Status:** ready-for-human

- [x] step 的完整循环：Tsumo → 合法动作集 → Dahai → 下家 Tsumo
- [x] 摸牌后阶段与他家打牌后响应阶段用不同类型区分，各自携带各自的合法动作集
      （`Phase.AwaitingDahai` / `Phase.AwaitingResponse`；本票无任何可响应动作，响应阶段恒为空、引擎不停在那里）
- [x] 牌山耗尽时产出荒牌 Ryuukyoku 事件，按听牌家数正确授受听牌料
- [x] 海底与河底的上下文标志在最后一张牌上置位（此票只置标志，不判役）
- [x] 非法 Action 一律返回 IllegalAction 值，不抛异常
- [x] 属性：任意时刻合法动作集非空，或 Kyoku 已终
- [x] 属性：牌数守恒
- [x] 属性：回放确定性——同一事件序列 fold 出同一 GameState
- [x] CLI 能用一个种子跑完一个 Kyoku，打印完整事件流与结算后点数

实现报告：`.scratch/llm-riichi-arena/run/reports/04-tsumogiri-loop-to-ryuukyoku.md`
