namespace Janpo.Web.Tests

open Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Web

/// F#/TS 那道边界的 wire 形态（票 23）。**这一侧只测 JSON**：跨出去的一段与回来的一段。
///
/// 解码器与编码器写在 `Thoth.Json.Core` 上（后端无关），因此这里能用 dotnet 的 Newtonsoft
/// 后端跑它们——浏览器里跑的是同一份代码，只是换了个后端。
module AgentTests =

    let private config: LlmSeat =
        {
            Provider = "deepseek"
            Model = "deepseek-v4-flash"
            ApiKey = "sk-测试用的假 key"
            TimeoutMs = 12000
            Thinking = Thinking.Low
            Tier = ScaffoldTier.Bare
        }

    let private package () : DecisionPackage =
        let state =
            match GameState.start Ruleset.yonma (KyokuContext.initial Ruleset.yonma) (Rng.ofSeed 2088) with
            | Ok(state, _) -> state
            | Error error -> failwith $"应当开得出局，却得到「{KyokuStartError.toDisplay error}」"

        match GameState.legalActions state |> List.tryHead with
        | Some choice ->
            match DecisionPackage.forSeat choice.Seat state with
            | Some package -> package
            | None -> failwith "正在被问的座位应当有决策包"
        | None -> failwith "开局之后总有一家被问"

    let private answer (json: string) : AgentAnswer =
        match Decode.fromString Agent.answerDecoder json with
        | Ok answer -> answer
        | Error message -> failwith $"这段回执应当读得动，却得到「{message}」"

    // ---- 出去的那一段 ----

    [<Fact>]
    let ``请求里带着原样的决策包、座位配置与重试上限`` () =
        let package = package ()

        let json =
            Agent.requestEncoder
                {
                    Package = package
                    Seat = config
                    RetryLimit = Agent.retryLimit
                }
            |> Encode.toString 0

        // 决策包一个字段都不改写：Agent 层读的与 `janpo decide` 打印的是同一份东西。
        Assert.Contains(DecisionPackage.encoder package |> Encode.toString 0, json)
        Assert.Contains("\"retry_limit\":2", json)
        Assert.Contains("\"provider\":\"deepseek\"", json)
        Assert.Contains("\"timeout_ms\":12000", json)
        Assert.Contains("\"thinking\":\"low\"", json)
        // 档位过界：24 号票的 prompt 分档按它走。
        Assert.Contains("\"tier\":\"bare\"", json)

    [<Fact>]
    let ``key 随请求过界——它只从这台浏览器走到 provider`` () =
        let json =
            Agent.requestEncoder
                {
                    Package = package ()
                    Seat = config
                    RetryLimit = 2
                }
            |> Encode.toString 0

        Assert.Contains("sk-测试用的假 key", json)

    // ---- 回来的那一段 ----

    [<Fact>]
    let ``合法回执：一个动作 id 加一句理由`` () =
        let answer =
            answer """{"action_id":3,"reason":"这张最安全","failure":null,"attempts":1,"latency_ms":2446}"""

        Assert.Equal(Some 3, answer.ActionId)
        Assert.Equal(Some "这张最安全", answer.Reason)
        Assert.Equal(None, answer.Failure)
        Assert.Equal(1, answer.Attempts)
        Assert.Equal(2446, answer.LatencyMs)

    [<Fact>]
    let ``交不出来的回执：没有 id，只有一条原因`` () =
        let answer =
            answer """{"action_id":null,"failure":"模型超时（重试 2 次仍无结果）","attempts":3,"latency_ms":90123}"""

        Assert.Equal(None, answer.ActionId)
        Assert.Equal(Some "模型超时（重试 2 次仍无结果）", answer.Failure)
        Assert.Equal(3, answer.Attempts)

    [<Fact>]
    let ``缺字段按「没有」处理，不是整条读不动`` () =
        // Agent 层是另一个语言写的，字段对不齐时这一手仍要走得下去（兜底），
        // 而不是因为一个字段缺失把整条回执废掉。
        let answer = answer "{}"

        Assert.Equal(None, answer.ActionId)
        Assert.Equal(None, answer.Failure)
        Assert.Equal(0, answer.Attempts)
        Assert.Equal(0, answer.LatencyMs)

    [<Fact>]
    let ``读不动的回执是 Error，不是异常`` () =
        Assert.True(Decode.fromString Agent.answerDecoder "不是 JSON" |> Result.isError)
        // 类型不对也一样（action_id 给了个字符串）。
        Assert.True(Decode.fromString Agent.answerDecoder """{"action_id":"三"}""" |> Result.isError)

    [<Fact>]
    let ``自造的「交不出来」与 TS 侧那条走同一个形状`` () =
        let refused = Agent.refused "Agent 层抛了异常"

        Assert.Equal(None, refused.ActionId)
        Assert.Equal(Some "Agent 层抛了异常", refused.Failure)

    // ---- 配置的编辑 ----

    [<Fact>]
    let ``读不懂的值一律原样不改：一次误输入不该把设定清掉`` () =
        Assert.Equal(config, LlmSeat.edit LlmField.TimeoutMs "三万" config)
        Assert.Equal(config, LlmSeat.edit LlmField.TimeoutMs "-1" config)
        Assert.Equal(config, LlmSeat.edit LlmField.Thinking "疯狂" config)

        Assert.Equal({ config with TimeoutMs = 5000 }, LlmSeat.edit LlmField.TimeoutMs " 5000 " config)

        Assert.Equal({ config with Thinking = Thinking.High }, LlmSeat.edit LlmField.Thinking "high" config)

    [<Fact>]
    let ``每个字段读出来再写回去都是原样的`` () =
        // localStorage 那一层就是这么存的（`Store` 遍历 `LlmField.all`），
        // 因此这条同时钉住「存进去再读回来不会变形」。
        for field in LlmField.all do
            Assert.Equal(config, LlmSeat.edit field (LlmSeat.field field config) config)

    [<Fact>]
    let ``字段的键名两两不同：否则两个设定会写到同一格`` () =
        let keys = LlmField.all |> List.map LlmField.key

        Assert.Equal<string list>(List.distinct keys, keys)
        Assert.Equal(5, List.length keys)

    [<Fact>]
    let ``provider 列表里没有 Bedrock——它是 Node-only`` () =
        Assert.DoesNotContain("amazon-bedrock", LlmSeat.providers)
        Assert.Contains("deepseek", LlmSeat.providers)
