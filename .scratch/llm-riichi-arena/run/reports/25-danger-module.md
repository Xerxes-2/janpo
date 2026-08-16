# 25 — Danger 危险度并入 Assisted

**状态**：done　**change**：`txxznwps`（本票一个 commit；commit id 随 amend 变，认 change id）　**fixed point**：`8233125c`（`vmupqzql`）

引擎多了一个**分析附件**：给定遮蔽后的观测与这一手打得出去的那几张牌，按**现物 / 筋 / 壁 / 宝牌周边**
四条规则给出安全度排序与逐张的理由标签。它进了 Assisted 档的决策包与 prompt、进了牌桌（默认关），
也把 24 号票欠的那半件事还了：**Assisted 档的兜底从「不退向听」变成「不退向听的安全打」**。

spec 对 Assisted 的定义（向听 + 有效牌 + 进退向 + 危险度排序）至此全部兑现。

---

## 1. 落在哪几处

| 层 | 文件 | 做了什么 |
|---|---|---|
| 引擎 | `src/Janpo.Engine/Danger.fs`（新，434 行） | `Threat` / `DangerTier` / `DangerReason` / `Danger` 四个类型 + 判定 + 排序 + encoder |
| 引擎 | `src/Janpo.Engine/Observation.fs` | `Observation.visible`——可见牌的算法从 `Scaffold` 私有提上来，两个消费方读同一份 |
| 引擎 | `src/Janpo.Engine/Scaffold.fs` | `Scaffold.Threats` + 每条试打上的 `Danger option`；encoder 各加一行 |
| 引擎 | `src/Janpo.Engine/Fallback.fs` | Assisted 档：不退向听的候选**再按危险度名次挑最安全的** |
| 页面 | `src/Janpo.Web/TablePage.fs` | 「危险度」开关（默认关）+ 排序面板；只碰视图与 `update` 的一支，**没碰 `settle`** |
| Agent 层 | `web/src/agent/prompt.ts` | `dangerLines`（`scaffoldBlock` 里多出来的那一节）+ `DangerView` / `ThreatView` |
| 录制 | `web/scripts/record-agent-fixtures.mjs` | 多录一份 `ask-danger`：模型对**带危险度那份 prompt** 的真实回答 |
| 闸门 | `scripts/check-style.sh` | 「规则判定的用例里不许出现 Danger」——纯附件这件事的自证（25-8） |

测试：引擎 `DangerTests`（16 条）+ `DangerProperties`（5 条属性）+ `ScaffoldTests`（+2）+
`FallbackTests`（+2）+ `DecisionPackageTests`（改 1 条）；dotnet 侧 `TablePageTests`（+2）；
Agent 层 `web/tests/agent`（33 条，全部回放录制响应）。
**dotnet 703 条（引擎 648 + 页面 55）+ node 33 条 + `./scripts/ci.sh` 全绿**（含浏览器内 40 条黄金用例）。

## 2. 判据：四条规则，一条都不多

`Danger.rank : Observation -> Tile list -> Danger list`。**收的是遮蔽后的观测**——他家的暗牌在
`MaskedSeat` 里根本没有位置，因此「危险度多看了一张牌」在结构上不可能（与 24 号票的脚手架同一条保证）。

1. **威胁判据**（25-1）：**立直（宣言或成立）或有副露**的他家。一家都没有 → 整份危险度是空表，
   `danger` 是 null，prompt 与牌桌都不出现这一节。危险度是「对谁危险」的排序，没有那个「谁」就不排。
2. **Genbutsu（现物）**：这家自己打过这张牌。**只认它自己打的**，不认「立直之后别人打过而没被和的」——
   后者要跨座位的牌河先后，观测里没有（25-4）。这个简化只低估安全度，不高估。
3. **Suji（筋）**：一张牌只被两种两面听等着（`n+1 n+2` 另一头 `n+3`、`n-2 n-1` 另一头 `n-3`），
   两种**全被这家的现物排掉**才算筋。因此 1-3 / 7-9 一侧现物即可，**4-6 要两侧都现物（片筋不算筋）**。
