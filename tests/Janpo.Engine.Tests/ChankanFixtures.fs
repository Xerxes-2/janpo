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
/// 「摊好牌山的那几条轨迹」（`traces`）、「宣言中、还没结局的那一刻」（`declarationWindows`）
/// 与「把一组判据逐步喂给它们」（`sweep`）。
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

    /// 这条流里去掉**没有成立的那个杠宣言**。两种情形，合起来就是抢杠那个窗口：
    ///
    /// - **被抢掉了**：宣言之后紧接着荣和（或三家和了的途中流局）；
    /// - **还挂在那一轮上**：宣言就是这条流的最后一条，响应还没收齐。
    ///
    /// 两种情形合起来就是 `CONTEXT.md` 的 `Ankan Declaration` 那句「宣言的结局由**下一条事件**
    /// 宣告」：荣和（或三家抢同一杠判成的途中流局）= 原地作废，其余每一条都意味着杠成立了。
    ///
    /// `asEvent` 是「这一项是哪条 mjai 事件」：事件流那一侧是它自己，掩蔽流那一侧是
    /// `MaskedEvent.publicEvent`——宣言与宣告它结局的那几条都是 `MaskedEvent.Public`，
    /// **两侧因此共用同一份判据**，不会各自飘掉。
    let private withoutUnestablishedKanBy (asEvent: 'item -> Event option) (items: 'item list) : 'item list =
        let declaresKan (item: 'item) =
            match asEvent item with
            | Some(Ankan _)
            | Some(Kakan _) -> true
            | _ -> false

        let robsKan (item: 'item) =
            match asEvent item with
            | Some(Hora _) -> true
            | Some(Ryuukyoku fields) -> fields.Reason = SanchaHora
            | _ -> false

        let rec loop (rest: 'item list) (kept: 'item list) =
            match rest with
            // 宣言之后紧接着的那一条把它抢了：丢掉宣言，留下抢它的那一条。
            | declaration :: next :: tail when declaresKan declaration && robsKan next -> loop tail (next :: kept)
            // 宣言就是最后一条：响应还没收齐，那个杠今天还没成立。
            | [ declaration ] when declaresKan declaration -> List.rev kept
            | event :: tail -> loop tail (event :: kept)
            | [] -> List.rev kept

        loop items []

    /// 事件流那一侧。
    ///
    /// **凡是从事件流重数「成立的杠」的地方都该先过一道它**：引擎的 `GameState.kanCount`
    /// 数的是副露，而宣言不改副露；它欠的那张宝牌指示牌也同理没翻（`revealPendingKanDora`
    /// 压根没跑过）。票 98 就是在这里逆了：`KanProperties.杠数与新宝牌` 把宣言当成了成立。
    let withoutUnestablishedKan (events: Event list) : Event list = withoutUnestablishedKanBy Some events

    /// 掩蔽流那一侧（票 100）。
    ///
    /// **「掩蔽流里不出现他家暗牌」那条不变量的定义域不含宣言中的那几张**
    /// （`CONTEXT.md` 的 `Ankan Declaration`）：暗杠与加杠加上去的那张一经宣言就是公开信息（不亮
    /// 别家就无从决定抢不抢），而引擎那侧宣言不改局面——那几张仍在宣言者手里。
    /// 判掩蔽流漏没漏之前先过一道它，那一段就落在定义域外。
    ///
    /// **摘掉的是那一条事件，不是那几种记法**：宣言一个 7z 的暗杠不会让别处漏出来的 7z
    /// 变得合法（`MaskedStreamProperties` 那条阴性对照验的就是这件事）。
    let maskedWithoutUnestablishedKan (stream: MaskedEvent list) : MaskedEvent list =
        withoutUnestablishedKanBy MaskedEvent.publicEvent stream

    // ---- 四条摊好牌山的轨迹 ----

    /// 名字、成因、逐步的全部局面。**取值域到不了它们中的任何一条**，因此它们只挂在
    /// 各族的定点锚点上（票 98 量过挂进 `Traces` 的账：权重 2 只买到一趟 47%，而锚点是 100%）：
    ///
    /// - **加杠**（默认规则集）：座位 1 碰了 Oya 的 5s，一圈之后摸进第四张加杠，
    ///   座位 2 抢它（无役 ⇒ 之前那张 5s 它压根没被问，因此不振听；抢杠本身就是役）；
    /// - **暗杠**（雀魂规则集）：国士抢暗杠，天凤禁、雀魂允（`KokushiAnkanChankan`）——
    ///   **这一条取值域里永远不可能出现**，那张表只用默认规则集；
    /// - **加杠、抢的那家先立直**：同一座牌山换个选手，抢杠时和的是「立直 + 一发 + 抢杠」——
    ///   「杠宣言了、还没成立」与「一发还亮着」同时在场的唯一一条轨迹（票 99）；
    /// - **加杠、加上去的那张是红宝牌**：碰的是三张正五、加的是 `5sr`（票 100）——
    ///   宣言那一刻掩蔽流里多出一个底下那组碰里没有的记法，而引擎认为它仍在手里。
    ///   另外两条加杠轨迹与真牌谱那两局**碰巧都不踩**它（票 99 报告 §5.4）。
    ///
    /// **四条全挂在同一张表上**：票 99 时暗杠那一条被「掩蔽流里不出现他家暗牌」显式排在外面
    /// （`kakanTraces`），因为那条不变量在暗杠窗口里看着不成立；术语裁完之后它收了回来
    /// （`Ankan Declaration`：那不是例外，是那条不变量本来就不管的一段）。
    let traces: (string * NakiKind * GameState list) list =
        [
            "加杠抢杠（天凤）", NakiKind.Kakan, chankanTrace chankanScript
            "国士抢暗杠（雀魂）", NakiKind.Ankan, kokushiChankanTrace kokushiChankanScript
            "加杠抢杠、抢的那家先立直（天凤）", NakiKind.Kakan, chankanRiichiTrace chankanScript
            "加杠抢杠、加的那张是红宝牌（天凤）", NakiKind.Kakan, chankanTrace chankanAkadoraScript
        ]

    /// 每条轨迹上**宣言中、还没结局**的那一步：名字、那一轮的响应阶段、那个局面。
    /// 一条轨迹恰好一步（抢杠即收场）。
    ///
    /// **阴性对照要停在这一刻上量**（判据 20）：走完一整局再抓，量的就是另一件事了——
    /// 到那时那个杠要么成立了（那几张进了副露，谁都看得见），要么被抢了
    /// （被抢的那张写在 `hora` 事件上，同样公开）。**两种结局都会把对照变成空转。**
    let declarationWindows
        (traces: (string * NakiKind * GameState list) list)
        : (string * AwaitingResponse * GameState) list =
        [
            for label, _, states in traces do
                for state in states do
                    match phaseOf state with
                    | Some phase -> yield label, phase, state
                    | None -> ()
        ]

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
        // 恰恰是「宣言那一步 + 终局那一步 × 每一条轨迹」这么一串，截掉就看不出形状了。
        Assert.True(List.isEmpty broken, broken |> String.concat "\n")
