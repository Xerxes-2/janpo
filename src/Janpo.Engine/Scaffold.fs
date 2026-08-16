namespace Janpo

open Thoth.Json.Core

/// 试打一张之后的形态（CONTEXT.md 的 ShantenDelta 进退向）。
///
/// **红 5 与正 5 是同一条**：形态判定按牌种走（`HandShape` 一律去红），
/// 因此这里的 `Pai` 是去红后的牌种，而打得出它的动作可能有好几条。
type DahaiScaffold =
    {
        /// 试打的那张，去红后的牌种。
        Pai: Tile
        /// 打得出这一张的动作 id（这一包内，按合法动作集的顺序）。**可能不止一条**：
        /// 手里同时有 `5m` 与 `5mr` 时是两条手切，刚摸进那张与手里那张同种时是手切与摸切各一条。
        ActionIds: int list
        /// 打完之后的向听数。
        Shanten: Shanten
        /// 进退向：打完之后减打之前。0 为不变，+1 为退向（向听戻し）。
        ShantenDelta: int
        /// 打完之后的有效牌。可见张数越界（算不出来）时是 None——那是数据出了问题，
        /// **不是「没有有效牌」**：后者是一个空的 `Ukeire`。
        Ukeire: Ukeire option
    }

/// 决策包的脚手架数值（CONTEXT.md 的 ScaffoldTier）：引擎算得出、LLM 算不明白的那几个数。
///
/// **它是分析附件，不是观测**：`Observation` 只做遮蔽不做计算，脚手架从那份遮蔽好的观测
/// 再算一遍，因此它一张多余的牌也看不见（他家的暗牌在 `MaskedSeat` 里根本没有位置）。
///
/// **档位不在这里**：包恒带脚手架，`ScaffoldTier` 决定的是 prompt 渲不渲染它
/// （Bare 档的 prompt 不出现这些数）。25 号票的 Danger 往这个类型上加一个字段。
type Scaffold =
    {
        /// 现在这副手牌的向听数（含刚摸进那张）。
        Shanten: Shanten
        /// 现在这副手牌的有效牌。**只有等摸形（还没摸进）才有**——已摸进的手牌要先试打一张，
        /// 那些在 `Dahai` 里逐条给。
        Ukeire: Ukeire option
        /// 逐张试打，按合法动作集里第一次出现的顺序。这一手打不了牌（响应阶段）时是空的。
        Dahai: DahaiScaffold list
    }

