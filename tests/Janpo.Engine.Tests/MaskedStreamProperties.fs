namespace Janpo.Engine.Tests

open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 掩蔽事件流的固件：**见逃し密集**的那种轨迹。
///
/// 既有的几个选手都是「见和就和」，因此见逃（同巡振听、立直后见逃的永久振听）在它们的
/// 轨迹里几乎不出现——而那恰恰是「座席看得见的历史」与「引擎知道的事实」最容易分家的地方。
module MinogashiFixtures =

    /// 响应阶段一律「过」，其余照 `riichiSeeking`（见立直就立、自摸和照和）。
    ///
    /// **「过」的判据是动作集里有没有 `Action.None`**：只有响应阶段才有它（DECISIONS 20-6）。
    let minogashiSeeking: Player<Rng> =
        fun rng state choice ->
            let responding =
                choice.Actions
                |> List.exists (fun action ->
                    match action with
                    | Action.None _ -> true
                    | Action.Dahai _
                    | Action.Hora _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.Ryuukyoku _ -> false)

            if responding then
                Action.None choice.Seat, rng
            else
                riichiSeeking rng state choice

    /// 一局见逃密集的全部局面。
    let trace (seed: int) : GameState list =
        let state, rng = start seed
        traceFrom minogashiSeeking rng state

    /// 这一批轨迹里真的出现过同巡振听吗——**没断言过覆盖率的属性只证明了没崩**（备注 N-8）。
    let doujunFuritenCount (states: GameState list) : int =
        states
        |> List.sumBy (fun state ->
            GameState.players state
            |> List.filter (fun player -> (PlayerState.furiten player).Doujun)
            |> List.length)

/// 见逃密集的可达局面。与 `GameStateArbitraries` 分开：那一份是全项目共用的取样，
/// 这里只想要「有人放过了能荣和的牌」这一小片局面。
type MinogashiArbitraries =

    static member GameState() : Arbitrary<GameState> =
        gen {
            let! seed = Gen.choose (1, 120)
            let states = MinogashiFixtures.trace seed
            let! index = Gen.choose (0, List.length states - 1)
            return List.item index states
        }
        |> Arb.fromGen

/// 一局逐手推进的**来路**：怎么开局、谁在打、看的是哪个座位。
///
/// **属性的参数是它而不是某个局面**：三方闸门要把一整局从头走一遍，随机取一个中途局面不够；
/// 而报错时这三样就够原地重跑出同一手。
type KyokuRun =
    {
        /// 开局的名字，认得的那几个在 `SeatStreamGate.openings`。
        Opening: string
        /// 牌山与选手的种子。**摊好剧本的那几局不看它**（牌山是写死的）。
        Seed: int
        /// 看的那个座位。
        Viewer: Seat
    }

    override this.ToString() =
        $"{this.Opening} / 种子 {this.Seed} / 座位 {Seat.index this.Viewer}"

