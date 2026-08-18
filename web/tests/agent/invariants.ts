/**
 * **渲染出来的那句中文在日麻规则下成不成立**（票 41）。
 *
 * 现有的十余道闸门里，黄金用例逐字段钉的是**决策包**（结构化数据，里面全是绝对座位，
 * 永远没错），前缀属性测试钉的是**字节单调**（这一手与上一手是不是同一串字节）。
 * 两道都绿，而模型唯一读得到的是渲出来的自然语言——票 40 那句「吃 2p（来自对家）」
 * （吃只吃得了上家，规则上不可能）就是从这个缺口漏过去的，十二张票没有一道拦得住。
 *
 * 这一份文件补的是那一层：**判据是「这句话在麻将规则下可不可能」，不是「这句话与上次是否逐字相同」**。
 *
 * ## 它只读文本
 *
 * 入口 `promptViolations` 只收**渲出来的那份 prompt**，一个决策包字段都不读。这是有意的：
 *
 * - 决策包是**被检查的那句话的来源**，拿它当期望值就等于「用同一份数据证明它自己」
 *   ——票 40 的错正是「包对、话错」，逐字段对拍看不出来（那种对拍另有其人，
 *   在 `prompt.test.ts` 里，钉的是「词 = 从副露方数到那一家的第几家」）。
 * - 这些不变量是**日麻规则**，不是这份实现的性质：河牌数与巡目的关系、暗杠没有来源、
 *   宝牌指示牌不多于杠数加一，随便哪一份牌谱、哪一版渲染器都得成立。
 *
 * 代价是要把 prompt 解析回来（`parse`），而解析器有解析器的错。**因此有第 0 条**
 * （`RULES.parsable`）：一条也解析不出来的 prompt 直接算违反——**一道从不失败的闸门
 * 等于没有闸门**，空转的解析器就是那种闸门。
 *
 * ## 它跑在哪
 *
 * - `invariants.test.ts`：committed 固件 + **逐条反向自证**（每条不变量都有一个把好句子
 *   改坏的变异，改完必须当场被点名抓住）。
 * - `web/scripts/verify-invariants.mjs`：**扫一批种子的真实对局**，逐手渲染两档再逐条断言。
 *
 * ## 它每一条各执行了几次（票 49）
 *
 * `promptAudit` 除了违反还报一份**逐条的执行次数**。理由是判据已经升级了
 * （DECISIONS「票 41 与 42 撞出一件事」）：以前只问「这道闸门失败过吗」，
 * 现在要多问一句**「它在真语料上执行过几次」**——反向自证只证明「这条断言写对了」，
 * 不证明「它有机会开口」。一条永远执行不到的断言，与一条从不失败的断言危害相同。
 *
 * 一次「执行」= 一次**有对象可判的求值**，逐条的口径写在各自的 `judge(...)` 那一行旁边：
 *
 * | 不变量 | 一次执行是什么 |
 * |---|---|
 * | prompt 解析得动 | 一份 prompt 解析一遍 |
 * | 吃恒来自上家 | 一组「吃」 |
 * | 副露来源的参照系 | 一组**写了来源**的副露（暗杠本来就没有来源） |
 * | 暗杠不带来源 | 一组副露 |
 * | 副露的张数 | 一组副露 |
 * | 河牌数与巡目 | 一家（读得出巡目的） |
 * | 宝牌指示牌数与杠数 | 一份 prompt |
 * | 立直后全摸切 | **立直宣言之后那一家打的每一张**（宣言牌不算） |
 * | 手牌张数 3n+1 / 3n+2 | 一家 |
 * | 可选动作的牌来自手牌 | 一条可选动作 |
 * | 同一牌种最多 4 张 | 一份 prompt 里数到的一个牌种 |
 * | 记法只有 mjai 一套 | 一个场风 / 一个自风 / 一条可选动作，加上每份 prompt 各一次全文扫描 |
 *
 * ## 它不管什么
 *
 * **措辞与结构不归它管**（那是 M2 三档对照实验的自由变量，票 41 明写不许改）：
 * 换抬头、换称呼、换人格都不该让这里变红——因此判据一律从 `PromptTemplate` 现取，
 * 而不是写死默认模板那几个字。真正写死的只有两处：`你`（渲染器里就是写死的身份词）
 * 与 `他的`（票 40 加的参照系声明，票 40 §8 已记着它该进模板）。
 */

import type { PromptTemplate } from "../../src/agent/template.ts";
import { DEFAULT_TEMPLATE } from "../../src/agent/template.ts";

// ---- 违反 ----

/**
 * 十一条不变量的名字（第 0 条是防空转那一条）。
 * **报错要点名是哪一条**，因此它们有名字而不是编号。
 */
export const RULES = {
  parsable: "prompt 解析得动",
  chiFromKamicha: "吃恒来自上家",
  nakiFrame: "副露来源的参照系",
  ankanHasNoSource: "暗杠不带来源",
  nakiTileCount: "副露的张数",
  kawaJunme: "河牌数与巡目",
  doraKan: "宝牌指示牌数与杠数",
  riichiTsumogiri: "立直后全摸切",
  tehaiCount: "手牌张数 3n+1 / 3n+2",
  actionTilesFromHand: "可选动作的牌来自手牌",
  fourOfAKind: "同一牌种最多 4 张",
  mjaiOnly: "记法只有 mjai 一套",
} as const;

/** 一条不成立的话。三项缺一不可：**哪条不变量、哪一手、哪句话**。 */
export interface Violation {
  /** `RULES` 里的一条。 */
  rule: string;
  /** 哪一手（种子 / 座位 / 手序 / 档位），由调用方给——文本自己说不出这些。 */
  where: string;
  /** 哪句话：prompt 里原样的那一行（或那一段）。 */
  sentence: string;
  /** 为什么它在日麻里不成立。 */
  detail: string;
}

