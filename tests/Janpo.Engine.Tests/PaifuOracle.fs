namespace Janpo.Engine.Tests

open System.Text.RegularExpressions
open Thoth.Json.Core
open Thoth.Json.Newtonsoft
open Janpo

/// 天凤官方 JSON 里的一行「役」。**三种宝牌在天凤是役行，在我们的 `Yaku` 里不是**，
/// 因此单列出来——对拍时它们与 `YakuTally` 的 `Dora` / `Uradora` / `Akadora` 三个计数比。
[<RequireQualifiedAccess>]
type OracleYaku =
    | Yaku of yaku: Yaku
    | Dora
    | Uradora
    | Akadora

/// 日文役名 ↔ `Yaku` 的对照表。
///
/// oracle 给的是 `"立直(1飜)"` `"役牌 白(1飜)"` `"四暗刻(役満)"` 这种文本串，
/// 而我们的 `Yaku` 是 DU。**`japanese` 写成穷尽 match，因此「表漏了某个役」
/// 由编译器保证不可能发生**（`Yaku` 加 case 时这里当面报错）；反向的 `parse`
/// 由 `all` 列出的具体值倒推，两个方向不会漂移。
///
/// **`japanese` 只写天凤的那一种写法，其余写法进 `aliases`**：雀魂牌谱经 tensoul
/// 转成天凤 JSON 后，役名几乎逐字对得上，只有一处不同。
[<RequireQualifiedAccess>]
module OracleYaku =

    // ---- 日文役名 ----

    /// 表里写死的记法不合法就是本文件写错了，直接让测试失败。
    let private tile (notation: string) : Tile =
        match Tile.parse notation with
        | Ok tile -> tile
        | Error error -> failwith $"对照表里的记法 {notation} 不合法：{TileParseError.toDisplay error}"

    let private kazeJapanese (kaze: Kaze) : string =
        match kaze with
        | Ton -> "東"
        | Nan -> "南"
        | Shaa -> "西"
        | Pei -> "北"

    /// 三元牌的日文名。役牌只可能是三元牌，其余牌种走不到（给 mjai 记法当兜底，不抛）。
    let private sangenJapanese (pai: Tile) : string =
        match Tile.suit pai, Tile.number pai with
        | Jihai, 5 -> "白"
        | Jihai, 6 -> "發"
        | Jihai, 7 -> "中"
        | _ -> Tile.toMjai pai

    /// `Yaku` → 天凤 JSON 的役名。**穷尽 match**：`Yaku` 加 case 时编译器会指出这里要补。
    let japanese (yaku: Yaku) : string =
        match yaku with
        | Yaku.Riichi -> "立直"
        | Yaku.DoubleRiichi -> "両立直"
        | Yaku.Ippatsu -> "一発"
        | Yaku.MenzenTsumo -> "門前清自摸和"
        | Yaku.Pinfu -> "平和"
        | Yaku.Iipeiko -> "一盃口"
        | Yaku.Tanyao -> "断幺九"
        | Yaku.Yakuhai pai -> "役牌 " + sangenJapanese pai
        | Yaku.Bakaze bakaze -> "場風 " + kazeJapanese bakaze
        | Yaku.Jikaze jikaze -> "自風 " + kazeJapanese jikaze
        | Yaku.Haitei -> "海底摸月"
        | Yaku.Houtei -> "河底撈魚"
        | Yaku.Rinshan -> "嶺上開花"
        | Yaku.Chankan -> "槍槓"
        | Yaku.Chanta -> "混全帯幺九"
        | Yaku.SanshokuDoujun -> "三色同順"
        | Yaku.Ittsuu -> "一気通貫"
        | Yaku.Toitoi -> "対々和"
        | Yaku.Sanankou -> "三暗刻"
        | Yaku.SanshokuDoukou -> "三色同刻"
        | Yaku.Sankantsu -> "三槓子"
        | Yaku.Chiitoitsu -> "七対子"
        | Yaku.Honroutou -> "混老頭"
        | Yaku.Shousangen -> "小三元"
        | Yaku.Honitsu -> "混一色"
        | Yaku.Junchan -> "純全帯幺九"
        | Yaku.Ryanpeikou -> "二盃口"
        | Yaku.Chinitsu -> "清一色"
        | Yaku.Kokushi -> "国士無双"
        | Yaku.KokushiJuusanmen -> "国士無双１３面"
        | Yaku.Suuankou -> "四暗刻"
        | Yaku.SuuankouTanki -> "四暗刻単騎"
        | Yaku.Daisangen -> "大三元"
        | Yaku.Shousuushii -> "小四喜"
        | Yaku.Daisuushii -> "大四喜"
        | Yaku.Tsuuiisou -> "字一色"
        | Yaku.Chinroutou -> "清老頭"
        | Yaku.Ryuuiisou -> "緑一色"
        | Yaku.Chuuren -> "九蓮宝燈"
        | Yaku.JunseiChuuren -> "純正九蓮宝燈"
        | Yaku.Suukantsu -> "四槓子"
        | Yaku.Tenhou -> "天和"
        | Yaku.Chiihou -> "地和"

    /// 对照表覆盖到的**具体**役值：三个带参数的 case 各自展开（役牌三种、场风与自风各四种）。
    /// 反向解析与覆盖率统计都读它。
    let all: Yaku list =
        [
            Yaku.Riichi
            Yaku.DoubleRiichi
            Yaku.Ippatsu
            Yaku.MenzenTsumo
            Yaku.Pinfu
            Yaku.Iipeiko
            Yaku.Tanyao
            Yaku.Haitei
            Yaku.Houtei
            Yaku.Rinshan
            Yaku.Chankan
            Yaku.Chanta
            Yaku.SanshokuDoujun
            Yaku.Ittsuu
            Yaku.Toitoi
            Yaku.Sanankou
            Yaku.SanshokuDoukou
            Yaku.Sankantsu
            Yaku.Chiitoitsu
            Yaku.Honroutou
            Yaku.Shousangen
            Yaku.Honitsu
            Yaku.Junchan
            Yaku.Ryanpeikou
            Yaku.Chinitsu
            Yaku.Kokushi
            Yaku.KokushiJuusanmen
            Yaku.Suuankou
            Yaku.SuuankouTanki
            Yaku.Daisangen
            Yaku.Shousuushii
            Yaku.Daisuushii
            Yaku.Tsuuiisou
            Yaku.Chinroutou
            Yaku.Ryuuiisou
            Yaku.Chuuren
            Yaku.JunseiChuuren
            Yaku.Suukantsu
            Yaku.Tenhou
            Yaku.Chiihou
        ]
        @ [ for notation in [ "5z"; "6z"; "7z" ] -> Yaku.Yakuhai(tile notation) ]
        @ [ for kaze in Kaze.all -> Yaku.Bakaze kaze ]
        @ [ for kaze in Kaze.all -> Yaku.Jikaze kaze ]

    /// 天凤的三种宝牌役行。它们不是 `Yaku`。雀魂的 `name_jp` 这三个逐字相同。
    let doraNames: (string * OracleYaku) list =
        [ "ドラ", OracleYaku.Dora; "裏ドラ", OracleYaku.Uradora; "赤ドラ", OracleYaku.Akadora ]

    /// 同一个役的别名：雀魂牌谱（经 tensoul 转成天凤 JSON）的写法与天凤不同的那几条。
    ///
    /// 雀魂 `fan` 表里四麻用得上的役名逐条对过，**只有 id 49 跟天凤不一样**：
    /// 它的 `name_jp` 是 `国士無双十三面待ち`，天凤写 `国士無双１３面`（全角数字）。
    /// 另两处本来也不一样，但 tensoul 自己已经改写成天凤写法，不必补：id 10 / 11
    /// （`役牌:自風牌` / `役牌:場風牌`）已展开成 `自風 東` / `場風 東`，
    /// id 18（`ダブル立直`）已改写成 `両立直`。
    ///
    /// **双倍役满在串里看不出来**：tensoul 把役满行一律写成 `(役満)`，雀魂那四个
    /// `yiman = 2` 的役（`Ruleset.DoubleYakuman`）也不例外——倍数只能从点数看。
    /// 真拿雀魂牌谱对拍得先处理这一条，否则 `Yakuman` 会少一倍。
    let aliases: (string * OracleYaku) list =
        [ "国士無双十三面待ち", OracleYaku.Yaku Yaku.KokushiJuusanmen ]

    /// 天凤的写法排在前面：反向解析与 `japanese` 不会因为加了别名而漂移。
    let private table: (string * OracleYaku) list =
        (all |> List.map (fun yaku -> japanese yaku, OracleYaku.Yaku yaku))
        @ doraNames
        @ aliases

    /// 日文役名 → 役或宝牌；表里没有的返回 None（**样本因此被显式跳过并计数**）。
    let parse (name: string) : OracleYaku option =
        table |> List.tryFind (fun (each, _) -> each = name) |> Option.map snd

