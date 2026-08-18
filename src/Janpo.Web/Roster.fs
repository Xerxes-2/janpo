namespace Janpo.Web

open Janpo

/// 引擎自带的两种 bot（票 42）。**两种都是随机选手**，差别只在有没有主见。
///
/// 跑批那个覆盖型偏好（`RandomPlayer.covering`）**不在这里**：它是量具
/// （见和就和、九种九牌权重 500），不是给人看的对手。它只在 CLI 上选得到。
[<RequireQualifiedAccess>]
type Bot =
    /// 均匀随机（`Kyoku.randomPlayer`）：从合法动作集里等概率挑一个。
    /// **它是 40 条黄金用例与双目标对拍的基准，行为一个字节都不许动**（票 42 的边界）。
    | Uniform
    /// 有主见的随机（`OpinionatedPlayer.player`）：能和就和、听牌就立直、有役才鸣。
    /// 立直、一发、里宝牌、供托与振听这些路径靠它才在正常对局里出现。
    | Opinionated

/// 坐在一个座位上的选手（CONTEXT.md 的 Player）。**四种选手对引擎完全同级**：
/// 给定观测与合法动作集，返回一个动作。差别只在「返回得同步还是要等」——
/// 而那件事在 `Table.decide` 那道缝上分岔，引擎一无所知。
///
/// M1 只有两种，票 87 加上真人坐席，票 92 加上强 AI 基线（强 AI 与真人坐席是 M3 的 case）。
///
/// **它与 `SeatChoice` 是两层**（票 73）：页面上人选的是「均匀随机 / 有主见 / 我自己 / 某份档案」
/// （`SeatChoice`，按名字引用档案），推导到这一层时档案已经展开成一份 `LlmSeat`。
[<RequireQualifiedAccess>]
type SeatPlayer =
    /// 引擎自带的 bot。动作当场就有。
    | Bot of kind: Bot
    /// LLM 适配器：决策包发给 Agent 层，动作由后来的一条 Msg 带回来。
    | Llm of config: LlmSeat
    /// **本地真人**（CONTEXT.md 的 `Human Seat`，票 87）：决策包摆到页面上，
    /// 动作由他点一下手牌带回来（`TableMsg.HumanPlayed`）。
    ///
    /// **它不带任何配置**：真人没有 provider、没有 key、没有超时（时限是票 89）；
    /// 脚手架档位（新手辅助轮，术语表说它复用同一类型）也是票 89 的事。
    | Human
    /// **强 AI 基线**（票 92，ADR-0006）：浏览器内 WASM 推理的那个网络。
    ///
    /// **它与 `Llm` 一样是异步的、与 `Human` 一样不带配置**：决策包过界（`Baseline.ask`），
    /// 回来的同样只有一个动作 id（ADR-0005）。没有 provider、没有 key、没有超时预算
    /// ——那几 MB 的资产**整桌只有一份**，拉不拉得动是 `BaselineStatus` 的事，不是座位的事。
    ///
    /// **它不会说话**：没有 thinking、没有一句话理由、没有 token 账单，因此它一条
    /// `DecisionRecord` 都不留（与 bot 席同级）——牌谱里认得出它的只有 `names`。
    | Baseline

/// 自带 bot 的实现与两种渲染。
[<RequireQualifiedAccess>]
module Bot =

    // ---- 拆解 ----

    /// 牌桌上选得到的那几种，按摆出来的顺序。
    let all: Bot list = [ Bot.Uniform; Bot.Opinionated ]

    /// 它的引擎实现。**均匀那档走的仍是 `Kyoku.randomPlayer` 本尊**：
    /// 换成同义的 `RandomPlayer.uniform` 也逐手一致（SoakTests 钉着），
    /// 但这一票的边界是「不碰它」，因此连指过去的那只手都不动。
    let player (kind: Bot) : Player<Rng> =
        match kind with
        | Bot.Uniform -> Kyoku.randomPlayer
        | Bot.Opinionated -> OpinionatedPlayer.player

    // ---- 渲染层出口（ADR-0001） ----

    /// **牌谱里的名字**（mjai `start_game` 的 `names`），不是给人看的那一份。
    /// 均匀那档仍叫 `random`：牌谱是可分享物，改它等于把既有牌谱的读法改了。
    let toWire (kind: Bot) : string =
        match kind with
        | Bot.Uniform -> "random"
        | Bot.Opinionated -> "opinionated"

    /// **渲染层的单向出口**：牌桌上那个控件的中文标签。
    let toDisplay (kind: Bot) : string =
        match kind with
        | Bot.Uniform -> "均匀随机"
        | Bot.Opinionated -> "有主见"

