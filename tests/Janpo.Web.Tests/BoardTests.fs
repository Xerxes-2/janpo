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

    // ---- 副露的画法（票 38） ----

    let private tile (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error _ -> failwith $"「{notation}」应当是合法的牌"

    let private tiles (notations: string) : Tile list =
        notations.Split(' ') |> List.ofArray |> List.map tile

    let private nakiOf (built: Result<Naki, NakiError>) : Naki =
        match built with
        | Ok naki -> naki
        | Error error -> failwith $"这一组副露应当构造得起来，却得到「{NakiError.toDisplay error}」"

    /// 副露方一律取座位 0：相对位置的参照系是**副露方**，验的就是这个。
    let private nakiView (naki: Naki) : NakiView = Board.nakiView ruleset Seat.first naki

    let private faces (view: NakiView) : Tile option list =
        view.Tiles |> List.map (fun each -> each.Pai)

    let private fromOther (view: NakiView) : bool list =
        view.Tiles |> List.map (fun each -> each.FromOther)

    [<Fact>]
    let ``吃：被鸣的那张就地横放，三张仍按升序摆`` () =
        // 吃只吃上家（座位 0 的上家是座位 3），两种吃法的三张牌一模一样——
        // 「哪一张是鸣来的」是它们唯一的区别，票 38 之前牌桌上看不出来。
        let middle = Naki.chi (seat 3) (tile "3p") (tiles "2p 4p") |> nakiOf |> nakiView
        let lowest = Naki.chi (seat 3) (tile "2p") (tiles "3p 4p") |> nakiOf |> nakiView

        Assert.Equal<Tile option list>(tiles "2p 3p 4p" |> List.map Some, faces middle)
        Assert.Equal<Tile option list>(faces middle, faces lowest)

        Assert.Equal<bool list>([ false; true; false ], fromOther middle)
        Assert.Equal<bool list>([ true; false; false ], fromOther lowest)

        // 上家。吃恒来自上家——参照系一旦换成观测者，这条就会漂。
        Assert.Equal<int option>(Some 3, middle.Relative)
        Assert.Equal<Seat option>(Some(seat 3), middle.Target)
        Assert.False(middle.Tiles |> List.exists (fun each -> each.Added))

    [<Fact>]
    let ``碰：来源是被鸣那家相对副露方的第几家`` () =
        let from (target: int) =
            Naki.pon (seat target) (tile "5z") (tiles "5z 5z") |> nakiOf |> nakiView

        // 1 下家、2 对家、3 上家（`MaskedSeat.Relative` 同一套编号）。
        Assert.Equal<int option>(Some 1, (from 1).Relative)
        Assert.Equal<int option>(Some 2, (from 2).Relative)
        Assert.Equal<int option>(Some 3, (from 3).Relative)

        let view = from 1
        Assert.Equal(3, List.length view.Tiles)
        Assert.Equal(1, fromOther view |> List.filter id |> List.length)
        Assert.Equal<Tile option list>(tiles "5z 5z 5z" |> List.map Some, faces view)

    [<Fact>]
    let ``暗杠：没有来源，两端扣着，一张横放的也没有`` () =
        let view = Naki.ankan (tiles "1z 1z 1z 1z") |> nakiOf |> nakiView

        Assert.Equal<Tile option list>([ None; Some(tile "1z"); Some(tile "1z"); None ], faces view)
        Assert.Equal<bool list>([ false; false; false; false ], fromOther view)
        Assert.Equal<Seat option>(None, view.Target)
        Assert.Equal<int option>(None, view.Relative)

    [<Fact>]
    let ``大明杠：四张里横放一张，来源在`` () =
        let view = Naki.minkan (seat 2) (tile "7s") (tiles "7s 7s 7s") |> nakiOf |> nakiView

        Assert.Equal(4, List.length view.Tiles)
        Assert.True(view.Tiles |> List.forall (fun each -> Option.isSome each.Pai), "大明杠一张也不扣着")
        Assert.Equal(1, fromOther view |> List.filter id |> List.length)
        Assert.Equal<int option>(Some 2, view.Relative)

    [<Fact>]
    let ``加杠：横放的是当初碰来的那张，加上去的那张另有记号`` () =
        // 底那副碰来自座位 2（对家），亮出的两张里有一张赤 5——排序会把它挪到别处去，
        // 所以加杠这一组**照 `Naki.consumed` 的原序**摆：第一张恒是当初被碰的那张。
        let pon = Naki.pon (seat 2) (tile "5p") (tiles "5pr 5p") |> nakiOf
        let view = Naki.kakan (tile "5p") pon |> nakiOf |> nakiView

        Assert.Equal<Tile option list>(tiles "5p 5pr 5p 5p" |> List.map Some, faces view)
        Assert.Equal<bool list>([ true; false; false; false ], fromOther view)
        Assert.Equal<bool list>([ false; false; false; true ], view.Tiles |> List.map (fun each -> each.Added))

        // 来源是**当初碰的那家**，不是加杠这一手的谁。
        Assert.Equal<Seat option>(Some(seat 2), view.Target)
        Assert.Equal<int option>(Some 2, view.Relative)

    [<Fact>]
    let ``一组副露里至多一张横放、至多一张是加上去的`` () =
        let all =
            [
                Naki.chi (seat 3) (tile "3p") (tiles "2p 4p") |> nakiOf
                Naki.pon (seat 1) (tile "5z") (tiles "5z 5z") |> nakiOf
                Naki.ankan (tiles "1z 1z 1z 1z") |> nakiOf
                Naki.minkan (seat 2) (tile "7s") (tiles "7s 7s 7s") |> nakiOf
                Naki.kakan (tile "5p") (Naki.pon (seat 2) (tile "5p") (tiles "5p 5p") |> nakiOf)
                |> nakiOf
            ]

        for naki in all do
            let view = nakiView naki

            let count (pick: NakiTileView -> bool) =
                view.Tiles |> List.filter pick |> List.length

            Assert.True(count (fun each -> each.FromOther) <= 1, "横放的至多一张")
            Assert.True(count (fun each -> each.Added) <= 1, "加上去的至多一张")
            // 暗杠之外，从他家那儿来的那一张必然画得出来。
            Assert.Equal((if Naki.isConcealed naki then 0 else 1), count (fun each -> each.FromOther))
            Assert.Equal(Naki.tiles naki |> List.length, List.length view.Tiles)
