# 89 — 真人的信息辅助与思考时限

**结论：`ScaffoldTier` 在真人这一侧真的生效了，而「他看到的那几个数」与「模型 prompt 里那几个数」
是同一次引擎调用的两份转录；思考时限挂在「轮到他了吗」上，到点代他打一手而牌局照走。**
`./scripts/ci.sh` **EXIT=0**，70s；dotnet 751 + **270** 条（原 751 + 256），浏览器闸门从十六趟变
**十七趟**（新增 `verify-assist`，**13.2–13.3s**）。

六件事落地：① 真人席那一格脚手架**真的在用**（`HumanScaffold`），Bare 一个数都不给、
Assisted 给向听 / 有效牌 / 进退向 / 危险度、ToolSearch **按 Assisted 处理并在页面上说清**；
② **辅助给不给只有一条判据**（`TableState.assists`），牌桌上那块危险度与面板上那一枚开关
读的是同一条——裸奔档的真人在座时那一枚**根本不在 DOM 里**；
③ **思考时限**是第四项座位级配置（`SeatField.Clock`，默认 0 = 不限时），倒计时挂在
`humanTurn` 上、每条消息重推一遍（`clocked`）；④ 到点由**引擎的 `Fallback.action`** 代他打一手
（Bare 那一支 = 摸切 → 过 → 合法动作集第一条），**牌局继续**；
⑤ 「时限代打」与「他自己按的过」在数据里分得开（`HumanPass.AutoPlayed`，票 88 欠的那一格）；
⑥ **面板上那一格把 ToolSearch 放开了**（`TablePanel.fs:662`，票 94 的挂账），
并配了一条「面板上选得到 + 那一席用这一档打完一局」的闸门。

**默认不限时那条路一个行为都没变**：`wound` 在没设时限时一个效果体都不发，
票 87/88 那几条数效果体的用例逐条照旧；十七趟里的 `verify-human`（3.9s）一条断言没动。

---

## 1. 形状：一条判据、两个消费点、一个新模块

| 层 | 加了什么 | 它答的问题 |
|---|---|---|
| `SeatingPlan` | `SeatField.Clock` / `SeatBinding.Clock: int`（秒，0 = 不限时）+ `SeatBinding.limit` | 这一席想多久 |
| `HumanScaffold`（新文件） | `shows` / `lines` / `toDisplay` / `summary` / `threats` | 这一档给不给、给出来长什么样 |
| `TableState` | `assists` / `humanScaffold` / `humanTier` / `humanClock` / `humanLimit` | **给不给**、给哪一份、走到哪儿了 |
| `TableState` | `HumanClock`（`Turn` / `Elapsed` / `Limit`）+ `LiveTable.Clock` | 这一手的倒计时 |
| `TableMsg` | `HumanTicked of turn: int` | 又走了一秒 |
| `HumanSeat` | `HumanPass.AutoPlayed: string option` + `HumanPass.pressed` | 这一次是不是他按的 |

### 1.1 「同一份数」是一条等式，不是一句承诺

`ScaffoldLine` **抱着引擎那条记录本身**：

```fsharp
type ScaffoldLine = {
    Id: int                 // 这一条打牌动作的包内 id（= 手牌上那张牌的 data-dahai-id）
    Label: string           // 引擎给的中文 label
    Trial: DahaiScaffold    // **引擎算的那一条原样带着**：向听 / 进退向 / 有效牌 / 危险度
}
```

`Trial` 就是 `DecisionPackage.scaffold` 里那一条——而模型 prompt 尾部那一节读的是
**同一份包的同一个字段**（跨界时由 `Scaffold.encoder` 编出去）。
不是「两处各算一遍再对拍」，是**同一次 `Scaffold.calculate`**（它随包算好，与档位无关，票 24 的裁决 24-1）。

**文字也不自己拼**：`toDisplay` 三段全部由引擎的单向出口渲（ADR-0001）——
`Ukeire.toDisplay`（自带打完之后的向听、每张牌的中文名与剩余枚数）、`Danger.toDisplay`
（与牌桌上那块危险度同一个函数）。页面这一层连「几枚怎么写」都不知道，也就漂不出第二种写法。

### 1.2 辅助给不给：一条判据，两个消费点

```fsharp
let assists (model) : bool =
    match lockedSeat model with
    | None -> true                                    // 没有真人 / 已终局：与从前逐字相同
    | Some _ -> humanTier model |> Option.map HumanScaffold.shows |> Option.defaultValue true

let humanScaffold (model) : Scaffold option =
    if not (assists model) then None else humanTurn model |> Option.bind DecisionPackage.scaffold
```

