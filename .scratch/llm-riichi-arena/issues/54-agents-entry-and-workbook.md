# 54 — AGENTS.md 收成地图，RUNBOOK 的耐用一半提进 docs/agents/

**What to build:** 三层沉淀的后两层（第一层 `docs/agents/dispatch.md` 调度器已写好，**别改它，只指路**）：
把 `AGENTS.md` 收成一份 **≤ 60 行的地图**（它每个 agent 每次都读，长度是每次都付的成本），
并把 `.scratch/llm-riichi-arena/run/RUNBOOK.md` 里**耐用的那一半**提成 `docs/agents/workbook.md`
（接票干活的人读它），跑批当次特有的留在 `.scratch`。

**Blocked by:** None

**Status:** ready-for-agent

## 一、AGENTS.md 有一处硬错，先修

- [ ] **「There is no remote」是假的**：仓库已公开在 `https://github.com/Xerxes-2/janpo`，
      站点在 `https://xerxes-2.github.io/janpo/`，`main` 有远端且 CI/Pages 都跑。
      **它是每个 agent 自动读到的文件，错在这里最贵**
- [ ] 顺便核一遍 AGENTS.md 其余每一句是否仍然为真（这是判据 15 那类活：**「哪些地方描述过它」**）

## 二、AGENTS.md 收成地图（≤ 60 行）

- [ ] 只留三样：**项目一句话**、**人人适用的硬约束**、**「哪份文件管哪件事」的指路**
- [ ] 硬约束至少含：只用 jj（现有那段留着，改掉 no remote）、**不许为了变绿破坏测试**（park 掉报上来）、
      **API key 绝不进代码/测试/fixture/提交**、**术语权威是 `CONTEXT.md` 且改它要单票授权**、
      `./scripts/ci.sh` 是唯一的全绿判据
- [ ] 指路要点名新增的三份：`docs/agents/judgments.md`（18 条判据，**每条附真实案例**）、
      `docs/agents/workbook.md`（接票干活的人）、`docs/agents/dispatch.md`（调度器）
- [ ] **不要把判据、跑批流程、调度协议抄进来**——只指路。抄一次就多一份会各自过期的副本

## 三、RUNBOOK 的耐用一半 → `docs/agents/workbook.md`

现在那份文件标题写着「M0 无人值守跑批」，可 M1/M2 一直在用它——**错位感就是它该被提升的信号**。

- [ ] **提上来的**（耐用）：身份与分工、强制约束、park 流程、交付物形状、资源预算的**判据**、
      「验证引擎行为用 `dotnet fsi` 不许移植」那条
- [ ] **留在 `.scratch/…/run/RUNBOOK.md` 的**（当次特有）：这一批的排班与并行上限、
      当次的资源上限数字、M0 专属的话术；并把标题改成不再自称 M0
- [ ] 两份都要**指向对方**，且**同一件事只写一处**——发现重复就删掉其中一份，别两处各留一份
- [ ] `.scratch` 那份仍是派工单必读的第一项，所以**别把它掏空到没用**

## 四、自查（写完自己回答，答不上就是没做完）

- [ ] 一个刚被派来的 agent，只读 `AGENTS.md` + 派工单，**知不知道自己该去读哪几份文件**？
- [ ] 这四份文件（AGENTS / judgments / workbook / dispatch）里**有没有同一条规则出现两次**？
      列出你查过的重复点与处置
- [ ] AGENTS.md 现在多少行？**超 60 行就再砍**

## 边界

- [ ] **不碰** `docs/agents/dispatch.md`（调度器刚写的，只指路）、`docs/agents/judgments.md`（票 53 刚定）、
      `CONTEXT.md`、`DECISIONS.md`（除末尾追加你自己那段）、任何代码与 workflow
- [ ] 不改 `docs/agents/fsharp-style.md`、`issue-tracker.md`、`triage-labels.md`、`domain.md` 的内容，
      只在 AGENTS.md 的指路里正确引用它们
- [ ] 照 `/home/xerxes2/.pi/agent/skills/writing-for-agents/SKILL.md` 写：短、可执行、没有寒暄与励志话