/// **「座席的历史与观测同出一源」这条 M1 核心不变量的闸门**（票 60）。
///
/// 原来守它的那条属性比的是 `List.fold advance` 与 `Observation.ofEvents`——
/// 后者展开就是前者，**恒真式**：弄坏 `SeatStream.absorb`、`SeatStream.advance`
/// 或 `MaskedEvent.forSeat` 它一次都不红，唯一红得了的是 `advanceAll` 那两行包装
/// （四份原始输出在 `run/reports/60-tautological-gate.md` §1）。
///
/// 现在是**三条腿**，每条腿的两侧不同源：
///
/// - **A 增量 vs 一次性**：左边 `start` → 逐条 `advance`，**只吃 `GameState.step` 吐出来的
///   那几条**；右边每一手重新调 `Observation.ofState`。两侧吃的不是同一份事件表。
/// - **B 增量 vs 引擎状态**：左边那份观测与引擎的 `GameState` 逐字段对。
/// - **C 一次性 vs 引擎状态**：右边那份观测与引擎的 `GameState` 逐字段对。
///
/// B 与 C 的右侧**既不经过掩蔽也不经过 fold**，因此三条腿归并不到同一个 fold 上去。
/// 三条分开报也是故意的：只有 A 红而 B/C 绿时，错的是「接事件」那一段而不是 fold。
[<RequireQualifiedAccess>]
module SeatStreamGate =

    /// 一处分歧：第几手、哪条腿、点名的字段。
    type Divergence =
        {
            Turn: int
            Leg: string
            Field: string
        }

    let private incrementalVsOneShot = "A 增量 vs 一次性"

    let private incrementalVsEngine = "B 增量 vs 引擎状态"

    let private oneShotVsEngine = "C 一次性 vs 引擎状态"

    /// 跑哪几局，带权重。**名单与权重照抄 `GameStateArbitraries.tracesFor`**：
    /// 那份名单挑的就是「哪几种局面到得了」，这里要的覆盖面与它一样。
    /// 见逃密集那一条不在这里（它自己一条属性，取样也另一批种子）。
    let openings: (int * string) list =
        [
            4, "random"
            4, "tenpai"
            4, "naki"
            4, "kan"
            4, "riichi"
            // 摊好的三局：一局三个暗杠、一局大明杠后岭上开花、一局暗杠后岭上开花。
            // 随机取样里杠太稀，杠那一路非得有它们不可。
            2, "three-kan"
            1, "minkan"
            1, "ankan"
            // 摊好的两局：一局自摸和收尾、一局双响收尾。
            1, "tsumo-hora"
            1, "double-ron"
        ]

    /// 名字 → 开局的局面、选手自己的随机源、选手。
    let private openingOf (name: string) (seed: int) : GameState * Rng * Player<Rng> =
        let fromSeed (player: Player<Rng>) =
            let state, rng = start seed
            state, rng, player

        let scripted (player: Player<Rng>) (state: GameState) = state, Rng.ofSeed 1, player

        match name with
        | "random" -> fromSeed Kyoku.randomPlayer
        | "tenpai" -> fromSeed tenpaiSeeking
        | "naki" -> fromSeed nakiSeeking
        | "kan" -> fromSeed kanSeeking
        | "riichi" -> fromSeed riichiSeeking
        | "minogashi" -> fromSeed MinogashiFixtures.minogashiSeeking
        | "three-kan" -> startScriptedRinshan "2m 3m 7z" threeKanScript |> scripted kanSeeking
        | "minkan" -> startScriptedRinshan "1z" minkanScript |> scripted kanSeeking
        | "ankan" -> startScriptedRinshan "5z" ankanScript |> scripted kanSeeking
        | "tsumo-hora" -> startScripted tsumoHoraScript |> scripted horaSeeking
        | "double-ron" -> startScripted doubleRonScript |> scripted horaSeeking
        | other -> failwith $"没有叫「{other}」的开局"

    /// 一整局逐手推进，收集三条腿上的全部分歧（空表就是处处一致）。
    ///
    /// **增量那一侧一次都不回头看 `GameState.events`**：开局那几条先逐条吃进去
    /// （牌桌的 `Table.start` 就是这么起的），之后每一手只吃 `GameState.step`
    /// 吐出来的那几条（`Table.played` 走的就是这条路）。一次性那一侧每一手重新调
    /// `Observation.ofState`。两侧吃的不是同一份事件表，因此 A 那条腿不是恒真式。
    let divergences (run: KyokuRun) : Divergence list =
        let opening, rng, player = openingOf run.Opening run.Seed
        let seat = run.Viewer

        let anchored (turn: int) (leg: string) (state: GameState) (observation: Observation option) =
            match observation with
            | None ->
                [
                    {
                        Turn = turn
                        Leg = leg
                        Field = "观测缺席"
                    }
                ]
            | Some each ->
                ObservationFixtures.mismatches seat state each
                |> List.map (fun field ->
                    {
                        Turn = turn
                        Leg = leg
                        Field = field
                    })

        let at (turn: int) (state: GameState) (stream: SeatStream) =
            let incremental = SeatStream.observation stream
            let oneShot = Observation.ofState seat state

            let sameObservation =
                if incremental = oneShot then
                    []
                else
                    [
                        {
                            Turn = turn
                            Leg = incrementalVsOneShot
                            Field = "整份观测"
                        }
                    ]

            sameObservation
            @ anchored turn incrementalVsEngine state incremental
            @ anchored turn oneShotVsEngine state oneShot

        // 开局那几条事件先逐条吃进去（牌桌的 `Table.start` 就是这么起的）。
        let seeded =
            (SeatStream.start ruleset seat, GameState.events opening)
            ||> List.fold (fun stream event -> SeatStream.advance event stream)

        let rec loop (turn: int) (rng: Rng) (state: GameState) (stream: SeatStream) (found: Divergence list) =
            let sofar = found @ at turn state stream

            match GameState.legalActions state with
            | [] -> sofar
            | choice :: _ ->
                let action, advanced = player rng state choice

                match GameState.step state action with
                | Error illegal -> failwith $"合法动作集里的动作应当被接受，却得到「{IllegalAction.toDisplay illegal}」"
                | Ok(next, produced) ->
                    let carried =
                        (stream, produced)
                        ||> List.fold (fun current event -> SeatStream.advance event current)

                    loop (turn + 1) advanced next carried sofar

        loop 0 rng opening seeded []

    /// 报错那一行：点名第几手、哪条腿、哪个字段。只报头几条——
    /// 一处对不上之后往后每一手都跟着对不上，全抄出来读不动。
    let toDisplay (run: KyokuRun) (found: Divergence list) : string =
        let head =
            found
            |> List.truncate 5
            |> List.map (fun each -> $"第 {each.Turn} 手 {each.Leg}：{each.Field}")
            |> String.concat "；"

        $"{run}：共 {List.length found} 处分歧，头几处是 {head}"

