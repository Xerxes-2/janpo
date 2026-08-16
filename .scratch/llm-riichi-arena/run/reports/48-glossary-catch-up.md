# 48 — 术语表收下 M1 攒的四处新词

**状态**：done　**change**：`kmmmykwu`（本票一 commit）　**工作区**：`janpo-ws-c`
**fixed point**：`452f9a39`（change `otpoqntn`）

一句话：`CONTEXT.md` 六处按主人 2026-08-17 第七次裁决落地——新增 `PromptTemplate` / `Persona` /
`RenderVersion` / **打码**四条，`Replay` 条目加一句 `Replayed`，`Danger` 条目收下「无依据」并写明
**它 ≠ 日麻的「無筋」**；`./scripts/ci.sh` **EXIT=0**。

**只改了 `CONTEXT.md` 一个文件**（外加本报告、票文件、`DECISIONS.md`）。代码一行没动——
核出的两处「代码用法与词条不一致」列在 §3，按票面**只报不改**。

---

## 1. 六处的最终文本（全文）

### 1.1 新增 `PromptTemplate（提示模板）`（放在 `ScaffoldTier` 与 `Fallback` 之间）

> **PromptTemplate（提示模板）**：
> prompt 三段里**全部可替换的槽位**：模板 id、人格、规则说明（system 那一段）、八项段落抬头
> （含重试那一节的收尾）与五张措辞表。
> 有了它 prompt 是**数据不是代码**——换措辞是改座位配置，不重编、不动渲染器；它是**座位级**的，
> 同一个模型跑两份措辞不必碰代码。
> 槽位切在**段落**这一级：抬头、人格、规则说明与措辞表归模板，段落**内部**那几行（`牌河：…`、`刚摸进：…`）
> 仍归渲染器。它、Persona 与 ScaffoldTier 是**三个独立维度**，各占一个字段，谁也不缠进谁的枚举：
> 模板换措辞、人格换风格、档位换给不给那几个算好的数。改模板任何一个字都换一个 RenderVersion，
> 也就废掉那一局的可缓存前缀。
> _Avoid_: 把三个维度并成一个「风格预设」枚举；模板引擎（段落内部不插值，见上）

### 1.2 新增 `Persona（人格）`（紧随其后）

> **Persona（人格）**：
> 座位级的一段**自由文本**，排在固定 preamble 的最前面（system 消息里人格在前、规则说明在后），
> 留空就是没有人格。
> 它是 PromptTemplate 的一个字段，但在配置面板上单独占一格——人格最常改，不该逼人去写一段 JSON。
> **一局内不变**：它在可缓存前缀里，打到一半换人格等于把这一局攒下的 provider 缓存全废，
> 还让同一局面的对照多出一个变量。它与 ScaffoldTier 是两个维度（换风格不换给多少信息），
> 与 PromptTemplate 的差别在于改的是哪一层：人格是这一席的一句话，模板是整套措辞。
> _Avoid_: 人设、角色扮演设定；也别拿它当强弱旋钮——它只换措辞，不换给多少信息

### 1.3 新增 `RenderVersion（渲染版本）`（放在 `Cacheable Prefix / Tail` 与 `Usage` 之间）

> **RenderVersion（渲染版本）**：
> `模板 id@内容哈希`，进 DecisionRecord，并与座位一起做 Paifu 里那几份 preamble 的键。
> 看见缓存命中率掉下来时，它是一眼归因到「换了模板或人格」的那个字段。**它是算出来的，不是手填的**：
> 没有任何东西保证改措辞的人会去 +1。
> **它今天只覆盖模板的内容**（人格、规则说明、抬头、五张措辞表）——**渲染器代码改了它不变**，
> 而改渲染器同样换掉了前缀的字节。所以它是「模板变没变」的证据，**不是**「这一手的 prompt 逐字节是哪一份」的
> 完整版本号；把另一半补上（或如实缩小它的承诺）是一件挂着的事。
> _Avoid_: 把它读成 Paifu 的格式版本（那是 `Paifu.Version`）；也别拿它当「缓存一定命中 / 一定没命中」的判据

### 1.4 新增 `打码（Redaction）`（放在 `Usage` 与 `Thinking Bubble` 之间）

