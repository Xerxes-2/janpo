# janpo

LLM 日麻对战平台 —— F# 规则引擎（Fable → JS，浏览器内运行）+ TypeScript Agent 层与 UI。
公开仓库 `https://github.com/Xerxes-2/janpo`，站点 `https://xerxes-2.github.io/janpo/`；
`main` 有远端 `origin`，远端跑着 CI 与 Pages。

**这份文件是地图**：人人适用的硬约束写在这里，其余一律指路——细节只在被指的那份文件里存一份。

## 硬约束（不分角色，每票都适用）

1. **版本控制只用 jj。** 这是 colocated 仓库（`.jj/` 与 `.git/` 并存），git 命令会搞乱 op log；
   用法见 `/home/xerxes2/.pi/agent/skills/jj-guide/SKILL.md`。**远端操作（`jj git push`、
   动 `main` 书签）只由调度器做**，干票的人提交到自己的 change 为止。
2. **`./scripts/ci.sh` 全绿是唯一的验收判据**——格式化、风格闸门、dotnet 测试、浏览器侧闸门都在它里面。
3. **测试只许往更硬的方向改。** 删测试、加 skip、放宽断言、改期望值迎合实现，一律不许；
   做不到就 park 并报上来（park 的写法见 `docs/agents/workbook.md`，案例见判据 5）。
4. **API key 绝不进代码、测试、fixture 与提交**，CI 里不许调真实 provider。
   key 只活在浏览器 `localStorage` 与人手里的环境变量。
5. **`CONTEXT.md` 是术语的唯一权威**：标识符用它定义的罗马字日麻术语；**改它要有单票授权**，
   没授权就把提案追加到 `.scratch/llm-riichi-arena/run/DECISIONS.md` 等人裁。
6. **干票时只动自己票里的文件**：别人的票、别人的工作区、`docs/adr/*` 都不碰，有异议同样写进 `DECISIONS.md`。

## 哪份文件管哪件事

| 你要做的事                                                              | 去读                                                          |
| ----------------------------------------------------------------------- | ------------------------------------------------------------- |
| 接一张票干活：身份、必读顺序、自主决策、park、交付物、资源预算          | `docs/agents/workbook.md`                                     |
| 验证引擎行为（对拍、扫语料、找反例）：用 `dotnet fsi`，不许移植         | `docs/agents/workbook.md` 末节 + `scripts/fsi/README.md`      |
| 判断「下次遇到同类情形该怎么判」：18 条判据，每条附一个真实案例         | `docs/agents/judgments.md`                                    |
| 当调度器：写票、编波、派工单骨架、集成、记账                            | `docs/agents/dispatch.md`                                     |
| 写 F#：嵌套应用怎么拆，以及三种**不许**强行管道的情况                   | `docs/agents/fsharp-style.md`                                 |
| 票与 spec 放在哪、文件怎么命名                                            | `docs/agents/issue-tracker.md`                                |
| `**Status:**` 那行能写哪五个值                                          | `docs/agents/triage-labels.md`                                |
| 领域词与既有决策怎么消费                                                | `docs/agents/domain.md` → `CONTEXT.md` + `docs/adr/`          |
| 这一批跑批特有的：排班、当次机器上限、里程碑话术                        | `.scratch/llm-riichi-arena/run/RUNBOOK.md`                    |
| 某个决定当初为什么那么定、某票具体做了什么                              | `.scratch/llm-riichi-arena/run/DECISIONS.md` 与 `run/reports/` |
