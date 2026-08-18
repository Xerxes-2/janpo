# 87 — 真人坐下，把一局打完（M3 的曳光弹）

**结论：真人是第四种选手，引擎与编排层不区分它与 AI——同一条 `Demand`、同一份决策包、
同样只回一个 id。** `./scripts/ci.sh` **EXIT=0**，54.6s；dotnet 748 + **228** 条（原 748 + 216），
浏览器闸门从十四趟变**十五趟**（新增 `verify-human`，**2.2–2.3s**）。

五件事落地：① 配桌上多了第三种选手「我自己」（`SeatChoice.Human` → `SeatPlayer.Human`，
牌谱里那一列写 `human`）；② **点自己手里的一张就打出去**，能点哪几张由**引擎给的合法动作集**
说了算（`data-dahai-id` 就是包内 id），摸切与手切各占一条；③ `humanSeated` 说真话了——
**对局中一个气泡都没有、视角锁死自家、`?dev=1` 的曳光弹不给开**，终局后三样一起松开；
④ 响应阶段一律自动过，**而且页面说得出替你过掉了什么**（票 88 要接的那道缝）；
⑤ 无头脚本坐一席**把一整场东风战打完**，四家点数之和恒为 100000。

**`reveals` / `unlocked` 的规则一个字没改**（票面明令）：改的只有 `humanSeated` 从恒 `false`
变成了真的——票 76 埋那颗桩时写的那句「M3 把真人加进 `Roster` 时改的就是这一个函数，
取值器与视图一行都不必动」**逐字兑现**。

---

## 1. 形状：真人在四条缝上各占一个 case

| 层 | 加了什么 | 它答的问题 |
|---|---|---|
| `SeatingPlan` | `SeatChoice.Human`（wire 名 `human`） | 面板上人拨到了哪儿 |
| `Roster` | `SeatPlayer.Human`；`Roster.humanName = "human"` | 这一席交给谁、牌谱里叫什么 |
| `Table` | `Demand.Human of package: DecisionPackage` | 这一手要问谁 |
| `TableState` | `TableMsg.HumanPlayed of id: int` | 回来的是什么 |

**`Demand.Human` 与 `Demand.Asked` 只差在「问谁」**：一个发一趟跨网请求、一个把包摆在页面上
等一次点击，拿回来的**同样是一个 id**（`DecisionPackage.tryAction`）。于是 spec 的 story 30
（「这些按钮只在动作合法时出现，所以我不可能犯规」）在**结构上**成立：页面构造不出一个 `Action`。

**没有 `config`**：真人没有 provider、没有 key、没有超时（时限是票 89），
`Roster.llmSeats` / `TableState.seatConfigOf` 各多一支返回 `None`。

### 1.1 `HumanSeat`：把一份决策包拆成 UI 要的形状，一条规则都不判

```fsharp
HumanSeat.dahai        : Tile -> bool -> DecisionPackage -> (int * string) option   // 这一张点不点得动
HumanSeat.dahaiOptions : DecisionPackage -> (int * Tile * bool * string) list       // 能点的那几条
HumanSeat.passAction   : DecisionPackage -> Action option                           // 「过」那一条
HumanSeat.unspoken     : DecisionPackage -> string list                             // 这一票还表达不出来的那几条
```

四个出口**全部现问那一份包**，渲染层一条日麻规则都不判（spec 的 UI 决策：合法性驱动 UI）。
直接后果：**立直之后只剩摸切、食替不许打回去，在页面上是「那张牌点不动」的自然结果**
——渲染层从没听说过这两条规则。

**`passAction` 同时是「此刻是哪一种轮到」的判据**（有「过」＝响应阶段，没有＝该他出牌），
因此不必另立一个「阶段」枚举——那就是第二份会漂的判据。依据写在 `Action.None` 的注释里：
「合法动作集里只要出现了 Ron 或 Pon / Chi / Kan，就必须同时有一条『过』」。

### 1.2 「轮到真人」不存在 model 上（判据 9）

第一版把它写成了 `LiveTable.Human: DecisionPackage option`，**当场被自己的用例咬了**：
`?table=1` 一打开，座位 0 就是东 1 局的亲、牌已经在手上，而那一格还是空的——
页面于是说「轮到别人，看着就好」，那是句假话。

改成**现问这一刻的局面**：

```fsharp
let private handOf (live: LiveTable) : DecisionPackage option =
    match SeatingPlan.humanSeats live.Seating |> List.tryHead, live.Table with
    | Some seat, Ok table ->
        Table.pending table
        |> Option.filter (fun choice -> choice.Seat = seat && not (pass choice))
        |> Option.bind (fun _ -> DecisionPackage.forSeat seat table.State)
    | _, _ -> None
```

