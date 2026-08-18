namespace Janpo.Web

open Fable.Core
open Thoth.Json.Core
open Thoth.Json.JavaScript
open Janpo

/// 思考预算（spec 的「扩展思考」开关，pi-ai 的 `reasoning` 档位）。
/// `Off` 就是**不传这个参数**，不是传一个 "off"——pi-ai 的 `ThinkingLevel` 里没有它。
[<RequireQualifiedAccess>]
type Thinking =
    | Off
    | Minimal
    | Low
    | Medium
    | High

/// 一个 LLM 座位**这一手真发出去的那份配置**。**API key 只在这台浏览器里存在**：
/// 它从 localStorage 读出来、随请求交给 Agent 层、由浏览器直发 provider
/// （ADR-0003：Host 的浏览器是唯一的对局推进者），本平台没有后端可以收它。
///
/// **它是推导出来的，不是面板上填的那一份**（票 73）：前六格来自一份 `ModelProfile`
/// （档案答「怎么问这个模型」），后三格来自这一席的 `SeatBinding`（座位答「给多少信息 /
/// 什么风格 / 哪套措辞」）——合成在 `SeatBinding.config` 一处。档案的**名字**不在里面：
/// 那是本机的私人叫法，跨界这段 JSON 与牌谱都不认它。
type LlmSeat = {
    /// provider id（pi-ai 的那套：`deepseek` / `anthropic` / …）。
    Provider: string
    /// 模型 id。
    Model: string
    /// API key。**不进牌谱、不进 DecisionRecord、不落任何可分享物**。
    ApiKey: string
    /// 一次请求最多等多久（毫秒）。到点 abort，走与 provider 报错同一条兜底路径。
    TimeoutMs: int
    /// 思考预算。
    Thinking: Thinking
    /// 脚手架档位。它决定 Agent 层渲哪一份 prompt，以及兜底怎么代打（`Fallback`）。
    Tier: ScaffoldTier
    /// OpenAI 兼容端点的 baseUrl（票 30），形如 `http://localhost:11434/v1`。
    /// **只有 `Provider` 是 `ModelProfile.customProvider` 时用得上**：官方那八家走自己的地址，
    /// 这一项填了也一字不看。**这一层不判读它**（读不读得懂在 `endpoint.ts`）。
    BaseUrl: string
    /// 这个座位的**人格 / 风格文本**（票 31），进 prompt 的 system 消息最前面；空串就是不写。
    ///
    /// **它与 `Tier` 是两个维度**（主人 8/16 第五次裁决第 2 条）：同一个人格可以跑两档，
    /// 同一档可以跑两个人格——M2 的对照实验要它。**这一层不判读它**，原样递给 Agent 层。
    Persona: string
    /// prompt 模板的覆盖，一段 JSON（票 31）；空串就是默认模板。
    ///
    /// 抬头、规则说明与五张措辞表都换得动，**不必改代码重编**。形状与合法与否全在
    /// `web/src/agent/template.ts`（ADR-0005：prompt 在 TS 侧渲染），这里只是一个字符串。
    Template: string
}

