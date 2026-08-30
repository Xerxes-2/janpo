/// 终局记分卡那几个数的**浏览器侧锚点**（票 133）。
///
/// 跨界只传字符串（ADR-0005）：进去是一份牌谱原文，出来是**引擎当时算出来的那几个数**
/// 的 JSON 文本。无头闸门（`web/scripts/verify-scorecard.mjs`）拿它与页面上那张表的
/// 每一格逐格对拍——「这张表里的数与引擎算的相同」这句话因此有一个可执行的右侧。
///
/// **它不是页面那一侧的那条路，但也不是完全独立的一条**（判据 6：断言「两种算法给出
/// 同一结果」时，先问两侧是不是同一个实现）。逐项说清楚，别把话说过头：
///
/// - **顺位与终点**：这里 `Replay.ofPaifu` 一次 fold 到底再取 `Game` 的精算，
///   页面那一侧走的是 `Table.replay` 摊出来的逐帧牌桌再取末帧的 `Board.final`。
///   两条路在 `Replay` 的那段 fold 上**是共用的**，分岔只在「怎么收」——
///   因此它抓得住装配错位（把 `Juni` 那一列反过来当场红出四条），抓不住 fold 本身的错。
/// - **逐席那七格**：两侧**共用** `Scorecard.tally`（判据 11：那一段该只有一份），
///   所以这一头的对拍在那七列上是**恒真式**。真正的右侧在闸门那边：
///   `verify-scorecard.mjs` 的 `tallyFromPaifu` 在 node 里照规则把那份 JSON 重数一遍。
module Janpo.Web.ScorecardCheck

open Thoth.Json.Core
open Thoth.Json.JavaScript
open Janpo

let private failure (message: string) : string =
    Encode.object [ "error", Encode.string message ] |> Encode.toString 0

/// 一席那一行：顺位与终点（终局精算）加上牌谱聚合出来的那几格。
let private seatEncoder (juni: int) (score: int) (tally: SeatTally) : IEncodable =
    Encode.object [
        "seat", Encode.int (Seat.index tally.Seat)
        "juni", Encode.int juni
        "score", Encode.int score
        "hora", Encode.int tally.Hora
        "hora_targeted", Encode.int tally.HoraTargeted
        "fallbacks", Encode.int tally.Fallbacks
        "retries", Encode.int tally.Retries
        "asked", Encode.int tally.Asked
        "input", Encode.int (Usage.promptTokens tally.Usage)
        "output", Encode.int tally.Usage.Output
    ]

/// 一份牌谱原文 → 记分卡那四行**引擎算出来的样子**。
///
/// 这一场还没终局（事件流断在半路）时回一句 `error`：**记分卡本来就只在终局那一屏有**，
/// 闸门要分得清「表不该在」与「表该在却算不出来」。
let tally (text: string) : string =
    match Decode.fromString Paifu.decoder text with
    | Error message -> failure $"牌谱读不动：{message}"
    | Ok paifu ->
        match Replay.ofPaifu paifu with
        | Error error -> failure (ReplayError.toDisplay error)
        | Ok replayed ->
            match Replayed.result replayed with
            | None -> failure "这份牌谱还没打完：没有终局精算，也就没有记分卡"
            | Some result ->
                let rows =
                    Scorecard.ofPaifu paifu
                    |> List.mapi (fun index tally ->
                        match List.tryItem index result.Juni, List.tryItem index result.Scores with
                        | Some juni, Some score -> Some(seatEncoder juni score tally)
                        // 走不到：`GameResult` 的两列与 `Seat.all` 同一个长度（都由规则集定）。
                        | _ -> None)
                    |> List.choose id

                Encode.object [ "seats", Encode.list rows ] |> Encode.toString 0
