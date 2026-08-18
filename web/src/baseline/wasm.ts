/**
 * 强 AI 基线那几 MB 的**取用与 ABI**（票 92；ADR-0006）。
 *
 * 产物是**一个自足的 `.wasm`**：没有任何 import，因此
 * `WebAssembly.instantiate(bytes, {})` 就能起——不进 JS 胶水包、不进构建链
 * （为什么不用 wasm-bindgen 写在 `probe/akagi-wasm/README.md` 里）。
 * 代价是自己管线性内存的进出口，那就是下面那几个 `probe_*` 调用。
 *
 * **这一层不认识牌，也不认识决策包**：进来出去只有 mjai JSONL 文本与一段 JSON。
 * 记法归一与 id 匹配在 `mjai.ts`，跨界那道口在 `baseline.ts`。
 *
 * **它绝不在模块加载时做任何事**（ADR-0006 边界 1）：这个文件被 import 进 bundle，
 * 但**只有 `load()` 被调到才发出那一次 `fetch`**——首页与不选那一席的对局因此
 * 一个字节都不拉，而闸门量的正是那个请求计数。
 */

/**
 * 资产在站点里的相对位置。`web/public/` 下的东西 Vite 原样拷进 `dist/`，
 * 因此它就是站点根下的一个文件。
 *
 * **6 MB 的二进制不入版本控制**（ADR-0006 边界 6）：`web/public/baseline/` 里只有一份
 * 说明，产物由 `probe/akagi-wasm/` 重建后放进去（怎么放写在那份说明里）。
 * 站点上没有它时这里就是一个 404，而 404 是有下场的（页面明说原因、那一席退回自带 bot）。
 */
const ASSET_FILE = "baseline/janpo-baseline.wasm";

/**
 * 拉资产最多等多久。**任何一个 `await` 都要有下界**（票 91 的第 4 条坑：
 * 一个没有超时的 `await` 把上一轮的 agent 干掉了 45 分钟）。
 *
 * 60 秒是照 ADR-0006 的数取的：4.8 MB gzip 在 5 Mbit/s 的移动网上约 7.7 秒，
 * 取一个宽到「慢网也拉得完」、又窄到「断网时人不必干等」的数。
 */
const FETCH_TIMEOUT_MS = 60000;

/** 四麻。三麻那份权重这一票用不上（janpo 现在只有四麻桌）。 */
const NUM_PLAYERS = 4;

/**
 * **按 `document.baseURI` 解析而不是写死斜杠开头**（同 `web/src/demo/paifu.ts`）：
 * 站点部署在子路径下（GitHub Pages 是 `/janpo/`，由 `JANPO_BASE` 注入），
 * 写死 `/baseline/…` 在那里会 404。
 */
export function assetUrl(): string {
  return new URL(ASSET_FILE, document.baseURI).toString();
}

/** 手写 C ABI 的那几个导出（`probe/akagi-wasm/crate/src/lib.rs`）。 */
interface Exports {
  memory: WebAssembly.Memory;
  probe_install_panic_hook: () => void;
  probe_last_panic: () => bigint;
  probe_alloc: (len: number) => number;
  probe_free: (ptr: number, len: number) => void;
  probe_init: (numPlayers: number, seat: number) => number;
  probe_feed: (ptr: number, len: number) => number;
  probe_decide: () => bigint;
}

const encoder = new TextEncoder();
const decoder = new TextDecoder();

/** 起好的那一份：实例、字节数，以及它此刻被 `probe_init` 钉在哪一席。 */
export interface Baseline {
  exports: Exports;
  bytes: number;
  seat: number;
}

/** 一段文本写进线性内存；调用方负责 `probe_free`。 */
function bytesIn(exports: Exports, text: string): { ptr: number; len: number } {
  const bytes = encoder.encode(text);
  const ptr = exports.probe_alloc(bytes.length);
  new Uint8Array(exports.memory.buffer, ptr, bytes.length).set(bytes);
  return { ptr, len: bytes.length };
}

