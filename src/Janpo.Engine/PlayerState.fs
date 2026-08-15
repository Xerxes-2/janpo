namespace Janpo

/// 引擎内某座位的牌局状态（CONTEXT.md）：手牌、河、点数……与占这个座位的 Player 是谁无关。
///
/// 副露（10 / 11）与立直标志（09）由后续票加字段。
type PlayerState =
    private
        {
            /// 暗牌，mjai 顺序升序。轮到自己打牌时**含**刚摸进的那张。
            Hand: Tile list
            /// 河：这家打出去的牌，按打出顺序。被鸣走的标记是 10 票的事。
            Kawa: Tile list
            /// 点数。
            Score: int
            /// 刚摸进、还没打出去的那张；打完就归 None。摸切判定看它。
            Drawn: Tile option
            /// 振听：永久与同巡分别维护（见 `Furiten`）。只挡荣和。
            Furiten: Furiten
        }

/// 家状态的构造、拆解与迁移。
[<RequireQualifiedAccess>]
module PlayerState =

    // ---- 构造 ----

    /// 配牌发完时的家状态：河为空。`drawn` 是「刚摸进的那张」，开局时只有 Oya 有，
    /// 且它**已经在** `haipai` 里（Oya 的那手是 14 张）。
    let ofHaipai (score: int) (drawn: Tile option) (haipai: Tile list) : PlayerState =
        {
            Hand = Tile.sort haipai
            Kawa = []
            Score = score
            Drawn = drawn
            Furiten = Furiten.none
        }

    // ---- 拆解 ----

    /// 暗牌，mjai 顺序升序。
    let hand (player: PlayerState) : Tile list = player.Hand

    /// 河，按打出顺序。
    let kawa (player: PlayerState) : Tile list = player.Kawa

    /// 点数。
    let score (player: PlayerState) : int = player.Score

    /// 刚摸进、还没打出去的那张。
    let drawn (player: PlayerState) : Tile option = player.Drawn

    /// 振听的两种状态。振听只挡荣和，不挡自摸。
    let furiten (player: PlayerState) : Furiten = player.Furiten

    /// 从牌列里拿掉**一张**给定的牌；没有这张则返回 None。
    /// 红宝牌与正牌是不同的牌（`5m` 与 `5mr` 各算各的）。
    let private removeOne (tile: Tile) (tiles: Tile list) : Tile list option =
        let rec loop (skipped: Tile list) (rest: Tile list) =
            match rest with
            | [] -> None
            | head :: tail when head = tile -> Some(List.rev skipped @ tail)
            | head :: tail -> loop (head :: skipped) tail

        loop [] tiles

    /// 手切能打的牌：手牌去掉刚摸进的那一张实例。摸切打的是 `drawn`，不在这里。
    let tedashi (player: PlayerState) : Tile list =
        match player.Drawn with
        | None -> player.Hand
        | Some drawn -> removeOne drawn player.Hand |> Option.defaultValue player.Hand

    /// 是否听牌：暗牌的 Shanten 为 0（CONTEXT.md）。听牌判定只有这一份，
    /// 荒牌流局的听牌料与后续票的立直合法性读的都是它。
    /// 张数不成形态的手牌（本票内不会出现）当作不听。
    let isTenpai (kindSet: TileKindSet) (player: PlayerState) : bool =
        match HandShape.create 0 player.Hand with
        | Error _ -> false
        | Ok shape -> Shanten.value (Shanten.calculate kindSet shape) = 0

    /// 暗牌自身是否已成和了型（刚摸完的 14 张手牌）。和了型判定只有 `AgariShape.classify`
    /// 一份，这里不另写。**只判型，不判役**（无役不可和是 07 票的事）。
    let isAgari (kindSet: TileKindSet) (player: PlayerState) : bool =
        match HandShape.create 0 player.Hand with
        | Error _ -> false
        | Ok shape -> AgariShape.isAgari kindSet shape

    /// 暗牌加上这一张是否成和了型（荣和：13 张 + 他家打出的那张）。
    /// 手里已有四张同种时自然不成立（`HandShape` 拒绝第五张）。
    let isAgariWith (kindSet: TileKindSet) (pai: Tile) (player: PlayerState) : bool =
        match HandShape.create 0 (pai :: player.Hand) with
        | Error _ -> false
        | Ok shape -> AgariShape.isAgari kindSet shape

    /// 这家此刻的和了牌姿：暗牌 + 副露 + 和了牌。自摸时和了牌已经在暗牌里（刚摸进那张），
    /// 荣和时把点出来的那张补进去——牌型上它就是手牌的一部分（牌本身仍留在放铳者的河里）。
    /// 张数不成和了牌姿时是 `AgariHandError`，是值不是异常。
    ///
    /// **引擎里构造 `AgariHand` 只有这一处**：副露（10 / 11 票）落地后只需改这里。
    let agari (tsumo: bool) (winning: Tile) (player: PlayerState) : Result<AgariHand, AgariHandError> =
        if tsumo then
            AgariHand.tsumo [] player.Hand winning
        else
            AgariHand.ron [] (winning :: player.Hand) winning

    /// 和了牌的牌种（听什么）：暗牌加上它就成和了型的那些牌种，按 mjai 顺序升序。
    /// 永久振听用它与自己的河对。张数不对时得空列表。
    let waits (kindSet: TileKindSet) (player: PlayerState) : Tile list =
        TileKindSet.kinds kindSet
        |> List.filter (fun kind -> isAgariWith kindSet kind player)

    // ---- 迁移 ----

    /// 摸进一张：进手牌，并记成「刚摸进的那张」。**同巡振听到此解除**
    /// （它的定义就是「到自己下次摸牌为止」）；永久振听不受影响。
    let draw (tile: Tile) (player: PlayerState) : PlayerState =
        { player with
            Hand = Tile.sort (tile :: player.Hand)
            Drawn = Some tile
            Furiten = { player.Furiten with Doujun = false }
        }

    /// 见逃：本巡放过了一张可以荣和的牌——同巡振听成立，到自己下次摸牌为止不能荣和。
    /// 它不永久：放过的那张在他家的河里，不在自己的河里（立直后见逃变永久是 09 的事）。
    let minogashi (player: PlayerState) : PlayerState =
        { player with
            Furiten = { player.Furiten with Doujun = true }
        }

    /// 重算永久振听：自己的河里只要有一张是自己现在的和了牌，就振听。
    /// 听牌一变就要重算，因此每次自家打牌后调一次（手牌只会在那时变）。
    /// 这是**重算**而不是置位：换听到不含自己打过的牌上就解除，这是通行规则。
    /// 红宝牌与对应正牌同一牌种，对比前先去红。
    let refreshFuriten (kindSet: TileKindSet) (player: PlayerState) : PlayerState =
        let waiting = waits kindSet player

        let hit =
            player.Kawa |> List.exists (fun tile -> List.contains (Tile.deaka tile) waiting)

        { player with
            Furiten = { player.Furiten with Permanent = hit }
        }

    /// 打出一张：出手牌、进河，「刚摸进的那张」归 None。
    /// 牌不在手里时原样返回——合法性由 `GameState.step` 在此之前判掉（不在这里重复一份）。
    let discard (tile: Tile) (player: PlayerState) : PlayerState =
        match removeOne tile player.Hand with
        | None -> player
        | Some hand ->
            { player with
                Hand = hand
                Kawa = player.Kawa @ [ tile ]
                Drawn = None
            }

    /// 点数增减。
    let addScore (delta: int) (player: PlayerState) : PlayerState =
        { player with
            Score = player.Score + delta
        }