4. **Kabe（壁）**：某牌**四张全见**（自家手牌 + `Observation.visible`）时，含它的那种两面听不成立；
   一张牌的两面听全被排掉、且至少一处靠壁 → 壁档。
5. **宝牌周边**：这张是宝牌（权重 2）／同花色两张之内（权重 1）。**它只在同档内破平**：
   现物是绝对安全的，是不是宝牌都排第一（25-2）。

档位阶梯 **现物 → 筋 → 壁 → 无依据**，排序键 `(档位序, 宝牌权重)`，`List.sortBy` 稳定因此同键保持动作顺序，
名次是**并列名次**（两张并列第一之后是第三）。多家有威胁时**档位取最危险的那一家**。

**理由标签**是「一条依据一个事实」，`NoEvidence`（「下家无依据」）**只在别的家有依据时才写**——
三家都没依据时档位已经说完了，逐条重复只是噪音。

## 3. 一手真实局面的完整排序（`janpo decide 99 --steps 6 --seat 3`）

这一手同时有三档、有宝牌周边、还有赤 5 与正 5 共用一条试打，因此它也是新加的那条黄金用例
（`decide-99-danger`）与 Agent 层固件 `decision-danger.json` 的来源。

```
座位 3（北家）手牌 4m 7m 3p 5p 5pr 1s 1s 3s 4s 5s 5s 7s 3z 6z（刚摸进 6z）
宝牌指示牌 6p（宝牌 7p）
  下家（座位 0）：河 4p　　副露无　　立直无
  对家（座位 1）：河 7z 4m　pon 1z　立直无      ← 有威胁的家
  上家（座位 2）：河 1z 7m　副露无　　立直无
```

| 名次 | 动作 | 牌 | 档位 | 理由标签 |
|---|---|---|---|---|
| 1 | id=0 手切4万 | 4m | 现物 | 对家现物 |
| 2 | id=1 手切7万 | 7m | 筋 | 对家 4m 筋 |
| 3 | id=2 手切3筒 | 3p | 无依据 | —— |
| 3 | id=5 手切1索 | 1s | 无依据 | —— |
| 3 | id=6 手切3索 | 3s | 无依据 | —— |
| 3 | id=7 手切4索 | 4s | 无依据 | —— |
| 3 | id=8 手切5索 | 5s | 无依据 | —— |
| 3 | id=9 手切7索 | 7s | 无依据 | —— |
| 3 | id=10 手切西 | 3z | 无依据 | —— |
| 3 | id=11 摸切发 | 6z | 无依据 | —— |
| 11 | id=3 手切5筒 | 5p | 无依据 | 宝牌 7p 周边 |
| 11 | id=4 手切赤5筒 | 5p | 无依据 | 宝牌 7p 周边 |

读法：对家碰了 1z 在做牌，它打过 4m —— 4m 对它绝对安全（现物），7m 因此成筋（`5m6m` 那种两面听
被 4m 的振听排掉，而 `8m9m` 等 7m 是边张不算两面）。宝牌是 7p，5p 在它两张之内，因此同为无依据时
5p 排在最后。**赤 5 与正 5 是两条动作、一个牌种**，两条各占一行、名次相同。

另外两份局面的排序（`docs` 之外的活证据，用 `scripts/fsi` 探针打的）：

- **壁**：种子 42 第 43 手，三家都有副露，`9s` 因为 **8s 四张全见**独占第一档（其余全是无依据）。
- **多家**：同一手的 `8m` 是「下家现物、对家无依据、上家无依据」→ 档位取最危险的那家 = 无依据。
  **只对一家安全不是安全**，这条在 `DangerProperties` 里也钉着（现物档 ⇔ 对每一家都是现物）。

## 4. Assisted 档 prompt 里那一节的全文

`pnpm run prompt -- --package tests/fixtures/agent/decision-danger.json --diff` 的尾巴（**原文**）：

