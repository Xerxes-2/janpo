// prompt 的**语义不变量**闸门（票 41，票 49 把语料喂饱）：扫一批**真实对局**，逐手渲染两档，
// 再逐条问「这句中文在日麻规则下可不可能」。
//
// 它与既有那十余道闸门补的不是同一个缺口：
//   - 黄金用例逐字段钉的是**决策包**（结构化数据，里面是绝对座位，永远没错）；
//   - `prefix.test.ts` 钉的是**字节单调**（这一手与上一手是不是同一串字节）；
//   - 这一道钉的是**那句话本身成不成立**——票 40 的「吃 2p（来自对家）」两道都绿地漏了过去。
//
// 判据在 `tests/agent/invariants.ts`（十二条只读文本）与两道对拍：
// `tests/agent/naki-crosscheck.ts`（副露来源 ↔ 决策包里的绝对座位）与
// `tests/agent/label-crosscheck.ts`（可选动作那一行 ↔ 引擎自己渲的中文 label，票 95）。
// **用例与这里共用同一份**，两处说法因此一致。
//
// **两批语料是掺进来的，不是换掉的**（票 49）：有主见的选手立直多但**从不吃**，
// 均匀随机有吃碰杠但几乎不立直（2000 颗种子只 15 场）。少哪一批都会让某几条断言空转。
//
// 跑法（在 web/ 下）：
//   node scripts/verify-invariants.mjs                    默认那两批种子
//   node scripts/verify-invariants.mjs --seeds 60         两批各从 --from 起扫 60 颗（离线大扫）
//   node scripts/verify-invariants.mjs --from 500 --seeds 20
//   node scripts/verify-invariants.mjs --jobs 2           少占几个核（默认 4，跑批机器上还有别的 agent）
//   node scripts/verify-invariants.mjs --show 40          多印几条违反（默认 10）
//
// **零网络请求**：局面来自 `janpo decide <种子> --seat N --sequence`（本机跑引擎），
// 渲染与断言全在本进程里。

import { spawn, spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderPrompt } from "../src/agent/prompt.ts";
import { PROOFS } from "../tests/agent/invariant-proofs.ts";
import { formatViolation, promptAudit, promptCoverage, RULES } from "../tests/agent/invariants.ts";
import {
  crosscheckLabels,
  LABEL_CROSSCHECK,
  LABEL_CROSSCHECK_PROOFS,
} from "../tests/agent/label-crosscheck.ts";
import {
  CROSSCHECK_PROOFS,
  crosscheckNaki,
  NAKI_CROSSCHECK,
} from "../tests/agent/naki-crosscheck.ts";

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, "../..");

/**
 * 两批语料。**掺，不是替换**（票 49）：
 *
 * - **均匀随机**那六颗不是随手挑的（票 41）：`janpo kyoku` 扫了 1-300 之后按副露形态选出来的，
 *   五种副露与「明杠欠着一张宝牌指示牌」那种局面都得真出现过。**立直一颗都没有**
 *   （均匀随机 2000 颗种子只有 15 场立直成立），因此「立直后全摸切」在这一批上永远空转。
 * - **有主见的**那三颗（票 42 的选手，`--opinionated`）补的正是立直：三颗一共 1,300 余次
 *   「立直之后打的那一张」。代价是它**从不吃**（规则 3：吃不带役），也几乎不杠——
 *   吃与三种杠仍由上面那一批供。
 *
 * 哪一项掉到 0，这一趟当场喊停（底下「形态覆盖」与「逐条执行次数」两道）。
 */
const CORPORA = [
  { name: "均匀", flags: [], seeds: [7, 92, 100, 106, 114, 237] },
  { name: "有主见", flags: ["--opinionated"], seeds: [8, 10, 35] },
];

const SEATS = [0, 1, 2, 3];
const TIERS = ["bare", "assisted"];

function parseArguments(argv) {
  const parsed = { seeds: null, from: 1, jobs: 4, show: 10 };
  for (let index = 0; index < argv.length; index += 1) {
    const key = argv[index].replace(/^--/, "");
    if (!(key in parsed)) throw new Error(`不认识的参数：${argv[index]}`);
    index += 1;
    parsed[key] = Number(argv[index]);
  }
  // 跑批机器上还有别的 agent 在编译与跑测试：并行度**显式封顶 4**（RUNBOOK 的资源预算）。
  parsed.jobs = Math.max(1, Math.min(4, parsed.jobs));
  return parsed;
}

