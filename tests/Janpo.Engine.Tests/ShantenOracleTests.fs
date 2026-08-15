namespace Janpo.Engine.Tests

open System.IO
open Xunit
open Janpo

/// 与现成实现（PyPI 的 `mahjong` 库）的向听数对拍。
///
/// 固件 `fixtures/shanten-oracle.tsv` 由 `scripts/oracle/refresh-fixture.sh` 生成并提交进仓库，
/// 因此这个测试离线跑、CI 不需要 Python——oracle 是**测试期的外部工具**，
/// 不是引擎或 CLI 的运行时依赖。大批量的现跑对拍见 `scripts/oracle/differential.sh`。
module ShantenOracleTests =

    let private fourPlayer = TileKindSet.fourPlayer

    let private fixturePath =
        Path.Combine(System.AppContext.BaseDirectory, "fixtures", "shanten-oracle.tsv")

    /// 一行固件：「<副露数> <mjai 记法...>」与 oracle 算出的向听数。
    let private parseCase (line: string) : (int * HandShape * int) option =
        if line.StartsWith "#" || line.Trim() = "" then
            None
        else
            match line.Split '\t' with
            | [| hand; expected |] ->
                match hand.Split(' ', 2) with
                | [| naki; notations |] ->
                    let nakiCount = int naki

                    match Tile.parseMany notations |> Result.map (HandShape.create nakiCount) with
                    | Ok(Ok shape) -> Some(nakiCount, shape, int expected)
                    | Ok(Error error) -> failwith $"固件里的手牌不合法：{HandShapeError.toDisplay error}（{line}）"
                    | Error error -> failwith $"固件里的记法不合法：{TileListParseError.toDisplay error}（{line}）"
                | _ -> failwith $"固件格式不对：{line}"
            | _ -> failwith $"固件格式不对：{line}"

    let private cases = lazy (File.ReadAllLines fixturePath |> Array.choose parseCase)

    [<Fact>]
    let ``固件本身够大且覆盖到和了型与听牌`` () =
        let cases = cases.Value
        Assert.True(Array.length cases >= 4000, $"固件只有 {Array.length cases} 手，对拍要有量")

        let byShanten =
            cases |> Array.countBy (fun (_, _, expected) -> expected) |> Map.ofArray

        for expected in -1 .. 4 do
            Assert.True(Map.containsKey expected byShanten, $"固件里没有 {expected} 向听的样本，对拍覆盖不到这一档")

        Assert.Contains(cases, fun (naki, _, _) -> naki > 0)

    [<Fact>]
    let ``与 oracle 的向听数差异为零`` () =
        let differences =
            cases.Value
            |> Array.choose (fun (naki, hand, expected) ->
                let actual = Shanten.value (Shanten.calculate fourPlayer hand)

                if actual = expected then
                    None
                else
                    Some $"naki={naki} {HandShape.tiles hand |> Tile.toMjaiMany} oracle={expected} engine={actual}")

        Assert.Equal<string array>([||], Array.truncate 10 differences)
        Assert.Equal(0, Array.length differences)
