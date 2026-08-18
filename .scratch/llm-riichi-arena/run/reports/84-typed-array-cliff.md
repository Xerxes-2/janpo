# 84 — 浏览器侧的分配悬崖：先量开关，再判 scratch 推广

fixed point `5188fbfa`（`main`）。工作区 `/home/xerxes2/janpo-ws-b`。
机器：32 核，node v26.7.0，dotnet 10，Fable 5.13.0。

## 0. 一句话

**(A) 采纳**：`web/package.json` 的两条 fable 命令加 `--typedArrays false`，
建一次决策包从 **1115 µs 掉到 915 µs（−18.0%）**，脚手架那一段 864 → 702 µs（−18.8%），
**零 F# 代码改动**；**(B) 不做**：票 55 落地之后剩下的分配只有 **106.85 个 34 长数组/决策**，
开关生效后它们值 **5.3 µs = 建包总成本的 0.58%**（.NET 侧 0.51% 时间 / 7.0% 字节），
而其中 62% 要把可变缓冲穿过 `PlayerState` 才拿得到——票 55 已经判过那条边界不值得跨。

**引擎的 `.fs` 一个字节都没改。** 改的是三份非代码文件：`web/package.json`（开关 + 一条 bench 脚本）、
`docs/development.md`（把开关的理由写在人会看的地方）、新增 `web/scripts/bench-decision.mjs`（量具）。

## 1. 量具的形状（票面点名「最容易做错的地方」）

`web/scripts/bench-decision.mjs`。**它量的是建决策包那条路，不是四家 bot 的对局。**

语料：种子 1–12 各打一场东风战，把每一个「有人被问」的局面（`GameState.legalActions` 非空的那一步）
连同座位存下来 —— **4326 个决策点**。驱动与 `Kyoku.run` 逐条相同，只是多一句「把这个局面记下来」。

五个口径，**全部按同一个分母（4326）归一，因此直接可比**：

| 口径 | 量的是 |
|---|---|
| `game` | **反面对照**：四家 bot 把这些局从头打到尾（`GameState.step`），一个决策包都不建。`verify-export --to-end` 量到的就是这条路 |
| `scaffold` | `Scaffold.calculate`（`Shanten` × ~400 + `Ukeire` + `Danger.rank`）；观测与动作表预先备好，不计时 |
| `package` | `DecisionPackage.forSeat` 整包 = 脚手架 + `Observation.ofState` |
| `observation` | 整包减脚手架的那一半，单独量一次好归因（判据 13） |
| `danger` | `Danger.rank` 单独一次 |

三件让它可复现的事：① 固定种子固定语料；② 每个口径先整跑一遍预热再计时，多轮取中位并报区间；
③ **逐轮交错**（A B A B …），别人在同一台机器上跑构建时噪声落在两边而不是一边。

`--engine` 收逗号分隔的多份 Fable 产物，各自建各自的语料（记录类型不能跨模块图混用）。
每条跑道印一个 **digest**：4326 份脚手架里每一档的向听、向听差、受入张数与种数、危险档位与名次，
折成一个 FNV-1a。**两份产物的 digest 必须逐字相同**——这是量具自带的语义对照。

**它不进 CI**。理由：一次完整跑要 30–60 s，每次提交都等它不划算。手跑那条命令在 §6。

### 反面对照给出的数（为什么票面那句警告是对的）

同一份语料，`game`（bot 打完）**51.0 µs/决策**，`package`（建包）**1114.6 µs/决策**——
差 21.9 倍。而开关对两者的效应也不同：`game` 1.15×、`package` 1.22×。
**拿 bot 对局当基准，量到的是另一个数**。

## 2. (A) `--typedArrays false`：数与结论

### 2.1 两份产物怎么来的

```sh
dotnet fable src/Janpo.Engine -o /tmp/janpo-84/engine-typed                     # 默认（Int32Array）
dotnet fable src/Janpo.Engine -o /tmp/janpo-84/engine-plain --typedArrays false # 普通 Array
```

开关**只改 7 个文件 18 行**（`grep` 得到，`Int32Array` 全量）：

