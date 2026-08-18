// 逐行喂：报接受率、决策点数、线性内存增长（整场半庄的耐久）。
import { readFile } from "node:fs/promises";
import { performance } from "node:perf_hooks";
import { instantiate, feedLine, decide } from "./probe.js";
const wasm = await readFile("./dist/akagi_wasm_probe.wasm");
const inst = await instantiate(wasm.buffer.slice(wasm.byteOffset, wasm.byteOffset + wasm.byteLength));
const mem = () => inst.exports.memory.buffer.byteLength;
console.log("instantiate 后线性内存 =", mem());
inst.exports.probe_init(4, 0);
console.log("probe_init 后线性内存 =", mem());
for (const path of process.argv.slice(2)) {
  const text = await readFile(path, "utf8");
  const rejected = new Map();
  let n = 0, decisions = 0;
  const samples = [];
  const t0 = performance.now();
  for (const line of text.split("\n").filter((l) => l.trim())) {
    n += 1;
    if (feedLine(inst, line) !== 1) {
      const t = JSON.parse(line).type;
      rejected.set(t, (rejected.get(t) ?? 0) + 1);
    }
    const t1 = performance.now();
    const d = decide(inst);
    if (d.ok && d.decision) { samples.push(performance.now() - t1); decisions += 1; }
  }
  samples.sort((a, b) => a - b);
  const at = (q) => samples[Math.min(samples.length - 1, Math.floor(samples.length * q))];
  console.log(
    `${path}\n  行=${n} 被拒=${[...rejected].map(([k, v]) => `${k}×${v}`).join(",") || "无"}` +
      ` 决策点=${decisions} 全场墙钟=${(performance.now() - t0).toFixed(1)}ms` +
      ` 中位=${at(0.5).toFixed(3)}ms p95=${at(0.95).toFixed(3)}ms 线性内存=${mem()}`,
  );
}
