namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// 一份牌谱摆成**逐帧的牌桌**（票 71）。这是首页 Demo 回放的地基，
/// 而它的判据只有一条：**回放这一侧不许有第二份实现**。
///
/// 因此这几条用例钉的都是「与 Live 那条路给出同一个东西」：
/// 逐帧的 `Table` 是 `Table.apply` 一手一手落出来的，役种在宣言那一刻捞下来、
/// 掩蔽流跟着引擎吐的事件长——三样都不是回放自己再判一遍。
module ReplayTableTests =

    let private ruleset = Ruleset.yonma

    let private roster = Roster.allRandom ruleset

    /// 种子 1177：单局以荣和终、整场打满 6 局（`TableTests` 认的也是它）。
    let private seed = 1177

    /// Live 那一侧：四家随机把整场打完，再导出牌谱。
    let private hostPaifu (seed: int) : Paifu * Table =
        let rec toEnd (left: int) (current: Table) =
            match Table.pending current with
            | None -> current
            | Some _ when left <= 0 -> failwith "这一局在预算内没打完"
            | Some _ -> toEnd (left - 1) (Table.advance roster current)

        let rec whole (left: int) (current: Table) =
            match Table.result current with
            | Some _ -> current
            | None when left <= 0 -> failwith "这一场在预算内没打完"
            | None -> whole (left - 1) (current |> toEnd 400 |> Table.nextKyoku)

        let table =
            match Table.start ruleset seed with
            | Ok table -> whole 40 table
            | Error error -> failwith $"这个种子应当开得了局，却得到「{error}」"

        Table.paifu roster table, table

    let private frames (paifu: Paifu) : Table list =
        match Table.replay paifu with
        | Ok frames -> frames
        | Error error -> failwith $"这份牌谱应当摆得出牌桌，却得到「{error}」"

    let private lastOf (frames: Table list) : Table =
        match List.tryLast frames with
        | Some table -> table
        | None -> failwith "逐帧的牌桌不该是空的"

    [<Fact>]
    let ``逐帧摆出来的牌桌，末帧与打出这份牌谱的那一桌逐条相同`` () =
        let paifu, played = hostPaifu seed
        let last = lastOf (frames paifu)

        Assert.Equal<Event list>(Table.events roster played, Table.events roster last)
        Assert.Equal(Table.result played, Table.result last)
        Assert.True(Option.isNone last.Fault)

    [<Fact>]
    let ``一帧就是一手：帧数与那一场落定的手数对得上`` () =
        let paifu, played = hostPaifu seed
        let frames = frames paifu

        // 帧数 = 落定的手数 + 局数（每一局多一帧开局，第 0 帧就是头一局的开局）。
        Assert.Equal(played.Turns, (lastOf frames).Turns)
        Assert.Equal(0, (List.head frames).Turns)
        Assert.Equal(played.Turns + List.length (Game.played played.Game), List.length frames)

        // 手数**单调不减**（开局那几帧持平，其余每帧 +1）：牌桌真的在往前走。
        frames
        |> List.pairwise
        |> List.iter (fun (before, after) ->
            Assert.True(after.Turns - before.Turns >= 0, "回放不许倒着走")
            Assert.True(after.Turns - before.Turns <= 1, "一帧最多走一手"))

    [<Fact>]
    let ``和了那一帧的读法在：结算面板的役与符番只有这一个来源`` () =
        let paifu, _ = hostPaifu seed

        // 这一场里以和了终的那几局：每一局收尾那一帧都要有读法，且与和了条数一样多。
        let settled =
            frames paifu
            |> List.choose (fun table ->
                match GameState.kyokuEnd table.State with
                | Some(KyokuEnd.Hora horas) -> Some(horas, table)
                | Some(KyokuEnd.Ryuukyoku _)
                | None -> None)

        Assert.NotEmpty settled

        for horas, table in settled do
            Assert.Equal(List.length horas, List.length table.Readings)

            for hora in horas do
                // 一局终了之后再问引擎就问不到了（阶段已是 Ended）——它只在宣言那一刻答得出来。
                Assert.True((GameState.horaOf hora.Actor table.State) |> Result.isError)
                Assert.True(table.Readings |> List.exists (fun (actor, _) -> actor = hora.Actor))

            // 结算显示因此拿得到役种：这是页面上那一屏的判据。
            match Board.settlement table with
            | Some settlement ->
                match settlement.Outcome with
                | Outcome.Hora views -> Assert.All(views, fun view -> Assert.NotEmpty view.Yaku)
                | Outcome.Ryuukyoku _ -> failwith "这一局以和了终"
            | None -> failwith "这一帧该有结算"

    [<Fact>]
    let ``逐帧的掩蔽流与重头 fold 一致：座位视角切得动`` () =
        let paifu, _ = hostPaifu seed

        for table in frames paifu do
            for seat in Seat.all ruleset do
                Assert.Equal(Observation.ofState seat table.State, Table.observation seat table)
                // 座位视角要的那份投影因此摆得出来。
                Assert.True(Board.ofTable (Viewpoint.Seated seat) table |> Option.isSome)

    [<Fact>]
    let ``牌谱里的决策记录按手序落到帧上：那一手之后才看得见`` () =
        // Demo 是 bot 牌谱（一条记录都没有），因此这一条自带阳性对照：先拌一条进去。
        let paifu, _ = hostPaifu seed

        let record: DecisionRecord =
            {
                Turn = 3
                Seat = Seat.first
                PromptTail = "【现在】……"
                RenderVersion = "janpo-default@aaaaaaaa.bbbbbbbb"
                ActionIds = [ 0 ]
                Output = "{}"
                Reason = Some "就它了"
                Thinking = None
                Attempts = 1
                LatencyMs = 640
                Applied = Some 0
                Fallback = None
                Usage = None
            }

        let withRecord = { paifu with Decisions = [ record ] }
        let frames = frames withRecord

        Assert.Equal<DecisionRecord list>([], (List.item 3 frames).Decisions)
        Assert.Equal<DecisionRecord list>([ record ], (List.item 4 frames).Decisions)

    [<Fact>]
    let ``一局都没有的牌谱摆不出牌桌，且是值不是异常`` () =
        let empty =
            Paifu.create ruleset [ StartGame [ "p0"; "p1"; "p2"; "p3" ] ] [] Prompting.empty

        Assert.True(Table.replay empty |> Result.isError)
