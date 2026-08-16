/**
 * 座席历史的渲染（票 29b）：**掩蔽事件流 → 一条事件一行中文短句**。
 *
 * 它是 prompt 的**前缀**那一段（【到目前为止你看到的】），append-only：
 * 同一局里第 n 手渲出来的每一行，第 n+1 手必须逐字节一模一样，只在末尾多几行。
 * provider 的前缀缓存吃的就是这一段。
 *
 * 因此有一条硬约束：**每一行只由那一条事件本身决定**（外加一局之内恒定的视角与称呼，
 * 以及它**之前**那些事件数出来的巡目），绝不依赖它之后发生的任何事。守这条的是
 * `tests/agent/prefix.test.ts` 里那条「前缀字节稳定」的属性测试——CI 里别的用例
 * 全绿它也照样红。
 *
 * 巡目是**从左往右数**出来的（某家在这一条之前摸过几次牌），因此它写下去就再也不会变；
 * 尾部那个「第几巡」是同一条流 fold 出来的当前值（`Observation.junme`），两者同源。
 *
 * 措辞与牌的记法由调用方给的 `Words` 决定（`wording.ts`），与尾部共用一份
 * （DECISIONS 第六次裁决）；票 31 起那一份来自模板，因此人格与措辞换得动而这里一行不动。
 * 引擎那边的结构在 `MaskedEvent.encoder`：`Public` 那十四条逐字段就是 mjai 事件，
 * 掩掉的两条也仍是 mjai 的形状（看不见的那一格写 `"?"`）。
 */

import type { DecisionPackage, MaskedEvent } from "./types.ts";
import type { Words } from "./wording.ts";

// ---- 读 wire 字段 ----
//
// 掩蔽事件在 TS 这侧是**没有结构的 JSON**（`Record<string, unknown>`）：Agent 层不解释动作，
// 也不该为了渲染一行字给十七条 mjai 事件各写一个接口。读不出来就退回一个看得见的占位，
// **不抛**——一手 prompt 渲不出来的代价是那一手兜底，太贵。

function num(event: MaskedEvent, field: string): number {
  const value = event[field];
  return typeof value === "number" ? value : -1;
}

function text(event: MaskedEvent, field: string): string {
  const value = event[field];
  return typeof value === "string" ? value : "?";
}

function flag(event: MaskedEvent, field: string): boolean {
  return event[field] === true;
}

function tiles(event: MaskedEvent, field: string): string[] {
  const value = event[field];
  return Array.isArray(value) ? value.map((item) => String(item)) : [];
}

function numbers(event: MaskedEvent, field: string): number[] {
  const value = event[field];
  return Array.isArray(value) ? value.map((item) => Number(item)) : [];
}

// ---- 一条事件一行 ----

/** 巡目：某家自己摸过几次牌（CONTEXT.md 的 Junme）。**鸣牌不摸牌，鸣的那家巡不涨。** */
type Junme = Map<number, number>;

function junmeOf(junme: Junme, seat: number): string {
  return `第${junme.get(seat) ?? 0}巡`;
}

/** `摸切` 的那张后面缀 `*`（与尾部的牌河同一个写法）。 */
function discarded(event: MaskedEvent): string {
  return flag(event, "tsumogiri") ? `${text(event, "pai")}*` : text(event, "pai");
}

/** 开局那一条：桌面上摆出来的全部 + 自家配牌。**他家的配牌不在事件里**（掩蔽掉了）。 */
function startKyoku(event: MaskedEvent, words: Words): string {
  const scores = numbers(event, "scores")
    .map((score, seat) => `${words.who(seat)} ${score}`)
    .join("　");

  return [
    `开局：${words.kaze(text(event, "bakaze"))}${num(event, "kyoku")}局 ${num(event, "honba")} 本场，`,
    `供托 ${num(event, "kyotaku")} 根，庄家是${words.who(num(event, "oya"))}，`,
    `宝牌指示牌：${text(event, "dora_marker")}，各家点数 ${scores}。`,
    `你的配牌：${tiles(event, "tehai").join(" ")}`,
  ].join("");
}

