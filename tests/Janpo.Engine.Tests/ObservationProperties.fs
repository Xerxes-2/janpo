namespace Janpo.Engine.Tests

open System.Text.RegularExpressions
open Xunit
open FsCheck.Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 观测投影的不变量：**看得见的牌一张不多**。局面取自可达局面（随机开一局再随机走若干步），
/// 每条属性对局面里的每个座位各验一遍。
///
/// 「他家暗牌看不见」在**类型层面**已经成立（`MaskedSeat` 没有那个字段），
/// 这里的属性是佐证不是保障：它挡的是往后有人给投影加字段时顺手把暗信息带出去。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |], Parallelism = 4)>]
module ObservationProperties =

    let private observationsOf (state: GameState) : (Seat * Observation) list =
        Seat.all ruleset
        |> List.choose (fun seat -> Observation.ofState seat state |> Option.map (fun each -> seat, each))

    /// 这一刻**在牌桌上亮着**的全部牌，按张数计：自家暗牌（含刚摸进那张）、各家的河、
    /// 各家的副露与已翻开的表宝牌指示牌。牌山、王牌、里宝牌与他家暗牌都不在其中。
    ///
    /// 副露里被鸣的那张同时算在打牌者的河里，刚摸进那张同时算在手牌里——**两边都重复数**，
    /// 因此下面的「子多重集」比较仍然是对的（投影那侧也照样重复）。
    let private visibleTo (seat: Seat) (state: GameState) : Tile list =
        let own =
            match GameState.player seat state with
            | Some player -> PlayerState.hand player @ Option.toList (PlayerState.drawn player)
            | None -> []

        let onTable =
            GameState.players state
            |> List.collect (fun player ->
                PlayerState.kawa player @ (PlayerState.naki player |> List.collect Naki.tiles))

        own @ onTable @ Wall.doraIndicators (GameState.wall state)

    /// 观测这份记录里出现的全部牌，按张数计（与 `visibleTo` 逐项对称）。
    let private tilesIn (observation: Observation) : Tile list =
        let kawa (entries: KawaEntry list) =
            entries |> List.map (fun entry -> entry.Pai)

        let naki (melds: Naki list) = melds |> List.collect Naki.tiles

        observation.Self.Hand
        @ Option.toList observation.Self.Drawn
        @ kawa observation.Self.Kawa
        @ naki observation.Self.Naki
        @ (observation.Others
           |> List.collect (fun other -> kawa other.Kawa @ naki other.Naki))
        @ observation.DoraMarkers

    /// `small` 的每一张都在 `large` 里拿得到（按张数，不是按牌种）。
    let private isSubMultisetOf (large: Tile list) (small: Tile list) : bool =
        (Some large, small)
        ||> List.fold (fun rest tile ->
            rest
            |> Option.bind (fun tiles ->
                if List.contains tile tiles then
                    Some(removeOne tile tiles)
                else
                    None))
        |> Option.isSome

    [<Property>]
    let ``任意局面任意座位，观测里的每一张牌都是这个座位看得见的`` (state: GameState) =
        observationsOf state
        |> List.forall (fun (seat, observation) -> tilesIn observation |> isSubMultisetOf (visibleTo seat state))

    /// 序列化结果里出现的全部牌记法。
    ///
    /// **先把风的那两个字段抹掉**：场风与自风的 wire 也是牌记法（ADR-0001 写作 `1z`-`4z`），
    /// 不抹掉的话四种风牌恒在允许集里，风牌这一路就等于没验。
    /// 牌记法在 wire 上恒是一个完整的 JSON 字符串，因此连引号一起找：`"5m"` 不会误配进 `"5mr"`。
    let private notationsIn (json: string) : Set<string> =
        let withoutKaze = Regex.Replace(json, "\"(bakaze|jikaze)\":\"[0-9]z\"", "")

        Regex.Matches(withoutKaze, "\"([0-9][mpsz]r?)\"")
        |> Seq.map (fun each -> each.Groups.[1].Value)
        |> Set.ofSeq

    [<Property>]
    let ``任意局面任意座位，观测的序列化结果里不出现他家暗牌里的牌`` (state: GameState) =
        observationsOf state
        |> List.forall (fun (seat, observation) ->
            let allowed = visibleTo seat state |> List.map Tile.toMjai |> Set.ofList
            let encoded = Encode.toString 0 (Observation.encoder observation)
            Set.isSubset (notationsIn encoded) allowed)

    [<Property>]
    let ``任意局面任意座位，观测的河与那一家的河逐张一致`` (state: GameState) =
        observationsOf state
        |> List.forall (fun (seat, observation) ->
            // `None` 那一支**不是兜底而是失败支**（票 114）：四个座位在任何局面上都取得到那一家，
            // 静静地当他没河的话，「他家的河逐张一致」在座位丢了的那一刻会**假绿**
            // （两边都是空河）。它零次是「这一趟绿的」的必然结果，不再是「有断言没人守」。
            let kawaOf (target: Seat) =
                match GameState.player target state with
                | Some player -> PlayerState.kawa player
                | None -> failwith $"{target} 这一席在任何局面上都取得到，取不到就是局面自己坏了"

            let selfMatches =
                observation.Self.Kawa |> List.map (fun entry -> entry.Pai) = kawaOf seat

            let othersMatch =
                observation.Others
                |> List.forall (fun other -> other.Kawa |> List.map (fun entry -> entry.Pai) = kawaOf other.Seat)

            selfMatches && othersMatch)

    /// 张数是公开信息（牌桌上每家手里摸得出来手里有几张），因此遮蔽之后仍然给得出。
    /// **它是遮蔽不是计算**：读的就是那一家实际的张数，不是按副露数推算的
    /// （摸牌那一手多一张，推算版本的 13 - 3×副露会差一张）。
    [<Property>]
    let ``任意局面任意座位，他家的手牌张数与那一家实际的一致`` (state: GameState) =
        observationsOf state
        |> List.forall (fun (_, observation) ->
            observation.Others
            |> List.forall (fun other ->
                match GameState.player other.Seat state with
                | Some player -> other.HandCount = List.length (PlayerState.hand player)
                | None -> false))

    // ---- 回归守卫： fold 出来的观测 vs 引擎的权威状态 ----

    [<Property>]
    let ``任意局面任意座位，掩蔽流 fold 出来的观测与引擎的状态逐字段一致`` (state: GameState) =
        observationsOf state
        |> List.collect (fun (seat, observation) -> ObservationFixtures.mismatches seat state observation)
        |> List.isEmpty

    /// 见逃密集的轨迹上再验一遍：同巡振听与立直后见逃的永久振听只在那批轨迹里出现，
    /// 而振听恰恰是「引擎知道的」与「座席的历史推得出的」最容易分家的那个字段。
    [<Property(Arbitrary = [| typeof<MinogashiArbitraries> |])>]
    let ``见逃密集的局面上，掩蔽流 fold 出来的观测与引擎的状态仍逐字段一致`` (state: GameState) =
        observationsOf state
        |> List.collect (fun (seat, observation) -> ObservationFixtures.mismatches seat state observation)
        |> List.isEmpty

    [<Property>]
    let ``任意局面，上帝视角亮出每一家的暗牌`` (state: GameState) =
        let view = GodView.ofState state

        view.Seats |> List.map (fun each -> each.Hand) = (GameState.players state |> List.map PlayerState.hand)
        && view.UraMarkers = Wall.uraIndicators (GameState.wall state)

    // ---- 抢杠那个窗口的定点锚点（票 99）----

    /// **上面每一条属性的随机部分都到不了抢杠那个窗口**（`GameStateArbitraries` 的全域里
    /// `ResponseCause.Kan` 的局面是 0 个，票 96 / 97 / 98 三把尺子量出同一个数）——
    /// 而 fold 恰恰在那一段与引擎分过家：`kakan` 一播出去 fold 就把那组碰原地升成了杠、
    /// 把那张牌从手里拿走，而引擎要等杠成立才动；被抢之后那个杠永远不成立，
    /// **观测那份再也没回滚**（票 98 §4 第四类，票 99 修掉）。
    ///
    /// 因此这一族在那个窗口里的闸门是下面这条定点锚点：**跑的就是上面那几条属性的
    /// 函数本身**，不另写一份（另写一份就会各自飘）。它红过：修 fold 之前，
    /// 三条轨迹上共 20 步破了「fold 与引擎一致」（原文在报告 `99-chankan-window-observation.md`）。
    [<Fact>]
    let ``抢杠那个窗口：摊好牌山的那几条轨迹逐步，观测的不变量都成立`` () =
        ChankanFixtures.sweep
            ChankanFixtures.traces
            [
                "看得见的牌", ``任意局面任意座位，观测里的每一张牌都是这个座位看得见的``
                "序列化不漏暗牌", ``任意局面任意座位，观测的序列化结果里不出现他家暗牌里的牌``
                "河一致", ``任意局面任意座位，观测的河与那一家的河逐张一致``
                "他家手牌张数", ``任意局面任意座位，他家的手牌张数与那一家实际的一致``
                "fold 与引擎一致", ``任意局面任意座位，掩蔽流 fold 出来的观测与引擎的状态逐字段一致``
                "上帝视角", ``任意局面，上帝视角亮出每一家的暗牌``
            ]
