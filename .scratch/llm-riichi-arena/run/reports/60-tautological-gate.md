# 60 — 一条永远红不了的闸门：增量维护与一次性 fold 的一致性

**Status:** ready-for-human　**fixed point:** `200ecdd2`（change `qqrqnsyy`）
**工作区:** `/home/xerxes2/janpo-ws-c`（jj workspace）
**改动面:** `tests/Janpo.Engine.Tests/` 四个文件。**`src/` 一行没动**（弄坏用的补丁全部当场还原，`jj st` 只列测试文件）

**一句话**：那条属性是恒真式这件事**当场证实了**（弄坏 `absorb`、`advance`、`MaskedEvent.forSeat`
三处它一次都不红）；换成**三条腿的闸门**之后，弄坏增量侧、弄坏一次性侧、只弄坏掩蔽
**各红一次，且各点亮不同的两条腿**；一次 CI 里三条腿各执行 **15,428 次**。

---

## 1. 恒真式的原始证明

判据 1 的精神：「它失败过」才算证据。下面四份都是 `dotnet test` 的原始逐条输出
（`--filter` 取 `MaskedStreamProperties|MinogashiStreamProperties|ObservationProperties`，
基点 `200ecdd2`，四次弄坏都在 `src/` 里临时打补丁、当场还原）。

被证的是这两条（同一个 `incrementalAgrees`）：

```fsharp
let incrementalAgrees (state: GameState) : bool =
    let events = GameState.events state
    Seat.all ruleset |> List.forall (fun seat ->
        let incremental =
            (SeatStream.start ruleset seat, events)
            ||> List.fold (fun stream event -> SeatStream.advance event stream)
            |> SeatStream.observation
        incremental = Observation.ofEvents ruleset seat events)
```

### 1.0 基线：15 条全绿

```
已通过 …MaskedStreamProperties.任意局面任意座位，逐条推进与一次性 fold 给出同一份观测 [796 ms]
已通过 …MinogashiStreamProperties.见逃密集的局面上，逐条推进与一次性 fold 仍给出同一份观测 [858 ms]
已通过 …ObservationProperties.任意局面任意座位，掩蔽流 fold 出来的观测与引擎的状态逐字段一致 [130 ms]
（其余 12 条略，全绿）
```

### 1.1 弄坏 `SeatStream.absorb`（`WallRemaining - 1` → `- 2`）→ **那两条仍然绿**

```
失败 …ObservationProperties.见逃密集的局面上，掩蔽流 fold 出来的观测与引擎的状态仍逐字段一致 [37 ms]
失败 …ObservationProperties.任意局面任意座位，掩蔽流 fold 出来的观测与引擎的状态逐字段一致 [5 ms]
已通过 …MaskedStreamProperties.任意局面任意座位，逐条推进与一次性 fold 给出同一份观测 [767 ms]   ← 绿
已通过 …MinogashiStreamProperties.见逃密集的局面上，逐条推进与一次性 fold 仍给出同一份观测 [911 ms] ← 绿
```

### 1.2 弄坏 `SeatStream.advance`（整条 `Dahai` 当没看见）→ **那两条仍然绿**

```
失败 …ObservationProperties.见逃密集的局面上，…仍逐字段一致 [59 ms]
失败 …ObservationProperties.任意局面任意座位，观测的河与那一家的河逐张一致 [11 ms]
失败 …ObservationProperties.任意局面任意座位，观测里的每一张牌都是这个座位看得见的 [43 ms]
失败 …ObservationProperties.任意局面任意座位，他家的手牌张数与那一家实际的一致 [181 ms]
失败 …ObservationProperties.任意局面任意座位，…逐字段一致 [17 ms]
已通过 …MaskedStreamProperties.任意局面任意座位，逐条推进与一次性 fold 给出同一份观测 [751 ms]   ← 绿
已通过 …MinogashiStreamProperties.见逃密集的局面上，逐条推进与一次性 fold 仍给出同一份观测 [978 ms] ← 绿
```

