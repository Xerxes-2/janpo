namespace Janpo.Engine.Tests

open Janpo

/// 一条对拍差异。**差异就是本票的产出**，不是失败：报出来才谈得上分类
/// （① 我们的 bug ② 规则集配错 ③ 牌谱解析 bug）。
type PaifuDiff =
    {
        /// 哪一局：牌谱 id + 局号 + 场风局数本场。
        Where: string
        /// 差在哪一类：`重放` / `事件流` / `役` / `符` / `番` / `和了点` / `清算` / `流局形态`。
        Kind: string
        Detail: string
    }

/// 一次和了的观测：引擎产出的 `Hora` 事件，加上**提交动作之前**从局面读到的读法。
/// 役与三种宝牌只在读法里（`Hora` 事件不带役），因此非在提交前读不可。
type HoraObservation = { Hora: Hora; Reading: HoraReading }

/// 一局重放的产物。
type KyokuReplay =
    {
        /// 报差异时定位用的标签。
        Label: string
        /// 引擎产出的事件流（含开局的 `start_kyoku` 与 Oya 的第一次 `tsumo`）。
        Events: Event list
        /// 每次和了的观测，按引擎产出 `Hora` 的顺序。
        Horas: HoraObservation list
        /// 流局收尾时的结果。
        Ryuukyoku: Ryuukyoku option
        /// 这一局打完之后的各家点数，按座位升序（含立直棒与授受）。
        /// **它该等于牌谱里下一局 `start_kyoku.scores`**，那是「每局终局点数」的对拍。
        Scores: int list
        /// 重放与事件流对拍报出的差异。
        Diffs: PaifuDiff list
    }

/// 一局的对拍结局。
type ReplayOutcome =
    /// 跑完了（`Diffs` 可能非空——**差异是产出**）。
    | Replayed of replay: KyokuReplay
    /// 显式跳过：牌谱转换不了或含不支持的规则变体。**要计数，不静默丢弃**。
    | Skipped of reason: string

