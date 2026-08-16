# 29a — 掩蔽事件流成为座席的唯一投影，快照降为它的 fold

**Status:** done　**Change:** `unvvslns`（commit `9307eba9`）　**fixed point:** `9d4df416`（change `qozkmuso`）

## 一句话

`Observation` 不再从 `GameState` 直算了。局面先出事件流，事件流经**唯一的一条掩蔽法则**
（`MaskedEvent.forSeat`）变成那个座位亲眼看得见的历史，历史 fold 成 `Observation`。
类型一个字段没改，24 的 `Scaffold`、25 的 `Danger`、22 的牌桌一行没动。

```
GameState ─ mask ─► 掩蔽事件流 ─┬─► SeatStream.events   （座席的历史，29b 的 prompt 前缀）
                               └─► fold ─► Observation （牌桌 / 脚手架 / Danger，类型不变）
```

## 交出去的接口（29b 直接用）

| 想要什么 | 怎么取 |
|---|---|
| 一条事件在某座位眼里的样子 | `MaskedEvent.forSeat : Seat -> Event -> MaskedEvent option` |
| 一整条流 | `MaskedEvent.stream : Seat -> Event list -> MaskedEvent list` |
| 局面 → 某座位的历史 | `Observation.stream : Seat -> GameState -> MaskedEvent list` |
| 上帝视角那条流（围观 / M2 复盘） | `GodView.stream : GameState -> Event list`（= `GameState.events`，一条不掩） |
| 增量 fold 的累加器 | `SeatStream.start ruleset seat` → `SeatStream.advance (event) `／`SeatStream.advanceAll` |
| 纯 fold（只吃掩蔽事件，证明不偷看） | `SeatStream.absorb : MaskedEvent -> SeatStream -> SeatStream` |
| 历史 / 观测 | `SeatStream.events`／`SeatStream.observation : SeatStream -> Observation option` |
| 一次性从流拿观测 | `Observation.ofMasked ruleset seat masked`／`Observation.ofEvents ruleset seat events` |
| 局面 → 观测（**签名没变**） | `Observation.ofState seat state`（内部就是 mask + fold） |
| 牌桌那份（增量维护，不重头 fold） | `Table.observation seat table`／`Table.history seat table` |

`MaskedEvent` 只有三个 case：

```fsharp
type MaskedEvent =
    | StartKyoku of startKyoku: MaskedStartKyoku   // 配牌只有自家那一手（`Tehai: Tile list`）
    | Tsumo of actor: Seat * pai: Tile option      // 他家摸牌：看得见他摸了，看不见摸的是什么
    | Public of event: Event                       // 其余十四条 mjai 事件逐字段公开，原样带着
```

**`Public` 那一支是故意的**：`MaskedEvent.forSeat` 的 match 穷举 `Event` 的每一个 case，
新增 mjai 事件时编译器在那里报不完整，逼着加的人回答「这条事件有没有看不见的部分」；
而绝大多数事件不必在 `MaskedEvent` 里再抄一遍字段（`Event` 加一个 case 的代价仍然是三处）。
另一个好处：`MaskedEvent.publicEvent` 能把公开那一半原样喂回引擎里既有的判据
（`GameState.firstTurnFor` 就是这么共用的，两立直与天和地和不必写第二份）。

## 一个座位在某一手能看到的完整掩蔽流（真数据）

种子 13、`Ruleset.yonma`、座位 1 视角（它第一巡就两立直了）。155 条事件里摘四段，
**每一条都是真跑出来的**（`dotnet fsi` 直调 API 打印，脚本见本文末尾）。

### 开局到立直宣言（#0–#8）

