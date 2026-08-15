namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 点数授受的生成器零件。测试模块用 `ScoreArbitraries` 注册，不直接用这里的东西。
module private ScoreGeneration =

    /// 符：一般型的合法取值（20 起、10 一档）加上七对子的 25。
    let fuGen: Gen<int> = Gen.elements [ 20; 25; 30; 40; 50; 60; 70; 80; 90; 100; 110 ]

    /// 番与役满倍数：役满以下压倒性多数，役满与复合役满各占一点。
    let valueGen: Gen<HoraValue> =
        gen {
            let! fu = fuGen

            let! yakuman = Gen.frequency [ 8, Gen.constant 0; 1, Gen.constant 1; 1, Gen.constant 2 ]

            let! han = Gen.choose (1, 20)

            return
                {
                    Fu = fu
                    Han = if yakuman > 0 then 0 else han
                    Yakuman = yakuman
                }
        }

    /// 授受条件：亲与和了者随机，自摸与荣和各半，本场与供托小范围随机。
    let transferGen (ruleset: Ruleset) : Gen<HoraTransfer> =
        gen {
            let! oya = Gen.elements (Seat.all ruleset)
            let! actor = Gen.elements (Seat.all ruleset)
            let! tsumo = Gen.elements [ true; false ]

            let! target = Seat.all ruleset |> List.filter (fun seat -> seat <> actor) |> Gen.elements

            let! honba = Gen.choose (0, 5)
            let! kyotaku = Gen.choose (0, 4)

            return
                {
                    Actor = actor
                    Target = (if tsumo then actor else target)
                    Oya = oya
                    Honba = honba
                    Kyotaku = kyotaku
                }
        }

/// 一次和了的授受条件与符番。反例要能一眼读懂，因此带 `ToString`。
type HoraCase =
    | HoraCase of HoraTransfer * HoraValue

    override this.ToString() =
        let (HoraCase(transfer, value)) = this

        let how =
            if transfer.Actor = transfer.Target then
                "自摸"
            else
                $"荣和座位 {transfer.Target}"

        $"座位 {transfer.Actor}（亲 {transfer.Oya}）{how} {value.Fu} 符 {value.Han} 番 役满 {value.Yakuman} 倍，"
        + $"{transfer.Honba} 本场 {transfer.Kyotaku} 供托"

/// 点数授受的 FsCheck 生成器集合。测试模块用
/// `[<Properties(Arbitrary = [| typeof<ScoreArbitraries> |])>]` 注册。
type ScoreArbitraries =

    static member HoraCase() : Arbitrary<HoraCase> =
        gen {
            let! transfer = ScoreGeneration.transferGen Ruleset.yonma
            let! value = ScoreGeneration.valueGen
            return HoraCase(transfer, value)
        }
        |> Arb.fromGen
