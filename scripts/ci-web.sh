#!/usr/bin/env bash
# JS 侧的关卡（M1 起）。`scripts/ci.sh` 会调它，也可以单独跑。
#
# 十五道：TS/JS 的格式与 lint（Biome）→ **TS 类型闸门**（tsc --noEmit）→ **Agent 层的用例**
# （node --test，回放录制的响应）→ **prompt 的语义不变量**（扫一批真实对局）→ Fable 编译
# → Vite 产物 → 之后是**浏览器里的那九趟**：**浏览器内跑引擎并与 dotnet 侧对拍**
# → **牌桌上人看得见的八项** → **浏览器内跑黄金用例** → **浏览器内导出牌谱并回放**
# → **同一道闸门再走一整场** → **那道 key 闸门的反向自证**
# → **回显 key 的端点跑一手，牌谱里仍然没有它**
# → **URL 分享的载荷往返得回去** → **那道载荷闸门的反向自证**。
#
# **前六道各是一条命令，后九趟合成一条**（票 56）：它们从前各起一个 node 进程、一个 vite
# 服务器与一个 Chrome，现在跑在同一条跑道上（`web/scripts/verify-browser.mjs`：一个 Chrome
# 进程 + 一台 preview + 一台 dev server，九趟各开自己的 page/context）。**一趟没少、
# 一条断言没拆**——每趟调的就是 `verify-*.mjs` 里那个函数，单跑（`pnpm run verify:board` 等）
# 与合并跑跑的是同一段代码。九趟各自的来历写在 `verify-browser.mjs` 的那张表里。
#
# 第七道是 19 票的验收：同一种子在浏览器里跑出的终局点数与顺位，必须与
# `janpo kyoku` / `janpo game` 逐项相同（跑的是 Vite 打包后的产物）。
# 它同时带着票 35：曳光弹现在藏在 `?dev=1` 后面，因此那一道先开不带开关的地址
# 确认开发向内容一样不在，再带开关去读那几行数。同一次打开顺带带着票 37 的反面：
# 默认视图的页脚里**必须**有一条回仓库的外链与一句许可（访客找到源码的唯一一条路）。
# 第八道**牌桌八项**是票 44 的验收：**渲染给模型**那一侧有黄金用例逐字段钉着，
# **渲染给人**那一侧 M1 只有截图（两次信息缺失都是人肉眼发现的）。它在一局**真对局**里
# 走到立直、副露、手切与摸切同时在场的那一手，然后逐项核八项信息（手牌 / 河 / 副露 /
# 点数 / 供托与本场 / 宝牌指示牌 / 巡目 / 立直状态）与**方位**
# （四家画在哪个格子里，切视角时要跟着转）。
# 第九道是 21 票的验收：`tests/fixtures/golden/dual-target.json` 里的每条用例，
# 在浏览器里跑出的每个字段的每一行都要与文件一致（跑的是 Fable 的输出本身）。
# 同一份用例文件在 dotnet 侧由 `dotnet test` 的 `GoldenSuiteTests` 跑——**两侧读同一份数据**。
# 第十道是 26 票的验收：浏览器里点一下「导出牌谱」真下下来一个文件，那份字节交回引擎
# fold 一遍，事件流逐条相同、点数与牌桌上的一致（ADR-0002 的回放）；它同时带着票 34 那条
# 「导出物里没有 key」。第十一道是票 39 的验收：同一个导出闸门再走一趟**整场**——
# 它那条点数断言在局中与终局读的是两个不同的权威（`GameState` / `Game`），
# 而只走几十手的那一趟永远碰不到后一种（票 39 未修时它在那里假红）。
# 第十二道是票 34 的反向自证：**一道从不失败的闸门等于没有闸门**，
# 因此每次 CI 都把那条断言按红一次（拌了 key 的导出物），并核实它红得就是因为那把 key。
# 第十三道是票 36 的验收：provider 的报错原文会流进牌谱的三处（`fallback` / `output` /
# 重试那一轮的 prompt），而自建网关完全可能把收到的 key 原样回显。那一道拿一个
# **真的会回显 key 的本机假端点**跑一手，导出牌谱，既查「里面没有 key」，
# 也查「打码记号在不在」（阳性对照：端点哪天不回显了，这道闸门就该告诉人它白给了）。
# 第十四道是票 77 的验收：URL 分享那一段载荷。引擎现打两场（东风战与半庄），
# 压一次、编一次、解一次，再把解出来那份交回引擎 fold：事件流逐条相同、终局点数与顺位相同。
# 它同时带着两样：**逐位置改坏一个字符**（每次要么红在「载荷读不动」、要么解出来逐字相同）与
# **审计三样一个都不上路**（thinking / prompt 尾部 / 一把假 key，自带阳性对照）。
# 第十五道是它的反向自证，理由与第十二道逐字相同。
set -euo pipefail

