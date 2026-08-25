# 票 114：普查点名的那几条零次断言 —— 逐条处置

**状态**：done（ready-for-human）　**工作区**：`janpo-ws-d`　**fixed point**：`7f12afcc`（`main`）
**验证**：`./scripts/ci.sh` **全绿**；dotnet 全量 **760 + 321**（改前 759 + 319，新增 3 条用例）；
**普查复量：甲档（真判断点零次）9 条 → 0 条**。

**结论先说五句。**

1. **九条逐条处置完了，一条都没剩**：**补执行者 4 条**（`TileProperties` 的 `Ok` 支、
   `HumanCallTests` 那个空 `for`、`TableTests` 的 `else` 支、`HumanCallTests` 那句 `| [] -> asked`）、
   **删掉 4 条**（四条**静默兜底**换成 `failwith`：零次仍是零次，但从此是「绿的必然结果」而不是
   「有断言没人守」）、**留着 + 单元执行者 1 条**（`advancedOrNext` 的换局支，照票 111 的形状）。
   **每条的判断理由在 §2 逐条写着。**
2. **`TileProperties` 那条属性没删，两句话现在各有各的执行者**：`Ok` 那一支
   **0 → 43 / 39 / 37 次**（每趟 100 个样本里 40 上下）。**它现在真的咬得动**：把 `Tile.parse`
   放宽成收下大写后缀，这条属性当场红（`TileNotationCandidate "4P"`）；**同一个破坏在老写法
   （`NonNull<string>`）下 9 条属性全绿**——这就是「后半句没人守」的实证（§3.1）。
3. **改前改后逐条对照在 §4**（同一件工具、同样 3 趟 Debug、同一台机器）。
   **九条改前全是 `0 0 0`**；改后：四条真判断点分别是 43/39/37、19、1、1（外加两条新的防空转断言
   227 与 3），四条失败支照口径仍是 0（乙档），换局那一支 **0 → 每趟 1 次**。
4. **每一条都先红过一次，红的原文在 §3**（共 **10 段**）。破坏一律打在**被测的那一处**
   （`Tile.parse` / `HumanSeat.kind` / `HumanSeat.buttons` / `Game.after` / `GameState.player` /
   `Table.nextKyoku`），跑完当场撤——`src/**` **最终一个字都没改**（`jj diff` 只有 `tests/**`）。
5. **一条都没放宽**，全量连跑 **20 趟全绿**（§6）。**根因在 `src/**` 的一条也没有**：九条零次
   没有一条是产品代码的死分支，全是测试自己的输入不够或形状不对（§7 逐条交代）。

---

## 1. 边界与口径

| | |
| --- | --- |
| 动过的文件 | `tests/Janpo.Engine.Tests/`：`TileGenerators.fs`、`TileProperties.fs`、`TileNotationTests.fs`、`ObservationProperties.fs`、`RiichiProperties.fs`；`tests/Janpo.Web.Tests/`：`TableTests.fs`、`HumanCallTests.fs`、`HumanAssistTests.fs`、`StaleAskTests.fs` |
| **一个字没碰** | `src/**`（红的那几把跑完当场撤）、`scripts/ci*.sh` 与 `.github/**`（票 115 在那儿）、`scripts/fsi/assertion-census.fsx`（普查工具本身，113 的清单要能被复核）、`Review*.fs` / `Playback.fs` / 账那一段、`CONTEXT.md`、`docs/adr/*` |
| 新增 | 3 条用例（`生成器里那份近似记法逐条都不是规范形`、`一整场里凡是该他出牌那一手…`、`没轮到他的那一手…`）、1 个 FsCheck 包装类型 + 1 个生成器 |
| 普查怎么跑的 | `dotnet fsi --exec scripts/fsi/assertion-census.fsx --json …`，**默认 3 趟 Debug**（113 §2.1：Release 会造 79 条假零）。改前改后各一次，参数逐字相同 |

**这一票没量页面侧**：九条零次全在 dotnet 侧，页面侧那 73 条是票 115 的地盘
（那份 6 MB 资产在不在场是它的题目），因此 `web/public/baseline/*.wasm` 本票**没挪也不需要挪**。

## 2. 逐条：结论与**判断理由**

### 甲、补执行者（4 条）

**① `TileProperties.fs:21` 的 `Ok` 支 —— 换生成器**

