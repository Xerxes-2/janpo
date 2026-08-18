# 71 — 首页第一眼是一桌牌在走：Live / Replay 接缝、`?table=1`、Demo Paifu

**结论：`/` 从此是一局录像在自动播，`?table=1` 是今天那一页一字不少。**
接缝是**一页一 Model + 一个联合类型**（`Source = Live of LiveTable | Replay of ReplayTable`），
播放、视角、危险度与**牌桌和结算的整套渲染**留在联合之外——回放那一侧一行渲染代码都没有新写。
`./scripts/ci.sh` **EXIT=0**，39.8s（基线 42.5s）；dotnet 744 + 121 条，浏览器闸门**十趟**全 ✓。

**回放不是第二条路**：逐帧的牌桌是 `Table.apply` 一手一手落出来的，与 Live 逐字同一条落子路径。
役种在**宣言那一刻**捞下来、掩蔽流跟着引擎吐的事件长、上一手写在 `Latest` 上——三样都没有第二份实现。
引擎那边只多了**一项输出**（`Replay.trace`：fold 本来就在逐条提交动作，从前提完就丢）。

---

## 1. 接缝的形状

### 1.1 联合长什么样（`src/Janpo.Web/TableState.fs`）

```fsharp
type TableModel = {
    Ruleset: Ruleset          // Live 取默认预设；回放取**牌谱自带的那一份**（ADR-0004）
    Source: Source            // 牌从哪来 —— 唯一分岔的一格
    Playback: Playback        // ↓ 这四格两种来源共用，没有第二套
    Viewpoint: Viewpoint
    ShowDanger: bool
}

[<RequireQualifiedAccess>]
type Source =
    | Live of live: LiveTable          // `?table=1`
    | Replay of replay: ReplayTable    // `/`

type LiveTable = {                     // 只属于主持人那一页
    SeedText: string; Table: Result<Table, string>
    LlmAt: Seat option; Bot: Bot; Llm: LlmSeat; Pinned: Rendering option
    Awaiting: Awaiting option; Ticket: int; Agent: AgentStatus
}

[<RequireQualifiedAccess>]
type ReplayTable =                     // 三段各一个 case，不是「记录 + 一堆 option」
    | Loading                          // 还在 fetch 那份牌谱
    | Failed of reason: string         // 拉不动 / 读不动 / 回放不动：一句中文。**不许白屏**
    | Ready of frames: Table list * cursor: int
```

**哪些留在联合外面、为什么**：

| 留在外面 | 理由 |
|---|---|
| `Playback` | 「一手多久」与「牌从哪来」正交。写成两套之后「暂停 → 立刻再播」那个世代号的坑要修两遍 |
| `Viewpoint` / `ShowDanger` | 票面明写「回放里视角切换照旧」。它俩读的是 `Board.ofTable` / `Danger`，两种来源同一份投影 |
| `Ruleset` | 视角那一排要 `Seat.all ruleset`。它两种来源都有，只是**取处不同**（回放取牌谱的） |
| 牌桌与结算的整套渲染 | `TableBoard.tableBody` 收的是 `Table`，而回放的每一帧**就是一个 `Table`** |

**分岔只落在三处**：`TablePage.homePage` / `hostPage` 两个布局、`TablePanel.controls` 摆哪几个按钮、
`TablePanel.viewpoints` 要不要那个种子框。三处都是「摆哪几个控件」，没有第二份逻辑。

### 1.2 `Shown`：两种来源共用的那一个出口

```fsharp
[<RequireQualifiedAccess>]
type Shown = Loading | Fault of reason: string | Board of table: Table

TableState.shown : TableModel -> Shown
```

`TablePage.board` 只认它，因此**画牌桌那一段代码不知道自己在画 Live 还是回放**。
`Loading` 只可能出现在首页；`Fault` 两边都有（Live 是「开不了局」，回放是那三种失法）。

### 1.3 回放的帧从哪来（`Table.replay`）

```fsharp
Replay.trace       : Ruleset -> Event list -> Result<ReplayKyoku list, ReplayError>   // 引擎，新增
Replay.traceOfPaifu: Paifu -> Result<ReplayKyoku list, ReplayError>
type ReplayKyoku = { Opening: GameState; Actions: Action list }

Table.replay : Paifu -> Result<Table list, string>                                    // Web，新增
```

