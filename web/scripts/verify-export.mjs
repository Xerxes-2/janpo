// 牌谱导出的**无头闸门**（票 26）：浏览器里点一下「导出牌谱」，
// 接住 playwright 的 download 事件，把**真下下来的那份字节**再喂回浏览器里的引擎 fold 一遍。
//
// 验三件事：
//   1. 点一下真的下载得到一个文件（不是「代码写了没验」）；
//   2. 那份 JSON 读得动：版本号、规则集、事件流、决策记录都在；
//   3. 它的事件流被引擎重新 fold 出**逐条相同**的事件流与同一份点数（ADR-0002 的回放）。
//
// 默认四家随机选手，**一个网络请求都不发**，因此它进 CI（M1 增量约束 6）。
// 加 `--llm` 就是人工验收那一档：一席交给真模型（key 从文件读），录出带 prompt / thinking
// 的真决策记录——**那一档调真实 provider，别放进 CI**。
//
// 跑法：
//   cd web && pnpm run fable && pnpm run verify:export
//   JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-export.mjs --llm --thinking medium
//
// 选项：--turns N（导出前先走几手，默认 60）、--seat N、--model X、--thinking off|low|medium|high、
//       --keep <路径>（把下下来的牌谱另存一份，报告里的样例就是这么来的）。

import { copyFileSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { createServer } from "vite";
import { chromeExecutable, missingChrome } from "./chrome.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const PORT = 4182;

const argv = process.argv.slice(2);
const flag = (name, fallback) => {
  const index = argv.indexOf(name);
  return index < 0 ? fallback : argv[index + 1];
};

const withLlm = argv.includes("--llm");
const turns = Number.parseInt(flag("--turns", "60"), 10);
const seat = Number.parseInt(flag("--seat", "1"), 10);
const model = flag("--model", "deepseek-v4-flash");
const thinking = flag("--thinking", "off");
const keep = flag("--keep", null);
const budgetMs = Number.parseInt(flag("--budget", "600000"), 10);

const apiKey = withLlm
  ? readFileSync(process.env.JANPO_KEY_FILE ?? "/tmp/deepseek_key", "utf8").trim()
  : null;

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

// 用 dev server 而不是 preview：闸门要在页面里点名 `import` Fable 输出的 `PaifuCheck.js`，
// 而 `dist/` 里的模块被打成一坨、文件名带哈希（与 verify-golden 同一个理由）。
const server = await createServer({
  root: webRoot,
  server: { port: PORT, strictPort: true },
  logLevel: "warn",
});
await server.listen();
const browser = await chromium.launch({ executablePath, headless: true });
const problems = [];

try {
  const context = await browser.newContext({ acceptDownloads: true });
  const page = await context.newPage();
  page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
  page.on("console", (message) => {
    if (message.type() !== "error") return;
    // 断电演习式的失败请求浏览器自己会往 console 写一条，那不是页面的错。
    if (message.text().includes("Failed to load resource")) return;
    problems.push(`[console.error] ${message.text()}`);
  });

  if (withLlm) {
    await page.addInitScript(
      ([seat, model, apiKey, thinking]) => {
        localStorage.setItem("janpo.llm.seat", String(seat));
        localStorage.setItem("janpo.llm.provider", "deepseek");
        localStorage.setItem("janpo.llm.model", model);
        localStorage.setItem("janpo.llm.api_key", apiKey);
        localStorage.setItem("janpo.llm.timeout_ms", "60000");
        localStorage.setItem("janpo.llm.thinking", thinking);
      },
      [seat, model, apiKey, thinking],
    );
  }

  await page.goto(`http://localhost:${PORT}/`, { waitUntil: "load" });

  const readText = async (testId) => (await page.getByTestId(testId).textContent()).trim();

  console.log(
    `模式：${withLlm ? `一席交给 ${model}（思考预算 ${thinking}）` : "四家随机选手（不发任何请求）"}　先走 ${turns} 手`,
  );

  // 一手一手走：单步之后要么当场落子，要么在等模型——等到「上一手」变了为止。
  for (let turn = 0; turn < turns; turn += 1) {
    const before = await readText("table-latest");
    await page.getByTestId("table-step").click();
    if (await page.getByTestId("table-step").isDisabled()) break; // 这一局打完了
    await page
      .waitForFunction(
        (previous) =>
          document.querySelector('[data-testid="table-latest"]').textContent.trim() !== previous,
        before,
        { timeout: budgetMs },
      )
      .catch(() => problems.push(`第 ${turn} 手没走动`));
  }

  console.log(`走完之后：${await readText("table-latest")}`);
  console.log(`Agent 状态：${await readText("table-agent")}`);

  const [download] = await Promise.all([
    page.waitForEvent("download", { timeout: 30000 }),
    page.getByTestId("table-export").click(),
  ]);

  const file = await download.path();
  const text = readFileSync(file, "utf8");
  console.log("");
  console.log(`下载文件名：${download.suggestedFilename()}　${text.length} 字节`);
  if (keep) {
    copyFileSync(file, resolve(keep));
    console.log(`另存到：${resolve(keep)}`);
  }

  // 下下来的那份字节交回浏览器里的引擎：fold 一遍，与它自己记的事件流逐条对照。
  const report = JSON.parse(
    await page.evaluate(async (paifu) => {
      const check = await import("/src/generated/PaifuCheck.js");
      return check.check(paifu);
    }, text),
  );

  if (report.error) {
    problems.push(`浏览器里的引擎读不动 / 回放不动这份牌谱：${report.error}`);
  } else {
    console.log(
      `牌谱：版本 ${report.version}　事件 ${report.events} 条　决策记录 ${report.decisions} 条` +
        `（其中带 thinking ${report.thinking} 条、兜底 ${report.fallbacks} 条）　已打完 ${report.kyokus} 局`,
    );
    console.log(`回放：事件流逐条相同 = ${report.same_events}　点数 ${report.scores.join(" / ")}`);
    if (!report.same_events) problems.push(`回放出的事件流与牌谱不同：${report.mismatch}`);

    // 页面上的点数与回放算出来的点数必须一致——牌桌与 fold 出来的是同一场对局。
    const onPage = await Promise.all(
      [0, 1, 2, 3].map(async (index) => Number.parseInt(await readText(`seat-${index}-score`), 10)),
    );
    if (onPage.join(",") !== report.scores.join(",")) {
      problems.push(
        `牌桌上的点数 ${onPage.join("/")} 与回放算出的 ${report.scores.join("/")} 不同`,
      );
    }
    if (report.events < 10) problems.push(`只导出了 ${report.events} 条事件，这一桌根本没走动`);
    if (withLlm && report.decisions === 0) problems.push("一席交给了模型，却一条决策记录都没有");
  }

  // 可分享物里永远不能出现 API key（ADR-0003）。
  if (apiKey !== null && text.includes(apiKey)) problems.push("导出的牌谱里出现了 API key");
} finally {
  await browser.close();
  await server.close();
}

if (problems.length > 0) {
  console.error("");
  console.error("导出验收没过：");
  console.error(problems.join("\n"));
  process.exit(1);
}

console.log("导出的牌谱下得下来、读得动、回放得回去 ✓");
