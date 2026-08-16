# 19 — Fable 工具链与浏览器里的第一颗曳光弹

**结论：done。** 引擎源码**一行没改**就被 Fable 编进了浏览器，同种子的一局与一整场在浏览器里
跑出的点数与顺位与 dotnet CLI **逐项相同**；再往前一步实测：24 个种子 × 两种跑法共 120 次运行、
约 9,000 行 mjai JSON，两侧**逐字相同**。`./scripts/ci.sh` 一条命令两侧全绿（宿主机 47s，
`nix develop --command ./scripts/ci.sh` 也是 47s）。

---

## 1. 验收逐条

| 验收 | 状态 | 证据 |
|---|---|---|
| Fable 工程引用引擎编出 JS，引擎源码一行不改 | ✅ | `src/Janpo.Web`；`jj diff src/Janpo.Engine/` 为空；Fable 0 error 0 warning |
| `pnpm dev` 有 HMR，`pnpm build` 出静态产物 | ✅ | 见 §4 |
| 浏览器内跑固定种子，点数与顺位与 `janpo kyoku` 逐项相同 | ✅ | 见 §3 |
| CI 增加 JS 侧关卡；nix dev shell 有 node 与 pnpm | ✅ | `scripts/ci-web.sh`、`flake.nix`；见 §5 |
| 引擎依赖白名单闸门仍然绿 | ✅ | `ci.sh` 的「引擎依赖检查通过（Fable 允许名单）」；`Thoth.Json.JavaScript` 只在 `Janpo.Web.fsproj` |
| ADR-0005 记下 B / 被否的 A / TS 边界 | ✅ | `docs/adr/0005-feliz-useelmish-ui-ts-only-agent-layer.md` |

---

## 2. 做了什么

### 新增 `src/Janpo.Web`（Fable → JS 的宿主工程，已进 `janpo.slnx`）

- `Tracer.fs` — `Trace` 类型（Seed / Kyokus / Events / Scores / Juni）与 `Tracer.kyoku`、
  `Tracer.game`。它只是把引擎跑一遍再摆成一个能逐项对照的形状，规则一行都不在这里。
  顺位借 `Game.settle ruleset 0 scores` 算——**顺位规则只该有一处实现**。
  `Tracer.eventJson` 是 `Event.encoder >> Encode.toString 0`，走 `Thoth.Json.JavaScript` 后端。
- `App.fs` — Elmish 的 `Model` / `Msg` / `init` / `update` 加 Feliz 视图，
  `React.useElmish` 挂进 React。页面只有两件事能发生：改种子、重跑。
- `Main.fs` — 只导出一个 `mount : string -> unit`。**这是 JS 侧唯一碰 F# 的入口**（ADR-0005）。

### 新增 `web/`（Vite 应用）

`index.html` + `src/main.ts`（3 行）+ `src/styles.css` + `vite.config.ts` + `biome.json` +
`scripts/verify-tracer.mjs`。Fable 输出到 `web/src/generated/`（gitignore），
`web/dist/` 也 gitignore。

### 改了什么既有文件

| 文件 | 改动 |
|---|---|
| `Directory.Packages.props` | 新增一段「Web 工程」：Fable.Core 5.0.0 / Fable.Elmish 4.0.0 / Feliz 3.3.3 / Feliz.UseElmish 5.0.0 / Thoth.Json.JavaScript 0.5.0 |
| `.config/dotnet-tools.json` | 新增 dotnet local tool `fable` 5.13.0 |
| `janpo.slnx` | 加 `src/Janpo.Web` |
| `.gitignore` | `web/src/generated/`、`web/dist/` |
| `.editorconfig` | 新增 `[src/Janpo.Web/*.fs]` → `fsharp_multiline_bracket_style = stroustrup`（决策 19-C） |
| `flake.nix` | dev shell 加 `nodejs_22` 与 `pnpm` |
| `scripts/check-style.sh` | 扫描目录加 `src/Janpo.Web` |
| `scripts/ci.sh` | `dotnet test` 之后加 `== web ==` 调 `ci-web.sh` |
| `src/Janpo.Cli/Program.fs` | `janpo kyoku` 多打一行 `juni:`（决策 19-E） |
| `README.md` / `.github/workflows/ci.yml` | 文档与 workflow 注释跟上 |

