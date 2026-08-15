namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 一局的驱动：四个随机选手从开局一路打到荒牌流局，以及跑不下去时的两条错误路径。
module KyokuTests =

    /// 可摸区张数：牌山减王牌减配牌。它同时是一局里 tsumo 与 dahai 的条数。
    let private liveWall =
        Ruleset.wallSize ruleset - ruleset.DeadWallSize - Ruleset.haipaiTotal ruleset

    let private countOf (predicate: Event -> bool) (events: Event list) =
        events |> List.filter predicate |> List.length

    let private isTsumo (event: Event) =
        match event with
        | Tsumo _ -> true
        | StartGame _
        | StartKyoku _
        | Dahai _
        | Pon _
        | Chi _
        | Hora _
        | Ryuukyoku _
        | Riichi _
        | RiichiAccepted _
        | EndKyoku
        | EndGame -> false

    let private isDahai (event: Event) =
        match event with
        | Dahai _ -> true
        | StartGame _
        | StartKyoku _
        | Tsumo _
        | Pon _
        | Chi _
        | Hora _
        | Ryuukyoku _
        | Riichi _
        | RiichiAccepted _
        | EndKyoku
        | EndGame -> false

    let private isNaki (event: Event) =
        match event with
        | Pon _
        | Chi _ -> true
        | StartGame _
        | StartKyoku _
        | Tsumo _
        | Dahai _
        | Hora _
        | Ryuukyoku _
        | Riichi _
        | RiichiAccepted _
        | EndKyoku
        | EndGame -> false

    [<Fact>]
    let ``四个随机选手把一局打到荒牌流局`` () =
        let state = runWith Kyoku.randomPlayer 42
        let events = GameState.events state

        Assert.Equal(70, liveWall)
        // 可摸区摸完才流局，因此 tsumo 恒为可摸区张数；鸣牌跳过摸牌却照样要打一张，
        // 因此 dahai 比 tsumo 多出来的那几条恰好是鸣牌的条数。
        Assert.Equal(liveWall, countOf isTsumo events)
        Assert.Equal(liveWall + countOf isNaki events, countOf isDahai events)
        Assert.True(countOf isNaki events > 0, "随机选手应当鸣过牌")
        Assert.Equal(0, Wall.remaining (GameState.wall state))
        Assert.True(GameState.isEnded state)

        match List.last events with
        | Ryuukyoku result -> Assert.Equal<RyuukyokuReason>(Fanpai, result.Reason)
        | other -> failwith $"一局应当以 ryukyoku 收尾，实际是 {other}"

    [<Fact>]
    let ``荒牌流局时各家手牌加副露 13 张，打出去的牌都在河里`` () =
        let state = runWith Kyoku.randomPlayer 42
        let events = GameState.events state

        for player in GameState.players state do
            // 一组副露抵三张暗牌（碰与吃都是三张）。
            Assert.Equal(
                ruleset.HaipaiSize,
                (PlayerState.hand player |> List.length) + 3 * PlayerState.nakiCount player
            )

        // 被鸣走的那张仍留在打牌者的河里，因此河的总长就是 dahai 的条数。
        Assert.Equal(
            countOf isDahai events,
            GameState.players state
            |> List.sumBy (fun player -> PlayerState.kawa player |> List.length)
        )

    [<Fact>]
    let ``事件流以 start_kyoku 开头、以 ryukyoku 收尾，一行一个 JSON 且能原样解回`` () =
        let events = GameState.events (runWith Kyoku.randomPlayer 42)

        match events with
        | StartKyoku _ :: Tsumo(actor, _) :: _ -> Assert.Equal(context.Oya, actor)
        | other -> failwith $"事件流应以 start_kyoku 与 Oya 的 tsumo 开头，实际是 {List.truncate 2 other}"

        let lines =
            events |> List.map (fun event -> Encode.toString 0 (Event.encoder event))

        for line in lines do
            Assert.DoesNotContain("\n", line)

        Assert.Equal<Result<Event, string> list>(
            events |> List.map Ok,
            lines |> List.map (Decode.fromString Event.decoder)
        )

    [<Fact>]
    let ``同一种子跑出同一局，不同种子跑出不同的局`` () =
        Assert.Equal<Event list>(
            GameState.events (runWith Kyoku.randomPlayer 42),
            GameState.events (runWith Kyoku.randomPlayer 42)
        )

        Assert.NotEqual<Event list>(
            GameState.events (runWith Kyoku.randomPlayer 42),
            GameState.events (runWith Kyoku.randomPlayer 43)
        )

    [<Fact>]
    let ``选手提交非法动作时驱动停下来并给出理由`` () =
        let state, rng = start 42

        let cheater: Player<Rng> =
            fun rng state choice ->
                let hand =
                    GameState.player choice.Seat state
                    |> Option.map PlayerState.hand
                    |> Option.defaultValue []

                let missing = Tile.all |> List.find (fun tile -> not (List.contains tile hand))
                Action.Dahai(choice.Seat, missing, false), rng

        match Kyoku.run cheater rng state with
        | Error(NotInHand(actor, _)) -> Assert.Equal(context.Oya, actor)
        | other -> failwith $"打不在手里的牌应当被拒，实际是 {other}"

    [<Fact>]
    let ``开不了局时给出开局失败的理由`` () =
        let tiny =
            { ruleset with
                TileKinds = Tile.kinds |> List.truncate 4
                Akadora = []
            }

        match Kyoku.runRandom tiny context (Rng.ofSeed 1) with
        | Error(CannotStart(LiveWallTooSmall(required, available))) ->
            Assert.Equal(53, required)
            Assert.Equal(2, available)
        | other -> failwith $"牌不够发配牌时应当开不了局，实际是 {other}"

    [<Fact>]
    let ``跑不下去的两种原因都有中文说明`` () =
        Assert.Equal(
            KyokuStartError.toDisplay (NoDoraIndicator 4),
            KyokuError.toDisplay (CannotStart(NoDoraIndicator 4))
        )

        Assert.Equal(IllegalAction.toDisplay KyokuAlreadyEnded, KyokuError.toDisplay (Illegal KyokuAlreadyEnded))
