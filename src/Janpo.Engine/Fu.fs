namespace Janpo

/// 符（CONTEXT.md）：与役满以下点数计算相关的细分计数，是正确性长尾的重灾区。
///
/// **分项不合并**：40 符是「底 20 + 暗刻 8 + 单骑 2 + 门清荣和 10」还是
/// 「底 20 + 明杠 16 + 自摸 2」，出错时不看分项无从查起；`toDisplay` 把分项原样摊开。
///
/// 规则集相关的两项一律从 `Ruleset` 读、不写死：连风牌雀头几符（天凤 4 符）、
/// 岭上自摸加不加自摸符（天凤加）。
type Fu =
    private
        {
            /// 底符：一般型与国士 20，七对子 25。
            Base: int
            /// 面子符合计：顺子 0，刻子 2 / 杠子 8，暗的翻倍、幺九牌翻倍。
            Mentsu: int
            /// 雀头符：三元牌与场风 / 自风 2 符，连风牌按规则集。
            Jantou: int
            /// 听牌型符：边张 / 嵌张 / 单骑 2 符，两面 / 双碰 0 符。
            Wait: int
            /// 门清荣和 10 符。
            MenzenRon: int
            /// 自摸 2 符。平和自摸不加（平和自摸恒 20 符）。
            Tsumo: int
            /// 副露的平和形（合计 20 符）上调到 30 符的补符。
            Open: int
        }

/// 符的计算。**纯函数**：牌姿 + 选中的读法 + 规则集 → 符。
[<RequireQualifiedAccess>]
module Fu =

    // ---- 牌的谓词 ----
    // Yaku 里有一份同样的私有谓词。两处各自私有是有意的：Fu 不该为了三个一行函数
    // 依赖 Yaku 的内部，而把它们提到 Tile 上是 01 票的文件，本票不动（见 DECISIONS 08）。

    let private isJihai (tile: Tile) : bool = Tile.suit tile = Jihai

    let private isYaochuu (tile: Tile) : bool =
        isJihai tile || Tile.number tile = 1 || Tile.number tile = 9

    /// 三元牌：白发中（`5z`-`7z`）。
    let private isSangenpai (tile: Tile) : bool = isJihai tile && Tile.number tile >= 5

    let private isKaze (kaze: Kaze) (tile: Tile) : bool =
        isJihai tile && Tile.number tile = Kaze.index kaze + 1

    // ---- 分项 ----

    let private zero: Fu =
        {
            Base = 0
            Mentsu = 0
            Jantou = 0
            Wait = 0
            MenzenRon = 0
            Tsumo = 0
            Open = 0
        }

    /// 一个面子的符：顺子 0；刻子 2、杠子 8，暗的翻倍、幺九牌翻倍。
    /// 荣和补上的那个刻子按明刻算——那一位在 `MentsuBreakdown` 分解时就定死了。
    let private mentsuFu (mentsu: Mentsu) : int =
        let baseFu =
            match Mentsu.kind mentsu with
            | Shuntsu -> 0
            | Koutsu -> 2
            | Kantsu -> 8

        baseFu
        * (if Mentsu.isConcealed mentsu then 2 else 1)
        * (if isYaochuu (Mentsu.tile mentsu) then 2 else 1)

    // ---- 拆解 ----

    /// 分项合计，**未切上**。
    let total (fu: Fu) : int =
        fu.Base + fu.Mentsu + fu.Jantou + fu.Wait + fu.MenzenRon + fu.Tsumo + fu.Open

    /// 点数表要的那个符：切上到 10 的倍数。
    /// 七对子的 25 符不切上——一般型的分项全是偶数，合计恒为 10 的倍数或 10n+2/4/6/8，
    /// 只有七对子会落在 25 上，因此这里按底符判而不是按合计的奇偶判。
    let value (fu: Fu) : int =
        let raw = total fu
        if fu.Base = 25 then raw else (raw + 9) / 10 * 10

    // ---- 计算 ----

    /// 这副牌姿按这一种读法算出的符。
    ///
    /// `tally` 是 `Yaku` 选中的读法：符随分解而变（`111222333m` 拆三刻还是三顺差 16 符），
    /// 因此符只能对着**某一个** `MentsuBreakdown` 算。高点法要在全部读法上比点数，
    /// 那是 `Score.best` 的事。
    let calculate (ruleset: Ruleset) (context: YakuContext) (hand: AgariHand) (tally: YakuTally) : Fu =
        let menzen = AgariHand.isMenzen hand
        let tsumo = AgariHand.isTsumo hand
        let pinfu = YakuTally.yaku tally |> List.contains Yaku.Pinfu

        // 平和自摸恒 20 符，因此平和不加自摸符；岭上自摸加不加由规则集定。
        let tsumoFu =
            if not tsumo || pinfu then 0
            elif context.Rinshan && not ruleset.RinshanTsumoFu then 0
            else 2

        let menzenRonFu = if menzen && not tsumo then 10 else 0

        match tally.Shape, tally.Breakdown with
        // 七对子恒 25 符：门清荣和与自摸都不加，也不切上。
        | Chiitoitsu, _ -> { zero with Base = 25 }
        // 国士是役满，符不参与算点；仍按底符 + 门清荣和 / 自摸算出来备查。
        // `Standard, None` 表示读法里没有分解，正常路径上走不到。
        | Kokushi, _
        | Standard, None ->
            { zero with
                Base = 20
                MenzenRon = menzenRonFu
                Tsumo = tsumoFu
            }
        | Standard, Some breakdown ->
            let jantou = MentsuBreakdown.jantou breakdown

            let jantouFu =
                let bakaze = isKaze context.Bakaze jantou
                let jikaze = isKaze context.Jikaze jantou

                if bakaze && jikaze then ruleset.DoubleKazeJantouFu
                elif bakaze || jikaze || isSangenpai jantou then 2
                else 0

            let waitFu =
                match MentsuBreakdown.wait breakdown with
                | Ryanmen
                | Shanpon -> 0
                | Penchan
                | Kanchan
                | Tanki -> 2

            let core =
                {
                    Base = 20
                    Mentsu = MentsuBreakdown.mentsu breakdown |> List.sumBy mentsuFu
                    Jantou = jantouFu
                    Wait = waitFu
                    MenzenRon = menzenRonFu
                    Tsumo = tsumoFu
                    Open = 0
                }

            // 副露的平和形（合计 20 符）按 30 符算：这是通行规则，也是天凤的算法。
            { core with
                Open = if not menzen && total core = 20 then 10 else 0
            }

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式，分项原样摊开。
    let toDisplay (fu: Fu) : string =
        let parts =
            [
                $"底 {fu.Base}"

                if fu.Mentsu > 0 then
                    $"面子 {fu.Mentsu}"

                if fu.Jantou > 0 then
                    $"雀头 {fu.Jantou}"

                if fu.Wait > 0 then
                    $"听牌型 {fu.Wait}"

                if fu.MenzenRon > 0 then
                    $"门清荣和 {fu.MenzenRon}"

                if fu.Tsumo > 0 then
                    $"自摸 {fu.Tsumo}"

                if fu.Open > 0 then
                    $"副露平和形 {fu.Open}"
            ]
            |> String.concat " + "

        $"{value fu} 符（{parts}）"
