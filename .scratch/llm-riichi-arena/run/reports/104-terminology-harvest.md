# 票 104：术语收词 —— 把 M3 长出来的 10 条不变量收进 `CONTEXT.md`

**结论先说五句。**

1. **10 条里 9.5 条有真执行者，逐条弄坏都当场红过一次**（红的原文照抄在 §2：
   **15 次破坏实验、46 条红**）。这一票的动作因此不是抄写，是把每一条不变量按到闸门前面看它红不红。
2. **半条没人红，它没有进术语表**：`复盘上的每一个数都是引擎已经会算的量，**没有总分**`（90-3 ②）
   的后半句——我在**每一条复盘标注的第二行上凭空印了一个「总分 82」**，
   `./scripts/ci.sh` **整套全绿**（288 条 Web 用例 + 759 条引擎用例 + 浏览器那十七趟 + 强 AI 那一趟）。
   今天守着这件事的只有**一个词**：把「总分」换成「评分」就当场红。
   **前半句（那几个具名的数逐项等于引擎的那一份）有硬执行者**，收进了词条；
   后半句改写成「复盘上一个自造的合成数都不出」，作为**提案待补闸门**留在 `DECISIONS.md`（104-2）。
3. **破坏实验全部复原**：收工 `jj st` 干净，diff 只有 `CONTEXT.md`（+ 票文件 + 本报告 + `DECISIONS.md`）。
   `./scripts/ci.sh` 全绿（`/tmp/104/ci-final.log`，末行 `== CI 全绿 ==`）。
4. **顺手核既有词条：找到 1 处直接与 ADR 矛盾的（已改并点名）+ 4 处过时或不完整的（只列不改）**，见 §4。
5. **认账一次假绿（自查抓住）**：破坏 `TableState.assists` 之后直接跑 `verify-assist`，**EXIT=0**
   ——那一趟跑的是上一版 JS。判据是**破坏 F# 之后必须先 `pnpm run fable` 再跑浏览器闸门**
   （票 87 红-9 同一课，这次踩在别的地方）。重编之后同一次破坏红出 5 条。

---

## 0. 改了什么

| 文件 | 改动 |
| --- | --- |
| `CONTEXT.md` | `Human Seat` 补 3 条不变量；`ScaffoldTier` 补 2 条；新词条 **`Review`（复盘）**（3 条）与 **`强 AI 基线的对照标注`**（2 条）；`Player` 词条里「强 AI 客户端」改成通名「强 AI 基线」（§4 的 A） |
| `.scratch/…/issues/104-terminology-harvest.md` | 勾框 + `Status: ready-for-human` |
| `.scratch/…/run/reports/104-terminology-harvest.md` | 本文 |
| `.scratch/…/run/DECISIONS.md` | 票 104 那一段 |

**代码与测试零改动**（票面边界）：12 次破坏实验各改一处、跑完当场 `jj restore`。
`Review*.fs`（票 103 在里面）、`web/src/baseline/**`、`.github/**`、`src/Janpo.Engine/Shanten.fs`
在**提交里**一个字节都没动。

---

## 1. 一张表：10 条各自由谁守着

