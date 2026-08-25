# 票 113：断言执行次数普查 —— 一趟 CI 里每条断言各开了几次口

**结论先说六句。**

1. **量到了 10,395 个断言点**（dotnet 侧 9,079 + 页面侧 1,316），**其中 10,353 个有数**
   （dotnet 全部 9,079；页面侧 1,274 个数得出来、32 个在浏览器里跑数不到、10 个整份没跑）。
   没量到的那一批（shell 闸门、`biome ci`、`tsc`、Fable / Vite、远端 Pages 那条流水线）在 §6 列了名单与理由。
2. **零次的一共 354 条**（dotnet 281 + 页面侧 73，页面侧取 CI 的形态），分三档：
   **真判断点 73 条**（dotnet 9 条 §3.1 + 页面侧 64 条 §3.4）、**放行支 4 条**（§3.2）、
   **失败支 277 条**（`failwith` / `-> false` / `throw`——**绿的时候它们本来就是 0**，§3.3）。
   **真判断点与放行支那 77 条逐条一句「为什么执行不到」**（页面侧同一个函数里的归一组，共六组）；
   失败支那 277 条**共用一句**并附逐文件计数——它们零次是同一个原因（这一趟是绿的），
   逐条写 277 遍只会把真正要看的那几十条埋掉。**没查清的一条也没有。**
3. **最值钱的三条**：①**页面侧 `verify-review.mjs` 的 `strongLeg` 整段 54 条断言在 CI 里零次**
   （本机把那份 6 MB 资产放进 `web/public/baseline/` 就有 52 条跑起来，最多的一条 854 次）；
   ②`TileProperties` 那条「解析任意字符串」属性**每趟跑 100 个样本、真判断的那一行一次也没进去**
   ——它今天等价于 `fun _ -> true`；③`HumanCallTests` 里一条 `Assert.DoesNotContain` 坐在一个**空循环**里，
   一趟都没求值过。
4. **恒真式抓到了**：一族靠运行时（「成员跑得到、体内做判断的行全零」）抓，抓到 1 条真的（第 ②）；
   一族靠静态（「两侧同一个表达式」）抓，抓到 4 条——**那 4 条都是「同一种子跑两遍必须一样」的确定性属性，
   是真断言不是废话**，但在纯函数上它们离恒真只差一个「有没有藏着可变状态」（§5）。
5. **两条阴性对照都落在该落的地方**（§4，原文留在里头）：故意加的那条永远执行不到的断言进了零次那一行，
   故意加的恒真式被点名。两条控制做完当场撤掉，`jj st` 里只剩两个新文件。
6. **默认不进 `ci.sh`**：普查一趟 **3 分 40 秒**，而 CI 全程 **1 分 55 秒**——接进去等于把 CI 变成 2.9 倍，
   而它答的是「断言够不够硬」，那是收尾时问一次的问题（账在 §7）。**这一票一条断言的语义都没动**，
   发现的问题只列不改（§9 交回调度器编号）。

---

## 1. 两件工具

| 工具 | 量谁 | 计数器 |
| --- | --- | --- |
| `scripts/fsi/assertion-census.fsx` | `dotnet test janpo.slnx`（Engine.Tests + Web.Tests） | **coverlet 的逐行命中数**（`--include-test-assembly`，只插桩测试工程） |
| `web/scripts/assertion-census.mjs` | `node --test` / `verify-invariants` / `verify-browser` / `verify-baseline` | **V8 的块级精确计数**（`NODE_V8_COVERAGE`，node 自带，零依赖） |

跑法写在各自的文件头上。两份都支持 `--from <目录>`：拿上一趟的覆盖数据**重算不重跑**（改分类规则时只要几秒）。
两份都能吐 `--json` 全表。

**这两个数不是行覆盖率。** 单位是**序列点 / 代码块**，不是文件也不是方法：F# 把 `&&` 的每一支、
`match` 的每一臂各留一个序列点，V8 给每个块一个执行次数——所以
`| ResponseCause.Kan kan -> Naki.isKan kan && …` 这一支**跑了几次是单独数得出来的**（票 98 那条抢杠属性
就是这个数为 0）。dotnet 侧的三档单位（`member` / `assert` / `branch`）与页面侧的三档
（`gate` / `assert` / `throw`）写在两份工具的文件头上。

## 2. 校准：两个仪器各有一个已知的坑，都量过了

