# 票 133 — 终局记分卡

**结论**：done。`./scripts/ci.sh` 全绿（浏览器闸门 22 趟，比接票时多两趟）。
牌谱格式一个字没动（`Paifu.Version` 仍是 3），localStorage 一个键没加，PNG 一行没写。

## 一、做了什么

### 1. 引擎：`Scorecard`（新文件 `src/Janpo.Engine/Scorecard.fs`）

一个类型 + 一个纯函数，**吃一份牌谱、出按座位升序的逐席记分**：

```fsharp
type SeatTally = {
    Seat: Seat            // 这一行是哪一席
    Asked: int            // 问过选手的手数（= 这一席的决策记录条数）——兜底率与重试率的分母
    Hora: int             // 和了几次（`Hora.Actor` 指着这一席）
    HoraTargeted: int     // 被荣和几次（「放铳」那一列；**自摸不算**）
    Fallbacks: int        // 兜底代打了几手（`Fallback <> None` 的条数）
    Retries: int          // 重试了几次（`Attempts - 1` 之和，首问不算）
    Usage: Usage          // 这一席的 token 账单（那几条记录的 `Usage` 之和）
}

Scorecard.ofPaifu : Paifu -> SeatTally list
Scorecard.tally   : Ruleset -> Event list -> DecisionRecord list -> SeatTally list
Scorecard.totalUsage : SeatTally list -> Usage
```

**和了 / 放铳只走事件流**（判据 11）：谁和了、点的谁是规则说了算的事，渲染层与 Agent 层
都不再数一遍。mjai 自摸时把 `Hora.Target` 写成和了者自己，因此「放铳」那一支要显式减掉
——不减的话每次自摸都会给和了者自己记一笔放铳。

### 2. 页面：`ScorecardView` + `Table.scorecard` + 一枚按钮

- `src/Janpo.Web/ScorecardView.fs`（新）：`ScorecardRow`（席位 / 风 / 选手·档 / 顺位 / 终点 /
  `SeatTally`）、`ScorecardPlayer` 与 `ScorecardTier`（「选手 · 档」那一格的四态，见 §八）、
  `rows` / `headers` / `cells` / `toText` / `voidedGap` / `voidedSaid`。**这一层一个数都不算**。
- `Table.scorecard : Table -> SeatTally list`（`Table.fs`）：**不问配桌**，因此回放与 Live
  同一条路。它就是 `Scorecard.tally`，不是第二份实现。
- `TableState.scorecard : TableModel -> Table -> ScorecardRow list`：**记分卡唯一的那份数**，
  屏幕上那张表与「复制记分卡」出去的那段文字读的都是它。还没终局时是空表。
- `TableBoard.scorecardPanel`：`table-scorecard` 那一块，四行 `scorecard-N`，
  每行 13 个 `data-*`（seat / player-source / player-name / player-tier / player / juni / score /
  hora / hora-targeted / fallbacks / retries / asked / input / output），
  一枚 `table-scorecard-copy`，一句 `table-scorecard-note`，一句 `table-scorecard-voids`。
- `ScorecardCheck.fs`（新）：闸门在浏览器里 `import` 的那个入口（与 `PaifuCheck` /
  `ReviewCheck` 同一种东西）。
- `web/src/styles.css` **末尾新起一段**（票 139 在动这个文件，特意避开 `--board` / `--tile-w`）。

复制出去那段文字长这样（真跑出来的，不是手写的）：

```
janpo 记分卡
| 席位 · 风 | 选手 · 档 | 顺位 · 终点 | 和 · 铳 | 兜底 | 重试 | 输入 · 输出 tok |
| --- | --- | --- | --- | --- | --- | --- |
| 座位 0 南 | deepseek/deepseek-v4-flash・档位牌谱没记 | 1 位 · 30700 | 1 · 0 | 0 | 1 | 321758 · 11632 |
| 座位 1 西 | deepseek/deepseek-v4-flash・档位牌谱没记 | 3 位 · 22500 | 1 · 0 | 0 | 0 | 366117 · 12585 |
| 座位 2 北 | deepseek/deepseek-v4-flash・档位牌谱没记 | 2 位 · 25500 | 1 · 0 | 0 | 0 | 327834 · 12180 |
| 座位 3 东 | deepseek/deepseek-v4-flash・档位牌谱没记 | 4 位 · 21300 | 1 · 1 | 0 | 0 | 348393 · 11555 |
```