```
  0  start_kyoku bakaze=1z kyoku=1 honba=0 kyotaku=0 oya=0 dora_marker=2z
                 scores=[25000; 25000; 25000; 25000]
                 tehai=2m 4m 5m 5m 5m 8p 9p 2s 3s 4s 4z 6z 6z      ← 只有自家那 13 张
  1  tsumo actor=0 pai=?                                            ← 亲摸了，牌面看不见
  2  dahai actor=0 pai=4z tsumogiri=false                           ← 手切
  3  tsumo actor=1 pai=3m                                           ← 自家摸的看得见
  4  reach actor=1                                                  ← 宣言
  5  dahai actor=1 pai=4z tsumogiri=false                           ← **宣言牌**（手切）
  6  reach_accepted actor=1                                         ← 立直成立，供托 +1
  7  tsumo actor=2 pai=?
  8  dahai actor=2 pai=2s tsumogiri=false
```

### 一次碰：时序、来源与鸣完打的那张（#42–#46）

```
 42  dahai actor=3 pai=1m tsumogiri=false
 43  pon   actor=0 target=3 pai=1m consumed=1m 1m     ← 座位 0 碰了座位 3 打的 1m
 44  dahai actor=0 pai=2s tsumogiri=false             ← 鸣完打的那张，**必然手切**
 45  tsumo actor=1 pai=4s
 46  dahai actor=1 pai=4s tsumogiri=true              ← 立直后全是摸切
```

### 见逃し：一个事件的缺席（#53–#57）

```
 53  tsumo actor=1 pai=1p
 54  dahai actor=1 pai=1p tsumogiri=true
 55  pon   actor=0 target=1 pai=1p consumed=1p 1p     ← 这一张**被要了**
 56  dahai actor=0 pai=2z tsumogiri=false
 57  tsumo actor=1 pai=9m                             ← #56 之后直接是摸牌 ⟹ 2z **谁都没要**
```

### 加杠与杠宝牌的揭示时机（#119–#123）

```
119  tsumo actor=0 pai=?
120  kakan actor=0 pai=1m consumed=1m 1m 1m           ← 明杠（加杠）
121  tsumo actor=0 pai=?                              ← 岭上补摸**先发生**
122  dora  dora_marker=1p                             ← 新宝牌欠到打牌那一刻才翻（票 16）
123  dahai actor=0 pai=9p tsumogiri=true
```

暗杠是另一种时序（`ankan` → `dora` → 岭上 `tsumo`），有一条具名用例钉住
（`MaskedStreamTests.杠宝牌的揭示时机在流里看得出来`）。

### 这条流 fold 出来的观测（终局那一刻）

```
self : junme=19  riichi=Accepted DoubleRiichi  furiten={Permanent=true; Doujun=false}
       tehai=2m 3m 4m 5m 5m 5m 8p 9p 2s 3s 4s 6z 6z
other seat=2 relative=1 junme=16 tehai_count=10 naki=1 riichi=None  kawa=2s 3m 9m 5z …
other seat=3 relative=2 junme=16 tehai_count= 7 naki=2 riichi=None  kawa=3p 9m 3z 3z …
other seat=0 relative=3 junme=19 tehai_count= 7 naki=2 riichi=None  kawa=4z 8p 7z 6p …
dora=2z 1p   wall_remaining=0   kyotaku=1
```

`Permanent=true` 是**从流里推出来的**：立直之后有一张自己的和了牌从别家河里过去了
（见逃 ⟹ 立直中的见逃是永久振听）。快照那套是引擎直接抄 `PlayerState.Furiten`；
现在这一位是座席自己的历史推的，两者逐字段对得上（见下）。

## 29b 要的那九项历史事实，分别怎么从流里读

