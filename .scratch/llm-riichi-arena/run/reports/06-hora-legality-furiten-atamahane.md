# 06 — 和了的成立路径（Furiten 与 Atamahane）· 实现报告

**Status:** done（票已标 `ready-for-human`）
**工作区:** `/home/xerxes2/janpo-ws-a`
**Fixed point:** `439a056f`
**验证:** `./scripts/ci.sh` 全绿（252 个测试，`fantomas --check` 干净，引擎依赖白名单通过）；
本票新增 29 个测试（26 条黄金用例 + 3 条属性），另做了 5 次变异验证（见末节）。
全量测试连跑 4 次结果一致（属性测试是随机的，专门确认没有偶发红）。

和了现在能被宣言并正确结束一个 Kyoku：Tsumo 后可宣言 Hora，他家 Dahai 后可宣言 Ron，
Furiten 挡住 Ron，同巡多家荣和按 Atamahane 裁决。**点数一律记 0**（08 票填）。

---

## 公开签名（本票新增 / 改动）

```fsharp
// Furiten.fs（新文件）—— 振听：永久与同巡分别维护
type Furiten = { Permanent: bool; Doujun: bool }
Furiten.none : Furiten
Furiten.blocksRon : Furiten -> bool          // 只挡荣和，从不挡自摸
Furiten.toDisplay : Furiten -> string

// Action.fs —— 加了两个 case
Action.Hora of actor: Seat * target: Seat * pai: Tile   // 自摸和 target = actor
Action.None of actor: Seat                              // mjai `none`：响应阶段的「过」

// Event.fs —— 加了一个事件与它的载荷
type Hora =
    { Actor: Seat; Target: Seat; Pai: Tile
      Fu: int; Fan: int; HoraPoints: int                 // ← 本票一律 0，08 填
      Deltas: int list; Scores: int list }
Event.Hora of hora: Hora                                 // wire: {"type":"hora", …}

// Ruleset.fs —— 加了一个规则开关
Ruleset.Atamahane : bool                                 // 四麻预设 true（票里定的默认值）
Ruleset.withoutAtamahane : Ruleset -> Ruleset            // 关掉 ⇒ 双响 / 三响都成立

// PlayerState.fs
PlayerState.furiten : PlayerState -> Furiten
PlayerState.isAgari : TileKindSet -> PlayerState -> bool             // 14 张自身成型（自摸和）
PlayerState.isAgariWith : TileKindSet -> Tile -> PlayerState -> bool // 13 张 + 一张（荣和）
PlayerState.waits : TileKindSet -> PlayerState -> Tile list          // 和了牌的牌种（听什么）
PlayerState.minogashi : PlayerState -> PlayerState                   // 见逃 ⇒ 同巡振听
PlayerState.refreshFuriten : TileKindSet -> PlayerState -> PlayerState // 重算永久振听
// draw 现在会清掉同巡振听（它的定义就是「到自己下次摸牌为止」）

// GameState.fs
type KyokuEnd = Hora of Hora list | Ryuukyoku of Ryuukyoku   // [<RequireQualifiedAccess>]
KyokuEnd.isRenchan : Seat -> KyokuEnd -> bool                // Oya 和了 / 流局时 Oya 听牌
type Phase = … | Ended of kyokuEnd: KyokuEnd                 // 原来是 Ended of Ryuukyoku
type AwaitingResponse =
    { Target: Seat; Pai: Tile
      Responses: LegalActions list      // ← **还没答复**的座位，答一家少一项
      Declared: Action list }           // ← 已宣言的响应，收齐后按优先级裁决
GameState.kyokuEnd : GameState -> KyokuEnd option
GameState.horas : GameState -> Hora list        // 双响时不止一条；不是和了收尾则为空
GameState.ryuukyoku : GameState -> Ryuukyoku option   // 语义不变（不是流局收尾就是 None）
type IllegalAction =
    | … | HoraTileMismatch of actor * target * pai | NotAgari of actor * pai
        | NothingToRespond of actor

// 给测试留的 internal 构造器（生产 API 不变，经 fsproj 的 InternalsVisibleTo 暴露）
Wall.ofOrdered : Ruleset -> Tile list -> Wall                       // internal
KyokuStart.createFrom : Ruleset -> KyokuContext -> Wall -> Result<…> // internal
GameState.startFrom : Ruleset -> KyokuContext -> Wall -> Result<…>   // internal
```

