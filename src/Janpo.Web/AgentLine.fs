namespace Janpo.Web

open Feliz
open Janpo

/// Agent 层那两行状态线（票 70 从 `TablePage.fs` 拆出来的第四块）：
/// 此刻在等谁回话、这一桌兜底代打了几手、累计的 token 账单。
///
/// **它与 `AgentStatus` 那个类型是两头**：类型在 `TableState`（`LiveTable` 的一格），
/// 这里只是把它渲染成牌桌顶上那两行。牌桌本体在 `TableBoard`，那边把这两行接在最前面。
///
/// **头一行只属于 Live**（票 71）：回放里没有在飞的问话，写一句「四家都是……选手」
/// 就是句错话——那一局的选手是**当时**坐那一桌的人，写在牌谱里。
/// 因此它收的是 `LiveTable` 而不是 `TableModel`：类型就拦住了这件事。
/// 后一行（token 账单）两种来源共用：它数的是牌谱里的决策记录，回放一份 LLM 牌谱时同样算得出来。
[<RequireQualifiedAccess>]
module AgentLine =

    // ---- 视图：Agent 层的状态 ----

    /// 刚落定的那一手是不是兜底代打的（`data-*` 只能是字符串）。
    let internal fallenBack (latest: Turn option) : string =
        match latest |> Option.bind (fun turn -> turn.Fallback) with
        | Some _ -> "true"
        | None -> "false"

    /// Agent 层此刻在干什么，以及这一桌兜底代打了几手。票 74 之后**按座位各一份**：
    /// 状态线把多席都列出来——三家同时在想时，人要看得出是三家、各等了多久。
    ///
    /// **断电演习看的就是这一行**：key 配坏了的时候对局照样打得完，但这里会一直红着
    /// 说模型怎么了。`data-agent` 给无头验收读：troubled 压过 asking 压过 spoke——
    /// 「有一席在兜底」比「有一席在想」要紧，与从前单席时的读法兼容。
    ///
    /// 没有模型坐席时那一句要说清楚**是哪一种自带 bot**（票 42 之后就不只一种了）：
    /// 杆子拨到「有主见」时牌桌上会出立直与供托，而行里还写着「四家都是随机选手」就是句错话。
    /// 措辞只有一份真源（`SeatingPlan.botsToDisplay`，它又只读 `Bot.toDisplay`，与面板上那些控件同字）。
    let internal agentLine (live: LiveTable) (table: Table) =
        // 在飞的那几席：一句合说（同一轮里它们是一起被问出去的，秒数取最久的那一席）。
        let asking =
            match live.Awaiting with
            | [] -> None
            | flying ->
                let seats =
                    flying |> List.map (Awaiting.seat >> Seat.index >> string) |> String.concat "、"

                let waited = flying |> List.map (fun each -> each.WaitedSeconds) |> List.max
                let limit = flying |> List.map Awaiting.limitSeconds |> List.max
                Some $"正在等座位 {seats} 的模型回话（已等 {waited} 秒 / 上限 {limit} 秒）……"

        // 说过话 / 兜底那几席：一席一句（票 74：状态按座位各一份，这一行把它们都列出来）。
        let latest =
            live.Agent
            |> List.mapi (fun index status ->
                match status with
                | AgentStatus.Idle
                | AgentStatus.Asking -> None
                | AgentStatus.Spoke(reason, latency) ->
                    let said =
                        match reason with
                        | Some reason -> $"：{reason}"
                        | None -> ""

                    Some $"座位 {index} 的模型选完了（{latency} ms）{said}"
                | AgentStatus.Troubled reason -> Some $"座位 {index} 兜底代打：{reason}")
            |> List.choose id

        let troubled =
            live.Agent
            |> List.exists (fun status ->
                match status with
                | AgentStatus.Troubled _ -> true
                | AgentStatus.Idle
                | AgentStatus.Asking
                | AgentStatus.Spoke _ -> false)

        let state, text =
            match Option.toList asking @ latest with
            | [] when List.isEmpty (SeatingPlan.llmSeats live.Seating) -> "idle", SeatingPlan.botsToDisplay live.Seating
            | [] -> "idle", "模型座位已就位，还没轮到它"
            | said ->
                let state =
                    if troubled then "troubled"
                    elif Option.isSome asking then "asking"
                    else "spoke"

                state, String.concat "；" said

        let fallbacks = Table.fallbacks table

        let tally = if fallbacks = 0 then "" else $"　这一桌已兜底 {fallbacks} 手"

        Html.p [
            prop.key "agent"
            prop.className (if state = "troubled" then "agent error" else "agent")
            prop.testId "table-agent"
            prop.custom ("data-agent", state)
            // 四席坐着谁（票 44 的 `data-bot`，票 73 扩成四席）：上面那句中文给人看，
            // 这一条给闸门看——逗号隔开的四个名字，与牌谱里那一列 `names` 同一份真源。
            prop.custom ("data-seats", SeatingPlan.names live.Seating |> String.concat ",")
            prop.custom ("data-fallbacks", string fallbacks)
            // 在飞的那几席的座位号（逗号串，没有就是空串）。**诊断用**：闸门数并发靠的是
            // 「thinking 态的气泡有几个」（verify-bubbles 的 MutationObserver），这一条只是
            // 让人肉查页面时不必逐席翻气泡。
            prop.custom (
                "data-asking-seats",
                live.Awaiting
                |> List.map (Awaiting.seat >> Seat.index >> string)
                |> String.concat ","
            )
            prop.text (text + tally)
        ]

    /// 这一桌的 token 账单（票 29b）。**「缓存真的命中了」在页面上看得见**：
    /// prompt 翻成「固定 preamble + append-only 历史 + 尾部现况」之后，
    /// 同一局里越往后打，命中的那一段越长。
    ///
    /// 一个 token 都还没花掉时不占位（四家随机选手的那一桌永远不长出这一行）。
    /// `data-*` 那几项给无头验收读。
    let internal usageLine (table: Table) =
        let usage = Table.usage table

        if Usage.promptTokens usage = 0 then
            []
        else
            [
                Html.p [
                    prop.key "usage"
                    prop.className "agent"
                    prop.testId "table-usage"
                    prop.custom ("data-prompt-tokens", string (Usage.promptTokens usage))
                    prop.custom ("data-cache-read", string usage.CacheRead)
                    prop.custom ("data-cache-write", string usage.CacheWrite)
                    prop.custom ("data-cache-percent", string (Usage.cacheHitPercent usage))
                    prop.text ("这一桌累计：" + Usage.toDisplay usage)
                ]
            ]