### 2.1 dotnet 侧：**必须用 Debug 量，Release 会造 79 条假零**

Release 下 F# 优化器把小的 `let private` 助手**内联**进调用点，原函数体从此没人进——coverlet 于是报它 0 次，
**而它其实每趟都在跑**。实测（Engine.Tests，各一趟）：

| | 有序列点的行 | 零次的行 |
| --- | ---: | ---: |
| Release | 9,927 | 569 |
| Debug | 9,933 | 508 |

两边的零次集合交集 **490**；**Release 独有 79 条**，逐条查过，全是被内联的一行助手，例如
`KanProperties.fs:38 establishedKanEvents`（Release **0** 次 / Debug **12** 次，而 `kanEvents` 调它 132 次）；
**Debug 独有 18 条**是稀疏分支（下一条）。**所以普查一律 Debug 跑**，理由与这组数写在脚本的文件头上。
Debug 与 Release 只差优化，**不差行为**：`src/**` 与 `tests/**` 的 `.fs` 里**一处 `#if` 都没有**
（搜得到的那句 `#if FABLE_COMPILER` 是注释里提到的），两边跑出来的测试数都是 759 + 319。

### 2.2 两侧共有：一趟不够，**FsCheck 每趟自换种子**

同一条稀疏分支这趟 3 次、下趟 0 次。因此工具默认跑 **3 趟**，只有 **`max = 0` 才叫零次**，
`min = 0 < max` 单列一栏「有趟空转」（判据 3 那条「十趟九空转」就住在这一栏）。
**这一栏本身每次跑都不一样**：本票跑的两次普查，这一栏一次是 25 条、一次是 7 条，**并集才是它的形状**——
要拿它当结论就把 `--runs` 开大。

### 2.3 页面侧：**`page.evaluate(…)` 里的断言 node 侧数不到**

那段代码序列化进浏览器里跑，在 node 的覆盖数据里它永远是「函数没被调用」。**32 条**是这一类
（`verify-human` 24、`verify-bubbles` 5、`verify-assist` / `verify-review` / `verify-share` 各 1），
工具把它们**单独一栏**列出来，**不混进零次那张表**——混进去就是 32 条假零。

### 2.4 页面侧：**本机与 CI 不是同一套局面**，两种形态都量了

`web/public/baseline/janpo-baseline.wasm`（6 MB，ADR-0006 边界 6 定的不入版本控制）**在这台机器上是有的**，
在远端 CI 上没有。它一在场，`verify-review.mjs` 就走「真推理」那一路，`verify-baseline.mjs` 的判据也换一支。

| 形态 | 页面侧零次 | 其中 `verify-review.mjs` |
| --- | ---: | ---: |
| **无资产（= 远端 CI）** | **73** | 56 |
| 有资产（本机默认） | 25 | 8 |

**§3.4 那张表报的是无资产那一趟**（CI 的形态）。差额 52 条只在 CI 里零次，本机跑得起来（§3.4 第一组）。
量之前先把那份资产（连同 `dist/` 里的副本）挪开，量完放回——**不挪就等于在量另一台机器**。

## 3. 那张表（按执行次数升序，零次的排最前）

### 3.1 dotnet 侧 · 甲档：真判断点零次，3 趟全零 —— **9 条**