### 3. 闸门

新增一份 `web/scripts/verify-scorecard.mjs`，在 `verify-browser.mjs` 的 `gates` 表里登记
**两项**（本体 + 反向自证），趟数从 20 涨到 22（那张表自己算，别处一个字没改，
`check-single-source.sh` 通过）。另在既有的 `verify-export.mjs --to-end` 里接了三行断言，
守住 **Live 那一半**：表在、四行齐、「选手 · 档」与名牌逐字相同。

## 二、每一条新断言怎么先红的

**引擎侧（`ScorecardTests`，9 条）**——三次破坏实验：

| 破坏 | 红的是哪几条 |
|---|---|
| 自摸也记一笔放铳（去掉 `Target = Actor` 那道减法） | 「自摸只记和了」+「真打完一场」 |
| `Attempts` 不减 1 | 「兜底与重试各按席数，首问不算重试」 |
| 不按座位过滤决策记录 | 「四行相加就是总账」+「token 按席相加」+「兜底与重试」 |

其中「真打完一场」那条自己先红过一次而且**是断言错**（判据 6）：我原先写的
`Assert.True(放铳数 <= 和了数)` 在破坏下照样绿——它太松。改成
「放铳数**恰好等于**荣和数」（荣和数由用例自己照规则数一遍）之后才红得起来。
同一条还红过第二次：种子 2088（均匀随机）**一次自摸都没有**，
`tsumos > 0` 那道前置当场把它按红——1..400 号种子的 400 场里一共只有
**2 次自摸、4 次荣和，且没有任何一颗种子同时出现两种**（判据 3：那样的语料上
「自摸不记放铳」恒空转）。换成**有主见**那个选手的种子 2（3 自摸 + 1 荣和）才有得可验。

**页面侧（`ScorecardViewTests`，6 条）**——三次破坏实验：

| 破坏 | 红的是哪一条 |
|---|---|
| `Table.scorecard` 少喂已打完那几局的事件流 | 「牌桌那条路与牌谱那条路给出同一份逐席记分」 |
| `rows` 拿默认值凑满四行 | 「某一处短了就少几行，不拿默认值凑」 |
| `toText` 每行少印一列 | 「复制出去那段文字与屏幕上那张表逐格相同」 |

**浏览器侧（`verify-scorecard.mjs`）**——五次破坏实验：

| 破坏 | 红的是哪一条 | 红的原话 |
|---|---|---|
| 没终局也摆一张表 | 阴性 | 「游标停在第 0 帧（共 470 帧，这一场还没打完），记分卡却已经在 DOM 里了」 |
| 终局那一屏整块不画 | **阳性** | 「拖到末帧（第 469 帧）却没有记分卡：上面那条『还没终局时不在』因此什么都没证明」 |
| `Juni` 那一列反过来 | 逐格对拍 | 「座位 0 的 juni：页面上是「4」，引擎算的是「1」」（四行全红） |
| 回放那一列改成读名牌 | 「选手 · 档」 | 「回放那一屏有 4 行的『选手 · 档』不是『牌谱没记』（是『deepseek/deepseek-v4-flash』…）」 |
| `--poison` 把假 key 拌进记分卡文本 | key | 「记分卡文本里出现了 API key：灌进 localStorage 的那把假 key」（这一条进了 `gates` 表当第 22 趟） |

### 逐格对拍差点是一条恒真式——被按红一次才发现

**这一票最值钱的那一次红**：把 `Attempts - 1` 改成 `Attempts`，
「逐格对拍了 36 格（4 行 × 9 列）」**照样全绿**。原因是判据 6 那一条：
`ScorecardCheck.tally`（闸门那一侧）与页面那一侧在那七列上**共用引擎里同一段
`Scorecard.tally`**——而那一段本来就该只有一份（判据 11），不该为了对拍再写第二份。