```
AgariHand.js  AgariShape.js  Danger.js  HandShape.js  MentsuBreakdown.js  Shanten.js  Yaku.js
  const counts = new Int32Array(34);   →   const counts = fill(new Array(34), 0, 34, 0);
  new Int32Array([0, 8, 9, …])         →   [0, 8, 9, …]                （两处只读常量表）
```

整个 `Janpo.Web` 编出来也是同样 7 个文件（`Agent.js` / `Demo.js` / `Share.js` 的差异只是
输出目录不同带来的 `.ts` 相对路径，与开关无关）。

### 2.2 浏览器（node v26.7.0，4326 个决策点，7 轮交错，µs/决策）

```
cd web && node scripts/bench-decision.mjs --engine /tmp/janpo-84/engine-typed,/tmp/janpo-84/engine-plain --seeds 1-12 --rounds 7
```

| 口径 | 默认（Int32Array） | `--typedArrays false` | 快了 |
|---|---|---|---|
| `game`（反面对照） | 51.0（47.0–53.9） | 44.5（42.2–57.8） | 1.147× |
| **`scaffold`** | **863.9**（856.2–877.7） | **701.8**（694.1–706.5） | **1.231×（−18.8%）** |
| **`package`** | **1114.6**（1099.7–1142.9） | **914.5**（904.6–920.7） | **1.219×（−18.0%）** |
| `observation` | 245.4（237.3–263.6） | 200.6（198.2–206.1） | 1.223× |
| `danger` | 55.5（55.0–62.0） | 55.4（54.5–57.2） | 1.002× |

**顺序对照**（把两份产物调个个儿，5 轮）：`scaffold` 835.9 → 704.4（1.187×）、
`package` 1093.5 → 917.8（1.191×）。方向与量级都不随顺序变。

**digest 两边同为 `a5e8d048`（4326 行 / 479289 字），逐字相同。**

`danger` 纹丝不动（1.002×）是一条内证：`Danger.rank` 每次只新建 1 个 34 长数组，
分配不是它的成本项——收益确实来自那些每决策上百次的路径，而不是量错了什么。

### 2.3 单价：那 20× 在这台机器上是多少（同一脚本的 `--prims`）

```
cd web && node scripts/bench-decision.mjs --prims --rounds 5     # 200000 次 × 5 轮，交错
```

| Fable 真发出来的那一句 | 中位 | 比值 |
|---|---|---|
| `new Int32Array(34)` | 611.9 ns（567.9–675.3） | — |
| `fill(new Array(34), 0, 34, 0)` | **50.0 ns**（49.1–52.1） | **12.2×** |
| `copy(Int32Array)` | 645.7 ns | — |
| `copy(Array)` | **15.6 ns** | **41×** |
| `copyTo(Int32Array)`（= `Array.blit`） | 25.7 ns | — |
| `copyTo(Array)` | 10.5 ns | 2.4× |

研究文档 §3.3(a) 说 20–25×，这台机器上是 12×——**结论方向一致，倍数别照抄**。
`copyTo` 那一行解释了票 55 留下的一个尾巴：typed array 的 `blit` 走 `dst.set(src.subarray(…))`，
每次多造一个 view；普通数组是纯循环，反而便宜一半多。

### 2.4 结论：远超 10% 的闸门，采纳

票面判据是「拿不到 10% 就别承担那 21% 的下标读惩罚」。**实测 18.0–18.8%**，
而且这个数**已经把下标读的惩罚算进去了**——量的是端到端的建包时间，不是分配那一项。
21% 的读惩罚确实存在（研究文档 §3.3(b)），但在这条路上，每决策上百次分配的省
压过了几千次下标读的多花。

落地：`web/package.json` 的 `fable` 与 `dev` 两条命令各加 `--typedArrays false`
（`dev` 那条要加在 `--run` **之前**，`--run` 之后的东西全被当成待执行命令）。

## 3. (B) 把 `ShantenScratch` 推广到剩下六处：**不做**，这是数

