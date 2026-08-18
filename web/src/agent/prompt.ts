/**
 * prompt 的渲染（票 23，票 29b 把形态翻了过来，票 31 把它降成数据）。
 * **prompt 在 TS 侧渲染，F# 只出结构化决策包**（ADR-0005）。
 *
 * ## 三段，前两段可缓存；第一段是 system 消息
 *
 * ```
 * ① 人格 + 规则与读法      逐字不变        ← **system 消息**（票 31：各家的缓存语义更认它）
 * ② 【到目前为止你看到的】   掩蔽事件流，append-only，既有的行永不改写   ← user 消息的前半，可缓存
 * ③ 【现在】+【可选动作】+（脚手架）+（重试原因）  每手重算，每手付全价   ← 尾部
 * ```
 *
 * 同一局里第 n 手的 ①②（`cacheablePrefix`）必须是第 n+1 手整份 prompt 的**字节前缀**，
 * 那条属性由 `tests/agent/prefix.test.ts` 守着。**前缀里因此不许出现会被重算的量**
 * ——当前巡目、牌山剩余、任何聚合数都只能待在尾部。①②分属两条消息不改变这件事：
 * provider 的前缀缓存吃的是整份请求的开头，而 system 消息就排在最前面。
 *
 * 两种客观事实**都给**、位置不同（主人 8/16 第四次裁决）：时间上的事件流在前缀，
 * 空间上的场况在尾部。**重复是故意的**——尾部就是前缀那条流的 fold（29a 保证的一条掩蔽法则），
 * 记账负担不是我们想测的能力。
 *
 * 载体是**单条不断增长的 user message**，不是多轮对话：不把模型自己的旧理由锚进上下文。
 *
 * **牌一律用 mjai 记法，一套到底**（`3m` / `7z` / `5mr`）：决策包里就是这个形态，
 * 手牌、牌河、副露、宝牌指示牌、历史那几行、脚手架算好的那几个数都照它写。
 * **场风与自风也写成 `1z`-`4z`**（票 95）：它们指的就是手牌里那张字牌，写两套等于让模型
 * 每局自己翻译一次（役牌正压在这条翻译上）。
 *
 * **可选动作那几行同样现渲**（`action-label.ts`），不再照抄决策包里那份中文 `label`
 * ——那份写的是中文牌名（`手切4万`），是这份 prompt 里最后一处第二套记法。
 * 它仍旧给牌桌那一侧用（ADR-0001 的渲染层单向出口：中文只活在给人看的那一侧）。
 *
 * ## 措辞与人格是数据（票 31）
 *
 * 三段的抬头、五张措辞表、规则说明与人格全在 `template.ts` 的 `PromptTemplate` 里，
 * 由座位配置注入（`resolveTemplate`）——**改措辞不必改代码重编，两个座位可以同模型不同人格**。
 * 这一份文件只剩「怎么把决策包摆成那几行」。默认模板下渲出来的字节与 29b 逐字相同。
 *
 * **牌谱只存尾部**（票 31 第三节）：前缀是 (事件流 + 座位 + 模板) 的派生物，而事件流就在
 * 同一份牌谱里。`promptTail` 是存下去的那一段，`rebuild` 是把它与重算出来的前缀接回去的那一步。
 */

import { labelOf } from "./action-label.ts";
import { historyLines } from "./history.ts";
import { DEFAULT_TEMPLATE, type PromptTemplate, preambleOf } from "./template.ts";
import type { DecisionPackage, MaskedSeat, Naki, RevealedSeat, ScaffoldTier } from "./types.ts";
import { type Words, wordsFor } from "./wording.ts";

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
 * 一家的副露画出来时的**参照系**（票 40）。
 *
 * 两个都要，因为同一句话里有两个参照系各管一半：【他家】那一节的抬头
 * （`下家（座位 2・…）`）相对**观测者**，而副露那句「来自谁」相对**副露方**。
 */
interface NakiFrame {
  /** 这几组副露的主人：「来自谁」按它算。 */
  owner: number;
  /** 正在读这份 prompt 的那一家：`words.who` 按它算。 */
  viewer: number;
}

/**
 * 副露里那句「来自谁」。**参照系是副露方，而且就地声明**（票 40）。
 *
 * 三种写法，各自都回答得了「这个上家是相对谁说的」：
 *
 * - 鸣的是观测者打的那张 → **`你`**。它是身份不是方位，无所谓参照系，
 *   而且「那张是我打的」比「它的下家」直接得多（振听与安全度都要这一条）。
 * - 自家那几组 → **`上家`**。副露方就是观测者，两个参照系重合，与固定 preamble 里
 *   那句「别家按相对位置称呼」说的是同一件事。
 * - 他家那几组 → **`他的上家`**。“他的”三个字就是参照系的声明：不写它，这个词
 *   会被读成相对观测者，而那正是票 40 要修的那个错。
 */
