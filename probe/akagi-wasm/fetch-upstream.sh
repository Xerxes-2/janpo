#!/usr/bin/env bash
# 把上游 Akagi v3 的 `native_bot/` 拉到 `.upstream/`（含 4.6 MB 内嵌权重），
# 顺带把 Apache-2.0 §4(d) 要求随分发件一起走的 `LICENSE.txt` 与 `NOTICE` 也拉下来。
#
# **产物不入库**（见 `.gitignore`）：仓库里留脚本与说明，随时重建。
# 上游 commit 钉死在下面那个 SHA——权重是 layout-specific 的（上游 README 明写：
# `adapt.rs` 修过 last_discard 的语义，配错版本的权重会静默地在约 2% 的鸣牌决策上分歧，
# 而且它自带的 parity fixture 抓不到，因为那组只核 candle-vs-PyTorch 的数值）。
set -euo pipefail
cd "$(dirname "$0")"

REPO="https://github.com/shinkuan/Akagi.git"
# 票 91 量的那一版。要升级就改这里，然后重跑 `scan-corpus.mjs` 与 `--bin parity` 复核。
COMMIT="394b329058e1b4d721dc40149658f9f9cfdd77ae"

if [[ -d .upstream/native_bot ]]; then
  echo ".upstream/native_bot 已存在，什么都不做（要重拉就先 rm -rf .upstream）"
  exit 0
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# sparse + blobless：整个 Akagi 仓库有前端与截图，我们只要 native_bot 与两份许可文件。
git clone --filter=blob:none --no-checkout "$REPO" "$tmp/akagi"
git -C "$tmp/akagi" sparse-checkout set --no-cone native_bot LICENSE.txt NOTICE
git -C "$tmp/akagi" checkout "$COMMIT"

mkdir -p .upstream
cp -r "$tmp/akagi/native_bot" .upstream/
cp "$tmp/akagi/LICENSE.txt" "$tmp/akagi/NOTICE" .upstream/

echo "拉好了：$(du -sh .upstream | cut -f1) @ ${COMMIT:0:12}"
echo "权重："
ls -l .upstream/native_bot/weights/*.safetensors
