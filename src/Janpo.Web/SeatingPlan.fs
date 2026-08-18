namespace Janpo.Web

open Janpo

/// **模型档案**（CONTEXT.md 的 `ModelProfile`，票 73）：一份命名的「怎么问这个模型」
/// ——provider·模型·key·baseUrl·超时·思考预算。
///
/// **它不含脚手架档位、人格与 prompt 模板**：那三样是**座位级**的（`SeatBinding`）。
/// 一份档案能被两席同时引用、各带各的风格与信息量——M2 的对照实验要的正是这个形态，
/// 而 key 因此**只填一次**（界面上 key 只出现在档案编辑处）。
///
/// **名字是本机的私人叫法**：它既不过界给 Agent 层（`SeatBinding.config` 组出来的
/// `LlmSeat` 里根本没有这个字段），也不进牌谱（`Roster.names` 恒是 `provider/model`）。
type ModelProfile = {
    /// 这份档案在这台机器上叫什么。**只给人看**，不上任何 wire。
    Name: string
    /// provider id（pi-ai 的那套：`deepseek` / `anthropic` / …）。
    Provider: string
    /// 模型 id。
    Model: string
    /// API key。**不进牌谱、不进 DecisionRecord、不落任何可分享物**。
    ApiKey: string
    /// OpenAI 兼容端点的 baseUrl（票 30），只有 `Provider` 是 `customProvider` 时用得上。
    BaseUrl: string
    /// 一次请求最多等多久（毫秒）。
    TimeoutMs: int
    /// 思考预算。
    Thinking: Thinking
}

/// 档案编辑处的一个字段。**输入框都是字符串**（连超时也是），因此读与写各只有一条路：
/// `ModelProfile.field` 与 `ModelProfile.edit`。
///
/// **加一个字段就是加一个 case**：编译器会把该改的几处全指出来，localStorage 那一层
/// 连碰都不用碰（它遍历 `ProfileField.all`，键名取自 `ProfileField.key`）。
[<RequireQualifiedAccess>]
type ProfileField =
    | Name
    | Provider
    | Model
    | BaseUrl
    | ApiKey
    | TimeoutMs
    | Thinking

/// 一个座位交给谁（票 73，票 87 加上真人）：自带 bot 的两档、本地真人，或者某份档案。
///
/// **档案按名字引用**：名字是人在面板上认的东西，删掉那份档案时引用它的座位
/// 由 `SeatingPlan.removeProfile` 明确退回 bot（并让页面把这件事说出来），
/// 改名时由 `SeatingPlan.editProfile` 跟着改指向——两处都不留悬空引用。
[<RequireQualifiedAccess>]
type SeatChoice =
    /// 引擎自带的 bot（均匀随机 / 有主见）。
    | Bot of kind: Bot
    /// 引用一份模型档案。
    | Profile of name: string
    /// **我自己**（票 87）：坐在这台浏览器前的那个人。
    ///
    /// **它不引用任何东西**：真人没有档案（没有 provider、没有 key）；
    /// 座位级那三项（脚手架 / 人格 / 模板）对它今天也不生效——
    /// 新手辅助轮（术语表说 `ScaffoldTier` 在真人这一侧复用同一类型）是票 89 的事。
    ///
    /// **一桌只坐得下一席**（`SeatingPlan.soloHuman`）：本地就一个人一副眼睛。
    | Human

/// 座位级那三项里在面板上编辑的部分。**它们不在档案里**（术语表的分工：
/// 档案答「怎么问」，这三样答「给多少信息 / 什么风格 / 哪套措辞」）。
[<RequireQualifiedAccess>]
type SeatField =
    | Tier
    | Persona
    | Template