/// 用真实牌谱重放引擎：从牌谱重建牌山、把牌谱的动作序列喂给 `GameState.step`，
/// 再把引擎产出的事件流与牌谱逐条对齐。
///
/// **不新造牌山注入机制**：摆好的一列牌交给已有的 `Wall.ofOrdered`，
/// 开局走已有的 `GameState.startFrom`。
[<RequireQualifiedAccess>]
module PaifuReplay =

    // ---- 牌山的重建 ----

    /// 从一列牌里拿掉第一张与 `tile` 相同的；没有就原样返回。
    let private removeOne (tile: Tile) (tiles: Tile list) : Tile list =
        match List.tryFindIndex ((=) tile) tiles with
        | Some index -> List.take index tiles @ List.skip (index + 1) tiles
        | None -> tiles

    /// 配牌的取牌手顺（4-4-4-1），与 `Wall.deal` 的手顺同一份，只是反过来用：
    /// 已知四家的配牌，倒推它们在牌山里的先后。
    let private haipaiOrder (ruleset: Ruleset) (oya: Seat) (tehais: Tile list list) : Tile list =
        let rounds = ruleset.HaipaiSize / 4
        let rest = ruleset.HaipaiSize % 4
        let chunks = [ for _ in 1..rounds -> 4 ] @ (if rest > 0 then [ rest ] else [])

        let plan =
            [
                for chunk in chunks do
                    for seat in Seat.orderFrom ruleset oya -> seat, chunk
            ]

        (([], tehais), plan)
        ||> List.fold (fun (taken, remaining) (seat, chunk) ->
            let hand = Seat.tryItem seat remaining |> Option.defaultValue []
            let size = min chunk (List.length hand)

            taken @ List.truncate size hand, remaining |> Seat.mapAt seat (fun _ -> List.skip size hand))
        |> fst

    /// 摸进的牌分两路：从可摸区摸的，与杠之后从王牌补摸的岭上牌。
    /// **紧跟在杠后面的那次自摸是岭上牌**；中间可能夹一条 `dora`（暗杠正是那个顺序）。
    let private drawnTiles (moves: PaifuEvent list) : Tile list * Tile list =
        let step (live, rinshan, afterKan) event =
            match event with
            | PaifuEvent.Tsumo(_, pai) ->
                if afterKan then
                    live, rinshan @ [ pai ], false
                else
                    live @ [ pai ], rinshan, false
            // 翻宝牌不打断「杠 → 补摸」：暗杠的顺序就是 `ankan` → `dora` → `tsumo`。
            | PaifuEvent.Dora _ -> live, rinshan, afterKan
            | PaifuEvent.Ankan _
            | PaifuEvent.Kakan _
            | PaifuEvent.Minkan _ -> live, rinshan, true
            | PaifuEvent.StartGame _
            | PaifuEvent.StartKyoku _
            | PaifuEvent.Dahai _
            | PaifuEvent.Pon _
            | PaifuEvent.Chi _
            | PaifuEvent.Riichi _
            | PaifuEvent.RiichiAccepted _
            | PaifuEvent.Hora _
            | PaifuEvent.Ryuukyoku _
            | PaifuEvent.EndKyoku
            | PaifuEvent.EndGame -> live, rinshan, false

        let live, rinshan, _ = moves |> List.fold step ([], [], false)
        live, rinshan

    /// 牌谱里露过面的表宝牌指示牌（开局那张加上每次杠翻的那张）与里宝牌指示牌
    /// （只有和了者立了直才公开；双响时两家看到的是同一列，取最长的那条）。
    let private indicatorsIn (kyoku: PaifuKyoku) : Tile list * Tile list =
        let dora = kyoku.Moves |> List.choose PaifuEvent.doraMarker

        let ura =
            kyoku.Moves
            |> List.choose (PaifuEvent.hora >> Option.map (fun hora -> hora.UraDoraMarkers))
            |> List.sortByDescending List.length
            |> List.tryHead
            |> Option.defaultValue []

        kyoku.Start.DoraMarker :: dora, ura

    /// 从一局牌谱重建牌山：配牌与摸牌按牌谱摆好，表里宝牌指示牌按牌谱摆进王牌，
    /// 其余位置由整副牌里**没露过面的**牌补满。
    ///
    /// 摆出来的一列牌交给 `Wall.ofOrdered`：可摸区在前、末尾 `DeadWallSize` 张是王牌，
    /// 王牌里岭上牌在前、其后每两张是一叠（表宝牌指示牌，里宝牌指示牌）。
    let private buildWall (ruleset: Ruleset) (kyoku: PaifuKyoku) : Result<Wall, string> =
        let haipai = haipaiOrder ruleset kyoku.Start.Oya kyoku.Start.Tehais
        let live, rinshan = drawnTiles kyoku.Moves
        let dora, ura = indicatorsIn kyoku
        let known = haipai @ live @ rinshan @ dora @ ura

        let filler =
            (Ruleset.wallTiles ruleset, known)
            ||> List.fold (fun rest tile -> removeOne tile rest)

        let liveSize = Ruleset.wallSize ruleset - ruleset.DeadWallSize
        let liveFillerCount = liveSize - List.length haipai - List.length live
        let rinshanFillerCount = ruleset.RinshanCount - List.length rinshan

        if List.length filler <> Ruleset.wallSize ruleset - List.length known then
            Error "牌谱里露过面的牌凑不进整副牌（同一种牌最多四张）"
        elif liveFillerCount < 0 then
            Error $"牌谱的摸牌数超出了可摸区（多 {-liveFillerCount} 张）"
        elif rinshanFillerCount < 0 then
            Error $"牌谱的杠数超出了岭上牌（多 {-rinshanFillerCount} 次）"
        else
            let liveFiller, afterLive = List.splitAt liveFillerCount filler
            let rinshanFiller, indicatorFiller = List.splitAt rinshanFillerCount afterLive

            // 指示牌区按「表, 里」成叠排；牌谱没公开的那几张用余牌顶上。
            let slots =
                [
                    for index in 0 .. (ruleset.DeadWallSize - ruleset.RinshanCount) / 2 - 1 do
                        yield List.tryItem index dora
                        yield List.tryItem index ura
                ]

            let indicators, _ =
                (([], indicatorFiller), slots)
                ||> List.fold (fun (placed, pool) slot ->
                    match slot, pool with
                    | Some tile, _ -> placed @ [ tile ], pool
                    | None, head :: rest -> placed @ [ head ], rest
                    | None, [] -> placed, [])

            let tiles = haipai @ live @ liveFiller @ rinshan @ rinshanFiller @ indicators

            if List.length tiles <> Ruleset.wallSize ruleset then
                Error $"重建出来的牌山有 {List.length tiles} 张，应当是 {Ruleset.wallSize ruleset} 张"
            else
                Ok(Wall.ofOrdered ruleset tiles)

    // ---- 重放 ----

    let private diff (label: string) (kind: string) (detail: string) : PaifuDiff =
        {
            Where = label
            Kind = kind
            Detail = detail
        }

    /// 重放过程中的游标：局面、还没喂进去的牌谱事件、读到的和了读法与已报的差异。
    type private Driving =
        {
            State: GameState
            Queue: PaifuEvent list
            Readings: (Seat * HoraReading) list
            Diffs: PaifuDiff list
            /// 重放推不下去了：停在这里，把已报的差异交出去。
            Aborted: bool
        }

    /// 引擎自己产出的事件，不必提交：摸牌、翻宝牌、立直成立与三条 game / kyoku 级事件。
    let private isEngineProduced (event: PaifuEvent) : bool =
        match event with
        | PaifuEvent.Tsumo _
        | PaifuEvent.Dora _
        | PaifuEvent.RiichiAccepted _
        | PaifuEvent.StartGame _
        | PaifuEvent.StartKyoku _
        | PaifuEvent.EndKyoku
        | PaifuEvent.EndGame -> true
        | PaifuEvent.Dahai _
        | PaifuEvent.Pon _
        | PaifuEvent.Chi _
        | PaifuEvent.Ankan _
        | PaifuEvent.Kakan _
        | PaifuEvent.Minkan _
        | PaifuEvent.Riichi _
        | PaifuEvent.Hora _
        | PaifuEvent.Ryuukyoku _ -> false

    /// 这条牌谱事件是不是对当前这一轮响应的宣言。
    /// 抢杠那一轮只可能是荣和（`ResponseCause.Kan`：杠上鸣不了牌）。
    let private isResponseTo (waiting: AwaitingResponse) (event: PaifuEvent) : bool =
        let toDiscard (target: Seat) (pai: Tile) =
            match waiting.Cause with
            | ResponseCause.Dahai -> target = waiting.Target && pai = waiting.Pai
            | ResponseCause.Kan _ -> false

        match event with
        | PaifuEvent.Hora hora -> hora.Target = waiting.Target && hora.Actor <> hora.Target
        | PaifuEvent.Pon(_, target, pai, _)
        | PaifuEvent.Chi(_, target, pai, _)
        | PaifuEvent.Minkan(_, target, pai, _) -> toDiscard target pai
        | PaifuEvent.StartGame _
        | PaifuEvent.StartKyoku _
        | PaifuEvent.Tsumo _
        | PaifuEvent.Dahai _
        | PaifuEvent.Ankan _
        | PaifuEvent.Kakan _
        | PaifuEvent.Dora _
        | PaifuEvent.Riichi _
        | PaifuEvent.RiichiAccepted _
        | PaifuEvent.Ryuukyoku _
        | PaifuEvent.EndKyoku
        | PaifuEvent.EndGame -> false

    let private abort (label: string) (detail: string) (driving: Driving) : Driving =
        { driving with
            Diffs = driving.Diffs @ [ diff label "重放" detail ]
            Aborted = true
        }

    /// 把一个动作交给引擎。被拒绝就是一处差异，且重放到此为止。
    let private submit (label: string) (action: Action) (driving: Driving) : Driving =
        match GameState.step driving.State action with
        | Ok(next, _) -> { driving with State = next }
        | Error illegal -> abort label $"引擎拒绝了牌谱里的动作：{IllegalAction.toDisplay illegal}" driving

    /// 提交一条和了宣言，并**在提交前**把这一手的读法记下来（役与宝牌只在读法里）。
    let private submitHora (label: string) (actor: Seat) (target: Seat) (pai: Tile) (driving: Driving) : Driving =
        let recorded =
            match GameState.horaOf actor driving.State with
            | Ok reading ->
                { driving with
                    Readings = driving.Readings @ [ actor, reading ]
                }
            | Error error ->
                let reason =
                    match error with
                    | YakuError.NoAgariShape -> "引擎判这手不成和了型"
                    | YakuError.NoYaku -> "引擎判这手无役"

                { driving with
                    Diffs = driving.Diffs @ [ diff label "役" $"牌谱里座位 {Seat.index actor} 和了，{reason}" ]
                }

        submit label (Action.Hora(actor, target, pai)) recorded

    /// 从队列里拿掉第一条与 `event` 相同的事件。
    let private removeFirstEvent (event: PaifuEvent) (queue: PaifuEvent list) : PaifuEvent list =
        match List.tryFindIndex ((=) event) queue with
        | Some index -> List.take index queue @ List.skip (index + 1) queue
        | None -> queue

    /// 队列里下一条要喂进去的事件：引擎自己产出的先跳过。
    let private pendingMoves (queue: PaifuEvent list) : PaifuEvent list = List.skipWhile isEngineProduced queue

    /// 响应阶段的一步：按引擎等答复的顺序，逐座位交出牌谱里的宣言，没宣言的交「过」。
    let private stepResponse (label: string) (waiting: AwaitingResponse) (driving: Driving) : Driving =
        match waiting.Responses with
        // 走不到：响应阶段必然还等着至少一家。
        | [] -> abort label "引擎停在响应阶段却没有等答复的座位" driving
        | pending :: _ ->
            let seat = pending.Seat

            let declared =
                pendingMoves driving.Queue
                |> List.takeWhile (isResponseTo waiting)
                |> List.tryFind (fun event -> PaifuEvent.actor event = Some seat)

            match declared with
            | None -> submit label (Action.None seat) driving
            | Some event ->
                let dropped =
                    { driving with
                        Queue = removeFirstEvent event driving.Queue
                    }

                match event with
                | PaifuEvent.Hora _ -> submitHora label seat waiting.Target waiting.Pai dropped
                | PaifuEvent.Pon(actor, target, pai, consumed) ->
                    submit label (Action.Pon(actor, target, pai, Tile.sort consumed)) dropped
                | PaifuEvent.Chi(actor, target, pai, consumed) ->
                    submit label (Action.Chi(actor, target, pai, Tile.sort consumed)) dropped
                | PaifuEvent.Minkan(actor, target, pai, consumed) ->
                    submit label (Action.Minkan(actor, target, pai, Tile.sort consumed)) dropped
                // 走不到：`isResponseTo` 只放行上面那四种。
                | PaifuEvent.StartGame _
                | PaifuEvent.StartKyoku _
                | PaifuEvent.Tsumo _
                | PaifuEvent.Dahai _
                | PaifuEvent.Ankan _
                | PaifuEvent.Kakan _
                | PaifuEvent.Dora _
                | PaifuEvent.Riichi _
                | PaifuEvent.RiichiAccepted _
                | PaifuEvent.Ryuukyoku _
                | PaifuEvent.EndKyoku
                | PaifuEvent.EndGame -> abort label "响应阶段收到了不是响应的牌谱事件" dropped

    /// 引擎没把响应机会给出去时，把那一家的振听与立直状态一并报出来。
    /// **分类差异靠它**：「引擎不让荣和」到底是振听判错、无役还是型不成，差很多。
    let private whyNotOffered (event: PaifuEvent) (state: GameState) : string =
        match PaifuEvent.actor event |> Option.bind (fun seat -> GameState.player seat state) with
        | Some player -> Furiten.toDisplay (PlayerState.furiten player)
        | None -> "读不到那一家的状态"

    /// 摸牌后阶段的一步：打牌、宣言立直、自家宣言的杠、自摸和或九种九牌。
    let private stepDahai (label: string) (waiting: AwaitingDahai) (driving: Driving) : Driving =
        match pendingMoves driving.Queue with
        | [] -> abort label "牌谱走完了，引擎还等着有人出手" driving
        | event :: rest ->
            let advanced = { driving with Queue = rest }

            match event with
            | PaifuEvent.Dahai(actor, pai, tsumogiri) -> submit label (Action.Dahai(actor, pai, tsumogiri)) advanced
            | PaifuEvent.Riichi actor -> submit label (Action.Riichi actor) advanced
            | PaifuEvent.Ankan(actor, consumed) -> submit label (Action.Ankan(actor, Tile.sort consumed)) advanced
            | PaifuEvent.Kakan(actor, pai, consumed) ->
                submit label (Action.Kakan(actor, pai, Tile.sort consumed)) advanced
            | PaifuEvent.Hora hora when hora.Actor = hora.Target ->
                match waiting.Tsumo with
                | Some drawn -> submitHora label hora.Actor hora.Actor drawn advanced
                | None -> abort label "牌谱说这里自摸和，但这一手没摸牌" advanced
            // 摸牌后阶段的 `ryukyoku` 只可能是九种九牌：其余形态由引擎自己判。
            | PaifuEvent.Ryuukyoku _ -> submit label (Action.Ryuukyoku waiting.Actor) advanced
            | PaifuEvent.Hora _
            | PaifuEvent.Pon _
            | PaifuEvent.Chi _
            | PaifuEvent.Minkan _ -> abort label $"牌谱里有人响应上一张，引擎却没进响应阶段（{whyNotOffered event advanced.State}）" advanced
            // 走不到：`pendingMoves` 已经跳过这几种。
            | PaifuEvent.Tsumo _
            | PaifuEvent.Dora _
            | PaifuEvent.RiichiAccepted _
            | PaifuEvent.StartGame _
            | PaifuEvent.StartKyoku _
            | PaifuEvent.EndKyoku
            | PaifuEvent.EndGame -> abort label "引擎自己产出的事件混进了要提交的队列" advanced

    let rec private drive (label: string) (driving: Driving) : Driving =
        if driving.Aborted then
            driving
        else
            match GameState.phase driving.State with
            // 一局已终：队列里剩下的事件由事件流对拍去比。
            | Ended _ -> driving
            | AwaitingResponse waiting -> driving |> stepResponse label waiting |> drive label
            | AwaitingDahai waiting -> driving |> stepDahai label waiting |> drive label

    // ---- 事件流的对拍 ----

    let private renderTiles (tiles: Tile list) : string = tiles |> Tile.sort |> Tile.toMjaiMany

    let private renderInts (values: int list) : string =
        values |> List.map string |> String.concat ","

    let private startKyokuLine
        (bakaze: Kaze)
        (kyoku: int)
        (honba: int)
        (kyotaku: int)
        (oya: Seat)
        (doraMarker: Tile)
        (scores: int list)
        (tehais: Tile list list)
        : string =
        let hands = tehais |> List.map renderTiles |> String.concat "/"

        let head =
            $"start_kyoku {Kaze.toMjai bakaze} {kyoku} {honba} {kyotaku} {Seat.index oya}"

        $"{head} {Tile.toMjai doraMarker} {renderInts scores} {hands}"

    let private nakiLine (wire: string) (actor: Seat) (target: Seat) (pai: Tile) (consumed: Tile list) : string =
        $"{wire} {Seat.index actor} {Seat.index target} {Tile.toMjai pai} {renderTiles consumed}"

    let private horaLine (actor: Seat) (target: Seat) (deltas: int list) (ura: Tile list) : string =
        $"hora {Seat.index actor} {Seat.index target} {renderInts deltas} {renderTiles ura}"

    /// 引擎事件 → 可与牌谱比的一行。**牌谱没有的字段不进这一行**
    /// （符 / 番 / 和了点 / 流局形态由 oracle 那侧比）；`start_game` 与
    /// 两条 kyoku 级事件不在引擎的一局事件流里。
    let private renderEngine (event: Event) : string option =
        match event with
        | StartKyoku fields ->
            startKyokuLine
                fields.Bakaze
                fields.Kyoku
                fields.Honba
                fields.Kyotaku
                fields.Oya
                fields.DoraMarker
                fields.Scores
                fields.Tehais
            |> Some
        | Tsumo(actor, pai) -> Some $"tsumo {Seat.index actor} {Tile.toMjai pai}"
        | Dahai(actor, pai, tsumogiri) -> Some $"dahai {Seat.index actor} {Tile.toMjai pai} {tsumogiri}"
        | Pon(actor, target, pai, consumed) -> Some(nakiLine "pon" actor target pai consumed)
        | Chi(actor, target, pai, consumed) -> Some(nakiLine "chi" actor target pai consumed)
        | Minkan(actor, target, pai, consumed) -> Some(nakiLine "daiminkan" actor target pai consumed)
        | Ankan(actor, consumed) -> Some $"ankan {Seat.index actor} {renderTiles consumed}"
        | Kakan(actor, pai, consumed) -> Some $"kakan {Seat.index actor} {Tile.toMjai pai} {renderTiles consumed}"
        | Dora marker -> Some $"dora {Tile.toMjai marker}"
        | Riichi actor -> Some $"reach {Seat.index actor}"
        | RiichiAccepted actor -> Some $"reach_accepted {Seat.index actor}"
        | Hora hora -> Some(horaLine hora.Actor hora.Target hora.Deltas hora.UraDoraMarkers)
        | Ryuukyoku result -> Some $"ryukyoku {renderInts result.Deltas}"
        | StartGame _
        | EndKyoku
        | EndGame -> None

    /// 牌谱事件 → 与 `renderEngine` 对称的一行。
    let private renderPaifu (event: PaifuEvent) : string option =
        match event with
        | PaifuEvent.StartKyoku fields ->
            startKyokuLine
                fields.Bakaze
                fields.Kyoku
                fields.Honba
                fields.Kyotaku
                fields.Oya
                fields.DoraMarker
                fields.Scores
                fields.Tehais
            |> Some
        | PaifuEvent.Tsumo(actor, pai) -> Some $"tsumo {Seat.index actor} {Tile.toMjai pai}"
        | PaifuEvent.Dahai(actor, pai, tsumogiri) -> Some $"dahai {Seat.index actor} {Tile.toMjai pai} {tsumogiri}"
        | PaifuEvent.Pon(actor, target, pai, consumed) -> Some(nakiLine "pon" actor target pai consumed)
        | PaifuEvent.Chi(actor, target, pai, consumed) -> Some(nakiLine "chi" actor target pai consumed)
        | PaifuEvent.Minkan(actor, target, pai, consumed) -> Some(nakiLine "daiminkan" actor target pai consumed)
        | PaifuEvent.Ankan(actor, consumed) -> Some $"ankan {Seat.index actor} {renderTiles consumed}"
        | PaifuEvent.Kakan(actor, pai, consumed) ->
            Some $"kakan {Seat.index actor} {Tile.toMjai pai} {renderTiles consumed}"
        | PaifuEvent.Dora marker -> Some $"dora {Tile.toMjai marker}"
        | PaifuEvent.Riichi actor -> Some $"reach {Seat.index actor}"
        | PaifuEvent.RiichiAccepted actor -> Some $"reach_accepted {Seat.index actor}"
        | PaifuEvent.Hora hora -> Some(horaLine hora.Actor hora.Target hora.Deltas hora.UraDoraMarkers)
        | PaifuEvent.Ryuukyoku deltas -> Some $"ryukyoku {renderInts deltas}"
        | PaifuEvent.StartGame _
        | PaifuEvent.EndKyoku
        | PaifuEvent.EndGame -> None

    /// 上游转换器的一处噪声：**双响时它把同一份 `ura_markers` 抄在两条 `hora` 上**，
    /// 哪怕后一家根本没立直（200 局语料里 6 处，全是双响，而两边的钱流一字不差）。
    /// 里宝牌只在和了者立了直时才翻，因此这里按牌谱**自己的** `reach_accepted` 把噪声抹掉——
    /// 判据取自牌谱而不是引擎，立了直那几家的里宝牌照比不误。
    let private denoiseUra (kyoku: PaifuKyoku) : PaifuEvent list =
        let accepted = kyoku.Moves |> List.choose PaifuEvent.riichiAccepted

        kyoku.Moves
        |> List.map (fun event ->
            match event with
            | PaifuEvent.Hora hora when not (List.contains hora.Actor accepted) ->
                PaifuEvent.Hora { hora with UraDoraMarkers = [] }
            | other -> other)

    /// 逐条比引擎产出的事件流与牌谱。**这是本票最粗的那道网**：
    /// 摸到的牌、鸣牌的时机、翻宝牌的时机、立直成立的时机与钱流全在里面。
    let private compareEvents (label: string) (kyoku: PaifuKyoku) (events: Event list) : PaifuDiff list =
        let expected =
            (PaifuEvent.StartKyoku kyoku.Start :: denoiseUra kyoku)
            |> List.choose renderPaifu

        let actual = events |> List.choose renderEngine

        List.init (max (List.length expected) (List.length actual)) (fun index ->
            let item (lines: string list) (fallback: string) =
                lines |> List.tryItem index |> Option.defaultValue fallback

            index, item expected "（牌谱到此为止）", item actual "（引擎到此为止）")
        |> List.filter (fun (_, one, other) -> one <> other)
        |> List.truncate 3
        |> List.map (fun (index, one, other) -> diff label "事件流" $"第 {index} 条：牌谱 [{one}]，引擎 [{other}]")

    // ---- 入口 ----

    /// 由牌谱的开局条件构造 `KyokuContext`。**点数取牌谱的**：
    /// 局与局之间的推进不在本票范围，每一局各自从牌谱的既定条件开起。
    let private contextOf (start: PaifuStartKyoku) : KyokuContext =
        {
            Bakaze = start.Bakaze
            Kyoku = start.Kyoku
            Honba = start.Honba
            Kyotaku = start.Kyotaku
            Oya = start.Oya
            Scores = start.Scores
        }

    /// 重放一局。`label` 进每一条差异，定位用。
    let kyoku (ruleset: Ruleset) (label: string) (kyoku: PaifuKyoku) : ReplayOutcome =
        if List.length kyoku.Start.Tehais <> ruleset.SeatCount then
            Skipped $"配牌有 {List.length kyoku.Start.Tehais} 家，不是 {ruleset.SeatCount} 家（可能不是四麻）"
        else
            match buildWall ruleset kyoku with
            | Error reason -> Skipped $"重建不出牌山：{reason}"
            | Ok wall ->
                match GameState.startFrom ruleset (contextOf kyoku.Start) wall with
                | Error error -> Skipped $"开不出局：{KyokuStartError.toDisplay error}"
                | Ok opening ->
                    let driven =
                        drive
                            label
                            {
                                State = opening
                                Queue = kyoku.Moves
                                Readings = []
                                Diffs = []
                                Aborted = false
                            }

                    let events = GameState.events driven.State

                    let unfinished =
                        if GameState.isEnded driven.State || driven.Aborted then
                            []
                        else
                            [ diff label "重放" "牌谱走完了，引擎这一局还没结束" ]

                    // 重放中止之后事件流必然对不齐，再比一遍只是噪声。
                    let stream =
                        if driven.Aborted then
                            []
                        else
                            compareEvents label kyoku events

                    let horas =
                        GameState.horas driven.State
                        |> List.choose (fun hora ->
                            driven.Readings
                            |> List.tryFind (fun (actor, _) -> actor = hora.Actor)
                            |> Option.map (fun (_, reading) -> { Hora = hora; Reading = reading }))

                    Replayed
                        {
                            Label = label
                            Events = events
                            Horas = horas
                            Ryuukyoku = GameState.ryuukyoku driven.State
                            Scores = GameState.scores driven.State
                            Diffs = driven.Diffs @ unfinished @ stream
                        }
