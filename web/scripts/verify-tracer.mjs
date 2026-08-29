// 无头验收（19 票）：同一种子，**浏览器里的引擎**与 **dotnet 侧的 CLI** 必须逐项相同。
//
// 这是「双目标语义没漂」的第一份证据。系统化的黄金用例是 21 票的事，这里只钉住一颗曳光弹：
// 一局（janpo kyoku）与一整场（janpo game）的终局点数与顺位。
//
// **地址带 `?dev=1`**（票 35）：曳光弹是开发向的自检页，默认访客看不到它，
// 只有这个开关能把它摆回牌桌下面（判据在 `src/Janpo.Web/Route.fs`）。
//
// **这一道一共开三次地址**（票 71），三次各量一件事：
//
//   `/`         首页：里面不得有开发向内容，且页脚那条回仓库的路必须在（票 35 / 37）
//   `?table=1`  主持人那一页：副露看得出被鸣的那张与来源（票 38）
//               ——它要填种子、要一手一手点单步，而那两个控件只在这一页上
//   `?dev=1`    曳光弹：与 dotnet 侧逐项对拍（票 19）
//
// 跑法：`cd web && pnpm run build && pnpm run verify [-- --seed 1177]`
// 它也是 `verify-browser.mjs` 里的一道（跑道上那几趟共用一个浏览器与一台服务器）。
// 浏览器：优先 $JANPO_CHROME，其次 playwright 自带的 chromium，最后系统里的 chrome/chromium。

import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, runStandalone, tick } from "./browser-lane.mjs";
import { checkNakiGroups, readNakiGroups } from "./naki-marks.mjs";
import { hostPage } from "./serve.mjs";
import { openSetup, stepTurns } from "./table-drive.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(webRoot, "..");

// 默认种子与 F# 侧的 `Tracer.defaultSeed` 一致（挑它的理由见 src/Janpo.Web/Tracer.fs）。
const DEFAULT_SEED = 1177;

function parseSeed(argv) {
  const index = argv.indexOf("--seed");
  if (index < 0) return DEFAULT_SEED;
  const seed = Number.parseInt(argv[index + 1], 10);
  if (Number.isNaN(seed)) throw new Error(`--seed 要一个整数，得到「${argv[index + 1]}」`);
  return seed;
}

// ---- dotnet 侧 ----

/** 已经构好的 CLI（`dotnet build -c Release` 的产物）。 */
const CLI_DLL = resolve(repoRoot, "src/Janpo.Cli/bin/Release/net10.0/janpo.dll");

/**
 * 跑一条 CLI 子命令，返回它的 stdout 全文。
 *
 * **构好了就直接跑那份 DLL**：`dotnet run` 每调一次都要重新求一遍 MSBuild
 * （实测 1.20s vs 直调 DLL 0.12s，而这一道要调两次）。`ci.sh` 先 build 后才跑到这里，
 * 因此 CI 里恒走前一条；没构过的工作区里单跑它时自动退回 `dotnet run`（跑得慢一点，
 * 但**跑的是同一个 CLI、对拍的是同一堆数**）。
 */
function runCli(args) {
  const command = existsSync(CLI_DLL)
    ? [CLI_DLL, ...args]
    : ["run", "--project", "src/Janpo.Cli", "--configuration", "Release", "--", ...args];

  return execFileSync("dotnet", command, {
    cwd: repoRoot,
    encoding: "utf8",
    maxBuffer: 128 * 1024 * 1024,
  });
}

/**
 * CLI 输出末尾那几行「<键>: <值>」。事件流每行是 JSON（以 `{` 开头），不会被误当成键值行。
 */
function summaryLines(text) {
  const summary = {};
  for (const line of text.split("\n")) {
    const matched = /^(scores|juni|kyokus):\s*(.*)$/.exec(line);
    if (matched) summary[matched[1]] = matched[2].trim();
  }
  return summary;
}

// ---- 浏览器侧 ----

/** 曳光弹那一块的开关（票 35）。页面侧认它的地方只有 `Route.devSurfaceRequested` 一处。 */
const DEV_QUERY = "?dev=1";

/** 曳光弹在页面上的钩子。带开关时它们都在，不带时一个都不该在。 */
const DEV_TEST_IDS = ["traces", "seed-input", "rerun"];

/** 只属于开发向叙述的词。默认视图的正文里出现任何一个都算漏。 */
const DEV_WORDS = ["曳光弹", "Fable", "dotnet"];