```text
危险度排序（有威胁的家：对家有副露）——现物 / 筋 / 壁 / 宝牌周边四条规则算出来的启发式，不是概率；排在前面的更安全，同级并列：
- 第1位 id=0（手切4万）：现物 —— 对家现物
- 第2位 id=1（手切7万）：筋 —— 对家 4m 筋
- 第3位 id=2（手切3筒）：无依据
- 第3位 id=5（手切1索）：无依据
- 第3位 id=6（手切3索）：无依据
- 第3位 id=7（手切4索）：无依据
- 第3位 id=8（手切5索）：无依据
- 第3位 id=9（手切7索）：无依据
- 第3位 id=10（手切西）：无依据
- 第3位 id=11（摸切发）：无依据
- 第11位 id=3（手切5筒）：无依据 —— 宝牌 7p 周边
- 第11位 id=4（手切赤5筒）：无依据 —— 宝牌 7p 周边
```

它接在【引擎算好的数】那一节的逐张试打之后，**骨架（`frame`）、段落名与调用点一个字没动**——
两档的差异仍然只有那一节（`prompt.test.ts` 的「两档只差一节」照旧绿）。

**文案的红线**（票里明写）：一个百分比都没有，「不是概率」四个字写在段首。
`prompt.test.ts` 有一条断言直接盯着它：这一节不许出现「放铳率」「%」「百分」。

**成本**：这一手 bare 608 字 → assisted 2703 字，其中**危险度那一节只占 364 字（13 行）**；
逐张试打那一节仍是大头。危险度的行数 = 打牌动作数，与巡目无关。

## 5. 模型真的在用它

同一份决策包、同一个模型（DeepSeek `deepseek-v4-flash`），录了一次真实回答
（`web/tests/fixtures/agent/ask-danger.json`，`pnpm run record:agent ask-danger`）：

| 输入 tok | 输出 tok | 选了 | 理由（原文） |
|---|---|---|---|
| 2332 | 100 | id=0 | 「手切4万是**对家现物**，安全度高；同时保持3向听，有效牌62枚较优，且在早期巡不考虑**宝牌周边**的情况下选最安全的现物打出较为合理。」 |

它把**现物**与**宝牌周边**两个标签逐个引了出来，选的正是排第一位的那张。
`loop.test.ts` 回放这条录制响应，断言 prompt 里有那一节、且模型的理由里出现「现物」。

人工验收（真 key，不进 CI）：

```
$ JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-llm-seat.mjs --seed 1177 --tier assisted
一局打完　provider 请求 8 次（4xx/5xx 0）　兜底代打 0 手
和了　座位 1 自摸6筒　立直 1 番、一发 1 番、门前清自摸和 1 番、宝牌 3 番　40 符 6 番 12000 点（跳满）
各家点数：19000 / 37000 / 22000 / 22000　页面 pageerror 0 条、资源错误 0 条
```

**这仍然是一局，不是证据**——它只说明危险度进了闭环、没把什么东西弄坏。

## 6. 黄金用例：老两条只多了两个字段，另加一条新的

```sh
dotnet run --project src/Janpo.Cli -- golden write tests/fixtures/golden/dual-target.json
```

核对照 24 号票报告第 5 节的脚本（parse 新旧两份、摘掉 `scaffold` 之后断言其余全等）：

```python
sb = pb.pop("scaffold"); sa = pa.pop("scaffold")
assert pb == pa                                  # 其余字段逐字相等
assert sb.keys() | {"threats"} == sa.keys()      # scaffold 只多了 threats
for tb, ta in zip(sb["dahai"], sa["dahai"]):
    assert set(ta) - set(tb) == {"danger"} and ta["danger"] is None
```

结论：`decide-1177-step-8` 与 `decide-42-first-hand` 只多了 `"threats": []` 与逐条 `"danger": null`
（两手都在早巡，没人立直也没人副露），**另外 37 条用例一个字节没动**。

新加的第 40 条 `decide-99-danger`（25-6）钉的是**排序本身**：老两条盯不住它（`threats` 是空的）。
浏览器侧同一份用例也绿 —— **危险度的名次在 Fable 下与 dotnet 逐字节相同**，
`List.sortBy` 的稳定性与并列名次的算法两个目标一致这件事因此有了闸门。