function nakiFrom(words: Words, frame: NakiFrame, target: number): string {
  if (target === frame.viewer) return "你";
  const relative = words.whoFrom(frame.owner, target);
  return frame.owner === frame.viewer ? relative : `他的${relative}`;
}

/**
 * 副露：**措辞与历史那一段同一套**（票 29b）——种类写中文（碰 / 吃 / 暗杠 / 大明杠 / 加杠），
 * 来源写相对位置而不是座位号，牌照 mjai 记法。
 *
 * **参照系当参数传进来**（票 40 的判据）：同一个函数既渲自家又渲他家，而方位是相对的，
 * 靠调用点隐含就一定会错。
 */
function naki(groups: Naki[], words: Words, frame: NakiFrame): string {
  if (groups.length === 0) return "无";
  return groups
    .map((group) => {
      const taken = group.pai === null ? "" : ` ${group.pai}`;
      const from = group.target === null ? "" : `来自${nakiFrom(words, frame, group.target)}，`;
      return `${words.nakiKind(group.type)}${taken}（${from}亮出 ${group.consumed.join(" ")}）`;
    })
    .join("，");
}

function marks(riichi: string, ippatsu: boolean, words: Words): string {
  const state = words.riichiState(riichi);
  return ippatsu ? `${state}・一发` : state;
}

function self(seat: RevealedSeat, words: Words): string {
  const furiten = seat.furiten.permanent ? "是（永久）" : seat.furiten.doujun ? "是（同巡）" : "否";
  return [
    // 自风写 mjai 记法（票 95）：它就是一张字牌，与下一行手牌里那几张同一套写法。
    `你是座位 ${seat.seat}（自风 ${words.kaze(seat.jikaze)}），第 ${seat.junme} 巡，${seat.score} 点。`,
    `手牌：${seat.tehai.join(" ")}（${seat.tehai.length} 张）`,
    `刚摸进：${seat.tsumo ?? "无（这一手不是你摸牌）"}`,
    // 副露方就是观测者：两个参照系重合，这一行因此与票 40 之前逐字相同。
    `副露：${naki(seat.naki, words, { owner: seat.seat, viewer: seat.seat })}`,
    `牌河：${kawa(seat.kawa)}`,
    `立直：${marks(seat.riichi, seat.ippatsu, words)}　振听：${furiten}`,
  ].join("\n");
}

/** 一家他家。**抬头相对观测者、副露那句「来自谁」相对它自己**（票 40）。 */
function other(seat: MaskedSeat, words: Words, viewer: number): string {
  return [
    `${words.who(seat.seat)}（座位 ${seat.seat}・自风 ${words.kaze(seat.jikaze)}）：手里 ${seat.tehai_count} 张，第 ${seat.junme} 巡，${seat.score} 点，立直：${marks(seat.riichi, seat.ippatsu, words)}`,
    `  副露：${naki(seat.naki, words, { owner: seat.seat, viewer })}`,
    `  牌河：${kawa(seat.kawa)}`,
  ].join("\n");
}

function board(decision: DecisionPackage, words: Words): string {
  const observation = decision.observation;
  return [
    // 场风写 mjai 记法，与局数中间隔一个 `・`：两个数字直接相邻（`1z1局`）读不动。
    `场风 ${words.kaze(observation.bakaze)}・第 ${observation.kyoku} 局 ${observation.honba} 本场，供托 ${observation.kyotaku} 根。`,
    `宝牌指示牌：${observation.dora_markers.join(" ") || "无"}　牌山剩余可摸 ${observation.wall_remaining} 张。`,
  ].join("\n");
}

/** 【可选动作】：一条一行，行首就是它的 id。**那一行现渲**（`action-label.ts`）。 */
function options(decision: DecisionPackage, words: Words): string {
  return decision.actions
    .map((option) => `- id=${option.id}：${labelOf(option, words)}`)
    .join("\n");
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
function dangerLines(decision: DecisionPackage, view: ScaffoldView, words: Words): string[] {
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
    ...ranked.map((entry) =>
      dangerLine(entry.danger, labelOf(entry.option, words), entry.option.id),
    ),
  ];
}