**两道前置都很便宜，贵的那一步只在真轮到他时才走**：先看引擎待答的头一家是不是他
（`Table.pending` 那份合法动作集 `canAdvance` 每帧本来就在算），再看那一堆动作里有没有「过」
（有＝响应阶段，这一票一律自动过，不该停下来等他）；两道都过了才去搭那份决策包
（`DecisionPackage.forSeat` 要一次从头 fold）。也就是说**它每局只在真人的那 ~18 次出手上各搭一次包**，
而不是每帧一次。

于是「停下来等他」这件事**没有状态**：`waiting` / `step` / 牌桌 / 真人那一行读的都是同一个函数。

### 1.3 驱动循环：真人与模型席在同一条路上

```fsharp
let private waiting (live: LiveTable) : bool =
    not (List.isEmpty live.Awaiting) || Option.isSome (handOf live)
```

`tick` 与 `resume` 读它：**等回执与等那一下点击是同一件事**——定时器不续（否则牌桌在他头上空转），
而把它重新开动的是 `Answered` / `HumanPlayed`。**真人因此与模型席完全同级**：他出完手，
这一桌照旧按播放状态往下走（按过一次「播放」的人不必再按第二次）。

`drain` 与 `step` 各多一支 `Demand.Human`，都落到同一个 `handed`：

```fsharp
let private handed (package: DecisionPackage) (table: Table) (live: LiveTable) : LiveTable =
    match HumanSeat.passAction package with
    | Some action -> {                                  // 响应阶段：替他过，并记下过掉了什么
        live with
            Table = Ok(Table.apply action table)
            AutoPassed = { Turn = table.Turns; Seat = …; Skipped = HumanSeat.unspoken package } :: live.AutoPassed }
    | None -> live                                      // 该他出牌：原样返回，`handOf` 自己会说停
```

**票 88 改的就是这一处**（写在代码注释里）：把上面那一支从「替他提交过」换成
「把 `unspoken` 那几条摆成按钮、跟下面那一支合并」——它要的两样东西（包与中文 label）这里都已在手上。

**真人那一手走 `Table.apply` 而不是 `applyRecorded`**：他没有可审计的推理，与 bot 席同级
——`Paifu` 格式因此一个字段都不必加（票面边界）。

### 1.4 真人只坐得下一席

`SeatingPlan.bind` 拨上第二席时把原来那一席腾空（**刚拨的那一席赢**），
`SeatingPlan.fit` 把从 localStorage 读回来的多余真人席掰回 bot（**留头一席**，那儿什么都可能）。
两处调同一个 `vacated`。理由：本地就一个人一副眼睛，第二席真人没有人操作，
那一桌会停在那儿等一个永远不来的动作；而且「视角锁死自家那一席」当场就没了主语（锁哪一席？）。

---

## 2. 可见性：一条判据，四个消费点

```fsharp
let private unlocked (model) (table) = not (humanSeated model) || Table.result table |> Option.isSome  // 一个字没改
let private lockedTo (model) (table)  = if unlocked model table then None else humanSeat model         // 它的反面
let lockedSeat (model) = match shown model with Shown.Board table -> lockedTo model table | _ -> None
let viewpoint (model)  = match lockedSeat model with Some seat -> Viewpoint.Seated seat | None -> model.Viewpoint
```

`lockedTo` **直接读 `unlocked`**：气泡藏不藏、视角锁不锁、曳光弹给不给开，本来就是同一件事
——「桌边坐着一个人，而这一场还没打完」。各写一份就是三处判据，而三处判据迟早漂到
「气泡藏了、上帝视角还开着」那一步。

| 消费点 | 读什么 | 锁着时是什么 |
|---|---|---|
| 气泡 `TableState.bubbles` | `unlocked`（票 76/81 原样） | `None` → **DOM 上没有那个元素** |
| 牌桌投影 `TableBoard.tableBody` | `TableState.viewpoint` | `Viewpoint.Seated 自家` → 他家是 `MaskedSeat`（**类型里就没有手牌**） |
| 视角那一排 `TablePanel.viewpoints` | `TableState.lockedSeat` | 只画自家那一枚 + 一句为什么 |
| 曳光弹 `TablePage.devSurface` | `TableState.devSurfaceAllowed` | 整块不画 |

**两道锁，不是一道礼貌**：按钮不在 DOM 里是一道（灰掉一行 DevTools 就平了），
`viewpoint` 是另一道——就算有人发一条 `ViewpointPicked God` 进来，牌桌也不换投影
（dotnet 用例 `对局中视角锁死自家：上帝视角与别席视角连值都给不出来` 逐条钉着）。
下面 §4 的红-1 正好只按掉了后一道，前一道纹丝不动——两道确实是独立的。

**`reveals` 只改了一个词**：`model.Viewpoint` → `viewpoint model`。规则本身（上帝全开、
坐座只看自家）一个字没动。副作用是**真人也看不见别席的状态线**（票 81 把状态线接进了同一条规则），
这正确——但它把闸门的阳性对照逼上了另一条路，见 §5.2。

