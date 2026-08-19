namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// **强 AI 基线的来历摆在人看得见的地方**（票 102；主人的要求：「在网页和 README 都说明
/// 这个强 AI 基线是什么、来自哪里」）。
///
/// 这一层钉的是**那几句话本身**（页面上的字就是这一票的交付物）：
///
/// 1. **署名落在人遇到它的那一刻**：配桌页拨到那一席那一句、牌桌上等它加载那一句、
///    它就位那一句——三处都点得出具名、作者与许可；
/// 2. **降级那一句语义不许退化**（票面原话）：`Unavailable` 那一句仍旧把
///    「为什么拉不动」与「那一席现在是谁在打」两件事都说完，只是多带了一个名字；
/// 3. **没选它的那一桌一个字都不说**（`Absent`）：首页与普通对局不长出这一行；
/// 4. **通名与具名的分工**（ADR-0006 边界 5，主人第九次术语授权）：概念用通名，
///    具名只在署名 / 说明里——因此 `SeatingPlan.baselineToDisplay` 里不许出现具名。
///
/// **DOM 上那几处真的看得见**由 `web/scripts/verify-baseline.mjs` 的第 ⑤ 趟守着
/// （判据 20：停在「那一席被拨上 / 正在加载」那一刻上量，不抓终局页面）。
/// 这一层守的是**话说得对不对**，那一层守的是**话到没到人眼前**。
module BaselineCreditTests =

    /// 那份声明里的事实（真源是 `web/public/third-party/README.md`，逐条出处在
    /// `probe/akagi-wasm/NOTICE-upstream.md`）。**测试里照抄一遍是故意的**：
    /// 页面上那几句话哪天被改成另一种来源描述，这里当场红。
    let private facts = [ "Akagi"; "native_bot"; "Apache-2.0" ]

    let private containsAll (words: string list) (said: string) (where: string) =
        for word in words do
            Assert.True(said.Contains word, $"{where}少了「{word}」：{said}")

    // ---- 配桌页：拨到那一席那一刻 ----

    [<Fact>]
    let ``配桌页那一句把「它是什么 + 谁做的 + 什么许可 + 一条通往声明的路」都说了`` () =
        let said = Credit.baselineIntro

        containsAll facts said "配桌页那一句"
        // 谁做的、哪一版：来源 commit 与作者都在（Apache-2.0 §4 的归属落在这几个字上）。
        containsAll [ "Shinkuan"; "394b3290" ] said "配桌页那一句"
        // 它到底是个什么东西（不是「一个强 AI」这种空话）。
        containsAll [ "Rust"; "天凤"; "浏览器" ] said "配桌页那一句"
        // **那条没消的风险不许被美化成「许可齐备」**（ADR-0006 后果 1，主人裁定照原样分发）。
        containsAll [ "无从核" ] said "配桌页那一句"
        // 通往那份声明的路：链接的文字与它指的那份文件都在这一句里。
        Assert.Contains(Credit.thirdPartyText, said)
        Assert.Equal("third-party/README.md", Credit.thirdPartyFile)

    [<Fact>]
    let ``配桌页那一句是一句话量级：不写成论文，也不复述那份声明`` () =
        // 面板底下那一大段说明（`TablePanel.panelNote`）有一千多字，它是查阅用的；
        // 这一句是**人拨到那一席时顺眼读到的**，长了就没人读。
        Assert.True(Credit.baselineIntro.Length < 220, $"配桌页那一句 {Credit.baselineIntro.Length} 字，太长了")

    // ---- 牌桌上：等它加载与它就位那一刻 ----

    [<Fact>]
    let ``正在加载那一句里有它的名字：人等那几 MB 的时候看得出等的是什么`` () =
        let said = Credit.baselineSaid BaselineStatus.Loading "1"

        containsAll facts said "加载那一句"
        Assert.Contains(SeatingPlan.baselineToDisplay, said)
        // 它仍旧说清「在下几 MB」这件事（票 92 那一句的本意）。
        Assert.Contains("MB", said)
        Assert.Contains("座位 1", said)

    [<Fact>]
    let ``它就位那一句里有它的名字，而「它不会说话」那半句一个字没少`` () =
        let said = Credit.baselineSaid (BaselineStatus.Ready 6039832) "0、2"

        containsAll facts said "就位那一句"
        Assert.Contains(Baseline.bytesToDisplay 6039832, said)
        // 票 92 的要害：这一席不会说话。加了署名不许把它挤掉。
        Assert.Contains("没有思考气泡", said)
        Assert.Contains("token 账单", said)

    [<Fact>]
    let ``降级那一句语义不许退化：原因、退回了谁、其余席照常打完，三件事都还在`` () =
        let reason = "强 AI 基线拉不动：取 baseline/janpo-baseline.wasm 时回了 HTTP 404"
        let said = Credit.baselineSaid (BaselineStatus.Unavailable reason) "0"

        // **主人在生产环境里读到过这一句**（票 101 的由来）：它的三件事一件都不许少。
        Assert.StartsWith($"{SeatingPlan.baselineToDisplay}用不了：", said)
        Assert.Contains(reason, said)
        Assert.Contains($"已退回「{Bot.toDisplay Bot.Opinionated}」的自带 bot", said)
        Assert.Contains("其余席照常打完这一局", said)
        // 可以加名字（票面原话），不许变成一句机器话。
        containsAll facts said "降级那一句"

    [<Fact>]
    let ``没选它的那一桌一个字都不说：首页与普通对局不长出这一行`` () =
        Assert.Equal("", Credit.baselineSaid BaselineStatus.Absent "")

    // ---- 通名与具名的分工 ----

    [<Fact>]
    let ``概念一律通名，具名只在署名与说明里`` () =
        // 面板上那枚按钮、牌桌上的名牌、牌谱里那一列：一个具名都不许有。
        for name in
            [
                SeatingPlan.baselineToDisplay
                Roster.baselineName
                SeatChoice.toWire SeatChoice.Baseline
            ] do
            for word in facts do
                Assert.False(name.Contains word, $"通名「{name}」里出现了具名「{word}」")

        // 而署名那一段必须点名（否则这一票等于没做）：具名一段 + 许可那一段。
        containsAll [ "Akagi"; "native_bot" ] Credit.baselineNamed "具名那一段"
        containsAll facts Credit.baselineBadge "署名那一段"
