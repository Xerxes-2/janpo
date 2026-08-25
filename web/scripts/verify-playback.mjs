// 播放速度（票 112）：**按下播放之后，一秒真的走了几手**。
//
// 牌桌有播放 / 暂停 / 倍速，票 78 修过「双倍速」那种世代 bug，而在这一趟之前
// **全仓库没有一处断言量过速度本身**——世代号那道锁只有「旧世代的 `Ticked` 发不出效果体」
// 这条间接证据（dotnet 侧的值层面），页面上一秒走几手从来没人数过。
//
// ## 量点（判据 20）
//
// 量的是**牌桌上那一步真的画出来的那一刻**：页面里挂一个 `MutationObserver`，
// 每次「这一屏变了」记一个 `performance.now()`，同时记下**手数计数器**当时的值。
// 一段量完只回一次 node（票 56 那一课：每手一次 playwright 往返是三千次跨进程调用）。
//
// **计数器分两种，各有各的理由**：
//
// - 回放（首页那份 Demo）：`data-cursor`（帧号）。一记定时器 = 一帧，**精确**；
//   一帧 ≈ 一手（那份资产 256 帧 / 252 手，差的是四个开局帧）。
// - Live（`?table=1`，四家 bot、零网络）：牌桌上没有手序可读（时间轴只有回放才有），
//   因此数的是**「这一屏变了」的次数**。一记被认下的定时器 = 引擎真走一手 = 一次重绘。
//
// ## 断言的形状：**手/秒**，不是「相邻两手的间隔中位数」
//
// 一开始想的是「间隔中位数」，它**抓不住这一票真正要防的那个 bug**：两条定时器链
// 错开 δ 一起跑时，间隔序列是 δ、interval−δ、δ、…，中位数落在两者之间——
// 而**每秒走的手数实实在在翻了倍**。所以量的是 (手数差 ÷ 秒数)，
// 这也正是票面那句话的字面意思。间隔的中位与 p95 照旧印出来（给人读的实况），但不进断言。
//
// ## 期望值为什么在这份文件里**另写一份**
//
// 下面那张 `DESIGN` 是**期望值**，不是从 `Playback.fs` 读回来的实现值。
// 从编译产物里 `import` 那个常数当然更「单一真源」，可那样一来**把 600ms 改成 300ms 时
// 这道闸门会跟着改口**——判据清单开头那条说得很清楚：断言「两边一样」之前先问
// 「两边是不是同一个实现」。这份表就是那个第三锚点，它与 `src/Janpo.Web/Playback.fs`
// 的 `Speed.interval` **必须一致，而它们是两处**：真改了档速就连这份表一起改，
// 并在报告里说清为什么（这道闸门红出来的话就是在提醒这件事）。
// 档位数量本身也有阳性对照：页面上有几枚 `table-speed-*` 按钮，就得与这张表一样长。
//
// ## 这条带子（`FAST_ROOM` / `SLOW_ROOM`）的来历
//
// 本机实测（报告 112 有全表）：四档的「实测手秒 ÷ 设计手秒」**连着跑十次都是 0.986–0.993**
// （同一台机器上另开一个 `dotnet test` 一起跑，仍旧 0.990–0.994）；
// 用 CDP 把 CPU 降速 **8×** 折算下来是 0.946–0.982、降速 **20×** 是 0.824–0.981。
// 而几种真会发生的坏各是多少：**两条定时器链一起跑 = 2.0**、**档位没接上**
// （8× 走成 1× 的间隔）= 0.1、**后台标签页被压到一秒一记** = 0.06–0.6。
// 于是带子取 **[0.70, 1.15]**：上界离最坏的实测抖动（1.008，定时器早响 0.8%）还有余量、
// 离「双倍速」那个 2.0 远得很；下界比 20× 降速那一档的 0.824 还宽 15%，
// 而所有「慢下来」的坏至少差三倍。**上界是对机器负载不敏感的那一半**
// （负载只会让手数变少），它正是抓票 78 那个 bug 的那一条。
//
// ## 后台节流那件（票面要害 2）
//
// `setTimeout` 在**后台标签页**里会被 Chrome 压到一秒一记。这条跑道上不会发生，两道保险：
//
//   ① playwright 起 chromium 时默认带着 `--disable-background-timer-throttling`、
//      `--disable-backgrounding-occluded-windows`、`--disable-renderer-backgrounding`
//      （playwright-core 1.62.1 的 `chromiumSwitches`，实测摘掉这三个 flag、
//      再在同一个浏览器里把另一页摆到前台，被量那一页的 `setTimeout(60)` 仍旧 60ms 一记）；
//   ② 无头 chromium 里每个 page 是各自的 target，互相不会把对方按成后台
//      ——实测被量那一页的 `document.visibilityState` 恒是 `visible`。
//
// 光写在注释里不算数（判据 2），因此**这一趟自己把它断言了**：量的时候顺手读一次
// `visibilityState`，不是 `visible` 就红——那时这一趟量到的数是噪声，得当场说出来而不是默默飘。
//
// 跑法：`cd web && pnpm run build && node scripts/verify-playback.mjs`
// 它也是 `verify-browser.mjs` 那张 `gates` 表里的一项（跑道上那几趟共用一个浏览器与一台服务器）。

