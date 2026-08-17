namespace Janpo

/// 随机选手的**偏好**：每一类动作在取样时的权重，外加一条打牌的偏好。
///
/// **为什么要偏好**：均匀取样的随机选手几乎走不到引擎最容易错的那一半。实测（14 票）：
/// 十场东风战 40 局里**和了 0 次、立直 0 次、暗杠 0 次**；把规模拉到 100 场 406 局也只有
/// 和了 2 次、立直 2 次（0.5%/局）。**差的是频率而不是可能性**，而跑批要的正是频率：
/// 和了、点数授受、立直、一发、里宝牌这些路径在均匀取样的跑批里几乎零覆盖。
/// 原因有两条：随机打牌几乎听不了牌，而就算听牌了，立直也只是十几条动作里的一条。
///
/// 权重是**相对**的，不是概率：一次取样把这一手的合法动作各按权重摊开再等概率取一个。
/// 因此「从完整合法动作集取样」这条性质不变——每个动作的权重都至少是 1
/// （`PlayerBias.weightOf` 兜了下界），偏好只改各条的相对粗细，不封任何一条。
type PlayerBias =
    {
        /// 和了。给得足够大就是「见和就和」。
        Hora: int
        /// 立直宣言。
        Riichi: int
        /// 三种杠（暗杠 / 加杠 / 大明杠）共用一个权重。
        Kan: int
        /// 碰。
        Pon: int
        /// 吃。
        Chi: int
        /// 九种九牌宣言（六种流局里唯一由选手宣言的那一种）。
        Ryuukyoku: int
        /// 响应阶段的「过」。它与碰 / 吃 / 大明杠的比值决定了鸣牌有多密。
        Pass: int
        /// 一条打牌的基础权重。
        Dahai: int
        /// **打完向听数最小**的那几张打牌，权重再乘这个倍数。
        ///
        /// 它是「会不会听牌」的唯一旋钮：1 表示不挑（打牌等概率），大于 1 表示
        /// 越偏向留听牌形。和了与立直的覆盖率全靠它——不挑牌的选手听不了牌，
        /// 也就永远轮不到和了与立直进合法动作集。
        ///
        /// **也是这个选手最贵的一步**：大于 1 时每一次打牌决策要对手里的每个牌种
        /// 各跑一次 `Shanten`（结构化的 14 张手牌单次约 150 µs，一手十几种就是毫秒级）。
        Tenpai: int
    }

/// 偏好的预设与取权重。
[<RequireQualifiedAccess>]
module PlayerBias =

    // ---- 预设 ----

    /// 均匀：全部权重相同、打牌不挑。**它与 `Kyoku.randomPlayer` 逐手一致**——
    /// 权重全 1 时按权重取样退化成按下标等概率取样，连消耗的随机数都一样。
    let uniform: PlayerBias =
        {
            Hora = 1
            Riichi = 1
            Kan = 1
            Pon = 1
            Chi = 1
            Ryuukyoku = 1
            Pass = 1
            Dahai = 1
            Tenpai = 1
        }

    /// 跑批（soak）用的覆盖型偏好：一批种子跑下来要走遍全部动作类型。
    ///
    /// 各条的数字都是实测调出来的（14 票，100 场东风战一组对比）：
    /// - `Hora` 压倒一切 → 见和就和，和了与点数授受这条路才有覆盖；
    /// - `Tenpai = 20` → 每 100 场约 164 次和了、174 次立直（均匀取样同规模是 2 与 2）；
    /// - `Pon` / `Chi` 压得比 `Pass` 低 → 鸣牌不至于把手牌全打散，否则门清没了、立直也就没了；
    /// - `Ryuukyoku`（九种九牌）给得高：它是唯一由选手宣言的流局形态，
    ///   而配牌里凑够九种幺九牌本就只有千分之四，不给高权重跑批规模内碰不到。
    let covering: PlayerBias =
        {
            Hora = 1000
            Riichi = 60
            Kan = 40
            Pon = 10
            Chi = 6
            Ryuukyoku = 500
            Pass = 20
            Dahai = 1
            Tenpai = 20
        }

    // ---- 取权重 ----

    /// 这个动作的权重。**下界是 1**：偏好只改相对粗细，不把任何一条合法动作封成 0
    /// （封成 0 就不是「从完整合法动作集取样」了）。打牌那一条还要再乘 `Tenpai`，
    /// 由 `RandomPlayer.ofBias` 接手——只有它手里才有向听数。
    let weightOf (bias: PlayerBias) (action: Action) : int =
        let weight =
            match action with
            | Action.Hora _ -> bias.Hora
            | Action.Riichi _ -> bias.Riichi
            | Action.Ankan _
            | Action.Kakan _
            | Action.Minkan _ -> bias.Kan
            | Action.Pon _ -> bias.Pon
            | Action.Chi _ -> bias.Chi
            | Action.Ryuukyoku _ -> bias.Ryuukyoku
            | Action.None _ -> bias.Pass
            | Action.Dahai _ -> bias.Dahai

        max 1 weight

