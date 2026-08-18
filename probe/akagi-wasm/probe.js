// 探路件的量测逻辑。浏览器（index.html）与 node（run-node.mjs）共用同一份，
// 免得两侧各量各的、数字对不上。
//
// 这个模块只做纯计算：wasm 字节与 fixture 文本都由调用方读进来。

const TEXT_ENCODER = new TextEncoder();
const TEXT_DECODER = new TextDecoder();

/** 实例化 wasm（它没有任何 import，所以不需要胶水层）。 */
export async function instantiate(wasmBytes) {
  const { instance } = await WebAssembly.instantiate(wasmBytes, {});
  return instance;
}

function bytesIn(instance, text) {
  const bytes = TEXT_ENCODER.encode(text);
  const ptr = instance.exports.probe_alloc(bytes.length);
  new Uint8Array(instance.exports.memory.buffer, ptr, bytes.length).set(bytes);
  return { ptr, len: bytes.length };
}

/** 喂一行 mjai JSON；返回 1 = 被接受。 */
export function feedLine(instance, line) {
  const { ptr, len } = bytesIn(instance, line);
  const rc = instance.exports.probe_feed(ptr, len);
  instance.exports.probe_free(ptr, len);
  return rc;
}

/** 让它出一手；返回解析好的 JSON 对象。 */
export function decide(instance) {
  const packed = instance.exports.probe_decide();
  // 高 32 位是指针、低 32 位是字节数（返回值是 i64，JS 侧拿到 BigInt）。
  const ptr = Number(packed >> 32n);
  const len = Number(packed & 0xffffffffn);
  const view = new Uint8Array(instance.exports.memory.buffer, ptr, len);
  const json = TEXT_DECODER.decode(view.slice());
  instance.exports.probe_free(ptr, len);
  return JSON.parse(json);
}

function stats(samples) {
  const sorted = [...samples].sort((a, b) => a - b);
  const at = (q) => sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * q))];
  return {
    n: sorted.length,
    min: sorted[0],
    median: at(0.5),
    p95: at(0.95),
    max: sorted[sorted.length - 1],
  };
}

function lines(text) {
  return text.split("\n").filter((l) => l.trim().length > 0);
}

/**
 * 三个场景：
 *   A 人工可核对的固定局面（听牌，摸进一张孤张北）——只有摸切北能保持听牌
 *   B 同一个决策点重复推理 `reps` 次，量单手延迟（决策点固定，方差来自运行时不是局面）
 *   C 真实牌谱的一整局（东1局），逐事件喂进去、每个我方决策点各推一次
 *
 * `now` 由调用方给（浏览器传 performance.now，node 传等价物），
 * 好让两边量的是同一件事。
 */
export async function runScenarios({ wasmBytes, tsumogiriText, kyokuText, reps = 200, now }) {
  const clock = now ?? (() => Number(process.hrtime.bigint()) / 1e6);
  const out = { wasmBytes: wasmBytes.byteLength };

  let t = clock();
  const instance = await instantiate(wasmBytes);
  out.instantiateMs = clock() - t;

  t = clock();
  const rc = instance.exports.probe_init(4, 0);
  out.initMs = clock() - t;
  if (rc !== 0) throw new Error(`probe_init 失败：rc=${rc}`);
  out.weightBytes4p = instance.exports.probe_weight_bytes(4);
  out.weightBytes3p = instance.exports.probe_weight_bytes(3);

  // ── 场景 A
  t = clock();
  for (const line of lines(tsumogiriText)) {
    if (feedLine(instance, line) !== 1) throw new Error(`引擎不认这行 mjai：${line}`);
  }
  out.feedMs = clock() - t;

  t = clock();
  const first = decide(instance);
  out.firstDecideMs = clock() - t;
  out.decision = first;

  // ── 场景 B
  const repSamples = [];
  for (let i = 0; i < reps; i += 1) {
    const t0 = clock();
    const d = decide(instance);
    repSamples.push(clock() - t0);
    if (JSON.stringify(d) !== JSON.stringify(first)) {
      throw new Error("同一局面上两次 decide() 结果不同");
    }
  }
  out.repeatedDecide = stats(repSamples);

  // ── 场景 C
  instance.exports.probe_init(4, 0);
  const kyokuSamples = [];
  const idleSamples = [];
  const kyokuActions = [];
  let fed = 0;
  const tKyoku = clock();
  for (const line of lines(kyokuText)) {
    if (feedLine(instance, line) !== 1) continue;
    fed += 1;
    const t0 = clock();
    const d = decide(instance);
    const dt = clock() - t0;
    if (d.ok && d.decision) {
      kyokuSamples.push(dt);
      kyokuActions.push(d.decision.action);
    } else {
      idleSamples.push(dt);
    }
  }
  out.kyokuMs = clock() - tKyoku;
  out.kyokuEventsFed = fed;
  out.kyokuDecide = stats(kyokuSamples);
  out.kyokuIdle = stats(idleSamples);
  out.kyokuActions = kyokuActions;

  out.wasmMemoryBytes = instance.exports.memory.buffer.byteLength;
  return out;
}
