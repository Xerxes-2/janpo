# 56 — CI 结构提速：属性测试的语料浪费、浏览器闸门共用一个浏览器

**What to build:** CI 现在本机约 1m40s、远端 5–6 分钟。**闸门一道都不许少、用例数一个都不许降**——
这一票只砍**结构性的浪费**：算了不用的东西、重复启动的东西。

**Blocked by:** None

**Status:** ready-for-human

## 调度器已量的分布（本机 32 核；远端 4 核，时间由 CPU 总量决定）

| 段 | 耗时 |
|---|---|
| `dotnet test` | **51.4s 墙钟 / 394s CPU / 717 用例**（最慢十五条全是属性测试，占 33%） |
| `dotnet build` | 9.4s |
| fable 编译 | 7.7s |
| 浏览器闸门六道 | tracer 7.0s + board 7.4s + export 2.8s + redaction 1.8s + golden 0.9s ≈ **20s** |
| 语义不变量 | 4.4s |
| vite build / fantomas / check-style | 1.7s / 1.0s / 0.1s |

## 一、属性测试的语料浪费（**先去实测确认，再改**）

`tests/Janpo.Engine.Tests/GameStateGenerators.fs` 的 `GameStateArbitraries.GameState()` 写成：

```fsharp
Gen.frequency [ 4, Gen.constant (GameStateFixtures.trace Kyoku.randomPlayer seed)
                4, Gen.constant (GameStateFixtures.trace tenpaiSeeking seed)
                ... 共九条 ... ]
```

`Gen.constant` 的参数是**即时求值**的，所以**每取一个样本都把九条轨迹全跑完，只用其中一条**——
而每条轨迹是「把一局用 seeking 选手打完」，seeking 选手每一步都在算向听。

- [x] **先实测确认这个判断**（例如给 `trace` 加个计数器数它被调了多少次，或直接量改前改后）。
      **判断错了就说出来，别顺着我的话改**（判据 13：因果判断先量）
- [x] 确认后修：只让被选中的那一条被计算（`Gen.delay` / `gen { }` 里再 `return!`，或重构成
      「先选分支再算」）。**取样分布不许变**——权重与九条轨迹一条不少
- [x] 顺带看**记忆化**：`(脚本, 种子)` 一共只有几千种组合，跨样本复用同一条轨迹能让重复取样免费。
      但**别引入跨测试的可变共享状态**（属性模块开着 `Parallelism = 4/8`），做不干净就不做并说明
- [x] **用例数与权重一个都不许降**。这一票要的是「同样的覆盖、更少的浪费」

## 二、浏览器闸门共用一个浏览器与一个服务器

现在六道各自起一个 vite 服务器 + 一个 Chrome（tracer / board / golden / export×2 / redaction / poison）。

- [x] 让它们**共用一个浏览器进程与一个静态服务器**（各自开自己的 page/context），
      或至少把最贵的那几道合起来。目标是 20s → 一半以下
- [x] **每道闸门的断言一条不许少、反向自证一条不许拆**。合并后要**逐道确认它仍然会红**
      （每道临时破坏一次，红的输出抄进报告）——本项目的判据 1 与 3
- [x] 保留 `JANPO_NO_BROWSER=1` 的逃生口，并把 `ci-web.sh` 里那句道数注释同步改对
- [x] 每道闸门失败时**仍要能单独重跑**（`pnpm run verify:xxx`），别为了合并把可调试性弄丢

## 三、别做的

- [x] **不改 `src/Janpo.Engine/`**——票 55 正在改热路径（`Shanten`/`Ukeire`/`Scaffold`/`HandShape`），
      两票会撞。你的收益与它的收益是**叠加**的，各自量各自的
- [x] 不加任何「有条件跳过」的闸门、不降 FsCheck 用例数、不删属性
- [x] `nix` 缓存那条路**别再走**：调度器实测过 restore 20s、冷拉只 18s，净亏（票 45，判据在
      `DECISIONS.md` 里）。远端慢是 4 核，不是拉工具链

## 验收

- [x] 改前 / 改后各给：`dotnet test` 墙钟与 CPU 总和、浏览器闸门总耗时、`./scripts/ci.sh` 全程墙钟
- [x] `./scripts/ci.sh` 全绿；用例总数与属性用例数**不低于改前**（把两个数字都写进报告）
- [x] 合并后的浏览器闸门**逐道会红**的证据

## 做完了（报告：`.scratch/llm-riichi-arena/run/reports/56-ci-structural-speedup.md`）

`./scripts/ci.sh` **2m00.9s → 39.2s**；`dotnet test` 70.99s / 507.7s CPU → 14.80s / 126.4s CPU；
浏览器七趟 38.0s → 6.3s。**用例总数 818 → 818、属性用例数 143 → 143**（生成用例 17,300 不变）。

三条与票面不同的地方，都在报告里给了数：
1. 那处浪费是**十条**轨迹不是九条（实测：400 个样本调 4000 次 `traceFrom`）；
2. **记忆化不做**——能省 15% CPU，但要常驻 ≈364 MB，且属性模块开着 `Parallelism`（§2.4）；
3. 票面第二节的因果只对了一半：共用浏览器只省 3.15s，那 38 秒的大头是**每走一手四次
   playwright 往返**（200 手：6680ms → 279ms）。合并照做了，杠杆另在一处（§3）。
