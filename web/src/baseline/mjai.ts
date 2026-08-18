/**
 * 强 AI 基线那一层**薄翻译**（票 92；ADR-0006 的「接缝不变」）。
 *
 * 两个方向各一件事：
 *
 * - **进去**：一份决策包里的**掩蔽事件流**（`DecisionPackage.history`，票 29a 的那条唯一
 *   掩蔽法则）→ 一串 mjai JSONL。掩蔽流的 `start_kyoku` 只带自家那一手（`tehai`，单数），
 *   而 mjai 服务端发给某一家的写法是四格 `tehais`、他家那三格填 `"?"`——**这一步就是那件事**。
 * - **出来**：wasm 印出来的那个 mjai 风格动作 → 这一包里的**一个 id**（ADR-0005：
 *   TS 侧永不构造 `Action`，回去的只有 id）。
 *
 * **这一层一条日麻规则都不判**：它只做记法归一与结构比对。合法与否由引擎给的那一包说了算
 * ——匹配不上就是「这一手交不出来」，由 F# 那侧兜底（`Fallback.action`），
 * **绝不在这里挑一条看着可行的**。
 */

/** mjai 里「这一格看不见」的写法（与 F# 侧 `MaskedEvent.hidden` 逐字相同）。 */
const HIDDEN = "?";

/**
 * 字牌的两种写法。**riichienv 读得懂 `1z`，但印出来的是 `E`**
 * （`riichienv_core::parser` 的 `mjai_to_tid` / `tid_to_mjai` 各走各的）——
 * 于是进去那一半不必翻，**出来那一半非翻不可**。
 *
 * 这一条是票 92 真踩到的：票 91 报告里那句「janpo 打出来的事件流零拒绝」量的是
 * **serde 解析**，而牌面记法是解析之后才由 `parse_mjai_tile` 读的，
 * 读不懂时它 `unwrap_or(0)` ——**不报错，静静地变成 1m**。
 */
const HONORS = ["E", "S", "W", "N", "P", "F", "C"];

/**
 * 一张牌归一到 janpo 的记法（`1z`…`7z`，红 5 是 `5mr`）。认不出来的原样返回
 * ——比对时它自然对不上，于是这一手走兜底，而不是被错当成别的牌。
 */
export function canonicalTile(pai: string): string {
  const honor = HONORS.indexOf(pai);
  return honor >= 0 ? `${honor + 1}z` : pai;
}

/**
 * 场风归一到 riichienv 读得懂的那四个字母。
 *
 * **这一条与牌面那一条不是同一件事**：牌面走 `mjai_to_tid`（它认得 `1z`），
 * 而 `start_kyoku` 的 `bakaze` 走的是 `state/event_handler.rs` 里一个手写的
 * `match bakaze.as_str() { "E" => …, _ => Wind::East }`——**认不出就当东场**。
 * janpo 的 `Kaze.toMjai` 印的是 `1z`…`4z`，不翻的话**南场会被它当成东场**，
 * 而场风是它观测张量里的一路通道（役牌与自风的判断全靠它）。
 *
 * 认不出来的原样返回：那时它退回东场，与不翻是同一个下场，但至少这一层没有假装翻过。
 */
export function canonicalBakaze(bakaze: string): string {
  const winds: Record<string, string> = { "1z": "E", "2z": "S", "3z": "W", "4z": "N" };
  return winds[bakaze] ?? bakaze;
}

/** 一串牌归一并排序：亮出来那几张的**顺序不是决策**，两侧的排法却不必相同。 */
function canonicalTiles(tiles: unknown): string {
  if (!Array.isArray(tiles)) return "";
  return tiles
    .map((tile) => canonicalTile(String(tile)))
    .sort()
    .join(",");
}

