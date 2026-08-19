# 90 — 复盘：逐手对照标注

**结论：复盘是一份现算的投影，不是一份新数据——`Paifu` / `DecisionRecord` / `LiveTable` /
`SeatChoice` 的形状一个字节都没动。** 每一条标注上的每一个数都由引擎的 `Scaffold.calculate`
对着**那一手落定之前那一帧**现算（`DecisionPackage.forSeat` 那一份，与当时真的问出去的那一份
同一个构造子）。`./scripts/ci.sh` **EXIT=0**，57.2s；dotnet 750 + **252** 条（原 750 + 243），
浏览器闸门从十六趟变**十七趟**（新增 `verify-review`，**2.8s**）。

五件事落地：① **只在终局后**——对局中复盘那一块**整个不在 DOM 里**（阴性对照真按红过，§4 红-1）；
② 真人那一席**每一手都有一条**（「过」也占一手），条数与手序等于引擎另一条路走出来的那一份；
③ 那几个数（向听 / 有效牌 / 危险度）与引擎**逐字相同**，闸门拿 `Scaffold` 的输出对拍，
dotnet 那一侧再拿 `Ukeire.calculate` 绕开脚手架核一遍（第三个锚点）；
④ **更好的候选**是帕累托占优（**没有总分**），构造一个一整场都在明显打错的局面证明它列得出来；
⑤ 点某一手 → 游标跳过去 → 「回到原处」回得来——**走的就是票 76 那条 `RecordOpened`**，
轴只有票 75 那一根（ADR-0002）。

**强 AI 那一行今天一个占位都没有**（票 93）：DOM 上没有元素、页面上没有「暂无」，
闸门里那条「一个都没有」等着 93 去翻面。

---

## 1. 形状：三个新文件，`TablePage` 里一行挂载

| 文件 | 是什么 | 谁读它 |
|---|---|---|
| `src/Janpo.Web/Review.fs` | **判据与算法**（一行 Feliz 都不 open） | 视图、dotnet 用例 |
| `src/Janpo.Web/ReviewPanel.fs` | **只画**（与 `HumanLine` / `AgentLine` 同一个形状） | `TablePage.layout` 的那一行 |
| `src/Janpo.Web/ReviewCheck.fs` | 无头闸门的**锚点**（与 `Golden.fs` / `PaifuCheck.fs` 同一种东西，不在页面里） | `verify-review.mjs` |

```fsharp
Review.settled   : TableModel -> bool                 // 这一场打完了吗（面板在不在 DOM 里就看它）
Review.addressed : TableModel -> Seat option          // 复盘对着谁（打完了才有主语）
Review.notesFor  : Seat -> Table list -> ReviewNote list   // 纯的：用例与闸门的锚
Review.notes     : Seat -> TableModel -> ReviewNote list
Review.shown     : TableModel -> ReviewShown          // Hidden | Unaddressed | Notes(seat, notes)
Review.opened    : TableModel -> int option           // 此刻摊开的是第几手
Review.signature : TableModel -> int                  // `React.useMemo` 的依赖（只给一个整数）
```

### 1.1 一条标注就是「那一帧交给引擎再问一次」

```fsharp
let private noteOf (seat: Seat) (frame: int) (before: Table) (after: Table) : ReviewNote option =
    after.Latest |> Option.map (fun turn -> turn.Action)
    |> Option.filter (fun action -> Action.actor action = seat)
    |> Option.map (fun action ->
        let scaffold = DecisionPackage.forSeat seat before.State |> Option.bind DecisionPackage.scaffold
        …)
```

**逐帧的牌桌本来就在那儿**（`Table.replay`，票 71 的形态）：帧 `i` 是第 `i` 手落定之后的那一桌，
于是「这一手打之前的局面」就是它的前一帧。**这一票因此没有新的数据源**——
复盘读的是牌谱，而牌谱早就够了（票面边界：不做「导出复盘报告」这类新产物）。

**脚手架现问 `DecisionPackage.forSeat`**（判据 11：要读规则才做得出的决定归引擎）：
复盘看到的数与当时模型 / 真人手上那一份**必然相同**，因为它们是同一个构造子的两次调用，
这一层没有第二条算路。

### 1.2 那几个数各是引擎的哪一个

