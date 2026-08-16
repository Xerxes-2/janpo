# README 发布准备（面向 GitHub 读者的 README + MIT LICENSE + 牌桌截图）

**状态**：done　**工作区**：`janpo-ws-b`　**fixed point**：`35fad8f0`（change `ywuyxvqo`）

产出四件：重写的 `README.md`、`LICENSE`（MIT，Xerxes-2，2026）、`docs/images/table.png`
与生成它的 `web/scripts/shoot-table.mjs`。**没碰**引擎、`web/src/`、`CONTEXT.md`、
`docs/adr/`、票文件与 `scripts/ci*.sh`。

---

## 1. README 的骨架

| 节 | 讲什么 |
|---|---|
| 首屏 | 中文一段「是什么」+ WIP 告示（M1 还差 29b/31/27）+ 英文简介（8 行）+ 牌桌截图 |
| 凭什么有意思 | 七条**约束**：双目标逐字段钉、结构性信息隐藏、脚手架判据、兜底闭环、牌谱唯一可分享物、危险度是启发式、规则集是一等输入 |
| 跑起来 | `nix develop` → `pnpm install` → `pnpm run dev`，页面上四步；本地端点指到 `docs/host/` |
| 不是什么 | 七条：不是天凤/雀魂替代品、无实时观战、无服务端、危险度不是统计模型、不做三麻古役、无动画/移动端、Mortal 是 M3 的可选依赖 |
| 这个仓库是怎么造出来的 | 调度器 + subagent + jj workspace 的跑批流程；`.scratch/` 目录导览；**三个真事**（形态错了、合流即红、假红先怀疑基础设施） |
| 路线 | M0 ✅ / M1 🚧（列出 29b、31、27 各自要做什么）/ M2 / M3 |
| 开发 | 原 README 的全部内容（环境、命令清单、仓库结构、加模块的约定）原样保留并补了三条 |
| 许可证 | MIT |

---

## 2. 每条技术声称与它的出处

**每条都在本工作区里核实过。**「实跑」= 我这次亲自跑的命令，输出见括号内。

### 首屏与「跑起来」

| 声称 | 出处 |
|---|---|
| 引擎经 Fable 编成 JS，与 Feliz 页面一起在浏览器里跑 | `web/package.json` 的 `fable` / `dev` 脚本；ADR-0005 的分层表；实跑 `pnpm run dev`（Fable 6.6s → Vite `http://localhost:5173/` 返回 200） |
| 没有后端，产物是静态文件 | 实跑 `cd web && pnpm run build` → `web/dist`（`✓ built in 1.33s`）；`scripts/ci-web.sh` 的 vite build 那道 |
| key 只落 localStorage、请求浏览器直发 provider | `src/Janpo.Web/Store.fs:19-24`（唯一读写处）、`src/Janpo.Web/Agent.fs:18-19` 的文档注释、`TablePage.fs` 配置面板那段说明文字 |
| provider 八家 + 自定义 OpenAI 兼容端点；模型是自由文本框 | `src/Janpo.Web/Agent.fs:158-169`（`providers` 列表）、`TablePage.fs` 模型输入框那段注释 |
| M1 只支持**一席** LLM | `TablePage.fs` 的 `llmPanel`：座位是**单选** picker，模型 `LlmAt: Seat option` |
| 脚手架「工具搜索」灰着（M3） | `TablePage.fs` 的 `selectField "table-llm-tier"`：`tier <> ScaffoldTier.ToolSearch` 才 enabled |
| 订阅制 OAuth 在浏览器里用不了，只能填 key | `Agent.fs:156` 的注释（票 18 实测：OAuth 与 Bedrock 都是 Node-only） |
| 本地端点的 CORS / mixed content 结论是实测的 | `docs/host/custom-endpoint.md`；实跑 `verify-custom-endpoint.mjs --mode allowed`（浏览器看到 200，状态 `spoke`）与 `--mode blocked`（CORS 拦截，状态 `troubled`） |

### 一份 F# 源码，两个目标

