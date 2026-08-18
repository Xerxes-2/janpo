// 一个**最小的 OpenAI 兼容假端点**（票 30）。手验工具，**不进 CI**。
//
// 本机通常没有 Ollama / LM Studio，而票 30 要验的是**通道**（自定义 baseUrl 通不通、
// CORS 放行了没有、https 页面调 http 端点会不会被拦），不是模型质量。因此这里只做一件事：
// 对 `POST <base>/chat/completions` 回一段固定的 SSE，内容是调用 `choose_action` 选 id=0。
//
//   node scripts/fake-endpoint.mjs                        # 不带 CORS 头（浏览器会拦）
//   node scripts/fake-endpoint.mjs --cors http://localhost:4181   # 放行那个 origin
//   node scripts/fake-endpoint.mjs --cors '*' --https     # 端点自己上 https（自签证书）
//   node scripts/fake-endpoint.mjs --cors '*' --echo-key  # **回一条把 key 原样抄回来的 401**（票 36）
//   node scripts/fake-endpoint.mjs --fail 429             # **固定回一个失败状态**（票 47）
//   node scripts/fake-endpoint.mjs --what-if 2            # **先查两次 what-if 再出牌**（票 94）
//   node scripts/fake-endpoint.mjs --what-if inf          # **能查就查**（验上限）
//
// 选项：--port N（默认 4199）、--action-id N（默认 0）、--reason <一句话>、--cors <origin|*>、
//       --https、--quiet、--echo-key、--fail <status>、--what-if <N|inf>、--delay <ms>（票 74：正常答话前
//       固定睡这么久——「真的并发了」的唯一硬证据就是四席同问时墙钟接近一份延迟而不是几份）。
// **CORS 默认关**：本地端点默认就不放行浏览器，这份默认值本身就是要验的那个坑。

import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync } from "node:fs";
import { createServer as createHttpServer } from "node:http";
import { createServer as createHttpsServer } from "node:https";
import { tmpdir } from "node:os";
import { join } from "node:path";

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const index = argv.indexOf(name);
  return index < 0 ? fallback : argv[index + 1];
};

const port = Number.parseInt(flag("--port", "4199"), 10);
const actionId = flag("--action-id", "0");

/**
 * `choose_action` 里那句 `reason`（票 76）。**可换是有用的**：思考气泡那道闸门要证
 * 「气泡里的字来自那一手的决策记录」，而证据就是这句只可能从端点那儿来的话
 * 一字不差地出现在页面上。
 */
const reason = flag("--reason", "假端点固定选第一条，只为验通道");
const origin = flag("--cors", null);
const https = argv.includes("--https");
const quiet = argv.includes("--quiet");

/**
 * **会原样回显 key 的那种网关**（票 36）：回一条 401，报错正文里把收到的 `Authorization`
 * 与被请求的地址一字不改地抄回去。
 *
 * 官方那八家都打码（DeepSeek 只回末 4 位），所以这种端点**只可能是用户自建的**——
 * 而票 30 正是放开了这一条路。它是「provider 报错原文会进牌谱」这条通道的
 * 反向自证用的靶子：`scripts/verify-redaction.mjs` 拿它跑一手再导出牌谱。
 */
const echoKey = argv.includes("--echo-key");

/**
 * **固定回一个失败状态**（票 47）：录一份「provider 是这样报错的」固件，要的是一个真的
 * HTTP 响应过一遍 pi-ai 的适配器。可**限流与 5xx 是问不来的**——429 不听人调度，
 * 5xx 更不会点单，而它们恰恰是「值得重试」那一类的正样本。这个开关让假端点把它们演出来。
 *
 * 与 `--echo-key` 分成两个开关：那一个演的是「回显 key 的自建网关」（票 36 的靶子），
 * 正文是它的正题；这一个只关心**状态码**，正文照各家真回的措辞写。
 */
const failStatus = Number.parseInt(flag("--fail", "0"), 10);

/**
 * **正常答话前固定睡这么久**（票 74）：并发那道闸门要量「同一个响应阶段几席一起在飞时，
 * 墙钟是一份延迟还是几份」，而本机假端点不带延迟时答得太快，量不出串行与并发的差别。
 * 只延迟成功那条路：失败路径（--fail / --echo-key）要验的是别的事。
 */
const delayMs = Number.parseInt(flag("--delay", "0"), 10);

