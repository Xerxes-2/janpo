// 配桌上那三项规则开关的**无头闸门**（票 72）：**对局长度 / 赤宝牌 / 食断**（spec 的 story 13）。
//
// 它验的不是「引擎认不认这几个开关」（那边早有 dotnet 侧用例），而是**开关真的从页面传下去了**：
// 每一条断言读的都是**页面上点出来的那一桌导出的牌谱**。
//
//   ① 默认那一桌是东风战 / 有赤 / 有食断，且**页面上印的那一份**（`data-rules`）与牌谱里的
//      `ruleset` 对得上；
//   ② 打完一整场东风战：`start_kyoku` 的场风**全是东**，而且四局都打到了；
//      赤宝牌开着，事件流里那三张赤牌**真的出现过**（下面 ⑤ 那条「一张都没有」的阳性对照，判据 3）；
//   ③ **半场不换规则**：把三项都拨到另一边、**不按重开**，导出的牌谱里 `ruleset` 一个字段都没变
//      （页面上那句「按重开才生效」跟着亮起来）；
//   ④ 按「重开」之后才换：`data-rules` 跟着变，且不再等着生效；
//   ⑤ 打完一整场半庄：`ruleset.length` 是 `hanchan`、**南场真的打到了**（局数序列由
//      `Ruleset.kyokus` 推：四麻半庄 8 局）；`ruleset.akadora` 为空且**事件流里一张赤牌都没有**；
//      `ruleset.kuitan` 是 false；
//   ⑥ 三项**落 localStorage**：把这一页重新打开一次，拨到的三项还在（键在
//      `janpo.rules.*`，与模型座位那几格分开）。
//
// 四家都是自带 bot，**一个网络请求都不发**，因此它进 CI。
//
// 跑法：`cd web && pnpm run build && pnpm run verify:setup`
// 它也是 `verify-browser.mjs` 里的一趟（跑道上那几趟共用一个浏览器与一台服务器）。

import { readFileSync } from "node:fs";
import { failure, isEntry, markerSince, runStandalone } from "./browser-lane.mjs";
import { hostPage } from "./serve.mjs";
import { openSetup, stepTurns } from "./table-drive.mjs";

/** 一整场最多走几手（东风战约 250 手、半庄约 500，留足连庄的余量）。 */
const TURN_LIMIT = 4000;

/** 赤 5 的 mjai 记法。**牌谱里的字节长这样**，因此闸门按字节找它们。 */
const AKADORA = ['"5mr"', '"5pr"', '"5sr"'];

/** 场风的 mjai 记法（`Kaze.toMjai`）：东 = `1z`、南 = `2z`。 */
const TON = "1z";
const NAN = "2z";

/** 事件流里出现了几张赤牌。**只扫事件流**：规则集那一段本来就列着赤牌种，连它一起扫的话
 * 「一张都没有」那条断言永远数得出东西来（阳性对照就白给了）。 */
function akadoraSeen(paifu) {
  const events = JSON.stringify(paifu.events);
  return AKADORA.map((each) => events.split(each).length - 1).reduce((sum, each) => sum + each, 0);
}

/** 这份牌谱里每一局的场风与局数（`start_kyoku`，按顺序；连庄会让同一项出现多次）。 */
function kyokus(paifu) {
  return paifu.events
    .filter((event) => event.type === "start_kyoku")
    .map((event) => `${event.bakaze}-${event.kyoku}`);
}

/** 这一桌页面上印着的那一份规则（`RulesetDraft.toWire`：长度/赤/食断）与「等着生效吗」。 */
async function onPage(page) {
  const rules = page.getByTestId("table-rules");
  return {
    rules: await rules.getAttribute("data-rules"),
    pending: await rules.getAttribute("data-rules-pending"),
  };
}

/** 牌谱里那一段规则集摊成同一种写法，好与页面上印的那一份直接比。 */
function fromPaifu(paifu) {
  const akadora = paifu.ruleset.akadora.length > 0 ? "on" : "off";
  return `${paifu.ruleset.length}/${akadora}/${paifu.ruleset.kuitan ? "on" : "off"}`;
}