| 页面上那一句 | 引擎里那一个 |
|---|---|
| `2 向听 → 2 向听` | `Scaffold.Shanten`（打之前，含刚摸那张）→ `DahaiScaffold.Shanten` |
| `（退向）` | `DahaiScaffold.ShantenDelta > 0`（CONTEXT.md 的 `ShantenDelta`） |
| `有效牌 21 枚 8 种` | `Ukeire.total` / `Ukeire.kindCount`（`DahaiScaffold.Ukeire`） |
| `危险度 无依据（这一手第 9 安全）` | `DangerTier.toDisplay` / `Danger.Rank`（`DahaiScaffold.Danger`） |
| `更好的候选：打8筒（有效牌 39 枚，+18；危险度更低（现物））` | 同一张试打表里的另几条 |

**「有效牌 33 → 21」那个箭头没做**，票面的示意里有它——因为**打之前那副 14 张手牌的有效牌
引擎不定义**（`Ukeire.calculate` 对非等摸形直接回 `HandNotAwaitingDraw`）。捏一个出来就是发明数值，
所以有效牌只报「打完之后是多少」，而「本来能有多少」由下面那一行的候选说（`+18` 就是那个差）。
向听那一条有前后两头，因为引擎两头都算得出来。取舍记在 `DECISIONS.md` 90-1。

### 1.3 「更好」= 帕累托占优，**不是总分**（票面明令）

```fsharp
// 向听不更差、有效牌不更少、危险度不更高，且至少一项严格更好。
// **算不出来的那一项不参与比较**（有效牌在可见张数越界时是 None）——拿 None 当 0 就是编一个数。
let private dominates (played: DahaiScaffold) (candidate: DahaiScaffold) : bool = …
```

排序（有效牌降序 → 危险度档位升序 → 进退向升序）**只决定先说哪一条**，不决定「算不算更好」；
至多列三条（复盘要的是「原来还有这几张」，把 13 张重排一遍那张表引擎本来就能给——
Assisted 档的 prompt 里就是它）。**没有更好的就明说**：`这一手是当时的最优之一。`

**不加权是有代价的**：有效牌多而危险度更高的那一张**不会**被列出来（它没占优）。
这是有意的——给这两项配权重就是「这一手打得几分」的开头，而那需要一个模型（票 93）。

### 1.4 复盘**对着谁**（这一条是我判的，记在 90-2）

```fsharp
let addressed (model) =
    if not (settled model) then None
    else
        match TableState.humanSeat model with
        | Some seat -> Some seat                            // 真人在座：主语恒是他
        | None -> match TableState.viewpoint model with
                  | Viewpoint.Seated seat -> Some seat      // 没有真人：跟着视角走
                  | Viewpoint.God -> None                   // 上帝视角没有主语
```

三条理由：① spec 的 story 34 说的是「针对**我**每一手的复盘」，而真人终局之后视角是松开的
（票 87 的 `unlocked`）——拿视角当主语的话，他一按上帝视角自己的复盘就没了；
② 票面要「模型席也能看」，坐到座位 2 就看座位 2 的，一条规则两种用法；
③ **上帝视角没有主语**顺带把首页那一屏的代价钉死在零：默认就是上帝视角，一手都不算。

### 1.5 点某一手：**没有第二条时间轴，也没有第二条回程**

点一条标注发的就是票 76 那条 `RecordOpened (Some turn)`，「回到原处」发的是 `RecordOpened None`。
于是：回放里游标跟着跳到那一帧（票 76 的 `openAt`）、一点就暂停、**收起来回到点开之前那一处**
（票 86 的 `Origin` / `returned`）——**这三样一行代码都不必写**。

**「回到原处」那一枚由复盘自己画**：真人那一手在牌谱里没有决策记录（票 87 的裁决），
因此 `TableState.detail` 是 None、票 76 的全文面板不会摊开，那一枚「收起」也就不在。
它只在真跳走了之后才画（`Review.opened` 是 Some）——没跳走时它是一枚点了什么也不会发生的按钮，
而那种按钮会让人以为自己刚才做错了什么。

### 1.6 贵的那一步包在 `React.useMemo` 里

