# 23 — LLM 座位闭环（Bare 档 + 兜底）

**状态**：done　**change**：见 `jj log`（本票一个 commit）　**fixed point**：`daea4452`（`nzxrltmv`）

M1 的曳光弹穿过了四层：TS Agent 层 → Fable 边界 → Elmish 驱动 → 牌桌。
浏览器里 LLM 坐一席、三随机 Player，**真跑打完了一个 Kyoku**（48–54 s，0 兜底）；
断电演习（坏 key）下同一局照样打完（24.9 s，20 手全兜底，页面红着说 401）。

---

## 1. 这一票落在哪几个文件上

| 层 | 文件 | 做了什么 |
|---|---|---|
| 引擎 | `src/Janpo.Engine/ScaffoldTier.fs` | 档位类型（Bare / Assisted / ToolSearch）+ wire 名 + 中文名 |
| 引擎 | `src/Janpo.Engine/Fallback.fs` | `Fallback.action : ScaffoldTier -> DecisionPackage -> Action`（Bare = 摸切 → 过 → 第一条） |
| 边界 | `src/Janpo.Web/Agent.fs` | `LlmSeat` 配置、请求 encoder、回执 decoder、`[<Import>]` 那一行 |
| 编排 | `src/Janpo.Web/Roster.fs` | 谁坐哪个座位（`SeatPlayer = Random \| Llm`） |
| 编排 | `src/Janpo.Web/Store.fs` | localStorage：**API key 唯一的落脚点** |
| 牌桌 | `src/Janpo.Web/Table.fs` | `decide` 按座位分派成 `Demand.Ready \| Demand.Asked`；`Move` 带兜底记号 |
| 页面 | `src/Janpo.Web/TablePage.fs` | 异步驱动（票号 / 等待 / 兜底落子）、配置面板、Agent 状态线 |
| Agent 层 | `web/src/agent/{types,prompt,ask,loop,piai,decide}.ts` | prompt 渲染、单轮 tool call、重试、四类失败的判读 |

测试：`FallbackTests`（引擎，5 条含 2 条属性）、`AgentTests` + `TablePageTests`（dotnet，18 条）、
`web/tests/agent/*`（node --test，20 条，回放录制响应）。

## 2. 边界长什么样（24 / 26 号票要读这一段）

**方向只有一个：F# 调 TS**（ADR-0005）。跨界的两段都是字符串：

```
F# ── {"decision":{…决策包…},"seat":{provider,model,api_key,timeout_ms,thinking,tier},"retry_limit":2} ──▶ TS
F# ◀── {"action_id":2,"reason":"…","failure":null,"attempts":1,"latency_ms":2131} ── TS
```

- `decision` 就是 `DecisionPackage.encoder` 的产物，一个字段都没改写（`AgentTests` 断言了这一条）。
- TS 侧**不认识 `Action`**：它只校验「这个 id 在不在包里」，回一个整数。
  换成真动作是 F# 侧 `DecisionPackage.tryAction` 的事，换不出来就 `Fallback.action` 代打。
- 兜底策略在**引擎**里（要读规则），Agent 层只回「我交不出来，原因是……」。

## 3. 兜底闭环

四类失败在 `loop.ts` 的 `judge` 里合流，之后走同一条路：**带着原因重问 → 上限 2 次 → 交不出来**。

| 触发 | 判读 |
|---|---|
| `stopReason: "aborted"` | 模型超时（值，不是异常） |
| `stopReason: "error"` | provider 报错（401 原文留着给人看） |
| 没调 `choose_action` / 调了别的工具 | 格式跑偏 |
| `action_id` 不是严格整数 | 格式跑偏 |
| id 不在这一包里 | 非法 id |

F# 侧收到 `failure` 就 `Fallback.action tier package` 代打，并把原因记在 `Table.Latest.Fallback` 上、
给 `Table.Fallbacks` 计数。**牌桌上看得见**：

