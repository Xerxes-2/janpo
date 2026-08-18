// **思考气泡**那道闸门（票 76）：CONTEXT.md 的 `Thinking Bubble` —— 展示某个 `DecisionRecord`
// 的 UI 部件。dotnet 那一侧（`ThinkingBubbleTests`）钉的是取值器与全文面板的判据，
// **这一道钉的是页面上真看得见的那几件事**：
//
//   ① 气泡里的字**来自那一手的决策记录**：端点回的那句 `reason` 一字不差地出现在气泡里，
//      而 `data-bubble-turn` 与牌桌上那一手对得上（**换个端点、换句话，气泡跟着变**，见 ④）；
//   ② **bot 席上没有气泡**：只有模型那一席有（今天一桌只坐得下一席模型，票 74 才四席）；
//   ③ **气泡挡不住牌与河**：气泡的矩形与四家的三排牌、牌桌中央**一律不相交**
//      —— 它是座位面板里的一行、不做绝对定位，这一条就是那句话的执行体（票 44 的八项因此不受影响）；
//   ④ **兜底那一态**：把 baseUrl 换到一个只会回越界 id 的端点，下一手的气泡变成「兜底」，
//      写的原因与牌桌上那句「上一手：……（兜底：……）」、`data-fallback` **同源**；
//   ⑤ **点得开**：全文面板给出 thinking / 理由 / prompt 尾部 / 动作 id 集 / 最终落定的动作 /
//      延迟 / 问了几次 / Usage / 渲染版本，牌桌跟着摆出**那一手落定那一刻的快照**，
//      收起来之后逐字回到现在（**只读**：这一桌一手都没退回去）。
//
// **全程本机**：页面是本地 dev server，两个端点是本地假端点（`fake-endpoint.mjs`），
// **一个字节都不出网**，因此它进 CI。它也是 `verify-browser.mjs` 里的一趟。
//
//   cd web && pnpm run fable && node scripts/verify-bubbles.mjs
//
// 选项：--seat N（默认 0，庄家，第一手就轮到它）、--budget ms、--shoot <目录>（截图给报告用）。