处置是**加第三锚点**：`verify-scorecard.mjs` 里的 `tallyFromPaifu` 在 node 里
照**规则**把那份 JSON 重数一遍（含「自摸时 mjai 把 `target` 写成和了者自己，那不是放铳」）。
加完之后同一次破坏当场红出四条：

```
座位 0 的 retries：闸门照规则数出 1，引擎算的是 116
座位 1 的 retries：闸门照规则数出 0，引擎算的是 122
座位 2 的 retries：闸门照规则数出 0，引擎算的是 112
座位 3 的 retries：闸门照规则数出 0，引擎算的是 115
```

**顺位与终点那两列不进第三锚点**：那两列两侧本来就是两条路
（`Replay.ofPaifu` 一次 fold 到底 ↔ 页面的 `Table.replay` 逐帧再取末帧）。

## 三、量到的数

- **闸门规模**：逐格对拍 **36 格**（4 行 × 9 列）、看得见的那几格 **28 格**、第三锚点 **28 格**（4 行 × 7 列）、
  文本里逐格核 **28 格**、剪贴板 **375 字符 / 7 行**。每一条断言都**报出它扇到了几个元素**，
  并且各带一条「扇到 0 个就红」的兜底（票 116–119 那一串栽过的形状）。
- **语料**：首页那份 Demo 牌谱 —— 756 条事件 / 6 局 / **464 条决策记录** /
  **4 条 hora 事件**（3 自摸 + 1 荣和）。逐席 tok：321,758 / 366,117 / 327,834 / 348,393 输入，
  11,632 / 12,585 / 12,180 / 11,555 输出；**重试只有座位 0 的那 1 次，兜底四席全 0**。
- **兜底那一列在这份语料上恒是 0**（判据 3 的诚实交代）：它有断言、也真的被执行了
  （逐格对拍与第三锚点各核过它 4 次），但**它从来没有非 0 过**。
  引擎侧的用例里手捏了一条 `Fallback = Some …` 把那一支钉住；
  真语料上要它开口，得有一份带兜底的牌谱资产（留给以后）。
- **四行相加 = 右轨那条账单行**（真截图上核过，判据 7 的「把图打开看」）：
  记分卡四行输入相加 **1,364,102**、输出相加 **47,952**，
  与右轨那句「这一桌累计：输入 1364102 tok（缓存命中 435968，31%）、输出 47952 tok」
  逐位相同——回放那一侧没有作废的问话，因此差额恰好是 0。
- **闸门耗时**：两趟合起来 **1.4 s**（0.7 + 0.7），比 `verify-review` 的 4.9 s 便宜——
  这是「拖时间轴到终局」而不是「再打一整场」换来的。
- **CI**：22 趟浏览器闸门全绿，dotnet 侧 `ScorecardTests` 9 条 + `ScorecardViewTests` **17 条**全绿。

## 四、我自己裁的几件（详见 `DECISIONS.md` 的 133-1 … 133-10）

1. **作废掉的那几次问话（`VoidedAsk`）不进记分卡的 tok 列**——它们不在牌谱里（裁决 110），
   而这张表每一格都是牌谱的聚合。代价是四行相加**小于**牌桌那条账单行；
   因此加了一句 `table-scorecard-voids`，**把差额当场说出来**（票 39：同一屏上不许有
   两个数并排站着不解释）。
2. **`HoraTargeted` 而不是 `Houjuu`**：`CONTEXT.md` 里没有「放铳」的罗马字词条，
   而改术语表要单票授权。中文那一列照旧写「和 · 铳」（渲染是单向出口，ADR-0001）。
   提案见 DECISIONS 133-2。
3. **「席位 · 风」那一格取的是最后一局的自风**（票面指定的来源 `Board.ofTable`），
   于是首页那份 Demo 上四行写的是「南 / 西 / 北 / 东」而不是起家的「东 / 南 / 西 / 北」。
   与同屏的座位卡一致（不许第二份判据），但**这一列对一整场而言本来就有歧义**，
   见 DECISIONS 133-3 待主人裁。
4. **`Asked`（问过几次）多加了一列**：票面没要它，但兜底率与重试率没有分母就读不了，
   而票 135 的首页小表要的正是「兜底率」。它只上 `data-asked`，**不占屏幕上那张表的列**。
