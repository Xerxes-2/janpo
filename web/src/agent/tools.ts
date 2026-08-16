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
 * 工具定义的 JSON 全文。
 *
 * `JSON.stringify` 落下的正是**上 wire 的那一份**（schema 里的 symbol 键本来就不上 wire）。
 */
export function toolsJson(actionIds: string[]): string {
  return JSON.stringify([chooseAction(actionIds)]);
}

/**
 * 工具定义的**形状**（票 31）：同一份 schema，只是 `action_id` 那个 enum 留空。
 *
 * 26 号票每手存一份工具定义（实测每手 437–513 字、几乎逐字相同），而那份里唯一随手变的
 * 就是 enum。牌谱因此**整场存一次形状**，每手只留那一手的 id 集
 * （`DecisionRecord.ActionIds`）：两者合起来就是那一手真发出去的那一份。
 */
export function toolsShape(): string {
  return toolsJson([]);
}