/// Agent 层的一次回执。**跨界回来的东西只有它**：一个动作 id（或者一句「我交不出来」），
/// 外加审计要的那几个数。`Action` 永远不从这边构造（ADR-0005）。
///
/// **审计那几样全在这里**（26 号票）：prompt / 工具定义 / 模型原始输出 / thinking
/// 过界之后由 `TablePage.settle` 组装成一条 `DecisionRecord`。
/// **改字段要同时改两侧**：这里、`Agent.answerDecoder` 与 TS 侧的 `DecideResponse`。
type AgentAnswer = {
    /// 模型最终选定的动作 id；交不出来时是 None。
    ActionId: int option
    /// 模型给的一句话理由。
    Reason: string option
    /// 交不出来的原因（中文，给人看）。**它是「这一手要兜底」的信号**。
    Failure: string option
    /// 一共问了几次（首问 + 重试）。
    Attempts: int
    /// 端到端毫秒，含重试。
    LatencyMs: int
    /// 最后一次问出去的 prompt 的**尾部**（票 31）。前缀不在里面：它是事件流的派生物。
    PromptTail: string
    /// 这一手 system 消息的正文（人格 + 规则说明）。**一局内不变，可在局间更换**
    /// （CONTEXT.md 的 `Persona`；边界为什么取「局」见 `TablePage.Rendering`）：
    /// 牌谱按「座位 + 渲染版本」各存一份，换过就多一条
    /// （`src/Janpo.Engine/Paifu.fs` 的 `Preamble` 那段注释说的就是这件事）。
    Preamble: string
    /// 渲染版本号（`模板 id@模板哈希.渲染器摘要`，票 31 + 43）。**算出来的，不是手填的**；
    /// 它是决策记录与牌谱里那份 preamble 之间的键。
    RenderVersion: string
    /// 工具定义的**形状**（`choose_action` 的 schema，动作 id 的 enum 留空）。**F# 不解释它**：
    /// 那是 Agent 层那侧的形状，这边只负责原样搬进牌谱（ADR-0005：跨界只传字符串）。
    Tools: string
    /// 这一手真发出去的那份 enum：合法动作的 id 集。
    ActionIds: int list
    /// 模型原始输出的 JSON 全文（停止原因、内容块、token 用量），**不含 thinking**。
    Output: string
    /// 扩展思考全文；没开、provider 不给、或这一次根本没答上话时是 None。
    Thinking: string option
    /// 这一手的 token 账单（票 29b）。**一次都没问成时是 None**（没有账单可记）。
    ///
    /// **它是「前缀真的命中了没有」的唯一证据**：`CacheRead` 大头意味着这一手的
    /// 固定 preamble 与 append-only 历史被 provider 的前缀缓存吃到了。
    Usage: Usage option
}

/// 一次问话：那一手的决策包、座位配置与重试上限。
type AgentRequest = {
    Package: DecisionPackage
    Seat: LlmSeat
    RetryLimit: int
}

/// 思考预算的取值与两个出口。
[<RequireQualifiedAccess>]
module Thinking =

    /// 全部档位，由弱到强。档案编辑处的选项就是它。
    let all: Thinking list = [ Thinking.Off; Thinking.Minimal; Thinking.Low; Thinking.Medium; Thinking.High ]

    /// wire 上的名字。`off` 在 Agent 层被翻译成「不传 reasoning」。
    let toWire (thinking: Thinking) : string =
        match thinking with
        | Thinking.Off -> "off"
        | Thinking.Minimal -> "minimal"
        | Thinking.Low -> "low"
        | Thinking.Medium -> "medium"
        | Thinking.High -> "high"

    /// wire 名回到档位。认不出来的是 None——配置从 localStorage 读，什么都可能。
    let ofWire (wire: string) : Thinking option =
        all |> List.tryFind (fun thinking -> toWire thinking = wire)

    /// **渲染层的单向出口**（ADR-0001）。
    let toDisplay (thinking: Thinking) : string =
        match thinking with
        | Thinking.Off -> "不开"
        | Thinking.Minimal -> "最少"
        | Thinking.Low -> "低"
        | Thinking.Medium -> "中"
        | Thinking.High -> "高"

