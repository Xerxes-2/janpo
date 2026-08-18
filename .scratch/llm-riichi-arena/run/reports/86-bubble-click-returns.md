# 86 — 点开气泡跳走了，关掉跳回来（回程）

**结论：去程一个字没改，补上的是回程——「点开之前停在哪儿」存在摊开的那一手身上（`Opened.Origin`），
关掉与「按播放」两条路都先回到它。** `./scripts/ci.sh` **EXIT=0**，48.9s；
dotnet 748 + **214** 条（`ThinkingBubbleTests` 20 → **28**），浏览器闸门十四趟全 ✓。

主人试玩时报的是「气泡的点击是不是有问题，为什么会动时间轴」。查下来跳是有意的（票 76：轴只有票 75 那一根，
全文面板不另开时间轴，于是牌桌自己就是那一手的快照）——**那条设计没动**。漏的是回程：
`RecordOpened None` 从前只做 `Opened = None`，游标留在跳过去那一处。

---

## 1. 原处存在哪

存在**摊开的那一手身上**，不是模型上另起一格：

```fsharp
type Origin = { Cursor: int; Playing: bool }          // 点开之前：游标停在第几帧、那一刻在不在播
type Opened = { Snapshot: Table; Origin: Origin option }
type TableModel = { …; Opened: Opened option; … }     // 原来是 `Opened: Table option`
```

**两格在同一个记录里**：有摊开的面板才有回得去的原处，拆成模型上的两格就表示得出
「有原处却没摊开」这种对不上的状态——而这一票治的正是「状态被改了却没人管改回来」。
`Origin` 是 option：**Live 那一侧没有可回的地方**（`openAt` 只摆快照，游标与 `live.Table` 都不动），
那边恒是 `None`，收起来仍旧只是收起来（要害 5）。

三个消费点：

| 谁 | 读它做什么 |
|---|---|
| `returned`（`RecordOpened None`） | 游标搬回 `Cursor`，播放状态按 `Playing` 还回去，**在播就续一记定时器** |
| `rewound`（`moves` 那条路） | **只搬游标**；播放状态让那条消息自己说了算（见 §3） |
| `detail` → `BubbleDetail.Origin: int option` | 面板上那句「正在看第 N 手」的判据（回放/Live 两句话），并给闸门读 `data-bubble-origin` |

**只记头一次**（要害 4）：`openAt` 里 `match model.Opened with Some opened -> opened.Origin | None -> 现在这一处`。
连点两家气泡时第二下是从「已经跳过去那一处」出发的，把它当原处的话关掉只回到上一次跳之前。

## 2. 怎么保证世代号不过期

`Origin` **不存 `Playback`，只存一个 bool**。理由是形状上的：那份旧值里的世代号在点开那一刻就已经过期
（`openAt` 的 `pause` 换掉了它），**存着它就早晚有人原样放回去**——那是票 78 按红过的坑
（在飞的那记定时器被重新认下，牌桌从此双倍速走）。收不进来就放不回去。

回程只有一条路：

```fsharp
// Playback.fs
let resumed (playing: bool) : Playback -> Playback =
    reborn (fun playback -> { playback with Playing = playing })   // 接着**现在**那个世代往下换
```

`reborn` 是这个模块里唯一的迁移入口（`restart` / `toggle` / `pause` / `setSpeed` 都经它），
因此「每次改播放状态都换世代」这条不变量在回程上照旧成立。执行它的是两条断言（判据 2）：

- **行为面**：`closed |> step (Ticked before.Playback.Generation)` 之后游标**不许动**（过期的那一记要被丢掉）；
- **诊断面**：`closed.Generation > opened.Generation`。

R3 与 R4 分别把这两条按红过（§5）。

**倍速不在回程里**：面板摊着的时候倍速仍拨得动（`SpeedPicked` 不推进牌桌，面板不收），
把点开那一刻的倍速一并塞回去等于把人刚拨的那一下悄悄撤掉。这一条也有用例钉着
（摊开时拨到 8×，收起来仍是 8×）。

## 3. 「按播放」那条路怎么处理的

今天任何推进牌桌的消息都会收起面板（`moves`，票 76 逐 case 穷举那张表）。改的是 `update` 头一行：

```fsharp
let model = if moves message then rewound model else model      // 原来是 { model with Opened = None }
```

**先回原处，再让那条消息跑**，而且 `rewound` **只搬游标不动播放状态**。这一条是这一票里唯一一处
「票面没写死、我自己判的」：

- 按「播放」的人要的是**从原处往下播**。若连播放状态一起还原，「点开之前正在自动播」那一路会变成
  「还原成在播 → `PlayToggled` 把它 toggle 成暂停」——**人按了播放，牌桌停住**。
