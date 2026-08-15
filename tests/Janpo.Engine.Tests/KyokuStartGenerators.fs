namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 开局条件的生成器。只生成**能开出局**的条件（座位数与规则集一致），
/// 开不了局的情形由 KyokuStartTests 的具名用例覆盖。
type KyokuStartArbitraries =

    static member KyokuContext() : Arbitrary<KyokuContext> =
        gen {
            let! bakaze = Gen.elements Kaze.all
            let! kyoku = Gen.choose (1, 4)
            let! honba = Gen.choose (0, 5)
            let! kyotaku = Gen.choose (0, 3)
            let! oya = Gen.choose (0, Ruleset.yonma.SeatCount - 1)
            let! scores = Gen.listOfLength Ruleset.yonma.SeatCount (Gen.choose (0, 100000))

            return
                {
                    Bakaze = bakaze
                    Kyoku = kyoku
                    Honba = honba
                    Kyotaku = kyotaku
                    Oya = oya
                    Scores = scores
                }
        }
        |> Arb.fromGen
