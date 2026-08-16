/**
 * 兜底闭环的那个循环（票 23）：**问 → 判读 → 不行就带着理由重问 → 用尽了就说「交不出来」**。
 *
 * 它不认识动作，只认识 id：判读的全部内容是「模型给回来的那个 id 在不在这一包里」。
 * **代打哪一手不是这里的事**——兜底策略要读规则（Bare 档摸切、Assisted 档不退向听的安全打），
 * 那在引擎的 `Fallback` 里。这一层的产出只有「选了 id」或者「我交不出来，原因是……」。
 *
 * `ask` 是注入的：CI 里喂录制的响应，浏览器里喂 `piai.ts`。
 *
 * **审计数据也在这一层收**（票 26）：问出去的 prompt 与工具定义、收回来的原始输出与
 * thinking 随回执一起过界，由 F# 侧组装成 `DecisionRecord`。记的是**最后一次**那轮：
 * 它才是产出这条回答的那一次，问了几次看 `attempts`。
 */

import type { Ask, AskResult } from "./ask.ts";
import { missingConfig } from "./endpoint.ts";
import { renderPrompt } from "./prompt.ts";
import { CHOOSE_ACTION, toolsJson } from "./tools.ts";
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
      why: `模型没有调用 ${CHOOSE_ACTION}，只回了一段话：「${excerpt(result.text)}」`,
    };
  }
  if (call.name !== CHOOSE_ACTION) {
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

/**
 * 模型这一次的**原始输出**，进决策记录。
 *
 * **thinking 不在里面**：它单独一个字段，因为 URL 分享（M2）要能只省掉它那一段。
 * `null` （一次都没问成）时给空串：牌谱里的字符串字段不写 null。
 */
function rawOutput(result: AskResult | null): string {
  if (result === null) return "";
  return JSON.stringify({
    stop_reason: result.stopReason,
    text: result.text,
    tool_call: result.toolCall,
    error_message: result.errorMessage,
    usage: result.usage,
  });
}

/**
 * 一轮问话里审计要的那几项。一次都没问成时 `result` 是 null。
 *
 * **token 账单也在这里过界**（票 29b）：`cache_read` 是「前缀真的命中了」的唯一证据，
 * 由 `TablePage.settle` 收进这一手的 `DecisionRecord`，页面上看得见。
 * 记的是**最后一轮**那次（与 prompt / 输出同一轮，裁决 26-16）。
 */
function audited(prompt: string, tools: string, result: AskResult | null) {
  const usage = result?.usage ?? null;

  return {
    prompt,
    tools,
    output: rawOutput(result),
    thinking: result?.thinking ?? null,
    usage:
      usage === null
        ? null
        : {
            input: usage.input,
            output: usage.output,
            cache_read: usage.cacheRead ?? 0,
            cache_write: usage.cacheWrite ?? 0,
          },
  };
}

/** 一条「我交不出来」。审计那四项默认是空的，真问过的那几条路上再盖上去。 */
function refuse(why: string, attempts: number, latencyMs: number): DecideResponse {
  return {
    action_id: null,
    reason: null,
    failure: why,
    attempts,
    latency_ms: latencyMs,
    ...audited("", "", null),
  };
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
  const missing = missingConfig(request.seat);
  if (missing !== null) {
    // 不发这一次请求：没有 key 时 provider 必然 401，白等一个来回；
    // 自定义端点没填 baseUrl 同理（它反而不要求 key，本地端点通常不校验）。
    // 这两条路上连 prompt 都没渲染过，因此审计那四项全是空的——记录仍然留一条，
    // 内容就是那句原因。
    return refuse(missing, 0, 0);
  }

  const ids = new Set(options.map((option) => option.id));
  const actionIds = options.map((option) => String(option.id));
  const tools = toolsJson(actionIds);
  const rounds = Math.max(1, request.retry_limit + 1);

  let attempts = 0;
  let latencyMs = 0;
  let why = "没问出结果";
  let note: string | null = null;
  let prompt = "";
  let last: AskResult | null = null;

  while (attempts < rounds) {
    prompt = renderPrompt(request.decision, request.seat.tier, note);
    let verdict: Verdict;
    try {
      const result = await ask({ seat: request.seat, prompt, actionIds });
      last = result;
      latencyMs += result.latencyMs;
      verdict = judge(result, ids);
    } catch (error) {
      // 不该发生（超时与报错都是值），但真抛了也只是这一次失败，不是整局崩掉。
      last = null;
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
        ...audited(prompt, tools, last),
      };
    }

    why = verdict.why;
    note = verdict.why;
  }

  return {
    ...refuse(`${why}（重试 ${rounds - 1} 次仍无结果）`, attempts, latencyMs),
    ...audited(prompt, tools, last),
  };
}
