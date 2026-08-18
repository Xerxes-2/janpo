# 84 — 浏览器侧的分配悬崖：`Int32Array(34)` 每次调用现新建

**What to build:** `docs/research/engine-perf-caller-and-browser.md` §3.4 那条「必须先验证的前提」
**已经验证**（主人验的，我复核过）：Fable 5.13.0 确实把 F# 的 `int array` 编成 `Int32Array`。
于是 §3.3(a) 那格悬崖是真的——**同一行代码，浏览器上比 .NET 贵 9–15 倍**：

| | 单价（研究文档实测） |
|---|---|
| `new Int32Array(34)` | **0.8–1.4 µs** |
| 同尺寸普通 `Array` | 40 ns（**20–25×**） |
| 复用一个缓冲 | 17 ns（**45×**） |
| .NET 侧 `Array.copy`（对照） | 90.5 ns |

**Blocked by:** None（引擎侧，与 79/81/82/83 那几张 Web 票零重叠）

**Status:** ready-for-human

**做完了**（数与量法在 `run/reports/84-typed-array-cliff.md`）：
**(A) 采纳**——`--typedArrays false` 使建一次决策包 1114.6 → 914.5 µs（**−18.0%**）、
脚手架 863.9 → 701.8 µs（**−18.8%**），远超票面 10% 的闸门，**引擎 `.fs` 零改动**。
**(B) 不做**——票 55 之后只剩 **106.85 个 34 长数组/决策**（票面引的 772 是票 55 之前的数），
开关生效后它们值 **0.58%**（.NET 0.51% 时间 / 7.0% 字节），且 62% 卡在 `PlayerState` 的纯度边界后面。

## 现状（我数的，别重数）

引擎生成物里 **11 处** `new Int32Array(34)`：

```
Shanten.js 4   Yaku.js 2   MentsuBreakdown.js 1
HandShape.js 1  Danger.js 1  AgariShape.js 1  AgariHand.js 1
```

`ShantenScratch`（研究文档 §4.2 推荐的「调用方持有暂存缓冲」）**已经落过地**——
生成物里有 `ShantenScratchModule_create`。**这一票是它没走完的那一半。**

热在哪：`Scaffold.calculate`（建决策包的那条路）里 `Danger.rank`、`Danger.threats` 各调一次，
`AgariShape.classify` 被 `Yaku.candidates/detect` 与 `PlayerState` 反复调。
研究文档 §2.1 量到 **772 次数组分配 / 决策** ⇒ 浏览器上 **≈ 0.7 ms/决策**只花在分配上。

## 两个杠杆，**先量第一个**

**(A) 一个开关：`--typedArrays false`**（`web/package.json` 的 fable 命令上加一句）。
零代码改动，20× 的分配成本换 **21% 的下标读惩罚**（25.2 → 30.6 ns，研究文档 §3.3(b)）。
按 772 次分配 vs 数千次下标读的调用形状，这笔交易**大概率是赚的——但必须实测，不许按比例推**。

**(B) 把 `ShantenScratch` 那套推广到剩下的六处**（`Danger` / `AgariShape` / `AgariHand` /
`HandShape` / `Yaku` / `MentsuBreakdown`）。**两个平台都受益**（.NET 不吃开关 A），
代价是公开签名要动，且 ADR-0001 的引擎纯度不许破——
**不许用全局可变缓存**（研究文档「方向 4」已被否：测试侧四个属性模块开着 `Parallelism = 8`，
共享数组会真的坏掉）。照 `ShantenScratch` 的既有形状：**调用方在进入批之前建一个，传进去。**

- [x] 先量 (A)。**若 (A) 在真 workload 上拿不到 10% 以上，就别为它承担那 21%**——写下数与结论
      → 拿到 **18.0%（整包）/ 18.8%（脚手架）**，且这个数**已经把 21% 的下标读惩罚算进去了**
      （量的是端到端建包时间）。采纳：`fable` 与 `dev` 两条命令各加 `--typedArrays false`。报告 §2
- [x] 再判 (B) 还值不值：(A) 生效后 (B) 的收益要重新量，不许拿 (A) 之前的数说话
      → **不做**。探针实测剩余分配 106.85 个/决策（票 55 已把 772 降到这里），× 开关后的单价 50 ns
      = 5.34 µs = **0.58%**（落在噪声带里）；票面点名的 `Danger`/`AgariHand`/`MentsuBreakdown`/`Yaku`
      四处合计 **0.85 个/决策**（后三处是 0.00），而 62% 的剩余量在 `PlayerState` 那条纯度边界后面
      （报告 55 §5.5 已判过）。报告 §3

