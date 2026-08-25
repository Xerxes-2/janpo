#!/usr/bin/env bash
# **强 AI 基线那一档的关卡**：那 6 MB 的产物**在场**时才跑得起来的那几条（票 115）。
#
# 它与 `scripts/ci.sh` 是**两种形态，不是两级严格程度**，两条都得跑：
#
#   * `ci.sh`（主关卡）跑的是**产物不在场**那一路——ADR-0006 边界 6 定了那 6 MB 不入版本控制，
#     于是拉不动它的用户看到的就是这一路（页面明说原因、那一席退回自带 bot）。
#     它照旧不要求任何人手里有那份产物：没有 cargo 的机器上 `ci.sh` 一样全绿。
#   * 这一份跑的是**产物在场**那一路：复盘面板里强 AI 那一列的每一个数、
#     它真坐一席打完一局。**票 113 量出来：这一档不跑的话，`verify-review.mjs` 里
#     `strongLeg` 整段的断言在 CI 里一次也不开口**（复量：接上之后多跑 52 条，
#     加上 `verify-baseline.mjs` 的 `--asset` 那一趟共 60 条；数与量法在报告 115 里）。
#
# **两条形态各有各的断言，换不了**：产物一在场，「算不动就整行不出现」那几条反而不跑了
# （实测 4 条）——所以主关卡**不**改成有产物的形态，这一份另起一条路。
#
# 跑法（本机；第一次要造那 6 MB，约 45 s，之后它就躺在 web/public/baseline/ 里）：
#
#   ./scripts/ci-baseline.sh
#
# CI 上它是 `.github/workflows/ci.yml` 里 `baseline` 那个 job，**与主 job 并行**：
# 产物由 Actions 缓存喂（命中时取它约 2 s，未命中时在 runner 上造一遍实测 91 s），
# 而那一整趟仍旧短于主 job，**因此 CI 的总墙钟不变**。数在报告 115 里。
set -euo pipefail

cd "$(dirname "$0")/.."

asset="web/public/baseline/janpo-baseline.wasm"

# 造它的那一步要 cargo 与 wasm32 那块 std（**不在 nix dev shell 里**：ADR-0006 边界 6 那一节
# 与报告 101 量过——为一条只有这里用得上的工具链把每趟 CI 都拖慢，不划算）。
# CI 上这一步永远不会走到：workflow 里已经先取过缓存、没命中就在 nix 之外造好了。
if [[ ! -f "$asset" ]]; then
  echo "== 造那份产物（本机第一次要约 45 s；要 cargo + rustup 的 wasm32-unknown-unknown）=="
  ./scripts/build-baseline-wasm.sh
fi

echo "== 那份产物 =="
printf '  %s\n  字节：%s\n  sha256：%s\n' \
  "$asset" "$(stat -c %s "$asset")" "$(sha256sum "$asset" | cut -d' ' -f1)"

if ! command -v pnpm >/dev/null 2>&1; then
  echo "找不到 pnpm。nix dev shell 里自带；宿主机上装法见 docs/development.md。" >&2
  exit 1
fi

echo "== pnpm install =="
(cd web && pnpm install --frozen-lockfile)

echo "== 恢复 dotnet 本地工具（Fable）=="
dotnet tool restore

# `pnpm run build` = Fable + Vite。两条闸门各要一半：复盘那一条走 dev server（要 `src/generated`），
# 强 AI 基线那一条走 preview（要 `web/dist/`，那份产物也是从 `web/public/` 拷进去的）。
echo "== fable + vite build =="
(cd web && pnpm run build)

# **为什么要核一遍印出来的话**（判据 1/3：闸门要证明自己真开了口）：
# 这两条闸门**探得到产物在不在**，产物不在时它们会静静地改跑降级那一路并照旧退 0
# ——那正是这一票要治的病（CI 里 54 条断言常年零次，而没有任何东西变红）。
# `--asset` 已经把「产物不在场」挡在门口（当场报错），这里再核一句**真推理那几行确实印出来了**：
# 哪天有别的原因让它退回降级（例如浏览器里编不动那份 wasm），这一道也红得起来。
# 代价是这几句话改了措辞就要跟着改——**宁可这样红一次**，也好过静静地少跑 60 条。
run_gate() {
  local title="$1" log="$2"
  shift 2
  echo "== $title =="
  (cd web && "$@") | tee "$log"
}

require_line() {
  local log="$1" needle="$2"
  grep -qF -- "$needle" "$log" || {
    echo "强 AI 基线那一档没真跑起来：印出来的话里找不到「$needle」" >&2
    echo "（这一档要的是产物在场时那条真推理的路；它退回降级那一路的话，那几条断言一条都不开口）" >&2
    exit 1
  }
}

refuse_line() {
  local log="$1" needle="$2"
  grep -qF -- "$needle" "$log" && {
    echo "强 AI 基线那一档跑到了降级那一路：印出来的话里有「$needle」" >&2
    exit 1
  }
  return 0
}

review_log="$(mktemp)"
baseline_log="$(mktemp)"
trap 'rm -f "$review_log" "$baseline_log"' EXIT

run_gate "复盘里强 AI 那一列：逐手对照、上帝视角打不出来的那几手、逐位对拍" \
  "$review_log" node scripts/verify-review.mjs --asset
require_line "$review_log" "强 AI 逐手对照"
require_line "$review_log" "上帝视角会打 A、该席视角只能打 B"
require_line "$review_log" "逐位对拍"
refuse_line "$review_log" "这一趟站点上没有那份 6 MB 产物"

run_gate "强 AI 基线真坐一席打完一局" \
  "$baseline_log" node scripts/verify-baseline.mjs --asset
require_line "$baseline_log" "本机演习：它真坐一席打完一局"
refuse_line "$baseline_log" "这一趟站点上没有那份产物"

echo "== 强 AI 基线那一档全绿 =="
