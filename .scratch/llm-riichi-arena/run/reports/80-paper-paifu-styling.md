# 80 — 前端美化：纸面牌谱风（静态打磨 + CC0 牌面资产 + 配色）

**状态**：done（ready-for-human——主人看图终审）　**工作区**：`janpo-ws-a`
**fixed point**：`5751a839`（`main`）
**验证**：`./scripts/ci.sh` **EXIT=0**（fantomas / 风格闸门 / dotnet 全量 / Biome / tsc /
浏览器十四趟全 ✓）；新改的赤牌断言**反向自证四次、每次 EXIT=1**（§4）。

一句话：每一屏落进同一套「纸面牌谱」视觉语言——米白纸底（`#f6f1e7`）、暖墨字（`#2e2a24`）、
朱红（赤牌·立直·警示）与靛青（链接·选中·时间轴）各司其职；**牌是真的牌**：FluffyStuff 的
CC0 SVG 牌面嵌进 CSS 画的象牙白圆角牌框，他家暗牌是牌背（`Back.svg`，CSS 压暗成旧书封皮红），
赤五用 `-Dora` 变体 + 朱红牌框；思考气泡是「淡纸灰底 + 左墨线」的页边批注。**功能一行没改**：
F# 只动了 `TableBoard.fs` 的一处（给立直/一发那两枚标记加 class），闸门只动了 `verify-board.mjs`
的赤牌断言（改硬，见 §3）。

改到的文件：`web/src/styles.css`（主战场，全文重写）、`web/src/tiles/`（38 张 SVG + README，新增）、
`src/Janpo.Web/TableBoard.fs`（marks 加 class，12 行）、`web/scripts/verify-board.mjs`（赤牌断言换通道）、
`web/biome.json`（把 `src/tiles` 排除出 lint——第三方 CC0 资产，与 `src/generated` 同列）、
`docs/images/home.png` / `table.png`（重出）。

**没碰**：`src/Janpo.Engine/**`、`src/Janpo.Cli/**`、`web/src/agent/**`、`Route` / `Share` /
MVU 消息与状态、`web/public/demo-paifu.json`（票 79）、`CONTEXT.md`、`docs/adr/**`、别人的票。
**没做**（主人裁决「只做静态」）：`transition` / `animation` / `@keyframes` 一个没有
（`grep -c` 为 0）、无移动端重排（票 44 那条 40rem 媒体查询原样保留）、无暗色
（`color-scheme` 从 `light dark` 钉成 `light`，理由见 §6-1）、无 webfont（本地字体栈）、
除 clone 外零新依赖（`package.json` 一字未动）。

---

## 1. 资产管线

- **来源**：`github.com/FluffyStuff/riichi-mahjong-tiles`，commit
  `26e127ba2117f45cdce5ea0225748cc0cfad3169`（2024-06-15），**CC0 公共领域**（上游 LICENSE.md）。
- **拿了 38 张** `Regular/` 的 SVG 进 `web/src/tiles/`：34 正牌 + `Man5-Dora`/`Pin5-Dora`/`Sou5-Dora`
  三张赤五 + `Back` 牌背。`Front.svg` / `Blank.svg` 没拿——牌框由 CSS 自己画（象牙白、圆角、
  底边一道压深的内阴影当厚度），SVG 只当牌面花色。
- **按需引，不做 sprite**：`styles.css` 按 `data-pai`（mjai 记法，`Tile.toMjai` 的输出，DOM 上
  本来就有）逐张 `background-image: url("./tiles/….svg")`，牌背走 `.tile.back`。Vite 把它们当
  普通资产哈希进 `dist/assets/`（38 张都超过 4 KB 内联阈值，**零内联**，浏览器按需拉、可缓存）。
  否决了 data-URI 内联（CSS 会膨一个数量级、闸门读不到可比对的 URL）与 SVG sprite
  （`background-image` 引 `<symbol>` 兼容性差，还得多一层构建）。
- **牌的身份仍然机器可读**：`data-pai` 一个没动，**牌面字符也还在元素的 textContent 里**
  （读屏与既有的 `textContent` 型闸门照读），只是画面上用 `color: transparent` 藏起来、由 SVG 说话。
  因此 `verify-home` / `verify-bubbles` 的 DOM 摘要、`verify-board` 的「宝牌指示牌那一格不是空的」
  这些**读文本的断言一条都不用改**。

## 2. 三组前后数

