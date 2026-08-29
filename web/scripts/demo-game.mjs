// 首页 Demo 资产的产线（票 79）。**不是闸门、不进 CI**：它拿真 key 跑一场四席真对局，
// 把导出的牌谱存下来，并把挑局与记账要的数一次算齐（墙钟、延迟分档、账单、兜底、事件形态）。
//
// 为什么不并进 `verify-export --llm`：那一档是**一份档案坐几席**的手验（票 73 的对照形态），
// 而 Demo 资产要的是**两份档案**（思考档 / 直觉档）加四句各不相同的人格——思考预算是档案的
// 字段不是座位的，混开思考就得有第二份档案。人格写死在这里是故意的：**它们是资产的出处**，
// 换成命令行参数之后「首页那一场是谁在打」就没人说得清了。
//
// key 的规矩（AGENTS.md 硬约束 4）：key 只从 `JANPO_KEY_FILE`（默认 /tmp/deepseek_key）读，
// 落进浏览器 localStorage 为止；导出的字节与文件名里出现它就当场红——与 verify-export 同一条断言。
//
// 跑法（一场一条命令，跑完看账再开下一场）：
//   cd web && pnpm run fable
//   JANPO_KEY_FILE=/tmp/deepseek_key node scripts/demo-game.mjs --seed 79 \
//     --keep /tmp/79-games/seed-79.json
//
// 选项：--seed N（必给：报告要写清这一场从哪颗种子开的）、
//       --thinkers 0（开思考预算的那几席，默认只有座位 0；`none` = 四席全不开思考）、
//       --tier bare|assisted|tool_search（四席的脚手架档位，默认 bare；`assisted` 会在 prompt 尾部
//         多一节算好的向听与逐张试打；`tool_search` 不推那一节，而是多给它一个 what-if 工具，票 94）、
//       --thinking low|medium（思考档的预算，默认 low）、
//       --model X（默认 deepseek-v4-flash）、--keep <路径>（导出的牌谱存哪儿）、
//       --turns N（手数上限，默认 4000 = 打到终局为止）、
//       --budget N（单手最多等多少 ms，默认 900000：thinking 档单手可到几分钟）。

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { plantSeating, profileChoice } from "./seating.mjs";
import { hostPage } from "./serve.mjs";
import { openSetup, stepTurns } from "./table-drive.mjs";

/** 两份档案在库里的叫法。**只活在本机 localStorage**，牌谱里出现它们就是泄漏。 */
const THINKER_PROFILE = "思考档";
const INSTINCT_PROFILE = "直觉档";

/**
 * 四句人格，一席一句（票 73 的对照形态：同一个模型，自变量只有人格）。
 * 防守 / 进攻 / 爱鸣牌 / 中庸——ADR-0003 说 Demo 是产品资产，这四句就是资产的配方。
 */
export const PERSONAS = [
  "你是谨慎的防守派：别家立直或场面转凶就立刻缩手，宁可流局也不放铳；只有手牌真值钱才继续押进。",
  "你是不要命的进攻派：一心只想最快和了与大牌，能立直就立直，危险牌也敢切。",
  "你最爱鸣牌：能碰就碰、能吃就吃，靠副露把速度拉满，役牌一露头就叫。",
  "你是中庸的实战派：按牌效率打，攻守随局面平衡，不勉强也不胆怯。",
];

/** 中位数与 p95（延迟分档报数用；样本空时报 0，别抛）。 */
const quantiles = (samples) => {
  if (samples.length === 0) return { median: 0, p95: 0 };
  const sorted = [...samples].sort((left, right) => left - right);
  const at = (ratio) => sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * ratio))];
  return { median: at(0.5), p95: at(0.95) };
};

