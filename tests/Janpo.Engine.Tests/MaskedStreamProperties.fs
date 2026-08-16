namespace Janpo.Engine.Tests

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

    let private streamTiles (seat: Seat) (state: GameState) : Set<string> =
        Observation.stream seat state
        |> List.collect MaskedEvent.tiles
        |> List.map Tile.toMjai
        |> Set.ofList

    [<Property>]
    let ``任意局面任意座位，掩蔽流里不出现他家暗牌中的任何一张`` (state: GameState) =
        Seat.all ruleset
        |> List.forall (fun seat -> Set.isSubset (streamTiles seat state) (visibleTo seat state))

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

    /// 增量维护与一次性全流 fold 给出同一份观测吗。**见逃密集那一组也用它**，因此抽在这里。
    let incrementalAgrees (state: GameState) : bool =
        let events = GameState.events state

        Seat.all ruleset
        |> List.forall (fun seat ->
            let incremental =
                (SeatStream.start ruleset seat, events)
                ||> List.fold (fun stream event -> SeatStream.advance event stream)
                |> SeatStream.observation

            incremental = Observation.ofEvents ruleset seat events)

    /// **增量维护与一次性全流 fold 给出同一份观测**：牌桌逐手推进时不必每帧重头 fold 全流。
    [<Property>]
    let ``任意局面任意座位，逐条推进与一次性 fold 给出同一份观测`` (state: GameState) = incrementalAgrees state

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

/// 见逃密集的局面上再跑一遍掩蔽流的不变量。**同巡振听与立直后见逃只在这批轨迹里出现**。
[<Properties(Arbitrary = [| typeof<MinogashiArbitraries> |], Parallelism = 4)>]
module MinogashiStreamProperties =

    [<Property>]
    let ``见逃密集的局面上，逐条推进与一次性 fold 仍给出同一份观测`` (state: GameState) =
        MaskedStreamProperties.incrementalAgrees state

    [<Property>]
    let ``见逃密集的局面上，立直成立之后那一家的打牌全是摸切`` (state: GameState) =
        let events = GameState.events state

        Seat.all ruleset
        |> List.forall (fun seat -> MaskedStreamProperties.dahaisAfterRiichi seat events |> List.forall snd)
