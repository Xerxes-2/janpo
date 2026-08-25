// 牌谱从外面进来的两条路（票 78）的**无头闸门**：分享链接与导入 JSON。
//
// 验四件事：
//   1. **分享链接真往返一趟**（不是单测）：`?table=1` 真打几手 → 点「复制分享链接」→
//      读剪贴板（真剪贴板，授了权的）→ 断言那是一条不带 `?table=1`、hash 里只有载荷的地址，
//      且载荷解出的事件流与「导出牌谱」下下来的那份**逐条相同**（走 `Share.ofPayload`，
//      与页面同一条路）→ 打开那条地址：自动播、没有配桌面板、页面说得清为什么没有气泡
//      （那句话里得点名「导入牌谱 JSON」——票 76 的说明要接得上票 78 的入口）、
//      拖到末帧之后**点数与主持人那一桌一致**；
//   2. **坏链接不白屏**：把载荷改坏一个字符再打开，页面上是「载荷读不动：……」，
//      而导入入口还在（人最需要换一份牌谱的时刻）；
//   3. **导入闸门**（与 `verify-export` 那道互为反向）：把导出的那份文件从首页导回去，
//      自动播、拖到末帧与主持人那一桌一致；再导一份**带决策记录**的（引擎现打 + 拌一条
//      带 thinking 的记录）：**气泡有话**、气泡里就是那句 thinking——这是与分享链接的
//      关键差别（推理不上 URL），两种来源各验一次；
//      带记录那份还带着 token 账单，因此它顺带钉票 110 那件：**牌谱里的账只算落了子的
//      那几手**（作废的问话不进牌谱），而导入那一屏得**自己把这件事说出来**、
//      且不许编一个「几笔没落子」的数（它无从知道）；
//   4. **坏输入三连**：不是 JSON / 缺字段的牌谱 / 中间某局断掉的事件流，各断言一次
//      「有中文错误且页面还活着」（正在播的那份回放一帧不动）。
//
// 全程本机、一个网络请求都不发，因此它进 CI。
//
// 跑法（它也是 `verify-browser.mjs` 里的一趟）：
//   cd web && pnpm run fable && pnpm run verify:inbound

import { readFileSync } from "node:fs";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { hostPage, retryOnReload } from "./serve.mjs";
import { stepTurns } from "./table-drive.mjs";

/** 拌进「带决策记录」那份牌谱的 thinking：一段只可能出现在审计数据里的字串。 */
const THINKING = "先数向听：这手牌 2 向听，切 9 万最不亏——INBOUND-THINKING-MARK";

/**
 * 同一条记录里那句**一句话理由**（票 81）。两个标记各管一头：
 * **气泡里该是理由那一句，面板里才是 thinking 全文**——两头各断一次，两头对调了也红。
 */
const REASON = "9 万是孤张——INBOUND-REASON-MARK";

/**
 * 拌进引擎现打那一场的决策记录（气泡的阳性对照）：`ShareCheck.sample` 打出来的是
 * bot 牌谱（0 条记录），不拌的话「导入的那一份气泡有话」什么都没证明。
 * 只认牌谱的字段名，不认牌局内容——`verify-share.mjs` 的同族写法。
 */
const audit = {
  turn: 1,
  seat: 1,
  prompt_tail: "【现在】东1局 0 本场，你是座位 1……",
  render_version: "janpo-default@08fcaec3.4b9e57c0",
  action_ids: [0, 1],
  output: '{"action_id":1}',
  reason: REASON,
  thinking: THINKING,
  attempts: 1,
  latency_ms: 1873,
  applied: 1,
  // **带一份真的 token 账单**（票 110）：牌谱带得走的就是落了子的那几手的账，
  // 而作废的问话（花了钱、没落子）不进牌谱——导入那一屏因此得自己说出这件事。
  // 没这一格的话账单行压根不会长出来（一个 token 都没花时它不占位）。
  usage: { input: 812, output: 96, cache_read: 1344, cache_write: 0 },
};

/**
 * 同一份牌谱里另一席的记录：**只有 thinking、没有 reason**（关着思考预算的模型给不出理由）。
 * 它钉的是票 81 那条退路：**取头一段、三点号收尾、挂一枚「点开看全文」**。
 * 上面那一条两样都有（走 reason 那一路），两条合起来两条支路在浏览器里各走一遍。
 */
const MUTE_HEAD = "先数向听：这手牌 1 向听——INBOUND-HEAD-MARK";

