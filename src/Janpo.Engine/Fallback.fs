namespace Janpo

/// 兜底（CONTEXT.md 的 Fallback）：Player 输出非法、解析失败或超时且重试用尽之后，
/// **代他执行**的那个动作。它保证「对局永不卡死」这条验收。
///
/// **兜底不放宽任何合法性**：代打的动作从那一手的决策包里挑，因此它与 Player 自己选的
/// 动作出自同一个合法动作集。引擎这边根本分不出哪一手是兜底来的——那是牌桌要显示的事
/// （23 号票：不许静默替换）。
///
/// **为什么在引擎里**：兜底策略要读规则（Bare 档的「摸切」是动作的形态，Assisted 档的
/// 「不退 Shanten 的安全打」还要算向听与危险度）。Agent 层拿不到 `Action`，也不该懂这些。
[<RequireQualifiedAccess>]
module Fallback =

    // ---- 候选的形态 ----

    /// 摸切：刚摸进那张原样打出去。Bare 档的兜底就是它（CONTEXT.md）。
    let private isTsumogiri (action: Action) : bool =
        match action with
        | Action.Dahai(_, _, true) -> true
        | _ -> false

    /// 「过」：他家打的这张不要。响应阶段轮不到打牌，最保守的一手是它。
    let private isPass (action: Action) : bool =
        match action with
        | Action.None _ -> true
        | _ -> false

    // ---- 策略 ----

    /// Bare 档：摸切 → 过 → 合法动作集的第一条。
    ///
    /// 第三级不是凑数：**立直宣言之后的那一手只许打「打完仍听牌」的牌**，摸切可能压根不在
    /// 合法动作集里；碰吃之后要打牌的那一手也没有「刚摸进的那张」。这两种局面下
    /// 前两级都落空，而合法动作集恒非空，因此取第一条必然给得出一个引擎接受的动作。
    let private bare (options: Action list) : Action =
        options
        |> List.tryFind isTsumogiri
        |> Option.orElseWith (fun () -> List.tryFind isPass options)
        |> Option.defaultWith (fun () -> List.head options)

    /// 代打一手。**必然回一个合法动作**：候选全部取自决策包，而包里的动作列表非空。
    ///
    /// 分档在这里，不在 `bare` 里面：24 号票把 Assisted 换成「不退 Shanten 的安全打」，
    /// 25 号票把 Danger 排序接进来，改的都是下面这个 `match` 的分支，`bare` 一行不动。
    let action (tier: ScaffoldTier) (package: DecisionPackage) : Action =
        let options = DecisionPackage.options package |> List.map ActionOption.action

        match tier with
        | ScaffoldTier.Bare -> bare options
        // 24 号票：Assisted 的兜底是「不退 Shanten 的安全打」；在那之前照 Bare 打，
        // 保守（摸切必不放铳新张，也必不退向听）且绝不卡死。
        | ScaffoldTier.Assisted
        | ScaffoldTier.ToolSearch -> bare options
