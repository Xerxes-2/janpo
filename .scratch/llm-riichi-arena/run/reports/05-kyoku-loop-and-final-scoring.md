# 05 — Kyoku 循环、连庄与终局精算 · 实现报告

**Status:** done（票已标 `ready-for-human`）
**工作区:** `/home/xerxes2/janpo-ws-b`
**Fixed point:** `fc74deaa`（`@-`，06 落地那个 change）
**验证:** `./scripts/ci.sh` 全绿（366 个测试，`fantomas --check` 干净，引擎依赖白名单通过）；
本票新增 32 个测试（23 条黄金用例 + 8 条属性 + 1 条事件 wire 用例），另做了 7 次变异验证（见末节）。

引擎现在能无头跑完一整场对局：`janpo game 42` 从东 1 局打到东 4 局，产出完整 mjai 事件流、
终局点数与顺位。**点数的具体数值一律不进断言**（裁决 D-4），全部写成不变量。

---

## 公开签名（本票新增 / 改动）

```fsharp
// GameLength.fs（新文件）—— 对局长度
type GameLength = Tonpuusen | Hanchan
GameLength.all : GameLength list
GameLength.bakazes : GameLength -> Kaze list          // 东风战 [Ton]；半庄战 [Ton; Nan]
GameLength.toDisplay : GameLength -> string

// Ruleset.fs —— 加两个字段与四个推导量
Ruleset.Length : GameLength                            // 四麻预设取 Tonpuusen（M0 的验收就是东风战）
Ruleset.RiichiBou : int                                // 立直棒一根的点数，预设 1000（09 票扣的也是它）
Ruleset.withLength : GameLength -> Ruleset -> Ruleset
Ruleset.kyokus : Ruleset -> (Kaze * int) list          // **局数序列**：每个场风各打 SeatCount 局
Ruleset.startingTotal : Ruleset -> int                 // SeatCount × StartingScore
Ruleset.kyotakuScore : Ruleset -> int -> int           // 供托根数 → 点数

// Event.fs —— 加两个事件（wire 与 mjai / libriichi 一致，均不带字段）
Event.EndKyoku            // {"type":"end_kyoku"}
Event.EndGame             // {"type":"end_game"}

// Game.fs（新文件）—— **GameState 之上的一层：一整场对局**
type GameResult = { Scores: int list; Juni: int list }          // 终局精算：最终点数与顺位
[<RequireQualifiedAccess>]
type GameProgress = NextKyoku of context: KyokuContext | Ended of result: GameResult
type Game = private { … }                                        // 规则集 + 已打完的局 + 进程

// 核心（纯函数，本票的主干）
Game.after : Ruleset -> KyokuContext -> KyokuEnd -> int list -> GameProgress
Game.settle : Ruleset -> int -> int list -> GameResult           // 供托归头名，再排顺位
// 构造与迁移
Game.start : Ruleset -> Game
Game.advance : GameState -> Game -> Game                         // 把打完的一局收进对局
// 拆解
Game.ruleset / progress / played / nextKyoku / result / isEnded / scores / kyotaku / events
// 驱动
Game.play : Player<'p> -> 'p -> Rng -> Game -> Result<Game * 'p * Rng, KyokuError>
Game.run  : Player<'p> -> 'p -> Rng -> Game -> Result<Game * 'p * Rng, KyokuError>
Game.runRandom : Ruleset -> Rng -> Result<Game * Rng, KyokuError>
GameResult.ranking : GameResult -> (int * Seat * int) list       // 顺位、座位、点数，头名在前
GameResult.toDisplay : GameResult -> string
```

CLI 新增一个子命令（**14 票 soak 的入口**）：

```
janpo game <种子> [--no-akadora] [--hanchan | --tonpuusen]
```

每行一个 mjai JSON 事件（每局之后 `end_kyoku`，终局 `end_game`），随后 `kyokus:` / `scores:` /
`juni:` / `display:` 四行。同一种子必然跑出同一场对局。

