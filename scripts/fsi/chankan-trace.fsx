// 抢杠那一轮的探针（票 98）。**它回答三个问题**：
//
//   1. 摊得出抢杠的牌山吗——两条摊好的轨迹逐步打出来，牌理由引擎复核（判据 19）；
//   2. 把它挂进 `GameStateArbitraries.Traces` 能买到什么——密度、给定权重的一趟开口率，
//      以及「要一趟必开口得给多大权重」；
//   3. 挂上去会撞坏什么——**全部吃 GameState 的属性**逐条喂给这两条轨迹，逐条报红。
//
// 票 98 的判决就是照第 2、3 问的数下的：挂上去只买到一趟 47%，却当场把七条现成的属性按红
// （两条轨迹合起来十条），因此那两条轨迹挂在 `KanProperties` 的定点锚点上（每趟 100%），不进取值域表。
//
// **票 99 把那十条红收了九条**：第四类（观测 / 决策包 / 掩蔽流）是 fold 的真 bug，修在
// `SeatStream` 里；第一 / 二 / 三类是那三条断言自己写错了，各自改在断言侧。
// 剩下的一条是术语问题——`Masked/不出现他家暗牌` 在**国士抢暗杠**那个窗口里本来就不成立：
// 那四张牌必须亮给别家看，而它们在引擎里仍是暗牌。它要的是术语裁决，不是把断言调松。
//
// **票 100 把那一条也收了**：主人裁定「宣言中的暗杠」是公开信息（`CONTEXT.md` 的
// `Ankan Declaration`），那条不变量的**定义域**不含宣言中的那几张，判据因此改成
// 「先摘掉还没成立的那条杠宣言再比」。同时摧了一条**加杠加上去的那张是红宝牌**的轨迹
// （票 99 报告 §5.4 那个同型漏：碰 `5s 5s 5s`、加杠加 `5sr`），因此这里现在扫四条轨迹。
//
//   nix develop --command dotnet build -c Release
//   dotnet fsi --exec scripts/fsi/chankan-trace.fsx

#I @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0"
#r "FsCheck.dll"
#r "Thoth.Json.Core.dll"
#r "Janpo.Engine.dll"
#r "Janpo.Engine.Tests.dll"

open Janpo
open Janpo.Engine.Tests
open Janpo.Engine.Tests.GameStateFixtures

// ---- 一、两条轨迹长什么样 ----

let isChankan (state: GameState) : bool =
    match GameState.phase state with
    | AwaitingResponse phase ->
        match phase.Cause with
        | ResponseCause.Kan _ -> true
        | ResponseCause.Dahai -> false
    | AwaitingDahai _
    | Ended _ -> false

let describeAction (action: Action) : string =
    let at (seat: Seat) = Seat.index seat

    match action with
    | Action.Dahai(actor, pai, tsumogiri) ->
        let how = if tsumogiri then "摸切" else "手切"
        $"打{Tile.toMjai pai}({how},{at actor})"
    | Action.Hora(actor, target, pai) -> $"和{Tile.toMjai pai}({at actor}←{at target})"
    | Action.Pon(actor, target, pai, _) -> $"碰{Tile.toMjai pai}({at actor}←{at target})"
    | Action.Chi(actor, target, pai, _) -> $"吃{Tile.toMjai pai}({at actor}←{at target})"
    | Action.Ankan(actor, tiles) -> $"暗杠{Tile.toMjai (List.head tiles)}({at actor})"
    | Action.Kakan(actor, pai, _) -> $"加杠{Tile.toMjai pai}({at actor})"
    | Action.Minkan(actor, target, pai, _) -> $"大明杠{Tile.toMjai pai}({at actor}←{at target})"
    | Action.Riichi actor -> $"立直({at actor})"
    | Action.Ryuukyoku actor -> $"九种九牌({at actor})"
    | Action.None actor -> $"过({at actor})"

let describePhase (state: GameState) : string =
    match GameState.phase state with
    | AwaitingDahai phase -> $"等打牌（座位 {Seat.index phase.Actor}）"
    | AwaitingResponse phase ->
        match phase.Cause with
        | ResponseCause.Dahai -> $"等响应（座位 {Seat.index phase.Target} 打出 {Tile.toMjai phase.Pai}）"
        | ResponseCause.Kan kan ->
            let kind =
                match Naki.kind kan with
                | NakiKind.Ankan -> "暗杠"
                | NakiKind.Kakan -> "加杠"
                | NakiKind.Minkan -> "大明杠"
                | NakiKind.Pon -> "碰"
                | NakiKind.Chi -> "吃"

            $"**抢杠那一轮**（座位 {Seat.index phase.Target} 宣言{kind} {Tile.toMjai phase.Pai}）"
    | Ended _ -> "终局"

