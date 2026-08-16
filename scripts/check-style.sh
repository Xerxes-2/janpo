#!/usr/bin/env bash
# 项目特有的风格闸门。规则与理由见 docs/agents/fsharp-style.md。
#
# 只放**零假阳性**的机械检查。凡是需要判断的（嵌套深度、该不该管道化）一律不进这里——
# 那些交给 code-review 的 Standards 轴看文档。
#
# 每条的基线都是当前值，多数是 0：这些闸门的作用是**锁住零**，不是清理债务。
set -euo pipefail
cd "$(dirname "$0")/.."

fail=0
report() { echo "  ✗ $1"; fail=1; }

python3 - <<'PY' || fail=1
import pathlib, re, sys
bad = []

DIRS = ("src/Janpo.Engine", "src/Janpo.Cli", "tests/Janpo.Engine.Tests", "scripts/fsi")
def files():
    for d in DIRS:
        for f in sorted(pathlib.Path(d).glob("*.fs*")):
            yield f

# ── 规则 8：F# 函数应用不许写 `f (x)`（单原子实参，括号纯冗余）
#    注意只查**带空格**的形式。`Assert.Empty(x)` 这类 .NET 方法调用保留括号是惯例，不查。
spaced = re.compile(r"(?<![.\w])((?:[A-Z][\w']*\.)*[A-Za-z_][\w']*) \(\s*((?:[A-Z][\w']*\.)*[A-Za-z_][\w']*|\d+)\s*\)")
KW = {"if","then","else","match","when","fun","let","do","yield","return","new","typeof","nameof",
      "sizeof","lazy","assert","raise","failwith","failwithf","printfn","printf","sprintf","eprintfn",
      "not","string","int","float","byte","char","bool","uint32","Some","None","Ok","Error","ignore"}
# ── 规则 2：`fun x -> Encode.foo (bar x)` 该写成 `bar >> Encode.foo`（窄形状，零假阳性）
lam = re.compile(r"fun (\w+) -> (Encode|Decode)\.\w+ \(\s*[\w.]+ +\1\s*\)")

for f in files():
    for i, line in enumerate(f.read_text().splitlines(), 1):
        s = line.strip()
        if s.startswith("//"):
            continue
        for m in spaced.finditer(line):
            callee = m.group(1)
            if callee in KW:
                continue
            if "." in callee and not callee[0].isupper():   # 实例成员调用，括号有语义
                continue
            bad.append(f"{f}:{i} 多余括号「{m.group(0)}」→ 去掉括号（规则 8）")
        if lam.search(s):
            bad.append(f"{f}:{i} lambda 包了一层调用 → 用 `>>` 组合（规则 2）")

# 规则 7（重属性模块加 Parallelism）**不在这里检查**：它的判据是「这个模块实测慢不慢」，
# 而静态检查数不出运行时间。实测过——给非关键路径的 8 个模块加 Parallelism，CI 35s→37s，
# 无收益反略慢。按属性条数当阈值是错的，留在文档里当指导。

# ── 规则 5：引擎的 `let mutable` 预算。现有 7 处全部有理由（Shanten 的 34 长计数数组 6 处、
#    Rng 的原地洗牌 1 处）。要新增就得先改这个数字，顺带被迫在 DECISIONS.md 里留一条。
#    只查引擎：CLI 的 stdin 读行循环是驱动层的标准写法，不在此约束内。
BUDGET = 7
n = sum(l.count("let mutable") for f in pathlib.Path("src/Janpo.Engine").rglob("*.fs") for l in f.read_text().splitlines())
if n > BUDGET:
    bad.append(f"引擎里 let mutable {n} 处 > 预算 {BUDGET}（规则 5）：确有必要就改预算并在 DECISIONS.md 留一条")

for b in bad:
    print(f"  ✗ {b}")
sys.exit(1 if bad else 0)
PY

if [ "$fail" -ne 0 ]; then
    echo "风格检查失败。规则与理由见 docs/agents/fsharp-style.md"
    exit 1
fi
echo "风格检查通过"