> **打码（Redaction）**：
> 可分享物离开 Agent 层之前，把这一席座位配置里的两样**字面量**从每个字符串里抹掉：API key 与端点地址
> （baseUrl 那一格里填着什么就抹什么，不按 provider 分叉），换成 `[API key 已打码]` / `[端点地址已打码]`。
> 起因是 provider 的报错原文会原样流进 DecisionRecord、页面上那句提示与下一次重试的 prompt，
> 而 Paifu 是唯一的可分享物（ADR-0002）。
> **抹的是确切的字面量，不猜「像 key 的正则」**（猜会漏，也会误伤模型说的话）；
> **只在 Agent 层的出口做一处**，不在每个消费点各打一遍——那种做法漏一个就等于没做，而消费点会长出来。
> 代价说在明处：牌谱里存的 prompt 尾部是打码后的那一份，与真发出去的那一次差这几个字。
> _Avoid_: 脱敏、加密、掩码；尤其别与**掩蔽**（MaskedEventStream）混为一谈——那条藏的是他家的牌，这条抹的是自己的密钥

### 1.5 `Replay` 条目加一句（**没有单开 `Replayed` 词条**）

> **Replay（回放）**：
> 对 Paifu 事件流的前缀做 fold 得到 GameState。回放不是另一套代码路径，就是引擎本身。
> `Replayed`（回放产物）是这个 fold 的**结果值**：已经收进 Game 的那几局，加上还没打完的那一局——
> 形状与牌桌相同，因此导入回放直接拿它摆桌。

（前两句是原文，一字未动；新增的是后两行。）

### 1.6 `Danger` 条目收下「无依据」

> **Danger（危险度）**：
> 基于 Genbutsu、Suji、Kabe 与宝牌周边的规则化安全度排序。第一版是启发式，不是统计模型；
> 其输出文案直接进入 prompt，措辞必须与本表一致。
> 排序的第四档叫**「无依据」**（`DangerTier.NoEvidence`）：现物、筋、壁三条都不成立。
> **它是「没有安全依据」，不是「一定危险」**，档位之间也没有倍率。
> **它不等于日麻的「無筋」**——無筋只说数牌（字牌无筋可言），而无依据对字牌照样成立，且它把壁也算作依据。

（第一段是原文，一字未动；新增的是后三行。）

---

## 2. 措辞上的几处斟酌（语义按裁定，没有改）

1. **`RenderVersion` 里不写票号**：`CONTEXT.md` 通篇只引 ADR 与模块名，不引 `.scratch/` 的票
   （票是临时物，术语表不是）。因此「票 43 在处理」写成「把另一半补上（或如实缩小它的承诺）是一件挂着的事」。
   局限本身**写死在词条里**，读者不会把它当完备版本号。
2. **「八项段落抬头（含重试那一节的收尾）」**：`Labels` 是 8 个字段，但其中 `retryTail` 是段尾不是抬头。
   报告 31 §一写的是「8 个抬头」，术语表里按实际形状说准。
3. **打码写「端点地址」而不是「自定义端点的 baseUrl」**：`redact.ts` 明写**不按 provider 分叉**——
   那一格里填着什么就抹什么。词条照实。
4. **打码的 _Avoid_ 点名了「掩蔽」**：仓库里已经有一个 Masking 语义（`MaskedEventStream`），
   两件事都在「不让某些字节出去」这条线上，最容易混。
5. **`PromptTemplate` 与 `Persona` 放在「座席与选手」节**（挨着 `ScaffoldTier`），
   `RenderVersion` 与 `打码` 放在「牌谱与回放」节：前两个是**座位级配置**，后两个是**可分享物**的事。
   三个维度因此在文件里连着出现，互相指认的那几句读得下来。

---

## 3. 核对：这四个词在代码 / prompt / 页面上的实际用法

核了这些地方：`web/src/agent/template.ts`、`prompt.ts`、`loop.ts`、`redact.ts`、`types.ts`、`ask.ts`、
`decide.ts`、`web/scripts/print-prompt.mjs`、`verify-redaction.mjs`、`src/Janpo.Engine/Paifu.fs`、
`Danger.fs`、`Replay.fs`、`src/Janpo.Web/Agent.fs`、`TablePage.fs`、`PaifuCheck.fs`、
`docs/host/custom-endpoint.md`，以及 `tests/` 与 `web/tests/` 里提到这几个词的用例。

