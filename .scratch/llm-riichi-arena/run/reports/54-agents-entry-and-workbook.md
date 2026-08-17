# 54 — AGENTS.md 收成地图，RUNBOOK 的耐用一半提进 `docs/agents/workbook.md`

**只动文档，零代码改动。** `./scripts/ci.sh` 全绿。

改了 5 个文件：

| 文件                                          | 改动                                      | 行数     |
| --------------------------------------------- | ----------------------------------------- | -------- |
| `AGENTS.md`                                   | 重写成地图（一句话 + 6 条硬约束 + 指路表） | 36 → **36** |
| `docs/agents/workbook.md`                     | **新增**：RUNBOOK 耐用的那一半            | 97       |
| `.scratch/llm-riichi-arena/run/RUNBOOK.md`    | 收成「当次须知」，不再自称 M0             | 92 → 36  |
| `.scratch/llm-riichi-arena/run/M2-SCHEDULE.md` | 一行指路修正（末段指着 RUNBOOK 的硬约束）  | −1 +2    |
| `.scratch/llm-riichi-arena/issues/54-*.md`    | 勾框 + `Status: ready-for-human` + 自查答案 | —        |

## 1. 那处硬错，以及 AGENTS.md 每一句的核对

**「There is no remote」是假的**，已删。现在写的是：公开仓库 `https://github.com/Xerxes-2/janpo`、
站点 `https://xerxes-2.github.io/janpo/`、`main` 有远端 `origin` 且远端跑着 CI 与 Pages。
核对方式：`jj git remote list` → `origin https://github.com/Xerxes-2/janpo.git`；
`jj bookmark list --all-remotes` → `main` 的 `@origin` 与本地同一个 commit；
`.github/workflows/` 下 `ci.yml` 与 `pages.yml` 都在。

顺手核了旧 AGENTS.md 的其余每一句（判据 15「哪些地方描述过它」）：

| 旧句子                                             | 核对结果                                                             |
| -------------------------------------------------- | -------------------------------------------------------------------- |
| 一句话简介（F# 引擎 + Fable + TS Agent 层与 UI）    | 真，保留                                                             |
| colocated `jj` repo，git 会搞乱状态                 | 真（`.jj/` + `.git/` 都在），保留并补「远端操作只由调度器做」          |
| **There is no remote**                              | **假**，改掉                                                          |
| F# 风格那三行（规则 1 的例子 + 「第 4 条列了三种」） | 规则编号仍对得上，但整段是 `fsharp-style.md` 的副本 → 收成一行指路     |
| issue tracker / triage labels / domain 三段          | 内容仍真，但都是被指文件里已有的话 → 收成指路表三行                    |
| 「验证引擎行为用 `dotnet fsi`」整段                  | 真，但按票面归 `workbook.md` → 这里只留一行指路                        |

## 2. RUNBOOK 一分为二：逐条归属

