// **四 LLM 同桌**那一屏的闸门（票 73）。全程本机（本地假端点），**一个字节都不出网**，
// 因此它进 CI（硬约束 4：CI 里一个真实请求都不许发）。
//
// 两程，各守票里的一条验收：
//
// 第一程 —— **一份档案坐两席、两席各带各的人格**，外加第三席引用一份「坏 key 的档案」：
//   1. 牌谱的 `names` 里三个 `provider/model` + 一个 bot，**档案的名字一个字都不在**
//      （那是本机的私人叫法，key 更不许在——票 34 那道闸门守着后者）；
//   2. 那两席各留下自己的一条 preamble：**正文不同**（人格跟着座位走）、
//      **渲染版本相同**（模板没换）——这正是 M2 对照实验要的形态，自变量只许有一个；
//   3. 三席各有自己的决策记录（否则上面那几条断言在空转）；
//   4. **断电演习扩到多席**：坏 key 那一席每手兜底，而这一局照样打得完，
//      **兜底只涨在那一席**；
//   5. 删掉一份还被座位引用的档案：那几席退回 bot，**页面把这件事说出来**；
//   6. **四席一眼看得全**（票 83）：四行同时落在一屏里，人格与模板那两块大文本默认收着，
//      **收着也看得出哪一席填过**（座位 0/1/2 有人格、座位 3 没有——这一对就是阴阳对照组），
//      **展开一席不把另外三席顶出屏外**，且展开之后那一格里真是那一席的人格。
//
// 第二程 —— **老配置不许丢**：把上一版的 `janpo.llm.*`（含一把 key）灌进一个干净的
//   浏览器上下文，打开页面，看它迁成「一份档案 + 老配置选中的那一席引用它」，
//   而且**迁移只做一次**（再打开一次，人后来改的东西不会被老键盖回去）。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:seats`
// 它也是 `verify-browser.mjs` 里的一趟（跑道上那几趟共用一个浏览器与一台服务器）。
//
// 选项：--budget ms、--keep <路径>（把导出的牌谱另存一份）。

import { spawn } from "node:child_process";
import { copyFileSync, readFileSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, mark, runStandalone } from "./browser-lane.mjs";
import { personaFor, plantSeating, preambleProblems, profileChoice } from "./seating.mjs";
import { hostPage } from "./serve.mjs";
import { openSetup, stepTurns } from "./table-drive.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/**
 * 两份档案在库里的叫法。**它们绝不该出现在牌谱里**（本机的私人叫法），
 * 因此故意取得一眼认得出、且与 `provider/model` 半点不像。
 */
const GOOD = "能答话的那一份";
const BROKEN = "坏 key 的那一份";

/**
 * 灌进「坏 key 的那一份」档案里的 key。**写死的字面量、全 ASCII**（与票 34 同一条规矩：
 * 断言按字节在导出物里找它，而 JSON 编码器可以把非 ASCII 写成 `\uXXXX`）。
 * 它交给的是一个固定回 401 的假端点，**换不来任何东西**。
 */
const BAD_KEY = "sk-janpo-fake-key-BROKEN-ON-PURPOSE-bing-3e91d7";

/** 借内核要一个空闲端口：跑批是并行的，写死端口迟早撞上另一个工作区（见 `serve.mjs` 那段）。 */
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

/** 起一个本地假端点。`args` 是它自己那几个开关（`--fail 401` 之类）。 */
function startEndpoint(port, origin, args) {
  return spawn(
    "node",
    ["scripts/fake-endpoint.mjs", "--port", String(port), "--cors", origin, "--quiet", ...args],
    { cwd: webRoot, stdio: ["ignore", "ignore", "inherit"] },
  );
}

/**
 * 四席那几行此刻各自的矩形，以及「人格·模板」那一格敲开了没有、摘要上写着什么。
 * **读的是真坐标**（`getBoundingClientRect`）：「一眼看得全」是个几何事实，DOM 里有这四行不算。
 */
