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
///
/// `shanten` 是**打完它之后**那个绝对向听数：票 107 的逐数溯源要拿它认领
/// 「向听更好（1 向听）」那一句里的那个数——拿 `delta` 去倒推等于闸门自己算了一遍。
let private trialEncoder (trial: DahaiScaffold) : IEncodable =
    Encode.object [
        "pai", Encode.string (Tile.toMjai trial.Pai)
        "shanten", Encode.int (Shanten.value trial.Shanten)
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

// ---- 强 AI 那一行要拿哪一份观测去问（票 93） ----

/// 一条动作给机器看的那一半。**这是闸门那一侧自己写的一份**（判据 6）：
/// 页面那一侧的 `Review.keyOf` 不在这里用，两边各写各的，对不上就是错。
let private keyOf (action: Action) : string =
    match action with
    | Action.Dahai(_, pai, _) -> $"dahai:{Tile.toMjai pai}"
    | _ -> HumanSeat.kind action

/// 一份问话的 wire 形态（TS 那侧的 `decide` 只读这三格：座位、历史、那一包动作）。
///
/// 上帝视角那两份拿它拼：**历史换成该席看不到的那一份，动作仍旧是那一手的那一包**
/// ——否则换的就不只是视角，而是换了一道题。
let private askEncoder (seat: Seat) (history: IEncodable list) (options: ActionOption list) : IEncodable =
    Encode.object [
        "seat", Seat.encoder seat
        "history", Encode.list history
        "actions", options |> List.map ActionOption.encoder |> Encode.list
    ]

/// 那一手的一条：投影本人，加上两份**故意的上帝视角**（判据 1 要的那一次红）：
///
/// - `god_later`：**同一席、同一局，但拿的是它在这一局最后一次出手时那一份流**
///   ——复盘时整局都在手上，「随手一喂」喂的就是这一种：它因此知道你后来摸到了什么，
///   而那是你那一手不可能知道的牌（票面原话：它会给出一个人类做不到的答案）；
/// - `god_all`：**一条不掩、一张不隐**（`GodView.stream`），四家的手牌都摊着。
///
/// 两份都只进闸门，**产品代码里没有任何一处造得出它们**。
let private planEncoder
    (seat: Seat)
    (turn: int)
    (action: Action)
    (package: DecisionPackage)
    (godLater: GameState option)
    (godAll: GameState option)
    : IEncodable =
    let options = DecisionPackage.options package
    let history = DecisionPackage.history package

    let godly (stream: MaskedEvent list) =
        askEncoder seat (stream |> List.map MaskedEvent.encoder) options

    Encode.object [
        "turn", Encode.int turn
        "kind", Encode.string (HumanSeat.kind action)
        "played_id", optional Encode.int (DecisionPackage.tryId action package)
        // 他家摸的那几张在这份投影里被遮着几条（闸门拿它做阳性对照：为 0 就是什么都没量到）。
        "hidden",
        history
        |> List.sumBy (fun event ->
            match event with
            | MaskedEvent.Tsumo(actor, None) when actor <> seat -> 1
            | _ -> 0)
        |> Encode.int
        "options",
        options
        |> List.map (fun option ->
            Encode.object [
                "id", Encode.int (ActionOption.id option)
                "key", Encode.string (keyOf (ActionOption.action option))
            ])
        |> Encode.list
        "decision", askEncoder seat (history |> List.map MaskedEvent.encoder) options
        "god_later", optional (fun (later: GameState) -> godly (Observation.stream seat later)) godLater
        "god_all",
        optional (fun (state: GameState) -> godly (GodView.stream state |> List.map MaskedEvent.Public)) godAll
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
            // 会把同一条跑道上其余那几趟一起搞挂（票 86/87/88 各写下过同一课）。下面那一句 `failwith` 走不到
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

/// 一份牌谱 + 一个座位号 → 那一席每一手**当时那份投影**（要问强 AI 就拿它去问），
/// 外加每隔 `godEvery` 手一份**故意的上帝视角**（见 `planEncoder`）。
///
/// **这是闸门那一侧的重建，不是页面那一份**（判据 6）：它走 `Replay.traceOfPaifu` +
/// `GameState.step`，页面那一侧走 `Table.replay` 的帧；两条路各自到达同一手，
/// 再各自向**同一个引擎**要那一份 `DecisionPackage.forSeat`。闸门拿这一份去问强 AI，
/// 与页面上渲出来的那一行逐手对拍——「喂给它的是同一份投影」这句话因此有一个可执行的右侧。
///
/// `godEvery <= 0` 就一份上帝视角都不造（那两份只服务破坏实验与「构造 A≠B」那一条）。
let asks (text: string) (index: int) (godEvery: int) : string =
    match Decode.fromString Paifu.decoder text, Seat.ofIndex index with
    | Error message, _ -> failure $"牌谱读不动：{message}"
    | _, None -> failure $"{index} 不是一个合法座位"
    | Ok paifu, Some seat ->
        match Replay.traceOfPaifu paifu with
        | Error error -> failure (ReplayError.toDisplay error)
        | Ok kyokus ->
            // **抛不出去**（同 `expected`）：闸门那一侧的契约是一份失败清单。
            try
                let played (turns: int, mine: (int * Action * GameState) list, state: GameState) (action: Action) =
                    let taken =
                        if Action.actor action = seat then
                            (turns, action, state) :: mine
                        else
                            mine

                    match GameState.step state action with
                    | Ok(next, _) -> turns + 1, taken, next
                    | Error illegal -> failwith $"第 {turns} 手引擎拒了：{IllegalAction.toDisplay illegal}"

                let kyoku (turns: int, notes: IEncodable list) (each: ReplayKyoku) =
                    let turns, taken, _ = ((turns, [], each.Opening), each.Actions) ||> List.fold played

                    let mine = List.rev taken

                    // 这一席在这一局里**最后一次打牌**那一刻的局面：`god_later` 拿的就是它。
                    // **挑打牌那一步而不是最后一步**：最后一步常常是响应阶段（能鸣不鸣），
                    // 那时它会回一句「过」——那也是一个人做不到的答案，但读报告的人看不出
                    // 「它拿后来那副手牌在选牌」这件事。打牌对打牌，A 与 B 才是同一种东西。
                    let dahai (action: Action) =
                        match action with
                        | Action.Dahai _ -> true
                        | _ -> false

                    let last =
                        mine
                        |> List.filter (fun (_, action, _) -> dahai action)
                        |> List.tryLast
                        |> Option.map (fun (turn, _, state) -> turn, state)

                    let encoded =
                        mine
                        |> List.mapi (fun order (turn, action, state) ->
                            let package =
                                match DecisionPackage.forSeat seat state with
                                | Some package -> package
                                | None -> failwith $"第 {turn} 手引擎该给得出一份决策包"

                            // 只在抽到的那几手上造上帝视角（每一手都造的话，一整局要多背几十份事件流）。
                            // **只在打牌那几手上造**，而且后来那一手要真的在后面：
                            // 否则「后来」与「此刻」是同一刻，什么都证不了。
                            let sampled = godEvery > 0 && order % godEvery = 0 && dahai action

                            let godLater =
                                if sampled then
                                    last |> Option.filter (fun (latest, _) -> latest > turn) |> Option.map snd
                                else
                                    None

                            let godAll = if sampled then Some state else None

                            planEncoder seat turn action package godLater godAll)

                    turns, notes @ encoded

                let turns, notes = ((0, []), kyokus) ||> List.fold kyoku

                Encode.object [
                    "seat", Encode.int index
                    "turns", Encode.int turns
                    "notes", Encode.list notes
                ]
                |> Encode.toString 0
            with error ->
                failure error.Message