/** 跑一场，导出并记账。返回失败清单（空 = 这一场跑完且账算齐了）。 */
export async function playDemoGame(lane, options) {
  const {
    seed,
    thinkers = [0],
    thinking = "low",
    tier = "bare",
    model = "deepseek-v4-flash",
    keep,
    turns = 4000,
    budgetMs = 900000,
  } = options;

  const apiKey = readFileSync(process.env.JANPO_KEY_FILE ?? "/tmp/deepseek_key", "utf8").trim();
  const url = await lane.devUrl();
  const context = await lane.newContext({ acceptDownloads: true });
  const problems = [];

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() !== "error") return;
      if (message.text().includes("Failed to load resource")) return;
      problems.push(`[console.error] ${message.text()}`);
    });

    // 两份档案：除思考预算外六格全同——对照实验的自变量因此只有「思考」与「人格」两个，
    // 而人格逐席不同、思考逐档不同，谁贡献了什么在牌谱里分得开。
    // **只摆真有人坐的那几份**（`--thinkers none` 下只有直觉档、四席全开时只有思考档）：
    // 档案库里留一份没人坐的档案，事后读牌谱的人会以为那一场开过（或没开）思考。
    const profile = (name, budget) => ({
      name,
      provider: "deepseek",
      model,
      api_key: apiKey,
      timeout_ms: "240000",
      thinking: budget,
    });

    const seated = (index) => thinkers.includes(index);
    const profiles = [
      ...(PERSONAS.some((_, index) => seated(index)) ? [profile(THINKER_PROFILE, thinking)] : []),
      ...(PERSONAS.some((_, index) => !seated(index)) ? [profile(INSTINCT_PROFILE, "off")] : []),
    ];

    await plantSeating(page, {
      profiles,
      seats: PERSONAS.map((persona, index) => ({
        choice: profileChoice(thinkers.includes(index) ? THINKER_PROFILE : INSTINCT_PROFILE),
        tier,
        persona,
      })),
    });

    await page.goto(hostPage(url), { waitUntil: "load" });
    await openSetup(page);
    await page.getByTestId("table-seed").fill(String(seed));
    await page.getByTestId("table-restart").click();

    const thinkingNote =
      thinkers.length === 0
        ? "四席都不开思考"
        : thinkers.length === PERSONAS.length
          ? `四席全开思考（${thinking}）`
          : `座位 ${thinkers.join("、")} 开思考（${thinking}）、其余不开`;
    console.log(`种子 ${seed}　四席全是 ${model}　${thinkingNote}　脚手架 ${tier} 档　人格各一句`);

    const started = Date.now();
    const { walked, kyokus, stuckAt } = await stepTurns(page, {
      limit: turns,
      nextKyoku: true,
      budgetMs,
    });
    const wallMs = Date.now() - started;

    if (stuckAt !== null) problems.push(`第 ${stuckAt} 手没走动（单手预算 ${budgetMs} ms）`);
    if (await page.getByTestId("table-fault").count()) {
      problems.push(`牌桌停住了：${(await page.getByTestId("table-fault").textContent()).trim()}`);
    }

    const ended = (await page.getByTestId("table-result").count()) > 0;
    console.log(
      `走了 ${walked} 手、${kyokus} 局，墙钟 ${(wallMs / 1000).toFixed(1)} s${ended ? "，已到终局精算" : "（没打完）"}`,
    );
    if (ended) {
      console.log(`终局：${(await page.getByTestId("table-result-ranking").textContent()).trim()}`);
    } else {
      problems.push(`这一场在 ${turns} 手内没打到终局`);
    }

    const [download] = await Promise.all([
      page.waitForEvent("download", { timeout: 30000 }),
      page.getByTestId("table-export").click(),
    ]);
    const text = readFileSync(await download.path(), "utf8");

    // key 绝不上路：字节 + 文件名一起查（verify-export 同一条口径）。
    if (`${download.suggestedFilename()}\n${text}`.includes(apiKey)) {
      problems.push("导出的牌谱（文件名 + 字节）里出现了 API key");
    }

    if (keep) {
      mkdirSync(dirname(resolve(keep)), { recursive: true });
      writeFileSync(resolve(keep), text);
      console.log(`牌谱存到：${resolve(keep)}（${text.length} 字节）`);
    }

    // ---- 记账（M3 定基线要的数，宁多勿漏） ----

    const paifu = JSON.parse(text);
    const decisions = paifu.decisions ?? [];
    const events = paifu.events ?? [];

    const count = (type) => events.filter((event) => event.type === type).length;
    const nakis = ["pon", "chi", "daiminkan", "kakan", "ankan"].map(count);
    const outcomes = events.filter((event) => event.type === "hora" || event.type === "ryukyoku");
    const endedWith = outcomes.at(-1)?.type ?? "（一局都没打完）";

    console.log(
      `事件 ${events.length} 条：立直成立 ${count("reach_accepted")}、` +
        `副露 ${nakis.reduce((sum, each) => sum + each, 0)}（碰${nakis[0]}/吃${nakis[1]}/明杠${nakis[2]}/加杠${nakis[3]}/暗杠${nakis[4]}）、` +
        `和了 ${count("hora")}、流局 ${count("ryukyoku")}、以「${endedWith}」终`,
    );

    const fallbacks = decisions.filter((record) => typeof record.fallback === "string");
    const retried = decisions.filter((record) => record.attempts > 1);
    console.log(
      `问话 ${decisions.length} 次：兜底 ${fallbacks.length} 手（${((fallbacks.length * 100) / Math.max(1, decisions.length)).toFixed(1)}%）、` +
        `重试过的 ${retried.length} 次、带 thinking ${decisions.filter((record) => typeof record.thinking === "string").length} 条`,
    );
    for (const record of fallbacks) {
      console.log(`  兜底＠第 ${record.turn} 手・座位 ${record.seat}：${record.fallback}`);
    }

    // 延迟分两档报：开思考的席与没开的席（气泡的「等待感」就是这两组数）。
    // ToolSearch 档才有的那一组数（票 94）：**模型实际问了几次**。
    // 读的是牌谱存下来的尾部（可观测性就是它），不是另记一份计数器。
    // **分母只算「真给了工具那几手」**：响应那几手没牌可打，工具一轮都不给，
    // 把它们算进去会把「它问得多勤」读得偏低（判据 13：说清条件）。
    if (tier === "tool_search") {
      const asked = decisions.map((record) => ({
        seat: record.seat,
        // 有牌可打才给工具（`【可选动作】` 里有一条 `打 X`）。
        offered: /^- id=\d+：打 /m.test(record.prompt_tail ?? ""),
        times: Number(/^你查过 (\d+) 次/m.exec(record.prompt_tail ?? "")?.[1] ?? "0"),
      }));
      const offered = asked.filter((each) => each.offered);
      const times = quantiles(offered.map((each) => each.times));
      const requests = decisions.length + asked.reduce((sum, each) => sum + each.times, 0);

      console.log(
        `what-if：给过工具的 ${offered.length} / ${decisions.length} 手，` +
          `其中真去问的 ${offered.filter((each) => each.times > 0).length} 手；` +
          `每手问几次：中位 ${times.median}、p95 ${times.p95}、` +
          `最多 ${offered.reduce((most, each) => Math.max(most, each.times), 0)}；` +
          `总请求 ${requests} 次（= ${decisions.length} 手 + ${requests - decisions.length} 次查询）`,
      );
      for (const seat of [0, 1, 2, 3]) {
        const mine = offered.filter((each) => each.seat === seat);
        if (mine.length === 0) continue;
        const each = quantiles(mine.map((one) => one.times));
        console.log(
          `  座位 ${seat}：给过工具 ${mine.length} 手、真问的 ${mine.filter((one) => one.times > 0).length} 手、` +
            `中位 ${each.median}、p95 ${each.p95}`,
        );
      }
    }

    const latencies = (mine) =>
      decisions
        .filter((record) => thinkers.includes(record.seat) === mine)
        .map((record) => record.latency_ms);
    const thoughtful = quantiles(latencies(true));
    const instinct = quantiles(latencies(false));
    console.log(
      `单手延迟：思考档 中位 ${thoughtful.median} ms、p95 ${thoughtful.p95} ms（${latencies(true).length} 次）；` +
        `直觉档 中位 ${instinct.median} ms、p95 ${instinct.p95} ms（${latencies(false).length} 次）`,
    );

    const usage = decisions.reduce(
      (sum, record) => ({
        input: sum.input + (record.usage?.input ?? 0),
        output: sum.output + (record.usage?.output ?? 0),
        cacheRead: sum.cacheRead + (record.usage?.cache_read ?? 0),
      }),
      { input: 0, output: 0, cacheRead: 0 },
    );
    const prompt = usage.input + usage.cacheRead;
    console.log(
      `账单：输入 ${prompt} tok（缓存命中 ${usage.cacheRead}，${prompt === 0 ? 0 : Math.round((usage.cacheRead * 100) / prompt)}%）、输出 ${usage.output} tok`,
    );

    for (const seat of [0, 1, 2, 3]) {
      const mine = decisions.filter((record) => record.seat === seat);
      const preamble = (paifu.prompting?.preambles ?? []).find((each) => each.seat === seat);
      console.log(
        `  座位 ${seat}：决策 ${mine.length} 条、兜底 ${mine.filter((record) => typeof record.fallback === "string").length}、` +
          `preamble ${preamble ? `${preamble.text.length} 字` : "没有"}`,
      );
    }
  } finally {
    await context.close();
  }

  if (problems.length > 0) return failure("这一场没跑利索：", problems);
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };
  const seed = flag("--seed", null);
  if (seed === null) {
    console.error("--seed 必给：报告要写清这一场从哪颗种子开的");
    process.exit(1);
  }

  /** `--thinkers` 收「座位表」或 `none`。**`none` 得有个说法**，不然它会解析成 `[NaN]`
   *  ——那种「一席都不开思考」的场子跑起来看着正常，记账那一段却会把四席全算成直觉档之外。 */
  const seatList = (text) =>
    text === "none" ? [] : text.split(",").map((each) => Number.parseInt(each, 10));

  await runStandalone((lane) =>
    playDemoGame(lane, {
      seed: Number.parseInt(seed, 10),
      thinkers: seatList(flag("--thinkers", "0")),
      tier: flag("--tier", "bare"),
      thinking: flag("--thinking", "low"),
      model: flag("--model", "deepseek-v4-flash"),
      keep: flag("--keep", null),
      turns: Number.parseInt(flag("--turns", "4000"), 10),
      budgetMs: Number.parseInt(flag("--budget", "900000"), 10),
    }),
  );
}
