namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 一局怎么结束的：**流局的全部七种形态**。荒牌流局与听牌料在 `GameStateTests`（04 落地的那批），
/// 本文件补的是四类途中流局、三家和了与流し満貫。
///
/// 牌山一律是**摊出来的**（`startScripted` / `nagashiManganWall`）：这几种形态随机取样几乎
/// 一辈子也碰不上一次（备注 N-8），因此覆盖只能靠摊好的剧本钉住，不能指望属性测试撞上。
///
/// 规则集一律是 `Ruleset.yonma`（= 天凤：三家和了判成途中流局、双响成立、九种九牌 9 种）。
module RyuukyokuTests =

    let private pai (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"{notation} 应当是合法记法，却得到 {error}"

    let private stepped (state: GameState) (action: Action) =
        match GameState.step state action with
        | Ok(next, events) -> next, events
        | Error illegal -> failwith $"这个动作应当合法，却被拒：{IllegalAction.toDisplay illegal}"

    let private actionsOf (seat: Seat) (state: GameState) : Action list =
        GameState.legalActions state
        |> List.tryFind (fun choice -> choice.Seat = seat)
        |> Option.map (fun choice -> choice.Actions)
        |> Option.defaultValue []

    /// 把这一手的摸切提交上去。摊好的剧本里推进局面用它。
    /// 四家依次打出同一张风牌（Oya 是摸切，其余三家手切）。
    let private discardWind (notation: string) (state: GameState) : GameState =
        [ 1; 2; 3 ]
        |> List.fold
            (fun current index -> fst (stepped current (Action.Dahai(seat index, pai notation, false))))
            (fst (stepped state (Action.Dahai(seat 0, pai notation, true))))

    let private tsumogiri (seat: Seat) (state: GameState) : GameState =
        actionsOf seat state
        |> List.tryPick (fun action ->
            match action with
            | Action.Dahai(_, _, true) -> Some action
            | Action.Dahai _
            | Action.Hora _
            | Action.Pon _
            | Action.Chi _
            | Action.Ankan _
            | Action.Kakan _
            | Action.Minkan _
            | Action.Riichi _
            | Action.Ryuukyoku _
            | Action.None _ -> None)
        |> Option.map (fst << stepped state)
        |> Option.defaultWith (fun () -> failwith $"座位 {Seat.index seat} 这一手应当摸切得了")

    /// 各家点数与供托之和。**任何形态结束后它都不变**——本文件每条用例都验它。
    let private totalWith (state: GameState) : int =
        List.sum (GameState.scores state)
        + Ruleset.kyotakuScore ruleset (GameState.kyotaku state)

    let private initialTotal =
        List.sum context.Scores + Ruleset.kyotakuScore ruleset context.Kyotaku

    // ---- 九种九牌 ----

    [<Fact>]
    let ``九种九牌：首巡且无人鸣牌时进合法动作集，由座位宣言`` () =
        let state = startScripted kyuushuScript

        Assert.Contains(Action.Ryuukyoku(seat 0), actionsOf (seat 0) state)

        let ended, events = stepped state (Action.Ryuukyoku(seat 0))
        let result = ryuukyokuOf ended

        Assert.Equal(KyuushuKyuuhai, result.Reason)
        Assert.Equal<Event list>([ Ryuukyoku result ], events)
        Assert.True(GameState.isEnded ended)

    [<Fact>]
    let ``九种九牌不授受，也不亮手牌`` () =
        let ended, _ = stepped (startScripted kyuushuScript) (Action.Ryuukyoku(seat 0))
        let result = ryuukyokuOf ended

        Assert.Equal<int list>([ 0; 0; 0; 0 ], result.Deltas)
        Assert.Equal<bool list>([ false; false; false; false ], result.Tenpais)
        Assert.Equal<int list>(context.Scores, result.Scores)
        Assert.Equal(initialTotal, totalWith ended)

    [<Fact>]
    let ``九种九牌只有第一巡宣言得了：手牌没变，第二巡也不再有那一条`` () =
        let first = startScripted kyuushuScript

        // 手切 2m：九种幺九牌一张不少，因此第二巡不给这一条只可能是「不是第一巡了」。
        let discarded, _ = stepped first (Action.Dahai(seat 0, pai "2m", false))

        let secondTurn =
            discarded |> tsumogiri (seat 1) |> tsumogiri (seat 2) |> tsumogiri (seat 3)

        let hand =
            GameState.player (seat 0) secondTurn
            |> Option.map PlayerState.hand
            |> Option.defaultValue []

        Assert.True(Ryuukyoku.yaochuuKinds hand >= ruleset.KyuushuKinds)
        Assert.DoesNotContain(Action.Ryuukyoku(seat 0), actionsOf (seat 0) secondTurn)

    [<Fact>]
    let ``有人鸣过牌就宣言不了九种九牌`` () =
        let state = startScripted kyuushuAfterNakiScript
        let discarded, _ = stepped state (Action.Dahai(seat 0, pai "5z", true))

        let ponned, _ =
            stepped discarded (Action.Pon(seat 1, seat 0, pai "5z", tilesOf "5z 5z"))

        // 座位 1 鸣完那一手手切一张，随后座位 2 摸牌——它手里有九种幺九牌，但第一巡已经没了。
        let drawn, _ = stepped ponned (Action.Dahai(seat 1, pai "9s", false))

        let hand =
            GameState.player (seat 2) drawn
            |> Option.map PlayerState.hand
            |> Option.defaultValue []

        Assert.True(Ryuukyoku.yaochuuKinds hand >= ruleset.KyuushuKinds)
        Assert.DoesNotContain(Action.Ryuukyoku(seat 2), actionsOf (seat 2) drawn)

    [<Fact>]
    let ``宣言不了九种九牌时提交它被拒，理由有中文说明`` () =
        let state = startScripted kyuushuAfterNakiScript

        match GameState.step state (Action.Ryuukyoku(seat 0)) with
        | Ok _ -> failwith "座位 0 的幺九牌不够九种，不该宣言得了"
        | Error illegal ->
            Assert.Equal<IllegalAction>(CannotRyuukyoku(seat 0), illegal)

            Assert.Equal("座位 0 此刻宣言不了九种九牌（不是第一巡、有人鸣过牌或幺九牌种数不够）", IllegalAction.toDisplay illegal)

    // ---- 四风连打 ----

    [<Fact>]
    let ``四风连打：首巡四家打同一张风牌且无人鸣牌`` () =
        let state = startScripted suufonRendaScript

        let ended = discardWind "1z" state
        let result = ryuukyokuOf ended

        Assert.Equal(SuufonRenda, result.Reason)
        Assert.Equal<int list>([ 0; 0; 0; 0 ], result.Deltas)
        Assert.Equal<bool list>([ false; false; false; false ], result.Tenpais)
        Assert.Equal<int list>(context.Scores, result.Scores)
        Assert.Equal(initialTotal, totalWith ended)
        // 四条 dahai 之后紧跟着 ryukyoku，中间没有第五条 tsumo。
        Assert.Equal<Event>(Ryuukyoku result, List.last (GameState.events ended))

    [<Fact>]
    let ``第四家打的是别的风牌就不是四风连打`` () =
        let state = startScripted suufonRendaScript

        let played =
            [ 1; 2 ]
            |> List.fold
                (fun current index -> fst (stepped current (Action.Dahai(seat index, pai "1z", false))))
                (fst (stepped state (Action.Dahai(seat 0, pai "1z", true))))

        // 座位 3 摸进的是 2z，摸切它——四家打的不是同一张风牌。
        let continued = tsumogiri (seat 3) played

        Assert.False(GameState.isEnded continued)

    // ---- 四杠散了 ----

    [<Fact>]
    let ``四杠散了：第四个杠成立、杠的那家打完一张就流局`` () =
        let fourKans =
            startScriptedRinshan "2m 3m 9p 3z" suukaikanScript
            |> driveUntil kanSeeking (fun state -> GameState.kanCount state = ruleset.RinshanCount)

        // 三个暗杠在 Oya，第四个是座位 1 的大明杠：**不是同一家杠的**。
        Assert.Equal(ruleset.RinshanCount, GameState.kanCount fourKans)
        Assert.False(GameState.isEnded fourKans)

        let ended = driveUntil kanSeeking GameState.isEnded fourKans
        let result = ryuukyokuOf ended

        Assert.Equal(Suukaikan, result.Reason)
        Assert.Equal<int list>([ 0; 0; 0; 0 ], result.Deltas)
        Assert.Equal<int list>(context.Scores, result.Scores)
        Assert.Equal(initialTotal, totalWith ended)

    [<Fact>]
    let ``单人四杠不流局：四个杠都是同一家杠的，这一局接着打`` () =
        let fourKans =
            startScriptedRinshan "2m 3m 9p 5z" singleSuukanScript
            |> driveUntil kanSeeking (fun state -> GameState.kanCount state = ruleset.RinshanCount)

        let kansOf (seat: Seat) =
            GameState.player seat fourKans
            |> Option.map PlayerState.kanCount
            |> Option.defaultValue 0

        Assert.Equal(ruleset.RinshanCount, kansOf (seat 0))

        // 杠完那一手打出去之后局面照旧推进：下家摸牌，不是流局。
        let continued = tsumogiri (seat 0) fourKans

        Assert.False(GameState.isEnded continued)

    // ---- 四家立直 ----

    [<Fact>]
    let ``四家立直：第四份立直成立的那一刻流局`` () =
        let ended =
            startScripted suuchaRiichiScript |> driveUntil riichiSeeking GameState.isEnded

        let result = ryuukyokuOf ended

        Assert.Equal(SuuchaRiichi, result.Reason)
        Assert.Equal(ruleset.SeatCount, acceptedRiichiCount ended)
        // 第四根立直棒照出：供托留在场上，由 05 结转到下一局。
        Assert.Equal(ruleset.SeatCount, GameState.kyotaku ended)
        Assert.Equal<int list>([ 0; 0; 0; 0 ], result.Deltas)
        // 四家的手牌都随立直亮着，因此 tenpais 四个都是 true。
        Assert.Equal<bool list>([ true; true; true; true ], result.Tenpais)
        Assert.Equal(initialTotal, totalWith ended)

    // ---- 三家和了 ----

    [<Fact>]
    let ``三家和了：默认（天凤）判成途中流局，一家也不成立`` () =
        let state =
            startScripted tripleRonScript
            |> driveUntil passive (fun state ->
                match GameState.phase state with
                | AwaitingResponse phase -> phase.Pai = pai "4p"
                | AwaitingDahai _
                | Ended _ -> false)

        Assert.True(ruleset.SanchaHoraRyuukyoku)

        let first, _ = stepped state (Action.Hora(seat 1, seat 0, pai "4p"))
        let second, _ = stepped first (Action.Hora(seat 2, seat 0, pai "4p"))
        let ended, events = stepped second (Action.Hora(seat 3, seat 0, pai "4p"))

        let result = ryuukyokuOf ended

        Assert.Equal(SanchaHora, result.Reason)
        Assert.Empty(GameState.horas ended)
        Assert.Equal<Event list>([ Ryuukyoku result ], events)
        Assert.Equal<int list>([ 0; 0; 0; 0 ], result.Deltas)
        // 亮出手牌的是宣言荣和的那三家，打牌的座位 0 没亮。
        Assert.Equal<bool list>([ false; true; true; true ], result.Tenpais)
        Assert.Equal<int list>(context.Scores, result.Scores)
        Assert.Equal(initialTotal, totalWith ended)

    [<Fact>]
    let ``两家荣和照样成立：三家和了要凑够三家`` () =
        let state =
            startScripted doubleRonScript
            |> driveUntil passive (fun state ->
                match GameState.phase state with
                | AwaitingResponse phase -> phase.Pai = pai "4p"
                | AwaitingDahai _
                | Ended _ -> false)

        let first, _ = stepped state (Action.Hora(seat 2, seat 0, pai "4p"))
        let ended, _ = stepped first (Action.Hora(seat 3, seat 0, pai "4p"))

        Assert.Equal(2, List.length (GameState.horas ended))
        Assert.Equal<Ryuukyoku option>(None, GameState.ryuukyoku ended)

    // ---- 流し満貫 ----

    /// 流し満貫的一局：四家一律摸切（因此谁也不鸣牌），座位 0 摸到的全是幺九牌。
    let private nagashiKyoku (nakiPai: Tile option) : GameState =
        startFromWall ruleset context (nagashiManganWall ruleset nakiPai)

    [<Fact>]
    let ``流し満貫：河里全是幺九牌且一张未被鸣走，以满贯清算替代听牌料`` () =
        let ended = nagashiKyoku None |> driveUntil passive GameState.isEnded
        let result = ryuukyokuOf ended

        let kawa =
            GameState.player (seat 0) ended
            |> Option.map PlayerState.kawa
            |> Option.defaultValue []

        Assert.Equal(NagashiMangan, result.Reason)
        Assert.NotEmpty(kawa)
        Assert.All(kawa, fun tile -> Assert.True(Ryuukyoku.yaochuuKinds [ tile ] = 1))

        Assert.False(
            GameState.player (seat 0) ended
            |> Option.map PlayerState.kawaTaken
            |> Option.defaultValue true
        )

        // Oya 的流し満貫走**满贯档**（自摸满贯 4000 all），而不是听牌料的 ±1000 / 1500 / 3000。
        Assert.Equal<int list>([ 12000; -4000; -4000; -4000 ], result.Deltas)
        Assert.Equal<int list>(List.map2 (+) context.Scores result.Deltas, result.Scores)
        Assert.Equal(initialTotal, totalWith ended)
        Assert.Equal(0, List.sum result.Deltas)

    [<Fact>]
    let ``河里有牌被鸣走，流し満貫就不成立`` () =
        // 座位 0 的最后一次自摸摸进 1z，座位 1 手里正好两张 1z——碰掉它。
        // **座位 0 的河一字不差**（被鸣那张仍留在河里），差的只有 `kawaTaken` 这一个标记。
        let taken = pai "1z"

        let atTheNaki =
            nagashiKyoku (Some taken)
            |> driveUntil passive (fun state ->
                Wall.remaining (GameState.wall state) = 1
                && actionsOf (seat 1) state
                   |> List.exists (fun action ->
                       match action with
                       | Action.Pon _ -> true
                       | Action.Dahai _
                       | Action.Hora _
                       | Action.Chi _
                       | Action.Ankan _
                       | Action.Kakan _
                       | Action.Minkan _
                       | Action.Riichi _
                       | Action.Ryuukyoku _
                       | Action.None _ -> false))

        let ponned, _ =
            stepped atTheNaki (Action.Pon(seat 1, seat 0, taken, [ taken; taken ]))

        let ended = driveUntil passive GameState.isEnded ponned
        let result = ryuukyokuOf ended

        let kawa =
            GameState.player (seat 0) ended
            |> Option.map PlayerState.kawa
            |> Option.defaultValue []

        // 河仍旧全是幺九牌，只是有一张被鸣走过。
        Assert.All(kawa, fun tile -> Assert.True(Ryuukyoku.yaochuuKinds [ tile ] = 1))

        Assert.True(
            GameState.player (seat 0) ended
            |> Option.map PlayerState.kawaTaken
            |> Option.defaultValue false
        )

        Assert.Equal(Fanpai, result.Reason)
        Assert.Equal(initialTotal, totalWith ended)

    [<Fact>]
    let ``流し満貫的判据只看河与那个记号，不看听牌`` () =
        let kawaOf (taken: bool) (notations: string) : PlayerState =
            let tiles = tilesOf notations

            let player =
                (PlayerState.ofHaipai ruleset.StartingScore None tiles, tiles)
                ||> List.fold (fun player tile -> PlayerState.discard tile player)

            if taken then PlayerState.markKawaTaken player else player

        let players =
            [
                kawaOf false "1m 9m 1p 9p 1s 9s 1z 2z 3z" // 全幺九、没被鸣走 → 成立
                kawaOf true "1m 9m 1p 9p 1s 9s 1z 2z 3z" // 同一条河，被鸣走过 → 不成立
                kawaOf false "1m 9m 1p 9p 1s 9s 1z 2z 5m" // 混了一张中张 → 不成立
                kawaOf false "" // 一张都没打过 → 不成立
            ]

        Assert.Equal<Seat list>([ seat 0 ], Ryuukyoku.nagashiSeats players)

    [<Fact>]
    let ``流し満貫的清算就是自摸满贯，多家成立时逐家累加`` () =
        let oya = seat 0

        // Ko（座位 1）一家成立：自摸满贯 2000 / 4000。
        Assert.Equal<int list>([ -4000; 8000; -2000; -2000 ], Ryuukyoku.nagashiDeltas ruleset oya [ seat 1 ])

        // Oya 与一个 Ko 同时成立：两笔逐家累加，和仍为 0。
        let both = Ryuukyoku.nagashiDeltas ruleset oya [ oya; seat 1 ]

        Assert.Equal<int list>([ 12000 - 4000; -4000 + 8000; -4000 - 2000; -4000 - 2000 ], both)
        Assert.Equal(0, List.sum both)

    // ---- 连庄、本场与供托 ----

    [<Fact>]
    let ``途中流局一律连庄，本场递增、供托原样结转`` () =
        let cases = [ KyuushuKyuuhai; SuufonRenda; Suukaikan; SuuchaRiichi; SanchaHora ]

        for reason in cases do
            let result =
                {
                    Reason = reason
                    // 途中流局的 tenpais 与连庄无关：这里故意全填 false。
                    Tenpais = [ false; false; false; false ]
                    Deltas = [ 0; 0; 0; 0 ]
                    Scores = context.Scores
                }

            let kyokuEnd = KyokuEnd.Ryuukyoku result

            Assert.True(KyokuEnd.isRenchan context.Oya kyokuEnd, $"{RyuukyokuReason.toMjai reason} 应当连庄")

            match Game.after ruleset { context with Kyotaku = 2 } kyokuEnd context.Scores with
            | GameProgress.NextKyoku next ->
                Assert.Equal(context.Oya, next.Oya)
                Assert.Equal(context.Kyoku, next.Kyoku)
                Assert.Equal(context.Honba + 1, next.Honba)
                Assert.Equal(2, next.Kyotaku)
            | GameProgress.Ended _ -> failwith "东 1 局之后还有下一局"

    [<Fact>]
    let ``荒牌流局与流し満貫仍然看 Oya 听不听牌`` () =
        let ryuukyokuOf (reason: RyuukyokuReason) (tenpais: bool list) =
            KyokuEnd.Ryuukyoku
                {
                    Reason = reason
                    Tenpais = tenpais
                    Deltas = [ 0; 0; 0; 0 ]
                    Scores = context.Scores
                }

        for reason in [ Fanpai; NagashiMangan ] do
            Assert.True(KyokuEnd.isRenchan context.Oya (ryuukyokuOf reason [ true; false; false; false ]))
            Assert.False(KyokuEnd.isRenchan context.Oya (ryuukyokuOf reason [ false; true; true; true ]))
