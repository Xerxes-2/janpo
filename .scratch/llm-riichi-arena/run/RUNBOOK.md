# 无人值守跑批 — 当次须知

**先读 `docs/agents/workbook.md`**（身份、必读顺序、流程、自主决策、park、交付物、资源预算判据、
用 `dotnet fsi` 验引擎）。人人适用的硬约束在 `AGENTS.md`，判据在 `docs/agents/judgments.md`，
调度器协议在 `docs/agents/dispatch.md`。

**这份文件只留这一批跑批特有的东西**：排班、当次的机器上限、里程碑话术。

## 这一批：M3

- 这一批要做什么、排班与波次：`.scratch/llm-riichi-arena/run/M3-SCHEDULE.md`
  （M0 / M1 / M2 的在 `SCHEDULE.md`、`M1-SCHEDULE.md`、`M2-SCHEDULE.md`，只当历史读）
- 工作区叫 `janpo-ws-a`、`janpo-ws-b`…（一个 agent 一个）；
  同时能派几路、怎么编波是调度器的事，见 `docs/agents/dispatch.md`

## 当次的机器上限

本机是共享的，同一时刻可能有好几个 agent 在跑 `./scripts/ci.sh`。你自己的实验按这几个数字来：

- 并行进程 **≤ 4**，长跑的 `nice -n 19` 起步
- 单次下载超过 **~200 MB** 就停手，换更小的粒度
- 宿主机 dotnet 可直接用（版本自己 `dotnet --version`），不必等 nix 拉工具链

## 里程碑话术

M0（引擎与摸打循环）、M1（浏览器里跑起来 + 两档脚手架）、M2（页面成形 + 真 key 跑真局 + 分享导入回放）
都已收官，**现在在做 M3**：本地真人坐席、强 AI 基线、复盘对照标注、脚手架三档完整化。

写票、写报告、写简报时别再用「先让引擎跑起来」「M0 验收」「把页面立起来」那套说法：
引擎、浏览器侧、两档脚手架、四席模型档案、气泡、回放时间轴、分享与导入**都已经在跑**，
站点活着，首页第一眼就是一局真对局的回放。**M3 新加的那一样是「桌边坐了个人」。**

真 key 在 `/tmp/deepseek_key`（模型 `deepseek-v4-flash`），口子是 `JANPO_KEY_FILE` + `--llm`
（用法见 `web/scripts/verify-export.mjs`）。**CI 里零真实请求**——要真跑必须是派工单点名允许的那几局。

## 老票据里的「RUNBOOK 第 N 条」

2026-08-17 之前，强制约束、park、交付物这些都编号写在这份文件里，所以老票与老报告里有
「RUNBOOK 第 6 条（不许改 `CONTEXT.md`）」「第 7 条（不在工具链上过夜）」这类引用，指的是那一版。
它们现在分别在 `AGENTS.md` 的硬约束与 `docs/agents/workbook.md` 里，**不再有编号**——
新写的东西要引编号，就引 `docs/agents/judgments.md` 的判据 1–18。
