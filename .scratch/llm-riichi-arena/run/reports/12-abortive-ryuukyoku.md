# 12 — 流局的其余全部形态（报告）

**结论：done。** 528 个测试全绿（落地前 502），fantomas 干净，`./scripts/ci.sh` 全绿。
工作区 `/home/xerxes2/janpo-ws-a`。

做完这票，「一局能怎么结束」在引擎里没有缺口了：和了（06/08）+ 七种流局。

## `RyuukyokuReason` 的最终全集

wire 取值取自 **mjai 的参考实现**（gimite 的 `mjai` gem，`lib/mjai/active_game.rb` 的
`process_ryukyoku` / `process_fanpai`）。gimite 的 wiki 页只举了 `fanpai` 一个例子，
Mortal 的 `libriichi` 干脆把 `reason` 整个丢了（它的 `Ryukyoku` 只有 `deltas`），
因此**代码才是这一层的权威**。裁决 D-1 在这里用得很足：七个 case 里有五个标识符与 wire 不一致。

| F# 标识符 | mjai wire | 中文 | 途中流局？ | 授受 |
|---|---|---|---|---|
| `Fanpai` | `fanpai` | 荒牌流局 | 否 | 听牌料（04 落地） |
| `NagashiMangan` | `nagashimangan` | 流し満貫 | 否 | **满贯档，替代听牌料** |
| `KyuushuKyuuhai` | `kyushukyuhai` | 九种九牌 | 是 | 无 |
| `SuufonRenda` | `sufonrenta` | 四风连打 | 是 | 无 |
| `Suukaikan` | `sukaikan` | 四杠散了 | 是 | 无 |
| `SuuchaRiichi` | `suchareach` | 四家立直 | 是 | 无 |
| `SanchaHora` | `sanchaho` | 三家和了 | 是 | 无 |

`RyuukyokuReason.isAbortive` 就是上表的「途中流局？」列，**两处读它**：
`KyokuEnd.isRenchan`（途中流局一律连庄）与文档。荒牌与流し満貫不在其列——那两种是打到底的。

`EventTests` 里有一条用例把七组「标识符 ↔ wire」逐条钉死，另一条钉住
「`suukaikan` 这个术语表拼法在 wire 上解码失败」（标识符不迁就 wire，wire 也不迁就标识符）。

## 新增的引擎文件与规则集字段

### `src/Janpo.Engine/Ryuukyoku.fs`（新，161 行）

**纯函数模块**：输入这一局的事实（`Ruleset` + `PlayerState list`），输出「是不是就此终了、
为什么」与「怎么清算」。一个字段都不改，状态机怎么走仍旧全在 `GameState`。

- `canDeclareKyuushuKyuuhai ruleset firstTurn hand` / `yaochuuKinds hand`
- `afterDahai ruleset players : RyuukyokuReason option`（四家立直 → 四风连打 → 四杠散了，
  顺序照 mjai 参考实现）
- `isSanchaHora ruleset winners`
- `nagashiSeats players` / `nagashiDeltas ruleset oya seats`
- `revealedBy ruleset reason declarers`（mjai `tenpais` 记的是「谁亮了手牌」）
- `noDeltas ruleset`

放在 fsproj 的「一局之内的状态机」组里、`PlayerState.fs` 与 `GameState.fs` 之间
（没有重排任何既有文件，见备注 N-11 与裁决 D-3）。

**一个 F# 的坑值得记**：同名的 `type Ryuukyoku`（mjai 事件载荷）在 `Event.fs`，
F# 只在**同一个文件**里自动给同名模块加 `Module` 后缀（`Tile` / `Seat` 那些都是同文件），
跨文件要手写 `[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]`，
否则 `error FS0250: 名称均为“Ryuukyoku”的一个模块和一个类型定义出现在命名空间的此程序集的两个部分中`。
叫法一分不变（`Ryuukyoku.nagashiSeats`），只是 IL 里多一个后缀。

### `Ruleset` 新增两个字段

- **`SanchaHoraRyuukyoku: bool`（默认 `true`）** —— 三家和了是不是途中流局。
  天凤是（默认，ADR-0004 决定 3「默认值对齐天凤」），雀魂三响成立、不流局，
  用 `Ruleset.withoutSanchaHoraRyuukyoku` 换到那一边。
  **头跳开着时它永远用不上**（那时至多成立一家，凑不齐三家）。