/** 和了那一条。**它不会出现在决策包的历史里**（和了即一局终，没有下一手要问）， */
/** 渲得出来是为了牌谱回放与围观视角共用同一条渲染路径。 */
function hora(event: MaskedEvent, words: Words): string {
  const actor = num(event, "actor");
  const target = num(event, "target");
  const how =
    actor === target
      ? `自摸 ${text(event, "pai")}`
      : `荣和${words.who(target)}打的 ${text(event, "pai")}`;
  const ura = tiles(event, "uradora_markers");
  const uraText = ura.length === 0 ? "" : `，里宝牌指示牌 ${ura.join(" ")}`;

  return `${words.who(actor)} 和了：${how}，${num(event, "fan")} 番 ${num(event, "fu")} 符，${num(event, "hora_points")} 点${uraText}`;
}

function ryuukyoku(event: MaskedEvent, words: Words): string {
  const tenpai = numbers(event, "tenpais")
    .map((value, seat) => (value ? words.who(seat) : null))
    .filter((who) => who !== null);
  const who = tenpai.length === 0 ? "无人听牌" : `听牌的是 ${tenpai.join("、")}`;

  return `流局（${words.ryuukyokuReason(text(event, "reason"))}）：${who}`;
}

/**
 * 一条事件的那一行。**没有 default 兜底成空串**：读不懂的事件也占一行，
 * 否则它从历史里静静消失，而模型看到的是一条缺了东西的流。
 */
function line(event: MaskedEvent, words: Words, junme: Junme): string {
  const actor = num(event, "actor");
  const who = words.who(actor);
  const at = junmeOf(junme, actor);

  switch (event.type) {
    case "start_kyoku":
      return startKyoku(event, words);
    case "tsumo":
      // 他家摸的那张看不见（wire 上是 mjai 的 `"?"`）：**「他摸了」本身是看得见的**，
      // 巡目与手切摸切的时间轴全靠这一条撑着。
      return text(event, "pai") === "?"
        ? `${who} ${at} 摸牌`
        : `${who} ${at} 摸 ${text(event, "pai")}`;
    case "dahai":
      return `${who} ${at} 打 ${discarded(event)}`;
    case "pon":
    case "chi":
    case "daiminkan":
      return `${who} ${at} ${words.nakiKind(event.type)} ${words.who(num(event, "target"))}打的 ${text(event, "pai")}（亮出 ${tiles(event, "consumed").join(" ")}）`;
    case "ankan":
      return `${who} ${at} 暗杠 ${tiles(event, "consumed").join(" ")}`;
    case "kakan":
      return `${who} ${at} 加杠 ${text(event, "pai")}（原来碰的 ${tiles(event, "consumed").join(" ")}）`;
    case "dora":
      return `翻开新的宝牌指示牌：${text(event, "dora_marker")}`;
    case "reach":
      // 宣言巡目就在这一行上；**紧接着那一条打牌就是宣言牌**。
      return `${who} ${at} 宣言立直`;
    case "reach_accepted":
      return `${who}的立直成立，放出立直棒`;
    case "hora":
      return hora(event, words);
    case "ryukyoku":
      return ryuukyoku(event, words);
    case "start_game":
      return "对局开始";
    case "end_kyoku":
      return "本局结束";
    case "end_game":
      return "整场对局结束";
    default:
      return `（读不懂的事件：${event.type}）`;
  }
}

// ---- 一整条流 ----

/**
 * 这个座位到此刻为止看见的每一件事，一件一行。
 *
 * **只从 `decision.history` 读**（引擎给的掩蔽事件流），一个字都不从尾部那份快照抄：
 * 两种形态同源是 29a 保证的，这一层再抄一遍就等于又造了一个来源。
 */
export function historyLines(decision: DecisionPackage, words: Words): string[] {
  const junme: Junme = new Map();
  // 读不出历史就当没有（29b 之前录下来的包就是这个形状）：**少一段不该让这一手渲不出来**，
  // 与 `readScaffold` 同一个方针。
  const events = Array.isArray(decision.history) ? decision.history : [];

  return events.map((event) => {
    // 摸牌那一条就是新一巡的开头：先把巡目推上去，这一行写的就是「第几巡摸的」。
    if (event.type === "tsumo") {
      const actor = num(event, "actor");
      junme.set(actor, (junme.get(actor) ?? 0) + 1);
    }
    return line(event, words, junme);
  });
}