/// 太长的一行截断，别把终端刷满。
let truncated (limit: int) (text: string) : string =
    if text.Length <= limit then
        text
    else
        text.Substring(0, limit) + "…"

let printTrace (label: string) (states: GameState list) =
    printfn "== %s：%d 步，抢杠局面 %d 个 ==" label (List.length states) (states |> List.filter isChankan |> List.length)

    states
    |> List.iteri (fun index state ->
        let actions =
            GameState.legalActions state
            |> List.collect (fun choice -> choice.Actions)
            |> List.map describeAction
            |> String.concat " "

        printfn "%2d %-52s %s" index (describePhase state) (truncated 400 actions))

    match GameState.horas (List.last states) with
    | [] -> printfn "（这一局没有和了）"
    | horas ->
        for hora in horas do
            printfn
                "和了：座位 %d ← 座位 %d，%s，%d 番 %d 符 %d 点"
                (Seat.index hora.Actor)
                (Seat.index hora.Target)
                (Tile.toMjai hora.Pai)
                hora.Fan
                hora.Fu
                hora.HoraPoints

    printfn ""

let kakanStates = chankanTrace chankanScript

let ankanStates = kokushiChankanTrace kokushiChankanScript

/// 票 100 那条：同一座牌山、同一个选手，只把那四张 5s 里「哪一张是红的」换了个位置：
/// 碰的是三张正五、加杠加的是 `5sr`。
let akadoraStates = chankanTrace chankanAkadoraScript

printTrace "加杠抢杠（chankanScript + chankanSeeking，默认规则集）" kakanStates
printTrace "国士抢暗杠（kokushiChankanScript + kanSeeking，雀魂规则集）" ankanStates
printTrace "加杠抢杠、加的那张是红宝牌（chankanAkadoraScript，默认规则集）" akadoraStates

// ---- 二、牌理由引擎复核（判据 19：人工可核对的证据先拿引擎核一遍）----

printfn "== chankanScript 的牌理（引擎算的，不是手算的）=="

let robber = SeatFixtures.seat 2

let robberHand = tilesOf "1m 2m 3m 4m 5m 6m 7p 8p 9p 4s 6s 9m 9m"

let shantenOfHand (tiles: Tile list) : int =
    match HandShape.create 0 tiles with
    | Ok shape -> Shanten.value (Shanten.calculate kindSet shape)
    | Error error -> failwith $"{HandShapeError.toDisplay error}"

let waits =
    Tile.kinds
    |> List.filter (fun tile -> shantenOfHand (robberHand @ [ tile ]) = -1)

printfn "座位 2 的配牌 %s：向听 %d，听 %s" (Tile.toMjaiMany robberHand) (shantenOfHand robberHand) (Tile.toMjaiMany waits)

/// 某座位在某个局面上此刻和不和得了。
let horaOf (seat: Seat) (state: GameState) : string =
    match GameState.horaOf seat state with
    | Ok reading ->
        reading.Tally
        |> YakuTally.yaku
        |> List.map Yaku.toDisplay
        |> String.concat " + "
    | Error error -> $"和不了（{YakuError.toDisplay error}）"

let firstDahai = List.item 1 kakanStates

let chankanState = kakanStates |> List.find isChankan

printfn "Oya 第 1 巡打出 5s 那一手，座位 2：%s" (horaOf robber firstDahai)

printfn
    "被问的座位：%A"
    (GameState.legalActions firstDahai
     |> List.map (fun choice -> Seat.index choice.Seat))

printfn "抢杠那一轮，座位 2：%s" (horaOf robber chankanState)
printfn ""

// ---- 三、假如把它挂进 `GameStateArbitraries.Traces` ----
//
// 一个样本的命中概率 `p = (w / (W + w)) × 这条轨迹里抢杠局面的密度`，
// 一趟（FsCheck 默认 100 个样本）至少开一次口的概率 `1 − (1 − p)^100`。

let tableWeight =
    GameStateArbitraries.Traces 1 |> List.sumBy (fun (weight, _, _) -> weight)

let atLeastOnce (probability: float) : float = 1.0 - (1.0 - probability) ** 100.0

