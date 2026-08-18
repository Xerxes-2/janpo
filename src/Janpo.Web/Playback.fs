namespace Janpo.Web

/// 播放速度的档位。**「倍速」是给人看的说法，落地是两手之间等多久**——
/// 第一版无动画（spec 的动画预算后置），一手就是一次状态跳变，快慢只体现在间隔上。
[<RequireQualifiedAccess>]
type Speed =
    | X1
    | X2
    | X4
    | X8

/// 播放控制的状态机：暂停 / 单步 / 加速。**它不含牌桌**——推进一手是 `Table` 的事，
/// 这里只回答「下一手什么时候来、这一记定时器还算不算数」。
type Playback = {
    /// 在播吗。暂停时不再续下一记定时器。
    Playing: bool
    /// 步进间隔的档位。
    Speed: Speed
    /// 世代号：**每次改播放状态或倍速都 +1**，过期的定时器回来时按它丢掉。
    /// 没有它的话，「暂停 → 立刻再播」会让在飞的那记定时器与新的一起跑，
    /// 牌桌以双倍速度走；连点两下「加速」同样。
    Generation: int
}

/// 速度档位的取值与渲染。
[<RequireQualifiedAccess>]
module Speed =

    // ---- 构造 ----

    /// 全部档位，由慢到快。UI 的按钮就是它。
    let all: Speed list = [ Speed.X1; Speed.X2; Speed.X4; Speed.X8 ]

    // ---- 拆解 ----

    /// 两手之间等多少毫秒。1× 定在 600ms：随机选手一局约 70 手，看完一局约 40 秒。
    let interval (speed: Speed) : int =
        match speed with
        | Speed.X1 -> 600
        | Speed.X2 -> 300
        | Speed.X4 -> 150
        | Speed.X8 -> 60

    // ---- 渲染层出口（ADR-0001） ----

    /// **渲染层的单向出口**：按钮上的那个字。
    let toDisplay (speed: Speed) : string =
        match speed with
        | Speed.X1 -> "1×"
        | Speed.X2 -> "2×"
        | Speed.X4 -> "4×"
        | Speed.X8 -> "8×"

/// 播放控制的迁移与判定。**纯函数**：定时器本身由 Elmish 的 Cmd 发，这里只管状态。
[<RequireQualifiedAccess>]
module Playback =

    // ---- 构造 ----

    /// 初始：暂停着，1×。**主持人那一页（`?table=1`）一直以它开头**：
    /// 一进页面就自己跑起来会让无头验收读到一个动着的牌桌，而要点、要读牌桌的
    /// 那几道闸门全靠这一条（票 71 把它们一并迁到了 `?table=1`）。
    ///
    /// **首页的 Demo 回放另走 `playing`**（票 71）：那一页的卖点就是自动播。
    let initial: Playback = {
        Playing = false
        Speed = Speed.X1
        Generation = 0
    }

    // ---- 迁移 ----

    /// 换一个世代。改播放状态与改倍速都经它，谁也别忘了 +1。
    let private reborn (change: Playback -> Playback) (playback: Playback) : Playback = {
        change playback with
            Generation = playback.Generation + 1
    }

    /// 从头自动播：这一档速度，**接着当前世代往下换**（牌谱拉回来、从头再放、
    /// 换了一份牌谱导入进来，走的都是它）。
    ///
    /// **为什么不是一个「世代号从 0 起」的构造子**（票 78 改掉的旧形状）：那样的值只在
    /// 「一记定时器都没发过」的初始时刻才安全——回放正在自动播时拿它接手，在飞的那记
    /// 定时器与新发的一起被认下（世代号撞回同一个数），牌桌从此**双倍速**走。
    /// 经 `reborn` 换世代，在飞的那一记必然作废。
    let restart (speed: Speed) : Playback -> Playback =
        reborn (fun playback -> {
            playback with
                Playing = true
                Speed = speed
        })

    /// 播 / 暂停。
    let toggle: Playback -> Playback =
        reborn (fun playback -> {
            playback with
                Playing = not playback.Playing
        })

    /// 暂停。**已经暂停着也照样换世代**：单步与「一局终了」都靠它把在飞的那记定时器废掉。
    let pause: Playback -> Playback =
        reborn (fun playback -> { playback with Playing = false })

    /// 换倍速。播着的时候换也成立：旧世代的定时器过期，新的按新间隔续上。
    let setSpeed (speed: Speed) : Playback -> Playback =
        reborn (fun playback -> { playback with Speed = speed })

    // ---- 判定 ----

    /// 这一记定时器还算数吗：现在在播，且它是当前世代发出的。
    let accepts (generation: int) (playback: Playback) : bool =
        playback.Playing && generation = playback.Generation