/// oracle 给出的一次和了：天凤自己写的役、符、番与点数。
type OracleHora =
    {
        Actor: Seat
        /// 放铳的座位；自摸时等于 `Actor`（与 mjai 同一约定）。
        Target: Seat
        /// 符。**満貫以上天凤不写符**，那时是 None（对拍时那几局不比符）。
        Fu: int option
        /// 番（含宝牌）。役满时是 None——役满不看番。
        Han: int option
        /// 役满倍数；不是役满时 0。
        Yakuman: int
        /// 和了点：**不含**本场与供托，与 `HoraScore.HoraPoints` 同义。
        HoraPoints: int
        /// 役与它的价值，按 oracle 写的顺序，**不含**三种宝牌。
        /// 用的就是引擎的 `YakuValue`，因此对拍时两边是同一个类型。
        Yaku: (Yaku * YakuValue) list
        Dora: int
        Uradora: int
        Akadora: int
        /// 本次授受，按座位升序。与 mjai 流里的 `hora.deltas` 逐项相等（本票会验）。
        Deltas: int list
    }

/// oracle 给出的一次流局：形态、逐座位听牌与授受。
type OracleRyuukyoku =
    {
        Reason: RyuukyokuReason
        /// 逐座位听牌。天凤只在**荒牌流局**这一种形态下说得清（`流局` 由钱流反推、
        /// `全員聴牌` / `全員不聴` 直说），其余形态是 None。
        Tenpais: bool list option
        Deltas: int list
    }

