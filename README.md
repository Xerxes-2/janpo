# janpo

**在浏览器里跑的 LLM 日本麻将（立直麻将）竞技场。** F# 写的规则引擎经 Fable 编成 JS，
连同 Feliz 页面一起在浏览器里运行；主持人自带 API key，把一个座位交给模型、拨一档信息脚手架，
看它一手一手打，随时导出牌谱。**没有后端**：产物是一份可静态托管的前端，key 只落在这台浏览器的
localStorage 里，请求由浏览器直发 provider。

> **WIP。** M0（无头引擎）已完成，M1（浏览器 + LLM 座位 + 兜底闭环 + 牌谱导出）正在收尾，
> 还差三张票（29b、31、27，见[路线](#路线)）：prompt 的形态还要翻一次，「LLM 坐一席打完
> **一整场**」也还没验收（四家随机选手打完整场东风战是跑得通的）。接口与页面都会变。
> 现在就发，是因为**过程本身**（`.scratch/` 里的票、验收报告与决策记录）值得看。

**In English.** janpo is a browser-only arena where LLMs play Japanese mahjong (riichi).
One unchanged F# rules engine is compiled twice — to .NET for property tests and differential
testing against real paifu (game logs), and to JavaScript via Fable to run inside the page.
You bring your own API key (it stays in `localStorage`; requests go straight from your browser to
the provider), seat a model with a chosen information-scaffold tier, watch it play, and export the
mjai-style event log. There is no server: the build output is a static bundle, and nothing about a
game leaves your browser except the provider calls you pay for. Docs, comments and prompts are
Chinese-first; identifiers use romaji mahjong terms. Work in progress — milestone M1 is not done.

![牌桌截图：围观视角坐在座位 0，种子 1177 走了 52 手](docs/images/table.png)

围观视角（座位 0）下的牌桌：场况、四家的河（虚线是摸切）与副露、自己的手牌。
**他家那几行给不出牌面**——他们的暗牌在这个投影的类型里根本没有字段（见下）。
上帝视角是另一份独立投影，按钮拨得到。

---

## 凭什么有意思

这个项目最有说服力的东西是**约束**，不是形容词。下面每条都指得到出处。

### 一份 F# 源码，两个目标，逐字段钉住

`src/Janpo.Engine/` 的 10,983 行 F# **一处 `#if FABLE_COMPILER` 都没有**，同时被 dotnet
与 Fable 编译：前者跑 xunit + FsCheck 与真实牌谱对拍，后者变成页面里那套判定。
`scripts/ci.sh` 有一道硬闸门守着它——引擎工程只准引 `FSharp.Core` / `Fable.Core` /
`Thoth.Json.Core` 三个包。

两侧不许漂移，由黄金用例钉住：`tests/fixtures/golden/dual-target.json` 是**用例本身**
（输入与期望都在数据里），dotnet 侧与浏览器侧读同一份、跑同一段 F#（`src/Janpo.Golden/`）。
最近一次 CI 两侧的输出逐字相同：

```
40 条用例、1947 个字段、3210 行：全部一致 ✓
```

同一种子的一局与一整场还要在浏览器里跑一遍，与 CLI 的点数、顺位与局数逐项对照
（`web/scripts/verify-tracer.mjs`，CI 的一道关卡）。

### 信息隐藏是结构性的，不靠纪律

- 他家的暗牌在观测类型 `MaskedSeat` 里**没有字段**（`src/Janpo.Engine/Observation.fs`）：
  投影函数想漏也没地方放。
- 掩蔽**只定义在事件上**，全项目一条法则：`MaskedEvent.forSeat`
  （`src/Janpo.Engine/MaskedEvent.fs`）。观测是这条掩蔽事件流 fold 出来的结果——
  `Observation.ofState` 内部就是 mask + fold，没有第二处判断「某座位能看见什么」。
- `forSeat` 的 match **穷举** `Event` 的每一个 case，不写 catch-all：新增一条 mjai 事件时，
  编译器逼着加的人回答「这条有没有看不见的部分」。
- 属性测试拿 fold 出来的观测与引擎权威状态**逐字段**对，报错点得出是哪个字段
  （`ObservationProperties`）。
- 上帝视角是另一份独立投影，不在掩蔽法则的定义域里（它没有观测者）。

### 脚手架分档的判据是「感知 vs 计算」

`CONTEXT.md` 的 `ScaffoldTier` 词条把那条线写成**判据**而不是内容清单：

> **Bare** 给的是一个坐在牌桌前的人**免费得到**的一切（他亲眼见过的事件序列、他一眼看得见的场况）；
> **Assisted** 给的是**要算才有**的量：Shanten、Ukeire、ShantenDelta、Danger。
> 判据不是「严格不可推导」而是「真人不用动脑子就拿得到」——巡目与牌山剩余都推得出来，
> 可没有牌手在数它们，所以照给。**可见牌按牌种归并的统计属于 Assisted**——那是数出来的，不是看见的。

判据是判据的好处是它能裁决以后冒出来的新项。**但别把它当成结构性隔离**：决策包恒带这些数，
档位决定的只是 prompt 渲不渲染它（`web/src/agent/prompt.ts` 那一个函数）。
结构性成立的是上面那条——他家的暗牌。

### 兜底闭环：模型怎么坏，对局都打得完

四类失败在 `web/src/agent/loop.ts` 的 `judge` 里合流：**超时**（abort 是值，不是异常）、
**provider 报错**、**输出格式跑偏**（没调工具 / `action_id` 不是整数）、
**给的 id 不在这一手的合法动作集里**。之后走同一条路——带着原因重问，重试上限 2 次
（`Agent.retryLimit`，因此每手最多问 3 次），仍交不出来就由引擎的 `Fallback.action` 代打：

| 档位 | 代打哪一手 |
|---|---|
| Bare | 摸切 → 「过」→ 合法动作集第一条（三级都取自同一个合法动作集，非空） |
| Assisted | 先取**进退向为 0**（不退向听）的试打，再在其中按危险度名次挑最安全的 |

代打**不静默**：牌桌上那一手写着兜底原因，Agent 状态线红着数「这一桌已兜底 N 手」。
断电演习（发布前重跑的实测）：故意配一把坏 key，

```
一局打完，用时 18.4 s ／ 兜底代打：20 手 ／ provider 请求：60 次，其中 4xx/5xx 60 次
```

一局照样打完，20 手每手问满 3 次全部 401。

### 牌谱是唯一可分享物

[ADR-0002](docs/adr/0002-paifu-is-the-only-shareable-artifact.md)：一场对局对外只有一种序列化形式
——Paifu（mjai 风格事件流 + 可选的决策记录）。**不存在局面快照格式**，回放不是另一套代码路径，
就是对事件前缀做 fold。

- 引擎侧：`dotnet run --project src/Janpo.Cli -- soak 1 200` 连跑 200 场（1038 局、86,324 手），
  逐手验不变量——牌数守恒、点数与供托之和恒定、合法动作集非空或局已终、**回放确定性**——
  9.6 秒跑完，「问题：无」。另外单跑过整场重放：200 场的事件流**逐条相同，差异 0**。
- 浏览器侧是 CI 的一道闸门（`web/scripts/verify-export.mjs`）：点一下真下载一个文件，
  把**下下来的那份字节**喂回浏览器里的引擎 fold，事件流逐条相同、点数与牌桌上显示的一致；
  闸门顺带检查牌谱里不含 API key。

决策记录是审计数据：那一手的 prompt 与工具定义、模型的原始输出与 thinking、延迟、重试次数。

### 危险度是规则化启发式，不是概率模型

`src/Janpo.Engine/Danger.fs` 按**现物 / 筋 / 壁 / 宝牌周边**四条规则给档位与并列名次，
威胁只认**立直或有副露**的家——一家都没有时整份危险度是空的，prompt 与牌桌都不出现这一节。
片筋不算筋（4-6 要两侧都现物）。它是**分析附件，不参与规则判定**，风格闸门里有一条专门守着
「规则判定的用例里不许出现 Danger」。**这不是 AI 评价系统，也不是统计模型**。

### 规则集是引擎的一等输入

[ADR-0004](docs/adr/0004-ruleset-as-first-class-input.md)：引擎里不得出现散落的规则字面量
（`4` / `136` / `1000`），一律从 `Ruleset` 读；预设**按现实平台命名**（天凤 / 雀魂），
默认值对齐天凤鳳凰卓（双响成立、无切上满贯）——这两个默认值是拿 60 局真牌谱实测定的，
不是凭直觉写的。符与点数的长尾由真实牌谱对拍守着：CI 里离线固件 18 局 / 177 kyoku 零差异；
把样本扩到 200 局 / 2110 kyoku 时，对拍抓出过两个真 bug（其中一个是明杠的新宝牌翻早了）。

---

## 跑起来

```sh
nix develop                  # dev shell：dotnet 10 / node / pnpm / uv，版本由 flake.lock 钉住
cd web && pnpm install
pnpm run dev                 # Fable watch + Vite dev server → http://localhost:5173/
```

页面上：

1. **模型坐席**选一个座位（M1 只支持一席，其余三家是随机选手）；
2. 填 provider（`deepseek` / `anthropic` / `openai` / `google` / `openrouter` / `xai` / `groq` /
   `mistral`，或**自定义 OpenAI 兼容端点**）、模型名（自由文本框）与 API key，按需要拨思考预算与超时；
3. **脚手架**拨「裸奔」或「信息辅助」（工具搜索是 M3，灰着）；
4. 按「播放」或「单步」，随时「导出牌谱」——不必等终局。

key 只写进这台浏览器的 localStorage，请求由浏览器直发 provider；本平台没有后端接得到它。
订阅制的 OAuth 登录在浏览器里用不了，只能填 API key。

想接本地模型（Ollama / LM Studio / llama.cpp / vLLM / 自建网关）看
[`docs/host/custom-endpoint.md`](docs/host/custom-endpoint.md)——baseUrl 怎么填、CORS 怎么放行、
https 页面调 http 端点拦不拦，结论全是实测的。

要静态托管就 `cd web && pnpm run build`，产物在 `web/dist`。

---

## 不是什么

- **不是天凤 / 雀魂的替代品**：没有账号、没有匹配、没有天梯，也不做联机对战（spec 的 Out of Scope）。
- **没有实时观战**（[ADR-0003](docs/adr/0003-no-live-spectating-host-drives-the-game.md)）：
  key 在主持人本地、请求由他的浏览器直发，因此**他的浏览器就是唯一能让对局前进的地方**。
  访客打开链接只可能看到牌谱回放——而 URL 分享与首页 Demo Paifu 自动播放属于 M2，现在还没有。
- **没有服务端**，因此也不存牌谱、不存 key。导出的 JSON 归你自己。
- **危险度不是统计模型**（见上）。它给的是规则化的安全度排序，别当成雀力评价。
- **本期不做三麻、古役与地方规则**。`Ruleset` 的形状已能表达三麻，但只跑四麻。
- **没有演出动画**，也不为移动端适配（不刻意破坏，但不是验收项）。
- **Mortal 基线接入是 M3 的事**，且是可选依赖，不做单点。

---

## 这个仓库是怎么造出来的

M0 与 M1 的实现由 **subagent 在各自的 jj workspace 里逐票完成**：一个调度器 agent 拆票、派活、
集成、守 CI 闸门，人只做裁决。硬约束写在
[`RUNBOOK.md`](.scratch/llm-riichi-arena/run/RUNBOOK.md)：只用 jj、不许问人、
**不许为了变绿而破坏测试**（做不下去就 park）、外部工具走 `uv run` 不进运行时依赖。

全过程都在 [`.scratch/llm-riichi-arena/`](.scratch/llm-riichi-arena/) 里，一起公开：

| 在哪 | 是什么 |
|---|---|
| `spec.md` | 第一版规格：用户故事、实现决策、里程碑切分、Out of Scope |
| `issues/` | 32 张票（M0 的 01–17、M1 的 18–31），每张写清「做什么、阻塞于谁、验收清单」 |
| `run/reports/` | 29 份验收报告：做了什么、关键取舍、实测数字、留给人的待审项 |
| `run/SCHEDULE.md`、`run/M1-SCHEDULE.md` | 排班表：波次、并行理由、每票的状态与集成记录 |
| `run/DECISIONS.md` | 2000 多行决策记录：每票的自主决策、**被否决的选项**与理由，含**六次人的裁决** |

一票一 commit，加上集成与裁决落盘的 chore，至今近百个 commit。三个具体的例子——

**一、「一个投影里出现第二个只能从历史算的字段，就是形态错了。」**
20 号票把座席观测做成了**快照**，实现者精确地做了票面要求的东西。症状是补丁：28 号票先要给投影补
`PendingKan`，又要补立直宣言牌与巡目。第二次补「发生过的事」时才停下来问——快照根本表达不了
**什么没发生**（见逃し：一张牌过了一圈没人要，是极强的读牌信息）。裁决把快照降为派生：
掩蔽只定义在事件上，观测是 fold 出来的结果。落地（票 29a）之后 `Observation` 类型一个字段没改，
牌桌、脚手架与危险度三张票**一行没动**，黄金用例一字未改。
这条判据当场又抓到一个孪生兄弟：牌谱每手存 prompt 全文，而 prompt 是事件流的派生物——
那件事被拆成票 31，还没做。

**二、两票各自绿，合流即红。** 21（黄金用例）与 22（最小牌桌）并行：22 给观测加了他家手牌张数
`tehai_count`，而 21 把决策包 JSON **按整行**钉住，合流后两条用例红。处置是照 21 号票定的流程
`golden write` 誊写、逐行核对「唯一的变化就是那个字段」；这件事顺带证明了整行钉的代价，
后来落成票 28 的「决策包改成逐字段钉」。

**三、红要先怀疑基础设施。** 30 号票集成时 CI 假红一次：两个工作区同时跑 CI，无头验收脚本
写死了端口撞在一起，报错长得像用例挂了。（本 README 的截图脚本因此另占一个端口。）

---

## 路线

- **M0 ✅ 无头引擎**：四随机 bot 打完东风战、点数正确、属性测试与真实牌谱对拍。
- **M1 🚧 浏览器**：Fable 工具链、最小牌桌与播放控制、LLM 坐一席（Bare / Assisted 两档）、
  兜底闭环、危险度、牌谱导出、自定义端点都已落地；还差三张票——
  - **29b**：prompt 翻转成「固定 preamble + append-only 的观测历史 + 尾部现况」，吃 provider
    的前缀缓存，并把命中率变成可观测指标；
  - **31**：prompt 从代码降为**数据**（模板 / 槽位 / system / 座位级人格），牌谱只存 prompt 尾部；
  - **27**：M1 验收——LLM 坐一席打完**一整场**东风战，导出的牌谱再 fold 回来逐项对照。
- **M2**：四 LLM 同桌、思考气泡、回放导入与 URL 分享、首页 Demo Paifu 自动播放。
- **M3**：本地真人坐席（观测投影、动作输入 UI、可见性规则）、Mortal 容器接入、复盘对照标注、
  脚手架三档完整化。

---

## 开发

术语以 [`CONTEXT.md`](CONTEXT.md) 为唯一权威（罗马字日麻术语），牌记法以
[ADR-0001](docs/adr/0001-mjai-notation-and-romaji-identifiers.md) 为准（mjai `1m-9m` / `1p-9p` /
`1s-9s` / `1z-7z`，红宝牌 `5mr` / `5pr` / `5sr`）。写 F# 前读
[`docs/agents/fsharp-style.md`](docs/agents/fsharp-style.md)。

### 开发环境

工具链由 nix flake 钉住（dotnet SDK、node/pnpm 与 uv），CI 与本地用同一个 shell：

```sh
nix develop            # 进 dev shell：dotnet、node、pnpm、uv
dotnet tool restore    # 装 Fantomas 与 Fable（dotnet local tool，版本在 .config/dotnet-tools.json）
```

宿主机上若已有匹配 `global.json` 的 dotnet SDK（10.0.1xx 及以上特性带），不进 dev shell 也能跑；
但**版本以 flake 为准**，CI 只认 dev shell 里的那一套。

### 常用命令

```sh
./scripts/ci.sh                       # CI 的全部关卡：dotnet 侧 + JS 侧，一条命令两侧全绿
./scripts/ci-web.sh                   # 只跑 JS 侧：Biome + tsc + Agent 层用例 + Fable + Vite + 浏览器内三道
dotnet build janpo.slnx               # 构建五个工程
dotnet test janpo.slnx                # 跑测试（xunit + FsCheck；当前 763 条）
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
pnpm run verify:export  # 无头验收：浏览器内导出牌谱，把下下来的字节 fold 回去对照
pnpm run check     # Biome（TS/JS 的格式 + lint）
pnpm run typecheck # tsc --noEmit：只管 Agent 层与它的用例（Fable 的输出不在 include 里）
pnpm run test      # Agent 层的确定性用例（node --test，回放录制的响应，**不调真实 API**）
pnpm run format    # Biome 写回格式
```

**LLM 座位**（票 23）的两条手动验收要真 key，因此不进 CI：

```sh
JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-llm-seat.mjs   # 真跑一局：LLM 坐一席 + 三随机
node scripts/verify-llm-seat.mjs --bad-key                          # 断电演习：坏 key，整局照样打完
JANPO_KEY_FILE=/tmp/deepseek_key node scripts/record-agent-fixtures.mjs  # 重录 tests/fixtures/agent/
```

key 只从文件读、只注入浏览器的 localStorage，**绝不进代码、产物或提交**。

**自定义端点**（票 30：本地 Ollama / LM Studio / 自建 OpenAI 兼容网关）不要 key，但同样不进 CI：

```sh
cd web
node scripts/fake-endpoint.mjs --cors http://localhost:5173   # 最小的 OpenAI 兼容假端点（手验用，origin 填你页面开在哪）
node scripts/verify-custom-endpoint.mjs --mode allowed        # CORS 放行之后：模型座位真的答上话
node scripts/verify-custom-endpoint.mjs --mode blocked        # 不放行：页面红着说「连不上端点」
```

README 里那张牌桌截图由 `web/scripts/shoot-table.mjs` 重跑得出（它**不进 CI**，端口另占 4190）：

```sh
cd web && pnpm run fable && node scripts/shoot-table.mjs   # → docs/images/table.png
node scripts/shoot-table.mjs --scan 8 --seed 340 --turns 44   # 挑种子：看各种子在那一手的河与副露
```

无头脚本都需要一个 Chrome/Chromium：优先 `$JANPO_CHROME`，其次 playwright 自带的，
最后 `/usr/bin/google-chrome-stable` 一类系统路径。

黄金用例（双目标防漂移）两侧读的是同一份数据，维护在 dotnet 侧：

```sh
dotnet run --project src/Janpo.Cli -- golden check tests/fixtures/golden/dual-target.json
dotnet run --project src/Janpo.Cli -- golden write tests/fixtures/golden/dual-target.json  # 重跑并写回期望
```

怎么加一条用例见 `tests/fixtures/golden/README.md`。

CLI 目前的能力（`janpo tile / deal / kyoku / game / decide / golden / soak / shanten / yaku`）：

```sh
$ dotnet run --project src/Janpo.Cli -- tile "1z 5sr 5s 9s 3m"
3m 5s 5sr 9s 1z
count: 5
display: 3万 5索 赤5索 9索 东
```

### 仓库结构

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
scripts/ci-web.sh        JS 侧的那八道，被 ci.sh 调，也能单跑
scripts/fsi/             `dotnet fsi` 探针：引用已编译的引擎 DLL 直调真实 API
flake.nix                dev shell（dotnet SDK + node/pnpm + uv）
.editorconfig            Fantomas 的 F# 格式规则（Web 工程另开 stroustrup，因为 Feliz 是嵌套 DSL）
web/biome.json           Biome 的 TS/JS 格式与 lint 规则
web/tsconfig.json        TS 的类型闸门（只覆盖 Agent 层，见 ADR-0005）
Directory.Packages.props 所有 NuGet 版本集中管理
```

引擎工程的依赖有白名单，由 `scripts/ci.sh` 强制：只允许 `FSharp.Core`、`Fable.Core`、
`Thoth.Json.Core`。要加包先确认 Fable 能编译它，再改名单并留下决策记录。

### 加新模块时的约定

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

---

## 许可证

[MIT](LICENSE) © 2026 Xerxes-2
