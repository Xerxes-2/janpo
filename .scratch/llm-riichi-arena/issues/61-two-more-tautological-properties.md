# 61 — 同族的另外两条恒真属性，以及 `SeatStream` 进术语表

**What to build:** 票 60 修好那条恒真属性时，顺手发现**同一族还有两条**（只报未改）。
它连改法都留好了：**用它新建的第三锚点 `ObservationFixtures.mismatches` 当右侧**，约十行。
顺带把 `SeatStream` 收进术语表——它现在是引擎侧闸门的主语，却还不在 `CONTEXT.md` 里。

**Blocked by:** None（票 60 已落地，锚点在库里）

**Status:** ready-for-human

## 一、两条恒真属性（`DecisionPackageProperties`）

1. 「包里的历史就是那条唯一的掩蔽流」——**两侧是同一个表达式**
2. 「包里的历史 fold 出来的就是包里的那份观测」——**两侧是同一个 fold**

票 60 实测：弄坏 `SeatStream.absorb` 与 `MaskedEvent.forSeat`，**这两条全绿**。

- [x] 用 `ObservationFixtures.mismatches : Seat -> GameState -> Observation -> string list`
      （票 60 新建，编译顺序在 `MaskedStreamTests.fs` 之前）当右侧改写它们——
      **别拿另一份同源观测当右侧**，那是这一族错误的成因
- [x] **逐条反向自证**：弄坏 `absorb` 要红、弄坏 `forSeat` 要红，红的原始输出抄进报告
- [x] 给出每条断言**一次 CI 里执行了多少次**（判据 3）
- [x] **顺手扫一遍还有没有第三条**：全仓库找「断言两种算法给出同一结果」的属性，
      逐条问**两侧是不是同一个实现**。找到就一并修或报上来；确认没有就在报告里写「扫过哪些、结论是什么」

## 二、`SeatStream` 进术语表（**本票获准修改 `CONTEXT.md`，仅限此一处**）

29a-B 与票 58 都提过，票 60 让它成了引擎侧闸门的主语，再不收就是「代码里的一等概念不在术语表里」。

- [x] 加 `SeatStream` 词条：它是**掩蔽事件流 fold 到此刻的累加器**，
      同时给出「那一家看得见的历史」与「那一家此刻的观测」——**两者同出一源**（这是它存在的理由）
- [x] 写清与 `Observation`（结果）、掩蔽事件流（输入）的关系，带 _Avoid_
      （别与「一次性从头 fold」混为一谈：那是同一份语义的另一条实现路径，靠票 60 的三腿闸门守着一致）
- [x] **其余词条一个字都不许动**

## 边界

- [x] 不改 `src/Janpo.Engine/` 的语义；`ObservationFixtures` 是测试侧设施，别搬进 `src/`
- [x] 不碰 `tests/fixtures/paifu/`、`PaifuDifferential.fs`、`GameState` 的杠处理与黄金用例
      （票 59 正在那边修两个真 bug）
- [x] 属性用例数只许增不许减；CI 墙钟涨幅 ≤15%（基线约 41s；实测 40.6s / 33.2s）
