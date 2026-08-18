// **强 AI 基线坐一席**那道闸门（票 92；ADR-0006 的边界 1、2 与要害「它不会说话」）。
//
// 四趟，各答一件事：
//
//   ① **懒加载**（边界 1）：首页与不选那一席的对局**网络请求计数为 0**
//      ——量的是浏览器真发出去的请求，不是「看着没拉」。
//      **阳性对照**：同一条地址、把某一席拨到强 AI 基线，请求恰好多出一条。
//   ② **降级不是 try/catch**（边界 2）：把那份资产的请求掐掉，断言页面**明说原因**、
//      那一席退回自带 bot（`data-seats` 与名牌一起说实话），**其余席照常打完一局**。
//   ③ **它不会说话**（票 92 的要害）：它那一席**没有气泡、没有账单行**——
//      不是显示一个空气泡或者「0 tok」。阳性对照是同一桌上的模型席：它有气泡、也有账单。
//   ④ **与真人同桌**（票 87 已在的那条）：真人 + 强 AI + 两个 bot 打一段，
//      整页 HTML 里**他家的手牌一张都不许有**——那条结构性不泄露照旧绿。
//
// **CI 里跑到的是「资产不在」那一路**（ADR-0006 边界 6：6 MB 不入版本控制）：
// 于是 ② 是常态，而 ① 的阳性对照量的是「请求真的发出去了」（回的是 404）。
// **真跑推理只在本机演习那一档**：先按 `web/public/baseline/README.md` 造一份产物放进去，
// 再 `node scripts/verify-baseline.mjs --asset`——那一档多跑第 ⑤ 趟（它真打一局）。
// **CI 因此覆盖不到「它出的那一手对不对」**，逐条写在报告 92 里（判据 4）。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:baseline`
// 选项：--asset（本机演习：资产在场，多跑真打一局那一趟）、--budget ms。

import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { plantSeating, profileChoice } from "./seating.mjs";
import { hostPage } from "./serve.mjs";
import { stepTurns } from "./table-drive.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** 那份产物在站点里的地址（与 `web/src/baseline/wasm.ts` 的 `ASSET_FILE` 逐字相同）。 */
const ASSET = "baseline/janpo-baseline.wasm";

/** 站点上到底有没有那份产物（决定第 ① 趟的阳性对照该期望 200 还是 404）。 */
const assetPresent = () => existsSync(resolve(webRoot, "public", ASSET));

/** 假端点固定回的那句理由（**只可能从它那儿来**：页面里没有任何一处写着它）。 */
const SAID = "假端点说：这一手照它的算法只能这么打";

/** 借内核要一个空闲端口（跑批是并行的，写死端口迟早撞上另一个工作区；判据 16）。 */
function freePort() {
  return new Promise((done, fail) => {
    const probe = createServer();
    probe.on("error", fail);
    probe.listen(0, "127.0.0.1", () => {
      const { port } = probe.address();
      probe.close(() => done(port));
    });
  });
}

/** 起一个本机假端点（**一个字节都不出网**），返回它的 baseUrl 与那个进程。 */
async function startEndpoint(origin) {
  const port = await freePort();
  const endpoint = spawn(
    "node",
    [
      "scripts/fake-endpoint.mjs",
      "--port",
      String(port),
      "--cors",
      origin,
      "--quiet",
      "--reason",
      SAID,
    ],
    { cwd: webRoot, stdio: ["ignore", "ignore", "inherit"] },
  );
  return { baseUrl: `http://127.0.0.1:${port}/v1`, endpoint };
}

/** 一个元素的 `data-*`，没有就是 `null`（理由同 `verify-human.mjs` 的同名函数）。 */
function attr(page, testId, name) {
  return page.evaluate(
    ({ testId, name }) =>
      document.querySelector(`[data-testid="${testId}"]`)?.getAttribute(name) ?? null,
    { testId, name },
  );
}