一条标注要现搭一份决策包（`DecisionPackage.forSeat` 一次从头 fold + 逐张试打约 400 次形态判定），
一整场东风战一席 115–122 条。**实测（浏览器，dev server，122 条）：头一次 185 ms，
memo 命中之后再渲染一次 26 ms**（.NET 侧同一份 115 条 188 ms——这一段 Fable 与 .NET 几乎同速）。

依赖只用三个整数（`Review.settled` / 座位号 / `Review.signature`）：`Option`、元组与列表
每次渲染都是新对象，拿它们当依赖等于没有 memo。**摊开的是哪一手不进 memo**——那一格每点一下就该变。

---

## 2. 闸门：谁在守什么

### 2.1 dotnet 侧（`tests/Janpo.Web.Tests/ReviewTests.fs`，**9 条**；243 → 252）

| 用例 | 钉的是 |
|---|---|
| **对局中一条标注都没有**：复盘整块不在这一屏上，终局之后才出现 | 走 120 步之后 `settled = false`、`shown = Hidden`（阳性对照：这一刻真的打了二十几手、这一场没打完）；打完之后主语是他、标注非空 |
| 真人那一席的**每一手**都有一条标注：手序与牌谱里逐个相同 | **锚点是另一条路重走一遍**（`Replay.traceOfPaifu` + `GameState.step`，不走 `Table.replay`）；label 与 mjai 动作名各对一遍；打牌 / 非打牌两类各报执行次数（判据 3） |
| **每一条上那几个数与引擎直接算的逐字相同**（有效牌再由第三个锚点核一遍） | 逐手与 `Scaffold.calculate` 对 `Shanten` / `ShantenDelta` / `Ukeire` / `Danger` 四项；有效牌再由 `Ukeire.calculate`（**绕开脚手架**）算一遍；执行次数：有效牌 > 40、危险度 > 0 |
| **故意每一手都打最差的那张**：更好的候选真的列得出来，头一条就是引擎算出的最优 | 一整场都在明显打错（>20 手列得出候选）；差得最多的那一手逐项摊开：头一条的牌与枚数 = 引擎试打表里「向听不比你差的那几张里有效牌最多的」，且差 ≥ 10 枚；那句话里牌与枚数都在；至多三条 |
| **每一手都打帕累托最优的那张**：那一栏恒是「这一手是当时的最优之一」 | 用例自己按**字典序最大**挑（它不可能被占优，理由写在用例里——这是从定义推的期望，不是拿实现对它自己）；每一手 `Better` 必须空且那句话必须在；非打牌那几条**根本没有这一栏**（不是空串） |
| 真人在座时复盘的主语恒是他：切到上帝视角也不变 | 终局后视角真的松开了（`ViewpointPicked God` 生效），主语仍是他；切到别席也是他 |
| 模型席也能看：回放里坐到某一席就有那一席的逐手复盘，上帝视角没有主语 | 首页默认 `God` → `Unaddressed`；坐到座位 1 → 手序与另一条路走出来的逐个相同 |
| 点某一手：游标跳到那一帧，收起来回到点开之前那一处 | 游标 200 → 那一帧 → 200；`Timeline.Turns - 1` 就是那一手；跳过去之后面板一条不少 |
| 强 AI 那一行今天整行不出现：一条标注只说得出三句话，里面没有「暂无」 | 每条 2–3 句；「暂无」「强 AI」「评分」一个词都不许出现 |

### 2.2 浏览器侧（第十七趟 `verify-review.mjs`，**2.8s**；一个字节都不出网）

**第一程**（真人坐座位 0、三家自带 bot、页面内驱动打完一整场东风战）：
① **对局中复盘那一块整个不在 DOM 里**（`table-review` 与 `table-review-hint` 都不在，
阳性对照：这一刻牌桌上真有落定的一手）；② 终局后主语是座位 0；
③④ 逐行逐项与 `ReviewCheck.expected`（引擎**另一条路**）对拍；
⑤ 点某一手牌桌换成那一刻的快照、「回到原处」回得来（Live 那一侧没有时间轴）。

**第二程**（首页那份 Demo 回放）：⑥ 上帝视角没有主语、只有那一句「坐到某一席就看得了」；
⑦ 坐到座位 1 → 逐项对拍；⑧ **点某一手 → 游标真的跳过去 → 「回到原处」回得来**；
⑨ 强 AI 那一行一个占位都没有、面板里没有「暂无 / 强 AI / 评分」。

