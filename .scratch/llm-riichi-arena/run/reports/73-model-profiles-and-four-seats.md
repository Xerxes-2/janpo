# 73 — `ModelProfile`（模型档案）+ 四个座位各自选选手

**结论：`?table=1` 上现在是「四席各挑一个选手 + 一个命名档案库」。**「怎么问这个模型」
（provider·模型·key·baseUrl·超时·思考预算）收进一份 **`ModelProfile`**，座位按**名字**引用它；
「给多少信息 / 什么风格 / 哪套措辞」（脚手架·人格·模板）留在**座位**上。四家都能是模型，
一把 key 只填一次（界面上 key 只出现在档案编辑处）。`./scripts/ci.sh` **EXIT=0**，42.4s；
dotnet 744 + **164** 条（新增 32 条），浏览器闸门从十一趟变**十二趟**（新增 `verify-seats`，2.1s）。

**引擎、Agent 层与回放那一半一行没改。** `jj diff --name-only` 里没有 `src/Janpo.Engine/**`、
没有 `web/src/agent/**`、没有 `docs/adr/*`、没有 `web/public/demo-paifu.json`。

**真跑一局的证据**（不进 CI）：两席引用同一份档案、人格各不同，105 手全由 DeepSeek 决定、
**0 兜底**，牌谱里两条 preamble 只差人格那一行，`names` 两个 `provider/model`，
key 与档案名一个字节都不在牌谱里。详见 §6。

---

## 1. 形状：四个维度，四个格子

### 1.1 新类型（`src/Janpo.Web/SeatingPlan.fs`，346 行）

```fsharp
type ModelProfile = {                  // 「怎么问这个模型」——一份命名的档案
    Name: string                       // 本机的私人叫法。**不进牌谱、不过界给 Agent 层**
    Provider: string; Model: string; ApiKey: string; BaseUrl: string
    TimeoutMs: int; Thinking: Thinking
}

[<RequireQualifiedAccess>]
type SeatChoice =                      // 这一席交给谁
    | Bot of kind: Bot                 // 均匀随机 / 有主见
    | Profile of name: string          // 引用一份档案（**按名字**）

type SeatBinding = {                   // 一席的绑定：交给谁 + 座位级那三项
    Choice: SeatChoice
    Tier: ScaffoldTier; Persona: string; Template: string
}

type SeatingPlan = { Profiles: ModelProfile list; Seats: SeatBinding list }
```

出口（`SeatingPlan` 模块，票 74/76 与用例读的都是它们）：

```fsharp
SeatingPlan.initial  : Ruleset -> SeatingPlan          // 一份空档案 + 四家均匀随机
SeatingPlan.fit      : Ruleset -> SeatingPlan -> SeatingPlan   // 绑定条数对齐到座位数
SeatingPlan.roster   : Ruleset -> SeatingPlan -> Roster        // 推导出配桌
SeatingPlan.playerAt / bindingAt / profileAt / tryProfile / references / llmSeats / names
SeatingPlan.bind / editSeat / addProfile / editProfile / removeProfile / freshName
SeatingPlan.botsToDisplay                              // 状态线上那句话（票 42 的措辞照旧）
SeatingPlan.ofLegacy : Ruleset -> (string -> string option) -> SeatingPlan option  // 迁移
SeatBinding.config   : ModelProfile -> SeatBinding -> LlmSeat  // 六格 + 三格 → 发出去的那份
```

**为什么是「档案库 + 按名字引用」而不是「四份独立配置」**：票面裁的就是这个形态，
理由在实测里也成立——真跑那一局两席共用一把 key，**填一次**；两席各带各的人格，
于是同一局面里只差人格这一个自变量（M2 对照实验要的正是它）。

**`LlmSeat` 保留下来，降为「这一手真发出去的那份」**：它仍是跨界那段 JSON 的形状
（`Agent.seatEncoder` 一个字段没改，`web/src/agent/**` 因此一行没动），只是不再由面板直接编辑
——六格从档案来、三格从座位来，合成在 `SeatBinding.config` 一处。