const parsed = parseArguments(process.argv.slice(2));
const corpora =
  parsed.seeds === null
    ? CORPORA
    : CORPORA.map((corpus) => ({
        ...corpus,
        seeds: Array.from({ length: parsed.seeds }, (_, index) => parsed.from + index),
      }));

// 先编一次，之后直接跑编出来的那个 dll。**不走 `dotnet run --no-build`**：那个每趟还要
// 过一遍 MSBuild 求值（实测 0.66s vs 0.27s），36 趟下来就是三秒多。
// 路径向 MSBuild 要（`TargetPath`）而不是拼出来：目标框架改了不该让这道闸门静静地坏掉。
const built = spawnSync("dotnet", ["build", "src/Janpo.Cli", "-c", "Release"], {
  cwd: repo,
  encoding: "utf8",
});
if (built.status !== 0) throw new Error(`janpo 编不出来：${built.stderr || built.stdout}`);

const located = spawnSync(
  "dotnet",
  ["build", "src/Janpo.Cli", "-c", "Release", "--getProperty:TargetPath"],
  { cwd: repo, encoding: "utf8" },
);
const cli = located.stdout.trim();
if (located.status !== 0 || !existsSync(cli))
  throw new Error(`找不到编出来的 janpo：「${cli}」${located.stderr}`);

/** 一局里某座位被问到的每一手（`janpo decide --sequence`），一次一个子进程。 */
function decideSequence(corpus, seed, seat) {
  const args = [cli, "decide", String(seed), "--seat", String(seat), "--sequence", ...corpus.flags];

  return new Promise((done, fail) => {
    const child = spawn("dotnet", args, { cwd: repo });
    // **两条流都先定成 utf8**：不定的话拿到的是 `Buffer`，而 `out += chunk` 是**逐块**
    // 转字符串——一个汉字正好被切在两块之间时就会碎成「6\ufffd\ufffd」，
    // 于是「label 里的『6』不是一个牌名」这种**假红**只在机器忙的时候偶尔出现一次
    // （判据 16：并行跑批时看到红，先怀疑基础设施。2026-08-18 真撞上过一次）。
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    let out = "";
    let err = "";
    child.stdout.on("data", (chunk) => {
      out += chunk;
    });
    child.stderr.on("data", (chunk) => {
      err += chunk;
    });
    child.on("close", (code) => {
      if (code !== 0)
        fail(
          new Error(
            `janpo decide ${seed} --seat ${seat} ${corpus.flags.join(" ")} 跑不出来：${err || out}`,
          ),
        );
      else done({ corpus, seed, seat, packages: JSON.parse(out) });
    });
  });
}

/** 最多 `jobs` 个子进程同时在跑。 */
async function pooled(tasks, jobs) {
  const results = [];
  let next = 0;
  const workers = Array.from({ length: Math.min(jobs, tasks.length) }, async () => {
    while (next < tasks.length) {
      const mine = next;
      next += 1;
      results[mine] = await tasks[mine]();
    }
  });
  await Promise.all(workers);
  return results;
}

/** 把一份「键 → 次数」加进另一份。 */
function add(into, counts) {
  for (const [key, count] of Object.entries(counts)) into[key] = (into[key] ?? 0) + count;
  return into;
}

// ---- 扫 ----

const started = Date.now();
const jobs = corpora.flatMap((corpus) =>
  corpus.seeds.flatMap((seed) => SEATS.map((seat) => () => decideSequence(corpus, seed, seat))),
);
const sequences = await pooled(jobs, parsed.jobs);

/**
 * 语料：一份 prompt、渲它的那份决策包、以及它是哪一手。
 *
 * **两档都渲**（档位只动尾部，但尾部也是话）；**包要留着**——「文本说法 ↔ 绝对座位」
 * 那道对拍要拿它当另一半（票 49 第二节）。
 */
const corpus = [];
for (const { corpus: which, seed, seat, packages } of sequences) {
  for (const [hand, decision] of packages.entries()) {
    for (const tier of TIERS) {
      corpus.push({
        which: which.name,
        where: `${which.name} 种子 ${seed}・座位 ${seat}・第 ${hand} 手・${tier} 档`,
        decision,
        prompt: renderPrompt(decision, tier, null),
      });
    }
  }
}

if (corpus.length === 0) throw new Error("一份 prompt 都没渲出来——语料是空的，这一道等于没跑");