- **`KyuushuKinds: int`（默认 `9`）** —— 九种九牌宣言所需的幺九牌种数。
  ADR-0004 决定 1 不许散落规则字面量，`9` 正是那种数（有的规则集要十种十牌）。

四杠散了没有新字段：**它读 `Ruleset.RinshanCount`**（「一局最多能杠几次」＝ 4），
两个数在任何规则集里本来就必须相等——岭上牌用完就杠不动了。

## Nagashi Mangan 的判据与清算

**判据读三样东西**（全在 `PlayerState`，都是前面的票留好的）：

1. `PlayerState.kawa` —— 打出去的牌，**被鸣走的那张仍留在里面**（10 票的做法）；
2. `PlayerState.kawaTaken` —— 10 票记的标记，`markKawaTaken` 只置不清；
3. 河非空（什么都没打过不算「流」过；荒牌流局时四家各打过十几张，走不到）。

判据是 `kawa 全是幺九牌 && not kawaTaken && kawa 非空`。**没有第四样**——特别是
**不看听牌、不看副露、不看点数**。

**清算替代听牌料，不叠加**：`GameState.exhaustiveDraw` 里是一个 `match`，
`nagashiSeats` 为空才走 `notenBappu`，否则走 `nagashiDeltas` 并把 `reason` 换成 `NagashiMangan`。
两条路合流到同一个 `endWithRyuukyoku`。

`nagashiDeltas` **直接调 08 的 `Score.hora`**，`HoraValue = { Fu = 0; Han = 5; Yakuman = 0 }`
（5 番就是满贯档），`Actor = Target`（自摸）、`Honba = 0`、`Kyotaku = 0`、`Sekinin = None`。
于是 Oya 成立是 `+12000 / −4000 ×3`、Ko 成立是 `+8000 / −4000(Oya) / −2000 ×2`，
与 mjai 参考实现逐项相同。**没有另写一张点数表**，也没有 `4000` / `8000` 这类字面量。
多家同时成立就逐家累加（和恒为 0）。

**本场与供托都不进这笔账**：流し満貫仍然是流局，供托留在场上、本场照流局递增（05 的 `Game.after`）。
**Oya 听牌则连庄**：`tenpais` 照常按 `Shanten` 算并进 wire，`KyokuEnd.isRenchan` 读的就是它
（06 的那一份，没有重写，只在开头加了「途中流局一律连庄」这一支）。

## 三家和了的规则集字段

**`Ruleset.SanchaHoraRyuukyoku`**（见上表）。判定点在 `GameState.applyResponse` 的裁决处：
`ronWinners` 给出 ≥ `SeatCount − 1` 家且字段开着 → `endWithAbortive ... SanchaHora`，
**一家也不成立**（`GameState.horas` 为空）。打牌那家挂着的立直宣言在这里作废
（与被荣和同理，立直棒不出）。

`HoraTests` 里那条「头跳关掉时同巡三响也都成立」改成显式用
`Ruleset.withoutSanchaHoraRyuukyoku`（雀魂那一边）并改了标题，断言一字未动；
天凤那一边（三响 → 流局）在 `RyuukyokuTests` 里另有一条。**这是本票唯一改动既有断言语义的地方**，
理由是 ADR-0004 定的默认值就是天凤。

## 途中流局的三处约定

- **不授受**：`deltas` 全 0，`scores` 不变（点数只可能因为立直棒变过）。
- **一律连庄**：`isRenchan` 对 `isAbortive` 的形态直接返回 true。
  **不能拿 `Tenpais` 判**——途中流局压根不验听牌，那样会把九种九牌判成进局。
- **`tenpais` 记的是「谁亮了手牌」**（`Ryuukyoku.revealedBy`，与 mjai 参考实现一致）：
  四家立直四家都亮、三家和了是宣言荣和的那三家、其余形态无人亮牌（含九种九牌的宣言者，
  它亮的是不听的手牌）。

## 加动作的三处（备注 N-1 的契约）

`Action.Ryuukyoku of actor: Seat`（mjai 的动作消息只带 `actor`，形态写在随后那条事件的 `reason` 里）：

1. `Action` 的 case；
2. `awaitingDahaiActions` 的 `RiichiState.None` 那一支（排在自摸和之后、立直宣言之前）——
   立直宣言之后那一手只能打宣言牌，立直成立之后早就不是第一巡了，因此另外两支不必加；
