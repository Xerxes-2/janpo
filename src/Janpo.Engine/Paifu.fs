namespace Janpo

open Thoth.Json.Core

/// 一次决策的完整审计数据（CONTEXT.md 的 DecisionRecord）：输入（prompt、工具定义）、
/// 输出（含 thinking）、延迟、重试次数，以及这一手最后落定的是哪个动作、是不是兜底代打的。
///
/// **它是数据，不是 UI**：思考气泡（M2）读它，但它自己不知道任何渲染的事。
///
/// **随机选手的手不产生记录**（没有可审计的推理），因此这个列表比手数短、`Turn` 不连续；
/// 但编号本身**不断裂**——`Turn` 是这一场里落定的第几手，随机座位的手照样占号。
type DecisionRecord =
    {
        /// 这一场里的第几手，0 起（CONTEXT.md 的 Turn）。**它是记录与事件流之间的锚**：
        /// 随机座位的手不产生记录，但它们照样把这个号往前推。
        Turn: int
        /// 做这次决策的座位。
        Seat: Seat
        /// 问出去的 prompt 全文。**重试时记最后一次**（重试的 prompt 只多一句「上次为什么不行」，
        /// 而最后那次才是产出这条回答的那次）；问了几次看 `Attempts`。
        Prompt: string
        /// 工具定义的 JSON 全文（`choose_action` 的 schema，含这一手的合法 id 列表）。
        ///
        /// **F# 不解释这段字符串**：它是 Agent 层那侧的形状（ADR-0005：跨界只传字符串），
        /// 牌谱只负责原样保存下来给人和分析脚本看。
        Tools: string
        /// 模型的原始输出，JSON 全文（停止原因、内容块、token 用量）。同样**不被 F# 解释**。
        ///
        /// **thinking 不在里面**：它单独一个字段，因为分享路径要省得掉它（见 `Thinking`）。
        Output: string
        /// 模型给的一句话理由（`choose_action` 的 `reason` 实参）。交不出来时是 None。
        Reason: string option
        /// 扩展思考的全文。**这是可省略的那一段**：JSON 导出全量带着，URL 分享（M2）
        /// 把它抹掉（`Paifu.stripThinking`）——体积大头就是它。
        /// 两条路径共用同一个解码器，因此它在 wire 上可缺省，而不是另立一个牌谱类型。
        Thinking: string option
        /// 一共问了几次（首问 + 重试）。
        Attempts: int
        /// 端到端毫秒，含重试。
        LatencyMs: int
        /// 这一手最终落进引擎的那个动作在**这一手的决策包**里的 id（兜底代打时就是代打的那条）。
        ///
        /// **牌谱里不存动作本身**：`Action` 是意图，意图不上牌谱（ADR-0002 / `Action.encoder`
        /// 那条「单向出口」）。id 在包内稳定，而包由「事件流 fold 到第 `Turn` 手的局面」
        /// 重新算得出来（`DecisionPackage.forSeat`）——状态是 fold 出来的，不是存下来的。
        ///
        /// **实际上恒是 Some**（连兜底代打挑的那条也取自这一包）；它是 option 只因为
        /// 「id ↔ 动作」的换算在引擎里恒是 option（`tryAction` / `tryId`），
        /// 记录不拿一个占位数字去假装它不是。
        Applied: int option
        /// 兜底代打的原因（中文，给人看）；模型自己决出来的那一手是 None。
        /// **「是否兜底」就是它是不是 None**。
        Fallback: string option
    }

/// 牌谱（CONTEXT.md 的 Paifu）：一场对局的完整记录，也是本项目**唯一的可分享物**（ADR-0002）。
///
/// 四样东西：格式版本号、规则集、mjai 事件流、逐手的决策记录。
/// **没有局面快照**——局面是对事件流做 fold 得出来的（`Replay`），不是存下来的。
///
/// URL 分享（M2）与 JSON 导入导出走的是同一个类型、同一个编码器、同一个解码器：
/// 前者只是先做一次 `Paifu.stripThinking`。
type Paifu =
    {
        /// 格式版本号。加**可缺省**的字段不涨版本（旧文件照样读得动）；
        /// 改字段含义、删字段或改结构才涨，涨了之后解码器要同时认得旧版本。
        Version: int
        /// 这一场的规则集。**逐字段带着**：回放要照这一场真正的规则重算符与点数。
        Ruleset: Ruleset
        /// mjai 事件流，含开头的 `start_game`。回放对它做 fold（`Replay.game`）。
        Events: Event list
        /// 决策记录，按手序。随机选手的手不在里面。
        Decisions: DecisionRecord list
    }

