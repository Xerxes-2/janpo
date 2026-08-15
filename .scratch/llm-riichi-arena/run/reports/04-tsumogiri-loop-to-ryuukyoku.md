# 04 — 摸打循环跑到荒牌 Ryuukyoku · 实现报告

**Status:** done（票已标 `ready-for-human`）
**工作区:** `/home/xerxes2/janpo-ws-a`
**Fixed point:** `067b79e4`
**验证:** `./scripts/ci.sh` 全绿（223 个测试通过、`fantomas --check` 干净、引擎依赖白名单通过）；
本票新增 36 个测试，另做了 4 次变异验证（见末节）。

M0 的第一颗真子弹：`janpo kyoku 42` 现在能让四个随机 Player 无头打完一整局，
70 条 `tsumo` + 70 条 `dahai` + 1 条 `ryukyoku`，点数按听牌家数结算。

---

## 公开签名

```fsharp
// Action.fs —— Player 提交的意图（可以非法），与 Event（既成事实）是两个类型
[<RequireQualifiedAccess>]
type Action =
    | Dahai of actor: Seat * pai: Tile * tsumogiri: bool
type LegalActions = { Seat: Seat; Actions: Action list }   // 合法动作集：等谁、能做什么
Action.actor : Action -> Seat

// PlayerState.fs —— 引擎内某座位的牌局状态
type PlayerState                                            // 私有记录
PlayerState.ofHaipai : int -> Tile option -> Tile list -> PlayerState
PlayerState.hand / kawa / score / drawn / tedashi           // tedashi = 手牌去掉刚摸进那张
PlayerState.isTenpai : TileKindSet -> PlayerState -> bool   // ← Shanten.value = 0，唯一一份
PlayerState.draw / discard / addScore

// GameState.fs —— 引擎权威状态与唯一入口
type KyokuFlags = { Haitei: bool; Houtei: bool }            // 只置位，不判役
type AwaitingDahai   = { Actor: Seat; Tsumo: Tile; Actions: Action list }
type AwaitingResponse = { Target: Seat; Pai: Tile; Responses: LegalActions list }
type Phase =
    | AwaitingDahai of AwaitingDahai        // 摸牌后阶段
    | AwaitingResponse of AwaitingResponse  // 他家打牌后的响应阶段（本票恒为空，见下）
    | Ended of Ryuukyoku
type GameState                                              // 私有记录
type IllegalAction =
    | SeatOutOfRange of actor: Seat * seatCount: int
    | NotYourTurn of actor: Seat * awaiting: Seat list
    | NotInHand of actor: Seat * pai: Tile
    | TsumogiriMismatch of actor: Seat * pai: Tile * tsumogiri: bool
    | KyokuAlreadyEnded

GameState.start : Ruleset -> KyokuContext -> Rng -> Result<GameState * Rng, KyokuStartError>
GameState.step  : GameState -> Action -> Result<GameState * Event list, IllegalAction>
GameState.legalActions : GameState -> LegalActions list     // 空列表 ⟺ Kyoku 已终
GameState.ruleset / context / wall / players / player / scores / phase / flags / events
GameState.isEnded / ryuukyoku                               // ryuukyoku: 终局结果，未终为 None
IllegalAction.toDisplay : IllegalAction -> string

// Kyoku.fs —— 一局的驱动与选手抽象
type Player<'player> = 'player -> GameState -> LegalActions -> Action * 'player
type KyokuError = CannotStart of KyokuStartError | Illegal of IllegalAction
Kyoku.run : Player<'p> -> 'p -> GameState -> Result<GameState * 'p, IllegalAction>
Kyoku.randomPlayer : Player<Rng>
Kyoku.runRandom : Ruleset -> KyokuContext -> Rng -> Result<GameState * Rng, KyokuError>
KyokuError.toDisplay : KyokuError -> string

// Event.fs —— 新增两个 mjai 事件
type RyuukyokuReason = Fanpai                               // wire: "fanpai"（荒牌流局）
type Ryuukyoku = { Reason: RyuukyokuReason; Tenpais: bool list; Deltas: int list; Scores: int list }
type Event = … | Dahai of actor * pai * tsumogiri | Ryuukyoku of Ryuukyoku
RyuukyokuReason.all / toMjai / parse / encoder / decoder

// Ruleset.fs —— 新增一个规则开关
Ruleset.NotenBappu : int                                    // 听牌料，四麻预设 3000
// KyokuStart.fs —— 新增一个字段
KyokuStart.Tsumo : Tile                                     // Oya 摸进的第一张
```

