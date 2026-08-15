namespace Janpo.Engine.Tests

open Xunit
open Janpo

/// 和了牌姿的构造，与它的面子分解枚举。
module AgariHandTests =

    let private tiles = AgariFixture.tiles
    let private tile = AgariFixture.tile

    // ---- 构造 ----

    [<Fact>]
    let ``和了的暗牌是 14 减 3 倍副露数`` () =
        Assert.True(
            (match AgariHand.ron [] (tiles "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z") (tile "9s") with
             | Ok _ -> true
             | Error _ -> false)
        )

        Assert.Equal(
            Error(AgariConcealedCountMismatch(13, 0)),
            AgariHand.ron [] (tiles "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z") (tile "9s")
        )

        Assert.Equal(
            Error(AgariConcealedCountMismatch(14, 1)),
            AgariHand.ron
                [ AgariFixture.pon 1 "5z" "5z 5z" ]
                (tiles "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z")
                (tile "9s")
        )

    [<Fact>]
    let ``和了牌必须在暗牌里`` () =
        Assert.Equal(
            Error(AgariTileNotInHand(tile "1m")),
            AgariHand.ron [] (tiles "2m 3m 4m 5p 6p 7p 2s 3s 4s 7s 8s 9s 3z 3z") (tile "1m")
        )

        // 红宝牌与正牌是同一牌种：和了牌写 5p、手里是 5pr 也算在手里。
        Assert.True(
            (match AgariHand.ron [] (tiles "5pr 5p 5p 2m 3m 4m 2s 3s 4s 7s 8s 9s 3z 3z") (tile "5p") with
             | Ok _ -> true
             | Error _ -> false)
        )

    [<Fact>]
    let ``同一牌种连副露一起最多四张`` () =
        Assert.Equal(
            Error(AgariKindOverflow(tile "5z", 5)),
            AgariHand.ron [ AgariFixture.pon 1 "5z" "5z 5z" ] (tiles "5z 5z 2m 3m 4m 2s 3s 4s 7s 8s 9s") (tile "9s")
        )

    [<Fact>]
    let ``门清算暗杠不算别的副露`` () =
        let ankan =
            AgariFixture.ron [ AgariFixture.ankan "1z 1z 1z 1z" ] "2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "9s"

        Assert.True(AgariHand.isMenzen ankan)

        let pon =
            AgariFixture.ron [ AgariFixture.pon 1 "1z" "1z 1z" ] "2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "9s"

        Assert.False(AgariHand.isMenzen pon)

    [<Fact>]
    let ``手上的牌含副露，杠是四张`` () =
        let hand =
            AgariFixture.ron [ AgariFixture.ankan "1z 1z 1z 1z" ] "2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "9s"

        Assert.Equal(15, List.length (AgariHand.tiles hand))
        Assert.Equal(11, List.length (AgariHand.concealed hand))

    // ---- 面子分解 ----

    let private breakdowns (hand: AgariHand) = MentsuBreakdown.enumerate hand

    [<Fact>]
    let ``一副牌可以有多种面子分解`` () =
        // 111222333m 既能拆三个刻子也能拆三副 123m。
        let hand = AgariFixture.tsumo [] "1m 1m 1m 2m 2m 2m 3m 3m 3m 4p 5p 6p 9s 9s" "9s"
        let all = breakdowns hand

        let kinds =
            all
            |> List.map (fun one ->
                MentsuBreakdown.mentsu one
                |> List.filter (fun mentsu -> Mentsu.kind mentsu = Koutsu)
                |> List.length)
            |> List.distinct
            |> List.sort

        Assert.Equal<int list>([ 0; 3 ], kinds)

    [<Fact>]
    let ``听牌型由和了牌落在哪一块上决定`` () =
        let waitOf (winning: string) =
            AgariFixture.ron [] "2m 3m 4m 5p 6p 7p 1s 2s 3s 7s 8s 9s 3z 3z" winning
            |> breakdowns
            |> List.map MentsuBreakdown.wait

        // 78s 等 9s 是两面，7s9s 等 8s 是嵌张，12s 等 3s 是边张。
        Assert.Equal<WaitKind list>([ Ryanmen ], waitOf "9s")
        Assert.Equal<WaitKind list>([ Kanchan ], waitOf "8s")
        Assert.Equal<WaitKind list>([ Penchan ], waitOf "3s")
        Assert.Equal<WaitKind list>([ Tanki ], waitOf "3z")

        // 双碰：和了牌补上的是刻子。
        let shanpon =
            AgariFixture.ron [] "1m 1m 1m 3p 3p 3p 7s 7s 7s 2m 3m 4m 9m 9m" "1m"
            |> breakdowns
            |> List.map MentsuBreakdown.wait

        Assert.Equal<WaitKind list>([ Shanpon ], shanpon)

    [<Fact>]
    let ``荣和补上的刻子按明刻算，自摸的仍是暗刻`` () =
        let concealed = "1m 1m 1m 3p 3p 3p 7s 7s 7s 2m 3m 4m 9m 9m"

        let ankouCount (hand: AgariHand) =
            breakdowns hand
            |> List.map (fun one ->
                MentsuBreakdown.mentsu one
                |> List.filter (fun mentsu -> Mentsu.kind mentsu <> Shuntsu && Mentsu.isConcealed mentsu)
                |> List.length)

        Assert.Equal<int list>([ 2 ], ankouCount (AgariFixture.ron [] concealed "1m"))
        Assert.Equal<int list>([ 3 ], ankouCount (AgariFixture.tsumo [] concealed "1m"))

    [<Fact>]
    let ``七对子与国士没有面子分解`` () =
        Assert.Empty(breakdowns (AgariFixture.ron [] "1m 1m 4m 4m 2p 2p 5p 5p 3s 3s 6s 6s 3z 3z" "3z"))
        Assert.Empty(breakdowns (AgariFixture.ron [] "1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z 7z 7z" "7z"))

    [<Fact>]
    let ``副露与暗杠原样进面子表`` () =
        let hand =
            AgariFixture.ron
                [ AgariFixture.ankan "1z 1z 1z 1z"; AgariFixture.chi 3 "3m" "4m 5m" ]
                "5p 6p 7p 7s 8s 9s 3z 3z"
                "9s"

        match breakdowns hand with
        | [ one ] ->
            let mentsu = MentsuBreakdown.mentsu one
            Assert.Equal(4, List.length mentsu)
            Assert.Contains(Mentsu.kantsu true (tile "1z"), mentsu)

            match Mentsu.shuntsu false (tile "3m") with
            | Some chi -> Assert.Contains(chi, mentsu)
            | None -> failwith "3m 起的顺子应当存在"
        | other -> failwith $"应当只有一种分解，得到 {List.length other} 种"

    [<Fact>]
    let ``和了牌姿的中文渲染`` () =
        let hand =
            AgariFixture.tsumo [ AgariFixture.pon 1 "7z" "7z 7z" ] "2m 3m 4m 5p 6p 7p 7s 8s 9s 3z 3z" "9s"

        Assert.Equal("2万 3万 4万 5筒 6筒 7筒 7索 8索 9索 西 西 碰中中中（自摸9索）", AgariHand.toDisplay hand)
