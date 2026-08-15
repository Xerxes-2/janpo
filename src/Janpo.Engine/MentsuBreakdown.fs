namespace Janpo

/// 听牌型：和了牌补上的是哪一种等待。平和要两面，符也看它（08 票）。
type WaitKind =
    /// 两面：`45m` 等 `3m` / `6m`。
    | Ryanmen
    /// 边张：`12m` 等 `3m`、`89m` 等 `7m`。
    | Penchan
    /// 嵌张：`13m` 等 `2m`。
    | Kanchan
    /// 双碰：两个对子等其中一边成刻。
    | Shanpon
    /// 单骑：等雀头。
    | Tanki

/// 一般型和了牌的一种**面子分解**：4 个面子（含副露）+ 雀头 + 听牌型。
///
/// 一手牌常常有多种分解——`111222333m` 既能拆三个顺子（可能有一杯口）也能拆三个刻子
/// （可能有三暗刻），`44556677p` 的和了牌落在哪一组也是选择。役种判定必须在全部分解里
/// **取番数最高的那一种**（高点法），因此 `enumerate` 返回的是一组分解，不是一个。
///
/// 面子的「暗不暗」在这里就定死了：荣和补上的那个刻子按明刻算（三暗刻与符都读这一位）。
type MentsuBreakdown =
    private
        {
            /// 4 个面子，含副露，按代表牌升序。
            Mentsu: Mentsu list
            /// 雀头。
            Jantou: Tile
            /// 和了牌补上的是哪一种等待。
            Wait: WaitKind
        }