| 消费点 | 读什么 | 裸奔档时是什么 |
|---|---|---|
| 辅助那一块 `HumanLine.assist` | `humanScaffold` | `None` → **整块不在 DOM 里** |
| 牌桌上那块危险度 `TableBoard.dangerPanels` | `assists` | 整块不画 |
| 面板上那一枚「危险度」开关 `TablePanel.viewpoints` | `assists` | **那一枚不在 DOM 里**（灰掉不算数） |

**危险度为什么归这一票管**：术语表那条「感知 vs 计算」把 Danger 明确放在 Assisted 一侧
（现物与筋都得从河里推）。票 25 那块面板从前只被「观测者自家那一手」限着，
裸奔档的真人拨一下就看得见——那时「裸奔」这个对照组靠的只是他自觉不按那一枚。
票 87 的报告 §10 nitpick 1 把这件事挂给了本票，这里兑现。

### 1.3 倒计时：不存「轮到他了吗」，每条消息重推一遍

票 87 立的那条判据（「轮到谁」不存状态，现问 `handOf`）在这一票上被放大了一次：

```fsharp
// 一条消息推一步，再把倒计时按此刻的局面重推一遍
let update (message) (model) =
    let model = if moves message then rewound model else model
    let stepped, cmd = stepped message model
    clocked stepped cmd            // ← `wound` 现问 `handOf`：轮到他且设了时限才上发条
```

于是「他中途坐下来」「他把时限拨成不限」「重开一桌」「模型席答上来了」这十几种情形
**一处启停都不必写**——那正是十几份会漂的判据的开头。`HumanClock.Turn` 只回答一件事：
**这还是同一手吗**（牌桌每落定一个动作 `Table.Turns` 就 +1，因此他的每一次决策各占一个手序号，
它天然就是这一记倒计时的票号）。链的形状与票 74 的 `Waited` 逐字相同：一手一条链，
他一出手旧链下一记回来时自己断。

**它挂在「轮到他了吗」上而不是「牌桌停着」上**（票面点名）：票 88 之后他在想的时候
模型席照问照答，牌桌并没停——挂错地方的话，别席在飞时他的钟也在走。

### 1.4 到点代他打一手：向引擎要那一手

```fsharp
let action = Fallback.action ScaffoldTier.Bare package
```

**判据 11**（要读规则才做得出的决定归引擎）：`Fallback` 的 Bare 那一支就是
「摸切 → 过 → 合法动作集的第一条」，正是票面要的「超时自动摸切，响应阶段自动过」；
而碰吃之后要打牌那一手**根本没有「刚摸进的那张」**，第三级因此不是凑数（那段注释是票 23 写的）。

**恒拿 Bare 那一支，不看他自己拨的档位**（记在 `DECISIONS.md` 89-2）：到点那一手不是他打的，
平台不该替他用一遍辅助（Assisted 那一支会挑「不退向听的安全打」）；而且那样的话，
「时限」会把「档位」这个自变量也搬进来。

**代打那一手与他自己点的那一下走同一条路**（`landed`）：都是 `Table.apply`、都不留决策记录，
因此**超时那一手在牌谱里与手动那一手同形**——用例的锚点是回放（`Table.replay` 重建时
根本不知道哪一手是到点代打的），见 §4.1。

### 1.5 `HumanPass` 拓宽了一格（票 88 欠的那一格）

```fsharp
type HumanPass = {
    Turn: int
    Seat: Seat
    Skipped: string list
    /// **时限到点平台代他打的那一手**（中文 label）；他自己按下去的那一次是 None。
    AutoPlayed: string option
}
```

票 88 的报告 §2 明写「票 89 的时限要加『到点自动过』时，这条记录要能说出『这一次不是他按的』，
那一格由那一票加」。**带 label 而不是一个 bool**：到点那一手可能是「过」也可能是摸切，
而「平台替你做了什么」不许静默替换（票 23 那条规矩）。

**页面上两个数各占一个钩子**：`data-human-passes`（**他自己按的次数，语义一字未改**，
票 88 的闸门因此逐字照旧）与 `data-human-expired`（时限代打的次数）。
一本账、两个滤镜（`HumanPass.pressed`），不新造第二张表。

---

## 2. 面板上那一格：ToolSearch 放开了（调度器点名带的一件）

```fsharp
// 改前：ScaffoldTier.toWire tier, ScaffoldTier.toDisplay tier, tier <> ScaffoldTier.ToolSearch
// 改后：ScaffoldTier.toWire tier, ScaffoldTier.toDisplay tier, true
```

票 94 报告 §10 第 1 条挂的账：那一档从 `localStorage` 与 `demo-game.mjs --tier tool_search`
早就拨得动，只有面板上那一行还灰着（当时 `src/Janpo.Web` 不归它）。