/// F#/TS 的那道边界（ADR-0005：**只有 F# 调 TS 这一个方向**）。
///
/// 出去的是一段 JSON（决策包 + 座位配置），回来的是一段 JSON（一个动作 id 或者一句原因）。
/// TS 侧拿不到 `GameState`，也构造不出 `Action`；它认识的只有 id。
[<RequireQualifiedAccess>]
module Agent =

    /// 重试上限（spec：解析失败重试上限 2 次，之后兜底）。**只有这一处**——
    /// 它随请求过界，Agent 层不自己定。
    let retryLimit = 2

    // ---- JSON ----

    /// 座位配置的 wire 形态。**key 在里面**：它只从这台浏览器走到 provider，
    /// 不进牌谱、不进任何可分享物。
    let private seatEncoder: Encoder<LlmSeat> =
        fun seat ->
            Encode.object [
                "provider", Encode.string seat.Provider
                "model", Encode.string seat.Model
                "base_url", Encode.string seat.BaseUrl
                "api_key", Encode.string seat.ApiKey
                "timeout_ms", Encode.int seat.TimeoutMs
                "thinking", Thinking.toWire seat.Thinking |> Encode.string
                "tier", ScaffoldTier.toWire seat.Tier |> Encode.string
                "persona", Encode.string seat.Persona
                "template", Encode.string seat.Template
            ]

    /// 一次问话的 wire 形态。`decision` 就是 `DecisionPackage.encoder` 的产物，
    /// 一个字段都不改写——Agent 层读的与 `janpo decide` 打印的是同一份东西。
    let requestEncoder: Encoder<AgentRequest> =
        fun request ->
            Encode.object [
                "decision", DecisionPackage.encoder request.Package
                "seat", seatEncoder request.Seat
                "retry_limit", Encode.int request.RetryLimit
            ]

    /// 回执的 wire 形态。**每个字段都可以缺**：Agent 层是另一个语言写的，
    /// 缺字段按「没有」处理而不是整条读不动——读不动的代价是这一手兜底，太贵。
    let answerDecoder: Decoder<AgentAnswer> =
        Decode.object (fun get -> {
            ActionId = get.Optional.Field "action_id" Decode.int
            Reason = get.Optional.Field "reason" Decode.string
            Failure = get.Optional.Field "failure" Decode.string
            Attempts = get.Optional.Field "attempts" Decode.int |> Option.defaultValue 0
            LatencyMs = get.Optional.Field "latency_ms" Decode.int |> Option.defaultValue 0
            PromptTail = get.Optional.Field "prompt_tail" Decode.string |> Option.defaultValue ""
            Preamble = get.Optional.Field "preamble" Decode.string |> Option.defaultValue ""
            RenderVersion = get.Optional.Field "render_version" Decode.string |> Option.defaultValue ""
            Tools = get.Optional.Field "tools" Decode.string |> Option.defaultValue ""
            ActionIds =
                get.Optional.Field "action_ids" (Decode.list Decode.int)
                |> Option.defaultValue []
            Output = get.Optional.Field "output" Decode.string |> Option.defaultValue ""
            Thinking = get.Optional.Field "thinking" Decode.string
            Usage = get.Optional.Field "usage" Usage.decoder
        })

    // ---- 构造 ----

    /// 「这一手交不出来」的回执。Agent 层根本没答上话时（promise 被 reject、
    /// 回执读不动）由这边造一条，**兜底路径因此只有一条**。
    ///
    /// 审计的那几项在这条路上是空的：**Agent 层都没答话，就没有真的 prompt 与输出可记**。
    /// 这一手仍然留一条记录（不能因为模型挂了就在牌谱里退形），只是它的内容就是那句原因。
    let refused (reason: string) : AgentAnswer = {
        ActionId = None
        Reason = None
        Failure = Some reason
        Attempts = 0
        LatencyMs = 0
        PromptTail = ""
        Preamble = ""
        RenderVersion = ""
        Tools = ""
        ActionIds = []
        Output = ""
        Thinking = None
        Usage = None
    }

    // ---- 边界 ----

    /// TS 侧的入口。`web/src/agent/decide.ts` 导出的那个函数，签名就是
    /// 「一段 JSON 进、一段 JSON 出的 Promise」——跨界只传字符串（ADR-0005）。
    [<Import("decide", "../../web/src/agent/decide.ts")>]
    let private decide (_request: string) : JS.Promise<string> = jsNative

    /// 回执文本 → 回执。读不动就是「交不出来」，不抛。
    let private read (text: string) : AgentAnswer =
        match Decode.fromString answerDecoder text with
        | Ok answer -> answer
        | Error message -> refused $"Agent 层的回执读不动：{message}"

    /// 问一次。**这个 Promise 不会 reject**：TS 侧把超时与 provider 报错都做成了值
    /// （票 18 的实测结论），万一还是抛了，`catch` 把它也变成一条「交不出来」。
    let ask (request: AgentRequest) : JS.Promise<AgentAnswer> =
        let failed (error: obj) = refused $"Agent 层抛了异常：{error}"

        (requestEncoder request |> Encode.toString 0 |> decide).``then``(read).catch (failed)
