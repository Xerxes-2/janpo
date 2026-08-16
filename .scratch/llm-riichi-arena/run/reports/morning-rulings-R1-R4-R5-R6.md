# 晨间裁决 R-1 / R-4 / R-5 / R-6 的代码化

用户已裁决的四条，四个独立 commit，**每个 commit 之间 `./scripts/ci.sh` 全绿**。
基线：`okuuwpzk`（集成 11，493 测试全绿）。

顺序：R-6（改名）→ R-1（默认值）→ R-4（`Ruleset` 携带 `TileKindSet`）→ R-5（`Seat` 真类型）。

---

## R-6：消除跨层同名

**改了什么**

- `YakuError.NotAgari` → `YakuError.NoAgariShape`。名字与 `AgariShape` 这个术语对齐：
  它说的是「`AgariShape.classify` 给不出任何读法」。
- `YakuError` 加 `[<RequireQualifiedAccess>]`，两个 case 永远写全名。
- `IllegalAction` **未动、未限定**。它的 case 是引擎拒绝一个动作的**理由**，
  `NotAgari(actor, pai)` 与 `NotYourTurn` / `NotInHand` / `NotInHand` 是同一风格的一串。

**结果**：源码里不限定的 `NotAgari` / `NoYaku` 从此只可能是 `IllegalAction` 的。
裁决 D-3 留下的「读代码的人仍要停一下」消失。

**触碰的文件**：`Yaku.fs`（类型定义 + `detect` + `toDisplay`）、`GameState.fs`（4 处 `Error` 构造
+ 2 处文档注释）、`YakuTests.fs` 与 `ScoreTests.fs` 各 1 处断言。

**测试**：期望值一处没改语义——两处测试断言里的 `Error YakuError.NotAgari` 只是跟着改名。
493 通过。

---

## R-1：默认规则集对齐天凤（ADR-0004 决定 3）

**改了什么**

- `Ruleset.yonma`：`Atamahane = true` → `false`（双响 / 三响成立）。
- `Ruleset.withoutAtamahane` → **`Ruleset.withAtamahane`**（打开头跳）。默认已经是关的，
  留一个「关掉」的组合子会让读的人以为默认是开的。
- 字段的文档注释从「默认开，待人裁决」改成「默认关，ADR-0004 决定 3」。
- 其余默认值本就对齐，一处没动：`KiriageMangan = false`、`DoubleKazeJantouFu = 4`、
  `RinshanTsumoFu = true`、`KokushiAnkanChankan = false`。

**改了哪些测试的期望值，各自为什么**

一条用例都没删。共 6 处，分三类：

| # | 用例 | 改动 | 理由 |
|---|---|---|---|
| 1 | `HoraTests.头跳开着时同巡双响只成立打牌者下家优先的那一家` | `Assert.True(ruleset.Atamahane)` → 先 `Ruleset.withAtamahane ruleset` 再断言 | **测的是头跳本身**。头跳不再是默认，用例显式打开它，断言与结论一字未改 |
| 2 | `HoraTests.头跳开着时同巡三响只成立最靠前的一家` | 同上，改用 `Ruleset.withAtamahane ruleset` 起局 | 同上 |
| 3 | `HoraTests.头跳关掉时同巡双响都成立，按打牌者下家优先排序` | `Ruleset.withoutAtamahane ruleset` → `ruleset`，并补一行 `Assert.False(doubleRon.Atamahane)` | 组合子没了；默认就是这个形态，补的那行把「默认 = 关」写在用例里 |
| 4 | `HoraTests.头跳关掉时同巡三响也都成立` | 同上（不必补断言，#3 已钉） | 同上 |
| 5 | `HoraTests.双响时本场与供托只归排在最前的那一家` | 同上；点数期望值**一个数都没动** | 它本来就跑在关头跳的规则集上，只是关法从组合子变成默认 |
| 6 | `RulesetTests.符与点数的规则项是字段不是写死的，默认值照天凤` | **新增** 2 行断言：`Assert.False(Ruleset.yonma.Atamahane)` 与 `withAtamahane` 打得开 | 新默认值需要一处钉子；这个用例就是钉默认值的地方 |