cd "$(dirname "$0")/.."

if ! command -v pnpm >/dev/null 2>&1; then
  echo "找不到 pnpm。nix dev shell 里自带；宿主机上装法见 docs/development.md。" >&2
  exit 1
fi

# 无头验收要一个 Chrome/Chromium。跑批机器上有 /usr/bin/google-chrome-stable；
# 别处用 JANPO_CHROME 指过去，或 `pnpm dlx playwright install chromium`。
# 实在没有浏览器的环境（例：最小容器）可以 JANPO_NO_BROWSER=1 跳过**后九趟**（它们合成一条
# 命令了，逐趟列在 `web/scripts/verify-browser.mjs` 的那张表里），前六道（biome / tsc /
# node --test / 语义不变量 / fable / vite build）照跑——
# 但那样浏览器里那九趟验收一趟都没被验，别拿它当绿。
NO_BROWSER="${JANPO_NO_BROWSER:-0}"

echo "== pnpm install =="
(cd web && pnpm install --frozen-lockfile)

# --error-on-warnings：Biome 默认只拿 error 当失败，警告（包括它自己的配置弃用提示）会静静滑过去。
echo "== biome ci（TS/JS 的格式与 lint）=="
(cd web && node node_modules/@biomejs/biome/bin/biome ci --error-on-warnings .)

# 票 23 装上的（ADR-0005 说好的时机）：**只管 Agent 层与它的用例**，
# `src/generated`（Fable 的上万行输出）不在 tsconfig 的 include 里。
echo "== tsc --noEmit（Agent 层的类型闸门）=="
(cd web && pnpm run typecheck)

# **这一道不调真实 API**：它回放 `web/tests/fixtures/agent/` 里录制下来的响应
# （合法输出 / 越界 id / 格式跑偏 / 超时 / provider 报错）。
# 重录用 `pnpm run record:agent`，需要一把真 key，手动跑。
# 它还带着票 43 那道**新鲜度闸门**：改了 prompt 渲染器却没重算渲染器摘要，
# `render-version.test.ts` 当场红（改法：`pnpm run render-digest`）——没它的话，
# “改渲染＝废缓存”那一半就又静默了。
echo "== node --test（Agent 层的确定性用例）=="
(cd web && pnpm run test)

# 票 41 的验收：黄金用例钉的是**决策包**（结构化数据），前缀属性钉的是**字节单调**，
# 两道都不检查**渲出来的那句中文在日麻规则下成不成立**——票 40 那句「吃 来自对家」
# 就是从这个缺口漏过去的。这一道扫一批真实对局（`janpo decide --sequence`，本机跑引擎、
# **零网络请求**），逐手渲染两档再逐条断言，最后把十一条不变量各自**按红一次**
# （反向自证：一道从不失败的闸门等于没有闸门）。
echo "== prompt 的语义不变量（扫真实对局 + 逐条反向自证）=="
(cd web && node scripts/verify-invariants.mjs)

echo "== fable 编译（引擎 + Feliz 页面 → JS）=="
(cd web && pnpm run fable)

echo "== vite build =="
(cd web && node node_modules/vite/bin/vite.js build)

if [[ "$NO_BROWSER" == "1" ]]; then
  echo "== 浏览器里那九趟（曳光弹对拍 / 牌桌八项 / 黄金用例 / 牌谱导出两趟 / 两道 key 闸门 / 分享载荷两趟）：按 JANPO_NO_BROWSER=1 跳过 =="
else
  # 九趟一条命令（票 56）：一个 Chrome + 一台 preview + 一台 dev server，九趟各开自己的
  # page/context。**每趟仍然单独跑得起来**——它红的时候会把单跑那一条命令抄给你
  # （`node scripts/verify-board.mjs` 之类），调试时照抄就只重跑那一趟。
  # 反向自证那一趟（拌了 key 的导出物必须当场红，且红得就是因为那把 key）也在里面，
  # 两种失法各报各的话，与从前那段 shell 逐字相同。
  echo "== 浏览器里那九趟（共用一个浏览器进程与一台服务器）=="
  (cd web && node scripts/verify-browser.mjs)
fi

echo "== JS 侧全绿 =="
