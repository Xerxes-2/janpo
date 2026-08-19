namespace Janpo.Web

open Janpo

/// 真人**自己按了「过」**的那一次响应（票 88 接手票 87 的那道缝）。
///
/// **语义在这一票里变了，字段一个没动**：票 87 那时页面上还没有鸣牌按钮，响应阶段是
/// **平台替他过的**；这一票把那一排按钮真做了出来，于是「过」是他自己按下去的那一条——
/// 这条记录记的因此是**他自己放掉了什么**。
///
/// **记账留着**（页面钩子 `data-human-passes` 也没换名字）：一局下来他放掉过哪几次碰、
/// 哪几次荣和，是复盘（票 90）第一件要问的事。
///
/// **票 89 把它拓宽了一格**（`AutoPlayed`）：时限到点代他打的那几手记在**同一本账**上
/// ——另起一张表的话，「这一局他有几手不是自己决的」就要从两处各数一遍再相加。
/// 这一本账因此记的是**这一手最后由谁决定**：`AutoPlayed = None` 是他自己按的那一下「过」，
/// `Some label` 是时限到点平台代他打的那一手。
type HumanPass = {
    /// 第几手（`Table.Turns`，跨局累计）。
    Turn: int
    /// 哪一席（本地真人那一席）。
    Seat: Seat
    /// 这一次放掉的那几条的中文 label，按合法动作集的既定顺序；**「过」本身不在里面**。
    /// 超时代打的那几手里它是「他没来得及宣言的那几条」（该他出牌那一手常常是空的）。
    Skipped: string list
    /// **时限到点平台代他打的那一手**（票 89），中文 label；**他自己按的那一次是 None**。
    ///
    /// 这一格就是票 88 欺下的那道缝：两种「过」在**数据里**分得开（而不是靠那句中文的措辞）。
    /// **带着 label 而不是一个 bool**：到点那一手可能是「过」也可能是摸切，
    /// 而「平台替你做了什么」不许静默替换（票 23 那条规矩）。
    AutoPlayed: string option
}

/// 一次「过」说出来是什么样。
[<RequireQualifiedAccess>]
module HumanPass =

    /// 这一次是他自己按的吗（票 89）。**两种「过」只有这一条判据**：
    /// 页面上那句话、两个计数钩子与用例读的都是它。
    let pressed (pass: HumanPass) : bool = Option.isNone pass.AutoPlayed

    /// 页面上那半句话。**逐条列出来**而不是「过了一次」：人要知道放掉的是碰还是荣和。
    let toDisplay (pass: HumanPass) : string =
        let lost =
            match pass.Skipped with
            // 他自己按那一下时走不到：引擎只在真有得选的时候才把这一席摆进合法动作集
            // （`Action.None` 的注释）；超时代打那几手则常常真的一条宣言都没有。
            | [] -> ""
            | skipped -> "，放掉了：" + String.concat "、" skipped

        match pass.AutoPlayed with
        | None -> $"第 {pass.Turn} 手你按了「过」{lost}"
        // **代打了什么要写出来**（同引擎兑底那一手，票 23）：
        // 人抬头看见河里多了一张，要知道那是时限打的而不是自己误点的。
        | Some played -> $"第 {pass.Turn} 手时限到点，替你打了：{played}{lost}"

/// 页面上**那一枚按钮**要的三样东西（票 88）：包内 id、给机器看的动作名、给人看的中文 label。
///
/// **它不是一个新的领域概念**（`CONTEXT.md` 一个词都没加）：三格全部照抄那一份决策包。
/// 「吃有左中右三种」「碰要不要亮赤 5」在这里只是**三条各自带 id 的按钮**——
/// 渲染层从没听说过吃是什么，因此也不可能算错。
type ActionButton = {
    /// 这一包内的 id。**跨界回去的只有它**（`TableMsg.HumanPlayed`）。
    Id: int
    /// mjai 的动作名。**给机器看的那一半**：闸门按它数按钮、样式按它挑颜色。
    /// 中文 label 只给人看（ADR-0001：渲染层的单向出口不得被判定消费）。
    Kind: string
    /// 引擎给的中文 label（`ActionOption.label`），一个字都不必再翻。
    Label: string
}