### 2.1 堵掉 22-A：曳光弹从 `Main.Shell` 搬进了 `TablePage.Page`

挂账 22-A（`?dev=1` 那页把带着四家配牌的 `start_kyoku` 印在同一张文档里）从 M1 记到现在，
**受害者今天才出现**。堵法是让它问一句「这一桌允不允许」，而那句话只有牌桌的 model 答得出。

从前的判据写在 `Main.Shell` 里，那一层没有 model。**读 localStorage 不行**（判据 2 的反面）：
那是第二份判据，而且人在面板上刚把自己摆上座位时 `Shell` 根本不重画——那正是要堵的那条缝。
于是 `Page` 交出一个 **fragment**（`page :: devSurface model`），`Shell` 变成纯外壳。
**fragment 不生成 DOM 节点**，`div.shell` 下那几个孩子与从前逐个相同——`verify-tracer` 一条断言不必改，
实测那一趟 1.1s 全绿。

---

## 3. 「点自己手里的一张就打出去」长什么样

`TableBoard.handTiles` 收一个 `play : Tile -> bool -> (int * string) option`（不是真人席时恒 `None`），
给得出 id 的那几张渲成 `<button class="tile playable" data-dahai-id="N">`。

- **摸切与手切在这里分岔**：牌桌本来就把刚摸那张拎出来摆开（票 44），
  点它是 `tsumogiri = true`、点手里那几张是 `false`——**两样东西不必各做一份 UI**。
- **`button` 而不是加了 onClick 的 `span`**：键盘走得到、读屏念得出、`:focus-visible` 那圈靛青自然就有。
- **一张牌上的 id 是引擎给的**：14 张牌上只有 10 个不同的 id（手里两张 8 索共用一条「手切8索」）。

实测那一屏（`/tmp/t87shots/87-hand.png`，**打开看过**）：13 张靛青描边、
**赤5筒那一张仍是朱红**——见下。

### 3.1 顺手治好的一处配色冲突（截图看出来的）

第一版 `.tile.playable { border-color: var(--indigo) }` 排在 `.tile.aka` 后面、同为
`(0,2,0)` 特异度，于是**轮到你出牌时赤 5 那一圈朱红被吃掉了**（票 80 把它当成「隔一张桌子
也分得开」的记号，`verify-board` 也在量它）。那一道闸门不会红——它跑的是没有真人的一桌，
`playable` 根本不出现。**是打开图看出来的**（判据 7）。

治法一行：`.tile.aka.playable { border-color: var(--vermilion) }`，并写清判据——
**这一张是不是赤牌是牌面的事实，点不点得动只是此刻的状态；撞在同一根轴上时前者赢**。
赤牌那一张仍旧点得动（`cursor` 与悬停底色还在说话），只是不拿牌框说。
computed style 实测：13 张 `rgb(47,75,110)`（靛青）+ 赤5筒 `rgb(180,58,44)`（朱红）。

---

## 4. 那条不泄露断言红的时候长什么样（判据 1，票面点名）

**做法是改产品代码**（不是改断言）：把 `TableState.viewpoint` 换回上帝视角
（`let viewpoint (model) = model.Viewpoint`——`?table=1` 默认就是上帝视角，票 82 定的），
重编 Fable，跑同一条命令。

```
$ cd web && node scripts/verify-human.mjs

整页 HTML 里有 88 个 data-pai，观测者本来就看得见的有 57 个
自家手牌行：13 张，露着 13 张、扣着 0 张
座位 1 的手牌行：7 张，露着 7 张、扣着 0 张
座位 2 的手牌行：10 张，露着 10 张、扣着 0 张
座位 3 的手牌行：14 张，露着 14 张、扣着 0 张

真人坐席这一道没过：
对局中整页 HTML 里多出 31 个他不该看得见的 data-pai：1m、1p、1z、2m、2p、2z、3p、3s、3z、3z、3z、4z、
5mr、5sr、5z、5z、6m、6p、6p、6s、7m、7p、7p、7s、7s、7z、8m、8s、8s、9m、9s
——他家的手牌一张都不许在里面，连 data-* 都不许有（spec 的 story 29）
座位 1 的手牌在页面上露了 7 张（data-hand-hidden=false）：他家的暗牌在投影里根本不该存在（`MaskedSeat` 没有手牌字段）
座位 1 说有 7 张手牌，却画了 0 张牌背
座位 2 的手牌在页面上露了 10 张（data-hand-hidden=false）：…
座位 2 说有 10 张手牌，却画了 0 张牌背
座位 3 的手牌在页面上露了 14 张（data-hand-hidden=false）：…
座位 3 说有 14 张手牌，却画了 0 张牌背
坐在座位 0 上，Agent 那一行却写着座位 1 的理由：「座位 1 的模型选完了（3 ms）：假端点说：这一手照它的算法只能这么打」
——气泡拦住了而状态线漏了，那闸门就只是个摆设（票 81）
```

