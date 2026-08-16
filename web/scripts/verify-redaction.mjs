// **provider 报错原文会不会把 key 带进牌谱**的闸门（票 36）。
//
// 票 34 建完「牌谱里不含 key」那道闸门后核出一条真通道：provider 的报错原文原样流进
// `fallback`、`output.error_message` 与**下一次重试的 prompt**（牌谱存最后一次）。
// 官方八家实测都打码，**但自定义端点是用户自建的网关，回显什么由它自己决定**。
//
// 因此这一道用一个**会原样回显 key 的假端点**（`fake-endpoint.mjs --echo-key`：401，
// 报错正文里抄着收到的 `Authorization` 与被请求的地址）真开一桌、真发请求、真导出牌谱，
// 然后同时查两件事：
//
//   1. 导出物（文件名 + 字节）里**没有**那把 key，也没有那个 baseUrl；
//   2. 导出物里**有**打码留下的记号 —— 这一条是**阳性对照**：端点真回显了、
//      回显真进了牌谱、只是在进去之前被打了码。没有它，端点哪天不回显了这道闸门也照样绿，
//      那就又成了一道从不失败的闸门（票 34 的教训）。
//
// 全程本机：页面是本地 dev server，端点是本地假端点，**一个字节都不出网**，因此它进 CI。
//
//   cd web && pnpm run fable && node scripts/verify-redaction.mjs
//
// 选项：--seat N（默认 0，庄家，第一手就轮到它）、--budget ms、--keep <路径>。

import { spawn } from "node:child_process";
import { copyFileSync, readFileSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { chromeExecutable, missingChrome } from "./chrome.mjs";
import { pageUrl, startDevServer } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const index = argv.indexOf(name);
  return index < 0 ? fallback : argv[index + 1];
};

const seat = Number.parseInt(flag("--seat", "0"), 10);
const budgetMs = Number.parseInt(flag("--budget", "120000"), 10);
const keep = flag("--keep", null);

/**
 * 灌进 localStorage、真交给端点、又被端点原样抄回来的那把 key。**写死的字面量**，
 * 与票 34 那把同一个理由：全 ASCII（断言按字节找，而 JSON 编码器可以把非 ASCII 写成 `\uXXXX`），
 * 且看一眼就知道是假的（**绝不从 /tmp/deepseek_key 之类的地方读**）。
 */
const FAKE_KEY = "sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8";

/**
 * 打码留下的记号（`src/agent/redact.ts` 的那两个常量）。**这里写死一份是故意的**：
 * 闸门与被验的代码各写各的，改了实现这边就得跟着改一次 —— 那正是要人看一眼的时刻。
 */
const KEY_MASK = "[API key 已打码]";
const URL_MASK = "[端点地址已打码]";

/** 借内核要一个空闲端口：跑批是并行的，写死端口迟早撞上另一个工作区（见 `serve.mjs` 那段）。 */
function freePort() {
  return new Promise((done, fail) => {
    const probe = createServer();
    probe.on("error", fail);
    probe.listen(0, "127.0.0.1", () => {
      const { port } = probe.address();
      probe.close(() => done(port));
    });
  });
}

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

const endpointPort = await freePort();
const baseUrl = `http://127.0.0.1:${endpointPort}/v1`;

// dev server 而不是 preview：与 verify-export 同一个理由（页面里要按源码路径取模块），
// 顺带省掉一次 `vite build`。
const server = await startDevServer(webRoot);
const pageOrigin = pageUrl(server);

const endpoint = spawn(
  "node",
  ["scripts/fake-endpoint.mjs", "--port", String(endpointPort), "--cors", pageOrigin, "--echo-key"],
  { cwd: webRoot, stdio: ["ignore", "pipe", "inherit"] },
);
const endpointLog = [];
endpoint.stdout.on("data", (chunk) => endpointLog.push(String(chunk).trimEnd()));
await new Promise((done) => setTimeout(done, 800));

const browser = await chromium.launch({ executablePath, headless: true });
const problems = [];