配的闸门是**第四程**（§3.2 ④）：三档一个都不许是 `disabled`、在面板上拨到工具搜索、
用这一档**把一局打完**，再读导出的牌谱——那一席有牌可打的每一手都真去查了（`--what-if 2`
的假端点看 `tools` 行事）、**0 兜底**。「面板上选得到」与「选了之后那一档真的传到了 Agent 层」
是两件事，这一条把后者也钉住了。

**真人席选到它时按 Assisted 处理**（票面原话）：`HumanScaffold.shows` 把两档并到一起，
页面上那一块的抬头照旧写着「信息辅助」，而 `data-human-tier` 仍旧是 `tool_search`
——拨到哪儿与拿到什么各说各的，不静默降级。理由记在 `DECISIONS.md` 89-1。

---

## 3. 闸门：谁在守什么

### 3.1 dotnet 侧（`tests/Janpo.Web.Tests/HumanAssistTests.fs`，**14 条**；256 → 270）

| 用例 | 钉的是 |
|---|---|
| **Bare 什么都不给**：向听 / 有效牌 / 危险度在页面这一侧连值都取不出来 | `humanScaffold = None`、`assists = false`，拨一下危险度也仍是 false；**两个阳性对照**（同一桌拨到 Assisted 当场有；四家 bot 那一桌照旧全给） |
| Bare **一整局**都不给，而同一局面拨到信息辅助就有 | 走一整局逐手核；**同一刻拨到 Assisted 必须真有数**（否则量的只是「这一手本来就没有」）；执行 > 10 次 |
| **Assisted 那几个数与模型跨界拿到的那一份逐字段相同** | 左边是 `HumanScaffold.lines`，右边是 `DecisionPackage.encoder` 编出去的 JSON（**第三个锚点**：模型真正读到的那份字节）；向听 / 进退向 / 有效牌枚数与种数 / 危险度名次五类 |
| **一整局逐手对拍**：一手都不许岔开 | 同上，走一整局；执行 > 10 手、> 100 行 |
| **辅助那几行与他点得动的那几张一一对应**：多一行少一行都不许 | 行的 id 集合 = `HumanSeat.dahaiOptions` 的 id 集合；每一行的 label = 那一条动作的 label |
| ToolSearch 按信息辅助处理：那几行与 Assisted 逐条相同 | `shows` 两档同真、Bare 假；同一局面两档的行逐条相同 |
| 危险度那一块在真人这一侧与辅助同进同出 | **走到真有威胁的那一刻**（判据 3），`Threats` 非空；拨回裸奔时 `assists` 与 `humanScaffold` 一起翻面 |
| **默认不限时**：一记倒计时都不发，那条链也推不动牌桌 | `humanLimit = None`、`humanClock = None`；`Advanced` / `PlayToggled` **零效果体**；凭空来一条 `HumanTicked` 什么都不做 |
| 没有真人那一桌一记倒计时都不发 | 四家 bot 那一桌把时限拨到 1 秒也长不出钟来 |
| **倒计时挂在「轮到他了吗」上**：轮到才走，不轮到当场停 | 座位 0（一打开就轮到他）有钟、座位 1（开局轮到别人）没有；走一秒牌桌不动；**他一出手钟当场没了**，推到下一手换成新的一记（`Turn` 不同、`Elapsed` 归零）；拨回不限时当场空掉 |
| 过期的那一记丢掉：链自己断，牌桌一手都不动 | 上一手的票号：手数不动、秒数不动、零效果体 |
| **时限到点自动摸切**：打的就是刚摸那一张，而牌局接着走 | 落定的那一手是 `Dahai(他, 摸切那张, true)`；手数 +1；`canAdvance` 仍真且下一手推得动；那一笔记账说得出「不是他按的」 |
| **时限到点、响应阶段自动过**：与他自己按的那一次在数据里分得开 | 走到响应那一刻；两条路（他按 / 到点）落定的**事件流逐条相同**，而 `AutoPlayed` 一个 None 一个 `Some "过"`、`Skipped` 相同 |
| **超时那一手在牌谱里与手动那一手同形** | 连着到点六次，`Table.replay` 重建出**逐条相同的事件流 + 相同手数**（锚点是回放，它不知道哪一手是代打的）；那一桌 `Decisions` 恒空 |

改过的既有用例两条，**都是往硬里改**（判据 5）：
`AgentTests`「字段的键名两两不同」把 `SeatField.all` 的条数从 3 改成 4 并多断言一条
`Contains SeatField.Clock`；`SeatingPlanTests`「档案答怎么问、座位答给多少信息」那份
`SeatBinding` 字面量补了 `Clock = 30`，并在注释里写明**`LlmSeat` 里没有这一格**
（`SeatBinding.config` 编得过就是证据：时限不过界给模型）。

