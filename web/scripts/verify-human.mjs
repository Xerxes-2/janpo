// **真人坐下，把一局打完**那道闸门（票 87；spec 的 story 28 / 29 / 30）。
// 全程本机（页面 + 一个本地假端点），**一个字节都不出网**，因此它进 CI。
//
// 第一程（真人坐座位 0、座位 1 交给一个本地假端点、座位 2/3 是自带 bot）：
//   ① **视角按钮不在 DOM 里**：上帝视角与别席视角**根本不存在**（不是灰掉——票 81 把视角
//      定成了信息闸门，而 `disabled` 一行 DevTools 就平了）；只剩自家那一枚，旁边一句为什么；
//   ② **点自己手里的一张就打出去**：能点的那几张挂着 `data-dahai-id`（引擎给的包内 id），
//      条数与真人那一行的 `data-human-playable` 逐个对得上；
//   ③ **结构性不泄露**（story 29，**这一票最重要的一条**）：对局中把**整页 HTML** 抓下来，
//      里面每一个 `data-pai` 都必须落在「自家手牌 + 四家的河 + 四家的副露 + 宝牌指示牌」
//      这份预算里——他家的手牌**一张都不许有，连 `data-*` 都不许有**；
//      顺带核他家三席的手牌行：一个 `data-pai` 都没有、`data-hand-hidden=true`、
//      画出来的牌背数与 `data-hand-count` 对得上，且里宝牌指示牌不在场上；
//   ④ **气泡对局中一个都没有**（`humanSeated` 生效），而**阳性对照**是 Agent 那条状态线
//      写着座位 1 的模型真说过话——没有它，「0 个气泡」也可能只是模型压根没开口；
//   ⑤ **鸣牌一律自动过，且页面说得出过掉了什么**（`data-human-passes` 与那句中文）；
//   ⑥ **把一整场东风战打完**：终局那一屏在，四家点数之和恒为 100000；
//   ⑦ 终局之后**三样一起松开**：五枚视角按钮回来、座位 1 的气泡回来、那句「视角锁着」没了。
//
// 第二程（`?dev=1` 的阴阳对照，堵挂账 22-A）：
//   ⑧ 真人在座时开 `?dev=1`：曳光弹**不给开**（它把 `start_kyoku` 印在同一张文档里，
//      而 `start_kyoku` 带着四家配牌，且它的种子输入框是任填的）；牌桌照旧活着；
//   ⑨ **阴性对照**：同一条地址、四家没有真人时曳光弹照旧开得了。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:human`
// 它也是 `verify-browser.mjs` 里的一趟（十五趟共用一个浏览器与一台服务器）。
//
// 选项：--budget ms、--peek N（走多少手之后抓那一份整页 HTML）。
//
// **把第③条按红的做法**（判据 1，票面点名）：改**产品代码**把投影换回上帝视角
// （`TableState.viewpoint` 直接返回 `model.Viewpoint`，而 `?table=1` 默认就是上帝视角），
// 重编 Fable 后跑这一趟：第①条与第③条会一起红，红的原文在报告里。

import { spawn } from "node:child_process";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { plantSeating, profileChoice } from "./seating.mjs";
import { hostPage } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** 真人坐这一席（东 1 局的亲：页面一打开就轮到他）。 */
const ME = 0;

/** 模型坐这一席（气泡那两条断言要一个真会说话的对手）。 */
const MODEL = 1;

/** 那份档案在库里的叫法（本机的私人叫法，绝不该出现在牌谱里）。 */
const PROFILE = "真人闸门的对手";

/** 假端点回的那句话。**只可能从它那儿来**：页面里没有任何一处写着它。 */
const SAID = "假端点说：这一手照它的算法只能这么打";

/**
 * 一个元素的 `data-*`，**没有就是 `null`**。
 *
 * 不用 `getByTestId(...).getAttribute(...)`：那一条在元素不存在时会**干等 30 秒再抛**，
 * 而这一道闸门的契约是交一份失败清单（合并跑那个入口要先关浏览器、再逐道汇报）——
 * 抛出去会把十五趟一起搞挂。**这一条是被破坏实验逃出来的**：
 * 把 `humanSeated` 按回恒 false 那一次，它正好把自己抛掉了。
 */
