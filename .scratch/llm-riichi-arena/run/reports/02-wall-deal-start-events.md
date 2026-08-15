# 02 — 牌山、配牌与开局事件 · 实现报告

**Status:** done（票已标 `ready-for-human`）
**Change:** `wmnqytql` — `feat: 牌山、配牌与 mjai 开局事件`
**Fixed point:** `aa71bed2`
**工作区:** `/home/xerxes2/janpo-ws-a`（jj workspace）
**验证:** `./scripts/ci.sh` 全绿（宿主机 dotnet 10.0.111；**112 个测试通过**，`fantomas --check` 干净，
引擎依赖白名单通过、没有新增 NuGet 包）

---

## 做了什么

### 新增的 7 个引擎模块（按 fsproj 的编译顺序，也就是依赖顺序）

| 文件 | 概念 | 对外的主要 API |
|---|---|---|
| `Kaze.fs` | 风（场风 / 自风） | `Ton`/`Nan`/`Shaa`/`Pei`、`all`/`ofIndex`/`index`、`toMjai`/`parse`、`encoder`/`decoder`、`toDisplay` |
| `Ruleset.fs` | **规则集**：座位数与牌山构成的唯一出处 | `yonma`、`withoutAkadora`、`wallSize`、`haipaiTotal`、`wallTiles` |
| `Seat.fs` | 座位（`type Seat = int`） | `all`/`orderFrom`/`next`/`isValid`（座位数全部从规则集读） |
| `Rng.fs` | 种子化确定性发生器 | `ofSeed`、`nextBelow`、`shuffle` |
| `Wall.fs` | 牌山（可摸区 + 王牌 + 指示牌） | `build`、`draw`、`deal`、`remaining`、`tiles`、`deadWall`、`rinshan`、`doraIndicators`、`uraIndicators`、`revealIndicator` |
| `Event.fs` | mjai 事件 | `Event`（3 个 case）、`StartKyoku` 载荷、`encoder`/`decoder` |
| `KyokuStart.fs` | 开局 | `KyokuStart.create`、`KyokuContext.initial`、`KyokuStartError.toDisplay` |

### Event 的形状（这是本票最要紧的一处设计）

```fsharp
type Event =
    | StartGame of names: string list
    | StartKyoku of startKyoku: StartKyoku      // 8 个字段，另立记录载荷
    | Tsumo of actor: Seat * pai: Tile
```

**加一个 case 的代价固定为三处**，漏掉哪处编译器都会报（全仓 `TreatWarningsAsErrors`，
不完整 match 是错误）：

1. DU 加 case——字段少的内联具名字段；字段多、或含多个同类型标量（`kyoku`/`honba`/`kyotaku`
   三个 int 挨着，位置传错编译器抓不住）的另立记录载荷，照 `StartKyoku` 抄。
   case 名取 mjai 事件名转 PascalCase（`dahai` → `Dahai`），不自创。
2. `Event.encoder` 加一支：`mjaiEvent "<mjai 事件名>" [ 字段名, 编码 … ]`。
3. `Event.decoder` 的 `type` 分派加一支，与第 2 步逐字段对称。

第四处是**可选的**：`EventGenerators.fs` 的 `Event()` 生成器里加一支，JSON 往返的属性测试就自动
覆盖到新 case。`Event` **没有** `toDisplay`——理由见 DECISIONS。

产出的 wire 形态（`janpo deal 42`，每行一个 JSON 事件）：

```json
{"type":"start_game","names":["p0","p1","p2","p3"]}
{"type":"start_kyoku","bakaze":"1z","dora_marker":"1z","kyoku":1,"honba":0,"kyotaku":0,"oya":0,
 "scores":[25000,25000,25000,25000],"tehais":[[13 张],[13 张],[13 张],[13 张]]}
{"type":"tsumo","actor":0,"pai":"7p"}
```

（真实输出每个事件都在一行，上面为了可读折了行。）

### 规则配置放在哪

