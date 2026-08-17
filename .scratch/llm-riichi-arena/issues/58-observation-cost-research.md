# 58 — 研究：`Observation.ofState` 那一段的真实成本，与最便宜的做法

**What to build:** 一份研究文档 + 一份可证伪的实验清单。**不动 `src/`**（这是研究票；
真要改由后续实现票做）。这是「引擎性能」长期课题的**第 5 轮**，登记册在
`docs/research/engine-performance-track.md`。

**Blocked by:** None

**Status:** ready-for-human

**交付**：`docs/research/observation-cost-and-incremental-seat-stream.md`（主体）、
`docs/research/engine-performance-track.md`（第 5 轮那一行 + 「未答」重写）、
`run/reports/58-observation-cost-research.md`（怎么量的 / 第 3 轮的预估 / 实现票草案）、
`DECISIONS.md` 的 `## 58` 段。**`src/` 与 `tests/` 一行未改。**

## 入口事实（调度器已核实，别重新发现）

- 票 55 实测：**`Observation.ofState` 是每决策最大的一段**；`legalActions` 只 0.1 µs、**不是热点**
- `DecisionPackage.forSeat` 调 `Observation.ofState seat state`，而它内部是
  **「掩蔽 + 从头 fold 整条流」**（`Observation.fs` 的 `ofMasked` / `ofEvents`）——
  **每次决策 O(n)，一局下来 O(n²)**
- **票 29a 已经造好增量维护**：`SeatStream.start / advance / absorb / observation / events`，
  **牌桌在用**（`Table.fs` 每座位维护一份；29a 报告实测：增量 0.56 ms vs 重头 fold 29 ms）

**所以这一轮要回答的不是「要不要造增量状态」，而是：决策路径为什么没用上已经有的那份，
以及让它用上要付什么代价。** 研究文档第 3 轮把这个方向叫「D 级」并预估成大工程——
**那个预估可能过时了**，因为 29a 已经把大半造好了。**先证实或推翻这一点。**

> **干完之后的批注（上面那段有一处不准）**：逐字核过第 3 轮 §4.4 之后发现，
> 它的「D 级」指的是「把 34 长牌种计数常驻进 `PlayerState`」，**不是观测的 fold**
> （同一份文档 §7 自己写着「`Observation` 一行没读性能」）。
> 对它说的那件事，它的预估仍然成立；被推翻的是**把它套到观测头上的读法**。
> 详见研究文档 §4 与 `DECISIONS.md` 58-1。

## 要回答的问题（每条都要给数字或明确的「量不出来，因为…」）

- [x] **`Observation.ofState` 的成本分布**：掩蔽（`MaskedEvent.forSeat` 逐条）、fold（`absorb` 逐条）、
      还是末尾的 `observation` 组装？一局第 5 手与第 60 手各多少？（O(n²) 的斜率要看得见）
      → **fold 99.3%（.NET）/ 99.8%（浏览器）**；掩蔽 1.6 / 2.8 µs，组装 0.3 / 0.07 µs；
      斜率按 n 分七桶（研究文档 §2.1 / §2.2）：**单次是线性的**，O(n²) 是每手重来攒的
- [x] **一整局里它被调多少次**：每次决策一次？四家都调？响应阶段每家各一次？
      （**判据：先问一趟做几次**）
      → **88.6 次/局**（均匀随机 40 局；有主见 76.9），**只给被问那一家建包**，
      响应阶段每家仍是各自一步被问到（同时等 >1 家的步数 0.5/局）；Σn ≈ 6703（§3）
- [x] **换成「调用方持有 `SeatStream`」要动什么**：`DecisionPackage.forSeat` 的签名是
      `Seat -> GameState -> DecisionPackage option`（纯函数，20 号票定的边界）。
      改成收一份已维护的流，**谁来持有它**？浏览器侧 `Table.fs` 已经有了；
      CLI（`janpo decide` / `soak`）与属性测试呢？**列出每个调用点与它的代价**
      → **十个调用点逐个列了**（§5）：只有三个在「每手 / 每帧」粒度上（`Table.decide` ≈1 行、
      `TablePage.dangerPanels` ≈3 行、CLI `decideSequence` ≈4 行）；`soak` **压根不建决策包**，
      其余七个（CLI 单次 decide、黄金用例、五个测试模块）都是一趟一次，**一行不动**