## 量具：没有现成的，你要建一个（**这一票最容易做错的地方**）

- [x] **workload 必须是「建决策包」**（`Scaffold.calculate` / `Ukeire` / `Danger.rank`）——
      772 次/决策就是从那儿来的。**四家 bot 的对局不走这条路**（bot 不建决策包），
      拿 `verify-export --to-end` 当基准会量出一个漂亮但无关的数
      → 主口径是 `Scaffold.calculate` 与 `DecisionPackage.forSeat`；**bot 对局作为反面对照也量了**
      （`game` 口径），同一分母下 51.0 vs 1114.6 µs/决策（差 21.9 倍），开关对它只有 1.15×——
      票面那句警告在报告 §1 里有数坐实
- [x] 两个平台各给一组：.NET（`dotnet` 侧，含分配字节）与浏览器（V8）
      → 报告 §2.2（浏览器）与 §3.4（.NET，含 B/决策：整包 243 895 B）
- [x] 基准要**可复现**：固定种子、固定手牌集合、跑够轮数压住噪声；命令写进报告
      → 种子 1–12 固定语料 4326 个决策点，预热 + 多轮**交错**取中位并报区间，
      另做了**顺序对照**（两份产物调个个儿重跑）；命令在报告 §6
- [x] 基准脚本放 `web/scripts/`（不进 CI 关卡，进 CI 会让每次提交都等它）
      → `web/scripts/bench-decision.mjs`，**不在 `ci-web.sh` 里**；`pnpm run bench:decision` 手跑

## 安全网（已经有了，跑就是）

- [x] `verify-golden`：**浏览器内跑引擎、与 dotnet 侧逐字段逐行对拍**。开关 (A) 会连
      `fable-library` 的 `Random.js` 一起改（它也用 `Int32Array`）——**牌山的可复现性压在这道闸门上**，
      它绿了才算 (A) 没改语义
      → 绿。**但票面这条前提错了**：`fable-library` 是随产物拷过去的**预编译 JS**，开关不改它——
      两份产物的 `fable_modules/` 除编译选项记录外**逐字节相同**，引擎自己的 `Rng.js` 也逐字节相同。
      **牌山可复现性实际由 `verify-tracer` + `verify-export --to-end` + `verify-share` 背书**
      （三道都要求同种子跑出逐条相同的事件流），`verify-golden` 背书的是形态判定的每一个字段。报告 §4.1–4.2
- [x] 引擎全量用例 + 属性测试（`Parallelism = 8` 那几个模块正是 (B) 的雷区）
      → `./scripts/ci.sh` 全绿（包含 `dotnet test`）。B 级没做，雷区没踩
- [x] `scripts/oracle/differential.sh`：动了向听/听牌/役种判定就跑它
      → 判定代码一个字节没改，仍跑了一遍：`differential.sh 20000 84` → 差异 0 处。
      另有一道量具自带的对照：两份产物各自重新打完 12 场，4326 个决策点的 digest 同为 `a5e8d048`

## 边界

- [x] 不碰 Web 层（`src/Janpo.Web/**`、`web/src/agent/**`）——那边有四张票在排队
      → 一个字节没动；`web/` 下只动了 `package.json`（开关 + 一条 bench 脚本）与新增的 `scripts/bench-decision.mjs`
- [x] 不改引擎的**行为**：这一票只改「同样的结果怎么算出来」
      → `src/Janpo.Engine/**` 零改动（`jj diff --stat` 里没有任何 `.fs`）
- [x] 不引入依赖（`Janpo.Engine.fsproj` 的 Fable 允许名单是硬的）
      → `.fsproj` 没动；量具只用 node 内置模块与 Fable 自己的运行时库，`package.json` 的依赖两栏没动
- [x] 不做「全局可变缓存」这一类破纯度的解法
      → B 级没做，没新增任何可变状态

## 交付

- [x] 报告 `run/reports/84-typed-array-cliff.md`：基准的形状与命令、(A) 的前后数（两平台）、
      (B) 值不值的判断与数、最终改了什么没改什么
- [x] `run/DECISIONS.md` 追加（`## 84` 开头）
- [x] `./scripts/ci.sh` 全绿；若 (A) 采纳，报告里明写「牌山可复现性由 `verify-golden` 背书」
      → 写了，**并把背书人改对**：牌山那一面真正背书的是 `verify-tracer` / `verify-export --to-end` /
      `verify-share`（同种子事件流逐条相同），`verify-golden` 背书形态判定的逐字段数值。理由见报告 §4.2