## 7. 兜底：Assisted 从「不退向听」变成「不退向听的安全打」

```fsharp
// 两步，顺序不能反：先取进退向为 0 的那几条试打，再在它们之间按危险度名次挑最安全的；
// 并列时优先摸切（与 Bare 同手，也不多暴露手牌信息）。
let candidates = scaffold.Dahai |> List.filter (fun trial -> trial.ShantenDelta = 0)
let best = candidates |> List.map rank |> List.min
```

**行为差异只在真有人做牌的时候**：没人立直也没人副露时每条试打的 `danger` 都是 None、
名次全相等，于是保持原顺序 —— 那时这一档与 24 号票**逐字相同**（24 号票钉的两条兜底用例原样绿）。

`FallbackTests` 新钉的那条（剧本：下家第 1 巡摸进 1z 就立直、打 1z；自家是三副顺子加五张孤张字牌的
2 向听，打哪张字牌都不退向听）：

| 档位 | 代打 | 为什么 |
|---|---|---|
| Bare | 摸切 5z | 摸切就是这一档的定义 |
| Assisted（24 号票） | 摸切 5z | 5z 不退向听，同为不退向听时优先摸切 |
| **Assisted（本票）** | **手切 1z** | 1z 是下家立直宣言牌，**现物**；它同样不退向听 |

**顺序不能反**：候选先过「不退向听」那道闸再比安全度，否则就会为了安全而退向听
（`CONTEXT.md` 的 Fallback 写的是「不退 Shanten 的安全打」，不是「最安全的一手」）。

## 8. 纯附件：这件事怎么自证（25-8）

- **引擎侧靠编译顺序**：`Danger.fs` 在 `Janpo.Engine.fsproj` 里排在**全部规则判定文件之后**
  （`Kyoku` / `GameState` / `Score` / `Yaku` / … 都在它前面），F# 的编译顺序因此保证判定路径
  **引用不到它**。这不是纪律，是结构。拿掉 Danger（连同脚手架上那一个字段与
  `Fallback.assisted` 里那三行排序）之后，对局照跑——判定那一堆文件一行都不用改。
- **测试侧靠一道 grep 闸门**：`scripts/check-style.sh` 新增一条，`tests/Janpo.Engine.Tests/` 里
  除 `Danger*` / `Scaffold*` / `Fallback*` / `DecisionPackage*` 之外的用例文件出现 `Danger` 字样就红。
  规则判定的 648 条用例里没有一条读它。

## 9. 牌桌上的显示（默认关）

「视角」那一排多一个「危险度」按钮（`data-testid="table-danger"`），拨开之后正在被问的那家
多一块面板（`table-danger-<座位>`）。**只显示手牌本来就看得见的那一家**（25-7）：坐在座位上看
只显示自己那一手，上帝视角显示正在被问的那家 —— 危险度的候选牌就是那家的手牌，
把他家的排序摆出来等于绕开 20 号票在类型层面立的那道墙。

无头核对（一次性脚本，没进仓库）：默认面板 0 块 → 拨开、单步 9 手（下家碰了一手）后 1 块，
再拨一下回到 0 块，`pageerror` 0 条。面板正文：

```
座位 0 的危险度（有威胁的家：下家有副露）
现物 / 筋 / 壁 / 宝牌周边四条规则算出来的启发式，不是概率；排在前面的更安全，同级并列。
第1位 2z 现物（下家现物）
第2位 6m 无依据
…
第10位 3s 无依据（宝牌）
```

打包：`index-*.js` 354.32 → **362.07 kB**（gzip 113.10 → 115.51）。

## 10. 关键取舍

全文见 `DECISIONS.md` 的「## 25」段（8 条）。三条最要紧的：

1. **没人立直也没人副露就不给排序**（25-1）。代价是早巡的 prompt 里看不到这一节，
   收益是不给无依据的名次，且兜底的回归面小到能一眼看完。
