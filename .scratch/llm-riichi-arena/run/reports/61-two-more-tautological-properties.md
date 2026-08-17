# 61 — 同族的另外两条恒真属性；`SeatStream` 进术语表；全仓库同族扫描

**Status:** ready-for-human　**fixed point:** `d0ea5662`（change `pqprlytm`）
**工作区:** `/home/xerxes2/janpo-ws-c`（jj workspace）
**改动面:** `tests/Janpo.Engine.Tests/DecisionPackageProperties.fs`、`tests/Janpo.Engine.Tests/ShantenProperties.fs`、`CONTEXT.md`（仅 `SeatStream` 一条）。**`src/` 一行没动**（三次弄坏补丁全部当场还原）。

**一句话**：票面那两条改成第三锚点后**各自红过两次**（弄坏 `absorb` / `forSeat` 各一弹）；
全仓库扫同族**新抓到第三条**——`ShantenProperties` 里字面上的 `shanten hand = shanten hand`，
已改成可红的（弄坏 deaka 法则当场红，旧写法在同一处弄坏下实测仍绿）；`SeatStream` 词条落地，
**其中前任写的一句假声称（「决策包增量取数」）对代码核实后改掉了**。

---

## 0. 接手时前任留下什么、验出什么

本票由中途死掉（provider 过载）的前一个 agent 开的头。接手时工作区里已有：
两条属性的改写、`SeatStream` 词条全文。**没有**：任何反向自证的输出、执行次数、同族扫描的痕迹。

逐项验证的结论：

| 前任遗留 | 验证结论 | 处置 |
|---|---|---|
| 两条属性的改写（右侧 `ObservationFixtures.mismatches`，保留原同源比对） | 形状正确、编译通过、基线绿 | **保留**；反向自证与计数由我补（§2、§3） |
| `SeatStream` 词条 | 逐句对代码核过：「O(1) 个字段」「牌桌增量取数」「63 倍」都有实据（`SeatStream.events`/`observation` 是公开口，`Table.fs:139/145/209` 增量维护，29a 报告实测表）；**但「牌桌与决策包都从它增量取数」是假的**——`DecisionPackage.forSeat` 调的是 `Observation.ofState`，那是**一次性全流 fold**（29a 明说决策包一手一次 0.9ms 走一次性入口） | **改写那一句**（§5），其余保留 |
| 同族扫描 | **完全没做** | 我做（§4），并抓到第三条 |

判据 2 的同一形状：词条声称「决策包增量取数」，而那件事的执行体不存在——
`forSeat` 每次被问都从头 fold。写进术语表就会误导后人以为决策包有增量路径。

## 1. 两条属性的改法

`DecisionPackageProperties`（前任改写，我验收）：

- **保留**原来的同源比对（它挡得住「两个字段建在不同座位/局面上」，票 60 §6 说清过），
- **另加**第三锚点：包里的观测（及包里的历史 fold 出来的观测）逐字段钉在**引擎的权威状态**上
  （`ObservationFixtures.mismatches`，票 60 新建；那一侧既不掩蔽也不 fold）。
- 报错用 `Prop.label` 点名座位、腿与字段；历史 fold 不出观测（`None`）也算一处分歧
  （包存在就说明引擎正在问这个座位）。

## 2. 反向自证（原始输出；`src/` 补丁当场还原）

### 2.1 弄坏 `SeatStream.absorb`（`WallRemaining - 1` → `- 2`）→ **两条都红**

票 60 §6 实测这两条在同一处弄坏下**全绿**；改写后：

```
失败 …DecisionPackageProperties.任意局面，包里的历史就是那条唯一的掩蔽流 [22 ms]
Falsifiable, after 1 test (0 shrinks)
Label of failing property: 共 1 处分歧，头几处是 座位 3 包里的历史 fold 出来的观测 vs 引擎状态：wall_remaining

失败 …DecisionPackageProperties.任意局面，包里的历史 fold 出来的就是包里的那份观测 [19 ms]
Falsifiable, after 1 test (0 shrinks)
Label of failing property: 共 1 处分歧，头几处是 座位 0 包里的观测 vs 引擎状态：wall_remaining
```

### 2.2 弄坏 `MaskedEvent.forSeat`（自家摸的那张也一并掩掉）→ **两条都红**

```
失败 …DecisionPackageProperties.任意局面，包里的历史就是那条唯一的掩蔽流 [66 ms]
Label of failing property: 共 2 处分歧，头几处是 座位 3 包里的历史 fold 出来的观测 vs 引擎状态：self.tehai；
  座位 3 包里的历史 fold 出来的观测 vs 引擎状态：self.tsumo

失败 …DecisionPackageProperties.任意局面，包里的历史 fold 出来的就是包里的那份观测 [20 ms]
Label of failing property: 共 2 处分歧，头几处是 座位 3 包里的观测 vs 引擎状态：self.tehai；
  座位 3 包里的观测 vs 引擎状态：self.tsumo
```

其余 5 条 `DecisionPackageProperties` 在两次弄坏下照旧（与它们的名字相称）。

