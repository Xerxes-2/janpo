# 31 — prompt 降为数据、牌谱只存尾部、术语表追上新形态

**Status:** done　**fixed point:** `984d6e89`（change `urvpnmlu`）　**工作区:** `janpo-ws-a`

## 一句话

29b 把 prompt 的**形态**摆对了，这一票把它从**代码**降成**数据**：三段各自成为可替换的槽位
（人格 / 措辞 / 抬头全从座位配置注入，**改配置不改代码**），固定 preamble 挪进 system 消息，
牌谱从「每手一整份 prompt」瘦成「每手只存尾部」（**Bare 档省 69%**，格式版本 1 → 2），
术语表补上 29a/29b 立起来的六处。

```
① 人格 + 规则与读法   ← system 消息（票 31 新）　┐ 可缓存前缀
② 【到目前为止你看到的】掩蔽事件流，append-only  ┘ ← user 消息前半
③ 【现在】+【可选动作】+（脚手架）+（重试原因）    ← 尾部，**牌谱只存这一段**
```

## 交出去的接口（27 号验收直接用）

| 想要什么 | 怎么取 |
|---|---|
| 这一手真发出去的两条消息 | `promptMessages(decision, tier, note, template?) : { system; user }` |
| 牌谱里存的那一段 | `promptTail(decision, tier, note, template?)` |
| 从牌谱重建当时那两条 | `rebuildMessages(preamble, decision, tail, template?)` |
| 可缓存的那一段 | `cacheablePrefix(decision, tier, template?)` = ① + `"\n\n"` + ② |
| 整份全文（① + ② + ③） | `renderPrompt(...)`，前缀属性与打印脚本按它说话 |
| 模板本身 | `DEFAULT_TEMPLATE` / `readTemplate(json)` / `resolveTemplate(seat)`（`web/src/agent/template.ts`） |
| 渲染版本号 | `renderVersion(template)` = `模板 id@内容哈希`，**算出来的** |
| 措辞表 | `DEFAULT_WORDING` + `wordsFor(wording, viewer, others)`（`wording.ts`） |
| 牌谱里的前置 | `Paifu.Prompting = { Tools; Preambles }`，`Prompting.preambleFor seat version` |
| 座位级的两格配置 | `LlmSeat.Persona` / `LlmSeat.Template`（wire 上是 `persona` / `template`） |

## 一、prompt 降为数据

`PromptTemplate` 五项：`id`、`persona`、`system`、`labels`（8 个抬头）、`wording`（5 张措辞表）。
**槽位切在段落这一级**：抬头、人格、规则说明与措辞表是数据；段落**内部**那几行
（`牌河：…`、`刚摸进：…`）仍是渲染器的事——再往下切就要把渲染器变成模板引擎，
而那几行的形状与决策包字段一一对应，换措辞的人真正想换的是抬头、称呼与人格（记 31-2）。

**三个维度互不相干**：档位（`ScaffoldTier`，只动尾部那一节）、人格（一段自由文本，在 system 里）、
模板（措辞本身）。三者各一个字段，谁也没缠进谁的枚举。用例
`人格 × 档位：两个维度各管各的，四种组合可分解` 钉住这条。

### 一份自定义模板 / 人格的例子（改配置不改代码）

页面配置面板多了两格（`table-llm-persona` / `table-llm-template`，都存 localStorage）。
填这两格：

```
人格：       你是一位以防守见长的雀士，宁可少和一把，也不点炮。说话简短。
prompt 模板：{"id":"shibori","labels":{"history":"【战况回放】"},"wording":{"naki":{"pon":"碰！"}}}
```

不改一行代码、不重编，prompt 当场变成（`node web/scripts/print-prompt.mjs --persona … --template …` 打出来的）：

```
档位 bare　渲染版本 shibori@324e1567　…　1175 字

你是一位以防守见长的雀士，宁可少和一把，也不点炮。说话简短。      ← 人格，system 消息最前面

你在打日本立直麻将（天凤规则，四人东）。现在轮到你做决策。
…（规则说明照旧，没给就沿用默认）

【战况回放】                                                  ← 抬头换了
开局：东1局 0 本场，…
…
你 第1巡 碰！ 对家打的 3m（亮出 3m 3m）                        ← 措辞换了，**前缀与尾部同一套**
```

