namespace Janpo.Web

open Janpo

/// 「换打这一张会怎样」的一条候选（票 90）。
///
/// **每一格都是引擎已经算好的那个数**（`Scaffold.calculate` 里逐张试打的那一条
/// `DahaiScaffold`），这一层只做减法与排序：**没有总分**——「这一手打得几分」要一个模型，
/// 那是票 93 的事，这一票一个自造的数都不出。
type ReviewCandidate = {
    /// 换打的那一张，去红后的牌种（`DahaiScaffold.Pai`）。
    Pai: Tile
    /// 打完它之后的向听（`DahaiScaffold.Shanten`）。
    Shanten: Shanten
    /// 进退向（`DahaiScaffold.ShantenDelta`）。
    ShantenDelta: int
    /// 打完它之后的有效牌枚数（`Ukeire.total`）；引擎算不出来时是 None。
    Ukeire: int option
    /// 比你打的那张多几枚；**两边都算得出来才有**（一边缺就不做减法）。
    UkeireGain: int option
    /// 打它的危险度（`DahaiScaffold.Danger`）；这一手没有立直或副露的家时是 None。
    Danger: Danger option
    /// 向听比你打的那张好吗（`ShantenDelta` 更小）。
    Advances: bool
    /// 危险度档位比你打的那张低吗（`DangerTier.order` 更小）。
    Safer: bool
}

/// 复盘里的一条标注（票 90，spec 的 story 34）：**这一席这一手做了什么，以及引擎当时算出来的那几个数**。
///
/// **它不是新的数据，是现算的投影**：`Paifu` / `DecisionRecord` / `LiveTable` 的形状
/// 一个字节都没动——复盘要的东西（那一手落定之前的局面）本来就在逐帧的牌桌里，
/// 把那一帧交给 `DecisionPackage.forSeat` 就得到当时那份脚手架。
///
/// **强 AI 那一行挂在 `Package` 上**（票 93）：那一格存的不是「它打了什么」，而是
/// **那一手当时喂给该席的那一份投影**——问它的时候交出去的就是它，一个字节不多。
/// 它答回来的那个 id 由 `Review.strongOf` 换成一行（`ReviewStrong`），
/// **答不上来就整行不出现**，页面上不写「暂无」。
type ReviewNote = {
    /// 第几手（`Table.Turns` 那个号，CONTEXT.md 的 Turn；跨局累计）。
    Turn: int
    /// 这一手**落定之后**那一帧的帧号。点这一条就是把游标搬到它（票 75 的那根轴）。
    Frame: int
    /// 哪一席。
    Seat: Seat
    /// 这一手给机器看的名字（mjai 动作名，`HumanSeat.kind`）。
    Kind: string
    /// 这一手给人看的中文（`Action.toDisplay`）。
    Label: string
    /// 这一手本身。**`Kind` 与 `Label` 是它的两种渲染**，这一格是拿来比对的那一份：
    /// 「强 AI 那一手与你的是不是同一条」按**这一包里的 id** 比（票 93），
    /// 而 id 要拿这个动作向包里问（`DecisionPackage.tryId`）。
    Played: Action
    /// **那一手当时喂给该席的那一份投影**（`DecisionPackage.forSeat seat before.State`）。
    ///
    /// **这一票的全部难点就在这一格**（票 93）：复盘时整局都在手上，随手一喂就是上帝视角，
    /// 而那样问出来的答案是人做不到的（它知道你不知道的牌）。这里存的是引擎那条唯一的
    /// 掩蔽法则给出的那一份（`Observation.ofState` 内部的「掩蔽 + fold」），
    /// **90 那几个数与 93 那一行因此是同一份包的两次消费**——没有第二条算路可漂。
    ///
    /// 这一席此刻不在合法动作集里时是 None（走不到：这一手就是它自己落定的）。
    Package: DecisionPackage option
    /// **打之前那一刻**引擎算出的脚手架（`Scaffold.calculate` 那一份）；
    /// 手牌形态读不出来时是 None——那时一个数都不给（`Scaffold` 自己的规矩：
    /// 错的向听数比没有向听数更坏）。
    Scaffold: Scaffold option
    /// 你打的那一张的那一条试打；这一手没打牌（鸣、立直宣言、和了、「过」）时是 None。
    Trial: DahaiScaffold option
    /// 比你打的那张更好的几个候选，按有效牌降序；**空表 = 这一手是当时的最优之一**。
    Better: ReviewCandidate list
}

/// 强 AI 那一次前向给出的**一条候选**（票 103）：哪一条、叫什么、多少、排第几。
///
/// **`P` 是上游那个数本人**：这一层不归一化、不重排、不四舍五入（舍入只发生在
/// 给人看的那一句里，而 `data-*` 上搬的是完整的那一串）。
/// **它不是理由**：把 0.95 读成「它很确定」是我们替一个不会说话的网络编话（票 103 的硬边界）。
type ReviewChoice = {
    /// 这一条在那一包里的 id。
    ActionId: int
    /// 那一条的中文 label（`ActionOption.label`，引擎给的那一句）。
    Label: string
    /// 给机器看的那一半（`dahai:3p` / `pon` / …）。
    Key: string
    /// 上游给它的那个数。
    P: float
    /// 在**上游那一列**里排第几（1 起）。**中间那一条认不出来时序号不重排**：
    /// 重排了的话「第 2」就不再是上游那张表里的第 2 了。
    Rank: int
}

