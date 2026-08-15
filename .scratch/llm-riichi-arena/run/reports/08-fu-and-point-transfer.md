# 08 — 符计算与点数授受

**Status:** ready-for-human ｜ 全量测试 383 通过 ｜ fantomas 干净 ｜ `./scripts/ci.sh` 全绿

## 做了什么

把 06 记 0 的和了点数填成真值，并把 06 留下的「无役不可和」钩子接上。

### 新增（引擎）

| 文件 | 内容 |
|---|---|
| `src/Janpo.Engine/Fu.fs` | `Fu`（私有记录：底 / 面子 / 雀头 / 听牌型 / 门清荣和 / 自摸 / 副露平和形），`Fu.calculate` / `total`（未切上）/ `value`（切上）/ `toDisplay` |
| `src/Janpo.Engine/Score.fs` | `Limit`、`HoraValue`（符+番+役满倍数）、`HoraTransfer`（谁和了谁点的几本场几供托）、`HoraScore`（级别+和了点+四家增减）、`HoraReading`；`Score.basePoints` / `limit` / `hora` / `best`，`HoraValue.ofTally` / `fan` |

公开签名（本票的对外接缝，13 票对拍与 UI 都走它们）：

```fsharp
Fu.calculate  : Ruleset -> YakuContext -> AgariHand -> YakuTally -> Fu
Fu.total      : Fu -> int          // 切上前的分项合计
Fu.value      : Fu -> int          // 点数表要的那个符（切上到 10；七对子 25 不切）
Score.basePoints : Ruleset -> HoraValue -> int
Score.limit      : Ruleset -> HoraValue -> Limit
Score.hora       : Ruleset -> HoraTransfer -> HoraValue -> HoraScore
Score.best       : Ruleset -> YakuContext -> AgariHand -> Result<HoraReading, YakuError>
GameState.horaOf : Seat -> GameState -> Result<HoraReading, YakuError>
```

### 改动（引擎）

- `Ruleset` 加 5 个字段：`KiriageMangan = false`（**默认关**）、`DoubleKazeJantouFu = 4`、
  `RinshanTsumoFu = true`、`HonbaPoints = 300`、`RiichiStick = 1000`，另加 `Ruleset.withKiriageMangan`。
- `Seat.jikaze : Ruleset -> Seat -> Seat -> Kaze`（自风由座位与亲推出，CONTEXT.md 说它不是座位的属性）。
- `PlayerState.agari : bool -> Tile -> PlayerState -> Result<AgariHand, AgariHandError>`
  ——**引擎里构造 `AgariHand` 只有这一处**，10 / 11 加副露时只改它。
- `GameState`：
  - `yakuContextOf`（私有）：判役上下文由局面自己填（场风 / 自风 / 宝牌指示牌 / 海底河底 / 天和地和）；
  - `horaOf`（公开）：某座位此刻和了能得到什么，**「无役不可和」的唯一判据**；
  - `responsesTo` 的 canRon、`awaitingDahaiActions` 的自摸和、`step` 的自摸和校验、`applyHora` 的结算
    全部读 `horaOf`；
  - `applyHora` 真算点：Oya / Ko × 自摸 / 荣和、本场与供托、双响逐条累加。
  - 新增 `IllegalAction.NoYaku of Seat * Tile`（按裁决 D-3 全部使用点显式限定）。
- `Event.Hora` 的字段语义补全（`Fu` / `Fan` / `HoraPoints` / `Deltas` 的注释）。
- CLI：`janpo yaku` 多打印 `fu` / `points` / `deltas` / `score`，新增 `--honba` / `--kyotaku` / `--kiriage`。

### 测试

- `FuTests`（13 条黄金用例）+ `FuProperties`（5 条）
- `ScoreTests`（16 条）+ `ScoreProperties`（9 条）+ `ScoreGenerators`
- `HoraTests` 的和了事件填成真值，另加 5 条：无役不可和（Ron 不进动作集 / 同手牌自摸能和）、
  自摸与荣和的本场供托、双响时本场与供托只归最前一家。