## 3. 每条断言一次 CI 里执行了多少次（判据 3）

临时 `Interlocked` 计数器 + `ProcessExit` 落盘，跑一次完整 `dotnet test janpo.slnx`（718 + 101 全绿），
测完拆掉：

| 断言 | 一次 CI 执行次数 | 怎么来的 |
|---|---|---|
| 「历史 fold vs 包里的观测」＋「包里的观测 vs 引擎状态」 | **95** | 100 个局面 × 平均 0.95 个在被问的座位 |
| 「包里的历史 vs 唯一那条掩蔽流」＋「历史 fold 的观测 vs 引擎状态」 | **101** | 同上（各自独立取样） |
| `ShantenProperties` 新属性（§4.1） | **300**（其中 **204** 次手里有五，换红那条腿真的开口） | 模块 `MaxTest = 300` |

没有一条为 0。与三腿闸门的 15,428 不在一个量级是形态使然（这两条是单局面属性，
不是整局逐手驱动），与 `ObservationProperties` 的 400 同级。

## 4. 全仓库同族扫描（`tests/` 163 条属性 + `Janpo.Web.Tests` 6 文件 + `web/tests/` 16 文件）

判法照 judgments 开头第六例：凡断言「两种算法给出同一结果」的，逐条问**两侧是不是同一个实现**。

### 4.1 在族内（两侧同一实现）：**新抓到一条，已修**

`ShantenProperties.牌种集合与副露数不变时向听数是确定的`，全文是
`shanten hand = shanten hand`——**同一表达式求值两次**，对纯函数是全仓库最纯粹的恒真式。

改法：`HandShape` 类型注释里那条「红宝牌在构造时一律 deaka：`5mr` 与 `5m` 是同一个牌种」
此前**没有执行体**（判据 2），正好由它执行——同一把牌每张五写成红五、整把倒序，
经 `HandShape.create` 重新构造，向听数必须一个不差。

**反向自证**（弄坏 `Tile.create` 的 deaka 法则：红五挪到隔壁牌种）：

```
失败 …ShantenProperties.向听数只看牌种与副露数：五都写成红五、整把倒序重构，向听数不变 [36 ms]
Falsifiable, after 5 tests (0 shrinks)
```

同一处弄坏下把旧写法临时加回去同跑一遍：**16 条里唯一红的是新写法，旧恒真式照旧绿**
（总计 16 = 15 + 临时 1，失败 1）。临时属性与弄坏补丁均已拆除。

### 4.2 同形但名实相符（同一实现跑两遍**是它的本义**——决定性守卫）：不改

`GameStateProperties.同一种子跑两次得到同一局`、`回放确定性`（守的是动作记录簿而非算法一致）、
`RngProperties` 两条、`WallProperties.同一种子建出同一座牌山`、`KyokuStartProperties.同一种子…开出同一局`、
Web `TableTests.同一种子的两张牌桌逐手相同`。名字宣称的就是决定性，没有超卖。

### 4.3 貌似同族、核过确属两侧独立：清单与理由

| 断言 | 为什么两侧独立 |
|---|---|
| `MaskedStreamProperties` 三条腿、`ObservationProperties` 两条 | 票 60 已逐处反向自证 |
| `TableTests.牌桌推出来的一局与引擎自己跑的那一局逐条相同` | Table 驱动 vs `Kyoku` 循环，两个驱动器 |
| `TableTests.一整局逐手推进，增量维护的掩蔽流与重头 fold 逐手一致` | 票 60 §5 表格：弄坏增量侧它红 |
| `SoakTests.覆盖率的名字就是 mjai 的事件类型` | `Event.encoder` 与 `Coverage.toMjai` 两份手写字串表（注释自己说明了） |
| `YakuProperties.选中的读法就是番数最高的那一个` | 右侧在测试里**独立重述**选取判据（役满优先、番数取 max） |
| `KanProperties.剩余张数恒等于…`、`ScoreProperties.和了事件里的点数与授受自洽` | 右侧是独立算术式 |
| `GameProperties.下一局的局初点数就是上一局终了时的点数` | 存下的 `KyokuContext` vs 从 `PlayerState` 重算，两份存储 |
| JSON / 记法往返族（Event、Tile、开局事件、`kindIndex` 互逆等） | encoder/decoder 是成对的两个实现 |
| `prefix.test.ts` 「尾部的场况 ≡ 前缀数出来的」两条 | 左侧是**测试文件自带**的独立数数（`kawaFromHistory` 等），不是引擎 fold；弄坏 `absorb` 它红得了 |
| `naki-crosscheck.ts` | 注释明言不共用渲染器的 `wordsFor`，就是防这一族 |
| `ShantenOracleTests` / `PaifuDifferential` / `GoldenSuiteTests` | 外部 oracle / 真实牌谱 / 黄金 JSON（跨实现；票 59 地盘，没碰） |
| `TablePageTests` 的 `Assert.Equal(Fallback.action …, action)` | 两侧同函数，但断的是**接线**（页面确实派了兜底那一手），不是算法一致 |

