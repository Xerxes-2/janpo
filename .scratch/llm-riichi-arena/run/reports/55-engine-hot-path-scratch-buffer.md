# 55 — 引擎热路径：调用方持有暂存缓冲（B 级 + A 级顺扫）

**Status:** ready-for-human
**基点（code-review fixed point）:** commit `c6d3a734`（change `zrmpvsyv`）
**工作区:** `/home/xerxes2/janpo-ws-b`（jj workspace）
**改动面:** 只有 `src/Janpo.Engine/` 的 8 个文件；测试、`web/`、workflow、牌谱格式一律没动

---

## 0. 一句话

把「每次形态判定都新建一份 34 长计数数组」这件事去掉——缓冲改由**调用方**在进批之前建一个、批内共用。
每决策的 34 长数组从 **596.8 → 5.9 个**（`Scaffold.calculate` 口径）、整包从 **1268.5 → 106.8 个**；
`dotnet test` 的 CPU 总和 **478.7 → 403.7 秒**（−15.7%，32 核机器）；**浏览器侧 `Scaffold.calculate` 1.49×、整包 1.37×**。
语义一个字节没变：37.4 万行差分对拍、160 场 soak 事件流、40 条黄金用例 / 2069 字段、真牌谱对拍（固件 18 局 + 扩样本 60 局）全部逐行相同。

---

## 1. 改法逐条

### B 级：调用方持有暂存缓冲

**新的类型（`Shanten.fs`，`internal`）**

```fsharp
type internal ShantenScratch =
    { Search: int array    // 面子分解搜索原地增删的副本
      Dahai:  int array    // 「试打一张之后」的手牌计数
      Tsumo:  int array    // 「试摸一张之后」的手牌计数
      Seen:   int array }  // 可见张数（Ukeire）
```

**四个格不能互相借用**，因为它们在同一条调用链上同时活着：`Scaffold` 的试打结果要活到 `Ukeire` 算完，
而 `Ukeire` 在它上面又要自己的试摸与可见张数，最里层才是搜索。这条在代码注释里写着。

**新的带缓冲入口**（全部 `internal`）：

| 新增 | 公开的那一支怎么办 |
|---|---|
| `Shanten.calculateWith (scratch) kindSet hand` | `Shanten.calculate` 原样保留，自己新建一份 34 长缓冲后走同一份 `calculateIn` |
| `Shanten.standardIn (search: int array) kindSet hand` | `Shanten.standard` 同上 |
| `Ukeire.calculateWith (scratch) kindSet (known: Shanten option) visible hand` | `Ukeire.calculate` 同上（`known = None`） |
| `AgariShape.classifyIn (search) kindSet hand` | `AgariShape.classify` 同上 |
| `HandShape.countsInto (destination) hand` | 新增（`internal`），把计数拷进调用方的缓冲 |
| `HandShape.ofScratch nakiCount counts` | 新增（`internal`），把缓冲**原样**包成形态，不复制也不校验 |

**建缓冲的位置（进批之前建一个）**：`Scaffold.calculate`、`Ukeire.calculate`、`RiichiState.tenpaiDahai`、
`AgariShape.waits`、`RandomPlayer.shantenByKind`。**一共五处，全在这份 diff 里。**

**免掉的全量重校验**：批内的试打/试摸由调用方先自己挡一下（试打要求手里真有那张且手牌是已摸形，
试摸要求那个牌种不足 4 张且手牌是等摸形），于是 `HandShape.add` / `remove` 的 `ofCounts` 全量校验在批内不必再跑。
**两侧的拒绝条件是逐条对上的**：

| 原来 `HandShape` 返回的错 | 现在调用方挡的那一条 |
|---|---|
| `TileNotInHand` | `HandShape.countOf pai hand = 0` |
| `ConcealedCountMismatch`（等摸形再打一张 / 已摸形再摸一张） | `HandShape.isAwaitingDraw` 的正反两面 |
| `TileKindOverflow`（第 5 张） | `drawn.[index] >= 4` |
| `NakiCountOutOfRange` | 走不到：`hand` 已经是构造成功的 `HandShape` |

### A 级：顺扫

