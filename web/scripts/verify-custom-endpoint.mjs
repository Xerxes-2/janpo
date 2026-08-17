// 自定义端点的**人工验收**（票 30）。**手动跑，不进 CI**：它要起真浏览器与真 HTTP 端点。
//
// 验的是**通道**，不是模型质量：假端点（`scripts/fake-endpoint.mjs`）固定回一条
// `choose_action(action_id=0)`，因此「模型答上话了」等价于「请求真的通到端点又回来了」。
//
//   node scripts/verify-custom-endpoint.mjs --mode blocked   # http 页面 + 端点不放行 CORS → 被拦
//   node scripts/verify-custom-endpoint.mjs --mode allowed   # http 页面 + 端点放行本页 origin → 通
//   node scripts/verify-custom-endpoint.mjs --mode mixed     # https 页面 + http 端点（mixed content）
//   node scripts/verify-custom-endpoint.mjs --mode https     # https 页面 + https 端点
//   node scripts/verify-custom-endpoint.mjs --mode lan --endpoint-host 192.168.x.x
//                                                            # https 页面 + 非回环的 http 端点
//   node scripts/verify-custom-endpoint.mjs --mode lna       # **公网 https 页面** → 本地端点（要外网）
//
// 前五种都真的开一桌、让模型座位答一手；`lna` 那种不碰我们的页面，它验的是**浏览器自己的规矩**
// （公网页面访问本地地址要「本地网络访问」权限），因此只在一个第三方 https 页面里裸 fetch 一次。
//
// 每种模式都把**浏览器实际报的那句话**打出来（console + requestfailed），报告里抄的就是它。
// 选项：--mode、--endpoint-host、--public-page、--seat N（默认 1）、--page-port、--endpoint-port、--budget ms、
//       --base-url（盖掉座位上的 baseUrl，用来演填错的情形：`--mode allowed --base-url http://127.0.0.1:4199`
//       就是「漏了 /v1」那一类）。

import { execFileSync, spawn } from "node:child_process";
import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { preview } from "vite";
import { chromeExecutable, missingChrome } from "./chrome.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const index = argv.indexOf(name);
  return index < 0 ? fallback : argv[index + 1];
};

const mode = flag("--mode", "blocked");
const seat = Number.parseInt(flag("--seat", "1"), 10);
const pagePort = Number.parseInt(flag("--page-port", "4183"), 10);
const endpointPort = Number.parseInt(flag("--endpoint-port", "4199"), 10);
const endpointHost = flag("--endpoint-host", "127.0.0.1");
const publicPage = flag("--public-page", "https://example.com");
const baseUrlOverride = flag("--base-url", null);
const budgetMs = Number.parseInt(flag("--budget", "60000"), 10);

/**
 * 五种页面模式各自的开关：页面走不走 https、端点走不走 https、端点放不放行、端点在哪个 host。
 *
 * `expect` 是**实测出来的**结果，不是拍脑袋的预期 —— mixed / lan 两栏原本猜的是「被拦」，
 * 实测是「通」：Chrome 不把 loopback 当不安全内容，页面自己在本地地址空间里时也不查
 * 本地网络访问权限。结论与原始输出写在 `.scratch/.../reports/30-custom-endpoint.md`。
 */
const MODES = {
  blocked: {
    pageHttps: false,
    endpointHttps: false,
    cors: false,
    host: "127.0.0.1",
    expect: "troubled",
  },
  allowed: {
    pageHttps: false,
    endpointHttps: false,
    cors: true,
    host: "127.0.0.1",
    expect: "spoke",
  },
  mixed: { pageHttps: true, endpointHttps: false, cors: true, host: "127.0.0.1", expect: "spoke" },
  https: { pageHttps: true, endpointHttps: true, cors: true, host: "127.0.0.1", expect: "spoke" },
  lan: { pageHttps: true, endpointHttps: false, cors: true, host: endpointHost, expect: "spoke" },
};

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

