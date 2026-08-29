// **模型席那条路上的过期问话**（票 108，接票 92 §⑧ 第 2 条）。全程本机（本地假端点），
// **一个字节都不出网**，因此它进 CI（硬约束 4）。
//
// 竞态**不靠时序碰运气**，靠假端点的 `--delay`：座位 0 那一席问出去之后端点先睡几秒，
// 而在这几秒里人**把那一席拨给了自己**（面板上那一枚「我自己」对局中随时点得动），
// 于是那几手由真人打了出去——真人那条路走 `handOf`，不经 `drain` 那条「沿引擎顺序落子」的顺序。
// **牌桌就此绕过那份在飞的问话往前走**，它包里的 id 从此不是这一手的号。
//
// 五条断言，各守一件（**每一条都在 dotnet 侧那几条之外**：那边验的是 update 的值，
// 这边验的是真页面上人看得见的东西，判据 20 的量点也在这边——「那一刻」是真的那一刻）：
//
//   ① 人拨完那一下之后，**在飞的那一席当场清空**（`data-asking-seats`）——剪枝真的发生了；
//   ② 牌桌**没停**：他接着打，手数一直往前走（挂着一份过期问话时 `waiting` 恒真、
//      定时器不续，而那一席又因为「在飞」不会被重问——那是这个 bug 的第一种死法）；
//   ③ 等那份鬼回执真的回来之后，**引擎没有被塞进一条旧动作**（`table-fault` 不在页面上）
//      ——那是第二种死法（拿旧包落子，同一个 id 指的已是另一条动作）；
//   ④ **花了钱**：账单那一行还在，`data-prompt-tokens` > 0（那一次问话真的调了端点、
//      端点真的报了用量）；
//   ⑤ **没落子**：整页一个思考气泡都没有（气泡读的是决策记录）——那一手没有发生，
//      因此不许有一条声称落了子的记录。④⑤ 合起来就是「花了钱、没落子」这件真实情形。
//
// **票 109 往里加了两条（⑥⑦）**，它俩钉的是「撤票是语义、剪枝只是兜底」：
//
//   ⑥ **拨那一下当场撤票**：人把那一席拨给自己的那一瞬间（**牌桌一手都还没走**），
//      在飞的那一席就清空了——不是等他打完一手再被剪枝剔下来（剪枝那一道按
//      「合法动作集是不是还是当下」判，而拨座位那一刻动作集往往没变，它因此剪不掉）；
//   ⑦ **回执赶在他出手之前回来也不落子**：第二程把那一席还给模型、等它被重新问出去、
//      趁它在飞再拨给自己，然后**他一下都不点**地等那份鬼回执回来：河里一张牌不许多、
//      那一手仍旧摆在他面前、而那一趟的钱同样落到账上。**量点就停在那一刻**（判据 20）。
//
// **票 111 把⑦ 开场那个绕法拆了，它自己就是一条断言（⑧）**：
//
//   ⑧ **撤票之后牌桌自己动起来**：把那一席还给模型之后，**人一下都不按**
//      （没有「单步」、没有第二次「播放」），那一席自己又被问了出去。
//      票 109 那一版这儿是靠「单步 1 下」走过去的：`SeatBound` 从来不发定时器，
//      于是人在自动播放中途拨一下座位，这一桌就停在那儿等他再按一下。
//
// **票 110 在尾上接了一条（⑨）**，它钉的是「这笔账人看不看得见」：
//
//   ⑨ **账单行自己说得出其中几笔没落子**：④⑤ 只证明「钱在账上、没有气泡」，
//      而人看着那一行只看得到一个变大了的总额。两个量点（第一只鬼之后 1 与 1、
//      第二只之后 2 与 2）合起来才说得了那两个数是**数出来的**；
//      那一句还得说出「导出的牌谱不带这几笔」——作废的问话不进牌谱（票 110 的判断），
//      而人正是在这一行上按下「导出牌谱」的。另一头（牌谱进来那一侧说得出自己缺了什么）
//      在 `verify-inbound.mjs`。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:stale-ask`
// 它也是 `verify-browser.mjs` 里的一趟（跑道上那几趟共用一个浏览器与一台服务器）。
//
// 选项：--budget ms、--delay ms（端点睡多久，默认 6000）。

import { spawn } from "node:child_process";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, mark, runStandalone } from "./browser-lane.mjs";
import { plantSeating, profileChoice } from "./seating.mjs";
import { hostPage } from "./serve.mjs";
import { openSetup } from "./table-drive.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** 那份档案在库里的叫法（**本机的私人叫法**，不该进牌谱）。 */
const SLOW = "答得很慢的那一份";