/// 强 AI 在那一手会怎么打（票 93，spec 的 story 36），**以及它那一次前向给出的那几条候选**（票 103）。
///
/// **它不给理由**（ADR-0006 / 票 92）：跨界回来的只有 id 与数，没有 thinking、没有一句话理由。
/// **别为它编一句**（票面原话）——这里因此一格「理由」都不留，
/// **概率也不是理由**：它只是上游那几个数，页面上只允许照抄。
///
/// **它不是裁判，是参照系**（票 93 的边界）：`Differs` 只说「不同」，不说「你错了」，
/// 也没有总分、没有「你比它差多少」。
type ReviewStrong = {
    /// 第几手（与那一条标注的 `Turn` 同一个号）。
    Turn: int
    /// 它选的那一条在**那一包**里的 id（跨界回来的就是这一个整数，ADR-0005）。
    ActionId: int
    /// 那一条的中文 label（`ActionOption.label`，引擎给的那一句）。
    Label: string
    /// 给机器看的那一半（`dahai:3p` / `pon` / …）：闸门读它，人读上面那一句。
    Key: string
    /// 与这一席当时打的那一手**不同吗**。**只标不同**。
    Differs: bool
    /// 端到端毫秒（TS 那侧量的：翻译 + 喂这一局的流 + 一次前向）。
    LatencyMs: int
    /// 它那一次前向给出的候选，按上游那一列的顺序（概率降序）。
    ///
    /// **空表是一种合法状态**（老产物 / 字段缺失）：那时这一行退回票 93 今天的样子
    /// ——只说它打什么，**不是空白、不是「暂无」**。
    Candidates: ReviewChoice list
    /// 上游一共给了几条（今天是 top-3）。**不是 `List.length Candidates`**：
    /// 这一包里认不出的那几条不占位，但它给了几条要照实说得出来。
    CandidatesTotal: int
    /// **你打的那一手在它的候选里的那一条**（票面：「你打的它给了 0.35，排第二」）；
    /// 不在那几条里时是 None——**那不等于它给了 0**，上游只抬出前几条。
    Yours: ReviewChoice option
    /// 你那一手的中文 label（`ReviewNote.Label`）：上面那一句要叫得出它的名字。
    PlayedLabel: string
}

/// 时间轴上那一枚标记（票 105）：**值得看的那一手落在第几帧**。
///
/// **它不是第二份判据**，是 `Review.focused` 那一列的投影：时间轴那一段只需要两个整数
/// （哪一手、第几帧），把整条 `ReviewNote` 搬过去等于让一个滑块认识复盘。
/// **一处算、两处消费**：复盘那一列摆哪几条与轴上标哪几枚，出自同一次 `Review.focused`。
type ReviewMark = {
    /// 那一手的手序（`ReviewNote.Turn`）。
    Turn: int
    /// 那一手**落定之后**那一帧的帧号（`ReviewNote.Frame`）：标记就钉在轴上的这一点。
    Frame: int
}

/// 复盘面板此刻该画什么（票 90）。**三态是三个 case**（判据 12：拒绝理由各有各的 case）：
/// 「还没打完」与「打完了但没有主语」在页面上是两句完全不同的话，混成一个空表就分不出了。
[<RequireQualifiedAccess>]
type ReviewShown =
    /// 这一场还没打完：**整块不在 DOM 里**。
    ///
    /// 对局中给出「换打会怎样」就是作弊——它属于 Assisted 档（票 89），
    /// 而真人在座的那一局里，那几个数正是他自己该算的东西。
    | Hidden
    /// 打完了，但这一屏没有主语（上帝视角）：只说一句「切到某一席就看得了」。
    | Unaddressed
    /// 这一席的逐手标注。
    | Notes of seat: Seat * notes: ReviewNote list

/// 一条候选说出来是什么样。
[<RequireQualifiedAccess>]
module ReviewCandidate =

    /// **渲染层的单向出口**：`打8筒（有效牌 39 枚，+18；危险度更低）`。
    ///
    /// **逐项都是引擎的那个数**，这一层不加权、不排名次——它只把「哪一项更好」说出来。
    let toDisplay (candidate: ReviewCandidate) : string =
        let ukeire =
            match candidate.Ukeire, candidate.UkeireGain with
            // **多出来的枚数才写那个号**：候选的有效牌不会比你少（`dominates` 拦着），
            // 但写成 `+{gain}` 一份就包不住这一条——那条判据被改坏时页面会印出 `+-4`。
            // （红-2 那次破坏实验印出来的就是它：数字的形状不该靠另一处的不变量撑着。）
            | Some total, Some gain when gain > 0 -> Some $"有效牌 {total} 枚，+{gain}"
            | Some total, Some gain when gain < 0 -> Some $"有效牌 {total} 枚，{gain}"
            | Some total, _ -> Some $"有效牌 {total} 枚"
            // 有效牌算不出来的那一张仍可能因为向听或危险度更好而排进来（下面两句会说清）。
            | None, _ -> None

        let advances =
            if candidate.Advances then
                Some $"向听更好（{Shanten.toDisplay candidate.Shanten}）"
            else
                None

        let safer =
            if candidate.Safer then
                candidate.Danger
                |> Option.map (fun danger -> $"危险度更低（{DangerTier.toDisplay danger.Tier}）")
            else
                None

        let said = [ ukeire; advances; safer ] |> List.choose id |> String.concat "；"

        $"打{Tile.toDisplay candidate.Pai}（{said}）"