function attr(page, testId, name) {
  return page.evaluate(
    ({ testId, name }) =>
      document.querySelector(`[data-testid="${testId}"]`)?.getAttribute(name) ?? null,
    { testId, name },
  );
}

/** 一个元素的文字，**没有就是 `null`**（理由同 `attr`）。 */
function text(page, testId) {
  return page.evaluate(
    (testId) => document.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? null,
    testId,
  );
}

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

/** 起一个假端点，返回它的 baseUrl 与那个进程。 */
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

/**
 * **页面内**把这一桌往前推（票 56 那条教训：每手一次 playwright 往返太贵）。
 *
 * 一步的判据只有一条：**手牌上有点得出去的那几张就点，没有就按「单步」**。
 * 于是「点手牌→打出→等三家→再点」这一圈完全跑在页面里，一整场东风战一次 `evaluate` 走完。
 *
 * @returns clicks 真人点了几次、steps 按了几次单步、kyokus 打了几局、
 *          stuck 卡在哪儿（null = 没卡）、ended 终局了没有
 */
function driveHuman(page, { limit, budgetMs }) {
  return page.evaluate(
    async ({ limit, budgetMs }) => {
      const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
      const playable = () => document.querySelector("[data-dahai-id]");
      const latest = () => at("table-latest")?.textContent?.trim() ?? "";

      // 先在微任务里抢答，拖久了退到宏任务（别把事件循环饿死：等模型那一档要它）。
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

      let clicks = 0;
      let steps = 0;
      let kyokus = 1;
      let stuck = null;

      for (let move = 0; move < limit; move += 1) {
        if (at("table-result") !== null) break;

        const tile = playable();
        if (tile !== null) {
          const before = latest();
          tile.click();
          clicks += 1;
          if (!(await until(() => latest() !== before || at("table-fault") !== null))) {
            stuck = `点了手牌（data-dahai-id=${tile.getAttribute("data-dahai-id")}）之后牌桌没走动`;
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
          if (next === null || next.disabled) break; // 终局：「下一局」也灰了
          next.click();
          kyokus += 1;
          if (!(await until(() => !at("table-step").disabled || playable() !== null))) {
            stuck = `点了「下一局」（第 ${kyokus} 局）之后牌桌没开动`;
            break;
          }
          continue;
        }

        const before = latest();
        step.click();
        steps += 1;

        const landed = await until(
          () =>
            latest() !== before ||
            at("table-fault") !== null ||
            at("table-step").disabled ||
            playable() !== null,
        );
        if (!landed) {
          stuck = `按了「单步」之后 ${budgetMs}ms 里牌桌没走动（上一手：${before}）`;
          break;
        }
        if (at("table-fault") !== null) break;
      }

      return { clicks, steps, kyokus, stuck, ended: at("table-result") !== null };
    },
    { limit, budgetMs },
  );
}

/**
 * **整页 HTML 里的每一个 `data-pai`**（票面原话：连 `data-*` 都不许有）。
 *
 * 读的是 `page.content()` 那一整份序列化文档，不是几个选择器捞出来的那几处
 * ——「他家的手牌一张都不在里面」这句话只有对整页说才算数。
 */
async function paiInDocument(page) {
  const html = await page.content();
  // **先把样式表挡掉**：`styles.css` 里每一张牌面都有一条 `.tile[data-pai="1m"]`
  // （牌面 SVG 就是按它贴的），vite 的 dev server 又把 CSS 内联成 `<style>`
  // ——不挡的话这一条会恒红 39 行，而那是样式不是牌。
  // **只挡这一种**：`<style>` 里的东西不是局面数据，其余整页一律算数。
  const body = html.replace(/<style[\s\S]*?<\/style>/g, "");
  return [...body.matchAll(/data-pai="([^"]*)"/g)].map((each) => each[1]).sort();
}

/**
 * 这一屏上**观测者本来就看得见**的那些牌：自家手牌 + 四家的河 + 四家的副露 + 宝牌指示牌。
 *
 * 它是上面那一份的**预算**：两份逐个相同才算「没泄露」。多出来的每一张都要报出来
 * ——把投影换成上帝视角时，多出来的正好是他家那三手暗牌。
 */
function visiblePai(page, viewer) {
  return page.evaluate((viewer) => {
    const from = (selector) =>
      [...document.querySelectorAll(selector)].map((node) => node.getAttribute("data-pai"));

    const seen = [...from(`[data-testid="seat-${viewer}-hand"] [data-pai]`)];
    for (const seat of [0, 1, 2, 3]) {
      seen.push(...from(`[data-testid="seat-${seat}-kawa"] [data-pai]`));
      seen.push(...from(`[data-testid="seat-${seat}-naki"] [data-pai]`));
    }
    seen.push(...from('[data-testid="table-dora"] [data-pai]'));
    return seen.sort();
  }, viewer);
}

/** 他家那一席的手牌行此刻是什么样（张数、牌背数、露没露牌面）。 */
function handShape(page, seat) {
  return page.evaluate((seat) => {
    const row = document.querySelector(`[data-testid="seat-${seat}-hand"]`);
    if (row === null) return null;
    return {
      count: row.getAttribute("data-hand-count"),
      hidden: row.getAttribute("data-hand-hidden"),
      faces: row.querySelectorAll("[data-pai]").length,
      backs: row.querySelectorAll(".tile.back").length,
    };
  }, seat);
}

/** 一份多重集减法：`left` 里有而 `right` 里没有的那几项（重复也算）。 */
function extras(left, right) {
  const pool = [...right];
  const found = [];
  for (const each of left) {
    const index = pool.indexOf(each);
    if (index < 0) found.push(each);
    else pool.splice(index, 1);
  }
  return found;
}

/** 第一程：真人坐一席，把一整场东风战打完。返回提前中止的失败清单（null = 接着往下走）。 */
async function tableLane(lane, pageOrigin, options) {
  const { budgetMs, peek, missing, problems } = options;
  const model = await startEndpoint(pageOrigin);
  const context = await lane.newContext();

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    // 坐法只从 localStorage 来（票 73 之后是**档案库 + 座位绑定**）：
    // 座位 0 是「我自己」，座位 1 引用那份自定义端点的档案（**不带 key**），座位 2/3 是 bot。
    await plantSeating(page, {
      profiles: [
        {
          name: PROFILE,
          provider: "custom-openai",
          model: "fake-model",
          base_url: model.baseUrl,
          timeout_ms: "10000",
        },
      ],
      seats: [{ choice: "human" }, { choice: profileChoice(PROFILE) }, {}, {}],
    });

    console.log(
      `页面 ${pageOrigin}　座位 ${ME} 是我自己，座位 ${MODEL} ← ${model.baseUrl}，座位 2/3 是 bot`,
    );
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });

    // ---- ① 视角按钮不在 DOM 里 ----
    const godCount = await page.getByTestId("table-view-god").count();
    const mineCount = await page.getByTestId(`table-view-${ME}`).count();
    const othersCount = await page.locator('[data-testid^="table-view-"]').count();
    const lockedAt = await attr(page, "table-view-locked", "data-view-locked");

    console.log("");
    console.log(
      `视角那一排：上帝 ${godCount} 枚、自家 ${mineCount} 枚、一共 ${othersCount} 枚（含那句「锁着」），锁在座位 ${lockedAt}`,
    );

    if (godCount !== 0) {
      missing.push(
        "真人在座、这一场还没打完，「上帝视角」那一枚却还在 DOM 里：" +
          "灰掉不算数——票 81 把视角定成了信息闸门，一行 DevTools 就能把 disabled 平掉",
      );
    }
    if (mineCount !== 1) {
      missing.push(
        `自家那一枚视角按钮有 ${mineCount} 枚（该正好 1 枚）：整排消失会让人以为页面坏了`,
      );
    }
    for (const seat of [1, 2, 3]) {
      const count = await page.getByTestId(`table-view-${seat}`).count();
      if (count !== 0) missing.push(`别席（座位 ${seat}）的视角按钮还在 DOM 里（${count} 枚）`);
    }
    if (lockedAt !== String(ME)) {
      missing.push(
        `页面没说清视角锁在哪一席（data-view-locked=「${lockedAt}」，该是 ${ME}）：` +
          "「那几枚按钮本来就没有」与「页面坏了」在屏幕上长得一模一样",
      );
    }

    // ---- ② 点自己手里的一张就打出去 ----
    const state = await attr(page, "table-human", "data-human");

    if (state === null) {
      // 真人那一行整行不在（`humanSeated` 没说真话）：底下每一条都会变成 null 对 null。
      return failure("真人坐席这一道没过：", [
        `座位 ${ME} 拨的是「我自己」，页面上却没有真人那一行（[data-testid="table-human"]）：` +
          "这一桌根本不知道桌边坐了人，底下那十几条都在空转",
      ]);
    }

    const playableSaid = Number.parseInt(
      (await attr(page, "table-human", "data-human-playable")) ?? "-1",
      10,
    );
    const dahai = await page.locator("[data-dahai-id]").count();
    const mineDahai = await page.locator(`[data-testid="seat-${ME}-hand"] [data-dahai-id]`).count();
    // **数的是不同的 id，不是点得动的牌数**：手里两张 3 索是两张牌、却只是
    // **一条**动作（手切 3 索），两张都挂同一个 id。这一条要铉的是
    // 「页面上点得到的动作集 = 引擎给的那一份」。
    const ids = await page.evaluate(
      () =>
        [
          ...new Set(
            [...document.querySelectorAll("[data-dahai-id]")].map((n) => n.dataset.dahaiId),
          ),
        ].length,
    );

    console.log(
      `真人那一行：${state}　它说点得出去 ${playableSaid} 条，页面上有 ${dahai} 张点得动（${ids} 条不同的动作）`,
    );
    console.log(`　「${await text(page, "table-human")}」`);

    if (state !== "waiting") {
      missing.push(`一打开这一页就该轮到真人出牌（他是东 1 局的亲），实际 data-human=「${state}」`);
    }
    if (playableSaid <= 0) {
      missing.push(`真人那一行说这一手点得出去 ${playableSaid} 条：那一条断言在空转`);
    }
    if (ids !== playableSaid) {
      missing.push(
        `页面上点得到 ${ids} 条不同的打牌动作，而合法动作集说该有 ${playableSaid} 条：` +
          "能点哪几张只许由引擎的合法动作集定（spec：合法性驱动 UI）",
      );
    }
    if (mineDahai !== dahai) {
      missing.push(`自家手牌之外还有 ${dahai - mineDahai} 张牌点得动`);
    }
    for (const seat of [1, 2, 3]) {
      const theirs = await page
        .locator(`[data-testid="seat-${seat}-hand"] [data-dahai-id]`)
        .count();
      if (theirs !== 0) missing.push(`他家（座位 ${seat}）的手牌上竟有 ${theirs} 张点得动`);
    }

    // **点下去的就是打出去的那一张**：上面只数了条数，而「id 与牌对不上」数得再准也看不出来。
    // 这一下把整条链走完：页面上那张牌 → 包内 id → 引擎落定的那一手 → 自家河里多出来的那一张。
    const picked = await page.evaluate(() => {
      const tile = document.querySelector(`[data-testid="seat-0-hand"] [data-dahai-id]`);
      return tile === null
        ? null
        : { pai: tile.getAttribute("data-pai"), id: tile.dataset.dahaiId };
    });
    const kawaBefore = await page.locator(`[data-testid="seat-${ME}-kawa"] [data-pai]`).count();
    await page.locator(`[data-testid="seat-${ME}-hand"] [data-dahai-id]`).first().click();
    const landed = await page.evaluate(
      (seat) =>
        [...document.querySelectorAll(`[data-testid="seat-${seat}-kawa"] [data-pai]`)]
          .map((node) => node.getAttribute("data-pai"))
          .at(-1) ?? null,
      ME,
    );
    const kawaAfter = await page.locator(`[data-testid="seat-${ME}-kawa"] [data-pai]`).count();

    console.log(
      `点了手里那张 ${picked?.pai}（包内 id ${picked?.id}）：自家河 ${kawaBefore} → ${kawaAfter} 张，末尾是 ${landed}`,
    );

    if (kawaAfter !== kawaBefore + 1) {
      missing.push(`点了一张手牌，自家的河却从 ${kawaBefore} 变成 ${kawaAfter} 张（该多一张）`);
    }
    if (landed !== picked?.pai) {
      missing.push(
        `点的是 ${picked?.pai}，打出去的却是 ${landed}：这一张牌上那个包内 id 指错了人` +
          "（数条数数得再准也看不出这一条）",
      );
    }

    // ---- 走一段，让模型开过口、也让「自动过」有机会发生 ----
    const walked = await driveHuman(page, { limit: peek, budgetMs });
    console.log("");
    console.log(
      `走了一段：真人点了 ${walked.clicks} 次、按了 ${walked.steps} 次单步、打到第 ${walked.kyokus} 局` +
        `${walked.stuck === null ? "" : `　卡住：${walked.stuck}`}`,
    );
    if (walked.stuck !== null) {
      return failure("真人坐席这一道没过：", [`这一桌推不动了：${walked.stuck}`]);
    }
    if (walked.clicks === 0) {
      return failure("真人坐席这一道没过：", [
        "走了一段，真人一次都没点过手牌：下面每一条都在空转",
      ]);
    }

    // ---- ③ 结构性不泄露（story 29） ----
    const inDocument = await paiInDocument(page);
    const budget = await visiblePai(page, ME);
    const leaked = extras(inDocument, budget);

    console.log("");
    console.log(
      `整页 HTML 里有 ${inDocument.length} 个 data-pai，观测者本来就看得见的有 ${budget.length} 个`,
    );

    if (leaked.length > 0) {
      missing.push(
        `对局中整页 HTML 里多出 ${leaked.length} 个他不该看得见的 data-pai：${leaked.join("、")}` +
          "——他家的手牌一张都不许在里面，连 data-* 都不许有（spec 的 story 29）",
      );
    }
    if (budget.length > inDocument.length) {
      missing.push(
        `预算（${budget.length}）比整页（${inDocument.length}）还多：这一条量错了，先查它自己`,
      );
    }

    const mine = await handShape(page, ME);
    console.log(`自家手牌行：${mine.count} 张，露着 ${mine.faces} 张、扣着 ${mine.backs} 张`);

    if (mine.hidden !== "false" || mine.faces === 0) {
      missing.push(
        `自家的手牌反而看不见了（data-hand-hidden=${mine.hidden}，露着 ${mine.faces} 张）`,
      );
    }

    for (const seat of [1, 2, 3]) {
      const shape = await handShape(page, seat);
      console.log(
        `座位 ${seat} 的手牌行：${shape.count} 张，露着 ${shape.faces} 张、扣着 ${shape.backs} 张`,
      );

      if (shape.hidden !== "true" || shape.faces !== 0) {
        missing.push(
          `座位 ${seat} 的手牌在页面上露了 ${shape.faces} 张（data-hand-hidden=${shape.hidden}）：` +
            "他家的暗牌在投影里根本不该存在（`MaskedSeat` 没有手牌字段）",
        );
      }
      if (shape.backs !== Number.parseInt(shape.count, 10)) {
        missing.push(`座位 ${seat} 说有 ${shape.count} 张手牌，却画了 ${shape.backs} 张牌背`);
      }
    }

    const ura = await page.getByTestId("table-uradora").count();
    if (ura !== 0) missing.push("对局中桌心摆着里宝牌指示牌：那是上帝视角才有的东西");

    // ---- ④ 气泡对局中一个都没有（阳性对照：模型真被问过） ----
    const bubbles = await page.locator('[data-testid$="-bubble"]').count();
    const agentSaid = (await text(page, "table-agent")) ?? "";
    // **阳性对照走账单而不是状态线**：真人坐在座位 0 上，而视角同样拦着状态线
    // （票 81：气泡与状态线同一条规则）——他本来就不该看见座位 1 说了什么。
    // 而 token 账单不按视角变：它 > 0 就证明那一席模型**真的被问过话**，
    // 于是「0 个气泡」量的不是一桌没人开口的空局。
    const tokens = Number.parseInt(
      (await attr(page, "table-usage", "data-prompt-tokens")) ?? "0",
      10,
    );

    console.log("");
    console.log(`对局中的气泡：${bubbles} 个　账单 ${tokens} tok　Agent 那一行：${agentSaid}`);

    if (!(tokens > 0)) {
      missing.push(
        `这一桌至今一个 prompt token 都没花（${tokens}）：座位 ${MODEL} 那一席模型根本没被问过，` +
          "下面那条「0 个气泡」因此什么都没证明",
      );
    }
    if (agentSaid.includes(SAID)) {
      missing.push(
        `坐在座位 ${ME} 上，Agent 那一行却写着座位 ${MODEL} 的理由：「${agentSaid}」` +
          "——气泡拦住了而状态线漏了，那闸门就只是个摆设（票 81）",
      );
    }
    if (bubbles !== 0) {
      missing.push(
        `有真人在座、对局还没打完，页面上却有 ${bubbles} 个思考气泡：` +
          "AI 的推理会向同桌的真人泄露它的手牌（spec 的 story 31）",
      );
    }

    // ---- ⑥ 把一整场东风战打完 ----
    const rest = await driveHuman(page, { limit: 4000, budgetMs });
    console.log("");
    console.log(
      `打到底：又点了 ${rest.clicks} 次、按了 ${rest.steps} 次单步，共 ${rest.kyokus} 局，终局=${rest.ended}` +
        `${rest.stuck === null ? "" : `　卡住：${rest.stuck}`}`,
    );

    if (rest.stuck !== null) missing.push(`这一桌推不动了：${rest.stuck}`);
    if (!rest.ended) {
      missing.push("这一场没走到终局：真人坐一席就该照样打得完（spec 的 story 28）");
      return null;
    }

    const scores = await page.evaluate(() =>
      [0, 1, 2, 3].map((seat) =>
        Number.parseInt(
          document
            .querySelector(`[data-testid="seat-${seat}-score"]`)
            ?.getAttribute("data-score") ?? "NaN",
          10,
        ),
      ),
    );
    const total = scores.reduce((sum, each) => sum + each, 0);

    console.log(`终局点数：${scores.join(" / ")}　合计 ${total}`);
    console.log(`终局精算：${await text(page, "table-result-ranking")}`);

    if (total !== 100000) {
      missing.push(`终局四家点数之和是 ${total}（该恒为 100000）：${scores.join(" / ")}`);
    }

    // ---- ⑤ 鸣牌自动过，而且页面说得出过掉了什么 ----
    // **量在打完之后**（判据 3）：开局头几十手里可能一次鸣牌机会都没碰上，
    // 那时候量它就是一条永远执行不到的断言；一整场下来实测稳定在十几到二十几次。
    const passes = Number.parseInt(
      (await attr(page, "table-human", "data-human-passes")) ?? "-1",
      10,
    );
    const saidNow = (await text(page, "table-human")) ?? "";

    const settledState = await attr(page, "table-human", "data-human");

    console.log(`这一场替他自动过了 ${passes} 次（真人那一行：${settledState}）：「${saidNow}」`);

    if (settledState !== "settled") {
      missing.push(
        `终局那一屏上真人那一行还写着「${settledState}」：那一刻既不轮到他、也不轮到别人，` +
          "而那正是视角与气泡一起松开的那一刻——不说的话人不知道刚才藏着的现在看得了",
      );
    }

    if (!(passes > 0)) {
      missing.push(
        `一整场下来一次鸣牌都没替他过（data-human-passes=${passes}）：` +
          "要么自动过根本没发生，要么它发生了却没记账——两种都是票 88 要接的那道缝断了",
      );
    }
    if (passes > 0 && !saidNow.includes("替你过了")) {
      missing.push(`替他过了 ${passes} 次，页面上却一个字都没说：人会以为这个平台漏了鸣牌`);
    }

    // ---- ⑦ 终局之后三样一起松开 ----
    const godBack = await page.getByTestId("table-view-god").count();
    const lockedStill = await page.getByTestId("table-view-locked").count();
    const bubbleBack = await page.getByTestId(`seat-${MODEL}-bubble`).count();

    console.log("");
    console.log(
      `终局之后：上帝视角 ${godBack} 枚、那句「锁着」${lockedStill} 句、座位 ${MODEL} 的气泡 ${bubbleBack} 个`,
    );

    if (godBack !== 1)
      missing.push(`终局之后「上帝视角」那一枚没回来（${godBack} 枚）：复盘本来就该看得见四家`);
    if (lockedStill !== 0) missing.push("终局之后页面还写着「视角锁着」");
    if (bubbleBack !== 1) {
      missing.push(
        `终局之后座位 ${MODEL} 的思考气泡没回来（${bubbleBack} 个）：` +
          "上面那条「对局中 0 个气泡」因此可能只是气泡整个坏了",
      );
    } else {
      // **并且里面写着它当时说的那句话**：这才把「对局中藏着、终局后放出来」
      // 铉成同一句话的两面——那句话只可能从那个假端点那儿来。
      const said = (await text(page, `seat-${MODEL}-bubble`)) ?? "";
      console.log(`　座位 ${MODEL} 的气泡：${said}`);

      if (!said.includes(SAID)) {
        missing.push(`终局后座位 ${MODEL} 的气泡里不是它端点回的那句：「${said}」`);
      }
    }

    for (const seat of [1, 2, 3]) {
      const count = await page.getByTestId(`table-view-${seat}`).count();
      if (count !== 1) missing.push(`终局之后座位 ${seat} 的视角按钮没回来（${count} 枚）`);
    }
  } finally {
    await context.close();
    model.endpoint.kill();
  }
  return null;
}