**执行次数**（闸门自己印出来，判据 3）：

```
对局中：走了 60 步，上一手：座位 0 手切2索
对局中：复盘那一块整个不在 DOM 里 ✓（阴性对照）
终局：又走了 303 步
真人那一桌：96 条逐项对拍 ✓（有效牌 74 条、危险度 59 条、更好的候选 52 条、最优之一 22 条、差 10 枚以上 16 条）
首页座位 1：122 条逐项对拍 ✓（有效牌 92 条、危险度 65 条、更好的候选 22 条、最优之一 70 条）
点开跳走了、关掉跳回来 ✓（第 234 帧 → 第 203 帧 → 第 234 帧）
```

没有一条断言是执行 0 次的；`counts.better` / `counts.best` / `counts.danger` 为 0 时闸门**自己报红**
（「那一栏的断言这一趟等于没跑」）。

### 2.3 右侧不许是同一个实现（判据 6）

三处刻意错开：

1. **状态从哪来**：页面走 `Table.replay` fold 出来的帧，`ReviewCheck` 走
   `Replay.traceOfPaifu` + `GameState.step` 自己推一遍——两条路各自到达同一手，
   再各自向同一个引擎问脚手架。**帧错一格当场红**（红-3 就是它）。
2. **有效牌再算一遍**：dotnet 那一侧从观测重建 `HandShape`、`HandShape.remove` 打出去那张、
   `Ukeire.calculate`（打出去那张进可见集）——**绕开 `Scaffold`** 的一条独立路径。
3. **「更好」那一栏**：闸门照**规则**在 JS 里另写一遍帕累托判据（判据 8：期望值取自规则，
   不取自被检查那句话的来源），拿引擎给的**逐张试打表**推，再与页面列的那几张对。
   dotnet 那一侧走的是另一条：按字典序挑一张打下去，从**定义**推出「一条都列不出来」。

---

## 3. 「更好的候选」那个明显打错的局面（票面点名）

**构造法**：让真人那一席每一手都打**字典序最小**的那一张（向听最退、有效牌最少、危险度最高），
一整场都在明显打错——这比手捏一副牌硬，因为它在一整场真对局的每一手上都成立。

那一场里差得最多的那一手（用例逐项摊开核过；`ReviewTests.故意每一手都打最差的那张…`）：

- 页面/标注给的头一条候选 = 引擎试打表里「向听不比你打的那张差」的那几张里**有效牌最多的**；
- 它多出来的枚数 = `Ukeire.total(它) − Ukeire.total(你打的)`，**用例要求 ≥ 10 枚**（实测远不止）；
- 那一栏说出来的话里，牌名与枚数都在。

首页那份真牌谱上现成的一条（探针印的原文，数值由闸门与引擎对拍过）：

```
第 285 手　手切5筒
    2 向听 → 3 向听（退向）　有效牌 26 枚 8 种
    更好的候选：打6万（有效牌 62 枚，+36）、打7万（有效牌 58 枚，+32）、打4索（有效牌 52 枚，+26）
```

反过来那一半同样有人守：`每一手都打帕累托最优的那张` 那条用例里，**每一手**的那一栏都必须是
`这一手是当时的最优之一。`——「更好的候选」不许无中生有。

---

## 4. 每条新断言先红一次（判据 1 的原始输出）

**六次，全部实跑**：改**产品代码**（不是改断言），跑同一条命令，抄红的原文，
最后逐文件 `diff` 对回 `/tmp/t90bak/`（五个文件全 OK）。

### 红-1｜**把「只在终局后」那道判断去掉**（票面点名的那一条）

`Review.settled` 改成恒 `true`：

```
（dotnet）
Janpo.Web.Tests.ReviewTests.对局中一条标注都没有：复盘整块不在这一屏上，终局之后才出现 [FAIL]
   Assert.False() Failure
失败!  - 失败: 1，通过: 8，总计: 9

（verify-review）
复盘那一道没过：
对局还没打完，复盘面板就在 DOM 里了（17 条标注）：对局中给出「换打会怎样」就是作弊——那是 Assisted 档的事（票 89），复盘只在终局之后
```

**17 条标注**——那正是作弊的样子：他打到第 60 步，前面每一手的「换打会怎样」都摆在他眼前。

