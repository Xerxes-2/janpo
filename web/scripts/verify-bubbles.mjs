// **思考气泡 + 同时问多席**那道闸门（票 76 立、票 74 扩成四席）：CONTEXT.md 的 `Thinking Bubble`
// —— 展示某个 `DecisionRecord` 的 UI 部件。dotnet 那一侧（`ThinkingBubbleTests` /
// `TablePageTests`）钉的是取值器、按座位的等待与回执错位的判据，
// **这一道钉的是页面上真看得见的那几件事**：
//
// 第一程（四席各配一个假端点，票 76 交代的形态）：
//   ① **四个气泡都在，各说各的**：四个端点各回一句只可能从它那儿来的话，四席的气泡
//      各自写着**自己那个端点**的话、不串线（端点 → 决策记录 → 气泡按座位各走各的）；
//   ② **气泡挡不住牌与河**：矩形一律不相交（读的是 `getBoundingClientRect`，不是承诺）；
//   ③ **点得开**：全文面板九样、快照、收起来逐字回到现在（与票 76 逐字同一套断言）；
//   ④ **一席坏了不拖累别席**：把座位 0 的端点换成只回越界 id 的，它变「兜底」态，
//      而其余三席照样一手一手往前说（气泡上的手序还在涨、没有一席跟着变红）；
//   ⑨ **视角是一道信息闸门**（票 81）：坐到座位 N 上，**DOM 上只剩那一席的气泡**，
//      Agent 那条状态线也只提那一席（别席那句「座位 N 的模型选完了：……」连理由一起不在）；
//      切回上帝四家都回来、四句话互不相同（**阳性对照**：不是全藏了，也不是四个同一句的空壳）。
//
// **两程都先切到上帝视角**（票 81 之后）：`?table=1` 默认坐在座位 0 上，
// 而这一道量的全是「四席各自怎么样」——不切的话它量的是闸门自己。
//
// 第二程（并发的墙钟，票 74 的硬证据）：三席共用一个**固定延迟**的假端点、座位 3 仍是 bot，
//   种子 48（第一局早早就有一轮多席响应，dotnet 侧探针挑的）。MutationObserver 记下
//   「几个『在想』气泡同时在场」的变迁：
//   ⑤ 真出现过 **≥2 席同时在想**（串行形态下这一幕根本不存在）；
//   ⑥ 那一轮的墙钟**接近一份延迟而不是几份**（两个数抄进报告）；
//   ⑦ 「在想」的气泡带着**已等秒数与上限**（`data-waited` / `data-wait-limit`，72-3 的代价）；
//   ⑧ bot 那一席（座位 3）从头到尾一个气泡都没有。
//
// **全程本机**：页面是本地 dev server，端点全是本地假端点（`fake-endpoint.mjs`），
// **一个字节都不出网**，因此它进 CI。它也是 `verify-browser.mjs` 里的一趟。
//
//   cd web && pnpm run fable && node scripts/verify-bubbles.mjs
//
// 选项：--budget ms、--delay ms（第二程端点睡多久，默认 700）、--shoot <目录>（截图给报告用）。

