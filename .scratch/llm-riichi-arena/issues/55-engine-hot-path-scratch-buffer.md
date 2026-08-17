# 55 — 引擎热路径：调用方持有暂存缓冲（B 级 + A 级顺扫）

**What to build:** 把向听/有效牌那条热路径上「每次调用都新建 34 长数组」这件事去掉——
让**调用方**持有一份暂存缓冲，批内共用。这不是为了让页面变快（页面本来够用：一次决策 0.5–2 ms，
夹在 1–30 秒的模型调用之间），而是因为**它同时是 CI 变慢的主因**：属性测试反复跑
`Scaffold` / `Danger` / `Fallback` / `Observation`，而这些每次决策要调 ≈397 次 `Shanten.calculate`、
新建 ≈772 个 34 长数组。

**Blocked by:** None

**Status:** ready-for-agent

## 依据（都在仓库里，别重新调研）

`docs/research/engine-perf-caller-and-browser.md`（**已并进 main**）第 4.1 / 4.2 节给了具体改法与预估：

| | 现状 | B 级之后（该文的外推） |
|---|---|---|
| 每决策 34 长数组新建 | ≈ 772 | ≈ 3 |
| .NET 上这一项 | ≈ 70 µs | ≈ 0.3 µs |
| 浏览器上这一项（`Int32Array`） | ≈ 0.7 ms | ≈ 13 µs |

调度器本轮实测的 CI 侧数字（本机 32 核）：`dotnet test` **墙钟 51.4s / CPU 总和 394s / 717 个用例**，
最慢十五条全是属性测试（`SoakTests` 20.2s、`RyuukyokuProperties` 18.9s、`FallbackTests` 9.2s、
`ObservationProperties` 8.4s…）。**远端只有 4 核**，所以远端时间由 CPU 总量决定，不由并行度决定。

## 要做的

- [ ] **B 级**：`Shanten.calculateWith (scratch: ShantenScratch) kindSet hand` 这类形状，
      缓冲由 `Scaffold.calculate` / `Ukeire.calculate` / `tenpaiDahai` 在**进入批之前**建一个，批内共用。
      **公开的纯函数签名保留**（库外调用者照旧），纯度与并发不变（缓冲是显式入参，不是全局状态）
- [ ] 同一个口子把 `HandShape.add` / `remove` 的**全量重校验**在批内免掉——
      批内的试打/试摸在构造上不可能违反张数与 4 张上限（研究文档 §4.2）
- [ ] **A 级顺扫**（研究文档 §4.1，单独不值一票，与 B 一起做）：
      `chiitoitsu`/`kokushi`/`deadQuadKinds` 四遍扫描融合成一遍；`Ukeire.calculate` 收一个
      「已知的当前向听」去掉那次重算；`TileKindSet.kinds` 存一份；`canJoinRun` 里的 `[ a .. b ]` 换 for
- [ ] **不做 C 级与 D 级**（段级缓存、增量状态）。它们要动数据结构与语义边界，另立项

## 怎么证明没改语义（这是这一票的真验收）

- [ ] `./scripts/ci.sh` 全绿：黄金用例 40 条 / 2069 字段两侧逐行相同、**真牌谱对拍零差异**、
      双目标对拍逐字相同、全部属性测试绿
- [ ] **改前改后各跑一次 soak，事件流逐条比对**（同种子必须逐字相同）——这是最硬的那道
- [ ] 若某处改动导致数值不同（哪怕只差一个符），**停下来**：那就不是「优化」而是行为变更

## 怎么证明真变快了（数字要能复现）

- [ ] `dotnet test` 的**墙钟与 CPU 总和**（用 `--logger trx` 后统计 duration 之和）改前 / 改后各一份
- [ ] 每决策的 34 长数组新建次数：改前 ≈772，改后应 ≈3（**给出你怎么数的**）
- [ ] **浏览器侧也量一次**（研究文档说那边收益大一个数量级，且那一侧是产品实际形态）：
      同一份决策包在 node 里跑 N 次的耗时，改前 / 改后
- [ ] 报告里写清：这些数字**分别是在几核机器上量的**（远端 4 核、本机 32 核，别混）

## 边界

- [ ] **不碰 `tests/Janpo.Engine.Tests/GameStateGenerators.fs` 与其余生成器**——票 56 正在改那里
      （它要修的是「每取一个样本把九条轨迹全跑完」的浪费）。你只改 `src/Janpo.Engine/`
- [ ] 不碰 `web/`、不碰 workflow、不碰牌谱格式
- [ ] **不许为了变快牺牲任何闸门或降低属性测试的用例数**——那是票 56 的地盘，且降数不是提速