| # | 不变量（措辞来自 `DECISIONS.md`） | 执行者 | 弄坏它时哪条闸门红 |
| --- | --- | --- | --- |
| 1 | 一桌只坐得下一席真人 | `SeatingPlan.soloHuman`（`fit`）+ `bind` 里的 `vacated` | `HumanSeatTests.真人只坐得下一席…`（两半各红一次；破坏 `bind` 时 `BaselineSeatTests.四席怎么混都行…` 一起红） |
| 2 | 牌谱里那一列恒是 `human`，里面没有私人信息 | `Roster.humanName` | `HumanSeatTests.真人是第四种选手…` + `BaselineSeatTests.四席怎么混都行…` |
| 3 | 有真人在座且未终局时，页面锁在他那一席上，上帝视角与别席视角连值都给不出来 | `TableState.viewpoint` | `HumanSeatTests.对局中视角锁死自家…`；浏览器侧 `verify-human` 当场漏 25 张他家的牌 |
| 4 | Bare 档的真人在座时，危险度那一块与那一枚开关都不在页面上 | `TableState.assists` | `HumanAssistTests` 3 条 + `verify-assist` 5 条（含「那一枚开关还在 DOM 里」） |
| 5 | ToolSearch 在真人这一侧按 Assisted 处理 | `HumanScaffold.shows` | `HumanAssistTests.ToolSearch 按信息辅助处理…` + `verify-assist` 2 条 |
| 6 | 复盘只在终局之后出现 | `Review.settled` | `ReviewTests.对局中一条标注都没有…` + `verify-review` ① |
| 7a | 复盘上那几个数是引擎已经会算的量 | `Review.notesFor` 只读 `DecisionPackage.scaffold` | `ReviewTests.每一条上那几个数与引擎直接算的逐字相同…` + `每一手都打帕累托最优的那张…` |
| **7b** | **……「没有总分」** | **无** | **全套 `ci.sh` 全绿**（详见 §2 红-7b） |
| 8 | 复盘对着一席，上帝视角没有主语 | `Review.addressed` | `ReviewTests.模型席也能看…上帝视角没有主语` + `verify-review` ⑥；破坏「真人在座恒是他」那一半时 9 条一起红 |
| 9 | 对照标注喂给强 AI 的必须是那一手当时喂给该席的那一份投影 | `ReviewNote.Package` | `ReviewTests.问强 AI 时交出去的就是那一手当时喂给该席的那一份投影…`（逐字节对拍） |
| 10 | 强 AI 只给一个动作，不给理由、不给分数 | `ReviewStrong` 的字段表 + 那条逐词用例 | `ReviewTests.它交回来的那个 id → 那一行…` |

**顺带核了第 11 条**（90-3 的**头一句**，不在那 10 条里，但它是「更好的候选」这个词的定义，
所以写进词条前也按了一次）：**「更好」= 帕累托占优，不是加权总分**（`Review.dominates`）——
换成加权总分之后 `ReviewTests.每一手都打帕累托最优的那张…` 当场红（§2 红-11）。

**关于第 9 条的一个事实，读的人要知道**：93-3 给它列了三个执行者
（`ReviewNote.Package`、`verify-review` ⑪⑫、`ReviewTests` 那条逐字节对拍），
但**CI 里跑得到的只有最后一个**——那份 6 MB 的产物不入版本控制（ADR-0006 边界 6），
`verify-review` 第三程在常规趟里走的是「它用不了」那一路，⑪⑫ 一次都执行不到（判据 3）。
词条里因此只署了 `ReviewNote.Package`。

---

## 2. 逐条「弄坏 → 红 → 复原」（红的原文照抄）

跑法：dotnet 侧 `dotnet build janpo.slnx -c Release && dotnet test tests/Janpo.Web.Tests/…`
（基线 288/288 绿，7 s）；浏览器侧先 `cd web && pnpm run fable` 再跑那一趟。
每条实验的完整日志在 `/tmp/104/break-*.log` 与 `/tmp/104/verify-*.log`（当次机器上）。

### 红-1a｜`SeatingPlan.soloHuman` 改成恒等（`fit` 那一半）

```
失败 Janpo.Web.Tests.HumanSeatTests.真人只坐得下一席：坐上第二席时，原来那一席退回均匀随机 [4 ms]
  错误消息:
   Assert.Equal() Failure: Collections differ
Expected: [{ Index = 0 }]
Actual:   [{ Index = 0 }, { Index = 1 }, { Index = 2 }, { Index = 3 }]
  堆栈跟踪: … HumanSeatTests.fs:line 165
失败! - 失败: 1，通过: 287，总计: 288
```

### 红-1b｜`bind` 里那次 `List.map vacated` 拿掉（「刚拨的那一席赢」那一半）

```
失败 Janpo.Web.Tests.BaselineSeatTests.四席怎么混都行：真人 + 强 AI + 两个 bot 同桌，四种选手各在各的位置上 [32 ms]
   Assert.Equal() Failure: Collections differ
Expected: [{ Index = 3 }]
Actual:   [{ Index = 0 }, { Index = 3 }]
失败 Janpo.Web.Tests.HumanSeatTests.真人只坐得下一席：坐上第二席时，原来那一席退回均匀随机 [< 1 ms]
   Assert.Equal() Failure: Collections differ
Expected: [{ Index = 3 }]
Actual:   [{ Index = 0 }, { Index = 3 }]
失败! - 失败: 2，通过: 286，总计: 288
```

