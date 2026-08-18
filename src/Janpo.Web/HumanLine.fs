namespace Janpo.Web

open Feliz
open Janpo

/// 真人坐席那一行（票 87）：**轮不轮到你、能点什么、平台替你过了什么**。
///
/// 与 `AgentLine` 同一个形状：这里只把 `TableState` 算好的那几样画成牌桌顶上的一行，
/// 判据（轮到谁、能点哪几张）在 `TableState.humanTurn` 与 `HumanSeat` 里。
///
/// **没有真人坐席时它一行都不画**：一句「这一桌没有真人」对四家模型那一桌是噪声。
[<RequireQualifiedAccess>]
module HumanLine =

    /// 这一手还合法、但这一票表达不出来的那几条要**说出来**（票 88 会把它们变成按钮）。
    /// 不说的话，「平台不支持立直」与「这一手本来就不能立直」在页面上长得一模一样。
    let private alsoLegal (labels: string list) : string =
        match labels with
        | [] -> ""
        | labels -> "　这一手还能：" + String.concat "、" labels + "（按钮是下一票的事）"

    /// 替你过掉的那几次（票 87）。**最近一次逐条列出来 + 这一桌总共几次**：
    /// 只报个数说不清错过的是碰还是荣和，只报最近一次又看不出这件事一直在发生。
    let private passed (passes: AutoPass list) : string =
        match passes with
        | [] -> ""
        | latest :: _ ->
            let tally =
                match List.length passes with
                | 1 -> ""
                | count -> $"（这一桌共 {count} 次）"

            $"　鸣牌一律自动过：{AutoPass.toDisplay latest}{tally}。"

    /// 真人坐席那一行；这一桌没有真人时是空表。
    ///
    /// `data-*` 给无头闸门读，人读的是那句中文——两头对不上就是错：
    /// `data-human-seat`（坐哪一席）、`data-human`（`waiting` 在等你点 / `watching` 轮到别人）、
    /// `data-human-playable`（此刻点得出去几张）、`data-human-passes`（替你过了几次）。
    let internal at (model: TableModel) : ReactElement list =
        match TableState.humanSeat model with
        | None -> []
        | Some seat ->
            let turn = TableState.humanTurn model
            let passes = TableState.autoPasses model

            let playable =
                turn
                |> Option.map (HumanSeat.dahaiOptions >> List.length)
                |> Option.defaultValue 0

            // 三态各是一句话：**终局那一屏不能再说「轮到别人」**（那时谁的回合都不是），
            // 而且那一刻正是视角与气泡一起松开的时候——这一句要把它说出来，
            // 否则人不知道刚才藏着的那几样现在看得了。
            // 判据直接读 `lockedSeat`（它就是 `unlocked` 的反面），不在这一层再判一遍。
            let state, said =
                match turn, TableState.lockedSeat model with
                | Some package, _ ->
                    "waiting",
                    $"轮到你出牌了（座位 {Seat.index seat}）：点自己手里的一张就打出去，能点的那几张由引擎给的合法动作集定（此刻 {playable} 条）。不限时，整桌等着你。"
                    + alsoLegal (HumanSeat.unspoken package)
                | None, Some _ -> "watching", $"你坐在座位 {Seat.index seat}：轮到别人，看着就好。"
                | None, None -> "settled", $"这一场打完了（你坐的是座位 {Seat.index seat}）：视角与思考气泡都解锁了，四家的牌与推理现在都看得了。"

            [
                Html.p [
                    prop.key "human"
                    prop.className "agent human-line"
                    prop.testId "table-human"
                    prop.custom ("data-human", state)
                    prop.custom ("data-human-seat", Seat.index seat)
                    prop.custom ("data-human-playable", playable)
                    prop.custom ("data-human-passes", List.length passes)
                    prop.text (said + passed passes)
                ]
            ]