| # | 事实 | 从流里怎么读 | 钉它的测试 |
|---|---|---|---|
| 1 | **每条打牌的巡目** | 那一家在这条 `dahai` 之前的 `tsumo` 条数（鸣牌不摸牌 ⟹ 鸣的那家巡不涨） | `巡目就是流里数自己的摸牌，与投影给的一致` |
| 2 | **跨家先后** | 流的顺序本身。两条 `dahai` 谁在前就是谁先打 | 掩蔽流与上帝视角流**逐条对齐**那条属性 |
| 3 | **手切摸切及其时序** | `dahai` 的 `tsumogiri` 字段 + 它在流里的位置 | `鸣的时序与来源都在流里` |
| 4 | **河与鸣的时序对齐**（第几巡被谁鸣走、鸣完打了什么） | `pon`/`chi`/`daiminkan` 的 `target` 指回打牌那家、`pai` 指回那一张；紧接着那条 `dahai` 就是鸣完打的 | 同上 |
| 5 | **立直宣言牌与宣言巡** | `reach` 之后那一家的第一条 `dahai` 是宣言牌；宣言巡是那之前它自己的 `tsumo` 条数 | `立直宣言牌与宣言巡` |
| 6 | **立直之后通过的牌** | `reach_accepted` 之后所有 `actor ≠ 立直者` 的 `dahai` | 立直后全摸切那条属性同源 |
| 7 | **见逃し**（谁都没要） | 一条 `dahai` 之后**没有** `pon`/`chi`/`daiminkan`/`hora` ——不必造新事件 | `见逃し不必造新事件` |
| 8 | **杠宝牌的揭示时机** | `dora` 事件在流里的位置：暗杠是 `ankan`→`dora`→`tsumo`，明杠是 `daiminkan`/`kakan`→`tsumo`→…→`dora`→`dahai` | `杠宝牌的揭示时机在流里看得出来` |
| 9 | **途中宣言** | `ryukyoku` 的 `reason`（`kyushukyuhai` / `sufonrenta` / `sukaikan` / `suchareach` / `sanchaho`） | `RyuukyokuReason` 既有用例 |

第 7 项的写法就是这一票的题眼：**它是一条事件的缺席**。快照里「2z 没人要」压根没有字段可放，
流里它是 `dahai(2z)` 的下一条不是鸣牌。

## expand → 闸门 → contract 三步

1. **expand**：新增 `MaskedEvent`（掩蔽）与 `SeatStream`（fold），与直算的
   `SeatProjection.masked` 并存；`Observation.ofStateDirect` 是直算那套的临时出口。
2. **迁移闸门**：`MigrationGate.fs` 两条属性，断言两种实现产出的 `Observation` 相等
   （随机对局 × 每一手 × 每个座位；另加一批**见逃密集**的轨迹，专打振听那一路）。
   **一次跑绿**，没有反例。
3. **contract**：删掉 `Observation.ofStateDirect` 与 `SeatProjection.masked`，闸门文件退役。
   **终局只剩一条掩蔽法则**：`MaskedEvent.forSeat`。全仓库再没有第二处回答
   「某座位能看见什么」。

闸门退役之后由**回归守卫**接手（`ObservationProperties.任意局面任意座位，掩蔽流 fold
出来的观测与引擎的状态逐字段一致`）：它比闸门更硬——闸门比的是「两种实现」，
守卫比的是「fold 出来的观测」与**引擎的权威状态**，而直算那套本来就只是 `GameState` 的誊写。
报错点名字段（`others.2.riichi` 这样），沿用裁决 21-c 的做法。

## 代价的数字（`dotnet fsi` 直调 API，Release 构建）

一局 95 手 / 160 条事件（种子 7，随机选手），单座位：

| 做法 | 一次 | 一整局逐手（95 次） |
|---|---|---|
| 直算 `GameState`（改前） | 0.068 ms | **0.46 ms** |
| 一次性 fold 全流（每手重头） | 0.906 ms | **29.0 ms** ← O(n²)，票里点名不许 |
| **增量 fold**（`SeatStream.advance`） | ~6 µs/条事件 | **0.56 ms** |
| 只掩蔽不 fold | 0.003 ms | — |

- **增量维护是必须的，不是加分项**：naive 做法比改前慢 63×，增量之后只慢 1.2×。
  牌桌（`Table.Views`）与决策路径都走增量；`Observation.ofState` 保留为一次性入口
  （黄金用例、CLI 与测试用它，一手一次，0.9 ms 无所谓）。
- fold 的开销大头是**见逃判据**（每条他家打牌都要问一次「我荣和得了吗」）。
  顺手给 `GameState.canRon` 加了一道 `PlayerState.isAgariWith` 的短路（型不成就不必构造牌姿、
  不必跑全套判役），一整局的增量 fold 从 0.93 ms 降到 0.56 ms，**引擎自己的 `responsesTo` 一起受益**。
  这道短路不改结果：`Score.best` 的每一条路都从 `AgariShape.classify` 起。

