namespace Janpo

open Thoth.Json.Core

/// 风。场风（mjai 的 `bakaze`）与自风共用同一个类型；自风由座位与局数推导，
/// 不是座位本身的属性（CONTEXT.md）。
///
/// mjai wire 上风就是一张风牌的记法，本项目取 ADR-0001 的 `1z`-`4z`
/// （mjai 生态原生写 `E` / `S` / `W` / `N`，那套记法已被 ADR-0001 否决）。
type Kaze =
    | Ton
    | Nan
    | Shaa
    | Pei

/// 风的构造、拆解与 mjai 记法。
[<RequireQualifiedAccess>]
module Kaze =

    // ---- 构造 ----

    /// 四种风，按 东 → 南 → 西 → 北，即座位的推进方向。
    let all: Kaze list = [ Ton; Nan; Shaa; Pei ]

    /// 由 0 起的序号构造：0 = Ton … 3 = Pei；越界返回 None。
    let ofIndex (index: int) : Kaze option = List.tryItem index all

    // ---- 拆解 ----

    /// 0 起的序号：Ton = 0 … Pei = 3。场风的推进与自风的推导都以它为准。
    let index (kaze: Kaze) : int =
        match kaze with
        | Ton -> 0
        | Nan -> 1
        | Shaa -> 2
        | Pei -> 3

    // ---- mjai 记法 ----

    /// 打印为对应风牌的 mjai 记法（`1z`-`4z`）。
    /// 记法由 `Tile` 的序数规则推出，不另立一张对照表，因此不会与 `Tile.toMjai` 漂移。
    let toMjai (kaze: Kaze) : string = string (index kaze + 1) + "z"

    /// 解析风牌记法；不是 `1z`-`4z` 返回 None。
    let parse (notation: string) : Kaze option =
        all |> List.tryFind (fun kaze -> toMjai kaze = notation)

    // ---- JSON（mjai wire） ----

    /// mjai wire 上场风就是一个字符串，与牌同形。
    let encoder: Encoder<Kaze> = toMjai >> Encode.string

    /// 解码失败是 Decoder 的错误值，不抛异常。诊断文案用英文（ADR-0001）。
    let decoder: Decoder<Kaze> =
        Decode.string
        |> Decode.andThen (fun notation ->
            match parse notation with
            | Some kaze -> Decode.succeed kaze
            | None -> Decode.fail ("not a valid mjai kaze notation: " + notation))

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：给人和 LLM 看的中文形式。
    /// 引擎判定、事件流、牌谱与测试固件都不得消费它的输出。
    let toDisplay (kaze: Kaze) : string =
        match kaze with
        | Ton -> "东"
        | Nan -> "南"
        | Shaa -> "西"
        | Pei -> "北"
