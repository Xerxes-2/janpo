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
/// 后一行（token 账单）两种来源共用一份实现，**但那个数在两边说的不是同一件事**（票 110）：
/// Live 那一桌是**花掉的总额**（决策记录 + 作废的问话，票 108 / 109），
/// 一份牌谱 fold 出来的那一桌只数得出牌谱里那几条决策记录——**作废的问话不进牌谱**。
/// 两边各说各的那两句话在 `TableState.usageSaid`。
///
/// **视角那道闸门同样拦头一行**（票 81）：坐到座位 N 上时它只列那一席——
/// 从前那句「座位 N 的模型选完了：……」把别席的理由连同气泡一起漏了出去。
/// 判据不在这一层：`reveals` 从外面传进来（`TableState.reveals`），与气泡用的是同一个函数。
[<RequireQualifiedAccess>]
module AgentLine =

    // ---- 视图：Agent 层的状态 ----

    /// 刚落定的那一手是不是兜底代打的（`data-*` 只能是字符串）。
    let internal fallenBack (latest: Turn option) : string =
        match latest |> Option.bind (fun turn -> turn.Fallback) with
        | Some _ -> "true"
        | None -> "false"

    /// 一席的近况：它说完了 / 它被兜底代打了；没什么可说的是 `None`。
    ///
    /// **一席一句，而不是先拼好再滤**（票 81）：视角那道闸门按座位问，
    /// 因此句子得先与座位配成对——拼成一串之后就再也分不出哪一句是哪一席的了。
    let private saidBy (seat: Seat) (status: AgentStatus) : string option =
        match status with
        | AgentStatus.Idle
        | AgentStatus.Asking -> None
        | AgentStatus.Spoke(reason, latency) ->
            let said =
                match reason with
                | Some reason -> $"：{reason}"
                | None -> ""

            Some $"座位 {Seat.index seat} 的模型选完了（{latency} ms）{said}"
        | AgentStatus.Troubled reason -> Some $"座位 {Seat.index seat} 兜底代打：{reason}"

    /// 这一席此刻在兜底代打吗（`data-agent` 那三态的优先级读它）。
    let private isTroubled (status: AgentStatus) : bool =
        match status with
        | AgentStatus.Troubled _ -> true
        | AgentStatus.Idle
        | AgentStatus.Asking
        | AgentStatus.Spoke _ -> false

    /// 四席的状态，**按座位配好对**（`live.Agent` 每席一项，下标就是座位号，票 74）。
    /// `Seat.ofIndex` 只在负数上失手，因此这里一条都不会丢。
    let private statuses (live: LiveTable) : (Seat * AgentStatus) list =
        live.Agent
        |> List.indexed
        |> List.choose (fun (index, status) -> Seat.ofIndex index |> Option.map (fun seat -> seat, status))

    /// Agent 层此刻在干什么，以及这一桌兜底代打了几手。票 74 之后**按座位各一份**：
    /// 状态线把看得见的那几席都列出来——三家同时在想时，人要看得出是三家、各等了多久。
    ///
    /// **断电演习看的就是这一行**：key 配坏了的时候对局照样打得完，但这里会一直红着
    /// 说模型怎么了。`data-agent` 给无头验收读：troubled 压过 asking 压过 spoke——
    /// 「有一席在兜底」比「有一席在想」要紧，与从前单席时的读法兼容。
    ///
    /// **视角同样拦这一行**（票 81）：`reveals` 从外面传进来（`TableState.reveals`），
    /// 这一层**不重新写一份判据**——气泡与状态线漏的本来就是同一件事，得由同一条规则治。
    /// 它收的是**谓词而不是 `TableModel`**：头一行只属于 Live（票 71），那一条靠的就是这里收 `LiveTable`。
    ///
    /// 被掩蔽那几席**要在行里说一句**（而不是默默少几句）：否则「别席没说话」与
    /// 「别席说了但你这个视角看不见」在页面上长得一模一样，而 escape hatch（上帝视角那一按）
    /// 就在旁边。**只在真有话被挡下时才提**：四家都是 bot 的那一桌没有任何话被掩蔽。
    ///
    /// 没有模型坐席时那一句要说清楚**是哪一种自带 bot**（票 42 之后就不只一种了）：
    /// 杆子拨到「有主见」时牌桌上会出立直与供托，而行里还写着「四家都是随机选手」就是句错话。
    /// 措辞只有一份真源（`SeatingPlan.botsToDisplay`，它又只读 `Bot.toDisplay`，与面板上那些控件同字）。
    let internal agentLine (reveals: Seat -> bool) (names: string list) (live: LiveTable) (table: Table) =
        // 在飞的那几席：一句合说（同一轮里它们是一起被问出去的，秒数取最久的那一席）。
        // **先按视角分两堆**：看得见的进那句话，看不见的只计数（下面那句提示读它）。
        let flying, flyingHushed =
            live.Awaiting |> List.partition (Awaiting.seat >> reveals)

        let asking =
            match flying with
            | [] -> None
            | flying ->
                let seats =
                    flying |> List.map (Awaiting.seat >> Seat.index >> string) |> String.concat "、"

                let waited = flying |> List.map (fun each -> each.WaitedSeconds) |> List.max
                let limit = flying |> List.map Awaiting.limitSeconds |> List.max
                Some $"正在等座位 {seats} 的模型回话（已等 {waited} 秒 / 上限 {limit} 秒）……"

        // 说过话 / 兜底那几席：一席一句（票 74：状态按座位各一份，这一行把它们都列出来），
        // 同样按视角分两堆。
        let bySeat = statuses live

        let spoken, spokenHushed =
            bySeat
            |> List.choose (fun (seat, status) -> saidBy seat status |> Option.map (fun said -> seat, said))
            |> List.partition (fst >> reveals)

        let latest = spoken |> List.map snd

        // 被挡下的那几席（真有话要说的才算：在飞的，或者刚说过话 / 刚兜底的）。
        let hushed = List.length flyingHushed + List.length spokenHushed

        // **只看得见的那几席算**：`data-agent` 说的是这一行上写着什么，而不是牌桌上发生了什么
        // （后者在 `data-fallbacks` 与牌桌那句「上一手：……」上，两样都不按视角变）。
        let troubled =
            bySeat |> List.exists (fun (seat, status) -> reveals seat && isTroubled status)

        let state, text =
            match Option.toList asking @ latest with
            | [] when List.isEmpty (SeatingPlan.llmSeats live.Seating) -> "idle", SeatingPlan.botsToDisplay live.Seating
            | [] when hushed > 0 -> "idle", "这个视角下没有要说的"
            | [] -> "idle", "模型座位已就位，还没轮到它"
            | said ->
                let state =
                    if troubled then "troubled"
                    elif Option.isSome asking then "asking"
                    else "spoke"

                state, String.concat "；" said

        let fallbacks = Table.fallbacks table

        // 被视角挡下那几席的那一句（票 81）：路就在旁边那一排按钮上。
        let masked =
            if hushed = 0 then
                ""
            else
                $"　另 {hushed} 席的状态被这个视角挡着（切上帝视角看全场）"

        let tally = if fallbacks = 0 then "" else $"　这一桌已兜底 {fallbacks} 手"

        Html.p [
            prop.key "agent"
            prop.className (if state = "troubled" then "agent error" else "agent")
            prop.testId "table-agent"
            prop.custom ("data-agent", state)
            // 四席坐着谁（票 44 的 `data-bot`，票 73 扩成四席）：上面那句中文给人看，
            // 这一条给闸门看——逗号隔开的四个名字，与牌谱里那一列 `names` 同一份真源。
            prop.custom ("data-seats", names |> String.concat ",")
            prop.custom ("data-fallbacks", string fallbacks)
            // 被视角挡下的席数（票 81）：人读的是上面那句中文，闸门读这一条。
            prop.custom ("data-hushed", string hushed)
            // 在飞的那几席的座位号（逗号串，没有就是空串）。**诊断用**：闸门数并发靠的是
            // 「thinking 态的气泡有几个」（verify-bubbles 的 MutationObserver），这一条只是
            // 让人肉查页面时不必逐席翻气泡。
            // **它同样只列看得见那几席**：一条绕过闸门的 `data-*` 就是一扇后门。
            prop.custom (
                "data-asking-seats",
                flying |> List.map (Awaiting.seat >> Seat.index >> string) |> String.concat ","
            )
            prop.text (text + masked + tally)
        ]

    /// 这一桌的 token 账单（票 29b）。**「缓存真的命中了」在页面上看得见**：
    /// prompt 翻成「固定 preamble + append-only 历史 + 尾部现况」之后，
    /// 同一局里越往后打，命中的那一段越长。
    ///
    /// 一个 token 都还没花掉时不占位（四家随机选手的那一桌永远不长出这一行）。
    /// `data-*` 那几项给无头验收读。
    /// **这一行说的话分两种**（票 110），它收 `TableModel` 就是为了分得出来：
    /// Live 那一桌报的是**花掉的总额**（含那几笔「花了钱、没落子」的作废问话）；
    /// 一份牌谱 fold 出来的那一桌报的是**牌谱里那几手的合计**——作废的问话不进牌谱
    /// （票 110 的判断），**差的那一块因此得在这一行上自己说出来**。
    /// 那两句话的措辞只有一处（`TableState.usageSaid`），这里只负责画。
    let internal usageLine (model: TableModel) (table: Table) =
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
                    // 账单里那几笔**花了钱、没落子**的，以及其中换人撤下来的那几笔（票 110）。
                    // **两个数各取自一处具名的取值器**（票 107 的逐数溯源）：
                    // 人读的是后面那句中文，闸门读这两条。
                    prop.custom ("data-void-asks", string (table |> Table.paidVoids |> List.length))
                    prop.custom ("data-void-rebound", string (table |> Table.paidRevoked |> List.length))
                    prop.text (TableState.usageSaid model table)
                ]
            ]