`print-prompt.mjs` 走的就是座位配置那条路（`resolveTemplate`），因此**页面上填什么，这里就看得到什么**。

**给主持人的一句提醒**（写进报告不写进代码）：默认 `system` 那段读法里点名了
`【到目前为止你看到的】`。只换 `labels.history` 不换 `system`，读法与抬头就对不上——
要换抬头就顺手把 `system` 也换掉。

### 读不动的模板

不是 JSON、不是对象、单项类型不对 → **那一项退回默认**并往 console 说一句，不把这一手卡死
（与 `readScaffold` / `Thinking.ofWire` 同一个方针）。退回之后渲染版本号仍是 `janpo-default@…`，
因此牌谱里看得出「这一手用的不是你以为的那份模板」。

## 二、system 槽位与它对缓存边界的影响

固定 preamble 进 `Context.systemPrompt`，user 消息只剩历史 + 现况 + 动作。
`cacheablePrefix` 的定义没变（① + `\n\n` + ②）：provider 的前缀缓存吃的是**整份请求的开头**，
而 system 就排在最前面，因此前缀属性照样是它该断言的那件事。

### 真跑的账单（DeepSeek `deepseek-v4-flash`，Bare 档，种子 2088，座位 1）

`JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-export.mjs --llm --turns N`。
key 只从文件读、只经 `addInitScript` 注入 localStorage，**不进代码、不进固件、不进提交**。

| | 29b（前缀在 user 里） | 31 跑 A（`--turns 60`） | 31 跑 B（`--turns 80`） |
|---|---:|---:|---:|
| 那一座位打了几手 | 21 | 16 | 22 |
| prompt token 合计 | 38,637 | 27,734 | 42,821 |
| 其中命中缓存 | 18,560（**48%**） | 12,800（**46%**） | 23,168（**54%**） |
| 末手命中率 | 67% | 65% | 53%（第 20 手 66%） |
| 首手命中率 | 21% | 0% | 97%（**跨次预热**，见下） |

**逐手曲线同形**（跑 A）：0% → 65%，中间三手掉回 22%–33%。

**要按同样的手数比才有意义**——命中率随手数上升，拿 16 手的数去比 21 手的数是自己骗自己：

| 同样取**前 16 手** | 29b | 31 跑 A |
|---|---:|---:|
| prompt token | 26,974 | 27,734 |
| 命中 | 11,008（**41%**） | 12,800（**46%**） |

| 同样取**前 21 手** | 29b | 31 跑 B |
|---|---:|---:|
| prompt token | 38,637 | 39,956 |
| 命中 | 18,560（**48%**） | 21,632（**54%**） |

**结论：把固定 preamble 挪进 system 消息没有伤到缓存，同手数下略好（41%→46%、48%→54%）。**

**三件要诚实的事**：

1. **跑 B 的头三手命中 91%–97%，那是跨次预热，不是本票的功劳**：跑 B 在跑 A 之后三分钟，
   同一个种子 ⇒ 开局那几行逐字相同，DeepSeek 那边还热着。跑 A（冷启动）头两手是 0%。
   这正是 29b-9 说的「命中率是统计量」——**单次跑的绝对值下不了结论，要看它随手数怎么走**。
2. **两次跑的手数不同是模型自己走出来的**：同种子同模型，模型的选择不同 ⇒ 牌局分岔 ⇒
   那一座位被问到的次数不同。所以上面按手数截断的两张表才是可比的那一对。
3. **命中的 token 是 128 的整数倍**（512 / 896 / 1024 / …），29b 记的「恒是 256 的整数倍」不确切
   （29b 自己的表里就有 896）。块大小是 provider 的实现细节，不是我们能控的。

## 三、牌谱只存尾部（格式版本 1 → 2）

`DecisionRecord` 的变化：

| 旧（v1） | 新（v2） | 为什么 |
|---|---|---|
| `Prompt`：整份 prompt | `PromptTail`：只有尾部 | 前缀是 (事件流 + 座位 + 模板) 的派生物，事件流就在同一份牌谱里 |
| `Tools`：每手一整份 schema | `ActionIds: int list` | 那一份里唯一随手变的就是 id 集 |
| — | `RenderVersion` | 「这一手用的是哪一版模板」，也是取 preamble 的键 |

