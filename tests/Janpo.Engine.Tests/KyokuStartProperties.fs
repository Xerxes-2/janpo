namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Thoth.Json.Newtonsoft
open Janpo

/// 开局的不变量。核心是牌数守恒：开完局，牌一张不多一张不少。
/// 具名用例见 KyokuStartTests。
[<Properties(Arbitrary = [| typeof<RulesetArbitraries>; typeof<KyokuStartArbitraries> |])>]
module KyokuStartProperties =

    let private start (ruleset: Ruleset) (context: KyokuContext) (seed: int) =
        KyokuStart.create ruleset context (Rng.ofSeed seed)

    [<Property>]
    let ``开局后手牌与山上的牌合起来仍是完整一副，无重复无丢失`` (ruleset: Ruleset) (context: KyokuContext) (seed: int) =
        match start ruleset context seed with
        | Error _ -> false
        | Ok(started, _) ->
            let everything = List.concat started.Hands @ Wall.tiles started.Wall

            List.length everything = Ruleset.wallSize ruleset
            && Tile.sort everything = Tile.sort (Ruleset.wallTiles ruleset)

    [<Property>]
    let ``开局后 Oya 比别家多一张`` (ruleset: Ruleset) (context: KyokuContext) (seed: int) =
        match start ruleset context seed with
        | Error _ -> false
        | Ok(started, _) ->
            started.Hands
            |> List.mapi (fun seat hand ->
                List.length hand = ruleset.HaipaiSize + (if seat = context.Oya then 1 else 0))
            |> List.forall id

    [<Property>]
    let ``手牌就是 start_kyoku 的配牌加上 tsumo 摸进的那张`` (ruleset: Ruleset) (context: KyokuContext) (seed: int) =
        match start ruleset context seed with
        | Error _ -> false
        | Ok(started, _) ->
            match started.Events with
            | [ StartKyoku fields; Tsumo(actor, pai) ] ->
                actor = context.Oya
                && fields.Tehais
                   |> List.mapi (fun seat tehai ->
                       let expected =
                           if seat = context.Oya then
                               Tile.sort (pai :: tehai)
                           else
                               tehai

                       expected = List.item seat started.Hands)
                   |> List.forall id
            | _ -> false

    [<Property>]
    let ``同一种子与同一条件开出同一局`` (ruleset: Ruleset) (context: KyokuContext) (seed: int) =
        start ruleset context seed = start ruleset context seed

    [<Property>]
    let ``开局事件的 JSON 往返不变`` (ruleset: Ruleset) (context: KyokuContext) (seed: int) =
        match start ruleset context seed with
        | Error _ -> false
        | Ok(started, _) ->
            started.Events
            |> List.forall (fun event ->
                Decode.fromString Event.decoder (Encode.toString 0 (Event.encoder event)) = Ok event)

    [<Property>]
    let ``开局翻开的宝牌指示牌就是 start_kyoku 里的那张`` (ruleset: Ruleset) (context: KyokuContext) (seed: int) =
        match start ruleset context seed with
        | Error _ -> false
        | Ok(started, _) ->
            match started.Events with
            | StartKyoku fields :: _ -> Wall.doraIndicators started.Wall = [ fields.DoraMarker ]
            | _ -> false
