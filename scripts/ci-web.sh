#!/usr/bin/env bash
# JS 侧的关卡（M1 起）。`scripts/ci.sh` 会调它，也可以单独跑。
#
# 五道：TS/JS 的格式与 lint（Biome）→ Fable 编译 → Vite 产物 → **浏览器内跑引擎并与 dotnet 侧对拍**
# → **浏览器内跑黄金用例**。
#
# 第四道是 19 票的验收：同一种子在浏览器里跑出的终局点数与顺位，必须与
# `janpo kyoku` / `janpo game` 逐项相同（跑的是 Vite 打包后的产物）。
# 第五道是 21 票的验收：`tests/fixtures/golden/dual-target.json` 里的每条用例，
# 在浏览器里跑出的每个字段的每一行都要与文件一致（跑的是 Fable 的输出本身）。
# 同一份用例文件在 dotnet 侧由 `dotnet test` 的 `GoldenSuiteTests` 跑——**两侧读同一份数据**。
set -euo pipefail

cd "$(dirname "$0")/.."

if ! command -v pnpm >/dev/null 2>&1; then
  echo "找不到 pnpm。nix dev shell 里自带；宿主机上装法见 README。" >&2
  exit 1
fi

# 无头验收要一个 Chrome/Chromium。跑批机器上有 /usr/bin/google-chrome-stable；
# 别处用 JANPO_CHROME 指过去，或 `pnpm dlx playwright install chromium`。
# 实在没有浏览器的环境（例：最小容器）可以 JANPO_NO_BROWSER=1 跳过**后两道**，
# 前三道照跑——但那样 19 与 21 票的 JS 侧验收就没被验，别拿它当绿。
NO_BROWSER="${JANPO_NO_BROWSER:-0}"

echo "== pnpm install =="
(cd web && pnpm install --frozen-lockfile)

# --error-on-warnings：Biome 默认只拿 error 当失败，警告（包括它自己的配置弃用提示）会静静滑过去。
echo "== biome ci（TS/JS 的格式与 lint）=="
(cd web && node node_modules/@biomejs/biome/bin/biome ci --error-on-warnings .)

echo "== fable 编译（引擎 + Feliz 页面 → JS）=="
(cd web && pnpm run fable)

echo "== vite build =="
(cd web && node node_modules/vite/bin/vite.js build)

if [[ "$NO_BROWSER" == "1" ]]; then
  echo "== 浏览器内的两道（曳光弹对拍 / 黄金用例）：按 JANPO_NO_BROWSER=1 跳过 =="
else
  echo "== 浏览器内曳光弹对拍（与 dotnet 侧逐项对照）=="
  (cd web && node scripts/verify-tracer.mjs)

  echo "== 浏览器内黄金用例（与 tests/fixtures/golden/ 逐字段逐行对照）=="
  (cd web && node scripts/verify-golden.mjs)
fi

echo "== JS 侧全绿 =="
