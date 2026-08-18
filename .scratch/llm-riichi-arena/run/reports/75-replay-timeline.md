# 75 — 回放的时间轴：一根滑块 + 逐事件步进 + 局边界，外加 71-8 的上帝视角

**结论：游标动的路只有一条 `CursorMoved`，帧一份没多、定时器一套没加。**
时间轴是**三排控件**（播放 / 拖动与步进 / 局边界）挂在 `TablePanel.replayControls` 里；
拖动就是 `ReplayTable.Ready(frames, cursor)` 里那个整数变一下，O(1) 取帧。
`./scripts/ci.sh` **EXIT=0**，36.6s（基线 39.8s）；dotnet 744 + **132** 条（原 121），浏览器闸门**十趟**全 ✓。

**两组更大的数量出来了，都撑得住**：半庄 741 帧 fold **132 ms**、首屏 **221 ms**；
换成票 79 那种带 thinking 的 2.54 MB 资产之后 fold **142 ms**、首屏 **237 ms**、
留住一份帧多占 **6.1 MB** 堆。**拖动一次 31 ms，256 帧与 741 帧无差别。**
没有在这一票动帧的形状（票面明令），阈值建议写在 §4.4。

---

## 1. 时间轴的形态

### 1.1 一条消息，四种走法

```fsharp
| CursorMoved of frame: int      // 拖动、上一步 / 下一步、跳局边界，全走它
```

**刻意不给「上一步 / 下一步 / 跳局」各立一条消息**：它们说的是同一件事——把游标挪到某一帧。
越界的帧号在 `moveCursor` 里夹回 `[0, 末帧]`，因此视图那边不必先算一遍边界。

**没有复用 `Advanced`**：那条在 Live 里是「推进一手并暂停」，而 `HomePageTests` 有一条
钉着「Live 那几条消息在回放里一律无事发生」——让它在回放里动游标就得放宽那条断言（硬约束 3）。

**一拖就暂停**（`Playback.pause`，顺带换世代号，在飞的那记定时器因此作废）：手搭在时间轴上的人
显然不想让定时器接着跑，与 Live 的「单步」同一个做法。**再按播放是从新游标往下走**，
不是从头——`replayTick` 读的就是当前游标（用例 `一拖就暂停，再按播放是从新游标往下走` 钉着）。

### 1.2 `Timeline`：视图与用例读同一处推导

```fsharp
type KyokuMark = { Frame: int; Label: string }        // 「东1」「东2·1」，Frame 是那一局的开局帧

type Timeline = {
    Cursor: int                  // 现在停在第几帧（滑块的 value）
    Last: int                    // 末帧号 = 帧数 − 1（滑块的 max）
    Turns: int                   // 这一帧落定了几手（跨局累计）
    Marks: KyokuMark list        // 各局的开局帧，升序
    Kyoku: int                   // 现在停在第几局（Marks 里的序号，0 起）
    Record: DecisionRecord option // **刚落定那一手**的决策记录
}

TableState.timeline : TableModel -> Timeline option   // Live 与还没 fold 好的那两段都是 None
TablePage.timeline  : TableModel -> Timeline option   // 转出去，dotnet 侧用例调的是它
```

**三处推导都在这里，没有第二份**：

| 格 | 怎么来的 | 为什么不用别的 |
|---|---|---|
| `Marks` | 扫一遍帧，取 `Latest = None` 的那几帧 | 那正是 `Table.opened` 干的事（它把「上一手」清空，而落定的每一手都会写上一手） |
| `Kyoku` | `Marks` 里帧号 ≤ 游标的个数 − 1 | **不拿 `Game.played` 的长度**：那一格在一局终了那一帧就已经 +1，拿它划局会把结算那一屏划给下一局 |
| `Record` | `Decisions` 的最后一条，且它的 `Turn = Turns − 1`；`Latest = None` 时一律 None | 帧上那几条是「手序 < 这一帧手数」的全部（票 71 切的），不问手序就会**粘着不掉** |

`Marks` **现扫而不存下来**（判据 9：多存一份就多一份会漂的东西）。代价量过：741 帧的半庄
与 256 帧的东风战，一次拖动都是 **31 ms**（§4.2），扫一遍不在噪声之上。

### 1.3 页面上多了三排

`TablePanel.replayControls` 从「一个 `.controls`」变成「一个 `.replay-controls` 装三排」，
**`TablePage` 一行没改**（它仍旧只调 `TablePanel.controls`）：

