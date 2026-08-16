# M1 跑批调度表

调度器＝主 agent，每票委派给后台 subagent；subagent 完成时回简报，调度器集成后派下一波。
硬约束仍是 `RUNBOOK.md`（jj-only、不许问人、不许为变绿破坏测试、资源预算、park 流程），
下面只列 M1 的**增量**。

## M1 增量约束（覆盖 RUNBOOK 的对应条目）

1. **决策记 `DECISIONS.md`**（M0 那份的末尾，「## M1」段之后追加），格式照旧：票号、决定、
   被否决的选项、理由。需要人裁决的写「提案」，主人回来逐条审。
2. **JS 侧也要格式化与闸门**：F# 仍是 `dotnet fantomas .` + `scripts/check-style.sh`；
   新增的 TS/JS 由 19 号票选定并装好格式化器（建议 Biome 或 Prettier，选了记一条），
   之后每票提交前两侧都跑。
3. **引擎依赖白名单不许放宽**：`Thoth.Json.JavaScript` 这类 Fable 运行时后端属于 Web 工程，
   不得进 `Janpo.Engine.fsproj`。`ci.sh` 的那道闸门是硬的。
4. **不许为 Fable 分叉引擎逻辑**。真的必须分叉用 `#if FABLE_COMPILER`，并在 `DECISIONS.md`
   写清语义差异。
5. **网络安装有预算**：pnpm / dotnet tool 的安装走一次就好，别反复重装；单次下载 >200MB 停手。
6. **CI 不调真实 LLM API**。Agent 层测试一律用录制的响应。真实调用只在人工验收时手动跑，
   key 从 `/tmp/deepseek_key` 取（`deepseek-v4-flash`），**绝不写进代码、测试或提交**。

## 波次

| 波 | 票 | 工作区 | 并行理由 |
|---|---|---|---|
| W1 | 19 Fable 工具链与曳光弹 ∥ 20 决策包与 Observation | ws-a ∥ ws-b | 一个只碰 web/ 与构建，一个只碰引擎，零重叠 |
| W2 | 21 双目标黄金用例 ∥ 22 最小牌桌与播放控制 | ws-a ∥ ws-b | 一个碰测试固件与 CI，一个碰 Feliz 组件 |
| W3 | 23 LLM 座位闭环 | default | 一票穿四层，独占 |
| W4 | 24 Assisted 档脚手架 ∥ 26 DecisionRecord 与 Paifu 导出 | ws-a ∥ ws-b | **调度器改的班**：26 只阻塞于 23，不必等 24。23 号票的简报把两者的扩展点分得很清（24 改 `prompt.ts` 的 RENDERERS 与 `scaffold` 槽位，26 往响应加字段并在 `TablePage.settle` 组装记录），可以并行 |
| W5 | 25 Danger | ws-a | 阻塞于 24，独占 |
| W6 | 28 裁决落地 | ws-a | 三项裁决都碰 `Observation` / 决策包编解码 / 黄金用例，与 25、26 抢文件，故排在其后合成一票 |
| W7 | 29a 掩蔽事件流成为唯一投影 ∥ 30 自定义端点 | ws-a ∥ ws-b | 一个纯引擎、一个纯 web，零重叠 |
| W7.5 | 29b 可缓存 prompt | ws-a | 阻塞于 29a |
| W8 | 27 M1 验收 | ws-a | 收官 |

DAG（18 已 done）：19、20 无阻塞；21←19；22←19；23←18,20,22；24←20,23；25←24；26←23；27←21,24,25,26。

## 状态