/** 自签证书（页面用；端点那张由假端点自己生成）。 */
function selfSigned() {
  const dir = mkdtempSync(join(tmpdir(), "janpo-page-cert-"));
  const key = join(dir, "key.pem");
  const cert = join(dir, "cert.pem");
  // biome-ignore format: 一行一个 openssl 参数比折行好读
  execFileSync("openssl", [
    "req", "-x509", "-newkey", "rsa:2048", "-nodes",
    "-keyout", key, "-out", cert, "-days", "1",
    "-subj", "/CN=localhost",
    "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1",
  ]);
  return { key: readFileSync(key), cert: readFileSync(cert) };
}

/** 起一个假端点，回它的日志数组（每行一条：收到的请求 / 预检要放行的头）。 */
function startEndpoint(args) {
  const child = spawn("node", ["scripts/fake-endpoint.mjs", ...args], {
    cwd: webRoot,
    stdio: ["ignore", "pipe", "inherit"],
  });
  const log = [];
  child.stdout.on("data", (chunk) => log.push(String(chunk).trim()));
  return { child, log };
}

/** 真开一桌：模型坐一席，看它答得上话还是兜底。 */
async function runTable(plan) {
  const pageOrigin = `${plan.pageHttps ? "https" : "http"}://localhost:${pagePort}`;
  const baseUrl =
    baseUrlOverride ?? `${plan.endpointHttps ? "https" : "http"}://${plan.host}:${endpointPort}/v1`;

  const endpointArgs = ["--port", String(endpointPort)];
  if (plan.cors) endpointArgs.push("--cors", pageOrigin);
  if (plan.endpointHttps) endpointArgs.push("--https");
  const endpoint = startEndpoint(endpointArgs);
  await new Promise((done) => setTimeout(done, 800));

  const server = await preview({
    root: webRoot,
    preview: {
      port: pagePort,
      strictPort: true,
      ...(plan.pageHttps ? { https: selfSigned() } : {}),
    },
    logLevel: "warn",
  });

  const browser = await chromium.launch({ executablePath, headless: true });
  const errors = [];
  const failures = [];
  const responses = [];

  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await context.newPage();
    page.on("console", (message) => {
      if (message.type() === "error") errors.push(message.text());
    });
    page.on("pageerror", (error) => errors.push(`[pageerror] ${error.message}`));
    page.on("requestfailed", (request) => {
      failures.push(`${request.method()} ${request.url()} → ${request.failure()?.errorText}`);
    });
    page.on("response", (response) => {
      if (response.url().includes(`:${endpointPort}`)) {
        responses.push(`${response.request().method()} ${response.url()} → ${response.status()}`);
      }
    });

    // 配置只从 localStorage 来（`Store.readSeatConfig`）：**没有 key**，本地端点不校验它。
    await page.addInitScript(
      ([seat, baseUrl]) => {
        localStorage.setItem("janpo.llm.seat", String(seat));
        localStorage.setItem("janpo.llm.provider", "custom-openai");
        localStorage.setItem("janpo.llm.model", "fake-model");
        localStorage.setItem("janpo.llm.base_url", baseUrl);
        localStorage.setItem("janpo.llm.api_key", "");
        localStorage.setItem("janpo.llm.timeout_ms", "10000");
        localStorage.setItem("janpo.llm.thinking", "off");
        localStorage.setItem("janpo.llm.tier", "bare");
      },
      [seat, baseUrl],
    );

    console.log(
      `模式 ${mode}：页面 ${pageOrigin}　端点 ${baseUrl}　CORS ${plan.cors ? `放行 ${pageOrigin}` : "不放行"}`,
    );
    await page.goto(`${pageOrigin}/`, { waitUntil: "load" });

    const readText = async (testId) => (await page.getByTestId(testId).textContent()).trim();
    const readAttr = async (testId, name) => await page.getByTestId(testId).getAttribute(name);

    // 配置面板：选了自定义端点，baseUrl 那一格才在（官方八家看不到它）。
    const provider = await page.getByTestId("table-llm-provider").inputValue();
    const shown = await page.getByTestId("table-llm-base-url").count();
    console.log(`配置面板：provider=${provider}　baseUrl 输入框 ${shown === 1 ? "在" : "不在"}`);

    await page.getByTestId("table-speed-8×").click();
    await page.getByTestId("table-play").click();

    // 等 Agent 层出结果：答上话（spoke）或兜底（troubled）。
    await page.waitForFunction(
      () => {
        const state = document.querySelector('[data-testid="table-agent"]')?.dataset.agent;
        return state === "spoke" || state === "troubled";
      },
      null,
      { timeout: budgetMs },
    );
    await page.getByTestId("table-play").click(); // 停下来，别把整局跑完

    const state = await readAttr("table-agent", "data-agent");
    console.log("");
    console.log(
      `Agent 状态（data-agent=${state}，已兜底 ${await readAttr("table-agent", "data-fallbacks")} 手）：`,
    );
    console.log(`  ${await readText("table-agent")}`);
    console.log(`${await readText("table-latest")}`);
    console.log("");
    console.log(`端点收到的请求：${endpoint.log.slice(1).join(" | ") || "（一条都没有）"}`);
    console.log(`浏览器看到的响应：${responses.join(" | ") || "（一条都没有）"}`);
    console.log(`请求失败（requestfailed）：${failures.join("\n  ") || "无"}`);
    console.log(`console.error：${errors.length > 0 ? `\n  ${errors.join("\n  ")}` : "无"}`);
    console.log("");
    console.log(
      `实测过的是 ${plan.expect}，这一跑是 ${state} —— ${state === plan.expect ? "一致" : "**变了，看上面**"}`,
    );
    if (state === "troubled") {
      console.log("（这一条正是要写进文档的：请求被拦时主持人在页面上看到的是哪句话）");
    }
  } finally {
    await browser.close();
    await server.close();
    endpoint.child.kill();
  }
}