3. `stepAwaitingDahai` 的执行 match，外加 `stepAwaitingResponse` 的拒绝支
   与新的 `IllegalAction.CannotRyuukyoku`。

**没有加 `Event` 的 case**：`ryukyoku` 事件 04 就有了，本票只给它的 `reason` 加取值。

编译器如约把所有漏掉的 match 点找了出来：**引擎 9 处 + 测试 38 处**，逐个补 case，
一个通配符都没用（备注 N-4 / N-7 的同一课）。测试那 38 处里有 4 处我批量脚本插错了组
（注释行把 or-pattern 的「一段」截断了），其中 3 处编译器当场用 `FS0018 此“or”模式的两侧
绑定了不同的变量组` 顶了出来，第 4 处是我逐条读 diff 时抓到的——**它不报错，因为
`| Action.Ryuukyoku _ | Action.Hora _ -> true` 与后面那组各自都还有可达的 case**。
批量改 match 之后必须逐条读 diff，编译器只兜住一半。

## 黄金用例（`RyuukyokuTests` 16 条 + `RyuukyokuProperties` 5 条）

备注 N-8 那条纪律在本票最吃紧：**随机取样一辈子也碰不上这几种形态**，全部靠摊好的牌山。
剧本放在 `GameStateFixtures`（与 06/10/11 的剧本同一处）。

| 形态 | 剧本 / 构造 | 怎么跑到 |
|---|---|---|
| 九种九牌 | `kyuushuScript`（Oya 配牌就有九种幺九牌） | 直接提交 `Action.Ryuukyoku` |
| 九种九牌（反例 ×3） | 同上 + `kyuushuAfterNakiScript` | 第二巡不再有那一条（手牌没变）／有人碰过之后座位 2 不再有／提交被拒且有中文说明 |
| 四风连打 | `suufonRendaScript`（四家各握一张 1z，Oya 摸进第四张） | 四条 `Dahai` |
| 四风连打（反例） | 同上 | 座位 3 改打 2z → 局面照常推进 |
| 四杠散了 | `suukaikanScript` + 岭上 `2m 3m 9p 3z` | `driveUntil kanSeeking`：Oya 三个暗杠，座位 1 大明杠第四个 |
| 单人四杠不流局 | `singleSuukanScript` + 岭上 `2m 3m 9p 5z` | Oya 一家四个暗杠 → 打完一张局面照常推进 |
| 四家立直 | `suuchaRiichiScript`（四家都是四组刻子 + 字牌单骑） | `driveUntil riichiSeeking`；供托 4 根照出 |
| 三家和了 | `tripleRonScript`（06 的） | 默认规则集下三条 `Hora` → `SanchaHora`，`horas` 为空 |
| 三家和了（反例） | `doubleRonScript` | 两家荣和照样成立 |
| 流し満貫 | `nagashiManganWall ruleset None` | 四家 `passive`（一律摸切、从不鸣牌） |
| 流し満貫（反例：河被鸣走） | `nagashiManganWall ruleset (Some 1z)` | 座位 1 碰掉座位 0 的**最后一张**打牌 |
| 流し満貫（判据 / 清算） | 直接构造 `PlayerState` | `nagashiSeats` / `nagashiDeltas` 的单元用例 |

**`nagashiManganWall` 是程序化摊的**：可摸区整整 70 张都要指定（座位 0 要摸满 18 巡都是幺九牌），
写成记法字符串没人读得下去。它把 `Ruleset.wallTiles` 按幺九 / 中张分开，
座位 0 的配牌与它的每一个摸牌位填幺九牌，其余位填中张（中张只有 84 张，不够的尾巴回落到幺九牌，
那时其余三家早就打过一堆中张了，不会误成立）。

「河被鸣走」那一条**只差一个 `kawaTaken`**：把 `nakiPai` 的第三张换到座位 0 的最后一次自摸上，
那一手可摸区还剩一张（碰得成），而座位 1 碰完不摸牌，**因此座位 0 先前的摸牌一张不受影响**——
它的河仍旧一字不差全是幺九牌，只是被鸣走过。用例断言的正是「河一样、结论不同」。

### 属性

- `任何形态结束后，四家点数与供托之和不变`：七种形态各一条跑到终局的轨迹，逐条验
  `Σscores + kyotaku × RiichiBou` 恒等于局初值，且 `Σdeltas = 0`、`result.Scores = 终局点数`。
