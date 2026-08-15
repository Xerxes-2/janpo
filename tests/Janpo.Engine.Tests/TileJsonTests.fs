namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Xunit
open Thoth.Json.Newtonsoft
open Janpo

/// Tile 的 JSON 编解码。mjai wire 上牌就是一个字符串，编码必须与 `Tile.toMjai` 一致。
/// 引擎只依赖 Thoth.Json.Core（Fable 兼容），具体后端由 dotnet 侧的工程提供。
[<Properties(Arbitrary = [| typeof<TileArbitraries> |])>]
module TileJsonTests =

    let private encode (tile: Tile) = Encode.toString 0 (Tile.encoder tile)

    [<Fact>]
    let ``牌编码为 mjai 记法字符串`` () =
        Assert.Equal("\"5mr\"", encode (Tile.tryCreate Manzu 5 true |> Option.get))
        Assert.Equal("\"1z\"", encode (Tile.tryCreate Jihai 1 false |> Option.get))

    [<Fact>]
    let ``非法记法字符串解码为错误值`` () =
        match Decode.fromString Tile.decoder "\"8z\"" with
        | Ok tile -> failwith $"8z 不该解码成功，却得到 {Tile.toMjai tile}"
        | Error _ -> ()

    [<Property>]
    let ``任意 Tile 的 JSON 编解码往返不变`` (tile: Tile) =
        Decode.fromString Tile.decoder (encode tile) = Ok tile
