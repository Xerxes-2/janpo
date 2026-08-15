namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 局面的固件：开局、把一局跑完、以及两种选手。具名用例与属性共用。
module GameStateFixtures =

    let ruleset = Ruleset.yonma

    let context = KyokuContext.initial ruleset

    let kindSet = TileKindSet.ofKinds ruleset.TileKinds

    /// 开一局。开不出来说明前序票坏了，直接让测试失败。
    let start (seed: int) : GameState * Rng =
        match GameState.start ruleset context (Rng.ofSeed seed) with
        | Ok pair -> pair
        | Error error -> failwith $"应当开得出局，却得到 {KyokuStartError.toDisplay error}"

    let removeOne (tile: Tile) (tiles: Tile list) : Tile list =
        let rec loop skipped rest =
            match rest with
            | [] -> List.rev skipped
            | head :: tail when head = tile -> List.rev skipped @ tail
            | head :: tail -> loop (head :: skipped) tail

        loop [] tiles

    let shantenOf (tiles: Tile list) : int =
        match HandShape.create 0 tiles with
        | Ok shape -> Shanten.value (Shanten.calculate kindSet shape)
        | Error _ -> System.Int32.MaxValue

    /// 尽量保持听牌的选手：打出后向听数最小的那张（并列取合法动作集里靠前的那条）。
    /// 随机选手几乎永远不听牌，听牌料的黄金用例要它。
    let tenpaiSeeking: Player<Rng> =
        fun rng state choice ->
            let hand =
                GameState.player choice.Seat state
                |> Option.map PlayerState.hand
                |> Option.defaultValue []

            let chosen =
                choice.Actions
                |> List.minBy (fun action ->
                    match action with
                    | Action.Dahai(_, pai, _) -> shantenOf (removeOne pai hand))

            chosen, rng

    /// 一局从开局到终局逐步的全部局面，含开局与终局。不变量在每一步上验。
    let trace (player: Player<Rng>) (seed: int) : GameState list =
        let rec loop (rng: Rng) (state: GameState) (visited: GameState list) =
            match GameState.legalActions state with
            | [] -> List.rev (state :: visited)
            | choice :: _ ->
                let action, advanced = player rng state choice

                match GameState.step state action with
                | Error illegal -> failwith $"合法动作集里的动作应当被接受，却得到「{IllegalAction.toDisplay illegal}」"
                | Ok(next, _) -> loop advanced next (state :: visited)

        let state, rng = start seed
        loop rng state []

    /// 把一局跑完，返回终局的局面。
    let runWith (player: Player<Rng>) (seed: int) : GameState =
        let state, rng = start seed

        match Kyoku.run player rng state with
        | Ok(final, _) -> final
        | Error illegal -> failwith $"这一局应当跑得完，却得到「{IllegalAction.toDisplay illegal}」"

    /// 跑一局，同时记下每一步提交的动作——回放要的就是这串动作。
    let record (player: Player<Rng>) (seed: int) : GameState * Action list =
        let rec loop (rng: Rng) (state: GameState) (taken: Action list) =
            match GameState.legalActions state with
            | [] -> state, List.rev taken
            | choice :: _ ->
                let action, advanced = player rng state choice

                match GameState.step state action with
                | Error illegal -> failwith $"合法动作集里的动作应当被接受，却得到「{IllegalAction.toDisplay illegal}」"
                | Ok(next, _) -> loop advanced next (action :: taken)

        let state, rng = start seed
        loop rng state []

    /// 回放：把一串动作从给定局面重新走一遍。
    let replay (actions: Action list) (state: GameState) : Result<GameState, IllegalAction> =
        (Ok state, actions)
        ||> List.fold (fun current action ->
            current
            |> Result.bind (fun state -> GameState.step state action |> Result.map fst))

    /// 终局的流局结果。
    let ryuukyokuOf (state: GameState) : Ryuukyoku =
        match GameState.ryuukyoku state with
        | Some result -> result
        | None -> failwith "这一局应当已经终了"

/// 局面与动作的生成器。局面只生成**可达**的那些：随机开一局、随机走若干步。
type GameStateArbitraries =

    static member GameState() : Arbitrary<GameState> =
        gen {
            let! seed = Gen.choose (1, 400)
            let! seeking = Gen.elements [ true; false ]

            let states =
                GameStateFixtures.trace
                    (if seeking then
                         GameStateFixtures.tenpaiSeeking
                     else
                         Kyoku.randomPlayer)
                    seed

            let! index = Gen.choose (0, List.length states - 1)
            return List.item index states
        }
        |> Arb.fromGen

    /// 随便造的动作：座位可能越界，牌可能不在手里，摸切标志可能是瞎写的。
    /// 「非法动作一律返回值而不抛异常」这条属性要的就是它。
    static member Action() : Arbitrary<Action> =
        gen {
            let! actor = Gen.choose (-1, Ruleset.yonma.SeatCount)
            let! pai = Gen.elements Tile.all
            let! tsumogiri = Gen.elements [ true; false ]
            return Action.Dahai(actor, pai, tsumogiri)
        }
        |> Arb.fromGen
