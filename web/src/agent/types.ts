/**
 * F#/TS 边界上的 wire 契约（票 23）。
 *
 * **这一侧只读，不构造**：决策包由 F# 的 `DecisionPackage.encoder` 生成，回执由 F# 的
 * `Agent.answerDecoder` 读回去。改这里任何一个字段名，都要同时改
 * `src/Janpo.Web/Agent.fs` —— 那是这份契约的另一半。
 *
 * Agent 层**认识 id，不认识动作**（ADR-0005）：`ActionOption.action` 里那坨 mjai 结构化字段
 * 原样进 prompt 给模型看，TS 自己一个字段都不解释，更不会去构造一个动作。
 */

/** 河里的一张：牌本身，以及它是不是摸切（公开信息）。 */
export interface KawaEntry {
  pai: string;
  tsumogiri: boolean;
}

/** 一组副露。`ankan` 的 `target` 与 `pai` 都是 null。 */
export interface Naki {
  type: string;
  target: number | null;
  pai: string | null;
  consumed: string[];
}

/** 亮着的一家：自家。 */
export interface RevealedSeat {
  seat: number;
  jikaze: string;
  junme: number;
  score: number;
  tehai: string[];
  tsumo: string | null;
  kawa: KawaEntry[];
  naki: Naki[];
  riichi: string;
  ippatsu: boolean;
  furiten: { permanent: boolean; doujun: boolean };
}

/**
 * 遮蔽之后的一家：他家。
 *
 * **这里没有 `tehai`，也没有振听**：F# 侧的 `MaskedSeat` 类型里就没有那个字段，
 * 因此隐藏信息的保护在结构上成立，不靠这边的纪律。
 */
export interface MaskedSeat {
  seat: number;
  jikaze: string;
  junme: number;
  score: number;
  tehai_count: number;
  relative: number;
  kawa: KawaEntry[];
  naki: Naki[];
  riichi: string;
  ippatsu: boolean;
}

/** 某座位的合法观测。 */
export interface Observation {
  seat: number;
  bakaze: string;
  kyoku: number;
  honba: number;
  kyotaku: number;
  dora_markers: string[];
  wall_remaining: number;
  self: RevealedSeat;
  others: MaskedSeat[];
}

/** 一条编号动作：id、中文 label、mjai 动作消息。 */
export interface ActionOption {
  id: number;
  label: string;
  action: Record<string, unknown>;
}

/**
 * 决策包：某座位的合法观测 + 带 id 的动作列表 + 脚手架槽位。
 *
 * `scaffold` 在 Bare 档恒为空对象。**24 号票往里填** Shanten / Ukeire / 进退向，
 * 25 号票填 Danger；填了之后由 `prompt.ts` 的 `assisted` 档渲染出去。
 */
export interface DecisionPackage {
  seat: number;
  observation: Observation;
  actions: ActionOption[];
  scaffold: Record<string, unknown>;
}

/** 脚手架档位（CONTEXT.md 的 ScaffoldTier），wire 名与 F# 的 `ScaffoldTier.toWire` 一致。 */
export type ScaffoldTier = "bare" | "assisted" | "tool_search";

/** 思考预算。`off` 就是不传 `reasoning`。 */
export type ThinkingLevel = "off" | "minimal" | "low" | "medium" | "high";

/** 一个 LLM 座位的配置。**key 只从这台浏览器走到 provider**。 */
export interface SeatConfig {
  provider: string;
  model: string;
  api_key: string;
  timeout_ms: number;
  thinking: ThinkingLevel;
  tier: ScaffoldTier;
}

/** 一次问话。 */
export interface DecideRequest {
  decision: DecisionPackage;
  seat: SeatConfig;
  retry_limit: number;
}

/**
 * 一次回执。
 *
 * **`action_id` 与 `failure` 恰好有一个是非 null 的**：前者是模型自己选的动作，
 * 后者是「我交不出来」——那一手由 F# 侧的 `Fallback` 代打。兜底策略要读规则
 * （Bare 档摸切、Assisted 档不退向听的安全打），因此它在引擎里，不在这一层。
 *
 * **审计的那四项**（票 26）：prompt / 工具定义 / 原始输出 / thinking。它们过界之后由
 * `TablePage.settle` 组装成牌谱里的一条 `DecisionRecord`。改字段要同时改
 * `src/Janpo.Web/Agent.fs` 的 `AgentAnswer` 与 `answerDecoder`。
 */
export interface DecideResponse {
  action_id: number | null;
  reason: string | null;
  failure: string | null;
  attempts: number;
  latency_ms: number;
  /** 最后一次问出去的 prompt 全文（重试的那几次只多一句「上次为什么不行」）。 */
  prompt: string;
  /** 工具定义的 JSON 全文（`tools.ts` 那一份，与真发出去的同一个）。 */
  tools: string;
  /** 模型最后一次的原始输出，JSON 全文，**不含 thinking**。 */
  output: string;
  /** 扩展思考全文。**它是可省略的那一段**：URL 分享（M2）会把它抹掉。 */
  thinking: string | null;
}
