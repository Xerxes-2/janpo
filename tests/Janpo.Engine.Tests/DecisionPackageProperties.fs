namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 决策包的不变量。**这是 F#/TS 边界的那一半**：跨出去的是包，回来的是一个整数，
/// 因此「包里每个 id 都取得回一个引擎接受的动作」「包外的 id 一律 None」是硬约束。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |], Parallelism = 4)>]
module DecisionPackageProperties =

    let private asked (state: GameState) : Seat list =
        GameState.legalActions state |> List.map (fun choice -> choice.Seat)

    let private packagesOf (state: GameState) : DecisionPackage list =
        asked state |> List.choose (fun seat -> DecisionPackage.forSeat seat state)

    [<Property>]
    let ``任意局面，正在被问的每个座位都有决策包，包里就是它的合法动作集`` (state: GameState) =
        let packages = packagesOf state

        List.length packages = List.length (asked state)
        && packages
           |> List.forall (fun package ->
               let expected =
                   GameState.legalActions state
                   |> List.tryFind (fun choice -> choice.Seat = DecisionPackage.seat package)
                   |> Option.map (fun choice -> choice.Actions)

               Some(DecisionPackage.options package |> List.map ActionOption.action) = expected)

    [<Property>]
    let ``任意局面，没被问到的座位没有决策包`` (state: GameState) =
        Seat.all ruleset
        |> List.filter (fun seat -> not (List.contains seat (asked state)))
        |> List.forall (fun seat -> Option.isNone (DecisionPackage.forSeat seat state))

    [<Property>]
    let ``任意局面，包里每个 id 都取得回一个引擎接受的动作`` (state: GameState) =
        packagesOf state
        |> List.forall (fun package ->
            DecisionPackage.options package
            |> List.forall (fun option ->
                match DecisionPackage.tryAction (ActionOption.id option) package with
                | None -> false
                | Some action ->
                    action = ActionOption.action option
                    && (GameState.step state action |> Result.isOk)))

    [<Property>]
    let ``任意局面，包外的 id 一律是 None 而不是异常`` (state: GameState) =
        // 兜底路径（23 号票）依赖这一条：LLM 给回来的整数什么都可能是。
        packagesOf state
        |> List.forall (fun package ->
            let count = DecisionPackage.options package |> List.length

            [ -1; count; count + 1; System.Int32.MaxValue; System.Int32.MinValue ]
            |> List.forall (fun id -> Option.isNone (DecisionPackage.tryAction id package)))

    [<Property>]
    let ``任意局面，同一包里两条动作的 label 不相同`` (state: GameState) =
        // 亮哪几张是选手的决策（手里 `5m 5m 5mr` 碰 `5m` 有两种亮法，宝牌数不同），
        // 因此 label 必须把它们分开——LLM 与真人看的都是这一行字。
        packagesOf state
        |> List.forall (fun package ->
            let labels = DecisionPackage.options package |> List.map ActionOption.label

            List.length (List.distinct labels) = List.length labels)
