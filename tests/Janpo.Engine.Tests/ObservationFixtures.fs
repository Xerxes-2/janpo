namespace Janpo.Engine.Tests

open Janpo

/// 一家在投影里的公开七项，自家与他家共有。**只服务下面那条判据**：
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

/// 观测与**引擎的权威状态**逐字段对的那份判据。
///
/// **它是票 29a 那道迁移闸门的继承人**：闸门比的是「直算与 fold 两种实现」，直算那套删掉之后
/// 无物可比；引擎的 `GameState` 同等强，且它**既不经过掩蔽也不经过 fold**——
/// 因此拿它当锚点的断言不可能退化成恒真式（票 60）。
///
/// 摊在这里而不是留在 `ObservationProperties` 里，是因为**两处要用同一份判据**：
/// 单个局面上的回归守卫（`ObservationProperties`），以及一整局逐手推进的三方闸门
/// （`SeatStreamGate`，票 60）。
[<RequireQualifiedAccess>]
module ObservationFixtures =

    /// 观测里与局面对不上的那几个**字段名**（空表就是逐字段相等）。
    /// 报错点名字段（裁决 21-c 的同一条理由）：前投影加一个字段只多一行。
    let mismatches (seat: Seat) (state: GameState) (observation: Observation) : string list =
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
