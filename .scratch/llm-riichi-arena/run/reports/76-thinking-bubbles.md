# 76 — 思考气泡：三态、按座位取的取值器、Live 与回放两边、点开看全文

**结论：气泡的数据源只有 `Table.Decisions` 一处，三态是三个 case，取值器是 `Seat -> Bubble option`。**
`./scripts/ci.sh` **EXIT=0**，41.4–44.1s（基线 36.6s）；dotnet 744 + **160** 条（原 143），浏览器闸门**十二趟**全 ✓
（新增第十二趟 `verify-bubbles`，**2.1–2.2s**）。

四件事落地：① 牌桌上每席一个气泡，**在想 / 说了什么 / 兜底代打**一眼分得开；② 点得开——全文面板九样
（thinking 全文、理由、prompt 尾部、动作 id 集、最终落定的动作、延迟、问了几次、Usage、渲染版本）
外加**当时的局面快照**；③ Live 与回放两边都要——回放沿游标、Live 走「导成牌谱 → `Table.replay` → 取那一帧」
（**实测点一下 6–62 ms**，§4）；④ 没有记录时不出气泡，页面上一句话说清为什么。
顺手那条也做了：**上帝视角未结算时不摆里宝牌指示牌**，结算那一屏照旧摆（阳性对照钉着）。

---

## 1. 三态的形状与取值器

### 1.1 `Bubble`：三个 case，两个直接抱着那条记录

```fsharp
[<RequireQualifiedAccess>]
type Bubble =
    | Thinking                                                  // 正在等这一席的回执（只有 Live 有）
    | Spoke of record: DecisionRecord                           // 上一次它自己说了什么
    | Troubled of record: DecisionRecord * reason: string       // 上一次是兜底代打的，附原因

Bubble.record   : Bubble -> DecisionRecord option   // 「在想」没有记录 → **它点不开**
Bubble.toWire   : Bubble -> string                  // thinking / spoke / troubled（`data-bubble`）
Bubble.toLabel  : Bubble -> string                  // 在想 / 说 / 兜底（气泡头上那两个字）
Bubble.toDisplay: Bubble -> string                  // 气泡上写的那句话
```

**不许存第二份**（`CONTEXT.md` 的 `Thinking Bubble` 词条：它是展示某个 `DecisionRecord` 的 UI 部件，
不要用它指代数据本身）：两个 case 抱的就是 `Table.Decisions` / `Paifu.Decisions` 里那条记录本身，
气泡这一侧一个字段都不复制。**`AgentStatus.Spoke` 里那句理由不读**——它只是同一条记录的另一份拄件，
而且只有最新一手。

**三态刻意是三个 case 而不是一个带 option 的记录**（判据 12）：「在想」根本没有记录可读，
「兜底」必然有一句原因；混成一个记录的话视图里每一处都要重判一遍「这一格有没有值」，
而那正是三态看不出区别的开头。

`toDisplay` 的取舍：**thinking 优先，没有思考预算时退回那一句理由**（票面原话），两样都没有时
写「（这一手没留下理由与思考原文）」——**空气泡与「没有气泡」在页面上分不出来**。
兜底那一态写的是**原因**，与牌桌上那句「上一手：……（兜底：……）」、`data-fallback` 同一个来源
（`DecisionRecord.Fallback`）；它的 thinking 与理由仍在全文面板里。

### 1.2 取值器：`Seat -> Bubble option`（票 74 只换它的实现）

```fsharp
TableState.bubbles : TableModel -> Table -> (Seat -> Bubble option)
TablePage.bubbles  : 同上（转出去的那个名字，dotnet 侧用例调的是它）
```

```fsharp
let asking =                                   // ← **票 74 换掉的就是这一段**
    live model |> Option.bind (fun live -> match live.Agent with AgentStatus.Asking seat -> Some seat | _ -> None)

fun seat ->
    if not (unlocked model table) then None
    elif asking = Some seat then Some Bubble.Thinking      // 「在想」压过上一条记录
    else
        table.Decisions
        |> List.tryFindBack (fun record -> record.Seat = seat)
        |> Option.map (fun record -> match record.Fallback with
                                     | Some reason -> Bubble.Troubled(record, reason)
                                     | None -> Bubble.Spoke record)
```

