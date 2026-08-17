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

    /// 打完一整场：一局打到终就开下一局，直到局数序列走完（`Table.result` 有值）。
    let private toGameEnd (budget: int) (start: Table) : Table =
        let rec loop (left: int) (current: Table) =
            match Table.result current with
            | Some _ -> current
            | None when left <= 0 -> failwith "这一场在预算内没打完"
            | None -> current |> toKyokuEnd 400 |> Table.nextKyoku |> loop (left - 1)

        loop budget start

    let private resultOf (table: Table) : GameResult =
        match Table.result table with
        | Some result -> result
        | None -> failwith "这一场应当已经终局"

    let private settlementOf (table: Table) : Settlement =
        match Board.settlement table with
        | Some settlement -> settlement
        | None -> failwith "这一局应当已经终了"

    let private scoresOn (board: BoardView) : int list =
        board.Seats |> List.map (fun view -> view.Score)

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

    /// **列表的顺序**恒按座位升序：`seat-N` 这些钩子与 DOM 的先后不随视角跳。
    /// 画出来的**方位**是另一回事——它跟着观测视角转（见下面那几条与 `Board.position`）。
    [<Fact>]
    let ``各家一律按座位升序排，DOM 的顺序不随视角跳`` () =
        let table = table seed

        let seatsOf (viewpoint: Viewpoint) =
            (board viewpoint table).Seats |> List.map (fun view -> Seat.index view.Seat)

        Assert.Equal<int list>([ 0; 1; 2; 3 ], seatsOf Viewpoint.God)
        Assert.Equal<int list>([ 0; 1; 2; 3 ], seatsOf (Viewpoint.Seated(seat 2)))

    // ---- 方位（票 44） ----

    /// 这张牌桌上四家各在哪个方位，按座位升序。
    let private positionsOn (board: BoardView) : Position list =
        board.Seats
        |> List.map (fun view -> Board.position ruleset (Board.anchor board) view.Seat)

    [<Fact>]
    let ``坐着看：自家在下、下家在右、对家在上、上家在左`` () =
        let board = table seed |> board (Viewpoint.Seated(seat 2))

        // 参照系是观测者（座位 2）：它自己是自家，座位 3 是它的下家，座位 0 是对家，座位 1 是上家。
        Assert.Equal<Position list>(
            [ Position.Toimen; Position.Kamicha; Position.Self; Position.Shimocha ],
            positionsOn board
        )

    [<Fact>]
    let ``换个座位坐，四家的方位跟着转`` () =
        let table = table seed

        let positions (viewpoint: Viewpoint) = board viewpoint table |> positionsOn

        // 「切了视角但布局没转」正是这一条要抓的错：四家没有一家留在原来的方位上。
        for left, right in List.zip (positions (Viewpoint.Seated Seat.first)) (positions (Viewpoint.Seated(seat 1))) do
            Assert.NotEqual<Position>(left, right)

        // 而每个方位恒有且只有一家（不管坐哪儿）：四家四个不同的方位。
        for viewpoint in [ Viewpoint.Seated Seat.first; Viewpoint.Seated(seat 1); Viewpoint.God ] do
            Assert.Equal(4, positions viewpoint |> List.distinct |> List.length)

    [<Fact>]
    let ``上帝视角的参照系是起家：座位 0 在下`` () =
        let table = table seed

        // 上帝视角没有观测者，方位得有个说得出口的参照系——取起家（座位 0）。
        Assert.Equal(Seat.first, Board.anchor (board Viewpoint.God table))

        Assert.Equal<Position list>(
            positionsOn (board (Viewpoint.Seated Seat.first) table),
            positionsOn (board Viewpoint.God table)
        )

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

    /// 票 39 的病根：终局那一屏上「点数」有两个说法——座位卡读最后一局的 `GameState`，
    /// 终局精算读 `Game`（精算后，供托已归头名）。**终局的权威是 `Game`**，
    /// 因此两处必须是同一份数。
    ///
    /// 种子 447 的那一场终局时场上还剩 1 根供托（27 号验收用的就是它），
    /// 于是「最后一局的局末点数」与「精算后的点数」真的不同——不同才验得出口径。
    [<Fact>]
    let ``终局那一屏：座位卡与终局精算是同一份点数`` () =
        let ended = table 447 |> toGameEnd 12
        let result = resultOf ended

        Assert.NotEqual<int list>(GameState.scores ended.State, result.Scores)

        for viewpoint in [ Viewpoint.God; Viewpoint.Seated Seat.first ] do
            Assert.Equal<int list>(result.Scores, board viewpoint ended |> scoresOn)

    [<Fact>]
    let ``终局之后桌上不再剩供托：它已经归了头名`` () =
        let ended = table 447 |> toGameEnd 12

        // 前提：这一场终局时最后一局手上确实还剩着供托（否则这条用例什么也没验）。
        Assert.True(GameState.kyotaku ended.State > 0, "种子 447 的终局那一局该剩着供托")

        for viewpoint in [ Viewpoint.God; Viewpoint.Seated Seat.first ] do
            Assert.Equal(0, (board viewpoint ended).Kyotaku)

    [<Fact>]
    let ``还没终局时点数与供托照旧读这一局的局面`` () =
        let playing = table 447 |> toKyokuEnd 400

        for viewpoint in [ Viewpoint.God; Viewpoint.Seated Seat.first ] do
            let board = board viewpoint playing
            Assert.Equal<int list>(GameState.scores playing.State, scoresOn board)
            Assert.Equal(GameState.kyotaku playing.State, board.Kyotaku)

    [<Fact>]
    let ``最后一局的结算说的是终局，不是进下一局`` () =
        let ended = table 447 |> toGameEnd 12

        Assert.True((settlementOf ended).Ended, "局数序列走完了，这一局之后没有下一局")

    [<Fact>]
    let ``不是最后一局的结算仍然有下一局`` () =
        let first = table 447 |> toKyokuEnd 400

        Assert.False((settlementOf first).Ended, "东 1 局之后还有下一局")

    [<Fact>]
    let ``终局精算那一屏说得出供托归了谁`` () =
        let ended = table 447 |> toGameEnd 12

        match Board.final ended with
        | None -> failwith "这一场应当已经终局"
        | Some final ->
            Assert.Equal<int list>((resultOf ended).Scores, final.Result.Scores)
            Assert.Equal(GameState.kyotaku ended.State, final.Kyotaku)
            // 头名多出来的正是那几根：供托在精算里只换归属，不凭空增减。
            let carried = List.sum final.Result.Scores - List.sum (GameState.scores ended.State)
            Assert.Equal(final.KyotakuScore, carried)

    [<Fact>]
    let ``还没终局就没有终局精算那一屏`` () =
        Assert.True(table 447 |> toKyokuEnd 400 |> Board.final |> Option.isNone)

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

    // ---- 副露的画法（票 38，位置编码来源是票 51） ----

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

    /// 逐张摊平（从左往右，同一格里从下往上）。
    let private flat (view: NakiView) : NakiTileView list = view.Slots |> List.concat

    let private faces (view: NakiView) : Tile option list =
        flat view |> List.map (fun each -> each.Pai)

    let private fromOther (view: NakiView) : bool list =
        flat view |> List.map (fun each -> each.FromOther)

    /// 横放那张落在第几格（从副露方自己的左边数起）。**这就是票 51 说的「位置即来源」**。
    let private takenSlot (view: NakiView) : int option =
        view.Slots |> List.tryFindIndex (List.exists (fun each -> each.FromOther))

    [<Fact>]
    let ``吃：横放那张恒在最左，另两张在它右边按升序`` () =
        // 吃只吃上家（座位 0 的上家是座位 3），两种吃法的三张牌一模一样——
        // 「哪一张是鸣来的」是它们唯一的区别。位置编码之后**整组不再是数字顺序**（票 51）：
        // 鸣来的那张搬到最左，剩下两张在它右边按升序。
        let middle = Naki.chi (seat 3) (tile "3p") (tiles "2p 4p") |> nakiOf |> nakiView
        let lowest = Naki.chi (seat 3) (tile "2p") (tiles "3p 4p") |> nakiOf |> nakiView

        Assert.Equal<Tile option list>(tiles "3p 2p 4p" |> List.map Some, faces middle)
        Assert.Equal<Tile option list>(tiles "2p 3p 4p" |> List.map Some, faces lowest)

        // 两种吃仍然分得开：牌面顺序不同，而横放那张恒在最左（吃只来得了上家）。
        Assert.NotEqual<Tile option list>(faces middle, faces lowest)
        Assert.Equal<bool list>([ true; false; false ], fromOther middle)
        Assert.Equal<bool list>([ true; false; false ], fromOther lowest)
        Assert.Equal<int option>(Some 0, takenSlot middle)
        Assert.Equal<int option>(Some 0, takenSlot lowest)

        // 上家。吃恒来自上家——参照系一旦换成观测者，这条就会漂。
        Assert.Equal<int option>(Some 3, middle.Relative)
        Assert.Equal<Seat option>(Some(seat 3), middle.Target)
        Assert.False(flat middle |> List.exists (fun each -> each.Added))

    [<Fact>]
    let ``碰：横放那张的位置就是来源——上家最左、对家中间、下家最右`` () =
        let from (target: int) =
            Naki.pon (seat target) (tile "5z") (tiles "5z 5z") |> nakiOf |> nakiView

        // 1 下家、2 对家、3 上家（`MaskedSeat.Relative` 同一套编号）。
        Assert.Equal<int option>(Some 1, (from 1).Relative)
        Assert.Equal<int option>(Some 2, (from 2).Relative)
        Assert.Equal<int option>(Some 3, (from 3).Relative)

        // **位置本身就是那句「来自X」**（M 联盟公式规则第 6 条第 2 款）。
        Assert.Equal<int option>(Some 2, takenSlot (from 1))
        Assert.Equal<int option>(Some 1, takenSlot (from 2))
        Assert.Equal<int option>(Some 0, takenSlot (from 3))

        let view = from 1
        Assert.Equal(3, List.length view.Slots)
        Assert.Equal(1, fromOther view |> List.filter id |> List.length)
        Assert.Equal<Tile option list>(tiles "5z 5z 5z" |> List.map Some, faces view)

    [<Fact>]
    let ``暗杠：没有来源，两端扣着，一张横放的也没有`` () =
        let view = Naki.ankan (tiles "1z 1z 1z 1z") |> nakiOf |> nakiView

        Assert.Equal(4, List.length view.Slots)
        Assert.Equal<Tile option list>([ None; Some(tile "1z"); Some(tile "1z"); None ], faces view)
        Assert.Equal<bool list>([ false; false; false; false ], fromOther view)
        Assert.Equal<Seat option>(None, view.Target)
        Assert.Equal<int option>(None, view.Relative)
        Assert.Equal<int option>(None, takenSlot view)

    [<Fact>]
    let ``大明杠：上家最左、对家左起第二、下家最右`` () =
        let from (target: int) =
            Naki.minkan (seat target) (tile "7s") (tiles "7s 7s 7s") |> nakiOf |> nakiView

        // 四张时「中间」落哪一格有明文：M 联盟公式规则第 6 条第 3 款
        // 「明槓子（大明槓によるもの）… 上家からは左、対面から左2番目、下家からは右に並べる」。
        Assert.Equal<int option>(Some 0, takenSlot (from 3))
        Assert.Equal<int option>(Some 1, takenSlot (from 2))
        Assert.Equal<int option>(Some 3, takenSlot (from 1))

        let view = from 2
        Assert.Equal(4, List.length view.Slots)
        Assert.True(flat view |> List.forall (fun each -> Option.isSome each.Pai), "大明杠一张也不扣着")
        Assert.Equal(1, fromOther view |> List.filter id |> List.length)
        Assert.Equal<int option>(Some 2, view.Relative)

    [<Fact>]
    let ``加杠：加上去那张叠在当初碰来的那张上，来源那一格不动`` () =
        // 底那副碰来自座位 2（对家），亮出的两张里有一张赤 5。
        let pon = Naki.pon (seat 2) (tile "5p") (tiles "5pr 5p") |> nakiOf
        let view = Naki.kakan (tile "5p") pon |> nakiOf |> nakiView

        // 三格（底那副碰），来源那一格摞着两张：下面是当初碰来的，上面是后加的。
        // M 联盟公式规则第 6 条第 4 款：「加槓子 加槓牌を指示牌の上に並べて重ねる」——
        // 横放那张不挪位，因此「当初是谁打的」加杠之后仍读得出来。
        Assert.Equal(3, List.length view.Slots)
        Assert.Equal<int option>(Some 1, takenSlot view)
        Assert.Equal<int list>([ 1; 2; 1 ], view.Slots |> List.map List.length)

        let stacked = view.Slots |> List.item 1
        Assert.Equal<bool list>([ true; false ], stacked |> List.map (fun each -> each.FromOther))
        Assert.Equal<bool list>([ false; true ], stacked |> List.map (fun each -> each.Added))
        Assert.Equal<Tile option list>([ Some(tile "5p"); Some(tile "5p") ], stacked |> List.map (fun each -> each.Pai))

        // 来源是**当初碰的那家**，不是加杠这一手的谁。
        Assert.Equal<Seat option>(Some(seat 2), view.Target)
        Assert.Equal<int option>(Some 2, view.Relative)

    [<Fact>]
    let ``位置的参照系是副露方自己：换个副露方，同一个来源就落到别的格`` () =
        // 同一家（座位 1）打出的那张，被不同的人碰走：它相对座位 0 是下家（最右）、
        // 相对座位 2 是上家（最左）、相对座位 3 是对家（中间）。
        // 参照系一旦漂到观测者或屏幕左右，这三条就会一起变成同一格。
        let by (owner: int) =
            Naki.pon (seat 1) (tile "5z") (tiles "5z 5z")
            |> nakiOf
            |> Board.nakiView ruleset (seat owner)
            |> takenSlot

        Assert.Equal<int option>(Some 2, by 0)
        Assert.Equal<int option>(Some 0, by 2)
        Assert.Equal<int option>(Some 1, by 3)

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
                flat view |> List.filter pick |> List.length

            Assert.True(count (fun each -> each.FromOther) <= 1, "横放的至多一张")
            Assert.True(count (fun each -> each.Added) <= 1, "加上去的至多一张")
            // 暗杠之外，从他家那儿来的那一张必然画得出来。
            Assert.Equal((if Naki.isConcealed naki then 0 else 1), count (fun each -> each.FromOther))
            Assert.Equal(Naki.tiles naki |> List.length, List.length (flat view))
            // 摞起来的只有加杠那一格：别的种类一格一张，因此「第几格」与「第几张」重合。
            let expected =
                if Naki.kind naki = NakiKind.Kakan then
                    3
                else
                    List.length (flat view)

            Assert.Equal(expected, List.length view.Slots)