**必须拿开关生效之后的数说话**，也必须拿票 55 之后的现状说话——票面引用的
「772 次分配/决策」出自研究文档 §2.1，那是**票 55 落地之前**量的。

### 3.1 现在还剩多少（一次性探针，做法照报告 55 §2.1）

把 `engine-typed` 整个复制一份，在每一处数组新建的语句前插一个计数器，
再拿同一份语料（4326 个决策点）跑一遍。探针脚本在 `/tmp/janpo-84/probe-alloc.mjs`，
它 `import` 的正是 `web/scripts/bench-decision.mjs` 导出的语料构造函数（同一份语料，不是另一套）。

探针不进仓库（`/tmp` 会没），因此把它插在哪几处拄在这里——这五列就是全部输入：
文件、定位用的锚（函数名）、要插在其前的那句、这一处的名字；
插法是把 `<那句>` 换成 `<那句>(hit("<名字>"), 0) || `（并给文件首行加一句 `import`）：

```
HandShape.js         HandShapeModule_create           const counts =                     HandShape.create
HandShape.js         HandShapeModule_add              const counts =                     HandShape.add
HandShape.js         HandShapeModule_remove           const counts =                     HandShape.remove
Shanten.js           ShantenScratchModule_create      return new ShantenScratch(         ShantenScratch.create（一次 4 个）
Shanten.js           ShantenModule_standard(          return ShantenModule_standardIn(   Shanten.standard
Shanten.js           ShantenModule_calculate(         return ShantenModule_calculateIn(  Shanten.calculate
AgariShape.js        AgariShapeModule_classify(       return AgariShapeModule_classifyIn( AgariShape.classify
Danger.js            DangerModule_visibleCounts       const counts =                     Danger.visibleCounts
AgariHand.js         AgariHandModule_create           const counts =                     AgariHand.create
MentsuBreakdown.js   MentsuBreakdownModule_enumerate  const counts =                     MentsuBreakdown.enumerate
Yaku.js              const counts = new Int32Array(9) const counts =                     Yaku.chuuren（9 长）
```

（这张表就是 `grep -n "new Int32Array\|copy(" engine-typed/*.js` 的全部命中项。
`Shanten.js` 里另有两个 `new Int32Array([…])` 是模块级只读常量表，整个进程只建一次，不计。）

| 新建者 | `Scaffold.calculate` | `DecisionPackage.forSeat` |
|---|---|---|
| `HandShape.create` | 1.00 | 38.12 |
| `AgariShape.classify` | 0 | 28.21 |
| `ShantenScratch.create`（一次 4 个） | 1.00（= 4 个） | 9.92（= 39.68 个） |
| `Danger.visibleCounts` | 0.85 | 0.85 |
| `AgariHand.create` / `MentsuBreakdown.enumerate` / `Yaku`（9 长） | 0 | 0.00 |
| **合计（数组个数）** | **5.85 个/决策** | **106.85 个/决策** |

与报告 55 §2.1 的 5.9 / 106.8 逐位吻合——票 55 的成果没有回退。
**772 已经是历史数字**，它在票 55 那一票就降到了 106.8。

### 3.2 上界（不是估计，是上界）

「把这些分配**全部**变成免费」能省多少：单价（§2.3 / §3.4 实测）× 个数（§3.1 实测）。

| | 浏览器（开关生效后） | .NET |
|---|---|---|
| 建包总成本 | 914.5 µs | 278.9 µs |
| 106.85 个数组值多少 | 106.85 × 50.0 ns = **5.34 µs** | 106.85 × 13.4 ns = **1.43 µs** |
| **B 级的收益上界** | **0.58%** | **0.51%** |
| 脚手架那条路（5.85 个） | 0.29 µs / 701.8 µs = **0.04%** | 0.08 µs / 209.5 µs = **0.04%** |
| 分配字节（.NET） | — | 106.85 × 160 B = 17.1 KB / 243.9 KB = **7.0%** |

**0.58% 落在噪声带里**（§2.2 每一格的区间宽度都在 ±1% 上下）。

### 3.3 就算值得，票面点名的那六处也不是收益所在

