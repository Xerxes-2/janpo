# M3 排班

**里程碑**（spec）：本地真人坐席（观测投影、动作输入 UI、可见性规则）+ 强 AI 基线接入
+ 复盘对照标注 + 脚手架三档完整化。

**起草时主人裁掉的四个分叉**（详见 `DECISIONS.md` 的「## M3 起草」）：

1. **强 AI 走 WASM 进浏览器，不开后端** —— 主人拿到了 `shinkuan/Akagi`（Apache-2.0，纯 Rust 推理）
   的授权，**且允许连权重一起公开分发**。这推翻了 spec 的 Out of Scope（「Mortal 的 WASM 化明确不做」）
   与 story 37 的「后端不可用时降级」，翻案的 ADR 等票 91 查实后由调度器写。
2. **复盘对照物**：引擎自算的量（Shanten/Ukeire/Danger）打底，强 AI 可用时叠加一行。
3. **ToolSearch 做**：单步 what-if 查询工具。
4. **两笔 prompt 债都还**：记法统一走 mjai、preamble 里加战略目标。

**它是不是 Mortal 没确认过**——票 91 的头等交付物就是查清这件事（连带权重体积与署名要求）。
若不是，`CONTEXT.md` 的 `Mortal` 词条与 spec 里那一排要改成通名，**那需要另开一次术语表授权**。

## 票

| 票 | 状态 | 阻塞于 | 交付 |
|---|---|---|---|
| 87 | **已集成** `lpvxpkxn`（`cbf52bf3`） | — | 真人坐下**把一局打完**（只出牌，鸣牌自动过）；`humanSeated` 变真 → 视角锁死 + 气泡终局前藏；顺手堵 22-A |
| 88 | **派工中**（ws-a，agent `f5e5b950`，基点 `soxmwnpp`） | 87 | 响应动作齐全：吃碰杠、立直两段、荣和自摸、过——**全由引擎的合法动作集驱动** |
| 89 | 待派（**87 已集成，阻塞解除**） | 87 | 真人的信息辅助（`ScaffoldTier` 复用）+ 思考时限（座位级，默认不限时） |
| 90 | 待派（**87 已集成，阻塞解除**） | 87 | 复盘：逐手对照标注（引擎自算打底，零外部依赖） |
| 91 | **已集成** `yynxkqup`（`ac052a4e`；两轮 agent 都死在收尾，活没丢） | — | **强 AI 探路**：编成 WASM、浏览器里对固定局面出一手；查清身份/权重/许可/体积/延迟。**不接牌桌** |
| 92 | 待派 | 91 | 强 AI 坐一席：懒加载、优雅降级、许可与署名进仓库 |
| 93 | 待派 | 90, 92 | 强 AI 的对照标注进复盘（**要用该席观测投影去问，不许上帝视角**） |
| 94 | **派工中**（ws-c，agent `12f3f1ed`，基点 `soxmwnpp`） | — | ToolSearch 档：单步 what-if 工具 + 调用上限 + 延迟与账单的乘数 |
| 95 | **已集成** `oqvxvksv`（`41c9ff53`） | — | prompt 债：记法统一 mjai + 战略目标进可缓存前缀 |

## 波次

| 波 | 内容 | 地盘 |
|---|---|---|
| W1 | **三票全部集成完毕**（87 `cbf52bf3` ∥ 95 `41c9ff53` ∥ 91 `ac052a4e`） | 三条线互不相交：页面 / Rust+WASM / `web/src/agent`；地盘按下面六条裁定切死 |
| W2 | **88**（响应动作）∥ **92**（强 AI 坐席）∥ **94**（ToolSearch） | 94 与 95 都碰 agent 层，**必须错开波次** |
| W3 | **89**（辅助与时限）∥ **90**（复盘骨架） | 都碰真人那一侧，若撞车就串行 |
| W4 | **93**（强 AI 进复盘） | 收口 |

