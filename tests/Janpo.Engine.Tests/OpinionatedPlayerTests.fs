namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 有主见的随机选手（票 42）：**三条主见逐条钉住**，外加两条边界——
/// 它愿意提交的动作恒是合法动作集的非空子集，且它没把均匀随机那个选手动过一个字节。
///
/// 牌山一律是**摊出来的**（`startScripted`），因此「谁在第几巡碰得到什么」是确定的事实，
/// 不靠碰种子。频率那一半（立直从几场涨到几场）在跑批那两条用例里。
module OpinionatedPlayerTests =

    let private pai (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"{notation} 应当是合法记法，却得到 {TileParseError.toDisplay error}"

    /// 这一手在等谁、它能提交什么。响应阶段同时等多家时取指定的那一家。
    let private choiceOf (seat: Seat) (state: GameState) : LegalActions =
        GameState.legalActions state
        |> List.tryFind (fun choice -> choice.Seat = seat)
        |> Option.defaultWith (fun () -> failwith $"座位 {Seat.index seat} 此刻应当被问到")

    /// 正在被问的第一家（响应阶段的裁决顺序与 `Kyoku.run` 同一条）。
    let private asked (state: GameState) : LegalActions =
        match GameState.legalActions state with
        | choice :: _ -> choice
        | [] -> failwith "此刻应当有人被问到"

    let private isHora (action: Action) : bool =
        match action with
        | Action.Hora _ -> true
        | Action.Dahai _
        | Action.Pon _
        | Action.Chi _
        | Action.Ankan _
        | Action.Kakan _
        | Action.Minkan _
        | Action.Riichi _
        | Action.None _
        | Action.Ryuukyoku _ -> false

    /// 合法动作集里已经有人和得了。
    let private someoneCanHora (state: GameState) : bool =
        GameState.legalActions state
        |> List.exists (fun choice -> choice.Actions |> List.exists isHora)

    /// 座位 0 摸进 `drawn` 就把它打出去，停在等人响应的那一步。
    let private atDiscardOf (drawn: string) (script: Script) : GameState =
        match GameState.step (startScripted script) (Action.Dahai(seat 0, pai drawn, true)) with
        | Ok(next, _) -> next
        | Error illegal -> failwith $"这一手应当合法，却被拒：{IllegalAction.toDisplay illegal}"

    // ---- 剧本 ----

    /// 非役牌的鸣：座位 0 第 1 巡摸进 3m 打出去，座位 1 手里三张 3m 加 4m 5m
    /// —— 碰、大明杠与吃**三条都成立**，而三条一个役都不带。
    let private plainNakiScript =
        {
            Hands =
                [
                    "1m 4m 7m 1p 4p 7p 2s 8s 2z 3z 4z 6z 7z"
                    "3m 3m 3m 4m 5m 1p 2p 3p 5s 6s 7s 9p 9s"
                    "2m 5m 8m 2p 5p 8p 3s 6s 9s 1z 2z 3z 4z"
                    "6m 9m 3p 6p 9p 1s 4s 7s 1z 2z 3z 4z 5z"
                ]
            Draws = "3m"
        }

    /// 自风牌的碰：座位 0 第 1 巡摸进 2z（南）打出去，**座位 1 的自风就是南**。
    let private jikazePonScript =
        {
            Hands =
                [
                    "1m 4m 7m 1p 4p 7p 2s 8s 3z 4z 5z 6z 7z"
                    "2z 2z 3m 4m 5m 1p 2p 3p 5s 6s 7s 9p 9s"
                    "2m 5m 8m 2p 5p 8p 3s 6s 9s 1z 3z 4z 5z"
                    "6m 9m 3p 6p 9p 1s 4s 7s 1z 3z 4z 6z 7z"
                ]
            Draws = "2z"
        }

    /// **同一张 2z 换个座位**：座位 2 的自风是西，南对它不是役牌。
    let private otherKazePonScript =
        {
            Hands =
                [
                    "1m 4m 7m 1p 4p 7p 2s 8s 3z 4z 5z 6z 7z"
                    "3m 4m 5m 1p 2p 3p 5s 6s 7s 9p 9s 1z 3z"
                    "2z 2z 2m 5m 8m 2p 5p 8p 3s 6s 9s 1z 4z"
                    "6m 9m 3p 6p 9p 1s 4s 7s 1z 4z 5z 6z 7z"
                ]
            Draws = "2z"
        }

    /// 三元牌的碰：白对四家都是役牌，座位 2 碰得。
    let private sangenPonScript =
        {
            Hands =
                [
                    "1m 4m 7m 1p 4p 7p 2s 8s 2z 3z 4z 6z 7z"
                    "3m 4m 5m 1p 2p 3p 5s 6s 7s 9p 9s 1z 2z"
                    "5z 5z 2m 5m 8m 2p 5p 8p 3s 6s 9s 1z 3z"
                    "6m 9m 3p 6p 9p 1s 4s 7s 1z 2z 4z 6z 7z"
                ]
            Draws = "5z"
        }

    // ---- 主见一：能和就和 ----

    [<Fact>]
    let ``能和就和：自摸和进了动作集，它愿意提交的就只剩这一条`` () =
        let state = startScripted tsumoHoraScript |> driveUntil passive someoneCanHora
        let choice = asked state

        // 那一手同时还有十几条打牌可选，主见把它们全筛掉了。
        Assert.True(List.length choice.Actions > 1, "这一手应当不止和了一条可选")
        Assert.Equal<Action list>([ Action.Hora(seat 0, seat 0, pai "5z") ], OpinionatedPlayer.wanted state choice)

    [<Fact>]
    let ``能和就和：荣和同理，响应阶段不见逃`` () =
        // 座位 0 第 2 巡打出 4p，座位 2 与座位 3 都听它（`passive` 一路摸切，从不宣言和了）。
        let state = startScripted doubleRonScript |> driveUntil passive someoneCanHora
        let choice = asked state

        // 「过」也在动作集里（见逃是合法的），但这个选手不见逃。
        Assert.Contains(Action.None choice.Seat, choice.Actions)
        Assert.Equal<Action list>([ Action.Hora(seat 2, seat 0, pai "4p") ], OpinionatedPlayer.wanted state choice)

    // ---- 主见二：听牌就立直 ----

    [<Fact>]
    let ``听牌就立直：立直宣言进了动作集，它愿意提交的就只剩这一条`` () =
        // 亲摸进 6z：打 1z 就是 6z 单骑听牌，立直宣言因此进了动作集。
        let state = startScripted riichiAnkanScript
        let choice = asked state

        Assert.Contains(Action.Riichi(seat 0), choice.Actions)
        Assert.Equal<Action list>([ Action.Riichi(seat 0) ], OpinionatedPlayer.wanted state choice)

    // ---- 主见三：有役才鸣 ----

    [<Fact>]
    let ``有役才鸣：非役牌的碰、大明杠与吃一条都不要`` () =
        let state = atDiscardOf "3m" plainNakiScript
        let choice = choiceOf (seat 1) state

        // 三条鸣牌都在合法动作集里（引擎照给），这个选手一条都不挑。
        Assert.Equal(4, List.length choice.Actions)
        Assert.Equal<Action list>([ Action.None(seat 1) ], OpinionatedPlayer.wanted state choice)

    [<Fact>]
    let ``有役才鸣：自风牌碰得，同一张牌换个座位就不碰`` () =
        let jikaze = atDiscardOf "2z" jikazePonScript
        let choice = choiceOf (seat 1) jikaze

        // 南是座位 1 的自风：碰完这一组本身就是一番，因此留着（挑不挑由取样定）。
        Assert.Equal<Action list>(
            [ Action.Pon(seat 1, seat 0, pai "2z", tilesOf "2z 2z"); Action.None(seat 1) ],
            OpinionatedPlayer.wanted jikaze choice
        )

        let other = atDiscardOf "2z" otherKazePonScript
        let otherChoice = choiceOf (seat 2) other

        // 同一张牌、同一个碰法，换成自风是西的那家：不碰。
        Assert.Contains(Action.Pon(seat 2, seat 0, pai "2z", tilesOf "2z 2z"), otherChoice.Actions)
        Assert.Equal<Action list>([ Action.None(seat 2) ], OpinionatedPlayer.wanted other otherChoice)

    [<Fact>]
    let ``有役才鸣：三元牌对谁都是役牌`` () =
        let state = atDiscardOf "5z" sangenPonScript
        let choice = choiceOf (seat 2) state

        Assert.Contains(Action.Pon(seat 2, seat 0, pai "5z", tilesOf "5z 5z"), OpinionatedPlayer.wanted state choice)

    // ---- 边界 ----

    [<Fact>]
    let ``愿意提交的那几条恒是合法动作集的非空子集`` () =
        // 主见只会**收窄**动作集，不许凭空造动作，也不许把它掏空——
        // 掏空了牌桌就推不动，造动作了引擎当场拒。逐手验，跑几场。
        let checking: Player<Rng> =
            fun rng state choice ->
                let wanted = OpinionatedPlayer.wanted state choice

                Assert.NotEmpty wanted

                for action in wanted do
                    Assert.Contains(action, choice.Actions)

                OpinionatedPlayer.player rng state choice

        for seed in 1..3 do
            match Game.run checking (Rng.ofSeed seed) (Rng.ofSeed seed) (Game.start ruleset) with
            | Ok _ -> ()
            | Error error -> failwith $"这一场应当跑得完，却得到 {KyokuError.toDisplay error}"

    [<Fact>]
    let ``它是新加的一种选手，均匀随机那个一个字节都没动`` () =
        // 均匀随机是 40 条黄金用例与双目标对拍的基准（票 42 的边界）：
        // `RandomPlayer.uniform` 与 `Kyoku.randomPlayer` 逐手一致这条在 SoakTests 里钉着，
        // 这里钉的是另一半——有主见的那个**确实是另一个选手**，不是把基准改了名。
        let events (player: Player<Rng>) =
            match Game.run player (Rng.ofSeed 42) (Rng.ofSeed 42) (Game.start ruleset) with
            | Ok(game, _, _) -> Game.events game
            | Error error -> failwith $"这一场应当跑得完，却得到 {KyokuError.toDisplay error}"

        Assert.NotEqual<Event list>(events RandomPlayer.uniform, events OpinionatedPlayer.player)

    // ---- 频率：这一票的目的 ----

    [<Fact>]
    let ``跑批：有主见的选手跑一批种子零异常`` () =
        // 牌数守恒、点数与供托之和、合法动作集非空或局已终、回放确定性——
        // 换了选手这四条一条都不能松。
        let report = Soak.run ruleset OpinionatedPlayer.player [ 1..5 ]

        let found = report.Issues |> List.map SoakIssue.toDisplay |> String.concat "\n"

        Assert.True(List.isEmpty report.Issues, $"跑批发现问题：\n{found}")
        Assert.True(report.Kyokus >= 20, $"五场东风战至少 20 局，实际 {report.Kyokus}")

    [<Fact>]
    let ``立直与和了真的发生了：同样五场，均匀随机零次`` () =
        // **这一票的整个理由**（27 号验收 §14.3：1..2000 号种子里只有 15 场立直成立）。
        // 阈值取得比实测低一截，防的是「随机流一动数字就掉」的噪声式红。
        let uniform = Soak.run ruleset RandomPlayer.uniform [ 1..5 ]
        let opinionated = Soak.run ruleset OpinionatedPlayer.player [ 1..5 ]

        Assert.Equal(0, Soak.count Coverage.RiichiAccepted uniform)
        Assert.Equal(0, Soak.count Coverage.Hora uniform)

        Assert.True(
            Soak.count Coverage.RiichiAccepted opinionated >= 10,
            $"五场里立直成立 {Soak.count Coverage.RiichiAccepted opinionated} 次，太少"
        )

        Assert.True(Soak.count Coverage.Hora opinionated >= 5, $"五场里和了 {Soak.count Coverage.Hora opinionated} 次，太少")
