/**
 * 兜底闭环的那个循环（票 23）：**问 → 判读 → 不行就带着理由重问 → 用尽了就说「交不出来」**。
 *
 * **不是每种失败都值得重问**（票 47）：“再问一遍还是一样”的那几类（认证失败、
 * 请求本身不合法、模型名不存在）**第一次就收手**，判据在 `retry.ts`。
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
 *
 * **交上去的审计数据只有尾部**（票 31）：前缀（固定 preamble + 事件流历史）是派生物，
 * 牌谱里另外整场存一份 preamble 与渲染版本号，重建走 `rebuildMessages`。
 */

import type { Ask, AskResult } from "./ask.ts";
import { missingConfig } from "./endpoint.ts";
import { messagesOf, promptSections } from "./prompt.ts";
import { redactSecrets } from "./redact.ts";
import { renderVersion } from "./render-version.ts";
import { type Retry, retryOf, WORTH } from "./retry.ts";
import { resolveTemplate } from "./template.ts";
import { CHOOSE_ACTION, toolsShape } from "./tools.ts";
import type { ActionOption, DecideRequest, DecideResponse } from "./types.ts";
import { WHAT_IF, type WhatIf, whatIfExhausted } from "./what-if.ts";

/**
 * 一次回答的判读。失败那一支带着**这条失败值不值得再问一遍**（票 47）：
 * 五类失败就在这个文件里定义，它们的重试判据也就在这里声明。
 */
type Verdict =
  | { ok: true; id: number; reason: string | null }
  | { ok: false; why: string; retry: Retry };

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
 * 这一手哪几条动作是**打牌**（票 94）。what-if 工具的 enum 就是它们。
 *
 * 判据是动作消息的 `type`，不是中文 label：**Agent 层认识 id 不认识动作**（ADR-0005），
 * 而这一句只是读一个 wire 字段来分类，不解释它、也不重造一条动作。
 * 碰 / 吃 / 立直宣言那几条没有「打完之后」可算，摆进 enum 等于邀模型烧掉一次额度。
 */
function discardIdsOf(options: ActionOption[]): string[] {
  return options
    .filter((option) => option.action.type === "dahai")
    .map((option) => String(option.id));
}

/**
 * 这一次是不是一次**合格的 what-if 查询**（票 94）；不是就返回 null，走原来那条判读。
 *
 * 三道门全过才算：调的是 `what_if`、这一轮**真把这个工具给了它**（`offered` 非空）、
 * 且 `action_id` 落在这一轮那份 enum 里。后两道是故意的：问满之后、或者根本没给过这个工具时
 * 它还拿出一条 `what_if`，那就该当「调了别的工具」报回去重问，而不是默默多给它一次额度
 * ——否则上限就只是一句话而不是一道门。
 */
function whatIfCall(result: AskResult, offered: string[]): WhatIf | null {
  const call = result.toolCall;
  if (call === null || call.name !== WHAT_IF || offered.length === 0) return null;

  const raw = String(call.arguments.action_id ?? "").trim();
  if (!offered.includes(raw)) return null;

  return { id: Number.parseInt(raw, 10) };
}

/**
 * 这次回答能用吗。**四类失败在这里合流**（超时 / provider 报错 / 格式跑偏 / id 非法），
 * 之后走同一条兜底路径——但**重不重问各是各的**（票 47）。
 *
 * 这里只有 provider 报错那一支需要分类：其余四类都是**模型这一次**的事
 * （没答完、没调工具、调错工具、给了个不存在的 id），把错在哪告诉它，
 * 下一轮常常就对了（`loop.test.ts` 里那条「第二次答对了」就是它）。
 */