**引擎只加了一项输出**：`Driving` 多一格 `Played`（倒序累加，出口 `List.rev`），
`Replay.game` 与 `Replay.trace` 共用同一段 `folded`——一份事件流只有一条 fold 路径，
两个出口因此不可能对不上，**报错也必然报在同一处**（`ReplayTests` 有一条钉着这件事）。
`Replay.fs` 一条规则都没自己判。

**`Table.replay` 把动作交回 `Table.apply`**，也就是 Live 每推一手走的那一条：

- **役种**：`Table.apply` 在提交 `Hora` 之前先 `GameState.horaOf`（一局终了之后再问就是 `NoAgariShape`）；
- **掩蔽流**：`Table.apply` 把引擎吐出来的事件接进四条 `SeatStream`；
- **上一手 / 手序 / 局的收拢**：`Latest` / `Turns` / `Game.advance` 全在同一处。

于是「回放的结算面板有役与符番」「回放里座位视角切得动」两条**不是回放这一侧新写的**，
是它复用了 Live 那一份。票面把这两件事写成「`Replay` fold 的时候捞下来」，
落地时判断是**别在引擎里再长一份**（裁决 71-2，见 `DECISIONS.md`）。

**一帧就是一手**：帧数 = 落定的手数 + 局数（每开一局多一帧开局）。
`Action.None`（过）也占一帧——Live 那边同样占，两边的「一手」是同一个粒度。

**决策记录按手序切到帧上**（`record.Turn < table.Turns`）：第 0 帧看不到末手的思考。
Demo 是 bot 牌谱（0 条记录），这一格是给票 76/79 留的口子，`ReplayTableTests` 拌了一条进去做阳性对照。

### 1.4 地址怎么解析（`src/Janpo.Web/Route.fs`）

```fsharp
type Landing = Home | Table
Route.landing              : unit -> Landing   // query 里有 `table=1` 就是 Table，否则 Home
Route.devSurfaceRequested  : unit -> bool      // `dev=1`（票 35 原样搬过来，行为一字未改）
Route.tableHref            : string            // "?table=1"，页面上那条链接与闸门读同一个真源
```

**页面侧认地址的地方从此只有这一个模块**（`Main.devSurfaceRequested` 搬进来了）。
三者正交：`?table=1&dev=1` 成立；**hash 不当路由用**——带 hash 打开落在 `Landing.Home`，
首页 Demo 照常播，不白屏、不报错（票 78 才解码它）。
**认不出来的 query 一律当首页**：陌生人手里那条链接可能带着 `?utm_source=…`。

`TableState.init ()` 按 `Route.landing ()` 分派到 `home ()` 或 `initial …`。
两个入口都是纯的（`home` 只造一条 `Cmd`，效果体在浏览器里才跑），因此 dotnet 侧用例两页都测得到。

### 1.5 公开面（`TablePage` 转出八个，dotnet 侧用例认的就是它们）

`initial` / `home` / `init` / `update` / `live` / `shown` / `rosterOf` / `renderingPending` / `canAdvance`。
两处签名变了，都变得更诚实：

- **`rosterOf : TableModel -> Roster option`**（原来是 `Roster`）：**回放没有配桌**。
  牌谱开头 `start_game` 里那几个名字是**录下来的**，不是这一桌推导出来的；
  编一份出来只会被人当真（判据 12：走不到的分支不立，走得到的别混进万能分支）。
- **`canAdvance` 从 `internal` 变公开**：播放键灰不灰，视图与用例读同一个判据。

---

## 2. Demo 资产

### 2.1 产出它的那一条命令

CLI 加了一个子命令（`janpo paifu`，与 `janpo game` 跑同一场对局、换一种输出）：

```sh
dotnet run --project src/Janpo.Cli -c Release -- paifu 3 --opinionated > web/public/demo-paifu.json
```

**种子 3，`--opinionated`，东风战**（`Ruleset.yonma` 默认就是东风战）。
它与页面上「导出牌谱」走**同一个编码器**（`Paifu.encoder`），不另拼一份 JSON。

