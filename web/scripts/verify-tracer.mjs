// 无头验收（19 票）：同一种子，**浏览器里的引擎**与 **dotnet 侧的 CLI** 必须逐项相同。
//
// 这是「双目标语义没漂」的第一份证据。系统化的黄金用例是 21 票的事，这里只钉住一颗曳光弹：
// 一局（janpo kyoku）与一整场（janpo game）的终局点数与顺位。
//
// **地址带 `?dev=1`**（票 35）：曳光弹是开发向的自检页，默认访客看不到它，
// 只有这个开关能把它摆回牌桌下面（判据在 `src/Janpo.Web/Main.fs`）。
//
// 跑法：`cd web && pnpm run build && pnpm run verify [-- --seed 1177]`
// 浏览器：优先 $JANPO_CHROME，其次 playwright 自带的 chromium，最后系统里的 chrome/chromium。

import { execFileSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { chromeExecutable, missingChrome } from "./chrome.mjs";
import { pageUrl, startPreview } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(webRoot, "..");

// 默认种子与 F# 侧的 `Tracer.defaultSeed` 一致（挑它的理由见 src/Janpo.Web/Tracer.fs）。
const DEFAULT_SEED = 1177;

function parseSeed(argv) {
  const index = argv.indexOf("--seed");
  if (index < 0) return DEFAULT_SEED;
  const seed = Number.parseInt(argv[index + 1], 10);
  if (Number.isNaN(seed)) throw new Error(`--seed 要一个整数，得到「${argv[index + 1]}」`);
  return seed;
}

// ---- dotnet 侧 ----

/** 跑一条 CLI 子命令，返回它的 stdout 全文。 */
function runCli(args) {
  return execFileSync(
    "dotnet",
    ["run", "--project", "src/Janpo.Cli", "--configuration", "Release", "--", ...args],
    { cwd: repoRoot, encoding: "utf8", maxBuffer: 128 * 1024 * 1024 },
  );
}

/**
 * CLI 输出末尾那几行「<键>: <值>」。事件流每行是 JSON（以 `{` 开头），不会被误当成键值行。
 */
function summaryLines(text) {
  const summary = {};
  for (const line of text.split("\n")) {
    const matched = /^(scores|juni|kyokus):\s*(.*)$/.exec(line);
    if (matched) summary[matched[1]] = matched[2].trim();
  }
  return summary;
}

// ---- 浏览器侧 ----

/** 曳光弹那一块的开关（票 35）。页面侧认它的地方只有 `Main.devSurfaceRequested` 一处。 */
const DEV_QUERY = "?dev=1";

/** 曳光弹在页面上的钩子。带开关时它们都在，不带时一个都不该在。 */
const DEV_TEST_IDS = ["traces", "seed-input", "rerun"];

/** 只属于开发向叙述的词。默认视图的正文里出现任何一个都算漏。 */
const DEV_WORDS = ["曳光弹", "Fable", "dotnet"];

/**
 * 票 35 的反向自证：**不带开关时，默认视图里不得有开发向内容**。
 *
 * 下面那道对拍已经证明了「带上 `?dev=1` 曳光弹还在」（它读的就是曳光弹的 testId）；
 * 只有它的话，把开关废掉、访客又看到调试页也照样全绿。两道合起来才是一个开关。
 */
async function checkDefaultView(page, url) {
  const leaks = [];
  await page.goto(`${url}/`, { waitUntil: "load" });
  // 牌桌本人必须在：否则「什么都没渲染出来」也能让下面几条断言全部通过。
  await page.getByTestId("table-board").waitFor();

  for (const testId of DEV_TEST_IDS) {
    const count = await page.getByTestId(testId).count();
    if (count !== 0) leaks.push(`默认视图里还挂着 [data-testid="${testId}"]（${count} 个）`);
  }

  const text = await page.evaluate(() => document.body.innerText);
  for (const word of DEV_WORDS) {
    if (text.includes(word)) leaks.push(`默认视图的正文里出现了开发向的词「${word}」`);
  }

  return leaks;
}

/**
 * 起一个 vite preview 托管 dist/，用无头 Chrome 打开页面，把种子输进去点「重跑」，
 * 再把页面上的数读回来。
 *
 * **先用一个诱饵种子跑一遍**：否则当请求的种子恰好等于页面默认种子时，
 * 「输入框没生效」与「输入框生效了」两种情况读到的数一模一样，这条验收就名存实亡。
 */
async function readBrowser(seed, executablePath) {
  const server = await startPreview(webRoot);
  const browser = await chromium.launch({ executablePath, headless: true });
  const problems = [];

  try {
    const page = await browser.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
    });

    const leaks = await checkDefaultView(page, pageUrl(server));

    await page.goto(`${pageUrl(server)}/${DEV_QUERY}`, { waitUntil: "load" });

    const rerunWith = async (value) => {
      await page.getByTestId("seed-input").fill(String(value));
      await page.getByTestId("rerun").click();
      await page.waitForFunction(
        (expected) =>
          document.querySelector('[data-testid="kyoku-command"]')?.textContent === expected,
        `janpo kyoku ${value}`,
      );
    };

    await rerunWith(seed === DEFAULT_SEED ? seed + 1 : DEFAULT_SEED);
    await rerunWith(seed);

    const read = async (testId) => (await page.getByTestId(testId).textContent()).trim();
    const trace = async (prefix) => ({
      scores: await read(`${prefix}-scores`),
      juni: await read(`${prefix}-juni`),
      kyokus: await read(`${prefix}-kyokus`),
    });

    return { kyoku: await trace("kyoku"), game: await trace("game"), problems, leaks };
  } finally {
    await browser.close();
    await server.close();
  }
}

// ---- 对照 ----

function compare(label, keys, dotnetSide, browserSide, failures) {
  const rows = [];
  for (const key of keys) {
    const expected = dotnetSide[key];
    const actual = browserSide[key];
    const same = expected === actual;
    if (!same) failures.push(`${label}.${key}: dotnet「${expected}」≠ 浏览器「${actual}」`);
    rows.push(`  ${same ? "✓" : "✗"} ${label}.${key.padEnd(6)} ${expected}`);
  }
  return rows;
}

const seed = parseSeed(process.argv.slice(2));
const executablePath = chromeExecutable();

if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

console.log(`种子 ${seed}，浏览器 ${executablePath}`);

const dotnetSide = {
  kyoku: summaryLines(runCli(["kyoku", String(seed)])),
  game: summaryLines(runCli(["game", String(seed)])),
};
const browserSide = await readBrowser(seed, executablePath);

const failures = [];
const rows = [
  ...compare("kyoku", ["scores", "juni"], dotnetSide.kyoku, browserSide.kyoku, failures),
  ...compare("game", ["scores", "juni", "kyokus"], dotnetSide.game, browserSide.game, failures),
];

console.log("dotnet 侧 vs 浏览器侧：");
console.log(rows.join("\n"));

if (browserSide.problems.length > 0) {
  console.error("页面报了错：");
  console.error(browserSide.problems.join("\n"));
  process.exit(1);
}

if (browserSide.leaks.length > 0) {
  console.error("默认视图（不带 ?dev=1）里漏出了开发向内容：");
  console.error(browserSide.leaks.join("\n"));
  process.exit(1);
}

if (failures.length > 0) {
  console.error("双目标语义漂了：");
  console.error(failures.join("\n"));
  process.exit(1);
}

console.log("默认视图里没有曳光弹，带上 ?dev=1 它回来了 ✓");
console.log("浏览器内的引擎与 dotnet 侧逐项相同 ✓");
