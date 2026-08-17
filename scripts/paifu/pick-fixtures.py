#!/usr/bin/env python3
"""挑进 CI 的那一份牌谱固件：**按覆盖挑，不按场数挑**（票 57）。

    python3 scripts/paifu/pick-fixtures.py <scan.tsv> <features.json> [--seed <id 列表>] [--kyoku N]

`scan.tsv` 是 `scripts/fsi/paifu-scan.fsx` 的输出（牌谱事件流里的形态 + 引擎判出的流局形态 + 差异），
`features.json` 是 `scripts/paifu/corpus-features.py` 的输出（天凤自己写下的役 / 符 / 满贯档 / 流局形态）。

挑法：

1. **候选池**去掉带差异的场——那些是还没结的账（票 57 报告第三节），进 CI 就是红的；
   也去掉没有天凤 JSON 的场（役 / 符 / 番 / 点数比不了）。
2. `--seed` 里的场无条件先进（现有固件，它们的挑选理由在 `tests/fixtures/paifu/README.md`）。
3. 之后**贪心补覆盖**：每一轮挑「补上最多个还没覆盖到的形态」的那一场，形态权重见 `WANTED`。
4. 覆盖齐了再按 `--kyoku` 补局数：候选池按牌谱 id（即开局时刻）排序后**等间隔取**，
   补进来的那一批因此摊在整个月上，不偏向任何一天、任何一种打法。

输出到 stdout：挑中的 id（每行一个）；覆盖表与统计到 stderr。
"""

import collections
import json
import os
import sys

# 想要覆盖到的形态。前缀分三类：`form-` 流局形态（天凤写的字）、`yaku-` 役种、其余是事件流形态。
# **每一项至少要有一场带着它**；`REPEAT` 里那几项要两场以上（它们是已知易错的时机）。
WANTED_PREFIXES = ("form-", "yaku-", "fu-", "limit-", "han-")
WANTED_FLAT = [
    "ankan",
    "kakan",
    "daiminkan",
    "ankan-rinshan-hora",
    "kakan-rinshan-hora",
    "daiminkan-rinshan-hora",
    # 打牌之前连着两次杠（票 59 的第一处 bug）：四种组合的事件顺序不同，**四种都要有**。
    # 全量 129,179 局里各 19 / 6 / 3 / 1 局（两个暗杠打头的那 24 局本来就无差异，不单列）。
    "kakan-then-kakan",
    "kakan-then-ankan",
    "daiminkan-then-kakan",
    "daiminkan-then-ankan",
    "chankan",
    # 立直后的自家暗杠（票 63 E 族）：天凤只要求禁送り杠 + 听不变，
    # 面子构成会变的那一撑（旧③拦下的）必须在固件里有代表。
    "riichi-ankan",
    # 鸣完打完、下次摸牌前的荣和（票 63 F 族的形）：同巡振听由自家鸣牌解除的那四场在这一形里。
    "ron-after-own-naki",
    "double-ron",
    "triple-ron",
    "honba",
    "honba3",
    "kyotaku",
    "kyotaku2",
    "ura2",
    "bakaze-1z",
    "bakaze-2z",
    "bakaze-3z",
    "pao",
    "reason:sufonrenta",
    "reason:nagashimangan",
    "reason:suchareach",
    "reason:sukaikan",
    "reason:kyushukyuhai",
]
REPEAT = {
    # 票 65 G 族：包牌（天凤 JSON 自己写的 `包牌家 ≠ 和了家`）。全年带本场的包牌荣和
    # 只有 4 场，全部在 `--seed` 里无条件保留；另加 1 场包牌自摸（带本场）钉自摸那一支。
    # 这里的下限守着固件不被换稀。
    "pao": 5,
    # 票 63 E 族：旧固件里已有 18 次（全是旧③也放行的那种）+ F 种子场带 1 次，
    # 抵到 25 迫使贪心步从 2025 的 20 场 E 族里补进代表（它们才是旧③拦错的那种）。
    "riichi-ankan": 25,
    # 票 63 F 族：四场全部在 `--seed` 里无条件保留（全量语料仅 4 场，两种子形各 2），
    # 这里的下限守着固件不被换稀（旧固件 45 次 + 那四场各 1 次）。
    "ron-after-own-naki": 49,
    "ankan-rinshan-hora": 2,
    "kakan-rinshan-hora": 2,
    # 大明杠 → 岭上开花是票 59 第二处 bug 的现场（责任支付）：全量 24 局里多拿一局。
    "daiminkan-rinshan-hora": 3,
    "kakan-then-kakan": 2,
    "daiminkan-then-kakan": 2,
    "chankan": 2,
    "double-ron": 3,
    "form-四風連打": 2,
    "form-流し満貫": 2,
    "form-四家立直": 2,
    "form-四槓散了": 2,
    "form-九種九牌": 2,
    "form-全員聴牌": 2,
    "form-全員不聴": 2,
}