2. **档位阶梯 + 宝牌只破平**（25-2）。不给 0-100 的分数——那正是票里禁止的、会被读成统计结论的形态。
3. **现物只认「这家自己打过的」**（25-4）。少认了「立直后通过的牌」，方向保守（低估安全度）。

## 11. 留给人的待审项

- **威胁判据是最粗的一档**（25-1）：立直或副露。它对「门清默听在做大牌」的那家一无所知，
  对「副露了一堆字牌明显在做役牌」的那家也不分强弱。要分强弱就要读牌河形状与巡目——
  那已经是统计模型的门口，第一版明确不做。M2 的评测口径出来之后再决定要不要往前一步。
- **筋与壁的先后是我定的**（25-2）。壁其实还能挡嵌张与边张（含那张的所有形），
  按「挡掉多少种形」排的话壁该在筋前面。我按术语表列判据的顺序取了「筋在前」，请复核。
- **「无依据」这个词是我拟的**（`DangerTier.NoEvidence`）。术语表里没有它，
  日麻的对应词「無筋」只说数牌、说不了字牌，所以没有直接用。要收进 `CONTEXT.md` 请一并定名。
- **`decision-danger.json` 是第三份决策包固件**（1235 行）。它是必要的（老两份没有威胁），
  但 Agent 层的固件目录在变胖，M2 该考虑只存「有威胁的那一段」而不是整包。
- **26 号票的 DecisionRecord 会记下危险度**：它在 `scaffold` 里，形状与档位无关（包恒带脚手架）。
  两档的记录仍然同形（24-1 的那条保证没变）。

## 12. code-review 结论

两轴各跑一遍（fixed point `8233125c`）。（这台机器上派不出 sub-agent，两轴由我顺序跑，各自只看自己那份材料。）

### Standards（`docs/agents/fsharp-style.md` + `CONTEXT.md`/ADR-0001 的命名约定 + Fowler 味道基线）

**0 条 blocking。** 顺手修了两处（都不是错误行为，是我自己看着不顺眼）：

1. *Speculative Generality*：`DangerTier.all` 定义了没人用。`ScaffoldTier.all` 存在是因为配置面板
   要列选项，而档位不是配置项。→ 删掉，并在模块注释里写清「为什么这里没有 `all`」。
2. *TS 侧的防御不一致*：`dangerLines` 读 `view.threats` 时只写了 `?? []`，而同一个文件的
   `readScaffold` 是「读不出来就当没有」。包是我们自己的引擎编的，但这一层的方针是**不许把这一手卡死**。
   → 改成 `Array.isArray(view.threats) ? … : []`。

**nitpick（只记录，未改）**：

- *Long Parameter List*：`Danger.assess` 收四个参数（壁判定、宝牌、有威胁的家、牌）。捆成一个 record
  会多一个只有一个消费者的类型（与 24 号票 `selectField` 三元组那条 nitpick 同源）。
- *Duplicated Code*：`Threat.who` 的「1 下家 / 2 对家 / 3 上家」与 `prompt.ts` 的 `RELATIVE` 是同一张表
  的两份。TS 那份是 23 号票渲染他家用的；真要合并得让观测里每一家都带上 `display`，那是 20 号票的形状问题。
- *Duplicated Computation*：`ryanmen pai` 在 `assess` 里算一次、在 `against` 里逐家各算一次
  （n ≤ 14、每次两个 option，实测不可测）。
- *Primitive Obsession*：`Danger.Rank: int` 与 `DangerTier.order: int`。给名次造类型之后
  `List.min` 与比较会更啰嗦（风格规则 4.2）。
- `Danger.rank` 的并列名次是 `1 + List.sumBy (…< key item)`，O(n²)。写成一次扫描要么引入可变量、
  要么引入 fold 的状态元组，都比现在难读；n ≤ 14。
