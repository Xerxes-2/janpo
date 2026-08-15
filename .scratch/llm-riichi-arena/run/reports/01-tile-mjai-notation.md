# 01 — 骨架与 Tile 的 mjai 记法 · 实现报告

**Status:** done（票已标 `ready-for-human`）
**Change:** `szpzyvsk` — `feat: 引擎骨架与 Tile 的 mjai 记法`
**Fixed point:** `5b514310`
**验证:** `nix develop --command ./scripts/ci.sh` 全绿（dotnet 10.0.302 / uv 0.12.1 均来自 flake，
26 个测试通过，`fantomas --check` 干净，引擎依赖白名单通过）

---

## 做了什么

### 工程划分（后 13 票照抄这个骨架）

```
src/Janpo.Engine/          规则引擎库，namespace Janpo。**限 Fable 兼容子集**
src/Janpo.Cli/             无头驱动入口，AssemblyName=janpo。只解析参数与打印
tests/Janpo.Engine.Tests/  xunit 作 runner，FsCheck 属性测试为主力
scripts/ci.sh              CI 关卡：依赖白名单 + fantomas --check + build + test
flake.nix / flake.lock     dev shell：dotnet-sdk_10 (10.0.302) + uv (0.12.1)
Directory.Packages.props   所有 NuGet 版本集中管理（CPM）
Directory.Build.props      net10.0 / TreatWarningsAsErrors / --warnon:1182
.editorconfig              Fantomas 规则（max_line_length=120、DU 前置 `|`、aligned 括号）
.config/dotnet-tools.json  Fantomas 7.0.5 钉版本
.github/workflows/ci.yml   在 nix dev shell 里跑 scripts/ci.sh
```

### Tile（`src/Janpo.Engine/Tile.fs`，唯一的引擎源文件）

- `Suit` = `Manzu` / `Pinzu` / `Souzu` / `Jihai`
- `Tile` 是 `[<Struct>]` **私有 record**（`Kind: int` 0-33 + `Akadora: bool`）。只能经
  `parse` / `tryCreate` / `ofKindIndex` 构造 ⇒ `8z`、`3mr` 这类不存在的牌在类型层不可表示。
- 记法：`toMjai` / `parse`（严格）、`toMjaiMany` / `parseMany` / `sort` / `canonicalize`（记法列）。
- 拆解：`suit` / `number` / `isAkadora` / `deaka` / `kindIndex`（0-33，供 34 长度计数数组）。
- 集合：`kinds`（34）/ `akadoraKinds`（3）/ `all`（37，升序）/ `KindCount`。
- JSON：`encoder` / `decoder`（Thoth.Json.Core，牌在 mjai wire 上就是一个字符串）。
- 错误：`TileParseError`（5 个具名 case）与 `TileListParseError`（第几个 token 出的错），
  **全是值，不抛异常**。
- 渲染出口：`Tile.toDisplay`、`TileParseError.toDisplay`、`TileListParseError.toDisplay`。
  中文只在这三处出现。

### CLI

```
$ janpo tile "1z 5sr 5s 9s 3m"     $ janpo tile "1m 3mr"
3m 5s 5sr 9s 1z                    第 2 个记法「3mr」无效：万子的 3 不是红宝牌，红宝牌只有 5mr / 5pr / 5sr
count: 5                           （exit 1）
display: 3万 5索 赤5索 9索 东
```

退出码：0 正常 / 1 记法错误 / 2 用法错误。

### 测试（26 个，全绿）

- `TileNotationTests.fs` — 14 个具名用例：34 种正牌的 mjai 顺序、红宝牌、`kindIndex`、
  `tryCreate` 的拒绝、11 类非法记法、分隔符、规范形排序、错误定位、`toDisplay` 的 12 个字面。
- `TileProperties.fs` — 9 条 FsCheck 属性：往返不变、幂等、任意字符串不抛异常、
  规范化不增删牌、规范形升序、记法列往返、`kindIndex`/`ofKindIndex` 互逆、
  `tryCreate` 与三件套互逆。
- `TileJsonTests.fs` — 3 个：编码为 mjai 字符串、非法记法解码为错误、JSON 往返属性。
- `TileGenerators.fs` — `TileArbitraries`（Tile、合法单张记法、合法记法列（随机分隔符与顺序））。

**属性测试做过变异验证**：把 `sort` 改成 `List.sortDescending`、`deaka` 改成恒等之后，
5 个测试（含 2 条属性）确实变红；改回即绿。属性不是空转的。

