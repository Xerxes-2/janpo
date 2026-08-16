// 牌桌截图（给 README 用）。**不是闸门**：它不进 CI，只负责把 `docs/images/` 下那张图重跑出来。
//
// 写法照 `verify-export.mjs`：起 vite dev server、playwright-core 开一个无头 Chrome、
// 按 testId 点「单步」推进对局，到指定手数之后给牌桌那一块拍一张 PNG。
//
// **视角是围观视角**（默认 `座位 0`，页面自己的默认值）——不开上帝视角：
// 对外展示的应当是「他家暗牌看不见」的那一份投影。
//
// 端口取 4190，与四个 `verify-*` 脚本（4179–4182）错开，跑批时并行也不撞。
//
// 跑法：
//   cd web && pnpm run fable && node scripts/shoot-table.mjs        # README 里那张：种子 1177、走 52 手
//   node scripts/shoot-table.mjs --scan 8 --seed 340 --turns 44     # 挑种子：印出各种子在目标手数时的副露与河
//
// 选项：--seed N、--turns N（先走几手）、--out <路径>、--port N、--scan N（试 N 个种子后退出）。

import { mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { createServer } from "vite";
import { chromeExecutable, missingChrome } from "./chrome.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const index = argv.indexOf(name);
  return index < 0 ? fallback : argv[index + 1];
};

const seed = Number.parseInt(flag("--seed", "1177"), 10);
const turns = Number.parseInt(flag("--turns", "52"), 10);
const port = Number.parseInt(flag("--port", "4190"), 10);
const scan = Number.parseInt(flag("--scan", "0"), 10);
const out = resolve(webRoot, "..", flag("--out", "docs/images/table.png"));

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

// dev server 而不是 preview：与 verify-export 同一个理由（要按源码路径拿页面）。
const server = await createServer({
  root: webRoot,
  server: { port, strictPort: true },
  logLevel: "warn",
});
await server.listen();
const browser = await chromium.launch({ executablePath, headless: true });

/** 走 n 手：一手一手点「单步」，等「上一手」那行变了再点下一手。
 * 一局打完就点「下一局」接着走（本场与供托结转在牌桌上看得见）。 */
async function step(page, count) {
  for (let turn = 0; turn < count; turn += 1) {
    const before = (await page.getByTestId("table-latest").textContent()).trim();
    if (await page.getByTestId("table-step").isDisabled()) {
      if (await page.getByTestId("table-next").isDisabled()) return turn; // 整场完了
      await page.getByTestId("table-next").click();
      continue;
    }
    await page.getByTestId("table-step").click();
    if (await page.getByTestId("table-step").isDisabled()) continue; // 这一局打完了
    await page.waitForFunction(
      (previous) =>
        document.querySelector('[data-testid="table-latest"]').textContent.trim() !== previous,
      before,
      { timeout: 30000 },
    );
  }
  return count;
}

/** 这一桌现在长什么样：各家副露的种类与河的长度（挑种子时看这个）。 */
async function shape(page) {
  return await page.evaluate(() =>
    [0, 1, 2, 3].map((index) => {
      const naki = [...document.querySelectorAll(`[data-testid="seat-${index}-naki"] [data-naki]`)]
        .map((node) => node.getAttribute("data-naki"))
        .join("/");
      const label = document.querySelector(
        `[data-testid="seat-${index}-kawa"] .row-label`,
      ).textContent;
      const kawa = label.match(/河 (\d+)/);
      const marks = [...document.querySelectorAll(`[data-testid="seat-${index}"] .mark`)]
        .map((node) => node.textContent)
        .join("/");
      return { seat: index, naki, marks, kawa: kawa ? Number.parseInt(kawa[1], 10) : 0 };
    }),
  );
}

async function open(page, withSeed) {
  await page.goto(`http://localhost:${port}/`, { waitUntil: "load" });
  await page.getByTestId("table-seed").fill(String(withSeed));
  await page.getByTestId("table-restart").click();
}

try {
  const page = await browser.newPage({ viewport: { width: 1180, height: 1400 } });
  const problems = [];
  page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

  if (scan > 0) {
    for (let candidate = seed; candidate < seed + scan; candidate += 1) {
      await open(page, candidate);
      const walked = await step(page, turns);
      const seats = await shape(page);
      const naki = seats.filter((view) => view.naki !== "").length;
      console.log(
        `种子 ${candidate}　走了 ${walked} 手　有副露的家 ${naki}　` +
          seats
            .map((view) => `[${view.seat}] 河${view.kawa} ${view.naki || "—"} ${view.marks}`)
            .join("　"),
      );
    }
  } else {
    await open(page, seed);
    const walked = await step(page, turns);
    console.log(`种子 ${seed}　走了 ${walked} 手`);
    for (const view of await shape(page)) {
      console.log(
        `  座位 ${view.seat}　河 ${view.kawa}　副露 ${view.naki || "无"}　标记 ${view.marks || "无"}`,
      );
    }
    console.log(`上一手：${(await page.getByTestId("table-latest").textContent()).trim()}`);

    mkdirSync(dirname(out), { recursive: true });
    await page.locator(".page.table-page").screenshot({ path: out });
    console.log(`截图：${out}`);
  }

  if (problems.length > 0) {
    console.error(problems.join("\n"));
    process.exit(1);
  }
} finally {
  await browser.close();
  await server.close();
}
