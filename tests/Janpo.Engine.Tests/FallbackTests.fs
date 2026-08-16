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

    [<Fact>]
    let ``另外两档暂时照 Bare 打——24 号票在那两支上填「不退向听的安全打」`` () =
        let package = packageFor (seat 0) opening
        let bare = Fallback.action ScaffoldTier.Bare package

        Assert.Equal(bare, Fallback.action ScaffoldTier.Assisted package)
        Assert.Equal(bare, Fallback.action ScaffoldTier.ToolSearch package)

    [<Property>]
    let ``任意局面，兜底出来的那一手引擎都接受`` (state: GameState) =
        packagesOf state
        |> List.forall (fun package ->
            let action = Fallback.action ScaffoldTier.Bare package
            let options = DecisionPackage.options package |> List.map ActionOption.action

            List.contains action options && (GameState.step state action |> Result.isOk))

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
