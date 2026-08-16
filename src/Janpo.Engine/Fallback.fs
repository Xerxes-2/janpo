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

    /// Assisted 档：**不退 Shanten 的安全打**（CONTEXT.md 的 Fallback）。
    ///
    /// 两步，顺序不能反：先取脚手架里**进退向为 0** 的那几条试打（不退向听），
    /// 再在它们之间按**危险度名次**挑最安全的那几张；并列时优先摸切——它与 Bare 档同手，
    /// 也不多暴露手牌信息。**摸切并不必然不退向听**：刚摸进的那张让手牌进了一步时，
    /// 把它扭头扔回去就是退向（向听戻し），这一档不这么打。
    ///
    /// **没人立直也没人副露时每条试打的 `Danger` 都是 None**（`Danger.rank`），
    /// 名次全相等于是保持原顺序——那时这一档与 24 号票的行为逐字相同。
    ///
    /// 打不了牌（响应阶段）或者脚手架算不出来时是 None，由调用方退回 `bare`。
    let private assisted (package: DecisionPackage) : Action option =
        DecisionPackage.scaffold package
        |> Option.bind (fun scaffold ->
            let candidates = scaffold.Dahai |> List.filter (fun trial -> trial.ShantenDelta = 0)

            let rank (trial: DahaiScaffold) : int =
                match trial.Danger with
                | Some danger -> danger.Rank
                | None -> 0

            let safest =
                match candidates with
                | [] -> []
                | _ ->
                    let best = candidates |> List.map rank |> List.min
                    candidates |> List.filter (fun trial -> rank trial = best)

            let actions =
                safest
                |> List.collect (fun trial -> trial.ActionIds)
                |> List.choose (fun id -> DecisionPackage.tryAction id package)

            actions
            |> List.tryFind isTsumogiri
            |> Option.orElseWith (fun () -> List.tryHead actions))

    /// 代打一手。**必然回一个合法动作**：候选全部取自决策包，而包里的动作列表非空。
    ///
    /// 分档在这里，不在 `bare` 里面：Assisted 在不退向听的那几手里挑最安全的，`bare` 一行不动。
    let action (tier: ScaffoldTier) (package: DecisionPackage) : Action =
        let options = DecisionPackage.options package |> List.map ActionOption.action

        match tier with
        | ScaffoldTier.Bare -> bare options
        // 挑不出不退向听的打牌（响应阶段、和了型手牌、脚手架算不出来）就退回 Bare：
        // 兜底只挑不发明，宁可摸切也不能没有一手交出去。
        | ScaffoldTier.Assisted -> assisted package |> Option.defaultWith (fun () -> bare options)
        // ToolSearch 是 M3 的事，它的兜底跟着 Assisted 走要等那一票定了工具的语义，先照 Bare 打。
        | ScaffoldTier.ToolSearch -> bare options