**两半各有各的执行者**：`fit` 那一半（从 localStorage 读回来的留头一席）与 `bind` 那一半
（人拨那一下，刚拨的赢）弄坏任何一半都红，红的不是同一条断言。

### 红-2｜`Roster.humanName` 从 `"human"` 改成 `"xerxes2@example.com/我"`（一个带私人信息、还带斜杠的名字）

```
失败 Janpo.Web.Tests.BaselineSeatTests.四席怎么混都行：真人 + 强 AI + 两个 bot 同桌，四种选手各在各的位置上 [21 ms]
   Assert.Equal() Failure: Collections differ
Expected: ["human", "baseline", "baseline", "opinionated"]
Actual:   ["xerxes2@example.com/我", "baseline", "baseline", "opinionated"]
失败 Janpo.Web.Tests.HumanSeatTests.真人是第四种选手：配桌里是 Human，牌谱里那一列写 human，与 bot 和模型都分得开 [< 1 ms]
   Assert.Equal() Failure: Collections differ
Expected: ["human", "random", "random", "random"]
Actual:   ["xerxes2@example.com/我", "random", "random", "random"]
失败! - 失败: 2，通过: 286，总计: 288
```

### 红-3｜`TableState.viewpoint` 改成直接返回 `model.Viewpoint`（把那道值锁拆掉）

dotnet 侧：

```
失败 Janpo.Web.Tests.HumanSeatTests.对局中视角锁死自家：上帝视角与别席视角连值都给不出来，终局后松开 [2 ms]
  错误消息:
   Assert.Equal() Failure: Values differ
Expected: Seated { Index = 2 }
Actual:   God
  堆栈跟踪: … HumanSeatTests.fs:line 379
失败! - 失败: 1，通过: 287，总计: 288
```

浏览器侧（`pnpm run fable` 之后 `node scripts/verify-human.mjs`，EXIT=1，**8 条**；下面照抄 5 条，
座位 2 / 座位 3 那两组与座位 1 同形）：

```
真人坐席这一道没过：
对局中整页 HTML 里多出 25 个他不该看得见的 data-pai：1m、1p、1z、2m、2s、2z、3z、3z、4m、5mr、5s、
5sr、5z、6m、6s、6z、7m、7p、7s、7z、8s、8s、9m、9m、9s——他家的手牌一张都不许在里面，
连 data-* 都不许有（spec 的 story 29）
座位 1 的手牌在页面上露了 4 张（data-hand-hidden=false）：他家的暗牌在投影里根本不该存在（`MaskedSeat` 没有手牌字段）
座位 2 的手牌在页面上露了 7 张（data-hand-hidden=false）：…
座位 3 的手牌在页面上露了 14 张（data-hand-hidden=false）：…
坐在座位 0 上，Agent 那一行却写着座位 1 的理由：「座位 1 的模型选完了（3 ms）：…」——气泡拦住了而状态线漏了，
那闸门就只是个摆设（票 81）
```

**判据 20 的一个正面例子**：`87-3` 说视角是**两道独立的锁**，这次量到了——
只拆值锁（`TableState.viewpoint`）时，「按钮不在 DOM 里」那一道**纹丝不动**
（同一份日志里 `视角那一排：上帝 0 枚、自家 1 枚…锁在座位 0` 照常打印，
`?dev=1` 那一段也照常 `seed-input 0 个`）。**只按掉一道就想量另一道，量的是一张空页面。**

### 红-4｜`TableState.assists` 改成恒真

dotnet 侧（3 条）：

```
失败 Janpo.Web.Tests.HumanAssistTests.Bare 一整局都不给，而同一局面拨到信息辅助就有：不是只有开局那一手 [65 ms]
   Assert.Equal() Failure: Values differ
Expected: null
Actual:   Some({ Shanten = Shanten 2 …
失败 Janpo.Web.Tests.HumanAssistTests.危险度那一块在真人这一侧与辅助同进同出：立直之后也不许单独漏出来 [35 ms]
   Assert.False() Failure
Expected: False
Actual:   True
失败 Janpo.Web.Tests.HumanAssistTests.Bare 什么都不给：向听 / 有效牌 / 危险度在页面这一侧连值都取不出来 [12 ms]
   Assert.Equal() Failure: Values differ
Expected: null
Actual:   Some({ Shanten = Shanten 2 …
失败! - 失败: 3，通过: 285，总计: 288
```