```
[播放]  暂停 | 从头再放 | 倍速 1× 2× 4× 8×
[时间轴] 上一步 | 下一步 | ━━━━●━━━━━━━━ | 第 24 手・东1局
[局边界] 跳到 | 东1 | 东2·1 | 东3 | 东4
[记录]  第 N 手・座位 K：兜底/理由/thinking 原文     ← 有记录时才画
```

新的 testId 五个：`table-timeline`（range 输入框，带 `data-cursor` / `data-last`）、
`table-timeline-at`（带 `data-turns` / `data-kyoku`）、`table-back` / `table-forward`、
`table-kyoku-{i}`。**一个都不与 `verify-home` 那份「只属于主持人那一页」的名单撞**
（那份名单里的 `table-next` 是 Live 的「下一局」，因此步进按钮叫 `table-back`/`table-forward`）。

`prop.onChange (fun (frame: int) -> …)` 走的是 Feliz 收 `int` 那条重载，它读 `valueAsNumber`
——range 输入框该用的正是这一个。

**决策记录那一段现在在页面上出现 0 次**（判据 4：语料到不了的情形要显式写出来）：
首页那份 Demo 是 bot 牌谱，一条记录都没有。阳性对照拌在
`ReplayTimelineTests.游标动时刚落定那一手的决策记录跟着变` 里（拌一条 `Turn = 7` 的记录进去，
第 8 帧看得见、第 7 与第 9 帧看不见）。**票 79 换上真资产之后它才在页面上开口**。

### 1.4 71-8：回放默认上帝视角

`TableState.home ()` 的 `Viewpoint` 从 `Seated Seat.first` 改成 `God`。**`initial`（Live）一字未动**。
首页那段文案跟着改准了（原文说的是「他家的手牌看不到牌面」，那是给 Live 写的）：

> 这是上帝视角，四家的牌都摊着——牌谱已经打完了，复盘本来就该看得见四家；想验「模型看到的和你一样多」
> 就按一下坐到某个座位，那时他家的暗牌在页面拿到的数据里根本不存在。

钉住它的有三处：dotnet 侧 `回放默认上帝视角：四家的牌都摊着`（上帝视角那份投影里
**没有一个 `HandView.Concealed`**，切回座位视角后**必须有**——阳性对照在同一条用例里）、
`Live 那一页的默认视角不动`、以及 `verify-home` 的 ⑤（`data-hand-hidden` 四家全 `false`，
切到座位 0 后至少三家 `true`）。

---

## 2. 每道新断言先红一次（判据 1 的原始输出）

**十次，全部实跑过，跑完逐个 `diff` 对回备份**（§5）。dotnet 侧 11 条用例、浏览器侧 2 条，
每一条都被至少一次改坏按红过。

### 红-1｜游标错一格（`moveCursor` 用 `frame + 1`）

```
Janpo.Web.Tests.ReplayTimelineTests.拖到中间那几个游标：手牌 / 河 / 点数与直接 fold 同一前缀得到的一致 [FAIL]
  错误消息:  Assert.Equal() Failure: Values differ
  Expected: 28
  Actual:   29
Janpo.Web.Tests.ReplayTimelineTests.逐事件步进：一步一帧，走 N 步与一拖到位落在同一帧 [FAIL]
  Expected: 61 / Actual: 63
Janpo.Web.Tests.ReplayTimelineTests.一拖就暂停，再按播放是从新游标往下走 [FAIL]
  Expected: 31 / Actual: 32
Janpo.Web.Tests.ReplayTimelineTests.局边界：一局一枚，跳过去就落在那一局的开局帧 [FAIL]
  错误消息:  第 0 局的开局帧还没走一手
Janpo.Web.Tests.ReplayTimelineTests.游标动时刚落定那一手的决策记录跟着变 [FAIL]
  Expected: Some({ Turn = 7 … })
  Actual:   null
失败! - 失败: 5，通过: 127，总计: 132
```

### 红-2｜「增量维护漂了」：**往回拖时**目标帧的掩蔽流沿用来处那一帧的

