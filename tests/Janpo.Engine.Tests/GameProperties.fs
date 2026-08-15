namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameFixtures

/// 一整场对局的不变量：点数与供托的总和守恒、点数跨局结转、场况不出局数序列、
/// 顺位由点数决定。具名用例见 GameTests。
///
/// **这里同样不断言点数的具体数值**（裁决 D-4）：断言的是总和、结转与序关系，
/// 08 票把和了点数从 0 改成真值之后，这些性质一条都不用改。
[<Properties(Arbitrary = [| typeof<GameArbitraries> |])>]
module GameProperties =

    [<Property>]
    let ``任意时刻四家点数与供托之和恒为初始总点`` (game: Game) =
        let ruleset = Game.ruleset game

        List.sum (Game.scores game) + Ruleset.kyotakuScore ruleset (Game.kyotaku game) = Ruleset.startingTotal ruleset

    [<Property>]
    let ``下一局的局初点数就是上一局终了时的点数`` (game: Game) =
        match List.tryLast (Game.played game), Game.nextKyoku game with
        | Some state, Some context -> GameState.scores state = context.Scores
        | Some _, None
        | None, _ -> true

    [<Property>]
    let ``下一局的场况必然是局数序列里的一项`` (game: Game) =
        match Game.nextKyoku game with
        | Some context -> List.contains (context.Bakaze, context.Kyoku) (Ruleset.kyokus (Game.ruleset game))
        | None -> true

    [<Property>]
    let ``对局已终，当且仅当没有下一局而有终局精算`` (game: Game) =
        if Game.isEnded game then
            Option.isNone (Game.nextKyoku game) && Option.isSome (Game.result game)
        else
            Option.isSome (Game.nextKyoku game) && Option.isNone (Game.result game)

    [<Property>]
    let ``打完的每一局都已经终了，且亲是那一局场况里的亲`` (game: Game) =
        Game.played game
        |> List.forall (fun state ->
            let context = GameState.context state
            GameState.isEnded state && Seat.isValid (Game.ruleset game) context.Oya)

    [<Property>]
    let ``终局精算只换供托的归属，不改变点数与供托的总和`` (settlement: Settlement) =
        let ruleset = Ruleset.yonma
        let result = Game.settle ruleset settlement.Kyotaku settlement.Scores

        List.sum result.Scores = List.sum settlement.Scores + Ruleset.kyotakuScore ruleset settlement.Kyotaku

    [<Property>]
    let ``顺位是每家一个名次的排列`` (settlement: Settlement) =
        let result = Game.settle Ruleset.yonma settlement.Kyotaku settlement.Scores

        List.sort result.Juni = [ 1 .. List.length settlement.Scores ]

    [<Property>]
    let ``点数高的顺位必然靠前，同点则起家方向在前的靠前`` (settlement: Settlement) =
        let result = Game.settle Ruleset.yonma settlement.Kyotaku settlement.Scores

        let pairs =
            [
                for left in 0 .. List.length settlement.Scores - 1 do
                    for right in 0 .. List.length settlement.Scores - 1 do
                        if left <> right then
                            left, right
            ]

        pairs
        |> List.forall (fun (left, right) ->
            let leftScore = List.item left settlement.Scores
            let rightScore = List.item right settlement.Scores
            let leftJuni = List.item left result.Juni
            let rightJuni = List.item right result.Juni

            if leftScore > rightScore then
                leftJuni < rightJuni
            elif leftScore = rightScore then
                (leftJuni < rightJuni) = (left < right)
            else
                leftJuni > rightJuni)
