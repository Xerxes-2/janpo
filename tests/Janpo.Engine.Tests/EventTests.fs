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
        Assert.Equal("""{"type":"tsumo","actor":2,"pai":"5mr"}""", encode (Tsumo(seat 2, tile "5mr")))

    [<Fact>]
    let ``start_kyoku 编码为 mjai 事件对象`` () =
        let event =
            StartKyoku
                {
                    Bakaze = Nan
                    Kyoku = 3
                    Honba = 2
                    Kyotaku = 1
                    Oya = seat 2
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
        Assert.Equal(
            """{"type":"dahai","actor":1,"pai":"7s","tsumogiri":true}""",
            encode (Dahai(seat 1, tile "7s", true))
        )

        Assert.Equal(
            """{"type":"dahai","actor":3,"pai":"5pr","tsumogiri":false}""",
            encode (Dahai(seat 3, tile "5pr", false))
        )

    [<Fact>]
    let ``pon 与 chi 编码为 mjai 事件对象，consumed 是自家亮出的那两张`` () =
        // F# 这侧的 case 名与 wire 同拼（术语表里就叫 Pon / Chi），红宝牌在 consumed 里原样保留。
        Assert.Equal(
            """{"type":"pon","actor":1,"target":0,"pai":"5s","consumed":["5s","5s"]}""",
            encode (Pon(seat 1, seat 0, tile "5s", [ tile "5s"; tile "5s" ]))
        )

        Assert.Equal(
            """{"type":"chi","actor":2,"target":1,"pai":"4p","consumed":["5pr","6p"]}""",
            encode (Chi(seat 2, seat 1, tile "4p", [ tile "5pr"; tile "6p" ]))
        )

    /// 三种杠在 mjai 里是**三个分立的事件**（没有统一的 `kan`），字段也各不相同：
    /// `ankan` 没有 `pai` 与 `target`，`kakan` 没有 `target`。
    /// 这三个形状是在真实牌谱（天凤鳳凰卓）里逐字实测过的，13 票对拍要对上它们。
    [<Fact>]
    let ``ankan 、 kakan 与 daiminkan 是三个分立的 mjai 事件`` () =
        Assert.Equal(
            """{"type":"ankan","actor":3,"consumed":["9s","9s","9s","9s"]}""",
            encode (Ankan(seat 3, [ tile "9s"; tile "9s"; tile "9s"; tile "9s" ]))
        )

        Assert.Equal(
            """{"type":"kakan","actor":1,"pai":"7z","consumed":["7z","7z","7z"]}""",
            encode (Kakan(seat 1, tile "7z", [ tile "7z"; tile "7z"; tile "7z" ]))
        )

        // 标识符按术语表拼作 `Minkan`，wire 上是 mjai 的 `daiminkan`（裁决 D-1）。
        Assert.Equal(
            """{"type":"daiminkan","actor":3,"target":0,"pai":"5s","consumed":["5s","5s","5sr"]}""",
            encode (Minkan(seat 3, seat 0, tile "5s", [ tile "5s"; tile "5s"; tile "5sr" ]))
        )

    /// 新宝牌是**独立的一条事件**，不挂在杠的事件上。
    [<Fact>]
    let ``dora 编码为 mjai 事件对象`` () =
        Assert.Equal("""{"type":"dora","dora_marker":"2s"}""", encode (Dora(tile "2s")))

    [<Fact>]
    let ``pon 与 chi 能从 mjai wire 解回来`` () =
        Assert.Equal<Result<Event, string>>(
            Ok(Pon(seat 1, seat 0, tile "5s", [ tile "5s"; tile "5s" ])),
            decode """{"type":"pon","actor":1,"target":0,"pai":"5s","consumed":["5s","5s"]}"""
        )

        Assert.Equal<Result<Event, string>>(
            Ok(Chi(seat 2, seat 1, tile "4p", [ tile "5pr"; tile "6p" ])),
            decode """{"type":"chi","actor":2,"target":1,"pai":"4p","consumed":["5pr","6p"]}"""
        )

    [<Fact>]
    let ``hora 编码为 mjai 事件对象，符与番与和了点是独立字段`` () =
        let event =
            Hora
                {
                    Actor = seat 2
                    Target = seat 0
                    Pai = tile "4p"
                    Fu = 0
                    Fan = 0
                    HoraPoints = 0
                    Deltas = [ 0; 0; 0; 0 ]
                    Scores = [ 25000; 25000; 25000; 25000 ]
                    UraDoraMarkers = []
                }

        let expected =
            """{"type":"hora","actor":2,"target":0,"pai":"4p","fu":0,"fan":0,"hora_points":0,"""
            + """"deltas":[0,0,0,0],"scores":[25000,25000,25000,25000],"uradora_markers":[]}"""

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
    let ``end_kyoku 与 end_game 编码为只带 type 的 mjai 事件对象`` () =
        Assert.Equal("""{"type":"end_kyoku"}""", encode EndKyoku)
        Assert.Equal("""{"type":"end_game"}""", encode EndGame)

        Assert.Equal<Result<Event, string>>(Ok EndKyoku, decode """{"type":"end_kyoku"}""")
        Assert.Equal<Result<Event, string>>(Ok EndGame, decode """{"type":"end_game"}""")

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
                Tsumo(seat 0, tile "1z")
                StartKyoku
                    {
                        Bakaze = Ton
                        Kyoku = 1
                        Honba = 0
                        Kyotaku = 0
                        Oya = seat 0
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
