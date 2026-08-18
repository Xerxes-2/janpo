/**
 * **算好的那几个数 ↔ dotnet 侧算出来的那一份**（票 94 的第三道对拍）。
 *
 * ## 为什么要它
 *
 * 票 94 的硬判据是「工具的答案必须与 Assisted 档给的**同一个函数**算出来，不许为工具另算一遍」。
 * 那件事有两条独立的证据，缺一不可：
 *
 * 1. **结构上的**：`what-if.test.ts` 断言同一个 id 的那一行与 Assisted 档印的那一行**逐字节相同**
 *    ——同一个函数、同一份 `decision.scaffold`，所以字节一样。
 * 2. **这一道**：把渲出来的那几行**解析回数**，与决策包里 `scaffold` 那一段（**dotnet 的
 *    `Scaffold.calculate` 算的**，随包过界）**逐字段**对上。它抓的是转录错：把 62 枚写成 26 枚、
 *    把两条查询的答案串了行、把某一条的危险度挂到另一条 id 上——这几种错在纯文本里全都自洽，
 *    `invariants.ts` 那十三条一条也看不见（与 `naki-crosscheck` / `label-crosscheck` 同一个理由）。
 *
 * **口径说清楚**（判据 18）：左边是「渲出来的文本解析回来」，右边是「dotnet 出的 JSON」，
 * 两侧不是同一份实现，但**同一个数据源**——这一道证的是**转录忠实**，不是「这个数算得对」。
 * 「算得对」由引擎自己的用例（`ShantenTests` / `UkeireTests` / `DangerTests`）守着，不在这一层。
 *
 * ## 它跑在哪
 *
 * - `tests/agent/what-if.test.ts`：committed 固件（`janpo decide` 的产物）逐条对拍 + 反向自证；
 * - `web/scripts/verify-invariants.mjs`：扫真实对局，每一手都合成几条查询再对拍。
 */

import { promptTail } from "../../src/agent/prompt.ts";
import { DEFAULT_TEMPLATE, type PromptTemplate } from "../../src/agent/template.ts";
import type { DecisionPackage } from "../../src/agent/types.ts";
import type { WhatIf } from "../../src/agent/what-if.ts";
import type { Violation } from "./invariants.ts";

/**
 * 报错时点名的那一道。**两档共用**：Assisted 推给它的那一整张表、ToolSearch 它自己查出来的那几条，
 * 是同一个函数渲的同一种行，因此由同一道对拍守（票 94）。
 */
export const SCAFFOLD_CROSSCHECK = "算好的数 ↔ 决策包里那一份";

/** 一次对拍的结果：对不上的那几条，加上**各类字段各对了几次**（判据 3）。 */
export interface Crosschecked {
  violations: Violation[];
  judged: Record<string, number>;
}

/** 试打那一行：`- id=3（打 5p）：打完 3 向听，进退向 0，有效牌 43 枚 13 种：2m(4) …` */
const TRIAL =
  /^- id=(\d+)（(.+?)）：打完 (.+?)，(进退向 0|退向 \+(\d+))(?:，有效牌 (?:(\d+) 枚 (\d+) 种：(.*)|无有效牌))?$/;

/** 危险度那一行：`- 第11位 id=3（打 5p）：无依据 —— 宝牌 7p 周边` */
const DANGER = /^- 第(\d+)位 id=(\d+)（(.+?)）：([^—]+?)(?: —— (.*))?$/;

/** 决策包里那一段脚手架的形状。**只读这几格**：这一道不解释别的。 */
interface Scaffold {
  dahai?: {
    action_ids?: number[];
    shanten?: { display?: string };
    shanten_delta?: number;
    ukeire?: { total?: number; kinds?: number; tiles?: { pai?: string; remaining?: number }[] };
    danger?: {
      tier?: { display?: string };
      rank?: number;
      reasons?: { display?: string }[];
    } | null;
  }[];
}

/** 尾部里【引擎算好的数】那一节的那几行（抬头从模板取，与 `invariants.ts` 同一个方针）。 */
function blockLines(tail: string, template: PromptTemplate): string[] {
  const lines = tail.split("\n");
  const at = lines.indexOf(template.labels.scaffold);
  if (at < 0) return [];

  const rest = lines.slice(at + 1);
  const end = rest.indexOf("");
  return end < 0 ? rest : rest.slice(0, end);
}

/** id → 决策包里那一条试打。 */
function byId(scaffold: Scaffold): Map<number, NonNullable<Scaffold["dahai"]>[number]> {
  const table = new Map<number, NonNullable<Scaffold["dahai"]>[number]>();
  for (const entry of scaffold.dahai ?? []) {
    for (const id of entry.action_ids ?? []) table.set(id, entry);
  }
  return table;
}

/**
 * 一手的对拍：把那一节渲出来的每一行解析回数，与决策包里同一个 id 那一条逐字段比。
 *
 * `tail` 由调用方渲好递进来（**两个跑法各渲各的**：用例渲固件，扫描器渲真对局），
 * 这一层只负责「这几行说的与那一份数据一致吗」。
 */
