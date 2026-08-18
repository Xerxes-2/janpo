/**
 * 语义不变量的**反向自证**（票 41）。
 *
 * **一道从不失败的闸门等于没有闸门**（票 34 立的规矩，ci-web.sh 里那道 poison 就是它）。
 * `invariants.ts` 里每一条都配一个把好句子改坏的**变异**：改完那一条必须当场红，
 * 而且报错点得出是哪条不变量、哪一手、哪句话。
 *
 * 变异一律作用在**渲出来的字节**上，不改渲染器：闸门要认的就是「这份文本里的话成不成立」，
 * 拿真 prompt 改一处出来的反例，比手捏一份假 prompt 更像真会发生的回归
 * （票 40 那个错落到文本上就是 `PROOFS` 里的 `nakiFrame` 那一条，一字不差）。
 *
 * 两处在用：`invariants.test.ts`（committed 固件上）与 `scripts/verify-invariants.mjs`
 * （扫出来的真语料上）。**共用一份，两处说法因此一致。**
 */

import { DEFAULT_TEMPLATE } from "../../src/agent/template.ts";
import { RULES } from "./invariants.ts";

/** 一条反向自证：把某一种错注进一份真 prompt，注不进去就返回 null。 */
export interface Proof {
  /** 该被点名的那条不变量（`RULES` 里的一条）。 */
  rule: string;
  /** 注的是哪一种错——报错与报告里都用这一句。 */
  note: string;
  mutate: (prompt: string) => string | null;
}

/** 只换第一处；换不到就返回 null（这份 prompt 里没有那种句子，换一份）。 */
function replaceOnce(prompt: string, pattern: RegExp, replacement: string): string | null {
  if (!pattern.test(prompt)) return null;
  return prompt.replace(pattern, replacement);
}

/** 逐行改：`change` 返回 null 就是这一行不动，第一行改成了就收手。 */
function editLine(prompt: string, change: (line: string) => string | null): string | null {
  const lines = prompt.split("\n");
  for (const [index, line] of lines.entries()) {
    const edited = change(line);
    if (edited === null) continue;
    lines[index] = edited;
    return lines.join("\n");
  }
  return null;
}

/** `牌河：2m 9m 2m* 2z` / `  牌河：…` 那一行里的那几张（含摸切的 `*`）。 */
function kawaOf(line: string): { indent: string; tiles: string[] } | null {
  const found = /^(\s*)牌河：(.+)$/.exec(line);
  if (found === null || found[2] === "空") return null;
  return { indent: found[1], tiles: found[2].split(" ") };
}

/** `Tile.toDisplay` 那一套中文牌名的花色字（**只给反向自证用**：它要把记法写回改之前的样子）。 */
const SUIT_DISPLAY: Record<string, string> = { m: "万", p: "筒", s: "索" };

/** 34 种牌的 mjai 记法。**只给反向自证用**：它要找一张「手里没有的牌」。 */
const ALL_TILES: string[] = [
  ...["m", "p", "s"].flatMap((suit) =>
    Array.from({ length: 9 }, (_, index) => `${index + 1}${suit}`),
  ),
  ...Array.from({ length: 7 }, (_, index) => `${index + 1}z`),
];

