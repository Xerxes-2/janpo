/**
 * prompt 的渲染（票 23）。**prompt 在 TS 侧渲染，F# 只出结构化决策包**（ADR-0005）。
 *
 * 第一版是中文（spec：语言是渲染层参数，第一版不向 UI 暴露开关）。
 *
 * 牌一律用 mjai 记法（`3m` / `7z` / `5mr`）：决策包里就是这个形态，而**中文牌名要查术语表，
 * 那是 F# 渲染层的事**（ADR-0001）——需要中文的地方由决策包携带已经渲染好的字符串，
 * 动作的 `label` 就是这么来的。
 */

import type { DecisionPackage, MaskedSeat, Naki, RevealedSeat, ScaffoldTier } from "./types.ts";

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

interface DahaiView {
  pai: string;
  /** 打得出这一张的动作 id。**接头处是 id 不是牌**：赤 5 与正 5 是两条动作、一个牌种。 */
  action_ids: number[];
  shanten: ShantenView;
  /** 进退向：0 不变，+1 退向（向听戻し）。 */
  shanten_delta: number;
  ukeire: UkeireView | null;
}

interface ScaffoldView {
  shanten: ShantenView;
  /** 只有等摸形（还没摸进）才有；已摸进的手牌看 `dahai` 里逐条给的那些。 */
  ukeire: UkeireView | null;
  dahai: DahaiView[];
}

const KAZE: Record<string, string> = { "1z": "东", "2z": "南", "3z": "西", "4z": "北" };

const RIICHI: Record<string, string> = { none: "无", declared: "已宣言", accepted: "已成立" };

const RELATIVE: Record<number, string> = { 1: "下家", 2: "对家", 3: "上家" };

function kaze(notation: string): string {
  return KAZE[notation] ?? notation;
}

/** 河：摸切的那几张后面缀一个 `*`（手切与摸切是公开信息）。 */
function kawa(entries: { pai: string; tsumogiri: boolean }[]): string {
  if (entries.length === 0) return "空";
  return entries.map((entry) => (entry.tsumogiri ? `${entry.pai}*` : entry.pai)).join(" ");
}

function naki(groups: Naki[]): string {
  if (groups.length === 0) return "无";
  return groups
    .map((group) => {
      const taken = group.pai === null ? "" : ` 鸣${group.pai}`;
      const from = group.target === null ? "" : `（来自座位 ${group.target}）`;
      return `${group.type}${taken}[${group.consumed.join(" ")}]${from}`;
    })
    .join("，");
}

function marks(riichi: string, ippatsu: boolean): string {
  const state = RIICHI[riichi] ?? riichi;
  return ippatsu ? `${state}・一发` : state;
}

function self(seat: RevealedSeat): string {
  const furiten = seat.furiten.permanent ? "是（永久）" : seat.furiten.doujun ? "是（同巡）" : "否";
  return [
    `你是座位 ${seat.seat}（${kaze(seat.jikaze)}家），第 ${seat.junme} 巡，${seat.score} 点。`,
    `手牌：${seat.tehai.join(" ")}（${seat.tehai.length} 张）`,
    `刚摸进：${seat.tsumo ?? "无（这一手不是你摸牌）"}`,
    `副露：${naki(seat.naki)}`,
    `牌河：${kawa(seat.kawa)}`,
    `立直：${marks(seat.riichi, seat.ippatsu)}　振听：${furiten}`,
  ].join("\n");
}

function other(seat: MaskedSeat): string {
  const who = RELATIVE[seat.relative] ?? `座位 ${seat.seat}`;
  return [
    `${who}（座位 ${seat.seat}・${kaze(seat.jikaze)}家）：手里 ${seat.tehai_count} 张，第 ${seat.junme} 巡，${seat.score} 点，立直：${marks(seat.riichi, seat.ippatsu)}`,
    `  副露：${naki(seat.naki)}`,
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

/**
 * Assisted 档多出来的那一节。措辞照 `CONTEXT.md`：**向听数 / 有效牌 / 进退向 / 退向（向听戻し）**。
 *
 * **25 号票的落点就在这里**：Danger 是同一个 `scaffold` 槽位里的另一个字段，
 * 排序结果要么接在每条试打后面，要么在下面另起一行；骨架（`frame`）与调用点不用动。
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
  ].join("\n");
}

/**
 * 两档共用的骨架。`scaffold` 是多出来的那一节：**两档的差异只能是它**，
 * 否则同一局面的对照就不是一个变量（本票的验收：两档 prompt 肉眼可比）。
 */
function frame(decision: DecisionPackage, scaffold: string | null): string {
  return [
    "你在打日本立直麻将（天凤规则，四人东）。现在轮到你做决策。",
    "",
    `【场况】\n${board(decision)}`,
    "",
    `【你的手牌】\n${self(decision.observation.self)}`,
    "",
    `【其他三家】（\`*\` 表示摸切）\n${decision.observation.others.map(other).join("\n")}`,
    ...(scaffold === null ? [] : ["", scaffold]),
    "",
    `【可选动作】只能从下面这些 id 里选一个：\n${options(decision)}`,
    "",
    "调用 choose_action 工具，给出你选的 action_id 与一句话理由。",
  ].join("\n");
}

/**
 * Bare 档（裸奔）：**只给原始局面**。
 *
 * 向听数、有效牌、危险度一个都不给 —— 这一档存在的意义就是量「模型自己会不会数牌」。
 * **决策包里有那些数**（引擎恒算），这一档只是不把它们写出去。
 */
function bare(decision: DecisionPackage): string {
  return frame(decision, null);
}

/**
 * Assisted 档（信息辅助）：Bare 的一切 + 决策包 `scaffold` 里的向听数、有效牌与逐张试打。
 *
 * **一个数都不在这里算**（票 24 的硬约束）：向听数与有效牌是引擎的 `Shanten` / `Ukeire`
 * 算好送过来的，这一层只把它们排成行。脚手架读不动时退回 Bare，**不卡住这一手**。
 */
function assisted(decision: DecisionPackage): string {
  return frame(decision, scaffoldBlock(decision));
}

/**
 * 档位 → 渲染器。**分档的接缝在这里**：它们读同一份决策包，差别只在写不写那一节数值。
 *
 * `tool_search` 是 M3 的事（在信息辅助之上追加局面模拟查询工具），在那之前退回 Bare；
 * 配置面板里它是灰的，所以这条分支正常走不到。
 */
const RENDERERS: Record<ScaffoldTier, (decision: DecisionPackage) => string> = {
  bare,
  assisted,
  tool_search: bare,
};

/**
 * 渲染这一手的 prompt。`note` 是上一次没被采用的原因（重试时才有）——
 * 把它接在末尾，模型才知道自己上一次错在哪。
 */
export function renderPrompt(
  decision: DecisionPackage,
  tier: ScaffoldTier,
  note: string | null,
): string {
  const render = RENDERERS[tier] ?? bare;
  const body = render(decision);
  if (note === null) return body;
  return `${body}\n\n【上一次的回答没有被采用】${note}\n请重新从上面列出的 id 里选一个。`;
}