import { spawn } from "node:child_process";
import { mkdirSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { hostPage } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/**
 * 两个端点各自回的那句 `reason`。**只可能从端点那儿来**：页面里没有任何一处写着它们，
 * 因此它出现在气泡里就证明了那条链路（端点 → 决策记录 → 气泡）真的通着。
 */
const SAID = "假端点甲说：这一手照它的算法只能这么打";

/** 借内核要一个空闲端口（跑批是并行的，写死端口迟早撞上另一个工作区）。 */
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

/** 起一个假端点，返回它的 baseUrl 与那个进程。 */
async function startEndpoint(origin, extra) {
  const port = await freePort();
  const endpoint = spawn(
    "node",
    ["scripts/fake-endpoint.mjs", "--port", String(port), "--cors", origin, "--quiet", ...extra],
    { cwd: webRoot, stdio: ["ignore", "ignore", "inherit"] },
  );
  return { baseUrl: `http://127.0.0.1:${port}/v1`, endpoint };
}

/**
 * 等页面安静下来。**超时不扔异常而是返回 false**：这一道闸门的契约是交一份失败清单
 * （合并跑的那个入口要先关浏览器、再逐道汇报），在 try 里抛会把十二趟一起搞挂
 * ——`verify-home` 早就写下过这一课。
 */
async function settles(page, predicate, argument, timeout) {
  try {
    await page.waitForFunction(predicate, argument, { timeout });
    return true;
  } catch (_error) {
    return false;
  }
}

/**
 * 播 / 停。**灰着就不点**：改坏之后这一桌可能一路打到局终（那时「播放」是灰的），
 * 而 playwright 会在灰按钮上干等 30 秒再抛——抛出去这一趟就成了一个堆栈，
 * 而不是一份读得懂的失败清单。
 */
async function playPause(page, wanted) {
  const play = page.getByTestId("table-play");
  if (await play.isDisabled()) return false;
  if ((await play.textContent()).trim() !== wanted) return false;
  await play.click();
  return true;
}

/** 四家的河合计（这一屏走到第几手的粗读数）。 */
async function kawaTotal(page) {
  return await page.evaluate(() =>
    [0, 1, 2, 3]
      .map((index) =>
        Number.parseInt(
          document
            .querySelector(`[data-testid="seat-${index}-kawa"]`)
            ?.getAttribute("data-kawa-count") ?? "0",
          10,
        ),
      )
      .reduce((sum, each) => sum + each, 0),
  );
}

/** 牌桌此刻画出来的那一份摘要（四家的张数、牌面与那句「上一手」）。 */
async function boardDigest(page) {
  return await page.evaluate(() =>
    [0, 1, 2, 3]
      .map((index) => {
        const hand = document.querySelector(`[data-testid="seat-${index}-hand"]`);
        const kawa = document.querySelector(`[data-testid="seat-${index}-kawa"]`);
        return [hand?.textContent, kawa?.textContent].join("|");
      })
      .concat([document.querySelector('[data-testid="table-latest"]')?.textContent ?? ""])
      .join("\n"),
  );
}

/** 一席的气泡此刻是什么（没有就是 null）。 */
async function bubbleAt(page, index) {
  return await page.evaluate((seat) => {
    const node = document.querySelector(`[data-testid="seat-${seat}-bubble"]`);
    if (node === null) return null;
    return {
      state: node.getAttribute("data-bubble"),
      turn: node.getAttribute("data-bubble-turn"),
      text: node.textContent.trim(),
      disabled: node.disabled === true,
    };
  }, index);
}

/**
 * 气泡与牌 / 河 / 副露 / 牌桌中央的矩形有没有相交（票面：**气泡不许挡住牌与河**）。
 *
 * **读的是真坐标**，不是「我们没写 position: absolute」——后者是承诺，前者是事实。
 */
async function overlaps(page) {
  return await page.evaluate(() => {
    const hit = (a, b) =>
      a.left < b.right - 0.5 &&
      b.left < a.right - 0.5 &&
      a.top < b.bottom - 0.5 &&
      b.top < a.bottom - 0.5;
    const found = [];

    for (const bubble of document.querySelectorAll('[data-testid$="-bubble"]')) {
      const box = bubble.getBoundingClientRect();
      if (box.width === 0 || box.height === 0) {
        found.push(`${bubble.dataset.testid} 的矩形是空的：它其实没画出来`);
        continue;
      }
      // **气泡是那一席的**：它得画在那一席的框里。没有这一条的话，一个飞到页面角上的气泡
      // 与谁都不相交，下面那一圈照样绿（`position: absolute` 那种改坏法实测就是这样）。
      const seat = bubble.closest(".seat")?.getBoundingClientRect();
      if (
        seat === undefined ||
        box.left < seat.left - 0.5 ||
        box.right > seat.right + 0.5 ||
        box.top < seat.top - 0.5 ||
        box.bottom > seat.bottom + 0.5
      ) {
        found.push(`${bubble.dataset.testid} 画到那一席的框外面去了`);
      }
      const targets = [
        ...document.querySelectorAll(".tiles"),
        ...document.querySelectorAll('[data-testid="table-center"]'),
      ];
      for (const target of targets) {
        if (bubble.contains(target) || target.contains(bubble)) continue;
        if (hit(box, target.getBoundingClientRect())) {
          found.push(
            `${bubble.dataset.testid} 压在 ${target.dataset.testid ?? target.className} 上了`,
          );
        }
      }
    }
    return found;
  });
}

/** 全文面板此刻那几格。 */
async function detailOf(page) {
  return await page.evaluate(() => {
    const panel = document.querySelector('[data-testid="table-bubble-detail"]');
    if (panel === null) return null;
    const at = (testId) =>
      document.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? null;
    return {
      turn: panel.getAttribute("data-bubble-turn"),
      seat: panel.getAttribute("data-bubble-seat"),
      head: at("bubble-at"),
      applied: at("bubble-applied"),
      fallback: at("bubble-fallback"),
      reason: at("bubble-reason"),
      thinking: at("bubble-thinking"),
      prompt: at("bubble-prompt"),
      actions: at("bubble-actions"),
      meta: at("bubble-meta"),
      version: at("bubble-version"),
    };
  });
}

/** 思考气泡那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyBubbles(lane, options = {}) {
  const { seat = 0, budgetMs = 120000, shoot = null } = options;

  // dev server 而不是 preview：与 verify-redaction 同一个理由（省掉一次 vite build）。
  const pageOrigin = await lane.devUrl();
  const good = await startEndpoint(pageOrigin, ["--reason", SAID]);
  // 越界的 id：Agent 层校完重试两次，最后交不出来 → 兜底代打（票 23 的那条路）。
  const bad = await startEndpoint(pageOrigin, ["--action-id", "9999"]);

  const context = await lane.newContext();
  const problems = [];
  const missing = [];

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    // 座位配置只从 localStorage 来（`Store.readSeatConfig`）：一席自定义端点，**不带 key**
    // （本地端点本来就不校验，而这道闸门一把真 key 都不该碰得着）。
    await page.addInitScript(
      ([seat, baseUrl]) => {
        localStorage.setItem("janpo.llm.seat", String(seat));
        localStorage.setItem("janpo.llm.provider", "custom-openai");
        localStorage.setItem("janpo.llm.model", "fake-model");
        localStorage.setItem("janpo.llm.base_url", baseUrl);
        localStorage.setItem("janpo.llm.api_key", "");
        localStorage.setItem("janpo.llm.timeout_ms", "10000");
        localStorage.setItem("janpo.llm.thinking", "off");
        localStorage.setItem("janpo.llm.tier", "bare");
      },
      [seat, good.baseUrl],
    );

    console.log(`页面 ${pageOrigin}　模型坐席 ${seat}`);
    console.log(`端点甲（正常答话）${good.baseUrl}：reason = 「${SAID}」`);
    console.log(`端点乙（越界 id）　${bad.baseUrl}：action_id = 9999 → 兜底`);
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });

    // ---- ① 说了什么 ----
    await page.getByTestId("table-speed-8×").click();
    await playPause(page, "播放");
    const spokeUp = await settles(
      page,
      (seat) =>
        document.querySelector(`[data-testid="seat-${seat}-bubble"]`)?.dataset.bubble === "spoke",
      seat,
      budgetMs,
    );
    // 让它再走几手（同一席会说好几次，气泡上该是**最新**那一条）。
    await page.waitForTimeout(1500);
    await playPause(page, "暂停");
    await settles(
      page,
      () => document.querySelector('[data-testid="table-play"]')?.textContent?.trim() === "播放",
      null,
      budgetMs,
    );

    const spoke = await bubbleAt(page, seat);
    console.log("");
    console.log(`座位 ${seat} 的气泡（${spoke?.state}）：${spoke?.text}`);

    if (!spokeUp || spoke === null) {
      // **这一趟到此为止**：底下每一条都要先有一个气泡（判据 3：一条执行不到的断言等于没有）。
      const said = (await page.getByTestId("table-agent").textContent()).trim();
      return failure("思考气泡这一道没过：", [
        `模型那一席（座位 ${seat}）上一直没有出气泡，底下那几条因此一条都没验到`,
        `页面上 Agent 那一行说的是：${said}`,
      ]);
    }
    if (!spoke.text.includes(SAID)) {
      missing.push(`气泡里的字不是那一手记录里的那句：看到的是「${spoke.text}」`);
    }
    if (!/^\d+$/.test(spoke.turn ?? "")) {
      missing.push(`气泡上没写它是第几手（data-bubble-turn=「${spoke.turn}」）`);
    }
    if (spoke.disabled) missing.push("说过话的气泡该点得开，它却是灰的");

    // ---- ② bot 席上没有气泡 ----
    for (const other of [0, 1, 2, 3].filter((index) => index !== seat)) {
      const bubble = await bubbleAt(page, other);
      if (bubble !== null) {
        missing.push(`座位 ${other} 是自带 bot，不该有气泡（看到的是「${bubble.text}」）`);
      }
    }

    // ---- ③ 挡不住牌与河 ----
    const covered = await overlaps(page);
    for (const each of covered) missing.push(each);

    // ---- ⑤ 点得开：全文面板 + 那一手的局面快照 ----
    const beforeOpen = await boardDigest(page);
    const liveKawa = await kawaTotal(page);
    await page.getByTestId(`seat-${seat}-bubble`).click();
    await settles(
      page,
      () => document.querySelector('[data-testid="table-bubble-detail"]') !== null,
      null,
      5000,
    );
    const detail = await detailOf(page);
    const snapshotKawa = await kawaTotal(page);

    console.log("");
    console.log(`点开之后：${detail?.head}`);
    console.log(`  最终落定：${detail?.applied}`);
    console.log(`  一句话理由：${detail?.reason}`);
    console.log(`  动作 id 集：${detail?.actions}`);
    console.log(`  这一次问话：${detail?.meta}`);
    console.log(`  渲染版本：${detail?.version}`);
    console.log(`  牌桌跟着回到那一刻：四家的河 ${liveKawa} → ${snapshotKawa} 张`);

    if (detail === null) {
      missing.push("点了气泡却没摊开全文面板");
    } else {
      if (detail.turn !== spoke?.turn) {
        missing.push(`摊开的是第 ${detail.turn} 手，气泡上写的却是第 ${spoke?.turn} 手`);
      }
      if (String(detail.seat) !== String(seat)) {
        missing.push(`摊开的那一手写着座位 ${detail.seat}，该是座位 ${seat}`);
      }
      // 九样：一样都不许是空的（缺哪一样在这里各报各的话）。
      const fields = [
        ["最终落定的动作", detail.applied],
        ["一句话理由", detail.reason],
        ["thinking 全文", detail.thinking],
        ["prompt 尾部", detail.prompt],
        ["动作 id 集", detail.actions],
        ["延迟 / 问了几次 / Usage", detail.meta],
        ["渲染版本", detail.version],
        ["兜底那一格", detail.fallback],
      ];
      for (const [name, value] of fields) {
        if (value === null || value === "") missing.push(`全文面板里「${name}」那一格是空的`);
      }
      if (!(detail.reason ?? "").includes(SAID)) {
        missing.push(`全文面板里的理由不是端点回的那句：「${detail.reason}」`);
      }
      if (!(detail.prompt ?? "").includes("【现在】")) {
        missing.push(
          `全文面板里的 prompt 尾部不像是 prompt 尾部：「${(detail.prompt ?? "").slice(0, 60)}」`,
        );
      }
      if (!/延迟 \d+ ms・问了 \d+ 次/.test(detail.meta ?? "")) {
        missing.push(`全文面板里没写清延迟与问了几次：「${detail.meta}」`);
      }
      if (snapshotKawa > liveKawa) {
        missing.push(
          `点开之后牌桌上的河反而多了（${liveKawa} → ${snapshotKawa}）：那不是当时的快照`,
        );
      }
    }

    if (shoot !== null) {
      mkdirSync(shoot, { recursive: true });
      await page.screenshot({ path: resolve(shoot, "bubbles-detail.png"), fullPage: true });
    }

    // 收起来：**逐字回到现在**（只读，这一桌一手都没退回去）。
    if (detail !== null) {
      await page.getByTestId("bubble-close").click();
      await settles(
        page,
        () => document.querySelector('[data-testid="table-bubble-detail"]') === null,
        null,
        5000,
      );
    }
    const afterClose = await boardDigest(page);
    if (afterClose !== beforeOpen) {
      missing.push(
        `点开又收起之后牌桌不一样了（这一桌该一手都没退回去）：\n${beforeOpen}\n——\n${afterClose}`,
      );
    }

    // ---- ④ 兜底那一态：换个端点，下一手交不出来 ----
    await page.getByTestId("table-llm-base-url").fill(bad.baseUrl);
    await playPause(page, "播放");
    const troubledUp = await settles(
      page,
      (seat) =>
        document.querySelector(`[data-testid="seat-${seat}-bubble"]`)?.dataset.bubble ===
        "troubled",
      seat,
      budgetMs,
    );
    await playPause(page, "暂停");
    if (!troubledUp) {
      missing.push(
        `换成那个交不出来的端点之后，座位 ${seat} 的气泡一直没变成「兜底」态（等了 ${budgetMs} ms）`,
      );
    }

    const troubled = await bubbleAt(page, seat);
    const latest = (await page.getByTestId("table-latest").textContent()).trim();
    const fallenBack = await page.getByTestId("table-latest").getAttribute("data-fallback");

    console.log("");
    console.log(`换成端点乙之后，座位 ${seat} 的气泡（${troubled?.state}）：${troubled?.text}`);
    console.log(`牌桌上那句：${latest}`);

    if (troubled === null || troubled.state !== "troubled") {
      missing.push(`换了个交不出来的端点，气泡却还是「${troubled?.state}」态`);
    } else {
      if (troubled.text.includes(SAID)) {
        missing.push("兜底那一手的气泡上还写着上一次的理由：它没跟着那一手的记录换");
      }
      // 与牌桌上那句「上一手：……（兜底：……）」同源：两边读的是同一条记录的同一格。
      if (!latest.includes("兜底") || fallenBack !== "true") {
        missing.push(
          `气泡说这一手是兜底的，牌桌上那句却没说（data-fallback=${fallenBack}）：「${latest}」`,
        );
      }
      const why = troubled.text.replace(/^兜底/, "").trim();
      if (why === "" || !latest.includes(why)) {
        missing.push(`气泡上的兜底原因与牌桌上那句对不上：气泡「${why}」／牌桌「${latest}」`);
      }
    }

    const coveredAgain = await overlaps(page);
    for (const each of coveredAgain) missing.push(`（兜底那一屏）${each}`);

    if (shoot !== null) {
      await page.screenshot({ path: resolve(shoot, "bubbles-troubled.png"), fullPage: true });
      console.log(`截图写在 ${shoot}`);
    }
  } finally {
    await context.close();
    good.endpoint.kill();
    bad.endpoint.kill();
  }

  if (problems.length > 0) return failure("页面报了错：", problems);
  if (missing.length > 0) return failure("思考气泡这一道没过：", missing);

  console.log("");
  console.log("气泡里的字来自那一手的决策记录、bot 席上没有气泡、气泡挡不住牌与河 ✓");
  console.log("点得开：全文面板九样都在，牌桌跟着摆出那一手的快照，收起来逐字回到现在 ✓");
  console.log("兜底那一手：气泡是「兜底」态，原因与牌桌上那句同源 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };

  await runStandalone((lane) =>
    verifyBubbles(lane, {
      seat: Number.parseInt(flag("--seat", "0"), 10),
      budgetMs: Number.parseInt(flag("--budget", "120000"), 10),
      shoot: flag("--shoot", null),
    }),
  );
}
