// 27 号验收第二节：线上站点上**逐条核 README / 页面的声称**（不带 key 的访客视角）。
// 跑法：cd /home/xerxes2/janpo-ws-a/web && node /tmp/janpo-27-site2.mjs

import playwright from "/home/xerxes2/janpo-ws-a/web/node_modules/playwright-core/index.js";
import { chromeExecutable, missingChrome } from "/home/xerxes2/janpo-ws-a/web/scripts/chrome.mjs";

const { chromium } = playwright;
const SITE = "https://xerxes-2.github.io/janpo/";

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

const browser = await chromium.launch({ executablePath, headless: true });
try {
  const context = await browser.newContext({ acceptDownloads: true });
  const page = await context.newPage();
  const origins = {};
  page.on("request", (r) => {
    const origin = new URL(r.url()).origin;
    origins[origin] = (origins[origin] ?? 0) + 1;
  });
  await page.setViewportSize({ width: 1180, height: 1600 });
  await page.goto(SITE, { waitUntil: "load" });
  const read = async (id) => (await page.getByTestId(id).textContent()).trim();

  // ① 不用 key、不用注册，打开就能走：单步 12 手
  for (let i = 0; i < 12; i += 1) await page.getByTestId("table-step").click();
  console.log(`走了 12 手：${await read("table-latest")}`);
  console.log(`Agent 状态：${await read("table-agent")}`);
  console.log(`场况：${await read("table-kyoku")}　${await read("table-wall")}`);
  console.log(`token 账单那一行在不在：${await page.getByTestId("table-usage").count()} 处（四家随机应当是 0）`);

  // ② 他家的暗牌在页面拿到的数据里根本不存在：`data-pai` 逐座位数
  const perSeat = await page.evaluate(() =>
    [0, 1, 2, 3].map((seat) => {
      const panel = document.querySelector(`[data-testid="seat-${seat}"]`);
      const tehai = panel.querySelector(`[data-testid="seat-${seat}-hand"]`);
      return {
        seat,
        tehaiText: tehai ? tehai.textContent.trim().slice(0, 20) : "（没有这一行）",
        withPai: tehai ? tehai.querySelectorAll("[data-pai]").length : -1,
        backs: tehai ? tehai.querySelectorAll(".tile.back").length : -1,
        html: panel.outerHTML.length,
        paiInHtml: (panel.outerHTML.match(/data-pai=/g) ?? []).length,
      };
    }),
  );
  for (const row of perSeat) {
    console.log(
      `  座位 ${row.seat}：手牌那一行有 data-pai ${row.withPai} 处、牌背 ${row.backs} 张；整块面板里 data-pai 共 ${row.paiInHtml} 处`,
    );
  }

  // ③ 上帝视角切得动
  await page.getByTestId("table-view-god").click();
  const god = await page.evaluate(() =>
    [0, 1, 2, 3].map((seat) => {
      const tehai = document.querySelector(`[data-testid="seat-${seat}-hand"]`);
      return tehai ? tehai.querySelectorAll("[data-pai]").length : -1;
    }),
  );
  console.log(`  上帝视角下四家手牌的 data-pai：${god.join(" / ")}`);
  await page.getByTestId("table-view-0").click();

  // ④ 脚手架档位：三档，工具搜索灰着
  const tiers = await page.evaluate(() => {
    const select = document.querySelector('[data-testid="table-llm-tier"]');
    return [...select.options].map((o) => ({ value: o.value, text: o.textContent, disabled: o.disabled }));
  });
  console.log(`  脚手架档位：${JSON.stringify(tiers)}`);

  // ⑤ provider 下拉里有哪几家
  const providers = await page.evaluate(() => {
    const select = document.querySelector('[data-testid="table-llm-provider"]');
    return select ? [...select.options].map((o) => o.value) : "（找不到这个 select）";
  });
  console.log(`  provider 下拉：${JSON.stringify(providers)}`);

  // ⑥ API key 那一格是 password（截图里不会露出来）
  const keyType = await page.evaluate(() => {
    const input = document.querySelector('[data-testid="table-llm-key"]');
    return input ? input.type : "（找不到）";
  });
  console.log(`  API key 输入框的 type：${keyType}`);

  // ⑦ 不必等终局就导得出牌谱
  const [download] = await Promise.all([
    page.waitForEvent("download", { timeout: 30000 }),
    page.getByTestId("table-export").click(),
  ]);
  const path = await download.path();
  const { readFileSync } = await import("node:fs");
  const text = readFileSync(path, "utf8");
  const paifu = JSON.parse(text);
  console.log(
    `  中途导出：${download.suggestedFilename()}　${text.length} 字节　版本 ${paifu.version}　事件 ${paifu.events.length} 条　决策记录 ${paifu.decisions.length} 条`,
  );

  await page.screenshot({ path: "/tmp/janpo-27-site-live.png", fullPage: true });
  console.log(`整页请求去了哪：${JSON.stringify(origins)}`);
} finally {
  await browser.close();
}
