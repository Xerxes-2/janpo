namespace Janpo

/// 立直的宣言状态。一发与里宝牌都以它为前提。
[<RequireQualifiedAccess>]
type RiichiDeclaration =
    /// 没立直。
    | None
    /// 立直。
    | Riichi
    /// 两立直：第一巡立直且此前无人鸣牌。
    | DoubleRiichi

/// 役种。SPEC 范围内的全部役与役满，不含古役与地方役。
///
/// `[<RequireQualifiedAccess>]`：case 名（`Chiitoitsu`、`Kokushi`、`Tenhou` …）与
/// `AgariShape` 的型名、与将来的规则集预设名都会撞车，40 个 case 一股脑倒进
/// `Janpo` 命名空间是灾难。一律写 `Yaku.Chiitoitsu`。
///
/// **声明顺序就是输出顺序**：`detect` 产出的役表按这里的顺序排，1 番在前、役满在后。
[<RequireQualifiedAccess>]
type Yaku =
    // ---- 1-2 番：立直与场况 ----
    /// 立直。
    | Riichi
    /// 两立直。
    | DoubleRiichi
    /// 一发。
    | Ippatsu
    /// 门前清自摸和。
    | MenzenTsumo
    /// 平和。
    | Pinfu
    /// 一杯口。
    | Iipeiko
    /// 断幺九。副露时要规则集开着食断。
    | Tanyao
    /// 役牌：三元牌（白发中）的刻子，一种一番。
    | Yakuhai of tile: Tile
    /// 场风牌的刻子。
    | Bakaze of bakaze: Kaze
    /// 自风牌的刻子。连风牌时与 `Bakaze` 同时成立，合计两番。
    | Jikaze of jikaze: Kaze
    /// 海底摸月。
    | Haitei
    /// 河底捞鱼。
    | Houtei
    /// 岭上开花。
    | Rinshan
    /// 抢杠。
    | Chankan
    // ---- 2 番 ----
    /// 混全带幺九。副露 1 番。
    | Chanta
    /// 三色同顺。副露 1 番。
    | SanshokuDoujun
    /// 一气通贯。副露 1 番。
    | Ittsuu
    /// 对对和。
    | Toitoi
    /// 三暗刻。
    | Sanankou
    /// 三色同刻。
    | SanshokuDoukou
    /// 三杠子。
    | Sankantsu
    /// 七对子。
    | Chiitoitsu
    /// 混老头。
    | Honroutou
    /// 小三元。
    | Shousangen
    // ---- 3 番 ----
    /// 混一色。副露 2 番。
    | Honitsu
    /// 纯全带幺九。副露 2 番。
    | Junchan
    /// 二杯口。
    | Ryanpeikou
    // ---- 6 番 ----
    /// 清一色。副露 5 番。
    | Chinitsu
    // ---- 役满 ----
    /// 国士无双。
    | Kokushi
    /// 国士无双十三面待。本版按单倍役满计（见 DECISIONS 07）。
    | KokushiJuusanmen
    /// 四暗刻。
    | Suuankou
    /// 四暗刻单骑。本版按单倍役满计。
    | SuuankouTanki
    /// 大三元。
    | Daisangen
    /// 小四喜。
    | Shousuushii
    /// 大四喜。本版按单倍役满计。
    | Daisuushii
    /// 字一色。
    | Tsuuiisou
    /// 清老头。
    | Chinroutou
    /// 绿一色。
    | Ryuuiisou
    /// 九莲宝灯。
    | Chuuren
    /// 纯正九莲宝灯。本版按单倍役满计。
    | JunseiChuuren
    /// 四杠子。
    | Suukantsu
    /// 天和。
    | Tenhou
    /// 地和。
    | Chiihou

/// 一个役在某手牌里的价值：番，或役满倍数。
[<RequireQualifiedAccess>]
type YakuValue =
    /// 番数。
    | Han of han: int
    /// 役满倍数。本版恒为 1——役满的复合靠多个 `Yaku` 各占一倍，不做双倍役满。
    | Yakuman of multiplier: int

