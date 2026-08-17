# 58 — 研究：`Observation.ofState` 那一段的真实成本，与最便宜的做法

**What to build:** 一份研究文档 + 一份可证伪的实验清单。**不动 `src/`**（这是研究票；
真要改由后续实现票做）。这是「引擎性能」长期课题的**第 5 轮**，登记册在
`docs/research/engine-performance-track.md`。

**Blocked by:** None

**Status:** ready-for-agent

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

## 要回答的问题（每条都要给数字或明确的「量不出来，因为…」）

- [ ] **`Observation.ofState` 的成本分布**：掩蔽（`MaskedEvent.forSeat` 逐条）、fold（`absorb` 逐条）、
      还是末尾的 `observation` 组装？一局第 5 手与第 60 手各多少？（O(n²) 的斜率要看得见）
- [ ] **一整局里它被调多少次**：每次决策一次？四家都调？响应阶段每家各一次？
      （**判据：先问一趟做几次**）
- [ ] **换成「调用方持有 `SeatStream`」要动什么**：`DecisionPackage.forSeat` 的签名是
      `Seat -> GameState -> DecisionPackage option`（纯函数，20 号票定的边界）。
      改成收一份已维护的流，**谁来持有它**？浏览器侧 `Table.fs` 已经有了；
      CLI（`janpo decide` / `soak`）与属性测试呢？**列出每个调用点与它的代价**
- [ ] **纯度与边界**：20 号票立的「`GameState` 永不越过边界、决策包是单向的」不许破。
      增量状态是**可变的还是不可变的**？不可变（每步产生新 `SeatStream`）是否够快？
      （票 55 的先例：缓冲**是显式入参、不是全局状态**，因为属性测试开着 `Parallelism`）
- [ ] **一致性怎么守**：增量维护与从头 fold 必须永远给出同一份 `Observation`——
      29a 用一条**迁移闸门属性测试**证明过（两种实现逐字段相等）。
      那条测试**现在还在吗**？若已退役，重新用上它是这一轮最便宜的保险
- [ ] **CI 与语料的收益**：这条路对 `dotnet test` 的 CPU 总量、以及票 57 那种**扩大牌谱语料**
      的可行性各值多少？（扩语料是主人明确要的方向，O(n²) 是它的天花板之一）
- [ ] **C 级（段级缓存）顺带估一下**：研究文档说花色段键在一次 `Ukeire` 内命中率约 75%。
      与本轮这条路比，**哪条更便宜、能不能同时做**？只要估算与判据，不要实现

## 交付

- [ ] `docs/research/observation-cost-and-incremental-seat-stream.md`：分布数字、调用次数、
      每个调用点的改造代价、**推荐做法与被否决的做法及理由**
- [ ] **可证伪的实验清单**：每条写清「怎么算它被否掉」（照第 3 轮那份文档的体例）
- [ ] 往 `docs/research/engine-performance-track.md` 的表里加一行（第 5 轮），
      并更新「未答」一节（**答掉的删掉、新出现的写上**）
- [ ] 一张**建议的实现票草案**（写进报告最后一节，别自己建票文件——票号由调度器分配）

## 边界

- [ ] **`src/` 一行不许改。** 要跑实验就在 `/tmp` 下的树副本里改（票 55 数数组就是这么做的）
- [ ] 不碰 `tests/fixtures/paifu/`、`PaifuDifferential.fs`、`scripts/paifu/`——票 57 正在那边扩语料
- [ ] 不碰 `web/`、不碰 workflow、不碰术语表
- [ ] **`.NET` 与浏览器两侧都要给数字**（浏览器是产品的实际形态；node 跑 Fable 产物即可）
- [ ] 转引旧数字必须**逐条标注出处**，别把别轮的数字当自己量的（第 3 轮那份文档做到了，照它）
