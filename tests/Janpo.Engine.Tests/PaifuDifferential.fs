namespace Janpo.Engine.Tests

open System
open System.IO
open Janpo

/// 一局对拍的产物：差异加上覆盖统计。**统计和差异一样重要**——
/// 「对拍绿了」若没有覆盖率佐证，只说明没跑到（备注 N-8 的教训）。
type KyokuDifferential =
    {
        Label: string
        Diffs: PaifuDiff list
        /// 对拍过的和了次数。
        Horas: int
        /// 比到符的次数（満貫以上天凤不写符，那几次比不了）。
        FuChecks: int
        /// 比过的役行数（**不含**三种宝牌）。
        YakuChecks: int
        /// oracle 里出现过的役，去重前。役名对照表的覆盖率读它。
        YakuSeen: Yaku list
        /// **独立锚点**：逐座位对拍过天凤清算的荒牌流局座位数。
        SettledSeats: int
        /// 拿下一局的开局点数对拍过本局终局点数的局数（一场的最后一局没下局，比不了）。
        ScoreChecks: int
        /// 引擎判出的流局形态；和了收尾时为 None。
        Reason: RyuukyokuReason option
        /// 这一局有没有天凤 JSON oracle（没有就只比动作序列与钱流）。
        HasOracle: bool
    }

/// 一批牌谱对拍的产物。
type PaifuDifferential =
    {
        Games: int
        Kyokus: KyokuDifferential list
        /// 显式跳过的样本：(定位, 原因)。**计数，不静默丢弃**。
        Skipped: (string * string) list
    }