另有 2 处是**默认值翻转后必然改变的观测结果**，不是「迁就实现」——它们测的是别的东西，
只是恰好跑在 `doubleRonScript` 上：

| # | 用例 | 改动 | 理由 |
|---|---|---|---|
| 7 | `HoraTests.摊好的两局各自以自摸和与荣和收尾` | 荣和那局的 `Actor` 期望 `[2]` → `[2; 3]`、`Target` `[0]` → `[0; 0]` | 用例测的是「摊好的剧本以荣和收尾」。剧本里座位 2 与 3 都听 4p，默认双响后**两家都成立**，这正是 R-1 要的行为 |
| 8 | `GameTests.子荣和了的那一局收进对局之后进局，本场归零、供托清零` | `match ... with \| [ hora ] -> ...` → 对**每一家** hora 断言不是亲 | 用例测的是「**子**和了 → 进局 / 本场归零 / 供托清零」，不是「只有一家和了」。双响后两家都是子，结论不变 |

**测试**：493 通过（数量不变）。

**留给人的一句话**：`Ruleset.withAtamahane` 是 13 票对拍的开关——牌谱来源若是允许头跳的平台，
在那里显式打开即可，而**默认配置现在跑得出真实牌谱**（备注 N-6 提的那条顾虑就此消掉）。

---

## R-4：`Ruleset` 直接携带 `TileKindSet`（ADR-0004 决定 4）

**改了什么**

- `Ruleset.TileKinds` 的类型：`Tile list` → **`TileKindSet`**。字段名不变（它说的还是同一件事），
  但值在规则集构造时派生一次，不再在每次形态判定前重派生。
- `Ruleset.yonma`：`TileKinds = TileKindSet.fourPlayer`。
- 派生量换 API（**两个都是 `TileKindSet` 已有的，一个新 API 都没加**）：
  - `Ruleset.wallSize`：`List.length ruleset.TileKinds` → `TileKindSet.count ruleset.TileKinds`
  - `Ruleset.wallTiles`：`ruleset.TileKinds |> List.collect ...` → `TileKindSet.kinds ruleset.TileKinds |> ...`
- 消掉的重派生点（原先每调一次分配一个 34 长 `bool array`）：
  - `Yaku.candidates`（**每副和了牌的每次读法枚举都走这里**）
  - `Yaku.detect` 的失败分支
  - `GameState.ofStarted`
- **顺带删掉 `GameState.KindSet` 字段**。它当初就是「每次 `ofKinds` 太贵」的缓解措施；
  规则集自己带了之后它是同一份东西的第二个表示，正是 ADR-0004 决定 4 要消掉的那种重复。
  4 个使用点改成 `state.Ruleset.TileKinds`；私有函数 `awaitingDahaiActions` 的 `kindSet` 形参
  也去掉了（同一个函数已经收着 `ruleset`）。
- `Janpo.Cli` 里独立的 `TileKindSet.fourPlayer` 常量改成 `Ruleset.yonma.TileKinds`，
  同一条理由：牌种全集从规则集读。

**封装没破**：`TileKindSet` 仍是 `private` record，`legalFlags` 仍是 `internal` 快路径，
`Ruleset.fs` 只用公开的 `fourPlayer` / `count` / `kinds` 三个 API。
编译顺序不必动（`TileKindSet.fs` 本来就排在 `Ruleset.fs` 之前）。

**测试**：期望值一处没改。7 处测试用的自定义规则集在末尾接了 `|> TileKindSet.ofKinds`
（`KyokuStartTests` 3、`RulesetTests` 2、`KyokuTests` 1、`WallTests` 1），
`GameStateFixtures.kindSet` 从 `TileKindSet.ofKinds ruleset.TileKinds` 简化成 `ruleset.TileKinds`。
493 通过。

