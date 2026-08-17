// prompt 的**语义不变量**闸门（票 41）：扫一批**真实对局**，逐手渲染两档，
// 再逐条问「这句中文在日麻规则下可不可能」。
//
// 它与既有那十余道闸门补的不是同一个缺口：
//   - 黄金用例逐字段钉的是**决策包**（结构化数据，里面是绝对座位，永远没错）；
//   - `prefix.test.ts` 钉的是**字节单调**（这一手与上一手是不是同一串字节）；
//   - 这一道钉的是**那句话本身成不成立**——票 40 的「吃 2p（来自对家）」两道都绿地漏了过去。
//
// 判据在 `tests/agent/invariants.ts`（用例与这里共用同一份，两处说法因此一致）。
//
// 跑法（在 web/ 下）：
//   node scripts/verify-invariants.mjs                    默认那几颗种子（覆盖五种副露）
//   node scripts/verify-invariants.mjs --seeds 60         从 1 起扫 60 颗
//   node scripts/verify-invariants.mjs --from 500 --seeds 20
//   node scripts/verify-invariants.mjs --jobs 2           少占几个核（默认 4，跑批机器上还有别的 agent）
//   node scripts/verify-invariants.mjs --show 40          多印几条违反（默认 10）
//
// **零网络请求**：局面来自 `janpo decide <种子> --seat N --sequence`（本机跑引擎），
// 渲染与断言全在本进程里。

import { spawn, spawnSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { renderPrompt } from "../src/agent/prompt.ts";
import { PROOFS } from "../tests/agent/invariant-proofs.ts";
import {
  formatViolation,
  promptCoverage,
  promptViolations,
  RULES,
} from "../tests/agent/invariants.ts";

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, "../..");

/**
 * 默认扫的那几颗种子。**不是随手挑的**：`janpo kyoku` 扫了 1-300 之后按副露形态选出来的，
 * 五种副露与「明杠欠着一张宝牌指示牌」那种局面都得真出现过，否则闸门在语料上空转。
 *
 * 立直**一颗都没有**：随机选手 300 颗种子里一次都没立过直（票 42 正是为此立的）。
 * 「立直后全摸切」那一条因此只能靠反向自证咬得动，见 `PROOFS`。
 */
const DEFAULT_SEEDS = [7, 92, 100, 106, 114, 237];

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
const seeds =
  parsed.seeds === null
    ? DEFAULT_SEEDS
    : Array.from({ length: parsed.seeds }, (_, index) => parsed.from + index);

// 先编一次，之后每一趟都 `--no-build`：24 趟里每趟省 1.4 秒的增量检查。
const built = spawnSync("dotnet", ["build", "src/Janpo.Cli", "-c", "Release"], {
  cwd: repo,
  encoding: "utf8",
});
if (built.status !== 0) throw new Error(`janpo 编不出来：${built.stderr || built.stdout}`);

/** 一局里某座位被问到的每一手（`janpo decide --sequence`），一次一个子进程。 */
function decideSequence(seed, seat) {
  const args = [
    "run",
    "--project",
    "src/Janpo.Cli",
    "-c",
    "Release",
    "--no-build",
    "--",
    "decide",
    String(seed),
    "--seat",
    String(seat),
    "--sequence",
  ];

  return new Promise((done, fail) => {
    const child = spawn("dotnet", args, { cwd: repo });
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
        fail(new Error(`janpo decide ${seed} --seat ${seat} 跑不出来：${err || out}`));
      else done({ seed, seat, packages: JSON.parse(out) });
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

// ---- 扫 ----

const started = Date.now();
const jobs = seeds.flatMap((seed) => SEATS.map((seat) => () => decideSequence(seed, seat)));
const sequences = await pooled(jobs, parsed.jobs);

/** 语料：一份 prompt 加上它是哪一手。**两档都渲**（档位只动尾部，但尾部也是话）。 */
const corpus = [];
for (const { seed, seat, packages } of sequences) {
  for (const [hand, decision] of packages.entries()) {
    for (const tier of TIERS) {
      corpus.push({
        where: `种子 ${seed}・座位 ${seat}・第 ${hand} 手・${tier} 档`,
        prompt: renderPrompt(decision, tier, null),
      });
    }
  }
}

if (corpus.length === 0) throw new Error("一份 prompt 都没渲出来——语料是空的，这一道等于没跑");

const violations = corpus.flatMap((each) =>
  promptViolations(each.prompt, { where: each.where }).map(formatViolation),
);

const coverage = {};
for (const each of corpus) {
  for (const [item, count] of Object.entries(promptCoverage(each.prompt))) {
    coverage[item] = (coverage[item] ?? 0) + count;
  }
}

const hands = corpus.length / TIERS.length;
console.log(
  `扫了 ${seeds.length} 颗种子 × ${SEATS.length} 座位 = ${hands} 手，` +
    `每手渲 ${TIERS.length} 档 = ${corpus.length} 份 prompt（${((Date.now() - started) / 1000).toFixed(1)}s）`,
);
console.log(
  `语料里数得出来的：副露 ${coverage.naki} 组（吃 ${coverage.chi}、碰 ${coverage.pon}、` +
    `暗杠 ${coverage.ankan}、大明杠 ${coverage.daiminkan}、加杠 ${coverage.kakan}）、` +
    `宝牌指示牌 ${coverage.dora} 张、历史 ${coverage.historyLines} 行、可选动作 ${coverage.actions} 条、` +
    `立直宣言 ${coverage.riichi} 次`,
);

if (violations.length > 0) {
  console.error(`\n在日麻规则下不成立的话 ${violations.length} 句（印前 ${parsed.show} 条）：\n`);
  console.error(violations.slice(0, parsed.show).join("\n"));
  process.exit(1);
}

console.log(`没有一句在日麻规则下不成立的话。`);

// **防空转**：语料里压根没有副露的话，前四条不变量一条也没验到，而它照样是 0 违反。
const wanted = {
  naki: "副露",
  chi: "吃",
  pon: "碰",
  ankan: "暗杠",
  kakan: "加杠",
  dora: "宝牌指示牌",
};
const missing = Object.entries(wanted).filter(([item]) => (coverage[item] ?? 0) === 0);
if (missing.length > 0) {
  console.error(
    `语料里一次都没出现：${missing.map(([, name]) => name).join("、")}` +
      `——相关的不变量这一趟等于没验。换一批种子（默认那几颗是按形态挑的）。`,
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

  const caught = promptViolations(hit.broken, { where: hit.where });
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

console.log(
  `\n${Object.keys(RULES).length} 条不变量、${proved} 个反向自证，每一条都当场证明咬得动。语义闸门通过。`,
);
