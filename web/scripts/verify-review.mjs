// **复盘：逐手对照标注**那一道闸门（票 90；spec 的 story 34）。
// 全程本机（页面 + 一份牌谱），**一个字节都不出网**，因此它进 CI。
//
// 第一程（真人坐座位 0、三家自带 bot，页面内驱动打完一整场东风战）：
//   ① **对局中一条标注都没有**（阴性对照，票面点名）：打到几十手时整页上
//      `table-review` 与 `table-review-hint` **一个都不在 DOM 里**——对局中给出
//      「换打会怎样」就是作弊（那属于 Assisted 档，票 89）；
//   ② 终局之后：面板在，主语是他（`data-review-seat=0`），**每一手都有一条**
//      ——条数与手序逐个等于**引擎另一条路走出来的那一份**（`ReviewCheck.expected`：
//      `Replay.traceOfPaifu` + `GameState.step`，而页面那一侧走的是 `Table.replay` 的帧）；
//   ③ **那几个数与引擎直接算的逐字相同**：向听（打之前 / 打之后 / 进退向）、
//      有效牌（枚数 / 种数）、危险度（档位 / 名次）——逐行逐项对拍；
//   ④ **更好的候选**：闸门照**规则**自己从引擎的逐张试打表里推一遍
//      （帕累托占优：向听不更差、有效牌不更少、危险度不更高，且至少一项更好），
//      页面列出来的那几张必须真的更好、数也对得上；说「这一手是当时的最优之一」的那几手，
//      引擎的试打表里必须真的一张更好的都没有；
//   ⑤ **点某一手**：牌桌摆出那一刻的快照（Live 那一侧没有时间轴），按「回到原处」回得来。
//
// 第二程（首页 `/` 那份 Demo 回放，**模型席也能看**）：
//   ⑥ 默认上帝视角：复盘**没有主语**——面板不在，只有那一句「坐到某一席就看得了」；
//   ⑦ 点一下座位 1 的视角：那一席的逐手复盘出来了，条数与那几个数同样与引擎对拍；
//   ⑧ **点某一手 → 游标跳过去 → 关掉回原处**（票 86 立的回程规矩）：轴只有票 75 那一根；
//   ⑨ **强 AI 那一行留位置但不占位**（票 93）：整块面板里 `data-review-strong` 一个都没有，
//      也没有任何一行写着「暂无」。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:review`
// 它也是 `verify-browser.mjs` 里的一趟（十七趟共用一个浏览器与一台服务器）。
//
// 选项：--budget ms（页面内驱动的时限）、--peek N（走多少手之后做①那一条）。
//
// **把①按红的做法**（判据 1，票面点名）：把 `Review.settled` 那道判断去掉
// （例如 `let settled (_: TableModel) : bool = true`），重编 Fable 再跑这一趟。红的原文在报告 90 里。

import { readFileSync } from "node:fs";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { plantSeating } from "./seating.mjs";
import { hostPage, retryOnReload } from "./serve.mjs";

/** 真人坐这一席（东 1 局的亲：页面一打开就轮到他）。 */
const ME = 0;

/** 第二程看的是这一席（**模型席也能看**：Demo 那份牌谱四席都是模型）。 */
const WATCHED = 1;

/**
 * 一个元素的 `data-*`，**没有就是 `null`**。
 *
 * 不用 `getByTestId(...).getAttribute(...)`：那一条在元素不存在时会**干等 30 秒再抛**，
 * 而这一道闸门的契约是交一份失败清单（合并跑那个入口要先关浏览器、再逐道汇报）——
 * 抛出去会把十七趟一起搞挂（票 86/87/88 各写下过同一课）。
 */
function attr(page, testId, name) {
  return page.evaluate(
    ({ testId, name }) =>
      document.querySelector(`[data-testid="${testId}"]`)?.getAttribute(name) ?? null,
    { testId, name },
  );
}

