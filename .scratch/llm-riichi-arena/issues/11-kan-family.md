# 11 — 三种杠及其连带效果

**What to build:** 鸣牌路径的后半：Ankan / Minkan / Kakan，以及杠带来的一整串连带规则——补摸、新宝牌、岭上开花、抢杠、大明杠责任支付。这一票的连带效果密度最高，黄金用例要按组合来写。

**Blocked by:** 10, 08

**Status:** ready-for-human

- [x] Ankan / Minkan / Kakan 的合法动作集正确，含立直后暗杠的条件
- [x] 杠后从王牌补摸；新宝牌指示牌的翻开时机区分明杠与暗杠
- [x] **事件的 wire 名照 mjai 原拼：`ankan` / `kakan` / `daiminkan` 三个分立**（mjai 没有统一的 `kan`），新宝牌翻开是独立的 `dora` 事件。前置任务在真实牌谱里实测过这四个，13 票对拍要对上
- [x] 岭上开花成立
- [x] 抢杠（对 Kakan 宣言 Ron）成立，且优先于杠的完成
- [x] 国士无双对 Ankan 的抢杠作为规则集字段：天凤禁止、雀魂允许（见 `smly/RiichiEnv` issue #43）
- [x] 大明杠责任支付（Sekinin Barai）的点数分担正确
- [x] 属性：杠后牌数守恒，王牌张数正确减少，牌山可摸张数正确减少
- [x] 黄金用例：加杠被抢杠、大明杠后岭上开花的责任支付、四杠前的第三个杠

**落地说明**（详见 `run/reports/11-kan-family.md` 与 DECISIONS 11-A…11-L）：

- 「王牌张数正确减少」按真实摆法落实：杠取走一张岭上牌的同时把可摸区的最后一张补进王牌，
  因此**王牌恒 14 张、可摸区每杠少一张、可用杠次数（`RinshanCount − kanCount`）减少**。
  不补充的话牌会凭空蒸发，牌数守恒那条属性就废了。
- 新宝牌的翻开时机照固件实测（18/18）：暗杠 `ankan → dora → tsumo`，明杠 `→ tsumo → dora`。
  与教科书「明槓は打牌後」有偏离，后果与待审项见 DECISIONS 11-B。
- 责任支付范围按天凤：大明杠后的岭上开花、大三元 / 大四喜由副露凑齐；四杠子无。
- 12 票要的杠数拆解器：`GameState.kanCount` 与 `PlayerState.kanCount`。
