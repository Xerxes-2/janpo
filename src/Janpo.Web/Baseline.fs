namespace Janpo.Web

open Fable.Core
open Thoth.Json.Core
open Thoth.Json.JavaScript
open Janpo

/// **强 AI 基线**那几 MB 资产此刻在哪一步（票 92；ADR-0006 的边界 1 与 2）。
///
/// **四态刻意是四个 case**（判据 12：拒绝理由各有各的 case）：页面上要分得出
/// 「这一桌压根没选它」「正在拉」「拉到了」「拉不动（原因）」——
/// 混成一个带 option 的记录，第一态与第四态在页面上就长得一模一样了。
///
/// **`Absent` 是「一个字节都没拉」的执行者**（ADR-0006 边界 1）：拉那一步只由
/// 「某一席被拨到强 AI 基线」触发（`TableState.baselineCmd`），首页与普通对局
/// 因此恒停在这一态。闸门量的正是它（不选它的那些趟，网络请求计数为 0）。
[<RequireQualifiedAccess>]
type BaselineStatus =
    /// 这一桌没有强 AI 基线席：**一个字节都不拉**。
    | Absent
    /// 正在拉那几 MB（页面要说话——ADR-0006 边界 1 的后半句）。
    | Loading
    /// 拉到了、也起得来，可以出手。`bytes` 是那份产物的字节数，页面上印出来
    /// ——「这一席让你多下了多少东西」是访客有权知道的事。
    | Ready of bytes: int
    /// 拉不动 / 起不来 / 出不了手：一句中文原因。**那一席退回自带 bot，其余席照常打完**
    /// （ADR-0006 边界 2：它是可选依赖，不是单点）。
    | Unavailable of reason: string

/// 强 AI 基线的一次出手。**跨界回来的东西只有它**：一个动作 id（或者一句「我交不出来」）。
///
/// **它比 `AgentAnswer` 短得多，而这正是这一席的性质**（票 92 的要害）：
/// 没有 thinking、没有一句话理由、没有 token 账单、没有 prompt 尾部与 preamble
/// ——那几样它一样都给不出，因此这里一格都不留。**别为它编一句**（票面原话）。
type BaselineAnswer = {
    /// 它选定的动作在这一包里的 id；交不出来时是 None。
    ActionId: int option
    /// 交不出来的原因（中文，给人看）。**它是「这一手要兜底」的信号**。
    Failure: string option
    /// 端到端毫秒（喂事件流 + 推理 + 翻译）。**只进状态线，不进牌谱**：
    /// 它是本机的一个耗时，不是这一场对局的事实。
    LatencyMs: int
}