/// 牌谱的构造、变换与编解码。
[<RequireQualifiedAccess>]
module Paifu =

    // ---- 版本 ----

    /// 当前的格式版本号。
    let version = 1

    /// 这个版本的引擎读得动的格式版本。加一个版本就在这里加一项，并让解码器认得它。
    let supported: int list = [ 1 ]

    // ---- 构造 ----

    /// 摆一份牌谱。版本号由这里给，调用方不必知道它是几。
    let create (ruleset: Ruleset) (events: Event list) (decisions: DecisionRecord list) : Paifu =
        {
            Version = version
            Ruleset = ruleset
            Events = events
            Decisions = decisions
        }

    // ---- 变换 ----

    /// 抹掉全部 thinking。**URL 分享（M2）走这一条**：体积大头是思考全文，
    /// 省掉它就够短了，而牌谱仍然是同一个类型、同一个编码器、同一个解码器（ADR-0002）。
    let stripThinking (paifu: Paifu) : Paifu =
        { paifu with
            Decisions = paifu.Decisions |> List.map (fun record -> { record with Thinking = None })
        }

    /// 某一手的决策记录；那一手没有记录（随机选手，或者压根没走到）则为 None。
    let decisionAt (turn: int) (paifu: Paifu) : DecisionRecord option =
        paifu.Decisions |> List.tryFind (fun record -> record.Turn = turn)

    // ---- JSON ----

    /// `None` 的字段**整个不写**（而不是写 `null`）：分享路径要的就是短，
    /// 而解码那侧本来就把「缺了」与「是 null」当同一件事。
    let private optional (encode: Encoder<'a>) (name: string) (value: 'a option) : (string * IEncodable) list =
        value |> Option.map (fun value -> name, encode value) |> Option.toList

    let recordEncoder: Encoder<DecisionRecord> =
        fun record ->
            Encode.object (
                [
                    "turn", Encode.int record.Turn
                    "seat", Seat.encoder record.Seat
                    "prompt", Encode.string record.Prompt
                    "tools", Encode.string record.Tools
                    "output", Encode.string record.Output
                ]
                @ optional Encode.string "reason" record.Reason
                @ optional Encode.string "thinking" record.Thinking
                @ [
                    "attempts", Encode.int record.Attempts
                    "latency_ms", Encode.int record.LatencyMs
                ]
                @ optional Encode.int "applied" record.Applied
                @ optional Encode.string "fallback" record.Fallback
            )

    /// **可缺省的四个字段**：`reason` / `thinking` / `applied` / `fallback`。
    /// thinking 缺省是分享路径的硬需求（ADR-0002），其余三个缺省分别是
    /// 「模型没说」「换不回 id」与「这一手不是兜底」。
    let recordDecoder: Decoder<DecisionRecord> =
        Decode.object (fun get ->
            {
                Turn = get.Required.Field "turn" Decode.int
                Seat = get.Required.Field "seat" Seat.decoder
                Prompt = get.Required.Field "prompt" Decode.string
                Tools = get.Required.Field "tools" Decode.string
                Output = get.Required.Field "output" Decode.string
                Reason = get.Optional.Field "reason" Decode.string
                Thinking = get.Optional.Field "thinking" Decode.string
                Attempts = get.Required.Field "attempts" Decode.int
                LatencyMs = get.Required.Field "latency_ms" Decode.int
                Applied = get.Optional.Field "applied" Decode.int
                Fallback = get.Optional.Field "fallback" Decode.string
            })

    /// 牌谱的 wire 形态。**导出与分享是同一个编码器**（分享那条先 `stripThinking`）。
    let encoder: Encoder<Paifu> =
        fun paifu ->
            Encode.object
                [
                    "version", Encode.int paifu.Version
                    "ruleset", Ruleset.encoder paifu.Ruleset
                    "events", paifu.Events |> List.map Event.encoder |> Encode.list
                    "decisions", paifu.Decisions |> List.map recordEncoder |> Encode.list
                ]

    let private versionDecoder: Decoder<int> =
        Decode.int
        |> Decode.andThen (fun version ->
            if List.contains version supported then
                Decode.succeed version
            else
                Decode.fail $"unsupported paifu version: {version}")

    /// **两条分享路径共用的那一个解码器**（ADR-0002）：JSON 导入的全量牌谱与 URL 里
    /// 省掉了 thinking 的牌谱，读出来是同一个类型。诊断文案用英文（ADR-0001）。
    let decoder: Decoder<Paifu> =
        Decode.object (fun get ->
            {
                Version = get.Required.Field "version" versionDecoder
                Ruleset = get.Required.Field "ruleset" Ruleset.decoder
                Events = get.Required.Field "events" (Decode.list Event.decoder)
                Decisions = get.Required.Field "decisions" (Decode.list recordDecoder)
            })
