namespace Janpo.Web.Tests

open Xunit
open Janpo.Web

/// 播放控制的状态机（票 22）。钉的是**世代号**：它存在的唯一理由是让过期的定时器
/// 回来时被丢掉，因此每条迁移都要验「世代变了没有」。
module PlaybackTests =

    [<Fact>]
    let ``初始是暂停着的 1×`` () =
        Assert.False(Playback.initial.Playing)
        Assert.Equal(Speed.X1, Playback.initial.Speed)

    [<Fact>]
    let ``播与暂停来回切`` () =
        let playing = Playback.toggle Playback.initial

        Assert.True(playing.Playing)
        Assert.False((Playback.toggle playing).Playing)

    [<Fact>]
    let ``每次迁移都换一个世代`` () =
        let playing = Playback.toggle Playback.initial
        let faster = Playback.setSpeed Speed.X4 playing

        Assert.NotEqual<int>(Playback.initial.Generation, playing.Generation)
        Assert.NotEqual<int>(playing.Generation, faster.Generation)
        Assert.NotEqual<int>(faster.Generation, (Playback.pause faster).Generation)

    [<Fact>]
    let ``暂停着的时候再暂停也换世代：单步要把在飞的定时器废掉`` () =
        let paused = Playback.pause Playback.initial

        Assert.False(paused.Playing)
        Assert.NotEqual<int>(Playback.initial.Generation, paused.Generation)

    [<Fact>]
    let ``当前世代的定时器算数`` () =
        let playing = Playback.toggle Playback.initial

        Assert.True(Playback.accepts playing.Generation playing)

    [<Fact>]
    let ``暂停之后连当前世代的定时器也不算数`` () =
        let playing = Playback.toggle Playback.initial
        let paused = Playback.pause playing

        Assert.False(Playback.accepts paused.Generation paused)

    [<Fact>]
    let ``暂停再播之后，旧世代的定时器不算数——否则牌桌会跑双份`` () =
        let playing = Playback.toggle Playback.initial
        let resumed = playing |> Playback.pause |> Playback.toggle

        Assert.True(resumed.Playing)
        Assert.False(Playback.accepts playing.Generation resumed)

    [<Fact>]
    let ``换倍速把旧世代的定时器废掉，新的按新间隔续`` () =
        let playing = Playback.toggle Playback.initial
        let faster = Playback.setSpeed Speed.X8 playing

        Assert.True(faster.Playing)
        Assert.False(Playback.accepts playing.Generation faster)
        Assert.True(Playback.accepts faster.Generation faster)

    [<Fact>]
    let ``档位越快间隔越短`` () =
        let intervals = Speed.all |> List.map Speed.interval

        Assert.Equal<int list>(intervals |> List.sortDescending, intervals)
        Assert.All(intervals, fun interval -> Assert.True(interval > 0))