### 3.2 浏览器侧（第十七趟 `verify-assist.mjs`，13.2–13.3s；**一个字节都不出网**）

**四程**：

① **裸奔档的阴性对照**（真人 + 三家有主见 bot，走 60 步 / 他出手 17 次）：
辅助那一块出现 **0** 次、辅助行 **0** 行、**危险度那一枚开关 0 枚**；
然后**停在「轮到他出牌」那一刻**抓整页 HTML：`data-scaffold-*` 一个都没有、
整页文字里「向听 / 有效牌 / 进退向 / 危险度」一个都没有。
**阳性对照**：同一页把座位 0 那一格拨到「信息辅助」，那一块（9 行）与那一枚开关当场全回来。

> **「停在他那一刻」这一条是被红-1 逼出来的**：第一版在走完之后随手抓整页，
> 而不轮到他时那一块本来就不画——把 `assists` 改成恒真之后，整页那两条**一声不吭**
> （只有「危险度那一枚」与逐手快照那两条红）。补上 `peekHuman`（走到他那一刻但不出手）
> 之后同一次破坏红出 5 条，见 §4 红-1。

② **Assisted 的对拍**（走 60 步，核 11 手 / 111 行）：辅助那几行的 id **恰好等于**他点得动的那几张；
每一行的数在引擎给的形状里（向听 ∈ [-1,8]、进退向 ≥ 0、枚数 ≥ 0）；
**牌桌上那块危险度与辅助那几行是同一份数**——两处各渲一遍，名次的多重集必须相同
（这一条要有威胁的家才开口，因此三家用**有主见** bot，实测 1 手碰上）。
然后**在同一个局面上**把档位拨到工具搜索：那几行**逐字相同**（9 行 → 9 行）。

③ **时限**：不设时限等 3 秒——牌桌一手不动、`data-human-clock` 是空串、那句话里没有「还剩」；
拨成 2 秒不动手——**一秒之后那一格是 1**（人读的那句话也在倒数：「还剩 1 秒（共 2 秒，到点自动摸切）」），
到点自家河多一张、**那一张 `data-tsumogiri=true` 且就是刚摸进的那张**（`5pr`），
`data-human-expired=1` 而 `data-human-passes=0`，页面说「第 0 手时限到点，替你打了：摸切赤5筒」，
**打出去之后那一格当场空掉**（只在轮到他时走）；
最后按下「播放」再等 6 秒：牌桌真的往下走了（「上一手：座位 2 手切6筒」）、代打累计 2 次
——**牌局继续**，而且他的下一手照样被吃掉。

> **为什么要按一下「播放」**：`?table=1` 默认暂停（票 71），他出完手别家要等下一记定时器
> ——那是票 87 就有的播放语义，与时限无关（票 87 报告 §10 第 2 条那件事原样还在）。

④ **面板上那一档**：三档一个都不灰 → 在面板上选 `tool_search` → 名牌上写着「…・工具搜索」
→ 走 29 手把这一局打完（结算面板在）→ 导出牌谱：那一席 10 条记录、有牌可打 7 手、
**7 手都真去查了两次**、**0 兜底**。

**执行次数**（判据 3，闸门自己印出来）：第一程他出手 17 次 + 停下来看 1 次；
第二程核 11 手 / 111 行、危险度对拍 1 手；第三程等 3 + 2 + 6 秒；第四程 29 手、7 手查询。
**没有一条断言是执行 0 次的**（第二程那三条各带一句「基本没开过口」的下限）。

**代价说清楚**（判据 14）：这一趟 13.2s 里 **11.6s 是死等**（3 + 1.2 + 1.4 + 6）。
时限这件事量的就是墙钟，没有别的量法；把那 6 秒收到 4.5 秒能省 1.5 秒，但他下一手要等
三家 bot 各走一步（600ms/手）再加两秒的钟，余量就只剩零点几秒——**宁可慢一秒也不要一道会抖的闸门**。
CI 总时长因此从 ~55s 变 **70s**。

---

## 4. 每条新断言先红一次（判据 1 的原始输出）

**八次，全部实跑**：改**产品代码**（不是改断言），重编 Fable / 重跑 dotnet，抄红的原文，
再逐文件 `diff` 对回 `/tmp/t89bak/`（四个文件全 OK）。

**红-1｜辅助渲染强行打开**（`TableState.assists` 恒 `true`）——**票面点名的那一条**