import { failure, isEntry, mark, markerSince, runStandalone } from "./browser-lane.mjs";

/**
 * **期望值**：每一档两手之间该等多少毫秒（`src/Janpo.Web/Playback.fs` 的 `Speed.interval`），
 * 与按钮上那个字（`Speed.toDisplay`，testId 是 `table-speed-<它>`）。
 * 为什么另写一份而不是 import 那个常数，见文件头。
 */
const DESIGN = [
  { label: "1×", ms: 600 },
  { label: "2×", ms: 300 },
  { label: "4×", ms: 150 },
  { label: "8×", ms: 60 },
];

/** 实测手秒 ÷ 设计手秒 的上界。**这一半对机器负载不敏感**：负载只会让手数变少。 */
const FAST_ROOM = 1.15;

/** 实测手秒 ÷ 设计手秒 的下界。慢机器落在这里（20× CPU 降速实测 0.824）。 */
const SLOW_ROOM = 0.7;

/**
 * 这一档量几个间隔。**按档速摊平**：每档大约花 3 秒，越快的档采得越多。
 * 上限 16 是为了让 8× 那一档不至于只花 0.3 秒就收工（样本太少中位没意义），
 * 下限 5 是为了 1× 那一档不至于独吞 10 秒——五个间隔 3 秒，手秒的估计已经稳到千分位。
 */
function samplesFor(ms) {
  return Math.max(5, Math.min(16, Math.round(3000 / ms)));
}

/**
 * 量一段：在页面里等到收够 `want` 个间隔，返回每一次重绘的时刻与当时的手数。
 *
 * `mode`：
 * - `"frames"`：手数计数器读时间轴的 `data-cursor`（回放，精确）；
 * - `"commits"`：牌桌上没有手序可读（Live），数的是「这一屏变了」的次数。
 *
 * 一局打完时牌桌自己会暂停（`resume`）：那时点「下一局」再点「播放」，
 * **并把已经收到的样本整段丢掉**（跨过那一停的间隔不是速度，是人手的延迟）。
 */
