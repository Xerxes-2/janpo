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
  prompt: string;
  /** `choose_action` 的 enum：这一手的合法动作 id（字符串形态，StringEnum 要的）。 */
  actionIds: string[];
}

/** 模型这一次的回答。**它永不抛**：出错也是一条带 `stopReason` 的记录。 */
export interface AskResult {
  stopReason: "stop" | "length" | "toolUse" | "error" | "aborted" | "pending" | "deferred";
  /** 调用了 `choose_action` 的话，它的实参。 */
  toolCall: { name: string; arguments: Record<string, unknown> } | null;
  /** 模型说的话（没调工具时看它）。 */
  text: string;
  errorMessage: string | null;
  latencyMs: number;
  usage: { input: number; output: number } | null;
}

/** 问一次模型。真实现在 `piai.ts`，用例里是回放录制响应的假实现。 */
export type Ask = (request: AskRequest) => Promise<AskResult>;
