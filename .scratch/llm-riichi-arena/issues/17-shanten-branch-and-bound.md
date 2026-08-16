# 17 — searchStandard 加分支限界剪枝

**What to build:** 给 `Shanten.searchStandard` 加分支限界剪枝。现在 `best` 只在叶子处取 min，**从不用于剪枝**——即使当前分支已不可能优于 `best`，搜索照样走完。研究实测这是最大的空子。

**Blocked by:** None — can start immediately

**Status:** ready-for-agent

## 研究已经做完了，别重做（`docs/research/shanten-search-alternatives.md`）

实测收益（真实牌谱抽的结构化手牌，交错跑四轮）：

| 场景 | 现状 | 剪枝后 |
|---|---|---|
| 单手 `calculate` | 8.67–9.36 µs | **1.28–1.36 µs（6.6–7.0×）** |
| 搜索节点数 | 10259/手 | **238/手** |
| 最难的 2000 手 | 60.4 µs | **1.35 µs（45×）** |
| `dotnet test -c Release` 全量 | 43.0 / 49.8 s | **30.7 / 31.4 s** |
| `janpo shanten --batch` 3 万手 | 0.60 / 0.69 s | **0.29 / 0.30 s** |

**可照抄的原型**：`~/janpo-prototypes/searchstandard/Shanten.bnb.fs`（从易失的 `/tmp` 救出来的；
同目录还有 `Shanten.base.fs` 与另外六个变体，以及 `bench.fsx` / `hardest.fsx` / `nodes.fsx` / `extract-hands.py`）。
**但不要盲抄**——原型是在研究条件下写的，你要对着本仓库当前的 `Shanten.fs` 重做一遍并自己验。

## 验收（研究报告 §7 的原文，逐条照做）

- [ ] **只动一个函数**：`searchStandard`（含注释）。`standard` / `calculate` / `chiitoitsu` / `kokushi` / `deadQuadKinds` 与所有公开签名一字不动
- [ ] `let mutable` 预算仍是 **2**（`scripts/check-style.sh` 会挡）
- [ ] **注释必须改**：现有那句「它不参与剪枝，只在叶子取 min」改后即为假。新注释要写清（a）下界的两条上界怎么来，（b）`maxGain = 0 && hasHead` 里 **`hasHead` 不能省**的理由
- [ ] **与改动前的 DLL 逐手对拍 0 差异**（`standard` 与 `calculate` 两个值都比）：
      结构化手牌 ≥ 20 万手、均匀随机 ≥ 20 万手、**满张偏置 ≥ 10 万手**、**三麻牌种集合 ≥ 10 万手**
- [ ] `tests/.../fixtures/shanten-oracle.tsv` 4000 手 0 差异；`scripts/oracle/differential.sh 30000` 0 差异
- [ ] 全量测试绿（543 个），且**墙钟应从 43–50 s 降到 31 s 上下**——没降说明补丁没生效，这是免费自检
- [ ] `dotnet fantomas --check` 与 `scripts/check-style.sh` 干净
- [ ] 报告里带**交错跑的区间**，不是单值；基准输入用真实牌谱手牌，**不许用均匀随机手牌**

## 研究踩过的两个坑（别重踩）

1. **分层 JIT 会骗你**：快版本首轮 1.9 µs、稳态 0.39 µs。**预热 3 遍 + 每轮 10 遍**再取数
2. **剪枝的提前收敛分支漏掉 `hasHead` 守卫会错** —— 是「满张偏置」那 10 万手顶出来的，
   普通语料测不出。这就是验收里那 10 万手不能省的原因

## 不要在这张票里做的事

花色分解、预计算表、任何缓存、`skipEmpty` 之外的微优化——各自的否决理由在研究报告 §3–§6。
预计算表的 Fable 可行性**已经钉死**（能编能跑、6.08 MB / gzip 251 KB、node 与 .NET 校验和一致），
将来要捡随时能捡，但它只比剪枝再快 2× 却要构建产物 + 按规则集分表，现在不值。