/** 第二程：`?dev=1` 的阴阳对照（挂账 22-A）。 */
async function devLane(lane, pageOrigin, options) {
  const { missing, problems } = options;
  const context = await lane.newContext();

  try {
    const shot = async (label, seats) => {
      const page = await context.newPage();
      page.on("pageerror", (error) => problems.push(`[pageerror ${label}] ${error.message}`));
      await plantSeating(page, { profiles: [], seats });
      await page.goto(`${hostPage(pageOrigin)}&dev=1`, { waitUntil: "load" });

      const found = {
        seed: await page.getByTestId("seed-input").count(),
        traces: await page.getByTestId("traces").count(),
        board: await page.getByTestId("table-board").count(),
      };
      console.log(
        `${label}：seed-input ${found.seed} 个、traces ${found.traces} 个、牌桌 ${found.board} 张`,
      );
      return found;
    };

    console.log("");
    const seated = await shot("真人在座 + ?dev=1", [{ choice: "human" }, {}, {}, {}]);
    const empty = await shot("没有真人 + ?dev=1（阴性对照）", [{}, {}, {}, {}]);

    if (seated.seed !== 0 || seated.traces !== 0) {
      missing.push(
        `真人在座时 ?dev=1 还是把曳光弹挂了出来（seed-input ${seated.seed} 个、traces ${seated.traces} 个）：` +
          "那一块把 start_kyoku（带着四家配牌）印在同一张文档里，挂账 22-A 说的就是它",
      );
    }
    if (seated.board !== 1) {
      missing.push(
        `真人在座 + ?dev=1 那一页上没有牌桌（${seated.board} 张）：不给开曳光弹不等于把页面弄坏`,
      );
    }
    if (empty.seed !== 1 || empty.traces !== 1) {
      missing.push(
        `没有真人时 ?dev=1 也开不出曳光弹了（seed-input ${empty.seed} 个、traces ${empty.traces} 个）：` +
          "上面那一条因此什么都没证明（可能只是曳光弹整个没了）",
      );
    }
  } finally {
    await context.close();
  }
}

