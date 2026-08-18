// node 侧的同一趟量测（浏览器那趟在 index.html + bench.mjs）。
// 用途：不开浏览器就能确认 wasm 通了、出的那一手对不对。
//   node run-node.mjs [reps]
import { readFile } from "node:fs/promises";
import { performance } from "node:perf_hooks";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

import { runScenarios } from "./probe.js";

const here = dirname(fileURLToPath(import.meta.url));
const reps = Number(process.argv[2] ?? 200);

const wasmBytes = await readFile(join(here, "dist/akagi_wasm_probe.wasm"));
const tsumogiriText = await readFile(join(here, "fixtures/tenpai-tsumogiri.jsonl"), "utf8");
const kyokuText = await readFile(join(here, "fixtures/kyoku-e1.jsonl"), "utf8");

const out = await runScenarios({
  wasmBytes: wasmBytes.buffer.slice(wasmBytes.byteOffset, wasmBytes.byteOffset + wasmBytes.byteLength),
  tsumogiriText,
  kyokuText,
  reps,
  now: () => performance.now(),
});

console.log(JSON.stringify(out, null, 2));