（**头一次改坏没编出来**：`error FS1182: 未使用值“model”`——票 87 记的那一课的第三次复现，
「破坏实验之前先确认那一版真的编出来了」。改成 `_model` 才真红。）

### 红-2｜「更好」的判据放宽（`dominates` 去掉「不更差」那一半，只看「至少一项更好」）

```
（dotnet）
失败 ReviewTests.每一手都打帕累托最优的那张：那一栏恒是「这一手是当时的最优之一」
   第 0 手打的是字典序最优的那张，却列出了 3 个更好的候选

（verify-review，节选）
首页那份牌谱 第 437 手（摸切1万）：引擎的试打表里一张更好的都没有，页面却列了 7s、9s、6s
首页那份牌谱 第 437 手（摸切1万）：没有更好的就该明说，页面上那一栏写的是「更好的候选：打7索（有效牌 34 枚，+20；危险度更低（筋））、…」
首页那份牌谱 第 461 手（手切3筒）：…「更好的候选：打5筒（有效牌 20 枚，+-4；危险度更低（筋））、…」
```

**这一次顺手治好了一处渲染**：末行那个 `+-4`——原来的写法是 `+{gain}`，
而它的「gain 恒 ≥ 0」靠的是另一处（`dominates`）的不变量。**数字的形状不该靠另一处的不变量撑着**，
改成按正负分三支（正数才写 `+`）。

### 红-3｜帧错一格（`noteOf` 拿 `after.State` 而不是 `before.State`）

```
失败 ReviewTests.每一条上那几个数与引擎直接算的逐字相同（有效牌再由第三个锚点核一遍）
   Assert.Equal() Failure: Values differ
失败 ReviewTests.故意每一手都打最差的那张：更好的候选真的列得出来…
   一整场都在明显打错，却只有 0 手列得出更好的候选
失败 ReviewTests.真人那一席的每一手都有一条标注：手序与牌谱里逐个相同
   Assert.All() Failure: 125 out of 146 items in the collection did not pass.
失败!  - 失败: 4，通过: 5，总计: 9
```

（「146 条」是那一版把非打牌那几手也算错之后的条数——**帧错一格连「有几手」都变了**。）

### 红-4｜「没有更好的就明说」那一句去掉（`advice` 在空表时回 None）

```
失败 ReviewTests.每一手都打帕累托最优的那张：那一栏恒是「这一手是当时的最优之一」
   Assert.Equal() Failure: Values differ
   Expected: Some(这一手是当时的最优之一。)
   Actual:   null
```

### 红-5｜复盘的主语不看真人席（`addressed` 只跟着视角走）

```
失败 ReviewTests.每一条上那几个数与引擎直接算的逐字相同… ： 这一刻该有座位 0 的复盘，却是 Unaddressed
失败 ReviewTests.真人在座时复盘的主语恒是他：切到上帝视角也不变
   Expected: Some({ Index = 0 })  Actual: null
失败 ReviewTests.故意每一手都打最差的那张… ： 这一刻该有座位 0 的复盘，却是 Unaddressed
失败 ReviewTests.每一手都打帕累托最优的那张… ： 这一刻该有座位 0 的复盘，却是 Unaddressed
失败 ReviewTests.真人那一席的每一手都有一条标注… ： 这一刻该有座位 0 的复盘，却是 Unaddressed
失败 ReviewTests.强 AI 那一行今天整行不出现… ： 这一刻该有座位 0 的复盘，却是 Unaddressed
失败 ReviewTests.对局中一条标注都没有… ： Expected: Some({ Index = 0 })  Actual: null
失败!  - 失败: 7，通过: 2，总计: 9
```

**这一条正是终局那一刻的真实形状**：真人那一桌打完之后视角自己松开（票 87 的 `unlocked`），
`?table=1` 默认又是上帝视角——只跟着视角走的话，他一打完，自己的复盘当场没了。

### 红-6｜点一条标注不走票 76 那条路（发 `CursorMoved note.Frame` 而不是 `RecordOpened`）

```
复盘那一道没过：
点了第 0 手，牌桌却纹丝不动：「上一手：座位 1 手切8筒」
点开的该是第 0 手，面板说的是「」
页面上没有 [data-testid="review-return"]：跳走了就要回得来（票 86 立的回程规矩）
页面上没有 [data-testid="review-return"]：跳走了就要回得来（票 86 立的回程规矩）
```

