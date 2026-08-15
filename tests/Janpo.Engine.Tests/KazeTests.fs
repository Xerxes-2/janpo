namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Newtonsoft
open Janpo

/// 风的 mjai 记法与编解码。记法必须与 Tile 的字牌记法同源（ADR-0001 的 `1z`-`4z`）。
module KazeTests =

    [<Fact>]
    let ``四种风按东南西北排列`` () =
        Assert.Equal<Kaze list>([ Ton; Nan; Shaa; Pei ], Kaze.all)
        Assert.Equal<int list>([ 0; 1; 2; 3 ], Kaze.all |> List.map Kaze.index)

    [<Fact>]
    let ``风的 mjai 记法是 1z 到 4z`` () =
        Assert.Equal<string list>([ "1z"; "2z"; "3z"; "4z" ], Kaze.all |> List.map Kaze.toMjai)

    [<Fact>]
    let ``风的记法就是对应的风牌`` () =
        for kaze in Kaze.all do
            match Tile.parse (Kaze.toMjai kaze) with
            | Error error -> failwith $"{Kaze.toMjai kaze} 应当是合法牌记法，却得到 {error}"
            | Ok tile ->
                Assert.Equal(Jihai, Tile.suit tile)
                Assert.Equal(Kaze.index kaze + 1, Tile.number tile)

    [<Fact>]
    let ``记法解析与打印互逆`` () =
        for kaze in Kaze.all do
            Assert.Equal<Kaze option>(Some kaze, Kaze.parse (Kaze.toMjai kaze))

    [<Fact>]
    let ``不是风牌的记法解析失败`` () =
        for notation in [ "5z"; "7z"; "1m"; "0z"; ""; "E" ] do
            Assert.Equal<Kaze option>(None, Kaze.parse notation)

    [<Fact>]
    let ``序号与风互逆且越界返回 None`` () =
        for kaze in Kaze.all do
            Assert.Equal<Kaze option>(Some kaze, Kaze.ofIndex (Kaze.index kaze))

        Assert.Equal<Kaze option>(None, Kaze.ofIndex 4)
        Assert.Equal<Kaze option>(None, Kaze.ofIndex -1)

    [<Fact>]
    let ``风编码为记法字符串且往返不变`` () =
        Assert.Equal("\"1z\"", Encode.toString 0 (Kaze.encoder Ton))

        for kaze in Kaze.all do
            Assert.Equal<Result<Kaze, string>>(
                Ok kaze,
                Decode.fromString Kaze.decoder (Encode.toString 0 (Kaze.encoder kaze))
            )

    [<Fact>]
    let ``非风牌字符串解码为错误值`` () =
        match Decode.fromString Kaze.decoder "\"5z\"" with
        | Ok kaze -> failwith $"5z 不该解码成功，却得到 {Kaze.toMjai kaze}"
        | Error _ -> ()

    [<Fact>]
    let ``风的中文渲染`` () =
        Assert.Equal<string list>([ "东"; "南"; "西"; "北" ], Kaze.all |> List.map Kaze.toDisplay)
