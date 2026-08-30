// **一行式开桌**那一道闸门（票 138；评审 P1-E）。
//
// 在这一票之前，开桌的最短路径是七步：自己开一桌 → 配桌 → 新建档案 → provider → 模型 →
// key → 把某席拨给档案 → 播放。这一道守的是那条路被压成了一步，**而且真开出了一桌**。
//
// 七条，各守一件：
//
//   ① **配桌收着也点得到那一行**——`table-quick-start` 在折叠外面（`checkVisibility()` 量），
//      三格加一枚按钮全在。摆进折叠里的话「一步」当场又变回两步。
//   ② **一步真的开出一桌**（票面的原话：填三格、按一下、牌桌上有牌在走）：
//      在配桌**始终收着**的前提下填 provider / 模型 / key，按一下〔开打〕，
//      隔 2.5 秒采两次四家的河——**张数得涨**。
//      它自带**同一个量点上的阴性对照**（判据 20/21）：按之前在同一张页面、
//      用同一个函数采两次，**那时的牌桌必须是停着的**（`?table=1` 默认暂停）。
//      先红的必须是阳性那半句。
//   ③ **key 在界面上只出现在两处**（票 73 的硬判据，票 138 的边界）：
//      整页 `input[type="password"]` 恰好两个，且 testId 恰好是
//      `table-quick-key` 与 `table-profile-key`——**多一个第三处就红**。
//   ④ **两处填的是同一个值**：在那一行里敲进去的 key，展开配桌之后
//      档案编辑处那一格里逐字相同；反着改回来，那一行里也跟着变。
//      两处各存一份的话「同一把 key 坐三席只填一次」就成了空话。
//   ⑤ **key 去向那句小字就在 key 那一格旁边**，而且**与 `README.md` 里那一行逐字相同**
//      ——同一件事不许有第二个说法（README 是这句话的出处）。
//   ⑦ **前两格的名目摆在页面上，且与 `aria-label` 逐字相同**（票 143）：
//      provider 是个 `<select>`、模型那格有默认值，**两格都不空 ⇒ placeholder 永远不显示**，
//      票 138 那一版于是对着头一回来的人失去了名目。这一条钉两件：那两枚标签**看得见**
//      （`checkVisibility()`），且它的字与 `aria-label` **逐字相同**——不然读屏念的
//      与眼睛看到的会各走各的（判据 2：写下一条不变量，先问谁来执行它）。
//   ⑥ **「进阶」默认收起，但收起不等于清空**：`table-profile-advanced` 一进页面
//      `open=false`、里面两格没渲染；敲开之后超时与思考预算**还是今天的默认值**
//      （`ModelProfile.initial`：240000 ms / off）。
//
// 全程本机（一个本地假端点），**一个字节都不出网**，因此它进 CI（硬约束 4）。
// 端点的 `baseUrl` 由 localStorage 预先摆好：**baseUrl 本来就不在那一行上**
// （票 30 的判据：官方那几家根本不看它），那一行只填 provider / 模型 / key。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:quickstart`
// 它也是 `verify-browser.mjs` 里的一趟（跑道上那几趟共用一个浏览器与一台服务器）。

import { spawn } from "node:child_process";
import { readFileSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, markerSince, runStandalone, tick } from "./browser-lane.mjs";
import { plantSeating } from "./seating.mjs";
import { hostPage } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(webRoot, "..");

/** 那一行里人要敲进去的三个值。**key 是写死的假串、全 ASCII**（同票 34 那条规矩）。 */
const PROVIDER = "custom-openai";
const MODEL = "fake-model";
const FAKE_KEY = "sk-janpo-fake-key-QUICKSTART-ONLY-4c1a92";
/** ④ 反着改那一遭用的第二把假 key。 */
const OTHER_KEY = "sk-janpo-fake-key-EDITED-IN-PROFILE-7b30de";

/** key 只该出现在这两处（票 73 的硬判据）。**第三个就红**。 */
const KEY_FIELDS = ["table-quick-key", "table-profile-key"];

/** 一份新档案的默认超时与思考预算（F# 侧 `ModelProfile.initial`，票 72 定的 4 分钟）。 */
const DEFAULT_TIMEOUT_MS = "240000";
const DEFAULT_THINKING = "off";

/** 隔多久采第二次河的张数。2.5 秒够 1× 走好几手（座位 0 每手还要走一趟假端点），
 *  也不至于让 CI 变慢。 */
const SAMPLE_GAP_MS = 2500;

/** 借内核要一个空闲端口：跑批是并行的，写死端口迟早撞上另一个工作区。 */
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

/** 起一个本地假端点（它对什么模型都答得出一个合法动作）。 */
function startEndpoint(port, origin) {
  return spawn(
    "node",
    ["scripts/fake-endpoint.mjs", "--port", String(port), "--cors", origin, "--quiet"],
    { cwd: webRoot, stdio: ["ignore", "ignore", "inherit"] },
  );
}

