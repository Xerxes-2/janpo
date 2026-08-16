#!/usr/bin/env python3
"""调度器集成用：解「两侧各自追加/插入」型的 jj 冲突。

只处理一种形状——两侧都在同一处**新增**内容、互不删改（M1 跑批里 DECISIONS.md 的追加、
TablePage.fs 里各加一个 Msg 分支都属于这一类）。做法是把 diff 侧的结果与 literal 侧的
新增部分**都保留**。一旦发现任一侧有删除或改写（diff 里出现 `-` 行、或 base 不是 literal
的前缀），立刻报错退出，交给人看——**不许猜**。

用法：resolve-append-conflicts.py <文件> [<文件>…]
"""

import sys


def resolve(path: str) -> int:
    lines = open(path, encoding="utf-8").read().split("\n")
    out: list[str] = []
    i = 0
    n = 0
    while i < len(lines):
        if not lines[i].startswith("<<<<<<< conflict"):
            out.append(lines[i])
            i += 1
            continue
        end = next(j for j in range(i, len(lines)) if lines[j].startswith(">>>>>>> conflict"))
        body = lines[i + 1 : end]
        pct = next(k for k, l in enumerate(body) if l.startswith("%%%%%%% diff from"))
        plus = next(k for k, l in enumerate(body) if l.startswith("+++++++ "))
        if pct < plus:
            diff, literal = body[pct + 2 : plus], body[plus + 1 :]
        else:
            literal, diff = body[plus + 1 : pct], body[pct + 2 :]
        removed = [l for l in diff if l.startswith("-")]
        if removed:
            raise SystemExit(f"{path}: diff 侧有删除行，不是纯追加冲突，交给人：{removed[:3]}")
        base = [l[1:] for l in diff if l[:1] == " "]
        after_diff = [l[1:] for l in diff if l[:1] in (" ", "+")]
        if literal[: len(base)] != base:
            raise SystemExit(f"{path}: literal 侧不以 base 开头，两侧改了同一处，交给人")
        out.extend(after_diff)
        out.extend(literal[len(base) :])
        i = end + 1
        n += 1
    if n:
        open(path, "w", encoding="utf-8").write("\n".join(out))
    return n


if __name__ == "__main__":
    for p in sys.argv[1:]:
        print(f"{p}: 解了 {resolve(p)} 处纯追加冲突")