const muteAudit = {
  ...audit,
  turn: 2,
  seat: 2,
  reason: null,
  thinking: `${MUTE_HEAD}\n再看安全度：这一段在气泡里不允许出现——INBOUND-TAIL-MARK`,
  applied: null,
};

/** 逐字符腐蚀用的字母表（与 verify-share 同一个）：换完仍在 base64url 字符集里。 */
const ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

/**
 * 这一桌此刻**与视角无关**的那份摘要：四家点数与河的张数、场况、供托、剩余摸牌。
 * 两页从票 82 起都默认上帝视角，但这一段仍旧只比与视角无关的那几样：
 * 视角是页面上按得动的一枚按钮，拿它当前提的断言迟早会被下一次改默认值咬到。
 */
async function boardSummary(page) {
  return await page.evaluate(() => {
    const attr = (testId, name) =>
      document.querySelector(`[data-testid="${testId}"]`)?.getAttribute(name) ?? "?";
    return [0, 1, 2, 3]
      .map(
        (index) =>
          `${attr(`seat-${index}-score`, "data-score")}|${attr(`seat-${index}-kawa`, "data-kawa-count")}`,
      )
      .concat([
        `${attr("table-kyoku", "data-bakaze")}${attr("table-kyoku", "data-kyoku")}.${attr("table-kyoku", "data-honba")}`,
        `供托 ${attr("table-kyotaku", "data-kyotaku")}`,
        `山 ${attr("table-wall", "data-wall")}`,
      ])
      .join("\n");
  });
}

