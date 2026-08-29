// 一次性的评估用截图（**不是闸门、不进 CI**）：按真实视口拍「第一屏」，
// 而 `shoot-table.mjs` 拍的是整个 `.page` 元素（那张图看不出一屏装得下多少）。
//
// 跑法：cd web && node ../.scratch/polish-shots.mjs
// 出图：.scratch/shots/*.png

import { mkdirSync } from "node:fs";
import { resolve } from "node:path";
import { chromium } from "../web/node_modules/playwright-core/index.mjs";
import { chromeExecutable, missingChrome } from "../web/scripts/chrome.mjs";
import { hostPage, pageUrl, startDevServer } from "../web/scripts/serve.mjs";

const webRoot = resolve(import.meta.dirname, "..", "web");
const outDir = resolve(import.meta.dirname, "shots");
mkdirSync(outDir, { recursive: true });

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

const server = await startDevServer(webRoot);
const browser = await chromium.launch({ executablePath, headless: true });

const sizes = [
  { name: "desktop", width: 1280, height: 800 },
  { name: "laptop", width: 1440, height: 900 },
  { name: "phone", width: 390, height: 844 },
];

/** 页面总高 / 视口高：要滚几屏。 */
async function metrics(page) {
  return await page.evaluate(() => ({
    scrollHeight: document.documentElement.scrollHeight,
    innerHeight: window.innerHeight,
    boardTop: document.querySelector('[data-testid="table-board"]')?.getBoundingClientRect().top,
    boardHeight: document.querySelector('[data-testid="table-board"]')?.getBoundingClientRect()
      .height,
  }));
}

try {
  for (const size of sizes) {
    for (const which of ["home", "table"]) {
      const page = await browser.newPage({
        viewport: { width: size.width, height: size.height },
      });
      const url = which === "home" ? `${pageUrl(server)}/` : hostPage(pageUrl(server));
      await page.goto(url, { waitUntil: "load" });
      await page.getByTestId("table-board").waitFor({ timeout: 30000 });
      if (which === "home") {
        // Demo 自己在播，等河里摆上几张再拍
        await page
          .waitForFunction(
            () => {
              const text = document.querySelector(
                '[data-testid="seat-0-kawa"] .row-label',
              )?.textContent;
              const kawa = text?.match(/河 (\d+)/);
              return kawa && Number.parseInt(kawa[1], 10) >= 5;
            },
            undefined,
            { timeout: 60000 },
          )
          .catch(() => {});
      } else {
        for (let i = 0; i < 40; i += 1) {
          const step = page.getByTestId("table-step");
          if (await step.isDisabled()) break;
          await step.click();
          await page.waitForTimeout(30);
        }
      }
      const m = await metrics(page);
      console.log(
        `${which}/${size.name} ${size.width}×${size.height}　页面高 ${m.scrollHeight}　` +
          `= ${(m.scrollHeight / m.innerHeight).toFixed(2)} 屏　` +
          `牌桌顶 ${Math.round(m.boardTop)}　牌桌高 ${Math.round(m.boardHeight)}`,
      );
      // 第一屏（视口裁切）
      await page.screenshot({ path: resolve(outDir, `${which}-${size.name}-fold.png`) });
      // 整页
      await page.screenshot({
        path: resolve(outDir, `${which}-${size.name}-full.png`),
        fullPage: true,
      });
      await page.close();
    }
  }
} finally {
  await browser.close();
  await server.close();
}
