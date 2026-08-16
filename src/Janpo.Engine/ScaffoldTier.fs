namespace Janpo

/// 脚手架档位（CONTEXT.md 的 ScaffoldTier）：**座位级配置**，决定引擎替这个座位算多少现成数值。
///
/// 它是一个配置项而不是难度旋钮：三档喂给 Player 的**规则事实完全相同**，
/// 差别只在「数值算不算好了递过去」。因此真人坐席复用同一个类型（它同时是新手辅助轮）。
///
/// **档位决定的是渲不渲染，不是算不算**：决策包恒带 `Scaffold`（向听数、有效牌、
/// 逐张试打的进退向），Bare 档的 prompt 不把它写出去，Assisted 档写。两档因此只差一个
/// 渲染函数，同一局面的 prompt 肉眼可比。
///
/// **M1 只实现前两档**：ToolSearch 的工具是 M3 的事，它在配置面板里灰着，
/// prompt 与 `Fallback` 都暂时照 Bare 走。
[<RequireQualifiedAccess>]
type ScaffoldTier =
    /// 裸奔：只给原始局面（合法观测 + 合法动作集），一个算好的数都不给。
    | Bare
    /// 信息辅助：附 Shanten / Ukeire / 进退向标注 / Danger 排序（24、25 号票）。
    | Assisted
    /// 工具搜索：在信息辅助之上追加「模拟打出某张后的局面」查询工具（M3）。
    | ToolSearch

/// 档位的取值与两个出口。
[<RequireQualifiedAccess>]
module ScaffoldTier =

    // ---- 构造 ----

    /// 全部档位，由弱到强。配置面板的选项就是它。
    let all: ScaffoldTier list =
        [ ScaffoldTier.Bare; ScaffoldTier.Assisted; ScaffoldTier.ToolSearch ]

    // ---- 出口 ----

    /// wire 上的名字。**Agent 层按它分档渲染 prompt**，因此它是接口的一部分，
    /// 不许跟着显示文案一起改（那是 `toDisplay` 的事）。
    let toWire (tier: ScaffoldTier) : string =
        match tier with
        | ScaffoldTier.Bare -> "bare"
        | ScaffoldTier.Assisted -> "assisted"
        | ScaffoldTier.ToolSearch -> "tool_search"

    /// wire 名回到档位。认不出来的字符串是 None——配置是人填的，什么都可能。
    let ofWire (wire: string) : ScaffoldTier option =
        all |> List.tryFind (fun tier -> toWire tier = wire)

    /// **渲染层的单向出口**（ADR-0001）：给人看的那个词。
    let toDisplay (tier: ScaffoldTier) : string =
        match tier with
        | ScaffoldTier.Bare -> "裸奔"
        | ScaffoldTier.Assisted -> "信息辅助"
        | ScaffoldTier.ToolSearch -> "工具搜索"
