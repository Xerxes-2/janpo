// LLM 座位闭环的**人工验收**（票 23）。**手动跑，不进 CI**：它调真实 provider。
//
// 两个模式，对应票里的两条验收：
//   1. 真跑一局：LLM 坐一席（票 73 之后可以坐**好几席**）+ 其余随机 Player，打完一个 Kyoku。
//        JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-llm-seat.mjs
//        JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-llm-seat.mjs --seats 0,1
//   2. 断电演习：故意配一把坏 key，整局照样打完（全程兜底），页面上有明确的错误状态。
//        node scripts/verify-llm-seat.mjs --bad-key
//
// 选项：--seats 0,1（交给模型的那几席，默认 1）、--seed N（默认页面自带的 2088）、--model X、
//       --speed 1|2|4|8、--tier bare|assisted（默认 bare；票 24 的脚手架档位）。
// **一份档案坐几席**（票 73）：key 只填一次，而那几席各带各的人格——闸门在下面核
// 「它们的 preamble 真的不同」。
// key 只从文件读、只经 addInitScript 注入 localStorage，**不进代码、不进产物、不进提交**。

import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { chromeExecutable, missingChrome } from "./chrome.mjs";
import { PROFILE_NAME, personaFor, plantSeating, profileChoice } from "./seating.mjs";
import { hostPage, pageUrl, startPreview } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const index = argv.indexOf(name);
  return index < 0 ? fallback : argv[index + 1];
};

const badKey = argv.includes("--bad-key");
const seats = flag("--seats", "1")
  .split(",")
  .map((each) => Number.parseInt(each, 10));
/** 报告与 DOM 取值都盯着头一席（多席时其余几席另外印）。 */
const seat = seats[0];
const seed = flag("--seed", null);
const model = flag("--model", "deepseek-v4-flash");
const speed = flag("--speed", "8");
const tier = flag("--tier", "bare");
const budgetMs = Number.parseInt(flag("--budget", "600000"), 10);

const apiKey = badKey
  ? "sk-deliberately-broken-key"
  : readFileSync(process.env.JANPO_KEY_FILE ?? "/tmp/deepseek_key", "utf8").trim();

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

const server = await startPreview(webRoot);
const browser = await chromium.launch({ executablePath, headless: true });
const problems = [];
const resourceErrors = [];