**这一条第一次是抛出来的，不是红出来的**（票 86/87/88 各写下过同一课的第四次复现）：
`getByTestId("review-return").click()` 在那一枚不存在时**干等 30 秒再抛 `TimeoutError`**，
而合并跑的入口要的是一份失败清单——抛出去会把十七趟一起搞挂。补了一个 `clicked()`
（先数个数、没有就记一条、返回 false）之后，同一次破坏才交出上面那份清单。

---

## 5. 截图：我亲眼看到了什么（判据 7）

`/tmp/t90shots/90-review.png`（1280×1600，一次性 playwright 探针，**没进仓库，跑完删了**），
**打开看过两次**（第一次看出了下面那处，改完再看一次）。

首页 → 拖到 70% → 点「座位 1」视角之后，牌桌下面是：

> **复盘：座位 1 的逐手对照（122 手）**
> 这几行是引擎按你当时看得见的牌现算的（向听、有效牌、危险度），不是打分——「更好的候选」
> 只列在这几个数上不比你差、至少一项更好的那几张。点某一手：牌桌摆出那一刻的快照
> （回放里时间轴跟着跳过去），按「回到原处」就回来。
>
> 　第 1 手　摸切西
> 　3 向听 → 3 向听　有效牌 30 枚 9 种
> 　这一手是当时的最优之一。
> 　第 6 手　手切1万
> 　3 向听 → 3 向听　有效牌 27 枚 8 种
> 　更好的候选：打1索（有效牌 29 枚，+2）、打白（有效牌 29 枚，+2）、打发（有效牌 29 枚，+2）
> 　…
> 　第 65 手　碰3万（亮3万 3万）
> 　1 向听（这一手没打牌，没有「换打会怎样」可比）

**看图看出来的一处**：第一版用 `<ol>` 的默认序号，于是每一行头上有**两套编号**
（列表序号 `14.` 与手序 `第 65 手`）——同一件事的第二个编号，票 86 记过同一族的一条
（「同一帧两个数」）。治法是 `styles.css` 里四条规则：`list-style: none`、
一条淡墨左边线、摊开那一条的左边线换靛青加淡靛底、抬头那一枚按钮**长得像一行字**
（一屏上百条，百来个方框会盖过牌谱本身；悬停与 `:focus-visible` 时它才显出可点，
键盘走得到这一条因此没丢）。

---

## 6. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，57.2s；引擎 750 + 页面 **252** 条；浏览器**十七趟**全 ✓ |
| 新那一趟单跑 | `cd web && node scripts/verify-review.mjs` | 2.8s，见 §2.2 |
| 每条新断言先红 | §4 的六次 | 全部红过，原文抄在 §4 |
| 标注的规模与代价（引擎侧） | `dotnet fsi /tmp/probe90.fsx` / `probe90b.fsx`（**没进仓库，跑完删了**） | 首页那份牌谱 470 帧 / 464 手；一席 115 条标注 **188 ms**（.NET） |
| 标注的代价（浏览器） | 一次性 playwright 探针（**没进仓库，跑完删了**） | 122 条**头一次 185 ms**、memo 命中之后再渲染 **26 ms** |
| 截图 | 同一个探针 | §5，打开看过 |
| 还原干净 | 逐文件 `diff` 对回 `/tmp/t90bak/` | `Review` / `ReviewPanel` / `ReviewCheck` / `TableState` / `TablePage` 五样全 OK |

`jj diff --stat`：13 个文件，+1923 / −18。**新增三个源文件**（`Review.fs` / `ReviewPanel.fs` /
`ReviewCheck.fs`）、一个用例文件（`ReviewTests.fs`）与一道闸门（`web/scripts/verify-review.mjs`）。

**没碰**：`src/Janpo.Engine/**`（引擎一字未动）、`web/src/agent/**`（票 94 的地盘）、
`SeatingPlan.fs` / `Store.fs` / `TablePanel.fs` / `Roster.fs`（**票 92 的座位那条线**）、
`web/public/**`、`probe/**`、`tests/Janpo.Engine.Tests/**`（票 98）、`docs/adr/*`、`CONTEXT.md`、
`Paifu` / `DecisionRecord` / `LiveTable` / `SeatChoice` 的形状、`reveals` / `unlocked` / `lockedSeat`
的规则、`Bubble` 的三态与 `openAt` / `returned` 的实现。
**没有新增任何对 `render_version` 值的断言**。

