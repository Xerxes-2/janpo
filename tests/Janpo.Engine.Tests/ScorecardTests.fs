namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 终局记分卡的逐席聚合（票 133）。
///
/// 这里钉的是**牌谱本身答得出的那几格**：和了 / 放铳 / 兜底 / 重试 / 按席的 token。
/// 页面上那张表的每一格都读它，因此「四家之间可比」这句话有一个可执行的右侧。
///
/// **两处最容易做错的地方各有一条用例**：自摸时 mjai 把 `Hora.Target` 写成和了者自己
/// （不减掉的话每次自摸都给自己记一笔放铳），以及 `Attempts` 是**问过几次**
/// （首问不算重试，一条 `Attempts = 1` 的记录贡献 0 次重试）。
module ScorecardTests =

    /// 一条最省的决策记录：只有这几条用例真正读的那几格是活的。
    let private record (turn: int) (index: int) : DecisionRecord =
        {
            Turn = turn
            Seat = seat index
            PromptTail = ""
            RenderVersion = ""
            ActionIds = []
            Output = ""
            Reason = None
            Thinking = None
            Attempts = 1
            LatencyMs = 0
            Applied = None
            Fallback = None
            Usage = None
        }

    /// 一条和了事件。`target = actor` 就是自摸（mjai 的约定）。
    let private hora (actor: int) (target: int) : Event =
        Hora
            {
                Actor = seat actor
                Target = seat target
                Pai = Tile.parse "1m" |> Result.defaultWith (fun _ -> failwith "1m 应当认得出来")
                Fu = 30
                Fan = 1
                HoraPoints = 1000
                Deltas = [ 0; 0; 0; 0 ]
                Scores = [ 25000; 25000; 25000; 25000 ]
                UraDoraMarkers = []
            }

    let private at (index: int) (tallies: SeatTally list) : SeatTally = tallies |> List.item index

    [<Fact>]
    let ``四家各一行，按座位升序`` () =
        let tallies = Scorecard.tally Ruleset.yonma [] []

        Assert.Equal(4, List.length tallies)
        Assert.Equal<Seat list>(seats [ 0; 1; 2; 3 ], tallies |> List.map (fun tally -> tally.Seat))

    [<Fact>]
    let ``荣和记在和了者头上，放铳记在点炮那家头上`` () =
        let tallies = Scorecard.tally Ruleset.yonma [ hora 1 3 ] []

        Assert.Equal(1, (at 1 tallies).Hora)
        Assert.Equal(0, (at 1 tallies).HoraTargeted)
        Assert.Equal(0, (at 3 tallies).Hora)
        Assert.Equal(1, (at 3 tallies).HoraTargeted)

    [<Fact>]
    let ``自摸只记和了，不给自己记一笔放铳`` () =
        // mjai 自摸时 `Target` 等于 `Actor`：照字面数就会两边各加一笔。
        let tallies = Scorecard.tally Ruleset.yonma [ hora 2 2 ] []

        Assert.Equal(1, (at 2 tallies).Hora)
        Assert.Equal(0, (at 2 tallies).HoraTargeted)

    [<Fact>]
    let ``双响时两家各记各的和了，点炮那家记两笔放铳`` () =
        let tallies = Scorecard.tally Ruleset.yonma [ hora 0 3; hora 1 3 ] []

        Assert.Equal(1, (at 0 tallies).Hora)
        Assert.Equal(1, (at 1 tallies).Hora)
        Assert.Equal(2, (at 3 tallies).HoraTargeted)

    [<Fact>]
    let ``兜底与重试各按席数，首问不算重试`` () =
        let records =
            [
                { record 0 1 with
                    Fallback = Some "端点没答话"
                }
                { record 1 1 with Attempts = 3 }
                { record 2 2 with Attempts = 1 }
            ]

        let tallies = Scorecard.tally Ruleset.yonma [] records

        Assert.Equal(2, (at 1 tallies).Asked)
        Assert.Equal(1, (at 1 tallies).Fallbacks)
        Assert.Equal(2, (at 1 tallies).Retries)
        Assert.Equal(1, (at 2 tallies).Asked)
        Assert.Equal(0, (at 2 tallies).Fallbacks)
        Assert.Equal(0, (at 2 tallies).Retries)
        // 一手都没被问过的那两席：三个数都是 0，而不是一条都没有。
        Assert.Equal(0, (at 0 tallies).Asked)
        Assert.Equal(0, (at 3 tallies).Asked)

    [<Fact>]
    let ``token 按席相加，没有账单的那几手按 0 算`` () =
        let billed (input: int) (output: int) (cacheRead: int) : Usage =
            {
                Input = input
                Output = output
                CacheRead = cacheRead
                CacheWrite = 0
            }

        let records =
            [
                { record 0 0 with
                    Usage = Some(billed 100 20 300)
                }
                { record 1 0 with Usage = None }
                { record 2 0 with
                    Usage = Some(billed 7 3 0)
                }
                { record 3 1 with
                    Usage = Some(billed 5 5 5)
                }
            ]

        let tallies = Scorecard.tally Ruleset.yonma [] records

        Assert.Equal(107, (at 0 tallies).Usage.Input)
        Assert.Equal(23, (at 0 tallies).Usage.Output)
        Assert.Equal(300, (at 0 tallies).Usage.CacheRead)
        // 输入侧合计走 `Usage.promptTokens`（付全价的 + 命中的 + 写缓存的），与账单行同一口径。
        Assert.Equal(407, Usage.promptTokens (at 0 tallies).Usage)
        Assert.Equal(10, Usage.promptTokens (at 1 tallies).Usage)
        Assert.Equal(0, Usage.promptTokens (at 2 tallies).Usage)

    [<Fact>]
    let ``四行相加就是这份牌谱的总账`` () =
        let records =
            [
                { record 0 0 with
                    Usage =
                        Some
                            {
                                Input = 10
                                Output = 1
                                CacheRead = 2
                                CacheWrite = 3
                            }
                }
                { record 1 2 with
                    Usage =
                        Some
                            {
                                Input = 20
                                Output = 2
                                CacheRead = 4
                                CacheWrite = 6
                            }
                }
            ]

        let total = Scorecard.tally Ruleset.yonma [] records |> Scorecard.totalUsage

        Assert.Equal(30, total.Input)
        Assert.Equal(3, total.Output)
        Assert.Equal(6, total.CacheRead)
        Assert.Equal(9, total.CacheWrite)

    [<Fact>]
    let ``三麻只出三行`` () =
        let sanma = { Ruleset.yonma with SeatCount = 3 }

        Assert.Equal(3, Scorecard.tally sanma [] [] |> List.length)

    [<Fact>]
    let ``真打完一场：和了数与事件流里的和了条数对得上`` () =
        // 手捏的事件流证明不了「这几个 case 分得对」，真语料才证明得了（判据 3）。
        //
        // **选手是「有主见」那个，不是均匀随机**（票 42 立它的理由在这里又用了一次）：
        // 均匀随机几乎不和了——1..400 号种子的 400 场里一共只有 2 次自摸、4 次荣和，
        // 而且没有任何一颗种子同时出现两种，「自摸不记放铳」在那样的语料上恒空转（判据 3）。
        // 种子 2 这一场有 3 次自摸 + 1 次荣和，两条路都真的走得到。
        let game =
            match Game.run OpinionatedPlayer.player (Rng.ofSeed 2) (Rng.ofSeed 2) (Game.start Ruleset.yonma) with
            | Ok(game, _, _) -> game
            | Error error -> failwith $"种子 2 应当打得完，却得到「{KyokuError.toDisplay error}」"

        let events = Game.events game

        let paifu =
            Paifu.create Ruleset.yonma (StartGame [ "a"; "b"; "c"; "d" ] :: events) [] Prompting.empty

        let tallies = Scorecard.ofPaifu paifu

        // 闸门那一侧自己数一遍（判据 6：右侧不许是同一个实现）：和了与荣和各数各的。
        let counted (chosen: Hora -> bool) =
            events
            |> List.sumBy (fun event ->
                match event with
                | Hora fields when chosen fields -> 1
                | _ -> 0)

        let horas = counted (fun _ -> true)
        let rons = counted (fun fields -> fields.Target <> fields.Actor)
        let tsumos = horas - rons

        Assert.True(horas > 0, "种子 2 这一场应当有人和了，否则下面几条断言在空转")
        Assert.True(tsumos > 0, "种子 2 这一场应当有人自摸，否则「自摸不记放铳」在这一局空转")
        Assert.Equal(horas, tallies |> List.sumBy (fun tally -> tally.Hora))
        // **放铳数恰好等于荣和数**：自摸那几次谁也没放铳。
        Assert.Equal(rons, tallies |> List.sumBy (fun tally -> tally.HoraTargeted))