**真引擎复验**（`dotnet fsi` 直调编译好的 DLL）：

```
Atamahane(默认) = false          牌种数 = 34
wallSize = 136, wallTiles = 136
三麻形状：牌种数 = 27, wallSize = 108, wallTiles = 108
wallTiles 升序规范形 = true
```

---

## R-5：`Seat` 从 `int` 透明别名换成真类型

### `Seat` 的最终形状

```fsharp
[<Struct>]
type Seat =
    private
        {
            /// 0 起的固定索引。
            Index: int
        }
```

与 `Tile` 同一形状（`[<Struct>]` + 私有 record），因此 Fable 侧零额外成本、`Map<Seat, _>` 与
`List.sortBy` 照旧可用（结构比较自动派生，`Wall.deal` 与 `Game.juniOf` 都靠它）。

**构造只有三条路**：

| 入口 | 用途 |
|---|---|
| `Seat.ofIndex : int -> Seat option` | 外来的裸整数（mjai wire、CLI、牌谱）。**负数不是座位** |
| `Seat.first` / `Seat.all` / `Seat.orderFrom` / `Seat.orderAfter` | 枚举 |
| `Seat.shimocha` / `kamicha` / `toimen` / `wrap` | 相对位置 |

外加 `internal Seat.ofIndexUnchecked`，给引擎内部与**测试固件**（经既有的
`InternalsVisibleTo`，与 `Wall.ofOrdered` / `GameState.startFrom` 同一道口子）。
测试里包了一层 `SeatFixtures.seat` / `seats`（`[<AutoOpen>]`），于是用例写 `seat 2` 而不是
`Seat.ofIndexUnchecked 2`——**它只在测试工程里存在**。

**上下界分工**：类型只守「非负」这条与规则集无关的下界；上界要座位数，由
`Seat.isValid ruleset seat` 判。所以 `seat 4` 仍造得出来（`SeatOutOfRange` 那条路径还在），
而 `-1` 造不出来。

### 收进 `Seat` 模块的座位算术（这次重构的真正收益）

`shift` 是私有的，**全仓库只有它一处对座位取模**。

| 函数 | 原先散在哪 |
|---|---|
| `shimocha`（下家） | 原 `Seat.next`。改名的理由同裁决 D-6：座位序的「下一个」就是下家，一个名字一件事 |
| `kamicha`（上家） | `GameState.responsesTo` 里的吃判据写作 `Seat.next ruleset target = seat`，现在是 `Seat.kamicha ruleset seat = target`（「只有上家打的能吃」，与注释同构）；`GameStateProperties` 的同一条也换了 |
| `toimen`（对家）**返回 option** | 新立。三麻没有对家，签名把这件事说清楚，省得别人写 `(seat + 2) % 4` |
| `distanceFrom`（相对第几家） | `GameState.nakiWinner` 原先造一遍 `orderFrom` 再 `List.tryFindIndex` 找位置；`Seat.jikaze` 原先自己写了一遍取模 |
| `orderAfter`（从打牌者下家起绕一圈） | 3 处 `Seat.orderFrom ruleset (Seat.next ruleset target)`（`responsesTo`、`ronWinners`、`nakiWinner`）——「打牌者下家优先」这条裁决顺序 |
| `wrap`（任意整数折进合法座位） | `KanProperties` 里的 `((liable % engine.SeatCount) + engine.SeatCount) % engine.SeatCount`，两处 |
| `first`（起家） | `KyokuContext.initial` 的 `Oya = 0`、`Wall.deal ruleset 0`、`GameTests` 的 `Oya = tonpuusen.SeatCount - 1`（→ `Seat.kamicha tonpuusen Seat.first`） |
| `tryItem` / `mapAt` / `indexed` | 「每家一项、按座位升序」的列表（`Scores`、`Deltas`、`Hands`、`Tenpais`、`Players`）：12 处 `List.tryItem seat xs`、`List.mapi (fun seat -> ...)`、`List.mapi2`、`List.indexed`。`GameState.updatePlayer` 现在就是 `Seat.mapAt` |
| `encoder` / `decoder` | mjai wire 的裸整数映射（裁决 D-1）。**唯一一处**：`Event.fs` 里 17 个 `Encode.int actor` / `Decode.int` 全换成它 |

