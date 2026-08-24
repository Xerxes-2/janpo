#!/usr/bin/env bash
# **随站点一起发出去的那几份许可**（Apache-2.0 §4(a)/(d)，ADR-0006 边界 4）。
#
# 两条路各核一次，跑的是**这同一份脚本**（票 106：那几份的名单只写在这里一处）：
#
#   ./scripts/check-third-party.sh web/public   # 核**源**：`scripts/ci-web.sh` 每趟都跑
#   ./scripts/check-third-party.sh web/dist     # 核**分发件**：`scripts/check-pages-dist.sh` 发布前跑
#
# **为什么源那一侧也要一道**（票 106 第三件）：从前只有 Pages 那条**不常跑**的路会因为
# 少了许可件而红，于是「今天谁删掉 `web/public/third-party/` 都跑得过 `./scripts/ci.sh`」。
# 一条法律义务不该挂在一条一天跑不了一次的路上——删一份下来 `./scripts/ci.sh` 必须当场红。
#
# 这一道**红得起来**：把 `web/public/third-party/NOTICE-akagi` 删掉跑一次 `./scripts/ci.sh`，
# 红的原文在 `run/reports/106-single-source-of-facts.md`。
set -euo pipefail
cd "$(dirname "$0")/.."

root="${1:?用法：check-third-party.sh <目录>（该目录下要有 third-party/）}"

# **这几份的名单只写在这一处。** 源与分发件核的是同一份名单：名单要是各写一份，
# 「本机绿而线上少一份」就又变成一件查不出来的事。
NEEDED=(LICENSE-akagi.txt NOTICE-akagi README.md)

failed=0
fail() {
  echo "许可闸门没过：$1" >&2
  failed=1
}

[[ -d "$root" ]] || {
  echo "没有 $root：核不了许可件" >&2
  exit 1
}

for name in "${NEEDED[@]}"; do
  file="$root/third-party/$name"
  [[ -s "$file" ]] || fail "$file 不在或是空的（Apache-2.0 §4，ADR-0006 边界 4）"
done

# **页面上那条链接指的那份文件必须真的在**：路径的真源是 `src/Janpo.Web/Credit.fs` 的
# `thirdPartyFile`（页脚与配桌页那一句读的都是它，票 102）。这里按它现取现核——
# 文件不在 = 页面上那条声明点进去是 404 = §4(d) 的义务没尽到。
linked="$(sed -n 's/.*let thirdPartyFile: string = "\(.*\)"/\1/p' src/Janpo.Web/Credit.fs)"
if [[ -z "$linked" ]]; then
  fail "从 src/Janpo.Web/Credit.fs 里读不出 thirdPartyFile：页面上那条链接指哪儿变得无从核对"
elif [[ ! -s "$root/$linked" ]]; then
  fail "页脚那条「第三方组件声明」指着 $linked，而 $root/$linked 不在或是空的（票 102）"
fi

[[ "$failed" -eq 0 ]] || exit 1
echo "许可件齐（$root/third-party/：${NEEDED[*]}；页面那条链接指的 $linked 也在）"
