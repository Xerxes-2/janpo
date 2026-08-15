namespace Janpo.Engine.Tests

open FsCheck.Xunit
open Thoth.Json.Newtonsoft
open Janpo

/// 事件的 JSON 不变量。具名用例见 EventTests。
[<Properties(Arbitrary = [| typeof<EventArbitraries> |])>]
module EventProperties =

    let private encode (event: Event) = Encode.toString 0 (Event.encoder event)

    [<Property>]
    let ``任意事件的 JSON 编解码往返不变`` (event: Event) =
        Decode.fromString Event.decoder (encode event) = Ok event

    [<Property>]
    let ``任意事件编码成一行`` (event: Event) = (encode event).Contains "\n" |> not

    [<Property>]
    let ``任意事件的编码都带 type 字段`` (event: Event) =
        (encode event).StartsWith """{"type":"""