CLI：`janpo kyoku <种子> [--no-akadora]` —— 每行一个 mjai JSON 事件，最后一行
`scores: 26500 23500 26500 23500`。退出码沿用 0 / 1（数据错）/ 2（用法错）。
顺手把 `deal` 与 `kyoku` 共用的参数解析抽成了一个 `parseSeedArguments`。

## 后续票要往哪里加东西

| 票 | 加什么 | 改哪几处 |
|---|---|---|
| 06 Hora / 10 Pon·Chi / 11 Kan | 他家打牌后的响应 | `GameState.responsesTo`（现在恒返回 `[]`）——一填东西，`step` 就会真的停在 `AwaitingResponse` 上；再给 `Action` 加 case、给 `step` 的执行 match 加一支 |
| 06 自摸和 / 09 立直 / 11 暗杠·加杠 | 摸牌后能做的事 | `GameState.awaitingDahaiActions`（摸牌后阶段的动作集）+ `Action` 的 case + `step` 的执行 match |
| 05 Kyoku 循环 | 连庄 / 进局 / 结转 | 读 `GameState.ryuukyoku`（`Tenpais` 判连庄、`Scores` 接下一局）与 `GameState.context` |
| 07 判役 | 上下文标志 | 读 `GameState.flags`（`Haitei` / `Houtei`）；09 的一发、11 的岭上与抢杠往 `KyokuFlags` 加字段 |
| 12 其余流局形态 | 终局形态 | `RyuukyokuReason` 加 case（`toMjai` 与 `parse` 各一行），`Phase.Ended` 那时应换成一个 `KyokuEnd` DU |

**加一个 Action 的代价固定为三处**：`Action` 的 case、产出合法动作集的那个函数、
`step` 里执行动作的 match。漏哪处编译器都会报（全仓 `--warnaserror`，不完整 match 是错误）。
**加一个 Event 的代价固定为三处 + 一处可选**：DU、`Event.encoder`、`Event.decoder`，
再加测试里的 `EventGenerators.Event()`（加了 JSON 往返属性就自动覆盖）。

## 关键取舍

| 取舍 | 选了什么 | 为什么 |
|---|---|---|
| 合法动作集 | `LegalActions list`，每项 `{ Seat; Actions }`，非空；空列表 ⟺ 局终 | 响应阶段会同时等多家；调用方不必先猜「该问谁」。属性把「非空或已终」钉死了 |
| 阶段 | `Phase` DU，前两个 case 各自**携带**自己的动作集，只能由私有构造器产出 | 票的硬要求；算动作集与建阶段是同一处代码，不会漂移 |
| 响应阶段 | 建了类型，但 `responsesTo` 恒为 `[]`，引擎打完牌直接推进 | 本票没有任何可响应的动作。若真停在空响应阶段上，「合法动作集非空」这条不变量当场就破了 |
| 摸切 / 手切 | 是两个不同的 Action，且引擎校验标志 | 手里两张 5m 时推断不出来；09 的「立直后只能摸切」与 12 的 Nagashi Mangan 都要它是声明 |
| 听牌判定 | 一律 `Shanten.value = 0`（`PlayerState.isTenpai`） | 票的接缝纪律。全仓只有这一处判听牌 |
| 牌种集合 | `TileKindSet.ofKinds ruleset.TileKinds`，`GameState` 建局时算一次 | 提案 S-C：不造第三份表示 |
| 上下文标志 | `KyokuFlags` 挂 `GameState`，不挂阶段 | 07 判役时一次取到；每个阶段抄一份迟早对不上 |
| 事件流 | `GameState` 内部保存（倒序），`GameState.events` 取正序 | ADR-0002：Paifu 是唯一可分享物，回放确定性直接比它 |
| 选手 | `Player<'player>` 纯函数 + `Kyoku.run` 在引擎里 | 随机 bot 被 CLI、属性测试与 14 票的 soak 三处用；写进 CLI 就得抄第二份 |
| 听牌料 | `Ruleset.NotenBappu = 3000`，整数除法照 mjai | 02 已立「规则开关进 Ruleset」；公式与 gimite/mjai 的 `3000 / tenpai_ids.size` 逐字一致 |
| 终局形态 | `Phase.Ended of Ryuukyoku`，不提前抽象成 `KyokuEnd` DU | 本票只有荒牌流局一种；06 加和了时把它换成 DU 是一行的事，提前抽象是投机 |

自主决策 10 条 + 2 条提案，记在 `run/DECISIONS.md`。

