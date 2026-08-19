namespace Janpo.Web

open Janpo

/// 真人这一侧看得到的**一条试打**（票 89 的 story 33）。
///
/// **它抱着引擎那条记录本身**（`DahaiScaffold`），一个数都不复制、更不重算：
/// 这就是票面那句「真人看到的与模型看到的必须来自同一个纯函数」落成的代码事实
/// ——`Trial` 与模型 prompt 里那一行读的是同一份 `Scaffold`
/// （`DecisionPackage.scaffold`，由引擎的 `Scaffold.calculate` 随包算好）。
///
/// **两格是引擎的、一格是包里的**：`Trial` 是那几个数，`Id` / `Label` 是这一条动作在
/// 这一包里的号与中文——三样合起来正好是「点这张牌会怎样」。
type ScaffoldLine = {
    /// 这一条打牌动作的包内 id。**与手牌上那张牌的 `data-dahai-id` 是同一个号**：
    /// 人看完这一行就知道该点哪一张，闸门也照它把两处对起来。
    Id: int
    /// 引擎给的中文 label（「手切3索」）。
    Label: string
    /// 打完这一张之后的形态：向听、进退向、有效牌、危险度。**引擎算的那一条原样带着**。
    Trial: DahaiScaffold
}

/// **新手辅助轮**（CONTEXT.md 的 `ScaffoldTier` 词条最后一句：「真人坐席复用同一类型，
/// 它同时是新手辅助轮」）：把一份决策包里**引擎已经算好的那几个数**摆成真人看得懂的几行。
///
/// **这一层一个数都不算**（同 `HumanSeat` 一条规则都不判）：向听、有效牌、进退向与危险度
/// 全部取自 `DecisionPackage.scaffold`——那正是模型 prompt 尾部那一节读的同一份。
/// 两处各算一遍迟早有一天对不上，而那时人会先怀疑引擎（票面的要害）。
///
/// **感知 vs 计算那条线逐字照用**（术语表原话，不自己发明清单）：
/// Bare 给的是「一个坐在牌桌前的人免费得到的一切」（手牌、四家的河与副露、点数、
/// 宝牌指示牌、巡目、牌山剩余——牌桌上本来就画着），**这一层一样都不给**；
/// Assisted 给的是「要算才有的量」——Shanten、Ukeire、ShantenDelta、Danger，
/// 也就是这个模块的全部内容。
[<RequireQualifiedAccess>]
module HumanScaffold =

    /// 这一档给不给他看那几个算好的数（票 89）。
    ///
    /// **ToolSearch 按 Assisted 处理**（票面原话）：那一档加的是「自己去问的能力」
    /// （术语表：不是又一批算好的数值），而**这一票不给真人做查询面板**——
    /// 真人这一侧因此只有「给不给算好的数」这一个分法。面板上那一格拨到它时，
    /// 页面会说清楚「你拿到的是信息辅助那一档」（`HumanLine`），不静默降级。
    let shows (tier: ScaffoldTier) : bool =
        match tier with
        | ScaffoldTier.Bare -> false
        | ScaffoldTier.Assisted
        | ScaffoldTier.ToolSearch -> true

    /// 这一手的那几行，按**合法动作集的既定顺序**（`Scaffold.Dahai` 就是那个顺序）。
    ///
    /// **一条试打可能对着好几个 id**（手里两张 5m、一张是赤 5：手切两条、摸切一条），
    /// 而页面上每一条 id 各是一张点得动的牌——因此这里**按 id 摊开**：
    /// 人点哪一张，就在哪一行上找得到那几个数。
    ///
    /// 响应阶段（没有牌可打）与手牌形态读不出来时是空表：**不给半个数**
    /// （同 `Scaffold.calculate` 那条注释：错的向听数比没有向听数更坏）。
    let lines (package: DecisionPackage) : ScaffoldLine list =
        match DecisionPackage.scaffold package with
        | None -> []
        | Some scaffold ->
            scaffold.Dahai
            |> List.collect (fun trial ->
                trial.ActionIds
                |> List.map (fun id -> {
                    Id = id
                    Label =
                        DecisionPackage.options package
                        |> List.tryFind (fun option -> ActionOption.id option = id)
                        |> Option.map ActionOption.label
                        // 走不到：那几个 id 就是从这一包的动作列表里编出来的（`Scaffold.calculate`
                        // 收的是 `numbered`）。真走到了也不编造一个名字。
                        |> Option.defaultValue $"第 {id} 条"
                    Trial = trial
                }))
            |> List.sortBy (fun line -> line.Id)

    /// 一条试打说出来是什么样。**三段都由引擎的单向出口渲**（ADR-0001：
    /// `Shanten.toDisplay` / `Ukeire.toDisplay` / `Danger.toDisplay`）——
    /// 页面这一层连「几向听怎么念」都不知道。
    ///
    /// 与 prompt 里那一行（`web/src/agent/prompt.ts` 的 `trial`）**说的是同一件事**，
    /// 措辞各自照各自那一侧的读者来（模型那一侧要 mjai 记法与 id，人这一侧要中文牌名）。
    /// **对得起来的是数，不是字节**——闸门对拍的正是那几个数（`HumanAssistTests`）。
    let toDisplay (line: ScaffoldLine) : string =
        let trial = line.Trial

        let delta =
            match trial.ShantenDelta with
            | 0 -> "进退向 0"
            | delta -> $"退向 +{delta}"

        // **有效牌那一段整段交给引擎的单向出口**（`Ukeire.toDisplay`：「2 向听，有效牌 8 枚：4筒(4) …」）：
        // 它自带打完之后的向听（`Ukeire.Shanten` 就是试打后那一个）、每张牌的中文名与剩余枚数
        // ——**页面这一层因此连「几枚怎么写」都不必知道**，也就漂不出第二种写法。
        // 算不出来（可见张数越界）时**不说有效牌**：那是数据出了问题，不是「没有有效牌」
        // （后者是一个空的 `Ukeire`，那句话由 `Ukeire.toDisplay` 自己写成「无有效牌」）。
        let ukeire =
            match trial.Ukeire with
            | None -> Shanten.toDisplay trial.Shanten
            | Some ukeire -> Ukeire.toDisplay ukeire

        let danger =
            match trial.Danger with
            // 没有一家立直或副露：这一手的危险度**没有被评价的对象**（`Danger.rank`），
            // 一个字都不写——写「无」会让人以为它安全。
            | None -> ""
            | Some danger -> $"　危险度第 {danger.Rank} 位（{Danger.toDisplay danger}）"

        $"{line.Label}：打完 {ukeire}（{delta}）{danger}"

    /// 这一手**现在**这副牌是什么样（还没打）：向听 + 有效牌（只有等摸形才有）。
    ///
    /// **它与那几行是两件事**：那几行说「打完某一张会怎样」，这一句说「现在怎样」。
    /// 少了它人读不出「我打完退了一步没有」——而进退向正是拿它当基准算的。
    let summary (scaffold: Scaffold) : string =
        match scaffold.Ukeire with
        | None -> $"现在 {Shanten.toDisplay scaffold.Shanten}"
        | Some ukeire ->
            $"现在 {Shanten.toDisplay scaffold.Shanten}，有效牌 {Ukeire.total ukeire} 枚 {Ukeire.kindCount ukeire} 种"

    /// 有威胁的那几家（立直了或有副露的他家）说出来是什么样；一家都没有时是空串。
    let threats (scaffold: Scaffold) : string =
        match scaffold.Threats with
        | [] -> ""
        | threats -> threats |> List.map Threat.toDisplay |> String.concat "、"