/**
 * 点一枚按钮，**它不在就报一条失败而不是抛出去**。
 *
 * `getByTestId(...).click()` 在元素不存在时会**干等 30 秒再抛**，而这一道闸门的契约是
 * 交一份失败清单（合并跑那个入口要先关浏览器、再逐道汇报）——抛出去会把十七趟一起搞挂。
 * **这一条是被破坏实验逃出来的**：把「点一条标注」改成 `CursorMoved` 那一次，
 * 「回到原处」那一枚根本没出现，于是这一趟抛了个 `TimeoutError`（票 86/87/88 各写下过同一课）。
 */
async function clicked(page, testId, problems, why) {
  if ((await page.getByTestId(testId).count()) === 0) {
    problems.push(`页面上没有 [data-testid="${testId}"]：${why}`);
    return false;
  }
  await page.getByTestId(testId).click();
  return true;
}

/** 页面上那几行复盘标注，连同它们身上的每一个数。 */
function panel(page) {
  return page.evaluate(() => {
    const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
    const section = at("table-review");
    if (section === null) {
      return { present: false, hint: at("table-review-hint") !== null };
    }

    const num = (node, name) => {
      const raw = node.getAttribute(name);
      return raw === null || raw === "" ? null : Number.parseInt(raw, 10);
    };

    const notes = [...section.querySelectorAll("[data-review-turn]")].map((row) => {
      const advice = row.querySelector("[data-review-advice]");
      const said = (selector) => row.querySelector(selector)?.textContent?.trim() ?? "";
      const list = (name) => {
        const raw = advice?.getAttribute(name) ?? "";
        return raw === "" ? [] : raw.split(" ");
      };

      return {
        turn: num(row, "data-review-turn"),
        frame: num(row, "data-review-frame"),
        kind: row.getAttribute("data-review-kind"),
        shanten: num(row, "data-review-shanten"),
        after: num(row, "data-review-shanten-after"),
        delta: num(row, "data-review-delta"),
        ukeire: num(row, "data-review-ukeire"),
        kinds: num(row, "data-review-ukeire-kinds"),
        danger: row.getAttribute("data-review-danger"),
        rank: num(row, "data-review-danger-rank"),
        open: row.getAttribute("data-review-open"),
        advice: advice === null ? null : advice.getAttribute("data-review-advice"),
        candidates: list("data-review-candidates"),
        gains: list("data-review-gains").map((each) => Number.parseInt(each, 10)),
        headline: said(".review-jump"),
        figures: said(".review-figures"),
        adviceText: advice?.textContent?.trim() ?? "",
      };
    });

    return {
      present: true,
      hint: at("table-review-hint") !== null,
      seat: num(section, "data-review-seat"),
      said: num(section, "data-review-notes"),
      open: section.getAttribute("data-review-open"),
      strong: section.querySelectorAll("[data-review-strong]").length,
      text: section.textContent ?? "",
      notes,
    };
  });
}

/**
 * 引擎那一份（**另一条路**）：把一份牌谱原文喂给 `ReviewCheck.expected`。
 *
 * 它走 `Replay.traceOfPaifu` + `GameState.step`，与页面那一侧的 `Table.replay` 各走各的
 * ——两条路各自到达同一手，再各自向同一个引擎问那份脚手架（判据 6：右侧不许是同一个实现）。
 */
function expectedFrom(page, text, seat) {
  return retryOnReload(() =>
    page.evaluate(
      async ({ text, seat }) => {
        // 相对页面地址 import：vite 的 base 可配（JANPO_BASE），写死 "/src/…" 一改 base 就 404。
        const check = await import("./src/generated/ReviewCheck.js");
        return JSON.parse(check.expected(text, seat));
      },
      { text, seat },
    ),
  );
}

/**
 * **这一张比你打的那张更好吗**——照规则写的那一份（判据 8：期望值取自规则，
 * 不取自被检查那句话的来源）。三项：向听不更差、有效牌不更少、危险度不更高，
 * 且至少一项严格更好；**算不出来的那一项不参与比较**（拿 null 当 0 就是编一个数）。
 */