- [x] **纯度与边界**：20 号票立的「`GameState` 永不越过边界、决策包是单向的」不许破。
      增量状态是**可变的还是不可变的**？不可变（每步产生新 `SeatStream`）是否够快？
      （票 55 的先例：缓冲**是显式入参、不是全局状态**，因为属性测试开着 `Parallelism`）
      → **这一格不必付钱**（§7）：`SeatStream` 本来就是不可变记录、构造子 `private`、
      `advance` 是纯函数且 **O(1)**（实测），无 module-level 可变状态；`SeatStream` 不上 wire
- [x] **一致性怎么守**：增量维护与从头 fold 必须永远给出同一份 `Observation`——
      29a 用一条**迁移闸门属性测试**证明过（两种实现逐字段相等）。
      那条测试**现在还在吗**？若已退役，重新用上它是这一轮最便宜的保险
      → **`MigrationGate.fs` 已退役（文件不在仓库里）**；接手的三道逐道验了，
      **两次反向自证（红的输出在研究文档 §8.2）**：`ObservationProperties` 两条是真的；
      **`MaskedStreamProperties.incrementalAgrees` 是恒真式、永远红不了**；
      真在守这件事的只有 Web 侧 `TableTests` 那一条（一颗种子）。实现票要立的三道在 §8.4
- [x] **CI 与语料的收益**：这条路对 `dotnet test` 的 CPU 总量、以及票 57 那种**扩大牌谱语料**
      的可行性各值多少？（扩语料是主人明确要的方向，O(n²) 是它的天花板之一）
      → **两条都量掉了**（§9）：CI **≈ 0.33%**（全量 `dotnet test` 只调 `ofState` 5713 次、
      共 fold 362 914 条事件≈ 1.3 s CPU，分母是票 55 实测的 403.7 s）；
      扩语料 **0**（`Replay` / `PaifuDifferential` 那条路一次观测都不建）。
      **「O(n²) 是扩语料天花板」这句不成立，已从登记册删掉。**
- [x] **C 级（段级缓存）顺带估一下**：研究文档说花色段键在一次 `Ukeire` 内命中率约 75%。
      与本轮这条路比，**哪条更便宜、能不能同时做**？只要估算与判据，不要实现
      → **C 级 1.20×（乐观上限，外推）vs 本轮这条 2.20×（实测）**；**两者正交、可以同时做**；
      建议先做本轮这条（做完之后 `Scaffold` 占 88%，C 级的靶子更干净），C 级仍按第 3 轮说的先立测量票（§11）

## 交付

- [x] `docs/research/observation-cost-and-incremental-seat-stream.md`：分布数字、调用次数、
      每个调用点的改造代价、**推荐做法与被否决的做法及理由**
- [x] **可证伪的实验清单**：每条写清「怎么算它被否掉」（照第 3 轮那份文档的体例）
      → 八条（§12）：四条本轮已做、四条留给实现票
- [x] 往 `docs/research/engine-performance-track.md` 的表里加一行（第 5 轮），
      并更新「未答」一节（**答掉的删掉、新出现的写上**）
- [x] 一张**建议的实现票草案**（写进报告最后一节，别自己建票文件——票号由调度器分配）
      → 研究文档 §15 + 报告 §4，**没建任何票文件**

## 边界

- [x] **`src/` 一行不许改。** 要跑实验就在 `/tmp` 下的树副本里改（票 55 数数组就是这么做的）
      → `src/` 与 `tests/` 一行未改；两次反向自证与调用计数在 `/tmp/janpo-58/tree`；
      Fable 产物输出到 `/tmp/janpo-58/js`，全程 `jj st` 干净
- [x] 不碰 `tests/fixtures/paifu/`、`PaifuDifferential.fs`、`scripts/paifu/`——票 57 正在那边扩语料
      （只 `grep` 读过 `PaifuDifferential.fs` / `Replay.fs` 确认它们不建观测，一个字没改）
- [x] 不碰 `web/`、不碰 workflow、不碰术语表
- [x] **`.NET` 与浏览器两侧都要给数字**（浏览器是产品的实际形态；node 跑 Fable 产物即可）
      → 每一张主表都是两侧并排
- [x] 转引旧数字必须**逐条标注出处**，别把别轮的数字当自己量的（第 3 轮那份文档做到了，照它）
      → 研究文档 §10 是一张出处表，十条，逐条标注「本轮有没有重量」