export function crosscheckWhatIf(
  decision: DecisionPackage,
  tail: string,
  where: string,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): Crosschecked {
  const violations: Violation[] = [];
  const judged: Record<string, number> = {};
  const table = byId(decision.scaffold as Scaffold);

  const count = (what: string) => {
    judged[what] = (judged[what] ?? 0) + 1;
  };
  const report = (sentence: string, detail: string) =>
    violations.push({ rule: SCAFFOLD_CROSSCHECK, where, sentence, detail });

  for (const line of blockLines(tail, template)) {
    const trial = TRIAL.exec(line);
    if (trial !== null) {
      const id = Number(trial[1]);
      const entry = table.get(id);
      if (entry === undefined) {
        report(line, `决策包的脚手架里没有 id=${id} 这一条试打，这一行却报出了它的数`);
        continue;
      }

      count("向听");
      if (trial[3] !== entry.shanten?.display) {
        report(line, `打完之后写的是「${trial[3]}」，决策包里是「${entry.shanten?.display}」`);
      }

      count("进退向");
      const delta = trial[5] === undefined ? 0 : Number(trial[5]);
      if (delta !== (entry.shanten_delta ?? 0)) {
        report(line, `进退向写的是 ${delta}，决策包里是 ${entry.shanten_delta}`);
      }

      const ukeire = entry.ukeire;
      if (ukeire !== null && ukeire !== undefined) {
        count("有效牌");
        const tiles = (ukeire.tiles ?? [])
          .map((tile) => `${tile.pai}(${tile.remaining})`)
          .join(" ");
        const wrote =
          trial[6] === undefined ? "无有效牌" : `${trial[6]} 枚 ${trial[7]} 种：${trial[8]}`;
        const want =
          (ukeire.tiles ?? []).length === 0
            ? "无有效牌"
            : `${ukeire.total} 枚 ${ukeire.kinds} 种：${tiles}`;
        if (wrote !== want) report(line, `有效牌写的是「${wrote}」，决策包里是「${want}」`);
      }
      continue;
    }

    const danger = DANGER.exec(line);
    if (danger === null) continue;

    const id = Number(danger[2]);
    const entry = table.get(id);
    const mine = entry?.danger;
    if (mine === undefined || mine === null) {
      report(line, `决策包里 id=${id} 这一条没有危险度，这一行却给它排了个名次`);
      continue;
    }

    count("危险度名次");
    if (Number(danger[1]) !== mine.rank) {
      report(line, `名次写的是第 ${danger[1]} 位，决策包里是第 ${mine.rank} 位`);
    }

    count("危险度档位");
    if (danger[4] !== mine.tier?.display) {
      report(line, `档位写的是「${danger[4]}」，决策包里是「${mine.tier?.display}」`);
    }

    count("危险度理由");
    const reasons = (mine.reasons ?? []).map((reason) => reason.display).join("、");
    if ((danger[5] ?? "") !== reasons) {
      report(line, `理由写的是「${danger[5] ?? ""}」，决策包里是「${reasons}」`);
    }
  }

  return { violations, judged };
}

/** 一个反向自证：把渲出来的那份尾部改坏一处，这一道必须当场红。 */
export interface Proof {
  note: string;
  /** 改得动就返回改坏的那一份，这一手没有可改的地方就返回 null。 */
  mutate: (tail: string) => string | null;
}

/**
 * 三个变异，各注一种**纯文本那十三条按设计看不见**的错：
 * 数抄错、名次挂到别的 id 上、理由丢了一半。三种在文本里全都自洽。
 */
export const SCAFFOLD_PROOFS: Proof[] = [
  {
    note: "把一条试打的有效牌枚数抄错（记法仍然合法，只是数不对了）",
    mutate: (tail) => {
      const found = /^- id=\d+（.+?）：打完 .+?，(?:进退向 0|退向 \+\d+)，有效牌 (\d+) 枚/m.exec(
        tail,
      );
      if (found === null) return null;
      const wrong = `${Number(found[1]) + 1} 枚`;
      return tail.replace(found[0], found[0].replace(`${found[1]} 枚`, wrong));
    },
  },
  {
    note: "把一条危险度的名次挪一位（那一行仍然是一句通顺的话）",
    mutate: (tail) => {
      const found = /^- 第(\d+)位 (id=\d+（.+?）：.+)$/m.exec(tail);
      if (found === null) return null;
      return tail.replace(found[0], `- 第${Number(found[1]) + 1}位 ${found[2]}`);
    },
  },
  {
    note: "把一条危险度的理由整段删掉（剩下的仍然是一句通顺的话）",
    mutate: (tail) => {
      const found = /^- 第\d+位 id=\d+（.+?）：[^—\n]+ —— .+$/m.exec(tail);
      if (found === null) return null;
      return tail.replace(found[0], found[0].replace(/ —— .*$/, ""));
    },
  },
];

/** 扫描器用：一手渲一份带查询的 ToolSearch 尾部（**查前几条打牌**，deterministic）。 */
export function askedFor(decision: DecisionPackage, howMany: number): WhatIf[] {
  return decision.actions
    .filter((option) => (option.action as { type?: string }).type === "dahai")
    .slice(0, howMany)
    .map((option) => ({ id: option.id }));
}

/** 扫描器用：那一手的 ToolSearch 尾部（查询由 `askedFor` 合成）。 */
export function toolSearchTail(
  decision: DecisionPackage,
  howMany: number,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): string {
  return promptTail(decision, "tool_search", null, template, askedFor(decision, howMany));
}
