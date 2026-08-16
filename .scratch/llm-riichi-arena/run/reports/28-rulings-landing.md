# 28 — 裁决落地：决策包用例逐字段、Roster 进术语表、脚手架档位的判据

工作区 `janpo-ws-a`（jj workspace），fixed point `3dfc5674`（change `nxpwouzp`）。
`./scripts/ci.sh` 全绿（含浏览器内 40 条黄金用例 / 1947 字段 / 3210 行、曳光弹对拍、牌谱导出回放），
`dotnet fantomas .` 干净，`pnpm run check` 与 `pnpm run typecheck` 干净。

三节都落地了，**一处与票面写法不同**（第 1 节的 decoder 落在 `Janpo.Golden` 而不是引擎里），
理由与代价在 §1.2 与 `DECISIONS.md` 的 28-1，**留给人复核**。

---

## 1. 决策包用例拆成逐字段（裁决 21-c）

### 1.1 新旧报错对照（这一节的证据）

把 `decide-42-first-hand` 的**同一个字段**（座位 1 的 `tehai_count`，13 改成 12）改坏，
两种形态各跑一遍 `janpo golden check`：

**旧形态**（`package` 整行钉住）——`17,276` 字节，两条 8 KB 的长行，字段藏在中间：

```
40 条用例、192 个字段、1439 行：1 处对不上
用例 decide-42-first-hand：字段 package 第 0 行：期望「{"seat":0,"observation":{"seat":0,"bakaze":"1z",
"kyoku":1,"honba":0,"kyotaku":0,"dora_markers":["1z"],"wall_remaining":69,"self":{"seat":0,"jikaze":"1z",
"junme":1,"score":25000,"tehai":["5m","5m","5mr","7m","8m","6p","7p","9p","9p","5s","9s","1z","5z","7z"],
"tsumo":"7p","kawa":[],"naki":[],"riichi":"none","ippatsu":false,"furiten":{"permanent":false,
"doujun":false}},"others":[{"seat":1,"jikaze":"2z","junme":0,"score":25000,"tehai_count":12,"relative":1,
…（此处省略约 16,000 字节：整包两遍，一遍期望一遍实际）…
```

**新形态**（逐字段）——`180` 字节，**报错本身就是那条路径**：

```
40 条用例、1947 个字段、3210 行：1 处对不上
用例 decide-42-first-hand：字段 package.observation.others.0.tehai_count 第 0 行：期望「12」，实际「13」
```

**再加一条 29 号票会遇到的那种**（引擎多产出一个字段：把 `package.observation.self.riichi`
从期望里删掉，模拟投影新增了它）：

```
40 条用例、1946 个字段、3209 行：1 处对不上
用例 decide-42-first-hand：字段 package.observation.self.riichi 跑出来了，期望里没有（用例数据过期，跑 `janpo golden write`）
```

**一条报错、指着新字段的路径，其余 1946 个字段一条不动。** 这就是这一票存在的理由：
29a 把 `Observation` 换成掩蔽流的 fold、29b 翻转 prompt 时，用例的churn 是可读的。

### 1.2 落地形态：`GoldenJson.fields`，不是引擎里的 `DecisionPackage.decoder`

票面写的是「给 `DecisionPackage` 补 decoder（注释与 DECISIONS 写清它只服务测试）」。
落地成 `src/Janpo.Golden/GoldenJson.fs`：把**已经编好的** JSON 摊成「路径 → 逐行的值」，
读回来的是**文本**，构造不出 `DecisionPackage`、更构造不出 `Action`。

**为什么不照字面做**（三条，完整版在 `DECISIONS.md` 28-1）：

1. 真的 `DecisionPackage.decoder` 必然要 `Action.decoder`，而 **26-2 刚刚把「事实有 decoder、
   意图没有」写成不变量**，`Action.encoder` 的注释也明写「单向出口……意图不上牌谱」。
   票 28 自己强调「产品边界仍是单向的」——照字面做，那条边界就只剩注释在守；
   照现在这样做，「只服务测试」是**结构上的**。
2. 摊平出来的字段名**就是 wire 上的字段名**，比「decode 成记录再重新渲染字段」钉得更死：
   encoder 与 decoder 一起改名也会红（黄金用例的键变了）。