## 关键取舍

| 取舍 | 选了什么 | 为什么 |
|---|---|---|
| Tile 表示 | 私有 struct record（kind + aka） | 不存在的牌不可表示；34 计数数组零成本；代价是下游要经 `suit`/`number` 才能模式匹配 |
| 解析严格度 | 最严：拒大写、拒空白、拒天凤 `0m`、拒紧凑 `123m` | ADR-0001 要「数据层只有一种记法」，宽松解析会让第二种记法从 CLI 边缘渗回事件流 |
| 红宝牌 | 排序紧跟同种正牌，`kindIndex` 与正牌相同 | 形态判定一律先 `deaka`；索引同一才对得上 34 计数数组 |
| Thoth | 引擎只引 `Thoth.Json.Core`，后端 `Thoth.Json.Newtonsoft` 由 CLI 与测试引 | Thoth v10 之后拆成 Core + 后端；M1 接 Fable 时只在 Fable 工程加 `Thoth.Json.JavaScript`，引擎零改动 |
| 「不引入 Fable 不兼容依赖」 | 写成 `scripts/ci.sh` 里的白名单关卡 | 验收项写在票里会腐坏，写成关卡则每次提交都在验 |
| nixpkgs 源 | flakehub 的 nixpkgs-weekly | 宿主机 Determinate Nix 的 registry 已是这份快照，复用缓存；纯 tarball URL，任何现代 Nix 都能取 |

自主决策共 9 条，逐条记在 `run/DECISIONS.md`（含 2 条待人裁决的提案）。

## Review 结论（两轴，fixed point `5b514310`）

无法派生 sub-agent，按 runbook 自己顺序跑了两轴。

### Standards（对照 `CONTEXT.md`、ADR-0001/0002/0003、`spec.md` 的实现与测试决策、Fowler smell 基线）

**已修（本轮自动修）**

1. **Duplicated Code（真实重复）** — 「哪些牌存在」这条规则同时写在 `tryCreate` 与 `parse` 两处
   （序数范围 + 红宝牌只在 5）。已抽成私有 `create : Suit -> int -> bool -> Result<Tile, TileParseError>`，
   两边都从它构造，规则只剩一份。副作用：`AkadoraNotAllowed` 的载荷从 `notation: string`
   改为 `number: int * suit: Suit`（信息量等价，且与 `NumberOutOfRange` 同形），测试同步改。
   **没有放宽任何断言**。
2. **CI 关卡在 darwin 上会挂** — `scripts/ci.sh` 用了 GNU 专有的 `grep -oP`，而 flake 声明了
   darwin 系统。已换成 POSIX `sed -n 's/.../\1/p'`。关卡本身做过反向验证：给引擎工程塞一个
   `Newtonsoft.Json` 后 `ci.sh` 确实退出 1。

**只记录不修（nitpick / 判断题）**

3. `Tile.fs` 顶部 `open Thoth.Json.Core`，即领域类型文件依赖了 JSON 库——有 Divergent Change 的味道。
   保留的理由：牌在 mjai wire 上的形式就是它的 mjai 记法，编解码与记法同源。若后续票觉得碍事，
   把 `encoder`/`decoder` 拆到 `TileJson.fs` 是零风险的机械操作。
4. 「`match suit with`」在 `suitOffset`、`maxNumber`、`suitSuffix`、`toDisplay`、`suitDisplay`
   出现 5 次（Repeated Switches）。判断：四个 case 的枚举映射，各自语义不同，合并只会更绕。
5. `.github/workflows/ci.yml` 把 action 钉在 `@main`（供应链 nit）。仓库还没有远端，先不折腾。
6. CLI 的用法文案与「未知命令」提示是中文。判断：CLI 输出本身就是渲染面，且**牌**的渲染
   全部走 `Tile.toDisplay`，没有第二条产出中文牌名的路径。

**未发现**违反 CONTEXT.md 术语表或 ADR 的地方：标识符全是罗马字（`Manzu`/`Akadora`/`deaka`/`Tile`），
人类可读中文只在三个 `toDisplay` 里，事件流/牌谱/测试固件一律 mjai 记法。

### Spec（对照票 01 的 9 条验收 + `spec.md` 相关段落）

