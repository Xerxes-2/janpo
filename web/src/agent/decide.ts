/**
 * Agent 层的入口（票 23）。**F# 唯一 import 的那个函数**（ADR-0005：跨界只有
 * 「F# 调 TS」一个方向，且只传字符串）。
 *
 * 进来一段决策包 JSON，出去一段回执 JSON。它不认识 `Action`，也拿不到 `GameState`——
 * 回去的是一个动作 id，由 F# 侧用 `DecisionPackage.tryAction` 换成真动作。
 *
 * F# 侧的对应物：`src/Janpo.Web/Agent.fs` 的 `Agent.ask`。
 */

import { decideWith } from "./loop.ts";
import { piAsk } from "./piai.ts";
import type { DecideRequest, DecideResponse } from "./types.ts";

/**
 * 决策一手。**永不 reject**：请求读不动、provider 报错、超时、模型胡说，
 * 一律回一条带 `failure` 的回执，由 F# 侧兜底代打（`Fallback`）。
 */
export async function decide(requestJson: string): Promise<string> {
  let request: DecideRequest;
  try {
    request = JSON.parse(requestJson) as DecideRequest;
  } catch (error) {
    const broken: DecideResponse = {
      action_id: null,
      reason: null,
      failure: `Agent 层读不动这份请求：${String(error)}`,
      attempts: 0,
      latency_ms: 0,
      // 连请求都没读懂，审计那四项无从谈起（票 26）。
      prompt: "",
      tools: "",
      output: "",
      thinking: null,
    };
    return JSON.stringify(broken);
  }

  return JSON.stringify(await decideWith(piAsk, request));
}
