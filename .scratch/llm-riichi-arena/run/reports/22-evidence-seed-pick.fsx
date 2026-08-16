#load "/home/xerxes2/janpo-ws-b/scripts/fsi/load-engine.fsx"

open Janpo

/// 挑一个当默认的种子：东 1 局里有杠、有碰吃，且以和了终（结算面板才有役种可看）。
let ruleset = Ruleset.yonma

let describe (seed: int) =
    match Rng.ofSeed seed |> Kyoku.runRandom ruleset (KyokuContext.initial ruleset) with
    | Error _ -> None
    | Ok(state, _) ->
        let events = GameState.events state

        let count (predicate: Event -> bool) =
            events |> List.filter predicate |> List.length

        let kan =
            count (fun e ->
                match e with
                | Ankan _
                | Kakan _
                | Minkan _ -> true
                | _ -> false)

        let naki =
            count (fun e ->
                match e with
                | Pon _
                | Chi _ -> true
                | _ -> false)

        let hora =
            count (fun e ->
                match e with
                | Hora _ -> true
                | _ -> false)

        if kan > 0 && naki > 0 && hora > 0 then
            Some(seed, kan, naki, List.length events)
        else
            None

[ 1..3000 ]
|> List.choose describe
|> List.truncate 10
|> List.iter (fun (seed, kan, naki, events) -> printfn $"seed {seed}: kan {kan} naki {naki} events {events}")
