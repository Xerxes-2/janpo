namespace Janpo

/// 种子化的确定性伪随机数发生器（xorshift32）。
///
/// 同一种子必然产出同一序列——「同种子同牌山」、黄金用例与 soak 的复现都靠这条。
/// 不用 `System.Random`：它的实现在 dotnet 与 Fable(JS) 两侧不同，跨目标不可复现。
/// 这里只用 32 位无符号数的位移与异或，两侧语义完全相同。
///
/// 它不是密码学安全的，也不必是：用途只有洗牌与 bot 的随机选择。
[<Struct>]
type Rng = private { State: uint32 }

/// 发生器的构造与取数。全部是纯函数：取一个数就得到一个新的发生器。
[<RequireQualifiedAccess>]
module Rng =

    /// xorshift32 的一步。0 是它的不动点，`ofSeed` 已把 0 排除掉。
    let private step (state: uint32) : uint32 =
        let shifted = state ^^^ (state <<< 13)
        let shifted = shifted ^^^ (shifted >>> 17)
        shifted ^^^ (shifted <<< 5)

    // ---- 构造 ----

    /// 从种子构造。任何 int 都是合法种子。
    let ofSeed (seed: int) : Rng =
        let mixed = uint32 seed ^^^ 0x9E3779B9u
        // 0 是 xorshift 的不动点，避开；再空转三步，免得相邻种子的头几个输出高度相关。
        let nonZero = if mixed = 0u then 0x6D2B79F5u else mixed
        { State = step (step (step nonZero)) }

    // ---- 取数 ----

    /// `[0, bound)` 内的均匀随机整数。`bound <= 1` 时恒为 0 且不消耗随机数。
    /// 用拒绝采样消掉取模偏差——洗牌的均匀性直接取决于它。
    let nextBelow (bound: int) (rng: Rng) : int * Rng =
        if bound <= 1 then
            0, rng
        else
            let limit = uint32 bound
            // 2^32 mod limit，即落在最后一个不完整区间里的那些值的个数。
            let excess = ((0xFFFFFFFFu % limit) + 1u) % limit

            let rec loop (current: Rng) =
                let state = step current.State
                let advanced = { State = state }

                if excess = 0u || state < 0xFFFFFFFFu - excess + 1u then
                    int (state % limit), advanced
                else
                    loop advanced

            loop rng

    /// Fisher-Yates 洗牌。同一发生器状态与同一输入必然得到同一结果。
    let shuffle (items: 'a list) (rng: Rng) : 'a list * Rng =
        let shuffled = List.toArray items
        let mutable current = rng

        for index in shuffled.Length - 1 .. -1 .. 1 do
            let target, advanced = nextBelow (index + 1) current
            current <- advanced
            let held = shuffled.[index]
            shuffled.[index] <- shuffled.[target]
            shuffled.[target] <- held

        List.ofArray shuffled, current
