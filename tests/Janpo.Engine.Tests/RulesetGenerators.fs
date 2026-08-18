namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 规则集与种子的生成器。属性测试只跑**发得出牌**的规则集：
/// 红宝牌开关是 SPEC 列出的开关，两种形态都要满足同一批不变量。
///
/// 雀魂那套（`Ruleset.majsoul`）也在里面：不变量不得只对天凤口径成立——
/// 三家和了成立、国士抢暗杠与双倍役满这三处同样要守住它们。
type RulesetArbitraries =

    static member Ruleset() : Arbitrary<Ruleset> =
        Gen.elements [ Ruleset.yonma; Ruleset.withoutAkadora Ruleset.yonma; Ruleset.majsoul ]
        |> Arb.fromGen