/// 役种判定要的、牌姿之外的上下文。由调用方填：04 填场风自风，09 填立直与一发，
/// 11 填岭上与抢杠，12 / 04 填海底河底，配牌阶段填天和地和。
///
/// 一发、天和、地和这类标志引擎不复算——它们是牌局历史的产物，历史在调用方手里。
type YakuContext =
    {
        /// 场风。
        Bakaze: Kaze
        /// 自风（由座位与局数推导，不是座位的属性）。
        Jikaze: Kaze
        /// 立直的宣言状态。
        Riichi: RiichiDeclaration
        /// 一发：立直后一巡内和了且此间无人鸣牌。没立直时这个标志不生效。
        Ippatsu: bool
        /// 岭上开花：杠后补摸的那张和了。
        Rinshan: bool
        /// 海底摸月：摸最后一张牌自摸。
        Haitei: bool
        /// 河底捞鱼：荣和最后一张打出的牌。
        Houtei: bool
        /// 抢杠：对加杠（规则集允许时也含国士对暗杠）宣言荣和。
        Chankan: bool
        /// 天和：亲的配牌就已和了。
        Tenhou: bool
        /// 地和：子的第一次自摸就和了。
        Chiihou: bool
        /// 表宝牌指示牌，含杠宝牌。
        DoraMarkers: Tile list
        /// 里宝牌指示牌。没立直时不计，调用方可以照传。
        UraDoraMarkers: Tile list
    }

/// 役种判定的结果：役、宝牌番数与选中的面子分解。**不含符与点数**（08 票）。
///
/// 有役满时 `Yaku` 里只剩役满——役满以下的役不再计入。宝牌照数不误，
/// 但役满不加宝牌，那是 08 算点数时的事。
type YakuTally =
    {
        /// 按哪种和了型算的。`Standard` 与 `Breakdown` 非 None 一一对应。
        Shape: AgariShape
        /// 选中的面子分解（番数最高的那一种）；七对子与国士为 None。
        Breakdown: MentsuBreakdown option
        /// 成立的役与各自的价值，按 `Yaku` 的声明顺序。副露降番已计入。
        Yaku: (Yaku * YakuValue) list
        /// 表宝牌的番数。
        Dora: int
        /// 里宝牌的番数；没立直时恒为 0。
        Uradora: int
        /// 红宝牌的番数。
        Akadora: int
    }

/// 判定为不可和的原因。是值，不是异常。
type YakuError =
    /// 牌姿根本不成和了型（`AgariShape.classify` 为空）。
    | NotAgari
    /// 型成立但一个役都没有：不能和（役无し）。宝牌不是役，救不了。
    | NoYaku

