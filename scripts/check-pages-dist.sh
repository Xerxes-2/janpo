#!/usr/bin/env bash
# 发出去之前核一遍 `web/dist`（票 101）。**它守的是分发件本身**，
# 因此只在发布那条路上跑（`.github/workflows/pages.yml`），本机跑法：
#
#   cd web && JANPO_BASE=/janpo/ pnpm run build && cd .. && ./scripts/check-pages-dist.sh
#
# 三件事：
#
#   1. **许可随产物上线**（ADR-0006 边界 4，Apache-2.0 §4(a)/(d)）：
#      `third-party/` 那三份必须在 `dist` 里，且都不是空的。
#      页脚那条链接指的就是其中的 `README.md`（`src/Janpo.Web/Footer.fs`），
#      文件不在 = 页面上那条声明点进去是 404 = 义务没尽到。
#   2. **产物在的时候，它得是一份像样的 wasm**：头四个字节 `\0asm`、体积够装下内嵌权重。
#      半截的 6 MB 比没有更坏：页面会当它拉到了，然后在 `instantiate` 那一步炸。
#   3. **产物不在的时候不算失败**（ADR-0006 边界 2）：站点照常发，那一席在浏览器里如实降级。
#      但这一步要**大声说出来**，免得没人注意到线上又回到了 404 那一天。
#
# 这一道**红得起来**：三份许可少一份、wasm 头四个字节不对、体积不到 5 MB，各红一次
# （反向自证的输出在 `run/reports/101-pages-baseline-asset.md`）。
set -euo pipefail
cd "$(dirname "$0")/.."

dist="web/dist"
failed=0

fail() {
  echo "发布件闸门没过：$1" >&2
  failed=1
}

[[ -d "$dist" ]] || {
  echo "没有 $dist：先 pnpm run build" >&2
  exit 1
}
[[ -s "$dist/index.html" ]] || fail "$dist/index.html 不在或是空的"

# ① 许可（三份都是页脚那条声明的落点，缺一份都算没尽到义务）
for name in LICENSE-akagi.txt NOTICE-akagi README.md; do
  file="$dist/third-party/$name"
  [[ -s "$file" ]] || fail "许可件 $file 不在或是空的（ADR-0006 边界 4）"
done

# ② / ③ 那 6 MB
wasm="$dist/baseline/janpo-baseline.wasm"
if [[ -f "$wasm" ]]; then
  bytes="$(stat -c %s "$wasm")"
  sound=1
  [[ "$(head -c 4 "$wasm" | xxd -p)" == "0061736d" ]] || {
    fail "$wasm 头四个字节不是 \\0asm，它不是一份 wasm"
    sound=0
  }
  ((bytes >= 5000000)) || {
    fail "$wasm 只有 $bytes 字节，装不下那约 4.8 MB 的内嵌权重"
    sound=0
  }
  # 「上线了」这句话只在它真站得住时才说——坏产物也报一句「随站点上线」，
  # 就是判据 1 那种「记录声称了一件事而那件事不成立」。
  ((sound == 1)) &&
    echo "强 AI 基线产物随站点上线：$bytes 字节，sha256 $(sha256sum "$wasm" | cut -d' ' -f1)"
else
  echo "注意：$wasm 不在，本次发布的站点上没有强 AI 基线那份产物。"
  echo "      页面会如实降级（那一席退回自带 bot，复盘里少一行），站点其余部分照常。"
  echo "      要它上线：让 scripts/build-baseline-wasm.sh 在 pnpm run build 之前跑成功。"
fi

[[ "$failed" -eq 0 ]] || exit 1
echo "发布件闸门通过：$dist"