5. 输入侧 tok 走 `Usage.promptTokens`（付全价 + 命中缓存 + 写缓存），与牌桌那条账单行同一口径。

## 五、越界发现（交调度器裁，我没动）

### A.（已了结）票面那句「牌谱里根本没有模型身份」不成立 → 调度器松了边界 → 已落地

我按票面交出第一版之后把这条报了上去。**调度器核过并认账**（`Event.fs:116` 的
`StartGame of names: string list`，回放名牌画的就是它），当场松了这条边界，
我照新边界重做了「选手 · 档」那一列。**结论见 §八，这一条不再是待审项。**

### B.（调度器已另开票，我没碰）`verify-export.mjs --to-end` **单跑**时页面会冒 5 条 React 警告

`Warning: Maximum update depth exceeded.` —— 单跑（自己起一台冷 vite dev）时**必现**，
在 `verify-browser.mjs` 那条共用跑道上（一切都热着、那一趟只跑 0.8 s）**不出现**，
因此 CI 是绿的。

**它与票 133 无关，我核过**：把我改过的全部文件（`Table.fs` / `TableState.fs` /
`TableBoard.fs` / 两个 fsproj / `styles.css`）逐个回退到 `46d18444`、把三个新文件挪走、
重新 fable，**照样必现**。

唯一的嫌疑是 `ReviewPanel.useReview` 里那一个 `React.useEffect`
（全仓库只有这一处 `useEffect`，它在终局那一刻 `setConsulted`）。
**我没有继续查，也没有开号**（判据 17：agent 只描述、不编号）。
它是一条**只在慢机 / 冷启动下开口的假绿风险**：判据 22 说「红了有人查，绿了没人查」，
而这一条今天正好落在「合并跑道上绿」那一侧。

### C. 兜底那一列在今天的语料上恒是 0

见 §三。不是断言写错，是仓库里**没有一份带兜底的牌谱资产**。

## 六、code-review 两轴与自动修的那一轮

fixed point `46d18444`，Standards 与 Spec 两轴各跑一个 sub-agent。

**Spec 轴：0 blocking。** 六个验收框、四条边界、派工单那三条都核过；报告里的数
（36 格 / 28 格 / 22 趟 / 464 条记录 / 逐席 tok）它拿 `demo-paifu.json` 重算过，一致。

**Standards 轴：4 blocking，全部当场修了并重跑全量。**

| 它逮到的 | 我改成 | 新的红证 |
|---|---|---|
| 闸门把「牌谱没记」这**句中文**当断言（判据 24：措辞一改就红 = 锁死实现） | 立 `ScorecardPlayer` 两个 case（`Nameplate` / `Unrecorded`）+ `toWire`；DOM 上多一个 `data-player-source`，闸门读它，中文只印给人看 | 把回放那一支改回读名牌 → 「有 4 行的 `data-player-source` 不是「unrecorded」（是「nameplate」…）」 |
| wire 与 DOM 上造了 `houjuu` 这个词，而 `CONTEXT.md` 里没有它（AGENTS.md 硬约束 5；F# 侧本来就避开了，转手在 JSON 上造了回来） | 一律改成 `hora_targeted` / `data-hora-targeted`（只复合引擎已有的两个词） | —（改名，靠既有的逐格对拍守着） |
| `ScorecardCopied` / `ScorecardCopySettled` 一条 dotnet 用例都没有，而代码注释在那儿写下了三条不变量（判据 2）；`failed` 那一支一次都执行不到（判据 3） | 补 3 条用例：三态（含 `failed`）、再点一次先撤旧话（**真把 Live 那一桌打到终局**才点得着）、还没终局时点它无事发生（阴阳同处，判据 21） | 把 `ScorecardCopySettled` 的 `Error` 支吞掉 → 「复制那一趟的三态」红 |
| `scorecardVoids`（133-1 那条自裁的执行体）在真语料上**执行 0 次**：回放 `paidVoids` 恒空，dotnet 侧一条用例都没有（判据 3：为 0 的当场喊停） | 把差额与那句话从视图里搬进 `ScorecardView.voidedGap` / `voidedSaid`（纯函数），补 2 条用例：造一桌带两笔 `VoidedAsk` 的（差额恰好 416 / 42），加一条**阴性对照**（没那几笔时恒 0） | `voidedGap` 反号 → 「四行相加加上那笔差额，恰好是牌桌那条账单行」红 |

