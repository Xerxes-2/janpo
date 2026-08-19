/**
 * 强 AI 基线那道**跨界口**（票 92；ADR-0005：只有「F# 调 TS」一个方向，且只传字符串）。
 *
 * F# 侧的对应物是 `src/Janpo.Web/Baseline.fs`。出去的是一段 JSON（就是
 * `DecisionPackage.encoder` 的产物），回来的是一段 JSON（**一个动作 id** 或者一句原因）。
 * **这一层拿不到 `GameState`，也构造不出 `Action`**——与 `decide.ts`（LLM 席）
 * 和真人那一席逐字同一条规矩。
 *
 * 三个出口：
 *
 * - `load()`：去拉那几 MB 并起起来。**只有它被调到才会有那一次 `fetch`**
 *   （ADR-0006 边界 1：首页与不选那一席的对局一个字节都不拉）。
 * - `decide(request)`：喂这一局的事件流、跑一次前向、把它出的那一手换成这一包里的 id，
 *   **顺手把同一次前向里那几条候选与它们的概率一并带回去**（票 103）。
 * - `decideAll(request)`：**复盘那一屏用的**（票 93）：一次交来一整场的几十上百份决策包，
 *   逐份走 `decide`（**同一道门，没有第二条推理路**），回一张「第几手 → 哪一条 id」的表。
 *   它自己会在需要时先 `load()`——复盘那一屏点下那一枚之前，同样一个字节都不拉。
 *
 * **两个出口都不 reject**：拉不动、跑不起来、翻译不动都是**值**——
 * 那一席是可选依赖而不是单点（ADR-0006 边界 2），页面必须永远说得出一句人话。
 */

import { historyLines, matchAction, numberedActions, TranslationError } from "./mjai.ts";
import { type Baseline, decideNow, detail, feed, instantiate, lastPanic, seatAt } from "./wasm.ts";

/**
 * 起好的那一份，**整页只有一个**。四席都拨到它也共用它（换座位只重钉一次，见 `seatAt`）。
 *
 * **它是模块级的可变状态，而这正是那条硬约束的形状**：`null` = 一个字节都还没拉。
 */
let loaded: Baseline | null = null;

/** 拿一份信封 JSON 出去（同 `demo/paifu.ts` / `decide.ts` 的做法）。 */
const envelope = (fields: Record<string, unknown>) => JSON.stringify(fields);

/**
 * 拉那几 MB 并起起来；已经起好了就直接报字节数（**不重拉**：整站共用一份）。
 *
 * 回的是 `{"bytes":N}` 或者 `{"error":"强 AI 基线拉不动：…"}`。
 */
export async function load(): Promise<string> {
  if (loaded !== null) return envelope({ bytes: loaded.bytes });

  try {
    loaded = await instantiate();
    return envelope({ bytes: loaded.bytes });
  } catch (error) {
    return envelope({ error: `强 AI 基线拉不动：${detail(error)}` });
  }
}

/** 这一手交不出来（F# 那侧照原样搬进状态线，并由 `Fallback.action` 代打）。 */
const refused = (why: string, startedAt: number) =>
  envelope({ failure: why, latency_ms: Math.round(performance.now() - startedAt) });

/** 它那一次前向给出的一条候选：这一包里的哪一条，以及上游给它的那个数。 */
export interface Choice {
  action_id: number;
  p: number;
}

/**
 * 它那一次前向给出的候选分布 → **这一包里的几条 id 加各自的概率**（票 103）。
 *
 * **分布早就在跨界的 JSON 里了，是我们自己在这一层把它扔了**：`crate/src/lib.rs` 的
 * `decision_json` 一直在印 `{"candidates":[{"action":…,"p":…}, …]}`，而票 92 只取了头一条的
 * `action`。量出来的形状（`probe/akagi-wasm/candidates-shape.mjs`，111 份牌谱 × 四席 ×
 * 69,318 个决策点）：**最多 3 条**（上游 `SHOW_TOP_N`），按概率降序，每条落在 [0,1] 里，
 * **和不是 1**（中位 0.9895、14% 的决策点和 < 0.9）——它是 top-3 的切片，不是全分布。
 *
 * **认不出来的那一条不许瞎猜**（同 `matchAction`）：它落不进这一包就不进这张表，
 * 但**上游一共给了几条照实报**（`candidates_total`），否则「我们扔了什么」这件事又没人知道了。
 */
export function candidatesOf(
  decision: Record<string, unknown>,
  options: ReturnType<typeof numberedActions>,
) {
  const raw = Array.isArray(decision.candidates) ? decision.candidates : [];

  const named = raw.flatMap((each): Choice[] => {
    const candidate = each as { action?: Record<string, unknown>; p?: unknown };
    if (candidate.action === undefined || typeof candidate.p !== "number") return [];
    const id = matchAction(candidate.action, options);
    return id === null ? [] : [{ action_id: id, p: candidate.p }];
  });

  return { candidates: named, candidates_total: raw.length };
}

