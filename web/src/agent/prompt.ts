/**
 * prompt 的渲染（票 23，票 29b 把形态翻了过来）。
 * **prompt 在 TS 侧渲染，F# 只出结构化决策包**（ADR-0005）。
 *
 * ## 三段，前两段可缓存
 *
 * ```
 * ① 固定 preamble          规则与任务，逐字不变
 * ② 【到目前为止你看到的】   掩蔽事件流，append-only，既有的行永不改写   ← 前缀，provider 缓存吃这两段
 * ③ 【现在】+【可选动作】+（脚手架）+（重试原因）  每手重算，每手付全价   ← 尾部
 * ```
 *
 * 同一局里第 n 手的 ①②（`cacheablePrefix`）必须是第 n+1 手整份 prompt 的**字节前缀**，
 * 那条属性由 `tests/agent/prompt.test.ts` 守着。**前缀里因此不许出现会被重算的量**
 * ——当前巡目、牌山剩余、任何聚合数都只能待在尾部。
 *
 * 两种客观事实**都给**、位置不同（主人 8/16 第四次裁决）：时间上的事件流在前缀，
 * 空间上的场况在尾部。**重复是故意的**——尾部就是前缀那条流的 fold（29a 保证的一条掩蔽法则），
 * 记账负担不是我们想测的能力。
 *
 * 载体是**单条不断增长的 user message**，不是多轮对话：不把模型自己的旧理由锚进上下文。
 *
 * 牌一律用 mjai 记法（`3m` / `7z` / `5mr`）：决策包里就是这个形态，而**中文牌名要查术语表，
 * 那是 F# 渲染层的事**（ADR-0001）——需要中文的地方由决策包携带已经渲染好的字符串，
 * 动作的 `label` 就是这么来的。措辞常量（风、相对座位、副露）在 `wording.ts`，
 * **前缀与尾部共用一份**。
 *
 * **票 31 要把这三段降成数据**（模板 + 槽位 + system/人格）：接口就是下面的
 * `promptSections`——三段各自一个函数，谁也没焊死在一次字符串拼接里。
 */

import { historyLines } from "./history.ts";
import type { DecisionPackage, MaskedSeat, Naki, RevealedSeat, ScaffoldTier } from "./types.ts";
import { kaze, type Naming, nakiKind, namesFor, riichiState } from "./wording.ts";

/**
 * 决策包 `scaffold` 槽位里那几个数的形状（票 24）。F# 侧的对应物是 `Scaffold.encoder`。
 *
 * `types.ts` 把 `scaffold` 留作 `Record<string, unknown>` 是故意的：**档位会长**
 * （25 号票往里加 Danger），而少一节不该让整包读不动。因此这里用完一次就算一次：
 * `readScaffold` 读不出来就退回 Bare。
 */
interface ShantenView {
  /** 向听数：-1 和了、0 听牌、正数是离听牌的距离。 */
  value: number;
  /** 引擎渲染好的中文（ADR-0005：不让 TS 去查术语表，0 叫「听牌」不叫「0 向听」）。 */
  display: string;
}

interface UkeireView {
  /** 总枚数。 */
  total: number;
  /** 牌种数。**引擎算的**（`Ukeire.kindCount`）：数不数 0 枚的那几种是规则问题，不是数数组长度。 */
  kinds: number;
  /** 牌种（mjai 记法）与各自的剩余枚数；枚数可能是 0（已经全部可见）。 */
  tiles: { pai: string; remaining: number }[];
}

/** 一条带着引擎渲染好的中文的枚举值（ADR-0005 第 2 条）。 */
interface LabelledView {
  /** wire 上的名字，分组用。 */
  value: string;
  /** 引擎渲染好的中文，直接进 prompt。 */
  display: string;
}

/**
 * 一张牌的危险度（票 25）。**启发式，不是概率**：档位与理由都是引擎按
 * 现物 / 筋 / 壁 / 宝牌周边四条规则算出来的，这一层只把它们排成行。
 */
