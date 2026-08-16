namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 回放（票 26）：**导出的事件流能被引擎重新 fold 出同一个终局**。
///
/// 这里不比「差不多」——比的是逐条相同的事件流与逐项相同的终局精算。回放没有第二套实现，
/// 它把事件流里的动作原样交回 `GameState.step`（ADR-0002），因此这几条用例真正验的是
/// 「事件流够不够把一场对局完整地记下来」。
module ReplayTests =

    let private played (seed: int) : Game =
        match Game.runRandom Ruleset.yonma (Rng.ofSeed seed) with
        | Ok(game, _) -> game
        | Error error -> failwith $"种子 {seed} 这一场应当打得完，却得到「{KyokuError.toDisplay error}」"

    let private replayed (ruleset: Ruleset) (events: Event list) : Replayed =
        match Replay.game ruleset events with
        | Ok replayed -> replayed
        | Error error -> failwith $"这份事件流应当回放得动，却得到「{ReplayError.toDisplay error}」"

    /// 一场对局的两个终局是不是同一个（点数与顺位）。
    let private assertSameResult (expected: Game) (actual: Replayed) =
        match Game.result expected, Replayed.result actual with
        | Some one, Some other ->
            Assert.Equal<int list>(one.Scores, other.Scores)
            Assert.Equal<int list>(one.Juni, other.Juni)
        | _ -> failwith "两场都该已经终局"

    [<Fact>]
    let ``回放出的事件流与原来的逐条相同，终局也相同`` () =
        // 三个种子：22 号票挑过的那两个（碰吃杠与和了都出现过）加一个流局多的。
        for seed in [ 2088; 1177; 4242 ] do
            let original = played seed
            let events = Game.events original
            let replayed = replayed Ruleset.yonma events

            Assert.Equal<Event list>(events, Replayed.events replayed)
            assertSameResult original replayed

    [<Fact>]
    let ``牌谱自带规则集，回放照它走`` () =
        let original = played 2088

        let paifu =
            Paifu.create Ruleset.yonma (StartGame [ "p0"; "p1"; "p2"; "p3" ] :: Game.events original) [] Prompting.empty

        match Replay.ofPaifu paifu with
        | Ok replayed ->
            Assert.Equal<Event list>(Game.events original, Replayed.events replayed)
            assertSameResult original replayed
        | Error error -> failwith $"这份牌谱应当回放得动，却得到「{ReplayError.toDisplay error}」"

    [<Fact>]
    let ``半庄战八局照样逐局 fold 回来`` () =
        let hanchan = Ruleset.yonma |> Ruleset.withLength Hanchan

        let original =
            match Game.runRandom hanchan (Rng.ofSeed 7) with
            | Ok(game, _) -> game
            | Error error -> failwith $"这一场应当打得完，却得到「{KyokuError.toDisplay error}」"

        let replayed = replayed hanchan (Game.events original)

        Assert.Equal(List.length (Game.played original), List.length (Game.played replayed.Game))
        Assert.Equal<Event list>(Game.events original, Replayed.events replayed)
        assertSameResult original replayed

    [<Fact>]
    let ``打到一半的事件流回放出打到一半的那一场`` () =
        // 分享一场还没打完的对局是常事（M2 的 URL 分享）：末尾没有 `end_game`，
        // 最后一局也可能没走完——能 fold 到哪就是哪，不是错误。
        let original = played 2088
        let full = Game.events original

        let truncated =
            full
            |> List.mapi (fun index event -> index, event)
            |> List.filter (fun (_, event) -> event = EndKyoku)
            |> List.tryHead
            |> Option.map (fun (index, _) -> List.truncate (index + 1) full)
            |> Option.defaultValue full

        let replayed = replayed Ruleset.yonma truncated

        Assert.Equal(1, List.length (Game.played replayed.Game))
        Assert.Equal<Event list>(truncated, Replayed.events replayed)

    // ---- 随机取样碰不到的那几条路 ----

    let private pai (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"{notation} 应当是合法记法，却得到 {error}"

    let private stepped (state: GameState) (action: Action) : GameState =
        match GameState.step state action with
        | Ok(next, _) -> next
        | Error illegal -> failwith $"这个动作应当合法，却被拒：{IllegalAction.toDisplay illegal}"

    let private atTheTripleRon () =
        startScripted tripleRonScript
        |> driveUntil passive (fun state ->
            match GameState.phase state with
            | AwaitingResponse phase -> phase.Pai = pai "4p"
            | AwaitingDahai _
            | Ended _ -> false)

    /// 七种流局形态各一条**跑到终局**的轨迹（各条副本见 `RyuukyokuProperties`）。
    /// 随机取样一辈子也碰不到途中流局（备注 N-8），而回放里两条特殊路径就在它们身上：
    /// **九种九牌**是唐一由座位宣言的流局，**三家和了**那三家的荣和宣言根本不产出 `hora` 事件。
    let private endedKyokus: (RyuukyokuReason * GameState) list =
        [
            Fanpai, runWith tenpaiSeeking 3

            NagashiMangan,
            (startFromWall ruleset context (nagashiManganWall ruleset None)
             |> driveUntil passive GameState.isEnded)

            KyuushuKyuuhai, stepped (startScripted kyuushuScript) (Action.Ryuukyoku(seat 0))

            SuufonRenda,
            ([ 1; 2; 3 ]
             |> List.fold
                 (fun current index -> stepped current (Action.Dahai(seat index, pai "1z", false)))
                 (stepped (startScripted suufonRendaScript) (Action.Dahai(seat 0, pai "1z", true))))

            Suukaikan,
            (startScriptedRinshan "2m 3m 9p 3z" suukaikanScript
             |> driveUntil kanSeeking GameState.isEnded)

            SuuchaRiichi, (startScripted suuchaRiichiScript |> driveUntil riichiSeeking GameState.isEnded)

            SanchaHora,
            (atTheTripleRon ()
             |> fun state -> stepped state (Action.Hora(seat 1, seat 0, pai "4p"))
             |> fun state -> stepped state (Action.Hora(seat 2, seat 0, pai "4p"))
             |> fun state -> stepped state (Action.Hora(seat 3, seat 0, pai "4p")))
        ]

    /// 一局的事件流回放回去，逐条相同；`Game.events` 会在每一局后面接一条 `end_kyoku`。
    let private assertKyokuReplays (label: string) (ended: GameState) =
        let events = GameState.events ended

        match Replay.game ruleset events with
        | Ok replayed ->
            Assert.Equal<Event list>(events @ [ EndKyoku ], Replayed.events replayed)
            Assert.Equal<int list>(GameState.scores ended, Game.scores replayed.Game)
        | Error error -> failwith $"{label} 这一局应当回放得动，却得到「{ReplayError.toDisplay error}」"

    [<Fact>]
    let ``七种流局形态各自回放出同一条事件流`` () =
        Assert.Equal<RyuukyokuReason list>(RyuukyokuReason.all, endedKyokus |> List.map fst)

        for reason, ended in endedKyokus do
            assertKyokuReplays (RyuukyokuReason.toMjai reason) ended

    [<Fact>]
    let ``双响与立直那几条事件也回放得回去`` () =
        let doubleRon =
            startScripted doubleRonScript
            |> driveUntil passive (fun state ->
                match GameState.phase state with
                | AwaitingResponse phase -> phase.Pai = pai "4p"
                | AwaitingDahai _
                | Ended _ -> false)
            |> fun state -> stepped state (Action.Hora(seat 2, seat 0, pai "4p"))
            |> fun state -> stepped state (Action.Hora(seat 3, seat 0, pai "4p"))

        assertKyokuReplays "双响" doubleRon
        // 自摸和：和了那一手在摸牌后阶段，与荣和不是同一条路。
        assertKyokuReplays "自摸和" (startScripted tsumoHoraScript |> driveUntil passive GameState.isEnded)

    [<Fact>]
    let ``一局都没有的事件流回放不出对局，且是值不是异常`` () =
        match Replay.game Ruleset.yonma [ StartGame [ "p0"; "p1"; "p2"; "p3" ] ] with
        | Ok _ -> failwith "没有一局的事件流不该回放出一场对局"
        | Error error -> Assert.Equal(ReplayError.NoKyoku, error)
