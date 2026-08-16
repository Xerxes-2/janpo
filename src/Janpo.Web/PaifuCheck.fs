/// 牌谱回放的**浏览器侧入口**（票 26）。
///
/// 跨界只传字符串（ADR-0005）：进去是一份导出的牌谱原文，出来是报告的 JSON 文本。
/// **回放逻辑不在这里**——它在引擎的 `Replay`（就是 `GameState.step` 本身，ADR-0002）；
/// 这个文件只负责把 JS 侧的 JSON 后端接上去，与 `Golden.check` 一一对应。
///
/// 无头闸门 `web/scripts/verify-export.mjs` 下载完那个文件就把原文喂进这里：
/// **浏览器下下来的那份字节**回放出的事件流必须与它自己记的逐条相同。
module Janpo.Web.PaifuCheck

open Thoth.Json.Core
open Thoth.Json.JavaScript
open Janpo

let private failure (message: string) : string =
    Encode.object [ "error", Encode.string message ] |> Encode.toString 0

let private ints (values: int list) : IEncodable =
    values |> List.map Encode.int |> Encode.list

/// 事件流去掉开头那条 `start_game`：它的 `names` 来自配桌，不属于任何一局，
/// 因此回放产出的事件流里也没有它。
let private replayable (events: Event list) : Event list =
    events
    |> List.filter (fun event ->
        match event with
        | StartGame _ -> false
        | _ -> true)

/// 两条事件流第一处不同的地方（都相同则为 None）。差异要说得出在哪，不能只说「不一样」。
let private firstMismatch (expected: Event list) (actual: Event list) : string option =
    let render (events: Event list) (index: int) =
        events
        |> List.tryItem index
        |> Option.map (Event.encoder >> Encode.toString 0)
        |> Option.defaultValue "（到此为止）"

    List.init (max (List.length expected) (List.length actual)) id
    |> List.tryFind (fun index -> render expected index <> render actual index)
    |> Option.map (fun index -> $"第 {index} 条：牌谱 {render expected index}，回放 {render actual index}")

/// 读一份牌谱、把它的事件流交回引擎 fold，回一份报告 JSON。
/// 读不动或回放不动时回 `{error}`——闸门那侧两种都是红，但要分得清是谁的错。
let check (text: string) : string =
    match Decode.fromString Paifu.decoder text with
    | Error message -> failure $"牌谱读不动：{message}"
    | Ok paifu ->
        match Replay.ofPaifu paifu with
        | Error error -> failure (ReplayError.toDisplay error)
        | Ok replayed ->
            let expected = replayable paifu.Events
            let actual = Replayed.events replayed

            // 事件流走到哪，点数就报到哪：还在打的那一局报它此刻的点数，
            // 正好在某局收尾处结束的报这一场的。
            let scores =
                replayed.Current
                |> Option.map GameState.scores
                |> Option.defaultValue (Game.scores replayed.Game)

            let mismatch =
                firstMismatch expected actual
                |> Option.map Encode.string
                |> Option.defaultValue Encode.nil

            let juni =
                Replayed.result replayed
                |> Option.map (fun result -> ints result.Juni)
                |> Option.defaultValue Encode.nil

            let counted (has: DecisionRecord -> bool) =
                paifu.Decisions
                |> List.sumBy (fun record -> if has record then 1 else 0)
                |> Encode.int

            Encode.object [
                "version", Encode.int paifu.Version
                "events", paifu.Events |> List.length |> Encode.int
                "decisions", paifu.Decisions |> List.length |> Encode.int
                "kyokus", Game.played replayed.Game |> List.length |> Encode.int
                "same_events", Encode.bool (expected = actual)
                "mismatch", mismatch
                "scores", ints scores
                "juni", juni
                "thinking", counted (fun record -> Option.isSome record.Thinking)
                "fallbacks", counted (fun record -> Option.isSome record.Fallback)
                // prompt 的前置（票 31）：每手的尾部能不能接回一整份 prompt，靠它两项。
                "preambles", paifu.Prompting.Preambles |> List.length |> Encode.int
                "tail_chars",
                paifu.Decisions
                |> List.sumBy (fun record -> record.PromptTail.Length)
                |> Encode.int
                // 每一手的尾部都指得回一份 preamble（否则重建不回当时那两条消息）。
                "rebuildable",
                Encode.bool (
                    paifu.Decisions
                    |> List.forall (fun record ->
                        Prompting.preambleFor record.Seat record.RenderVersion paifu.Prompting
                        |> Option.isSome)
                )
            ]
            |> Encode.toString 0