| 量什么 | 改前（`5751a839`） | 改后 | 差 |
|---|---|---|---|
| 牌面资产总字节 | 0（牌是文字） | **776,153 B**（38 张 SVG，src 与 dist 同字节） | +776 KB（clone 全仓约 5 MB，安装预算内） |
| `vite build` 产物（`du -sb dist`） | 1,281,254 B | **2,061,898 B** | +780,644 B（= 38 张 SVG + CSS 7,242→11,663 B） |
| 首页首屏（打开 → `table-board` 出现，5 次中位，量法照报告 71 §2.2） | **123 ms**（122/122/123/123/151，本机复测与票 71 基线同数） | **152 ms**（148/148/152/154/186） | +29 ms（首屏多拉 ~30 张牌面 SVG；仍远在秒级之内） |

## 3. 数值断言：改了哪几处、前后值与理由

**只改了一处闸门（`verify-board.mjs` 的赤牌断言），且是往硬的方向**；其余数值全部原样活着：

| 断言 | 前 | 后 | 理由 |
|---|---|---|---|
| 赤五与普通五一眼可分 | `.tile.aka` 的文字色 ≠ 随便哪张普通牌的文字色（绿时打印 `rgb(192,57,43)` vs `rgb(0,0,0)`） | **两个通道、对照组换成同花色的普通五**（探针元素从同一份样式表现取 `data-pai` 去掉尾缀 r 那张）：① 牌面图 ≠ 普通五的牌面图，且两张都真贴了图；② 牌框色 ≠ 普通五的牌框色（绿时打印 `rgb(180,58,44)` vs `rgb(182,169,143)`） | 牌面从文字变图后所有 `.tile` 的文字色都是 transparent，旧断言必然假红；新形态一条没放宽反而多一条（图不同 **且** 框不同），对照组也更准（从「随便哪张牌」收紧到「同种普通五」）。四次反向自证见 §4 |
| 刚摸那张的间隔 9.6px | `.tile.drawn { margin-left: 0.6rem }` | **没改**（0.6rem = 9.6px 照旧） | 新牌宽 1.75rem 下 0.6rem 的空仍一眼可见，不必动 |
| 摸切 dashed / 手切 solid | `borderTopStyle` | **没改**（虚线 + 0.55 淡化照旧） | 语义记号照旧 |
| 牌背可见（票 32） | 边框色不透明 + `backgroundImage ≠ none` | **判据没改**；红话从「没有斜纹底」改说「没有牌背图」 | 底纹从 CSS 斜纹换成 `Back.svg`，旧措辞会误导下一个读红的人；断言本体（EXIT=1 的条件）逐字未动，§4 red-4 证明它仍咬得动 |
| 票 44 八项 + 五视角方位、票 51 槽位坐标、票 76 气泡矩形不相交 | — | **一个字没动，全绿** | CI 十四趟原文可查 |

顺带说清一个「像改了其实没改」的数：`.tile` 从 `min-width: 1.5rem` 的文字块变成
`1.75rem × 2.35rem` 的定尺牌（高宽比 ≈ SVG 的 300×400）。横放那张的
`margin: 0 0.3rem` 数字没变，但现在是**算出来的**：(2.35 − 1.75) / 2 = 0.3rem，恰好等于
旋转后两侧探出的那截，注释里写了式子。

## 4. 反向自证：新 / 变形断言各按红一次（改产品 CSS，不改闸门）

四次全部实跑，`node scripts/verify-board.mjs`，EXIT 均为 1，改完 `diff` 对回备份还原：

- **red-1**｜`5sr` 的牌面图改指 `Sou5.svg`（与普通五同图）：
  `赤牌「赤5索」（5sr）与普通「5s」贴的是同一张牌面图：赤 5 看不出来是赤 5`
- **red-2**｜`.tile.aka` 的边框色改回 `--tile-edge`：
  `赤牌「赤5索」的牌框与普通「5s」同色（rgb(182, 169, 143)）：一眼扫过去分不出赤 5`
- **red-3**｜把 `5sr` 那条映射整个删掉（背景图 none）：
  `赤牌「赤5索」（5sr）或普通「5s」没贴牌面图（赤 none／普通 url("…/Sou5-….svg")）：牌不像牌了`
- **red-4**｜把 `.tile.back` 的 `background-image` 删掉（票 32 断言在新 CSS 下还咬得动吗）：
  `座位 1/2/3 的手牌的牌背没有斜纹底：与明牌一眼分不开`（三行；此后把措辞改成「没有牌背图」）

## 5. testId 与 `data-*`：逐字不变（照票 70 的做法）

- 改到源码的文件里只有 `src/Janpo.Web/TableBoard.fs` 一个 F#/TS 文件（`jj st` 为证），
  对它跑 `grep -oE 'testId "[^"]*"|testId \$"[^"]*"'` 与 `grep -oE '\("data-[a-z-]+"'`
  排序对照基点 `5751a839` 的同文件：**两份 diff 均为空**。