**改回去当场绿**（`整页 HTML 里有 59 个 data-pai，观测者本来就看得见的有 59 个`），
`jj diff` 逐文件对回备份，五个文件全 OK。

两件顺带看清的事：

1. **视角按钮那一条纹丝不动**（`lockedSeat` 没被动过）——两道锁真的是独立的两道；
2. **气泡那一条也纹丝不动**（`unlocked` 与视角正交，票 81 定的）——这次泄的是手牌不是推理，
   而闸门把这两件事分开报了。

### 4.1 断言的形状（为什么这样量才算数）

```js
const html = await page.content();
const body = html.replace(/<style[\s\S]*?<\/style>/g, "");     // 见下
const inDocument = [...body.matchAll(/data-pai="([^"]*)"/g)].map(…).sort();
const budget = /* 自家手牌 + 四家的河 + 四家的副露 + 宝牌指示牌 */;
extras(inDocument, budget)   // 多重集减法：多出来的每一张都报出来
```

**量的是整页序列化文档，不是几个选择器捞出来的那几处**——「他家的手牌一张都不在里面」
这句话只有对整页说才算数。多出来的那几张逐张印出来，因此红的时候一眼看得出泄的是什么。

**`<style>` 要先挡掉，而这不是放宽**：`styles.css` 里每一张牌面都有一条
`.tile[data-pai="1m"] { background-image: … }`（牌面 SVG 就是按它贴的），
而 vite 的 dev server 把 CSS 内联成 `<style>`。第一次跑这一条**恒红 39 行**，
逐张对下来正好是「每种牌各一张 + `5z` 三次」——那是样式表不是牌（判据 6:
新断言第一次大面积报红，先验算自己的式子）。挡掉之后同一份页面 57 对 57。

---

## 5. 闸门：谁在守什么

### 5.1 dotnet 侧（`tests/Janpo.Web.Tests/HumanSeatTests.fs`，**12 条**；216 → 228）

| 用例 | 钉的是 |
|---|---|
| 真人是第四种选手：配桌里是 Human，牌谱里那一列写 human，与 bot 和模型都分得开 | `names = [human; random; random; random]`；`human` 里没有斜杠（模型席恒有）、与两种 bot 名都不同 |
| 真人只坐得下一席：坐上第二席时，原来那一席退回均匀随机 | **刚拨的那一席赢**（`bind`）；localStorage 里两席都写着 `human` 时由 `fit` 留头一席 |
| **能点哪几张由引擎给的合法动作集定：多一张少一张都不许** | 与**引擎的 `Table.pendings`**（第三个锚点，不是拿包对包）逐条相同；再对全部 34 种牌 × 摸切/手切**双向**核一遍；每条 id 都换得回那条 `Action.Dahai` |
| 摸切与手切各占一条：点哪一条，河上那一格就写着哪一种 | 摸切恰一条且是刚摸那张；点下去之后 `KawaEntry.Tsumogiri` 两头各对一次 |
| 点一条不在这一包里的 id：没有事情发生 | `9999` 与 `-1`：手数不动、那一份包还在（**不放宽合法性**） |
| 真人在想的时候整桌等着：单步与定时器都推不动，他点一下才走 | `Advanced` / `Ticked` 手数不动；**`PlayToggled` 一个效果体都不发**（阳性对照：四家 bot 那一桌必须真发一记定时器） |
| 真人那一手不留决策记录：他与 bot 在牌谱里同级 | `Decisions` 空、兜底 0 |
| **响应阶段一律自动过，且记得住过掉了什么** | 打完一整场：`AutoPassed` 非空（防空转）、每条 `Skipped` 非空、里面没有「过」自己 |
| **真人坐一席，把一整场东风战打完：终局点数四家和为定值** | 走到 `Table.result`，四家点数和 = 100000，兜底 0 |
| 有真人在座：对局中一个气泡都没有，终局后四家的都回来 | **同一份决策记录**在没有真人的那一桌上气泡该在（阳性对照）；真人在座时四席全 `None`；终局后回来 |
| 对局中视角锁死自家：上帝视角与别席视角连值都给不出来，终局后松开 | `viewpoint` 恒 `Seated 真人席`，`ViewpointPicked God/别席` 都改不动；没有真人那一桌照旧（阳性对照）；终局后松开 |
| 真人在座时曳光弹不给开（22-A），没有真人时照旧开得了 | 四家 bot / 首页回放 / 终局后三种都 `true`，真人在座且未终局 `false` |

`SeatingPlanTests` 动了一行（`| SeatPlayer.Bot _` 的 match 加 `| SeatPlayer.Human`），**断言一条没改**。

### 5.2 浏览器侧（第十五趟 `verify-human.mjs`，2.2–2.3s；**一个字节都不出网**）