/**
 * 等那几 MB 有个下场（拉到了 / 拉不动）。
 *
 * **不等的话这一桌是停着的**（`TableState.waiting`：轮到强 AI 基线而资产还在路上时
 * 定时器不续、单步也推不动），于是后面那些「走几十手」会全数落空——
 * 而那种落空长得像「它不会说话」，正是判据 16 那类假红。
 */
function settledBaseline(page) {
  return page.waitForFunction(
    () => {
      const state = document
        .querySelector('[data-testid="table-baseline"]')
        ?.getAttribute("data-baseline");
      return state === "ready" || state === "unavailable";
    },
    undefined,
    { timeout: 30000 },
  );
}

/** 一个元素的文字，没有就是 `null`。 */
function text(page, testId) {
  return page.evaluate(
    (testId) => document.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? null,
    testId,
  );
}

/**
 * 开一页并**从头数它发出去的每一条请求**。
 *
 * 计数挂在 `context` 上而不是 `page` 上：`page.on("request")` 漏不掉子资源，
 * 但换了页（点「去牌桌」）之后还想接着数——那就得挂在 context 上。
 */
async function openCounted(lane, url, { seating, blockAsset = false } = {}) {
  const context = await lane.newContext();
  const asked = [];
  context.on("request", (request) => {
    if (request.url().includes(ASSET)) asked.push(request.url());
  });

  const page = await context.newPage();
  if (seating !== undefined) await plantSeating(page, seating);
  if (blockAsset) {
    // **「把资产地址改坏」**（票面原话）在无头里最干净的一种做法：让那一次请求真的失败。
    // 它与「站点上没有这份产物」是同一个下场（页面明说原因 + 那一席退回 bot），
    // 但它**在有没有产物两种环境下都成立**，因此这一趟不看运气。
    await context.route(`**/${ASSET}`, (route) => route.abort("failed"));
  }
  await page.goto(url, { waitUntil: "load" });
  return { context, page, asked };
}

/** 四家均匀随机（不选强 AI 基线的那一桌）。 */
const plainSeats = { profiles: [], seats: [{}, {}, {}, {}] };

/** 强 AI 基线坐第 `index` 席、其余三家「有主见」。 */
const baselineSeats = (index) => ({
  profiles: [],
  seats: [0, 1, 2, 3].map((seat) => ({ choice: seat === index ? "baseline" : "opinionated" })),
});

/** ① 懒加载：不选它的那两页一个字节都不拉；选了它就恰好多出一条请求。 */
async function lazyLoading(lane, url, missing) {
  // 首页：自动播一份 Demo 回放，最该不拉的就是它。
  const home = await openCounted(lane, `${url}/`);
  try {
    await home.page.waitForTimeout(600);
    if (home.asked.length !== 0) {
      missing.push(`首页拉了那份资产 ${home.asked.length} 次：${home.asked[0]}`);
    }
  } finally {
    await home.context.close();
  }

  // 普通对局（四家均匀随机）：真走几十手，同样一个字节都不许拉。
  const plain = await openCounted(lane, hostPage(url), { seating: plainSeats });
  try {
    const walked = await stepTurns(plain.page, { limit: 30 });
    if (walked.walked < 10) missing.push(`普通对局那一页只走了 ${walked.walked} 手，量不出什么`);
    if (plain.asked.length !== 0) {
      missing.push(`不选强 AI 基线的对局拉了那份资产 ${plain.asked.length} 次：${plain.asked[0]}`);
    }
    // 那一行状态线在这一页上根本不该存在（`BaselineStatus.Absent` 一行都不画）。
    const line = await attr(plain.page, "table-baseline", "data-baseline");
    if (line !== null) missing.push(`不选它的那一页却有强 AI 基线那一行（data-baseline=${line}）`);
  } finally {
    await plain.context.close();
  }

  // **阳性对照**：拨上它，那一条请求必须真的发出去（不然上面两条零就毫无意义）。
  const picked = await openCounted(lane, hostPage(url), { seating: baselineSeats(1) });
  try {
    await picked.page.waitForTimeout(1500);
    if (picked.asked.length !== 1) {
      missing.push(`拨到强 AI 基线之后那份资产被请求了 ${picked.asked.length} 次，该正好 1 次`);
    }
  } finally {
    await picked.context.close();
  }
}