### 1.3 弄坏 `MaskedEvent.forSeat`（自家摸的那张也一并掩掉）→ **那两条仍然绿**

```
失败 …MaskedStreamProperties.任意局面任意座位，只有自家那几条摸牌带着牌面 [16 ms]
失败 …ObservationProperties.见逃密集的局面上，…仍逐字段一致 [28 ms]
失败 …ObservationProperties.任意局面任意座位，…逐字段一致 [< 1 ms]
已通过 …MaskedStreamProperties.任意局面任意座位，逐条推进与一次性 fold 给出同一份观测 [774 ms]   ← 绿
已通过 …MinogashiStreamProperties.见逃密集的局面上，逐条推进与一次性 fold 仍给出同一份观测 [1 s]  ← 绿
```

### 1.4 弄坏 `SeatStream.advanceAll`（最后一条事件吃不进去）→ **红**

```
失败 …MaskedStreamProperties.任意局面任意座位，逐条推进与一次性 fold 给出同一份观测 [116 ms]     ← 红
失败 …MinogashiStreamProperties.见逃密集的局面上，逐条推进与一次性 fold 仍给出同一份观测 [157 ms] ← 红
（另有 5 条 ObservationProperties 红）
```

### 1.5 结论：它到底守了什么

| 弄坏哪里 | 那两条属性 |
|---|---|
| `SeatStream.absorb`（fold 的本体，约 150 行） | **绿** |
| `SeatStream.advance`（掩蔽 + fold 的入口） | **绿** |
| `MaskedEvent.forSeat`（全项目唯一那条掩蔽法则） | **绿** |
| `SeatStream.advanceAll`（**两行的包装**） | 红 |

它顶着「增量维护与一次性全流 fold 给出同一份观测」这个名字，**实际守的只有
`advanceAll` 那两行包装**：左侧手写 `List.fold advance`，右侧走 `ofEvents → advanceAll`，
两侧唯一不共用的就是这个包装。掩蔽、fold、增量维护本身，它一处也守不到。
票 58 §3.1 的判断因此**证实**，并且比它说得更细一格（58 没有区分 `advanceAll` 那一格）。

---

## 2. 改成真的能红：三条腿，右侧两两不同源

`MaskedStreamProperties.SeatStreamGate`（新）。一整局**从头逐手走完**，每一手在被看的那个座位上验三条腿：

| 腿 | 左 | 右 |
|---|---|---|
| **A 增量 vs 一次性** | `SeatStream.start` → 逐条 `advance`，**只吃 `GameState.step` 吐出来的 `produced`**（牌桌 `Table.played` 走的就是这条） | 每一手重新调 `Observation.ofState` |
| **B 增量 vs 引擎状态** | 左边那份观测 | **引擎的 `GameState`**，逐字段（`ObservationFixtures.mismatches`） |
| **C 一次性 vs 引擎状态** | 右边那份观测 | **引擎的 `GameState`**，逐字段 |

**为什么它归并不到同一个 fold 上去**：

- A 的两侧吃的**不是同一份事件表**——左边一次都不回头看 `GameState.events`，
  它只认引擎每一步交出来的那几条；右边读的是引擎的日志。
  「引擎的日志与它交出来的 `produced` 对不上」这件事在引擎侧**原来没有任何守卫**（见 §3.1）。
- B / C 的右侧是引擎的权威状态，**既不经过掩蔽也不经过 fold**。锚点不共享实现，
  因此不管 `absorb` / `advance` / `forSeat` 怎么坏，它都不会跟着一起坏。

覆盖面：**随机对局 × 随机座位 × 每一手**（票 29a 的原始要求）。
对局的名单与权重照抄 `GameStateArbitraries.tracesFor`（随机 / 听牌 / 副露 / 杠 / 立直各 4，
摊好的三个杠局 2+1+1，摊好的自摸和与双响各 1），另一条属性专跑见逃密集那一批。

**改后自己再验了一次它不是恒真式**：§3 的四份红输出全部是在**最终代码**上重跑的。

---

## 3. 三份反向自证（都在最终代码上重跑）

