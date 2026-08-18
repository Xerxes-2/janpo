// `GameStateArbitraries.GameState()` 的取值域覆盖率探针（票 96 数了一遍立直，票 97 把它做成常驻工具）。
//
// **它回答的问题**：那一族属性一趟到底开几次口。做法是把整个取值域（400 颗种子 × 那张轨迹表）
// 逐步走一遍，对每一类关键局面数两个数：
//
//   1. **采样概率 p**：按生成器真正的分布算——先均匀取种子，再按权重取轨迹，再在那条轨迹里均匀取一步。
//      `p = Σ_轨迹 (w/W) · 平均_种子(命中数 / 步数)`。**这才是「一个样本命中的概率」**。
//   2. **穷举占比**：全部局面里命中的比例（把与种子无关的固定轨迹按 400 次计）。
//      它不带权重，因此**与 p 不是一回事**；票 96 报的 0.103% 是这一个。
//
// 有了 p 就能算 `1 − (1 − p)^100`：**一趟属性（FsCheck 默认 100 个样本）至少开一次口的概率**。
//
// 轨迹表读的是 `GameStateArbitraries.Traces`（生成器用的同一份），因此这份探针不会与生成器飘开。
//
//   nix develop --command dotnet build -c Release
//   dotnet fsi --exec scripts/fsi/arbitrary-coverage.fsx

#I @"../../tests/Janpo.Engine.Tests/bin/Release/net10.0"
#r "FsCheck.dll"
#r "Thoth.Json.Core.dll"
#r "Janpo.Engine.dll"
#r "Janpo.Engine.Tests.dll"

open FsCheck
open FsCheck.FSharp
open Janpo
open Janpo.Engine.Tests

// ---- 关键局面：每一条对应一族属性里「不满足它就整条空转」的那个前提 ----

let riichiPlayers (state: GameState) : PlayerState list =
    GameState.players state
    |> List.filter (fun player -> RiichiState.isActive (PlayerState.riichi player))

let nakiKinds (state: GameState) : NakiKind list =
    GameState.players state |> List.collect PlayerState.naki |> List.map Naki.kind

let isKan (kind: NakiKind) : bool =
    match kind with
    | NakiKind.Ankan
    | NakiKind.Kakan
    | NakiKind.Minkan -> true
    | NakiKind.Pon
    | NakiKind.Chi -> false

let lastEvent (state: GameState) : Event option = GameState.events state |> List.tryLast

let isPonChiEvent (event: Event option) : bool =
    match event with
    | Some(Pon _)
    | Some(Chi _) -> true
    | _ -> false

/// 抢杠那一轮：响应阶段是对某个还没成立的杠的响应。
/// **不能拿「最后一条事件是杠」当判据**：宣言杠不改局面，那一刻还没有杠的事件；
/// 而杠成立时引擎当场就接上 `dora` / `tsumo`，最后一条事件也不是杠。
let isChankan (state: GameState) : bool =
    match GameState.phase state with
    | AwaitingResponse phase ->
        match phase.Cause with
        | ResponseCause.Kan _ -> true
        | ResponseCause.Dahai -> false
    | AwaitingDahai _
    | Ended _ -> false

let responses (state: GameState) : LegalActions list =
    match GameState.phase state with
    | AwaitingResponse phase -> phase.Responses
    | AwaitingDahai _
    | Ended _ -> []

let awaitingDahai (state: GameState) : LegalActions option =
    match GameState.phase state with
    | AwaitingDahai phase ->
        Some
            {
                Seat = phase.Actor
                Actions = phase.Actions
            }
    | AwaitingResponse _
    | Ended _ -> None

let hasAction (predicate: Action -> bool) (choice: LegalActions) : bool = choice.Actions |> List.exists predicate

let isRon (action: Action) : bool =
    match action with
    | Action.Hora _ -> true
    | _ -> false

let isKyuushu (action: Action) : bool =
    match action with
    | Action.Ryuukyoku _ -> true
    | _ -> false

let isAnkan (action: Action) : bool =
    match action with
    | Action.Ankan _ -> true
    | _ -> false

let isKanAction (action: Action) : bool =
    match action with
    | Action.Ankan _
    | Action.Kakan _
    | Action.Minkan _ -> true
    | _ -> false