**动了但值得点名的四处**：

1. `TableState.fs` **只改了一个词**：`let private liveFrames` → `let internal liveFrames`
   （复盘要的是同一份帧；再写一份「导成牌谱再 fold」就是第二条算路）。**形状一个字节没动。**
2. `TablePage.fs` **只加了一行挂载**（`@ ReviewPanel.at model dispatch`，摆在牌桌下面）。
3. `web/scripts/verify-browser.mjs` / `scripts/ci-web.sh` / `web/package.json`：新增一趟绕不开的
   三处登记（一个 import + 表尾一条 + 一行 `verify:review` + 那段说明）。
   **顺带把「十六趟」改成了「十七趟」**（`ci-web.sh` 与 `verify-browser.mjs` 里各几处）——
   票 92 也要加它自己那一行，这几处的数字会撞，集成时以「趟数 = 表里的条数」为准。
4. `web/src/styles.css`：**末尾追加**一段（§5 那四条规则），没动任何既有规则。

---

## 7. code-review（Standards + Spec 两轴，fixed point `pslzyykr` / `2713529c`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓**：全程 `jj st` / `jj diff` / `jj log` / `jj commit`，无远端操作、无交互式 flag。
- **工具强制的**：`fantomas --check` / `check-style.sh` / Biome / tsc 全绿；**`let mutable` 一处未新增**
  （第一版的用例拿两个可变计数器数「这几条断言开口了几次」，自查时改成
  `List.choose` 收一张表、`List.sumBy` 数一遍——同票 87 那一次）。
- **F# 风格**（`docs/agents/fsharp-style.md`）：
  - 规则 1/3：新代码是从左往右的数据流（`frames |> List.pairwise |> List.indexed |> List.choose …`、
    `after.Latest |> Option.map … |> Option.filter … |> Option.map …`、
    `scaffold.Dahai |> List.filter … |> List.sortBy … |> List.truncate … |> List.map …`）。
  - 规则 2：`optional (Ukeire.total >> Encode.int)` 是正例；`fun (each: Danger) -> …` 那几个
    lambda 带类型标注（Thoth 的 `IEncodable` 推不出来），属明文例外。
  - 规则 4.1 的「谓词套取值器」保留（`Option.isSome (…)`）。
  - 限制 B：`dominates` 里那两条 `&&` / `||` 链**没有抽 `let`**——短路掉的那一半不该算。
- **注释写「为什么」✓**：为什么不做「有效牌 33 → 21」那个箭头、为什么「更好」是帕累托而不是总分、
  为什么主语恒是真人席、为什么 memo 的依赖只用整数、为什么强 AI 那一行连占位都不留、
  为什么闸门要 `clicked()` 而不是直接 `.click()`——都写在代码上。
- **术语 ✓**：`Review` / `ReviewNote` / `ReviewCandidate` / `ReviewShown` 是**渲染层的名字**
  （同 `Bubble` / `Origin` / `ActionButton`），日麻术语一个没自造；数值一律用 `CONTEXT.md` 的
  `Shanten` / `Ukeire` / `ShantenDelta` / `Danger`。**`CONTEXT.md` 一字未改**（硬约束 5）——
  提案见 §8 第 1 条与 `DECISIONS.md`。
- **ADR-0002 ✓**：复盘读的是牌谱（逐帧就是对事件前缀 fold），**没有第二条时间轴**，
  也没有新的可分享产物。
- **ADR-0003 ✓**：`unlocked` / `lockedSeat` / `reveals` 一字未动；复盘的判据（终局 + 主语）
  仍旧挂在**对局配置与终局状态**上。
- **ADR-0005 ✓**：TS 一行没碰；`ReviewCheck` 与 `Golden` / `PaifuCheck` 同形，跨界只传字符串，
  **而且抛不出去**（自查时补的：那一句 `failwith` 包在 `try … with` 里，真走到了交一句中文原因
  ——`page.evaluate` 里抛出来的异常同样会把十七趟一起搞挂，与 §4 红-6 同一族）。
- **blocking：0。**

### Spec（票面 6 条行为 + 4 条闸门 + 4 条边界）