三件事写在这个形状里：

| 要求 | 落在哪 |
|---|---|
| 「在想」按座位取，票 74 只换实现 | `asking` 那一段（今天挂的是 Agent 层**单席**的 `AgentStatus`）；视图与全部用例读的都是这个函数 |
| bot 席 / 分享链接那种棋谱不出气泡 | `tryFindBack` 找不到就是 `None`——**没有记录就没有气泡**，不必另立开关 |
| 回放与 Live 用同一份 | 两边都读**这一帧**的 `Table.Decisions`（回放那一侧票 71 的 `recordedBy` 已按手序切好） |

**可见性判据挂在对局配置与终局状态上**（ADR-0003 的 consequence）：

```fsharp
let private humanSeated (_: TableModel) : bool = false          // **恒 false，M3 才改这一处**
let private unlocked (model) (table) = not (humanSeated model) || Table.result table |> Option.isSome
```

`humanSeated` 是**写在代码里的一个值而不是注释里的旁白**（判据 4）：座位今天只有自带 bot 与模型两种
（`SeatPlayer` 就两个 case），真人坐席是 M3，因此术语表那句「有真人参与时终局前隐藏」**今天谁也到不了**。
**没有为那条走不到的支路立断言**（判据 3）；立的是它的**反面**且真的跑得到的那一条：
`可见性判据不看谁在看：五个视角下四席的气泡逐个相同`——切视角改不动气泡，就是那条 consequence 的执行体。

### 1.3 全文面板：`BubbleDetail = { Record; Snapshot }`

```fsharp
TableState.detail : TableModel -> BubbleDetail option     // 摊开的那一手；没点开时 None
TableMsg.RecordOpened of turn: int option                 // 点开 / 收起（turn 是手序）
TableModel.Opened : Table option                          // **存的是那一手落定之后的那一帧**
```

**记录不存第二份**：`Record` 是从 `Snapshot` 上现读的（`recordOf`，与时间轴上那一格同一处推导），
因此快照与记录不可能对不上。**「最终落定的动作」也从快照上来**：牌谱里存的是**包内 id**
（26-3：意图不上牌谱），而那一帧的 `Latest` 就是那一手真落进引擎的动作——面板上印的是
`手切6万（包内 id 0）`。

页面上的 testId：气泡 `seat-{N}-bubble`（带 `data-bubble` / `data-bubble-turn`），
面板 `table-bubble-detail`（带 `data-bubble-turn` / `data-bubble-seat`），
面板里各行 `bubble-at` / `bubble-applied` / `bubble-fallback` / `bubble-reason` / `bubble-thinking` /
`bubble-prompt` / `bubble-actions` / `bubble-meta` / `bubble-version`，收起来那一枚 `bubble-close`，
没有记录时那句话 `table-no-bubbles`。

**票 75 那段应急形态（`table-replay-record`）整个删了**——它明说等这一票换掉。`Timeline.Record` 留着
（票 75 的用例钉着它，且 `recordOf` 被全文面板复用）。

---

## 2. 「点开」这件事的三条取舍

1. **回放里点气泡 = 把游标挪到那一帧**。轴只有票 75 那一根，全文面板**不另开一条时间轴**
   （票面边界：不做第二套时间轴）；于是牌桌自己就是那一手的快照，两种来源只有一份渲染。
2. **Live 里只摆快照，`live.Table` 一字不动**。story 5 的「局面快照」不是另画一张牌桌，而是
   `shown` 那一个出口指到那一帧上（`model.Opened` 压过 `model.Source`）。用例钉着「这一桌一手都没退回去」
   （事件流逐条相同）与「收起来就回到现在」。
3. **一点就暂停，一推进就收起来**。前者与「一拖就暂停」同一条判据；后者是 `moves : TableMsg -> bool`
   （逐 case 穷举，加新消息时编译器会指出来）。`Ticked` **刻意不在** `moves` 里：一点开就暂停，
   而重新按「播放」本身就在 `moves` 里，因此面板摊着时不可能有被接受的 `Ticked`——
   列上它反而会让一记**过期**的定时器把面板关掉。