挑种子的探针（一次性脚本，没进仓库）扫了 1–59 号种子，筛「有立直 + 有副露 + 以和了终」，
按事件数排序取最小的那一颗：

```
{"seed":3,"bytes":21484,"events":404,"kyokus":4,"reach":5,"naki":3,"hora":3,"endedWith":"hora"}
{"seed":15,"bytes":24042,"events":446,"kyokus":5,"reach":5,"naki":6,"hora":5,"endedWith":"hora"}
{"seed":38,"bytes":24202,"events":461,"kyokus":4,"reach":3,"naki":3,"hora":3,"endedWith":"hora"}
…（59 颗里合格 33 颗）
```

**这一局长什么样**：东 1 局到东 4 局四局打满，5 次立直成立、3 组碰、3 次和了、1 次流局，
**末局以一记 30 符 5 番 8000 点的荣和收尾**（座位 0 荣和座位 1，翻出里宝牌 9 万），
终局 `[32000, 15000, 34000, 19000]` / 顺位 `[2, 4, 1, 3]`。停在那一屏时结算面板与终局精算都在。

### 2.2 体积与首屏

| 量 | 值 |
|---|---|
| 资产字节 | **21,485**（`web/public/demo-paifu.json`，一行紧凑 JSON） |
| 过线字节 | **2,704**（vite 的 gzip，实测 `transferSize`） |
| 帧数 | **256**（4 局、252 手） |
| `fetch` 耗时 | 1 ms（本机 preview） |
| `Paifu.decoder` | 1–4 ms |
| `Table.replay` fold | **46–74 ms**（5 次：74 / 54 / 49 / 50 / 46） |
| 首屏 `/`（打开 → `table-board` 出现） | **123 ms**（5 次中位；121/123/123/124/143） |
| 首屏 `?table=1` 同一量 | **53 ms**（5 次中位；51/52/53/54/55） |

**差的那 70 ms 里，fold 占约 50 ms**，其余是解码与第一次 React 渲染——
没有再往下拆（判据 14：没量过的分段不写成因果）。**资产不打进 bundle**：
`web/public/` 由 vite 原样拷进 `dist/`，页面用 `fetch` 拿。

**播完一整场要多久**：256 帧 × 300 ms（2×）= **1 分 17 秒**。1× 是 2 分 34 秒，
所以首页的初始档速定在 2×（`TableState.demoSpeed`）；人一按倍速就照他的走。

### 2.3 换资产的手续（写给票 79）

1. **换哪个文件**：`web/public/demo-paifu.json`，**只有这一个**。代码一行不动，
   `web/src/demo/paifu.ts` 里那个文件名（`demo-paifu.json`）是唯一写死的字符串。
2. **怎么产出**：上面那条命令，或任何能吐出一份合法 `Paifu` JSON 的路子
   （页面上的「导出牌谱」下下来的那一份同样能用——同一个编码器）。
   **报告里要写清是哪条命令 + 哪颗种子**，别留下一份没人说得清来历的文件。
3. **会跟着红的断言**（`tests/Janpo.Web.Tests/HomePageTests.fs`，共五条）：
   - `Demo 是东风战`（`Ruleset.Length = Tonpuusen`）
   - `Demo 里有立直也有副露`
   - `Demo 以和了终`（最后一条结局事件是 `hora` 而不是 `ryukyoku`）
   - `Demo 的体积在预算内`（**512 KB**；现在 21 KB，留了很大余量）
   - `Demo 回放得动，且打到了终局精算`（`Table.replay` + `Board.final` / `Board.settlement`）
   这五条钉的是**够不够格当门面**，不是「哪一张牌打在哪一巡」——ADR-0003 说它是产品资产不是测试固件。
4. **体积上限建议**：真 LLM 对局带 thinking 约 10 KB/手，一场东风战 250 手 ≈ **2.5 MB**，
   **会撞上 512 KB 那条断言**。到时候两条路：`Paifu.stripThinking` 只留棋谱（约 20 KB），
   或者留 thinking 但把预算显式提到那个数并在报告里说清首屏多花了多少毫秒
   （**别默默改预算**——改它这个动作本身就是让人停下来想一想）。
   顺带一提：`fold` 是 O(帧数) 的，与 thinking 的字节数无关；变慢的是 `fetch` 与 `Paifu.decoder`。