| 声称 | 出处 |
|---|---|
| 引擎 10,983 行 F# | 实跑 `wc -l src/Janpo.Engine/*.fs` → `10983 总计` |
| 引擎里**一处 `#if FABLE_COMPILER` 都没有** | 实跑 `grep -rn FABLE_COMPILER src/ tests/ --include=*.fs` → 全仓库唯一一处在 `src/Janpo.Web/TablePage.fs:166`（浏览器宿主，不是引擎）；DECISIONS 19-A |
| 引擎依赖白名单（三个包），CI 强制 | `scripts/ci.sh:10` 的 `FABLE_ALLOWED_PACKAGES` 与 `check_fable_dependencies` |
| 黄金用例两侧读同一份、跑同一段 F# | `tests/fixtures/golden/README.md` 的表；`src/Janpo.Golden/`；`scripts/ci-web.sh` 第五道 |
| **40 条用例、1947 个字段、3210 行：全部一致** | 实跑 `dotnet run --project src/Janpo.Cli -- golden check tests/fixtures/golden/dual-target.json`；浏览器侧同一行见本次 `./scripts/ci.sh` 日志「浏览器里的引擎与黄金用例逐字段逐行相同 ✓」 |
| 同种子的一局/一整场在浏览器里与 CLI 逐项对照 | `web/scripts/verify-tracer.mjs`；本次 CI 日志的 `game.scores 29800 24000 22200 24000 / juni 1 2 4 3 / kyokus 6` |

### 结构性信息隐藏

| 声称 | 出处 |
|---|---|
| 他家暗牌在 `MaskedSeat` 里没有字段 | `src/Janpo.Engine/Observation.fs:21-49`（类型里只有 `HandCount`，没有牌） |
| 掩蔽只定义在事件上，全项目一条法则 | `src/Janpo.Engine/MaskedEvent.fs:53`（「全项目唯一的一条掩蔽法则」）与 `:66` 的 `forSeat` |
| 观测是掩蔽流 fold 出来的 | `Observation.fs:828-829`：`ofState` 就是 `ofEvents (GameState.events state)` |
| `forSeat` 的 match 穷举 `Event`，不写 catch-all | `MaskedEvent.fs:66` 起的 match；DECISIONS 29a-1 |
| 属性测试拿 fold 出来的观测与权威状态逐字段对 | DECISIONS 29a-6（`ObservationProperties`，报错点名 `others.2.riichi`） |
| 上帝视角是独立投影，不在掩蔽法则定义域内 | DECISIONS 29a-5；`Observation.fs:890` 的 `GodView.ofState` |

### 脚手架档位

| 声称 | 出处 |
|---|---|
| 判据是「感知 vs 计算」，README 里那段引文 | `CONTEXT.md:161` 的 `ScaffoldTier` 词条（**逐字摘录，没有改写**） |
| 档位只决定 prompt 渲不渲染，包恒带脚手架 | `.scratch/.../reports/24-assisted-tier-scaffold.md` §2 第 1 条（DECISIONS 24-1）——因此 README 明写它**不是**结构性隔离 |

### 兜底闭环

| 声称 | 出处 |
|---|---|
| 四类失败在 `judge` 里合流 | `web/src/agent/loop.ts:41` 的 `judge`（aborted / error / 没调工具或调错工具 / id 不合法） |
| 重试上限 2 次，每手最多问 3 次 | `src/Janpo.Web/Agent.fs:242`（`let retryLimit = 2`）+ `loop.ts:134`（`rounds = retry_limit + 1`） |
| 代打策略：Bare 摸切→过→第一条；Assisted 不退向听里最安全 | `src/Janpo.Engine/Fallback.fs`（`bare` / `assisted` / `action`，行 82 起分档） |
| 代打不静默，牌桌上看得见 | `TablePage.fs` 的 `latest` 与 `data-fallback`；报告 23 §3 |
| **断电演习：坏 key，一局照样打完 18.4 s，兜底 20 手，60 次请求全 4xx** | 实跑 `cd web && node scripts/verify-llm-seat.mjs --bad-key`（本次发布前重跑，输出逐字抄进 README） |

### 牌谱与回放

| 声称 | 出处 |
|---|---|
| Paifu 是唯一可分享物、不存在局面快照格式、回放就是 fold | `docs/adr/0002-paifu-is-the-only-shareable-artifact.md` |
| **soak 200 场 / 1038 局 / 86,324 手，逐手验不变量含回放确定性，问题：无（9.6 s）** | 实跑 `dotnet run --project src/Janpo.Cli -- soak 1 200` |
| 另有 200 场整场重放逐条相同、差异 0 | 报告 `26-decision-record-and-paifu-export.md` §3.1 |
| 浏览器侧闸门：真下载 → 那份字节 fold 回去 → 逐条相同、点数一致、不含 key | `web/scripts/verify-export.mjs`；`scripts/ci-web.sh` 第八道；本次 CI 日志 |
| 决策记录的内容（prompt / 工具定义 / 原始输出 / thinking / 延迟 / 重试次数） | `CONTEXT.md` 的 `DecisionRecord` 词条；`loop.ts` 的 `audited` |

