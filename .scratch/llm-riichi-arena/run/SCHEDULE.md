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
| 01 | dispatched | | |
| 02 | pending | | |
| 03 | pending | | |
| 04 | pending | | |
| 05 | pending | | |
| 06 | pending | | |
| 07 | pending | | |
| 08 | pending | | |
| 09 | pending | | |
| 10 | pending | | |
| 11 | pending | | |
| 12 | pending | | |
| 13 | pending | | |
| 14 | pending | | |

## 早上先看哪里

1. 这张表 —— 谁 done、谁 parked
2. `DECISIONS.md` —— 夜里替你做的所有决定，逐条审
3. `PARKED.md`（若存在）—— 需要你裁决才能继续的
4. `reports/` —— 每票的完整报告与 review 结论
5. `jj log` —— 线性历史，一票一 commit
