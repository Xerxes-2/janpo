namespace Janpo.Web

open Fable.Core
open Thoth.Json.Core
open Thoth.Json.JavaScript
open Janpo

/// 首页那份 **Demo Paifu**（CONTEXT.md 的词条；ADR-0003）：随应用一起分发的预录牌谱，
/// 让零 key 的访客第一眼就看到一桌牌在走。
///
/// 这是本工程第三处 F# 调 TS 的地方（ADR-0005：只有这一个方向，且只传字符串）——
/// `fetch` 是浏览器 API 且是**异步**的，因此出口是 Promise，形状与 `Agent.ask` /
/// `Share.ofPayload` 同类。「去哪儿拿」那一层在 `web/src/demo/paifu.ts`，
/// **这一层不碰网络**：它只把拿回来的那段文本读成一份牌谱。
///
/// **它是产品资产，不是测试固件**（ADR-0003）：换成真的四席对局是票 79 的活，
/// 手续只有一条——换 `web/public/demo-paifu.json` 那个文件，代码一行不动。
/// 产出那份文件的命令写在 CLI 的 usage 里（`janpo paifu <种子> --opinionated`）。
[<RequireQualifiedAccess>]
module Demo =

    // ---- 边界（ADR-0005） ----

    /// 拉那份牌谱的原文 → 一个**信封 JSON**（`{"text"}` 或 `{"error"}`）。
    /// **它不 reject、不空手而归**：拉不到的三种失法在 TS 那侧各有一句中文原因，
    /// 一律以「Demo 牌谱拉不到：」开头。
    [<Import("loadDemoPaifu", "../../web/src/demo/paifu.ts")>]
    let private loadDemoPaifu () : JS.Promise<string> = jsNative

    /// 信封的两种。诊断原样带过来——措辞是 TS 那侧写的，这边不重写一遍
    /// （**同一句话只有一处**，改了那边这边不必跟着改）。
    let private envelopeDecoder: Decoder<Result<string, string>> =
        Decode.object (fun get ->
            match get.Optional.Field "error" Decode.string with
            | Some reason -> Error reason
            | None ->
                match get.Optional.Field "text" Decode.string with
                | Some text -> Ok text
                | None -> Error "Demo 牌谱拉不到：TS 侧的信封里既没有正文也没有原因")

    // ---- 出口 ----

    /// 那份牌谱**读成值**。两层诊断分得清，与 `Share.ofPayload` 同一条规矩：
    /// 「Demo 牌谱拉不到：……」是没拿到那个文件（404、离线、被拦），
    /// 「Demo 牌谱读不动：……」是拿到了但里面那份牌谱不合形状（引擎的诊断，英文，ADR-0001）。
    /// 页面上那句提示按这个前缀分得出该说「站点资产没部署全」还是「这份牌谱太新／太旧」。
    let paifu () : JS.Promise<Result<Paifu, string>> =
        let read (envelope: string) : Result<Paifu, string> =
            match Decode.fromString envelopeDecoder envelope with
            | Error message -> Error $"Demo 牌谱拉不到：信封读不动（{message}）"
            | Ok(Error reason) -> Error reason
            | Ok(Ok text) ->
                Decode.fromString Paifu.decoder text
                |> Result.mapError (fun message -> $"Demo 牌谱读不动：{message}")

        let failed (error: obj) : Result<Paifu, string> = Error $"Demo 牌谱拉不到：取它时抛了异常（{error}）"

        (loadDemoPaifu ()).``then``(read).catch (failed)
