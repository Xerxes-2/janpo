namespace Janpo.Engine.Tests

open Xunit
open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 立直的不变量。局面取自**可达**局面（含一整条立直密集的轨迹，见 `GameStateArbitraries`），
/// 具名用例见 RiichiTests。
///
/// 最值钱的两条：**供托的根数恒等于「局初供托 + `reach_accepted` 的条数」**（立直棒不会
/// 凭空多出来也不会漏记），以及**一发只可能亮在立直成立的那家头上**。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |])>]
module RiichiProperties =

    let private riichiOf (seat: Seat) (state: GameState) : RiichiState =
        match GameState.player seat state with
        | Some player -> PlayerState.riichi player
        | None -> RiichiState.none

    /// 最后一条事件是不是鸣牌（**三种杠也算**：它们同样打断全场的一发）。
    let private lastIsNaki (state: GameState) : bool =
        match List.tryLast (GameState.events state) with
        | Some(Pon _)
        | Some(Chi _)
        | Some(Ankan _)
        | Some(Kakan _)
        | Some(Minkan _) -> true
        | Some(StartGame _)
        | Some(StartKyoku _)
        | Some(Tsumo _)
        | Some(Dahai _)
        | Some(Riichi _)
        | Some(RiichiAccepted _)
        | Some(Hora _)
        | Some(Ryuukyoku _)
        | Some(Dora _)
        | Some EndKyoku
        | Some EndGame
        | None -> false

    [<Property>]
    let ``场上的立直棒恒等于局初供托加上这一局成立的立直，和了收走之后归零`` (state: GameState) =
        let expected =
            if List.isEmpty (GameState.horas state) then
                context.Kyotaku + acceptedRiichiCount state
            else
                0

        GameState.kyotaku state = expected

    [<Property>]
    let ``每条 reach_accepted 都对得上一条 reach，且成立的不多于宣言的`` (state: GameState) =
        let declared, accepted =
            (([], []), GameState.events state)
            ||> List.fold (fun (declared, accepted) event ->
                match event with
                | Riichi actor -> actor :: declared, accepted
                | RiichiAccepted actor -> declared, actor :: accepted
                | StartGame _
                | StartKyoku _
                | Tsumo _
                | Dahai _
                | Pon _
                | Chi _
                | Hora _
                | Ryuukyoku _
                | EndKyoku
                | Ankan _
                | Kakan _
                | Minkan _
                | Dora _
                | EndGame -> declared, accepted)

        List.length accepted <= List.length declared
        && accepted |> List.forall (fun actor -> List.contains actor declared)

    [<Property>]
    let ``一发只可能亮在立直成立的那家头上，任何鸣牌之后全场都没有一发`` (state: GameState) =
        GameState.players state
        |> List.mapi (fun seat player ->
            let ippatsu = PlayerState.ippatsu player

            (not ippatsu || RiichiState.isAccepted (riichiOf seat state))
            && (not (lastIsNaki state) || not ippatsu))
        |> List.forall id

    [<Property>]
    let ``立直成立之后那家只剩自摸和、暗杠与摸切，宣言牌那一手只剩仍然听牌的打法`` (state: GameState) =
        match GameState.phase state with
        | AwaitingDahai phase ->
            let keeps =
                match GameState.player phase.Actor state with
                | Some player ->
                    RiichiState.tenpaiDahai kindSet (PlayerState.nakiCount player) (PlayerState.hand player)
                | None -> []

            match riichiOf phase.Actor state with
            | RiichiState.Accepted _ ->
                phase.Actions
                |> List.forall (fun action ->
                    match action with
                    | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                    | Action.Hora _ -> true
                    // 暗杠是立直后唯一的例外（判据在 `RiichiState.allowsAnkan`）；
                    // 加杠与大明杠都不行。
                    | Action.Ankan _ -> true
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.None _ -> false)
            | RiichiState.Declared _ ->
                phase.Actions
                |> List.forall (fun action ->
                    match action with
                    | Action.Dahai(_, pai, _) -> List.contains (Tile.deaka pai) keeps
                    | Action.Hora _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.None _ -> false)
            | RiichiState.None -> true
        | AwaitingResponse _
        | Ended _ -> true

    [<Property>]
    let ``立直中的座位鸣不了牌，但仍然被问到荣和`` (state: GameState) =
        match GameState.phase state with
        | AwaitingResponse phase ->
            phase.Responses
            |> List.forall (fun choice ->
                if RiichiState.isActive (riichiOf choice.Seat state) then
                    choice.Actions
                    |> List.forall (fun action ->
                        match action with
                        // 立直中碰不了吃不了，大明杠同理。
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Minkan _ -> false
                        | Action.Hora _
                        | Action.Dahai _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.None _ -> true)
                else
                    true)
        | AwaitingDahai _
        | Ended _ -> true

    [<Property>]
    let ``立直中的家永远听牌，且它的手牌自立直起不再变`` (state: GameState) =
        GameState.players state
        |> List.filter (fun player -> RiichiState.isActive (PlayerState.riichi player))
        |> List.forall (fun player ->
            // 立直后只摸切，因此暗牌张数恒是配牌那么多（等着打牌时多一张刚摸进的）。
            let held = List.length (PlayerState.hand player) + 3 * PlayerState.nakiCount player

            PlayerState.isTenpai kindSet player
            && (held = ruleset.HaipaiSize || held = ruleset.HaipaiSize + 1))

    [<Fact>]
    let ``立直的轨迹里确实立起了直：不变量不是空转`` () =
        let traces = [ for seed in 1..40 -> trace riichiSeeking seed ]

        let accepted =
            traces |> List.sumBy (fun states -> acceptedRiichiCount (List.last states))

        let horasWithRiichi =
            traces
            |> List.collect (fun states -> GameState.horas (List.last states))
            |> List.filter (fun hora -> not (List.isEmpty hora.UraDoraMarkers))

        Assert.True(accepted > 0, $"40 局里一次立直都没有：{accepted}")
        Assert.NotEmpty(horasWithRiichi)