判断理由：**这条属性守着两句话，而喂进去的输入只养得起前半句**。随机串一辈子解析不出一张牌，
于是「能解析出来的，原串本身就是规范形」没有任何输入走得到它。**票面明说别删**——对，
「什么串都不抛异常」是真判据（`Tile.parse` 返回 `Result` 是 ADR-0001 定的方针，不是异常）。
处置是给后半句配一份输入：新的 `TileNotationCandidate` 按 **2 : 2 : 1** 掺
**合法记法 : 近似记法 : 纯随机串**。

- **合法记法**让 `Ok` 那一支每趟开 40 次上下口；
- **近似记法**（`1M` / ` 1m` / `1m ` / `1mR` / `0m` / `5rm` / `8z` / `m1` / `""`…）是**真正咬人的那一份**：
  它们逐条都不是规范形，`Tile.parse` 一旦放宽收下其中任何一条，`toMjai` 出来的与原串对不上，
  `Ok` 那一支当场红；
- **纯随机串**仍旧是 FsCheck 自己的字符串生成器（`NonNull` 排掉 null），前半句一个字没弱。

**近似记法那份表不拿 `Tile.parse` 自己筛**（拿被测物筛输入，放宽了它就自己把证据丢掉），
改由一条具名用例守着「逐条都不是规范形」——它自己也带一句防空转（表里不许少于 100 条）。

**② `HumanCallTests.fs:252` 那个空 `for` —— 换成空表断言 + 一条走整场的新用例**

判断理由：**那一句写在了一个到不了的位置上，而它想说的两件事各该由不同的东西来守**。
开局那一手 `HumanSeat.buttons` 本来就是空表（立直 / 暗杠 / 加杠 / 自摸 / 九种九牌一条都谈不上），
所以「这一手没有吃 / 碰 / 大明杠」的**准确说法是「那一排是空的」**——改成 `Assert.Empty`，
比原来那个空循环严格（空表蕴含不含那四种），而且每趟真的求值一次。
「**凡是**该他出牌那一手都没有吃 / 碰 / 大明杠 / 过」是另一句话，它需要**真的摆出过按钮的出牌手**：
新用例走三整场（种子 1、2 各一路「过」，种子 2 再来一场「碰得了就碰」——加杠要先有一组碰摆在桌上），
凡是**打牌在合法动作集里**的那 227 手逐手核，其中 19 枚按钮逐枚核。
挑局面的判据用「打牌在不在合法动作集里」而**不是**「有没有『过』」：后者正是要断言的那一句，
拿它挑局面就成了拿结论当前提。

**③ `TableTests.fs:120` 的 `else` 支 —— 再挑一颗种子**

判断理由：**那一支不是到不了，是这颗种子走不到**。种子 1177 的东 1 局以亲家荣和终（恒连庄），
`else`（不连庄就进下一局）永远轮不上。加一颗**种子 1**：它的东 1 局荒牌流局、四家一个听牌的都没有，
亲流れ。两颗种子跑同一段判据，最后一行**把两支各走一次这件事本身钉住**
（`Assert.Equal<bool list>([ true; false ], …)`）——将来哪一支又落回零次，这一行当场红。

**④ `HumanCallTests.fs:519` 的 `| [] -> asked` —— 换成一条断言**

判断理由：**它静默放行的那一种下场，恰恰是这条用例「比的不是同一件事」**。这条用例比的是
「他先答 vs 模型先答，事件流逐条相同」；`Awaiting` 空表意味着模型压根没被问出去，
两条路当然一样——那是一次假绿。`Advanced` 那一下就是把那一席问出去的那一记，
所以正确的写法是 `Assert.NotEmpty (liveOf asked).Awaiting` 然后照答，**每趟开一次口**。

### 乙、删掉（4 条：静默兜底 → 失败支）

这四条的共同理由：**它们长在测试里，而测试里的「取不到就当没有」是会假绿的**。
按 113 的口径（§3.3 乙档），`failwith` 那一族零次是「这一趟是绿的」的必然结果，
**不再占着甲档那张表**。逐条：

**⑤ `ObservationProperties.fs:99` `| None -> []`**：座位取不到就当他没河——而这条属性比的正是
「观测里的河 == 那一家的河」，两边都空就**恰好绿**。改成 `failwith`：局面真坏了就当场红。

**⑥ `RiichiProperties.fs:117` `| None -> []`**：空的 `keeps` 会把「宣言牌那一手只剩仍然听牌的打法」
变成「一张都不许打」——那是另一条断言，不是这一条。同上改 `failwith`。