**Standards 的 nitpick 顺手修了三条**（其余只记录）：`ScorecardView.players` 这个**不存在的函数**
被两处注释引用（改成 `TableState.scorecardPlayers`）、`rows` 补上返回类型标注、
`List.map (fun _ -> …)` 改成 `List.replicate`。
`ScorecardCheck.fs` 那段「两条路」的注释也改了——**它把话说过头了**：
顺位与终点两侧共用 `Replay` 的那段 fold，分岔只在「怎么收」，注释现在逐项写清它抓得住什么、抓不住什么。

**Spec 的 nitpick 里有一条真值钱，也修了**：文本对拍用的是 `line.includes(cell)`，
而「0」这种格在任何一行里都命中得了——列错位它逮不住。改成**逐列切开比**
（`|` 切、trim、按位置逐格相等），dotnet 与浏览器两侧同时改。
改完随手试了一次「把兜底与重试两列对调」，**发现它仍旧不红**：那两处读的是同一份 `cells`，
一起换位置。于是又补了一段 ③b：**看得见的那一格与同一行的 `data-*` 对得上**
（28 格）。同一次对调这才红出来。

## 七、边界自查

- 牌谱格式：`Paifu.fs` 一行没改，`Paifu.Version` 仍是 3，ADR-0002 没动。
- 没做 PNG（票 134）；没碰右轨那四行席位卡（票 124/126）；
  没碰 `TablePanel.fs`（票 138）；`styles.css` 只在**文件末尾**新起一段，
  `--board` / `--tile-w` 那一段一个字节没动（票 139）。
- 没动 `Playback` 世代号、没加 localStorage 键、没碰 `web/src/agent/**`。
- 测试只往更硬改：删了 0 条、skip 0 条、放宽 0 条；
  唯一一次改期望值是 §二里那条「`<=` 改成 `=`」——**那是往更硬改**。
- key：一把写死的假 key（与票 34 那趟同一个字面量族），只灌 localStorage，
  不进代码逻辑、不进 fixture；CI 里一个真实 provider 都没调。

## 八、边界松了之后重做的那一列（调度器票外授权）

**授权原话**：身份取牌谱的 `start_game.names`（回放屏也有），档位仍旧只有 Live 才有。

### 做成了什么

「选手 · 档」不再是一个字符串，而是**身份与档位两件事各自可缺**：

```fsharp
type ScorecardTier =
    | Set of said: string   // Live：这一席此刻拨在哪一档
    | NotApplicable         // 这一席**没有**档位（bot / 强 AI 基线不走 prompt）
    | Unrecorded            // 牌谱**没记**档位（回放）

type ScorecardPlayer =
    | Named of name: string * tier: ScorecardTier   // 牌谱记下了这一席是谁
    | Unknown                                       // 连身份都没有（v1 老牌谱 / names 是空串）
```

四态在页面上是四句不同的话，wire 值（`data-player-source`）因此有四个：

| 态 | `data-player-source` | 屏幕上那一格 | 什么时候 |
|---|---|---|---|
| `Named(n, Set t)` | `tiered` | `deepseek/deepseek-v4-flash・完整` | Live 的模型席 / 真人席 |
| `Named(n, NotApplicable)` | `no-tier` | `random` | Live 的 bot 席与强 AI 基线席 |
| `Named(n, Unrecorded)` | `tier-unrecorded` | `deepseek/deepseek-v4-flash・档位牌谱没记` | 回放（首页 Demo、分享链接） |
| `Unknown` | `unrecorded` | `牌谱没记` | `names` 短了一截 / 那一格是空串（v1 老牌谱） |

**「这一席没有档位」与「这份牌谱没记档位」不许压成一个 case**（判据 12）：前者是
*这一席本来就不走 prompt*，后者是*记录缺了一样*。压成一个之后，bot 席会被写上
「档位牌谱没记」——那是句假话。

### 两半各自的来源，各只有一处