function dominates(played, candidate) {
  const both = (left, right) => left !== null && right !== null;
  const notWorse =
    candidate.delta <= played.delta &&
    (!both(candidate.ukeire, played.ukeire) || candidate.ukeire >= played.ukeire) &&
    (!both(candidate.order, played.order) || candidate.order <= played.order);
  const better =
    candidate.delta < played.delta ||
    (both(candidate.ukeire, played.ukeire) && candidate.ukeire > played.ukeire) ||
    (both(candidate.order, played.order) && candidate.order < played.order);
  return notWorse && better;
}

/**
 * 页面那几行与引擎那一份逐项对拍。返回失败清单（空 = 绿）与执行次数。
 *
 * **两件事一起量**（判据 3）：对拍本身，以及「这几条断言各开口了几次」——
 * 一条永远执行不到的断言与一条从不失败的断言，危害相同。
 */
function compare(where, shown, expected) {
  const problems = [];
  const counts = { rows: 0, ukeire: 0, danger: 0, better: 0, best: 0, gain: 0 };

  if (shown.notes.length !== expected.notes.length) {
    problems.push(
      `${where}：页面上有 ${shown.notes.length} 条标注，而引擎说这一席落定了 ${expected.notes.length} 手` +
        "：每一手都该有一条（票 90 的第一条验收）",
    );
    return { problems, counts };
  }
  if (shown.said !== expected.notes.length) {
    problems.push(`${where}：面板说有 ${shown.said} 条，实际画了 ${shown.notes.length} 条`);
  }

  for (const [index, note] of shown.notes.entries()) {
    const each = expected.notes[index];
    const at = `${where} 第 ${each.turn} 手（${each.label}）`;

    if (note.turn !== each.turn) {
      problems.push(`${at}：页面上那一条写着第 ${note.turn} 手——手序对不上，两边说的不是同一手`);
      continue;
    }
    if (note.kind !== each.kind) {
      problems.push(`${at}：页面说这一手是 ${note.kind}，引擎说是 ${each.kind}`);
    }

    counts.rows += 1;

    const same = (name, mine, theirs) => {
      if (mine !== theirs) {
        problems.push(`${at}：${name} 页面写 ${mine}，引擎算的是 ${theirs}`);
      }
    };

    same("打之前的向听", note.shanten, each.shanten);
    same("打之后的向听", note.after, each.after);
    same("进退向", note.delta, each.delta);
    same("有效牌枚数", note.ukeire, each.ukeire);
    same("有效牌种数", note.kinds, each.kinds);
    same("危险度档位", note.danger === "" ? null : note.danger, each.danger);
    same("危险度名次", note.rank, each.rank);

    if (each.ukeire !== null) counts.ukeire += 1;
    if (each.danger !== null) counts.danger += 1;

    // 「更好的候选」那一栏：照规则自己推一遍。
    if (each.kind !== "dahai") {
      if (note.advice !== null) {
        problems.push(`${at}：这一手没打牌，却摆出了「换打会怎样」那一栏`);
      }
      continue;
    }

    const yours = each.trials.find((trial) => trial.pai === each.pai);

    if (yours === undefined) {
      problems.push(`${at}：引擎的试打表里找不到他打的那一张`);
      continue;
    }

    const rule = each.trials.filter((trial) => dominates(yours, trial));

    if (rule.length === 0) {
      counts.best += 1;
      if (note.advice !== "best") {
        problems.push(
          `${at}：引擎的试打表里一张更好的都没有，页面却列了 ${note.candidates.join("、")}`,
        );
      }
      if (!note.adviceText.includes("最优之一")) {
        problems.push(`${at}：没有更好的就该明说，页面上那一栏写的是「${note.adviceText}」`);
      }
      continue;
    }

    counts.better += 1;
    if (note.advice !== "better") {
      problems.push(
        `${at}：引擎说还有 ${rule.length} 张更好的（${rule.map((trial) => trial.pai).join("、")}），` +
          `页面那一栏却写着「${note.adviceText}」`,
      );
      continue;
    }
    if (note.candidates.length !== Math.min(3, rule.length)) {
      problems.push(
        `${at}：更好的有 ${rule.length} 张，页面列了 ${note.candidates.length} 条（该是 ${Math.min(3, rule.length)} 条）`,
      );
    }

    const best = Math.max(...rule.map((trial) => trial.ukeire ?? 0));

    for (const [rank, pai] of note.candidates.entries()) {
      const trial = each.trials.find((trial) => trial.pai === pai);
      if (trial === undefined) {
        problems.push(`${at}：页面列的「${pai}」根本不在引擎的试打表里`);
        continue;
      }
      if (!dominates(yours, trial)) {
        problems.push(
          `${at}：页面把「${pai}」当成更好的，可它在引擎的数上并不更好` +
            `（向听 ${trial.delta} vs ${yours.delta}、有效牌 ${trial.ukeire} vs ${yours.ukeire}、危险度 ${trial.order} vs ${yours.order}）`,
        );
      }
      const gain = note.gains[rank];
      if (trial.ukeire !== null && yours.ukeire !== null && gain !== trial.ukeire - yours.ukeire) {
        problems.push(
          `${at}：「${pai}」页面说多 ${gain} 枚，引擎算的是 ${trial.ukeire - yours.ukeire} 枚`,
        );
      }
      if (rank === 0) {
        if ((trial.ukeire ?? 0) !== best) {
          problems.push(
            `${at}：头一条该是有效牌最多的那张（${best} 枚），页面摆的是「${pai}」（${trial.ukeire} 枚）`,
          );
        }
        if (gain >= 10) counts.gain += 1;
      }
    }
  }

  return { problems, counts };
}

