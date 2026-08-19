namespace Janpo.Engine.Tests

open FsCheck
open FsCheck.FSharp
open Janpo

/// 局面的固件：开局、把一局跑完、以及两种选手。具名用例与属性共用。
module GameStateFixtures =

    let ruleset = Ruleset.yonma

    let context = KyokuContext.initial ruleset

    let kindSet = ruleset.TileKinds

    /// 开一局。开不出来说明前序票坏了，直接让测试失败。
    let start (seed: int) : GameState * Rng =
        match GameState.start ruleset context (Rng.ofSeed seed) with
        | Ok pair -> pair
        | Error error -> failwith $"应当开得出局，却得到 {KyokuStartError.toDisplay error}"

    let removeOne (tile: Tile) (tiles: Tile list) : Tile list =
        let rec loop skipped rest =
            match rest with
            | [] -> List.rev skipped
            | head :: tail when head = tile -> List.rev skipped @ tail
            | head :: tail -> loop (head :: skipped) tail

        loop [] tiles

    /// 逐张拿掉给定的那几张。
    let removeAll (tiles: Tile list) (from: Tile list) : Tile list =
        (from, tiles) ||> List.fold (fun rest tile -> removeOne tile rest)

    /// 配牌的取牌手顺（4-4-4-1）：与 `Wall.deal` 的手顺同一份，只是反过来用。
    let private haipaiChunks (ruleset: Ruleset) : int list =
        let rounds = ruleset.HaipaiSize / 4
        let rest = ruleset.HaipaiSize % 4
        [ for _ in 1..rounds -> 4 ] @ (if rest > 0 then [ rest ] else [])

    /// 配牌与摸牌一共用掉的那一串牌，按牌山里的位置顺序。
    let private usedBy (ruleset: Ruleset) (oya: Seat) (hands: Tile list list) (draws: Tile list) : Tile list =
        let plan =
            [
                for chunk in haipaiChunks ruleset do
                    for seat in Seat.orderFrom ruleset oya -> seat, chunk
            ]

        let dealt, _ =
            (([], hands), plan)
            ||> List.fold (fun (taken, remaining) (seat, chunk) ->
                let hand = Seat.tryItem seat remaining |> Option.defaultValue []

                let drawnNow = List.truncate chunk hand
                let rest = List.skip (min chunk (List.length hand)) hand

                taken @ drawnNow, remaining |> Seat.mapAt seat (fun _ -> rest))

        dealt @ draws

    /// 整副牌里没被用掉的那些，用来把牌山补满。
    let private fillerFor (ruleset: Ruleset) (used: Tile list) : Tile list =
        let filler =
            (Ruleset.wallTiles ruleset, used)
            ||> List.fold (fun rest tile -> removeOne tile rest)

        if List.length filler <> Ruleset.wallSize ruleset - List.length used then
            failwith "摊牌山用了整副牌里没有的牌（同一种牌最多四张）"

        filler

    /// 摊一座牌山：四家按 `hands` 拿到配牌，之后按 `draws` 的顺序摸牌（`draws` 的第一张
    /// 是 Oya 的第一次自摸，之后依次是下家、对家、上家……），余下的位置用整副牌里
    /// 没被用掉的牌补满，末尾 `DeadWallSize` 张自然成为王牌。
    ///
    /// 黄金用例「让指定的和了在指定 Junme 发生」靠它：牌山是确定性的，不必去碰运气找种子。
    let scriptedWall (ruleset: Ruleset) (oya: Seat) (hands: Tile list list) (draws: Tile list) : Wall =
        let used = usedBy ruleset oya hands draws
        Wall.ofOrdered ruleset (used @ fillerFor ruleset used)

    /// 摊一座**连岭上牌也指定**的牌山：`rinshan` 按补摸顺序摆在王牌的头上，
    /// 不足 `RinshanCount` 张的部分由余牌补齐，多出来的那几张自然落进指示牌区。
    ///
    /// 岭上开花与责任支付的黄金用例非得指定补摸的那张不可。
    let scriptedWallWithRinshan
        (ruleset: Ruleset)
        (oya: Seat)
        (hands: Tile list list)
        (draws: Tile list)
        (rinshan: Tile list)
        : Wall =
        let used = usedBy ruleset oya hands draws
        let filler = fillerFor ruleset (used @ rinshan)
        let liveSize = Ruleset.wallSize ruleset - ruleset.DeadWallSize
        let liveFiller = List.truncate (liveSize - List.length used) filler
        let deadFiller = List.skip (List.length liveFiller) filler

        Wall.ofOrdered ruleset (used @ liveFiller @ rinshan @ deadFiller)

    /// 记法写的手牌。摊牌山的用例里手牌一律写 mjai 记法，读起来才像牌。
    let tilesOf (notations: string) : Tile list =
        match Tile.parseMany notations with
        | Ok parsed -> parsed
        | Error error -> failwith $"应当是合法记法，却得到 {TileListParseError.toDisplay error}"

    /// 一局的剧本：四家的配牌加上摸牌顺序，都写成 mjai 记法。
    type Script = { Hands: string list; Draws: string }

    /// 用一座摊好的牌山开一局。牌山是程序化摊出来的那几条用例（流し満貫）直接用它。
    let startFromWall (ruleset: Ruleset) (kyokuContext: KyokuContext) (wall: Wall) : GameState =
        match GameState.startFrom ruleset kyokuContext wall with
        | Ok state -> state
        | Error error -> failwith $"应当开得出局，却得到 {KyokuStartError.toDisplay error}"

    /// 用摊好的牌山开一局，规则集与开局条件都可换
    /// （头跳开关、本场与供托的那几条用例要它）。
    let startScriptedIn (ruleset: Ruleset) (kyokuContext: KyokuContext) (script: Script) : GameState =
        let hands = script.Hands |> List.map tilesOf
        let wall = scriptedWall ruleset kyokuContext.Oya hands (tilesOf script.Draws)

        startFromWall ruleset kyokuContext wall

    /// 用摊好的牌山开一局，规则集可换（头跳开关的两条用例要它）。
    let startScriptedWith (ruleset: Ruleset) (script: Script) : GameState = startScriptedIn ruleset context script

    /// 用摊好的牌山开一局。
    let startScripted (script: Script) : GameState = startScriptedWith ruleset script

    /// 用摊好的牌山开一局，**岭上牌也是摊出来的**（按补摸顺序写 mjai 记法）。
    let startScriptedRinshanIn (ruleset: Ruleset) (rinshan: string) (script: Script) : GameState =
        let hands = script.Hands |> List.map tilesOf

        let wall =
            scriptedWallWithRinshan ruleset context.Oya hands (tilesOf script.Draws) (tilesOf rinshan)

        match GameState.startFrom ruleset context wall with
        | Ok state -> state
        | Error error -> failwith $"应当开得出局，却得到 {KyokuStartError.toDisplay error}"

    /// 用摊好的牌山（含岭上牌）开一局。
    let startScriptedRinshan (rinshan: string) (script: Script) : GameState =
        startScriptedRinshanIn ruleset rinshan script

    /// 自摸和的剧本：Oya 听 5z 单骑，第 1 巡摸进没用的 1z 打掉，第 2 巡摸进 5z。
    /// 其余三家是一手孤张，既和不了也听不了。
    let tsumoHoraScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 4z 6z 7z 9m"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 4z 6z 7z 9m"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 4z 6z 7z 9m"
                ]
            Draws = "1z 2z 3z 4z 5z"
        }

    /// 荣和与振听的剧本：座位 1 与座位 2 都听 4p，座位 1 第 1 巡自己打掉 7p
    /// （它的另一张和了牌）因而永久振听；座位 0 第 2 巡摸进 4p 打出去。
    /// 之后座位 1 第 2 巡摸进 1p 打出、座位 2 摸进 3z、座位 3 再打出 1p——同巡振听的用例要这一段。
    ///
    /// 座位 2 是四顺子 + 非役牌雀头的**平和**形：1p 与 4p 两端都成平和，因此它的荣和不靠
    /// 「无役也能和」成立（无役不可和是 08 票接上的）。
    let ronFuritenScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 5s 8s 6p"
                    "2m 3m 4m 5m 6m 7m 2p 3p 4p 5p 6p 9s 9s"
                    "2s 3s 4s 6s 7s 8s 3m 4m 5m 9m 9m 2p 3p"
                    "6z 6z 7z 7z 3m 6m 9m 1s 4s 7s 1p 8p 9p"
                ]
            Draws = "6z 7p 7z 5z 4p 1p 3z 1p"
        }

    /// 自家鸣牌解除同巡振听的剧本（票 63 F 族，镜像 2025123101-2b47265f 第 6 局的形）：
    /// 座位 2 听 1p / 4p（234m 456m 777s + 55p + 23p）；座位 1 第 1 巡摸切 4p，
    /// 座位 2 不荣和而是吃它（3p5p）——见逃；打掉 2p 之后新听 5p 单骑，
    /// 座位 3 随即摸切 5p——天凤让这一荣和成立（断幺九）。
    let nakiLiftsFuritenScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 9m 9m 1s 9s 1m 5s 8s"
                    "2z 2z 3z 3z 6z 7z 1m 9p 9p 1s 4s 2m 8m"
                    "2m 3m 4m 4m 5m 6m 2p 3p 5p 5p 7s 7s 7s"
                    "6z 6z 7z 7z 1z 3z 4z 1m 8m 2s 6s 9s 9p"
                ]
            Draws = "1z 4p 5p"
        }

    /// 同巡三响的剧本：座位 1、2、3 都听 4p，座位 0 第 2 巡打出 4p。
    /// 头跳关掉时三家都成立（天凤把三家和了判成途中流局，那是 12 票的规则集字段）。
    /// 三家各自有役：座位 1 自风南刻子、座位 2 平和、座位 3 平和断幺九。
    let tripleRonScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 5s 8s 6p"
                    "5m 6m 7m 2z 2z 2z 3z 3z 3z 4z 4z 3p 5p"
                    "2s 3s 4s 6s 7s 8s 3m 4m 5m 9m 9m 2p 3p"
                    "2m 3m 4m 6m 7m 8m 3s 4s 5s 7s 7s 5p 6p"
                ]
            Draws = "6z 7z 9p 9s 4p"
        }

    /// 同巡双响的剧本：座位 2 听 1p / 4p、座位 3 听 4p / 7p，座位 0 第 2 巡打出 4p。
    /// 打牌者是座位 0，因此下家优先的顺序是 1 → 2 → 3，座位 2 排在座位 3 前面。
    /// 两家都是平和形（座位 3 还带断幺九），无役不可和不会把它们挡在外面。
    let doubleRonScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 5s 8s 6p"
                    "1z 1z 2z 2z 3z 3z 4z 4z 5z 6z 7z 9p 9s"
                    "2s 3s 4s 6s 7s 8s 3m 4m 5m 9m 9m 2p 3p"
                    "2m 3m 4m 6m 7m 8m 3s 4s 5s 7s 7s 5p 6p"
                ]
            Draws = "6z 7z 9p 9s 4p"
        }

    /// 无役的剧本：座位 2 听 2m 嵌张，但那手牌荣和时**一个役都没有**
    /// （不成平和——嵌张听；不成断幺九——带 1m 与 1z；三色 / 一气 / 混全带都不成）。
    ///
    /// 座位 0 第 2 巡摸进 2m 打出去：座位 2 既不振听、牌型也成立，但无役不可和，
    /// Ron 压根不进合法动作集；随后座位 2 自己第 2 巡摸进 2m，门前清自摸就是役，于是能和。
    let noYakuRonScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 5s 8s 6p"
                    "9m 9m 9p 9p 1s 1s 6z 6z 7z 7z 2z 3z 4z"
                    "1m 3m 5p 6p 7p 3s 4s 5s 7s 8s 9s 1z 1z"
                    "1p 2p 3p 4p 1m 4m 7m 6s 6s 2s 3z 4z 5z"
                ]
            Draws = "9s 9p 8p 8m 2m 9p 2m"
        }

    // ---- 立直的剧本（RiichiTests / RiichiProperties 共用） ----
    //
    // **随机取样几乎给不出立直局面**：票 96 把 `GameStateArbitraries` 的全域扫了一遍，
    // 21.7 万个局面里只有 104 个「有家立直中」。因此立直也要像杠那样摊牌山
    // （备注 N-8 的同一课），下面这几条剧本就是喂给它们的。

    /// 立直后见逃的剧本：Oya 听 5z 单骑，第 1 巡立直；座位 1 第 1 巡就摸切 5z，
    /// 它放过了那一张（见逃）——**立直后的见逃是永久振听**，之后座位 2 与 3 再摸切
    /// 3z / 4z，它都荣和不了；自己第 2 巡摸进 2z 仍旧听 5z。
    ///
    /// **同一座牌山换个选手就是另一条用例**：见和就和的选手（`riichiSeeking`）拿它跑出来的是
    /// **立直一发荣和**的五步一局，而那五步里有一步是「立直中的家被问荣和」
    /// （全域扫一遍只有 3 个那一类的局面，票 97）。
    let minogashiScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 1p 2p 3p 5z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 4z 6z 7z 9m"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 4z 6z 7z 9m"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 4z 6z 7z 9m"
                ]
            Draws = "1z 5z 3z 4z 2z"
        }

    /// 三家立直、其中两家立直后暗杠、一路打到荒牌流局的剧本（票 97）：
    ///
    /// - 座位 0：`123456789m + 555z + 1z`，第 1 巡摸进 6z 宣立直摸切，听 1z 单骑；
    ///   第 2 巡摸进第四张 5z——听牌与面子构成都不变，**杠得**（`RiichiState.allowsAnkan`）；
    /// - 座位 1（`111s 999s 111p 999p + 7z`）与座位 2（`222m 888m 222p 888p + 3z`）配牌就是
    ///   四暗刻单骑（听 7z / 3z），各自摸进 4z 宣立直摸切；座位 1 之后摸到第四张 1s，同样暗杠；
    /// - 座位 3 一手孤张，既和不了也听不了。
    ///
    /// **只有三家立直**（四家立直当场流局，那是 `suuchaRiichiScript`），且配它的选手
    /// `riichiKanSeeking` 从不宣言和了，因此这一局打到荒牌流局（听牌料 +1000 × 3 / −3000）：
    /// **75 步里 74 步都是「有家立直中」**——立直那一族属性的主力取值域是它。
    /// 这几句牌理都是引擎跑出来的，不是手算的（判据 19）。
    let sanchaRiichiAnkanScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 5z 5z 5z 1z"
                    "1s 1s 1s 9s 9s 9s 1p 1p 1p 9p 9p 9p 7z"
                    "2m 2m 2m 8m 8m 8m 2p 2p 2p 8p 8p 8p 3z"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 6z 7z 4m 5m"
                ]
            Draws = "6z 4z 4z 4z 5z"
        }

    // ---- 杠的剧本（KanTests / KanProperties 共用） ----

    /// 暗杠的剧本：Oya 手里三张 9s 加一条 123456789m 与 5z 单骑，第一次自摸就摸进第四张 9s。
    /// 岭上牌摊的是 `5z`，因此这一杠直接岭上开花（暗杠不破门清，还有门前清自摸和与一气通贯）。
    let ankanScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 9s 9s 9s 5z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 4z 6z 7z 2m"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 4z 6z 7z 3m"
                    "3p 6p 9p 3s 6s 8s 1z 2z 3z 4z 6z 7z 4m"
                ]
            Draws = "9s"
        }

    /// 立直后暗杠的剧本：Oya 第一手摸进 6z，宣立直打 1z（听 6z 单骑）；
    /// 三家各摸切一张之后，Oya 第二手摸进第四张 5z——听牌与面子构成都不变，杠得。
    let riichiAnkanScript =
        {
            Hands =
                [
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 5z 5z 5z 1z"
                    "1p 4p 7p 1s 4s 7s 1z 2z 3z 6z 7z 2m 8m"
                    "2p 5p 8p 2s 5s 8s 1z 2z 3z 6z 7z 3m 8m"
                    "3p 6p 9p 3s 6s 9s 1z 2z 3z 6z 7z 4m 8m"
                ]
            Draws = "6z 4z 4z 4z 5z"
        }

    /// 送り杠的剧本：Oya 立直时手里就握着四张 1p（`111p + 123p`），听 9s 单骑。
    /// 之后摸进的不是 1p，因此那四张**杠不得**——禁送り杠。
    let okuriKanScript =
        {
            Hands =
                [
                    "1p 1p 1p 1p 2p 3p 4m 5m 6m 7m 8m 9m 9s"
                    "4p 7p 1s 4s 7s 1z 2z 3z 6z 7z 2m 8m 9p"
                    "5p 8p 2s 5s 8s 1z 2z 3z 6z 7z 3m 8m 9p"
                    "6p 9p 3s 6s 9s 1z 2z 3z 6z 7z 4m 8m 1m"
                ]
            Draws = "3z 4z 4z 4z 5z"
        }

    /// 大明杠的剧本：Oya 第一手摸切 5s，座位 1 手里三张 5s 全亮出去大明杠；
    /// 岭上牌摊的是 `1z`，正好给它配成雀头——大明杠后的岭上开花，责任支付落在 Oya 头上。
    let minkanScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 6s 8s 6p"
                    "5s 5s 5sr 1m 2m 3m 4m 5m 6m 7m 8m 9m 1z"
                    "2p 5p 8p 2s 8s 9s 2z 3z 4z 6z 7z 3m 4p"
                    "3p 6p 9p 3s 6s 9s 2z 3z 4z 6z 7z 4m 7p"
                ]
            Draws = "5s"
        }

    /// 加杠与抢杠的剧本：Oya 第一手摸切 5s，座位 1 碰它；一圈之后座位 1 摸进第四张 5s 加杠。
    ///
    /// 座位 2 一开局就听 5s / 8s（一气通贯 + 平和）：它先见逃了那张 5s（只是同巡振听，
    /// 摸过一张就解除），因此后来抢得了那个加杠。
    let kakanScript =
        {
            Hands =
                [
                    "5z 6z 7z 2m 4m 8m 1s 4s 7s 3p 4p 7p 9p"
                    "5s 5sr 1m 2m 3m 4m 5m 6m 7m 8m 9m 1z 1z"
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 6s 7s 9p 9p"
                    "2p 5p 8p 2s 3s 9s 1z 2z 3z 4z 6z 7z 6p"
                ]
            Draws = "5s 4z 4z 4z 5s"
        }

    /// **抢杠必然发生**的剧本（票 98）：Oya 第一手摸切 5s，座位 1 碰它；一圈之后座位 1
    /// 摸进第四张 5s 加杠，而座位 2 正好听 5s ——它抢了那个杠。
    ///
    /// 与 `kakanScript` 的差别是**座位 2 荣和 5s 时一个役都没有**（`123m 456m 789p + 99m + 46s`：
    /// 嵌张听不成平和、带 1m 9m 9p 不成断幺九，三色 / 一气 / 混全带一个都不成）。这不是花活，
    /// 是这条轨迹走得下去的**前提**：无役不可和 ⇒ Oya 打出那张 5s 时它压根不被问，于是那张 5s
    /// 才轮得到座位 1 碰；抢杠时**抢杠本身就是役**（1 番 40 符 1300 点，引擎跑出来的数）。
    /// 见和就和的选手拿 `kakanScript` 跑只会在第 1 巡就荣和收场，一个杠都摸不到。
    ///
    /// 其余两家全是死牌：Oya 的配牌是九种幺九牌（顺手把九种九牌那一支也带进这条轨迹），
    /// 座位 1 除了两张 5s 全是字牌对子（它打出去谁也碰不走、吃不了），座位 3 一手孤张。
    /// **九步跑完**：摸切 → 碰 → 手切 → 三家各摸切一张 → 加杠 → 抢杠 → 终局。
    let chankanScript =
        {
            Hands =
                [
                    "1m 9m 1p 9p 1s 9s 1z 2z 3z 2m 4m 6m 8m"
                    "5s 5sr 1z 1z 2z 2z 3z 3z 5z 5z 6z 6z 7z"
                    "1m 2m 3m 4m 5m 6m 7p 8p 9p 4s 6s 9m 9m"
                    "2p 5p 8p 2s 3s 9s 4z 6z 7z 6p 3p 7s 4p"
                ]
            Draws = "5s 4z 4z 4z 5s"
        }

    /// 加杠后当场岭上开花的剧本（**明杠的新宝牌翻牌时机**，16 票）：
    /// Oya 第一手摸切 5s，座位 1 碰它；一圈之后座位 1 摸进第四张 5s 加杠，
    /// 岭上牌正好是它单骑听的 1z——**加杠 → 岭上开花**，中间没有任何一次打牌。
    ///
    /// 与 `kakanScript` 的差别就在其余三家全是孤张：**没人抢得了那个杠**，因此它当场成立。
    /// 配套的岭上牌写 `kakanRinshan`：新宝牌指示牌摩成 4s（对应宝牌 5s），
    /// 一旦多翻一张，那四张 5s 的杠就多出四番——真牌谱里那局就是这么算错的。
    let kakanRinshanScript =
        {
            Hands =
                [
                    "5z 6z 7z 2m 4m 8m 1s 4s 7s 3p 4p 7p 9p"
                    "5s 5sr 1m 2m 3m 4m 5m 6m 7m 8m 9m 1z 1z"
                    "2p 5p 8p 2s 8s 9s 2z 3z 4z 6z 7z 3m 6p"
                    "3p 6p 9p 3s 6s 9s 2z 3z 6z 7z 4m 7p 1p"
                ]
            Draws = "5s 4z 4z 4z 5s"
        }

    /// `kakanRinshanScript` 配套的王牌：头四张是岭上牌（只用得到第一张 `1z`），
    /// **多出来的三张落进指示牌区**（`scriptedWallWithRinshan` 写明了这一点）：
    /// 开局那叠是（表 `2z`，里 `8p`），杠那叠的表指示牌是 `4s`。
    ///
    /// 于是两边都是确定的事实：开局宝牌 3z（座位 1 一张没有），杠宝牌 5s（它杠了四张）。
    let kakanRinshan = "1z 9p 9p 8p 2z 8p 4s"

    /// 大明杠之后没打牌就又暗杠一次的剧本（两种翻牌时机同时在场，16 票）：
    /// Oya 第一手摸切 5s，座位 1 三张 5s 全亮出去大明杠；补摸的岭上牌是第四张 9m，
    /// 它接着暗杠那四张——暗杠当场翻一张，而大明杠那一张仍欠着，等着接下来的打牌。
    let minkanAnkanScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 6s 8s 6p"
                    "5s 5s 5sr 9m 9m 9m 1m 2m 3m 4m 5m 6m 1z"
                    "2p 5p 8p 2s 8s 9s 2z 3z 4z 6z 7z 3m 4p"
                    "3p 6p 9p 3s 6s 9s 2z 3z 4z 6z 7z 4m 7p"
                ]
            Draws = "5s"
        }

    /// 打牌前连着两次明杠的剧本（**两次都是明杠**，票 59）：
    /// 座位 1 先碰 Oya 摸切的 5s、再碰座位 2 摸切的 9m；一圈之后摸进第四张 5s 加杠，
    /// 补摸的岭上牌正是第四张 9m——**没打牌就又加一次杠**。
    ///
    /// 全量语料 129,179 局里这一类（先明杠、紧接着又一杠）共 29 局，其中 28 局与天凤不符：
    /// 天凤在**第二次杠成立时**就把第一次欠的那张指示牌翻了，引擎原来改到打牌那一刻才翻。
    let kakanKakanScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 6s 8s 6p"
                    "5s 5sr 9m 9m 1m 2m 3m 4m 5m 6m 7m 8m 1z"
                    "2p 5p 8p 2s 8s 9s 2z 3z 4z 6z 7z 3m 4p"
                    "3p 6p 9p 3s 6s 9s 2z 3z 4z 6z 7z 4m 7p"
                ]
            Draws = "5s 9m 7s 7s 7s 5s"
        }

    /// `kakanKakanScript` 配套的王牌：头四张是岭上牌（第一杠补摸 `9m`、第二杠补摸 `9s`），
    /// 其后逐叠是（表，里）指示牌：开局那叠 `4p` / `1p`、第一杠那叠 `1m` / `1p`、第二杠那叠 `9p`。
    /// 三张表指示牌两两不同，因此「哪一步翻出了哪一张」是确定的事实。
    let kakanKakanRinshan = "9m 9s 1p 1p 4p 1p 1m 1p 9p"

    /// 国士抢暗杠的剧本：座位 1 第一手摸进第四张 7z 暗杠，而座位 2 是等 7z 的国士无双。
    /// 能不能抢看规则集（`KokushiAnkanChankan`：天凤禁、雀魂允）。
    let kokushiChankanScript =
        {
            Hands =
                [
                    "4m 5m 6m 7m 8m 9m 2p 3p 6p 9p 2s 3s 4s"
                    "7z 7z 7z 2m 3m 4m 5m 6m 7m 2p 3p 4p 5p"
                    "1m 1m 9m 1p 9p 1s 9s 1z 2z 3z 4z 5z 6z"
                    "2m 3m 5s 6s 7s 8s 5p 6p 7p 8p 4s 3s 2s"
                ]
            Draws = "3z 7z"
        }

    /// 三个杠的剧本（四杠前的那一个）：Oya 手里 111m 222m 333m，摸进第四张 1m 暗杠，
    /// 补摸的岭上牌又恰好是 2m、再下一张是 3m——一口气三个暗杠。
    /// 四杠散了是 12 票的，本票只验到第三个杠为止。
    let threeKanScript =
        {
            Hands =
                [
                    "1m 1m 1m 2m 2m 2m 3m 3m 3m 9p 9p 5z 6z"
                    "4m 5m 6m 1p 4p 7p 1s 4s 7s 1z 2z 3z 4z"
                    "7m 8m 9m 2p 5p 8p 2s 5s 8s 1z 2z 3z 4z"
                    "4m 5m 7m 3p 6p 9p 3s 6s 9s 1z 2z 3z 4z"
                ]
            Draws = "1m"
        }

    /// 杠撞车的剧本：Oya 第一手摸切 5s，三家各有所图——
    /// 座位 1（下家）拿 `6s 7s` 吃得，座位 2 手里三张 5s 碰得也大明杠得，
    /// 座位 3 听 5s / 8s 荣和得（一气通贯 + 平和）。优先级 Ron > Pon / Minkan > Chi 的两条边都在这一手上。
    let kanRaceScript =
        {
            Hands =
                [
                    "1z 1z 2z 3z 4z 5z 2m 4m 8m 2s 8s 6p 4p"
                    "6s 7s 1p 4p 7p 1s 4s 7s 1z 2z 3z 6z 7z"
                    "5s 5s 5sr 2p 5p 8p 2s 9s 3m 6z 7z 4z 4z"
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 6s 7s 9p 9p"
                ]
            Draws = "5s"
        }

    /// 大三元包的剧本：座位 1 碰完三组三元牌，**第三组（中）是座位 2 点的**，
    /// 因此它要为这个大三元负责；随后座位 1 自摸 9p 和了。
    let daisangenScript =
        {
            Hands =
                [
                    "1p 2p 3p 4p 5p 6p 7p 8p 1s 2s 3s 4m 5m"
                    "5z 5z 6z 6z 7z 7z 1m 2m 3m 9p 1z 2z 3z"
                    "2p 5p 8p 6s 7s 8s 6m 7m 8m 1z 2z 3z 9s"
                    "3p 6p 9p 3s 4s 5s 4m 7m 9m 1z 2z 3z 1s"
                ]
            Draws = "5z 6z 7z 4z 4z 4z 9p"
        }

    // ---- 流局形态的剧本（RyuukyokuTests / RyuukyokuProperties 共用） ----
    //
    // **这几种形态随机取样几乎跑不出来**（四风连打 / 四杠散了 / 九种九牌 / 四家立直
    // 都极稀），因此一律摊牌山写黄金用例，把覆盖钉成用例——备注 N-8 的同一课。

    /// 九种九牌的剧本：Oya 配牌里就摊着九种幺九牌（1m 9m 1p 9p 1s 9s 1z 2z 3z），
    /// 第一次自摸摸进一张没用的 6m——第一巡、无人鸣牌，宣言得了。
    /// 它手切 2m 之后幺九牌一张不少，因此「只有第一巡宣言得了」用同一条剧本就验得到。
    let kyuushuScript =
        {
            Hands =
                [
                    "1m 9m 1p 9p 1s 9s 1z 2z 3z 2m 3m 4m 5m"
                    "4p 5p 6p 4s 5s 6s 6m 7m 8m 4z 5z 6z 7z"
                    "2p 3p 7p 2s 3s 7s 5m 6m 7m 4z 5z 6z 7z"
                    "6p 7p 8p 6s 7s 8s 7m 8m 2m 4z 5z 6z 7z"
                ]
            Draws = "6m"
        }

    /// 鸣牌之后就宣言不了九种九牌的剧本：Oya 摸切 5z，座位 1 碰它；
    /// 座位 2 手里本来就有九种幺九牌，但轮到它摸牌时「无人鸣牌的第一巡」已经没了。
    let kyuushuAfterNakiScript =
        {
            Hands =
                [
                    "1m 4m 7m 1p 4p 7p 2s 5s 8s 3z 4z 6z 7z"
                    "5z 5z 2m 5m 8m 2p 5p 8p 3s 6s 9s 6z 7z"
                    "1m 9m 1p 9p 1s 9s 1z 2z 3z 3m 6m 3p 6p"
                    "2m 3m 5m 6m 8m 2p 3p 6p 7p 3s 4s 7s 4z"
                ]
            Draws = "5z 7m"
        }

    /// 四风连打的剧本：Oya 摸进 1z 摸切，其余三家配牌里各握一张 1z，依次手切。
    /// 四家手牌全是孤张，既和不了也听不了；四张 1z 全散在四家手上，因此碰不成、也无人鸣得了。
    /// 最后一手摸进的 2z 是另一条用例的材料：打它而不打 1z，四风连打就不成立。
    let suufonRendaScript =
        {
            Hands =
                [
                    "1m 4m 7m 1p 4p 7p 1s 4s 7s 4z 5z 6z 7z"
                    "1z 2m 5m 8m 2p 5p 8p 2s 5s 8s 5z 6z 7z"
                    "1z 3m 6m 9m 3p 6p 9p 3s 6s 9s 5z 6z 7z"
                    "1z 1m 4m 7m 1p 4p 7p 1s 4s 7s 4z 4z 3z"
                ]
            Draws = "1z 2z 2z 2z"
        }

    /// 四杠散了的剧本：Oya 手里 111m 222m 333m，摸进第四张 1m 暗杠，岭上又是 2m、再一张 3m
    /// ——一口气三个暗杠；第三张岭上摸进的 9p 它摸切出去，而座位 1 手里三张 9p 大明杠
    /// ——**第四个杠不是同一家杠的**，它打完岭上那一张就四杠散了。
    let suukaikanScript =
        {
            Hands =
                [
                    "1m 1m 1m 2m 2m 2m 3m 3m 3m 4z 5z 6z 7z"
                    "9p 9p 9p 4m 5m 6m 7m 8m 9m 1p 2p 3p 1s"
                    "4p 5p 6p 4s 5s 6s 2s 3s 7s 1z 2z 3z 4z"
                    "7p 8p 6s 7s 8s 9s 5m 6m 8m 1z 2z 3z 4z"
                ]
            Draws = "1m"
        }

    /// 单人四杠不流局的剧本：同上，只是第四个杠也是 Oya 自己的（岭上摸进第四张 9p）。
    let singleSuukanScript =
        {
            Hands =
                [
                    "1m 1m 1m 2m 2m 2m 3m 3m 3m 9p 9p 9p 4z"
                    "4m 5m 6m 7m 8m 9m 1p 2p 3p 1s 2s 3s 5z"
                    "4p 5p 6p 4s 5s 6s 2s 3s 7s 1z 2z 3z 6z"
                    "7p 8p 6s 7s 8s 9s 5m 6m 8m 1z 2z 3z 7z"
                ]
            Draws = "1m"
        }

    /// 四家立直的剧本：四家配牌就都是四组刻子加一张字牌的单骑听牌（分别听 5z / 6z / 7z / 4z），
    /// 第一次自摸摸进的 1z / 2z / 3z / 1z 谁也和不了也荣和不了——四家依次立直摸切。
    let suuchaRiichiScript =
        {
            Hands =
                [
                    "1m 1m 1m 9m 9m 9m 1p 1p 1p 9p 9p 9p 5z"
                    "1s 1s 1s 9s 9s 9s 2m 2m 2m 8m 8m 8m 6z"
                    "3m 3m 3m 7m 7m 7m 3p 3p 3p 7p 7p 7p 7z"
                    "2p 2p 2p 8p 8p 8p 2s 2s 2s 8s 8s 8s 4z"
                ]
            Draws = "1z 2z 3z 1z"
        }

    /// 流し満貫的牌山：**座位 0 的配牌与每一次自摸全是幺九牌**，它一路摸切下来，
    /// 河里因此全是幺九牌；其余三家拿到的先是中张，因此它们不成立。
    ///
    /// **它必须程序化地摊**：可摸区整整 70 张都要指定（座位 0 要摸满 18 巡），
    /// 写成记法字串就是一堆没人读得下去的牌。
    ///
    /// `nakiPai` 是「河被鸣走因此不成立」那条用例的钩子：给座位 1 的配牌发两张同种牌，
    /// 并把第三张排到**座位 0 的最后一次自摸**上——那一手可摸区还剩一张，碰得成，
    /// 且座位 1 碰完不摸牌，**座位 0 先前的摸牌一张不受影响**（河仍旧全是幺九牌）。
    /// 两条用例于是只差一个 `kawaTaken`。
    let nagashiManganWall (ruleset: Ruleset) (nakiPai: Tile option) : Wall =
        let isYaochuu (tile: Tile) =
            Tile.suit tile = Jihai || Tile.number tile = 1 || Tile.number tile = 9

        let reserved =
            nakiPai |> Option.map (fun tile -> [ tile; tile ]) |> Option.defaultValue []

        let yaochuu, chuuchan =
            Ruleset.wallTiles ruleset |> removeAll reserved |> List.partition isYaochuu

        // 座位 0 的配牌全取幺九牌；其余三家先拿指定的那几张，不够的部分拿中张。
        let oyaHand = List.truncate ruleset.HaipaiSize yaochuu
        let restYaochuu = List.skip (List.length oyaHand) yaochuu

        let others, afterOthers =
            (([], chuuchan), [ reserved; []; [] ])
            ||> List.fold (fun (hands, pool) fixedPart ->
                let take = ruleset.HaipaiSize - List.length fixedPart
                hands @ [ fixedPart @ List.truncate take pool ], List.skip take pool)

        // 可摸区减掉配牌占的那一段，就是一局里摸得到的张数（四麻：122 − 52 = 70）。
        let drawCount =
            Ruleset.wallSize ruleset - ruleset.DeadWallSize - Ruleset.haipaiTotal ruleset

        // 可摸区：下标 0 起按座位轮转，座位 0 的那几个位置填幺九牌，其余位置填中张（不够了再拿幺九牌）。
        let draws =
            (([], restYaochuu, afterOthers), [ 0 .. drawCount - 1 ])
            ||> List.fold (fun (taken, mineLeft, othersLeft) index ->
                if index % ruleset.SeatCount = 0 then
                    taken @ List.truncate 1 mineLeft, List.skip 1 mineLeft, othersLeft
                else
                    match othersLeft with
                    | head :: tail -> taken @ [ head ], mineLeft, tail
                    | [] -> taken @ List.truncate 1 mineLeft, List.skip 1 mineLeft, othersLeft)
            |> fun (taken, _, _) -> taken

        // 座位 0 的**最后一次自摸**换成 `nakiPai`：两张对换，牌山的多重集不变，
        // 且两边都是幺九牌（座位 0 的位置本来就只填幺九牌），因此它的河仍旧全是幺九牌。
        let scripted =
            match nakiPai with
            | None -> draws
            | Some tile ->
                let last = drawCount - 1 - ((drawCount - 1) % ruleset.SeatCount)

                match draws |> List.tryFindIndex (fun each -> each = tile) with
                | Some found when found <> last ->
                    draws
                    |> List.mapi (fun index each ->
                        if index = last then tile
                        elif index = found then List.item last draws
                        else each)
                | Some _ -> draws
                | None -> failwith $"{Tile.toMjai tile} 没有出现在可摸区里，换不到座位 0 的最后一手上"

        scriptedWall ruleset Seat.first (oyaHand :: others) scripted

    /// 某座位摸到第几巡了：它的 `tsumo` 事件条数。引擎的 `GameState.junme` 算的是同一件事，
    /// 这里从事件流重新数一遍——两条路径对上才算数对。
    let junmeOf (seat: Seat) (state: GameState) : int =
        GameState.events state
        |> List.filter (fun event ->
            match event with
            | Tsumo(actor, _) -> actor = seat
            | StartGame _
            | StartKyoku _
            | Dahai _
            | Pon _
            | Chi _
            | Hora _
            | Ryuukyoku _
            | Riichi _
            | RiichiAccepted _
            | EndKyoku
            | Ankan _
            | Kakan _
            | Minkan _
            | Dora _
            | EndGame -> false)
        |> List.length

    /// 这一局里成立的立直根数：`reach_accepted` 的条数。一根立直棒对应一条事件，
    /// 真实牌谱重建点数用的也是这条式子（上一局点数 − 1000 × reach_accepted + Σdeltas）。
    let acceptedRiichiCount (state: GameState) : int =
        GameState.events state
        |> List.filter (fun event ->
            match event with
            | RiichiAccepted _ -> true
            | StartGame _
            | StartKyoku _
            | Tsumo _
            | Dahai _
            | Pon _
            | Chi _
            | Riichi _
            | Hora _
            | Ryuukyoku _
            | EndKyoku
            | Ankan _
            | Kakan _
            | Minkan _
            | Dora _
            | EndGame -> false)
        |> List.length

    /// 某座位的副露。
    let nakiOf (seat: Seat) (state: GameState) : Naki list =
        GameState.player seat state
        |> Option.map PlayerState.naki
        |> Option.defaultValue []

    let shantenOf (tiles: Tile list) : int =
        match HandShape.create 0 tiles with
        | Ok shape -> Shanten.value (Shanten.calculate kindSet shape)
        | Error _ -> System.Int32.MaxValue

    /// 尽量保持听牌的选手：打出后向听数最小的那张（并列取合法动作集里靠前的那条）。
    /// 随机选手几乎永远不听牌，听牌料的黄金用例要它。
    ///
    /// **它从不宣言和了、响应一律「过」**：听牌料的那几条黄金用例要求这一局必然打到荒牌流局。
    let tenpaiSeeking: Player<Rng> =
        fun rng state choice ->
            let hand =
                GameState.player choice.Seat state
                |> Option.map PlayerState.hand
                |> Option.defaultValue []

            let dahais =
                choice.Actions
                |> List.choose (fun action ->
                    match action with
                    | Action.Dahai(_, pai, _) -> Some(action, pai)
                    | Action.Hora _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.Ryuukyoku _
                    | Action.None _ -> None)

            let chosen =
                match dahais with
                | [] -> Action.None choice.Seat
                | _ -> dahais |> List.minBy (fun (_, pai) -> shantenOf (removeOne pai hand)) |> fst

            chosen, rng

    /// 见和就和的选手：能自摸和 / 荣和就宣言，否则摸切，响应阶段不和就「过」。
    /// 和了那几条黄金用例要它——随机选手几乎永远和不了。
    let horaSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Hora _ -> true
                    | Action.Dahai _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.Ryuukyoku _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 摸切选手：一律摸切，响应一律「过」（**也从不鸣牌**），**从不宣言和了**。
    /// 振听与同巡振听的黄金用例要它——见逃是它的默认行为。
    /// 鸣牌一律打手切那一手（鸣完没摸切可打），因此它在鸣牌之后也推得动局面。
    let passive: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                    | Action.Hora _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.Ryuukyoku _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 能立直就立直的选手：能和先和，否则能立直就宣言，接下来优先摸切（宣言牌只能从
    /// 听牌形里挑，摸切不在其中时取第一条手切），响应一律「过」。
    ///
    /// 随机与听牌选手都从不宣言立直，而一发、立直棒与供托守恒这几条不变量非得有立直的轨迹不可。
    let riichiSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Hora _ -> true
                    | Action.Dahai _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.Ryuukyoku _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Riichi _ -> true
                        | Action.Dahai _
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai _ -> true
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 见立直就立直、之后见暗杠就暗杠的选手，**从不宣言和了**：
    /// 能立直就宣言，能暗杠就杠，其余时候优先摸切（摸切不在合法动作里时取第一条手切），
    /// 响应一律「过」。
    ///
    /// **它是为「立直 + 副露」那一支来的**：`riichiSeeking` 从不杠、`kanSeeking` 从不立直，
    /// 因此旧的取值域里「立直中且有副露」的局面**一个都没有**（票 96 报告 §6.2），
    /// 而 `RiichiProperties.stillTenpai` 里 `nakiCount > 0` 那一支只能靠它喂。
    /// **不宣言和了**是故意的：一局打满十八巡，立直中的局面才够密（见逃就永久振听，
    /// 那也是真局面）。
    let riichiKanSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Riichi _ -> true
                    | Action.Dahai _
                    | Action.Hora _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.Ryuukyoku _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Ankan _ -> true
                        | Action.Dahai _
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai _ -> true
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 见鸣就鸣的选手：能碰就碰、能吃就吃（碰优先），其余时候打合法动作集里第一条打牌，
    /// **从不宣言和了**。鸣牌的不变量要一条副露密集的轨迹，随机选手鸣得太稀。
    let nakiSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Pon _ -> true
                    | Action.Chi _
                    | Action.Dahai _
                    | Action.Hora _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.Ryuukyoku _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Chi _ -> true
                        | Action.Pon _
                        | Action.Dahai _
                        | Action.Hora _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai _ -> true
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Hora _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 见杠就杠的选手：能杠就杠（暗杠 / 加杠 / 大明杠），能和就和（抢杠与岭上开花靠它），
    /// 其余时候摸切，响应一律「过」。杠的不变量要一条杠密集的轨迹，
    /// 随机选手杠得太稀（四个种子里慕得还不到一个）。
    let kanSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _ -> true
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Dahai _
                    | Action.Hora _
                    | Action.Riichi _
                    | Action.Ryuukyoku _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Hora _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Riichi _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Riichi _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Riichi _
                        | Action.Ryuukyoku _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 见和就和、见杠就杠、见碰就碰的选手，其余时候摸切（摸切不在合法动作里时取第一条手切）。
    ///
    /// **它是为抢杠那一支来的**：抢杠要先有人碰、再摸进第四张加杠，而
    /// `kanSeeking` 从不碰（碰不进它的偏好表）、`nakiSeeking` 从不杠也从不和，
    /// 两者都跑不出 `chankanScript` 那九步。和了排在杠前面：抢杠那一轮只有荣和与「过」。
    let chankanSeeking: Player<Rng> =
        fun rng _ choice ->
            let pick (predicate: Action -> bool) =
                choice.Actions |> List.tryFind predicate

            let chosen =
                pick (fun action ->
                    match action with
                    | Action.Hora _ -> true
                    | Action.Dahai _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Riichi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.Ryuukyoku _
                    | Action.None _ -> false)
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _ -> true
                        | Action.Dahai _
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Pon _ -> true
                        | Action.Dahai _
                        | Action.Hora _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai(_, _, tsumogiri) -> tsumogiri
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.Dahai _ -> true
                        | Action.Hora _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.None _ -> false))
                |> Option.orElseWith (fun () ->
                    pick (fun action ->
                        match action with
                        | Action.None _ -> true
                        | Action.Dahai _
                        | Action.Pon _
                        | Action.Chi _
                        | Action.Riichi _
                        | Action.Ankan _
                        | Action.Kakan _
                        | Action.Minkan _
                        | Action.Ryuukyoku _
                        | Action.Hora _ -> false))
                |> Option.defaultValue (List.head choice.Actions)

            chosen, rng

    /// 见立直就立直，其余照 `chankanSeeking`。
    ///
    /// **它是为「抢的那家先立直」那条轨迹来的**（票 99）：抢杠时和的是
    /// 「立直 + 一发 + 抢杠」，而一发要活到那一刻就得有人先立直。
    /// `riichiSeeking` 代替不了：它从不碰，而加杠要先有人碰。
    let chankanRiichiSeeking: Player<Rng> =
        fun rng state choice ->
            let declaration =
                choice.Actions
                |> List.tryFind (fun action ->
                    match action with
                    | Action.Riichi _ -> true
                    | Action.Dahai _
                    | Action.Hora _
                    | Action.Pon _
                    | Action.Chi _
                    | Action.Ankan _
                    | Action.Kakan _
                    | Action.Minkan _
                    | Action.Ryuukyoku _
                    | Action.None _ -> false)

            match declaration with
            | Some riichi -> riichi, rng
            | None -> chankanSeeking rng state choice

    /// 用给定选手推进，直到 `until` 成立或这一局终了。黄金用例用它跑到「有事发生」的那一步，
    /// 再自己提交那一手关键动作。
    let driveUntil (player: Player<Rng>) (until: GameState -> bool) (state: GameState) : GameState =
        let rec loop (rng: Rng) (state: GameState) =
            if until state then
                state
            else
                match GameState.legalActions state with
                | [] -> state
                | choice :: _ ->
                    let action, advanced = player rng state choice

                    match GameState.step state action with
                    | Error illegal -> failwith $"合法动作集里的动作应当被接受，却得到「{IllegalAction.toDisplay illegal}」"
                    | Ok(next, _) -> loop advanced next

        loop (Rng.ofSeed 1) state

    /// 从给定局面到终局逐步的全部局面，含首尾。不变量在每一步上验。
    let traceFrom (player: Player<Rng>) (rng: Rng) (state: GameState) : GameState list =
        let rec loop (rng: Rng) (state: GameState) (visited: GameState list) =
            match GameState.legalActions state with
            | [] -> List.rev (state :: visited)
            | choice :: _ ->
                let action, advanced = player rng state choice

                match GameState.step state action with
                | Error illegal -> failwith $"合法动作集里的动作应当被接受，却得到「{IllegalAction.toDisplay illegal}」"
                | Ok(next, _) -> loop advanced next (state :: visited)

        loop rng state []

    /// 一局从开局到终局逐步的全部局面，含开局与终局。
    let trace (player: Player<Rng>) (seed: int) : GameState list =
        let state, rng = start seed
        traceFrom player rng state

    /// 一局**必然出现杠**的全部局面：摊好的杠剧本（连岭上牌一起摊）+ 见杠就杠的选手。
    ///
    /// **光靠随机取样验不到杠**：见杠就杠的选手跑四个种子也只有一局杠得成
    /// （杠要手里四张同种，概率就那么小）。这是备注 N-8 的同一课：
    /// 没断言过覆盖率的属性只证明了没崩，没证明跑到过。
    let kanTrace (rinshan: string) (script: Script) : GameState list =
        traceFrom kanSeeking (Rng.ofSeed 1) (startScriptedRinshan rinshan script)

    /// 一局**必然出现抢杠**的全部局面：摊好的抢杠剧本 + 碰完就杠、见和就和的选手。
    /// 九步里恰好一步是「加杠宣言了、还没成立」那一轮（`ResponseCause.Kan`）。
    ///
    /// **它不在 `GameStateArbitraries.Traces` 里**，而是挂在 `KanProperties` 的定点锚点上。
    /// 票 98 量过两件事（`scripts/fsi/chankan-trace.fsx`）：一是买不到什么——权重 2 只换得到
    /// 一趟 47% 的开口率，要一趟必开口得占权重表的 40.5%（权重 22 / 合计 55），
    /// 而锚点是每趟 100%（这九步局面是锁死的，让采样去抽它找不出新 bug）；
    /// 二是挂上去会当场把**七条**现成的属性按红（两条轨迹合起来十条）——
    /// 那是真发现，归另一票，逐条写在报告 `98-chankan-never-sampled.md` 里。
    let chankanTrace (script: Script) : GameState list =
        traceFrom chankanSeeking (Rng.ofSeed 1) (startScripted script)

    /// 一局**国士抢暗杠**的全部局面：抢暗杠要规则集开着 `KokushiAnkanChankan`（雀魂），
    /// 天凤那一侧暗杠当场成立、压根不进响应阶段（四步就完：摸切 → 暗杠 → 抢 → 终局）。
    /// **取值域里永远不会有它**：那张表只用默认规则集，因此抢暗杠只有锚点守得住。
    let kokushiChankanTrace (script: Script) : GameState list =
        let soul = Ruleset.withKokushiAnkanChankan ruleset

        traceFrom kanSeeking (Rng.ofSeed 1) (startScriptedWith soul script)

    /// 一局**抢的那家先立直**的抢杠：同一座 `chankanScript` 的牌山，只换选手。
    /// 十步里第 8 步就是抢杠那一轮，和了是**立直 + 一发 + 抢杠**（3 番 40 符 5200 点）。
    ///
    /// **它为一发那条不变量来的**（票 99）：被抢的杠没有发生，因此它不打断一发——
    /// 少了这条轨迹，「杠宣言了、还没成立」与一发同时在场的局面一个也到不了。
    let chankanRiichiTrace (script: Script) : GameState list =
        traceFrom chankanRiichiSeeking (Rng.ofSeed 1) (startScripted script)

    /// 一局**必然有人立直**的全部局面：摊好的立直剧本 + 见立直就立直的选手。
    ///
    /// **光靠随机取样验不到立直**：见立直就立直的选手跑 400 颗种子、三万多个局面，
    /// 其中「有家立直中」的只有 104 个（票 96 的全域扫描）。这是 `kanTrace` 的同一课。
    let riichiTrace (script: Script) : GameState list =
        traceFrom riichiSeeking (Rng.ofSeed 1) (startScripted script)

    /// 一局**立直之后还暗杠**的全部局面：摊好的立直剧本 + 立直完照样杠、从不和了的选手。
    /// 「立直 + 副露」这个组合只有它喂得进来（见 `riichiKanSeeking`）。
    let riichiKanTrace (script: Script) : GameState list =
        traceFrom riichiKanSeeking (Rng.ofSeed 1) (startScripted script)

    /// 一局**以和了收尾**的全部局面。随机选手几乎永远和不了（扫过 400 个种子，一次都没有），
    /// 可达局面里必须有一批和了的，牌数守恒与手牌张数这些不变量才验得到和了这条路。
    let horaTrace (script: Script) : GameState list =
        traceFrom horaSeeking (Rng.ofSeed 1) (startScripted script)

    /// 把一局跑完，返回终局的局面。
    let runWith (player: Player<Rng>) (seed: int) : GameState =
        let state, rng = start seed

        match Kyoku.run player rng state with
        | Ok(final, _) -> final
        | Error illegal -> failwith $"这一局应当跑得完，却得到「{IllegalAction.toDisplay illegal}」"

    /// 跑一局，同时记下每一步提交的动作——回放要的就是这串动作。
    let record (player: Player<Rng>) (seed: int) : GameState * Action list =
        let rec loop (rng: Rng) (state: GameState) (taken: Action list) =
            match GameState.legalActions state with
            | [] -> state, List.rev taken
            | choice :: _ ->
                let action, advanced = player rng state choice

                match GameState.step state action with
                | Error illegal -> failwith $"合法动作集里的动作应当被接受，却得到「{IllegalAction.toDisplay illegal}」"
                | Ok(next, _) -> loop advanced next (action :: taken)

        let state, rng = start seed
        loop rng state []

    /// 回放：把一串动作从给定局面重新走一遍。
    let replay (actions: Action list) (state: GameState) : Result<GameState, IllegalAction> =
        (Ok state, actions)
        ||> List.fold (fun current action ->
            current
            |> Result.bind (fun state -> GameState.step state action |> Result.map fst))

    /// 终局的流局结果。
    let ryuukyokuOf (state: GameState) : Ryuukyoku =
        match GameState.ryuukyoku state with
        | Some result -> result
        | None -> failwith "这一局应当已经终了"

