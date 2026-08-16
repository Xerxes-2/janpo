namespace Janpo.Engine.Tests

open System.Text.RegularExpressions
open FsCheck.Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 观测投影的不变量：**看得见的牌一张不多**。局面取自可达局面（随机开一局再随机走若干步），
/// 每条属性对局面里的每个座位各验一遍。
///
/// 「他家暗牌看不见」在**类型层面**已经成立（`MaskedSeat` 没有那个字段），
/// 这里的属性是佐证不是保障：它挡的是往后有人给投影加字段时顺手把暗信息带出去。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |], Parallelism = 4)>]
module ObservationProperties =

    let private observationsOf (state: GameState) : (Seat * Observation) list =
        Seat.all ruleset
        |> List.choose (fun seat -> Observation.ofState seat state |> Option.map (fun each -> seat, each))

    /// 这一刻**在牌桌上亮着**的全部牌，按张数计：自家暗牌（含刚摸进那张）、各家的河、
    /// 各家的副露与已翻开的表宝牌指示牌。牌山、王牌、里宝牌与他家暗牌都不在其中。
    ///
    /// 副露里被鸣的那张同时算在打牌者的河里，刚摸进那张同时算在手牌里——**两边都重复数**，
    /// 因此下面的「子多重集」比较仍然是对的（投影那侧也照样重复）。
    let private visibleTo (seat: Seat) (state: GameState) : Tile list =
        let own =
            match GameState.player seat state with
            | Some player -> PlayerState.hand player @ Option.toList (PlayerState.drawn player)
            | None -> []

        let onTable =
            GameState.players state
            |> List.collect (fun player ->
                PlayerState.kawa player @ (PlayerState.naki player |> List.collect Naki.tiles))

        own @ onTable @ Wall.doraIndicators (GameState.wall state)

    /// 观测这份记录里出现的全部牌，按张数计（与 `visibleTo` 逐项对称）。
    let private tilesIn (observation: Observation) : Tile list =
        let kawa (entries: KawaEntry list) =
            entries |> List.map (fun entry -> entry.Pai)

        let naki (melds: Naki list) = melds |> List.collect Naki.tiles

        observation.Self.Hand
        @ Option.toList observation.Self.Drawn
        @ kawa observation.Self.Kawa
        @ naki observation.Self.Naki
        @ (observation.Others
           |> List.collect (fun other -> kawa other.Kawa @ naki other.Naki))
        @ observation.DoraMarkers

    /// `small` 的每一张都在 `large` 里拿得到（按张数，不是按牌种）。
    let private isSubMultisetOf (large: Tile list) (small: Tile list) : bool =
        (Some large, small)
        ||> List.fold (fun rest tile ->
            rest
            |> Option.bind (fun tiles ->
                if List.contains tile tiles then
                    Some(removeOne tile tiles)
                else
                    None))
        |> Option.isSome

    [<Property>]
    let ``任意局面任意座位，观测里的每一张牌都是这个座位看得见的`` (state: GameState) =
        observationsOf state
        |> List.forall (fun (seat, observation) -> tilesIn observation |> isSubMultisetOf (visibleTo seat state))

    /// 序列化结果里出现的全部牌记法。
    ///
    /// **先把风的那两个字段抹掉**：场风与自风的 wire 也是牌记法（ADR-0001 写作 `1z`-`4z`），
    /// 不抹掉的话四种风牌恒在允许集里，风牌这一路就等于没验。
    /// 牌记法在 wire 上恒是一个完整的 JSON 字符串，因此连引号一起找：`"5m"` 不会误配进 `"5mr"`。
    let private notationsIn (json: string) : Set<string> =
        let withoutKaze = Regex.Replace(json, "\"(bakaze|jikaze)\":\"[0-9]z\"", "")

        Regex.Matches(withoutKaze, "\"([0-9][mpsz]r?)\"")
        |> Seq.map (fun each -> each.Groups.[1].Value)
        |> Set.ofSeq

    [<Property>]
    let ``任意局面任意座位，观测的序列化结果里不出现他家暗牌里的牌`` (state: GameState) =
        observationsOf state
        |> List.forall (fun (seat, observation) ->
            let allowed = visibleTo seat state |> List.map Tile.toMjai |> Set.ofList
            let encoded = Encode.toString 0 (Observation.encoder observation)
            Set.isSubset (notationsIn encoded) allowed)

    [<Property>]
    let ``任意局面任意座位，观测的河与那一家的河逐张一致`` (state: GameState) =
        observationsOf state
        |> List.forall (fun (seat, observation) ->
            let kawaOf (target: Seat) =
                match GameState.player target state with
                | Some player -> PlayerState.kawa player
                | None -> []

            let selfMatches =
                observation.Self.Kawa |> List.map (fun entry -> entry.Pai) = kawaOf seat

            let othersMatch =
                observation.Others
                |> List.forall (fun other -> other.Kawa |> List.map (fun entry -> entry.Pai) = kawaOf other.Seat)

            selfMatches && othersMatch)

    [<Property>]
    let ``任意局面，上帝视角亮出每一家的暗牌`` (state: GameState) =
        let view = GodView.ofState state

        view.Seats |> List.map (fun each -> each.Hand) = (GameState.players state |> List.map PlayerState.hand)
        && view.UraMarkers = Wall.uraIndicators (GameState.wall state)