let density (states: GameState list) : float =
    float (states |> List.filter isChankan |> List.length)
    / float (List.length states)

printfn "== 假如把加杠抢杠那条挂进取值域表（现在的权重合计 %d）==" tableWeight
printfn "%6s %10s %12s" "权重" "采样 p" "一趟开口率"

let kakanDensity = density kakanStates

for weight in [ 1; 2; 4; 8; 16 ] do
    let probability = float weight / float (tableWeight + weight) * kakanDensity

    printfn "%6d %9.4f%% %11.3f%%" weight (100.0 * probability) (100.0 * atLeastOnce probability)

// 要一趟 99% 开口：p ≥ 1 − 0.01^(1/100)，反解出份额与权重。
let needed = 1.0 - 0.01 ** 0.01

let neededShare = needed / kakanDensity

let neededWeight = float tableWeight * neededShare / (1.0 - neededShare)

printfn ""
printfn "抢杠局面在这条轨迹里的密度 %.1f%%（%d 步里 1 步）" (100.0 * kakanDensity) (List.length kakanStates)

printfn
    "要一趟 99%% 开口，需要 p ≥ %.4f%%，即这条轨迹独占权重表的 %.1f%%（权重 %.0f / 合计 %.0f）"
    (100.0 * needed)
    (100.0 * neededShare)
    neededWeight
    (float tableWeight + neededWeight)

printfn ""

// ---- 四、挂上去会撞坏什么：全部吃 GameState 的属性逐条喂给这两条轨迹 ----

/// 返回 `Property`（带 `Prop.label`）的那两条：跑一趟，抛异常就算红。
let ofProperty (f: GameState -> FsCheck.Property) : GameState -> bool =
    fun state ->
        let saved = System.Console.Out
        use writer = new System.IO.StringWriter()

        try
            try
                System.Console.SetOut writer
                FsCheck.Check.QuickThrowOnFailure(f state)
                true
            with _ ->
                false
        finally
            System.Console.SetOut saved