- 「上一手」那行写成 `座位 1 摸切4索（兜底：provider 报错：401 …）`，`data-fallback="true"`
- Agent 状态线 `data-agent="troubled"`，红字 + 「这一桌已兜底 N 手」（`data-fallbacks`）

## 4. 人工验收的证据

### 4.1 真跑一局（种子 1177，座位 1 = DeepSeek `deepseek-v4-flash`）

```
$ JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-llm-seat.mjs --seed 1177
模式：真跑一局　座位 1　模型 deepseek-v4-flash
模型坐席状态：模型座位已就位，还没轮到它

一局打完，用时 48.4 s
上一手：座位 1 手切2万
Agent 状态（data-agent=spoke）：
  座位 1 的模型选完了（1376 ms）：手牌只有8张且牌山已空，需最大化听牌及和牌概率，
  打出孤张2m保留更多进张空间。
兜底代打：0 手
provider 请求：23 次，其中 4xx/5xx 0 次
请求间隔：中位数 1920 ms，均值 2116 ms

模型座位的牌河：河 19　1筒 6索 2万 7筒 西 8万 9筒 3万 9筒 3筒 5筒 2索 5万 4筒 9筒 3筒 发 1索 2万
模型座位的副露：副露　暗杠[背 9索 9索 背]　吃[6索 7索 8索]
流局　荒牌流局　听牌：座位 2　点数授受：座位 0 -1000　座位 1 -1000　座位 2 +3000　座位 3 -1000
各家点数：24000 / 24000 / 28000 / 24000
浏览器资源错误（请求失败）：0 条
人工验收通过 ✓
```

**注意副露那一行**：模型不只在打牌那一手被问到——它宣言了一次**暗杠**、吃了一次，
说明响应阶段的决策包（只有「碰 / 吃 / 过」那种两三条动作的包）也跑通了。

另外两次跑：默认种子 2088（54.0 s / 28 次请求 / 0 兜底 / 单次 2698 ms）与种子 4242
（49.9 s / 23 次请求 / 0 兜底 / 吃了两次，均值 2146 ms）。后者是 code-review 改完之后重跑的，
三次都没出现兜底。
**单手成本**：输入约 800 tok、输出约 90 tok（录制固件里的实测：`input 814 / output 94`）。

### 4.2 断电演习（同一页面，配一把坏 key）

```
$ node scripts/verify-llm-seat.mjs --bad-key
模式：断电演习（坏 key）　座位 1　模型 deepseek-v4-flash

一局打完，用时 24.9 s
上一手：座位 3 摸切4索
Agent 状态（data-agent=troubled）：
  座位 1 兜底代打：provider 报错：401: {"message":"Authentication Fails, Your api key:
  ****-key is invalid","type":"authentication_error",…}（重试 2 次仍无结果）　这一桌已兜底 20 手
兜底代打：20 手
provider 请求：60 次，其中 4xx/5xx 60 次   ← 20 手 × 3 次（首问 + 重试 2 次）
流局　荒牌流局　听牌：无
各家点数：25000 / 25000 / 25000 / 25000
人工验收通过 ✓
```

对局**没有卡死一次**，页面全程红着说明原因。`pageerror` 0 条（60 条 `console.error` 全是
浏览器自己为 401 写的 "Failed to load resource"，脚本单独计数）。

同一件事在 dotnet 上还有一条不依赖网络的用例：`TablePageTests.模型一次都答不上话，这一局照样打得完`。

### 4.3 打包（票 18 的结论复现）

```
dist/assets/index-*.js                348.77 kB │ gzip: 111.15 kB   ← 引擎 + Feliz + React + pi-ai 核心
dist/assets/deepseek-*.js               1.40 kB │ gzip:   0.59 kB   ← 选中的那一家
dist/assets/openai-completions-*.js    21.67 kB │ gzip:   6.90 kB   ← 它用的 SDK，懒加载
dist/assets/google-generative-ai-*.js 293.35 kB │ gzip:  59.90 kB   ← 没选就不下载
```

