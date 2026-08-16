# 21 — 双目标黄金用例冒烟

**结论：done。没有发现任何双目标差异。** 39 条黄金用例、190 个字段、1437 行，
dotnet 侧（`dotnet test` 的 `GoldenSuiteTests` 与 `janpo golden check`）与浏览器侧
（`web/scripts/verify-golden.mjs`，Chrome headless 里跑 Fable 的输出）**逐字段逐行相同**。
用例是数据（`tests/fixtures/golden/dual-target.json`），两侧读同一份、跑同一段 F#。
`./scripts/ci.sh` 一条命令全绿。

---

## 1. 验收逐条

| 验收 | 状态 | 证据 |
|---|---|---|
| 用例以数据形式落盘，两侧读同一份，不写死在任一侧代码里 | ✅ | `tests/fixtures/golden/dual-target.json`（输入 `run` 与期望 `expect` 都在数据里）；dotnet 侧 `GoldenSuiteTests` + `janpo golden`，JS 侧 `verify-golden.mjs`，读的是同一个路径 |
| 覆盖 Rng 取数序列与牌山头几张 | ✅ | `rng-*` 4 条（含 M0 钉住的三行取数序列、负种子、bound 136 的洗牌）、`wall-*` 2 条（头 14 张 / 宝牌指示牌 / 岭上 / 四家配牌） |
| 覆盖整数除法与取整（符与点数） | ✅ | `points-*` 9 条（每笔切上到 100、跳满的 3/2、本场平摊、包的平分、切上满贯开/关同一副手牌）、`hora-*` 7 条（20/25/30/40 符与役满） |
| 覆盖集合与 Map 的遍历顺序 | ✅ | `collection-*` 2 条（`Set` / `Map.toList` / `List.sort` / `distinct` / `groupBy` / 中文串进 `Set`）；`shanten-*` 5 条走 34 长计数数组那条路 |
| 覆盖字符串与牌记法往返 | ✅ | `notation-*` 4 条（含全 34 种 + 3 红的排序与 `canonicalize` 往返、两条错误文案） |
| 至少一场完整对局，事件流逐条相同、终局点数与顺位相同 | ✅ | `game-1177`：940 条 mjai 事件逐条钉住 + `kyokus` / `scores` / `juni`；另有 `kyoku-1177`（146 条）与 `kyoku-42`（152 条） |
| JS 侧的跑法进 CI，与 dotnet 侧同一个关卡 | ✅ | `scripts/ci-web.sh` 第五道；`scripts/ci.sh` 一条命令跑两侧 |
| 任一侧漂了就红，且报错指得出漂在哪个用例的哪个字段 | ✅ | 报错形如「用例 game-1177：字段 events 第 137 行：期望「…」，实际「…」」。**闸门本身有反向测试**（见 §4） |

---

## 2. 做了什么

### 新增 `src/Janpo.Golden`（两个目标共用的那段代码，已进 `janpo.slnx`）

- `GoldenCase.fs` — 数据模型与编解码：`GoldenSuite` / `GoldenCase` / `GoldenRun`（10 种用例）/
  `GoldenRuleset` / `GoldenHora` / `GoldenPoints`。**期望是有序键值对而不是 `Map`**：
  这份数据自己不该依赖集合的遍历顺序，那正是被它测的东西之一。
- `GoldenObservation.fs` — 「一条用例 → 一串字段」。一律不抛异常：解析不了、算不出来都变成一条
  `error` 字段，于是**错误文案本身也被钉住**（字符串语义同样要防漂）。
- `GoldenCheck.fs` — 逐字段逐行对照、`GoldenDrift` 的五种形态与渲染、`GoldenReport`。

工程受与引擎同一条约束：只准 `Thoth.Json.Core`、只准引 `Janpo.Engine`。
**JSON 的具体后端由宿主注入**（`IEncodable -> string` 作参数传进来），dotnet 侧是 Newtonsoft、
JS 侧是 Thoth.Json.JavaScript——所以这段代码 Fable 编得动。

### 新增用例数据 `tests/fixtures/golden/dual-target.json`（39 条）+ 同目录 `README.md`

`README.md` 写清「怎么加一条用例」与 10 种 `kind` 各盯什么。

### 两侧的入口

| 侧 | 入口 | CI 里谁跑 |
|---|---|---|
| dotnet | `janpo golden check <文件>` / `janpo golden write <文件>`（`src/Janpo.Cli/Program.fs`） | — |
| dotnet | `tests/Janpo.Engine.Tests/GoldenSuiteTests.fs`（12 条测试） | `dotnet test` |
| 浏览器 | `src/Janpo.Web/Golden.fs` 的 `check : string -> string`（用例文件原文 → 报告 JSON） | `web/scripts/verify-golden.mjs` |

### 改了什么既有文件

| 文件 | 改动 |
|---|---|
| `scripts/ci.sh` | Fable 依赖白名单从「引擎工程」推广到「所有会被 Fable 编的工程」，并新增**工程引用**白名单（决策 21-6） |
| `scripts/ci-web.sh` | 第五道：浏览器内黄金用例 |
| `web/package.json` | `verify:golden` |
| `web/scripts/verify-tracer.mjs` | 浏览器查找抽到 `web/scripts/chrome.mjs`，两个闸门共用 |
| `src/Janpo.Cli/Program.fs` | `janpo golden` 子命令与 usage |
| `janpo.slnx`、两个 `.fsproj`、`README.md` | 新工程 / 新固件 / 新命令 |

**没碰**：`src/Janpo.Engine/` 一个字节没动（`jj diff src/Janpo.Engine/` 为空），
`CONTEXT.md` 与 ADR 没动，其他票的文件没动。

---

## 3. 关键取舍

