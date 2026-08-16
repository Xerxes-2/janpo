namespace Janpo

/// 流局的判据（CONTEXT.md 的 Ryuukyoku）：**纯函数**，输入这一局的事实，输出「是不是就此终了、
/// 为什么」与「怎么清算」。状态机怎么走是 `GameState` 的事，这里一个字段都不改。
///
/// 荒牌流局的判据不在这里——它就是「可摸区摸完」，`Wall.remaining` 一句话说完，
/// 而听牌料的授受与听牌判定同属 `GameState`（04 已落地）。本模块补的是余下的六种：
/// 四类途中流局、三家和了与流し満貫。
///
/// `ModuleSuffix`：同名的 `type Ryuukyoku`（mjai `ryukyoku` 的载荷）在 `Event.fs`，
/// 与本模块**不在同一个文件**——F# 只在同一文件里自动加后缀（`Tile` / `Seat` 那些都是），
/// 跨文件时要手写。叫法不变（`Ryuukyoku.nagashiSeats`），只是 IL 里多一个 `Module` 后缀。
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ryuukyoku =

    // ---- 牌的判据 ----

    /// 幺九牌：老头牌（数牌的 1 与 9）与字牌。`Yaku` 与 `Fu` 里各有一份同样的私有谓词，
    /// `GameState` 里也有半份（三元牌 / 风牌）——这几个一行函数不值得让一层去依赖另一层的内部
    /// （见 DECISIONS 08 与 11 的同一取舍）。红宝牌是 5，不可能是幺九牌，因此不必先去红。
    let private isYaochuu (tile: Tile) : bool =
        Tile.suit tile = Jihai || Tile.number tile = 1 || Tile.number tile = 9

    /// 风牌（东南西北）。三元牌不算——四风连打只认四种风牌。
    let private isKazehai (tile: Tile) : bool =
        Tile.suit tile = Jihai && Tile.number tile <= 4

    // ---- 九种九牌 ----

    /// 手里有几**种**幺九牌。九种九牌只数种数：种数达标时张数必然也达标（每种至少一张）。
    let yaochuuKinds (hand: Tile list) : int =
        hand
        |> List.filter isYaochuu
        |> List.map Tile.kindIndex
        |> List.distinct
        |> List.length

    /// 九种九牌宣言得了吗：**第一巡、此前无人鸣牌**的那一次自摸之后，手里的幺九牌够
    /// `Ruleset.KyuushuKinds` 种。`firstTurn` 由调用方给（`GameState` 里那条「这家还没打过牌
    /// 且此前无人鸣牌」的判据，天和 / 地和与两立直读的是同一份，不在这里再写一遍）。
    ///
    /// **它是唯一一个可以不宣言的流局**（通行规则；宣言与否是选手的决策），
    /// 因此它进合法动作集，而其余五种由引擎自己判。
    let canDeclareKyuushuKyuuhai (ruleset: Ruleset) (firstTurn: bool) (hand: Tile list) : bool =
        firstTurn && yaochuuKinds hand >= ruleset.KyuushuKinds

    // ---- 打牌落定之后的三种途中流局 ----

    /// 四家立直：四家的立直都**已经成立**（宣言了还没落定的不算——立直棒还没出）。
    let private isSuuchaRiichi (players: PlayerState list) : bool =
        players |> List.forall (PlayerState.riichi >> RiichiState.isAccepted)

    /// 四风连打：第一巡里四家各打了同一张风牌，且**无人鸣牌**。
    ///
    /// 「第一巡」不必另外记：四家各只打过一张，本来就是第一巡。
    /// 无人鸣牌那一条是**照规则原文写的保险**：第一张风牌被鸣走之后那种风牌就只剩一张，
    /// 实际上凑不齐四张同风牌的河；不写它也对，但下一个读代码的人会停下来想。
    let private isSuufonRenda (players: PlayerState list) : bool =
        players |> List.forall (fun player -> PlayerState.nakiCount player = 0)
        && (match players |> List.map PlayerState.kawa with
            | [ first ] :: rest -> isKazehai first && rest |> List.forall (fun kawa -> kawa = [ first ])
            | _ -> false)

    /// 四杠散了：全场的杠数到顶（`Ruleset.RinshanCount` 就是一局最多能杠几次），
    /// 且**不是同一家杠的**——单人四杠不流局（那是四杠子，它还等着和了）。
    let private isSuukaikan (ruleset: Ruleset) (players: PlayerState list) : bool =
        let counts = players |> List.map PlayerState.kanCount

        List.sum counts >= ruleset.RinshanCount
        && not (counts |> List.exists (fun count -> count >= ruleset.RinshanCount))

    /// 打牌落定之后的途中流局：没人荣和、也没人鸣走那张，本该轮下家摸牌的那一刻。
    /// 三种都不成立时是 None。
    ///
    /// **时机就是这一处**：四杠散了要等杠的那家打完一张（那张仍可被荣和，荣和优先），
    /// 四家立直要等第四份宣言牌落定（立直棒这时才出），四风连打要等第四张风牌落定。
    /// 顺序照 mjai 的参考实现（四家立直 → 四风连打 → 四杠散了）：四家都立直着打出第四张
    /// 同风牌时算四家立直。
    let afterDahai (ruleset: Ruleset) (players: PlayerState list) : RyuukyokuReason option =
        if isSuuchaRiichi players then Some SuuchaRiichi
        elif isSuufonRenda players then Some SuufonRenda
        elif isSuukaikan ruleset players then Some Suukaikan
        else None

    // ---- 三家和了 ----

    /// 这几家同时荣和是不是三家和了：宣言成立的家数到了「除打牌者以外全部」，
    /// 且规则集把它判成途中流局（天凤如此，雀魂三响成立、不流局；ADR-0004）。
    ///
    /// 头跳开着时至多成立一家，因此这条恒不成立。
    let isSanchaHora (ruleset: Ruleset) (winners: Seat list) : bool =
        ruleset.SanchaHoraRyuukyoku && List.length winners >= ruleset.SeatCount - 1

    // ---- 途中流局的清算与 tehai 公开 ----

    /// 每家 0 的一列增减。**途中流局的授受就是它**（一律不授受听牌料，票 12），
    /// 也是流し満貫逐家累加的起点。供托原样留在场上由 05 结转，本场的递增同样是 05 的事。
    let noDeltas (ruleset: Ruleset) : int list =
        Seat.all ruleset |> List.map (fun _ -> 0)

    /// 途中流局里**亮出手牌**的那几家——mjai `tenpais` 记的是它，不是「谁听牌」。
    /// 途中流局不验听牌、不授受听牌料，因此除了下面两种，谁都不亮：
    ///
    /// - 四家立直：四家的手牌本来就随立直亮着；
    /// - 三家和了：宣言荣和的那三家亮的是成型的手牌。
    ///
    /// 九种九牌的宣言者亮的是**不听**的手牌，因此它也记 false（与 mjai 的参考实现一致）。
    /// 连庄不读这一份：途中流局一律连庄（`KyokuEnd.isRenchan`）。
    let revealedBy (ruleset: Ruleset) (reason: RyuukyokuReason) (declarers: Seat list) : Seat list =
        match reason with
        | SuuchaRiichi -> Seat.all ruleset
        | SanchaHora -> declarers
        | Fanpai
        | NagashiMangan
        | KyuushuKyuuhai
        | SuufonRenda
        | Suukaikan -> []

    // ---- 流し満貫 ----

    /// 流し満貫的点数：**满贯档**（5 番就到满贯），符与役满倍数不参与。
    /// 它不是和了，因此没有役、没有宝牌、也不看符——「満貫」这个词就是它的全部定价。
    let private manganValue: HoraValue = { Fu = 0; Han = 5; Yakuman = 0 }

    /// 流し満貫成立的座位，按座位升序：河里打出去的**全是幺九牌**，且**一张都没被鸣走**
    /// （`PlayerState.kawaTaken`，10 票记的那个标记；被鸣那张仍留在河里，因此只能看标记）。
    ///
    /// 河是空的不算：什么都没「流」过。荒牌流局时四家各打过十几张，走不到这条。
    let nagashiSeats (players: PlayerState list) : Seat list =
        Seat.indexed players
        |> List.filter (fun (_, player) ->
            let kawa = PlayerState.kawa player

            not (List.isEmpty kawa)
            && not (PlayerState.kawaTaken player)
            && kawa |> List.forall isYaochuu)
        |> List.map fst

    /// 流し満貫的授受：成立的每一家按**自摸满贯**收（Oya 12000、Ko 8000），
    /// 多家同时成立就逐家累加，因此增减之和恒为 0。
    ///
    /// **它替代听牌料的授受，不与之叠加**（票 12）。本场与供托都不进这笔账：
    /// 流し満貫仍然是流局，供托留在场上、本场照流局递增（05 的 `Game.after`）。
    let nagashiDeltas (ruleset: Ruleset) (oya: Seat) (seats: Seat list) : int list =
        (noDeltas ruleset, seats)
        ||> List.fold (fun totals actor ->
            let score =
                Score.hora
                    ruleset
                    {
                        Actor = actor
                        Target = actor
                        Oya = oya
                        Honba = 0
                        Kyotaku = 0
                        Sekinin = None
                    }
                    manganValue

            List.map2 (+) totals score.Deltas)