`Ruleset.fs` 的 `Ruleset` 记录，预设是 `Ruleset.yonma`。**引擎里 `4` / `136` / `13` / `14` /
`25000` 这些数只出现在 `Ruleset.yonma` 这一个字面量块里**（已 grep 核对：其余的 `4` 只有
`Wall.haipaiChunks` 里那个具名的 `perRound`，指的是一次抓 4 张的取牌手顺，与座位数无关）。

```fsharp
{ SeatCount = 4; TileKinds = Tile.kinds; CopiesPerKind = 4; Akadora = Tile.akadoraKinds
  HaipaiSize = 13; DeadWallSize = 14; RinshanCount = 4; StartingScore = 25000 }
```

- 牌山构成 = `Ruleset.wallTiles`：每种正牌各 `CopiesPerKind` 张，`Akadora` 列出的每种**换掉**
  一张对应正牌（不是加）——所以开红与不开红都恰好 136 张。
- 红宝牌开关 = `Ruleset.withoutAkadora`（CLI 的 `--no-akadora`）。
- **三麻的门没焊死**：`KyokuStartTests` 里有一条 `三麻形状的规则集也开得出局`，
  用 `{ SeatCount = 3; TileKinds = 去掉 2m-8m; Akadora = 去掉 5mr }` 开局，断言 108 张守恒、
  三家各 13 张、牌山里没有 5m。**这不代表支持三麻**（没有拔北、没有自摸损、向听在 corner case
  上也不同），只证明「座位数与牌山构成是参数」。

### 确定性洗牌

`Rng` 是 xorshift32（`[<Struct>]` 私有记录，一个 `uint32` 状态），只用位移与异或，
`nextBelow` 拒绝采样去掉取模偏差，`shuffle` 是 Fisher-Yates。不用 `System.Random`——
它在 dotnet 与 Fable(JS) 两侧实现不同，跨目标不可复现，而「同种子同牌山」正是 M1 黄金用例
与 soak 复现的地基。

两条**跨目标黄金用例**已经钉在测试里，M1 编到 JS 之后必须一字不差：

- `RngTests.固定种子产出固定的取数序列`：种子 0 / 42 / -1 各 8 个数。
- `WallTests.固定种子建出固定的牌山`：种子 42 的可摸区头 6 张 = `7z 5z 5m 9p 2p 8s`。

### CLI

```
janpo deal <种子> [--no-akadora]
```

退出码沿用 0 / 1（数据错，即 `KyokuStartError`）/ 2（用法错：种子缺失或不是整数）。

### 测试（112 个，全绿；本票新增 66 个）

| 文件 | 内容 |
|---|---|
| `KazeTests.fs` | 9 个：记法是 `1z`-`4z`、记法就是对应风牌（与 `Tile` 同源）、序号互逆、JSON 往返、中文渲染 |
| `RulesetTests.fs` | 8 个：136 张、每种 4 张、红宝牌换而不加、关掉红宝牌、升序规范形、牌山张数随构成变 |
| `SeatTests.fs` | 4 个：枚举、下家绕回、从亲起的顺序、越界不合法（都用 4 家与 3 家两个规则集对照） |
| `RngTests.fs` + `RngProperties.fs` | 7 + 6：黄金序列、种子 0 不退化、401 个种子洗出 401 个不同结果、取模无偏（4 万次落在 ±5%）、洗牌是排列、同种子同结果 |
| `WallTests.fs` + `WallProperties.fs` | 12 + 7：张数取自规则集、王牌结构、指示牌翻开节奏、**配牌 4-4-4-1 手顺**（按洗好的可摸区逐位置比对，四个亲位都验）、摸空后 `draw` 返回 `None` 而王牌不动、牌数守恒 |
| `EventTests.fs` + `EventProperties.fs` | 8 + 3：三个事件的**逐字节 JSON**、一行不含换行、未知事件 / 缺字段 / 非法记法 / `E` 记法都解码为错误值、任意事件往返不变 |
| `KyokuStartTests.fs` + `KyokuStartProperties.fs` | 15 + 6：事件顺序、13/14 张、指示牌来自王牌、同种子同局、四类错误值与它们的中文渲染、**开局后 136 张守恒无重复无丢失** |