- 拖时间轴 / 从头再放这两条消息自己就在搬游标，让它们说了算就是原样（`rewound` 先搬回原处，
  紧接着 `moveCursor` / `Restarted` 覆盖掉）。这一条也立了用例（拖到第 40 帧就得停在 40，不许被回程顶掉），
  R5 把「回程放在消息之后」这种写法按红过。

于是两条路的分工是：**关掉 = 回到点开之前那一刻（连播放状态）**，**按播放 = 回到点开之前那一处再往下播**。

## 4. 面板上那句话（人看得见自己被搬到了哪儿）

`bubble-viewing`（新 testId，只增不改）：摊开的面板头一句话。**两种来源两句话**，判据在
`BubbleDetail.toDisplay`（视图仍旧只画不判）：

- 回放：`正在看第 11 手：时间轴跟着跳到了这一手，收起（或按「播放」）就回到点开之前那一处。`
- Live：`正在看第 4 手：牌桌上摆的是那一刻的快照，这一桌照旧停在现在那一手。`

**Live 那一句里不提时间轴**：`?table=1` 上根本没有时间轴（`timeline` 在那边恒是 None），
说它就是在说一个页面上不存在的东西。用例两头都钉（回放里要有「时间轴」，Live 里不许有）。

顺带一个 `data-bubble-origin`（点开之前那一帧的帧号，Live 上是空串）给闸门读。
**没有为它新起 CSS 类**：它与旁边那句用同一种画法（`intro`），一条谁也不用的类名是句空话。

**我把图打开看了**（判据 7）：两张都亲眼看过。
Live 那一屏（`verify-bubbles --shoot`，1280×2708）：面板抬头「第 4 手・座位 0・东1局 0 本场」＋「收起」，
下面第一行就是那句「正在看第 4 手：牌桌上摆的是那一刻的快照，这一桌照旧停在现在那一手。」，
再下面才是原来那句「上面那张牌桌就是这一手落定那一刻的局面快照（只读；……）」，九样一行不少。
首页那一屏（一次性 playwright 探针，**没进仓库、跑完删了**）：点座位 1 的气泡，游标 13 → 12，
面板上那句是「正在看第 11 手：时间轴跟着跳到了这一手，收起（或按「播放」）就回到点开之前那一处。」

## 5. 每条新断言先红一次（判据 1 的原始输出）

**十次，全部实跑**：改**产品代码**（不是改断言），跑同一条命令，抄红的原文，最后 `diff` 对回备份。
dotnet 侧命令一律 `dotnet test tests/Janpo.Web.Tests --filter ThinkingBubbleTests`。

### R1｜回程整个还不存在（写完用例、还没动实现那一刻）

```
失败 ThinkingBubbleTests.按「播放」也算关：先回原处再往下播，不许从跳过去的地方接着播
   Assert.Equal() Failure: Values differ　Expected: 11 / Actual: 9
失败 ThinkingBubbleTests.回放里点开某一手：游标跟着挪到那一帧，轴只有一根
   Assert.Equal() Failure: Values differ　Expected: 11 / Actual: 9
失败 ThinkingBubbleTests.关掉全文面板：游标与「在播不在播」一起回到点开之前那一处
   Assert.Equal() Failure: Values differ　Expected: 11 / Actual: 9
失败 ThinkingBubbleTests.面板摊着的时候拨倍速：收起来照人刚拨的那一档，不是点开那一刻的
   Assert.Equal() Failure: Values differ　Expected: 11 / Actual: 9
失败 ThinkingBubbleTests.连点两家气泡再关：回到最初那一处，不是上一次跳之前那一处
   Assert.Equal() Failure: Values differ　Expected: 11 / Actual: 8
失败 ThinkingBubbleTests.点开之前暂停着：收起来还是暂停着，游标照样回到原处
   Assert.Equal() Failure: Values differ　Expected: 11 / Actual: 9
失败!  - 失败: 6，通过: 21，总计: 27
```

（「连点两家」那一条 `Actual: 8` 是第二次跳过去那一处——正是要害 4 说的「回到上一次跳之前」。）

### R2｜回程不还播放状态（`Playback.resumed` 恒 false）

```
失败 ThinkingBubbleTests.关掉全文面板：游标与「在播不在播」一起回到点开之前那一处
   点开之前在播，收起来就该接着播
失败 ThinkingBubbleTests.面板摊着的时候拨倍速：收起来照人刚拨的那一档，不是点开那一刻的
   Assert.True() Failure　Expected: True / Actual: False
失败!  - 失败: 2，通过: 26，总计: 28
```