## W1 派工前的地盘裁定（调度器写，2026-08-19）

起草时说过一条判据：**并行两票只要都改同一个类型的构造形状，集成必红**（M2 三次撞车全是这个形状）。
所以 W1 的边界点名到**目录、文件与构造点**，不是「别碰对方的模块」：

| # | 裁定 | 理由 |
|---|---|---|
| S-1 | **91 不许用 `?dev=1` 那一侧**，探路件放仓库根的独立目录 `probe/akagi-wasm/`（自带 `index.html` + 自己起静态服务器） | 票面写「例如 `?dev=1`」，但 `Tracer.fs` 与 dev 面正是 **87 要堵 22-A 的地方**。两票同时改 dev 面必撞 |
| S-2 | **91 不许改 `flake.nix` / `scripts/ci.sh` / `web/**`**；宿主机已有 `cargo 1.97.1` + `rustc 1.97.1`，直接用 | 那两个是全仓公共设施（起草时点名的风险 3）。工具链要不要进 flake **只出建议**，落地是票 92 的事 |
| S-3 | **`scripts/ci.sh` 本波归 87 独占** | 只有 87 要往闸门列表里加一行（新的真人闸门）。91、95 一个字都不许动它 |
| S-4 | **95 的地盘是 `web/src/agent/**` + `web/tests/agent/**` + `verify-invariants.mjs` / `print-prompt.mjs`**；ADR-0005 已定 prompt 在 TS 侧渲染，所以它**不改** `src/Janpo.Web/Agent.fs` 的类型形状 | 87 整票都在 `src/Janpo.Web/**`。两票的唯一潜在接触点就是这个文件 |
| S-5 | **87 与 91 都不许新增对 `render_version` 值的断言** | 95 正在把它顶上去（`web/src/agent/render-version.ts`）。新断言会在集成那一刻变成假红 |
| S-6 | 87 独占 `SeatChoice` / `SeatBinding` 的构造点（`SeatingPlan.fs:52`、`Store.fs`、`TablePanel.fs` 那三处 `SeatChoice.Bot/Profile`） | 加「我自己」这一支要动 DU 的全部 match；本波无人与它共享这个类型 |

三票都往 `DECISIONS.md` 末尾追加，集成时照旧走 `resolve-append-conflicts.py`。

## W2 派工前的地盘裁定（调度器写，2026-08-19）

88 与 94 看上去一个在牌桌、一个在 agent 层，**但它俩真有一处接触点**：
`TableState.fs` 里把答案搭成 `DecisionRecord` 那一处构造（今天的 `Tools = answer.Tools`）。
M2 三次撞车全是这个形状，所以点名到行：

| # | 裁定 | 理由 |
|---|---|---|
| T-1 | **`src/Janpo.Web/Agent.fs` 与「答案 → `DecisionRecord`」那一处构造归 94 独占**；88 不许动 `Tools` 那一格 | 工具调用要往记录里写，那是 94 的可观测性本体 |
| T-2 | **`src/Janpo.Web/**` 其余全归 88**（`Table.fs` / `TableState.fs` / `HumanSeat.fs` / `HumanLine.fs` / `TableBoard.fs` / `TablePanel.fs`）；94 若发现非动不可，**停下来写进简报**，不许自己扩边界 | 88 要往 `Demand.Human` / `handed` / 合法动作集那一排里加七八种响应动作 |
| T-3 | **`scripts/ci-web.sh` / `web/scripts/verify-browser.mjs` / `web/package.json` 本波归 94 独占** | 88 只需扩已有的第十五趟 `verify-human.mjs`（已在列表里），根本不用碰闸门列表 |
| T-4 | **88 不许新增对 `render_version` 值的断言** | 94 改 prompt 就必顶它（跑 `pnpm run render-digest`） |
| T-5 | **96（引擎随机反例）暂不同波发**，等 91 回来再上 ws-d | 机器上已有 91 在跑 Rust/浏览器；四个 agent 同时跑 CI 撞过假红（判据 16） |
| T-6 | 96 若查到根因落在 `src/Janpo.Engine/Shanten.fs`，**停下来报** | `janpo-human` 工作区里主人自己有未提交的 `Shanten.fs` 改动 |