CLI 没有新子命令（票没要）。`janpo kyoku <种子>` 照跑——随机选手几乎永远和不了，
扫过 400 个种子一次和了都没有，因此 04 的种子化用例全部原样通过。

## 三处形状（09 / 10 / 11 直接复用）

### 1. Hora / Ron / 「过」在动作集里长什么样

| 阶段 | 座位 | 动作集 |
|---|---|---|
| 摸牌后（和了型成立） | 出手那家 | `[Hora(a, a, 刚摸进那张); Dahai 手切×n; Dahai 摸切]` —— **Hora 排最前** |
| 他家打牌后 | 每个够格的座位 | `[Hora(seat, target, 刚打出那张); None seat]` |

- 响应阶段**只列够格的座位**：不够格（振听、不成和了型）的座位压根不在 `legalActions` 里，
  因此「振听座位的 Ron 不出现在合法动作集」是结构上的事实，不是宣言后报错。
- **有任何响应就必然同时有一条「过」**（`Action.None`）——04 留下的死锁坑在这里填上了。
  10 / 11 往同一处加 Pon / Chi / Kan 时，「过」已经在了，照着 `responsesTo` 的 `List.choose` 加一支即可。
- 座位顺序是**打牌者下家优先**（`Seat.orderFrom ruleset (Seat.next ruleset target)`）。

### 2. Furiten 两种状态存在哪、谁负责更新

`PlayerState.Furiten : Furiten`（`{ Permanent; Doujun }`），两条更新路径互不相干：

| 状态 | 谁置位 | 谁解除 |
|---|---|---|
| `Permanent` | `PlayerState.refreshFuriten`，由 `applyDahai` 在**自家每次打牌后**调用 | 同一个函数——它是**重算**（当前听牌 × 自己的河），换听到不含自己打过的牌上就自动解除 |
| `Doujun` | `PlayerState.minogashi`，由 `applyResponse` 在**够格却选了「过」**时调用 | `PlayerState.draw`，自己下次摸牌时 |

`responsesTo` 只读 `Furiten.blocksRon`（两位或起来）。**自摸和不看振听**。
09 的立直振听（见逃 = 闩死）是这个记录上的第三种语义，由 09 决定加一位还是让重算跳过立直座位。

### 3. 多家响应的裁决顺序（09 / 10 / 11 要复用的就是这块）

```
Dahai → responsesTo 给出「够格的座位 × 各自的动作集」→ 停在 AwaitingResponse
      → 每收一家答复：从 Responses 去掉它，宣言（非「过」）压进 Declared
      → Responses 空了 ⇒ 裁决 Declared：
           ronWinners = Declared 里的 Hora，按「打牌者下家优先」重排；
                        Atamahane 开 ⇒ 取第一家；关 ⇒ 全取（双响 / 三响）
           有赢家 ⇒ applyHora（Ended KyokuEnd.Hora）
           没有   ⇒ drawNext（下家摸牌，或荒牌流局）
```

三条不变式，后面的票别踩：

1. **先被问到 ≠ 优先**。`Declared` 是答复顺序，裁决时按优先级重排。10 加 Pon / Chi 时，
   在 `ronWinners` 那一处按「Ron > Pon / Kan > Chi，再按座位」排，`Kyoku.run` 与 UI 一行都不用改。
