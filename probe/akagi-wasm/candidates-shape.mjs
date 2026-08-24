// 票 103 的地基实验：**上游那份候选分布到底是什么形状？**
//
// `crate/src/lib.rs` 的 `decision_json` 一直在印 `{"candidates":[{"action":…,"p":…}, …]}`，
// 而票 92 的跨界口只取了 `action`（一个 id）。要把这份分布接过来之前，得先量清楚三件事
// ——**不许先写一条 `sum = 1` 的断言再去调数据，那是拿断言去规定事实**（票 103 票面）：
//
//   ① **几条**：是 top-k 还是全分布？k 是几？会不会更少？
//   ② **和是多少**：如果是 top-k，和就不必是 1；那它实际落在哪个区间？
//   ③ **顺序与形状**：是不是按概率降序？有没有负数 / NaN / 超出 [0,1] 的值？
//      runner-up 那几条里有没有「上游自己说了它是残缺的」那一种（`Reach` 的 `pai` 是空串）？
//
// 读上游源码只能得到一个印象（`SHOW_TOP_N = 3`、注释说 "softmax over the legal set"），
// 而印象与事实是两件事（判据 19 的同一族）：这个脚本在**真语料**上把三件事各量一遍。
//
// 跑法（要先造出那份 wasm：`../../scripts/build-baseline-wasm.sh` 或 README 里那两行 cargo）：
//   node candidates-shape.mjs ../../tests/fixtures/paifu/mjai/*.mjson
//   node candidates-shape.mjs --seats 0,1,2,3 --wasm dist/akagi_wasm_probe.wasm ../../tests/fixtures/paifu/mjai/*.mjson
//
// 量出来的数写在 `.scratch/llm-riichi-arena/run/reports/103-baseline-confidence.md`。

import { readFileSync } from "node:fs";
import { PUBLIC_ASSET } from "../../web/scripts/baseline-asset.mjs";
import { decideText, feedLine, instantiate } from "./probe.js";

/**
 * 默认量的就是**站点上真发出去的那一份**（`scripts/build-baseline-wasm.sh` 造的那份）。
 * 路径不在这里再写一遍：真源是 `web/scripts/baseline-asset.mjs`（票 106）。
 */
const DEFAULT_WASM = PUBLIC_ASSET;

/** `p` 在 wasm 印出来那段原文里的样子：`"p":0.5961328`。**逐位取的是这一串字符**。 */
const P_LITERAL = /"p":(-?[0-9][0-9.eE+-]*)/g;

function argOf(args, name, fallback) {
  const at = args.indexOf(name);
  return at === -1 ? fallback : args[at + 1];
}

/** 一段 `decision_json` 原文里那几条 `p` 的**字面**（不解析，逐字取）。 */
function literals(text) {
  return [...text.matchAll(P_LITERAL)].map((match) => match[1]);
}

function summarise(values) {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const at = (q) => sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * q))];
  return { n: sorted.length, min: sorted[0], p05: at(0.05), median: at(0.5), p95: at(0.95), max: sorted[sorted.length - 1] };
}

const show = (stats) =>
  stats === null
    ? "（一条都没有）"
    : `n=${stats.n} min=${stats.min.toFixed(6)} p05=${stats.p05.toFixed(6)} 中位=${stats.median.toFixed(6)} p95=${stats.p95.toFixed(6)} max=${stats.max.toFixed(6)}`;

const args = process.argv.slice(2);
const seats = String(argOf(args, "--seats", "0"))
  .split(",")
  .map((each) => Number.parseInt(each, 10));
const wasmPath = argOf(args, "--wasm", DEFAULT_WASM);
const paths = args.filter((each) => each.endsWith(".mjson") || each.endsWith(".jsonl"));

const bytes = readFileSync(wasmPath);
const instance = await instantiate(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength));

const counts = new Map();
const sums = [];
const tops = [];
const seconds = [];
const smallest = [];
let decisions = 0;
let descendingViolations = 0;
let outOfRange = 0;
let sumOverOne = 0;
let sumIsOneExactly = 0;
let emptyReachRunnerUp = 0;
let topIsCandidateZero = 0;
let topMismatch = 0;
let forced = 0;
let forcedNotSingleton = 0;
let literalMismatch = 0;