| 改法 | 位置 |
|---|---|
| 七对子的「有牌的牌种数 / 成对的牌种数」两遍 `Array.sumBy` → 一遍尾递归扫描 | `Shanten.chiitoitsu` |
| 国士的 `sumBy` + `exists` 两遍 → 一遍尾递归扫描 | `Shanten.kokushi` |
| `deadQuadKinds` 的「预算索引数组 + `Array.sumBy` 闭包」→ 一遍尾递归扫描（少 34 次委托调用，`allKindIndices` 整个删掉） | `Shanten.deadQuadKinds` |
| `canJoinRun` 里的 `[ a .. b ] \|> List.exists` → 尾递归，不分配小 list | `Shanten.canJoinRun` |
| `Ukeire` 收「已知的当前向听」，去掉它内部那次重算 | `Ukeire.calculateWith` + `Scaffold.trials` |
| `TileKindSet.kinds` 与 `count` 存成构造时的派生字段 | `TileKindSet` |

**尾递归全部编成了循环**（.NET 与 Fable 两边都是），因此**没有新增 `let mutable`**：
`scripts/check-style.sh` 的预算仍是 2，闸门没动过。

### 顺带做了但票面没点名的两处（都是同一形状，都在 `src/Janpo.Engine/`）

**`AgariShape.waits` 与 `RandomPlayer.shantenByKind`。** 理由是量出来的（判据 13）：
分段计时说每决策 1268.5 个 34 长数组里，**`Observation.ofState` 一个人占 671.8 个**，比 `Scaffold.calculate`（596.8）还多——
它每次重放都要按自家每一次打牌重算一遍永久振听，而振听走 `PlayerState.waits → AgariShape.waits`，
那是一批 34 次形态判定、每次新建两个数组（`HandShape.add` 一个 + 搜索副本一个），一次 `waits` 就是 68 个。
**票面预估的「≈772 个/决策」只数到了 `Scaffold` 那一支；真实分布里另有一支同样大。**

---

## 2. 数字（每一格都注明在几核机器上量的）

**机器**：AMD Ryzen 9 9950X3D，**32 逻辑核 / 16 物理核**，91 GB 内存。
**dotnet 10.0.302，node v26.7.0。** 跑批期间机器上有别的 agent 在跑构建（load average 5–19），
因此所有对比**都是交错跑的**（before/after 轮换），并且报区间与中位，不报单值。
**远端 CI 是 4 核**，本报告里没有一个远端数字——远端时间由 CPU 总量决定，见 §2.2 的 CPU 列。

### 2.1 每决策的 34 长数组新建次数（怎么数的）

**数法**：把整棵树复制两份到 `/tmp`（改前 / 改后各一份），在每一处新建 34 长数组的语句前加一行计数
（`AllocProbe.counts34 <- AllocProbe.counts34 + 1`），编译后用 `dotnet fsi` 引真 DLL 跑真决策点。
**仓库源码里没有这个探针**（`scripts/fsi/README.md` 的「复制到 /tmp 改副本」那条做法）。

被计数的位置（改前）：`HandShape.create` / `add` / `remove`、`Shanten.standard` 的 `Array.copy`、
`Ukeire.calculate` 的 `Array.copy`、`Danger`、`AgariHand`、`MentsuBreakdown` 各自的 `zeroCreate`。
改后同一批位置，外加 `ShantenScratch.create` 记 4 个、三个公开入口（`Shanten.standard` / `calculate`、
`AgariShape.classify`）各记 1 个。

**语料**：种子 1–12 各打一局，取每一手「有人被问」的局面共 **1079 个决策点**（试打候选平均 8.1 张）。

| 口径 | 改前 | 改后 | 倍率 |
|---|---|---|---|
| `Scaffold.calculate`（全部被问的手） | **596.8 / 决策** | **5.9 / 决策** | **101×** |
| `Scaffold.calculate`（打得出牌的手，916 个） | 690.0 / 决策 | 5.9 / 决策 | 117× |
| `Observation.ofState` | 671.8 / 决策 | 101.0 / 决策 | 6.7× |
| **`DecisionPackage.forSeat`（整包）** | **1268.5 / 包** | **106.8 / 包** | **11.9×** |

