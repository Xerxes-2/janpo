namespace Janpo.Engine.Tests

open Xunit
open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.AgariFixture
open Janpo.Engine.Tests.GameStateFixtures

/// 兜底（票 23）：Player 交不出合法动作时代打的那一手。**它只挑，不发明**——
/// 代打的动作恒取自那一手的决策包，因此引擎必然接受它，对局不会卡死。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |], Parallelism = 4)>]
module FallbackTests =

    let private packageFor (seat: Seat) (state: GameState) : DecisionPackage =
        match DecisionPackage.forSeat seat state with
        | Some package -> package
        | None -> failwith "这个座位应当正在被问"

    /// 摊好的牌山：Oya 听 5z 单骑，第 1 巡摸进 1z。合法动作集里既有立直宣言，
    /// 也有 13 条手切与一条摸切 1z。
    let private opening = startScripted tsumoHoraScript

    let private packagesOf (state: GameState) : DecisionPackage list =
        GameState.legalActions state
        |> List.choose (fun choice -> DecisionPackage.forSeat choice.Seat state)

    [<Fact>]
    let ``Bare 档摸切：刚摸进那张原样打出去，不立直也不手切`` () =
        let package = packageFor (seat 0) opening

        Assert.Equal(Action.Dahai(seat 0, tile "1z", true), Fallback.action ScaffoldTier.Bare package)

    [<Fact>]
    let ``响应阶段兜底成「过」：轮不到打牌，最保守的一手是不要`` () =
        // Oya 摸切 5s，座位 1 手里三张 5s——它碰得、大明杠得，于是有一个响应阶段。
        let responding =
            match GameState.step (startScripted minkanScript) (Action.Dahai(seat 0, tile "5s", true)) with
            | Ok(next, _) -> next
            | Error illegal -> failwith $"摸切应当合法，却得到「{IllegalAction.toDisplay illegal}」"

        let asked =
            GameState.legalActions responding |> List.map (fun choice -> choice.Seat)

        Assert.NotEmpty(asked)

        for seat in asked do
            let package = packageFor seat responding
            Assert.Equal(Action.None seat, Fallback.action ScaffoldTier.Bare package)

    /// 摸进来那张让手牌从一向听进了听牌：**摸切等于把它扭头扔回去**（退向），
    /// 不退向听的那一手是手切孤张 6z。三家的配牌沿用自摸和的剧本（既和不了也听不了）。
    let private tsumogiriRetreatsScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 5z 6z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 4z 6z 7z 9m"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 4z 6z 7z 9m"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 4z 6z 7z 9m"
                ]
            Draws = "5z"
        }

    let private retreating = startScripted tsumogiriRetreatsScript

    [<Fact>]
    let ``Assisted 档不退向听：摸切会退向时改打那张孤张`` () =
        let package = packageFor (seat 0) retreating

        // Bare 档照旧：摸切。它把刚摸进的雀头扭头扔了，从听牌退回一向听。
        Assert.Equal(Action.Dahai(seat 0, tile "5z", true), Fallback.action ScaffoldTier.Bare package)

        // Assisted 档：打掉那张谁也不搭的 6z，听牌保住。
        Assert.Equal(Action.Dahai(seat 0, tile "6z", false), Fallback.action ScaffoldTier.Assisted package)

    [<Fact>]
    let ``Assisted 档不退向听时仍然摸切：不为了不同而不同`` () =
        // 摸进来的 1z 谁也不搭，摸切本就不退向——两档同一手。
        let package = packageFor (seat 0) opening

        Assert.Equal(Fallback.action ScaffoldTier.Bare package, Fallback.action ScaffoldTier.Assisted package)

    /// 立直的剧本（票 25）：座位 1 是听 4p / 7p 的门清形，第 1 巡摸进 1z 就宣言立直、打出 1z。
    ///
    /// 座位 0 是三副顺子加四张孤张字牌的 2 向听：**打掉哪张字牌都不退向听**，
    /// 因此「不退向听」挑不出唯一一手，安全度才决定得了。
    let private riichiAheadScript =
        {
            Hands =
                [
                    "1m 2m 3m 4p 5p 6p 7s 8s 9s 1z 2z 3z 4z"
                    "2m 3m 4m 5m 6m 7m 2p 3p 4p 5p 6p 9s 9s"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 4z 6z 7z 9m"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 4z 6z 7z 9m"
                ]
            Draws = "7z 1z 6z 6z 5z"
        }

    /// 下家（座位 1）立直了，而座位 0 刚摸进 5z。
    let private riichiAhead =
        let asked (state: GameState) =
            let riichi =
                GameState.player (seat 1) state
                |> Option.map (PlayerState.riichi >> RiichiState.isActive)
                |> Option.defaultValue false

            let drawn =
                GameState.player (seat 0) state
                |> Option.map (PlayerState.drawn >> Option.isSome)
                |> Option.defaultValue false

            let turn =
                GameState.legalActions state |> List.exists (fun choice -> choice.Seat = seat 0)

            riichi && drawn && turn

        driveUntil riichiSeeking asked (startScripted riichiAheadScript)

    [<Fact>]
    let ``Assisted 档是安全打：不退向听的那几手里挑危险度最低的`` () =
        let package = packageFor (seat 0) riichiAhead

        // Bare 档照旧：摸切刚摸进的 5z。它不退向听，因此 24 号票的 Assisted 也打它。
        Assert.Equal(Action.Dahai(seat 0, tile "5z", true), Fallback.action ScaffoldTier.Bare package)

        // Assisted 档：下家的立直宣言牌就是 1z，打它既不退向听又是现物。
        Assert.Equal(Action.Dahai(seat 0, tile "1z", false), Fallback.action ScaffoldTier.Assisted package)

    [<Fact>]
    let ``安全打不放宽不退向听：两个条件是串联的`` () =
        // 候选先过「进退向为 0」这道闸，再比安全度；反过来就会为了安全而退向听。
        let package = packageFor (seat 0) riichiAhead
        let chosen = Fallback.action ScaffoldTier.Assisted package

        let scaffold =
            match DecisionPackage.scaffold package with
            | Some scaffold -> scaffold
            | None -> failwith "这一手的脚手架应当算得出来"

        // 选中的那一手在不退向听的候选里，且它的危险度名次是那批候选里最小的。
        let keeping = scaffold.Dahai |> List.filter (fun trial -> trial.ShantenDelta = 0)

        let ranks =
            keeping
            |> List.choose (fun trial -> trial.Danger)
            |> List.map (fun danger -> danger.Rank)

        let picked =
            keeping
            |> List.filter (fun trial ->
                trial.ActionIds
                |> List.exists (fun id -> DecisionPackage.tryAction id package = Some chosen))

        Assert.NotEmpty(picked)

        Assert.Equal(
            List.min ranks,
            (List.head picked).Danger
            |> Option.map (fun danger -> danger.Rank)
            |> Option.get
        )

    [<Fact>]
    let ``ToolSearch 暂时照 Bare 打：它是 M3 的事`` () =
        for state in [ opening; retreating ] do
            let package = packageFor (seat 0) state

            Assert.Equal(Fallback.action ScaffoldTier.Bare package, Fallback.action ScaffoldTier.ToolSearch package)

    [<Fact>]
    let ``响应阶段 Assisted 也兜底成「过」：没牌可打就退回 Bare 的那三级`` () =
        let responding =
            match GameState.step (startScripted minkanScript) (Action.Dahai(seat 0, tile "5s", true)) with
            | Ok(next, _) -> next
            | Error illegal -> failwith $"摸切应当合法，却得到「{IllegalAction.toDisplay illegal}」"

        for choice in GameState.legalActions responding do
            let package = packageFor choice.Seat responding

            Assert.Equal(Action.None choice.Seat, Fallback.action ScaffoldTier.Assisted package)

    [<Property>]
    let ``任意局面，兜底出来的那一手引擎都接受`` (state: GameState) =
        // 三档一起验：兜底**只挑不发明**，哪一档都不得把牌桌卡住。
        packagesOf state
        |> List.forall (fun package ->
            let options = DecisionPackage.options package |> List.map ActionOption.action

            ScaffoldTier.all
            |> List.forall (fun tier ->
                let action = Fallback.action tier package
                List.contains action options && (GameState.step state action |> Result.isOk)))

    [<Property>]
    let ``任意局面，Assisted 档有不退向听的打法就不退向听`` (state: GameState) =
        packagesOf state
        |> List.forall (fun package ->
            match DecisionPackage.scaffold package with
            | None -> true
            | Some scaffold ->
                let keeping =
                    scaffold.Dahai
                    |> List.filter (fun trial -> trial.ShantenDelta = 0)
                    |> List.collect (fun trial -> trial.ActionIds)

                match keeping with
                | [] -> true
                | ids ->
                    let chosen = Fallback.action ScaffoldTier.Assisted package

                    ids
                    |> List.choose (fun id -> DecisionPackage.tryAction id package)
                    |> List.contains chosen)

    [<Property>]
    let ``任意局面，合法动作集里有摸切就一定摸切`` (state: GameState) =
        packagesOf state
        |> List.forall (fun package ->
            let options = DecisionPackage.options package |> List.map ActionOption.action

            let tsumogiri =
                options
                |> List.tryFind (fun action ->
                    match action with
                    | Action.Dahai(_, _, true) -> true
                    | _ -> false)

            match tsumogiri with
            | None -> true
            | Some expected -> Fallback.action ScaffoldTier.Bare package = expected)