**⑦ `HumanAssistTests.fs:519` 那个上限出口**：从这里出去意味着这一趟**没停在「一次到点摸切刚落定」
那个边界**上，而下面拿牌谱对拍的前提就是那个边界；静默把 `fired, model` 交出去的话，
`fired >= 6` 照样能绿（到点够了、只是最后一手不是打牌），而两边比的已经不是同一个边界了。
改成 `failwith`，把「这一趟走飞了」说出口。

**⑧ `StaleAskTests.fs:533` `poke` 的 `| [] -> model`**：**它在构造上就到不了**——`playGame` 只在
匹配到非空那一支里调它。而静默返回 `model` 的下场是：一下都没戳过，「撤票 0 次」因此假绿
（那正是它自己那三条阳性对照防的事）。改成 `failwith`。

### 丙、留着当防御分支 + 单元执行者（1 条，票 111 那个形状）

**⑨ `HumanAssistTests.fs:534` 的换局支**：**这一支不是错，只是这一趟走不到**——
六次到点全落在同一局里。它守的是「没轮到他、而这一局已经终了」这件**真会发生**的事
（走手循环跨局时就是它），所以不能改成失败支（那等于宣称「六次到点必在同一局内」，
一句与这条用例无关、还会被牌局长度推翻的话）。
按票 111 `VoidCause.Expired` 的形状办：把两行提成具名助手 `advancedOrNext`，
**分支留着**，另加一条具名用例把牌桌推到「这一局真的终了」那一刻直接喂给它
（量点停在那儿，判据 20），并配一条局中的阳性对照（走一手就只走一手）。

### 不在本票范围内的那四条（丙档 · 放行支）

113 §3.2 那四条（`FallbackTests.fs:199`、`GameStateProperties.fs:307`、`RiichiProperties.fs:125`、
`YakuProperties.fs:105`）**票面没点名，本票没动**：它们是「那一类局面一次也没出现」，
113 §8-D 的处置是「写明谁也到不了」，而其中 `YakuProperties` 那条要动 `AgariCase` 生成器
（113 §9 待审项 2，尚未编号）。本票改完之后它们仍旧是 4 条，一条不多一条不少。

## 3. 每条都先红过一次（红的原文）

破坏一律打在**被测的那一处**，跑完当场撤。

### 3.1 `TileProperties` 的 `Ok` 支：**新老两种写法的对照就是这一票最值钱的一段**

破坏（`src/Janpo.Engine/Tile.fs` 的 `suitOfSuffix` 收下大写后缀 `M/P/S/Z`）：

```
Janpo.Engine.Tests.TileProperties.解析任意字符串都返回值而不抛异常 [FAIL]
  FsCheck.Xunit.PropertyFailedException :
Falsifiable, after 28 tests (0 shrinks) (10096957862726185221,4494521093514520955).
Original:
TileNotationCandidate "4P"
---- System.Exception : Expected true, got false.
```

**同一个破坏、把入参换回 `NonNull(text: string)`（老写法）再跑一遍**：

```
已通过! - 失败:     0，通过:     9，已跳过:     0，总计:     9 - Janpo.Engine.Tests.dll (net10.0)
```

**九条属性全绿**——`Tile.parse` 已经开始收大写记法了，而这条属性一声不吭。
（另一头有具名用例守着大写与 `0m`（`TileNotationTests`），所以仓库整体不会漏；
这段对照要说的是**这条属性自己**从前守不住它写着的那半句。）

### 3.2 `HumanCallTests` 开局那一手的空表断言

破坏（`HumanSeat.buttons` 不再滤掉打牌）：

```
Janpo.Web.Tests.HumanCallTests.不合法就点不着：该他出牌那一手没有「过」，响应那一手一张牌都打不出去 [FAIL]
  Assert.Empty() Failure: Collection was not empty
Collection: [{ Id = 0
  Kind = "dahai"
  Label = "手切2万" }, { Id = 1
  Kind = "dahai"
  Label = "手切3万" }, …
```

### 3.3 走整场那一条：逐枚按钮的 kind

破坏（`HumanSeat.kind` 把立直宣言印成 `pon`）：

