# 71 — 首页第一眼是一桌牌在走：Live / Replay 接缝、`?table=1`、Demo Paifu 占位

**What to build:** 访客打开 `https://xerxes-2.github.io/janpo/` 什么都不用配、不用 key，
第一眼就是一桌牌在走（spec 的 story 1，ADR-0003 说它由 Demo Paifu 兑现）。
主持人要自己开一桌就去 `?table=1`。**页面从此有两种「牌桌从哪来」**，而播放控制、视角切换、
牌桌与结算的渲染**只有一份实现**。

**Blocked by:** 70（拆文件）

**Status:** ready-for-human

## 上一波留给你的既成事实（票 70 与 77 已并入 `main`）

- **页面已经拆开了**（票 70，零行为改动）。`<Compile>` 顺序即编译顺序：
  `App.fs → TableState.fs → AgentLine.fs → TableBoard.fs → TablePanel.fs → TablePage.fs → Footer.fs → Main.fs`。
  **你的活主要在 `TableState.fs`**（`TableModel` / `TableMsg` / `init` / `update` / `schedule` 全在里面）；
  `TablePage.fs` 只剩 58 行转出外壳——**加新的公开入口要两处都加**（`TableState` 定义 + `TablePage` 转出），
  否则 `tests/Janpo.Web.Tests` 调不到。跳文件的助手一律 `internal`，dotnet 侧用例看不见。
  五个模块都带 `[<RequireQualifiedAccess>]`。
- **分享载荷的编解码已经有了**（票 77）：`Share.toPayload` / `Share.ofPayload`（都是 Promise）。
  **但这一票仍然不接它**（接上去是票 78）：带 hash 打开时退回首页 Demo，不许白屏。
- 浏览器闸门现在是 **九趟**（票 77 新增两趟 `verify-share`）；改趟数措辞时
  `ci-web.sh` / `verify-browser.mjs` / `browser-lane.mjs` 三处都要跟着改（票 77 刚改过一轮，照它的样子）。

## 主人已经裁掉的分叉（照做，别重新设计）

- **一页一 Model，模式是联合类型**：`Source = Live of … | Replay of …`，
  `Playback` / `Viewpoint` / `ShowDanger` 与牌桌视图在联合之外，两种来源共用。
- **地址**：`/` = Demo 回放（自动播）；`?table=1` = 配桌与 Live（**默认暂停**，与今天一致）；
  `#<载荷>` 留给分享（**这一票只保证它不炸**，解码是票 77/78）；`?dev=1` 与它们正交（票 35 不动）。
  hash **只装分享载荷**，不当路由用（35-1 当年就是为这件事把 dev 开关放在 query 上）。
- **Demo 资产先用 bot 牌谱占位**；换成真的四席对局是**票 79**（主人已把真 key 放在 `/tmp/deepseek_key`，
  但那要等四席与气泡落地才跑得出来）。这一票只负责「换文件就换了 Demo」的口子。

## 要什么行为

- [x] `/`：载入随应用分发的 Demo Paifu，**自动播**；播到终局停在结算面板，页面上有「从头再放」
      —— `Demo.paifu` 用 `fetch` 拉 → `Table.replay` fold 成 256 帧 → `Playback.playing 2×` 当场开播；
      末帧停下来（`HomePageTests.播到终局就停在结算面板上`），「从头再放」= `Restarted` 回第 0 帧
- [x] `/` 上有一条「自己开一桌」的路（链到 `?table=1`）——访客要摸得到 Host 那一侧
      —— `home-host-link`，地址的真源是 `Route.tableHref`；闸门**真点过去**并核落地地址
- [x] `/` 上**没有**配桌与模型面板（访客第一眼不该是一张表单；票 35 的「默认视图只该有牌桌」同一条标准）
      —— 类型上就摆不出来（`TablePage.homePage` 拿不到 `LiveTable`）；闸门另查 9 个 testId
- [x] `?table=1`：今天的页面一字不少（配桌、模型面板、种子、单步/播放/倍速、导出、下一局、视角、危险度）
      —— `docs/images/table.png` 重出后逐项对过（报告 §5）；八趟闸门全在这一页上绿
- [x] 回放模式里**结算面板有役与符番**：`Replay` fold 的时候把 `HoraReading` 捞下来
      （`GameState.horaOf` 只在宣言那一刻答得出来，与 `Table.Readings` 同一个理由）
      —— **落成了「回放复用 `Table.apply` 的捞法」**（裁决 71-2）：引擎只多交出动作序列，
      捞的那一刻仍是宣言那一刻，而且少了一份实现。`ReplayTableTests.和了那一帧的读法在` 钉着
- [x] 回放模式里视角切换照旧（座位视角要的掩蔽流也得在 fold 时建起来）
      —— 同上一条：`Table.apply` 本来就在推四条 `SeatStream`。
      `ReplayTableTests.逐帧的掩蔽流与重头 fold 一致` 逐帧比 `Observation.ofState`

## Demo 资产

- [x] **可复现**：由一条写在报告里的命令 + 一颗种子产出（CLI 加子命令、或复用现有输出拼装，你定），
      不是「某次手点导出下下来的一份神秘 JSON」
      —— 新子命令：`janpo paifu 3 --opinionated > web/public/demo-paifu.json`（与页面导出同一个编码器）