真人坐座位 0、座位 1 交给一个本地假端点（回一句只可能从它那儿来的话）、座位 2/3 是自带 bot。
**整段驱动跑在页面内**（有牌点得动就点，没有就按「单步」；票 56 那条教训），
一整场东风战 88 次点击 + 356 次单步一次 `evaluate` 走完。

九条：① 视角按钮不在 DOM 里（上帝 0 枚、别席 0 枚、自家 1 枚 + 一句为什么）；
② 点得到的**不同动作条数** = `data-human-playable`，且**点下去的就是打出去的那一张**
（页面上那张牌 → 包内 id → 引擎落定 → 自家河末尾那一张，整条链走完）；
③ **整页 HTML 不泄他家一张手牌**（§4）+ 他家三席手牌行 0 个 `data-pai` / `data-hand-hidden=true` /
牌背数对得上 + 桌心没有里宝牌；④ 对局中 0 个气泡；⑤ 一整场替他过了 27 次且页面说得出
过掉的是「碰南（亮南 南）」这种；⑥ 终局点数和 100000；⑦ 终局后视角五枚回来、那句「锁着」没了、
座位 1 的气泡回来**且里面就是它当时说的那句**；⑧⑨ `?dev=1` 的阴阳对照。

**④ 的阳性对照走的是 token 账单而不是状态线**：真人坐在座位 0 上，
而**视角同样拦着状态线**（票 81：气泡与状态线同一条规则），他本来就不该看见座位 1 说了什么
——闸门反过来核「状态线里不许出现那句话」。而 `table-usage` 的 `data-prompt-tokens`
不按视角变，它 > 0（实测 8954 tok）就证明那一席模型**真的被问过话**，
于是「0 个气泡」量的不是一桌没人开口的空局。**第二道阳性对照在 ⑦**：终局之后那句话真的放出来了。

**⑤ 量在打完之后**（判据 3）：开局头几十手可能一次鸣牌机会都没碰上，那时量它就是一条
执行不到的断言；一整场下来实测稳定在 16–27 次。

**执行次数**：这一趟每跑一次执行 1 整场（5 局、444 步）、1 次整页快照、4 席手牌形态、
27 次自动过、2 页 `?dev=1` 对照。没有一条是构造不出来的支路。

### 5.3 顺手治好的一处「闸门自己会把十五趟一起搞挂」

破坏实验（把 `humanSeated` 按回恒 `false`）时闸门**没红，而是抛了个 `TimeoutError`**：
`getByTestId("table-view-locked").getAttribute(...)` 在元素不存在时**干等 30 秒再抛**，
而这一趟的契约是交一份失败清单（合并跑的入口要先关浏览器再逐道汇报）。
`verify-bubbles` / `verify-home` 早就各写下过同一课。改法是两个小助手
（`attr` / `text`，走 `page.evaluate`，没有就是 `null`）+ 一条早退。
改完那次破坏实验红出 15 条（`/tmp` 里那份原文的头两条）：

```
真人在座、这一场还没打完，「上帝视角」那一枚却还在 DOM 里：灰掉不算数——票 81 把视角定成了信息闸门…
别席（座位 1 / 2 / 3）的视角按钮还在 DOM 里（1 枚）
页面没说清视角锁在哪一席（data-view-locked=「null」，该是 0）…
有真人在座、对局还没打完，页面上却有 1 个思考气泡：AI 的推理会向同桌的真人泄露它的手牌（spec 的 story 31）
真人在座时 ?dev=1 还是把曳光弹挂了出来（seed-input 1 个、traces 1 个）…挂账 22-A 说的就是它
```

---

## 6. 每条新断言先红一次（判据 1 的原始输出）

**六次，全部实跑**：改**产品代码**，跑同一条命令，抄红的原文，再 `diff` 对回 `/tmp/t87bak/`。

**红-1｜投影换回上帝视角**（`viewpoint` 直接返回 `model.Viewpoint`）→ §4 那一整段。

**红-2｜视角按钮照旧全画**（`viewpoints` 不看 `lockedSeat`）

```
真人坐席这一道没过：
真人在座、这一场还没打完，「上帝视角」那一枚却还在 DOM 里：灰掉不算数——票 81 把视角定成了信息闸门，一行 DevTools 就能把 disabled 平掉
别席（座位 1）的视角按钮还在 DOM 里（1 枚）
别席（座位 2）的视角按钮还在 DOM 里（1 枚）
别席（座位 3）的视角按钮还在 DOM 里（1 枚）
```

**红-3｜`humanSeated` 退回票 76 那一版（恒 `false`）**

```
（dotnet）
HumanSeatTests.真人在座时曳光弹不给开（22-A），没有真人时照旧开得了 [FAIL]
HumanSeatTests.有真人在座：对局中一个气泡都没有，终局后四家的都回来 [FAIL]
HumanSeatTests.对局中视角锁死自家：上帝视角与别席视角连值都给不出来，终局后松开 [FAIL]
失败: 3，通过: 225，总计: 228
（verify-human）§5.3 那 15 条
```