```
Janpo.Web.Tests.HumanCallTests.一整场里凡是该他出牌那一手：那一排没有吃 / 碰 / 大明杠，也没有「过」 [FAIL]
  Assert.DoesNotContain() Failure: Item found in collection
                    ↓ (pos 1)
Collection: ["chi", "pon", "daiminkan", "none"]
Found:      "pon"
```

### 3.4 `TableTests` 的 `else` 支

破坏（`Game.after` 在亲流れ那一支不推进局数）：

```
Janpo.Web.Tests.TableTests.下一局由引擎定场况：连庄看 KyokuEnd，本场与供托由 Game 结转 [FAIL]
  Assert.Equal() Failure: Values differ
Expected: 2
Actual:   1
  at Janpo.Web.Tests.TableTests.renchanAt@112.Invoke(Int32 seed) in …/TableTests.fs:line 127
```

### 3.5 两条属性里的失败支

破坏（`GameState.player` 把第 2 席弄丢）：

```
Janpo.Engine.Tests.ObservationProperties.任意局面任意座位，观测的河与那一家的河逐张一致 [FAIL]
---- System.Exception : { Index = 2 } 这一席在任何局面上都取得到，取不到就是局面自己坏了

Janpo.Engine.Tests.RiichiProperties.立直成立之后那家只剩自摸和、暗杠与摸切，宣言牌那一手只剩仍然听牌的打法 [FAIL]
---- System.Exception : { Index = 2 } 正等着打牌，这一席必然取得到
```

### 3.6 走手循环的上限出口

破坏（把预算从 300 手改成 3 手）：

```
Janpo.Web.Tests.HumanAssistTests.超时那一手在牌谱里与手动那一手同形：回放重建得出逐条相同的事件流 [FAIL]
---- System.Exception : 走了 4 手还没停在一次到点摸切刚落定处（到点 1 次）：这一趟走飞了
```

### 3.7 换局那一支的单元执行者

破坏（`Table.nextKyoku` 不开下一局）：

```
Janpo.Web.Tests.HumanAssistTests.没轮到他的那一手：局中只走一手，这一局终了就开下一局 [FAIL]
  Assert.False() Failure
Expected: False
Actual:   True
  at …/HumanAssistTests.fs:line 616
```

### 3.8 `HumanCallTests` 那句 `Assert.NotEmpty`

破坏（他先点之后**不**把那一席问出去）：

```
Janpo.Web.Tests.HumanCallTests.真人在想的时候模型席照问照答，而谁先答不改裁决 [FAIL]
  Assert.NotEmpty() Failure: Collection was empty
```

### 3.9 `poke` 的失败支

破坏（在没有问话在飞时戳一下面板）：

```
Janpo.Web.Tests.StaleAskTests.阴性对照：没换人的一整场，撤票 0 次 [FAIL]
---- System.Exception : poke 只在问话在飞时被调用（`playGame` 已经先匹配过非空）
```

## 4. 改前改后的普查次数（**这一票的主证据**）

同一件工具、同样 **3 趟 Debug 插桩**、同一台机器；`hits` 是逐趟的求值次数。

| # | 改前（`main`） | 改前 3 趟 | 结论 | 改后 | 改后 3 趟 |
| --- | --- | --- | --- | --- | --- |
| ① | `TileProperties.fs:21` `\| Ok tile -> Tile.toMjai tile = text` | **0 0 0** | 补执行者 | 同一行（:26） | **43 39 37** |
| ② | `HumanCallTests.fs:252` `Assert.DoesNotContain(button.Kind, …)`（空 `for`） | **0 0 0** | 补执行者 | :255 `Assert.Empty(HumanSeat.buttons dahai)` | **1 1 1** |
| | | | | :303 同一句 `DoesNotContain`（新用例里） | **19 19 19** |
| | | | | :300 `Assert.Equal(None, HumanSeat.pass package)`（新） | **227 227 227** |
| ③ | `TableTests.fs:120` `Assert.Equal(before.Kyoku + 1, after.Kyoku)` | **0 0 0** | 补执行者 | 同一行（:134） | **1 1 1** |
| | | | | :143 `Assert.Equal<bool list>([ true; false ], …)`（防空转，新） | **3 3 3** |
| ④ | `ObservationProperties.fs:99` `\| None -> []` | **0 0 0** | 删（→ 失败支） | :102 `\| None -> failwith …` | 0 0 0（**乙档**） |
| ⑤ | `RiichiProperties.fs:117` `\| None -> []` | **0 0 0** | 删（→ 失败支） | :120 `\| None -> failwith …` | 0 0 0（**乙档**） |
| ⑥ | `HumanAssistTests.fs:519` `fired, model` | **0 0 0** | 删（→ 失败支） | :535 `failwith "…这一趟走飞了"` | 0 0 0（**乙档**） |
| ⑦ | `HumanAssistTests.fs:534` `walk … (step KyokuAdvanced model)` | **0 0 0** | 留着 + 单元执行者 | `advancedOrNext` 里那一支（:100，**普查口径外**，见下） | **1 1 1**（原始覆盖数据） |
| | | | | :615 `let opened = tableOf (advancedOrNext ended)`（执行者） | **1 1 1** |
| ⑧ | `HumanCallTests.fs:519` `\| [] -> asked` | **0 0 0** | 补执行者 | :575 `Assert.NotEmpty (liveOf asked).Awaiting` | **1 1 1** |
| ⑨ | `StaleAskTests.fs:533` `\| [] -> model` | **0 0 0** | 删（→ 失败支） | :536 `\| [] -> failwith "poke 只在…"` | 0 0 0（**乙档**） |