**没碰**：`src/Janpo.Engine/` 一个字节没动，`CONTEXT.md` 与 ADR-0001..0004 没动，
其他票的文件没动。

---

## 3. 双目标对拍（这一票的要害）

### 3.1 票面要求的那一条：种子 1177

**dotnet 侧**

```
$ dotnet run --project src/Janpo.Cli -- kyoku 1177
...
{"type":"hora","actor":0,"target":2,"pai":"5p","fu":30,"fan":3,"hora_points":5800,"deltas":[5800,0,-5800,0],"scores":[30800,25000,19200,25000],"uradora_markers":[]}
scores: 30800 25000 19200 25000
juni: 1 2 4 3

$ dotnet run --project src/Janpo.Cli -- game 1177
...
{"type":"end_game"}
kyokus: 6
scores: 29800 24000 22200 24000
juni: 1 2 4 3
display: 1位 座位0 29800  2位 座位1 24000  3位 座位3 24000  4位 座位2 22200（东风战）
```

**浏览器侧**（无头 Chrome 读页面 DOM 的可见文本，`web/scripts/verify-tracer.mjs`）

```
janpo —— 浏览器里的第一颗曳光弹
同一套 F# 引擎源码，经 Fable 编成 JS 在这里跑。下面的点数与顺位由浏览器算出，应与 dotnet 侧同种子的 CLI 输出逐项相同。
种子 [1177]  [重跑]

一局（Kyoku）          janpo kyoku 1177
scores   30800 25000 19200 25000
juni     1 2 4 3
kyokus   1
座位 点数 顺位 / 0 30800 1位 / 1 25000 2位 / 2 19200 4位 / 3 25000 3位
mjai 事件 146 条（头尾各三条）

一整场（东风战）        janpo game 1177
scores   29800 24000 22200 24000
juni     1 2 4 3
kyokus   6
座位 点数 顺位 / 0 29800 1位 / 1 24000 2位 / 2 22200 4位 / 3 24000 3位
mjai 事件 940 条（头尾各三条）
```

**闸门的输出**（`./scripts/ci-web.sh` 的最后一段，CI 里跑的就是它）

```
种子 1177，浏览器 /usr/bin/google-chrome-stable
dotnet 侧 vs 浏览器侧：
  ✓ kyoku.scores 30800 25000 19200 25000
  ✓ kyoku.juni   1 2 4 3
  ✓ game.scores 29800 24000 22200 24000
  ✓ game.juni   1 2 4 3
  ✓ game.kyokus 6
浏览器内的引擎与 dotnet 侧逐项相同 ✓
```

种子 1177 是用 `dotnet fsi` 直调引擎扫 1..2000 挑的（决策 19-D）：单局以 30符3飜5800 的荣和终，
四家点数互异；整场打满 6 局（两次连庄），终局有两家同为 24000，顺位要靠起家方向拆分。
对照组：种子 42 两侧都是 25000×4，拿它对拍等于什么都没验。

### 3.2 加强证据：整条事件流逐字对拍

页面只显示几个数，说服力有限。所以另跑了一轮**不进仓库的**核对（脚本在 `/tmp/janpo-xcheck/`）：
拿 Fable 的输出直接在 node 里调 `Tracer_kyoku` / `Tracer_game`，把每条 mjai 事件 JSON 打出来，
与 CLI 的 stdout 逐行 `diff`（剔掉 CLI 独有的 `start_game` 与 `display:` 两行）。

| 批次 | 运行数 | 结果 |
|---|---|---|
| 挑出来的特殊种子（1177 / 157 / 1240 / 1420 / 1159 / 42 / 7 / 1 / 2 / 3 / 12345 / 99999） | 24 | 全部逐字相同 |
| 种子 1..60 | 120 | 全部逐字相同，0 差异 |

