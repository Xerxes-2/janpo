// 42 号票的验收数：同样扫 1..2000 号种子，数立直成立、一发、里宝牌、供托结转与振听。
//
// **与 27 号验收（§14.3）同一条路**：`Game.runRandom` 的驱动形态逐字复制（牌山与选手同一条
// 随机流，局与局之间续着走），只是逐手多看一眼局面——因此均匀随机那一列必须重现出
// 「15 场立直、3 场供托结转」。重现不出来就说明这份探针和当时量的不是同一件事。
//
// 一发与里宝牌**只有和了宣言的那一刻问得到**（`GameState.horaOf`，局终之后阶段已变），
// 所以这里逐手驱动而不是 `Game.runRandom` 一把跑完。用 fsi 直调真实 API，不移植逻辑（AGENTS.md）。
//
// 跑法：dotnet fsi --exec .scratch/llm-riichi-arena/run/reports/42-path-scan.fsx [种子上限]
#load "/home/xerxes2/janpo-ws-b/scripts/fsi/load-engine.fsx"

open Janpo

let ruleset = Ruleset.yonma

/// 一场对局里数到的那几件事。**局数与场数分开数**：立直成立的「场次」要与 27 那组数对得上，
/// 而一发、里宝牌这些按次数看才有意义。
type Tally =
    {
        Kyokus: int
        RiichiAccepted: int
        Ippatsu: int
        Uradora: int
        Hora: int
        /// 开局时供托非零的局数（上一局的立直棒结转了过来）。
        KyotakuCarried: int
        /// 某一刻有人处于永久振听的局数。
        FuritenPermanent: int
        /// 某一刻有人处于同巡振听的局数。
        FuritenDoujun: int
    }

let empty =
    {
        Kyokus = 0
        RiichiAccepted = 0
        Ippatsu = 0
        Uradora = 0
        Hora = 0
        KyotakuCarried = 0
        FuritenPermanent = 0
        FuritenDoujun = 0
    }

let add (a: Tally) (b: Tally) =
    {
        Kyokus = a.Kyokus + b.Kyokus
        RiichiAccepted = a.RiichiAccepted + b.RiichiAccepted
        Ippatsu = a.Ippatsu + b.Ippatsu
        Uradora = a.Uradora + b.Uradora
        Hora = a.Hora + b.Hora
        KyotakuCarried = a.KyotakuCarried + b.KyotakuCarried
        FuritenPermanent = a.FuritenPermanent + b.FuritenPermanent
        FuritenDoujun = a.FuritenDoujun + b.FuritenDoujun
    }

/// 这一刻有人振听吗（永久 / 同巡各看各的）。
let furitenNow (state: GameState) =
    let furiten = GameState.players state |> List.map PlayerState.furiten
    furiten |> List.exists (fun one -> one.Permanent), furiten |> List.exists (fun one -> one.Doujun)

/// 把一局逐手跑完，顺带在**和了宣言的那一刻**把读法捞下来（一发与里宝牌只有那时问得到）。
let playKyoku (player: Player<Rng>) (rng: Rng) (start: GameState) =
    let rec loop (rng: Rng) (state: GameState) (tally: Tally) =
        let permanent, doujun = furitenNow state

        let tally =
            { tally with
                FuritenPermanent = max tally.FuritenPermanent (if permanent then 1 else 0)
                FuritenDoujun = max tally.FuritenDoujun (if doujun then 1 else 0)
            }

        match GameState.legalActions state with
        | [] -> state, rng, tally
        | choice :: _ ->
            let action, advanced = player rng state choice

            let tally =
                match action with
                | Action.Hora(actor, _, _) ->
                    match GameState.horaOf actor state with
                    | Error _ -> tally
                    | Ok reading ->
                        let yaku = reading.Tally.Yaku |> List.map fst

                        { tally with
                            Hora = tally.Hora + 1
                            Ippatsu = tally.Ippatsu + (if List.contains Yaku.Ippatsu yaku then 1 else 0)
                            Uradora = tally.Uradora + (if reading.Tally.Uradora > 0 then 1 else 0)
                        }
                | _ -> tally

            match GameState.step state action with
            | Error illegal -> failwith $"不该发生：合法动作集里的动作被拒 —— {IllegalAction.toDisplay illegal}"
            | Ok(next, _) -> loop advanced next tally

    loop rng start empty

/// 一整场对局。**驱动形态与 `Game.runRandom` 逐字相同**：牌山与选手共用同一条随机流，
/// 开局那一步推进过的发生器接着给选手用，局与局之间续着走。
let playGame (player: Player<Rng>) (seed: int) =
    let rec loop (game: Game) (rng: Rng) (tally: Tally) =
        match Game.progress game with
        | GameProgress.Ended _ -> tally
        | GameProgress.NextKyoku context ->
            match GameState.start ruleset context rng with
            | Error error -> failwith $"不该发生：开不了局 —— {KyokuStartError.toDisplay error}"
            | Ok(start, advanced) ->
                let final, rng, one = playKyoku player advanced start

                let one =
                    { one with
                        Kyokus = 1
                        KyotakuCarried = (if context.Kyotaku > 0 then 1 else 0)
                        RiichiAccepted =
                            GameState.events final
                            |> List.sumBy (fun event ->
                                match event with
                                | RiichiAccepted _ -> 1
                                | _ -> 0)
                    }

                loop (Game.advance final game) rng (add tally one)

    loop (Game.start ruleset) (Rng.ofSeed seed) empty

/// 一批种子。**场次**（这一场里出过没出过）与**次数**分开数：27 那组数是场次。
let scan (player: Player<Rng>) (seeds: int list) =
    let games = seeds |> List.map (playGame player)
    let total = games |> List.fold add empty

    let gamesWith (pick: Tally -> int) =
        games |> List.filter (fun one -> pick one > 0) |> List.length

    total,
    [
        "立直成立", gamesWith (fun one -> one.RiichiAccepted)
        "一发", gamesWith (fun one -> one.Ippatsu)
        "里宝牌", gamesWith (fun one -> one.Uradora)
        "供托结转", gamesWith (fun one -> one.KyotakuCarried)
        "永久振听", gamesWith (fun one -> one.FuritenPermanent)
        "同巡振听", gamesWith (fun one -> one.FuritenDoujun)
        "和了", gamesWith (fun one -> one.Hora)
    ]

let upper =
    match
        System.Environment.GetCommandLineArgs()
        |> Array.tryLast
        |> Option.map System.Int32.TryParse
    with
    | Some(true, value) when value > 0 -> value
    | _ -> 2000

let seeds = [ 1..upper ]

let report (name: string) (player: Player<Rng>) =
    let started = System.Diagnostics.Stopwatch.StartNew()
    let total, games = scan player seeds
    printfn "== %s（种子 1..%d，%d 局，%.1f 秒）" name upper total.Kyokus started.Elapsed.TotalSeconds

    printfn
        "   场次：%s"
        (games
         |> List.map (fun (label, count) -> $"{label} {count}")
         |> String.concat "  ")

    printfn
        "   次数：立直成立 %d  一发 %d  里宝牌 %d  和了 %d  供托结转（局）%d"
        total.RiichiAccepted
        total.Ippatsu
        total.Uradora
        total.Hora
        total.KyotakuCarried

report "均匀随机（Kyoku.randomPlayer / RandomPlayer.uniform）" RandomPlayer.uniform
report "有主见的（OpinionatedPlayer.player）" OpinionatedPlayer.player