async function seatShape(page) {
  const shape = await page.evaluate(() => {
    const rows = [0, 1, 2, 3].map((index) => {
      const row = document.querySelector(`[data-testid="table-seat-${index}"]`);
      const mark = document.querySelector(`[data-testid="table-seat-${index}-detail"]`);
      const rect = row.getBoundingClientRect();
      return {
        seat: index,
        top: Math.round(rect.top),
        bottom: Math.round(rect.bottom),
        open: mark.closest("details").open,
        // 真的渲染出来了吗。**不能拿矩形是否为零代替**：
        // Chrome 新版 `<details>` 收起时用的是 `content-visibility: hidden`
        // 而不是 `display: none`，子元素**点不到、却仍有布局**
        // （实测收起时这四行照旧 h=37、top 不变）。
        shown: row.checkVisibility(),
        custom: mark.getAttribute("data-seat-custom"),
        said: (mark.textContent ?? "").trim(),
      };
    });
    return { rows, viewport: window.innerHeight };
  });

  // 票 116 把配桌收成了默认收起的 `<details>`，而四席在它里面。
  // 收着时这四行**仍有布局**（Chrome 用 `content-visibility: hidden`，不是 `display: none`），
  // 于是下面那条「四席同屏」会拿着**人看不见的那份布局**算出一个漂亮的数。
  // 因此先拒掉没渲染的那一屏：**宁可红，不许空过**。
  //
  // 这一条写坏过一次：头一版量的是「矩形高度是不是 0」，
  // 而 `content-visibility: hidden` 下高度照旧是 37——那条守卫永远不会触发。
  const hidden = shape.rows.filter((row) => !row.shown);
  if (hidden.length > 0)
    throw new Error(
      `座位那几行没渲染出来：${hidden.map((r) => `座位 ${r.seat}`).join("、")}` +
        "——配桌大概还收着（票 116）。这一屏量不出密度，不许当成「都在同一屏里」。",
    );
  return shape;
}

/**
 * **名牌上看得出这一席是谁在打**（票 82 的意见⑤）——这一道核的是**模型席那一半**：
 * 牌桌上那一席的名牌写着**档案名 + 脚手架档位**（bot 席写那两档的中文）。
 *
 * 三条断言，各守一件：
 *
 *   ① 四家的名牌逐字对得上这一桌的坐法（两席同一份档案、一席另一份、一席 bot）；
 *   ② **档案名只活在这一页上**：它绝不该进牌谱（那一条在下面的导出断言里，两处互为对照）；
 *   ③ **档位真的写在名牌上**：把座位 1 拨成「信息辅助」，它那一枚名牌当场跟着变，
 *      而**引同一份档案的座位 0 不动**——两席同档案不同档位正是对照实验的常态，
 *      只写档案名就分不出那两席（拨完拨回去，后面那几程看到的仍是人刚打开时那一屏）。
 */
async function nameplates(page) {
  return await page.evaluate(() =>
    [0, 1, 2, 3].map(
      (seat) =>
        document.querySelector(`[data-testid="seat-${seat}-player"]`)?.textContent?.trim() ?? null,
    ),
  );
}

async function nameplateProblems(page) {
  const problems = [];
  const plate = async (seat) => (await nameplates(page))[seat];

  const wanted = [`${GOOD}・裸奔`, `${GOOD}・裸奔`, `${BROKEN}・裸奔`, "均匀随机"];
  const said = await nameplates(page);
  for (const seat of [0, 1, 2, 3]) {
    if (said[seat] !== wanted[seat])
      problems.push(
        `座位 ${seat} 的名牌上写着「${said[seat]}」，该是「${wanted[seat]}」：` +
          "名牌上要看得出这一席是哪份档案、哪一档在打（票 82）",
      );
  }

  await page.getByTestId("table-seat-1-tier").selectOption("assisted");
  const switched = await plate(1);
  const untouched = await plate(0);
  if (switched !== `${GOOD}・信息辅助`)
    problems.push(
      `把座位 1 拨成「信息辅助」之后名牌仍写着「${switched}」：档位没写在名牌上，` +
        "同一份档案坐两席就分不出哪一席给了多少信息（票 82）",
    );
  if (untouched !== `${GOOD}・裸奔`)
    problems.push(`拨座位 1 的档位把座位 0 的名牌也改成了「${untouched}」：档位是**座位级**的`);
  await page.getByTestId("table-seat-1-tier").selectOption("bare");

  return problems;
}

