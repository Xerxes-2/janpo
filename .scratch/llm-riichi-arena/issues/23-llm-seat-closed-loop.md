# 23 — LLM 座位闭环（Bare 档 + 兜底）

**What to build:** M1 的曳光弹。一个座位交给 LLM：主持人在配置面板里填 provider、模型与 API key
（只落 localStorage），开局后那个座位的每一手都由模型经**单轮 tool call** 决定，其余三家仍是随机 Player。
模型超时、报错、给出解析不了的输出时，有限次重试后自动兜底，**对局永不卡死**。

这一票同时穿过 TS Agent 层、Fable 边界、Elmish 驱动与牌桌四层 —— 它跑通，M1 的骨架就立住了。
脚手架先用 **Bare 档**（只喂原始局面），Assisted 的数值是 24 号票的加码。

**Blocked by:** 18, 20, 22

**Status:** ready-for-human

- [x] Agent 层是 TS：输入决策包 JSON，输出一个动作 id 的 Promise；**它不认识 `Action`，也拿不到 `GameState`**
- [x] `choose_action` 工具的参数 schema 由决策包的动作 id 动态生成（`StringEnum` 约束 + 一句话理由字段）
- [x] 座位级配置：provider / 模型 / API key / 思考预算 / 超时；key 只进 localStorage，绝不外发到本平台
- [x] 兜底闭环：解析失败或 id 非法 → 重试上限 2 次 → 仍不行则**摸切**（Bare 档的兜底策略）；
      超时与 provider 报错走同一条兜底路径
- [x] 牌桌上能看出某一手是兜底出来的（不是静默替换）
- [x] Agent 层确定性测试：用**录制的响应**覆盖四类路径 —— 合法输出、非法牌 / 越界 id、格式跑偏、超时；
      **CI 里不调真实 API**
- [x] 浏览器里 LLM 坐一席 + 三随机 Player 打完一个 Kyoku
- [x] **断电演习**：故意配一把坏 key，整局照样打完（全程兜底），页面上有明确的错误状态提示

**实测约束（18 号票的结论，逐条别踩）**：包名 `@earendil-works/pi-ai`；按 provider 分入口导入，
不要 `providers/all` 与 `/compat`；OAuth 是 Node-only，配置面板只提供 API key；Bedrock 过滤掉；
超时与错误都是 `stopReason` 上的**值**，兜底写成 match 而不是 try/catch。详见
`docs/research/pi-ai-browser-usability.md`。
