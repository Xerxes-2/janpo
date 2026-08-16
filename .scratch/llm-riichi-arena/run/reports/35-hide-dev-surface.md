# 35 — 线上页面把开发向内容藏起来

**状态**：done　**工作区**：`janpo-ws-c`　**fixed point**：`b9227b21`（change `stzowwuy`）

一句话：曳光弹整块挪到 `?dev=1` 后面（**藏，不是删**——CI 那道浏览器内对拍闸门靠它），
牌桌的 h1 与说明从开发向措辞换成用户向措辞，实质一个字没弱。
`./scripts/ci.sh` **EXIT=0**（引擎 695 + 浏览器宿主 73 条测试；浏览器内四道全 ✓）。

改到的文件五个：`src/Janpo.Web/Main.fs`（开关）、`src/Janpo.Web/App.fs`（曳光弹页自报家门）、
`src/Janpo.Web/TablePage.fs`（h1 + 说明）、`web/scripts/verify-tracer.mjs`（带开关的地址 + 反向自证）、
`scripts/ci-web.sh`（两行注释/回显），外加 `docs/development.md` 新增一小节讲开关。

**没碰**：`README.md`、`web/index.html`（`<title>` 与 `meta description` 一字未动）、
`web/src/agent/`、`web/scripts/verify-export.mjs`、引擎、`CONTEXT.md`、`docs/adr/`、`.github/workflows/`、
`web/src/styles.css`、牌桌布局。

---

## 1. 开关

**`?dev=1`**，判据只有一处：

```fsharp
// src/Janpo.Web/Main.fs
let private devSurfaceRequested () : bool =
    window.location.search.TrimStart('?').Split('&') |> Array.contains "dev=1"

[<ReactComponent>]
let Shell () =
    Html.div [
        prop.className "shell"
        prop.children [
            TablePage.Page()
            if devSurfaceRequested () then
                Html.hr []
                App.TracerPage()
        ]
    ]
```

- 连那条 `<hr>` 一起挂在开关后面：默认视图不留一条孤零零的分隔线。
- **query 参数而不是 hash**：`base` 可配（票 33 的 `JANPO_BASE`），两种 base 下 `?dev=1` 都成立，
  而 hash 与将来可能的路由/锚点抢同一根位置。挑 query 的理由与被否决项见 DECISIONS「## 35」第 1 条。
- 加新的开发向部件时挂到 `devSurfaceRequested ()` 后面即可；页面侧认这个开关的地方只有这一处，
  脚本侧只有 `verify-tracer.mjs` 顶上的 `DEV_QUERY` 常量一处。

## 2. 我亲眼看到了什么

方式：`cd web && pnpm run fable && vite build` 出本地产物 → `vite preview` 托管 `dist/` →
playwright-core 起无头 Chrome（`/usr/bin/google-chrome-stable`，视口 1280×1000）→ 整页 PNG。
**线上地址一次都没开**（跑批禁止远端操作）。两张图存在旁边：
[`35-default-view.png`](./35-default-view.png)、[`35-dev-view.png`](./35-dev-view.png)。
拍图的脚本是一次性的，没进仓库。

### 2.1 默认视图（`/`）——图 `35-default-view.png`

整页高 **1278px**，正文 871 字。从上到下我看到的全部东西：

1. h1「janpo —— 浏览器里的 LLM 日麻竞技场」；
2. 说明一段（措辞见 §3）；
3. 一排控制：播放 / 单步 / 下一局（灰着，这一局还没打完）/ 导出牌谱 / 倍速 1×2×4×8×；
4. 一排视角：座位 0（选中）/ 1 / 2 / 3 / 上帝视角 / 危险度 / 种子 `2088` / 重开；
5. 配置面板：模型坐席（无 / 座位 0-3）、provider `deepseek`、模型 `deepseek-v4-flash`、
   API key（空）、超时 30000、思考预算、脚手架「裸奔」，下面那段 key 去向的说明；
6. 场况条：东1局 0 本场・供托 0 根・剩余摸牌 69 张・宝牌指示牌 2索；
7.「还没走一手」「四家都是随机选手」；
8. 四张座位卡：座位 0（东家，14 张亮着，含一张赤5筒）、座位 1 / 2 / 3 各 13 张**斜纹背面**。

