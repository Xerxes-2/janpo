namespace Janpo.Web

open Thoth.Json.JavaScript
open Janpo

/// 一次曳光弹（19 票）的结果：**引擎在浏览器里算出来的**那几行数。
///
/// 它存在的唯一理由是「双目标语义没漂」这件事要有一个能逐项对照的形状——
/// `Scores` 与 `Juni` 就是要与 dotnet 侧 `janpo kyoku` / `janpo game` 对齐的两行。
/// 牌桌长什么样是 22 票的事，这里不碰渲染。
type Trace = {
    /// 种子。同一种子在 dotnet 与 Fable 两个目标上必然跑出同一场。
    Seed: int
    /// 跑了几局：`Tracer.kyoku` 恒为 1，`Tracer.game` 是局数序列走完的局数（连庄各算一局）。
    Kyokus: int
    /// 产出的 mjai 事件流。**不含 `start_game`**——它的 names 来自配桌，不是引擎算得出来的。
    Events: Event list
    /// 终局各家点数，按座位升序。
    Scores: int list
    /// 各家顺位，按座位升序，1 起（1 是头名）。
    Juni: int list
}

/// 曳光弹的驱动。规则与算法全在 `Janpo.Engine` 里，这里只是把它跑一遍再摆成 `Trace`。
[<RequireQualifiedAccess>]
module Tracer =

    // ---- 默认场次 ----

    /// 页面初次打开时跑的那个种子。挑它是因为它同时覆盖和了、听牌料、连庄与同点顺位：
    /// 单局以荣和终（点数 30800 / 25000 / 19200 / 25000），整场打满 6 局（两次连庄），
    /// 终局有两家同为 24000（顺位靠起家方向拆分）。选种子的探针见报告 19。
    let defaultSeed = 1177

    // ---- 内部 ----

    /// 顺位：按点数排，同点时起家方向在前的那家名次高。
    ///
    /// 借 `Game.settle` 算而不自己再排一遍——顺位规则只该有一处实现。
    /// **供托传 0**：一局打完时场上可能还剩立直棒，而它要到终局精算才归属；
    /// 传 0 使 `settle` 只排名次、不动点数，于是 `Scores` 仍是引擎给的那一行。
    let private juniOf (ruleset: Ruleset) (scores: int list) : int list = (Game.settle ruleset 0 scores).Juni

    // ---- 跑 ----

    /// 跑固定种子的**一局**：四个随机选手把这一局打到终。对应 `janpo kyoku <种子>`。
    let kyoku (ruleset: Ruleset) (seed: int) : Result<Trace, string> =
        Rng.ofSeed seed
        |> Kyoku.runRandom ruleset (KyokuContext.initial ruleset)
        |> Result.mapError KyokuError.toDisplay
        |> Result.map (fun (state, _) ->
            let scores = GameState.scores state

            {
                Seed = seed
                Kyokus = 1
                Events = GameState.events state
                Scores = scores
                Juni = juniOf ruleset scores
            })

    /// 跑固定种子的**一整场**：局数序列走完为止，终局精算给出点数与顺位。
    /// 对应 `janpo game <种子>`。
    let game (ruleset: Ruleset) (seed: int) : Result<Trace, string> =
        Rng.ofSeed seed
        |> Game.runRandom ruleset
        |> Result.mapError KyokuError.toDisplay
        |> Result.bind (fun (played, _) ->
            match Game.result played with
            | None -> Error "这场对局没有走到终局精算"
            | Some result ->
                Ok {
                    Seed = seed
                    Kyokus = Game.played played |> List.length
                    Events = Game.events played
                    Scores = result.Scores
                    Juni = result.Juni
                })

    // ---- 渲染层出口 ----

    /// 一条 mjai 事件的 JSON。**JSON 的 JS 后端属于本工程**：引擎只依赖 `Thoth.Json.Core`
    /// 这层抽象，具体后端（这里的 `Thoth.Json.JavaScript`、CLI 侧的 `Thoth.Json.Newtonsoft`）
    /// 各自由宿主工程带——`scripts/ci.sh` 的引擎依赖闸门守的就是这条。
    let eventJson: Event -> string = Event.encoder >> Encode.toString 0