/**
 * **先查几次 what-if 再出牌**（票 94）：`--what-if 2` = 最多查两次，`--what-if inf` = 能查就查。
 *
 * **它看 `tools` 行事**：请求里没摆 `what_if` 就直接出牌——那正是 constrained sampling 下
 * 真模型的样子（工具不在单子上就点不了），也因此它能把「到上限就停且这一手照常打完」演出来。
 *
 * **无状态**：查过几次不记在进程里，而是从 prompt 里读【你查过 N 次】那一行——
 * 一个端点同时坐几席、几手交错着飞时，进程里那个计数器会串味。
 */
const whatIfText = flag("--what-if", "0");
const whatIfTimes = whatIfText === "inf" ? Number.POSITIVE_INFINITY : Number(whatIfText);

/** 点名那几档的 OpenAI 风格正文。没点名的走一句通用的。 */
const FAIL_BODIES = {
  429: { message: "Rate limit reached for requests, please slow down.", type: "rate_limit_error" },
  404: { message: "The model `no-such-model` does not exist.", type: "invalid_request_error" },
  503: {
    message: "The engine is currently overloaded, please try again later.",
    type: "server_error",
  },
};

const failBody = () =>
  FAIL_BODIES[failStatus] ?? { message: `fake endpoint says ${failStatus}.`, type: "server_error" };

/** 自签证书（只为验 mixed content 的对策那一条：端点上 https）。浏览器要 --ignore-certificate-errors。 */
function selfSigned() {
  const dir = mkdtempSync(join(tmpdir(), "janpo-fake-endpoint-"));
  const key = join(dir, "key.pem");
  const cert = join(dir, "cert.pem");
  execFileSync("openssl", [
    "req",
    "-x509",
    "-newkey",
    "rsa:2048",
    "-nodes",
    "-keyout",
    key,
    "-out",
    cert,
    "-days",
    "1",
    "-subj",
    "/CN=localhost",
    "-addext",
    "subjectAltName=DNS:localhost,IP:127.0.0.1",
  ]);
  return { key: readFileSync(key), cert: readFileSync(cert) };
}

/**
 * 放行头。**没给 --cors 就一条都不给**——那正是本地端点的出厂状态。
 *
 * `allow-headers` **把预检请求里列的那些原样回回去**（宽松服务器的常规做法）。
 * 实测过一次写死名单的版本：OpenAI SDK 会带一批 `x-stainless-*`，而
 * `Access-Control-Allow-Headers` 不支持通配符，于是被浏览器拦在预检那一步。
 */
function cors(response, request) {
  if (origin === null) return;
  response.setHeader("access-control-allow-origin", origin);
  response.setHeader(
    "access-control-allow-headers",
    request.headers["access-control-request-headers"] ?? "authorization, content-type",
  );
  response.setHeader("access-control-allow-methods", "POST, OPTIONS");
  response.setHeader("access-control-max-age", "600");
}

/**
 * 这一次该回哪一条 tool call（票 94）。
 *
 * 三道门都过才查：开了 `--what-if`、这一轮真的摆了 `what_if` 这个工具、
 * 且 prompt 里读出来的「已经查过几次」还没到这个假模型自己那个数。
 */
function toolCall(body) {
  const tools = Array.isArray(body?.tools) ? body.tools : [];
  const offered = tools
    .map((tool) => tool?.function ?? tool)
    .find((each) => each?.name === "what_if");
  const ids = offered?.parameters?.properties?.action_id?.enum ?? [];

  const prompt = (body?.messages ?? []).map((message) => message?.content ?? "").join("\n");
  const asked = Number(/^你查过 (\d+) 次/m.exec(prompt)?.[1] ?? "0");

  if (whatIfTimes > 0 && ids.length > 0 && asked < whatIfTimes) {
    return {
      name: "what_if",
      arguments: JSON.stringify({ action_id: String(ids[asked % ids.length]) }),
    };
  }
  return {
    name: "choose_action",
    arguments: JSON.stringify({ action_id: String(actionId), reason }),
  };
}