浏览器侧（**重编 Fable 之后**，`node scripts/verify-assist.mjs`，EXIT=1，5 条）：

```
真人的信息辅助与思考时限这一道没过：
裸奔档的真人坐在桌边，「危险度」那一枚开关却还在 DOM 里：灰掉不算数——危险度是「要算才有的量」
（术语表那条「感知 vs 计算」），拨得出来「裸奔」这个对照组就没了
裸奔档下辅助那一块出现了 18 次（他出手 17 次，另加停下来看的那一刻）
裸奔档下页面上摆出了 120 行算好的数
裸奔档下整页 HTML 里还有算好的数：data-scaffold-lines、data-scaffold-shanten、data-scaffold-id、
data-scaffold-delta、data-scaffold-ukeire、data-scaffold-kinds、data-scaffold-danger
——「一个坐在牌桌前的人免费得到的一切」里没有它们（CONTEXT.md 的 ScaffoldTier）
裸奔档下整页文字里出现了「向听」「有效牌」「进退向」「危险度」：那几个词只可能从「要算才有的那几个量」那一侧来
```

**红 5 条正是 `89-6` 补上 `peekHuman` 之后应有的数**（那之前只红 3 条，整页那两条在空转）——
判据 20 的量点今天仍旧停在「轮到他、而他还没出手」那一刻。

**假绿认账**：这一次我先跑了一趟 `verify-assist` 就报 EXIT=0
（`/tmp/104/verify-assist-04.log`），因为**改了 F# 却没重跑 `pnpm run fable`**，
那一趟量的是上一版 JS。重编之后才是上面这 5 条。

### 红-5｜`HumanScaffold.shows` 把 `ToolSearch` 从 `true` 改成 `false`

dotnet 侧：

```
失败 Janpo.Web.Tests.HumanAssistTests.ToolSearch 按信息辅助处理：那几行与 Assisted 逐条相同，页面也说得出这件事 [2 ms]
   Assert.True() Failure
Expected: True
Actual:   False
  堆栈跟踪: … HumanAssistTests.fs:line 318
失败! - 失败: 1，通过: 287，总计: 288
```

浏览器侧（EXIT=1）：

```
真人的信息辅助与思考时限这一道没过：
工具搜索档下真人这一侧一行辅助都没有：票面原话是「选到它时按 Assisted 处理并说明」
（这一票不给真人做查询面板，但不是把他降回裸奔）
同一个局面下，工具搜索档给真人的那几行与信息辅助档不同：信息辅助「手切4筒：打完 2 向听，有效牌 24 枚：
9筒(1) 1索(4) 2索(2) 3索(2) 4索(4) 6索(3) 7索(3) 8索(1) 9索(3) 东(1) 南(0)（进退向 0）　
危险度第 3 位（4p 筋（下家现物、上家 1p 7p 筋））」／工具搜索「undefined」
```

### 红-6｜`Review.settled` 改成恒真

dotnet 侧：

```
失败 Janpo.Web.Tests.ReviewTests.对局中一条标注都没有：复盘整块不在这一屏上，终局之后才出现 [82 ms]
   Assert.False() Failure
Expected: False
Actual:   True
  堆栈跟踪: … ReviewTests.fs:line 216
失败! - 失败: 1，通过: 287，总计: 288
```

浏览器侧（EXIT=1）：

```
复盘那一道没过：
对局还没打完，复盘面板就在 DOM 里了（17 条标注）：对局中给出「换打会怎样」就是作弊
——那是 Assisted 档的事（票 89），复盘只在终局之后
```

量点没问题：那一趟先走 60 步再抓（`对局中：走了 60 步，上一手：座位 0 手切2索`），
dotnet 那一侧是 `drive bestPick 120` 之后停下来量（判据 20）。

### 红-7a｜`Review.notesFor`（`noteOf`）里那一条试打的 `ShantenDelta` **+1**（这一层自己造一个数）

```
失败 Janpo.Web.Tests.ReviewTests.每一条上那几个数与引擎直接算的逐字相同（有效牌再由第三个锚点核一遍） [2 s]
   Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
失败 Janpo.Web.Tests.ReviewTests.每一手都打帕累托最优的那张：那一栏恒是「这一手是当时的最优之一」 [103 ms]
失败! - 失败: 2，通过: 286，总计: 288
```

