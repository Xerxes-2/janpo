#!/usr/bin/env bash
# JS 侧的关卡（M1 起）。`scripts/ci.sh` 会调它，也可以单独跑。
#
# **这份文件是一串命令，不是一份闸门目录**（票 106）。它按顺序跑：
# 先是几道不用起浏览器的静态闸门（同一事实只许一处真源 / 随产物上线的那几份许可），
# 再是 Biome → tsc → Agent 层的用例 → prompt 的语义不变量 → Fable → Vite 产物，
# 最后是**浏览器那条跑道**（`web/scripts/verify-browser.mjs`）与**强 AI 基线自己那条跑道**
# （`web/scripts/verify-baseline.mjs`）。每一步为什么在这儿，写在它自己那一段注释上。
#
# **浏览器那条跑道跑几趟、各是哪一趟、各是哪张票的验收——这份文件一个字都不写。**
# 唯一的真源是 `verify-browser.mjs` 里那张 `gates` 表（趟数由它现算现印），
# 每一趟在断言什么写在它自己那份 `web/scripts/verify-*.mjs` 的文件头上。
# 从前这里抄了一整份逐道叙述，于是每加一趟就要两侧各改一行——票 92 / 90 / 89 各撞红过一次，
# 而本票接手时这份文件一处写着十七、一处写着十八，`verify-setup.mjs` 还写着十一
# ——**那个数到底是几，只有 `gates` 表知道**。抄一遍就是这一票要治的那件事；
# `scripts/check-single-source.sh` 现在盯着这一条。

set -euo pipefail

cd "$(dirname "$0")/.."

if ! command -v pnpm >/dev/null 2>&1; then
  echo "找不到 pnpm。nix dev shell 里自带；宿主机上装法见 docs/development.md。" >&2
  exit 1
fi

# 无头验收要一个 Chrome/Chromium。跑批机器上有 /usr/bin/google-chrome-stable；
# 别处用 JANPO_CHROME 指过去，或 `pnpm dlx playwright install chromium`。
# 实在没有浏览器的环境（例：最小容器）可以 JANPO_NO_BROWSER=1 跳过**浏览器那两条跑道**
# （各合成一条命令了，逐趟列在 `web/scripts/verify-browser.mjs` 的那张 `gates` 表里），
# 前面那几条命令（静态闸门 / biome / tsc / node --test / 语义不变量 / fable / vite build）照跑——
# 但那样浏览器里的验收一趟都没被验，别拿它当绿。
NO_BROWSER="${JANPO_NO_BROWSER:-0}"

# 下面两道都是**静态的**（不起服务器、不装依赖，毫秒级），因此摆在最前面：错了就立刻红。

# 票 106：同一个事实写在好几处，于是它们各自漂。这一道把两件钉死：
# 浏览器那条跑道的趟数只得从 `gates` 现算，强 AI 基线那份产物的路径每一处都指向同一个字面量。
echo "== 同一事实只许一处真源（趟数 / 产物路径）=="
bash "$(dirname "$0")/check-single-source.sh"

# 票 107（接 106 留下的那一项）：叙述行里那个勾**必须由数据决定**。
# 106 把 9 处无条件打印的 `✓` 改成由成败决定、并在 `browser-lane.mjs` 上留下三个助手，
# **但那条约定当时没有执行者**（它只活在文档里，判据 2）。这一道就是那个执行者。
echo "== 叙述行里的勾都由数据决定（写死的勾等于没有勾）=="
bash "$(dirname "$0")/check-narration.sh"

# 票 106 第三件：**Apache-2.0 §4 的义务不该挂在一条不常跑的路上**。
# 从前只有 Pages 那条路上的 `check-pages-dist.sh` 会因为少了许可件而红，
# 于是今天谁删掉 `web/public/third-party/` 都跑得过 `./scripts/ci.sh`。
# 两边调的是**同一份脚本**（那三份的名单只写在它那里一处），只是核的目录不同：
# 这里核**源**（`web/public/`），发布那条路上核**分发件**（`web/dist/`）。
echo "== 随站点上线的那几份许可（Apache-2.0 §4）=="
bash "$(dirname "$0")/check-third-party.sh" web/public

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
# **零网络请求**），逐手渲染三档再逐条断言，最后把每一条不变量与每一道对拍各自**按红一次**
# （几条几道由它自己数出来印，见 `web/scripts/verify-invariants.mjs`）
# （反向自证：一道从不失败的闸门等于没有闸门）。
echo "== prompt 的语义不变量（扫真实对局 + 逐条反向自证）=="
(cd web && node scripts/verify-invariants.mjs)

echo "== fable 编译（引擎 + Feliz 页面 → JS）=="
(cd web && pnpm run fable)

echo "== vite build =="
(cd web && node node_modules/vite/bin/vite.js build)

if [[ "$NO_BROWSER" == "1" ]]; then
  echo "== 浏览器那两条跑道（verify-browser.mjs 那张 gates 表 + 强 AI 基线自己那条）：按 JANPO_NO_BROWSER=1 跳过 =="
else
  # 一条命令跑完整条跑道（票 56）：一个 Chrome + 一台 preview + 一台 dev server，每趟各开自己的
  # page/context。**每趟仍然单独跑得起来**——它红的时候会把单跑那一条命令抄给你
  # （`node scripts/verify-board.mjs` 之类），调试时照抄就只重跑那一趟。
  # 跑几趟、各趟在验什么，看 `verify-browser.mjs` 里那张 `gates` 表（它自己会把趟数印出来）。
  echo "== 浏览器里那条跑道（共用一个浏览器进程与一台服务器）=="
  (cd web && node scripts/verify-browser.mjs)

  # 票 92 的验收：**强 AI 基线坐一席**（ADR-0006；票 102 把「它的来历」接在它尾上）。
  # 它跑几趟、每趟在断言什么，写在 `web/scripts/verify-baseline.mjs` 的文件头上
  # （票 106：这里不抄第二份）。
  #
  # **它自己起一条跑道**（不并进上面那条）：那一票与另外两票同时在跑，
  # `verify-browser.mjs` 是共用面——多起一个 Chrome 的代价实测 0.15–0.35 s，不值当为它抢文件。
  #
  # **常规趟里那份 6 MB 的产物不在场**（ADR-0006 边界 6：它不入版本控制），
  # 因此这里跑到的是**降级那一路**；真推理是本机演习那一档
  # （造一份放进 web/public/baseline/ 再 `node scripts/verify-baseline.mjs --asset`）。
  # CI 因此覆盖不到「它真出的那一手对不对」——写在报告 92 里。
  echo "== 强 AI 基线坐一席（懒加载 / 降级 / 它不会说话 / 与真人同桌 / 来历摆在人眼前）=="
  (cd web && node scripts/verify-baseline.mjs)
fi

echo "== JS 侧全绿 =="
