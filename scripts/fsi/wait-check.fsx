// 票 91 的「可人工核对的那一手」复核：README 说 4p/7p 两面，主人说 147p 三面听。
#load "load-engine.fsx"
open Janpo
let ks = TileKindSet.fourPlayer

let parse (s: string) =
    match Tile.parseMany s with
    | Ok t -> t
    | Error e -> failwithf "%A" e

let mk (tiles: Tile list) =
    HandShape.create 0 tiles |> Result.defaultWith (fun e -> failwithf "%A" e)

let shanten (tiles: Tile list) =
    Shanten.calculate ks (mk tiles) |> Shanten.value

let hand13 = parse "1m 1m 2m 3m 4m 5m 6m 7m 2p 3p 4p 5p 6p"
printfn "手牌       %s" (Tile.toMjaiMany hand13)
printfn "向听       %d（0 = 听牌）" (shanten hand13)

let waits = Tile.kinds |> List.filter (fun t -> shanten (hand13 @ [ t ]) = -1)
printfn "听的是     %s（%d 面）" (Tile.toMjaiMany waits) (List.length waits)

// 真正被当成证据用的那句：摸进北之后，唯一保持听牌的打法是不是摸切北
let hand14 = hand13 @ parse "4z" // mjai 记法：北 = 4z

let removeOne (t: Tile) (tiles: Tile list) =
    let i = List.findIndex ((=) t) tiles

    List.mapi (fun j x -> j, x) tiles
    |> List.filter (fun (j, _) -> j <> i)
    |> List.map snd

let keepsTenpai =
    hand14
    |> List.distinct
    |> List.filter (fun t -> shanten (removeOne t hand14) = 0)

printfn "摸 N 之后，打哪张仍听牌：%s" (Tile.toMjaiMany keepsTenpai)