/** 四家的河合计几张。**「牌在走」是这个数在涨**，DOM 里有牌桌不算（同 `verify-home`）。 */
async function kawaCount(page) {
  return await page.evaluate(() =>
    [0, 1, 2, 3]
      .map((index) => {
        const label = document.querySelector(`[data-testid="seat-${index}-kawa"]`);
        return Number.parseInt(label?.getAttribute("data-kawa-count") ?? "0", 10);
      })
      .reduce((sum, each) => sum + each, 0),
  );
}

/** 配桌那一枚折叠此刻开着吗。**这一道全程要它收着**：那正是「一步」的意思。 */
async function setupOpen(page) {
  return await page.getByTestId("table-setup").evaluate((el) => el.open);
}

/** README 里那一行去掉强调记号之后的正文（那句 key 去向的话的出处）。 */
function readmeText() {
  return readFileSync(resolve(repoRoot, "README.md"), "utf8").replaceAll("**", "");
}

/** 一行式开桌那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyQuickStart(lane) {
  const port = await freePort();
  // dev server 而不是 preview：与 verify-seats / verify-redaction 同一个理由（假端点要跨域）。
  const pageOrigin = await lane.devUrl();
  const baseUrl = `http://127.0.0.1:${port}/v1`;
  const endpoint = startEndpoint(port, pageOrigin);
  await new Promise((done) => setTimeout(done, 800));

  const context = await lane.newContext();
  const problems = [];

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    // **只摆 baseUrl**：那一行上要填的三格（provider / 模型 / key）一个都不预置，
    // 它们由下面那几下真的敲进去——预置了的话 ② 量的就不再是「填三格、按一下」。
    await plantSeating(page, {
      profiles: [{ name: "档案 1", base_url: baseUrl }],
      seats: [{}, {}, {}, {}],
    });

    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });
    // **`attached` 而不是 `visible`**：这一道的头一条断言就是「配桌收着时它也看得见」，
    // 等它可见的话，那一条会以一记超时异常收场，而不是以一行说得清的红。
    await page.getByTestId("table-quick-start").waitFor({ state: "attached", timeout: 15000 });

    // ① 配桌收着，那一行照旧点得到。
    const shutMark = markerSince(problems);
    if (await setupOpen(page)) {
      problems.push("配桌一进页面就摊开着：这一道量的正是「收着也开得了桌」");
    }
    const shown = await page.evaluate(() =>
      [
        "table-quick-start",
        "table-quick-provider",
        "table-quick-model",
        "table-quick-key",
        "table-quick-key-note",
        "table-quick-play",
      ].map((testId) => [
        testId,
        document.querySelector(`[data-testid="${testId}"]`)?.checkVisibility() ?? false,
      ]),
    );
    for (const [testId, visible] of shown) {
      if (!visible) {
        problems.push(
          `配桌收着时 [data-testid="${testId}"] 没渲染出来：一行式开桌被关进折叠里了，「一步」又变回两步`,
        );
      }
    }
    console.log(
      `配桌收着（open=${await setupOpen(page)}）时那一行上：` +
        `${shown.map(([id, ok]) => `${id}${tick(ok)}`).join("　")} ${shutMark()}`,
    );

    // ⑦ 前两格的名目摆在页面上，且与 `aria-label` 逐字相同（票 143）。
    const namedMark = markerSince(problems);
    const labels = await page.evaluate(() =>
      ["table-quick-provider", "table-quick-model"].map((testId) => {
        const control = document.querySelector(`[data-testid="${testId}"]`);
        const wrap = control?.closest("label") ?? null;
        const label = wrap?.querySelector(".label") ?? null;
        return {
          testId,
          shown: label?.checkVisibility() ?? false,
          text: label?.textContent?.trim() ?? null,
          aria: control?.getAttribute("aria-label") ?? null,
        };
      }),
    );
    for (const one of labels) {
      if (!one.shown) {
        problems.push(
          `[data-testid="${one.testId}"] 那一格在页面上没有名字（标签${one.text === null ? "根本不在" : "画不出来"}）：` +
            "那一格不空、占位符永远不显示，头一回来的人只看得到一个没有名目的框（票 143）",
        );
      } else if (one.text !== one.aria) {
        problems.push(
          `[data-testid="${one.testId}"] 眼睛看到的是「${one.text}」、读屏念的是「${one.aria}」：两侧分叉了（票 143）`,
        );
      }
    }
    console.log(
      "前两格的名目摆在页面上、与 aria-label 逐字相同：" +
        `${labels.map((one) => `${one.testId}「${one.text}」${tick(one.shown && one.text === one.aria)}`).join("　")} ${namedMark()}`,
    );

    // ③ key 只出现在两处。**整页数**，不是只数那一行。
    const keyMark = markerSince(problems);
    const passwords = await page.evaluate(() =>
      [...document.querySelectorAll('input[type="password"]')].map(
        (node) => node.getAttribute("data-testid") ?? "(没有 testId)",
      ),
    );
    const unexpected = passwords.filter((each) => !KEY_FIELDS.includes(each));
    if (unexpected.length > 0) {
      problems.push(
        `界面上多出了 key 输入框：${unexpected.join("、")}——key 只许出现在那一行与档案编辑处（票 73 的硬判据）`,
      );
    }
    for (const wanted of KEY_FIELDS) {
      const seen = passwords.filter((each) => each === wanted).length;
      if (seen !== 1) problems.push(`[data-testid="${wanted}"] 在界面上有 ${seen} 个，该正好一个`);
    }
    console.log(`整页的 key 输入框：${passwords.join("、")} ${keyMark()}`);

    // ⑤ key 去向那句小字：就在那一格旁边，且与 README 逐字相同。
    const noteMark = markerSince(problems);
    const note = (await page.getByTestId("table-quick-key-note").textContent()).trim();
    if (!readmeText().includes(note)) {
      problems.push(
        `key 那一格旁边写着「${note}」，而 README.md 里没有这一句：同一件事有了第二个说法`,
      );
    }
    // 它得真挨着 key 那一格（同一行、在它右边），不是掉到页脚去了。
    const beside = await page.evaluate(() => {
      const key = document.querySelector('[data-testid="table-quick-key"]');
      const said = document.querySelector('[data-testid="table-quick-key-note"]');
      if (key === null || said === null) return null;
      const a = key.getBoundingClientRect();
      const b = said.getBoundingClientRect();
      return { gap: Math.round(b.left - a.right), overlap: b.top < a.bottom && a.top < b.bottom };
    });
    if (beside === null || !beside.overlap || beside.gap < 0) {
      problems.push(
        `那句小字没挨着 key 那一格（量到 ${JSON.stringify(beside)}）：` +
          "「需要它的那一秒它不在」正是这一票要修的那件事",
      );
    }
    console.log(`key 那一格右边 ${beside?.gap} px 处写着：「${note}」 ${noteMark()}`);

    // ② 阴性那半句：**按之前**在同一张页面、用同一个函数采两次，牌桌必须是停着的。
    // 它自己修前修后都绿（判据 21），因此下面阳性那半句才是这一条真正的证据。
    const idleMark = markerSince(problems);
    const idleBefore = await kawaCount(page);
    await page.waitForTimeout(SAMPLE_GAP_MS);
    const idleAfter = await kawaCount(page);
    if (idleAfter !== idleBefore) {
      problems.push(
        `还没按〔开打〕，牌桌就自己走起来了（河 ${idleBefore} → ${idleAfter}）：` +
          "那么下面那条「按一下就走」量的不是这一枚按钮",
      );
    }
    const seatBefore = await page.getByTestId("table-seat-0").getAttribute("data-seat-choice");
    if (seatBefore !== "random") {
      problems.push(
        `还没按〔开打〕，座位 0 就已经是「${seatBefore}」：这一桌不是从默认那一桌起步的`,
      );
    }
    console.log(
      `按之前：座位 0 = ${seatBefore}　河 ${idleBefore} →（${SAMPLE_GAP_MS} ms）→ ${idleAfter}（停着）${idleMark()}`,
    );

    // ② 阳性那半句：**填三格、按一下**。全程不碰配桌、不碰「播放」「单步」。
    const playMark = markerSince(problems);
    await page.getByTestId("table-quick-provider").selectOption(PROVIDER);
    await page.getByTestId("table-quick-model").fill(MODEL);
    await page.getByTestId("table-quick-key").fill(FAKE_KEY);
    await page.getByTestId("table-quick-play").click();

    if (await setupOpen(page)) {
      problems.push("按〔开打〕把配桌顶开了：那就不是一步，是「按一下再关掉一个抽屉」");
    }

    const seatAfter = await page.getByTestId("table-seat-0").getAttribute("data-seat-choice");
    if (seatAfter !== "profile:档案 1") {
      problems.push(`按了〔开打〕，座位 0 却是「${seatAfter}」：它该绑上那一行填的那份档案`);
    }
    for (const [index, wanted] of [1, 2, 3].map((each) => [each, "random"])) {
      const said = await page.getByTestId(`table-seat-${index}`).getAttribute("data-seat-choice");
      if (said !== wanted) {
        problems.push(`按了〔开打〕，座位 ${index} 变成了「${said}」：其余三席该原样留着自带 bot`);
      }
    }

    const playedBefore = await kawaCount(page);
    await page.waitForTimeout(SAMPLE_GAP_MS);
    const playedAfter = await kawaCount(page);
    if (playedAfter <= playedBefore) {
      problems.push(
        `填了三格、按了〔开打〕，${SAMPLE_GAP_MS} ms 里四家的河合计还是 ${playedBefore} 张：` +
          "牌桌上没有牌在走——「开出了一桌」只在 DOM 里成立",
      );
    }
    if ((await page.getByTestId("table-fault").count()) > 0) {
      problems.push(`牌桌停住了：${await page.getByTestId("table-fault").textContent()}`);
    }
    console.log(
      `按一下之后：座位 0 = ${seatAfter}　河 ${playedBefore} →（${SAMPLE_GAP_MS} ms）→ ${playedAfter} ${playMark()}`,
    );

    // ④ 两处填的是同一个值。展开配桌再看档案编辑处那一格。
    const sameMark = markerSince(problems);
    await page.getByTestId("table-setup-summary").click();
    const inProfile = await page.getByTestId("table-profile-key").inputValue();
    if (inProfile !== FAKE_KEY) {
      problems.push(
        `那一行里敲的 key 与档案编辑处那一格对不上（档案里是「${inProfile}」）：` +
          "两处各存一份的话，「同一把 key 坐三席只填一次」就成了空话",
      );
    }
    // 反着改回来：档案编辑处改一把，那一行里跟着变。
    await page.getByTestId("table-profile-key").fill(OTHER_KEY);
    const inQuick = await page.getByTestId("table-quick-key").inputValue();
    if (inQuick !== OTHER_KEY) {
      problems.push(
        `在档案编辑处改了 key，那一行里还是「${inQuick}」：两处不是同一个值，人会以为自己填过了`,
      );
    }
    const model = await page.getByTestId("table-profile-model").inputValue();
    const provider = await page.getByTestId("table-profile-provider").inputValue();
    if (model !== MODEL || provider !== PROVIDER) {
      problems.push(`那一行填的 provider / 模型没进档案（档案里是「${provider}」/「${model}」）`);
    }
    console.log(
      `两处同一个值：那一行 → 档案「${inProfile === FAKE_KEY ? "逐字相同" : inProfile}」，` +
        `档案 → 那一行「${inQuick === OTHER_KEY ? "逐字相同" : inQuick}」 ${sameMark()}`,
    );

    // ⑥ 「进阶」默认收起，收起不等于清空。
    const advMark = markerSince(problems);
    const advanced = await page.evaluate(() => {
      const summary = document.querySelector('[data-testid="table-profile-advanced"]');
      const shell = summary?.closest("details") ?? null;
      const inner = ["table-profile-timeout", "table-profile-thinking"].map((testId) => [
        testId,
        document.querySelector(`[data-testid="${testId}"]`)?.checkVisibility() ?? false,
      ]);
      return { there: shell !== null, open: shell?.open ?? null, inner };
    });
    if (!advanced.there) {
      problems.push("档案编辑处没有「进阶」那一枚：超时与思考预算还摊在面上");
    }
    if (advanced.open) problems.push("「进阶」一进页面就摊开着：它该默认收起");
    for (const [testId, visible] of advanced.inner) {
      if (visible) problems.push(`「进阶」收着，[data-testid="${testId}"] 却还渲染着`);
    }
    await page.getByTestId("table-profile-advanced").click();
    const timeout = await page.getByTestId("table-profile-timeout").inputValue();
    const thinking = await page.getByTestId("table-profile-thinking").inputValue();
    // **收起不等于清空**：这一份档案摆下来时那两格就是默认值（`plantSeating` 一格都没覆盖），
    // 折起来之后它们必须还是那两个值——「折叠」是票 83 那副写法：不进 model、不碰 localStorage。
    if (thinking !== DEFAULT_THINKING) {
      problems.push(
        `敲开「进阶」，思考预算成了「${thinking}」：折起来把它清空了（该是 ${DEFAULT_THINKING}）`,
      );
    }
    if (timeout !== DEFAULT_TIMEOUT_MS) {
      problems.push(
        `敲开「进阶」，超时那一格写着「${timeout}」：折起来把它清空了（该是 ${DEFAULT_TIMEOUT_MS}）`,
      );
    }
    console.log(
      `进阶：一进页面 open=${advanced.open}　里面渲染 ${advanced.inner.filter(([, ok]) => ok).length} 个` +
        ` → 敲开后 超时 ${timeout} ms・思考 ${thinking}（默认该是 ${DEFAULT_TIMEOUT_MS} / ${DEFAULT_THINKING}）${advMark()}`,
    );
  } finally {
    await context.close();
    endpoint.kill();
  }

  if (problems.length > 0) return failure("一行式开桌那一道没过：", problems);

  console.log("配桌收着填三格、按一下〔开打〕，牌桌上就有牌在走；key 只在那两处，且是同一个值 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  await runStandalone((lane) => verifyQuickStart(lane));
}