**总表**：

| | 改前 | 改后 |
| --- | ---: | ---: |
| 断言点 | 9,079 | 9,127 |
| 零次合计 | 281 | 283 |
| **甲档（真判断点零次）** | **9** | **0** |
| 乙档（失败支零次，绿的必然结果） | 268 | 274 |
| 丙档（放行支零次） | 4 | 4 |

**再跑一次、把 `--runs` 开到 5**（113 §2.2 说的那件事：FsCheck 每趟自换种子，3 趟不够定形）：
**甲档仍旧 0 条**，零次 278 条（乙 274 / 丙 4）。3 趟那一版里有一次把
`GameStateProperties.fs:190-194`（鸣牌那五行）报进了甲档——5 趟里它是 `0 1 3 0 0`，
即 113 §3.6 点过名的**有趟空转**那一族，**与本票九条无关**（§7-2）。

**⑦ 那一行为什么读的是原始覆盖数据**：普查工具只收**测试成员自己那个方法行段内**的行
（`membersIn` / `sitesOf`），而 `advancedOrNext` 是模块级私有助手，不在任何成员的行段里，
**因此它不出现在普查表上**——这不是它没被求值，是这件工具的取景框不收它（本票不许改工具在量什么）。
同一份 coverlet 覆盖数据（`/tmp/janpo-assertion-census/runs/*-Janpo.Web.Tests.json`）里那三行是：

```
99  advancedOrNext(TableModel)  79 次   （if Table.isKyokuEnded …）
100 advancedOrNext(TableModel)   1 次   ← 换局那一支（改前 0 次）
102 advancedOrNext(TableModel)  78 次   （else：只走一手）
```

三趟一模一样。**它的具名执行者在普查表上是看得见的**（:615，1 1 1），所以「谁在走这一支」这件事
不靠这份原始数据也能核。

**顺带发现的一件事，写在这里**（不改工具，只写下口径）：把断言写进**闭包**里时，
只有当成员自己先有一个序列点，闭包那几行才落在成员的行段内。本票头一版把 `TableTests` 与
`HumanCallTests` 的断言整段搬进闭包，结果那几行**从普查表上消失了**——不是零次，是量不到。
处置是把种子表 / 场次表那一行**摊在闭包前面**（成员自己因此有了第一个序列点），
两处的注释里各写了一句为什么。**这条坑值得进 113 那份工具的文件头**（§7 交回调度器）。

## 5. 一条都没放宽（判据 5 自查）

| 改动 | 它是更硬还是更松 |
| --- | --- |
| `NonNull<string>` → `TileNotationCandidate` | **更硬**：纯随机串那一份原样留着（前半句一个字没弱），另加合法记法与近似记法两份 |
| 空 `for` + `DoesNotContain` → `Assert.Empty` | **更硬**：空表蕴含「不含那四种」，且每趟真求值 |
| 一颗种子 → 两颗种子 + `[ true; false ]` | **更硬**：多跑一支，且把「两支各走一次」本身钉住 |
| `\| None -> []` / `fired, model` / `\| [] -> model` → `failwith` | **更硬**：静默放行变成当场红 |
| `\| [] -> asked` → `Assert.NotEmpty` | **更硬**：从「没人答就算了」变成「必须有人答」 |
| 走手循环那两行 → `advancedOrNext` | **不松不硬**（同一段逻辑），另加一条具名用例把换局那一支跑起来 |

