# 37 — 站点要能链回 GitHub 仓库

**状态**：done　**工作区**：`janpo-ws-c`　**fixed point**：`eac6f5a9`（change `rroturqn`）

一句话：默认视图最底下加了**一行页脚**——「源码、现在做到哪一步、以及页面里提到的那几份文档，
都在 [GitHub 上的 Xerxes-2/janpo]。按 [MIT 许可]放出。」两条外链的地址在页面侧**只写一处**
（`src/Janpo.Web/Footer.fs`），许可那条由仓库地址派生。`./scripts/ci.sh` **EXIT=0**
（引擎 700 + 浏览器宿主 75 条测试；浏览器内四道全 ✓）。

改到的文件五个（外加两张图）：新增 `src/Janpo.Web/Footer.fs`、`Janpo.Web.fsproj` 加一行编译项、
`src/Janpo.Web/Main.fs`（外壳挂上页脚）、`web/src/styles.css`（新增 `.site-footer` 两块）、
`web/scripts/verify-tracer.mjs`（默认视图那一道多守一条）、`scripts/ci-web.sh`（两处注释/回显）。

**没碰**：`README.md`、`web/index.html`、`web/src/agent/`、引擎、`CONTEXT.md`、`docs/adr/`、
`.github/workflows/`、牌桌布局、`styles.css` 里既有的任何一条规则（只在文件里**追加**了两块）。
**没引任何第三方脚本**：`index.html` 依旧零 CDN 零 analytics（票 34 核过的那条事实没被动过）。

---

## 1. 链接放在哪、长什么样

**位置**：页面最底下的 `<footer class="site-footer">`，挂在**页面外壳**里（`Main.Shell`），
排在牌桌之后、且**在 `?dev=1` 开关之外**：

```fsharp
// src/Janpo.Web/Main.fs
prop.children [
    TablePage.Page()
    if devSurfaceRequested () then
        Html.hr []
        App.TracerPage()
    Footer.Bar()          // ← 开关之外：访客看得到的才算数
]
```

**措辞**（一行，`0.8rem`、`opacity: .7`）：

> 源码、现在做到哪一步、以及页面里提到的那几份文档，都在 **GitHub 上的 Xerxes-2/janpo**。
> 按 **MIT 许可**放出。

三处判断：

1. **说的是访客关心的三件事**——能读到源码、项目做到哪一步、按什么许可放出。
   「现在做到哪一步」对应 README「现在能玩到什么，还差什么」那一节（这一票要解决的正是
   「访客找不到源码、许可与项目状态」）。**不出现 Fable / dotnet / Vite / 曳光弹**，
   语感接着票 35 改过的牌桌说明与票 33 的 README 走。
2. **顺带把页面里那些 repo 相对路径接上了**。配置面板里那段本地端点说明写着
   「配法见 `docs/host/custom-endpoint.md`」（票 30/33 的现有做法：**在正文里点名一份仓库里的文档**，
   不是 `<a>`）。站点在此之前没有任何一条路通向仓库，那行路径对访客等于死链；
   页脚这句「以及页面里提到的那几份文档，都在 …」把它们一次性接上了，**没有改那段说明一个字**。
3. **两条链接都 `target="_blank"`**（配 `rel="noopener noreferrer"`）。理由不是习惯：
   这个平台没有后端也不存档，正在看的那一局只活在当前页面的内存里（README「没有实时观战」那节），
   在原地跳走等于把人打了一半的牌局扔掉。

## 2. 地址的唯一真源

`src/Janpo.Web/Footer.fs` 顶上两行，页面侧再无第二处：

```fsharp
let private repoUrl = "https://github.com/Xerxes-2/janpo"
let private licenseUrl = $"{repoUrl}/blob/HEAD/LICENSE"
```

- **许可那条由仓库地址派生**，不另写一份地址；仓库改名只改一行。
  这是 README 那头的镜像做法（站点地址只写在 README 末尾那条 `[play]` 定义里，旁边一行注释说着同一件事）。
- **`blob/HEAD/` 而不是 `blob/main/`**：HEAD 由 GitHub 解析成当前默认分支，
  默认分支哪天改名（main/master/trunk）这条链接也不会烂。仓库里没有远端可查，不猜分支名最省事。
