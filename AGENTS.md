# janpo

LLM 日麻对战平台 —— F# 规则引擎（Fable → JS，浏览器内运行）+ TypeScript Agent 层与 UI。

## Version control

This is a **colocated `jj` repo** (`.jj/` alongside `.git/`). Use `jj` exclusively for version-control
operations — running `git` commands here can corrupt or confuse state. There is no remote.

## Agent skills

### Issue tracker

Local markdown: specs and issues live under `.scratch/<feature-slug>/`; the active feature is
`llm-riichi-arena`. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical roles, each label string equal to its name (`needs-triage`, `needs-info`,
`ready-for-agent`, `ready-for-human`, `wontfix`), recorded as a `Status:` line in each issue file.
See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## 验证引擎行为

用 `dotnet fsi` 引用已编译的引擎 DLL 直接调真实 API，**不要把引擎逻辑移植到别的语言**。
现成探针与实测数字见 `scripts/fsi/README.md`。Python 只用于跑第三方 oracle 与解析数据。