for (const path of paths) {
  const lines = readFileSync(path, "utf8")
    .split("\n")
    .filter((line) => line.trim().length > 0);

  for (const seat of seats) {
    instance.exports.probe_init(4, seat);

    for (const line of lines) {
      if (feedLine(instance, line) !== 1) continue;

      const text = decideText(instance);
      const parsed = JSON.parse(text);
      if (parsed.ok !== true || parsed.decision === null || parsed.decision === undefined) continue;

      const decision = parsed.decision;
      const candidates = decision.candidates ?? [];
      decisions += 1;
      counts.set(candidates.length, (counts.get(candidates.length) ?? 0) + 1);

      // ③ 顺序与形状。
      const ps = candidates.map((each) => each.p);
      for (const p of ps) {
        if (!(p >= 0 && p <= 1)) outOfRange += 1;
      }
      for (let i = 1; i < ps.length; i += 1) {
        if (ps[i] > ps[i - 1]) descendingViolations += 1;
      }

      // ② 和。**先量，再决定断言什么**。
      const sum = ps.reduce((total, p) => total + p, 0);
      sums.push(sum);
      if (sum > 1) sumOverOne += 1;
      if (sum === 1) sumIsOneExactly += 1;
      if (ps.length > 0) tops.push(ps[0]);
      if (ps.length > 1) seconds.push(ps[1]);
      if (ps.length > 0) smallest.push(ps[ps.length - 1]);

      // `decision.action` 与 `candidates[0].action` 是不是同一条（上游注释这么说的）。
      if (candidates.length > 0) {
        if (JSON.stringify(candidates[0].action) === JSON.stringify(decision.action)) {
          topIsCandidateZero += 1;
        } else {
          topMismatch += 1;
        }
      }

      // runner-up 的 `reach` 上游明说不带宣言牌（`pai` 是空串）：接过来时不能把它当一手真动作。
      for (const [index, each] of candidates.entries()) {
        if (index > 0 && each.action?.type === "reach" && each.action.reach_dahai === "") {
          emptyReachRunnerUp += 1;
        }
      }

      if (decision.forced === true) {
        forced += 1;
        if (candidates.length !== 1) forcedNotSingleton += 1;
      }

      // 逐位：原文那几串字符 → JS 双精度 → 再印回去，必须是同一串字符
      // （页面那一侧就是这样把它带到 DOM 上的）。
      const raw = literals(text);
      const expected = ps.map((p) => String(p));
      if (raw.length !== expected.length || raw.some((each, index) => each !== expected[index])) {
        literalMismatch += 1;
        if (literalMismatch <= 5) console.log(`  逐位对不上：原文 ${raw.join(",")} vs 回印 ${expected.join(",")}`);
      }
    }
  }
}

console.log(`牌谱 ${paths.length} 份、座位 ${seats.join("/")}、决策点 ${decisions} 个`);
console.log(
  `① 几条：${[...counts.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([k, v]) => `${k} 条 ×${v}`)
    .join("、")}`,
);
console.log(`② 和：${show(summarise(sums))}`);
console.log(
  `   和 > 1 的：${sumOverOne} 个（最多超出 ${Math.max(0, ...sums.map((sum) => sum - 1)).toExponential(3)}）；` +
    `和恰好 == 1 的：${sumIsOneExactly} 个；和 < 0.9 的：${sums.filter((sum) => sum < 0.9).length} 个`,
);
console.log(`③ 降序被破坏：${descendingViolations} 次；落在 [0,1] 之外：${outOfRange} 个`);
console.log(`   第 1 条：${show(summarise(tops))}`);
console.log(`   第 2 条：${show(summarise(seconds))}`);
console.log(`   最末一条：${show(summarise(smallest))}`);
console.log(`   runner-up 是「不带宣言牌的立直」：${emptyReachRunnerUp} 条`);
console.log(`   decision.action == candidates[0].action：${topIsCandidateZero} 个（对不上 ${topMismatch} 个）`);
console.log(`   forced：${forced} 个（其中候选不止一条的 ${forcedNotSingleton} 个）`);
console.log(`   p 的字面往返（原文 → 双精度 → 印回）对不上的决策点：${literalMismatch} 个`);
