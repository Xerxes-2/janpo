// **真人的信息辅助与思考时限**那道闸门（票 89；spec 的 story 33 / 32）。
// 全程本机（页面 + 本地假端点），**一个字节都不出网**，因此它进 CI。
//
// 第一程（**Bare 的阴性对照，这一票最重要的一条**）：真人坐座位 0、裸奔档，
//   走一段对局，然后断言**整页 HTML 里没有一个算好的数**——
//   ① 一个 `data-scaffold-*` 都没有、辅助那一块整块不在 DOM 里；
//   ② 整页文字里不出现「向听 / 有效牌 / 进退向 / 危险度」；
//   ③ **危险度那一枚开关根本不在 DOM 里**（灰掉不算数——一行 DevTools 就平了，
//      票 81 对视角说过同一句话），点不出那一块来；
//   **阳性对照**：同一页把座位 0 那一格拨到「信息辅助」，上面三样当场全回来
//   ——没有它，这一程量的可能只是「那一块整个坏了」。
//
// 第二程（Assisted 的对拍）：真人坐座位 0、信息辅助档，一路走一路核：
//   ④ 辅助那几行的 id **恰好等于他手里点得动的那几张**（多一行是凭空造的，
//      少一行是有数却点不到，人会照着一份对不上的表出牌）；
//   ⑤ 每一行的数都是引擎给的形状（向听 ∈ [-1, 8]、进退向 ≥ 0、有效牌枚数 ≥ 0）；
//   ⑥ **牌桌上那块危险度与辅助那几行是同一份数**：两处各渲一遍，名次的多重集必须相同；
//   ⑦ 拨到「工具搜索」：那一块照旧在，而且那几个数**与信息辅助档逐字相同**
//      （票面原话：真人席选到它时按 Assisted 处理）。
//
// 第三程（时限）：
//   ⑧ **不限时那条路一个行为都不变**：等足够久（3 秒），牌桌一手都不动、`data-human-clock` 是空串；
//   ⑨ 设两秒不动手：`data-human-clock` 真的往下走（一秒之后是 1，人读的那句话也在倒数），
//      到点**自动打了摸切那一张**
//      （河上多的那一张 `data-tsumogiri=true`），页面说得出「时限到点，替你打了」，
//      `data-human-expired` 涨了而 `data-human-passes` 没涨（两种「过」分得开）；
//   ⑩ **牌局必须继续**：再等一会儿，他的下一手照样被时限吃掉——不是卡死在那儿。
//
// 第四程（票 94 那一档在面板上放开了，票 89 顺手带的）：
//   ⑪ 「工具搜索」那一项**在面板上真的选得到**（不是灰的），拨上去之后
//      那一席**用这一档打完一局**：导出的牌谱里那一席每一手有牌可打时都真去查了，
//      而且 0 兜底——这就是「面板上那一下真的传到了 Agent 层」的硬证据。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:assist`
// 它也是 `verify-browser.mjs` 里的一趟。
//
// 选项：--budget ms、--moves N（第一二程各走多少步）。
//
// **把第一程按红的做法**（判据 1，票面点名）：把 `TableState.assists` 改成恒 `true`
// （辅助渲染强行打开），重编 Fable 后跑这一趟——红的原文在报告 89 里。