/** 打完一整场，再点「导出牌谱」把那份字节读回来。 */
async function playAndExport(page, problems, what) {
  const { kyokus: played, stuckAt } = await stepTurns(page, {
    limit: TURN_LIMIT,
    nextKyoku: true,
    budgetMs: 30000,
  });
  if (stuckAt !== null) problems.push(`${what}：第 ${stuckAt} 手没走动`);
  if ((await page.getByTestId("table-fault").count()) > 0) {
    problems.push(`${what}：牌桌停住了：${await page.getByTestId("table-fault").textContent()}`);
  }
  if ((await page.getByTestId("table-result").count()) === 0) {
    problems.push(`${what}：走了 ${played} 局也没走到终局（手数上限 ${TURN_LIMIT}）`);
  }
  return await exportPaifu(page);
}

/** 点一下「导出牌谱」，把真下下来的那份字节读成 JSON。 */
async function exportPaifu(page) {
  const [download] = await Promise.all([
    page.waitForEvent("download", { timeout: 30000 }),
    page.getByTestId("table-export").click(),
  ]);
  return JSON.parse(readFileSync(await download.path(), "utf8"));
}

/** 配桌那三项开关那一道。返回的是失败清单（空 = 绿）。 */
/**
 * 配桌那一枚折叠（票 116）。四条，各守一件：
 *
 *   ① **默认收着**——收起前它占着第一屏 810 px 里的 528 px，牌桌一像素看不见。
 *   ② **点得开且点得收**——两个方向都真点一次（只验开的话，
 *      一个收不回去的抽屉照样全绿）。
 *   ③ **摘要行写的是那四项的值**——不是「配桌」两个字。只画一枚点
 *      会让人看得出「有东西」却看不出「是什么」（同票 83 §2.1 那条判据）。
 *   ④ **收着时里面那些真的没渲染**——拿 `checkVisibility()` 量。
 *      **不能拿矩形是否为零代替**：Chrome 新版 `<details>` 收起用的是
 *      `content-visibility: hidden` 而不是 `display: none`，子元素**点不到、却仍有布局**
 *      （实测收起时四席那几行照旧 h=37、top 不变）。
 */