- **CI 那道闸门里不复述地址**（见 §4）：它只断言「页脚里有一条 `https://` 外链」，
  否则地址就又变成两处。

## 3. 我亲眼看到了什么

方式与票 35 同一套：`cd web && pnpm run fable && vite build` 出本地产物 → `vite preview` 托管 `dist/`
→ playwright-core 起无头 Chrome（`/usr/bin/google-chrome-stable`）→ 整页 PNG。
**线上地址一次都没开**（跑批禁止远端操作）。拍图脚本是一次性的，跑完删了，没进仓库。

### 3.1 默认视图（`/`，视口 1280×1000）——图 [`37-default-view.png`](./37-default-view.png)

整页高 **1417px**（票 35 记的 1278px + 139px，多出来的就是页脚那一块：
`margin-top 2rem` + 细线 + `padding-top .75rem` + 一行字 32px）。
从上到下与票 35 §2.1 逐项对得上，**没有任何东西被挤动**：h1 →说明 → 一排控制（播放/单步/下一局灰着/
导出牌谱/倍速）→ 一排视角（座位 0 选中 … 上帝视角/危险度/种子 2088/重开）→ 配置面板 →
场况条（东1局 0 本场・供托 0 根・剩余摸牌 69 张・宝牌指示牌 2索）→「还没走一手」「四家都是随机选手」→
四张座位卡（座位 0 亮着 14 张含赤5筒，1/2/3 各 13 张斜纹背面）。

**页脚在座位 3 那张卡下面**（元素测得 `top = 1353px`、高 32px）：先是一条与卡片同色的细横线
（`border-top: 1px solid var(--line)`，与牌桌卡片的边框同一个变量），空 12px，然后是那一行小字。
放大看过 [`37-footer-crop.png`](./37-footer-crop.png)：字比正文小一号、淡了一档，
两条链接**带下划线但不是蓝色**（`color: inherit`），左边缘与上面所有卡片的左边缘对齐，
右边一大片空白——**一眼找得到，但视线扫牌桌时不会被它拽走**。座位 3 的卡片与页脚之间隔着 32px，
没有任何重叠或挤压。

从 DOM 里读回的两条链接（脚本打印，与图上文字一致）：

```
GitHub 上的 Xerxes-2/janpo -> https://github.com/Xerxes-2/janpo            [_blank]
MIT 许可                    -> https://github.com/Xerxes-2/janpo/blob/HEAD/LICENSE [_blank]
```

### 3.2 另外两张看过但没进仓库的图

- **`?dev=1`（视口 1280×1000）**：整页高 2027px（票 35 记的 1888 + 同样的 139）。
  曳光弹整块原样在牌桌下面，**页脚排在它之后**（`top = 1963px`）——开关开着时页脚也在，
  它本来就不受开关管。
- **窄视口 820×1000**：整页高 1538px，页脚**仍是一行**（没折行、没横向溢出）。
  再窄会自然折成两行，页脚是纯文本流，不参与牌桌那套网格。

## 4. 顺带加的一道闸门（scope creep，已记 DECISIONS）

`web/scripts/verify-tracer.mjs` 里票 35 那段 `checkDefaultView` 已经在默认视图上跑一遍了，
**同一次打开顺带断言了反面的两条**：

```js
const footerLinks = await page.getByTestId("site-footer").locator('a[href^="https://"]').count();
if (footerLinks === 0) missing.push("默认视图的页脚里没有一条指回仓库的外链（票 37）");
if (!text.includes("MIT")) missing.push("默认视图的正文里没提许可（MIT）（票 37）");
```

理由与票 34/35 同源：**这条链接被谁顺手删掉、或被挪到 `?dev=1` 后面，页面看上去完全照常，没人会发现**。
报错分成两个数组（`leaks` =「多了不该给访客看的」，`missing` =「少了该给访客的」），
输出成两段独立的报错，不与票 35 那三条混在一起。零额外开销：同一台 preview、同一次 `goto`。

**自证这道自证真的红得起来**（实跑过，已还原）：把 `Footer.Bar()` 临时挪进 `if devSurfaceRequested ()`，
重跑 Fable + vite + `verify-tracer.mjs`：