/**
 * **四席一眼看得全**（票 83 的硬判据）。四条断言，各守一件：
 *
 *   ① 四行同时完整落在视口里（不用滚就读得完「座位 → 选手 → 档位」）；
 *   ② 人格与模板那两块大文本**默认收着**；
 *   ③ 收着也看得出哪一席填过：座位 0/1/2 灌了人格、座位 3 没灌
 *      ——**后一半是阴性对照**，没它的话「记号恒亮」同样能让上一句变绿（判据 3）；
 *   ④ **展开一席不把另外三席顶出屏外**，而且展开之后那一格里真是那一席的人格
 *      （收起来的那一格得真装着东西，不能只是一枚空壳记号）。
 *
 * 量完把它合上：后面那几程看到的应当是与人刚打开时同一个屏。
 */
async function seatDensityProblems(page) {
  const problems = [];
  const { rows, viewport } = await seatShape(page);

  const offscreen = rows.filter((row) => row.top < 0 || row.bottom > viewport);
  if (offscreen.length > 0) {
    problems.push(
      `四席没落在同一屏里（视口 ${viewport} px）：` +
        offscreen.map((row) => `座位 ${row.seat} 在 ${row.top}→${row.bottom}`).join("、"),
    );
  }

  const opened = rows.filter((row) => row.open);
  if (opened.length > 0) {
    problems.push(
      `人格 / 模板那两块大文本默认就摊开着（座位 ${opened.map((row) => row.seat).join("、")}）：四席就一眼看不完了`,
    );
  }

  for (const seat of [0, 1, 2]) {
    if (rows[seat].custom !== "persona") {
      problems.push(
        `座位 ${seat} 灌了人格，收起来的那一格却写着「${rows[seat].said}」（data-seat-custom="${rows[seat].custom}"）`,
      );
    }
  }
  if (rows[3].custom !== "") {
    problems.push(`座位 3 一个字的人格与模板都没灌，记号却说「${rows[3].said}」：那枚记号是恒亮的`);
  }
  if (rows[0].said === rows[3].said) {
    problems.push(
      `填过人格的座位 0 与没填的座位 3 收起来长得一模一样（都写着「${rows[0].said}」）：人看不出哪一席定制过`,
    );
  }

  // ④ 敲开座位 0 那一格：里面真是它自己的人格，而四行仍在同一屏里。
  await page.getByTestId("table-seat-0-detail").click();
  const persona = page.getByTestId("table-seat-0-persona");
  if (!(await persona.isVisible())) {
    problems.push("敲开座位 0 的「人格·模板」，人格那一格却没出来：收起来的东西就敲不开了");
  } else if ((await persona.inputValue()) !== personaFor(0)) {
    problems.push(
      `敲开座位 0 的人格，里面写的是「${await persona.inputValue()}」，该是「${personaFor(0)}」`,
    );
  }

  const expanded = await seatShape(page);
  const pushedOut = expanded.rows.filter((row) => row.top < 0 || row.bottom > expanded.viewport);
  if (pushedOut.length > 0) {
    problems.push(
      `展开座位 0 之后，座位 ${pushedOut.map((row) => row.seat).join("、")} 被顶出了屏外` +
        `（视口 ${expanded.viewport} px，它们在 ${pushedOut.map((row) => `${row.top}→${row.bottom}`).join("、")}）`,
    );
  }
  // 这一句的勾读的就是这一项自己的清单（票 106）：这个函数只管「四席在一屏里」这一件事。
  console.log(
    `四席在一屏里 ${mark(problems)}（视口 ${viewport} px，四行跨 ${rows[0].top}→${rows[3].bottom}；` +
      `展开一席后跨 ${expanded.rows[0].top}→${expanded.rows[3].bottom}），` +
      `记号：${rows.map((row) => `座位 ${row.seat}「${row.said}」`).join("　")}`,
  );

  // 敲回去：后面那几程看到的应当是与人刚打开时同一个屏。
  await page.getByTestId("table-seat-0-detail").click();
  return problems;
}

