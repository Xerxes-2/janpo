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
    let ``dahai 编码为 mjai 事件对象`` () =
        Assert.Equal("""{"type":"dahai","actor":1,"pai":"7s","tsumogiri":true}""", encode (Dahai(1, tile "7s", true)))

        Assert.Equal(
            """{"type":"dahai","actor":3,"pai":"5pr","tsumogiri":false}""",
            encode (Dahai(3, tile "5pr", false))
        )

    [<Fact>]
    let ``hora 编码为 mjai 事件对象，符与番与和了点是独立字段`` () =
        let event =
            Hora
                {
                    Actor = 2
                    Target = 0
                    Pai = tile "4p"
                    Fu = 0
                    Fan = 0
                    HoraPoints = 0
                    Deltas = [ 0; 0; 0; 0 ]
                    Scores = [ 25000; 25000; 25000; 25000 ]
                }

        let expected =
            """{"type":"hora","actor":2,"target":0,"pai":"4p","fu":0,"fan":0,"hora_points":0,"""
            + """"deltas":[0,0,0,0],"scores":[25000,25000,25000,25000]}"""

        Assert.Equal(expected, encode event)

    [<Fact>]
    let ``ryukyoku 编码为 mjai 事件对象，荒牌流局的 reason 写作 fanpai`` () =
        let event =
            Ryuukyoku
                {
                    Reason = Fanpai
                    Tenpais = [ true; false; true; false ]
                    Deltas = [ 1500; -1500; 1500; -1500 ]
                    Scores = [ 26500; 23500; 26500; 23500 ]
                }

        let expected =
            """{"type":"ryukyoku","reason":"fanpai","tenpais":[true,false,true,false],"""
            + """"deltas":[1500,-1500,1500,-1500],"scores":[26500,23500,26500,23500]}"""

        Assert.Equal(expected, encode event)

    [<Fact>]
    let ``不认识的流局形态解码为错误值`` () =
        let json =
            """{"type":"ryukyoku","reason":"nagashimangan","tenpais":[true,true,true,true],"""
            + """"deltas":[0,0,0,0],"scores":[25000,25000,25000,25000]}"""

        match decode json with
        | Ok event -> failwith $"12 票才会加流し満貫，现在不该解码成功，却得到 {event}"
        | Error _ -> ()

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