/**
 * **公网 https 页面 → 本地端点**：这一条与我们的页面无关，验的是浏览器自己的规矩
 * （Chrome 的「本地网络访问」权限）。因此不加载牌桌，只在一个第三方 https 页面里裸 fetch 一次，
 * 一次不授权、一次授权，把两次的结果都打出来。**要外网**。
 */
async function runLocalNetworkAccess() {
  const endpoint = startEndpoint(["--port", String(endpointPort), "--cors", "*", "--quiet"]);
  await new Promise((done) => setTimeout(done, 800));

  const browser = await chromium.launch({ executablePath, headless: true });
  const target = `http://127.0.0.1:${endpointPort}/v1/chat/completions`;

  try {
    for (const granted of [false, true]) {
      const context = await browser.newContext();
      if (granted) {
        await context.grantPermissions(["local-network-access"], { origin: publicPage });
      }
      const page = await context.newPage();
      const errors = [];
      page.on("console", (message) => {
        if (message.type() === "error" || message.type() === "warning") {
          errors.push(`[${message.type()}] ${message.text()}`);
        }
      });
      await page.goto(publicPage, { waitUntil: "domcontentloaded" });
      const result = await page.evaluate(async (url) => {
        try {
          const response = await fetch(url, {
            method: "POST",
            headers: { "content-type": "application/json" },
            body: JSON.stringify({ model: "fake-model", messages: [], stream: true }),
          });
          return `通了（HTTP ${response.status}）`;
        } catch (error) {
          return `被拦：${String(error)}`;
        }
      }, target);

      console.log(`${publicPage} → ${target}　本地网络访问权限：${granted ? "已授权" : "未授权"}`);
      console.log(`  结果：${result}`);
      for (const line of errors) console.log(`  ${line}`);
      await context.close();
    }
  } finally {
    await browser.close();
    endpoint.child.kill();
  }
}

if (mode === "lna") {
  await runLocalNetworkAccess();
} else if (MODES[mode] === undefined) {
  console.error(`没有这个模式：${mode}（有 ${Object.keys(MODES).join(" / ")} / lna）`);
  process.exit(1);
} else if (mode === "lan" && endpointHost === "127.0.0.1") {
  console.error("--mode lan 要一个非回环地址：--endpoint-host 192.168.x.x");
  process.exit(1);
} else {
  await runTable(MODES[mode]);
}
