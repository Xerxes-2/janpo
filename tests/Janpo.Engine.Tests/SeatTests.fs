namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 座位的枚举与相对关系。座位数一律从规则集读，测试里也不写死 4。
module SeatTests =

    /// 三麻本版不做，但座位数是规则集的一项——这里只用它证明门没焊死。
    let private threeSeats = { Ruleset.yonma with SeatCount = 3 }

    [<Fact>]
    let ``全部座位按升序枚举`` () =
        Assert.Equal<Seat list>([ 0; 1; 2; 3 ], Seat.all Ruleset.yonma)
        Assert.Equal<Seat list>([ 0; 1; 2 ], Seat.all threeSeats)

    [<Fact>]
    let ``下家绕回第一个座位`` () =
        Assert.Equal<Seat list>([ 1; 2; 3; 0 ], Seat.all Ruleset.yonma |> List.map (Seat.next Ruleset.yonma))
        Assert.Equal<Seat list>([ 1; 2; 0 ], Seat.all threeSeats |> List.map (Seat.next threeSeats))

    [<Fact>]
    let ``从某座位起按下家方向绕一圈`` () =
        Assert.Equal<Seat list>([ 2; 3; 0; 1 ], Seat.orderFrom Ruleset.yonma 2)
        Assert.Equal<Seat list>([ 0; 1; 2; 3 ], Seat.orderFrom Ruleset.yonma 0)
        Assert.Equal<Seat list>([ 1; 2; 0 ], Seat.orderFrom threeSeats 1)

    [<Fact>]
    let ``越界的座位不合法`` () =
        Assert.True(Seat.isValid Ruleset.yonma 0)
        Assert.True(Seat.isValid Ruleset.yonma 3)
        Assert.False(Seat.isValid Ruleset.yonma 4)
        Assert.False(Seat.isValid Ruleset.yonma -1)
        Assert.False(Seat.isValid threeSeats 3)