/// 一个座位的绑定（票 73）：交给谁，以及这一席自己的脚手架档位、人格与 prompt 模板。
///
/// **三项挂在座位上而不是档案上**是这一票的形态：同一份档案坐两席时，
/// 两席可以一个凶一个稳、一个裸奔一个信息辅助——那正是对照实验的自变量。
type SeatBinding = {
    /// 这一席交给谁。
    Choice: SeatChoice
    /// 脚手架档位（CONTEXT.md 的 `ScaffoldTier`）。
    Tier: ScaffoldTier
    /// 人格（CONTEXT.md 的 `Persona`）：一段自由文本，**一局内不变**。
    Persona: string
    /// prompt 模板的覆盖（CONTEXT.md 的 `PromptTemplate`），一段 JSON；**一局内不变**。
    Template: string
}

/// 这一桌的坐法在**页面这一侧**的那一份（票 73）：一个档案库 + 每席一条绑定。
///
/// **它不是 `Roster`**：`Roster` 是引擎那一侧要的东西（规则集 + 每席一个 `SeatPlayer`），
/// 由这一份推导出来（`SeatingPlan.roster`）。分两层是因为「引用哪份档案」这件事只有页面认，
/// 引擎连档案是什么都不该知道（ADR-0001）。
type SeatingPlan = {
    /// 档案库，按新建顺序。
    Profiles: ModelProfile list
    /// 每席一条，按座位升序。
    Seats: SeatBinding list
}

/// 模型档案的默认值、provider 那一列，以及一个字段怎么改。
/// **它不碰 localStorage**：读写浏览器本地存储是 `Store` 的事。
[<RequireQualifiedAccess>]
module ModelProfile =

    /// 自定义端点那一项的 provider id（票 30）。**它不是 pi-ai 认识的一家**：
    /// 选中它时 Agent 层按 `BaseUrl` 现搭一个 OpenAI 兼容 provider。
    /// TS 侧的同名常量在 `web/src/agent/endpoint.ts` 的 `CUSTOM_PROVIDER`——**改一处要改两处**。
    ///
    /// **带个 `-openai` 后缀是故意的**（票 46/30-A）：它原来叫 `custom`，
    /// pi-ai 哪天真有一家叫 `custom`，那一家就会**静默地**进不来（编译不会红）。
    let customProvider = "custom-openai"

    /// 旧的那个 id（票 30 定的，票 46 改掉）。**它只活在迁移路上**：
    /// 下拉框里已经没有它，但上一版页面存进 localStorage 的就是这个字串。
    let legacyCustomProvider = "custom"

    /// 能选的 provider。**Bedrock 不在里面**（Node-only，票 18 的实测）；
    /// 订阅制的 OAuth 登录同样只有 Node 有，因此这里只列「填一把 API key 就能用」的那几家。
    /// **自定义端点排在最后**：它不是又一家，而是「你自己那一家」。
    let providers: string list = [
        "deepseek"
        "anthropic"
        "openai"
        "google"
        "openrouter"
        "xai"
        "groq"
        "mistral"
        customProvider
    ]

    /// 这份档案走自定义端点吗。**只看 provider**：官方八家填了 baseUrl 也不走（票 30）。
    let isCustom (profile: ModelProfile) : bool = profile.Provider = customProvider

    /// provider 列表上的显示名。**渲染层的单向出口**（ADR-0001）：
    /// 官方八家就是它们自己的 id，只有自定义端点要一句中文说清它是什么。
    let providerToDisplay (provider: string) : string =
        if provider = customProvider then
            "自定义端点（OpenAI 兼容）"
        else
            provider

    /// 一份新档案的默认值。默认 provider 取 DeepSeek：票 18 实测过的就是它（跨域 200）。
    ///
    /// **名字带序号**（`SeatingPlan.freshName` 接着往下编）：档案库里两份同名的话，
    /// 座位引用的是头一份——名字是引用的键，因此默认值不能是空串。
    let initial: ModelProfile = {
        Name = "档案 1"
        Provider = "deepseek"
        Model = "deepseek-v4-flash"
        ApiKey = ""
        // 官方八家用不上它（票 30）。
        BaseUrl = ""
        // **4 分钟**（票 72 重定）。旧值 30 秒是票 23 在没有思考预算的年代定的（票 18 实测
        // 单轮 tool call 约 2.4 秒），而 **DeepSeek medium 思考实测单手 17–180 秒**
        // （DECISIONS 2026-08-16）——开着思考预算时 30 秒会把大半手掐成兜底，而兜底代打的
        // 那几手量的不再是模型。取实测上界 180 秒再加一成三的余量；**不再大**是因为超时还要
        // 重试两次（`Agent.retryLimit`），最坏情形下一手要等 3 × 这个数才看得出「模型不说话了」。
        TimeoutMs = 240000
        Thinking = Thinking.Off
    }

    /// 字段现在的值（输入框里显示的那个）。
    let field (which: ProfileField) (profile: ModelProfile) : string =
        match which with
        | ProfileField.Name -> profile.Name
        | ProfileField.Provider -> profile.Provider
        | ProfileField.Model -> profile.Model
        | ProfileField.BaseUrl -> profile.BaseUrl
        | ProfileField.ApiKey -> profile.ApiKey
        | ProfileField.TimeoutMs -> string profile.TimeoutMs
        | ProfileField.Thinking -> Thinking.toWire profile.Thinking

    /// 读一个 provider id。**只有一条迁移**（票 46/30-A）：旧的 `custom` 当场升成
    /// `custom-openai`——两个 id 指的本来就是同一件事（自定义 OpenAI 兼容端点），
    /// 而把旧值原样留着才是**静默地读成别的东西**：`isCustom` 会当它不是自定义端点，
    /// 转而拿 `custom` 去 pi-ai 的目录里查一家。其余值一律原样。
    let readProvider (value: string) : string =
        if value = legacyCustomProvider then
            customProvider
        else
            value

    /// 改一个字段。**读不懂的值一律原样不改**（超时框里的非数字、认不出的思考档位）：
    /// 配置是人填的，不该因为一次误输入就把设定清掉。
    let edit (which: ProfileField) (value: string) (profile: ModelProfile) : ModelProfile =
        match which with
        // 名字原样存着（连空串也是）：面板上正在改名的中间态不该被吐回去。
        // **改名要跟着改座位的指向**，那一步在 `SeatingPlan.editProfile`——这一层只认一份档案。
        | ProfileField.Name -> { profile with Name = value }
        | ProfileField.Provider -> {
            profile with
                Provider = readProvider value
          }
        | ProfileField.Model -> { profile with Model = value }
        // baseUrl 原样存着，这一层不判读：合法与否是 Agent 层的事（`endpoint.ts`），
        // 而且人边打边存的中间态（`http://`）不该被吐回去。
        | ProfileField.BaseUrl -> { profile with BaseUrl = value }
        | ProfileField.ApiKey -> { profile with ApiKey = value }
        | ProfileField.TimeoutMs ->
            match System.Int32.TryParse(value.Trim()) with
            | true, timeout when timeout > 0 -> { profile with TimeoutMs = timeout }
            | _ -> profile
        | ProfileField.Thinking ->
            match Thinking.ofWire value with
            | Some thinking -> { profile with Thinking = thinking }
            | None -> profile