let properties: (string * (GameState -> bool)) list =
    [
        "GameState/合法动作集非空", GameStateProperties.``任意时刻合法动作集非空，或这一局已终``
        "GameState/牌数守恒", GameStateProperties.``牌数守恒：各家手牌、河与副露里自家出的那几张加上山上的牌恒为完整一副``
        "GameState/鸣牌后牌数守恒", GameStateProperties.``鸣牌后牌数守恒：暗牌加副露的张数与没鸣牌时一样多``
        "GameState/14 张", GameStateProperties.``等着打牌的那家 14 张，自摸和了的那家 14 张，其余各家 13 张``
        "GameState/动作推得动局面", GameStateProperties.``合法动作集里的每个动作都推得动局面``
        "GameState/响应能过且 Ron 有条件", GameStateProperties.``响应阶段等的每一家都能「过」，且它的 Ron 只在不振听、型成立、有役时出现``
        "GameState/鸣牌三条", GameStateProperties.``鸣牌：吃只吃上家、河底牌鸣不得、鸣完那一手一定有牌可打``
        "GameState/刚鸣完那一手", GameStateProperties.``鸣牌：刚鸣完那一手不摸牌、只有手切，且打不出被鸣的那张（禁食替）``
        "GameState/被鸣那张仍在河里", GameStateProperties.``鸣牌：被鸣的那张仍在对家的河里，那家的河也被标成鸣走过``
        "GameState/和了收尾守恒", GameStateProperties.``和了收尾时授受把点数与供托一起守恒``
        "GameState/事件流 JSON 往返", GameStateProperties.``事件流的 JSON 往返不变``
        "Kan/王牌张数", KanProperties.``王牌恒是规则集给的那么多张：杠取走一张岭上牌，可摸区就补进一张``
        "Kan/可摸区张数", KanProperties.``可摸区每杠少一张：剩余张数恒等于「开局可摸张数 − 已摸 − 杠数」``
        "Kan/杠数与新宝牌", KanProperties.``杠数与新宝牌：每个杠翻一张，明杠那张欠到下一次杠或下一次打牌``
        "Kan/杠后牌数守恒", KanProperties.``杠后牌数守恒：一组杠仍旧只折三张暗牌``
        "Kan/杠的副露牌种唯一", KanProperties.``杠的副露里牌种唯一、四张齐全``
        "Kan/杠只在摸完牌那一手", KanProperties.``摸牌后阶段的杠只在自己摸完牌那一手出现，且杠数没到上限``
        "Kan/抢杠那一轮", KanProperties.``抢杠那一轮只有荣和与「过」，宣言杠的那家不在被问之列``
        "Riichi/供托守恒", RiichiProperties.``场上的立直棒恒等于局初供托加上这一局成立的立直，和了收走之后归零``
        "Riichi/reach 配对", RiichiProperties.``每条 reach_accepted 都对得上一条 reach，且成立的不多于宣言的``
        "Riichi/一发", RiichiProperties.``一发只可能亮在立直成立的那家头上，任何鸣牌之后全场都没有一发``
        "Riichi/立直后只剩摸切", RiichiProperties.``立直成立之后那家只剩自摸和、暗杠与摸切，宣言牌那一手只剩仍然听牌的打法``
        "Riichi/立直中鸣不了但被问荣和", RiichiProperties.``立直中的座位鸣不了牌，但仍然被问到荣和``
        "Riichi/立直中的手牌不再变", RiichiProperties.``立直中的家永远听牌，且它的手牌自立直起不再变``
        "Danger/有威胁才有排序", DangerProperties.``任意局面，有威胁的家才有排序，且每张可打之牌恰好一条``
        "Danger/名次不下降", DangerProperties.``任意局面，名次从 1 起、不下降，并列的共用一个名次``
        "Danger/现物在前", DangerProperties.``任意局面，现物排在非现物之前``
        "Danger/现物档", DangerProperties.``任意局面，现物档意味着对每一家都是现物``
        "Danger/筋与壁", DangerProperties.``任意局面，筋与壁两档都说得出理由``
        "Decision/每个被问座位都有包", DecisionPackageProperties.``任意局面，正在被问的每个座位都有决策包，包里就是它的合法动作集``
        "Decision/没被问的没有包", DecisionPackageProperties.``任意局面，没被问到的座位没有决策包``
        "Decision/id 取得回动作", DecisionPackageProperties.``任意局面，包里每个 id 都取得回一个引擎接受的动作``
        "Decision/包外 id 是 None", DecisionPackageProperties.``任意局面，包外的 id 一律是 None 而不是异常``
        "Decision/label 不重复", DecisionPackageProperties.``任意局面，同一包里两条动作的 label 不相同``
        "Decision/历史 fold", ofProperty DecisionPackageProperties.``任意局面，包里的历史 fold 出来的就是包里的那份观测``
        "Decision/唯一掩蔽流", ofProperty DecisionPackageProperties.``任意局面，包里的历史就是那条唯一的掩蔽流``
        "Masked/不出现他家暗牌", MaskedStreamProperties.``任意局面任意座位，掩蔽流里不出现他家暗牌中的任何一张``
        "Masked/只有自家摸牌带牌面", MaskedStreamProperties.``任意局面任意座位，只有自家那几条摸牌带着牌面``
        "Masked/与上帝视角对齐", MaskedStreamProperties.``任意局面任意座位，掩蔽流与上帝视角那条流一样长且逐条对齐``
        "Masked/立直后全摸切", MaskedStreamProperties.``任意局面，立直成立之后那一家的打牌全是摸切（宣言牌除外）``
        "Masked/鸣完必手切", MaskedStreamProperties.``任意局面，碰或吃之后的那一张打牌必然是手切``
        "Obs/看得见的牌", ObservationProperties.``任意局面任意座位，观测里的每一张牌都是这个座位看得见的``
        "Obs/序列化不漏暗牌", ObservationProperties.``任意局面任意座位，观测的序列化结果里不出现他家暗牌里的牌``
        "Obs/河一致", ObservationProperties.``任意局面任意座位，观测的河与那一家的河逐张一致``
        "Obs/他家手牌张数", ObservationProperties.``任意局面任意座位，他家的手牌张数与那一家实际的一致``
        "Obs/fold 与引擎一致", ObservationProperties.``任意局面任意座位，掩蔽流 fold 出来的观测与引擎的状态逐字段一致``
        "Obs/上帝视角", ObservationProperties.``任意局面，上帝视角亮出每一家的暗牌``
        "Ryuukyoku/九种九牌判据", RyuukyokuProperties.``九种九牌的判据只看第一巡与幺九牌种数``
        "Scaffold/每个被问座位都有脚手架", ScaffoldProperties.``任意局面，正在被问的每个座位都有脚手架``
        "Scaffold/有效牌算得出", ScaffoldProperties.``任意局面，有效牌恒算得出来：可见张数从不数重``
        "Scaffold/剩余枚数 0..4", ScaffoldProperties.``任意局面，剩余枚数落在 0 到 4 之间``
        "Scaffold/打一张向听不变小", ScaffoldProperties.``任意局面，打一张牌不会让向听数变小``
        "Score/点数供托守恒", ScoreProperties.``任一步之后，四家点数与场上供托之和不变``
        "Score/和了事件自洽", ScoreProperties.``和了事件里的点数与授受自洽``
    ]

