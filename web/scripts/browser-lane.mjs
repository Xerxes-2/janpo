// 浏览器闸门的**共用跑道**（票 56）。
//
// 从前每道浏览器闸门各起一个 node 进程、一个 vite 服务器、一个 Chrome；
// 现在 `verify-browser.mjs` 起**一条跑道**（一个 Chrome + 按需一个 preview 服务器
// + 按需一个 dev 服务器），跑道上那些闸门各开自己的 page / context 跑在上面。
// **跑几趟、各是哪一趟，唯一的真源是 `verify-browser.mjs` 里那张 `gates` 表**（票 106）——
// 这份文件不再抄一个数字过来。
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
 * - `previewUrl()`：`vite preview` 托管 `dist/`（曳光弹、首页与牌桌那几道跑的是**打包后的产物**）
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

/**
 * 叙述行里那个勾。**它必须由数据决定**（票 106）。
 *
 * 这个仓库栽过一次：闸门**正确地 exit 1 并逐条印出两边的数**，而同一次跑批的叙述行照样印着
 * 「页面上那几个概率与 wasm 直接印的严格相等 ✓」——那个勾是**写死在字符串里、
 * 在收集完失败之后无条件打印的**。它不影响退出码，**但它让日志不能读**：
 * 判据 16 记着「假红比真红危险」，而「印着 ✓ 的那一行其实刚失败」是同一族里更阴的一种
 * ——**没人会去查一条打了勾的行**。
 *
 * 三种用法，按那一句印在哪儿挑：
 *
 * - `tick(ok)`：手里已经有那一项自己的判据（一个布尔）时，直接把它交出来。
 * - `mark(failures)`：**判完再印**的那几句总述（`if (…length > 0) return failure(…)` 之后）
 *   ——勾读的是那一刻的失败清单，因此写死不了。
 * - `markerSince(failures)`：**飞行中**印的那一句（这一项做完了、整道闸门还没判）。
 *   进这一项之前先 `const mark = markerSince(problems)`，那一句里摆 `${mark()}`：
 *   它记着**这一项开始时**清单有多长，于是勾说的是**这一项自己**的成败。
 *
 * **不要再往叙述行里写死 `✓`。** 写死的勾等于没有勾。
 */
export function tick(ok) {
  return ok ? "✓" : "✗";
}

/** 判完之后那几句总述的勾：清单空才是 `✓`。 */
export function mark(failures) {
  return tick(failures.length === 0);
}

/**
 * 飞行中那一句的勾：记下此刻的清单长度，之后 `mark()` 只问「这一项自己有没有往里推东西」。
 *
 * **它交出来的是一个函数，不是一个勾**——名字里那个 `Since` 就是为此：
 * 手滑写成 `${markerSince(x)}` 会把一个函数体印进日志而没有任何东西报错，
 * 而这一票治的正是「日志说谎」。
 */
export function markerSince(failures) {
  const at = failures.length;
  return () => tick(failures.length === at);
}

/** 这个模块是不是被直接 `node xxx.mjs` 跑起来的（而不是被合并跑的那个入口 import 进来）。 */
export function isEntry(metaUrl) {
  return process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(metaUrl);
}

/**
 * 单跑一道闸门：自己开一条跑道，跑完关掉，红了印出来并以 1 退出。
 * 合并跑的那个入口不走这里——它把 `gates` 表里那些跑在同一条跑道上。
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