/**
 * **页面内**把这一桌往前推（票 56 那条教训：每手一次 playwright 往返太贵）。
 *
 * 代点极简：有「过」就过，有牌可打就点手牌行里的**头一张**，否则点那一排头一枚，
 * 都没有就按「单步」（一局打完就点「下一局」）。**它故意不讲牌理**——正因为如此，
 * 一整场下来会有几十手「本来还有更好的」，第④条那几个数才有东西可核。
 *
 * **这一手落没落下去看的是一个签名**（票 88 那一课）：连着两次「过」时
 * 「上一手」那句话一个字不变，因此把真人那一行的几个计数一起算进去。
 */
function drive(page, { limit, budgetMs }) {
  return page.evaluate(
    async ({ limit, budgetMs }) => {
      const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
      const all = (selector) => [...document.querySelectorAll(selector)];
      const line = () => at("table-human");
      const signature = () =>
        [
          at("table-latest")?.textContent?.trim() ?? "",
          line()?.getAttribute("data-human") ?? "",
          line()?.getAttribute("data-human-passes") ?? "",
          all("[data-dahai-id]").length,
          all("[data-human-action-id]").length,
        ].join("|");
      const breathe = (attempt) =>
        attempt < 8
          ? Promise.resolve()
          : new Promise((done) => setTimeout(done, attempt < 64 ? 0 : 8));

      const changed = async (before) => {
        const deadline = performance.now() + budgetMs;
        let attempt = 0;
        while (signature() === before) {
          if (performance.now() > deadline) return false;
          await breathe(attempt);
          attempt += 1;
        }
        return true;
      };

      let steps = 0;
      let stuck = null;

      for (let move = 0; move < limit; move += 1) {
        if (at("table-result") !== null) break;

        const before = signature();
        const buttons = all("[data-human-action-id]");
        const pass = buttons.find((node) => node.dataset.humanAction === "none");
        const tiles = all("[data-dahai-id]");
        const step = at("table-step");
        const target = pass ?? tiles[0] ?? buttons[0] ?? step;

        if (target === null || target === undefined || target.disabled) {
          // 这一局打完了：接着开下一局（终局那一刻「下一局」也是灰的，上面那一句先跳出去）。
          const next = at("table-next");
          if (next === null || next.disabled) {
            stuck = "既点不了牌、也开不了下一局";
            break;
          }
          next.click();
        } else {
          target.click();
        }

        steps += 1;
        if (!(await changed(before))) {
          stuck = `第 ${steps} 步点下去之后牌桌没走动`;
          break;
        }
      }

      return { steps, stuck, ended: at("table-result") !== null };
    },
    { limit, budgetMs },
  );
}

