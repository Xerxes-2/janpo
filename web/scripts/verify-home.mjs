// 首页（票 71）：**访客打开 `/` 什么都不用配，第一眼就是一桌牌在走**
//（spec 的 story 1，ADR-0003 由 Demo Paifu 兑现）。
//
// 四条断言，各守一件这一票才成立的事：
//
//   ① 牌桌**在动**：隔一会儿采两次，手数必须不同（自动播是这一页的全部卖点）；
//   ② 页面上**没有配桌控件**：访客第一眼不该是一张表单（票 35 的「默认视图只该有牌桌」同一条标准）；
//   ③ 有一条**去 `?table=1`** 的路：Host 那一侧访客得摸得到，而且点过去真的是那一页；
//   ④ 页脚照旧（票 37）：回仓库的外链与许可。
//
// **它与 `verify-tracer` 不重**：那一道量的是「首页里没有开发向内容」（藏没藏住），
// 这一道量的是「首页本身像不像个门面」。两道都开 `/`，其余七道全开 `?table=1`。
//
// **资产是 `fetch` 拉的**（`web/public/demo-paifu.json`，不打进 bundle）：因此这一道顺带
// 是那条取用路径的唯一无头证据——404 了页面会说一句「Demo 牌谱拉不到」，而 ① 会当场红。
//
// 跑法：`cd web && pnpm run build && pnpm run verify:home`
// 它也是 `verify-browser.mjs` 里的一道（十道共用一个浏览器与一台服务器）。

import { failure, isEntry, runStandalone } from "./browser-lane.mjs";

/** 只属于主持人那一页的控件。首页上出现任何一个都算「第一眼是张表单」。 */
const HOST_TEST_IDS = [
  "table-llm-panel",
  "table-seed",
  "table-step",
  "table-next",
  "table-export",
  "table-llm-none",
  "table-llm-provider",
  "table-llm-key",
  "table-bot-random",
];

/** 隔多久采第二次手数。1 秒够 2× 播三四手（`TableState.demoSpeed`），也不至于让 CI 变慢。 */
const SAMPLE_GAP_MS = 1000;

/** 这一屏此刻走到第几手（牌桌上那句「上一手：……」旁边没有数字，就数四家的河）。 */
async function progress(page) {
  return await page.evaluate(() =>
    [0, 1, 2, 3]
      .map((index) => {
        const label = document.querySelector(`[data-testid="seat-${index}-kawa"]`);
        return Number.parseInt(label?.getAttribute("data-kawa-count") ?? "0", 10);
      })
      .reduce((sum, each) => sum + each, 0),
  );
}

/** 首页那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyHome(lane) {
  const url = await lane.previewUrl();
  const page = await lane.newPage();
  const problems = [];
  const missing = [];
  const leaks = [];

  try {
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
    });

    await page.goto(`${url}/`, { waitUntil: "load" });

    // 牌桌得先真的摆出来：那份 Demo 牌谱是 `fetch` 回来的，拉不到时这里就等不到。
    // 等不到的话下面几条断言会各说各的话，而真正的原因是页面上那句「Demo 牌谱拉不到：……」。
    try {
      await page.getByTestId("table-board").waitFor({ timeout: 15000 });
    } catch (_error) {
      const said = await page.evaluate(
        () =>
          document.querySelector('[data-testid="table-error"]')?.textContent ??
          "（页面上什么也没说）",
      );
      return failure("首页没摆出牌桌（那份 Demo 牌谱多半没拉到）：", [said]);
    }

    // ① 在动：隔一会儿采两次。**手数不同**才算数——静止的牌桌与「自动播坏了」看上去一样。
    const before = await progress(page);
    await page.waitForTimeout(SAMPLE_GAP_MS);
    const after = await progress(page);

    if (after <= before) {
      missing.push(
        `牌桌没在动：${SAMPLE_GAP_MS} ms 前后四家的河合计都是 ${before} 张（自动播没跑起来）`,
      );
    }

    // ② 没有配桌控件。
    for (const testId of HOST_TEST_IDS) {
      const count = await page.getByTestId(testId).count();
      if (count !== 0) leaks.push(`首页上还挂着 [data-testid="${testId}"]（${count} 个）`);
    }

    // ③ 去 `?table=1` 的路：**真点过去**，落地那一页必须是主持人那一页。
    const link = page.getByTestId("home-host-link");
    if ((await link.count()) === 0) {
      missing.push("首页上没有一条去 `?table=1` 的路（访客摸不到 Host 那一侧）");
    } else {
      await link.click();
      await page.getByTestId("table-llm-panel").waitFor({ timeout: 15000 });
      const landed = new URL(page.url());
      if (landed.searchParams.get("table") !== "1") {
        missing.push(`那条路点过去落在 ${page.url()}，不是 ?table=1`);
      }
      // 主持人那一页**默认暂停**：那几道要点、要读牌桌的闸门全靠这一条。
      const playing = (await page.getByTestId("table-play").textContent()).trim();
      if (playing !== "播放") {
        missing.push(`?table=1 落地那一刻不是暂停着的（播放键上写着「${playing}」）`);
      }
    }

    // ④ 页脚照旧（票 37）：地址本身不在这里复述（真源在 `src/Janpo.Web/Footer.fs` 一处）。
    await page.goto(`${url}/`, { waitUntil: "load" });
    const footerLinks = await page
      .getByTestId("site-footer")
      .locator('a[href^="https://"]')
      .count();
    if (footerLinks === 0) missing.push("首页的页脚里没有一条指回仓库的外链（票 37）");
    const text = await page.evaluate(() => document.body.innerText);
    if (!text.includes("MIT")) missing.push("首页的正文里没提许可（MIT）（票 37）");

    if (problems.length > 0) return failure("页面报了错：", problems);
    if (leaks.length > 0) return failure("首页上漏出了只属于主持人那一页的控件：", leaks);
    if (missing.length > 0) return failure("首页少了该给访客的东西：", missing);

    console.log(`牌桌在动 ✓（${SAMPLE_GAP_MS} ms 里四家的河从 ${before} 张长到 ${after} 张）`);
    console.log(`首页上没有配桌控件 ✓（查了 ${HOST_TEST_IDS.length} 个）`);
    console.log("「自己开一桌」点过去就是 ?table=1，且那一页默认暂停 ✓");
    console.log("页脚里有回仓库的外链与许可（MIT）✓");
    return [];
  } finally {
    await page.close();
  }
}

if (isEntry(import.meta.url)) {
  await runStandalone((lane) => verifyHome(lane));
}