### 红-7b｜**这一条没人红**：在每一条复盘标注的第二行末尾印一个凭空造的「总分」

改的是 `ReviewNote.figures`：

```fsharp
let score =
    let seed = (Shanten.value trial.Shanten) * 17 + trial.ShantenDelta * 31
    Some $"总分 {50 + (abs seed % 50)}"

[ Some shanten; ukeire; danger; score ] |> List.choose id |> String.concat "　"
```

页面上每一手因此长成 `2 向听 → 3 向听（退向）　有效牌 20 枚 7 种　总分 82`。跑：

- `dotnet test tests/Janpo.Web.Tests` → **已通过! - 失败: 0，通过: 288，总计: 288**
- `node scripts/verify-review.mjs`（重编 Fable 之后）→ **EXIT=0**，
  且它照常打印 `真人那一桌：96 条逐项对拍 ✓` / `首页座位 1：122 条逐项对拍 ✓`
- **`./scripts/ci.sh` → 末行 `== CI 全绿 ==`**（`/tmp/104/ci-break-07b.log`，全套 920 行日志零红）

**为什么逐项对拍抓不到它**：那道对拍是**逐字段**比（向听 / 进退向 / 有效牌 / 危险度各一条
`Assert.Equal`），它证明「这几个数没被改」，不证明「没有第 五 个数被凭空加上去」。
**「每一个数」比闸门实际守的「这几个数」强一档，而强出来的那一档没有执行者。**

**换一个词就红**（同一处改动，`总分` → `评分`）：

```
失败 Janpo.Web.Tests.ReviewTests.没问之前强 AI 那一行整行不出现：一条标注只说得出三句话，里面没有「暂无」 [402 ms]
   Assert.DoesNotContain() Failure: Sub-string found
                                      ↓ (pos 29)
String: "2 向听 → 3 向听（退向）　有效牌 20 枚 7 种　评分 82"
Found:  "评分"
  堆栈跟踪: … ReviewTests.fs:line 484
```

**所以今天守着「不造总分」的是一张词的黑名单**：`ReviewTests` 那条查 `暂无 / 强 AI / 评分`，
`verify-review` 那条查 `暂无 / 评分`，`ReviewStrong` 那条逐词用例查
`因为 / 理由 / 评分 / 总分 / 暂无 / 错`。**任何不含这几个词的合成数都进得来。**
处置：词条里只写有执行者的那一半，「一个自造的合成数都不出」进 `DECISIONS.md` 的 104-2 等补闸门。

### 红-8a｜`Review.addressed` 的 `Viewpoint.God` 分支改成「给座位 0」

dotnet 侧：

```
失败 Janpo.Web.Tests.ReviewTests.模型席也能看：回放里坐到某一席就有那一席的逐手复盘，上帝视角没有主语 [22 ms]
   Assert.Equal() Failure: Values differ
Expected: null
Actual:   Some({ Index = 0 })
  堆栈跟踪: … ReviewTests.fs:line 407
失败! - 失败: 1，通过: 287，总计: 288
```

浏览器侧（EXIT=1）：

```
复盘那一道没过：
首页默认是上帝视角，复盘却已经有主语了：复盘的第一个字是「你」
上帝视角下页面一句话都没说：人因此不知道坐下来就看得到复盘
```

### 红-8b｜同一个函数的另一半：`TableState.humanSeat` 那一支拿掉（真人在座不再恒是他）

```
失败: 9，通过: 279，总计: 288
  失败 ReviewTests.真人在座时复盘的主语恒是他：切到上帝视角也不变
     Expected: Some({ Index = 0 })   Actual: null
  另外 8 条：每一条上那几个数与引擎直接算的逐字相同…／它交回来的那个 id → 那一行…／
  没问之前强 AI 那一行整行不出现…／问强 AI 时交出去的就是那一手当时喂给该席的那一份投影…／
  故意每一手都打最差的那张…／每一手都打帕累托最优的那张…／真人那一席的每一手都有一条标注…
```

### 红-9｜`ReviewNote.Package` 喂**后一帧**的投影（`after.State`，即「它知道你后来摸到了什么」）