/** 四 LLM 同桌那一趟。返回的是失败清单（空 = 绿）。 */
export async function verifySeats(lane, options = {}) {
  const { budgetMs = 180000, keep = null } = options;

  const goodPort = await freePort();
  const brokenPort = await freePort();
  // dev server 而不是 preview：与 verify-export / verify-redaction 同一个理由。
  const pageOrigin = await lane.devUrl();
  const goodUrl = `http://127.0.0.1:${goodPort}/v1`;
  const brokenUrl = `http://127.0.0.1:${brokenPort}/v1`;

  const good = startEndpoint(goodPort, pageOrigin, []);
  // 401（认证失败）**不重试**（票 47 的判据）：一手一个请求，断电演习因此跑得快。
  const broken = startEndpoint(brokenPort, pageOrigin, ["--fail", "401"]);
  await new Promise((done) => setTimeout(done, 800));

  const context = await lane.newContext({ acceptDownloads: true });
  const problems = [];

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    // 一份档案坐两席（座位 0 与 1，人格各不同），第三席引用那份坏 key 的档案，座位 3 是 bot。
    await plantSeating(page, {
      profiles: [
        {
          name: GOOD,
          provider: "custom-openai",
          model: "fake-model",
          base_url: goodUrl,
          timeout_ms: "10000",
        },
        {
          name: BROKEN,
          provider: "custom-openai",
          model: "broken-model",
          base_url: brokenUrl,
          api_key: BAD_KEY,
          timeout_ms: "10000",
        },
      ],
      seats: [
        { choice: profileChoice(GOOD), persona: personaFor(0) },
        { choice: profileChoice(GOOD), persona: personaFor(1) },
        { choice: profileChoice(BROKEN), persona: personaFor(2) },
        {},
      ],
    });

    console.log(`页面 ${pageOrigin}　能答话的端点 ${goodUrl}　固定回 401 的端点 ${brokenUrl}`);
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });
    await openSetup(page);

    const readText = async (testId) => (await page.getByTestId(testId).textContent()).trim();
    const readAttr = async (testId, name) => await page.getByTestId(testId).getAttribute(name);

    // 四席绑定**一眼看得全**（票面的硬判据）：四行各自印着自己拨到哪儿、在牌谱里叫什么。
    const bound = [];
    for (const index of [0, 1, 2, 3]) {
      bound.push(
        `座位 ${index}：${await readAttr(`table-seat-${index}`, "data-seat-choice")} → ` +
          `${await readAttr(`table-seat-${index}`, "data-seat-name")}`,
      );
    }
    console.log(bound.join("\n"));
    console.log(`状态线上的四席：${await readAttr("table-agent", "data-seats")}`);
    console.log(`牌桌上四家的名牌：${(await nameplates(page)).join(" / ")}`);

    problems.push(...(await seatDensityProblems(page)));
    problems.push(...(await nameplateProblems(page)));

    // **key 在界面上只出现在档案编辑处**：座位那几行一个 key 输入框都没有。
    const keyFields = await page
      .locator('[data-testid^="table-seat-"] input[type="password"]')
      .count();
    if (keyFields > 0) {
      problems.push(`座位那几行上有 ${keyFields} 个 key 输入框：key 只该出现在档案编辑处`);
    }
    if ((await page.getByTestId("table-profile-key").count()) !== 1) {
      problems.push("档案编辑处没有 key 那一格：那 key 该在哪儿填？");
    }

    // 打完一局：三席轮流被问，坏 key 那一席每手兜底。
    const { walked, stuckAt } = await stepTurns(page, { limit: 400, budgetMs });
    if (stuckAt !== null) problems.push(`第 ${stuckAt} 手没走动：三席模型同桌时对局卡住了`);
    console.log("");
    console.log(`走了 ${walked} 手，${await readText("table-latest")}`);
    console.log(`Agent 状态（data-agent=${await readAttr("table-agent", "data-agent")}）：`);
    console.log(`  ${await readText("table-agent")}`);

    if (await page.getByTestId("table-fault").count()) {
      problems.push(`牌桌停住了：${await readText("table-fault")}`);
    }

    const [download] = await Promise.all([
      page.waitForEvent("download", { timeout: 30000 }),
      page.getByTestId("table-export").click(),
    ]);
    const file = await download.path();
    const text = readFileSync(file, "utf8");
    if (keep) copyFileSync(file, resolve(keep));

    const paifu = JSON.parse(text);
    const names = paifu.events[0]?.names ?? [];
    const records = paifu.decisions ?? [];
    console.log("");
    console.log(
      `牌谱：${download.suggestedFilename()}　${text.length} 字节　names ${names.join(" / ")}`,
    );

    // ① 牌谱里的名字：三席 `provider/model` + 一席 bot；**档案的名字不在里面**。
    const wanted = [
      "custom-openai/fake-model",
      "custom-openai/fake-model",
      "custom-openai/broken-model",
      "random",
    ];
    if (names.join(",") !== wanted.join(",")) {
      problems.push(`牌谱的 names 是「${names.join(",")}」，该是「${wanted.join(",")}」`);
    }
    const shareable = `${download.suggestedFilename()}\n${text}`;
    for (const secret of [GOOD, BROKEN, BAD_KEY, goodUrl, brokenUrl]) {
      if (shareable.includes(secret)) {
        problems.push(`导出物（文件名 + 字节）里出现了「${secret}」：那是本机的东西，不该上路`);
      }
    }

    // ② 两席的 preamble：正文不同、渲染版本相同。
    for (const preamble of paifu.prompting?.preambles ?? []) {
      console.log(
        `  座位 ${preamble.seat} 的 preamble：${preamble.render_version}　${preamble.text.length} 字　` +
          `含人格「${personaFor(preamble.seat)}」= ${preamble.text.includes(personaFor(preamble.seat))}`,
      );
    }
    problems.push(...preambleProblems(paifu, [0, 1]));
    for (const seat of [0, 1]) {
      const mine = (paifu.prompting?.preambles ?? []).find((each) => each.seat === seat);
      if (mine !== undefined && !mine.text.includes(personaFor(seat))) {
        problems.push(`座位 ${seat} 的 preamble 里没有它自己那句人格：人格没跟着座位走`);
      }
    }

    // ③ 三席各有决策记录；④ 兜底**只涨在坏 key 那一席**。
    // **兜底的判据是「有没有那一格」**：牌谱的编码器把空的 `fallback` 整个略掉，
    // 拿 `!== null` 数会把每一条记录都数成兜底（这道闸门第一次跑就是这么红的）。
    const fell = (record) => typeof record.fallback === "string";
    const tally = [0, 1, 2, 3].map((seat) => ({
      seat,
      records: records.filter((record) => record.seat === seat).length,
      fallbacks: records.filter((record) => record.seat === seat && fell(record)).length,
    }));
    console.log(
      `  决策记录 / 兜底：${tally.map((each) => `座位 ${each.seat} ${each.records}/${each.fallbacks}`).join("　")}`,
    );

    for (const seat of [0, 1, 2]) {
      if (tally[seat].records === 0)
        problems.push(`座位 ${seat} 一条决策记录都没有：那一席没被问过话`);
    }
    if (tally[3].records !== 0) problems.push("座位 3 是 bot，却留下了决策记录");
    if (tally[2].fallbacks !== tally[2].records || tally[2].records === 0) {
      problems.push(`坏 key 那一席该每手都兜底，实为 ${tally[2].fallbacks}/${tally[2].records} 手`);
    }
    for (const seat of [0, 1]) {
      if (tally[seat].fallbacks !== 0) {
        problems.push(`座位 ${seat} 的 key 是好的，却兜底了 ${tally[seat].fallbacks} 手`);
      }
    }

    // ⑤ 删掉一份还被座位引用的档案：那几席退回 bot，页面把这件事说出来。
    await page.getByTestId("table-profile-0").click();
    await page.getByTestId("table-profile-delete").click();
    const notice = await page.getByTestId("table-profile-notice").count();
    if (notice === 0) {
      problems.push("删掉一份还被引用的档案，页面一个字都没说（不许静静地变成「没有选手」）");
    } else {
      console.log("");
      console.log(`删掉「${GOOD}」之后页面说：${await readText("table-profile-notice")}`);
    }
    const unbound = await readAttr("table-agent", "data-seats");
    if (unbound !== "random,random,custom-openai/broken-model,random") {
      problems.push(
        `删掉那份档案之后四席该是「random,random,custom-openai/broken-model,random」，实为「${unbound}」`,
      );
    }

    // 再把座位 3 拨成「有主见」。**这一下是给下面那条落盘断言用的**：
    // 上面那几席的变化全是「档案没了」的连带结果，就算绑定压根没写进 localStorage，
    // 重新打开时它们也会因为引用不到而退回均匀随机——那条断言会因此空转（判据 3）。
    await page.getByTestId("table-seat-3-opinionated").click();
    const after = await readAttr("table-agent", "data-seats");

    // 落 localStorage：**另开一个页面**（同一个上下文，localStorage 是共享的）。
    // 不 `reload` 是因为上面那份坐法是 `addInitScript` 灌的，而它每次导航都会再跑一遍
    // ——那样量到的是「灌进去的那一份」，不是「页面自己存下来的那一份」（第一次跑就栽在这儿）。
    const reopened = await context.newPage();
    await reopened.goto(hostPage(pageOrigin), { waitUntil: "load" });
    await openSetup(reopened);
    await reopened.getByTestId("table-llm-panel").waitFor({ timeout: 15000 });
    const stored = await reopened.getByTestId("table-agent").getAttribute("data-seats");
    if (stored !== after) {
      problems.push(`重新打开这一页，四席从「${after}」变成了「${stored}」：坐法没落 localStorage`);
    }
    console.log(`重新打开这一页：四席仍是「${stored}」`);
  } finally {
    await context.close();
    good.kill();
    broken.kill();
  }

  problems.push(...(await verifyMigration(lane)));

  if (problems.length > 0) return failure("四席那一屏没过：", problems);

  console.log("");
  console.log(
    "四 LLM 同桌：一份档案坐两席（人格各不同）、坏 key 那一席只兜自己的底、老配置迁得过来 ✓",
  );
  return [];
}