/// 被问到的那几家里，有没有一家**看得见别人在做牌**（`Danger.threats` 非空的等价条件：
/// 别的座位立直中或有副露）。`riichiThreat` 只数「威胁里含立直」的那一半。
let threatSeen (riichiOnly: bool) (state: GameState) : bool =
    let asked = GameState.legalActions state |> List.map (fun choice -> choice.Seat)

    let threatening (seat: Seat) =
        GameState.players state
        |> Seat.indexed
        |> List.exists (fun (other, player) ->
            let riichi = RiichiState.isActive (PlayerState.riichi player)
            let naki = PlayerState.naki player |> List.isEmpty |> not

            other <> seat && (riichi || (naki && not riichiOnly)))

    asked |> List.exists threatening

/// 每一条：名字、哪一族属性靠它开口、判据。
let predicates: (string * string * (GameState -> bool)) list =
    [
        "riichi-active", "Riichi 全族", fun state -> riichiPlayers state |> List.isEmpty |> not
        "riichi-accepted",
        "Riichi 立直棒/一发",
        fun state ->
            GameState.players state
            |> List.exists (fun player -> RiichiState.isAccepted (PlayerState.riichi player))
        "riichi-naki",
        "Riichi stillTenpai 的 nakiCount>0 支",
        fun state ->
            riichiPlayers state
            |> List.exists (fun player -> PlayerState.nakiCount player > 0)
        "riichi-dahai",
        "Riichi「立直后只剩摸切」的 Accepted 支",
        fun state ->
            match GameState.phase state with
            | AwaitingDahai phase ->
                match GameState.player phase.Actor state with
                | Some player -> RiichiState.isAccepted (PlayerState.riichi player)
                | None -> false
            | AwaitingResponse _
            | Ended _ -> false
        "riichi-declared",
        "Riichi「宣言牌只剩听牌打法」的 Declared 支",
        fun state ->
            match GameState.phase state with
            | AwaitingDahai phase ->
                match GameState.player phase.Actor state with
                | Some player ->
                    match PlayerState.riichi player with
                    | RiichiState.Declared _ -> true
                    | RiichiState.Accepted _
                    | RiichiState.None -> false
                | None -> false
            | AwaitingResponse _
            | Ended _ -> false
        "riichi-response",
        "Riichi「立直中鸣不了但被问荣和」",
        fun state ->
            responses state
            |> List.exists (fun choice ->
                match GameState.player choice.Seat state with
                | Some player -> RiichiState.isActive (PlayerState.riichi player)
                | None -> false)
        "riichi-ankan-offered",
        "Riichi 立直后暗杠进动作集",
        fun state ->
            match awaitingDahai state with
            | Some choice ->
                let riichi =
                    match GameState.player choice.Seat state with
                    | Some player -> RiichiState.isActive (PlayerState.riichi player)
                    | None -> false

                riichi && hasAction isAnkan choice
            | None -> false
        "ippatsu", "Riichi 一发", fun state -> GameState.players state |> List.exists PlayerState.ippatsu
        "kyotaku", "Riichi 供托守恒的非零支", fun state -> GameState.kyotaku state > 0
        "threat-any", "Danger 全族（有威胁才排序）", threatSeen false
        "threat-riichi", "Danger 威胁里含立直", threatSeen true
        "naki-any", "GameState 鸣牌族", fun state -> nakiKinds state |> List.isEmpty |> not
        "naki-ponchi", "GameState 碰吃族", fun state -> nakiKinds state |> List.exists (isKan >> not)
        "naki-fresh", "GameState「刚鸣完那一手」", lastEvent >> isPonChiEvent
        "kan-any", "Kan 全族", fun state -> nakiKinds state |> List.exists isKan
        "kan-offered",
        "Kan「杠只在摸完牌那一手出现」",
        fun state ->
            match awaitingDahai state with
            | Some choice -> hasAction isKanAction choice
            | None -> false
        "chankan", "Kan 抢杠那一轮", isChankan
        "response", "GameState 响应阶段族", fun state -> responses state |> List.isEmpty |> not
        "ron-offered", "GameState「Ron 只在不振听有役时出现」", fun state -> responses state |> List.exists (hasAction isRon)
        "kyuushu-offered",
        "Ryuukyoku 九种九牌判据",
        fun state ->
            match awaitingDahai state with
            | Some choice -> hasAction isKyuushu choice
            | None -> false
        "hora", "Score 和了授受", fun state -> GameState.horas state |> List.isEmpty |> not
        "ended",
        "GameState 终局支",
        fun state ->
            match GameState.phase state with
            | Ended _ -> true
            | AwaitingDahai _
            | AwaitingResponse _ -> false
    ]