---

## 关键取舍

### 「一局的结局 → 下一局的场况」是 `Game.after`，一个纯函数

它收这一局的场况、`KyokuEnd` 与终了时的点数，给出下一局的场况或终局精算。三条结转规则：

| 结局 | 局数与亲 | Honba | Kyotaku |
| --- | --- | --- | --- |
| Oya 和了（连庄） | 不变 | +1 | 清零（归和了者） |
| Ko 和了（进局） | 亲移到下家、局数走序列下一项 | **归零** | 清零（归和了者） |
| 流局 · Oya 听牌（连庄） | 不变 | +1 | 原样结转 |
| 流局 · Oya 不听（进局） | 亲移到下家、局数走序列下一项 | **+1**（不归零） | 原样结转 |

「流局进局时 Honba 照样递增」是通行规则（东1局0本场荒牌流局、亲不听 ⇒ 东2局1本场），
票里那句「进局且非连庄时归零」按这条读作「**和了**进局才归零」。

把它做成纯函数而不是只藏在驱动里，是为了让全部推进规则的黄金用例都能拿合成的 `KyokuEnd`
直接验，不必去碰运气找一个「亲刚好听牌的种子」。

### 局数序列从规则集推，不写死

`Ruleset.kyokus = GameLength.bakazes × [1 .. SeatCount]`：四麻东风战 4 局、四麻半庄 8 局、
三麻半庄 6 局，三个数都是推出来的。终局条件就是「序列走完」——**连庄也不延长**：
东 4 局无论谁和了 / 谁听牌，打完就终局（票明文：不做西入 / 延长）。

### 终局精算：供托归头名，顺位由点数定

`Game.settle` 把场上剩下的供托折成点数加给头名（同点取起家方向在前的那家），再排顺位。
顺位按精算前的点数排——把供托给头名不可能改变名次顺序，两种算法同解。
同点的名次由座位号决定（起家是座位 0），这是通行做法。

### 测试对 08 天然免疫（裁决 D-4）

**没有一条断言写死点数的数值。** 断言分三类：

1. **总和守恒**：`Σ 各家点数 + 供托点数 = Ruleset.startingTotal`，在一场对局的每一局边界上验；
   精算前后同样验（供托只换归属）。
2. **结转与归属**：下一局的局初点数 = 上一局终了时的点数（`GameState.scores`，值是谁算的不管）；
   供托流局结转、和了清零；Honba 与 Oya 按上表变化。
3. **序关系**：顺位是 `1 .. SeatCount` 的排列；点数高的名次靠前，同点则座位号小的靠前。

黄金用例里出现的点数（`[26000; 24000; 27000; 23000]` 这类）全是**用例自己喂进去的输入**，
断言的是「原样搬过去了」或「总和没变」，不是「引擎算出了这个数」。08 把和了点数从 0 改成真值时，
这些用例一条都不用改。

---

## 收敛了 06 的哪段重复逻辑（备注 N-2）

**只有一段重复：连庄判定。** 06 已经把它落成 `KyokuEnd.isRenchan : Seat -> KyokuEnd -> bool`
（Oya 和了则连庄，双响里只要有 Oya 也算；流局时 Oya 听牌则连庄），并在 `HoraTests` 里有用例。

我**没有**再写一份，而是让 `Game.after` 读它这一个布尔值——具体地说，本层从头到尾没有碰过
`Ryuukyoku.Tenpais`，也没有对 `KyokuEnd.Hora` 里的 `Actor` 与 Oya 做过第二次比较。
全仓 `grep isRenchan` 只有一处定义、一处生产调用。

