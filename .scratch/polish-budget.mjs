// 一次性评估：牌桌上面那 810 px 是谁花的（**不是闸门**）。
import { resolve } from "node:path";
import { chromium } from "../web/node_modules/playwright-core/index.mjs";
import { chromeExecutable, missingChrome } from "../web/scripts/chrome.mjs";
import { hostPage, pageUrl, startDevServer } from "../web/scripts/serve.mjs";

const webRoot = resolve(import.meta.dirname, "..", "web");
const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}
const server = await startDevServer(webRoot);
const browser = await chromium.launch({ executablePath, headless: true });

async function budget(page, label) {
  const rows = await page.evaluate(() => {
    const shell = document.querySelector(".page");
    if (!shell) return [];
    const out = [];
    for (const node of shell.children) {
      const r = node.getBoundingClientRect();
      out.push({
        tag: node.tagName.toLowerCase(),
        cls: node.className || "",
        testid: node.getAttribute("data-testid") || "",
        top: Math.round(r.top),
        height: Math.round(r.height),
        text: (node.textContent || "").trim().slice(0, 34).replace(/\s+/g, " "),
      });
      // 再下一层，方便看清操作区各排
      if (/ops|setup|replay-controls/.test(node.className)) {
        for (const kid of node.children) {
          const kr = kid.getBoundingClientRect();
          out.push({
            tag: `  └ ${kid.tagName.toLowerCase()}`,
            cls: kid.className || "",
            testid: kid.getAttribute("data-testid") || "",
            top: Math.round(kr.top),
            height: Math.round(kr.height),
            text: (kid.textContent || "").trim().slice(0, 34).replace(/\s+/g, " "),
          });
        }
      }
    }
    return out;
  });
  console.log(`\n===== ${label} =====`);
  for (const r of rows) {
    console.log(
      `${String(r.height).padStart(5)} px  top=${String(r.top).padStart(5)}  ` +
        `${r.tag} .${r.cls}${r.testid ? ` #${r.testid}` : ""}　「${r.text}」`,
    );
  }
}

try {
  for (const [which, url] of [
    ["home", `${pageUrl(server)}/`],
    ["table", hostPage(pageUrl(server))],
  ]) {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await page.goto(url, { waitUntil: "load" });
    await page.getByTestId("table-board").waitFor({ timeout: 30000 });
    await page.waitForTimeout(1500);
    await budget(page, `${which} 1280×800`);
    await page.close();
  }
} finally {
  await browser.close();
  await server.close();
}