/// 面子分解的枚举与拆解。
[<RequireQualifiedAccess>]
module MentsuBreakdown =

    // ---- 拆解 ----

    /// 4 个面子，含副露，按代表牌升序。
    let mentsu (breakdown: MentsuBreakdown) : Mentsu list = breakdown.Mentsu

    /// 雀头。
    let jantou (breakdown: MentsuBreakdown) : Tile = breakdown.Jantou

    /// 听牌型。
    let wait (breakdown: MentsuBreakdown) : WaitKind = breakdown.Wait

    // ---- 枚举 ----

    let private kindRank (kind: MentsuKind) : int =
        match kind with
        | Shuntsu -> 0
        | Koutsu -> 1
        | Kantsu -> 2

    /// 面子表的规范顺序。两种分解只有排列不同的时候要能判等，因此顺序不能随手顺走。
    let private canonical (mentsu: Mentsu list) : Mentsu list =
        mentsu
        |> List.sortBy (fun one ->
            Tile.kindIndex (Mentsu.tile one), kindRank (Mentsu.kind one), (if Mentsu.isConcealed one then 0 else 1))

    /// 数牌里能当顺子头的牌种索引：同花色内序数 1-7。
    let private isShuntsuStart (index: int) : bool = index < 27 && index % 9 <= 6

    /// 把暗牌拆成 `needed` 个面子的全部办法。`counts` 是可以原地增删的副本，
    /// 每次都从最低的牌种开始拆，因此同一种分解只会被生成一次。
    let private decompositions (counts: int array) (needed: int) : Mentsu list list =
        let rec search (index: int) (taken: int) (acc: Mentsu list) : Mentsu list list =
            if index >= Tile.KindCount then
                if taken = needed then [ List.rev acc ] else []
            elif counts.[index] = 0 then
                search (index + 1) taken acc
            elif taken >= needed then
                // 面子已经够了却还有牌剩着：这条路不通。
                []
            else
                match Tile.ofKindIndex index with
                | None -> []
                | Some tile ->
                    let koutsu =
                        if counts.[index] >= 3 then
                            counts.[index] <- counts.[index] - 3
                            let found = search index (taken + 1) (Mentsu.koutsu true tile :: acc)
                            counts.[index] <- counts.[index] + 3
                            found
                        else
                            []

                    let shuntsu =
                        if isShuntsuStart index && counts.[index + 1] > 0 && counts.[index + 2] > 0 then
                            match Mentsu.shuntsu true tile with
                            | None -> []
                            | Some mentsu ->
                                counts.[index] <- counts.[index] - 1
                                counts.[index + 1] <- counts.[index + 1] - 1
                                counts.[index + 2] <- counts.[index + 2] - 1
                                let found = search index (taken + 1) (mentsu :: acc)
                                counts.[index] <- counts.[index] + 1
                                counts.[index + 1] <- counts.[index + 1] + 1
                                counts.[index + 2] <- counts.[index + 2] + 1
                                found
                        else
                            []

                    koutsu @ shuntsu

        search 0 0 []

    /// 顺子里补上和了牌的是哪一种等待。`lowest` 是顺子最小的那张。
    let private shuntsuWait (lowest: Tile) (winning: Tile) : WaitKind =
        let start = Tile.number lowest
        let won = Tile.number winning

        if won = start + 1 then
            Kanchan
        elif won = start then
            (if start <= 6 then Ryanmen else Penchan)
        elif start >= 2 then
            Ryanmen
        else
            Penchan

    /// 和了牌能落在哪一块上。同一副面子分解里，和了牌落在不同块上是**不同的**分解：
    /// 它决定听牌型，也决定荣和时哪个刻子变成明刻。
    let private placements
        (winning: Tile)
        (ron: bool)
        (nakiMentsu: Mentsu list)
        (jantou: Tile)
        (concealed: Mentsu list)
        : MentsuBreakdown list =
        [
            if jantou = winning then
                {
                    Mentsu = canonical (nakiMentsu @ concealed)
                    Jantou = jantou
                    Wait = Tanki
                }

            for index in 0 .. List.length concealed - 1 do
                let one = List.item index concealed

                if Mentsu.contains winning one then
                    match Mentsu.kind one with
                    // 暗牌里拆不出杠子：4 张同种牌只能是刻子加一张零牌。
                    | Kantsu -> ()
                    | Koutsu ->
                        let replaced =
                            concealed
                            |> List.mapi (fun position other ->
                                if position = index then
                                    Mentsu.withConcealed (not ron) other
                                else
                                    other)

                        {
                            Mentsu = canonical (nakiMentsu @ replaced)
                            Jantou = jantou
                            Wait = Shanpon
                        }
                    | Shuntsu ->
                        {
                            Mentsu = canonical (nakiMentsu @ concealed)
                            Jantou = jantou
                            Wait = shuntsuWait (Mentsu.tile one) winning
                        }
        ]

    /// 这副和了牌的全部面子分解。不是一般型（七对子、国士、根本没和）时为空表。
    ///
    /// 副露与暗杠原样进面子表；暗牌部分穷举「雀头 × 面子拆法 × 和了牌落点」，
    /// 重复的分解会被去掉。
    let enumerate (hand: AgariHand) : MentsuBreakdown list =
        let nakiMentsu = AgariHand.naki hand |> List.map Naki.mentsu
        let needed = 4 - List.length nakiMentsu
        let winning = Tile.deaka (AgariHand.winningTile hand)
        let ron = not (AgariHand.isTsumo hand)
        let counts = Array.zeroCreate Tile.KindCount

        for tile in AgariHand.concealed hand do
            let index = Tile.kindIndex tile
            counts.[index] <- counts.[index] + 1

        let byJantou (jantouIndex: int) : MentsuBreakdown list =
            if counts.[jantouIndex] < 2 then
                []
            else
                match Tile.ofKindIndex jantouIndex with
                | None -> []
                | Some jantou ->
                    counts.[jantouIndex] <- counts.[jantouIndex] - 2
                    let sets = decompositions counts needed
                    counts.[jantouIndex] <- counts.[jantouIndex] + 2
                    sets |> List.collect (placements winning ron nakiMentsu jantou)

        if needed < 0 then
            []
        else
            [ 0 .. Tile.KindCount - 1 ] |> List.collect byJantou |> List.distinct

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    let toDisplay (breakdown: MentsuBreakdown) : string =
        let mentsu = breakdown.Mentsu |> List.map Mentsu.toDisplay |> String.concat " "
        let jantou = Tile.toDisplay breakdown.Jantou

        let wait =
            match breakdown.Wait with
            | Ryanmen -> "两面"
            | Penchan -> "边张"
            | Kanchan -> "嵌张"
            | Shanpon -> "双碰"
            | Tanki -> "单骑"

        $"{mentsu} 雀头{jantou} {wait}听"