改后 `Scaffold` 那 5.9 个 = `HandShape.create`（1）+ `ShantenScratch.create`（4）+ `Danger`（0.9）。
**票面预估的 ≈3 个是对同一件事的低估一点点**：缓冲是四格不是一格（理由见 §1）。

### 2.2 `dotnet test` 的墙钟与 CPU 总和（32 核，交错跑 6 轮）

命令：`dotnet test janpo.slnx --configuration Release --no-build --logger trx`，
CPU 用 `/usr/bin/time` 的 `user + sys`，另按票面要求统计 trx 里 duration 之和。

| | 墙钟（中位 / 区间） | **CPU = user+sys**（中位 / 区间） | trx duration 之和（中位） | 用例数 |
|---|---|---|---|---|
| 改前 | 51.5 s（50.8–53.5） | **478.7 s**（457.6–491.3） | 430.6 s | 818 |
| 改后 | **44.0 s**（43.4–53.3） | **403.7 s**（390.7–443.2） | **354.4 s** | 818 |
| 降幅 | **−14.6%** | **−15.7%** | −17.7% | 0 |

改后区间上端那个 53.3 s / 443.2 s 是第 2 轮：当时另一个工作区在跑 CI（判据 16 的同一台机器噪声），
其余五轮全在 43.4–47.0 s。**trx 的 duration 是每条用例的墙钟，因此它比 `user+sys` 更容易被别人的负载污染**——
两个都报，判断以 CPU 列为准。

**最慢的那几个模块（trx 中位，秒）**

| 模块 | 改前 | 改后 | 降幅 |
|---|---|---|---|
| GameStateProperties | 49.1 | 41.4 | 15.8% |
| SoakTests | 44.5 | 36.2 | 18.8% |
| ObservationProperties | 41.6 | 33.7 | 19.1% |
| DecisionPackageProperties | 39.4 | 34.4 | 12.6% |
| MaskedStreamProperties | 36.1 | 30.5 | 15.4% |
| DangerProperties | 31.8 | 23.4 | 26.5% |
| ScaffoldProperties | 26.8 | 21.0 | 21.6% |
| RyuukyokuProperties | 24.2 | 20.1 | 16.9% |
| （合计 818 条） | 435.9 | 371.9 | **14.7%** |

`ScoreProperties`（13.6 → 15.4）与 `FallbackTests`（17.8 → 18.2）看着变慢了：**这两条都在噪声带里**，
它们几乎不碰形态判定的批路径（分数与兜底），中位数被别的 agent 的负载抬了一下。

### 2.3 浏览器侧（**产品的实际形态**，node v26.7.0 跑 Fable 产物）

**先验掉研究文档 §3.4 那条悬空的前提**：`dotnet fable --help` 里 `--typedArrays` 仍然默认 true，
`web/src/generated/Janpo.Engine/HandShape.js:90` 是 `new Int32Array(34)`。**前提成立。**
本机实测的原语价（node v26.7.0，2 000 000 次 × 3 轮）：

| 形状 | 本轮实测 | 研究文档 §3.2（node 22） |
|---|---|---|
| `new Int32Array(34)` | **815–963 ns** | 661–1437 ns |
| `src.slice()`（Int32Array） | 714–877 ns | 807–1968 ns |
| `Array.blit` 编出来的 `dst.set(src.subarray(0,34))` | **18.5–20.7 ns** | —— |
| `dst.set(src)` | 7.9–8.1 ns | 17.0–18.8 ns |

**结论：`Array.blit` 已经比新建便宜 40 倍**，剩下的 12 ns/次（`subarray` 那个视图）每决策只值约 5 µs，不值得为它写不安全的发射技巧。

同一份决策语料（1079 个决策点），改前 / 改后各跑 5 轮：

| 口径 | 改前 | 改后 | 倍率 |
|---|---|---|---|
| **`Scaffold.calculate`** | **1272–1343 µs/决策** | **857–869 µs/决策** | **1.49×** |
| **`DecisionPackage.forSeat`（整包）** | **2881–2989 µs/决策** | **2114–2172 µs/决策** | **1.37×** |

