# 20 — 决策包：Observation 投影与编号动作集（报告）

**结论：done。** 全量 586 条测试绿（本票新增 31 条），`dotnet fantomas .` 干净，`scripts/check-style.sh`
与 `scripts/ci.sh` 全绿。纯引擎票，没碰 `web/`、`package.json`、`scripts/ci.sh`、`flake.nix`（19 号票的地盘）。

## 做了什么

新增两个引擎文件、给三个既有文件加了东西：

| 文件 | 内容 |
|---|---|
| `src/Janpo.Engine/Observation.fs`（新） | `KawaEntry` / `MaskedSeat` / `RevealedSeat` / `Observation` / `GodView` 五个类型，`Observation.ofState`、`GodView.ofState` 两个投影与各自的 encoder |
| `src/Janpo.Engine/DecisionPackage.fs`（新） | `ActionOption`（私有表示）、`DecisionPackage`（私有表示）、`forSeat` / `tryAction` / `encoder` |
| `src/Janpo.Engine/Action.fs` | `Action.encoder`（mjai 动作消息，单向）与 `Action.toDisplay`（中文 label，ADR-0001 的渲染层出口） |
| `src/Janpo.Engine/Kyoku.fs` | `Kyoku.runSteps`：把一局往前推最多 N 手（CLI 要停在中局） |
| `src/Janpo.Cli/Program.fs` | `janpo decide` 子命令 |

测试：`ObservationTests`（11）、`ObservationProperties`（4）、`DecisionPackageTests`（9）、
`DecisionPackageProperties`（5）、`KyokuTests` 加 2 条。

## 边界长什么样（23 / 24 号票直接照这个写）

```
F# ──决策包 JSON（单向 encode）──▶ TS Agent 层
F# ◀──────── 一个整数 id ────────  TS
```

`DecisionPackage.forSeat : Seat -> GameState -> DecisionPackage option`
（**只有正在被问的座位有包**；不在合法动作集里、或不是这个规则集的座位 → `None`）

`DecisionPackage.tryAction : int -> DecisionPackage -> Action option`
（**越界 / 不存在一律 `None`，不抛**；兜底路径靠它）

`DecisionPackage.encoder : Encoder<DecisionPackage>`（**没有 decoder**）

JSON 形状（`janpo decide 42` 可现场看）：

```json
{
  "seat": 0,
  "observation": {
    "seat": 0, "bakaze": "1z", "kyoku": 1, "honba": 0, "kyotaku": 0,
    "dora_markers": ["1z"], "wall_remaining": 69,
    "self": {
      "seat": 0, "jikaze": "1z", "junme": 1, "score": 25000,
      "tehai": ["5m","5m","5mr","7m","8m","6p","7p","9p","9p","5s","9s","1z","5z","7z"],
      "tsumo": "7p",
      "kawa": [{"pai": "1z", "tsumogiri": false}],
      "naki": [{"type": "pon", "target": 1, "pai": "5m", "consumed": ["5m","5mr"]}],
      "riichi": "none", "ippatsu": false,
      "furiten": {"permanent": false, "doujun": false}
    },
    "others": [ { …同上，但**没有 tehai / tsumo / furiten**，多一个 "relative": 1 … } ]
  },
  "actions": [
    {"id": 0, "label": "立直宣言", "action": {"type": "reach", "actor": 0}},
    {"id": 1, "label": "手切1万", "action": {"type": "dahai", "actor": 0, "pai": "1m", "tsumogiri": false}}
  ],
  "scaffold": {}
}
```

- `others` 里 `relative` 是相对观测者第几家（1 下家、2 对家、3 上家）。三麻没有对家，
  因此这里是整数而不是三选一的枚举。
- `riichi` 三值：`"none"` / `"declared"` / `"accepted"`（宣言与成立是两步）。
- `action` 整块就是一条 **mjai 动作消息**（`type` 用 mjai 的拼法：`reach` / `daiminkan` /
  `ryukyoku`），与本项目自己的 `id` / `label` 分开放。
- **`scaffold` 现在恒为 `{}`**，24 号票往里加 Shanten / Ukeire / 进退向，25 号加 Danger。

上帝视角另一条路：`GodView.ofState : GameState -> GodView` + `GodView.encoder`，
形状是「场况 + `uradora_markers` + `seats`（每家都带 `tehai` / `tsumo` / `furiten`）」，
**没有** `self` / `others` / `relative` —— 它没有观测者。

## 关键取舍

1. **上帝视角是独立类型，不是带 flag 的 Observation。** 两者共用的只有「一家亮着的样子」
   （`RevealedSeat`）与私有的取值 / 编码模块 `SeatProjection`。`Observation` 里他家是
   `MaskedSeat`——**那个类型里根本没有 `Hand` 字段**，投影函数想漏也没地方放。
   属性测试因此是佐证不是保障。

2. **手切 / 摸切从事件流读，不从 `PlayerState.Kawa` 读。** `Kawa` 只存牌，摸切与否只有
   mjai `dahai` 事件的 `tsumogiri` 知道。有一条属性钉住两者逐张一致。