/** 一行报错。`verify-invariants.mjs` 与用例的断言消息都用它，两处说法因此一致。 */
export function formatViolation(violation: Violation): string {
  return `[${violation.rule}] ${violation.where}：${violation.detail}\n    原文：${violation.sentence}`;
}

/** 逐条不变量**真的求值了几次**（票 49）。键是 `RULES` 里的一条，口径见文件开头那张表。 */
export type Judged = Record<string, number>;

/** 一份 prompt 判下来的全部：不成立的那几句话，加上每一条各执行了几次。 */
export interface Audit {
  violations: Violation[];
  judged: Judged;
}

// ---- 牌 ----

/** mjai 记法的一张牌。 */
const TILE = /^[1-9][mps]r?$|^[1-7]z$/;

/** 红宝牌与它的正牌是同一牌种（CONTEXT.md 的 Akadora）：数张数之前先去红。 */
function deaka(pai: string): string {
  return pai.endsWith("r") ? pai.slice(0, -1) : pai;
}

/**
 * **中文牌名里的数牌那一半**：`3索`、`赤5筒`（`Tile.toDisplay` 写出来就是这个形状）。
 *
 * 字牌那一半（`东` `白` `中`）**不能拿它扫全文**：它们同时是常用汉字，人格里的
 * 「中庸」与行文里的「其中」会当场假红。字牌由「每一处点名一张牌的地方都得是 mjai 记法」
 * 那几条守（手牌 / 牌河 / 副露 / 宝牌指示牌 / 场风与自风 / 可选动作那几行）。
 */
const CHINESE_SUITED = /赤?[0-9一二三四五六七八九][万萬筒饼餅索]/;

/**
 * **mjai 的动作 wire 名**：它们是数据层的词（事件流、牌谱、动作消息），
 * 不该出现在写给模型看的话里。真会漏进来的地方有两处：措辞表查不到时把 wire 值原样
 * 写出去（`wording.ts` 的 `lookup`），以及历史那一段遇上读不懂的事件
 * （`history.ts` 的 `（读不懂的事件：X）`）。
 */
const WIRE_WORDS =
  /\b(dahai|tsumogiri|tsumo|pon|chi|ankan|daiminkan|kakan|reach|hora|ryukyoku|none)\b/;

// ---- 解析回来 ----

/** 一组副露，从渲出来的那半句话里读回来。 */
interface ParsedNaki {
  /** 种类的中文（模板的 `wording.naki`）。 */
  kind: string;
  /** 种类的 wire 名（`pon` / `chi` / `ankan` / `daiminkan` / `kakan`）。 */
  wire: string;
  /** 鸣来的那张；暗杠没有。 */
  pai: string | null;
  /** 那句「来自X」里的 X；暗杠没有。 */
  from: string | null;
  /** 亮出的那几张。 */
  consumed: string[];
}

/** 一家：自家那一节与他家那一节读回来之后是同一个形状。 */
interface ParsedSeat {
  /** 这一节的抬头原文——报错时的「哪句话」。 */
  headline: string;
  /** 在这份 prompt 里的称呼：`你` / `下家` / `对家` / `上家`。 */
  who: string;
  isSelf: boolean;
  junme: number;
  /** 自风（抬头里那一格）。**它指的就是一张字牌**，因此记法那一条要看它。 */
  jikaze: string;
  /** 手里几张：自家数手牌那一行，他家读「手里 N 张」。 */
  tehaiCount: number;
  /** 自家的手牌（他家是空的——决策包里就没有）。 */
  tehai: string[];
  naki: ParsedNaki[];
  nakiLine: string;
  /** 河里的每一张（去掉摸切的 `*`）。 */
  kawa: string[];
  kawaLine: string;
}

/** 【可选动作】里的一行：id、那一行的 label、以及原文。 */
export interface ParsedAction {
  id: number;
  label: string;
  line: string;
}