**红-4｜自动过了却不记账**（`handed` 不追加 `AutoPassed`）

```
（dotnet）
HumanSeatTests.响应阶段一律自动过，且记得住过掉了什么（票 88 换成真按钮时读的就是它） [FAIL]
  Assert.NotEmpty() Failure: Collection was empty

（verify-human）
这一场替他自动过了 0 次：「你坐在座位 0：轮到别人，看着就好。」
真人坐席这一道没过：
一整场下来一次鸣牌都没替他过（data-human-passes=0）：要么自动过根本没发生，要么它发生了却没记账
——两种都是票 88 要接的那道缝断了
```

**这一条是被自己按红的过程改硬的**（判据 3）：第一版写的是
`if (passes > 0 && !said.includes("替你过了"))`——`passes = 0` 时它**整条不执行**，
于是红-4 在浏览器侧全绿。改成「一整场下来必须 > 0」并挪到打完之后才咬得住。

**红-5｜能点哪几张由 UI 自己判**（`playAt` 不问包，轮到他就把每张都画成点得动）

```
真人坐席这一道没过：
页面上点得到 1 条不同的打牌动作，而合法动作集说该有 10 条：能点哪几张只许由引擎的合法动作集定（spec：合法性驱动 UI）
```

**这一条也是第二次才咬住的**：第一版的改坏法是「手切给不出 id 就退回摸切那条」——
开局那一手两条都合法，**闸门全绿**。于是补了 §5.2 第②条后半句
（**点下去的就是打出去的那一张**：牌 → id → 引擎 → 河末尾），再把改坏法换成真正的
「UI 自己判」（恒给 id 0）才红。

**红-6｜曳光弹不问这一桌允不允许**（22-A 那一版）

```
真人坐席这一道没过：
真人在座时 ?dev=1 还是把曳光弹挂了出来（seed-input 1 个、traces 1 个）：那一块把 start_kyoku（带着四家配牌）
印在同一张文档里，挂账 22-A 说的就是它
```

**这一条差点变成一次假绿**（判据 16 的同族）：头一次改坏之后 `pnpm run fable`
**编译失败**（`error FS1182: The value 'model' is unused`），而我没看它的退出码就去跑闸门
——跑的是**上一版的 JS**，于是「全绿」。第二次把改坏法写成不触发那条警告的形状才真红。
**教训：破坏实验之前先确认那一版真的编出来了。**

**红-7｜`waiting` 不看真人**（定时器照续）

```
HumanSeatTests.真人在想的时候整桌等着：单步与定时器都推不动，他点一下才走 [FAIL]
  Assert.Equal() Failure: Values differ
```

**这一条是先绿后改硬的**：原来那条用例只断言「手数不动」，而手数不动是 `handed` 那一支
保证的，不是 `waiting`——按掉 `waiting` 全绿。补上「`PlayToggled` 一个效果体都不发」
（`Cmd` 就是一串效果体，`List.length` 数得出来）**并配一条阳性对照**（四家 bot 那一桌必须真发一记）
才咬得住。

**红-8｜真人坐得下两席**（`bind` 与 `fit` 都不掰）

```
HumanSeatTests.真人只坐得下一席：坐上第二席时，原来那一席退回均匀随机 [FAIL]
  Assert.Equal() Failure: Collections differ
```

---

## 7. 截图：我亲眼看到了什么（判据 7）

三张，**都自己打开看过**（一次性探针，跑完删了；图在 `/tmp/t87shots/`，没进仓库）。

### 7.1 轮到你出牌那一屏（`87-human-turn.png`，1280×1440）

- 配桌那一块：座位 0 那一行上「**我自己**」按下去（靛青），另外三行是「均匀随机」。
- **视角那一排只有两枚按钮**：`座位 0`（按下去的）与`危险度`，后面跟着一句
  「视角锁在座位 0（你自己）：桌边坐着真人，上帝视角与别席视角在这一页上不存在——终局后它们回来。」
  ——「上帝视角」那一枚**根本不在那儿**。
- 真人那一行：「轮到你出牌了（座位 0）：点自己手里的一张就打出去，能点的那几张由引擎给的
  合法动作集定（此刻 10 条）。不限时，整桌等着你。」
- 牌桌：自家（下方）14 张牌面朝上、刚摸那张空开一格；**另外三家整排蓝背**，
  一张牌面都没有；桌心只有「场况 / 供托 / 剩余摸牌 / 宝牌指示牌」，**没有里宝牌那一行**。

### 7.2 自家那一排牌（`87-hand.png`，3× 缩放）

14 张全带靛青细框（点得动），**最右那张赤5筒是朱红框**（§3.1 治好的那一处）。
牌面 SVG 照旧（六萬 六萬 七萬 九筒 三索 七索 八索 八索 東 南 南 中 中 赤5筒）。