async function sample(page, { want, budgetMs, mode }) {
  return await page.evaluate(
    async ({ want, budgetMs, mode }) => {
      const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
      const attr = (testId, name) => at(testId)?.getAttribute(name) ?? "?";

      // 「这一屏变了没有」：回放读游标，Live 读那句「上一手」加四家的河与手牌张数。
      // 两种都只在**真走了一手**时变（Live 那一桌四家全是 bot，没有在飞的问话与气泡）。
      const digest = () =>
        mode === "frames"
          ? attr("table-timeline", "data-cursor")
          : [
              at("table-latest")?.textContent ?? "",
              [0, 1, 2, 3].map((seat) => attr(`seat-${seat}-kawa`, "data-kawa-count")).join(","),
              [0, 1, 2, 3].map((seat) => attr(`seat-${seat}-hand`, "data-hand-count")).join(","),
            ].join("|");

      const marks = [];
      let commits = 0;
      let last = digest();
      const hands = () =>
        mode === "frames" ? Number.parseInt(attr("table-timeline", "data-cursor"), 10) : commits;

      const observer = new MutationObserver(() => {
        const now = digest();
        if (now === last) return;
        last = now;
        commits += 1;
        marks.push({ t: performance.now(), hands: hands() });
      });
      observer.observe(document.body, {
        subtree: true,
        childList: true,
        characterData: true,
        attributes: true,
      });

      const playing = () => at("table-play")?.textContent?.trim() === "暂停";
      const started = performance.now();
      let stalls = 0;

      while (marks.length < want + 1 && performance.now() - started < budgetMs) {
        await new Promise((done) => setTimeout(done, 10));
        if (playing()) continue;
        // 停了：这一局打完了（Live）或者回放播到了末帧。接着开一局，样本重来。
        stalls += 1;
        const next = at("table-next");
        if (next !== null && !next.disabled) next.click();
        await new Promise((done) => setTimeout(done, 60));
        const play = at("table-play");
        if (play !== null && !play.disabled && !playing()) play.click();
        marks.length = 0;
        last = digest();
      }
      observer.disconnect();

      return { marks, stalls, visible: document.visibilityState, focused: document.hasFocus() };
    },
    { want, budgetMs, mode },
  );
}

/** 一段样本的实况：手/秒、相邻两次重绘之间的间隔（中位与 p95）。 */
function statsOf(marks) {
  const gaps = marks.slice(1).map((each, index) => each.t - marks[index].t);
  const sorted = [...gaps].sort((a, b) => a - b);
  const quantile = (q) => sorted[Math.min(sorted.length - 1, Math.floor(q * sorted.length))];
  const span = (marks.at(-1).t - marks[0].t) / 1000;
  const walked = marks.at(-1).hands - marks[0].hands;
  return {
    n: gaps.length,
    walked,
    span,
    perSecond: walked / span,
    p50: quantile(0.5),
    p95: quantile(0.95),
    min: sorted[0],
    max: sorted.at(-1),
  };
}

/** 把一档的实况写成一句人读得懂的话。 */
function saying(where, gear, seen) {
  const design = 1000 / gear.ms;
  return (
    `${where} ${gear.label}：${seen.perSecond.toFixed(2)} 手/秒` +
    `（设计 ${design.toFixed(2)} = 每 ${gear.ms}ms 一手，实测/设计 ${(seen.perSecond / design).toFixed(3)}）` +
    `　${seen.walked} 手 / ${seen.span.toFixed(2)}s` +
    `　间隔中位 ${seen.p50.toFixed(1)}ms、p95 ${seen.p95.toFixed(1)}ms`
  );
}

/** 一档的判据：手/秒 落在设计值的 [SLOW_ROOM, FAST_ROOM] 倍之间。 */
function problemsOf(where, gear, seen) {
  const design = 1000 / gear.ms;
  const ratio = seen.perSecond / design;
  const said = [];

  if (seen.n < 3) {
    said.push(
      `${where} ${gear.label} 只采到 ${seen.n} 个间隔（要 ${samplesFor(gear.ms)} 个）：` +
        "牌桌多半根本没在走，这一档什么都没量到",
    );
    return said;
  }
  if (ratio > FAST_ROOM) {
    said.push(
      `${saying(where, gear, seen)}\n      → 走得**太快**了（上限 ${FAST_ROOM}）：` +
        "一记定时器该走一手，这里像是有两条链一起在跑（票 78 那种世代 bug），" +
        `或者 Playback.fs 的 Speed.interval 已经不是 ${gear.ms}ms 了` +
        "——真改了档速就连这份文件里的 DESIGN 一起改，并在报告里说清为什么",
    );
  }
  if (ratio < SLOW_ROOM) {
    said.push(
      `${saying(where, gear, seen)}\n      → 走得**太慢**了（下限 ${SLOW_ROOM}）：` +
        "要么这一档的间隔没接上（拨了倍速还按老间隔走），要么定时器被压住了" +
        "（后台标签页会被 Chrome 压到一秒一记），要么这台机器慢到了实测从没见过的程度",
    );
  }
  return said;
}

