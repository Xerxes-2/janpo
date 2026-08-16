namespace Janpo.Golden

open Thoth.Json.Core
open Janpo

/// 一条用例所依据的规则集。
///
/// ADR-0004 的要求：**每条涉及符与点数的黄金用例必须注明所依据的规则集**，
/// 否则对拍出现差异时分不清「实现错」与「规则集配错」。因此它挂在每条用例上，
/// 而不是整份文件一个全局设定。数据里省略的项按四麻默认预设补齐，
/// `janpo golden write` 再把补齐后的值写回文件——于是文件里的每条用例都注明了规则集。
type GoldenRuleset =
    {
        /// 红宝牌（关掉即 `Ruleset.withoutAkadora`）。
        Akadora: bool
        /// 食断。
        Kuitan: bool
        /// 切上满贯。
        Kiriage: bool
        /// 头跳。
        Atamahane: bool
        /// 对局长度。
        Length: GameLength
    }

/// 「一副和了牌姿 → 役 / 符 / 点数」的用例输入。
///
/// 各字段的形态与 `janpo yaku` 的选项**故意一致**：一条用例照抄成一行 CLI 就能手工复现。
type GoldenHora =
    {
        /// 暗牌记法，含和了牌。
        Concealed: string
        /// 副露，每条形如「pon 5z 5z 5z」，第一张是鸣来的那张（加杠是加上去的那张）。
        Naki: string list
        /// 和了牌，要包含在暗牌里。
        Winning: string
        /// 场风（`1z`-`4z`）。
        Bakaze: string
        /// 自风（`1z`-`4z`）。自风为 `1z` 时和了者就是亲。
        Jikaze: string
        /// 表宝牌指示牌。
        Dora: string list
        /// 里宝牌指示牌。
        Ura: string list
        /// 上下文标志：`tsumo` / `riichi` / `double-riichi` / `ippatsu` / `rinshan` /
        /// `haitei` / `houtei` / `chankan` / `tenhou` / `chiihou`。
        /// 不认识的标志跑出来是一条 `error` 字段，因此数据里写错了会红。
        Flags: string list
        /// 本场。
        Honba: int
        /// 供托（立直棒根数）。
        Kyotaku: int
    }

/// 「符 + 番 → 级别 / 点数 / 授受」的用例输入。
///
/// **整数除法与取整全在这条路上**：每笔支付各自切上到 100、跳满的 3/2、
/// 本场在付家之间的平摊。数值语义要漂，这里最先漂。
type GoldenPoints =
    {
        /// 符（已切上）。
        Fu: int
        /// 番。
        Han: int
        /// 役满倍数，0 表示不是役满。
        Yakuman: int
        /// 和了者的座位序号。
        Actor: int
        /// 放铳者的座位序号；**自摸时等于 `Actor`**。
        Target: int
        /// 亲的座位序号。
        Oya: int
        /// 本场。
        Honba: int
        /// 供托（立直棒根数）。
        Kyotaku: int
        /// 责任支付（包）的座位序号，没有则省略。
        Sekinin: int option
    }

/// 一条用例跑的是什么。每个 case 自带它要的输入，**输入只在数据里，不在代码里**。
///
/// `[<RequireQualifiedAccess>]` 是必需的：`Hora` / `Game` / `Notation` 这些名字
/// 在 `Janpo` 命名空间里另有其物，不加就会打架。
[<RequireQualifiedAccess>]
type GoldenRun =
    /// Rng 的取数序列与洗牌结果。牌山的一切随机性都从这里来。
    | Rng of seed: int * bound: int * count: int
    /// 牌山：洗好之后的头几张、宝牌指示牌、岭上牌与四家配牌。
    | Wall of seed: int * count: int
    /// 牌记法的解析、规范形与往返。
    | Notation of notation: string
    /// 集合与 Map 的遍历顺序：同一串牌进 `Set` / `Map` / `groupBy` 之后的次序。
    | Collection of notation: string
    /// 向听数、和了型与有效牌（34 长计数数组那条路）。
    | Shanten of nakiCount: int * notation: string
    /// 一副和了牌姿的役、符与点数。
    | Hora of hora: GoldenHora
    /// 点数表与授受。
    | Points of points: GoldenPoints
    /// 决策包：把一局推到第 N 手，取那一手的决策包 JSON（跨 F#/TS 边界的那个包，票 20）。
    | Decide of seed: int * steps: int * seat: int option
    /// 一局：四个随机选手把这一局打到终，逐条事件 + 终了点数与顺位。
    | Kyoku of seed: int
    /// 一整场：局数序列走完，逐条事件 + 终局精算。
    | Game of seed: int