### 7.3 终局那一屏（`87-settled.png`）

视角那一排回到五枚 + 危险度（「上帝视角」按着），**那句「视角锁着」没了**；
真人那一行变成「这一场打完了（你坐的是座位 0）：视角与思考气泡都解锁了，四家的牌与推理现在都看得了。」
后面接着「鸣牌一律自动过：第 341 手替你过了：碰南（亮南 南）（这一桌共 22 次）。」

**终局那一句是看图看出来要加的**：原来三态只有两态，终局时它写着「轮到别人，看着就好」
——那时谁的回合都不是，而且那一刻正是三样一起松开的时刻，不说的话人不知道刚才藏着的现在看得了。
闸门跟着加了一条（`data-human` 必须是 `settled`）。

---

## 8. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，53.2s / 54.6s；引擎 748 + 页面 **228** 条；浏览器**十五趟**全 ✓ |
| 新那一趟单跑 | `cd web && node scripts/verify-human.mjs` | 2.2–2.3s，见 §5.2 |
| 每条新断言先红 | §6 的八次 | 全部红过，原文抄在 §6 |
| 真人把一整场打完（引擎侧探针） | `dotnet fsi /tmp/probe87.fsx`（**没进仓库，跑完删了**） | 五颗种子各 360–365 步 + 72–75 次点击，全部走到终局、点数和 100000、替他过 16–22 次 |
| 截图 | 一次性 playwright 探针（**没进仓库**） | §7，三张都打开看过 |
| 还原干净 | 逐文件 `diff` 对回 `/tmp/t87bak/` | `TableState` / `TablePanel` / `TableBoard` / `TablePage` / `SeatingPlan` 五样全 OK |

`jj diff --stat`：20 个文件，+1904 / −99。**新增三个文件**
（`src/Janpo.Web/HumanSeat.fs`、`src/Janpo.Web/HumanLine.fs`、`web/scripts/verify-human.mjs`）
与一个用例文件（`tests/Janpo.Web.Tests/HumanSeatTests.fs`）。

**没碰**：`src/Janpo.Engine/**`（引擎一字未动）、`web/src/agent/**`（票 95 的地盘）、
`probe/`（票 91 的）、`flake.nix`、`docs/adr/*`、`CONTEXT.md`、`Paifu` 格式、
`web/public/demo-paifu.json`、`Route`、`Share`、回放那一半、`reveals` / `unlocked` 的规则、
`Bubble` 的三态与取值器、`.github/workflows/`。**没有新增任何对 `render_version` 值的断言**（票 95 的地盘）。

**动了但不在票面那张清单上的两处**（都是「新增一趟闸门」绕不开的登记处，写在这里备查）：
`web/scripts/verify-browser.mjs`（那张十五趟的表 + 一个 import）与 `web/package.json`
（多一行 `verify:human`）。`scripts/ci.sh` 本身一个字没改，改的是它调的 `scripts/ci-web.sh`
的**注释与那两句抬头文案**（十四趟 → 十五趟）。

---

## 9. code-review（Standards + Spec 两轴，fixed point `struwmpn` / `698cdf25`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓**：全程 `jj st` / `jj diff` / `jj log` / `jj commit`，无远端操作、无交互式 flag。
- **工具强制的**：`fantomas --check` / `scripts/check-style.sh` / Biome / tsc 全绿；
  `let mutable` 一处未新增（第一版的用例驱动循环用了三个 `mutable`，自查时改成 `let rec`）。
- **F# 风格**（`docs/agents/fsharp-style.md`）：
  - 规则 1/3：新代码里没有从里往外读的嵌套——`DecisionPackage.options package |> List.tryFind … |> Option.map …`、
    `Table.pending table |> Option.filter … |> Option.bind …`、`seats |> List.mapi …` 都是从左往右的数据流。
  - 规则 2：`turn |> Option.map (HumanSeat.dahaiOptions >> List.length)` 是正例；
    `fun pai giri -> HumanSeat.dahai pai giri package` 捕获了 `package`，属明文例外。
  - 规则 4.1 的「谓词套取值器」保留（`Option.isSome (handOf live)`、`Option.isNone (Table.result …)`）。
  - 规则 5：没有新 `let mutable`。
- **注释写「为什么」✓**：为什么「轮到谁」不存状态、两道前置为什么便宜而第三步为什么贵、
  票 88 该改哪一处、真人那一手为什么不留决策记录、为什么只坐得下一席、
  赤牌那圈朱红为什么压过靛青、`attr`/`text` 两个助手为什么不用 `getByTestId(...).getAttribute`
  ——都写在代码上。