| 旧 RUNBOOK 的段落                                     | 去处                                            | 判据                                            |
| ------------------------------------------------------ | ----------------------------------------------- | ----------------------------------------------- |
| 开场「这份文件是每个 implement subagent 的硬约束」      | 两份各留一句**指向对方**                         | —                                               |
| 你的身份（一张票、没人可问、自己验证自己提交）          | `workbook.md`                                   | 耐用                                            |
| 约束 1 只用 jj（+ jj-guide 路径、禁远端与 `-i`）        | **`AGENTS.md` 硬约束 1**                         | 人人适用，不止接票的人                           |
| 约束 2 只在分配给你的工作区里干活                       | `workbook.md`「你的身份」                        | 只对接票的人成立                                 |
| 约束 3 不许问人                                         | `workbook.md`「你的身份」                        | 同上                                            |
| 约束 4 不许为变绿破坏测试                               | **`AGENTS.md` 硬约束 3**                         | 人人适用；案例指判据 5，不复述                    |
| 约束 5 提交前跑 `dotnet fantomas .`                     | `workbook.md`「流程」                            | 是干活的一步；「01 票负责装好」是 M0 话术，删      |
| 约束 5 的「CI 有 `--check` 关卡」                       | 并进 **`AGENTS.md` 硬约束 2**（`ci.sh` 是唯一判据） | 同一件事的两种说法                               |
| 约束 6 不许改 `CONTEXT.md` / ADR / 别人的票             | **`AGENTS.md` 硬约束 5 + 6**                     | 人人适用                                        |
| 约束 7 不在工具链上过夜                                 | `workbook.md`「流程」末                          | 耐用；`dotnet 10.0.111` 这个会过期的版本号删掉     |
| 必读 1 你的票 / 4 spec / 6 judgments                     | `workbook.md`「必读」                            | 耐用；顺序对齐 `dispatch.md` §3（judgments 在前） |
| 必读 2 `CONTEXT.md`「标识符唯一权威」                    | 条目留 `workbook.md`；**规则**上提 `AGENTS.md` 5   | 规则人人适用，读的顺序只对接票的人有用            |
| 必读 3 「`docs/adr/` 0001 0002 0003」                    | `workbook.md`，改成「与你这票相关的那几条」        | 写死三条已过期（现有 0001–0005）                 |
| 必读 5 「`docs/agents/*.md`」                            | **删**                                          | `AGENTS.md` 的指路表就是这件事                   |
| 流程（implement/tdd/code-review 两轴、blocking 修一轮）  | `workbook.md`                                   | 耐用                                            |
| 自主决策（三步 + 最保守那一种）                          | `workbook.md`                                   | 耐用                                            |
| Park（三步）                                            | `workbook.md`                                   | 耐用                                            |
| 交付物 1–4                                              | `workbook.md`                                   | 耐用；「简报 ≤15 行」改成「上限由派工单给」        |
| 资源预算的**判据**（显式限并行、`nice -n 19`、先估规模）+ 2026-08-16 那次事故 | `workbook.md`                | 耐用，且带真实案例                               |
| 资源预算的**数字**（并行 ≤4 进程、下载 ~200 MB、宿主机 dotnet） | **留 `RUNBOOK.md`**                        | 当次机器特有                                    |
| 资源预算里的「这台机器 16 核」                           | **删**                                          | 本机实为 32 核 / 91 G，且 `dispatch.md` 已记一份 |
| 「验证引擎行为用 `dotnet fsi`，不许移植」整节            | `workbook.md` 末节                               | 票面点名的耐用条                                 |
| F# 风格那一节（抄了 `fsharp-style.md` 的规则 2）         | **删**                                          | 真源是 `fsharp-style.md`，`AGENTS.md` 指路       |
| （新增）当次排班指向 `M2-SCHEDULE.md`、M2 一行           | `RUNBOOK.md`                                    | 当次特有                                        |
| （新增）里程碑话术：M0/M1 已收官，别再用 M0 说法          | `RUNBOOK.md`                                    | 票面要求「标题不再自称 M0」的落地                 |
| （新增）老票据里的「RUNBOOK 第 N 条」怎么对上现在的位置   | `RUNBOOK.md`                                    | 老票/老报告有 5 处这样的引用，编号没了要有兜底     |

两份互指：`RUNBOOK.md` 第一句「先读 `docs/agents/workbook.md`」；`workbook.md` 开头指
`RUNBOOK.md` 是「派工单必读的第一项」。`.scratch` 那份仍有实体内容（排班入口、当次上限、
里程碑话术、老编号对照），没被掏空。

## 3. 重复点：查过 12 处，删/合并 9 处，判为指路 3 处

查法：把四份文件（AGENTS / judgments / workbook / dispatch）+ 旧 RUNBOOK 的每条规则列出来，
按「同一条规则在哪几处出现」对齐。