import { spawn } from "node:child_process";
import { readFileSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { plantSeating, profileChoice } from "./seating.mjs";
import { hostPage } from "./serve.mjs";
import { openSetup, stepTurns } from "./table-drive.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** 真人坐这一席（东 1 局的亲：页面一打开就轮到他）。 */
const ME = 0;

/** 那几个只可能从「算好的数」那一侧来的词（第一程整页文字里一个都不许有）。 */
const COMPUTED = ["向听", "有效牌", "进退向", "危险度"];

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

/** 起一个假端点（第四程要一个会发 `what_if` 工具调用的对手）。 */
async function startEndpoint(origin, extra) {
  const port = await freePort();
  const endpoint = spawn(
    "node",
    ["scripts/fake-endpoint.mjs", "--port", String(port), "--cors", origin, "--quiet", ...extra],
    { cwd: webRoot, stdio: ["ignore", "ignore", "inherit"] },
  );
  return { baseUrl: `http://127.0.0.1:${port}/v1`, endpoint };
}

/** 一个元素的 `data-*`，**没有就是 `null`**（理由见 `verify-human.mjs` 的同名函数）。 */
function attr(page, testId, name) {
  return page.evaluate(
    ({ testId, name }) =>
      document.querySelector(`[data-testid="${testId}"]`)?.getAttribute(name) ?? null,
    { testId, name },
  );
}

/** 一个元素的文字，**没有就是 `null`**。 */
function text(page, testId) {
  return page.evaluate(
    (testId) => document.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? null,
    testId,
  );
}

/**
 * **页面内**把这一桌往前推，一路核辅助那一块（票 56 那条教训：每手一次往返太贵）。
 *
 * 一步：轮到他就点（有按钮先按「过」/ 和了，否则点手牌），不轮到就按「单步」。
 * **每一次轮到他都取一份快照**：辅助那几行的 id 与数、他点得动的那几张、危险度那一块。
 * 出了页面就再也看不到那一刻的局面了，因此比对放在这里、判定放在外面。
 */
function driveHuman(page, { limit, budgetMs }) {
  return page.evaluate(
    async ({ limit, budgetMs }) => {
      const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
      const all = (selector) => [...document.querySelectorAll(selector)];
      const num = (node, name) => {
        const raw = node?.getAttribute(name);
        return raw === null || raw === undefined || raw === "" ? null : Number.parseInt(raw, 10);
      };

      const snapshot = () => {
        const line = at("table-human");
        const block = at("table-human-scaffold");
        return {
          state: line?.getAttribute("data-human") ?? null,
          tier: line?.getAttribute("data-human-tier") ?? null,
          block: block !== null,
          summary: num(block, "data-scaffold-shanten"),
          said: block?.querySelector("h3")?.textContent?.trim() ?? null,
          lines: all("[data-scaffold-id]").map((node) => ({
            id: num(node, "data-scaffold-id"),
            shanten: num(node, "data-scaffold-shanten"),
            delta: num(node, "data-scaffold-delta"),
            ukeire: num(node, "data-scaffold-ukeire"),
            kinds: num(node, "data-scaffold-kinds"),
            danger: num(node, "data-scaffold-danger"),
            said: node.textContent?.trim() ?? "",
          })),
          dahai: [
            ...new Set(
              all("[data-dahai-id]").map((node) => Number.parseInt(node.dataset.dahaiId, 10)),
            ),
          ].sort((left, right) => left - right),
          // 牌桌上那一块危险度（票 25 的面板，另一个渲染器读同一份脚手架）。
          ranks: all('[data-testid^="table-danger-"] p')
            .map((node) => /^第(\d+)位/.exec(node.textContent ?? ""))
            .filter((found) => found !== null)
            .map((found) => Number.parseInt(found[1], 10)),
        };
      };

      const breathe = (attempt) => {
        if (attempt < 8) return Promise.resolve();
        if (attempt < 64) return new Promise((done) => setTimeout(done, 0));
        return new Promise((done) => setTimeout(done, 8));
      };

      const until = async (done) => {
        const deadline = performance.now() + budgetMs;
        let attempt = 0;
        while (!done()) {
          if (performance.now() > deadline) return false;
          await breathe(attempt);
          attempt += 1;
        }
        return true;
      };

      const signature = () => {
        const line = at("table-human");
        return [
          at("table-latest")?.textContent?.trim() ?? "",
          line?.getAttribute("data-human") ?? "",
          line?.getAttribute("data-human-options") ?? "",
          line?.getAttribute("data-human-passes") ?? "",
        ].join("|");
      };

      const checks = [];
      let steps = 0;
      let stuck = null;

      for (let move = 0; move < limit; move += 1) {
        if (at("table-result") !== null) break;

        const view = snapshot();
        const buttons = [...document.querySelectorAll("[data-human-action-id]")];

        if (view.dahai.length > 0 || buttons.length > 0) {
          checks.push(view);

          const before = signature();
          // 和了就和；其余一律「过」；没有按钮就点手里第一张点得动的。
          const wanted =
            buttons.find((node) => node.dataset.humanAction === "hora") ??
            buttons.find((node) => node.dataset.humanAction === "none") ??
            document.querySelector("[data-dahai-id]");

          if (wanted === null || wanted === undefined) {
            stuck = `轮到他（${view.state}）却没有一枚点得下去的`;
            break;
          }

          wanted.click();
          if (!(await until(() => signature() !== before || at("table-fault") !== null))) {
            stuck = "他点下去之后牌桌没走动";
            break;
          }
          continue;
        }

        const step = at("table-step");
        if (step === null) {
          stuck = "页面上没有「单步」那一枚";
          break;
        }
        if (step.disabled) {
          const next = at("table-next");
          if (next === null || next.disabled) break;
          next.click();
          if (!(await until(() => !at("table-step").disabled))) {
            stuck = "点了「下一局」之后牌桌没开动";
            break;
          }
          continue;
        }

        const before = signature();
        step.click();
        steps += 1;
        if (
          !(await until(
            () =>
              signature() !== before ||
              at("table-fault") !== null ||
              at("table-step").disabled ||
              document.querySelector("[data-dahai-id]") !== null ||
              document.querySelector("[data-human-action-id]") !== null,
          ))
        ) {
          stuck = `按了「单步」之后 ${budgetMs}ms 里牌桌没走动`;
          break;
        }
      }

      return { checks, steps, stuck };
    },
    { limit, budgetMs },
  );
}

/**
 * **只看一眼，不出手**：走到他下一次有牌可打的那一刻，把那一刻的样子取回来。
 *
 * 第二程比「同一份局面下两个档位给的东西」时要它：**局面必须是同一个**，
 * 出了手就是另一个局面，那时比出来的相同或不同都不算数。
 */
function peekHuman(page, { budgetMs }) {
  return page.evaluate(
    async ({ budgetMs }) => {
      const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
      const deadline = performance.now() + budgetMs;

      while (document.querySelector("[data-dahai-id]") === null) {
        if (performance.now() > deadline) return null;
        const step = at("table-step");
        if (step === null || step.disabled) return null;
        step.click();
        await new Promise((done) => setTimeout(done, 0));
      }

      const block = at("table-human-scaffold");
      return {
        tier: at("table-human")?.getAttribute("data-human-tier") ?? null,
        block: block !== null,
        said: block?.textContent?.trim() ?? null,
        lines: [...document.querySelectorAll("[data-scaffold-id]")].map((node) =>
          node.textContent?.trim(),
        ),
      };
    },
    { budgetMs },
  );
}

/**
 * **整页 HTML 里那几样算好的数**（票面原话：断言页面上没有任何向听 / 有效牌 / 危险度的数字）。
 *
 * 读的是 `page.content()` 那一整份序列化文档，不是几个选择器捞出来的那几处
 * ——「一个都没有」这句话只有对整页说才算数。**先把 `<style>` 挡掉**：
 * `styles.css` 里可能带着这一块的选择器名（同 `verify-human.mjs` 那份预算的理由）。
 */
async function computedInDocument(page) {
  const html = await page.content();
  const body = html.replace(/<style[\s\S]*?<\/style>/g, "");
  const words = await page.evaluate((wanted) => {
    const said = document.body.innerText ?? "";
    return wanted.filter((word) => said.includes(word));
  }, COMPUTED);

  return {
    attributes: [
      ...new Set([...body.matchAll(/(data-scaffold-[a-z]+)="/g)].map((each) => each[1])),
    ],
    words,
  };
}

/** 第一程：Bare 什么都不给（阴性对照），拨到信息辅助之后当场全回来（阳性对照）。 */
async function bareLane(lane, pageOrigin, options) {
  const { budgetMs, moves, missing, problems } = options;
  const context = await lane.newContext();

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror 第一程] ${error.message}`));

    await plantSeating(page, {
      profiles: [],
      seats: [
        { choice: "human", tier: "bare" },
        { choice: "opinionated" },
        { choice: "opinionated" },
        { choice: "opinionated" },
      ],
    });
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });
    await openSetup(page);

    console.log("");
    console.log(`第一程（阴性对照）：座位 ${ME} 是我自己、裸奔档，其余三家有主见 bot`);

    const tier = await attr(page, "table-human", "data-human-tier");
    if (tier !== "bare") {
      return failure("真人的信息辅助这一道没过：", [
        `座位 ${ME} 拨的是裸奔档，页面却说 data-human-tier=「${tier}」：底下每一条都在空转`,
      ]);
    }

    // **危险度那一枚开关根本不在 DOM 里**（不是灰掉）。
    const toggle = await page.getByTestId("table-danger").count();
    const walked = await driveHuman(page, { limit: moves, budgetMs });

    if (walked.stuck !== null) {
      return failure("真人的信息辅助这一道没过：", [`这一桌推不动了：${walked.stuck}`]);
    }
    if (walked.checks.length < 5) {
      return failure("真人的信息辅助这一道没过：", [
        `走了一段，他只出手 ${walked.checks.length} 次：底下每一条都在空转`,
      ]);
    }

    // **整页那一份要在「轮到他」那一刻取**：不轮到他的时候这一块本来就不画
    // （信息辅助档也一样），那时候抓整页等于什么都没量。
    // 这一条是被红-1 逼出来的：第一版在走完之后随手抓，改坏之后整页那两条一声不吭。
    const parked = await peekHuman(page, { budgetMs });
    if (parked === null) {
      return failure("真人的信息辅助这一道没过：", [
        "这一段里没停在「轮到他出牌」的那一刻：整页那两条因此什么都没证明",
      ]);
    }

    const document = await computedInDocument(page);
    const blocks = walked.checks.filter((each) => each.block).length + (parked.block ? 1 : 0);
    const lines =
      walked.checks.reduce((sum, each) => sum + each.lines.length, 0) + parked.lines.length;

    console.log(
      `　他出手 ${walked.checks.length} 次，辅助那一块出现 ${blocks} 次、辅助行 ${lines} 行、` +
        `危险度那一枚 ${toggle} 枚`,
    );
    console.log(
      `　整页带数的属性：${document.attributes.join("、") || "一个都没有"}　` +
        `整页文字里那几个词：${document.words.join("、") || "一个都没有"}`,
    );

    if (toggle !== 0) {
      missing.push(
        "裸奔档的真人坐在桌边，「危险度」那一枚开关却还在 DOM 里：灰掉不算数——" +
          "危险度是「要算才有的量」（术语表那条「感知 vs 计算」），拨得出来「裸奔」这个对照组就没了",
      );
    }
    if (blocks !== 0) {
      missing.push(
        `裸奔档下辅助那一块出现了 ${blocks} 次（他出手 ${walked.checks.length} 次，另加停下来看的那一刻）`,
      );
    }
    if (lines !== 0) {
      missing.push(`裸奔档下页面上摆出了 ${lines} 行算好的数`);
    }
    if (document.attributes.length > 0) {
      missing.push(
        `裸奔档下整页 HTML 里还有算好的数：${document.attributes.join("、")}` +
          "——「一个坐在牌桌前的人免费得到的一切」里没有它们（CONTEXT.md 的 ScaffoldTier）",
      );
    }
    if (document.words.length > 0) {
      missing.push(
        `裸奔档下整页文字里出现了「${document.words.join("」「")}」：` +
          "那几个词只可能从「要算才有的那几个量」那一侧来",
      );
    }

    // ---- 阳性对照：同一页拨到信息辅助 ----
    await page.getByTestId(`table-seat-${ME}-tier`).selectOption("assisted");
    const after = await driveHuman(page, { limit: 6, budgetMs });
    const shown = after.checks.filter((each) => each.block).length;
    const rows = after.checks.reduce((sum, each) => sum + each.lines.length, 0);
    const toggleBack = await page.getByTestId("table-danger").count();

    console.log(
      `　阳性对照（拨到信息辅助）：他出手 ${after.checks.length} 次，` +
        `辅助那一块 ${shown} 次、辅助行 ${rows} 行、危险度那一枚 ${toggleBack} 枚`,
    );

    if (after.checks.length === 0) {
      missing.push("阳性对照里他一次都没出手：上面那几条因此什么都没证明");
    }
    if (shown === 0 || rows === 0) {
      missing.push(
        `拨到信息辅助之后那一块还是不在（${shown} 次 / ${rows} 行）：` +
          "上面那几条「裸奔什么都不给」因此可能只是那一块整个坏了",
      );
    }
    if (toggleBack !== 1) {
      missing.push(`拨到信息辅助之后「危险度」那一枚没回来（${toggleBack} 枚）`);
    }
  } finally {
    await context.close();
  }
  return null;
}

/** 第二程：Assisted 的那几个数与他点得动的那几张对得上，工具搜索档按它处理。 */
async function assistedLane(lane, pageOrigin, options) {
  const { budgetMs, moves, missing, problems } = options;
  const context = await lane.newContext();

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror 第二程] ${error.message}`));

    await plantSeating(page, {
      profiles: [],
      seats: [
        { choice: "human", tier: "assisted" },
        { choice: "opinionated" },
        { choice: "opinionated" },
        { choice: "opinionated" },
      ],
    });
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });
    await openSetup(page);
    // 危险度那一块拨开：它与辅助那几行读的是同一份脚手架，两处必须对得上。
    await page.getByTestId("table-danger").click();

    console.log("");
    console.log(`第二程（对拍）：座位 ${ME} 是我自己、信息辅助档，危险度那一块拨开`);

    const walked = await driveHuman(page, { limit: moves, budgetMs });
    if (walked.stuck !== null) {
      return failure("真人的信息辅助这一道没过：", [`这一桌推不动了：${walked.stuck}`]);
    }

    let audited = 0;
    let rows = 0;
    let dangerChecks = 0;

    for (const view of walked.checks) {
      // 响应那一手没有「打完之后」可算（引擎的 `Scaffold.Dahai` 是空的）：那一手不核这一条。
      if (view.dahai.length === 0) continue;
      audited += 1;
      rows += view.lines.length;

      const ids = view.lines.map((line) => line.id).sort((left, right) => left - right);
      if (JSON.stringify(ids) !== JSON.stringify(view.dahai)) {
        missing.push(
          `辅助那几行是 [${ids}]，而他点得动的那几张是 [${view.dahai}]：` +
            "多一行是凭空造的，少一行是有数却点不到——人会照着一份对不上的表出牌",
        );
        break;
      }
      if (view.summary === null) {
        missing.push("辅助那一块没说「现在几向听」：进退向正是拿它当基准算的");
        break;
      }

      const strange = view.lines.filter(
        (line) =>
          !(line.shanten >= -1 && line.shanten <= 8) ||
          !(line.delta >= 0) ||
          (line.ukeire !== null && !(line.ukeire >= 0 && line.kinds >= 0)),
      );
      if (strange.length > 0) {
        missing.push(`辅助那几行里有说不通的数：${JSON.stringify(strange[0])}`);
        break;
      }

      // **两处渲染同一份数**：牌桌上那块危险度的名次多重集 = 辅助那几行里的名次多重集。
      if (view.ranks.length > 0) {
        dangerChecks += 1;
        const mine = view.lines
          .map((line) => line.danger)
          .filter((rank) => rank !== null)
          .sort((left, right) => left - right);
        const theirs = [...view.ranks].sort((left, right) => left - right);

        if (JSON.stringify(mine) !== JSON.stringify(theirs)) {
          missing.push(
            `牌桌上那块危险度排的是 [${theirs}]，辅助那几行说的是 [${mine}]：` +
              "两处渲染读的该是同一份脚手架（引擎的 `Scaffold.calculate` 那一次）",
          );
          break;
        }
      }
    }

    console.log(
      `　核了 ${audited} 手、${rows} 行；其中 ${dangerChecks} 手同时摆着牌桌上那块危险度`,
    );

    if (audited < 5) missing.push(`只核了 ${audited} 手：这几条断言基本没开过口（判据 3）`);
    if (rows < 40) missing.push(`只核了 ${rows} 行：这几条断言基本没开过口（判据 3）`);
    if (dangerChecks === 0) {
      missing.push(
        "这一段里一次都没碰上有威胁的家（没人立直、没人副露）：" +
          "「两处渲染同一份数」那一条因此一次都没执行到（判据 3）",
      );
    }

    // ---- ⑦ 工具搜索档按信息辅助处理（**同一个局面**，只换那一格） ----
    const before = await peekHuman(page, { budgetMs });
    await page.getByTestId(`table-seat-${ME}-tier`).selectOption("tool_search");
    const after = await peekHuman(page, { budgetMs });

    console.log(
      `　同一局面换档位：信息辅助 ${before?.lines.length ?? "—"} 行 → 工具搜索 ${after?.lines.length ?? "—"} 行` +
        `（data-human-tier=${after?.tier}）`,
    );

    if (before === null || after === null) {
      missing.push("这一段里没走到「轮到他出牌」的那一刻：⑦ 那一条因此什么都没证明");
    } else {
      if (after.tier !== "tool_search") {
        missing.push(`面板上拨到了工具搜索档，页面却说 data-human-tier=「${after.tier}」`);
      }
      if (!after.block || after.lines.length === 0) {
        missing.push(
          "工具搜索档下真人这一侧一行辅助都没有：票面原话是「选到它时按 Assisted 处理并说明」" +
            "（这一票不给真人做查询面板，但不是把他降回裸奔）",
        );
      }
      if (JSON.stringify(before.lines) !== JSON.stringify(after.lines)) {
        missing.push(
          "同一个局面下，工具搜索档给真人的那几行与信息辅助档不同：" +
            `信息辅助「${before.lines[0]}」／工具搜索「${after.lines[0]}」`,
        );
      }
    }
  } finally {
    await context.close();
  }
  return null;
}