/** 真人坐席那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyHuman(lane, options = {}) {
  const { budgetMs = 120000, peek = 40 } = options;

  // dev server 而不是 preview：与 verify-bubbles 同一个理由（省掉一次 vite build）。
  const pageOrigin = await lane.devUrl();
  const problems = [];
  const missing = [];

  const early = await tableLane(lane, pageOrigin, { budgetMs, peek, missing, problems });
  if (early !== null) return early;

  await devLane(lane, pageOrigin, { missing, problems });

  if (problems.length > 0) return failure("页面报了错：", problems);
  if (missing.length > 0) return failure("真人坐席这一道没过：", missing);

  console.log("");
  console.log("视角按钮不在 DOM 里、点手牌就打得出去、整页 HTML 里没有他家的一张手牌 ✓");
  console.log("对局中一个气泡都没有（而模型真说过话）、鸣牌自动过且页面说得出过了什么 ✓");
  console.log("真人坐一席把一整场东风战打完，四家点数之和 100000；终局后视角与气泡一起回来 ✓");
  console.log("真人在座时 ?dev=1 的曳光弹不给开，没有真人时照旧开得了 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };

  await runStandalone((lane) =>
    verifyHuman(lane, {
      budgetMs: Number.parseInt(flag("--budget", "120000"), 10),
      peek: Number.parseInt(flag("--peek", "40"), 10),
    }),
  );
}