/** 逐批分开记：**哪一批喂饱了哪几条**是这一票要回答的问题。 */
const tally = new Map(
  corpora.map((each) => [
    each.name,
    { prompts: 0, coverage: {}, judged: {}, crosschecked: {}, labelled: {} },
  ]),
);
const violations = [];
const mismatches = [];
const mislabelled = [];

for (const each of corpus) {
  const mine = tally.get(each.which);
  mine.prompts += 1;

  const audit = promptAudit(each.prompt, { where: each.where });
  violations.push(...audit.violations);
  add(mine.judged, audit.judged);
  add(mine.coverage, promptCoverage(each.prompt));

  const crosscheck = crosscheckNaki(each.decision, each.prompt, each.where);
  mismatches.push(...crosscheck.violations);
  add(mine.crosschecked, crosscheck.judged);

  const labels = crosscheckLabels(each.decision, each.prompt, each.where);
  mislabelled.push(...labels.violations);
  add(mine.labelled, labels.judged);
}

const total = (pick) => corpora.reduce((into, each) => add(into, pick(tally.get(each.name))), {});
const coverage = total((each) => each.coverage);
const judged = total((each) => each.judged);
const crosschecked = total((each) => each.crosschecked);
const labelled = total((each) => each.labelled);

const seeds = corpora.reduce((count, each) => count + each.seeds.length, 0);
const hands = corpus.length / TIERS.length;
console.log(
  `扫了 ${seeds} 颗种子 × ${SEATS.length} 座位 = ${hands} 手，` +
    `每手渲 ${TIERS.length} 档 = ${corpus.length} 份 prompt（${((Date.now() - started) / 1000).toFixed(1)}s）`,
);

for (const each of corpora) {
  const mine = tally.get(each.name);
  console.log(
    `  ${each.name}（${each.seeds.length} 颗）：${mine.prompts} 份 prompt、` +
      `副露 ${mine.coverage.naki ?? 0} 组（吃 ${mine.coverage.chi ?? 0}、碰 ${mine.coverage.pon ?? 0}、` +
      `暗杠 ${mine.coverage.ankan ?? 0}、大明杠 ${mine.coverage.daiminkan ?? 0}、加杠 ${mine.coverage.kakan ?? 0}）、` +
      `宝牌指示牌 ${mine.coverage.dora ?? 0} 张、历史 ${mine.coverage.historyLines ?? 0} 行、` +
      `可选动作 ${mine.coverage.actions ?? 0} 条、立直宣言 ${mine.coverage.riichi ?? 0} 次`,
  );
}

// ---- 逐条不变量各执行了多少次（票 49 的核心） ----
//
// **判据升级了**（DECISIONS「票 41 与 42 撞出一件事」）：以前只问「这道闸门失败过吗」，
// 现在要多问一句「**它在真语料上执行过几次**」。反向自证只证明「这条断言写对了」，
// 不证明「它有机会开口」——一条永远执行不到的断言，与一条从不失败的断言，危害相同。

/** 终端里的列宽：中文与全角标点占两格。对不齐的表读起来很痛。 */
const visualWidth = (text) =>
  [...text].reduce((width, glyph) => width + (glyph.codePointAt(0) > 0x2e7f ? 2 : 1), 0);
const pad = (text, size) => text + " ".repeat(Math.max(0, size - visualWidth(text)));
const width = Math.max(...Object.values(RULES).map(visualWidth)) + 2;

console.log(`\n每条不变量在这批语料上各执行了多少次（一次 = 一次有对象可判的求值）：`);
console.log(
  `  ${pad("不变量", width)}${corpora.map((each) => pad(each.name, 10)).join("")}${pad("合计", 10)}`,
);
for (const rule of Object.values(RULES)) {
  const counts = corpora.map((batch) => String(tally.get(batch.name).judged[rule] ?? 0));
  console.log(
    `  ${pad(rule, width)}${counts.map((count) => pad(count, 10)).join("")}${pad(String(judged[rule] ?? 0), 10)}`,
  );
}

// ---- 文本说法 ↔ 决策包里的绝对座位（票 40 的对拍，票 49 搬上批量语料） ----
//
// 十一条只读文本，因此它们**按设计**抓不住「碰的来源指错了人」——碰 / 大明杠 / 加杠
// 谁都鸣得了，一句指错人的话在纯文本里完全自洽（票 41 报告 §8-1）。
// 这一节拿包里的绝对座位与那句话对拍，参照系取**副露方**（票 40 的裁定）。

// 每一组副露在「视角・」那几格里恰好被数一次（一组只有一个观测者）。
const crossed = Object.entries(crosschecked)
  .filter(([key]) => key.startsWith("视角・"))
  .reduce((count, [, value]) => count + value, 0);