**名字叫 `SeatingPlan` 不叫 `Seating`**：`Seating` 已经被 `TableBoard.fs` 占着（票 44 的渲染上下文，
四格：规则集 / 观测者 / 参照系 / 亲），而那个文件是票 76 的地盘。提案追加在 `DECISIONS.md`（73-1）。

### 1.2 引用为什么是「名字」而不是下标或 id

- **下标**：删掉库中间一份，后面每一席的引用都要跟着挪，错一次就静默地指到别人身上；
- **另造 id**：要多一套生成与存储，而这是一台浏览器里的一个本地面板；
- **名字**：人在面板上认的就是它。两处善后把悬空引用堵死——
  **改名时 `editProfile` 跟着改座位的指向**（不然改一个字就把那几席静默地踢回 bot），
  **删除时 `removeProfile` 明确把那几席退回均匀随机并交回名单**（页面照着名单说话）。
  剩下的一种（localStorage 被手改过、引用不到）退回均匀随机，牌桌照样推得动。

**已知的边角**：两份档案同名时，座位引用的是**头一份**（`tryProfile` 用 `List.tryFind`）。
没有拦重名——面板上正在改名的中间态本来就会短暂撞名。见 `DECISIONS.md` 73-2。

### 1.3 一局内不变：按座位各自成立

`LiveTable.Pinned` 从**一份** `Rendering option` 变成**每席一项**的
`Rendering option list`：座位 1 被问过话，不该把座位 2 的人格一并定死（那一席本局
可能还没开过口）。定型点仍是「那一席这一局的头一次问话」（`step` 里 `Seat.mapAt asked`），
`Restarted` / `KyokuAdvanced` 时四席一起松开。`renderingPending` 变成「四席里只要有一席欠着就算」。

**执行者指得出来**（判据 2）：定型在 `TableState.step`，比较在 `TableState.renderingPending`，
用例是 `TablePageTests.人格一局内不变按座位各自成立：定住一席不定住别席`（红-8 把它按红过）。

### 1.4 响应阶段仍旧串行（票面的边界）

`Table.decide` 一次只给一家，`Awaiting` 仍是**一条**，一句都没改。四家同桌时是
「问 → 落子 → 问下一家」。用例 `四家都是模型时仍旧一次只问一席：并发是票 74` 钉住它；
红-9 把「等回执时又问下一席」按红过（顺带把票 23 那条也按红了）。

---

## 2. localStorage：新键、迁移，与「只迁一次」的判据

| 键 | 值 |
|---|---|
| `janpo.profiles.count` | 库里几份（`"2"`）。**它同时是「新格式写过没有」的判据** |
| `janpo.profiles.<i>.name\|provider\|model\|base_url\|api_key\|timeout_ms\|thinking` | 一份档案 |
| `janpo.seats.<座位>.choice\|tier\|persona\|template` | 一席的绑定 |

`choice` 三种写法：`random` / `opinionated` / `profile:<档案名>`（冒号后面整段都是名字）。
**六个档案字段的键名与票 23 那一版逐字相同**（`name` 是新的）——迁移就是拿它们去老前缀
`janpo.llm.` 下面读一遍。

**迁移的判据（三条路，按顺序试）**：

1. `janpo.profiles.count` **在** → 照新格式读。（一份档案都没有时它是 `"0"`，仍旧在，
   因此「把档案全删光」不会被当成「还没迁过」。）
2. 不在、而老 `janpo.llm.*` 里**至少有一个键** → `SeatingPlan.ofLegacy` 迁一次，
   **当场把新格式写回去**（`Store.readSeating` 里唯一一处「读的时候写盘」），于是下一次打开走第 1 条。
   写回去这一步是被闸门逼出来的：不写的话，「人一次都没动过面板」这条路上老键会**每次**都赢
   （红-W3 就是这个样子）。
3. 两样都没有 → 默认那一份：一份空档案 + 四家均匀随机。

**老键只读不删**：迁移万一有 bug，主人那把真 key 还在原地。判据不靠它在不在。