/**
 * 票 38 那几条断言走哪一局：**种子 1223 走 90 手同时摆着吃、碰、暗杠与加杠**
 * （四家都是随机选手，同一种子每次走出来的一模一样）。一局打完就停，不往下一局走。
 */
const NAKI_SEED = 1223;
const NAKI_TURNS = 90;

/**
 * 票 38：副露上**被鸣的是哪一张、来自谁**必须真的挂在 DOM 上。
 *
 * 这两件事早就在数据里（引擎的 `Naki.Taken` / `Naki.Target`），prompt 尾部也写了，
 * 之前只有**牌桌上**看不见——围观者因此看不懂这一局怎么走的。它又死回去也不会有人发现，
 * 因此摆在默认视图这道闸门里。逐组验四件事：
 *
 * 1. 非暗杠的每一组**恰好一张**带 `data-naki-taken`（横放的那张），且写了来源；
 * 2. 暗杠两样都没有——它不是鸣来的；
 * 3. 来源的**中文说法与绝对座位对得上**：参照系是副露方，不是观测者（漂了这条当场就红），
 *    且**吃恒来自上家**；
 * 4. 只有加杠有那一张 `data-naki-added`（后加上去的，出自自家手里）。
 *
 * 头一条是防空转的：一组鸣来的副露都没走出来的话，下面全部断言都会空着全绿。
 */
async function checkNaki(page, url) {
  // **开主持人那一页**（票 71）：这段要填种子、要一手一手点单步，
  // 而那两个控件只在 `?table=1` 上；首页是自动播的回放，没有种子可换。
  // 断言一条没改：验的仍然是同一颗种子、同一批副露、同四条性质。
  await page.goto(hostPage(url), { waitUntil: "load" });
  await page.getByTestId("table-board").waitFor();
  await openSetup(page);
  await page.getByTestId("table-seed").fill(String(NAKI_SEED));
  await page.getByTestId("table-restart").click();
  // 一手一手点「单步」，等牌桌真的走动了再点下一手（驱动在 `table-drive.mjs`）。
  const { walked } = await stepTurns(page, { limit: NAKI_TURNS });

  const groups = await readNakiGroups(page);
  const { missing, seen } = checkNakiGroups(groups);

  // 防空转：一组鸣来的副露都没走出来的话，上面那几条断言会空着全绿。
  const called = groups.filter((group) => group.kind !== "暗杠");
  if (called.length === 0)
    missing.push(
      `种子 ${NAKI_SEED} 走了 ${walked} 手，一组鸣来的副露都没有：副露那几条断言全在空转`,
    );

  return { missing, seen: `种子 ${NAKI_SEED} 走 ${walked} 手：${seen}` };
}

/**
 * 票 35 的反向自证：**不带开关时，默认视图里不得有开发向内容**；
 * 票 37 的正面：**默认视图里必须有回仓库的那一行**。
 *
 * 下面那道对拍已经证明了「带上 `?dev=1` 曳光弹还在」（它读的就是曳光弹的 testId）；
 * 只有它的话，把开关废掉、访客又看到调试页也照样全绿。两道合起来才是一个开关。
 *
 * 两件事分两个数组报：`leaks` 是「多了不该给访客看的」，`missing` 是「少了该给访客的」。
 */
async function checkDefaultView(page, url) {
  const leaks = [];
  const missing = [];
  await page.goto(`${url}/`, { waitUntil: "load" });
  // 牌桌本人必须在：否则「什么都没渲染出来」也能让下面几条断言全部通过。
  // 首页的牌桌要等那份 Demo 牌谱 `fetch` 回来才摆得出来（票 71）。
  await page.getByTestId("table-board").waitFor();

  for (const testId of DEV_TEST_IDS) {
    const count = await page.getByTestId(testId).count();
    if (count !== 0) leaks.push(`默认视图里还挂着 [data-testid="${testId}"]（${count} 个）`);
  }

  const text = await page.evaluate(() => document.body.innerText);
  for (const word of DEV_WORDS) {
    if (text.includes(word)) leaks.push(`默认视图的正文里出现了开发向的词「${word}」`);
  }

  // 票 37：访客落在站点上时，页脚那条外链是回到源码与许可的**唯一一条路**。
  // 它被谁顺手删掉、或被挪到 `?dev=1` 后面，页面看上去照常，没人会发现。
  // 地址本身不在这里复述（真源在 `src/Janpo.Web/Footer.fs` 一处），只断言它真是一条链接。
  const footerLinks = await page.getByTestId("site-footer").locator('a[href^="https://"]').count();
  if (footerLinks === 0) missing.push("默认视图的页脚里没有一条指回仓库的外链（票 37）");
  if (!text.includes("MIT")) missing.push("默认视图的正文里没提许可（MIT）（票 37）");

  // 票 38：副露的来源与被鸣的那张。开局那一帧一组副露也没有，得先把牌局走起来
  // ——它因此另开 `?table=1`（票 71），上面那几条仍旧量的是 `/`。
  const naki = await checkNaki(page, url);

  return { leaks, missing: missing.concat(naki.missing), naki: naki.seen };
}

