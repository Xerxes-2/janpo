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
| W4 | 24 Assisted 档脚手架 | default | 接 23 的 Agent 层，独占 |
| W5 | 25 Danger ∥ 26 DecisionRecord 与 Paifu 导出 | ws-a ∥ ws-b | 一个是引擎新模块，一个是牌谱类型与导出；**两票都会碰 Agent 层的决策模块，各自小心** |
| W6 | 27 M1 验收 | default | 收官 |

DAG（18 已 done）：19、20 无阻塞；21←19；22←19；23←18,20,22；24←20,23；25←24；26←23；27←21,24,25,26。

## 状态

| 票 | 状态 | change | 备注 |
|---|---|---|---|
| 18 | **done** | ornkrxst | 调度器亲自跑的 spike；结论：pi-ai 浏览器可用，不需要薄后端 |
| 19 | 派工中 | | ws-a |
| 20 | 派工中 | | ws-b |
| 21 | 待派 | | |
| 22 | 待派 | | |
| 23 | 待派 | | |
| 24 | 待派 | | |
| 25 | 待派 | | |
| 26 | 待派 | | |
| 27 | 待派 | | |

## 主人回来先看哪里

1. 这张表 —— 谁 done、谁 parked
2. `DECISIONS.md` 的「## M1」段 —— 替你做的决定与待裁的提案
3. `reports/` —— 每票的完整报告
4. `jj log` —— 一票一 commit
