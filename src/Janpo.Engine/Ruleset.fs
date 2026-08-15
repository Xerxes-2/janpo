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
        /// 牌山里出现的牌种（正牌）。四麻是全部 34 种。
        TileKinds: Tile list
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
        /// ノーテン罰符（听牌料）：荒牌流局时不听牌的家合计付出的点数，听牌的家平分。
        /// 全听或全不听时不授受。它应当能被 `1 .. SeatCount - 1` 整除，否则授受不平：
        /// 3000 对四麣（1/2/3 家听）与三麣（1/2 家听）都成立，这也是 mjai 的算法。
        NotenBappu: int
    }

/// 规则集的预设与推导量。
[<RequireQualifiedAccess>]
module Ruleset =

    // ---- 构造 ----

    /// 四麻默认规则集：34 种正牌各 4 张（136 张），红 5 各一张，
    /// 配牌 13 张，王牌 14 张（4 张岭上 + 5 叠表里宝牌指示牌），起手 25000，听牌料 3000。
    let yonma: Ruleset =
        {
            SeatCount = 4
            TileKinds = Tile.kinds
            CopiesPerKind = 4
            Akadora = Tile.akadoraKinds
            Kuitan = true
            HaipaiSize = 13
            DeadWallSize = 14
            RinshanCount = 4
            StartingScore = 25000
            NotenBappu = 3000
        }

    /// 关掉红宝牌（SPEC 的「红宝牌有无」开关）。开着的形态就是各预设本身。
    let withoutAkadora (ruleset: Ruleset) : Ruleset = { ruleset with Akadora = [] }

    /// 关掉食断（SPEC 的「食断有无」开关）：副露后断幺九不成立。
    let withoutKuitan (ruleset: Ruleset) : Ruleset = { ruleset with Kuitan = false }

    // ---- 拆解 ----

    /// 牌山总张数。牌山构成变了它就跟着变，因此别处不必知道 136 这个数。
    let wallSize (ruleset: Ruleset) : int =
        List.length ruleset.TileKinds * max 0 ruleset.CopiesPerKind

    /// 配牌阶段要发出去的总张数。
    let haipaiTotal (ruleset: Ruleset) : int =
        max 0 ruleset.SeatCount * max 0 ruleset.HaipaiSize

    /// 牌山的构成：每种正牌各 `CopiesPerKind` 张，其中 `Akadora` 列出的每种替换掉一张对应正牌。
    /// 这是**洗牌前**的规范形（mjai 顺序升序）；张数恒为 `wallSize`，红宝牌只换不加。
    let wallTiles (ruleset: Ruleset) : Tile list =
        let normals =
            ruleset.TileKinds
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