/**
 * **这一页是不是前台**（票面要害 2）。后台标签页里 `setTimeout` 会被 Chrome 压到一秒一记，
 * 那会让这一趟变成噪声源。这条跑道上不会发生（playwright 起 chromium 时带着三个关节流的 flag，
 * 无头里每个 page 又各是各的 target），**但那两件都在别人手里**——所以每一段量完都问一次。
 *
 * 顺带把「中途停过几次」报出来：一局打完时 `sample` 会点「下一局」并把样本整段丢掉，
 * 停得太多说明这一段是在跟牌局边界赛跑，量到的数该被人看一眼。
 */
function noiseProblems(where, seen) {
  const said = [];
  if (seen.visible !== "visible") {
    said.push(
      `量 ${where} 的时候这一页是 ${seen.visible}（不是 visible）：` +
        "后台标签页里 setTimeout 会被压到一秒一记，这一段量到的数是噪声",
    );
  }
  if (seen.stalls > 2) {
    said.push(
      `量 ${where} 的时候牌桌停了 ${seen.stalls} 次（每停一次样本重来）：` +
        "这一段一直在跟牌局边界赛跑，量到的数不可信",
    );
  }
  return said;
}

/** 挑一档来做世代那一条与 Live 那一条：太快的档点击本身就占满了间隔，太慢的档白等。 */
function middleGear() {
  return DESIGN[Math.min(2, DESIGN.length - 1)];
}

/** 拨到这一档并确保它在播。 */
async function playAt(page, gear) {
  await page.getByTestId(`table-speed-${gear.label}`).click();
  const label = (await page.getByTestId("table-play").textContent()).trim();
  if (label === "播放") await page.getByTestId("table-play").click();
}

/**
 * **世代那一条的制造现场**（票 78 那个坑）：连点四下。
 *
 * 「暂停 → 立刻再播」与「连点两下倍速」各会留下一记**在飞的**定时器（上一档那一记还没回来）。
 * 世代号那道锁要是破了，那几记会与新发的一起被认下，**而每一条链自己又续下一记**
 * （`resume` / `replayTick` 都在尾上 `schedule`）——踩上去就是**永远**双倍速，不会自己好。
 *
 * **量点停在连点四下之后**（判据 20）：churn 之前那一段只有一条链，它多快证明不了这件事。
 */
async function churnClicks(page, gear) {
  const play = page.getByTestId("table-play");
  await play.click(); // 暂停
  await play.click(); // 立刻再播：上一记定时器还在飞
  await page.getByTestId(`table-speed-${gear.label}`).click();
  await page.getByTestId(`table-speed-${gear.label}`).click(); // 连点两下加速
}

/** 量一档（回放或 Live）。预算给到设计耗时的三倍加 6 秒——慢机器上也别把整趟卡死。 */
async function measure(page, gear, mode) {
  const want = samplesFor(gear.ms);
  return await sample(page, { want, budgetMs: want * gear.ms * 3 + 6000, mode });
}

