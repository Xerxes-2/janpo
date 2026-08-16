// 27 号票（M1 验收）的跑批脚本：**LLM 坐一席 + 三家随机，打完一整场东风战**。
//
// 与仓库里现成的两个脚本的关系：
//   - `web/scripts/verify-llm-seat.mjs` 只打**一个 Kyoku**（等 `table-settlement` 出现就停）；
//   - `web/scripts/verify-export.mjs --llm` 只走 `--turns N` 手，不管局间推进。
// 这一票要的是「局间推进、连庄、本场、供托结转都在 UI 层跑一遍」，所以要在每局结算后
// 点「下一局」接着打，直到 `table-result`（终局精算）出现。
//
// 放在 /tmp 而不是 web/scripts/：27 是**验收票**，不该往产品树里加文件（同 22 票的做法）。
// 一份原样的拷贝在 `.scratch/llm-riichi-arena/run/reports/27-run-game.mjs`。
//
// 跑法：
//   JANPO_KEY_FILE=/tmp/deepseek_key node /tmp/janpo-27-game.mjs \
//     --tier bare --seed 2088 --seat 1 --keep /tmp/janpo-27-bare.json --log /tmp/janpo-27-bare.log.json
//
// key 只从文件读、只经 addInitScript 注入 localStorage，**不进代码、不进产物、不进提交**。

import { copyFileSync, readFileSync, writeFileSync } from "node:fs";
import playwright from "/home/xerxes2/janpo-ws-a/web/node_modules/playwright-core/index.js";

const { chromium } = playwright;
import { chromeExecutable, missingChrome } from "/home/xerxes2/janpo-ws-a/web/scripts/chrome.mjs";
import { pageUrl, retryOnReload, startDevServer } from "/home/xerxes2/janpo-ws-a/web/scripts/serve.mjs";

const webRoot = "/home/xerxes2/janpo-ws-a/web";

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const index = argv.indexOf(name);
  return index < 0 ? fallback : argv[index + 1];
};

const tier = flag("--tier", "bare");
const seed = flag("--seed", "2088");
const seat = Number.parseInt(flag("--seat", "1"), 10);
const model = flag("--model", "deepseek-v4-flash");
const thinking = flag("--thinking", "off");
const speed = flag("--speed", "8");
const keep = flag("--keep", null);
const logPath = flag("--log", null);
// 一个 Kyoku 的预算：LLM 一手 2–3 秒，一局二三十手，10 分钟绰绰有余。
const kyokuBudgetMs = Number.parseInt(flag("--budget", "900000"), 10);

const badKey = argv.includes("--bad-key");
const apiKey = badKey
  ? "sk-deliberately-broken-key"
  : readFileSync(process.env.JANPO_KEY_FILE ?? "/tmp/deepseek_key", "utf8").trim();

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

const server = await startDevServer(webRoot);
const browser = await chromium.launch({ executablePath, headless: true });
const problems = [];
const resourceErrors = [];
const calls = [];
const kyokus = [];

const started = Date.now();