### R3｜`resumed` 不走 `reborn`（世代号不换）

```
失败 ThinkingBubbleTests.关掉全文面板：游标与「在播不在播」一起回到点开之前那一处
   回程该换一个新世代（点开之前 3、摊开时 4、收起来 4）
失败!  - 失败: 1，通过: 27，总计: 28
```

### R4｜`Origin` 存整份 `Playback` 并**原样放回去**（票 78 那个坑的形状）

```
失败 ThinkingBubbleTests.关掉全文面板：游标与「在播不在播」一起回到点开之前那一处
   Assert.Equal() Failure: Values differ　Expected: 11 / Actual: 12
失败 ThinkingBubbleTests.面板摊着的时候拨倍速：收起来照人刚拨的那一档，不是点开那一刻的
   Assert.Equal() Failure: Values differ　Expected: X8 / Actual: X2
失败!  - 失败: 2，通过: 26，总计: 28
```

**11 → 12 就是那个坑本身**：过期世代被放回去之后，点开之前那一记还在飞的定时器被重新认下，
牌桌自己多走了一帧。第二条同时说明「倍速也跟着被撤回去了」。

### R5｜回程放在那条消息**之后**（顺序反了）

```
失败 ThinkingBubbleTests.拖时间轴与从头再放：面板收起来，而回程不许把人刚拖到的那一帧顶掉
   Assert.Equal() Failure: Values differ　Expected: 40 / Actual: 11
失败!  - 失败: 1，通过: 27，总计: 28
```

### R6｜Live 那一侧也记一份原处

```
失败 ThinkingBubbleTests.面板上说得出它把人搬到了哪儿：「正在看第 N 手」，回放里还说清收起会回去
   Assert.Equal() Failure: Values differ　Expected: null / Actual: Some(0)
失败 ThinkingBubbleTests.Live 那一侧一字不变：点开与收起前后，这一桌、推进能力与播放状态逐项相同
   Assert.False() Failure　Expected: False / Actual: True
失败!  - 失败: 2，通过: 26，总计: 28
```

### R7｜面板那句话不看原处（两种来源同一句）

```
失败 ThinkingBubbleTests.面板上说得出它把人搬到了哪儿：「正在看第 N 手」，回放里还说清收起会回去
   Assert.DoesNotContain() Failure: Sub-string found
   String: "正在看第 0 手：时间轴跟着跳到了这一手，收起（或按「播放」）就回到点开之前那一处"···
失败!  - 失败: 1，通过: 27，总计: 28
```

### R8｜面板不知道原处（`detail.Origin` 恒 None）

```
失败 ThinkingBubbleTests.面板上说得出它把人搬到了哪儿：「正在看第 N 手」，回放里还说清收起会回去
   Assert.Equal() Failure: Values differ　Expected: Some(11) / Actual: null
失败!  - 失败: 1，通过: 27，总计: 28
```

### R9｜浏览器侧：回程整个去掉（退回票 76 那一版）

`node scripts/verify-home.mjs`（真页面、真点击）：

```
首页少了该给访客的东西：
按 bubble-close 关掉面板之后游标停在第 465 帧，没回到点开之前那一处（第 469 帧）：看完一条理由，时间轴不许被搬走（票 86）
按 table-play 关掉面板之后游标停在第 465 帧，没回到点开之前那一处（第 469 帧）：看完一条理由，时间轴不许被搬走（票 86）
```

**这一条第一版只红了一句**（判据 3 当场咬了我一口）：`table-play` 那一路原本靠
`settledAt(page, 原处)`「等游标等于原处」——**没有回程时它一帧一帧往前播，几百毫秒后照样路过原处**，
于是那条断言等得到、永远绿。改成**面板一消失就当场读游标**（两件事在同一次 update 里，
读到的就是落定值；而原处正是末帧，按播放回去之后 `replayTick` 当场停下，不必与定时器赛跑）之后，
两条路才都红。

### R10｜浏览器侧：`bubble-viewing` 整个不画

`node scripts/verify-bubbles.mjs`（Live 那一页）：

```
思考气泡这一道没过：
面板上那句「正在看第 N 手」与它摊开的那一手（第 4 手）对不上：「null」
面板上根本没有「正在看第 N 手」那一句（[data-testid="bubble-viewing"]）：人因此看不见自己正在看哪一手（票 86）
```

`node scripts/verify-home.mjs`（回放那一页，两条路各报一次）：