/// 强 AI 那一行说出来是什么样。
[<RequireQualifiedAccess>]
module ReviewStrong =

    /// 一个概率**给人看的那一半**：两位小数。
    ///
    /// **两头各拦一道，因为舍入会把事实舍成另一件事**：真语料里末一条的 p05 是 0.0004
    /// （报告 103 §1），写成 `0.00` 就成了「它给了零」；同理 0.999 写成 `1.00`
    /// 就成了「它给了全部」。两句都是事实上的界，**不是形容词**（票 103 的硬边界：
    /// 页面上一个度量词都不许出现）。**完整的那一串在 `data-*` 上**（`probabilityToWire`）。
    let probabilityToDisplay (p: float) : string =
        if p > 0.0 && p < 0.005 then "<0.01"
        elif p < 1.0 && p >= 0.995 then ">0.99"
        else $"%.2f{p}"

    /// 一个概率**给机器看的那一半**（`data-review-strong-ps`）：**一位都不舍**。
    ///
    /// 闸门拿它与 wasm 直接印出来的那一串逐位对拍（票 103 的那道闸），
    /// 因此这里用的是**最短往返表示**（.NET 与 JS 的 `ToString()` 都是它），
    /// 而不是 `%g` 那种按有效位数截的写法——截一位，那道闸就变成了「四舍五入看着差不多」。
    let probabilityToWire (p: float) : string = string p

    /// **渲染层的单向出口**（ADR-0001）：
    /// `〔强 AI〕手切3索（与你不同）　它给了 3 条：手切3索 0.62、摸切9万 0.21、立直 0.11　你打的摸切1万 0.21（第 2）`。
    ///
    /// **相同那一半也说出来**：只标「不同」的话，没有标记的那几手看着像「还没算完」
    /// ——而「算不动」在这一票里的样子是**整行不出现**，两者在页面上必须分得开。
    ///
    /// **分布那两句只能照抄数字**（票 103 的硬边界）：不写「很确定 / 犹豫」这类度量词，
    /// 不写「它认为……」。要不要按阈值分档是产品口味，留给人裁（提案在报告 103 里）。
    ///
    /// **分布为空时逐字退回票 93 那一句**：老产物上那一行仍然说得出它打什么，
    /// 而不是多出一句「暂无候选」（票 90/92/93 同一个规矩：没有的东西不占位）。
    let toDisplay (strong: ReviewStrong) : string =
        let compared = if strong.Differs then "与你不同" else "与你相同"
        let head = $"〔强 AI〕{strong.Label}（{compared}）"

        match strong.Candidates with
        | [] -> head
        | candidates ->
            let listed =
                candidates
                |> List.map (fun choice -> $"{choice.Label} {probabilityToDisplay choice.P}")
                |> String.concat "、"

            // 认得出来的比上游给的少时，**把这件事说出来**：票 92 把分布整个扔了而没人知道，
            // 靠的就是没有一句话把「上游给了几条」抬到台面上。（实测未触发，报告 103 §2。）
            let counted =
                if List.length candidates = strong.CandidatesTotal then
                    $"它给了 {strong.CandidatesTotal} 条"
                else
                    $"它给了 {strong.CandidatesTotal} 条（这一包里认得出 {List.length candidates} 条）"

            let yours =
                match strong.Yours with
                | Some choice -> $"你打的{strong.PlayedLabel} {probabilityToDisplay choice.P}（第 {choice.Rank}）"
                // **不写成「它给了 0」**：上游只抬前几条，没抬到的那一手它的概率我们根本没拿到。
                | None -> $"你打的{strong.PlayedLabel}：不在这 {strong.CandidatesTotal} 条里"

            $"{head}　{counted}：{listed}　{yours}"

/// 复盘那一列此刻摆几条（票 105）：**筛掉了多少要说出来**（票面原话：不许静静地少显示）。
///
/// **两句话里的数是同两个**（`total` / `kept`，顺序也一样）：筛选开着与关掉时页面上的话不同，
/// 而「这一句里的每一个数指得回哪一格」这件事两态共用一张来源表（票 107 的逐数溯源）。
[<RequireQualifiedAccess>]
module ReviewFilter =

    /// 筛选那一格给机器看的那一半（`data-review-filter`）；人读的是下面那句话。
    let toWire (filtered: bool) : string = if filtered then "on" else "off"

    /// 那一枚按钮上写什么。**不带数**：它旁边那句话已经把两个数说清了，
    /// 按钮上再摆一个就是同一件事的第二处（票 86 记过的「同一帧两个数」同一族）。
    let toggle (filtered: bool) : string = if filtered then "看全部" else "只看值得看的那几手"

    /// 筛选那一句。**判据照实写出来**：读者要看得出被藏起来的那些手是按什么藏的，
    /// 否则「显示 22 手」只是一个没有来历的数。
    ///
    /// **没问过强 AI 时不提它那一条**（`consulted`）：那几 MB 没拉之前，第二条判据
    /// 一手都筛不到，写出来就是在声称一件此刻没有执行体的事（判据 2；
    /// 同票 90/93 那条「没有的东西不占位」）。
    ///
    /// **阈值那个数不印在页面上**：它是这一层自己的一个旋钮（`Review` 里量出来的那个常数），
    /// 不是引擎或上游那一份里的哪一格——印出来就得给它编一个来源（票 107 的逐数溯源）。
    /// 「头两条」写成汉字也是为这件事：那一句里**只该有 `total` 与 `kept` 两个数**，
    /// 四种措辞都不例外（因此两处来源表各只摆两格就够了）。
    let toDisplay (filtered: bool) (consulted: bool) (total: int) (kept: int) : string =
        // 两条判据里的第二条只在真问过之后才算数。
        let judged =
            if consulted then
                "两条判据：引擎的试打表里还有更好的换法，或者强 AI 给自己那一手的概率过了阈值、而你打的不在它头两条里。"
            else
                "判据是引擎的试打表里还有更好的换法；问过强 AI 之后还有第二条。"

        if not filtered then
            $"这一列摆着全部 {total} 手，其中值得看的有 {kept} 手（时间轴上标着的就是它们）。"
        elif kept = 0 then
            $"只看值得看的那几手：{total} 手里显示 {kept} 手——这一席这一场没有一手落进判据。按「看全部」摆出其余那些。"
        else
            $"只看值得看的那几手：{total} 手里显示 {kept} 手。{judged}"