每一次弄坏点亮的是**不同的两条腿**——这本身就是三条腿彼此独立的证据。

### 3.1 弄坏增量侧 → A + B 红，C 绿

弄坏的是 `GameState.step`：日志照记全套，但**吐给调用方的那一份漏了打牌**
（这正是「牌桌接到的事件与引擎的日志对不上」那一类，票 58 在 Web 侧手工复现过的失效模式）。

```
失败 …MaskedStreamProperties.一整局逐手推进，增量维护、一次性 fold 与引擎的状态三方逐手一致 [78 ms]
失败 …MinogashiStreamProperties.见逃密集的一整局逐手推进，三方仍逐手一致 [338 ms]

Label of failing property: minogashi / 种子 28 / 座位 1：共 850 处分歧，头几处是
  第 1 手 A 增量 vs 一次性：整份观测；第 1 手 B 增量 vs 引擎状态：others.0.tehai_count；
  第 1 手 B 增量 vs 引擎状态：others.0.kawa；第 2 手 A 增量 vs 一次性：整份观测；
  第 2 手 B 增量 vs 引擎状态：self.tehai
Label of failing property: three-kan / 种子 97 / 座位 1：共 701 处分歧，头几处是
  第 4 手 A 增量 vs 一次性：整份观测；第 4 手 B 增量 vs 引擎状态：others.0.tehai_count；…
```

**引擎侧其余 13 条一条没红**（`ObservationProperties` 全绿）——这一类失效模式此前在引擎侧
完全无人守，只有 Web 侧 `TableTests` 的一颗种子碰得到。

### 3.2 弄坏一次性侧 → A + C 红，B 绿

弄坏的是 `Observation.ofState`（少吃最后一条事件）。

```
失败 …MaskedStreamProperties.一整局逐手推进，…三方逐手一致 [23 ms]
失败 …MinogashiStreamProperties.见逃密集的一整局逐手推进，三方仍逐手一致 [390 ms]

Label of failing property: minogashi / 种子 36 / 座位 2：共 341 处分歧，头几处是
  第 0 手 A 增量 vs 一次性：整份观测；第 0 手 C 一次性 vs 引擎状态：wall_remaining；
  第 0 手 C 一次性 vs 引擎状态：others.0.tehai_count；第 0 手 C 一次性 vs 引擎状态：others.0.junme；
  第 1 手 A 增量 vs 一次性：整份观测
Label of failing property: three-kan / 种子 169 / 座位 0：共 320 处分歧，头几处是
  第 0 手 A 增量 vs 一次性：整份观测；第 0 手 C 一次性 vs 引擎状态：wall_remaining；…
```

### 3.3 只弄坏掩蔽（`MaskedEvent.forSeat`）→ B + C 红，A 绿

```
失败 …MaskedStreamProperties.一整局逐手推进，…三方逐手一致 [49 ms]
失败 …MinogashiStreamProperties.见逃密集的一整局逐手推进，三方仍逐手一致 [74 ms]

Label of failing property: minogashi / 种子 35 / 座位 0：共 188 处分歧，头几处是
  第 0 手 B 增量 vs 引擎状态：self.tehai；第 0 手 B 增量 vs 引擎状态：self.tsumo；
  第 0 手 C 一次性 vs 引擎状态：self.tehai；第 0 手 C 一次性 vs 引擎状态：self.tsumo；
  第 5 手 B 增量 vs 引擎状态：self.tehai
Label of failing property: tenpai / 种子 117 / 座位 3：共 222 处分歧，头几处是
  第 4 手 B 增量 vs 引擎状态：self.tehai；…
```

**A 绿是对的、也是设计好的**：掩蔽坏了两侧一起坏，A 那条腿本来就分辨不出——
这正是原来那条属性恒真的原因。B / C 的锚点不经过掩蔽，因此它们开口。

### 3.4 （附）弄坏 `absorb` → B + C 红

原来那条属性**一次都红不了**的那处（§1.1），现在红：

