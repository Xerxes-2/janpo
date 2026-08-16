namespace Janpo

/// 规则集：一场对局的规则配置。
///
/// **座位数、牌山构成与规则开关的唯一出处**——引擎别处不得再出现 `4`（座位数）、`136`（牌山张数）、
/// `13`（配牌）、`14`（王牌）这类字面量，一律从这里读。
///
/// 三麻（3 家 / 108 张 / 无 2-8m / 无红 5m）本版**不做**，但它在结构上只是另一个
/// `Ruleset` 值，这道门没有焊死（见 DECISIONS 提案 S-B）。
type Ruleset =
    {
        /// 座位数。
        SeatCount: int
        /// 对局长度：一场对局打哪几局由它与座位数一起推出（`Ruleset.kyokus`）。
        Length: GameLength
        /// 牌山里出现的牌种（正牌）。四麻是全部 34 种。
        ///
        /// 类型**就是** `TileKindSet`，规则集构造时派生一次：形态判定（Shanten / Ukeire /
        /// 和了型）要的正是它，而「这个规则集里存在哪些牌种」只该有一个表示（ADR-0004 决定 4）。
        /// 要一列牌用 `TileKindSet.kinds`，要张数用 `TileKindSet.count`。
        TileKinds: TileKindSet
        /// 每种牌几张。
        CopiesPerKind: int
        /// 红宝牌：牌山里把对应正牌的其中一张换成它。空列表 = 关掉红宝牌（SPEC 的规则开关）。
        Akadora: Tile list
        /// 食断（SPEC 的「食断有无」开关）：关掉时副露的手牌不成立断幺九。
        Kuitan: bool
        /// 每家的配牌张数。
        HaipaiSize: int
        /// 王牌张数。
        DeadWallSize: int
        /// 王牌里岭上牌的张数，也就是一局最多能杠几次。
        RinshanCount: int
        /// 每家的起始点数。
        StartingScore: int
        /// 立直棒一根的点数。供托（`KyokuContext.Kyotaku`）记的是**根数**，换算成点数要它；
        /// 09 票宣言立直时从点数里扣的也是它。
        RiichiBou: int
        /// ノーテン罰符（听牌料）：荒牌流局时不听牌的家合计付出的点数，听牌的家平分。
        /// 全听或全不听时不授受。它应当能被 `1 .. SeatCount - 1` 整除，否则授受不平：
        /// 3000 对四麣（1/2/3 家听）与三麣（1/2 家听）都成立，这也是 mjai 的算法。
        NotenBappu: int
        /// Atamahane（头跳）：同巡多家荣和时只成立打牌者下家优先的那一家。
        /// 关掉则双响 / 三响都成立。**默认关**——天凤与雀魂都是双响（ADR-0004 决定 3），
        /// 60 局鳳凰卓牌谱里 3 处 `hora` 事件相邻即为实测印证。要头跳用 `Ruleset.withAtamahane`。
        Atamahane: bool
        /// 三家和了是不是途中流局：**天凤判成流局（默认）、雀魂三响都成立**
        /// （ADR-0004：默认值对齐天凤）。关掉它就是雀魂的做法，用 `Ruleset.withoutSanchaHoraRyuukyoku`。
        ///
        /// **头跳开着时它永远用不上**：那时至多成立一家，凑不齐三家。
        SanchaHoraRyuukyoku: bool
        /// 九种九牌宣言所需的幺九牌**种数**。通行规则是 9（因此叫「九种」九牌），
        /// 要十种十牌的规则集把它改成 10。
        KyuushuKinds: int
        /// Kiriage Mangan（切上满贯）：把 4 番 30 符 / 3 番 60 符（基本点 1920）上调为满贯。
        /// **默认关**——天凤与雀魂段位战都不采用（DECISIONS 提案 S-A）。
        KiriageMangan: bool
        /// 连风牌雀头的符：雀头同时是场风与自风时算几符。**天凤是 4 符**；
        /// 只算 2 符的规则集把它改成 2。单独的场风 / 自风 / 三元牌雀头恒为 2 符，不可配。
        DoubleKazeJantouFu: int
        /// 岭上自摸加不加自摸符（2 符）。天凤加，因此默认 true。
        RinshanTsumoFu: bool
        /// 国士无双能不能抢暗杠（Chankan）。**天凤禁止、雀魂允许**（提案 S-A、
        /// `smly/RiichiEnv` issue #43），因此默认 false；其余牌型任何规则集都抢不了暗杠。
        /// 加杠不受它影响：加杠恒可抢。
        KokushiAnkanChankan: bool
        /// 一本场的点数：荣和时放铳者多付这么多，自摸时由付家平摊。
        HonbaPoints: int
    }