**绑的是老配置选中的那一席，不是硬绑座位 0**（与票面措辞的差别，理由见 `DECISIONS.md` 73-3）：
老键 `seat` 存空串就是「四家都随机」（票 34 那道 key 闸门跑的正是这一档），
硬绑 0 会把「默认四家均匀随机」这个基准悄悄改掉，而票 42 的边界量的就是它。

---

## 3. 面板（`TablePanel.llmPanel`）

```
座位 0 [均匀随机][有主见][档案 1]  脚手架[裸奔▾] 人格[……] 模板[……]
座位 1 …  座位 2 …  座位 3 …
模型档案 [档案 1][新建档案][删掉这一份]
档案名[…] provider[deepseek▾] 模型[…] (baseUrl[…]) API key[••••] 超时[240000] 思考预算[不开▾]
（删过档案之后多一行：删掉了档案「X」：座位 0、1 本来引用着它，已退回均匀随机。）
```

两条硬判据都落成了可断言的东西：

- **key 只出现在档案编辑处**：`verify-seats` 数 `[data-testid^="table-seat-"] input[type=password]`
  必须是 0，且 `table-profile-key` 必须正好 1 个；
- **四席一眼看得全**：四行各自带 `data-seat-choice`（拨到哪儿）与 `data-seat-name`
  （在牌谱里叫什么），状态线上那条 `data-seats` 是四个名字的逗号串。

**截图我打开看了**（判据 7）：`docs/images/table.png` 重出（同一条命令、同一颗种子 1177、同 52 手）。
四行绑定 + 档案库摆在播放条与牌桌之间，key 那一格只有一处，四席的选择一屏看得全。
面板比从前高（每席的人格与模板各占一格），牌桌因此往下挪了一屏——README 那张图跟着重出了。

**新增的 testId**：`table-seat-{0..3}`（行本身）、`table-seat-{i}-random` / `-opinionated` /
`-profile-{n}` / `-tier` / `-persona` / `-template`、`table-profile-{n}` / `-new` / `-delete` /
`-name` / `-provider` / `-model` / `-base-url` / `-key` / `-timeout` / `-thinking` / `-notice`。
**消失的**：`table-llm-none` / `table-llm-{seat}` / `table-bot-{kind}` / `table-llm-provider` /
`table-llm-model` / `table-llm-key` / `table-llm-timeout` / `table-llm-thinking` / `table-llm-tier` /
`table-llm-persona` / `table-llm-template` / `table-llm-base-url`。
`table-llm-panel` / `table-render-version` / `table-llm-custom-note` 照旧。

---

## 4. 闸门

### 4.1 新那一趟（`web/scripts/verify-seats.mjs`，第十二趟，2.1s）

**两个本地假端点**（一个照常答话、一个 `--fail 401`），**一个字节都不出网**，因此它进 CI。
一份档案坐两席（人格各不同）、第三席引用「坏 key 的那一份」、座位 3 是 bot，打完一局：

```
座位 0：profile:能答话的那一份 → custom-openai/fake-model
座位 1：profile:能答话的那一份 → custom-openai/fake-model
座位 2：profile:坏 key 的那一份 → custom-openai/broken-model
座位 3：random → random
走了 82 手　座位 2 兜底代打：provider 报错：401 …（没有重试）　这一桌已兜底 22 手
牌谱：names custom-openai/fake-model / custom-openai/fake-model / custom-openai/broken-model / random
  座位 0 的 preamble：janpo-default@fbd504be.4b9e57c0　416 字　含自己那句人格 = true
  座位 1 的 preamble：janpo-default@f2f60986.4b9e57c0　416 字　含自己那句人格 = true
  决策记录 / 兜底：座位 0 21/0　座位 1 21/0　座位 2 22/22　座位 3 0/0
删掉「能答话的那一份」之后页面说：……座位 0、1 本来引用着它，已退回均匀随机。
重新打开这一页：四席仍是「random,random,custom-openai/broken-model,opinionated」
老配置迁移那一程：四席 random,random,deepseek/deepseek-v4-flash,random；
  新键里的档案：档案 1　超时 123000　思考 medium；座位 2：profile:档案 1　脚手架 assisted　人格「…」
  拨回均匀随机再打开一次：random,random,random,random
```

