namespace Janpo.Engine.Tests

open Xunit
open Thoth.Json.Core
open Janpo.Golden

/// 把一份编好的 JSON 摊平成「路径 → 逐行的值」（票 28）。
///
/// 它存在的理由只有一个：黄金用例里的决策包此前按**整行**钉住（约 8 KB 一行），
/// 加一个字段就印两条长行，只能写脚本比对。摊平之后一个叶子一个字段，
/// 报错指得到 `package.observation.self.tehai` 这样的路径。
module GoldenJsonTests =

    let private fields (encodable: IEncodable) = GoldenJson.fields "root" encodable

    [<Fact>]
    let ``标量字段各占一个字段，值是 JSON 的字面量`` () =
        let encoded =
            Encode.object
                [
                    "seat", Encode.int 3
                    "name", Encode.string "5m"
                    "riichi", Encode.bool true
                    "tsumo", Encode.nil
                ]

        Assert.Equal<(string * string list) list>(
            [
                "root.seat", [ "3" ]
                "root.name", [ "5m" ]
                "root.riichi", [ "true" ]
                "root.tsumo", [ "null" ]
            ],
            fields encoded
        )

    [<Fact>]
    let ``嵌套对象的路径用点号拼`` () =
        let encoded =
            Encode.object
                [
                    "self", Encode.object [ "furiten", Encode.object [ "doujun", Encode.bool false ] ]
                ]

        Assert.Equal<(string * string list) list>([ "root.self.furiten.doujun", [ "false" ] ], fields encoded)

    [<Fact>]
    let ``全是标量的数组是一个字段的多行`` () =
        let encoded =
            Encode.object [ "tehai", [ "1m"; "2m"; "3m" ] |> List.map Encode.string |> Encode.list ]

        Assert.Equal<(string * string list) list>([ "root.tehai", [ "1m"; "2m"; "3m" ] ], fields encoded)

    [<Fact>]
    let ``对象的数组按下标继续摊开`` () =
        let entry (pai: string) (tsumogiri: bool) =
            Encode.object [ "pai", Encode.string pai; "tsumogiri", Encode.bool tsumogiri ]

        let encoded =
            Encode.object [ "kawa", [ entry "1z" false; entry "9p" true ] |> Encode.list ]

        Assert.Equal<(string * string list) list>(
            [
                "root.kawa.0.pai", [ "1z" ]
                "root.kawa.0.tsumogiri", [ "false" ]
                "root.kawa.1.pai", [ "9p" ]
                "root.kawa.1.tsumogiri", [ "true" ]
            ],
            fields encoded
        )

    /// 空表与空对象仍然要占一个字段：它们从字段表里消失就等于「这一项没被钉住」，
    /// 而「一个都没有」本身就是一种要钉住的输出（`GoldenObservation` 的 `joined` 同理）。
    [<Fact>]
    let ``空表与空对象各占一个零行的字段`` () =
        let encoded = Encode.object [ "naki", Encode.list []; "scaffold", Encode.object [] ]

        Assert.Equal<(string * string list) list>([ "root.naki", []; "root.scaffold", [] ], fields encoded)

    /// 字段的先后就是 encoder 里写的先后——摊平之后的用例文件读起来仍像那份 JSON。
    [<Fact>]
    let ``字段的顺序就是 encoder 里的顺序`` () =
        let encoded =
            Encode.object [ "z", Encode.int 1; "a", Encode.int 2; "m", Encode.int 3 ]

        Assert.Equal<string list>([ "root.z"; "root.a"; "root.m" ], fields encoded |> List.map fst)