3. **`Action.encoder` / `Action.toDisplay` 放进 `Action.fs`**，与 `Tile` / `Kaze` / `Naki`
   把 wire 与渲染出口放在类型旁边的做法一致。代价是 `Action.fs` 头上那句「加一个 case 的代价
   固定为三处」改成了五处——两处新增都是穷尽 match，编译器当面抓。

4. **`ActionOption` 与 `DecisionPackage` 的表示是私有的。** 外面构造不出一条 `ActionOption`，
   因此包里的动作恒是引擎自己给出的合法动作；`Action` 只能经 `tryAction` 取回。

5. **`scaffold` 是 wire 上的空对象，F# 类型里没有对应字段。** 这样 23 号票的 TS 类型现在就能
   写 `pkg.scaffold`，而 24 号票加字段时只改一处 encoder + 一处记录。

## 实测数字

`scripts/fsi/` 的探针（`dotnet fsi` 直调引擎 DLL，未移植任何逻辑），40 局 × 覆盖型随机选手：

```
packages=3425  multiNaki=80  responsePackages=685  labelClashes=0  maxOptions=15
```

- `multiNaki=80`：**80 个决策包里有不止一条鸣牌**（同一张牌的不同亮法 / 碰与吃并存），
  也就是「label 必须能把两种亮法分开」这条属性不是空跑。
- `labelClashes=0`：3425 个包里没有一个包出现两条同名 label。
- `maxOptions=15`：单个决策包最多 15 条动作（LLM 的工具 schema 规模由此可估）。

## 两轴 review 结论（fixed point `bb820d78`，顺序自跑）

无法派生 sub-agent，按 RUNBOOK 自己顺序跑了 Standards 与 Spec 两轴。

**Standards**（对着 `docs/agents/fsharp-style.md` + `CONTEXT.md` + ADR-0001/0002/0004 + Fowler 味道基线）
—— blocking 3 条，已全部修掉并重跑全量测试：

1. `Program.fs` 注释里一个错别字（「胉眼」→「肉眼」）。
2. 两处 `|> fun x -> …` 的「管道里的 lambda」（`ObservationTests.occurrences`、
   `DecisionPackageTests` 取合法动作集）——规则 1 的那条形状，改成算术表达式与具名中间值。
3. `SeatProjection.riichi` 写成了「match 的最后一支后面接 `|> Encode.string`」，
   靠 offside 规则才作用到整个 match，读的人要愣一下。改成 `text >> Encode.string`
   （与 `Tile` / `Kaze` 的 `toMjai >> Encode.string` 同形）。

nitpick，**只记录不改**：

- `Observation.encoder` 与 `GodView.encoder` 各自重复了六行场况字段（`bakaze` / `kyoku` /
  `honba` / `kyotaku` / `dora_markers` / `wall_remaining`）。抽成一个六参数的 helper 反而是
  Data Clumps，不如留着；真要治得先有一个「场况」类型，那是别的票的事。
- `DecisionPackageProperties` 里 `not (List.contains seat (asked state))` 每个座位重算一次
  `asked state`（四次，测试里无所谓）。

**Spec**（对着票 20 与 `CONTEXT.md` 的六个词条）—— 八条验收框逐条对上，无缺项。
两处 scope 加项已在 DECISIONS 20-5 记明（`ippatsu` / `relative`，都是公开信息的搬运）。
两处刻意不做已在 DECISIONS 20-6 与提案 20-A 记明（phase 标记、抢杠窗口里的杠宣言）。
脚手架数值一个都没塞进来（`scaffold` 恒为 `{}`）。

## 留给人的待审项

1. **抢杠那一窗口，投影里看不见「有人宣言了杠」。** 引擎在等抢杠时**不改局面**（副露与
   `kakan` 事件都还没落），因此 `Observation` 里既没有那组杠也没有被抢的那张——它只出现在
   Hora 那一条动作的 `pai` 与 label 里。决策做得了，但围观视角与 25 号票的 Danger 可能想要它。
   见 DECISIONS 的提案 20-A。
2. **投影不带 phase 标记。** 「现在等我干什么」完全由动作列表表达（有 `none` 就是响应阶段）。
   如果 23 号票渲染 prompt 时发现需要一个显式的阶段字段，那是加一个字段的小改动。
3. **两立直与立直在 wire 上不分。** `riichi` 只有三值；`RiichiDeclaration` 的分别是算番的事。
4. `Observation.ofState` 每次会把事件流走 4 遍（每家一遍取河）。一局百来条事件，
   一次投影几十微秒量级，没优化的必要；真要优化就一次 fold 出四家的河。

## 没做的（守边界）

- 不算 Shanten / Ukeire / ShantenDelta / Danger（24、25 号票）。
- 不碰 Fable / `web/` / `package.json` / `scripts/ci.sh` / `flake.nix`（19 号票）。
- 没有 decoder：决策包是单向出口，ADR-0002 明确不存在「局面快照格式」，
  别有人把这份 JSON 当成可以解回去的局面。