覆盖到的事实：`tehais` 配牌顺序、`tsumo` / `dahai` / `pon` / `chi` / `kan` 的字段、
`reach` / `reach_accepted`、`hora` 的 `fu` / `fan` / `hora_points` / `deltas` / `scores`、
`ryukyoku` 的 `reason` 与听牌料 `deltas`、`end_kyoku` / `end_game`。
**也就是说洗牌（xorshift32）、随机选手的选择序列、向听、役、符、点数授受在两侧完全一致。**

耗时约 32s（120 次运行），跑在 `nice -n 19` 下。

### 3.3 为什么零改动就通了（供 21 票参考）

预期会踩的坑逐条落空：

- `[<Struct>]`（`Rng` / `Seat` / `Shanten` / `Tile`）→ Fable 编成普通 class，语义无差。
- `System.String.IsNullOrEmpty`（`Tile.fs` 两处）→ Fable 有实现。
- **整数溢出**：`Rng` 是 xorshift32，只有 `^^^` / `<<<` / `>>>` / `%`，**没有 uint32 乘法**。
  JS 的 double 会在 32 位乘法上丢精度，那才是真雷；`Rng.fs` 的注释里当初就写了
  「不用 `System.Random`，它两侧实现不同」，这个选择在今天兑现了。
- `List.item`（`Kyoku.randomPlayer` 用它从合法动作集里取）→ 复杂度是 O(n)，但动作集只有个位数长。
- 34 长计数数组的原地循环（`Shanten` / `HandShape` / `Ukeire`）→ 照编。

---

## 4. `pnpm dev` 与 `pnpm build`

```
web/package.json 的脚本
  fable    dotnet fable ../src/Janpo.Web -o src/generated
  dev      dotnet fable watch ... --run node node_modules/vite/bin/vite.js
  build    pnpm run fable && node node_modules/vite/bin/vite.js build
  preview  vite preview
  verify   node scripts/verify-tracer.mjs
  check    biome ci --error-on-warnings .
  format   biome format --write .
```

**HMR 实测**：起 `pnpm run dev`，改 `src/Janpo.Web/App.fs` 里的 `Html.h1` 文本，
轮询 `http://localhost:5173/src/generated/App.js`——**约 6s 后**（Fable 增量重编）
vite 供出了新内容；`/@vite/client`（HMR 客户端）返回 200。

**产物**：

```
dist/index.html                   0.66 kB │ gzip:  0.51 kB
dist/assets/index-BTfcmo_4.css    1.30 kB │ gzip:  0.62 kB
dist/assets/index-C8Brj74w.js   279.23 kB │ gzip: 88.10 kB
```

279 kB 里是**整个引擎 + Feliz + React**。`base: "./"`，可静态托管在任意子路径。
对比 18 票量到的 pi-ai provider chunk（171 kB / gzip 44 kB），量级相当，M1 的总包不会失控。

**pnpm 的 `ignored build scripts` 坑**：`web/pnpm-workspace.yaml` 里显式 `allowBuilds`
放行了 `esbuild` 与 `@biomejs/biome`，所以 `pnpm install` 是干净的。
但所有脚本仍然走 `node node_modules/<pkg>/...` 的直呼路径而不是 `pnpm exec`，
免得换机器时再撞一次。

---

## 5. CI

`./scripts/ci.sh` 现在是：

```
dotnet --version → 引擎依赖名单 → dotnet tool restore → fantomas --check →
风格闸门 → build（四个工程）→ test（555 个）→ web → 全绿
```

`== web ==` 转调 `scripts/ci-web.sh`，它有四道：

1. `pnpm install --frozen-lockfile`
2. `biome ci --error-on-warnings .`（TS/JS 的格式 + lint）
3. `pnpm run fable`（Fable 编译）
4. `vite build` + `node scripts/verify-tracer.mjs`（浏览器内对拍）

实测：

