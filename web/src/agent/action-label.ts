/**
 * 一条可选动作**写给模型看的那一行**（票 95 的第一笔债）。
 *
 * ## 为什么这一行由 TS 渲染，而不是照抄决策包里的 `label`
 *
 * 决策包里带着引擎渲好的中文 label（`Action.toDisplay`，ADR-0001 说的那个「渲染层单向出口」），
 * 它写的是**中文牌名**：`手切4万`、`摸切发`、`碰赤5筒（亮5筒 5筒）`。而同一份 prompt 里
 * 别的每一处（手牌、牌河、副露、宝牌指示牌、历史那几行、脚手架算好的那几个数）写的都是
 * **mjai 记法**。于是模型读一手牌要在两套记法之间来回翻译，而它翻错的时候我们只看得到
 * 「它打错了」——这就是从 M1 挂到 M3 的那笔债。
 *
 * 这一份文件把动作那一行改成从 `ActionOption.action`（就是一条 mjai 动作消息）现渲：
 * **牌照 mjai、种类照措辞表**，与 `history.ts` 渲一条事件走的是同一条路、同一份 `Words`。
 * 引擎那个中文 label 一个字没改，它照旧给牌桌上「上一手是谁做了什么」那行字用
 * （`TableBoard.fs`）——**中文仍旧只活在给人看的那一侧**，正是 ADR-0001 要的形态。
 *
 * ADR-0005 那句「渲染层的中文文案要进 prompt 时由决策包携带已经渲染好的字符串」仍然成立：
 * 这里一个术语表都没查——牌是包里原样的 mjai 字符串，五种副露的中文词来自模板的
 * `wording.naki`（与历史那一段共用一份），其余那几个动词与 `history.ts` 里的 `打` / `摸`
 * 同一个层级，是渲染器写死的句式而不是术语翻译。
 *
 * ## 手切 / 摸切写成 `*`，与牌河、历史同一个记号
 *
 * 旧 label 用「手切」「摸切」两个词，而同一份 prompt 的牌河与历史用的是牌后面缀一个 `*`
 * ——同一件事的两种写法，也是这一票要收掉的重复。现在统一成 `打 5s` / `打 5s*`，
 * 读法在固定 preamble 里只说一次。**动作 id 仍是唯一的接头**：赤 5 与正 5、手切与摸切
 * 各是一条动作、各有各的 id，这一行只是把那条动作念出来。
 *
 * ## 认不出来的动作退回引擎那份 label
 *
 * mjai 将来多一种动作时，宁可这一行写着中文也不能让这一手渲不出来（与 `readScaffold`、
 * `Thoth` 那几处同一个方针：读不懂的东西不许悄悄消失）。退回去之后中文牌名就会重新出现，
 * 而语义闸门里「记法只有 mjai 一套」那一条会当场把它抓出来——**兜底是看得见的**。
 */

import type { ActionOption } from "./types.ts";
import type { Words } from "./wording.ts";

/** 读一个 wire 字段；不是字符串就返回 null（认不出来就整条退回，不写半句话）。 */
function text(action: Record<string, unknown>, field: string): string | null {
  const value = action[field];
  return typeof value === "string" ? value : null;
}

/** 读一串牌；不是数组就返回 null。 */
function tiles(action: Record<string, unknown>, field: string): string[] | null {
  const value = action[field];
  return Array.isArray(value) ? value.map((item) => String(item)) : null;
}

/** 亮出来的那几张。**碰 / 吃 / 大明杠的亮法是选手的决策**（赤 5 亮不亮番数不同），因此写出来。 */
function shown(consumed: string[] | null): string {
  return consumed === null ? "" : `（亮出 ${consumed.join(" ")}）`;
}

/**
 * 这一条动作念出来是哪一行。**认不出来返回 null**，调用方退回引擎那份 label。
 *
 * 种类词从 `words.nakiKind` 取（模板换措辞时这一行跟着换，与历史那一段同一份表）；
 * `打` / `自摸` / `荣和` / `立直宣言` / `过` / `九种九牌` 与 `history.ts` 里的 `打` / `摸`
 * 同一层级，是句式不是术语表。
 */
export function actionLabel(option: ActionOption, words: Words): string | null {
  const action = option.action;
  if (typeof action !== "object" || action === null) return null;

  const pai = text(action, "pai");
  const consumed = tiles(action, "consumed");

  switch (action.type) {
    case "dahai":
      // 摸切缀 `*`：与牌河、历史那一段同一个记号（读法只在 preamble 里说一次）。
      return pai === null ? null : `打 ${pai}${action.tsumogiri === true ? "*" : ""}`;
    case "pon":
    case "chi":
    case "daiminkan":
      return pai === null || consumed === null
        ? null
        : `${words.nakiKind(action.type)} ${pai}${shown(consumed)}`;
    case "ankan":
      // 暗杠没有「鸣来的那张」——四张全出自自己手里，因此只写亮出来的那四张。
      return consumed === null ? null : `${words.nakiKind("ankan")}${shown(consumed)}`;
    case "kakan":
      // 加杠只有一种亮法（底下那组碰已经在牌桌上），写加上去的那张就够了。
      return pai === null ? null : `${words.nakiKind("kakan")} ${pai}`;
    case "hora":
      return pai === null ? null : `${action.actor === action.target ? "自摸" : "荣和"} ${pai}`;
    case "reach":
      return "立直宣言";
    case "none":
      return "过";
    case "ryukyoku":
      // 动作消息不带形态；走得到这一条的只有九种九牌（`Action.toDisplay` 同一口径）。
      return "九种九牌";
    default:
      return null;
  }
}

/** 这一条动作在 prompt 里那一行：渲得出来就用 mjai 那份，认不出来退回引擎的中文 label。 */
export function labelOf(option: ActionOption, words: Words): string {
  return actionLabel(option, words) ?? option.label;
}
