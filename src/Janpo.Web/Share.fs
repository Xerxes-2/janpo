namespace Janpo.Web

open Fable.Core
open Thoth.Json.Core
open Thoth.Json.JavaScript
open Janpo

/// URL 分享的那一段载荷（票 77）：**牌谱 ↔ 一串放得进 hash 的字符**。
///
/// 这是本工程第二处 F# 调 TS 的地方（ADR-0005：只有这一个方向，且只传字符串）——
/// 压缩要的 `CompressionStream` 是浏览器 API 且是**异步**的，因此两个出口都是 Promise，
/// 形状与 `Agent.ask` 同类。压缩与 base64url 那一层在 `web/src/share/payload.ts`，
/// **这一层不碰字节**：它只做牌谱那一侧的两件事——上路前的变换、下路后的解码。
///
/// **页面不在这一票里**（票 78 才接地址栏）：这里只有两个纯函数式的出口。
[<RequireQualifiedAccess>]
module Share =

    // ---- 边界（ADR-0005） ----

    /// 一段文本 → 载荷。压缩是异步的，因此它是 Promise。
    [<Import("encodePayload", "../../web/src/share/payload.ts")>]
    let private encodePayload (_text: string) : JS.Promise<string> = jsNative

    /// 载荷 → 一个**信封 JSON**（`{"text"}` 或 `{"error"}`）。**它不 reject、不空手而归**：
    /// 读不动的四种失法在 TS 那侧各有一句中文原因，一律以「载荷读不动：」开头。
    [<Import("decodePayload", "../../web/src/share/payload.ts")>]
    let private decodePayload (_payload: string) : JS.Promise<string> = jsNative

    /// 信封的两种。诊断原样带过来——措辞是 TS 那侧写的，这边不重写一遍
    /// （**同一句话只有一处**，改了那边这边不必跟着改）。
    let private envelopeDecoder: Decoder<Result<string, string>> =
        Decode.object (fun get ->
            match get.Optional.Field "error" Decode.string with
            | Some reason -> Error reason
            | None ->
                match get.Optional.Field "text" Decode.string with
                | Some text -> Ok text
                | None -> Error "载荷读不动：TS 侧的信封里既没有正文也没有原因")

    // ---- 出去 ----

    /// 一份牌谱 → URL hash 里那一段。
    ///
    /// **URL 只带棋谱**（`Paifu.stripAudit`）：决策记录与 prompt 前置整段不上 URL，
    /// 理由与实测长度写在那个变换旁边。JSON 导出那条路不走这里，它仍是全量。
    let toPayload (paifu: Paifu) : JS.Promise<string> =
        paifu |> Paifu.stripAudit |> Paifu.encoder |> Encode.toString 0 |> encodePayload

    // ---- 回来 ----

    /// 载荷 → 里面那份牌谱**原文**。两层诊断分得清：这一层只回「载荷读不动：……」。
    ///
    /// 单独留一个出口是因为闸门要拿这份原文查「三样审计数据一个都不在」——
    /// 而 `ofPayload` 那一层已经把它变成了值。
    let paifuText (payload: string) : JS.Promise<Result<string, string>> =
        let read (envelope: string) : Result<string, string> =
            match Decode.fromString envelopeDecoder envelope with
            | Ok result -> result
            | Error message -> Error $"载荷读不动：信封读不动（{message}）"

        let failed (error: obj) : Result<string, string> = Error $"载荷读不动：解载荷时抛了异常（{error}）"

        (decodePayload payload).``then``(read).catch (failed)

    /// 载荷 → 牌谱。**读不动就是一句中文原因，不抛、不空手而归**（票 77）：
    /// 「载荷读不动：……」是这段字符本身不对（截断、抄错一位、不是我们发出去的），
    /// 「牌谱读不动：……」是载荷解得开、里面那份牌谱不合形状（引擎的诊断，英文，ADR-0001）。
    /// 票 78 那句提示按这个前缀分得出该劝人「链接是不是被截断了」还是「这份牌谱太新／太旧」。
    let ofPayload (payload: string) : JS.Promise<Result<Paifu, string>> =
        let readPaifu (text: string) : Result<Paifu, string> =
            Decode.fromString Paifu.decoder text
            |> Result.mapError (fun message -> $"牌谱读不动：{message}")

        (paifuText payload).``then`` (Result.bind readPaifu)