```
第一程（阴性对照）：座位 0 是我自己、裸奔档，其余三家有主见 bot
　他出手 17 次，辅助那一块出现 18 次、辅助行 120 行、危险度那一枚 1 枚
　整页带数的属性：data-scaffold-lines、data-scaffold-shanten、data-scaffold-id、data-scaffold-delta、
　　data-scaffold-ukeire、data-scaffold-kinds、data-scaffold-danger
　整页文字里那几个词：向听、有效牌、进退向、危险度

真人的信息辅助与思考时限这一道没过：
裸奔档的真人坐在桌边，「危险度」那一枚开关却还在 DOM 里：灰掉不算数——危险度是「要算才有的量」
（术语表那条「感知 vs 计算」），拨得出来「裸奔」这个对照组就没了
裸奔档下辅助那一块出现了 18 次（他出手 17 次，另加停下来看的那一刻）
裸奔档下页面上摆出了 120 行算好的数
裸奔档下整页 HTML 里还有算好的数：data-scaffold-lines、…、data-scaffold-danger
——「一个坐在牌桌前的人免费得到的一切」里没有它们（CONTEXT.md 的 ScaffoldTier）
裸奔档下整页文字里出现了「向听」「有效牌」「进退向」「危险度」：那几个词只可能从「要算才有的那几个量」那一侧来
```

**这一条第一次跑只红了 3 条**（整页那两条空转），补 `peekHuman` 之后才是上面这 5 条——
**闸门自己被这次破坏改硬了一档**（同票 87 红-4、票 88 红-3 那一族）。

**红-2｜把工具搜索档灰回去**（`tier <> ScaffoldTier.ToolSearch`）

```
　脚手架那一格的选项：bare、assisted、tool_search（灰）
真人的信息辅助与思考时限这一道没过：
脚手架那一格里还有选不了的档位：tool_search——票 94 已经把工具搜索档做完了
（`janpo.seats.<N>.tier=tool_search` 与 demo-game 都拨得动），面板上不该还灰着
```

**红-3｜到点代打不说「不是他按的」**（`AutoPlayed = None`）

```
（dotnet）
HumanAssistTests.时限到点自动摸切：打的就是刚摸那一张，而牌局接着走 [FAIL]
HumanAssistTests.时限到点、响应阶段自动过：与他自己按的那一次在数据里分得开 [FAIL]
HumanAssistTests.超时那一手在牌谱里与手动那一手同形：回放重建得出逐条相同的事件流 [FAIL]

（浏览器）
时限到点代打了一手，页面却记着 0 次：「这一手不是他按的」要在数据里说得出来（票 88 欠下的那一格）
他一次「过」都没按，页面却记着 1 次：时限代打的那几手与他自己按的那几次必须分得开
时限替他打了一手，页面上却没说这件事：「你坐在座位 0：轮到别人，看着就好。　第 0 手你按了「过」（这一桌你按「过」 1 次）。」
```

第三行正是这一格存在的理由：**页面对着一手他从没碰过的牌说「你按了「过」」**。

**红-4｜倒计时挂在「桌边坐着人」上而不是「轮到他了吗」上**（`wound` 不问 `handOf`）

```
（dotnet）HumanAssistTests.倒计时挂在「轮到他了吗」上：轮到才走，不轮到当场停 [FAIL]
（浏览器）他这一手已经打出去了（轮到别人），倒计时那一格却还写着「2」：
　　它只许在轮到他的时候走（挂在「轮到他了吗」上，不挂在「牌桌停着」上）
```

**红-5｜真人这一侧的有效牌自己重算一遍**（`lines` 里把 `Ukeire.Tiles` 截成一张）

```
HumanAssistTests.Assisted 那几个数与模型跨界拿到的那一份逐字段相同 [FAIL]
  Assert.Equal() Failure: Collections differ
  Expected: [Tuple (0, 3, 1, Some((20, 7)), null), Tuple (1, 2, 0, Some((15, 5)), null), …]
  Actual:   [Tuple (0, 3, 1, Some((2, 1)),  null), Tuple (1, 2, 0, Some((3, 1)),  null), …]
HumanAssistTests.一整局逐手对拍：他看到的与模型拿到的是同一份，一手都不许岔开 [FAIL]
```

**红-6｜辅助那几行自己筛掉退向的那几条**（`lines` 加一道 `ShantenDelta = 0`）

```
（dotnet）辅助那几行与他点得动的那几张一一对应：多一行少一行都不许 [FAIL]
        Assisted 那几个数与模型跨界拿到的那一份逐字段相同 [FAIL]
        一整局逐手对拍 [FAIL]
（浏览器）
辅助那几行是 [1,2,3,4,6,9]，而他点得动的那几张是 [0,…,9]：多一行是凭空造的，少一行是有数却点不到
——人会照着一份对不上的表出牌
只核了 1 手 / 只核了 6 行：这几条断言基本没开过口（判据 3）
```