/** ② 降级：把那次请求掐掉，页面说得出原因，其余席照常打完一局。 */
async function degrades(lane, url, missing, budgetMs) {
  const { context, page } = await openCounted(lane, hostPage(url), {
    seating: baselineSeats(1),
    blockAsset: true,
  });

  try {
    await page.waitForFunction(
      () =>
        document.querySelector('[data-testid="table-baseline"]')?.getAttribute("data-baseline") ===
        "unavailable",
      undefined,
      { timeout: 15000 },
    );

    // **页面明说原因**：不是一句「出错了」，而是那一句带着地址与失法的中文。
    const said = (await text(page, "table-baseline")) ?? "";
    if (!said.includes("强 AI 基线用不了")) missing.push(`那一行没说它用不了：「${said}」`);
    if (!said.includes(ASSET)) missing.push(`那一行没说是哪份资产拉不动：「${said}」`);
    if (!said.includes("退回")) missing.push(`那一行没说那一席现在是谁在打：「${said}」`);

    // **牌谱里那一列跟着说实话**：真正把这几手打出来的是自带 bot，不是强 AI 基线。
    const seats = await attr(page, "table-agent", "data-seats");
    if (seats !== "opinionated,opinionated,opinionated,opinionated") {
      missing.push(`退回之后 data-seats 该四家都是 opinionated，却是「${seats}」`);
    }

    // **其余席照常打完一局**：这一趟真走到这一局终了（而不是断言一个标志位）。
    const walked = await stepTurns(page, { limit: 400, budgetMs });
    if (!walked.closed) missing.push(`资产拉不动之后这一局没打完（走了 ${walked.walked} 手）`);
    if (walked.stuckAt !== null) missing.push(`第 ${walked.stuckAt} 手卡住了`);
    if ((await text(page, "table-fault")) !== null) {
      missing.push(`引擎拒了一手：${await text(page, "table-fault")}`);
    }
  } finally {
    await context.close();
  }
}

/** 那一席的思考气泡（没有就是 `null`）。 */
function bubbleAt(page, seat) {
  return page.evaluate(
    (seat) =>
      document.querySelector(`[data-testid="seat-${seat}-bubble"]`)?.getAttribute("data-bubble") ??
      null,
    seat,
  );
}

/**
 * ③ 它不会说话：那一席没有气泡、这一桌没有账单行；阳性对照是同一桌上的模型席。
 *
 * **这一趟故意不掐资产**：掐了的话那一席当场退回自带 bot，于是这一趟量到的
 * 「没有气泡」是**bot 席没有气泡**（`verify-bubbles` 早就钉着的那件事），
 * 而不是「强 AI 基线不会说话」。**破坏实验当场证过这一条**：
 * 把 `settleBaseline` 改成留一条决策记录（等于给它编了一句理由），
 * 掐着资产跑时这一趟一声不吭。
 *
 * 于是它分两档：站点上有那份产物时它真的在量那一席（红得起来），
 * 没有时它退成「降级那一路照旧不长气泡」——**返回值把走的是哪一档说出来**，
 * 调用方印在总结里，免得读日志的人以为 CI 里量到的是前一档。
 */