- 全仓 `src/Janpo.Web/*.fs` 的 testId 出现 48 处、`data-*` 47 处，与基点同数
  （其余文件未动，恒等）。新增的 class（`mark riichi`）不在 testId / `data-*` 集合里。

## 6. 配色与形态：关键取舍

1. **`color-scheme` 从 `light dark` 钉成 `light`**。主人裁决「不做暗色」，而纸面配色是一套
   固定的浅色值；留着 `dark` 的话系统深色下会得到一套谁也没审过的「深底配纸色变量」拼盘
   （票 32 那类隐形的温床）。这是视觉语言的一部分，不是砍功能——从前的「深色」也只是跟随系统，
   没有开关。
2. **配色一处定义**：全部色值只活在 `:root` 的 CSS 变量里（`--paper` / `--ink` / `--vermilion`
   `#b43a2c` / `--indigo` `#2f4b6e` / 牌框牌面等），警示、兜底气泡、立直棒、赤牌框全部引用变量，
   `#c0392b` 这类散写清干净了。对比度：ink/paper ≈ 12:1、vermilion/paper ≈ 5.2:1、
   indigo/paper ≈ 7.9:1，全过 WCAG AA；次要文字的淡化统一抬到 ≥0.72 不透明度
   （原 0.6 那档在纸底上只有 3.8:1，不到 4.5）。
3. **字体**：本地 serif 栈（`Noto Serif CJK SC` → `Source Han Serif` → `Songti SC` → serif），
   零 webfont。围棋书的气质大半来自它。
4. **牌背的橙红压暗**：上游 `Back.svg` 是高饱和橙红，13 张排一排比谁都响、还抢朱红的戏；
   按票面「调色走 CSS」给 `.tile.back` 一条静态 `filter: saturate(0.45) brightness(1.04)`，
   压成旧书封皮那种砖红。
5. **立直/一发标记的朱红**要 CSS 能选中它们，因此 `TableBoard.fs` 的 marks 从字符串表改成
   （文字, class）对——**只加 class，语义与文字逐字未动**（票面「视图文件只许加 class 与包裹结构」）。
6. **W5 新部件都罩住了**：分享按钮与回执行（`table-share-note`）、导入的原生 file input
   （`::file-selector-button` 穿上与其他按钮同一件衣服，宽度不再被 `.controls input` 的 8rem 掐住）、
   `table-import-fault` 走统一的朱红 `.error`；「在想」气泡的已等秒数加 `tabular-nums`（数字跳动不抖）。

## 7. 截图：我亲眼看到了什么（判据 7，每张都自己打开看过）

- **`docs/images/home.png`**（首页，上帝视角，重出）：牌**像牌了**——万子红「萬」黑数字、
  筒索是真花色，四家的河是 6 列方阵。座位 0 河里的**赤五萬一眼跳出来**（整字红 + 朱红框，
  邻牌全是墨框）；座位 3 的白皮碰是三张空白牌（白就是白板，authentic，但见 §9-2）；
  摸切虚线淡色在河里认得出。中央那格比纸面凹一层，像谱面的注记框。时间轴、选中的
  「2×」「东1」「上帝视角」全是靛青。
- **`docs/images/table.png`**（`?table=1`，围观视角，重出）：他家三排**砖红牌背**，与明牌
  隔一屏也分得开；对家「碰 中中中」**横放的中躺在最左格**，就是牌谱的画法；配桌三开关、
  四席绑定、档案库的按钮与输入框全套纸面化，选中项靛青。
- **`80-bubbles-detail.png`**：四席四个气泡，「说 假端点甲说：……」淡纸灰底 + 左墨线，
  **真像页边批注**；点开的全文面板九样齐整，prompt 尾部在自己的滚动格里。
- **`80-bubbles-troubled.png`**：座位 0 的兜底气泡整条朱红（红左线 + 红字 + 淡红底），
  与其余三席的墨色批注一图之内就分开；上方 Agent 状态行同一个红。三态（虚线在想/墨线说/朱红兜底）
  不用读字就分得出。
- **`80-settlement.png`**（首页拖到末帧）：结算面板「和了……役：立直 1 番、平和 1 番、
  断幺九 1 番、红宝牌 2 番／30 符 5 番 8000 点（满贯）」在纸面卡片里逐行可读；
  里宝牌指示牌回到桌心；座位 0 头上的「立直」标记是朱红小章。
- **`80-danger.png`**（种子 9 有主见档 + 危险度开）：危险度排序逐行清楚；座位 3 的
  「立直」「一发」两枚朱红章并排，桌心一根朱红立直棒——朱红=立直这条分工在一屏里自洽。
