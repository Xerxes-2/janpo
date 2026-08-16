namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 脚手架的不变量（票 24）。四条都是**跨任意可达局面**的：具名用例只钉得住那几副摊好的手牌，
/// 而「可见张数会不会数重」「进退向会不会是负的」这类错误要靠随机局面撞出来。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |], Parallelism = 4)>]
module ScaffoldProperties =

    let private scaffoldsOf (state: GameState) : Scaffold list =
        GameState.legalActions state
        |> List.choose (fun choice -> DecisionPackage.forSeat choice.Seat state)
        |> List.choose DecisionPackage.scaffold

    [<Property>]
    let ``任意局面，正在被问的每个座位都有脚手架`` (state: GameState) =
        let packages =
            GameState.legalActions state
            |> List.choose (fun choice -> DecisionPackage.forSeat choice.Seat state)

        List.length (scaffoldsOf state) = List.length packages

    [<Property>]
    let ``任意局面，有效牌恒算得出来：可见张数从不数重`` (state: GameState) =
        // 被鸣走的那张同时在河里与副露里出现，多数一遍就会超过 4 张，
        // `Ukeire` 判可见张数越界，有效牌整个变成 None——这条属性就是那个错的探针。
        scaffoldsOf state
        |> List.forall (fun scaffold ->
            let trials = scaffold.Dahai |> List.forall (fun trial -> Option.isSome trial.Ukeire)

            // 已摸进的手牌没有有效牌（要先试打一张），此外都该算得出来。
            trials && (List.isEmpty scaffold.Dahai |> not || Option.isSome scaffold.Ukeire))

    [<Property>]
    let ``任意局面，剩余枚数落在 0 到 4 之间`` (state: GameState) =
        let sane (ukeire: Ukeire option) =
            match ukeire with
            | None -> true
            | Some ukeire ->
                ukeire.Tiles
                |> List.forall (fun (_, remaining) -> remaining >= 0 && remaining <= 4)

        scaffoldsOf state
        |> List.forall (fun scaffold ->
            sane scaffold.Ukeire
            && (scaffold.Dahai |> List.forall (fun trial -> sane trial.Ukeire)))

    [<Property>]
    let ``任意局面，打一张牌不会让向听数变小`` (state: GameState) =
        // 进退向恒 >= 0。**能打牌的手牌至少有一条不退向**（打出去那张本来就是多出来的），
        // 除非手牌已经成了和了型——那时打哪张都是拆和了型。
        scaffoldsOf state
        |> List.forall (fun scaffold ->
            let nonNegative =
                scaffold.Dahai |> List.forall (fun trial -> trial.ShantenDelta >= 0)

            let keepsShape =
                List.isEmpty scaffold.Dahai
                || Shanten.isAgari scaffold.Shanten
                || scaffold.Dahai |> List.exists (fun trial -> trial.ShantenDelta = 0)

            nonNegative && keepsShape)
