/**
 * 兜底闭环的那个循环（票 23）：**问 → 判读 → 不行就带着理由重问 → 用尽了就说「交不出来」**。
 *
 * 它不认识动作，只认识 id：判读的全部内容是「模型给回来的那个 id 在不在这一包里」。
 * **代打哪一手不是这里的事**——兜底策略要读规则（Bare 档摸切、Assisted 档不退向听的安全打），
 * 那在引擎的 `Fallback` 里。这一层的产出只有「选了 id」或者「我交不出来，原因是……」。
 *
 * `ask` 是注入的：CI 里喂录制的响应，浏览器里喂 `piai.ts`。
 */

import type { Ask, AskResult } from "./ask.ts";
import { renderPrompt } from "./prompt.ts";
import type { DecideRequest, DecideResponse } from "./types.ts";

/** 一次回答的判读。 */
type Verdict = { ok: true; id: number; reason: string | null } | { ok: false; why: string };

/** 模型说的那段话，截短了放进原因里——原因是给人看的，不是给机器解析的。 */
function excerpt(text: string): string {
  const trimmed = text.trim().replace(/\s+/g, " ");
  return trimmed.length > 60 ? `${trimmed.slice(0, 60)}…` : trimmed;
}

/** 严格的整数：`"3"` 是 3，`"3索"`、`"三"`、`""`、`"3.5"` 都不是。 */
function strictInt(value: unknown): number | null {
  const text = String(value ?? "").trim();
  if (!/^-?\d+$/.test(text)) return null;
  return Number.parseInt(text, 10);
}

/**
 * 这次回答能用吗。**四类失败在这里合流**（超时 / provider 报错 / 格式跑偏 / id 非法），
 * 之后走同一条重试与兜底路径。
 */
function judge(result: AskResult, ids: Set<number>): Verdict {
  // 票 18 实测：这两样都是值，不是异常。
  if (result.stopReason === "aborted") {
    return { ok: false, why: `模型超时（${result.latencyMs} ms 没答完）` };
  }
  if (result.stopReason === "error") {
    return { ok: false, why: `provider 报错：${result.errorMessage ?? "没给原因"}` };
  }

  const call = result.toolCall;
  if (call === null) {
    return {
      ok: false,
      why: `模型没有调用 choose_action，只回了一段话：「${excerpt(result.text)}」`,
    };
  }
  if (call.name !== "choose_action") {
    return { ok: false, why: `模型调了别的工具：${call.name}` };
  }

  const id = strictInt(call.arguments.action_id);
  if (id === null) {
    return { ok: false, why: `action_id 不是一个 id：${JSON.stringify(call.arguments.action_id)}` };
  }
  if (!ids.has(id)) {
    return { ok: false, why: `action_id=${id} 不在这一手的合法动作集里` };
  }

  const reason = typeof call.arguments.reason === "string" ? call.arguments.reason : null;
  return { ok: true, id, reason };
}

function refuse(why: string, attempts: number, latencyMs: number): DecideResponse {
  return { action_id: null, reason: null, failure: why, attempts, latency_ms: latencyMs };
}

/**
 * 决策一手。**永远 resolve，永远不 reject**：所有失败都变成 `failure` 字段，
 * 由 F# 侧据此兜底。`retry_limit` 是重试次数上限，因此最多问 `retry_limit + 1` 次。
 */
export async function decideWith(ask: Ask, request: DecideRequest): Promise<DecideResponse> {
  const options = request.decision.actions;
  if (options.length === 0) {
    // 引擎给的合法动作集非空，走到这里说明契约破了。仍然不抛。
    return refuse("这一手没有合法动作", 0, 0);
  }
  if (request.seat.api_key.trim() === "") {
    // 不发这一次请求：没有 key 时 provider 必然 401，白等一个来回。
    return refuse(`没有填 ${request.seat.provider} 的 API key`, 0, 0);
  }

  const ids = new Set(options.map((option) => option.id));
  const actionIds = options.map((option) => String(option.id));
  const rounds = Math.max(1, request.retry_limit + 1);

  let attempts = 0;
  let latencyMs = 0;
  let why = "没问出结果";
  let note: string | null = null;

  while (attempts < rounds) {
    const prompt = renderPrompt(request.decision, request.seat.tier, note);
    let verdict: Verdict;
    try {
      const result = await ask({ seat: request.seat, prompt, actionIds });
      latencyMs += result.latencyMs;
      verdict = judge(result, ids);
    } catch (error) {
      // 不该发生（超时与报错都是值），但真抛了也只是这一次失败，不是整局崩掉。
      verdict = { ok: false, why: `Agent 层抛了异常：${String(error)}` };
    }
    attempts += 1;

    if (verdict.ok) {
      return {
        action_id: verdict.id,
        reason: verdict.reason,
        failure: null,
        attempts,
        latency_ms: latencyMs,
      };
    }

    why = verdict.why;
    note = verdict.why;
  }

  return refuse(`${why}（重试 ${rounds - 1} 次仍无结果）`, attempts, latencyMs);
}