console.log(`\n「${NAKI_CROSSCHECK}」对拍了 ${crossed} 组副露：`);
for (const key of Object.keys(crosschecked).sort()) {
  console.log(`  ${pad(key, 26)}${crosschecked[key]}`);
}

console.log(
  `\n「${LABEL_CROSSCHECK}」对拍了 ${Object.values(labelled).reduce((sum, each) => sum + each, 0)} 条动作：`,
);
for (const kind of Object.keys(labelled).sort()) {
  console.log(`  ${pad(kind, 26)}${labelled[kind]}`);
}

if (violations.length > 0 || mismatches.length > 0 || mislabelled.length > 0) {
  const all = [...violations, ...mismatches, ...mislabelled];
  console.error(
    `\n在日麻规则下不成立、或与决策包对不上的话 ${all.length} 句（印前 ${parsed.show} 条）：\n`,
  );
  console.error(all.slice(0, parsed.show).map(formatViolation).join("\n"));
  process.exit(1);
}

console.log(
  `\n没有一句在日麻规则下不成立的话，没有一组副露的来源与决策包对不上，` +
    `也没有一条动作那一行与引擎那份 label 说岔了。`,
);

// **防空转一：执行次数为 0 的那几条，这一趟的「0 违反」不是证据**（票 49 的核心）。
const idle = Object.values(RULES).filter((rule) => (judged[rule] ?? 0) === 0);
if (idle.length > 0) {
  console.error(
    `这几条不变量这一趟一次都没执行：${idle.join("、")}` +
      `——它们的「0 违反」不是证据。换一批语料（两批种子都在这份脚本的 CORPORA 里）。`,
  );
  process.exit(1);
}

// **防空转二**：语料里压根没有副露的话，前四条不变量一条也没验到，而它照样是 0 违反。
const wanted = {
  naki: "副露",
  chi: "吃",
  pon: "碰",
  ankan: "暗杠",
  daiminkan: "大明杠",
  kakan: "加杠",
  dora: "宝牌指示牌",
  riichi: "立直宣言",
};
const missing = Object.entries(wanted).filter(([item]) => (coverage[item] ?? 0) === 0);
if (missing.length > 0) {
  console.error(
    `语料里一次都没出现：${missing.map(([, name]) => name).join("、")}` +
      `——相关的不变量这一趟等于没验。换一批种子（两批都是按形态挑的）。`,
  );
  process.exit(1);
}

// **防空转三**：对拍那四种来源与四个座位视角，**少一格就是那一格没被验过**。
const branches = [
  "吃・他家",
  "碰・他家",
  "大明杠・他家",
  "加杠・他家",
  "暗杠・无来源",
  "鸣了你打的那张・写「你」",
  "自家的副露・相对观测者",
  "他家的副露・「他的X」",
  ...SEATS.map((seat) => `视角・座位 ${seat}`),
];
const uncrossed = branches.filter((key) => (crosschecked[key] ?? 0) === 0);
if (uncrossed.length > 0) {
  console.error(
    `「${NAKI_CROSSCHECK}」这一趟没走到：${uncrossed.join("、")}` +
      `——那几格的换算这一趟没被对拍过。`,
  );
  process.exit(1);
}

// **防空转四**：动作那一行的对拍要真的走到几种形态，少一种就是那一种没被对拍过。
// 立直宣言 / 和了 / 九种九牌在这两批语料里不保证出现（`janpo decide --sequence`
// 只走打牌那一路），因此这里只钉**每一批语料都到得了**的那几种。
const LABEL_KINDS = ["dahai", "dahai*", "pon", "chi", "ankan", "daiminkan", "kakan", "none"];
const unlabelled = LABEL_KINDS.filter((kind) => (labelled[kind] ?? 0) === 0);
if (unlabelled.length > 0) {
  console.error(
    `「${LABEL_CROSSCHECK}」这一趟没走到：${unlabelled.join("、")}` +
      `——那几种动作的那一行这一趟没被对拍过。`,
  );
  process.exit(1);
}

// ---- 反向自证：每一条都当场按红一次 ----
//
// **一道从不失败的闸门等于没有闸门**（票 34 立的规矩，ci-web.sh 里那道 poison 就是它）。
// 上面扫完是绿的，绿得对不对要另有证据：把每一条不变量各自对应的那种错**注进真 prompt**，
// 那一条必须当场红，且报错点得出是哪条不变量、哪一手、哪句话。