`Paifu` 多一段 `Prompting = { Tools; Preambles }`：工具定义的**形状**（enum 留空）整场一份，
`Preambles` 按「座位 + 渲染版本」去重——一场里换了人格就多一条，没换就整场只有一条。
**重建**：`rebuildMessages(preambleFor(seat, version), decision, tail)` ≡ 当时真发出去的那两条消息，
逐字节（`prefix.test.ts` 里 12 手 × 2 档 × 有无重试 = 48 组，全部 `deepEqual`）。

### 瘦身前后的字节数

真数据（`decision-sequence.json`，同一局连续 12 手；「旧存」= 全文 + 每手一份工具定义，
「新存」= 尾部 + 那一手的 id 集）：

| 档位 | 旧 | 新 | 省 |
|---|---:|---:|---:|
| Bare | 23,310 字 | 7,315 字（含整场一份的前置 763 字） | **69%** |
| Assisted | 36,875 字 | 20,880 字（同上） | **43%** |

逐手看更清楚（Bare）：第 1 手 1,417 → 472 字，第 12 手 2,494 → 661 字。
**旧的那份随手数线性增长，新的近似恒定**——这正是票面说的「快照式下只是浪费，事件流式下是 O(n²)」。

真跑一局的实测（跑 B，22 手）：导出 35,988 字节，其中尾部合计 13,217 字，preamble **1 份** 395 字，
工具形状 **1 份** 368 字。旧格式下这两样要各存 22 份，而每份前缀还得再加上那一手的整段历史。
（这一份牌谱里没存前缀，因此旧格式的确切字数算不出来了——上面那张表是在 12 手固件上量的，那里两种格式都算得出来。）

### 旧版本怎么读

- `Paifu.supported = [1; 2]`，v1 照样读得动：`prompt` 读进 `PromptTail`，`render_version` /
  `action_ids` / `prompting` 缺省成空。
- **编码器按牌谱自己那个版本号写**（`recordEncoderFor`）：v1 读进来写出去仍是 v1，
  写的仍是 `"prompt"` 键、不写 `prompting`。**不把当年那份整文重标成「尾部」**——
  那是把一个谎写进可分享物。两条用例钉着（`版本 1 的牌谱照样读得动` / `版本 1 读进来再写出去仍是版本 1`）。
- 回放与往返全绿：`PaifuExportTests` 六条、`ReplayTests`、浏览器内 `verify-export`（真下载 → fold 回去）。

### 渲染版本号（收 29b-A）

`renderVersion(template)` = `模板 id@FNV-1a(模板内容)`，内容含人格与全部五张措辞表，键排过序，
**跨进程稳定**（有一条用例把**测试里写死的那份模板**的值钉死 —— 钉默认模板会让「改一句默认措辞」
变成一次 CI 红，而票面明写「版本变了不需要 CI 变红」）。
改任何一个字就换一个值；只改 `id` 则前半截变、后半截不变（「同一份措辞的另一个名字」看得出来）。

**运维含义（写进 DECISIONS 与 CONTEXT）**：**改渲染 = 废缓存，而 CI 不会因此变红**。
前缀属性只保证同一次运行内单调，不保证跨版本一致。M2 看命中率掉时，
`DecisionRecord.RenderVersion` 就是一眼归因的那个字段。

## 四、术语表（`CONTEXT.md` 六处，其余一个字未动）

改动全文如下（新增三条、重写一条、扩写两条）。

**1. 重写 `Observation Projection`**：

> 从 GameState 得到某座位 Observation 的那条路：**全局状态 → 掩蔽事件流 → fold → 观测**。
> 中间那一步是它的定义所在——投影不是「把 GameState 抹掉几个字段」，而是「重放那条该座位看得见的流」。
> LLM prompt 构建与真人 UI 渲染共用同一投影，隐藏信息的保护因此在结构上成立（他家的暗牌在类型里就不存在）。
> 旁观者的上帝视角是独立投影。

**2. 新增 `MaskedEventStream（掩蔽事件流）`**：

> 某座位**亲眼看得见**的那条历史：把引擎产出的 Event 流逐条按该座位的视角掩蔽（他家摸进的牌面写成 `?`，
> 配牌只留自己那一手）之后剩下的事件序列。它是该座位**唯一**的信息来源——观测由它 fold 得出，
> prompt 的可缓存前缀逐行渲染的也是它。掩蔽是一条法则（`MaskedEvent.forSeat`），不是每个消费点各自过滤。
> 只有编码器没有解码器：掩蔽流出得去、回不来。
> _Avoid_: 「日志」「历史记录」；也不要叫它 Paifu——Paifu 是上帝视角的完整事件流，掩蔽流是它的一个投影