| 位置 | 它声称守什么 | **为什么执行不到** | 建议 |
| --- | --- | --- | --- |
| `TileProperties.fs:21`（属性 `解析任意字符串都返回值而不抛异常`） | 「能解析出来的，原串本身就是规范形」 | **随机字符串解析不出一张牌**：`Ok` 那一支 300 个样本一次没进过，整条属性等价于 `fun _ -> true`（唯一做判断的行就是它） | **补执行者**：喂一批合法记法（`ValidTileNotation` 已经有了） |
| `HumanCallTests.fs:252`（`Assert.DoesNotContain`） | 「该他出牌那一手没有吃/碰/大明杠/过的按钮」 | **那个 `for` 是空的**：`HumanSeat.buttons dahai` 在打牌那一手返回空表，循环一圈都没走 | **补执行者**：先断言按钮非空，或换成对「有按钮的那一手」断言 |
| `TableTests.fs:120`（`Assert.Equal(before.Kyoku + 1, after.Kyoku)`） | 「不连庄就进下一局」 | **种子 1177 那一局恒连庄**，`if renchan then … else …` 的 `else` 一趟没进过；连庄那一支照跑 | **补执行者**：再挑一颗子和了的种子 |
| `ObservationProperties.fs:99`（`\| None -> []`） | 取河的兜底 | **座位恒存在**：`GameState.player seat state` 对四个座位永远是 `Some`，这一支到不了 | 防御分支，**留着但要一个单元执行者**（票 111 `VoidCause.Expired` 那一档） |
| `RiichiProperties.fs:117`（`\| None -> []`） | 同上（取听牌打法的兜底） | 同上 | 同上 |
| `HumanAssistTests.fs:519`（`fired, model`） | 走手循环的**上限出口**（`moves > 300` 或已终局） | **循环恒从「到点第 6 次且刚打完一手」那个出口走**，上限出口没用过 | 防御分支，留着；它红了才说明那条循环走飞了 |
| `HumanAssistTests.fs:534`（`walk … (step KyokuAdvanced model)`） | 走手循环里「这一局打完了就开下一局」 | **六次到点都发生在同一局里**，没走到换局 | 同上 |
| `HumanCallTests.fs:519`（`\| [] -> asked`） | 「没人在等答复就不再答」的兜底 | **那一刻恒有人在等**（他先点之后模型席必被问） | 同上 |
| `StaleAskTests.fs:533`（`\| [] -> model`） | 同上（`poke` 的兜底） | 同上 | 同上 |

**这一档没有第十条**：`member` 这一档（1,072 条属性 / 用例）**零次的是 0 条**，
`assert` 这一档 2,961 条里零次的就是上表那 2 条。**xunit 的断言基本都在跑，问题出在属性的分支上。**

### 3.2 dotnet 侧 · 丙档：放行支零次 —— **4 条**（判据 4 的「谁也到不了」）

它们零次不代表断言坏了，代表**那一类局面一次也没出现**——而那一支本来就什么都不守（直接 `true`）。

| 位置 | 那一类局面 | 为什么到不了 |
| --- | --- | --- |
| `YakuProperties.fs:105`（`\| _, None -> true`） | 非一般型（七对子 / 国士）的和了 | **`AgariCase` 生成器只造得出一般型**：连同它旁边的 `\| _, Some _ -> false`（乙档）一起零次，即这条属性的后半句「其它型必然没有面子分解」**从没被检验过** |
| `GameStateProperties.fs:307`（`\| SanchaHora -> true`） | 三家和了 | 途中流局极稀；模块注释已写明「各自的授受在 `RyuukyokuTests` 里钉着」 |
| `RiichiProperties.fs:125`（`\| Action.Hora _ -> true`） | 立直后动作集里出现自摸和 | 立直家摸到和了牌本身就稀 |
| `FallbackTests.fs:199`（`\| None -> true`） | 决策包里没有脚手架 | 采样到的局面**恒有**脚手架，这一支是给「没有」准备的 |

### 3.3 两侧 · 乙档：失败支零次 —— **277 条**（dotnet 268 + 页面侧 9 条 `throw`）

`failwith "…"`、`\| Error _ -> false`、`throw new Error(…)` 这一族：**它们零次是「一趟绿的 CI」的必然结果**，
与页面侧那句 `problems.push(…)` 同理。**逐条列出来是噪音**，工具按文件计数（`ReviewTests.fs` 25 条、
`KanTests.fs` 14 条、`StaleAskTests.fs` 14 条……），全表在 `--json` 里。
**把它们与甲档分开，是这份普查最要紧的一条口径**——混在一起就是一张 354 行、九成是废话的表。

### 3.4 页面侧：零次 —— **73 条**（无资产 = 远端 CI 的形态）