| 跑法 | 结果 | 耗时 |
|---|---|---|
| `./scripts/ci.sh`（宿主机 dotnet 10.0.111） | 全绿，555 tests | 47s |
| `nix develop --command ./scripts/ci.sh`（dotnet 10.0.302 / node 22.23.2 / pnpm 11.18.0） | 全绿 | 47s |

**nix flake 没有偏离 RUNBOOK 第 7 条**：`nix develop` 首次拉 node/pnpm 约 5s（全命中二进制缓存）。

**引擎依赖闸门**：`ENGINE_ALLOWED_PACKAGES` 一个字没改，
`Thoth.Json.JavaScript` / `Feliz` / `Fable.Elmish` 全在 `Janpo.Web.fsproj` 里。

---

## 6. 关键取舍

全部七条决策记在 `DECISIONS.md` 的「## 19」段（19-A .. 19-H）。这里只点最容易有异议的三条：

1. **19-C：`.editorconfig` 为 `src/Janpo.Web/` 单开 stroustrup。** 代价是一个仓库两种 record
   形状。理由是 Feliz 的嵌套 list DSL 在 aligned 下每层多缩进 8 格，实测同一段视图最深处
   32 格 vs 16 格；22 票会写大量视图。**这条最值得人复核**。
2. **19-E：给 `janpo kyoku` 加 `juni:` 一行。** 票的验收要「顺位逐项相同」，而原来 dotnet 侧
   没有这个数。改的是 CLI 的打印，不是引擎。
3. **19-D：曳光弹跑两条（一局 + 一整场）。** 票只要求一局；多跑一整场是因为顺位只在终局精算里
   才是一等概念，且整场覆盖连庄 / 本场 / 供托结转。

---

## 7. code-review 结论（fixed point `bb820d78`）

两轴自查，逐条如下。

### Standards 轴（对照 `docs/agents/fsharp-style.md` 与 README 的加新模块约定）

| 规则 | 结论 |
|---|---|
| 1 / 3 嵌套应用不许从里往外读 | 通过。`Tracer.kyoku` / `game` 是 `Rng.ofSeed seed \|> ... \|> Result.mapError ... \|> Result.map ...` 一条流；`eventJson` 用 `>>` |
| 2 `fun x -> f (g x)` → `>>` | 通过。`prop.onChange (SeedEdited >> dispatch)`、`Tracer.eventJson` 都是组合式；`check-style.sh` 的机械检查也过 |
| 4 不许强行管道 | 通过。`init` 里的 record 构造、`update` 的 `{ model with ... }` 都保持原样 |
| 5 命令式边界 | 通过。新代码 0 个 `let mutable`，引擎的预算 2 没动 |
| 8 `f (atom)` 多余括号 | 通过（闸门锁零） |
| 一概念一文件 + 段落注释 | 通过。三个文件各一职，模块内按 `// ---- 段 ----` 分 |
| 渲染层出口 | 通过。中文只在 Feliz 视图与 `KyokuError.toDisplay` 的消费处，不进数据 |
| 错误是值 | 通过。`Tracer.*` 返回 `Result<Trace, string>`，页面 `match` 它 |
| 术语表（CONTEXT.md） | 通过。`Kyoku` / `Juni` / `Seat` / `Ruleset` / `Event` 照用；`Trace` / `Traces` 是本票自造的工具类型，不与领域词冲突 |

**Blocking：无。**

**nitpick（只记录，未改）**：

- `App.fs` 的 `seatTable` 里 `List.zip3 [ 0 .. List.length trace.Scores - 1 ] trace.Scores trace.Juni`
  自己造了座位序号，而引擎有 `Seat.indexed`。没用它是因为 `Seat.indexed` 需要 `Seat` 类型进视图，
  而 19 票的视图刻意只吃 `int list`。**22 票做真牌桌时应该改用 `Seat.indexed`。**
- `Trace.Kyokus` 在 `Tracer.kyoku` 里恒为 1，是个只为对齐两张卡片而存在的字段。
  22 票如果重构 `Trace`，这个字段可以去掉。
- `verify-tracer.mjs` 的端口 4179 是写死的。CI 单线程跑没问题，并行跑两个会撞。

