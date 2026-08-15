namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Janpo

/// 符的不变量。牌姿取自 `AgariCase`（一定成立的一般型和了牌姿 + 随机上下文），
/// 具名用例见 FuTests。
[<Properties(Arbitrary = [| typeof<YakuArbitraries> |])>]
module FuProperties =

    let private ruleset = Ruleset.yonma

    /// 这副牌姿的全部读法各自的符。无役的牌姿没有读法，得空表。
    let private fuOf (context: YakuContext) (hand: AgariHand) : Fu list =
        Yaku.candidates ruleset context hand
        |> List.map (Fu.calculate ruleset context hand)

    [<Property>]
    let ``符恒是 10 的倍数，只有七对子的 25 符例外`` (AgariCase(hand, context)) =
        fuOf context hand
        |> List.forall (fun fu ->
            let value = Fu.value fu
            value % 10 = 0 || value = 25)

    [<Property>]
    let ``符恒不低于底符 20`` (AgariCase(hand, context)) =
        fuOf context hand |> List.forall (fun fu -> Fu.value fu >= 20)

    [<Property>]
    let ``切上前的合计不超过切上后的符，且两者相差不到 10`` (AgariCase(hand, context)) =
        fuOf context hand
        |> List.forall (fun fu -> Fu.total fu <= Fu.value fu && Fu.value fu - Fu.total fu < 10)

    [<Property>]
    let ``平和的读法自摸恒 20 符、荣和恒 30 符`` (AgariCase(hand, context)) =
        // 平和意味着门清、四顺子、非役牌雀头、两面听：分项里只剩底符
        // 与门清荣和（自摸符不加——平和自摸恒 20 符）。
        Yaku.candidates ruleset context hand
        |> List.filter (fun tally -> YakuTally.yaku tally |> List.contains Yaku.Pinfu)
        |> List.forall (fun tally ->
            let fu = Fu.value (Fu.calculate ruleset context hand tally)

            if AgariHand.isTsumo hand then fu = 20 else fu = 30)

    [<Property>]
    let ``连风牌雀头只在场风与自风同一种时才比单风多`` (AgariCase(hand, context)) =
        // 规则集把连风牌雀头调到 2 符时，只有「场风 = 自风」的手牌会变；其余一符不动。
        let renfonIsTwo = { ruleset with DoubleKazeJantouFu = 2 }

        let sameKaze = context.Bakaze = context.Jikaze

        let values (ruleset: Ruleset) =
            Yaku.candidates ruleset context hand
            |> List.map (Fu.calculate ruleset context hand >> Fu.total)

        sameKaze || values ruleset = values renfonIsTwo