/** 被问的那一席：`?table=1` 的默认视角就坐在它上面，人拨「我自己」也拨的是它。 */
const SEAT = 0;

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

/** 起那个**答得很慢**的本地假端点：正常答话，但先睡 `delayMs`。 */
function startEndpoint(port, origin, delayMs) {
  return spawn(
    "node",
    [
      "scripts/fake-endpoint.mjs",
      "--port",
      String(port),
      "--cors",
      origin,
      "--quiet",
      "--delay",
      String(delayMs),
    ],
    { cwd: webRoot, stdio: ["ignore", "ignore", "inherit"] },
  );
}

/** 这一刻页面上那几件事（在飞的席、手数那句话、账单、气泡、引擎有没有拒过东西）。 */
function snapshot(page) {
  return page.evaluate(() => {
    const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
    const attr = (testId, name) => at(testId)?.getAttribute(name) ?? null;
    return {
      asking: attr("table-agent", "data-asking-seats"),
      latest: at("table-latest")?.textContent?.trim() ?? "",
      kawa: document.querySelectorAll('[data-testid$="-kawa"] [data-pai]').length,
      tokens: Number.parseInt(attr("table-usage", "data-prompt-tokens") ?? "0", 10),
      // 账单里那几笔**花了钱、没落子**的（票 110）：闸门读这两条，人读那一行中文。
      voidAsks: Number.parseInt(attr("table-usage", "data-void-asks") ?? "-1", 10),
      voidRebound: Number.parseInt(attr("table-usage", "data-void-rebound") ?? "-1", 10),
      usageText: at("table-usage")?.textContent?.trim() ?? "",
      bubbles: document.querySelectorAll('[data-testid$="-bubble"]').length,
      fault: at("table-fault")?.textContent?.trim() ?? null,
      mine: document.querySelectorAll("[data-dahai-id], [data-human-action-id]").length,
    };
  });
}

/**
 * **页面内**让真人打几手（票 56 那条教训：每手一次 playwright 往返太贵）。
 * 他此刻做得了什么就点什么：牌桌下面那一排优先点「过」，没有那一排就点手牌，
 * 两样都没有就等（bot 那几家由定时器推着走）。
 *
 * @returns played 他自己出手几次、waited 等过几轮、stuck 卡在哪一手（null = 一路都在走）
 */
function playHuman(page, { hands, budgetMs }) {
  return page.evaluate(
    async ({ hands, budgetMs }) => {
      const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
      const kawa = () => document.querySelectorAll('[data-testid$="-kawa"] [data-pai]').length;
      const sleep = (ms) => new Promise((done) => setTimeout(done, ms));

      let played = 0;
      let waited = 0;
      let stuck = null;

      for (let each = 0; each < hands; each += 1) {
        const before = kawa();
        const deadline = performance.now() + budgetMs;

        // 等到这一桌真的往前走了一手为止（他自己出手，或者 bot 打了一张）。
        while (kawa() === before) {
          if (performance.now() > deadline) {
            stuck = `第 ${played} 手之后牌桌 ${budgetMs} ms 没动（河里还是 ${before} 张）`;
            return { played, waited, stuck };
          }

          const pass = [...document.querySelectorAll("[data-human-action-id]")].find(
            (node) => node.dataset.humanAction === "none",
          );
          const tile = document.querySelector("[data-dahai-id]");

          if (pass !== undefined) {
            pass.click();
            played += 1;
          } else if (tile !== null) {
            tile.click();
            played += 1;
          } else {
            // 不轮到他：bot 那几家由定时器推着走，这里什么都不必做。
            // **不能在这里改点「单步」**：那等于替牌桌推一把，
            // 而这一趟要验的正是「它自己转不转得动」。
            waited += 1;
          }

          await sleep(16);
        }
      }

      // 单步那一枚在真人这一桌上推不动他自己那一手，因此这里不碰它。
      return { played, waited, stuck, ended: at("table-result") !== null };
    },
    { hands, budgetMs },
  );
}

