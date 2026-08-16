# M0 跑批调度表

夜间无人值守。调度器＝主 agent，每票委派给后台 subagent；subagent 完成时主动唤醒调度器，调度器集成后派下一波。

**规则**（睡前定的四条）
- 并行：只 `{02,03}` 与 `{04,07}` 并行（各自 jj workspace），其余串行在 `default` 工作区
- 失败：重试一次 → 仍失败则 park，跳过依赖它的票，继续做其他能做的
- 分岔：agent 自行决策并记入 `DECISIONS.md` 待审
- review：blocking 项自动修一轮，其余记待审

## 波次

| 波 | 票 | 工作区 |
|---|---|---|
| W1 | 01 骨架与 Tile 记法 | default |
| W2 | 02 牌山配牌 ∥ 03 Shanten | ws-a ∥ ws-b |
| W3 | 04 摸打循环 ∥ 07 役种 | ws-a ∥ ws-b |
| W4 | 06 和了成立 | default |
| W5 | 09 立直 | default |
| W6 | 10 Pon/Chi | default |
| W7 | 08 符与点数 | default |
| W8 | 11 三种杠 | default |
| W9 | 05 Kyoku 循环与终局 | default |
| W10 | 12 特殊流局 | default |
| W11 | 13 真实牌谱对拍 | default |
| W12 | 14 M0 验收 soak | default |

串行段的顺序在满足 DAG 的前提下，优先解锁 12 与 13 这两张扇入最多的票。

## 状态

| 票 | 状态 | change | 备注 |
|---|---|---|---|
| 01 | **done** | szpzyvsk | 9 条自主决策，2 条提案待审（提案 01-A/01-B） |
| 02 | **done** | wmnqytql | 10 条决策，提案 02-A + 3 条待裁 |
| 03 | **done** | szxmlrtu | 对拍 100 万手差异 0，逼出 3 个真 bug；7 条决策 + 提案 03-A |
| 04 | **done** | vzoymsox | 223 测试；GameState/step/LegalActions 立起，事件流可回放 |
| 05 | **done** | qurrntxs | 415 测试；Game 层，GameState 语义未改；janpo game 是 soak 入口 |
| 06 | **done** | pstowroz | 334 测试；Hora/Ron/「过」、Furiten 两态、多家裁决 |
| 07 | **done** | zsxqxsly | 269→305 测试；Mentsu/Naki 表示 + 多分解取最高番；提案 07-A |
| 08 | **done** | lolyuwrx | 383 测试；Fu/Score + 接上无役不可和；修了 06 两个无役剧本 |
| 09 | **done** | vmurqows | 463 测试；reach/reach_accepted 两事件、一发、给 11 的 allowsAnkan 判据 |
| 10 | **done** | nzlvxmur | 437 测试；禁止食替判据三处共用；一发钩子留给 09 |
| 11 | **done** | wqzppokx | 493 测试；岭上/抢杠/新宝牌/责任支付；kanCount 给 12 |
| 12 | dispatched | | ws-a |
| 13 | pending | | 数据与固件已就位（D-7 前置），票已按缺口清单重写 |
| 14 | pending | | |

## 中途插入的调研（用户给了牌谱屋链接与三麻 bonus）

未派工的 8 张票已打补丁：02 03 05 08 10 11 小改，12 13 重写。
两条提案进了 `DECISIONS.md`：**S-A Ruleset 与 ADR-0004**（早上必看）、**S-B 三麻的门缝**。
关键发现：spec 漏了 **Nagashi Mangan** 与 **三家和了**，且头跳默认值与两大平台相反。

## 晨间裁决代码化（R-1/R-4/R-5/R-6）

四条分四个 commit 落地，每条之间 CI 绿：qxukuowm(R-6 改名) → ussuvuyx(R-1 默认双响) → xkmulykq(R-4 Ruleset 携带 TileKindSet) → pqvvzuku(R-5 Seat 换真类型)。
502 测试（R-5 新增 9 条 SeatTests，一条未删）。宽改动的验证方式：把基线整树抽到 /tmp 单独构建，用 fsi 对两个引擎跑同种子对局比事件频次——逐项相同。

## 资源纪律

没有常驻监控进程（看门狗已撤，见 SCHEDULER-NOTES 的 N-9）。纪律写在 `RUNBOOK.md`：
并行上限 4 进程、长跑 `nice -n 19`、全量跑之前先用 100 个文件估耗时。

## 早上先看哪里

1. 这张表 —— 谁 done、谁 parked
2. `DECISIONS.md` —— 夜里替你做的所有决定，逐条审
3. `PARKED.md`（若存在）—— 需要你裁决才能继续的
4. `reports/` —— 每票的完整报告与 review 结论
5. `jj log` —— 线性历史，一票一 commit
