// 22 票的自查（第二部分）：杠的形态与立直棒/供托要在真页面上看得见。
// 种子由 dotnet fsi 探针挑出来（scripts/fsi 的用法）：106 有暗杠 + 加杠，343 的第 2 局有人立直。

import { existsSync, writeFileSync } from "node:fs";
import playwright from "/home/xerxes2/janpo-ws-b/web/node_modules/playwright-core/index.js";
import { preview } from "/home/xerxes2/janpo-ws-b/web/node_modules/vite/dist/node/index.js";

const { chromium } = playwright;
const webRoot = "/home/xerxes2/janpo-ws-b/web";
const outDir = "/home/xerxes2/janpo-ws-b/.scratch/llm-riichi-arena/run/reports";
const PORT = 4184;

const chrome = ["/usr/bin/google-chrome-stable", "/usr/bin/chromium"].find((p) => existsSync(p));
const server = await preview({
  root: webRoot,
  preview: { port: PORT, strictPort: true },
  logLevel: "warn",
});
const browser = await chromium.launch({ executablePath: chrome, headless: true });
const problems = [];
const report = {};

try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 1200 } });
  page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
  });
  await page.goto(`http://localhost:${PORT}/`, { waitUntil: "load" });

  const restart = async (seed) => {
    await page.getByTestId("table-seed").fill(String(seed));
    await page.getByTestId("table-restart").click();
  };
  const nakiKinds = async () => {
    const kinds = [];
    for (let seat = 0; seat < 4; seat += 1) {
      kinds.push(
        ...(await page
          .locator(`[data-testid="seat-${seat}-naki"] .naki`)
          .evaluateAll((nodes) => nodes.map((node) => node.dataset.naki))),
      );
    }
    return kinds;
  };

  // ---- 杠：种子 106 有暗杠与加杠 ----
  await restart(106);
  await page.getByTestId("table-speed-8×").click();
  await page.getByTestId("table-play").click();
  await page.getByTestId("table-settlement").waitFor({ timeout: 120000 });
  await page.getByTestId("table-view-god").click();
  report.kan = {
    naki: await nakiKinds(),
    // 暗杠的两端是扣着的：那两张没有 data-pai，只有 .back。
    ankanBacks: await page.locator('[data-naki="暗杠"] .back').count(),
    ankanFaces: await page.locator('[data-naki="暗杠"] [data-pai]').count(),
    kanTiles: await page.locator('[data-naki="加杠"] .tile').count(),
  };
  await page.screenshot({ path: `${outDir}/22-table-kan.png`, fullPage: false });

  // ---- 立直棒与供托：种子 343 的第 2 局有人立直 ----
  await restart(343);
  await page.getByTestId("table-play").click();
  await page.getByTestId("table-settlement").waitFor({ timeout: 120000 });
  await page.getByTestId("table-next").click();

  let found = false;
  for (let step = 0; step < 300 && !found; step += 1) {
    await page.getByTestId("table-step").click();
    found = (await page.locator('[data-testid="table-bou"] .bou').count()) > 0;
  }

  report.riichi = {
    found,
    kyoku: (await page.getByTestId("table-kyoku").textContent()).trim(),
    kyotaku: (await page.getByTestId("table-kyotaku").textContent()).trim(),
    bou: await page.locator('[data-testid="table-bou"] .bou').count(),
    marks: await page.locator(".seat .mark").evaluateAll((nodes) => nodes.map((n) => n.textContent)),
  };
  await page.screenshot({ path: `${outDir}/22-table-riichi.png`, fullPage: false });

  report.problems = problems;
  writeFileSync(`${outDir}/22-table-features.json`, `${JSON.stringify(report, null, 2)}\n`);
  console.log(JSON.stringify(report, null, 2));
} finally {
  await browser.close();
  await server.close();
}

if (problems.length > 0) {
  console.error(problems.join("\n"));
  process.exit(1);
}