七条记在 `DECISIONS.md` 的「## 21」段（21-1..21-7），这里只补两句背景：

- **为什么不复用 19 票的 `verify-tracer.mjs`**：那道跑的是 **Vite 打包后的产物**（`dist/`，
  文件名带哈希、模块打成一坨），点名 `import` 不到某个模块。黄金用例这道用 vite 的 **dev server**
  托管源码形态的 Fable 输出，再在页面里 `import("/src/generated/Golden.js")`。
  代价是这一道跑的是未打包的输出；两道合起来，「Fable 的输出」与「Vite 的产物」都被跑过。
  好处是不必为闸门新增 HTML 入口或往 `mount` 里塞测试钩子。
- **golden 文件的对错靠人看 diff**：`write` 只是把当前引擎的输出誊上去，它不判断对错。
  若有人在引擎错的状态下跑了 `write`，两侧会一起错——这是所有 golden 方案的固有边界。
  防线是：`write` 会打印「**逐行看 diff**再提交」，且 `expect` 的每一行都是人能读懂的形态
  （mjai JSON、中文文案、空格分隔的整数），不是哈希。

---

## 4. 闸门本身被验过

一个不会红的闸门比没有闸门更糟，所以 `GoldenSuiteTests` 里有 6 条反向测试：
改坏一行必须报 `Line(字段, 行号, 期望, 实际)`；一整场对局改坏第 137 条事件必须精确报第 137 行；
空期望报 `NoExpectation`；引擎多产出字段报 `UnexpectedField`；期望里多写字段报 `MissingField`；
id 重复报 `DuplicateId`。

实测（把用例文件改坏两处后跑两侧）：

```
$ node scripts/verify-golden.mjs /tmp/golden-corrupt.json     # 浏览器侧
用例 rng-seed-0：字段 draws 第 0 行：期望「21 51 31 12 31 65 68 99」，实际「21 51 31 12 31 65 68 14」
用例 game-1177：字段 events 第 137 行：期望「{"type":"tsumo","actor":9,"pai":"9z"}」，实际「{"type":"tsumo","actor":3,"pai":"5m"}」
exit=1
$ janpo golden check /tmp/golden-corrupt.json                  # dotnet 侧
（同样两行，exit=1）
```

两侧的报错文案一字不差——因为渲染也是同一段 F#。

---

## 5. 跑的数字

| | |
|---|---|
| 用例 / 字段 / 行 | 39 / 190 / 1437 |
| 其中 mjai 事件 | 1238 条（`game-1177` 940、`kyoku-42` 152、`kyoku-1177` 146） |
| 用例文件大小 | 131 KB |
| 浏览器侧一跑 | 约 12 s（含 vite dev server 起停与 Chrome 启动） |
| `dotnet test` | 598 条测试 34 s（其中黄金用例 12 条） |
| 打包产物 | 仍是 279.24 kB / gzip 88.02 kB——**黄金用例的代码没进生产包**（`main.ts` 不 import 它） |

---

## 6. 留给人的待审项

1. **用例文件 131 KB，其中 96% 是那 1238 条事件**。它让「事件流逐条相同」这句话有位置可指，
   代价是 diff 大。若嫌大，可以把 `game-1177` 换成 `kyoku` 级别，但那样一整场（连庄、终局精算、
   `end_kyoku` / `end_game`）就不在闸门里了——我按票面「至少一场完整对局」留着。
2. **`hora-*` 用例的座位约定跟 `janpo yaku` 一样**（和了者坐 0，自风为 `1z` 时它就是亲）。
   副露记法也与 `--naki` 同形，因此一条用例能照抄成一行 CLI 手工复现。
   代价是 `GoldenObservation.parseNaki` 与 CLI 的 `parseNakiSpec` 是两份**形状相同**的解析
   （各约 20 行）。合并要把它挪进引擎，而它是 CLI 的输入格式、不是引擎概念——没做，留给人裁。
3. **`decide` 用例把决策包 JSON 当一整行钉住**（约 2 KB 一行）。漂了会把整行印出来。
   若 23 票之后决策包频繁变形，可以考虑拆成逐字段，但那要给 `DecisionPackage` 写 decoder
   （20 票只写了 encoder）。

---

## 7. Code review（两轴，fixed point `78116659`）

自己顺序跑的（本工作区无法派生 sub-agent）。

**Standards**：无 blocking。检查了 `docs/agents/fsharp-style.md` 的 9 条规则与 Fowler 味道基线：
新增代码里没有 `let mutable`（预算仍是 2，闸门绿）、没有 `fun x -> f (g x)`、
没有多余括号（`check-style.sh` 绿）、`f (g (h x))` 形状的嵌套已在 review 中拆掉一轮
（`string (List.length …)` 一类七处改成 `number` / `joined` 两个具名字段构造子）。
判断项两条，均按「不修，只记录」处理：
(a) `GoldenObservation.parseNaki` 与 CLI 的 `parseNakiSpec` 是 Duplicated Code——理由见 §6.2；
(b) `GoldenRun` 的 10 个 case 在「编码 / 解码 / 跑」三处各有一支 match（Repeated Switches）——
这是本仓库既有的形状（`Event` 的 DU/encoder/decoder 三处、`Action` 的五处），且编译器会把三处都指出来，
故按仓库约定处理，并在 `tests/fixtures/golden/README.md` 写明「加一种 kind 要改三处」。

**Spec**：票面 5 条验收全部落地（§1）。范围外的东西只加了一样——`decide` 用例
（票面没点名，但 20 票的决策包正是 23 票要跨界读的那份，成本一条 case 分支，记在决策 21-5）。
`ci.sh` 的白名单推广（21-6）严格说也是票外，但它守的是 M1 增量约束第 3 条，
不推广的话新工程就是白名单上的一个洞。
