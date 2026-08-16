#!/usr/bin/env python3
"""票 28 的核对脚本：**决策包用例只换了对照粒度，值一个都没变。**

用法（重跑前先把旧那份用例文件留一份）：

    python3 .scratch/llm-riichi-arena/run/reports/28-verify-golden-split.py \
        <旧的 dual-target.json> <新的 dual-target.json>

它做两件事：
1. 非 decide 的 37 条用例的 `expect` 必须**逐字节相同**；
2. decide 的 3 条：把旧那条 `package` 长行 parse 出来、按新那套规则摊平，
   得到的「路径 → 逐行的值」必须与新文件里的字段**逐条同序相同**。

摊平的规则要与 `src/Janpo.Golden/GoldenJson.fs` 一致：一个叶子一个字段；
全是标量的数组是一个字段的多行；空表与空对象是一个零行的字段；
字符串原样、数字与 true/false/null 按 JSON 字面量。
"""

import json
import sys


def render(value):
    if value is None:
        return "null"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if isinstance(value, str):
        return value
    return json.dumps(value)


def flatten(value, path, out):
    if isinstance(value, dict):
        if not value:
            out.append((".".join(path), []))
            return
        for key, item in value.items():
            flatten(item, path + [key], out)
    elif isinstance(value, list):
        if not value:
            out.append((".".join(path), []))
        elif all(not isinstance(item, (dict, list)) for item in value):
            out.append((".".join(path), [render(item) for item in value]))
        else:
            for index, item in enumerate(value):
                flatten(item, path + [str(index)], out)
    else:
        out.append((".".join(path), [render(value)]))


def main(old_path, new_path):
    old = {case["id"]: case for case in json.load(open(old_path))["cases"]}
    new = {case["id"]: case for case in json.load(open(new_path))["cases"]}
    problems = []

    if list(old) != list(new):
        problems.append("用例集合变了")

    for case_id in old:
        before, after = old[case_id], new[case_id]
        if (before["run"], before["ruleset"], before["note"]) != (
            after["run"],
            after["ruleset"],
            after["note"],
        ):
            problems.append(f"{case_id}: 输入或说明变了")
        if before["run"]["kind"] != "decide":
            if before["expect"] != after["expect"]:
                problems.append(f"{case_id}: 非 decide 用例的期望变了")
            continue
        if before["expect"]["asked"] != after["expect"]["asked"]:
            problems.append(f"{case_id}: asked 变了")
        expected = []
        flatten(json.loads(before["expect"]["package"][0]), ["package"], expected)
        actual = [(k, v) for k, v in after["expect"].items() if k != "asked"]
        if expected == actual:
            print(f"{case_id}: {len(actual)} 个字段，与旧那一行摊平后逐字段逐行相同 ✓")
            continue
        by_name = dict(expected)
        for name, lines in actual:
            if name not in by_name:
                problems.append(f"{case_id}: 多了字段 {name}")
            elif by_name[name] != lines:
                problems.append(f"{case_id}: {name} 值不同 {by_name[name]} vs {lines}")
        for name, _ in expected:
            if name not in dict(actual):
                problems.append(f"{case_id}: 少了字段 {name}")
        if [n for n, _ in expected] != [n for n, _ in actual]:
            problems.append(f"{case_id}: 字段顺序不同")

    if problems:
        print("问题：")
        for problem in problems:
            print("  " + problem)
        return 1
    print("非 decide 的用例逐字节相同；decide 的三条只换了粒度，值没变 ✓")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2]))