**3. 新增 `Observation（观测）`**（「它 fold 到此刻的累加器」）：

> 掩蔽事件流**fold 到此刻的累加器**：自家手牌、四家的 Kawa 与 Naki、点数、宝牌指示牌、Junme、牌山剩余。
> 它与那条流是同一件事的两种形态（时间上的与空间上的），**不是两份互相独立的情报**——
> 尾部的场况必须逐字段等于前缀那条流数出来的场况，这条由引擎侧的构造性属性与 Agent 层的一致性用例两头守着。
> _Avoid_: 把它当成「局面快照」存进 Paifu（ADR-0002：状态是 fold 出来的，不是存下来的）

**4. 新增 `Cacheable Prefix / Tail（可缓存前缀 / 尾部）`**：

> prompt 里的一对**位置词**，不是两种内容。**前缀** = 固定 preamble（人格 + 规则说明，进 system 消息）+
> 逐行渲染的 MaskedEventStream：同一局里只往后加、既有的字节永不改写，因此 provider 的前缀缓存吃得到。
> **尾部** = 【现在】的 Observation + 可选动作 + 脚手架 + 重试原因：每手重算、每手付全价。
> **会被重算的量只能待在尾部**（此刻的巡目、牌山剩余、任何聚合数）；写在某一行上的历史时刻不算，
> 它写下去就不再变。**改前缀的措辞 = 废掉那一局的缓存**。
> _Avoid_: 拿它们指代「给什么信息」——那是 ScaffoldTier 的事

**5. 新增 `Usage（token 账单）`**：

> 一次问话的 token 用量，四个数都是 provider 报的：付全价的输入、输出（含思考）、**命中**前缀缓存的输入、
> **写入**缓存的输入。它是「可缓存前缀真的命中了没有」的唯一证据。**只存 token 不存钱**：单价随价表漂，
> 而 Paifu 是可分享物。命中率是**统计量**，单手的数下不了结论。
> _Avoid_: cost、花费、计费

**6. `ScaffoldTier` 核对后的两处校正**（判据那几段一字未动，只改末尾的位置说明）：

> 两种客观事实在 prompt 里的位置不同：事件序列（MaskedEventStream）在**可缓存前缀**里，append-only；
> 场况（Observation）在**尾部**，每手重算、每手付全价。
> **档位只动得了尾部**：两档共用同一份前缀，否则同一局面的对照就不是一个变量。
> 它与「人格」（座位级的风格文本，在 preamble 里）是**两个独立维度**，不得缠进同一个枚举。

顺带把 `DecisionRecord` 词条的一句话改成新形态（「只存尾部」+ 记 Usage 与渲染版本）——
这一条本来就在票面第三节的射程内。

## 五、顺手：截图脚本的写死端口

`shoot-table.mjs` 原来写死 4190 + `strictPort: true`，现在与四个 `verify-*.mjs` 共用
`serve.mjs` 的 `startDevServer` / `pageUrl`。**只动了端口那一处**，截图逻辑（走多少手、
拍哪个选择器、`--scan` 挑种子）一个字没改——票 32 正在用它重出图。
`--port` 那个 flag 撤了，钉端口改用 `JANPO_PORT=4190`（与其余四个脚本同一种写法）。

## 六、验证

- `./scripts/ci.sh` 全绿：fantomas --check、风格闸门、Fable 依赖白名单、**775 条 dotnet 测试**、
  Biome、tsc、**95 条 Agent 层用例**、浏览器内曳光弹对拍、黄金用例 40 条 2,069 字段、牌谱导出与回放。
- 新增测试：TS 侧 `template.test.ts`（9 条：默认模板、人格维度、措辞注入、读不动退回、版本号算得出）、
  `prefix.test.ts` 新增 5 条（自定义模板下的前缀属性、system 逐手不变、48 组重建、尾部只是尾部）；
  引擎侧 `PaifuTests` 新增 5 条（只存尾部、前置往返、去重、v1 读得动、v1 写回仍是 v1）、
  `AgentTests` 新增 2 条（人格与档位两个维度、人格模板原样不判读）。
