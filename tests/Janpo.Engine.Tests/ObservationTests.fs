namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 观测投影（CONTEXT.md 的 Observation Projection）：局面 → 某座位**能合法看到的一切**。
/// 这里钉的是「看得见什么」与「看不见什么」；序列化里不漏牌那条在 ObservationProperties。
module ObservationTests =

    let private observe (seat: Seat) (state: GameState) : Observation =
        match Observation.ofState seat state with
        | Some observation -> observation
        | None -> failwith "这个座位应当有观测"

    let private playerAt (seat: Seat) (state: GameState) : PlayerState =
        match GameState.player seat state with
        | Some player -> player
        | None -> failwith "这个座位应当在局面里"

    let private tile (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"记法 {notation} 应当合法，却得到 {error}"

    /// 提交一条指定的打牌。它必须就在合法动作集里，否则用例自己先破。
    let private discard (notation: string) (tsumogiri: bool) (state: GameState) : GameState =
        let wanted = tile notation

        let action =
            GameState.legalActions state
            |> List.collect (fun choice -> choice.Actions)
            |> List.tryFind (fun action ->
                match action with
                | Action.Dahai(_, pai, flag) -> pai = wanted && flag = tsumogiri
                | _ -> false)

        match action with
        | None -> failwith $"合法动作集里应当有打 {notation} 这一条"
        | Some action ->
            match GameState.step state action with
            | Ok(next, _) -> next
            | Error illegal -> failwith $"这一手应当被接受，却得到「{IllegalAction.toDisplay illegal}」"

    [<Fact>]
    let ``自家看得见自家的暗牌、刚摸进那张与振听`` () =
        let state, _ = start 7
        let observation = observe (seat 0) state
        let player = playerAt (seat 0) state

        Assert.Equal<Tile list>(PlayerState.hand player, observation.Self.Hand)
        Assert.Equal(PlayerState.drawn player, observation.Self.Drawn)
        Assert.Equal(PlayerState.furiten player, observation.Self.Furiten)

    [<Fact>]
    let ``他家只剩公开信息：河、副露、点数、立直与一发`` () =
        let state, _ = start 7
        let observation = observe (seat 0) state

        Assert.Equal(3, List.length observation.Others)

        for other in observation.Others do
            let player = playerAt other.Seat state
            Assert.Equal<Tile list>(PlayerState.kawa player, other.Kawa |> List.map (fun entry -> entry.Pai))
            Assert.Equal<Naki list>(PlayerState.naki player, other.Naki)
            Assert.Equal(PlayerState.score player, other.Score)
            Assert.Equal(PlayerState.riichi player, other.Riichi)
            Assert.Equal(PlayerState.ippatsu player, other.Ippatsu)

    [<Fact>]
    let ``他家按下家、对家、上家的顺序排，相对位置一并给出`` () =
        let state, _ = start 7
        let observation = observe (seat 1) state

        Assert.Equal<Seat list>(seats [ 2; 3; 0 ], observation.Others |> List.map (fun other -> other.Seat))
        Assert.Equal<int list>([ 1; 2; 3 ], observation.Others |> List.map (fun other -> other.Relative))

    [<Fact>]
    let ``观测只带已翻开的表宝牌指示牌`` () =
        let state, _ = start 7
        let observation = observe (seat 0) state

        Assert.Equal<Tile list>(Wall.doraIndicators (GameState.wall state), observation.DoraMarkers)
        Assert.Equal(Wall.remaining (GameState.wall state), observation.WallRemaining)

    [<Fact>]
    let ``场况取自局面：场风、局数、本场、供托与各家自风`` () =
        let state, _ = start 7
        let observation = observe (seat 2) state
        let context = GameState.context state

        Assert.Equal(context.Bakaze, observation.Bakaze)
        Assert.Equal(context.Kyoku, observation.Kyoku)
        Assert.Equal(context.Honba, observation.Honba)
        Assert.Equal(GameState.kyotaku state, observation.Kyotaku)
        Assert.Equal(Shaa, observation.Self.Jikaze)

    [<Fact>]
    let ``座位不在这个规则集里就没有观测`` () =
        let state, _ = start 7
        Assert.True((Observation.ofState (seat 9) state).IsNone)

    [<Fact>]
    let ``河里每一张都记着是手切还是摸切`` () =
        // 摧好的牌山：Oya 摸进 1z（摸切），座位 1 摸进 2z 却手切 9m。
        // 两张都鸣不了（字牌各家只一张、9m 也只各一张），因此不会拐进响应阶段。
        let state = startScripted tsumoHoraScript |> discard "1z" true |> discard "9m" false

        let observation = observe (seat 1) state

        Assert.Equal<KawaEntry list>([ { Pai = tile "9m"; Tsumogiri = false } ], observation.Self.Kawa)

        let kamicha = observation.Others |> List.find (fun other -> other.Seat = seat 0)

        Assert.Equal<KawaEntry list>([ { Pai = tile "1z"; Tsumogiri = true } ], kamicha.Kawa)

    [<Fact>]
    let ``上帝视角亮出全部座位的暗牌与里宝牌`` () =
        let state = startScripted tsumoHoraScript
        let view = GodView.ofState state

        Assert.Equal<Tile list list>(
            GameState.players state |> List.map PlayerState.hand,
            view.Seats |> List.map (fun each -> each.Hand)
        )

        Assert.Equal<Tile list>(Wall.uraIndicators (GameState.wall state), view.UraMarkers)
        Assert.False(List.isEmpty view.UraMarkers)

    // ---- wire 形态（单向） ----

    let private json (encoder: Encoder<'a>) (value: 'a) : string = Encode.toString 0 (encoder value)

    /// 子串出现了几次。「整份观测里只有一处 tehai」靠它钉。
    let private occurrences (needle: string) (text: string) : int =
        text.Split(needle) |> Array.length |> (fun parts -> parts - 1)

    [<Fact>]
    let ``整份观测里只有自家带暗牌与振听`` () =
        // 他家那三项是 `MaskedSeat`，类型里就没有这两个字段。
        let state, _ = start 7
        let encoded = json Observation.encoder (observe (seat 0) state)

        Assert.Equal(1, occurrences "\"tehai\"" encoded)
        Assert.Equal(1, occurrences "\"furiten\"" encoded)
        Assert.Equal(3, occurrences "\"relative\"" encoded)

    [<Fact>]
    let ``观测编码为一个带自家与他家的对象`` () =
        let state = startScripted tsumoHoraScript
        let encoded = json Observation.encoder (observe (seat 0) state)

        Assert.Contains(""""seat":0""", encoded)
        Assert.Contains("\"bakaze\":\"1z\"", encoded)
        Assert.Contains(""""wall_remaining":69""", encoded)
        Assert.Contains(""""tehai":["1m","2m","3m","4m","5m","6m","7m","8m","9m","1p","2p","3p","1z","5z"]""", encoded)
        Assert.Contains("\"tsumo\":\"1z\"", encoded)
        Assert.Contains("\"riichi\":\"none\"", encoded)
        Assert.Contains(""""furiten":{"permanent":false,"doujun":false}""", encoded)
        // 里宝牌不在观测里：它没翻开，谁也看不见。
        Assert.DoesNotContain("uradora_markers", encoded)

    [<Fact>]
    let ``上帝视角编码为一个带全部座位与里宝牌的对象`` () =
        let state = startScripted tsumoHoraScript
        let encoded = json GodView.encoder (GodView.ofState state)

        Assert.Contains("uradora_markers", encoded)
        Assert.Contains("\"tehai\":[\"9m\",\"1p\",\"4p\",\"7p\"", encoded)
        // 上帝视角没有观测者，因此既没有自家也没有相对位置。
        Assert.DoesNotContain(""""self":""", encoded)
        Assert.DoesNotContain("relative", encoded)