```
默认视图里少了该给访客的东西：
默认视图的页脚里没有一条指回仓库的外链（票 37）
默认视图的正文里没提许可（MIT）（票 37）
EXIT=1
```

两条全中，而对拍那五行仍然 ✓、票 35 那三条也仍然 ✓——**红的只是「访客看不看得到」这一件事**。

## 5. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套闸门 | `./scripts/ci.sh` | **EXIT=0**（fantomas / 风格闸门 / 引擎 700 / 浏览器宿主 75 / 浏览器内四道全 ✓）|
| 测试条数 | `dotnet test janpo.slnx -c Release --no-build` | 引擎 700 通过、浏览器宿主 75 通过、失败 0 |
| TS/JS 格式与 lint | `cd web && pnpm run check` | 48 个文件，无 fix |
| TS 类型 | `cd web && pnpm run typecheck` | 干净 |
| dotnet 侧类型（Fable 工程）| `dotnet build src/Janpo.Web -c Release` | 0 警告 0 错误 |
| 对拍闸门单跑 | `cd web && node scripts/verify-tracer.mjs` | 五行 ✓ +「默认视图里没有曳光弹…✓」+「页脚里有回仓库的外链与许可（MIT）✓」|
| 页脚没了闸门会红吗 | 临时把页脚挪进 `?dev=1` 后重跑 | **红，EXIT=1**（§4）|
| 默认视图长什么样 | 无头 Chrome + 整页 PNG | §3.1，亲眼看了 |
| `?dev=1` 与窄视口 | 同上 | §3.2，亲眼看了 |

## 6. 留给人的待审项 / 已知不完美

1. **措辞里「现在做到哪一步」指的是 README 的「现在能玩到什么，还差什么」那一节**，
   但页脚只把人送到仓库首页，没有直接锚到那一节。锚点（`#…`）依赖 GitHub 对中文标题生成的
   fragment，改一次标题就烂，故不锚。
2. **页脚没有版权年份与作者**（README 写的是「[MIT](LICENSE) © 2026 Xerxes-2」）。
   页面上多一句 `© 2026 Xerxes-2` 属于纯装饰，票要的是「许可提一句」，已经提了；
   要加是一行字的事，留给人裁。
3. **两条链接不加图标、不加「在新标签打开」的提示字**。这一票明确不做徽章墙、不引第三方脚本，
   而 `target="_blank"` 的可访问性提示（`aria-label` 之类）在只有两条链接的页脚上属于过度设计。
4. **`.site-footer` 的样式是追加的两块**，没有动 `styles.css` 里既有的任何一条；
   颜色只用了 `var(--line)` 与 `inherit`，因此明暗两种 `color-scheme` 下都跟着正文走
   （只在浅色下看过图；深色下没有任何写死的颜色可漂）。
5. **闸门断言的是「页脚里有一条 `https://` 外链」，不是「地址正好是那一个」**。
   要断言地址就得把地址复述进脚本，那正是票里禁止的「写死在多处」。
   地址错了这道闸门拦不住——那种错由 §2 的单一真源与人的验收（27 票）来管。

## 7. code-review — Standards 轴

fixed point `eac6f5a9`（change `rroturqn`）→ 本次工作副本。无法派生 sub-agent，自己顺序跑的。
标准来源：`AGENTS.md`、`docs/agents/fsharp-style.md`、`docs/agents/issue-tracker.md`、
`docs/agents/triage-labels.md`、`.scratch/llm-riichi-arena/run/RUNBOOK.md`、`docs/adr/0005`，
外加 code-review skill 那份 Fowler 坏味道基线。工具强制的（Fantomas / `check-style.sh` / Biome / tsc）
本次全绿，不重复列。

### Hard violation：0

- **jj-only ✓** 全程 `jj status` / `jj diff` / `jj commit`，一条 git 命令都没跑；无远端操作、
  无 `jj op restore`、没 abandon 别人的 change、没用交互式 flag。只在 `janpo-ws-c` 里干活。
