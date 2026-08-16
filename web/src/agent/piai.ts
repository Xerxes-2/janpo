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
 * - **自定义端点**（票 30）不在那张 provider 表里：它按主持人填的 baseUrl 现搭一家，
 *   接本地 Ollama / LM Studio / 自建网关。CORS 与 mixed content 两个坑写在
 *   `docs/host/custom-endpoint.md`，判读与错误话术在 `endpoint.ts`。
 * - 走 **`streamSimple`** 而不是 `completeSimple`（票 26）：后者就是
 *   `streamSimple(…).result()`，而前者多给一条 `thinking_delta` 流——思考全文要进牌谱，
 *   M2 的思考气泡还要实时显示它。
 */

import {
  type Api,
  type AssistantMessage,
  type Context,
  createModels,
  createProvider,
  type Model,
  type MutableModels,
  type Provider,
  type ThinkingLevel,
} from "@earendil-works/pi-ai";
import type { Ask, AskResult } from "./ask.ts";
import {
  CUSTOM_PROVIDER,
  customModel,
  explainFailure,
  isCustom,
  keyFor,
  readBaseUrl,
} from "./endpoint.ts";
import { chooseAction } from "./tools.ts";
import type { SeatConfig } from "./types.ts";

/**
 * provider id → 那一家的工厂。**逐条写死是有意的**：动态拼路径的 `import()` 打不出
 * 懒加载 chunk（打包器分析不了），而且这张表同时就是「本平台支持哪几家」的唯一清单。
 *
 * 这里没有 Amazon Bedrock（Node-only），也没有靠订阅制 OAuth 登录的那几家
 * （openai-codex / github-copilot / …）：浏览器里它们必然失败，列出来只会骗人。
 * 与 `LlmSeat.providers`（F# 侧配置面板的选项）保持同一份名单。**自定义端点不在这里**：
 * 它不是“支持的一家”，而是一个现搭的工厂（见 `customProvider`）。
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

/**
 * 自定义端点（票 30）：**按主持人填的 baseUrl 现搭一家**，接本地 Ollama / LM Studio /
 * 自建 OpenAI 兼容网关。它不在 `PROVIDERS` 表里——那张表是「本平台支持哪几家」的清单，
 * 而这一项支持的是「你自己那一家」。
 *
 * 与官方八家的两处不同：
 * - **模型目录是空的**，因此模型由 `customModel` 当场造（名字保持自由文本）；
 * - **auth 不查环境变量**（浏览器里根本没有），空 key 也照样 resolve ——
 *   本地端点通常不校验它，真要 key 的网关就在配置面板里填。
 */
async function customProvider(baseUrl: string): Promise<Provider> {
  const { openAICompletionsApi } = await import(
    "@earendil-works/pi-ai/api/openai-completions.lazy"
  );

  return createProvider({
    id: CUSTOM_PROVIDER,
    name: "自定义端点",
    baseUrl,
    auth: {
      apiKey: {
        name: "自定义端点的 API key",
        resolve: async ({ credential }) => ({
          auth: { apiKey: credential?.key ?? "" },
          source: "配置面板",
        }),
      },
    },
    models: [],
    api: openAICompletionsApi(),
  });
}

/** 接上一家的结果：要么拿到那个模型，要么一句中文原因（与 `readBaseUrl` 同一个形状）。 */
type Wired = { ok: true; model: Model<Api> } | { ok: false; why: string };

/**
 * 接上这个座位那一家，并找出它要的那个模型。**认不出来时回一句中文原因**（不抛）。
 *
 * 官方八家走的是原来那三步（工厂 → `setProvider` → 查目录），一个字节没变；
 * 自定义端点那条路查不了目录，因此现搭 provider、现造模型。
 */
async function wire(models: MutableModels, seat: SeatConfig): Promise<Wired> {
  if (isCustom(seat)) {
    const url = readBaseUrl(seat.base_url);
    if (!url.ok) return url;

    models.setProvider(await customProvider(url.baseUrl));
    return { ok: true, model: customModel(url.baseUrl, seat.model) };
  }

  const factory = PROVIDERS[seat.provider];
  if (factory === undefined) return { ok: false, why: `不认识的 provider：${seat.provider}` };

  models.setProvider(await factory());
  const model = models.getModel(seat.provider, seat.model);
  if (model === undefined) {
    return { ok: false, why: `${seat.provider} 的模型目录里没有 ${seat.model}` };
  }
  return { ok: true, model };
}

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

  const models = createModels();
  const wired = await wire(models, request.seat);
  if (!wired.ok) return failed(wired.why, elapsed());
  const model = wired.model;

  // 固定 preamble 走 system（票 31）：各家的缓存语义更认它，user 消息只剩历史 + 现况 + 动作。
  const context: Context = {
    systemPrompt: request.system,
    messages: [{ role: "user", content: request.prompt, timestamp: Date.now() }],
    tools: [chooseAction(request.actionIds)],
  };

  // 超时就是 abort：pi-ai 把它记成 `stopReason: "aborted"`，与 provider 报错走同一条兜底路。
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), request.seat.timeout_ms);

  try {
    const stream = models.streamSimple(model, context, {
      // 自定义端点没填 key 时带一个占位串（官方八家原样交出去）。
      apiKey: keyFor(request.seat),
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
      // 自定义端点的“Connection error.”在这里变成一句说得清的话（官方八家原样透传）。
      errorMessage:
        message.errorMessage === undefined
          ? null
          : explainFailure(request.seat, message.errorMessage),
      latencyMs: elapsed(),
      // 缓存那两项是票 29b 的验收数据：前缀命中了多少 token，账单上看得见。
      usage: {
        input: message.usage.input,
        output: message.usage.output,
        cacheRead: message.usage.cacheRead,
        cacheWrite: message.usage.cacheWrite,
      },
    };
  } catch (error) {
    // 实测里 abort 与坏 key 都不抛，但适配器层真抛了也不能把牌桌卡住。
    const aborted = controller.signal.aborted;
    return {
      ...failed(explainFailure(request.seat, String(error)), elapsed()),
      stopReason: aborted ? "aborted" : "error",
    };
  } finally {
    clearTimeout(timer);
  }
};
