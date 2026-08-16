// 27 号验收：找一个**供托真的结转到下一局**的种子（立直 + 那一局流局 ⇒ 立直棒留到下一局）。
// 随机选手是**均匀随机**（`Kyoku.randomPlayer`：从合法动作集里等概率挑一个），立直因此很稀有。
// 用 fsi 直调真实 API，不移植逻辑（AGENTS.md）。
#load "/home/xerxes2/janpo-ws-a/scripts/fsi/load-engine.fsx"

open Janpo

let ruleset = Ruleset.yonma

/// 一整场东风战逐局的 (局, 本场, 供托, 该局有没有立直成立)。
let playGame (seed: int) =
    match Game.runRandom ruleset (Rng.ofSeed seed) with
    | Error _ -> []
    | Ok(game, _) ->
        Game.played game
        |> List.map (fun state ->
            let context = GameState.context state

            let riichi =
                GameState.events state
                |> List.exists (fun event ->
                    match event with
                    | RiichiAccepted _ -> true
                    | _ -> false)

            context.Kyoku, context.Honba, context.Kyotaku, riichi)

let games = [ 1..2000 ] |> List.map (fun seed -> seed, playGame seed)

let carried =
    games
    |> List.filter (fun (_, rows) -> rows |> List.exists (fun (_, _, kyotaku, _) -> kyotaku > 0))

let withRiichi =
    games
    |> List.filter (fun (_, rows) -> rows |> List.exists (fun (_, _, _, r) -> r))

printfn "1..2000 局里："
printfn "  有立直成立的种子 %d 个：%A" (List.length withRiichi) (withRiichi |> List.map fst |> List.truncate 15)
printfn "  供托结转到下一局的种子 %d 个：%A" (List.length carried) (carried |> List.map fst |> List.truncate 15)
printfn ""

for seed, rows in carried |> List.truncate 5 do
    printfn "seed %d" seed

    for kyoku, honba, kyotaku, riichi in rows do
        printfn "  东%d局 %d本场 供托%d根 该局有立直=%b" kyoku honba kyotaku riichi