/** 拆那个打包的返回值：高 32 位是指针、低 32 位是字节数（i64 在 JS 侧是 BigInt）。 */
function textOut(exports: Exports, packed: bigint): string {
  const ptr = Number(packed >> 32n);
  const len = Number(packed & 0xffffffffn);
  if (len === 0) return "";
  const view = new Uint8Array(exports.memory.buffer, ptr, len);
  const text = decoder.decode(view.slice());
  exports.probe_free(ptr, len);
  return text;
}

/** 出错时给人看的那一句里，把浏览器的原话截短（同 `demo/paifu.ts`）。 */
export function detail(error: unknown): string {
  const message = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  const flat = message.trim().replace(/\s+/g, " ");
  return flat.length > 80 ? `${flat.slice(0, 80)}…` : flat;
}

/**
 * 拉那份产物并起起来。**只在被调到的那一刻才发请求**（ADR-0006 边界 1）。
 *
 * 三种失法各抛一句中文：拉不到（404 / 离线 / 超时）、编不动 / 起不来、初始化不了。
 * 调用方（`baseline.ts`）把它们变成值——**页面上永远说得出是哪一种**。
 */
export async function instantiate(): Promise<Baseline> {
  const url = assetUrl();

  let response: Response;
  try {
    response = await fetch(url, { signal: AbortSignal.timeout(FETCH_TIMEOUT_MS) });
  } catch (error) {
    throw new Error(`请求 ${url} 时出错（${detail(error)}）`);
  }
  if (!response.ok) throw new Error(`${url} 回了 HTTP ${response.status}`);

  let buffer: ArrayBuffer;
  try {
    buffer = await response.arrayBuffer();
  } catch (error) {
    throw new Error(`${url} 的正文读不下来（${detail(error)}）`);
  }

  let instance: WebAssembly.Instance;
  try {
    ({ instance } = await WebAssembly.instantiate(buffer, {}));
  } catch (error) {
    throw new Error(`${url} 编译或实例化不了（${detail(error)}）`);
  }

  const exports = instance.exports as unknown as Exports;
  // panic 在 wasm32-unknown-unknown 上无处可打印（没有 WASI、没有 stderr）：
  // 装上这个 hook，trap 之后才取得到那句话，否则只有一句 `unreachable`。
  exports.probe_install_panic_hook();

  const seat = 0;
  if (exports.probe_init(NUM_PLAYERS, seat) !== 0) {
    throw new Error(`${url} 起来了，但那份权重装不上`);
  }

  return { exports, bytes: buffer.byteLength, seat };
}

/**
 * 把它钉到某一席上。**换座位才重来一次**：`probe_init` 会重新解一遍 safetensors
 * （约 2 ms）并重建模型，而 wasm 的线性内存**永不归还给宿主**——每重建一次就永久多占
 * 约 2.75 MiB（票 91 量到的）。四席都拨到它也只重建至多四次。
 */
export function seatAt(baseline: Baseline, seat: number): void {
  if (baseline.seat === seat) return;
  if (baseline.exports.probe_init(NUM_PLAYERS, seat) !== 0) {
    throw new Error(`换到座位 ${seat} 时那份权重装不上`);
  }
  baseline.seat = seat;
}

/**
 * 喂一行 mjai。**`false` 当硬错误抛**（票 91 报告第 ④ 节点名的那一条）：
 * riichienv 把认不出的 `type` 映射成 `Other` 再回 `false`，
 * 一路 `continue` 下去的话它就静静地少看了一条事件——而少看一条鸣牌足以让它算错整局。
 */
export function feed(baseline: Baseline, line: string): void {
  const { exports } = baseline;
  const { ptr, len } = bytesIn(exports, line);
  const code = exports.probe_feed(ptr, len);
  exports.probe_free(ptr, len);
  if (code !== 1) throw new Error(`它不认这行 mjai（rc=${code}）：${line}`);
}

/** 让它出一手；返回那段 JSON 解出来的对象（形状见 `crate/src/lib.rs` 的 `decision_json`）。 */
export function decideNow(baseline: Baseline): Record<string, unknown> {
  const text = textOut(baseline.exports, baseline.exports.probe_decide());
  return JSON.parse(text) as Record<string, unknown>;
}

/** trap 之后取那句 panic（没有就是空串）。 */
export function lastPanic(baseline: Baseline): string {
  try {
    return textOut(baseline.exports, baseline.exports.probe_last_panic());
  } catch {
    return "";
  }
}