| 组 | 条数 | **为什么执行不到** | 建议 |
| --- | ---: | --- | --- |
| `verify-review.mjs` 的 `strongLeg`（票 105/107 那一段：强 AI 对照标注） | **54** | **那份 6 MB 的产物不在 CI 上**（ADR-0006 边界 6：不入版本控制），于是复盘面板走「算不动」那一路，整段真推理的对拍**在 CI 里一次也不跑**。本机把资产放进 `web/public/baseline/` 之后**其中 52 条跑起来**（`if (row.strongText.includes(word))` 一趟 **854 次**，十几条 122 次）；剩下 2 条（`:1510` / `:1627`）两种形态都零次，它们要的是「强 AI 与你不同」那一档局面 | **CI 覆盖不到，报告 92/93 早写过定性结论；这次是第一次量出规模**。要么把它做成「有资产才跑的那一趟」并在 CI 里显式标注跳过了 54 条，要么给它一份小得多的产物 |
| `verify-baseline.mjs` 的 `playsForReal`（7 条）+ `verifyBaseline` 里那句资产检查（1 条） | 8 | 同一件事：这一段只在 `--asset` 手动档跑 | 同上 |
| `throw new Error(…)`（`verify-invariants` 4、`verify-review` 2、`verify-tracer` 1、两条用例里各 1） | 9 | **失败支**：编不出 `janpo`、参数不认识、语料空、wasm 喂不进去…… 绿的时候它们本来就是 0 | 不动 |
| `verify-export.mjs:281`（`座位 N 一条决策记录都没有`） | 1 | 它坐在 `if (withLlm && seats.length > 1)` 里，而 **CI 不调真实 provider**（`withLlm` 恒假） | 不动；它是手动 `--llm` 档的断言 |
| `verify-golden.mjs:72`（`…… 还有 N 处`） | 1 | 它在「已经红了」那一段里（`mismatches.length > 0`）——是**红了才走的印字**，不是断言 | 不动 |

### 3.5 **整份没跑**：2 份闸门，10 条断言点

| 文件 | 断言点 | 为什么 |
| --- | ---: | --- |
| `web/scripts/verify-custom-endpoint.mjs` | 6 | 文件头第一句就写着「**手动跑，不进 CI**」（票 30：要真浏览器 + 真 HTTP 端点 + CORS 场景） |
| `web/scripts/verify-llm-seat.mjs` | 4 | 同上（票 23：调真实 provider，CI 里零真实请求是硬约束） |

**这两份是「写明了的不跑」，不是缺陷**（判据 4 的正面例子）。列在这里是因为普查表必须能回答
「仓库里有断言而 CI 一次没跑」——答案就是这 10 条，**别的都跑了**。

### 3.6 有趟空转（`min = 0 < max`）：这一趟 **7 条**

| 位置 | 逐趟 | 那一支要等什么 |
| --- | --- | --- |
| `GameStateProperties.fs:78` | 1 1 **0** | 荒牌流局收尾时那个「被抢的杠」判据 |
| `ShantenProperties.fs:53` | **0** 2 1 | 手里已有四张、摸不进第五张 |
| `GameStateProperties.fs:98` | 3 **0** **0** | `step` 接受了一个动作那一支 |
| `RyuukyokuProperties.fs:129` | 1 4 **0** | 九种九牌真出现在动作集里 |
| `FuProperties.fs:41` | **0** 5 1 | 平和成立的和了 |

上一次跑（同一份工具、同一套代码）这一栏是 **25 条**，含「鸣牌：刚鸣完那一手…」整段判断体
（0 / 1 / 4 次）与「吃只吃上家」那五行（0 / 1 / 1）。**两份的并集才是这一栏的形状**（§2.2）。

### 3.7 非零那一头：分布

| 一趟里被求值 | dotnet（9,079 条） | 页面侧 CI 形态（1,274 条数得出来的） |
| --- | ---: | ---: |
| 0 次 | 281 | 73（本机有资产时 25） |
| 1 次 | 6,010 | 911 |
| 2–10 次 | 1,396 | 186 |
| 11–100 次 | 754 | 61 |
| 101–1000 次 | 536 | 25 |
| >1000 次 | 102 | 18 |

**开口最多的那一条是页面侧的语义不变量**：`web/tests/agent/invariants.ts:266`
（「找不到某一节的抬头」）一趟 **37,240 次**，第二名 29,792 次——票 41/49 那道闸门是这个仓库里
被求值得最狠的断言。dotnet 侧最狠的一行一趟 **1,183,488 次**（某条属性里 `List.forall` 的内层）。

## 4. 两条阴性对照（做完当场撤掉，原文留这儿）

**没有阴性对照的普查就是一张收据**（判据 20）。两侧各加两条，跑完就删。

### 4.1 dotnet 侧：`tests/Janpo.Engine.Tests/CensusControlTests.fs`（临时，已撤）

