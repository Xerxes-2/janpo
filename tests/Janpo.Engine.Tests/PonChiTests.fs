namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 鸣牌路径的前半：碰与吃。谁能鸣、鸣完谁出手、禁食替、河被鸣走的记号与 Junme。
/// 杠本身在 KanTests；这里只在合法动作集的断言里带上它（手里三张同种时大明杠与碰并列）。
///
/// 牌山是**摊出来的**（`startScripted`），因此「谁在第几巡能碰什么」是确定的事实。
/// 不变量（牌数守恒、吃只吃上家、河底牌鸣不得）见 GameStateProperties。
module PonChiTests =

    let private pai (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"{notation} 应当是合法记法，却得到 {error}"

    let private stepped (state: GameState) (action: Action) =
        match GameState.step state action with
        | Ok(next, events) -> next, events
        | Error illegal -> failwith $"这个动作应当合法，却被拒：{IllegalAction.toDisplay illegal}"

    let private rejected (state: GameState) (action: Action) =
        match GameState.step state action with
        | Error illegal -> illegal
        | Ok _ -> failwith "这个动作应当被拒，却被接受了"

    /// 现在在等哪些座位。
    let private waiting (state: GameState) : Seat list =
        GameState.legalActions state |> List.map (fun choice -> choice.Seat)

    let private actionsOf (seat: Seat) (state: GameState) : Action list =
        GameState.legalActions state
        |> List.tryFind (fun choice -> choice.Seat = seat)
        |> Option.map (fun choice -> choice.Actions)
        |> Option.defaultValue []

    /// 某座位此刻能打出的牌，按 mjai 记法。
    let private dahaiOf (seat: Seat) (state: GameState) : string list =
        actionsOf seat state
        |> List.choose (fun action ->
            match action with
            | Action.Dahai(_, pai, _) -> Some(Tile.toMjai pai)
            | Action.Hora _
            | Action.Pon _
            | Action.Chi _
            | Action.Riichi _
            | Action.Ankan _
            | Action.Kakan _
            | Action.Minkan _
            | Action.Ryuukyoku _
            | Action.None _ -> None)

    let private handOf (seat: Seat) (state: GameState) : Tile list =
        GameState.player seat state
        |> Option.map PlayerState.hand
        |> Option.defaultValue []

    let private kawaTakenOf (state: GameState) : bool list =
        GameState.players state |> List.map PlayerState.kawaTaken

    let private junmes (state: GameState) : int list =
        Seat.all ruleset |> List.map (fun seat -> GameState.junme seat state)

    /// 鸣牌的剧本：四家各摸一张摸切，随后座位 0 第 2 巡摸进 3s 打出去。
    ///
    /// - 座位 1（座位 0 的下家）手里三张 3s 与 4s 5s 6s：**碰与吃都成立**，
    ///   而且鸣完手里还留着 3s（现物食替）与 6s（筋食替），禁手看得见；
    /// - 座位 3 手里也有 4s 5s，但它是座位 0 的上家：**吃只吃上家**，因此它不被问；
    /// - 座位 1 打出 1z 之后，座位 3（座位 1 的对家）碰它：**碰是任意他家**，
    ///   连着两次鸣牌，中间座位 2 一张牌都没摸到。
    let private nakiScript =
        {
            Hands =
                [
                    "1m 4m 7m 1p 4p 7p 2s 8s 2z 3z 4z 5z 5z"
                    "3s 3s 3s 4s 5s 6s 1m 2m 3m 9p 9p 1z 1z"
                    "2m 5m 8m 2p 5p 8p 7s 9s 3z 3z 4z 4z 6z"
                    "4s 5s 3m 6m 9m 3p 6p 9p 1s 2s 1z 1z 6z"
                ]
            Draws = "9m 1s 2s 7s 3s"
        }

    /// 撞车的剧本：座位 0 第 1 巡摸进 5s 就打出去，三家同时想要它。
    ///
    /// - 座位 1（下家）听 5s / 8s（一气通贯 + 平和），**既能荣和也能吃**；
    /// - 座位 2 手里 5s 5s 5sr，**能碰，且亮正牌还是亮红 5 是两条不同的动作**（红 5 计番）；
    /// - 优先级 Ron > Pon > Chi 的两条边都在这一局里撞得到。
    let private nakiRaceScript =
        {
            Hands =
                [
                    "1m 4m 7m 1p 4p 7p 2s 8s 2z 3z 4z 5z 5z"
                    "1m 2m 3m 4m 5m 6m 7m 8m 9m 9p 9p 6s 7s"
                    "5s 5s 5sr 2m 5m 8m 2p 5p 8p 3z 3z 4z 4z"
                    "4s 6s 3m 6m 9m 3p 6p 9p 1s 2s 1z 1z 6z"
                ]
            Draws = "5s"
        }

    /// 跑到座位 0 第 1 巡打出 5s、三家里有两家被问的那一步。
    let private atTheRaceFor5s () =
        startScripted nakiRaceScript
        |> fun state -> fst (stepped state (Action.Dahai(seat 0, pai "5s", true)))

    /// 跑到座位 0 第 2 巡打出 3s、等人响应的那一步。
    let private atTheDiscardOf3s () =
        startScripted nakiScript
        |> driveUntil passive (fun state -> junmeOf (seat 0) state = 2)
        |> fun state -> fst (stepped state (Action.Dahai(seat 0, pai "3s", true)))

    // ---- 谁能鸣 ----

    [<Fact>]
    let ``他家打出的牌，能碰的与能吃的一起进入合法动作集，碰排在吃前面`` () =
        let state = atTheDiscardOf3s ()

        // 座位 2 没有 3s；座位 3 有 4s 5s，但吃只吃上家。
        Assert.Equal<Seat list>(seats [ 1 ], waiting state)

        // 座位 1 手里三张 3s，因此大明杠也在列：它与碰同级，排在吃前面。
        Assert.Equal<Action list>(
            [
                Action.Pon(seat 1, seat 0, pai "3s", tilesOf "3s 3s")
                Action.Minkan(seat 1, seat 0, pai "3s", tilesOf "3s 3s 3s")
                Action.Chi(seat 1, seat 0, pai "3s", tilesOf "4s 5s")
                Action.None(seat 1)
            ],
            actionsOf (seat 1) state
        )

    [<Fact>]
    let ``吃只吃上家：同样握着 4s 5s 的上家不被问`` () =
        let state = atTheDiscardOf3s ()

        Assert.Contains(pai "4s", handOf (seat 3) state)
        Assert.Contains(pai "5s", handOf (seat 3) state)
        Assert.DoesNotContain(seat 3, waiting state)

        // 硬要吃：得到的是「现在不轮到你」——它压根不在被问之列。
        Assert.Equal<IllegalAction>(
            NotYourTurn(seat 3, seats [ 1 ]),
            rejected state (Action.Chi(seat 3, seat 0, pai "3s", tilesOf "4s 5s"))
        )

    [<Fact>]
    let ``碰是任意他家：对家打出的牌照样能碰`` () =
        let state = atTheDiscardOf3s ()

        let ponned, _ =
            stepped state (Action.Pon(seat 1, seat 0, pai "3s", tilesOf "3s 3s"))

        let discarded, _ = stepped ponned (Action.Dahai(seat 1, pai "1z", false))

        // 座位 3 是座位 1 的对家（既不是下家也不是上家），它能碰。
        Assert.Equal<Seat list>(seats [ 3 ], waiting discarded)

        Assert.Equal<Action list>(
            [ Action.Pon(seat 3, seat 1, pai "1z", tilesOf "1z 1z"); Action.None(seat 3) ],
            actionsOf (seat 3) discarded
        )

    [<Fact>]
    let ``红 5 与正牌各算各的：亮哪两张是两条不同的合法动作`` () =
        let state = atTheRaceFor5s ()

        // 座位 2 手里 5s 5s 5sr：亮两张正牌，或者亮一张正牌加红 5。
        // 大明杠只有一种亮法（三张全上），红 5 也跟着亮出去。
        Assert.Equal<Action list>(
            [
                Action.Pon(seat 2, seat 0, pai "5s", tilesOf "5s 5s")
                Action.Pon(seat 2, seat 0, pai "5s", tilesOf "5s 5sr")
                Action.Minkan(seat 2, seat 0, pai "5s", tilesOf "5s 5s 5sr")
                Action.None(seat 2)
            ],
            actionsOf (seat 2) state
        )

        // 座位 1 也被问到了（它能荣和也能吃），因此收齐答复才裁决。
        let declared, pending =
            stepped state (Action.Pon(seat 2, seat 0, pai "5s", tilesOf "5s 5sr"))

        Assert.Empty(pending)

        let ponned, events = stepped declared (Action.None(seat 1))

        Assert.Equal<Event list>([ Pon(seat 2, seat 0, pai "5s", tilesOf "5s 5sr") ], events)

        // 亮出去的是红 5，留在手里的是正牌 —— 红宝牌在副露里原样保留。
        Assert.Equal<Tile list>(tilesOf "5s 5s 5sr", nakiOf (seat 2) ponned |> List.collect Naki.tiles)
        Assert.Contains(pai "5s", handOf (seat 2) ponned)
        Assert.DoesNotContain(pai "5sr", handOf (seat 2) ponned)

    // ---- 撞车时谁赢：Ron > Pon / Kan > Chi ----

    [<Fact>]
    let ``荣和压过碰：宣言的先后不影响裁决`` () =
        let state = atTheRaceFor5s ()

        // 座位 1 既能荣和也能吃，座位 2 能碰。
        Assert.Equal<Seat list>(seats [ 1; 2 ], waiting state)

        // 碰先宣言、荣和后宣言 —— 收齐答复才裁决，因此「先被问到」不等于「优先」。
        let ponFirst, events =
            stepped state (Action.Pon(seat 2, seat 0, pai "5s", tilesOf "5s 5s"))

        Assert.Empty(events)

        let ended, _ = stepped ponFirst (Action.Hora(seat 1, seat 0, pai "5s"))

        Assert.Equal<Seat list>(seats [ 1 ], GameState.horas ended |> List.map (fun hora -> hora.Actor))
        Assert.True(GameState.isEnded ended)

        // 荣和赢了，碰没有发生：座位 2 一组副露都没有。
        Assert.Empty(nakiOf (seat 2) ended)

    [<Fact>]
    let ``碰压过吃：吃的那家什么也没发生`` () =
        let state = atTheRaceFor5s ()

        let chiFirst, _ =
            stepped state (Action.Chi(seat 1, seat 0, pai "5s", tilesOf "6s 7s"))

        let ponned, events =
            stepped chiFirst (Action.Pon(seat 2, seat 0, pai "5s", tilesOf "5s 5s"))

        // 只产出碰的那条事件，随后等碰的那家打牌。
        Assert.Equal<Event list>([ Pon(seat 2, seat 0, pai "5s", tilesOf "5s 5s") ], events)
        Assert.Equal<Seat list>(seats [ 2 ], waiting ponned)

        Assert.Equal<NakiKind list>([ NakiKind.Pon ], nakiOf (seat 2) ponned |> List.map Naki.kind)
        Assert.Empty(nakiOf (seat 1) ponned)

        // 吃没成，座位 1 的暗牌一张没动。
        Assert.Equal<Tile list>(handOf (seat 1) state, handOf (seat 1) ponned)

    // ---- 鸣完之后 ----

    [<Fact>]
    let ``碰产出 mjai 事件、进公开副露，随后直接由碰的那家打牌`` () =
        let state = atTheDiscardOf3s ()

        let ponned, events =
            stepped state (Action.Pon(seat 1, seat 0, pai "3s", tilesOf "3s 3s"))

        Assert.Equal<Event list>([ Pon(seat 1, seat 0, pai "3s", tilesOf "3s 3s") ], events)

        // 副露是公开信息，字段与 mjai 的 pai / consumed / target 1:1。
        match nakiOf (seat 1) ponned with
        | [ naki ] ->
            Assert.Equal<NakiKind>(NakiKind.Pon, Naki.kind naki)
            Assert.Equal<Tile option>(Some(pai "3s"), Naki.taken naki)
            Assert.Equal<Tile list>(tilesOf "3s 3s", Naki.consumed naki)
            Assert.Equal<Seat option>(Some(seat 0), Naki.target naki)
        | other -> failwith $"座位 1 应当恰有一组副露，实际是 {other}"

        // **跳过摸牌**：没有 tsumo 事件，也没有「刚摸进的那张」，直接等碰的那家打牌。
        match GameState.phase ponned with
        | AwaitingDahai phase ->
            Assert.Equal(seat 1, phase.Actor)
            Assert.Equal<Tile option>(None, phase.Tsumo)
        | other -> failwith $"碰完应当等座位 1 打牌，实际是 {other}"

        Assert.Equal<Seat list>(seats [ 1 ], waiting ponned)
        Assert.Equal<Tile option>(None, GameState.player (seat 1) ponned |> Option.bind PlayerState.drawn)

    [<Fact>]
    let ``鸣完那一手只有手切，牌数守恒：暗牌加副露的张数不变`` () =
        let state = atTheDiscardOf3s ()

        let ponned, _ =
            stepped state (Action.Pon(seat 1, seat 0, pai "3s", tilesOf "3s 3s"))

        // 碰之前 13 张暗牌，碰之后 11 张暗牌 + 1 组副露 = 等打牌的 14 张。
        Assert.Equal(13, handOf (seat 1) state |> List.length)
        Assert.Equal(11, handOf (seat 1) ponned |> List.length)

        match GameState.player (seat 1) ponned with
        | Some player ->
            Assert.Equal(1, PlayerState.nakiCount player)

            Assert.Equal(
                ruleset.HaipaiSize + 1,
                List.length (PlayerState.hand player) + 3 * PlayerState.nakiCount player
            )
        | None -> failwith "座位 1 应当存在"

        // 鸣完不摸牌，因此这一手一条摸切都没有。
        Assert.All(
            actionsOf (seat 1) ponned,
            fun action ->
                match action with
                | Action.Dahai(_, _, tsumogiri) -> Assert.False(tsumogiri)
                | other -> failwith $"这一手只该有打牌，实际是 {other}"
        )

    [<Fact>]
    let ``禁食替：碰完不能马上打回同一种牌，别的牌照打`` () =
        let state = atTheDiscardOf3s ()

        let ponned, _ =
            stepped state (Action.Pon(seat 1, seat 0, pai "3s", tilesOf "3s 3s"))

        // 手里还剩第三张 3s，但它被现物食替封住了；碰没有筋食替，4s 5s 6s 照打。
        Assert.Contains(pai "3s", handOf (seat 1) ponned)
        Assert.Equal<string list>([ "1m"; "2m"; "3m"; "9p"; "4s"; "5s"; "6s"; "1z" ], dahaiOf (seat 1) ponned)

        Assert.Equal<IllegalAction>(Kuikae(seat 1, pai "3s"), rejected ponned (Action.Dahai(seat 1, pai "3s", false)))

    [<Fact>]
    let ``禁食替：吃完既不能打回被吃的那张，也不能打顺子另一端`` () =
        let state = atTheDiscardOf3s ()

        let chied, events =
            stepped state (Action.Chi(seat 1, seat 0, pai "3s", tilesOf "4s 5s"))

        Assert.Equal<Event list>([ Chi(seat 1, seat 0, pai "3s", tilesOf "4s 5s") ], events)
        Assert.Equal<NakiKind list>([ NakiKind.Chi ], nakiOf (seat 1) chied |> List.map Naki.kind)

        // 吃 3s 用的是 4s 5s（两面搭子），因此 3s（现物）与 6s（筋）都不能打。
        Assert.Contains(pai "3s", handOf (seat 1) chied)
        Assert.Contains(pai "6s", handOf (seat 1) chied)
        Assert.Equal<string list>([ "1m"; "2m"; "3m"; "9p"; "1z" ], dahaiOf (seat 1) chied)

        Assert.Equal<IllegalAction>(Kuikae(seat 1, pai "3s"), rejected chied (Action.Dahai(seat 1, pai "3s", false)))
        Assert.Equal<IllegalAction>(Kuikae(seat 1, pai "6s"), rejected chied (Action.Dahai(seat 1, pai "6s", false)))

    // ---- Junme 与河的记号 ----

    [<Fact>]
    let ``连续鸣牌：中间被跳过的座位一张牌都没摸到，四家的 Junme 都不动`` () =
        let state = atTheDiscardOf3s ()

        Assert.Equal<int list>([ 2; 1; 1; 1 ], junmes state)

        let ponned, _ =
            stepped state (Action.Pon(seat 1, seat 0, pai "3s", tilesOf "3s 3s"))

        let discarded, _ = stepped ponned (Action.Dahai(seat 1, pai "1z", false))

        let again, events =
            stepped discarded (Action.Pon(seat 3, seat 1, pai "1z", tilesOf "1z 1z"))

        Assert.Equal<Event list>([ Pon(seat 3, seat 1, pai "1z", tilesOf "1z 1z") ], events)

        // 座位 2 在这两次鸣牌之间本该摸一张，被跳过了；鸣牌自己也不摸牌。
        Assert.Equal<int list>([ 2; 1; 1; 1 ], junmes again)
        Assert.Equal<Seat list>(seats [ 3 ], waiting again)

        // 鸣完那家打完之后，摸牌的是**它的下家**，不是原本的顺序。
        let after, _ = stepped again (Action.Dahai(seat 3, pai "9m", false))

        Assert.Equal<Seat list>(seats [ 0 ], waiting after)
        Assert.Equal<int list>([ 3; 1; 1; 1 ], junmes after)

    [<Fact>]
    let ``引擎给的 Junme 与事件流里数出来的一致`` () =
        let state = atTheDiscardOf3s ()

        Assert.Equal<int list>(Seat.all ruleset |> List.map (fun seat -> junmeOf seat state), junmes state)

    [<Fact>]
    let ``被鸣的那张仍在打牌者的河里，那家的河被记上「被鸣走过」`` () =
        let state = atTheDiscardOf3s ()

        Assert.Equal<bool list>([ false; false; false; false ], kawaTakenOf state)

        let ponned, _ =
            stepped state (Action.Pon(seat 1, seat 0, pai "3s", tilesOf "3s 3s"))

        // 只有被鸣的那一家被记上；记号只置位不清除（流し満貫的前提，12 票消费）。
        Assert.Equal<bool list>([ true; false; false; false ], kawaTakenOf ponned)

        // 牌本身留在河里：振听看的是「自己打过什么」，与那张后来被谁拿走无关。
        match GameState.player (seat 0) ponned with
        | Some player -> Assert.Contains(pai "3s", PlayerState.kawa player)
        | None -> failwith "座位 0 应当存在"

        let discarded, _ = stepped ponned (Action.Dahai(seat 1, pai "1z", false))

        let again, _ =
            stepped discarded (Action.Pon(seat 3, seat 1, pai "1z", tilesOf "1z 1z"))

        Assert.Equal<bool list>([ true; true; false; false ], kawaTakenOf again)

    // ---- 拒绝的理由 ----

    [<Fact>]
    let ``鸣的不是刚打出的那张，或者那几张凑不成，都被拒`` () =
        let state = atTheDiscardOf3s ()

        Assert.Equal<IllegalAction>(
            NakiTileMismatch(seat 1, seat 2, pai "3s"),
            rejected state (Action.Pon(seat 1, seat 2, pai "3s", tilesOf "3s 3s"))
        )

        Assert.Equal<IllegalAction>(
            NakiTileMismatch(seat 1, seat 0, pai "9p"),
            rejected state (Action.Chi(seat 1, seat 0, pai "9p", tilesOf "4s 5s"))
        )

        // 手里确实有 4s 与 6s，但 4s 6s 配 3s 凑不成顺子。
        Assert.Equal<IllegalAction>(
            CannotNaki(seat 1, NakiKind.Chi, pai "3s", tilesOf "4s 6s"),
            rejected state (Action.Chi(seat 1, seat 0, pai "3s", tilesOf "4s 6s"))
        )

        // 1s 2s 3s 是顺子，但座位 1 手里没有 1s 2s。
        Assert.Equal<IllegalAction>(
            CannotNaki(seat 1, NakiKind.Chi, pai "3s", tilesOf "1s 2s"),
            rejected state (Action.Chi(seat 1, seat 0, pai "3s", tilesOf "1s 2s"))
        )

    [<Fact>]
    let ``摸牌后阶段没有可响应的牌，鸣牌被拒`` () =
        let state = startScripted nakiScript

        Assert.Equal<IllegalAction>(
            NothingToRespond context.Oya,
            rejected state (Action.Pon(context.Oya, seat 1, pai "3s", tilesOf "3s 3s"))
        )

    [<Fact>]
    let ``鸣牌相关的拒绝理由有中文说明`` () =
        Assert.Equal(
            "座位 1 声称鸣座位 2 打出的 3索，但刚打出的不是这张",
            IllegalAction.toDisplay (NakiTileMismatch(seat 1, seat 2, pai "3s"))
        )

        Assert.Equal(
            "座位 1 用 4索5索 碰不了 3索",
            IllegalAction.toDisplay (CannotNaki(seat 1, NakiKind.Pon, pai "3s", tilesOf "4s 5s"))
        )

        Assert.Equal("座位 2 振听，不能荣和 3索", IllegalAction.toDisplay (RonWhileFuriten(seat 2, pai "3s")))

        Assert.Equal("座位 1 刚鸣完，这一手不能打 3索（禁食替）", IllegalAction.toDisplay (Kuikae(seat 1, pai "3s")))