```
失败 Janpo.Web.Tests.ReviewTests.问强 AI 时交出去的就是那一手当时喂给该席的那一份投影（上帝视角那一份真的不同） [252 ms]
  错误消息:
   Assert.Equal() Failure: Strings differ
                                  ↓ (pos 2126)
Expected: ···"s","tsumogiri":true}],"observation":{"sea"···
Actual:   ···"s","tsumogiri":true},{"type":"tsumo","act"···
                                  ↑ (pos 2126)
  堆栈跟踪: … ReviewTests.fs:line 516（leaked@508）
另一条：每一条上那几个数与引擎直接算的逐字相同…（Expected: Some(Shanten 1) / Actual: Some(Shanten 0)）
失败! - 失败: 2，通过: 286，总计: 288
```

红的位置正好是**多出来的那条 `tsumo` 事件**——喂出去的那份历史里，多了他那一手当时还看不见的一次摸牌。
这是逐字节对拍，不是逐字段：它抓得住「多了一件事」。

### 红-10｜`ReviewStrong.toDisplay` 替它编一句理由 + 一个总分

```
失败 Janpo.Web.Tests.ReviewTests.它交回来的那个 id → 那一行：交不出来、认不出来的那几手整行不出现 [253 ms]
   Assert.DoesNotContain() Failure: Sub-string found
                         ↓ (pos 16)
String: "〔强 AI〕手切6万（与你不同；理由：它的算法算下来这张最稳；总分 60）"
Found:  "理由"
  堆栈跟踪: … ReviewTests.fs:line 613
失败! - 失败: 1，通过: 287，总计: 288
```

### 红-11｜（第 11 条，写进词条前顺手按的）`Review.dominates` 从帕累托占优换成加权总分

```
失败 Janpo.Web.Tests.ReviewTests.每一手都打帕累托最优的那张：那一栏恒是「这一手是当时的最优之一」 [94 ms]
  错误消息:
   第 0 手打的是字典序最优的那张，却列出了 3 个更好的候选
  堆栈跟踪: … ReviewTests.fs:line 377
失败! - 失败: 1，通过: 287，总计: 288
```

---

## 3. 收进 `CONTEXT.md` 的到底是哪几句

**`Human Seat`（补 3 条）**：一桌只坐得下一席（`SeatingPlan.soloHuman` + `bind`）／牌谱那一列恒是
`human` 且没有私人信息（`Roster.humanName`）／有真人在座且未终局时页面锁在他那一席上、
上帝视角与别席视角连值都给不出来（`TableState.viewpoint`，**两道锁**）。`_Avoid_` 行点名：
别把「锁在他那一席」读成权限级别（ADR-0003）。

**`ScaffoldTier`（补 2 条）**：Bare 档的真人在座时危险度那一块与那一枚开关都不在页面上
（`TableState.assists`，**灰掉不算数**）／ToolSearch 在真人这一侧按 Assisted 处理
（`HumanScaffold.shows`，**页面不静默降级**）。`_Avoid_` 补一句：别在真人那一侧另立第二条判据。

**新词条 `Review`（复盘）**：定义 + 「更好」=帕累托占优（`Review.dominates`）+ 3 条不变量
（`settled` / `notesFor` / `addressed`），并**在词条里写明**「一个自造的合成数都不出」今天没有执行者、
只是提案。`_Avoid_`：别与 `Replay` 混、别叫「打分 / 评价 / 棋力分析」、别为它给 `Paifu` 加字段。

**新词条 `强 AI 基线的对照标注`**：参照系不是裁判 + 2 条不变量（`ReviewNote.Package` /
`ReviewStrong` 的字段表与逐词用例）+ 「同不同按同一包里的 id 比」。`_Avoid_` 第一句就是**通名纪律**：
术语里不写具名，具名只在署名与说明处（ADR-0006 边界 5 的修订 + `src/Janpo.Web/Credit.fs`）。

**通名纪律自查**：新写与改写的段落里出现「强 AI 基线」4 次、具名 0 次（全文 5 次，多的那一次是抬头那一句）；
`Akagi` / `Shinkuan` / `native_bot` 这三个词在 `CONTEXT.md` 全文仍是 **0** 次。

---

## 4. 既有词条的过时清单（**A 已改并点名，B–E 只列不改**）