/// 带偏好的随机选手（CONTEXT.md 的 Player）：从**完整**合法动作集里按权重取一个动作。
///
/// LLM 适配器、Mortal 与真人坐席都是 Agent 层的事，引擎侧只有这一个自带实现。
/// 它是纯函数：随机发生器经 `'player` 穿进穿出，因此「同一种子跑两次结果相同」在类型层面成立。
[<RequireQualifiedAccess>]
module RandomPlayer =

    // ---- 打牌的向听评分 ----

    /// 打出各牌种之后的向听数。手牌形态只建一次，逐个牌种试打
    /// （与 `RiichiState.tenpaiDahai` 同一条路子，只是要的是数值而不是「还听不听」）。
    ///
    /// 张数不成形态（还没摸牌的 13 张、或副露数对不上）时给空表，调用方于是不挑牌。
    let private shantenByKind (kindSet: TileKindSet) (nakiCount: int) (hand: Tile list) : (Tile * int) list =
        match HandShape.create nakiCount hand with
        | Error _ -> []
        // 等摸形的手牌试打之后张数不成形态（原来由 `HandShape.remove` 逐张拒掉）。
        | Ok shape when HandShape.isAwaitingDraw shape -> []
        | Ok shape ->
            // 与 `RiichiState.tenpaiDahai` 同一条路子：一批建一份缓冲，逐张试打共用。
            let scratch = ShantenScratch.create ()
            let counts = scratch.Dahai
            HandShape.countsInto counts shape
            let rest = HandShape.ofScratch nakiCount counts

            HandShape.tiles shape
            |> List.distinct
            |> List.map (fun kind ->
                let index = Tile.kindIndex kind
                counts.[index] <- counts.[index] - 1
                let shanten = Shanten.value (Shanten.calculateWith scratch kindSet rest)
                counts.[index] <- counts.[index] + 1
                kind, shanten)

    /// 这一手打得出去的牌种数。**一种以下就不必评分**：只剩一种打法时挑不挑都一样，
    /// 而立直成立之后每一手都是这个形状（只能摸切），跳过它省下的正是最贵的那一步。
    let private dahaiKinds (actions: Action list) : int =
        actions
        |> List.choose (fun action ->
            match action with
            | Action.Dahai(_, pai, _) -> Some(Tile.deaka pai)
            | Action.Hora _
            | Action.Pon _
            | Action.Chi _
            | Action.Ankan _
            | Action.Kakan _
            | Action.Minkan _
            | Action.Riichi _
            | Action.Ryuukyoku _
            | Action.None _ -> None)
        |> List.distinct
        |> List.length

    // ---- 取样 ----

    /// 按权重取一个动作：把各条按权重摊在一根数轴上，掷一次骰子看落在谁身上。
    /// 权重全 1 时它就是「按下标等概率取一个」，与 `Kyoku.randomPlayer` 逐手一致。
    let private pick (weighted: (Action * int) list) (rng: Rng) : Action * Rng =
        let total = weighted |> List.sumBy snd
        let roll, advanced = Rng.nextBelow total rng

        // 权重和恒大于 roll，因此走得到某一条；`[]` 那支是为了穷尽，取不到。
        let rec walk (passed: int) (rest: (Action * int) list) =
            match rest with
            | [] -> weighted |> List.map fst |> List.last
            | (action, weight) :: tail ->
                if passed + weight > roll then
                    action
                else
                    walk (passed + weight) tail

        walk 0 weighted, advanced

    /// 按给定偏好取样的选手。`Rng` 是它自己的状态，与牌山那一条流分开
    /// （同一条流会让选牌与洗牌相关）。
    let ofBias (bias: PlayerBias) : Player<Rng> =
        fun rng state choice ->
            let ruleset = GameState.ruleset state

            let scored =
                if bias.Tenpai <= 1 || dahaiKinds choice.Actions <= 1 then
                    []
                else
                    match GameState.player choice.Seat state with
                    | None -> []
                    | Some player ->
                        PlayerState.hand player
                        |> shantenByKind ruleset.TileKinds (PlayerState.nakiCount player)

            // 打完最接近听牌的那个向听数；没评分时是 None，于是各条打牌一样粗。
            let best =
                match scored with
                | [] -> None
                | _ -> scored |> List.map snd |> List.min |> Some

            let weightOf (action: Action) =
                match action, best with
                | Action.Dahai(_, pai, _), Some best ->
                    let keepsShanten =
                        scored
                        |> List.tryFind (fun (kind, _) -> kind = Tile.deaka pai)
                        |> Option.exists (fun (_, shanten) -> shanten = best)

                    if keepsShanten then
                        max 1 (bias.Dahai * bias.Tenpai)
                    else
                        PlayerBias.weightOf bias action
                | _ -> PlayerBias.weightOf bias action

            let weighted = choice.Actions |> List.map (fun action -> action, weightOf action)

            pick weighted rng

    /// 均匀取样的随机选手，`Kyoku.randomPlayer` 的另一种写法（逐手一致）。
    let uniform: Player<Rng> = ofBias PlayerBias.uniform

    /// 跑批用的覆盖型随机选手：会和了、会立直、会鸣牌、会杠、会宣言九种九牌。
    let covering: Player<Rng> = ofBias PlayerBias.covering