## 顺带钉住的两条不变量（票第四节）

- `立直成立之后那一家的打牌全是摸切（宣言牌除外）`——两批轨迹各一条属性。
- `碰或吃之后的那一张打牌必然是手切`（杠之后是岭上摸牌，允许摸切，因此不在此列）。

两条都是**事件生成的不变量**：违反即引擎产事件的地方有 bug。两条都一次跑绿。

## 值得人过一眼的三件事

1. **同巡振听的落地时机推迟到「这一轮响应收齐」**（`AwaitingResponse.Minogashi`）。
   原来是某家一答复就当场改 `PlayerState.Furiten`，而「我刚才过了」这件事**不是事件**——
   引擎的状态因此会领先座席看得见的历史一拍，两种实现在那一瞬间必然对不上。
   收齐再落之后两边严丝合缝，且行为不变：这一轮里没有任何判据读得到它
   （`responsesTo` 在这一轮开始前就跑完了），下一轮开始前它必然已经落定。既有用例全绿，一条没改。
2. **暗杠不在掩蔽之列**。票面第一节写「暗杠隐去牌面」，但日麻的暗杠亮着两张、牌种是公开信息
   （国士抢暗杠这条规则的前提就是它看得见），20 号票的 `MaskedSeat.Naki` 也一直把他家的暗杠
   原样给出来。按 RUNBOOK「找不到答案就选最贴近日麻通行规则、最不影响其他票的那一种」处理，
   记在 DECISIONS 29a-2。
3. **上帝视角没有跟着改成 fold**。它一张也不蔽，因此不属于掩蔽法则；而且里宝牌指示牌
   **压根不在事件流里**（它没翻开），`GodView` 只能从局面读。`GodView.stream` 给的是
   未掩蔽的事件流本身。

## 没做的（边界）

- **prompt 一行没动**（`web/src/agent/prompt.ts`）——那是 29b。这一票交的是「历史存在且可取用」。
- **`MaskedEvent` 没有 encoder**。29b 若要把历史送过 F#→TS 的接缝（决策包 JSON），
  需要一个 encoder；形状由 29b 定（前缀怎么切、渲染成什么），现在写就是替它做决定。
- 黄金用例**一个字段都没加**，`tests/fixtures/golden/dual-target.json` 一字未动
  （40 条用例 / 1947 个字段 / 3210 行，浏览器侧逐字段逐行仍然相同）——
  这正是「`Observation` 类型不变」的证据。

## 验证

- `./scripts/ci.sh` 全绿（1 分 17 秒）：fantomas --check、风格闸门、Fable 依赖白名单、
  759 条 dotnet 测试、Biome、浏览器内曳光弹对拍、浏览器内黄金用例 1947 字段、牌谱导出与回放。
- `cd web && pnpm run check` / `pnpm run typecheck` 干净。
- 新增测试：引擎侧 `MaskedStreamTests`（8 条具名）+ `MaskedStreamProperties`（8 条属性，
  含见逃密集轨迹那一组）+ `ObservationProperties` 新增 2 条回归守卫；
  Web 侧 `TableTests` 新增 3 条（增量维护的流与重头 fold 逐手一致、开下一局重置、历史与上帝视角等长）。

### 复现那份样例

```bash
dotnet build -c Release
dotnet fsi --exec /tmp/print-stream.fsx      # 脚本见本报告的「样例」一节，靠 scripts/fsi/load-engine.fsx
```

探针本身没有落进仓库（一次性的打印脚本，`scripts/fsi/` 现有的两个够用了）；
上面每一段真数据都能用 `Observation.stream (seat 1) state` 在 `dotnet fsi` 里三行复现。

## 两轴 code review（fixed point `9d4df416`，无法派生 sub-agent，顺序自跑）

### Standards（`docs/agents/fsharp-style.md` + CONTEXT.md + ADR + Fowler 基线）

**blocking：0**（已自动修的三处见下）。**已修**：

