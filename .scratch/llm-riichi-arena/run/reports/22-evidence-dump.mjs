// 22 票的自查：无头跑真页面，把一个 Kyoku 从头看到尾，再把 DOM 摘要与截图落地。
// 放在 /tmp 而不是 web/scripts/：那个目录 21 票正在改，这一票不碰。
// 跑法：cd /home/xerxes2/janpo-ws-b/web && node /tmp/janpo-table-dump.mjs

import { existsSync, writeFileSync } from "node:fs";
import playwright from "/home/xerxes2/janpo-ws-b/web/node_modules/playwright-core/index.js";
import { preview } from "/home/xerxes2/janpo-ws-b/web/node_modules/vite/dist/node/index.js";

const webRoot = "/home/xerxes2/janpo-ws-b/web";
const outDir = "/home/xerxes2/janpo-ws-b/.scratch/llm-riichi-arena/run/reports";
const PORT = 4183;

function chromeExecutable() {
  for (const path of [
    process.env.JANPO_CHROME,
    "/usr/bin/google-chrome-stable",
    "/usr/bin/chromium",
  ]) {
    if (path && existsSync(path)) return path;
  }
  throw new Error("找不到 Chrome");
}

const { chromium } = playwright;

const server = await preview({
  root: webRoot,
  preview: { port: PORT, strictPort: true },
  logLevel: "warn",
});
const browser = await chromium.launch({ executablePath: chromeExecutable(), headless: true });
const problems = [];

try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 1400 } });
  page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
  });

  await page.goto(`http://localhost:${PORT}/`, { waitUntil: "load" });

  const text = (id) => page.getByTestId(id).textContent();
  const countPai = (id) => page.locator(`[data-testid="${id}"] [data-pai]`).count();

  const snapshot = async (label) => {
    const seats = [];
    for (let seat = 0; seat < 4; seat += 1) {
      seats.push({
        seat,
        score: (await text(`seat-${seat}-score`)).trim(),
        handPai: await countPai(`seat-${seat}-hand`),
        handBacks: await page.locator(`[data-testid="seat-${seat}-hand"] .back`).count(),
        kawa: await countPai(`seat-${seat}-kawa`),
        tsumogiri: await page
          .locator(`[data-testid="seat-${seat}-kawa"] [data-tsumogiri="true"]`)
          .count(),
        naki: await page.locator(`[data-testid="seat-${seat}-naki"] .naki`).count(),
        nakiKinds: await page
          .locator(`[data-testid="seat-${seat}-naki"] .naki`)
          .evaluateAll((nodes) => nodes.map((node) => node.dataset.naki)),
      });
    }
    return {
      label,
      kyoku: (await text("table-kyoku")).trim(),
      kyotaku: (await text("table-kyotaku")).trim(),
      wall: (await text("table-wall")).trim(),
      dora: await page
        .locator('[data-testid="table-dora"] [data-pai]')
        .evaluateAll((nodes) => nodes.map((node) => node.dataset.pai)),
      latest: (await text("table-latest")).trim(),
      seats,
    };
  };

  const report = { steps: [] };

  report.steps.push(await snapshot("开局（坐在座位 0）"));

  // 单步几手，确认一步一 Msg。
  for (let i = 0; i < 3; i += 1) await page.getByTestId("table-step").click();
  report.steps.push(await snapshot("单步 3 手之后"));

  // 上帝视角：四家全亮。
  await page.getByTestId("table-view-god").click();
  report.steps.push(await snapshot("上帝视角"));

  // 关回座位 2：DOM 里不该再留着他家的牌。
  await page.getByTestId("table-view-2").click();
  report.steps.push(await snapshot("坐到座位 2"));

  // 8× 播到这一局终。
  await page.getByTestId("table-speed-8×").click();
  await page.getByTestId("table-play").click();
  await page.getByTestId("table-settlement").waitFor({ timeout: 120000 });
  report.steps.push(await snapshot("这一局打完"));
  report.settlement = (await text("table-settlement")).trim();

  await page.getByTestId("table-view-god").click();
  await page.screenshot({ path: `${outDir}/22-table.png`, fullPage: false });

  // 下一局：本场 / 供托的结转看得见。
  await page.getByTestId("table-view-0").click();
  await page.getByTestId("table-next").click();
  report.steps.push(await snapshot("下一局"));

  report.problems = problems;
  writeFileSync(`${outDir}/22-table-dom.json`, `${JSON.stringify(report, null, 2)}\n`);
  console.log(JSON.stringify(report, null, 2));
} finally {
  await browser.close();
  await server.close();
}

if (problems.length > 0) {
  console.error(problems.join("\n"));
  process.exit(1);
}
