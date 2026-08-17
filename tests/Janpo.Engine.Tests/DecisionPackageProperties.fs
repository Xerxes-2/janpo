namespace Janpo.Engine.Tests

open FsCheck.FSharp
open FsCheck.Xunit
open Janpo
open Janpo.Engine.Tests.GameStateFixtures

/// 决策包的不变量。**这是 F#/TS 边界的那一半**：跨出去的是包，回来的是一个整数，
/// 因此「包里每个 id 都取得回一个引擎接受的动作」「包外的 id 一律 None」是硬约束。
[<Properties(Arbitrary = [| typeof<GameStateArbitraries> |], Parallelism = 4)>]
module DecisionPackageProperties =

    let private asked (state: GameState) : Seat list =
        GameState.legalActions state |> List.map (fun choice -> choice.Seat)

    let private packagesOf (state: GameState) : DecisionPackage list =
        asked state |> List.choose (fun seat -> DecisionPackage.forSeat seat state)

    // ---- 包里那两个字段的第三锚点（票 61） ----

    /// 一处分歧：哪个座位的包、哪条腿、点名的字段。
    let private divergence (seat: Seat) (leg: string) (field: string) : string = $"座位 {Seat.index seat} {leg}：{field}"

    /// 报错那一行，只报头几条——一个字段对不上之后往往整份都对不上，全抄出来读不动。
    let private toDisplay (found: string list) : string =
        let head = found |> List.truncate 5 |> String.concat "；"
        $"共 {List.length found} 处分歧，头几处是 {head}"

    /// 一份观测与**引擎的权威状态**逐字段对（`ObservationFixtures.mismatches`，票 60 新建）。
    ///
    /// **这是包里那两个字段唯一的第三锚点**：包的 `History` 与 `Observation` 都出自
    /// 同一条掩蔽流的同一个 fold（`DecisionPackage.forSeat` 里 `Observation.stream` 与
    /// `Observation.ofState`），拿其中一个去比另一个、或者比一份新求值的同源观测，
    /// 展开之后两侧是同一个表达式——**恒真式**，弄坏 `SeatStream.absorb` 或
    /// `MaskedEvent.forSeat` 一次都不红（票 60 §6 与本票的原始输出）。
    /// 引擎的 `GameState` **既不经过掩蔽也不经过 fold**，因此它坏不到一块去。
    let private anchored (seat: Seat) (state: GameState) (leg: string) (observation: Observation) : string list =
        ObservationFixtures.mismatches seat state observation
        |> List.map (divergence seat leg)

    [<Property>]
    let ``任意局面，正在被问的每个座位都有决策包，包里就是它的合法动作集`` (state: GameState) =
        let packages = packagesOf state

        List.length packages = List.length (asked state)
        && packages
           |> List.forall (fun package ->
               let expected =
                   GameState.legalActions state
                   |> List.tryFind (fun choice -> choice.Seat = DecisionPackage.seat package)
                   |> Option.map (fun choice -> choice.Actions)

               Some(DecisionPackage.options package |> List.map ActionOption.action) = expected)

    [<Property>]
    let ``任意局面，没被问到的座位没有决策包`` (state: GameState) =
        Seat.all ruleset
        |> List.filter (fun seat -> not (List.contains seat (asked state)))
        |> List.forall (fun seat -> Option.isNone (DecisionPackage.forSeat seat state))

    [<Property>]
    let ``任意局面，包里每个 id 都取得回一个引擎接受的动作`` (state: GameState) =
        packagesOf state
        |> List.forall (fun package ->
            DecisionPackage.options package
            |> List.forall (fun option ->
                match DecisionPackage.tryAction (ActionOption.id option) package with
                | None -> false
                | Some action ->
                    action = ActionOption.action option
                    && (GameState.step state action |> Result.isOk)))

    [<Property>]
    let ``任意局面，包外的 id 一律是 None 而不是异常`` (state: GameState) =
        // 兜底路径（23 号票）依赖这一条：LLM 给回来的整数什么都可能是。
        packagesOf state
        |> List.forall (fun package ->
            let count = DecisionPackage.options package |> List.length

            [ -1; count; count + 1; System.Int32.MaxValue; System.Int32.MinValue ]
            |> List.forall (fun id -> Option.isNone (DecisionPackage.tryAction id package)))

    [<Property>]
    let ``任意局面，同一包里两条动作的 label 不相同`` (state: GameState) =
        // 亮哪几张是选手的决策（手里 `5m 5m 5mr` 碰 `5m` 有两种亮法，宝牌数不同），
        // 因此 label 必须把它们分开——LLM 与真人看的都是这一行字。
        packagesOf state
        |> List.forall (fun package ->
            let labels = DecisionPackage.options package |> List.map ActionOption.label

            List.length (List.distinct labels) = List.length labels)

    [<Property>]
    let ``任意局面，包里的历史 fold 出来的就是包里的那份观测`` (state: GameState) =
        // **两种形态构造性一致**（票 29b 第零节）：前缀是掩蔽事件流，尾部是它的 fold。
        // 对不上就意味着模型看到的是自相矛盾的输入，而我们无从判断该信哪个。
        //
        // **「两者相同」不够**（票 61）：两侧是同一个 fold，一起错就一起对得上。
        // 因此尾部那份观测还要钉在引擎的权威状态上——那一侧不掩蔽、不 fold。
        let found =
            packagesOf state
            |> List.collect (fun package ->
                let seat = DecisionPackage.seat package
                let observation = DecisionPackage.observation package

                let folded = DecisionPackage.history package |> Observation.ofMasked ruleset seat

                let sameFold =
                    if folded = Some observation then
                        []
                    else
                        [ divergence seat "历史 fold vs 包里的观测" "整份观测" ]

                sameFold @ anchored seat state "包里的观测 vs 引擎状态" observation)

        List.isEmpty found |> Prop.label (toDisplay found)

    [<Property>]
    let ``任意局面，包里的历史就是那条唯一的掩蔽流`` (state: GameState) =
        // 没有第二处判断「某座位能看见什么」（29a 的一条掩蔽法则）。
        //
        // **右侧不是另一份同源的流**（票 61）：与那条唯一的掩蔽法则对，只证明包没把
        // 两个字段建在不同的座位或不同的局面上；掩蔽法则自己坏了，两侧一起坏。
        // 因此还要把这条流 **fold 出来的观测**钉在引擎的权威状态上。
        let found =
            packagesOf state
            |> List.collect (fun package ->
                let seat = DecisionPackage.seat package
                let history = DecisionPackage.history package

                let sameStream =
                    if history = Observation.stream seat state then
                        []
                    else
                        [ divergence seat "包里的历史 vs 唯一那条掩蔽流" "整条流" ]

                let leg = "包里的历史 fold 出来的观测 vs 引擎状态"

                // 座位不在规则集里时 fold 不出观测——但包存在就说明引擎正在问这个座位，因此那也是一处分歧。
                let foldsToObservation =
                    match Observation.ofMasked ruleset seat history with
                    | None -> [ divergence seat leg "观测缺席" ]
                    | Some folded -> anchored seat state leg folded

                sameStream @ foldsToObservation)

        List.isEmpty found |> Prop.label (toDisplay found)
