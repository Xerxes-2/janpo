namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 事件的生成器。后续票每加一个 Event case，就在 `Event()` 里加一支生成器，
/// JSON 往返的属性测试便自动覆盖到它。
type EventArbitraries =

    static member Kaze() : Arbitrary<Kaze> = Gen.elements Kaze.all |> Arb.fromGen

    static member Event() : Arbitrary<Event> =
        let tile = Gen.elements Tile.all
        let seat = Gen.choose (0, 3)

        let startGame =
            gen {
                let! names = Gen.listOfLength 4 (Gen.elements [ "p0"; "gpt"; "克劳德"; "a \"b\"" ])
                return StartGame names
            }

        let startKyoku =
            gen {
                let! bakaze = Gen.elements Kaze.all
                let! kyoku = Gen.choose (1, 4)
                let! honba = Gen.choose (0, 9)
                let! kyotaku = Gen.choose (0, 4)
                let! oya = seat
                let! doraMarker = tile
                let! scores = Gen.listOfLength 4 (Gen.choose (-30000, 80000))
                let! tehais = Gen.listOfLength 4 (Gen.listOfLength 13 tile)

                return
                    StartKyoku
                        {
                            Bakaze = bakaze
                            Kyoku = kyoku
                            Honba = honba
                            Kyotaku = kyotaku
                            Oya = oya
                            DoraMarker = doraMarker
                            Scores = scores
                            Tehais = tehais
                        }
            }

        let tsumo =
            gen {
                let! actor = seat
                let! pai = tile
                return Tsumo(actor, pai)
            }

        let dahai =
            gen {
                let! actor = seat
                let! pai = tile
                let! tsumogiri = Gen.elements [ true; false ]
                return Dahai(actor, pai, tsumogiri)
            }

        let ryuukyoku =
            gen {
                let! reason = Gen.elements RyuukyokuReason.all
                let! tenpais = Gen.listOfLength 4 (Gen.elements [ true; false ])
                let! deltas = Gen.listOfLength 4 (Gen.choose (-3000, 3000))
                let! scores = Gen.listOfLength 4 (Gen.choose (-30000, 80000))

                return
                    Ryuukyoku
                        {
                            Reason = reason
                            Tenpais = tenpais
                            Deltas = deltas
                            Scores = scores
                        }
            }

        Gen.oneof [ startGame; startKyoku; tsumo; dahai; ryuukyoku ] |> Arb.fromGen