def read_scan(path):
    """事件流形态、引擎判出的流局形态、差异，按场汇总。"""
    tags = collections.defaultdict(collections.Counter)
    kyokus = collections.Counter()
    diffs = set()
    for line in open(path, encoding="utf-8"):
        parts = line.rstrip("\n").split("\t")
        if parts[0] == "K":
            kyokus[parts[1]] += 1
            if parts[3] != "-":
                for tag in parts[3].split(","):
                    tags[parts[1]][tag] += 1
        elif parts[0] == "C" and parts[8] not in ("hora", "fanpai"):
            tags[parts[1]]["reason:" + parts[8]] += 1
        elif parts[0] in ("D", "S"):
            diffs.add(parts[1])
    return tags, kyokus, diffs


def main():
    scan, features_path = sys.argv[1], sys.argv[2]
    argv = sys.argv[3:]
    seeds = []
    if "--seed" in argv:
        with open(argv[argv.index("--seed") + 1], encoding="utf-8") as handle:
            seeds = [line.strip() for line in handle if line.strip()]
    budget = int(argv[argv.index("--kyoku") + 1]) if "--kyoku" in argv else 600
    corpus = argv[argv.index("--corpus") + 1] if "--corpus" in argv else "data/paifu/full"

    tags, kyokus, diffs = read_scan(scan)
    with open(features_path, encoding="utf-8") as handle:
        features = json.load(handle)

    def merged(log_id):
        both = collections.Counter(tags.get(log_id, {}))
        both.update(features.get(log_id, {}).get("tags", {}))
        return both

    def size(log_id):
        return os.path.getsize(os.path.join(corpus, "mjai", log_id + ".mjson"))

    pool = [log_id for log_id in features if log_id not in diffs]
    wanted = set(WANTED_FLAT)
    for log_id in pool:
        for tag in merged(log_id):
            if tag.startswith(WANTED_PREFIXES):
                wanted.add(tag)

    picked = [log_id for log_id in seeds if log_id in features]
    have = collections.Counter()
    for log_id in picked:
        have.update(merged(log_id))

    def missing():
        return {tag for tag in wanted if have[tag] < REPEAT.get(tag, 1)}

    while True:
        gap = missing()
        if not gap:
            break
        scored = [
            (len(gap & set(merged(log_id))), -size(log_id), log_id) for log_id in pool if log_id not in picked
        ]
        # 池子挑空了（`--seed` 已含全部候选）：剩下的缺口语料里本就没有，照常走到
        # 末尾的「仍未覆盖」清单里报出来（票 65 碰到的边角：110 场全在 seed 里）。
        if not scored:
            break
        best = max(scored)
        if best[0] == 0:
            break
        picked.append(best[2])
        have.update(merged(best[2]))

    covered = len(picked)
    total = sum(kyokus[log_id] for log_id in picked)
    # 覆盖齐了再补局数：**等间隔**取，摊在整个月上（id 前缀就是开局时刻）。
    rest = sorted(log_id for log_id in pool if log_id not in picked)
    average = max(sum(kyokus[log_id] for log_id in rest) / max(len(rest), 1), 1)
    stride = max(int(len(rest) / max((budget - total) / average, 1)), 1)
    for log_id in rest[::stride]:
        if total >= budget:
            break
        picked.append(log_id)
        have.update(merged(log_id))
        total += kyokus[log_id]

    print("\n".join(picked))
    bytes_total = sum(size(log_id) for log_id in picked)
    print(
        f"挑中 {len(picked)} 场（覆盖用 {covered} 场，补量 {len(picked) - covered} 场）"
        f"／{total} 局／{bytes_total / 1e6:.2f} MB mjai",
        file=sys.stderr,
    )
    still = sorted(tag for tag in wanted if have[tag] < REPEAT.get(tag, 1))
    print(f"仍未覆盖（语料里也没有或全在差异场里）：{still}", file=sys.stderr)
    print("--- 覆盖表 ---", file=sys.stderr)
    for tag in sorted(wanted):
        print(f"{tag}\t{have[tag]}", file=sys.stderr)


if __name__ == "__main__":
    main()