**属性测试做过变异验证**（改实现看是否变红，改回即绿）：

| 变异 | 报红 |
|---|---|
| 红宝牌由「换」改成「加」 | 10 |
| 配牌不排序 | 2 |
| Oya 不把摸进的牌并入手牌 | 6 |
| 去掉种子 0 的不动点保护 | 4 |
| 王牌与可摸区重叠一张 | 4 |

## 关键取舍

| 取舍 | 选了什么 | 为什么 |
|---|---|---|
| 随机源 | 自带 xorshift32 | `System.Random` 跨目标不可复现；只用位移异或是 dotnet/JS 语义完全一致的子集 |
| 规则集 | 公开记录 + 预设 + 开关组合子 | 后面 6 张票要往里加开关，公开记录加字段是一行；私有记录要一个开关一个组合子 |
| 规则集校验 | 不做校验类型，把不自洽变成 `KyokuStartError` | 「已校验规则集」类型会把 `Result` 推给所有下游；将来从 JSON 读规则再加 `create` 是纯增量 |
| `bakaze` | `Kaze` 类型，wire 上写 `1z`-`4z` | `Tile` 会让 `5m` 当场风；`E`/`S`/`W`/`N` 已被 ADR-0001 否决（有测试钉住不接受） |
| `Seat` | `type Seat = int` 透明别名 + `Seat` 模块 | mjai 的 `actor` 就是个 int，13 张票天天用；收紧成包装类型是一次机械替换 |
| 配牌手顺 | 4-4-4-1，发完排序 | 贴日麻实际手顺，复盘不必解释；顺序不携带信息，排序让固件与 diff 稳定 |
| `start_game` | 由调用方构造，引擎只产 `start_kyoku` + `tsumo` | 不在 02 就替 05 定 Game 层的形状；构造一个无逻辑的事件不算逻辑跑进 CLI |
| `Event.toDisplay` | 不做 | 会把「加一个 case」的代价从三处变四处，而后面 12 张票都在加 case |

自主决策共 10 条，逐条记在 `run/DECISIONS.md`（含 1 条待人裁决的提案 02-A）。

## Review 结论（两轴，fixed point `aa71bed2`）

无法派生 sub-agent，按 runbook 自己顺序跑了两轴。

### Standards（对照 `CONTEXT.md`、ADR-0001/0002/0003、01 票立下的结构约定、Fowler smell 基线）

**已修（本轮自动修）**

1. **测试踩在未写明的不变量上** — `WallTests` 用 `Wall.tiles |> List.truncate (Wall.remaining w)`
   取「洗好但没发的可摸区」，而 `tiles` 的文档没说顺序是「先可摸区后王牌」。已把顺序写进
   `Wall.tiles` 的文档注释，测试站在**写明的**公开行为上。
2. **验收项 2 缺直接证据** — 「三麻的门没焊死」原本只由 `SeatTests`（3 家规则集）与
   `RulesetTests`（筛过的牌种）间接证明。已补 `三麻形状的规则集也开得出局`：3 家 108 张、
   无 2-8m、无红 5m，走完整条开局路径并断言守恒。
3. 测试里两处中文变量名（`只有万子` / `没有万子`）已改回罗马字/英文——中文只属于测试**名**
   与渲染出口，标识符一律不用（ADR-0001）。

**只记录不修（nitpick / 判断题）**

4. `Wall.fs` 的 `haipaiChunks` 里有个 `perRound = 4`。它是「一次抓 4 张」的取牌手顺，
   与座位数、牌山张数无关，不在票要求集中的那类字面量里。若将来某个规则集手顺不同，
   把它提进 `Ruleset` 是一行。
5. `KyokuStart.create` 有三层嵌套 match（校验 → `deal` → `draw` + 指示牌）。可以用
   computation expression 摊平，但会为一个 40 行的函数引入一套自定义 builder，没做。
6. 公开但本票没有消费者的 API：`Wall.rinshan` / `uraIndicators` / `revealIndicator`（11 票的杠、
   09 票的里宝牌要用）、`Seat.next`（04 票的摸打推进）、`Kaze.ofIndex`/`toDisplay`（05 票的场风推进、
   UI）。都有测试覆盖，且都是「王牌 14 张独立留出」这个验收项的自然完成面。