**放在哪一层、为什么：** 判定留在 `GameState.fs` 的 `KyokuEnd` 模块里（06 的位置，一字未动），
因为它是 `KyokuEnd` 这个类型的一个拆解——只看「这一局是怎么结束的 + 亲是谁」，不需要规则集、
不需要局数序列。而**消费**这个布尔值的那些规则（Honba 递增、Kyotaku 结转、局数序列、终局精算）
需要 `Ruleset` 与跨局的上下文，因此全部落在新的 `Game` 层。
两层的分界线正好是「一局知道的事」与「一场对局才知道的事」。

反过来的做法（把 `isRenchan` 搬进 `Game`）被否决：那会让 06 的 `HoraTests` 依赖 05 的文件，
也会让「终局形态」这个类型的拆解散落在两个文件里。

---

## 边界：本票没做什么

- **点数的真值**：和了点、本场与供托的授受一律是 08。本层只搬运 `GameState.scores`。
- **一局之内**的「点数与供托之和不变」：那是 08 的验收项（授受的正确性）与 14 的 soak 全集；
  本票的属性验在**每一局的边界**上。理由：09 之前引擎里没有「局内增加供托」这回事，
  硬写一条局内属性会在 09 落地时红给别人看。
- **09 的接口债（写在 `Game.after` 的文档注释里）**：立直棒是**局内**产生的，而 `after` 现在结转的是
  `context.Kyotaku`（局初的供托）。09 落地时要把供托的来源换成「这一局终了时场上实际还剩几根」，
  其余规则一条不变。
- **飞び / 西入 / アガリやめ**：都不做（票的 Out of Scope）。点数可以是负的，局数序列走完就终局。

---

## Review 结论（两轴，fixed point `fc74deaa`）

### Standards

- 一概念一文件、`namespace Janpo`、fsproj 按依赖顺序（`GameLength` 在 `Ruleset` 前，`Game` 在最后）✔
- 类型与同名 `[<RequireQualifiedAccess>]` module 同文件；`toDisplay` 在文件末尾且中文只在那里 ✔
- 错误是值：本票**没有**新增错误 DU——`Game.run` / `play` 复用 04 的 `KyokuError`，
  `Game.advance` 的两种退化输入（这一局还没终 / 这场对局已终）定义为「原样返回」并写进文档 ✔
- **裁决 D-3**：`GameProgress` 有 `Ended`，与 `Phase.Ended` 同名 ⇒ 给 `GameProgress` 加了
  `[<RequireQualifiedAccess>]`（这正是 D-3 说的那类碰撞，这次在写的时候就挡掉了）✔
- **裁决 D-1**：`EndKyoku` / `EndGame` 的 F# 拼法与 mjai wire 一致（术语表没有这两个词，
  按备注 N-1 用 mjai 事件名转 PascalCase）；`Tonpuusen` / `Hanchan` 是术语表原词 ✔
- Fable 子集：没有新包、没有反射、没有 `System.*`（`lazy` 只出现在测试固件里）✔
- 测试三件套 `GameGenerators` / `GameTests` / `GameProperties`；中文测试名断言的都是公开 API，
  构造走的是票明文允许的 `internal` 入口（`GameState.startFrom` + `Wall.ofOrdered`）✔

两条自查出来的 nitpick（**已修**，不留）：`juniOf` 里多传的两个参数收成了闭包；
`after` 里只用一次的 thunk 内联了。

一条**记录不修**的：`GameResult.ranking` 返回三元组 `(顺位, 座位, 点数)` 而不是具名记录——
它只服务渲染与一条用例，立一个类型不划算。

### Spec（票的 8 条验收）

8 条**全部满足**，逐条勾在票文件里。三处要说清楚的限定：

- 「无人和了时按规则决定进局或连庄」：判据读 06 的 `KyokuEnd.isRenchan`（见上一节），
  本票做的是判据之后的那一段。
- 「Kyotaku 跨 Kyoku 结转，终局时归属正确」：结转与归属都做了并有用例；
  **和了时把供托的点数加给和了者是 08 的授受**，本层只负责把场上的根数清零。
  M0 里 09 还没落地，供托恒为 0，因此这条在跑起来的对局里暂时是平凡成立的——
  用例用的是显式给供托的合成场况，不靠随机对局碰。