try {
  const context = await browser.newContext({ acceptDownloads: true });
  const page = await context.newPage();
  page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
  page.on("console", (message) => {
    if (message.type() !== "error") return;
    if (message.text().includes("Failed to load resource")) {
      resourceErrors.push(message.text());
      return;
    }
    problems.push(`[console.error] ${message.text()}`);
  });
  page.on("response", (response) => {
    const url = response.url();
    if (url.startsWith(pageUrl(server))) return;
    calls.push({ status: response.status(), at: Date.now(), origin: new URL(url).origin });
  });

  await page.addInitScript(
    ([seat, model, apiKey, tier, thinking]) => {
      localStorage.setItem("janpo.llm.seat", String(seat));
      localStorage.setItem("janpo.llm.provider", "deepseek");
      localStorage.setItem("janpo.llm.model", model);
      localStorage.setItem("janpo.llm.api_key", apiKey);
      localStorage.setItem("janpo.llm.timeout_ms", "60000");
      localStorage.setItem("janpo.llm.thinking", thinking);
      localStorage.setItem("janpo.llm.tier", tier);
    },
    [seat, model, apiKey, tier, thinking],
  );

  await page.goto(`${pageUrl(server)}/`, { waitUntil: "load" });

  const readText = async (testId) => (await page.getByTestId(testId).textContent()).trim();
  const readAttr = async (testId, name) => await page.getByTestId(testId).getAttribute(name);
  const scores = async () =>
    await Promise.all([0, 1, 2, 3].map((s) => readText(`seat-${s}-score`)));

  await page.getByTestId("table-seed").fill(String(seed));
  await page.getByTestId("table-restart").click();
  await page.getByTestId(`table-speed-${speed}×`).click();

  console.log(
    `一整场东风战　种子 ${seed}　模型坐席 ${seat}（${model}，思考预算 ${thinking}）　脚手架 ${await page.getByTestId("table-llm-tier").inputValue()}　倍速 ${speed}×`,
  );
  console.log(`开局状态：${await readText("table-agent")}`);
  console.log("");

  for (let index = 0; index < 40; index += 1) {
    const kyokuStarted = Date.now();
    const callsBefore = calls.length;
    // 开局那一刻的场况：局数 / 本场 / 供托（连庄与结转在这里看得出来）。
    const head = await readText("table-kyoku");
    const kyotaku = await readText("table-kyotaku");
    const scoresBefore = await scores();
    await page.getByTestId("table-play").click();
    await page.waitForSelector('[data-testid="table-settlement"]', { timeout: kyokuBudgetMs });

    const record = {
      index,
      head,
      kyotaku,
      scoresBefore,
      elapsedMs: Date.now() - kyokuStarted,
      settlement: await readText("table-settlement"),
      renchan: await readText("table-renchan"),
      scores: await scores(),
      fallbacks: Number.parseInt(await readAttr("table-agent", "data-fallbacks"), 10),
      calls: calls.length - callsBefore,
      usage: (await page.getByTestId("table-usage").count())
        ? {
            prompt: Number.parseInt(await readAttr("table-usage", "data-prompt-tokens"), 10),
            cacheRead: Number.parseInt(await readAttr("table-usage", "data-cache-read"), 10),
            cacheWrite: Number.parseInt(await readAttr("table-usage", "data-cache-write"), 10),
            percent: Number.parseInt(await readAttr("table-usage", "data-cache-percent"), 10),
          }
        : null,
    };
    kyokus.push(record);
    console.log(
      `第 ${index + 1} 局　${record.head}　供托 ${record.kyotaku}　用时 ${(record.elapsedMs / 1000).toFixed(1)} s　请求 ${record.calls} 次　累计兜底 ${record.fallbacks} 手`,
    );
    console.log(`  ${record.settlement.replace(/\n+/g, " ／ ")}`);
    console.log(`  ${record.renchan}　点数 ${record.scores.join(" / ")}`);

    if (await page.getByTestId("table-result").count()) {
      console.log("");
      console.log(await readText("table-result"));
      break;
    }
    await page.getByTestId("table-next").click();
    await page.waitForSelector('[data-testid="table-settlement"]', { state: "detached" });
  }

  const elapsed = Date.now() - started;
  console.log("");
  console.log(`整场用时 ${(elapsed / 1000).toFixed(1)} s，共 ${kyokus.length} 局`);
  console.log(`Agent 状态：${await readText("table-agent")}`);
  if (await page.getByTestId("table-usage").count()) console.log(await readText("table-usage"));
  const provider = calls.filter((call) => call.status > 0);
  console.log(
    `provider 请求：${provider.length} 次，其中 4xx/5xx ${provider.filter((c) => c.status >= 400).length} 次`,
  );
  // 「请求由你的浏览器直发 provider、不经任何第三方」这条声称的证据：
  // 整场里**除页面自身之外**的每一个 origin 都数出来。
  const origins = {};
  for (const call of calls) origins[call.origin] = (origins[call.origin] ?? 0) + 1;
  console.log(`页面之外的请求去了哪：${JSON.stringify(origins)}`);
  console.log(`浏览器资源错误（请求失败）：${resourceErrors.length} 条`);

  // 导出牌谱：真点那个按钮，接住 download 事件（与 verify-export.mjs 同一条路）。
  const [download] = await Promise.all([
    page.waitForEvent("download", { timeout: 30000 }),
    page.getByTestId("table-export").click(),
  ]);
  const file = await download.path();
  const text = readFileSync(file, "utf8");
  console.log("");
  console.log(`下载文件名：${download.suggestedFilename()}　${text.length} 字节`);
  if (keep) {
    copyFileSync(file, keep);
    console.log(`另存到：${keep}`);
  }

  // 下下来的那份字节交回浏览器里的引擎 fold 一遍。
  const report = JSON.parse(
    await retryOnReload(() =>
      page.evaluate(async (paifu) => {
        const check = await import("./src/generated/PaifuCheck.js");
        return check.check(paifu);
      }, text),
    ),
  );
  console.log(`fold 回来：${JSON.stringify(report)}`);
  if (report.error) problems.push(`回放不动：${report.error}`);
  if (report.same_events === false) problems.push(`事件流对不上：${report.mismatch}`);

  const onPage = await scores();
  if (onPage.join(",") !== (report.scores ?? []).join(",")) {
    problems.push(`牌桌上的点数 ${onPage.join("/")} 与回放算出的 ${(report.scores ?? []).join("/")} 不同`);
  }
  if (text.includes(apiKey)) problems.push("导出的牌谱里出现了 API key");

  if (logPath) {
    writeFileSync(
      logPath,
      JSON.stringify(
        { tier, seed, seat, model, thinking, elapsedMs: elapsed, kyokus, report, resourceErrors: resourceErrors.length, calls: provider.length },
        null,
        2,
      ),
    );
    console.log(`跑批日志：${logPath}`);
  }
} finally {
  await browser.close();
  await server.close();
}

if (problems.length > 0) {
  console.error("");
  console.error("这一趟有问题：");
  console.error(problems.join("\n"));
  process.exit(1);
}

console.log("一整场东风战跑完 ✓");
