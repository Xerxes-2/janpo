# 黄金用例（双目标防漂移，票 21）

`dual-target.json` 是**用例本身**：输入（`run`）与期望（`expect`）都在数据里，
**不写死在任一侧的代码里**。两侧读的是同一份：

| 侧 | 跑法 | 谁在 CI 里跑它 |
|---|---|---|
| dotnet | `dotnet run --project src/Janpo.Cli -- golden check tests/fixtures/golden/dual-target.json` | `GoldenSuiteTests`（`dotnet test`） |
| 浏览器（Fable → JS） | `cd web && pnpm run verify:golden` | `scripts/ci-web.sh` 的第五道 |

跑与对照的代码是**同一段 F#**（`src/Janpo.Golden/`），两个编译器各编一遍。
因此「一侧红一侧绿」只有一个意思：那条用例的那个字段在两个目标上算出来不一样——
**这时不要改引擎去让它变绿**，先把差异记进 `DECISIONS.md`。

## 加一条用例

1. 往 `cases` 里加一个对象，写 `id`（文件内唯一）、`note`（这条盯的是什么）、
   `run`（跑什么），`expect` 先留 `{}`；涉及符与点数时按 ADR-0004 写清 `ruleset`
   （省略的开关按四麻默认预设补齐，`write` 会把补齐后的值写回文件）。
2. `dotnet run --project src/Janpo.Cli -- golden write tests/fixtures/golden/dual-target.json`
3. **逐行看 diff**——这份文件的对错靠人看 diff 把关，`write` 只负责把当前引擎的输出誊上去。
4. `cd web && pnpm run verify:golden` 确认浏览器侧也一致。

引擎的行为**有意**变了（改了规则、改了文案）也是同一套动作：跑 `write`，diff 里应当
只出现你预期的那几行。

## `run` 的种类

| kind | 输入 | 盯的是 |
|---|---|---|
| `rng` | `seed` / `bound` / `count` | 取数序列与洗牌：xorshift32 的位运算、拒绝采样、Fisher-Yates |
| `wall` | `seed` / `count` | 牌山头几张、宝牌指示牌、岭上牌、四家配牌 |
| `notation` | `tiles` | 牌记法的解析、排序、往返与错误文案 |
| `collection` | `tiles` | `Set` / `Map` / `groupBy` / `sort` 的遍历顺序（含中文串） |
| `shanten` | `naki` / `tiles` | 向听、和了型、有效牌（34 长计数数组那条路） |
| `hora` | `hora`（形态同 `janpo yaku` 的选项） | 役、符与点数授受 |
| `points` | `points`（符 + 番 + 授受条件） | 整数除法与取整：切上到 100、跳满的 3/2、本场平摊、包的平分 |
| `decide` | `seed` / `steps` / `seat` | 决策包 JSON（票 20），23 票的 Agent 层读的就是它 |
| `kyoku` | `seed` | 一局：逐条 mjai 事件 + 终了点数与顺位 |
| `game` | `seed` | 一整场：局数、逐条事件、终局精算的点数与顺位 |

加一种新的 `kind` 要改三处：`GoldenRun` 的 case、它的编解码、`GoldenObservation.run`
的那一支（编译器会把三处都指出来）。