- **身份**：`start_game` 那一列 `names`（恒是 `provider/model`，`Roster.playerName`）。
  回放取 `ReplayTable.Ready` 带着的那一份（**与名牌同一份**），Live 取配桌那一份
  （`Roster.names`，也就是这一桌导出牌谱时会写进 `start_game` 的那一列）。
  **没有去碰 `DecisionRecord.Output` 里那个 `model` 字段**——那是 provider 的原始回执，
  F# 不解释它（`Paifu.fs` 那条注释）。
- **档位**：`SeatingPlan.tiers`。为它把 `SeatingPlan.nameplates` 拆成了 `plateWho` + `plateTier`
  两半，**`nameplates` 与 `tiers` 都由同一个 `plateTier` 出**——「这一席写不写档位」这条判据
  因此物理上只有一处，两边漂不了。既有的 `SeatingPlanTests` 16 条一条没红（行为逐字未变）。

### 同一屏上一个说法（要害 1）

改完之后回放那一屏：右轨席位卡写 `deepseek/deepseek-v4-flash`，记分卡身份格逐字相同
（**截图核过**，判据 7）。之前那一幕（名牌写模型名、记分卡写「牌谱没记」）没了。

**Live 那一桌两处仍旧不同，这是故意的**：名牌写本机那个私人档案名（`我的小D・完整`），
记分卡写 `deepseek/deepseek-v4-flash・完整`。理由是**记分卡是要被带走的东西**
（贴 issue / 贴群），本机档案名带出去谁也认不出是哪个模型（`ModelProfile.Name` 那条术语：
它只活在这一页上）。而**档位那半句两处逐字相同**（同一条判据）。

### 闸门与它们怎么先红的

**回放那一趟**（`verify-scorecard.mjs` ⑤，要害 3）：逐行核三件——
`data-player-source` 是 `tier-unrecorded`、`data-player-tier` 是「档位牌谱没记」、
`data-player-name` **与同一屏名牌上那一句逐字相同**（名牌那一份是当场从 DOM 里扇出来的，
`seat-N-player`，并且先核「扇到几张名牌 = 记分卡几行」）。
**先红了一次**（拿旧实现跑新断言，12 条）：

```
座位 0 的 data-player-source 是「unrecorded」，回放那一屏该是「tier-unrecorded」：牌谱记得下身份、记不下档位
座位 0 的档位那半格写着「undefined」，回放那一屏该是「档位牌谱没记」
座位 0 的记分卡身份格写着「undefined」，而名牌上写着「deepseek/deepseek-v4-flash」：同一屏上不许两个说法（…）
（四席各三条）
```

**Live 那一趟**（`verify-export.mjs --to-end`）：三件——身份格 = **导出的那份牌谱里那一列
`names`**、档位格 = 名牌上「・」之后那半句、wire 值 = 有档位就 `tiered` 没有就 `no-tier`。
两次破坏实验各红一次：

| 破坏 | 红的原话 |
|---|---|
| Live 的身份改用本机档案名 | 「座位 0 的记分卡身份格写着「均匀随机」，而牌谱里那一列 names 是「random」」（四席全红） |
| bot 席也写一个档位 | 「座位 0 的记分卡档位那半格是「裸奔」，而名牌上那半句是「」：那半段该与名牌同源」+「来源是「tiered」…该是「no-tier」」（四席各两条） |

**dotnet 侧新增 6 条**（`ScorecardViewTests` 11 → 17）：四态各说各的话且 wire 两两不同、
两半各自的取值器、**档位与名牌同源**（`nameplates` 与 `tiers` 逐席对得上，带阳性对照
「这份坐法里两种都有」）、回放那一屏身份 = `Table.names`、
**`names` 是空串时才整格退回「牌谱没记」**（带阳性对照：同一条路上没抹名字的那份四行都写得出身份）、
Live 那一桌身份是 wire 名而不是私人档案名。

### 顺带修的一处视觉

那一格变长之后，1600×1000 上最后一列「输入 · 输出 tok」被挤出了视野（`overflow-x` 只是让它可滚，
人第一眼看不到）。处置：**只让「选手 · 档」那一列折行**（数那几列仍旧不折，折了就对不齐上下四家）。
截图核过：四列数字全部回到视野内。