// ---- 扫描 ----

let seeds = [ 1..400 ]

let names = predicates |> List.map (fun (name, _, _) -> name)

/// 一条轨迹在一颗种子上的一次扫描：步数 + 每条判据的命中数。
let scanTrace (run: unit -> GameState list) : int * int list =
    let states = run ()

    let hits =
        predicates
        |> List.map (fun (_, _, predicate) -> states |> List.filter predicate |> List.length)

    List.length states, hits

/// 与种子无关的那几条轨迹（摊好的牌山 + 确定性选手）跑一次就够，其余逐种子跑。
let scriptedTraces =
    set
        [
            "threeKan"
            "minkan"
            "ankan"
            "riichiAnkan"
            "suuchaRiichi"
            "riichiRon"
            "tsumoHora"
            "doubleRon"
        ]

/// **票 97 之前的那张表**（票 96 全域扫描量的就是它）。它只用来做改前改后的对照：
/// 轨迹怎么跑照现在那份，只是把权重换回当时的（不在表里的新轨迹权重计 0）。
/// **改前改后用同一份判据量**，否则两组数不可比（判据 13）。
let legacyWeights =
    [
        "random", 4
        "tenpaiSeeking", 4
        "nakiSeeking", 4
        "kanSeeking", 4
        "threeKan", 2
        "minkan", 1
        "ankan", 1
        "riichiSeeking", 4
        "tsumoHora", 1
        "doubleRon", 1
    ]

let table = GameStateArbitraries.Traces 1

let weightTotal = table |> List.sumBy (fun (weight, _, _) -> weight)

/// 这颗种子上那条轨迹的跑法。
let runOf (name: string) (seed: int) : unit -> GameState list =
    GameStateArbitraries.Traces seed
    |> List.pick (fun (_, each, run) -> if each = name then Some run else None)

let started = System.Diagnostics.Stopwatch.StartNew()

/// 逐条轨迹：权重、总步数、总命中数、以及「一个样本落在这条轨迹上时的命中概率」。
let perTrace =
    table
    |> List.map (fun (weight, name, _) ->
        let scans =
            if Set.contains name scriptedTraces then
                runOf name 1 |> scanTrace |> List.replicate (List.length seeds)
            else
                seeds |> List.map (runOf name >> scanTrace)

        let lengths = scans |> List.map fst
        let hitLists = scans |> List.map snd

        let totals =
            names
            |> List.mapi (fun index _ -> hitLists |> List.sumBy (fun hits -> List.item index hits))

        // 一条轨迹内部是均匀取一步，因此这条轨迹的命中概率是逐种子「命中数 / 步数」的平均。
        let rates =
            names
            |> List.mapi (fun index _ ->
                List.zip lengths hitLists
                |> List.averageBy (fun (length, hits) -> float (List.item index hits) / float length))

        name, weight, List.sum lengths, totals, rates)

let elapsed = started.Elapsed.TotalSeconds

// ---- 报数 ----

printfn "扫描耗时 %.1f 秒；种子 1..400，轨迹 %d 条，权重合计 %d" elapsed (List.length table) weightTotal
printfn ""
printfn "== 逐条轨迹 =="
printfn "%-16s %6s %10s" "轨迹" "权重" "局面数"

for name, weight, states, _, _ in perTrace do
    printfn "%-16s %6d %10d" name weight states

printfn ""
printfn "总局面数 %d（固定轨迹按 400 次计）" (perTrace |> List.sumBy (fun (_, _, states, _, _) -> states))
printfn ""
printfn "== 逐条判据（改前 = 票 96 那张权重表，改后 = 当下这张）=="

printfn "%-22s %10s %10s %9s %9s %9s %9s  %s" "关键局面" "命中数" "穷举占比" "改前 p" "改前一趟" "改后 p" "改后一趟" "哪一族"

let totalStates = perTrace |> List.sumBy (fun (_, _, states, _, _) -> states)

let legacyTotal = legacyWeights |> List.sumBy snd