/** 第一程：真人坐一席，打完一整场，终局之后才有复盘。 */
async function humanLeg(lane, { budgetMs, peek }) {
  const problems = [];
  const origin = await lane.devUrl();
  const context = await lane.newContext({ acceptDownloads: true });
  const page = await context.newPage();

  try {
    await plantSeating(page, { profiles: [], seats: [{ choice: "human" }, {}, {}, {}] });
    await page.goto(hostPage(origin), { waitUntil: "domcontentloaded" });
    await page.getByTestId("table-board").waitFor({ timeout: 15000 });

    // ① 对局中：先走几十步，再看整页上有没有复盘（**阴性对照**）。
    const midway = await drive(page, { limit: peek, budgetMs });
    const latest = (await page.getByTestId("table-latest").textContent()).trim();
    console.log(`对局中：走了 ${midway.steps} 步，${latest}`);

    if (midway.steps < peek || midway.ended) {
      problems.push(
        `对局中那一条什么都没证明：只走了 ${midway.steps} 步（卡在：${midway.stuck ?? "没卡"}）` +
          `${midway.ended ? "，而且已经终局了" : ""}`,
      );
    }
    if (latest === "") {
      problems.push("对局中那一刻牌桌上一手都没落定：阴性对照量的不能是一桌没开起来的空局");
    }

    const during = await panel(page);
    if (during.present) {
      problems.push(
        `对局还没打完，复盘面板就在 DOM 里了（${during.notes.length} 条标注）：` +
          "对局中给出「换打会怎样」就是作弊——那是 Assisted 档的事（票 89），复盘只在终局之后",
      );
    }
    if (during.hint) {
      problems.push("对局还没打完，页面上却已经在说「坐到某一席就看得到复盘」");
    }
    if (!during.present && !during.hint) {
      console.log("对局中：复盘那一块整个不在 DOM 里 ✓（阴性对照）");
    }

    // ② 打完一整场。
    const rest = await drive(page, { limit: 4000, budgetMs });
    if (!rest.ended) {
      problems.push(`这一场没走到终局（又走了 ${rest.steps} 步，卡在：${rest.stuck ?? "没卡"}）`);
      return problems;
    }
    console.log(`终局：又走了 ${rest.steps} 步`);

    const shown = await panel(page);
    if (!shown.present) {
      problems.push("这一场打完了，复盘面板却不在：终局之后立刻该有东西看（票 90 的零外部依赖）");
      return problems;
    }
    if (shown.seat !== ME) {
      problems.push(`复盘的主语该是真人那一席（座位 ${ME}），页面写的是座位 ${shown.seat}`);
    }

    // ③④ 与引擎另一条路算出来的逐项对拍。牌谱走「导出」那条真路（与票 26 同一份字节）。
    const [download] = await Promise.all([
      page.waitForEvent("download", { timeout: 30000 }),
      page.getByTestId("table-export").click(),
    ]);
    const text = readFileSync(await download.path(), "utf8");
    const expected = await expectedFrom(page, text, ME);

    if (expected.error) {
      problems.push(`引擎读不动这一桌导出的牌谱：${expected.error}`);
      return problems;
    }

    const { problems: mismatches, counts } = compare("真人那一桌", shown, expected);
    problems.push(...mismatches);
    console.log(
      `真人那一桌：${counts.rows} 条逐项对拍 ✓（有效牌 ${counts.ukeire} 条、危险度 ${counts.danger} 条、` +
        `更好的候选 ${counts.better} 条、最优之一 ${counts.best} 条、差 10 枚以上 ${counts.gain} 条）`,
    );

    if (counts.better === 0) {
      problems.push("一整场下来一条「更好的候选」都没出现：那一栏的断言这一趟等于没跑（判据 3）");
    }
    if (counts.best === 0) {
      problems.push("一整场下来一条「这一手是当时的最优之一」都没有：那一句的断言这一趟等于没跑");
    }
    if (counts.danger === 0) {
      problems.push("一整场下来没有一条标注带危险度：这一桌没人立直也没人副露？");
    }

    // ⑤ 点某一手：牌桌摆出那一刻的快照，按「回到原处」回得来（Live 侧没有时间轴）。
    const jump = shown.notes.find((note) => note.kind === "dahai");
    const before = (await page.getByTestId("table-latest").textContent()).trim();
    const why = "每一条标注都该点得开（票 90 的第三条验收）";

    if (await clicked(page, `review-turn-${jump.turn}`, problems, why)) {
      const snapshot = (await page.getByTestId("table-latest").textContent()).trim();
      const opened = await attr(page, "table-review", "data-review-open");

      if (snapshot === before) {
        problems.push(`点了第 ${jump.turn} 手，牌桌却纹丝不动：「${snapshot}」`);
      }
      if (opened !== String(jump.turn)) {
        problems.push(`点开的该是第 ${jump.turn} 手，面板说的是「${opened}」`);
      }

      if (
        await clicked(page, "review-return", problems, "跳走了就要回得来（票 86 立的回程规矩）")
      ) {
        const back = (await page.getByTestId("table-latest").textContent()).trim();
        if (back !== before) {
          problems.push(
            `按「回到原处」之后牌桌停在「${back}」，点开之前是「${before}」（票 86 的回程）`,
          );
        }
        if ((await page.getByTestId("review-return").count()) !== 0) {
          problems.push("回来了，「回到原处」那一枚却还在：它是「你正在看别处」的标记");
        }
      }
    }
  } finally {
    await context.close();
  }

  return problems;
}