## mjai wire 的取值（有出处）

- `dahai`：`{"type":"dahai","actor":1,"pai":"7s","tsumogiri":true}` —— 与 Cryolite/mjai 的
  `schema/dahai.json` 示例、Akagi 的 `MjaiEvent` TS 定义一致。
- `ryukyoku`：`{"type":"ryukyoku","reason":"fanpai","tenpais":[…],"deltas":[…],"scores":[…]}` ——
  字段名与取值照 gimite/mjai `lib/mjai/active_game.rb` 的 `process_fanpai`（它同时是听牌料公式的出处：
  `deltas[id] += 3000 / tenpai_ids.size`、`deltas[id] -= 3000 / noten_ids.size`）。
- **没有**照抄 mjai 的 `tehais` 字段（流局时亮听牌家的手牌、其余写 `"?"`）：`Tile` 表示不了「未知牌」，
  而「谁听牌」已经由 `tenpais` 说清楚了。Mortal 的 libriichi 同样不带这个字段。

## 测试（223 个全绿；本票新增 36 个）

- `GameStateTests.fs`（16）— 开局只等 Oya；合法动作集的具体形状（种子 42 的 12 条动作逐条钉死，
  含「两张 5m 只出现一条、5m 与 5mr 各一条、摸切排最后」）；摸切打的就是摸进那张；
  打牌后进河 / 手牌 13 张 / 下家摸牌 / 事件同时进事件流；五种拒绝理由各一条
  （别家出手、座位越界、牌不在手里、谎称摸切、谎称手切）；被拒后局面原样不动；
  局终后一律拒绝；**海底与河底各恰好置位一次**；**听牌料 0/1/2/3/4 家听牌五条黄金用例**；
  听牌与否与 `Shanten` 一致；中文说明。
- `GameStateProperties.fs`（10 条 FsCheck 属性）— 合法动作集非空或已终；牌数守恒（手牌 + 河 + 山
  = 完整一副）；等打牌那家 14 张其余 13 张；**step 接受一个动作当且仅当它在合法动作集里**
  （随便造的动作：座位可能越界、牌可能不在手里、标志可能瞎写——一律返回值，从不抛）；
  合法动作都推得动局面；**回放确定性**（记下一局的动作串重放，局面与事件流逐字节相同）；
  同一种子跑两次同一局；事件流 JSON 往返；荒牌授受和为零且点数 = 局初 + 增减；
  听牌的收、不听的付、全听全不听不授受。
- `KyokuTests.fs`（7）— 四个随机选手打到荒牌流局（70 tsumo / 70 dahai / 结尾 ryukyoku）；
  终局各家 13 张、河合计 70 张；事件流以 `start_kyoku` 开头、一行一个 JSON 可原样解回；
  同种子同局、异种子异局；选手作弊时驱动停下并给理由；开不了局时给开局失败的理由；两种理由的中文。
- `GameStateGenerators.fs` — 可达局面的生成器（随机开一局，用随机选手或「尽量保持听牌」的选手
  走若干步，取轨迹上任一局面）+ 乱造动作的生成器；另有 `trace` / `record` / `replay` 三个固件。
- `EventTests.fs`（+3）、`EventGenerators.fs` — 两个新事件的 wire 形态与生成器。

**听牌料的黄金用例怎么来的**：随机选手几乎永远不听牌（扫了 480 个座位样本，0 家听牌），
所以固件里放了一个「打出后向听最小的那张」的选手，四家都用它，再挑种子凑齐 0/1/2/3/4 家听牌
五种情形（种子 23 / 3 / 1 / 28 / 20），期望值全部写死。

**变异验证**（改实现 → 跑测试 → 确认变红 → 改回）：

| 变异 | 变红 |
|---|---|
| 听牌料收付颠倒 | 3 个（黄金用例 + 两条属性） |
| 河底标志永不置位 | 1 个（海底与河底） |
| 摸切标志不校验 | 2 个（谎称摸切 + 「接受 ⟺ 在集合里」） |
| 打出的牌不进河 | 3 个（牌数守恒 + 打牌后进河 + 河合计 70 张） |

## Review 结论（两轴，fixed point `067b79e4`）

无法派生 sub-agent，按 RUNBOOK 自己顺序跑了两轴。

### Standards（对照 CONTEXT.md、ADR-0001/2/3、01/02/03 立的结构约定）

**已修（本轮自动修）**

1. **Duplicated Code** — `PlayerState` 里 `tedashi` 与 `discard` 各写了一份「从牌列里拿掉一张」的
   递归，抽成私有的 `removeOne : Tile -> Tile list -> Tile list option`。