`Scaffold` 一项省下的 ~430 µs 与研究文档 §3.3(a) 的外推（597 个数组 × ~900 ns ≈ 0.54 ms）**是一个量级、方向一致**。
**浏览器侧的收益确实比 .NET 侧大**（1.49× vs 1.15×），研究文档那条主张成立。

### 2.4 .NET 侧的同口径（32 核，交错两轮）

| 口径 | 改前 | 改后 | 倍率 |
|---|---|---|---|
| `Scaffold.calculate` | 233–258 µs/决策 | 205–209 µs/决策 | ~1.15× |
| `DecisionPackage.forSeat` | 550–669 µs/决策 | 497–511 µs/决策 | ~1.15× |
| `Shanten.calculate` 单次（10 660 手真对局手牌） | 0.58–0.73 µs/手 | 0.51–0.59 µs/手 | ~1.12×（A 级那部分） |
| `janpo soak 1 60`（60 场覆盖型偏好） | 3.34–3.38 s | 3.18–3.32 s | ~1.03× |

**`soak` 只降 3%**：它不建决策包，热的是状态机而不是脚手架。这条数字放在这里是为了别让人把 15% 当成「引擎整体快了 15%」。

### 2.5 分段计时（判据 13：先量再归因）

`DecisionPackage.forSeat` 拆开量（.NET，32 核）：

| 段 | 改前 µs/决策 | 改后 µs/决策 | 改前数组/决策 | 改后数组/决策 |
|---|---|---|---|---|
| `GameState.legalActions` | 0.1 | 0.0 | 0 | 0 |
| **`Observation.ofState`** | **316.6** | **282.7** | 671.8 | 101.0 |
| `Observation.stream` | 1.7 | 1.7 | 0 | 0 |
| **`Scaffold.calculate`** | **234.5** | **204.4** | 596.8 | 5.9 |
| `Danger.rank` | 10.9 | 11.9 | 0.9 | 0.9 |
| 整包 | 539.2 | 487.6 | 1268.5 | 106.8 |

**两件事值得记进账**：

1. **`GameState.legalActions` 是免费的**（0.1 µs）——合法动作集在 `step` 里就算好存进 `Phase` 了。
   研究文档 §2.2 把 `tenpaiDahai` 记成「立直合法性判定那一支」是对的，但那笔钱花在 `GameState.step` 里，不在决策包里。
2. **`Observation.ofState` 比 `Scaffold.calculate` 还贵**，而前两轮研究一行都没读过它（研究文档 §7 自己列着）。
   它现在（改后）剩下的 282.7 µs 基本是「每问一次就把整条掩蔽事件流重放一遍」，那是 D 级（增量状态）的地盘，本票没碰。

---

## 3. 语义没改：四类证据

### 3.1 `./scripts/ci.sh` 全绿（最终版跑于最终提交内容）

- `dotnet test`：**818 条全绿**（引擎 717 + web 101），含全部属性测试（`Parallelism = 4/8` 的那四个模块照旧并发跑）
- **黄金用例：40 条用例、2069 个字段、3378 行**，浏览器里的引擎与 `tests/fixtures/golden/dual-target.json` 逐字段逐行相同
- **真牌谱对拍零差异**：`PaifuDifferentialTests` 的 18 局 / 177 kyoku 固件全绿（动作序列、役种集合、符、番、和了点、流局形态、逐座位清算）
- **双目标对拍逐字相同**：浏览器内曳光弹与 dotnet 侧逐项对照通过
- prompt 的 11 条语义不变量 + 1 道对拍 + 14 个反向自证全部当场证明咬得动
- `dotnet fantomas --check` 干净、`scripts/check-style.sh` 通过（`let mutable` 预算仍是 2）

### 3.2 扩样本真牌谱对拍（票面之外多加的一道）

`JANPO_PAIFU_DIR=/tmp/janpo-55/paifu-big`（`data/paifu/raw` 的 **60 局** + `data/paifu/oracle` 的 60 份天凤 JSON），
改前树与改后树各跑一次 `PaifuDifferentialTests`：**两侧都是 12 条全绿、零差异。**