按 provider 分入口 + 动态 `import()`：**没选的那几家一个字节都不下载**。

## 5. 关键取舍

决策全文见 `DECISIONS.md` 的「## 23」段（8 条）。三条最要紧的：

1. **兜底在引擎**，Agent 层只说「我交不出来」——因为 Bare 档的摸切与 Assisted 档的安全打
   都是规则知识，而 TS 拿不到 `Action`。
2. **回执是五字段 JSON 而不是裸 id**——兜底原因要有地方放，顺带给 26 号票留好落点。
3. **票号与播放世代号分开**——重开一桌必须作废在飞的问话（旧 id 是按另一份包编的号），
   而暂停 / 换倍速不该作废它。

## 6. 留给人的待审项

- **23-6（重试 401）**：认证失败重试两次必然还是失败，代价是每手多两个请求。
  现在一视同仁，理由是分支少、实测可接受。真要省需要给 provider 错误分类。
- **配置面板的形态**：一行按钮选座位 + 五个输入框，没做美化（票里说别在这上面过夜）。
  M2 的配桌页会把它吃掉。
- **Bare 档 prompt 的措辞**：`web/src/agent/prompt.ts` 的中文是我拟的，第一版。
  24 号票会在它上面加 Assisted 档，改措辞的最佳时机是那一票。
- **21-c 的老待裁项又被撞了一次**：本票没有往决策包里加字段，因此黄金用例没红；
  但 24 号票填 `scaffold` 时一定会红。

## 7. code-review 结论

两轴各跑一遍（fixed point `daea4452`）：

- **Standards**（`docs/agents/fsharp-style.md` + 仓库既有约定 + Fowler 味道基线）：**2 条 blocking，已修**。
  1. *命名违反术语表*：那条「刚落定的一手」初版叫 `Move`，而 `CONTEXT.md` 的 **Turn（手）**
     已经占住这个概念（`Soak` 里也早有 `Turn: int`）。→ 改名 `Turn`（DECISIONS 23-10）。
  2. *Duplicated Code / Shotgun Surgery*：localStorage 的键名（`"provider"` / `"api_key"` …）
     在 `Store.fs` 里手写了两遍（读一遍写一遍），与 `LlmField` 的 case 是三份平行清单，
     加一个配置项要改五处。→ 键名收进 `LlmField.key`，`Store` 遍历 `LlmField.all`；
     加配置项现在只改 `LlmField` 的 case（编译器会指出其余几处）+ 视图。
     顺带补了两条用例（键名两两不同、每个字段读出来写回去原样）。
  修完重跑全量：dotnet 657 条、node 20 条、`./scripts/ci.sh` 全绿。
  `scripts/check-style.sh` 与 `dotnet fantomas --check` 干净；引擎 `let mutable` 预算未动（仍是 2）。
- **Spec**（票里的 8 条验收 + spec 的 Agent 层段落 + ADR-0005 的边界）：无 blocking。
  逐条对照见票文件的勾选框；ADR-0005 的四条边界（`GameState` 不越界、prompt 在 TS 侧渲染、
  不给 Fable 输出写 `.d.ts`、Fable 运行时后端不进引擎工程）逐条守住。

nitpick（只记录，未改）：

- `TablePage.fs` 已经 800+ 行，视图部分该在 M2 拆成几个组件文件（Divergent Change：
  它现在既是 MVU 又是四块视图）。
- `verify-llm-seat.mjs` 与 `verify-tracer.mjs` 各自起 `vite preview` + playwright + 收 console 错误，
  那段样板可以抽一个 `withPage()` 助手。
- `LlmSeat.Provider` / `.Model` 是裸 `string`（Primitive Obsession）。它们是用户自己填的 id、
  原样交给 pi-ai，本票不值得为它们造类型；provider 真要收敛成枚举，该等 M2 的配桌页。
- spec 的 Agent 层还有一条「自定义端点配置项支持本地模型（文档说明 CORS/mixed-content 限制）」
  **不在本票的验收里**，因此没做。