`Seat.index` 只剩 **26 处**，全在渲染层（`IllegalAction.toDisplay` / `KyokuStartError.toDisplay` /
`GameResult.toDisplay` / CLI 的玩家名）与 wire（`Seat.encoder`）。模块注释把这条写成规矩：
**不许拿 `Seat.index` 出去做算术**。

### mjai wire 没变

`EventTests` 的逐字断言（`{"type":"tsumo","actor":2,...}`、`{"type":"pon","actor":1,"target":0,...}`）
一个字都没改，全部照过。真引擎复验也确认：

```
{"type":"tsumo","actor":2,"pai":"5mr"}
{"type":"pon","actor":2,"target":0,"pai":"5s","consumed":["5s","5s"]}
```

### 途中发现的真 bug

**没有行为 bug。** 这一条我做了对照实验才敢写：把基线 commit（`okuuwpzk`）整树抽到 `/tmp` 单独构建，
用 `dotnet fsi` 对**两个引擎**跑同样四个种子的完整对局，事件频次逐项相同：

```
基线 okuuwpzk        本次改完
chi        76        chi        76
dahai    1232        dahai    1232
pon        36        pon        36
ryukyoku   16        ryukyoku   16
tsumo    1120        tsumo    1120
other      20        other      20
```

（顺带纠正备注 N-8 的一个读法：那里记的 `chi 80 / pon 32` 是 **09 落地时**的数，
10 与 11 之后随机 Player 的动作集变了、RNG 消耗也变了，所以频次本就不同。
不是这次改动引起的。）

**但类型换掉之后，有三处「过去编译器抓不住」的东西被顶了出来**——都不是运行期 bug，
是表达方式上的漏洞：

1. **负数座位过去是可表达的。** `WallTests.亲不是合法座位时发不出配牌` 里原有一行
   `Wall.deal ruleset -1 (built 1)`，测的是运行期拒绝负数。现在负数在**类型层**就不是座位，
   那一行改成 `Assert.Equal<Seat option>(None, Seat.ofIndex -1)`——**判据没删，只是从运行期挪到了构造处**。
   同理 `GameStateArbitraries.Action()` 的取样从 `Gen.choose (-1, SeatCount)` 收成 `(0, SeatCount)`：
   越界仍取得到（属性要的是「非法动作不抛异常」），负数不必再取。
2. **`GameTests` 拿座位数当座位算**：`Oya = tonpuusen.SeatCount - 1`。它碰巧对（四家时最后一个座位就是
   起家的上家），但那是「局数/张数」类的标量在做座位算术——正是 R-5 要挡的那一类。现在是
   `Seat.kamicha tonpuusen Seat.first`。
3. **属性测试自己写了一遍取模折座位**（`KanProperties` 两处）。现在是 `Seat.wrap`。

### 测试

493 → **502**，一条都没删。多出来的 9 条全在 `SeatTests`：原先只测 `all` / `next` / `orderFrom` /
`isValid` 四件事，现在把新收进来的算术逐个钉住（构造的上下界分工、上家 / 对家 / 相对位置 /
`orderAfter` / `jikaze` / 三个列表拆解 / wire 往返）。三麻（`SeatCount = 3`）在每条里都跟着测一遍，
座位数照旧不写死 4。

新增文件 `tests/Janpo.Engine.Tests/SeatFixtures.fs`（排在测试工程编译列表最前）。

### 触碰面

35 个文件、307 处使用点里，src 侧 8 个文件、测试侧 24 个文件、CLI 1 个。
推进方式照票里说的「改定义 → 编译 → 修编译器指出的第一批 → 再编译」，一共十几轮。