```
失败 …MaskedStreamProperties.一整局逐手推进，…三方逐手一致 [11 ms]
失败 …MinogashiStreamProperties.见逃密集的一整局逐手推进，三方仍逐手一致 [210 ms]

Label of failing property: minogashi / 种子 33 / 座位 2：共 158 处分歧，头几处是
  第 0 手 B 增量 vs 引擎状态：wall_remaining；第 0 手 C 一次性 vs 引擎状态：wall_remaining；…
Label of failing property: tsumo-hora / 种子 379 / 座位 2：共 12 处分歧，…
```

---

## 4. 每条断言在一次 CI 里执行了多少次（判据 3）

在测试代码里临时插 `Interlocked` 计数器 + `ProcessExit` 落盘，跑**一次完整的 `dotnet test janpo.slnx`**
（718 引擎 + 101 Web，全绿），测完把计数器全部拆掉。

| 断言 | 一次 CI 的执行次数 | 怎么来的 |
|---|---|---|
| **A 增量 vs 一次性** | **15,428** | 200 局 × 每局每一手各一次 |
| **B 增量 vs 引擎状态** | **15,428** | 同上（逐字段，每次比 30+ 个字段） |
| **C 一次性 vs 引擎状态** | **15,428** | 同上 |
| `ObservationProperties.…与引擎的状态逐字段一致` | **400** | 100 个局面 × 4 个座位 |
| `ObservationProperties.见逃密集的局面上，…仍逐字段一致` | **400** | 同上 |

原始输出：

```
runs=200 turns=15428 A=15428 BC=30856
mismatches=31656
```

（`mismatches` 总调用 31,656 = 三方闸门的 B+C 30,856 + `ObservationProperties` 两条的 800。）

局数 200 = 两条属性 × FsCheck 默认 100 例；平均每局 **77.1 手**。
**没有一条为 0，也没有一条是「只在稀有形态里才开口」**——每一局的每一手都走它。

---

## 5. 现在到底有哪几条在守「座席的历史与观测同出一源」

按「它红过一次」逐条核过（红/绿全部取自 §1 与 §3 的原始输出）：

| # | 断言 | 在哪 | 弄坏 `absorb` | 弄坏 `forSeat` | 弄坏增量维护 | 弄坏 `ofState` |
|---|---|---|---|---|---|---|
| 1 | 一整局逐手推进，三方逐手一致 | `MaskedStreamProperties`（**本票新增**） | **红** | **红** | **红** | **红** |
| 2 | 见逃密集的一整局逐手推进，三方仍逐手一致 | `MinogashiStreamProperties`（**本票新增**） | **红** | **红** | **红** | **红** |
| 3 | 掩蔽流 fold 出来的观测与引擎的状态逐字段一致 | `ObservationProperties` | **红** | **红** | 绿 | **红** |
| 4 | 见逃密集的局面上，…仍逐字段一致 | `ObservationProperties` | **红** | **红** | 绿 | **红** |
| 5 | 一整局逐手推进，增量维护的掩蔽流与重头 fold 逐手一致 | Web 侧 `TableTests`（一颗种子、一局） | 绿 | 绿 | **红** | **红** |
| — | ~~逐条推进与一次性 fold 给出同一份观测~~ | 已被 1 / 2 取代 | 绿 | 绿 | — | — |

**票 58 说 `ObservationProperties` 那两条是真的——确认属实**（第 3、4 行；弄坏 `absorb`
与弄坏 `forSeat` 都当场红）。它们守的是「fold 出来的观测 = 引擎的权威状态」，
守不到的是**增量维护**那一半（第 3、4 行的第三列是绿）——那正是本票补上的空缺，
此前引擎侧一条都没有，只有 Web 侧那一颗种子。

**M1 的核心不变量现在有五条在守，其中三条覆盖增量维护那一半。**

---

## 6. 顺带发现：还有两条同形的空转属性（**只报不改**）

`DecisionPackageProperties` 里这两条是同一个形状（判据 17：只描述、不编号）：