3. 21-2 定过「测试脚手架不进引擎」。

**若主人要的就是引擎里那个 decoder**：那是十几行的活，但上面三条会一起松掉。请裁。

### 1.3 摊平的规则（`GoldenJson.fs`，两个目标编同一段 F#）

| 形状 | 落成什么 | 为什么 |
|---|---|---|
| 标量 | 一个字段一行 | 值原样落地、不带引号——这份文件要给人逐行核对 |
| 全是标量的数组 | 一个字段的多行 | 手牌漂一张，报错指得出第几张 |
| 对象的数组 | 下标进路径（`actions.11.action.pai`） | 加/减一条动作不会让后面全部错位成 `Line` 漂 |
| 空表 / 空对象 | 一个**零行**的字段 | 「一个都没有」也要有位置被钉住 |
| 数字 / true / false / null | 构造那一刻就渲染成文本 | `Json.Number of float` 那条路会让「dotnet 印 69、浏览器印 69.0」成为可能 |

用例文件里长这样（读起来仍像那份 JSON，因为字段序就是 encoder 序）：

```json
"package.observation.self.junme": [ "1" ],
"package.observation.self.tehai": [ "5m", "5m", "5mr", "7m", "8m", "6p", "7p", "9p", "9p", "5s", "9s", "1z", "5z", "7z" ],
"package.observation.self.tsumo": [ "7p" ],
```

### 1.4 重录与逐项核对

`dotnet run --project src/Janpo.Cli -- golden write tests/fixtures/golden/dual-target.json`。
**核对方式**（脚本落在 `reports/28-verify-golden-split.py`，可重跑）：

```
$ python3 .scratch/llm-riichi-arena/run/reports/28-verify-golden-split.py /tmp/28/before.json tests/fixtures/golden/dual-target.json
decide-1177-step-8: 558 个字段，与旧那一行摊平后逐字段逐行相同 ✓
decide-42-first-hand: 578 个字段，与旧那一行摊平后逐字段逐行相同 ✓
decide-99-danger: 622 个字段，与旧那一行摊平后逐字段逐行相同 ✓
非 decide 的用例逐字节相同；decide 的三条只换了粒度，值没变 ✓
```

它把**旧那条 `package` 长行 parse 出来、按新规则摊平**，再与新文件里的字段逐条同序比对——
因此「字段值没变、只是对照粒度变了」这句话是被验过的，不是我看了几眼。
另外 37 条用例的 `expect` 逐字节相同。

**规模变化**（故意付的代价）：用例文件 159,607 → 270,504 字节（2,709 → 7,964 行），
字段 190 → 1,947，行 1,437 → 3,210。三条 `decide` 摊出 1,758 个字段。

### 1.5 一处削弱（记在 `DECISIONS.md` 28-2）

旧那条 8 KB 长行同时钉住了「Newtonsoft 与 Thoth.Json.JavaScript 两个后端序列化逐字节相同」，
现在决策包不再经过宿主的 `toString`。**两个后端仍被 1,238 条 mjai 事件逐行钉着**（`kyoku` / `game`
用例走的还是 `toText`），而决策包这侧真正要防的是「引擎在两个编译器上算出不同的值」——那没松：
浏览器侧同一份用例 1,947 个字段全绿。顺带的好处是换 JSON 后端不再churn 这份文件。

### 1.6 测试

- `GoldenJsonTests`（6 条）：标量 / 嵌套 / 标量数组 / 对象数组 / 空表空对象 / 字段序。
- `GoldenSuiteTests` 新增 3 条：
  - `decide 用例逐字段钉住，不再有那条整包的长行`（`package` 这个字段名不许再出现）；
  - **`决策包漂一个字段就红，且报错点名那个字段`**（票面点名的反向测试）；
  - `决策包多一个字段只多一条报错`（29 号票的考验）。
- 浏览器侧 `pnpm run verify:golden` 同一份用例全绿——摊平在 Fable 下与 dotnet 逐字段相同。

---

## 2. `Roster` 收进 `CONTEXT.md`（裁决 23-A）