**只记录不修（nitpick / 判断题）**

2. **Speculative Generality（判断题）** — `Phase.AwaitingResponse` 本票不会被构造。它是票的明文
   要求（「摸牌后阶段与他家打牌后响应阶段用不同类型区分」），且 `step` 里对应的分支是一行
   `Error(NotYourTurn …)`。06 一到就会变成主路径。
3. `Player<'player>` 是泛型的，本票只用 `Rng` 与测试里的固定策略两种实例。留泛型是为了让
   14 票的 soak 与 06 的黄金用例（脚本化选手）不必再造一个接口。
4. `GameState.step` 里 `List.tryItem actor state.Players` 的 `None` 分支不可达（`Seat.isValid` 已经
   保证过）。保留是为了不写 `List.item`（会抛异常）。
5. `Action.fs` 一个文件里放了 `Action` 与 `LegalActions` 两个类型；`GameState.fs` 放了阶段、
   `GameState` 与 `IllegalAction`。按 `KyokuStart.fs` 的先例（错误类型与操作同文件）安排的，
   仍算「一概念一文件」的松解释。
6. `Ruleset` 加了 `NotenBappu` 字段——07 若也动 `Ruleset`，集成时会撞一次（记录以便调度器留意）。

**未发现**违反术语表或 ADR 的地方：标识符全是罗马字或结构性英文；中文只出现在 `toDisplay`
与 CLI 的打印里；引擎内部诊断串（decoder 的失败信息）是英文；事件字段与 mjai wire 1:1。

### Spec（对照票 04 的 9 条验收 + spec.md 第 85 / 88 / 128 行）

- 9 条验收**全部满足**，逐条勾在票文件里。第 2 条有一处必须说清楚的限定：响应阶段的类型建好了、
  也带着自己的合法动作集，但**本票里它恒为空且引擎不会停在那里**——因为 04 的动作集里没有任何
  可响应的动作，真停下来就会破坏「合法动作集非空或 Kyoku 已终」这条同票的验收。
- spec.md 第 85 行的核心签名 `step : GameState -> Action -> Result<GameState * Event list, IllegalAction>`
  逐字落地；第 88 行的「阶段用类型区分」见上；第 128 行点名的三条不变量
  （牌数守恒、非法动作返回错误而非异常、回放确定性）都落成了属性。
- **超出票面的部分（scope creep，均为有意）**：`Player` / `Kyoku.run` / `Kyoku.randomPlayer`
  （CLI 的「四个随机 Player」与属性测试都要）、`Ruleset.NotenBappu`（不想把 3000 写死）、
  `KyokuStart.Tsumo`（`GameState.start` 需要）、CLI 里 `deal` 与 `kyoku` 的参数解析合并。
- **未做（不属于本票）**：Junme（巡）没有维护——04 的验收里没有它，06 / 09 的同巡振听与一发到时候
  会需要，那时加在 `GameState` 上。

## 留给人的待审项

1. **`Ryuukyoku` 的拼法**（DECISIONS 04 第一条 + 提案 04-B）：F# 标识符用术语表的 `Ryuukyoku`，
   wire 上写 mjai 的 `"ryukyoku"`。派工时说的是「case 名 = mjai 事件名转 PascalCase」，
   我按 RUNBOOK 里「CONTEXT.md 是标识符命名的唯一权威」这条选了术语表拼法。
   要翻的话是一次机械重命名（4 处）。
2. **提案 04-A**：`Kawa` / `NotenBappu` / `Haitei` / `Houtei` / `Phase` / `Tsumogiri` 补进 CONTEXT.md。
3. **响应阶段恒为空**这件事请在 review 06 / 10 时一并看：它们必须同时做两件事——往 `responsesTo`
   里填动作、并给「所有人都不响应」加一条 `Action`（mjai 的 `none`），否则响应阶段一停下来
   就没人能把它推走。
4. **03 的听牌边界**（四张全在手里的单骑不算听牌等）直接决定听牌料的授受。我这票没有与它矛盾的
   黄金用例，全部按现有 `Shanten` 的行为写；若早上裁决改了 `Shanten`，本票的五条听牌料用例
   （种子 23 / 3 / 1 / 28 / 20 的期望值）需要重新生成。
5. `NotenBappu` 用整数除法，要求它能被 `1 .. SeatCount-1` 整除（3000 对四麻与三麻都成立）。
   若将来有规则集不满足，授受会不平——已在字段文档里写明。
