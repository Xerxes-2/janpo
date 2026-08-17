# 46 — M1 收尾的一把碎账（含票 48 核出的两条）

**状态**：done　**change**：`zutovnyq`（本票一 commit）　**工作区**：`janpo-ws-b`
**fixed point**：`7d3e19c0`（change `ovrvmyrz`）
**验证**：`./scripts/ci.sh` **EXIT=0**（全绿，含浏览器那六道）、`dotnet fantomas .` 146 文件
unchanged、`pnpm run check` / `typecheck` 干净、`dotnet test` 807 条全过。

一句话：**「`Persona` 一局内不变」从文档里的一句话变成了页面上执行得住、测试钉得死的约束**
（边界取「局」，改动生效于下一局，页面当场说出来），外加六条碎账逐条落地。

---

## 1. 最重要的那条：`Persona` / `PromptTemplate` 一局内不变

### 1.1 执行者是谁

新类型 `TablePage.Rendering`（`{ Persona; Template }`）+ `TableModel.Pinned: Rendering option`：

| 时刻 | 发生什么 |
|---|---|
| 一局的**头一次问话**（`step` 走到 `Demand.Asked`） | `Pinned = Some(Rendering.ofSeat config)` —— 定型 |
| 定型之后改那两格 | `Llm` 照变（面板与 localStorage 照存），**发出去的仍是定型那一版** |
| `KyokuAdvanced` / `Restarted` | `Pinned = None` —— 松开，面板上改过的当场生效 |
| 这一局还没问过话 | `Pinned = None`，改了立刻生效（开局前填人格不该等一局） |

真正发出去的那份配置由 `TablePage.rosterOf` 推导（`effective`：人格与模板取定型那版，
**其余字段取面板现在的值**）。**只定这两格**：provider / 模型 / key / 超时 / 思考预算不动前缀的
字节，脚手架档位只动尾部，它们照旧下一手生效。

**没有锁那两格**（票面给的两个选项里选了后一个）：textarea 照常可编辑，但面板上多一行
`最近一手的渲染版本：janpo-default@08fcaec3　人格 / 模板改过了：本局仍用定型那一版，下一局生效。`
（改过之后那一行加粗不淡化）。两格的 label 也改成「人格（一局内不变）」「prompt 模板（一局内不变）」，
面板说明里加了一句「两格都在可缓存的前缀里，因此一局之内不会变——开局后再改照样存住，但要下一局才发得出去」。

### 1.2 会红的测试（红的输出照抄）

四条新用例在 `tests/Janpo.Web.Tests/TablePageTests.fs`。**先红后绿**：

```
[xUnit.net]     Janpo.Web.Tests.TablePageTests.一局问过话之后再改人格，本局仍然发定型那一版 [FAIL]
  错误消息:
   Assert.Equal() Failure: Strings differ
Expected: ""
Actual:   "你是一位以防守见长的雀士，宁可少和一把，也不点炮。"
           ↑ (pos 0)
[xUnit.net]     Janpo.Web.Tests.TablePageTests.模板同理：一局内改不动，开下一局才生效 [FAIL]
  错误消息:
   Assert.Equal() Failure: Strings differ
Expected: ""
Actual:   "{"id":"我的模板"}"
           ↑ (pos 0)

失败!  - 失败:     2，通过:    17，总计:    19
```

那正是修之前的行为：**改了人格，这一手就换了前缀**（`rosterOf` 每一手现推导 `model.Llm`）。
修完 20/20 绿。

另两条用例本来就绿（`Paifu` 的形状本来就对，票面也说了别改），因此各按红一次自证：

- `改过的人格在面板上看得见“下一局生效”`：把 `renderingPending` 改成恒 `false` →
  `Assert.True() Failure / Expected: True / Actual: False`（`TablePageTests.fs:324`）
- `局间换人格：牌谱里两版 preamble 都在，各自记着自己的渲染版本`：把第二局那条回执的渲染版本
  改成与第一局同一个 → `Assert.Equal() Failure: Values differ / Expected: 2 / Actual: 1`
  —— 它数的确实是「牌谱里有几版 preamble」，不是「有没有 preamble」。

### 1.3 真浏览器里看过（M1 那条规矩）

本机假端点（`fake-endpoint.mjs`，零网络），模型坐席 0，一次性脚本跑出来的四行：