**词条最终文本**（`CONTEXT.md`「座席与选手」节，插在 Player 与 PlayerState 之间）：

```markdown
**Roster（配桌）**：
一桌的坐法：一个 Ruleset 加 Seat → Player 的绑定（谁坐哪个座位）。它只回答
「这一场按什么规则打、每个座位由谁来决策」，**不含任何牌局状态**——牌在 GameState 里。
座位数本来就由 Ruleset 定（三麻只有三家），因此规则集与绑定绑在一起走。
_Avoid_: Table（牌桌是局面与推进，不是坐法）、Lineup、Setup、「配置」
```

**代码跟着改了**（票面：「不一致就改代码」）：`Roster` 原本只有 `Seats`，现在是
`{ Ruleset; Seats }`；`TablePage.openTable` 从「拿 model 的规则集开局」改成
「按这一桌的**配桌**开局」（`Table.start roster.Ruleset`）。于是「四家的配桌配上三麻的牌桌」
在页面这条路上构造不出来。**不动的**：`Table` 仍然不存 `Roster`（23 号票的形状），
牌局状态仍只有引擎那一份。`Roster.allRandom` / `withLlm` 的签名没变（它们本来就收 `Ruleset`）。

---

## 3. Bare / Assisted 的判据（主人 8/16 第四次裁决）

**词条最终文本**（`CONTEXT.md`，整条替换）：

```markdown
**ScaffoldTier（脚手架档位）**：
座位级配置，取 Bare（裸奔）/ Assisted（信息辅助）/ ToolSearch（追加局面模拟查询工具）。
**Bare 与 Assisted 之间那条线的判据是「感知 vs 计算」，不是一张内容清单**——以后冒出来的新项按它裁决。
**Bare** 给的是一个坐在牌桌前的人**免费得到**的一切：他亲眼见过的**事件序列**
（谁摸了、谁打了什么、谁鸣了谁的牌、谁宣言了立直、哪张过了一圈没人要），
与他一眼看得见的**场况**（自己的手牌、四家的 Kawa 与 Naki、点数、宝牌指示牌、
Honba 与 Kyotaku、Junme、牌山剩余）。判据不是「严格不可推导」而是「真人不用动脑子就拿得到」
——Junme 与牌山剩余都推得出来，可没有牌手在数它们，所以照给。
**Assisted** 给的是**要算才有**的量：Shanten、Ukeire、ShantenDelta、Danger
（Genbutsu 与 Suji 都得从河里推）。**可见牌按牌种归并的统计属于这一档**——那是数出来的，
不是看见的。再远一层（某家的听牌率这类要统计或模型才给得出的量）两档都不装。
**ToolSearch** 加的是自己去问的能力，不是又一批算好的数值，因此不落在这条判据的两端上。
两种客观事实在 prompt 里的位置不同：事件序列在**前缀**（append-only，因此可缓存），
场况在**尾部**（每手重算、每手付全价）。
真人坐席复用同一类型，它同时是新手辅助轮。
_Avoid_: AssistLevel、难度、强度
```

**自查（拿现有项与假想新项套判据）**：

| 项 | 判据怎么答 | 落在哪 | 与既成的决定 |
|---|---|---|---|
| 手牌 / 河 / 副露 | 呈现 | Bare | 一致（20 票的投影） |
| Junme、牌山剩余、Honba、Kyotaku | 真人一看就知道 | Bare | 一致 |
| 手切摸切 | 亲眼见过的 | Bare | 一致（20 票就在河里） |
| 现物 / 筋 | 要从河里推 | Assisted | 一致（25 票放在那） |
| 向听数 / 有效牌 / 进退向 | 要算 | Assisted | 一致（24 票） |
| **可见牌按牌种归并的统计** | 数出来的 | **Assisted** | 判据点名的那一项，别放进 Bare |
| **某家的听牌率** | 要统计或模型 | **两档都不装** | 判据的上界 |
| 见逃し（某张过了一圈没人要） | 亲眼见过的**缺席** | Bare（事件序列那一半） | 与 29a 的动机一致 |

**ToolSearch 一字未改**：「追加局面模拟查询工具」原样留着，只添了一句它**不落在这条判据的两端上**
（它加的是「自己去问」的能力，不是又一批算好的数值），因此不打架。

