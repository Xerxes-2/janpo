/**
 * **副露那句「来自谁」↔ 决策包里的绝对座位**（票 40 的对拍，票 49 搬到批量语料上）。
 *
 * 与 `invariants.ts` 那十一条是**两件事**，别混：
 *
 * - `invariants.ts` **只读文本**，判据是「这句中文在日麻规则下可不可能」。因此它抓得住
 *   「吃 来自对家」（吃只吃得了上家），却**永远抓不住「碰的来源指错了人」**——
 *   碰 / 大明杠 / 加杠谁都鸣得了，一句指错人的话在纯文本里完全自洽（票 41 报告 §8-1）。
 * - 这一份**拿文本与决策包对拍**：包里是绝对座位（`Naki.target`），永远没错；
 *   错的是把绝对座位换算成方位词那一步。票 40 的 `decision-danger` 里那句
 *   「碰 1z（来自上家）」就是这一类——**它不会自己露馅**。
 *
 * ## 参照系是副露方（票 40 的裁定）
 *
 * 那句「来自X」说的是**副露方**的第几家（`Naki.Target` 的语义，与牌桌那边一致），
 * 而【他家】那一节的抬头（`下家（座位 2・…）`）说的是**观测者**的第几家。同一份 prompt 里
 * 两个参照系各管一半，因此谁是谁必须在那句话里就看得出来：他家那几组写「他的X」，
 * 自家那几组写光秃秃的「X」（两个参照系重合），鸣的是观测者打的那张时写「你」。
 *
 * ## 它自己走一遍座位环
 *
 * `distance` 不调渲染器用的那份 `wordsFor`：两边都错成同一个样子的话这道对拍就白写了
 * （与 `invariants.ts` 里那份第二份中文牌名表同一个理由）。它数的是**决策包自己给的**
 * 那个圈（`relative` 排出来的顺序），不做座位取模。
 *
 * 两处在用：`prompt.test.ts`（15 份 committed 固件）与 `scripts/verify-invariants.mjs`
 * （扫出来的真语料）。**共用一份，两处说法因此一致。**
 */

import type { PromptTemplate } from "../../src/agent/template.ts";
import { DEFAULT_TEMPLATE } from "../../src/agent/template.ts";
import type { DecisionPackage, Naki } from "../../src/agent/types.ts";
import type { Violation } from "./invariants.ts";

/** 这道对拍的名字。**它不是 `RULES` 里的一条**（那十一条只读文本，是票 41 的地盘）。 */
export const NAKI_CROSSCHECK = "副露来源 ↔ 决策包座位";

/** 渲出来的一组副露：挂在谁名下、写着什么种类、写着来自谁。 */
export interface RenderedNaki {
  /** 这一组挂在谁名下：`你`，或【他家】那一节的抬头里那个称呼。 */
  owner: string;
  /** 种类的中文（模板的 `wording.naki`）。 */
  kind: string;
  /** 那句「来自X」里的 X；暗杠没有这一项。 */
  from: string | null;
  /** 报错时的「哪句话」。 */
  line: string;
}