逐条对照见票文件的勾选框。四处值得写下来：

- **「有效牌怎么变」落成了「打完之后是多少 + 更好的那张多几枚」**，不是票面示意里那个
  「33 → 21」的箭头：14 张手牌的有效牌引擎不定义（§1.2）。这是这一票里唯一一处**没有照抄票面示意**
  的地方，理由与被否决的两个做法记在 `DECISIONS.md` 90-1。
- **「每一手」按牌谱的口径算**：`Action.None`（他自己按的那一次「过」）同样占一手，
  因为它在牌谱里就是一手（`Table.Turns` 数得到它）。那几条只报向听，不报「换打会怎样」
  ——手牌根本没变。票 88 §2 说的「复盘第一件要问的事」（这一次「过」是不是他自己按的）
  仍旧由那本账（`data-human-passes`）回答，这一票没有把它复制一份。
- **模型席那一半是白得的**：`notesFor` 只按座位取，从不问那一席是谁在打。
  于是票面「顺手记着」那件事成立了——模型席的 `reason` 与真人席的这几条标注排在同一根轴上。
- **对局中一律不给**做在了 `Review.settled` 一处（三态在 `ReviewShown` 里穷举），
  而不是在视图里再判一遍。

### 记录但没改的 nitpick

1. **首页那一屏坐到某一席之后，回放每走一帧都要重画 122 行**（memo 命中，26 ms/帧）。
   眼下没有问题（首页默认上帝视角，一手都不算），但哪天有人想「边播边看复盘」，
   该做的是把行虚拟化，而不是把标注存起来。
2. **一整场一百多条排成一列，没有折叠也没有筛选**（例如「只看列得出更好候选的那几手」）。
   一屏读下来要滚很久。加筛选就要一个状态，而这一票不往 `TableModel` 上加格子——
   留给主人裁要不要做（§8 第 3 条）。
3. **`verify-setup.mjs` 的文件头仍写着「十一趟共用一个浏览器」**（现在十七趟）。
   票 72 的文件，票 76 / 81 / 87 的报告各记过一次，这一票同样没碰。
4. **`Review.notesFor` 对同一份帧算两遍**（页面一遍、闸门那一侧的锚点另算一遍）——那是有意的
   （判据 6：右侧不许是同一个实现），但它让这一趟闸门里两次 `DecisionPackage.forSeat`
   全场扫描，2.8s 里大头就是它。

---

## 8. 留给人的待审项

1. **`CONTEXT.md` 里今天没有「复盘」这个词条。** 这一票落地之后它至少有三条不变量有了执行者，
   **但改术语表要单票授权，因此我一个字没写**，提案记在 `DECISIONS.md` 90-3：
   ①「复盘只在终局之后出现」（执行者 `Review.settled` + `ReviewTests` 那条 + `verify-review` 第①条）；
   ②「复盘上的每一个数都是引擎已经会算的量，没有总分」（执行者 `Review.notesFor` 只读
   `Scaffold.calculate`，加 `verify-review` 的逐项对拍）；
   ③「复盘对着一席，上帝视角没有主语」（执行者 `Review.addressed`）。请主人裁要不要收词。
2. **「更好」的判据是帕累托占优**（§1.3）：有效牌多但危险度更高的那一张**不会**被列出来。
   这是我判的（票面只说「按有效牌或危险度排」），因为加权就是「这一手打得几分」的开头。
   若主人认为该按「有效牌优先、危险度只当附注」列，改的是 `Review.dominates` 一处 + 两条用例，
   **这一票没有做**。
3. **一整场一百多条排成一列**（nitpick 2）。要不要「只看有更好候选的那几手」这样的筛选，
   是产品口味；它要在 `TableModel` 上加一格，而这一票的硬边界是不动共享类型的形状——
   因此**这一票没有做**，请主人裁。
4. **票 93 接上去要改三处**（写在代码注释里）：`ReviewNote` 上加一格（例如 `Strong: string option`）、
   `ReviewPanel.noteRow` 的 `advice` 下面多一行（带 `data-review-strong`）、
   `verify-review.mjs` 第⑨条从「一个都没有」翻成「该有的那几手都有」。
   **今天连占位都没有**——「暂无」那种占位比不显示更糟（它看着像坏了）。
