namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Newtonsoft
open Janpo

/// 座位的构造、枚举与相对关系。座位数一律从规则集读，测试里也不写死 4。
///
/// **座位算术只有 `Seat` 一处实现**（晨间裁决 R-5），因此这里也是那些算术唯一被直接测的地方。
module SeatTests =

    /// 三麻本版不做，但座位数是规则集的一项——这里只用它证明门没焊死。
    let private threeSeats = { Ruleset.yonma with SeatCount = 3 }

    // ---- 构造 ----

    [<Fact>]
    let ``裸整数造座位：负数不是座位，上界不在这里判`` () =
        Assert.Equal<Seat option>(Some(seat 0), Seat.ofIndex 0)
        Assert.Equal<Seat option>(Some(seat 3), Seat.ofIndex 3)
        // 上界要规则集，构造处判不了：4 造得出来，但 `isValid` 说它不合法。
        Assert.Equal<Seat option>(Some(seat 4), Seat.ofIndex 4)
        Assert.Equal<Seat option>(None, Seat.ofIndex -1)

    [<Fact>]
    let ``起家是座位 0`` () =
        Assert.Equal(seat 0, Seat.first)
        Assert.Equal(0, Seat.index Seat.first)

    [<Fact>]
    let ``全部座位按升序枚举`` () =
        Assert.Equal<Seat list>(seats [ 0; 1; 2; 3 ], Seat.all Ruleset.yonma)
        Assert.Equal<Seat list>(seats [ 0; 1; 2 ], Seat.all threeSeats)

    // ---- 相对位置 ----

    [<Fact>]
    let ``下家绕回第一个座位`` () =
        Assert.Equal<Seat list>(seats [ 1; 2; 3; 0 ], Seat.all Ruleset.yonma |> List.map (Seat.shimocha Ruleset.yonma))

        Assert.Equal<Seat list>(seats [ 1; 2; 0 ], Seat.all threeSeats |> List.map (Seat.shimocha threeSeats))

    [<Fact>]
    let ``上家是下家的反方向，绕回最后一个座位`` () =
        Assert.Equal<Seat list>(seats [ 3; 0; 1; 2 ], Seat.all Ruleset.yonma |> List.map (Seat.kamicha Ruleset.yonma))

        Assert.Equal<Seat list>(seats [ 2; 0; 1 ], Seat.all threeSeats |> List.map (Seat.kamicha threeSeats))

    [<Fact>]
    let ``只有四家才有对家`` () =
        Assert.Equal<Seat option list>(
            [ Some(seat 2); Some(seat 3); Some(seat 0); Some(seat 1) ],
            Seat.all Ruleset.yonma |> List.map (Seat.toimen Ruleset.yonma)
        )

        Assert.Equal<Seat option list>([ None; None; None ], Seat.all threeSeats |> List.map (Seat.toimen threeSeats))

    [<Fact>]
    let ``相对位置：0 是自己，1 是下家，绕一圈`` () =
        Assert.Equal<int list>(
            [ 0; 1; 2; 3 ],
            Seat.all Ruleset.yonma |> List.map (Seat.distanceFrom Ruleset.yonma Seat.first)
        )

        Assert.Equal<int list>(
            [ 2; 3; 0; 1 ],
            Seat.all Ruleset.yonma |> List.map (Seat.distanceFrom Ruleset.yonma (seat 2))
        )

        Assert.Equal<int list>([ 0; 1; 2 ], Seat.all threeSeats |> List.map (Seat.distanceFrom threeSeats Seat.first))

    [<Fact>]
    let ``从某座位起按下家方向绕一圈`` () =
        Assert.Equal<Seat list>(seats [ 2; 3; 0; 1 ], Seat.orderFrom Ruleset.yonma (seat 2))
        Assert.Equal<Seat list>(seats [ 0; 1; 2; 3 ], Seat.orderFrom Ruleset.yonma Seat.first)
        Assert.Equal<Seat list>(seats [ 1; 2; 0 ], Seat.orderFrom threeSeats (seat 1))

    [<Fact>]
    let ``打牌者之后的裁决顺序：下家优先，打牌者自己排最后`` () =
        Assert.Equal<Seat list>(seats [ 1; 2; 3; 0 ], Seat.orderAfter Ruleset.yonma Seat.first)
        Assert.Equal<Seat list>(seats [ 3; 0; 1; 2 ], Seat.orderAfter Ruleset.yonma (seat 2))
        Assert.Equal<Seat list>(seats [ 2; 0; 1 ], Seat.orderAfter threeSeats (seat 1))

    [<Fact>]
    let ``自风由座位与亲推出`` () =
        Assert.Equal<Kaze list>(
            [ Ton; Nan; Shaa; Pei ],
            Seat.all Ruleset.yonma |> List.map (Seat.jikaze Ruleset.yonma Seat.first)
        )

        Assert.Equal<Kaze list>(
            [ Shaa; Pei; Ton; Nan ],
            Seat.all Ruleset.yonma |> List.map (Seat.jikaze Ruleset.yonma (seat 2))
        )

    // ---- 拆解 ----

    [<Fact>]
    let ``越界的座位不合法`` () =
        Assert.True(Seat.isValid Ruleset.yonma (seat 0))
        Assert.True(Seat.isValid Ruleset.yonma (seat 3))
        Assert.False(Seat.isValid Ruleset.yonma (seat 4))
        Assert.False(Seat.isValid threeSeats (seat 3))

    [<Fact>]
    let ``按座位存取「每家一项」的列表`` () =
        let scores = [ 25000; 24000; 26000; 25000 ]

        Assert.Equal(Some 26000, Seat.tryItem (seat 2) scores)
        Assert.Equal<int option>(None, Seat.tryItem (seat 4) scores)

        Assert.Equal<int list>([ 25000; 24000; 27000; 25000 ], Seat.mapAt (seat 2) ((+) 1000) scores)
        // 越界时整列原样返回。
        Assert.Equal<int list>(scores, Seat.mapAt (seat 4) ((+) 1000) scores)

        Assert.Equal<(Seat * int) list>(
            [ seat 0, 25000; seat 1, 24000; seat 2, 26000; seat 3, 25000 ],
            Seat.indexed scores
        )

    // ---- mjai wire ----

    [<Fact>]
    let ``wire 上座位是裸整数`` () =
        Assert.Equal("2", Encode.toString 0 (Seat.encoder (seat 2)))

        Assert.Equal<Result<Seat, string>>(Ok(seat 2), Decode.fromString Seat.decoder "2")

        Assert.True(
            match Decode.fromString Seat.decoder "-1" with
            | Error _ -> true
            | Ok _ -> false
        )