/// 役种判定：牌姿 + 上下文 → 役种集合与番数。**纯函数，不含符与点数**。
[<RequireQualifiedAccess>]
module Yaku =

    // ---- 役的番数 ----

    /// 一个役在门清 / 副露下的价值。副露降番（食い下がり）只影响这几个役：
    /// 混全带幺九、三色同顺、一气通贯、混一色、纯全带幺九、清一色。
    let value (menzen: bool) (yaku: Yaku) : YakuValue =
        let kuisagari (closed: int) (opened: int) =
            YakuValue.Han(if menzen then closed else opened)

        match yaku with
        | Yaku.Riichi
        | Yaku.Ippatsu
        | Yaku.MenzenTsumo
        | Yaku.Pinfu
        | Yaku.Iipeiko
        | Yaku.Tanyao
        | Yaku.Yakuhai _
        | Yaku.Bakaze _
        | Yaku.Jikaze _
        | Yaku.Haitei
        | Yaku.Houtei
        | Yaku.Rinshan
        | Yaku.Chankan -> YakuValue.Han 1
        | Yaku.DoubleRiichi
        | Yaku.Toitoi
        | Yaku.Sanankou
        | Yaku.SanshokuDoukou
        | Yaku.Sankantsu
        | Yaku.Chiitoitsu
        | Yaku.Honroutou
        | Yaku.Shousangen -> YakuValue.Han 2
        | Yaku.Chanta
        | Yaku.SanshokuDoujun
        | Yaku.Ittsuu -> kuisagari 2 1
        | Yaku.Honitsu
        | Yaku.Junchan -> kuisagari 3 2
        | Yaku.Ryanpeikou -> YakuValue.Han 3
        | Yaku.Chinitsu -> kuisagari 6 5
        | Yaku.Kokushi
        | Yaku.KokushiJuusanmen
        | Yaku.Suuankou
        | Yaku.SuuankouTanki
        | Yaku.Daisangen
        | Yaku.Shousuushii
        | Yaku.Daisuushii
        | Yaku.Tsuuiisou
        | Yaku.Chinroutou
        | Yaku.Ryuuiisou
        | Yaku.Chuuren
        | Yaku.JunseiChuuren
        | Yaku.Suukantsu
        | Yaku.Tenhou
        | Yaku.Chiihou -> YakuValue.Yakuman 1

    /// 是不是役满。
    let isYakuman (yaku: Yaku) : bool =
        match value true yaku with
        | YakuValue.Yakuman _ -> true
        | YakuValue.Han _ -> false

    /// 是不是门清限定役：副露（暗杠除外）之后不可能成立。
    let isMenzenOnly (yaku: Yaku) : bool =
        match yaku with
        | Yaku.Riichi
        | Yaku.DoubleRiichi
        | Yaku.Ippatsu
        | Yaku.MenzenTsumo
        | Yaku.Pinfu
        | Yaku.Iipeiko
        | Yaku.Chiitoitsu
        | Yaku.Ryanpeikou
        | Yaku.Kokushi
        | Yaku.KokushiJuusanmen
        | Yaku.Suuankou
        | Yaku.SuuankouTanki
        | Yaku.Chuuren
        | Yaku.JunseiChuuren
        | Yaku.Tenhou
        | Yaku.Chiihou -> true
        | _ -> false

    // ---- 牌的谓词 ----

    let private isJihai (tile: Tile) : bool = Tile.suit tile = Jihai

    let private isRoutou (tile: Tile) : bool =
        not (isJihai tile) && (Tile.number tile = 1 || Tile.number tile = 9)

    let private isYaochuu (tile: Tile) : bool = isJihai tile || isRoutou tile

    /// 三元牌：白发中（`5z`-`7z`）。
    let private isSangenpai (tile: Tile) : bool = isJihai tile && Tile.number tile >= 5

    /// 风牌：东南西北（`1z`-`4z`）。
    let private isKazehai (tile: Tile) : bool = isJihai tile && Tile.number tile <= 4

    let private isKaze (kaze: Kaze) (tile: Tile) : bool =
        isJihai tile && Tile.number tile = Kaze.index kaze + 1

    /// 绿一色的牌：`2s` `3s` `4s` `6s` `8s` 与发（`6z`）。
    let private isRyuuiisouTile (tile: Tile) : bool =
        match Tile.suit tile with
        | Souzu -> List.contains (Tile.number tile) [ 2; 3; 4; 6; 8 ]
        | Jihai -> Tile.number tile = 6
        | Manzu
        | Pinzu -> false

    // ---- 场况役（三种和了型共用） ----

    let private contextYaku (context: YakuContext) (hand: AgariHand) : Yaku list =
        let menzen = AgariHand.isMenzen hand
        let tsumo = AgariHand.isTsumo hand

        [
            if menzen then
                match context.Riichi with
                | RiichiDeclaration.Riichi -> Yaku.Riichi
                | RiichiDeclaration.DoubleRiichi -> Yaku.DoubleRiichi
                | RiichiDeclaration.None -> ()

            // 一发以立直为前提：没立直的手牌传进来也不生效。
            if context.Ippatsu && menzen && context.Riichi <> RiichiDeclaration.None then
                Yaku.Ippatsu

            if menzen && tsumo then
                Yaku.MenzenTsumo

            if context.Haitei && tsumo then
                Yaku.Haitei

            if context.Houtei && not tsumo then
                Yaku.Houtei

            if context.Rinshan && tsumo then
                Yaku.Rinshan

            if context.Chankan && not tsumo then
                Yaku.Chankan
        ]

    /// 天和 / 地和。两者都要门清自摸；是亲还是子由调用方判断后填标志。
    let private tenhouChiihou (context: YakuContext) (hand: AgariHand) : Yaku list =
        let firstDraw = AgariHand.isTsumo hand && AgariHand.isMenzen hand

        [
            if context.Tenhou && firstDraw then
                Yaku.Tenhou

            if context.Chiihou && firstDraw then
                Yaku.Chiihou
        ]

    /// 断幺九：全是中张。副露时要规则集开着食断。
    let private tanyao (ruleset: Ruleset) (hand: AgariHand) (tiles: Tile list) : Yaku list =
        [
            if
                (AgariHand.isMenzen hand || ruleset.Kuitan)
                && tiles |> List.forall (isYaochuu >> not)
            then
                Yaku.Tanyao
        ]

    /// 混一色 / 清一色：全部牌落在同一门数牌（加字牌）里。
    let private iisou (tiles: Tile list) : Yaku list =
        let hasJihai = tiles |> List.exists isJihai

        let suits =
            tiles
            |> List.filter (isJihai >> not)
            |> List.map Tile.suit
            |> List.distinct
            |> List.length

        [
            if suits <= 1 && hasJihai then
                Yaku.Honitsu

            if suits = 1 && not hasJihai then
                Yaku.Chinitsu
        ]

    // ---- 一般型 ----

    let private standardYaku
        (ruleset: Ruleset)
        (context: YakuContext)
        (hand: AgariHand)
        (breakdown: MentsuBreakdown)
        : Yaku list =
        let menzen = AgariHand.isMenzen hand
        let tiles = AgariHand.tiles hand |> List.map Tile.deaka
        let blocks = MentsuBreakdown.mentsu breakdown
        let jantou = MentsuBreakdown.jantou breakdown
        let wait = MentsuBreakdown.wait breakdown

        let shuntsu = blocks |> List.filter (fun one -> Mentsu.kind one = Shuntsu)
        let koutsu = blocks |> List.filter (fun one -> Mentsu.kind one <> Shuntsu)
        let kantsu = blocks |> List.filter (fun one -> Mentsu.kind one = Kantsu)
        let ankou = koutsu |> List.filter Mentsu.isConcealed

        let hasStart (suit: Suit) (number: int) (candidates: Mentsu list) =
            candidates
            |> List.exists (fun one -> Tile.suit (Mentsu.tile one) = suit && Tile.number (Mentsu.tile one) = number)

        let sanshoku (candidates: Mentsu list) (numbers: int list) =
            numbers
            |> List.exists (fun number ->
                [ Manzu; Pinzu; Souzu ]
                |> List.forall (fun suit -> hasStart suit number candidates))

        /// 一杯口的组数：同一个顺子出现两次算一组。
        let peikou =
            shuntsu |> List.countBy Mentsu.tile |> List.sumBy (fun (_, count) -> count / 2)

        let sangenKoutsu = koutsu |> List.filter (fun one -> isSangenpai (Mentsu.tile one))
        let kazeKoutsu = koutsu |> List.filter (fun one -> isKazehai (Mentsu.tile one))

        let isYakuhaiTile (tile: Tile) =
            isSangenpai tile || isKaze context.Bakaze tile || isKaze context.Jikaze tile

        /// 每一块（含雀头）都带幺九牌。
        let everyBlockYaochuu =
            isYaochuu jantou
            && blocks |> List.forall (fun one -> Mentsu.tiles one |> List.exists isYaochuu)

        let hasJihai = tiles |> List.exists isJihai

        // 九莲宝灯：门清、一门数牌、`1112345678999` 打底再多一张。
        let numberCounts =
            let counts = Array.zeroCreate 9

            for tile in tiles do
                if not (isJihai tile) then
                    counts.[Tile.number tile - 1] <- counts.[Tile.number tile - 1] + 1

            counts

        let chuurenBase = [| 3; 1; 1; 1; 1; 1; 1; 1; 3 |]

        let isChuuren =
            List.isEmpty (AgariHand.naki hand)
            && not hasJihai
            && (tiles |> List.map Tile.suit |> List.distinct |> List.length) = 1
            && Array.forall2 (fun count baseline -> count >= baseline) numberCounts chuurenBase

        let isJunseiChuuren =
            isChuuren
            && (let winning = Tile.deaka (AgariHand.winningTile hand)
                let remaining = Array.copy numberCounts
                remaining.[Tile.number winning - 1] <- remaining.[Tile.number winning - 1] - 1
                remaining = chuurenBase)

        let yakuman =
            [
                if List.length ankou = 4 then
                    if wait = Tanki then Yaku.SuuankouTanki else Yaku.Suuankou

                if List.length sangenKoutsu = 3 then
                    Yaku.Daisangen

                if List.length kazeKoutsu = 3 && isKazehai jantou then
                    Yaku.Shousuushii

                if List.length kazeKoutsu = 4 then
                    Yaku.Daisuushii

                if tiles |> List.forall isJihai then
                    Yaku.Tsuuiisou

                if tiles |> List.forall isRoutou then
                    Yaku.Chinroutou

                if tiles |> List.forall isRyuuiisouTile then
                    Yaku.Ryuuiisou

                if isChuuren then
                    if isJunseiChuuren then Yaku.JunseiChuuren else Yaku.Chuuren

                if List.length kantsu = 4 then
                    Yaku.Suukantsu

                yield! tenhouChiihou context hand
            ]

        if not (List.isEmpty yakuman) then
            yakuman
        else
            [
                yield! contextYaku context hand

                if
                    menzen
                    && List.length shuntsu = 4
                    && wait = Ryanmen
                    && not (isYakuhaiTile jantou)
                then
                    Yaku.Pinfu

                if menzen && peikou = 1 then
                    Yaku.Iipeiko

                yield! tanyao ruleset hand tiles

                for one in sangenKoutsu do
                    Yaku.Yakuhai(Mentsu.tile one)

                if koutsu |> List.exists (fun one -> isKaze context.Bakaze (Mentsu.tile one)) then
                    Yaku.Bakaze context.Bakaze

                if koutsu |> List.exists (fun one -> isKaze context.Jikaze (Mentsu.tile one)) then
                    Yaku.Jikaze context.Jikaze

                // 混老头（全是幺九牌）没有顺子，此时不再计混全带幺九 / 纯全带幺九。
                if everyBlockYaochuu && not (List.isEmpty shuntsu) then
                    if hasJihai then Yaku.Chanta else Yaku.Junchan

                if sanshoku shuntsu [ 1..7 ] then
                    Yaku.SanshokuDoujun

                if
                    [ Manzu; Pinzu; Souzu ]
                    |> List.exists (fun suit -> [ 1; 4; 7 ] |> List.forall (fun number -> hasStart suit number shuntsu))
                then
                    Yaku.Ittsuu

                if List.isEmpty shuntsu then
                    Yaku.Toitoi

                if List.length ankou = 3 then
                    Yaku.Sanankou

                if sanshoku koutsu [ 1..9 ] then
                    Yaku.SanshokuDoukou

                if List.length kantsu = 3 then
                    Yaku.Sankantsu

                if tiles |> List.forall isYaochuu then
                    Yaku.Honroutou

                if List.length sangenKoutsu = 2 && isSangenpai jantou then
                    Yaku.Shousangen

                yield! iisou tiles

                if menzen && peikou = 2 then
                    Yaku.Ryanpeikou
            ]

    // ---- 七对子与国士 ----

    let private chiitoitsuYaku (ruleset: Ruleset) (context: YakuContext) (hand: AgariHand) : Yaku list =
        let tiles = AgariHand.tiles hand |> List.map Tile.deaka

        let yakuman =
            [
                if tiles |> List.forall isJihai then
                    Yaku.Tsuuiisou

                yield! tenhouChiihou context hand
            ]

        if not (List.isEmpty yakuman) then
            yakuman
        else
            [
                yield! contextYaku context hand
                yield! tanyao ruleset hand tiles
                Yaku.Chiitoitsu

                if tiles |> List.forall isYaochuu then
                    Yaku.Honroutou

                yield! iisou tiles
            ]

    let private kokushiYaku (context: YakuContext) (hand: AgariHand) : Yaku list =
        let winning = Tile.deaka (AgariHand.winningTile hand)

        let isJuusanmen =
            AgariHand.concealed hand
            |> List.filter (fun tile -> Tile.deaka tile = winning)
            |> List.length = 2

        [
            if isJuusanmen then Yaku.KokushiJuusanmen else Yaku.Kokushi

            yield! tenhouChiihou context hand
        ]

    // ---- 结果 ----

    let private tally
        (ruleset: Ruleset)
        (context: YakuContext)
        (hand: AgariHand)
        (shape: AgariShape)
        (breakdown: MentsuBreakdown option)
        : YakuTally =
        let menzen = AgariHand.isMenzen hand

        let yaku =
            match shape, breakdown with
            | Standard, Some one -> standardYaku ruleset context hand one
            | Standard, None -> []
            | Chiitoitsu, _ -> chiitoitsuYaku ruleset context hand
            | Kokushi, _ -> kokushiYaku context hand

        let tiles = AgariHand.tiles hand

        {
            Shape = shape
            Breakdown = breakdown
            Yaku = yaku |> List.map (fun one -> one, value menzen one)
            Dora = Dora.count context.DoraMarkers tiles
            Uradora =
                if context.Riichi = RiichiDeclaration.None then
                    0
                else
                    Dora.count context.UraDoraMarkers tiles
            Akadora = Dora.akadoraCount tiles
        }

    /// 同番时的确定性 tiebreak，逼近高点法的「同番取高符」。
    /// 用的是符里**随分解而变**的三项（面子、雀头、听牌型），不含门清荣和与自摸这类常数项，
    /// 也不含规则集相关的连风牌雀头；真正的符由 08 算，08 可以自己在 `candidates` 上再选一次。
    let private fuLikeness (context: YakuContext) (tally: YakuTally) : int =
        match tally.Breakdown with
        | None -> 0
        | Some breakdown ->
            let mentsuFu =
                MentsuBreakdown.mentsu breakdown
                |> List.sumBy (fun one ->
                    let baseFu =
                        match Mentsu.kind one with
                        | Shuntsu -> 0
                        | Koutsu -> 2
                        | Kantsu -> 8

                    let concealed = if Mentsu.isConcealed one then 2 else 1
                    let yaochuu = if isYaochuu (Mentsu.tile one) then 2 else 1
                    baseFu * concealed * yaochuu)

            let jantou = MentsuBreakdown.jantou breakdown

            let jantouFu =
                if
                    isSangenpai jantou
                    || isKaze context.Bakaze jantou
                    || isKaze context.Jikaze jantou
                then
                    2
                else
                    0

            let waitFu =
                match MentsuBreakdown.wait breakdown with
                | Ryanmen
                | Shanpon -> 0
                | Penchan
                | Kanchan
                | Tanki -> 2

            mentsuFu + jantouFu + waitFu

    /// 这副和了牌的全部读法，各自的役与番数，**从高到低**排好。
    ///
    /// 一手牌常有多种面子分解，二盃口的手还同时是七对子；高点法要求取番数最高的一种，
    /// 因此这里把每一种读法都算一遍再排序。无役的读法不会出现在结果里。
    /// 排序键：役满倍数 → 番数（含宝牌）→ `fuLikeness`。
    let candidates (ruleset: Ruleset) (context: YakuContext) (hand: AgariHand) : YakuTally list =
        let kindSet = TileKindSet.ofKinds ruleset.TileKinds
        let shapes = AgariShape.classify kindSet (AgariHand.toHandShape hand)

        let readings =
            [
                for shape in shapes do
                    match shape with
                    | Standard ->
                        for breakdown in MentsuBreakdown.enumerate hand do
                            shape, Some breakdown
                    | Chiitoitsu
                    | Kokushi -> shape, None
            ]

        readings
        |> List.map (fun (shape, breakdown) -> tally ruleset context hand shape breakdown)
        |> List.filter (fun one -> not (List.isEmpty one.Yaku))
        |> List.sortByDescending (fun one ->
            let yakuman =
                one.Yaku
                |> List.sumBy (fun (_, value) ->
                    match value with
                    | YakuValue.Yakuman multiplier -> multiplier
                    | YakuValue.Han _ -> 0)

            let han =
                one.Yaku
                |> List.sumBy (fun (_, value) ->
                    match value with
                    | YakuValue.Han han -> han
                    | YakuValue.Yakuman _ -> 0)

            yakuman, han + one.Dora + one.Uradora + one.Akadora, fuLikeness context one)

    /// 役种判定：牌姿 + 上下文 → 役种集合与番数，取番数最高的读法。
    ///
    /// **无役即不可和**：型成立不等于能和（`AgariShape` 判的是型），
    /// 断幺九在食断关掉的副露手里不成立，这时返回 `NoYaku`。
    let detect (ruleset: Ruleset) (context: YakuContext) (hand: AgariHand) : Result<YakuTally, YakuError> =
        match candidates ruleset context hand with
        | best :: _ -> Ok best
        | [] ->
            let kindSet = TileKindSet.ofKinds ruleset.TileKinds

            if List.isEmpty (AgariShape.classify kindSet (AgariHand.toHandShape hand)) then
                Error NotAgari
            else
                Error NoYaku

    // ---- 稳定标识符 ----

    /// 稳定的罗马字标识符，供 CLI 输出与牌谱对拍（13 票）使用。
    /// 它**不是渲染层**——中文形式在 `toDisplay`。
    let name (yaku: Yaku) : string =
        match yaku with
        | Yaku.Riichi -> "riichi"
        | Yaku.DoubleRiichi -> "double_riichi"
        | Yaku.Ippatsu -> "ippatsu"
        | Yaku.MenzenTsumo -> "menzen_tsumo"
        | Yaku.Pinfu -> "pinfu"
        | Yaku.Iipeiko -> "iipeiko"
        | Yaku.Tanyao -> "tanyao"
        | Yaku.Yakuhai tile -> "yakuhai:" + Tile.toMjai tile
        | Yaku.Bakaze kaze -> "bakaze:" + Kaze.toMjai kaze
        | Yaku.Jikaze kaze -> "jikaze:" + Kaze.toMjai kaze
        | Yaku.Haitei -> "haitei"
        | Yaku.Houtei -> "houtei"
        | Yaku.Rinshan -> "rinshan"
        | Yaku.Chankan -> "chankan"
        | Yaku.Chanta -> "chanta"
        | Yaku.SanshokuDoujun -> "sanshoku_doujun"
        | Yaku.Ittsuu -> "ittsuu"
        | Yaku.Toitoi -> "toitoi"
        | Yaku.Sanankou -> "sanankou"
        | Yaku.SanshokuDoukou -> "sanshoku_doukou"
        | Yaku.Sankantsu -> "sankantsu"
        | Yaku.Chiitoitsu -> "chiitoitsu"
        | Yaku.Honroutou -> "honroutou"
        | Yaku.Shousangen -> "shousangen"
        | Yaku.Honitsu -> "honitsu"
        | Yaku.Junchan -> "junchan"
        | Yaku.Ryanpeikou -> "ryanpeikou"
        | Yaku.Chinitsu -> "chinitsu"
        | Yaku.Kokushi -> "kokushi"
        | Yaku.KokushiJuusanmen -> "kokushi_juusanmen"
        | Yaku.Suuankou -> "suuankou"
        | Yaku.SuuankouTanki -> "suuankou_tanki"
        | Yaku.Daisangen -> "daisangen"
        | Yaku.Shousuushii -> "shousuushii"
        | Yaku.Daisuushii -> "daisuushii"
        | Yaku.Tsuuiisou -> "tsuuiisou"
        | Yaku.Chinroutou -> "chinroutou"
        | Yaku.Ryuuiisou -> "ryuuiisou"
        | Yaku.Chuuren -> "chuuren"
        | Yaku.JunseiChuuren -> "junsei_chuuren"
        | Yaku.Suukantsu -> "suukantsu"
        | Yaku.Tenhou -> "tenhou"
        | Yaku.Chiihou -> "chiihou"

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (yaku: Yaku) : string =
        match yaku with
        | Yaku.Riichi -> "立直"
        | Yaku.DoubleRiichi -> "两立直"
        | Yaku.Ippatsu -> "一发"
        | Yaku.MenzenTsumo -> "门前清自摸和"
        | Yaku.Pinfu -> "平和"
        | Yaku.Iipeiko -> "一杯口"
        | Yaku.Tanyao -> "断幺九"
        | Yaku.Yakuhai tile -> $"役牌 {Tile.toDisplay tile}"
        | Yaku.Bakaze kaze -> $"场风 {Kaze.toDisplay kaze}"
        | Yaku.Jikaze kaze -> $"自风 {Kaze.toDisplay kaze}"
        | Yaku.Haitei -> "海底摸月"
        | Yaku.Houtei -> "河底捞鱼"
        | Yaku.Rinshan -> "岭上开花"
        | Yaku.Chankan -> "抢杠"
        | Yaku.Chanta -> "混全带幺九"
        | Yaku.SanshokuDoujun -> "三色同顺"
        | Yaku.Ittsuu -> "一气通贯"
        | Yaku.Toitoi -> "对对和"
        | Yaku.Sanankou -> "三暗刻"
        | Yaku.SanshokuDoukou -> "三色同刻"
        | Yaku.Sankantsu -> "三杠子"
        | Yaku.Chiitoitsu -> "七对子"
        | Yaku.Honroutou -> "混老头"
        | Yaku.Shousangen -> "小三元"
        | Yaku.Honitsu -> "混一色"
        | Yaku.Junchan -> "纯全带幺九"
        | Yaku.Ryanpeikou -> "二杯口"
        | Yaku.Chinitsu -> "清一色"
        | Yaku.Kokushi -> "国士无双"
        | Yaku.KokushiJuusanmen -> "国士无双十三面"
        | Yaku.Suuankou -> "四暗刻"
        | Yaku.SuuankouTanki -> "四暗刻单骑"
        | Yaku.Daisangen -> "大三元"
        | Yaku.Shousuushii -> "小四喜"
        | Yaku.Daisuushii -> "大四喜"
        | Yaku.Tsuuiisou -> "字一色"
        | Yaku.Chinroutou -> "清老头"
        | Yaku.Ryuuiisou -> "绿一色"
        | Yaku.Chuuren -> "九莲宝灯"
        | Yaku.JunseiChuuren -> "纯正九莲宝灯"
        | Yaku.Suukantsu -> "四杠子"
        | Yaku.Tenhou -> "天和"
        | Yaku.Chiihou -> "地和"