```
首页少了该给访客的东西：
回放里摊开的面板没说清它把人搬到了哪一手（该是第 459 手）：「（面板上根本没有那一句）」
回放里摊开的面板没说清它把人搬到了哪一手（该是第 459 手）：「（面板上根本没有那一句）」
```

（头一版的第二句诊断是「Live 那一页的面板写着一个原处（data-bubble-origin=「null」）」——
元素根本不在时它报的是「原处写错了」。**错的诊断比没有诊断更贵**（判据 12），
因此把「那一句没画出来」与「原处写错了」分成了两句话，上面的输出是分完之后重跑的。）

## 6. 改了一条既有断言的期望值（判据 5，得说清楚）

`ThinkingBubbleTests.回放里点开某一手：游标跟着挪到那一帧，轴只有一根` 的最后一句：

```fsharp
// 票 76：收起来：面板没了，牌桌仍停在那一帧（收起不是「跳回去」）。
Assert.Equal(9, (timelineOf closed).Cursor)
// 票 86：
Assert.Equal(11, (timelineOf closed).Cursor)
```

**那份期望本身就是这一票要治的病**：票 76 把「收起不是跳回去」写进了注释与断言，
而主人试玩时报的正是它。票 86 的票面第一条验收（「关面板之后游标与播放状态回到点开之前那一刻」）
就是重裁这一条。同一条用例的前半段（点开时游标真的挪到第 9 帧）**一个字没动**——
去程仍旧钉着，改的只有回程那一句。除此之外**没有第二处期望值被改**，
`ThinkingBubbleTests` 原来的 20 条只多不少（现在 28 条）。

## 7. 闸门：谁在守什么

### dotnet 侧（`ThinkingBubbleTests` 新增 8 条）

| 用例 | 钉的是「改过去」与「改回来」哪两头 |
|---|---|
| 关掉全文面板：游标与「在播不在播」一起回到点开之前那一处 | 改过去：游标 11→9、面板开了、一点就暂停；改回来：游标 9→11、在播回来了、倍速不变、**过期世代作废且当前世代推得动** |
| 点开之前暂停着：收起来还是暂停着，游标照样回到原处 | 「一点就暂停」不许被回程改成「一收起就开播」 |
| 面板摊着的时候拨倍速：收起来照人刚拨的那一档 | 回程只还「在播不在播」，不还倍速（人刚拨的那一下不许被撤） |
| 按「播放」也算关：先回原处再往下播 | 面板收了 + 游标回 11 + 在播 + **下一帧是 12（原处的下一帧），不是 10** |
| 拖时间轴与从头再放：回程不许把人刚拖到的那一帧顶掉 | 回程与消息的先后（拖到 40 就停在 40、从头再放停在 0） |
| 连点两家气泡再关：回到最初那一处 | 两次跳（11→9→8）之后，「收起」与「按播放」两条路都回 11 |
| Live 那一侧一字不变 | `live.Table`（手数 / 事件流 / 决策记录）、`canAdvance`、播放状态在点开与收起三个时刻逐项相同 |
| 面板上说得出它把人搬到了哪儿 | 回放：`Origin = Some 11` 且那句话提时间轴；Live：`Origin = None` 且**不许**提时间轴 |

### 浏览器侧（不新增趟数，两趟各加几条）

- `verify-home.mjs`（回放那一页，真点击）：**⑩ 点开跳走了、关掉跳回来**——先挑手序最小那一席的气泡
  （它落定那一帧必然不是末帧，「游标真的挪了」才验得到东西），按「收起」与按「播放」**两条路各走一遍**，
  面板一消失就当场读游标，必须逐字等于点开之前那一帧；顺带核面板那句话写着第几手。
- `verify-bubbles.mjs`（Live 那一页）：面板那句话与摊开的那一手对得上，且 `data-bubble-origin` **必须是空的**
  （那边没有游标）——Live 侧「什么也没搬走」这件事因此在浏览器里也有人守。

## 8. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，48.9s（另一次 53.3s）；dotnet 748 + 页面 **214** 条 |
| 回程那几条单跑 | `dotnet test tests/Janpo.Web.Tests --filter ThinkingBubbleTests` | 28 条全绿（原 20） |
| 首页那一道单跑 | `cd web && node scripts/verify-home.mjs` | 十条各印一行 ✓（新那句：游标 469 → 第 459 手那一帧 → 469） |
| 气泡那一道单跑 | `cd web && node scripts/verify-bubbles.mjs` | 四段各印一行 ✓ |
| 每条新断言先红 | §5 十次 | 全部红过，输出抄在 §5 |
| 截图 | `verify-bubbles.mjs --shoot`＋一次性探针（已删） | §4，两张都打开看过 |
| 还原干净 | `diff` 对回 `/tmp/t86bak/*` | `TableState` / `Playback` / `ThinkingBubble` 三样全 OK |

