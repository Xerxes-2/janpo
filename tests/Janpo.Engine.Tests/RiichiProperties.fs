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
///
/// **这一族的采样有多稀**（票 96 把 `GameStateArbitraries` 的全域——400 颗种子 × 10 条轨迹
/// ——逐步数了一遍）：21.7 万个局面里只有 **104 个**「有家立直中」，且全部来自
/// `riichiSeeking` 那一条轨迹；换算下来，一趟属性（100 个样本）里这几条断言只有
/// **约 10%** 的概率真的开过口（判据 3）。因此**具名用例才是这一族的主力闸门**：
/// `RiichiTests` 与下面那两条 `[<Fact>]`。
///
/// **立直 + 副露（暗杠）在这个取值域里到不了**：`riichiSeeking` 从不杠、`kanSeeking`
/// 从不立直，因此 `stillTenpai` 里 `nakiCount > 0` 那一支靠的是 `KanTests` 里
/// `riichiAnkanScript` 的具名用例（判据 4：覆盖不到的要写出来）。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |], Parallelism = 8)>]
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
        |> Seat.indexed
        |> List.forall (fun (seat, player) ->
            let ippatsu = PlayerState.ippatsu player

            (not ippatsu || RiichiState.isAccepted (riichiOf seat state))
            && (not (lastIsNaki state) || not ippatsu))

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
                    | Action.Ryuukyoku _
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
                    | Action.Ryuukyoku _
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
                        | Action.Ryuukyoku _
                        | Action.None _ -> true)
                else
                    true)
        | AwaitingDahai _
        | Ended _ -> true

    /// 这手牌的向听。
    let private shantenValue (shape: HandShape) : int =
        shape |> Shanten.calculate kindSet |> Shanten.value

    /// 立直中那家此刻还听不听牌。**问的是哪几张，由手牌形态与立直进到哪一步定**：
    ///
    /// - **等摸形（3n+1）**：手上这几张就是宣言时那几张，向听必须是 0；
    /// - **刚摸完（3n+2）且立直已成立**：只能摸切，因此要求**去掉刚摸进那张**
    ///   之后仍然听牌——这比拿 14 张问「向听 = 0」更硬（后者只说「存在一张打了还听」），
    ///   而且不会把**摸进和了牌那一手**误判成不听：那 14 张已成和了型、向听是 **−1**，
    ///   `PlayerState.isTenpai`（向听 = 0）对它返回 false——那不是「它不听牌」，是问错了牌
    ///   （`PlayerState.isTenpai` 自己写明只接 3n+1；票 96 的定点反例就是这一手）；
    /// - **刚摸完（3n+2）而宣言牌还没打出去**：手牌还没冻住，宣言牌可以是手切的，
    ///   因此只要求**打得出至少一张仍然听牌的牌**（向听 ≤ 0；已成和了型时是 −1，
    ///   而放弃自摸宣立直是合法的，见 `RiichiState.canDeclare`）。宣言牌只能从听牌形里挑
    ///   这一条由「立直成立之后那家只剩……」那条属性守着。
    let private stillTenpai (player: PlayerState) : bool =
        let hand = PlayerState.hand player
        let nakiCount = PlayerState.nakiCount player

        let tenpaiWithout (drawn: Tile) =
            match hand |> removeOne drawn |> HandShape.create nakiCount with
            | Ok shape -> shantenValue shape = 0
            | Error _ -> false

        match HandShape.create nakiCount hand with
        | Error _ -> false
        | Ok shape when HandShape.isAwaitingDraw shape -> shantenValue shape = 0
        // 摸进那张记不着就该红：立直成立之后多出来的那一张只可能是自摸或岭上摸来的。
        | Ok _ when RiichiState.isAccepted (PlayerState.riichi player) ->
            PlayerState.drawn player |> Option.exists tenpaiWithout
        | Ok shape -> shantenValue shape <= 0

    /// 立直中的每一家：**仍然听牌**（问的是哪几张见 `stillTenpai`），且暗牌张数没变过。
    let private riichiHandsIntact (state: GameState) : bool =
        GameState.players state
        |> List.filter (fun player -> RiichiState.isActive (PlayerState.riichi player))
        |> List.forall (fun player ->
            // 立直后只摸切，因此暗牌张数恒是配牌那么多（等着打牌时多一张刚摸进的）。
            let held = List.length (PlayerState.hand player) + 3 * PlayerState.nakiCount player

            stillTenpai player
            && (held = ruleset.HaipaiSize || held = ruleset.HaipaiSize + 1))

    /// 立直中那几家的手牌，报红时用（票 96 的定点反例就是靠它看清的）。
    let private riichiHandsOf (state: GameState) : string =
        GameState.players state
        |> Seat.indexed
        |> List.filter (fun (_, player) -> RiichiState.isActive (PlayerState.riichi player))
        |> List.map (fun (seat, player) ->
            let hand = PlayerState.hand player |> List.map Tile.toMjai |> String.concat " "

            $"座位 {Seat.index seat}：{hand}")
        |> String.concat "；"

    [<Property>]
    let ``立直中的家永远听牌，且它的手牌自立直起不再变`` (state: GameState) = riichiHandsIntact state

    /// 一局**立直之后自摸和**的全部局面（`tsumoHoraScript` 的牌山 + 见立直就立直的选手）：
    /// Oya 听 `5z` 单骑，第 1 巡摸进 `1z` 就立直、摸切宣言牌，第 2 巡摸进 `5z`
    /// ——**那一手它立直中、手里是 14 张的和了型**。
    let private riichiTsumoStates =
        traceFrom riichiSeeking (Rng.ofSeed 1) (startScripted tsumoHoraScript)

    /// **票 96 的定点锚点**：那条随机属性稀稀落落红过的那一张局面就是这一手
    /// （FsCheck 里是 `1p2p3p 8p8p 1s2s3s 6s6s7s7s8s8s`，形同）。随机属性每趟换种子、
    /// 而它要抓到这一手得同时撞上「立直密集的那条轨迹」与「采样恰好落在这一步」，
    /// 比 1/7 还稀；**这条锚点每趟都跑到它**（判据 3）。
    [<Fact>]
    let ``立直中的家摸进和了牌那一手：不变量仍然成立`` () =
        let holdingAgari =
            riichiTsumoStates
            |> List.filter (fun state ->
                GameState.players state
                |> List.exists (fun player ->
                    RiichiState.isActive (PlayerState.riichi player)
                    && PlayerState.isAgari kindSet player))

        Assert.NotEmpty holdingAgari

        riichiTsumoStates
        |> List.iteri (fun index state ->
            Assert.True(riichiHandsIntact state, $"第 {index} 步的局面破了立直的不变量：{riichiHandsOf state}"))

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
