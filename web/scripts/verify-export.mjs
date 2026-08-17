// 牌谱导出的**无头闸门**（票 26）：浏览器里点一下「导出牌谱」，
// 接住 playwright 的 download 事件，把**真下下来的那份字节**再喂回浏览器里的引擎 fold 一遍。
//
// 验四件事：
//   1. 点一下真的下载得到一个文件（不是「代码写了没验」）；
//   2. 那份 JSON 读得动：版本号、规则集、事件流、决策记录都在；
//   3. 它的事件流被引擎重新 fold 出**逐条相同**的事件流与同一份点数（ADR-0002 的回放）；
//      **点数那一条比的是「牌桌该显示什么」**：局中的权威是那一局的 `GameState`，
//      终局的权威是 `Game`（精算之后，供托已归头名）——两侧都照这条口径取数，
//      因此它在**打完整场**的情形下同样说得通（票 39；从前不是，那时它是一条假红）。
//   4. **导出物里没有 API key**（票 34）：一把假 key 灌进 localStorage，四家仍是随机选手
//      （于是一个请求都不发），导出的文件名与字节里都不该出现它。
//
// 默认四家随机选手，**一个网络请求都不发**，因此它进 CI（M1 增量约束 6）。
// 加 `--llm` 就是人工验收那一档：一席交给真模型（key 从文件读），录出带 prompt / thinking
// 的真决策记录——**那一档调真实 provider，别放进 CI**。key 那道闸门在两档各有一条断言，
// **不是替代关系**：CI 那条守的是「key 只是躺在 localStorage 里」，手验那条守的是
// 「key 真交给过 provider、决策记录里有真 prompt 与真输出」。
//
// 跑法（它也是 `verify-browser.mjs` 里的三趟：走 40 手、打完整场、反向自证）：
//   cd web && pnpm run fable && pnpm run verify:export
//   node scripts/verify-export.mjs --to-end    # 一路打到终局精算那一屏
//   JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-export.mjs --llm --thinking medium
//   node scripts/verify-export.mjs --poison   # 反向自证：这一趟**必须**红
//
// 选项：--turns N（导出前先走几手，默认 60；一局打完就接着开下一局）、
//       --to-end（打完一整场再导出：走到终局精算出现为止，手数上限默认 4000）、
//       --seed N（开跑前先换一个种子重开一桌）、--seat N、--model X、--thinking off|low|medium|high、
//       --keep <路径>（把下下来的牌谱另存一份，报告里的样例就是这么来的）、
//       --poison（往导出物里拌一把 key，见下面 `poisoned`）。

import { copyFileSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { retryOnReload } from "./serve.mjs";
import { stepTurns } from "./table-drive.mjs";

/**
 * CI 那一档灌进 localStorage 的那把 key（票 34）。**它是假的，且看一眼就知道是假的**——
 * 这道闸门要的只是「浏览器里确实有一把 key 可以夹带」，不需要它能换来任何东西，
 * 因此它是写死的字面量，**绝不从 /tmp/deepseek_key 之类的地方读**。
 *
 * 全 ASCII 是故意的：断言是按**字节**在导出物里找它，而 JSON 编码器可以把非 ASCII
 * 写成 `\uXXXX`——那样夹带了也找不着。
 */
const FAKE_KEY = "sk-janpo-fake-key-NOT-A-REAL-KEY-jia-4f2a91";

/**
 * 反向自证（票 34）用的那份**脏牌谱**：把这一趟那把 key 塞进导出物的一个字段里，
 * 于是下面那条断言**必须**红。一道从不失败的闸门等于没有闸门——`--poison` 就是
 * 随时把它按红一次的办法，CI 里也真按（`verify-browser.mjs` 里紧跟着的那一趟）。
 *
 * 只认「这是一个 JSON 对象」，不认牌谱的具体字段，因此牌谱格式怎么变它都还在。
 */
const poisoned = (paifu, key) => JSON.stringify({ ...JSON.parse(paifu), leaked_api_key: key });

/**
 * 牌谱导出那一道。**同一段代码跑三趟**（走 40 手 / 打完整场 / 反向自证），
 * 差别只在 options，因此三趟的断言恒是同一套。
 */
export async function verifyExport(lane, options = {}) {
  const {
    withLlm = false,
    toEnd = false,
    turns = toEnd ? 4000 : 60,
    seed = null,
    seat = 1,
    model = "deepseek-v4-flash",
    thinking = "off",
    keep = null,
    budgetMs = 600000,
    poison = false,
  } = options;

  const apiKey = withLlm
    ? readFileSync(process.env.JANPO_KEY_FILE ?? "/tmp/deepseek_key", "utf8").trim()
    : null;

  /** 这一趟灌进 localStorage 的那把 key：CI 是假的，手验是真的。导出物里绝不能出现它。 */
  const plantedKey = withLlm ? apiKey : FAKE_KEY;

  // 用 dev server 而不是 preview：闸门要在页面里点名 `import` Fable 输出的 `PaifuCheck.js`，
  // 而 `dist/` 里的模块被打成一坨、文件名带哈希（与 verify-golden 同一个理由）。
  const url = await lane.devUrl();
  const context = await lane.newContext({ acceptDownloads: true });
  const problems = [];

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() !== "error") return;
      // 断电演习式的失败请求浏览器自己会往 console 写一条，那不是页面的错。
      if (message.text().includes("Failed to load resource")) return;
      problems.push(`[console.error] ${message.text()}`);
    });

    if (withLlm) {
      await page.addInitScript(
        ([seat, model, apiKey, thinking]) => {
          localStorage.setItem("janpo.llm.seat", String(seat));
          localStorage.setItem("janpo.llm.provider", "deepseek");
          localStorage.setItem("janpo.llm.model", model);
          localStorage.setItem("janpo.llm.api_key", apiKey);
          localStorage.setItem("janpo.llm.timeout_ms", "60000");
          localStorage.setItem("janpo.llm.thinking", thinking);
        },
        [seat, model, apiKey, thinking],
      );
    } else {
      // 票 34 的那一档：**key 灌进去，坐席不给**。页面照样把这把 key 从 localStorage 读进配置
      // （`Store.readSeatConfig`），但四家仍是随机选手（座位存空串就是「四家都随机」），
      // 于是一个请求都发不出去——而导出的字节里照样不该出现它。
      await page.addInitScript((fakeKey) => {
        localStorage.setItem("janpo.llm.seat", "");
        localStorage.setItem("janpo.llm.api_key", fakeKey);
      }, FAKE_KEY);
    }

    await page.goto(`${url}/`, { waitUntil: "load" });

    const readText = async (testId) => (await page.getByTestId(testId).textContent()).trim();

    // 换种子就是页面上那两下：填输入框 + 「重开」。种子 447 那一场终局时场上还剩着供托，
    // 也就是「局末点数」与「精算后点数」真的不同的那种场（票 39 的现场）。
    if (seed !== null) {
      await page.getByTestId("table-seed").fill(String(seed));
      await page.getByTestId("table-restart").click();
      console.log(`种子换成 ${seed} 重开了一桌`);
    }

    console.log(
      `模式：${withLlm ? `一席交给 ${model}（思考预算 ${thinking}）` : "四家随机选手（不发任何请求）"}　` +
        `${toEnd ? `一路打到终局（手数上限 ${turns}）` : `先走 ${turns} 手`}`,
    );
    if (!withLlm) console.log(`localStorage 里躺着一把假 key：${FAKE_KEY}`);

    // 一手一手走：单步之后要么当场落子，要么在等模型——等到「上一手」变了为止。
    // 一局打完就点「下一局」接着打；终局那一刻「下一局」也是灰的，于是停在终局那一屏。
    // 这一整段跑在**页面里**（`table-drive.mjs`）：从前每一手要四次 playwright 往返，
    // 一整场 785 手就是三千多次跨进程调用，实测占掉这一趟的九成时间（票 56）。
    const { kyokus, stuckAt } = await stepTurns(page, { limit: turns, nextKyoku: true, budgetMs });
    if (stuckAt !== null) problems.push(`第 ${stuckAt} 手没走动`);

    // 引擎拒掉了某一手（**不该发生**：提交的动作都取自合法动作集）：牌桌停在那里，闸门要说出来。
    if (await page.getByTestId("table-fault").count()) {
      problems.push(`牌桌停住了：${await readText("table-fault")}`);
    }

    const ended = (await page.getByTestId("table-result").count()) > 0;
    console.log(`打了 ${kyokus} 局${ended ? "，已到终局精算那一屏" : "（还没终局）"}`);
    if (toEnd && !ended) problems.push(`要求打完一整场，却在手数上限 ${turns} 里没走到终局`);

    console.log(`走完之后：${await readText("table-latest")}`);
    console.log(`Agent 状态：${await readText("table-agent")}`);

    // token 账单（票 29b）：四家随机选手的那一档没有这一行（一个 token 都没花）。
    if (await page.getByTestId("table-usage").count()) {
      console.log(await readText("table-usage"));
    }

    const [download] = await Promise.all([
      page.waitForEvent("download", { timeout: 30000 }),
      page.getByTestId("table-export").click(),
    ]);

    const file = await download.path();
    const text = readFileSync(file, "utf8");
    console.log("");
    console.log(`下载文件名：${download.suggestedFilename()}　${text.length} 字节`);
    if (keep) {
      copyFileSync(file, resolve(keep));
      console.log(`另存到：${resolve(keep)}`);
    }

    // 下下来的那份字节交回浏览器里的引擎：fold 一遍，与它自己记的事件流逐条对照。
    const report = JSON.parse(
      await retryOnReload(() =>
        page.evaluate(async (paifu) => {
          // 相对页面地址 import：vite 的 base 可配（JANPO_BASE），写死 "/src/…" 一改 base 就 404。
          const check = await import("./src/generated/PaifuCheck.js");
          return check.check(paifu);
        }, text),
      ),
    );

    if (report.error) {
      problems.push(`浏览器里的引擎读不动 / 回放不动这份牌谱：${report.error}`);
    } else {
      console.log(
        `牌谱：版本 ${report.version}　事件 ${report.events} 条　决策记录 ${report.decisions} 条` +
          `（其中带 thinking ${report.thinking} 条、兜底 ${report.fallbacks} 条）　已打完 ${report.kyokus} 局`,
      );
      // prompt 的前置（票 31）：尾部每手一份，preamble 整场几份（换人格才多一份）。
      console.log(
        `prompt：尾部共 ${report.tail_chars} 字　preamble ${report.preambles} 份　逐手重建得回去 = ${report.rebuildable}`,
      );
      console.log(
        `回放：事件流逐条相同 = ${report.same_events}　点数 ${report.scores.join(" / ")}`,
      );
      if (!report.same_events) problems.push(`回放出的事件流与牌谱不同：${report.mismatch}`);
      if (report.decisions > 0 && !report.rebuildable) {
        problems.push("有决策记录指不回它那一份 preamble：重建不出当时发出去的 prompt");
      }

      // 页面上的点数与回放算出来的点数必须一致——牌桌与 fold 出来的是同一场对局。
      //
      // **两边比的是同一个量**（票 39）：`report.scores` 报的是「事件流走到哪，点数就报到哪」
      // ——还在打的那一局报它此刻的点数（`GameState`），正好在某局收尾处结束的报这一场的
      // （`Game`，终局那一份是精算之后、供托已归头名）。牌桌上的座位卡照同一条口径取数，
      // 因此这条断言在**打完整场**时同样成立。从前不是：座位卡恒读最后一局的 `GameState`，
      // 于是打完整场比的是两个不同的量，红了而代码没错（那条假红就是这张票的第三症）。
      const onPage = await Promise.all(
        [0, 1, 2, 3].map(async (index) =>
          Number.parseInt(await readText(`seat-${index}-score`), 10),
        ),
      );
      if (onPage.join(",") !== report.scores.join(",")) {
        problems.push(
          `牌桌上的点数 ${onPage.join("/")} 与回放算出的 ${report.scores.join("/")} 不同`,
        );
      }

      // 终局那一屏：牌桌上只许有一种说法（票 39）。座位卡跟着精算走（上面那条已经比过），
      // 供托跟着归零、立直棒收走，结算面板也不再邀人「进下一局」。
      if (ended) {
        console.log(`终局精算：${await readText("table-result-ranking")}`);
        if (await page.getByTestId("table-result-kyotaku").count()) {
          console.log(await readText("table-result-kyotaku"));
        }
        const kyotaku = await readText("table-kyotaku");
        if (kyotaku !== "0 根") {
          problems.push(`终局之后场况行还写着「供托 ${kyotaku}」：供托在精算里已经归了头名`);
        }
        if (await page.getByTestId("table-bou").count()) {
          problems.push("终局之后桌上还画着立直棒：那几根已经归了头名");
        }
        const progress = await page.getByTestId("table-renchan").getAttribute("data-progress");
        if (progress !== "ended") {
          problems.push(
            `最后一局的结算面板写着「${await readText("table-renchan")}」，而没有下一局了`,
          );
        }
      }
      if (report.events < 10) problems.push(`只导出了 ${report.events} 条事件，这一桌根本没走动`);
      if (withLlm && report.decisions === 0) problems.push("一席交给了模型，却一条决策记录都没有");
    }

    // 可分享物里永远不能出现 API key（ADR-0003）。**导出这条路给出去的是两样东西**：
    // 那份字节，与它的文件名（文件名拼的是人随手填的种子）——两样一起查。
    const shareable = `${download.suggestedFilename()}\n${poison ? poisoned(text, plantedKey) : text}`;

    // 两条断言并存，**不是替代关系**：
    //   票 34 那条恒跑（CI 里也跑）：假 key 就躺在 localStorage 里，一个请求都没发过；
    //   票 26 那条只在 --llm：那一趟真把 key 交给了 provider，决策记录里带着真 prompt
    //   与真输出，是另一种夹带机会。
    if (!withLlm && shareable.includes(FAKE_KEY)) {
      problems.push("导出物（文件名 + 字节）里出现了 API key：灌进 localStorage 的那把假 key");
    }
    if (apiKey !== null && shareable.includes(apiKey)) problems.push("导出的牌谱里出现了 API key");
  } finally {
    await context.close();
  }

  if (problems.length > 0) return failure("导出验收没过：", problems);

  console.log("导出的牌谱下得下来、读得动、回放得回去 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };
  const toEnd = argv.includes("--to-end");

  await runStandalone((lane) =>
    verifyExport(lane, {
      withLlm: argv.includes("--llm"),
      toEnd,
      turns: Number.parseInt(flag("--turns", toEnd ? "4000" : "60"), 10),
      seed: flag("--seed", null),
      seat: Number.parseInt(flag("--seat", "1"), 10),
      model: flag("--model", "deepseek-v4-flash"),
      thinking: flag("--thinking", "off"),
      keep: flag("--keep", null),
      budgetMs: Number.parseInt(flag("--budget", "600000"), 10),
      poison: argv.includes("--poison"),
    }),
  );
}
