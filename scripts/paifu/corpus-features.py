#!/usr/bin/env python3
"""从天凤官方 JSON 里数出每一场的覆盖特征，供挑固件与写覆盖表用（票 57）。

    python3 scripts/paifu/corpus-features.py <语料目录> [> features.json]

只读 `<语料目录>/tenhou/*.json` 的**每局末尾那一格结果**（役 / 符 / 番 / 点数 / 流局形态），
即对拍时当 oracle 的那一份——覆盖表的期望值取自天凤自己写的字，不取自我们的实现。

产出 JSON：`{"<牌谱id>": {"tags": {"标记": 次数}, "kyokus": 局数}}`。
**标记名与 `scripts/fsi/paifu-scan.fsx` 那边严格不重叠**（重叠了两边会相加，覆盖表就在擒谎）：
那边数牌谱事件流里的形态（杠 / 双响 / 本场 / 供托 / 里宝牌…），**这边只数天凤写下的结果**：
`form-` 流局形态、`yaku-` 役种、`fu-` 符、`han-` 番、`limit-` 满贯档、`pao` 包牌。
"""

import collections
import glob
import json
import os
import re
import sys

SCORE = re.compile(r"^(?:(\d+)符)?(?:(\d+)飜)?(.*?)(\d+)点$")
YAKU = re.compile(r"^(.+?)\((\d+)飜\)$")
LIMITS = ["満貫", "跳満", "倍満", "三倍満", "役満", "数え役満"]


def hora_tags(detail, tags):
    """一次和了：`[和了家, 放铳家, 包家, "30符4飜7700点", "立直(1飜)", …]`。"""
    winner, liable = detail[0], detail[2]
    if liable != winner:
        tags["pao"] += 1
    matched = SCORE.match(detail[3])
    if matched:
        fu, han, limit, _ = matched.groups()
        if fu:
            tags[f"fu-{fu}"] += 1
        if han:
            tags[f"han-{min(int(han), 13)}"] += 1
        for name in LIMITS:
            if name and name in limit:
                tags[f"limit-{name}"] += 1
    for line in detail[4:]:
        matched = YAKU.match(line)
        tags["yaku-" + (matched.group(1) if matched else line.split("(")[0])] += 1


def game_features(path):
    with open(path, encoding="utf-8") as handle:
        game = json.load(handle)
    tags = collections.Counter()
    for kyoku in game["log"]:
        result = kyoku[-1]
        form = result[0]
        if form != "和了":
            tags["form-" + form] += 1
            continue
        for detail in result[2::2]:
            hora_tags(detail, tags)
    return {"tags": dict(tags), "kyokus": len(game["log"]), "rule": game["rule"]["disp"]}


def main():
    directory = sys.argv[1] if len(sys.argv) > 1 else "data/paifu/full"
    features = {}
    for path in sorted(glob.glob(os.path.join(directory, "tenhou", "*.json"))):
        log_id = os.path.basename(path)[: -len(".json")]
        try:
            features[log_id] = game_features(path)
        except (ValueError, KeyError, IndexError) as error:
            print(f"跳过 {log_id}：{error}", file=sys.stderr)
    print(json.dumps(features, ensure_ascii=False))
    print(f"{len(features)} 场", file=sys.stderr)


if __name__ == "__main__":
    main()