7. `Event.fs` 与 `Kaze.fs` 都 `open Thoth.Json.Core`，即领域类型文件依赖 JSON 库——沿用 01 票
   `Tile.fs` 的判断（wire 形态与记法同源）。若后续票觉得碍事，拆 `*Json.fs` 是机械操作。
8. `Ruleset` 是公开记录，因此**可以**手搓出不自洽的值（如 `SeatCount = 0`）。所有引擎函数对此
   都是「返回值而不是抛异常」（`Seat.next` 对非正座位数原样返回、`Wall.deal` 返回 `None`、
   `KyokuStart.create` 返回具名错误），但没有一处集中的校验。见 DECISIONS 里的取舍。

**未发现**违反术语表或 ADR 的地方：标识符全是罗马字或 CONTEXT.md 已用的英文结构词，
中文只在 `Kaze.toDisplay` / `KyokuStartError.toDisplay` / 测试名 / CLI 文案里，
事件流与固件一律 mjai 记法，引擎内部诊断串（两个 Decoder 的失败信息）是英文。

### Spec（对照票 02 的 9 条验收 + `spec.md` 相关段落）

- 9 条验收**全部满足**，逐条见票文件的勾选。
- **一处需要人确认的读法**：「产出 mjai 事件：**对局开始**与 Kyoku 开始」——`start_game` 这个
  case、它的编解码与测试都在引擎里，但**产出它的那一步**在调用方（CLI）而不是引擎函数里，
  因为 `names` 来自配桌、`start_game` 是对局级事件（05 票的 Game 层）。若这条读法不合意，
  05 票加 `Game.start` 时把它收回引擎即可，`Event` 侧不用改。
- **超出票面的部分（scope creep，均为有意）**：
  - `Wall.revealIndicator` / `uraIndicators` / `rinshan`：王牌结构一旦定下来，这三个是它的自然
    完成面，且 09/11 票马上要用；不做的话 11 票要回头改 `Wall` 的私有表示。
  - `Kaze` 类型：票只要求 `start_kyoku` 里有场风，用 `Tile` 也能交差，但那样 `5m` 能当场风。
  - `Seat` 模块与 `Rng` 模块本身：票没点名，但「座位数不写死」与「种子化洗牌」两条验收要它们。
- **未发现**实现与票面相悖的地方。

## 留给人的待审项

1. `run/DECISIONS.md` 的 10 条 02 决策，尤其是三条会影响后续票的：
   **Event 的形状**（内联 vs 记录载荷的分界）、**`Ruleset` 公开且不做校验**、
   **`Seat = int` 透明别名**。这三条后面 12 张票都会照抄，要推翻趁早。
2. **提案 02-A**：把 `Ruleset` / `Wall` / `DeadWall` / `Rinshan` / `Haipai` / `Kaze` /
   `DoraIndicator` / `Rng` 补进 `CONTEXT.md`；另外 `Seat` 条目写死的「0-3」若要纳入三麻需改口径。
3. 场风在 wire 上写 `1z`-`4z` 而不是 mjai 生态原生的 `E`/`S`/`W`/`N`。这是 ADR-0001 的直接推论，
   已按它落地并写了测试，但**与生态牌谱互转时风牌要过一次映射**——13 票做对拍时会第一次撞上。
4. 两条黄金用例（Rng 的取数序列、种子 42 的牌山头 6 张）钉的是**当前算法**。任何对 `Rng` 或
   `Wall.build` 的改动都会让它们变红——那是它们的目的（跨目标漂移探针），不是脆弱测试。
5. `Ruleset.yonma` 的 `StartingScore = 25000` 与「没有 `Kiriage` / `Kuitan` / 对局长度」等开关，
   是本票只落地了用得到的字段。提案 S-A 若被采纳（命名规则集 `Tenhou` / `MahjongSoul`），
   加预设就是往 `Ruleset` 模块里加值。