try {
  const context = await browser.newContext({ acceptDownloads: true });
  const page = await context.newPage();
  page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

  // 座位配置只从 localStorage 来（`Store.readSeatConfig`）：自定义端点 + 一把真交出去的 key。
  await page.addInitScript(
    ([seat, baseUrl, apiKey]) => {
      localStorage.setItem("janpo.llm.seat", String(seat));
      localStorage.setItem("janpo.llm.provider", "custom");
      localStorage.setItem("janpo.llm.model", "fake-model");
      localStorage.setItem("janpo.llm.base_url", baseUrl);
      localStorage.setItem("janpo.llm.api_key", apiKey);
      localStorage.setItem("janpo.llm.timeout_ms", "10000");
      localStorage.setItem("janpo.llm.thinking", "off");
      localStorage.setItem("janpo.llm.tier", "bare");
    },
    [seat, baseUrl, FAKE_KEY],
  );

  console.log(`页面 ${pageOrigin}　端点 ${baseUrl}（会原样回显 key 的 401）　模型坐席 ${seat}`);
  console.log(`交给端点的那把假 key：${FAKE_KEY}`);
  await page.goto(`${pageOrigin}/`, { waitUntil: "load" });

  const readText = async (testId) => (await page.getByTestId(testId).textContent()).trim();

  await page.getByTestId("table-speed-8×").click();
  await page.getByTestId("table-play").click();
  await page.waitForFunction(
    () => document.querySelector('[data-testid="table-agent"]')?.dataset.agent === "troubled",
    null,
    { timeout: budgetMs },
  );
  await page.getByTestId("table-play").click(); // 停下来，一手够了

  console.log("");
  console.log("端点收到的：");
  for (const line of endpointLog) console.log(`  ${line}`);
  console.log("");
  console.log(`页面上那句话：${await readText("table-agent")}`);

  const [download] = await Promise.all([
    page.waitForEvent("download", { timeout: 30000 }),
    page.getByTestId("table-export").click(),
  ]);
  const file = await download.path();
  const text = readFileSync(file, "utf8");
  if (keep) copyFileSync(file, resolve(keep));

  console.log("");
  console.log(`下载文件名：${download.suggestedFilename()}　${text.length} 字节`);

  const paifu = JSON.parse(text);
  const records = paifu.decisions ?? [];
  console.log(`牌谱：事件 ${paifu.events.length} 条　决策记录 ${records.length} 条`);
  for (const record of records.slice(0, 1)) {
    console.log(`  fallback：${record.fallback}`);
    console.log(`  output：${record.output}`);
    console.log(`  prompt_tail 的末一段：…${record.prompt_tail.slice(-160)}`);
  }

  // 端点那侧真的拿到了这把 key 吗（这是「通道确实通着」的第一环）。
  if (!endpointLog.some((line) => line.includes(FAKE_KEY))) {
    problems.push("假端点一次都没收到那把 key：这一趟根本没走到夹带通道上，闸门是空的");
  }
  if (records.length === 0) {
    problems.push("牌谱里一条决策记录都没有：provider 的报错没进牌谱，这一趟什么都没验到");
  }

  // 可分享物里永远不能出现 API key（ADR-0003 / ADR-0002）。导出这条路给出去的是两样东西：
  // 那份字节，与它的文件名。**按字节查**，与票 34 同一条规矩。
  const shareable = `${download.suggestedFilename()}\n${text}`;
  if (shareable.includes(FAKE_KEY)) {
    problems.push("导出物（文件名 + 字节）里出现了 API key：端点回显的那把，原样进了牌谱");
  }
  if (shareable.includes(baseUrl)) {
    problems.push(`导出物里出现了自定义端点的 baseUrl：${baseUrl}（那是主持人的内网地址）`);
  }

  // **阳性对照**：记号必须在。查的是解码之后的那份 —— 记号里有汉字，而
  // 「牌谱字节里的汉字有没有被写成 \uXXXX」是编码器的自由，闸门不该押在它身上。
  const decoded = JSON.stringify(paifu);
  if (!decoded.includes(KEY_MASK)) {
    problems.push(
      `牌谱里找不到打码记号 ${KEY_MASK}：要么端点没回显 key，要么回显没进牌谱 —— ` +
        "这道闸门于是什么都没证明（它绿得没有意义）",
    );
  }
  if (!decoded.includes(URL_MASK)) {
    problems.push(`牌谱里找不到打码记号 ${URL_MASK}：端点回显的那个地址没进牌谱`);
  }
} finally {
  await browser.close();
  await server.close();
  endpoint.kill();
}

if (problems.length > 0) {
  console.error("");
  console.error("打码闸门没过：");
  console.error(problems.join("\n"));
  process.exit(1);
}

console.log("");
console.log("回显 key 的端点跑了一手：牌谱里没有 key、没有 baseUrl，打码记号都在 ✓");
