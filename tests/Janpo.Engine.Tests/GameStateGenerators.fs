namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 局面的固件：开局、把一局跑完、以及两种选手。具名用例与属性共用。
module GameStateFixtures =

    let ruleset = Ruleset.yonma

    let context = KyokuContext.initial ruleset

    let kindSet = TileKindSet.ofKinds ruleset.TileKinds

    /// 开一局。开不出来说明前序票坏了，直接让测试失败。
    let start (seed: int) : GameState * Rng =
        match GameState.start ruleset context (Rng.ofSeed seed) with
        | Ok pair -> pair
        | Error error -> failwith $"应当开得出局，却得到 {KyokuStartError.toDisplay error}"

    let removeOne (tile: Tile) (tiles: Tile list) : Tile list =
        let rec loop skipped rest =
            match rest with
            | [] -> List.rev skipped
            | head :: tail when head = tile -> List.rev skipped @ tail
            | head :: tail -> loop (head :: skipped) tail

        loop [] tiles

    /// 配牌的取牌手顺（4-4-4-1）：与 `Wall.deal` 的手顺同一份，只是反过来用。
    let private haipaiChunks (ruleset: Ruleset) : int list =
        let rounds = ruleset.HaipaiSize / 4
        let rest = ruleset.HaipaiSize % 4
        [ for _ in 1..rounds -> 4 ] @ (if rest > 0 then [ rest ] else [])

    /// 配牌与摸牌一共用掉的那一串牌，按牌山里的位置顺序。
    let private usedBy (ruleset: Ruleset) (oya: Seat) (hands: Tile list list) (draws: Tile list) : Tile list =
        let plan =
            [
                for chunk in haipaiChunks ruleset do
                    for seat in Seat.orderFrom ruleset oya -> seat, chunk
            ]

        let dealt, _ =
            (([], hands), plan)
            ||> List.fold (fun (taken, remaining) (seat, chunk) ->
                let hand = List.item seat remaining

                let drawnNow = List.truncate chunk hand
                let rest = List.skip (min chunk (List.length hand)) hand

                taken @ drawnNow, remaining |> List.mapi (fun index each -> if index = seat then rest else each))

        dealt @ draws

    /// 整副牌里没被用掉的那些，用来把牌山补满。
    let private fillerFor (ruleset: Ruleset) (used: Tile list) : Tile list =
        let filler =
            (Ruleset.wallTiles ruleset, used)
            ||> List.fold (fun rest tile -> removeOne tile rest)

        if List.length filler <> Ruleset.wallSize ruleset - List.length used then
            failwith "摊牌山用了整副牌里没有的牌（同一种牌最多四张）"

        filler

    /// 摊一座牌山：四家按 `hands` 拿到配牌，之后按 `draws` 的顺序摸牌（`draws` 的第一张
    /// 是 Oya 的第一次自摸，之后依次是下家、对家、上家……），余下的位置用整副牌里
    /// 没被用掉的牌补满，末尾 `DeadWallSize` 张自然成为王牌。
    ///
    /// 黄金用例「让指定的和了在指定 Junme 发生」靠它：牌山是确定性的，不必去碰运气找种子。
    let scriptedWall (ruleset: Ruleset) (oya: Seat) (hands: Tile list list) (draws: Tile list) : Wall =
        let used = usedBy ruleset oya hands draws
        Wall.ofOrdered ruleset (used @ fillerFor ruleset used)

    /// 摊一座**连岭上牌也指定**的牌山：`rinshan` 按补摸顺序摆在王牌的头上，
    /// 不足 `RinshanCount` 张的部分由余牌补齐，多出来的那几张自然落进指示牌区。
    ///
    /// 岭上开花与责任支付的黄金用例非得指定补摸的那张不可。
    let scriptedWallWithRinshan
        (ruleset: Ruleset)
        (oya: Seat)
        (hands: Tile list list)
        (draws: Tile list)
        (rinshan: Tile list)
        : Wall =
        let used = usedBy ruleset oya hands draws
        let filler = fillerFor ruleset (used @ rinshan)
        let liveSize = Ruleset.wallSize ruleset - ruleset.DeadWallSize
        let liveFiller = List.truncate (liveSize - List.length used) filler
        let deadFiller = List.skip (List.length liveFiller) filler

        Wall.ofOrdered ruleset (used @ liveFiller @ rinshan @ deadFiller)

    /// 记法写的手牌。摊牌山的用例里手牌一律写 mjai 记法，读起来才像牌。
    let tilesOf (notations: string) : Tile list =
        match Tile.parseMany notations with
        | Ok parsed -> parsed
        | Error error -> failwith $"应当是合法记法，却得到 {TileListParseError.toDisplay error}"

    /// 一局的剧本：四家的配牌加上摸牌顺序，都写成 mjai 记法。
    type Script = { Hands: string list; Draws: string }

    /// 用摊好的牌山开一局，规则集与开局条件都可换
    /// （头跳开关、本场与供托的那几条用例要它）。
    let startScriptedIn (ruleset: Ruleset) (kyokuContext: KyokuContext) (script: Script) : GameState =
        let hands = script.Hands |> List.map tilesOf
        let wall = scriptedWall ruleset kyokuContext.Oya hands (tilesOf script.Draws)

        match GameState.startFrom ruleset kyokuContext wall with
        | Ok state -> state
        | Error error -> failwith $"应当开得出局，却得到 {KyokuStartError.toDisplay error}"

    /// 用摊好的牌山开一局，规则集可换（头跳开关的两条用例要它）。
    let startScriptedWith (ruleset: Ruleset) (script: Script) : GameState = startScriptedIn ruleset context script

    /// 用摊好的牌山开一局。
    let startScripted (script: Script) : GameState = startScriptedWith ruleset script

    /// 用摊好的牌山开一局，**岭上牌也是摊出来的**（按补摸顺序写 mjai 记法）。
    let startScriptedRinshanIn (ruleset: Ruleset) (rinshan: string) (script: Script) : GameState =
        let hands = script.Hands |> List.map tilesOf

        let wall =
            scriptedWallWithRinshan ruleset context.Oya hands (tilesOf script.Draws) (tilesOf rinshan)

        match GameState.startFrom ruleset context wall with
        | Ok state -> state
        | Error error -> failwith $"应当开得出局，却得到 {KyokuStartError.toDisplay error}"

    /// 用摊好的牌山（含岭上牌）开一局。
    let startScriptedRinshan (rinshan: string) (script: Script) : GameState =
        startScriptedRinshanIn ruleset rinshan script

    /// 自摸和的剧本：Oya 听 5z 单骑，第 1 巡摸进没用的 1z 打掉，第 2 巡摸进 5z。
    /// 其余三家是一手孤张，既和不了也听不了。
    let tsumoHoraScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 4z 6z 7z 9m"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 4z 6z 7z 9m"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 4z 6z 7z 9m"
                ]
            Draws = "1z 2z 3z 4z 5z"
        }

    /// 荣和与振听的剧本：座位 1 与座位 2 都听 4p，座位 1 第 1 巡自己打掉 7p
    /// （它的另一张和了牌）因而永久振听；座位 0 第 2 巡摸进 4p 打出去。
    /// 之后座位 1 第 2 巡摸进 1p 打出、座位 2 摸进 3z、座位 3 再打出 1p——同巡振听的用例要这一段。
    ///
    /// 座位 2 是四顺子 + 非役牌雀头的**平和**形：1p 与 4p 两端都成平和，因此它的荣和不靠
    /// 「无役也能和」成立（无役不可和是 08 票接上的）。
    let ronFuritenScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 5s 8s 6p"
                    "2m 3m 4m 5m 6m 7m 2p 3p 4p 5p 6p 9s 9s"
                    "2s 3s 4s 6s 7s 8s 3m 4m 5m 9m 9m 2p 3p"
                    "6z 6z 7z 7z 3m 6m 9m 1s 4s 7s 1p 8p 9p"
                ]
            Draws = "6z 7p 7z 5z 4p 1p 3z 1p"
        }

    /// 同巡三响的剧本：座位 1、2、3 都听 4p，座位 0 第 2 巡打出 4p。
    /// 头跳关掉时三家都成立（天凤把三家和了判成途中流局，那是 12 票的规则集字段）。
    /// 三家各自有役：座位 1 自风南刻子、座位 2 平和、座位 3 平和断幺九。
    let tripleRonScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 5s 8s 6p"
                    "5m 6m 7m 2z 2z 2z 3z 3z 3z 4z 4z 3p 5p"
                    "2s 3s 4s 6s 7s 8s 3m 4m 5m 9m 9m 2p 3p"
                    "2m 3m 4m 6m 7m 8m 3s 4s 5s 7s 7s 5p 6p"
                ]
            Draws = "6z 7z 9p 9s 4p"
        }

    /// 同巡双响的剧本：座位 2 听 1p / 4p、座位 3 听 4p / 7p，座位 0 第 2 巡打出 4p。
    /// 打牌者是座位 0，因此下家优先的顺序是 1 → 2 → 3，座位 2 排在座位 3 前面。
    /// 两家都是平和形（座位 3 还带断幺九），无役不可和不会把它们挡在外面。
    let doubleRonScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 5s 8s 6p"
                    "1z 1z 2z 2z 3z 3z 4z 4z 5z 6z 7z 9p 9s"
                    "2s 3s 4s 6s 7s 8s 3m 4m 5m 9m 9m 2p 3p"
                    "2m 3m 4m 6m 7m 8m 3s 4s 5s 7s 7s 5p 6p"
                ]
            Draws = "6z 7z 9p 9s 4p"
        }

    /// 无役的剧本：座位 2 听 2m 嵌张，但那手牌荣和时**一个役都没有**
    /// （不成平和——嵌张听；不成断幺九——带 1m 与 1z；三色 / 一气 / 混全带都不成）。
    ///
    /// 座位 0 第 2 巡摸进 2m 打出去：座位 2 既不振听、牌型也成立，但无役不可和，
    /// Ron 压根不进合法动作集；随后座位 2 自己第 2 巡摸进 2m，门前清自摸就是役，于是能和。
    let noYakuRonScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 5s 8s 6p"
                    "9m 9m 9p 9p 1s 1s 6z 6z 7z 7z 2z 3z 4z"
                    "1m 3m 5p 6p 7p 3s 4s 5s 7s 8s 9s 1z 1z"
                    "1p 2p 3p 4p 1m 4m 7m 6s 6s 2s 3z 4z 5z"
                ]
            Draws = "9s 9p 8p 8m 2m 9p 2m"
        }

    // ---- 杠的剧本（KanTests / KanProperties 共用） ----

    /// 暗杠的剧本：Oya 手里三张 9s 加一条 123456789m 与 5z 单骑，第一次自摸就摸进第四张 9s。
    /// 岭上牌摊的是 `5z`，因此这一杠直接岭上开花（暗杠不破门清，还有门前清自摸和与一气通贯）。
    let ankanScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 9s 9s 9s 5z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 4z 6z 7z 2m"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 4z 6z 7z 3m"
                    "3p 6p 9p 3s 6s 8s 1z 2z 3z 4z 6z 7z 4m"
                ]
            Draws = "9s"
        }

    /// 立直后暗杠的剧本：Oya 第一手摸进 6z，宣立直打 1z（听 6z 单骑）；
    /// 三家各摸切一张之后，Oya 第二手摸进第四张 5z——听牌与面子构成都不变，杠得。
    let riichiAnkanScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 5z 5z 5z 1z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 6z 7z 2m 8m"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 6z 7z 3m 8m"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 6z 7z 4m 8m"
                ]
            Draws = "6z 4z 4z 4z 5z"
        }

    /// 送り杠的剧本：Oya 立直时手里就握着四张 1p（`111p + 123p`），听 9s 单骑。
    /// 之后摸进的不是 1p，因此那四张**杠不得**——禁送り杠。
    let okuriKanScript =
        {
            Hands =
                [
                    "1p 1p 1p 1p 2p 3p 4m 5m 6m 7m 8m 9m 9s"
                    "4p 7p 1s 4s 7s 1z 2z 3z 6z 7z 2m 8m 9p"
                    "5p 8p 2s 5s 8s 1z 2z 3z 6z 7z 3m 8m 9p"
                    "6p 9p 3s 6s 9s 1z 2z 3z 6z 7z 4m 8m 1m"
                ]
            Draws = "3z 4z 4z 4z 5z"
        }

    /// 大明杠的剧本：Oya 第一手摸切 5s，座位 1 手里三张 5s 全亮出去大明杠；
    /// 岭上牌摊的是 `1z`，正好给它配成雀头——大明杠后的岭上开花，责任支付落在 Oya 头上。
    let minkanScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 6s 8s 6p"
                    "5s 5s 5sr 1m 2m 3m 4m 5m 6m 7m 8m 9m 1z"
                    "2p 5p 8p 2s 8s 9s 2z 3z 4z 6z 7z 3m 4p"
                    "3p 6p 9p 3s 6s 9s 2z 3z 4z 6z 7z 4m 7p"
                ]
            Draws = "5s"
        }

    /// 加杠与抢杠的剧本：Oya 第一手摸切 5s，座位 1 碰它；一圈之后座位 1 摸进第四张 5s 加杠。
    ///
    /// 座位 2 一开局就听 5s / 8s（一气通贯 + 平和）：它先见逃了那张 5s（只是同巡振听，
    /// 摸过一张就解除），因此后来抢得了那个加杠。
    let kakanScript =
        {
            Hands =
                [
                    "5z 6z 7z 2m 4m 8m 1s 4s 7s 3p 4p 7p 9p"
                    "5s 5sr 1m 2m 3m 4m 5m 6m 7m 8m 9m 1z 1z"
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 6s 7s 9p 9p"
                    "2p 5p 8p 2s 3s 9s 1z 2z 3z 4z 6z 7z 6p"
                ]
            Draws = "5s 4z 4z 4z 5s"
        }

    /// 国士抢暗杠的剧本：座位 1 第一手摸进第四张 7z 暗杠，而座位 2 是等 7z 的国士无双。
    /// 能不能抢看规则集（`KokushiAnkanChankan`：天凤禁、雀魂允）。
    let kokushiChankanScript =
        {
            Hands =
                [
                    "4m 5m 6m 7m 8m 9m 2p 3p 6p 9p 2s 3s 4s"
                    "7z 7z 7z 2m 3m 4m 5m 6m 7m 2p 3p 4p 5p"
                    "1m 1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z"
                    "2m 3m 5s 6s 7s 8s 5p 6p 7p 8p 4s 3s 2s"
                ]
            Draws = "3z 7z"
        }

    /// 三个杠的剧本（四杠前的那一个）：Oya 手里 111m 222m 333m，摸进第四张 1m 暗杠，
    /// 补摸的岭上牌又恰好是 2m、再下一张是 3m——一口气三个暗杠。
    /// 四杠散了是 12 票的，本票只验到第三个杠为止。
    let threeKanScript =
        {
            Hands =
                [
                    "1m 1m 1m 2m 2m 2m 3m 3m 3m 9p 9p 5z 6z"
                    "4m 5m 6m 1p 4p 7p 1s 4s 7s 1z 2z 3z 4z"
                    "7m 8m 9m 2p 5p 8p 2s 5s 8s 1z 2z 3z 4z"
                    "4m 5m 7m 3p 6p 9p 3s 6s 9s 1z 2z 3z 4z"
                ]
            Draws = "1m"
        }

    /// 杠撞车的剧本：Oya 第一手摸切 5s，三家各有所图——
    /// 座位 1（下家）拿 `6s 7s` 吃得，座位 2 手里三张 5s 碰得也大明杠得，
    /// 座位 3 听 5s / 8s 荣和得（一气通贯 + 平和）。优先级 Ron > Pon / Minkan > Chi 的两条边都在这一手上。
    let kanRaceScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 8s 6p 4p"
                    "6s 7s 1p 4p 7p 1s 4s 7s 1z 2z 3z 6z 7z"
                    "5s 5s 5sr 2p 5p 8p 2s 9s 3m 6z 7z 4z 4z"
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 6s 7s 9p 9p"
                ]
            Draws = "5s"
        }

    /// 大三元包的剧本：座位 1 碰完三组三元牌，**第三组（中）是座位 2 点的**，
    /// 因此它要为这个大三元负责；随后座位 1 自摸 9p 和了。
    let daisangenScript =
        {
            Hands =
                [
                    "1p 2p 3p 4p 5p 6p 7p 8p 1s 2s 3s 4m 5m"
                    "5z 5z 6z 6z 7z 7z 1m 2m 3m 9p 1z 2z 3z"
                    "2p 5p 8p 6s 7s 8s 6m 7m 8m 1z 2z 3z 9s"
                    "3p 6p 9p 3s 4s 5s 4m 7m 9m 1z 2z 3z 1s"
                ]
            Draws = "5z 6z 7z 4z 4z 4z 9p"
        }

    /// 某座位摸到第几巡了：它的 `tsumo` 事件条数。引擎的 `GameState.junme` 算的是同一件事，
    /// 这里从事件流重新数一遍——两条路径对上才算数对。
    let junmeOf (seat: Seat) (state: GameState) : int =
        GameState.events state
        |> List.filter (fun event ->
            match event with
            | Tsumo(actor, _) -> actor = seat
            | StartGame _
            | StartKyoku _
            | Dahai _
            | Pon _
            | Chi _
            | Hora _
            | Ryuukyoku _
            | Riichi _
            | RiichiAccepted _
            | EndKyoku
            | Ankan _
            | Kakan _
            | Minkan _
            | Dora _
            | EndGame -> false)
        |> List.length

    /// 这一局里成立的立直根数：`reach_accepted` 的条数。一根立直棒对应一条事件，
    /// 真实牌谱重建点数用的也是这条式子（上一局点数 − 1000 × reach_accepted + Σdeltas）。
    let acceptedRiichiCount (state: GameState) : int =
        GameState.events state
        |> List.filter (fun event ->
            match event with
            | RiichiAccepted _ -> true
            | StartGame _
            | StartKyoku _
            | Tsumo _
            | Dahai _
            | Pon _
            | Chi _
            | Riichi _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | Ankan _
            | Kakan _
            | Minkan _
            | Dora _
            | EndGame -> false)
        |> List.length

    /// 某座位的副露。
    let nakiOf (seat: Seat) (state: GameState) : Naki list =
        GameState.player seat state
        |> Option.map PlayerState.naki
        |> Option.defaultValue []

    let shantenOf (tiles: Tile list) : int =
        match HandShape.create 0 tiles with
        | Ok shape -> Shanten.value (Shanten.calculate kindSet shape)
        | Error _ -> System.Int32.MaxValue

    /// 尽量保持听牌的选手：打出后向听数最小的那张（并列取合法动作集里靠前的那条）。
    /// 随机选手几乎永远不听牌，听牌料的黄金用例要它。
    ///
    /// **它从不宣言和了、响应一律「过」**：听牌料的那几条黄金用例要求这一局必然打到荒牌流局。
    let tenpaiSeeking: Player<Rng> =
        fun rng state choice ->
            let hand =
                GameState.player choice.Seat state
                |> Option.map PlayerState.hand
                |> Option.defaultValue []

            let dahais =
                choice.Actions
                |> List.choose (fun action ->
                    match action with
                    | Action.Dahai(_, pai, _) -> Some(action, pai)
                    | Action.Hora _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.None _ -> None)

            let chosen =
                match dahais with
                | [] -> Action.None choice.Seat
                | _ -> dahais |> List.minBy (fun (_, pai) -> shantenOf (removeOne pai hand)) |> fst

            chosen, rng

    /// 见和就和的选手：能自摸和 / 荣和就宣言，否则摸切，响应阶段不和就「过」。
    /// 和了那几条黄金用例要它——随机选手几乎永远和不了。
    let horaSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Hora _ -> true
                    | Action.Dahai _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 摸切选手：一律摸切，响应一律「过」（**也从不鸣牌**），**从不宣言和了**。
    /// 振听与同巡振听的黄金用例要它——见逃是它的默认行为。
    /// 鸣牌一律打手切那一手（鸣完没摸切可打），因此它在鸣牌之后也推得动局面。
    let passive: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                    | Action.Hora _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 能立直就立直的选手：能和先和，否则能立直就宣言，接下来优先摸切（宣言牌只能从
    /// 听牌形里挑，摸切不在其中时取第一条手切），响应一律「过」。
    ///
    /// 随机与听牌选手都从不宣言立直，而一发、立直棒与供托守恒这几条不变量非得有立直的轨迹不可。
    let riichiSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Hora _ -> true
                    | Action.Dahai _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Riichi _ -> true
                        | Action.Dahai _
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai _ -> true
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 见鸣就鸣的选手：能碰就碰、能吃就吃（碰优先），其余时候打合法动作集里第一条打牌，
    /// **从不宣言和了**。鸣牌的不变量要一条副露密集的轨迹，随机选手鸣得太稀。
    let nakiSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Pon _ -> true
                    | Action.Chi _
                    | Action.Dahai _
                    | Action.Hora _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Chi _ -> true
                        | Action.Pon _
                        | Action.Dahai _
                        | Action.Hora _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai _ -> true
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Hora _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 见杠就杠的选手：能杠就杠（暗杠 / 加杠 / 大明杠），能和就和（抢杠与岭上开花靠它），
    /// 其余时候摸切，响应一律「过」。杠的不变量要一条杠密集的轨迹，
    /// 随机选手杠得太稀（四个种子里慕得还不到一个）。
    let kanSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _ -> true
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Dahai _
                    | Action.Hora _
                    | Action.Riichi _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Hora _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Riichi _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Riichi _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Riichi _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 用给定选手推进，直到 `until` 成立或这一局终了。黄金用例用它跑到「有事发生」的那一步，
    /// 再自己提交那一手关键动作。
    let driveUntil (player: Player<Rng>) (until: GameState -> bool) (state: GameState) : GameState =
        let rec loop (rng: Rng) (state: GameState) =
            if until state then
                state
            else
                match GameState.legalActions state with
                | [] -> state
                | choice :: _ ->
                    let action, advanced = player rng state choice

                    match GameState.step state action with
                    | Error illegal -> failwith $"合法动作集里的动作应当被接受，却得到「{IllegalAction.toDisplay illegal}」"
                    | Ok(next, _) -> loop advanced next

        loop (Rng.ofSeed 1) state

    /// 从给定局面到终局逐步的全部局面，含首尾。不变量在每一步上验。
    let traceFrom (player: Player<Rng>) (rng: Rng) (state: GameState) : GameState list =
        let rec loop (rng: Rng) (state: GameState) (visited: GameState list) =
            match GameState.legalActions state with
            | [] -> List.rev (state :: visited)
            | choice :: _ ->
                let action, advanced = player rng state choice

                match GameState.step state action with
                | Error illegal -> failwith $"合法动作集里的动作应当被接受，却得到「{IllegalAction.toDisplay illegal}」"
                | Ok(next, _) -> loop advanced next (state :: visited)

        loop rng state []

    /// 一局从开局到终局逐步的全部局面，含开局与终局。
    let trace (player: Player<Rng>) (seed: int) : GameState list =
        let state, rng = start seed
        traceFrom player rng state

    /// 一局**必然出现杠**的全部局面：摊好的杠剧本（连岭上牌一起摊）+ 见杠就杠的选手。
    ///
    /// **光靠随机取样验不到杠**：见杠就杠的选手跑四个种子也只有一局杠得成
    /// （杠要手里四张同种，概率就那么小）。这是备注 N-8 的同一课：
    /// 没断言过覆盖率的属性只证明了没崩，没证明跑到过。
    let kanTrace (rinshan: string) (script: Script) : GameState list =
        traceFrom kanSeeking (Rng.ofSeed 1) (startScriptedRinshan rinshan script)

    /// 一局**以和了收尾**的全部局面。随机选手几乎永远和不了（扫过 400 个种子，一次都没有），
    /// 可达局面里必须有一批和了的，牌数守恒与手牌张数这些不变量才验得到和了这条路。
    let horaTrace (script: Script) : GameState list =
        traceFrom horaSeeking (Rng.ofSeed 1) (startScripted script)

    /// 把一局跑完，返回终局的局面。
    let runWith (player: Player<Rng>) (seed: int) : GameState =
        let state, rng = start seed

        match Kyoku.run player rng state with
        | Ok(final, _) -> final
        | Error illegal -> failwith $"这一局应当跑得完，却得到「{IllegalAction.toDisplay illegal}」"

    /// 跑一局，同时记下每一步提交的动作——回放要的就是这串动作。
    let record (player: Player<Rng>) (seed: int) : GameState * Action list =
        let rec loop (rng: Rng) (state: GameState) (taken: Action list) =
            match GameState.legalActions state with
            | [] -> state, List.rev taken
            | choice :: _ ->
                let action, advanced = player rng state choice

                match GameState.step state action with
                | Error illegal -> failwith $"合法动作集里的动作应当被接受，却得到「{IllegalAction.toDisplay illegal}」"
                | Ok(next, _) -> loop advanced next (action :: taken)

        let state, rng = start seed
        loop rng state []

    /// 回放：把一串动作从给定局面重新走一遍。
    let replay (actions: Action list) (state: GameState) : Result<GameState, IllegalAction> =
        (Ok state, actions)
        ||> List.fold (fun current action ->
            current
            |> Result.bind (fun state -> GameState.step state action |> Result.map fst))

    /// 终局的流局结果。
    let ryuukyokuOf (state: GameState) : Ryuukyoku =
        match GameState.ryuukyoku state with
        | Some result -> result
        | None -> failwith "这一局应当已经终了"

