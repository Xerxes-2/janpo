namespace Janpo

/// 引擎内某座位的牌局状态（CONTEXT.md）：手牌、河、点数……与占这个座位的 Player 是谁无关。
///
/// 04 票只有摸打，所以只有手牌、河、点数与「刚摸进的那张」；副露（10 / 11）、立直与振听
/// 标志（06 / 09）由后续票加字段。
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

    // ---- 迁移 ----

    /// 摸进一张：进手牌，并记成「刚摸进的那张」。
    let draw (tile: Tile) (player: PlayerState) : PlayerState =
        { player with
            Hand = Tile.sort (tile :: player.Hand)
            Drawn = Some tile
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
