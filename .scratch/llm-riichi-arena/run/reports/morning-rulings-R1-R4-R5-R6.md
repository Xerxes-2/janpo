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

---

## R-1：默认规则集对齐天凤（ADR-0004 决定 3）

**改了什么**

- `Ruleset.yonma`：`Atamahane = true` → `false`（双响 / 三响成立）。
- `Ruleset.withoutAtamahane` → **`Ruleset.withAtamahane`**（打开头跳）。默认已经是关的，
  留一个「关掉」的组合子会让读的人以为默认是开的。
- 字段的文档注释从「默认开，待人裁决」改成「默认关，ADR-0004 决定 3」。
- 其余默认值本就对齐，一处没动：`KiriageMangan = false`、`DoubleKazeJantouFu = 4`、
  `RinshanTsumoFu = true`、`KokushiAnkanChankan = false`。

**改了哪些测试的期望值，各自为什么**

一条用例都没删。共 6 处，分三类：

| # | 用例 | 改动 | 理由 |
|---|---|---|---|
| 1 | `HoraTests.头跳开着时同巡双响只成立打牌者下家优先的那一家` | `Assert.True(ruleset.Atamahane)` → 先 `Ruleset.withAtamahane ruleset` 再断言 | **测的是头跳本身**。头跳不再是默认，用例显式打开它，断言与结论一字未改 |
| 2 | `HoraTests.头跳开着时同巡三响只成立最靠前的一家` | 同上，改用 `Ruleset.withAtamahane ruleset` 起局 | 同上 |
| 3 | `HoraTests.头跳关掉时同巡双响都成立，按打牌者下家优先排序` | `Ruleset.withoutAtamahane ruleset` → `ruleset`，并补一行 `Assert.False(doubleRon.Atamahane)` | 组合子没了；默认就是这个形态，补的那行把「默认 = 关」写在用例里 |
| 4 | `HoraTests.头跳关掉时同巡三响也都成立` | 同上（不必补断言，#3 已钉） | 同上 |
| 5 | `HoraTests.双响时本场与供托只归排在最前的那一家` | 同上；点数期望值**一个数都没动** | 它本来就跑在关头跳的规则集上，只是关法从组合子变成默认 |
| 6 | `RulesetTests.符与点数的规则项是字段不是写死的，默认值照天凤` | **新增** 2 行断言：`Assert.False(Ruleset.yonma.Atamahane)` 与 `withAtamahane` 打得开 | 新默认值需要一处钉子；这个用例就是钉默认值的地方 |

另有 2 处是**默认值翻转后必然改变的观测结果**，不是「迁就实现」——它们测的是别的东西，
只是恰好跑在 `doubleRonScript` 上：

| # | 用例 | 改动 | 理由 |
|---|---|---|---|
| 7 | `HoraTests.摊好的两局各自以自摸和与荣和收尾` | 荣和那局的 `Actor` 期望 `[2]` → `[2; 3]`、`Target` `[0]` → `[0; 0]` | 用例测的是「摊好的剧本以荣和收尾」。剧本里座位 2 与 3 都听 4p，默认双响后**两家都成立**，这正是 R-1 要的行为 |
| 8 | `GameTests.子荣和了的那一局收进对局之后进局，本场归零、供托清零` | `match ... with \| [ hora ] -> ...` → 对**每一家** hora 断言不是亲 | 用例测的是「**子**和了 → 进局 / 本场归零 / 供托清零」，不是「只有一家和了」。双响后两家都是子，结论不变 |

**测试**：493 通过（数量不变）。

**留给人的一句话**：`Ruleset.withAtamahane` 是 13 票对拍的开关——牌谱来源若是允许头跳的平台，
在那里显式打开即可，而**默认配置现在跑得出真实牌谱**（备注 N-6 提的那条顾虑就此消掉）。