1. **管道进 lambda 且遮住外层同名参数**（`SeatStream.absorb` 的摸牌那一支写了
   `|> fun stream -> …`，`stream` 与 `absorb` 的参数同名）→ 改成具名中间值 `drawn` 与
   一个 `if`。规则 1 的「不许从里往外读」与遮蔽都不该出现。
2. **Duplicated Code**：「作废还没落定的立直宣言」在 `hora` 与三家和了两支各写了一遍
   → 抽成 `cancelDeclaredRiichi`。
3. **Duplicated Code（测试）**：「增量维护与一次性 fold 一致」在两个属性模块里各抄了一份
   → 抽成 `MaskedStreamProperties.incrementalAgrees`；
   **Data Clumps（测试）**：回归守卫里的 `seatFields` 收十个位置参数 → 收一个 `SeatFields` 记录。
4. 顺带删了没人用的 `SeatStream.viewer`（Speculative Generality）。

**judgement calls，只记录不改**：

- `Observation.fs` 涨到约 790 行，装了三件事（两个投影的类型、掩蔽流的 fold、两个 encoder）。
  拆出 `SeatStream.fs` 会更薄，但 `Observation.ofState` 必须调 fold，拆了就要把 `ofState`
  也搬过去——那会让「观测投影」这个概念散在两个文件里，与「一概念一文件」相悖。**留着**。
- `SeatStream.absorb` 约 150 行。它是对事件 DU 的穷举 match，与 `Event.encoder`、
  `GameState.step` 同形，是这个仓库的既有形状。**留着**。
- `GameState.yakuContextFor` 收 8 个参数（Long Parameter List / Data Clumps）。它们正是那个函数
  真正读的东西，而把它们打成一个记录就等于重新发明一个「局面的子集」类型；
  仓库里 `RiichiState.canDeclare`（6 个）、`allowsAnkan`（6 个）是同样的形状。**留着**。
- `MaskedEvent.forSeat` 返回 `option` 却现在恒为 `Some`（Speculative Generality）。
  票面点名要「（或不可见）」，且注释写清了它的定义域是「可见与否」。**留着**。
- 术语表缺「掩蔽事件流」的词条，`SeatStream` 也不在 `CONTEXT.md` 里 —— RUNBOOK 不许我改它，
  已记提案 29a-B。

### Spec（票 `29a-masked-event-stream-as-single-projection.md`）

**缺失或只做了一半：0。** 五节逐条对过，全部落地并有测试。

**偏离票面 1 处（已记 DECISIONS 29a-2）**：票面第一节写「暗杠隐去牌面」，实现没有掩它。
理由是日麻的暗杠亮两张、牌种公开（国士抢暗杠的前提），且 20 号票的投影一直把他家的暗杠
原样给出来——掩掉它 `Observation` 就变了，而同一张票要求「类型不变、下游一行不改」。

**票面没要求但改了的 3 处**（都记了 DECISIONS，都不是可选的顺手活）：

1. **同巡振听推迟到这一轮收齐才落**（29a-3）。不改的话迁移闸门必然红：「我刚才过了」不是事件。
2. **`GameState.canRon` 加一道型判定短路**（29a-7）。fold 的开销大头就是见逃判据；
   不加的话「别每帧重头 fold」那条要求即使做了增量也仍然贵一倍。结果不变。
3. **`Board.ofState` 改名 `ofTable`**（29a-8）。票面允许「接上 fold」；不改名就得让牌桌
   每帧重头 fold，正是票面点名不许的。外观一个像素没动。

**看着实现了但可能不对的**：无。唯一有分寸问题的是 29a-4 记的那道缝——头跳开着时
「宣言了荣和却被刷掉」在事件流里与「真的放过了」分不出来，fold 会记成见逃。
那一刻一局已终，同巡振听没有下一次摸牌可解除，也没有任何判据读它；默认规则集头跳是关的。

### 一句话

Standards 4 项（全部已修）+ 5 条 judgement 记录；Spec 0 缺失、1 处有据的偏离、3 处有据的额外改动。
两轴都没有 blocking 遗留。