/** 第二程：首页那份 Demo 回放——模型席也能看，而且时间轴真的跟着跳。 */
async function replayLeg(lane) {
  const problems = [];
  const origin = await lane.devUrl();
  const page = await lane.newPage();

  try {
    await page.goto(`${origin}/`, { waitUntil: "domcontentloaded" });
    await page.getByTestId("table-timeline").waitFor({ timeout: 15000 });

    // 首页自动播（票 71）：先在滑块中间**真点一下**（一拖就暂停，票 75）。
    // 这一下同时把回程那一条的“原处”搬到牌谱中间：停在第 0 帧的话，
    // “回得来”与“什么都没发生”在数上分不开。
    const slider = page.getByTestId("table-timeline");
    const box = await slider.boundingBox();
    await slider.click({ position: { x: box.width * 0.5, y: box.height / 2 } });

    // ⑥ 默认上帝视角：复盘没有主语。
    const god = await panel(page);
    if (god.present) {
      problems.push("首页默认是上帝视角，复盘却已经有主语了：复盘的第一个字是「你」");
    }
    if (!god.hint) {
      problems.push("上帝视角下页面一句话都没说：人因此不知道坐下来就看得到复盘");
    }

    // ⑦ 坐到座位 1：那一席的逐手复盘出来了（**复盘不是真人专属**）。
    await page.getByTestId(`table-view-${WATCHED}`).click();
    await page.getByTestId("table-review").waitFor({ timeout: 20000 });
    const shown = await panel(page);

    if (shown.seat !== WATCHED) {
      problems.push(`坐到了座位 ${WATCHED}，复盘的主语却是座位 ${shown.seat}`);
    }

    const text = readFileSync(new URL("../public/demo-paifu.json", import.meta.url), "utf8");
    const expected = await expectedFrom(page, text, WATCHED);
    if (expected.error) {
      problems.push(`引擎读不动首页那份牌谱：${expected.error}`);
      return problems;
    }

    const { problems: mismatches, counts } = compare("首页那份牌谱", shown, expected);
    problems.push(...mismatches);
    console.log(
      `首页座位 ${WATCHED}：${counts.rows} 条逐项对拍 ✓（有效牌 ${counts.ukeire} 条、危险度 ${counts.danger} 条、` +
        `更好的候选 ${counts.better} 条、最优之一 ${counts.best} 条）`,
    );

    // ⑧ 点某一手 → 游标跳过去 → 关掉回原处（票 86 立的回程规矩）。
    const cursor = () => attr(page, "table-timeline", "data-cursor");
    const origin0 = await cursor();
    // 挑一条落在游标**前面**的标注：跳过去才看得出游标真的动了（票 86 那一课：
    // 往前跑的回放早晚会路过原处，往回跳才分得出“回来了”与“又播到了”）。
    const jump = shown.notes
      .filter((note) => note.kind === "dahai" && note.frame + 30 < Number(origin0))
      .at(-1);

    if (jump === undefined) {
      problems.push(`游标停在第 ${origin0} 帧，它前面一条可点的标注都没有：这一条什么都证不了`);
      return problems;
    }

    if (!(await clicked(page, `review-turn-${jump.turn}`, problems, "每一条标注都该点得开"))) {
      return problems;
    }
    const moved = await cursor();
    if (moved !== String(jump.frame)) {
      problems.push(
        `点了第 ${jump.turn} 手，游标却停在第 ${moved} 帧（该是第 ${jump.frame} 帧）：` +
          "轴只有票 75 那一根，点一条标注就是把它搬过去",
      );
    }
    if (moved === origin0) {
      problems.push(`点开之前游标就在第 ${origin0} 帧：这一条什么都没证明，换一条落在别处的标注`);
    }

    const still = await panel(page);
    if (!still.present || still.notes.length !== shown.notes.length) {
      problems.push("跳过去之后复盘面板变了样：复盘读的是整份牌谱，不是游标停在哪儿");
    }

    if (
      !(await clicked(page, "review-return", problems, "跳走了就要回得来（票 86 立的回程规矩）"))
    ) {
      return problems;
    }
    const returned = await cursor();
    if (returned !== origin0) {
      problems.push(
        `按「回到原处」之后游标停在第 ${returned} 帧，点开之前是第 ${origin0} 帧（票 86 立的回程规矩）`,
      );
    } else {
      console.log(
        `点开跳走了、关掉跳回来 ✓（第 ${origin0} 帧 → 第 ${jump.frame} 帧 → 第 ${returned} 帧）`,
      );
    }

    // ⑨ 强 AI 那一行留位置但不占位（票 93）。
    if (shown.strong !== 0) {
      problems.push(
        `面板里已经有 ${shown.strong} 处强 AI 的位置：那是票 93 的事，今天整行不该出现`,
      );
    }
    for (const word of ["暂无", "强 AI", "评分"]) {
      if (shown.text.includes(word)) {
        problems.push(`复盘面板里出现了「${word}」：没有的东西不占位，也不造总分（票 90 的边界）`);
      }
    }
  } finally {
    await page.close();
  }

  return problems;
}

/** 这一道闸门（合并跑与单跑走的是同一段代码）。 */
export async function verifyReview(lane, options = {}) {
  const budgetMs = options.budgetMs ?? 20000;
  const peek = options.peek ?? 60;
  const problems = [];

  problems.push(...(await humanLeg(lane, { budgetMs, peek })));
  problems.push(...(await replayLeg(lane)));

  return problems.length === 0 ? [] : failure("复盘那一道没过：", problems);
}

if (isEntry(import.meta.url)) {
  const args = process.argv.slice(2);
  const numberAt = (name, fallback) => {
    const at = args.indexOf(name);
    return at === -1 ? fallback : Number.parseInt(args[at + 1], 10);
  };

  await runStandalone((lane) =>
    verifyReview(lane, { budgetMs: numberAt("--budget", 20000), peek: numberAt("--peek", 60) }),
  );
}