**没做**（票面边界）：流式 thinking（`Agent.ask` 一行没改）、气泡历史滚动列表、`Paifu` 格式、
`web/src/agent/**`、引擎。

---

## 3. 每条新断言先红一次（判据 1 的原始输出）

**十九次，全部实跑**：改**产品代码**（不是改断言），跑同一条命令，抄红的原文，然后 `diff` 对回备份。

### dotnet 侧（`ThinkingBubbleTests`，17 条；命令 `dotnet test tests/Janpo.Web.Tests --filter ThinkingBubbleTests`）

**红-1｜取值器不按座位取**（`List.tryFindBack (…Seat = seat)` → `List.tryLast`）

```
ThinkingBubbleTests.一席一条：同一席说了两次，气泡上是新的那一条 [FAIL]
ThinkingBubbleTests.回放里拖动游标：气泡跟着换成那一手的记录，拖到还没有记录的那几帧就消失 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: null / Actual: Some(Spoke { Turn = 9
ThinkingBubbleTests.四席都有记录时四个气泡都在，各说各的那一条 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: Some(Spoke { Turn = 7 Seat = { Index = 0 } …
失败! - 失败: 3，通过: 13，总计: 16
```

**红-2｜「在想」不压过上一条记录**（`elif asking = Some seat && List.isEmpty table.Decisions`）

```
ThinkingBubbleTests.在想压过上一条记录：这一席上一手说过话也一样 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: Some(Thinking) / Actual: Some(Spoke { Turn = 0 …
失败! - 失败: 1，通过: 15，总计: 16
```

**红-3｜thinking 与理由的优先级反过来**

```
ThinkingBubbleTests.说了什么：thinking 优先，没有思考预算时退回那一句理由 [FAIL]
  Expected: Some(第 7 手的思考原文（座位 0）) / Actual: Some(第 7 手的一句话理由（座位 0）)
ThinkingBubbleTests.气泡里的字来自那一手的决策记录：改一个字，气泡跟着变 [FAIL]
  Assert.NotEqual() Failure: Values are equal
失败! - 失败: 2，通过: 14，总计: 16
```

**红-4｜兜底不另立一态**（一律 `Bubble.Spoke`）

```
ThinkingBubbleTests.兜底那一手：气泡是兜底态、写着原因，与 data-fallback 同源 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: Some(Troubled ({ Turn = 7 …
失败! - 失败: 1，通过: 15，总计: 16
```

**红-5｜回放的帧不带那一手的记录**（`Table.replay` 里退回 `apply`，也就是这一票改它之前的样子）

```
ThinkingBubbleTests.兜底那一手：气泡是兜底态、写着原因，与 data-fallback 同源 [FAIL]
  Expected: Some(模型超时（60001 ms 没答完）（重试 2 次仍无结果）) / Actual: null
失败! - 失败: 1，通过: 15，总计: 16
```

**红-6｜`frameOfTurn` 在局边界上取错帧**（`tryFindIndex` → `tryFindIndexBack`）

```
ThinkingBubbleTests.跨局边界也点得开：快照是那一手落定之后那一帧，不是下一局的开局帧 [FAIL]
  System.Exception : 第 91 手（第一局最后一手）该摊得开
失败! - 失败: 1，通过: 16，总计: 17
```

**这一条先怀疑了断言自己（判据 6）。** 头一版改坏法是去掉 `&& Option.isSome frame.Latest`
（票 75 红-7 的同形错法），**全绿**——因为 `tryFindIndex` 的顺序已经保证了先撞上真落定那一手的那一帧。
于是补了一条**跨局边界**的用例（`lastTurnOfFirstKyoku`，从帧上现算），换成 `tryFindIndexBack` 才咬得住。
那个 `&& Option.isSome` 留着：它是这条不变量的明说，而结果由新那条用例钉住。

**红-7｜可见性挂在「谁在看」上**（`unlocked` 里加 `model.Viewpoint = Viewpoint.God`）

