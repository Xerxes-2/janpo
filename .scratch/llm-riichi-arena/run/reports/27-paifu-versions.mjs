// 27 号票第二节最后一条：**亲手核一遍「导出 → 改版本 → 读回」**。
// 拿真跑那一场导出的 v2 牌谱，手工降级成 v1 的形状（`prompt_tail` → `prompt`，
// 去掉 `render_version` / `action_ids` / `prompting`），两份都喂进浏览器里的引擎。
// 再加两条反面：版本号写 3 必须读不动；v2 里把 `prompting` 拿掉、重建那条必须报红。
// 跑法：cd /home/xerxes2/janpo-ws-a/web && node /tmp/janpo-27-versions.mjs /tmp/janpo-27-bare.json

import { readFileSync } from "node:fs";
import playwright from "/home/xerxes2/janpo-ws-a/web/node_modules/playwright-core/index.js";
import { chromeExecutable, missingChrome } from "/home/xerxes2/janpo-ws-a/web/scripts/chrome.mjs";
import { pageUrl, startDevServer } from "/home/xerxes2/janpo-ws-a/web/scripts/serve.mjs";

const { chromium } = playwright;
const webRoot = "/home/xerxes2/janpo-ws-a/web";
const source = process.argv[2] ?? "/tmp/janpo-27-bare.json";

const v2 = JSON.parse(readFileSync(source, "utf8"));

/** 手工降级成票 31 说的那种 v1：整份 prompt 存在 `prompt` 里，没有前置那一段。 */
const downgrade = (paifu) => ({
  version: 1,
  ruleset: paifu.ruleset,
  events: paifu.events,
  decisions: paifu.decisions.map((record) => {
    const { prompt_tail, render_version, action_ids, ...rest } = record;
    return { ...rest, prompt: prompt_tail };
  }),
});

const cases = [
  ["v2（真导出的那份，一字未改）", JSON.stringify(v2), true],
  ["v1（手工降级：prompt_tail → prompt，去掉 prompting）", JSON.stringify(downgrade(v2)), true],
  ["版本号 3（不该读得动）", JSON.stringify({ ...v2, version: 3 }), false],
  [
    "v2 但把 prompting.preambles 掏空（重建那条该报红）",
    JSON.stringify({ ...v2, prompting: { ...v2.prompting, preambles: [] } }),
    "rebuildable-false",
  ],
];

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

const server = await startDevServer(webRoot);
const browser = await chromium.launch({ executablePath, headless: true });
const problems = [];

try {
  const page = await browser.newPage();
  await page.goto(`${pageUrl(server)}/`, { waitUntil: "load" });

  for (const [name, text, expectation] of cases) {
    const report = JSON.parse(
      await page.evaluate(async (paifu) => {
        const check = await import("./src/generated/PaifuCheck.js");
        return check.check(paifu);
      }, text),
    );
    console.log(`\n${name}　${text.length} 字节`);
    console.log(`  ${JSON.stringify(report)}`);

    if (expectation === true) {
      if (report.error) problems.push(`${name}：读不动 —— ${report.error}`);
      else if (!report.same_events) problems.push(`${name}：回放出的事件流对不上`);
    } else if (expectation === false) {
      if (!report.error) problems.push(`${name}：竟然读得动`);
    } else if (expectation === "rebuildable-false") {
      if (report.rebuildable !== false) problems.push(`${name}：preamble 掏空了 rebuildable 仍是 true`);
    }
  }
} finally {
  await browser.close();
  await server.close();
}

if (problems.length > 0) {
  console.error("\n版本兼容核对没过：");
  console.error(problems.join("\n"));
  process.exit(1);
}
console.log("\nv2 读得动、v1 也读得动、版本 3 读不动、preamble 掏空重建报红 ✓");
