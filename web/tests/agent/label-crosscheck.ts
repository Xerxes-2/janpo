/**
 * **可选动作那一行 ↔ 引擎自己渲的中文 label**（票 95 的对拍）。
 *
 * 票 95 把动作那一行从「照抄决策包里的中文 label」改成「从 mjai 动作消息现渲」
 * （`src/agent/action-label.ts`）。改记法最怕的不是难看，是**把动作说错**：
 * 少一张亮牌、把碰写成吃、把赤 5 写成正 5——这几种错在纯文本里全都自洽，
 * `invariants.ts` 那十二条一条也看不见（与副露来源那道对拍是同一个道理）。
 *
 * 这一份文件因此拿**引擎那份中文 label**（`Action.toDisplay`，一个字没改）当第三锚点，
 * 与渲出来的那一行逐条对拍：**种类要对得上、点名的那几张牌要是同一个多重集**。
 * 两侧是两份互相独立的实现——一份 F# 写在引擎里、一份 TS 写在渲染层里，
 * 各自从同一个 `Action` 出发（判据 18：别拿同源结果当右侧）。
 *
 * 中文牌名 → mjai 那张表**只在这一层**（`tileOfDisplay`，票 41 起就在这儿，
 * 从 `invariants.ts` 搬过来的）：产品代码里一张都不许有。
 *
 * 两处在用：`prompt.test.ts`（committed 固件）与 `scripts/verify-invariants.mjs`
 * （扫出来的真语料）。**共用一份，两处说法因此一致。**
 */

import type { PromptTemplate } from "../../src/agent/template.ts";
import { DEFAULT_TEMPLATE } from "../../src/agent/template.ts";
import type { DecisionPackage } from "../../src/agent/types.ts";
import { parseActionLabel, parsedActions, type Violation } from "./invariants.ts";

/** 这道对拍的名字。**它不是 `RULES` 里的一条**（那十二条只读文本）。 */
export const LABEL_CROSSCHECK = "动作那一行 ↔ 引擎的中文 label";

const SUIT_OF_DISPLAY: Record<string, string> = { 万: "m", 筒: "p", 索: "s" };
const JIHAI_DISPLAY = ["东", "南", "西", "北", "白", "发", "中"];

/**
 * 中文牌名 → mjai 记法（`赤5筒` → `5pr`、`中` → `7z`）。
 *
 * **它只活在测试与闸门里**（票 41 立的规矩，票 95 把它从 `invariants.ts` 搬到这儿）：
 * 产品那一侧现在从头到尾只有 mjai 一套记法，这份表是**闸门自己认一遍中文**用的，
 * 好让两边不会错成同一个样子。
 */
export function tileOfDisplay(display: string): string | null {
  const akadora = display.startsWith("赤");
  const body = akadora ? display.slice(1) : display;

  const jihai = JIHAI_DISPLAY.indexOf(body);
  if (jihai >= 0) return akadora ? null : `${jihai + 1}z`;

  const parsed = /^([1-9])([万筒索])$/.exec(body);
  if (parsed === null) return null;
  return `${parsed[1]}${SUIT_OF_DISPLAY[parsed[2]]}${akadora ? "r" : ""}`;
}

/** 一条动作拆开之后：**它是哪一种**、**它点名了哪几张牌**。两侧各自拆一遍再比。 */
export interface Named {
  /** 种类的规范名（`dahai` / `dahai*` / `pon` / … / `hora-tsumo`）。 */
  kind: string;
  /** 这一行点名的每一张牌，mjai 记法，**顺序保留**（多重集比较时再排序）。 */
  tiles: string[];
}

/**
 * 引擎那份中文 label 拆开（`Action.toDisplay` 的句式，**与模板无关**）。
 *
 * 拆不动就返回 null——那说明引擎那边换了句式，这道对拍当场说出来，
 * 而不是悄悄放行（一道读不懂被检查物的闸门等于没有闸门）。
 */
export function ofEngineLabel(label: string): Named | null {
  const tiles = (text: string): string[] | null => {
    const found = text.split(" ").map(tileOfDisplay);
    return found.some((pai) => pai === null) ? null : (found as string[]);
  };

  const patterns: [RegExp, (groups: string[]) => Named | null][] = [
    [/^摸切(.+)$/, ([pai]) => named("dahai*", tiles(pai))],
    [/^手切(.+)$/, ([pai]) => named("dahai", tiles(pai))],
    [/^自摸(.+)$/, ([pai]) => named("hora-tsumo", tiles(pai))],
    [/^荣和(.+)$/, ([pai]) => named("hora-ron", tiles(pai))],
    [/^碰(.+)（亮(.+)）$/, ([pai, shown]) => named("pon", tiles(`${pai} ${shown}`))],
    [/^吃(.+)（亮(.+)）$/, ([pai, shown]) => named("chi", tiles(`${pai} ${shown}`))],
    [/^大明杠(.+)（亮(.+)）$/, ([pai, shown]) => named("daiminkan", tiles(`${pai} ${shown}`))],
    [/^暗杠（亮(.+)）$/, ([shown]) => named("ankan", tiles(shown))],
    [/^加杠(.+)$/, ([pai]) => named("kakan", tiles(pai))],
    [/^立直宣言$/, () => named("reach", [])],
    [/^过$/, () => named("none", [])],
    [/^九种九牌$/, () => named("ryukyoku", [])],
  ];

  for (const [pattern, build] of patterns) {
    const found = pattern.exec(label);
    if (found !== null) return build(found.slice(1));
  }
  return null;
}

