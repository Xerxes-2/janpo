namespace Janpo

open Thoth.Json.Core

/// 决策包里的一条动作：**稳定的 id、mjai 风格的结构化字段与中文 label**。
///
/// 表示私有：`Action` 只进不出——它经 `ActionOption.action` 取得回来，
/// 但外面构造不出一条 `ActionOption`，因此包里的动作恒是引擎自己给出的合法动作。
type ActionOption =
    private
        {
            /// 这一包内的序号，0 起。
            Id: int
            /// 那个动作本身。**不上 wire**：跨界回来的只有 `Id`。
            Action: Action
            /// 中文 label（渲染层的单向出口，ADR-0001）。
            Label: string
        }

/// 决策包：某座位的合法观测 + 带 id 与中文 label 的动作列表。
/// **LLM 与（M3 的）真人坐席消费的唯一输入**，两边共用同一个投影，
/// 隐藏信息的保护因此在结构上成立而不靠纪律。
///
/// **`GameState` 永不越过 F#/TS 边界**：跨界的只有这份包的 JSON（单向 encode）
/// 与回来的一个动作 id（`DecisionPackage.tryAction`）。TS 侧永不构造 `Action`，
/// 非法动作因此在结构上不可能。
type DecisionPackage =
    private
        {
            /// 正在被问的座位。
            Seat: Seat
            /// 这个座位的合法观测。
            Observation: Observation
            /// 它此刻能提交的全部动作，按合法动作集的既定顺序编号。非空。
            Options: ActionOption list
            /// 脚手架数值（向听数、有效牌、逐张试打的进退向）。**包恒带它**，
            /// 档位决定的是 prompt 渲不渲染（Bare 档不渲染）。手牌形态读不出来时是 None。
            Scaffold: Scaffold option
        }

/// 一条编号动作的拆解与编码。
[<RequireQualifiedAccess>]
module ActionOption =

    // ---- 拆解 ----

    /// 这一包内的序号。
    let id (option: ActionOption) : int = option.Id

    /// 那个动作本身。**只有引擎这侧拿得到**。
    let action (option: ActionOption) : Action = option.Action

    /// 中文 label。
    let label (option: ActionOption) : string = option.Label

    // ---- JSON（单向出口） ----

    /// `{ "id": 3, "label": "手切3索", "action": { …mjai 动作消息… } }`。
    /// 结构化字段整个嵌在 `action` 里：它就是一条 mjai 动作消息，与 `id` / `label`
    /// 这两个本项目的东西分开放，读的人不会把它跟事件混起来。
    let encoder: Encoder<ActionOption> =
        fun option ->
            Encode.object
                [
                    "id", Encode.int option.Id
                    "label", Encode.string option.Label
                    "action", Action.encoder option.Action
                ]

/// 决策包的构造、取回与编码。
[<RequireQualifiedAccess>]
module DecisionPackage =

    // ---- 构造 ----

    /// 局面 + 座位 → 那个座位的决策包。**只有正在被问的座位有决策包**：
    /// 这个座位此刻不在合法动作集里（或者压根不是这个规则集的座位）时是 None。
    ///
    /// 编号就是合法动作集的下标：同一局面必得同一份编号（合法动作集的顺序是既定的），
    /// 因此 id 在**这一包内**稳定。它不跨局面稳定——每问一次就是新的一包。
    let forSeat (seat: Seat) (state: GameState) : DecisionPackage option =
        let asked =
            GameState.legalActions state |> List.tryFind (fun choice -> choice.Seat = seat)

        match asked, Observation.ofState seat state with
        | Some choice, Some observation ->
            let options =
                choice.Actions
                |> List.mapi (fun index action ->
                    {
                        Id = index
                        Action = action
                        Label = Action.toDisplay action
                    })

            // 脚手架从**这份遮蔽好的观测**再算一遍，不从 `state` 抄近路：
            // 它因此一张多余的牌也看不见（他家的暗牌在 `MaskedSeat` 里没有位置）。
            let numbered = options |> List.map (fun option -> option.Id, option.Action)

            Some
                {
                    Seat = seat
                    Observation = observation
                    Options = options
                    Scaffold = Scaffold.calculate (GameState.ruleset state).TileKinds numbered observation
                }
        | Some _, None
        | None, _ -> None

    // ---- 拆解 ----

    /// 正在被问的座位。
    let seat (package: DecisionPackage) : Seat = package.Seat

    /// 这个座位的合法观测。
    let observation (package: DecisionPackage) : Observation = package.Observation

    /// 编号动作，按合法动作集的既定顺序。
    let options (package: DecisionPackage) : ActionOption list = package.Options

    /// 脚手架数值。手牌形态读不出来时是 None——`Fallback` 的 Assisted 档读的就是它，
    /// 读不到就退回 Bare 档的兜底。
    let scaffold (package: DecisionPackage) : Scaffold option = package.Scaffold

    /// **反方向的唯一入口**：一个整数 → 那个动作。
    /// id 越界或压根不存在时是 None 而不是异常——兜底路径（23 号票）依赖这一条，
    /// 而 LLM 给回来的整数什么都可能是。
    let tryAction (id: int) (package: DecisionPackage) : Action option =
        package.Options
        |> List.tryFind (fun option -> option.Id = id)
        |> Option.map (fun option -> option.Action)

    /// `tryAction` 的反向：一个动作 → 它在这一包里的 id。包里没有这个动作时是 None。
    ///
    /// **决策记录用它**（26 号票）：兜底代打的那一手要记下「最终采用的是哪一条」，
    /// 而 `Fallback.action` 给回的是动作本身。牌谱里存的是 id 不是动作——动作是意图，
    /// 意图不上牌谱（`Action.encoder` 那条「单向出口」）。
    let tryId (action: Action) (package: DecisionPackage) : int option =
        package.Options
        |> List.tryFind (fun option -> option.Action = action)
        |> Option.map (fun option -> option.Id)

    // ---- JSON（单向出口） ----

    /// 决策包的 wire 形态。**只有 encoder，没有 decoder**：包只往外走，回来的是一个 id。
    ///
    /// 裁决 21-c（票 28）要的那个「只服务测试的 decoder」**不在这里，也不产出这个类型**：
    /// 黄金用例逐字段对照时读的是 `GoldenJson.fields` 摊平出来的**文本**（`Janpo.Golden`，
    /// 测试脚手架不进引擎），它构造不出 `DecisionPackage`、更构造不出 `Action`。
    /// 「出去的是决策包，回来的是 id 与事实」这条边界因此一个字都没松。
    ///
    /// `scaffold` 是分析附件（24 号票填的向听数、有效牌与逐张试打的进退向，
    /// 25 号票再往里加 Danger），**与 `observation` 分开放**：投影只做遮蔽不做计算，
    /// 两件事不混在一起。算不出来时它退回空对象。
    let encoder: Encoder<DecisionPackage> =
        fun package ->
            Encode.object
                [
                    "seat", Seat.encoder package.Seat
                    "observation", Observation.encoder package.Observation
                    "actions", package.Options |> List.map ActionOption.encoder |> Encode.list
                    "scaffold", Scaffold.slotEncoder package.Scaffold
                ]