/** 第三程：不限时什么都不发生；设了时限到点真的自动打了，而牌局接着走。 */
async function clockLane(lane, pageOrigin, options) {
  const { missing, problems } = options;
  const context = await lane.newContext();

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror 第三程] ${error.message}`));

    await plantSeating(page, {
      profiles: [],
      seats: [{ choice: "human", tier: "bare" }, {}, {}, {}],
    });
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });
    await openSetup(page);

    console.log("");
    console.log("第三程（时限）：先不限时等 3 秒，再拨成 2 秒不动手");

    // ---- ⑧ 不限时：等足够久，什么都不会自动发生 ----
    const before = {
      latest: await text(page, "table-latest"),
      clock: await attr(page, "table-human", "data-human-clock"),
      expired: await attr(page, "table-human", "data-human-expired"),
      said: await text(page, "table-human"),
    };

    await page.waitForTimeout(3000);

    const after = {
      latest: await text(page, "table-latest"),
      clock: await attr(page, "table-human", "data-human-clock"),
      expired: await attr(page, "table-human", "data-human-expired"),
    };

    console.log(`　不限时：3 秒后「${after.latest}」，data-human-clock=「${after.clock}」`);

    if (before.clock !== "" || after.clock !== "") {
      missing.push(
        `没设时限，页面上却有一记倒计时（前 ${before.clock} / 后 ${after.clock}）：默认必须是不限时`,
      );
    }
    if (after.latest !== before.latest) {
      missing.push(
        `不限时那条路上等了 3 秒，牌桌自己走了一手：「${before.latest}」→「${after.latest}」`,
      );
    }
    if (after.expired !== "0") {
      missing.push(`不限时那条路上「时限代打」的计数是 ${after.expired}（该恒是 0）`);
    }
    if ((before.said ?? "").includes("还剩")) {
      missing.push(`没设时限，真人那一行却在倒数：「${before.said}」`);
    }

    // ---- ⑨ 设两秒不动手：到点自动摸切 ----
    await page.getByTestId(`table-seat-${ME}-clock`).fill("2");
    // `fill` 之后要让 onChange 真的发出去（Feliz 的 `prop.onChange` 挂在 input 事件上）。
    await page.waitForTimeout(50);

    const armed = await attr(page, "table-human", "data-human-clock");
    const kawaBefore = await page.locator(`[data-testid="seat-${ME}-kawa"] [data-pai]`).count();
    // 这一手刚摸进的那一张（牌桌把它空开一格摆着，票 44）：到点打的必须是它。
    const drawn = await page.evaluate(
      (seat) =>
        document
          .querySelector(`[data-testid="seat-${seat}-hand"] .tile.drawn[data-pai]`)
          ?.getAttribute("data-pai") ?? null,
      ME,
    );

    console.log(`　拨到 2 秒：data-human-clock=「${armed}」，刚摸进的是 ${drawn}`);

    if (armed !== "2") {
      missing.push(`把时限拨到 2 秒，页面上那一记倒计时却是「${armed}」`);
    }

    // **倒计时要看得见，而且真的在走**（票面那一条）：一秒之后它该是 1，而不是一直挂着 2。
    await page.waitForTimeout(1200);
    const ticking = await attr(page, "table-human", "data-human-clock");
    const counting = (await text(page, "table-human")) ?? "";
    console.log(`　一秒之后：data-human-clock=「${ticking}」　「${counting}」`);

    if (ticking !== "1") {
      missing.push(`拨到 2 秒、过了一秒之后那一记倒计时是「${ticking}」（该是 1）：它根本没在走`);
    }
    if (!counting.includes("还剩 1 秒")) {
      missing.push(`倒计时只在 data-* 上看得见，人读的那句话里没有：「${counting}」`);
    }

    await page.waitForTimeout(1400);

    const fired = {
      expired: await attr(page, "table-human", "data-human-expired"),
      passes: await attr(page, "table-human", "data-human-passes"),
      said: await text(page, "table-human"),
      latest: await text(page, "table-latest"),
      // 打完那一下就轮到别人了：**倒计时只在轮到自己时走**，这一格该当场空掉。
      clock: await attr(page, "table-human", "data-human-clock"),
    };
    const kawaAfter = await page.locator(`[data-testid="seat-${ME}-kawa"] [data-pai]`).count();
    const landed = await page.evaluate((seat) => {
      const tiles = [...document.querySelectorAll(`[data-testid="seat-${seat}-kawa"] [data-pai]`)];
      const last = tiles.at(-1);
      return last === undefined
        ? null
        : { pai: last.getAttribute("data-pai"), tsumogiri: last.getAttribute("data-tsumogiri") };
    }, ME);

    console.log(
      `　到点之后：自家河 ${kawaBefore} → ${kawaAfter} 张，末尾 ${landed?.pai}（摸切=${landed?.tsumogiri}）` +
        `　代打 ${fired.expired} 次、他自己按「过」${fired.passes} 次`,
    );
    console.log(`　「${fired.said}」`);

    if (kawaAfter !== kawaBefore + 1) {
      missing.push(
        `时限到点了，自家的河却从 ${kawaBefore} 变成 ${kawaAfter} 张（该多一张）：` +
          "到点那一手根本没打出去——「牌局必须继续」于是也就无从谈起",
      );
    }
    if (landed?.tsumogiri !== "true") {
      missing.push(
        `到点打出去的那一张不是摸切（data-tsumogiri=${landed?.tsumogiri}）：` +
          "票面原话是「超时自动摸切」（引擎 `Fallback` 的 Bare 那一支）",
      );
    }
    if (drawn !== null && landed?.pai !== drawn) {
      missing.push(`到点打的是 ${landed?.pai}，而刚摸进的那张是 ${drawn}`);
    }
    if (fired.expired !== "1") {
      missing.push(
        `时限到点代打了一手，页面却记着 ${fired.expired} 次：` +
          "「这一手不是他按的」要在数据里说得出来（票 88 欠下的那一格）",
      );
    }
    if (fired.passes !== "0") {
      missing.push(
        `他一次「过」都没按，页面却记着 ${fired.passes} 次：` +
          "时限代打的那几手与他自己按的那几次必须分得开",
      );
    }
    if (fired.clock !== "") {
      missing.push(
        `他这一手已经打出去了（轮到别人），倒计时那一格却还写着「${fired.clock}」：` +
          "它只许在轮到他的时候走（挂在「轮到他了吗」上，不挂在「牌桌停着」上）",
      );
    }
    if (!(fired.said ?? "").includes("时限到点")) {
      missing.push(`时限替他打了一手，页面上却没说这件事：「${fired.said}」`);
    }

    // ---- ⑩ 牌局必须继续：按下「播放」，他的下一手照样被时限吃掉 ----
    //
    // **要按一下「播放」**：`?table=1` 默认暂停（票 71），他出完手之后别家要等下一记定时器
    // ——那是票 87 就有的播放语义，与时限无关。这里量的是「时限吃掉一手之后这一桌还活着」：
    // 别家真的接着打，而他的下一手照样被吃掉（钟按 `humanTurn` 重新起了一记）。
    const stopped = await text(page, "table-latest");
    await page.getByTestId("table-play").click();
    await page.waitForTimeout(6000);

    const again = await attr(page, "table-human", "data-human-expired");
    const stillFault = await page.getByTestId("table-fault").count();
    const moved = await text(page, "table-latest");

    console.log(
      `　按下「播放」再等 6 秒：代打累计 ${again} 次，牌桌出错 ${stillFault} 处，「${moved}」`,
    );

    if (moved === stopped) {
      missing.push(
        `时限吃掉一手之后按「播放」等了 6 秒，牌桌还停在「${stopped}」：` +
          "时限到点的要害是**牌局必须继续**",
      );
    }
    if (!(Number.parseInt(again ?? "0", 10) > 1)) {
      missing.push(
        `到点打过一手之后又等了 6 秒，代打次数仍是 ${again}：` +
          "他的下一手该照样被时限吃掉（钟按「轮到他了吗」重新起一记）",
      );
    }
    if (stillFault !== 0) {
      missing.push(`时限代打之后牌桌报了错：${await text(page, "table-fault")}`);
    }
  } finally {
    await context.close();
  }
  return null;
}

/** 第四程：「工具搜索」那一项在面板上真的选得到，而且那一席用这一档打完一局。 */
async function toolSearchLane(lane, pageOrigin, options) {
  const { budgetMs, missing, problems } = options;
  const model = await startEndpoint(pageOrigin, ["--what-if", "2"]);
  const context = await lane.newContext({ acceptDownloads: true });

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror 第四程] ${error.message}`));

    await plantSeating(page, {
      profiles: [
        {
          name: "工具档在面板上拨得动吗",
          provider: "custom-openai",
          model: "fake-model",
          base_url: model.baseUrl,
          timeout_ms: "10000",
        },
      ],
      // **从裸奔档起步**：这一程要证的正是「面板上那一下真的把它拨到了工具搜索」。
      seats: [
        { choice: profileChoice("工具档在面板上拨得动吗"), tier: "bare" },
        { choice: "opinionated" },
        { choice: "opinionated" },
        { choice: "opinionated" },
      ],
    });
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });
    await openSetup(page);

    console.log("");
    console.log("第四程（票 94 那一档在面板上放开了）：座位 0 从裸奔拨到工具搜索，再打完一局");

    const disabled = await page.evaluate(() =>
      [...document.querySelectorAll('[data-testid="table-seat-0-tier"] option')].map((node) => [
        node.value,
        node.disabled,
      ]),
    );

    console.log(
      `　脚手架那一格的选项：${disabled.map(([value, off]) => `${value}${off ? "（灰）" : ""}`).join("、")}`,
    );

    const locked = disabled.filter(([, off]) => off).map(([value]) => value);
    if (locked.length > 0) {
      missing.push(
        `脚手架那一格里还有选不了的档位：${locked.join("、")}` +
          "——票 94 已经把工具搜索档做完了（`janpo.seats.<N>.tier=tool_search` 与 demo-game 都拨得动），面板上不该还灰着",
      );
    }

    await openSetup(page);
    await page.getByTestId("table-seat-0-tier").selectOption("tool_search");
    const picked = await page.inputValue('[data-testid="table-seat-0-tier"]');
    if (picked !== "tool_search") {
      return failure("工具搜索档这一道没过：", [
        `面板上选了工具搜索，那一格却停在「${picked}」：底下每一条都在空转`,
      ]);
    }

    // 名牌上写着这一席是哪一档（票 82 的那一句）：拨错了要在看得见的地方看得出来。
    const nameplate = (await attr(page, "seat-0-player", "data-player")) ?? "";
    console.log(`　座位 0 的名牌：「${nameplate}」`);
    if (!nameplate.includes("工具搜索")) {
      missing.push(`拨到工具搜索之后，座位 0 的名牌上没写这件事：「${nameplate}」`);
    }

    // **打完一局**：走到这一局终了（结算面板摆出来）。
    const { walked, stuckAt, closed } = await stepTurns(page, { limit: 400, budgetMs });
    const settled = await page.getByTestId("table-settlement").count();

    console.log(`　走了 ${walked} 手，单步灰了=${closed}，结算面板 ${settled} 块`);

    if (stuckAt !== null) missing.push(`第 ${stuckAt} 手没走动（单手预算 ${budgetMs} ms）`);
    if (!closed || settled === 0) {
      missing.push(
        `那一席用工具搜索档没把这一局打完（单步灰了=${closed}、结算面板 ${settled} 块）：` +
          "「面板上选得到」不等于「选了还打得动」",
      );
    }

    const [download] = await Promise.all([
      page.waitForEvent("download", { timeout: 30000 }),
      page.getByTestId("table-export").click(),
    ]);
    const paifu = JSON.parse(readFileSync(await download.path(), "utf8"));
    const mine = (paifu.decisions ?? []).filter((record) => record.seat === 0);
    const asked = mine.map((record) => {
      const counted = /^你查过 (\d+) 次，还可以再查 (\d+) 次：$/m.exec(record.prompt_tail ?? "");
      return {
        asked: counted === null ? 0 : Number(counted[1]),
        discards: /^- id=\d+：打 /m.test(record.prompt_tail ?? ""),
      };
    });
    const fallbacks = mine.filter((record) => typeof record.fallback === "string");
    const queried = asked.filter((each) => each.asked === 2).length;
    const playable = asked.filter((each) => each.discards).length;

    console.log(
      `　牌谱里座位 0 的记录 ${mine.length} 条：有牌可打 ${playable} 手、真查了两次 ${queried} 手、兜底 ${fallbacks.length}`,
    );

    if (mine.length === 0) {
      missing.push("牌谱里座位 0 一条决策记录都没有：那一席根本没被问过话");
    }
    if (playable === 0) {
      missing.push("座位 0 这一局一手牌都没打过：下面那条「真去查了」在空转");
    }
    if (queried !== playable) {
      missing.push(
        `座位 0 有 ${playable} 手有牌可打，其中只有 ${queried} 手真去查了：` +
          "面板上拨到工具搜索之后，那一档必须真的传到了 Agent 层（否则它就只是一个好看的下拉项）",
      );
    }
    if (fallbacks.length > 0) {
      missing.push(`座位 0 用这一档打出了 ${fallbacks.length} 次兜底：这一档在页面上跑不通`);
    }
  } finally {
    await context.close();
    model.endpoint.kill();
  }
  return null;
}

