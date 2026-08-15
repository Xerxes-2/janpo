namespace Janpo

/// 座位：0 起的固定索引，一场对局内不变（CONTEXT.md）。
/// mjai wire 上的 `actor` / `target` / `oya` 就是这个数，因此这里是个透明别名而不是包装类型。
/// 座位数是规则集的一项，不写死 4。
type Seat = int

/// 座位的枚举与相对关系。所有函数都从规则集读座位数。
[<RequireQualifiedAccess>]
module Seat =

    // ---- 构造 ----

    /// 全部座位，升序。
    let all (ruleset: Ruleset) : Seat list = [ 0 .. ruleset.SeatCount - 1 ]

    /// 从某座位起、按下家方向绕一圈的座位序列。配牌与摸打顺序都从这里取。
    let orderFrom (ruleset: Ruleset) (first: Seat) : Seat list =
        if ruleset.SeatCount <= 0 then
            []
        else
            all ruleset |> List.map (fun offset -> (first + offset) % ruleset.SeatCount)

    // ---- 拆解 ----

    /// 是否是这个规则集下的合法座位。
    let isValid (ruleset: Ruleset) (seat: Seat) : bool = seat >= 0 && seat < ruleset.SeatCount

    /// 自风：由座位与亲推出——亲是东，下家是南，依次类推（CONTEXT.md：
    /// 自风不是座位本身的属性）。座位数非正或超过四风时退回东（不抛异常）。
    let jikaze (ruleset: Ruleset) (oya: Seat) (seat: Seat) : Kaze =
        if ruleset.SeatCount <= 0 then
            Ton
        else
            let offset =
                ((seat - oya) % ruleset.SeatCount + ruleset.SeatCount) % ruleset.SeatCount

            Kaze.ofIndex offset |> Option.defaultValue Ton

    /// 下家：摸打推进的方向。座位数非正时原样返回（规则集不自洽时不抛异常）。
    let next (ruleset: Ruleset) (seat: Seat) : Seat =
        if ruleset.SeatCount <= 0 then
            seat
        else
            (seat + 1) % ruleset.SeatCount