```
模型答上话之后：   最近一手的渲染版本：janpo-default@08fcaec3
本局内改人格：     最近一手的渲染版本：janpo-default@08fcaec3　人格 / 模板改过了：本局仍用定型那一版，下一局生效。
开下一局：         最近一手的渲染版本：janpo-default@08fcaec3
下一局问过一手：   最近一手的渲染版本：janpo-default@da89c183
```

第二行是「不静默地半局换掉」的证据，第四行是「局间换得动」的证据。**版本号真的换了**
（`08fcaec3 → da89c183`），而它是 Agent 层按模板内容算出来的，不是 F# 这边编的。

### 1.4 四处说法统一

| 位置 | 改成 |
|---|---|
| `src/Janpo.Web/Agent.fs:69`（`AgentAnswer.Preamble`） | 「**一局内不变，可在局间更换**」+ 指向 `Paifu.fs` 的 `Preamble` 与 `TablePage.Rendering` |
| `web/src/agent/types.ts:180`（`DecideResponse.preamble`） | 同上，并写明执行者是页面 |
| `web/src/agent/ask.ts:18`（`AskRequest.system`） | 「一局内逐字不变、局间可以换」 |
| `web/src/agent/loop.ts:99`（`Asked.preamble`） | 同上（**第四处，票里没点名但说的是同一件事**） |
| `web/tests/agent/record.test.ts:37` | 同上（第五处，同一句话） |
| `src/Janpo.Engine/Paifu.fs:95`（`Preamble`） | 原话「主持人打到一半换了人格」→「一局内不变、局间换得动……这就是它是一个**列表**的理由」。**只动注释，形状一个字段没改** |

---

## 2. 其余六条

### 2.1 30-A：`custom` → `custom-openai`（含旧值迁移）

- `LlmSeat.customProvider = "custom-openai"`（`Agent.fs`）与 `CUSTOM_PROVIDER = "custom-openai"`
  （`endpoint.ts`）——两处的「改一处要改两处」注释都在，各自加了一句为什么带后缀。
- **旧值怎么办**：新增 `LlmSeat.legacyCustomProvider = "custom"` 与 `LlmSeat.readProvider`，
  在 `LlmSeat.edit` 的 `Provider` 分支上做**一条**迁移：读到 `custom` 当场升成 `custom-openai`。
  localStorage 的值全部经 `Store.readSeatConfig → LlmSeat.edit` 进来，因此**迁移只有这一处**。
  下次任何一次编辑都会把新值写回 localStorage。
  - 为什么是「兼容」而不是「报错」：两个 id 指的本来就是同一件事（自定义 OpenAI 兼容端点），
    而**把旧值原样留着才是「静默地读成别的东西」**——`isCustom` 会当它不是自定义端点，
    转而拿 `custom` 去 pi-ai 的目录里查一家，那正是这次改名要防的事。
  - **TS 侧不认旧 id**（`endpoint.ts` 写了注释说明）：在那一层再认一次等于把刚防住的撞名放回来。
- **牌谱里的旧值**：provider id 只以 `provider/model` 的形态出现在 `start_game` 的 `names` 里
  （`Roster.names`）。那是**wire 上的名字，没有任何代码把它解析回配置**（回放只 fold 事件流），
  因此旧牌谱里的 `custom/qwen3:8b` 照旧读得动、含义不变，**不迁移、也不需要迁移**。
- 跟着改的：`AgentTests` 两条断言 + 一条新的迁移用例、`verify-custom-endpoint.mjs` 与
  `verify-redaction.mjs` 灌进 localStorage 的那个值、`docs/host/custom-endpoint.md` 加两句
  （自己写 localStorage 的话填 `custom-openai`，旧值会自动升上来）。
- 真浏览器验过两遍：localStorage 填 `custom-openai` 与填旧值 `custom`，两次都走进自定义端点、
  模型答上话；填旧值那次面板上 provider 那一格显示的是 `custom-openai`。
  CI 里那道打码闸门（真发请求给本机假端点）也是走的这条路，绿。

### 2.2 31-D：渲染版本显示出来

面板上人格/模板那一行下面新增 `table-render-version` 一行：`最近一手的渲染版本：模板 id@内容哈希`。
**取自最近一条 `DecisionRecord.RenderVersion`**，不在 F# 侧重算——哈希在 `template.ts` 算，
这边再算一份就是第二份权威。因此这一行说的是**真发出去过的那一份**。
还没发出去过一次问话时（含没填 key 那几手：记录留着，但 prompt 根本没渲染过、版本号是空串）
写「渲染版本：这一桌还没发出去过一次问话」。`data-render-version` / `data-rendering-pending`
给无头验收读。

