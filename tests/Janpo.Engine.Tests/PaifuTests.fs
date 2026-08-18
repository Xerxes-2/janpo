namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo

/// 牌谱的 wire 形态（票 26）：**编解码往返逐字段相同**，且 thinking 是可省略的那一段
/// ——JSON 导出全量带着，URL 分享（M2）抹掉它，**两条路径共用同一个解码器**（ADR-0002）。
///
/// 票 31 又多一条：版本 1 的牌谱（每手存一整份 prompt）**照样读得动**，而且写回去仍是版本 1。
module PaifuTests =

    let private json (paifu: Paifu) : string =
        Paifu.encoder paifu |> Encode.toString 0

    let private decode (text: string) : Paifu =
        match Decode.fromString Paifu.decoder text with
        | Ok paifu -> paifu
        | Error message -> failwith $"这份牌谱应当读得动，却得到「{message}」"

    /// 一份手写的记录：兜底与非兜底、有 thinking 与没 thinking 各占一条。
    let private records: DecisionRecord list =
        [
            {
                Turn = 0
                Seat = seat 1
                PromptTail = "【现在】东1局 0 本场，你是座位 1……\n【可选动作】\n- id=0：摸切1索\n- id=1：手切9万"
                RenderVersion = "janpo-default@08fcaec3.4b9e57c0"
                ActionIds = [ 0; 1 ]
                Output = """{"stop_reason":"toolUse","content":[{"type":"toolCall","name":"choose_action"}]}"""
                Reason = Some "9万是孤张，先切它"
                Thinking = Some "先数向听：现在是 2 向听……"
                Attempts = 1
                LatencyMs = 2131
                Applied = Some 1
                Fallback = None
                Usage =
                    Some
                        {
                            Input = 812
                            Output = 96
                            CacheRead = 1344
                            CacheWrite = 0
                        }
            }
            {
                Turn = 7
                Seat = seat 1
                PromptTail = "【现在】东1局 0 本场，你是座位 1……"
                RenderVersion = "janpo-default@08fcaec3.4b9e57c0"
                ActionIds = [ 0 ]
                Output = ""
                Reason = None
                Thinking = None
                Attempts = 3
                LatencyMs = 812
                Applied = Some 0
                Fallback = Some "provider 报错：401（重试 2 次仍无结果）"
                // 一次都没问成的那一手没有账单：`usage` 整个不写。
                Usage = None
            }
        ]

    /// prompt 的前置（票 31）：工具定义形状整场一份，座位 1 的固定 preamble 一份。
    let private prompting: Prompting =
        {
            Tools = """[{"name":"choose_action","parameters":{"properties":{"action_id":{"enum":[]}}}}]"""
            Preambles =
                [
                    {
                        Seat = seat 1
                        RenderVersion = "janpo-default@08fcaec3.4b9e57c0"
                        Text = "你在打日本立直麻将（天凤规则，四人东）……"
                    }
                ]
        }

    let private paifu () : Paifu =
        match Game.runRandom Ruleset.yonma (Rng.ofSeed 2088) with
        | Ok(game, _) ->
            Paifu.create Ruleset.yonma (StartGame [ "p0"; "p1"; "p2"; "p3" ] :: Game.events game) records prompting
        | Error error -> failwith $"这一场应当打得完，却得到「{KyokuError.toDisplay error}」"

    // ---- 往返 ----

    [<Fact>]
    let ``编码再解码，逐字段与原来相同`` () =
        let original = paifu ()
        let round = original |> json |> decode

        Assert.Equal(original.Version, round.Version)
        Assert.Equal(original.Ruleset, round.Ruleset)
        Assert.Equal<Event list>(original.Events, round.Events)
        Assert.Equal<DecisionRecord list>(original.Decisions, round.Decisions)
        // 整条记录也相等（上面四条是差异出现时看得懂的诊断，这一条是真正的验收）。
        Assert.Equal(original, round)

    [<Fact>]
    let ``规则集逐字段往返，牌种集合与红宝牌也不丢`` () =
        let ruleset =
            Ruleset.yonma
            |> Ruleset.withLength Hanchan
            |> Ruleset.withoutKuitan
            |> Ruleset.withAtamahane
            |> Ruleset.withKiriageMangan
            |> Ruleset.withKokushiAnkanChankan
            |> Ruleset.withoutSanchaHoraRyuukyoku
            |> Ruleset.withDoubleYakuman

        let sanma =
            { ruleset with
                SeatCount = 3
                TileKinds =
                    Tile.kinds
                    |> List.filter (fun tile -> Tile.deaka tile |> Tile.toMjai <> "2m")
                    |> TileKindSet.ofKinds
            }

        for original in [ Ruleset.yonma; ruleset; sanma ] do
            let round =
                match
                    Ruleset.encoder original
                    |> Encode.toString 0
                    |> Decode.fromString Ruleset.decoder
                with
                | Ok decoded -> decoded
                | Error message -> failwith $"这份规则集应当读得动，却得到「{message}」"

            Assert.Equal(original, round)

    [<Fact>]
    let ``三个规则开关字段可缺省，缺省都是天凤侧，其余字段仍是必需`` () =
        // 票 65（调度器裁 63 的待裁三之三）：`minkan_rinshan_sekinin`（票 59）与
        // `riichi_ankan_mentsu_unchanged`（票 63）都是后加的规则开关，旧导出件里没有它们；
        // 而那些对局就是按天凤口径（false）打的，缺省补 false 回放逐字相同——
        // 按 26 号的版本策略「加可缺省字段不涨版本」，两个开关同一种处理。
        // `double_yakuman`（雀魂的双倍役满）同理：旧导出件里没有它，那些对局也是天凤口径。
        let full = Ruleset.encoder Ruleset.yonma |> Encode.toString 0

        let missing =
            full
                .Replace("\"minkan_rinshan_sekinin\":false,", "")
                .Replace("\"riichi_ankan_mentsu_unchanged\":false,", "")
                .Replace("\"double_yakuman\":false,", "")

        Assert.DoesNotContain("minkan_rinshan_sekinin", missing)
        Assert.DoesNotContain("riichi_ankan_mentsu_unchanged", missing)
        Assert.DoesNotContain("double_yakuman", missing)

        match Decode.fromString Ruleset.decoder missing with
        | Ok decoded ->
            Assert.False(decoded.MinkanRinshanSekinin)
            Assert.False(decoded.RiichiAnkanMentsuUnchanged)
            Assert.False(decoded.DoubleYakuman)
            Assert.Equal(Ruleset.yonma, decoded)
        | Error message -> failwith $"缺了三个可缺省开关的规则集应当读得动，却得到「{message}」"

        // 写过 true 的导出件恒带字段（编码器逐字段写），读回来也是 true：往返那条已盖，
        // 这里只钉「可缺省 ≠ 被忽略」。
        let switched =
            Ruleset.yonma
            |> Ruleset.withMinkanRinshanSekinin
            |> Ruleset.withRiichiAnkanMentsuUnchanged
            |> Ruleset.withDoubleYakuman

        match
            Ruleset.encoder switched
            |> Encode.toString 0
            |> Decode.fromString Ruleset.decoder
        with
        | Ok decoded ->
            Assert.True(decoded.MinkanRinshanSekinin)
            Assert.True(decoded.RiichiAnkanMentsuUnchanged)
            Assert.True(decoded.DoubleYakuman)
        | Error message -> failwith $"开着开关的规则集应当读得动，却得到「{message}」"

        // 其余字段仍是必需的：可缺省只是这几个后加开关的例外，不是解码器的新设计。
        let noKuitan = full.Replace("\"kuitan\":true,", "")

        match Decode.fromString Ruleset.decoder noKuitan with
        | Ok _ -> failwith "缺了必需字段的规则集不该读得动"
        | Error _ -> ()

    // ---- thinking 是可省略的那一段 ----

    [<Fact>]
    let ``抹掉 thinking 之后，其余字段一个不少`` () =
        let original = paifu ()
        let shared = Paifu.stripThinking original

        Assert.Equal<Event list>(original.Events, shared.Events)
        Assert.Equal(List.length original.Decisions, List.length shared.Decisions)
        Assert.All(shared.Decisions, fun record -> Assert.True(Option.isNone record.Thinking))

        // 省掉的只有 thinking：把原来那份也抹掉之后两者逐字段相同。
        Assert.Equal(Paifu.stripThinking original, shared)

    [<Fact>]
    let ``省掉 thinking 的牌谱由同一个解码器读回来`` () =
        let shared = paifu () |> Paifu.stripThinking
        let text = json shared

        // wire 上根本没有这个字段（不是写成 null）：分享路径要的就是短。
        Assert.DoesNotContain("thinking", text)
        Assert.Equal(shared, decode text)

    [<Fact>]
    let ``thinking 写成 null 与整个缺省读出来是同一件事`` () =
        let withNull =
            """{"version":1,"ruleset":RULESET,"events":[],"decisions":[
                 {"turn":0,"seat":0,"prompt":"p","tools":"t","output":"o","thinking":null,
                  "attempts":1,"latency_ms":5,"applied":0}]}"""
                .Replace("RULESET", Ruleset.encoder Ruleset.yonma |> Encode.toString 0)

        let record = decode withNull |> Paifu.decisionAt 0

        Assert.Equal(Some None, record |> Option.map (fun record -> record.Thinking))
        Assert.Equal(Some None, record |> Option.map (fun record -> record.Fallback))
        Assert.Equal(Some None, record |> Option.map (fun record -> record.Reason))

    // ---- 版本号 ----

    [<Fact>]
    let ``牌谱带着格式版本号`` () =
        Assert.Contains($"\"version\":{Paifu.version}", json (paifu ()))
        Assert.Contains(Paifu.version, Paifu.supported)

    // ---- prompt 的前置（票 31） ----

    [<Fact>]
    let ``牌谱只存 prompt 的尾部，前置整场各存一份`` () =
        let text = json (paifu ())

        Assert.Contains("\"prompt_tail\"", text)
        Assert.DoesNotContain("\"prompt\":", text)
        // 工具定义只在 `prompting` 里出现一次，不在每条记录里（两条记录共享同一份形状）。
        Assert.Equal(1, text.Split("\"tools\":").Length - 1)
        Assert.Contains("\"action_ids\":[0,1]", text)

    [<Fact>]
    let ``前置往返：preamble 按座位与渲染版本取得回来`` () =
        let round = paifu () |> json |> decode

        Assert.Equal(prompting, round.Prompting)

        Assert.Equal(
            Some "你在打日本立直麻将（天凤规则，四人东）……",
            Prompting.preambleFor (seat 1) "janpo-default@08fcaec3.4b9e57c0" round.Prompting
        )

        // 换了人格（渲染版本跟着变）就取不到那一份：**版本号就是那把键**。
        Assert.Equal(None, Prompting.preambleFor (seat 1) "janpo-default@ffffffff" round.Prompting)
        Assert.Equal(None, Prompting.preambleFor (seat 0) "janpo-default@08fcaec3.4b9e57c0" round.Prompting)

    [<Fact>]
    let ``同一座位同一版本只存一份 preamble，换了人格才多一份`` () =
        let one (version: string) (text: string) : Prompting =
            {
                Tools = "[shape]"
                Preambles =
                    [
                        {
                            Seat = seat 1
                            RenderVersion = version
                            Text = text
                        }
                    ]
            }

        let after =
            Prompting.empty
            |> Prompting.add (one "v1" "甲")
            |> Prompting.add (one "v1" "甲")
            |> Prompting.add (one "v2" "乙")
            // 一次都没问成的那几手没有 prompt 可言，不该撑出一条空记录。
            |> Prompting.add (one "v3" "")

        Assert.Equal(2, List.length after.Preambles)
        Assert.Equal("[shape]", after.Tools)
        Assert.Equal(Some "甲", Prompting.preambleFor (seat 1) "v1" after)
        Assert.Equal(Some "乙", Prompting.preambleFor (seat 1) "v2" after)

    // ---- 旧版牌谱 ----

    /// 票 31 之前的那一版：每手一整份 prompt、每手一份工具定义，没有 `prompting`。
    let private v1 =
        """{"version":1,"ruleset":RULESET,"events":[],"decisions":[
             {"turn":0,"seat":1,"prompt":"你在打日本立直麻将……\n【现在】东1局",
              "tools":"{\"name\":\"choose_action\"}","output":"o","attempts":1,"latency_ms":5,"applied":0}]}"""
            .Replace("RULESET", Ruleset.encoder Ruleset.yonma |> Encode.toString 0)

    [<Fact>]
    let ``版本 1 的牌谱照样读得动：那一版存的整文就在 PromptTail 里`` () =
        let old = decode v1
        let record = Paifu.decisionAt 0 old |> Option.get

        Assert.Equal(1, old.Version)
        Assert.Contains("【现在】", record.PromptTail)
        // 那一版没有的三项：读出来是空的，不是编一个出来。
        Assert.Equal("", record.RenderVersion)
        Assert.Equal<int list>([], record.ActionIds)
        Assert.Equal(Prompting.empty, old.Prompting)

    [<Fact>]
    let ``版本 1 读进来再写出去仍是版本 1：不把当年的整文重标成尾部`` () =
        let old = decode v1
        let text = json old

        Assert.Contains("\"version\":1", text)
        Assert.Contains("\"prompt\":", text)
        Assert.DoesNotContain("prompt_tail", text)
        Assert.DoesNotContain("prompting", text)
        Assert.Equal(old, decode text)

    /// 票 43 之前的那一版：形状与 v3 逐字相同，只有 `render_version` 那一串只哈希了模板内容
    /// （`模板 id@内容哈希`，后面没有渲染器那一截）。
    let private v2 =
        """{"version":2,"ruleset":RULESET,"events":[],"decisions":[
             {"turn":0,"seat":1,"prompt_tail":"【现在】东1局","render_version":"janpo-default@08fcaec3",
              "action_ids":[0],"output":"o","attempts":1,"latency_ms":5,"applied":0}],
           "prompting":{"tools":"[shape]","preambles":[
             {"seat":1,"render_version":"janpo-default@08fcaec3","text":"你在打日本立直麻将……"}]}}"""
            .Replace("RULESET", Ruleset.encoder Ruleset.yonma |> Encode.toString 0)

    [<Fact>]
    let ``版本 2 的牌谱照样读得动，那一版的渲染版本号仍然当键用`` () =
        let old = decode v2
        let record = Paifu.decisionAt 0 old |> Option.get

        Assert.Equal(2, old.Version)
        // 那一串原样读进来，**不补一截假的渲染器摘要**：当年没有记下来就是没有。
        Assert.Equal("janpo-default@08fcaec3", record.RenderVersion)
        // 键仍然对得上：同一份牌谱里两处写的是同一串，重建 prompt 那条链没断。
        Assert.Equal(Some "你在打日本立直麻将……", Prompting.preambleFor (seat 1) record.RenderVersion old.Prompting)

    [<Fact>]
    let ``版本 2 读进来再写出去仍是版本 2：不把当年那个版本号说成它懂渲染器`` () =
        let old = decode v2
        let text = json old

        // 形状与 v3 逐字相同，因此只有牌谱头那个数字能告诉读的人「这个版本号说得了什么」。
        Assert.Contains("\"version\":2", text)
        Assert.Contains("\"prompt_tail\"", text)
        Assert.Equal(old, decode text)

    [<Fact>]
    let ``认不出的版本号是读不动，而不是当成当前版本读`` () =
        let future =
            json (paifu ())
            |> fun text -> text.Replace($"\"version\":{Paifu.version}", "\"version\":99")

        match Decode.fromString Paifu.decoder future with
        | Ok _ -> failwith "未来版本的牌谱不该读得动"
        | Error message -> Assert.Contains("unsupported paifu version", message)

    // ---- 手序编号 ----

    [<Fact>]
    let ``随机选手的手不产生记录，但手序编号不因此断裂`` () =
        let paifu = paifu ()

        // 手写的这两条记录之间隔着若干随机座位的手：编号跳号，但每条都指得回某一手。
        Assert.Equal<int list>([ 0; 7 ], paifu.Decisions |> List.map (fun record -> record.Turn))
        Assert.True(Paifu.decisionAt 0 paifu |> Option.isSome)
        Assert.True(Paifu.decisionAt 3 paifu |> Option.isNone, "第 3 手是随机选手，没有可审计的推理")