2. **答复本身不是既成事实**：不是最后一份答复的那一步产出 0 个事件（属性钉住了这条）。
3. `Kyoku.run` 不必改成「先收齐再裁决」——收齐的状态机在 `GameState` 里，驱动只要每次问第一个待答座位。
   代价：顺序询问时，后答的那家能从局面里看见先答的宣言。真实牌桌是同时宣言的，
   隐藏这一点是观测投影（Observation Projection）的职责，不是 `step` 的。已记进 DECISIONS。

## 黄金用例怎么构造的

引擎给测试留了两个 `internal` 构造器（`Wall.ofOrdered` / `GameState.startFrom`），
测试固件 `GameStateFixtures.scriptedWall` 按 `Wall.deal` 的 4-4-4-1 手顺**反推**牌山：
给定四家配牌与摸牌顺序，剩下的位置用整副牌里没用掉的牌补满（用超四张会当场 failwith）。
于是「指定的和了在指定 Junme 发生」是确定的事实，不必碰运气找种子；黄金用例走的仍是
生产的发牌与开局路径。四份剧本写在 `GameStateFixtures` 里（`tsumoHoraScript` /
`ronFuritenScript` / `doubleRonScript` / `tripleRonScript`），属性测试的可达局面生成器也拿它们
掺进来——否则「以和了收尾」的局面一个都生成不出来（随机选手扫 400 个种子，0 次和了）。

## 测试（252 个全绿；本票新增 29 个）

`HoraTests.fs`（26 条）：

- **自摸和**：Hora 进动作集且排在打牌之前；**第 2 巡自摸和**（`junmeOf` 钉死巡数）产出
  `hora` 事件、进事件流、Oya 和了 ⇒ 连庄；和了收尾时 `ryuukyoku` 为 None；
  不成型宣言被拒（`NotAgari`）；来源 / 牌不对被拒（`HoraTileMismatch`）；摸牌后阶段的「过」被拒。
- **荣和与振听**：Ron 与「过」一起进对应座位的动作集；**振听座位不在被问之列且它确实是听牌的**
  （用 `PlayerState.isTenpai` 钉住「被排除是因为振听，不是因为没听牌」）；荣和结束一局、Ko 和了不连庄；
  见逃 ⇒ 同巡振听、自己摸牌后解除；同巡振听期间他家再打出和了牌**引擎压根不停**
  （用事件流 `[dahai; tsumo]` 钉死）；解除之后同一张牌又能荣和。
- **头跳与双响**：两家都能荣和时两家都进动作集；**头跳开 ⇒ 只成立下家优先的那家**；
  **头跳关 ⇒ 双响都成立且按下家优先排序**；三响两种开关各一条；
  头跳裁决的是实际宣言（优先那家见逃 ⇒ 靠后那家成立）；两家都见逃 ⇒ 接着打；
  答复过的座位不能再答复；响应阶段不接受打牌。
- **不是空转**：保听选手互相点炮的局面里确实出现响应阶段（扫 40 个种子），
  且每个被问的座位都不振听、都有「过」；摊好的两局各自以自摸和与荣和收尾。
- **渲染**：三条新拒绝理由与 `Furiten.toDisplay` 的四种取值。

`GameStateProperties.fs`（+3 / 改 2）：

- 新增：响应阶段等的每一家都不振听、和了型成立、且必有一条「过」与一条对得上牌的 Ron；
  和了收尾时点数一张不动（符 / 番 / 和了点全 0，`Scores` = 局初点数）。
- 改：「等着打牌的那家 14 张」⇒ 「等着打牌的那家 + **自摸和了的那家** 14 张，其余 13 张」
  （荣和的那张留在放铳者河里，因此荣和的那家仍是 13 张）；
  「合法动作集里的每个动作都推得动局面」⇒ 局面必变 + 事件流按返回值增长 +
  **当且仅当这一步收齐了答复**才产出事件（比原来的「事件非空」强）。

