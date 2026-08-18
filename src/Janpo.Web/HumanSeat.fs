namespace Janpo.Web

open Janpo

/// 替真人**自动过掉**的那一次响应（票 87）。
///
/// 这一票只做「出牌」那条最窄的路：他家打出一张、真人本来可以碰 / 吃 / 杠 / 荣和的那一刻，
/// 平台替他提交「过」。**替他过掉了什么必须说出来**——不说的话，人会以为这个平台漏了鸣牌。
///
/// **它是留给票 88 的接缝**：那一票把「过」换成一排真按钮时改的是「给不给自动过」这一处判断
/// （`TableState.handed`），而 `Skipped` 那一列**就是它要长出来的那几枚按钮**——
/// 它已经是引擎给的中文 label（`ActionOption.label`），一个字都不必再翻。
type AutoPass = {
    /// 第几手（`Table.Turns`，跨局累计）。
    Turn: int
    /// 哪一席（本地真人那一席）。
    Seat: Seat
    /// 这一手被跳过的那几条的中文 label，按合法动作集的既定顺序；**「过」本身不在里面**。
    Skipped: string list
}

/// 一次自动过说出来是什么样。
[<RequireQualifiedAccess>]
module AutoPass =

    /// 页面上那半句话。**逐条列出来**而不是「替你过了一次」：人要知道错过的是碰还是荣和。
    let toDisplay (pass: AutoPass) : string =
        match pass.Skipped with
        // 走不到：引擎只在真有得选的时候才把这一席摆进合法动作集（`Action.None` 的注释）。
        // 真走到了也要说一句，空白与「没发生过」在页面上分不开。
        | [] -> $"第 {pass.Turn} 手替你过了"
        | skipped -> $"第 {pass.Turn} 手替你过了：" + String.concat "、" skipped

/// **真人坐席**（CONTEXT.md 的 `Human Seat`）：由本地真人操作的 Player 实现，
/// 它的「决策函数」就是渲染动作输入 UI 并等一次点击（spec 的 story 28 / 30）。
///
/// **这个模块一条规则都不判**（spec 的 UI 决策：合法性驱动 UI，而不是 UI 自行判断）：
/// 哪几张点得出去、「过」是哪一条、这一手还有哪几条这一票表达不出来，
/// 全部**现问那一份决策包**（`DecisionPackage`）——而那份包与 LLM prompt 消费的是同一份投影
/// （`Observation Projection` 那条词条），因此真人这一侧也看不见他家的暗牌。
///
/// **它与 `Roster` 是两层**：`SeatPlayer.Human` 说「这一席是他」，这里说「他此刻点得动什么」。
[<RequireQualifiedAccess>]
module HumanSeat =

    /// 正在被问的那一席（就写在包上，不另存一份）。
    let seat (package: DecisionPackage) : Seat = DecisionPackage.seat package

    /// 「打出这一张」在这一包里的 id 与中文 label；包里没有这一条时是 None
    /// （**于是那张牌在页面上就点不动**：立直之后只剩摸切、食替不许打回去，都是这一条的后果）。
    ///
    /// **手切与摸切各问各的**：它们是两个不同的动作（河上的手切信息是公开信息，
    /// 见 `Action.Dahai` 那段注释），因此同一张牌可能有两条、也可能只有一条。
    let dahai (pai: Tile) (tsumogiri: bool) (package: DecisionPackage) : (int * string) option =
        DecisionPackage.options package
        |> List.tryFind (fun option ->
            match ActionOption.action option with
            | Action.Dahai(_, played, giri) -> played = pai && giri = tsumogiri
            | _ -> false)
        |> Option.map (fun option -> ActionOption.id option, ActionOption.label option)

    /// 这一手点得出去的那几张，按包内顺序：包内 id、哪张牌、是不是摸切、中文 label。
    ///
    /// **它就是「能点哪几张」的唯一来源**：页面照它渲染，用例照它对拍——
    /// 渲染层拿手牌自己筛一遍就是第二处判据，而那正是「合法性驱动 UI」要治的病。
    let dahaiOptions (package: DecisionPackage) : (int * Tile * bool * string) list =
        DecisionPackage.options package
        |> List.choose (fun option ->
            match ActionOption.action option with
            | Action.Dahai(_, pai, tsumogiri) ->
                Some(ActionOption.id option, pai, tsumogiri, ActionOption.label option)
            | _ -> None)

    /// 「过」那一条（`Action.None`）；该他出牌的那一手是 None。
    ///
    /// **它同时是「此刻是哪一种轮到」的判据**：有「过」= 他家打了牌、在等他要不要鸣；
    /// 没有「过」= 该他自己出牌。响应阶段必有它（`Action.None` 那段注释：
    /// 合法动作集里出现了 Ron / Pon / Chi / Kan，就必须同时有一条「过」）——
    /// 因此这里不必另立一个「阶段」枚举，也就没有第二份会漂的判据。
    let passAction (package: DecisionPackage) : Action option =
        DecisionPackage.options package
        |> List.map ActionOption.action
        |> List.tryFind (fun action ->
            match action with
            | Action.None _ -> true
            | _ -> false)

    /// 这一手合法、但**这一票还表达不出来**的那几条的中文 label（吃 / 碰 / 杠 / 立直 /
    /// 荣和 / 自摸 / 九种九牌）——票 88 会把它们变成真按钮。
    ///
    /// **不许在这一层判它们是什么**：这里只把「不是打牌、也不是过」的那几条原样交出去，
    /// 加一个新动作（三麻的拔北）时它自己就跟着出现在页面上。
    let unspoken (package: DecisionPackage) : string list =
        DecisionPackage.options package
        |> List.filter (fun option ->
            match ActionOption.action option with
            | Action.Dahai _
            | Action.None _ -> false
            | _ -> true)
        |> List.map ActionOption.label