/// 一局的结局，天凤自己的说法。
[<RequireQualifiedAccess>]
type OracleResult =
    /// 和了。双响时两条，按天凤写的顺序（与 mjai 流的顺序一致）。
    | Hora of horas: OracleHora list
    | Ryuukyoku of ryuukyoku: OracleRyuukyoku

/// oracle 里的一局。头三项用来与 mjai 流对齐。
type OracleKyoku =
    {
        /// 这一局在整场里的序号，0 起：`场风序号 × 座位数 + 局数 - 1`。
        Index: int
        Honba: int
        Kyotaku: int
        Result: OracleResult
    }

/// 一整场对局的 oracle。
type OracleGame =
    {
        Id: string
        /// `rule.disp`，如 `鳳南喰赤`。规则集断言读它。
        Rule: string
        Kyokus: OracleKyoku list
    }

/// 天凤官方 JSON（`tenhou.net/5/mjlog2json.cgi?<id>`）的解析。
///
/// **它只是 oracle**：本项目不消费它的牌谱本体（配牌 / 摸牌 / 打牌都从 mjai 流取），
/// 只读每局末尾的结果——役 / 符 / 番 / 点数 / 流局形态，正是 mjai 流被上游丢掉的那部分。
[<RequireQualifiedAccess>]
module PaifuOracle =

    // ---- 异构数组的解析 ----

    /// 天凤的一局是个异构数组（数、串与嵌套数组混排）。本票只读它的头与尾，
    /// 因此不给它建模，只解成一棵小树再按位置取。
    type private Cell =
        | Number of int
        | Text of string
        | Items of Cell list

    let private cellDecoder: Decoder<Cell> =
        Decode.fix (fun self ->
            Decode.oneOf
                [
                    Decode.int |> Decode.map Number
                    Decode.string |> Decode.map Text
                    Decode.list self |> Decode.map Items
                ])

    let private items (cell: Cell) : Cell list =
        match cell with
        | Items cells -> cells
        | Number _
        | Text _ -> []

    let private number (cell: Cell) : int option =
        match cell with
        | Number value -> Some value
        | Text _
        | Items _ -> None

    let private text (cell: Cell) : string option =
        match cell with
        | Text value -> Some value
        | Number _
        | Items _ -> None

    // ---- 结果串的解析 ----

    /// 天凤的点数串：`30符2飜2000点`（荣和）、`30符1飜300-500点`（子自摸）、
    /// `30符3飜2000点∀`（亲自摸）、`満貫12000点`（満貫以上不写符与番）。
    let private scorePattern =
        Regex(@"^(?:(\d+)符(\d+)飜)?[^0-9]*(\d+)(?:-(\d+))?点(∀?)$", RegexOptions.Compiled)

    /// 天凤的役行：`立直(1飜)` 或 `四暗刻(役満)`。
    let private yakuPattern =
        Regex(@"^(.+?)\((?:(\d+)飜|(ダブル)?役満)\)$", RegexOptions.Compiled)

    let private group (matched: Match) (index: int) : string option =
        let captured = matched.Groups.[index]

        if captured.Success && captured.Value <> "" then
            Some captured.Value
        else
            None

    /// 点数串 → （符, 番, 和了点）。付家的份数由座位数推出，不写死 2 与 3。
    let private parseScore (seatCount: int) (rendered: string) : Result<int option * int option * int, string> =
        let matched = scorePattern.Match rendered

        if not matched.Success then
            Error $"看不懂的点数串：{rendered}"
        else
            let first = group matched 3 |> Option.map int |> Option.defaultValue 0

            let horaPoints =
                match group matched 4 |> Option.map int, group matched 5 with
                // 子自摸：`a-b` 是「每个子付 a、亲付 b」。
                | Some fromOya, _ -> (seatCount - 2) * first + fromOya
                // 亲自摸：`a点∀` 是「三家各付 a」。
                | None, Some _ -> (seatCount - 1) * first
                | None, None -> first

            Ok(group matched 1 |> Option.map int, group matched 2 |> Option.map int, horaPoints)

    /// 役行 → （役或宝牌, 价值）。`ダブル役満` 记作两倍。
    let private parseYakuLine (rendered: string) : Result<OracleYaku * YakuValue, string> =
        let matched = yakuPattern.Match rendered

        if not matched.Success then
            Error $"看不懂的役行：{rendered}"
        else
            match OracleYaku.parse (matched.Groups.[1].Value) with
            | None -> Error $"对照表里没有这个役名：{rendered}"
            | Some yaku ->
                let value =
                    match group matched 2 with
                    | Some han -> YakuValue.Han(int han)
                    | None -> YakuValue.Yakuman(if group matched 3 |> Option.isSome then 2 else 1)

                Ok(yaku, value)

    // ---- 一局的结果 ----

    let private seatOf (index: int) : Result<Seat, string> =
        match Seat.ofIndex index with
        | Some seat -> Ok seat
        | None -> Error $"座位号 {index} 不合法"

    let private traverse (results: Result<'a, string> list) : Result<'a list, string> =
        (Ok [], results)
        ||> List.fold (fun acc each ->
            match acc, each with
            | Error error, _ -> Error error
            | Ok _, Error error -> Error error
            | Ok collected, Ok value -> Ok(value :: collected))
        |> Result.map List.rev

    let private hanOf (value: YakuValue) : int =
        match value with
        | YakuValue.Han han -> han
        | YakuValue.Yakuman _ -> 0

    /// 把读好的三块（座位、点数串、各役行）拼成一条 oracle。
    let private buildHora
        (deltas: int list)
        (actor: Seat, target: Seat)
        (fu: int option, han: int option, horaPoints: int)
        (lines: (OracleYaku * YakuValue) list)
        : OracleHora =
        let counted (which: OracleYaku) =
            lines
            |> List.sumBy (fun (yaku, value) -> if yaku = which then hanOf value else 0)

        let yakuman =
            lines
            |> List.sumBy (fun (_, value) ->
                match value with
                | YakuValue.Yakuman multiplier -> multiplier
                | YakuValue.Han _ -> 0)

        {
            Actor = actor
            Target = target
            Fu = fu
            // 満貫以上天凤不写番，那时由各役行求和（含宝牌行）；役满不看番。
            Han =
                if yakuman > 0 then
                    None
                else
                    han |> Option.orElse (Some(lines |> List.sumBy (snd >> hanOf)))
            Yakuman = yakuman
            HoraPoints = horaPoints
            Yaku =
                lines
                |> List.choose (fun (yaku, value) ->
                    match yaku with
                    | OracleYaku.Yaku each -> Some(each, value)
                    | OracleYaku.Dora
                    | OracleYaku.Uradora
                    | OracleYaku.Akadora -> None)
            Dora = counted OracleYaku.Dora
            Uradora = counted OracleYaku.Uradora
            Akadora = counted OracleYaku.Akadora
            Deltas = deltas
        }

    /// 一条 `["和了", deltas, [和了家; 放铳家; 包牌家; 点数串; 役行…]]`。
    /// 前三项是座位，第四项起全是文本（第一条是点数串，其余是役行）。
    let private parseHora (seatCount: int) (deltas: Cell) (detail: Cell) : Result<OracleHora, string> =
        let cells = items detail

        match cells |> List.truncate 2 |> List.map number, cells |> List.skip (min 3 (List.length cells)) with
        | [ Some who; Some from ], (Text rendered :: rest) ->
            let seats =
                seatOf who
                |> Result.bind (fun actor -> seatOf from |> Result.map (fun target -> actor, target))

            let lines = rest |> List.choose text |> List.map parseYakuLine |> traverse

            match seats, parseScore seatCount rendered, lines with
            | Ok seats, Ok score, Ok lines -> Ok(buildHora (items deltas |> List.choose number) seats score lines)
            | Error error, _, _
            | _, Error error, _
            | _, _, Error error -> Error error
        | _, _ -> Error "和了的详情读不出来（座位或点数串缺了）"

    /// 流し満貫的清算判据（**只有 mjai 流、没有 oracle 时用它**）：
    /// 听牌料只可能是 `{0, ±1000, ±1500, ±3000}`，而流し満貫是
    /// `{±12000, ±8000, ±4000, ±2000}`——出现绝对值 ≥ 4000 即是它。
    let isNagashiDeltas (deltas: int list) : bool =
        deltas |> List.exists (fun delta -> abs delta >= 4000)

    /// 荒牌流局的逐座位听牌由**天凤自己的钱流**反推：收听牌料的听牌、付的不听。
    /// 这就是本票的独立锚点——它与我们的实现无因果关系。
    ///
    /// 全 0（全听或全不听，也可能是途中流局）与流し満貫都反推不出听牌，那时是 None。
    let tenpaisFromDeltas (deltas: int list) : bool list option =
        if isNagashiDeltas deltas || deltas |> List.forall (fun delta -> delta = 0) then
            None
        else
            Some(deltas |> List.map (fun delta -> delta > 0))

    /// 一条流局：`["流局", deltas]` / `["全員聴牌"]` / `["九種九牌"]` …
    ///
    /// **两种写法同时认**：四杠散了与三家和了在雀魂牌谱（经 tensoul 转成天凤 JSON）里
    /// 写作 `四開槓` / `三家和`，其余五种形态两边逐字相同。雀魂本身不会出三家和
    /// （三响都成立，`Ruleset.majsoul` 就是这么配的），但字串还是认下来。
    ///
    /// **没有对应写法的是 `全員聴牌` / `全員不聴`**：雀魂全听与全不听时不给授受，
    /// tensoul 只写 `["流局", [0,0,0,0]]`，那时听牌逆推不出来（`tenpaisFromDeltas` 给 None）。
    let private parseRyuukyoku (seatCount: int) (form: string) (rest: Cell list) : Result<OracleRyuukyoku, string> =
        let deltas =
            match rest with
            | first :: _ ->
                match items first |> List.choose number with
                | [] -> List.replicate seatCount 0
                | values -> values
            | [] -> List.replicate seatCount 0

        let allSeats (tenpai: bool) = Some(List.replicate seatCount tenpai)

        match form with
        | "流局" -> Ok(Fanpai, tenpaisFromDeltas deltas)
        | "全員聴牌" -> Ok(Fanpai, allSeats true)
        | "全員不聴" -> Ok(Fanpai, allSeats false)
        | "流し満貫" -> Ok(NagashiMangan, None)
        | "九種九牌" -> Ok(KyuushuKyuuhai, None)
        | "四風連打" -> Ok(SuufonRenda, None)
        | "四槓散了"
        | "四開槓" -> Ok(Suukaikan, None)
        | "四家立直" -> Ok(SuuchaRiichi, None)
        | "三家和了"
        | "三家和" -> Ok(SanchaHora, None)
        | other -> Error $"看不懂的流局形态：{other}"
        |> Result.map (fun (reason, tenpais) ->
            {
                Reason = reason
                Tenpais = tenpais
                Deltas = deltas
            })

    /// 一局：`[[局号; 本场; 供托]; 各家点数; 表宝牌; 里宝牌; (配牌; 摸; 打)×4; 结果]`。
    /// 中间那 12 项是天凤自己的牌谱本体，本票不读——动作序列一律从 mjai 流取。
    let private parseKyoku (seatCount: int) (cells: Cell list) : Result<OracleKyoku, string> =
        match cells with
        | [] -> Error "空的一局"
        | header :: _ ->
            match items header |> List.truncate 3 |> List.map number, List.last cells |> items with
            | [ Some index; Some honba; Some kyotaku ], (Text form :: rest) ->
                (if form = "和了" then
                     rest
                     |> List.chunkBySize 2
                     |> List.map (fun chunk ->
                         match chunk with
                         | [ deltas; detail ] -> parseHora seatCount deltas detail
                         | _ -> Error "和了的详情少了一半")
                     |> traverse
                     |> Result.map OracleResult.Hora
                 else
                     parseRyuukyoku seatCount form rest |> Result.map OracleResult.Ryuukyoku)
                |> Result.map (fun result ->
                    {
                        Index = index
                        Honba = honba
                        Kyotaku = kyotaku
                        Result = result
                    })
            | _ -> Error "一局的头或结果读不出来"

    let private gameDecoder: Decoder<string * Cell list list> =
        Decode.map2
            (fun rule log -> rule, log |> List.map items)
            (Decode.at [ "rule"; "disp" ] Decode.string)
            (Decode.field "log" (Decode.list cellDecoder))

    /// 载入一份天凤 JSON oracle。`id` 就是天凤牌谱 id（固件的文件名）。
    let parse (seatCount: int) (id: string) (json: string) : Result<OracleGame, string> =
        match Decode.fromString gameDecoder json with
        | Error error -> Error $"oracle 解不开：{error}"
        | Ok(rule, log) ->
            log
            |> List.map (parseKyoku seatCount)
            |> traverse
            |> Result.map (fun kyokus ->
                {
                    Id = id
                    Rule = rule
                    Kyokus = kyokus
                })