/**
 * 起一个 vite preview 托管 dist/，用无头 Chrome 打开页面，把种子输进去点「重跑」，
 * 再把页面上的数读回来。
 *
 * **先用一个诱饵种子跑一遍**：否则当请求的种子恰好等于页面默认种子时，
 * 「输入框没生效」与「输入框生效了」两种情况读到的数一模一样，这条验收就名存实亡。
 */
async function readBrowser(lane, seed) {
  const url = await lane.previewUrl();
  const page = await lane.newPage();
  const problems = [];

  try {
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
    });

    const { leaks, missing, naki } = await checkDefaultView(page, url);

    await page.goto(`${url}/${DEV_QUERY}`, { waitUntil: "load" });

    const rerunWith = async (value) => {
      await page.getByTestId("seed-input").fill(String(value));
      await page.getByTestId("rerun").click();
      await page.waitForFunction(
        (expected) =>
          document.querySelector('[data-testid="kyoku-command"]')?.textContent === expected,
        `janpo kyoku ${value}`,
      );
    };

    await rerunWith(seed === DEFAULT_SEED ? seed + 1 : DEFAULT_SEED);
    await rerunWith(seed);

    const read = async (testId) => (await page.getByTestId(testId).textContent()).trim();
    const trace = async (prefix) => ({
      scores: await read(`${prefix}-scores`),
      juni: await read(`${prefix}-juni`),
      kyokus: await read(`${prefix}-kyokus`),
    });

    return {
      kyoku: await trace("kyoku"),
      game: await trace("game"),
      problems,
      leaks,
      missing,
      naki,
    };
  } finally {
    await page.close();
  }
}

// ---- 对照 ----

function compare(label, keys, dotnetSide, browserSide, failures) {
  const rows = [];
  for (const key of keys) {
    const expected = dotnetSide[key];
    const actual = browserSide[key];
    const same = expected === actual;
    if (!same) failures.push(`${label}.${key}: dotnet「${expected}」≠ 浏览器「${actual}」`);
    rows.push(`  ${tick(same)} ${label}.${key.padEnd(6)} ${expected}`);
  }
  return rows;
}

/** 曳光弹对拍那一道（顺带默认视图与副露来源）。返回的是失败清单（空 = 绿）。 */
export async function verifyTracer(lane, { seed = DEFAULT_SEED } = {}) {
  console.log(`种子 ${seed}，浏览器 ${lane.executablePath}`);

  const dotnetSide = {
    kyoku: summaryLines(runCli(["kyoku", String(seed)])),
    game: summaryLines(runCli(["game", String(seed)])),
  };
  const browserSide = await readBrowser(lane, seed);

  const drifted = [];
  const rows = [
    ...compare("kyoku", ["scores", "juni"], dotnetSide.kyoku, browserSide.kyoku, drifted),
    ...compare("game", ["scores", "juni", "kyokus"], dotnetSide.game, browserSide.game, drifted),
  ];

  console.log("dotnet 侧 vs 浏览器侧：");
  console.log(rows.join("\n"));

  if (browserSide.problems.length > 0) return failure("页面报了错：", browserSide.problems);
  if (browserSide.leaks.length > 0)
    return failure("默认视图（不带 ?dev=1）里漏出了开发向内容：", browserSide.leaks);
  if (browserSide.missing.length > 0)
    return failure("默认视图里少了该给访客的东西：", browserSide.missing);
  if (drifted.length > 0) return failure("双目标语义漂了：", drifted);

  console.log("默认视图里没有曳光弹，带上 ?dev=1 它回来了 ✓");
  console.log("默认视图的页脚里有回仓库的外链与许可（MIT）✓");
  console.log(`副露看得出被鸣的那张与来源 ✓（${browserSide.naki}）`);
  console.log("浏览器内的引擎与 dotnet 侧逐项相同 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  const seed = parseSeed(process.argv.slice(2));
  await runStandalone((lane) => verifyTracer(lane, { seed }));
}