/** 真人的信息辅助与思考时限那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyAssist(lane, options = {}) {
  const { budgetMs = 30000, moves = 60 } = options;

  // dev server 而不是 preview：与 verify-human 同一个理由（省掉一次 vite build）。
  const pageOrigin = await lane.devUrl();
  const problems = [];
  const missing = [];
  const shared = { budgetMs, moves, missing, problems };

  const early = await bareLane(lane, pageOrigin, shared);
  if (early !== null) return early;

  const second = await assistedLane(lane, pageOrigin, shared);
  if (second !== null) return second;

  await clockLane(lane, pageOrigin, shared);

  const fourth = await toolSearchLane(lane, pageOrigin, shared);
  if (fourth !== null) return fourth;

  if (problems.length > 0) return failure("页面报了错：", problems);
  if (missing.length > 0) return failure("真人的信息辅助与思考时限这一道没过：", missing);

  console.log("");
  console.log("裸奔档：整页 HTML 里一个算好的数都没有，危险度那一枚开关也不在 DOM 里 ✓");
  console.log("拨到信息辅助：那一块与那几行当场回来（阳性对照）✓");
  console.log("辅助那几行 = 他点得动的那几张，牌桌上那块危险度与它是同一份数 ✓");
  console.log("工具搜索档给真人的与信息辅助逐字相同（这一票不给真人做查询面板）✓");
  console.log(
    "不限时那条路上等 3 秒什么都不会发生；拨成 2 秒到点自动摸切、看得见在走，牌局接着走 ✓",
  );
  console.log("面板上选得到工具搜索档，那一席用这一档把一局打完、0 兜底 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };

  await runStandalone((lane) =>
    verifyAssist(lane, {
      budgetMs: Number.parseInt(flag("--budget", "30000"), 10),
      moves: Number.parseInt(flag("--moves", "60"), 10),
    }),
  );
}
