// 27 号票第二节：**真的打开线上站点核一遍**。
//   1. 默认视图里没有开发面（曳光弹的三个 testId 与「曳光弹 / Fable / dotnet」这几个词）
//   2. 页脚两条链接**点得开**（真发 HTTP 请求看状态码，不是只看 href 存在）
//   3. `?dev=1` 才出曳光弹
// 跑法：cd /home/xerxes2/janpo-ws-a/web && node /tmp/janpo-27-site.mjs

import playwright from "/home/xerxes2/janpo-ws-a/web/node_modules/playwright-core/index.js";
import { chromeExecutable, missingChrome } from "/home/xerxes2/janpo-ws-a/web/scripts/chrome.mjs";

const { chromium } = playwright;
const SITE = "https://xerxes-2.github.io/janpo/";
const DEV_IDS = ["traces", "seed-input", "rerun"];
const DEV_WORDS = ["曳光弹", "Fable", "dotnet"];

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

const browser = await chromium.launch({ executablePath, headless: true });
const problems = [];

try {
  const page = await browser.newPage();
  const consoleErrors = [];
  page.on("pageerror", (error) => consoleErrors.push(`[pageerror] ${error.message}`));
  page.on("console", (m) => {
    if (m.type() === "error") consoleErrors.push(`[console.error] ${m.text()}`);
  });

  const response = await page.goto(SITE, { waitUntil: "load" });
  console.log(`GET ${SITE} → ${response.status()}`);
  console.log(`<title>：${await page.title()}`);
  console.log(`h1：${(await page.locator("h1").first().textContent()).trim()}`);

  // ---- 1. 默认视图无开发面 ----
  const body = await page.evaluate(() => document.body.innerText);
  for (const id of DEV_IDS) {
    const count = await page.getByTestId(id).count();
    console.log(`  默认视图 testId ${id}：${count} 个`);
    if (count > 0) problems.push(`默认视图里出现了开发向 testId ${id}`);
  }
  for (const word of DEV_WORDS) {
    const hit = body.includes(word);
    console.log(`  默认视图正文含「${word}」：${hit}`);
    if (hit) problems.push(`默认视图正文里出现了开发向词汇「${word}」`);
  }

  // 牌桌本身在（不是把整页都藏了）
  for (const id of ["table-board", "table-step", "table-export", "table-llm-panel"]) {
    const count = await page.getByTestId(id).count();
    console.log(`  默认视图 testId ${id}：${count} 个`);
    if (count === 0) problems.push(`默认视图里少了 ${id}`);
  }

  // ---- 2. 页脚的外链 ----
  const links = await page.evaluate(() =>
    [...document.querySelectorAll("footer a, .footer a, a")]
      .map((a) => ({ href: a.href, text: a.textContent.trim(), target: a.target, rel: a.rel }))
      .filter((a) => a.href.startsWith("http")),
  );
  console.log("  页面上的外链：");
  for (const link of links) console.log(`    ${link.text} → ${link.href}（target=${link.target} rel=${link.rel}）`);
  if (links.length === 0) problems.push("页面上一条外链都没有（页脚那条回仓库的路没了）");
  console.log(`  正文含「MIT」：${body.includes("MIT")}`);
  if (!body.includes("MIT")) problems.push("页面上没有提到 MIT 许可");

  // **点得开**：逐条真发请求。
  for (const link of links) {
    // 真的用浏览器导航过去（GitHub 对裸 HTTP 客户端会回 429，那不是链接坏了）。
    const opened = await page.goto(link.href, { waitUntil: "domcontentloaded" });
    console.log(`    浏览器打开 ${link.href} → ${opened.status()}　<title>：${(await page.title()).slice(0, 60)}`);
    if (opened.status() >= 400) problems.push(`页脚链接打不开：${link.href} → ${opened.status()}`);
  }

  await page.screenshot({ path: "/tmp/janpo-27-site-default.png", fullPage: true });

  // ---- 3. ?dev=1 才出曳光弹 ----
  const devResponse = await page.goto(`${SITE}?dev=1`, { waitUntil: "load" });
  console.log(`GET ${SITE}?dev=1 → ${devResponse.status()}`);
  for (const id of DEV_IDS) {
    const count = await page.getByTestId(id).count();
    console.log(`  ?dev=1 下 testId ${id}：${count} 个`);
    if (count === 0) problems.push(`?dev=1 之下仍然没有 ${id}`);
  }
  const devBody = await page.evaluate(() => document.body.innerText);
  for (const word of DEV_WORDS) {
    console.log(`  ?dev=1 正文含「${word}」：${devBody.includes(word)}`);
  }
  for (const id of ["kyoku-command", "kyoku-scores", "kyoku-juni", "game-scores", "game-juni", "game-kyokus"]) {
    const text = await page.getByTestId(id).textContent().catch(() => "（读不到）");
    console.log(`  曳光弹 ${id}：${text.trim()}`);
  }
  await page.screenshot({ path: "/tmp/janpo-27-site-dev.png", fullPage: true });

  console.log(`console 错误：${consoleErrors.length} 条`);
  for (const line of consoleErrors) console.log(`  ${line}`);
} finally {
  await browser.close();
}

if (problems.length > 0) {
  console.error("\n线上站点核对没过：");
  console.error(problems.join("\n"));
  process.exit(1);
}
console.log("\n线上站点核对通过 ✓");
