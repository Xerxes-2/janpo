namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// 牌桌的推进（票 22）。钉两件事：**一次调用只走一手**，以及
/// **牌桌不自己判规则**（局面、连庄、本场与供托一律由引擎给）。
module TableTests =

    let private ruleset = Ruleset.yonma

    /// 四家都是随机选手。**票 23 之后 `Table.advance` 要一份配桌**：谁坐哪个座位不再是
    /// 牌桌自己的事，而 LLM 座位那一半走的是 Elmish 的异步路（`Demand.Asked`），不在这里测。
    let private roster = Roster.allRandom ruleset

    /// 19 票挑的那个种子：单局以荣和终，整场打满 6 局（两次连庄）。
    let private seed = 1177

    let private table (seed: int) : Table =
        match Table.start ruleset seed with
        | Ok table -> table
        | Error error -> failwith $"这个种子应当开得了局，却得到「{error}」"

    /// 推进到这一局终，最多 `budget` 手。跑不完就让用例自己破，不要静静地过。
    let private toKyokuEnd (budget: int) (start: Table) : Table =
        let rec loop (left: int) (current: Table) =
            match Table.pending current with
            | None -> current
            | Some _ when left <= 0 -> failwith "这一局在预算内没打完"
            | Some _ -> loop (left - 1) (Table.advance roster current)

        loop budget start

    [<Fact>]
    let ``开局就摆好了一局：亲摸了牌，等它出手`` () =
        let table = table seed

        Assert.True((Table.pending table).IsSome)
        Assert.False(Table.isKyokuEnded table)
        Assert.True(Table.result table |> Option.isNone)
        Assert.True(table.Latest |> Option.isNone)
        Assert.Equal(0, List.length table.Readings)

    [<Fact>]
    let ``推进一手就只走一手：事件流恰好长出这一手产的那几条`` () =
        let before = table seed
        let after = Table.advance roster before

        let grew =
            List.length (GameState.events after.State)
            - List.length (GameState.events before.State)

        Assert.True(grew >= 1, "一手至少产一条事件")
        Assert.True(after.Latest |> Option.isSome)
        // 打出去的那一手：局面必然变了（同一个 GameState 说明压根没推进）。
        Assert.NotEqual<Event list>(GameState.events before.State, GameState.events after.State)

    [<Fact>]
    let ``同一种子的两张牌桌逐手相同`` () =
        let left = table seed |> toKyokuEnd 400
        let right = table seed |> toKyokuEnd 400

        Assert.Equal<Event list>(GameState.events left.State, GameState.events right.State)
        Assert.Equal<int list>(GameState.scores left.State, GameState.scores right.State)

    [<Fact>]
    let ``牌桌推出来的一局与引擎自己跑的那一局逐条相同`` () =
        let played = table seed |> toKyokuEnd 400

        let expected =
            match Rng.ofSeed seed |> Kyoku.runRandom ruleset (KyokuContext.initial ruleset) with
            | Ok(state, _) -> GameState.events state
            | Error error -> failwith $"引擎应当跑得完这一局，却得到「{KyokuError.toDisplay error}」"

        Assert.Equal<Event list>(expected, GameState.events played.State)

    [<Fact>]
    let ``一局打完就停在那里：不自己开下一局`` () =
        let ended = table seed |> toKyokuEnd 400

        Assert.True(Table.isKyokuEnded ended)
        Assert.True((Table.pending ended).IsNone)
        // 再推也不动。
        Assert.Equal<Event list>(GameState.events ended.State, GameState.events (Table.advance roster ended).State)

    [<Fact>]
    let ``和了那一手把役种捞了下来——结算显示只有这一个来源`` () =
        let ended = table seed |> toKyokuEnd 400

        match GameState.kyokuEnd ended.State with
        | Some(KyokuEnd.Hora horas) ->
            Assert.Equal(List.length horas, List.length ended.Readings)

            for hora in horas do
                let reading = ended.Readings |> List.tryFind (fun (actor, _) -> actor = hora.Actor)
                Assert.True(reading.IsSome, "每条和了都要有对应的读法")
                // 一局终了之后再问引擎就问不到了：阶段已经是 Ended。
                Assert.True((GameState.horaOf hora.Actor ended.State) |> Result.isError)
        | Some(KyokuEnd.Ryuukyoku _)
        | None -> failwith "种子 1177 的东 1 局以和了终"

    [<Fact>]
    let ``下一局由引擎定场况：连庄看 KyokuEnd，本场与供托由 Game 结转`` () =
        let ended = table seed |> toKyokuEnd 400
        let next = Table.nextKyoku ended

        let before = GameState.context ended.State
        let after = GameState.context next.State

        let renchan =
            match GameState.kyokuEnd ended.State with
            | Some kyokuEnd -> KyokuEnd.isRenchan before.Oya kyokuEnd
            | None -> failwith "这一局应当已经终了"

        if renchan then
            Assert.Equal(before.Kyoku, after.Kyoku)
            Assert.Equal(before.Honba + 1, after.Honba)
        else
            Assert.Equal(before.Kyoku + 1, after.Kyoku)

        Assert.Equal<int list>(GameState.scores ended.State, after.Scores)
        Assert.True(next.Latest |> Option.isNone)
        Assert.Empty(next.Readings)

    [<Fact>]
    let ``这一局还没终就不许开下一局`` () =
        let table = table seed

        Assert.Equal<Event list>(GameState.events table.State, GameState.events (Table.nextKyoku table).State)

    [<Fact>]
    let ``整场打完：局数序列走完之后有终局精算，没有下一局`` () =
        let rec loop (left: int) (current: Table) =
            match Table.result current with
            | Some result -> result
            | None when left <= 0 -> failwith "这一场在预算内没打完"
            | None -> loop (left - 1) (current |> toKyokuEnd 400 |> Table.nextKyoku)

        let result = loop 40 (table seed)

        Assert.Equal(Ruleset.startingTotal ruleset, List.sum result.Scores)
        Assert.Equal<int list>([ 1; 2; 3; 4 ], List.sort result.Juni)

    [<Fact>]
    let ``非法动作是值不是异常：牌桌停住并把话说给人看`` () =
        let table = table seed
        // 摸牌后阶段没有可响应的牌，「过」无从谈起，这条必然被拒。
        let faulted = Table.apply (Action.None Seat.first) table

        Assert.True(faulted.Fault.IsSome)
        Assert.True((Table.pending faulted).IsNone)
        // 停住之后不再推进。
        Assert.Equal<Event list>(GameState.events faulted.State, GameState.events (Table.advance roster faulted).State)

    [<Fact>]
    let ``开不了局是 Error，不是抛异常`` () =
        // 配牌每家 100 张：牌山不够发，`GameState.start` 必然拒掉。
        Assert.True(Table.start { ruleset with HaipaiSize = 100 } 1 |> Result.isError)