```fsharp
module CensusControlTests =

    [<Fact>]
    let ``阴性对照甲：永远执行不到的那条断言`` () =
        let counts = [ 1; 2; 3 ]

        for count in counts do
            // 这一行永远进不去（`counts` 里没有大于 100 的），普查表里它必须是零次。
            if count > 100 then
                Assert.True(false, "这条断言永远执行不到")

        Assert.Equal(3, List.length counts)

    /// `8z` 不是合法记法（ADR-0001），因此 `Ok` 那一支永远进不去
    [<Property>]
    let ``阴性对照乙：判断支永远进不去（等价于恒真式）`` (tile: Tile) =
        match Tile.parse "8z" with
        | Ok parsed -> parsed = tile
        | Error _ -> true

    [<Property>]
    let ``阴性对照丙：两侧同一个表达式`` (tile: Tile) = Tile.toMjai tile = Tile.toMjai tile
```

普查当场逮住三条，原样输出：

```
-- 甲档：真判断点零次（相邻行已归块）--
assert    tests/Janpo.Engine.Tests/CensusControlTests.fs:20 [阴性对照甲：永远执行不到的那条断言] Assert.True(false, "这条断言永远执行不到")
branch    tests/Janpo.Engine.Tests/CensusControlTests.fs:29 [阴性对照乙：判断支永远进不去（等价于恒真式）] | Ok parsed -> parsed = tile

== 恒真式嫌疑 ==
甲 · 成员跑得到、体内做判断的行全零（等价于 `fun _ -> true`）：2 条
   tests/Janpo.Engine.Tests/CensusControlTests.fs:27 [Property] 阴性对照乙：判断支永远进不去（等价于恒真式）
   tests/Janpo.Engine.Tests/TileProperties.fs:19 [Property] 解析任意字符串都返回值而不抛异常
丙 · 两侧同一个表达式：5 条
   tests/Janpo.Engine.Tests/CensusControlTests.fs:34 [阴性对照丙：两侧同一个表达式] let ``阴性对照丙：两侧同一个表达式`` (tile: Tile) = Tile.toMjai tile = Tile.toMjai tile
   …（另四条是真的，见 §5）
```

**顺带证到一件事**：最土的那种恒真式——`let ``某属性`` (state: GameState) = true`——**在这个仓库里根本编不过**：

```
error FS1182: 未使用值“tile”
```

`Directory.Build.props` 的 `--warnon:1182` 加上 `TreatWarningsAsErrors` 已经把它挡在编译期
（这是本票头一版对照的写法，编译当场红）。所以工具里那条「入参一次都没用到」的静态判据
**在本仓库恒为 0，不是它抓不住**。

### 4.2 页面侧：`web/tests/agent/census-control.test.ts`（临时，已撤）

```ts
test("阴性对照甲：永远执行不到的那条断言", () => {
  const values = [1, 2, 3];
  const problems: string[] = [];

  if (values.length > 100) {
    // 这一段永远进不去，里头那条断言一次也没被求值：普查表里它必须是零次。
    if (values[0] === 1) problems.push("这条断言永远执行不到");
  }

  assert.equal(problems.length, 0);
});

test("阴性对照乙：恒真式（两侧同一个表达式）", () => {
  const values = [1, 2, 3];

  assert.equal(values.length, values.length);
});
```

```
== 零次：这一趟一次也没被求值 —— 7 条 ==
gate    web/tests/agent/census-control.test.ts:18 [阴性对照甲：永远执行不到的那条断言] if (values[0] === 1) problems.push("这条断言永远执行不到");

== 恒真式嫌疑 ==
共 1 条
   web/tests/agent/census-control.test.ts:27 —— 两侧同一个表达式：assert.equal(values.length, values.length)
```

**头一版对照写错了，写在这里**：第一次写的是循环里的 `if (v > 100) push(…)`，普查报它 **3 次**而不是 0 次
——**没错，是我写错了**：`if` 的条件确实被求值了三次（三次都没红）。页面侧数的是**条件被求值几次**，
不是**它红了几次**。改成「坐在一个永远进不去的块里」才是真正的零次。这条口径写进了工具的文件头。

## 5. 恒真式：抓到什么、抓不住什么

三条判据（两侧的工具用同一套）：