**A（已改，错字级的明显矛盾）｜`Player`（选手）里写着「强 AI 客户端」。**
ADR-0006 边界 5 明文：「写 spec / `CONTEXT.md` 时写**强 AI 基线**」（主人第九次术语授权），
而同一份 `CONTEXT.md` 的抬头第一句已经写着「强 AI 基线」——同一份文件里两个名字指同一件事，
其中一个直接违反标着的 ADR。已改成「强 AI 基线」（一处、四个字）。

**B｜`God View`（上帝视角）：「有真人参与的对局**默认禁用**」已经不是今天的行为。**
票 87 之后它不是「默认关、可以打开」，而是**有真人在座且未终局时连值都给不出来**
（`TableState.viewpoint`：发一条 `ViewpointPicked God` 进来也换不掉投影），**终局之后自己松开**。
「默认禁用」会让读的人以为有个开关能拨回来。建议改写，**本票不动**（那是第 11 条不变量，超出授权）。

**C｜`Fallback`（兜底）只说了两档，今天有三档、还多了一个触发口。**
① `ScaffoldTier.ToolSearch` 的兜底今天**照 Bare 打**（`Fallback.action` 里那一支自带注释说明
「等工具语义定了再说」）；② 真人席的**思考时限到点代打**恒走 Bare 那一支、**不看他自己的档位**
（89-2 的裁决），而词条只提「Player 输出非法、解析失败或超时且重试用尽」——真人那条路不在这几种里。

**D｜`ModelProfile`（模型档案）那句「四个维度各占一格」漏了第五格。**
89-4 之后座位级还有一项**思考时限**（`SeatBinding.Clock` / `SeatField.Clock`），
它与档案里的 `TimeoutMs` **量的不是一件事**（一次跨网请求的上限 vs 人自己的节奏），
而且**只画在真人那一行**。词条今天读起来像是座位级只有三项。

**E｜`Danger`（危险度）那句「其输出文案直接进入 prompt」今天有三个消费点。**
除了 prompt，还有真人的信息辅助（`HumanScaffold.toDisplay`）与复盘标注（`ReviewNote.figures`），
三处读的是同一份 `Scaffold`。「措辞必须与本表一致」这条约束因此比词条写的更宽。

**核过没问题的几条**（M3 动过语义、但词条仍然对）：`Observation`（票 99 改的是 `SeatStream` 的
fold 时机，`Observation` 的字段与「与那条流是同一件事的两种形态」都没变，而宣言窗口那一段
已经由票 100 收的 `Ankan Declaration` 词条管着）、`SeatStream`（新增的 `DeclaredKan` 不在词条
枚举的字段里，「输入是掩蔽事件流、输出是 Observation」照旧）、`Thinking Bubble`
（「有真人参与时终局前隐藏」票 87 起真的有人执行了，正是 `unlocked`）、
`Turn`（「复盘与 DecisionRecord 以手为编号单位」——**这次收词把它那个悬空的引用接上了**）。

---

## 5. 复原与验证

- 15 次破坏实验各改一处、跑完当场 `jj restore <那一个文件>`；
  最后一次复原后重跑了 `pnpm run fable`（免得留下一份改坏的 JS）。
- `jj st`：`The working copy has no changes.` → 之后才动 `CONTEXT.md`。
- 提交前 `./scripts/ci.sh` **全绿**（`/tmp/104/ci-final.log`，末行 `== CI 全绿 ==`）。
  `dotnet fantomas .` 未跑写盘那一遍——这一票没有 `.fs` 改动，`--check` 在 CI 里已绿。
- 提交里的 diff：`CONTEXT.md`（+59 −4）、票文件、本报告、`DECISIONS.md`。

## 6. 留给人的待审项

1. **104-2 的提案要不要立票**：「复盘上一个自造的合成数都不出」今天靠一张词的黑名单，
   要做成真闸门大约是「一条标注的三句话里，出现的每一个数字都得在引擎那份脚手架里找得到出处」
   ——那是一条**结构**断言，不是词表。票号由调度器分配（判据 17）。
2. **§4 的 B–E 四条**：`God View` 那条与代码直接不一致，建议下一次术语授权时一并改；
   C / D / E 是「写得不全」，改不改看主人。
3. **93-3 那两个浏览器侧执行者（`verify-review` ⑪⑫）在 CI 里执行 0 次**（判据 3）：
   它要那份 6 MB 的产物，而产物不入版本控制。今天的替代是 dotnet 侧那条逐字节对拍。
   这件事票 92/93 的报告已写过，这里只是收词时又撞了一次，一并记着。

