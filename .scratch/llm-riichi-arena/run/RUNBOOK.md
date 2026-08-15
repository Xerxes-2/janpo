# M0 无人值守跑批 — Runbook

这份文件是**每个 implement subagent 的硬约束**。调度器在派活时会把它的路径给到 agent。

## 你的身份

你在无人值守的夜间跑批里实现**一张票**。没有人可以问。你必须自己做完、自己验证、自己提交，然后回一份简报。

## 强制约束

1. **版本控制只用 jj，绝不用 git。** 这是 colocated 仓库，git 命令会搞乱 op log。先读 `/home/xerxes2/.pi/agent/skills/jj-guide/SKILL.md`。禁止：`jj git push`、`jj op restore`、`jj abandon` 别人的 change、任何交互式 flag（`-i`）。提交用 `jj commit -m "..."`（`jj commit` 之后 `@` 自动变空，不要再 `jj new`）。
2. **只在分配给你的工作区里干活。** 路径由调度器给出。不要动其他工作区。
3. **不许问人。** 不要用 ask_user_question，不要停下来等确认。
4. **不许为了变绿而破坏测试。** 禁止删测试、加 skip、放宽断言、改期望值去迎合实现。做不到就 park（见下）。
5. **提交前必须跑格式化**：`dotnet fantomas .`（01 票负责把它装好）。CI 有 `--check` 关卡，格式不合就是红。
6. **不许改 `CONTEXT.md`、`docs/adr/*`、其他票的文件。** 有异议写进 `DECISIONS.md` 的「提案」区，由人裁决。
7. **不要在工具链上过夜。** 宿主机已有 dotnet 10.0.111 可直接用。nix flake 是加分项，若 `nix develop` 或 SDK 构建超过 ~10 分钟就退回宿主机 dotnet，把偏离记进 `DECISIONS.md`，继续做正事。

## 必读（按顺序）

1. 你的票：`.scratch/llm-riichi-arena/issues/<NN>-*.md`
2. `CONTEXT.md` — 术语表，**标识符命名的唯一权威**（罗马字日麻术语）
3. `docs/adr/0001` `0002` `0003` — 记法、可分享物、范围边界
4. `.scratch/llm-riichi-arena/spec.md` — 只读与你这票相关的段落
5. `docs/agents/*.md` — issue tracker 与领域文档约定

## 流程

按 `/home/xerxes2/.pi/agent/skills/implement/SKILL.md` 做：以 `/tdd` 逐个红绿切片推进，边做边跑类型检查与单文件测试，最后跑全量测试。

收尾按 `/home/xerxes2/.pi/agent/skills/code-review/SKILL.md` 做两轴 review（Standards + Spec），**fixed point 用调度器给你的 commit id**。若你无法派生 sub-agent，就自己顺序跑两轴。

- review 报出的 **blocking** 问题（错误行为、偏离票的验收、违反 ADR 或术语表）**自动修一轮**，修完重跑全量测试。
- 风格与 nitpick **只记录**，不修。

## 自主决策

票里没写清的地方（日麻规则长尾尤其多：符的边界、四杠散了的细节、mjai 事件字段的取值）：

1. 先在 `CONTEXT.md` 与三条 ADR 里找答案
2. 找不到 → **选最保守的一种**（最贴近日麻通行规则、最不影响其他票的），继续做
3. 追加到 `.scratch/llm-riichi-arena/run/DECISIONS.md`：票号、决定、被否决的选项、理由。一条三五行，不要长篇

## Park（做不下去时）

重试一次仍不绿，或发现该票的前提被前序票破坏了：

1. **不要提交半成品到主线**——把工作留在你自己的 change 里，`jj describe -m "WIP <NN>: <一句话为什么 park>"`
2. 写 `.scratch/llm-riichi-arena/run/reports/<NN>-*.md`：卡在哪、试过什么、你判断需要人做什么决定
3. 简报里第一行写 `STATUS: parked`

## 交付物

1. 代码 + 测试，全量测试绿，fantomas 干净，已 `jj commit`
2. 票文件：勾掉已完成的验收框，把 `**Status:**` 改为 `ready-for-human`
3. 完整报告写到 `.scratch/llm-riichi-arena/run/reports/<NN>-<slug>.md`（做了什么、关键取舍、review 结论、留给人的待审项）
4. **回给调度器的简报 ≤ 15 行**，第一行必须是 `STATUS: done` 或 `STATUS: parked`，第二行是 `CHANGE: <jj change id>`。简报之外的一切都写文件——调度器的上下文窗口要留给剩下的票。

## 资源预算（硬约束）

这台机器 16 核，且**同时有别的 agent 在跑 dotnet 构建与测试**。你的实验不能把机器占满：

- **不许 `multiprocessing.Pool()` 不带参数**（默认吃满所有核心）。要并行就显式限制在 **4 个进程以内**。
- 长跑实验用 `nice -n 19` 起步。
- 单次下载超过 ~200MB 就停手换更小的粒度。
- 跑之前先估算规模：「12,188 个文件 × 每文件 N 局」这种量级要先跑 100 个文件看单位耗时，再决定全量跑不跑。

违反这条的后果很具体：调度器在 2026-08-16 上午发现一个研究 agent 用 16 个 worker 全开扫语料，
load average 冲到 32，把另一个 agent 的 dotnet 测试挤慢了。**它的实验本身是对的，只是没有预算意识。**
