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

    [<Fact>]
    let ``从任何一处截断，回放吐出来的事件都是喂进去那份的前缀`` () =
        // 票 85：末尾截断的事件流停在「下一步该摸牌了」那种相位上时，引擎会从**推断出来的牌山**
        // 自己摸一张（摸牌不是 `Action`，看事件类型看不出来）——而那一张牌谱里根本没有。
        //
        // **这一条只拿喂进去的那份事件流当参照物**：回放吐出来的每一条都要在它里对得上。
        // 它不需要牌山，也不拿第二份实现当期望值，**因此造不出一张牌、也造不出一条事件**
        // （判据 18：票 79 的教训是第三个锚点自己也可能会造数据，要选一个「造不出数据」的）。
        let full = Game.events (played 2088)
        let total = List.length full

        // `end_kyoku` / `end_game` 是 `Game.events` 按这一场的**进程**写出来的（不是 fold 出来的事件）：
        // 截在一局收尾那一条上时它们照样会被补上。**两样都不带牌**，因此比前缀时两边都摘掉。
        let moves = List.filter (fun event -> event <> EndKyoku && event <> EndGame)

        // 阳性对照（判据 3）：这份语料里真有那么多个切点截在「下一条就是自摸」的位置上
        // ——引擎正是在这些位置上自己摸牌的，一个都没有的话下面那条断言永远开不了口。
        let beforeDraw =
            full
            |> List.sumBy (fun event ->
                match event with
                | Tsumo _ -> 1
                | _ -> 0)

        Assert.True(beforeDraw > 100, $"这份语料只有 {beforeDraw} 个切点截在自摸前，太少")

        // 从 2 起：只截下一条 `start_kyoku` 时连 Oya 的第一次自摸都不在，那一条由下面那条用例钉。
        for cut in 2..total do
            let truncated = List.truncate cut full

            match Replay.game Ruleset.yonma truncated with
            | Error error -> failwith $"截到第 {cut} 条应当回放得动，却得到「{ReplayError.toDisplay error}」"
            | Ok replayed ->
                let events = moves (Replayed.events replayed)
                let expected = moves truncated

                Assert.True(
                    List.length events <= List.length expected,
                    $"截到第 {cut} 条，回放却吐出了 {List.length events} 条——多出来的是牌谱里没有的事件"
                )

                Assert.Equal<Event list>(List.truncate (List.length events) expected, events)

    [<Fact>]
    let ``只有一条 start_kyoku 的事件流摆不出开局：Oya 的第一次自摸不在里面`` () =
        // 开局那一刻引擎必给 Oya 摸一张（`GameState.startFrom`），而那一张只有紧跟着的
        // `tsumo` 交代得出来；事件流到 `start_kyoku` 为止时，它只能从推断的牌山里取（票 85）。
        // **宁可一局都不给，也不给人看一张牌谱里不存在的牌。**
        let opening = Game.events (played 2088) |> List.truncate 1

        Assert.True(
            opening
            |> List.forall (fun event ->
                match event with
                | StartKyoku _ -> true
                | _ -> false)
        )

        match Replay.game Ruleset.yonma opening with
        | Ok _ -> failwith "连 Oya 的第一次自摸都没有的事件流不该回放出一局"
        | Error error -> Assert.Equal(ReplayError.NoKyoku, error)

    [<Fact>]
    let ``中间某局只有一条 start_kyoku：报得出是哪一局`` () =
        // 「连开局都交代不出来」只允许是**最后一局**（事件流到此为止，ADR-0002）；
        // 后面还有别的局就说明这份事件流自己不自洽——这一支得真有人走到过（判据 3）。
        let full = Game.events (played 2088)

        let boundaries =
            full
            |> List.indexed
            |> List.filter (fun (_, event) -> event = EndKyoku)
            |> List.map fst

        Assert.True(List.length boundaries >= 3, "这份语料要至少三局才摆得出「中间那一局」")

        // 头一局照旧；把第二局削成光秃秃的一条 `start_kyoku`；第三局起原样接上。
        let broken =
            List.truncate (boundaries[0] + 2) full
            @ [ EndKyoku ]
            @ List.skip (boundaries[1] + 1) full

        match Replay.game Ruleset.yonma broken, Replay.trace Ruleset.yonma broken with
        | Error one, Error other ->
            Assert.Equal(one, other)
            Assert.Equal(ReplayError.Stranded(1, "这一局只有一条 start_kyoku，后面却还有别的局"), one)
        | one, other -> failwith $"这份事件流两个出口都该红，却得到 {one} / {other}"

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

    // ---- 逐手轨迹（票 71） ----

    let private traced (ruleset: Ruleset) (events: Event list) : ReplayKyoku list =
        match Replay.trace ruleset events with
        | Ok kyokus -> kyokus
        | Error error -> failwith $"这份事件流应当走得出轨迹，却得到「{ReplayError.toDisplay error}」"

    [<Fact>]
    let ``逐手轨迹交回引擎，走出的事件流与 fold 出来的那一份逐条相同`` () =
        // `trace` 是 fold 的**旁白**，不是第二条路：照着它一条条 `step`，
        // 得到的必须与 `Replay.game` 那一份逐条相同。页面把牌谱摆成逐帧牌桌靠的就是这一条。
        for seed in [ 2088; 1177; 4242 ] do
            let events = Game.events (played seed)

            let walked =
                traced Ruleset.yonma events
                |> List.collect (fun kyoku ->
                    let ended = kyoku.Actions |> List.fold stepped kyoku.Opening
                    GameState.events ended @ [ EndKyoku ])

            // `Game.events` 在末尾多一条 `end_game`（它不属于任何一局）。
            Assert.Equal<Event list>(events, walked @ [ EndGame ])

    [<Fact>]
    let ``轨迹一局一段，段数与 fold 出来的局数相同`` () =
        let hanchan = Ruleset.yonma |> Ruleset.withLength Hanchan

        let original =
            match Game.runRandom hanchan (Rng.ofSeed 7) with
            | Ok(game, _) -> game
            | Error error -> failwith $"这一场应当打得完，却得到「{KyokuError.toDisplay error}」"

        let kyokus = traced hanchan (Game.events original)

        Assert.Equal(List.length (Game.played original), List.length kyokus)
        // **「过」也在轨迹里**：它不产出事件，因此段里的动作数必然多于这一局的事件数之类的
        // 粗略换算都不成立——这一条只钉「每一段都真有动作」。
        Assert.All(kyokus, fun kyoku -> Assert.NotEmpty kyoku.Actions)

    [<Fact>]
    let ``轨迹的开局局面就是这一局的开头那几条事件`` () =
        // 回放的开局不走随机流（牌山由事件流重建）。每一段的 `Opening` 必须是
        // 「这一局还没人出手」那一刻：它的事件流是整局事件流的**前缀**，且局还没终。
        let events = Game.events (played 2088)

        for kyoku in traced Ruleset.yonma events do
            Assert.False(GameState.isEnded kyoku.Opening)

            let opening = GameState.events kyoku.Opening
            let ended = kyoku.Actions |> List.fold stepped kyoku.Opening

            Assert.NotEmpty opening
            Assert.Equal<Event list>(opening, GameState.events ended |> List.truncate (List.length opening))

    [<Fact>]
    let ``轨迹与 fold 共用同一条路：中间某局没走完，两个出口报同一个错`` () =
        // 两个出口都从 `folded` 出来，因此这份自相矛盾的事件流在两边都该红，且红在同一处。
        let full = Game.events (played 2088)

        // 把第一局的末尾几手削掉，后面几局照旧：中间那一局因此走不完。
        let boundary = full |> List.findIndex ((=) EndKyoku)

        let broken = List.truncate (boundary - 2) full @ List.skip boundary full

        match Replay.game Ruleset.yonma broken, Replay.trace Ruleset.yonma broken with
        | Error one, Error other -> Assert.Equal(one, other)
        | one, other -> failwith $"这份事件流两个出口都该红，却得到 {one} / {other}"