- 9 条验收**全部满足**，逐条见票文件的勾选。
- **部分满足 1 条**：「CI 在同一个 dev shell 里跑…」——workflow 已写，但仓库没有远端，
  GitHub Actions 从没真跑过。等价验证是本地 `nix develop --command ./scripts/ci.sh` 全绿。
- **超出票面的部分（scope creep，均为有意）**：
  - `Tile.encoder`/`decoder`（票的验收没要 JSON，但票面把「JSON 用 Thoth.Json」写进了工程划分，
    且 02 的验收「Event 的 JSON 编解码往返不变」马上要用；先把双目标的引用方式钉死，省 02 一次返工）。
  - `tryCreate` / `kindIndex` / `ofKindIndex` / `deaka` / `kinds` / `akadoraKinds` / `all`
    （02 要建牌山、03 要 34 计数数组，都得有；每个都有测试覆盖）。
  - CLI 多打了一行 `display:`（票只要求规范形与牌数），用来把渲染出口走通一次。
- **未发现**实现与票面相悖的地方。

## 留给人的待审项

1. `run/DECISIONS.md` 的 9 条决策，特别是两条提案：
   - **01-A**：把 `Manzu`/`Pinzu`/`Souzu`/`Jihai`/`Akadora`/`deaka`/`kindIndex` 补进 `CONTEXT.md`
     的「牌与手牌」一节，否则后续票会各写各的（`Honor`/`Red`/`tileId`）。
   - **01-B**：把「所有产出中文的函数一律叫 `toDisplay`，集中在文件末尾的渲染段」写进 ADR-0001
     的 Consequences，让「渲染层是单向出口」有个可 grep 的判据。
2. GitHub workflow 没在真 CI 上跑过（无远端）。
3. `TreatWarningsAsErrors` 全仓打开——若后续票被某个无害警告卡住，是我这票立的规矩，可以推翻。
4. 格式风格（`fsharp_multiline_bracket_style = aligned`，多行 record 展开成三行）是我定的，
   13 票之后再改会产生一次全仓 reformat，要改趁早。

## 给后续 13 票的结构约定

1. **新引擎模块**：一个概念一个文件放 `src/Janpo.Engine/`，`namespace Janpo`，
   按依赖顺序加进 `Janpo.Engine.fsproj` 的 `<Compile Include=... />`（F# 编译顺序即依赖顺序）。
   引擎工程**不许**加不在 `scripts/ci.sh` 白名单里的包；要加先确认 Fable 能编译它，
   连同一条 DECISIONS 记录一起改名单。
2. **类型 + 同名模块同文件**：`type Foo` 与 `[<RequireQualifiedAccess>] module Foo` 写一起，
   模块内分段注释：`// ---- 构造 ----`、`// ---- 拆解 ----`、`// ---- mjai 记法 ----`、
   `// ---- JSON（mjai wire） ----`、`// ---- 渲染层出口（ADR-0001） ----`。
3. **渲染出口一律叫 `toDisplay`**，放文件末尾的渲染段。引擎判定、事件流、牌谱、测试固件
   都不得消费它的输出；引擎内部诊断串（Decoder 失败信息等）用英文。
4. **错误是值**：失败返回 `Result<_, 具名错误 DU>`，DU case 带结构化载荷（不是拼好的字符串），
   人话在 `toDisplay` 里拼。
5. **测试组织**：`tests/Janpo.Engine.Tests/` 下按模块三件套 ——
   `<Module>Tests.fs`（具名用例）、`<Module>Properties.fs`（FsCheck 属性，模块上标
   `[<Properties(Arbitrary = [| typeof<XxxArbitraries> |])>]`）、`<Module>Generators.fs`（生成器）。
   测试名用中文写清「断言的是什么行为」，只测公开 API。
   新文件同样要按顺序加进测试工程的 `<Compile>` 列表。
6. **CLI 加子命令**：在 `Program.fs` 的 `main` match 上加一支，逻辑写进引擎库，CLI 只做参数与打印。
   退出码沿用 0 / 1（数据错误）/ 2（用法错误）。
7. **提交前**：`dotnet fantomas .` → `./scripts/ci.sh` → `jj commit`。
   `./scripts/ci.sh` 就是 CI 的全部关卡，本地绿了 CI 才会绿。
8. **测试期的外部工具**（03 的向听 oracle、13 的牌谱转换）走 `uv run --with <pkg>`，
   uv 已在 dev shell 里；不得成为引擎或 CLI 的运行时依赖。