interface DangerView {
  tier: LabelledView;
  /** 这一手里的名次，1 最安全；**并列名次**（两张并列第一之后是第三）。 */
  rank: number;
  /** 理由标签，可以是空的（一条依据都没有）。 */
  reasons: LabelledView[];
}

/** 有威胁的一家：立直了或者有副露。空表时整份危险度不渲染。 */
interface ThreatView {
  seat: number;
  relative: number;
  riichi: boolean;
  naki: boolean;
  display: string;
}

interface DahaiView {
  pai: string;
  /** 打得出这一张的动作 id。**接头处是 id 不是牌**：赤 5 与正 5 是两条动作、一个牌种。 */
  action_ids: number[];
  shanten: ShantenView;
  /** 进退向：0 不变，+1 退向（向听戻し）。 */
  shanten_delta: number;
  ukeire: UkeireView | null;
  /** 危险度。**没人立直也没人副露时是 null**（那时排序没有被评价的对象）。 */
  danger?: DangerView | null;
}

interface ScaffoldView {
  shanten: ShantenView;
  /** 只有等摸形（还没摸进）才有；已摸进的手牌看 `dahai` 里逐条给的那些。 */
  ukeire: UkeireView | null;
  dahai: DahaiView[];
  /** 有威胁的家（票 25）。**可能没有这一项**：24 号票那份形状的包里就没有。 */
  threats?: ThreatView[];
}

/** 河：摸切的那几张后面缀一个 `*`（手切与摸切是公开信息）。 */
function kawa(entries: { pai: string; tsumogiri: boolean }[]): string {
  if (entries.length === 0) return "空";
  return entries.map((entry) => (entry.tsumogiri ? `${entry.pai}*` : entry.pai)).join(" ");
}

/**
 * 副露：**措辞与历史那一段同一套**（票 29b）——种类写中文（碰 / 吃 / 暗杠 / 大明杠 / 加杠），
 * 来源写相对位置而不是座位号，牌照 mjai 记法。
 */
function naki(groups: Naki[], naming: Naming): string {
  if (groups.length === 0) return "无";
  return groups
    .map((group) => {
      const taken = group.pai === null ? "" : ` ${group.pai}`;
      const from = group.target === null ? "" : `来自${naming.who(group.target)}，`;
      return `${nakiKind(group.type)}${taken}（${from}亮出 ${group.consumed.join(" ")}）`;
    })
    .join("，");
}

function marks(riichi: string, ippatsu: boolean): string {
  const state = riichiState(riichi);
  return ippatsu ? `${state}・一发` : state;
}

function self(seat: RevealedSeat, naming: Naming): string {
  const furiten = seat.furiten.permanent ? "是（永久）" : seat.furiten.doujun ? "是（同巡）" : "否";
  return [
    `你是座位 ${seat.seat}（${kaze(seat.jikaze)}家），第 ${seat.junme} 巡，${seat.score} 点。`,
    `手牌：${seat.tehai.join(" ")}（${seat.tehai.length} 张）`,
    `刚摸进：${seat.tsumo ?? "无（这一手不是你摸牌）"}`,
    `副露：${naki(seat.naki, naming)}`,
    `牌河：${kawa(seat.kawa)}`,
    `立直：${marks(seat.riichi, seat.ippatsu)}　振听：${furiten}`,
  ].join("\n");
}

function other(seat: MaskedSeat, naming: Naming): string {
  return [
    `${naming.who(seat.seat)}（座位 ${seat.seat}・${kaze(seat.jikaze)}家）：手里 ${seat.tehai_count} 张，第 ${seat.junme} 巡，${seat.score} 点，立直：${marks(seat.riichi, seat.ippatsu)}`,
    `  副露：${naki(seat.naki, naming)}`,
    `  牌河：${kawa(seat.kawa)}`,
  ].join("\n");
}

