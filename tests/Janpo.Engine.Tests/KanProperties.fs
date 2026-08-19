namespace Janpo.Engine.Tests

open Xunit
open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 杠的不变量。**最值钱的三条是牌山那三条**：杠要从王牌补摸一张、同时把可摸区的最后一张
/// 补进王牌，因此「王牌恒 14 张、可摸区每杠少一张、总牌数守恒」三者必须同时成立——
/// 少一条就意味着有牌凭空多出来或者蒸发了。具名用例见 KanTests。
///
/// **抢杠那一支随机采样到不了**（判据 4：到不了的写出来，别留在旁白里）。
/// `GameStateArbitraries` 的全域扫描里，`AwaitingResponse` 且 `Cause = ResponseCause.Kan`
/// 的局面是 **0 个**（25.3 万个局面，票 96 / 97 / 98 三把尺子量出同一个数）：引擎只在
/// **真有人抢得了**时才进那个响应阶段（`GameState.fs` 的 `responsesTo`：空表就当场成立），
/// 而那张表里十三条轨迹没有一条造得出「加杠正撞上别家的和了牌且它不振听有役」。
///
/// 因此下面那条 `抢杠那一轮…` **属性的随机部分今天一件事也没守**：三个分支里
/// `ResponseCause.Dahai` 与 `AwaitingDahai` / `Ended` 都直接 `true`，唯一做事的那一支从没进去过
/// ——它实际上等于 `fun _ -> true`。**它的闸门是下面那条定点锚点**
/// （`摊好牌山的两条抢杠轨迹…`）与 `KanTests` 的具名用例，两处都每趟跑到。
///
/// **为什么不把抢杠轨迹挂进 `Traces`**（票 98 量过两件事，探针 `scripts/fsi/chankan-trace.fsx`）：
/// 一是买不到什么——那条轨迹只有九步、局面全是锁死的，给到权重 2 也只有一趟 47%
/// （要一趟必开口得占权重表的 40.5%），而锚点是 100%；二是挂上去当场把
/// **七条现成的属性按红**（两条轨迹合起来十条；下面那条锚点里排掉的 `杠数与新宝牌`
/// 只是其中一条），那是另一票的事。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries>; typeof<ScoreArbitraries> |], Parallelism = 8)>]
module KanProperties =

    let private engine = Ruleset.yonma

    /// 一局里成立过几个杠：`ankan` / `kakan` / `daiminkan` 三种事件的条数。
    /// 引擎的 `GameState.kanCount` 数的是副露，这里从事件流重新数一遍——两条路径对上才算数对。
    let private kanEvents (state: GameState) : int =
        GameState.events state
        |> List.filter (fun event ->
            match event with
            | Ankan _
            | Minkan _ -> true
            // 加杠不新增一组副露，它是把原来那组碰换成杠：数副露的那份也只算一个。
            | Kakan _ -> true
            | StartGame _
            | StartKyoku _
            | Tsumo _
            | Dahai _
            | Pon _
            | Chi _
            | Dora _
            | Riichi _
            | RiichiAccepted _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | EndGame -> false)
        |> List.length

    /// 一局里翻开的新宝牌条数。
    let private doraEvents (state: GameState) : int =
        GameState.events state
        |> List.filter (fun event ->
            match event with
            | Dora _ -> true
            | StartGame _
            | StartKyoku _
            | Tsumo _
            | Dahai _
            | Pon _
            | Chi _
            | Ankan _
            | Kakan _
            | Minkan _
            | Riichi _
            | RiichiAccepted _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | EndGame -> false)
        |> List.length

    /// 明杠欠着还没翻的新宝牌张数，**从事件流重新数一遍**：明杠欠一张，而欠的那张在
    /// **下一次杠成立时或下一次打牌之前**还清（票 59，28 局真实牌谱），暗杠当场就翻、一张不欠。
    /// 引擎自己那份记在 `GameState` 里，两条路径对上才算数对。
    ///
    /// **这个 fold 结构上只吐得出 0 与 1**，`pendingKanDora ≤ 1` 那条合取因此是恒真式，
    /// 这份属性也当不成「欠账恒不过 1 张」的执行体——生成器到不了连杠夹着欠账的局面
    /// （票 64 把还账故意弄坏实证：这里 9 条照样全绿）；真正守着它的是 KanTests 的
    /// 连杠具名用例与真牌谱对拍，两处都红过。
    ///
    /// 前提与 `kanEvents` 同一条：被抢的那个杠不算成立（那时事件数与 `kanCount` 就对不上了，
    /// 下面那条合取先报）。
    let private pendingKanDora (state: GameState) : int =
        (0, GameState.events state)
        ||> List.fold (fun pending event ->
            match event with
            // 明杠：前一杠欠的在这一刻还清，换成它自己欠一张。
            | Kakan _
            | Minkan _ -> 1
            // 暗杠当场翻，同时把前一杠欠的一并还清；打牌也把欠的那张翻出来。
            | Ankan _
            | Dahai _ -> 0
            | StartGame _
            | StartKyoku _
            | Tsumo _
            | Pon _
            | Chi _
            | Dora _
            | Riichi _
            | RiichiAccepted _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | EndGame -> pending)

    // ---- 牌山 ----

    [<Property>]
    let ``王牌恒是规则集给的那么多张：杠取走一张岭上牌，可摸区就补进一张`` (state: GameState) =
        List.length (Wall.deadWall (GameState.wall state)) = engine.DeadWallSize

    [<Property>]
    let ``可摸区每杠少一张：剩余张数恒等于「开局可摸张数 − 已摸 − 杠数」`` (state: GameState) =
        let wall = GameState.wall state

        let opening =
            Ruleset.wallSize engine - engine.DeadWallSize - Ruleset.haipaiTotal engine

        // 每条 `tsumo` 摸掉一张，但**岭上那几张来自王牌**：它们各自把可摸区的最后一张顶进王牌，
        // 因此可摸区少掉的总数 = 全部自摸条数（岭上的那几次摸的不是可摸区，却各顶走一张）。
        let tsumos =
            GameState.events state
            |> List.filter (fun event ->
                match event with
                | Tsumo _ -> true
                | StartGame _
                | StartKyoku _
                | Dahai _
                | Pon _
                | Chi _
                | Ankan _
                | Kakan _
                | Minkan _
                | Dora _
                | Riichi _
                | RiichiAccepted _
                | Hora _
                | Ryuukyoku _
                | EndKyoku
                | EndGame -> false)
            |> List.length

        Wall.remaining wall = opening - tsumos

    [<Property>]
    let ``杠数与新宝牌：每个杠翻一张，明杠那张欠到下一次杠或下一次打牌`` (state: GameState) =
        let kans = kanEvents state
        let wall = GameState.wall state

        kans = GameState.kanCount state
        // 已翻开的恒是「杠数 − 欠着的」，而欠着的只可能是 0 或 1（下一次杠也还清它）。
        // 打完牌欠账归零，因此一局打下来每个杠仍旧恰好对应一张指示牌。
        && pendingKanDora state <= 1
        && doraEvents state = kans - pendingKanDora state
        && List.length (Wall.doraIndicators wall) = 1 + doraEvents state
        // 里宝牌与表宝牌成叠翻开：杠里宝牌的张数与表的一样多。
        && List.length (Wall.uraIndicators wall) = List.length (Wall.doraIndicators wall)
        // 岭上牌有几张就最多杠几次。
        && kans <= engine.RinshanCount

    [<Property>]
    let ``杠后牌数守恒：一组杠仍旧只折三张暗牌`` (state: GameState) =
        // 杠比碰多吃一张手牌，但它随即从王牌补摸一张回来，因此「暗牌 + 3 × 副露数」不变。
        GameState.players state
        |> List.forall (fun player ->
            let held = List.length (PlayerState.hand player) + 3 * PlayerState.nakiCount player

            held = engine.HaipaiSize || held = engine.HaipaiSize + 1)

    [<Property>]
    let ``杠的副露里牌种唯一、四张齐全`` (state: GameState) =
        GameState.players state
        |> List.collect PlayerState.naki
        |> List.filter Naki.isKan
        |> List.forall (fun naki ->
            let tiles = Naki.tiles naki

            List.length tiles = 4
            && tiles |> List.map Tile.kindIndex |> List.distinct |> List.length = 1
            // 暗杠没有来源，明杠与加杠都记着来源座位（责任支付要它）。
            && (Naki.kind naki = NakiKind.Ankan) = Option.isNone (Naki.target naki))

    // ---- 动作集 ----

    [<Property>]
    let ``摸牌后阶段的杠只在自己摸完牌那一手出现，且杠数没到上限`` (state: GameState) =
        match GameState.phase state with
        | AwaitingDahai phase ->
            phase.Actions
            |> List.forall (fun action ->
                match action with
                | Action.Ankan(actor, consumed) ->
                    actor = phase.Actor
                    && Option.isSome phase.Tsumo
                    && List.length consumed = 4
                    && GameState.kanCount state < engine.RinshanCount
                | Action.Kakan(actor, _, consumed) ->
                    actor = phase.Actor
                    && Option.isSome phase.Tsumo
                    && List.length consumed = 3
                    && GameState.kanCount state < engine.RinshanCount
                // 大明杠是响应阶段的动作，摸牌后阶段不该有。
                | Action.Minkan _ -> false
                | Action.Dahai _
                | Action.Hora _
                | Action.Pon _
                | Action.Chi _
                | Action.Riichi _
                | Action.Ryuukyoku _
                | Action.None _ -> true)
        | AwaitingResponse _
        | Ended _ -> true

    /// **随机采样到不了它的非空分支**（取值域里 `ResponseCause.Kan` 的局面 0 个，见模块头）：
    /// 下面那条 `[<Fact>]` 锚点拿两条摊好牌山的抢杠轨迹每趟都把它跑一遍。
    [<Property>]
    let ``抢杠那一轮只有荣和与「过」，宣言杠的那家不在被问之列`` (state: GameState) =
        match GameState.phase state with
        | AwaitingResponse phase ->
            match phase.Cause with
            | ResponseCause.Dahai -> true
            | ResponseCause.Kan kan ->
                Naki.isKan kan
                && phase.Responses
                   |> List.forall (fun choice ->
                       choice.Seat <> phase.Target
                       && choice.Actions
                          |> List.forall (fun action ->
                              match action with
                              | Action.Hora(_, target, pai) -> target = phase.Target && pai = phase.Pai
                              | Action.None _ -> true
                              | Action.Dahai _
                              | Action.Pon _
                              | Action.Chi _
                              | Action.Ankan _
                              | Action.Kakan _
                              | Action.Minkan _
                              | Action.Ryuukyoku _
                              | Action.Riichi _ -> false))
        | AwaitingDahai _
        | Ended _ -> true

    // ---- 摊好牌山的抢杠锚点（采样到不了那一支，判据 1 / 3 / 4）----

    /// 抢杠那一轮的响应阶段（不是那一轮就是 `None`）。
    /// **不能拿「最后一条事件是杠」当判据**：宣言杠不改局面，而杠成立时引擎当场就接上
    /// `dora` / `tsumo`，两边的最后一条事件都不是杠。
    let private chankanPhaseOf (state: GameState) : AwaitingResponse option =
        match GameState.phase state with
        | AwaitingResponse phase ->
            match phase.Cause with
            | ResponseCause.Kan _ -> Some phase
            | ResponseCause.Dahai -> None
        | AwaitingDahai _
        | Ended _ -> None

    /// 两条**摊好牌山**的抢杠轨迹，两种成因各一条：
    ///
    /// - **加杠**（默认规则集）：座位 1 碰了 Oya 的 5s，一圈之后摸进第四张加杠，
    ///   座位 2 抢它（无役 ⇒ 之前那张 5s 它压根没被问，因此不振听；抢杠本身就是役）；
    /// - **暗杠**（雀魂规则集）：国士抢暗杠，天凤禁、雀魂允（`KokushiAnkanChankan`）。
    ///
    /// 后一条**取值域里永远不可能出现**（那张表只用默认规则集），只有锚点守得住它。
    let private scriptedChankanTraces =
        [
            "加杠抢杠（天凤）", NakiKind.Kakan, chankanTrace chankanScript
            "国士抢暗杠（雀魂）", NakiKind.Ankan, kokushiChankanTrace kokushiChankanScript
        ]

    /// 锚点逐步验的不变量：**跑的就是上面那几条随机属性的函数本身**，不另写一份
    /// （另写一份就会各自飘，而那正是判据 3 抱怨的「看着在守」）。
    ///
    /// **`杠数与新宝牌` 不在这张表里**，而且不是因为它不重要：它在抢杠局面上就是红的
    /// （它把 `Kakan` 事件当成立的杠数，而被抢的那个杠不成立，`GameState.kanCount` 是 0）——
    /// `pendingKanDora` 的注释自己写着这一条前提。把它排在外面是**显式记下这件事**（判据 4），
    /// 不是把它调松；它与另外七条在抢杠局面上的红写在票 98 的报告里，归另一票。
    let private chankanInvariants: (string * (GameState -> bool)) list =
        [
            "王牌张数", ``王牌恒是规则集给的那么多张：杠取走一张岭上牌，可摸区就补进一张``
            "可摸区张数", ``可摸区每杠少一张：剩余张数恒等于「开局可摸张数 − 已摸 − 杠数」``
            "杠后牌数守恒", ``杠后牌数守恒：一组杠仍旧只折三张暗牌``
            "杠的副露牌种唯一", ``杠的副露里牌种唯一、四张齐全``
            "杠只在摸完牌那一手", ``摸牌后阶段的杠只在自己摸完牌那一手出现，且杠数没到上限``
            "抢杠那一轮的形状", ``抢杠那一轮只有荣和与「过」，宣言杠的那家不在被问之列``
        ]

    /// **票 98 的定点锚点**：两条摊好牌山的抢杠轨迹逐步验上面那几条不变量，
    /// **并先自证抢杠那一轮真的到过**（两种成因各至少一个，且那一轮真在问荣和）——
    /// 少了这几句自证，锚点会悄悄退化成空转，而那正是这一票要治的毛病（判据 3）。
    [<Fact>]
    let ``摊好牌山的两条抢杠轨迹：抢杠那一轮真的到过，杠的不变量逐步都成立`` () =
        for label, kind, states in scriptedChankanTraces do
            let phases = states |> List.choose chankanPhaseOf

            match phases with
            | [] -> failwith $"{label} 这条轨迹里一个抢杠局面都没有：它已经退化成空转了"
            | _ ->
                for phase in phases do
                    match phase.Cause with
                    | ResponseCause.Kan kan -> Assert.Equal<NakiKind>(kind, Naki.kind kan)
                    | ResponseCause.Dahai -> failwith $"{label} 的抢杠局面竟然不是对杠的响应"

                    // 被问的那几家里真的有人抢得了：否则那条属性的内层 forall 仍旧是空转。
                    let asked =
                        phase.Responses
                        |> List.filter (fun choice ->
                            choice.Actions
                            |> List.exists (fun action ->
                                match action with
                                | Action.Hora _ -> true
                                | Action.Dahai _
                                | Action.Pon _
                                | Action.Chi _
                                | Action.Riichi _
                                | Action.Ankan _
                                | Action.Kakan _
                                | Action.Minkan _
                                | Action.Ryuukyoku _
                                | Action.None _ -> false))

                    Assert.True(not (List.isEmpty asked), $"{label} 的抢杠那一轮没有任何一家被问荣和：那一支仍是空转")

                states
                |> List.iteri (fun index state ->
                    for invariant, predicate in chankanInvariants do
                        Assert.True(predicate state, $"{label} 第 {index} 步破了「{invariant}」"))

    // ---- 责任支付 ----

    [<Property>]
    let ``包只改谁付、不改和了点：增减之和仍等于收走的供托`` (HoraCase(transfer, value)) (liable: int) =
        let seat = Seat.wrap engine liable
        let plain = Score.hora engine transfer value

        let packed = Score.hora engine { transfer with Sekinin = Some seat } value

        // 和了点、级别与符番一概不受包影响（`Fu` / `basePoints` / `limit` 都不看它）。
        packed.HoraPoints = plain.HoraPoints
        && packed.Limit = plain.Limit
        && List.sum packed.Deltas = transfer.Kyotaku * engine.RiichiBou
        && Seat.tryItem transfer.Actor packed.Deltas = Seat.tryItem transfer.Actor plain.Deltas
        // 付钱的只可能是责任者与放铳者（自摸时放铳者就是和了者自己，因此只剩责任者）。
        // 和了者包不了自己：那种情形当作没包，授受照常。
        && (seat = transfer.Actor
            || packed.Deltas
               |> Seat.indexed
               |> List.forall (fun (each, delta) ->
                   each = transfer.Actor || each = seat || each = transfer.Target || delta = 0))

    [<Property>]
    let ``包在自摸时由责任者一家付光`` (HoraCase(transfer, value)) (liable: int) =
        let seat = Seat.wrap engine liable

        if transfer.Actor <> transfer.Target || seat = transfer.Actor then
            true
        else
            let packed = Score.hora engine { transfer with Sekinin = Some seat } value

            // 自摸的三家平摊变成责任者一家付：其余两家分文不动。
            packed.Deltas
            |> Seat.indexed
            |> List.forall (fun (each, delta) -> each = transfer.Actor || each = seat || delta = 0)
            && (Seat.tryItem seat packed.Deltas
                |> Option.exists (fun delta -> -delta >= packed.HoraPoints))