```
ThinkingBubbleTests.在想那一态是按座位取的：正在等谁的回执，谁头上就是它 [FAIL]
  Expected: Some(Thinking) / Actual: null
ThinkingBubbleTests.可见性判据不看谁在看：五个视角下四席的气泡逐个相同 [FAIL]
  Assert.Equal() Failure: Collections differ
ThinkingBubbleTests.在想压过上一条记录：这一席上一手说过话也一样 [FAIL]
  System.Exception : 答过话之后该是「说了什么」，却是
失败! - 失败: 3，通过: 14，总计: 17
```

**红-8｜`recordless` 拿这一帧当判据**（那句话会在开局闪一下）

```
ThinkingBubbleTests.牌谱里一条决策记录都没有：四席一个气泡都不出，而且页面上说得出为什么 [FAIL]
  Assert.False() Failure　Expected: False / Actual: True
失败! - 失败: 1，通过: 16，总计: 17
```

**红-9｜推进牌桌的消息不把面板收起来**（`moves` 里去掉 `PlayToggled` / `Restarted` / `CursorMoved`）

```
ThinkingBubbleTests.拖一下时间轴就把全文面板收起来：牌桌走了，面板不许留在原地 [FAIL]
  Assert.True() Failure　Expected: True / Actual: False
失败! - 失败: 1，通过: 16，总计: 17
```

**红-10｜Live 侧点历史某一手时牌桌不摆快照**（`shown` 不看 `model.Opened`）

```
ThinkingBubbleTests.Live 里点历史某一手：牌桌摆的是当时的快照，而这一桌一手都没退回去 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: 1 / Actual: 7
失败! - 失败: 1，通过: 16，总计: 17
```

**红-11｜Live 侧导牌谱时把审计抹掉**（`Table.paifu |> Paifu.stripAudit |> Table.replay`）

```
ThinkingBubbleTests.Live 里点历史某一手：牌桌摆的是当时的快照，而这一桌一手都没退回去 [FAIL]
  System.Exception : Live 里点历史某一手该摊得开
失败! - 失败: 1，通过: 16，总计: 17
```

### 浏览器侧（`node scripts/verify-bubbles.mjs` 与 `node scripts/verify-home.mjs`）

**红-12｜气泡压在牌上**（`.bubble { position: relative; top: 2.5rem }`）

```
思考气泡这一道没过：
seat-0-bubble 压在 seat-0-kawa 上了
（兜底那一屏）seat-0-bubble 压在 seat-0-kawa 上了
```

**红-13｜气泡飞出那一席**（`.bubble { position: fixed; top: 0; left: 0 }`）

```
思考气泡这一道没过：
seat-0-bubble 画到那一席的框外面去了
（兜底那一屏）seat-0-bubble 画到那一席的框外面去了
```

**这一条是红-12 顺出来的**：先试的是 `position: absolute`，闸门**全绿**——Chrome 把它摆在
flex 容器的静态位置上，仍在那一席的框里、也没压住任何一排牌（探针读到的矩形：气泡
`x=400 w=480`，那一席 `x=96 w=1088`）。于是补了「气泡得画在那一席的框里」这一条，
用 `position: fixed` 把它按红。**一个飞到页面角上的气泡与谁都不相交**，没有这一条那一圈照样绿。

**红-14｜气泡整个不画**（取值器恒 `None`）

```
思考气泡这一道没过：
模型那一席（座位 0）上一直没有出气泡，底下那几条因此一条都没验到
页面上 Agent 那一行说的是：座位 0 的模型选完了（4 ms）：假端点甲说：这一手照它的算法只能这么打
```

（第二行是**诊断**：模型明明说了话，因此红的是气泡不是端点。这一条同时改掉了闸门的一个真毛病——
原来它在 `waitForFunction` 上抛 `TimeoutError`，那会把合并跑的十二趟一起搞挂；现在超时是一份失败清单。）

**红-15｜气泡的字不来自那一手的记录**（`Bubble.toDisplay` 的 `Spoke` 支写死一句话）

```
思考气泡这一道没过：
气泡里的字不是那一手记录里的那句：看到的是「说它想了想，选了这一张」
```

**红-16｜兜底那一态与 `data-fallback` 不同源**（按 `Reason` 是不是 None 分态）

```
思考气泡这一道没过：
气泡上的兜底原因与牌桌上那句对不上：气泡「交不出来」／牌桌「上一手：座位 0 摸切7筒（兜底：action_id=9999 不在这一手的合法动作集里（重试 2 次仍无结果））」
```

