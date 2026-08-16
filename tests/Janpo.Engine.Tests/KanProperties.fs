namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 杠的不变量。**最值钱的三条是牌山那三条**：杠要从王牌补摸一张、同时把可摸区的最后一张
/// 补进王牌，因此「王牌恒 14 张、可摸区每杠少一张、总牌数守恒」三者必须同时成立——
/// 少一条就意味着有牌凭空多出来或者蒸发了。具名用例见 KanTests。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries>; typeof<ScoreArbitraries> |])>]
module KanProperties =

    let private engine = Ruleset.yonma

    /// 一局里成立过几个杠：`ankan` / `kakan` / `daiminkan` 三种事件的条数。
    /// 引擎的 `GameState.kanCount` 数的是副露，这里从事件流重新数一遍——两条路径对上才算数对。
    let private kanEvents (state: GameState) : int =
        GameState.events state
        |> List.filter (fun event ->
            match event with
            | Ankan _
            | Minkan _ -> true
            // 加杠不新增一组副露，它是把原来那组碰换成杠：数副露的那份也只算一个。
            | Kakan _ -> true
            | StartGame _
            | StartKyoku _
            | Tsumo _
            | Dahai _
            | Pon _
            | Chi _
            | Dora _
            | Riichi _
            | RiichiAccepted _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | EndGame -> false)
        |> List.length

    /// 一局里翻开的新宝牌条数。
    let private doraEvents (state: GameState) : int =
        GameState.events state
        |> List.filter (fun event ->
            match event with
            | Dora _ -> true
            | StartGame _
            | StartKyoku _
            | Tsumo _
            | Dahai _
            | Pon _
            | Chi _
            | Ankan _
            | Kakan _
            | Minkan _
            | Riichi _
            | RiichiAccepted _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | EndGame -> false)
        |> List.length

    // ---- 牌山 ----

    [<Property>]
    let ``王牌恒是规则集给的那么多张：杠取走一张岭上牌，可摸区就补进一张`` (state: GameState) =
        List.length (Wall.deadWall (GameState.wall state)) = engine.DeadWallSize

    [<Property>]
    let ``可摸区每杠少一张：剩余张数恒等于「开局可摸张数 − 已摸 − 杠数」`` (state: GameState) =
        let wall = GameState.wall state

        let opening =
            Ruleset.wallSize engine - engine.DeadWallSize - Ruleset.haipaiTotal engine

        // 每条 `tsumo` 摸掉一张，但**岭上那几张来自王牌**：它们各自把可摸区的最后一张顶进王牌，
        // 因此可摸区少掉的总数 = 全部自摸条数（岭上的那几次摸的不是可摸区，却各顶走一张）。
        let tsumos =
            GameState.events state
            |> List.filter (fun event ->
                match event with
                | Tsumo _ -> true
                | StartGame _
                | StartKyoku _
                | Dahai _
                | Pon _
                | Chi _
                | Ankan _
                | Kakan _
                | Minkan _
                | Dora _
                | Riichi _
                | RiichiAccepted _
                | Hora _
                | Ryuukyoku _
                | EndKyoku
                | EndGame -> false)
            |> List.length

        Wall.remaining wall = opening - tsumos

    [<Property>]
    let ``杠数与新宝牌：每个杠翻一张，翻开的指示牌恒是「1 + 杠数」`` (state: GameState) =
        let kans = kanEvents state
        let wall = GameState.wall state

        kans = GameState.kanCount state
        && doraEvents state = kans
        && List.length (Wall.doraIndicators wall) = 1 + kans
        // 里宝牌与表宝牌成叠翻开：杠里宝牌的张数与表的一样多。
        && List.length (Wall.uraIndicators wall) = List.length (Wall.doraIndicators wall)
        // 岭上牌有几张就最多杠几次。
        && kans <= engine.RinshanCount

    [<Property>]
    let ``杠后牌数守恒：一组杠仍旧只折三张暗牌`` (state: GameState) =
        // 杠比碰多吃一张手牌，但它随即从王牌补摸一张回来，因此「暗牌 + 3 × 副露数」不变。
        GameState.players state
        |> List.forall (fun player ->
            let held = List.length (PlayerState.hand player) + 3 * PlayerState.nakiCount player

            held = engine.HaipaiSize || held = engine.HaipaiSize + 1)

    [<Property>]
    let ``杠的副露里牌种唯一、四张齐全`` (state: GameState) =
        GameState.players state
        |> List.collect PlayerState.naki
        |> List.filter Naki.isKan
        |> List.forall (fun naki ->
            let tiles = Naki.tiles naki

            List.length tiles = 4
            && tiles |> List.map Tile.kindIndex |> List.distinct |> List.length = 1
            // 暗杠没有来源，明杠与加杠都记着来源座位（责任支付要它）。
            && (Naki.kind naki = NakiKind.Ankan) = Option.isNone (Naki.target naki))

    // ---- 动作集 ----

    [<Property>]
    let ``摸牌后阶段的杠只在自己摸完牌那一手出现，且杠数没到上限`` (state: GameState) =
        match GameState.phase state with
        | AwaitingDahai phase ->
            phase.Actions
            |> List.forall (fun action ->
                match action with
                | Action.Ankan(actor, consumed) ->
                    actor = phase.Actor
                    && Option.isSome phase.Tsumo
                    && List.length consumed = 4
                    && GameState.kanCount state < engine.RinshanCount
                | Action.Kakan(actor, _, consumed) ->
                    actor = phase.Actor
                    && Option.isSome phase.Tsumo
                    && List.length consumed = 3
                    && GameState.kanCount state < engine.RinshanCount
                // 大明杠是响应阶段的动作，摸牌后阶段不该有。
                | Action.Minkan _ -> false
                | Action.Dahai _
                | Action.Hora _
                | Action.Pon _
                | Action.Chi _
                | Action.Riichi _
                | Action.None _ -> true)
        | AwaitingResponse _
        | Ended _ -> true

    [<Property>]
    let ``抢杠那一轮只有荣和与「过」，宣言杠的那家不在被问之列`` (state: GameState) =
        match GameState.phase state with
        | AwaitingResponse phase ->
            match phase.Cause with
            | ResponseCause.Dahai -> true
            | ResponseCause.Kan kan ->
                Naki.isKan kan
                && phase.Responses
                   |> List.forall (fun choice ->
                       choice.Seat <> phase.Target
                       && choice.Actions
                          |> List.forall (fun action ->
                              match action with
                              | Action.Hora(_, target, pai) -> target = phase.Target && pai = phase.Pai
                              | Action.None _ -> true
                              | Action.Dahai _
                              | Action.Pon _
                              | Action.Chi _
                              | Action.Ankan _
                              | Action.Kakan _
                              | Action.Minkan _
                              | Action.Riichi _ -> false))
        | AwaitingDahai _
        | Ended _ -> true

    // ---- 责任支付 ----

    [<Property>]
    let ``包只改谁付、不改和了点：增减之和仍等于收走的供托`` (HoraCase(transfer, value)) (liable: int) =
        let seat = ((liable % engine.SeatCount) + engine.SeatCount) % engine.SeatCount
        let plain = Score.hora engine transfer value

        let packed = Score.hora engine { transfer with Sekinin = Some seat } value

        // 和了点、级别与符番一概不受包影响（`Fu` / `basePoints` / `limit` 都不看它）。
        packed.HoraPoints = plain.HoraPoints
        && packed.Limit = plain.Limit
        && List.sum packed.Deltas = transfer.Kyotaku * engine.RiichiBou
        && List.item transfer.Actor packed.Deltas = List.item transfer.Actor plain.Deltas
        // 付钱的只可能是责任者与放铳者（自摸时放铳者就是和了者自己，因此只剩责任者）。
        // 和了者包不了自己：那种情形当作没包，授受照常。
        && (seat = transfer.Actor
            || packed.Deltas
               |> List.mapi (fun each delta ->
                   each = transfer.Actor || each = seat || each = transfer.Target || delta = 0)
               |> List.forall id)

    [<Property>]
    let ``包在自摸时由责任者一家付光`` (HoraCase(transfer, value)) (liable: int) =
        let seat = ((liable % engine.SeatCount) + engine.SeatCount) % engine.SeatCount

        if transfer.Actor <> transfer.Target || seat = transfer.Actor then
            true
        else
            let packed = Score.hora engine { transfer with Sekinin = Some seat } value

            // 自摸的三家平摊变成责任者一家付：其余两家分文不动。
            packed.Deltas
            |> List.mapi (fun each delta -> each = transfer.Actor || each = seat || delta = 0)
            |> List.forall id
            && -(List.item seat packed.Deltas) >= packed.HoraPoints