### 2.3 31-C：那两格 textarea 的样式

`.llm-panel textarea` 归到与 input/select 同一条规则里（`font: inherit` + 同一份内边距），
另加多行文本自己要的两样：宽度 18rem（与那一行控件对齐）、`resize: vertical`（横向拉宽会把面板挤断行）。
**边框与配色仍交给浏览器**——旁边的 input/select 走的就是默认，单给 textarea 画一套才叫不一致。

### 2.4 37-A：页脚补版权年份与作者

页脚末尾变成「按 MIT 许可放出。© 2026 Xerxes-2」。年份与作者写死在 `Footer.copyright` 一处，
注释里点明与 `LICENSE` / README 末尾那一行是同一个说法。**不拿 `DateTime.Now` 算年份**：
版权年是法律事实，不是看页那天的日历。`verify-tracer.mjs` 那道页脚断言照旧绿。

### 2.5 33-A：`Agent.fs` 的死链接

`providerToDisplay` 的注释从「README 的『渲染层出口』约定」改成
「`docs/development.md`「加新模块时的约定」那节的『渲染层出口』」——那条约定 33 票搬过去了。

### 2.6 36-C：打码措辞两处各留一条注释

措辞定稿不动（`[API key 已打码]` / `[端点地址已打码]`）。`redact.ts` 的两个常量与
`verify-redaction.mjs` 里写死的那份，现在**两边都写明「另一处有一份一字不差的，改一处必须改另一处」**，
并说清为什么不抽成 `import`：闸门从被验实现里取常量的话，实现换成空串它照样绿。

---

## 3. 边界（票面写死不做的，一样没做）

- 没做布局改造（票 44 的地盘）：只加了一行文字与一条 textarea 样式，`.controls` 的结构没动。
- prompt 的措辞与结构一个字没碰（`template.ts` / `wording.ts` / `prompt.ts` 未编辑）。
- 引擎的规则判定没碰：`Paifu.fs` 只改了一段注释，类型与函数一个字段都没动。
- 票尾那六条「维持原判、不做的」一条都没碰。

## 4. 截图

`docs/images/table.png` 重出（`node scripts/shoot-table.mjs`，种子 1177 / 52 手），**自己打开看过**。
除了这一票的两处（textarea 现在是正文字体、下面多一行渲染版本），图里还顺带补上了**票 42 加的
「其余座位：均匀随机 / 有主见」那一行**——42 号票落地时没重出截图，这次一并进去了。

## 5. Standards 自审（无法派生 sub-agent，自己顺序跑）

按 `docs/agents/fsharp-style.md` + `docs/development.md` + Fowler 味道基线过了一遍：

- 规则 1/2/3（不许从里往外读）：新增代码里的变换链全是管道
  （`table.Decisions |> List.tryLast |> Option.map … |> Option.filter …`、
  `model.Llm |> Rendering.applyTo pinned`）；boolean 与 `prop.custom` 里的两层调用按规则 4.1 保留。
- 规则 5（命令式边界）：新增零 `let mutable`、零循环；`check-style.sh` 绿。
- **Data Clumps（正面）**：`Persona` 与 `Template` 一直成对出现，这次给了它一个类型 `Rendering`。
- **Duplicated Code（发现并修）**：新写的 `playKyoku` 与「断电演习」那条用例里的循环一字不差，
  已把后者改成调 `playKyoku refused`（断言未动）。
- **Feature Envy（判断题，留着）**：`Rendering.applyTo` 读写的是 `LlmSeat` 的字段。
  没有把它挪进 `Agent.fs` 的 `LlmSeat`：那一层不该知道「局」这个边界——它是牌桌的概念。
- 一处**格式上的不齐**（fantomas 的决定，非人手）：`LlmSeat.edit` 的 `Provider` 分支因为字段值是
  一次函数调用，被 stroustrup 风格拆成了四行，与邻居的单行 `{ seat with Model = value }` 不齐。
  `fantomas` 是格式的唯一权威，照它的输出留着。

## 6. 留给人的

- 面板上那句「下一局生效」是**告知**不是**阻拦**。真要更硬（禁用那两格）随时可以改成锁，
  这次按票面选了「接受编辑 + 明确告知」——开局前填人格是常事，锁住会挡住它。
- 渲染版本那一行只印**最近一手**的。四家 LLM 同桌（M2）之后它要变成一席一行，
  那时应当与思考气泡一起排（票 44 的布局）。