/// 配桌（CONTEXT.md 的 Roster）：这一桌按什么规则打、每个座位由谁来决策。
///
/// **规则集在里面**：坐位数本来就由 `Ruleset` 定（三麻只有三家），
/// 两者分开拿的话「四家的配桌配上三麻的牌桌」在类型上是合法的。
/// **但它不是第二份牌局状态**：牌在 `GameState` 里，配桌只说人与规则。
type Roster = {
    /// 这一场所遵的规则集。牌桌就是按它开的（`TablePage.openTable`）。
    Ruleset: Ruleset
    /// 谁坐哪个座位，按座位升序。
    Seats: SeatPlayer list
}

/// 配桌的构造与查表。
[<RequireQualifiedAccess>]
module Roster =

    // ---- 构造 ----

    /// 四家都是同一种自带 bot（22 号票那一桌）。
    let allBots (ruleset: Ruleset) (kind: Bot) : Roster = {
        Ruleset = ruleset
        Seats = Seat.all ruleset |> List.map (fun _ -> SeatPlayer.Bot kind)
    }

    /// 四家都是均匀随机选手。
    let allRandom (ruleset: Ruleset) : Roster = allBots ruleset Bot.Uniform

    /// 每席各指定一个选手（票 73：四家都可以是模型）。
    /// **谁坐哪里由页面那一侧推**（`SeatingPlan.roster`）：引擎不认识「模型档案」这件事。
    let create (ruleset: Ruleset) (seats: SeatPlayer list) : Roster = { Ruleset = ruleset; Seats = seats }

    // ---- 查表 ----

    /// 这个座位是谁。**越界按均匀随机算**：牌桌永远推得动，不因为配置出错卡住。
    let playerAt (seat: Seat) (roster: Roster) : SeatPlayer =
        Seat.tryItem seat roster.Seats
        |> Option.defaultValue (SeatPlayer.Bot Bot.Uniform)

    /// 强 AI 基线在牌谱里那个名字（票 92）。**通名，不写具体是谁**（ADR-0006 边界 5：
    /// 具体是哪一个网络只写在 NOTICE、报告与页脚里）——牌谱是可分享物，
    /// 而「上游是谁、哪一版权重」是站点的署名义务，不是这一行数据的内容。
    ///
    /// **与另外三种分得开**：模型席恒带一道斜杠（`provider/model`），
    /// bot 叫 `random` / `opinionated`，真人叫 `human`，都不撞。
    let baselineName: string = "baseline"

    /// 真人坐席在牌谱里那个名字（票 87）。**只有这一份**，奇权在它身上：
    ///
    /// - **与模型席分得开**：模型席恒带一道斜杠（`provider/model`），它没有；
    ///   bot 叫 `random` / `opinionated`，也不撞。复盘（票 90）与分享都要读它。
    /// - **里面没有任何私人信息**：不写昵称、不写档案名、更不写 key——
    ///   牌谱是可分享物（ADR-0002），而真人自己那一行就是最容易渗出去的那一行。
    let humanName: string = "human"

    /// 一个选手在牌谱里的名字。**只有这一份**：页面上那一行摘要（`SeatingPlan.names`）
    /// 读的也是它，两份写法只会漂。
    ///
    /// **档案的名字不在里面**（票 73）：那是本机的私人叫法，
    /// 而牌谱是可分享物——LLM 席恒叫 `provider/model`。
    let playerName (player: SeatPlayer) : string =
        match player with
        | SeatPlayer.Bot kind -> Bot.toWire kind
        | SeatPlayer.Llm config -> $"{config.Provider}/{config.Model}"
        | SeatPlayer.Human -> humanName
        | SeatPlayer.Baseline -> baselineName

    /// 各家的名字，按座位升序——mjai `start_game` 的 `names`，也就是牌谱第一条事件里的那一列。
    ///
    /// **它是 wire 数据不是渲染**（ADR-0001）：自带 bot 叫 `random` / `opinionated`，
    /// LLM 座位叫 `provider/model`。
    /// **key 不在里面**：牌谱是可分享物，它里头永远不能出现 API key。
    let names (roster: Roster) : string list = roster.Seats |> List.map playerName

    /// 坐着 LLM 的那些座位。M1 只会有一个，形状仍按多个写——M2 是四家 LLM 同桌。
    let llmSeats (roster: Roster) : (Seat * LlmSeat) list =
        roster.Seats
        |> Seat.indexed
        |> List.choose (fun (seat, player) ->
            match player with
            | SeatPlayer.Llm config -> Some(seat, config)
            | SeatPlayer.Bot _
            | SeatPlayer.Human
            | SeatPlayer.Baseline -> None)

    /// 坐着强 AI 基线的那些座位（票 92）。**形状是一个表**：四席怎么混都行
    /// （三模型 + 一强 AI、真人 + 强 AI……），而那几 MB 的资产整桌共用一份。
    let baselineSeats (roster: Roster) : Seat list =
        roster.Seats
        |> Seat.indexed
        |> List.choose (fun (seat, player) ->
            match player with
            | SeatPlayer.Baseline -> Some seat
            | SeatPlayer.Bot _
            | SeatPlayer.Llm _
            | SeatPlayer.Human -> None)