**页面到座位 3 那张卡就结束了**——没有分隔线、没有第二个 h1、没有「曳光弹」、没有种子 1177 的
「重跑」按钮、没有 scores/juni/kyokus 那几行、没有 mjai 事件的折叠块。
逐词核过 `document.body.innerText`：「曳光弹」「Fable」「dotnet」「投影」「类型层面」「最小牌桌」
「mjai」**七个词一个都没有**。

### 2.2 带开关的视图（`/?dev=1`）——图 `35-dev-view.png`

整页高 **1888px**（多出 610px）。上半截与默认视图**逐像素同一份**（同一个种子 2088 的牌桌，
连座位 0 那手牌都一样），座位 3 的卡片下面多出一条横线，横线下面是曳光弹：

- h2 级别的标题「janpo —— 浏览器里的第一颗曳光弹（开发页）」；
- 一段说明：「开发向的自检页，地址带 `?dev=1` 才出现。同一套 F# 引擎源码，经 Fable 编成 JS
  在这里跑……」；
- 种子 `1177` + 「重跑」按钮；
- 两张卡并排：「一局（Kyoku）」`janpo kyoku 1177`，scores `30800 25000 19200 25000`、
  juni `1 2 4 3`、kyokus `1`，下面四行座位表；「一整场（东风战）」`janpo game 1177`，
  scores `29800 24000 22200 24000`、juni `1 2 4 3`、kyokus `6`，同样四行；
- 每张卡底部一个折叠块「mjai 事件 146 条（头尾各三条）」/「940 条」。

**曳光弹整块原封不动地还在**，包括 `verify-tracer.mjs` 要读的那五个 testId。

## 3. 措辞对照（保住实质、换掉行话）

| 位置 | 改前 | 改后 |
|---|---|---|
| 牌桌 h1 | `janpo —— 最小牌桌` | `janpo —— 浏览器里的 LLM 日麻竞技场` |
| 牌桌说明 | 默认四家随机选手，可以把一席交给 LLM。牌桌上的一切都是引擎局面的**投影**：坐在某个座位上看时，他家的暗牌**在类型层面**就不存在；上帝视角是**另一份独立投影**。虚线的牌是摸切。 | 默认四家随机选手；在下面挑一个座位交给模型，按「播放」看它一手一手打。他家那几行看不到牌面——**模型看到的和你一样多**，别人的暗牌**在页面拿到的数据里根本不存在**；想复盘就按一下切到上帝视角。虚线的牌是摸切。 |
| 曳光弹 h1 | `janpo —— 浏览器里的第一颗曳光弹` | `janpo —— 浏览器里的第一颗曳光弹（开发页）` |
| 曳光弹说明 | 同一套 F# 引擎源码，经 Fable 编成 JS 在这里跑。…… | **开发向的自检页，地址带 `?dev=1` 才出现。**同一套 F# 引擎源码，经 Fable 编成 JS 在这里跑；…… |

三处判断，写在这里备查：

1. **h1 与 `<title>` 现在是同一句话**。`<title>` 是票 33 定的稿，不许动；h1 抄它而不是另编一句，
   页面标题与标签页标题因此不会各说各的。要改品牌语只改 `web/index.html` 那一行再抄过来。
2. **「模型看到的和你一样多」整句抄的是 README 的图注**（票 33 的语感），刻意不改写：
   同一个卖点在 README 与页面上说法一致才立得住。README 说的是「别人的暗牌在页面拿到的数据里
   根本不存在」——这正是「在类型层面就不存在」那句行话的人话版，实质（凭什么信它不作弊）一点没弱。
3. **「上帝视角是另一份独立投影」缩成「想复盘就按一下切到上帝视角」**。「另一份独立投影」
   是给读代码的人看的实现事实（`Board.ofTable` 对上帝视角走另一条），用户只需要知道有这么个按钮。
4. **曳光弹自己的措辞没改成用户向**——它现在只有开发者看得到，本来就该说开发的话，
   只是加一句自报家门，免得谁误以为它是给访客的。
5. 页面上其余长文本（配置面板那段 key 去向、危险度那段「四条规则算出来的启发式」、
   Agent 状态行、兜底说明）**逐条看过，本来就是用户向的**，与 README 的说法一致，一个字没改。

## 4. CI 那道闸门怎么改的

`web/scripts/verify-tracer.mjs`：

1. 打开的地址从 `${pageUrl(server)}/` 改成 `${pageUrl(server)}/?dev=1`
   （常量 `DEV_QUERY`，脚本侧只此一处）。
