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
      // **不把读不动的那段原文抄进来**（票 36）：V8 的 JSON 报错会把出错处前后十几个字符
      // 抄进消息里（实测 `Unexpected token 's', ..."api_key": sk-probe-s"... is not valid JSON`），
      // 而这份请求正是**带着 key 的那一份**。这条路上连座位配置都还没解析出来，
      // 因此没有字面量可打码（`redact.ts` 用的是确切的 key，不猜），只能不抄原文。
      // 丢掉的诊断力有限：这份 JSON 是 F# 的 `DecisionPackage.encoder` 生成的，
      // 走到这里就是契约破了，错误类别与字节数够定位。
      failure: `Agent 层读不动这份请求（${error instanceof Error ? error.name : "未知错误"}，${requestJson.length} 字节）`,
      attempts: 0,
      latency_ms: 0,
      // 连请求都没读懂，审计那几项无从谈起（票 26）；一个 token 都没花，也就没有账单（票 29b）。
      prompt_tail: "",
      preamble: "",
      render_version: "",
      tools: "",
      action_ids: [],
      output: "",
      thinking: null,
      usage: null,
    };
    return JSON.stringify(broken);
  }

  return JSON.stringify(await decideWith(piAsk, request));
}