逐条断言：`names` 三个 `provider/model` + 一个 `random`；导出物里没有档案名、没有 key、没有 baseUrl；
两席的 preamble 正文不同、各含自己那句人格；三席各有决策记录、座位 3 一条都没有；
坏 key 那一席**每手**兜底而另两席 0 手；删档案有 notice 且四席跟着退回；四席落 localStorage；
老配置迁得过来且只迁一次。

**「渲染版本相同」那条断言我改对了一次**（判据 6：新断言第一次报红先怀疑断言自己）。
第一次跑它红在「座位 0 与 1 的渲染版本不同」——查 `render-version.ts` 才发现
`templateDigest` 把 `template.system` 算了进去，而**人格就排在 system 里**。
票面的原话是「渲染版本**相同那一截**（模板没换）」，于是断言改成拆三截：
**模板 id 与渲染器摘要那两截必须相同**（自变量只许有一个），**模板哈希那一截必须不同**
——后者是阳性对照：人格哪天被挪出可缓存前缀，这一条会当场红，而那正是「缓存命中率崩了」
要先知道的事。

### 4.2 既有闸门跟着改的地方

- **`verify-home` 的名单从 13 个变 17 个**，并且**新加了阳性对照**（判据 3）：
  点过去 `?table=1` 之后逐个查它们**必须都在**。没有这一步的话，写错一个 testId
  会让对应那条「首页上没有它」永远为真——红-W6 把这条按红过。
- `verify-board`：全局那个 bot 开关没了，改成逐席拨四次；`data-bot` 变
  `data-seats`（四个名字的逗号串），那条断言因此从「一个名字」变成「四席逐个核」。
- `verify-export` / `verify-redaction` / `verify-custom-endpoint` / `verify-llm-seat`：
  灌 localStorage 改走新键（共用 `web/scripts/seating.mjs` 一个入口，**键名只有这一份**）。
  `verify-export --llm` 与 `verify-llm-seat` 都多了 `--seats 0,1`。
- **`table-drive.mjs` 一行没改**：它按「上一手变了 / 单步灰了 / 引擎拒了」等落定，
  与几席是模型无关——三席模型同桌那一趟就是拿它走的 82 手。

### 4.3 key 那两道照旧绿

`verify-export --turns 40`（假 key 躺在 localStorage 里、四家仍是随机选手）、
它的反向自证（拌了 key 的导出物当场红）、`verify-redaction`（回显 key 的端点跑一手、
打码记号在）——三趟都在这一轮 CI 里绿着，一条断言没动。

---

## 5. 每条新断言先红一次（判据 1 的原始输出）

**全部实跑过，跑完逐个 `diff` 对回备份**（`/tmp/73bak/`，五个文件全部 OK）。

### 5.1 dotnet 侧（新增 32 条）

