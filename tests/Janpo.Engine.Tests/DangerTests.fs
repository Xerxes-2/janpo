namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.AgariFixture
open Janpo.Engine.Tests.GameStateFixtures

/// 危险度（票 25）：现物 / 筋 / 壁 / 宝牌周边的**规则化启发式**排序。
///
/// **它是分析附件**：这里验的是判据本身（谁算有威胁、哪张排在哪张前面、理由标签写了什么），
/// 规则判定一个字节都不读它——`Danger.fs` 排在全部判定文件之后，那些文件引用不到它。
module DangerTests =

    /// 合成一份观测：自家手牌与他家的河、副露、立直逐项摆好。
    /// **直接构造是有意的**——危险度只看得见的信息，摆一手比推一局精确。
    let private observationWith
        (hand: string)
        (doraMarkers: string)
        (others: (string * Naki list * RiichiState) list)
        : Observation =
        let masked (index: int) (kawa: string, naki: Naki list, riichi: RiichiState) : MaskedSeat =
            {
                Seat = seat index
                HandCount = 13 - 3 * List.length naki
                Relative = index
                Jikaze = Kaze.ofIndex index |> Option.defaultValue Kaze.Ton
                Junme = 6
                Score = 25000
                Kawa = tilesOf kawa |> List.map (fun pai -> { Pai = pai; Tsumogiri = false })
                Naki = naki
                Riichi = riichi
                Ippatsu = false
            }

        {
            Seat = seat 0
            Bakaze = Kaze.Ton
            Kyoku = 1
            Honba = 0
            Kyotaku = 0
            DoraMarkers = tilesOf doraMarkers
            WallRemaining = 40
            Self =
                {
                    Seat = seat 0
                    Jikaze = Kaze.Ton
                    Junme = 6
                    Score = 25000
                    Hand = tilesOf hand
                    Drawn = None
                    Kawa = []
                    Naki = []
                    Riichi = RiichiState.None
                    Ippatsu = false
                    Furiten = { Permanent = false; Doujun = false }
                }
            Others = others |> List.mapi (fun index other -> masked (index + 1) other)
        }

    let private quiet (kawa: string) : string * Naki list * RiichiState = kawa, [], RiichiState.None

    let private riichiAt (kawa: string) : string * Naki list * RiichiState =
        kawa, [], RiichiState.Accepted RiichiDeclaration.Riichi

    let private nakiAt (kawa: string) (naki: Naki list) : string * Naki list * RiichiState =
        kawa, naki, RiichiState.None

    /// 一手够杂的牌：万子、索子与字牌都有，逐张试打时排序看得见分层。
    let private hand = "1m 2m 3m 1s 2s 3s 4s 5s 6s 7s 8s 9s 7z"

    let private rankOf (observation: Observation) (candidates: string) : Danger list =
        Danger.rank observation (tilesOf candidates)

    let private dangerOf (notation: string) (ranking: Danger list) : Danger =
        match ranking |> List.tryFind (fun danger -> Tile.toMjai danger.Pai = notation) with
        | Some danger -> danger
        | None -> failwith $"排序里应当有 {notation}"

    let private tierOf (notation: string) (ranking: Danger list) : DangerTier = (dangerOf notation ranking).Tier

    let private onlyThreat (observation: Observation) : Threat =
        match Danger.threats observation with
        | [ threat ] -> threat
        | others -> failwith $"应当只有一家有威胁，得到 {List.length others} 家"

    let private reasonsOf (notation: string) (ranking: Danger list) : string =
        (dangerOf notation ranking).Reasons
        |> List.map DangerReason.toDisplay
        |> String.concat "、"

    // ---- 威胁判据 ----

    [<Fact>]
    let ``没人立直也没人副露：不给排序`` () =
        // 危险度是「对谁危险」的排序。一家都没在做牌时它没有被评价的对象，
        // 因此宁可不给数（DECISIONS 25-1），也不给一份没有依据的名次。
        let observation = observationWith hand "" [ quiet "1z 2z"; quiet "3z"; quiet "" ]

        Assert.Empty(Danger.threats observation)
        Assert.Empty(rankOf observation hand)

    [<Fact>]
    let ``立直的那家有威胁`` () =
        let observation = observationWith hand "" [ riichiAt "4s"; quiet ""; quiet "" ]

        let threat = onlyThreat observation
        Assert.Equal(seat 1, threat.Seat)
        Assert.True(threat.Riichi)
        Assert.Equal("下家已立直", Threat.toDisplay threat)

    [<Fact>]
    let ``未立直但副露的对家也有威胁，也照样给排序`` () =
        let observation =
            observationWith hand "" [ quiet ""; nakiAt "4s" [ pon (seat 0) "5z" "5z 5z" ]; quiet "" ]

        let threat = onlyThreat observation
        Assert.Equal(seat 2, threat.Seat)
        Assert.False(threat.Riichi)
        Assert.True(threat.Naki)
        Assert.Equal("对家有副露", Threat.toDisplay threat)

        // 排序照给：现物最安全，1s 是 4s 的筋。
        let ranking = rankOf observation hand
        Assert.Equal(List.length (tilesOf hand), List.length ranking)
        Assert.Equal(DangerTier.Genbutsu, tierOf "4s" ranking)
        Assert.Equal(DangerTier.Suji, tierOf "1s" ranking)

    // ---- 现物 ----

    [<Fact>]
    let ``现物必最安全：它排在第一位，且宝牌也不改变这一条`` () =
        // 立直家打过 4s 与 7z；4s 同时是宝牌（指示牌 3s），仍然绝对安全。
        let observation = observationWith hand "3s" [ riichiAt "4s 7z"; quiet ""; quiet "" ]
        let ranking = rankOf observation hand

        Assert.Equal(DangerTier.Genbutsu, tierOf "4s" ranking)
        Assert.Equal(DangerTier.Genbutsu, tierOf "7z" ranking)
        Assert.Equal(1, (dangerOf "4s" ranking).Rank)
        Assert.Equal(1, (dangerOf "7z" ranking).Rank)

        // 排序是从安全到危险，现物在最前面。
        let safest =
            ranking |> List.truncate 2 |> List.map (fun danger -> Tile.toMjai danger.Pai)

        Assert.Equal<string list>([ "4s"; "7z" ], List.sort safest)

    [<Fact>]
    let ``现物是逐家的：只对一家现物就不是全桌安全`` () =
        let observation =
            observationWith hand "" [ riichiAt "4s"; nakiAt "" [ pon (seat 0) "5z" "5z 5z" ]; quiet "" ]

        let ranking = rankOf observation hand

        // 下家打过 4s，对家没打过——档位取最危险的那一家。
        Assert.Equal(DangerTier.NoEvidence, tierOf "4s" ranking)
        Assert.Equal("下家现物、对家无依据", reasonsOf "4s" ranking)

    // ---- 筋 ----

    [<Fact>]
    let ``筋：打过 4s 则 1s 与 7s 成筋`` () =
        // CONTEXT.md 的 Suji 词条就是这个例子。
        let observation = observationWith hand "" [ riichiAt "4s"; quiet ""; quiet "" ]
        let ranking = rankOf observation hand

        Assert.Equal(DangerTier.Suji, tierOf "1s" ranking)
        Assert.Equal(DangerTier.Suji, tierOf "7s" ranking)
        Assert.Equal("下家 4s 筋", reasonsOf "1s" ranking)

    [<Fact>]
    let ``筋：中张要两侧都打过才成筋`` () =
        // 4s 的两面听有两种（2s3s 与 5s6s），只打过 1s 只挡掉一半（片筋不是筋）。
        let half = observationWith hand "" [ riichiAt "1s"; quiet ""; quiet "" ]
        Assert.Equal(DangerTier.NoEvidence, tierOf "4s" (rankOf half hand))

        let full = observationWith hand "" [ riichiAt "1s 7s"; quiet ""; quiet "" ]
        let ranking = rankOf full hand
        Assert.Equal(DangerTier.Suji, tierOf "4s" ranking)
        Assert.Equal("下家 1s 7s 筋", reasonsOf "4s" ranking)

    [<Fact>]
    let ``筋只在同一花色内成立`` () =
        let observation = observationWith hand "" [ riichiAt "4m"; quiet ""; quiet "" ]
        let ranking = rankOf observation hand

        Assert.Equal(DangerTier.Suji, tierOf "1m" ranking)
        Assert.Equal(DangerTier.NoEvidence, tierOf "1s" ranking)

    [<Fact>]
    let ``字牌没有筋：它压根没有两面听`` () =
        let observation = observationWith hand "" [ riichiAt "4z"; quiet ""; quiet "" ]

        Assert.Equal(DangerTier.NoEvidence, tierOf "7z" (rankOf observation hand))

    // ---- 壁 ----

    [<Fact>]
    let ``壁：4s 四张全见时 2s 与 3s 的两面听被挡住`` () =
        // 自家手里一张 4s，下家河里三张——四张全见。2s 与 3s 只有含 4s 的那一种两面听。
        let observation =
            observationWith hand "" [ riichiAt "4s 4s 4s"; quiet ""; quiet "" ]

        let ranking = rankOf observation hand

        Assert.Equal(DangerTier.Kabe, tierOf "2s" ranking)
        Assert.Equal(DangerTier.Kabe, tierOf "3s" ranking)
        Assert.Equal("4s 四张全见", reasonsOf "3s" ranking)

        // 4s 自己是现物（下家打过），5s 的另一侧（6s7s）没挡住，仍然无依据。
        Assert.Equal(DangerTier.Genbutsu, tierOf "4s" ranking)
        Assert.Equal(DangerTier.NoEvidence, tierOf "5s" ranking)

    [<Fact>]
    let ``壁要真的四张全见：三张不算`` () =
        let observation = observationWith hand "" [ riichiAt "4s 4s"; quiet ""; quiet "" ]

        // 自家一张 + 河里两张 = 三张，第四张还在外面。
        Assert.Equal(DangerTier.NoEvidence, tierOf "2s" (rankOf observation hand))

    [<Fact>]
    let ``壁数的是全部可见牌：副露亮出来的那几张也算`` () =
        // 对家碰了 4s（自己亮两张、拿走一张），加上自家手里那张就是四张。
        let observation =
            observationWith hand "" [ riichiAt "4s"; nakiAt "" [ pon (seat 1) "4s" "4s 4s" ]; quiet "" ]

        let ranking = rankOf observation hand

        // 被鸣走的那张仍留在下家河里，只数一次：三张副露 + 一张手牌 = 四张全见。
        Assert.Equal(DangerTier.Kabe, tierOf "2s" ranking)

    // ---- 宝牌周边 ----

    [<Fact>]
    let ``宝牌与它周边的牌在同档里排后面`` () =
        // 指示牌 5s → 宝牌 6s。6s 本身、以及 4s-8s 都算周边（同花色两张之内）。
        let observation = observationWith hand "5s" [ riichiAt "1z"; quiet ""; quiet "" ]
        let ranking = rankOf observation hand

        Assert.Equal("宝牌", reasonsOf "6s" ranking)
        Assert.Equal("宝牌 6s 周边", reasonsOf "8s" ranking)
        Assert.Equal("", reasonsOf "9s" ranking)

        // 同为无依据时：无关牌 < 周边 < 宝牌本身。
        Assert.True((dangerOf "9s" ranking).Rank < (dangerOf "8s" ranking).Rank)
        Assert.True((dangerOf "8s" ranking).Rank < (dangerOf "6s" ranking).Rank)

    [<Fact>]
    let ``宝牌不越档：无依据的宝牌仍然排在有依据的牌后面`` () =
        // 指示牌 3s → 宝牌 4s，而下家打过 1s 与 7s（4s 成筋）、打过 9s（现物）。
        let observation =
            observationWith hand "3s" [ riichiAt "1s 7s 9s"; quiet ""; quiet "" ]

        let ranking = rankOf observation hand

        Assert.Equal(DangerTier.Genbutsu, tierOf "9s" ranking)
        Assert.Equal(DangerTier.Suji, tierOf "4s" ranking)
        Assert.True((dangerOf "9s" ranking).Rank < (dangerOf "4s" ranking).Rank)
        Assert.True((dangerOf "4s" ranking).Rank < (dangerOf "5s" ranking).Rank)

    // ---- 名次 ----

    [<Fact>]
    let ``名次是并列名次：同档同宝牌权重的牌同名次`` () =
        let observation = observationWith hand "" [ riichiAt "1m 4s"; quiet ""; quiet "" ]
        let ranking = rankOf observation hand

        let ranks = ranking |> List.map (fun danger -> danger.Rank)

        // 从 1 起、不下降，且并列的那几张共用同一个名次。
        Assert.Equal(1, List.head ranks)
        Assert.Equal<int list>(ranks, List.sort ranks)
        Assert.Equal((dangerOf "1m" ranking).Rank, (dangerOf "4s" ranking).Rank)

        // 并列名次会跳号：两张并列第一之后是第三。
        let after = ranks |> List.filter (fun rank -> rank > 1) |> List.min
        Assert.Equal(3, after)

    [<Fact>]
    let ``理由标签直接进 prompt：措辞照术语表`` () =
        let observation = observationWith hand "5s" [ riichiAt "1m 4s 4s 4s" ]
        let ranking = rankOf observation hand

        Assert.Equal("现物", DangerTier.toDisplay DangerTier.Genbutsu)
        Assert.Equal("筋", DangerTier.toDisplay DangerTier.Suji)
        Assert.Equal("壁", DangerTier.toDisplay DangerTier.Kabe)
        Assert.Equal("无依据", DangerTier.toDisplay DangerTier.NoEvidence)

        // 一整行就是 prompt 与牌桌上那一行的内容。
        // 宝牌的那条理由在现物上也照写（它是一个事实），只是**不改名次**——现物恒排第一。
        Assert.Equal("4s 现物（下家现物、宝牌 6s 周边）", Danger.toDisplay (dangerOf "4s" ranking))
        Assert.Equal(1, (dangerOf "4s" ranking).Rank)
        Assert.Equal("3s 壁（4s 四张全见）", Danger.toDisplay (dangerOf "3s" ranking))
