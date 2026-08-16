namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo

/// 牌谱的 wire 形态（票 26）：**编解码往返逐字段相同**，且 thinking 是可省略的那一段
/// ——JSON 导出全量带着，URL 分享（M2）抹掉它，**两条路径共用同一个解码器**（ADR-0002）。
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
                Prompt = "东1局 0 本场，你是座位 1……\n可选动作：\n0. 摸切1索\n1. 手切9万"
                Tools = """{"name":"choose_action","parameters":{"properties":{"action_id":{"enum":["0","1"]}}}}"""
                Output = """{"stop_reason":"toolUse","content":[{"type":"toolCall","name":"choose_action"}]}"""
                Reason = Some "9万是孤张，先切它"
                Thinking = Some "先数向听：现在是 2 向听……"
                Attempts = 1
                LatencyMs = 2131
                Applied = Some 1
                Fallback = None
            }
            {
                Turn = 7
                Seat = seat 1
                Prompt = "东1局 0 本场，你是座位 1……"
                Tools = """{"name":"choose_action"}"""
                Output = ""
                Reason = None
                Thinking = None
                Attempts = 3
                LatencyMs = 812
                Applied = Some 0
                Fallback = Some "provider 报错：401（重试 2 次仍无结果）"
            }
        ]

    let private paifu () : Paifu =
        match Game.runRandom Ruleset.yonma (Rng.ofSeed 2088) with
        | Ok(game, _) -> Paifu.create Ruleset.yonma (StartGame [ "p0"; "p1"; "p2"; "p3" ] :: Game.events game) records
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
