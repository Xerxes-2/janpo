#!/usr/bin/env bash
# **叙述行里那个勾必须由数据决定**（票 106 立的约定，票 107 给它配的执行者）。
#
# 这个仓库栽过一次：`verify-review.mjs` **正确地 exit 1 并逐条印出两边的数**，而同一次跑批的
# 叙述行照样印着「页面上那几个概率与 wasm 直接印的严格相等 ✓」——那个勾是**写死在字符串里、
# 在收集完失败之后无条件打印的**。它不影响退出码，**但它让日志不能读**：判据 16 记着
# 「假红比真红危险」，而「印着 ✓ 的那一行其实刚失败」是同一族里更阴的一种——
# **没人会去查一条打了勾的行**。票 106 改好了 9 处，并在 `browser-lane.mjs` 上留下
# `tick` / `mark` / `markerSince` 三个助手，**但那条约定当时没有执行者**（106 报告 §④ 的留给人 1）。
#
# 这一道就是那个执行者。判据不是「不许出现 ✓」（那会逼着判决之后那六十几句总述都写成
# `mark(problems)` 这样的恒真式，而这个仓库刚栽过「属性测试是恒真式」那一课），而是
# **每一处字面量 `✓` 都得指得出「谁决定了它」**，两种合法出处：
#
#   ① **判决之后**：同一个顶层函数里，这一行之前已经 `return failure(…)` 过
#      （手验那几份没有跑道，它们的判决是 `process.exit(1)`）
#      ——那时清单是空的，这一句才印得出来；
#   ② **条件分支里**：它落在某个 `if (…) {` / `} else {` 的分支体内，那个条件就是它的判据。
#
# 两样都不是（飞行中无条件打印的那一种，也就是 106 那次的病灶）就红，并指路那三个助手。
# **`✓` 这个字符本身的真源是 `browser-lane.mjs` 的 `tick`**：那份文件里除了它那一行，
# 代码里不许再出现第二处（下面第二段盯着）。
#
# 反向自证：在 `verify-*.mjs` 里随便哪个函数体、判决之前写一句
# `console.log("我说它绿了 ✓")`，这一道当场红（报告 107 的红-4）。
set -euo pipefail
cd "$(dirname "$0")/.."

python3 - <<'PY' || exit 1
import pathlib
import re
import sys

bad = []
TICK = "\u2713"

# 一行是不是注释（注释里的勾不会印进日志，`browser-lane.mjs` 的那段说明写的就是它）。
def commented(line: str) -> bool:
    stripped = line.strip()
    return stripped.startswith("//") or stripped.startswith("*") or stripped.startswith("/*")

def indent(line: str) -> int:
    return len(line) - len(line.lstrip())

# 顶层函数的开头：判决（`return failure(`）只在它自己那个函数里算数。
TOP_LEVEL = re.compile(r"^(export\s+)?(async\s+)?function\b|^(export\s+)?const\s+\w+\s*=\s*(async\s*)?[(\w]")
# 条件分支的开块行。`else if` 与 `} else {` 都在内。
BRANCH = re.compile(r"^\s*(\}\s*)?(else\b|if\s*\()")
# 一句话可能跨好几行（`console.log(` + 模板串），因此往上找块头时要一层层跳过这些续行。
CONTINUED = re.compile(r"^\s*(console\.\w+\(|problems\.push\(|return\s|\w+\s*=|\w+\(|`|\"|'|\+)")

def encloser(lines: list[str], at: int) -> str | None:
    """这一行外面**最近的那个块头**（跳过跨行语句自己的那几层缩进）。"""
    depth = indent(lines[at])
    for index in range(at - 1, -1, -1):
        line = lines[index]
        if not line.strip() or commented(line):
            continue
        if indent(line) < depth:
            if BRANCH.match(line):
                return "branch"
            if line.rstrip().endswith(("{", "(", "[", "=>")) and not CONTINUED.match(line):
                return "block"
            # 还在同一句话里（`console.log(` 这类）：接着往外找。
            depth = indent(line)
    return None

lane = pathlib.Path("web/scripts/browser-lane.mjs")
lane_lines = lane.read_text().splitlines()
lane_hits = [
    (index + 1, line)
    for index, line in enumerate(lane_lines)
    if TICK in line and not commented(line)
]

# ── ① 勾这个字符的真源只有 `tick` 一处 ────────────────────────────────────────
if len(lane_hits) != 1 or "return ok ?" not in lane_hits[0][1]:
    where = "、".join(f"{lane}:{number}" for number, _ in lane_hits) or "一处都没有"
    bad.append(
        f"{lane} 里造得出勾的地方该只有 `tick` 那一行，实际是：{where}"
        "——`\u2713` 的真源只许有一处（票 106）"
    )

# ── ② 每一处字面量勾都得指得出谁决定了它 ──────────────────────────────────────
after_verdict = 0
in_branch = 0
scanned = 0

for path in sorted(pathlib.Path("web/scripts").glob("verify-*.mjs")):
    scanned += 1
    lines = path.read_text().splitlines()
    verdict = False

    for index, line in enumerate(lines):
        if TOP_LEVEL.match(line):
            verdict = False
        if "return failure(" in line or "process.exit(1)" in line:
            verdict = True
        if TICK not in line or commented(line):
            continue
        if verdict:
            after_verdict += 1
            continue
        if encloser(lines, index) == "branch":
            in_branch += 1
            continue
        bad.append(
            f"{path}:{index + 1} 写死了一个勾，而这一句既不在判决之后、也不在条件分支里："
            f"{line.strip()[:72]}\n"
            "      → 飞行中那一句用 `markerSince(problems)`（进这一项之前取，勾说的就是这一项自己），"
            "手里已经有布尔就用 `tick(ok)`，判完再印的总述用 `mark(problems)`（都在 browser-lane.mjs 上）"
        )

for each in bad:
    print(f"  \u2717 {each}")

if bad:
    sys.exit(1)

print(
    f"叙述行里的勾都由数据决定：通过（扫了 {scanned} 份 verify-*.mjs，"
    f"{after_verdict + in_branch} 处字面量勾各指得出谁决定了它——判决之后 {after_verdict} 处、"
    f"条件分支里 {in_branch} 处；勾的真源只有 browser-lane.mjs 的 tick）"
)
PY