/// 分享载荷的**浏览器侧入口**（票 77），与 `PaifuCheck` / `Golden.check` 同一形态：
/// 跨界只传字符串，进去是牌谱原文与载荷，出来是报告的 JSON 文本。
///
/// 无头闸门是 `web/scripts/verify-share.mjs`，三件事：真往返（编→解→`Paifu.decoder`→`Replay`）、
/// 改坏一个字符必须当场红在「载荷读不动」、以及**载荷里没有审计那三样**。
/// **它不碰页面**——语料由 `sample` 现打一场出来（页面是票 78 的地盘）。
[<RequireQualifiedAccess>]
module ShareCheck =

    let private failure (message: string) : string =
        Encode.object [ "error", Encode.string message ] |> Encode.toString 0

    let private ints (values: int list) : IEncodable =
        values |> List.map Encode.int |> Encode.list

    /// 两条事件流第一处不同的地方（都相同则为 None）——与 `PaifuCheck` 同一条口径：
    /// 差异要说得出在哪，不能只说「不一样」。
    let private firstMismatch (expected: Event list) (actual: Event list) : string option =
        let render (events: Event list) (index: int) =
            events
            |> List.tryItem index
            |> Option.map (Event.encoder >> Encode.toString 0)
            |> Option.defaultValue "（到此为止）"

        List.init (max (List.length expected) (List.length actual)) id
        |> List.tryFind (fun index -> render expected index <> render actual index)
        |> Option.map (fun index -> $"第 {index} 条：原牌谱 {render expected index}，载荷里 {render actual index}")

    /// 闸门的语料：随机四家现打一整场，回一份牌谱原文（没有决策记录——审计那三样由闸门自己拌进去）。
    ///
    /// **长度记账靠它**：东风战与半庄各来一场，两个数才比得出「URL 只带棋谱」值不值。
    /// 长度名认不出来就当场抛：那是闸门自己传的常量，默默换一种长度会把记账写歪。
    let sample (lengthWire: string) (seed: int) : string =
        let length =
            match GameLength.ofWire lengthWire with
            | Some length -> length
            | None -> failwith $"认不出这个对局长度：{lengthWire}"

        let ruleset = Ruleset.yonma |> Ruleset.withLength length

        match Game.runRandom ruleset (Rng.ofSeed seed) with
        | Error error -> failure $"这一场没打完：{KyokuError.toDisplay error}"
        | Ok(game, _) ->
            let events =
                StartGame [ "random"; "random"; "random"; "random" ] :: Game.events game

            Paifu.create ruleset events [] Prompting.empty
            |> Paifu.encoder
            |> Encode.toString 0

    /// 一份牌谱原文 → 载荷，外加这份牌谱**上路前**带着多少审计数据。
    ///
    /// 后半截是**阳性对照**（票 34 那道 key 闸门的规矩）：拌进去的 thinking / prompt 尾部
    /// 若压根没进来，「载荷里没有它们」就什么都没证明。
    let encode (text: string) : JS.Promise<string> =
        match Decode.fromString Paifu.decoder text with
        | Error message -> JS.Constructors.Promise.resolve (failure $"牌谱读不动：{message}")
        | Ok paifu ->
            let counted (has: DecisionRecord -> bool) =
                paifu.Decisions |> List.sumBy (fun record -> if has record then 1 else 0)

            let report (payload: string) : string =
                Encode.object [
                    "payload", Encode.string payload
                    "payload_chars", payload.Length |> Encode.int
                    "full_chars", text.Length |> Encode.int
                    // 压缩前的那一份：变换之后、编码之前的牌谱原文有多少字。
                    "kifu_chars",
                    Paifu.stripAudit paifu
                    |> Paifu.encoder
                    |> Encode.toString 0
                    |> String.length
                    |> Encode.int
                    "events", paifu.Events |> List.length |> Encode.int
                    "decisions", paifu.Decisions |> List.length |> Encode.int
                    "thinking", counted (fun record -> Option.isSome record.Thinking) |> Encode.int
                    "tail_chars",
                    paifu.Decisions
                    |> List.sumBy (fun record -> record.PromptTail.Length)
                    |> Encode.int
                    "preambles", paifu.Prompting.Preambles |> List.length |> Encode.int
                ]
                |> Encode.toString 0

            (Share.toPayload paifu).``then`` report

    /// 一段载荷 → 里面那份牌谱的**原文**（`{"text"}`）或者一句读不动的原因（`{"error"}`）。
    ///
    /// 闸门拿它查「审计那三样一个都不在」——**要按字节查就得在变成值之前接一手**。
    let read (payload: string) : JS.Promise<string> =
        let report (result: Result<string, string>) : string =
            match result with
            | Ok text -> Encode.object [ "text", Encode.string text ] |> Encode.toString 0
            | Error message -> failure message

        (Share.paifuText payload).``then`` report

    /// 一段载荷 + 它本来那份牌谱 → 一份对照报告。**真往返验的就是这一条**：
    /// 解出来的事件流与原牌谱逐条相同、两侧回放出的终局点数与顺位相同。
    ///
    /// **走的就是票 78 要调的那个 `Share.ofPayload`**（不是另搭一条路）：
    /// 那一句写错了这道闸门就该红。载荷读不动、牌谱读不动都回 `{error}`：
    /// 两种在闸门那侧都是红，但要分得清是谁的错
    /// （反向自证要求红在「载荷读不动」，红在别处不算数）。
    let check (text: string) (payload: string) : JS.Promise<string> =
        let compare (kifu: Paifu) : string =
            match Decode.fromString Paifu.decoder text with
            | Error message -> failure $"牌谱读不动：{message}"
            | Ok full ->
                let replayed (paifu: Paifu) =
                    Replay.ofPaifu paifu
                    |> Result.map (fun replayed -> Replayed.events replayed, Replayed.result replayed)

                match replayed full, replayed kifu with
                | Error error, _ -> failure $"原牌谱回放不动：{ReplayError.toDisplay error}"
                | _, Error error -> failure $"载荷里那份牌谱回放不动：{ReplayError.toDisplay error}"
                | Ok(fullEvents, fullResult), Ok(kifuEvents, kifuResult) ->
                    let mismatch =
                        firstMismatch full.Events kifu.Events
                        |> Option.map Encode.string
                        |> Option.defaultValue Encode.nil

                    let scores (result: GameResult option) =
                        result
                        |> Option.map (fun result -> ints result.Scores)
                        |> Option.defaultValue Encode.nil

                    let juni (result: GameResult option) =
                        result
                        |> Option.map (fun result -> ints result.Juni)
                        |> Option.defaultValue Encode.nil

                    Encode.object [
                        "version", Encode.int kifu.Version
                        "same_version", Encode.bool (full.Version = kifu.Version)
                        "same_ruleset", Encode.bool (full.Ruleset = kifu.Ruleset)
                        "events", kifu.Events |> List.length |> Encode.int
                        // 逐条相同比的是**牌谱里的事件流**，回放那一条比的是 fold 出来的。
                        "same_events", Encode.bool (full.Events = kifu.Events)
                        "same_replay", Encode.bool (fullEvents = kifuEvents)
                        "mismatch", mismatch
                        "scores", scores kifuResult
                        "full_scores", scores fullResult
                        "juni", juni kifuResult
                        "full_juni", juni fullResult
                        // 变换抹掉的那两段：解出来必须是零。
                        "decisions", kifu.Decisions |> List.length |> Encode.int
                        "preambles", kifu.Prompting.Preambles |> List.length |> Encode.int
                    ]
                    |> Encode.toString 0

        let report (result: Result<Paifu, string>) : string =
            match result with
            | Ok kifu -> compare kifu
            | Error message -> failure message

        (Share.ofPayload payload).``then`` report