- `Danger` **0.85/决策**、`AgariHand` / `MentsuBreakdown` / `Yaku` **0.00/决策**——
  这四处加起来 0.85 个/决策，浏览器上 **0.043 µs = 0.005%**。改它们是纯粹的签名噪声。
- `HandShape.create`（38.12）与 `AgariShape.classify`（28.21）合计 **62%** 的剩余分配，
  但它们一次都不在 `Scaffold.calculate` 里，全部来自 `Observation.ofState` 重放时的
  `PlayerState.isTenpai / isAgari / canRon / waits` 与 `RiichiState`
  （`AgariShape.isAgari` 就是 `classify |> List.isEmpty |> not`，分配在 `classify`）。
  要复用缓冲，就得把它从 `Observation` 的 fold 一路穿过 `PlayerState` 的四个公开函数——
  **报告 55 §5.5 已经判过这条边界**：「收益已经拿到 92%，剩下的 8% 不值得把可变缓冲推过
  `PlayerState` 那条纯度边界」。那时的判断依据是 8%，现在开关生效后是 **0.58%**，
  只会更不值得。
- `ShantenScratch.create` 9.92/决策全来自 `AgariShape.waits`（它自己在进批前建一个，
  正是票 55 落的形状），同样卡在 `PlayerState.waits` 那条边界上。

### 3.4 .NET 侧（开关不作用于它，但 B 级会）

```sh
dotnet fsi /tmp/janpo-84/bench-dotnet.fsx     # 与浏览器侧同一份语料、同一条路，另报分配字节
```

| 口径 | 中位 µs/决策 | 分配字节/决策 |
|---|---|---|
| `scaffold` | 209.5（205.9–211.2） | 76 646 B |
| `package` | 278.9（272.9–282.0） | 243 895 B |
| `observation` | 59.3（57.7–62.8） | 155 833 B |
| `danger` | 10.2（10.0–10.5） | 32 911 B |

原语单价（`dotnet fsi --optimize+`，200 万次 × 5 轮）：
`Array.zeroCreate<int> 34` = **13.4 ns / 160 B**、`Array.copy` = 91.7 ns / 160 B、
`Array.blit` = 6.1 ns / **0 B**。

顺带一个跨平台的数：开关之前浏览器/.NET = 1114.6/278.9 = **4.00×**，
开关之后 914.5/278.9 = **3.28×**（研究文档 §3.3 预期 4–6×，现在好于预期）。

## 4. 语义没改：四类证据

### 4.1 `./scripts/ci.sh` 全绿

跑于最终提交内容（见 §6 的记录）。其中与这一票直接相关的：

- **`verify-golden`**：浏览器内跑 Fable 产物，与 `tests/fixtures/golden/dual-target.json`
  逐字段逐行对拍；同一份文件在 dotnet 侧由 `GoldenSuiteTests` 跑。
- **`verify-tracer`**：同种子在浏览器里打完，终局点数与顺位与 `janpo kyoku` / `janpo game` 逐项相同。
- **`verify-export`（两趟，其一 `--to-end`）**：浏览器内导出牌谱，字节交回引擎 fold，事件流逐条相同。
- **`verify-share`**：引擎现打两场，压/编/解一轮再 fold，事件流逐条相同。

**牌山的可复现性由 `verify-tracer` + `verify-export --to-end` + `verify-share` 背书**
（三道各自都要求同种子跑出逐条相同的事件流；`verify-golden` 背书的是形态判定的每一个字段）。

### 4.2 票面前提的一处更正：开关**不碰** `fable-library`

票面说「开关会连 `fable-library` 的 `Random.js` 一起改（它也用 `Int32Array`）」。
实测**不是这样**：`fable-library` 是随产物拷过去的**预编译 JS**，Fable 的开关不改它。
两份产物的 `fable_modules/` 除 `project_cracked.json`（编译选项的记录）外**逐字节相同**，
`diff -rq` 无输出。引擎自己的 `Rng.js`（洗牌的那份 xorshift）在两份产物里也**逐字节相同**。