| 判据 | 怎么抓 | 本仓库的结果 |
| --- | --- | --- |
| **甲 · 运行时空转** | 成员跑得到（entry > 0），可它体内**每一条做判断的行**都是 0 | **1 条真的**：`TileProperties.fs:19`（§3.1 第一行）。票 98 那条抢杠属性是同一形状——今天它有了定点锚点，所以那一支有 4 次，不再落网 |
| **乙 · 入参没用到** | 属性的参数名在函数体里一次都不出现 | **恒为 0**：`--warnon:1182` 让它编不过（§4.1） |
| **丙 · 两侧同一表达式** | 断言两侧 / `Assert.Equal` 的两个实参**源码逐字相同** | **4 条**，全部是**确定性属性**：`同一种子产出同一序列`（`RngProperties.fs:12`）、`同一种子与同一条件开出同一局`、`同一种子开出同一局`（`KyokuStartTests.fs:115`）、`同一种子建出同一座牌山`（`WallTests.fs:145`） |

**丙那 4 条要不要算恒真式，是个判断题，本票只报不改**：`f x = f x` 在**纯**函数上确实恒成立，
它唯一能抓的是「这个函数里偷偷藏了可变状态」。`Rng.ofSeed` 与 `Wall.build` 里**确实有原地洗牌的
`let mutable`**（风格文档规则 5 记着那两处），所以这几条断言不是纯废话——但它们**守的是「洗牌没有泄漏到调用之间」，
不是「同一种子给同一结果」**。要更硬就把右侧换成**另一条路径**（例如落盘的期望值 / 另一个进程算出来的），
判据 5 那条「引入第三个锚点」说的就是这件事。

**抓不住的（判据 4）**：两侧写法不同、但在所有可达输入上恒等的断言——例如
`Assert.Equal(List.length xs, List.length (List.rev xs))`。**这一族只有变异测试抓得住**
（把被测处真弄坏看它红不红，判据 1 的那条判法），而变异测试要跑 N 遍全量测试，比这份普查贵两个数量级。
本票**没做**，理由就是这一句。

## 6. 没量到的那一批（判据 4：抓不住的要写出来）

| 没量的 | 为什么这一批没量 |
| --- | --- |
| `scripts/check-style.sh` / `check-single-source.sh` / `check-narration.sh` / `check-third-party.sh` / `check-pages-dist.sh` | 它们是 **bash + 内嵌 python 的 grep 管道**，「一条断言」在那里是「一条正则 × 一个文件」——**口径与这两份工具不是一回事**，硬凑一个数只会让这张表变得可疑。要量得另起一种量法（例如给每条规则加一个计数器），那是另一票 |
| `biome ci` / `tsc --noEmit` / `dotnet fantomas --check` | 第三方检查器，断言在它们自己的代码里，不在本仓库 |
| Fable / Vite 构建 | 不含断言（构建失败即红） |
| `.github/workflows/` 里 Pages 那条流水线 | 只在远端跑，本机量不到；它的闸门是 `check-pages-dist.sh`（同第一行） |
| `page.evaluate(…)` 里的 32 条页面侧断言 | 在浏览器进程里求值，node 的覆盖数据看不见（§2.3）。要量得开 Chrome 的 `Profiler.takePreciseCoverage`，工程量另算 |
| 引擎 `src/**` 里的守卫（`HandShape.create` 的张数校验等） | 它们是**产品代码的返回值**不是测试断言，本票的口径不收；真要数，`--include "[Janpo.Engine]*"` 一开就有数 |
| `probe/`、`scripts/oracle/`、`scripts/paifu/` | 手动工具，不在 `ci.sh` 的路径上 |

**页面侧还有一处口径要说明**：工具把 `if (…) X.push(…)` 一律当断言收，因此
`marks.push({t: …})` 那种**数据收集**也可能被收进来——站点数（1,316）是**上界**。
自审：22 份 `verify-*.mjs` 里源码 786 处 `push(`/`return failure(`，工具认出 727 个站点（差额是
「一个条件下连推几条」算一条、以及 `problems.push(...另一个函数的结果)` 这种把断言记在**被调那份**里）；
**零次那 73 条逐条看过，73 条全是真断言点或真失败支，没有一条是数据收集。**

## 7. 要不要接进 `ci.sh`：**不接**，账在这里

| | 秒 |
| --- | ---: |
| 现在的 `./scripts/ci.sh` 全程 | **115–118**（两次实测：115.2 / 117.5） |
| dotnet 侧普查（Debug 构建 + 3 趟 × 2 工程插桩） | **125** |
| 页面侧普查（`node --test` 0.2 + 语义不变量 3.7 + 浏览器跑道 84 + 基线 7 + 分析） | **95** |
| 接进去之后 | **≈ 335（2.9×）** |

