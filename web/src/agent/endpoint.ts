/**
 * 自定义端点（票 30）：**把一个座位接到任意 OpenAI 兼容的 baseUrl 上** ——
 * 本地 Ollama、LM Studio、llama.cpp、公司内网的自建网关。
 *
 * 这个文件是纯的：**不 import pi-ai，也不发请求**。它只回答四个问题——
 * 这个座位走不走自定义端点、填的 baseUrl 读不读得懂、这一次要带什么 key、
 * 失败那句话怎么说给人听。真的接线在 `piai.ts`（那是唯一发网络请求的文件）。
 *
 * 分出来有两个理由：`loop.ts` 要问「这一次发不发得出去」而它不该拖进 provider SDK；
 * 用例要能在几十毫秒内把边界情况全过一遍。
 *
 * **官方八家一个字节都不许受影响**：下面每个出口都以「provider 是不是 `custom`」开门，
 * 不是 `custom` 的一律走原来那条路（`endpoint.test.ts` 逐条守着这件事）。
 *
 * 主持人怎么配（CORS 的环境变量、https 页面调 http 端点的对策）写在
 * `docs/host/custom-endpoint.md`，不在这里。
 */

import type { Model } from "@earendil-works/pi-ai";
import type { SeatConfig } from "./types.ts";

/**
 * 自定义端点这一项的 provider id。**它不是 pi-ai 认识的一家**：选中它时这一层
 * 按座位上的 `base_url` 现搭一个 OpenAI 兼容 provider。
 *
 * F# 侧的同名常量在 `src/Janpo.Web/Agent.fs` 的 `LlmSeat.customProvider`——**改一处要改两处**。
 *
 * **`-openai` 那个后缀是故意的**（票 46/30-A）：它原来叫 `custom`，pi-ai 哪天真有一家
 * 叫 `custom`，那一家就会静默地进不来。**这一层不认旧 id**：旧值只可能来自
 * localStorage，而那条迁移在 F# 侧的入口做完了（`LlmSeat.readProvider`）——在这里再认一次
 * 等于把刚防住的撞名又放进来。
 */
export const CUSTOM_PROVIDER = "custom-openai";

/** 一个抄得走的例子。错误提示里带着它，比让人去猜 baseUrl 的写法便宜得多。 */
const EXAMPLE = "http://localhost:11434/v1";

/** 无鉴权的本地端点用的占位 key（pi-ai 空 key 会直接抛，Ollama 又根本不看这个头）。 */
const PLACEHOLDER_KEY = "unused";

/** 这个座位走自定义端点吗。 */
export function isCustom(seat: SeatConfig): boolean {
  return seat.provider === CUSTOM_PROVIDER;
}

/** baseUrl 读得懂吗。读不懂时给的是**一句人话**，不是一个异常。 */
export type BaseUrl = { ok: true; baseUrl: string } | { ok: false; why: string };

/**
 * 读一个 baseUrl：去掉前后空白与末尾斜杠，只认 http/https。
 *
 * **不替人补 `/v1`**：网关的路径前缀五花八门（`/openai/v1`、`/api/v1`、根路径），
 * 猜错了只会让人对着一个 404 更迷惑。真填漏了由 `explainFailure` 在 404 那一刻点出来。
 */
export function readBaseUrl(raw: string): BaseUrl {
  const text = raw.trim();
  if (text === "") return { ok: false, why: `自定义端点没有填 baseUrl（例：${EXAMPLE}）` };

  let url: URL;
  try {
    url = new URL(text);
  } catch {
    return { ok: false, why: `自定义端点的 baseUrl 读不懂：「${text}」（例：${EXAMPLE}）` };
  }
  if (url.protocol !== "http:" && url.protocol !== "https:") {
    return {
      ok: false,
      why: `自定义端点的 baseUrl 只能是 http:// 或 https://，得到「${text}」（例：${EXAMPLE}）`,
    };
  }

  return { ok: true, baseUrl: text.replace(/\/+$/, "") };
}

/**
 * 这一次请求缺什么配置吗（缺就别发，白等一个来回）。缺什么就是那句原因，齐了是 null。
 *
 * 两条路各缺各的：**官方八家缺 key**（那句话与票 23 的一字不差），
 * **自定义端点缺 baseUrl**——它反而不要求 key，本地端点通常根本不校验。
 */
export function missingConfig(seat: SeatConfig): string | null {
  if (isCustom(seat)) {
    const url = readBaseUrl(seat.base_url);
    return url.ok ? null : url.why;
  }

  return seat.api_key.trim() === "" ? `没有填 ${seat.provider} 的 API key` : null;
}

/** 这一次带哪把 key。自定义端点没填时带一个占位串，好让无鉴权的本地端点跑得起来。 */
export function keyFor(seat: SeatConfig): string {
  if (isCustom(seat) && seat.api_key.trim() === "") return PLACEHOLDER_KEY;
  return seat.api_key;
}

/**
 * 自定义端点的模型：**当场造一个**，因为这种端点没有模型目录可查
 * （官方八家走的是 `models.getModel(provider, id)`）。
 *
 * 模型名保持自由文本：本地模型的名字五花八门（`qwen3:8b`、`gpt-oss-20b@q4`、
 * 一个文件路径），下拉框只会挡路。填错的代价是端点回一句「没这个模型」，那是**值**。
 */
export function customModel(baseUrl: string, model: string): Model<"openai-completions"> {
  return {
    id: model.trim(),
    name: model.trim(),
    api: "openai-completions",
    provider: CUSTOM_PROVIDER,
    baseUrl,
    // 思考档位由座位配置管；本地端点给不给 thinking 块是它自己的事，这里不预设。
    reasoning: false,
    input: ["text"],
    // 本地模型没有价目表：一律 0，别在决策记录里编一个价出来。
    cost: { input: 0, output: 0, cacheRead: 0, cacheWrite: 0 },
    contextWindow: 32768,
    maxTokens: 8192,
  };
}

/**
 * 把 provider 那句报错翻成主持人读得懂的话。**只翻自定义端点这一条路**，
 * 官方八家原样透传（那些报错本来就是各家自己的话，改写只会挡住线索）。
 *
 * 两种最常撞的：
 * - 浏览器根本没把请求发出去（端点没起、地址写错、**CORS 没放行**、**https 页面调 http 端点**）
 *   —— OpenAI SDK 把它统一成一句 `Connection error.`，光看这句谁也不知道该去查什么；
 * - 端点回了 404 —— 十有八九是 baseUrl 漏了 `/v1`。
 *
 * 原始报错一律留在末尾：翻译是给人指路的，不是拿走证据。
 */
export function explainFailure(seat: SeatConfig, message: string): string {
  if (!isCustom(seat)) return message;

  const where = seat.base_url.trim();
  if (/connection error|failed to fetch|networkerror|load failed/i.test(message)) {
    return (
      `连不上自定义端点 ${where}：浏览器没能把请求发出去。` +
      "常见原因：端点没起、地址写错、端点没放行本页面的跨域请求（CORS），" +
      "或者本页面是 https 而端点是 http（mixed content 被拦）。" +
      `配法见 docs/host/custom-endpoint.md。原始报错：${message}`
    );
  }
  if (/^404\b/.test(message)) {
    return `自定义端点 ${where} 回了 404：baseUrl 多半漏了 /v1 之类的路径前缀。原始报错：${message}`;
  }

  return message;
}