5. **四席对局的名字**：`start_game` 的 `names` 现在是 `["opinionated" ×4]`。
   换成真模型时它会变成 `provider/model`——牌桌上现在不显示它，票 76 的气泡才会用到。

---

## 3. 闸门：谁开哪个地址

**十趟**（原九趟 + 首页那一道）。地址一次改到位，票 75/76/78 不必再动这几个脚本。

| 闸门 | 地址 | 为什么 |
|---|---|---|
| `verify-tracer` | `/` **＋** `?table=1` **＋** `?dev=1` | 见下 |
| `verify-home`（**新**） | `/` | 它量的就是首页 |
| `verify-board` | `?table=1` | 要填种子、要点单步、要读牌桌 |
| `verify-export` ×3（含 `--poison`） | `?table=1` | 要点「导出牌谱」 |
| `verify-golden` | `?table=1` | 只借页面跑引擎；那是最安静的一页 |
| `verify-share` | `?table=1` | 同上 |
| `verify-redaction` | `?table=1` | 要配模型坐席 |
| `verify-custom-endpoint`（不进 CI） | `?table=1` | 同上 |
| `verify-llm-seat`（手验，不进 CI） | `?table=1` | 同上 |
| `shoot-table` | `?table=1`（默认）／`/`（`--home`） | 两张图两个地址 |

地址不再各写各的：`serve.mjs` 出一个 `hostPage(origin)`，八个脚本读它一处。

**`verify-tracer` 为什么开三次**：它量的三件事分属三个地址。
① `/` 上「没有开发向内容 + 页脚那条回仓库的路」（票 35/37）——**继续开 `/`**，
含义从「默认视图」变成「首页 Demo」，断言一条没改、一条没放宽；
② 副露的来源与被鸣的那张（票 38）要**填种子 + 一手一手点单步**，那两个控件只在 `?table=1` 上，
因此那一段改开主持人那一页——**同一颗种子 1223、同一批副露、同一四条性质**；
③ `?dev=1` 读曳光弹那几行数（票 19）。

**`verify-home` 的四条断言**（票面点名的那四条）：

1. **牌桌在动**：隔 1000 ms 采两次「四家的河合计」，**必须变大**；
2. **没有配桌控件**：查 9 个只属于 `?table=1` 的 testId（`table-llm-panel` / `table-seed` /
   `table-step` / `table-next` / `table-export` / `table-llm-none` / `table-llm-provider` /
   `table-llm-key` / `table-bot-random`）；
3. **有一条去 `?table=1` 的路**：**真点过去**，落地地址 `table=1`，
   且那一页的播放键上写着「播放」（**默认暂停**——要点、要读牌桌的闸门全靠这一条）；
4. **页脚照旧**（票 37）：外链 + MIT。

它还顺带是「资产用 `fetch` 拉、不打进 bundle」那条路径的唯一无头证据：拉不到时 ① 当场红。

---

## 4. 每道新断言先红一次（判据 1 的原始输出）

**全部实跑过，跑完已还原**（改完逐个 `diff` 对回备份，见 §6 的清单）。

### 4.1 首页那一道（`node scripts/verify-home.mjs`）

**红-1｜把自动播关掉**（`Playback.playing demoSpeed` → `Playback.initial`）：

```
首页少了该给访客的东西：
牌桌没在动：1000 ms 前后四家的河合计都是 0 张（自动播没跑起来）
```

**红-2｜往首页上挂一个配桌面板**：

```
首页上漏出了只属于主持人那一页的控件：
首页上还挂着 [data-testid="table-llm-panel"]（1 个）
```

**红-3｜把「自己开一桌」那条 `<a>` 换成 `<span>`**：

```
首页少了该给访客的东西：
首页上没有一条去 `?table=1` 的路（访客摸不到 Host 那一侧）
```

**红-4｜让 `?table=1` 落地时也自动播**（`Playback.initial` → `Playback.playing Speed.X1`）：

```
首页少了该给访客的东西：
?table=1 落地那一刻不是暂停着的（播放键上写着「暂停」）
```

