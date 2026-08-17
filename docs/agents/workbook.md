# 接票干活手册

**给谁看**：在这个仓库里实现一张票的 agent。调度器读 `docs/agents/dispatch.md`，
判据读 `docs/agents/judgments.md`，**这一批跑批特有的排班与机器上限**读
`.scratch/llm-riichi-arena/run/RUNBOOK.md`（派工单必读的第一项）。

人人适用的硬约束（只用 jj、`./scripts/ci.sh` 是唯一判据、测试只许改硬、key 不进仓库、
术语权威是 `CONTEXT.md`）在 `AGENTS.md` 里，你已经读到了。**这份文件只写它之外的东西。**

## 你的身份

你在无人值守的跑批里实现**一张票**。没有人可以问：自己做完、自己验证、自己提交，回一份简报。
票里没写清的地方**自己判断并继续**（怎么判见下面「自主决策」），不要 ask_user_question，不等确认。
只在派工单分配给你的那个 jj workspace 里干活，别动别的工作区。

## 必读（按顺序）

1. `docs/agents/judgments.md` —— 开工过一遍（与你这票不相干的跳过），**收工前再过一遍你动过的那几条**
2. 你的票：`.scratch/llm-riichi-arena/issues/<NN>-*.md`
3. 派工单点名的那几份 `.scratch/llm-riichi-arena/run/reports/`
4. `CONTEXT.md`，以及 `docs/adr/` 里与你这票相关的那几条
5. `.scratch/llm-riichi-arena/spec.md` 里与你这票相关的段落

## 流程

按 `/home/xerxes2/.pi/agent/skills/implement/SKILL.md` 做：以 `/tdd` 逐个红绿切片推进，
边做边跑类型检查与单文件测试，最后跑全量。

**提交前跑 `dotnet fantomas .`**（写盘那一遍；`ci.sh` 里的 `--check` 只判红不改文件）。

收尾按 `/home/xerxes2/.pi/agent/skills/code-review/SKILL.md` 跑 Standards + Spec 两轴，
fixed point 用派工单给的 commit id；派不出 sub-agent 就自己顺序跑两轴。

- **blocking**（错误行为、偏离票的验收、违反 ADR 或术语表）**自动修一轮**，修完重跑全量测试。
- 风格与 nitpick **只记录**，不修。

**不要在工具链上过夜**：`nix develop` 或 SDK 构建超过 ~10 分钟就退回宿主机 dotnet（当次用哪个见 RUNBOOK），
把偏离记进 `DECISIONS.md`，继续做正事。

## 自主决策

票里没写清的地方（日麻规则长尾尤其多：符的边界、四杠散了的细节、mjai 事件字段的取值）：

1. 先在 `CONTEXT.md` 与 `docs/adr/` 里找答案
2. 找不到 → **选最保守的一种**（最贴近日麻通行规则、最不影响其他票的），继续做
3. 追加到 `.scratch/llm-riichi-arena/run/DECISIONS.md`：票号、决定、被否决的选项、理由。
   一条三五行，不要长篇

## Park（做不下去时）

重试一次仍不绿，或发现这票的前提被前序票破坏了：

1. **不要把半成品并进主线**——把工作留在你自己的 change 里，
   `jj describe -m "WIP <NN>: <一句话为什么 park>"`
2. 报告照写：卡在哪、试过什么、你判断需要人做什么决定
3. 简报第一行写 `STATUS: parked`

## 交付物

交付物的形状以这一节为准（派工单可以加码，但不会与这里矛盾）：

1. 代码 + 测试，`./scripts/ci.sh` 全绿，已 `jj commit`
2. 票文件：勾掉做完的验收框，把 `**Status:**` 改成 `ready-for-human`
3. 报告 `.scratch/llm-riichi-arena/run/reports/<NN>-<slug>.md`：做了什么、关键取舍、
   review 结论、留给人的待审项
4. `DECISIONS.md` **末尾**追加你这票那一段
5. **简报**（行数上限由派工单给）：首行 `STATUS: done` 或 `STATUS: parked`，
   次行 `CHANGE: <jj change id>`，正文写**下一票必须知道的接口事实**，而不是复述你做了什么。
   简报之外的一切都写进文件——调度器的上下文窗口要留给剩下的票。

## 资源预算

这台机器是共享的，**同时有别的 agent 在跑构建与测试**。当次的核数与上限数字在 RUNBOOK；判据是：

- 并行度**显式写死**，别用默认吃满所有核心的 API（`multiprocessing.Pool()` 不带参数是典型）。
- 长跑实验 `nice -n 19` 起步。
- **跑之前先估规模**：「12,188 个文件 × 每文件 N 局」这种量级，先跑 100 个看单位耗时，
  再决定全量跑不跑。
- 下载超出 RUNBOOK 那个上限就停手，换更小的粒度。

_案例_：2026-08-16 上午一个研究 agent 用 16 个 worker 全开扫语料，load average 冲到 32，
把另一个 agent 的 dotnet 测试挤慢了。**它的实验本身是对的，只是没有预算意识。**

## 验证引擎行为：用 `dotnet fsi`，不许移植

要检验引擎的行为（对拍、扫语料、找反例），**用 `dotnet fsi` 引用已编译的引擎 DLL 直接调真实 API**。
现成的探针与用法在 `scripts/fsi/README.md`（`#load "load-engine.fsx"` 一行就能用）。

**把引擎逻辑移植到 Python 或别的语言来验证引擎，不算数。** 两个理由：

1. **慢 2–3 个数量级**：实测同一批 5 万手，fsi 直调 API **5.8–8.2 µs/手**，纯 Python 移植版 ~1–10 ms/手。
   12,188 局牌谱的听牌判定，fsi 单线程约 1 秒；移植版要开 16 核跑几分钟（并因此挤慢别的 agent）。
2. **证据强度**：移植版与外部 oracle 不符时，你分不清是移植错了还是引擎错了。**移植版证明不了原实现。**
   它还会漏掉真 API 的不变量校验（`HandShape.create` 会拒绝张数不合法的手牌，移植版通常不会）。

Python 该用在两处：跑**现成的第三方** oracle（`scripts/oracle/`，PyPI `mahjong` 库），
以及牌谱数据的解析与统计。
