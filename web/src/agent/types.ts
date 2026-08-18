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

/**
 * 一条**掩蔽事件**（票 29a 的 `MaskedEvent`，票 29b 送过接缝）：那个座位亲眼看得见的一件事。
 *
 * **形状就是 mjai 的事件**（`type` + 那几个字段），公开的十四条逐字段与 `Event.encoder`
 * 出来的一模一样——掩蔽流没有第二种事件记法。掩掉的两条也仍是 mjai 的写法：
 * 他家摸的那张是 `"pai": "?"`，`start_kyoku` 只带自家那一手（`tehai`，不是四家的 `tehais`）。
 *
 * **这一侧不给它写十七个接口**：Agent 层认识 id 不认识动作（ADR-0005），历史只是拿来
 * 渲染成一行中文（`history.ts`）。字段按需读，读不出来就退回占位，不抛。
 */
export interface MaskedEvent {
  type: string;
  [field: string]: unknown;
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
  /**
   * 这个座位在这一局里亲眼看得见的那条历史（掩蔽事件流，票 29a）。
   *
   * **它与 `observation` 是同一件事的两种形态**：观测就是这条流的 fold。
   * prompt 把流放在可缓存的**前缀**里（append-only），把 fold 出来的场况放在
   * 每手付全价的**尾部**（票 29b）。重复是故意的：记账负担不是我们想测的能力。
   */
  history: MaskedEvent[];
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
  /**
   * OpenAI 兼容端点的 baseUrl（票 30）。**只有 `provider` 是 `custom` 时用得上**：
   * 官方那八家走自己的地址，这一项填了也一字不看。形如 `http://localhost:11434/v1`。
   */
  base_url: string;
  /**
   * 座位级的**人格 / 风格文本**（票 31），进 system 消息的最前面；空串就是不写。
   *
   * **它与脚手架档位是两个维度**：同一个人格可以跑两档，同一档可以跑两个人格。
   */
  persona: string;
  /**
   * prompt 模板的覆盖，一段 JSON（票 31）。空串就是默认模板。
   *
   * 给几项换几项（`template.ts` 的 `readTemplate`）：抬头、规则说明、五张措辞表都换得动，
   * **不必改代码重编**。读不动就退回默认并往 console 说一句。
   */
  template: string;
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
  /**
   * 最后一次问出去的 prompt 的**尾部**（票 31）：【现在】+【可选动作】+（脚手架）+（重试原因）。
   *
   * **前缀不在里面**：它是 (事件流 + 座位 + 模板) 的派生物，而事件流就在同一份牌谱里。
   */
  prompt_tail: string;
  /**
   * 这一手 system 消息的正文（人格 + 规则说明）。**一局内不变，可在局间更换**
   * （CONTEXT.md 的 `Persona`）：牌谱按「座位 + 渲染版本」各存一份，换过就多一条
   * （`src/Janpo.Engine/Paifu.fs` 的 `Preamble` 那段注释）。
   * 一局之内不得变这件事由页面执行（`src/Janpo.Web/TablePage.fs` 的 `Rendering`）。
   */
  preamble: string;
  /** 渲染版本号 `模板 id@内容哈希`（票 31）。**算出来的，不是手填的**。 */
  render_version: string;
  /** 工具定义的**形状**（`tools.ts` 那一份，动作 id 的 enum 留空）。牌谱里整场存一次。 */
  tools: string;
  /** 这一手真发出去的那份 enum：合法动作的 id 集。 */
  action_ids: number[];
  /** 模型最后一次的原始输出，JSON 全文，**不含 thinking**。 */
  output: string;
  /** 扩展思考全文。**它是可省略的那一段**：URL 分享（M2）会把它抹掉。 */
  thinking: string | null;
  /**
   * 这一手的 token 账单（票 29b）。**一次都没问成时是 null**（没有账单可记）。
   *
   * **它是「前缀真的命中了没有」的唯一证据**：`cache_read` 是命中前缀缓存的输入 token，
   * 数出来的就是缓存命中率。字段名与 F# 的 `Usage.encoder` 逐字段对应。
   */
  usage: TokenUsage | null;
}

/**
 * **这一手**的 token 账单（票 29b）。**四个数都是 provider 报的**：pi-ai 的 `usage` 已经
 * 把各家的字段名统一好了（DeepSeek 的 `prompt_cache_hit_tokens` 由它的
 * `openai-completions` 适配器读进 `cacheRead`），这一层不自己拼。
 *
 * **一手发了几次请求就加几次**（票 94）：重试那几轮、ToolSearch 档查 what-if 那几轮都算进来
 * ——否则那一档的账单在牌谱里与 Bare 档长得一模一样。零重试零查询时与从前逐字相同。
 * 审计那四项（prompt / 工具定义 / 原始输出 / thinking）照旧只记最后一轮（裁决 26-16）。
 */
export interface TokenUsage {
  /** 新付全价的输入 token（**不含**命中缓存与写缓存的那两部分）。 */
  input: number;
  /** 输出 token（含思考）。 */
  output: number;
  /** 命中前缀缓存、按折扣价计的输入 token。 */
  cache_read: number;
  /** 写进前缀缓存的输入 token。DeepSeek 不单独报（恒 0），Anthropic 报。 */
  cache_write: number;
}
