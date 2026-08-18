namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// 模型档案库 + 四席绑定（票 73）：**四 LLM 同桌**那一层的纯逻辑。
///
/// 这里钉的是四件事：
///
/// 1. **两个维度不缠在一起**——档案答「怎么问这个模型」（provider·模型·key·baseUrl·超时·
///    思考预算），座位答「给多少信息 / 什么风格 / 哪套措辞」（脚手架档位·人格·模板）。
///    一份档案坐两席、两席各带各的人格，正是 M2 对照实验要的形态。
/// 2. **默认那一桌仍是四家均匀随机**（票 42 的边界：黄金用例、双目标对拍与既有闸门
///    量的都是它跑出来的那几局）。
/// 3. **删掉一份还被引用的档案，那几席退回 bot 而且说得出来**——不许静静地变成「没有选手」。
/// 4. **老配置不许丢**：`janpo.llm.*` 那一份（含主人那把真 key）迁成一份档案 + 那一席的绑定。
///
/// localStorage 那一层不在这里（dotnet 上没有它）：迁移的判据由浏览器闸门
/// `web/scripts/verify-seats.mjs` 守着——那一趟真把老键灌进去、打开页面、看新键长出来。
module SeatingPlanTests =

    let private rules = Ruleset.yonma

    let private seat (index: int) : Seat =
        match Seat.ofIndex index with
        | Some seat -> seat
        | None -> failwith $"{index} 应当是合法座位"

    /// 一份填好的档案（key 是假的，一眼看得出）。
    let private zhang: ModelProfile =
        {
            Name = "凶狠的老张"
            Provider = "deepseek"
            Model = "deepseek-v4-flash"
            ApiKey = "sk-测试用的假 key"
            BaseUrl = ""
            TimeoutMs = 12000
            Thinking = Thinking.Off
        }

    /// 档案库里两份、四家还都是 bot 的一份坐法。
    let private library: SeatingPlan =
        { SeatingPlan.initial rules with
            Profiles =
                [
                    zhang
                    { zhang with
                        Name = "稳健的李四"
                        Model = "deepseek-v4"
                    }
                ]
        }

    let private nameAt (index: int) (plan: SeatingPlan) : string =
        SeatingPlan.names plan |> List.item index

    // ---- 默认那一桌（票 42 的边界） ----

    [<Fact>]
    let ``默认坐法是四家均匀随机：既有闸门量的仍是它`` () =
        let plan = SeatingPlan.initial rules

        Assert.Equal<string list>([ "random"; "random"; "random"; "random" ], SeatingPlan.names plan)
        Assert.Empty(SeatingPlan.llmSeats plan)
        // 库里先摆一份空档案（面板一打开就有得填），但**它不坐任何座位**：
        // 默认那一桌一个请求都不发。
        Assert.Equal(1, List.length plan.Profiles)
        Assert.Equal("", plan.Profiles.Head.ApiKey)

    [<Fact>]
    let ``四席各管各的：一席换成有主见，别的三席不动`` () =
        let plan =
            SeatingPlan.initial rules
            |> SeatingPlan.bind (seat 2) (SeatChoice.Bot Bot.Opinionated)

        Assert.Equal<string list>([ "random"; "random"; "opinionated"; "random" ], SeatingPlan.names plan)

    // ---- 四家都能是模型（这一票的正题） ----

    [<Fact>]
    let ``四个座位可以同时绑到档案上：这就是四 LLM 同桌`` () =
        let plan =
            (library, Seat.all rules)
            ||> List.fold (fun plan seat -> plan |> SeatingPlan.bind seat (SeatChoice.Profile zhang.Name))

        Assert.Equal<Seat list>(Seat.all rules, SeatingPlan.llmSeats plan)

        Assert.Equal<string list>([ for _ in Seat.all rules -> "deepseek/deepseek-v4-flash" ], SeatingPlan.names plan)

    [<Fact>]
    let ``同一份档案坐两席，两席各带各的人格与档位——key 只填一次`` () =
        // M2 的对照实验就是这个形态：自变量在座位上（人格 / 档位），
        // 「怎么问这个模型」那六格两席共用同一份。
        let plan =
            library
            |> SeatingPlan.bind (seat 0) (SeatChoice.Profile zhang.Name)
            |> SeatingPlan.bind (seat 1) (SeatChoice.Profile zhang.Name)
            |> SeatingPlan.editSeat (seat 0) SeatField.Persona "你打得很凶。"
            |> SeatingPlan.editSeat (seat 1) SeatField.Persona "你打得很稳。"
            |> SeatingPlan.editSeat (seat 1) SeatField.Tier "assisted"

        let configOf (index: int) =
            match SeatingPlan.playerAt (seat index) plan with
            | SeatPlayer.Llm config -> config
            | SeatPlayer.Bot _
            | SeatPlayer.Human -> failwith $"座位 {index} 该是模型"

        Assert.Equal("你打得很凶。", (configOf 0).Persona)
        Assert.Equal("你打得很稳。", (configOf 1).Persona)
        Assert.Equal(ScaffoldTier.Bare, (configOf 0).Tier)
        Assert.Equal(ScaffoldTier.Assisted, (configOf 1).Tier)

        // 「怎么问」那六格逐格相同——key 也只有档案里那一份。
        Assert.Equal(zhang.ApiKey, (configOf 0).ApiKey)
        Assert.Equal(zhang.ApiKey, (configOf 1).ApiKey)
        Assert.Equal((configOf 0).Provider, (configOf 1).Provider)
        Assert.Equal((configOf 0).Model, (configOf 1).Model)
        Assert.Equal((configOf 0).TimeoutMs, (configOf 1).TimeoutMs)
        Assert.Equal((configOf 0).Thinking, (configOf 1).Thinking)

    [<Fact>]
    let ``档案的名字不进牌谱：那一列恒是 provider slash model`` () =
        // 名字是**本机的私人叫法**（票 34 的闸门守着里面那把 key，这一条守着名字）。
        let plan = library |> SeatingPlan.bind (seat 3) (SeatChoice.Profile zhang.Name)

        Assert.Equal("deepseek/deepseek-v4-flash", nameAt 3 plan)
        Assert.DoesNotContain(zhang.Name, String.concat "," (SeatingPlan.names plan))
        // 配桌那一侧读的是同一份（`Roster.names` 就是牌谱第一条事件里那一列）。
        Assert.Equal<string list>(SeatingPlan.names plan, Roster.names (SeatingPlan.roster rules plan))

    [<Fact>]
    let ``档案答怎么问、座位答给多少信息：九格各归各家`` () =
        let binding: SeatBinding =
            {
                Choice = SeatChoice.Profile zhang.Name
                Tier = ScaffoldTier.Assisted
                Persona = "你打得很凶。"
                Template = """{"id":"我的模板"}"""
            }

        let config = SeatBinding.config zhang binding

        Assert.Equal(zhang.Provider, config.Provider)
        Assert.Equal(zhang.Model, config.Model)
        Assert.Equal(zhang.ApiKey, config.ApiKey)
        Assert.Equal(zhang.BaseUrl, config.BaseUrl)
        Assert.Equal(zhang.TimeoutMs, config.TimeoutMs)
        Assert.Equal(zhang.Thinking, config.Thinking)
        Assert.Equal(binding.Tier, config.Tier)
        Assert.Equal(binding.Persona, config.Persona)
        Assert.Equal(binding.Template, config.Template)

    // ---- 档案库的增删改 ----

    [<Fact>]
    let ``新建的档案不自动坐上任何座位，名字也不撞`` () =
        let plan = library |> SeatingPlan.addProfile

        Assert.Equal(3, List.length plan.Profiles)
        Assert.Equal<string list>(List.distinct (plan.Profiles |> List.map _.Name), plan.Profiles |> List.map _.Name)
        Assert.Empty(SeatingPlan.llmSeats plan)

    [<Fact>]
    let ``改档案名不把座位踢回 bot：引用跟着改`` () =
        // 引用是按名字的。不跟着改的话，改一个字就会**静默地**把那几席变成 bot
        // ——而「退回 bot」这件事只许由删档案那一步做，并且要说出来。
        let plan =
            library
            |> SeatingPlan.bind (seat 1) (SeatChoice.Profile zhang.Name)
            |> SeatingPlan.editProfile 0 ProfileField.Name "老张（medium）"

        Assert.Equal(SeatChoice.Profile "老张（medium）", (SeatingPlan.bindingAt (seat 1) plan).Choice)
        Assert.Equal("deepseek/deepseek-v4-flash", nameAt 1 plan)

    [<Fact>]
    let ``改档案里的 key 只改那一份：另一份不受影响`` () =
        let plan = library |> SeatingPlan.editProfile 1 ProfileField.ApiKey "sk-另一把假 key"

        Assert.Equal(zhang.ApiKey, (SeatingPlan.profileAt 0 plan).Value.ApiKey)
        Assert.Equal("sk-另一把假 key", (SeatingPlan.profileAt 1 plan).Value.ApiKey)

    [<Fact>]
    let ``删掉一份还被引用的档案：那几席退回 bot，而且点得出是哪几席`` () =
        let plan =
            library
            |> SeatingPlan.bind (seat 0) (SeatChoice.Profile zhang.Name)
            |> SeatingPlan.bind (seat 2) (SeatChoice.Profile zhang.Name)
            |> SeatingPlan.bind (seat 3) (SeatChoice.Profile "稳健的李四")

        Assert.Equal<Seat list>([ seat 0; seat 2 ], SeatingPlan.references zhang.Name plan)

        let after, orphans = SeatingPlan.removeProfile 0 plan

        // 页面那句话就是按这个列表写的（`TableState` 的 `Notice`）。
        Assert.Equal<Seat list>([ seat 0; seat 2 ], orphans)
        Assert.Equal(1, List.length after.Profiles)
        // 退回的是**均匀随机**（默认那一档），不是「没有选手」。
        Assert.Equal<string list>([ "random"; "random"; "random"; "deepseek/deepseek-v4" ], SeatingPlan.names after)
        // 没被牵连的那一席一个字都没动。
        Assert.Equal(SeatChoice.Profile "稳健的李四", (SeatingPlan.bindingAt (seat 3) after).Choice)

    [<Fact>]
    let ``引用不到的档案：那一席照样推得动牌桌（退回均匀随机）`` () =
        // localStorage 被手改过这类情形。**牌桌永远推得动**，与 `Roster.playerAt` 越界同一条规矩。
        let plan = library |> SeatingPlan.bind (seat 0) (SeatChoice.Profile "根本没有这一份")

        Assert.Equal("random", nameAt 0 plan)
        Assert.Empty(SeatingPlan.llmSeats plan)

    // ---- 状态线上那句话（票 42 的措辞照旧） ----

    [<Fact>]
    let ``四家同一种 bot 时那句话一字未改，混着坐时逐席报`` () =
        Assert.Equal("四家都是均匀随机的选手", SeatingPlan.botsToDisplay (SeatingPlan.initial rules))

        let mixed =
            SeatingPlan.initial rules
            |> SeatingPlan.bind (seat 1) (SeatChoice.Bot Bot.Opinionated)

        Assert.Contains("座位 1 有主见", SeatingPlan.botsToDisplay mixed)
        Assert.DoesNotContain("四家都是", SeatingPlan.botsToDisplay mixed)

    // ---- 座位数对齐 ----

    [<Fact>]
    let ``绑定的条数对齐到座位数：localStorage 里长歪了也不歪着用`` () =
        let short = SeatingPlan.fit rules { library with Seats = [] }

        let long =
            SeatingPlan.fit
                rules
                { library with
                    Seats = List.replicate 7 SeatBinding.initial
                }

        Assert.Equal(4, List.length short.Seats)
        Assert.Equal(4, List.length long.Seats)

    // ---- 老配置的迁移（票 73 的「不许丢」） ----

    /// 上一版页面写进 `janpo.llm.*` 的那一份（含主人那把真 key）。
    let private legacy: (string * string) list =
        [
            "seat", "2"
            "provider", "deepseek"
            "model", "deepseek-v4-flash"
            "api_key", "sk-老配置里那把假 key"
            "base_url", ""
            "timeout_ms", "180000"
            "thinking", "medium"
            "tier", "assisted"
            "persona", "你是一位以防守见长的雀士。"
            "template", """{"id":"我的模板"}"""
        ]

    let private reader (pairs: (string * string) list) : string -> string option =
        fun key -> pairs |> List.tryFind (fst >> (=) key) |> Option.map snd

    [<Fact>]
    let ``老配置迁成一份档案加那一席的绑定：一格都不许丢`` () =
        let plan =
            match SeatingPlan.ofLegacy rules (reader legacy) with
            | Some plan -> plan
            | None -> failwith "老键里有东西，就该迁得出来一份坐法"

        let profile = List.exactlyOne plan.Profiles

        // 「怎么问」那六格全在（**key 是重点**：主人那把真 key 就在这一格里）。
        Assert.Equal("deepseek", profile.Provider)
        Assert.Equal("deepseek-v4-flash", profile.Model)
        Assert.Equal("sk-老配置里那把假 key", profile.ApiKey)
        Assert.Equal(180000, profile.TimeoutMs)
        Assert.Equal(Thinking.Medium, profile.Thinking)

        // 座位级那三项跟着落到**老配置选中的那一席**上。
        let binding = SeatingPlan.bindingAt (seat 2) plan
        Assert.Equal(SeatChoice.Profile profile.Name, binding.Choice)
        Assert.Equal(ScaffoldTier.Assisted, binding.Tier)
        Assert.Equal("你是一位以防守见长的雀士。", binding.Persona)
        Assert.Equal("""{"id":"我的模板"}""", binding.Template)

        // 其余三席仍是均匀随机：迁移不许顺手多摆一席模型。
        Assert.Equal<Seat list>([ seat 2 ], SeatingPlan.llmSeats plan)

    [<Fact>]
    let ``老配置没选模型坐席：档案照建，四家仍是均匀随机`` () =
        // 那一版里 `seat` 存空串就是「四家都随机」（票 34 那道 key 闸门跑的正是这一档）。
        // 硬绑座位 0 会把默认那一桌悄悄改掉，而票 42 的闸门量的就是它。
        let plan =
            match SeatingPlan.ofLegacy rules (reader [ "api_key", "sk-躺着的那把假 key"; "seat", "" ]) with
            | Some plan -> plan
            | None -> failwith "老键里有东西，就该迁得出来一份坐法"

        Assert.Equal("sk-躺着的那把假 key", (List.exactlyOne plan.Profiles).ApiKey)
        Assert.Empty(SeatingPlan.llmSeats plan)
        Assert.Equal<string list>([ "random"; "random"; "random"; "random" ], SeatingPlan.names plan)

    [<Fact>]
    let ``老键一个都没有：没什么可迁的`` () =
        // 这台浏览器压根没配过。**判据在这里**：迁不迁只看老键里有没有东西，
        // 「迁过没有」由新格式那个 count 键答（`Store.readSeating`）。
        Assert.True(SeatingPlan.ofLegacy rules (reader []) |> Option.isNone)
        Assert.True(SeatingPlan.ofLegacy rules (reader [ "别的东西", "x" ]) |> Option.isNone)
