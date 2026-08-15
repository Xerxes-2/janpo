namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 摸打循环的不变量：合法动作集非空、牌数守恒、回放确定性、非法动作是值不是异常。
/// 局面取自**可达**局面（随机开一局再随机走若干步），具名用例见 GameStateTests。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |])>]
module GameStateProperties =

    let private allActions (state: GameState) =
        GameState.legalActions state |> List.collect (fun choice -> choice.Actions)

    [<Property>]
    let ``任意时刻合法动作集非空，或这一局已终`` (state: GameState) =
        if GameState.isEnded state then
            List.isEmpty (GameState.legalActions state)
        else
            let choices = GameState.legalActions state

            not (List.isEmpty choices)
            && choices |> List.forall (fun choice -> not (List.isEmpty choice.Actions))

    [<Property>]
    let ``牌数守恒：各家手牌、河与副露里自家亮的那几张加上山上的牌恒为完整一副`` (state: GameState) =
        // 副露里被鸣的那张（`Naki.taken`）**仍算在打牌者的河里**，因此这里只数自家亮出的
        // `consumed`，不然要重复数一张。
        let everything =
            (GameState.players state
             |> List.collect (fun player ->
                 PlayerState.hand player
                 @ PlayerState.kawa player
                 @ (PlayerState.naki player |> List.collect Naki.consumed)))
            @ Wall.tiles (GameState.wall state)

        List.length everything = Ruleset.wallSize ruleset
        && Tile.sort everything = Tile.sort (Ruleset.wallTiles ruleset)

    [<Property>]
    let ``鸣牌后牌数守恒：暗牌加副露的张数与没鸣牌时一样多`` (state: GameState) =
        // 一组碰 / 吃是三张，其中两张来自手牌、一张来自他家的河；因此「暗牌 + 3 × 副露数」
        // 与没鸣牌时的手牌张数完全一致（等打牌时多一张）。
        GameState.players state
        |> List.forall (fun player ->
            let held = List.length (PlayerState.hand player) + 3 * PlayerState.nakiCount player

            held = ruleset.HaipaiSize || held = ruleset.HaipaiSize + 1)

    [<Property>]
    let ``等着打牌的那家 14 张，自摸和了的那家 14 张，其余各家 13 张`` (state: GameState) =
        let holdingFourteen =
            match GameState.phase state with
            | AwaitingDahai phase -> Some phase.Actor
            // 荣和的那张留在放铳者的河里，和了的那家仍是 13 张（牌数守恒也靠这条）。
            | Ended(KyokuEnd.Hora horas) ->
                horas
                |> List.tryPick (fun hora -> if hora.Actor = hora.Target then Some hora.Actor else None)
            | AwaitingResponse _
            | Ended(KyokuEnd.Ryuukyoku _) -> None

        GameState.players state
        |> List.mapi (fun seat player ->
            let expected =
                if Some seat = holdingFourteen then
                    ruleset.HaipaiSize + 1
                else
                    ruleset.HaipaiSize

            // 一组副露抵三张暗牌。
            List.length (PlayerState.hand player) + 3 * PlayerState.nakiCount player = expected)
        |> List.forall id

    [<Property>]
    let ``step 接受一个动作，当且仅当它在合法动作集里`` (state: GameState) (action: Action) =
        let legal = allActions state |> List.contains action

        match GameState.step state action with
        | Ok _ -> legal
        | Error _ -> not legal

    [<Property>]
    let ``合法动作集里的每个动作都推得动局面`` (state: GameState) =
        allActions state
        |> List.forall (fun action ->
            match GameState.step state action with
            | Ok(next, events) ->
                let movedOn =
                    next <> state && GameState.events next = GameState.events state @ events

                // 响应阶段的答复（宣言荣和或「过」）本身都不是既成事实：还有别家没答复时
                // 一个事件都不产出，**收齐最后一份答复的那一步**才裁决、才产出事件。
                let resolvesNow =
                    match GameState.phase state with
                    | AwaitingResponse phase -> List.length phase.Responses = 1
                    | AwaitingDahai _
                    | Ended _ -> true

                movedOn && (not (List.isEmpty events)) = resolvesNow
            | Error _ -> false)

    [<Property>]
    let ``响应阶段等的每一家都能「过」，且它的 Ron 只在不振听、型成立、有役时出现`` (state: GameState) =
        match GameState.phase state with
        | AwaitingResponse phase ->
            phase.Responses
            |> List.forall (fun choice ->
                let canRon =
                    match GameState.player choice.Seat state with
                    | Some player ->
                        not (Furiten.blocksRon (PlayerState.furiten player))
                        && PlayerState.isAgariWith kindSet phase.Pai player
                    | None -> false

                // 无役不可和：能荣和的那几家都算得出符与番。
                let hasYaku =
                    match GameState.horaOf choice.Seat state with
                    | Ok _ -> true
                    | Error _ -> false

                let ronOffered =
                    List.contains (Action.Hora(choice.Seat, phase.Target, phase.Pai)) choice.Actions

                // 被问到的座位至少有一样实事可做（荣和或鸣牌），否则不应当被问。
                let hasSomethingToDo =
                    choice.Actions
                    |> List.exists (fun action ->
                        match action with
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _ -> true
                        | Action.Dahai _
                        | Action.Riichi _
                        | Action.None _ -> false)

                choice.Seat <> phase.Target
                && hasSomethingToDo
                && ronOffered = (canRon && hasYaku)
                && List.contains (Action.None choice.Seat) choice.Actions)
        | AwaitingDahai _
        | Ended _ -> true

    [<Property>]
    let ``鸣牌：吃只吃上家、河底牌鸣不得、鸣完那一手一定有牌可打`` (state: GameState) =
        match GameState.phase state with
        | AwaitingResponse phase ->
            phase.Responses
            |> List.collect (fun choice -> choice.Actions)
            |> List.forall (fun action ->
                match action with
                | Action.Pon(_, target, pai, consumed) ->
                    target = phase.Target
                    && pai = phase.Pai
                    && List.length consumed = 2
                    && Wall.remaining (GameState.wall state) > 0
                | Action.Chi(actor, target, pai, consumed) ->
                    target = phase.Target
                    && pai = phase.Pai
                    && List.length consumed = 2
                    // 吃只能吃上家：宣言的那家恒是打牌者的下家。
                    && Seat.next ruleset target = actor
                    && Wall.remaining (GameState.wall state) > 0
                | Action.Hora _
                | Action.Dahai _
                | Action.Riichi _
                | Action.None _ -> true)
        | AwaitingDahai _
        | Ended _ -> true

    [<Property>]
    let ``鸣牌：刚鸣完那一手不摸牌、只有手切，且打不出被鸣的那张（禁食替）`` (state: GameState) =
        match List.tryLast (GameState.events state), GameState.phase state with
        | Some(Pon(actor, _, pai, _) | Chi(actor, _, pai, _)), AwaitingDahai phase ->
            phase.Actor = actor
            // 鸣牌跳过摸牌：没有「刚摸进的那张」。
            && phase.Tsumo = None
            && phase.Actions
               |> List.forall (fun action ->
                   match action with
                   | Action.Dahai(_, dahai, tsumogiri) -> not tsumogiri && Tile.deaka dahai <> Tile.deaka pai
                   | Action.Hora _
                   | Action.Pon _
                   | Action.Chi _
                   | Action.Riichi _
                   | Action.None _ -> false)
        | _ -> true

    [<Property>]
    let ``鸣牌：被鸣的那张仍在对家的河里，那家的河也被标成鸣走过`` (state: GameState) =
        GameState.players state
        |> List.forall (fun player ->
            PlayerState.naki player
            |> List.forall (fun naki ->
                match Naki.taken naki, Naki.target naki with
                | Some taken, Some target ->
                    match GameState.player target state with
                    | Some victim -> List.contains taken (PlayerState.kawa victim) && PlayerState.kawaTaken victim
                    | None -> false
                // 暗杠（11 票）没有被鸣的那张，本票里也不会出现。
                | _ -> true))

    [<Property>]
    let ``和了收尾时授受把点数与供托一起守恒`` (state: GameState) =
        match GameState.horas state with
        | [] -> true
        | horas ->
            // 和了者收走的是局初的供托**加上这一局里成立的立直棒**（含立直者自己那一根）。
            let kyotaku = (context.Kyotaku + acceptedRiichiCount state) * ruleset.RiichiBou

            // 一次和了的增减之和 = 它收走的供托；全部和了加起来正好把供托收干净。
            (horas |> List.sumBy (fun hora -> List.sum hora.Deltas)) = kyotaku
            // 逐条累加：最后一条就是这一局的最终点数。
            && (List.last horas).Scores = GameState.scores state
            // 立直棒从立直者手里扣出去又进了和了者手里，四家点数之和因此只多出局初的供托。
            && List.sum (GameState.scores state) = List.sum context.Scores + context.Kyotaku * ruleset.RiichiBou
            // 和了者收、放铳者（或付家）付；自摸时其余三家都付。
            && horas
               |> List.forall (fun hora ->
                   List.item hora.Actor hora.Deltas > 0
                   && hora.Fu > 0
                   && hora.Fan > 0
                   && hora.HoraPoints > 0)

    [<Property>]
    let ``回放确定性：同一串动作重放出同一局面与同一事件流`` (seed: int) =
        let final, actions = record Kyoku.randomPlayer seed

        match replay actions (fst (start seed)) with
        | Error _ -> false
        | Ok replayed -> replayed = final && GameState.events replayed = GameState.events final

    [<Property>]
    let ``同一种子跑两次得到同一局`` (seed: int) =
        let first = runWith Kyoku.randomPlayer seed
        let second = runWith Kyoku.randomPlayer seed

        first = second && GameState.events first = GameState.events second

    [<Property>]
    let ``事件流的 JSON 往返不变`` (state: GameState) =
        GameState.events state
        |> List.forall (fun event ->
            Decode.fromString Event.decoder (Encode.toString 0 (Event.encoder event)) = Ok event)

    [<Property>]
    let ``荒牌流局的授受和为零，结算后点数就是局初点数加上增减`` (seed: int) =
        let final = runWith tenpaiSeeking seed
        let result = ryuukyokuOf final

        List.sum result.Deltas = 0
        && result.Scores = List.map2 (+) context.Scores result.Deltas
        && GameState.scores final = result.Scores

    [<Property>]
    let ``听牌的家收听牌料，不听的家付；全听或全不听时不授受`` (seed: int) =
        let result = ryuukyokuOf (runWith tenpaiSeeking seed)
        let tenpaiCount = result.Tenpais |> List.filter id |> List.length

        if tenpaiCount = 0 || tenpaiCount = List.length result.Tenpais then
            result.Deltas |> List.forall (fun delta -> delta = 0)
        else
            List.forall2 (fun tenpai delta -> if tenpai then delta > 0 else delta < 0) result.Tenpais result.Deltas
