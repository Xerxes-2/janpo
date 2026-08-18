// **ToolSearch 档整条链路**那道闸门（票 94）：模型发起工具调用 → 拿到答案 → 出牌。
//
// 上一层（`node --test`）钉的是渲染与判读：查出来那一行与 Assisted 档逐字节相同、
// 前缀的分叉恰好是那一段、上限到了就不给工具。**那一层喂的是构造出来的回答**。
// 这一道钉的是**真的一条 HTTP tool call 走完整个来回**：本机假端点回一段真的 SSE，
// 名字就叫 `what_if`，过 pi-ai 的适配器、过决策循环、落进牌谱——四件事：
//
//   ① **链路走通**：先查两次、再出牌那一席，每一手的记录里都有查询与答案，
//      而且 `applied` 有值、`fallback` 是空的（**这一手是它自己决出来的，不是兜底**）；
//   ② **到上限就停，这一手照常打完**：另一席的端点「能查就查」，
//      记录里恒是「你查过 4 次，还可以再查 0 次」+ 4 条答案，而且照样 0 兜底；
//   ③ **账单真的算了几倍**：假端点每次回同一份 usage，因此
//      `usage.input = (查询次数 + 1) × 每次那个数`——这是「多轮往返的代价进得了牌谱」的硬证据；
//   ④ **Bare 那一席一个字都没变**：它的尾部里一行查询都没有，工具也从没摆到它面前。
//
// **全程本机**（页面是本地 dev server、端点是 `fake-endpoint.mjs`），一个字节都不出网，
// 因此它进 CI。它也是 `verify-browser.mjs` 里的一趟。
//
//   cd web && pnpm run fable && node scripts/verify-toolsearch.mjs
//
// 选项：--budget ms、--turns N、--keep <路径>（把导出的牌谱另存一份）。

import { spawn } from "node:child_process";
import { copyFileSync, readFileSync } from "node:fs";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { WHAT_IF_LIMIT } from "../src/agent/what-if.ts";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { plantSeating, profileChoice } from "./seating.mjs";
import { hostPage } from "./serve.mjs";
import { stepTurns } from "./table-drive.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** 先查两次再出牌的那一席（**没到上限**，因此走的是「自己收手」那一条路）。 */
const PATIENT = 2;

/** 假端点每次回的那份 usage 里的 `prompt_tokens`（`fake-endpoint.mjs` 里写死的）。 */
const INPUT_PER_CALL = 814;

/** 三席的坐法：查两次的、能查就查的、以及一席裸奔档的对照。座位 3 留给 bot。 */
const SEATS = [
  { seat: 0, tier: "tool_search", whatIf: String(PATIENT), asks: PATIENT },
  { seat: 1, tier: "tool_search", whatIf: "inf", asks: WHAT_IF_LIMIT },
  { seat: 2, tier: "bare", whatIf: "0", asks: 0 },
];

/** 借内核要一个空闲端口（跑批是并行的，写死端口迟早撞上另一个工作区）。 */
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
async function startEndpoint(origin, extra) {
  const port = await freePort();
  const endpoint = spawn(
    "node",
    ["scripts/fake-endpoint.mjs", "--port", String(port), "--cors", origin, "--quiet", ...extra],
    { cwd: webRoot, stdio: ["ignore", "ignore", "inherit"] },
  );
  return { baseUrl: `http://127.0.0.1:${port}/v1`, endpoint };
}

/**
 * 一条记录里那一节查询：查了几次、答了几条、还剩几次。读的是**牌谱存下来的尾部**。
 *
 * `discards` 是这一手有没有牌可打（【可选动作】里有没有一条 `打 X`）——
 * **响应那一手没有「打完之后」可算，工具因此一轮都不给**，那一手的查询数就该是 0。
 * 两条路各有各的期望，不把它们揉成一条宽断言。
 */
