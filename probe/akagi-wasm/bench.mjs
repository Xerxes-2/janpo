// 浏览器里跑一趟并把数抄出来：起静态服务器 → 无头 Chromium 开 index.html →
// 读页面算好的 `window.__probeResults`。
//   node bench.mjs [reps] [port]
//
// playwright-core 与浏览器都用 `web/node_modules` 里已经装好的那份（只读，不动 web/**）。
//
// **每一处「等」都带超时，每一个子进程都带 error/exit 监听。**
// 上一轮就死在这条：端口被残留的 serve.mjs 占着 → 新服务器报错退出 → 永远不吐 stdout →
// `await new Promise((resolve) => server.stdout.once("data", resolve))` 无限等，
// 看门狗 45 分钟后把整个 agent 砍了。裸的 `once("data")` 是个没有下界的等待，
// 这个文件里不许再出现。
import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const reps = Number(process.argv[2] ?? 200);
const port = Number(process.argv[3] ?? 4191);

const SERVER_BOOT_MS = 10_000;
const PAGE_MS = 120_000;

/** 浏览器的查找顺序照抄 `web/scripts/chrome.mjs`（只读地复用，不动 web/**）。 */
function chromeExecutable(chromium) {
  if (process.env.JANPO_CHROME) return process.env.JANPO_CHROME;
  try {
    const bundled = chromium.executablePath();
    if (bundled && existsSync(bundled)) return bundled;
  } catch {
    // playwright-core 没装过浏览器时会抛，落到下面的系统路径。
  }
  return [
    "/usr/bin/google-chrome-stable",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser",
  ].find((path) => existsSync(path));
}

/** 起服务器之前先自己占一下端口：占不上就当场失败，不要等到超时才知道。 */
function assertPortFree(p) {
  return new Promise((resolve, reject) => {
    const probe = createServer();
    probe.once("error", (e) =>
      reject(new Error(`端口 ${p} 被占用（${e.code}）：先 \`pgrep -af serve.mjs\` 清残留`)),
    );
    probe.once("listening", () => probe.close(() => resolve()));
    probe.listen(p, "127.0.0.1");
  });
}

/** 起 serve.mjs，等它自报家门；它先 exit / error 就立刻失败，绝不静默等。 */
function startServer() {
  const child = spawn(process.execPath, [join(here, "serve.mjs"), String(port)], {
    stdio: ["ignore", "pipe", "inherit"],
  });
  const ready = new Promise((resolve, reject) => {
    const timer = setTimeout(
      () => reject(new Error(`serve.mjs ${SERVER_BOOT_MS} ms 内没起来`)),
      SERVER_BOOT_MS,
    );
    const done = (fn) => (arg) => {
      clearTimeout(timer);
      fn(arg);
    };
    child.stdout.once("data", done(resolve));
    child.once("error", done(reject));
    child.once("exit", done((code) => reject(new Error(`serve.mjs 提前退出：code=${code}`))));
  });
  return { child, ready };
}

await assertPortFree(port);

const { chromium } = await import(join(here, "../../web/node_modules/playwright-core/index.mjs"));

const { child: server, ready } = startServer();
let browser;
try {
  await ready;
  const executablePath = chromeExecutable(chromium);
  if (!executablePath) throw new Error("找不到可用的 Chrome/Chromium（用 JANPO_CHROME=<路径> 指过去）");
  browser = await chromium.launch({ executablePath, headless: true });
  // 每趟一个全新 context：HTTP 缓存是空的，量到的 fetch 就是冷缓存那一次。
  const context = await browser.newContext();
  const page = await context.newPage();
  page.on("console", (m) => {
    if (m.type() === "error") console.error("[page]", m.text());
  });
  page.on("pageerror", (e) => console.error("[pageerror]", e.message));
  await page.goto(`http://localhost:${port}/?reps=${reps}`, {
    waitUntil: "load",
    timeout: PAGE_MS,
  });
  await page.waitForFunction(() => window.__probeResults !== undefined, null, { timeout: PAGE_MS });
  const out = await page.evaluate(() => window.__probeResults);
  console.log(JSON.stringify(out, null, 2));
  await context.close();
  process.exitCode = out.verdict === "ok" ? 0 : 1;
} catch (e) {
  console.error(`bench 失败：${e.message}`);
  process.exitCode = 1;
} finally {
  // 顺序要紧：先关浏览器再杀服务器，免得关闭途中的请求打到已死的端口上刷一屏噪音。
  await browser?.close().catch(() => {});
  server.kill("SIGTERM");
  // 收尸，否则 node 可能在子进程还占着端口时就退出，下一趟又撞车。
  await new Promise((resolve) => {
    const timer = setTimeout(() => {
      server.kill("SIGKILL");
      resolve();
    }, 3000);
    server.once("exit", () => {
      clearTimeout(timer);
      resolve();
    });
  });
}