- `Danger.fs` 434 行里有四个模块（`DangerTier` / `Threat` / `DangerReason` / `Danger`）。
  仓库惯例是一概念一文件——这四个是同一个概念的四个面（档位、威胁、理由、结果），
  拆开会让「Danger 是一件事」读不出来。同类先例：`Tile.fs` 里 `Tile` + `TileParseError`。
- `against` 收一个元组参数 `(threat, genbutsu)`（因为调用方手上就是这个元组）。仓库里多是柯里化参数。

引擎的 `let mutable` 预算未动（仍是 2）：`visibleCounts` 的 34 长计数数组是原地累加的 `for`，
与 `TileKindSet.ofKinds` 同一套写法，不占预算。`dotnet fantomas --check` 与 `scripts/check-style.sh` 干净。

### Spec（票 25 的 7 条验收 + `CONTEXT.md` 的 Danger / Genbutsu / Suji / Kabe / Fallback 词条 + ADR-0005）

**0 条 blocking。** 逐条对验收：

| 验收 | 落在哪 |
|---|---|
| 规则化启发式，不做统计模型 | 四条判据全是观测里的事实；档位是序不是分；文案里一个百分比都没有（`prompt.test.ts` 有断言盯着） |
| 排序 + 每张牌的理由标签 | `Danger.Rank` + `Danger.Reasons`；标签由引擎渲染（`DangerReason.toDisplay`）直接进 prompt |
| 措辞与术语表一致 | 现物 / 筋 / 壁三个词条各一档，`DangerTests` 有一条专盯这三个 `toDisplay`；壁的理由文案照词条写成「四张全见」 |
| 纯附件 | 编译顺序 + `check-style.sh` 的 grep 闸门（第 8 节） |
| 测试钉住判据 | 现物必最安全（用例 + 属性各一条）、四张全见形成壁（含「三张不算」与「副露亮的也算」）、筋的成立条件（含「中张要两侧」「只在同花色内」「字牌没有筋」）、对未立直但副露的对家也给排序 |
| 进 Assisted 的包与 prompt，Bare 不带 | 包恒带（24-1 的形状没变）；`prompt.ts` 只在 Assisted 渲染；Bare 那条「不许出现『危险』」的老断言照旧绿 |
| 牌桌可选显示，默认关 | `TableModel.ShowDanger = false`，两条 dotnet 用例 + 一次无头核对（第 9 节） |

**与票里字面不同、有意为之的三处**：

1. **理由标签比票里的例子多带一个「谁」**：票写「4s 筋」，本票写「对家 4m 筋」。
   多家有威胁时不写清是对谁成筋，这条标签就没法读（`8m` 可能对下家是现物、对上家什么都不是）。
2. **宝牌周边不单独成档**，只当同档内的破平项（25-2）：它影响的是放铳的**代价**不是概率，
   与前三条不是一个量纲。`CONTEXT.md` 的 Danger 把它与三条并列，因此这条点名。
3. **没人立直也没人副露就不给排序**（25-1）：票允许「第一版对所有他家都给排序」，本票取了更严的一档。
   理由与代价都写在 DECISIONS 里，`decide-99-danger` 那条黄金用例是它的补偿。

**scope creep 自查**（都不在验收框里，逐条给理由）：

- `Observation.visible`（把 `Scaffold` 的私有函数提上来）→ 壁要数可见牌，两处各数一遍必然漂（25-5）。
- 黄金用例 `decide-99-danger` → 老两条的 `threats` 是空的，盯不住排序本身；而名次靠
  `List.sortBy` 的稳定性，那正是黄金用例存在的理由（25-6）。
- `ask-danger` 录制 + `loop.test.ts` 一条 → 「理由要能直接进 prompt」这条验收，
  只有真模型引了标签才算兑现。
- `check-style.sh` 那道闸门 → 票里点名要「纯附件」能自证。

ADR-0005 的四条边界逐条守住：`GameState` 不越界（`Danger.rank` 收 `Observation`）、
prompt 在 TS 侧渲染（引擎只出结构化值 + `display`）、没给 Fable 输出写 `.d.ts`、
Fable 运行时后端没进引擎工程（`ci.sh` 的白名单闸门绿）。