/// 局面与动作的生成器。局面只生成**可达**的那些：随机开一局、随机走若干步。
type GameStateArbitraries =

    static member GameState() : Arbitrary<GameState> =
        gen {
            let! seed = Gen.choose (1, 400)

            let! states =
                Gen.frequency
                    [
                        4, Gen.constant (GameStateFixtures.trace Kyoku.randomPlayer seed)
                        4, Gen.constant (GameStateFixtures.trace GameStateFixtures.tenpaiSeeking seed)
                        // 副露密集的一局：鸣牌的不变量（牌数守恒、手牌张数、河被鸣走的记号）靠它验。
                        4, Gen.constant (GameStateFixtures.trace GameStateFixtures.nakiSeeking seed)
                        // 杠密集的一局：王牌、岭上牌与新宝牌的不变量靠它验。
                        4, Gen.constant (GameStateFixtures.trace GameStateFixtures.kanSeeking seed)
                        // 摊好的三局：一局三个暗杠、一局大明杠后岭上开花、一局暗杠后岭上开花。
                        // 随机取样里杠太稀，杠的不变量非得有这几条轨迹不可。
                        2, Gen.constant (GameStateFixtures.kanTrace "2m 3m 7z" GameStateFixtures.threeKanScript)
                        1, Gen.constant (GameStateFixtures.kanTrace "1z" GameStateFixtures.minkanScript)
                        1, Gen.constant (GameStateFixtures.kanTrace "5z" GameStateFixtures.ankanScript)
                        // 立直密集的一局：立直棒、供托守恒与「立直后只能摸切」靠它验。
                        4, Gen.constant (GameStateFixtures.trace GameStateFixtures.riichiSeeking seed)
                        // 摊好的两局：一局自摸和收尾、一局荣和收尾。
                        1, Gen.constant (GameStateFixtures.horaTrace GameStateFixtures.tsumoHoraScript)
                        1, Gen.constant (GameStateFixtures.horaTrace GameStateFixtures.doubleRonScript)
                    ]

            let! index = Gen.choose (0, List.length states - 1)
            return List.item index states
        }
        |> Arb.fromGen

    /// 随便造的动作：座位可能越界，牌可能不在手里，摸切标志可能是瞎写的，
    /// 和了可能既不成型也不是刚打出的那张。
    /// 「非法动作一律返回值而不抛异常」「接受一个动作当且仅当它在合法动作集里」两条属性要的就是它。
    static member Action() : Arbitrary<Action> =
        let seat = Gen.choose (-1, Ruleset.yonma.SeatCount)

        let dahai =
            gen {
                let! actor = seat
                let! pai = Gen.elements Tile.all
                let! tsumogiri = Gen.elements [ true; false ]
                return Action.Dahai(actor, pai, tsumogiri)
            }

        let hora =
            gen {
                let! actor = seat
                let! target = seat
                let! pai = Gen.elements Tile.all
                return Action.Hora(actor, target, pai)
            }

        let naki =
            gen {
                let! actor = seat
                let! target = seat
                let! pai = Gen.elements Tile.all
                let! consumed = Gen.listOfLength 2 (Gen.elements Tile.all)
                let! isPon = Gen.elements [ true; false ]

                return
                    if isPon then
                        Action.Pon(actor, target, pai, consumed)
                    else
                        Action.Chi(actor, target, pai, consumed)
            }

        let kan =
            gen {
                let! actor = seat
                let! target = seat
                let! pai = Gen.elements Tile.all
                let! consumed = Gen.listOfLength 3 (Gen.elements Tile.all)
                let! kind = Gen.elements [ 0; 1; 2 ]

                return
                    match kind with
                    | 0 -> Action.Ankan(actor, pai :: consumed)
                    | 1 -> Action.Kakan(actor, pai, consumed)
                    | _ -> Action.Minkan(actor, target, pai, consumed)
            }

        let riichi = seat |> Gen.map Action.Riichi
        let pass = seat |> Gen.map Action.None

        Gen.oneof [ dahai; hora; naki; kan; riichi; pass ] |> Arb.fromGen