```
红-1｜座位的默认绑定不是均匀随机（`SeatBinding.initial.Choice` → Opinionated）
  SeatingPlanTests.默认坐法是四家均匀随机：既有闸门量的仍是它 [FAIL]
  SeatingPlanTests.四席各管各的：一席换成有主见，别的三席不动 [FAIL]
  SeatingPlanTests.老配置没选模型坐席：档案照建，四家仍是均匀随机 [FAIL]
  SeatingPlanTests.四家同一种 bot 时那句话一字未改，混着坐时逐席报 [FAIL]
  SeatingPlanTests.删掉一份还被引用的档案：那几席退回 bot，而且点得出是哪几席 [FAIL]
  TablePageTests.自带 bot 默认是均匀随机：默认视图那几道闸门量的仍是它 [FAIL]
  TablePageTests.删掉一份还被座位引用的档案：那几席退回 bot，页面把这件事说出来 [FAIL]

红-2｜人格与档位不跟着座位走（`playerOf` 把 Persona/Tier 抹成默认）
  SeatingPlanTests.同一份档案坐两席，两席各带各的人格与档位——key 只填一次 [FAIL]
  TablePageTests.兜底按座位自己那一档代打 [FAIL]
  TablePageTests.人格一局内不变按座位各自成立：定住一席不定住别席 [FAIL]
  TablePageTests.改过的人格在面板上看得见“下一局生效” [FAIL]
  失败: 4，通过: 160

红-3｜删档案时不报「哪几席被牵连」（`removeProfile` 恒返回 []）
  SeatingPlanTests.删掉一份还被引用的档案：那几席退回 bot，而且点得出是哪几席 [FAIL]
  TablePageTests.删掉一份还被座位引用的档案：那几席退回 bot，页面把这件事说出来 [FAIL]
    错误消息: Assert.Contains() Failure: Sub-string not found
  失败: 2，通过: 162

红-4｜改档案名不跟着改座位的指向
  SeatingPlanTests.改档案名不把座位踢回 bot：引用跟着改 [FAIL]　失败: 1，通过: 163

红-5｜迁移硬绑座位 0（不看老配置选的是哪一席）
  SeatingPlanTests.老配置没选模型坐席：档案照建，四家仍是均匀随机 [FAIL]
    错误消息: Assert.Empty() Failure: Collection was not empty
  失败: 1，通过: 163

红-6｜迁移时漏掉 key 那一格
  SeatingPlanTests.老配置迁成一份档案加那一席的绑定：一格都不许丢 [FAIL]
  SeatingPlanTests.老配置没选模型坐席：档案照建，四家仍是均匀随机 [FAIL]
  失败: 2，通过: 162

红-7｜页面上那行摘要印档案的名字（不是 provider/model）
  SeatingPlanTests.档案的名字不进牌谱：那一列恒是 provider slash model [FAIL]
  SeatingPlanTests.四个座位可以同时绑到档案上：这就是四 LLM 同桌 [FAIL]
  （另有 引用不到的档案 / 删掉一份 / 改档案名 三条跟着红）　失败: 5，通过: 159

红-8｜定型时把四席一起定住（不变量没按座位各自成立）
  TablePageTests.人格一局内不变按座位各自成立：定住一席不定住别席 [FAIL]
  失败: 1，通过: 163

红-9｜等回执的那段又问了下一席（并发是票 74，这一票不许改这个形态）
  TablePageTests.四家都是模型时仍旧一次只问一席：并发是票 74 [FAIL]
  TablePageTests.等回执的那段不再问第二次：同一手不许有两个请求在飞 [FAIL]   ← 票 23 那条
  失败: 2，通过: 162

红-10｜四席绑定没接到牌桌上（`step` 里改用 `Roster.allRandom`）
  TablePageTests 里 11 条一起红（分派 / 兜底 / 定型 / 断电演习 / 四席那几条）
```

**顺带一个更硬的结果**：红-9 的第一版改法（把「上一次问话还没回来就不再问」那条 case
直接删掉）**编译就红了**——`--warnaserror` 下 match 不完整是错误。也就是说
「同一手不许有两个请求在飞」有两道守：类型系统一道、用例一道。

### 5.2 浏览器侧（`verify-seats` / `verify-home`）