`GameStateGenerators.fs`：可达局面的生成器掺进两条「以和了收尾」的摊牌剧本；
`tenpaiSeeking` 明确成「从不宣言和了、响应一律过」（04 的听牌料黄金用例要它打到荒牌流局），
新增 `passive`（摸切 + 过）与 `horaSeeking`（见和就和）两个选手、`driveUntil` 驱动、
`scriptedWall` / `startScripted` / `junmeOf` / `tilesOf` 等固件。

`EventTests.fs` / `EventGenerators.fs`：`hora` 的 wire 形态一条，生成器加一支（JSON 往返属性自动覆盖）。

**变异验证**（改实现 → 跑测试 → 确认变红 → 改回）：

| 变异 | 变红 |
|---|---|
| 振听不再挡 Ron | 7 条（含振听、同巡振听、双响、非空转四组） |
| 头跳恒关（不取第一家） | 2 条 |
| 摸牌不解除同巡振听 | 2 条 |
| 响应动作集里去掉「过」 | 6 条（驱动直接卡住 → 固件 failwith） |
| 裁决顺序反过来（上家优先） | 3 条 |

另有一次**属性抓到的真 bug**：最初把「除「过」外必产出事件」写死，随后属性在双响局面上
证伪了它——**第一家宣言 Ron 时也不产出事件**（还没收齐）。属性写对之后连跑 4 次全绿。

## 关键取舍

| 取舍 | 选了什么 | 为什么 |
|---|---|---|
| 头跳 | 裁决**实际宣言**，Ron 进每个够格座位的动作集 | 优先那家见逃时靠后那家该成立；gimite/mjai 的 `process_hora` 同样遍历全部宣言并按 `distance(actor, target)` 排 |
| 响应阶段 | 逐家答复、`Declared` 累积、收齐再裁决 | `Kyoku.run` 与 UI 不用改；10 / 11 的优先级只改 `ronWinners` 一处 |
| 「过」 | 是一个 `Action`，不产出事件 | mjai wire 上没有 `none` 事件；它是答复不是事实 |
| 振听 | 两位分别维护：永久**重算**、同巡**摸牌解除** | 解除条件完全不同；合成一个 bool 就分不出来 |
| 振听的拒绝 | 不加 `FuritenRon` 错误 case | 振听在动作集里就被滤掉，那个 case 在 `step` 里永远不可达 |
| 终局形态 | `Phase.Ended of KyokuEnd`（DU） | 04 说好了「06 加和了时换成 DU」；12 补流局形态时不必再动这里 |
| 连庄 | `KyokuEnd.isRenchan : Seat -> KyokuEnd -> bool` | 05 只要一个布尔值；Honba / Kyotaku / 局数序列仍归 05 |
| 荣和的牌 | 留在放铳者的河里 | 牌数守恒（04 的属性）要求每张牌只在一处 |
| 点数 | 全 0，字段留好 | 票的明文要求；08 填 |
| 黄金用例 | `internal` 构造器 + 反推牌山 | 票的明文要求「别把生产 API 弄脏」；走的仍是生产发牌路径 |

## Review 结论（两轴，fixed point `439a056f`）

无法派生 sub-agent，按 RUNBOOK 自己顺序跑了两轴。

### Standards（对照 CONTEXT.md、ADR-0001/2/3、01/02/04 立的结构约定）

**已修（本轮自动修）**

1. 最初 `awaitingDahaiActions` 用 `PlayerState.isTenpai` 给 `waits` 做快路径过滤，等于在
   「和了型」判定上多压了一层 Shanten 的语义（03 的三条待确认修正正好动的就是那条边界）。
   改成一律走 `AgariShape.classify`（经 `PlayerState.isAgariWith`），全仓和了型判定仍只有一份。
2. 属性「除「过」外必产出事件」被属性测试自己证伪（见上），改成「当且仅当收齐答复才产出事件」。

**只记录不修（nitpick / 判断题）**

3. `stepAwaitingResponse` 的兜底分支里 `Action.None` 那一支不可达（待答座位的动作集必含「过」）。
   保留是因为它与 `Action.Dahai` 合并成一支，写 `failwith` 反而引入异常。