function queriesOf(record) {
  const tail = record.prompt_tail ?? "";
  const counted = /^你查过 (\d+) 次，还可以再查 (\d+) 次：$/m.exec(tail);
  const answers = tail.split("\n").filter((line) => /^- id=\d+（.+?）：打完 /.test(line)).length;

  return {
    asked: counted === null ? 0 : Number(counted[1]),
    left: counted === null ? null : Number(counted[2]),
    answers,
    discards: /^- id=\d+：打 /m.test(tail),
  };
}

/** ToolSearch 档整条链路。返回失败清单（空 = 绿）。 */
export async function verifyToolSearch(lane, options = {}) {
  const { budgetMs = 30000, turns = 24, keep = null } = options;
  const url = await lane.devUrl();
  const pageOrigin = new URL(url).origin;
  const problems = [];

  const endpoints = [];
  for (const each of SEATS) {
    endpoints.push(await startEndpoint(pageOrigin, ["--what-if", each.whatIf]));
  }

  const context = await lane.newContext({ acceptDownloads: true });

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    await plantSeating(page, {
      profiles: SEATS.map((each, index) => ({
        name: `工具闸门的档案（座位 ${each.seat}）`,
        provider: "custom-openai",
        model: `fake-model-${each.seat}`,
        base_url: endpoints[index].baseUrl,
        timeout_ms: "10000",
      })),
      seats: [
        ...SEATS.map((each) => ({
          choice: profileChoice(`工具闸门的档案（座位 ${each.seat}）`),
          tier: each.tier,
        })),
        { choice: "random" },
      ],
    });

    console.log(`页面 ${pageOrigin}`);
    for (const [index, each] of SEATS.entries()) {
      console.log(
        `  座位 ${each.seat}　${each.tier} 档　${endpoints[index].baseUrl}　` +
          `端点会先查 ${each.whatIf} 次 what-if`,
      );
    }

    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });
    await page.getByTestId("table-restart").click();

    const { walked, stuckAt } = await stepTurns(page, { limit: turns, budgetMs });
    if (stuckAt !== null) problems.push(`第 ${stuckAt} 手没走动（单手预算 ${budgetMs} ms）`);
    if (await page.getByTestId("table-fault").count()) {
      problems.push(`牌桌停住了：${(await page.getByTestId("table-fault").textContent()).trim()}`);
    }

    const [download] = await Promise.all([
      page.waitForEvent("download", { timeout: 30000 }),
      page.getByTestId("table-export").click(),
    ]);
    const file = await download.path();
    const text = readFileSync(file, "utf8");
    if (keep) copyFileSync(file, resolve(keep));

    const paifu = JSON.parse(text);
    const records = paifu.decisions ?? [];
    console.log(`走了 ${walked} 手，牌谱 ${text.length} 字节、决策记录 ${records.length} 条`);

    // 工具定义的形状整场存一份（票 31）：这一场有 ToolSearch 席，因此它里面看得见 `what_if`。
    const shape = paifu.prompting?.tools ?? "";
    if (!shape.includes("what_if")) {
      problems.push(`牌谱里那份工具定义的形状里没有 what_if：${shape.slice(0, 200)}`);
    }

    for (const each of SEATS) {
      const mine = records.filter((record) => record.seat === each.seat);
      if (mine.length === 0) {
        problems.push(`座位 ${each.seat} 一条决策记录都没有——底下几条断言在空转`);
        continue;
      }

      const counted = mine.map(queriesOf);
      const fallbacks = mine.filter((record) => typeof record.fallback === "string");
      const applied = mine.filter((record) => typeof record.applied === "number");
      const inputs = [...new Set(mine.map((record) => record.usage?.input ?? 0))];

      console.log(
        `  座位 ${each.seat}（${each.tier}）：记录 ${mine.length} 条、` +
          `每手查 ${[...new Set(counted.map((c) => c.asked))].join("/")} 次、` +
          `答案 ${[...new Set(counted.map((c) => c.answers))].join("/")} 条、` +
          `兜底 ${fallbacks.length}、输入 tok ${inputs.join("/")}`,
      );

      // ①②：查了几次、答了几条、还剩几次——三个数必须互相对得上，且等于这一席该有的那个。
      const playable = counted.filter((count) => count.discards);
      if (playable.length === 0) {
        problems.push(`座位 ${each.seat} 这一趟一手牌都没打过——底下几条断言在空转`);
      }

      for (const [hand, count] of counted.entries()) {
        // 有牌可打那几手才该查；响应那几手工具一轮都不给（空 enum 等于邀它烧一次额度）。
        const wanted = count.discards ? each.asks : 0;
        if (count.asked !== wanted) {
          problems.push(
            `座位 ${each.seat} 第 ${hand} 条记录查了 ${count.asked} 次，该是 ${wanted} 次` +
              `（这一手${count.discards ? "有" : "没有"}牌可打）`,
          );
          break;
        }
        if (count.answers !== wanted) {
          problems.push(
            `座位 ${each.seat} 第 ${hand} 条记录说查了 ${count.asked} 次，却摆了 ${count.answers} 条答案`,
          );
          break;
        }
        if (count.left !== null && count.asked + count.left !== WHAT_IF_LIMIT) {
          problems.push(
            `座位 ${each.seat} 第 ${hand} 条记录：已查 ${count.asked} + 还能查 ${count.left} ≠ 上限 ${WHAT_IF_LIMIT}`,
          );
          break;
        }
      }

      // ①②：**这一手照常打完**——它自己决出来的，不是兜底代打的。
      if (fallbacks.length > 0) {
        problems.push(
          `座位 ${each.seat} 有 ${fallbacks.length} 手兜底：${fallbacks[0].fallback}` +
            `（ToolSearch 档到了上限该照常出牌，不该卡到兜底）`,
        );
      }
      if (applied.length !== mine.length) {
        problems.push(`座位 ${each.seat} 有 ${mine.length - applied.length} 手没落定动作`);
      }

      // ③：账单是 (这一手查了几次 + 1) 倍。假端点每次回同一份 usage，因此这个乘数是确定的。
      for (const [hand, record] of mine.entries()) {
        const billed = (counted[hand].asked + 1) * INPUT_PER_CALL;
        if ((record.usage?.input ?? 0) !== billed) {
          problems.push(
            `座位 ${each.seat} 第 ${hand} 条记录的输入 tok 是 ${record.usage?.input}，` +
              `该是 ${billed}（${counted[hand].asked + 1} 次请求 × ${INPUT_PER_CALL}）`,
          );
          break;
        }
      }

      // ④：裸奔那一席的尾部里，一行查询都不许有。
      if (each.tier === "bare" && counted.some((count) => count.left !== null)) {
        problems.push(`座位 ${each.seat} 是 bare 档，尾部里却出现了查询那一节`);
      }
    }

    // ④ 的另一半：ToolSearch 那两席的 preamble 里有那一段工具说明，bare 那一席没有。
    for (const preamble of paifu.prompting?.preambles ?? []) {
      const tier = SEATS.find((each) => each.seat === preamble.seat)?.tier;
      const has = preamble.text.includes("【打之前你可以先问】");
      if (tier === undefined) continue;
      if ((tier === "tool_search") !== has) {
        problems.push(
          `座位 ${preamble.seat} 是 ${tier} 档，它的 preamble 里${has ? "却有" : "却没有"}那一段工具说明`,
        );
      }
    }
  } finally {
    await context.close();
    for (const each of endpoints) each.endpoint.kill();
  }

  if (problems.length > 0) return failure("ToolSearch 档那条链路没走通：", problems);
  console.log(
    `工具调用整条链路走通了 ✓（查两次那一席自己收手、能查就查那一席到上限 ${WHAT_IF_LIMIT} 次就停，` +
      `两席都 0 兜底；账单是 (查询次数 + 1) 倍；bare 那一席一行查询都没有）`,
  );
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };

  await runStandalone((lane) =>
    verifyToolSearch(lane, {
      budgetMs: Number.parseInt(flag("--budget", "30000"), 10),
      turns: Number.parseInt(flag("--turns", "24"), 10),
      keep: flag("--keep", null),
    }),
  );
}
