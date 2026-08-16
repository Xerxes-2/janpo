# janpo

LLM 日麻对战平台 —— F# 规则引擎（Fable → JS，浏览器内运行）+ TypeScript Agent 层与 UI。

术语以 [`CONTEXT.md`](./CONTEXT.md) 为唯一权威（罗马字日麻术语），牌记法以
[ADR-0001](./docs/adr/0001-mjai-notation-and-romaji-identifiers.md) 为准（mjai `1m-9m` / `1p-9p` /
`1s-9s` / `1z-7z`，红宝牌 `5mr` / `5pr` / `5sr`）。

## 开发环境

工具链由 nix flake 钉住（dotnet SDK、node/pnpm 与 uv），CI 与本地用同一个 shell：

```sh
nix develop            # 进 dev shell：dotnet、node、pnpm、uv
dotnet tool restore    # 装 Fantomas 与 Fable（dotnet local tool，版本在 .config/dotnet-tools.json）
```

宿主机上若已有匹配 `global.json` 的 dotnet SDK（10.0.1xx 及以上特性带），不进 dev shell 也能跑；
但**版本以 flake 为准**，CI 只认 dev shell 里的那一套。

## 常用命令

```sh
./scripts/ci.sh                       # CI 的全部关卡：dotnet 侧 + JS 侧，一条命令两侧全绿
./scripts/ci-web.sh                   # 只跑 JS 侧：Biome + Fable + Vite + 浏览器内对拍 + 黄金用例
dotnet build janpo.slnx               # 构建五个工程
dotnet test janpo.slnx                # 跑测试（xunit + FsCheck）
dotnet fantomas .                     # 格式化（提交前必跑）
dotnet fantomas --check .             # 只检查，CI 用这个
dotnet run --project src/Janpo.Cli -- --help
```

浏览器侧（M1 起）—— 命令都在 `web/` 下跑：

```sh
cd web
pnpm install
pnpm run dev       # Fable watch + Vite dev server（HMR），改 .fs 约 6s 后页面更新
pnpm run build     # Fable 编译 + Vite 打包 → web/dist（可静态托管）
pnpm run verify    # 无头验收：浏览器内跑同种子的一局 / 一整场，与 CLI 逐项对照
pnpm run verify:golden  # 无头验收：浏览器内跑黄金用例，与 tests/fixtures/golden/ 逐字段逐行对照
pnpm run check     # Biome（TS/JS 的格式 + lint）
pnpm run format    # Biome 写回格式
```

两条 `verify` 都需要一个 Chrome/Chromium：优先 `$JANPO_CHROME`，其次 playwright 自带的，
最后 `/usr/bin/google-chrome-stable` 一类系统路径。

黄金用例（双目标防漂移）两侧读的是同一份数据，维护在 dotnet 侧：

```sh
dotnet run --project src/Janpo.Cli -- golden check tests/fixtures/golden/dual-target.json
dotnet run --project src/Janpo.Cli -- golden write tests/fixtures/golden/dual-target.json  # 重跑并写回期望
```

怎么加一条用例见 `tests/fixtures/golden/README.md`。

CLI 目前的能力：

```sh
$ dotnet run --project src/Janpo.Cli -- tile "1z 5sr 5s 9s 3m"
3m 5s 5sr 9s 1z
count: 5
display: 3万 5索 赤5索 9索 东
```

## 仓库结构

```
src/Janpo.Engine/        规则引擎库。**限 Fable 兼容的 F# 子集**，JSON 走 Thoth.Json.Core
src/Janpo.Cli/           无头驱动入口（dotnet only）。只做参数解析与打印，逻辑一律回引擎库
src/Janpo.Golden/        黄金用例：**两个目标共用**的那段「怎么跑一条用例」与「怎么对照」。同样限 Fable 子集
src/Janpo.Web/           浏览器宿主（Fable → JS）：Feliz + useElmish 的页面。Fable 运行时后端只能在这里
web/                     Vite 应用：index.html、一行 TS 入口、样式与无头验收脚本
tests/Janpo.Engine.Tests/ 引擎测试：xunit 作 runner，FsCheck 属性测试为主力
tests/fixtures/golden/   黄金用例的**数据**（两侧读同一份），用法见同目录 README
scripts/ci.sh            CI 关卡，本地与 CI 同一份
scripts/ci-web.sh        JS 侧的那五道，被 ci.sh 调，也能单跑
flake.nix                dev shell（dotnet SDK + node/pnpm + uv）
.editorconfig            Fantomas 的 F# 格式规则（Web 工程另开 stroustrup，因为 Feliz 是嵌套 DSL）
web/biome.json           Biome 的 TS/JS 格式与 lint 规则
Directory.Packages.props 所有 NuGet 版本集中管理
```

引擎工程的依赖有白名单，由 `scripts/ci.sh` 强制：只允许 `FSharp.Core`、`Fable.Core`、
`Thoth.Json.Core`。要加包先确认 Fable 能编译它，再改名单并留下决策记录。

## 加新模块时的约定

- **引擎模块**：一个概念一个文件放在 `src/Janpo.Engine/`，并按依赖顺序加进 `Janpo.Engine.fsproj`
  的 `<Compile Include=... />`（F# 的编译顺序就是依赖顺序）。命名空间统一 `Janpo`。
- **类型 + 同名模块**：类型（`Tile`）与其操作模块（`[<RequireQualifiedAccess>] module Tile`）
  写在同一个文件里，模块内按「构造 / 拆解 / 记法 / JSON / 渲染」分段。
- **渲染层出口**：所有产出人类可读中文的函数一律叫 `toDisplay`，集中在文件末尾的渲染段，
  引擎判定、事件流、牌谱与测试固件都不得消费它们的输出（ADR-0001）。
- **错误是值**：解析与判定失败返回 `Result<_, 具名错误 DU>`，不抛异常。
- **测试**：与被测模块同名，`tests/Janpo.Engine.Tests/<Module>Tests.fs` 放具名用例、
  `<Module>Properties.fs` 放 FsCheck 属性、`<Module>Generators.fs` 放生成器；
  测试名用中文写清断言的是什么行为。属性测试的 `Arbitrary` 用
  `[<Properties(Arbitrary = [| typeof<TileArbitraries> |])>]` 注册。
- **测试期的外部工具**（Python oracle、牌谱转换）走 `uv run --with <pkg>`，
  不得成为引擎或 CLI 的运行时依赖。
