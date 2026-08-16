/**
 * pi-ai 的接线（票 23）。**这是整个工程里唯一真的发网络请求的文件。**
 *
 * 票 18 的实测结论逐条落在这里：
 * - 包名是 `@earendil-works/pi-ai`（spec 里的 `@mariozechner/pi-ai` 是旧 scope）。
 * - **按 provider 分入口动态导入**，不碰 `providers/all` 与 `/compat`：provider SDK 因此落在
 *   各自的懒加载 chunk 里（实测核心 9 KB gzip、OpenAI 兼容 SDK 44 KB 独立 chunk），
 *   没选的那几家一个字节都不下载。
 * - 工具参数用 **`StringEnum`** 而不是 `Type.Enum`（后者生成的 `anyOf/const` Google 不吃），
 *   配 `constrainedSampling: { type: "json_schema", strict: "prefer" }`：支持的 provider 走
 *   服务端强制 schema，不支持的自动退回普通 tool call。
 * - **OAuth 登录是 Node-only**，因此这里只有 API key 一条路；Bedrock 同理不在 provider 表里。
 * - 超时与报错都是**值**：abort → `stopReason: "aborted"`，坏 key → `"error"` + `errorMessage`。
 */

import {
  type Context,
  createModels,
  type Provider,
  StringEnum,
  type ThinkingLevel,
  type Tool,
  Type,
} from "@earendil-works/pi-ai";
import type { Ask, AskResult } from "./ask.ts";
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

/** 合法动作集 → 工具的参数 schema。**enum 是这一手动态生成的**，不是固定表。 */
function chooseAction(actionIds: string[]): Tool {
  return {
    name: "choose_action",
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

function failed(message: string, latencyMs: number): AskResult {
  return {
    stopReason: "error",
    toolCall: null,
    text: "",
    errorMessage: message,
    latencyMs,
    usage: null,
  };
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
    const message = await models.completeSimple(model, context, {
      apiKey: request.seat.api_key,
      signal: controller.signal,
      reasoning: reasoningOf(request.seat),
    });

    const call = message.content.find((block) => block.type === "toolCall");
    const text = message.content
      .filter((block) => block.type === "text")
      .map((block) => block.text)
      .join("");

    return {
      stopReason: message.stopReason,
      toolCall: call === undefined ? null : { name: call.name, arguments: call.arguments },
      text,
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
