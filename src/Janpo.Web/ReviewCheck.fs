/// 复盘那几个数的**浏览器侧锚点**（票 90）。
///
/// 跨界只传字符串（ADR-0005）：进去是一份牌谱原文与一个座位号，出来是**引擎当时算出来的
/// 那几个数**的 JSON 文本。无头闸门（`web/scripts/verify-review.mjs`）拿它与页面上渲出来的
/// 那几行逐字对拍——「与引擎直接算的逐字相同」这句话因此有一个可执行的右侧。
///
/// **它不调 `Review`**（判据 6：断言「两种算法给出同一结果」时，先问两侧是不是同一个实现）：
/// 这里从 `Replay.traceOfPaifu` 拿逐局的开局局面与动作序列，自己 `GameState.step` 走一遍
/// ——页面那一侧走的是 `Table.replay` fold 出来的帧。两条路各自到达同一手，
/// 再各自向**同一个引擎**问那一份脚手架（`DecisionPackage.forSeat`）。
///
/// **「更好的候选」不在这里算**：那一栏的判据是帕累托占优，闸门那侧照**规则**再写一遍
/// （判据 8：期望值取自规则，不取自被检查那句话的来源），因此这里只把引擎的**逐张试打表**
/// 原样交出去，让闸门自己去推。
module Janpo.Web.ReviewCheck

open Thoth.Json.Core
open Thoth.Json.JavaScript
open Janpo

let private failure (message: string) : string =
    Encode.object [ "error", Encode.string message ] |> Encode.toString 0

/// 「有就编，没有就写 null」（同 `Scaffold` 里那一个）。
let private optional (encode: 'a -> IEncodable) : 'a option -> IEncodable =
    function
    | Some value -> encode value
    | None -> Encode.nil

/// 一条试打：牌、进退向、有效牌枚数与危险度的**安全度序**（0 最安全）。
/// 闸门按这三项推「哪几张比你打的那张更好」。
let private trialEncoder (trial: DahaiScaffold) : IEncodable =
    Encode.object [
        "pai", Encode.string (Tile.toMjai trial.Pai)
        "delta", Encode.int trial.ShantenDelta
        "ukeire", optional (Ukeire.total >> Encode.int) trial.Ukeire
        "kinds", optional (Ukeire.kindCount >> Encode.int) trial.Ukeire
        "order", optional (fun (danger: Danger) -> Encode.int (DangerTier.order danger.Tier)) trial.Danger
    ]

/// 这一席这一手：引擎在**落定之前**那一刻给出的那份脚手架。
let private noteEncoder (turn: int) (action: Action) (scaffold: Scaffold option) : IEncodable =
    let trial =
        match scaffold, action with
        | Some scaffold, Action.Dahai(_, pai, _) ->
            scaffold.Dahai |> List.tryFind (fun each -> each.Pai = Tile.deaka pai)
        | _, _ -> None

    let danger = trial |> Option.bind (fun trial -> trial.Danger)

    Encode.object [
        "turn", Encode.int turn
        "kind", Encode.string (HumanSeat.kind action)
        "label", Encode.string (Action.toDisplay action)
        // 他打出去的那一张（去红后的牌种）：闸门按它在下面那张试打表里认出「你打的那一条」。
        // **不让闸门去 label 里认牌**：那是拿渲染层的中文当判据（ADR-0001 的禁令）。
        "pai", optional (fun (each: DahaiScaffold) -> Encode.string (Tile.toMjai each.Pai)) trial
        "shanten", optional (fun (each: Scaffold) -> Encode.int (Shanten.value each.Shanten)) scaffold
        "after", optional (fun (each: DahaiScaffold) -> Encode.int (Shanten.value each.Shanten)) trial
        "delta", optional (fun (each: DahaiScaffold) -> Encode.int each.ShantenDelta) trial
        "ukeire", optional (fun (each: DahaiScaffold) -> optional (Ukeire.total >> Encode.int) each.Ukeire) trial
        "kinds", optional (fun (each: DahaiScaffold) -> optional (Ukeire.kindCount >> Encode.int) each.Ukeire) trial
        "danger", optional (fun (each: Danger) -> Encode.string (DangerTier.toWire each.Tier)) danger
        "rank", optional (fun (each: Danger) -> Encode.int each.Rank) danger
        "trials",
        scaffold
        |> Option.map (fun each -> each.Dahai |> List.map trialEncoder |> Encode.list)
        |> Option.defaultValue (Encode.list [])
    ]

/// 一份牌谱 + 一个座位号 → 那一席**每一手**的那几个数。
///
/// 手序按引擎的口径跨局累计（`Table.Turns` 那个号），因此闸门可以拿它与页面上
/// `data-review-turn` 逐个对上。座位号越界、牌谱读不动、回放不动都回 `{error}`。
let expected (text: string) (index: int) : string =
    match Decode.fromString Paifu.decoder text, Seat.ofIndex index with
    | Error message, _ -> failure $"牌谱读不动：{message}"
    | _, None -> failure $"{index} 不是一个合法座位"
    | Ok paifu, Some seat ->
        match Replay.traceOfPaifu paifu with
        | Error error -> failure (ReplayError.toDisplay error)
        | Ok kyokus ->
            // **抛不出去**：闸门那一侧的契约是一份失败清单，一条 `page.evaluate` 里抛出来的异常
            // 会把十七趟一起搞挂（票 86/87/88 各写下过同一课）。下面那一句 `failwith` 走不到
            // （这份牌谱刚从同一个引擎里导出来），真走到了就当一句中文原因交回去。
            try
                // 逐手推进：走的是引擎自己的 `step`，与页面那一侧的 `Table.replay` 各走各的。
                let played (turns: int, notes: IEncodable list, state: GameState) (action: Action) =
                    let taken =
                        if Action.actor action = seat then
                            let scaffold =
                                DecisionPackage.forSeat seat state |> Option.bind DecisionPackage.scaffold

                            noteEncoder turns action scaffold :: notes
                        else
                            notes

                    match GameState.step state action with
                    | Ok(next, _) -> turns + 1, taken, next
                    // 走不到：这份牌谱刚从同一个引擎里导出来。真走到了就当场说清是哪一手。
                    | Error illegal -> failwith $"第 {turns} 手引擎拒了：{IllegalAction.toDisplay illegal}"

                let kyoku (turns: int, notes: IEncodable list) (each: ReplayKyoku) =
                    let turns, notes, _ =
                        ((turns, notes, each.Opening), each.Actions) ||> List.fold played

                    turns, notes

                let turns, notes = ((0, []), kyokus) ||> List.fold kyoku

                Encode.object [
                    "seat", Encode.int index
                    "turns", Encode.int turns
                    "notes", notes |> List.rev |> Encode.list
                ]
                |> Encode.toString 0
            with error ->
                failure error.Message