```
红-W1｜人格不跟着座位走（`playerOf` 把 Persona 抹成空串）
  座位 0 与座位 1 的 preamble 正文逐字相同：两席的人格没跟着座位走
  座位 0 与座位 1 的模板哈希相同（08fcaec3）：两席的人格不同，它却没进可缓存前缀——那条阳性对照塌了
  座位 0 的 preamble 里没有它自己那句人格：人格没跟着座位走
  座位 1 的 preamble 里没有它自己那句人格：人格没跟着座位走

红-W2｜四席绑定不落 localStorage（`writeSeating` 不写 choice）
  重新打开这一页，四席从「random,random,custom-openai/broken-model,opinionated」
  变成了「random,random,custom-openai/broken-model,random」：坐法没落 localStorage

红-W3｜迁完不把新格式写回去（迁移不是一次性的）
  老配置里那把 key 没迁进档案：主人的 key 会在改版里丢掉
  老配置的超时没迁过来 / 思考预算没迁过来 / 脚手架档位没迁到那一席上 / 人格没迁到那一席上

红-W4｜删掉还被引用的档案，页面一个字都不说
  删掉一份还被引用的档案，页面一个字都没说（不许静静地变成「没有选手」）

红-W5｜档案的名字漏进了牌谱（`SeatBinding.config` 把 Provider 换成 Name）
  牌谱的 names 是「能答话的那一份/fake-model,…」，该是「custom-openai/fake-model,…」
  导出物（文件名 + 字节）里出现了「能答话的那一份」：那是本机的东西，不该上路
  导出物（文件名 + 字节）里出现了「坏 key 的那一份」
  （另有 preamble 与兜底那几条跟着红）

红-W6｜档案编辑处那格 key 改了名（`verify-home` 那份名单的阳性对照）
  ?table=1 上没有 [data-testid="table-profile-key"]：那么「首页上没有它」那一条永远为真（空转）
```

**红-W2 的第一版是空转的，是我自己改硬的**（判据 3）：一开始它只在「删掉档案」之后重开页面，
可那几席的变化本来就是「档案没了」的连带结果——绑定压根不写盘，重开时它们也会因为
引用不到而退回均匀随机。改法是**再把座位 3 拨成有主见**（一个与档案无关的变化）再重开。
同一段还栽过一次：头一版用 `page.reload()`，而 `addInitScript` 每次导航都会把灌进去的那份
坐法**再写一遍**——量到的于是是「灌进去的那一份」。改成在同一个上下文里**另开一个页面**。

**还有一处是真 bug，不是断言写错**：兜底计数第一次数出「座位 0 也兜了 21 手」——
牌谱的编码器把空的 `fallback` 整个略掉，而闸门写的是 `record.fallback !== null`
（`undefined !== null` 恒真）。改成 `typeof record.fallback === "string"`。

---

## 6. 真跑一局（不进 CI，真 key）

**两席引用同一份档案、人格各不同**（`verify-export.mjs --llm --seats 0,1 --turns 200`，
key 从 `JANPO_KEY_FILE` 读，模型 `deepseek-v4-flash`，思考预算 off）：

```
模式：座位 0、1 交给 deepseek-v4-flash（思考预算 off）　先走 200 手
打了 3 局（还没终局）　走完之后：上一手：座位 2 手切8索
Agent 状态：座位 1 的模型选完了（1587 ms）：打出中（7z），它是三元牌中的单张……
牌谱：版本 3　事件 329 条　决策记录 105 条（带 thinking 0 条、**兜底 0 条**）　已打完 2 局
prompt：尾部共 62553 字　preamble 2 份　逐手重建得回去 = true
回放：事件流逐条相同 = true
  座位 0 的 preamble：janpo-default@fbd504be.4b9e57c0　416 字　头一句「你是座位 0 的雀士，第 0 号打法。」
  座位 1 的 preamble：janpo-default@f2f60986.4b9e57c0　416 字　头一句「你是座位 1 的雀士，第 1 号打法。」
  座位 0 的决策记录 52 条　座位 1 的决策记录 53 条
```

事后逐项核那份牌谱：

- `names` = `['deepseek/deepseek-v4-flash', 'deepseek/deepseek-v4-flash', 'random', 'random']`；
- **含 key = False，含档案名 = False**（档案叫「同一份档案」）；
- 两条 preamble 的差异**正好一行**：`-你是座位 0 的雀士，第 0 号打法。` / `+你是座位 1 的雀士，第 1 号打法。`；
- 兜底 0 手 ⇒ **key 真的从档案里传下去了**（两席共用那一份档案里填的一次）；
- 单手延迟中位数 **1873 ms**。

**墙钟**：218 s（200 手、3 局、105 次问话，两席串行）。
**账单**：输入 **215,222 tok**（其中缓存命中 98,176，命中率 **45%**）、输出 **12,452 tok**。

