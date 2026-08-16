namespace Janpo.Engine.Tests

open System.Text.RegularExpressions
open FsCheck.Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 一家在投影里的公开七项，自家与他家共有。**只服务下面那条回归守卫**：
/// `RevealedSeat` 与 `MaskedSeat` 是两个类型（20-1），共有的部分摊到这里才好用同一份判据对。
type SeatFields =
    {
        Jikaze: Kaze
        Junme: int
        Score: int
        Kawa: KawaEntry list
        Naki: Naki list
        Riichi: RiichiState
        Ippatsu: bool
    }

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

    /// 张数是公开信息（牌桌上每家手里摸得出来手里有几张），因此遮蔽之后仍然给得出。
    /// **它是遮蔽不是计算**：读的就是那一家实际的张数，不是按副露数推算的
    /// （摸牌那一手多一张，推算版本的 13 - 3×副露会差一张）。
    [<Property>]
    let ``任意局面任意座位，他家的手牌张数与那一家实际的一致`` (state: GameState) =
        observationsOf state
        |> List.forall (fun (_, observation) ->
            observation.Others
            |> List.forall (fun other ->
                match GameState.player other.Seat state with
                | Some player -> other.HandCount = List.length (PlayerState.hand player)
                | None -> false))

    // ---- 回归守卫： fold 出来的观测 vs 引擎的权威状态 ----

    /// 观测里与局面对不上的那几个**字段名**（空表就是逐字段相等）。
    ///
    /// **票 29a 的迁移闸门就地降级成了它**：那道闸门比的是「直算与 fold 两种实现」，
    /// 直算那套删掉之后它无物可比；但直算那套本来就只是 `GameState` 的谄写，
    /// 因此直接比**引擎的权威状态**同等强且不必养一份死代码。
    /// 报错点名字段（裁决 21-c 的同一条理由）：前投影加一个字段只多一行。
    let private mismatches (seat: Seat) (state: GameState) (observation: Observation) : string list =
        let context = GameState.context state
        let wall = GameState.wall state

        let kawaOf (target: Seat) =
            GameState.events state
            |> List.choose (fun event ->
                match event with
                | Dahai(actor, pai, tsumogiri) when actor = target -> Some { Pai = pai; Tsumogiri = tsumogiri }
                | _ -> None)

        let check (name: string) (equal: bool) = if equal then [] else [ name ]

        // 自家与他家共有的那七项。**投影那边是两个类型**（`RevealedSeat` / `MaskedSeat`），
        // 共有的部分在这里摊成一个记录再逐项对，省得同一份判据写两遍。
        let seatFields (prefix: string) (target: Seat) (player: PlayerState) (fields: SeatFields) =
            check (prefix + ".jikaze") (fields.Jikaze = Seat.jikaze (GameState.ruleset state) context.Oya target)
            @ check (prefix + ".junme") (fields.Junme = GameState.junme target state)
            @ check (prefix + ".score") (fields.Score = PlayerState.score player)
            @ check (prefix + ".kawa") (fields.Kawa = kawaOf target)
            @ check (prefix + ".naki") (fields.Naki = PlayerState.naki player)
            @ check (prefix + ".riichi") (fields.Riichi = PlayerState.riichi player)
            @ check (prefix + ".ippatsu") (fields.Ippatsu = PlayerState.ippatsu player)

        let self =
            match GameState.player seat state with
            | None -> [ "self" ]
            | Some player ->
                check "self.seat" (observation.Self.Seat = seat)
                @ check "self.tehai" (observation.Self.Hand = PlayerState.hand player)
                @ check "self.tsumo" (observation.Self.Drawn = PlayerState.drawn player)
                @ check "self.furiten" (observation.Self.Furiten = PlayerState.furiten player)
                @ seatFields
                    "self"
                    seat
                    player
                    {
                        Jikaze = observation.Self.Jikaze
                        Junme = observation.Self.Junme
                        Score = observation.Self.Score
                        Kawa = observation.Self.Kawa
                        Naki = observation.Self.Naki
                        Riichi = observation.Self.Riichi
                        Ippatsu = observation.Self.Ippatsu
                    }

        let others =
            observation.Others
            |> List.collect (fun other ->
                match GameState.player other.Seat state with
                | None -> [ "others" ]
                | Some player ->
                    let prefix = $"others.{Seat.index other.Seat}"

                    check (prefix + ".tehai_count") (other.HandCount = List.length (PlayerState.hand player))
                    @ check
                        (prefix + ".relative")
                        (other.Relative = Seat.distanceFrom (GameState.ruleset state) seat other.Seat)
                    @ seatFields
                        prefix
                        other.Seat
                        player
                        {
                            Jikaze = other.Jikaze
                            Junme = other.Junme
                            Score = other.Score
                            Kawa = other.Kawa
                            Naki = other.Naki
                            Riichi = other.Riichi
                            Ippatsu = other.Ippatsu
                        })

        let othersOrder =
            Seat.orderFrom (GameState.ruleset state) seat
            |> List.filter (fun each -> each <> seat)

        check "seat" (observation.Seat = seat)
        @ check "bakaze" (observation.Bakaze = context.Bakaze)
        @ check "kyoku" (observation.Kyoku = context.Kyoku)
        @ check "honba" (observation.Honba = context.Honba)
        @ check "kyotaku" (observation.Kyotaku = GameState.kyotaku state)
        @ check "dora_markers" (observation.DoraMarkers = Wall.doraIndicators wall)
        @ check "wall_remaining" (observation.WallRemaining = Wall.remaining wall)
        @ check "others" (observation.Others |> List.map (fun other -> other.Seat) = othersOrder)
        @ self
        @ others

    [<Property>]
    let ``任意局面任意座位，掩蔽流 fold 出来的观测与引擎的状态逐字段一致`` (state: GameState) =
        observationsOf state
        |> List.collect (fun (seat, observation) -> mismatches seat state observation)
        |> List.isEmpty

    /// 见逃密集的轨迹上再验一遍：同巡振听与立直后见逃的永久振听只在那批轨迹里出现，
    /// 而振听恰恰是「引擎知道的」与「座席的历史推得出的」最容易分家的那个字段。
    [<Property(Arbitrary = [| typeof<MinogashiArbitraries> |])>]
    let ``见逃密集的局面上，掩蔽流 fold 出来的观测与引擎的状态仍逐字段一致`` (state: GameState) =
        observationsOf state
        |> List.collect (fun (seat, observation) -> mismatches seat state observation)
        |> List.isEmpty

    [<Property>]
    let ``任意局面，上帝视角亮出每一家的暗牌`` (state: GameState) =
        let view = GodView.ofState state

        view.Seats |> List.map (fun each -> each.Hand) = (GameState.players state |> List.map PlayerState.hand)
        && view.UraMarkers = Wall.uraIndicators (GameState.wall state)