**红-5｜把 `dist/demo-paifu.json` 藏起来**（资产拉不到）：

```
首页没摆出牌桌（那份 Demo 牌谱多半没拉到）：
Demo 牌谱读不动：Given an invalid JSON: Unexpected token '<', "<!doctype "... is not valid JSON
```

（说的是「读不动」而不是「拉不到」，因为 `vite preview` 对缺失路径回的是 `index.html` 而不是 404
——那是服务器的行为不是我们的；Pages 上是真 404，那时走的是「Demo 牌谱拉不到：… 回了 HTTP 404」。
**两条路都不白屏**，这才是这条断言要的。）

**红-6｜把页脚挪到 `?dev=1` 后面**：

```
首页少了该给访客的东西：
首页的页脚里没有一条指回仓库的外链（票 37）
首页的正文里没提许可（MIT）（票 37）
```

### 4.2 dotnet 侧

**红-7｜回放不捞读法**（`Table.replay` 每帧把 `Readings` 清空）：

```
Janpo.Web.Tests.ReplayTableTests.和了那一帧的读法在：结算面板的役与符番只有这一个来源 [FAIL]
  错误消息:  Assert.Equal() Failure: Values differ
失败! - 失败: 1，通过: 120，总计: 121
```

**红-8｜开局那一帧不重建掩蔽流**（`opened` 里 `viewsOf` 换成沿用上一局的）：

```
Janpo.Web.Tests.ReplayTableTests.逐帧的掩蔽流与重头 fold 一致：座位视角切得动 [FAIL]
  错误消息:  Assert.Equal() Failure: Values differ
失败! - 失败: 1，通过: 120，总计: 121
```

**红-9｜换一份不合格的资产**（`janpo paifu 4242 --hanchan --uniform`：半庄 + 均匀随机 + 以流局终）
——**这就是票 79 换资产时会看到的样子**：

```
Janpo.Web.Tests.HomePageTests.Demo 是东风战：首页不该让人等半小时 [FAIL]
  Expected: Tonpuusen
  Actual:   Hanchan
Janpo.Web.Tests.HomePageTests.Demo 以和了终：停下来那一屏有役与符番可看 [FAIL]
  System.Exception : 首页那份 Demo 该以和了终，末尾却是 Some(Ryuukyoku { Reason = Fanpai …
Janpo.Web.Tests.HomePageTests.Demo 里有立直也有副露：挑的是一局看得懂的牌 [FAIL]
失败! - 失败: 3，通过: 118，总计: 121
```

**红-10｜体积预算**（临时把 512 KB 压到 8 KB）：

```
Janpo.Web.Tests.HomePageTests.Demo 的体积在预算内：首屏要 fetch 它 [FAIL]
  Demo 牌谱 21485 字节，超出 8192 字节的预算
失败! - 失败: 1，通过: 120，总计: 121
```

**红-11｜首页不自动播**（同红-1，看 dotnet 侧那四条状态机断言）：

```
Janpo.Web.Tests.HomePageTests.一记定时器推一帧：牌桌真的在走 [FAIL]
Janpo.Web.Tests.HomePageTests.「从头再放」回到第 0 帧并接着播 [FAIL]
Janpo.Web.Tests.HomePageTests.播到终局就停在结算面板上 [FAIL]
Janpo.Web.Tests.HomePageTests.牌谱回来就自动播，且规则集换成牌谱自带的那一份 [FAIL]
失败! - 失败: 4，通过: 117，总计: 121
```

**红-12｜`onLive` 不再拦回放**（让 Live 的消息动到回放那一屏）：

```
Janpo.Web.Tests.HomePageTests.Live 那几条消息在回放里一律无事发生 [FAIL]
  System.Exception : 这一刻该有一桌，却是 Loading
失败! - 失败: 1，通过: 120，总计: 121
```

**红-13｜轨迹漏掉「过」**（`Action.None` 不进 `Played`——它不产出事件，最容易漏）：

```
Janpo.Engine.Tests.ReplayTests.逐手轨迹交回引擎，走出的事件流与 fold 出来的那一份逐条相同 [FAIL]
Janpo.Engine.Tests.ReplayTests.轨迹的开局局面就是这一局的开头那几条事件 [FAIL]
失败! - 失败: 2，通过: 9，总计: 11
```