let sweep (label: string) (states: GameState list) =
    let reds =
        [
            for name, predicate in properties do
                for index, state in List.indexed states do
                    if not (predicate state) then
                        yield name, index, isChankan state
        ]

    printfn "== %s：%d 条属性 × %d 步 ==" label (List.length properties) (List.length states)

    match reds with
    | [] -> printfn "全绿"
    | _ ->
        printfn "%-30s %6s %8s" "属性" "第几步" "抢杠局面"

        for name, index, chankan in reds do
            printfn "%-30s %6d %8b" name index chankan

    printfn ""

printfn "== 四、把这几条轨迹喂给全部吃 GameState 的属性 =="
printfn ""
sweep "加杠抢杠" kakanStates
sweep "国士抢暗杠（雀魂）" ankanStates
sweep "加杠抢杠、加的那张是红宝牌" akadoraStates

// ---- 四之二、fold 出来的观测与引擎在哪一段分了家（上面第四类红的细节）----

let printObservationDrift (label: string) (states: GameState list) =
    printfn "== %s：观测 vs 引擎的权威状态 ==" label

    let drifted =
        states
        |> List.filter (fun state ->
            Seat.all ruleset
            |> List.exists (fun seat ->
                Observation.ofState seat state
                |> Option.map (ObservationFixtures.mismatches seat state)
                |> Option.map (List.isEmpty >> not)
                |> Option.defaultValue false))

    if List.isEmpty drifted then
        printfn "逐步、逐座位、逐字段全对得上"

    states
    |> List.iteri (fun index state ->
        let drift =
            Seat.all ruleset
            |> List.choose (fun seat ->
                Observation.ofState seat state
                |> Option.map (ObservationFixtures.mismatches seat state)
                |> Option.bind (fun fields ->
                    if List.isEmpty fields then
                        None
                    else
                        Some(Seat.index seat, fields)))

        for seat, fields in drift do
            printfn "%2d 座位 %d 对不上：%s" index seat (String.concat "、" fields)

        // 那一段两边差的就是这一张：宣言杠的那家手里还握着它。
        if not (List.isEmpty drift) then
            let engineSide =
                GameState.players state
                |> Seat.indexed
                |> List.map (fun (seat, player) ->
                    let count = List.length (PlayerState.hand player)

                    let naki =
                        PlayerState.naki player
                        |> List.map (Naki.kind >> sprintf "%A")
                        |> String.concat ","

                    $"{Seat.index seat}:{count} 张[{naki}]")
                |> String.concat " "

            printfn "   引擎那侧：%s" engineSide)

    printfn ""

printObservationDrift "加杠抢杠" kakanStates
printObservationDrift "国士抢暗杠（雀魂）" ankanStates
printObservationDrift "加杠抢杠、加的那张是红宝牌" akadoraStates

// ---- 五、同一座牌山，抢的那家先立直：一发那条也红（票 98 报的第三类）----
//
// 座位 2 在第 3 步立直，抢杠时和的是「立直 + 一发 + 抢杠」——而 `RiichiProperties` 那条
// 一发的属性把 `Kakan` **宣言**当成鸣牌，于是在杠还没成立的那一刻就要求全场没有一发。
// 票 99 把那条判据改对了，并把这条轨迹从探针搬进了固件（`chankanRiichiTrace`）——
// 四族属性的定点锚点现在每趟都跑它，探针这里只是把同一条轨迹摄下来给人看。

let riichiStates = chankanRiichiTrace chankanScript

printTrace "加杠抢杠（抢的那家先立直）" riichiStates
sweep "加杠抢杠（抢的那家先立直）" riichiStates
printObservationDrift "加杠抢杠（抢的那家先立直）" riichiStates

printfn "取值域里现在有几个抢杠局面：跑 scripts/fsi/arbitrary-coverage.fsx 看 `chankan` 那一行（票 98 时是 0）。"