/** 一次 tool call 的 SSE，拆成三块发（真端点也是流式的）。 */
function chunks(body) {
  const call = {
    index: 0,
    id: "call_fake",
    type: "function",
    function: toolCall(body),
  };
  const base = {
    id: "fake",
    object: "chat.completion.chunk",
    created: Math.floor(Date.now() / 1000),
    model: "fake-model",
  };
  return [
    {
      ...base,
      choices: [{ index: 0, delta: { role: "assistant", content: "" }, finish_reason: null }],
    },
    { ...base, choices: [{ index: 0, delta: { tool_calls: [call] }, finish_reason: null }] },
    {
      ...base,
      choices: [{ index: 0, delta: {}, finish_reason: "tool_calls" }],
      usage: { prompt_tokens: 814, completion_tokens: 94, total_tokens: 908 },
    },
  ];
}

const handler = (request, response) => {
  const url = new URL(request.url, "http://localhost");
  if (!quiet)
    console.log(`${request.method} ${url.pathname}  origin=${request.headers.origin ?? "-"}`);

  if (request.method === "OPTIONS") {
    if (!quiet)
      console.log(
        `  预检要求放行的头：${request.headers["access-control-request-headers"] ?? "-"}`,
      );
    cors(response, request);
    response.writeHead(origin === null ? 405 : 204).end();
    return;
  }

  if (url.pathname !== "/v1/chat/completions") {
    // **只认 `/v1/chat/completions`**（Ollama / LM Studio 也是这样）：baseUrl 漏了 `/v1`
    // 这类写法错误因此落在这里，**404 是给人的线索**。
    cors(response, request);
    response.writeHead(404, { "content-type": "application/json" });
    response.end(
      JSON.stringify({ error: { message: `没有这个路径：${url.pathname}`, type: "not_found" } }),
    );
    return;
  }

  if (failStatus > 0) {
    // 正文的形状与真端点一致（`{ error: { message, type } }`），因为 pi-ai 是从这个形状里
    // 把正文抠出来拼成 `<status>: <body>` 的（`utils/error-body.ts`）——形状不对就录不到那句话。
    if (!quiet) console.log(`  固定回 ${failStatus}`);
    request.resume();
    request.on("end", () => {
      cors(response, request);
      response.writeHead(failStatus, { "content-type": "application/json" });
      response.end(JSON.stringify({ error: failBody() }));
    });
    return;
  }

  if (echoKey) {
    const bearer = (request.headers.authorization ?? "").replace(/^Bearer\s+/i, "");
    const scheme = https ? "https" : "http";
    const at = `${scheme}://${request.headers.host ?? `127.0.0.1:${port}`}${url.pathname}`;
    if (!quiet) console.log(`  收到 authorization：${bearer || "（空）"}　原样回显进 401`);

    request.resume();
    request.on("end", () => {
      cors(response, request);
      response.writeHead(401, { "content-type": "application/json" });
      response.end(
        JSON.stringify({
          error: { message: `invalid api key: ${bearer} for ${at}`, type: "invalid_request_error" },
        }),
      );
    });
    return;
  }

  // **正常那条路要读正文**（票 94）：`--what-if` 那一档得看看这一轮摆了哪几个工具。
  // 上面几条失败路径照旧 `resume()` 丢掉它——它们不看内容。
  const parts = [];
  request.on("data", (chunk) => parts.push(chunk));
  request.on("end", () => {
    let body = null;
    try {
      body = JSON.parse(Buffer.concat(parts).toString("utf8"));
    } catch (_error) {
      body = null;
    }

    const respond = () => {
      cors(response, request);
      response.writeHead(200, {
        "content-type": "text/event-stream",
        "cache-control": "no-cache",
        connection: "keep-alive",
      });
      for (const chunk of chunks(body)) response.write(`data: ${JSON.stringify(chunk)}\n\n`);
      response.end("data: [DONE]\n\n");
    };
    if (delayMs > 0) setTimeout(respond, delayMs);
    else respond();
  });
};

const server = https ? createHttpsServer(selfSigned(), handler) : createHttpServer(handler);
server.listen(port, () => {
  const scheme = https ? "https" : "http";
  const what = echoKey
    ? "固定回 401 并把收到的 key 原样抄进报错"
    : failStatus > 0
      ? `固定回 ${failStatus}`
      : `${whatIfTimes > 0 ? `先查 ${whatIfText} 次 what-if，再` : "固定"}选 action_id=${actionId}${delayMs > 0 ? `（先睡 ${delayMs} ms）` : ""}`;
  console.log(`假端点在 ${scheme}://127.0.0.1:${port}/v1（CORS：${origin ?? "不放行"}），${what}`);
});
