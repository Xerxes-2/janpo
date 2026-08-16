namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 决策包：某座位的合法观测 + 带 id 与中文 label 的动作列表。它是 LLM 与真人坐席消费的
/// 唯一输入，反方向只有一个整数（动作 id）。**`GameState` 永不越过这道边界。**
module DecisionPackageTests =

    let private tile (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"记法 {notation} 应当合法，却得到 {error}"

    let private packageFor (seat: Seat) (state: GameState) : DecisionPackage =
        match DecisionPackage.forSeat seat state with
        | Some package -> package
        | None -> failwith "这个座位应当正在被问"

    /// 摊好的牌山：Oya 一手 1m-9m 1p 2p 3p 5z 摸进 1z，因此它听 5z 单骑，立直得了。
    /// 合法动作集固定为「立直宣言 + 13 条手切 + 摸切 1z」。
    let private opening = startScripted tsumoHoraScript

    [<Fact>]
    let ``只有正在被问的座位才有决策包`` () =
        Assert.True((DecisionPackage.forSeat (seat 1) opening).IsNone)
        Assert.True((DecisionPackage.forSeat (seat 9) opening).IsNone)
        Assert.Equal(seat 0, DecisionPackage.seat (packageFor (seat 0) opening))

    [<Fact>]
    let ``动作按合法动作集的既定顺序从 0 起编号`` () =
        let package = packageFor (seat 0) opening

        let asked =
            GameState.legalActions opening |> List.find (fun choice -> choice.Seat = seat 0)

        Assert.Equal<Action list>(asked.Actions, DecisionPackage.options package |> List.map ActionOption.action)

        Assert.Equal<int list>(
            [ 0 .. List.length asked.Actions - 1 ],
            DecisionPackage.options package |> List.map ActionOption.id
        )

    [<Fact>]
    let ``id 取得回那个动作`` () =
        let package = packageFor (seat 0) opening

        Assert.Equal(Some(Action.Riichi(seat 0)), DecisionPackage.tryAction 0 package)
        Assert.Equal(Some(Action.Dahai(seat 0, tile "1m", false)), DecisionPackage.tryAction 1 package)
        Assert.Equal(Some(Action.Dahai(seat 0, tile "1z", true)), DecisionPackage.tryAction 14 package)

    [<Fact>]
    let ``id 越界或不存在时是 None 而不是异常`` () =
        // 兜底路径依赖这一条：LLM 给回来的整数什么都可能是。
        let package = packageFor (seat 0) opening

        Assert.Equal(None, DecisionPackage.tryAction 15 package)
        Assert.Equal(None, DecisionPackage.tryAction -1 package)
        Assert.Equal(None, DecisionPackage.tryAction 9999 package)

    [<Fact>]
    let ``取回来的动作引擎照单全收`` () =
        let package = packageFor (seat 0) opening

        for option in DecisionPackage.options package do
            match DecisionPackage.tryAction (ActionOption.id option) package with
            | None -> failwith "包里的 id 应当取得回动作"
            | Some action ->
                match GameState.step opening action with
                | Ok _ -> ()
                | Error illegal -> failwith $"决策包给出的动作应当合法，却得到「{IllegalAction.toDisplay illegal}」"

    [<Fact>]
    let ``动作的中文 label 是渲染层的单向出口`` () =
        Assert.Equal("摸切东", Action.toDisplay (Action.Dahai(seat 0, tile "1z", true)))
        Assert.Equal("手切赤5万", Action.toDisplay (Action.Dahai(seat 0, tile "5mr", false)))
        Assert.Equal("自摸3索", Action.toDisplay (Action.Hora(seat 0, seat 0, tile "3s")))
        Assert.Equal("荣和3索", Action.toDisplay (Action.Hora(seat 0, seat 1, tile "3s")))

        Assert.Equal(
            "碰5万（亮5万 赤5万）",
            Action.toDisplay (Action.Pon(seat 0, seat 1, tile "5m", [ tile "5m"; tile "5mr" ]))
        )

        Assert.Equal("吃3索（亮4索 5索）", Action.toDisplay (Action.Chi(seat 0, seat 3, tile "3s", [ tile "4s"; tile "5s" ])))

        Assert.Equal(
            "暗杠（亮5万 5万 5万 赤5万）",
            Action.toDisplay (Action.Ankan(seat 0, [ tile "5m"; tile "5m"; tile "5m"; tile "5mr" ]))
        )

        Assert.Equal("加杠5万", Action.toDisplay (Action.Kakan(seat 0, tile "5m", [ tile "5m"; tile "5m"; tile "5m" ])))

        Assert.Equal(
            "大明杠5万（亮5万 5万 赤5万）",
            Action.toDisplay (Action.Minkan(seat 0, seat 2, tile "5m", [ tile "5m"; tile "5m"; tile "5mr" ]))
        )

        Assert.Equal("立直宣言", Action.toDisplay (Action.Riichi(seat 0)))
        Assert.Equal("过", Action.toDisplay (Action.None(seat 0)))
        Assert.Equal("九种九牌", Action.toDisplay (Action.Ryuukyoku(seat 0)))

    [<Fact>]
    let ``每条动作都带 label`` () =
        let package = packageFor (seat 0) opening
        let labels = DecisionPackage.options package |> List.map ActionOption.label

        Assert.Equal("立直宣言", List.head labels)
        Assert.Equal("摸切东", List.last labels)
        Assert.True(labels |> List.forall (fun label -> label <> ""))

    // ---- wire 形态（单向） ----

    let private json (encoder: Encoder<'a>) (value: 'a) : string = Encode.toString 0 (encoder value)

    [<Fact>]
    let ``决策包编码为观测 + 编号动作 + 脚手架槽位`` () =
        let encoded = json DecisionPackage.encoder (packageFor (seat 0) opening)

        Assert.Contains("\"seat\":0", encoded)
        Assert.Contains("\"observation\":{", encoded)
        Assert.Contains("{\"id\":0,\"label\":\"立直宣言\",\"action\":{\"type\":\"reach\",\"actor\":0}}", encoded)

        Assert.Contains(
            "{\"id\":14,\"label\":\"摸切东\",\"action\":{\"type\":\"dahai\",\"actor\":0,\"pai\":\"1z\",\"tsumogiri\":true}}",
            encoded
        )

        // 脚手架数值（票 24）：向听数、有效牌与逐张试打的进退向。
        // 这一手已经摸进了，因此手牌自己没有有效牌（要先试打一张）。
        // 向听数带着**引擎渲染好的中文**过界（ADR-0005）：0 叫「听牌」，不是「0 向听」。
        Assert.Contains(
            "\"scaffold\":{\"shanten\":{\"value\":0,\"display\":\"听牌\"},\"ukeire\":null,\"dahai\":[",
            encoded
        )

        // 摸切 1z（id=14）不退向听：打完听 5z 单骑，还剩 3 枚。
        Assert.Contains(
            "{\"pai\":\"1z\",\"action_ids\":[14],\"shanten\":{\"value\":0,\"display\":\"听牌\"},\"shanten_delta\":0,\"ukeire\":{\"total\":3,\"kinds\":1,\"tiles\":[{\"pai\":\"5z\",\"remaining\":3}]}}",
            encoded
        )

    [<Fact>]
    let ``动作的结构化字段照 mjai 的动作消息`` () =
        let encode = json Action.encoder

        Assert.Contains("\"type\":\"none\",\"actor\":2", encode (Action.None(seat 2)))
        Assert.Contains("\"type\":\"ryukyoku\",\"actor\":2", encode (Action.Ryuukyoku(seat 2)))

        Assert.Contains(
            "\"type\":\"hora\",\"actor\":2,\"target\":1,\"pai\":\"3s\"",
            encode (Action.Hora(seat 2, seat 1, tile "3s"))
        )

        Assert.Contains(
            "\"type\":\"pon\",\"actor\":2,\"target\":1,\"pai\":\"5m\",\"consumed\":[\"5m\",\"5mr\"]",
            encode (Action.Pon(seat 2, seat 1, tile "5m", [ tile "5m"; tile "5mr" ]))
        )

        Assert.Contains(
            "\"type\":\"chi\",\"actor\":2,\"target\":1,\"pai\":\"3s\",\"consumed\":[\"4s\",\"5s\"]",
            encode (Action.Chi(seat 2, seat 1, tile "3s", [ tile "4s"; tile "5s" ]))
        )

        Assert.Contains(
            "\"type\":\"ankan\",\"actor\":2,\"consumed\":[\"5m\",\"5m\",\"5m\",\"5mr\"]",
            encode (Action.Ankan(seat 2, [ tile "5m"; tile "5m"; tile "5m"; tile "5mr" ]))
        )

        Assert.Contains(
            "\"type\":\"kakan\",\"actor\":2,\"pai\":\"5m\",\"consumed\":[\"5m\",\"5m\",\"5m\"]",
            encode (Action.Kakan(seat 2, tile "5m", [ tile "5m"; tile "5m"; tile "5m" ]))
        )

        // 标识符按术语表拼作 Minkan，wire 仍是 mjai 的 daiminkan（ADR-0001）。
        Assert.Contains(
            "\"type\":\"daiminkan\",\"actor\":2,\"target\":1,\"pai\":\"5m\"",
            encode (Action.Minkan(seat 2, seat 1, tile "5m", [ tile "5m"; tile "5m"; tile "5mr" ]))
        )