/**
 * 第二程：**老配置不许丢**（票 73）。
 *
 * 上一版页面把单席配置写在 `janpo.llm.*` 下面（含主人那把真 key）。这一程在一个干净的
 * 上下文里灌一份老键，打开页面，核三件事：档案建出来了、老配置选中的那一席引用着它、
 * 座位级那三项（脚手架 / 人格 / 模板）也跟着落到了那一席上。
 *
 * **迁移只做一次**：改一处再重开，老键不许把人改过的东西盖回去。
 */
async function verifyMigration(lane) {
  const legacyKey = "sk-janpo-fake-key-LEGACY-MIGRATED-ding-6a24b0";
  const pageOrigin = await lane.devUrl();
  const context = await lane.newContext();
  const problems = [];

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    // **只灌老键**，一个新键都不给：迁移那条路就是这么被走到的。
    await page.addInitScript((key) => {
      localStorage.setItem("janpo.llm.seat", "2");
      localStorage.setItem("janpo.llm.provider", "deepseek");
      localStorage.setItem("janpo.llm.model", "deepseek-v4-flash");
      localStorage.setItem("janpo.llm.api_key", key);
      localStorage.setItem("janpo.llm.timeout_ms", "123000");
      localStorage.setItem("janpo.llm.thinking", "medium");
      localStorage.setItem("janpo.llm.tier", "assisted");
      localStorage.setItem("janpo.llm.persona", "老配置里那句人格");
    }, legacyKey);

    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });
    await openSetup(page);
    await page.getByTestId("table-llm-panel").waitFor({ timeout: 15000 });

    console.log("");
    console.log("老配置（janpo.llm.*）迁移那一程：");
    const seats = await page.getByTestId("table-agent").getAttribute("data-seats");
    console.log(`  四席：${seats}`);
    if (seats !== "random,random,deepseek/deepseek-v4-flash,random") {
      problems.push(`老配置选的是座位 2，迁完四席却是「${seats}」`);
    }

    // 「怎么问」那六格：key、超时与思考预算都得原样进档案。
    const planted = await page.evaluate(() =>
      Object.fromEntries(
        Object.keys(localStorage)
          .filter((key) => key.startsWith("janpo.profiles.") || key.startsWith("janpo.seats.2."))
          .map((key) => [key, localStorage.getItem(key)]),
      ),
    );
    console.log(
      `  新键里的档案：${planted["janpo.profiles.0.name"]}　超时 ${planted["janpo.profiles.0.timeout_ms"]}　思考 ${planted["janpo.profiles.0.thinking"]}`,
    );
    console.log(
      `  座位 2：${planted["janpo.seats.2.choice"]}　脚手架 ${planted["janpo.seats.2.tier"]}　人格「${planted["janpo.seats.2.persona"]}」`,
    );

    if (planted["janpo.profiles.0.api_key"] !== legacyKey) {
      problems.push("老配置里那把 key 没迁进档案：主人的 key 会在改版里丢掉");
    }
    if (planted["janpo.profiles.0.timeout_ms"] !== "123000") problems.push("老配置的超时没迁过来");
    if (planted["janpo.profiles.0.thinking"] !== "medium")
      problems.push("老配置的思考预算没迁过来");
    if (planted["janpo.seats.2.tier"] !== "assisted")
      problems.push("老配置的脚手架档位没迁到那一席上");
    if (planted["janpo.seats.2.persona"] !== "老配置里那句人格") {
      problems.push("老配置的人格没迁到那一席上");
    }
    if (await page.getByTestId("table-profile-0").count()) {
      const shown = (await page.getByTestId("table-profile-0").textContent()).trim();
      console.log(`  档案库里那一份在面板上叫「${shown}」`);
    }

    // **迁移只做一次**：把那一席拨回均匀随机、重开这一页，老键不许把它盖回去。
    await page.getByTestId("table-seat-2-random").click();
    await page.reload({ waitUntil: "load" });
    const again = await page.getByTestId("table-agent").getAttribute("data-seats");
    console.log(`  拨回均匀随机再打开一次：${again}`);
    if (again !== "random,random,random,random") {
      problems.push(`迁移又跑了一遍：人拨回去的四席被老键盖成了「${again}」`);
    }
  } finally {
    await context.close();
  }

  return problems;
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };

  await runStandalone((lane) =>
    verifySeats(lane, {
      budgetMs: Number.parseInt(flag("--budget", "180000"), 10),
      keep: flag("--keep", null),
    }),
  );
}