- [x] 挑一场看得懂的：有立直、有副露、以和了终（有主见 bot；均匀随机基本碰不到立直）
      —— 5 次立直成立、3 组碰、3 次和了、1 次流局，末局以 30 符 5 番 8000 点荣和收尾（翻出里宝牌）
- [x] **东风战**，不是半庄——首页不该让人等半小时；也让资产小一截
      —— 四局 252 手 / 256 帧，2× 播完 1 分 17 秒；`HomePageTests.Demo 是东风战` 钉着
- [x] **用 `fetch` 拉，不打进 JS bundle**：主人事后换成真 LLM 对局的那一份会带 thinking
      （实测约 10 KB/手），打进 bundle 会把首屏拖死
      —— `web/public/demo-paifu.json`（vite 原样拷进 `dist/`）+ `web/src/demo/paifu.ts` 的 `fetch`。
      21,485 字节 / 过线 2,704 字节；首屏 `/` 123ms vs `?table=1` 53ms（报告 §2.2）
- [x] 报告里写清**换资产的手续**：换哪个文件、有没有断言会跟着红、体积上限建议
      —— 报告 §2.3，五条会跟着红的断言 + 一次真换（半庄 + 流局）的红输出在 §4.2 红-9

## 闸门（web/scripts）

谁在导航到 `/`（我替你数完了，**别漏**）：`shoot-table` / `verify-board` / `verify-custom-endpoint` /
`verify-export` / `verify-golden` / `verify-llm-seat` / `verify-redaction` / `verify-share` / `verify-tracer`（开两次）。
`table-drive.mjs` 不导航（它是页面内驱动的助手，被上面三道 import）。

- [x] **要点、要读牌桌的都改开 `?table=1`**：`verify-board` / `verify-export`（三趟，含 `--poison`）/
      `verify-llm-seat` / `verify-redaction` / `verify-custom-endpoint` / `shoot-table`
      —— 地址收在 `serve.mjs` 的 `hostPage(origin)` 一处，八个脚本读它
- [x] **只是借页面跑引擎与闸门代码的也改开 `?table=1`**：`verify-golden` / `verify-share`
      ——它默认暂停，是最安静的一页；留在 `/` 上会被自动播放与资产拉取干扰
- [x] `verify-tracer` **继续开 `/`**（它量的就是「默认视图里没有开发向内容」）；
      含义从此变成「首页 Demo 里没有开发向内容」，断言一条不准放宽
      —— 一条没改。它现在开**三个**地址：`/`（无开发向内容 + 页脚）、`?table=1`（票 38 那段要
      填种子、要点单步，控件只在那一页）、`?dev=1`（对拍）。同一颗种子 1223、同一四条性质
- [x] `shoot-table` 的输出是 `docs/images/table.png`（README 在用）：它得改开 `?table=1`，
      否则那张图会默默变成 Demo 的截图。**另出一张首页的**（访客第一眼是产品门面，值得有图）
      —— `--home` 那一档出 `docs/images/home.png`；两张都重出并**打开看过**（报告 §5）。
      README 顺手改准了（那张图的图注与「怎么玩」第 1 步），记在 DECISIONS 71-6
- [x] 新一道开 `/`：断言 ① 牌桌在动（隔一会儿采两次，手数不同）② 页面上没有配桌控件
      ③ 有一条去 `?table=1` 的路 ④ 页脚照旧（票 37）
      —— `web/scripts/verify-home.mjs`，进 `verify-browser.mjs`（第 2 趟，十趟里）。
      ③ 是**真点过去**并顺带核「那一页默认暂停」
- [x] `verify-tracer` 那两道照旧绿（`/` 无开发向内容、`?dev=1` 才有曳光弹）
- [x] 每道新断言**先红一次**，红的原始输出抄进报告（票 44 立的规矩）
      —— 十三次，浏览器侧六次 + dotnet 侧七次，原文在报告 §4；另有一次「先怀疑断言自己」（判据 6）

## 边界

- [x] **时间轴不在这一票**（拖动与逐事件步进是票 75）：这一票的回放只会顺着播
      —— 回放的控制条上根本没有「单步」
- [x] **不解码 hash**（票 77/78）：带 hash 打开时退回首页 Demo，不许白屏
      —— `Route.landing` 只读 query；`Share.ofPayload` 一次都没被调到
- [x] 不碰 `web/src/agent/**`、不碰引擎的规则判定；`Replay.fs` 只准加「捞役种/建掩蔽流」这类**输出**，
      一条规则都不自己判
      —— `Replay.fs` 只多一项输出（`Driving.Played` + `trace`），与 `game` 共用同一段 fold
- [x] 曳光弹留在 `?dev=1` 后面不动（22-A 那条泄露挂账归 M3 真人坐席）
      —— `App.fs` / `Tracer.fs` 一字未动；判据从 `Main` 原样搬进 `Route`，行为不变
- [x] 截图会变（首页第一眼换了）：重出并**自己打开图看**，报告里写你看到了什么
      —— 报告 §5（两张都打开看过；顺带确认票 32 那次「牌背整片透明」没有回来）

## 为什么这一票要连着地址一起做

首页要自动播，而 `Playback.initial` 的注释明写「一进页面就自己跑起来会让无头验收读到一个动着的牌桌」——
四道闸门今天全靠 `/` 是静止的。**地址与闸门一次改到位**，否则票 75/76/78 每张都要再动一遍这四个脚本。