**另有一次「先怀疑断言自己」**（判据 6）：`一帧就是一手` 那条头一次红在
`Expected 553 / Actual 552`——是我把「种子帧」多数了一次，**式子错了不是实现错了**，
改成 `帧数 = 手数 + 局数` 之后绿。原文留在这里免得下一个人重推一遍。

---

## 5. 截图：我亲眼看到了什么

两张都重出了（`node scripts/shoot-table.mjs` / `--home`），**都打开看过**（判据 7）。

### `docs/images/home.png`（**新**，`/`，1088×861）

从上到下：h1「janpo —— 浏览器里的 LLM 日麻竞技场」→ 一段访客向的话（「下面这一局是录下来的，
正在自动回放——不用配置、不用 API key……」）→ **蓝色链接「自己开一桌 →」**加一句说明 →
控制条 **「暂停」**（也就是**正在播**）／「从头再放」／倍速 1× **2×**（选中）4× 8× →
视角排 座位 0（选中）/1/2/3/上帝视角/危险度，**后面没有种子框、没有「重开」** →
「上一手：座位 0 手切白」→ 四家围坐的牌桌。

牌桌上我看到的：座位 0（自家、亲、视角三枚标记）手牌 13 张**亮着**（河里有一张红字的赤 5 万），
其余三家 13/10/13 张**斜纹牌背**（票 32 那次「牌背整片透明」没有回来）；
座位 3 有一组碰「白」，横放那张落在**中间格**＝对家，与它的绝对座位对得上；
桌心：东 1 局 0 本场、供托 0 根、剩余摸牌 50 张、宝牌指示牌 7 筒，加参照系那两句。

**没有配桌与模型面板**——图上从视角那一排直接就是牌桌。这正是这一票要的第一眼。
（页脚不在图里：截的是 `.page.table-page` 这一块，页脚在它外面的 `.shell` 里，
与 `table.png` 一直以来的取景一致；页脚由 `verify-home` 与 `verify-tracer` 各钉一次。）

### `docs/images/table.png`（`?table=1`，1088×1369）

**语义变了**（从「打开站点看到的」变成「主持人那一页」），**内容与从前一样**：
播放/单步/下一局（灰）/导出牌谱/倍速、视角与种子 `1177`/重开、整块配桌与模型面板、
「四家都是均匀随机的选手」、种子 1177 走 52 手的牌桌（座位 0 两组吃、其余三家各一组碰）。
README 那句图注跟着改成了「`?table=1`，围观视角坐在座位 0，种子 1177 走了 52 手」。

---

## 6. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，39.8s（基线 42.5s）；引擎 744 + 页面 121 条 |
| 浏览器十趟 | `cd web && node scripts/verify-browser.mjs` | 全 ✓（tracer 0.8s / **home 1.3s** / board 1.1s / golden 0.4s / export×3 / redaction 1.2s / share×2） |
| 首页那一道单跑 | `node scripts/verify-home.mjs` | 四条各印一行 ✓ |
| 每道新断言先红 | §4 十三次 | 全部红过，输出抄在 §4 |
| 首屏耗时 | 一次性 playwright 脚本（没进仓库） | §2.2 |
| 帧数与终局 | `dotnet fsi`（一次性） | 256 帧 / 4 局 / `[32000,15000,34000,19000]` |
| 截图 | `shoot-table.mjs` 与 `--home` | §5，两张都打开看过 |
| 还原干净 | 逐个 `diff` 对回 `/tmp/*.bak` | `TableState` / `TablePage` / `TablePanel` / `Table` / `Main` / `Replay` / `paifu.ts` / 资产 八个全 OK |

`jj diff --stat`：42 个文件，+1856 / −341。
**没碰**：`web/src/agent/**`、引擎的规则判定、`docs/adr/*`、`CONTEXT.md`、
`src/Janpo.Engine/Paifu.fs`（格式与 `Paifu.Version` 一字未动）、`.github/workflows/`、
`web/index.html`、`web/src/styles.css`、曳光弹（`App.fs` / `Tracer.fs`）。

