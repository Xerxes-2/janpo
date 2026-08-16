namespace Janpo

open Thoth.Json.Core

/// 座位：0 起的固定索引，一场对局内不变（CONTEXT.md）。
///
/// **不是 `int` 的别名。** 引擎里挨着的同类型标量太多——局数、本场、供托根数、点数、番、符——
/// 透明别名下把它们互相传错编译器一声不吭。02 票给 `StartKyoku` 立记录载荷正是这条理由，
/// 座位该用同一条（晨间裁决 R-5）。
///
/// 内部表示私有：座位只能经 `Seat.ofIndex`（外来的裸整数）、`Seat.all` / `Seat.orderFrom`
/// 这类枚举，或 `Seat.shimocha` 这类相对位置得到。**上界是规则集的事**
/// （`Seat.isValid` 对着 `Ruleset.SeatCount` 判，座位数不写死 4），
/// 类型本身只守住与规则集无关的那条下界：非负。
///
/// **mjai wire 上仍是裸整数**（`actor` / `target` / `oya`）：编解码在 `Seat.encoder` /
/// `Seat.decoder` 一处映射，标识符不迁就 wire（裁决 D-1）。
[<Struct>]
type Seat =
    private
        {
            /// 0 起的固定索引。
            Index: int
        }

/// 座位的构造、枚举、相对位置，以及「每家一项」列表的按座位存取。
///
/// **座位算术只有这一处实现**：别处不许再出现 `(seat + 1) % 4` 这类式子，
/// 也不许拿 `Seat.index` 出去做算术——那等于把 `type Seat = int` 又放回来。
[<RequireQualifiedAccess>]
module Seat =

    // ---- 构造 ----

    /// 快构造，**调用方自己保证 `index` 非负**（枚举出来的、取过模的、已经校验过的 wire）。
    /// 库外拿不到（`internal`，与 `Wall.ofOrdered` 同一道留给测试固件的口子）。
    let internal ofIndexUnchecked (index: int) : Seat = { Index = index }

    /// 外来的裸整数 → 座位：mjai wire 的 `actor` / `target` / `oya`、CLI 入参、牌谱。
    /// 负数不是座位；**上界不在这里判**（那要规则集，见 `isValid`）。
    let ofIndex (index: int) : Seat option =
        if index < 0 then None else Some { Index = index }

    /// 起家：座位 0。一场对局的第一局由它坐亲。
    let first: Seat = { Index = 0 }

    /// 全部座位，升序。
    let all (ruleset: Ruleset) : Seat list =
        [ for index in 0 .. ruleset.SeatCount - 1 -> { Index = index } ]

    // ---- 拆解 ----

    /// 0 起的固定索引。**只在 wire、渲染与外部下标处用**。
    let index (seat: Seat) : int = seat.Index

    /// 是否是这个规则集下的合法座位。下界由构造保证，这里只判上界。
    let isValid (ruleset: Ruleset) (seat: Seat) : bool = seat.Index < ruleset.SeatCount

    // ---- 相对位置 ----

    /// 从 `seat` 起按下家方向数 `offset` 家（`offset` 可负，即逆着数）。
    /// **本模块唯一的座位算术**，下面的相对位置全由它推出。
    /// 座位数非正时原样返回（规则集不自洽时不抛异常）。
    let private shift (ruleset: Ruleset) (offset: int) (seat: Seat) : Seat =
        if ruleset.SeatCount <= 0 then
            seat
        else
            {
                Index = ((seat.Index + offset) % ruleset.SeatCount + ruleset.SeatCount) % ruleset.SeatCount
            }

    /// 下家（Shimocha）：摸打推进的方向，也是进局时亲传下去的方向，
    /// 还是「多家争同一张时谁优先」的排序起点。
    let shimocha (ruleset: Ruleset) (seat: Seat) : Seat = shift ruleset 1 seat

    /// 上家（Kamicha）：**只有它打出的牌能被吃**。
    let kamicha (ruleset: Ruleset) (seat: Seat) : Seat = shift ruleset -1 seat

    /// 对家（Toimen）。**只有四家才有对家**：座位数不是 4 时是 None（三麻没有对家）。
    let toimen (ruleset: Ruleset) (seat: Seat) : Seat option =
        if ruleset.SeatCount = 4 then
            Some(shift ruleset 2 seat)
        else
            None

    /// `other` 相对 `seat` 是第几家：0 = 自己、1 = 下家、2 = 对家（四麻）、3 = 上家。
    /// **「打牌者下家优先」这条裁决顺序就是它**——头跳与多家鸣牌都按它排。
    let distanceFrom (ruleset: Ruleset) (seat: Seat) (other: Seat) : int =
        if ruleset.SeatCount <= 0 then
            0
        else
            let raw = (other.Index - seat.Index) % ruleset.SeatCount
            (raw + ruleset.SeatCount) % ruleset.SeatCount

    /// 把任意整数折进合法座位（负数也折得进）。**这不是 `ofIndex`**：
    /// 外来的裸整数（wire、CLI）该走 `ofIndex` 并在越界时报错，
    /// 这个只在「随便取一家」时用——属性测试的取样，以及轮转。
    let wrap (ruleset: Ruleset) (value: int) : Seat = shift ruleset value first

    /// 从某座位起、按下家方向绕一圈的座位序列（自己排在头）。配牌与摸打顺序都从这里取。
    let orderFrom (ruleset: Ruleset) (origin: Seat) : Seat list =
        [ for offset in 0 .. ruleset.SeatCount - 1 -> shift ruleset offset origin ]

    /// 响应与裁决的顺序：从打牌者的**下家**起绕一圈，打牌者自己排在最后。
    let orderAfter (ruleset: Ruleset) (target: Seat) : Seat list =
        orderFrom ruleset (shimocha ruleset target)

    /// 自风：由座位与亲推出——亲是东，下家是南，依次类推（CONTEXT.md：
    /// 自风不是座位本身的属性）。座位数非正或超过四风时退回东（不抛异常）。
    let jikaze (ruleset: Ruleset) (oya: Seat) (seat: Seat) : Kaze =
        Kaze.ofIndex (distanceFrom ruleset oya seat) |> Option.defaultValue Ton

    // ---- 「每家一项」的列表 ----

    /// 点数、Deltas、配牌、听牌标志、`Players`……凡是「每家一项、按座位升序」的列表
    /// 都走下面三个，别处不写 `List.item (Seat.index seat)`。
    let tryItem (seat: Seat) (items: 'a list) : 'a option = List.tryItem seat.Index items

    /// 只改指定座位那一项，其余原样。座位越界时整列原样返回。
    let mapAt (seat: Seat) (change: 'a -> 'a) (items: 'a list) : 'a list =
        items
        |> List.mapi (fun index each -> if index = seat.Index then change each else each)

    /// 「每家一项」的列表 → `(座位, 那一项)`，按座位升序。
    let indexed (items: 'a list) : (Seat * 'a) list =
        items |> List.mapi (fun index each -> { Index = index }, each)

    // ---- mjai wire ----

    /// mjai wire 上座位是**裸整数**，标识符不迁就 wire、wire 也不迁就标识符（裁决 D-1）。
    let encoder: Encoder<Seat> = fun seat -> Encode.int seat.Index

    let decoder: Decoder<Seat> =
        Decode.int
        |> Decode.andThen (fun index ->
            match ofIndex index with
            | Some seat -> Decode.succeed seat
            | None -> Decode.fail ("not a valid mjai seat: " + string index))