- **禁改边界 ✓** `README.md` / `web/index.html` / `web/src/agent/` / 引擎 / `CONTEXT.md` /
  `docs/adr/` / `.github/workflows/` 全部一字未动；`styles.css` 只追加、没改既有规则；没做布局改造。
  `jj diff --stat` 共 8 个条目（5 个代码 + 2 张图 + 报告；票文件与 DECISIONS 随后一并提交）。
- **F# 风格 ✓** 新代码三个绑定，没有嵌套应用（规则 1）、没有 `fun x -> f (g x)`（规则 2）、
  没有三层变换链（规则 3）、没有多余括号（规则 8，闸门也查）、没新增 `let mutable`（规则 5 预算不动）。
  `$"{repoUrl}/blob/HEAD/LICENSE"` 是插值字符串，不是被禁的那种嵌套。
- **ADR-0005 ✓** 页脚是 Feliz 组件，写在 F# 侧（「配置表单等杂活也在 F# 里写」的同一条）；
  没有新的跨界，TS 侧一行没加。
- **注释写「为什么」✓** `Footer.fs` 写清了三件为什么（为什么单一真源、为什么 `blob/HEAD`、
  为什么 `target="_blank"`）；`Main.fs` 写清了「为什么在开关之外」；`verify-tracer.mjs`
  写清了「为什么这条也要有闸门、为什么不在脚本里复述地址」。
- **票文件 ✓** 验收框逐条勾上并各注一句证据，`**Status:**` 按 triage-labels 改成 `ready-for-human`；
  决策追加在 `DECISIONS.md` **文件末尾**新起的「## 37」段。

### 判断题（坏味道基线，都不是硬伤）

1. **Divergent Change：`verify-tracer.mjs` 现在为三个理由被改**（双目标对拍 + 藏没藏住 + 露没露）。
   **未拆**：与票 35 记过的理由相同——拆出去要多起一次 preview 与一次无头浏览器，
   而「默认视图上访客该看到什么、不该看到什么」本就是同一次打开该问的两个问题。
   两类问题分了数组、分了报错段，不会混。
2. **Duplicated Code（看过，不算）**：`site-footer` 这个 testId 在 F# 与脚本里各写一次。
   与票 35 记的「开关字面量跨语言重复」同源，且同样由闸门当场扣住（对不上就红）。
   为一个字符串开一道 TS→F# 的反方向口子不值（ADR-0005）。
3. **Speculative Generality（轻微）：`link` 是个只有两个调用点的私有辅助函数。**
   **留着**：它承载的是那条「为什么 `target="_blank"`」的理由，两处复制粘贴等于把理由复制两遍；
   删掉它省不下行数。
4. **Mysterious Name（自查）**：`Footer.Bar` 的 `Bar` 是「一条横栏」的意思，与 `App.TracerPage` /
   `TablePage.Page` 同一类命名（组件名是名词）。`CONTEXT.md` 的术语表管的是日麻词，页脚不在其列。
5. **Shotgun Surgery（看过，不算）**：一条链接改到 6 个文件看着散，但其中 3 个是「注册新文件」
   （fsproj）、「挂上外壳」（Main.fs）、「给它样式」（css），是这个代码库加一个页面部件的固有代价；
   闸门与回显各一处。真源仍然只有 `Footer.fs` 一个。

其余基线项（Feature Envy / Data Clumps / Primitive Obsession / Repeated Switches / Message Chains /
Middle Man / Refused Bequest）逐条比过，本次 diff 一条都不沾。

### Spec 轴（顺带自查）

票的五条验收全部落地（§1 §2 §3），三条「不做」全部遵守（无徽章、无 star 按钮、无第三方脚本；
未碰 README/agent/引擎/workflows；未做布局改造）。
**scope creep 两处，都记在 DECISIONS**：`verify-tracer.mjs` 的那两条断言（§4）、
`ci-web.sh` 的两处注释与回显（那一道现在多守一件事，回显不说就成了暗桩）。
两处都不在禁改清单里，且都跑过全套 CI。

**一句话总结**：Standards 0 个 hard violation（5 条判断题，最重的是 `verify-tracer.mjs`
的 Divergent Change，理由与票 35 同源）；Spec 0 个缺失（2 处有记录的 scope creep）。没有 blocking 项要修。