try {
  const page = await browser.newPage();
  page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
  page.on("console", (message) => {
    // 请求失败浏览器自己会往 console 写一条（“Failed to load resource”）。
    // **断电演习里它本就该出现**，真跑里它也不是页面的错——另行计数，
    // 真正算错的是 `pageerror`（JS 抛了）与其余 console.error。
    if (message.type() !== "error") return;
    if (message.text().includes("Failed to load resource")) {
      resourceErrors.push(message.text());
      return;
    }
    problems.push(`[console.error] ${message.text()}`);
  });

  // provider 请求逐条记下来（延迟与状态码是报告要的证据）。
  const calls = [];
  page.on("response", async (response) => {
    const url = response.url();
    // 页面自己的请求不算：它托管在内核挑的那个端口上（`serve.mjs`）。
    if (url.startsWith(pageUrl(server))) return;
    calls.push({ url, status: response.status(), at: Date.now() });
  });

  // 坐法只从 localStorage 来 —— 页面 `init` 读的就是它（`Store.readSeating`，票 73）。
  // **一份档案**（key 只填一次），点名的那几席各引用它、各带各的人格。
  await plantSeating(page, {
    profiles: [
      {
        name: PROFILE_NAME,
        provider: "deepseek",
        model,
        api_key: apiKey,
        timeout_ms: "30000",
      },
    ],
    seats: [0, 1, 2, 3].map((index) =>
      seats.includes(index)
        ? { choice: profileChoice(PROFILE_NAME), tier, persona: personaFor(index) }
        : {},
    ),
  });

  await page.goto(hostPage(pageUrl(server)), { waitUntil: "load" });

  if (seed !== null) {
    await page.getByTestId("table-seed").fill(String(seed));
    await page.getByTestId("table-restart").click();
  }

  const readText = async (testId) => (await page.getByTestId(testId).textContent()).trim();
  const readAttr = async (testId, name) => await page.getByTestId(testId).getAttribute(name);

  console.log(
    `模式：${badKey ? "断电演习（坏 key）" : "真跑一局"}　座位 ${seats.join("、")}　模型 ${model}　` +
      `脚手架 ${await page.getByTestId(`table-seat-${seat}-tier`).inputValue()}`,
  );
  console.log(`四席坐着谁：${await readAttr("table-agent", "data-seats")}`);
  console.log(`模型坐席状态：${await readText("table-agent")}`);
  for (const index of seats) {
    console.log(
      `  座位 ${index}：${await readAttr(`table-seat-${index}`, "data-seat-choice")}　` +
        `人格「${await page.getByTestId(`table-seat-${index}-persona`).inputValue()}」`,
    );
  }

  await page.getByTestId(`table-speed-${speed}×`).click();
  const started = Date.now();
  await page.getByTestId("table-play").click();

  // 打完一个 Kyoku：结算面板出现即止。
  await page.waitForSelector('[data-testid="table-settlement"]', { timeout: budgetMs });
  const elapsed = Date.now() - started;

  const fallbacks = Number.parseInt(await readAttr("table-agent", "data-fallbacks"), 10);
  const provider = calls.filter((call) => call.url.includes("api.deepseek.com"));
  const failed = provider.filter((call) => call.status >= 400);

  console.log("");
  console.log(`一局打完，用时 ${(elapsed / 1000).toFixed(1)} s`);
  console.log(await readText("table-latest"));
  console.log(`Agent 状态（data-agent=${await readAttr("table-agent", "data-agent")}）：`);
  console.log(`  ${await readText("table-agent")}`);
  console.log(`兜底代打：${fallbacks} 手`);

  // token 账单（票 29b）：前缀可缓存的 prompt 值不值，只有这几个数说了算。
  if (await page.getByTestId("table-usage").count()) {
    console.log(await readText("table-usage"));
    console.log(
      `  输入 ${await readAttr("table-usage", "data-prompt-tokens")} tok、` +
        `命中 ${await readAttr("table-usage", "data-cache-read")} tok、` +
        `写缓存 ${await readAttr("table-usage", "data-cache-write")} tok、` +
        `命中率 ${await readAttr("table-usage", "data-cache-percent")}%`,
    );
  }
  console.log(`provider 请求：${provider.length} 次，其中 4xx/5xx ${failed.length} 次`);

  if (provider.length > 0) {
    const spans = provider.slice(1).map((call, index) => call.at - provider[index].at);
    const total = spans.reduce((sum, span) => sum + span, 0);
    if (spans.length > 0) {
      console.log(
        `请求间隔：中位数 ${[...spans].sort((a, b) => a - b)[spans.length >> 1]} ms，均值 ${Math.round(total / spans.length)} ms`,
      );
    }
  }

  console.log("");
  console.log(`模型座位的牌河：${await readText(`seat-${seat}-kawa`)}`);
  console.log(`模型座位的副露：${await readText(`seat-${seat}-naki`)}`);
  console.log("");
  console.log(await readText("table-settlement"));
  const scores = await Promise.all([0, 1, 2, 3].map((s) => readText(`seat-${s}-score`)));
  console.log(`各家点数：${scores.join(" / ")}`);
  console.log(`浏览器资源错误（请求失败）：${resourceErrors.length} 条`);

  // 断电演习的验收：整局照样打完，且页面上红着说模型出了什么事。
  if (badKey) {
    const state = await readAttr("table-agent", "data-agent");
    if (state !== "troubled") problems.push(`坏 key 之下 data-agent 该是 troubled，实为 ${state}`);
    if (fallbacks === 0) problems.push("坏 key 之下该有兜底代打的手，实为 0");
  } else if (fallbacks > 0) {
    console.log(`（注意：真跑里出现了 ${fallbacks} 手兜底，看上面的原因）`);
  }
} finally {
  await browser.close();
  await server.close();
}

if (problems.length > 0) {
  console.error("页面报了错：");
  console.error(problems.join("\n"));
  process.exit(1);
}

console.log("人工验收通过 ✓");