| 属性 | 为什么它红不了 |
|---|---|
| `任意局面，包里的历史就是那条唯一的掩蔽流` | `DecisionPackage.forSeat` 里 `History = Observation.stream seat state`；属性比的是 `package.History = Observation.stream seat state`。**同一个表达式的两次求值** |
| `任意局面，包里的历史 fold 出来的就是包里的那份观测` | 左边 `ofMasked ruleset seat package.History` = `fold absorb (mask events)`；右边 `package.Observation` = `ofState` = `fold advance events` = `fold absorb (mask events)`。**同一个 fold** |

**实测证实**（`--filter DecisionPackageProperties`，两次弄坏各跑一遍）：

```
=== 弄坏 absorb（WallRemaining -1 → -2）===
已通过 …DecisionPackageProperties.任意局面，包里的历史就是那条唯一的掩蔽流 [142 ms]
已通过 …DecisionPackageProperties.任意局面，包里的历史 fold 出来的就是包里的那份观测 [138 ms]
（七条全绿）

=== 弄坏 MaskedEvent.forSeat（自家摸的那张也掩掉）===
已通过 …DecisionPackageProperties.任意局面，包里的历史就是那条唯一的掩蔽流 [370 ms]
已通过 …DecisionPackageProperties.任意局面，包里的历史 fold 出来的就是包里的那份观测 [149 ms]
（七条全绿）
```

它们不是全无用处：**能挡住「包把两个字段建在不同的座位 / 不同的局面上」**
（决策包是跨 F#→TS 接缝的那一份，字段错配是真实的失效模式）。
但它们的名字宣称的是「同出一源」，而那件事它们守不到。

**本票没有动它们**（不在票面范围内，且属于决策包那一块）。给人的建议是照本票的形状改：
把右侧换成引擎的权威状态（`ObservationFixtures.mismatches` 现成可用），或者干脆
把名字改成它真正守的那件事。

---

## 7. 改了什么

| 文件 | 改动 |
|---|---|
| `tests/Janpo.Engine.Tests/ObservationFixtures.fs`（**新**） | 从 `ObservationProperties` 抽出 `SeatFields` 与 `mismatches`（**一行判据没改**），好让三方闸门与原来那两条守卫共用同一份锚点 |
| `tests/Janpo.Engine.Tests/MaskedStreamProperties.fs` | 删掉恒真的 `incrementalAgrees`；新增 `KyokuRun`、`SeatStreamGate`（三条腿的驱动）与两个取样器；那两条属性改成一整局逐手推进的三方闸门 |
| `tests/Janpo.Engine.Tests/ObservationProperties.fs` | 只改成调 `ObservationFixtures.mismatches`，**两条属性的语义一字未改** |
| `tests/Janpo.Engine.Tests/Janpo.Engine.Tests.fsproj` | 编译顺序里加 `ObservationFixtures.fs`（排在 `MaskedStreamTests.fs` 前） |

**`src/Janpo.Engine/` 一行没改**——两侧成为独立路径**不需要动 `src/`**：
增量那一侧要的入口（`SeatStream.start` / `advance` / `observation`）与一次性那一侧的
`Observation.ofState` 早就都是公开 API，而第三个锚点是 `GameState` 自己。
票面允许的「必须动 `src/` 就先在 `DECISIONS.md` 说清」这一条**没有用上**。

## 8. 数字

| | 基点 `200ecdd2` | 本票 |
|---|---|---|
| `./scripts/ci.sh` 墙钟（同一台机器、同一轮，各跑两次） | 39.1 s / 39.8 s | **36.5 s / 36.4 s / 37.4 s** |
| dotnet 测试条数 | 718（引擎）+ 101（Web） | **718 + 101**（一条没删） |
| 属性用例数 | 每条 FsCheck 默认 100 | **每条仍是 100**（只许增不许减：没减） |
| 那两条的耗时 | 796 ms + 858 ms | 1 s + 2 s |
| 每条断言的执行次数 | A/B/C 三条都不存在；老属性 100 例 × 4 座位 = 400 次同一个 fold | **15,428 × 3** |

