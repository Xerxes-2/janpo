#!/usr/bin/env -S uv run --script
# /// script
# requires-python = ">=3.11"
# dependencies = ["mahjong==2.0.0"]
# ///
"""向听数 oracle —— **测试期的外部工具**，不是引擎或 CLI 的运行时依赖。

用现成的 Python 实现（PyPI 的 `mahjong`，MahjongRepository/mahjong）为 F# 引擎产出
对拍用例：随机手牌 + 该实现算出的向听数。引擎的对拍测试读的是这份产物，
`dotnet test` 不碰 Python，`scripts/ci.sh` 的引擎依赖白名单也不受影响。

从零复现（uv 会自己装好 Python 与钉死版本的 mahjong）：

    uv run scripts/oracle/shanten_oracle.py generate --count 4000 --seed 20260816

输出是 TSV，每行两列：

    <副露数> <mjai 记法...>\t<oracle 向听数>

第一列可以直接喂给 `janpo shanten --batch`，第二列是期望值。对拍见
`scripts/oracle/differential.sh`。

关于副露手：`mahjong` 库对不足 13 张的手牌按「缺的面子都是副露」处理
（文档原文 "A pair alone is a complete hand (remaining melds are implied open)"），
与本引擎 `HandShape.create nakiCount concealed` 的语义一致；且它只在 13 张以上
才考虑七对子与国士，与「副露过就不成立」也一致。
"""

from __future__ import annotations

import argparse
import random
import sys

from mahjong.shanten import Shanten

SUITS = "mps"
YAOCHUU = [0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33]


def kind_to_mjai(kind: int) -> str:
    """0-33 的牌种索引 → mjai 记法。红宝牌不参与形态判定，这里不产出。"""
    if kind < 27:
        return f"{kind % 9 + 1}{SUITS[kind // 9]}"
    return f"{kind - 27 + 1}z"


class Hand:
    """34 长度的牌种计数，最多 4 张/种。"""

    def __init__(self) -> None:
        self.counts = [0] * 34

    @property
    def total(self) -> int:
        return sum(self.counts)

    def take(self, kind: int) -> bool:
        if self.counts[kind] >= 4:
            return False
        self.counts[kind] += 1
        return True

    def drop_random(self, rng: random.Random) -> None:
        present = [k for k in range(34) if self.counts[k] > 0]
        if present:
            self.counts[rng.choice(present)] -= 1

    def fill_random(self, rng: random.Random, target: int, pool: list[int]) -> None:
        guard = 0
        while self.total < target and guard < 10000:
            guard += 1
            self.take(rng.choice(pool))

    def to_mjai(self) -> str:
        return " ".join(
            kind_to_mjai(kind) for kind in range(34) for _ in range(self.counts[kind])
        )


def pool_all() -> list[int]:
    return list(range(34))


def gen_uniform(rng: random.Random, size: int) -> Hand:
    """均匀随机手牌。多数是 3-4 向听，负责铺开状态空间。"""
    hand = Hand()
    hand.fill_random(rng, size, pool_all())
    return hand