### 3.1 不一致（**只报，未改代码**）

**① `Persona`「一局内不变」今天没有任何东西守着，而牌谱格式是按「可以中途换」设计的。**

- `web/src/agent/loop.ts:190` 每一手都 `resolveTemplate(request.seat)` 重解一次模板与人格
  （同一手里的重试共用，跨手不共用）。
- `src/Janpo.Web/TablePage.fs:1016` 的 `areaField` 两格（`table-llm-persona` / `table-llm-template`）
  **对局进行中照样可编辑**，没有 `disabled`，也没有一句「改了会废缓存」的即时提示
  （旁边那段 `intro` 只说它们是另一个维度）。
- `src/Janpo.Engine/Paifu.fs:94-97` 的 `Preamble` 注释**明写**：「主持人打到一半换了人格，就多一条，
  而每条决策记录靠自己的 `RenderVersion` 指得回当时那一份」——即数据格式是**为中途换人格准备的**。

  判断：术语表现在把「一局内不变」定成规范，代码**不违反**它（它只是不执行它）；牌谱格式的宽容是好事
  （中途换了也审计得回去）。**要不要在 UI 上挡住或至少提示，是另一张票**。这里不改。

**② 三处代码注释把 preamble 说成「整场不变」，比裁定的「一局内不变」更强，且与 ①里 `Paifu.fs` 自己的注释打架。**

- `src/Janpo.Web/Agent.fs:69`（`AgentAnswer.Preamble`）：「**整场不变**，牌谱里存一次」
- `web/src/agent/types.ts:180`（`DecideResponse.preamble`）：「**整场不变**，牌谱里存一次」
- `web/src/agent/ask.ts:18`：「整场逐字不变」

  三处说的都是「今天这一场里它没变过」，不是不变式；`Prompting.Preambles` 是**一个列表**，
  正因为它可能不止一份。术语表取的是 M2 对照实验真正要的那条下限（**一局内**不变）。
  改注释是代码改动，本票不做。

### 3.2 核过、一致的部分

- **`PromptTemplate`**：`template.ts:66` 的接口就是 `id` / `persona` / `system` / `labels`（8 项）/
  `wording`（5 张表）；F# 侧 `LlmSeat.Template` 只当字符串搬运、不判读（ADR-0005）；
  `resolveTemplate` 里**人格那一格优先于 JSON 里的 `persona`**，与词条「单独占一格」一致。
  段内那几行确实仍在渲染器里（`prompt.ts` 的 `boardLines` / `handLines` 等），词条的「切在段落这一级」属实。
- **三个维度**：`prompt.ts:387`、`types.ts:138`、`Agent.fs:40` 三处注释与 `AgentTests`
  「人格与档位两个维度」、`template.test.ts` 的人格维度用例都与词条一致；没有任何地方把三者合成一个枚举。
- **`RenderVersion`**：`template.ts:253` 的 canonical 串只由模板字段拼成（人格 + system + 8 抬头 + 5 张表），
  **确实认不出渲染器代码的改动**——词条写明的局限与实现逐项对得上；
  `Paifu.preambleFor` 按「座位 + 渲染版本」查，`PaifuCheck.fs:98` 逐手重建也按它。
  （`TablePage.fs:1147` 那句「改它们 = 废掉那一局的缓存（渲染版本号会跟着变）」对模板/人格成立，
  对渲染器代码改动不成立——这正是词条要挡住的那半句，注释本身没说错。）
- **打码**：`redact.ts` 的两个常量、`loop.ts:167` 的单点出口、`verify-redaction.mjs` 里**故意各写一份**的
  同样两个常量、`docs/host/custom-endpoint.md:142` 给用户的说法，四处措辞完全一致；
  CI 里那道闸门这次也跑过了（阳性对照与反向自证都绿）。
- **`Replayed`**：`Replay.fs:25` 就是 `Game` + `Current: GameState option`，`Replayed.events/result` 两个取值器，
  与词条一字对得上。
- **「无依据」**：`Danger.fs` 的 `DangerTier.NoEvidence` → wire `no_evidence`、显示「无依据」；
  `Danger.fs:24` 自己就写着「**它不是「危险」的度量**——它是没有依据」，与词条同义。
  prompt 里出现的形态是 `无依据` / `无依据 —— 宝牌 7p 周边`（`prompt.test.ts:178-182`）。
  **仓库里没有任何地方使用「無筋」**，因此新加的那句只是防将来有人这么读，没有既有文案要改。

