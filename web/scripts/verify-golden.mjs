// 双目标黄金用例的 **JS 侧那一跑**（票 21）。
//
// 同一份 `tests/fixtures/golden/dual-target.json`：dotnet 侧由 `GoldenSuiteTests` 与
// `janpo golden check` 跑，这里把它原文喂进**浏览器里的引擎**再跑一遍。
// 跑与对照的逻辑是同一段 F#（`Janpo.Golden`），两侧不各写一份——
// 因此「这边红那边绿」只可能是 Fable 与 dotnet 编出来的语义不一样。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:golden [-- <用例文件>]`
// 它也是 `verify-browser.mjs` 里的一道（跑道上那几趟共用一个浏览器与一台服务器）。
// 浏览器：优先 $JANPO_CHROME，其次 playwright 自带的 chromium，最后系统里的 chrome/chromium。

import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { hostPage, retryOnReload } from "./serve.mjs";
import { openSetup } from "./table-drive.mjs";

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
async function runInBrowser(lane, casesText) {
  const url = await lane.devUrl();
  const page = await lane.newPage();
  const problems = [];

  try {
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
    });

    await page.goto(hostPage(url), { waitUntil: "load" });
    await openSetup(page);

    const payload = await retryOnReload(() =>
      page.evaluate(async (text) => {
        // 相对页面地址 import：vite 的 base 可配（JANPO_BASE），写死 "/src/…" 一改 base 就 404。
        const golden = await import("./src/generated/Golden.js");
        return golden.check(text);
      }, casesText),
    );

    return { report: JSON.parse(payload), problems };
  } finally {
    await page.close();
  }
}

/** 黄金用例那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyGolden(lane, { casesPath = DEFAULT_CASES } = {}) {
  const casesText = readFileSync(casesPath, "utf8");
  console.log(`用例 ${casesPath}，浏览器 ${lane.executablePath}`);

  const { report, problems } = await runInBrowser(lane, casesText);

  if (problems.length > 0) return failure("页面报了错：", problems);
  if (report.error) return failure(`浏览器读不动用例文件：${report.error}`, []);

  console.log(`${report.cases} 条用例、${report.fields} 个字段、${report.lines} 行`);

  if (report.mismatches.length > 0) {
    // 事件流漂一行就是几百条，印前 20 条够定位了，完整清单跑 `janpo golden check` 看。
    const shown = report.mismatches.slice(0, 20);
    if (report.mismatches.length > 20) shown.push(`…… 还有 ${report.mismatches.length - 20} 处`);
    return failure("双目标语义漂了（期望取自用例文件，实际是浏览器里的引擎算出来的）：", shown);
  }

  console.log("浏览器里的引擎与黄金用例逐字段逐行相同 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  const casesPath = process.argv[2] ? resolve(process.argv[2]) : DEFAULT_CASES;
  await runStandalone((lane) => verifyGolden(lane, { casesPath }));
}