function board(decision: DecisionPackage): string {
  const observation = decision.observation;
  return [
    `${kaze(observation.bakaze)}${observation.kyoku}局 ${observation.honba} 本场，供托 ${observation.kyotaku} 根。`,
    `宝牌指示牌：${observation.dora_markers.join(" ") || "无"}　牌山剩余可摸 ${observation.wall_remaining} 张。`,
  ].join("\n");
}

function options(decision: DecisionPackage): string {
  return decision.actions.map((option) => `- id=${option.id}：${option.label}`).join("\n");
}

// ---- 脚手架（Assisted 档，票 24） ----

/** 脚手架读不读得出来。读不出来就是 null——调用方退回 Bare，不招呼、也不卡住。 */
function readScaffold(raw: Record<string, unknown>): ScaffoldView | null {
  const scaffold = raw as Partial<ScaffoldView>;
  if (typeof scaffold.shanten?.display !== "string") return null;
  if (!Array.isArray(scaffold.dahai)) return null;
  return scaffold as ScaffoldView;
}

/** 有效牌一行：`66 枚 19 种：3m(1) 4m(4) …`。括号里是那张牌的剩余枚数。 */
function ukeire(view: UkeireView): string {
  if (view.tiles.length === 0) return "无有效牌";
  const tiles = view.tiles.map((tile) => `${tile.pai}(${tile.remaining})`).join(" ");
  return `${view.total} 枚 ${view.kinds} 种：${tiles}`;
}

/** 一条试打。每一条**打牌动作**各占一行，行首就是它的 id——模型要回的正是那个 id。 */
function trial(view: DahaiView, label: string, id: number): string {
  const delta = view.shanten_delta === 0 ? "进退向 0" : `退向 +${view.shanten_delta}`;
  const acceptance = view.ukeire === null ? "" : `，有效牌 ${ukeire(view.ukeire)}`;
  return `- id=${id}（${label}）：打完 ${view.shanten.display}，${delta}${acceptance}`;
}

/** 一条危险度。行首是名次与动作 id——**模型要回的正是那个 id**，不必自己去配牌。 */
function dangerLine(view: DangerView, label: string, id: number): string {
  const reasons = view.reasons.map((reason) => reason.display).join("、");
  const why = reasons === "" ? "" : ` —— ${reasons}`;
  return `- 第${view.rank}位 id=${id}（${label}）：${view.tier.display}${why}`;
}

/**
 * 危险度那几行（票 25）。**没有有威胁的家就一行也不写**：那时引擎也不给排序。
 *
 * 措辞照 `CONTEXT.md` 的 Danger / Genbutsu / Suji / Kabe：**它是启发式，不是概率**。
 * 这一层一个判据也不算，档位、名次与理由全是引擎算好递过来的。
 */
function dangerLines(decision: DecisionPackage, view: ScaffoldView): string[] {
  // 跟 `readScaffold` 同一个方针：读不出来就当没有，**不把这一手卡死**。
  const threats = Array.isArray(view.threats) ? view.threats : [];
  if (threats.length === 0) return [];

  const byId = new Map<number, DangerView>();
  for (const entry of view.dahai) {
    const danger = entry.danger;
    if (danger === undefined || danger === null) continue;
    for (const id of entry.action_ids) byId.set(id, danger);
  }

  const ranked = decision.actions.flatMap((option) => {
    const danger = byId.get(option.id);
    return danger === undefined ? [] : [{ danger, option }];
  });

  if (ranked.length === 0) return [];

  // 排序由引擎给（名次）；同名次的按动作 id，跟上面那两节的顺序一致。
  ranked.sort(
    (left, right) => left.danger.rank - right.danger.rank || left.option.id - right.option.id,
  );

  const who = threats.map((threat) => threat.display).join("、");

  return [
    `危险度排序（有威胁的家：${who}）——现物 / 筋 / 壁 / 宝牌周边四条规则算出来的启发式，不是概率；排在前面的更安全，同级并列：`,
    ...ranked.map((entry) => dangerLine(entry.danger, entry.option.label, entry.option.id)),
  ];
}