**红-7｜`HumanScaffold.shows` 恒真（裸奔档也给）**

```
HumanAssistTests.Bare 一整局都不给，而同一局面拨到信息辅助就有 [FAIL]
HumanAssistTests.Bare 什么都不给：向听 / 有效牌 / 危险度在页面这一侧连值都取不出来 [FAIL]
HumanAssistTests.危险度那一块在真人这一侧与辅助同进同出 [FAIL]
HumanAssistTests.ToolSearch 按信息辅助处理：那几行与 Assisted 逐条相同 [FAIL]
```

**红-8｜到点只把钟停掉，不代他打**（票面那句「不许卡死」的执行体）

```
（dotnet）时限到点自动摸切 [FAIL]／响应阶段自动过 [FAIL]／牌谱里与手动那一手同形 [FAIL]

（浏览器）
时限到点了，自家的河却从 0 变成 0 张（该多一张）：到点那一手根本没打出去——「牌局必须继续」于是也就无从谈起
到点打出去的那一张不是摸切（data-tsumogiri=undefined）
到点打的是 undefined，而刚摸进的那张是 5pr
时限到点代打了一手，页面却记着 0 次
时限吃掉一手之后按「播放」等了 6 秒，牌桌还停在「还没走一手」：时限到点的要害是**牌局必须继续**
到点打过一手之后又等了 6 秒，代打次数仍是 0
```

### 4.1 一处「先绿后改硬」的记录

「超时那一手在牌谱里与手动那一手同形」第一版**停在一轮响应的中间**，
于是这一桌比牌谱多走了一个动作（`Action.None` 不产生事件），而牌谱末尾那记摸牌后面没有动作跟着
——回放重建自然少一条（票 85 那条截断牌谱的老账，**与到点那一手无关**）。
改法是把停的位置挑在**一手打牌刚落定处**（两边在同一个边界上比才算数），
并在停下之前多断言一条「到点那几手真的都记在那本账上、而他一次「过」都没按」。

---

## 5. 画出来长什么样（判据 7：图打开看过）

`/tmp/t89shots/89-assist.png`（1280×2235 整页）与 `89-assist-block.png`（那一块单独一张）
——**两张都自己打开看过**，一次性探针跑完删了（没进仓库）。

看到的那一屏（真人坐座位 0、信息辅助档、时限 30 秒、下家已立直）：

- **辅助那一块紧接在牌桌与那一排响应按钮下面**：抬头一行「信息辅助（引擎算的事实，不是建议）：
  现在 2 向听。有威胁的家：下家已立直」，接一句「这几个数与同桌模型拿到的是同一份（同一次引擎计算）。
  拨回「裸奔」就一个都不给，那才是一个坐在牌桌前的人本来看得见的。」
- 十来行，一条打牌动作一行，**等宽数字**上下对齐：
  `手切7筒：打完 2 向听，有效牌 8 枚：4筒(2) 3索(2) 7索(3) 东(1)（进退向 0）　危险度第 7 位（7p 筋（下家 4p 筋））`
- 名牌上写着「我自己・信息辅助」（票 82 那一句多了档位那一半）；
- 真人那一行在牌桌上方：「…（此刻 9 条）。还剩 30 秒（共 30 秒，到点自动摸切）。」
- 下面接着票 25 那块危险度面板（拨开时才有）——**两处的名次逐条相同**，那正是第二程对拍的那一条。

**看图看出来的一处**：危险度那半句里的牌是 mjai 记法（`7p 筋（下家 4p 筋）`），
而同一行前半句是中文（`手切7筒`）。它来自引擎的 `Danger.toDisplay`——**与牌桌上那块危险度
是同一个函数**（票 25 定的），改它要动 `src/Janpo.Engine/**`，本票的边界不许。记在 §7 nitpick 1。

---

## 6. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，70s；引擎 751 + 页面 **270** 条；浏览器**十七趟**全 ✓ |
| 新那一趟单跑 | `cd web && node scripts/verify-assist.mjs` | 13.2–14.0s，见 §3.2 |
| 每条新断言先红 | §4 的八次 | 全部红过（红-1 是补了 `peekHuman` 之后才红全的），原文抄在 §4 |
| 截图 | 一次性 playwright 探针（**没进仓库**，跑完删） | §5，两张都打开看过 |
| 还原干净 | 逐文件 `diff` 对回 `/tmp/t89bak/` | `TableState` / `HumanScaffold` / `TablePanel` / `HumanLine` 四样全 OK |
| 风格 | `dotnet fantomas .`、`scripts/check-style.sh`、`biome ci --error-on-warnings` | 全绿（`let mutable` 一处未新增） |