- `七种流局形态各有一条轨迹跑到终局，一种不漏`：把 `RyuukyokuReason.all` 与轨迹表对齐——
  **将来再加形态，这条会立刻红**。
- `途中流局一律不授受`、`每条轨迹的事件流都以 ryukyoku 收尾且 JSON 往返不变`。
- `九种九牌的判据只看第一巡与幺九牌种数`（FsCheck，跑在可达局面上）。

## 留给人的待审项

1. **`CONTEXT.md` 的 Ryuukyoku 条目只给了中文**（「四风连打、四杠散了、九种九牌、四家立直」），
   没给罗马字，三家和了也没进条目。本票取的标识符是
   `SuufonRenda` / `Suukaikan` / `SuuchaRiichi` / `KyuushuKyuuhai` / `SanchaHora`（见 DECISIONS 12-A）。
   术语表归人维护，请一并裁。
2. **`Ryuukyoku` 记录没有 `actor` 字段**：mjai 的参考实现在九种九牌那条 `ryukyoku` 上带 `actor`，
   我没加（见 DECISIONS 12-C）。若 13 票的对拍需要它，加一个 `Actor: Seat option` 是小改动。
3. **三家和了时那张牌的振听**：本票走途中流局这条路时**没有**给其余座位记见逃振听
   （这一局立刻结束，振听不跨局），也没有给宣言的三家记什么。若将来要给
   「三家和了之后下一局的振听」立规矩，那是 05 层的事。

## 给 13 票：怎么识别并跳过 Nagashi Mangan 的局

上游 mjai 牌谱的 `ryukyoku` **只有 `deltas`**（13-prep 报告的 A2），没有 `reason`。两条判据：

1. **首选查 oracle**：`tests/fixtures/paifu/tenhou/` 的天凤 JSON 在那一局写的是 `["流し満貫", deltas]`，
   与 `["流局", deltas]` / `["九種九牌"]` 是不同的字符串。**这是唯一可靠的判据。**
2. **只有 mjai 流时看 deltas 的形状**：四麻听牌料的 `deltas` 只可能是
   `{0, ±1000, ±1500, ±3000}` 的组合（`NotenBappu = 3000` 除以 1/2/3 家），
   而流し満貫是**满贯档**：`{±12000, ±8000, ±4000, ±2000}`。
   出现绝对值 ≥ 4000 的 `ryukyoku.deltas` 就是流し満貫，**不是荒牌流局**。
   注意它与 `hora` 无关——事件类型仍是 `ryukyoku`。

顺带：**九种九牌那 6/93 局 13 票不必再跳过了**，本票把 `reason` 补全了；
`13-prep` 报告里「前驱是 `tsumo` ⟹ 九種九牌，前驱是 `dahai` ⟹ 荒牌流局」那条推断判据仍然成立
（我们的引擎产出的事件流也满足它：`Action.Ryuukyoku` 只在摸牌后阶段提交得了）。

## Review 结论（两轴，自己顺序跑的）

**Standards**：
- 一概念一文件、`namespace Janpo`、按领域加进 fsproj 对应 `ItemGroup`、没有重排既有文件 ✅
- 错误是值（`IllegalAction.CannotRyuukyoku`），`step` 对任何输入都返回 `Result` ✅
- 没有通配符 match；47 处漏 match 逐个补齐 ✅
- 规则字面量全部进 `Ruleset`（`SanchaHoraRyuukyoku` / `KyuushuKinds`；四杠散了复用 `RinshanCount`）✅
- 中文只在文档注释与 `toDisplay`；`Ryuukyoku.fs` 不产出任何面向人的字符串 ✅
- 修了自己两处：`applyResponse` 里 `state.Ruleset` 改成 `answered.Ruleset`（与同一函数其余处一致）、
  `CannotRyuukyoku` 的中文里把写死的「九种」改成「种数不够」（种数是规则集的）✅
- nitpick（未改）：`awaitingDahaiActions` 里 `kyuushu` 在立直那两支也会算一遍（`firstTurn` 恒 false，
  结果恒为空表）——与既有的 `hora` / `ankan` / `kakan` 同一写法，为一致性保留。

**Spec**：票里 10 条验收框逐条对齐，全部有用例（见上表）。三条纪律都守住了：
清算替代而非叠加、连庄用 06 的 `isRenchan`（只加了一支，没重写）、`Game.fs` 一个字没动。