/**
 * Assisted 档多出来的那一节。措辞照 `CONTEXT.md`：**向听数 / 有效牌 / 进退向 / 退向（向听戻し）**，
 * 以及危险度那一批标签（**现物 / 筋 / 壁**，票 25）。
 */
function scaffoldBlock(decision: DecisionPackage): string | null {
  const view = readScaffold(decision.scaffold);
  if (view === null) return null;

  // 按**可选动作那一节的顺序**排：两节的 id 对得上，模型不必自己去配。
  const byId = new Map<number, DahaiView>();
  for (const entry of view.dahai) {
    for (const id of entry.action_ids) byId.set(id, entry);
  }

  const trials = decision.actions.flatMap((option) => {
    const entry = byId.get(option.id);
    return entry === undefined ? [] : [trial(entry, option.label, option.id)];
  });

  const head = [
    "【引擎算好的数】（下面这几个数是引擎算出来的事实，不是建议）",
    `当前向听数：${view.shanten.display}`,
  ];

  if (trials.length === 0) {
    // 这一手打不了牌（响应阶段）：手牌就是等摸形，有效牌直接给得出来。
    const acceptance = view.ukeire === null ? [] : [`有效牌 ${ukeire(view.ukeire)}`];
    return [...head, ...acceptance].join("\n");
  }

  return [
    ...head,
    "逐张试打（进退向 0 为不变，+1 为退向（向听戻し）；有效牌括号里是那张牌的剩余枚数）：",
    ...trials,
    ...dangerLines(decision, view),
  ].join("\n");
}

// ---- 三段 ----

/**
 * ①**固定 preamble**：规则、读法与任务，**逐字不变**。
 *
 * 它是整份 prompt 里第一段可缓存的字节，因此**这里一个跟局面有关的字都不许出现**
 * ——座位、巡目、点数全在后面两段里。
 *
 * 票 31 会把它换成模板 + system 槽位 + 人格文本；那时这个常量变成默认模板。
 */
export const PREAMBLE = [
  "你在打日本立直麻将（天凤规则，四人东）。现在轮到你做决策。",
  "每一次都调用 choose_action 工具，给出你选的 action_id 与一句话理由。",
  "",
  "【怎么读这份 prompt】",
  "牌用 mjai 记法：`3m` 是万子 3、`7z` 是中、`5mr` 是红 5。",
  "别家按相对位置称呼：下家（你的下一家）、对家、上家（打给你的那家）。",
  "打出去的牌缀 `*` 表示摸切（打的就是刚摸进那张），不缀就是手切。",
  "【到目前为止你看到的】是你亲眼看见的每一件事，按发生顺序逐条列着，只往后加，既有的行永不改写；",
  "看不见的东西不在里面（别家摸进的牌面、别家的手牌）。",
  "一张牌打出去之后，下一行若直接是摸牌，就说明那一张谁都没要——没人碰、吃、杠，也没人荣和。",
  "【现在】是此刻摆在桌面上的场况——它就是上面那条流到此刻的样子，两者说的是同一件事，",
  "写两遍是为了省掉你的心算，不是两份互相独立的情报。",
].join("\n");

/** ②**【到目前为止你看到的】**：append-only 的那一段。 */
function historySection(decision: DecisionPackage, naming: Naming): string {
  return ["【到目前为止你看到的】", ...historyLines(decision, naming)].join("\n");
}

/**
 * ③**尾部**：【现在】+【可选动作】+（脚手架）+（重试原因）。**每手重算，每手付全价。**
 *
 * 顺序是票 29b 定的：先摆场况、再摆能选什么，算好的数与重试原因排在后面。
 * 牌谱只存这一段（票 31）——前缀由事件流重算得出来，尾部才是那一手独有的。
 */