### 危险度与规则集

| 声称 | 出处 |
|---|---|
| 四条规则（现物/筋/壁/宝牌周边）、并列名次、威胁只认立直或副露、片筋不算筋 | `src/Janpo.Engine/Danger.fs`（`rank` 在 :367）；报告 `25-danger-module.md` §2 |
| 是启发式不是统计模型 | `CONTEXT.md:278` 的 `Danger` 词条（「第一版是启发式，不是统计模型」）；牌桌面板上那句「不是概率」 |
| 不参与规则判定，且有闸门自证 | `scripts/check-style.sh` 的「票 25」段（规则判定的用例里出现 `Danger` 就红） |
| 规则集是一等输入、预设按平台命名、默认对齐天凤（60 局实测） | `docs/adr/0004-ruleset-as-first-class-input.md` 背景与决定 1–3 |
| 真牌谱对拍：CI 内 18 局 / 177 kyoku 零差异；扩到 200 局 / 2110 kyoku 抓到两个真 bug | 报告 `13-real-paifu-differential.md` 摘要与 §五；`jj log` 里的 `fix(16): 明杠的新宝牌欠到打牌那一刻才翻` |

### 「这个仓库是怎么造出来的」

| 声称 | 出处 |
|---|---|
| 硬约束（jj-only、不许问人、不许为变绿破坏测试、park 流程） | `.scratch/llm-riichi-arena/run/RUNBOOK.md` |
| 32 张票 / 29 份报告 / DECISIONS「2000 多行」/「近百个 commit」 | 实跑 `ls issues \| wc -l`＝32、`ls run/reports/*.md \| wc -l`＝29、`wc -l run/DECISIONS.md`＝2352（追加本次决策段后 2394）、`jj log -r 'all() & ~empty()'` 计数＝99。**README 里写成模糊数**（「2000 多行」「近百个」）：两个数字每次追加都会漂 |
| **六次人的裁决** | DECISIONS.md 的六段：「主人 8/16 提的 prompt 改造」「第二条提醒：手切摸切」「第三次裁决：快照降为派生」「第四次裁决：感知 vs 计算」「第五次裁决：自定义空间」「第六次裁决：历史由 TS 渲染」 |
| 例一（形态错了 + 29a 落地零改动） | DECISIONS「教训：什么时候该停下来问『是不是形态错了』」段、「第三次裁决」段、29a 段；M1-SCHEDULE 的「29a 集成」记录（`Observation` 签名未变 → 22/24/25 一行没改、黄金用例一字未动） |
| 例二（21 与 22 各自绿、合流即红，`tehai_count`） | DECISIONS「W2 集成」段；M1-SCHEDULE「22 集成」记录 |
| 例三（并行跑批撞死端口造成假红） | M1-SCHEDULE「30 集成」记录；DECISIONS「集成 30 号票时的一次假红」段 |
| M1 还差 29b / 31 / 27 及各自内容 | 票 `29b-cacheable-prompt.md`、`31-prompt-as-data.md`、`27-m1-acceptance.md`；M1-SCHEDULE 状态表 |
| M2 / M3 的内容 | `.scratch/llm-riichi-arena/spec.md` 的「里程碑切分」 |

### 开发一节

| 声称 | 出处 |
|---|---|
| 每条命令都能跑 | 实跑：`nix develop --command …`（dev shell 起得来）、`./scripts/ci.sh`（**全绿**）、`dotnet build`、`dotnet test`（763 条）、`dotnet fantomas .`（141 文件 unchanged）、`golden check`、`golden write`（写回后 `jj diff` 无变化）、`janpo tile`、`janpo --help`、`janpo soak`、`pnpm install/build/dev/test/format`、`verify-llm-seat --bad-key`、`verify-custom-endpoint --mode allowed|blocked`、`shoot-table.mjs`；CI 内部覆盖了 `ci-web.sh`、`pnpm run check/typecheck/verify/verify:golden/verify:export` |
| 测试 763 条 | `dotnet test` 输出：引擎 690 + 浏览器宿主 73 |
| Agent 层用例回放录制响应、不调真实 API | 实跑 `pnpm run test` → 51 条通过；`scripts/ci-web.sh` 第三道的注释 |
| `ci-web.sh` 是八道 | `scripts/ci-web.sh` 头注释 |
| 只有真 key 的两条与自定义端点三条不进 CI | 各脚本头注释；M1 增量约束 6（`M1-SCHEDULE.md`） |

---