所以牌山这件事上，开关连改的机会都没有——但上面那三道闸门照跑照绿，结论不靠这条推理硬扛。

### 4.3 量具自带的对照：4326 个决策点的 digest 逐字相同

两份产物各自从种子 1–12 重新打完 12 场（**牌山、事件流、每一步的合法动作都由各自的产物算**），
各自得到 4326 个决策点——个数相同；每个点的脚手架数值折出的 digest 同为 `a5e8d048`。
局面若在任何一步分岔，决策点个数或 digest 必然不同。

粗算这道对照覆盖的判定量：4326 个决策点 × 每决策约 14 档可打之牌 × 每档一次向听 + 一次受入
（受入本身是 34 次试摸）——十万量级的形态判定，逐个数值相同。

### 4.4 `scripts/oracle/differential.sh`（票面说「动了判定就跑」）

**没动判定代码**（`.fs` 零改动），仍然跑了一遍作阳性背景：
`./scripts/oracle/differential.sh 20000 84` → 「对拍 20000 手，差异 0 处」。

## 5. 我**没做**的，与为什么

1. **B 级推广**：§3 的数，收益上界 0.58%（浏览器）/ 0.51%（.NET），且 62% 的量卡在
   `PlayerState` 的纯度边界后面。**park 在这里，不是忘了**。
2. **把开关做成 CI 闸门**（例如「`package.json` 里必须有 `--typedArrays false`」）：
   越出票面边界（要动闸门脚本的断言）。理由改写进了 `docs/development.md` 与本报告，
   `pnpm run bench:decision` 是任何人都跑得起来的复核手段。
3. **`bench-decision.mjs` 进 CI**：票面明写不进，一次跑 30–60 s。
4. **`Yaku.js` 那两处**（`Int32Array(9)` 与九莲的常量表）：探针实测 0.00 次/决策
   （只在有人和牌时走），不值一次签名改动。
5. **碰 Web 层 / `demo-paifu.json` / 闸门脚本的断言**：一个字节都没动。

## 6. 复现

```sh
# 两份 Fable 产物（引擎单编，跑得快；整个 Janpo.Web 编出来的差异是同样 7 个文件）
dotnet fable src/Janpo.Engine -o /tmp/janpo-84/engine-typed
dotnet fable src/Janpo.Engine -o /tmp/janpo-84/engine-plain --typedArrays false

# 浏览器形态：一条命令出 §2.2 那张表（两份产物逐轮交错，digest 自带语义对照）
cd web && node scripts/bench-decision.mjs \
  --engine /tmp/janpo-84/engine-typed,/tmp/janpo-84/engine-plain --seeds 1-12 --rounds 7

# 现在这份产物自己的数（`--engine` 不给就用 src/generated）
cd web && pnpm run bench:decision

# 原语单价（§2.3）
cd web && node scripts/bench-decision.mjs --prims --rounds 5

# .NET 侧（§3.4；探针在 /tmp，做法见 scripts/fsi/README.md 的「复制到 /tmp 改副本」）
dotnet fsi /tmp/janpo-84/bench-dotnet.fsx
dotnet fsi --optimize+ /tmp/janpo-84/prims-dotnet.fsx

# 每决策还剩几个 34 长数组（§3.1）
cd web && node /tmp/janpo-84/probe-alloc.mjs

# 闸门
./scripts/ci.sh
./scripts/oracle/differential.sh 20000 84
```

## 7. 留给人的待审项

1. **B 级 park 的复议触发条件**：若哪天决策包的建包成本被别的优化压到现在的 1/5 以下
   （比如向听搜索换算法，见 `docs/research/shanten-search-alternatives.md`），
   那 106.85 个分配的占比会从 0.58% 升到 3% 上下，届时值得重新量。
2. **`docs/research/engine-perf-caller-and-browser.md` 的两处数字已过时**：
   §2.1 的「772 次/决策」是票 55 之前的现状（现为 106.85），
   §3.4 关于开关会改 `fable-library` 的推断不成立（§4.2）。研究文档是那一刻的记录，
   我没有改它——**要不要回填，请人裁**。