4. `KyokuEnd` 与 `Phase` / `GameState` / `IllegalAction` 同在 `GameState.fs`，是 04 立的
   「一概念一文件」的松解释（`KyokuStart.fs` 也是错误类型与操作同文件）。
5. `Furiten.Permanent` 是英文而不是罗马字——「永久振听」没有通行的罗马字短词。见提案 06-A。
6. `Ruleset` 加了 `Atamahane` 字段：07 若也动 `Ruleset`，集成时会在这里撞一次（记录以便调度器留意）。
7. `HoraTests` 的中文测试名断言的全是公开 API（`GameState.*` / `PlayerState.*` / `Furiten.*`），
   但**构造**局面走的是 `internal` 的 `GameState.startFrom`。这是票明文允许的测试构造入口。

**未发现**违反术语表或 ADR 的地方：标识符是罗马字或结构性英文；中文只在 `toDisplay`、注释与测试名里；
事件字段与 mjai wire 1:1；`Hora` 的 F# 拼法与 wire 拼法一致（裁决 D-1 这次没有出入要映射）。

### Spec（对照票 06 的 7 条验收 + spec.md 第 88 / 89 行）

7 条验收**全部满足**，逐条勾在票文件里。三处要说清楚的限定：

- 「Hora 进入合法动作集」**不判役**：无役不可和是 07 的事（票明说本票不消费 07）。
  因此本票的引擎会让一手无役的牌宣言和了——这是切片的已知边界，07 / 08 合并后由那两票收口。
- 「正确进局或连庄」落成 `KyokuEnd.isRenchan` 这个纯函数并有用例；**真正的进局动作**
  （Honba 递增、Kyotaku 结转、开下一局）是 05 的事，本票只把判据交出去。
- 「Honba / Kyotaku 的处理」：它们对点数的影响是授受的一部分 ⇒ 归 08（本票点数全 0）；
  对局面的推进 ⇒ 归 05。本票没有静默改动这两项。
- spec.md 第 88 行「两个阶段各自携带自己的合法动作集」仍然成立，且响应阶段第一次真的会停下来。
- spec.md 第 89 行的规则清单里，本票覆盖「同巡多家荣和优先级（开关，默认头跳）」与
  「振听（永久 / 同巡分别维护）」两项。

## 留给人的待审项

1. **头跳的默认值**（调度器提案 S-A）：票里定的默认是**开**，我按票实现，开关留好
   （`Ruleset.withoutAtamahane`）。若早上裁决翻成双响，改的是 `Ruleset.yonma` 的一个字段，
   `HoraTests` 里「头跳开 / 关」两条用例分别显式指定规则集，不会被这次翻转弄坏。
2. **三家和了**：本票在头跳关掉时让三响都成立；天凤把它判成途中流局。12 票已按提案 S-A
   把它列为规则集字段，届时会在 `ronWinners` 的裁决之后加一层。
3. **提案 06-A**：`Hora` / `Minogashi` / `Doujun` / `KyokuEnd` / `Renchan` 补进 CONTEXT.md；
   `Furiten.Permanent` 这一位要不要换成罗马字，一并裁。
4. **无役不可和**：见上，07 / 08 合并后要有人把「有和了型但无役 ⇒ Ron 不进动作集」这条接上。
   接口已经留好：`responsesTo` 里 `canRon` 的那个 `&&` 后面再加一项即可。
5. **顺序询问的信息泄漏**：响应阶段逐家询问，后答那家能从 `GameState` 里看到先答的宣言。
   引擎层这样最简单，隐藏它是观测投影的事——请在设计观测投影（M1）时确认这条。
6. **03 的听牌边界**：本票没有与它矛盾的用例（振听的听牌判定用 `PlayerState.waits`，
   走的是 `AgariShape.classify` 而不是 Shanten 的边界修正）。若早上裁决改了 `Shanten`，
   本票的黄金用例不受影响；受影响的仍是 04 的听牌料五条。