/// **真人坐席**（CONTEXT.md 的 `Human Seat`）：由本地真人操作的 Player 实现，
/// 它的「决策函数」就是渲染动作输入 UI 并等一次点击（spec 的 story 28 / 30）。
///
/// **这个模块一条规则都不判**（spec 的 UI 决策：合法性驱动 UI，而不是 UI 自行判断）：
/// 哪几张点得出去、能吃哪几种、「过」是哪一条、立直宣言完还能打什么，
/// 全部**现问那一份决策包**（`DecisionPackage`）——而那份包与 LLM prompt 消费的是同一份投影
/// （`Observation Projection` 那条词条），因此真人这一侧也看不见他家的暗牌。
///
/// **四个出口合起来正好是那一包，一条不多一条不少**（票 88 的要害判据）：
/// `dahaiOptions`（画在自家手牌上）+ `buttons`（牌桌下面那一排）+ `pass`（那一排最后一枚）
/// = `DecisionPackage.options`。找不到 id 的按钮不许存在，包里的每一条也都得有地方点得到
/// ——两个方向的用例各钉一遍（`HumanCallTests`）。
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

    /// 一条动作**给机器看的那个名字**：mjai 动作消息里的 `type`（`Action.encoder` 那一份）。
    ///
    /// **照抄 mjai，不自造**（裁决 D-1：标识符按术语表拼作 `Riichi` / `Minkan` / `Ryuukyoku`，
    /// wire 上仍是 `reach` / `daiminkan` / `ryukyoku`）。它只是页面上的一个 `data-*`：
    /// 判定一律不许读它（ADR-0001 那条对中文 label 的禁令，对这一份同样成立）。
    ///
    /// `Action` 加一个 case（三麻的拔北）时编译器会在这里点名（`--warnaserror` 下
    /// 不完整 match 是错误），因此页面上不会冒出一枚没名字的按钮。
    let kind (action: Action) : string =
        match action with
        | Action.Dahai _ -> "dahai"
        | Action.Hora _ -> "hora"
        | Action.Pon _ -> "pon"
        | Action.Chi _ -> "chi"
        | Action.Ankan _ -> "ankan"
        | Action.Kakan _ -> "kakan"
        | Action.Minkan _ -> "daiminkan"
        | Action.Riichi _ -> "reach"
        | Action.None _ -> "none"
        | Action.Ryuukyoku _ -> "ryukyoku"

    /// 一条动作变成页面上那一枚按钮。
    let private buttonOf (option: ActionOption) : ActionButton = {
        Id = ActionOption.id option
        Kind = ActionOption.action option |> kind
        Label = ActionOption.label option
    }

    /// 「过」那一条（`Action.None`）；该他出牌的那一手是 None。
    ///
    /// **它同时是「此刻是哪一种轮到」的判据**：有「过」= 他家打了牌、在等他要不要鸣；
    /// 没有「过」= 该他自己出牌。响应阶段必有它（`Action.None` 那段注释：
    /// 合法动作集里出现了 Ron / Pon / Chi / Kan，就必须同时有一条「过」）——
    /// 因此这里不必另立一个「阶段」枚举，也就没有第二份会漂的判据。
    ///
    /// **页面上「过」永远在**（票 88 的票面原话）：响应阶段不点就卡住是最难受的死法，
    /// 而「不点」在这一层根本不存在——这一条与吃碰杠同样只是包里的一条 id。
    let pass (package: DecisionPackage) : ActionButton option =
        DecisionPackage.options package
        |> List.tryFind (fun option ->
            match ActionOption.action option with
            | Action.None _ -> true
            | _ -> false)
        |> Option.map buttonOf

    /// 这一手**除打牌与「过」之外**的那几条（吃 / 碰 / 杠 / 立直 / 荣和 / 自摸 / 九种九牌），
    /// 按包内顺序——它们就是牌桌下面那一排按钮（票 88）。
    ///
    /// **不许在这一层判它们是什么**：这里只把「不是打牌、也不是过」的那几条原样交出去，
    /// 因此吃的左中右、碰要不要亮赤 5 各自是一条，加一个新动作（三麻的拔北）时
    /// 它自己就跟着出现在页面上。
    ///
    /// **打牌不在这里**：那几条画在自家手牌上（`dahaiOptions`），点牌本身就是打出去——
    /// 再摆一排「手切 3 索」的按钮是同一件事的第二个入口。
    let buttons (package: DecisionPackage) : ActionButton list =
        DecisionPackage.options package
        |> List.filter (fun option ->
            match ActionOption.action option with
            | Action.Dahai _
            | Action.None _ -> false
            | _ -> true)
        |> List.map buttonOf

    /// **立直宣言了、正在选宣言牌**吗（票 88 的「立直是两段」）。
    ///
    /// **这不是渲染层在判规则**：宣言与成立是引擎的两段状态（`RiichiState.Declared`），
    /// 就摆在这一席自己的观测里；能打哪几张同样仍旧只由合法动作集说了算
    /// （宣言之后那一集自然只剩「打完仍听牌」的那几张）。页面读它只为了**把话说对**——
    /// 那一手能点的牌突然少了一半，不说一句人会以为页面坏了。
    let declaringRiichi (package: DecisionPackage) : bool =
        (DecisionPackage.observation package).Self.Riichi |> RiichiState.isDeclared