| # | 重复点                                       | 出现在                                                       | 处置                                                                                 |
| - | -------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------------------------------ |
| 1 | 只用 jj / colocated / 禁远端                  | 旧 `AGENTS.md`「Version control」+ 旧 RUNBOOK 约束 1          | **合成一处**（`AGENTS.md` 硬约束 1）；`workbook.md` 只说「硬约束在 `AGENTS.md`」        |
| 2 | 不许为变绿破坏测试（删测试/skip/放宽/改期望）  | 旧 RUNBOOK 约束 4 + `judgments.md` 判据 5                     | `AGENTS.md` 留**一行规则**并写「案例见判据 5」；案例与来龙去脉只在 judgments 一处        |
| 3 | 不许改 `CONTEXT.md` / ADR / 别人的票           | 旧 RUNBOOK 约束 6（`AGENTS.md` 原本没有）                     | 上提 `AGENTS.md` 硬约束 5 + 6，`workbook.md` 不再写                                    |
| 4 | 用 `dotnet fsi` 验引擎、不许移植                | 旧 `AGENTS.md` 一段 + 旧 RUNBOOK 一整节 + `scripts/fsi/README.md` | 论证与实测数字只留 `workbook.md` 一份；`AGENTS.md` 一行指路；`fsi/README.md` 保持「怎么用」（未动） |
| 5 | F# 风格的例子                                  | 旧 `AGENTS.md` 抄规则 1、旧 RUNBOOK 抄规则 2、`fsharp-style.md` 是真源 | 两处抄写**都删**，只在 `AGENTS.md` 指路表留一行                                        |
| 6 | 机器规格与并行路数                             | 旧 RUNBOOK「16 核」+ `dispatch.md`「上限四路（32 核/91 G）」   | RUNBOOK 不再写机器规格与路数（且旧数字是错的），只写 agent 自己实验的上限；编波指 dispatch |
| 7 | 必读顺序                                       | `dispatch.md` §3.3（派工单骨架的一行）+ `workbook.md`「必读」  | **判为指路**：dispatch 那行是调度器写派工单的 checklist，展开只在 workbook；已把 workbook 的顺序对齐成 dispatch 那行的顺序，避免两处打架 |
| 8 | 交付物五件                                     | `dispatch.md` §3.6（一行）+ `workbook.md`「交付物」            | **判为指路**：同上；`workbook.md` 写明「以这一节为准，派工单可加码但不矛盾」              |
| 9 | 简报格式（首行 `STATUS:`、次行 `CHANGE:`、写接口事实、行数上限） | `dispatch.md` §3.6/3.7 + 旧 RUNBOOK 交付物 4 | **判为指路**；行数上限两处都不再写死（旧 RUNBOOK 的「≤15 行」改成「由派工单给」），免得与派工单的数字冲突 |

后三处是 **Standards 轴自查时在我自己的草稿里抓到的**（「提升」变「复制」的现行犯），当场改掉：

| # | 重复点                         | 出现在                                             | 处置                                                        |
| - | ---------------------------- | -------------------------------------------------- | ----------------------------------------------------------- |
| 10 | 「宿主机 dotnet 可直接用」      | 我草稿的 `workbook.md`「流程」+ `RUNBOOK.md`「机器上限」 | workbook 只留规则（超 10 分钟就退回），**哪个 dotnet 只在 RUNBOOK 一处** |
| 11 | spec 的 M2 一行（四 LLM 同桌…） | 我草稿的 `RUNBOOK.md` + `M2-SCHEDULE.md` 第 3 行      | RUNBOOK 删掉，只留一行「这一批要做什么看 M2-SCHEDULE」      |
| 12 | 「活跃 feature 是 `llm-riichi-arena`」 | 我草稿的 `AGENTS.md` 指路表 + `issue-tracker.md`     | `AGENTS.md` 删掉那个括号（表里的路径已经显着写着它）        |

**没有做的重复消解（越界，只报不改，留给调度器）：**

- **`docs/agents/issue-tracker.md` 第 5 行也写着「This repo has no git remote」**——与本票修掉的那处
  同源、同样是自动被读到的路径上的假话。该文件在票面禁改名单里，一个字没动。
  建议：那半句删掉即可（jj-only 那半句是真的）。
- **`docs/agents/judgments.md` 末节「不在这份清单里的」**说硬约束（只用 jj、资源预算、fsi 验引擎）
  在 `.scratch/…/RUNBOOK.md` 与 `AGENTS.md`——**资源预算与 fsi 这两条现在在 `workbook.md`**，
  那行指路被本票拆过期了（判据 15 的同形）。禁改文件，一个字没动。建议：那一句加上 `workbook.md`。
- 老报告（`README-发布准备.md` 等）里指着 RUNBOOK 的硬约束表：历史日志，不改；
  `RUNBOOK.md` 新加的「老票据里的『RUNBOOK 第 N 条』」一节兜住了这类引用。

**做了的一处**：`M2-SCHEDULE.md` 末段「清单不覆盖的两样……**`RUNBOOK.md` 的硬约束**（工作区、
只用 jj、不许问人、资源预算、park 流程）」——这几样已分到 `AGENTS.md` 与 `workbook.md`，
指路当场过期，改了这一行（run 目录里的文件，属本票射程）。

## 4. 自查（票面第四节）