async function drawerProblems(page) {
  const problems = [];
  const setup = page.getByTestId("table-setup");
  await setup.waitFor({ state: "attached" });

  // **数的是摘要行以外那些**：摘要行自己在收着时本就该看得见，
  // 把它也数进去的话「收着 = 0 个渲染」永远不成立（头一版就这么误报的）。
  const state = async () =>
    await setup.evaluate((el) => {
      const inner = [...el.querySelectorAll("[data-testid]")].filter(
        (e) => e.closest("summary") === null,
      );
      return {
        open: el.open,
        inside: inner.length,
        shown: inner.filter((e) => e.checkVisibility()).length,
      };
    });

  const shut = await state();
  if (shut.inside === 0)
    problems.push("配桌里一个带 testId 的元素都没扇到：这一条自己坏了，不是页面好了");
  if (shut.open) problems.push("配桌一进页面就摊开着：它开局前用一次，不该占着整个第一屏");
  if (shut.shown !== 0)
    problems.push(`配桌收着，里面却还有 ${shut.shown} / ${shut.inside} 个元素渲染着`);

  // 摘要行上那串值：四项逐项与页面在按的那份对得上。
  const digest = (await page.getByTestId("table-setup-digest").textContent()).trim();
  const wire = await page.getByTestId("table-rules").getAttribute("data-rules");
  const [length, aka, kuitan] = (wire ?? "//").split("/");
  const want = [
    length === "tonpuusen" ? "东风战" : "半庄战",
    `赤宝牌${aka === "on" ? "有" : "无"}`,
    `食断${kuitan === "on" ? "有" : "无"}`,
  ];
  for (const piece of want)
    if (!digest.includes(piece)) problems.push(`摘要行上没写「${piece}」（它写的是「${digest}」）`);
  if (!/种子\s*\S+/.test(digest)) problems.push(`摘要行上没写种子（它写的是「${digest}」）`);

  // 展开记号得看得出来，且开合两态不同。
  // 给 summary 设 `display` 会把浏览器默认那枚三角去掉，于是那一行看着不像可点的
  // （票 116 的头一版真这么坏过，是看截图看出来的，不是闸门报的）。
  const marker = async () =>
    await page
      .getByTestId("table-setup-summary")
      .evaluate((el) =>
        getComputedStyle(el.querySelector(".label"), "::after").content.replace(/["']/g, "").trim(),
      );
  const shutMark = await marker();
  if (shutMark === "" || shutMark === "none")
    problems.push("摘要行上没有展开记号：那一行看着就不像可点的");

  // 点得开。
  await page.getByTestId("table-setup-summary").click();
  const open = await state();
  const openMark = await marker();
  if (openMark === shutMark)
    problems.push(`展开记号开合两态一模一样（都是「${shutMark}」）：看不出此刻是开着还是收着`);
  if (!open.open) problems.push("点了摘要行，配桌没开");
  if (open.shown === 0) problems.push("配桌开了，里面却一个元素也没渲染出来");

  // 点得收。
  await page.getByTestId("table-setup-summary").click();
  const again = await state();
  if (again.open) problems.push("再点一下摘要行，配桌收不回去");
  if (again.shown !== 0) problems.push(`收回去了，里面却还有 ${again.shown} 个元素渲染着`);

  // 印的得是**量到的**，不是期望的：头一版这里硬写着「默认收着」，
  // 于是拿写死 `open` 的版本去试时它照旧印「默认收着」——一行会说谎的日志。
  console.log(
    `配桌折叠：一进页面 open=${shut.open}　扇到 ${shut.inside} 个 testId　` +
      `渲染数 ${shut.shown} → 点一下 ${open.shown}（open=${open.open}）` +
      ` → 再点一下 ${again.shown}（open=${again.open}）　` +
      `记号「${shutMark}」→「${openMark}」　摘要行「${digest}」`,
  );
  return problems;
}

export async function verifySetup(lane) {
  const url = await lane.previewUrl();
  const context = await lane.newContext({ acceptDownloads: true });
  const problems = [];

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
    });

    await page.goto(hostPage(url), { waitUntil: "load" });

    // ①’ 配桌那一枚折叠（票 116）。**必须在展开之前验**：默认收着是它的全部意义。
    problems.push(...(await drawerProblems(page)));

    await openSetup(page);

    // ① 默认那一桌：没拨过任何开关（这个 context 的 localStorage 是空的）。
    const fresh = await onPage(page);
    if (fresh.rules !== "tonpuusen/on/on") {
      problems.push(
        `一进页面那一桌印着「${fresh.rules}」，默认该是 tonpuusen/on/on（天凤那份预设）`,
      );
    }
    if (fresh.pending !== "false") problems.push("一进页面就写着「按重开才生效」，可什么都还没拨");

    // ② 打完一整场东风战。
    const tonpuu = await playAndExport(page, problems, "默认那一桌");
    const tonpuuKyokus = kyokus(tonpuu);
    const tonpuuAka = akadoraSeen(tonpuu);

    if (fromPaifu(tonpuu) !== fresh.rules) {
      problems.push(`页面上印着「${fresh.rules}」，导出的牌谱里却是「${fromPaifu(tonpuu)}」`);
    }
    const southern = tonpuuKyokus.filter((each) => each.startsWith(`${NAN}-`));
    if (southern.length > 0) {
      problems.push(`东风战打出了南场：${southern.join(" / ")}（局数序列该只有东 1 到东 4）`);
    }
    if (!tonpuuKyokus.includes(`${TON}-4`)) {
      problems.push(`东风战没打到东 4 局（打的是 ${tonpuuKyokus.join(" / ")}）`);
    }
    // 阳性对照：⑤ 那条「一张赤牌都没有」得有机会开口，这里先证明它数得出东西来。
    if (tonpuuAka === 0) {
      problems.push("赤宝牌开着，打完一整场却一张赤牌都没进事件流——那条「一张都没有」等于永远为真");
    }
    console.log(
      `默认那一桌：${fromPaifu(tonpuu)}　${tonpuuKyokus.length} 局（${tonpuuKyokus.join(" ")}）　事件流里赤牌 ${tonpuuAka} 张`,
    );

    // ③ 三项都拨到另一边，**先不按重开**：这一桌必须还按老规则算。
    await openSetup(page);
    await page.getByTestId("table-length-hanchan").click();
    await page.getByTestId("table-akadora-off").click();
    await page.getByTestId("table-kuitan-off").click();

    // 那一句里的勾由**这一项自己**的成败决定（票 106）。
    const halfwayMark = markerSince(problems);
    const picked = await onPage(page);
    if (picked.pending !== "true") problems.push("三项都拨过了，页面却没说「按重开才生效」");
    if (picked.rules !== fresh.rules) {
      problems.push(
        `还没按重开，页面上这一桌的规则就从「${fresh.rules}」变成了「${picked.rules}」`,
      );
    }

    const halfway = await exportPaifu(page);
    if (fromPaifu(halfway) !== fresh.rules) {
      problems.push(
        `拨完开关（没重开）导出的牌谱写着「${fromPaifu(halfway)}」，而这一场是按「${fresh.rules}」打的`,
      );
    }
    console.log(
      `拨完三项没按重开：页面「${picked.rules}」、牌谱「${fromPaifu(halfway)}」，都还是老规则 ${halfwayMark()}`,
    );

    // ④ 按「重开」那一刻才换。
    await page.getByTestId("table-restart").click();
    const restarted = await onPage(page);
    if (restarted.rules !== "hanchan/off/off") {
      problems.push(`按了重开，页面上却还印着「${restarted.rules}」`);
    }
    if (restarted.pending !== "false") problems.push("按了重开，页面却还说「按重开才生效」");

    // ⑤ 打完一整场半庄。
    const hanchan = await playAndExport(page, problems, "半庄那一桌");
    const hanchanKyokus = kyokus(hanchan);
    const hanchanAka = akadoraSeen(hanchan);

    if (fromPaifu(hanchan) !== "hanchan/off/off") {
      problems.push(
        `重开之后导出的牌谱写着「${fromPaifu(hanchan)}」，而页面上拨的是 hanchan/off/off`,
      );
    }
    if (hanchan.ruleset.akadora.length > 0) {
      problems.push(`关掉了赤宝牌，牌谱的 ruleset.akadora 里却还有 ${hanchan.ruleset.akadora}`);
    }
    if (hanchanAka !== 0) {
      problems.push(`关掉了赤宝牌，事件流里却出现了 ${hanchanAka} 张赤牌`);
    }
    if (hanchan.ruleset.kuitan !== false)
      problems.push("关掉了食断，牌谱的 ruleset.kuitan 却是 true");
    if (!hanchanKyokus.some((each) => each.startsWith(`${NAN}-`))) {
      problems.push(`半庄打完了却一局南场都没有（打的是 ${hanchanKyokus.join(" / ")}）`);
    }
    if (!hanchanKyokus.includes(`${NAN}-4`)) {
      problems.push(`半庄没打到南 4 局（打的是 ${hanchanKyokus.join(" / ")}）`);
    }
    console.log(
      `重开之后：${fromPaifu(hanchan)}　${hanchanKyokus.length} 局（${hanchanKyokus.join(" ")}）　事件流里赤牌 ${hanchanAka} 张`,
    );

    // ⑥ 三项落 localStorage：把这一页重新打开一次，拨到的还在。
    const storedMark = markerSince(problems);
    const stored = await page.evaluate(() =>
      ["length", "akadora", "kuitan"].map(
        (key) => `${key}=${localStorage.getItem(`janpo.rules.${key}`)}`,
      ),
    );
    await page.goto(hostPage(url), { waitUntil: "load" });
    await openSetup(page);
    const reopened = await onPage(page);
    if (reopened.rules !== "hanchan/off/off") {
      problems.push(`重新打开这一页，拨到的三项没了：印着「${reopened.rules}」`);
    }
    if (reopened.pending !== "false") {
      problems.push("重新打开这一页，它立刻就说「按重开才生效」——存下来的与开出来的对不上");
    }
    console.log(
      `localStorage 里：janpo.rules.{${stored.join(", ")}}　重新打开还在 ${storedMark()}`,
    );
  } finally {
    await context.close();
  }

  if (problems.length > 0) return failure("配桌那三项开关没过：", problems);

  console.log("对局长度 / 赤宝牌 / 食断：拨得动、按重开才生效、牌谱里跟着变 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  await runStandalone((lane) => verifySetup(lane));
}