/** 这一屏此刻走到哪（四家的河合计）：自动播那条断言采它两次。 */
async function progress(page) {
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

/** 等一个条件成立；超时返回 false 而不是抛（合并跑要先关浏览器再汇报）。 */
async function settles(page, predicate, argument) {
  try {
    await page.waitForFunction(predicate, argument, { timeout: 15000 });
    return true;
  } catch (_error) {
    return false;
  }
}

/** 把时间轴拖到末帧（点滑块右端，等游标落定）。返回 false = 没拖动。 */
async function dragToEnd(page) {
  const slider = page.getByTestId("table-timeline");
  if ((await slider.count()) === 0) return false;
  const box = await slider.boundingBox();
  await slider.click({ position: { x: box.width - 1, y: box.height / 2 } });
  return await settles(
    page,
    () =>
      document.querySelector('[data-testid="table-timeline"]')?.getAttribute("data-cursor") ===
      document.querySelector('[data-testid="table-timeline"]')?.getAttribute("data-last"),
  );
}

/** 导入一份「文件」（真走 `<input type="file">` 的 change 事件）。 */
async function importFile(page, text) {
  await page.getByTestId("table-import").setInputFiles({
    name: "paifu.json",
    mimeType: "application/json",
    buffer: Buffer.from(text, "utf8"),
  });
}

/** 牌谱从外面进来的那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyInbound(lane) {
  const url = await lane.devUrl();
  // 剪贴板要真授权：读剪贴板是这一趟「真往返」的关节，DOM 里塞一份副本不算数。
  const context = await lane.newContext({
    acceptDownloads: true,
    permissions: ["clipboard-read", "clipboard-write"],
  });
  const problems = [];

  const watch = (page) => {
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
    });
  };

  try {
    // ---- 1. 主持人那一页：打几手、导出、复制分享链接 ----

    const host = await context.newPage();
    watch(host);
    await host.goto(hostPage(url), { waitUntil: "load" });

    const { walked, kyokus, stuckAt } = await stepTurns(host, {
      limit: 30,
      nextKyoku: true,
      budgetMs: 30000,
    });
    if (stuckAt !== null) problems.push(`第 ${stuckAt} 手没走动`);
    console.log(`主持人那一桌走了 ${walked} 手（${kyokus} 局）`);

    const [download] = await Promise.all([
      host.waitForEvent("download", { timeout: 30000 }),
      host.getByTestId("table-export").click(),
    ]);
    const exported = readFileSync(await download.path(), "utf8");
    const hostBoard = await boardSummary(host);

    await host.getByTestId("table-share").click();
    const shared = await settles(
      host,
      () =>
        (document.querySelector('[data-testid="table-share-note"]')?.getAttribute("data-share") ??
          "") !== "",
    );
    if (!shared) problems.push("点了「复制分享链接」，那一行却一直没说下场（data-share 空着）");

    const wire = await host.getByTestId("table-share-note").getAttribute("data-share");
    const chars = await host.getByTestId("table-share-note").getAttribute("data-share-chars");
    console.log(`复制的下场：${wire}（载荷 ${chars} 字符）`);
    if (wire !== "copied") {
      problems.push(`这几十手的载荷远在阈值之内，下场却是「${wire}」`);
    }

    const copiedUrl = await host.evaluate(() => navigator.clipboard.readText());
    console.log(`剪贴板里：${copiedUrl.slice(0, 60)}…（共 ${copiedUrl.length} 字符）`);

    let payload = "";
    try {
      const parsed = new URL(copiedUrl);
      payload = parsed.hash.replace(/^#/, "");
      if (!copiedUrl.startsWith(url)) {
        problems.push(`剪贴板里的地址不指向本站：${copiedUrl.slice(0, 60)}`);
      }
      if (parsed.searchParams.get("table") !== null) {
        problems.push("分享链接带上了 ?table=1：访客会被摆上一张配桌面板");
      }
      if (payload === "" || !/^[A-Za-z0-9_-]+$/.test(payload)) {
        problems.push(`hash 里不是一段光秃秃的 base64url 载荷：「#${payload.slice(0, 24)}…」`);
      }
    } catch (_error) {
      problems.push(`剪贴板里不是一条地址：${copiedUrl.slice(0, 60)}`);
    }

    // 载荷解出的事件流与导出的那份逐条相同（走 `Share.ofPayload`，与页面同一条路）。
    if (payload !== "") {
      const checked = JSON.parse(
        await retryOnReload(() =>
          host.evaluate(
            async ({ text, payload }) => {
              const share = await import("./src/generated/Share.js");
              return share.ShareCheck_check(text, payload);
            },
            { text: exported, payload },
          ),
        ),
      );
      if (checked.error) {
        problems.push(`剪贴板里的载荷解不回牌谱：${checked.error}`);
      } else {
        console.log(
          `载荷 ↔ 导出件：事件流逐条相同 = ${checked.same_events}　载荷里决策记录 ${checked.decisions} 条`,
        );
        if (!checked.same_events) {
          problems.push(`载荷解出的事件流与导出的那份不同：${checked.mismatch}`);
        }
        if (checked.decisions !== 0) {
          problems.push(`分享载荷里还剩 ${checked.decisions} 条决策记录：推理不该上 URL`);
        }
      }
    }

    // ---- 2. 打开那条地址：访客那一屏 ----

    if (payload !== "") {
      const guest = await context.newPage();
      watch(guest);
      await guest.goto(copiedUrl, { waitUntil: "load" });

      try {
        await guest.getByTestId("table-board").waitFor({ timeout: 15000 });
      } catch (_error) {
        const said = await guest.evaluate(
          () =>
            document.querySelector('[data-testid="table-error"]')?.textContent ??
            "（页面上什么也没说）",
        );
        problems.push(`打开分享链接没摆出牌桌：${said}`);
      }

      if ((await guest.getByTestId("table-board").count()) > 0) {
        // 自动播：与首页 Demo 同一条路。
        const before = await progress(guest);
        await guest.waitForTimeout(800);
        const after = await progress(guest);
        if (after <= before) {
          problems.push(`打开分享链接后牌桌没在动（800 ms 前后河合计都是 ${before} 张）`);
        }

        if ((await guest.getByTestId("table-llm-panel").count()) !== 0) {
          problems.push("分享链接那一屏摆出了配桌面板：它是给访客看的回放");
        }

        // 只带棋谱：一个气泡都没有，而那句解释里点名了导入入口（票 76 ↔ 票 78）。
        const bubbles = await guest.locator('[data-testid$="-bubble"]').count();
        if (bubbles !== 0) {
          problems.push(`分享链接不带推理，页面上却画出了 ${bubbles} 个思考气泡`);
        }
        const note = guest.getByTestId("table-no-bubbles");
        if ((await note.count()) === 0) {
          problems.push("分享链接那一屏没说为什么没有气泡（table-no-bubbles）");
        } else if (!(await note.textContent()).includes("导入牌谱 JSON")) {
          problems.push("那句「为什么没有气泡」没指回「导入牌谱 JSON」：人不知道完整版从哪儿进来");
        }

        // 拖到末帧：与主持人那一桌逐项一致（点数、河、场况、供托、剩余摸牌）。
        if (!(await dragToEnd(guest))) {
          problems.push("分享链接那一屏的时间轴拖不到末帧");
        } else {
          const guestBoard = await boardSummary(guest);
          if (guestBoard !== hostBoard) {
            problems.push(
              `分享链接回放到末帧与主持人那一桌不同：\n主持人：\n${hostBoard}\n——\n访客：\n${guestBoard}`,
            );
          } else {
            console.log("分享链接回放到末帧：点数、河、场况与主持人那一桌逐项一致 ✓");
          }
        }
      }
      await guest.close();
    }

    // ---- 3. 坏链接：改坏一个字符再打开，中文原因、页面活着 ----

    if (payload !== "") {
      const middle = Math.floor(payload.length / 2);
      const swapped = ALPHABET[(ALPHABET.indexOf(payload[middle]) + 1) % ALPHABET.length];
      const broken = payload.slice(0, middle) + swapped + payload.slice(middle + 1);

      const bad = await context.newPage();
      watch(bad);
      await bad.goto(`${url}/#${broken}`, { waitUntil: "load" });
      const said = await settles(
        bad,
        () =>
          document
            .querySelector('[data-testid="table-error"]')
            ?.textContent?.startsWith("载荷读不动：") === true,
      );
      if (!said) {
        const shown = await bad.evaluate(
          () =>
            document.querySelector('[data-testid="table-error"]')?.textContent ??
            "（页面上什么也没说）",
        );
        problems.push(`改坏一个字符的链接该红在「载荷读不动」，页面上却是：${shown}`);
      } else {
        console.log(
          `坏链接：${(await bad.getByTestId("table-error").textContent()).slice(0, 48)}…`,
        );
      }
      // 页面活着，而且导入入口还在——人最需要换一份牌谱的时刻。
      if ((await bad.getByTestId("table-import").count()) === 0) {
        problems.push("坏链接那一屏连「导入牌谱 JSON」都没了：死路一条");
      }
      await bad.close();
    }

    // ---- 4. 导入：导出的那份回得去，带记录的那份气泡有话，坏输入三连 ----

    const home = await context.newPage();
    watch(home);
    await home.goto(`${url}/`, { waitUntil: "load" });
    await home.getByTestId("table-board").waitFor({ timeout: 15000 });

    const lastOf = () =>
      home.evaluate(
        () =>
          document.querySelector('[data-testid="table-timeline"]')?.getAttribute("data-last") ??
          "?",
      );

    // 4a. 导出的那份文件重新导入：末帧与主持人那一桌逐项一致（verify-export 的反向）。
    const demoLast = await lastOf();
    await importFile(home, exported);
    const swappedIn = await settles(
      home,
      (previous) => {
        const last = document
          .querySelector('[data-testid="table-timeline"]')
          ?.getAttribute("data-last");
        return last !== undefined && last !== previous;
      },
      demoLast,
    );
    if (!swappedIn) problems.push("导入导出的那份文件之后，牌桌没换成那一场（帧数没变）");

    const playing = (await home.getByTestId("table-play").textContent()).trim();
    if (playing !== "暂停") {
      problems.push(`导入之后该自动播（播放键上该写「暂停」），却写着「${playing}」`);
    }

    if (!(await dragToEnd(home))) {
      problems.push("导入之后时间轴拖不到末帧");
    } else {
      const importedBoard = await boardSummary(home);
      if (importedBoard !== hostBoard) {
        problems.push(
          `导入的回放到末帧与主持人那一桌不同：\n主持人：\n${hostBoard}\n——\n导入：\n${importedBoard}`,
        );
      } else {
        console.log("导出的文件重新导入：末帧与主持人那一桌逐项一致 ✓");
      }

      // 导进来的这份是 bot 棋谱（一条决策记录都没有）：气泡得全没了、那句指路话得回来。
      // **这是 4b 的阳性对照**：票 79 把首页资产换成带记录的真对局之后，首页一打开就已经
      // 没有那句话了——少了这一段，4b 那条「导完不该还挂着那句话」从此永远为真（判据 3）。
      const strippedBubbles = await home.locator('[data-testid$="-bubble"]').count();
      if (strippedBubbles !== 0) {
        problems.push(
          `导入的那份是 bot 棋谱，页面上却还有 ${strippedBubbles} 个思考气泡（上一份的没清掉？）`,
        );
      }
      if ((await home.getByTestId("table-no-bubbles").count()) === 0) {
        problems.push(
          "导入一份没有决策记录的牌谱之后，页面上没说为什么没有气泡（table-no-bubbles）",
        );
      }
    }

    // 4b. 带决策记录的那份：气泡有话——与分享链接的关键差别，两种来源各验一次。
    const recorded = await retryOnReload(() =>
      home.evaluate(
        async ({ audit, muteAudit }) => {
          const share = await import("./src/generated/Share.js");
          const doc = JSON.parse(share.ShareCheck_sample("tonpuusen", 2088));
          doc.decisions = [audit, muteAudit];
          return JSON.stringify(doc);
        },
        { audit, muteAudit },
      ),
    );

    const exportedLast = await lastOf();
    await importFile(home, recorded);
    const recordedIn = await settles(
      home,
      (previous) => {
        const last = document
          .querySelector('[data-testid="table-timeline"]')
          ?.getAttribute("data-last");
        return last !== undefined && last !== previous;
      },
      exportedLast,
    );
    if (!recordedIn) problems.push("导入带决策记录的那份之后，牌桌没换成那一场");

    if (!(await dragToEnd(home))) {
      problems.push("带记录那份导进来之后时间轴拖不到末帧");
    } else {
      const bubble = home.getByTestId("seat-1-bubble");
      if ((await bubble.count()) === 0) {
        problems.push("导入的全量牌谱带着决策记录，座位 1 却一个气泡都没有——导入丢了推理");
      } else {
        const state = await bubble.getAttribute("data-bubble");
        const saidText = await bubble.textContent();
        if (state !== "spoke") problems.push(`座位 1 的气泡该是 spoke，却是 ${state}`);

        // **气泡里是那句理由，不是 thinking 全文**（票 81 把票 76 那条优先级翻了面）。
        // 两句都核：只核前一句的话，「气泡里把 thinking 也一并塞进去了」同样能绿。
        if (!saidText.includes("INBOUND-REASON-MARK")) {
          problems.push(`气泡里不是那句一句话理由：「${saidText.slice(0, 48)}…」`);
        }
        if (saidText.includes("INBOUND-THINKING-MARK")) {
          problems.push(
            `气泡里把 thinking 全文也塞进去了：「${saidText.slice(0, 48)}…」` +
              "（票 81：气泡只放一句话，全文在点开那一屏）",
          );
        }

        // **thinking 全文在点开那一屏里**：「导入没丢推理」这件事从此由它钉着。
        await bubble.click();
        const gotPanel = await settles(
          home,
          () => document.querySelector('[data-testid="table-bubble-detail"]') !== null,
        );
        const thinkingText = gotPanel
          ? ((await home.getByTestId("bubble-thinking").textContent()) ?? "")
          : "（面板没摊开）";
        if (!thinkingText.includes("INBOUND-THINKING-MARK")) {
          problems.push(
            `点开之后面板里也没有那句 thinking 全文：「${thinkingText.slice(0, 48)}…」`,
          );
        } else {
          console.log("导入的那一份气泡有话：气泡里是那句理由、thinking 全文在点开那一屏里 ✓");
        }
        if (gotPanel) await home.getByTestId("bubble-close").click();
      }

      // **只有 thinking 没有 reason 那一路**（票 81）：气泡里是**头一段**，
      // 后面那一段不允许出现，而且得挂着那枚招子——否则人不知道后面还有。
      //
      // **得先重新拖到末帧**：上面点开那一下把游标挪到了第 1 手那一帧（票 76：轴只有一根），
      // 而收起来并不跳回去——第 2 手那条记录在那一帧上本来就不存在。
      if (!(await dragToEnd(home))) problems.push("收起全文面板之后时间轴又拖不到末帧了");
      const mute = home.getByTestId("seat-2-bubble");
      if ((await mute.count()) === 0) {
        problems.push("只有 thinking 的那一条记录没有堆出气泡（座位 2）");
      } else {
        const muteText = (await mute.textContent()) ?? "";
        if (!muteText.includes("INBOUND-HEAD-MARK")) {
          problems.push(`只有 thinking 时气泡里不是它的头一段：「${muteText.slice(0, 48)}…」`);
        }
        if (muteText.includes("INBOUND-TAIL-MARK")) {
          problems.push(
            `只有 thinking 时气泡里把后面那几段也写上了：「${muteText.slice(0, 64)}…」`,
          );
        }
        if (!muteText.includes("……")) {
          problems.push(`只有 thinking 时气泡里取了头一段却没有三点号收尾：「${muteText}」`);
        }
        if ((await home.getByTestId("seat-2-bubble-more").count()) !== 1) {
          problems.push("只有 thinking 时气泡上没挂「点开看全文」：后面那几段就这么无声无息地没了");
        }
        console.log(`只有 thinking 那一条：气泡里是「${muteText.trim()}」`);
      }
      if ((await home.getByTestId("table-no-bubbles").count()) !== 0) {
        problems.push("带记录的牌谱导进来了，页面上却还挂着「这一局没有思考气泡」");
      }

      // **牌谱里的账少一块，进来那一屏得自己说出来**（票 110）。
      // 作废的问话是**这一次会话的事实**，不进牌谱；于是拿到牌谱的人看到的这个数
      // 只是「落了子的那几手」的合计，而当时花掉的只多不少。
      // **没说出来的缺失就是骗人**，而这一头（牌谱进来）正是人读到那个数的地方；
      // 另一头（Live 那桌导出之前印出那两个数）在 `verify-stale-ask.mjs` 的 ⑨。
      const usage = home.getByTestId("table-usage");
      if ((await usage.count()) === 0) {
        problems.push(
          "导入的牌谱里那条记录带着 token 账单，页面上却一行账都没有（table-usage）：" +
            "下面那几条在空转",
        );
      } else {
        const usageText = (await usage.textContent()) ?? "";
        if (!usageText.includes("牌谱只带得走落了子的那几手的账")) {
          problems.push(
            `导入那一屏的账单行没说出自己缺了一块：「${usageText.trim()}」` +
              "——作废的问话不进牌谱，拿到牌谱的人无从知道当时还花掉了多少",
          );
        }
        // **不许在回放里编一个数**：牌谱压根没告诉它有几笔作废。
        const invented = await usage.getAttribute("data-void-asks");
        if (usageText.includes("其中") || invented !== "0") {
          problems.push(
            `导入那一屏编出了一个「几笔没落子」的数（data-void-asks=${invented}）：` +
              `「${usageText.trim()}」——牌谱里没有这一格，它无从知道`,
          );
        }
        console.log(`导入那一屏的账单行：${usageText.trim()}`);
      }
    }

    // 4c. 坏输入三连：中文原因 + 正在播的那份回放一帧不动。
    const stranded = (() => {
      const doc = JSON.parse(recorded);
      const starts = doc.events
        .map((event, index) => (event.type === "start_kyoku" ? index : -1))
        .filter((index) => index >= 0);
      const second = starts[1];
      doc.events = doc.events.filter((_, index) => index < second - 3 || index >= second);
      return JSON.stringify(doc);
    })();

    const trio = [
      ["不是 JSON", "这不是 JSON", "牌谱读不动："],
      ["缺字段的牌谱", '{"version":3}', "牌谱读不动："],
      ["中间某局断掉的事件流", stranded, "牌谱回放不动："],
    ];

    const lastBefore = await lastOf();
    let previousFault = "";
    for (const [label, content, prefix] of trio) {
      await importFile(home, content);
      const red = await settles(
        home,
        ({ prefix, previous }) => {
          const said = document.querySelector('[data-testid="table-import-fault"]')?.textContent;
          return said?.startsWith(prefix) && said !== previous;
        },
        { prefix, previous: previousFault },
      );
      if (!red) {
        const said = await home.evaluate(
          () =>
            document.querySelector('[data-testid="table-import-fault"]')?.textContent ??
            "（页面上什么也没说）",
        );
        problems.push(`导入「${label}」该红在「${prefix}」，页面上却是：${said}`);
        continue;
      }
      previousFault = await home.getByTestId("table-import-fault").textContent();
      console.log(`导入「${label}」：${previousFault.slice(0, 56)}…`);

      // 页面活着：牌桌还在、正在放的那份回放一帧没少。
      if ((await home.getByTestId("table-board").count()) === 0) {
        problems.push(`导入「${label}」之后牌桌整个没了`);
      }
      if ((await lastOf()) !== lastBefore) {
        problems.push(`导入「${label}」失败却把正在放的那份回放换掉了`);
      }
    }

    await home.close();
    await host.close();
  } finally {
    await context.close();
  }

  if (problems.length > 0) return failure("牌谱从外面进来的两条路验收没过：", problems);

  console.log("");
  console.log("分享链接真往返回得去、导入的全量牌谱气泡有话、坏输入三连各说各的中文原因 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  await runStandalone((lane) => verifyInbound(lane));
}