/// 判定上下文的构造。
[<RequireQualifiedAccess>]
module YakuContext =

    /// 只有场风与自风、其余标志全关、没有宝牌指示牌的上下文。
    /// 调用方在它之上用 `with` 打开需要的标志。
    let create (bakaze: Kaze) (jikaze: Kaze) : YakuContext =
        {
            Bakaze = bakaze
            Jikaze = jikaze
            Riichi = RiichiDeclaration.None
            Ippatsu = false
            Rinshan = false
            Haitei = false
            Houtei = false
            Chankan = false
            Tenhou = false
            Chiihou = false
            DoraMarkers = []
            UraDoraMarkers = []
        }

/// 判定结果的推导量与渲染。
[<RequireQualifiedAccess>]
module YakuTally =

    /// 役的番数合计，**不含宝牌**。役满时为 0。
    let han (tally: YakuTally) : int =
        tally.Yaku
        |> List.sumBy (fun (_, value) ->
            match value with
            | YakuValue.Han han -> han
            | YakuValue.Yakuman _ -> 0)

    /// 役满的倍数合计。0 表示不是役满；役满复合时是各役倍数之和。
    let yakuman (tally: YakuTally) : int =
        tally.Yaku
        |> List.sumBy (fun (_, value) ->
            match value with
            | YakuValue.Yakuman multiplier -> multiplier
            | YakuValue.Han _ -> 0)

    /// 宝牌合计：表 + 里 + 红。它计番但不是役。
    let doraTotal (tally: YakuTally) : int =
        tally.Dora + tally.Uradora + tally.Akadora

    /// 成立的役，不带番数。
    let yaku (tally: YakuTally) : Yaku list = tally.Yaku |> List.map fst

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (tally: YakuTally) : string =
        let yaku =
            tally.Yaku
            |> List.map (fun (one, value) ->
                match value with
                | YakuValue.Han han -> $"{Yaku.toDisplay one} {han} 番"
                | YakuValue.Yakuman multiplier when multiplier = 1 -> $"{Yaku.toDisplay one} 役满"
                | YakuValue.Yakuman multiplier -> $"{Yaku.toDisplay one} {multiplier} 倍役满")
            |> String.concat "、"

        if yakuman tally > 0 then
            yaku
        else
            let dora = doraTotal tally
            if dora = 0 then yaku else $"{yaku}、宝牌 {dora} 番"

/// 判定失败的渲染。
[<RequireQualifiedAccess>]
module YakuError =

    /// **渲染层的单向出口**：错误原因的中文说明，只供 CLI 与 UI 提示使用。
    let toDisplay (error: YakuError) : string =
        match error with
        | NotAgari -> "牌型不成和了"
        | NoYaku -> "无役，不能和"