**这道闸门的反向自证**（判据 1）：把 `JANPO_PAIFU_DIR` 指到一个不存在的目录，
当场红 4 条并打出 `/tmp/janpo-55/does-not-exist：语料目录下没有 mjai/ 子目录`——
证明那个环境变量真的被读了，60 局是真跑了，不是悄悄回退到 18 局固件。

### 3.3 37.4 万行的逐行差分对拍（`/tmp` 上的一次性探针）

一份 `.fsx` 探针把形态判定这一路的**全部输出**逐行打出来，改前树与改后树各跑一次，`cmp` 必须静默。

| 行类 | 条数 | 内容 |
|---|---|---|
| `S` | 224 180 | `calculate` / `standard` / `chiitoitsu` / `kokushi` / `AgariShape.classify` / `AgariShape.waits` |
| `U` | 84 180 | `Ukeire.calculate` 的向听、总枚数、牌种数与逐牌种剩余枚数（含各种错误分支的中文原文） |
| `T` | 56 180 | `RiichiState.tenpaiDahai`、`PlayerState.isTenpai` / `waits` / `furiten` |
| `P` | 3 545 | **整份决策包的 JSON**（脚手架里的每一个数） |
| `E` | 6 092 | 事件流 |
| 合计 | **374 177 行 / 72 MB** | **`cmp` 静默：逐字节相同** |

语料两来源：**(A)** 脚本自带的 xorshift 造的 21 万手手牌，四麻 13/14 张、满张偏置、三麻牌种集、只有 12 种牌的小规则集，
副露数 0–4 全覆盖——**故意不经引擎的 `Rng`**，免得语料本身跟着改动一起变；
**(B)** 种子 1–40 的真对局，每一手四家的手牌形态各判一遍，顺带把牌局本身也对了一遍。

### 3.4 改前改后各跑一次 soak，同种子事件流逐条相同（票面说的「最硬的那道」）

`janpo game <seed>` 打的就是完整事件流。四种选手 × 种子 1–40 = **160 场**，
外加三种选手的 `janpo soak 1 60`（各 60 场、共 180 场的覆盖率报告与问题清单）：

```
103 414 行，cmp 静默 —— 事件流逐条相同、覆盖率计数逐项相同、问题清单同为「无」
```

（跑了两遍：一次在实现完成后，一次在 code-review 的修改之后，两次都是 `cmp` 静默。）

### 3.5 没有发现任何数值分歧

**一处都没有。** 上面四类证据里没有任何一格出现过差异，因此没有触发票面那条「停下来」。

---

## 4. code-review 两轴（fixed point `c6d3a734`，无法派生 sub-agent，按 RUNBOOK 顺序自跑）

### Standards 轴

依据 `docs/agents/fsharp-style.md`、`CONTEXT.md`/ADR-0001、`scripts/check-style.sh`（机械项交给工具，不重复报），
外加 Fowler 味道基线。