/// 局面与动作的生成器。局面只生成**可达**的那些：随机开一局、随机走若干步。
type GameStateArbitraries =

    /// 十三条轨迹：**权重、名字与「怎么跑出这一条」**。生成器与探针
    /// （`scripts/fsi/arbitrary-coverage.fsx`）读的是**同一份表**——采样占比要量得准，
    /// 探针就不能自己抄一份会飘的副本。
    ///
    /// **权重不是拍脑袋拍出来的**：一个样本先均匀取种子、再按权重取轨迹、再在那条轨迹里
    /// 均匀取一步，因此一类局面被抽中的概率是 `Σ (w/W) · 那条轨迹里它的密度`。
    /// 改权重前先用探针量一遍，改完再量一遍：**加一条轨迹就会挤掉别族的占比**
    /// （票 97 的报告里有逐族的改前改后对照表）。
    static member Traces(seed: int) : (int * string * (unit -> GameState list)) list =
        [
            4, "random", fun () -> GameStateFixtures.trace Kyoku.randomPlayer seed
            4, "tenpaiSeeking", fun () -> GameStateFixtures.trace GameStateFixtures.tenpaiSeeking seed
            // 副露密集的一局：鸣牌的不变量（牌数守恒、手牌张数、河被鸣走的记号）靠它验。
            // **权重 4 → 6 是票 97 补的补偿**：下面三条立直轨迹把它稀释了，而「刚鸣完那一手」
            // （禁食替、不摸牌、只有手切）只有 random 与它两条喂得出来，本来就只有 92.8% 的趟数开口。
            6, "nakiSeeking", fun () -> GameStateFixtures.trace GameStateFixtures.nakiSeeking seed
            // 杠密集的一局：王牌、岭上牌与新宝牌的不变量靠它验。
            4, "kanSeeking", fun () -> GameStateFixtures.trace GameStateFixtures.kanSeeking seed
            // 摊好的三局：一局三个暗杠、一局大明杠后岭上开花、一局暗杠后岭上开花。
            // 随机取样里杠太稀，杠的不变量非得有这几条轨迹不可。
            // `ankan` 的权重 1 → 2 同样是票 97 的补偿：它才三步，却背着「杠进得了动作集」
            // 将近六成的采样概率（改前全族 2.18% 里的 1.28%；三步里有一步是它）。
            2, "threeKan", fun () -> GameStateFixtures.kanTrace "2m 3m 7z" GameStateFixtures.threeKanScript
            1, "minkan", fun () -> GameStateFixtures.kanTrace "1z" GameStateFixtures.minkanScript
            2, "ankan", fun () -> GameStateFixtures.kanTrace "5z" GameStateFixtures.ankanScript
            // 立直密集的一局：它是取值域里**唯一的随机牌山立直来源**（票 96 那条真反例就出在这里），
            // 但它稀：三万多个局面里只有 104 个「有家立直中」。密度靠下面三条摊牌山的补，
            // **这一条的权重一张不减**：摊好的牌山局面固定，找新 bug 靠的是它的随机牌山。
            4, "riichiSeeking", fun () -> GameStateFixtures.trace GameStateFixtures.riichiSeeking seed
            // 摊好的三局立直（票 97）。旧表里「有家立直中」的样本只占 0.103%，
            // 算下来 `RiichiProperties` 一趟只有 9.8% 的概率开过口——十趟有九趟空转（判据 3）。
            // 三家立直到荒牌流局那一条是主力（75 步里 74 步立直中，且带着立直后的暗杠），
            // 四家立直与立直一发荣和那两条各只有九步与五步，分别喂「宣言牌还没打出去」
            // 与「立直中的家被问荣和」这两类局面（它们在旧表里全域只有 4 个与 3 个）。
            2, "riichiAnkan", fun () -> GameStateFixtures.riichiKanTrace GameStateFixtures.sanchaRiichiAnkanScript
            1, "suuchaRiichi", fun () -> GameStateFixtures.riichiTrace GameStateFixtures.suuchaRiichiScript
            1, "riichiRon", fun () -> GameStateFixtures.riichiTrace GameStateFixtures.minogashiScript
            // 摊好的两局：一局自摸和收尾、一局荣和收尾。
            1, "tsumoHora", fun () -> GameStateFixtures.horaTrace GameStateFixtures.tsumoHoraScript
            1, "doubleRon", fun () -> GameStateFixtures.horaTrace GameStateFixtures.doubleRonScript
        ]

    /// 把轨迹表接成 FsCheck 的带权重生成器。**每一条都是 `Gen.fresh`，不是 `Gen.constant`**：
    /// `Gen.constant` 的参数是即时求值的，那张表每构造一次就把整张表全跑完、只用其中一条
    /// （票 56 实测：取 400 个样本调了 4000 次 `traceFrom`，而一条轨迹是「把一局用 seeking 选手打完」）。
    /// `Gen.fresh` 在**选中之后**才求值，因此一个样本只跑一条（4000 → 400）。
    ///
    /// 票 97 把表拆成上面那份 `Traces` 时**抽样分布一字没变**：同一颗随机种子、
    /// 三种 `size` 各 200 个样本，拆前拆后的指纹逐个相同（权重本身的变动是另一回事，
    /// 逐族影响在票 97 的报告里）。
    static member private tracesFor(seed: int) : (int * Gen<GameState list>) list =
        GameStateArbitraries.Traces seed
        |> List.map (fun (weight, _, run) -> weight, Gen.fresh run)

    static member GameState() : Arbitrary<GameState> =
        gen {
            let! seed = Gen.choose (1, 400)
            let! states = seed |> GameStateArbitraries.tracesFor |> Gen.frequency
            let! index = Gen.choose (0, List.length states - 1)
            return List.item index states
        }
        |> Arb.fromGen

    /// 随便造的动作：座位可能越界，牌可能不在手里，摸切标志可能是瞎写的，
    /// 和了可能既不成型也不是刚打出的那张。
    /// 「非法动作一律返回值而不抛异常」「接受一个动作当且仅当它在合法动作集里」两条属性要的就是它。
    static member Action() : Arbitrary<Action> =
        // 越界的座位仍要取到（属性要的就是「非法动作不抛异常」），但**负数在类型层已经不存在**
        // 了：`Seat` 只能经 `Seat.ofIndex` 从裸整数来，负数在那里就被挡住（晨间裁决 R-5）。
        let seat = Gen.choose (0, Ruleset.yonma.SeatCount) |> Gen.map SeatFixtures.seat

        let dahai =
            gen {
                let! actor = seat
                let! pai = Gen.elements Tile.all
                let! tsumogiri = Gen.elements [ true; false ]
                return Action.Dahai(actor, pai, tsumogiri)
            }

        let hora =
            gen {
                let! actor = seat
                let! target = seat
                let! pai = Gen.elements Tile.all
                return Action.Hora(actor, target, pai)
            }

        let naki =
            gen {
                let! actor = seat
                let! target = seat
                let! pai = Gen.elements Tile.all
                let! consumed = Gen.listOfLength 2 (Gen.elements Tile.all)
                let! isPon = Gen.elements [ true; false ]

                return
                    if isPon then
                        Action.Pon(actor, target, pai, consumed)
                    else
                        Action.Chi(actor, target, pai, consumed)
            }

        let kan =
            gen {
                let! actor = seat
                let! target = seat
                let! pai = Gen.elements Tile.all
                let! consumed = Gen.listOfLength 3 (Gen.elements Tile.all)
                let! kind = Gen.elements [ 0; 1; 2 ]

                return
                    match kind with
                    | 0 -> Action.Ankan(actor, pai :: consumed)
                    | 1 -> Action.Kakan(actor, pai, consumed)
                    | _ -> Action.Minkan(actor, target, pai, consumed)
            }

        let riichi = seat |> Gen.map Action.Riichi
        let pass = seat |> Gen.map Action.None

        Gen.oneof [ dahai; hora; naki; kan; riichi; pass ] |> Arb.fromGen