**断电演习那一档也真跑了一次**（坏 key，两席，不花 token）：
`verify-llm-seat.mjs --bad-key --seats 0,1` → 一局 15.8 s、46 手全兜底、46 次请求全 4xx、
对局照样打完、页面全程红着说 401（**没有重试**：认证失败那一类不重问，票 47）。

**超预算了，认账**：票面写的是「这一趟 ≤ 1 局」，我按 `--turns 200` 跑成了 3 局
（`verify-export` 的 `--turns` 是手数不是局数，两席串行时一局约 40 手，我没换算）。
花掉的是上面那笔账单，量级上是分币级，但判据是「先估规模再跑」（workbook 的资源预算），
这一次没估。见 `DECISIONS.md` 73-5。

---

## 7. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，42.4s；引擎 744 + 页面 **164** 条 |
| 浏览器十二趟 | `cd web && node scripts/verify-browser.mjs` | 全 ✓（新那一趟 2.1s） |
| 新那一趟单跑 | `cd web && pnpm run verify:seats` | 见 §4.1 |
| 每条新断言先红 | §5 的 10 + 6 次 | 全部红过，原文抄在 §5 |
| 真跑一局 | `verify-export.mjs --llm --seats 0,1` | 见 §6（0 兜底） |
| 断电演习（多席） | `verify-llm-seat.mjs --bad-key --seats 0,1` | 46 手全兜底，对局打完 |
| 截图 | `cd web && node scripts/shoot-table.mjs` | `docs/images/table.png` 重出，**打开看过**（§3） |
| 还原干净 | 逐个 `diff` 对回 `/tmp/73bak/` | 五个文件全 OK |

`jj diff --stat`：32 个文件，+2506 / −664。**新增三个文件**（`src/Janpo.Web/SeatingPlan.fs`、
`tests/Janpo.Web.Tests/SeatingPlanTests.fs`、`web/scripts/verify-seats.mjs`）与一个闸门助手
（`web/scripts/seating.mjs`）。

**没碰**：`src/Janpo.Engine/**`、`web/src/agent/**`、`docs/adr/*`、`web/public/demo-paifu.json`、
回放那一半（`replayTick` / `demoLoaded` / `TablePanel.replayControls` 一字未动）、
`web/index.html`、`web/src/styles.css`、`.github/workflows/`。

**与票 76 会撞的两处**（派工单点过名，照现状改了）：
① **`TablePage.initial` 的签名**：`RulesetDraft -> Seat option -> LlmSeat` 变
`RulesetDraft -> SeatingPlan`（`rg` 过全仓，7 处调用点全在 `tests/Janpo.Web.Tests`，断言一条没动）；
② **`AgentLine.fs` 动了两行**（不得不动：`live.LlmAt` / `live.Bot` 两个字段没了）——
Idle 那一句改读 `SeatingPlan.llmSeats` / `botsToDisplay`（措辞一字未改），
`data-bot` 改成 `data-seats`（四个名字的逗号串）。**其余一行没动**。

---

## 8. code-review（Standards + Spec 两轴，fixed point `6cede36e`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓**：全程 `jj st` / `jj diff` / `jj commit`，无远端操作、无交互式 flag。
- **工具强制的**：`fantomas --check` / `scripts/check-style.sh` / Biome / tsc 全绿；
  `let mutable` 一处未新增（Web 层仍是 0）。
- **F# 风格**（`docs/agents/fsharp-style.md`）：
  - 规则 1/3：新代码里没有从里往外读的嵌套；自查时**改了一处**——`removeProfile` 里
    `List.mapi (fun each profile -> each, profile)` 换成 `List.indexed`。
  - 规则 2：`references` / `llmSeats` 用 `Seat.indexed |> List.choose`，没有 lambda 包调用。
  - 规则 4.1：`List.isEmpty (SeatingPlan.llmSeats live.Seating)` 这类两层谓词保持原样。
  - 规则 9 的同族：`SeatingPlan.fit` 把「绑定条数 = 座位数」放进构造路径，而不是写进类型。
