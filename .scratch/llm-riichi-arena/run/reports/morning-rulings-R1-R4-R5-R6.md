# 晨间裁决 R-1 / R-4 / R-5 / R-6 的代码化

用户已裁决的四条，四个独立 commit，**每个 commit 之间 `./scripts/ci.sh` 全绿**。
基线：`okuuwpzk`（集成 11，493 测试全绿）。

顺序：R-6（改名）→ R-1（默认值）→ R-4（`Ruleset` 携带 `TileKindSet`）→ R-5（`Seat` 真类型）。

---

## R-6：消除跨层同名

**改了什么**

- `YakuError.NotAgari` → `YakuError.NoAgariShape`。名字与 `AgariShape` 这个术语对齐：
  它说的是「`AgariShape.classify` 给不出任何读法」。
- `YakuError` 加 `[<RequireQualifiedAccess>]`，两个 case 永远写全名。
- `IllegalAction` **未动、未限定**。它的 case 是引擎拒绝一个动作的**理由**，
  `NotAgari(actor, pai)` 与 `NotYourTurn` / `NotInHand` / `NotInHand` 是同一风格的一串。

**结果**：源码里不限定的 `NotAgari` / `NoYaku` 从此只可能是 `IllegalAction` 的。
裁决 D-3 留下的「读代码的人仍要停一下」消失。

**触碰的文件**：`Yaku.fs`（类型定义 + `detect` + `toDisplay`）、`GameState.fs`（4 处 `Error` 构造
+ 2 处文档注释）、`YakuTests.fs` 与 `ScoreTests.fs` 各 1 处断言。

**测试**：期望值一处没改语义——两处测试断言里的 `Error YakuError.NotAgari` 只是跟着改名。
493 通过。