其余 TS 文件（decide / endpoint / loop / record / redact / retry / template / render-version /
invariants / history / prompt）逐个过了：全是接线与固件断言，没有「两算法同一结果」形状。

**结论：全仓库该族清零**——票面那两条 + 扫出的第三条都已改成有第三锚点（或独立右侧）的形状。

## 5. `SeatStream` 词条最终文本（`CONTEXT.md`，本票唯一授权处；其余词条一字未动）

> **SeatStream（座席流）**：
> 某座位那条掩蔽事件流**fold 到此刻的累加器**：吃进一条事件改 O(1) 个字段，随时同时给得出两样东西——**那一家看得见的历史**（流本身，prompt 的可缓存前缀逐行渲染它）与**那一家此刻的观测**（同一条流的 fold）。**两者同出一源，这正是它存在的理由**：历史与场况不是两份互相独立的情报，因此不可能对不上。输入是掩蔽事件流，输出是 Observation。逐手推进的牌桌从它增量取数，不每帧从头重放（那是 O(n²)，实测慢 63 倍）；决策包一手只问一次，走一次性从头 fold 那条路径。
> 执行「同出一源」的是引擎侧这几道闸门：`MaskedStreamProperties` 的**三条腿**（增量维护 / 一次性 fold / 引擎的权威状态三方逐手一致）与 `DecisionPackageProperties` 那两条（包里的历史与包里的观测**各自**钉在引擎的权威状态上）。
> _Avoid_: 别把「一次性从头 fold」（`Observation.ofState` / `ofEvents`）当成另一件事——那是同一份语义的另一条实现路径（从空流起吃完整条日志），一致靠上面那几道闸门守着，**不是定义使然**；也别叫它「缓存」（缓存丢了可以重算，它是那条流的语义本身）。

与前任稿的差别只有一句：原「牌桌与决策包都从它增量取数，不必每帧从头重放（每帧重放是 O(n²)，实测慢 63 倍）」
改为现在的「牌桌增量取数；**决策包一手只问一次，走一次性从头 fold 那条路径**」（§0 的核实）。
词条声称的执行体（判据 2）：三条腿 + 本票那两条，全部在 §2 与票 60 §3 红过。

## 6. 数字

| | 基点 `d0ea5662` | 本票 |
|---|---|---|
| `./scripts/ci.sh` 墙钟 | 基线约 41s（票面）；票 60 实测 36.5–37.4s | **40.6s / 33.2s**（涨幅上限 ≤15% ≈ 47s，未超） |
| dotnet 测试条数 | 718 + 101 | **718 + 101**（一条没删，一条没加——三条都是就地改硬） |
| 属性用例数 | 每条 FsCheck 100（Shanten 模块 300） | **不变**（只许增不许减：没减） |

## 7. code-review（无法派 sub-agent，自跑两轴；fixed point `d0ea5662`）

**Standards 轴**（`fsharp-style.md` + 硬约束 + judgments）：只改测试与术语表。
新代码全是管道与具名中间值；新增 `let mutable` 0、循环 0；`dotnet fantomas .` 无一文件需要改写；
`check-style.sh` 在 CI 里全绿。**blocking：0。**

judgement calls，只记录不改：

- `anchored` 的 doc comment 有九行（前任写的），比模块惯例长；但它讲的正是「为什么右侧非得是
  引擎状态」这条来之不易的判据，删了后人会再犯。**留着**。
- `toDisplay` 在属性绿时也会求值（`Prop.label` 吃现成字符串）；每次拼一条短字符串，
  95–101 次/CI，开销不可测。**留着**。
- Shanten 新属性用 `List.rev` 而不是随机重排：FsCheck 属性要可复现，确定性变换足以覆盖
  「顺序无关」这半句（计数数组结构上就商掉了顺序，真正可红的是换红那半句）。

**Spec 轴**：票面十个复选框逐条对过（票文件已全勾）。唯一超出票面字面的动作是改词条里那句
假声称——票授权的是「加 `SeatStream` 词条」，词条内容写对是这个授权的应有之义；
**其余词条一字未动**（`jj diff CONTEXT.md` 只有那一段 +5 行）。

## 8. 留给人的待审项

1. **`Shanten` 的「确定性」现在没有单独的属性了**：旧那条名义上守决定性（实际恒真）。
   纯 F# 函数的决定性由语言语义保证（`Shanten.calculate` 无全局可变状态），新属性严格更强
   （同把牌两种写法两次求值仍要相等），我判断不缺；不同意就再立一条真的决定性守卫（例如跨线程重入）。
2. §4.2 那批决定性守卫（同一实现跑两遍）名实相符，我没动；若嫌它们弱，加硬的方向是
   给 `Rng`/`Wall` 找外部参照（已有 `PaifuDifferential` 在更高层盖住）。
3. 词条那句修正（决策包走一次性路径）若与将来「决策包也增量化」的计划冲突，改的该是代码不是词条。