2. **另加一段反向自证**（`checkDefaultView`）：同一个浏览器、同一台 preview，
   先开**不带开关**的 `/`，等到 `table-board` 出来（牌桌得真渲染出来，否则「什么都没有」
   也能让下面的断言全绿），然后断言：
   - `traces` / `seed-input` / `rerun` 三个 testId **一个都不在**；
   - `document.body.innerText` 里**不含**「曳光弹」/「Fable」/「dotnet」三个词。
   失败时单独一段报错「默认视图（不带 ?dev=1）里漏出了开发向内容」，与「页面报了错」
   和「双目标语义漂了」分开。

理由与票 34 那条「一道从不失败的闸门等于没有闸门」同源：只改地址的话，
**哪天有人把开关删了，闸门照样全绿，而访客又看见调试页了**。两道合起来才是一个开关：
带开关它必须在（对拍那五个 testId 读的就是它），不带开关它必须不在。

**自证这道自证真的红得起来**（实跑过，已还原）：把 `Main.fs` 的判据临时改成
`if true || devSurfaceRequested ()`，重跑 Fable + vite + `verify-tracer.mjs`：

```
默认视图（不带 ?dev=1）里漏出了开发向内容：
默认视图里还挂着 [data-testid="traces"]（1 个）
默认视图里还挂着 [data-testid="seed-input"]（1 个）
默认视图里还挂着 [data-testid="rerun"]（1 个）
默认视图的正文里出现了开发向的词「曳光弹」
默认视图的正文里出现了开发向的词「Fable」
默认视图的正文里出现了开发向的词「dotnet」
EXIT=1
```

六条全中，且对拍那五行仍然 ✓（曳光弹本身没坏，坏的只是「藏」这件事）——这正是要区分的两件事。

## 5. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套闸门 | `./scripts/ci.sh` | **EXIT=0**，fantomas / 风格闸门 / 引擎 695 / 浏览器宿主 73 / 浏览器内四道全 ✓ |
| TS/JS 格式与 lint | `cd web && pnpm run check` | 46 个文件，无 fix |
| TS 类型 | `cd web && pnpm run typecheck` | 干净 |
| dotnet 侧类型（Fable 工程） | `dotnet build src/Janpo.Web -c Release` | 0 警告 0 错误 |
| 对拍闸门单跑 | `cd web && node scripts/verify-tracer.mjs` | 五行逐项 ✓ + 「默认视图里没有曳光弹，带上 ?dev=1 它回来了 ✓」 |
| 开关坏掉时闸门会红吗 | 临时 `if true \|\| …` 后重跑 | **红，EXIT=1**（§4） |
| 默认视图长什么样 | 无头 Chrome + 整页 PNG | §2.1，亲眼看了 |
| 带开关的视图长什么样 | 同上 | §2.2，亲眼看了 |

## 6. 留给人的待审项 / 已知不完美

1. **开关是「藏」不是「关」**：`?dev=1` 只决定挂不挂那个 React 组件，曳光弹的代码仍在 bundle 里
   （它必须在——CI 闸门跑的就是打包后的产物）。这不是安全边界，只是读者面。想真删得先给
   `verify-tracer.mjs` 另找一个入口，那是另一张票。
2. **h1 与 `<title>` 是同一句话的两处副本**（`web/index.html` 与 `TablePage.fs`）。
   收成一处要么让 F# 去读 `document.title`（页面逻辑的 dotnet 用例就跑不了），
   要么把标题改成运行时注入（Pages 的静态 HTML 就没了标题）。两种都比重复更差，故留着：
   在 `TablePage.fs` 那一处写了两行注释指向 `web/index.html`（**没碰** `index.html`——
   票里写着别动，连注释也没加）。见 DECISIONS「## 35」第 3 条。
3. **`shoot-table.mjs` 不用改**：它拍的是牌桌那一块，默认地址本来就不带开关；
   曳光弹藏起来之后它拍到的东西只会更干净。没重拍 `docs/images/table.png`（票 32 的活，
   且 README 的图注仍然准确）。
4. **`docs/development.md` 加了一小节**讲 `?dev=1`（开发手册是它该待的地方）。
   README 一个字没动。
5. `.scratch/.../reports/35-*.png` 两张图进了仓库（照票 32 存 PNG 的先例）。共 313KB。

## 7. code-review — Standards 轴

fixed point `b9227b21`（change `stzowwuy`）→ `8be3a4a3`。无法派生 sub-agent，自己顺序跑的。
标准来源：`AGENTS.md`、`docs/agents/fsharp-style.md`、`docs/agents/issue-tracker.md`、
`docs/agents/triage-labels.md`、`.scratch/llm-riichi-arena/run/RUNBOOK.md`，外加 code-review skill
那份 Fowler 坏味道基线。工具强制的（Fantomas / `check-style.sh` / Biome / tsc）本次全绿，不重复列。

