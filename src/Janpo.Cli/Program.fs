module Janpo.Cli.Program

open Janpo

/// 无头驱动入口。规则与算法都在 Janpo.Engine 里，这里只做参数解析与打印；
/// 后续票新增子命令时，在 `main` 的 match 上加一支，逻辑仍写进引擎库。
let private usage =
    """janpo —— 无头驱动入口

用法:
  janpo tile <记法>...    读入一串 mjai 牌记法，打印规范形、牌数与人类可读形式
  janpo --help            打印本帮助

示例:
  janpo tile "1z 5sr 5s 9s 3m"
  janpo tile 1z 5sr 5s 9s 3m"""

/// `janpo tile <记法>...`：多个参数按空格拼接后当作一串记法。
let private runTile (arguments: string list) : int =
    match Tile.parseMany (String.concat " " arguments) with
    | Error error ->
        eprintfn "%s" (TileListParseError.toDisplay error)
        1
    | Ok tiles ->
        let sorted = Tile.sort tiles
        printfn "%s" (Tile.toMjaiMany sorted)
        printfn "count: %d" (List.length sorted)
        printfn "display: %s" (sorted |> List.map Tile.toDisplay |> String.concat " ")
        0

[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | []
    | [ "--help" ]
    | [ "-h" ]
    | [ "help" ] ->
        printfn "%s" usage
        0
    | "tile" :: arguments -> runTile arguments
    | unknown ->
        eprintfn "未知命令: %s" (String.concat " " unknown)
        eprintfn "%s" usage
        2
