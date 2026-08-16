namespace Janpo

/// 牌山：一局开始时洗好、并已分好王牌的整座山。
///
/// 王牌的分法照日麻的实际摆法：末尾一段单独留出，其中一端的若干张是岭上牌，
/// 相邻的每一叠是「表宝牌指示牌（上）+ 里宝牌指示牌（下）」。开局翻开第一叠的表宝牌指示牌，
/// 之后每杠一次多翻一叠。指示牌与岭上牌的位置在 `build` 时就定死，之后不必再带着规则集跑。
type Wall =
    private
        {
            /// 可摸区，首元素就是下一张。
            Live: Tile list
            /// 岭上牌，按摸取顺序。
            Rinshan: Tile list
            /// 王牌里的指示牌叠：（表宝牌指示牌，里宝牌指示牌）。
            Indicators: (Tile * Tile) list
            /// 已翻开的叠数。开局为 1。
            Revealed: int
        }

/// 牌山的构成、摸牌、配牌与宝牌指示牌。
[<RequireQualifiedAccess>]
module Wall =

    // ---- 构造 ----

    let private pairUp (tiles: Tile list) : (Tile * Tile) list =
        tiles
        |> List.chunkBySize 2
        |> List.choose (fun chunk ->
            match chunk with
            | [ dora; ura ] -> Some(dora, ura)
            | _ -> None)

    /// 把一列已经排好序的牌摊成一座牌山：开头是可摸区（首元素就是下一张），
    /// 末尾 `DeadWallSize` 张是王牌。张数与构成由调用方负责，这里不校验。
    ///
    /// 库外拿不到（`internal`）：它是测试用的牌山构造入口（黄金用例要「指定和了在指定
    /// Junme 发生」），生产代码只能经 `build` 洗出牌山。
    let internal ofOrdered (ruleset: Ruleset) (tiles: Tile list) : Wall =
        let deadSize = tiles |> List.length |> min (max 0 ruleset.DeadWallSize)
        let liveSize = List.length tiles - deadSize
        let dead = List.skip liveSize tiles
        let rinshanCount = min (max 0 ruleset.RinshanCount) deadSize

        {
            Live = List.truncate liveSize tiles
            Rinshan = List.truncate rinshanCount dead
            Indicators = dead |> List.skip rinshanCount |> pairUp
            Revealed = 1
        }

    /// 按规则集构成牌山、用发生器洗牌、留出王牌，并翻开第一张表宝牌指示牌。
    /// 张数一律取自规则集，这里没有 136 / 14 之类的字面量。
    let build (ruleset: Ruleset) (rng: Rng) : Wall * Rng =
        let shuffled, advanced = Rng.shuffle (Ruleset.wallTiles ruleset) rng
        ofOrdered ruleset shuffled, advanced

    // ---- 拆解 ----

    /// 可摸区剩余张数。荒牌流局的判据就是它归零。
    let remaining (wall: Wall) : int = List.length wall.Live

    /// 岭上牌，按摸取顺序。
    let rinshan (wall: Wall) : Tile list = wall.Rinshan

    /// 王牌全部张数（岭上 + 表里指示牌），按摆放顺序。
    let deadWall (wall: Wall) : Tile list =
        wall.Rinshan
        @ (wall.Indicators |> List.collect (fun (dora, ura) -> [ dora; ura ]))

    /// 山上还在的全部牌：**先可摸区（摸牌顺序）、再王牌**。
    /// 牌数守恒的属性测试从这里取；前 `remaining` 张就是接下来会被摸到的那些牌。
    let tiles (wall: Wall) : Tile list = wall.Live @ deadWall wall

    /// 已翻开的表宝牌指示牌，翻开顺序。
    let doraIndicators (wall: Wall) : Tile list =
        wall.Indicators |> List.truncate wall.Revealed |> List.map fst

    /// 已翻开的表宝牌指示牌对应的里宝牌指示牌，翻开顺序。立直和了时才公开。
    let uraIndicators (wall: Wall) : Tile list =
        wall.Indicators |> List.truncate wall.Revealed |> List.map snd

    // ---- 摸牌与配牌 ----

    /// 从可摸区摸一张；摸完了返回 None（荒牌流局）。
    let draw (wall: Wall) : (Tile * Wall) option =
        match wall.Live with
        | [] -> None
        | head :: tail -> Some(head, { wall with Live = tail })

    let rec private drawMany (count: int) (wall: Wall) : (Tile list * Wall) option =
        if count <= 0 then
            Some([], wall)
        else
            match draw wall with
            | None -> None
            | Some(drawn, rest) ->
                drawMany (count - 1) rest
                |> Option.map (fun (others, afterOthers) -> drawn :: others, afterOthers)

    /// 配牌的取牌手顺：从 Oya 起按下家方向，每家先取几轮 4 张，最后一轮每家取余数张
    /// （13 张 = 4 + 4 + 4 + 1，日麻的通行手顺）。
    let private haipaiChunks (ruleset: Ruleset) : int list =
        let size = max 0 ruleset.HaipaiSize
        let perRound = 4
        let rounds = size / perRound
        let rest = size % perRound
        [ for _ in 1..rounds -> perRound ] @ (if rest > 0 then [ rest ] else [])

    /// 配牌：给每家发 `HaipaiSize` 张，返回按座位升序排列的手牌（每手已按 mjai 顺序排序）。
    /// 牌不够发、或 Oya 不是合法座位时返回 None。
    let deal (ruleset: Ruleset) (oya: Seat) (wall: Wall) : (Tile list list * Wall) option =
        if not (Seat.isValid ruleset oya) then
            None
        else
            let plan =
                [
                    for chunk in haipaiChunks ruleset do
                        for seat in Seat.orderFrom ruleset oya -> seat, chunk
                ]

            (Some(Map.empty, wall), plan)
            ||> List.fold (fun state (seat, count) ->
                state
                |> Option.bind (fun (hands: Map<Seat, Tile list>, current) ->
                    drawMany count current
                    |> Option.map (fun (drawn, rest) ->
                        let held = hands |> Map.tryFind seat |> Option.defaultValue []
                        Map.add seat (held @ drawn) hands, rest)))
            |> Option.map (fun (hands, rest) ->
                let ordered =
                    Seat.all ruleset
                    |> List.map (fun seat -> hands |> Map.tryFind seat |> Option.defaultValue [] |> Tile.sort)

                ordered, rest)

    // ---- 宝牌指示牌与岭上牌 ----

    /// 多翻一叠表宝牌指示牌（杠）：给出**新翻开的那张**表指示牌与翻开之后的牌山。
    /// 指示牌用尽时返回 None，牌山原样——新宝牌是一个事件（mjai `dora`），
    /// 「翻了但没有新的那张」表示不出来才对。
    let reveal (wall: Wall) : (Tile * Wall) option =
        if wall.Revealed >= List.length wall.Indicators then
            None
        else
            let revealed =
                { wall with
                    Revealed = wall.Revealed + 1
                }

            doraIndicators revealed
            |> List.tryLast
            |> Option.map (fun marker -> marker, revealed)

    /// 杠之后从王牌补摸一张岭上牌。**可摸区的最后一张同时补进王牌**：王牌恒为
    /// `DeadWallSize` 张，可摸区因此每杠少一张——这就是日麻实际摆法里「杠一次，
    /// 海底往前挪一张」。
    ///
    /// 可摸区空了（补不进王牌）或岭上牌用尽时返回 None：那时杠不成立。
    let drawRinshan (wall: Wall) : (Tile * Wall) option =
        match wall.Rinshan, List.rev wall.Live with
        | drawn :: rest, last :: reversedLive ->
            Some(
                drawn,
                { wall with
                    Live = List.rev reversedLive
                    Rinshan = rest @ [ last ]
                }
            )
        | _ -> None