interface Parsed {
  /** 【到目前为止你看到的】那一段，一条事件一行。 */
  history: string[];
  dora: string[];
  boardLine: string;
  /** 场况那一节第一行（场风与局数）的原文。 */
  bakazeLine: string;
  /** 场风。**它指的也是一张字牌**。 */
  bakaze: string;
  seats: ParsedSeat[];
  actions: ParsedAction[];
  /** 解析不动的地方。**它非空就是第 0 条违反**。 */
  problems: string[];
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/** 模板给的那几个副露中文词 → wire 名。**判据从模板现取**，换措辞不该让闸门失灵。 */
function nakiKinds(template: PromptTemplate): Map<string, string> {
  return new Map(Object.entries(template.wording.naki).map(([wire, kind]) => [kind, wire]));
}

/**
 * 一节里的那几组副露：`碰 2z（来自你，亮出 2z 2z），吃 3m（来自他的上家，亮出 4m 5mr）`。
 *
 * 空的那一节写「无」，解析出来就是空表。
 */
function parseNaki(value: string, template: PromptTemplate): ParsedNaki[] {
  const kinds = nakiKinds(template);
  const alternatives = [...kinds.keys()].map(escapeRegExp).join("|");
  const pattern = new RegExp(
    `(${alternatives})(?: ([^（）\\s]+))?（(?:来自([^，）]+)，)?亮出 ([^）]*)）`,
    "g",
  );

  return [...value.matchAll(pattern)].map((group) => ({
    kind: group[1],
    wire: kinds.get(group[1]) ?? group[1],
    pai: group[2] ?? null,
    from: group[3] ?? null,
    consumed: group[4] === "" ? [] : group[4].split(" "),
  }));
}

/** 河：`2m 9m 2m* 2z`，摸切的那张缀 `*`；空的那一行写「空」。 */
function parseKawa(value: string): string[] {
  if (value === "空") return [];
  return value.split(" ").map((entry) => (entry.endsWith("*") ? entry.slice(0, -1) : entry));
}

function readNumber(line: string, pattern: RegExp): number {
  const found = pattern.exec(line);
  return found === null ? Number.NaN : Number(found[1]);
}

/**
 * 把渲出来的 prompt 解析回一个结构。
 *
 * **抬头从模板取**（`labels.*`），因此换了抬头的模板照样解析得动；段内那几行
 * （`手牌：…` / `副露：…` / `牌河：…`）是渲染器写死的形状（票 31：槽位切在段落这一级），
 * 这里就按那个形状读。读不出来的都进 `problems`——**不许静静地当作没有**。
 */
function parse(prompt: string, template: PromptTemplate): Parsed {
  const labels = template.labels;
  const lines = prompt.split("\n");
  const problems: string[] = [];

  const at = (label: string, what: string): number => {
    const index = lines.indexOf(label);
    if (index < 0) problems.push(`找不到${what}那一节的抬头「${label}」`);
    return index;
  };

  const historyAt = at(labels.history, "历史");
  const boardAt = at(labels.board, "场况");
  const handAt = at(labels.hand, "自家");
  const othersAt = at(labels.others, "他家");
  const actionsAt = at(labels.actions, "可选动作");

  const parsed: Parsed = {
    history: [],
    dora: [],
    boardLine: "",
    bakazeLine: "",
    bakaze: "",
    seats: [],
    actions: [],
    problems,
  };

  if (historyAt >= 0 && boardAt > historyAt) {
    parsed.history = lines.slice(historyAt + 1, boardAt).filter((line) => line !== "");
    if (parsed.history.length === 0) problems.push("历史那一段一行都没有");
  }

  // 场况第一行：`场风 1z・第 1 局 0 本场，供托 0 根。`（票 95：场风也是 mjai 记法）。
  // 第二行：`宝牌指示牌：2s 8m　牌山剩余可摸 23 张。`（两项之间是全角空格）。
  if (boardAt >= 0) {
    parsed.bakazeLine = lines[boardAt + 1] ?? "";
    const bakaze = /^场风 (\S+)・第 \d+ 局/.exec(parsed.bakazeLine);
    if (bakaze === null) problems.push(`场况那一节读不出场风：「${parsed.bakazeLine}」`);
    else parsed.bakaze = bakaze[1];

    parsed.boardLine = lines[boardAt + 2] ?? "";
    const dora = /^宝牌指示牌：([^　]*)　/.exec(parsed.boardLine);
    if (dora === null) problems.push(`场况那一节读不出宝牌指示牌：「${parsed.boardLine}」`);
    else parsed.dora = dora[1] === "无" ? [] : dora[1].split(" ");
  }

  // 自家：抬头之后六行（座位 / 手牌 / 刚摸进 / 副露 / 牌河 / 立直）。
  if (handAt >= 0) {
    const headline = lines[handAt + 1] ?? "";
    const tehaiLine = lines[handAt + 2] ?? "";
    const tehai = /^手牌：(.*)（(\d+) 张）$/.exec(tehaiLine);
    const naki = /^副露：(.*)$/.exec(lines[handAt + 4] ?? "");
    const kawa = /^牌河：(.*)$/.exec(lines[handAt + 5] ?? "");

    const jikaze = /（自风 ([^）]+)）/.exec(headline);

    if (tehai === null || naki === null || kawa === null || jikaze === null) {
      problems.push(`自家那一节的形状不对：「${headline}」「${tehaiLine}」`);
    } else {
      const tiles = tehai[1] === "" ? [] : tehai[1].split(" ");
      parsed.seats.push({
        headline,
        who: "你",
        isSelf: true,
        junme: readNumber(headline, /第 (\d+) 巡/),
        jikaze: jikaze[1],
        // 声明的那个张数与真列出来的那几张**分别记**：两者不等本身就是一条违反。
        tehaiCount: Number(tehai[2]),
        tehai: tiles,
        naki: parseNaki(naki[1], template),
        nakiLine: lines[handAt + 4] ?? "",
        kawa: parseKawa(kawa[1]),
        kawaLine: lines[handAt + 5] ?? "",
      });
    }
  }

  // 他家：抬头之后每三行一家，到空行为止。
  if (othersAt >= 0) {
    for (let index = othersAt + 1; index < lines.length; index += 3) {
      const headline = lines[index] ?? "";
      if (headline === "") break;

      const who = /^(.+?)（座位 \d+・/.exec(headline);
      const count = /手里 (\d+) 张/.exec(headline);
      const jikaze = /^.+?（座位 \d+・自风 ([^）]+)）/.exec(headline);
      const naki = /^\s*副露：(.*)$/.exec(lines[index + 1] ?? "");
      const kawa = /^\s*牌河：(.*)$/.exec(lines[index + 2] ?? "");

      if (who === null || count === null || jikaze === null || naki === null || kawa === null) {
        problems.push(`他家那一节的形状不对：「${headline}」`);
        break;
      }

      parsed.seats.push({
        headline,
        who: who[1],
        isSelf: false,
        junme: readNumber(headline, /第 (\d+) 巡/),
        jikaze: jikaze[1],
        tehaiCount: Number(count[1]),
        tehai: [],
        naki: parseNaki(naki[1], template),
        nakiLine: lines[index + 1] ?? "",
        kawa: parseKawa(kawa[1]),
        kawaLine: lines[index + 2] ?? "",
      });
    }
  }

  if (actionsAt >= 0) {
    for (let index = actionsAt + 1; index < lines.length; index += 1) {
      const option = /^- id=(\d+)：(.+)$/.exec(lines[index] ?? "");
      if (option === null) break;
      parsed.actions.push({ id: Number(option[1]), label: option[2], line: lines[index] });
    }
    if (parsed.actions.length === 0) problems.push("可选动作那一节一条都没有");
  }

  if (parsed.seats.length === 0) problems.push("一家都没解析出来");

  return parsed;
}

// ---- 十条规则不变量（第 0 条在 `promptViolations` 里） ----

interface Check {
  where: string;
  /** 渲出来那份 prompt 的原文。**记法那一条要扫全文**，别的几条读的是解析回来的结构。 */
  prompt: string;
  parsed: Parsed;
  template: PromptTemplate;
  violations: Violation[];
  judged: Map<string, number>;
}

function report(check: Check, rule: string, sentence: string, detail: string): void {
  check.violations.push({ rule, where: check.where, sentence, detail });
}

/**
 * 这一条不变量在这里**真的对着一个对象求了一次值**（票 49）。
 *
 * 与 `report` 成对：那个记「它红了几次」，这个记「它张口说话的机会有几次」。
 * **不该写在循环外面**：语料里一组副露都没有时，副露那几条的次数就该是 0。
 */
function judge(check: Check, rule: string, times = 1): void {
  check.judged.set(rule, (check.judged.get(rule) ?? 0) + times);
}

/** 副露里那句「来自谁」，落在四种形态之一。 */
type Frame =
  | { kind: "none" }
  | { kind: "self" }
  | { kind: "viewer"; relative: string }
  | { kind: "owner"; relative: string };

/** 「你」是身份不是方位；「他的X」是就地声明的参照系（票 40）；光写 X 的参照系是观测者。 */
function frameOf(from: string | null): Frame {
  if (from === null) return { kind: "none" };
  if (from === "你") return { kind: "self" };
  if (from.startsWith("他的")) return { kind: "owner", relative: from.slice(2) };
  return { kind: "viewer", relative: from };
}

/**
 * 一、**吃恒来自上家**（`Naki.chi` 的不变量，`Action.fs`）。
 *
 * 票 40 那句「吃 2p（来自对家）」在日麻里不成立，而决策包里的绝对座位完全正确、
 * 前缀字节也单调——两道既有闸门都绿。这一条只看那句中文本身。
 *
 * 三种写法各自都得对得上：自家那组吃写「来自上家」（参照系是观测者，两者重合）；
 * 他家那组吃写「来自他的上家」；鸣的是观测者打的那张时写「来自你」，
 * 而那**只可能出现在下家那一节**——只有观测者的下家吃得了观测者打的牌。
 */
function checkChi(check: Check): void {
  const relative = check.template.wording.relative;
  const kamicha = relative["3"];
  const shimocha = relative["1"];

  for (const seat of check.parsed.seats) {
    for (const naki of seat.naki) {
      if (naki.wire !== "chi") continue;
      judge(check, RULES.chiFromKamicha);
      const frame = frameOf(naki.from);

      if (frame.kind === "self") {
        if (seat.isSelf || seat.who !== shimocha) {
          report(
            check,
            RULES.chiFromKamicha,
            seat.nakiLine,
            `${seat.who}那组「${naki.kind}」写着「来自你」：吃只吃得了上家，而${seat.who}的上家不是你`,
          );
        }
        continue;
      }

      const said = frame.kind === "none" ? "（没写来源）" : `来自${naki.from}`;
      if (frame.kind === "none" || frame.relative !== kamicha) {
        report(
          check,
          RULES.chiFromKamicha,
          seat.nakiLine,
          `${seat.who}那组「${naki.kind}」写着「${said}」：吃只吃得了${kamicha}`,
        );
      }
    }
  }
}

/**
 * 二、**副露来源的参照系**（票 40 的那个错，从文本这一侧抓）。
 *
 * 固定 preamble 宣告了整份 prompt 的默认参照系是**观测者**（「别家按相对位置称呼」），
 * 因此：
 *
 * - **自家**那几组的来源相对观测者算，写光秃秃的方位词就对（两个参照系重合），
 *   但**绝不能是「你」**——自己鸣不了自己打的牌。
 * - **他家**那几组的来源相对**副露方**算，因此必须就地声明「他的」；写光秃秃的方位词
 *   就会被按宣告的读法解出另一家来（票 40 那句错话）。
 * - 数不出相对位置而退回「座位 N」的，也算这一条没成立：那时读者手上没有参照系。
 */
function checkNakiFrame(check: Check): void {
  const relatives = new Set(Object.values(check.template.wording.relative));

  for (const seat of check.parsed.seats) {
    for (const naki of seat.naki) {
      const frame = frameOf(naki.from);
      // 暗杠本来就没有来源（那是上一条的事），这一条在它身上无从求值。
      if (frame.kind === "none") continue;
      judge(check, RULES.nakiFrame);

      if (seat.isSelf) {
        if (frame.kind === "self") {
          report(
            check,
            RULES.nakiFrame,
            seat.nakiLine,
            `自家那组「${naki.kind}」写着「来自你」：鸣不了自己打的牌`,
          );
        } else if (frame.kind === "owner") {
          report(
            check,
            RULES.nakiFrame,
            seat.nakiLine,
            `自家那组「${naki.kind}」写着「来自${naki.from}」：副露方就是观测者，不该多一层「他的」`,
          );
        } else if (!relatives.has(frame.relative)) {
          report(
            check,
            RULES.nakiFrame,
            seat.nakiLine,
            `自家那组「${naki.kind}」的来源「${naki.from}」不是一个相对位置——参照系数不出来了`,
          );
        }
        continue;
      }

      if (frame.kind === "viewer") {
        report(
          check,
          RULES.nakiFrame,
          seat.nakiLine,
          `${seat.who}那组「${naki.kind}」写着「来自${naki.from}」：他家那几组的参照系是副露方，` +
            `不声明就会被按 preamble 宣告的读法（相对观测者）读成另一家`,
        );
      } else if (frame.kind === "owner" && !relatives.has(frame.relative)) {
        report(
          check,
          RULES.nakiFrame,
          seat.nakiLine,
          `${seat.who}那组「${naki.kind}」的来源「${naki.from}」不是一个相对位置——参照系数不出来了`,
        );
      }
    }
  }
}

/**
 * 三、**暗杠不带来源**：暗杠是从自己手里杠的，没有「来自谁」这回事；
 * 其余四种恒有来源（碰、吃、大明杠鸣的是别人打的那张，加杠的底是当初那组碰）。
 */
function checkAnkanSource(check: Check): void {
  for (const seat of check.parsed.seats) {
    for (const naki of seat.naki) {
      judge(check, RULES.ankanHasNoSource);
      const concealed = naki.wire === "ankan";
      if (concealed && (naki.from !== null || naki.pai !== null)) {
        report(
          check,
          RULES.ankanHasNoSource,
          seat.nakiLine,
          `${seat.who}那组「${naki.kind}」写了来源「${naki.from ?? naki.pai}」：暗杠是从自己手里杠的`,
        );
      }
      if (!concealed && naki.from === null) {
        report(
          check,
          RULES.ankanHasNoSource,
          seat.nakiLine,
          `${seat.who}那组「${naki.kind}」没写来源：只有暗杠没有来源`,
        );
      }
    }
  }
}

/** 四、**副露的张数**：碰 / 吃亮 2 张、大明杠亮 3 张、暗杠亮 4 张、加杠亮 3 张。 */
const CONSUMED_COUNT: Record<string, number> = {
  pon: 2,
  chi: 2,
  daiminkan: 3,
  ankan: 4,
  kakan: 3,
};

function checkNakiTileCount(check: Check): void {
  for (const seat of check.parsed.seats) {
    for (const naki of seat.naki) {
      judge(check, RULES.nakiTileCount);
      const wanted = CONSUMED_COUNT[naki.wire];
      if (wanted !== undefined && naki.consumed.length !== wanted) {
        report(
          check,
          RULES.nakiTileCount,
          seat.nakiLine,
          `${seat.who}那组「${naki.kind}」亮出 ${naki.consumed.length} 张：该是 ${wanted} 张`,
        );
      }
      for (const pai of [...naki.consumed, ...(naki.pai === null ? [] : [naki.pai])]) {
        if (!TILE.test(pai)) {
          report(
            check,
            RULES.nakiTileCount,
            seat.nakiLine,
            `${seat.who}那组「${naki.kind}」里的「${pai}」不是一张牌`,
          );
        }
      }
    }
  }
}

/**
 * 五、**河牌数与巡目**：`河牌数 = 巡目 + 碰吃组数 - 暗杠组数`，或者比它少一张（轮到那家还没打）。
 *
 * 从「每打一张牌之前发生了什么」数出来的（`GameState` 的事件序）：
 *
 * | 那一家做了什么 | 摸牌（巡目） | 打牌（河） |
 * |---|---|---|
 * | 平常一巡 | 摸 1 | 打 1 |
 * | 碰 / 吃 | **不摸** | 打 1 |
 * | 大明杠 | 岭上摸 1 | 打 1 |
 * | 暗杠 / 加杠 | 摸 1 **加**岭上摸 1 | 打 1 |
 *
 * **巡目数的是这家自己摸过几次**（CONTEXT.md 的 Junme，岭上那张也算），因此碰吃各多出一次打牌，
 * 暗杠各多出一次摸牌。加杠两头都占：底下那组碰多一次打牌、杠本身多一次摸牌，正好抵掉,
 * 所以式子里它一次都不出现——**那组碰已经被改画成加杠了，不会再数一遍**。
 * 差的那一张是「轮到它、摸完了还没打」，至多一张。
 */
function checkKawaJunme(check: Check): void {
  for (const seat of check.parsed.seats) {
    if (Number.isNaN(seat.junme)) continue;
    judge(check, RULES.kawaJunme);

    const kinds = (...wires: string[]) =>
      seat.naki.filter((naki) => wires.includes(naki.wire)).length;
    const called = kinds("pon", "chi");
    const ankan = kinds("ankan");
    const upper = seat.junme + called - ankan;

    if (seat.kawa.length > upper || seat.kawa.length < upper - 1) {
      report(
        check,
        RULES.kawaJunme,
        `${seat.headline}\n    ${seat.kawaLine}`,
        `${seat.who}第 ${seat.junme} 巡、碰吃 ${called} 组、暗杠 ${ankan} 组，河里却有 ${seat.kawa.length} 张：` +
          `该是 ${Math.max(upper - 1, 0)} 或 ${upper} 张（一摸一打；鸣了不摸也要打，杠了多摸一张）`,
      );
    }

    for (const pai of seat.kawa) {
      if (!TILE.test(pai)) {
        report(check, RULES.kawaJunme, seat.kawaLine, `${seat.who}河里的「${pai}」不是一张牌`);
      }
    }
  }
}

/**
 * 六、**宝牌指示牌数与杠数**：开局翻一张，之后一个杠一张。
 *
 * 时机分两种（`GameState.completeKan`，13 票在 218 局真实牌谱上实证）：暗杠**当场翻**，
 * 明杠（大明杠 / 加杠）**欠到打牌那一刻**。因此明杠可以欠着，暗杠不会——
 * 全是暗杠时张数是死的等式，有明杠时才允许少一张。
 */
function checkDoraKan(check: Check): void {
  const naki = check.parsed.seats.flatMap((seat) => seat.naki);
  const kan = naki.filter((group) => ["ankan", "daiminkan", "kakan"].includes(group.wire)).length;
  const open = naki.filter((group) => ["daiminkan", "kakan"].includes(group.wire)).length;
  const count = check.parsed.dora.length;
  const lowest = Math.max(1, kan + 1 - open);
  judge(check, RULES.doraKan);

  if (count < lowest || count > kan + 1) {
    const range = lowest === kan + 1 ? `${lowest}` : `${lowest} 或 ${kan + 1}`;
    report(
      check,
      RULES.doraKan,
      check.parsed.boardLine,
      `桌面上 ${kan} 个杠（其中明杠 ${open} 个），宝牌指示牌却有 ${count} 张：该是 ${range} 张` +
        `（开局一张 + 一杠一张；明杠那张欠到打牌才翻，暗杠当场翻）`,
    );
  }

  for (const pai of check.parsed.dora) {
    if (!TILE.test(pai)) {
      report(check, RULES.doraKan, check.parsed.boardLine, `宝牌指示牌「${pai}」不是一张牌`);
    }
  }
}

/**
 * 七、**立直后全摸切**：宣言那一张之后，那一家再打出来的每一张都得是摸切。
 *
 * 立直之后手牌不能动（能动的只有符合条件的暗杠，而那之后打的是岭上摸进的那张，
 * 仍旧是摸切）。这一条读的是【到目前为止你看到的】那一段——**历史那几行本身就得成立**，
 * 它是模型推别家听什么牌的主要依据。
 */
function checkRiichiTsumogiri(check: Check): void {
  const who = whoPattern(check.template);
  const declared = new Set<string>();
  const pending = new Set<string>();

  for (const line of check.parsed.history) {
    const riichi = new RegExp(`^(${who}) 第\\d+巡 宣言立直$`).exec(line);
    if (riichi !== null) {
      declared.add(riichi[1]);
      pending.add(riichi[1]);
      continue;
    }

    const dahai = new RegExp(`^(${who}) 第\\d+巡 打 (\\S+)$`).exec(line);
    if (dahai === null) continue;

    const actor = dahai[1];
    if (!declared.has(actor)) continue;
    // 宣言牌是宣言之后的第一张，它本来就可以手切。
    if (pending.delete(actor)) continue;
    // 走到这里才真的在判一张牌：语料里没立直时它一次也不执行（票 41 的原状）。
    judge(check, RULES.riichiTsumogiri);

    if (!dahai[2].endsWith("*")) {
      report(
        check,
        RULES.riichiTsumogiri,
        line,
        `${actor}已经立直了，这一张却是手切：立直之后手牌动不了，打的只能是刚摸进那张`,
      );
    }
  }
}

/** 这份 prompt 里那几个称呼（`你` 与模板给的相对位置词），拼成一段正则。 */
function whoPattern(template: PromptTemplate): string {
  const names = ["你", ...Object.values(template.wording.relative)];
  return [...names.map(escapeRegExp), "座位 \\d+"].join("|");
}

/**
 * 八、**手牌张数**：`手里的张数 + 3 × 副露组数` 恒是 13（等着别人打）或 14（刚摸进 / 刚鸣完）。
 *
 * 杠在这条式子里也按 3 算——多出来的那一张是岭上补摸抵掉的。
 * 顺带钉住自家那一行**声明的张数与真列出来的那几张**一致（`手牌：… （13 张）`）。
 */
function checkTehaiCount(check: Check): void {
  for (const seat of check.parsed.seats) {
    judge(check, RULES.tehaiCount);

    if (seat.isSelf && seat.tehai.length !== seat.tehaiCount) {
      report(
        check,
        RULES.tehaiCount,
        `${seat.headline}\n    手牌：${seat.tehai.join(" ")}`,
        `手牌那一行写着 ${seat.tehaiCount} 张，实际列了 ${seat.tehai.length} 张`,
      );
    }

    const total = seat.tehaiCount + 3 * seat.naki.length;
    if (total !== 13 && total !== 14) {
      report(
        check,
        RULES.tehaiCount,
        `${seat.headline}\n    ${seat.nakiLine}`,
        `${seat.who}手里 ${seat.tehaiCount} 张 + 副露 ${seat.naki.length} 组 × 3 = ${total}：` +
          `一手牌只能是 13 张（等打）或 14 张（刚摸进 / 刚鸣完）`,
      );
    }

    for (const pai of seat.tehai) {
      if (!TILE.test(pai)) {
        report(check, RULES.tehaiCount, seat.headline, `手牌里的「${pai}」不是一张牌`);
      }
    }
  }
}

/**
 * 一条可选动作那一行拆开：**它是哪一种**、**它点名了哪几张牌**、**其中哪几张必须出自你手里**。
 *
 * 句式由渲染器定（`src/agent/action-label.ts`），五种副露的词从**模板现取**——换措辞不该
 * 让闸门失灵（与 `parseNaki` 同一个方针）。拆不动返回 null：**闸门读不懂的动作等于没被检查**，
 * 那本身就是一条违反。
 */
export interface ParsedLabel {
  /** 种类的规范名，与 `label-crosscheck.ts` 那一侧取的是同一套值。 */
  kind: string;
  /** 这一行点名的每一张牌（摸切的 `*` 已经去掉）。**记法那一条看的就是它**。 */
  named: string[];
  /** 其中**必须出自你手牌**的那几张。 */
  fromHand: string[];
}

export function parseActionLabel(label: string, template: PromptTemplate): ParsedLabel | null {
  const kinds = nakiKinds(template);
  const alternatives = [...kinds.keys()].map(escapeRegExp).join("|");

  // 碰 / 吃 / 大明杠：`碰 1z（亮出 1z 1z）`；暗杠没有鸣来的那张：`暗杠（亮出 5s 5s 5s 5s）`。
  const shown = new RegExp(`^(${alternatives})(?: ([^（）\\s]+))?（亮出 ([^）]*)）$`).exec(label);
  if (shown !== null) {
    const consumed = shown[3] === "" ? [] : shown[3].split(" ");
    const taken = shown[2] === undefined ? [] : [shown[2]];
    // 亮出来的那几张出自手牌；鸣来的那张来自别人的河（点名了，但不出自你手里）。
    return {
      kind: kinds.get(shown[1]) ?? shown[1],
      named: [...taken, ...consumed],
      fromHand: consumed,
    };
  }

  // 加杠：底下那组碰已经在牌桌上，只写加上去的那张。
  const bare = new RegExp(`^(${alternatives}) (\\S+)$`).exec(label);
  if (bare !== null) {
    return { kind: kinds.get(bare[1]) ?? bare[1], named: [bare[2]], fromHand: [bare[2]] };
  }

  // 打牌：摸切缀 `*`（与牌河、历史同一个记号）。
  const dahai = /^打 (\S+?)(\*?)$/.exec(label);
  if (dahai !== null) {
    return { kind: dahai[2] === "*" ? "dahai*" : "dahai", named: [dahai[1]], fromHand: [dahai[1]] };
  }

  const tsumo = /^自摸 (\S+)$/.exec(label);
  if (tsumo !== null) return { kind: "hora-tsumo", named: [tsumo[1]], fromHand: [tsumo[1]] };

  // 荣和的那张来自别人的河：点名了它，但它不出自你的手牌。
  const ron = /^荣和 (\S+)$/.exec(label);
  if (ron !== null) return { kind: "hora-ron", named: [ron[1]], fromHand: [] };

  const plain: Record<string, string> = { 立直宣言: "reach", 过: "none", 九种九牌: "ryukyoku" };
  const kind = plain[label];
  return kind === undefined ? null : { kind, named: [], fromHand: [] };
}

/**
 * 九、**可选动作的牌来自手牌**：动作那一行点名的「你要出的牌」，
 * 每一张都得在【你的手牌】那一节里，且**张数够**。
 *
 * 打出去的那张、吃碰杠要亮出来的那几张、加杠加上去的那张、自摸和了的那张，
 * 全都出自你的手牌；只有荣和的那张与鸣来的那张来自别人的河。**这一条是拿多重集比的**：
 * 暗杠要亮四张，手里就得真有四张。
 *
 * 票 95 之前动作那一行写的是中文牌名（引擎渲的 `Action.toDisplay`），这一条因此要隔着
 * 一份中文↔mjai 的表来比；现在两侧都是 mjai，直接比。**那份表没被删掉，搬去当第三锚点了**
 * （`label-crosscheck.ts`：渲出来那一行与引擎那份中文 label 逐条对拍）。
 */
function checkActionTiles(check: Check): void {
  const self = check.parsed.seats.find((seat) => seat.isSelf);
  if (self === undefined) return;

  for (const option of check.parsed.actions) {
    judge(check, RULES.actionTilesFromHand);
    const parsed = parseActionLabel(option.label, check.template);
    if (parsed === null) {
      report(
        check,
        RULES.actionTilesFromHand,
        option.line,
        `这条动作那一行认不出形状——闸门读不懂的动作等于没被检查`,
      );
      continue;
    }

    const remaining = [...self.tehai];
    for (const pai of parsed.fromHand) {
      const index = remaining.indexOf(pai);
      if (index < 0) {
        report(
          check,
          RULES.actionTilesFromHand,
          `${option.line}\n    手牌：${self.tehai.join(" ")}`,
          `这条动作要出的「${pai}」不在你的手牌里`,
        );
        continue;
      }
      remaining.splice(index, 1);
    }
  }
}

/**
 * 十、**同一牌种最多 4 张**：把这份 prompt 里**看得见的每一张牌**数一遍
 * （自家手牌 + 四家的河 + 四家亮出来的副露 + 宝牌指示牌），没有哪一种能超过 4 张。
 *
 * 红宝牌与正牌同种（先 `deaka`）。**被鸣走的那张仍留在打牌者的河里**（CONTEXT.md 的 Kawa），
 * 因此副露只数「自家出的那几张」：碰 / 吃 / 大明杠数亮出来的，暗杠数那四张，
 * 加杠数**加上去的那张 + 亮出来的后两张**——亮出来的第一张就是当初被碰的那张，
 * 它还在别人的河里躺着（`Naki.fromKawa`），数两遍就凭空多一张。
 */
function checkFourOfAKind(check: Check): void {
  const counts = new Map<string, number>();
  const seen = new Map<string, string>();

  const count = (pai: string, sentence: string) => {
    const kind = deaka(pai);
    counts.set(kind, (counts.get(kind) ?? 0) + 1);
    if (!seen.has(kind)) seen.set(kind, sentence);
  };

  for (const pai of check.parsed.dora) count(pai, check.parsed.boardLine);

  for (const seat of check.parsed.seats) {
    for (const pai of seat.tehai) count(pai, `手牌：${seat.tehai.join(" ")}`);
    for (const pai of seat.kawa) count(pai, seat.kawaLine);

    for (const naki of seat.naki) {
      const mine =
        naki.wire === "kakan"
          ? [...(naki.pai === null ? [] : [naki.pai]), ...naki.consumed.slice(1)]
          : naki.consumed;
      for (const pai of mine) count(pai, seat.nakiLine);
    }
  }

  // 一个牌种算一次：数得出牌来这一条才有对象。
  judge(check, RULES.fourOfAKind, counts.size);

  for (const [kind, total] of counts) {
    if (total > 4) {
      report(
        check,
        RULES.fourOfAKind,
        seen.get(kind) ?? "",
        `这份 prompt 里看得见 ${total} 张 ${kind}：一种牌总共只有 4 张`,
      );
    }
  }
}

/**
 * 十一、**记法只有 mjai 一套**（票 95 还的那笔债）。
 *
 * 从 M1 挂到 M3 的那笔债是：同一份 prompt 里牌有两种写法——手牌 / 牌河 / 副露 / 宝牌指示牌 /
 * 历史那几行是 mjai（`3s`），而可选动作那几行是引擎渲的中文牌名（`手切3索`），
 * 场风与自风又是第三种（`东1局`、`北家`）。**模型要在几套记法之间翻译，而它翻错的时候
 * 我们只看得到「它打错了」。**
 *
 * 这一条从三个方向守住「只有一套」：
 *
 * 1. **场风与自风**：它们指的就是手牌里那张字牌，必须写成 `1z`-`4z`；
 * 2. **可选动作那几行点名的每一张牌**：必须是 mjai 记法（那一行现在由 TS 渲，
 *    认不出动作时会退回引擎那份中文 label——**退回去是看得见的**，这一条当场红）；
 * 3. **全文两遍扫描**：中文数牌名（`3索` / `赤5筒`）与 mjai 的动作 wire 名
 *    （`dahai` / `pon` / …）一个都不许出现。后者真会漏进来：措辞表查不到时
 *    `wording.ts` 的 `lookup` 把 wire 值原样写出去，历史那一段遇上读不懂的事件也一样。
 *
 * 手牌 / 牌河 / 副露 / 宝牌指示牌那四处的记法**早就各有一条在守**（第四、五、六、八条里
 * 那几行 `TILE.test`），这一条不重复数它们。**字牌的中文名扫不了全文**：`中`、`东` 同时
 * 是常用汉字（人格里的「中庸」），因此它们只在「点名一张牌的地方」被查——
 * 而那些地方这里与上面那四条加起来是全的。
 */
function checkMjaiOnly(check: Check): void {
  const parsed = check.parsed;

  const kaze = (value: string, sentence: string, what: string) => {
    judge(check, RULES.mjaiOnly);
    if (value !== "" && !TILE.test(value)) {
      report(
        check,
        RULES.mjaiOnly,
        sentence,
        `${what}写成了「${value}」：它指的就是手牌里那张字牌，该写 mjai 记法（\`1z\`-\`4z\`）`,
      );
    }
  };

  kaze(parsed.bakaze, parsed.bakazeLine, "场风");
  for (const seat of parsed.seats) kaze(seat.jikaze, seat.headline, `${seat.who}的自风`);

  for (const option of parsed.actions) {
    judge(check, RULES.mjaiOnly);
    const label = parseActionLabel(option.label, check.template);
    // 拆不动是第九条的事（那一条会点名它），这里只判拆得动的那几张。
    for (const pai of label?.named ?? []) {
      if (!TILE.test(pai)) {
        report(
          check,
          RULES.mjaiOnly,
          option.line,
          `这一行里的「${pai}」不是 mjai 记法：整份 prompt 只该有 \`3s\` / \`5mr\` 那一套`,
        );
      }
    }
  }

  judge(check, RULES.mjaiOnly);
  const chinese = CHINESE_SUITED.exec(check.prompt);
  if (chinese !== null) {
    report(
      check,
      RULES.mjaiOnly,
      lineAround(check.prompt, chinese.index),
      `prompt 里出现了中文牌名「${chinese[0]}」：中文只活在给人看的那一侧（ADR-0001）`,
    );
  }

  judge(check, RULES.mjaiOnly);
  const wire = WIRE_WORDS.exec(check.prompt);
  if (wire !== null) {
    report(
      check,
      RULES.mjaiOnly,
      lineAround(check.prompt, wire.index),
      `prompt 里出现了 mjai 的动作 wire 名「${wire[0]}」：那是数据层的词，不是写给模型看的话`,
    );
  }
}

/** 报错要抄出那句原话：把命中的位置扩到整行。 */
function lineAround(prompt: string, index: number): string {
  const start = prompt.lastIndexOf("\n", index) + 1;
  const end = prompt.indexOf("\n", index);
  return prompt.slice(start, end < 0 ? undefined : end);
}

// ---- 入口 ----

export interface CheckOptions {
  /** 哪一手：种子 / 座位 / 手序 / 档位。报错要点名它。 */
  where: string;
  /** 判据从模板取，不写死默认模板那几个字。 */
  template?: PromptTemplate;
}

/**
 * 一份渲出来的 prompt：**不成立的那几句话**，加上**每一条各执行了几次**。
 *
 * 违反为空就是这一手过了。**不抛**：一次扫一批局面，一句坏话不该让扫描停在那里。
 *
 * 执行次数要与违反一起看（票 49）：**执行次数为 0 的那几条，这一趟的「0 违反」不是证据**。
 */
export function promptAudit(prompt: string, options: CheckOptions): Audit {
  const template = options.template ?? DEFAULT_TEMPLATE;
  const parsed = parse(prompt, template);
  const check: Check = {
    where: options.where,
    prompt,
    parsed,
    template,
    violations: [],
    judged: new Map(),
  };

  // 第 0 条：解析不动就没有下文——**空转的解析器等于没有闸门**。
  judge(check, RULES.parsable);
  for (const problem of parsed.problems) {
    report(check, RULES.parsable, problem, "prompt 解析不动，底下十条因此一条也没验到");
  }
  if (parsed.problems.length === 0) {
    checkChi(check);
    checkNakiFrame(check);
    checkAnkanSource(check);
    checkNakiTileCount(check);
    checkKawaJunme(check);
    checkDoraKan(check);
    checkRiichiTsumogiri(check);
    checkTehaiCount(check);
    checkActionTiles(check);
    checkFourOfAKind(check);
    checkMjaiOnly(check);
  }

  return { violations: check.violations, judged: Object.fromEntries(check.judged) };
}

/** 【可选动作】那一节解析回来的那几行（`label-crosscheck.ts` 拿它与引擎那份 label 对拍）。 */
export function parsedActions(
  prompt: string,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): ParsedAction[] {
  return parse(prompt, template).actions;
}

/** 一份 prompt 里不成立的那几句话（只要这一半时用它）。 */
export function promptViolations(prompt: string, options: CheckOptions): Violation[] {
  return promptAudit(prompt, options).violations;
}

/**
 * 这一手真被验到了什么——**防空转**。
 *
 * 扫一批局面时光看「违反数为 0」是不够的：解析器读空了、语料里压根没有副露，
 * 得出来的也是 0。因此每一手顺带报一份计数，扫描器把它们加起来，
 * 数到 0 的那几项当场说出来（`verify-invariants.mjs` 的覆盖那一节）。
 */
export function promptCoverage(prompt: string, template: PromptTemplate = DEFAULT_TEMPLATE) {
  const parsed = parse(prompt, template);
  const naki = parsed.seats.flatMap((seat) => seat.naki);
  const kinds = (...wires: string[]) => naki.filter((group) => wires.includes(group.wire)).length;

  return {
    seats: parsed.seats.length,
    actions: parsed.actions.length,
    historyLines: parsed.history.length,
    naki: naki.length,
    chi: kinds("chi"),
    pon: kinds("pon"),
    ankan: kinds("ankan"),
    daiminkan: kinds("daiminkan"),
    kakan: kinds("kakan"),
    dora: parsed.dora.length,
    riichi: parsed.history.filter((line) => line.endsWith("宣言立直")).length,
  };
}
