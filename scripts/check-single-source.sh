#!/usr/bin/env bash
# **同一个事实只许有一处真源**（票 106）。这一道盯两件已经漂过的事：
#
#   ① **浏览器那条跑道跑几趟**：真源是 `web/scripts/verify-browser.mjs` 里那张 `gates` 表，
#      趟数由它现算现印。别处（`scripts/ci-web.sh`、`browser-lane.mjs`、各 `verify-*.mjs`
#      的文件头）**一个写死的趟数都不许有**——票 92 / 90 / 89 各加一趟，每次都要两侧改同一行，
#      而本票接手时同一个数在仓库里有七种说法（十七 / 十八 / 十五 / 十四 / 十三 / 十一 / 十）。
#   ② **强 AI 基线那份产物的路径**：跨 TS / YAML / bash 写在好几处，收不成一处
#      （理由逐处写在下面），因此**把它们钉在一起**：每一处都得指向同一个字面量。
#   ③ 同一形状的第三处：**页脚那条第三方声明的落点**（`Credit.fs` 的 `thirdPartyFile`）。
#
# 这两件都是「记录声称了一件事，而那件事的执行体不存在」的老毛病（判据 2）：
# 从前每一处都写着「与 XXX 逐字相同」的叮嘱，而没有任何东西执行那句叮嘱。
#
# 这一道**红得起来**：往 `gates` 表里加一趟不该红（本票真加过一趟假闸门自证），
# 而在别处写一个「十九趟」、或者把任意一处路径改一个字，都当场红。
set -euo pipefail
cd "$(dirname "$0")/.."

failed=0
fail() {
  echo "  ✗ $1" >&2
  failed=1
}