### Hard violation：0

- **jj-only ✓** 全程 `jj status` / `jj diff` / `jj commit`，一条 git 命令都没跑；
  无远端操作、无 `jj op restore`、没 abandon 别人的 change、没用交互式 flag。
- **禁改边界 ✓** `README.md` / `CONTEXT.md` / `docs/adr/` / `.github/workflows/` /
  `web/src/agent/` / `web/scripts/verify-export.mjs` / 引擎 / `web/index.html` / `styles.css`
  全部一字未动（`jj diff --stat` 共 11 个条目：六个代码/文档 + 票文件 + DECISIONS + 报告三件）。
- **F# 风格 ✓** 新代码只有一个函数。
  `window.location.search.TrimStart('?').Split('&') |> Array.contains "dev=1"` 从左往右读，
  **不是规则 1 禁的那种嵌套应用**（它写成 `Array.contains "dev=1" (…Split('&'))` 才是）；
  没新增 `let mutable`（规则 5 预算不动），没为了管道而管道（规则 4）。
- **注释写「为什么」✓** `Main.fs` 两段写清了「为什么是藏不是删」与「加新部件挂哪里」；
  `verify-tracer.mjs` 写清了「为什么还要开一次不带开关的地址」。
- **票文件 ✓** 验收框逐条勾上并各注一句证据，`**Status:**` 按 triage-labels 写成
  `ready-for-human`；决策追加在 `DECISIONS.md` **文件末尾**新起的「## 35」段。

### 判断题（坏味道基线，都不是硬伤）

1. **Duplicated Code / 两处真源：开关的字面量在两个语言里各写了一遍。**
   F# 侧 `Main.devSurfaceRequested` 里的 `"dev=1"`，JS 侧 `verify-tracer.mjs` 顶上的
   `DEV_QUERY = "?dev=1"`（文档里又一遍）。**未收**：ADR-0005 订的跨界方向只往「F# 调 TS」走，
   为一个字符串开一道反方向的口子不值；两处注释互相指名，且它一旦对不上，那道闸门
   （带开关读不到 testId）**当场就红**——不是一处会静默腐掉的重复。
2. **Divergent Change：`verify-tracer.mjs` 现在为两个理由被改**（双目标对拍 + 藏没藏住）。
   **未拆**：拆成两个脚本要多起一次 preview + 一次无头浏览器（CI 里十几秒），
   而「带开关它必须在 / 不带它必须不在」本就是同一件事的两面。报错分了段，不会混。
3. **Speculative Generality（轻微）：`DEV_WORDS` 是一份关键词黑名单**，会随措辞漂。
   **留着**：testId 那三条只盖得住「组件挂没挂」，盖不住「谁又往牌桌上写了一句开发向的话」
   ——而后者正是这一票的一半。三个词都只属于开发叙述，漂了的代价是一次显式的 CI 红。
4. **Primitive Obsession（已抑制）**：开关是一个裸字符串而不是一个类型。
   一个布尔开关包成 DU 是纯加法，CONTEXT.md 也没这个领域词。不改。
5. **Message Chains（看过，不算）**：`window.location.search.TrimStart('?').Split('&')`
   是对浏览器 API + 字符串的三步变换，不是在别人的对象图里导航。
6. **h1 与 `<title>` 的重复**（§6 第 2 条），已在 F# 那一头加注释指回 `index.html`。

其余基线项（Mysterious Name / Feature Envy / Data Clumps / Repeated Switches /
Shotgun Surgery / Middle Man / Refused Bequest）逐条比过，本次 diff 里一条都不沾。

### Spec 轴（顺带自查）

票的五条验收全部落地（§1 §2 §3 §4），三条边界全部遵守，没有缺失项。
**scope creep 三处，都记在 DECISIONS 第 5 条**：`verify-tracer.mjs` 的反向自证段、
`ci-web.sh` 的两行注释与一句回显、`docs/development.md` 的新小节。
三处都不在禁改清单里，且都跑过全套 CI。

**一句话总结**：Standards 0 个 hard violation（6 条判断题，最重的是开关字面量跨语言重复，
已由闸门扣住）；Spec 0 个缺失（3 处有记录的 scope creep）。没有 blocking 项要修。
