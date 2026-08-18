# 72 — 配桌上的三个规则开关，以及重定超时默认值

**What to build:** spec 的 story 13（对局长度 / 红宝牌 / 食断）**在 UI 上根本不存在**：
页面写死 `Ruleset.yonma`，只有一个种子输入框（挂账 27-E，主人 2026-08-16 裁定进 M2 配桌页）。
引擎与 CLI 早就支持，只差控件。同一票顺手重定 LLM 座位的**超时默认值**——
30 秒是票 23 在没有思考预算的年代定的，而 DeepSeek medium 思考实测**单手 17–180 秒**
（`DECISIONS.md`「另记一条不用裁但 M2 必须知道的」），M2 一开思考气泡它必然不够。

**Blocked by:** 71（Live 在 `?table=1` 上，接缝已切）

**Status:** ready-for-human

## 上一波留给你的既成事实（票 70/71/77 已并入 `main`）

- 页面拆成了五个文件：`TableState.fs`（Model/Msg/MVU）、`TablePanel.fs`（控件与面板）、
  `TableBoard.fs`（牌桌与结算）、`AgentLine.fs`、`TablePage.fs`（58 行转出外壳）。
  **你的活在前两个**；加公开入口要 `TableState` 定义 + `TablePage` 转出 **两处都加**。
- **`Ruleset` 已经是 `TableModel` 的顶层字段**（`{ Ruleset; Source; Playback; Viewpoint; ShowDanger }`），
  不在 `Source` 联合里——你要做的就是把它从写死的 `Ruleset.yonma` 变成配桌拨得动的一份值。
- Live 侧的控件挂在 **`TablePanel.hostControls`**（回放侧是 `replayControls`，**那是票 75 的地盘**）。
- 开桌入口：`TablePage.initial llmAt config`（`?table=1`）与 `TablePage.home ()`（`/`）；
  地址解析在 `Route.fs`。`rosterOf : TableModel -> Roster option`（回放没有配桌）。
- 闸门现在**十趟**；要点牌桌的那几道已经全开 `?table=1`（真源在 `serve.mjs` 的 `hostPage(origin)`）。
  新增一道闸门要同时改 `verify-browser.mjs` 的名单与 `ci-web.sh` 的趟数措辞。

## 要什么行为

- [x] `?table=1` 上拨得动三项：**对局长度**（东风战 / 半庄战）、**赤宝牌**（有 / 无）、**食断**（有 / 无）
- [x] 拨完按「重开」才生效（与种子同一条路：`Roster` 带着 `Ruleset`，牌桌按配桌开）；
      **不许半场换规则**——那会让同一份牌谱前后按两套规则算
- [x] 三项落 localStorage，下次打开还在（与模型配置同一处 `Store`）
- [x] 牌谱里的 `ruleset` 跟着变（它逐字段写，26-8：回放要照这一场真正的规则重算）
- [x] LLM 座位的**超时默认值**改成对开着思考预算的模型也够用的数；理由写进报告
      （现有配置面板的提示语要跟着改，别留下「30 秒」这类过期数字）
- [x] 长度是 `Ruleset` 的一根轴（`GameLength`），**不是牌桌的字段**：局数序列由 `Ruleset.kyokus` 推

## 闸门

- [x] 关掉赤宝牌开一桌：导出的牌谱里 `ruleset.akadora` 为空，且事件流里一张赤牌都没有
- [x] 东风战 / 半庄战各开一桌：局数序列的长度对得上（四麻 4 / 8 局）
- [x] 食断开关：牌谱 `ruleset.kuitan` 跟着变（引擎的判定已有 dotnet 侧用例，这里只核「开关真的传下去了」）
- [x] 每条断言先红一次，红的输出进报告

## 边界

- [x] **不做预设选择器**。`Ruleset.majsoul` 已经存在（雀魂口径），但它不进 UI：
      spec story 13 只要那三项，而主人对采样参数那条裁决的理由同样适用——
      **对照实验的自由变量越多，结论越难归因**。要做另立一票。
- [x] 不碰三麻（座位数那根轴不动）
- [x] 不碰引擎的规则判定：这一票只把已有开关接到控件上
- [x] 不碰 `web/src/agent/**`（超时值是座位配置的一个数，Agent 层照旧读它）
- [x] 别动模型面板的**结构**（四席与档案库是票 73，会大改那块）——这一票只改超时那一格的默认值与提示语

## 顺手记着

一局制（只打一局就收）不在 `GameLength` 里，本票也不加。若将来对照实验嫌东风战还长，
那是给 `GameLength` 加一个 case 的事（F# 会把该改的 match 全指出来）。