/// F#/TS 的第四道边界（ADR-0005：**只有 F# 调 TS 这一个方向，且只传字符串**）。
///
/// 出去的是一段 JSON（就是 `DecisionPackage.encoder` 的产物，一个字段都不改写），
/// 回来的是一段 JSON（一个动作 id 或者一句原因）。TS 侧拿不到 `GameState`，
/// 也构造不出 `Action`；它认识的只有 id——**与 LLM 席、真人席逐字同一条规矩**。
///
/// **那几 MB 怎么拉、mjai 怎么翻译全在 TS 那侧**（`web/src/baseline/`）：
/// 这一层只管「拉没拉到」与「它交回来的 id 是哪一条」。
[<RequireQualifiedAccess>]
module Baseline =

    // ---- 渲染层的单向出口（ADR-0001） ----

    /// 四态给机器看的那一半（`data-baseline`），闸门读它；人读的是那一行中文。
    ///
    /// **懒加载那道闸门量的就是 `absent`**（ADR-0006 边界 1）：
    /// 不选那一席的那几趟，它恒是 `absent` 且网络请求计数为 0——两样一起断言。
    let toWire (status: BaselineStatus) : string =
        match status with
        | BaselineStatus.Absent -> "absent"
        | BaselineStatus.Loading -> "loading"
        | BaselineStatus.Ready _ -> "ready"
        | BaselineStatus.Unavailable _ -> "unavailable"

    /// 字节数写成人读得懂的那一句。**页面上要印出来**：
    /// 「选这一席让你多下了多少东西」是访客有权知道的事（ADR-0006 唯一的真实成本）。
    let bytesToDisplay (bytes: int) : string =
        let mib = float bytes / 1048576.0
        $"%.1f{mib} MiB"

    // ---- JSON ----

    /// 拉资产那一趟的回执：拉到了多少字节，或者一句中文原因。
    ///
    /// **`error` 优先**：TS 那侧两个字段只会出现一个，真都出现时以坏消息为准
    /// ——把一次失败读成成功，页面就会摆着一个不存在的选手。
    let private loadDecoder: Decoder<Result<int, string>> =
        Decode.object (fun get ->
            match get.Optional.Field "error" Decode.string with
            | Some reason -> Error reason
            | None ->
                match get.Optional.Field "bytes" Decode.int with
                | Some bytes -> Ok bytes
                | None -> Error "强 AI 基线拉不动：TS 侧的信封里既没有字节数也没有原因")

    /// 一次出手的回执。**每个字段都可以缺**（同 `Agent.answerDecoder`）：
    /// 读不动的代价是这一手兜底，太贵。
    let private answerDecoder: Decoder<BaselineAnswer> =
        Decode.object (fun get -> {
            ActionId = get.Optional.Field "action_id" Decode.int
            Failure = get.Optional.Field "failure" Decode.string
            LatencyMs = get.Optional.Field "latency_ms" Decode.int |> Option.defaultValue 0
        })

    /// 一次问话的 wire 形态。`decision` 就是 `DecisionPackage.encoder` 的产物
    /// ——**与 Agent 层读的是同一份东西**，也与 `janpo decide` 打印的是同一份。
    ///
    /// **座位不另传**：它就写在包上（`DecisionPackage.seat`），传第二份只会对不上。
    let private requestEncoder: Encoder<DecisionPackage> =
        fun package -> Encode.object [ "decision", DecisionPackage.encoder package ]

    // ---- 构造 ----

    /// 「这一手交不出来」的回执。TS 侧根本没答上话时由这边造一条，
    /// **兜底路径因此只有一条**（同 `Agent.refused`）。
    let refused (reason: string) : BaselineAnswer = {
        ActionId = None
        Failure = Some reason
        LatencyMs = 0
    }

    // ---- 边界（ADR-0005） ----

    /// TS 侧的两个入口，`web/src/baseline/baseline.ts` 导出的那两个函数。
    [<Import("load", "../../web/src/baseline/baseline.ts")>]
    let private loadAsset () : JS.Promise<string> = jsNative

    [<Import("decide", "../../web/src/baseline/baseline.ts")>]
    let private askFor (_request: string) : JS.Promise<string> = jsNative

    /// 去拉那几 MB 并起起来。**这个 Promise 不会 reject**：TS 那侧把 404、离线、
    /// 编译不动与初始化失败都做成了值，万一还是抛了，`catch` 把它也变成一句原因。
    ///
    /// **只有「某一席被拨到强 AI 基线」才调得到它**（ADR-0006 边界 1）。
    let load () : JS.Promise<Result<int, string>> =
        let read (envelope: string) : Result<int, string> =
            match Decode.fromString loadDecoder envelope with
            | Ok result -> result
            | Error message -> Error $"强 AI 基线拉不动：信封读不动（{message}）"

        let failed (error: obj) : Result<int, string> = Error $"强 AI 基线拉不动：取它时抛了异常（{error}）"

        (loadAsset ()).``then``(read).catch (failed)

    /// 问它这一手打什么。**这个 Promise 同样不会 reject**（理由同上）。
    let ask (package: DecisionPackage) : JS.Promise<BaselineAnswer> =
        let read (text: string) : BaselineAnswer =
            match Decode.fromString answerDecoder text with
            | Ok answer -> answer
            | Error message -> refused $"强 AI 基线的回执读不动：{message}"

        let failed (error: obj) = refused $"强 AI 基线抛了异常：{error}"

        (requestEncoder package |> Encode.toString 0 |> askFor).``then``(read).catch (failed)