- **术语 ✓**：`HumanSeat` / `SeatChoice.Human` / `SeatPlayer.Human` 用的是 `CONTEXT.md` 的
  `Human Seat` 词条；`AutoPass` / `playable` 是渲染层的名字，日麻术语一个没自造。
  **`CONTEXT.md` 一字未改**（硬约束 5：没有授权）——提案见 §10 第 1 条与 `DECISIONS.md`。
- **ADR-0003 ✓**：`unlocked` 一字未动；新那一根（`lockedTo`）读的正是它，
  因此判据仍旧挂在**对局配置与终局状态**上，不挂在「用户是谁」上。`docs/adr/*` 未改。
- **ADR-0005 ✓**：TS 一行没碰，跨界回来的仍旧只有一个 id。
- **blocking：0。**

### Spec（票面 6 条行为 + 5 条闸门 + 6 条边界）

逐条对照见票文件的勾选框。四处值得写下来：

- **「真人在想的时候整桌等着」落成了与模型席同一条规则**（`waiting`），而不是给真人另开一个
  「暂停」——因此他出完手，这一桌照旧按播放状态往下走。这也意味着**页面默认暂停时，
  真人点完一张之后还要按「单步 / 播放」别家才动**（`?table=1` 从票 71 起就是这样）。
  留给人裁的口味问题见 §10 第 2 条。
- **「响应阶段一律自动过」的接缝落在 `TableState.handed` 一处**，而 `AutoPass.Skipped`
  已经是引擎给的中文 label——票 88 拿它直接摆按钮，一个字都不必翻。
- **「视角按钮不在 DOM 里」做成了两道锁**（§2），因为票 81 已经把视角定成信息闸门，
  而一道只在 DOM 上的锁不是闸门。
- **`?dev=1` 那一条顺带把 `Main.Shell` 的形状改了**（fragment），理由与代价见 §2.1；
  渲染出来的 DOM 逐个节点相同，`verify-tracer` 一条断言没动。

### 记录但没改的 nitpick

1. **`danger` 那枚开关在真人锁着时仍旧拨得动**（`TableBoard.dangerSeats` 把它限死在观测者
   自家那一手上，因此不泄露）。它是不是「信息辅助」、该不该在真人对局里默认关掉，
   是票 89 的地盘，这一票一行没动。
2. **`AutoPassed` 跨局累计、只在重开一桌时清**（与 `Table.fallbacks` 同一个做法）。
   一整场东风战实测 16–27 条，内存不成问题；真要按局分开就得决定页面上那句话说的是哪一局。
3. **`verify-setup.mjs` 的文件头仍写着「十一趟共用一个浏览器」**（现在十五趟）。
   那是票 72 的文件，票 76 与 81 的报告各记过一次，这一票同样没碰——留给集成时顺手统一。
4. **`HumanLine` 与 `AgentLine` 各自读一遍 `TableState`**（一个读 `humanTurn`/`autoPasses`，
   一个收 `reveals` 谓词）。两行都只画一句话，没有共用的必要；真要合并得先决定
   「真人那一行属不属于 Agent 那一块」——它不属于。

---

## 10. 留给人的待审项

1. **`CONTEXT.md` 的 `Human Seat` 词条今天只有一句话**（「由本地真人操作的 Player 实现，
   其决策函数渲染动作输入 UI 并等待交互」）。这一票落地之后它至少还有三条不变量有了执行者，
   **但改术语表要单票授权，因此我一个字没写**，提案记在 `DECISIONS.md` 87-1：
   ①「一桌只坐得下一席真人」（执行者 `SeatingPlan.soloHuman`）；
   ②「牌谱里那一列恒是 `human`，里面没有任何私人信息」（执行者 `Roster.humanName`）；
   ③「有真人在座且未终局时，页面锁在他那一席上——上帝视角与别席视角连值都给不出来」
   （执行者 `TableState.viewpoint`）。请主人裁要不要补进那一条词条。
2. **真人点完一张之后，别家要等下一记定时器**（或者他自己按「单步」）。
   按过一次「播放」的人不受影响（`resume` 会把定时器接上），但**默认暂停的那一页上
   第一次点完手牌会觉得「怎么没动」**。做法上有两个选择：让 `HumanPlayed` 无条件推一步，
   或者让真人在座时 `?table=1` 默认开着播放。两者都动的是既有的播放语义（票 71/83 的地盘），
   **这一票没有动**——它是产品口味，请主人裁。
3. **真人席在牌谱里叫 `human`**（`Roster.humanName`）。票 88 / 90 / 93 都要读它。
   要更亲切（例如让人自己填一个昵称）就要先回答「昵称算不算私人信息、上不上可分享物」
   ——今天的答案是**不上**，因此写死一个词。
4. **一整场东风战真人要点 72–75 次**（探针实测五颗种子）。这个数字对「亲自下场」的体验没问题，
   但对**闸门**是个成本：新那一趟 2.2s 里的大头就是这 444 步。以后要在真人这条路上
   再加闸门，先想清楚要不要每一趟都打满一整场。
