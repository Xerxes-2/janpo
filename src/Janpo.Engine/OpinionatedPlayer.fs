namespace Janpo

/// 有主见的随机选手（票 42）：仍然是随机取样，只是先按**三条主见**把动作集收窄。
///
/// **为什么要它**：均匀随机的那个几乎走不到日麻的对抗面。27 号验收实测，
/// 1..2000 号种子里**只有 15 场出现过立直成立、3 场供托结转到下一局**——
/// 立直、一发、里宝牌、立直棒、振听的一大半在整场对局里根本不发生，
/// 代码有、用例有，但跑一整场碰不到。
///
/// **三条主见，够用就停**：
///
/// 1. **能和就和** —— 和了进了动作集就只提交它（不见逃）；
/// 2. **听牌就立直** —— 立直宣言进了动作集就只提交它；
/// 3. **有役才鸣** —— 吃、以及非役牌的碰 / 大明杠一条都不要。
///
/// **不做打点判断、不做危险度判断**：它的目的不是变强，是让规则路径真的发生
/// （放铳、追立直、被立直家碰走一张，这些都算「发生了」）。牌力评估是 M3 的事
/// （Mortal 基线），别把它往这里塞。
///
/// **它不是 `RandomPlayer.uniform` 的替代品**：那个是 40 条黄金用例与双目标对拍的基准，
/// 行为一个字节都不许动（票 42 的边界）。这里是**另一个**选手，另一份文件。
[<RequireQualifiedAccess>]
module OpinionatedPlayer =

    // ---- 主见 ----

    /// 役牌：三元牌（`5z`-`7z`）与这一家的场风 / 自风。
    ///
    /// **这是「鸣完还有役」的充分条件，不是完整判役**：鸣来的那一组本身就值一番，
    /// 因此鸣完这手牌不会没役。断幺九、混一色、混全带幺九那几个要看整手牌才算得出来，
    /// 那是打点判断——这个选手不做（票 42 的边界）。判据与 `Yaku` 里那份同义，
    /// 只是那份是私有的：这里算的是「要不要鸣」，那里算的是「成不成役」，两件事。
    let private isYakuhai (bakaze: Kaze) (jikaze: Kaze) (tile: Tile) : bool =
        let number = Tile.number tile

        Tile.suit tile = Jihai
        && (number >= 5 || number = Kaze.index bakaze + 1 || number = Kaze.index jikaze + 1)

    /// 它是和了吗（主见一）。
    let private isHora (action: Action) : bool =
        match action with
        | Action.Hora _ -> true
        | Action.Dahai _
        | Action.Pon _
        | Action.Chi _
        | Action.Ankan _
        | Action.Kakan _
        | Action.Minkan _
        | Action.Riichi _
        | Action.None _
        | Action.Ryuukyoku _ -> false

    /// 它是立直宣言吗（主见二）。
    let private isRiichi (action: Action) : bool =
        match action with
        | Action.Riichi _ -> true
        | Action.Dahai _
        | Action.Hora _
        | Action.Pon _
        | Action.Chi _
        | Action.Ankan _
        | Action.Kakan _
        | Action.Minkan _
        | Action.None _
        | Action.Ryuukyoku _ -> false

    /// 这一条鸣牌带不带役（主见三）。**吃恒不带**（顺子本身不成役），
    /// 碰与大明杠只看鸣的是不是役牌；暗杠与加杠不在其中——它们不破门清，
    /// 立直那条路仍然走得通。
    let private nakiHasYaku (bakaze: Kaze) (jikaze: Kaze) (action: Action) : bool =
        match action with
        | Action.Chi _ -> false
        | Action.Pon(_, _, pai, _)
        | Action.Minkan(_, _, pai, _) -> isYakuhai bakaze jikaze pai
        | Action.Dahai _
        | Action.Hora _
        | Action.Ankan _
        | Action.Kakan _
        | Action.Riichi _
        | Action.None _
        | Action.Ryuukyoku _ -> true

    /// 这个选手**愿意提交**的那几条动作：合法动作集按三条主见筛过之后剩下的。
    ///
    /// **恒是合法动作集的非空子集**：主见只收窄、不新造，也不许把动作集掏空
    /// （掏空了牌桌就推不动）。三条主见把它筛没了时原样返回——
    /// 响应阶段恒有「过」、摸牌之后恒有打牌，因此这一支实际走不到，留着是为了这条不变量
    /// 不依赖「引擎那边恰好总给得出一条」。
    ///
    /// **剩下的由取样定**（`RandomPlayer.ofBias`）：打哪张、要不要碰这组役牌、
    /// 要不要杠，主见一概不表态。
    let wanted (state: GameState) (choice: LegalActions) : Action list =
        // 和了排在立直前面：同一手里两条都在时（听牌自摸就和了）当然是和。
        // `orElseWith` 而不是先算两遍：和了在手时第二次 `tryFind` 不必跑。
        let decided =
            List.tryFind isHora choice.Actions
            |> Option.orElseWith (fun () -> List.tryFind isRiichi choice.Actions)

        match decided with
        | Some action -> [ action ]
        | None ->
            let context = GameState.context state
            let jikaze = Seat.jikaze (GameState.ruleset state) context.Oya choice.Seat

            match choice.Actions |> List.filter (nakiHasYaku context.Bakaze jikaze) with
            | [] -> choice.Actions
            | kept -> kept

    // ---- 取样 ----

    /// 主见管不到的那些动作的相对粗细。**和了与立直的权重不参与**：那两条由主见定，
    /// 到取样这一步动作集里只剩一条。其余几条的用意：
    ///
    /// - `Tenpai` 是这个选手唯一的「会不会听牌」旋钮，也是整票的命根子——
    ///   不挑牌就听不了牌，听不了牌就轮不到立直。给到 100 时打牌几乎总落在
    ///   「打完向听数最小」的那几张上（13 种打法里只有 1 种最小时仍有约 89% 落在它身上），
    ///   但仍留着一成的走岔，不至于退化成一个确定性的最短路机器人；
    /// - `Pon` 压过 `Pass`：能进这一步的碰已经是役牌了（非役牌的在主见那关就没了），
    ///   碰它是役牌手的正路；
    /// - `Chi` 恒不参与（吃在主见那关全被筛掉），写 1 只是不留空洞；
    /// - `Kan` 给得住手：暗杠 / 加杠开新宝牌指示牌，杠了还有岭上与里宝牌那条路；
    /// - `Ryuukyoku`（九种九牌）**故意不给权重**：宣不宣言是打点判断，这个选手不表态，
    ///   于是它照常把这一局打下去。跑批要九种九牌的覆盖率，那是 `PlayerBias.covering` 的活。
    let bias: PlayerBias =
        { PlayerBias.uniform with
            Kan = 8
            Pon = 30
            Pass = 20
            Tenpai = 100
        }

    /// 有主见的随机选手：三条主见收窄动作集，剩下的交给 `RandomPlayer` 按 `bias` 取样。
    /// 与 `RandomPlayer` 的两个预设同形（`Player<Rng>`），因此牌桌、CLI 与跑批换着用。
    let player: Player<Rng> =
        let sample = RandomPlayer.ofBias bias

        fun rng state choice ->
            sample
                rng
                state
                { choice with
                    Actions = wanted state choice
                }