## 起草时就点名的三处撞车风险

1. **94 与 95 都碰 `web/src/agent/**`** —— 已排进不同波次。
2. **87–90 全在真人那一侧**（`TableState` / `TableBoard` / `TablePanel`）——
   87 是它们的地基，必须先落地；89 与 90 若同波，要点名到函数一级
   （M2 三次集成撞车全发生在「并行两票各自改同一个类型的构造形状」上）。
3. **91/92 可能要动 `flake.nix` 与 `ci.sh`**（Rust 工具链）——那是全仓公共设施，
   派工时要独占那两个文件。

## M2 结转的挂账

| 挂账 | 处置 |
|---|---|
| 22-A：`?dev=1` 的曳光弹在 DOM 里印四家配牌 | **票 87 顺手堵**（受害者终于出现了） |
| 采样参数（temperature 等）不做 | 照旧不做（主人两次裁决：自变量越多越难归因） |
| 多轮上下文 | 仍挂着（与可缓存前缀相冲，要单独立项） |
| 左右两家不镜像 / 移动端 / 暗色模式 / 动效 | 都没选，照「没选就是不做」办 |
| 首页资产是旧 prompt 跑的 | 票 95 之后不重跑（它是产品门面，不是基线），报告里写明 |

## 从这里接（2026-08-19 W1 派完）

**当前集成头**：`struwmpn`（`698cdf25`，= `main`，已推远端）。派工前 `./scripts/ci.sh` 全绿、
`jj log -r 'conflicts()'` 为空、三个工作区均已 `jj new struwmpn`。

**在跑**：87（ws-a）、91（ws-b）、95（ws-c）。ws-d 空着，留给临时插队。

**收到简报后的集成顺序（按撞面大小排，不按到达先后）**：
1. **91 最先合**——它只动 `probe/`，与任何人零交集，合进去不会挡后面两张 rebase
2. **再合 95**——`web/src/agent/**` 与 `render_version` 都是它的，87 被明令不得新增那类断言
3. **最后合 87**——它动 `scripts/ci.sh`（本波独占），放在最后只需解一次闸门列表的冲突

**集成时必验的两件**（不是“CI 绿就行”）：
- **87 的不泄露断言我自己要让它红一次**（判据 1；票 22 那三张截图我只读报告就放行，漏掉牌背隐形两周）
- **91 的许可结论我自己核**（对外声称与安全声称不信报告）——它直接决定票 92 开不开工，
  以及翻案的 ADR 能不能写

### 91 首轮被看门狗砍（2026-08-19 01:50）

单条 bash 跑过 45 分钟。**现场勘过，活干得很深，没丢**：

- WASM **已经编出来了**：`probe/akagi-wasm/dist/akagi_wasm_probe.wasm`（6,039,832 B）；
  cargo 缓存 `/tmp/janpo-probe-target`（863 MB）完好，增量重建便宜
- 上游克隆在 `probe/akagi-wasm/.upstream/native_bot`（权重 2.1 + 2.6 MB）与 `/tmp/akagi`
- **死因查实**：`bench.mjs` 那句等 `server.stdout` 第一次吐字的 `await` 没有超时，而端口 4191
  被上一轮残留的 `serve.mjs` 占着 ⇒ 新服务器报错退出、永远不吐 stdout ⇒ 无限等。
  残留进程已杀，4191–4193 现在都空。

**续跑单里新加的进程规矩**（往后每张带长构建的票都要带上）：单条 bash ≤ 20 分钟；
长构建一律 `nohup … &` + 轮询；每一处「等」都要有超时；起服务器前先探端口、用完必杀。