**红-17｜未结算也摆里宝牌**（`tableCenter` 里去掉 `not settled`，也就是这一票改它之前的样子）

```
首页少了该给访客的东西：
一局刚开就把「里宝牌指示牌」摆在桌心了：它只在有人立直和了的那一刻才翻开、才算番
```

**红-18｜里宝牌整个不画（阳性对照）**

```
首页少了该给访客的东西：
结算那一屏上没有「里宝牌指示牌」：上一条「未结算不摆」因此什么都没证明（它可能是整个不画了）
```

**红-19｜没有记录时不说那句话**

```
首页少了该给访客的东西：
首页上没有那句「为什么没有思考气泡」（[data-testid="table-no-bubbles"]）：「本来就不带推理」与「气泡坏了」因此分不开（票 76）
```

---

## 4. Live 侧那条快照路的实测耗时

量的是**点下气泡 → 全文面板与快照都画完**的墙钟（含 `Table.paifu` 拼事件流 + `Table.replay` 逐帧 fold
+ React 重画整张牌桌 + 画面板），一次性 playwright 探针（**没进仓库，跑完删了**），
本机 dev server + 假端点，每档开 5 次取中位：

| 打到第几手 | 点开一次（5 次） | 中位 |
|---|---|---|
| 21 | 6/6/6/7/9 ms | **6 ms** |
| 81 | 15/15/15/17/19 ms | **15 ms** |
| 198 | 33/33/33/35/39 ms | **33 ms** |
| 395 | 55/57/58/61/61 ms | **58 ms** |
| 429 | 59/61/62/62/63 ms | **62 ms** |

**线性，约 0.15 ms/手**，外推一整个半庄（约 700 手）≈ 100 ms。人手点击的感知阈值一般取 100 ms，
**东风战全程都在它的六分之一以内**。票面那句「点一下算一次完全够」实测成立，
因此 **Live 侧没有常驻帧数组**（票面明令），也没有缓存——`liveFrames` 每次点开现算。

半庄打满之后会踩到 100 ms 那条线：真到了那一天该做的是**只 fold 到那一手为止**
（`Replay.trace` 的动作序列截断），而不是常驻一份帧——那样每落一手都得重算一遍。这是另一张票。

---

## 5. 截图：我亲眼看到了什么（判据 7）

三张，**都自己打开看过**。

### 5.1 首页 `docs/images/home.png`（重出，1088×1006 → 现在多一行字）

从上到下：h1 → 介绍段 →「自己开一桌 →」→ 三排控件（播放 / 时间轴 / 局边界）→ 视角排 →
「上一手：座位 0 手切白」→ **新的一行**：

> 这一局没有思考气泡：牌谱里一条决策记录都没有——要么四家都是自带 bot，要么这是一条只带棋谱的
> 分享链接（推理不上 URL，完整版得让对方把 JSON 给你）。

→ 牌桌。四家的牌都摊着（71-8 照旧），**四家旁边一个气泡都没有**（这份 Demo 是 bot 牌谱，对的）。

**桌心那一行「里宝牌指示牌 赤5筒」没了**（报告 75 §5 第 2 条说的就是它）：现在只剩
`场况 东1局 0 本场 / 供托 0 根 / 剩余摸牌 50 张 / 宝牌指示牌 7筒`。拖到末帧（结算那一屏）它回来
——闸门两头都核（红-17 / 红-18）。

### 5.2 气泡与全文面板（`node scripts/verify-bubbles.mjs --shoot <目录>`，1280×2209）

`?table=1`、座位 0 交给假端点、走到第 21 手点开它的气泡。我看到的：

- **气泡在座位 0 那一格里，朝桌心那一侧**（自家那一块是 `column-reverse`，它因此浮在最上、
  紧挨着牌桌中央），一个 30rem 宽的圆角框：`说 假端点甲说：这一手照它的算法只能这么打`。
  **它没压住任何一张牌**：它下面依次是河 6 张、手牌 7 张（`赤5筒` 仍是红字）、两组副露。