```
Janpo.Web.Tests.ReplayTimelineTests.同一个游标来回到达两次，渲染逐字段相同 [FAIL]
  错误消息:  Assert.Equal() Failure: Values differ
  Expected: Some({ Bakaze = Ton
              Kyoku = 2
              Honba = 1
              Kyotaku = 1
              DoraMarkers = [{ Kind = 12 … }]
              UraMarkers = []
              …
Janpo.Web.Tests.ReplayTimelineTests.逐事件步进：一步一帧，走 N 步与一拖到位落在同一帧 [FAIL]
  错误消息:  前进一步再后退一步没回到原地那一张牌桌
失败! - 失败: 2，通过: 130，总计: 132
```

**这一条先怀疑了断言自己（判据 6）。** 头两版改坏法都没能把它按红：
① 无差别地漂 → 两次到达都从同一处过来，一起漂，断言白给；
② 有方向地漂但来回路径撞车（`once` 与 `again` 的最后一跳都从第 0 帧过来）→ 又白给。
**是断言的走法太弱，不是被测物没坏**。改法：`again` 的最后一跳**故意从末帧过来**，
并在用例里写下这条理由。这一课值得留着——「幂等」类断言必须让两次到达**走不同的路**。

### 红-3｜下界钉在 1（`frame |> max 1`）

```
Janpo.Web.Tests.ReplayTimelineTests.拖回 0 就是开局那一瞬 [FAIL]
  Expected: 0 / Actual: 1
Janpo.Web.Tests.ReplayTimelineTests.越界的帧号夹回 [0, 末帧]，不许把牌桌弄丢 [FAIL]
  Expected: 0 / Actual: 1
Janpo.Web.Tests.ReplayTimelineTests.局边界：一局一枚，跳过去就落在那一局的开局帧 [FAIL]
  错误消息:  第 0 局的开局帧还没走一手
失败! - 失败: 3，通过: 129，总计: 132
```

### 红-4｜上界钉在末帧 −1（`min (last - 1)`）

```
Janpo.Web.Tests.ReplayTimelineTests.拖到末尾就是票 71 今天那一屏：结算面板与终局精算都在 [FAIL]
  错误消息:  末帧该有结算面板
Janpo.Web.Tests.ReplayTimelineTests.拖到中间那几个游标：…… [FAIL]
  Expected: 255 / Actual: 254
Janpo.Web.Tests.ReplayTimelineTests.越界的帧号夹回 [0, 末帧]，不许把牌桌弄丢 [FAIL]
  Expected: 255 / Actual: 254
失败! - 失败: 3，通过: 129，总计: 132
```

（这一条也修过一次断言：原本先核游标再核结算，于是红出来的是一个光秃秃的帧号。
改成**先核那一屏上真有的东西**之后，红的第一句就是「末帧该有结算面板」。）

### 红-5｜首页默认视角改回座位 0（71-8 没做）

dotnet 侧：

```
Janpo.Web.Tests.ReplayTimelineTests.回放默认上帝视角：四家的牌都摊着 [FAIL]
  Expected: God
  Actual:   Seated { Index = 0 }
失败! - 失败: 1，通过: 131，总计: 132
```

浏览器侧（`node scripts/verify-home.mjs`）：

```
首页少了该给访客的东西：
首页不是上帝视角：四家里有 3 家的手牌扣着（裁决 71-8）
```

### 红-6｜`CursorMoved` 在 Live 那一侧也动点什么

```
Janpo.Web.Tests.ReplayTimelineTests.Live 那一页的默认视角不动，也没有时间轴 [FAIL]
  错误消息:  Assert.Equal() Failure: Values differ
  Expected: { Playing = false; Speed = X1; Generation = 0 }
  Actual:   { Playing = false; Speed = X1; Generation = 1 }
失败! - 失败: 1，通过: 131，总计: 132
```

### 红-7｜决策记录不问手序（`List.tryLast` 直接返回）

```
Janpo.Web.Tests.ReplayTimelineTests.游标动时刚落定那一手的决策记录跟着变 [FAIL]
  错误消息:  Assert.Equal() Failure: Values differ
  Expected: null
  Actual:   Some({ Turn = 7
              PromptTail = "【现在】……"
              Reason = Some "就它了"
              Thinking = Some "想了想" … })
失败! - 失败: 1，通过: 131，总计: 132
```

### 红-8｜拖动不动游标（`moveCursor` 原样返回当前游标）

