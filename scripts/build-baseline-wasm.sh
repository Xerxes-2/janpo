#!/usr/bin/env bash
# 造**强 AI 基线**那份产物（约 6 MB 的自足 `.wasm`，ADR-0006），放进 `web/public/baseline/`，
# 让 `pnpm run build` 把它随 `web/public/` 一起拷进 `web/dist/`。
#
# **本机与 Pages 跑的是这同一份脚本**（`scripts/ci.sh` 那条规矩的同一个理由：
# 本地造出来的与线上发出去的必须是同一条链路，否则「我这儿是好的」就没有意义）。
#
# 那 6 MB **不入版本控制**（ADR-0006 边界 6），因此它只能在两处出现：
# 开发者本机（跑一次这个脚本）与发布流水线（`.github/workflows/pages.yml` 调这个脚本）。
#
# 用法：
#   ./scripts/build-baseline-wasm.sh            # 造一份放进 web/public/baseline/
#   CARGO_TARGET_DIR=/tmp/my-target ./scripts/build-baseline-wasm.sh
#
# 退出码非 0 = 没造出来。**调用方（pages.yml）不因此中止部署**：站点上没有这份产物时
# 页面会如实降级（ADR-0006 边界 2），把整个站点扣下来反而更坏。
set -euo pipefail
cd "$(dirname "$0")/.."

# target/ 有几百 MB，默认挪到 /tmp：它是可重建的中间物，不该躺在工作区里。
# Pages 上那台 runner 每跑一次都是新机器，放哪儿都一样。
target_dir="${CARGO_TARGET_DIR:-/tmp/janpo-baseline-target}"
out="web/public/baseline/janpo-baseline.wasm"

# 上游 `native_bot/`（含内嵌权重）。commit 钉死在脚本里；`.upstream/` 已存在时它什么都不做。
probe/akagi-wasm/fetch-upstream.sh

# `wasm32-unknown-unknown` 的 std。GitHub 的 ubuntu 镜像自带 rustup 与 stable
# （实测 2026-08-19：镜像里是 Rust/Cargo **1.97.1**，与本机同一版），只缺这一块，
# 装它约 22 MB。已经装过时这行是 0.02 s，因此不必加条件。
if command -v rustup >/dev/null 2>&1; then
  rustup target add wasm32-unknown-unknown
fi

(cd probe/akagi-wasm/crate &&
  CARGO_TARGET_DIR="$target_dir" cargo build --release --target wasm32-unknown-unknown)

mkdir -p "$(dirname "$out")"
install -m 644 "$target_dir/wasm32-unknown-unknown/release/akagi_wasm_probe.wasm" "$out"

# **自证一遍**：产物是不是真的一份 wasm、有没有那么大。
#
# 印 sha256 是为了让「线上那份到底是不是这一次发的」变成一句可核对的话：
# 部署之后 `curl -s <site>/baseline/janpo-baseline.wasm | sha256sum` 应当等于
# **那一次跑批总结里印的那个**。
# **不是拿本机造的那份比**：产物不是逐字可重现的，源码目录不同就差几十个字节
# （实测：同一份源码在两个目录下编出 6,039,832 与 6,039,960）。
bytes="$(stat -c %s "$out")"
if [[ "$(head -c 4 "$out" | xxd -p)" != "0061736d" ]]; then
  echo "造出来的东西不是 wasm（头四个字节不是 \\0asm）：$out" >&2
  exit 1
fi
if ((bytes < 5000000)); then
  echo "产物只有 $bytes 字节，太小了——内嵌权重（约 4.8 MB）多半没进去：$out" >&2
  exit 1
fi

echo "强 AI 基线产物：$out"
echo "  字节：$bytes"
echo "  sha256：$(sha256sum "$out" | cut -d' ' -f1)"
