namespace Janpo.Web

open Feliz
open Janpo

/// 强 AI 基线那一行状态线（票 92；ADR-0006 的边界 1 与 2）：
/// **那几 MB 拉没拉、拉不动是为什么、那一席退回了什么**。
///
/// 与 `AgentLine` / `HumanLine` 同一个形状：这里只把 `TableState` 算好的那几样画出来，
/// 判据（拉不拉、退不退）在 `TableState.started` 与 `TableState.degraded` 里。
///
/// **它说的是资产，不是那一席在想什么**（票 92 的要害）：那一席**不会说话**
/// ——没有 thinking、没有一句话理由、没有 token 账单，因此它**没有气泡、也没有账单行**
/// （`Table.Decisions` 里根本没有它的记录，`bubbles` 与 `usage` 因此在结构上就为空）。
/// 这一行不是给它编的一句理由，而是「站点多给你下了几 MB」这件事的交代。
///
/// **这一桌没选它时一行都不画**：一句「这一桌没有强 AI 基线」对四家模型那一桌是噪声，
/// 而且那正是「一个字节都没拉」那一态（`BaselineStatus.Absent`）。
[<RequireQualifiedAccess>]
module BaselineLine =

    /// 被兜底代打了几手（票 92）。**不许静默替换**（票 23 那条规矩）：
    /// 最近一次的原因逐字列出来 + 这一桌总共几次——只报个数说不清它为什么交不出手。
    let private troubled (troubles: string list) : string =
        match troubles with
        | [] -> ""
        | latest :: _ ->
            let tally =
                match List.length troubles with
                | 1 -> ""
                | count -> $"（这一桌共 {count} 手）"

            $"　有一手它交不出来、由兜底代打：{latest}{tally}。"

    /// 四态各一句话。**拉不动那一句要把两件事都说清**：为什么拉不动、那一席现在是谁在打
    /// ——只说前半的话，人会以为这一桌停了；只说后半的话，人会以为自己拨错了按钮。
    let private said (status: BaselineStatus) (seats: string) : string =
        match status with
        | BaselineStatus.Absent -> ""
        | BaselineStatus.Loading -> $"正在取强 AI 基线那份资产（座位 {seats}）：第一次要下几 MB，之后走浏览器缓存。"
        | BaselineStatus.Ready bytes ->
            $"强 AI 基线已就位（座位 {seats}，{Baseline.bytesToDisplay bytes}）。它不会说话：没有思考气泡，也没有 token 账单。"
        | BaselineStatus.Unavailable reason ->
            $"强 AI 基线用不了：{reason}　座位 {seats} 已退回「{Bot.toDisplay Bot.Opinionated}」的自带 bot，其余席照常打完这一局。"

    /// 这一行（票 92）；这一桌没有强 AI 基线席时一行都不画。
    ///
    /// **座位号列出来**：四席怎么混都行（三模型 + 一强 AI、真人 + 强 AI……），
    /// 只说「有一席」的话，四席那一屏上人得自己去数。
    let at (model: TableModel) =
        let status = TableState.baseline model

        match TableState.live model, status with
        | None, _
        | Some _, BaselineStatus.Absent -> []
        | Some live, _ ->
            let indices = SeatingPlan.baselineSeats live.Seating |> List.map Seat.index
            let seats = indices |> List.map string |> String.concat "、"
            let troubles = TableState.baselineTroubles model

            // 拉不动那一态画成红的（同 `AgentLine` 的 `troubled`）：那一席换了人在打，
            // 而人拨的是强 AI——这件事不该与「一切正常」长得一样。
            let className =
                match status with
                | BaselineStatus.Unavailable _ -> "agent error"
                | BaselineStatus.Absent
                | BaselineStatus.Loading
                | BaselineStatus.Ready _ -> "agent"

            // **只有拉到了才报字节数**：还在拉的时候我们根本不知道它有多大，
            // 写一个 0 会让人以为它是空的（`data-*` 只能是字符串，因此空串就是「还没有」）。
            let bytes =
                match status with
                | BaselineStatus.Ready bytes -> string bytes
                | BaselineStatus.Absent
                | BaselineStatus.Loading
                | BaselineStatus.Unavailable _ -> ""

            [
                Html.p [
                    prop.key "baseline"
                    prop.className className
                    prop.testId "table-baseline"
                    // 四态给闸门读（人读的是上面那句中文）。**懒加载那道闸门量的就是它**：
                    // 不选那一席的趟里它压根不在 DOM 上，而网络请求计数为 0。
                    prop.custom ("data-baseline", Baseline.toWire status)
                    prop.custom ("data-baseline-bytes", bytes)
                    prop.custom ("data-baseline-seats", indices |> List.map string |> String.concat ",")
                    prop.custom ("data-baseline-troubles", string (List.length troubles))
                    prop.text (said status seats + troubled troubles)
                ]
            ]