接进去还得往仓库里加一个 NuGet 工具（`coverlet.console`）或一条 `PackageReference`，
而**它的答案只在「测试改了」时才变**——那正是收尾与 code-review 该问的时刻，不是每次提交。
票 112 那一趟 16.5 秒已经是第二贵的，这一份是它的 13 倍。**做成手动工具，写在两份文件头上。**

**建议的用法**：新加一族属性 / 一道闸门时跑一次（`--runs 1 --project <工程>` 只要 20 秒），
看新写的那几行有没有落进零次那一栏——**那比事后普查便宜得多**。

## 8. 排序建议（本票不改，交回调度器）

**A. 值得补执行者（三条，按值钱程度）**

1. **`verify-review.mjs` 的 `strongLeg` 54 条在 CI 里零次**（§3.4）。这是全仓最大的一片「看着在守、其实没执行」，
   而它守的是票 105/107 的核心验收（复盘面板里强 AI 那一列的每一个数）。
   两条路：给 CI 一份**小得多的**基线产物，或把「CI 里这 54 条不跑」做成一句**由数据印出来的**声明
   （现在没有任何地方说得出这个数）。
2. **`TileProperties.解析任意字符串都返回值而不抛异常`**（§3.1）：改喂一批合法记法就能让那一支开口，
   代价近乎零，而它现在等价于 `fun _ -> true`。
3. **`HumanCallTests` 那个空循环**与 **`TableTests` 那条 `else`**（§3.1 第二、三行）：
   两条都是「一句断言写在一个到不了的位置上」，各补一个执行者即可。

**B. 该删的：一条都没有。** 甲档九条里没有一条是死代码——四条是防御分支（下一档），
两条是循环出口，三条在 A 里。

**C. 留着当防御分支，但要一个单元执行者（六条）**：`ObservationProperties.fs:99`、
`RiichiProperties.fs:117`、`HumanAssistTests.fs:519/534`、`HumanCallTests.fs:519`、`StaleAskTests.fs:533`。
照票 111 `VoidCause.Expired` 那条的形状办：**分支留着，另加一条直接构造那个输入的具名用例**，
让它每趟都被跑一次。

**D. 判据 4 那一档（丙档四条 + 那两份手动闸门）**：不补执行者，但**在代码里写明「谁也到不了」**。
其中 `YakuProperties.fs:105` 值得单独看一眼——它意味着「非一般型必然没有面子分解」这后半句
**从来没被检验过**，而 `AgariCase` 生成器造不出七对子 / 国士。

## 9. 留给人的待审项（**只描述不编号**，判据 17）

1. **CI 里 54 条 `strongLeg` 断言零次**（§3.4 第一组）——影响面最大的一条，涉及 `verify-review.mjs`
   与那份 6 MB 产物的取舍，出了本票的边界（不碰 `Review*`）。
2. **`YakuProperties` 那条属性的后半句从没被检验过**（§3.2）——要动 `AgariCase` 生成器。
3. **四条「两侧同一个表达式」的确定性属性**（§5）要不要引入第三个锚点。
4. **`TileProperties` / `HumanCallTests` / `TableTests` 那三条**（§8-A）各是一次十分钟的小修，
   但都要动别人票里的文件。
5. **六条防御分支缺单元执行者**（§8-C）。
6. **要不要给 `Soak` / `PaifuDifferential` 那一族的 `JANPO_PAIFU_DIR` 大扫路径单独量一次**：
   本票量的是 CI 的形态，那条路在 CI 里整段不跑（`PaifuDifferential.fs` 在 Release 那次预扫里
   有 81 行零次，全在那一段），**而它是票 108–110 的地盘，本票不碰**。

## 10. code-review（Standards + Spec 两轴，fixed point `wzlvqxqr` / `37e1b2e4`）

派不出 sub-agent，两轴顺序自跑（`docs/agents/workbook.md` 允许）。
diff：新增 `scripts/fsi/assertion-census.fsx`（+760）、新增 `web/scripts/assertion-census.mjs`（+471），
外加本报告、票文件与 `DECISIONS.md`。**`src/**`、`tests/**`、`web/scripts/verify-*.mjs`、
`scripts/ci*.sh` 一字未改。**

**Standards**（`docs/agents/fsharp-style.md`；规则 6 明写 `.fsx` 同样受约束）