---

## 7. code-review（Standards + Spec 两轴，fixed point `d5098272`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓** 全程 `jj status` / `jj diff` / `jj commit`，无远端操作、无交互式 flag。
- **工具强制的** `fantomas --check` / `check-style.sh` / Biome / tsc 全绿；
  引擎 `let mutable` 一处未新增（预算仍是 2）。
- **F# 风格（`docs/agents/fsharp-style.md`）**：新代码里没有规则 1/2/3 的形状
  （`Table.replay` 与 `Replay.folded` 都是从左往右的 `|> Result.bind` 链；
  `Error(ReplayError.Stranded(…))` 是限制 A 明说不该管道化的构造子嵌套，保留）。
  规则 4.1 的「谓词套取值器」保留了几处（`Option.isNone live.LlmAt`），正确。
- **注释写「为什么」✓** 新增的每个类型与出口都写了「为什么是这个形状」与「别写成什么」。
- **blocking：0。**

### Spec（票面的六条行为 + 六条闸门 + 六条边界）

逐条对照见票文件的勾选框。三处值得写下来：

- **「`Replay` fold 的时候把 `HoraReading` 捞下来」落成了「回放复用 `Table.apply` 的捞法」**
  ——实质（结算面板有役与符番、且捞在宣言那一刻）一分没少，而且**少了一份实现**。裁决 71-2。
- **`rosterOf` 的签名变了**（`Roster` → `Roster option`）：这是联合类型逼出来的诚实，不是加码。
- **边界守住了**：没接 `Share.ofPayload`（带 hash 打开退回首页 Demo，实测不白屏）、
  没做时间轴（回放只顺着播，控制条上根本没有单步）、没碰 `web/src/agent/**` 与引擎的规则判定。

### 记录但没改的 nitpick

1. `ReplayTable.Ready` 把**整局的帧**都留在内存里（256 个 `Table`）。
   一次 fold 好换来的是「播一手 = `cursor + 1`」这个纯到可以在 dotnet 上测的 update，
   顺带把票 75 的时间轴拖动变成白送。真要省内存得改成「只留当前帧 + 从局首重放」，
   那时 update 就不纯了——等 75 真嫌它占内存再说。
2. `TableBoard.tableBody` 现在从 `TableState.live model` 拿 Live 那一半来决定画不画 Agent 状态行。
   视图去问状态模块要一格，形状上不算漂亮；改成「`tableBody` 收一个 `LiveTable option` 参数」
   会让 `TablePage` 那两个布局各传一次。两种都行，没改是因为前者调用点少一处。
3. `web/biome.json` 把 `public` 整个排除在外了（那份资产是一行紧凑 JSON，
   biome 会把它格式化成几千行）。理由与 `dist` / `src/generated` 同类：那是产出物不是源码。
4. `Table.fs` 的 `replay` 那一段（75 行）住在「牌桌的构造与推进」里。它确实是构造的一种，
   但也确实让这个文件多了一个理由被改。真要拆得等第二个回放消费方出现（票 78 的导入）。

---

## 8. 留给人的待审项

1. **README 改了两处**（不在票面里，属于 scope creep，记在 DECISIONS 71-6）：
   首页那一段加了「第一眼是一局录像」并插了 `home.png`；「怎么玩」第 1 步改成
   「打开链接 → 点『自己开一桌』」。**不改就是假的**——原文写着「打开就能玩」后面直接
   接配置面板的说明，而那一页现在要多点一下。措辞值得主人过一眼。
2. **首页初始档速定在 2×**（1 分 17 秒播完一整场）。这是产品口味，`TableState.demoSpeed` 一行改得动。
3. **票 79 换资产时的体积**：真对局带 thinking 约 2.5 MB，会撞上 512 KB 那条断言（§2.3 第 4 点）。
   要不要在 URL 分享之外也给 Demo 做一次 `stripThinking`，是票 79 的取舍，这里只把数摆出来。
4. **`?dev=1` 现在落在首页上**（`/?dev=1` = Demo 回放 + 曳光弹）。三者正交是主人裁的，
   但曳光弹页下面有一桌在自动播，读起来略吵；真嫌吵就写 `?table=1&dev=1`。