/// 三方闸门的取样：**随机对局 × 随机座位**（每一手在闸门自己里走完）。
type SeatStreamRunArbitraries =

    static member KyokuRun() : Arbitrary<KyokuRun> =
        gen {
            let! seed = Gen.choose (1, 400)

            // **这里 `Gen.constant` 是对的**（与 `GameStateArbitraries.tracesFor` 的 `Gen.fresh` 不同）：
            // 载荷只是一个名字，跑那一局的钱在 `SeatStreamGate.divergences` 里付，取样时一分不花。
            let! opening =
                SeatStreamGate.openings
                |> List.map (fun (weight, name) -> weight, Gen.constant name)
                |> Gen.frequency

            let! viewer = Gen.elements (Seat.all ruleset)

            return
                {
                    Opening = opening
                    Seed = seed
                    Viewer = viewer
                }
        }
        |> Arb.fromGen

/// 见逃密集那一批的取样：同巡振听与立直后见逃的永久振听只在这条轨迹上出现。
type MinogashiRunArbitraries =

    static member KyokuRun() : Arbitrary<KyokuRun> =
        gen {
            let! seed = Gen.choose (1, 120)
            let! viewer = Gen.elements (Seat.all ruleset)

            return
                {
                    Opening = "minogashi"
                    Seed = seed
                    Viewer = viewer
                }
        }
        |> Arb.fromGen

