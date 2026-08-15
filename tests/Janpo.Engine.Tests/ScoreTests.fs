namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 点数表与授受的黄金用例：Oya / Ko 的自摸与荣和、满贯以上各级、切上满贯开关、
/// 本场与供托，以及高点法（按**真符**在全部读法里再选一次）。
///
/// **每条用例都注明依据的规则集**。除非另注，一律依据 `Ruleset.yonma`：
/// 切上满贯 **关**（天凤与雀魂段位战都不采用）、一本场 300 点、立直棒 1000 点、
/// 13 番起数え役满。符本身的边界见 FuTests。
module ScoreTests =

    let private ruleset = Ruleset.yonma

    let private value (fu: int) (han: int) : HoraValue = { Fu = fu; Han = han; Yakuman = 0 }

    let private yakuman (multiplier: int) : HoraValue =
        {
            Fu = 30
            Han = 0
            Yakuman = multiplier
        }

    /// 亲固定在座位 0：`ko` 是座位 1 和了、座位 2 放铳，`oya` 是座位 0 和了、座位 1 放铳。
    let private koRon: HoraTransfer =
        {
            Actor = 1
            Target = 2
            Oya = 0
            Honba = 0
            Kyotaku = 0
        }

    let private koTsumo: HoraTransfer = { koRon with Target = 1 }

    let private oyaRon: HoraTransfer = { koRon with Actor = 0; Target = 1 }

    let private oyaTsumo: HoraTransfer = { oyaRon with Target = 0 }

    let private scoreIn (ruleset: Ruleset) (transfer: HoraTransfer) (value: HoraValue) : HoraScore =
        Score.hora ruleset transfer value

    let private points (transfer: HoraTransfer) (value: HoraValue) : int =
        (scoreIn ruleset transfer value).HoraPoints

    let private deltas (transfer: HoraTransfer) (value: HoraValue) : int list = (scoreIn ruleset transfer value).Deltas

    // ---- 满贯以下的点数表 ----

    [<Fact>]
    let ``子荣和的点数表`` () =
        // 规则集：`Ruleset.yonma`。基本点 = 符 × 2^(2+番)，子荣和 4 倍，每笔切上到 100。
        Assert.Equal(1000, points koRon (value 30 1))
        Assert.Equal(2000, points koRon (value 30 2))
        Assert.Equal(3900, points koRon (value 30 3))
        Assert.Equal(7700, points koRon (value 30 4))
        Assert.Equal(1300, points koRon (value 40 1))
        Assert.Equal(2600, points koRon (value 40 2))
        Assert.Equal(5200, points koRon (value 40 3))
        Assert.Equal(1600, points koRon (value 50 1))
        Assert.Equal(6400, points koRon (value 50 3))
        Assert.Equal(4500, points koRon (value 70 2))
        // 七对子的 25 符照样进同一张表。
        Assert.Equal(1600, points koRon (value 25 2))
        Assert.Equal(3200, points koRon (value 25 3))

    [<Fact>]
    let ``亲荣和是子的 1 点 5 倍`` () =
        // 规则集：`Ruleset.yonma`。亲荣和 6 倍基本点。
        Assert.Equal(1500, points oyaRon (value 30 1))
        Assert.Equal(2900, points oyaRon (value 30 2))
        Assert.Equal(5800, points oyaRon (value 30 3))
        Assert.Equal(11600, points oyaRon (value 30 4))
        Assert.Equal(7700, points oyaRon (value 40 3))

    [<Fact>]
    let ``子自摸：亲付两份、子各付一份`` () =
        // 规则集：`Ruleset.yonma`。20 符 2 番 = 400 / 700；30 符 4 番 = 2000 / 3900。
        Assert.Equal<int list>([ -700; 1500; -400; -400 ], deltas koTsumo (value 20 2))
        Assert.Equal(1500, points koTsumo (value 20 2))

        Assert.Equal<int list>([ -2000; 4000; -1000; -1000 ], deltas koTsumo (value 30 3))
        Assert.Equal<int list>([ -3900; 7900; -2000; -2000 ], deltas koTsumo (value 30 4))

    [<Fact>]
    let ``亲自摸：三家各付两份`` () =
        // 规则集：`Ruleset.yonma`。30 符 3 番 = 2000 all；40 符 4 番封顶满贯 = 4000 all。
        Assert.Equal<int list>([ 6000; -2000; -2000; -2000 ], deltas oyaTsumo (value 30 3))
        Assert.Equal<int list>([ 12000; -4000; -4000; -4000 ], deltas oyaTsumo (value 40 4))

    // ---- 满贯以上 ----

    [<Fact>]
    let ``满贯以上各级：子与亲`` () =
        // 规则集：`Ruleset.yonma`（13 番起数え役满）。
        let table =
            [
                5, Limit.Mangan, 8000, 12000
                6, Limit.Haneman, 12000, 18000
                7, Limit.Haneman, 12000, 18000
                8, Limit.Baiman, 16000, 24000
                10, Limit.Baiman, 16000, 24000
                11, Limit.Sanbaiman, 24000, 36000
                12, Limit.Sanbaiman, 24000, 36000
                13, Limit.Kazoe, 32000, 48000
                26, Limit.Kazoe, 32000, 48000
            ]

        for han, expected, ko, oya in table do
            Assert.Equal(expected, Score.limit ruleset (value 30 han))
            Assert.Equal(ko, points koRon (value 30 han))
            Assert.Equal(oya, points oyaRon (value 30 han))

    [<Fact>]
    let ``满贯的自摸授受`` () =
        // 规则集：`Ruleset.yonma`。子满贯自摸 2000 / 4000，亲满贯自摸 4000 all。
        Assert.Equal<int list>([ -4000; 8000; -2000; -2000 ], deltas koTsumo (value 30 5))
        Assert.Equal<int list>([ 12000; -4000; -4000; -4000 ], deltas oyaTsumo (value 30 5))
        // 跳满：子 3000 / 6000。
        Assert.Equal<int list>([ -6000; 12000; -3000; -3000 ], deltas koTsumo (value 30 6))

    [<Fact>]
    let ``基本点封顶到满贯：40 符 4 番与 70 符 3 番都是满贯`` () =
        // 规则集：`Ruleset.yonma`。40 符 4 番 = 2560、70 符 3 番 = 2240，都超过 2000 封顶。
        Assert.Equal(Limit.Mangan, Score.limit ruleset (value 40 4))
        Assert.Equal(8000, points koRon (value 40 4))
        Assert.Equal(Limit.Mangan, Score.limit ruleset (value 70 3))
        Assert.Equal(8000, points koRon (value 70 3))
        // 30 符 4 番的基本点是 1920，够不着封顶：切上满贯关着时它就是 7700。
        Assert.Equal(Limit.Normal, Score.limit ruleset (value 30 4))

    [<Fact>]
    let ``役满按倍数算，复合役满按倍数叠加`` () =
        // 规则集：`Ruleset.yonma`。单倍役满：子 32000、亲 48000。
        Assert.Equal(Limit.Yakuman, Score.limit ruleset (yakuman 1))
        Assert.Equal(32000, points koRon (yakuman 1))
        Assert.Equal(48000, points oyaRon (yakuman 1))
        Assert.Equal<int list>([ -16000; 32000; -8000; -8000 ], deltas koTsumo (yakuman 1))
        Assert.Equal<int list>([ 48000; -16000; -16000; -16000 ], deltas oyaTsumo (yakuman 1))

        // 复合役满（07 只产单倍，倍数叠加是役满复合的事）。
        Assert.Equal(64000, points koRon (yakuman 2))
        Assert.Equal(96000, points oyaRon (yakuman 2))

    // ---- 切上满贯 ----

    [<Fact>]
    let ``切上满贯默认关，打开后 4 番 30 符与 3 番 60 符上调为满贯`` () =
        // 规则集：默认 `Ruleset.yonma`（切上满贯 **关**，天凤与雀魂段位战都不采用）；
        // 对照组是 `Ruleset.withKiriageMangan`。两者只有基本点 1920 那一档不同。
        Assert.False(ruleset.KiriageMangan)

        let kiriage = Ruleset.withKiriageMangan ruleset

        Assert.Equal(7700, points koRon (value 30 4))
        Assert.Equal(7700, points koRon (value 60 3))
        Assert.Equal(8000, (scoreIn kiriage koRon (value 30 4)).HoraPoints)
        Assert.Equal(8000, (scoreIn kiriage koRon (value 60 3)).HoraPoints)
        Assert.Equal(Limit.Mangan, Score.limit kiriage (value 30 4))
        Assert.Equal(12000, (scoreIn kiriage oyaRon (value 30 4)).HoraPoints)

        // 别的档一点都不受影响。
        Assert.Equal(points koRon (value 30 3), (scoreIn kiriage koRon (value 30 3)).HoraPoints)
        Assert.Equal(points koRon (value 40 3), (scoreIn kiriage koRon (value 40 3)).HoraPoints)

    // ---- 本场与供托 ----

    [<Fact>]
    let ``荣和的本场由放铳者一家付，供托归和了者`` () =
        // 规则集：`Ruleset.yonma`（一本场 300 点、立直棒 1000 点）。
        let transfer = { koRon with Honba = 2; Kyotaku = 1 }
        let score = scoreIn ruleset transfer (value 30 1)

        // 和了点不含本场与供托。
        Assert.Equal(1000, score.HoraPoints)
        Assert.Equal<int list>([ 0; 2600; -1600; 0 ], score.Deltas)
        // 增减之和 = 供托点数（和了者把立直棒收走了）。
        Assert.Equal(1000, List.sum score.Deltas)

    [<Fact>]
    let ``自摸的本场由付家平摊`` () =
        // 规则集：`Ruleset.yonma`。2 本场 = 每家多付 200；1 根立直棒 = 1000 点归和了者。
        let transfer = { koTsumo with Honba = 2; Kyotaku = 1 }
        let score = scoreIn ruleset transfer (value 30 1)

        Assert.Equal(1100, score.HoraPoints)
        Assert.Equal<int list>([ -700; 2700; -500; -500 ], score.Deltas)
        Assert.Equal(1000, List.sum score.Deltas)

    [<Fact>]
    let ``没有本场与供托时授受之和为零`` () =
        Assert.Equal(0, List.sum (deltas koRon (value 30 3)))
        Assert.Equal(0, List.sum (deltas koTsumo (value 30 3)))
        Assert.Equal(0, List.sum (deltas oyaRon (value 30 3)))
        Assert.Equal(0, List.sum (deltas oyaTsumo (value 30 3)))

    // ---- 高点法 ----

    [<Fact>]
    let ``同一手牌的多种读法里取点数最高的那一种`` () =
        // 规则集：`Ruleset.yonma`。场风东、自风南。
        // `111m 222m 333m` 既能拆三个暗刻（三暗刻 2 番、50 符）也能拆三个 `123m`
        // （平和 + 一杯口 2 番、30 符）：番数一样，取符高的那一种。
        let context = YakuContext.create Ton Nan
        let hand = AgariFixture.ron [] "1m 1m 1m 2m 2m 2m 3m 3m 3m 4p 5p 6p 9s 9s" "6p"

        match Score.best ruleset context hand with
        | Error error -> failwith $"应当能和：{YakuError.toDisplay error}"
        | Ok reading ->
            Assert.Contains(Yaku.Sanankou, YakuTally.yaku reading.Tally)
            Assert.DoesNotContain(Yaku.Pinfu, YakuTally.yaku reading.Tally)
            Assert.Equal<HoraValue>({ Fu = 50; Han = 2; Yakuman = 0 }, reading.Value)
            Assert.Equal(3200, points koRon reading.Value)

    [<Fact>]
    let ``取的是点数最高的读法，不是番数最高的读法`` () =
        // 高点法比的是点数：全部读法里没有一种能算出比选中的更多的点。
        let context = YakuContext.create Ton Nan
        let hand = AgariFixture.ron [] "1m 1m 1m 2m 2m 2m 3m 3m 3m 4p 5p 6p 9s 9s" "6p"

        let best =
            match Score.best ruleset context hand with
            | Ok reading -> points koRon reading.Value
            | Error error -> failwith $"应当能和：{YakuError.toDisplay error}"

        let candidates =
            Yaku.candidates ruleset context hand
            |> List.map (fun tally -> points koRon (HoraValue.ofTally ruleset context hand tally))

        Assert.NotEmpty(candidates)
        Assert.All(candidates, (fun one -> Assert.True(one <= best)))

    [<Fact>]
    let ``和不了的牌姿算不出点数：不成型与无役分得开`` () =
        let context = YakuContext.create Ton Nan

        // 型成立但一个役都没有。
        let noYaku = AgariFixture.ron [] "1m 2m 3m 5p 6p 7p 3s 4s 5s 7s 8s 9s 1z 1z" "2m"
        Assert.Equal(Error YakuError.NoYaku, Score.best ruleset context noYaku |> Result.map (fun one -> one.Value))

        // 根本不成和了型。
        let notAgari = AgariFixture.ron [] "1m 2m 3m 5p 6p 7p 3s 4s 5s 7s 9s 1z 2z 3z" "2m"

        Assert.Equal(Error YakuError.NotAgari, Score.best ruleset context notAgari |> Result.map (fun one -> one.Value))

    // ---- 渲染 ----

    [<Fact>]
    let ``点数的中文形式`` () =
        Assert.Equal("", Limit.toDisplay Limit.Normal)
        Assert.Equal("满贯", Limit.toDisplay Limit.Mangan)
        Assert.Equal("数え役满", Limit.toDisplay Limit.Kazoe)

        Assert.Equal("40 符 3 番 5200 点", HoraScore.toDisplay (scoreIn ruleset koRon (value 40 3)))
        Assert.Equal("30 符 5 番 满贯 8000 点", HoraScore.toDisplay (scoreIn ruleset koRon (value 30 5)))
        Assert.Equal("役满 32000 点", HoraScore.toDisplay (scoreIn ruleset koRon (yakuman 1)))
        Assert.Equal("2 倍役满 64000 点", HoraScore.toDisplay (scoreIn ruleset koRon (yakuman 2)))