/** 一条 JSON 值当对象读；不是对象就是 `null`。 */
function asObject(value: unknown): Record<string, unknown> | null {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

/**
 * 掩蔽流里的一条 `start_kyoku` → mjai 服务端那种四格 `tehais`。
 *
 * **他家那三格填 `"?"`**：`mjai_to_tid("?")` 认不出来，riichienv 于是把它们当成同一张牌
 * ——而**那正是我们要的**：`native_bot` 的观测编码里根本没有「他家暗牌」这一路通道
 * （`obs.rs` 的 channel 表），因此他家手里躺着什么对它的输出没有影响。
 * 这件事不是读上游代码读出来的，是**量出来的**：`probe/akagi-wasm/mask-parity.mjs`
 * 拿 111 份真实牌谱 × 四个座位跑了 69,318 个决策点，遮与不遮**逐条同一手**。
 *
 * 张数按自家那一手算（配牌 13 张；亲那一手的第 14 张走 `tsumo`）。
 */
function startKyoku(event: Record<string, unknown>, seat: number): Record<string, unknown> {
  const tehai = Array.isArray(event.tehai) ? event.tehai : [];
  const scores = Array.isArray(event.scores) ? event.scores : [];
  const hidden = Array.from({ length: tehai.length }, () => HIDDEN);
  const tehais = scores.map((_, index) => (index === seat ? tehai : hidden));

  const { tehai: _dropped, ...rest } = event;
  const bakaze = typeof event.bakaze === "string" ? canonicalBakaze(event.bakaze) : event.bakaze;
  return { ...rest, bakaze, tehais };
}

/** 翻译不动时说的那句话（中文，给人看；F# 那侧原样搬进状态线）。 */
export class TranslationError extends Error {}

/**
 * 一份决策包的历史 → 喂给引擎的那串 mjai JSONL。
 *
 * **它必须以 `start_kyoku` 打头**：riichienv 的 `start_kyoku` 处理**本身就是一次整局重置**
 * （`state/event_handler.rs`：牌山、各家手牌、巡目、立直状态全部清掉），
 * 因此每问一手就从这一局的头喂一遍是**正确且够用**的——不必另开一个 `reset` 导出，
 * 也不必在 JS 侧维护「上次喂到哪儿」那份会漂的游标。
 */
export function historyLines(history: unknown, seat: number): string[] {
  if (!Array.isArray(history) || history.length === 0) {
    throw new TranslationError("这一包里没有历史，喂不出局面");
  }
  const first = asObject(history[0]);
  if (first === null || first.type !== "start_kyoku") {
    throw new TranslationError(`这一包的历史不是从 start_kyoku 开始的（${String(first?.type)}）`);
  }

  return history.map((raw) => {
    const event = asObject(raw);
    if (event === null) throw new TranslationError("这一包的历史里有一条不是对象");
    return JSON.stringify(event.type === "start_kyoku" ? startKyoku(event, seat) : event);
  });
}

/**
 * 一条 mjai 风格动作的**比对键**。两侧共用这一个函数，因此「怎么算同一手」只有一份判据。
 *
 * **`actor` 一律不进键**：wasm 那侧的 `BotAction` 根本不带座位（座位在 `probe_init` 时就钉死了），
 * 而这一包里的每一条动作恒是被问那一席的（`DecisionPackage.forSeat`）——比它等于比一个常量。
 *
 * 逐条为什么只取这几格（差异逐条写在报告 91 第 ④ 节）：
 *
 * - `dahai`：只取 `pai`。**不取 `tsumogiri`**——一包里同一张牌只有一条打法，
 *   而摸切与否是引擎算出来的事实，不是它的决策；两侧对这一格的看法不一致时
 *   该以引擎为准，而不是让这一手掉进兜底。
 * - `reach`：只取 type。wasm 那侧把「宣言 + 宣言牌」融成一条（`reach_dahai`），
 *   **而 janpo 会再问一次**——于是宣言牌那一手照旧由它自己决（喂进 `reach` 事件之后再问一遍，
 *   与上游 `predict_reach_discard` 做的是同一件事）。`reach_dahai` 因此**扔掉**。
 * - `pon` / `chi` / `daiminkan`：`pai` + 排序过的 `consumed`。**红 5 的两种亮法在这里分得开**
 *   （`5m 5m` 与 `5m 5mr` 是两条不同的动作，宝牌数不同）。
 * - `ankan`：排序过的 `consumed`。`kakan`：`pai`（一包里同一张牌只加得了一次杠）。
 * - `hora` / `ryukyoku` / `none`：只取 type。一包里各至多一条。
 *   **`hora` 不取 `pai`**：wasm 那侧根本不印它（报告 91 的第 ④ 节），
 *   而和的是哪张由引擎说了算。
 */
export function actionKey(action: Record<string, unknown>): string {
  const type = String(action.type);
  const pai = typeof action.pai === "string" ? canonicalTile(action.pai) : "";

  switch (type) {
    case "dahai":
      return `dahai:${pai}`;
    case "pon":
    case "chi":
    case "daiminkan":
      return `${type}:${pai}:${canonicalTiles(action.consumed)}`;
    case "ankan":
      return `ankan:${canonicalTiles(action.consumed)}`;
    case "kakan":
      return `kakan:${pai}`;
    case "reach":
    case "hora":
    case "ryukyoku":
    case "none":
      return type;
    default:
      return `${type}:${pai}`;
  }
}

/** 决策包里的一条编号动作（`ActionOption.encoder` 印出来的形状）。 */
export interface NumberedAction {
  id: number;
  action: Record<string, unknown>;
}

/** 决策包里那一列编号动作；形状不对的一条都不要（宁可兜底，不许猜）。 */
export function numberedActions(actions: unknown): NumberedAction[] {
  if (!Array.isArray(actions)) return [];
  return actions.flatMap((raw) => {
    const option = asObject(raw);
    const action = asObject(option?.action);
    if (option === null || action === null || typeof option.id !== "number") return [];
    return [{ id: option.id, action }];
  });
}

/**
 * wasm 印出来的那一手 → 这一包里的 id；**这一包里没有这一条就是 `null`**（走兜底）。
 *
 * **绝不退而求其次**：翻译层挑一条「看着可行的」等于在这一层重新长出一份日麻判断
 * （判据 11：要读规则才做得出的决定归引擎）。
 */
export function matchAction(
  botAction: Record<string, unknown>,
  options: NumberedAction[],
): number | null {
  const wanted = actionKey(botAction);
  const found = options.find((option) => actionKey(option.action) === wanted);
  return found === undefined ? null : found.id;
}