function presentSection(
  decision: DecisionPackage,
  naming: Naming,
  scaffold: string | null,
  note: string | null,
): string {
  const observation = decision.observation;

  return [
    `【现在】\n${board(decision)}`,
    "",
    `【你的手牌】\n${self(observation.self, naming)}`,
    "",
    `【其他三家】\n${observation.others.map((seat) => other(seat, naming)).join("\n")}`,
    "",
    `【可选动作】只能从下面这些 id 里选一个：\n${options(decision)}`,
    ...(scaffold === null ? [] : ["", scaffold]),
    ...(note === null
      ? []
      : ["", `【上一次的回答没有被采用】${note}\n请重新从上面列出的 id 里选一个。`]),
  ].join("\n");
}

/**
 * 三段各自一份，**拼装留给调用方**（票 31 要在这上面做槽位注入）。
 *
 * `prefix` = ① + ②，就是那段可缓存的字节；`present` 是尾部。
 * **往里插东西只能插在 `present` 里**：动 `preamble` 或 `history` 的措辞等于换一份前缀，
 * 那一局之前攒下的缓存全废（改渲染 = 废缓存，见报告里那条运维含义）。
 */
export interface PromptSections {
  /** ①固定 preamble：逐字不变。 */
  preamble: string;
  /** ②【到目前为止你看到的】：append-only。 */
  history: string;
  /** ③【现在】+【可选动作】+（脚手架）+（重试原因）：每手重算。 */
  present: string;
}

/** 两段之间的分隔。**只有这一处**：属性测试算前缀长度时按的就是它。 */
const JOIN = "\n\n";

/**
 * 这一手的三段。`note` 是上一次没被采用的原因（重试时才有）。
 *
 * 档位只决定 `present` 里有没有那一节算好的数——**两档的差异只能是它**，
 * 否则同一局面的对照就不是一个变量（票 24 的验收）。
 */
export function promptSections(
  decision: DecisionPackage,
  tier: ScaffoldTier,
  note: string | null,
): PromptSections {
  const naming = namesFor(decision.observation.self.seat, decision.observation.others);
  const scaffold = (SCAFFOLDS[tier] ?? SCAFFOLDS.bare)(decision);

  return {
    preamble: PREAMBLE,
    history: historySection(decision, naming),
    present: presentSection(decision, naming, scaffold, note),
  };
}

/**
 * 档位 → 那一节算好的数。**分档的接缝在这里**：两档读同一份决策包，
 * 差别只在写不写这一节。
 *
 * `tool_search` 是 M3 的事（在信息辅助之上追加局面模拟查询工具），在那之前照 Bare 渲染；
 * 配置面板里它是灰的，所以这条分支正常走不到。
 */
const SCAFFOLDS: Record<ScaffoldTier, (decision: DecisionPackage) => string | null> = {
  bare: () => null,
  assisted: scaffoldBlock,
  tool_search: () => null,
};

/**
 * **可缓存的那一段**：① + ②。
 *
 * 同一局里第 n 手的它必须是第 n+1 手整份 prompt 的**字节前缀**——这就是 provider
 * 的前缀缓存能吃到的那一段，也是 `prompt.test.ts` 里那条属性测试断言的东西。
 */
export function cacheablePrefix(decision: DecisionPackage, tier: ScaffoldTier): string {
  const sections = promptSections(decision, tier, null);
  return sections.preamble + JOIN + sections.history;
}

/**
 * 渲染这一手的 prompt：三段拼起来的一整条 user message。
 *
 * **单条不断增长的 user message，不是多轮对话**（裁决）：模型自己上一手的推理不进上下文，
 * 前缀因此只由客观事实决定。
 */
export function renderPrompt(
  decision: DecisionPackage,
  tier: ScaffoldTier,
  note: string | null,
): string {
  const sections = promptSections(decision, tier, note);
  return [sections.preamble, sections.history, sections.present].join(JOIN);
}
