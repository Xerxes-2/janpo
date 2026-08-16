/**
 * pi-ai 的接线（票 23）。**这是整个工程里唯一真的发网络请求的文件。**
 *
 * 票 18 的实测结论逐条落在这里：
 * - 包名是 `@earendil-works/pi-ai`（spec 里的 `@mariozechner/pi-ai` 是旧 scope）。
 * - **按 provider 分入口动态导入**，不碰 `providers/all` 与 `/compat`：provider SDK 因此落在
 *   各自的懒加载 chunk 里（实测核心 9 KB gzip、OpenAI 兼容 SDK 44 KB 独立 chunk），
 *   没选的那几家一个字节都不下载。
 * - 工具定义在 `tools.ts`（票 18 实测的 `StringEnum` + `constrainedSampling` 都在那里）。
 * - **OAuth 登录是 Node-only**，因此这里只有 API key 一条路；Bedrock 同理不在 provider 表里。
 * - 超时与报错都是**值**：abort → `stopReason: "aborted"`，坏 key → `"error"` + `errorMessage`。
 * - 走 **`streamSimple`** 而不是 `completeSimple`（票 26）：后者就是
 *   `streamSimple(…).result()`，而前者多给一条 `thinking_delta` 流——思考全文要进牌谱，
 *   M2 的思考气泡还要实时显示它。
 */

import {
  type AssistantMessage,
  type Context,
  createModels,
  type Provider,
  type ThinkingLevel,
} from "@earendil-works/pi-ai";
import type { Ask, AskResult } from "./ask.ts";
import { chooseAction } from "./tools.ts";
import type { SeatConfig } from "./types.ts";

/**
 * provider id → 那一家的工厂。**逐条写死是有意的**：动态拼路径的 `import()` 打不出
 * 懒加载 chunk（打包器分析不了），而且这张表同时就是「本平台支持哪几家」的唯一清单。
 *
 * 这里没有 Amazon Bedrock（Node-only），也没有靠订阅制 OAuth 登录的那几家
 * （openai-codex / github-copilot / …）：浏览器里它们必然失败，列出来只会骗人。
 * 与 `LlmSeat.providers`（F# 侧配置面板的选项）保持同一份名单。
 */
const PROVIDERS: Record<string, () => Promise<Provider>> = {
  deepseek: async () =>
    (await import("@earendil-works/pi-ai/providers/deepseek")).deepseekProvider(),
  anthropic: async () =>
    (await import("@earendil-works/pi-ai/providers/anthropic")).anthropicProvider(),
  openai: async () => (await import("@earendil-works/pi-ai/providers/openai")).openaiProvider(),
  google: async () => (await import("@earendil-works/pi-ai/providers/google")).googleProvider(),
  openrouter: async () =>
    (await import("@earendil-works/pi-ai/providers/openrouter")).openrouterProvider(),
  xai: async () => (await import("@earendil-works/pi-ai/providers/xai")).xaiProvider(),
  groq: async () => (await import("@earendil-works/pi-ai/providers/groq")).groqProvider(),
  mistral: async () => (await import("@earendil-works/pi-ai/providers/mistral")).mistralProvider(),
};

function failed(message: string, latencyMs: number): AskResult {
  return {
    stopReason: "error",
    toolCall: null,
    text: "",
    thinking: null,
    errorMessage: message,
    latencyMs,
    usage: null,
  };
}

/** 模型说的话（一段话可能被拆成好几块）。 */
function textOf(message: AssistantMessage): string {
  return message.content
    .filter((block) => block.type === "text")
    .map((block) => block.text)
    .join("");
}

/** 末态里的思考块（provider 常常只在流结束时给齐一块）。 */
function thinkingOf(message: AssistantMessage): string {
  return message.content
    .filter((block) => block.type === "thinking")
    .map((block) => block.thinking)
    .join("");
}

/** `off` 就是不传 `reasoning`（pi-ai 的 `ThinkingLevel` 里没有 "off"）。 */
function reasoningOf(seat: SeatConfig): ThinkingLevel | undefined {
  return seat.thinking === "off" ? undefined : seat.thinking;
}

/** 真的去问一次模型。**它不抛**：一切失败都变成一条带 `stopReason` 的记录。 */
export const piAsk: Ask = async (request) => {
  const started = performance.now();
  const elapsed = () => Math.round(performance.now() - started);

  const factory = PROVIDERS[request.seat.provider];
  if (factory === undefined) {
    return failed(`不认识的 provider：${request.seat.provider}`, elapsed());
  }

  const models = createModels();
  models.setProvider(await factory());
  const model = models.getModel(request.seat.provider, request.seat.model);
  if (model === undefined) {
    return failed(`${request.seat.provider} 的模型目录里没有 ${request.seat.model}`, elapsed());
  }

  const context: Context = {
    messages: [{ role: "user", content: request.prompt, timestamp: Date.now() }],
    tools: [chooseAction(request.actionIds)],
  };

  // 超时就是 abort：pi-ai 把它记成 `stopReason: "aborted"`，与 provider 报错走同一条兜底路。
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), request.seat.timeout_ms);

  try {
    const stream = models.streamSimple(model, context, {
      apiKey: request.seat.api_key,
      signal: controller.signal,
      reasoning: reasoningOf(request.seat),
    });

    // 收流是为了 **thinking**：`completeSimple` 就是 `streamSimple(…).result()`，
    // 少的正是这条增量。M2 的思考气泡要边下边显示，接的也是这里。
    let streamed = "";
    for await (const event of stream) {
      if (event.type === "thinking_delta") streamed += event.delta;
    }

    const message = await stream.result();
    const call = message.content.find((block) => block.type === "toolCall");
    // 收齐的思考块优先（provider 可能只在末态给），流里拼的那份兜底。
    const thinking = thinkingOf(message) || streamed;

    return {
      stopReason: message.stopReason,
      toolCall: call === undefined ? null : { name: call.name, arguments: call.arguments },
      text: textOf(message),
      thinking: thinking === "" ? null : thinking,
      errorMessage: message.errorMessage ?? null,
      latencyMs: elapsed(),
      usage: { input: message.usage.input, output: message.usage.output },
    };
  } catch (error) {
    // 实测里 abort 与坏 key 都不抛，但适配器层真抛了也不能把牌桌卡住。
    const aborted = controller.signal.aborted;
    return {
      ...failed(String(error), elapsed()),
      stopReason: aborted ? "aborted" : "error",
    };
  } finally {
    clearTimeout(timer);
  }
};