## 3. 删掉或写弱的声称（以及为什么）

1. **「座位可以坐不同模型、不同信息辅助档」→ 写弱成「把一个座位交给模型」。**
   `TablePage.fs` 的模型坐席是**单选** picker（`LlmAt: Seat option`），M1 只有一席 LLM。
   四席同坐是 M2（spec 里程碑）。README 首屏与「跑起来」都按一席写。
2. **「跑完整场」→ 拆成两句。** 四家随机选手在页面里打完整场东风战我实测过（种子 1177 走 552 手，
   终局精算 29800 / 24000 / 22200 / 24000、顺位 1/2/4/3，与 CLI 的 `janpo game 1177` 逐项相同）；
   但**LLM 坐一席打完一整场是票 27 的验收，还没做**，因此 WIP 告示里明写。
3. **URL 分享 / 导入牌谱 / 首页 Demo Paifu → 全部移到 M2，不在能力里写。**
   报告 26 §「没有多做」：URL 分享、导入 UI、思考气泡都还没做。ADR-0002 描述的是设计，不是现状。
4. **脚手架档位不写成「结构性隔离」。** 决策包恒带脚手架，档位只决定 prompt 渲不渲染
   （DECISIONS 24-1）。结构性成立的只有他家暗牌那条，README 明确把两者分开。
5. **「同一份源码零改动」限定到引擎。** 全仓库确实还有一处 `#if FABLE_COMPILER`
   （`src/Janpo.Web/TablePage.fs:166`，浏览器宿主的 `Cmd.OfPromise`），因此措辞是
   「`src/Janpo.Engine/` 的 10,983 行**一处都没有**」，不是「全仓库零处」。
6. **不写 LLM 真跑一局的旧数字**（报告 23 的 48.4 s / 0 兜底）。那是 23 号票当时的实测，
   我这次没有复跑（要真 key、要花钱），所以 README 里只用**我自己重跑过的**断电演习数字。
7. **不写 prompt 缓存命中率、token 账单**：29b 还没落地，现在没有可引的数。
8. **不写引擎的微性能数字**（增量 fold 0.56 ms 之类）：与读者的判断无关，且改一版就过时。
9. **不写「Mortal 已接入」「天梯」「联机」**：spec 的 Out of Scope 与 M3，都还没有。
10. **不写「快好了」这类话**：WIP 告示直接列出还差哪三票、以及尚未验收的那条。

---

## 4. 截图与那个脚本

- 图：`docs/images/table.png`，**围观视角**（座位 0，页面默认值，没开上帝视角），
  种子 1177 走 52 手：东1局第 9 巡，四家都有副露，河里手切/摸切（虚线）看得出来。
- 脚本：`web/scripts/shoot-table.mjs`，**选择留下**而不是用完删。理由：README 的图是仓库资产，
  改了页面就要重出一张；它与四个 `verify-*.mjs` 同构（同一个 `chrome.mjs`、同一套 testId），
  留着的边际成本近乎零，删掉则那张图变成没法复现的产物。它**不进 CI**。
- 端口取 **4190**，与 `verify-tracer`(4179) / `verify-golden`(4180) / `verify-llm-seat`(4181) /
  `verify-export`(4182) / `verify-custom-endpoint`(4183、4199) 全部错开——票 29b 正在改这些脚本的
  端口逻辑，落地后这个脚本应当改用它那个共享端口助手（**留给 29b 或后续一并收**）。

### 顺带发现的一个 UI 缺陷（不在我可改范围，留给主人）

`web/src/styles.css:230` 的 `.tile.back` 把 `color` 设成 `transparent`，而同一元素的
`background: color-mix(in srgb, currentcolor 25%, transparent)` 与 `.tile` 的
`border: 1px solid currentcolor` 都解析到这个 `color` —— 于是**牌背整块透明**：
围观视角下他家手牌那一行、以及暗杠两端扣着的那两张，在页面上什么都看不见
（截图里就是这个样子）。功能没错（张数、投影都对），是纯样式问题。
`web/src/` 在本次任务的禁改清单里，因此只报告不修；一行 CSS 的事（把背景与边框换成
写死的颜色或 `var(--line)`）。修完 README 那张图值得重出一遍。

---

## 5. CI

`./scripts/ci.sh` 在本工作区跑到 **全绿**（含浏览器内三道）。之后的改动只有 README、LICENSE、
截图与那个脚本；`biome ci`（新脚本进它的检查范围）与 `dotnet fantomas --check`、`pnpm run format`
都单独跑过，无变化。提交前重跑一次全量 CI 确认。