/// 一条标注说出来是什么样。**面板只画这几句，一条规则都不判**（同 `HumanLine` / `AgentLine`）。
[<RequireQualifiedAccess>]
module ReviewNote =

    /// 第一句：第几手、做了什么。
    let headline (note: ReviewNote) : string = $"第 {note.Turn} 手　{note.Label}"

    /// 第二句：**引擎当时算出来的那几个数**。
    ///
    /// **三种情形三句话**：打了牌（向听怎么变、有效牌多少、危险度多少）、
    /// 这一手没打牌（只有向听，没有「换打会怎样」可言）、形态读不出来（一个数都不给）。
    let figures (note: ReviewNote) : string =
        match note.Scaffold, note.Trial with
        | None, _ -> "（这一手的手牌形态引擎读不出来：一个数都不给）"
        | Some scaffold, None -> $"{Shanten.toDisplay scaffold.Shanten}（这一手没打牌，没有「换打会怎样」可比）"
        | Some scaffold, Some trial ->
            let shanten =
                let before = Shanten.toDisplay scaffold.Shanten
                let after = Shanten.toDisplay trial.Shanten

                if trial.ShantenDelta > 0 then
                    $"{before} → {after}（退向）"
                else
                    $"{before} → {after}"

            let ukeire =
                trial.Ukeire
                |> Option.map (fun ukeire -> $"有效牌 {Ukeire.total ukeire} 枚 {Ukeire.kindCount ukeire} 种")

            // 没有立直、也没有一家副露时**整段不出现**：那时危险度没有被评价的对象
            // （`Danger.rank` 返回空表），硬写一句「危险度未知」是在说一件没发生的事。
            let danger =
                trial.Danger
                |> Option.map (fun danger -> $"危险度 {DangerTier.toDisplay danger.Tier}（这一手第 {danger.Rank} 安全）")

            [ Some shanten; ukeire; danger ] |> List.choose id |> String.concat "　"

    /// 第三句：更好的候选；**这一手没打牌时根本没有这一句**（返回 None）。
    ///
    /// **没有更好的就明说**（票面原话）：空着的一栏与「这一手已经是最优之一」在页面上
    /// 长得一模一样，而后者是复盘最想听见的那句话。
    let advice (note: ReviewNote) : string option =
        match note.Trial, note.Better with
        | None, _ -> None
        | Some _, [] -> Some "这一手是当时的最优之一。"
        | Some _, better ->
            better
            |> List.map ReviewCandidate.toDisplay
            |> String.concat "、"
            |> sprintf "更好的候选：%s"
            |> Some

