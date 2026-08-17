namespace Janpo.Engine.Tests

open Xunit
open FsCheck.Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo

/// M0 的验收跑批：一批种子连着跑完东风战，**逐手验不变量、逐场数覆盖率**。
///
/// 与 13 票的真实牌谱对拍互补，不重复（备注 N-23）：牌谱一次走到 32 种役、双响、抢杠、
/// 大明杠与役满，那些随机选手走不到；跑批走到的是随机探索下的不变量与「不崩」。
/// **跑批不是正确性的最后一道防线**——13 票修的那个振听 bug 表现为「引擎默默不给荣和」，
/// 跑批只会看到和了偏少，看不出错。
module SoakTests =

    let private ruleset = Ruleset.yonma

    /// 读一个“跑几场”的环境变量，不认的取值一律退回默认。
    /// **不带 `private`**：同一文件里 `RareSoakTests` 那两道扫描用同一套规矩，另抄一份不如共用一份。
    let gamesFrom (variable: string) (fallback: int) : int =
        match System.Environment.GetEnvironmentVariable variable with
        | null -> fallback
        | text ->
            match System.Int32.TryParse text with
            | true, value when value > 0 -> value
            | _ -> fallback

    /// 跑几场。**规模可配置**：`JANPO_SOAK_GAMES=500 dotnet test` 就能把它放大，
    /// CI 用默认值（`Soak.defaultGames`，实测约 10 秒）。
    ///
    /// **它是给放大用的**：调到默认值以下时，稀有的那几种（九种九牌、暗杠）
    /// 可能走不到，覆盖率断言于是不成立——那时该改的是这个变量，不是断言。
    let private games = gamesFrom "JANPO_SOAK_GAMES" Soak.defaultGames

    /// 跑批的种子：`1 .. games`。**跑一次存下来给全部用例共用**——一次跑批十秒起步，
    /// 每条用例各跑一次就没法进 CI 了。`lazy` 而不是模块级的值，理由同 `GameFixtures`。
    let private cached = lazy (Soak.run ruleset RandomPlayer.covering [ 1..games ])

    let private report () : SoakReport = cached.Value

    /// 覆盖率那几行，塞进断言的失败说明里——跑批失败时最想看到的就是这个。
    let private coverage (report: SoakReport) : string =
        SoakReport.coverageLines report |> String.concat " "

    [<Fact>]
    let ``跑批：一批种子连着跑完，零异常、零卡死`` () =
        let report = report ()

        // **失败说明里必须有种子**：跑批的命根子是能复现。
        let found = report.Issues |> List.map SoakIssue.toDisplay |> String.concat "\n"

        Assert.True(
            List.isEmpty report.Issues,
            $"跑批发现问题（复现：`janpo soak <种子> <种子>`；事件流：`janpo game <种子> --covering`）：\n{found}"
        )

        Assert.Equal(games, List.length report.Seeds)
        // 东风战至少四局，连庄再多；手数与局数都该是正的，否则跑批压根没跑。
        Assert.True(report.Kyokus >= games * 4, $"{games} 场东风战至少 {games * 4} 局，实际 {report.Kyokus}")
        Assert.True(report.Turns > report.Kyokus, $"手数 {report.Turns} 不该少于局数 {report.Kyokus}")

    [<Fact>]
    let ``跑批：每一种动作类型与流局形态都至少走到过一次`` () =
        let report = report ()

        Assert.True(
            List.isEmpty (Soak.missing report),
            let missing = Soak.missing report |> List.map Coverage.toMjai |> String.concat " "
            $"这些一次都没走到：{missing}\n覆盖率：{coverage report}"
        )

    [<Fact>]
    let ``跑批：没覆盖到的只有那几种稀到不当闸门的流局形态`` () =
        // **断言的是「没覆盖到的不多于 `Soak.rare`」**：将来真碰到了不算回归，
        // 因此用子集而不是相等；反过来，本该覆盖的形态一旦掉出去就是红。
        let unexpected =
            Soak.uncovered (report ())
            |> List.filter (fun coverage -> not (List.contains coverage Soak.rare))

        Assert.True(
            List.isEmpty unexpected,
            let names = unexpected |> List.map Coverage.toMjai |> String.concat " "
            $"这些形态本该被跑批覆盖，却一次都没走到：{names}"
        )

    [<Fact>]
    let ``流局的每一种形态要么当闸门、要么显式记在稀有名单里`` () =
        // `RyuukyokuReason` 加一个 case 而两份名单都没动，它就会静静地不在覆盖率里——
        // 那正是备注 N-8 说的「没断言过覆盖率的跑批只证明了没崩」。
        for reason in RyuukyokuReason.all do
            Assert.True(
                List.contains (Coverage.Ryuukyoku reason) Soak.all,
                $"流局形态 {RyuukyokuReason.toMjai reason} 既不在 Soak.required 也不在 Soak.rare"
            )

        // 两份名单不重叠：同一种形态不会既当闸门又算稀有。
        Assert.Empty(Soak.required |> List.filter (fun coverage -> List.contains coverage Soak.rare))

    [<Fact>]
    let ``每一种流局形态都说得出归哪道闸门守——含「仍无闸门」那一类`` () =
        // 50 票：三份名单把形态分完且不重叠。**`unguarded` 是一句显式的「它没人守」**，
        // 新加一种流局形态时必须在三份里选一份表态，不能静静地滑过去。
        let lists =
            [
                "required", Soak.required
                "rareByCovering", Soak.rareByCovering
                "rareByOpinionated", Soak.rareByOpinionated
                "unguarded", Soak.unguarded
            ]

        for reason in RyuukyokuReason.all do
            let owners =
                lists
                |> List.filter (fun (_, list) -> List.contains (Coverage.Ryuukyoku reason) list)
                |> List.map fst

            Assert.True(List.length owners = 1, $"流局形态 {RyuukyokuReason.toMjai reason} 归属不清：{owners}")

        // 两道扫描守的那几种，默认规模的跑批碰不到也不算回归（它们在 `rare` 里）。
        Assert.Equal<Coverage list>(Soak.rareByCovering @ Soak.rareByOpinionated @ Soak.unguarded, Soak.rare)

    [<Fact>]
    let ``报出问题的那一场用 janpo game --covering 原样重现`` () =
        // 「失败时输出可复现的种子与事件流」：**种子就是事件流的指针**，
        // 前提是那条复现命令真的跑出同一场。这一条把它钉住：
        // 跑批的逐场驱动与 `Game.run` 只差在逐手的检查上，选中的动作应当一手不差。
        let seed = 7
        let report = Soak.run ruleset RandomPlayer.covering [ seed ]

        match Game.run RandomPlayer.covering (Soak.playerRng seed) (Rng.ofSeed seed) (Game.start ruleset) with
        | Error error -> failwith $"这一场应当跑得完，却得到「{KyokuError.toDisplay error}」"
        | Ok(game, _, _) ->
            let replayed =
                Game.events game
                |> List.countBy Coverage.ofEvent
                |> List.sortBy (fst >> Coverage.toMjai)

            Assert.Equal<(Coverage * int) list>(Soak.frequencies report, replayed)

    [<Fact>]
    let ``跑批：同一批种子跑两次得到同一份报告`` () =
        let seeds = [ 1..3 ]

        Assert.Equal<SoakReport>(
            Soak.run ruleset RandomPlayer.covering seeds,
            Soak.run ruleset RandomPlayer.covering seeds
        )

    [<Fact>]
    let ``均匀取样的随机选手十场里和了 0 次立直 0 次，带偏好的十几次`` () =
        // 备注 N-8 的那组实测写成用例。**差的是频率而不是可能性**：
        // 把规模拉到 100 场 406 局，均匀取样也能碰到 2 次和了与 2 次立直（0.5%/局），
        // 而覆盖型是 164 次（32%/局）。十场这个规模上均匀取样就是 0——
        // 「随机 Player 覆盖全部动作类型」若不加偏好就只是个口号，这一条把它钉住。
        let uniform = Soak.run ruleset RandomPlayer.uniform [ 1..10 ]

        Assert.Empty(uniform.Issues)
        Assert.Equal(0, Soak.count Coverage.Hora uniform)
        Assert.Equal(0, Soak.count Coverage.Riichi uniform)
        Assert.Equal(0, Soak.count Coverage.RiichiAccepted uniform)
        Assert.Equal(0, Soak.count Coverage.Ankan uniform)

        // 而带偏好的那个在同样十场里四样都走到了（实测 17 / 20 / 20 / 6）。
        let covering = Soak.run ruleset RandomPlayer.covering [ 1..10 ]

        Assert.True(Soak.count Coverage.Hora covering > 0)
        Assert.True(Soak.count Coverage.Riichi covering > 0)
        Assert.True(Soak.count Coverage.RiichiAccepted covering > 0)
        Assert.True(Soak.count Coverage.Ankan covering > 0)

    [<Fact>]
    let ``均匀偏好的随机选手与 randomPlayer 逐手一致`` () =
        // 权重全 1 时按权重取样退化成按下标等概率取样，连消耗的随机数都一样——
        // 因此 `RandomPlayer` 不是另起一份随机选手，而是把原来那个包了进去。
        for seed in 1..10 do
            let events (player: Player<Rng>) =
                match Game.run player (Rng.ofSeed seed) (Rng.ofSeed seed) (Game.start ruleset) with
                | Ok(game, _, _) -> Game.events game
                | Error error -> failwith $"这一场应当跑得完，却得到「{KyokuError.toDisplay error}」"

            Assert.Equal<Event list>(events Kyoku.randomPlayer, events RandomPlayer.uniform)

    [<Fact>]
    let ``跑批报告的中文渲染带上规模、覆盖率与未覆盖的形态`` () =
        let rendered =
            SoakReport.toDisplay (Soak.run ruleset RandomPlayer.covering [ 1..2 ])

        Assert.Contains("场数: 2", rendered)
        Assert.Contains("hora=", rendered)
        Assert.Contains("未覆盖: ", rendered)
        Assert.Contains("问题: 无", rendered)

    [<Fact>]
    let ``跑批真的会响：选手交非法动作时报出能复现的种子`` () =
        // 「对拍 0 差异」只有在能证明它会响的时候才是好消息（13 票的同一课）。
        // 摸牌后阶段没有「过」这条动作，交上去必被拒——跑批应当当场报出来。
        // 其余四类不变量（牌数守恒、点数与供托之和、卡死、回放确定性）的变异测试
        // 要改引擎源码，写在 14 票的报告里（五类逐一响过）。
        let cheater: Player<Rng> = fun rng _ choice -> Action.None choice.Seat, rng

        let report = Soak.run ruleset cheater [ 7 ]

        match report.Issues with
        | [ issue ] ->
            Assert.Equal(7, issue.Seed)
            Assert.Equal(1, issue.Kyoku)
            Assert.Equal(1, issue.Turn)
            Assert.Contains("种子 7", SoakIssue.toDisplay issue)
        | other -> failwith $"应当正好报出一条问题，实际是 {List.map SoakIssue.toDisplay other}"

    [<Fact>]
    let ``跑批的问题都说得出种子、局、手与原因`` () =
        let issue =
            {
                Seed = 42
                Kyoku = 3
                Turn = 17
                Fault = SoakFault.TileCount(135, 136)
            }

        let rendered = SoakIssue.toDisplay issue

        Assert.Contains("种子 42", rendered)
        Assert.Contains("第 3 局", rendered)
        Assert.Contains("第 17 手", rendered)
        Assert.Contains("135", rendered)