---

## 7. code-review 两轴（fixed point `rysrqzux` / `952bedd1`）

派不出 sub-agent，两轴自己顺序跑（workbook 允许）。diff 是**纯文档**：
`CONTEXT.md` + 票文件 + 本报告 + `DECISIONS.md`，零 `.fs` / `.ts` / `.mjs`。

### Standards

标准源：`AGENTS.md`、`docs/agents/workbook.md`、`docs/agents/judgments.md`、
`docs/agents/triage-labels.md`、ADR-0001、`CONTEXT.md` 自己的词条格式。
`docs/agents/fsharp-style.md` 与 Fowler 的代码坏味在这一份 diff 上没有着力点（没有代码）。

- **硬约束逐条过**：只用 jj ✓；`./scripts/ci.sh` 全绿 ✓；测试零改动（谈不上放宽）✓；
  提交里没有 key ✓；`CONTEXT.md` 有单票授权（主人第十一次）✓；只动自己票里的文件 ✓
  （`Review*.fs` 与 `web/src/baseline/**` 在**提交里**零改动，票 103 不受影响）。
- **词条格式**：四段都是「定义 + 不变量 + `_Avoid_`」，与票 100 收的 `Ankan Declaration` 同形 ✓。
- **ADR-0001**：`Review` 不是日麻术语（日麻没有这个词），照代码的 `Review` 模块取名，
  与既有的 `Replay` / `God View` / `Thinking Bubble` 同一种处理 ✓。
- **引用的标识符逐个核过存在**：`SeatingPlan.soloHuman` / `fit` / `bind`、`Roster.humanName`、
  `TableState.viewpoint` / `assists`、`TablePanel.viewpoints`、`HumanScaffold.shows`、
  `Review.settled` / `notesFor` / `addressed` / `dominates`、`ReviewNote.Package`、
  `ReviewStrong`、`DecisionPackage.forSeat` / `scaffold`、`Ukeire.calculate`、`unlocked` ✓。
  其中 `dominates` / `unlocked` / `viewpoints` 是 private——`CONTEXT.md` 早有先例
  （`Seat` 模块的私有 `shift`、`GameState.kuikaeKinds`），不算问题。
- **判断题一条**：新词条名「强 AI 基线的对照标注」是一句中文短语而不是标识符。
  票面点名要这个名字，且 `CONTEXT.md` 里有同形先例（`打码（Redaction）`、`重试判据（Retry）`、
  `Honba 的点数`）。**保留**。
- **零发现的硬违规。**

### Spec（票 `.scratch/llm-riichi-arena/issues/104-terminology-harvest.md`）

- **(a) 票要而没做全的**：只有一处，**是票面自己要求的那一处**——
  「复盘上的每一个数……**没有总分**」的后半句没进术语表（票面原话：
  「这 10 条里只要有一条弄坏了没人红，那一条就不许写进术语表」）。
  证据在 §2 红-7b，处置在 `DECISIONS.md` 的 104-2。**其余 9.5 条全部收录且各有执行者。**
- **(b) 票没要而做了的（两处，都请调度器过目）**：
  1. **第 11 条不变量**「『更好』= 帕累托占优」写进了 `Review` 词条（执行者 `Review.dominates`，
     §2 红-11 按红过）。理由：那是 90-3 的**头一句**，而「更好的候选」这个词不给定义，
     词条就说不完整。**这超出了「10 条」那个数**，虽然仍在票面点名的 90-3 原文范围内。
  2. **`Player` 词条那个「强 AI 客户端」改成了通名**（§4 的 A）。票面许可「错字级的明显矛盾
     直接改并点名」，而它直接违反 ADR-0006 边界 5 的明文。改动是四个字。
- **(c) 做了但看着不对的**：无。所有断言的执行者都跑过一次红。
- **边界核对**：`spec.md` / `docs/adr/*` / `.github/**` / `src/Janpo.Engine/Shanten.fs`
  在提交里零改动 ✓。

**一句话小结**：Standards 轴 0 条硬违规 / 1 条判断题（词条名是中文短语，有先例，保留）；
Spec 轴 0 条缺漏 / 2 条超范围（都在报告里点名，等调度器裁）。