/// 一条黄金用例。
type GoldenCase =
    {
        /// 用例 id，报错时指的就是它。文件内唯一。
        Id: string
        /// 这条用例盯的是什么。**一句话写在数据里**，不写在代码里。
        Note: string
        /// 所依据的规则集（ADR-0004）。
        Ruleset: GoldenRuleset
        /// 跑什么。
        Run: GoldenRun
        /// 期望：字段名 → 逐行的值。标量就是一行。
        ///
        /// 是有序的键值对而不是 `Map`：这份数据自己不该依赖集合的遍历顺序——
        /// 那正是被它测的东西之一。
        Expect: (string * string list) list
    }

/// 一份黄金用例文件。
type GoldenSuite =
    {
        /// 整份文件的说明。
        Note: string
        /// 用例，按文件里的顺序。
        Cases: GoldenCase list
    }

/// 规则集配置的摊开与编解码。
[<RequireQualifiedAccess>]
module GoldenRuleset =

    // ---- 构造 ----

    /// 四麻默认预设对应的那一组开关。数据里省略的项按它补齐。
    let standard: GoldenRuleset =
        {
            Akadora = true
            Kuitan = true
            Kiriage = false
            Atamahane = false
            Length = Ruleset.yonma.Length
        }

    /// 摊开成引擎的 `Ruleset`。**只从 `Ruleset.yonma` 出发**：预设是唯一出处（ADR-0004）。
    let value (spec: GoldenRuleset) : Ruleset =
        Ruleset.yonma
        |> (if spec.Akadora then id else Ruleset.withoutAkadora)
        |> (if spec.Kuitan then id else Ruleset.withoutKuitan)
        |> (if spec.Kiriage then Ruleset.withKiriageMangan else id)
        |> (if spec.Atamahane then Ruleset.withAtamahane else id)
        |> Ruleset.withLength spec.Length

    // ---- JSON ----

    let private lengthName (length: GameLength) : string =
        match length with
        | Tonpuusen -> "tonpuusen"
        | Hanchan -> "hanchan"

    let private lengthDecoder: Decoder<GameLength> =
        Decode.string
        |> Decode.andThen (fun text ->
            match GameLength.all |> List.tryFind (fun length -> lengthName length = text) with
            | Some length -> Decode.succeed length
            | None -> Decode.fail ("unknown game length: " + text))

    let encoder: Encoder<GoldenRuleset> =
        fun spec ->
            Encode.object
                [
                    "akadora", Encode.bool spec.Akadora
                    "kuitan", Encode.bool spec.Kuitan
                    "kiriage", Encode.bool spec.Kiriage
                    "atamahane", Encode.bool spec.Atamahane
                    "length", Encode.string (lengthName spec.Length)
                ]

    let decoder: Decoder<GoldenRuleset> =
        Decode.object (fun get ->
            let flag (name: string) (fallback: bool) =
                get.Optional.Field name Decode.bool |> Option.defaultValue fallback

            {
                Akadora = flag "akadora" standard.Akadora
                Kuitan = flag "kuitan" standard.Kuitan
                Kiriage = flag "kiriage" standard.Kiriage
                Atamahane = flag "atamahane" standard.Atamahane
                Length = get.Optional.Field "length" lengthDecoder |> Option.defaultValue standard.Length
            })

