namespace Janpo.Web.Tests

open Xunit
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// 配桌上那三项规则开关（票 72）：**对局长度 / 赤宝牌 / 食断**，spec 的 story 13。
///
/// 三件事在这里钉住：
///
/// 1. **拨到的那一份与真在打的那一份是两回事**——拨完要按「重开」才生效
///    （与种子同一条路）。半场换规则会让同一份牌谱前后按两套规则算，而回放照的是
///    牌谱自带的那一份（ADR-0004），于是那份牌谱就再也重现不了。
/// 2. **开关真的传到了引擎**——重开之后牌谱里的 `ruleset` 逐字段跟着变（26-8），
///    关掉赤宝牌之后**事件流里一张赤牌都没有**（配了阳性对照：开着时它必须数得出来，
///    否则这条断言等于永远执行不到，判据 3）。
/// 3. **长度是规则集的一根轴而不是牌桌的字段**——局数序列由 `Ruleset.kyokus` 推，
///    而且真打一场：东风战打不到南场、半庄打得到。
///
/// localStorage 那一条不在这里（dotnet 上没有 localStorage）：它由浏览器闸门
/// `web/scripts/verify-setup.mjs` 守着——那一趟把页面重新打开一次，看三项还在不在。
module TableSetupTests =

    /// 四家自带 bot 的一桌（配桌那三项与坐法都从外面给，与 `?table=1` 打开时同一条路）。
    let private table (rules: RulesetDraft) : TableModel =
        TablePage.initial rules (SeatingPlan.initial (RulesetDraft.ruleset rules))
        |> fst

    let private step (message: TableMsg) (model: TableModel) : TableModel = TablePage.update message model |> fst

    let private liveOf (model: TableModel) : LiveTable =
        match TablePage.live model with
        | Some live -> live
        | None -> failwith "这几条用例跑的是 `?table=1` 那一页，它必然是 Live"

    let private tableOf (model: TableModel) : Table =
        match (liveOf model).Table with
        | Ok table -> table
        | Error error -> failwith $"这一桌应当开得起来，却得到「{error}」"

    /// 这一桌到此刻为止的牌谱（就是「导出牌谱」那个按钮给出去的那一份）。
    let private paifuOf (model: TableModel) : Paifu =
        match TablePage.rosterOf model with
        | Some roster -> Table.paifu roster (tableOf model)
        | None -> failwith "Live 那一桌必然有配桌"

    /// 走一段：一局打完就接着开下一局，走到终局或走满预算为止。
    /// **四家都是均匀随机 bot**，因此每一手当场落子，没有在飞的请求。
    let rec private drive (budget: int) (model: TableModel) : TableModel =
        let table = tableOf model

        match Table.result table with
        | Some _ -> model
        | None when budget <= 0 -> failwith "这一场在预算内没打完"
        | None when Table.isKyokuEnded table -> drive (budget - 1) (model |> step KyokuAdvanced)
        | None -> drive (budget - 1) (model |> step Advanced)

    /// 一段文本里出现了几次 `needle`。
    let private occurrences (needle: string) (text: string) : int =
        text.Split([| needle |], System.StringSplitOptions.None).Length - 1

    /// **事件流里**出现了几张赤牌。扫的是事件流而不是整份牌谱：规则集那一段本来就列着
    /// 赤牌种（`ruleset.akadora`），连它一起扫的话「一张都没有」那条断言永远数得出东西来。
    let private akadoraSeen (paifu: Paifu) : int =
        let text =
            paifu.Events
            |> List.map (Event.encoder >> Encode.toString 0)
            |> String.concat ""

        Tile.akadoraKinds
        |> List.sumBy (fun tile -> occurrences $"\"{Tile.toMjai tile}\"" text)

    // ---- 那三项与规则集的关系 ----

    [<Fact>]
    let ``默认那一份就是 Ruleset.yonma：页面不悄悄换一套规则`` () =
        Assert.Equal(Ruleset.yonma, RulesetDraft.ruleset RulesetDraft.initial)

    [<Fact>]
    let ``八种拨法：三项之外一个字段都不动（底子恒是天凤那份预设）`` () =
        for length in GameLength.all do
            for akadora in [ true; false ] do
                for kuitan in [ true; false ] do
                    let draft =
                        {
                            Length = length
                            Akadora = akadora
                            Kuitan = kuitan
                        }

                    let built = RulesetDraft.ruleset draft

                    // 拨到的那三项确实变了……
                    Assert.Equal(draft, RulesetDraft.ofRuleset built)

                    // ……而把那三项按回预设之后，剩下的每一个字段都与 `Ruleset.yonma` 逐字相同。
                    Assert.Equal(
                        Ruleset.yonma,
                        { built with
                            Length = Ruleset.yonma.Length
                            Akadora = Ruleset.yonma.Akadora
                            Kuitan = Ruleset.yonma.Kuitan
                        }
                    )

    [<Fact>]
    let ``长度是规则集的一根轴：局数序列四麻东风战 4 局、半庄 8 局`` () =
        let kyokus (length: GameLength) =
            { RulesetDraft.initial with
                Length = length
            }
            |> RulesetDraft.ruleset
            |> Ruleset.kyokus
            |> List.length

        Assert.Equal(4, kyokus Tonpuusen)
        Assert.Equal(8, kyokus Hanchan)

    // ---- 拨完要按「重开」才生效 ----

    [<Fact>]
    let ``拨开关不动正在打的那一桌：半场不换规则`` () =
        let played =
            table RulesetDraft.initial
            |> step Advanced
            |> step Advanced
            |> step (RulePicked(RuleChoice.Length Hanchan))
            |> step (RulePicked(RuleChoice.Akadora false))
            |> step (RulePicked(RuleChoice.Kuitan false))

        // 页面上那三枚按钮已经拨过去了……
        Assert.Equal(
            {
                Length = Hanchan
                Akadora = false
                Kuitan = false
            },
            (liveOf played).Rules
        )

        // ……而这一桌仍旧按开局那一份在打：牌桌的规则集、牌谱里的规则集都没动。
        Assert.Equal(Ruleset.yonma, played.Ruleset)
        Assert.Equal(Ruleset.yonma, Game.ruleset (tableOf played).Game)
        Assert.Equal(Ruleset.yonma, (paifuOf played).Ruleset)

        // 页面上那句「按重开才生效」的判据。
        Assert.True(TablePage.rulesPending played)

    [<Fact>]
    let ``按「重开」那一刻才换规则，换完就不再等着生效`` () =
        let restarted =
            table RulesetDraft.initial
            |> step Advanced
            |> step (RulePicked(RuleChoice.Length Hanchan))
            |> step (RulePicked(RuleChoice.Akadora false))
            |> step (RulePicked(RuleChoice.Kuitan false))
            |> step Restarted

        let expected =
            Ruleset.yonma
            |> Ruleset.withLength Hanchan
            |> Ruleset.withoutAkadora
            |> Ruleset.withoutKuitan

        Assert.Equal(expected, restarted.Ruleset)
        Assert.Equal(expected, Game.ruleset (tableOf restarted).Game)
        Assert.False(TablePage.rulesPending restarted)
        // 重开就是回到第一局：这一桌是新开的，不是接着打的。
        Assert.Equal(0, (tableOf restarted).Turns)

    [<Fact>]
    let ``回放那一侧没有配桌：三项拨不动，也永远不在等着生效`` () =
        let home = TablePage.home () |> fst

        Assert.False(TablePage.rulesPending home)
        Assert.Equal(home, home |> step (RulePicked(RuleChoice.Length Hanchan)))

    // ---- 牌谱里的 `ruleset` 跟着变（26-8） ----

    [<Fact>]
    let ``关掉赤宝牌：牌谱的 ruleset.akadora 为空，事件流里一张赤牌都没有`` () =
        let rules =
            { RulesetDraft.initial with
                Akadora = false
            }

        let paifu = table rules |> drive 6000 |> paifuOf

        Assert.Equal<Tile list>([], paifu.Ruleset.Akadora)
        Assert.Equal(0, akadoraSeen paifu)

    [<Fact>]
    let ``阳性对照：赤宝牌开着时，同一场里那三张赤牌真的出现在事件流里`` () =
        // 判据 3：一条永远执行不到的断言与一条从不失败的断言危害相同。
        // 上一条数的是「零张」，这一条证明那个数**数得出东西来**——同一颗默认种子、同一场对局。
        let paifu = table RulesetDraft.initial |> drive 6000 |> paifuOf

        Assert.Equal<Tile list>(Tile.akadoraKinds, paifu.Ruleset.Akadora)
        Assert.True(akadoraSeen paifu > 0, $"赤宝牌开着的一整场里只数出 {akadoraSeen paifu} 张赤牌")

    [<Fact>]
    let ``关掉食断：牌谱的 ruleset.kuitan 跟着变`` () =
        let off =
            { RulesetDraft.initial with
                Kuitan = false
            }
            |> table
            |> step Advanced
            |> paifuOf

        Assert.False(off.Ruleset.Kuitan)
        Assert.True((table RulesetDraft.initial |> step Advanced |> paifuOf).Ruleset.Kuitan)

    [<Fact>]
    let ``东风战打不到南场，半庄打得到：局数序列真的按长度走`` () =
        let bakazes (length: GameLength) =
            let played =
                { RulesetDraft.initial with
                    Length = length
                }
                |> table
                |> drive 6000
                |> tableOf

            Game.played played.Game
            |> List.map (fun state -> (GameState.context state).Bakaze)

        let tonpuu = bakazes Tonpuusen
        let hanchan = bakazes Hanchan

        Assert.Equal<Kaze list>(List.replicate (List.length tonpuu) Ton, tonpuu)
        Assert.True(hanchan |> List.contains Nan, "半庄打完了却一局南场都没有")
        // 连庄会把同一项再打一遍，因此局数只有下界：东风战至少 4 局、半庄至少 8 局。
        Assert.True(List.length tonpuu >= 4, $"东风战只打了 {List.length tonpuu} 局")
        Assert.True(List.length hanchan >= 8, $"半庄只打了 {List.length hanchan} 局")

    // ---- 超时默认值（票 72 顺手重定） ----

    [<Fact>]
    let ``超时默认值够开着思考预算的模型用：实测单手上界 180 秒`` () =
        // 30 秒是票 23 在没有思考预算的年代定的；DeepSeek medium 思考实测单手 17–180 秒
        // （DECISIONS 2026-08-16）。默认值低于那个上界，M2 的思考气泡必然大面积兜底。
        Assert.True(
            ModelProfile.initial.TimeoutMs >= 180_000,
            $"超时默认 {ModelProfile.initial.TimeoutMs} ms，接不住实测单手 180 秒的思考"
        )