/// 一个样本命中第 `index` 条判据的概率：`Σ (w/W) · 那条轨迹里它的密度`。
let sampledAt (weightOf: string -> int) (total: int) (index: int) : float =
    perTrace
    |> List.sumBy (fun (name, _, _, _, rates) -> float (weightOf name) / float total * List.item index rates)

let atLeastOnce (probability: float) : float = 1.0 - (1.0 - probability) ** 100.0

let legacyWeightOf (name: string) : int =
    legacyWeights
    |> List.tryFind (fun (each, _) -> each = name)
    |> Option.map snd
    |> Option.defaultValue 0

let currentWeightOf (name: string) : int =
    table
    |> List.pick (fun (weight, each, _) -> if each = name then Some weight else None)

predicates
|> List.iteri (fun index (name, family, _) ->
    let hits =
        perTrace |> List.sumBy (fun (_, _, _, totals, _) -> List.item index totals)

    let before = sampledAt legacyWeightOf legacyTotal index
    let after = sampledAt currentWeightOf weightTotal index

    printfn
        "%-22s %10d %9.4f%% %8.4f%% %8.3f%% %8.4f%% %8.3f%%  %s"
        name
        hits
        (100.0 * float hits / float totalStates)
        (100.0 * before)
        (100.0 * atLeastOnce before)
        (100.0 * after)
        (100.0 * atLeastOnce after)
        family)

printfn ""

let dropped =
    predicates
    |> List.mapi (fun index (name, _, _) ->
        let before = atLeastOnce (sampledAt legacyWeightOf legacyTotal index)
        let after = atLeastOnce (sampledAt currentWeightOf weightTotal index)

        name, before, after)
    |> List.filter (fun (_, before, after) -> after < before - 0.0005)

match dropped with
| [] -> printfn "**一族都没掉**：每一条判据的「一趟至少开口一次」改后都 ≥ 改前。"
| _ ->
    printfn "**掉了的那几族（要么调权重，要么写进报告）**："

    for name, before, after in dropped do
        printfn "  %-22s %.3f%% → %.3f%%" name (100.0 * before) (100.0 * after)

printfn ""
printfn "== 逐条轨迹 × 逐条判据（命中数）=="

printfn
    "%-22s %s"
    "关键局面"
    (perTrace
     |> List.map (fun (name, _, _, _, _) -> sprintf "%12s" name)
     |> String.concat "")

predicates
|> List.iteri (fun index (name, _, _) ->
    let cells =
        perTrace
        |> List.map (fun (_, _, _, totals, _) -> sprintf "%12d" (List.item index totals))
        |> String.concat ""

    printfn "%-22s %s" name cells)

// ---- 二、拿真生成器模拟 20 趟：一趟 100 个样本，逐条判据数它开了几次口 ----
//
// 上面那半是**算**出来的概率，这半是**抽**出来的次数：同一件事的两条路径，对不上就说明哪边错了。
// 取样用的就是 `GameStateArbitraries.GameState()` 本身（FsCheck 的默认一趟也是 100 个样本）。
// `size` 不影响这个生成器（`Gen.choose` 与 `Gen.frequency` 都不看 size），因此固定取 50。

let runs = 20

let samplesPerRun = 100

let sampled = System.Diagnostics.Stopwatch.StartNew()

let arbitrary = GameStateArbitraries.GameState()

let perRun =
    [
        for run in 1..runs ->
            let states =
                Gen.sampleWithSeed (Rnd(uint64 (20260819 + run))) 50 samplesPerRun arbitrary.Generator

            predicates
            |> List.map (fun (_, _, predicate) -> states |> Array.filter predicate |> Array.length)
    ]

printfn ""
printfn "== %d 趟 × %d 个样本：每趟的开口次数（耗时 %.1f 秒）==" runs samplesPerRun sampled.Elapsed.TotalSeconds
printfn "%-22s %6s %6s  %s" "关键局面" "零次趟" "均值" "逐趟"

predicates
|> List.iteri (fun index (name, _, _) ->
    let counts = perRun |> List.map (List.item index)
    let zeros = counts |> List.filter (fun count -> count = 0) |> List.length
    let mean = counts |> List.averageBy float

    printfn "%-22s %6d %6.1f  %s" name zeros mean (counts |> List.map string |> String.concat " "))
