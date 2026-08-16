namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.AgariFixture
open Janpo.Engine.Tests.GameStateFixtures

/// 脚手架（票 24）：向听数、有效牌与逐张试打的进退向。**数值复用 `Shanten` / `Ukeire`**，
/// 这里验的是「算的是哪副手牌、扣掉了哪些可见牌、试打了哪几张」。
module ScaffoldTests =

    let private packageFor (seat: Seat) (state: GameState) : DecisionPackage =
        match DecisionPackage.forSeat seat state with
        | Some package -> package
        | None -> failwith "这个座位应当正在被问"

    let private scaffoldOf (package: DecisionPackage) : Scaffold =
        match DecisionPackage.scaffold package with
        | Some scaffold -> scaffold
        | None -> failwith "这一手的脚手架应当算得出来"

    let private scaffoldFor (seat: Seat) (state: GameState) : Scaffold = packageFor seat state |> scaffoldOf

    let private trialOf (notation: string) (scaffold: Scaffold) : DahaiScaffold =
        match scaffold.Dahai |> List.tryFind (fun trial -> Tile.toMjai trial.Pai = notation) with
        | Some trial -> trial
        | None -> failwith $"逐张试打里应当有 {notation}"

    let private ukeireOf (ukeire: Ukeire option) : Ukeire =
        match ukeire with
        | Some ukeire -> ukeire
        | None -> failwith "等摸形的有效牌应当算得出来"

    /// 摊好的牌山：Oya 一手 1m-9m 1p 2p 3p 5z 摸进 1z，因此它听 5z 单骑。
    /// 合法动作集固定为「立直宣言 + 13 条手切 + 摸切 1z」。
    let private opening = startScripted tsumoHoraScript

    /// 大明杠的剧本推一手：Oya 摸切 5s 之后座位 1 在响应阶段（碰 / 大明杠 / 过），
    /// 手里是 `5s 5s 5sr 1m-9m 1z` 十三张——**等摸形**，因此有效牌直接算得出来。
    let private responding =
        match GameState.step (startScripted minkanScript) (Action.Dahai(seat 0, tile "5s", true)) with
        | Ok(next, _) -> next
        | Error illegal -> failwith $"摸切应当合法，却得到「{IllegalAction.toDisplay illegal}」"

    // ---- 已摸进的手牌：向听数 + 逐张试打 ----

    [<Fact>]
    let ``已摸进的手牌：向听数含刚摸进那张，有效牌要先试打一张才有`` () =
        let scaffold = scaffoldFor (seat 0) opening

        // 1m-9m 三副顺子 + 1p2p3p + 5z + 1z：四面子无雀头，打掉一张孤张就听单骑。
        Assert.Equal(0, Shanten.value scaffold.Shanten)
        Assert.Equal(None, scaffold.Ukeire)
        Assert.NotEmpty(scaffold.Dahai)

    [<Fact>]
    let ``逐张试打：不退向听的进退向是 0，退向的是 +1`` () =
        let scaffold = scaffoldFor (seat 0) opening

        // 两张孤张打掉哪张都还是听牌（另一张成单骑）。
        for notation in [ "1z"; "5z" ] do
            let kept = trialOf notation scaffold
            Assert.Equal(0, Shanten.value kept.Shanten)
            Assert.Equal(0, kept.ShantenDelta)

        // 拆顺子就退向（向听戻し）：1m 一走，2m3m 只剩搭子。
        let back = trialOf "1m" scaffold
        Assert.Equal(1, Shanten.value back.Shanten)
        Assert.Equal(1, back.ShantenDelta)

    [<Fact>]
    let ``试打之后的有效牌就是打剩下的那个单骑`` () =
        let scaffold = scaffoldFor (seat 0) opening
        let ukeire = (trialOf "1z" scaffold).Ukeire |> ukeireOf

        Assert.Equal("5z", Ukeire.toMjai ukeire)
        // 自己手里那张扣掉，还剩 3 枚。
        Assert.Equal(3, Ukeire.total ukeire)

    [<Fact>]
    let ``逐张试打覆盖合法动作集里的每一条打牌，且对得上动作 id`` () =
        let package = packageFor (seat 0) opening

        let dahaiIds =
            DecisionPackage.options package
            |> List.filter (fun option ->
                match ActionOption.action option with
                | Action.Dahai _ -> true
                | _ -> false)
            |> List.map ActionOption.id

        let scaffolded =
            (scaffoldOf package).Dahai |> List.collect (fun trial -> trial.ActionIds)

        // 立直宣言不是打牌，它没有试打；打牌则一条不落。
        Assert.Equal<int list>(dahaiIds, List.sort scaffolded)

    // ---- 等摸的手牌：有效牌 ----

    [<Fact>]
    let ``等摸的手牌：有效牌直接算得出，没有可试打的牌`` () =
        let scaffold = scaffoldFor (seat 1) responding

        Assert.Equal(0, Shanten.value scaffold.Shanten)
        Assert.Empty(scaffold.Dahai)
        Assert.Equal("1z", scaffold.Ukeire |> ukeireOf |> Ukeire.toMjai)

    [<Fact>]
    let ``剩余枚数只扣得见的牌：他家的暗牌一张都算不进来`` () =
        // 座位 1 听 1z 单骑，而**座位 0 手里就有两张 1z**（`minkanScript` 的配牌）。
        // 合法观测里看不见那两张，因此剩余枚数是 3（4 减自己那张），不是 1。
        let scaffold = scaffoldFor (seat 1) responding

        Assert.Equal(3, scaffold.Ukeire |> ukeireOf |> Ukeire.total)

    // ---- 可见牌：河、副露与宝牌指示牌 ----

    /// 合成一份观测：自家手牌写死，他家只填这几条测得着的字段。
    /// **直接构造是有意的**——河与副露里各摆几张，剩余枚数才验得准。
    let private observationWith (hand: string) (kawa: string) (naki: Naki list) (doraMarkers: string) : Observation =
        let masked (index: int) (kawa: string) (naki: Naki list) : MaskedSeat =
            {
                Seat = seat index
                HandCount = 13
                Relative = index
                Jikaze = Kaze.ofIndex index |> Option.defaultValue Kaze.Ton
                Junme = 1
                Score = 25000
                Kawa = tilesOf kawa |> List.map (fun pai -> { Pai = pai; Tsumogiri = false })
                Naki = naki
                Riichi = RiichiState.None
                Ippatsu = false
            }

        {
            Seat = seat 0
            Bakaze = Kaze.Ton
            Kyoku = 1
            Honba = 0
            Kyotaku = 0
            DoraMarkers = tilesOf doraMarkers
            WallRemaining = 60
            Self =
                {
                    Seat = seat 0
                    Jikaze = Kaze.Ton
                    Junme = 1
                    Score = 25000
                    Hand = tilesOf hand
                    Drawn = None
                    Kawa = []
                    Naki = []
                    Riichi = RiichiState.None
                    Ippatsu = false
                    Furiten = { Permanent = false; Doujun = false }
                }
            Others = [ masked 1 kawa []; masked 2 "" naki; masked 3 "" [] ]
        }

    let private synthetic (observation: Observation) : Scaffold =
        match Scaffold.calculate kindSet [] observation with
        | Some scaffold -> scaffold
        | None -> failwith "这副手牌的脚手架应当算得出来"

    /// 听 5z 单骑的一手，自己占掉一张 5z。
    let private tanki = "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"

    [<Fact>]
    let ``剩余枚数扣掉河里的牌`` () =
        let scaffold = synthetic (observationWith tanki "5z 5z" [] "")

        Assert.Equal("5z", scaffold.Ukeire |> ukeireOf |> Ukeire.toMjai)
        Assert.Equal(1, scaffold.Ukeire |> ukeireOf |> Ukeire.total)

    [<Fact>]
    let ``被鸣走的那张只扣一次：它仍留在打牌者的河里`` () =
        // 座位 1 打出的那张 5z 被座位 2 碰走，`Kawa` 里仍然留着它（CONTEXT.md 的 Kawa）。
        // 全世界只有 4 张：自己 1 张 + 被碰走那张 + 碰的两张 = 4，因此剩余 0 枚。
        // 把 `Naki.taken` 也数一遍就是 5 张，`Ukeire` 会判可见张数越界，有效牌直接没了。
        let scaffold =
            synthetic (observationWith tanki "5z" [ pon (seat 1) "5z" "5z 5z" ] "")

        let ukeire = scaffold.Ukeire |> ukeireOf

        Assert.Equal("5z", Ukeire.toMjai ukeire)
        Assert.Equal(0, Ukeire.total ukeire)
        // 牌种还是有效牌，只是摸不到了（CONTEXT.md：有效牌为空不等于不听牌）。
        Assert.Equal(1, Ukeire.kindCount ukeire)

    [<Fact>]
    let ``剩余枚数扣掉宝牌指示牌`` () =
        let scaffold = synthetic (observationWith tanki "" [] "5z")

        Assert.Equal(2, scaffold.Ukeire |> ukeireOf |> Ukeire.total)

    // ---- 危险度（票 25） ----

    /// 把对家换成立直且河里摆了这几张的一家——危险度算的就是它。
    let private riichiAt (index: int) (kawa: string) (observation: Observation) : Observation =
        let others =
            observation.Others
            |> List.map (fun other ->
                if other.Seat = seat index then
                    { other with
                        Kawa = tilesOf kawa |> List.map (fun pai -> { Pai = pai; Tsumogiri = false })
                        Riichi = RiichiState.Accepted RiichiDeclaration.Riichi
                    }
                else
                    other)

        { observation with Others = others }

    /// 听 5z 单骑那一手多摸了一张 1m（十四张），因此试得了牌。
    let private drawn = $"{tanki} 1m"

    let private dahaiActions =
        [
            0, Action.Dahai(seat 0, tile "1m", false)
            1, Action.Dahai(seat 0, tile "5z", false)
        ]

    let private syntheticWith (actions: (int * Action) list) (observation: Observation) : Scaffold =
        match Scaffold.calculate kindSet actions observation with
        | Some scaffold -> scaffold
        | None -> failwith "这副手牌的脚手架应当算得出来"

    [<Fact>]
    let ``逐张试打带上危险度，脆弱的那几家单列一项`` () =
        let observation = observationWith drawn "" [] "" |> riichiAt 2 "1m"
        let scaffold = syntheticWith dahaiActions observation

        // 有威胁的家只有立直的对家。
        Assert.Equal<string list>([ "对家已立直" ], scaffold.Threats |> List.map Threat.toDisplay)

        // 危险度逐条挂在试打上，且与那一张对得上：1m 是对家的现物。
        let danger (notation: string) =
            match (trialOf notation scaffold).Danger with
            | Some danger -> danger
            | None -> failwith $"{notation} 这一条应当带着危险度"

        Assert.Equal(DangerTier.Genbutsu, (danger "1m").Tier)
        Assert.Equal(1, (danger "1m").Rank)
        Assert.Equal(DangerTier.NoEvidence, (danger "5z").Tier)
        Assert.True((danger "1m").Rank < (danger "5z").Rank)

    [<Fact>]
    let ``没人立直也没人副露时不给危险度`` () =
        // 危险度没有被评价的对象时宁可不给数（DECISIONS 25-1）。
        let scaffold = syntheticWith dahaiActions (observationWith drawn "" [] "")

        Assert.Empty(scaffold.Threats)
        Assert.All(scaffold.Dahai, fun trial -> Assert.Equal(None, trial.Danger))