```
首页少了该给访客的东西：
在时间轴 75% 处点一下，游标却停在 3/255
「下一步 / 上一步」没把游标挪到第 4 / 3 帧（逐事件步进坏了）
拖到滑块最左端，游标却没回到第 0 帧
拖回第 0 帧却不是开局那一瞬：四家的河共 3 张，上一手那句写着「上一手：座位 2 手切6索」
拖到后面与拖回开头看到的是同一屏（河各 3 / 3 张）：牌桌没跟着游标走
跳到第二局落在了第 0 局、第 3 手，上一手那句写着「上一手：座位 2 手切6索」
```

### 红-9｜「上一步」也往前走（浏览器侧的幂等断言）

```
首页少了该给访客的东西：
「下一步 / 上一步」没把游标挪到第 194 / 193 帧（逐事件步进坏了）
同一个游标（第 193 帧）来回到达两次，牌桌却不一样了：
13|false|手牌 131万9万5筒赤5筒8筒9筒1索1索4索4索5索白白|8|河 88索8万中2筒8索西6万3索|24000
…
上一手：座位 2 摸切中
第 191 手・东3局
——
14|false|手牌 141万9万5筒赤5筒8筒9筒1索1索4索4索5索白白2索|8|河 88索8万中2筒8索西6万3索|24000
…
上一手：座位 3 手切6万
第 193 手・东3局
```

### 红-10｜时间轴整条不画（= 票 71 今天那一屏）

```
首页少了该给访客的东西：
首页上没有时间轴（[data-testid="table-timeline"]）：回放拖不动（票 75）
```

---

## 3. 闸门：谁在守什么

### 3.1 dotnet 侧（`tests/Janpo.Web.Tests/ReplayTimelineTests.fs`，11 条）

**「拖到某一处 = 直接 fold 同一前缀」用的是第三个锚点**（判据：不许拿同源结果当右侧）。
页面那一侧的帧是 `Replay.trace` 交出来的动作序列、由 `Table.apply` 一手一手落出来的；
用例这一侧把牌谱的事件流**截到第 N 条**，让 `Replay.game` **重建另一座牌山、重跑一次 fold**，
再逐项比手牌 / 河 / 副露 / 点数 / 供托 / 剩余摸牌。游标由截断后的轨迹算出来
（`帧数 = 手数 + 局数`，票 71 的形态）。五个截点：事件流的 1/8、1/4、1/2、3/4 与末尾。

**比的是「看得见的那几样」而不是整个 `GameState`**，理由写在用例里：截断之后引擎停在
「等这一张的响应」那一刻，而帧那一侧已经把没宣言的那几家的「过」交回去了——
`Action.None` 不产出事件，截断的流里根本看不见它。**阶段会差一步，看得见的东西一张都不许差。**

| 用例 | 钉的是 |
|---|---|
| 拖到中间那几个游标：…与直接 fold 同一前缀得到的一致 | 上面那条，五个截点 |
| 同一个游标来回到达两次，渲染逐字段相同 | 五个视角的投影、四条掩蔽流、结算、终局精算、`Timeline` 自己，最后再核**是不是同一张牌桌**（`ReferenceEquals`） |
| 拖回 0 就是开局那一瞬 | 手数 0、没有上一手、没有记录、四家的河都空 |
| 拖到末尾就是票 71 今天那一屏 | 结算面板 + 终局精算 + `canAdvance = false` |
| 越界的帧号夹回 [0, 末帧] | 四种越界值，且夹回来之后仍摆得出牌桌 |
| 逐事件步进：一步一帧，走 N 步与一拖到位落在同一帧 | 一步最多走一手；来回一步回到同一张牌桌；走五步 == 一拖到位 |
| 局边界：一局一枚，跳过去就落在那一局的开局帧 | 枚数取自**事件流里的 `start_kyoku` 条数**（不是从帧那边数的）；帧号升序；落点没走一手、不是终了的；标签与那一帧的场况对得上 |
| 一拖就暂停，再按播放是从新游标往下走 | 世代号换掉、`PlayToggled` 之后 `Ticked` 落在游标 +1 |
| 游标动时刚落定那一手的决策记录跟着变 | 阳性对照（Demo 本身 0 条记录，先断言这件事，再拌一条进去） |
| 回放默认上帝视角：四家的牌都摊着 | 71-8，带阳性对照 |
| Live 那一页的默认视角不动，也没有时间轴 | Live 一行没改；喂 `CursorMoved` 进去连世代号都不许变 |

### 3.2 浏览器侧（`verify-home` 从四条加到六条）

**没有新增闸门脚本**（十趟仍是十趟）：时间轴是首页那一屏的东西，而 `verify-home` 就是量那一屏的。
`verify-browser.mjs` 那张表与 `ci-web.sh` 的第八道措辞跟着改准了。