- 规则 1 / 3（不许从里往外读）：`.fsx` 里没有 `f (g (h x))` 形状；聚合一律管道
  （`sites |> List.filter … |> List.length`、`coverageFiles |> List.groupBy runOf |> List.map …`）。
- 规则 2（lambda 包一层调用）：0 处；`projects |> List.collect (fst >> membersIn)` 用的是 `>>`。
- 规则 4（不许强行管道）：`Path.Combine(…)`、`printfn` 的实参、算术保持原样。
- 规则 5（`let mutable`）：新增 **0** 处（`splitTop` 用 `Seq.fold` 折出括号深度）。
- 规则 8（多余括号）：`scripts/check-style.sh` 通过（它扫 `scripts/fsi/*.fsx`）。
- `fantomas --check`：通过（写盘那一遍已跑）。
- 页面侧：`biome ci --error-on-warnings` 通过；没有新依赖——`vite` 的 `parseAst` 与 node 自带的
  `stripTypeScriptTypes` 都是现成的（**`typescript` 那条路走不通**：本仓库装的是 7.0.2，
  它的 JS API 里没有 `createSourceFile`，实测 `ts.ScriptTarget` 是 `undefined`）。
- 术语：新词只有工具自己的（`Site` / `Grade` / `gate` / `member`），没碰 `CONTEXT.md` 的日麻词。
- **review 里抬出三条 blocking，当场修了并重跑了全量**：
  ① `coverletPath` 原来是模块级的急求值，于是 **`--from` 那一路也会去装 coverlet**
  （没网的机器上白白红一次）——改成 `lazy`；
  ② 页面侧 `hitsAt` 原来把**所有进程的区间混成一堆**再挑最窄的，而同一份源码在不同进程里
  块划分不一定一样（没被调用过的函数 V8 只给一条函数级区间）——改成**每个进程各取各的最内层再相加**；
  改前改后数一样（零次 73 / 数得出来 1,274 / 最高 37,240），但**口径站得住了**；
  ③ `--from` 指一个不存在的目录时抛的是 `DirectoryNotFoundException`，换成一句读得懂的话。
- **味道（判断题，只记录不改）**：①两份工具的报表结构是**平行的两份实现**（一份 F# 一份 JS），
  合不了——它们读的是两种完全不同的覆盖格式；②`ASSERT_NAMES` 把 `push` 一律当断言收，
  是有意的宽判据（§6 交代了它的上界性质）；③`gradeOf` 用行尾文本判失败支，
  行内字符串里含 `//` 时会误判——本仓库实测 0 例，写在函数注释里。

**Spec**（票 `.scratch/llm-riichi-arena/issues/113-assertion-census.md`）

- 「工具」：两侧各一份，都能回答「一趟 `ci.sh` 里每条断言各执行了多少次」，都支持 `--from` 重算与 `--json` 全表。
- 「清单」：§3 按执行次数升序，零次的排最前，**每一行都有一句「为什么执行不到」**，
  没查清的一条也没有（票面闸门第三条）。
- 「恒真式也要抓」：§5 三条判据，抓到 1 条真的 + 4 条判断题；抓不住的那一族（写法不同但恒等）写明了为什么。
- 「排个序」：§8 分四档（补执行者 / 该删 / 留着当防御分支但要单元执行者 / 判据 4 那一档）。
- 闸门：两条阴性对照都落在该落的地方（§4，原文留下，控制已撤）；`./scripts/ci.sh` 全绿；
  **一条既有断言的语义都没动**。
- 边界：只新增 `scripts/fsi/*.fsx` 与 `web/scripts/*.mjs` 两个文件；发现的问题只列不改（§9）；
  `Paifu` / 账那一段、`Review*.fs`、`Playback.fs` **只读不碰**（它们出现在表里是因为普查覆盖它们，
  一个字没改）；没往 `ci.sh` 里加东西（§7 算了账）。
- **与票面预期的偏离两处**：①票面允许「两侧各一份或只一份」，本票做了两份——两侧的计数器完全不同源
  （coverlet vs V8），合成一份没有意义；②票面说「按执行次数升序的表」，本票把表**按档分了三栏**
  （真判断点 / 失败支 / 放行支）再各自升序——不分档的话那张表 354 行里有 272 行是「绿的必然结果」，
  会把真正的九条埋掉（这一条判断写在 §3.3）。