export const PROOFS: Proof[] = [
  {
    rule: RULES.parsable,
    note: "把【可选动作】那一节的抬头抹掉（解析器读空了）",
    mutate: (prompt) => replaceOnce(prompt, new RegExp(`${DEFAULT_TEMPLATE.labels.actions}\n`), ""),
  },
  {
    rule: RULES.chiFromKamicha,
    note: "把他家那组吃的来源从「他的上家」改成「他的对家」（吃只吃得了上家）",
    mutate: (prompt) => replaceOnce(prompt, /吃 (\S+)（来自他的上家，/, "吃 $1（来自他的对家，"),
  },
  {
    rule: RULES.nakiFrame,
    note: "把他家那组副露的「他的」两个字抹掉（票 40 那个错，一字不差）",
    mutate: (prompt) => replaceOnce(prompt, /来自他的/, "来自"),
  },
  {
    rule: RULES.ankanHasNoSource,
    note: "给暗杠安一个来源",
    mutate: (prompt) => replaceOnce(prompt, /暗杠（亮出/, "暗杠（来自上家，亮出"),
  },
  {
    // 同一条不变量的另一个方向：**只有**暗杠没有来源。两个方向各自能独立坏，因此各自要证一次。
    rule: RULES.ankanHasNoSource,
    note: "把碰的来源抹掉（它鸣的是别人打的那张，不可能没有来源）",
    mutate: (prompt) => replaceOnce(prompt, /(碰 \S+（)来自[^，）]+，/, "$1"),
  },
  {
    rule: RULES.nakiTileCount,
    note: "把碰亮出来的两张吞掉一张",
    mutate: (prompt) => replaceOnce(prompt, /(碰 \S+（(?:来自[^，）]+，)?亮出 )\S+ /, "$1"),
  },
  {
    rule: RULES.kawaJunme,
    // **吞两张**不是一张：差一张本来就允许（轮到那家、摸完了还没打），吞一张证明不了什么。
    note: "从河里吞掉两张（河与巡目就对不上了）",
    mutate: (prompt) =>
      editLine(prompt, (line) => {
        const kawa = kawaOf(line);
        if (kawa === null || kawa.tiles.length < 3) return null;
        return `${kawa.indent}牌河：${kawa.tiles.slice(0, -2).join(" ")}`;
      }),
  },
  {
    rule: RULES.doraKan,
    note: "吞掉一张宝牌指示牌（开局那一张是无论如何都有的）",
    mutate: (prompt) =>
      editLine(prompt, (line) => {
        const found = /^宝牌指示牌：([^　]+)(　.*)$/.exec(line);
        if (found === null || found[1] === "无") return null;
        const kept = found[1].split(" ").slice(0, -1);
        return `宝牌指示牌：${kept.length === 0 ? "无" : kept.join(" ")}${found[2]}`;
      }),
  },
  {
    rule: RULES.riichiTsumogiri,
    // 随机选手一次都不立直（票 42），因此这一条在真语料上只能靠注进去的立直来证明。
    note: "往历史里插一行「宣言立直」，而那一家后面还有手切",
    mutate: (prompt) => {
      const lines = prompt.split("\n");
      const discard = /^(你|下家|对家|上家) (第\d+巡) 打 (\S+)$/;

      for (const [index, line] of lines.entries()) {
        const first = discard.exec(line);
        if (first === null) continue;

        // 宣言牌之后还得有一张**手切**，否则「立直后全摸切」本来就成立，证明不了什么。
        const tedashi = lines.slice(index + 1).some((later) => {
          const found = discard.exec(later);
          return found !== null && found[1] === first[1] && !found[3].endsWith("*");
        });
        if (!tedashi) continue;

        lines.splice(index, 0, `${first[1]} ${first[2]} 宣言立直`);
        return lines.join("\n");
      }
      return null;
    },
  },
  {
    rule: RULES.tehaiCount,
    note: "把手牌那一行声明的张数改小一张",
    mutate: (prompt) =>
      editLine(prompt, (line) => {
        const found = /^手牌：(.+)（(\d+) 张）$/.exec(line);
        return found === null ? null : `手牌：${found[1]}（${Number(found[2]) - 1} 张）`;
      }),
  },
  {
    rule: RULES.actionTilesFromHand,
    note: "把一条打牌动作换成一张手里没有的牌",
    mutate: (prompt) => {
      const hand = /^手牌：(.+)（\d+ 张）$/m.exec(prompt);
      if (hand === null) return null;
      const held = new Set(hand[1].split(" "));
      const absent = ALL_TILES.find((pai) => !held.has(pai));
      if (absent === undefined) return null;
      return replaceOnce(prompt, /^(- id=\d+：)打 \S+$/m, `$1打 ${absent}`);
    },
  },
  {
    rule: RULES.fourOfAKind,
    note: "把某一家河里的牌全换成同一张（张数不变，只是凭空多出几张同种牌）",
    mutate: (prompt) =>
      editLine(prompt, (line) => {
        const kawa = kawaOf(line);
        if (kawa === null || kawa.tiles.length < 5) return null;
        const first = kawa.tiles[0].replace("*", "");
        return `${kawa.indent}牌河：${kawa.tiles.map(() => first).join(" ")}`;
      }),
  },
  {
    rule: RULES.mjaiOnly,
    // **这就是改之前那一行的原样**：`Tile.toDisplay` 渲的中文牌名 + 手切 / 摸切两个词。
    // 阴性对照因此不是手捏的，是「把这一票的改动在那一行上退回去」。
    note: "把一条打牌动作写回票 95 之前的样子（中文牌名 + 手切 / 摸切）",
    mutate: (prompt) => {
      const found = /^- id=(\d+)：打 ([1-9])([mps])(r?)(\*?)$/m.exec(prompt);
      if (found === null) return null;
      const display = `${found[4] === "r" ? "赤" : ""}${found[2]}${SUIT_DISPLAY[found[3]]}`;
      return prompt.replace(
        found[0],
        `- id=${found[1]}：${found[5] === "*" ? "摸切" : "手切"}${display}`,
      );
    },
  },
  {
    rule: RULES.mjaiOnly,
    // 措辞表查不到时 `wording.ts` 的 `lookup` 就是把 wire 值原样写出去，这一条演的是那种漏法。
    note: "把历史里一组碰的种类词换成 mjai 的 wire 名（措辞表查不到时就是这个样子）",
    mutate: (prompt) =>
      replaceOnce(
        prompt,
        new RegExp(`(第\\d+巡) ${DEFAULT_TEMPLATE.wording.naki.pon} `),
        "$1 pon ",
      ),
  },
  {
    rule: RULES.mjaiOnly,
    note: "把场风写成中文风名（`1z` → 东）",
    mutate: (prompt) => replaceOnce(prompt, /^场风 1z・/m, "场风 东・"),
  },
  {
    rule: RULES.scaffoldEchoesActions,
    // 这一种错**纯看那一行是看不出来的**：`打 3p` 与 `打 5p` 都是通顺的一行，
    // 只有把它与【可选动作】里同一个 id 对起来才知道它把数挂到了别的牌上。
    note: "把算好的数那一节里一条试打点名的牌换成另一张（那一行仍然通顺）",
    mutate: (prompt) => {
      const found = /^- id=(\d+)（打 (\S+?)）：打完 /m.exec(prompt);
      if (found === null) return null;
      const other = found[2] === "1m" ? "9s" : "1m";
      return prompt.replace(found[0], found[0].replace(`（打 ${found[2]}）`, `（打 ${other}）`));
    },
  },
  {
    rule: RULES.scaffoldEchoesActions,
    note: "把一条危险度挂到一个根本不在这一手里的 id 上",
    mutate: (prompt) => {
      const found = /^- 第(\d+)位 id=(\d+)（/m.exec(prompt);
      if (found === null) return null;
      return prompt.replace(found[0], `- 第${found[1]}位 id=997（`);
    },
  },
  {
    rule: RULES.scaffoldEchoesActions,
    // ToolSearch 档那一行：写给模型看的「还能问几次」与真上限对不上，
    // 模型就会按一个错的预算去分配它那几次查询。
    note: "把「你查过 N 次，还可以再查 M 次」里的 M 加一（加起来不再等于上限）",
    mutate: (prompt) => {
      const found = /^你查过 (\d+) 次，还可以再查 (\d+) 次：$/m.exec(prompt);
      if (found === null) return null;
      return prompt.replace(
        found[0],
        `你查过 ${found[1]} 次，还可以再查 ${Number(found[2]) + 1} 次：`,
      );
    },
  },
];