| 票 | 状态 | change | 备注 |
|---|---|---|---|
| 18 | **done** | ornkrxst | 调度器亲自跑的 spike；结论：pi-ai 浏览器可用，不需要薄后端 |
| 19 | **done** | zttrrqru | ws-a；引擎零改动过 Fable，ADR-0005 落 UI 形态 B，格式化器取 Biome |
| 20 | **done** | kxvnlvum | ws-b；`MaskedSeat` 让他家暗牌在类型层面不存在；提案 20-A 待人裁 |
| 21 | **done** | xxmssluz | ws-a；39 条用例落成数据两侧读同一份，**双侧零差异**；共用跑法在 `src/Janpo.Golden` |
| 22 | **done** | rvwmxlzo | ws-b；上帝视角＝切换消费哪个投影；`Table.decide`/`apply` 两半分离，23 只需改前一半 |
| 23 | **done** | kwyryxrz | ws-a；兜底策略落在引擎 `Fallback.action`，Agent 层只回「交不出来」 |
| 24 | **done** | xkrnkzww | ws-a；`scaffold` 槽位变成引擎的 `Scaffold` 记录；包恒带脚手架，档位只决定 prompt 渲不渲染 |
| 25 | **done** | txxznwps | ws-a；Assisted 四项齐；无人立直也无人副露时整份危险度是 None |
| 26 | **done** | mwuloxvq | ws-b；Paifu 版本 1，thinking 是「值上的一次变换」可省；200 场回放逐条相同 |
| 28 | 待派 | | 裁决落地（21-c 逐字段 + 23-A Roster），阻塞于 25、26；PendingKan 与立直宣言牌已抽给 29 |
| 29a | **done** | unvvslns | ws-a；一条掩蔽法则 `MaskedEvent.forSeat`；`Observation.ofState` 签名未变 → 22/24/25 一行没改、黄金用例一字未动；增量 fold 0.56 ms | | 掩蔽事件流成为唯一投影、快照降为 fold 派生（expand–contract），阻塞于 28 |
| 29b | 派工中 | ws-a | 历史过接缝 + prompt 翻转 + 前缀稳定性 + 缓存指标 + 端口债 | | 前缀可缓存 prompt + **prompt 降为数据 + system 槽位/人格** + 缓存指标 + 牌谱只存尾部，阻塞于 29a |
| 32 | 派工中 | ws-b | 牌背隐形（22 号票逃逸缺陷）+ 重出 README 截图 |
| README | **done** | twporvny | ws-b；七条声称核到出处，六条核不实的删/写弱；发现牌背隐形 → 票 32 |
| 31 | 待派 | | prompt 降为数据 + system/人格 + 牌谱只存尾部 + 术语表，阻塞于 29b |
| 30 | **done** | nrkwlpuo | ws-b；**实测推翻了票面对 mixed content 的假设**（见下）；新开 `docs/host/` 面向主持人的手册 | | 自定义 baseUrl 接本地端点（含 CORS 与 mixed-content 实证），无阻塞 |
| 27 | 待派 | | 阻塞于 21、24、25、26、28、29a、29b、30、31 |

## 集成记录

- **W1 集成**（调度器）：20 号 rebase 到 19 号之上，唯一冲突是 `DECISIONS.md` 两侧同时追加，
  按「19 段在前、20 段在后」手工并进去。合并后 `./scripts/ci.sh` 全绿 47s，
  含浏览器内曳光弹对拍（种子 1177，两侧 scores / juni / kyokus 逐项相同）。

- **21 集成**（调度器）：无冲突，直接接在 W1 集成头之后；`./scripts/ci.sh` 全绿，
  含浏览器内黄金用例 39 条 / 190 字段 / 1437 行逐行对照。

- **22 集成**（调度器）：`DECISIONS.md` 文本冲突照旧手工并；另有一处**语义撞车**——
  22 新增的 `tehai_count` 打红了 21 钉死的决策包用例，按流程誊写用例并逐行核对后全绿。详见本文件同名段。

- **23 集成**（调度器）：无冲突，`./scripts/ci.sh` 全绿。

- **24 集成**（调度器）：无冲突，`./scripts/ci.sh` 全绿。26 号票仍在 ws-b 跑（基于 24 之前的头），
  落地时由调度器 rebase 到 25 之上。

- **26 集成**（调度器）：两处冲突。`DECISIONS.md` 照旧；`web/scripts/record-agent-fixtures.mjs`
  被 24 与 26 各自重构过——取 24 的结构（`record()` 助手 + `ask-assisted` 用例），
  补回 26 的语义增量（text-only 那条的 `thinking: null`）。合并后 CI 全绿，含无头下载验收。

- **25 集成**（调度器）：两处纯追加冲突（`DECISIONS.md`、`TablePage.fs` 各加一个 Msg 分支），
  用 `run/resolve-append-conflicts.py` 解——那个脚本只处理「两侧各自新增」，一见删改就报错交人，不许猜。
  合流后 `./scripts/ci.sh` 全绿（含牌谱导出下载与回放）。

- **28 集成**（调度器）：一处纯追加冲突（`DECISIONS.md`），脚本解。CI 全绿。
  28-1 的偏离（decoder 落在 `Janpo.Golden` 而非引擎）由调度器**接受**：它比票面写法更好，
  产品路径的单向边界没被破，且摊平的是已编好的 JSON，不可能与 encoder 漂移。

- **30 集成**（调度器）：无文本冲突。但**第一遍 CI 假红**——`web/scripts/verify-*.mjs` 写死端口且
  `strictPort: true`，与 ws-a 里 29a 的 agent 同时跑 CI 撞了端口，报错长得像用例挂了。第二遍全绿。
  修端口的活并进票 29b 第三之三节。**教训：并行跑批时的红要先怀疑基础设施，再怀疑代码。**

- **29a 集成**（调度器）：一处纯追加冲突，脚本解，CI 全绿 75s。
  **换供给方的预测兑现了**：`Observation` 类型与 `ofState` 签名都没变，24/25/22 一行未改、黄金用例一字未动。
  29a 纠正了票面一处麻将常识错误（暗杠不该掩，见 29a-2）。

## 主人回来先看哪里

1. 这张表 —— 谁 done、谁 parked
2. `DECISIONS.md` 的「## M1」段 —— 替你做的决定与待裁的提案
3. `reports/` —— 每票的完整报告
4. `jj log` —— 一票一 commit