**既有断言的语义一条都没动**：改的是**喂什么输入**与**兜底怎么写**，没有一条期望值被改成迎合实现。

## 6. 全量连跑 20 趟

`dotnet test janpo.slnx -c Debug` 连跑 20 趟（`nice -n 19`，FsCheck 每趟自换种子）：

```
全绿趟数：20 / 20
（每趟 `已通过! 失败: 0，通过: 760 … Janpo.Engine.Tests` + `已通过! 失败: 0，通过: 321 … Janpo.Web.Tests`）
```

**生成器那一侧顺手对了一遍账**（票 97 立的规矩：改生成器不许挤掉别的族）：
`scripts/fsi/arbitrary-coverage.fsx` 跑完自己判——

```
**一族都没掉**：每一条判据的「一趟至少开口一次」改后都 ≥ 改前。
```

**本票本来也挤不掉谁**：`GameStateArbitraries.Traces`（那张轨迹表）**一个字没动**，
新增的是 `TileArbitraries` 里一个**新包装类型**的生成器（`TileNotationCandidate`），
它与 `GameState()` 那一族的采样互不相干；`Tile()` / `ValidTileNotation()` /
`ValidTileListNotation()` 三个既有生成器也一个字没改。20 趟 × 100 个样本的那张表里
`chankan` 仍旧是 20 趟零次——那是票 98 交回去的老账（本票没碰）。

`./scripts/ci.sh` **全绿**（fantomas / 风格闸门 / 单一事实闸门 / dotnet 760 + 321 / 浏览器 19 趟）。

## 7. 留给人的待审项（只描述不编号，判据 17）

1. **普查工具的取景框有两处该写进文件头**（本票没改工具，判据在票面边界里）：
   ① 模块级私有助手的行**不在任何成员行段里**，因此不出现在普查表上（本票 ⑦ 那一行就是）；
   ② 写进闭包的断言只有在**成员自己先有一个序列点**时才落进行段——否则整段从表上消失，
   看起来像「问题没了」。**这两条会让下一次复量的人误读**。
2. **`GameStateProperties.fs:190-194`（鸣牌那五行）在这次复量里落进了甲档**：它是 113 §3.6
   点过名的「有趟空转」那一族（上一次 3 趟是 `1 0 2`），**与本票九条无关**，
   是 FsCheck 每趟换种子的抖动。要把它钉死得**把那一族的采样做成定点**（票 97/98 那条路），
   或者把普查的 `--runs` 常开大。
3. **丙档那四条仍旧零次**（§2 末），其中 `YakuProperties.fs:105` 要动 `AgariCase` 生成器
   ——113 §9 的待审项 2，仍未编号。
4. **`TileNotationCandidate` 的近似记法是按条均匀取的**：小类（天凤式 `0m` 那 7 条）
   每 100 个样本平均只抽到 1–2 次，因此它对「某一种放宽」的捕获是概率性的
   （大写那一类 37 条，捕获接近必然）。**确定性的那一半在 `TileNotationTests` 的具名用例里**
   （`0m` / `8z` / `5M` / 空白那几条逐条钉着），两边合起来才是完整的判据。

## 8. code-review（Standards + Spec 两轴）

fixed point `7f12afcc`（`main` 当前头）。派不出 sub-agent，两轴自己顺序各跑一遍
（`docs/agents/workbook.md` 允许）。diff：**只有 `tests/**` 那 9 个文件**（+244 / −38），
外加本报告、票文件与 `DECISIONS.md`。

**Standards**（`docs/agents/fsharp-style.md` + Fowler 坏味道 + 判据 1 / 3 / 5 / 20）

- **改掉的两处**（review 当场修，修完重跑全量）：
  ① `| Some package when not (List.isEmpty (HumanSeat.dahaiOptions package))` —— 规则 3 的
  canonical 坏例子（三层、从里往外读），改成 `when HumanSeat.dahaiOptions package |> List.isEmpty |> not`；
  ② `List.filter (fun text -> canonical.Contains text)` 改成 `Set.contains text canonical`
  （仓库里 `Set` 一律走模块函数，`Set.isSubset` 是邻居）。