- **注释写「为什么」✓**：新类型、每个新出口、新那一屏与新闸门各写了「为什么是这个形状」
  与「别写成什么」。
- **blocking：0。**

### Spec（票面 8 条行为 + 5 条闸门 + 5 条边界 + 2 处术语表授权）

逐条对照见票文件的勾选框，四处值得写下来：

- **「key 只填一次」落成了形状而不是纪律**：`ModelProfile` 里有 key、`SeatBinding` 里没有，
  面板上因此**画不出**第二个 key 输入框（闸门再数一遍 password 输入框的个数）。
- **「删掉还被引用的档案」走的是「先算名单、再照名单说话」**：`removeProfile` 把那几席交回来，
  `update` 拿它拼那句中文——不是「删完扫一遍看谁没了」。
- **术语表只动了授权的两处**：新增 `ModelProfile` 词条（含 `_Avoid_`、四维度分工），
  `Player` 词条加一句。`jj diff CONTEXT.md` = **+13 / −0**，别处一个字没动。
- **`Persona` 词条里那句「一局内不变」现在按座位各自成立**，而那句话在词条里没有改
  （改它不在这一票的授权范围）：提案写进了 `DECISIONS.md` 73-4，等人裁。

### 记录但没改的 nitpick

1. 面板高了不少（四席 × 两格文本）。真要收，做法是把人格 / 模板折进「展开」里，
   但那是又一轮交互设计，且会让「一眼看得全」这条判据要重新定义——留给做面板那一票。
2. `SeatChoice.Profile` 按名字引用，两份同名时取头一份（§1.2）。要更硬就得给档案一个稳定 id，
   那是一整套生成 + 存储 + 迁移，这一票不值当。
3. `SeatingPlan.names` 与 `Roster.names` 是两个入口（一个走 `SeatingPlan`、一个走 `Roster`），
   但**命名规则只有一份**（`Roster.playerName`），两边都调它。
4. `verify-seats` 的第二程（迁移）自己开上下文、不共用第一程那台假端点——它根本不发请求。
   合并只会让两件事缠在一起。

---

## 9. 票外顺手修的一处（scope creep，跑过全套 CI）

**`verify-invariants.mjs` 里的一处随机假红**（判据 16 的现场）。收尾那一趟 CI 红在：

```
[可选动作的牌来自手牌] 有主见 种子 10・座位 0・第 2 手・bare 档：label 里的「6��」不是一个牌名
    原文：- id=7：手切6��
```

单跑那一道立刻绿，读代码找到病根：它收 `janpo decide` 的输出时写的是 `out += chunk`，
而 `chunk` 是 **`Buffer`**（没定 encoding）——逐块转字符串，一个汉字正好被切在两块之间就碎了。
机器忙的时候（同一台上有别的工作区在跑 CI）块边界会挪，于是这条假红**偶尔**出现一次。
修法是一行：两条流都 `setEncoding("utf8")`。**它不是我这一票碰出来的**，但留着就是一颗
「重跑一遍就好」的种子——而那正是判据 16 说的最危险的那种训练。

## 10. 留给人的待审项

1. **档案名叫「档案 1」「档案 2」**（`ModelProfile.initial.Name` + `SeatingPlan.freshName`）。
   老配置迁过来的那一份也叫「档案 1」。要更亲切（例如迁移那份叫「我的模型」）就改一行。
2. **`SeatingPlan` 这个类型名**（`CONTEXT.md` 里没有）：`Seating` 被 `TableBoard` 的渲染上下文
   占着（票 44）。要么让票 76 把那个改名成 `BoardContext` 之类、这个占回 `Seating`，
   要么就这么留着。提案在 `DECISIONS.md` 73-1。
3. **迁移在「读」的时候写盘**（`Store.readSeating`）。它是这一条迁移一次性的代价（§2）；
   要洁癖的话得让 `init` 多发一条 Cmd，而那会把「什么时候写」这件事摊到两处。
4. **超预算的那一趟真跑**（§6）：账单在报告里，判据没守住。