async function speechless(lane, url, missing, budgetMs, endpoint) {
  const seating = {
    profiles: [
      {
        name: "闸门的对手",
        provider: "custom-openai",
        model: "fake",
        base_url: endpoint,
        // key 里不放非 ASCII：它直接进 HTTP 头，非 ASCII 会让 `fetch` 当场抛。
        api_key: "sk-gate-only-not-a-real-key",
        timeout_ms: "20000",
      },
    ],
    seats: [
      { choice: "baseline" },
      { choice: profileChoice("闸门的对手") },
      { choice: "opinionated" },
      { choice: "opinionated" },
    ],
  };

  const { context, page } = await openCounted(lane, hostPage(url), { seating });
  const real = assetPresent();

  try {
    await settledBaseline(page);
    await stepTurns(page, { limit: 120, budgetMs });

    // 强 AI 基线那一席：**一个气泡都没有**。
    const mine = await bubbleAt(page, 0);
    if (mine !== null) missing.push(`强 AI 基线那一席长出了气泡（data-bubble=${mine}）`);

    // **阳性对照**：模型那一席有气泡、这一桌也有账单行——
    // 没有它，「0 个气泡」也可能只是这一桌根本没人开过口。
    const theirs = await bubbleAt(page, 1);
    if (theirs === null) missing.push("模型那一席一个气泡都没有：这一趟量的不是「它不说话」");

    const tokens = await attr(page, "table-usage", "data-prompt-tokens");
    if (tokens === null || Number.parseInt(tokens, 10) <= 0) {
      missing.push(`账单行该因为模型席而存在，却是「${tokens}」`);
    }

    // 账单里的那几个数**一个都不来自强 AI 基线**：它一条决策记录都不留，
    // 因此把模型那一席换成 bot 的话账单行会整条消失（那一条由 `verify-bubbles` 钉着）。
    const troubles = await attr(page, "table-baseline", "data-baseline-troubles");
    if (troubles === null) missing.push("强 AI 基线那一行不见了");
  } finally {
    await context.close();
  }

  return real;
}

/** ④ 与真人同桌：真人那一侧的结构性不泄露照旧绿。 */
async function withHuman(lane, url, missing) {
  const seating = {
    profiles: [],
    seats: [
      { choice: "human" },
      { choice: "baseline" },
      { choice: "opinionated" },
      { choice: "opinionated" },
    ],
  };

  const { context, page } = await openCounted(lane, hostPage(url), { seating });

  try {
    await settledBaseline(page);
    // 真人坐座位 0：有牌点得动就点，没有就单步（与 `verify-human` 同一种驱动）。
    const played = await page.evaluate(async (limit) => {
      const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
      const settle = () => new Promise((done) => setTimeout(done, 4));
      let moves = 0;

      for (let turn = 0; turn < limit; turn += 1) {
        const playable = document.querySelector("[data-dahai-id]");
        const call = document.querySelector("[data-human-action-id]");
        if (playable !== null) {
          playable.click();
          moves += 1;
        } else if (call !== null) {
          call.click();
          moves += 1;
        } else {
          const step = at("table-step");
          if (step === null || step.disabled) break;
          step.click();
        }
        await settle();
      }
      return moves;
    }, 240);

    if (played < 5) missing.push(`真人只出手了 ${played} 次，量不出什么`);

    // **结构性不泄露**（story 29）：整页 HTML 里每一个 `data-pai` 都要落在
    // 「自家手牌 + 四家的河 + 四家的副露 + 宝牌指示牌」那份预算里。
    // 这里用最硬的那一半：**他家三席的手牌行里一个 `data-pai` 都不许有**。
    const leaked = await page.evaluate(() =>
      [1, 2, 3].flatMap((seat) => {
        const hand = document.querySelector(`[data-testid="table-seat-${seat}-hand"]`);
        if (hand === null) return [];
        const tiles = hand.querySelectorAll("[data-pai]");
        return tiles.length === 0 ? [] : [`座位 ${seat} 的手牌行里有 ${tiles.length} 个 data-pai`];
      }),
    );
    missing.push(...leaked);

    // 强 AI 基线那一席在这一桌上同样不长气泡（真人在座时四席本来就都不长，
    // 这一条因此是「与真人同桌也不出岔子」而不是一条独立的可见性断言）。
    const mine = await bubbleAt(page, 1);
    if (mine !== null) missing.push(`与真人同桌时强 AI 基线那一席长出了气泡（${mine}）`);
  } finally {
    await context.close();
  }
}

