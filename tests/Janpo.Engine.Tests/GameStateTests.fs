namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 摸打循环：合法动作集的形状、step 的迁移与非法动作的拒绝理由。
/// 不变量（牌数守恒、合法动作集非空、回放确定性）见 GameStateProperties，
/// 整局跑完的事件流见 KyokuTests。
module GameStateTests =

    let private handOf (seat: Seat) (state: GameState) =
        GameState.player seat state
        |> Option.map PlayerState.hand
        |> Option.defaultValue []

    let private kawaOf (seat: Seat) (state: GameState) =
        GameState.player seat state
        |> Option.map PlayerState.kawa
        |> Option.defaultValue []

    let private drawnOf (seat: Seat) (state: GameState) =
        match GameState.player seat state |> Option.bind PlayerState.drawn with
        | Some tile -> tile
        | None -> failwith $"座位 {seat} 应当刚摸过一张"

    /// 当前该出手的那一家的合法动作集。04 票里恒为一家。
    let private waiting (state: GameState) =
        match GameState.legalActions state with
        | [ choice ] -> choice
        | others -> failwith $"应当恰有一家在等出手，实际是 {others}"

    let private stepped (state: GameState) (action: Action) =
        match GameState.step state action with
        | Ok(next, events) -> next, events
        | Error illegal -> failwith $"这个动作应当合法，却被拒：{IllegalAction.toDisplay illegal}"

    let private rejected (state: GameState) (action: Action) =
        match GameState.step state action with
        | Error illegal -> illegal
        | Ok _ -> failwith $"这个动作应当被拒，却被接受了"

    let private paiOf (action: Action) =
        match action with
        | Action.Dahai(_, pai, _) -> pai
        | Action.Hora(_, _, pai) -> pai
        | Action.Pon(_, _, pai, _) -> pai
        | Action.Chi(_, _, pai, _) -> pai
        | Action.Kakan(_, pai, _) -> pai
        | Action.Minkan(_, _, pai, _) -> pai
        | Action.Ankan(actor, _) -> failwith $"座位 {actor} 的暗杠没有被鸣的那张"
        | Action.Riichi actor -> failwith $"座位 {actor} 的立直宣言没有牌"
        | Action.None actor -> failwith $"座位 {actor} 的「过」没有牌"

    [<Fact>]
    let ``开局后只等 Oya 打牌`` () =
        let state = fst (start 42)
        let choice = waiting state

        Assert.Equal(context.Oya, choice.Seat)
        Assert.NotEmpty(choice.Actions)
        Assert.Equal<Seat list>([ context.Oya ], choice.Actions |> List.map Action.actor |> List.distinct)
        Assert.False(GameState.isEnded state)

    [<Fact>]
    let ``合法动作集是手切每一种牌各一条，再加摸切那一条`` () =
        let state = fst (start 42)
        let choice = waiting state

        // 种子 42 的 Oya 配牌是 5m 5m 5mr 7m 8m 6p 9p 9p 5s 9s 1z 5z 7z，摸进 7p。
        // 同一种牌只出现一条（两张 5m），但 5m 与 5mr 是两条；摸切排在最后。
        Assert.Equal<string list>(
            [ "5m"; "5mr"; "7m"; "8m"; "6p"; "9p"; "5s"; "9s"; "1z"; "5z"; "7z"; "7p" ],
            choice.Actions |> List.map (paiOf >> Tile.toMjai)
        )

        Assert.Equal<bool list>(
            [
                for index in 1 .. List.length choice.Actions -> index = List.length choice.Actions
            ],
            choice.Actions
            |> List.map (fun action ->
                match action with
                | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                | Action.Hora _
                | Action.Pon _
                | Action.Chi _
                | Action.Riichi _
                | Action.Ankan _
                | Action.Kakan _
                | Action.Minkan _
                | Action.None _ -> failwith $"这一手不该有和了、鸣牌、立直或「过」：{action}")
        )

    [<Fact>]
    let ``摸切打的是刚摸进的那张`` () =
        let state = fst (start 42)
        let choice = waiting state
        let drawn = drawnOf choice.Seat state

        Assert.Contains(Action.Dahai(choice.Seat, drawn, true), choice.Actions)
        Assert.DoesNotContain(Action.Dahai(choice.Seat, drawn, false), choice.Actions)

    [<Fact>]
    let ``打出一张后牌进河、手牌少一张，轮到下家摸牌`` () =
        let state = fst (start 42)
        let choice = waiting state
        let action = List.head choice.Actions
        let next, events = stepped state action
        let downstream = Seat.next ruleset choice.Seat

        Assert.Equal(ruleset.HaipaiSize, handOf choice.Seat next |> List.length)
        Assert.Equal<Tile list>([ paiOf action ], kawaOf choice.Seat next)
        Assert.Equal(ruleset.HaipaiSize + 1, handOf downstream next |> List.length)
        Assert.Equal(downstream, (waiting next).Seat)

        Assert.Equal<Event list>(
            [
                Dahai(choice.Seat, paiOf action, false)
                Tsumo(downstream, drawnOf downstream next)
            ],
            events
        )

    [<Fact>]
    let ``step 产出的事件同时进了局面的事件流`` () =
        let state = fst (start 42)
        let next, events = stepped state (List.head (waiting state).Actions)

        Assert.Equal<Event list>(GameState.events state @ events, GameState.events next)

    [<Fact>]
    let ``别家提交打牌被拒`` () =
        let state = fst (start 42)
        let other = Seat.next ruleset context.Oya
        let pai = handOf other state |> List.head

        Assert.Equal<IllegalAction>(
            NotYourTurn(other, [ context.Oya ]),
            rejected state (Action.Dahai(other, pai, false))
        )

    [<Fact>]
    let ``座位越界的打牌被拒`` () =
        let state = fst (start 42)
        let pai = handOf context.Oya state |> List.head

        Assert.Equal<IllegalAction>(
            SeatOutOfRange(ruleset.SeatCount, ruleset.SeatCount),
            rejected state (Action.Dahai(ruleset.SeatCount, pai, false))
        )

    [<Fact>]
    let ``打不在手里的牌被拒`` () =
        let state = fst (start 42)
        let hand = handOf context.Oya state

        let missing = Tile.all |> List.find (fun tile -> not (List.contains tile hand))

        Assert.Equal<IllegalAction>(
            NotInHand(context.Oya, missing),
            rejected state (Action.Dahai(context.Oya, missing, false))
        )

    [<Fact>]
    let ``声称摸切但打的不是刚摸进那张时被拒`` () =
        let state = fst (start 42)
        let drawn = drawnOf context.Oya state

        let other = handOf context.Oya state |> List.find (fun tile -> tile <> drawn)

        Assert.Equal<IllegalAction>(
            TsumogiriMismatch(context.Oya, other, true),
            rejected state (Action.Dahai(context.Oya, other, true))
        )

    [<Fact>]
    let ``声称手切但手里只有刚摸进那一张时被拒`` () =
        let state = fst (start 42)
        let drawn = drawnOf context.Oya state

        Assert.Equal(1, handOf context.Oya state |> List.filter ((=) drawn) |> List.length)

        Assert.Equal<IllegalAction>(
            TsumogiriMismatch(context.Oya, drawn, false),
            rejected state (Action.Dahai(context.Oya, drawn, false))
        )

    [<Fact>]
    let ``被拒之后局面原样不动，合法动作照样能提交`` () =
        let state = fst (start 42)
        let legal = List.head (waiting state).Actions

        let missing =
            Tile.all
            |> List.find (fun tile -> not (List.contains tile (handOf context.Oya state)))

        rejected state (Action.Dahai(context.Oya, missing, false)) |> ignore

        Assert.Equal<Action list>((waiting state).Actions, (waiting (fst (start 42))).Actions)
        stepped state legal |> ignore

    [<Fact>]
    let ``一局已终之后任何动作都被拒`` () =
        let state = runWith Kyoku.randomPlayer 42
        let pai = handOf context.Oya state |> List.head

        Assert.True(GameState.isEnded state)
        Assert.Empty(GameState.legalActions state)
        Assert.Equal<IllegalAction>(KyokuAlreadyEnded, rejected state (Action.Dahai(context.Oya, pai, false)))

    [<Fact>]
    let ``海底与河底的标志只在最后一张牌上置位`` () =
        let states = trace Kyoku.randomPlayer 42

        let haitei = states |> List.filter (fun state -> (GameState.flags state).Haitei)
        let houtei = states |> List.filter (fun state -> (GameState.flags state).Houtei)

        Assert.Equal(1, List.length haitei)
        Assert.Equal(1, List.length houtei)

        // 海底：可摸区已空，还等那家把最后一张摸进来的牌打出去。
        let haitei = List.head haitei
        Assert.Equal(0, Wall.remaining (GameState.wall haitei))
        Assert.False(GameState.isEnded haitei)
        Assert.False((GameState.flags haitei).Houtei)

        // 河底：那张打出去之后就是荒牌流局，两个标志不会同时置位。
        let houtei = List.head houtei
        Assert.True(GameState.isEnded houtei)
        Assert.False((GameState.flags houtei).Haitei)

    [<Fact>]
    let ``荒牌流局按听牌家数授受听牌料`` () =
        // 四家都用「尽量保持听牌」的选手，种子决定最后有几家听牌。
        let cases =
            [
                23, [ false; false; false; false ], [ 0; 0; 0; 0 ]
                3, [ true; false; false; false ], [ 3000; -1000; -1000; -1000 ]
                1, [ false; true; false; true ], [ -1500; 1500; -1500; 1500 ]
                28, [ true; true; false; true ], [ 1000; 1000; -3000; 1000 ]
                20, [ true; true; true; true ], [ 0; 0; 0; 0 ]
            ]

        for seed, tenpais, deltas in cases do
            let state = runWith tenpaiSeeking seed
            let result = ryuukyokuOf state

            Assert.Equal(Fanpai, result.Reason)
            Assert.Equal<bool list>(tenpais, result.Tenpais)
            Assert.Equal<int list>(deltas, result.Deltas)
            Assert.Equal<int list>(List.map2 (+) context.Scores deltas, result.Scores)
            Assert.Equal<int list>(result.Scores, GameState.scores state)

    [<Fact>]
    let ``听牌与否由 Shanten 判定，不另写一份`` () =
        let state = runWith tenpaiSeeking 20
        let result = ryuukyokuOf state

        let byShanten =
            GameState.players state
            |> List.map (fun player -> shantenOf (PlayerState.hand player) = 0)

        Assert.Equal<bool list>(byShanten, result.Tenpais)

    [<Fact>]
    let ``被拒的理由有中文说明`` () =
        Assert.Equal("座位 4 不合法，座位只有 0-3", IllegalAction.toDisplay (SeatOutOfRange(4, 4)))
        Assert.Equal("现在不轮到座位 2 出手，等的是座位 0", IllegalAction.toDisplay (NotYourTurn(2, [ 0 ])))
        Assert.Equal("现在不轮到座位 2 出手", IllegalAction.toDisplay (NotYourTurn(2, [])))

        match Tile.parse "3s" with
        | Error error -> failwith $"3s 应当是合法记法，却得到 {error}"
        | Ok tile ->
            Assert.Equal("座位 1 手里没有 3索", IllegalAction.toDisplay (NotInHand(1, tile)))
            Assert.Equal("座位 1 声称摸切 3索，但刚摸进的不是这张", IllegalAction.toDisplay (TsumogiriMismatch(1, tile, true)))

            Assert.Equal("座位 1 声称手切 3索，但手里只有刚摸进的那一张", IllegalAction.toDisplay (TsumogiriMismatch(1, tile, false)))

        Assert.Equal("这一局已经结束了，不再接受任何动作", IllegalAction.toDisplay KyokuAlreadyEnded)