**墙钟没涨**（票面允许涨 15%，基线 41.9 s）。多出来的约 1.3 s CPU 被 xunit 的并行吃掉了；
本轮四次测量都在 36–40 s 区间，两组之间的差在噪声带内，**不宣称本票让 CI 变快了**。

---

## 9. code-review：Standards 一轴（fixed point `200ecdd2`）

无法派生 sub-agent，按 workbook 顺序自跑。**只改了测试**，因此对着
`docs/agents/fsharp-style.md` + `AGENTS.md` 硬约束 + `docs/agents/judgments.md` 过。

**blocking：0。** 已在 review 中自修的 4 处：

1. **Dead Code**：`SeatStreamGate.turns`（本来打算用它数执行次数，最后用 `Interlocked` 量了）
   —— **删掉**。判据 2 的同一形状：留着就是一段没人执行的记录。
2. **多余的公开面**：三条腿的名字（`incrementalVsOneShot` 等）没人从模块外读 → 改 `private`。
3. **从下往上读**：`divergences` 里 `seeded` 定义在 `loop` 之后、用在 `loop 0 …` 那一行
   —— 挪到 `loop` 之前，读起来是「先起流、再走局」。
4. **取样器里 `Gen.constant` 与 `Gen.fresh` 的分别**（票 56 踩过的坑）：这里载荷只是个名字、
   取样时一分钱不花，与 `GameStateArbitraries.tracesFor` 必须用 `Gen.fresh` 的理由正相反
   —— 就地写了一句注释，免得后人照抄错方向。

**规则逐条**：规则 1/3（不许从里往外读）—— 新代码全是管道与具名中间值，最深一处是
`ObservationFixtures.mismatches seat state each |> List.map (fun field -> …)`，两层；
规则 5（命令式边界）—— 新增 `let mutable` **0** 处、循环 0 处（`check-style.sh` 全绿）；
`dotnet fantomas --check .` 干净。

**judgement calls，只记录不改**：

- `MaskedStreamProperties.fs` 从 220 行涨到 428 行，装了三件事（见逃固件、三方闸门、掩蔽流的属性）。
  拆出 `SeatStreamGate.fs` 会更薄，但闸门要用同一个文件里的 `MinogashiFixtures`，
  拆了就要把见逃固件也搬走——那会让「掩蔽流的不变量」散在两个文件里。**留着**。
- `KyokuRun.Opening` 是字符串而不是 DU。**故意的**：FsCheck 报错时 `{ Opening = "riichi"; … }`
  比一个 DU 的 case 名更直接地告诉人「照这个名字重跑」，而 `openingOf` 的 `match` 末尾有
  `| other -> failwith`，写错名字当场炸。代价是编译器不替你穷举——
  这一处的名单只有十一个，且只有取样器一个调用方。
- 三条腿都在同一条属性里报，没有拆成三条独立的属性。拆了执行次数一样、报错更细，
  但要把一整局走三遍（`ofState` 那一段是三方里最贵的）。**留着**，报错的 label 已经点名是哪条腿。

**Spec 轴**：票面五个复选框逐条对过，`## 边界` 四条逐条对过，**缺失 0**（明细见票文件的勾选）。
唯一没用上的是「必须动 `src/` 就先记 `DECISIONS.md`」——**没动 `src/`**（§7）。

---

## 10. 留给人的待审项

1. **`DecisionPackageProperties` 那两条同形空转的属性要不要立票**（§6）。判据 17：我只描述不编号。
   现成的改法是把右侧换成 `ObservationFixtures.mismatches`，代价约十行。
2. **`SeatStream` 至今不在 `CONTEXT.md` 里**（29a-B 与票 58 的待审项 2 都提过）。
   本票让它成了引擎侧闸门明面上的主语（`SeatStreamGate`），这个词更该收进去了。我不许改术语表。
3. **判据清单里那句「本项目最常犯的一类错」现在有第六例了**：`docs/agents/judgments.md`
   开头写的是「已抓到五例」，本票是第六例、也是唯一一次「执行体存在但空转」。
   改那份清单需要授权，我没改，记在这里。
