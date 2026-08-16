namespace Janpo.Web.Tests

open Xunit
open Janpo
open Janpo.Web

/// 投影选择与结算显示（票 22）。这里钉的是**上帝视角开关切的是投影，不是渲染纪律**：
/// 关掉时他家只映得到张数，因为源类型（`MaskedSeat`）里根本没有手牌。
module BoardTests =

    let private ruleset = Ruleset.yonma

    /// 四家都是随机选手。**票 23 之后 `Table.advance` 要一份配桌**：谁坐哪个座位不再是
    /// 牌桌自己的事，而 LLM 座位那一半走的是 Elmish 的异步路（`Demand.Asked`），不在这里测。
    let private roster = Roster.allRandom ruleset
    let private seed = 1177

    let private table (seed: int) : Table =
        match Table.start ruleset seed with
        | Ok table -> table
        | Error error -> failwith $"这个种子应当开得了局，却得到「{error}」"

    let private board (viewpoint: Viewpoint) (table: Table) : BoardView =
        match Board.ofTable viewpoint table with
        | Some board -> board
        | None -> failwith "这个视角应当有牌桌"

    let private seat (index: int) : Seat =
        match Seat.ofIndex index with
        | Some seat -> seat
        | None -> failwith $"{index} 应当是合法座位"

    let private toKyokuEnd (budget: int) (start: Table) : Table =
        let rec loop (left: int) (current: Table) =
            match Table.pending current with
            | None -> current
            | Some _ when left <= 0 -> failwith "这一局在预算内没打完"
            | Some _ -> loop (left - 1) (Table.advance roster current)

        loop budget start

    let private handsOf (board: BoardView) : HandView list =
        board.Seats |> List.map (fun view -> view.Hand)

    [<Fact>]
    let ``坐着看：自家亮着，他家只有张数`` () =
        let board = table seed |> board (Viewpoint.Seated(seat 2))

        Assert.Equal<Seat option>(Some(seat 2), board.Viewer)

        for view in board.Seats do
            match view.Hand with
            | HandView.Revealed(hand, _) ->
                Assert.Equal(seat 2, view.Seat)
                Assert.NotEmpty(hand)
            | HandView.Concealed count ->
                Assert.NotEqual<Seat>(seat 2, view.Seat)
                Assert.True(count > 0, "他家的张数是公开信息")

    [<Fact>]
    let ``上帝视角：四家全亮，里宝牌也亮着`` () =
        let board = table seed |> board Viewpoint.God

        Assert.True(board.Viewer |> Option.isNone)
        Assert.NotEmpty(board.UraMarkers)

        for view in board.Seats do
            match view.Hand with
            | HandView.Revealed(hand, _) -> Assert.NotEmpty(hand)
            | HandView.Concealed _ -> failwith "上帝视角不该有扣着的手牌"

    [<Fact>]
    let ``坐着看时里宝牌在投影里根本不存在`` () =
        let board = table seed |> board (Viewpoint.Seated Seat.first)

        Assert.Empty(board.UraMarkers)

    [<Fact>]
    let ``换个座位坐，亮着的那家跟着换`` () =
        let table = table seed

        let revealedIn (viewpoint: Viewpoint) =
            (board viewpoint table).Seats
            |> List.filter (fun view ->
                match view.Hand with
                | HandView.Revealed _ -> true
                | HandView.Concealed _ -> false)
            |> List.map (fun view -> view.Seat)

        Assert.Equal<Seat list>([ Seat.first ], revealedIn (Viewpoint.Seated Seat.first))
        Assert.Equal<Seat list>([ seat 3 ], revealedIn (Viewpoint.Seated(seat 3)))

    [<Fact>]
    let ``一路推到底，坐着看的每一步都只亮自家`` () =
        let rec loop (left: int) (current: Table) =
            let board = board (Viewpoint.Seated Seat.first) current

            let revealed =
                handsOf board
                |> List.filter (fun hand ->
                    match hand with
                    | HandView.Revealed _ -> true
                    | HandView.Concealed _ -> false)

            Assert.Single(revealed) |> ignore

            match Table.pending current with
            | None -> ()
            | Some _ when left <= 0 -> failwith "这一局在预算内没打完"
            | Some _ -> loop (left - 1) (Table.advance roster current)

        loop 400 (table seed)

    [<Fact>]
    let ``座位不在这个规则集里就没有牌桌`` () =
        Assert.True((Board.ofTable (Viewpoint.Seated(seat 9)) (table seed)) |> Option.isNone)

    [<Fact>]
    let ``各家一律按座位升序排，画出来的位置不随视角跳`` () =
        let table = table seed

        let seatsOf (viewpoint: Viewpoint) =
            (board viewpoint table).Seats |> List.map (fun view -> Seat.index view.Seat)

        Assert.Equal<int list>([ 0; 1; 2; 3 ], seatsOf Viewpoint.God)
        Assert.Equal<int list>([ 0; 1; 2; 3 ], seatsOf (Viewpoint.Seated(seat 2)))

    [<Fact>]
    let ``场况两个视角一致：只有暗牌不一样`` () =
        let table = table seed
        let seated = board (Viewpoint.Seated(seat 1)) table
        let god = board Viewpoint.God table

        Assert.Equal(seated.Bakaze, god.Bakaze)
        Assert.Equal(seated.Kyoku, god.Kyoku)
        Assert.Equal(seated.Honba, god.Honba)
        Assert.Equal(seated.Kyotaku, god.Kyotaku)
        Assert.Equal(seated.WallRemaining, god.WallRemaining)
        Assert.Equal<Tile list>(seated.DoraMarkers, god.DoraMarkers)

        Assert.Equal<Kaze list>(
            seated.Seats |> List.map (fun view -> view.Jikaze),
            god.Seats |> List.map (fun view -> view.Jikaze)
        )

    [<Fact>]
    let ``还没终就没有结算`` () =
        Assert.True(table seed |> Board.settlement |> Option.isNone)

    [<Fact>]
    let ``和了的结算给役种、番符、点数授受与连庄`` () =
        let ended = table seed |> toKyokuEnd 400

        match Board.settlement ended with
        | None -> failwith "这一局应当已经终了"
        | Some settlement ->
            match settlement.Outcome with
            | Outcome.Ryuukyoku _ -> failwith "种子 1177 的东 1 局以和了终"
            | Outcome.Hora horas ->
                let hora = List.exactlyOne horas

                Assert.NotEmpty(hora.Yaku)
                Assert.True(hora.Fu > 0)
                Assert.True(hora.Fan > 0)
                Assert.True(hora.HoraPoints > 0)
                Assert.Equal(4, List.length hora.Deltas)
                Assert.Equal<int list>(GameState.scores ended.State, hora.Scores)
                // 连庄与否只有一个判据，结算照抄它。
                Assert.Equal(hora.Actor = settlement.Oya, settlement.Renchan)

    [<Fact>]
    let ``流局的结算给听牌家与授受`` () =
        // 找一个以流局终的种子。随机四家荒牌流局不算罕见，前 40 个种子里必有。
        let ryuukyoku =
            [ 1..40 ]
            |> List.tryPick (fun seed ->
                let ended = table seed |> toKyokuEnd 400

                match Board.settlement ended with
                | Some settlement ->
                    match settlement.Outcome with
                    | Outcome.Ryuukyoku ryuukyoku -> Some(settlement, ryuukyoku)
                    | Outcome.Hora _ -> None
                | None -> None)

        match ryuukyoku with
        | None -> failwith "前 40 个种子里应当有一局流局"
        | Some(settlement, ryuukyoku) ->
            Assert.Equal(4, List.length ryuukyoku.Tenpais)
            Assert.Equal(4, List.length ryuukyoku.Deltas)
            Assert.Equal(0, List.sum ryuukyoku.Deltas)
            Assert.NotEqual<string>("", RyuukyokuReason.toDisplay ryuukyoku.Reason)
            // 连庄照抄 `KyokuEnd.isRenchan`（途中流局一律连庄，荒牌看亲听不听牌），
            // 这里只验它确实被填了，判据本身在引擎侧有自己的用例。
            Assert.Equal(Seat.first, settlement.Oya)