- 规则 1 / 3：新代码里没有第三处嵌套变换；`tableOf (advancedOrNext model)` 是规则 4.1 允许的两层。
- 规则 2：新 lambda 都捕获了外部值（`canonical` / `pick` / `state`），按例外保留。
- 规则 4：`Rng.ofSeed (seed * 7919 + 13)`、`Assert.*(…)` 的实参、算术保持原样，没强行管道。
- 规则 5：**新增 `let mutable` 0 处**；`check-style.sh` 通过（预算没动）。
- 规则 8 / `fantomas --check`：通过（写盘那一遍已跑）。
- 术语：新标识符全是测试脚手架（`TileNotationCandidate` / `TileNotationSamples` / `advancedOrNext` /
  `untilOthers` / `renchanAt` / `nonRenchanSeed`），**没碰 `CONTEXT.md` 的日麻词**（照 113 那份工具的先例）。
- 判据 20（量点）：换局那一支的执行者**停在「这一局真的终了」之后**；`TableTests` 两颗种子都跑到
  这一局终；走整场那一条停在 `Table.result` 有值之后。
- **味道（判断题，只记录不改）**：① `HumanCallTests` 新那条走手循环与同文件里 ①②③④ 那条形状相近
  （Duplicated Code）——考过抽一个公共 `walkGame`，**否决了**：两条循环挑的局面、核的东西、
  防空转的判据都不同，抽出来要多两个高阶参数，读者反而得跳着读；
  ② 同文件里既有的 `let discarded = not (List.isEmpty (HumanSeat.dahaiOptions package))`
  （`HumanAssistTests.fs`）是同一个规则 3 的形状，**不是本票的行，没动**（判据：只动自己票里的东西）；
  ③ `TileNotationSamples` 是测试程序集里的公开模块（`TileNotationTests` 要读它），
  没做成 `private`。

**Spec**（票 `.scratch/llm-riichi-arena/issues/114-zero-execution-assertions.md` 逐条）

| 票面那一条 | 落在哪 |
| --- | --- |
| ① `Ok` 支真的被走到 | §2-①、§4（**0 → 43 / 39 / 37**） |
| ① 别把那条属性删掉，两句话各有各的执行者 | 属性原地没动，只换了入参；前半句由纯随机串 + 近似记法守，后半句由合法记法守（§2-①） |
| ① 复量后把 `Ok` 支的次数写进报告 | §4 第一行 |
| ② 查清那个循环为什么空 | §2-②：开局那一手 `buttons` 本来就是空表（立直 / 暗杠 / 加杠 / 自摸 / 九种九牌都谈不上） |
| ② 让它非空，或写明凭什么空并换成真能执行的断言 | 两样都做了：空表那一手改成 `Assert.Empty`（每趟 1 次），「凡是出牌那一手」另开一条走整场（227 手 / 19 枚） |
| ③ `TableTests` 的 `else` 支 | §2-③：加种子 1（亲流れ），并把「两支各走一次」钉成一行断言 |
| ④ 六条防御分支逐条判、每条一句理由 | §2 乙（4 条删）与丙（1 条留 + 单元执行者）——**六条里那两条属性的兜底判成了「删」，与 113 §8-C 的建议不同，理由写在 §2 乙开头** |
| 闸门：每一条先红一次，红的原文进报告 | §3，共 10 段 |
| 闸门：普查复量、改前改后逐条对照 | §4（主证据） |
| 闸门：一条都不许放宽 | §5 逐条对照 |
| 闸门：连跑 20 趟 | §6 |
| 闸门：`./scripts/ci.sh` 全绿 | 页首 + §6 |
| 边界：不碰 `src/**` / `ci*.sh` / `.github/**` / 普查工具 / 108–112 那几块 | §1；`jj st` 只有 `tests/**` 那 9 个文件 + 报告 / 票 / DECISIONS |
| 边界：根因在 `src/**` 的停下来交回编号 | **一条都没有**（§7 没有这一类待审项）：九条零次没有一条是产品代码的死分支 |

**与票面的一处偏离（写在明处）**：票面 §4 说「多数应该是『留着 + 单元执行者』这一档」，
本票**只有一条**判成了那一档，另外四条判成「删（换成失败支）」。理由在 §2 乙：
票 111 那个先例是**产品代码**里的防御分支（将来会有路走到它），而这四条是**测试自己的兜底**，
它们静默放行的下场是假绿——测试里的兜底该响，不该配一个「兜底返回空表是对的」的单元执行者。

