// 浏览器闸门的**共用跑道**（票 56）。
//
// 从前每道浏览器闸门各起一个 node 进程、一个 vite 服务器、一个 Chrome；
// 现在 `verify-browser.mjs` 起**一条跑道**（一个 Chrome + 按需一个 preview 服务器
// + 按需一个 dev 服务器），十道闸门各开自己的 page / context 跑在上面。
//
// **每道闸门仍然单独跑得起来**：每个 `verify-*.mjs` 都还有自己的入口
// （`pnpm run verify:board` 等），那时它自己开一条只有它用的跑道——
// 红了要单独重跑一道时，命令与从前一模一样。
//
// 两台服务器都是**懒起**的：只跑黄金用例那一道时不会白起一个 preview。

import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { chromeExecutable, missingChrome } from "./chrome.mjs";
import { pageUrl, startDevServer, startPreview } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/**
 * 开一条跑道：一个无头 Chrome，外加两台按需起的 vite 服务器。
 *
 * - `previewUrl()`：`vite preview` 托管 `dist/`（曳光弹、首页与牌桌那三道跑的是**打包后的产物**）
 * - `devUrl()`：`vite dev` 托管源码形态的 Fable 输出（黄金用例、牌谱导出、打码那几道
 *   要在页面里点名 `import` 某个模块，而 `dist/` 里文件名带哈希）
 */
export async function openLane() {
  const executablePath = chromeExecutable();
  if (!executablePath) {
    console.error(missingChrome);
    process.exit(1);
  }

  const browser = await chromium.launch({ executablePath, headless: true });
  let preview = null;
  let dev = null;

  return {
    executablePath,
    newPage: (options) => browser.newPage(options),
    newContext: (options) => browser.newContext(options),
    async previewUrl() {
      preview ??= await startPreview(webRoot);
      return pageUrl(preview);
    },
    async devUrl() {
      dev ??= await startDevServer(webRoot);
      return pageUrl(dev);
    },
    async close() {
      await browser.close();
      if (preview !== null) await preview.close();
      if (dev !== null) await dev.close();
    },
  };
}

/**
 * 一组失败。**闸门不自己 `process.exit`**：合并跑的时候要先关浏览器、再逐道汇报，
 * 在 try 里退出会把 `finally` 整个跳过（`verify-export.mjs` 早就写下过这一课）。
 */
export function failure(title, lines) {
  return [{ title, lines }];
}

/** 把一组失败印出来（单跑与合并跑印的是同一份文字）。 */
export function printFailures(failures) {
  for (const { title, lines } of failures) {
    console.error(title);
    console.error(lines.join("\n"));
  }
}

/** 这个模块是不是被直接 `node xxx.mjs` 跑起来的（而不是被合并跑的那个入口 import 进来）。 */
export function isEntry(metaUrl) {
  return process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(metaUrl);
}

/**
 * 单跑一道闸门：自己开一条跑道，跑完关掉，红了印出来并以 1 退出。
 * 合并跑的那个入口不走这里——它把十道跑在同一条跑道上。
 */
export async function runStandalone(gate) {
  const lane = await openLane();
  let failures;

  try {
    failures = await gate(lane);
  } finally {
    await lane.close();
  }

  if (failures.length > 0) {
    console.error("");
    printFailures(failures);
    process.exit(1);
  }
}
