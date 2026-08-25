# 开发

想玩的人看 [README](../README.md) 就够了；这份文件是给要**自己跑一份、改点什么、或者读代码**的人的。

术语以 [`CONTEXT.md`](../CONTEXT.md) 为唯一权威（罗马字日麻术语），牌记法以
[ADR-0001](adr/0001-mjai-notation-and-romaji-identifiers.md) 为准（mjai `1m-9m` / `1p-9p` /
`1s-9s` / `1z-7z`，红宝牌 `5mr` / `5pr` / `5sr`）。为什么是现在这个形状，看 [`adr/`](adr/) 那五条。
写 F# 前读 [`agents/fsharp-style.md`](agents/fsharp-style.md)。

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
./scripts/ci-web.sh                   # 只跑 JS 侧（Biome、tsc、Agent 层用例、prompt 语义不变量、Fable、Vite，再加浏览器内那几道）
./scripts/ci-baseline.sh              # 强 AI 基线那一档（那 6 MB 的产物**在场**那一路；不在就自己造一份，要 cargo）
dotnet build janpo.slnx               # 构建解决方案里的全部工程
dotnet test janpo.slnx                # 跑测试（xunit + FsCheck）
dotnet fantomas .                     # 格式化（提交前必跑）
dotnet fantomas --check .             # 只检查，CI 用这个
dotnet run --project src/Janpo.Cli -- --help
```

浏览器侧 —— 命令都在 `web/` 下跑：

```sh
cd web
pnpm install
pnpm run dev       # Fable watch + Vite dev server（HMR），改 .fs 约 6s 后页面更新
pnpm run build     # Fable 编译 + Vite 打包 → web/dist（可静态托管）
pnpm run verify:browser # 浏览器里那整条跑道（CI 跑的就是它）：共用一个浏览器与一台服务器，趟数它自己印，红了会告诉你单跑哪一趟
pnpm run verify    # 无头验收：浏览器内跑同种子的一局 / 一整场，与 CLI 逐项对照
pnpm run verify:home    # 无头验收：首页（`/`）就是一局回放——牌桌在动、没有配桌控件、上帝视角、时间轴拖得动
pnpm run verify:inbound # 无头验收：牌谱从外面进来的两条路——分享链接真往返（剪贴板）、导入 JSON（气泡有话）、坏输入三连
pnpm run verify:golden  # 无头验收：浏览器内跑黄金用例，与 tests/fixtures/golden/ 逐字段逐行对照
pnpm run verify:export  # 无头验收：浏览器内导出牌谱，把下下来的字节 fold 回去对照
pnpm run verify:share   # 无头验收：URL 分享的载荷往返、逐位置腐蚀、审计三样一个都不上路
pnpm run verify:invariants  # prompt 的语义不变量：本机扫一批真实对局，零网络请求
pnpm run verify:redaction   # 无头验收：会回显 key 的本机假端点跑一手，牌谱里仍然没有它
pnpm run verify:bubbles     # 无头验收：两个本机假端点跑几手——气泡里的字来自那一手的决策记录、挡不住牌、点得开、兜底那一态