/// 和了牌姿输入的编解码。
[<RequireQualifiedAccess>]
module GoldenHora =

    // ---- JSON ----

    let encoder: Encoder<GoldenHora> =
        fun spec ->
            Encode.object
                [
                    "concealed", Encode.string spec.Concealed
                    "naki", spec.Naki |> List.map Encode.string |> Encode.list
                    "win", Encode.string spec.Winning
                    "bakaze", Encode.string spec.Bakaze
                    "jikaze", Encode.string spec.Jikaze
                    "dora", spec.Dora |> List.map Encode.string |> Encode.list
                    "ura", spec.Ura |> List.map Encode.string |> Encode.list
                    "flags", spec.Flags |> List.map Encode.string |> Encode.list
                    "honba", Encode.int spec.Honba
                    "kyotaku", Encode.int spec.Kyotaku
                ]

    let decoder: Decoder<GoldenHora> =
        Decode.object (fun get ->
            let strings (name: string) =
                get.Optional.Field name (Decode.list Decode.string) |> Option.defaultValue []

            let number (name: string) =
                get.Optional.Field name Decode.int |> Option.defaultValue 0

            {
                Concealed = get.Required.Field "concealed" Decode.string
                Naki = strings "naki"
                Winning = get.Required.Field "win" Decode.string
                Bakaze = get.Optional.Field "bakaze" Decode.string |> Option.defaultValue "1z"
                Jikaze = get.Optional.Field "jikaze" Decode.string |> Option.defaultValue "1z"
                Dora = strings "dora"
                Ura = strings "ura"
                Flags = strings "flags"
                Honba = number "honba"
                Kyotaku = number "kyotaku"
            })

/// 点数表输入的编解码。
[<RequireQualifiedAccess>]
module GoldenPoints =

    // ---- JSON ----

    let encoder: Encoder<GoldenPoints> =
        fun spec ->
            let sekinin =
                match spec.Sekinin with
                | Some seat -> [ "sekinin", Encode.int seat ]
                | None -> []

            Encode.object (
                [
                    "fu", Encode.int spec.Fu
                    "han", Encode.int spec.Han
                    "yakuman", Encode.int spec.Yakuman
                    "actor", Encode.int spec.Actor
                    "target", Encode.int spec.Target
                    "oya", Encode.int spec.Oya
                    "honba", Encode.int spec.Honba
                    "kyotaku", Encode.int spec.Kyotaku
                ]
                @ sekinin
            )

    let decoder: Decoder<GoldenPoints> =
        Decode.object (fun get ->
            let number (name: string) =
                get.Optional.Field name Decode.int |> Option.defaultValue 0

            {
                Fu = number "fu"
                Han = number "han"
                Yakuman = number "yakuman"
                Actor = number "actor"
                Target = number "target"
                Oya = number "oya"
                Honba = number "honba"
                Kyotaku = number "kyotaku"
                Sekinin = get.Optional.Field "sekinin" Decode.int
            })