/// 掩蔽事件流的不变量（票 29a）。
///
/// **掩蔽只定义在事件上**：`MaskedEvent.forSeat` 是全项目唯一的一条掩蔽法则，
/// `Observation` 是它 fold 出来的结果。这里守三件事：不泄露、fold 与增量维护一致、
/// 以及两条规则蕴含的时序不变量。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |], Parallelism = 4)>]
module MaskedStreamProperties =

    /// 这一刻这个座位**看得见**的每一张牌的记法：自家暗牌（含刚摸进那张）、四家的河、
    /// 四家的副露、已翻开的表宝牌指示牌，以及和了时翻开的里宝牌。
    ///
    /// 牌山、王牌与他家暗牌都不在其中——掩蔽流里冒出这些之外的记法就是泄露。
    let private visibleTo (seat: Seat) (state: GameState) : Set<string> =
        let own =
            match GameState.player seat state with
            | Some player -> PlayerState.hand player @ Option.toList (PlayerState.drawn player)
            | None -> []

        let onTable =
            GameState.players state
            |> List.collect (fun player ->
                PlayerState.kawa player @ (PlayerState.naki player |> List.collect Naki.tiles))

        // 和了那一刻公开的两样：和了牌本身（自摸时它还在和了者手里），
        // 以及立直和了才翻的里宝牌指示牌。两者都写在 `hora` 事件上，牌桌上也都摆着。
        let horas =
            GameState.horas state
            |> List.collect (fun hora -> hora.Pai :: hora.UraDoraMarkers)

        own @ onTable @ Wall.doraIndicators (GameState.wall state) @ horas
        |> List.map Tile.toMjai
        |> Set.ofList

    /// 一条掩蔽流里出现的全部记法。
    let private notationsIn (stream: MaskedEvent list) : Set<string> =
        stream |> List.collect MaskedEvent.tiles |> List.map Tile.toMjai |> Set.ofList

    /// 这条掩蔽流**泄露给这个座位**的记法：流里出现、而它此刻看不见的那几个（空集就是没漏）。
    ///
    /// **定义域不含宣言中的那个杠**（`CONTEXT.md` 的 `Ankan Declaration`，票 100）：
    /// 暗杠与加杠加上去的那张一经宣言就是公开信息，而引擎那侧宣言不改局面——那几张仍在宣言者
    /// 手里。两边都对，差的是词：那一段**本来就不在这条不变量管的范围里**，因此比之前先过一道
    /// `ChankanFixtures.maskedWithoutUnestablishedKan`。
    ///
    /// **这是改定义域，不是调松**，两件事各自有证据：
    ///
    /// - 改完之后它守的局面**比从前多一种**——国士抢暗杠那条轨迹票 99 时被显式排在锚点外，
    ///   现在收了回来（`ChankanFixtures.traces` 四条全扫）；
    /// - 摘掉的是**那一条事件**而不是那几种记法：宣言窗口之外的他家暗牌照旧抓得住
    ///   （下面那条阴性对照）。
    let private leaksTo (seat: Seat) (state: GameState) (stream: MaskedEvent list) : Set<string> =
        let disclosed =
            stream |> ChankanFixtures.maskedWithoutUnestablishedKan |> notationsIn

        Set.difference disclosed (visibleTo seat state)

    [<Property>]
    let ``任意局面任意座位，掩蔽流里不出现他家暗牌中的任何一张`` (state: GameState) =
        Seat.all ruleset
        |> List.forall (fun seat -> Observation.stream seat state |> leaksTo seat state |> Set.isEmpty)

    /// 掩蔽流里**他家摸的那张一律没有牌面**，自家摸的一律有。
    [<Property>]
    let ``任意局面任意座位，只有自家那几条摸牌带着牌面`` (state: GameState) =
        Seat.all ruleset
        |> List.forall (fun seat ->
            Observation.stream seat state
            |> List.forall (fun masked ->
                match masked with
                | MaskedEvent.Tsumo(actor, pai) -> Option.isSome pai = (actor = seat)
                | MaskedEvent.StartKyoku _
                | MaskedEvent.Public _ -> true))

    /// 掩蔽流与未掩蔽流**逐条对齐**：掩蔽只丢字段，不丢事件、不改顺序、不添事件。
    /// 上帝视角那条流就是未掩蔽的这一条（围观与 M2 复盘读它）。
    [<Property>]
    let ``任意局面任意座位，掩蔽流与上帝视角那条流一样长且逐条对齐`` (state: GameState) =
        let god = GodView.stream state

        Seat.all ruleset
        |> List.forall (fun seat ->
            let masked = Observation.stream seat state

            List.length masked = List.length god
            && List.forall2
                (fun (each: MaskedEvent) (event: Event) ->
                    // 逐条对齐：`Public` 那几条原样，两条被掩蔽的与原事件同种。
                    match each, event with
                    | MaskedEvent.Public kept, _ -> kept = event
                    | MaskedEvent.StartKyoku _, StartKyoku _ -> true
                    | MaskedEvent.Tsumo(actor, _), Tsumo(other, _) -> actor = other
                    | _ -> false)
                masked
                god)

    /// **座席的历史与观测同出一源**（票 60）：一整局逐手推进，增量维护、一次性 fold
    /// 与引擎的权威状态**三方处处一致**。覆盖面是随机对局 × 随机座位 × **每一手**
    /// （票 29a 的原始要求）。三条腿各守什么、为什么它们归并不到同一个 fold，见 `SeatStreamGate`。
    [<Property(Arbitrary = [| typeof<SeatStreamRunArbitraries> |])>]
    let ``一整局逐手推进，增量维护、一次性 fold 与引擎的状态三方逐手一致`` (run: KyokuRun) =
        let found = SeatStreamGate.divergences run
        List.isEmpty found |> Prop.label (SeatStreamGate.toDisplay run found)

    // ---- 第四节：两条规则蕴含的时序不变量 ----

    /// 一条打牌事件是不是这一家的立直宣言牌：它紧跟在自己那条 `reach` 之后。
    ///
    /// 事件流里「宣言 → 宣言牌」中间不可能插进这一家的别的动作（立直宣言之后那一手只能打牌）。
    let dahaisAfterRiichi (seat: Seat) (events: Event list) : (Tile * bool) list =
        let rec loop (declared: bool) (accepted: bool) (rest: Event list) (found: (Tile * bool) list) =
            match rest with
            | [] -> List.rev found
            | Riichi actor :: tail when actor = seat -> loop true accepted tail found
            // 宣言牌本身通常是手切，不在「立直后只能摸切」之列。
            | Dahai(actor, _, _) :: tail when actor = seat && declared -> loop false accepted tail found
            | Dahai(actor, pai, tsumogiri) :: tail when actor = seat && accepted ->
                loop declared accepted tail ((pai, tsumogiri) :: found)
            | RiichiAccepted actor :: tail when actor = seat -> loop declared true tail found
            | _ :: tail -> loop declared accepted tail found

        loop false false events []

    [<Property>]
    let ``任意局面，立直成立之后那一家的打牌全是摸切（宣言牌除外）`` (state: GameState) =
        let events = GameState.events state

        Seat.all ruleset
        |> List.forall (fun seat -> dahaisAfterRiichi seat events |> List.forall snd)

    /// 碰 / 吃之后的那一张打牌：鸣牌不摸牌，因此**必然是手切**。
    /// 三种杠不在此列——杠要补摸岭上牌，那一手摸切是合法的。
    let private dahaisAfterNaki (events: Event list) : (Seat * bool) list =
        let rec loop (pending: Seat option) (rest: Event list) (found: (Seat * bool) list) =
            match rest, pending with
            | [], _ -> List.rev found
            | Pon(actor, _, _, _) :: tail, _
            | Chi(actor, _, _, _) :: tail, _ -> loop (Some actor) tail found
            | Dahai(actor, _, tsumogiri) :: tail, Some caller when actor = caller ->
                loop None tail ((actor, tsumogiri) :: found)
            | _ :: tail, _ -> loop pending tail found

        loop None events []

    [<Property>]
    let ``任意局面，碰或吃之后的那一张打牌必然是手切`` (state: GameState) =
        GameState.events state
        |> dahaisAfterNaki
        |> List.forall (fun (_, tsumogiri) -> not tsumogiri)

    // ---- 抢杠那个窗口的定点锚点（票 99 / 100）----

    /// 随机采样到不了抢杠那个窗口（全域里 `ResponseCause.Kan` 的局面 0 个），
    /// 而那一段恰好是掩蔽流最容易与局面分家的一段：事件已经播出去了、局面还没动。
    ///
    /// **`不出现他家暗牌` 现在四条轨迹全扫**（票 100）：票 99 时国士抢暗杠那一条被
    /// 显式排在外面，因为那四张牌必须亮给别家看而引擎里它们仍是暗牌；术语裁完之后
    /// （`CONTEXT.md` 的 `Ankan Declaration`）那一段落在定义域外，这条锚点把它收了回来。
    [<Fact>]
    let ``抢杠那个窗口：摊好牌山的轨迹逐步，掩蔽流的不变量都成立`` () =
        ChankanFixtures.sweep
            ChankanFixtures.traces
            [
                "只有自家摸牌带牌面", ``任意局面任意座位，只有自家那几条摸牌带着牌面``
                "与上帝视角对齐", ``任意局面任意座位，掩蔽流与上帝视角那条流一样长且逐条对齐``
                "立直后全摸切", ``任意局面，立直成立之后那一家的打牌全是摸切（宣言牌除外）``
                "鸣完必手切", ``任意局面，碰或吃之后的那一张打牌必然是手切``
            ]

        ChankanFixtures.sweep ChankanFixtures.traces [ "不出现他家暗牌", ``任意局面任意座位，掩蔽流里不出现他家暗牌中的任何一张`` ]

    // ---- 定义域那一段：它放行了什么、它之外还抓不抓得住（票 100）----

    /// 宣言那一刻，他家看得见那条掩蔽流，以及宣言的那个杠本身。
    ///
    /// **验的那几条都停在这一刻**（判据 20）：换一步量就不是这件事了——杠一旦成立，
    /// 那几张进了副露；一旦被抢，被抢的那张写在 `hora` 事件上。**两种结局都公开**，
    /// 拿它们量出来的空集只能说明量错了地方。
    let private viewsAtDeclaration () =
        [
            for label, phase, state in ChankanFixtures.declarationWindows ChankanFixtures.traces do
                let declarer = phase.Target

                let declared =
                    match phase.Cause with
                    | ResponseCause.Kan kan -> Naki.tiles kan
                    | ResponseCause.Dahai -> failwith $"{label} 的抢杠那一轮竟然不是对杠的响应"

                for viewer in Seat.all ruleset do
                    if viewer <> declarer then
                        yield label, declarer, declared, viewer, state
        ]

    /// **这条定义域到底放行了什么**：宣言那一刻，掩蔽流里那几个记法减去引擎那侧
    /// 「这家看得见的牌」——也就是改判据之前那条属性红在哪几个记法上。**逐条轨迹写死。**
    ///
    /// 少了这一条，「定义域不含宣言中的那几张」就可能是句空话：两条普通加杠轨迹加上去的
    /// 那张与底下那组碰同记法，差集本来就是空的——**票 99 §5.4 那条同型漏就是这么躲过去的**
    /// （真牌谱那两局同样不踩），因此票 100 摄了一条踩得到的进来。
    [<Fact>]
    let ``宣言中的那个杠：定义域放行的就是它亮出去、而引擎仍当成暗牌的那几张`` () =
        // 渲成一段文字再比，不拿 `Assert.Equal` 直比集合：xunit 把集合截成 `[···]`，
        // 而这一条红的时候要看的恰恰就是「那几个记法到底是哪几个」（同 `ChankanFixtures.sweep`）。
        let rendered =
            viewsAtDeclaration ()
            |> List.map (fun (label, _, _, viewer, state) ->
                let disclosed = Observation.stream viewer state |> notationsIn
                let excused = Set.difference disclosed (visibleTo viewer state)

                let notations = Set.toList excused |> String.concat " "

                if Set.isEmpty excused then
                    $"{label}：（一张也没放行）"
                else
                    $"{label}：{notations}")
            |> List.distinct
            |> String.concat "\n"

        let expected =
            [
                "加杠抢杠（天凤）：（一张也没放行）"
                "国士抢暗杠（雀魂）：7z"
                "加杠抢杠、抢的那家先立直（天凤）：（一张也没放行）"
                "加杠抢杠、加的那张是红宝牌（天凤）：5sr"
            ]
            |> String.concat "\n"

        Assert.Equal(expected, rendered)

    /// 往宣言那一条之前塞一条伪造的「他家摸牌带着牌面」（票 99 §6.1 弄坏 `MaskedEvent.forSeat`
    /// 时漏出来的就是这个形状）。
    ///
    /// **必须塞在宣言那一条之前**：塞在后面会给那条宣言接上一个「不是荣和」的下一条，
    /// 而按 `Ankan Declaration`，那就意味着那个杠成立了——定义域不再放行它，
    /// 这条对照验的就变成另一件事了。
    let private leaking (pai: Tile) (actor: Seat) (stream: MaskedEvent list) : MaskedEvent list =
        match List.rev stream with
        | declaration :: earlier -> List.rev earlier @ [ MaskedEvent.Tsumo(actor, Some pai); declaration ]
        | [] -> [ MaskedEvent.Tsumo(actor, Some pai) ]

    /// **「收窄定义域」与「把断言调松」的分界线**（票 100）：停在宣言中、还没结局那一刻，
    /// 往那条掩蔽流里塞一张**宣言之外**的他家暗牌，那条不变量必须照旧抓得住。
    ///
    /// 少了它，上面那条属性转绿在证据上有两种读法而分不开：一是定义域本来就不含宣言中的
    /// 那几张（本票认的），二是那道闸门被拆了当修好。**两头各验一遍**：
    ///
    /// - 宣言者手里那几张**与杠无关**的暗牌：一张也不准出现；
    /// - 宣言亮出去的那几个记法本身：换一条事件漏出来照样抓得住——
    ///   放行的是**那一条宣言事件**，不是那几种牌。
    [<Fact>]
    let ``宣言窗口之外的他家暗牌，那条不变量照旧抓得住`` () =
        let windows = viewsAtDeclaration ()

        // 四条轨迹 × 三个他家：少一个就说明哪条轨迹退化成了空转（判据 3）。
        Assert.Equal(3 * List.length ChankanFixtures.traces, List.length windows)

        for label, declarer, declared, viewer, state in windows do
            let stream = Observation.stream viewer state
            let where = $"{label}，座位 {Seat.index viewer} 看座位 {Seat.index declarer} 宣言的那个杠"
            let leaked = leaksTo viewer state stream
            let leakedNotations = Set.toList leaked |> String.concat " "

            // 同一刻、同一个座位：真的那条流不漏。对照组与对照的那一面在同一处量。
            Assert.True(Set.isEmpty leaked, $"{where}：真的那条流漏了 {leakedNotations}")

            let declaredNotations = declared |> List.map Tile.toMjai |> Set.ofList

            let concealed =
                match GameState.player declarer state with
                | Some player -> PlayerState.hand player @ Option.toList (PlayerState.drawn player)
                | None -> failwith $"{where}：宣言那家竟然不在牌桌上"

            // 宣言之外的他家暗牌：既不在那个杠里，这一刻也不在牌桌上。
            let outsiders =
                concealed
                |> List.filter (fun pai ->
                    not (Set.contains (Tile.toMjai pai) declaredNotations)
                    && not (Set.contains (Tile.toMjai pai) (visibleTo viewer state)))

            match outsiders with
            | [] -> failwith $"{where}：这一刻找不出一张宣言之外的他家暗牌，阴性对照已经退化成空转了"
            | outside :: _ ->
                let caught = leaking outside declarer stream |> leaksTo viewer state

                Assert.True(
                    Set.contains (Tile.toMjai outside) caught,
                    $"{where}：漏了一张 {Tile.toMjai outside}（宣言之外的暗牌），而那条不变量没抓住它"
                )

            // 宣言亮出去的那几个记法：放行的是那一条事件，换一条事件漏出来照样不行。
            for pai in declared do
                if not (Set.contains (Tile.toMjai pai) (visibleTo viewer state)) then
                    let caught = leaking pai declarer stream |> leaksTo viewer state

                    Assert.True(
                        Set.contains (Tile.toMjai pai) caught,
                        $"{where}：{Tile.toMjai pai} 换一条事件漏出来就不算漏了——放行的应当只有宣言那一条"
                    )

/// 见逃密集的局面上再跑一遍掩蔽流的不变量。**同巡振听与立直后见逃只在这批轨迹里出现**。
[<Properties(Arbitrary = [| typeof<MinogashiArbitraries> |], Parallelism = 4)>]
module MinogashiStreamProperties =

    /// 见逃密集的轨迹上再走一遍三方闸门：振听恰恰是「引擎知道的」与
    /// 「座席的历史推得出的」最容易分家的那个字段。
    [<Property(Arbitrary = [| typeof<MinogashiRunArbitraries> |])>]
    let ``见逃密集的一整局逐手推进，三方仍逐手一致`` (run: KyokuRun) =
        let found = SeatStreamGate.divergences run
        List.isEmpty found |> Prop.label (SeatStreamGate.toDisplay run found)

    [<Property>]
    let ``见逃密集的局面上，立直成立之后那一家的打牌全是摸切`` (state: GameState) =
        let events = GameState.events state

        Seat.all ruleset
        |> List.forall (fun seat -> MaskedStreamProperties.dahaisAfterRiichi seat events |> List.forall snd)
