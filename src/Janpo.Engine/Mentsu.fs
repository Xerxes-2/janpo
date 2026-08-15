namespace Janpo

/// 面子的种类。杠子的「暗不暗」不在这里，是 `Mentsu` 的另一维。
type MentsuKind =
    /// 顺子：同花色三张连号。
    | Shuntsu
    /// 刻子：三张同牌。
    | Koutsu
    /// 杠子：四张同牌。
    | Kantsu

/// 面子：和了形的一块。
///
/// **`Concealed` 是役与符的关键一位**：门清手里自己凑成的面子与暗杠为暗，
/// 副露（碰 / 吃 / 大明杠 / 加杠）为明；**荣和补上的那个刻子按明刻算**——
/// 三暗刻、四暗刻与符都读这一位，因此它由 `MentsuBreakdown` 在分解时就定死。
///
/// 代表牌一律去红：红宝牌只影响宝牌番数，不影响面子的构成。
type Mentsu =
    private
        {
            Kind: MentsuKind
            /// 代表牌：顺子取最小的一张，刻子与杠子就是那一张。
            Tile: Tile
            Concealed: bool
        }

/// 面子的构造与拆解。
[<RequireQualifiedAccess>]
module Mentsu =

    // ---- 构造 ----

    /// 顺子。`lowest` 是三张里最小的那张，只能是数牌 1-7；其余返回 None。
    let shuntsu (concealed: bool) (lowest: Tile) : Mentsu option =
        if Tile.suit lowest = Jihai || Tile.number lowest > 7 then
            None
        else
            Some
                {
                    Kind = Shuntsu
                    Tile = Tile.deaka lowest
                    Concealed = concealed
                }

    /// 刻子。
    let koutsu (concealed: bool) (tile: Tile) : Mentsu =
        {
            Kind = Koutsu
            Tile = Tile.deaka tile
            Concealed = concealed
        }

    /// 杠子。暗杠 `concealed = true`，大明杠与加杠为 false。
    let kantsu (concealed: bool) (tile: Tile) : Mentsu =
        {
            Kind = Kantsu
            Tile = Tile.deaka tile
            Concealed = concealed
        }

    /// 改写「暗不暗」。荣和补上的刻子要在分解时改成明刻，用的就是它。
    let withConcealed (concealed: bool) (mentsu: Mentsu) : Mentsu = { mentsu with Concealed = concealed }

    // ---- 拆解 ----

    /// 面子的种类。
    let kind (mentsu: Mentsu) : MentsuKind = mentsu.Kind

    /// 代表牌：顺子取最小的一张，刻子与杠子就是那一张。一律已去红。
    let tile (mentsu: Mentsu) : Tile = mentsu.Tile

    /// 是不是暗的（含暗杠；荣和补上的刻子不是）。
    let isConcealed (mentsu: Mentsu) : bool = mentsu.Concealed

    /// 面子的牌，顺子 3 张、刻子 3 张、杠子 4 张，去红后升序。
    let tiles (mentsu: Mentsu) : Tile list =
        match mentsu.Kind with
        | Shuntsu ->
            let suit = Tile.suit mentsu.Tile
            let number = Tile.number mentsu.Tile

            [
                for offset in 0..2 do
                    match Tile.tryCreate suit (number + offset) false with
                    | Some tile -> tile
                    | None -> ()
            ]
        | Koutsu -> List.replicate 3 mentsu.Tile
        | Kantsu -> List.replicate 4 mentsu.Tile

    /// 这个面子里有没有这个牌种（去红后比较）。
    let contains (tile: Tile) (mentsu: Mentsu) : bool =
        tiles mentsu |> List.contains (Tile.deaka tile)

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (mentsu: Mentsu) : string =
        let body = tiles mentsu |> List.map Tile.toDisplay |> String.concat ""
        if mentsu.Concealed then body else $"{body}（明）"