def gen_suited(rng: random.Random, size: int) -> Hand:
    """牌集中在一两门花色里：搭子密集、分解方式多，最能打出分解搜索的错。"""
    suits = rng.sample(range(4), rng.randint(1, 2))
    pool = [k for k in range(34) if (k // 9 if k < 27 else 3) in suits]
    hand = Hand()
    hand.fill_random(rng, size, pool)
    return hand


def gen_structured(rng: random.Random, size: int) -> Hand:
    """先搭出若干面子与雀头再随机扰动：产出大量听牌与和了型附近的样本。"""
    hand = Hand()
    while hand.total + 3 <= size:
        if rng.random() < 0.5:
            suit = rng.randrange(3)
            start = suit * 9 + rng.randrange(7)
            if not all(hand.counts[start + offset] < 4 for offset in range(3)):
                break
            for offset in range(3):
                hand.take(start + offset)
        else:
            kind = rng.randrange(34)
            if hand.counts[kind] > 1:
                break
            for _ in range(3):
                hand.take(kind)

    if hand.total + 2 <= size:
        kind = rng.randrange(34)
        if hand.counts[kind] <= 2:
            hand.take(kind)
            hand.take(kind)

    for _ in range(rng.randint(0, 3)):
        if hand.total > 0:
            hand.drop_random(rng)

    hand.fill_random(rng, size, pool_all())
    return hand


def gen_pairs(rng: random.Random, size: int) -> Hand:
    """对子多的手牌：压七对子的公式与「一般型 / 七对子谁更小」的取最小值。"""
    hand = Hand()
    for kind in rng.sample(range(34), rng.randint(3, 7)):
        if hand.total + 2 <= size:
            hand.take(kind)
            hand.take(kind)
    hand.fill_random(rng, size, pool_all())
    return hand


def gen_yaochuu(rng: random.Random, size: int) -> Hand:
    """幺九牌为主：压国士的公式。"""
    hand = Hand()
    pool = YAOCHUU * 9 + pool_all()
    hand.fill_random(rng, size, pool)
    return hand


def gen_quads(rng: random.Random, size: int) -> Hand:
    """含若干「四张全在手里」的牌种：压「摸不到第五张」这一族修正。

    随机手牌几乎摸不到四枚一色，但它是向听数最容易算错的地方——四张字牌里的第四张
    是死张，四张数牌的第四张做不了单骑的雀头。"""
    hand = Hand()
    kinds = rng.sample(range(34), rng.randint(1, 2))
    for kind in kinds:
        if hand.total + 4 <= size:
            for _ in range(4):
                hand.take(kind)

    # 再塞一两组面子，让剩下的部分本身接近听牌。
    while hand.total + 3 <= size and rng.random() < 0.7:
        suit = rng.randrange(3)
        start = suit * 9 + rng.randrange(7)
        for offset in range(3):
            hand.take(start + offset)

    if hand.total + 2 <= size and rng.random() < 0.5:
        kind = rng.randrange(34)
        if hand.counts[kind] <= 2:
            hand.take(kind)
            hand.take(kind)

    hand.fill_random(rng, size, pool_all())
    return hand


GENERATORS = [gen_uniform, gen_suited, gen_structured, gen_pairs, gen_yaochuu, gen_quads]


def generate_case(rng: random.Random, naki: int | None = None) -> tuple[int, Hand]:
    if naki is None:
        # 门清占大头（真实对局绝大多数判定发生在门清或少量副露上），但四种副露数都要覆盖。
        naki = rng.choices([0, 1, 2, 3, 4], weights=[60, 15, 12, 8, 5])[0]
    size = 13 - 3 * naki + rng.randint(0, 1)
    generator = rng.choice(GENERATORS)
    return naki, generator(rng, size)


def command_generate(args: argparse.Namespace) -> int:
    rng = random.Random(args.seed)
    shanten = Shanten()
    for _ in range(args.count):
        naki, hand = generate_case(rng, args.naki)
        if hand.total not in (13 - 3 * naki, 14 - 3 * naki):
            raise SystemExit(f"生成器产出了张数不合法的手牌: {hand.to_mjai()}")
        expected = shanten.calculate_shanten(hand.counts)
        print(f"{naki} {hand.to_mjai()}\t{expected}")
    return 0


def command_check(args: argparse.Namespace) -> int:
    """从 stdin 读「<副露数> <记法...>\t<引擎算出的向听数>」，逐行与 oracle 比对。"""
    shanten = Shanten()
    offsets = {"m": 0, "p": 9, "s": 18, "z": 27}
    differences = 0
    total = 0
    for line in sys.stdin:
        line = line.rstrip("\n")
        if not line.strip():
            continue
        total += 1
        case, actual = line.split("\t")
        naki, _, notations = case.partition(" ")
        counts = [0] * 34
        for token in notations.split():
            counts[offsets[token[1]] + int(token[0]) - 1] += 1
        expected = shanten.calculate_shanten(counts)
        if expected != int(actual):
            differences += 1
            if differences <= args.show:
                print(
                    f"差异 naki={naki} {notations} oracle={expected} engine={actual}",
                    file=sys.stderr,
                )
    print(f"对拍 {total} 手，差异 {differences} 处")
    return 1 if differences else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)

    generate = commands.add_parser("generate", help="产出随机手牌与 oracle 向听数（TSV）")
    generate.add_argument("--count", type=int, default=4000)
    generate.add_argument("--seed", type=int, default=20260816)
    generate.add_argument("--naki", type=int, default=None, help="固定副露数，默认按权重随机")
    generate.set_defaults(func=command_generate)

    check = commands.add_parser("check", help="从 stdin 逐行比对引擎算出的向听数")
    check.add_argument("--show", type=int, default=20, help="最多打印几条差异")
    check.set_defaults(func=command_check)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
