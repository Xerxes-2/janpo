namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Newtonsoft
open Janpo

/// 事件的 mjai wire 形态：事件名与字段名必须与 mjai 生态一致，且是紧凑的一行 JSON。
/// 往返不变见 EventProperties。
module EventTests =

    let private encode (event: Event) = Encode.toString 0 (Event.encoder event)

    let private tile (notation: string) =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"记法 {notation} 应当合法，却得到 {error}"

    let private decode (json: string) = Decode.fromString Event.decoder json

    [<Fact>]
    let ``start_game 编码为 mjai 事件对象`` () =
        Assert.Equal(
            """{"type":"start_game","names":["p0","p1","p2","p3"]}""",
            encode (StartGame [ "p0"; "p1"; "p2"; "p3" ])
        )

    [<Fact>]
    let ``tsumo 编码为 mjai 事件对象`` () =
        Assert.Equal("""{"type":"tsumo","actor":2,"pai":"5mr"}""", encode (Tsumo(2, tile "5mr")))

    [<Fact>]
    let ``start_kyoku 编码为 mjai 事件对象`` () =
        let event =
            StartKyoku
                {
                    Bakaze = Nan
                    Kyoku = 3
                    Honba = 2
                    Kyotaku = 1
                    Oya = 2
                    DoraMarker = tile "3s"
                    Scores = [ 25000; 24000; 26000; 25000 ]
                    Tehais =
                        [
                            [ tile "1m"; tile "2m" ]
                            [ tile "3m"; tile "4m" ]
                            [ tile "5m"; tile "6m" ]
                            [ tile "7m"; tile "8m" ]
                        ]
                }

        let expected =
            """{"type":"start_kyoku","bakaze":"2z","dora_marker":"3s","kyoku":3,"honba":2,"kyotaku":1,"""
            + """"oya":2,"scores":[25000,24000,26000,25000],"""
            + """"tehais":[["1m","2m"],["3m","4m"],["5m","6m"],["7m","8m"]]}"""

        Assert.Equal(expected, encode event)

    [<Fact>]
    let ``事件编码成一行，不含换行`` () =
        let events =
            [
                StartGame [ "p0"; "p1"; "p2"; "p3" ]
                Tsumo(0, tile "1z")
                StartKyoku
                    {
                        Bakaze = Ton
                        Kyoku = 1
                        Honba = 0
                        Kyotaku = 0
                        Oya = 0
                        DoraMarker = tile "1p"
                        Scores = [ 25000; 25000; 25000; 25000 ]
                        Tehais = [ []; []; []; [] ]
                    }
            ]

        for event in events do
            Assert.DoesNotContain("\n", encode event)

    [<Fact>]
    let ``未知的事件类型解码为错误值`` () =
        match decode """{"type":"nukidora","actor":0,"pai":"4z"}""" with
        | Ok event -> failwith $"未知事件不该解码成功，却得到 {event}"
        | Error _ -> ()

    [<Fact>]
    let ``缺字段的事件解码为错误值`` () =
        match decode """{"type":"tsumo","actor":0}""" with
        | Ok event -> failwith $"缺 pai 的 tsumo 不该解码成功，却得到 {event}"
        | Error _ -> ()

    [<Fact>]
    let ``字段里的非法牌记法解码为错误值`` () =
        match decode """{"type":"tsumo","actor":0,"pai":"8z"}""" with
        | Ok event -> failwith $"8z 不该解码成功，却得到 {event}"
        | Error _ -> ()

    [<Fact>]
    let ``mjai 原生的 E 记法不被接受`` () =
        match decode """{"type":"tsumo","actor":0,"pai":"E"}""" with
        | Ok event -> failwith $"ADR-0001 已否决 E/S/W/N 记法，却解码出了 {event}"
        | Error _ -> ()