- **黄金用例一字未改**：决策包的形状没动（模板是 Agent 层的事，引擎不知道它存在）。
- **`ask-*.json` 没有重录**（29b-10 的理由照旧：它们是「模型这么答过」的证据，与 prompt 形态无关；
  四条失败路径正钉在那几个答案上）。录制脚本本身跟着改成了两条消息，下次重录才录得对。
- 浏览器内 `verify-export` 多验两项：`preambles` 份数、**逐手重建得回去**（每条记录的
  `RenderVersion` 都指得回一份 preamble，指不回就是红）。

## 七、留给人的

1. **`PromptTemplate` / `Persona` / `RenderVersion` 三个词没进 `CONTEXT.md`**：本票获准改的六处
   是票面锁死的，没有它们。建议 M2 一并收（提案 31-A）。
2. **模板的 `system` 与 `labels` 之间有一处隐性耦合**：默认 `system` 里点名了
   `【到目前为止你看到的】`。改抬头不改 `system` 不会报错，只会让读法与正文对不上。
   要么把抬头也做成 `system` 里的插值（那会让模板更难写），要么就在文档里说清（现在是后者）。
3. **采样参数与多轮仍然没有**（M2，主人已裁）。
4. **人格与模板换了会废缓存，但页面上没有任何提示**。M2 若要做对照实验，
   建议在配置面板上把渲染版本号显示出来（现在只在牌谱与 `print-prompt` 里看得到）。
5. **那两格 textarea 没有自己的样式**：`.llm-panel input, .llm-panel select` 那条规则没包 textarea，
   因此字体与内边距走浏览器默认（能用，只是不归一）。**本票没碰 `web/src/styles.css`**
   ——票 32 正在改它（牌背隐形），同时改必撞。补一行 `.llm-panel textarea { … }` 就完事。

## 八、两轴 code review（fixed point `984d6e89`，无法派生 sub-agent，顺序自跑）

### Standards（`docs/agents/fsharp-style.md` + CONTEXT.md + ADR-0001/0002/0005）

**blocking：0。已修的两处**：

1. `resolveTemplate` 原来收整份 `SeatConfig`，实际只读两格 → 改成
   `Pick<SeatConfig, "persona" | "template">`，`print-prompt.mjs` 也因此不必编一份假座位。
2. `prompt.ts` 一度导出 `templateFor = resolveTemplate` 做转发 → 删掉，调用方直接 import
   `template.ts`（多一层名字不叫解耦，叫多一个要维护的名字）。

**judgement calls，只记录不改**：

- `Labels` 有 8 个字段，`readTemplate` 因此有 8 行 `text(labels, …, base.labels.…)`。
  写成表驱动会省 6 行，但会把「有哪几个抬头」从类型里挪进一个字符串数组——**类型是这里的文档**。留着。
- `wordsFor` 一次造 5 个闭包。它一手调一次（12 手一局也就 12 次），不是热路径。留着。
- `Prompting.add` 是 O(n²)（每条 preamble 扫一遍已有的）。n 是「一场里换过几次人格」，实测恒为 1。留着。

### Spec（票 `31-prompt-as-data.md`）

**缺失或只做了一半：0。** 一之二、三、三之二、四、四之二逐条对过，全部落地并有测试或报告数据。

**偏离票面 2 处（都记了 DECISIONS）**：

1. 票面第四节写「改动限于以下三处」，紧接着的清单是**六条**（收了 29b-B）；实际落成六处 +
   `DecisionRecord` 词条那一句（31-6）。
2. 票面说「记 preamble 与渲染版本号」没说记在哪；落成**牌谱级的 `Prompting`** 而不是每条记录各记一份
   ——每手记一份 preamble 就是把刚删掉的冗余换个名字再存一遍（31-4）。

**票面没要求但改了的 3 处**：

1. `README.md` 两句话过期了（「票 31 还没做」、「截图脚本因此另占一个端口」），顺手改准（31-9）。
2. `record-agent-fixtures.mjs` 改成两条消息——**不改它，下次重录就录成了别的 prompt**。
3. `verify-export.mjs` 多验「逐手重建得回去」：重建是本票的验收，而它是唯一在真牌谱上跑的关卡。
