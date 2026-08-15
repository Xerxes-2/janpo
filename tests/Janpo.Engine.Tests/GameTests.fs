namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameFixtures

/// 一整场对局：局与局之间怎么推进（连庄、进局、本场与供托的结转）、局数序列走完之后
/// 怎么精算，以及四随机选手无头跑完一整场东风战。
///
/// **这里不断言任何点数的具体数值**：08 票正在把和了点数从 0 改成真值，
/// 断言写成不变量（总和守恒、结转与归属、顺位由点数决定）才对它的改动免疫（裁决 D-4）。
module GameTests =

    /// 用例自己给的一串点数：本层只搬运它，从不算它。四家互不相同，顺位才验得出来。
    let private carried = [ 26000; 24000; 27000; 23000 ]

    let private tonpuusenContext = KyokuContext.initial tonpuusen

    /// 东 4 局的场况：局数序列的最后一项。
    let private lastKyoku =
        { tonpuusenContext with
            Kyoku = tonpuusen.SeatCount
            Oya = tonpuusen.SeatCount - 1
        }

    // ---- 局数序列 ----

    [<Fact>]
    let ``东风战打东1到东4，半庄战再接南1到南4`` () =
        Assert.Equal<(Kaze * int) list>([ Ton, 1; Ton, 2; Ton, 3; Ton, 4 ], Ruleset.kyokus tonpuusen)

        Assert.Equal<(Kaze * int) list>(
            [ Ton, 1; Ton, 2; Ton, 3; Ton, 4; Nan, 1; Nan, 2; Nan, 3; Nan, 4 ],
            Ruleset.kyokus hanchan
        )

    [<Fact>]
    let ``局数序列从座位数推出来，三麻半庄是六局`` () =
        let sanma = { hanchan with SeatCount = 3 }

        Assert.Equal<(Kaze * int) list>([ Ton, 1; Ton, 2; Ton, 3; Nan, 1; Nan, 2; Nan, 3 ], Ruleset.kyokus sanma)

        Assert.Equal(3, Ruleset.kyokus { tonpuusen with SeatCount = 3 } |> List.length)

    // ---- 连庄与进局 ----

    [<Fact>]
    let ``亲和了则连庄：场风局数与亲都不变，本场加一`` () =
        let progress =
            Game.after tonpuusen tonpuusenContext (horaBy tonpuusenContext.Oya carried) carried
            |> nextOf

        Assert.Equal<Kaze>(tonpuusenContext.Bakaze, progress.Bakaze)
        Assert.Equal(tonpuusenContext.Kyoku, progress.Kyoku)
        Assert.Equal(tonpuusenContext.Oya, progress.Oya)
        Assert.Equal(tonpuusenContext.Honba + 1, progress.Honba)
        Assert.Equal<int list>(carried, progress.Scores)

    [<Fact>]
    let ``子和了则进局：亲移到下家，局数走到序列的下一项，本场归零`` () =
        let context = { tonpuusenContext with Honba = 3 }
        let ko = Seat.next tonpuusen context.Oya

        let progress = Game.after tonpuusen context (horaBy ko carried) carried |> nextOf

        Assert.Equal(context.Kyoku + 1, progress.Kyoku)
        Assert.Equal(Seat.next tonpuusen context.Oya, progress.Oya)
        Assert.Equal(0, progress.Honba)

    [<Fact>]
    let ``流局时亲听牌则连庄，本场加一`` () =
        let tenpais =
            Seat.all tonpuusen |> List.map (fun seat -> seat = tonpuusenContext.Oya)

        let progress =
            Game.after tonpuusen tonpuusenContext (ryuukyokuWith tenpais carried) carried
            |> nextOf

        Assert.Equal(tonpuusenContext.Kyoku, progress.Kyoku)
        Assert.Equal(tonpuusenContext.Oya, progress.Oya)
        Assert.Equal(tonpuusenContext.Honba + 1, progress.Honba)

    [<Fact>]
    let ``流局时亲不听则进局，但本场照样加一而不是归零`` () =
        let context = { tonpuusenContext with Honba = 2 }
        let tenpais = Seat.all tonpuusen |> List.map (fun seat -> seat <> context.Oya)

        let progress =
            Game.after tonpuusen context (ryuukyokuWith tenpais carried) carried |> nextOf

        Assert.Equal(context.Kyoku + 1, progress.Kyoku)
        Assert.Equal(Seat.next tonpuusen context.Oya, progress.Oya)
        Assert.Equal(context.Honba + 1, progress.Honba)

    [<Fact>]
    let ``连庄判定只有一份：亲在双响里也算连庄`` () =
        let ko = Seat.next tonpuusen tonpuusenContext.Oya

        let doubleRon =
            match horaBy ko carried, horaBy tonpuusenContext.Oya carried with
            | KyokuEnd.Hora first, KyokuEnd.Hora second -> KyokuEnd.Hora(first @ second)
            | _ -> failwith "合成的和了应当是 Hora 收尾"

        let progress = Game.after tonpuusen tonpuusenContext doubleRon carried |> nextOf

        Assert.True(KyokuEnd.isRenchan tonpuusenContext.Oya doubleRon)
        Assert.Equal(tonpuusenContext.Kyoku, progress.Kyoku)
        Assert.Equal(tonpuusenContext.Oya, progress.Oya)

    // ---- 供托的结转与归属 ----

    [<Fact>]
    let ``供托流局时原样结转到下一局`` () =
        let context = { tonpuusenContext with Kyotaku = 2 }
        let tenpais = Seat.all tonpuusen |> List.map (fun _ -> false)

        let progress =
            Game.after tonpuusen context (ryuukyokuWith tenpais carried) carried |> nextOf

        Assert.Equal(2, progress.Kyotaku)

    [<Fact>]
    let ``供托在和了之后清零：它归和了者，点数授受是 08 票的事`` () =
        let context = { tonpuusenContext with Kyotaku = 3 }

        let renchan =
            Game.after tonpuusen context (horaBy context.Oya carried) carried |> nextOf

        let advanced = Game.after tonpuusen context (horaBy 2 carried) carried |> nextOf

        Assert.Equal(0, renchan.Kyotaku)
        Assert.Equal(0, advanced.Kyotaku)

    [<Fact>]
    let ``终局时场上剩下的供托归点数最高的那家，总点数不变`` () =
        let scores = [ 24000; 27000; 25000; 22000 ]
        let result = Game.settle tonpuusen 2 scores

        let awarded = List.map2 (fun after before -> after - before) result.Scores scores

        Assert.Equal<int list>([ 0; Ruleset.kyotakuScore tonpuusen 2; 0; 0 ], awarded)

        Assert.Equal(List.sum scores + Ruleset.kyotakuScore tonpuusen 2, List.sum result.Scores)

    // ---- 终局精算 ----

    [<Fact>]
    let ``顺位由最终点数决定，点数高的名次靠前`` () =
        let result = Game.settle tonpuusen 0 [ 24000; 27000; 25000; 22000 ]

        Assert.Equal<int list>([ 3; 1; 2; 4 ], result.Juni)

        Assert.Equal<(int * Seat * int) list>(
            [ 1, 1, 27000; 2, 2, 25000; 3, 0, 24000; 4, 3, 22000 ],
            GameResult.ranking result
        )

    [<Fact>]
    let ``同点时起家方向在前的那家名次高`` () =
        let result = Game.settle tonpuusen 0 [ 25000; 25000; 25000; 25000 ]

        Assert.Equal<int list>([ 1; 2; 3; 4 ], result.Juni)

    // ---- 终局条件 ----

    [<Fact>]
    let ``东风战在东4局结束后终局，不做西入与延长`` () =
        let tenpais = Seat.all tonpuusen |> List.map (fun _ -> false)

        let advanced =
            Game.after tonpuusen lastKyoku (ryuukyokuWith tenpais carried) carried

        let renchan = Game.after tonpuusen lastKyoku (horaBy lastKyoku.Oya carried) carried

        Assert.Equal<int list>(carried, (resultOf advanced).Scores)
        // 连庄也不延长：局数序列走完就是终局（票的明文）。
        Assert.Equal<int list>(carried, (resultOf renchan).Scores)

    [<Fact>]
    let ``半庄战的东4局之后接南1局，亲回到起家`` () =
        let progress = Game.after hanchan lastKyoku (horaBy 0 carried) carried |> nextOf

        Assert.Equal<Kaze>(Nan, progress.Bakaze)
        Assert.Equal(1, progress.Kyoku)
        Assert.Equal(0, progress.Oya)

    // ---- 真打一局再推进 ----

    [<Fact>]
    let ``亲自摸和了的那一局收进对局之后连庄，点数原样搬进下一局`` () =
        let context = { tonpuusenContext with Honba = 1 }
        let ended = playScripted context GameStateFixtures.tsumoHoraScript
        let game = Game.advance ended (Game.start tonpuusen)

        let hora =
            match GameState.horas ended with
            | [ hora ] -> hora
            | other -> failwith $"这一局应当只有一家和了，实际是 {List.length other} 家"

        let next = nextOf (Game.progress game)

        Assert.Equal(context.Oya, hora.Actor)
        Assert.Equal(1, List.length (Game.played game))
        Assert.Equal(context.Kyoku, next.Kyoku)
        Assert.Equal(context.Oya, next.Oya)
        Assert.Equal(context.Honba + 1, next.Honba)
        // 点数的具体数值是 08 票的事，本层只验「原样搬过去」。
        Assert.Equal<int list>(GameState.scores ended, next.Scores)

    [<Fact>]
    let ``子荣和了的那一局收进对局之后进局，本场归零、供托清零`` () =
        let context =
            { tonpuusenContext with
                Honba = 3
                Kyotaku = 2
            }

        let ended = playScripted context GameStateFixtures.doubleRonScript
        let game = Game.advance ended (Game.start tonpuusen)
        let next = nextOf (Game.progress game)

        match GameState.horas ended with
        | [ hora ] -> Assert.NotEqual(context.Oya, hora.Actor)
        | other -> failwith $"头跳开着时只有一家荣和，实际是 {List.length other} 家"

        Assert.Equal(context.Kyoku + 1, next.Kyoku)
        Assert.Equal(Seat.next tonpuusen context.Oya, next.Oya)
        Assert.Equal(0, next.Honba)
        Assert.Equal(0, next.Kyotaku)

    // ---- 无头跑完一整场 ----

    [<Fact>]
    let ``四个随机选手无头跑完一整场东风战，终局有最终点数与顺位`` () =
        let game = runWith Kyoku.randomPlayer tonpuusen 42

        Assert.True(Game.isEnded game)
        Assert.Equal<KyokuContext option>(None, Game.nextKyoku game)

        let result =
            match Game.result game with
            | Some result -> result
            | None -> failwith "终局应当有精算"

        // 连庄会多打几局，因此局数只多不少。
        Assert.True(List.length (Game.played game) >= List.length (Ruleset.kyokus tonpuusen))
        Assert.Equal<int list>(List.sort result.Juni, [ 1 .. tonpuusen.SeatCount ])
        Assert.Equal(Ruleset.startingTotal tonpuusen, List.sum result.Scores)

    [<Fact>]
    let ``每一局都打到终，且场况按局数序列推进`` () =
        let game = runWith Kyoku.randomPlayer tonpuusen 7

        for state in Game.played game do
            Assert.True(GameState.isEnded state)

        let played =
            Game.played game
            |> List.map (fun state ->
                let context = GameState.context state
                context.Bakaze, context.Kyoku)
            |> List.distinct

        Assert.Equal<(Kaze * int) list>(Ruleset.kyokus tonpuusen, played)

    [<Fact>]
    let ``半庄战比东风战多打一整个南场`` () =
        let game = runWith Kyoku.randomPlayer hanchan 42

        let bakazes =
            Game.played game
            |> List.map (fun state -> (GameState.context state).Bakaze)
            |> List.distinct

        Assert.Equal<Kaze list>([ Ton; Nan ], bakazes)
        Assert.True(List.length (Game.played game) >= List.length (Ruleset.kyokus hanchan))

    [<Fact>]
    let ``同一种子跑出同一场对局，不同种子跑出不同的对局`` () =
        Assert.Equal<Event list>(
            Game.events (runWith Kyoku.randomPlayer tonpuusen 42),
            Game.events (runWith Kyoku.randomPlayer tonpuusen 42)
        )

        Assert.NotEqual<Event list>(
            Game.events (runWith Kyoku.randomPlayer tonpuusen 42),
            Game.events (runWith Kyoku.randomPlayer tonpuusen 43)
        )

    [<Fact>]
    let ``整场对局的事件流每局收一条 end_kyoku，终局收一条 end_game`` () =
        let game = runWith Kyoku.randomPlayer tonpuusen 42
        let events = Game.events game

        let countOf (target: Event) =
            events |> List.filter (fun event -> event = target) |> List.length

        let startKyokus =
            events
            |> List.filter (fun event ->
                match event with
                | StartKyoku _ -> true
                | StartGame _
                | Tsumo _
                | Dahai _
                | Hora _
                | Ryuukyoku _
                | EndKyoku
                | EndGame -> false)
            |> List.length

        Assert.Equal(List.length (Game.played game), startKyokus)
        Assert.Equal(List.length (Game.played game), countOf EndKyoku)
        Assert.Equal(1, countOf EndGame)
        Assert.Equal<Event>(EndGame, List.last events)

    [<Fact>]
    let ``还没打完的对局不产出 end_game`` () =
        let game = Game.start tonpuusen

        Assert.False(Game.isEnded game)
        Assert.Empty(Game.events game)
        Assert.Equal<int list>(Game.scores game, (KyokuContext.initial tonpuusen).Scores)

    [<Fact>]
    let ``还没打完的一局与已经终局的对局都推不动`` () =
        let state, _ = GameStateFixtures.start 42
        let game = Game.start tonpuusen

        // 这一局才刚开，收不进对局。
        Assert.Equal<KyokuContext option>(Game.nextKyoku game, Game.nextKyoku (Game.advance state game))

        let ended = runWith Kyoku.randomPlayer tonpuusen 42
        let played = Game.played ended

        Assert.Equal<int list>(Game.scores ended, Game.scores (Game.advance (List.head played) ended))