/// 牌谱对拍：把重放的结果与天凤官方 JSON 的 oracle 逐项比。
///
/// 两个数据源各管一半（D-7 前置任务的实证结论）：**动作序列**从 mjai 牌谱取，
/// **役 / 符 / 番 / 点数 / 流局形态**从天凤 JSON 取。两者按
/// `(bakaze, kyoku, honba, kyotaku)` 与 `deltas` 对齐。
[<RequireQualifiedAccess>]
module PaifuDifferential =

    /// 牌谱的规则集：天凤鳳凰卓 `鳳南喰赤`（`gm-00a9`）。
    ///
    /// **默认规则集已经对齐天凤**（ADR-0004 决定 3 与晨间裁决 R-1：双响成立、无切上满贯、
    /// 连风牌雀头 4 符、三家和了流局、赤 5 各一张、食断有），因此这里只把长度换成半庄。
    /// 「默认规则集就是天凤那一套」这条断言由 `PaifuDifferentialTests` 钉住——**不假定，验它**。
    let ruleset: Ruleset = Ruleset.yonma |> Ruleset.withLength Hanchan

    // ---- 对齐 ----

    let private diff (label: string) (kind: string) (detail: string) : PaifuDiff =
        {
            Where = label
            Kind = kind
            Detail = detail
        }

    /// 一局在整场里的序号，与天凤 JSON 的 `log[k][0][0]` 同一算法：
    /// 东 1 局是 0、南 1 局是 4（四麻）。
    let private kyokuIndex (seatCount: int) (start: PaifuStartKyoku) : int =
        Kaze.index start.Bakaze * seatCount + start.Kyoku - 1

    /// mjai 流的一局与天凤 JSON 的一局对得上吗。对不上就不是同一局，比下去没有意义。
    let private aligned (seatCount: int) (kyoku: PaifuKyoku) (oracle: OracleKyoku) : Result<unit, string> =
        let expected =
            kyokuIndex seatCount kyoku.Start, kyoku.Start.Honba, kyoku.Start.Kyotaku

        let actual = oracle.Index, oracle.Honba, oracle.Kyotaku

        if expected = actual then
            Ok()
        else
            Error $"mjai 流与 oracle 对不齐：mjai {expected}，oracle {actual}"

    // ---- 和了的对拍 ----

    let private renderYaku (yaku: Yaku, value: YakuValue) : string =
        let name = OracleYaku.japanese yaku

        match value with
        | YakuValue.Han han -> $"{name}({han}飜)"
        | YakuValue.Yakuman 1 -> $"{name}(役満)"
        | YakuValue.Yakuman multiplier -> $"{name}({multiplier}倍役満)"

    let private renderYakuList (yaku: (Yaku * YakuValue) list) : string =
        yaku |> List.map renderYaku |> List.sort |> String.concat " "

    /// 一处标量的比对。相等就没有差异。
    let private compareValue
        (label: string)
        (kind: string)
        (what: string)
        (expected: 'a)
        (actual: 'a)
        : PaifuDiff list =
        if expected = actual then
            []
        else
            [ diff label kind $"{what}：天凤 {expected}，引擎 {actual}" ]

    /// 一次和了：役种集合、符、番、和了点。**役与宝牌只在读法里**（`Hora` 事件不带役），
    /// 因此比的是重放时提前读下来的那一份。
    let private compareHora (label: string) (observation: HoraObservation) (oracle: OracleHora) : PaifuDiff list =
        let hora = observation.Hora
        let tally = observation.Reading.Tally
        let where = $"{label} 座位 {Seat.index hora.Actor}"

        let fan =
            if oracle.Yakuman > 0 then
                Some(13 * oracle.Yakuman)
            else
                oracle.Han

        [
            yield! compareValue where "和了者" "和了的座位" (Seat.index oracle.Actor) (Seat.index hora.Actor)
            yield! compareValue where "和了者" "放铳的座位" (Seat.index oracle.Target) (Seat.index hora.Target)
            // 満貫以上天凤不写符，那几次比不了。
            yield!
                (match oracle.Fu with
                 | Some fu -> compareValue where "符" "符" fu hora.Fu
                 | None -> [])
            yield!
                (match fan with
                 | Some fan -> compareValue where "番" "番（含宝牌）" fan hora.Fan
                 | None -> [])
            yield! compareValue where "和了点" "和了点" oracle.HoraPoints hora.HoraPoints
            yield! compareValue where "役" "役种集合" (renderYakuList oracle.Yaku) (renderYakuList tally.Yaku)
            // **役满时不比宝牌**：天凤那时根本不列宝牌行（役满不看番也不加宝牌），
            // 而我们的 `YakuTally` 照数不误——两边都对，只是天凤不写。点数不受影响（`HoraValue.ofTally`
            // 在役满时把 `Han` 置 0），因此和了点那一条仍然把它盖住了。
            yield!
                (if oracle.Yakuman > 0 then
                     []
                 else
                     [
                         yield! compareValue where "宝牌" "表宝牌" oracle.Dora tally.Dora
                         yield! compareValue where "宝牌" "里宝牌" oracle.Uradora tally.Uradora
                         yield! compareValue where "宝牌" "赤宝牌" oracle.Akadora tally.Akadora
                     ])
        ]

    // ---- 流局的对拍 ----

    /// 一次流局：形态与**逐座位的听牌判定**。后者就是本票的独立锚点——
    /// 期望值来自天凤自己的钱流（谁收听牌料谁听牌），与我们的实现无因果关系。
    let private compareRyuukyoku
        (label: string)
        (result: Ryuukyoku)
        (reason: RyuukyokuReason option)
        (tenpais: bool list option)
        : PaifuDiff list * int =
        let form =
            match reason with
            | Some expected ->
                compareValue label "流局形态" "形态" (RyuukyokuReason.toMjai expected) (RyuukyokuReason.toMjai result.Reason)
            | None -> []

        match tenpais with
        | None -> form, 0
        | Some expected when List.length expected <> List.length result.Tenpais ->
            form
            @ [
                diff label "清算" $"听牌判定有 {List.length result.Tenpais} 家，天凤给了 {List.length expected} 家"
            ],
            0
        | Some expected ->
            let rendered (values: bool list) =
                values
                |> List.map (fun tenpai -> if tenpai then "听" else "不听")
                |> String.concat ""

            let seats =
                List.zip expected result.Tenpais
                |> List.indexed
                |> List.filter (fun (_, (one, other)) -> one <> other)
                |> List.map (fun (seat, (one, other)) -> diff label "清算" $"座位 {seat} 的听牌判定：天凤 {one}，引擎 {other}")

            form
            @ (if List.isEmpty seats then
                   []
               else
                   seats
                   @ [ diff label "清算" $"整局：天凤 {rendered expected}，引擎 {rendered result.Tenpais}" ]),
            List.length expected

    // ---- 一局 ----

    /// **每局终局点数**：本局打完之后的各家点数，应当逐项等于牌谱里**下一局**的
    /// `start_kyoku.scores`。这一条不循环：本局的开局点数是喂进去的，而终局点数是
    /// 引擎自己算的（立直棒 + 和了 / 听牌料授受 + 供托归属），拿牌谱的下一局去对。
    let private compareCarry (label: string) (replay: KyokuReplay) (next: PaifuKyoku option) : PaifuDiff list * int =
        match next with
        | None -> [], 0
        | Some next ->
            let rendered (scores: int list) =
                scores |> List.map string |> String.concat ","

            (compareValue label "终局点数" "本局打完的各家点数" (rendered next.Start.Scores) (rendered replay.Scores)), 1

    /// 只有 mjai 流时的听牌期望值：从 `ryukyoku.deltas` 反推。
    /// 流し満貫与全 0 都反推不出来，那时是 None（**跳过并计数**，不猜）。
    let private tenpaisFromPaifu (kyoku: PaifuKyoku) : bool list option =
        kyoku.Moves
        |> List.tryPick PaifuEvent.ryuukyokuDeltas
        |> Option.bind PaifuOracle.tenpaisFromDeltas

    let private empty (label: string) (hasOracle: bool) (diffs: PaifuDiff list) : KyokuDifferential =
        {
            Label = label
            Diffs = diffs
            Horas = 0
            FuChecks = 0
            YakuChecks = 0
            YakuSeen = []
            SettledSeats = 0
            ScoreChecks = 0
            Reason = None
            HasOracle = hasOracle
        }

    /// 把重放结果与 oracle 比。`oracle` 为 None 时只有动作序列与钱流被比过
    /// （事件流对拍已经在重放里做完），役 / 符 / 点数那几项没有期望值可比。
    let private compare (label: string) (replay: KyokuReplay) (oracle: OracleResult option) : KyokuDifferential =
        let baseline = empty label (Option.isSome oracle) replay.Diffs

        match replay.Ryuukyoku, oracle with
        | Some result, Some(OracleResult.Ryuukyoku expected) ->
            let diffs, seats =
                compareRyuukyoku label result (Some expected.Reason) expected.Tenpais

            { baseline with
                Diffs = baseline.Diffs @ diffs
                SettledSeats = seats
                Reason = Some result.Reason
            }
        | Some result, None ->
            { baseline with
                Reason = Some result.Reason
            }
        | Some result, Some(OracleResult.Hora _) ->
            { baseline with
                Diffs =
                    baseline.Diffs
                    @ [
                        diff label "流局形态" $"天凤这一局是和了，引擎判成了流局（{RyuukyokuReason.toMjai result.Reason}）"
                    ]
                Reason = Some result.Reason
            }
        | None, Some(OracleResult.Ryuukyoku expected) ->
            { baseline with
                Diffs =
                    baseline.Diffs
                    @ [ diff label "流局形态" $"天凤这一局是 {RyuukyokuReason.toMjai expected.Reason}，引擎判成了和了" ]
            }
        | None, Some(OracleResult.Hora expected) when List.length expected <> List.length replay.Horas ->
            { baseline with
                Diffs =
                    baseline.Diffs
                    @ [
                        diff label "和了者" $"天凤有 {List.length expected} 次和了，引擎有 {List.length replay.Horas} 次"
                    ]
            }
        | None, Some(OracleResult.Hora expected) ->
            let paired = List.zip replay.Horas expected

            { baseline with
                Diffs =
                    baseline.Diffs
                    @ (paired |> List.collect (fun (observed, each) -> compareHora label observed each))
                Horas = List.length paired
                FuChecks = paired |> List.filter (fun (_, each) -> Option.isSome each.Fu) |> List.length
                YakuChecks = paired |> List.sumBy (fun (_, each) -> List.length each.Yaku)
                YakuSeen = paired |> List.collect (fun (_, each) -> each.Yaku |> List.map fst)
                Reason = None
            }
        | None, None ->
            { baseline with
                Horas = List.length replay.Horas
            }

    // ---- 入口 ----

    /// 对拍一整场：逐局重建牌山、重放、与 oracle 比。
    /// `oracle` 为 None（只有 mjai 流的大样本）时仍然跑动作序列与钱流的对拍。
    let game (paifu: PaifuGame) (oracle: OracleGame option) : KyokuDifferential list * (string * string) list =
        let oracleKyokus = oracle |> Option.map (fun each -> each.Kyokus)

        match oracleKyokus with
        | Some kyokus when List.length kyokus <> List.length paifu.Kyokus ->
            [],
            [
                paifu.Id, $"mjai 有 {List.length paifu.Kyokus} 局，oracle 有 {List.length kyokus} 局"
            ]
        | _ ->
            ((([], []): KyokuDifferential list * (string * string) list), paifu.Kyokus)
            ||> List.fold (fun (compared, skipped) kyoku ->
                let start = kyoku.Start

                let label =
                    $"{paifu.Id} 第 {kyoku.Index + 1} 局（{Kaze.toMjai start.Bakaze}{start.Kyoku}局 {start.Honba} 本场）"

                let matching =
                    oracleKyokus |> Option.bind (fun kyokus -> List.tryItem kyoku.Index kyokus)

                let misaligned =
                    matching
                    |> Option.map (aligned ruleset.SeatCount kyoku)
                    |> Option.defaultValue (Ok())

                match misaligned with
                | Error reason -> compared, skipped @ [ label, reason ]
                | Ok() ->
                    match PaifuReplay.kyoku ruleset label kyoku with
                    | Skipped reason -> compared, skipped @ [ label, reason ]
                    | Replayed replay ->
                        let result = matching |> Option.map (fun each -> each.Result)

                        let carried, checks =
                            compareCarry label replay (List.tryItem (kyoku.Index + 1) paifu.Kyokus)

                        let differential =
                            let compared = compare label replay result

                            { compared with
                                Diffs = compared.Diffs @ carried
                                ScoreChecks = checks
                            }

                        // 只有 mjai 流时，荒牌流局的听牌期望值从 `ryukyoku.deltas` 反推——
                        // 那同样是天凤自己的钱流，独立锚点因此不依赖 oracle。
                        let anchored =
                            match result, replay.Ryuukyoku with
                            | None, Some ryuukyoku ->
                                let diffs, seats = compareRyuukyoku label ryuukyoku None (tenpaisFromPaifu kyoku)

                                { differential with
                                    Diffs = differential.Diffs @ diffs
                                    SettledSeats = seats
                                }
                            | None, None
                            | Some _, Some _
                            | Some _, None -> differential

                        compared @ [ anchored ], skipped)

    /// 对拍一批牌谱。
    let run (games: (PaifuGame * OracleGame option) list) : PaifuDifferential =
        let compared, skipped =
            (([], []), games)
            ||> List.fold (fun (kyokus, skipped) (paifu, oracle) ->
                let one, missed = game paifu oracle
                kyokus @ one, skipped @ missed)

        {
            Games = List.length games
            Kyokus = compared
            Skipped = skipped
        }

/// 对拍用的牌谱语料：默认是随测试工程复制到输出目录的 18 局固件。
///
/// **样本量可通过环境变量 `JANPO_PAIFU_DIR` 扩大，不改代码**（本票的验收）：
/// 指向一个含 `mjai/*.mjson` 的目录即可，同级的 `tenhou/<id>.json` 有就当 oracle 用、
/// 没有就只对拍动作序列与钱流（独立锚点仍然成立——它读的是 mjai 自己的 `deltas`）。
[<RequireQualifiedAccess>]
module PaifuCorpus =

    let private fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures", "paifu")

    /// 语料目录。环境变量没设就是随工程分发的固件。
    let directory: string =
        match Environment.GetEnvironmentVariable "JANPO_PAIFU_DIR" with
        | null -> fixtures
        | "" -> fixtures
        | custom -> custom

    /// 载入语料。**载不进来的样本显式跳过并计数**，不静默丢弃。
    let load () : (PaifuGame * OracleGame option) list * (string * string) list =
        let mjai = Path.Combine(directory, "mjai")

        if not (Directory.Exists mjai) then
            [], [ directory, "语料目录下没有 mjai/ 子目录" ]
        else
            Directory.GetFiles(mjai, "*.mjson")
            |> Array.sort
            |> Array.toList
            |> List.fold
                (fun (loaded, skipped) path ->
                    let id = Path.GetFileNameWithoutExtension path

                    match PaifuGame.parse id (File.ReadLines path) with
                    | Error reason -> loaded, skipped @ [ id, reason ]
                    | Ok paifu ->
                        let oraclePath = Path.Combine(directory, "tenhou", id + ".json")

                        if not (File.Exists oraclePath) then
                            loaded @ [ paifu, None ], skipped
                        else
                            match
                                PaifuOracle.parse PaifuDifferential.ruleset.SeatCount id (File.ReadAllText oraclePath)
                            with
                            | Error reason -> loaded, skipped @ [ id, reason ]
                            | Ok oracle -> loaded @ [ paifu, Some oracle ], skipped)
                ([], [])