- `GameStateProperties`：把「点数一张不动」换成「授受把点数与供托一起守恒」；
  响应阶段那条加了「有役」。
- **每条点数 / 符的用例都注明依据的规则集**（默认 `Ruleset.yonma` = 天凤的这几项），13 票对拍时分得清是实现错还是规则集错。

## 关键取舍（详见 DECISIONS.md 的 08 段）

1. **高点法按点数排，不是按番数排**：`3 番 70 符`（封顶满贯 8000）比 `4 番 30 符`（7700）高。
   顺带查清了 07 的近似符与真符的差只有常数项与连风牌雀头，实践中不会分叉——真正的分叉是排序键。
2. **供托进和了者的 `Deltas`**，因此一次和了的增减之和 = 供托点数而不是 0。
   **05 只需在和了后把 `Kyotaku` 归零，别再给和了者补一次立直棒。**
3. **06 的三个剧本里有两手牌在真实规则下无役、压根不能荣和**，本票把它们改成平和形；
   听牌张、巡目、振听与头跳的断言一字未改。这是固件不合法，不是迁就实现。
4. 役满的 `Hora.Fan` 记 13 × 倍数（不是 0），点数一律由 `Yakuman` 倍数算。

## 留给人的待审项

1. **提案 08-A**（DECISIONS 末尾）：`Fu` 的分项、`Honba`/`Kyotaku` 的点数换算、`Limit`、`HoraPoints`
   进不进 CONTEXT.md；两个英汉混拼字段名 `DoubleKazeJantouFu` 与 `RiichiStick` 请裁一下。
2. **数え役满定在 13 番**、**平和自摸 20 符 / 副露平和形 30 符 / 七对子 25 符**、
   **每笔支付各自切上到 100**——spec 与 CONTEXT.md 都没写，取的是天凤 / 通行规则。
3. `Fu` 与 `Yaku` 各有一份 `isYaochuu` / `isSangenpai` / `isKaze` 私有谓词（三个一行函数）。
   要合并的话该提到 `Tile` 上，那是 01 票的文件，本票没动。
4. 国士与役满的符按结构算出来备查（点数不看它）；`Hora.Fu` 因此会给一个非 0 的数。

## 给 11 票（责任支付）的挂点

`Score.hora` 只认 `HoraTransfer`（谁和了、谁点的、亲是谁、本场与供托），
责任支付要做的是**在算出 `HoraScore` 之后改写 `Deltas`**：给 `HoraTransfer` 加一个
`Sekinin: Seat option`，或在 `Score` 里加 `Score.sekininBarai : HoraScore -> ... -> HoraScore`。
`Fu` / `basePoints` / `limit` 一行都不必动。大明杠的责任支付与大三元 / 大四喜的包牌都不在本票。

## Review 结论（两轴，自跑）

- **Standards**：一概念一文件、`namespace Janpo`、fsproj 按依赖顺序、类型与同名
  `[<RequireQualifiedAccess>]` module 同文件、`toDisplay` 在文件末尾且中文只在那里、错误是值、
  测试三件套、中文测试名只测公开 API、Fable 允许名单未变（无新包、无反射，只用位移与 List 函数）
  ——逐条对过，无 blocking。
  裁决 D-1（标识符照术语表）与 D-3（同名 case 显式限定）都照做了；D-3 顺手把 07 里
  原本不限定的 16 处 `NoYaku` 一并限定，免得下一票再踩。
- **Spec**：票的 8 条验收逐条对上（见票文件的勾选），另加 06 交接的「无役不可和 + 一条用例」
  与 07 交接的「拿 `candidates` 按真符再选一次」。包牌 / 责任支付按票要求**没做**；
  跨局的场况推进（05 的范围）一行没碰。
- 修了 review 里唯一一条 blocking：`FuProperties` 最初写的「门清荣和的符不低于同型自摸」
  被 FsCheck 证伪（无役的荣和读法会被过滤掉，最大值退化成 0），换成了成立的
  「平和的读法自摸恒 20 符、荣和恒 30 符」。
