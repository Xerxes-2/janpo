#!/usr/bin/env python3
"""汇总 `paifu-scan-zip.fsx` 的分片输出，并把差异场按签名归类（票 62）。

    python3 scripts/paifu/summarize-scan.py <扫描输出目录>

做三件事：

1. 合并各分片最后一条 `CK` 行（场数、局数、和了数……与流局形态/差异类/役种计数表）；
2. 把 `diffs-*.tsv` 逐行去重（断点续跑会把 ≤500 场重做一遍，产物是完全相同的重复行），
   按场分组、按**差异签名**归类。签名对应票 57 已定性的三类，其余一律「未归类」：

       kan-dora-order   相邻两行 dora ↔ 他行互换（票 57 第三节 A：连杠时明杠宝牌的翻牌时机）
       hora-deltas      同一家和了、deltas 不同（票 57 第三节 B：大明杠→岭上开花的责任支付）
       sancha-ryukyoku  重放中止于「宣言不了九种九牌」（票 57 第三节 D：上游把三家和了删成裸 ryukyoku）

3. 把「未归类」的场 id 写到 `<目录>/suspects.txt`——**oracle 只对这份名单取**（先筛后取），
   全部归类结果写到 `<目录>/classified.tsv`（`类<tab>场id`）供复查。

归类只是初筛：每一类都要再抽样人查（见 62 号票报告），新类必须逐个查。
"""

import collections
import os
import re
import sys

EVENT_DIFF = re.compile(r"第 (\d+) 条：牌谱 \[(.*)\]，引擎 \[(.*)\]")


def parse_counts(text):
    if text == "-":
        return {}
    counts = {}
    for pair in text.split(","):
        key, _, value = pair.rpartition(":")
        counts[key] = int(value)
    return counts


def merge_progress(directory):
    total = collections.Counter()
    maps = {"reasons": collections.Counter(), "kinds": collections.Counter(), "yaku": collections.Counter()}
    unfinished = []
    for name in sorted(os.listdir(directory)):
        if not name.startswith("progress-"):
            continue
        with open(os.path.join(directory, name), encoding="utf-8") as handle:
            lines = handle.read().splitlines()
        if not any(line.startswith("DONE\t") for line in lines):
            unfinished.append(name)
        last = [line for line in lines if line.startswith("CK\t")][-1].split("\t")
        for key, index in [
            ("games", 1), ("kyokus", 3), ("horas", 4), ("settled_seats", 5), ("score_checks", 6),
            ("diff_kyokus", 7), ("diff_games", 8), ("diffs", 9), ("skips", 10), ("elapsed_s", 11),
        ]:
            total[key] += int(float(last[index]))
        for key, index in [("reasons", 12), ("kinds", 13), ("yaku", 14)]:
            maps[key].update(parse_counts(last[index]))
    return total, maps, unfinished


def load_diffs(directory):
    lines = set()
    for name in sorted(os.listdir(directory)):
        if name.startswith("diffs-"):
            with open(os.path.join(directory, name), encoding="utf-8") as handle:
                lines.update(line for line in handle.read().splitlines() if line)
    games = collections.defaultdict(list)
    for line in sorted(lines):
        fields = line.split("\t")
        games[fields[1]].append(fields)
    return games


def classify(rows):
    """一场的全部 D/S 行 → 签名。规则要窄：对不上任何一类就是「未归类」，宁可多筛去取 oracle。"""
    features = set()
    events = []
    for row in rows:
        if row[0] == "S":
            features.add("skip")
        elif row[3] == "重放":
            features.add("sancha" if "宣言不了九种九牌" in row[4] else "replay-other")
        elif row[3] == "终局点数":
            features.add("carry")
        elif row[3] == "事件流":
            matched = EVENT_DIFF.search(row[4])
            if not matched:
                features.add("event-other")
                continue
            index, paifu, engine = int(matched.group(1)), matched.group(2), matched.group(3)
            events.append((index, paifu, engine))
        else:
            features.add("other")

    for index, paifu, engine in events:
        dora_swap = any(
            other_index in (index - 1, index + 1) and paifu == other_engine and engine == other_paifu
            for other_index, other_paifu, other_engine in events
        ) and (paifu.startswith("dora ") or engine.startswith("dora "))
        hora_deltas = (
            paifu.startswith("hora ")
            and engine.startswith("hora ")
            and paifu.split(" ")[1:3] == engine.split(" ")[1:3]
            and paifu != engine
        )
        # 只有一侧是 dora 行：欠账插补/晚翻把后续行错一位的形状（票 57 第三节 A 的「杠→暗杠」支）。
        dora_shift = paifu.startswith("dora ") != engine.startswith("dora ")
        if dora_swap:
            features.add("dora-swap")
        elif hora_deltas:
            features.add("hora-deltas")
        elif dora_shift:
            features.add("dora-shift")
        else:
            features.add("event-other")

    if features == {"sancha"}:
        return "sancha-ryukyoku"
    if features <= {"dora-swap", "dora-shift"} and "dora-swap" in features:
        return "kan-dora-order"
    if features <= {"hora-deltas", "carry", "dora-swap"} and "hora-deltas" in features:
        return "hora-deltas"
    return "unclassified"


def main():
    directory = sys.argv[1]
    total, maps, unfinished = merge_progress(directory)
    if unfinished:
        print(f"注意：这些分片还没跑完（没有 DONE）：{', '.join(unfinished)}\n")

    print(
        f"场 {total['games']:,} / 局 {total['kyokus']:,}，和了 {total['horas']:,}，"
        f"清算座位 {total['settled_seats']:,}，终局点数对拍 {total['score_checks']:,}"
    )
    print(
        f"差异 {total['diffs']:,} 处 / {total['diff_kyokus']:,} 局 / {total['diff_games']:,} 场，"
        f"跳过 {total['skips']:,}；累计 CPU {total['elapsed_s'] / 3600:.1f} 核时"
    )
    print(f"\n流局形态：{dict(maps['reasons'].most_common())}")
    print(f"差异类：{dict(maps['kinds'].most_common())}")
    seen = maps["yaku"]
    print(f"\n引擎读出的役种（{len(seen)} 种）：{dict(seen.most_common())}")

    games = load_diffs(directory)
    classes = collections.defaultdict(list)
    for game, rows in sorted(games.items()):
        classes[classify(rows)].append(game)

    print("\n差异场归类（签名初筛，每类都要再抽样人查）：")
    for name, ids in sorted(classes.items(), key=lambda item: -len(item[1])):
        exemplars = " ".join(ids[:3])
        print(f"  {name:20} {len(ids):6} 场   例：{exemplars}")

    with open(os.path.join(directory, "classified.tsv"), "w", encoding="utf-8") as handle:
        for name, ids in sorted(classes.items()):
            for game in ids:
                handle.write(f"{name}\t{game}\n")
    with open(os.path.join(directory, "suspects.txt"), "w", encoding="utf-8") as handle:
        for game in classes.get("unclassified", []):
            handle.write(game + "\n")
    print(
        f"\n已写 {os.path.join(directory, 'classified.tsv')}；"
        f"未归类 {len(classes.get('unclassified', []))} 场 → suspects.txt（oracle 只对这份名单取）"
    )


if __name__ == "__main__":
    main()