- 「属性：任意时刻四家点数与 Kyotaku 之和恒为初始总点」：验在每一局的边界上（见上一节的边界说明）。

spec.md 第 19 条用户故事（「四个随机 bot 在命令行跑完完整东风战」）现在跑得通：
`janpo game <种子>`。第 13 条的「对局长度（东风战 / 半庄战）」也落在规则集里了。

### 变异验证（7 次，每次只改一处再跑测试）

| 变异 | 被哪条抓住 |
| --- | --- |
| 流局不递增 Honba | `流局时亲听牌则连庄，本场加一`、`流局时亲不听则进局，但本场照样加一而不是归零` |
| 和了后供托不清零 | `供托在和了之后清零…`、`子荣和了的那一局收进对局之后进局，本场归零、供托清零` |
| 局数序列绕回（连庄延长对局） | `东风战在东4局结束后终局，不做西入与延长`（另：整场对局的用例直接跑不完 —— 属性与黄金用例都会挂） |
| 同点顺位反排 | `同点时起家方向在前的那家名次高`、属性 `点数高的顺位必然靠前…` |
| 精算把供托发给所有人 | `终局时场上剩下的供托归点数最高的那家，总点数不变`、属性 `终局精算只换供托的归属…` |
| 一律进局（忽略连庄） | 4 条（含真打一局的 `亲自摸和了的那一局…`） |
| 下一局沿用局初点数（不结转） | `亲和了则连庄…`、属性 `下一局的局初点数就是上一局终了时的点数` |

顺带修的一处测试卫生问题：`GameFixtures` 里那几场用于属性取样的对局原本是模块级的值，
会被同模块的任何一条用例连带初始化（跑七场对局）。改成 `lazy` + 取值函数之后，
只用推进规则的黄金用例不再为它买单。

---

## 留给人的待审项

1. **`Juni`（顺位）不在 `CONTEXT.md` 里**（提案 05-A）。术语表没有「顺位」这个词，
   我按 ADR-0001 取了罗马字 `Juni`。它与术语表里的 `Junme`（巡）只差一个字母，读起来容易晃眼；
   若要换成英文 `Rank`，改的是 `GameResult.Juni` 一个字段与几处用例。
2. **`Ruleset.yonma` 的长度预设取了 `Tonpuusen`**。M0 的验收与 CLI 默认都是东风战，
   而通行的「一场麻将」其实是半庄。若早上想让预设跟通行规则走，翻 `Ruleset.yonma` 的一个字段即可，
   用例里凡是依赖长度的都显式指定了规则集（`GameFixtures.tonpuusen` / `hanchan`）。
3. **オーラス的连庄一律不延长**。票明文「东4局结束后终局，不做西入/延长」，因此东 4 局亲连庄
   也终局。天凤 / 雀魂的通行规则是「亲不是头名就继续打（アガリやめ / テンパイやめ 是可选）」——
   要不要做成规则集开关，值得一裁（12 / 14 之前定就行）。
4. **飞び（点数为负即终局）没有做**，也没有相应的规则集字段。M0 的随机选手打不出负分，
   但 08 之后就打得出了；若要做，它是 `Game.after` 里终局条件的第二个来源。
5. **`Ruleset` 又被动了一次**（加 `Length` 与 `RiichiBou`）。08 若也动 `Ruleset`（Kiriage Mangan 开关），
   集成时会在这个 record 上撞一次——我把两个字段分别插在 `SeatCount` 与 `StartingScore` 之后，
   而不是追加在末尾，就是为了避开末尾那一行。
6. **`Event` 又加了两个 case**（`EndKyoku` / `EndGame`），凡是对 `Event` 做穷尽 match 的地方都要跟着补
   （本票补了 3 处测试）。09-12 加 case 时会再撞一次，这是备注 N-1 那个「三处」之外的既有成本。