import { spawn } from "node:child_process";
import { mkdirSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { plantSeating, profileChoice } from "./seating.mjs";
import { hostPage } from "./serve.mjs";

/** 四个座位。 */
const Seats = [0, 1, 2, 3];

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/**
 * 四个端点各自回的那句 `reason`。**只可能从各自的端点那儿来**：页面里没有任何一处写着它们，
 * 因此「座位 N 的气泡里是第 N 句」就证明了那条链路（端点 → 决策记录 → 气泡）**按座位各走各的**
 * ——串了线（甲席的回执落进乙席）这里当场看得见。
 */
const SAID = Seats.map(
  (seat) => `假端点${"甲乙丙丁"[seat]}说：座位 ${seat} 这一手照它的算法只能这么打`,
);

/**
 * 座位 0 那一句**故意加长到超过气泡的上限**（票 81）：真语料里 13% 的理由比它还长
 * （票 79 那份 Demo：中位 48 字、最长 260），而票 76 那版把它们无声地剪掉了。
 *
 * **挑座位 0** 是为了与 ③（点开那一屏）碰到一起：同一手上一眼就比得出
 * **气泡里是截过的一句、面板里是完整那一句**。
 * **长句以 `SAID[0]` 开头**：截到上限之后那一截标记还在，因此上面那几条
 * 「座位 N 的气泡里是它自己端点回的那句」一条都不必放松。
 */
const LONG_SEAT = 0;
const LONG_TAIL =
  "；本来还想说一说这一手的安全度与巋目，可惜一个气泡里只放得下一句话，" +
  "剩下的得点开全文面板才看得到，这一句就是专门拉长了来验那一枚招子的。";

/** 四个端点真正回的那句（座位 1 那句带长尾巴）。 */
const REASONS = Seats.map((seat) => (seat === LONG_SEAT ? SAID[seat] + LONG_TAIL : SAID[seat]));

/**
 * 两程共用的种子：dotnet 侧探针挑的——**第一局第 2 手就有一轮多席响应**
 * （四席全模型、或三席模型 + 座位 3 bot，两种坐法都成立）。
 * 第一程也要它：「回执不串线」这条断言得在真出现过多席同问的一局上验，不然它在空转（判据 3）。
 */
const CONCURRENT_SEED = 48;

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
 * （合并跑的那个入口要先关浏览器、再逐道汇报），在 try 里抛会把同一条跑道上其余那几趟一起搞挂
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
      // 「正在看第 N 手」那一句（票 86）：人看得见自己被搬到了哪一手。
      viewing: at("bubble-viewing"),
      origin:
        document
          .querySelector('[data-testid="bubble-viewing"]')
          ?.getAttribute("data-bubble-origin") ?? null,
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

/** 第一程：四席各配一个假端点。返回失败清单（空 = 绿）。 */
async function fourSeatsLane(lane, pageOrigin, options) {
  const { budgetMs, shoot, missing, problems } = options;

  const endpoints = [];
  for (const seat of Seats) {
    endpoints.push(await startEndpoint(pageOrigin, ["--reason", REASONS[seat]]));
  }
  // 越界的 id：Agent 层校完重试两次，最后交不出来 → 兜底代打（票 23 的那条路）。
  const bad = await startEndpoint(pageOrigin, ["--action-id", "9999"]);

  const context = await lane.newContext();

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    // 坐法只从 localStorage 来（票 73 之后是**档案库 + 座位绑定**）：四份自定义端点的档案，
    // **不带 key**（本地端点本来就不校验，而这道闸门一把真 key 都不该碰得着），四席各引各的。
    await plantSeating(page, {
      profiles: Seats.map((seat) => ({
        name: `气泡闸门的档案（座位 ${seat}）`,
        provider: "custom-openai",
        model: `fake-model-${seat}`,
        base_url: endpoints[seat].baseUrl,
        timeout_ms: "10000",
      })),
      seats: Seats.map((seat) => ({ choice: profileChoice(`气泡闸门的档案（座位 ${seat}）`) })),
    });

    console.log(`页面 ${pageOrigin}　四席各配一个端点：`);
    for (const seat of Seats) {
      console.log(
        `  座位 ${seat} ← ${endpoints[seat].baseUrl}：reason = 「${REASONS[seat]}」（${REASONS[seat].length} 字）`,
      );
    }
    console.log(`端点（越界 id）${bad.baseUrl}：action_id = 9999 → 兜底`);
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });

    // 换成探针挑好的种子（第一局第 2 手就有一轮多席同问），重开一桌。
    await page.getByTestId("table-seed").fill(String(CONCURRENT_SEED));
    await page.getByTestId("table-restart").click();
    // 上帝视角（票 81）：这一程量的是四席各自的气泡，而 `?table=1` 默认坐在座位 0 上。
    await page.getByTestId("table-view-god").click();

    // **全程盯着**「有没有哪一席的气泡写过别席端点的话」：串线只发生在多席同问的那一轮里，
    // 而那一轮几十毫秒就落幕——只在暂停后抽查最新一条是抓不住它的（红-W2 的第一版就没抓住）。
    // 顺带数「最多几席同时在想」：它是「这一程真出现过多席同问」的执行证据（判据 3）。
    await page.evaluate((said) => {
      window.__crossed = [];
      window.__mostThinking = 0;
      const snap = () => {
        window.__mostThinking = Math.max(
          window.__mostThinking,
          document.querySelectorAll('[data-bubble="thinking"]').length,
        );
        for (let seat = 0; seat < 4; seat += 1) {
          const text =
            document.querySelector(`[data-testid="seat-${seat}-bubble"]`)?.textContent ?? "";
          for (let other = 0; other < 4; other += 1) {
            if (other !== seat && text.includes(said[other])) {
              const node = document.querySelector(`[data-testid="seat-${seat}-bubble"]`);
              window.__crossed.push(
                `座位 ${seat} 的气泡写过座位 ${other} 端点的话：「${text}」` +
                  `（t=${Math.round(performance.now())} state=${node?.dataset.bubble} turn=${node?.dataset.bubbleTurn} ` +
                  `detail=${document.querySelector('[data-testid="table-bubble-detail"]') !== null} ` +
                  `latest=${document.querySelector('[data-testid="table-latest"]')?.textContent}）`,
              );
            }
          }
        }
      };
      new MutationObserver(snap).observe(document.body, {
        subtree: true,
        childList: true,
        attributes: true,
      });
      snap();
    }, SAID);

    // ---- ① 四个气泡都在，各说各的 ----
    await page.getByTestId("table-speed-8×").click();
    await playPause(page, "播放");
    const allSpoke = await settles(
      page,
      () =>
        [0, 1, 2, 3].every(
          (seat) =>
            document.querySelector(`[data-testid="seat-${seat}-bubble"]`)?.dataset.bubble ===
            "spoke",
        ),
      null,
      budgetMs,
    );
    await playPause(page, "暂停");
    // 停下来之后把在飞的回执等落地（暂停只停定时器，不停已经问出去的话）。
    await settles(
      page,
      () => document.querySelectorAll('[data-bubble="thinking"]').length === 0,
      null,
      budgetMs,
    );

    if (!allSpoke) {
      // **这一趟到此为止**：底下每一条都要先有四个气泡（判据 3：执行不到的断言等于没有）。
      const said = (await page.getByTestId("table-agent").textContent()).trim();
      return failure("思考气泡这一道没过：", [
        "四席都交给了模型，却没等到四席各自的「说了什么」气泡",
        `页面上 Agent 那一行说的是：${said}`,
      ]);
    }

    console.log("");
    for (const seat of Seats) {
      const spoke = await bubbleAt(page, seat);
      console.log(`座位 ${seat} 的气泡（${spoke?.state}）：${spoke?.text}`);

      if (spoke === null) {
        missing.push(`座位 ${seat} 的气泡在暂停后不见了`);
        continue;
      }
      if (!spoke.text.includes(SAID[seat])) {
        missing.push(`座位 ${seat} 的气泡里不是它自己端点回的那句：看到的是「${spoke.text}」`);
      }
      for (const other of Seats.filter((each) => each !== seat)) {
        if (spoke.text.includes(SAID[other])) {
          missing.push(`座位 ${seat} 的气泡里写着座位 ${other} 那个端点的话：回执串线了`);
        }
      }
      if (!/^\d+$/.test(spoke.turn ?? "")) {
        missing.push(`座位 ${seat} 的气泡上没写它是第几手（data-bubble-turn=「${spoke.turn}」）`);
      }
      if (spoke.disabled) missing.push(`座位 ${seat} 说过话的气泡该点得开，它却是灰的`);
    }

    const watched = await page.evaluate(() => ({
      crossed: window.__crossed,
      mostThinking: window.__mostThinking,
    }));

    console.log(`这一程最多 ${watched.mostThinking} 席同时在想`);

    if (watched.mostThinking < 2) {
      missing.push(
        `这一程从没出现过 ≥2 席同时在想（最多 ${watched.mostThinking} 席）：「回执不串线」那条断言在空转`,
      );
    }
    for (const each of [...new Set(watched.crossed)].slice(0, 4)) missing.push(each);

    // ---- ⑩ 气泡只放一句话：长的截了并且**说一声**（票 81） ----
    // 两头都核：长那一席要有那枚招子且字真的少了，**短那几席一枚都不许有**
    // （后半句是阳性对照：招子恒显示的话，前半句什么都没证明）。
    const longBubble = await bubbleAt(page, LONG_SEAT);
    const longMore = await page.getByTestId(`seat-${LONG_SEAT}-bubble-more`).count();

    console.log("");
    console.log(
      `座位 ${LONG_SEAT} 的理由共 ${REASONS[LONG_SEAT].length} 字，气泡里写了 ${longBubble?.text.length} 字` +
        `（含「说」与招子），招子 ${longMore} 枚`,
    );

    if (longMore !== 1) {
      missing.push(
        `座位 ${LONG_SEAT} 的理由有 ${REASONS[LONG_SEAT].length} 字（远过气泡那一句话的量），` +
          `气泡上却没有「点开看全文」那枚招子（[data-testid="seat-${LONG_SEAT}-bubble-more"] 有 ${longMore} 枚）：` +
          "硬裁而不说正是票 81 要治的那条病（79 §8）",
      );
    }
    if ((longBubble?.text.length ?? 0) >= REASONS[LONG_SEAT].length) {
      missing.push(
        `座位 ${LONG_SEAT} 的气泡里把 ${REASONS[LONG_SEAT].length} 字的理由整句都写上了：那不是一句话`,
      );
    }
    if (!(longBubble?.text ?? "").includes("……")) {
      missing.push(`座位 ${LONG_SEAT} 的气泡截了却没有三点号收尾：「${longBubble?.text}」`);
    }
    for (const seat of Seats.filter((each) => each !== LONG_SEAT)) {
      const more = await page.getByTestId(`seat-${seat}-bubble-more`).count();
      if (more !== 0) {
        missing.push(
          `座位 ${seat} 的理由只有 ${REASONS[seat].length} 字，整句都写得下，气泡上却挂着 ${more} 枚` +
            "「点开看全文」：那枚招子说的是「这句话被截过」，恒显示就是句谎话",
        );
      }
    }

    // ---- ⑨ 视角是一道信息闸门（票 81） ----
    // 四席刚好各说过一句只可能从它自己端点来的话（上面刚核过），因此这一段量得到真东西：
    // 坐到座位 N 上，**别席的那句话在页面上应当一个字都找不到**——气泡里没有，
    // Agent 那条状态线里也没有（票 74 那句「座位 N 的模型选完了：……」连理由一起漏的就是它）。
    console.log("");
    for (const seat of Seats) {
      await page.getByTestId(`table-view-${seat}`).click();
      const only = await page.locator('[data-testid$="-bubble"]').count();
      const mine = await page.getByTestId(`seat-${seat}-bubble`).count();
      const line = ((await page.getByTestId("table-agent").textContent()) ?? "").trim();
      const hushed = await page.getByTestId("table-agent").getAttribute("data-hushed");

      if (only !== 1 || mine !== 1) {
        missing.push(
          `坐到座位 ${seat} 上，页面上还有 ${only} 个气泡（自家的 ${mine} 个）：` +
            "该只剩自家那一个（票 81：视角与手牌同一条规则）",
        );
      }
      if (!line.includes(SAID[seat])) {
        missing.push(`坐到座位 ${seat} 上，状态线里反而没了它自己那句：「${line}」`);
      }
      for (const other of Seats.filter((each) => each !== seat)) {
        if (line.includes(SAID[other])) {
          missing.push(
            `坐到座位 ${seat} 上，状态线里还写着座位 ${other} 的理由：「${line}」` +
              "（气泡拦住了而状态线漏了，那闸门就只是个摆设）",
          );
        }
        if (line.includes(`座位 ${other} 的模型选完了`)) {
          missing.push(`坐到座位 ${seat} 上，状态线里还列着座位 ${other}：「${line}」`);
        }
      }
      if (hushed !== "3") {
        missing.push(`坐到座位 ${seat} 上，状态线说被挡下的是 ${hushed} 席（该是 3 席）`);
      }
      console.log(`坐到座位 ${seat}：${only} 个气泡、状态线「${line}」`);
    }

    // 切回上帝：四家都回来，且**四句话互不相同**。后半句是阳性对照：
    // 没有它，「四个写着同一句话的空壳」与「四家各说各的」在上面那几条下长得一模一样。
    await page.getByTestId("table-view-god").click();
    const backAtGod = await page.locator('[data-testid$="-bubble"]').count();
    const godLine = ((await page.getByTestId("table-agent").textContent()) ?? "").trim();
    const saidBySeat = [];
    for (const seat of Seats)
      saidBySeat.push((await bubbleAt(page, seat))?.text ?? `（座位 ${seat} 没有）`);

    if (backAtGod !== 4) {
      missing.push(
        `切回上帝视角只有 ${backAtGod} 个气泡（该有 4 个）：上面那四轮「只剩一家」因此什么都没证明`,
      );
    }
    if (new Set(saidBySeat).size !== Seats.length) {
      missing.push(`上帝视角下四家的气泡里有重复的话：${JSON.stringify(saidBySeat)}`);
    }
    for (const seat of Seats) {
      if (!godLine.includes(SAID[seat])) {
        missing.push(
          `上帝视角下状态线里没有座位 ${seat} 那句：「${godLine}」` +
            "（那上面「坐座视角里没有别席的话」就是句空话）",
        );
      }
    }

    // ---- ② 挡不住牌与河 ----
    const covered = await overlaps(page);
    for (const each of covered) missing.push(each);

    // ---- ③ 点得开：全文面板 + 那一手的局面快照（与票 76 同一套断言，点座位 0 的） ----
    const spoke = await bubbleAt(page, 0);
    const beforeOpen = await boardDigest(page);
    const liveKawa = await kawaTotal(page);
    await page.getByTestId("seat-0-bubble").click();
    await settles(
      page,
      () => document.querySelector('[data-testid="table-bubble-detail"]') !== null,
      null,
      5000,
    );
    const detail = await detailOf(page);
    const snapshotKawa = await kawaTotal(page);

    console.log("");
    console.log(`点开座位 0 之后：${detail?.head}`);
    console.log(`  面板说的那一句：${detail?.viewing}`);
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
      if (String(detail.seat) !== "0") {
        missing.push(`摊开的那一手写着座位 ${detail.seat}，该是座位 0`);
      }
      // **面板上说得出人被搬到了哪儿**（票 86）：从前只有 `data-bubble-turn` 给机器看。
      // 这一页是 Live（`?table=1`）：那边没有游标也没有时间轴，
      // 因此 `data-bubble-origin` 必须是空的，那句话里也不允许提一根不存在的轴。
      if (
        detail.viewing !==
        `正在看第 ${detail.turn} 手：牌桌上摆的是那一刻的快照，这一桌照旧停在现在那一手。`
      ) {
        missing.push(
          `面板上那句「正在看第 N 手」与它摊开的那一手（第 ${detail.turn} 手）对不上：「${detail.viewing}」`,
        );
      }
      if (detail.origin === null) {
        missing.push(
          '面板上根本没有「正在看第 N 手」那一句（[data-testid="bubble-viewing"]）：' +
            "人因此看不见自己正在看哪一手（票 86）",
        );
      } else if (detail.origin !== "") {
        missing.push(
          `Live 那一页的面板写着一个原处（data-bubble-origin=「${detail.origin}」）：` +
            "那边根本没有游标，`openAt` 也什么都没搬走（票 86）",
        );
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
      if (!(detail.reason ?? "").includes(SAID[0])) {
        missing.push(`全文面板里的理由不是端点回的那句：「${detail.reason}」`);
      }
      // **面板里是完整那一句**（票 81）：气泡里那句刚刚被截过（⑩），
      // 而点开之后不允许再少一个字——否则「点开看全文」那枚招子是句空话。
      if (detail.reason !== REASONS[LONG_SEAT]) {
        missing.push(
          `全文面板里的理由不是完整那一句（${(detail.reason ?? "").length} 字 vs 端点回的 ${REASONS[LONG_SEAT].length} 字）：` +
            `「${detail.reason}」`,
        );
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

    // ---- ④ 一席坏了不拖累别席：座位 0 换到交不出来的端点，其余三席照说 ----
    const beforeTurns = [];
    for (const seat of [1, 2, 3]) {
      beforeTurns[seat] = Number.parseInt((await bubbleAt(page, seat))?.turn ?? "-1", 10);
    }
    // 换端点：票 73 之后端点住在**档案**里，得先把座位 0 那份档案摊开再改它的 base_url。
    await page.getByTestId("table-profile-0").click();
    await page.getByTestId("table-profile-base-url").fill(bad.baseUrl);
    await playPause(page, "播放");
    const troubledUp = await settles(
      page,
      () => document.querySelector('[data-testid="seat-0-bubble"]')?.dataset.bubble === "troubled",
      null,
      budgetMs,
    );
    // 别席的答复照收：等到其余三席里有人把手序往前推过 beforeTurns 再停。
    const othersMoved = await settles(
      page,
      (before) =>
        [1, 2, 3].some((seat) => {
          const turn = Number.parseInt(
            document
              .querySelector(`[data-testid="seat-${seat}-bubble"]`)
              ?.getAttribute("data-bubble-turn") ?? "-1",
            10,
          );
          return turn > before[seat];
        }),
      beforeTurns,
      budgetMs,
    );
    await playPause(page, "暂停");
    await settles(
      page,
      () => document.querySelectorAll('[data-bubble="thinking"]').length === 0,
      null,
      budgetMs,
    );
    if (!troubledUp) {
      missing.push(
        `换成那个交不出来的端点之后，座位 0 的气泡一直没变成「兜底」态（等了 ${budgetMs} ms）`,
      );
    }
    if (!othersMoved) {
      missing.push("座位 0 兜底之后，其余三席的气泡再没往前走过一手：一席坏了拖住了别席");
    }

    const troubled = await bubbleAt(page, 0);
    const latest = (await page.getByTestId("table-latest").textContent()).trim();

    console.log("");
    console.log(`换成越界端点之后，座位 0 的气泡（${troubled?.state}）：${troubled?.text}`);
    console.log(`牌桌上那句：${latest}`);

    if (troubled === null || troubled.state !== "troubled") {
      missing.push(`换了个交不出来的端点，座位 0 的气泡却还是「${troubled?.state}」态`);
    } else if (troubled.text.includes(SAID[0])) {
      missing.push("兜底那一手的气泡上还写着上一次的理由：它没跟着那一手的记录换");
    }
    for (const seat of [1, 2, 3]) {
      const bubble = await bubbleAt(page, seat);
      if (bubble?.state === "troubled") {
        missing.push(`座位 0 的端点坏了，座位 ${seat} 却也变成了「兜底」态：拖累别席了`);
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
    for (const each of endpoints) each.endpoint.kill();
    bad.endpoint.kill();
  }
  return null;
}

/** 第二程：固定延迟下的并发墙钟。返回失败清单（空 = 绿）。 */
async function wallClockLane(lane, pageOrigin, options) {
  const { budgetMs, delayMs, missing, problems } = options;

  const slow = await startEndpoint(pageOrigin, ["--delay", String(delayMs)]);
  const context = await lane.newContext();

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    // 三席共用那个延迟端点（一份档案坐三席），座位 3 仍是自带 bot：
    // ⑧ 的「bot 席上没有气泡」要在同一局真语料上一起验。
    await plantSeating(page, {
      profiles: [
        {
          name: "延迟端点的档案",
          provider: "custom-openai",
          model: "slow-model",
          base_url: slow.baseUrl,
          timeout_ms: "240000",
        },
      ],
      seats: Seats.map((seat) => (seat === 3 ? {} : { choice: profileChoice("延迟端点的档案") })),
    });

    console.log("");
    console.log(
      `第二程：座位 0/1/2 ← ${slow.baseUrl}（每问一次先睡 ${delayMs} ms），座位 3 是 bot`,
    );
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });

    // 换成探针挑好的种子（第一局第 2 手就有一轮多席响应），重开一桌。
    await page.getByTestId("table-seed").fill(String(CONCURRENT_SEED));
    await page.getByTestId("table-restart").click();
    // 上帝视角（票 81）：并发量的是「同时有几席在想」，坐座视角下最多只看得见一席；
    // ⑧（bot 那一席一个气泡都没有）同样要在全开的视角下验，否则它恒真。
    await page.getByTestId("table-view-god").click();

    // 记「几个『在想』气泡同时在场」的每次变迁；顺带盯 bot 席与 data-waited。
    await page.evaluate(() => {
      window.__thinkLog = [];
      window.__botBubbled = false;
      window.__waitAttrs = null;
      const snap = () => {
        const thinking = document.querySelectorAll('[data-bubble="thinking"]');
        if (document.querySelector('[data-testid="seat-3-bubble"]')) window.__botBubbled = true;
        if (window.__waitAttrs === null && thinking.length > 0) {
          const first = thinking[0];
          window.__waitAttrs = {
            waited: first.getAttribute("data-waited"),
            limit: first.getAttribute("data-wait-limit"),
          };
        }
        const log = window.__thinkLog;
        const last = log[log.length - 1];
        if (!last || last.count !== thinking.length) {
          log.push({ t: performance.now(), count: thinking.length });
        }
      };
      new MutationObserver(snap).observe(document.body, {
        subtree: true,
        childList: true,
        attributes: true,
      });
      snap();
    });

    await page.getByTestId("table-speed-8×").click();
    await playPause(page, "播放");

    // 等到一轮「≥2 席同时在想」完整落幕（升到 ≥2，之后回到 0）。
    const sawRound = await settles(
      page,
      () => {
        const log = window.__thinkLog;
        const rise = log.findIndex((each) => each.count >= 2);
        return rise >= 0 && log.slice(rise).some((each) => each.count === 0);
      },
      null,
      budgetMs,
    );
    await playPause(page, "暂停");
    await settles(
      page,
      () => document.querySelectorAll('[data-bubble="thinking"]').length === 0,
      null,
      budgetMs,
    );

    const record = await page.evaluate(() => ({
      log: window.__thinkLog,
      botBubbled: window.__botBubbled,
      waitAttrs: window.__waitAttrs,
    }));

    if (!sawRound) {
      missing.push(
        `等了 ${budgetMs} ms 也没见到「≥2 席同时在想」的一轮：并发没发生（观测到的变迁：${JSON.stringify(record.log.slice(0, 20))}）`,
      );
      return null;
    }

    // ---- ⑤⑥ 那一轮的墙钟 ----
    const rise = record.log.findIndex((each) => each.count >= 2);
    const settle = record.log.findIndex((each, index) => index > rise && each.count === 0);
    const window_ = record.log.slice(rise, settle + 1);
    const most = Math.max(...window_.map((each) => each.count));
    const wallMs = Math.round(record.log[settle].t - record.log[rise].t);

    console.log("");
    console.log(
      `同时在想的那一轮：${most} 席一起在飞，墙钟 ${wallMs} ms（端点延迟 ${delayMs} ms/份；` +
        `串行要 ${most} 份 ≈ ${most * delayMs} ms）`,
    );

    if (most < 2) missing.push(`该有 ≥2 席同时在想，实际最多 ${most} 席`);
    if (wallMs >= delayMs * 2) {
      missing.push(
        `同时在想的那一轮墙钟 ${wallMs} ms ≥ 两份延迟（${delayMs * 2} ms）：问话没有真的并发`,
      );
    }
    if (wallMs < delayMs * 0.4) {
      missing.push(
        `同时在想的那一轮墙钟只有 ${wallMs} ms（延迟 ${delayMs} ms）：端点的延迟根本没发生，这一条量了个空`,
      );
    }

    // ---- ⑦ 「在想」带着已等秒数与上限 ----
    if (record.waitAttrs === null) {
      missing.push("整程没抓到过一个「在想」气泡：data-waited 那两条断言没执行到");
    } else {
      if (!/^\d+$/.test(record.waitAttrs.waited ?? "")) {
        missing.push(`「在想」的气泡上没有已等秒数（data-waited=「${record.waitAttrs.waited}」）`);
      }
      if (record.waitAttrs.limit !== "240") {
        missing.push(
          `「在想」的气泡上限该是档案超时 240 秒，实际 data-wait-limit=「${record.waitAttrs.limit}」`,
        );
      }
    }

    // ---- ⑧ bot 席上没有气泡 ----
    if (record.botBubbled) {
      missing.push("座位 3 是自带 bot，整程却出过气泡");
    }
  } finally {
    await context.close();
    slow.endpoint.kill();
  }
  return null;
}