/// 档案那几个字段的全体与它们的键名。
[<RequireQualifiedAccess>]
module ProfileField =

    /// 全部字段，按档案编辑处上的顺序。localStorage 的读写遍历它。
    let all: ProfileField list = [
        ProfileField.Name
        ProfileField.Provider
        ProfileField.Model
        ProfileField.BaseUrl
        ProfileField.ApiKey
        ProfileField.TimeoutMs
        ProfileField.Thinking
    ]

    /// localStorage 里的键名（不带前缀）。**只有这一份**：写的与读的拼不到一块去。
    ///
    /// **这六个键名与票 23 那一版逐字相同**（`name` 是新的）：老配置的迁移就是拿它们
    /// 去老前缀 `janpo.llm.` 下面读一遍（`SeatingPlan.ofLegacy`）。
    let key (field: ProfileField) : string =
        match field with
        | ProfileField.Name -> "name"
        | ProfileField.Provider -> "provider"
        | ProfileField.Model -> "model"
        | ProfileField.BaseUrl -> "base_url"
        | ProfileField.ApiKey -> "api_key"
        | ProfileField.TimeoutMs -> "timeout_ms"
        | ProfileField.Thinking -> "thinking"

/// 座位那三项的全体与它们的键名。
[<RequireQualifiedAccess>]
module SeatField =

    /// 全部字段，按座位那一行上的顺序。
    let all: SeatField list = [ SeatField.Tier; SeatField.Persona; SeatField.Template ]

    /// localStorage 里的键名（不带前缀）。**与票 23 那一版逐字相同**，理由同 `ProfileField.key`。
    let key (field: SeatField) : string =
        match field with
        | SeatField.Tier -> "tier"
        | SeatField.Persona -> "persona"
        | SeatField.Template -> "template"

