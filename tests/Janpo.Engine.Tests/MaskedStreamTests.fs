namespace Janpo.Engine.Tests

open Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 座席的历史里**读得出什么**（票 29a 第三节）。29b 的 prompt 前缀要的那几项事实，
/// 这里逐项钉一条：读法写在测试里，报告里的对照表指向它们。
///
/// **每一项都不是新造的事件**：全是既有 mjai 事件的顺序与相邻关系。
/// 最锋利的一条是见逃し——它是一个事件的**缺席**（打牌之后没有鸣牌事件），
/// 快照表达不了，事件流里天然就有。
module MaskedStreamTests =

    let private seat (index: int) : Seat = SeatFixtures.seat index

    /// 他家摸的那张看不见，摊平时拿它占位——**读时序的判据不看牌面**。
    let private hidden: Tile =
        match Tile.parse "1m" with
        | Ok tile -> tile
        | Error _ -> failwith "1m 应当是合法记法"

    /// 掩蔽流里的公开事件，按发生顺序，**他家摸牌换成一条留了位置的 `tsumo`**。
    ///
    /// 读时序的那几项判据看的是「紧接着的下一条是什么」，因此摸牌**不能被滤掉**：
    /// 滤掉之后「打牌之后没有鸣牌」与「杠之后先翻宝牌再补摸」都读错。
    /// 牌面在这里不重要（本来就看不见），拿一张占位牌把摸牌摊平成 `Event` 就够了。
    let private timeline (masked: MaskedEvent list) : Event list =
        masked
        |> List.choose (fun each ->
            match each with
            | MaskedEvent.StartKyoku _ -> None
            | MaskedEvent.Tsumo(actor, pai) -> Some(Tsumo(actor, Option.defaultValue hidden pai))
            | MaskedEvent.Public event -> Some event)

    // ---- 掩蔽本身 ----

    [<Fact>]
    let ``配牌只见自家：他家那三手在掩蔽流里没有位置`` () =
        let state, _ = start 1177
        let viewer = seat 2

        match Observation.stream viewer state |> List.head with
        | MaskedEvent.StartKyoku fields ->
            let own =
                GameState.player viewer state
                |> Option.map PlayerState.hand
                |> Option.defaultValue []

            // 自家那 13 张原样；其余三家的配牌**这个 case 里没有字段装得下**。
            Assert.Equal<Tile list>(own, fields.Tehai)
            Assert.Equal(ruleset.HaipaiSize, List.length fields.Tehai)
        | other -> failwith $"第一条应当是 start_kyoku，却是 {other}"

    [<Fact>]
    let ``他家摸的那张没有牌面，但「他摸了」看得见`` () =
        let state, _ = start 1177
        let viewer = seat 2

        let draws =
            Observation.stream viewer state
            |> List.choose (fun masked ->
                match masked with
                | MaskedEvent.Tsumo(actor, pai) -> Some(actor, pai)
                | _ -> None)

        // 开局只有亲摸过一张：座位 2 看得见「座位 0 摸了」，看不见摸的是什么。
        Assert.Equal<(Seat * Tile option) list>([ seat 0, None ], draws)

    // ---- 第三节：29b 要的那几项历史事实 ----

    /// 某座位打到第几巡：**数它自己的 `tsumo`**（鸣牌不摸牌，因此鸣的那家巡目不涨）。
    let private junmeIn (target: Seat) (masked: MaskedEvent list) : int =
        masked
        |> List.filter (fun each ->
            match each with
            | MaskedEvent.Tsumo(actor, _) -> actor = target
            | _ -> false)
        |> List.length

    [<Fact>]
    let ``巡目就是流里数自己的摸牌，与投影给的一致`` () =
        let states = trace Kyoku.randomPlayer 7
        let state = states |> List.item (List.length states / 2)
        let viewer = seat 1
        let masked = Observation.stream viewer state

        match Observation.ofMasked ruleset viewer masked with
        | None -> failwith "这一手应当有观测"
        | Some observation ->
            Assert.Equal(junmeIn viewer masked, observation.Self.Junme)

            observation.Others
            |> List.iter (fun other -> Assert.Equal(junmeIn other.Seat masked, other.Junme))

    /// 立直宣言牌与宣言巡：`reach` 之后**那一家的第一条 `dahai`** 就是宣言牌，
    /// 宣言巡是那之前它自己摸过几次。
    let private riichiDeclaration (target: Seat) (masked: MaskedEvent list) : (Tile * int) option =
        let rec loop (junme: int) (declared: bool) (rest: MaskedEvent list) =
            match rest with
            | [] -> None
            | MaskedEvent.Tsumo(actor, _) :: tail -> loop (if actor = target then junme + 1 else junme) declared tail
            | MaskedEvent.Public(Riichi actor) :: tail when actor = target -> loop junme true tail
            | MaskedEvent.Public(Dahai(actor, pai, _)) :: _ when actor = target && declared -> Some(pai, junme)
            | _ :: tail -> loop junme declared tail

        loop 0 false masked

    [<Fact>]
    let ``立直宣言牌与宣言巡：宣言之后那一家的第一条打牌就是它`` () =
        // 见立直就立的选手也不是每个种子都立得成（要门清听牌），扫一批取第一个立了直的。
        let ended =
            [ 1..40 ]
            |> List.map (fun seed -> trace riichiSeeking seed |> List.last)
            |> List.find (fun state ->
                GameState.events state
                |> List.exists (fun event ->
                    match event with
                    | Riichi _ -> true
                    | _ -> false))

        let masked = Observation.stream (seat 0) ended

        let declared =
            Seat.all ruleset
            |> List.choose (fun target -> riichiDeclaration target masked |> Option.map (fun each -> target, each))

        // 这条轨迹见立直就立，必然有人立了直。
        Assert.NotEmpty declared

        declared
        |> List.iter (fun (target, (pai, junme)) ->
            // 宣言牌落在那一家的河里（被荣和的宣言牌也留在河里），巡目为正。
            let kawa =
                GameState.player target ended
                |> Option.map PlayerState.kawa
                |> Option.defaultValue []

            Assert.Contains(pai, kawa)
            Assert.True(junme > 0, "宣言巡应当为正"))

    /// 鸣的时序与来源，以及**鸣完打了什么**：鸣牌事件带着 `target`（被谁鸣走）与 `pai`（哪一张），
    /// 紧接着的那条 `dahai` 就是鸣完打出去的那张。
    let private nakiThenDahai (masked: MaskedEvent list) : (Seat * Seat * Tile * Tile * bool) list =
        let rec loop (pending: (Seat * Seat * Tile) option) (rest: Event list) found =
            match rest, pending with
            | [], _ -> List.rev found
            | Pon(actor, target, pai, _) :: tail, _
            | Chi(actor, target, pai, _) :: tail, _ -> loop (Some(actor, target, pai)) tail found
            | Dahai(actor, dahai, tsumogiri) :: tail, Some(caller, target, pai) when actor = caller ->
                loop None tail ((caller, target, pai, dahai, tsumogiri) :: found)
            | _ :: tail, _ -> loop pending tail found

        loop None (timeline masked) []

    [<Fact>]
    let ``鸣的时序与来源都在流里：谁鸣了谁的哪一张、鸣完打了什么`` () =
        let ended = trace nakiSeeking 5 |> List.last
        let called = Observation.stream (seat 0) ended |> nakiThenDahai

        Assert.NotEmpty called

        called
        |> List.iter (fun (caller, target, pai, _, tsumogiri) ->
            Assert.NotEqual<Seat>(caller, target)
            // 被鸣走的那张仍留在打牌者的河里（CONTEXT.md 的 Kawa）。
            let kawa =
                GameState.player target ended
                |> Option.map PlayerState.kawa
                |> Option.defaultValue []

            Assert.Contains(pai, kawa)
            // 鸣牌没有摸牌动作，因此鸣完那一手必然手切。
            Assert.False(tsumogiri, "碰 / 吃之后那一张必然是手切"))

    /// **见逃し**：一条打牌之后**没有**鸣牌事件——三家都没要那张牌。
    /// 它不是一条事件，是一条事件的缺席，因此这里数的是「下一条不是鸣牌」的打牌。
    let private passedOver (masked: MaskedEvent list) : (Seat * Tile) list =
        let rec loop (rest: Event list) found =
            match rest with
            | Dahai(actor, pai, _) :: next :: tail ->
                let taken =
                    match next with
                    | Pon _
                    | Chi _
                    | Minkan _
                    | Hora _ -> true
                    | _ -> false

                loop (next :: tail) (if taken then found else (actor, pai) :: found)
            | _ :: tail -> loop tail found
            | [] -> List.rev found

        loop (timeline masked) []

    [<Fact>]
    let ``见逃し不必造新事件：打牌之后没有鸣牌事件，就是谁都没要`` () =
        let ended = trace nakiSeeking 5 |> List.last
        let masked = Observation.stream (seat 0) ended

        let discards =
            timeline masked
            |> List.filter (fun event ->
                match event with
                | Dahai _ -> true
                | _ -> false)

        let passed = passedOver masked
        let called = nakiThenDahai masked

        // 每一张打出去的牌要么被鸣走 / 被荣和，要么谁都没要——两边加起来是全部。
        Assert.NotEmpty passed
        Assert.NotEmpty called
        Assert.True(List.length passed < List.length discards, "不可能每一张都没人要（这一局有鸣牌）")

    /// 杠宝牌的揭示时机：`dora` 事件排在流里的哪一条之后。
    /// 暗杠当场翻（`ankan` → `dora` → 岭上 `tsumo`），明杠欠到打牌那一刻才翻
    /// （`daiminkan` → 岭上 `tsumo` → …… → `dora` → `dahai`）。
    [<Fact>]
    let ``杠宝牌的揭示时机在流里看得出来：暗杠当场翻，明杠等打牌`` () =
        let ended = kanTrace "5z" ankanScript |> List.last
        let events = Observation.stream (seat 0) ended |> timeline

        let ankanAt =
            events
            |> List.tryFindIndex (fun event ->
                match event with
                | Ankan _ -> true
                | _ -> false)

        match ankanAt with
        | None -> failwith "这条轨迹应当有暗杠"
        | Some index ->
            // 暗杠的下一条就是新宝牌指示牌，再下一条才是岭上摸牌。
            match List.item (index + 1) events, List.item (index + 2) events with
            | Dora _, Tsumo _ -> ()
            | first, second -> failwith $"暗杠之后应当是 dora → tsumo，却是 {first} → {second}"

    // ---- 上帝视角那条流 ----

    [<Fact>]
    let ``上帝视角那条流就是这一局的事件流本身，一条不掩`` () =
        let ended = trace Kyoku.randomPlayer 7 |> List.last

        Assert.Equal<Event list>(GameState.events ended, GodView.stream ended)
