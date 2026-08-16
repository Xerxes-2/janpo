namespace Janpo.Web

open Browser.WebStorage
open Janpo

/// 浏览器本地存储：**API key 唯一的落脚点**。
///
/// spec 与 ADR-0003 的那条硬约束落在这里——key 只存在这台浏览器里，请求由它直发 provider，
/// 本平台没有后端可以收它，牌谱与任何可分享物里也不会出现它（`Paifu` 里根本没有这个字段）。
///
/// **一个字段一个键，不存 JSON**：字段少、都是字符串，JSON 只会多一层「读不动怎么办」。
/// 读不出来 / 读到垃圾一律退回默认值（`LlmSeat.edit` 本来就拒绝读不懂的值）。
[<RequireQualifiedAccess>]
module Store =

    let private prefix = "janpo.llm."

    let private read (key: string) : string option =
        match localStorage.getItem (prefix + key) with
        | null -> None
        | value -> Some value

    let private write (key: string) (value: string) : unit =
        localStorage.setItem (prefix + key, value)

    // ---- 座位 ----

    /// 哪个座位交给 LLM。空串表示「四家都随机」。
    let readSeat (ruleset: Ruleset) : Seat option =
        read "seat"
        |> Option.bind (fun value ->
            match System.Int32.TryParse value with
            | true, index -> Seat.all ruleset |> List.tryFind (fun seat -> Seat.index seat = index)
            | false, _ -> None)

    let writeSeat (seat: Seat option) : unit =
        seat
        |> Option.map (Seat.index >> string)
        |> Option.defaultValue ""
        |> write "seat"

    // ---- 座位配置 ----

    /// 配置面板里那几个字段。没存过的字段用默认值（`LlmSeat.initial`），
    /// 存了但读不懂的也一样（`LlmSeat.edit` 本来就拒绝读不懂的值）。
    let readSeatConfig () : LlmSeat =
        (LlmSeat.initial, LlmField.all)
        ||> List.fold (fun config field ->
            match read (LlmField.key field) with
            | Some value -> LlmSeat.edit field value config
            | None -> config)

    let writeSeatConfig (config: LlmSeat) : unit =
        for field in LlmField.all do
            write (LlmField.key field) (LlmSeat.field field config)