/** 播放速度那一趟。返回的是失败清单（空 = 绿）。 */
export async function verifyPlayback(lane) {
  const url = await lane.previewUrl();
  const page = await lane.newPage();
  const problems = [];
  const errors = [];
  let liveSeen = null;
  let churn = null;
  let live = null;
  let quiet = null;

  try {
    page.on("pageerror", (error) => errors.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") errors.push(`[console.error] ${message.text()}`);
    });

    // ── ① 回放那一页：四档各量一段 ──────────────────────────────────────────
    // 首页那份 Demo 一进来就自动播（票 71），手数计数器是时间轴的帧号——**精确**。
    await page.goto(`${url}/`, { waitUntil: "load" });
    await page.getByTestId("table-board").waitFor({ timeout: 20000 });

    // 阳性对照：页面上有几档，这份期望表就得有几项。加了一档而闸门没跟上时它当场说出来。
    const buttons = await page.locator('[data-testid^="table-speed-"]').count();
    if (buttons !== DESIGN.length) {
      problems.push(
        `页面上有 ${buttons} 枚倍速按钮，这道闸门的 DESIGN 表里只有 ${DESIGN.length} 档：` +
          "档位加减了而这一趟没跟上——没跟上的那几档一手都没被量过",
      );
    }

    for (const gear of DESIGN) {
      const flag = markerSince(problems);
      await playAt(page, gear);
      const seen = await measure(page, gear, "frames");
      problems.push(...noiseProblems(`回放 ${gear.label}`, seen));
      if (seen.marks.length < 2) {
        problems.push(`回放 ${gear.label} 一次重绘都没等到：牌桌根本没在走`);
        continue;
      }
      const stats = statsOf(seen.marks);
      problems.push(...problemsOf("回放", gear, stats));
      console.log(`  ${flag()} ${saying("回放", gear, stats)}`);
      if (gear.label === middleGear().label) quiet = stats;
    }

    // ── ② 世代那一条（票 78 的那个坑）：churn 之后仍旧是一记定时器一手 ────────
    // 连点四下留下的那几记在飞的定时器要是被认下，两条链从此并行（`churnClicks` 的 doc）。
    const gear = middleGear();
    const flag = markerSince(problems);
    await playAt(page, gear);
    await churnClicks(page, gear);
    const churned = await measure(page, gear, "frames");
    problems.push(...noiseProblems(`回放（连点四下之后） ${gear.label}`, churned));
    if (churned.marks.length < 2) {
      problems.push("连点四下之后牌桌不走了：暂停/播放/倍速那几下把定时器弄丢了");
    } else {
      churn = statsOf(churned.marks);
      problems.push(...problemsOf("回放（连点四下之后）", gear, churn));
      console.log(`  ${flag()} ${saying("回放（连点四下之后）", gear, churn)}`);
    }

    // ── ③ Live 那一侧同一条定时器路径 ──────────────────────────────────────
    // `?table=1` 四家 bot、零网络：一记被认下的定时器 = 引擎真走一手。
    // 量它回答的是「**真对局**里一秒走几手」——回放那一侧一帧只是换个下标，
    // 而这一侧每一步都真在跑引擎（引擎那点耗时就落在这个数里）。
    //
    // **这一段同样连点四下再量**：世代那道锁两种来源各有一个执行者，而它不额外花时间
    // （churn 只是四下点击，后面那一段本来就要量）。
    await page.goto(`${url}/?table=1`, { waitUntil: "load" });
    await page.getByTestId("table-board").waitFor({ timeout: 20000 });
    const liveFlag = markerSince(problems);
    await playAt(page, gear);
    await churnClicks(page, gear);
    const walked = await measure(page, gear, "commits");
    problems.push(...noiseProblems(`Live（连点四下之后） ${gear.label}`, walked));
    if (walked.marks.length < 2) {
      problems.push("?table=1 上按了播放，牌桌一手都没走");
    } else {
      live = statsOf(walked.marks);
      problems.push(...problemsOf("Live（连点四下之后）", gear, live));
      console.log(`  ${liveFlag()} ${saying("Live（连点四下之后）", gear, live)}`);
      liveSeen = walked;
    }

    if (errors.length > 0) return failure("页面报了错：", errors);
    if (problems.length > 0) return failure("播放速度这一道没过：", problems);

    console.log(
      `一秒走了几手 ${mark(problems)}（回放四档 + Live 一档，都落在设计值的 ` +
        `${SLOW_ROOM}–${FAST_ROOM} 倍之间；两种来源各连点四下「暂停/播放/倍速」之后仍旧 ` +
        `${churn.perSecond.toFixed(2)} / ${live.perSecond.toFixed(2)} 手/秒，没有第二条定时器链）`,
    );
    console.log(
      `　　量点：DOM 上那一步画出来的那一刻；这一页 visibilityState=${liveSeen.visible}、` +
        `hasFocus=${liveSeen.focused}（后台标签页会被压到一秒一记，这条跑道上没有）` +
        `　中间那一档静态间隔中位 ${quiet.p50.toFixed(1)}ms / churn 后 ${churn.p50.toFixed(1)}ms / ` +
        `Live ${live.p50.toFixed(1)}ms`,
    );
    return [];
  } finally {
    await page.close();
  }
}

if (isEntry(import.meta.url)) {
  await runStandalone((lane) => verifyPlayback(lane));
}