function named(kind: string, tiles: string[] | null): Named | null {
  return tiles === null ? null : { kind, tiles };
}

/** 渲出来那一行拆开（`invariants.ts` 的解析器，模板给的措辞现取）。 */
function ofRendered(label: string, template: PromptTemplate): Named | null {
  const parsed = parseActionLabel(label, template);
  return parsed === null ? null : { kind: parsed.kind, tiles: parsed.named };
}

/** 多重集比较用的规范写法。 */
const canonical = (tiles: string[]) => [...tiles].sort().join(" ");

export interface Crosscheck {
  violations: Violation[];
  /** 逐格执行次数（防空转：哪一种动作这一趟没被对拍过，报告里看得见）。 */
  judged: Record<string, number>;
}

/**
 * 逐条动作对拍：渲出来那一行说的，与引擎那份中文 label 说的，必须是同一件事。
 *
 * **决策包只用来取 `label`**（引擎渲好的那一份），不拿它的结构化字段当右侧——
 * 那正是渲染器自己读的那一份，用它比等于自己证自己。
 */
export function crosscheckLabels(
  decision: DecisionPackage,
  prompt: string,
  where: string,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): Crosscheck {
  const violations: Violation[] = [];
  const judged: Record<string, number> = {};
  const tally = (key: string) => {
    judged[key] = (judged[key] ?? 0) + 1;
  };
  const report = (sentence: string, detail: string) =>
    violations.push({ rule: LABEL_CROSSCHECK, where, sentence, detail });

  const rendered = new Map(parsedActions(prompt, template).map((each) => [each.id, each]));

  for (const option of decision.actions) {
    const line = rendered.get(option.id);
    if (line === undefined) {
      report(`- id=${option.id}`, `包里有 id=${option.id} 这条动作，prompt 里却没有它那一行`);
      continue;
    }

    const engine = ofEngineLabel(option.label);
    if (engine === null) {
      report(line.line, `引擎那份 label「${option.label}」这道对拍拆不动——它换句式了？`);
      continue;
    }

    const mine = ofRendered(line.label, template);
    if (mine === null) {
      report(line.line, `渲出来那一行拆不动：引擎那边说的是「${option.label}」`);
      continue;
    }

    tally(engine.kind);
    if (mine.kind !== engine.kind) {
      report(
        line.line,
        `渲出来是「${mine.kind}」，引擎那份 label「${option.label}」说的是「${engine.kind}」`,
      );
    }
    if (canonical(mine.tiles) !== canonical(engine.tiles)) {
      report(
        line.line,
        `渲出来点名的是 ${canonical(mine.tiles) || "（一张都没有）"}，` +
          `引擎那份 label「${option.label}」点名的是 ${canonical(engine.tiles) || "（一张都没有）"}`,
      );
    }
  }

  return { violations, judged };
}

/** 一条反向自证：把某一种错注进渲出来那一行，注不进去就返回 null。 */
export interface LabelProof {
  note: string;
  /** 纯文本那十二条**按设计看不见它**吗（换过的仍是一张合法的牌、换过的词仍在措辞表里）。 */
  textBlind: boolean;
  mutate: (prompt: string) => string | null;
}

/** 把一张牌换成另一张同花色的（`9m` 换成 `1m`）。**换出来的仍是一张合法的牌**。 */
function shift(pai: string): string {
  const found = /^([1-9])([mps])(r?)$/.exec(pai);
  if (found === null) return pai;
  return `${found[1] === "9" ? "1" : String(Number(found[1]) + 1)}${found[2]}`;
}

/**
 * 反向自证。三种错各证一次——**牌变了**与**种类变了**是「换记法」最容易出的两类错，
 * 而后两条纯文本那十二条按设计一条也看不见（换过的那张牌仍然合法，碰与吃都在措辞表里）。
 */
export const LABEL_CROSSCHECK_PROOFS: LabelProof[] = [
  {
    note: "把一条打牌动作的牌换成另一张（记法仍然合法，只是说的不是同一张牌了）",
    // 这一条纯文本那边也看得见（换出来的牌多半不在手里），它证的是这道对拍**不比它弱**。
    textBlind: false,
    mutate: (prompt) => {
      const found = /^(- id=\d+：打 )([1-9][mps]r?)(\*?)$/m.exec(prompt);
      return found === null
        ? null
        : prompt.replace(found[0], `${found[1]}${shift(found[2])}${found[3]}`);
    },
  },
  {
    note: "把一条碰鸣来的那张换掉，亮出来的两张不动（纯文本那十二条判不出）",
    textBlind: true,
    mutate: (prompt) => {
      const pon = escapeRegExp(DEFAULT_TEMPLATE.wording.naki.pon);
      const found = new RegExp(`^(- id=\\d+：${pon} )([1-9][mps]r?)(（亮出 [^）]*）)$`, "m").exec(
        prompt,
      );
      return found === null
        ? null
        : prompt.replace(found[0], `${found[1]}${shift(found[2])}${found[3]}`);
    },
  },
  {
    note: "把一条碰改写成吃（两个词都在措辞表里，纯文本那十二条判不出）",
    textBlind: true,
    mutate: (prompt) => {
      const pon = escapeRegExp(DEFAULT_TEMPLATE.wording.naki.pon);
      const pattern = new RegExp(`^(- id=\\d+：)${pon}( .+（亮出 .+）)$`, "m");
      return pattern.test(prompt)
        ? prompt.replace(pattern, `$1${DEFAULT_TEMPLATE.wording.naki.chi}$2`)
        : null;
    },
  },
];

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
