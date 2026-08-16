# 28 — 裁决落地：PendingKan、立直宣言牌、决策包逐字段、Roster 进术语表

**What to build:** 把主人回来后拍定的三项裁决一次落地。三项都碰同一批文件（`Observation`、决策包的
编解码、黄金用例），所以合成一票做，避免和 25 / 26 抢文件。裁决的原文与理由在
`DECISIONS.md`「# M1」段的「### 处置」表里。

**Blocked by:** 25, 26

**Status:** ready-for-agent

## 一、`Observation` 补 `PendingKan`（裁决 20-A）

抢杠窗口里，投影现在看不见「有人刚宣言了加杠」——引擎等抢杠时不改局面，那组杠只出现在 Hora 动作的
label 里。补上它，让 LLM 与围观者都能看到对家动了什么牌。

- [ ] `Observation` 加 `PendingKan: Naki option`，wire 上的字段名沿用 mjai 的习惯
- [ ] 只在抢杠窗口里是 `Some`，其余时刻是 `None`（属性测试钉住这条）
- [ ] 上帝视角同步能看到
- [ ] 危险度与脚手架若能用上它则用上（25 号票落地后按实际情况判断，用不上就只管投影）

## 一之二、`Observation` 补立直宣言牌与立直巡目（调度器复核 prompt 时发现）

`RiichiState.Declared` 携带的是 `None|Riichi|DoubleRiichi`，编码器只输出 `"declared"/"accepted"`。
于是投影里能看出「对家立直了」，**但看不出是第几巡立的、哪张是宣言牌、河里哪几张是立直之后打的**。
真牌桌上宣言牌是横放的**公开信息**，早立直 / 追立直、筋引っかけ的判断全靠它。这不是加料，是补漏。

- [ ] `KawaEntry` 能看出哪一张是立直宣言牌（横放），以及每张牌是立直前还是立直后打出的
- [ ] 观测里能读出立直宣言的巡目（`Declared` / `Accepted` 携带的信息不要在编码时丢掉）
- [ ] 两立直（DoubleRiichi）与普通立直可区分
- [ ] Bare 档 prompt 的河渲染把宣言牌标出来（`prompt.ts` 的 `kawa`，摸切已经用 `*`，宣言牌另择一个记号）
- [ ] 牌桌上宣言牌也要看得出来（22 号票的河渲染，现在只区分手切摸切）
- [ ] 属性测试：没立直的座位永远读不出宣言牌；立直成立后宣言牌恒有且唯一

## 二、决策包用例拆成逐字段（裁决 21-c）

黄金用例现在把决策包 JSON 按整行钉住，2 KB 一行，加一个字段就印两条长行、只能写脚本比对。
这个代价已经付了三次（22 的 `tehai_count`、24 的 `scaffold`、25 的 Danger），拆掉它。

- [ ] 给 `DecisionPackage` 补 decoder，**注释与 DECISIONS 里都要写清：它只服务测试，不是产品路径**
      （产品边界仍是单向的 —— encoder 出去、动作 id 回来，20 号票的决策没有被推翻）
- [ ] `decide` 用例改成逐字段对照，报错能指到具体字段而不是整行
- [ ] 加一条反向测试：改坏决策包里的一个字段必须红，且报错点名那个字段

## 三、`Roster` 收进 `CONTEXT.md`（裁决 23-A）

「谁坐哪个座位」这个映射术语表里缺词条（有 Seat、有 Player，没有两者的绑定）。23 号票已经在用
`Roster` 这个名字，主人裁定收进术语表。

- [ ] `CONTEXT.md` 的「座席与选手」节加 `Roster（配桌）` 词条，写明它是 Seat → Player 的绑定、
      带 Ruleset，并给 _Avoid_ 列表（Table、Lineup、Setup 之类的同义词）
- [ ] 代码里的用法与词条一致，不一致就改代码

## 收尾

- [ ] `./scripts/ci.sh` 全绿；黄金用例若因 `PendingKan` 需要重录，按逐字段的新形态重录