/// 复盘：**逐手对照标注**（票 90，spec 的 story 34）。
///
/// **引擎自算那一层零外部依赖**（M3 起草时主人已裁，ADR-0006 的后果 3）：
/// 向听 / 有效牌 / 危险度那几个数由 `Scaffold.calculate` 对着**那一手落定之前那一帧**
/// 现算一遍——终局之后立刻有东西看，不等任何模型。
/// **强 AI 那一行叠在它上面而不是替掉它**（票 93）：那几 MB 拉不动时复盘照常出，
/// 只是没有那一行。
///
/// **它读的是牌谱**（ADR-0002：牌谱是唯一的可分享物）：逐帧的牌桌由 `Table.replay` fold 出来，
/// 而每一帧的局面就是那一刻引擎的权威状态。因此**复盘不需要任何新字段**：
/// `Paifu` / `DecisionRecord` / `LiveTable` / `SeatChoice` 的形状一个字节都没动。
///
/// **模型席与真人席同一条路**（票面：复盘不是真人专属）：这里只按座位取，
/// 从不问那一席是谁在打——模型席顺带把 `DecisionRecord.Reason` 与这几条标注排在同一根轴上，
/// 那就是「它为什么那样打」与「我为什么这样打」的并排。
[<RequireQualifiedAccess>]
module Review =

    // ---- 一条标注怎么算出来 ----

    /// 这一张打完之后的有效牌枚数；引擎算不出来时是 None（**不当 0**）。
    let private ukeireOf (trial: DahaiScaffold) : int option = trial.Ukeire |> Option.map Ukeire.total

    /// 这一张的安全度序（0 最安全）；这一手没有立直也没有副露时是 None
    /// （那时危险度没有被评价的对象，`Danger.rank` 回的是空表）。
    let private tierOf (trial: DahaiScaffold) : int option =
        trial.Danger |> Option.map (fun danger -> DangerTier.order danger.Tier)

    /// 这两条试打，哪一条在引擎算得出的那三个量上**不比另一条差**。
    ///
    /// **判据是帕累托占优，不是加权总分**（票面明令）：向听不更差、有效牌不更少、
    /// 危险度不更高，且至少一项严格更好。三项里**算不出来的那一项不参与比较**
    /// （有效牌在可见张数越界时是 None）——拿 None 当 0 就是在编一个数。
    let private dominates (played: DahaiScaffold) (candidate: DahaiScaffold) : bool =
        // 「不更差」与「更好」各比一遍：一项都不许倒退，而且至少一项要真的前进。
        let notWorse =
            candidate.ShantenDelta <= played.ShantenDelta
            && (match ukeireOf candidate, ukeireOf played with
                | Some theirs, Some yours -> theirs >= yours
                | _, _ -> true)
            && (match tierOf candidate, tierOf played with
                | Some theirs, Some yours -> theirs <= yours
                | _, _ -> true)

        let better =
            candidate.ShantenDelta < played.ShantenDelta
            || (match ukeireOf candidate, ukeireOf played with
                | Some theirs, Some yours -> theirs > yours
                | _, _ -> false)
            || (match tierOf candidate, tierOf played with
                | Some theirs, Some yours -> theirs < yours
                | _, _ -> false)

        notWorse && better

    /// 最多列几条。**三条**：复盘要的是「原来还有这几张」，不是把 13 张重排一遍
    /// ——那张表引擎本来就能给（Assisted 档的 prompt 里就是它），复盘要的是**挑出来的那几张**。
    let private betterLimit = 3

    /// 比你打的那张更好的那几个候选，按**有效牌降序 → 危险度档位升序 → 进退向升序**排。
    ///
    /// 排序键三项都是引擎的数，**没有权重**：它决定的只是「先说哪一条」，
    /// 而「算不算更好」由 `dominates` 说了算。
    let private betterThan (played: DahaiScaffold) (scaffold: Scaffold) : ReviewCandidate list =
        let yours = ukeireOf played

        scaffold.Dahai
        |> List.filter (dominates played)
        // 有效牌取负数是为了降序（`List.sortBy` 只升序）；算不出来的那两项排到最后
        // （排序键里当 0 / 9 只影响「先说哪一条」，不影响「算不算更好」——后者在 `dominates` 里）。
        |> List.sortBy (fun trial ->
            -(ukeireOf trial |> Option.defaultValue 0), tierOf trial |> Option.defaultValue 9, trial.ShantenDelta)
        |> List.truncate betterLimit
        |> List.map (fun trial -> {
            Pai = trial.Pai
            Shanten = trial.Shanten
            ShantenDelta = trial.ShantenDelta
            Ukeire = ukeireOf trial
            UkeireGain =
                match ukeireOf trial, yours with
                | Some theirs, Some mine -> Some(theirs - mine)
                | _, _ -> None
            Danger = trial.Danger
            Advances = trial.ShantenDelta < played.ShantenDelta
            Safer =
                match tierOf trial, tierOf played with
                | Some theirs, Some mine -> theirs < mine
                | _, _ -> false
        })

    /// 这一手打出去的那张（去红后的牌种）；不是打牌那一手时是 None。
    ///
    /// **去红**：形态判定按牌种走（`HandShape` 一律去红），因此 `Scaffold` 里的试打
    /// 也是按牌种编的——拿 `5mr` 去配 `5m` 那一条会配不上（`DahaiScaffold.Pai` 那段注释）。
    let private discarded (action: Action) : Tile option =
        match action with
        | Action.Dahai(_, pai, _) -> Some(Tile.deaka pai)
        | _ -> None

    /// 一条动作给机器看的那一半（`dahai:3p` / `pon` / …）。
    ///
    /// **打牌那一类带上牌**（包里同一张牌只有一条打法，因此不带摸切与否），
    /// 其余只写 mjai 动作名。**它只给人与闸门读**：同不同的判据是 id（见 `strongOf`），
    /// 不是这一串字——一串字一旦拿来当判据，吃那几种吃法就会被当成同一手。
    let private keyOf (action: Action) : string =
        match action with
        | Action.Dahai(_, pai, _) -> $"dahai:{Tile.toMjai pai}"
        | _ -> HumanSeat.kind action

    /// 一帧一条：`before` 是那一手**落定之前**的局面，`after` 是落定之后的那一帧。
    ///
    /// **脚手架现问 `DecisionPackage.forSeat`**（判据 11：要读规则才做得出的决定归引擎）：
    /// 那一份包与当时真的问出去的那一份走的是同一个构造子，因此复盘看到的数
    /// 与当时模型/真人手上那份**必然相同**——这里没有第二条算路。
    let private noteOf (seat: Seat) (frame: int) (before: Table) (after: Table) : ReviewNote option =
        after.Latest
        |> Option.map (fun turn -> turn.Action)
        |> Option.filter (fun action -> Action.actor action = seat)
        |> Option.map (fun action ->
            // **一处构造、两处消费**：90 那几个数（脚手架）与 93 那一行（拿它去问强 AI）
            // 读的是**同一份包**。各造一份也跑得动，但那就有了两条可以各自漂的路，
            // 而「喂给它的必须与那一手当时喂给该席的是同一份投影」正是票 93 的全部难点。
            let package = DecisionPackage.forSeat seat before.State
            let scaffold = package |> Option.bind DecisionPackage.scaffold

            let trial =
                match scaffold, discarded action with
                | Some scaffold, Some pai -> scaffold.Dahai |> List.tryFind (fun trial -> trial.Pai = pai)
                | _, _ -> None

            {
                // 手序是**落定之前**那一帧的手数：第 0 手就是这一场的第一手（票 76 的 `frameOfTurn`
                // 读的也是这个号，点开那一条才跳得到同一帧）。
                Turn = before.Turns
                Frame = frame
                Seat = seat
                Kind = HumanSeat.kind action
                Label = Action.toDisplay action
                Played = action
                Package = package
                Scaffold = scaffold
                Trial = trial
                Better =
                    match scaffold, trial with
                    | Some scaffold, Some trial -> betterThan trial scaffold
                    | _, _ -> []
            })

    /// 某一席在这份逐帧牌桌里的**每一手**（票面第一条验收）。
    ///
    /// **纯的**：用例与闸门读的就是它，页面那一侧只是多绕一层取帧。
    /// 「每一手」按牌谱的口径算——`Action.None`（他自己按的那一次「过」）同样占一手，
    /// 因为它在牌谱里就是一手（`Table.Turns` 数得到它），复盘漏掉它就与牌谱对不上。
    let notesFor (seat: Seat) (frames: Table list) : ReviewNote list =
        frames
        |> List.pairwise
        |> List.indexed
        |> List.choose (fun (index, (before, after)) -> noteOf seat (index + 1) before after)

    // ---- 强 AI 那一行（票 93） ----

    /// 要问强 AI 的那几手，以及**每一手该交出去的那一份**。
    ///
    /// **交出去的就是 `note.Package`**（`DecisionPackage.forSeat seat before.State`）：
    /// 与那一手当时喂给该席的是**同一个构造子的同一份值**，这一层一个字段都不拼。
    /// 复盘时手上握着整局（每一帧的 `GameState` 都在），**随手一喂就是上帝视角**
    /// ——而那样的对照毫无意义：它会拿你那一手根本不知道的牌去选，人做不到。
    let requests (notes: ReviewNote list) : (int * DecisionPackage) list =
        notes
        |> List.choose (fun note -> note.Package |> Option.map (fun package -> note.Turn, package))

    /// 它交回来的那一条（id + 同一次前向的候选分布）→ 这一行。
    ///
    /// **算不动就整行不出现**（票面原话，与票 92 同一个规矩）：它交不出来（`None`）、
    /// 或者那个 id 压根不在这一包里时回 None——页面上因此一个元素都没有，
    /// 而不是一行「暂无」（占位的那一行看着像坏了）。
    /// **分布缺失不在此列**（票 103）：那时这一行照旧出，只是退回票 93 那一句。
    ///
    /// **同不同按 id 比**：两边都是同一包里的序号，因此这一步没有第二份「怎么算同一手」的判据
    /// （摸切与手切也因此不会被当成分歧：一包里同一张牌只有一条打法，见 `mjai.ts` 的 `actionKey`）。
    /// **「你排第几」同理按 id 比**，而不是拿中文去配。
    let strongOf (note: ReviewNote) (answer: BaselineAnswer) : ReviewStrong option =
        // 一条候选 → 它在这一包里叫什么。**序号用的是上游那一列的位置**（`List.indexed` 在前）：
        // 先 choose 再编号的话，中间一条认不出来就会把第 3 条悔成第 2 条。
        let choicesFrom (options: ActionOption list) : ReviewChoice list =
            answer.Candidates
            |> List.indexed
            |> List.choose (fun (index, choice) ->
                options
                |> List.tryFind (fun option -> ActionOption.id option = choice.ActionId)
                |> Option.map (fun option -> {
                    ActionId = choice.ActionId
                    Label = ActionOption.label option
                    Key = keyOf (ActionOption.action option)
                    P = choice.P
                    Rank = index + 1
                }))

        match note.Package, answer.ActionId with
        | Some package, Some id ->
            let options = DecisionPackage.options package

            options
            |> List.tryFind (fun option -> ActionOption.id option = id)
            |> Option.map (fun option ->
                let candidates = choicesFrom options
                let played = DecisionPackage.tryId note.Played package

                {
                    Turn = note.Turn
                    ActionId = id
                    Label = ActionOption.label option
                    Key = keyOf (ActionOption.action option)
                    // 包里找不到你那一手时算「不同」：那只会发生在包与牌谱对不上时，
                    // 而那一天该看见的是一个刺眼的标记，不是一句「与你相同」。
                    // （dotnet 那一侧的用例正面钉着「每一手都找得到」，这一支因此跑不到。）
                    Differs = played <> Some id
                    LatencyMs = answer.LatencyMs
                    Candidates = candidates
                    CandidatesTotal = answer.CandidatesTotal
                    Yours = candidates |> List.tryFind (fun choice -> Some choice.ActionId = played)
                    PlayedLabel = note.Label
                })
        | Some _, None
        | None, _ -> None

    /// 分歧那几手的**手序**（升序）。
    ///
    /// **它是「几手不同」那个数的来源**（`disagreements` 就是它的长度）：面板抬头那一句
    /// 与时间轴上那几枚标记因此数的是同一件事，不会一边说 64 手、另一边标出 63 枚。
    let disagreeing (rows: ReviewStrong list) : int list =
        rows
        |> List.filter (fun row -> row.Differs)
        |> List.map (fun row -> row.Turn)
        |> List.sort

    /// 分歧手数（面板抬头那一句与闸门读它）。**它不是分数**：
    /// 分母是“问出来了几手”而不是“你打了几手”，也不成百分比——
    /// 一旦写成百分比，下一步就是拿它当分数（票面边界：不造总分）。
    let disagreements (rows: ReviewStrong list) : int = disagreeing rows |> List.length

    // ---- 值得看的那几手（票 105） ----

    /// 「它很确定」那一头的阈值：**上游给它自己那一手的那个数**（`candidates[0].p`）要过它。
    ///
    /// **这个数是量出来的，不是拍的**（判据 14：先量再说）。两次实测：
    ///
    /// - **真人牌谱语料**（`tests/fixtures/paifu/mjai/` 111 份 × 四席 = 69,318 个决策点，
    ///   与报告 103 量的是同一批点）：你打的与它头一条**不同**的占 23.7%，
    ///   那几手的 `p0` 中位 0.5627、p75 0.7210、p95 0.9229。**取 0.8 大约落在它们的前五分之一**。
    ///   一局一席（半庄）落进这一条的：中位 2 手、p75 3 手、最多 8 手，19% 的席位一手都没有。
    /// - **换成 0.9**：46% 的席位一手都没有——那条判据就成了摆设；
    ///   **换成 0.6**：中位 5 手、最多 20 手，与「有分歧」那一半的毛病同族（票 103 实测
    ///   一整场 122 手里分歧 64 手，**一半以上**）。
    ///
    /// **分布上没有断崖**（0.75 / 0.8 / 0.85 分别命中 1147 / 876 / 642 个决策点，平滑下降）：
    /// 这是一个旋钮，不是一条自然边界。取 0.8 的判据是「一局一席剩几手」——**几手，不是一半**。
    [<Literal>]
    let private telling = 0.8

    /// 你那一手在它那一列里排到第几才算「排得很后」：**第三或根本不在这几条里**。
    ///
    /// 上游只给 top-3（报告 103），因此「第 3」就是那一列的末位；
    /// 真人牌谱语料上第 1 占 76.3%、第 2 占 14.4%、第 3 占 4.7%、不在里面占 4.5%。
    [<Literal>]
    let private trailing = 3

    /// 强 AI 那一行值不值得看：**它很确定，而你打的排在很后面**（票 105 的第三条筛选判据）。
    ///
    /// **它比「有分歧」窄得多**：分歧那一半在一整场里占一半（票 103 实测 122 手里 64 手），
    /// 光按分歧筛等于把一半的手改叫「精选」。
    ///
    /// **`notable` 蕴含 `Differs`**：排第三或不在候选里的那一手，必然不是它头一条挑的那一手
    /// （`Differs` 比的是同一包里的 id，`Rank` 也是）——因此时间轴上每一枚由这一条点亮的标记，
    /// 都落在 `disagreeing` 数出来的那几手里。
    ///
    /// **上游没给分布时恒 false**（老产物、或它这一手交不出候选）：那时「它有多确定」这件事
    /// 我们根本没拿到，拿缺席当「它很确定」就是编一个数。
    let notable (strong: ReviewStrong) : bool =
        match strong.Candidates with
        | [] -> false
        | head :: _ ->
            head.P >= telling
            && (match strong.Yours with
                | Some yours -> yours.Rank >= trailing
                | None -> true)

    /// 这一条标注**为什么**值得看（票 105 的筛选判据）。**两条，或的关系**：
    ///
    /// - `better`：**引擎的试打表里还有更好的换法**（`Better` 非空 = 你打的那张被帕累托占优）
    ///   ——零外部依赖，那几 MB 不在场时它照样筛得动（CI 里跑得到的就是这一半）；
    /// - `strong`：**强 AI 那一行 `notable`**（要那份产物在场）；两样都占就是 `both`。
    ///
    /// **为什么交的是一个词而不是一个布尔**：这一格上 DOM（`data-review-worth`），
    /// 于是闸门得以**逐手**核「是哪一条判据点亮了它」；只交布尔的话，两条判据里
    /// 任一条被改坠而另一条刚好盖住它的那几手，闸门一声不响（实测过：把阈值从 0.8
    /// 改成 0.7，多出来的那两手本来就已经因为 `better` 在列里，并集一数不变——报告 105 §4 红-2）。
    ///
    /// **「与强 AI 分歧」本身不在这两条里**（票面第一条本来写的是它）：量出来一整场
    /// 122 手里分歧 64 手，筛完还剩一半——那不是导航，是换个说法把整列再摆一遍。
    /// 收紧成 `notable` 之后，同一场里只靠强 AI 点亮的只有个位数（报告 105 §2）。
    let worth (strong: ReviewStrong option) (note: ReviewNote) : string =
        match not (List.isEmpty note.Better), strong |> Option.exists notable with
        | true, true -> "both"
        | true, false -> "better"
        | false, true -> "strong"
        | false, false -> ""

    /// 这一条标注值不值得看。**它就是上面那一格空不空**（不另写一遍判据：
    /// 写两遍就会有一天一处说值得看、另一处说不值得）。
    let worthwhile (strong: ReviewStrong option) (note: ReviewNote) : bool = worth strong note <> ""

    /// 这一席**值得看的那几手**（顺序与手序照旧）。
    ///
    /// **它同时是时间轴上那几枚标记的来源**（`marks`）：复盘那一列摆哪几条与轴上标哪几枚
    /// 出自同一次调用，因此两处不可能各标各的。
    let focused (rows: Map<int, ReviewStrong>) (notes: ReviewNote list) : ReviewNote list =
        notes |> List.filter (fun note -> worthwhile (Map.tryFind note.Turn rows) note)

    /// 时间轴上那几枚标记：**上面那一列的投影**（哪一手、第几帧）。
    let marks (notes: ReviewNote list) : ReviewMark list =
        notes |> List.map (fun note -> { Turn = note.Turn; Frame = note.Frame })

    // ---- 这一屏此刻给不给看 ----

    /// 这一场打完了吗（**面板在不在 DOM 里就看它**）。
    ///
    /// **判据是终局精算，不是「这一局终了」**：一局打完只是中场，
    /// 而这一票的边界是「对局中一律不给」——半场给出「换打会怎样」同样是作弊。
    ///
    /// **回放那一侧看的是末帧**：游标停在哪儿都不改变「这份牌谱是不是一场打完了的对局」。
    /// 断在半路的那种牌谱（票 85 那条）因此没有复盘面板——它本来就还没打完。
    let settled (model: TableModel) : bool =
        match model.Source with
        | Source.Live live ->
            match live.Table with
            | Ok table -> Table.result table |> Option.isSome
            | Error _ -> false
        | Source.Replay(ReplayTable.Ready(frames, _, _)) ->
            frames |> List.tryLast |> Option.bind Table.result |> Option.isSome
        | Source.Replay ReplayTable.Loading
        | Source.Replay(ReplayTable.Failed _) -> false

    /// 复盘**对着哪一席**；这一场还没打完、或者这一屏没有主语时是 None。
    ///
    /// **真人在座就恒是他**：那是 spec 的 story 34 说的那件事（「针对我每一手的复盘」），
    /// 而他终局之后视角是松开的（票 87 的 `unlocked`）——拿视角当主语的话，
    /// 他一按上帝视角，自己的复盘就没了。
    ///
    /// **没有真人时跟着视角走**（票面：模型席也能看）：坐到座位 2 就看座位 2 的逐手复盘。
    /// **上帝视角没有主语**：复盘的第一个字是「你」，四家一起复盘不是这一票的东西
    /// ——而且它顺带把首页那一屏的代价钉死在零（默认就是上帝视角，一手都不算）。
    let addressed (model: TableModel) : Seat option =
        if not (settled model) then
            None
        else
            match TableState.humanSeat model with
            | Some seat -> Some seat
            | None ->
                match TableState.viewpoint model with
                | Viewpoint.Seated seat -> Some seat
                | Viewpoint.God -> None

    /// 这一屏的逐帧牌桌。回放那一侧现成（帧在载入时一次 fold 好），
    /// Live 那一侧**导成牌谱再 fold 一遍**（`TableState.liveFrames`，与「导出牌谱」同一条路）
    /// ——Live 侧不常驻一份帧数组，那是票 76 定下的形态。
    let private framesOf (model: TableModel) : Table list =
        match model.Source with
        | Source.Replay(ReplayTable.Ready(frames, _, _)) -> frames
        | Source.Replay ReplayTable.Loading
        | Source.Replay(ReplayTable.Failed _) -> []
        | Source.Live _ -> TableState.liveFrames model

    /// 这一席的逐手标注。**贵在这一步**（每一手要现搭一份决策包），
    /// 因此面板那一侧把它包在 `React.useMemo` 里，同一份帧只算一次（见 `ReviewPanel`）。
    let notes (seat: Seat) (model: TableModel) : ReviewNote list = framesOf model |> notesFor seat

    /// 复盘面板此刻该画什么。**三态见 `ReviewShown`**。
    ///
    /// **公开的**：视图与页面逻辑的用例读同一处推导（同 `TableState.timeline` / `canAdvance`）。
    let shown (model: TableModel) : ReviewShown =
        match addressed model with
        | Some seat -> ReviewShown.Notes(seat, notes seat model)
        | None ->
            if settled model then
                ReviewShown.Unaddressed
            else
                ReviewShown.Hidden

    /// 这一屏的帧「还是不是那一份」的**便宜签名**（面板那一侧拿它当 `React.useMemo` 的依赖）。
    ///
    /// **只给一个整数**：`Option`、元组与列表每次渲染都是新对象，拿它们当依赖等于没有 memo。
    /// 回放是帧数（那一份帧在载入时就定了），Live 是这一桌落定了几手（终局之后它不再动）。
    let signature (model: TableModel) : int =
        match model.Source with
        | Source.Live live ->
            match live.Table with
            | Ok table -> table.Turns
            | Error _ -> -1
        | Source.Replay(ReplayTable.Ready(frames, _, _)) -> List.length frames
        | Source.Replay ReplayTable.Loading
        | Source.Replay(ReplayTable.Failed _) -> -1

    /// 此刻摊开的是第几手（票 86 的 `Opened`）：那一条标注要标出来，「回到原处」也读它。
    ///
    /// **不另存一份**：点一条标注发的就是票 76 那条 `RecordOpened`，
    /// 于是「跳过去」与「回得来」用的是同一套机制——轴只有一根（ADR-0002）。
    let opened (model: TableModel) : int option =
        model.Opened
        |> Option.filter (fun opened -> opened.Snapshot.Latest |> Option.isSome)
        |> Option.map (fun opened -> opened.Snapshot.Turns - 1)
