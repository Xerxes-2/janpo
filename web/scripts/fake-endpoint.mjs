// 一个**最小的 OpenAI 兼容假端点**（票 30）。手验工具，**不进 CI**。
//
// 本机通常没有 Ollama / LM Studio，而票 30 要验的是**通道**（自定义 baseUrl 通不通、
// CORS 放行了没有、https 页面调 http 端点会不会被拦），不是模型质量。因此这里只做一件事：
// 对 `POST <base>/chat/completions` 回一段固定的 SSE，内容是调用 `choose_action` 选 id=0。
//
//   node scripts/fake-endpoint.mjs                        # 不带 CORS 头（浏览器会拦）
//   node scripts/fake-endpoint.mjs --cors http://localhost:4181   # 放行那个 origin
//   node scripts/fake-endpoint.mjs --cors '*' --https     # 端点自己上 https（自签证书）
//
// 选项：--port N（默认 4199）、--action-id N（默认 0）、--cors <origin|*>、--https、--quiet。
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
const origin = flag("--cors", null);
const https = argv.includes("--https");
const quiet = argv.includes("--quiet");

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

/** 一次 tool call 的 SSE，拆成三块发（真端点也是流式的）。 */
function chunks() {
  const call = {
    index: 0,
    id: "call_fake",
    type: "function",
    function: {
      name: "choose_action",
      arguments: JSON.stringify({
        action_id: String(actionId),
        reason: "假端点固定选第一条，只为验通道",
      }),
    },
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

  request.resume();
  request.on("end", () => {
    cors(response, request);
    response.writeHead(200, {
      "content-type": "text/event-stream",
      "cache-control": "no-cache",
      connection: "keep-alive",
    });
    for (const chunk of chunks()) response.write(`data: ${JSON.stringify(chunk)}\n\n`);
    response.end("data: [DONE]\n\n");
  });
};

const server = https ? createHttpsServer(selfSigned(), handler) : createHttpServer(handler);
server.listen(port, () => {
  const scheme = https ? "https" : "http";
  console.log(
    `假端点在 ${scheme}://127.0.0.1:${port}/v1（CORS：${origin ?? "不放行"}），固定选 action_id=${actionId}`,
  );
});
