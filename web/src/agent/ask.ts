/**
 * 「问一次模型」这件事的形状（票 23）。
 *
 * 把它单独拎出来是为了**确定性测试**：CI 里绝不调真实 API（M1 增量约束 6），
 * 用例喂进去的是 `tests/fixtures/agent/` 里**录制下来的**响应。录制脚本
 * （`web/scripts/record-agent-fixtures.mjs`）跑的是下面 `piai.ts` 的真实实现。
 *
 * 字段贴着 pi-ai 的 `AssistantMessage`：`stopReason` 是**值不是异常**（票 18 实测：
 * abort → `"aborted"`、坏 key → `"error"`，两者都不抛），因此兜底逻辑写成对它的 match。
 */

import type { SeatConfig } from "./types.ts";

/** 一次问话要的全部东西。 */
export interface AskRequest {
  seat: SeatConfig;
  /**
   * **system 消息**：人格 + 规则与读法（票 31）。**一局内逐字不变**
   * （各家的前缀缓存语义认的就是这一点），**局间可以换**——换了就是另一份前缀，
   * 牌谱按「座位 + 渲染版本」各记一份（`src/Janpo.Engine/Paifu.fs` 的 `Preamble` 那段注释）。
   */
  system: string;
  /** **user 消息**：历史（append-only）+ 尾部现况与动作。 */
  prompt: string;
  /** `choose_action` 的 enum：这一手的合法动作 id（字符串形态，StringEnum 要的）。 */
  actionIds: string[];
  /**
   * `what_if` 的 enum：这一轮还查得的那几条**打牌**动作 id（票 94）。
   *
   * **空表 = 这一轮不把 `what_if` 放进 `tools`**，三种情形都落在这一条上：
   * 不是 ToolSearch 档、这一手问满了上限、以及这一手压根没有牌可打（响应阶段）。
   * 上限因此是**模型调不出来**，不是 prompt 里求它自觉。
   */
  whatIfIds: string[];
}

/** 模型这一次的回答。**它永不抛**：出错也是一条带 `stopReason` 的记录。 */
export interface AskResult {
  stopReason: "stop" | "length" | "toolUse" | "error" | "aborted" | "pending" | "deferred";
  /** 调用了 `choose_action` 的话，它的实参。 */
  toolCall: { name: string; arguments: Record<string, unknown> } | null;
  /** 模型说的话（没调工具时看它）。 */
  text: string;
  /**
   * 扩展思考的全文（票 26）；没开 `reasoning` 或 provider 不给时是 null。
   *
   * 它由 `streamSimple` 收 `thinking_delta` 拼出来（票 18 实测：`reasoning: "medium"`
   * 收到 4062 个 delta）。**它不在 `text` 里**：牌谱要能单独省掉这一段（URL 分享）。
   */
  thinking: string | null;
  errorMessage: string | null;
  latencyMs: number;
  /**
   * token 账单（票 29b）。字段名照 pi-ai 的 `Usage`：`cacheRead` 是**命中前缀缓存**、
   * 按折扣价计的那部分输入 token（DeepSeek 的 `prompt_cache_hit_tokens` 由 pi-ai 的
   * `openai-completions` 适配器读进来），`input` 已经把它减掉了。
   *
   * **它是「前缀可缓存的 prompt 真的省下钱了没有」的唯一证据**，一路进 `DecisionRecord`。
   */
  usage: {
    input: number;
    output: number;
    /** **票 29b 之前录下来的固件没有这两项**，因此可缺省；真跑起来 pi-ai 恒给。 */
    cacheRead?: number;
    cacheWrite?: number;
  } | null;
}

/** 问一次模型。真实现在 `piai.ts`，用例里是回放录制响应的假实现。 */
export type Ask = (request: AskRequest) => Promise<AskResult>;