⑤ **默认上帝视角**：四家 `data-hand-hidden` 全 `false`；**再点一下座位 0 视角，至少三家必须 `true`**
——没有后半句的话，「投影恒亮」这种坏法同样能让上一句变绿（票 32 那次就是这么滑过去的）。

⑥ **时间轴真的拖得动**：**在滑块上真点**（`locator.click({position})`，不是设 `value`——
设 `value` 只证明属性写得进去）。四件事：拖到 3/4 处（游标落在 `(0.5, 0.95) × 末帧` 那一带）、
「下一步 → 上一步」走一个来回后 **DOM 摘要逐字相同**（四家的张数 / 牌面 / 河 / 点数
＋「上一手」那句 ＋「第几手・第几局」那句）、拖回最左端是开局那一瞬、点第二局的局号落在那一局的开局帧。

**顺带修了一个会让闸门自己变脆的东西**：`第 3 手・东1局` 与 `第 191 手・南4·2局` 不一样宽，
不钉住的话 flex 里的滑块会在拖动中途伸缩——**手底下的东西会跑**。
`.timeline-at { min-width: 9.5rem }` 钉住它，闸门里也改成每次点之前重新取一次 boundingBox。
这不是为了闸门好过，是那个抖动本身就是 bug（一次性探针在 0.05 处点不中，就是它撞出来的）。

---

## 4. 两组更大的数

量法：一次性 playwright 探针（**没进仓库**，跑完删掉），
`--enable-precise-memory-info --js-flags=--expose-gc`，首屏与拖动量的是 `vite preview`
托管的**打包产物**，fold / decode / 堆量的是 dev server（要点名 `import` 模块）。

### 4.1 三份资产

| 量 | 东风战 Demo（现资产） | 半庄 | 半庄 + 250 条 thinking 记录 |
|---|---|---|---|
| 产出它的命令 | `janpo paifu 3 --opinionated` | `janpo paifu 7 --hanchan --opinionated` | 同左，再拌 250 条 10 KB thinking 的决策记录 |
| 资产字节 | 21,485 | 61,135 | **2,662,523（2.54 MB）** |
| 局数 / 帧数 | 4 / **256** | 10 / **741** | 10 / **741** |
| `Paifu.decoder` | 2.1 ms | 9.2 ms | 9.2 / 9.7 ms（两次） |
| `Table.replay` fold（5 次中位） | **51 ms**（57/52/51/50/48） | **132 ms**（134/132/130/135/128） | **142 / 140 ms**（143/137/142/156/135） |
| 首屏（打开 → `table-board` 出现，5 次中位） | **119 ms** | **221 ms** | **237 / 239 ms** |
| 一次拖动（点下去 → 牌桌重画完，7 次中位） | **31 ms** | **31 ms** | **31 / 32 ms** |
| 留住一份帧多占的 JS 堆 | **+1.2 MB**（23.8 → 25.0） | **+3.2 MB**（28.4 → 31.6） | **+6.1 MB**（38.5 → 44.6） |

基线对得上票 71 报的那几个数（fold 46–74 ms、首屏 123 ms、256 帧），所以这台机器与那次可比。

### 4.2 拖动跟不跟手：**跟得上，而且与帧数无关**

741 帧与 256 帧的一次拖动都是 **31 ms**（7 次里最小 17 ms、最大 38 ms）。
这 31 ms 里包含：range 输入框的事件 → Elmish update（一个整数）→ React 重画整张牌桌。
**帧数不进这条路**（O(1) 取帧），唯一与帧数有关的是 `marksOf` 扫一遍求局边界，
而 256 → 741 帧没有把它推出噪声带。人手拖动的感知阈值一般取 100 ms，**留了三倍余量**。

### 4.3 内存：`Decisions` 的切片是那 2.9 MB 的来处

带 thinking 与不带的差是 **6.1 − 3.2 = 2.9 MB**，而 thinking 的**字节本身只有一份**
（250 条记录里的字符串是共享的，帧只拿引用）。这 2.9 MB 是 `Table.replay` 里
`recordedBy` 给**每一帧**切出的那份 `DecisionRecord list`：741 帧 × 平均 125 条 ≈ 9.3 万个 cons，
每个 cons 在 Fable 里是一个对象。**这就是「帧里那么多份 GameState 会不会把页面吐死」的真答案
——吐不死，而且贵的不是 `GameState`，是决策记录的切片。**