/// 稀有流局形态那两道闸门（票 50）：**扫到为止的跑批**。
///
/// 上面那一模块的跑批只跑 60 场，于是四家立直（0.73%/场）与四杠散了（0.53%/场）
/// 两条规则路径长期**没有任何跑批闸门**：代码有、剧本用例有，跑批碰不到
/// （四杠散了偶尔碰得到，但那是运气，断言不得）。
///
/// 这里各开一道**走到就停**的扫描，每一种配一个到得了它的选手：
/// 四杠散了配覆盖型偏好（实测扫 7 场）、四家立直配有主见的选手（票 42，实测扫 21 场）。
/// **两个选手互补而不是二选一**：有主见的从不吃（`chi` 恒为 0）、几乎不杠
/// （四杠散了 6000 场 0 次），拿它换掉默认那道就把 `required` 里的几种覆盖丢了。
module RareSoakTests =

    let private ruleset = Ruleset.yonma

    /// 至多扫几场。默认值的频率依据写在 `Soak.rareGames`；
    /// `JANPO_SOAK_RARE_GAMES=6000 dotnet test` 把它放大（夜里想跑大的就用它）。
    let private games = SoakTests.gamesFrom "JANPO_SOAK_RARE_GAMES" Soak.rareGames

    /// 一道扫描：那几种形态**头一次出现在哪个种子**。走 `Soak.firstSeen` 而不是 `Soak.run`：
    /// 上千场的上限上逐手验不变量进不了 CI（实测 52 毫秒/场）。
    /// 不变量那一半由上面那道跑批与 `OpinionatedPlayerTests` 里
    /// 那条「有主见的选手跑一批种子零异常」守着。
    let private scan (player: Player<Rng>) (wanted: Coverage list) : Map<Coverage, int> =
        match Soak.firstSeen ruleset player wanted [ 1..games ] with
        | Ok seen -> seen
        | Error(seed, error) -> failwith $"种子 {seed} 这一场应当跑得完，却得到「{KyokuError.toDisplay error}」"

    /// 一道扫描的断言：想要的那几种形态都在上限之内走到过。
    /// **失败说明里带重跑命令**：扫描与 `janpo soak` 看到的是同一批对局（下面有条用例钉着）。
    let private assertReached (rerun: string) (player: Player<Rng>) (wanted: Coverage list) =
        let seen = scan player wanted

        for coverage in wanted do
            match Map.tryFind coverage seen with
            | Some _ -> ()
            | None -> failwith $"{Coverage.toMjai coverage} 在 {games} 场里一次都没走到（重跑：`{rerun}`）"

    [<Fact>]
    let ``稀有形态：有主见的选手把四家立直走到了`` () =
        assertReached $"janpo soak 1 {games} --opinionated" OpinionatedPlayer.player Soak.rareByOpinionated

    [<Fact>]
    let ``稀有形态：覆盖型偏好的选手扫得够多时把四杠散了走到了`` () =
        // `janpo soak` 不带开关时跑的就是覆盖型偏好，因此重跑命令上没有选手开关。
        assertReached $"janpo soak 1 {games}" RandomPlayer.covering Soak.rareByCovering

    [<Fact>]
    let ``稀有形态：那几种仍然无人能到的形态，两道扫描也没守着`` () =
        // **这一条是反着写的**：它不断言「没走到」（将来真走到了不算回归），
        // 只钉住一件事：`unguarded` 里的形态**不在任何一道跑批的必覆盖名单里**。
        // 把它们混进必覆盖名单假装有覆盖，正是 50 票要消灭的那件事。
        let guarded = Soak.required @ Soak.rareByCovering @ Soak.rareByOpinionated

        for coverage in Soak.unguarded do
            Assert.False(
                List.contains coverage guarded,
                $"{Coverage.toMjai coverage} 记在 unguarded（“仍无闸门”），却又出现在某道跑批的必覆盖名单里"
            )

    [<Fact>]
    let ``扫到为止的快路径与逐手验不变量的跑批看到的是同一批对局`` () =
        // 快路径是那两道闸门的量具，也是失败说明里那句「重跑：`janpo soak ...`」的前提。
        // 逐场跑一遍 `Soak.run`，把每种事实头一次出现在哪个种子算出来，两边逐字对。
        // 两种选手各钉一遍。
        let seeds = [ 1..5 ]

        for player in [ RandomPlayer.covering; OpinionatedPlayer.player ] do
            let verified =
                (Map.empty, seeds)
                ||> List.fold (fun seen seed ->
                    (seen, (Soak.run ruleset player [ seed ]).Counts |> Map.toList)
                    ||> List.fold (fun seen (coverage, _) ->
                        if Map.containsKey coverage seen then
                            seen
                        else
                            Map.add coverage seed seen))

            // `wanted` 给一个跑批产不出的事实（`start_game`），扫描就不会早停，五场全走完。
            Assert.Equal<Result<Map<Coverage, int>, int * KyokuError>>(
                Ok verified,
                Soak.firstSeen ruleset player [ Coverage.StartGame ] seeds
            )

/// 覆盖率的计量单位与 mjai 事件类型的对应。
[<Properties(Arbitrary = [| typeof<EventArbitraries> |])>]
module CoverageProperties =

    [<Property>]
    let ``覆盖率的名字就是 mjai 的事件类型`` (event: Event) =
        // 两处各写一份类型字串（`Event.encoder` 与 `Coverage.toMjai`），这条把它们锁在一起。
        let encoded = Encode.toString 0 (Event.encoder event)
        let label = event |> Coverage.ofEvent |> Coverage.toMjai

        let eventType = Decode.object (fun get -> get.Required.Field "type" Decode.string)

        // 流局的标签缀了形态（`ryukyoku:fanpai`），比的是冒号前那一段。
        Decode.fromString eventType encoded = Ok(label.Split(':') |> Array.head)