/// 脚手架的计算与编码。**数值一律复用引擎既有实现**（`Shanten` / `Ukeire`），
/// 这里只负责「从哪副手牌、扣掉哪些可见牌、试打哪几张」。
[<RequireQualifiedAccess>]
module Scaffold =

    // ---- 计算 ----

    /// 手牌之外的可见牌：四家的河、全部副露里**自家亮出的**那几张、宝牌指示牌。
    ///
    /// 副露只数 `Naki.fromHand`：被鸣走的那张仍留在打牌者的河里（CONTEXT.md 的 Kawa），
    /// 连 `Naki.taken` 一起数就把同一张数了两遍，剩余枚数会凭空少一张。
    let private visible (observation: Observation) : Tile list =
        let kawa (entries: KawaEntry list) =
            entries |> List.map (fun entry -> entry.Pai)

        let others =
            observation.Others
            |> List.collect (fun other -> kawa other.Kawa @ List.collect Naki.fromHand other.Naki)

        kawa observation.Self.Kawa
        @ List.collect Naki.fromHand observation.Self.Naki
        @ others
        @ observation.DoraMarkers

    /// 有效牌；算不出来（手牌不是等摸形、可见张数越界）时是 None。
    let private tryUkeire (kindSet: TileKindSet) (seen: Tile list) (hand: HandShape) : Ukeire option =
        match Ukeire.calculate kindSet seen hand with
        | Ok ukeire -> Some ukeire
        | Error _ -> None

    /// 逐张试打。`dahai` 是「动作 id × 打出去的那张（已去红）」，按合法动作集的顺序。
    ///
    /// 试打之后那张牌**进了可见集**（它马上就要落到自己河里），否则它自己的剩余枚数会多算一张。
    let private trials
        (kindSet: TileKindSet)
        (seen: Tile list)
        (hand: HandShape)
        (current: Shanten)
        (dahai: (int * Tile) list)
        : DahaiScaffold list =
        dahai
        |> List.map snd
        |> List.distinct
        |> List.choose (fun pai ->
            // 打不出来的牌不该出现在合法动作集里；真出现了就跳过这一条，不编造数值。
            match HandShape.remove pai hand with
            | Error _ -> None
            | Ok after ->
                let shanten = Shanten.calculate kindSet after

                Some
                    {
                        Pai = pai
                        ActionIds = dahai |> List.filter (fun (_, tile) -> tile = pai) |> List.map fst
                        Shanten = shanten
                        ShantenDelta = Shanten.value shanten - Shanten.value current
                        Ukeire = tryUkeire kindSet (pai :: seen) after
                    })

    /// 某座位这一手的脚手架。`actions` 是**这一包里编好号的合法动作**，
    /// 逐张试打只试它给得出的那几张（立直之后打不了的牌不在里面）。
    ///
    /// 手牌形态读不出来时是 None：那时决策包的 `scaffold` 退回空对象，
    /// **不给半个数**（错的向听数比没有向听数更坏）。
    let calculate (kindSet: TileKindSet) (actions: (int * Action) list) (observation: Observation) : Scaffold option =
        let self = observation.Self

        match HandShape.create (List.length self.Naki) self.Hand with
        | Error _ -> None
        | Ok hand ->
            let seen = visible observation
            let current = Shanten.calculate kindSet hand

            let dahai =
                actions
                |> List.choose (fun (id, action) ->
                    match action with
                    | Action.Dahai(_, pai, _) -> Some(id, Tile.deaka pai)
                    | _ -> None)

            Some
                {
                    Shanten = current
                    Ukeire = tryUkeire kindSet seen hand
                    Dahai = trials kindSet seen hand current dahai
                }

    // ---- JSON（单向出口） ----

    /// 向听数：数值 + **已经渲染好的中文**。
    ///
    /// 带上 `display` 是 ADR-0005 的第 2 条：渲染层要中文时由决策包携带，**不让 TS 去查术语表**。
    /// 0 叫「听牌」、-1 叫「和了」，那是术语表的事不是格式化的事（`Shanten.toDisplay`）。
    /// 牌本身相反，上 wire 的是 mjai 记法：prompt 里的牌一律写记法，拼起来不需要术语表。
    let private shantenEncoder: Encoder<Shanten> =
        fun shanten ->
            Encode.object
                [
                    "value", Encode.int (Shanten.value shanten)
                    "display", Encode.string (Shanten.toDisplay shanten)
                ]

    /// 有效牌：总枚数、牌种数与「牌种 + 剩余枚数」。
    ///
    /// **枚数可能是 0**（该牌种已经全部可见）——它仍是形态上的有效牌，只是摸不到了。
    /// `kinds` 因此不是「数一数 `tiles` 有多长」那么简单：数不数 0 枚的那几种是规则问题
    /// （CONTEXT.md：有效牌为空不等于不听牌），因此由引擎的 `Ukeire.kindCount` 说了算，
    /// 不让渲染层自己挑一个口径。
    let private ukeireEncoder: Encoder<Ukeire> =
        fun ukeire ->
            Encode.object
                [
                    "total", Encode.int (Ukeire.total ukeire)
                    "kinds", Encode.int (Ukeire.kindCount ukeire)
                    "tiles",
                    ukeire.Tiles
                    |> List.map (fun (pai, remaining) ->
                        Encode.object [ "pai", Tile.encoder pai; "remaining", Encode.int remaining ])
                    |> Encode.list
                ]

    /// 「有就编，没有就写 null」。
    let private optional (encode: Encoder<'a>) : Encoder<'a option> =
        fun value ->
            match value with
            | Some item -> encode item
            | None -> Encode.nil

    /// 一条试打。`action_ids` 是它与动作列表的接头处——**渲染层认识 id，不认识牌**，
    /// 因此这里给出 id 而不是让它去按记法配对（`5m` 与 `5mr` 会配错）。
    let private dahaiEncoder: Encoder<DahaiScaffold> =
        fun trial ->
            Encode.object
                [
                    "pai", Tile.encoder trial.Pai
                    "action_ids", trial.ActionIds |> List.map Encode.int |> Encode.list
                    "shanten", shantenEncoder trial.Shanten
                    "shanten_delta", Encode.int trial.ShantenDelta
                    "ukeire", optional ukeireEncoder trial.Ukeire
                ]

    /// 脚手架的 wire 形态。**只有 encoder，没有 decoder**：它跟决策包一样只往外走。
    let encoder: Encoder<Scaffold> =
        fun scaffold ->
            Encode.object
                [
                    "shanten", shantenEncoder scaffold.Shanten
                    "ukeire", optional ukeireEncoder scaffold.Ukeire
                    "dahai", scaffold.Dahai |> List.map dahaiEncoder |> Encode.list
                ]

    /// 决策包上那个槽位：**算不出来就是空对象**，与 23 号票留下的形状一致。
    /// 读的人因此只有「有没有这几个字段」一种分法，不必再分「null 还是没有」。
    let slotEncoder: Encoder<Scaffold option> =
        fun value ->
            match value with
            | Some scaffold -> encoder scaffold
            | None -> Encode.object []