console.log(`\n反向自证（把每一种错注进真 prompt，那一条必须当场红）：`);

// 新加一条不变量而不证明它咬得动，这里当场红。
const unproved = Object.values(RULES).filter(
  (rule) => !PROOFS.some((proof) => proof.rule === rule),
);
if (unproved.length > 0) {
  console.error(`这几条不变量没有反向自证：${unproved.join("、")}`);
  process.exit(1);
}

let proved = 0;
for (const proof of PROOFS) {
  const hit = corpus
    .map((each) => ({ where: each.where, broken: proof.mutate(each.prompt) }))
    .find((each) => each.broken !== null);

  if (hit === undefined) {
    console.error(`「${proof.rule}」这一趟没被证明咬得动：${proof.note}的局面语料里一个都没有。`);
    process.exit(1);
  }

  const caught = promptAudit(hit.broken, { where: hit.where }).violations;
  const mine = caught.filter((violation) => violation.rule === proof.rule);
  if (mine.length === 0) {
    console.error(
      `「${proof.rule}」没咬住：${proof.note}之后它该红，实际红的是 ` +
        `${caught.map((violation) => violation.rule).join("、") || "（一条都没红）"}。`,
    );
    process.exit(1);
  }

  const also = [...new Set(caught.map((v) => v.rule))].filter((rule) => rule !== proof.rule);
  proved += 1;
  console.log(`  ${formatViolation(mine[0])}`);
  if (also.length > 0) console.log(`    （同一处顺带红了：${also.join("、")}）`);
}

// 对拍那一道同样要自证。第二个变异是这道闸门存在的理由：**方位词仍然合法、只是指错了人**，
// 纯文本那十一条按设计看不见它。
for (const proof of CROSSCHECK_PROOFS) {
  const hit = corpus
    .map((each) => ({ each, broken: proof.mutate(each.prompt) }))
    .find((entry) => entry.broken !== null);

  if (hit === undefined) {
    console.error(`「${NAKI_CROSSCHECK}」没被证明咬得动：${proof.note}的局面语料里一个都没有。`);
    process.exit(1);
  }

  const caught = crosscheckNaki(hit.each.decision, hit.broken, hit.each.where).violations;
  if (caught.length === 0) {
    console.error(`「${NAKI_CROSSCHECK}」没咬住：${proof.note}之后它该红，实际一条都没红。`);
    process.exit(1);
  }

  const alsoText = promptAudit(hit.broken, { where: hit.each.where }).violations;
  proved += 1;
  console.log(`  ${formatViolation(caught[0])}`);
  console.log(
    `    （${proof.note}；同一处纯文本那十一条${
      alsoText.length === 0
        ? "**一条都没红**——只有这道对拍看得见"
        : `顺带红了：${[...new Set(alsoText.map((v) => v.rule))].join("、")}`
    }）`,
  );
}

for (const proof of LABEL_CROSSCHECK_PROOFS) {
  const hit = corpus
    .map((each) => ({ each, broken: proof.mutate(each.prompt) }))
    .find((entry) => entry.broken !== null);

  if (hit === undefined) {
    console.error(`「${LABEL_CROSSCHECK}」没被证明咬得动：${proof.note}的局面语料里一个都没有。`);
    process.exit(1);
  }

  const caught = crosscheckLabels(hit.each.decision, hit.broken, hit.each.where).violations;
  if (caught.length === 0) {
    console.error(`「${LABEL_CROSSCHECK}」没咬住：${proof.note}之后它该红，实际一条都没红。`);
    process.exit(1);
  }

  const alsoText = promptAudit(hit.broken, { where: hit.each.where }).violations;
  if (proof.textBlind && alsoText.length > 0) {
    console.error(
      `${proof.note}本该是纯文本那十二条看不见的那一种，实际它们红了：` +
        `${[...new Set(alsoText.map((v) => v.rule))].join("、")}——这个变异挑得不对。`,
    );
    process.exit(1);
  }
  proved += 1;
  console.log(`  ${formatViolation(caught[0])}`);
  console.log(
    `    （${proof.note}；同一处纯文本那十二条${
      alsoText.length === 0
        ? "**一条都没红**——只有这道对拍看得见"
        : `顺带红了：${[...new Set(alsoText.map((v) => v.rule))].join("、")}`
    }）`,
  );
}

console.log(
  `\n${Object.keys(RULES).length} 条不变量 + 2 道对拍、${proved} 个反向自证，每一条都当场证明咬得动。语义闸门通过。`,
);