# ── ① 趟数 ────────────────────────────────────────────────────────────────────
#
# 覆盖面**由真源自己算出来**：`verify-browser.mjs` import 了哪几份 `verify-*.mjs`，
# 哪几份就在这条跑道上（写死一份名单的话，这道闸门自己就成了第二处真源）。
# `verify-invariants.mjs` / `verify-baseline.mjs` 这些不在跑道上的不受这一条管——
# 它们文件头里的「几趟」说的是它们自己那条路，不是这条跑道。
# `ReviewCheck.fs` 是 `verify-review.mjs` 的 F# 那一半（它就在这条跑道上，注释里同样会
# 提「抛出去会把其余那几趟一起搞挂」）；票 106 被禁止碰 `Review*.fs`，那句「十七趟」因此漂到了
# 票 107 才改掉。把它一并盯上，否则下一句写死的趟数又只能靠自觉（判据 2）。
lane_files=(
  scripts/ci-web.sh
  web/scripts/verify-browser.mjs
  web/scripts/browser-lane.mjs
  src/Janpo.Web/ReviewCheck.fs
)
lane_fixed=${#lane_files[@]}
while read -r imported; do
  [[ -z "$imported" ]] && continue
  lane_files+=("web/scripts/$imported")
done < <(sed -n 's|^import .* from "\./\(verify-[a-z-]*\.mjs\)";$|\1|p' web/scripts/verify-browser.mjs)

# **名单少算一份就等于那一份没人扫，而它照旧报绿**（票 101 那次 `hashFiles` 空串的同一族）。
# 因此再数一遍：`verify-browser.mjs` 里有几行 import 指着 `./verify-*.mjs`，
# 上面就得算出几份——对不上说明那条 sed 的形状假设过期了。
imports="$(grep -cE 'from "\./verify-[a-z-]+\.mjs"' web/scripts/verify-browser.mjs)"
(((${#lane_files[@]} - lane_fixed) == imports)) ||
  fail "verify-browser.mjs 里有 $imports 行 import 指着 ./verify-*.mjs，这道闸门只算出 $((${#lane_files[@]} - lane_fixed)) 份：名单退化了，跑道上有文件没被扫"

# 「三趟」「十七趟」「18 趟」「第十四道」都算写死；汉字的「一趟」「两趟」「几趟」不算——
# 它们是量词不是计数（阿拉伯数字一律算，因为「1 趟」在这几份文件里从来没有过）。
hardcoded='(第)?([0-9]+ ?|[三四五六七八九十][一二三四五六七八九十]*)(趟|道)'
for file in "${lane_files[@]}"; do
  [[ -f "$file" ]] || {
    fail "$file 不在：跑道上那几份文件的名单是从 verify-browser.mjs 的 import 算出来的，它对不上了"
    continue
  }
  while IFS= read -r hit; do
    fail "$file:$hit —— 趟数写死了。真源只有 verify-browser.mjs 的 \`gates\` 表（票 106）"
  done < <(grep -nEo "$hardcoded" "$file" || true)
done

# 反过来也要成立：真源**真的在**现算，而不是又被人改回一个常量。
grep -q 'gates.length' web/scripts/verify-browser.mjs ||
  fail "verify-browser.mjs 里没有 \`gates.length\`：趟数又不是算出来的了"
grep -q 'results.length} 趟浏览器闸门' web/scripts/verify-browser.mjs ||
  fail "verify-browser.mjs 的收尾总述没有从 \`results.length\` 现算趟数"

# ── ② 强 AI 基线那份产物的路径 ────────────────────────────────────────────────
#
# **真源是浏览器自己那一份**（`web/src/baseline/wasm.ts` 的 `ASSET_FILE`）：
# 它决定页面到底去站点上的哪个地址取那 6 MB，别处都是围着它转的。
asset="$(sed -n 's/^const ASSET_FILE = "\(.*\)";$/\1/p' web/src/baseline/wasm.ts)"
if [[ -z "$asset" ]]; then
  fail "web/src/baseline/wasm.ts 里读不出 ASSET_FILE：这一件的真源没了"
else
  # 每一行「哪份文件 · 该出现的那一行 · 为什么它收不进真源」：
  #   baseline-asset.mjs   node 那几个脚本共用的一处（verify-baseline / verify-review /
  #                        probe/akagi-wasm/candidates-shape 都读它）。它读不到 TS 那份常量，
  #                        因为 `web/scripts/` 不在 tsconfig 的 include 里、也不过 vite。
  #   build-baseline-wasm  bash，**本机要能单跑**（`./scripts/build-baseline-wasm.sh`），
  #                        拿不到 workflow 的 env，也不该为一个常量去起一个 node。
  #   check-pages-dist     bash，同上；而且它核的是 `web/dist/` 下的那一份。
  #   pages.yml            YAML，**workflow 解析期**就要这个值（`env:` 段），
  #                        那时既没有 checkout 也没有 node。
  expect_one() {
    local file="$1" pattern="$2" why="$3"
    grep -qF -- "$pattern" "$file" ||
      fail "$file 里找不到「$pattern」：那份产物的路径漂了（$why）"
  }
  expect_one web/scripts/baseline-asset.mjs "export const ASSET = \"$asset\";" \
    "node 侧那几个脚本共用的一处"
  expect_one scripts/build-baseline-wasm.sh "out=\"web/public/$asset\"" \
    "造它的那一处；本机要能单跑，拿不到 workflow 的 env"
  expect_one scripts/check-pages-dist.sh "wasm=\"\$dist/$asset\"" \
    "核发布件的那一处"
  expect_one .github/workflows/pages.yml "BASELINE_WASM: web/public/$asset" \
    "Pages 那条流水线；YAML 在解析期就要这个值"
  expect_one .github/workflows/ci.yml "BASELINE_WASM: web/public/$asset" \
    "CI 里强 AI 基线那个 job（票 115）；YAML 同上"
  expect_one scripts/ci-baseline.sh "asset=\"web/public/$asset\"" \
    "强 AI 基线那一档的关卡；bash，本机要能单跑"

  # **第六处冒出来的时候要有人喊一声**（判据 4：覆盖不到的做成代码里的一个值）。
  # 下面这份名单里的每一份都已经被上面钉过、或者只是在文里提它一嘴（报告 / 说明 / 用例的措辞）；
  # 新出现一份没在名单上的，就是又多了一处会漂的路径。
  known=(
    .github/workflows/ci.yml
    .github/workflows/pages.yml
    scripts/build-baseline-wasm.sh
    scripts/check-pages-dist.sh
    scripts/ci-baseline.sh
    web/scripts/baseline-asset.mjs
    web/src/baseline/wasm.ts
    web/public/baseline/README.md
    web/public/third-party/README.md
    tests/Janpo.Web.Tests/BaselineCreditTests.fs
  )
  base="${asset##*/}"
  while IFS= read -r found; do
    [[ " ${known[*]} " == *" $found "* ]] ||
      fail "$found 里也写着「$base」：名单之外又冒出一处产物路径——把它钉进这份名单，或改成读 web/scripts/baseline-asset.mjs"
  done < <(grep -rlF --exclude-dir=node_modules --exclude-dir=dist --exclude-dir=.jj \
    --exclude-dir=.git --exclude-dir=generated --exclude-dir=.scratch --exclude='*.wasm' \
    -- "$base" . |
    sed 's|^\./||' | sort)

  # 那几处指的必须真是同一个字面量：站点那一侧与仓库那一侧只差一个 `web/public/` 前缀。
  grep -qF -- "\"$asset\"" web/src/baseline/wasm.ts ||
    fail "wasm.ts 的 ASSET_FILE 读出来是「$asset」，却在文件里对不上：这道闸门自己读错了"
fi

# ── ③′ 那份产物的**缓存键**：两条流水线共用一条缓存 ────────────────────────
#
# CI 那个 `baseline` job（票 115）与 Pages 那条流水线取的是**同一条缓存**：谁先造都算，
# 而那取决于两边算出同一个键。键里那一长串 `hashFiles` **在 YAML 里收不成一处**
# （workflow 解析期就要这个值，那时既没 checkout 也没 shell），于是**钉在一起**。
# 两边一旦漂了：不会有任何东西变红，只会您各造各的、各存各的，而那正是判据 2 那一族。
ci_key="$(sed -n "s/^ *digest='\${{ hashFiles(\(.*\)) }}'$/\1/p" .github/workflows/ci.yml)"
pages_key="$(sed -n "s/^ *digest='\${{ hashFiles(\(.*\)) }}'$/\1/p" .github/workflows/pages.yml)"
if [[ -z "$ci_key" || -z "$pages_key" ]]; then
  fail "两条流水线里至少一边读不出那一行 \`digest='\${{ hashFiles(…) }}'\`：形状变了，这一道自己失明了"
elif [[ "$ci_key" != "$pages_key" ]]; then
  fail "CI 与 Pages 算缓存键的那一串 hashFiles 不一样：两边会各存各的一份强 AI 基线产物（票 115）"
fi

# ── ③ 页脚那条声明的落点 ──────────────────────────────────────────────────────
#
# 真源是 `src/Janpo.Web/Credit.fs` 的 `thirdPartyFile`（页脚与配桌页那一句读的都是它，票 102）。
# 另外两处**同样收不成一处**（一处是 bash、一处是 node 脚本，都够不着 Fable 编出来的常量），
# 而它们的注释里各写着一句「与 `Credit.fs` 逐字相同」——**那句叮嘱从前没有执行体**。
# 第三处 `tests/Janpo.Web.Tests/BaselineCreditTests.fs` 本来就是钉它的用例，不必再钉一遍。
notice="$(sed -n 's/.*let thirdPartyFile: string = "\(.*\)"/\1/p' src/Janpo.Web/Credit.fs)"
if [[ -z "$notice" ]]; then
  fail "src/Janpo.Web/Credit.fs 里读不出 thirdPartyFile：页脚那条链接的真源没了"
else
  grep -qF -- "\"$notice\"" web/scripts/verify-baseline.mjs ||
    fail "web/scripts/verify-baseline.mjs 的 THIRD_PARTY 与 Credit.fs 的「$notice」对不上"
  grep -qF -- "\"$notice\"" scripts/check-pages-dist.sh ||
    fail "scripts/check-pages-dist.sh 那份「打包产物里要找得到」的名单与 Credit.fs 的「$notice」对不上"
fi

[[ "$failed" -eq 0 ]] || {
  echo "同一事实只许一处真源：没过（票 106）" >&2
  exit 1
}
echo "同一事实只许一处真源：通过（趟数只在 gates 表里；产物路径 $asset 与声明落点 $notice 每一处逐字相同）"