`jj diff --stat`：19 个文件，+2204 / −77。**新增三个文件**
（`src/Janpo.Web/HumanScaffold.fs`、`tests/Janpo.Web.Tests/HumanAssistTests.fs`、
`web/scripts/verify-assist.mjs`）。

**没碰**：`src/Janpo.Engine/**`（引擎一字未动：Shanten / Ukeire / Danger 的算法与 `Fallback` 都没动）、
`tests/Janpo.Engine.Tests/**`、`web/src/agent/**`（票 94 的地盘，渲染器摘要因此一字未变）、
`src/Janpo.Web/Review.fs` / `Playback.fs`（票 90 的地盘）、`Paifu` 格式、`DecisionRecord`、
`SeatChoice` 的形状、`docs/adr/*`、`CONTEXT.md`、`web/public/demo-paifu.json`、
`reveals` / `unlocked` / `lockedSeat` 的规则。**没有新增任何对 `render_version` 值的断言**。

**动了但不在票面那张清单上的四处**（都是「新增一趟闸门 / 加一格座位配置」绕不开的登记处）：
`web/scripts/verify-browser.mjs`（那张表 + 一个 import）、`web/package.json`（`verify:assist` 一行）、
`scripts/ci-web.sh`（末尾新增一段 + 十六趟 → 十七趟）、`web/scripts/seating.mjs`
（那张默认绑定表多一格 `clock`，否则闸门灌不进时限）。
`TablePage.fs` 只**在既有转出口之后追加了五行**（`humanTier` / `assists` / `humanScaffold` /
`humanClock` / `humanLimit`）与文件头那句「十八个入口」改成「二十多个」——**没有重排任何一行**（票 90 的地盘）。

---

## 7. code-review（Standards + Spec 两轴，fixed point `solktxlz` / `69c9f626`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓**：全程 `jj st` / `jj diff` / `jj log` / `jj commit`，无远端操作。
- **工具强制的**：`fantomas --check` / `check-style.sh` / Biome / tsc 全绿；**没有新增 `let mutable`**。
- **F# 风格**（`docs/agents/fsharp-style.md`）：
  - 规则 1/3：新代码全是从左往右的数据流（`humanBinding live |> Option.bind SeatBinding.limit`、
    `scaffold.Dahai |> List.collect … |> List.sortBy …`、`passes |> List.filter which |> List.length`）。
  - 规则 2：`Option.map (HumanClock.remaining >> string)`、`Option.map (Ukeire.total >> string)` 是正例；
    `fun binding -> binding.Tier` 是取字段不是调用，组合写不出来。
  - 规则 4.1 的「谓词套取值器」保留（`Option.isSome (handOf live)` 一处未动）。
  - 限制 B：`wound` 那几支是 `match` 不是布尔链，没有抽 `let` 破坏短路的问题。
- **注释写「为什么」✓**：为什么倒计时不存「轮到他了吗」、为什么每条消息重推一遍、
  为什么到点恒拿 Bare 那一支、为什么 `HumanPass` 拓宽一格而不是新造一张表、
  为什么危险度跟着 `assists` 走、为什么整页那一份要在「轮到他」那一刻抓、
  为什么有效牌那一段整段交给引擎的单向出口——都写在代码上。
- **术语 ✓**：`ScaffoldTier` / `Scaffold` / `Shanten` / `Ukeire` / `Danger` 全用 `CONTEXT.md` 的词；
  `ScaffoldLine` / `HumanClock` / `AutoPlayed` 是渲染层与页面状态的名字，日麻术语一个没自造。
  **`CONTEXT.md` 一字未改**（硬约束 5：没有授权）——提案见 §8 第 1 条与 `DECISIONS.md` 89-3。
- **ADR-0001 ✓**：中文只在渲染层的单向出口出现（`Ukeire.toDisplay` / `Danger.toDisplay` /
  `Shanten.toDisplay` / `Action.toDisplay`），判定一律不读它。**闸门读了一句中文**
  （「时限到点」那半句）——那是判据 8 点名要的语义闸门（页面说的话要与数据对得上），
  代价是措辞改了这一趟会红，那是有意的。
- **ADR-0002 ✓**：没有第二份牌局状态；`HumanClock` 只装「哪一手、几秒、上限」，
  「轮到谁」仍旧现问引擎。
- **ADR-0003 ✓**：`unlocked` / `lockedSeat` 一字未动；`assists` **读的是 `lockedSeat`**，
  因此判据仍旧挂在「对局配置与终局状态」上，没有第二条平行的可见性规则。
- **ADR-0005 ✓**：TS 一行没碰，跨界回来的仍旧只有一个 id。
- **blocking：0。**

