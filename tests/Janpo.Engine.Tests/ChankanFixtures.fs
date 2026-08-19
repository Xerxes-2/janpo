namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 一局里**被抢掉的**那个杠：它宣言过，但从来没有成立。
///
/// 三处要认它：`GameStateProperties` 的手牌张数（宣言的那家收场时仍握着刚摸进的那张）、
/// `KanProperties` 的杠数（`GameState.kanCount` 不算它）、以及真牌谱那道回放闸门
/// （牌桌上不许出现它）。
type RobbedKan =
    {
        /// 宣言那个杠的座位。
        Declarer: Seat
        /// 成因：加杠或暗杠（大明杠是对打牌的响应，抢不了）。
        Kind: NakiKind
        /// 被抢的那一张：加杠是加上去的那张，暗杠是那四张里的一张。
        Pai: Tile
    }

/// **抢杠那个窗口**：某家宣言了暗杠 / 加杠、这个杠还没成立的那一段（`ResponseCause.Kan`）。
///
/// 引擎里它是唯一一段「事件已经播出去、局面还没动」的时间（`GameState.declareKan` 的
/// 「宣言不改局面，因此被抢时无需回滚」）。**好几族属性各自对这一段有一份判断**，
/// 而随机采样一次也到不了这里（`GameStateArbitraries` 的全域里这种局面是 **0 个**，
/// 票 96 / 97 / 98 三把尺子量出同一个数），因此那些判断一起错了很久也没人知道（票 98 §4）。
///
/// 这个模块把这一段的**词汇摊在一处**，各族属性的定点锚点都从这里取：
/// 「哪一步是抢杠那一轮」（`phaseOf`）、「哪个杠被抢掉了」（`robbedDeclarer`）、
/// 「三条摊好牌山的轨迹」（`traces`）与「把一组判据逐步喂给它们」（`sweep`）。
/// 票 98 时同一件事在两份探针与 `KanProperties` 里各写了一遍，那正是它们各自飘掉的路。
[<RequireQualifiedAccess>]
module ChankanFixtures =

    // ---- 这个窗口的词汇 ----

    /// 抢杠那一轮的响应阶段（不在那一轮时是 None）。
    ///
    /// **不能拿「最后一条事件是杠」当判据**：宣言杠不改局面，而杠成立时引擎当场就接上
    /// `dora` / `tsumo`，两边的最后一条事件都不是杠。
    let phaseOf (state: GameState) : AwaitingResponse option =
        match GameState.phase state with
        | AwaitingResponse phase ->
            match phase.Cause with
            | ResponseCause.Kan _ -> Some phase
            | ResponseCause.Dahai -> None
        | AwaitingDahai _
        | Ended _ -> None

    /// 这条事件流里**被抢掉的**那个杠（没有被抢的杠时是 None）。
    ///
    /// 判据在事件流上：一条 `ankan` / `kakan` 之后紧接着的是荣和（或三家抢同一个杠判成的
    /// 途中流局）时，引擎的 `applyKan` 压根没跑过——**那个杠没有发生**，副露仍是碰、
    /// 手里仍握着刚摸进的那张。其余每一条事件都意味着杠成立：成立那一刻引擎当场接上
    /// 翻宝牌与补摸岭上牌，因此宣言之后不可能什么都不发生。
    ///
    /// 一局至多一个：抢杠即收场（荣和或途中流局），后面不会再有第二个被抢的杠。
    let robbedKanIn (events: Event list) : RobbedKan option =
        ((None, None), events)
        ||> List.fold (fun (declared, robbed) event ->
            match event, declared with
            | Ankan(actor, consumed), _ ->
                consumed
                |> List.tryHead
                |> Option.map (fun pai ->
                    {
                        Declarer = actor
                        Kind = NakiKind.Ankan
                        Pai = pai
                    }),
                robbed
            | Kakan(actor, pai, _), _ ->
                Some
                    {
                        Declarer = actor
                        Kind = NakiKind.Kakan
                        Pai = pai
                    },
                robbed
            | Hora _, Some kan -> None, Some kan
            | Ryuukyoku fields, Some kan when fields.Reason = SanchaHora -> None, Some kan
            | _, _ -> None, robbed)
        |> snd

    /// 这一局里被抢掉的那个杠。
    let robbedKan (state: GameState) : RobbedKan option = GameState.events state |> robbedKanIn

    /// 这条事件流里去掉**没有成立的那个杠宣言**。两种情形，合起来就是抢杠那个窗口：
    ///
    /// - **被抢掉了**：宣言之后紧接着荣和（或三家和了的途中流局）；
    /// - **还挂在那一轮上**：宣言就是这条流的最后一条，响应还没收齐。
    ///
    /// **凡是从事件流重数「成立的杠」的地方都该先过一道它**：引擎的 `GameState.kanCount`
    /// 数的是副露，而宣言不改副露；它欠的那张宝牌指示牌也同理没翻（`revealPendingKanDora`
    /// 压根没跑过）。票 98 就是在这里逆了：`KanProperties.杠数与新宝牌` 把宣言当成了成立。
    let withoutUnestablishedKan (events: Event list) : Event list =
        let declaresKan (event: Event) =
            match event with
            | Ankan _
            | Kakan _ -> true
            | _ -> false

        let robsKan (event: Event) =
            match event with
            | Hora _ -> true
            | Ryuukyoku fields -> fields.Reason = SanchaHora
            | _ -> false

        let rec loop (rest: Event list) (kept: Event list) =
            match rest with
            // 宣言之后紧接着的那一条把它抢了：丢掉宣言，留下抢它的那一条。
            | declaration :: next :: tail when declaresKan declaration && robsKan next -> loop tail (next :: kept)
            // 宣言就是最后一条：响应还没收齐，那个杠今天还没成立。
            | [ declaration ] when declaresKan declaration -> List.rev kept
            | event :: tail -> loop tail (event :: kept)
            | [] -> List.rev kept

        loop events []

    // ---- 三条摊好牌山的轨迹 ----

    /// 名字、成因、逐步的全部局面。**取值域到不了它们中的任何一条**，因此它们只挂在
    /// 各族的定点锚点上（票 98 量过挂进 `Traces` 的账：权重 2 只买到一趟 47%，而锚点是 100%）：
    ///
    /// - **加杠**（默认规则集）：座位 1 碰了 Oya 的 5s，一圈之后摸进第四张加杠，
    ///   座位 2 抢它（无役 ⇒ 之前那张 5s 它压根没被问，因此不振听；抢杠本身就是役）；
    /// - **暗杠**（雀魂规则集）：国士抢暗杠，天凤禁、雀魂允（`KokushiAnkanChankan`）——
    ///   **这一条取值域里永远不可能出现**，那张表只用默认规则集；
    /// - **加杠、抢的那家先立直**：同一座牌山换个选手，抢杠时和的是「立直 + 一发 + 抢杠」——
    ///   「杠宣言了、还没成立」与「一发还亮着」同时在场的唯一一条轨迹（票 99）。
    let traces: (string * NakiKind * GameState list) list =
        [
            "加杠抢杠（天凤）", NakiKind.Kakan, chankanTrace chankanScript
            "国士抢暗杠（雀魂）", NakiKind.Ankan, kokushiChankanTrace kokushiChankanScript
            "加杠抢杠、抢的那家先立直（天凤）", NakiKind.Kakan, chankanRiichiTrace chankanScript
        ]

    /// 只有加杠那两条。**给「掩蔽流里不出现他家暗牌」用**：国士抢暗杠时那四张牌必须亮给
    /// 别家看（不然没法决定抢不抢），而它们在引擎里仍是暗牌——那条不变量在暗杠这个窗口里
    /// **本来就不成立**。这是术语问题（`CONTEXT.md` 的 `MaskedEvent` / `Naki` 要认「宣言中的
    /// 暗杠」是公开信息），不是断言强度问题，因此这里**显式把那条轨迹排在外面**（判据 4），
    /// 而不是把断言调松。建议写在报告 `99-chankan-window-observation.md` 的术语那一节。
    let kakanTraces: (string * NakiKind * GameState list) list =
        traces |> List.filter (fun (_, kind, _) -> kind = NakiKind.Kakan)

    // ---- 逐步扫一遍 ----

    /// 这一条轨迹真的到过抢杠那一轮吗——**先自证覆盖再验不变量**：成因对得上、
    /// 那一轮真的有人被问荣和。少了这几句，锚点会悄悄退化成空转，而那正是判据 3 抱怨的
    /// 「看着在守」（票 98 §5.2 把牌山换成一座没人抢得了的，锚点当场报「退化成空转」）。
    let private assertReaches (label: string) (kind: NakiKind) (states: GameState list) : unit =
        let phases = states |> List.choose phaseOf

        if List.isEmpty phases then
            failwith $"{label} 这条轨迹里一个抢杠局面都没有：它已经退化成空转了"

        for phase in phases do
            match phase.Cause with
            | ResponseCause.Kan kan -> Assert.Equal<NakiKind>(kind, Naki.kind kan)
            | ResponseCause.Dahai -> failwith $"{label} 的抢杠局面竟然不是对杠的响应"

            // 被问的那几家里真的有人抢得了：否则内层的 forall 仍旧是空转。
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

    /// 把一组判据喂给这几条轨迹的**每一步**。喂进来的应当是那几条随机属性的函数**本身**，
    /// 不是另写一份——另写一份就会各自飘，而那正是判据 3 抱怨的毛病。
    ///
    /// **破的那几步一次报完**，不是第一步就停：这一族的红往往一破就是一串
    /// （宣言那一步与终局那一步各一条），只报头一条会把「抢完之后那份从没回滚」盖掉。
    let sweep (traces: (string * NakiKind * GameState list) list) (invariants: (string * (GameState -> bool)) list) =
        for label, kind, states in traces do
            assertReaches label kind states

        let broken =
            [
                for label, _, states in traces do
                    for index, state in List.indexed states do
                        for invariant, predicate in invariants do
                            if not (predicate state) then
                                yield $"{label} 第 {index} 步破了「{invariant}」"
            ]

        // 逐条报而不是 `Assert.Equal<string list>`：xunit 把集合截到前五项，而这一族的红
        // 恰恰是「宣言那一步 + 终局那一步 × 三条轨迹」这么一串，截掉就看不出形状了。
        Assert.True(List.isEmpty broken, broken |> String.concat "\n")
