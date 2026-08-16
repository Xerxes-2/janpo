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

/**
 * Bare 档（裸奔）：**只给原始局面**。
 *
 * 向听数、有效牌、危险度一个都不给 —— 这一档存在的意义就是量「模型自己会不会数牌」。
 * 想给那些数值是 24 / 25 号票的事，别往这里加。
 */
function bare(decision: DecisionPackage): string {
  return [
    "你在打日本立直麻将（天凤规则，四人东）。现在轮到你做决策。",
    "",
    `【场况】\n${board(decision)}`,
    "",
    `【你的手牌】\n${self(decision.observation.self)}`,
    "",
    `【其他三家】（\`*\` 表示摸切）\n${decision.observation.others.map(other).join("\n")}`,
    "",
    `【可选动作】只能从下面这些 id 里选一个：\n${options(decision)}`,
    "",
    "调用 choose_action 工具，给出你选的 action_id 与一句话理由。",
  ].join("\n");
}

/**
 * 档位 → 渲染器。**分档的接缝在这里**。
 *
 * 24 号票把 `assisted` 换成「Bare 的一切 + 决策包 `scaffold` 槽位里的 Shanten / Ukeire /
 * 进退向标注」，25 号票再往里加 Danger 排序；ToolSearch 是 M3 的事。在那之前两档都退回
 * Bare —— **配置面板也只给得出 Bare**，所以这两条分支现在走不到。
 */
const RENDERERS: Record<ScaffoldTier, (decision: DecisionPackage) => string> = {
  bare,
  assisted: bare,
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