**许可这条线我已经自己核了**（不信报告；对外声称与安全声称自验）：
`native_bot/Cargo.toml` 写着 **「libriichi-free … BC-trained CNN inference (candle)」、
「derives from no copyleft source」**；`NOTICE` 里第三方只有 mahjong-helper（MIT）与 riichienv-core（Apache-2.0）。

- ⇒ **它不是 Mortal，没有 libriichi / AGPL 血缘，Apache-2.0 站得住 ⇒ 票 92 放行**
- ⇒ **术语要改名**（`Mortal` → 通名），需主人第九次授权；agent 只出影响面清单
- ⇒ **权重的来源（BC 克隆的是谁的对局）我核不了**，已列为续跑单的必查项：
  许可覆盖代码，不自动覆盖训练数据的来源，而我们要把权重一并公开分发到 Pages

### 95 与 87 集成完毕（2026-08-19）

顺序按撞面大小走的：**95 先（`41c9ff53`）→ 87 后（`cbf52bf3`）**，两趟 `ci.sh` 各自全绿，已推远端。
冲突只有 `DECISIONS.md` 一处纯追加——**六条地盘裁定成了**：两票 19 + 22 个文件，代码零重叠。

**我自己动手验的那一件**（不是读报告）：把 `TableState.viewpoint` 改回上帝视角、重编 Fable、
跑 `verify-human.mjs` ——**exit 1，30 张漏牌逐张点名**（座位 1/2/3 的手牌全露、Agent 那一行也漏）；
改回去 `59` 对 `59` 全绿。顺带看清两件：**两道锁真的独立**（上帝视角漏了牌，`?dev=1` 依然堵着，
因为 `devSurfaceAllowed` 走的是 `lockedSeat`）；22-A 的阴性对照两侧都对（真人在座 traces 0、无真人 traces 1）。
票 22 那三张截图只读报告就放行、漏掉牌背隐形两周的账，这次没再记一笔。

**集成时撞到的一个工具 bug（已修，`8bda9f4e`）**：`resolve-append-conflicts.py` 按定长前缀认
`<<<<<<< conflict`，而 jj 发现正文里有同形标记时会把分隔符**加长到 10 个**（DECISIONS.md 里拄过例子）。
它于是静静地报「解了 0 处」——**比报错更坏，差一点就把带标记的文件提进去了**。改成正则认 ≥ 7 个。

**新开票 96**（判据 17，票号由我分配）：引擎那条立直属性 `RiichiProperties.立直中的家永远听牌…`
在 95 收尾时红过一次（随后七趟全绿）。不当噪声放过：它每趟 CI 都在替所有人掷骰子，
今天放过、明天它红在别人的票上，那个 agent 要花半小时自证「不是我」——票 95 就花了。
replay 三元组已抄进票面。**不阻塞 M3，可随时插进空着的 ws-d。**

### 91 集成完毕，W1 收官（2026-08-19）

**两轮 agent 都死在收尾**（一轮看门狗、一轮服务商过载），**但实质工作全部存下来了**：
20 个验收框全勾、四节报告 + 改名影响面清单齐备。我只补了三件：写 commit message、
解 `DECISIONS.md` 那一处纯追加冲突、跑 CI（全绿）。

**结论三句话**：
1. **它不是 Mortal**。`native_bot` 自己写着 libriichi-free、`derives from no copyleft source`，
   上游 `NOTICE` 里只有 mahjong-helper（MIT）与 riichienv-core（Apache-2.0），且那两条在 Akagi 本体里、
   `native_bot/` 一行未引。**Apache-2.0 站得住，票 92 放行。**
