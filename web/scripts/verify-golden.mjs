// 双目标黄金用例的 **JS 侧那一跑**（票 21）。
//
// 同一份 `tests/fixtures/golden/dual-target.json`：dotnet 侧由 `GoldenSuiteTests` 与
// `janpo golden check` 跑，这里把它原文喂进**浏览器里的引擎**再跑一遍。
// 跑与对照的逻辑是同一段 F#（`Janpo.Golden`），两侧不各写一份——
// 因此「这边红那边绿」只可能是 Fable 与 dotnet 编出来的语义不一样。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:golden [-- <用例文件>]`
// 浏览器：优先 $JANPO_CHROME，其次 playwright 自带的 chromium，最后系统里的 chrome/chromium。

import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { chromeExecutable, missingChrome } from "./chrome.mjs";
import { pageUrl, retryOnReload, startDevServer } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(webRoot, "..");

const DEFAULT_CASES = resolve(repoRoot, "tests/fixtures/golden/dual-target.json");

/**
 * 用 vite 的 dev server 托管**源码形态**的 Fable 输出，再在页面里 `import` 那个模块。
 *
 * 用 dev server 而不是 preview：`dist/` 里的文件名带哈希、模块被打成一坨，
 * 点名 import 不到 `Golden.js`。曳光弹那道闸门（verify-tracer）跑的是打包后的产物，
 * 两道合起来「Fable 的输出」与「Vite 的产物」都被跑过。
 */
async function runInBrowser(casesText, executablePath) {
  const server = await startDevServer(webRoot);
  const browser = await chromium.launch({ executablePath, headless: true });
  const problems = [];

  try {
    const page = await browser.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
    });

    await page.goto(`${pageUrl(server)}/`, { waitUntil: "load" });

    const payload = await retryOnReload(() =>
      page.evaluate(async (text) => {
        const golden = await import("/src/generated/Golden.js");
        return golden.check(text);
      }, casesText),
    );

    return { report: JSON.parse(payload), problems };
  } finally {
    await browser.close();
    await server.close();
  }
}

const casesPath = process.argv[2] ? resolve(process.argv[2]) : DEFAULT_CASES;
const casesText = readFileSync(casesPath, "utf8");
const executablePath = chromeExecutable();

if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

console.log(`用例 ${casesPath}，浏览器 ${executablePath}`);

const { report, problems } = await runInBrowser(casesText, executablePath);

if (problems.length > 0) {
  console.error("页面报了错：");
  console.error(problems.join("\n"));
  process.exit(1);
}

if (report.error) {
  console.error(`浏览器读不动用例文件：${report.error}`);
  process.exit(1);
}

console.log(`${report.cases} 条用例、${report.fields} 个字段、${report.lines} 行`);

if (report.mismatches.length > 0) {
  console.error("双目标语义漂了（期望取自用例文件，实际是浏览器里的引擎算出来的）：");
  // 事件流漂一行就是几百条，印前 20 条够定位了，完整清单跑 `janpo golden check` 看。
  console.error(report.mismatches.slice(0, 20).join("\n"));
  if (report.mismatches.length > 20) {
    console.error(`…… 还有 ${report.mismatches.length - 20} 处`);
  }
  process.exit(1);
}

console.log("浏览器里的引擎与黄金用例逐字段逐行相同 ✓");