- 另外三席（自带 bot）**一个气泡都没有**——那三格与票 44 那一版逐字相同。
- 牌桌下面是全文面板：`第 21 手・座位 0・东1局 0 本场` + 「收起」，然后逐行
  `最终落定 手切6万（包内 id 0）` / `兜底 （不是兜底：它自己决的）` / `一句话理由 …` /
  `thinking 全文 （这一手没有思考原文：多半关着思考预算）` / `prompt 尾部 【现在】…（自己滚动）` /
  `动作 id 集 0、1、2、3、4、5、6` / `这一次问话 延迟 4 ms・问了 1 次・输入 814 tok（缓存命中 0，0%）、输出 94 tok` /
  `渲染版本 janpo-default@08fcaec3.4b9e57c0`。
- 面板上那句「上面那张牌桌就是这一手落定那一刻的局面快照（只读；这一桌该怎么走还怎么走）」。

### 5.3 兜底那一态（同一道闸门的第二张）

把 baseUrl 换到只回越界 id 的端点、再走一手之后：**同一位置的气泡变成红字加红色左边框**，
写着 `兜底 action_id=9999 不在这一手的合法动作集里（重试 2 次仍无结果）`，
与牌桌上那句「上一手：座位 0 摸切7筒（兜底：……）」逐字同源。
**三态在一张图里就分得开**：虚线淡色 = 在想、实线 = 说了什么、红 = 兜底。

**第一版气泡是整行宽的**（上下两家占满整行，气泡因此拉到 40rem），看着不像一句话；
加了 `max-width: 30rem` 与「上下两家居中」两条之后就是上面这个样子。CSS 净增约 70 行，
没有阴影、渐变、动画（票面：CSS 不许过夜）。

---

## 6. 闸门：谁在守什么

### 6.1 dotnet 侧（`tests/Janpo.Web.Tests/ThinkingBubbleTests.fs`，17 条）

| 用例 | 钉的是 |
|---|---|
| 牌谱里一条决策记录都没有：四席一个气泡都不出，而且页面上说得出为什么 | 没有记录 → 没有气泡 + `recordless`（带阳性对照：拌了记录之后它为 false） |
| Live 那一桌不说那句话：模型随时可能开口 | `recordless` 在 Live 恒 false |
| 说了什么：thinking 优先，没有思考预算时退回那一句理由 | 三种记录（全 / 无 thinking / 两样都无）各一句话 |
| 气泡里的字来自那一手的决策记录：改一个字，气泡跟着变 | **数据源只有一处**（改记录 → 气泡跟着变） |
| 兜底那一手：气泡是兜底态、写着原因，与 data-fallback 同源 | 三态之一 + 与 `Turn.Fallback` 同源 |
| 三态给机器看的那一半各不相同 | `data-bubble` 三个值 |
| 四席都有记录时四个气泡都在，各说各的那一条 | **按座位取**（四席四条不同的记录） |
| 一席一条：同一席说了两次，气泡上是新的那一条 | `tryFindBack`；别人仍然没有 |
| 在想那一态是按座位取的 | Live 里 `Asking` 的那一席是 Thinking，其余三席 None |
| 在想压过上一条记录 | 说过话之后再问一次，仍是 Thinking |
| 回放里拖动游标：气泡跟着换，拖到还没有记录的那几帧就消失 | 一手一手拖过去，每落一手多一个气泡；拖回 0 全消失 |
| 可见性判据不看谁在看：五个视角下四席的气泡逐个相同 | **ADR-0003 consequence 的执行体** |
| 点开气泡：全文面板给的是那一手的记录与当时的局面快照 | 九样的来源 + 快照是「那一手落定之后那一帧」 |
| 跨局边界也点得开 | 局边界上两帧同手数，摊开的必须是真落定那一手的那一帧 |
| 回放里点开某一手：游标跟着挪到那一帧，轴只有一根 | 一根轴；收起不跳回去 |
| 拖一下时间轴就把全文面板收起来 | `moves`（拖 / 播 / 重开三条各一次） |
| Live 里点历史某一手：牌桌摆的是当时的快照，而这一桌一手都没退回去 | story 5 的 Live 那一半 + **只读**（事件流逐条相同）+ 一点就暂停 |