export async function verifyStaleAsk(lane, options = {}) {
  const { budgetMs = 90000, delayMs = 6000 } = options;

  const port = await freePort();
  // dev server 而不是 preview：与 verify-seats / verify-human 同一个理由（本机端点要放行它）。
  const pageOrigin = await lane.devUrl();
  const baseUrl = `http://127.0.0.1:${port}/v1`;
  const endpoint = startEndpoint(port, pageOrigin, delayMs);
  await new Promise((done) => setTimeout(done, 800));

  const context = await lane.newContext();
  const problems = [];

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    // 座位 0 交给那份**答得很慢**的档案，其余三家自带 bot。
    // **其余三席刻意不是模型**：这一趟要数「账单上那几个数」与「有几个气泡」，
    // 而 bot 席一条决策记录都不留、一个 token 都不花。
    await plantSeating(page, {
      profiles: [
        {
          name: SLOW,
          provider: "custom-openai",
          model: "slow-model",
          base_url: baseUrl,
          timeout_ms: "60000",
        },
      ],
      seats: [{ choice: profileChoice(SLOW) }, {}, {}, {}],
    });

    console.log(`页面 ${pageOrigin}　答得慢的端点 ${baseUrl}（先睡 ${delayMs} ms 再答话）`);
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });
    await openSetup(page);

    // ---- 把那一手问出去，趁它在飞把那一席拨给自己 ----
    // **按下「播放」而不是「单步」**：这一趟要验的就是「牌桌还转不转得动」，
    // 而开动它的正是定时器——挂着一份过期问话时 `waiting` 恒真，定时器就不续了。
    await page.getByTestId("table-speed-8\u00d7").click();
    await page.getByTestId("table-play").click();
    await page
      .locator('[data-testid="table-agent"][data-asking-seats*="0"]')
      .waitFor({ timeout: budgetMs });

    const flying = await snapshot(page);
    console.log(`问出去了：在飞的席「${flying.asking}」　${flying.latest}`);

    const started = Date.now();
    await page.getByTestId(`table-seat-${SEAT}-human`).click();
    await page.getByTestId("table-human").waitFor({ timeout: budgetMs });

    // 拨完那一下牌桌还没动：那份问话此刻仍旧对得上这一手（它还没过期）。
    const seated = await snapshot(page);
    console.log(`拨给自己了：在飞的席「${seated.asking}」　他此刻点得动 ${seated.mine} 处`);

    // ⑥ **撤票是语义**（票 109）：拨那一下当场作废，而不是等它回来再剪。
    // **量点就在这一刻**（判据 20）：牌桌一手都还没走（下面那一句先核它），
    // 因此剪枝那一道在这一刻什么都剪不掉——清空了就只能是撤票干的。
    if (seated.latest !== flying.latest) {
      problems.push(
        `拨那一下之前牌桌就已经往前走了（上一手「${flying.latest}」→「${seated.latest}」）：` +
          "⑥ 那一条在空转——它要钉的是「牌桌没动而在飞的席清了」",
      );
    }

    if (seated.asking !== "") {
      problems.push(
        `人把那一席拨给自己之后，在飞的问话还挂着（data-asking-seats="${seated.asking}"）：` +
          "拨座位那一刻合法动作集往往没变，剪枝那一道因此剪不掉它——" +
          "撤票得是语义（这一席交给了别人，旧回执就不算它的答复）",
      );
    }

    // ---- 他自己打一手：牌桌翻篇，那份在飞的问话就此过期 ----
    const first = await playHuman(page, { hands: 1, budgetMs: 20000 });
    const expired = await snapshot(page);
    console.log(
      `他打了一手（点了 ${first.played} 下）：在飞的席「${expired.asking}」　${expired.latest}`,
    );

    // ① 过期的那一份当场剪掉：在飞的席清空了。
    if (expired.asking !== "") {
      problems.push(
        `他自己打完一手之后，在飞的问话还挂着（data-asking-seats="${expired.asking}"）：` +
          "那一份的 id 是按上一手的合法动作集编的号，留着它 `waiting` 就恒真、牌桌停在这儿不动",
      );
    }

    // ---- ② 牌桌没停：他接着打，一直走到那份鬼回执回来之后 ----
    const walked = await playHuman(page, { hands: 8, budgetMs: 25000 });
    const elapsed = Date.now() - started;
    console.log(
      `接着走了一段：他出手 ${walked.played} 次、等了 ${walked.waited} 轮、拨完到现在 ${elapsed} ms`,
    );

    if (walked.stuck !== null) {
      problems.push(`牌桌停住了：${walked.stuck}`);
    }

    // 等到端点那份回执**确实**回来了（睡够 + 一点余量），再看它有没有闯祸。
    const left = delayMs + 2000 - (Date.now() - started);
    if (left > 0) await page.waitForTimeout(left);
    await page.waitForTimeout(1200);

    const after = await snapshot(page);
    console.log(
      `鬼回执回来之后：河里 ${after.kawa} 张　账单 ${after.tokens} tok　气泡 ${after.bubbles} 个　` +
        `Fault ${after.fault ?? "无"}　${after.latest}`,
    );

    // ③ 旧包没有被塞进引擎（塞进去要么当场被拒、要么替他打了一手）。
    if (after.fault !== null) {
      problems.push(
        `引擎拒了一个动作：「${after.fault}」——那正是过期问话拿旧包落子的下场（同一个 id 指的已是另一条动作）`,
      );
    }

    // ④ 花了钱：那一次问话真的调了端点、端点真的报了用量。
    if (!(after.tokens > 0)) {
      problems.push(
        `账单上一个 prompt token 都没有（${after.tokens}）：那一次问话真的调过端点、真的计过费，` +
          "作废之后 token 不许从账上消失（账单报的是花掉的总额，不是落了子的那几手的总额）",
      );
    }

    // ⑨ **账单行自己说得出这几笔**（票 110）：④⑤ 只证明「钱在账上、没有气泡」，
    // 而人看着那一行只看得到一个变大了的总额——**其中有几笔没落子，页面得说出来**。
    // **量点就在这一刻**（判据 20）：鬼回执刚补完账，那一笔既在账上、又没有对应的一手。
    if (after.voidAsks !== 1 || after.voidRebound !== 1) {
      problems.push(
        `账单行没说出那一笔没落子的花销（data-void-asks=${after.voidAsks}、` +
          `data-void-rebound=${after.voidRebound}，该是 1 与 1）：` +
          "账单总额诚实了，人却看不见其中有几笔是花了钱没落子的",
      );
    }

    // 人看的就是这一句（判据 7：看得见这类验收要把那句话真的摆出来）。
    console.log(`账单行此刻写着：${after.usageText}`);

    for (const fragment of ["其中 1 笔花了钱没落子", "1 笔是换人撤的", "导出的牌谱不带这几笔"]) {
      if (!after.usageText.includes(fragment)) {
        problems.push(
          `账单行那一句里没有「${fragment}」，它此刻写着：${after.usageText}` +
            "——人是在这一行上按下「导出牌谱」的，导出之后账少一块这件事得在这儿说",
        );
      }
    }

    // ⑤ 没落子：气泡读的是决策记录，作废的那一次不许留下一条声称落了子的记录。
    if (after.bubbles !== 0) {
      problems.push(
        `牌桌上长出了 ${after.bubbles} 个思考气泡：作废的那一次问话没有落成一手，` +
          "不许留下一条声称落了子的决策记录（气泡读的就是它）",
      );
    }

    // 阳性对照：这一段真的走过牌（否则上面那几条在空转，判据 3）。
    if (after.kawa <= expired.kawa) {
      problems.push(
        `拨完那一下之后这一桌一张牌都没再打出去（河 ${expired.kawa} → ${after.kawa}）：` +
          "上面那几条断言全在空转",
      );
    }

    // ---- ⑦ 回执**赶在他出手之前**回来：那一手仍旧是他的（票 109）----
    //
    // 前面那一程量的是「他打完一手之后」；这一程量的是票 108 §⑦ 第 4 条明写的那个洞：
    // **他一下都没点**的时候回执回来了。从前包还对得上（`stillCurrent` 为真），
    // 于是模型替坐在桌边的那个人打了一手。
    //
    // **⑧ 就在这一下上**（票 111）：票 109 那一版这儿是一个敲「单步」的 `while` 循环
    // ——`SeatBound` 从来不发定时器，拨完座位没有任何东西去重新问那一席，
    // 闸门只好替牌桌推一把，而**那正是这一趟要验的事**（同 `playHuman` 里
    // 「不能在这里改点单步」那句）。现在它自己走：拨那一下把牌桌按它此刻的
    // 播放状态重新推一记（`roused`），人一下都不必按。
    //
    // **它同时是 ⑦ 的开场**：问出去之后牌桌本来就停在那儿等回执（`waiting`），
    // 因此「趁它在飞拨座位」不靠时序碰运气——那几秒里牌桌一手都不会走。
    const handedBack = Date.now();
    await page.getByTestId(`table-seat-${SEAT}-profile-0`).click();

    // 20 s 是慷慨的上限：8× 那一档一记定时器 60 ms，本机实测拨完到问出去约 90 ms。
    // **不用 `budgetMs`（90 s）**：这一条红的时候是「它压根不会自己走」，早点说出来。
    const selfAsked = await page
      .locator(`[data-testid="table-agent"][data-asking-seats*="${SEAT}"]`)
      .waitFor({ timeout: 20000 })
      .then(
        () => true,
        () => false,
      );

    const reasked = Date.now();
    const flyingAgain = await snapshot(page);
    console.log(`还给模型之后它自己又问了出去（人一下没按）：在飞的席「${flyingAgain.asking}」`);

    if (!selfAsked || !flyingAgain.asking.includes(String(SEAT))) {
      problems.push(
        `把那一席还给模型之后，牌桌自己没把它重新问出去（data-asking-seats="${flyingAgain.asking}"）：` +
          "撤完票那一席就该被重新问了，而拨座位那一下不续定时器的话，" +
          "人在自动播放中途拨一下座位这一桌就停在这儿等他再按一下（⑧）；下面 ⑦ 那几条也因此无从开口",
      );
    }

    await page.getByTestId(`table-seat-${SEAT}-human`).click();
    await page.getByTestId("table-human").waitFor({ timeout: budgetMs });

    const seized = await snapshot(page);

    if (seized.asking !== "") {
      problems.push(`第二次拨给自己之后，在飞的问话还挂着（data-asking-seats="${seized.asking}"）`);
    }

    // **他一下都不点**，就在这儿等那份鬼回执回来（睡够 + 一点余量）。
    const remaining = delayMs + 2500 - (Date.now() - reasked);
    if (remaining > 0) await page.waitForTimeout(remaining);

    const settled = await snapshot(page);
    console.log(
      `他一下没点、鬼回执已经回来：河里 ${seized.kawa} → ${settled.kawa} 张　账单 ${after.tokens} → ${settled.tokens} tok　` +
        `他此刻点得动 ${settled.mine} 处　气泡 ${settled.bubbles} 个　Fault ${settled.fault ?? "无"}`,
    );

    if (settled.kawa !== seized.kawa) {
      problems.push(
        `回执赶在他出手之前回来，替他打了一手（河 ${seized.kawa} → ${settled.kawa}）：` +
          "那一席已经是他的了，牌谱里那一手却会记在模型名下",
      );
    }

    if (settled.mine === 0) {
      problems.push("那一手已经不在他手上了（他一处都点不动）：鬼回执把他那一手顶掉了");
    }

    if (settled.fault !== null) {
      problems.push(`引擎拒了一个动作：「${settled.fault}」——那是鬼回执拿旧包落子的下场`);
    }

    if (settled.bubbles !== 0) {
      problems.push(`牌桌上长出了 ${settled.bubbles} 个思考气泡：被撤的那一次问话没有落成一手`);
    }

    // 被撤的那一趟同样**花了钱**：账单要比第一只鬼那时又长一截。
    if (!(settled.tokens > after.tokens)) {
      problems.push(
        `第二趟问话的 token 没落到账上（${after.tokens} → ${settled.tokens}）：` +
          "撤票与剪枝同一本账——钱真的付了，只是那一手没发生",
      );
    }

    // ⑨ 的第二个量点（票 110）：**那两个数跟着账一起长**。
    // 两笔都是换人撤的，因此这一刻是 2 与 2——它与上一处那对 1 与 1 合起来
    // 才证明这两个数是**数出来的**，不是写死的。
    if (settled.voidAsks !== 2 || settled.voidRebound !== 2) {
      problems.push(
        `第二笔没落子的花销没进账单行（data-void-asks=${settled.voidAsks}、` +
          `data-void-rebound=${settled.voidRebound}，该是 2 与 2）`,
      );
    }

    console.log("");
    console.log(
      `${mark(problems)} 过期问话这一道：剪得掉、牌桌没停、旧包没落子、` +
        `账上那 ${after.tokens} tok 还在、气泡 ${after.bubbles} 个`,
    );
    console.log(
      `${mark(problems)} 换人撤票这一道（票 109）：拨那一下当场清表、` +
        `他一下没点而鬼回执一手没落（河 ${settled.kawa} 张不变）、账上又多了 ${settled.tokens - after.tokens} tok`,
    );
    console.log(
      `${mark(problems)} 撤票之后自己动起来这一道（票 111）：把那一席还给模型之后人一下没按，` +
        `牌桌自己在 ${reasked - handedBack} ms 内把它重新问了出去（从前要敲一下「单步」）`,
    );
    console.log(
      `${mark(problems)} 账单行说得出这几笔这一道（票 110）：${settled.voidAsks} 笔花了钱没落子、` +
        `其中 ${settled.voidRebound} 笔是换人撤的，那一行还说了「导出的牌谱不带这几笔」`,
    );
  } finally {
    await context.close();
    endpoint.kill();
  }

  return problems.length === 0 ? [] : failure("过期问话这一道没过：", problems);
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };

  await runStandalone((lane) =>
    verifyStaleAsk(lane, {
      budgetMs: Number.parseInt(flag("--budget", "90000"), 10),
      delayMs: Number.parseInt(flag("--delay", "6000"), 10),
    }),
  );
}