| # | 结论 | 说明 |
|---|---|---|
| 1 | **通过**（规则 5：命令式代码的允许边界） | 新增的原地读写全部包在纯接口后面，且每一处都注明了性能理由并指向研究文档的节号。**没有新增 `let mutable`**（尾递归编成循环），预算仍是 2 |
| 2 | **通过**（规则 1/2/3：不许从里往外读） | `Shanten.value (Shanten.calculateWith …) = 0` 是**沿用原样**的两层「谓词套取值器」，规则 4.1 明写 boolean 条件里保持两层是可以的；`classifyIn … \|> List.isEmpty \|> not` 沿用 `AgariShape.isAgari` 的管道形状 |
| 3 | **判断题**（ADR-0001 标识符） | `ShantenScratch` / `Search` / `Seen` 是英文机制词。`Dahai` / `Tsumo` 两格是罗马字术语；类型是 `internal`，不上 wire、不进 UI 与 prompt。先例：`HandShape`、`TileKindSet`、`MentsuBreakdown`、`KawaTaken`（后者 `CONTEXT.md` 自己标着「本项目自造」）。**`CONTEXT.md` 我不许改**，因此作为提案记进 `DECISIONS.md` 由人裁决 |
| 4 | **已在 review 中修**（味道：Middle Man） | 第一版的 `Ukeire.compute` 与 `Ukeire.calculateWith` **签名完全相同**，后者纯粹转手——合并成一个。其余的 `standard` / `calculate` / `classify` / `Ukeire.calculate` 也是一行转手，但那正是票面要求的「公开纯函数签名保留」：那一行做的事是「自己新建一份缓冲」，留着 |
| 5 | **通过**（味道：Data Clumps / Speculative Generality） | `ShantenScratch` 收拢的正是「四个总是一起走的 34 长数组」这个 clump；四个格各有调用方，三个 `*In` 入口各有 ≥2 个调用方，没有为将来预留的钩子 |
| 6 | **留给人看的一条**（判据 2：谁执行这条不变量） | 代码里写下了「包出来的这个值不得越出批的边界」。**现在执行它的是**：`internal` 可见性（全引擎五个调用点，全在这份 diff 里）+ CI 里的常驻闸门（属性测试并发跑、黄金 40 条、真牌谱对拍、soak）——缓冲一旦别名或逃逸，这些当场出错数。**没有一道专门的静态闸门**；加法很便宜（`scripts/check-style.sh` 里给 `ofScratch` 记一条出现预算，形状与 `let mutable` 预算一样），但那个文件不在本票的改动面上，作为提案记进 `DECISIONS.md` |

### Spec 轴

| 票面要求 | 结论 |
|---|---|
| B 级：`Shanten.calculateWith (scratch) kindSet hand` 这类形状 | **做到**（`internal`，理由见 DECISIONS 55-2） |
| 缓冲由 `Scaffold.calculate` / `Ukeire.calculate` / `tenpaiDahai` 在进批之前建一个 | **做到**，另加 `AgariShape.waits` 与 `RandomPlayer.shantenByKind`（见「多做的」） |
| 公开纯函数签名保留、纯度与并发不变、缓冲是显式入参不是全局状态 | **做到**：`Shanten.calculate/standard/chiitoitsu/kokushi`、`Ukeire.calculate`、`AgariShape.classify/isAgari/waits`、`RiichiState.tenpaiDahai`、`TileKindSet.kinds/count` 的签名一个字符没动；缓冲一律在函数内部新建或由参数传入，没有任何 module-level 可变状态 |
| `HandShape.add` / `remove` 的全量重校验在批内免掉 | **做到**（`HandShape.ofScratch` + 调用方逐条对上的守卫，见 §1 的对照表） |
| A 级：四遍扫描融合成一遍 | **部分做到**：5 遍 → 3 遍（七对子 2→1、国士 2→1、死张的闭包扫描 1→1 但去掉了 34 次委托调用）。**没有融合成字面上的一遍**，理由见 DECISIONS 55-3：三组量的计算条件不同（副露过就不算七对子/国士，和了型就不算死张），无条件合成一遍会在最常见的副露路径上**增加**工作 |
| `Ukeire.calculate` 收「已知的当前向听」 | **做到** |
| `TileKindSet.kinds` 存一份 | **做到**（`count` 一并存了：`chiitoitsu` 每次调用都问它） |
| `canJoinRun` 的 `[ a .. b ]` 换 for | **做到**（尾递归，不占可变绑定预算） |
| 不做 C 级与 D 级 | **一格没碰**：没有段级缓存、没有把计数常驻进 `PlayerState`、没有事件驱动维护 |
| 不碰测试生成器 / `web/` / workflow / 牌谱格式 | **做到**：diff 只有 `src/Janpo.Engine/` 的 8 个文件 |
| 不许降属性测试的用例数 | **做到**：用例数改前改后都是 818 |

**多做的（scope creep，如实报）**：`AgariShape.waits`、`RandomPlayer.shantenByKind`、`TileKindSet.Count`。
三处都是票面 B/A 级的同一形状、都在 `src/Janpo.Engine/`、都被 §3 的四类证据盖住。
`AgariShape.waits` 那处是**先量后做**的：分段计时说 `Observation.ofState` 每决策比 `Scaffold.calculate` 还多分配 75 个数组，
而它的钱全花在振听重算调的 `waits` 上（判据 13）。

---