### 4.4 阈值建议（**这一票不动帧的形状**，票面明令）

1. **现在这个量级完全够用**：2.54 MB 资产 + 741 帧，首屏 237 ms、堆 +6.1 MB、拖动 31 ms。
   票 79 换真资产**不需要**先重构帧。
2. **会先撞上的不是内存而是 `HomePageTests` 那条 512 KB 的体积断言**（票 71 §2.3 已经写下这一点）：
   2.54 MB 会当场红。两条路仍是「`Paifu.stripThinking`」或「把预算显式提到那个数」，
   **别默默改预算**。若选后者，首屏从 119 → 237 ms（+118 ms），这是本报告量出来的代价。
3. **真要省内存，先动的是 `recordedBy` 而不是帧**：把「这一帧看得见几条记录」从
   **一份切片**改成**一个计数**（`Decisions` 整份共享 + 每帧存一个 `DecisionCount`），
   就能把那 2.9 MB 变成 741 个整数。这是另一张票，而且要先有更大的数逼它。
4. **拉一条线**：帧数上到 ~3000（三个半庄连打）之前，fold 是线性的（256→741 帧：51→132 ms，
   每帧 0.19 ms 对 0.18 ms），预计 fold ~560 ms、首屏 ~700 ms。**过了那条线该做的是分段 fold，
   不是换帧的形状**——因为拖动本身还是 O(1)。

---

## 5. 截图：我亲眼看到了什么（判据 7）

`docs/images/home.png` 重出（`node scripts/shoot-table.mjs --home`，1088×1006），**我打开看了**。