`jj diff --stat`：6 个文件、+436 −27。**没碰**：引擎（`src/Janpo.Engine/**` 一字未动）、`Paifu`、
`web/src/agent/**`、`web/public/demo-paifu.json`、`docs/adr/*`、`CONTEXT.md`、`styles.css`、
`TableBoard.fs` / `TablePage.fs` / `TablePanel.fs`（票 82 的地盘）、`reveals` / `unlocked`（票 81 的两根轴）、
气泡的文字判据与 72 字上限。testId 只增了一个（`bubble-viewing`），没改名、没删。

## 9. code-review（Standards + Spec 两轴，fixed point `149799ea`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓**：全程 `jj status` / `jj diff` / `jj describe` / `jj commit`，无远端操作、无交互式 flag。
- 工具强制的 `fantomas --check` / `check-style.sh` / Biome / tsc 全绿；`let mutable` 未新增。
- **F# 风格**（`docs/agents/fsharp-style.md`）：新代码都是从左往右的一条流
  （`model.Opened |> Option.bind (fun opened -> opened.Origin)`、
  `back.Playback |> Playback.resumed origin.Playing`）；构造子嵌套按限制 A 保留
  （`Source.Replay(ReplayTable.Ready(frames, origin.Cursor))`）；没有强行管道化的布尔链与算术。
  两处重复的 `Option.bind` 抽成了 `originOf`（`rewound` 与 `returned` 读同一处）。
- **注释写「为什么」✓**：原处为什么只记头一次、为什么不存整份 `Playback`、
  为什么 `moves` 那条路不还播放状态、Live 为什么恒是 `None`——都写在代码上。
- **术语 ✓**：`Origin` / `Opened` 是页面状态层的名字，没自造日麻术语；**`CONTEXT.md` 一字未改**。
- **blocking：0。**

### Spec（票面五条行为 + 五条闸门 + 六条边界）

逐条对照见票文件的勾选框。三处值得写下来：

1. **「点开跳到那一手」一个字没改**（票面第一条边界）：`openAt` 仍旧挪游标、仍旧暂停，
   改的全是它的逆动作。
2. **Live 侧的「行为不许变」我按最严的读法做**：那边**根本不记原处**（`Origin = None`），
   收起来就只是 `Opened = None`，与票 86 之前逐字相同。被否掉的另一种做法记在 DECISIONS（86-1）。
3. **「按播放」那条路只回游标不回播放状态**，理由见 §3（票面没写死，我判的；记在 DECISIONS 86-2）。

### 记录但没改的 nitpick

1. 面板抬头写「第 8 手」用的是 `record.Turn`（0 起的手序），时间轴上那句「第 9 手」用的是
   `Timeline.Turns`（这一帧落定了几手）——**同一帧两个数**，票 75/76 就是这样，我这一票没动它，
   新那句话因此**只提一个数**（正在看第 N 手），不提原处是第几手，免得把两套编号摆在一句话里。
   真要统一得连票 75 那条用例一起改，是另一张票。
2. `verify-home.mjs` 的文件头还写着「八条断言」（现在十条）。那是票 71/76/79/81/83 一路加上去的，
   我加了 ⑩ 但**没重编号那段抬头**（它同时被票 82 可能动到）——留给集成时顺手改。
3. `BubbleDetail` 多了一格 `Origin`（判据 9 说的「第二个只能从历史算的字段」不适用：它不是从历史算的，
   是点开那一刻记下来的）。若将来面板还要第三格「回程会怎么样」，那时该停下来问问形态。

## 10. 留给人的待审项

1. **面板上新增的那一句**（§4）出现在**回放与 Live 两屏**上。措辞若要改，改的是
   `TableState.BubbleDetail.toDisplay` 一处（两句话都在那儿），闸门里对着的那两句字符串跟着改。
2. **「按播放」= 回原处再往下播**（§3）：这是我判的，票面只说「先回原处再往下播」。
   若主人认为按播放应当**连播放状态一起还原再 toggle**（即「点开之前在播 → 按播放变成暂停」），
   那是 `rewound` 改一行 + 两条用例改期望——**没在这一票做**。
3. **Live 那一侧仍旧不回播放状态**：主持人那一桌若正在自动播时点开一个气泡，会暂停；收起来**不会**自己接着播
   （票面「Live 行为不许变」）。哪天觉得两侧该一致，改的是 `openAt` 里那个 `None` 分支。
</content>
</invoke>