**没改的两处措辞**（记在 `DECISIONS.md` 28-7）：`ScaffoldTier.fs` 与 `prompt.ts` 里
「Bare 只给原始局面」。新判据说 Bare 该同时有事件序列与场况，而**事件序列那一半要等 29b
才真的进 prompt**——现在改成「两种都给」就是撒谎。两处与新判据不冲突（观测＝感知、
「一个算好的数都不给」＝排除计算），差的只是尚未落地的那一半，29b 落地时顺手改。

---

## 4. 票面点名不做的事，确认没做

- **没碰 prompt 渲染**（`web/src/agent/prompt.ts` 零改动）、**没碰投影**（`Observation.fs` 零改动）、
  **没加投影字段**。`PendingKan` 与立直宣言牌是 29a 的，一行都没顺手做。
- `DecisionPackage.fs` 只多了 5 行**注释**（指向 `GoldenJson`，说明边界没松），行为零改动。

## 5. Code review（两轴，fixed point `3dfc5674`）

本工作区派生不了 sub-agent，两轴自己顺序跑。

**Standards**（`docs/agents/fsharp-style.md` 九条 + `CONTEXT.md` + ADR + Fowler 基线）：**无 blocking**。
`fantomas --check` 与 `scripts/check-style.sh` 绿；新增代码零 `let mutable`、零 `fun x -> f (g x)`、
无从里往外读的嵌套（`flatten` 的两支都是 `xs |> List.indexed |> List.collect …` 形状）。
自查时改掉一处：`match <布尔> with | true | false` 改成 `if/then/else`（style 规则 4 的精神：
算术与分支不强行套 match/管道）。记录不修的两条 nitpick：
1. `GoldenJson.fields` 回的是 `(string * string list) list` 而不是 `GoldenField`（可能的
   Primitive Obsession）。**不改**：`GoldenField` 定义在 `GoldenObservation.fs`（编译序在它之后），
   而这个元组形状**正是** `GoldenCase.Expect` 的形状，与既有数据模型一致；转换是一行。
2. `TablePage.initial` 里 `Roster.withLlm ruleset llmAt config` 与 `rosterOf` 是同形的两处
   （Duplicated Code）。**不改**：`rosterOf` 要一个还没造出来的 model，这是 Elmish `init` 的常态。

**Spec**（票 `.scratch/llm-riichi-arena/issues/28-rulings-landing.md` 的 8 个验收框）：
8/8 落地，**一条与字面不同**——「给 `DecisionPackage` 补 decoder」落成了 `GoldenJson.fields`
（§1.2，`DECISIONS.md` 28-1）。它满足票面的实质要求（逐字段对照、只服务测试、产品边界仍单向），
但**不是引擎里的那个 decoder**，需要人确认。范围外的改动只有一样：`Roster` 带上 `Ruleset`
并让 `openTable` 按它开局——票面明写「代码里的用法与词条一致，不一致就改代码」，
`DECISIONS.md` 28-5 记了被否决的选项（只写词条不动代码）。

**blocking 自动修一轮**：两轴都没有 blocking，无需修。

## 6. 留给人的待审项

1. **§1.2 的 decoder 形态**（最要紧的一条）：要不要坚持引擎里的 `DecisionPackage.decoder`。
   要的话代价是 `Action.decoder` 与 26-2 那条不变量。
2. **用例文件涨到 270 KB**（21-a 那条「文件大」的老问题又厚了 110 KB）。缩它的唯一办法是
   降低摊平的粒度，而那正是这一票要消灭的东西——建议不动，记着。
3. **决策包不再钉两个 JSON 后端的字节**（§1.5）。若你认为那条仍值得钉，最省的补法是给
   `decide` 用例加一个「整包序列化的长度」字段，但那是个人核对不了的数字，我没加。

## 7. 偏离记录

- 中途在一条 bash 命令末尾误带了一个只读的 `git stash list`（RUNBOOK 第 1 条：只用 jj）。
  它不写任何状态，op log 未受影响；其余全部版本控制操作都是 `jj`。记在这里而不是装作没发生。
