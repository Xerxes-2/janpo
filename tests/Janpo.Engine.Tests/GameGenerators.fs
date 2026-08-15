namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 一整场对局的固件：合成的终局形态（推进规则的黄金用例用它，不必真打一局）、
/// 跑完整场的驱动，以及逐局的全部状态。
module GameFixtures =

    let tonpuusen = Ruleset.yonma

    let hanchan = Ruleset.withLength Hanchan Ruleset.yonma

    /// 一手随便写的牌：和了的哪张牌本层不关心（连庄只看「谁和了」）。
    let private anyPai =
        match Tile.parse "1m" with
        | Ok tile -> tile
        | Error error -> failwith $"1m 应当合法，却得到 {TileParseError.toDisplay error}"

    /// 合成一次和了收尾。**点数字段一律记 0**：08 票才填真值，本层只读「谁和了」，
    /// 因此这些固件对 08 的改动天然免疫（裁决 D-4）。
    let horaBy (actor: Seat) (scores: int list) : KyokuEnd =
        KyokuEnd.Hora
            [
                {
                    Actor = actor
                    Target = actor
                    Pai = anyPai
                    Fu = 0
                    Fan = 0
                    HoraPoints = 0
                    Deltas = scores |> List.map (fun _ -> 0)
                    Scores = scores
                    UraDoraMarkers = []
                }
            ]

    /// 合成一次荒牌流局。`tenpais` 决定谁听牌——亲听不听正是连庄判定读的那一位。
    let ryuukyokuWith (tenpais: bool list) (scores: int list) : KyokuEnd =
        KyokuEnd.Ryuukyoku
            {
                Reason = Fanpai
                Tenpais = tenpais
                Deltas = scores |> List.map (fun _ -> 0)
                Scores = scores
            }

    /// 下一局的场况；已经终局就让用例失败（用例自己知道该不该终局）。
    let nextOf (progress: GameProgress) : KyokuContext =
        match progress with
        | GameProgress.NextKyoku context -> context
        | GameProgress.Ended result -> failwith $"这里应当还有下一局，却已经终局：{GameResult.toDisplay result}"

    /// 终局精算；还没终局就让用例失败。
    let resultOf (progress: GameProgress) : GameResult =
        match progress with
        | GameProgress.Ended result -> result
        | GameProgress.NextKyoku context -> failwith $"这里应当已经终局，却还有第 {context.Kyoku} 局"

    /// 用摊好的牌山与**给定的场况**开一局，再让「见和就和」的选手把它打完。
    /// 和了收尾这条路只有摊牌山跑得出来（随机选手扫过四百个种子一次都没和了），
    /// 而「和了 → 下一局的场况」正是本票要验的那一段。
    let playScripted (context: KyokuContext) (script: GameStateFixtures.Script) : GameState =
        let hands = script.Hands |> List.map GameStateFixtures.tilesOf

        let wall =
            GameStateFixtures.scriptedWall tonpuusen context.Oya hands (GameStateFixtures.tilesOf script.Draws)

        let state =
            match GameState.startFrom tonpuusen context wall with
            | Ok state -> state
            | Error error -> failwith $"应当开得出局，却得到 {KyokuStartError.toDisplay error}"

        match Kyoku.run GameStateFixtures.horaSeeking (Rng.ofSeed 1) state with
        | Ok(final, _) -> final
        | Error illegal -> failwith $"这一局应当跑得完，却得到「{IllegalAction.toDisplay illegal}」"

    /// 选手自己的发生器与牌山的分开：两者同种子会跑出同一串数，选牌与洗牌就相关了。
    let private playerRng (seed: int) : Rng = Rng.ofSeed (seed + 9973)

    /// 把一整场对局跑完。跑不完说明前序票坏了，直接让测试失败。
    let runWith (player: Player<Rng>) (ruleset: Ruleset) (seed: int) : Game =
        match Game.run player (playerRng seed) (Rng.ofSeed seed) (Game.start ruleset) with
        | Ok(game, _, _) -> game
        | Error error -> failwith $"这一场应当跑得完，却得到「{KyokuError.toDisplay error}」"

    /// 一场对局**逐局**的全部状态，含开局（还没打）与终局。不变量在每一步上验。
    let trace (player: Player<Rng>) (ruleset: Ruleset) (seed: int) : Game list =
        let rec loop (game: Game) (current: Rng) (rng: Rng) (visited: Game list) =
            if Game.isEnded game then
                List.rev (game :: visited)
            else
                match Game.play player current rng game with
                | Error error -> failwith $"这一场应当跑得完，却得到「{KyokuError.toDisplay error}」"
                | Ok(next, advanced, advancedRng) -> loop next advanced advancedRng (game :: visited)

        loop (Game.start ruleset) (playerRng seed) (Rng.ofSeed seed) []

    /// 属性测试取样用的几场对局。整场对局跑起来不便宜（一局七十手），
    /// 因此跑一次存下来给全部属性共用。**是 `lazy` 而不是模块级的值**：
    /// 模块级的值会在任何一条用到本模块的用例里被初始化，让完全用不着它的黄金用例
    /// 也跟着跑七场对局。
    let private cached =
        lazy
            [
                for seed in 1..3 do
                    trace Kyoku.randomPlayer tonpuusen seed
                    trace GameStateFixtures.tenpaiSeeking tonpuusen seed
                trace Kyoku.randomPlayer hanchan 1
            ]

    let traces () : Game list list = cached.Value

/// 终局精算的输入：各家点数与场上还剩的供托。
type Settlement = { Scores: int list; Kyotaku: int }

/// 对局与精算输入的生成器。对局只生成**可达**的那些：真跑一场，随机取其中一局的进程。
type GameArbitraries =

    static member Game() : Arbitrary<Game> =
        gen {
            let! games = Gen.elements (GameFixtures.traces ())
            let! index = Gen.choose (0, List.length games - 1)
            return List.item index games
        }
        |> Arb.fromGen

    static member Settlement() : Arbitrary<Settlement> =
        // 一半的点数从一小把取值里选：均匀随机的点数几乎碰不到同点，
        // 而同点才是顺位的边界（同点时起家方向在前的名次高）。
        let score =
            Gen.frequency [ 1, Gen.choose (-30000, 80000); 1, Gen.elements [ 24000; 25000; 26000 ] ]

        gen {
            let! scores = Gen.listOfLength Ruleset.yonma.SeatCount score
            let! kyotaku = Gen.choose (0, 5)
            return { Scores = scores; Kyotaku = kyotaku }
        }
        |> Arb.fromGen