/**
 * Assisted 档多出来的那一节。措辞照 `CONTEXT.md`：**向听数 / 有效牌 / 进退向 / 退向（向听戻し）**，
 * 以及危险度那一批标签（**现物 / 筋 / 壁**，票 25）。
 */
function scaffoldBlock(
  decision: DecisionPackage,
  template: PromptTemplate,
  words: Words,
): string | null {
  const view = readScaffold(decision.scaffold);
  if (view === null) return null;

  // 按**可选动作那一节的顺序**排：两节的 id 对得上，模型不必自己去配。
  const byId = new Map<number, DahaiView>();
  for (const entry of view.dahai) {
    for (const id of entry.action_ids) byId.set(id, entry);
  }

  const trials = decision.actions.flatMap((option) => {
    const entry = byId.get(option.id);
    return entry === undefined ? [] : [trial(entry, labelOf(option, words), option.id)];
  });

  const head = [template.labels.scaffold, `当前向听数：${view.shanten.display}`];

  if (trials.length === 0) {
    // 这一手打不了牌（响应阶段）：手牌就是等摸形，有效牌直接给得出来。
    const acceptance = view.ukeire === null ? [] : [`有效牌 ${ukeire(view.ukeire)}`];
    return [...head, ...acceptance].join("\n");
  }

  return [
    ...head,
    "逐张试打（进退向 0 为不变，+1 为退向（向听戻し）；有效牌括号里是那张牌的剩余枚数）：",
    ...trials,
    ...dangerLines(decision, view, words),
  ].join("\n");
}

// ---- 三段 ----

/** ②**【到目前为止你看到的】**：append-only 的那一段。 */
function historySection(decision: DecisionPackage, words: Words, template: PromptTemplate): string {
  return [template.labels.history, ...historyLines(decision, words)].join("\n");
}

/**
 * ③**尾部**：【现在】+【可选动作】+（脚手架）+（重试原因）。**每手重算，每手付全价。**
 *
 * 顺序是票 29b 定的：先摆场况、再摆能选什么，算好的数与重试原因排在后面。
 * **牌谱只存这一段**（票 31）——前缀由事件流重算得出来，尾部才是那一手独有的。
 */
function presentSection(
  decision: DecisionPackage,
  words: Words,
  template: PromptTemplate,
  scaffold: string | null,
  note: string | null,
): string {
  const observation = decision.observation;
  const labels = template.labels;

  return [
    `${labels.board}\n${board(decision, words)}`,
    "",
    `${labels.hand}\n${self(observation.self, words)}`,
    "",
    `${labels.others}\n${observation.others.map((seat) => other(seat, words, observation.self.seat)).join("\n")}`,
    "",
    `${labels.actions}\n${options(decision, words)}`,
    ...(scaffold === null ? [] : ["", scaffold]),
    ...(note === null ? [] : ["", `${labels.retry}${note}\n${labels.retryTail}`]),
  ].join("\n");
}

/**
 * 三段各自一份，**拼装留给调用方**（票 31 的槽位注入就落在这上面）。
 *
 * `preamble` + `history` 就是那段可缓存的字节；`present` 是尾部。
 * **往里插东西只能插在 `present` 里**：动 `preamble` 或 `history` 的措辞等于换一份前缀，
 * 那一局之前攒下的缓存全废（改渲染 = 废缓存，见报告里那条运维含义）。
 */
export interface PromptSections {
  /** ①人格 + 规则与读法：逐字不变，**它是 system 消息**。 */
  preamble: string;
  /** ②【到目前为止你看到的】：append-only。 */
  history: string;
  /** ③【现在】+【可选动作】+（脚手架）+（重试原因）：每手重算。 */
  present: string;
}

/**
 * 真发出去的那两条消息（票 31）。
 *
 * **固定 preamble 进 system**：各家的缓存语义更认它（Anthropic 的 `cache_control` 就挂在
 * system 块上），而 user 消息只剩历史 + 现况 + 动作。两条消息按 `JOIN` 接起来就是
 * `renderPrompt` 那一份全文——审计与前缀属性都按那一份说话。
 */
export interface PromptMessages {
  /** system 消息：人格 + 规则与读法。 */
  system: string;
  /** user 消息：历史 + 尾部。 */
  user: string;
}

/** 两段之间的分隔。**只有这一处**：属性测试算前缀长度时按的就是它。 */
const JOIN = "\n\n";

/**
 * 这一手的三段。`note` 是上一次没被采用的原因（重试时才有）。
 *
 * 档位只决定 `present` 里有没有那一节算好的数——**两档的差异只能是它**，
 * 否则同一局面的对照就不是一个变量（票 24 的验收）。模板决定措辞与人格，
 * 与档位是**两个维度**：不给就是默认模板（票 31：默认仍在代码里，只是不再唯一）。
 */
