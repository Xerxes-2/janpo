#!/usr/bin/env bash
# JS 侧的关卡（M1 起）。`scripts/ci.sh` 会调它，也可以单独跑。
#
# 九道：TS/JS 的格式与 lint（Biome）→ **TS 类型闸门**（tsc --noEmit）→ **Agent 层的用例**
# （node --test，回放录制的响应）→ Fable 编译 → Vite 产物 → **浏览器内跑引擎并与 dotnet 侧对拍**
# → **浏览器内跑黄金用例** → **浏览器内导出牌谱并回放** → **那道 key 闸门的反向自证**。
#
# 第四道是 19 票的验收：同一种子在浏览器里跑出的终局点数与顺位，必须与
# `janpo kyoku` / `janpo game` 逐项相同（跑的是 Vite 打包后的产物）。
# 第五道是 21 票的验收：`tests/fixtures/golden/dual-target.json` 里的每条用例，
# 在浏览器里跑出的每个字段的每一行都要与文件一致（跑的是 Fable 的输出本身）。
# 同一份用例文件在 dotnet 侧由 `dotnet test` 的 `GoldenSuiteTests` 跑——**两侧读同一份数据**。
# 第八道是 26 票的验收：浏览器里点一下「导出牌谱」真下下来一个文件，那份字节交回引擎
# fold 一遍，事件流逐条相同、点数与牌桌上的一致（ADR-0002 的回放）；它同时带着票 34 那条
# 「导出物里没有 key」。第九道是票 34 的反向自证：**一道从不失败的闸门等于没有闸门**，
# 因此每次 CI 都把那条断言按红一次（拌了 key 的导出物），并核实它红得就是因为那把 key。
set -euo pipefail

cd "$(dirname "$0")/.."

if ! command -v pnpm >/dev/null 2>&1; then
  echo "找不到 pnpm。nix dev shell 里自带；宿主机上装法见 docs/development.md。" >&2
  exit 1
fi

# 无头验收要一个 Chrome/Chromium。跑批机器上有 /usr/bin/google-chrome-stable；
# 别处用 JANPO_CHROME 指过去，或 `pnpm dlx playwright install chromium`。
# 实在没有浏览器的环境（例：最小容器）可以 JANPO_NO_BROWSER=1 跳过**后三道**，
# 前四道照跑——但那样 19、21 与 26 票的 JS 侧验收就没被验，别拿它当绿。
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
echo "== node --test（Agent 层的确定性用例）=="
(cd web && pnpm run test)

echo "== fable 编译（引擎 + Feliz 页面 → JS）=="
(cd web && pnpm run fable)

echo "== vite build =="
(cd web && node node_modules/vite/bin/vite.js build)

if [[ "$NO_BROWSER" == "1" ]]; then
  echo "== 浏览器内的三道（曳光弹对拍 / 黄金用例 / 牌谱导出）：按 JANPO_NO_BROWSER=1 跳过 =="
else
  echo "== 浏览器内曳光弹对拍（与 dotnet 侧逐项对照）=="
  (cd web && node scripts/verify-tracer.mjs)

  echo "== 浏览器内黄金用例（与 tests/fixtures/golden/ 逐字段逐行对照）=="
  (cd web && node scripts/verify-golden.mjs)

  # 票 26 的验收：点一下真的下载得到一份牌谱，而那份字节回放出逐条相同的事件流。
  # 四家随机选手，**一个网络请求都不发**（M1 增量约束 6）；
  # 带真模型的那一档是 `--llm`，人工跑。
  # 票 34 把「导出物里没有 key」一并放进这一道：假 key 灌进 localStorage、模型坐席不给。
  echo "== 浏览器内牌谱导出（下载事件 + 把下下来的字节 fold 回去）=="
  (cd web && node scripts/verify-export.mjs --turns 40)

  # 票 34 的反向自证：拿同一份导出物拌一把 key 进去，上面那条断言**必须**当场红。
  # 没这一道的话，「代码里有那行断言」与「那行断言真的拦得住东西」就又分不开了
  # （立这张票的起因就是上一次只核到了前者）。手数压到 12：它只需要导出得成。
  echo "== 反向自证：拌了 key 的导出物必须让那道闸门变红 =="
  poison_log="$(mktemp)"
  trap 'rm -f "$poison_log"' EXIT
  if (cd web && node scripts/verify-export.mjs --turns 12 --poison) >"$poison_log" 2>&1; then
    cat "$poison_log"
    echo "反向自证没过：拌了 key 的导出物竟然过了闸门——那条断言等于没有。" >&2
    exit 1
  fi
  if ! grep -q "里出现了 API key" "$poison_log"; then
    cat "$poison_log"
    echo "反向自证没过：闸门是红了，但不是因为那把 key（红的原因见上）。" >&2
    exit 1
  fi
  echo "拌了 key 的导出物被闸门当场逮住：$(grep "里出现了 API key" "$poison_log")"
fi

echo "== JS 侧全绿 =="
