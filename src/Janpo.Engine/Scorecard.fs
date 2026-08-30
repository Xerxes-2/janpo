namespace Janpo

/// 一份牌谱里**某一席打得怎么样**（票 133）：终局记分卡上那一行里，
/// 牌谱本身答得出的那几格。
///
/// **它是聚合，不是第二份记录**：每一项都从 `Paifu.Events` 与 `Paifu.Decisions` 现算，
/// 牌谱里一个新字段都不存（`Paifu.Version` 因此一动不动）。
///
/// **顺位与终点不在里面**：那两项 `GameResult` 已经有了（`Paifu` fold 一遍就得），
/// 再算一遍就是两份计数，而两份计数只会漂（裁决 26-6，同 `Table.fallbacks`）。
/// **「选手 · 档」也不在里面**：模型名与 `ScaffoldTier` 是本机配桌的事，
/// 牌谱里没有（`Roster.playerName` 只写得下 `provider/model`），
/// 由渲染层自己去它那份坐法里取。
type SeatTally =
    {
        /// 这一行说的是哪一席。
        Seat: Seat
        /// **问过选手**的手数：这一席有几条决策记录。
        ///
        /// 它是兜底率与重试率的分母——bot 席与随机选手的手不产生记录（`Paifu.Decisions`
        /// 的那条注释），因此它恒 0，而 0 与「一手都没兜底」不是一回事。
        Asked: int
        /// 和了几次：事件流里 `Hora.Actor` 指着这一席的那几条。**自摸与荣和都算**
        /// （`Hora` 这个词本来就含两者，CONTEXT.md）；双响时两家各记各的。
        Hora: int
        /// **被荣和几次**（给人看时那一列叫「放铳」）：`Hora.Target` 指着这一席、
        /// 而和了的是**别人**的那几条。
        ///
        /// **自摸不算**：mjai 的约定是自摸时 `Target` 等于 `Actor`（见 `Hora.Target`），
        /// 不减掉的话每次自摸都会给自己记一笔放铳。
        HoraTargeted: int
        /// 兜底代打了几手：这一席的记录里 `Fallback` 不是 None 的条数
        /// （`DecisionRecord.Fallback` 那条注释：**「是否兜底」就是它是不是 None**）。
        Fallbacks: int
        /// 重试了几次：每条记录 `Attempts - 1` 之和（首问不算重试）。
        Retries: int
        /// 这一席的 token 账单：它那几条记录的 `Usage` 之和。
        ///
        /// **它不是整桌那一份的四分之一**（`Table.usage`）：那一份还含着
        /// **花了钱、没落子**的那几次问话（`VoidedAsk`），而那几次**不在牌谱里**
        /// （裁决 110：那笔账不进牌谱）。因此四行相加**小于等于**账单行上的总额，
        /// 差额就是那几笔——渲染层要把差额说出来，别让两个数并排站着不解释（票 39）。
        Usage: Usage
    }

/// 记分卡的聚合。**纯函数，吃一份牌谱**（票 133）。
[<RequireQualifiedAccess>]
module Scorecard =

    /// 这一席在这条事件流里和了几次、被荣和几次。
    ///
    /// **只走事件流**：谁和了、点的谁，是规则说了算的事（判据 11），
    /// 渲染层与 Agent 层都不该再数一遍。
    let private horaOf (seat: Seat) (events: Event list) : int * int =
        let counted (hora: int, targeted: int) (event: Event) =
            match event with
            | Hora fields when fields.Actor = seat -> hora + 1, targeted
            | Hora fields when fields.Target = seat -> hora, targeted + 1
            | _ -> hora, targeted

        ((0, 0), events) ||> List.fold counted

    /// 这一份规则集、这条事件流、这几条决策记录 → **按座位升序**的逐席记分。
    ///
    /// 三个参数就是 `Paifu` 里那三样，分开收是为了牌桌那一侧
    /// （`Janpo.Web` 的 `Table.scorecard`）不必先拼一份连名字都是假的牌谱出来。
    let tally (ruleset: Ruleset) (events: Event list) (records: DecisionRecord list) : SeatTally list =
        let counted (seat: Seat) : SeatTally =
            let hora, targeted = horaOf seat events
            let mine = records |> List.filter (fun record -> record.Seat = seat)

            {
                Seat = seat
                Asked = List.length mine
                Hora = hora
                HoraTargeted = targeted
                Fallbacks =
                    mine
                    |> List.sumBy (fun record -> if Option.isSome record.Fallback then 1 else 0)
                Retries = mine |> List.sumBy (fun record -> record.Attempts - 1)
                Usage =
                    mine
                    |> List.choose (fun record -> record.Usage)
                    |> List.fold Usage.add Usage.zero
            }

        Seat.all ruleset |> List.map counted

    /// 一份牌谱 → 逐席记分。**票 133 钉住的就是这一个**。
    let ofPaifu (paifu: Paifu) : SeatTally list =
        tally paifu.Ruleset paifu.Events paifu.Decisions

    /// 四行相加的 token 账单。渲染层拿它与整桌那一份（`Table.usage`）比，
    /// 差额就是**花了钱、没落子**的那几次问话。
    let totalUsage (tallies: SeatTally list) : Usage =
        tallies |> List.map (fun tally -> tally.Usage) |> List.fold Usage.add Usage.zero
