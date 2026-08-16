namespace Janpo.Golden

open Thoth.Json.Core
open Janpo

/// 一条用例跑出来的一个字段：字段名 + 逐行的值。标量就是一行。
///
/// **对照以行为单位**，所以报错落得到「哪个用例的哪个字段的第几行」——
/// 一整场对局的九百多条事件是一个字段的九百多行，漂了指得出是第几条。
type GoldenField = { Name: string; Lines: string list }

/// 把一条用例跑成一串字段。**这里是两个目标共用的那段代码**：
/// dotnet 侧与 Fable 侧跑的是同一个 `run`，因此两侧的差异只可能来自编译器，不可能来自驱动。
///
/// 一律不抛异常：解析不了、算不出来都变成一条 `error` 字段，于是「错误文案本身」
/// 也被钉住（字符串语义同样是这一票要防的漂移之一）。
[<RequireQualifiedAccess>]
module GoldenObservation =

    // ---- 字段的构造 ----

    let private scalar (name: string) (value: string) : GoldenField = { Name = name; Lines = [ value ] }

    let private lines (name: string) (values: string list) : GoldenField = { Name = name; Lines = values }

    let private number (name: string) (value: int) : GoldenField = value |> string |> scalar name

    let private integers (name: string) (values: int list) : GoldenField =
        values |> List.map string |> String.concat " " |> scalar name

    /// 一串中文 / mjai 片段拼成一行。空表就是空串——「一个都没有」也是一种要钉住的输出。
    let private joined (name: string) (values: string list) : GoldenField =
        values |> String.concat " " |> scalar name

    let private tileList (name: string) (values: Tile list) : GoldenField =
        values |> Tile.toMjaiMany |> scalar name

    let private failure (message: string) : GoldenField list = [ scalar "error" message ]

    // ---- 共用的解析 ----

    /// 「<副露数> <记法>...」→ 手牌形态。与 CLI 的 `janpo shanten` 同一形态。
    let private parseHand (nakiCount: int) (notation: string) : Result<HandShape, string> =
        match Tile.parseMany notation with
        | Error error -> Error(TileListParseError.toDisplay error)
        | Ok tiles -> HandShape.create nakiCount tiles |> Result.mapError HandShapeError.toDisplay

    let private parseTiles (notations: string list) : Result<Tile list, string> =
        (Ok [], notations)
        ||> List.fold (fun state notation ->
            state
            |> Result.bind (fun parsed ->
                Tile.parse notation
                |> Result.map (fun tile -> parsed @ [ tile ])
                |> Result.mapError TileParseError.toDisplay))

    // ---- Rng 与牌山 ----

    /// 连取 `count` 个数。写法照搬 `RngTests` 里那条钉住取数序列的用例。
    let private drawSequence (bound: int) (count: int) (rng: Rng) : int list =
        (([], rng), [ 1..count ])
        ||> List.fold (fun (drawn, current) _ ->
            let value, advanced = Rng.nextBelow bound current
            value :: drawn, advanced)
        |> fst
        |> List.rev

    let private rngFields (seed: int) (bound: int) (count: int) : GoldenField list =
        let seeded = Rng.ofSeed seed

        [
            integers "draws" (seeded |> drawSequence bound count)
            integers "shuffle" (Rng.shuffle [ 1..bound ] seeded |> fst |> List.truncate count)
        ]

    let private wallFields (ruleset: Ruleset) (seed: int) (count: int) : GoldenField list =
        let wall = Wall.build ruleset (Rng.ofSeed seed) |> fst

        let head =
            [
                // 可摸区在前，因此这就是接下来会被摸到的头几张（`Wall.tiles` 的约定）。
                tileList "live" (Wall.tiles wall |> List.truncate count)
                tileList "dora" (Wall.doraIndicators wall)
                tileList "ura" (Wall.uraIndicators wall)
                tileList "rinshan" (Wall.rinshan wall)
                number "remaining" (Wall.remaining wall)
            ]

        match Wall.deal ruleset Seat.first wall with
        | None -> head @ failure "这个规则集发不出配牌"
        | Some(hands, rest) ->
            head
            @ [
                lines "haipai" (hands |> List.map Tile.toMjaiMany)
                number "remaining-after-deal" (Wall.remaining rest)
            ]

    // ---- 记法与集合 ----

    let private notationFields (notation: string) : GoldenField list =
        match Tile.parseMany notation with
        | Error error -> failure (TileListParseError.toDisplay error)
        | Ok parsed ->
            let sorted = Tile.sort parsed

            let roundtrip =
                match sorted |> Tile.toMjaiMany |> Tile.canonicalize with
                | Ok canonical -> canonical
                | Error error -> TileListParseError.toDisplay error

            [
                tileList "mjai" sorted
                number "count" (List.length sorted)
                joined "display" (sorted |> List.map Tile.toDisplay)
                integers "kinds" (sorted |> List.map Tile.kindIndex)
                scalar "roundtrip" roundtrip
            ]

    /// 集合与 Map 的遍历顺序。**这几行的值全部由「牌的结构化比较」决定**，
    /// 而那正是 Fable 自己实现的一段：两个目标一旦对不上，先看这里。
    let private collectionFields (notation: string) : GoldenField list =
        match Tile.parseMany notation with
        | Error error -> failure (TileListParseError.toDisplay error)
        | Ok parsed ->
            let indexed =
                parsed
                |> List.mapi (fun index tile -> tile, index)
                |> Map.ofList
                |> Map.toList
                |> List.map (fun (tile, index) -> $"{Tile.toMjai tile}={index}")

            let displays = parsed |> List.map Tile.toDisplay |> Set.ofList |> Set.toList

            [
                tileList "set" (parsed |> Set.ofList |> Set.toList)
                joined "map" indexed
                tileList "sorted" (List.sort parsed)
                tileList "distinct" (List.distinct parsed)
                // 分组的次序是「首次出现」的次序，不是花色的次序。
                lines "groups" (parsed |> List.groupBy Tile.suit |> List.map (snd >> Tile.toMjaiMany))
                // 中文串进 Set：字符串的比较与去重在两侧必须一致。
                joined "display-set" displays
            ]

    // ---- 手牌形态 ----

    let private shantenFields (ruleset: Ruleset) (nakiCount: int) (notation: string) : GoldenField list =
        match parseHand nakiCount notation with
        | Error message -> failure message
        | Ok hand ->
            let kindSet = ruleset.TileKinds
            let shanten = Shanten.calculate kindSet hand

            let ukeire =
                match Ukeire.calculate kindSet [] hand with
                | Error error -> [ scalar "ukeire" (UkeireError.toDisplay error) ]
                | Ok value ->
                    [
                        scalar "ukeire" (Ukeire.toMjai value)
                        number "ukeire-total" (Ukeire.total value)
                    ]

            let shapes = AgariShape.classify kindSet hand |> List.map AgariShape.toDisplay

            [
                number "shanten" (Shanten.value shanten)
                joined "agari" shapes
                scalar "display" (Shanten.toDisplay shanten)
            ]
            @ ukeire

    // ---- 和了与点数 ----

    /// 副露记法「<种类> <牌>...」。座位只影响 mjai 事件与责任支付，判役用不上，
    /// 因此照 CLI 的约定填示例座位：宣言者坐起家，被鸣的是它的下家，吃只吃上家打的。
    let private parseNaki (ruleset: Ruleset) (text: string) : Result<Naki, string> =
        let shimocha = Seat.shimocha ruleset Seat.first
        let kamicha = Seat.kamicha ruleset Seat.first

        let described (result: Result<Naki, NakiError>) =
            result |> Result.mapError NakiError.toDisplay

        let tokens =
            text.Split([| ' '; '\t'; ',' |])
            |> Array.filter (fun token -> token <> "")
            |> List.ofArray

        match tokens with
        | [] -> Error "副露不能为空"
        | kind :: rest ->
            match Tile.parseMany (String.concat " " rest) with
            | Error error -> Error(TileListParseError.toDisplay error)
            | Ok tiles ->
                match kind, tiles with
                | "pon", taken :: consumed -> described (Naki.pon shimocha taken consumed)
                | "chi", taken :: consumed -> described (Naki.chi kamicha taken consumed)
                | "ankan", _ -> described (Naki.ankan tiles)
                | "minkan", taken :: consumed -> described (Naki.minkan shimocha taken consumed)
                | "kakan", added :: taken :: consumed ->
                    described (Naki.pon shimocha taken consumed |> Result.bind (Naki.kakan added))
                | ("pon" | "chi" | "minkan" | "kakan"), _ -> Error $"「{kind}」后面牌太少"
                | _ -> Error $"未知的副露种类「{kind}」，只有 pon / chi / ankan / minkan / kakan"

    /// 上下文标志。`tsumo` 决定的是 `AgariHand` 怎么构造，不进上下文，这里放行。
    let private withFlag (flag: string) (context: YakuContext) : Result<YakuContext, string> =
        match flag with
        | "tsumo" -> Ok context
        | "riichi" ->
            Ok
                { context with
                    Riichi = RiichiDeclaration.Riichi
                }
        | "double-riichi" ->
            Ok
                { context with
                    Riichi = RiichiDeclaration.DoubleRiichi
                }
        | "ippatsu" -> Ok { context with Ippatsu = true }
        | "rinshan" -> Ok { context with Rinshan = true }
        | "haitei" -> Ok { context with Haitei = true }
        | "houtei" -> Ok { context with Houtei = true }
        | "chankan" -> Ok { context with Chankan = true }
        | "tenhou" -> Ok { context with Tenhou = true }
        | "chiihou" -> Ok { context with Chiihou = true }
        | unknown -> Error $"未知的上下文标志「{unknown}」"

    let private horaContext (spec: GoldenHora) : Result<YakuContext, string> =
        match Kaze.parse spec.Bakaze, Kaze.parse spec.Jikaze with
        | None, _ -> Error $"场风要写成 1z-4z，得到「{spec.Bakaze}」"
        | _, None -> Error $"自风要写成 1z-4z，得到「{spec.Jikaze}」"
        | Some bakaze, Some jikaze ->
            let withMarkers (dora: Tile list) (ura: Tile list) =
                { YakuContext.create bakaze jikaze with
                    DoraMarkers = dora
                    UraDoraMarkers = ura
                }

            let markers =
                parseTiles spec.Dora
                |> Result.bind (fun dora -> parseTiles spec.Ura |> Result.map (withMarkers dora))

            (markers, spec.Flags)
            ||> List.fold (fun state flag -> state |> Result.bind (withFlag flag))

    let private horaHand (ruleset: Ruleset) (spec: GoldenHora) : Result<AgariHand, string> =
        let tsumo = spec.Flags |> List.contains "tsumo"
        let build = if tsumo then AgariHand.tsumo else AgariHand.ron

        (Ok [], spec.Naki)
        ||> List.fold (fun state text ->
            state
            |> Result.bind (fun parsed -> parseNaki ruleset text |> Result.map (fun naki -> parsed @ [ naki ])))
        |> Result.bind (fun naki ->
            match Tile.parse spec.Winning, Tile.parseMany spec.Concealed with
            | Error error, _ -> Error(TileParseError.toDisplay error)
            | _, Error error -> Error(TileListParseError.toDisplay error)
            | Ok winning, Ok concealed -> build naki concealed winning |> Result.mapError AgariHandError.toDisplay)

    /// 授受的座位约定与 `janpo yaku` 相同：和了者固定在座位 0，自风为 `1z` 时它就是亲，
    /// 否则亲是座位 1；荣和的放铳者取座位 1。
    let private horaTransfer (ruleset: Ruleset) (spec: GoldenHora) (context: YakuContext) : HoraTransfer =
        let shimocha = Seat.shimocha ruleset Seat.first
        let tsumo = spec.Flags |> List.contains "tsumo"

        {
            Actor = Seat.first
            Target = (if tsumo then Seat.first else shimocha)
            Oya = (if context.Jikaze = Ton then Seat.first else shimocha)
            Honba = spec.Honba
            Kyotaku = spec.Kyotaku
            Sekinin = None
        }

    let private horaFields (ruleset: Ruleset) (spec: GoldenHora) : GoldenField list =
        let read =
            horaContext spec
            |> Result.bind (fun context ->
                horaHand ruleset spec
                |> Result.bind (fun hand ->
                    // 高点法：`Score.best` 把全部读法各算一遍，取点数最高的那一种。
                    Score.best ruleset context hand
                    |> Result.mapError YakuError.toDisplay
                    |> Result.map (fun reading -> context, reading)))

        match read with
        | Error message -> failure message
        | Ok(context, reading) ->
            let tally = reading.Tally
            let score = Score.hora ruleset (horaTransfer ruleset spec context) reading.Value

            [
                scalar "shape" (AgariShape.toDisplay tally.Shape)
                joined "yaku" (YakuTally.yaku tally |> List.map Yaku.name)
                number "han" (YakuTally.han tally)
                number "yakuman" (YakuTally.yakuman tally)
                scalar "dora" $"{tally.Dora} {tally.Uradora} {tally.Akadora}"
                number "fu" reading.Value.Fu
                number "points" score.HoraPoints
                integers "deltas" score.Deltas
                scalar "display" (HoraScore.toDisplay score)
            ]

    let private isSeat (ruleset: Ruleset) (index: int) : bool =
        Seat.ofIndex index |> Option.exists (Seat.isValid ruleset)

    let private pointsFields (ruleset: Ruleset) (spec: GoldenPoints) : GoldenField list =
        let indices = [ spec.Actor; spec.Target; spec.Oya ] @ Option.toList spec.Sekinin

        if indices |> List.forall (isSeat ruleset) |> not then
            failure "座位序号要落在这个规则集的座位里（四麻 0-3）"
        else
            // 上面验过每个序号都是这个规则集里的座位，`Seat.wrap` 在这里因此是恒等的。
            let seat = Seat.wrap ruleset

            let value =
                {
                    Fu = spec.Fu
                    Han = spec.Han
                    Yakuman = spec.Yakuman
                }

            let transfer =
                {
                    Actor = seat spec.Actor
                    Target = seat spec.Target
                    Oya = seat spec.Oya
                    Honba = spec.Honba
                    Kyotaku = spec.Kyotaku
                    Sekinin = spec.Sekinin |> Option.map seat
                }

            let score = Score.hora ruleset transfer value

            [
                number "base" (Score.basePoints ruleset value)
                scalar "limit" (Limit.toDisplay score.Limit)
                number "points" score.HoraPoints
                integers "deltas" score.Deltas
                scalar "display" (HoraScore.toDisplay score)
            ]

    // ---- 决策包 ----

    /// 把一局推到第 `steps` 手，取那一手的决策包。**这就是跨 F#/TS 边界的那个包**（票 20）：
    /// 23 票的 Agent 层读的是它在**浏览器里**的那份，因此它在两个目标上一字不差才算数。
    /// 走的选手与 `janpo kyoku` 同一个，因此同一种子的第 N 手两边对得上。
    ///
    /// **包是逐字段钉住的，不是整行**（票 28，裁决 21-c）：`GoldenJson.fields` 把它摊成
    /// `package.observation.self.tehai` 这样一条条路径，于是漂了指得出是哪个字段，
    /// 而 29 号票往投影里加一个字段时报错也只多一条（`UnexpectedField`），不会重印整包。
    let private decideFields (ruleset: Ruleset) (seed: int) (steps: int) (seat: int option) : GoldenField list =
        let advanced =
            match GameState.start ruleset (KyokuContext.initial ruleset) (Rng.ofSeed seed) with
            | Error error -> Error(KyokuStartError.toDisplay error)
            | Ok(state, rng) ->
                Kyoku.runSteps steps Kyoku.randomPlayer rng state
                |> Result.map fst
                |> Result.mapError IllegalAction.toDisplay

        match advanced with
        | Error message -> failure message
        | Ok state ->
            let asked = GameState.legalActions state |> List.map (fun choice -> choice.Seat)

            let wanted =
                match seat with
                | Some index -> Seat.ofIndex index
                | None -> List.tryHead asked

            match wanted |> Option.bind (fun seat -> DecisionPackage.forSeat seat state) with
            | None -> failure $"走了 {steps} 手之后这一手没有这家的决策包"
            | Some package ->
                let packageFields =
                    package
                    |> DecisionPackage.encoder
                    |> GoldenJson.fields "package"
                    |> List.map (fun (name, values) -> lines name values)

                integers "asked" (asked |> List.map Seat.index) :: packageFields

    // ---- 一局与一整场 ----

    /// 一局 / 一整场共用的那几行。事件流是一个字段的很多行，逐条对照。
    let private traceFields
        (toText: IEncodable -> string)
        (kyokus: int)
        (events: Event list)
        (scores: int list)
        (juni: int list)
        : GoldenField list =
        [
            number "kyokus" kyokus
            integers "scores" scores
            integers "juni" juni
            number "event-count" (List.length events)
            lines "events" (events |> List.map (Event.encoder >> toText))
        ]

    let private kyokuFields (toText: IEncodable -> string) (ruleset: Ruleset) (seed: int) : GoldenField list =
        let played =
            Rng.ofSeed seed
            |> Kyoku.runRandom ruleset (KyokuContext.initial ruleset)
            |> Result.mapError KyokuError.toDisplay

        match played with
        | Error message -> failure message
        | Ok(state, _) ->
            let scores = GameState.scores state
            // 顺位借 `Game.settle` 排（顺位规则只该有一处实现）。一局打完时场上可能还剩
            // 立直棒，而它要到终局精算才归属：**供托传 0**，于是点数不动，只排名次。
            let juni = (Game.settle ruleset 0 scores).Juni

            traceFields toText 1 (GameState.events state) scores juni

    let private gameFields (toText: IEncodable -> string) (ruleset: Ruleset) (seed: int) : GoldenField list =
        let played =
            Rng.ofSeed seed
            |> Game.runRandom ruleset
            |> Result.mapError KyokuError.toDisplay

        match played with
        | Error message -> failure message
        | Ok(game, _) ->
            match Game.result game with
            | None -> failure "这场对局没有走到终局精算"
            | Some result ->
                traceFields toText (Game.played game |> List.length) (Game.events game) result.Scores result.Juni

    // ---- 跑 ----

    /// 跑一条用例。`toText` 是宿主注入的 JSON 后端（引擎只依赖 `Thoth.Json.Core` 这层抽象，
    /// 具体后端 dotnet 侧是 Newtonsoft、JS 侧是 Thoth.Json.JavaScript）。
    let run (toText: IEncodable -> string) (case: GoldenCase) : GoldenField list =
        let ruleset = GoldenRuleset.value case.Ruleset

        match case.Run with
        | GoldenRun.Rng(seed, bound, count) -> rngFields seed bound count
        | GoldenRun.Wall(seed, count) -> wallFields ruleset seed count
        | GoldenRun.Notation notation -> notationFields notation
        | GoldenRun.Collection notation -> collectionFields notation
        | GoldenRun.Shanten(nakiCount, notation) -> shantenFields ruleset nakiCount notation
        | GoldenRun.Hora hora -> horaFields ruleset hora
        | GoldenRun.Points points -> pointsFields ruleset points
        | GoldenRun.Decide(seed, steps, seat) -> decideFields ruleset seed steps seat
        | GoldenRun.Kyoku seed -> kyokuFields toText ruleset seed
        | GoldenRun.Game seed -> gameFields toText ruleset seed