除 `verify:home`、`verify:inbound`（它两个地址都开）与 `verify`（它开三个地址）之外，浏览器闸门开的都是 `?table=1`——
**首页从此自动播** Demo 回放，而要点、要读牌桌的闸门靠的是「默认暂停」那一页（票 71）。
pnpm run check     # Biome（TS/JS 的格式 + lint）
pnpm run typecheck # tsc --noEmit：只管 Agent 层与它的用例（Fable 的输出不在 include 里）
pnpm run test      # Agent 层的确定性用例（node --test，回放录制的响应，**不调真实 API**）
pnpm run format    # Biome 写回格式
pnpm run render-digest  # 改了 prompt 渲染器后重算渲染器摘要（渲染版本号的后一截）
pnpm run bench:decision # 手跑（**不在 CI 里**）：浏览器形态下建一次决策包要多久，固定种子、交错多轮
```

**`pnpm run fable` 那条命令末尾的 `--typedArrays false` 不是可有可无的**（票 84）：
默认 Fable 把 F# 的 `int[]` 编成 `Int32Array`，而 V8 上新建一个 34 长 `Int32Array`
要 612 ns，新建一个同样长度的普通数组只要 50 ns（12×）。引擎的形态判定一个决策包要新建
约 107 个这种数组，去掉它们之后建一次决策包从 1086 µs 掉到 911 µs（**1.19×**，
代价是普通数组的下标读慢一点，已经算在这个数里了）。数与量法见
`.scratch/llm-riichi-arena/run/reports/84-typed-array-cliff.md`，量具是上面那条 `bench:decision`。
改这条命令之前先跑一遍它。

### 开发向内容的开关：`?dev=1`

页面默认只摆牌桌——README 那条「单纯面向用户」的标准同样管页面本身。
**地址后面加 `?dev=1`** 才把开发向的那块挂在牌桌下面：

```sh
http://localhost:5173/?dev=1          # 曳光弹（同种子的一局 / 一整场，与 CLI 对拍的那几行数）
```

判据只有一处：`src/Janpo.Web/Main.fs` 的 `devSurfaceRequested`。加新的开发向部件就挂在它后面。
`pnpm run verify` 跑的就是带开关的地址，它另外先开一遍不带开关的地址、确认那块真的不在。

**LLM 座位**的两条手动验收要真 key，因此不进 CI：

```sh
JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-llm-seat.mjs   # 真跑一局：LLM 坐一席 + 三随机
JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-llm-seat.mjs --seats 0,1   # 两席引用同一份档案（人格各不同，票 73）
JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-export.mjs --llm --seats 0,1  # 同上，外加导出牌谱逐条核 preamble
node scripts/verify-llm-seat.mjs --bad-key                          # 断电演习：坏 key，整局照样打完
JANPO_KEY_FILE=/tmp/deepseek_key node scripts/record-agent-fixtures.mjs  # 重录 tests/fixtures/agent/
```

key 只从文件读、只注入浏览器的 localStorage，**绝不进代码、产物或提交**。

**自定义端点**（本地 Ollama / LM Studio / 自建 OpenAI 兼容网关）不要 key，但同样不进 CI：

```sh
cd web
node scripts/fake-endpoint.mjs --cors http://localhost:5173   # 最小的 OpenAI 兼容假端点（手验用，origin 填你页面开在哪）
node scripts/verify-custom-endpoint.mjs --mode allowed        # CORS 放行之后：模型座位真的答上话
node scripts/verify-custom-endpoint.mjs --mode blocked        # 不放行：页面红着说「连不上端点」
```

结论与排错表在 [`host/custom-endpoint.md`](host/custom-endpoint.md)。

README 里那两张截图由 `web/scripts/shoot-table.mjs` 重跑得出（它**不进 CI**）。
**两张图两个地址**（票 71）：牌桌那张拍的是主持人那一页（`?table=1`，带配桌面板），
首页那张拍的是 `/`（Demo 回放，自动播）：

```sh
cd web && pnpm run fable && node scripts/shoot-table.mjs          # → docs/images/table.png（?table=1）
cd web && pnpm run fable && node scripts/shoot-table.mjs --home   # → docs/images/home.png（/）
node scripts/shoot-table.mjs --scan 8 --seed 340 --turns 44   # 挑种子：看各种子在那一手的河与副露
```

首页那份 Demo 牌谱（`web/public/demo-paifu.json`，ADR-0003 的**产品资产**）由 CLI 产出，
**一条命令加一颗种子**就复现得出来，换资产就是重跑它：

```sh
dotnet run --project src/Janpo.Cli -c Release -- paifu 3 --opinionated > web/public/demo-paifu.json
```

它得过 `HomePageTests` 那四条验收（东风战、有立直有副露、以和了终、体积在预算内）。

无头脚本都需要一个 Chrome/Chromium：优先 `$JANPO_CHROME`，其次 playwright 自带的，
最后 `/usr/bin/google-chrome-stable` 一类系统路径。

黄金用例（双目标防漂移）两侧读的是同一份数据，维护在 dotnet 侧：

```sh
dotnet run --project src/Janpo.Cli -- golden check tests/fixtures/golden/dual-target.json
dotnet run --project src/Janpo.Cli -- golden write tests/fixtures/golden/dual-target.json  # 重跑并写回期望
```

怎么加一条用例见 `tests/fixtures/golden/README.md`。

CLI 目前的能力（`janpo tile / deal / kyoku / game / decide / golden / soak / shanten / yaku`）。
子命令的开关以 `janpo --help` 为准（那份帮助文本就长在 `src/Janpo.Cli/Program.fs` 里，
改实现时就在旁边）：对局长度 `--hanchan` / `--tonpuusen`、自带选手
`--uniform` / `--covering` / `--opinionated`（有主见的那个才走得到立直与供托）、`--no-akadora` 等：

```sh
$ dotnet run --project src/Janpo.Cli -- tile "1z 5sr 5s 9s 3m"
3m 5s 5sr 9s 1z
count: 5
display: 3万 5索 赤5索 9索 东
```

## 部署（GitHub Pages）

产物是纯静态的 `web/dist`，随便哪个静态托管都放得下。仓库自带一份
[`.github/workflows/pages.yml`](../.github/workflows/pages.yml)：**只在默认分支推送时跑**，
在 nix dev shell 里 `pnpm run build`（Fable + Vite），把 `web/dist` 发到 GitHub Pages。
仓库那侧要先把 Settings → Pages → Source 设成 **GitHub Actions**。

**站点挂在子路径下时要设 `base`。** vite 的 `base` 默认是 `"./"`（相对路径，因此产物放在任意
子路径下都能跑，无头脚本也按这个默认跑），部署到固定前缀时用环境变量覆盖：

```sh
JANPO_BASE=/janpo/ pnpm run build     # 产物里的资源前缀变成 /janpo/
JANPO_BASE=/janpo/ pnpm run verify    # dev/preview 读同一个变量，本地能复现 Pages 上的路径
```

`JANPO_BASE` 只在 `web/vite.config.ts` 读一次。**仓库改名（或换自定义域名，那时填 `/`）
只要改 `pages.yml` 里 `JANPO_BASE:` 那一行**；另有一处写着站点地址的是 README 末尾那个 `[play]:`
引用式链接（给人点的，不参与构建）。

无头脚本对 base 不敏感（它们从 vite 报出的 `resolvedUrls` 读真实地址，页面内的动态 `import`
也都是相对页面地址写的），因此 `JANPO_BASE=/janpo/ ./scripts/ci.sh` 同样全绿。

## 仓库结构

```
src/Janpo.Engine/        规则引擎库。**限 Fable 兼容的 F# 子集**，JSON 走 Thoth.Json.Core
src/Janpo.Cli/           无头驱动入口（dotnet only）。只做参数解析与打印，逻辑一律回引擎库
src/Janpo.Golden/        黄金用例：**两个目标共用**的那段「怎么跑一条用例」与「怎么对照」。同样限 Fable 子集
src/Janpo.Web/           浏览器宿主（Fable → JS）：Feliz + useElmish 的页面。Fable 运行时后端只能在这里
web/                     Vite 应用：index.html、一行 TS 入口、样式与无头验收脚本
web/src/agent/           **Agent 层**（TypeScript）：prompt 渲染、单轮 tool call、重试。F# 只 import 它一个函数
web/tests/               Agent 层的用例与固件（录制下来的模型响应，CI 回放它们）
docs/adr/                **为什么**：五条架构决策记录
docs/host/               **面向主持人的操作文档**（怎么配），与 docs/adr（为什么）、docs/research（实测）分开
docs/agents/             给 agent 的约定：F# 风格、issue tracker、triage 标签、领域文档
tests/Janpo.Engine.Tests/ 引擎测试：xunit 作 runner，FsCheck 属性测试为主力
tests/fixtures/golden/   黄金用例的**数据**（两侧读同一份），用法见同目录 README
tests/fixtures/paifu/    真实牌谱固件（离线对拍用），样本扩大走环境变量不改代码
scripts/ci.sh            CI 关卡，本地与 CI 同一份
scripts/ci-web.sh        JS 侧的关卡（道数与清单看脚本头部注释），被 ci.sh 调，也能单跑
scripts/ci-baseline.sh   强 AI 基线那一档的关卡（与 ci.sh 是**两种形态**不是两级严格程度，理由写在脚本头）
scripts/fsi/             `dotnet fsi` 探针：引用已编译的引擎 DLL 直调真实 API
flake.nix                dev shell（dotnet SDK + node/pnpm + uv）
.editorconfig            Fantomas 的 F# 格式规则（Web 工程另开 stroustrup，因为 Feliz 是嵌套 DSL）
web/biome.json           Biome 的 TS/JS 格式与 lint 规则
web/tsconfig.json        TS 的类型闸门（只覆盖 Agent 层，见 ADR-0005）
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