/** ⑤ 本机演习：资产在场，它真打一局，牌谱里认得出它。 */
async function playsForReal(lane, url, missing, budgetMs) {
  const { context, page, asked } = await openCounted(lane, hostPage(url), {
    seating: baselineSeats(0),
  });

  try {
    await page.waitForFunction(
      () =>
        document.querySelector('[data-testid="table-baseline"]')?.getAttribute("data-baseline") ===
        "ready",
      undefined,
      { timeout: 60000 },
    );

    if (asked.length !== 1) missing.push(`那份资产被请求了 ${asked.length} 次，该正好 1 次`);

    const bytes = await attr(page, "table-baseline", "data-baseline-bytes");
    console.log(`那份产物 ${bytes} 字节；页面上写的是：${await text(page, "table-baseline")}`);

    const walked = await stepTurns(page, { limit: 400, budgetMs });
    if (!walked.closed) missing.push(`它坐一席时这一局没打完（走了 ${walked.walked} 手）`);
    if ((await text(page, "table-fault")) !== null) {
      missing.push(`引擎拒了一手：${await text(page, "table-fault")}`);
    }

    // **牌谱里认得出它**：`start_game` 的那一列 `names`。
    const seats = await attr(page, "table-agent", "data-seats");
    if (seats !== "baseline,opinionated,opinionated,opinionated") {
      missing.push(`牌谱里那一列该是 baseline 打头，却是「${seats}」`);
    }

    // **兜底一手都不该有**：它交不出来的每一手都记在那一格上。
    const troubles = await attr(page, "table-baseline", "data-baseline-troubles");
    if (troubles !== "0") missing.push(`它有 ${troubles} 手交不出来（该是 0）`);

    // 它仍旧不说话。
    const mine = await bubbleAt(page, 0);
    if (mine !== null) missing.push(`它真出手之后长出了气泡（${mine}）`);
    if ((await attr(page, "table-usage", "data-prompt-tokens")) !== null) {
      missing.push("它真出手之后这一桌长出了 token 账单行");
    }
  } finally {
    await context.close();
  }
}

export async function verifyBaseline(lane, { budgetMs = 60000, asset = false } = {}) {
  const url = await lane.previewUrl();
  const missing = [];

  await lazyLoading(lane, url, missing);
  await degrades(lane, url, missing, budgetMs);

  const { baseUrl, endpoint } = await startEndpoint(url);
  let spoke = false;
  try {
    spoke = await speechless(lane, url, missing, budgetMs, baseUrl);
  } finally {
    endpoint.kill();
  }

  await withHuman(lane, url, missing);

  if (asset) {
    if (!assetPresent()) {
      return failure("--asset 说资产在场，但 web/public/ 里没有它：", [
        `找不到 web/public/${ASSET}——造一份的做法见 web/public/baseline/README.md`,
      ]);
    }
    await playsForReal(lane, url, missing, budgetMs);
  }

  if (missing.length > 0) return failure("强 AI 基线这一道没过：", missing);

  console.log("");
  console.log("首页与不选它的对局：那份资产的网络请求计数为 0（阳性对照：拨上它就恰好 1 次）✓");
  console.log("资产拉不动：页面明说原因、那一席退回自带 bot，其余席照常打完一局 ✓");
  console.log(
    spoke
      ? "它真出手了一整段，那一席仍旧没有气泡、没有账单行（阳性对照：同桌的模型席两样都有）✓"
      : "（那一席这一趟是降级态的自带 bot：这一趟量的不是「它不会说话」——那一条在 dotnet 侧的 BaselineSeatTests 里）",
  );
  console.log("与真人同桌：他家三席的手牌行里一个 data-pai 都没有，那一席也不长气泡 ✓");
  if (asset) console.log("本机演习：它真坐一席打完一局，牌谱里认得出它，一手都没兜底 ✓");
  else if (assetPresent())
    console.log("（这一趟站点上有那份产物，但没跑真打一局那一趟——加 --asset）");
  else
    console.log("（这一趟站点上没有那份产物，因此第 ②③④ 趟走的都是降级那一路；真推理见 --asset）");
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };

  await runStandalone((lane) =>
    verifyBaseline(lane, {
      budgetMs: Number.parseInt(flag("--budget", "60000"), 10),
      asset: argv.includes("--asset"),
    }),
  );
}