function judge(result: AskResult, ids: Set<number>): Verdict {
  // 票 18 实测：这两样都是值，不是异常。
  if (result.stopReason === "aborted") {
    // 超时是「这一次」的事：同一份请求再问一遍完全可能答得完。
    return { ok: false, why: `模型超时（${result.latencyMs} ms 没答完）`, retry: WORTH };
  }
  if (result.stopReason === "error") {
    // **唯一需要分类的一支**（票 47）：401 与 429 都是 provider 报错，
    // 前者再问一遍必然还是 401，后者可能就过了。
    return {
      ok: false,
      why: `provider 报错：${result.errorMessage ?? "没给原因"}`,
      retry: retryOf(result.errorMessage),
    };
  }

  const call = result.toolCall;
  if (call === null) {
    return {
      ok: false,
      why: `模型没有调用 ${CHOOSE_ACTION}，只回了一段话：「${excerpt(result.text)}」`,
      retry: WORTH,
    };
  }
  if (call.name !== CHOOSE_ACTION) {
    return { ok: false, why: `模型调了别的工具：${call.name}`, retry: WORTH };
  }

  const id = strictInt(call.arguments.action_id);
  if (id === null) {
    return {
      ok: false,
      why: `action_id 不是一个 id：${JSON.stringify(call.arguments.action_id)}`,
      retry: WORTH,
    };
  }
  if (!ids.has(id)) {
    return { ok: false, why: `action_id=${id} 不在这一手的合法动作集里`, retry: WORTH };
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

/** 审计里那几项「问出去的是什么」。一次请求都没发时全是空串与空表。 */
interface Asked {
  /** 尾部：牌谱存的就是它（票 31）。 */
  tail: string;
  /** system 消息正文：**一局内不变，局间可换**，牌谱按「座位 + 渲染版本」各存一份。 */
  preamble: string;
  /** 渲染版本号：模板 id + 模板哈希 + 渲染器摘要，**算出来的**（票 43）。 */
  version: string;
  /** 工具定义的**形状**（enum 留空）：真发出去的那份 = 形状 + 下面那一行的 id 集。 */
  tools: string;
  /** 这一手的合法动作 id 集（工具定义里唯一随手变的那一项）。 */
  actionIds: number[];
}

const NOTHING_ASKED: Asked = { tail: "", preamble: "", version: "", tools: "", actionIds: [] };

/** 这一手的 token 账单。**一次都没问成时一直是 null**（没有账单可记）。 */
type Billed = DecideResponse["usage"];

/**
 * 把一轮的账单加进这一手的总数（票 94）。
 *
 * **记的是「这一手」而不是「最后一轮」**：`TokenUsage` 自己写的就是「这一手的 token 账单」，
 * 而 ToolSearch 档一手可能真发出去好几次请求（每查一次就多一次）——只记最后一轮的话，
 * 那一档的账单在牌谱里会与 Bare 档长得一模一样，而这一票要量的正是那个乘数。
 *
 * **裁决 26-16 不受影响**：那一条说的是**审计四项**（prompt / 工具定义 / 原始输出 / thinking）
 * 只记最后一轮（否则牌谱体积翻几倍），它们照旧只记最后一轮。账单是四个数，加起来不胀。
 * **零重试、零查询的那一手（即 Bare / Assisted 的常态）加完与从前逐字相同**。
 */
function billed(into: Billed, result: AskResult | null): Billed {
  const usage = result?.usage ?? null;
  if (usage === null) return into;

  const base = into ?? { input: 0, output: 0, cache_read: 0, cache_write: 0 };
  return {
    input: base.input + usage.input,
    output: base.output + usage.output,
    cache_read: base.cache_read + (usage.cacheRead ?? 0),
    cache_write: base.cache_write + (usage.cacheWrite ?? 0),
  };
}

/**
 * 一轮问话里审计要的那几项。一次都没问成时 `result` 是 null。
 *
 * **token 账单也在这里过界**（票 29b）：`cache_read` 是「前缀真的命中了」的唯一证据，
 * 由 `TablePage.settle` 收进这一手的 `DecisionRecord`，页面上看得见。
 * 审计那四项记的是**最后一轮**那次（裁决 26-16）；**账单记的是这一手的合计**（`billed`）。
 */
function audited(asked: Asked, result: AskResult | null, usage: Billed) {
  return {
    prompt_tail: asked.tail,
    preamble: asked.preamble,
    render_version: asked.version,
    tools: asked.tools,
    action_ids: asked.actionIds,
    output: rawOutput(result),
    thinking: result?.thinking ?? null,
    usage,
  };
}

/**
 * 交不出来那句话的收尾。**「没重试」与「少试了」必须一眼分得开**（票 47）——
 * 这句话会原样印在牌桌上那一手的兜底原因里（`TablePage` 读的就是它），
 * 也会进牌谱的 `fallback`。两边读的是**同一个字符串**，因此不必也不该各写一份。
 */
function gaveUpBecause(why: string, retry: Retry, rounds: number): string {
  return retry.worth
    ? `${why}（重试 ${rounds - 1} 次仍无结果）`
    : `${why}（没有重试：${retry.because}，再问一遍还是一样）`;
}

/** 一条「我交不出来」。审计那几项默认是空的，真问过的那几条路上再盖上去。 */
function refuse(why: string, attempts: number, latencyMs: number): DecideResponse {
  return {
    action_id: null,
    reason: null,
    failure: why,
    attempts,
    latency_ms: latencyMs,
    ...audited(NOTHING_ASKED, null, null),
  };
}

/**
 * 决策一手。**永远 resolve，永远不 reject**：所有失败都变成 `failure` 字段，
 * 由 F# 侧据此兜底。`retry_limit` 是重试次数上限，因此最多问 `retry_limit + 1` 次。
 *
 * **回执在这里过一道打码**（票 36），而且**只在这里**：provider 的报错原文原样流进
 * `failure`、`output` 与重试那一轮的 prompt，而这三样连同页面上那句话，全都是从这份
 * 回执组装出来的（`TablePage.settle`）—— 这就是那条通道唯一的出海口。每个 sink
 * 各打一遍的做法漏一个就等于没做，而 sink 会长出来。
 *
 * 代价说清楚：牌谱里存的 `prompt_tail` 因此是**打码后的**那一份，与真发出去的那一次
 * 差这几个字。真发出去的那一份不打码是有意的 —— 那把 key 本来就是这个端点给回来的，
 * 抄回去它不多知道一个字节，而牌谱是要发给别人的。
 */
export async function decideWith(ask: Ask, request: DecideRequest): Promise<DecideResponse> {
  return redactSecrets(await answered(ask, request), request.seat);
}

/** 打码前的那一份回执。**除了上面那一行没有第二个调用方**，因此它出不去这个文件。 */
async function answered(ask: Ask, request: DecideRequest): Promise<DecideResponse> {
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
  const actionIds = options.map((option) => option.id);
  const enumIds = actionIds.map(String);
  // 模板一手解一次（重试的那几轮共用）：同一手里前缀不得因为重试而变。
  const template = resolveTemplate(request.seat);
  const version = renderVersion(template);
  const rounds = Math.max(1, request.retry_limit + 1);
  // ToolSearch 档才有的那一批（票 94）：只有打牌那几条查得出「打完之后」。
  const discardIds = request.seat.tier === "tool_search" ? discardIdsOf(options) : ([] as string[]);

  let attempts = 0;
  let latencyMs = 0;
  let why = "没问出结果";
  let retry: Retry = WORTH;
  let note: string | null = null;
  let asked: Asked = NOTHING_ASKED;
  let last: AskResult | null = null;
  let usage: Billed = null;
  /** 这一手已经查过的那几条。**只增不减**，它同时是上限的计数器与尾部那一节的内容。 */
  const queries: WhatIf[] = [];

  while (attempts < rounds) {
    // 问满了就不再给这个工具（空表）——**上限在这一行执行**，不是求模型自觉。
    const offered = whatIfExhausted(queries) ? [] : discardIds;

    // 一轮只渲染一次：发出去的两条消息与存进牌谱的尾部必须是同一次渲染的产物。
    const sections = promptSections(request.decision, request.seat.tier, note, template, queries);
    const messages = messagesOf(sections);
    asked = {
      tail: sections.present,
      preamble: sections.preamble,
      version,
      tools: toolsShape(request.seat.tier),
      actionIds,
    };

    let verdict: Verdict;
    try {
      const result = await ask({
        seat: request.seat,
        system: messages.system,
        prompt: messages.user,
        actionIds: enumIds,
        whatIfIds: offered,
      });
      last = result;
      latencyMs += result.latencyMs;
      usage = billed(usage, result);

      // **查一次不算一次回答**（票 94）：它不吃重试额度，只吃 what-if 自己那个上限。
      // 循环因此仍旧有界：最多 `WHAT_IF_LIMIT` 次查 + `rounds` 次回答。
      // 问满之后它再调 `what_if` 就落回 `judge` 的「调了别的工具」——而那一条路上
      // `tools` 里根本没有它，模型要是还能拿出来，那就是端点真的不听 schema。
      const query = whatIfCall(result, offered);
      if (query !== null) {
        queries.push(query);
        continue;
      }

      verdict = judge(result, ids);
    } catch (error) {
      // 不该发生（超时与报错都是值），但真抛了也只是这一次失败，不是整局崩掉。
      // 归「值得重试」：走到这里就是适配器出了意外，而意外是分不清的那一类。
      last = null;
      verdict = { ok: false, why: `Agent 层抛了异常：${String(error)}`, retry: WORTH };
    }
    attempts += 1;

    if (verdict.ok) {
      return {
        action_id: verdict.id,
        reason: verdict.reason,
        failure: null,
        attempts,
        latency_ms: latencyMs,
        ...audited(asked, last, usage),
      };
    }

    why = verdict.why;
    retry = verdict.retry;
    // **不值得重试的立刻走兜底**（票 47）：重试发的是同一份请求，得到的也是同一个答案。
    // 27 号实测：一整场 255 次请求里有 170 次是这么烧掉的。
    if (!retry.worth) break;
    note = verdict.why;
  }

  return {
    ...refuse(gaveUpBecause(why, retry, rounds), attempts, latencyMs),
    ...audited(asked, last, usage),
  };
}
