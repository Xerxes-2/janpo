/**
 * prompt 的模板（票 31）：**prompt 从代码降成数据**。
 *
 * 29b 把 prompt 的**形态**摆对了（固定 preamble + append-only 历史 + 尾部现况），但那三段的
 * 措辞写死在渲染器里：改一句要重编，同档位的措辞全局唯一，两个座位没法跑不同风格。
 * 这一份文件让三段各自成为**可替换的槽位**：
 *
 * ```
 * ① system    = persona + system      ← 座位级人格 + 规则与读法，**逐字不变**，进 system 消息
 * ② history   = labels.history + 逐行事件                              ┐ user 消息
 * ③ present   = labels.board/hand/others/actions/scaffold/retry + 场况  ┘
 * ```
 *
 * **三个维度互不相干**（主人 8/16 第五次裁决第 2 条）：
 * - **档位**（Bare / Assisted）决定尾部写不写那一节算好的数——它是 `ScaffoldTier`，不在模板里；
 * - **人格**是一段自由文本，进 system 消息的最前面；
 * - **模板**是措辞本身（抬头、五张措辞表、规则说明）。
 * 三者各自一个字段，谁也不缠进谁的枚举。
 *
 * ## 槽位切在哪一级
 *
 * 切在**段落**这一级：抬头、人格、规则说明与五张措辞表是数据；段落**内部**那几行
 * （`牌河：…`、`刚摸进：…`）仍是渲染器的事。再往下切就要把渲染器变成一个模板引擎，
 * 而那正是「值不值」翻过去的地方——那几行的形状与决策包的字段一一对应，
 * 换措辞的人真正想换的是抬头、称呼与人格。
 *
 * ## 换了模板就是换了一份前缀
 *
 * `renderVersion`（`render-version.ts`）是**算出来的**，不是手填的数字：没有任何东西保证
 * 改措辞的人会去 +1（裁决 29b-A）。它进 `DecisionRecord`，M2 看命中率掉时一眼归因到
 * 「换了措辞」还是「换了排版的那几行代码」——票 43 把后一半也补进去了。
 *
 * **运维含义：改渲染 = 废缓存**。人格与措辞都在可缓存前缀里，改一个字，那一局之前攒下的
 * provider 缓存全废。前缀属性测试只保证**同一次运行内**单调，不保证跨版本一致——
 * 因此换模板不会让 CI 变红，只会让账单变贵。
 */

import type { SeatConfig } from "./types.ts";
import { DEFAULT_WORDING, type Wording } from "./wording.ts";

/**
 * 各段的抬头与那几句固定的话。**每一项都是一整段的开头**，段内的行由渲染器出。
 *
 * 改这里任何一项都等于换一份可缓存前缀（`history` 在前缀里）或换一份尾部。
 */
export interface Labels {
  /** ②那一段的抬头。**它在可缓存前缀里**。 */
  history: string;
  /** ③场况那一节的抬头。 */
  board: string;
  /** 自家那一节的抬头。 */
  hand: string;
  /** 他家那一节的抬头。 */
  others: string;
  /** 可选动作那一节的抬头（后面直接跟着逐条 id）。 */
  actions: string;
  /** Assisted 档那一节的抬头（票 24）。 */
  scaffold: string;
  /** 重试那一节的抬头，后面接上一次没被采用的原因。 */
  retry: string;
  /** 重试那一节的收尾。 */
  retryTail: string;
}

/** 一份 prompt 模板。**它就是 prompt 的全部可变部分**。 */
export interface PromptTemplate {
  /** 模板标识。它排在渲染版本号最前面，是**给人看的那一截**。 */
  id: string;
  /**
   * 座位级的人格 / 风格文本，**排在 system 消息的最前面**；空串就是不写。
   *
   * 它与档位是两个维度：同一个人格可以跑两档，同一档可以跑两个人格——M2 的对照实验要它。
   */
  persona: string;
  /**
   * 规则说明与读法。**这里一个跟局面有关的字都不许出现**（座位、巡目、点数全在后两段里）：
   * 它是整份请求里第一段可缓存的字节。
   */
  system: string;
  labels: Labels;
  wording: Wording;
}

/**
 * 默认模板（票 31 要求「有个能跑的缺省」）。`system` 那一段就是 29b 的 `PREAMBLE`
 * ——**逐字未改**，因此默认模板下的可缓存前缀与 29b 那次实测是同一份字节。
 */
