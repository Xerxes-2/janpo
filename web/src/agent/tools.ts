/**
 * 交给模型的工具定义（票 26 从 `piai.ts` 拆出来）。
 *
 * **只有这一份**：`piai.ts` 拿它发请求，`loop.ts` 把它原样记进决策记录
 * （DecisionRecord 的「工具定义」，牌谱里那一段）。两处读同一个函数，
 * 因此牌谱里记下的 schema 与真正发出去的**必然一致**——审计数据不能是另抄一遍的。
 *
 * 票 18 的两条实测结论落在这里：用 **`StringEnum`** 而不是 `Type.Enum`
 * （后者生成的 `anyOf/const` Google 不吃），配 `constrainedSampling` 让支持的 provider
 * 走服务端强制 schema、不支持的自动退回普通 tool call。
 */

import { StringEnum, type Tool, Type } from "@earendil-works/pi-ai";
import type { ScaffoldTier } from "./types.ts";
import { WHAT_IF, WHAT_IF_LIMIT } from "./what-if.ts";

/** 模型必须调的那个工具的名字。判读回答时也认它。 */
export const CHOOSE_ACTION = "choose_action";

/** 合法动作集 → 工具定义。**enum 是这一手动态生成的**，不是固定表。 */
export function chooseAction(actionIds: string[]): Tool {
  return {
    name: CHOOSE_ACTION,
    description: "从这一手的合法动作集中选择一个动作。只能选列出的 action_id。",
    parameters: Type.Object(
      {
        action_id: StringEnum(actionIds as [string, ...string[]], {
          description: "所选动作的 id",
        }),
        reason: Type.String({ description: "一句话理由（中文）" }),
      },
      { additionalProperties: false },
    ),
    constrainedSampling: { type: "json_schema", strict: "prefer" },
  };
}

/**
 * 单步 what-if 查询工具（票 94）。**只有 ToolSearch 档拿得到它**，
 * 而且一手问满 `WHAT_IF_LIMIT` 次之后就不再放进 `tools`（`loop.ts` 执行这一条）。
 *
 * **接头处是 id 不是牌**，与 `choose_action` 同一个参数名：赤 5 与正 5、手切与摸切
 * 各是一条动作、各有各的 id（`types.ts` 的 `DahaiView.action_ids`）——改成给牌名就分不开它们。
 *
 * enum 里只摆**打牌那几条**：碰、吃、立直宣言没有「打完之后」可算，摆进去等于邀它问一句
 * 没答案的话（而那一问照样占一次额度）。
 */
export function whatIf(discardIds: string[]): Tool {
  return {
    name: WHAT_IF,
    description:
      `查一条打牌动作打完之后的向听数、有效牌与危险度。` +
      `它给事实不给建议，也不会替你出牌；一手最多问 ${WHAT_IF_LIMIT} 次。`,
    parameters: Type.Object(
      {
        action_id: StringEnum(discardIds as [string, ...string[]], {
          description: "要查的那条打牌动作的 id",
        }),
      },
      { additionalProperties: false },
    ),
    constrainedSampling: { type: "json_schema", strict: "prefer" },
  };
}

/**
 * 这一轮真发出去的那几个工具。**只有这一份**：`piai.ts` 拿它发请求，
 * `toolsJson` 拿它写成 JSON（录制固件与用例读的就是那一份），两处因此不可能分岔。
 *
 * `discardIds` 空表 = **不给 what-if 工具**（不是 ToolSearch 档，或者这一手问满了）。
 * 上限因此是**模型压根没得可调**，不是求它自觉。
 */
export function toolsFor(actionIds: string[], discardIds: string[]): Tool[] {
  const tools = [chooseAction(actionIds)];
  if (discardIds.length > 0) tools.push(whatIf(discardIds));
  return tools;
}

/**
 * 工具定义的 JSON 全文。
 *
 * `JSON.stringify` 落下的正是**上 wire 的那一份**（schema 里的 symbol 键本来就不上 wire）。
 */
export function toolsJson(actionIds: string[], discardIds: string[] = []): string {
  return JSON.stringify(toolsFor(actionIds, discardIds));
}

/**
 * 工具定义的**形状**（票 31）：同一份 schema，只是两个 enum 留空。
 *
 * 26 号票每手存一份工具定义（实测每手 437–513 字、几乎逐字相同），而那份里唯一随手变的
 * 就是 enum。牌谱因此**整场存一次形状**，每手只留那一手的 id 集
 * （`DecisionRecord.ActionIds`）：两者合起来就是那一手真发出去的那一份。
 *
 * **档位在这里看得见**（票 94）：ToolSearch 那一场的牌谱里这一格多一份 `what_if` 的 schema
 * ——也就是「那一场到底把哪几个工具摆到了模型面前」的存证。
 * 形状记的是**这一档的菜单**，不是某一轮真发出去的那一份（问满之后那一轮少一个工具）
 * ——与 `ActionIds` 那一格同一个口径：形状整场一份，随手变的那部分另记。
 */
export function toolsShape(tier: ScaffoldTier): string {
  const tools = [chooseAction([])];
  if (tier === "tool_search") tools.push(whatIf([]));
  return JSON.stringify(tools);
}