### 6.2 浏览器侧（第十二趟 `verify-bubbles.mjs`，2.1s；**两个本机假端点，零网络**）

端点甲好好答话（`--reason「假端点甲说：…」`，这句话**只可能从端点那儿来**），端点乙固定回越界 id
（`--action-id 9999` → Agent 层重试两次 → 兜底）。走几手之后逐条核：气泡里的字**一字不差**是端点回的那句、
`data-bubble-turn` 写着第几手、bot 那三席一个气泡都没有、**气泡与四家的三排牌 / 牌桌中央的矩形一律不相交
且画在那一席的框里**（读的是 `getBoundingClientRect`，不是「我们没写 position: absolute」这句承诺）、
点开之后九样都在、牌桌跟着回到那一刻（河从 20 张回到 18 张）、收起来 DOM 摘要逐字回到点开之前，
最后把 baseUrl 换到端点乙：气泡变「兜底」态，原因与 `data-fallback` 同源。

**`fake-endpoint.mjs` 多了一个 `--reason`**（那句话得能换，否则「改记录 → 气泡跟着变」在浏览器里证不了）。

### 6.3 `verify-home.mjs`：六条加到八条

⑦ **这份牌谱不含推理**：一个 `seat-N-bubble` 都没有，且 `table-no-bubbles` 那句话在。
⑧ **未结算不摆里宝牌**：跳到第二局的开局帧上没有 `table-uradora`，**拖到末帧（结算那一屏）它必须在**
——后半句是阳性对照，没有它「里宝牌整个不画了」同样能让上一句变绿（红-18 证过）。

票 44 的八项与方位断言、票 51 的副露位置、票 71/75 的那六条**一条没动、全绿**。

---

## 7. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，两次 41.4s / 44.1s（基线 36.6s）；引擎 744 + 页面 **160** 条 |
| 浏览器十二趟 | `cd web && node scripts/verify-browser.mjs` | 全 ✓（新那趟 2.1–2.2s，十二趟合计约 11s） |
| 气泡那一道单跑 | `node scripts/verify-bubbles.mjs` | 三段各印一行 ✓ |
| 首页那一道单跑 | `node scripts/verify-home.mjs` | 八条各印一行 ✓ |
| 每条新断言先红 | §3 十九次 | 全部红过，输出抄在 §3 |
| Live 侧快照耗时 | 一次性 playwright 探针（**没进仓库，已删**） | §4 |
| 截图 | `shoot-table.mjs --home` / `verify-bubbles.mjs --shoot` | §5，三张都打开看过 |
| 还原干净 | `diff` 对回 `/tmp/t76bak/*` | `TableState` / `ThinkingBubble` / `TableBoard` / `Table` / `styles.css` 五样全 OK |

`jj diff --stat`：18 个文件。**没碰**：引擎（`src/Janpo.Engine/**` 一字未动）、`web/src/agent/**`、
`Paifu` 格式、`docs/adr/*`、`CONTEXT.md`、别人的票、`TablePanel.llmPanel` 与 `setup`（票 73 的地盘）、
`Store.fs`、`TableBoard` 的副露与河（票 44/51 的地盘）。

---

## 8. code-review（Standards + Spec 两轴，fixed point `6cede36e`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓**：全程 `jj status` / `jj diff` / `jj describe` / `jj commit`，无远端操作、无交互式 flag。
- **工具强制的** `fantomas --check` / `check-style.sh` / Biome / tsc 全绿；引擎 `let mutable` 未新增（预算仍是 2）。
- **F# 风格**（`docs/agents/fsharp-style.md`）：新代码里没有规则 1/2/3 的形状——
  `table.Decisions |> List.tryFindBack … |> Option.map …` 是一条从左往右的数据流；
  `record.Thinking |> Option.orElse record.Reason |> Option.defaultValue …` 同理；
  `model.Opened |> Option.bind (…)` 没有从里往外读的嵌套。规则 4.1 的「谓词套取值器」保留
  （`Option.isSome frame.Latest`、`Option.isNone turn`），正确。规则 5：没有新 `let mutable`。
- **注释写「为什么」✓**：三态为什么是三个 case、取值器为什么是 `Seat -> …`（票 74 换实现）、
  `humanSeated` 为什么恒 false、`Ticked` 为什么不在 `moves` 里、Live 侧为什么不常驻帧数组——都写在代码上。
