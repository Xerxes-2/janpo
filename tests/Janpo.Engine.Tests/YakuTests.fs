namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 役种判定的黄金用例：spec 范围内每个役至少一条，外加复合与互斥关系。
/// 随机不变量见 YakuProperties。
module YakuTests =

    let private ruleset = Ruleset.yonma

    /// 场风东、自风南：`1z` 是场风牌、`2z` 是自风牌，`3z` `4z` 都不是役牌。
    let private context = YakuContext.create Ton Nan

    let private ron = AgariFixture.ron
    let private tsumo = AgariFixture.tsumo

    let private detect (context: YakuContext) (hand: AgariHand) : YakuTally =
        match Yaku.detect ruleset context hand with
        | Ok tally -> tally
        | Error error -> failwith $"应当能和：{YakuError.toDisplay error}"

    /// 成立的役，按 `Yaku` 的声明顺序。
    let private yaku (context: YakuContext) (hand: AgariHand) : Yaku list = detect context hand |> YakuTally.yaku

    /// 役的番数合计，不含宝牌。
    let private han (context: YakuContext) (hand: AgariHand) : int = detect context hand |> YakuTally.han

    /// 役满倍数合计。
    let private yakuman (context: YakuContext) (hand: AgariHand) : int =
        detect context hand |> YakuTally.yakuman

    // ---- 门清限定的 1 番役 ----

    [<Fact>]
    let ``立直只在宣言过立直时成立`` () =
        // 嵌张听 8s：不成平和，役表里只剩立直本身。
        let hand = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "8s"

        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset context hand)

        let declared =
            { context with
                Riichi = RiichiDeclaration.Riichi
            }

        Assert.Equal<Yaku list>([ Yaku.Riichi ], yaku declared hand)
        Assert.Equal(1, han declared hand)

    [<Fact>]
    let ``两立直是 2 番且不与立直同时成立`` () =
        let hand = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "8s"

        let declared =
            { context with
                Riichi = RiichiDeclaration.DoubleRiichi
            }

        Assert.Equal<Yaku list>([ Yaku.DoubleRiichi ], yaku declared hand)
        Assert.Equal(2, han declared hand)

    [<Fact>]
    let ``一发要以立直为前提`` () =
        let hand = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "8s"

        let riichi =
            { context with
                Riichi = RiichiDeclaration.Riichi
                Ippatsu = true
            }

        Assert.Equal<Yaku list>([ Yaku.Riichi; Yaku.Ippatsu ], yaku riichi hand)

        // 没立直却把一发标志打开：不成立。
        let noRiichi = { context with Ippatsu = true }
        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset noRiichi hand)

    [<Fact>]
    let ``门前清自摸和要门清且自摸`` () =
        let concealed = "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z"
        Assert.Equal<Yaku list>([ Yaku.MenzenTsumo ], yaku context (tsumo [] concealed "8s"))
        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset context (ron [] concealed "8s"))

        // 副露之后自摸不成役。
        let opened =
            tsumo [ AgariFixture.chi (seat 3) "2m" "3m 4m" ] "5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "8s"

        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset context opened)

    [<Fact>]
    let ``平和要四顺子加两面听且雀头不是役牌`` () =
        // 78s 两面听 9s。
        Assert.Equal<Yaku list>([ Yaku.Pinfu ], yaku context (ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "9s"))

        // 同一手牌换成嵌张 8s：不是平和。
        Assert.Equal(
            Error YakuError.NoYaku,
            Yaku.detect ruleset context (ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "8s")
        )

        // 雀头是场风牌：不是平和。
        Assert.Equal(
            Error YakuError.NoYaku,
            Yaku.detect ruleset context (ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 1z 1z" "9s")
        )

    [<Fact>]
    let ``一杯口是门清限定`` () =
        let concealed = "2m 3m 4m 2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z"
        Assert.Equal<Yaku list>([ Yaku.Iipeiko ], yaku context (ron [] concealed "8s"))

        // 同样的牌形，把一副顺子鸣出来就不再是一杯口。
        let opened =
            ron [ AgariFixture.chi (seat 3) "2m" "3m 4m" ] "2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "8s"

        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset context opened)

    // ---- 断幺九与食断开关 ----

    [<Fact>]
    let ``断幺九全是中张`` () =
        Assert.Equal<Yaku list>([ Yaku.Tanyao ], yaku context (ron [] "2m 3m 4m 6m 7m 8m 5p 6p 7p 2s 3s 4s 5s 5s" "3s"))

    [<Fact>]
    let ``食断关掉时副露的断幺九不成立`` () =
        let hand =
            ron [ AgariFixture.pon (seat 1) "2s" "2s 2s" ] "2m 3m 4m 6m 7m 8m 5p 6p 7p 5s 5s" "4m"

        Assert.Equal<Yaku list>([ Yaku.Tanyao ], yaku context hand)
        Assert.Equal(Error YakuError.NoYaku, Yaku.detect (Ruleset.withoutKuitan ruleset) context hand)

        // 食断关掉也不影响门清的断幺九。
        let menzen = ron [] "2m 3m 4m 6m 7m 8m 5p 6p 7p 2s 3s 4s 5s 5s" "3s"

        Assert.Equal<Yaku list>(
            [ Yaku.Tanyao ],
            Yaku.detect (Ruleset.withoutKuitan ruleset) context menzen
            |> Result.map YakuTally.yaku
            |> Result.defaultValue []
        )

    // ---- 役牌 ----

    [<Fact>]
    let ``三元牌的刻子一种一番`` () =
        Assert.Equal<Yaku list>(
            [ Yaku.Yakuhai(AgariFixture.tile "5z") ],
            yaku context (ron [] "5z 5z 5z 2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "8s")
        )

    [<Fact>]
    let ``场风与自风各一番，连风牌两番`` () =
        Assert.Equal<Yaku list>(
            [ Yaku.Bakaze Ton ],
            yaku context (ron [] "1z 1z 1z 2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "8s")
        )

        Assert.Equal<Yaku list>(
            [ Yaku.Jikaze Nan ],
            yaku context (ron [] "2z 2z 2z 2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "8s")
        )

        // 东场东家：东的刻子同时是场风与自风，合计两番。
        let doubleWind = YakuContext.create Ton Ton
        let hand = ron [] "1z 1z 1z 2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "8s"
        Assert.Equal<Yaku list>([ Yaku.Bakaze Ton; Yaku.Jikaze Ton ], yaku doubleWind hand)
        Assert.Equal(2, han doubleWind hand)

    // ---- 上下文标志 ----

    [<Fact>]
    let ``海底摸月要自摸，河底捞鱼要荣和`` () =
        let concealed = "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z"
        let haitei = { context with Haitei = true }
        let houtei = { context with Houtei = true }

        Assert.Equal<Yaku list>([ Yaku.MenzenTsumo; Yaku.Haitei ], yaku haitei (tsumo [] concealed "8s"))
        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset haitei (ron [] concealed "8s"))

        Assert.Equal<Yaku list>([ Yaku.Houtei ], yaku houtei (ron [] concealed "8s"))
        // 自摸时河底不成立（门前清自摸和是另一回事）。
        Assert.Equal<Yaku list>([ Yaku.MenzenTsumo ], yaku houtei (tsumo [] concealed "8s"))

    [<Fact>]
    let ``岭上开花要自摸，抢杠要荣和`` () =
        // 副露手：把役限制在上下文役上。
        let naki = [ AgariFixture.chi (seat 3) "2m" "3m 4m" ]
        let concealed = "5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z"
        let rinshan = { context with Rinshan = true }
        let chankan = { context with Chankan = true }

        Assert.Equal<Yaku list>([ Yaku.Rinshan ], yaku rinshan (tsumo naki concealed "8s"))
        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset rinshan (ron naki concealed "8s"))

        Assert.Equal<Yaku list>([ Yaku.Chankan ], yaku chankan (ron naki concealed "8s"))
        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset chankan (tsumo naki concealed "8s"))

    [<Fact>]
    let ``天和与地和是役满`` () =
        let concealed = "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z"
        let tenhou = { context with Tenhou = true }
        let chiihou = { context with Chiihou = true }

        Assert.Equal<Yaku list>([ Yaku.Tenhou ], yaku tenhou (tsumo [] concealed "8s"))
        Assert.Equal(1, yakuman tenhou (tsumo [] concealed "8s"))
        Assert.Equal<Yaku list>([ Yaku.Chiihou ], yaku chiihou (tsumo [] concealed "8s"))

        // 天和只可能是自摸。
        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset tenhou (ron [] concealed "8s"))

    // ---- 2 番役 ----

    [<Fact>]
    let ``混全带幺九每一块都带幺九且有顺子`` () =
        let hand = ron [] "1m 2m 3m 9m 9m 7p 8p 9p 1s 2s 3s 3z 3z 3z" "9m"
        Assert.Equal<Yaku list>([ Yaku.Chanta ], yaku context hand)
        Assert.Equal(2, han context hand)

        // 副露降番：2 番变 1 番。
        let opened =
            ron [ AgariFixture.pon (seat 1) "3z" "3z 3z" ] "1m 2m 3m 9m 9m 7p 8p 9p 1s 2s 3s" "9m"

        Assert.Equal<Yaku list>([ Yaku.Chanta ], yaku context opened)
        Assert.Equal(1, han context opened)

    [<Fact>]
    let ``纯全带幺九不含字牌`` () =
        let hand = ron [] "1m 2m 3m 9m 9m 7p 8p 9p 1s 2s 3s 9s 9s 9s" "9m"
        Assert.Equal<Yaku list>([ Yaku.Junchan ], yaku context hand)
        Assert.Equal(3, han context hand)

    [<Fact>]
    let ``三色同顺三门同号`` () =
        let hand = ron [] "2m 3m 4m 2p 3p 4p 2s 3s 4s 7s 8s 9s 3z 3z" "8s"
        Assert.Equal<Yaku list>([ Yaku.SanshokuDoujun ], yaku context hand)
        Assert.Equal(2, han context hand)

    [<Fact>]
    let ``一气通贯一门一二三四五六七八九`` () =
        let hand = ron [] "1m 2m 3m 4m 5m 6m 7m 8m 9m 2p 3p 4p 3z 3z" "3p"
        Assert.Equal<Yaku list>([ Yaku.Ittsuu ], yaku context hand)
        Assert.Equal(2, han context hand)

    [<Fact>]
    let ``三色同顺与一气通贯在四面子里不可能同时成立`` () =
        // 一气通贯占掉三副顺子且都在一门；三色同顺要三门各一副，四副面子放不下两者。
        let ittsuu = ron [] "1m 2m 3m 4m 5m 6m 7m 8m 9m 2p 3p 4p 3z 3z" "3p"
        Assert.False(List.contains Yaku.SanshokuDoujun (yaku context ittsuu))

        let sanshoku = ron [] "2m 3m 4m 2p 3p 4p 2s 3s 4s 7s 8s 9s 3z 3z" "8s"
        Assert.False(List.contains Yaku.Ittsuu (yaku context sanshoku))

    [<Fact>]
    let ``对对和四组刻子`` () =
        let hand =
            ron
                [
                    AgariFixture.pon (seat 1) "3p" "3p 3p"
                    AgariFixture.pon (seat 2) "7s" "7s 7s"
                ]
                "1m 1m 1m 3z 3z 3z 9m 9m"
                "9m"

        Assert.Equal<Yaku list>([ Yaku.Toitoi ], yaku context hand)
        Assert.Equal(2, han context hand)

    [<Fact>]
    let ``三暗刻数的是暗刻，荣和补上的刻子算明刻`` () =
        let concealed = "1m 1m 1m 3p 3p 3p 7s 7s 7s 2m 3m 4m 9m 9m"

        Assert.Equal<Yaku list>([ Yaku.MenzenTsumo; Yaku.Sanankou ], yaku context (tsumo [] concealed "4m"))

        // 荣和补上的正是其中一个刻子：它按明刻算，只剩两个暗刻。
        let riichi =
            { context with
                Riichi = RiichiDeclaration.Riichi
            }

        Assert.Equal<Yaku list>([ Yaku.Riichi ], yaku riichi (ron [] concealed "1m"))

        // 荣和补的是顺子，三个暗刻不受影响。
        Assert.Equal<Yaku list>([ Yaku.Riichi; Yaku.Sanankou ], yaku riichi (ron [] concealed "4m"))

    [<Fact>]
    let ``暗杠算暗刻，但杠子让平和不成立`` () =
        // 暗杠的是西（不是役牌），免得混进役牌的番。
        let hand =
            tsumo [ AgariFixture.ankan "3z 3z 3z 3z" ] "1m 1m 1m 3p 3p 3p 2m 3m 4m 9m 9m" "4m"

        Assert.Equal<Yaku list>([ Yaku.MenzenTsumo; Yaku.Sanankou ], yaku context hand)

        // 暗杠不破门清，但四面子里有杠子就不是四副顺子。
        let noPinfu =
            ron [ AgariFixture.ankan "4z 4z 4z 4z" ] "2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "9s"

        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset context noPinfu)

    [<Fact>]
    let ``三色同刻三门同号的刻子`` () =
        let hand = ron [] "2m 2m 2m 2p 2p 2p 2s 2s 2s 4s 5s 6s 3z 3z" "6s"
        Assert.Equal<Yaku list>([ Yaku.Sanankou; Yaku.SanshokuDoukou ], yaku context hand)
        Assert.Equal(4, han context hand)

    [<Fact>]
    let ``三色同顺与一气通贯的副露降番`` () =
        let sanshoku =
            ron [ AgariFixture.chi (seat 3) "2p" "3p 4p" ] "2m 3m 4m 2s 3s 4s 7s 8s 9s 3z 3z" "8s"

        Assert.Equal<Yaku list>([ Yaku.SanshokuDoujun ], yaku context sanshoku)
        Assert.Equal(1, han context sanshoku)

        let ittsuu =
            ron [ AgariFixture.chi (seat 3) "1m" "2m 3m" ] "4m 5m 6m 7m 8m 9m 2p 3p 4p 3z 3z" "3p"

        Assert.Equal<Yaku list>([ Yaku.Ittsuu ], yaku context ittsuu)
        Assert.Equal(1, han context ittsuu)

    [<Fact>]
    let ``三杠子`` () =
        let hand =
            ron
                [
                    AgariFixture.minkan (seat 1) "3p" "3p 3p 3p"
                    AgariFixture.ankan "7s 7s 7s 7s"
                    AgariFixture.minkan (seat 2) "9m" "9m 9m 9m"
                ]
                "2m 3m 4m 3z 3z"
                "4m"

        Assert.Equal<Yaku list>([ Yaku.Sankantsu ], yaku context hand)
        Assert.Equal(2, han context hand)

    [<Fact>]
    let ``七对子七组不同的对子`` () =
        let hand = ron [] "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 3z 3z" "3z"
        Assert.Equal<Yaku list>([ Yaku.Chiitoitsu ], yaku context hand)
        Assert.Equal(2, han context hand)

    [<Fact>]
    let ``七对子可以叠上牌型役`` () =
        // 七对子 + 断幺九。
        Assert.Equal<Yaku list>(
            [ Yaku.Tanyao; Yaku.Chiitoitsu ],
            yaku context (ron [] "2m 2m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 8s 8s" "8s")
        )

        // 七对子 + 混老头。
        Assert.Equal<Yaku list>(
            [ Yaku.Chiitoitsu; Yaku.Honroutou ],
            yaku context (ron [] "1m 1m 9m 9m 1p 1p 9p 9p 1s 1s 1z 1z 3z 3z" "3z")
        )

        // 七对子 + 混一色。
        Assert.Equal<Yaku list>(
            [ Yaku.Chiitoitsu; Yaku.Honitsu ],
            yaku context (ron [] "1m 1m 3m 3m 5m 5m 7m 7m 9m 9m 3z 3z 4z 4z" "4z")
        )

        // 七组字牌对子：字一色役满，七对子不再计。
        let tsuuiisou = ron [] "1z 1z 2z 2z 3z 3z 4z 4z 5z 5z 6z 6z 7z 7z" "7z"
        Assert.Equal<Yaku list>([ Yaku.Tsuuiisou ], yaku context tsuuiisou)
        Assert.Equal(1, yakuman context tsuuiisou)

    [<Fact>]
    let ``混老头全是幺九牌且没有顺子`` () =
        let hand =
            ron [ AgariFixture.pon (seat 1) "1p" "1p 1p" ] "1m 1m 1m 3z 3z 3z 9s 9s 9s 9m 9m" "9m"

        Assert.Equal<Yaku list>([ Yaku.Toitoi; Yaku.Sanankou; Yaku.Honroutou ], yaku context hand)
        Assert.Equal(6, han context hand)

    [<Fact>]
    let ``小三元两组三元刻加三元雀头`` () =
        let hand = ron [] "5z 5z 5z 6z 6z 6z 7z 7z 1s 2s 3s 2s 3s 4s" "1s"

        Assert.Equal<Yaku list>(
            [
                Yaku.Yakuhai(AgariFixture.tile "5z")
                Yaku.Yakuhai(AgariFixture.tile "6z")
                Yaku.Shousangen
                Yaku.Honitsu
            ],
            yaku context hand
        )

        Assert.Equal(7, han context hand)

    // ---- 3 番与 6 番 ----

    [<Fact>]
    let ``混一色一门数牌加字牌`` () =
        let hand = ron [] "2m 3m 4m 6m 7m 8m 1m 1m 1m 3z 3z 3z 5m 5m" "5m"
        Assert.Equal<Yaku list>([ Yaku.Honitsu ], yaku context hand)
        Assert.Equal(3, han context hand)

        let opened =
            ron [ AgariFixture.pon (seat 1) "3z" "3z 3z" ] "2m 3m 4m 6m 7m 8m 1m 1m 1m 5m 5m" "5m"

        Assert.Equal(2, han context opened)

    [<Fact>]
    let ``清一色一门到底`` () =
        let hand = ron [] "1m 1m 1m 2m 3m 4m 5m 6m 7m 7m 8m 9m 3m 3m" "9m"
        Assert.Equal<Yaku list>([ Yaku.Chinitsu ], yaku context hand)
        Assert.Equal(6, han context hand)

    [<Fact>]
    let ``二杯口取代一杯口，也压过七对子`` () =
        // 这副牌同时是一般型（二盃口）与七对子，高点法取番数高的一般型。
        let hand = ron [] "2m 3m 4m 2m 3m 4m 5p 6p 7p 5p 6p 7p 3z 3z" "7p"
        Assert.Equal<Yaku list>([ Yaku.Pinfu; Yaku.Ryanpeikou ], yaku context hand)
        Assert.Equal(4, han context hand)
        Assert.False(List.contains Yaku.Iipeiko (yaku context hand))
        Assert.False(List.contains Yaku.Chiitoitsu (yaku context hand))

    // ---- 多分解取最高番 ----

    [<Fact>]
    let ``同一副牌的多种面子分解取番数最高的一种`` () =
        // 111222333m 既能拆成三个刻子（三暗刻 2 番）也能拆成三副 123m（一杯口 1 番）。
        let hand = tsumo [] "1m 1m 1m 2m 2m 2m 3m 3m 3m 4p 5p 6p 9s 9s" "9s"
        Assert.Equal<Yaku list>([ Yaku.MenzenTsumo; Yaku.Sanankou ], yaku context hand)
        Assert.Equal(3, han context hand)

        // 两种读法都在 candidates 里，排序把高番的放在最前。
        let all = Yaku.candidates ruleset context hand
        Assert.True(List.length all >= 2)
        Assert.True(List.contains Yaku.Iipeiko (all |> List.collect YakuTally.yaku))

    // ---- 役满 ----

    [<Fact>]
    let ``国士无双与十三面待`` () =
        // 和了牌就是成对的那张：听的是十三面。
        let juusanmen = ron [] "1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z 7z" "7z"
        Assert.Equal<Yaku list>([ Yaku.KokushiJuusanmen ], yaku context juusanmen)
        Assert.Equal(1, yakuman context juusanmen)

        // 成对的是 1m、和了牌是 9m：单面听，普通国士。
        let single = ron [] "1m 1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z" "9m"
        Assert.Equal<Yaku list>([ Yaku.Kokushi ], yaku context single)

    [<Fact>]
    let ``四暗刻，荣和补上的刻子不算暗刻`` () =
        let concealed = "1m 1m 1m 3p 3p 3p 7s 7s 7s 3z 3z 3z 9m 9m"

        Assert.Equal<Yaku list>([ Yaku.Suuankou ], yaku context (tsumo [] concealed "1m"))

        // 单骑自摸：四暗刻单骑。
        Assert.Equal<Yaku list>([ Yaku.SuuankouTanki ], yaku context (tsumo [] concealed "9m"))

        // 单骑荣和：雀头由荣和补上，四个刻子仍然是暗刻。
        Assert.Equal<Yaku list>([ Yaku.SuuankouTanki ], yaku context (ron [] concealed "9m"))

        // 双碰荣和：补上的那个刻子是明刻，掉成三暗刻加对对和。
        let riichi =
            { context with
                Riichi = RiichiDeclaration.Riichi
            }

        Assert.Equal<Yaku list>([ Yaku.Riichi; Yaku.Toitoi; Yaku.Sanankou ], yaku riichi (ron [] concealed "1m"))

    [<Fact>]
    let ``天和与国士复合成两倍役满`` () =
        let tenhou = { context with Tenhou = true }
        let hand = tsumo [] "1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z 7z" "7z"
        Assert.Equal<Yaku list>([ Yaku.KokushiJuusanmen; Yaku.Tenhou ], yaku tenhou hand)
        Assert.Equal(2, yakuman tenhou hand)

    [<Fact>]
    let ``大三元`` () =
        let hand = ron [] "5z 5z 5z 6z 6z 6z 7z 7z 7z 2m 3m 4m 9m 9m" "4m"
        Assert.Equal<Yaku list>([ Yaku.Daisangen ], yaku context hand)
        Assert.Equal(1, yakuman context hand)

    [<Fact>]
    let ``小四喜与大四喜`` () =
        let shousuushii = ron [] "1z 1z 1z 2z 2z 2z 3z 3z 3z 4z 4z 2m 3m 4m" "4m"
        Assert.Equal<Yaku list>([ Yaku.Shousuushii ], yaku context shousuushii)

        // 大四喜 + 四暗刻单骑：役满复合，两倍。
        let daisuushii = ron [] "1z 1z 1z 2z 2z 2z 3z 3z 3z 4z 4z 4z 5m 5m" "5m"
        Assert.Equal<Yaku list>([ Yaku.SuuankouTanki; Yaku.Daisuushii ], yaku context daisuushii)
        Assert.Equal(2, yakuman context daisuushii)

    [<Fact>]
    let ``字一色`` () =
        let hand =
            ron [ AgariFixture.pon (seat 1) "1z" "1z 1z" ] "2z 2z 2z 3z 3z 3z 5z 5z 5z 7z 7z" "7z"

        Assert.Equal<Yaku list>([ Yaku.Tsuuiisou ], yaku context hand)
        Assert.Equal(1, yakuman context hand)

    [<Fact>]
    let ``清老头`` () =
        let hand =
            ron [ AgariFixture.pon (seat 1) "1p" "1p 1p" ] "1m 1m 1m 9m 9m 9m 9p 9p 9p 1s 1s" "1s"

        Assert.Equal<Yaku list>([ Yaku.Chinroutou ], yaku context hand)

    [<Fact>]
    let ``绿一色`` () =
        let hand =
            ron [ AgariFixture.pon (seat 1) "6z" "6z 6z" ] "2s 2s 2s 3s 3s 3s 4s 4s 4s 8s 8s" "8s"

        Assert.Equal<Yaku list>([ Yaku.Ryuuiisou ], yaku context hand)

    [<Fact>]
    let ``九莲宝灯与纯正九莲宝灯`` () =
        // 1112345678999m 听九面，和 5m 就是纯正九莲。
        let junsei = ron [] "1m 1m 1m 2m 3m 4m 5m 5m 6m 7m 8m 9m 9m 9m" "5m"
        Assert.Equal<Yaku list>([ Yaku.JunseiChuuren ], yaku context junsei)

        // 1112245678999m 只听 3m：普通九莲宝灯。
        let plain = ron [] "1m 1m 1m 2m 2m 3m 4m 5m 6m 7m 8m 9m 9m 9m" "3m"
        Assert.Equal<Yaku list>([ Yaku.Chuuren ], yaku context plain)

    [<Fact>]
    let ``四杠子`` () =
        let hand =
            ron
                [
                    AgariFixture.ankan "1m 1m 1m 1m"
                    AgariFixture.ankan "2m 2m 2m 2m"
                    AgariFixture.ankan "3m 3m 3m 3m"
                    AgariFixture.minkan (seat 1) "4m" "4m 4m 4m"
                ]
                "9m 9m"
                "9m"

        Assert.Equal<Yaku list>([ Yaku.Suukantsu ], yaku context hand)

    [<Fact>]
    let ``役满成立时役满以下的役全部不计`` () =
        let hand = ron [] "5z 5z 5z 6z 6z 6z 7z 7z 7z 1z 1z 1z 9m 9m" "9m"
        let found = yaku context hand
        Assert.True(List.contains Yaku.Daisangen found)
        Assert.False(List.contains Yaku.Toitoi found)
        Assert.False(List.contains Yaku.Honitsu found)
        Assert.False(List.contains (Yaku.Bakaze Ton) found)
        Assert.Equal(0, han context hand)

    // ---- 宝牌 ----

    [<Fact>]
    let ``宝牌计番但不是役`` () =
        let hand = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "9s"

        let withDora =
            { context with
                DoraMarkers = AgariFixture.tiles "1m 2z"
            }

        let tally = detect withDora hand
        // 指示牌 1m 指向 2m（手里一张），2z 指向 3z（手里两张）。
        Assert.Equal(3, tally.Dora)
        Assert.Equal<Yaku list>([ Yaku.Pinfu ], YakuTally.yaku tally)
        Assert.Equal(1, YakuTally.han tally)

    [<Fact>]
    let ``里宝牌只在立直时计入`` () =
        let hand = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "9s"

        let noRiichi =
            { context with
                UraDoraMarkers = AgariFixture.tiles "2z"
            }

        Assert.Equal(0, (detect noRiichi hand).Uradora)

        let riichi =
            { noRiichi with
                Riichi = RiichiDeclaration.Riichi
            }

        Assert.Equal(2, (detect riichi hand).Uradora)

    [<Fact>]
    let ``红宝牌单独计番`` () =
        let hand = AgariFixture.ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "9s"
        Assert.Equal(0, (detect context hand).Akadora)

        let withAka = AgariFixture.ron [] "2m 3m 4m 5pr 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "9s"
        Assert.Equal(1, (detect context withAka).Akadora)
        Assert.Equal<Yaku list>([ Yaku.Pinfu ], yaku context withAka)

    [<Fact>]
    let ``只有宝牌救不了无役`` () =
        let hand =
            ron [ AgariFixture.pon (seat 1) "3z" "3z 3z" ] "2m 3m 4m 5p 6p 7p 7s 8s 9s 5m 5m" "4m"

        let withDora =
            { context with
                DoraMarkers = AgariFixture.tiles "1m 1m 1m"
            }

        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset withDora hand)

    // ---- 不成和了型 ----

    [<Fact>]
    let ``不成和了型与无役是两回事`` () =
        let notAgari = ron [] "1m 3m 5m 7m 9m 1p 3p 5p 7p 9p 1s 3s 5s 7s" "7s"
        Assert.Equal(Error YakuError.NoAgariShape, Yaku.detect ruleset context notAgari)

        let noYaku =
            ron [ AgariFixture.pon (seat 1) "3z" "3z 3z" ] "2m 3m 4m 5p 6p 7p 7s 8s 9s 5m 5m" "4m"

        Assert.Equal(Error YakuError.NoYaku, Yaku.detect ruleset context noYaku)

    // ---- 渲染与标识符 ----

    [<Fact>]
    let ``役的中文渲染`` () =
        Assert.Equal("立直", Yaku.toDisplay Yaku.Riichi)
        Assert.Equal("役牌 中", Yaku.toDisplay (Yaku.Yakuhai(AgariFixture.tile "7z")))
        Assert.Equal("场风 东", Yaku.toDisplay (Yaku.Bakaze Ton))
        Assert.Equal("国士无双十三面", Yaku.toDisplay Yaku.KokushiJuusanmen)

    [<Fact>]
    let ``役的稳定标识符`` () =
        Assert.Equal("riichi", Yaku.name Yaku.Riichi)
        Assert.Equal("yakuhai:7z", Yaku.name (Yaku.Yakuhai(AgariFixture.tile "7z")))
        Assert.Equal("bakaze:1z", Yaku.name (Yaku.Bakaze Ton))
        Assert.Equal("junsei_chuuren", Yaku.name Yaku.JunseiChuuren)

    [<Fact>]
    let ``判定结果的中文渲染`` () =
        let hand = ron [] "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z" "9s"

        let withDora =
            { context with
                DoraMarkers = AgariFixture.tiles "1m"
            }

        Assert.Equal("平和 1 番、宝牌 1 番", YakuTally.toDisplay (detect withDora hand))

        Assert.Equal(
            "大三元 役满",
            YakuTally.toDisplay (detect context (ron [] "5z 5z 5z 6z 6z 6z 7z 7z 7z 2m 3m 4m 9m 9m" "4m"))
        )

        Assert.Equal("无役，不能和", YakuError.toDisplay YakuError.NoYaku)