### 3.3 两条 nitpick（不是不一致，记着）

- `tests/Janpo.Engine.Tests/PaifuReplay.fs:41` 有一个**同名但无关**的测试内部 DU case `| Replayed of ...`
  （第三方牌谱差分用的 `KyokuReplay`）。作用域只在那个文件里，不与引擎的 `Replayed` 类型冲突，
  但以后有人搜这个词会先撞见它。
- 页面上仍然看不到渲染版本号（只在牌谱与 `print-prompt.mjs` 里）——31 §7.4 已挂账的那条，与词条无关。

---

## 4. 验证

- `./scripts/ci.sh` **EXIT=0**：fantomas --check、风格闸门、Fable 依赖白名单、dotnet 全量测试、
  Biome、tsc、Agent 层用例、浏览器内曳光弹对拍、黄金用例、牌谱导出与回放、打码闸门（含反向自证）。
- **代码零改动**：`jj diff --stat` 只有 `CONTEXT.md` + 本报告 + 票文件 + `DECISIONS.md`。
  风格闸门扫的是 `src/**` 与 `tests/**` 的 F#，本票没有触及它的输入。
- 词条里出现的每一个标识符（`DangerTier.NoEvidence`、`Paifu.Version`、`Replayed`、`MaskedEventStream`）
  都在仓库里存在，逐个 grep 过。

## 5. 两轴 code review（fixed point `452f9a39`，无法派生 sub-agent，顺序自跑）

**Standards**：本票没有 F# / TS 代码改动，`docs/agents/fsharp-style.md` 不适用。
文档轴按 `CONTEXT.md` 既有体例核：四条新词条都是「**粗体词（中文）**：定义 + 关系 + _Avoid_ 列表」，
与相邻词条同形；两处扩写都是往既有段落后面追加，原文一字未动。没有自创格式、没有小标题、没有列表符号。
**通过。**

**Spec**（票 `48-glossary-catch-up.md` 的六条裁定 + 三条纪律）：

| 裁定 | 落点 | 结论 |
|---|---|---|
| `PromptTemplate` 新词条，三个独立维度 | §1.1 | ✓ 明写「它、Persona 与 ScaffoldTier 是三个独立维度，各占一个字段」 |
| `Persona` 新词条，坐席级、进 preamble、**一局内不变** | §1.2 | ✓ 「一局内不变」带理由（废缓存 + 多一个变量） |
| `RenderVersion` 新词条，**必须写明局限** | §1.3 | ✓ 「渲染器代码改了它不变」「不是完整版本号」 |
| 打码新词条，措辞参考 36 §9.5 | §1.4 | ✓ 采纳那段建议措辞并补上「只做一处」与代价 |
| `Replayed` **不单开**，往 Replay 加一句 | §1.5 | ✓ 没有新词条，只加两行 |
| 「无依据」收进 Danger，**标明 ≠「無筋」** | §1.6 | ✓ 并写明理由（無筋只说数牌） |
| 照体例、含 _Avoid_ | 四条新词条 | ✓ 四条都有 _Avoid_ |
| 不许顺手改别的词条、不补 M1 之外的词 | 全文 | ✓ `jj diff` 只有这六处，其余一个字未动 |
| 收词后核用法，不一致只报不改 | §3 | ✓ 两条不一致已列，代码零改动 |

**通过。** 无 blocking 项。

## 6. 留给人的

1. **§3.1 ①**：要不要在对局进行中**锁住**人格/模板那两格（或至少提示「改了会废掉这一局的缓存」）。
   术语表现在说「一局内不变」，而 UI 没有任何东西守着它。M2 跑对照实验时这是会出事的一处。
2. **§3.1 ②**：三处「整场不变」的注释与 `Paifu.fs` 的「打到一半换了人格就多一条」对不上，
   建议顺手统一到「一局内不变，跨局可换（牌谱按座位 + 渲染版本存多份）」。**是代码改动，本票没做。**
3. `RenderVersion` 那条局限一旦被票 43 补上，词条里「把另一半补上……是一件挂着的事」那半句要跟着改
   （届时又是一次 `CONTEXT.md` 例外）。