/**
 * 问它这一手打什么。
 *
 * 每问一手都**从这一局的头把事件流喂一遍**：riichienv 的 `start_kyoku` 处理本身就是一次
 * 整局重置（见 `mjai.ts` 的 `historyLines`），因此这样是正确的，也免掉了一份
 * 「上次喂到哪儿」的游标——而那份游标只要与真实历史错开一条就会静静地算错整局。
 * 代价是每手多喂几十行；实测（ADR-0006 的数）单手推理 0.37 ms、立直手 0.7 ms，
 * 而喂一整局也只是同一个量级——这一桌一手至少隔着一记 600 ms 的定时器。
 */
export async function decide(request: string): Promise<string> {
  const startedAt = performance.now();

  if (loaded === null) return refused("强 AI 基线还没起来（那几 MB 还没拉到）", startedAt);

  let seat: number;
  let history: unknown;
  let options: ReturnType<typeof numberedActions>;
  try {
    const parsed = JSON.parse(request) as { decision?: Record<string, unknown> };
    const decision = parsed.decision ?? {};
    seat = typeof decision.seat === "number" ? decision.seat : -1;
    history = decision.history;
    options = numberedActions(decision.actions);
  } catch (error) {
    return refused(`这一包读不动（${detail(error)}）`, startedAt);
  }

  if (seat < 0) return refused("这一包里没写是问哪一席", startedAt);
  if (options.length === 0) return refused("这一包里一条动作都没有", startedAt);

  let action: Record<string, unknown>;
  let distribution = { candidates: [] as Choice[], candidates_total: 0 };
  try {
    const lines = historyLines(history, seat);
    seatAt(loaded, seat);
    for (const line of lines) feed(loaded, line);

    const out = decideNow(loaded);
    if (out.ok !== true) return refused(`它答不上来（${String(out.error)}）`, startedAt);

    const decision = out.decision as Record<string, unknown> | null;
    if (decision == null || decision.action === undefined) {
      // 它认为此刻自己没有合法动作，而引擎正等着这一席——两边对局面的看法分了岔。
      return refused("它认为这一手没有它的事，而引擎正在等它", startedAt);
    }
    action = decision.action as Record<string, unknown>;
    // **同一次前向**：这几条候选与上面那一手出自 `probe_decide` 的同一段 JSON，
    // 因此带它们回去**不多跑一次推理**（票 103 量过：逐手 0.72 ms 不变）。
    distribution = candidatesOf(decision, options);
  } catch (error) {
    const panic = lastPanic(loaded);
    const why = error instanceof TranslationError ? error.message : detail(error);
    return refused(panic === "" ? why : `${why}（wasm 里的最后一句：${panic}）`, startedAt);
  }

  const id = matchAction(action, options);
  if (id === null) {
    // **绝不在这里挑一条看着可行的**（判据 11）：这一手交回去，由引擎那侧兜底。
    return refused(`它出的那一手不在这一包里：${JSON.stringify(action)}`, startedAt);
  }

  return envelope({
    action_id: id,
    latency_ms: Math.round(performance.now() - startedAt),
    ...distribution,
  });
}

/** 复盘那一屏交来的一手：手序 + 那一手当时喂给该席的那一份投影。 */
interface ReviewTurn {
  turn: number;
  decision: unknown;
}

/**
 * **复盘：一整场逐手问一遍**（票 93）。
 *
 * 进来的是 `{"turns":[{"turn":N,"decision":{…}}, …]}`，回去的是
 * `{"bytes":N,"load_ms":N,"ask_ms":N,"answers":[{"turn":N,"action_id":N,"latency_ms":N,`
 * `"candidates":[{"action_id":N,"p":…}, …],"candidates_total":N}, …]}` 或者一句 `{"error":"…"}`。
 *
 * **逐手走的就是 `decide`**：翻译、喂流、前向、比对 id 全在那一条路上，这里只是个循环
 * ——复盘看到的那一手与它真坐一席时出的那一手因此不可能分岔（**没有第二条推理路**）。
 *
 * **那几 MB 在这里才拉**（ADR-0006 边界 1）：复盘那一屏要有人真按下那一枚才走到这里，
 * 首页与对局中一个字节都不拉。拉不动就是一句 `{"error":…}`，页面照常出，只是没有那一行。
 *
 * **一手交不出来不打断整场**：那一条的信封里带 `failure`，F# 那侧把它当成「这一手整行不出现」。
 */
export async function decideAll(request: string): Promise<string> {
  let turns: ReviewTurn[];
  try {
    const parsed = JSON.parse(request) as { turns?: unknown };
    turns = Array.isArray(parsed.turns) ? (parsed.turns as ReviewTurn[]) : [];
  } catch (error) {
    return envelope({ error: `复盘交来的那一叠读不动（${detail(error)}）` });
  }

  const loadStartedAt = performance.now();
  if (loaded === null) {
    const said = JSON.parse(await load()) as { error?: string };
    if (said.error !== undefined) return envelope({ error: said.error });
  }
  const loadMs = Math.round(performance.now() - loadStartedAt);
  if (loaded === null) return envelope({ error: "强 AI 基线起不来" });

  const askStartedAt = performance.now();
  const answers: unknown[] = [];
  for (const each of turns) {
    const reply = JSON.parse(await decide(JSON.stringify({ decision: each.decision }))) as Record<
      string,
      unknown
    >;
    answers.push({ ...reply, turn: each.turn });
  }

  return envelope({
    bytes: loaded.bytes,
    load_ms: loadMs,
    ask_ms: Math.round(performance.now() - askStartedAt),
    answers,
  });
}