### Spec 轴（对照票面、`spec.md` 的 UI 段与里程碑切分、ADR-0002/0003/0004、M1 增量约束）

| 要求 | 结论 |
|---|---|
| 票面 6 条验收 | 全部达成（§1） |
| 「不许为 Fable 分叉引擎逻辑」 | 达成，`#if FABLE_COMPILER` 零处，引擎零改动 |
| M1 约束 3：引擎依赖白名单不许放宽 | 达成，`ENGINE_ALLOWED_PACKAGES` 未改 |
| M1 约束 2：JS 侧要有格式化器与闸门 | 达成（Biome，接进 `ci.sh`） |
| M1 约束 5：网络安装有预算 | 达成。pnpm 装 22 个包（一次），NuGet 装 5 个包（一次），Fable tool 走本地缓存。单次下载最大 < 10 MB |
| M1 约束 6：CI 不调真实 LLM API | 达成。本票**完全没引入 pi-ai**（那是 23 票） |
| spec「UI 状态 = fold(事件前缀)，无独立游戏状态」 | 达成。`Model` 只存种子文本与引擎返回的 `Trace`，顺位都由 `Game.settle` 给 |
| spec「渲染用 DOM，不用 Canvas」 | 达成 |
| ADR-0002（Paifu 是唯一可分享物） | 未违反。本票不做分享，事件流只是显示 |
| ADR-0003（Host 驱动） | 未违反。页面里跑的是引擎自带的随机选手 |
| ADR-0004（Ruleset 是一等输入） | 达成。`Model.Ruleset` 是显式字段，`Tracer.*` 都收 `Ruleset` 参数，没有硬编码规则字面量 |

**Blocking：无。**

**留给人的待审项**（不阻塞，写进 DECISIONS 了）：

1. **19-C 的 stroustrup**：一个仓库两种 record 形状，值不值。
2. **19-E 给 `janpo kyoku` 加 `juni:`**：是否算 CLI 的行为变更（无其他消费者，无测试断言它）。
3. **19-H 的 `JANPO_NO_BROWSER=1` 逃生口**：它让 CI 能在没浏览器的环境里「绿」，
   而那个绿不含 19 票的验收。要不要收紧成「必须显式声明」由人定。

---

## 8. 后续票要知道的接口事实

- **Fable 输出**：`web/src/generated/`（gitignore）。`pnpm run fable` 重建。
  模块布局镜像 fsproj：`Tracer.js` / `App.js` / `Main.js` / `Janpo.Engine/*.js` /
  `fable_modules/`（Fable 运行时与 Thoth）。
- **导出名**：F# 的 `namespace Janpo.Web` + `module Tracer` → JS 的 `Tracer_kyoku`、
  `Tracer_game`、`Tracer_defaultSeed`、`Tracer_eventJson`；`module Janpo.Web.Main` 的顶层
  `let mount` → `export function mount`。**F# list 到 JS 要 `List.toArray`**
  （`fable_modules/fable-library-js.*/List.js` 的 `toArray`），`Result` 是 `{tag, fields}`。
- **dev**：`cd web && pnpm install && pnpm run dev` → http://localhost:5173，改 `.fs` 约 6s 生效。
- **JS 调引擎**：能调，但**别养成习惯**——ADR-0005 定的方向是 F# 调 TS。
  唯一允许的 TS→F# 调用是 `main.ts` 里那行 `mount("janpo-root")`。
- **格式化器**：Biome 2.5.8（`web/biome.json`）。提交前 `cd web && pnpm run check`。
  F# 侧仍是 `dotnet fantomas .` + `scripts/check-style.sh`。
- **CI 新增命令**：`scripts/ci-web.sh`（也被 `scripts/ci.sh` 调）。
  单跑 JS 侧用它，约 10s。
- **验收脚本可复用**：`web/scripts/verify-tracer.mjs` 的 `runCli` / `readBrowser` 两个函数
  就是 21 票黄金用例的骨架，`--seed N` 已经通了。
