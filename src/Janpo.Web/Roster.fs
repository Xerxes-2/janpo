namespace Janpo.Web

open Janpo

/// 坐在一个座位上的选手（CONTEXT.md 的 Player）。**四种选手对引擎完全同级**：
/// 给定观测与合法动作集，返回一个动作。差别只在「返回得同步还是要等」——
/// 而那件事在 `Table.decide` 那道缝上分岔，引擎一无所知。
///
/// M1 只有两种。Mortal 与真人坐席是 M3 的 case，加在这里。
[<RequireQualifiedAccess>]
type SeatPlayer =
    /// 引擎自带的随机 bot（`Kyoku.randomPlayer`）。动作当场就有。
    | Random
    /// LLM 适配器：决策包发给 Agent 层，动作由后来的一条 Msg 带回来。
    | Llm of config: LlmSeat

/// 配桌：谁坐哪个座位，按座位升序。
type Roster = { Seats: SeatPlayer list }

/// 配桌的构造与查表。
[<RequireQualifiedAccess>]
module Roster =

    // ---- 构造 ----

    /// 四家都是随机选手（22 号票那一桌）。
    let allRandom (ruleset: Ruleset) : Roster = {
        Seats = Seat.all ruleset |> List.map (fun _ -> SeatPlayer.Random)
    }

    /// 一席交给 LLM，其余随机（23 号票那一桌）。`seat` 是 None 时四家全随机。
    /// 座位越界时也是四家全随机（`Seat.mapAt` 越界不改）。
    let withLlm (ruleset: Ruleset) (seat: Seat option) (config: LlmSeat) : Roster =
        match seat with
        | None -> allRandom ruleset
        | Some seat -> {
            Seats = (allRandom ruleset).Seats |> Seat.mapAt seat (fun _ -> SeatPlayer.Llm config)
          }

    // ---- 查表 ----

    /// 这个座位是谁。**越界按随机算**：牌桌永远推得动，不因为配置出错卡住。
    let playerAt (seat: Seat) (roster: Roster) : SeatPlayer =
        Seat.tryItem seat roster.Seats |> Option.defaultValue SeatPlayer.Random

    /// 各家的名字，按座位升序——mjai `start_game` 的 `names`，也就是牌谱第一条事件里的那一列。
    ///
    /// **它是 wire 数据不是渲染**（ADR-0001）：随机选手叫 `random`，LLM 座位叫 `provider/model`。
    /// **key 不在里面**：牌谱是可分享物，它里头永远不能出现 API key。
    let names (roster: Roster) : string list =
        roster.Seats
        |> List.map (fun player ->
            match player with
            | SeatPlayer.Random -> "random"
            | SeatPlayer.Llm config -> $"{config.Provider}/{config.Model}")

    /// 坐着 LLM 的那些座位。M1 只会有一个，形状仍按多个写——M2 是四家 LLM 同桌。
    let llmSeats (roster: Roster) : (Seat * LlmSeat) list =
        roster.Seats
        |> Seat.indexed
        |> List.choose (fun (seat, player) ->
            match player with
            | SeatPlayer.Llm config -> Some(seat, config)
            | SeatPlayer.Random -> None)