2. **浏览器里跑得动且快到不像话**：6.0 MB 产物（4.8 MB 是内嵌权重），
   单手中位 **0.37 ms**（n = 17,260，111 场真实天凤牌谱全场重放）、p95 0.64 ms、常驻 11 MiB 不漏；
   一整场半庄 181 个决策点跑完 89 ms——**比一次 LLM 调用快两个数量级**。
   且它把「立直手要跑第二次前向」这件查出来了（mjai 的 `reach` 必须同时说出宣言牌）：
   **票 92 排预算要按「立直手 0.7 ms」而不是平均值**。原生对拍 0.281 ms，wasm 税 2.4×。
3. **权重是天凤牌谱行为克隆来的，具体语料查不到**（三处出处互证「imitates human play from Tenhou logs」，
   但仓库里无数据集名/URL/model card）。**Apache-2.0 授权的是上游对代码与权重文件本身的权利，
   不代表牌谱权利人授权过什么**——这一条要主人抬手，见下面。

**又一个静默丢文件的工具 bug（已修，`367bec29`）**：根 `.gitignore` 的 `bin/`（本意是 dotnet 的 obj/bin）
**吞掉了 `probe/akagi-wasm/crate/src/bin/parity.rs`**——Rust 的 bin 目标就住在 `src/bin/`，
而那个文件是本票最硬的一条证据（原生对拍）。agent 自己发现并用反否定捞了回来，
我把根上那条改成 `src/*/bin/` `tests/*/bin/`。**这类 bug 的可怕处在于没有人报错。**

**W2 开工前要先处理的**：
- 上面那条已落实：那个网络**不是 Mortal** ⇒ 术语表改名要**另开一次授权**（第九次），且翻案 ADR 由我写，
  **两件都得在派 92 之前办完**（否则 92 会拿一个就要被改掉的名字到处写）
- ~~94 必须等 95~~ **已解除**。派 94 时照抄这三条（来自 95 的简报，不要让它自己去扪）：
  ① **工具定义加在 preamble 第③段之后、第④段之前**（前缀四段，顺序即字节序；别加进尾部、别加进人格）
  ② **what_if 结果写进 prompt 时牌必须是 mjai**，工具名/参数名不得落进正文——否则第十二条
  不变量（`RULES.mjaiOnly`）当场红；写动作一律走新出口 `action-label.ts` 的 `labelOf`，别读 `option.label`
  ③ 改了 `web/src/agent/{prompt,history,template,wording,action-label}.ts` 就**必须跑 `pnpm run render-digest`**，
  否则 `render-version.test.ts` 当场红；要钉 `render_version` 只能钉形状正则，不钉值
- ~~88 阻塞于 87~~ **已解除**。派 88 时照抄：接缝就一处——`TableState.handed` 里
  `passAction` 是 `Some` 就替他过、否则原样返回（改成真按钮 = 改掉 `Some` 那一支，
  它要的两样已在手上：那份包 + `HumanSeat.unspoken`）；真人席是 `SeatChoice.Human` / wire `human` /
  `SeatPlayer.Human` / `Demand.Human of package`；动作输入走 `TableMsg.HumanPlayed of id: int`；
  「轮到他了吗」不存状态，现问 `TablePage.humanTurn`
- **89 / 90 开工前照抄**：可见性只有一条判据 `TableState.lockedSeat`（四个消费点：气泡 /
  牌桌投影 / 视角那一排 / 曳光弹）——**不许重造第二条**

**挂着的两件历史遗留物（不阻塞 W1，但交接前要收）**：
- 游离 change `nuzxrvmxxwvu`（引擎性能第二轮研究）——内容已经由 `srypxosxpuys` 进了主线
  并被 `unzrpzvzlsmn` 就地更正过，这一份是**过时的重份**。待主人确认后 abandon。
- `janpo-human` 工作区停在 M2 中段的 `vmllwlrx`（Shanten int 哨兵，**还有未提交的
  `src/Janpo.Engine/Shanten.fs` 改动**）。那是主人自己的地盘，我不碰；三个在跑的 agent 也已被明令不得碰。
  它不与 W1 任何一票重叠（W1 三票一行引擎代码都不改）。