## 5. 我**没做**的，与为什么

1. **C 级（段级缓存）与 D 级（增量状态）** —— 票面明确排除。研究文档 §4.3 也说 C 级必须按批实测、不能按比例推，
   §4.4 说 D 级是架构决策不该在性能票里顺手做。**本票一格没碰。**
2. **`Observation.ofState` 的重放本身** —— 分段计时说它是现在最大的一段（282.7 µs / 决策，占整包 58%），
   但它的成本是「每问一次就把整条掩蔽事件流从头 fold 一遍」，那是 D 级。
   **本票只把它里面那一批形态判定的分配摊掉了**（671.8 → 101.0 个数组，316.6 → 282.7 µs）。
3. **`--typedArrays false`** —— 研究文档 §3.4 建议顺带量一次。**没量**：它是 `web/` 的构建开关，本票不碰 `web/`；
   而且 §2.3 的实测说 `Array.blit` 已经把分配悬崖绕过去了，那个开关现在换来的只是下标读的 21%，方向不明。留给别的票。
4. **给「缓冲不得越出批的边界」加一道静态闸门** —— 加法很便宜，但要改 `scripts/check-style.sh`，不在本票改动面上。提案见 DECISIONS。
5. **把 `AgariShape.waits` 的缓冲再往上提到 `Observation` 的 fold 里** —— 那要把缓冲穿过 `PlayerState.refreshFuriten`，
   而 `PlayerState` 是纯度要守住的那一层（研究文档 §4.4 划的边界）。现在 `waits` 每次自建一份（4 个数组），
   剩的 101 个数组/决策里大半是它。**收益已经拿到 92%，剩下的 8% 不值得把可变缓冲推过那条边界。**
6. **`Danger.rank`** —— 每决策 10.9 → 11.9 µs（噪声带内），`Danger.fs` 里的 `List.map` / `List.distinct` 一行没动。研究文档 §7 也把它列在「没估」。
7. **降任何用例数、放宽任何断言** —— 一条没有。818 条改前改后都在。

---

## 6. 复现

```bash
# 语义对拍（37.4 万行）与 soak 事件流：探针在 /tmp，做法见 scripts/fsi/README.md 的「复制到 /tmp 改副本」
JANPO_DIFF_OUT=/tmp/before.txt dotnet fsi --exec /tmp/janpo-55/diff-engine.fsx     # 改前树
JANPO_DIFF_OUT=/tmp/after.txt  dotnet fsi --exec /tmp/janpo-55/diff-after.fsx      # 改后树
cmp /tmp/before.txt /tmp/after.txt

# 每决策成本（.NET）
dotnet fsi --exec /tmp/janpo-55/bench-after.fsx

# 每决策成本（浏览器形态）
dotnet fable src/Janpo.Engine -o /tmp/js-after && node /tmp/janpo-55/bench-node.mjs /tmp/js-after

# 34 长数组计数：树副本 + AllocProbe，源码补丁见 §2.1
# CI 时间：交错跑 6 轮，/usr/bin/time 取 user+sys
dotnet test janpo.slnx -c Release --no-build --logger "trx;LogFileName=x.trx"
```

**这些探针都在 `/tmp/janpo-55/` 里，是易失的**；仓库里没有留新文件（本票的 diff 只有 `src/Janpo.Engine/`）。
要重跑，照 §2.1 与 §6 的说明重建即可，全部步骤都写在这里。

---

## 7. 留给人的待审项

1. **`ShantenScratch` 要不要收进 `CONTEXT.md`？**（DECISIONS 55-1）——我不许改术语表，先记提案。
2. **要不要给 `HandShape.ofScratch` 加一条静态出现预算**（`scripts/check-style.sh`，形状同 `let mutable` 预算）？（DECISIONS 55-4）
3. **研究文档要不要回填两条实测？**（DECISIONS 55-5）——§2.2 的「`tenpaiDahai` 是最密调用点」在决策包口径上不成立（它是 0.1 µs，钱花在 `GameState.step` 里），
   而 §7 列为「一行没读」的 `Observation` 其实是最大的一段。**我没改 `docs/research/`**（那是别人的产出，且票面说别重新调研）。