export function promptSections(
  decision: DecisionPackage,
  tier: ScaffoldTier,
  note: string | null,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): PromptSections {
  const words = wordsFor(
    template.wording,
    decision.observation.self.seat,
    decision.observation.others,
  );
  const scaffold = (SCAFFOLDS[tier] ?? SCAFFOLDS.bare)(decision, template, words);

  return {
    preamble: preambleOf(template),
    history: historySection(decision, words, template),
    present: presentSection(decision, words, template, scaffold, note),
  };
}

/**
 * 档位 → 那一节算好的数。**分档的接缝在这里**：两档读同一份决策包，
 * 差别只在写不写这一节。
 *
 * `tool_search` 是 M3 的事（在信息辅助之上追加局面模拟查询工具），在那之前照 Bare 渲染；
 * 配置面板里它是灰的，所以这条分支正常走不到。
 */
const SCAFFOLDS: Record<
  ScaffoldTier,
  (decision: DecisionPackage, template: PromptTemplate, words: Words) => string | null
> = {
  bare: () => null,
  assisted: scaffoldBlock,
  tool_search: () => null,
};

/**
 * **可缓存的那一段**：① + ②。
 *
 * 同一局里第 n 手的它必须是第 n+1 手整份 prompt 的**字节前缀**——这就是 provider
 * 的前缀缓存能吃到的那一段，也是 `prefix.test.ts` 里那条属性测试断言的东西。
 * ①进了 system 消息之后这条仍然成立：请求是「system 在前、user 在后」序列化的。
 */
export function cacheablePrefix(
  decision: DecisionPackage,
  tier: ScaffoldTier,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): string {
  const sections = promptSections(decision, tier, null, template);
  return sections.preamble + JOIN + sections.history;
}

/**
 * 真发出去的那两条消息。
 *
 * **user 是单条不断增长的消息，不是多轮对话**（裁决）：模型自己上一手的推理不进上下文，
 * 前缀因此只由客观事实决定。
 */
export function promptMessages(
  decision: DecisionPackage,
  tier: ScaffoldTier,
  note: string | null,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): PromptMessages {
  return messagesOf(promptSections(decision, tier, note, template));
}

/**
 * 三段 → 两条消息。**拼法只有这一处**：决策循环要同时拿到消息与尾部，
 * 渲染两遍再拉各自那一半，两份就可能对不上。
 */
export function messagesOf(sections: PromptSections): PromptMessages {
  return {
    system: sections.preamble,
    user: sections.history + JOIN + sections.present,
  };
}

/**
 * **牌谱里存下去的那一段**：尾部，且只有尾部（票 31 第三节）。
 *
 * 前缀不存——它是 (事件流 + 座位 + 模板) 的派生物，而事件流就在同一份牌谱里。
 * 存它在快照式 prompt 下只是浪费，在事件流式 prompt 下是 O(n²)。
 */
export function promptTail(
  decision: DecisionPackage,
  tier: ScaffoldTier,
  note: string | null,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): string {
  return promptSections(decision, tier, note, template).present;
}

/**
 * 把牌谱里那一段尾部接回重算出来的前缀：**重建出当时真发出去的那两条消息**（票 31 第三节）。
 *
 * `preamble` 从牌谱的 `Prompting` 里按「座位 + 渲染版本」取，`decision` 由事件流 fold
 * 到那一手重新算出来（`DecisionPackage.forSeat`），尾部原样取自那一条记录。
 * **审计价值不打折**：渲染器当时出了什么怪，尾部原样留着证。
 */
export function rebuildMessages(
  preamble: string,
  decision: DecisionPackage,
  tail: string,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): PromptMessages {
  const words = wordsFor(
    template.wording,
    decision.observation.self.seat,
    decision.observation.others,
  );

  return {
    system: preamble,
    user: historySection(decision, words, template) + JOIN + tail,
  };
}

/**
 * 整份 prompt 的全文：两条消息按 `JOIN` 拼起来。
 *
 * **它不是又一种发法**（真发出去的是 `promptMessages` 那两条），而是「这一手模型看到的
 * 全部文字」的那一份——前缀属性、打印脚本与录制固件都按它说话。
 */
export function renderPrompt(
  decision: DecisionPackage,
  tier: ScaffoldTier,
  note: string | null,
  template: PromptTemplate = DEFAULT_TEMPLATE,
): string {
  const messages = promptMessages(decision, tier, note, template);
  return messages.system + JOIN + messages.user;
}