/// 一个座位交给谁：wire 名与两个方向的转换。
[<RequireQualifiedAccess>]
module SeatChoice =

    /// 引用一份档案时的前缀。**冒号后面整段都是名字**（名字里带冒号也拆得对）。
    let private profilePrefix = "profile:"

    /// localStorage 里的写法。自带 bot 那两档直接借 `Bot.toWire`
    /// （`random` / `opinionated`，与牌谱里那个名字同一份真源）；
    /// 真人那一档借 `Roster.humanName`（同一理由：牌谱里写的就是它）。
    let toWire (choice: SeatChoice) : string =
        match choice with
        | SeatChoice.Bot kind -> Bot.toWire kind
        | SeatChoice.Profile name -> profilePrefix + name
        | SeatChoice.Human -> Roster.humanName

    /// wire 名回到绑定。认不出来的是 None——配置从 localStorage 读，什么都可能。
    let ofWire (wire: string) : SeatChoice option =
        if wire.StartsWith profilePrefix then
            Some(SeatChoice.Profile(wire.Substring profilePrefix.Length))
        elif wire = Roster.humanName then
            Some SeatChoice.Human
        else
            Bot.all
            |> List.tryFind (fun kind -> Bot.toWire kind = wire)
            |> Option.map SeatChoice.Bot

/// 座位绑定的默认值、一个字段怎么改，以及「一份档案 + 这一席三项」怎么合成发出去的那份配置。
[<RequireQualifiedAccess>]
module SeatBinding =

    /// 一席的默认绑定：**均匀随机**（票 42 的边界——黄金用例、双目标对拍与既有闸门
    /// 量的都是它跑出来的那几局），裸奔档，没有人格与模板。
    let initial: SeatBinding = {
        Choice = SeatChoice.Bot Bot.Uniform
        // 默认裸奔：它是对照组（量「模型自己会不会数牌」），加脚手架是主持人自己拨的实验变量。
        Tier = ScaffoldTier.Bare
        // 不填就是默认模板、没有人格（票 31）。
        Persona = ""
        Template = ""
    }

    /// 字段现在的值（输入框里显示的那个）。
    let field (which: SeatField) (binding: SeatBinding) : string =
        match which with
        | SeatField.Tier -> ScaffoldTier.toWire binding.Tier
        | SeatField.Persona -> binding.Persona
        | SeatField.Template -> binding.Template

    /// 改一个字段。**读不懂的档位原样不改**（同 `ModelProfile.edit`）；
    /// 人格与模板原样存着，这一层不判读：它们的形状在 Agent 层（`template.ts`），
    /// 而人边打边存的中间态（写一半的 JSON）不该被吐回去。
    let edit (which: SeatField) (value: string) (binding: SeatBinding) : SeatBinding =
        match which with
        | SeatField.Tier ->
            match ScaffoldTier.ofWire value with
            | Some tier -> { binding with Tier = tier }
            | None -> binding
        | SeatField.Persona -> { binding with Persona = value }
        | SeatField.Template -> { binding with Template = value }

    /// 一份档案 + 这一席那三项 → **真发给 Agent 层的那份配置**（`LlmSeat`）。
    ///
    /// **档案的名字不在里面**：它是本机的私人叫法，跨界那段 JSON 与牌谱都不认它（票 73）。
    /// 这一步是「档案答怎么问、座位答给多少信息 / 什么风格 / 哪套措辞」这条分工的落点：
    /// 六格从档案来，三格从座位来，一格都不重叠。
    let config (profile: ModelProfile) (binding: SeatBinding) : LlmSeat = {
        Provider = profile.Provider
        Model = profile.Model
        ApiKey = profile.ApiKey
        BaseUrl = profile.BaseUrl
        TimeoutMs = profile.TimeoutMs
        Thinking = profile.Thinking
        Tier = binding.Tier
        Persona = binding.Persona
        Template = binding.Template
    }