- **术语 ✓**：`Bubble` / `BubbleDetail` 是渲染层的名字，doc 里指回 `CONTEXT.md` 的 `Thinking Bubble` 词条；
  日麻术语（`Seat` / `Turn` / `Fallback` / `DecisionRecord`）一个没自造。**`CONTEXT.md` 一字未改**。
- **ADR-0003 ✓**：可见性判据挂在对局配置与终局状态上（`unlocked`），不挂在视角上（用例钉着）。
- **blocking：0。**

### Spec（票面四条行为 + 六条闸门 + 四条边界 + 顺手那一条）

逐条对照见票文件的勾选框。三处值得写下来：

- **「点历史某一手」的入口就是气泡本身**，没有另做一份历史列表（票面边界：不做第二套时间轴 /
  气泡历史滚动列表）。每席的气泡指的就是**那一席最近说过的那一手**，它已经是历史上的一手。
- **回放里点气泡会挪游标**：这是「轴只有一根」的直接后果，也让两种来源共用同一份快照渲染。
- **`src/Janpa.Web/Table.fs` 的 `Table.replay` 改了两行**（scope creep，记在 DECISIONS 76-4）：
  回放的帧现在带着那一手的 `Turn.Fallback`。不改的话「气泡说兜底、牌桌一声不响」，
  而票面明写气泡的兜底态要与 `data-fallback` 同源。

### 记录但没改的 nitpick

1. `web/scripts/verify-setup.mjs` 的文件头还写着「十一趟共用一个浏览器」（现在十二趟）。
   那是票 72 的文件，而票 73 可能正在动它，**没碰**——留给集成时顺手改。
   （同一处腐烂在票 75 时就发生过一次：那时它写的是「十道」。）
2. `Timeline.Record` 现在只有用例在读（视图那一头换成了气泡）。它仍是「刚落定那一手」的唯一推导，
   而 `recordOf` 被全文面板复用，因此没有删；真要收敛得连票 75 那条用例一起重写，不划算。
3. **全文面板里 prompt 尾部与 thinking 那两格各自滚动**（`max-height: 14rem`）。整段贴出来会把
   牌桌顶出屏幕，而票面要的是「点开看全文」——滚动条是最省的做法，没做折叠/展开。
4. `liveFrames` 在 `Table.paifu` 那一步会把整场事件流重拼一遍。§4 的数字是**含它**的，
   因此没有单独优化；真要省，`Replay.trace` 那一层截到第 N 手就够（另一张票）。

---

## 9. 留给人的待审项

1. **首页现在多了一句「这一局没有思考气泡……」**（§5.1）。它是票面第四条要求的那句话，
   但它出现在**产品门面**上（ADR-0003：Demo Paifu 决定陌生人的第一印象）。
   票 79 换上带推理的真资产之后这句话自己就消失、四个气泡接上——**在那之前它是首页的一部分**，
   主人若嫌它扫兴，改的是 `ThinkingBubble.note` 一处措辞。
2. **里宝牌那条改的是渲染层不是投影**（`TableBoard.tableCenter` 收一个 `settled`）：
   `Viewpoint.God` 的投影里仍然有 `UraMarkers`（`BoardTests` 那两条因此一字未改）。
   理由：上帝视角本来就是全知的，误导的是**桌心那一行字**而不是那份数据。
   若认为该在投影里就没有，那是 `Board.ofTable` 的一行改动 + `BoardTests` 两条用例改期望——
   **没有在这一票做**（改别人的用例期望要单票授权）。
3. **一桌今天只坐得下一席模型**，因此「四席都是模型时四个气泡都在」这一条只在 dotnet 侧
   （拌四条记录进牌谱）验得到，浏览器那一侧验的是「一席有、三席没有」。**票 74 落地后**
   应当把 `verify-bubbles` 那一趟改成四席各配一个假端点——那时它才在真语料上验到四个气泡。
4. **票 79 换资产时**：这一票没有动 `HomePageTests` 那条 512 KB 的体积断言（报告 75 §4.4 第 2 条仍成立）。