/// 用例输入的编解码。加一种用例：加一个 case、在这两处各加一支。
[<RequireQualifiedAccess>]
module GoldenRun =

    // ---- 拆解 ----

    /// wire 上的 `kind`。报告里也用它给用例分类。
    let kind (run: GoldenRun) : string =
        match run with
        | GoldenRun.Rng _ -> "rng"
        | GoldenRun.Wall _ -> "wall"
        | GoldenRun.Notation _ -> "notation"
        | GoldenRun.Collection _ -> "collection"
        | GoldenRun.Shanten _ -> "shanten"
        | GoldenRun.Hora _ -> "hora"
        | GoldenRun.Points _ -> "points"
        | GoldenRun.Decide _ -> "decide"
        | GoldenRun.Kyoku _ -> "kyoku"
        | GoldenRun.Game _ -> "game"

    // ---- JSON ----

    let encoder: Encoder<GoldenRun> =
        fun run ->
            let fields =
                match run with
                | GoldenRun.Rng(seed, bound, count) ->
                    [
                        "seed", Encode.int seed
                        "bound", Encode.int bound
                        "count", Encode.int count
                    ]
                | GoldenRun.Wall(seed, count) -> [ "seed", Encode.int seed; "count", Encode.int count ]
                | GoldenRun.Notation notation
                | GoldenRun.Collection notation -> [ "tiles", Encode.string notation ]
                | GoldenRun.Shanten(nakiCount, notation) ->
                    [ "naki", Encode.int nakiCount; "tiles", Encode.string notation ]
                | GoldenRun.Hora hora -> [ "hora", GoldenHora.encoder hora ]
                | GoldenRun.Points points -> [ "points", GoldenPoints.encoder points ]
                | GoldenRun.Decide(seed, steps, seat) ->
                    [ "seed", Encode.int seed; "steps", Encode.int steps ]
                    @ (match seat with
                       | Some index -> [ "seat", Encode.int index ]
                       | None -> [])
                | GoldenRun.Kyoku seed
                | GoldenRun.Game seed -> [ "seed", Encode.int seed ]

            Encode.object (("kind", Encode.string (kind run)) :: fields)

    let decoder: Decoder<GoldenRun> =
        let seeded (make: int -> GoldenRun) =
            Decode.field "seed" Decode.int |> Decode.map make

        let notated (make: string -> GoldenRun) =
            Decode.field "tiles" Decode.string |> Decode.map make

        Decode.field "kind" Decode.string
        |> Decode.andThen (fun kind ->
            match kind with
            | "rng" ->
                Decode.map3
                    (fun seed bound count -> GoldenRun.Rng(seed, bound, count))
                    (Decode.field "seed" Decode.int)
                    (Decode.field "bound" Decode.int)
                    (Decode.field "count" Decode.int)
            | "wall" ->
                Decode.map2
                    (fun seed count -> GoldenRun.Wall(seed, count))
                    (Decode.field "seed" Decode.int)
                    (Decode.field "count" Decode.int)
            | "notation" -> notated GoldenRun.Notation
            | "collection" -> notated GoldenRun.Collection
            | "shanten" ->
                Decode.map2
                    (fun nakiCount notation -> GoldenRun.Shanten(nakiCount, notation))
                    (Decode.field "naki" Decode.int)
                    (Decode.field "tiles" Decode.string)
            | "hora" -> Decode.field "hora" GoldenHora.decoder |> Decode.map GoldenRun.Hora
            | "points" -> Decode.field "points" GoldenPoints.decoder |> Decode.map GoldenRun.Points
            | "decide" ->
                Decode.object (fun get ->
                    GoldenRun.Decide(
                        get.Required.Field "seed" Decode.int,
                        get.Optional.Field "steps" Decode.int |> Option.defaultValue 0,
                        get.Optional.Field "seat" Decode.int
                    ))
            | "kyoku" -> seeded GoldenRun.Kyoku
            | "game" -> seeded GoldenRun.Game
            | unknown -> Decode.fail ("unknown golden case kind: " + unknown))

/// 一条用例的编解码。
[<RequireQualifiedAccess>]
module GoldenCase =

    // ---- JSON ----

    let encoder: Encoder<GoldenCase> =
        fun case ->
            let expected =
                case.Expect
                |> List.map (fun (name, lines) -> name, lines |> List.map Encode.string |> Encode.list)

            Encode.object
                [
                    "id", Encode.string case.Id
                    "note", Encode.string case.Note
                    "ruleset", GoldenRuleset.encoder case.Ruleset
                    "run", GoldenRun.encoder case.Run
                    "expect", Encode.object expected
                ]

    let decoder: Decoder<GoldenCase> =
        Decode.object (fun get ->
            {
                Id = get.Required.Field "id" Decode.string
                Note = get.Optional.Field "note" Decode.string |> Option.defaultValue ""
                Ruleset =
                    get.Optional.Field "ruleset" GoldenRuleset.decoder
                    |> Option.defaultValue GoldenRuleset.standard
                Run = get.Required.Field "run" GoldenRun.decoder
                Expect =
                    get.Optional.Field "expect" (Decode.keyValuePairs (Decode.list Decode.string))
                    |> Option.defaultValue []
            })

/// 整份用例文件的编解码。
[<RequireQualifiedAccess>]
module GoldenSuite =

    // ---- JSON ----

    let encoder: Encoder<GoldenSuite> =
        fun suite ->
            Encode.object
                [
                    "note", Encode.string suite.Note
                    "cases", suite.Cases |> List.map GoldenCase.encoder |> Encode.list
                ]

    let decoder: Decoder<GoldenSuite> =
        Decode.object (fun get ->
            {
                Note = get.Optional.Field "note" Decode.string |> Option.defaultValue ""
                Cases = get.Required.Field "cases" (Decode.list GoldenCase.decoder)
            })