/** 思考气泡 + 并发那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyBubbles(lane, options = {}) {
  const { budgetMs = 120000, delayMs = 700, shoot = null } = options;

  // dev server 而不是 preview：与 verify-redaction 同一个理由（省掉一次 vite build）。
  const pageOrigin = await lane.devUrl();
  const problems = [];
  const missing = [];

  const early = await fourSeatsLane(lane, pageOrigin, { budgetMs, shoot, missing, problems });
  if (early !== null) return early;

  const early2 = await wallClockLane(lane, pageOrigin, { budgetMs, delayMs, missing, problems });
  if (early2 !== null) return early2;

  if (problems.length > 0) return failure("页面报了错：", problems);
  if (missing.length > 0) return failure("思考气泡这一道没过：", missing);

  console.log("");
  console.log("四席四个气泡各说各的、挡不住牌与河、点得开且收起来逐字回到现在 ✓");
  console.log("一席换成交不出来的端点：它走兜底，其余三席照样往前说 ✓");
  console.log("视角是一道信息闸门：坐到座位 N 上只剩那一席的气泡与状态线，切回上帝四家各说各的 ✓");
  console.log("固定延迟下几席同时在想：墙钟接近一份延迟而不是几份；在想的气泡带着已等秒数与上限 ✓");
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
      budgetMs: Number.parseInt(flag("--budget", "120000"), 10),
      delayMs: Number.parseInt(flag("--delay", "700"), 10),
      shoot: flag("--shoot", null),
    }),
  );
}
