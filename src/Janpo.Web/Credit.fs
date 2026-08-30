namespace Janpo.Web

open Fable.Core

/// **强 AI 基线「是什么、来自哪里」在页面侧只写在这一处**（票 102；主人的要求原话：
/// 「在网页和 README 都说明这个强 AI 基线是什么、来自哪里」）。
///
/// 事实的唯一真源是**随站点一起分发的那份声明** `web/public/third-party/README.md`
/// （Apache-2.0 §4 的逐条义务与查证出处在 `probe/akagi-wasm/NOTICE-upstream.md`）。
/// 这里只摘它的四样——上游是谁、谁做的、什么许可、来源哪一版，外加**那条没消的风险**。
/// **不许在这里另编一份来源描述**：两处措辞不一致就是下一个 bug（票面原话）。
///
/// **通名与具名的分工**（ADR-0006 边界 5，主人第九次术语授权）：概念一律叫「强 AI 基线」
/// （`SeatingPlan.baselineToDisplay` 那一份，面板上那枚按钮、名牌、牌谱里那一列读的都是它），
/// **具名只出现在署名 / 说明 / 报告里**——这个模块正是「署名与说明」那一处，
/// 所以这里写具名是对的。**边界 5 里那个「页脚」在这一票扩到了配桌页那一句与牌桌上那三句**
/// （主人的直接要求；措辞提案记在 `DECISIONS.md` 的 102-1，ADR 不许这一票改）。
///
/// **为什么那几句话在这里而不在视图里**：它们是同一份具名的三个落点
/// （配桌页拨到那一席、牌桌上等它加载、它就位），分散写就会漂成三种说法；
/// 而摆在一个不 open Feliz 的模块里，dotnet 侧的用例才够得着它们
/// （`tests/Janpo.Web.Tests/BaselineCreditTests.fs`——CI 里的浏览器摸不到 `Ready` 那一态，
/// 因为那 6 MB 不入版本控制）。**画在哪儿仍旧是视图的事**：
/// 配桌页那一句在 `TablePanel`，牌桌那一行在 `BaselineLine`，页脚那条链接在 `Footer`。
[<RequireQualifiedAccess>]
module Credit =

    /// 上游项目与我们真正取的那个子目录。**只取 `native_bot/`**（`fetch-upstream.sh` 的
    /// sparse-checkout 钉死了这一点），因此署名写到子目录这一层才是实话。
    let private project = "Akagi"

    let private crate = "native_bot"

    /// 上游的版权人（上游 `NOTICE` 首行：`Akagi v3 / Copyright 2026 Shinkuan`）。
    let author: string = "Shinkuan"

    /// 上游的许可（`native_bot/Cargo.toml` 的 `license = "Apache-2.0"`；许可正文与上游 `NOTICE`
    /// 随站点一起发，落点是 `web/public/third-party/`）。
    let licence: string = "Apache-2.0"

    /// 我们取的那一版（`394b329058e1b4d721dc40149658f9f9cfdd77ae` 的短号）。
    /// **来源 commit 是分发件的一部分**：不写它，「照原样分发」这句话就无从复核。
    let sourceCommit: string = "394b3290"

    /// 具名那一段。**页面上出现它的每一处都读这一份**。
    let baselineNamed: string = $"{project} 的 {crate}"

    /// 具名 + 许可：牌桌上那几句话里插的就是这一小段（一句话量级，只够点个名）。
    let baselineBadge: string = $"{baselineNamed}，{licence}"

    /// 那份声明在站点里的相对路径。**按 `document.baseURI` 解析**（同 `web/src/demo/paifu.ts`）：
    /// 站点部署在子路径下（GitHub Pages 是 `/janpo/`），写死斜杠开头在那里会 404。
    ///
    /// `web/public/` 下的东西 Vite 原样拷进 `dist/`，`scripts/check-pages-dist.sh` 核的就是它。
    let thirdPartyFile: string = "third-party/README.md"

    /// 那条链接上的字（页脚与配桌页那一句共用一个说法）。
    let thirdPartyText: string = "第三方组件声明"

    /// **是一个函数而不是一个值**（同 `Footer.fs` 从前那一份）：模块初始化时算的话，
    /// 这一行在 dotnet 那侧（只当类型检查用）会在加载模块那一刻就抛。
    [<Emit("new URL($0, document.baseURI).toString()")>]
    let private resolveFromBase (_file: string) : string = jsNative

    /// 那份声明在站点里的完整地址。
    let thirdPartyUrl () : string = resolveFromBase thirdPartyFile

    /// **配桌页拨到那一席那一刻说的那句话**（票 102 的要害之一）：
    /// 署名要落在人**遇到它**的那一刻，而不是只落在一个要点开才看得见的页面里。
    ///
    /// 四样齐：**它是什么**（纯 Rust 的麻将 CNN，行为克隆自人类天凤牌谱）、**谁做的**
    /// （上游 + 作者 + 来源 commit）、**什么许可**（Apache-2.0）、**一条通往那份声明的路**。
    /// 外加那条**没消的风险**——权重的语料出处上游未公开，我们无从核（ADR-0006 后果 1；
    /// 主人的裁定是「照原样分发，风险记在案」，**不许在页面上美化成「许可齐备」**）。
    ///
    /// **一句话量级**：页面其余部分的语感是「只说访客关心的那几件事」（`Footer.fs` 的判断 2），
    /// 细节交给那份声明。中间断开是因为那几个字是一条链接（`TablePanel` 把三段接起来）。
    let baselineIntroHead: string =
        $"「{SeatingPlan.baselineToDisplay}」是 {baselineNamed}（{author} 作，{licence}，来源 commit {sourceCommit}）："
        + "一个纯 Rust 的麻将 CNN，行为克隆自人类天凤牌谱，编成 WebAssembly 在你的浏览器里推理，"
        + "拨上它的这一席才会去下那几 MB。它的权重训练用了哪一份牌谱，上游没有公开、我们无从核，"
        + "因此照原样分发——归属、许可与这条风险逐条写在"

    let baselineIntroTail: string = "里。"

    /// 那一句的整句（用例与报告读它；页面上画的是断开的三段）。
    let baselineIntro: string = baselineIntroHead + thirdPartyText + baselineIntroTail

    /// **牌桌上那四态各一句话**（票 92 立的那四句，票 102 往里加了具名）。
    ///
    /// 加名字的两处理由各不相同：
    ///
    /// - `Loading`：人正在等那几 MB，**这一刻他最想知道等的是什么**；
    /// - `Ready`：它已经在替某一席打牌了，署名该跟着它出场。
    ///
    /// **`Unavailable` 那一句语义不许退化**（票面原话）：那句话已经在生产环境里被主人读到过一次
    /// （票 101 的由来），它把两件事都说清——为什么拉不动、那一席现在是谁在打。
    /// 只说前半，人会以为这一桌停了；只说后半，人会以为自己拨错了按钮。
    /// 这一票只在句末补一句「本来要取的是谁」，**头上那一句一个字没动**
    /// （`verify-baseline.mjs` 那三条断言读的就是它）。
    ///
    /// **`Absent` 一个字都不说**：一句「这一桌没有强 AI 基线」对四家模型那一桌是噪声，
    /// 而那正是「一个字节都没拉」那一态（ADR-0006 边界 1）。
    let baselineSaid (status: BaselineStatus) (seats: string) : string =
        match status with
        | BaselineStatus.Absent -> ""
        | BaselineStatus.Loading ->
            $"正在取{SeatingPlan.baselineToDisplay}那份资产（{baselineBadge}；座位 {seats}）："
            + "第一次要下几 MB，之后走浏览器缓存。"
        | BaselineStatus.Ready _ ->
            // 就位之后**只说「谁在哪几席」**（票 121，主人裁的）。
            // 上游是谁、什么许可、多大——都在页脚那条「第三方组件声明」里，
            // 不必每一局都在牌桌边复述一遍。「它不会说话」也删了：
            // 那三席不冒气泡、不记 token，**看得见就不用说**。
            $"{SeatingPlan.baselineToDisplay}：座位 {seats}（许可与出处见页脚）"
        | BaselineStatus.Unavailable reason ->
            $"{SeatingPlan.baselineToDisplay}用不了：{reason}　座位 {seats} 已退回「{Bot.toDisplay Bot.Opinionated}」的自带 bot，"
            + $"其余席照常打完这一局——本来要取的是 {baselineNamed}（{licence}）。"