/// 规则集的预设与推导量。
[<RequireQualifiedAccess>]
module Ruleset =

    // ---- 构造 ----

    /// 四麻默认规则集：34 种正牌各 4 张（136 张），红 5 各一张，
    /// 配牌 13 张，王牌 14 张（4 张岭上 + 5 叠表里宝牌指示牌），起手 25000，听牌料 3000，
    /// 立直棒 1000。长度取**东风战**：M0 的验收就是「四随机 Player 跑完一整场东风战」，
    /// 半庄用 `Ruleset.withLength Hanchan` 换。
    let yonma: Ruleset =
        {
            SeatCount = 4
            Length = Tonpuusen
            TileKinds = TileKindSet.fourPlayer
            CopiesPerKind = 4
            Akadora = Tile.akadoraKinds
            Kuitan = true
            HaipaiSize = 13
            DeadWallSize = 14
            RinshanCount = 4
            StartingScore = 25000
            RiichiBou = 1000
            NotenBappu = 3000
            Atamahane = false
            SanchaHoraRyuukyoku = true
            KyuushuKinds = 9
            KiriageMangan = false
            DoubleKazeJantouFu = 4
            RinshanTsumoFu = true
            KokushiAnkanChankan = false
            HonbaPoints = 300
        }

    /// 关掉红宝牌（SPEC 的「红宝牌有无」开关）。开着的形态就是各预设本身。
    let withoutAkadora (ruleset: Ruleset) : Ruleset = { ruleset with Akadora = [] }

    /// 关掉食断（SPEC 的「食断有无」开关）：副露后断幺九不成立。
    let withoutKuitan (ruleset: Ruleset) : Ruleset = { ruleset with Kuitan = false }

    /// 打开头跳：同巡多家荣和时只成立打牌者下家优先的那一家。关着的形态就是各预设本身——
    /// 天凤与雀魂都是双响（ADR-0004 决定 3）。
    let withAtamahane (ruleset: Ruleset) : Ruleset = { ruleset with Atamahane = true }

    /// 让三家和了成立而不流局（雀魂的做法）。开着的形态就是各预设本身——天凤把三家和了
    /// 列在途中流局里（ADR-0004）。
    let withoutSanchaHoraRyuukyoku (ruleset: Ruleset) : Ruleset =
        { ruleset with
            SanchaHoraRyuukyoku = false
        }

    /// 打开切上满贯（SPEC 的规则开关）：4 番 30 符 / 3 番 60 符按满贯算。
    /// 关着的形态就是各预设本身——天凤与雀魂段位战都不采用它。
    let withKiriageMangan (ruleset: Ruleset) : Ruleset = { ruleset with KiriageMangan = true }

    /// 允许国士无双抢暗杠（雀魂的做法）。关着的形态就是各预设本身——天凤禁止。
    let withKokushiAnkanChankan (ruleset: Ruleset) : Ruleset =
        { ruleset with
            KokushiAnkanChankan = true
        }

    /// 换对局长度（spec 的「对局长度」配置）。
    let withLength (length: GameLength) (ruleset: Ruleset) : Ruleset = { ruleset with Length = length }

    // ---- 拆解 ----

    /// 牌山总张数。牌山构成变了它就跟着变，因此别处不必知道 136 这个数。
    let wallSize (ruleset: Ruleset) : int =
        TileKindSet.count ruleset.TileKinds * max 0 ruleset.CopiesPerKind

    /// 配牌阶段要发出去的总张数。
    let haipaiTotal (ruleset: Ruleset) : int =
        max 0 ruleset.SeatCount * max 0 ruleset.HaipaiSize

    /// 一场对局的**局数序列**，按顺序，每项是「场风 × 局数」（「东 1 局」= `Ton, 1`）。
    /// 每个场风各打 `SeatCount` 局，因此四麻东风战 4 局、四麻半庄 8 局、三麻半庄 6 局——
    /// 这些数都是推出来的，不写死（05 票的验收）。连庄不改变序列，只是把同一项再打一遍。
    let kyokus (ruleset: Ruleset) : (Kaze * int) list =
        GameLength.bakazes ruleset.Length
        |> List.collect (fun bakaze -> [ for kyoku in 1 .. max 0 ruleset.SeatCount -> bakaze, kyoku ])

    /// 一场对局的总点数：座位数乘起手点数。**四家点数与供托之和恒为它**——
    /// 这是贯穿 05 / 08 / 12 / 14 的那条不变量。
    let startingTotal (ruleset: Ruleset) : int =
        max 0 ruleset.SeatCount * ruleset.StartingScore

    /// 供托的点数：供托记的是立直棒的根数，换算成点数要乘立直棒的面值。
    let kyotakuScore (ruleset: Ruleset) (kyotaku: int) : int = kyotaku * ruleset.RiichiBou

    /// 牌山的构成：每种正牌各 `CopiesPerKind` 张，其中 `Akadora` 列出的每种替换掉一张对应正牌。
    /// 这是**洗牌前**的规范形（mjai 顺序升序）；张数恒为 `wallSize`，红宝牌只换不加。
    let wallTiles (ruleset: Ruleset) : Tile list =
        let normals =
            TileKindSet.kinds ruleset.TileKinds
            |> List.collect (fun kind -> List.replicate (max 0 ruleset.CopiesPerKind) kind)

        let replaceOne (tiles: Tile list) (akadora: Tile) =
            let target = Tile.deaka akadora

            let rec loop (skipped: Tile list) (rest: Tile list) =
                match rest with
                | [] -> List.rev skipped // 对应正牌不在牌山里：不换，也不凭空多出一张
                | head :: tail when head = target -> List.rev skipped @ (akadora :: tail)
                | head :: tail -> loop (head :: skipped) tail

            loop [] tiles

        List.fold replaceOne normals ruleset.Akadora |> Tile.sort
