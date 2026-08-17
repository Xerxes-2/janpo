namespace Janpo

/// 振听（CONTEXT.md）：因自己曾打出和了牌而不能荣和的状态。**两种分别维护**，语义不同：
///
/// - **永久振听**：自己的河里有自己的和了牌。它按「当前听牌 × 自己的河」重算，
///   只要听牌没变就一直成立，**不会因为过了一巡而解除**——这是它与同巡振听的区别。
///   （09 的立直振听是另一回事：立直后见逃是**闩死的**，不能被重算解除；
///   它是在这个记录上再加一位还是让重算跳过立直中的座位，由 09 自己定，06 不替它预设。）
/// - **同巡振听**：本巡见逃了一次可以荣和的牌，到自己下次**摸打**为止不能荣和
///   （摸牌解除；自家鸣牌接着的打牌同样翻篇——票 63 的天凤实证）。
///
/// 两者都只挡荣和，从不挡自摸。合成一个 bool 就分不出「重算」与「到下次摸牌为止」
/// 这两条不同的解除条件，因此这里是两个字段。
type Furiten =
    {
        /// 永久振听：自己的河里有自己的和了牌（立直后见逃亦然）。
        Permanent: bool
        /// 同巡振听：本巡见逃过一次荣和，到自己下次摸打（摸牌或鸣牌）为止有效。
        Doujun: bool
    }

/// 振听两种状态的构造与判定。
[<RequireQualifiedAccess>]
module Furiten =

    // ---- 构造 ----

    /// 两种都不成立：可以荣和。
    let none: Furiten = { Permanent = false; Doujun = false }

    // ---- 判定 ----

    /// 是否挡住荣和。**只挡荣和**：自摸和与振听无关。
    let blocksRon (furiten: Furiten) : bool = furiten.Permanent || furiten.Doujun

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (furiten: Furiten) : string =
        match furiten.Permanent, furiten.Doujun with
        | true, true -> "振听（永久 + 同巡）"
        | true, false -> "振听（永久）"
        | false, true -> "振听（同巡）"
        | false, false -> "非振听"