- **载荷错误与 `?dev=1`**（看过没进仓库）：坏 hash 的「载荷读不动：……」一行朱红、控件照在；
  曳光弹页两张卡片落在纸面卡片样式里，表格、mjai 事件折叠一切照旧（`verify-tracer` 全绿）。

## 8. code-review（Standards + Spec 两轴，fixed point `5751a839`，派不出 sub-agent 自己顺序跑）

### Standards — blocking 0

- **jj-only ✓**（全程 `jj st` / `jj diff` / `jj commit`，无远端操作、无交互式 flag；
  clone 上游仓库在 /tmp，不在本仓）。
- **F# 风格 ✓**：唯一的 F# 改动是列表字面量从 `string` 换成 `(string, string)` 元组加一处
  `List.map`，无嵌套应用、无 mutable；fantomas 写盘跑过、`--check` 绿。
- **Biome/tsc ✓**；`biome.json` 的 `!src/tiles` 与既有 `!src/generated` / `!public` 同列
  （第三方资产不进自家 lint），不是放宽自家代码的闸门。
- **注释写为什么 ✓**：styles.css 头部写了配色锚点、AA 校核、「闸门读计算样式的那几处」清单；
  牌背为什么压暗、taken 的 margin 为什么是 0.3rem、color-scheme 为什么钉 light 都在注释里。
- **术语 ✓**：没有新标识符进领域层；`CONTEXT.md` 一字未动。

### Spec — 票面逐条

三条裁决照做（只做静态 §0；纸面牌谱 §7 的图为证；指定资产与配色 §1/§6）。四个勾选组
全部落地：牌/身份/每一屏/资产管线见上文；「数」在 §2/§3；验收三条在 §4/§5/§7。
**scope creep 两处，都记进 DECISIONS「## 80」**：`biome.json` 加一条排除（不加 CI 必红，
别无他路）；`verify-board.mjs` 赤牌红话与牌背红话的措辞更新（断言语义不变）。

### 记录不修的 nitpick

1. `verify-bubbles` 驱动时偶发把 h1 文字选中，全页截图里像一道高亮（截图工件，页面无此态）。
2. `.tile` 的文字仍可被鼠标选中复制（牌面下藏着透明字）——对读屏与闸门是特性，对拖选是小怪癖。

## 9. 留给人的待审项 / 已知不完美

1. **我自己最不满意的一处：白板（白）在河里像一格空白**——上游 `Haku.svg` 就是一圈极淡的
   蓝框，26px 下几乎隐形，乍看像「掉了一张牌」。这是牌的本来样子（白板就是白的），
   数据与断言都齐（`data-pai="5z"`、textContent「白」都在），但主人若嫌它虚，
   可给 `.tile[data-pai="5z"]` 单加一圈描边——一处 CSS 的事。
2. **牌背压暗用的是 `filter`**，主人若要别的背色（比如靛青系），改 `.tile.back` 那两行即可；
   想彻底换掉可以把 `Back.svg` 换成自画的纸纹背（另一张票的量级）。
3. **左右两家的牌仍未转 90°**（票 44 §9-2 的旧账，票面也没要）：纸面化之后左右两家的
   「河贴中心、副露贴外」继续用横向对齐表达。
4. **首屏 +29 ms**（123→152）：全部来自首帧要拉的那批牌面 SVG；hash 文件名可长缓存，
   第二次打开就回到无增量。若将来要抠，per-tile SVGO 或 sprite 才有意义（本票禁加依赖，没做）。
5. **深色系统下页面恒为纸面浅色**（§6-1）。若哪天要暗色，是「第二套配色变量」的新票，
   不是把 `color-scheme` 拨回去。

## 10. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**（dotnet 全量 + 浏览器十四趟全 ✓） |
| 牌桌那道单跑 | `cd web && node scripts/verify-board.mjs` | 八项 + 方位 + 槽位全 ✓，赤牌两通道打印在日志 |
| 新断言咬得动 | §4 四次反向自证 | 每次 EXIT=1，红话原文抄录 |
| TS/JS 格式与 lint | `pnpm run check` | 84 文件，无 fix |
| TS 类型 | `pnpm run typecheck` | 干净 |
| F# 格式 | `dotnet fantomas .`（写盘）+ CI 的 `--check` | 绿 |
| 首屏 | 一次性 playwright 探针（/tmp，没进仓库），preview + 5 次取中位 | §2（基线 123 ms 先复测对上，再量改后 152 ms） |
| 截图 | `shoot-table.mjs`（两张）、`verify-bubbles.mjs --shoot`、一次性补充探针（结算/危险度/错误/dev） | §7，八张全自己打开看过 |
| 还原干净 | 四次坏法各 `diff` 对回 `/tmp/t80-styles-good.css` | 全 OK |