/// 档案库 + 四席绑定：查表、编辑，以及推导出这一桌的 `Roster`。
///
/// `ModuleSuffix`：同名的 `type SeatingPlan` 就在上面，F# 只在紧挨着的那一对上自动加后缀，
/// 中间隔了几个模块就得手写（同 `Janpo.Ryuukyoku`）。叫法不变，只是 IL 里多一个 `Module` 后缀。
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SeatingPlan =

    // ---- 构造 ----

    /// 这个规则集下每席一条绑定的默认坐法：**四家均匀随机**（票 42 的边界）。
    let private bindings (ruleset: Ruleset) : SeatBinding list =
        Seat.all ruleset |> List.map (fun _ -> SeatBinding.initial)

    /// 头一次打开这一页时的坐法：一份还没填 key 的空档案 + 四家均匀随机。
    ///
    /// **档案库里先摆一份**而不是空着：面板上那一屏因此一打开就有得填，
    /// 而「哪一席是模型」仍旧是主持人自己绑的——默认那一桌一个请求都不发。
    let initial (ruleset: Ruleset) : SeatingPlan = {
        Profiles = [ ModelProfile.initial ]
        Seats = bindings ruleset
    }

    /// 这一席上的真人站起来（退回均匀随机）；不是真人席就原样。
    let private vacated (binding: SeatBinding) : SeatBinding =
        if binding.Choice = SeatChoice.Human then
            {
                binding with
                    Choice = SeatBinding.initial.Choice
            }
        else
            binding

    /// **真人只坐得下一席**（票 87）：头一席算数，余下那几席站起来。
    ///
    /// 本地就一个人一副眼睛：第二席真人没有人操作，那一桌会停在那儿等一个永远不来的动作；
    /// 而且「视角锁死自家那一席」这句话当场就没了主语（锁哪一席？）。
    ///
    /// **它只管 `fit`（从 localStorage 读回来的那一份）**：那里什么都可能，
    /// 而“留头一席”至少是确定的。人在面板上拨那一下走的是 `bind`（那里**刚拨的那一席赢**）。
    let private soloHuman (seats: SeatBinding list) : SeatBinding list =
        let seated =
            seats |> List.tryFindIndex (fun binding -> binding.Choice = SeatChoice.Human)

        seats
        |> List.mapi (fun index binding -> if Some index = seated then binding else vacated binding)

    /// 把绑定的条数对齐到这个规则集的座位数：少了补默认、多了截掉，
    /// 并把多出来的真人席掰回 bot（`soloHuman`，票 87）。
    /// **从 localStorage 读回来的那一份要过这道**（三麻改四麻、手改过存储都会长歪）。
    let fit (ruleset: Ruleset) (seating: SeatingPlan) : SeatingPlan = {
        seating with
            Seats =
                Seat.all ruleset
                |> List.mapi (fun index _ ->
                    List.tryItem index seating.Seats |> Option.defaultValue SeatBinding.initial)
                |> soloHuman
    }

    // ---- 查表 ----

    /// 第几份档案（0 起）；越界是 None。
    let profileAt (index: int) (seating: SeatingPlan) : ModelProfile option = List.tryItem index seating.Profiles

    /// 叫这个名字的档案。**两份同名时取头一份**：名字是引用的键，
    /// 而面板上正在改名的中间态可以短暂撞名（`ModelProfile.edit` 不拦它）。
    let tryProfile (name: string) (seating: SeatingPlan) : ModelProfile option =
        seating.Profiles |> List.tryFind (fun profile -> profile.Name = name)

    /// 这一席的绑定；越界按默认那一条算（牌桌永远推得动，不因为配置出错卡住）。
    let bindingAt (seat: Seat) (seating: SeatingPlan) : SeatBinding =
        Seat.tryItem seat seating.Seats |> Option.defaultValue SeatBinding.initial

    /// 一条绑定 → 坐在那儿的选手。
    ///
    /// **引用不到那份档案时退回均匀随机**：删档案那一步已经把引用它的座位改回 bot 了
    /// （`removeProfile`），因此这一支只在「localStorage 被手改过」这类情形下走得到——
    /// 那时也要让这一桌照样推得动。
    let private playerOf (seating: SeatingPlan) (binding: SeatBinding) : SeatPlayer =
        match binding.Choice with
        | SeatChoice.Bot kind -> SeatPlayer.Bot kind
        | SeatChoice.Human -> SeatPlayer.Human
        | SeatChoice.Profile name ->
            match tryProfile name seating with
            | Some profile -> SeatPlayer.Llm(SeatBinding.config profile binding)
            | None -> SeatPlayer.Bot Bot.Uniform

    /// 这一席坐着谁。
    let playerAt (seat: Seat) (seating: SeatingPlan) : SeatPlayer =
        bindingAt seat seating |> playerOf seating

    /// 这一桌的配桌（CONTEXT.md 的 `Roster`）：规则集 + 每席一个选手。
    /// **推导出来而不存下来**：坐法只有 `SeatingPlan` 这一份，不会与第二份对不上。
    let roster (ruleset: Ruleset) (seating: SeatingPlan) : Roster =
        Roster.create ruleset (Seat.all ruleset |> List.map (fun seat -> playerAt seat seating))

    /// 各席在牌谱里的名字，按座位升序（`Roster.names` 那一份，只是不必先造 `Roster`）。
    /// **档案的名字不在里面**：LLM 席叫 `provider/model`。
    let names (seating: SeatingPlan) : string list =
        seating.Seats |> List.map (playerOf seating >> Roster.playerName)

    /// 坐着模型的那几席。
    let llmSeats (seating: SeatingPlan) : Seat list =
        seating.Seats
        |> Seat.indexed
        |> List.choose (fun (seat, binding) ->
            match playerOf seating binding with
            | SeatPlayer.Llm _ -> Some seat
            | SeatPlayer.Bot _
            | SeatPlayer.Human -> None)

    /// 真人坐着的那几席（票 87）。**形状仍是一个表**（同 `llmSeats`），
    /// 而 `soloHuman` 保证它至多一项——不写成 `Seat option` 是为了
    /// “这一条不变量由谁执行”看得见（判据 2）：执行它的是 `soloHuman`，不是这个类型。
    let humanSeats (seating: SeatingPlan) : Seat list =
        seating.Seats
        |> Seat.indexed
        |> List.choose (fun (seat, binding) ->
            match playerOf seating binding with
            | SeatPlayer.Human -> Some seat
            | SeatPlayer.Bot _
            | SeatPlayer.Llm _ -> None)

    /// 引用了这份档案的那几席。删档案时要靠它把话说清楚。
    let references (name: string) (seating: SeatingPlan) : Seat list =
        seating.Seats
        |> Seat.indexed
        |> List.choose (fun (seat, binding) ->
            match binding.Choice with
            | SeatChoice.Profile referenced when referenced = name -> Some seat
            | _ -> None)

    // ---- 渲染层出口（ADR-0001） ----

    /// 真人坐席给人看的那一半（票 87）。**只有这一份**：面板上那枚按钮、
    /// 牌桌上的名牌、状态线上那句话读的都是它。
    /// **不写昵称**：平台不知道你叫什么，也不该问（牌谱里那一列恒是 `human`）。
    let humanToDisplay: string = "我自己"

    /// 牌桌上每家名牌上那一句「这一席是谁在打」（票 82），按座位升序。
    ///
    /// **模型席写的是档案名 + 脚手架档位**：两席引同一份档案而档位不同是对照实验的常态
    /// （CONTEXT.md 的 `ModelProfile` / `ScaffoldTier` 是两个维度），只写档案名就分不出那两席。
    /// **bot 席不写档位**：它们不走 prompt，那一格对它们没意义，写上去只会让人以为它在生效。
    ///
    /// **它不是 `names`**（那一份恒是 `provider/model`，上牌谱）：档案名是本机的私人叫法，
    /// 只活在这一页上（`ModelProfile.Name` 那条术语）。
    let nameplates (seating: SeatingPlan) : string list =
        seating.Seats
        |> List.map (fun binding ->
            match binding.Choice with
            | SeatChoice.Bot kind -> Bot.toDisplay kind
            | SeatChoice.Human -> humanToDisplay
            | SeatChoice.Profile name ->
                match tryProfile name seating with
                | Some profile -> $"{profile.Name}・{ScaffoldTier.toDisplay binding.Tier}"
                // 引的那份档案被删了：这一席**真的**退回了均匀随机（`playerOf` 同一条），
                // 名牌就要跟着说实话——写着模型名而实际在打的是 bot 是句假话。
                | None -> Bot.toDisplay Bot.Uniform)

    /// 四家全是自带 bot 时状态线上那句话。**同一种 bot 坐满时仍旧是「四家都是……的选手」**
    /// （票 42 那句话一字未改），混着坐时逐席报——写一句「四家都是随机选手」
    /// 而实际有一席是有主见的，那就是句错话。
    let botsToDisplay (seating: SeatingPlan) : string =
        let kinds =
            seating.Seats
            |> List.map (fun binding ->
                match binding.Choice with
                | SeatChoice.Bot kind -> Bot.toDisplay kind
                | SeatChoice.Human -> humanToDisplay
                | SeatChoice.Profile name -> name)

        match List.distinct kinds with
        | [ only ] -> $"四家都是{only}的选手"
        | _ -> kinds |> List.mapi (fun index kind -> $"座位 {index} {kind}") |> String.concat "　"

    // ---- 编辑 ----

    /// 只改一席的绑定，其余原样。
    let private withBinding (seat: Seat) (change: SeatBinding -> SeatBinding) (seating: SeatingPlan) : SeatingPlan = {
        seating with
            Seats = seating.Seats |> Seat.mapAt seat change
    }

    /// 把一席交给谁。
    ///
    /// **坐上第二席真人时，原来那一席当场退回均匀随机**（票 87：本地就一个人）。
    /// **刚拨的那一席赢**：人刚把自己摆到座位 3，结果坐到了座位 0——那才叫话都不说。
    let bind (seat: Seat) (choice: SeatChoice) (seating: SeatingPlan) : SeatingPlan =
        let vacant =
            match choice with
            | SeatChoice.Human -> {
                seating with
                    Seats = seating.Seats |> List.map vacated
              }
            | SeatChoice.Bot _
            | SeatChoice.Profile _ -> seating

        vacant |> withBinding seat (fun binding -> { binding with Choice = choice })

    /// 改一席的脚手架档位 / 人格 / 模板。
    let editSeat (seat: Seat) (which: SeatField) (value: string) (seating: SeatingPlan) : SeatingPlan =
        seating |> withBinding seat (SeatBinding.edit which value)

    /// 档案库里还没被用过的一个名字（`档案 2`、`档案 3`……）。
    let freshName (seating: SeatingPlan) : string =
        let taken (name: string) =
            seating.Profiles |> List.exists (fun profile -> profile.Name = name)

        let rec pick (n: int) =
            if taken $"档案 {n}" then pick (n + 1) else $"档案 {n}"

        pick 1

    /// 新建一份档案，摆在库尾。**新的那一份不自动坐上任何座位**：
    /// 谁坐哪里由主持人自己绑（默认那一桌因此仍是四家均匀随机）。
    let addProfile (seating: SeatingPlan) : SeatingPlan = {
        seating with
            Profiles =
                seating.Profiles
                @ [
                    {
                        ModelProfile.initial with
                            Name = freshName seating
                    }
                ]
    }

    /// 改第几份档案的一个字段。
    ///
    /// **改名时跟着改座位的指向**：引用是按名字的，不跟着改的话那几席会**静默地**
    /// 退回 bot——而「退回 bot」这件事只许由删档案那一步做，并且要说出来。
    let editProfile (index: int) (which: ProfileField) (value: string) (seating: SeatingPlan) : SeatingPlan =
        match profileAt index seating with
        | None -> seating
        | Some before ->
            let after = ModelProfile.edit which value before

            let repoint (binding: SeatBinding) =
                match binding.Choice with
                | SeatChoice.Profile name when name = before.Name -> {
                    binding with
                        Choice = SeatChoice.Profile after.Name
                  }
                | _ -> binding

            {
                Profiles =
                    seating.Profiles
                    |> List.mapi (fun each profile -> if each = index then after else profile)
                Seats =
                    if after.Name = before.Name then
                        seating.Seats
                    else
                        seating.Seats |> List.map repoint
            }

    /// 删掉第几份档案。**返回被它牵连的那几席**（它们当场退回均匀随机）：
    /// 页面要把这件事说出来，不许静静地变成「没有选手」。
    let removeProfile (index: int) (seating: SeatingPlan) : SeatingPlan * Seat list =
        match profileAt index seating with
        | None -> seating, []
        | Some doomed ->
            let orphans = references doomed.Name seating

            let unbind (binding: SeatBinding) =
                match binding.Choice with
                | SeatChoice.Profile name when name = doomed.Name -> {
                    binding with
                        Choice = SeatBinding.initial.Choice
                  }
                | _ -> binding

            {
                Profiles =
                    seating.Profiles
                    |> List.indexed
                    |> List.filter (fst >> (<>) index)
                    |> List.map snd
                Seats = seating.Seats |> List.map unbind
            },
            orphans

    // ---- 老配置的迁移（票 73） ----

    /// 上一版页面存在 `janpo.llm.*` 下面的那一份单席配置 → 一份档案 + 那一席的绑定。
    ///
    /// **判据是「老键里有没有东西」**：一个都没有就是 None（这台浏览器压根没配过），
    /// 有就把它整份读进来——**主人那把真 key 不许在改版里丢掉**。
    ///
    /// **绑的是老配置选中的那一席**（老键 `seat` 空着就是四家 bot，那时只建档案、不绑座位）：
    /// 硬绑座位 0 会把「四家均匀随机」这个默认桌悄悄改掉，而票 42 的闸门量的正是它。
    ///
    /// **只收一个取值器**：localStorage 只在浏览器里有，这样这条迁移在 dotnet 上也测得动。
    let ofLegacy (ruleset: Ruleset) (read: string -> string option) : SeatingPlan option =
        let keys =
            "seat" :: (ProfileField.all |> List.map ProfileField.key)
            @ (SeatField.all |> List.map SeatField.key)

        if keys |> List.forall (read >> Option.isNone) then
            None
        else
            let profile =
                (ModelProfile.initial, ProfileField.all)
                ||> List.fold (fun profile field ->
                    match read (ProfileField.key field) with
                    | Some value -> ModelProfile.edit field value profile
                    | None -> profile)

            let binding =
                (SeatBinding.initial, SeatField.all)
                ||> List.fold (fun binding field ->
                    match read (SeatField.key field) with
                    | Some value -> SeatBinding.edit field value binding
                    | None -> binding)

            let seated =
                read "seat"
                |> Option.bind (fun value ->
                    match System.Int32.TryParse value with
                    | true, index -> Seat.all ruleset |> List.tryFind (fun seat -> Seat.index seat = index)
                    | false, _ -> None)

            let seats =
                match seated with
                | None -> bindings ruleset
                | Some seat ->
                    bindings ruleset
                    |> Seat.mapAt seat (fun _ -> {
                        binding with
                            Choice = SeatChoice.Profile profile.Name
                    })

            Some {
                Profiles = [ profile ]
                Seats = seats
            }