从上到下：h1 →「这是上帝视角，四家的牌都摊着……拖时间轴回看任意一手，或者点局号跳到那一局的开局。」
→ 蓝色链接「自己开一桌 →」→ **三排控件**：
① 暂停 / 从头再放 / 倍速 1× **2×**(选中) 4× 8×；
② **上一步 / 下一步 / 一根蓝色滑块（滑块头停在最左侧约 1/10 处）/ 右端「第 24 手・东1局」**；
③ 跳到 **东1**(选中) / **东2·1** / 东3 / 东4；
→ 视角排 座位 0/1/2/3/**上帝视角**(选中)/危险度 →「上一手：座位 0 手切白」→ 牌桌。

**四家的手牌全部亮着牌面**：座位 2（对家）13 张、座位 3（上家）10 张 + 一组碰「白」、
座位 1（下家）14 张、座位 0（自家，带「亲」标记）13 张——**一张斜纹牌背都没有**。
这正是 71-8 要的那一眼：复盘看得见四家。

两处值得说：

- **「东2·1」那个局号是真的**：这一场东 1 局流局、亲没听，于是东 2 局带着 1 本场。
  标签里那个 `·1` 就是本场——不带它的话「东2」会出现两次而人分不清（连庄时更明显）。
- **桌心多了一行「里宝牌指示牌 赤5筒」**。这**不是这一票加的**，是 `Viewpoint.God` 那份投影
  本来就有的一格（票 22/25），只是从前首页坐在座位 0 上看不见它。
  对一份已经打完的牌谱它没有泄露问题（71-8 的裁决理由），但**它现在是首页第一眼的一部分**，
  值得主人过一眼（§7 第 2 条）。

---

## 6. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，36.6s（基线 39.8s）；引擎 744 + 页面 **132** 条 |
| 浏览器十趟 | `cd web && node scripts/verify-browser.mjs` | 全 ✓（tracer 0.9s / **home 1.5s** / board 1.1s / golden 0.4s / export×3 / redaction 1.2s / share×2） |
| 首页那一道单跑 | `node scripts/verify-home.mjs` | 六条各印一行 ✓ |
| 每道新断言先红 | §2 十次 | 全部红过，输出抄在 §2 |
| 半庄与内存 | 一次性 playwright 探针（**没进仓库，已删**） | §4 |
| 截图 | `node scripts/shoot-table.mjs --home` | §5，打开看过 |
| 还原干净 | `diff` 对回 `/tmp/t75bak/*` + `jj file show` 对回资产 | `TableState` / `TablePanel` / `demo-paifu.json` 三样全 OK |

`jj diff --stat`：11 个文件。
**没碰**：`web/src/agent/**`、引擎的规则判定与 `Paifu` 格式、`docs/adr/*`、`CONTEXT.md`、
Live 的推进逻辑（`step` / `resume` / `settle` / `onLive` 一字未动）、
`TablePanel.hostControls`（票 72 的地盘）、`TableBoard.fs`、`Playback.fs`。

---

## 7. code-review（Standards + Spec 两轴，fixed point `5d0a64d7`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓** 全程 `jj status` / `jj diff` / `jj commit`，无远端操作、无交互式 flag。
- **工具强制的** `fantomas --check` / `check-style.sh` / Biome / tsc 全绿；引擎 `let mutable` 未新增。
- **F# 风格**（`docs/agents/fsharp-style.md`）：新代码里没有规则 1/2/3 的形状——
  `marksOf` 是 `List.indexed |> List.filter (snd >> isOpening) |> List.map …` 一条数据流（规则 1/2），
  `recordOf` 是 `List.tryLast |> Option.filter …`（规则 3 的正解）；
  `frame |> max 0 |> min last` 从左往右读；数个数用的是 `List.sumBy` 不是 `filter |> length`（规则 5 末段）。
  规则 4.1 的「谓词套取值器」保留（`Option.isNone table.Latest`），正确。
- **注释写「为什么」✓** 每个新类型与出口都写了「为什么是这个形状」与「别写成什么」
  （尤其 `Kyoku` 为什么不拿 `Game.played` 的长度、`Record` 为什么要问手序）。
- **blocking：0。**

### Spec（票面六条行为 + 五条 71-8/性能 + 四条闸门 + 四条边界）

逐条对照见票文件的勾选框。三处值得写下来：

- **「跳到局边界的路（哪种形态你定）」落成了一排局号按钮**，不是「上一局 / 下一局」两枚：
  半庄十局时一排按钮一点就到，而两枚要点九下；连庄用 `·本场` 区分同名局。
- **「那一手有决策记录时把它显示出来」落在回放控制条里而不是牌桌上**：牌桌（`TableBoard`）
  是两种来源共用的那一份，往里塞回放专用的东西会把票 72 的 Live 那一半也拖下水。
- **边界守住了**：Live 模式一行没改（`onLive` 那条路原样）、没做键盘快捷键与动画补间、
  没碰 `Paifu` 格式与 `web/src/agent/**`、牌谱来源仍是首页那份 Demo 资产。

### 记录但没改的 nitpick

1. `TableState.timeline` 每次渲染扫一遍帧求 `Marks`。741 帧实测在噪声带内（§4.2），
   但它是这一票里唯一与帧数成正比的东西。真要省，`Marks` 该在 `DemoLoaded` 那一刻算一次
   存进 `ReplayTable.Ready`——那会给那个 case 加第三格，等有第二个消费方（票 78 的导入）再说。
2. 决策记录那一段用 `String.concat "\n"` 拼「兜底 / 理由 / thinking」三样，靠 CSS 的
   `white-space: pre-wrap` 断行。票 76 要做气泡时这一段整个会被替掉，因此没有为它立结构。
3. `verify-home` 现在有六条断言、一趟点五下，1.3s → 1.5s。仍是十趟里第二快的那几道之一。

---

## 8. 留给人的待审项

1. **README 与 `docs/development.md` 各改了一处措辞**（scope creep，同票 71 的先例，记在 DECISIONS 75-5）：
   README 首页那张图的图注与说明（原文写着「围观视角坐在座位 0」，现在是上帝视角 + 一根时间轴）、
   `development.md` 里 `verify:home` 那行的注释（四条变六条）。**不改就是假的。**
2. **上帝视角把「里宝牌指示牌」摆到了首页第一眼**（§5）。那是 `Viewpoint.God` 本来就有的一格，
   不是这一票加的；对已终局的牌谱没有泄露问题。但它现在是门面的一部分，
   若觉得开局就亮里宝牌太剧透，那是 `Board.ofTable` 那一层的一行改动（**没有在这一票动**）。
3. **票 79 换资产时会先撞体积断言而不是内存**（§4.4 第 2 条）：2.54 MB 的资产
   首屏 237 ms、堆 +6.1 MB，都撑得住；512 KB 那条断言撑不住。
4. **时间轴的滑块没有做键盘快捷键**（票面边界明写不做）。range 输入框本身收方向键，
   因此聚焦之后左右键已经能逐手走——**那是浏览器给的，不是我们做的**，没有断言钉着它。