### Spec（票面 6 条行为 + 4 条闸门 + 4 条边界）

逐条对照见票文件的勾选框。四处值得写下来：

- **「同一份数」落成了三层证据**：结构上（`ScaffoldLine.Trial` 抱着引擎那条记录）、
  用例上（与跨界 JSON 逐字段对拍，一整局）、页面上（辅助那几行与牌桌上那块危险度的名次多重集相同）。
- **「Bare 什么都不给」的清单逐字照术语表**：没有自己发明清单——给出去的正好是
  Shanten / Ukeire / ShantenDelta / Danger 四样（术语表 Assisted 那一句的全部内容），
  而 Bare 那一侧一样都不给（含危险度那一块）。
- **「超时自动摸切」向引擎要那一手**（`Fallback.action`），因此碰吃之后那一手（没有「刚摸进的那张」）
  与立直之后那一手（只许打仍听牌的）都不必这一层判规则。
- **边界四条**：不做 ToolSearch 的查询面板（真人席按 Assisted 处理）、不做复盘
  （`Review.fs` / `Playback.fs` 一个字节没碰）、引擎零改动、不做暂停/续下
  （播放语义一行没动——见 §3.2 ③ 那句「为什么要按一下播放」）。

### 记录但没改的 nitpick

1. **危险度那半句里的牌是 mjai 记法**（`7p 筋（下家 4p 筋）`），与同一行前半句的中文牌名
   （`手切7筒`）拉扯。它是引擎的 `Danger.toDisplay`，牌桌上那块危险度面板（票 25）用的是同一个
   ——改它要动引擎，本票的边界不许。要改的话是一票「`Danger.toDisplay` 的牌名改中文」，
   会同时改到那块面板与 prompt 里那一节（后者属票 94/95 的地盘）。
2. **辅助那一块在响应阶段仍旧画一行抬头**（那时 `Scaffold.Dahai` 是空的，只剩「现在几向听」）。
   有用（要不要碰得先知道自己几向听），但看上去像一块空表。真要收掉得先决定
   「响应阶段算不算轮到他看数的时候」——我判它算。
3. **`SeatField.Clock` 对模型席与 bot 席也会写进 localStorage**（`janpo.seats.<N>.clock=0`），
   只是没人读。跟着 `SeatField.all` 走的写法换来的是「加一格设定不必改 `Store`」，代价是
   四行无用的键。与 `tier` 对 bot 席的处境完全相同（票 73 就是这么留的）。
4. **这一趟 13.2s 里 11.6s 是死等**（§3.2 末）。以后要在时限这条路上再加闸门，
   先想清楚要不要每一条都真等一遍墙钟。

---

## 8. 留给人的待审项

1. **`CONTEXT.md` 的 `ScaffoldTier` 词条最后一句「真人坐席复用同一类型，它同时是新手辅助轮」
   从这一票起有了执行者**（`HumanScaffold.shows` + `TableState.assists`），
   而词条里还差两条今天已经成立的不变量：①「**Bare 档的真人在座时，危险度那一块与那一枚开关
   都不在页面上**」（执行者 `TableState.assists`，两个消费点）；②「**ToolSearch 在真人这一侧
   按 Assisted 处理**」（执行者 `HumanScaffold.shows`）。**改术语表要单票授权，因此一个字没写**，
   提案记在 `DECISIONS.md` 89-3，请主人裁要不要补进那一条词条。
2. **思考时限那一格没有上限校验**：填 `99999` 就是 27 小时（等于不限时），填 `1` 就是一秒一手。
   我判它是**主持人自己的事**（同 `TimeoutMs` 那一格），因此只挡了负数与读不懂的输入。
   要不要给一个下限（例如 ≥ 3 秒，防止误填 1 让整局自动打完）是产品口味，请主人裁。
3. **到点代打恒走 Bare 那一支**（§1.4）：Assisted 档的真人到点会拿到一手摸切，而不是
   「不退向听的安全打」。理由是「那一手不是他打的，平台不该替他用一遍辅助」，
   代价是同一个人拨到 Assisted 时，超时那一手比模型席的兜底更笨。请主人裁这个取舍。
4. **时限与「他与模型席同轮待答」那件事有一处交互**（票 88 报告 §9 第 2 条）：
   模型排在他前面时他要等模型答完才拿到按钮，而**那段等待不计入他的时限**（`handOf` 说了算）。
   这是对的（不该让他为模型的延迟买单），但主持人拿它模拟竞技压力时要知道这件事。
5. **首页那份资产没有重跑**（`demo-paifu.json` 一个字节没动）：这一票没有改任何模型侧的渲染，
   渲染器摘要与 `render_version` 一字未变。