export const DEFAULT_TEMPLATE: PromptTemplate = {
  id: "janpo-default",
  persona: "",
  system: [
    "你在打日本立直麻将（天凤规则，四人东）。现在轮到你做决策。",
    "每一次都调用 choose_action 工具，给出你选的 action_id 与一句话理由。",
    "",
    "【你要争的是什么】",
    "整场打完时的顺位才是目标，单局的点数只是换取顺位的手段：一位与四位之间的差距，",
    "远大于一局里多和几百点。领先时守住位置比拉大分差值钱，落后时要按还剩几局、",
    "差多少点来决定冒多大的险；别为了一局的得失赌掉整场的名次。",
    "",
    "【怎么读这份 prompt】",
    "牌一律用 mjai 记法，整份 prompt 从头到尾只有这一套写法：",
    "`1m`-`9m` / `1p`-`9p` / `1s`-`9s` 是三门数牌，`1z`-`7z` 是七种字牌",
    "（`1z`-`4z` 是四种风牌，`5z`-`7z` 是三元牌），红 5 写 `5mr` / `5pr` / `5sr`。",
    "场风与自风也写成同一套记法（`1z`-`4z`），因此役牌与你手里的字牌直接对得上。",
    "别家按相对位置称呼：下家（你的下一家）、对家、上家（打给你的那家）。",
    "打出去的牌缀 `*` 表示摸切（打的就是刚摸进那张），不缀就是手切；可选动作里的打牌用同一个记号。",
    "【到目前为止你看到的】是你亲眼看见的每一件事，按发生顺序逐条列着，只往后加，既有的行永不改写；",
    "看不见的东西不在里面（别家摸进的牌面、别家的手牌）。",
    "一张牌打出去之后，下一行若直接是摸牌，就说明那一张谁都没要——没人碰、吃、杠，也没人荣和。",
    "【现在】是此刻摆在桌面上的场况——它就是上面那条流到此刻的样子，两者说的是同一件事，",
    "写两遍是为了省掉你的心算，不是两份互相独立的情报。",
  ].join("\n"),
  labels: {
    history: "【到目前为止你看到的】",
    board: "【现在】",
    hand: "【你的手牌】",
    others: "【其他三家】",
    actions: "【可选动作】只能从下面这些 id 里选一个：",
    scaffold: "【引擎算好的数】（下面这几个数是引擎算出来的事实，不是建议）",
    retry: "【上一次的回答没有被采用】",
    retryTail: "请重新从上面列出的 id 里选一个。",
  },
  wording: DEFAULT_WORDING,
};

/**
 * **system 消息的正文**：人格在前、规则说明在后。
 *
 * 人格排在最前面是因为它最短也最像「你是谁」；两段之间空一行，与三段之间同一个分隔。
 */
export function preambleOf(template: PromptTemplate): string {
  const persona = template.persona.trim();
  return persona === "" ? template.system : `${persona}\n\n${template.system}`;
}

// ---- 从配置注入 ----

/** 一张表的覆盖：给了几项就换几项，没给的沿用默认。 */
function mergeTable(base: Record<string, string>, patch: unknown): Record<string, string> {
  if (typeof patch !== "object" || patch === null) return base;

  const merged = { ...base };
  for (const [key, value] of Object.entries(patch as Record<string, unknown>)) {
    if (typeof value === "string") merged[key] = value;
  }
  return merged;
}

function mergeWording(base: Wording, patch: unknown): Wording {
  if (typeof patch !== "object" || patch === null) return base;
  const given = patch as Record<string, unknown>;

  return {
    kaze: mergeTable(base.kaze, given.kaze),
    riichi: mergeTable(base.riichi, given.riichi),
    relative: mergeTable(base.relative, given.relative),
    naki: mergeTable(base.naki, given.naki),
    ryuukyoku: mergeTable(base.ryuukyoku, given.ryuukyoku),
  };
}

function text(given: Record<string, unknown>, key: string, fallback: string): string {
  const value = given[key];
  return typeof value === "string" ? value : fallback;
}

/**
 * 覆盖那一份 JSON → 模板。**给几项换几项**，没给的沿用默认那一份。
 *
 * 读不动（不是 JSON、不是对象）就整份退回默认，并往 console 说一句：**配置是人填的，
 * 一个手误不该让这一手卡死**（与 `readScaffold` / `Thinking.ofWire` 同一个方针）。
 * 退回之后渲染版本号仍是默认那一个，因此牌谱里看得出「这一手用的不是你以为的那份模板」。
 */
export function readTemplate(raw: string, base: PromptTemplate = DEFAULT_TEMPLATE): PromptTemplate {
  if (raw.trim() === "") return base;

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch (error) {
    console.warn(`prompt 模板不是一段 JSON，这一手用默认模板：${String(error)}`);
    return base;
  }

  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    console.warn("prompt 模板要一个 JSON 对象，这一手用默认模板");
    return base;
  }

  const given = parsed as Record<string, unknown>;
  const labels = (
    typeof given.labels === "object" && given.labels !== null ? given.labels : {}
  ) as Record<string, unknown>;

  return {
    id: text(given, "id", base.id),
    persona: text(given, "persona", base.persona),
    system: text(given, "system", base.system),
    labels: {
      history: text(labels, "history", base.labels.history),
      board: text(labels, "board", base.labels.board),
      hand: text(labels, "hand", base.labels.hand),
      others: text(labels, "others", base.labels.others),
      actions: text(labels, "actions", base.labels.actions),
      scaffold: text(labels, "scaffold", base.labels.scaffold),
      retry: text(labels, "retry", base.labels.retry),
      retryTail: text(labels, "retryTail", base.labels.retryTail),
    },
    wording: mergeWording(base.wording, given.wording),
  };
}

/**
 * 这个座位这一手用哪份模板。
 *
 * 两个来源，**人格那一格优先**：`template` 是整份模板的覆盖（JSON），`persona` 是单独
 * 一格自由文本。人格最常改，不该逼人去写 JSON；写了 JSON 又填了那一格时以那一格为准
 * ——它离人更近。
 */
export function resolveTemplate(seat: Pick<SeatConfig, "persona" | "template">): PromptTemplate {
  // 两格都用 `?? ""` 护一手：请求是另一个语言 encode 的 JSON，缺字段不该把这一手卡死。
  const template = readTemplate(seat.template ?? "");
  const persona = (seat.persona ?? "").trim();

  return persona === "" ? template : { ...template, persona };
}
