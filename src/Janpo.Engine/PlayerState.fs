namespace Janpo

/// 引擎内某座位的牌局状态（CONTEXT.md）：手牌、河、副露、点数……与占这个座位的 Player 是谁无关。
type PlayerState =
    private
        {
            /// 暗牌，mjai 顺序升序。轮到自己打牌时**含**刚摸进的那张；**不含副露**。
            Hand: Tile list
            /// 河：这家打出去的牌，按打出顺序。
            ///
            /// **被鸣走的那张仍留在这里**：振听看的是「自己打过什么」，与那张牌后来被谁拿走无关。
            /// 拿走这件事记在 `KawaTaken` 里，牌本身则另外进了鸣牌那家的副露。
            Kawa: Tile list
            /// 副露（CONTEXT.md 的 Naki），按鸣的先后。公开信息。
            Naki: Naki list
            /// 河里是否有牌被他家鸣走过。**只置位不清除**——流し満谯（12 票）的前提
            /// 就是它一局下来恒为 false。
            KawaTaken: bool
            /// 点数。
            Score: int
            /// 刚摸进、还没打出去的那张；打完就归 None。摸切判定看它。
            Drawn: Tile option
            /// 振听：永久与同巡分别维护（见 `Furiten`）。只挡荣和。
            Furiten: Furiten
            /// 立直：没立直 / 宣言了还没落定 / 已成立（见 `RiichiState`）。
            Riichi: RiichiState
            /// 一发：立直成立后一巡内有效。三条解除各自有入口：自家再打一张（`discard`）、
            /// 任何人鸣牌（`clearIppatsu`，由 `GameState` 统一打断），以及这一局终了。
            Ippatsu: bool
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
            Naki = []
            KawaTaken = false
            Score = score
            Drawn = drawn
            Furiten = Furiten.none
            Riichi = RiichiState.none
            Ippatsu = false
        }

    // ---- 拆解 ----

    /// 暗牌，mjai 顺序升序。
    let hand (player: PlayerState) : Tile list = player.Hand

    /// 河，按打出顺序；被鸣走的那张也在里面。
    let kawa (player: PlayerState) : Tile list = player.Kawa

    /// 副露，按鸣的先后。
    let naki (player: PlayerState) : Naki list = player.Naki

    /// 副露数。形态判定（`HandShape` / `AgariHand`）要的就是它。**杠也只算一组**：
    /// 杠多吃掉的那一张恰好被补摸的岭上牌抵回来，因此「暗牌 + 3 × 副露数」恒定。
    let nakiCount (player: PlayerState) : int = List.length player.Naki

    /// 这家杠了几次（三种杠都算）。**12 票的四杠散了读它**：单人四杠不流局，
    /// 因此光有全场总数（`GameState.kanCount`）不够，还要逐家的这一份。
    let kanCount (player: PlayerState) : int =
        player.Naki |> List.filter Naki.isKan |> List.length

    /// 河里是否有牌被他家鸣走过。**流し満谯（12 票）的前提读的就是它。**
    let kawaTaken (player: PlayerState) : bool = player.KawaTaken

    /// 点数。
    let score (player: PlayerState) : int = player.Score

    /// 刚摸进、还没打出去的那张。
    let drawn (player: PlayerState) : Tile option = player.Drawn

    /// 振听的两种状态。振听只挡荣和，不挡自摸。
    let furiten (player: PlayerState) : Furiten = player.Furiten

    /// 立直状态。判役读的是它的 `RiichiState.declaration`（只有成立了的才算）。
    let riichi (player: PlayerState) : RiichiState = player.Riichi

    /// 一发是不是还亮着。没立直时恒为 false。
    let ippatsu (player: PlayerState) : bool = player.Ippatsu

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

    /// 是否听牌：暗牌加副露的 Shanten 为 0（CONTEXT.md）。听牌判定只有这一份，
    /// 荒牌流局的听牌料与后续票的立直合法性读的都是它。
    /// 张数不成形态的手牌（本票内不会出现）当作不听。
    let isTenpai (kindSet: TileKindSet) (player: PlayerState) : bool =
        match HandShape.create (nakiCount player) player.Hand with
        | Error _ -> false
        | Ok shape -> Shanten.value (Shanten.calculate kindSet shape) = 0

    /// 暗牌自身是否已成和了型（刚摸完的 14 张手牌）。和了型判定只有 `AgariShape.classify`
    /// 一份，这里不另写。**只判型，不判役**（无役不可和是 07 票的事）。
    let isAgari (kindSet: TileKindSet) (player: PlayerState) : bool =
        match HandShape.create (nakiCount player) player.Hand with
        | Error _ -> false
        | Ok shape -> AgariShape.isAgari kindSet shape

    /// 暗牌加上这一张是否成和了型（荣和：13 张 + 他家打出的那张）。
    /// 手里已有四张同种时自然不成立（`HandShape` 拒绝第五张）。
    let isAgariWith (kindSet: TileKindSet) (pai: Tile) (player: PlayerState) : bool =
        match HandShape.create (nakiCount player) (pai :: player.Hand) with
        | Error _ -> false
        | Ok shape -> AgariShape.isAgari kindSet shape

    /// 这家此刻的和了牌姿：暗牌 + 副露 + 和了牌。自摸时和了牌已经在暗牌里（刚摸进那张），
    /// 荣和时把点出来的那张补进去——牌型上它就是手牌的一部分（牌本身仍留在放铳者的河里）。
    /// 张数不成和了牌姿时是 `AgariHandError`，是值不是异常。
    ///
    /// **引擎里构造 `AgariHand` 只有这一处**：副露已经接上，11 的杠不必再动它。
    let agari (tsumo: bool) (winning: Tile) (player: PlayerState) : Result<AgariHand, AgariHandError> =
        if tsumo then
            AgariHand.tsumo player.Naki player.Hand winning
        else
            AgariHand.ron player.Naki (winning :: player.Hand) winning

    /// 和了牌的牌种（听什么）：暗牌加上它就成和了型的那些牌种，按 mjai 顺序升序。
    /// 永久振听用它与自己的河对。张数不对时得空列表。
    let waits (kindSet: TileKindSet) (player: PlayerState) : Tile list =
        match HandShape.create (nakiCount player) player.Hand with
        | Error _ -> []
        | Ok shape -> AgariShape.waits kindSet shape

    // ---- 迁移 ----

    /// 鸣牌：亮出的那几张离开暗牌进副露，**不摸牌**（`Drawn` 因此是 None，接下来那一手必定是手切）。
    /// 被鸣的那张不进手牌——它已经在 `Naki` 里（`Naki.taken`）。
    /// 牌不在手里时原样返回：合法性由 `GameState` 在此之前判掉（不在这里重复一份）。
    ///
    /// **自家鸣牌也解除同巡振听**（票 63，2025 整年语料 4/4 实证）：同巡的窗口到自家
    /// 下一次**摸打**为止，鸣牌接着的打牌同样翻篇。解除点取在鸣牌这一步：鸣牌到打牌
    /// 之间自家没有荣和的机会，行为上与「打完才解除」无异；且必须排在见逃落地之后
    /// （`GameState` 先 settle 见逃再套用鸣牌，因此「鸣走能荣的那张」也被这里清掉）。
    /// 暗杠与大明杠随后补摸岭上牌，`draw` 本就会清；这里统一清掉不多一层分支。
    let addNaki (naki: Naki) (player: PlayerState) : PlayerState =
        let hand =
            (player.Hand, Naki.consumed naki)
            ||> List.fold (fun rest tile -> removeOne tile rest |> Option.defaultValue rest)

        { player with
            Hand = Tile.sort hand
            Naki = player.Naki @ [ naki ]
            Drawn = None
            Furiten = { player.Furiten with Doujun = false }
        }

    /// 加杠：手里那张离开暗牌，原来那组碰**原地换成加杠**——副露数不变、位置不变，
    /// 因此「暗牌 + 3 × 副露数」也不变（多吃的那一张由岭上牌补回来）。
    ///
    /// `kakan` 必须是由那组碰升上来的（`Naki.kakan`）；找不到同牌种的碰时原样返回
    /// ——合法性由 `GameState` 在此之前判掉（不在这里重复一份）。
    let addKakan (added: Tile) (kakan: Naki) (player: PlayerState) : PlayerState =
        let index =
            player.Naki
            |> List.tryFindIndex (fun naki ->
                Naki.kind naki = NakiKind.Pon
                && Naki.tiles naki
                   |> List.forall (fun tile -> Tile.kindIndex tile = Tile.kindIndex added))

        match index, removeOne added player.Hand with
        | Some index, Some hand ->
            { player with
                Hand = Tile.sort hand
                Naki = player.Naki |> List.mapi (fun each naki -> if each = index then kakan else naki)
                Drawn = None
            }
        | _ -> player

    /// 河里的一张被他家鸣走了。**只置位，不清除，也不把牌从河里拿掉**
    /// （振听要看它；流し満谯要看记号）。
    let markKawaTaken (player: PlayerState) : PlayerState = { player with KawaTaken = true }

    /// 摸进一张：进手牌，并记成「刚摸进的那张」。**同巡振听到此解除**
    /// （它的窗口到自家下次摸打为止，摸牌是其一，另一支在 `addNaki`）；永久振听不受影响。
    let draw (tile: Tile) (player: PlayerState) : PlayerState =
        { player with
            Hand = Tile.sort (tile :: player.Hand)
            Drawn = Some tile
            Furiten = { player.Furiten with Doujun = false }
        }

    /// 见逃：本巡放过了一张可以荣和的牌——同巡振听成立，到自己下次摸打
    /// （摸牌或鸣牌）为止不能荣和。
    /// 放过的那张在他家的河里而不在自己的河里，因此平时它不永久。
    ///
    /// **立直中的见逃是永久振听**：立直后手牌不再变，这一位从此闩死
    /// （`refreshFuriten` 对立直**已成立**的座位只置位不清除）。
    let minogashi (player: PlayerState) : PlayerState =
        { player with
            Furiten =
                {
                    Permanent = player.Furiten.Permanent || RiichiState.isActive player.Riichi
                    Doujun = true
                }
        }

    /// 重算永久振听：自己的河里只要有一张是自己现在的和了牌，就振听。
    /// 听牌一变就要重算，因此每次自家打牌后调一次（手牌只会在那时变）。
    /// 这是**重算**而不是置位：换听到不含自己打过的牌上就解除，这是通行规则。
    /// 红宝牌与对应正牌同一牌种，对比前先去红。
    ///
    /// **闩死只从立直成立（`Accepted`）那一刻算起**：宣言牌那一手手牌还在变（状态是
    /// `Declared`），宣言牌换了听就照常解除振听——否则「振听时立直、用宣言牌换听」
    /// 这个常见手筋会被闩死，那一家从此荣和不了。真实牌谱实证（票 13 的对拍，200 局里 2 处）：
    /// 天凤在这种局面下让荣和成立。
    let refreshFuriten (kindSet: TileKindSet) (player: PlayerState) : PlayerState =
        let waiting = waits kindSet player

        let hit =
            player.Kawa |> List.exists (fun tile -> List.contains (Tile.deaka tile) waiting)

        { player with
            Furiten =
                { player.Furiten with
                    // 立直**成立后**只置位不清除：那之后手牌不再变，重算只会把「立直后见逃」
                    // 那一位（放过的牌不在自家河里，重算看不到）冲掉。
                    // 宣言牌那一手（`Declared`）不在此列：那一手听牌还会变。
                    Permanent = hit || (RiichiState.isAccepted player.Riichi && player.Furiten.Permanent)
                }
        }

    /// 打出一张：出手牌、进河，「刚摸进的那张」归 None。
    /// 牌不在手里时原样返回——合法性由 `GameState.step` 在此之前判掉（不在这里重复一份）。
    ///
    /// **自家再打一张，一发就到头了**：一发的窗口是「立直成立到自己下一手打牌为止」，
    /// 包含自己下次自摸（一发自摸）。宣言牌那一手打时一发还没亮（要等 `acceptRiichi`），
    /// 因此这里清它不会把刚立的直那一发清掉。
    let discard (tile: Tile) (player: PlayerState) : PlayerState =
        match removeOne tile player.Hand with
        | None -> player
        | Some hand ->
            { player with
                Hand = hand
                Kawa = player.Kawa @ [ tile ]
                Drawn = None
                Ippatsu = false
            }

    /// 点数增减。
    let addScore (delta: int) (player: PlayerState) : PlayerState =
        { player with
            Score = player.Score + delta
        }

    /// 宣言立直：进「宣言了还没落定」。**立直棒还没出**（它在 `acceptRiichi` 那一步）。
    let declareRiichi (declaration: RiichiDeclaration) (player: PlayerState) : PlayerState =
        { player with
            Riichi = RiichiState.declare declaration
        }

    /// 立直成立：宣言牌没被荣和。**一发到此亮起**；立直棒的扣点走 `addScore`，
    /// 进供托是 `GameState` 那一层的事。
    let acceptRiichi (player: PlayerState) : PlayerState =
        if RiichiState.isDeclared player.Riichi then
            { player with
                Riichi = RiichiState.accept player.Riichi
                Ippatsu = true
            }
        else
            player

    /// 宣言牌被荣和：立直不成立，退回没立直（立直棒也就不出）。
    let cancelRiichi (player: PlayerState) : PlayerState =
        { player with
            Riichi = RiichiState.cancel player.Riichi
        }

    /// 一发被打断。全场统一打断的入口在 `GameState.interruptIppatsu`。
    let clearIppatsu (player: PlayerState) : PlayerState = { player with Ippatsu = false }
