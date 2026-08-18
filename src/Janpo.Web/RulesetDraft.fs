namespace Janpo.Web

open Janpo

/// 配桌上拨得动的那三项规则（spec 的 story 13；票 72）：**对局长度 / 赤宝牌 / 食断**。
///
/// **它不是第二个规则集**：这一桌真正在按的那一份是 `TableModel.Ruleset`
/// （引擎的一等输入，ADR-0004），牌谱里逐字段写的也是它。这一份是**还没开桌的那一份**
/// ——拨完要按「重开」才生效，与种子同一条路（`TableState.rulesPending` 把这件事说出来）。
/// **不许半场换规则**：那会让同一份牌谱前后按两套规则算，而回放照的是牌谱自带的那一份。
///
/// **只有三项，不做预设选择器**（票 72 的边界）：`Ruleset.majsoul` 早就存在，但它不进 UI
/// ——对照实验的自由变量越多，结论越难归因。其余每一根轴（头跳 / 切上满贯 / 三麻的座位数……）
/// 一律照 `Ruleset.yonma`（默认值对齐天凤，ADR-0004）。
type RulesetDraft = {
    /// 对局长度（CONTEXT.md 的 `GameLength`）。**它是规则集的一根轴，不是牌桌的字段**：
    /// 局数序列由它与座位数一起推出（`Ruleset.kyokus`），四麻东风战 4 局、半庄 8 局。
    Length: GameLength
    /// 赤宝牌用不用（CONTEXT.md 的 `Akadora`）。**引擎那边是一列牌**（`Ruleset.Akadora`，
    /// 空列表 = 关掉），这里只留「有没有」——牌山里换掉哪几张由预设说了算。
    Akadora: bool
    /// 食断成不成立（CONTEXT.md 的 `Kuitan`，`Ruleset.Kuitan`）：关掉时副露的手牌不成立断幺九。
    Kuitan: bool
}

/// 配桌上那三项各能拨到哪儿（票 72）。**一个 case 一根轴**：拨一项不动另外两项，
/// 因此页面上那一排按钮与 `RulesetDraft.pick` 的分支一一对应。
[<RequireQualifiedAccess>]
type RuleChoice =
    /// 拨对局长度。
    | Length of length: GameLength
    /// 拨赤宝牌的有无。
    | Akadora of akadora: bool
    /// 拨食断的有无。
    | Kuitan of kuitan: bool

/// 那三项的构造、拆解与两种记法。
[<RequireQualifiedAccess>]
module RulesetDraft =

    // ---- 构造 ----

    /// 从一份规则集读回那三项。**它是 `ruleset` 的逆向**：两个方向都在，
    /// 因此「页面上拨到的」与「这一桌真在按的」随时比得出来（`TableState.rulesPending`）。
    let ofRuleset (ruleset: Ruleset) : RulesetDraft = {
        Length = ruleset.Length
        Akadora = ruleset.Akadora |> List.isEmpty |> not
        Kuitan = ruleset.Kuitan
    }

    /// 一进页面摆的那一份：**就是 `Ruleset.yonma` 的那三项**（东风战 / 有赤 / 有食断）。
    /// 派生而不是另写一份字面量——预设改了它跟着改，两处不会漂。
    let initial: RulesetDraft = ofRuleset Ruleset.yonma

    /// 拨一项。
    let pick (choice: RuleChoice) (draft: RulesetDraft) : RulesetDraft =
        match choice with
        | RuleChoice.Length length -> { draft with Length = length }
        | RuleChoice.Akadora akadora -> { draft with Akadora = akadora }
        | RuleChoice.Kuitan kuitan -> { draft with Kuitan = kuitan }

    // ---- 拆解 ----

    /// 这一份拨到的三项对应的规则集。**底子恒是 `Ruleset.yonma`**：
    /// 三项之外一个字段都不动（`TableSetupTests` 有一条钉着这件事），
    /// 于是「拨了什么」与「这一桌与天凤默认差在哪」是同一句话。
    let ruleset (draft: RulesetDraft) : Ruleset =
        // 开着的形态就是预设本身，引擎那边只给了「关掉」的那一半（`Ruleset.withoutAkadora`
        // 等等），因此这里按开关选一个恒等变换或那一半。
        let unless (on: bool) (off: Ruleset -> Ruleset) : Ruleset -> Ruleset = if on then id else off

        Ruleset.yonma
        |> Ruleset.withLength draft.Length
        |> unless draft.Akadora Ruleset.withoutAkadora
        |> unless draft.Kuitan Ruleset.withoutKuitan

    // ---- wire 记法 ----

    /// 开关的 wire 名：**localStorage 的值与控件的 testId 用的都是它**（ADR-0001：
    /// wire 记法与中文渲染分开）。
    let switchToWire (on: bool) : string = if on then "on" else "off"

    /// wire 名回到开关；不认识的是 None（localStorage 里人手改过什么都可能）。
    let switchOfWire (wire: string) : bool option =
        match wire with
        | "on" -> Some true
        | "off" -> Some false
        | _ -> None

    /// 三项摊成一行 wire 摘要（`长度/赤/食断`）。**给无头闸门读**：页面上那一格
    /// `data-rules` 印的就是**这一桌真在按的**那一份，与导出牌谱里的 `ruleset` 逐项对得上。
    let toWire (draft: RulesetDraft) : string =
        let akadora = switchToWire draft.Akadora
        $"{GameLength.toWire draft.Length}/{akadora}/{switchToWire draft.Kuitan}"

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：开关那两枚按钮上的中文。
    let switchToDisplay (on: bool) : string = if on then "有" else "无"
