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

    /// Agent 层此刻在干什么，以及这一桌兜底代打了几手。
    ///
    /// **断电演习看的就是这一行**：key 配坏了的时候对局照样打得完，但这里会一直红着
    /// 说模型怎么了。`data-agent` 给无头验收读。
    ///
    /// 没有模型坐席时那一句要说清楚**是哪一种自带 bot**（票 42 之后就不只一种了）：
    /// 杆子拨到「有主见」时牌桌上会出立直与供托，而行里还写着「四家都是随机选手」就是句错话。
    /// 措辞只有一份真源（`Bot.toDisplay`，与面板上那个控件同字）。
    let internal agentLine (live: LiveTable) (table: Table) =
        let state, text =
            match live.Agent with
            | AgentStatus.Idle when Option.isNone live.LlmAt -> "idle", $"四家都是{Bot.toDisplay live.Bot}的选手"
            | AgentStatus.Idle -> "idle", "模型座位已就位，还没轮到它"
            | AgentStatus.Asking seat -> "asking", $"正在等座位 {Seat.index seat} 的模型回话……"
            | AgentStatus.Spoke(seat, reason, latency) ->
                let said =
                    match reason with
                    | Some reason -> $"：{reason}"
                    | None -> ""

                "spoke", $"座位 {Seat.index seat} 的模型选完了（{latency} ms）{said}"
            | AgentStatus.Troubled(seat, reason) -> "troubled", $"座位 {Seat.index seat} 兜底代打：{reason}"

        let fallbacks = Table.fallbacks table

        let tally = if fallbacks = 0 then "" else $"　这一桌已兜底 {fallbacks} 手"

        Html.p [
            prop.key "agent"
            prop.className (if state = "troubled" then "agent error" else "agent")
            prop.testId "table-agent"
            prop.custom ("data-agent", state)
            // 坐着的是哪种自带 bot（票 44）：上面那句中文给人看，这一条给闸门看。
            prop.custom ("data-bot", Bot.toWire live.Bot)
            prop.custom ("data-fallbacks", string fallbacks)
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
