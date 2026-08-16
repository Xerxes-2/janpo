namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 符的黄金用例：面子构成、雀头、听牌型、门清荣和、自摸符、七对子固定符与切上。
///
/// **每条用例都注明依据的规则集**（13 票对拍时才分得出是实现错还是规则集错）。
/// 除非另注，一律依据 `Ruleset.yonma`——它就是天凤的这几项：
/// 连风牌雀头 **4 符**、岭上自摸 **加**自摸符、切上满贯 **关**。
module FuTests =

    let private ruleset = Ruleset.yonma

    /// 场风东、自风南：`1z` 是场风牌、`2z` 是自风牌，`3z` `4z` 都不是役牌。
    let private context = YakuContext.create Ton Nan

    /// 场风东、自风东（亲）：`1z` 是**连风牌**。
    let private oyaContext = YakuContext.create Ton Ton

    /// 无役的牌姿判不出符（`Score.best` 要有役才给读法），用立直凑一个役——立直不加符。
    let private riichi (context: YakuContext) : YakuContext =
        { context with
            Riichi = RiichiDeclaration.Riichi
        }

    let private ron = AgariFixture.ron
    let private tsumo = AgariFixture.tsumo

    /// 点数最高的那种读法算出的符（已切上）。
    let private fuWith (ruleset: Ruleset) (context: YakuContext) (hand: AgariHand) : int =
        match Score.best ruleset context hand with
        | Ok reading -> reading.Value.Fu
        | Error error -> failwith $"应当能和：{YakuError.toDisplay error}"

    let private fu (context: YakuContext) (hand: AgariHand) : int = fuWith ruleset context hand

    /// 符的分项（渲染用）。取的是 `Yaku.detect` 选中的那种读法。
    let private breakdown (context: YakuContext) (hand: AgariHand) : Fu =
        match Yaku.detect ruleset context hand with
        | Ok tally -> Fu.calculate ruleset context hand tally
        | Error error -> failwith $"应当能和：{YakuError.toDisplay error}"

    // ---- 底符与切上 ----

    [<Fact>]
    let ``平和自摸 20 符，平和荣和 30 符`` () =
        // 规则集：`Ruleset.yonma`。四顺子 + 非役牌雀头 + 两面听：自摸不加自摸符（恒 20 符），
        // 荣和加门清荣和 10 符。
        let concealed = "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z"

        Assert.Equal(20, fu context (tsumo [] concealed "9s"))
        Assert.Equal(30, fu context (ron [] concealed "9s"))

    [<Fact>]
    let ``七对子恒 25 符：自摸与门清荣和都不加，也不切上`` () =
        // 规则集：`Ruleset.yonma`（七对子 25 符是通行规则，天凤同）。
        let concealed = "1m 1m 3m 3m 5p 5p 7p 7p 9s 9s 2z 2z 4z 4z"

        Assert.Equal(25, fu context (ron [] concealed "4z"))
        Assert.Equal(25, fu context (tsumo [] concealed "4z"))

    [<Fact>]
    let ``符切上到 10 的倍数，分项原样留着备查`` () =
        // 规则集：`Ruleset.yonma`。底 20 + 单骑 2 + 门清荣和 10 = 32 → 切上 40 符。
        let hand = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "3z"

        Assert.Equal(32, Fu.total (breakdown (riichi context) hand))
        Assert.Equal(40, fu (riichi context) hand)

    // ---- 雀头 ----

    [<Fact>]
    let ``连风牌雀头在天凤是 4 符，单风与三元牌是 2 符`` () =
        // 规则集：`Ruleset.yonma`（连风牌雀头 4 符 = 天凤）。
        // 底 20 + 暗刻 2m（中张）4 + 嵌张 2 + 自摸 2 = 28，雀头 `1z` 加几符正好跨过切上的坎：
        // 连风（亲，自风东）4 符 → 32 → 40 符；只当场风 2 符 → 30 → 30 符。
        let hand = tsumo [] "2m 2m 2m 4p 5p 6p 2s 3s 4s 6s 7s 8s 1z 1z" "5p"

        Assert.Equal(40, fu oyaContext hand)
        Assert.Equal(30, fu context hand)

        // 只算 2 符的规则集：同一手牌落回 30 符。这项是规则集的事，不是写死的。
        let renfonIsTwo = { ruleset with DoubleKazeJantouFu = 2 }

        Assert.Equal(30, fuWith renfonIsTwo oyaContext hand)

    [<Fact>]
    let ``三元牌雀头 2 符，非役牌雀头 0 符`` () =
        // 规则集：`Ruleset.yonma`。底 20 + 嵌张 2 + 门清荣和 10 = 32；雀头是白再加 2 = 34。
        // 两者切上后都是 40 符，因此这里断言的是切上前的分项。
        let sangen = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 5z 5z" "8s"
        let plain = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "8s"

        Assert.Equal(34, Fu.total (breakdown (riichi context) sangen))
        Assert.Equal(32, Fu.total (breakdown (riichi context) plain))

    // ---- 听牌型 ----

    [<Fact>]
    let ``边张 嵌张 单骑各 2 符，两面 0 符`` () =
        // 规则集：`Ruleset.yonma`。四顺子 + 非役牌雀头 + 门清荣和，只有听牌型不同：
        // 两面 20 + 10 = 30 符；边张 / 嵌张 / 单骑各多 2 符 → 32 → 切上 40 符。
        let ryanmen = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "9s"
        let kanchan = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "8s"
        let penchan = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 1s 2s 3s 3z 3z" "3s"
        let tanki = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "3z"

        Assert.Equal(30, fu (riichi context) ryanmen)
        Assert.Equal(40, fu (riichi context) kanchan)
        Assert.Equal(40, fu (riichi context) penchan)
        Assert.Equal(40, fu (riichi context) tanki)

    [<Fact>]
    let ``双碰听 0 符，符全来自补上的那个刻子`` () =
        // 规则集：`Ruleset.yonma`。荣和补上的刻子按**明刻**算（中张明刻 2 符）：
        // 底 20 + 明刻 2 + 双碰 0 + 门清荣和 10 = 32 → 40 符。
        // 同一手牌自摸则补成暗刻（4 符）：20 + 4 + 2 = 26 → 30 符。
        let concealed = "2m 2m 2m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z"

        Assert.Equal(40, fu (riichi context) (ron [] concealed "2m"))
        Assert.Equal(30, fu (riichi context) (tsumo [] concealed "2m"))

    // ---- 面子 ----

    [<Fact>]
    let ``暗刻按幺九翻倍：中张 4 符、幺九 8 符`` () =
        // 规则集：`Ruleset.yonma`。两个暗刻 + 两面听 + 门清荣和：
        // 中张 20 + 4 + 4 + 10 = 38 → 40 符；幺九 20 + 8 + 8 + 10 = 46 → 50 符。
        let chunchan = ron [] "2m 2m 2m 8p 8p 8p 2s 3s 4s 7s 8s 9s 3z 3z" "9s"
        let yaochuu = ron [] "1m 1m 1m 9p 9p 9p 2s 3s 4s 7s 8s 9s 3z 3z" "9s"

        Assert.Equal(40, fu (riichi context) chunchan)
        Assert.Equal(50, fu (riichi context) yaochuu)

    [<Fact>]
    let ``明刻是暗刻的一半：碰出来的中张刻子 2 符`` () =
        // 规则集：`Ruleset.yonma`（食断开着，副露的断幺九成立）。
        // 副露 20 + 明刻 2 = 22 → 30 符；同样构成的门清荣和 20 + 暗刻 4 + 门清荣和 10 = 34 → 40 符。
        let opened =
            ron [ AgariFixture.pon (seat 1) "2m" "2m 2m" ] "4p 5p 6p 2s 3s 4s 5s 5s 6s 7s 8s" "8s"

        let closed = ron [] "2m 2m 2m 4p 5p 6p 2s 3s 4s 5s 5s 6s 7s 8s" "8s"

        Assert.Equal(30, fu context opened)
        Assert.Equal(40, fu context closed)

    [<Fact>]
    let ``杠是刻子的四倍：字牌暗杠 32 符、字牌大明杠 16 符`` () =
        // 规则集：`Ruleset.yonma`。暗杠不破门清：20 + 32 + 门清荣和 10 = 62 → 70 符。
        // 大明杠破门清（场风东的刻子给了役）：20 + 16 = 36 → 40 符。
        let ankan =
            ron [ AgariFixture.ankan "1z 1z 1z 1z" ] "2m 3m 4m 4p 5p 6p 2s 3s 4s 3z 3z" "4m"

        let minkan =
            ron [ AgariFixture.minkan (seat 1) "1z" "1z 1z 1z" ] "2m 3m 4m 4p 5p 6p 2s 3s 4s 3z 3z" "4m"

        Assert.Equal(70, fu (riichi context) ankan)
        Assert.Equal(40, fu context minkan)

    // ---- 副露的平和形 ----

    [<Fact>]
    let ``副露的平和形按 30 符算，不是 20 符`` () =
        // 规则集：`Ruleset.yonma`（副露平和形上调 30 符是通行规则，天凤同；食断开着才有役）。
        let hand =
            ron
                [
                    AgariFixture.chi (seat 3) "3m" "4m 5m"
                    AgariFixture.chi (seat 3) "3p" "4p 5p"
                ]
                "2s 3s 4s 5s 5s 6s 7s 8s"
                "8s"

        Assert.Equal(30, fu context hand)
        Assert.Equal("30 符（底 20 + 副露平和形 10）", Fu.toDisplay (breakdown context hand))

    // ---- 自摸符 ----

    [<Fact>]
    let ``岭上自摸加不加自摸符是规则集的事`` () =
        // 规则集：`Ruleset.yonma`（岭上自摸 **加** 2 符 = 天凤）。
        // 底 20 + 暗杠 2m（中张）16 + 雀头（白）2 + 嵌张 2 = 40，自摸符正好跨过切上的坎：
        // 加 → 42 → 50 符；不加 → 40 符。
        let hand =
            tsumo [ AgariFixture.ankan "2m 2m 2m 2m" ] "4m 5m 6m 5p 6p 7p 2s 3s 4s 5z 5z" "5m"

        let rinshan = { context with Rinshan = true }

        Assert.Equal(50, fu rinshan hand)

        let withoutRinshanFu = { ruleset with RinshanTsumoFu = false }

        Assert.Equal(40, fuWith withoutRinshanFu rinshan hand)

        // 不是岭上的普通自摸不受这个开关影响。
        let plainTsumo = { context with Rinshan = false }

        Assert.Equal(50, fu plainTsumo hand)
        Assert.Equal(50, fuWith withoutRinshanFu plainTsumo hand)

    // ---- 渲染 ----

    [<Fact>]
    let ``符的中文形式把分项原样摊开`` () =
        let pinfu =
            breakdown context (ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "9s")

        Assert.Equal("30 符（底 20 + 门清荣和 10）", Fu.toDisplay pinfu)

        let chiitoitsu =
            breakdown context (ron [] "1m 1m 3m 3m 5p 5p 7p 7p 9s 9s 2z 2z 4z 4z" "4z")

        Assert.Equal("25 符（底 25）", Fu.toDisplay chiitoitsu)

        let ankou =
            breakdown (riichi context) (tsumo [] "1m 1m 1m 5p 6p 7p 2s 3s 4s 7s 8s 9s 1z 1z" "9s")

        // 底 20 + 幺九暗刻 8 + 场风雀头 2 + 自摸 2 = 32 → 切上 40 符。
        Assert.Equal("40 符（底 20 + 面子 8 + 雀头 2 + 自摸 2）", Fu.toDisplay ankou)
