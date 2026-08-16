namespace Janpo

/// 开一局需要由上层给定的既定条件，也就是 mjai `start_kyoku` 里引擎自己算不出来的那几项：
/// 场风、局数、本场、供托、亲与各家点数。它们的推进（连庄、进局、供托结转）是后续票的事。
type KyokuContext =
    {
        /// 场风。
        Bakaze: Kaze
        /// 局数，1 起。
        Kyoku: int
        /// 本场。
        Honba: int
        /// 供托：场上堆着的立直棒数。
        Kyotaku: int
        /// 亲。
        Oya: Seat
        /// 各家点数，按座位升序。
        Scores: int list
    }

/// 开局的产物：mjai 事件流，以及事件所对应的牌山与手牌。
type KyokuStart =
    {
        /// mjai 事件流，按产出顺序：`start_kyoku`，随后 Oya 的 `tsumo`。
        Events: Event list
        /// Oya 摸完第一张之后的牌山。
        Wall: Wall
        /// 各家手牌，按座位升序，每手已按 mjai 顺序排序。
        /// Oya 的那手**已含**摸进的第一张（14 张），其余各家 13 张。
        Hands: Tile list list
        /// Oya 摸进的第一张，也就是 `Events` 里那条 `tsumo` 的 `pai`。
        /// 摸切判定要知道「刚摸进的是哪张」，手牌已排序反推不出来。
        Tsumo: Tile
    }

/// 开不了局的原因。是值，不是异常（与 IllegalAction 同一方针）。
/// 只有规则集或上层给的条件不自洽时才会出现——预设规则集不可能触发。
type KyokuStartError =
    /// 可摸区发不出配牌与 Oya 的第一张：需要 `required` 张，只有 `available` 张。
    | LiveWallTooSmall of required: int * available: int
    /// 王牌里没有一叠完整的指示牌，翻不出开局的表宝牌指示牌。
    | NoDoraIndicator of deadWallSize: int
    /// 点数列表的长度与座位数不符。
    | ScoreCountMismatch of expected: int * actual: int
    /// 亲不是这个规则集下的合法座位。
    | OyaOutOfRange of oya: Seat * seatCount: int

/// 开局：构成牌山 → 洗牌 → 留王牌与首张宝牌指示牌 → 配牌 → Oya 摸第一张，
/// 并把这一串产出为 mjai 事件。
[<RequireQualifiedAccess>]
module KyokuStart =

    // ---- 构造 ----

    /// 从一座**已经摊好的**牌山开局。`create` 洗完牌就调它，因此发牌手顺与开局事件只有这一份。
    /// 库外拿不到（`internal`）：它是黄金用例摊牌山的入口（配 `Wall.ofOrdered`），
    /// 生产代码只能经 `create` 用种子开局。
    let internal createFrom
        (ruleset: Ruleset)
        (context: KyokuContext)
        (wall: Wall)
        : Result<KyokuStart, KyokuStartError> =
        if not (Seat.isValid ruleset context.Oya) then
            Error(OyaOutOfRange(context.Oya, ruleset.SeatCount))
        elif List.length context.Scores <> ruleset.SeatCount then
            Error(ScoreCountMismatch(ruleset.SeatCount, List.length context.Scores))
        else
            let required = Ruleset.haipaiTotal ruleset + 1

            match Wall.deal ruleset context.Oya wall with
            | None -> Error(LiveWallTooSmall(required, Wall.remaining wall))
            | Some(tehais, dealt) ->
                match Wall.draw dealt, Wall.doraIndicators dealt with
                | None, _ -> Error(LiveWallTooSmall(required, Wall.remaining wall))
                | _, [] -> Error(NoDoraIndicator(List.length (Wall.deadWall dealt)))
                | Some(drawn, afterTsumo), doraMarker :: _ ->
                    let startKyoku =
                        Event.StartKyoku
                            {
                                Bakaze = context.Bakaze
                                Kyoku = context.Kyoku
                                Honba = context.Honba
                                Kyotaku = context.Kyotaku
                                Oya = context.Oya
                                DoraMarker = doraMarker
                                Scores = context.Scores
                                Tehais = tehais
                            }

                    let hands = tehais |> Seat.mapAt context.Oya (fun hand -> Tile.sort (drawn :: hand))

                    Ok
                        {
                            Events = [ startKyoku; Event.Tsumo(context.Oya, drawn) ]
                            Wall = afterTsumo
                            Hands = hands
                            Tsumo = drawn
                        }

    /// 同一种子、同一规则集、同一条件必然开出同一局：牌山、配牌与事件流逐字节相同。
    let create (ruleset: Ruleset) (context: KyokuContext) (rng: Rng) : Result<KyokuStart * Rng, KyokuStartError> =
        let wall, advanced = Wall.build ruleset rng
        createFrom ruleset context wall |> Result.map (fun started -> started, advanced)

/// 开局条件的构造。
[<RequireQualifiedAccess>]
module KyokuContext =

    /// 一场对局的第一局：东 1 局 0 本场，供托为空，Oya 是座位 0，各家起手点数取自规则集。
    let initial (ruleset: Ruleset) : KyokuContext =
        {
            Bakaze = Ton
            Kyoku = 1
            Honba = 0
            Kyotaku = 0
            Oya = Seat.first
            Scores = Seat.all ruleset |> List.map (fun _ -> ruleset.StartingScore)
        }

/// 开局失败原因的渲染。
[<RequireQualifiedAccess>]
module KyokuStartError =

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: KyokuStartError) : string =
        match error with
        | LiveWallTooSmall(required, available) -> $"牌山发不出配牌：需要 {required} 张，可摸区只有 {available} 张"
        | NoDoraIndicator deadWallSize -> $"王牌只有 {deadWallSize} 张，凑不出一叠宝牌指示牌"
        | ScoreCountMismatch(expected, actual) -> $"点数列表应有 {expected} 项（座位数），实际 {actual} 项"
        | OyaOutOfRange(oya, seatCount) -> $"亲的座位 {Seat.index oya} 不合法，座位只有 0-{seatCount - 1}"