/** 把渲出来的 prompt 拆回「谁的哪一组副露、写着来自谁」。 */
export function renderedNaki(prompt: string, template: PromptTemplate): RenderedNaki[] {
  const kinds = Object.values(template.wording.naki).map(escapeRegExp).join("|");
  const groups = new RegExp(`(${kinds})(?: [^（]+)?（(?:来自([^，]+)，)?亮出 [^）]+）`, "g");
  const owners = /^(?:你是座位 \d+|(.+?)（座位 \d+・)/;

  const found: RenderedNaki[] = [];
  let owner = "";

  for (const line of prompt.split("\n")) {
    const head = owners.exec(line);
    if (head !== null) owner = head[1] ?? "你";

    const naki = /^\s*副露：(.+)$/.exec(line);
    if (naki === null) continue;

    for (const [, kind, from] of naki[1].matchAll(groups)) {
      found.push({ owner, kind, from: from ?? null, line });
    }
  }

  return found;
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/** 决策包里那几家：绝对座位、观测者眼里的称呼、以及各自的副露。 */
function seatsOf(decision: DecisionPackage) {
  const relatives = DEFAULT_TEMPLATE.wording.relative;
  const self = decision.observation.self;

  return [
    { seat: self.seat, name: "你", naki: self.naki },
    ...decision.observation.others.map((other) => ({
      seat: other.seat,
      name: relatives[String(other.relative)] ?? `座位 ${other.seat}`,
      naki: other.naki,
    })),
  ];
}

/**
 * 从 `owner` 起沿**下家方向**数到 `seat` 是第几家。
 *
 * 独立走一遍座位环（决策包给的 `relative` 排出来的那个圈），**不去调渲染器用的那份**。
 */
export function distance(decision: DecisionPackage, owner: number, seat: number): number {
  const ring = [
    { seat: decision.observation.self.seat, relative: 0 },
    ...decision.observation.others,
  ]
    .sort((left, right) => left.relative - right.relative)
    .map((each) => each.seat);

  const start = ring.indexOf(owner);
  return [...ring.slice(start), ...ring.slice(0, start)].indexOf(seat);
}

/** 一组副露的两侧配好对：渲出来的那半句话，与包里那一组的绝对座位。 */
export interface NakiPair {
  /** 哪一手的哪一组——报错要点名它。 */
  where: string;
  /** 观测者坐在哪。 */
  viewer: number;
  /** 副露方的绝对座位。 */
  owner: number;
  /** 来源那一家的绝对座位；暗杠没有。 */
  target: number | null;
  /** 包里的种类（wire 名）。 */
  wire: string;
  /** 从副露方起沿下家方向数到来源那家是第几家；暗杠没有来源，记 0。 */
  steps: number;
  /** 渲出来的那一组。 */
  rendered: RenderedNaki;
  /** 那句「来自X」**该**怎么写；`null` 是「本来就不该有来源」。 */
  expected: string | null;
}

/** 这一趟对拍**真的执行了几次**（票 49）：一组副露一次，按种类、副露方与换算的分支分开数。 */
export type CrosscheckTally = Record<string, number>;

export interface Crosscheck {
  violations: Violation[];
  judged: CrosscheckTally;
  pairs: NakiPair[];
}

function count(tally: CrosscheckTally, key: string): void {
  tally[key] = (tally[key] ?? 0) + 1;
}

/** 换算落在哪一格——**四个分支各自会独立坏**，因此各自要数。 */
function branchOf(pair: { viewer: number; owner: number; target: number | null }): string {
  if (pair.target === null) return "暗杠・无来源";
  if (pair.target === pair.viewer) return "鸣了你打的那张・写「你」";
  if (pair.owner === pair.viewer) return "自家的副露・相对观测者";
  return "他家的副露・「他的X」";
}

/**
 * 一手的对拍：**渲出来的每一组副露**与**包里那一组的绝对座位**逐组比。
 *
 * 三件事各自会独立坏，因此各自报：这一组挂在谁名下、写的是哪一种副露、来源那个词。
 * **不抛**：一次扫一批局面，一句坏话不该让扫描停在那里（与 `promptViolations` 同一个形态）。
 */
export function crosscheckNaki(
  decision: DecisionPackage,
  prompt: string,
  where: string,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): Crosscheck {
  const relatives = template.wording.relative;
  const wording = template.wording.naki;
  const rendered = renderedNaki(prompt, template);
  const expected = seatsOf(decision).flatMap((seat) =>
    seat.naki.map((group: Naki) => ({ seat: seat.seat, name: seat.name, group })),
  );

  const violations: Violation[] = [];
  const judged: CrosscheckTally = {};
  const pairs: NakiPair[] = [];
  const viewer = decision.observation.self.seat;

  const report = (sentence: string, detail: string) =>
    violations.push({ rule: NAKI_CROSSCHECK, where, sentence, detail });

  if (rendered.length !== expected.length) {
    report(
      rendered.map((group) => group.line).join(" / "),
      `渲出来 ${rendered.length} 组副露，包里有 ${expected.length} 组：逐组对不起来`,
    );
    return { violations, judged, pairs };
  }

  for (const [index, group] of rendered.entries()) {
    const mine = expected[index];
    const target = mine.group.target;
    const steps = target === null ? 0 : distance(decision, mine.seat, target);
    const relative = relatives[String(steps)] ?? `座位 ${target}`;

    // 写出来的那个词必须就是「从**副露方**起数到那一家是第几家」；
    // 鸣的是观测者打的那张时写「你」（那是身份不是方位）；自家那几组两个参照系重合。
    const wanted =
      target === null
        ? null
        : target === viewer
          ? "你"
          : mine.seat === viewer
            ? relative
            : `他的${relative}`;

    // 报错里的「哪一句话」：`formatViolation` 已经印了哪一手，这里只说哪一组。
    const label = `${mine.name}那组「${group.kind}」`;

    const pair: NakiPair = {
      where: `${where} 的${label}`,
      viewer,
      owner: mine.seat,
      target,
      wire: mine.group.type,
      steps,
      rendered: group,
      expected: wanted,
    };
    pairs.push(pair);

    count(
      judged,
      `${wording[mine.group.type] ?? mine.group.type}・${mine.seat === viewer ? "自家" : "他家"}`,
    );
    count(judged, branchOf(pair));
    count(judged, `视角・座位 ${viewer}`);

    if (group.owner !== mine.name) {
      report(group.line, `${label}挂错了人：包里这一组是${mine.name}（座位 ${mine.seat}）的`);
    }

    const kind = wording[mine.group.type] ?? mine.group.type;
    if (group.kind !== kind) {
      report(group.line, `${label}：包里那一组是「${kind}」（${mine.group.type}）`);
    }

    if (group.from !== wanted) {
      report(
        group.line,
        `${label}写着「${group.from === null ? "（没写来源）" : `来自${group.from}`}」：` +
          `包里的来源是座位 ${target}（副露方是座位 ${mine.seat}、观测者是座位 ${viewer}），` +
          `参照系取副露方算出来该写「${wanted === null ? "（不写来源）" : `来自${wanted}`}」`,
      );
    }
  }

  return { violations, judged, pairs };
}

/**
 * 这道对拍的**反向自证**：把渲出来的那句话改坏，它必须当场点名。
 *
 * **一道从不失败的闸门等于没有闸门**（票 34 立的规矩）。两个变异各证一件事：
 *
 * 1. 参照系漂到观测者（票 40 那个错，一字不差）——纯文本闸门只在「吃」上抓得住它；
 * 2. **来源指错了人但方位词仍然合法**（把碰的来源换成另一家）——纯文本闸门
 *    **按设计就看不见**这一类（碰谁都碰得了，票 41 报告 §8-1），只有这道对拍看得见。
 *
 * 变异一律作用在**渲出来的字节**上，不改渲染器：拿真 prompt 改一处出来的反例，
 * 比手捏一份假 prompt 更像真会发生的回归。
 */
export interface CrosscheckProof {
  note: string;
  mutate: (prompt: string) => string | null;
}

export const CROSSCHECK_PROOFS: CrosscheckProof[] = [
  {
    note: "把他家那组副露的「他的」两个字抹掉（参照系漂回观测者，票 40 那个错）",
    mutate: (prompt) => (prompt.includes("来自他的") ? prompt.replace("来自他的", "来自") : null),
  },
  {
    note: "把一组**碰**的来源换成另一家（方位词仍然合法：碰谁都碰得了，纯文本判不出）",
    mutate: (prompt) => {
      const found = /碰 \S+（来自(他的)?(下家|对家|上家)，/.exec(prompt);
      if (found === null) return null;
      const other = found[2] === "上家" ? "下家" : "上家";
      return prompt.replace(found[0], found[0].replace(found[2], other));
    },
  },
];
