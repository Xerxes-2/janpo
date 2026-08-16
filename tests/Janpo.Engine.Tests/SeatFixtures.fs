namespace Janpo.Engine.Tests

open Janpo

/// 测试固件里的座位字面量。
///
/// `Seat` 是真类型不是 `int` 别名（晨间裁决 R-5），因此用例里写不出裸的 `2`。
/// 引擎留给测试的快构造是 `internal` 的（经 `InternalsVisibleTo`，与 `Wall.ofOrdered`
/// 同一道口子），这里只给它一个短名字——`seat 2` 比 `Seat.ofIndexUnchecked 2` 好读，
/// 而「座位不是 int」那条约束一分不减：**它仍然只在测试里存在**。
[<AutoOpen>]
module SeatFixtures =

    /// 0 起的座位字面量。越界与否由被测代码判（`Seat.isValid`），这里不拦。
    let seat (index: int) : Seat = Seat.ofIndexUnchecked index

    /// `[ 0; 1; 2; 3 ]` 这种「按座位升序的期望值」写起来最顺的形式。
    let seats (indexes: int list) : Seat list = indexes |> List.map seat
