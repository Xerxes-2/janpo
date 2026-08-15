namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 点数授受的不变量。**最值钱的一条是「任一次授受后四家点数与供托之和不变」**——
/// 它比任何单个点数用例都强：点数只在四家与供托之间搬，凭空多出来或少掉都会被它抓住。
/// 具名用例见 ScoreTests / FuTests / HoraTests。
[<Properties(Arbitrary = [| typeof<ScoreArbitraries>; typeof<GameStateArbitraries> |])>]
module ScoreProperties =

    let private engine = Ruleset.yonma

    // ---- 授受本身 ----

    [<Property>]
    let ``一次授受的增减之和恒等于收走的供托`` (HoraCase(transfer, value)) =
        let score = Score.hora engine transfer value

        List.sum score.Deltas = transfer.Kyotaku * engine.RiichiBou

    [<Property>]
    let ``和了者收、付家付，旁人分文不动`` (HoraCase(transfer, value)) =
        let score = Score.hora engine transfer value
        let tsumo = transfer.Actor = transfer.Target

        score.Deltas
        |> List.mapi (fun seat delta ->
            if seat = transfer.Actor then delta > 0
            elif tsumo || seat = transfer.Target then delta < 0
            else delta = 0)
        |> List.forall id

    [<Property>]
    let ``每一笔支付都是 100 的倍数`` (HoraCase(transfer, value)) =
        Score.hora engine transfer value
        |> fun score -> score.Deltas |> List.forall (fun delta -> delta % 100 = 0)

    [<Property>]
    let ``自摸时亲付的不少于子付的`` (HoraCase(transfer, value)) =
        let score = Score.hora engine transfer value

        if transfer.Actor <> transfer.Target || transfer.Actor = transfer.Oya then
            true
        else
            let paid (seat: Seat) = -List.item seat score.Deltas

            Seat.all engine
            |> List.filter (fun seat -> seat <> transfer.Actor && seat <> transfer.Oya)
            |> List.forall (fun ko -> paid transfer.Oya >= paid ko)

    [<Property>]
    let ``和了点随番与符单调不减`` (HoraCase(transfer, value)) =
        let points (value: HoraValue) =
            (Score.hora engine transfer value).HoraPoints

        points value <= points { value with Han = value.Han + 1 }
        && points value <= points { value with Fu = value.Fu + 10 }

    [<Property>]
    let ``切上满贯只动基本点 1920 那一档`` (HoraCase(_, value)) =
        let kiriage = Ruleset.withKiriageMangan engine
        let plain = Score.basePoints engine value
        let raised = Score.basePoints kiriage value

        if plain = 1920 then raised = 2000 else raised = plain

    // ---- 一局之内 ----

    [<Property>]
    let ``任一步之后，四家点数与场上供托之和不变`` (state: GameState) =
        // 供托在和了那一步被和了者收走，此后它不再「在场上」——两种写法加起来恒等于局初。
        let onTable =
            if List.isEmpty (GameState.horas state) then
                context.Kyotaku * engine.RiichiBou
            else
                0

        List.sum (GameState.scores state) + onTable = List.sum context.Scores + context.Kyotaku * engine.RiichiBou

    [<Property>]
    let ``带本场与供托开局时，一整局的每一步都守恒`` (honba: int) (kyotaku: int) =
        let honba = ((honba % 6) + 6) % 6
        let kyotaku = ((kyotaku % 5) + 5) % 5

        let opening =
            { context with
                Honba = honba
                Kyotaku = kyotaku
            }

        let sticks = kyotaku * engine.RiichiBou

        [ tsumoHoraScript; ronFuritenScript; doubleRonScript; noYakuRonScript ]
        |> List.collect (fun script -> startScriptedIn engine opening script |> traceFrom horaSeeking (Rng.ofSeed 1))
        |> List.forall (fun state ->
            let onTable = if List.isEmpty (GameState.horas state) then sticks else 0

            List.sum (GameState.scores state) + onTable = List.sum opening.Scores + sticks)

    [<Property>]
    let ``和了事件里的点数与授受自洽`` (state: GameState) =
        GameState.horas state
        |> List.forall (fun hora ->
            let paid =
                hora.Deltas
                |> List.mapi (fun seat delta -> if seat = hora.Actor then 0 else -delta)
                |> List.sum

            // 收到的 = 付出去的 + 供托；和了点是其中不含本场与供托的那部分。
            List.item hora.Actor hora.Deltas = paid + context.Kyotaku * engine.RiichiBou
            && hora.HoraPoints <= paid
            && hora.Fu % 5 = 0
            && hora.Fan > 0)