**Q1：一个刚被派来的 agent，只读 `AGENTS.md` + 派工单，知不知道该去读哪几份文件？**

知道。`AGENTS.md` 的指路表是按**「你要做的事」**检索的，接票干活那一行直接命中 `workbook.md`，
而 `workbook.md` 里有编好号的「必读」。我按新 `AGENTS.md` 模拟了四种接票情形，每种都能一跳到位：

| 派来干的事                   | 从 AGENTS.md 走到哪                                            |
| ---------------------------- | -------------------------------------------------------------- |
| 实现一张功能票               | `workbook.md`（身份/必读/流程/park/交付物）→ 它的必读第 1 项 `judgments.md` |
| 要写 F#                      | `fsharp-style.md`（且硬约束 2 告诉他 `ci.sh` 里有格式与风格闸门）  |
| 要扫语料 / 对拍引擎          | `workbook.md` 末节 + `scripts/fsi/README.md`                     |
| 想知道某个词该怎么拼、能不能改 | 硬约束 5 →`CONTEXT.md`；改它要单票授权，否则写 `DECISIONS.md`      |

剩一个已知缺口：**派工单本身的路径不在 `AGENTS.md` 里**——派工单由调度器直接给进上下文，
不是靠 `AGENTS.md` 找到的，所以没写。

**Q2：四份文件里有没有同一条规则出现两次？** 查了 12 处：**9 处合成一处**（其中 3 处是我自己草稿
里新写出来的重复，Standards 轴自查时抓到），**3 处判为「一行 checklist 指向一处展开」**而非复制。
逐条见 §3 两张表。另有两处在禁改文件里（`issue-tracker.md` 的 no-remote、`judgments.md` 末节的过期指路），
只报不改。

**Q3：AGENTS.md 现在多少行？** **36 行**（`wc -l`；非空行 30），其中指路表 10 行、硬约束 6 条 12 行。
改前也是 36 行，换掉的是**里面装的东西**：旧版 22 行非空行里 14 行是别处已有的内容副本。
预算 60 行还剩 24 行——这份文件每个 agent 每次都读，剩下的额度留给以后真的人人适用的硬约束，
不给内容。

## 5. review（Standards 轴）

按 `code-review` skill 的 Standards 轴自查，fixed point `8d787b94`。本票**零 F# / TS 改动**，
`fsharp-style.md` 与 `scripts/check-style.sh` 不适用；对照的标准是
`/home/xerxes2/.pi/agent/skills/writing-for-agents/SKILL.md` 与 `docs/agents/` 现有四份的体例。

- **单一真源**：§3 的 9 处逐条处置，无新增副本。
- **上下文负载**：唯一常驻文件 `AGENTS.md` 前后都是 36 行（远低于 60 行预算），但**装的东西换了**：
  旧版 22 行非空行里有 14 行是 `fsharp-style.md` / `issue-tracker.md` / RUNBOOK 里已有的内容副本；
  新版 30 行非空行里零副本，全是硬约束与指路。
- **指路措辞**：指路表的左列是**触发条件**（你要做的事）不是文件名，首词就是分支词；
  `workbook.md` / `RUNBOOK.md` 两份互指。
- **缓存与环境**：删掉三处会各自过期的缓存（`dotnet 10.0.111`、`docs/adr/0001 0002 0003`、
  「16 核」），改成让人去问环境或去看指路。
- **体例**：与 `dispatch.md` / `judgments.md` 一致——中文、`**给谁看**` 开头、判据带真实案例、
  无寒暄与励志话。
- **自己写出来的重复**：草稿里抓到 3 处（§3 的 10–12），已改。这正是票面警告的那个失败形式。
- **nitpick（只记录不改）**：`workbook.md` 97 行，是 `docs/agents/` 里第二长的常驻文档，但它不常驻
  上下文（靠指路进），且四节都是接票时按顺序要走的步骤，没有可下沉的分支。

## 6. 留给人的待审项

1. `docs/agents/issue-tracker.md` 的「no git remote」半句（禁改文件，见 §3）。
2. `docs/agents/judgments.md` 末节指向 RUNBOOK 找「资源预算 / fsi」的那一句（禁改文件，见 §3）。
3. `dispatch.md` §3.3 的必读顺序现在与 `workbook.md`「必读」逐项对得上；若以后要改顺序，
   改 `workbook.md` 那份、dispatch 那行只指路——本票没动 dispatch。
