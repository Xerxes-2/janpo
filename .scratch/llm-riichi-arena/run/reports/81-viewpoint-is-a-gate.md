# 81 — 视角成为信息闸门；思考气泡只放一句话

**结论：可见性从此是两根轴的 AND——`TableState.reveals`（视角）× `unlocked`（ADR-0003），
合在 `TableState.bubbles` 一处；气泡里只放一句话理由，截了会说，全文在点开那一屏里。**
`./scripts/ci.sh` **EXIT=0**，49.5s；dotnet 745 + **205** 条（原 745 + 202），浏览器闸门**十四趟**全 ✓
（`verify-bubbles` 4.2s、`verify-home` 1.9s、`verify-inbound` 2.2s）。

四件事落地：① **座位 N 视角只看得见那一席**的气泡、状态线与全文面板，**DOM 上根本没有别席的气泡元素**；
② 上帝视角四家全开，Live 与回放一致，**回放里终局也不放开**（escape hatch 是那一按，不是时间）；
③ 气泡文字 **`reason` 优先**，只有 thinking 时取它的头一段，超过 72 字截断并挂一枚「点开看全文」
——`max-height: 4.2rem; overflow: hidden` 那条无声硬裁拿掉了（79 §8 报的病）；
④ 术语表 `Thinking Bubble` 那一条按授权改了，别处一个字没动。

---

## 1. 两根轴怎么合

### 1.1 加的是一个函数，不是一个条件

```fsharp
// src/Janpo.Web/TableState.fs
let reveals (model: TableModel) (seat: Seat) : bool =
    match model.Viewpoint with
    | Viewpoint.God -> true
    | Viewpoint.Seated viewer -> viewer = seat
```

它与既有那一根**正交**，合起来是 AND，而且**只在取值器那一处合**：

```fsharp
let bubbles (model: TableModel) (table: Table) : Seat -> Bubble option =
    fun seat ->
        // 两根轴是 AND：视角掩蔽（票 81）× unlocked（ADR-0003）
        if not (reveals model seat) || not (unlocked model table) then None
        else …（票 76 原样：Asking 压过 tryFindBack）
```

`unlocked`（`not (humanSeated model) || Table.result table |> Option.isSome`）**一个字没动**——
它是 M3 真人坐席的地基，票面明令。

| 谁 | 读什么 | 不读什么 |
|---|---|---|
| `unlocked`（ADR-0003） | 对局配置（有没有真人）+ 终局状态 | 谁在看 |
| `reveals`（票 81） | `model.Viewpoint` | 牌桌、终局、有没有记录 |

**这不违反 ADR-0003 那条 consequence**（「可见性挂在对局配置与终局状态上，不挂在用户是谁上」）：
ADR 那句话的对象是**身份/权限**（「围观者不是权限级别，只是视角」）。`reveals` 读的正是**视角**，
而视角是一排谁都按得了的按钮——escape hatch 就在旁边。用例 `两根轴正交：……` 把这件事钉成了
「两根轴各自只读自己那一份输入」。**`docs/adr/*` 一个字没改**（硬约束 6）。

### 1.2 三个消费点，一份判据

票面要害 2 是「改一个函数，别在视图里再滤一遍」。`bubbles` 是唯一数据源没错，但状态线**不读气泡**
（它读 `live.Agent` 与 `live.Awaiting`），于是「同一条规则治」落成了**同一个函数被三处消费**：

| 消费点 | 怎么拿到规则 | 看不见时是什么 |
|---|---|---|
| 气泡 `TableState.bubbles` | 自己调 `reveals` | `None` → `ThinkingBubble.at` 一行都不画（**DOM 上没有**） |
| 状态线 `AgentLine.agentLine` | **谓词从外面传进来**（`TableBoard`：`AgentLine.agentLine (TableState.reveals model) live table`） | 那一席的句子不进这一行；`data-asking-seats` 同样只列看得见的 |
| 全文面板 `TableState.detail` | 自己调 `reveals` | `None` → 面板整块不画 |

**状态线收谓词而不是 `TableModel`**：头一行只属于 Live（票 71 的理由是「它收 `LiveTable`，类型就拦住了」），
传 `TableModel` 会把那条类型约束拆掉。

**面板那一处是补漏而不是第二处判据**：面板今天只从气泡点得开，而别席的气泡在座位视角下根本不存在
——但「今天点不到」不是闸门（切视角不会把已经摊开的面板收起来）。它调的仍是同一个 `reveals`。

### 1.3 被挡下的那几席要说一句

状态线上多了一句 `　另 N 席的状态被这个视角挡着（切上帝视角看全场）`（并带 `data-hushed="N"`）。
理由与票 76 那句「这一局没有思考气泡……」同一条：**「别席没说话」与「别席说了但你这个视角看不见」
在页面上长得一模一样**。它**只在真有话被挡下时才出现**（`hushed` 数的是「被挡下且此刻有话要说」的席数），
所以四家 bot 那一桌上一个字都不多。

---

## 2. 气泡文字的取法与溢出形态

### 2.1 判据（写在 `Bubble.said` 的注释里）

```fsharp
match record.Reason, record.Thinking with
| Some reason, _ -> clip false reason                    // reason 优先（票 81 主人裁的）
| None, Some thinking ->
    let head = firstParagraph thinking                   // 头一段 = 第一个换行之前那一截
    clip (head <> thinking.Trim()) head                  // 真丢了东西才算「截过」
| None, None -> "（这一手没留下理由与思考原文）", false     // 空气泡与「没有气泡」分不出来
```

`clip` 是唯一那处判断，**一个函数同时给出「写什么」与「截没截」**：

```fsharp
let clip (more: bool) (text: string) : string * bool =
    if String.length text <= sentenceLimit then (if more then text + "……" else text), more
    else text.Substring(0, sentenceLimit) + "……", true
```

于是 `Bubble.toDisplay = said >> fst`、`Bubble.clipped = said >> snd`——分成两个函数各算一遍的话，
「三个点」与「点开看全文」迟早会漂到一边有、另一边没有。

**「头一段本来就是全部」时不装作截过**：票面写的是「取头一段并三点号收尾」，但一段短 thinking
后面什么都没丢，硬加三个点是句谎话（用例 `只有 thinking 且它本来就只有一段` 钉着）。

### 2.2 上限 72 字是量出来的，不是拍的

`web/public/demo-paifu.json` 的 **464 条真理由**（票 79 换上去那份）：

| min | p25 | 中位 | p75 | p90 | p95 | max |
|---|---|---|---|---|---|---|
| 25 | 40 | **48** | 62 | 77 | 91 | 260 |

72 字 → **87.3% 的理由整句放得下**，余下 12.7% 截断 + 招子。它同时是「气泡不把牌桌撑变形」的判据：
最窄那一席一行约 22–25 个汉字，72 字 ≈ 3 行，**正是票 76 那个 `max-height: 4.2rem` 的高度**
——所以 CSS 那条硬裁可以整个拿掉：长度在 F# 里就已经有界，而且**截了会说**。

### 2.3 溢出形态：DOM 上多一枚元素

`.bubble-more`（新 testId `seat-{N}-bubble-more`，**只增不改**）：另起一行、小字、点线下划线的
「点开看全文」。**「有没有」是 F# 里 `Bubble.clipped` 说了算**，CSS 里没有任何「什么时候显示」的判据
——闸门读的是元素在不在（票面第 3 条）。

---

## 3. `verify-home` 第⑦条怎么跟着改的（前后各贴一遍）

### 3.1 前（票 79 立的十条：2 + 4×2）

```js
// ⑦ 这份牌谱带着推理（票 79 把票 76 那一条翻了面）……**这一段量的是末帧**
const bubbles = await page.locator('[data-testid$="-bubble"]').count();
const said = await page.getByTestId("table-no-bubbles").count();
if (bubbles !== 4) { missing.push(`首页那场四席都是模型，末帧上却只有 ${bubbles} 个思考气泡（该有 4 个）：…`); }
if (said !== 0) { missing.push('首页那份牌谱带着决策记录，页面上却还挂着「这一局没有思考气泡」…'); }

for (const seat of [0, 1, 2, 3]) {
  const bubble = page.getByTestId(`seat-${seat}-bubble`);
  if ((await bubble.count()) === 0) continue;
  const state = await bubble.getAttribute("data-bubble");
  const text = ((await bubble.textContent()) ?? "").replace("说", "").trim();
  if (state !== "spoke" && state !== "troubled") { missing.push(`座位 ${seat} 的气泡停在「${state}」态：…`); }
  if (text.length < 8) { missing.push(`座位 ${seat} 的气泡里只有「${text}」：…`); }
}
```

### 3.2 后（十条一条不改，**方向改成「按视角该有几个就有几个」，另加 6 条**）

```js
// **数之前先说清这是哪个视角**（票 81）：「四个」是上帝视角下的死数。
await page.getByTestId("table-view-god").click();
const bubbles = await page.locator('[data-testid$="-bubble"]').count();
const said = await page.getByTestId("table-no-bubbles").count();
if (bubbles !== 4) { missing.push(`首页那场四席都是模型，上帝视角下的末帧却只有 ${bubbles} 个思考气泡（该有 4 个）：…`); }
if (said !== 0) { … }                                    // ← 原样

const saidBySeat = [];
for (const seat of [0, 1, 2, 3]) { …原来那两条一字未改，顺手把四句话收起来… }

// **阳性对照**（新）：四句话互不相同——否则「四个写着同一句话的空壳」也能让上面那几条变绿。
if (new Set(saidBySeat).size !== saidBySeat.length) { missing.push(`上帝视角下四家的气泡里有重复的话（…）：四家该各说各的`); }

// ⑦b 视角是一道信息闸门（新，4 条）：坐到座位 N 上，DOM 上只剩那一席那一个气泡。
for (const seat of [0, 1, 2, 3]) {
  await page.getByTestId(`table-view-${seat}`).click();
  const only = await page.locator('[data-testid$="-bubble"]').count();
  const mine = await page.getByTestId(`seat-${seat}-bubble`).count();
  if (only !== 1 || mine !== 1) { missing.push(`坐到座位 ${seat} 上，末帧上还有 ${only} 个思考气泡（自家的 ${mine} 个）：…`); }
}

// 阳性对照（新）：切回上帝四家必须都回来，否则上面那四轮什么都没证明。
await page.getByTestId("table-view-god").click();
if ((await page.locator('[data-testid$="-bubble"]').count()) !== 4) { missing.push(`切回上帝视角只有 … 个气泡（该有 4 个）：…`); }
```

**强度对账**：原十条**一条没删、一条没放宽**（`!== 4`、`!== 0`、`spoke/troubled`、`≥8 字` 全在，
只是明说了它们量的是上帝视角，而首页默认就是上帝视角——裁决 71-8，同一道闸门的⑤刚核过）；
新增 4（座位视角只剩一家）+ 1（切回上帝四个）+ 1（四句互不相同）= **10 → 16 条**。
这一段现在停在**末帧**（结算那一屏），因此顺带钉死了「回放里终局也不放开」。

### 3.3 另外三道闸门跟着动的地方

| 闸门 | 改了什么 | 为什么 |
|---|---|---|
| `verify-bubbles`（CI 第 13 趟） | 两程都先按一下 `table-view-god`；**新增第⑨程**（座位视角只剩一席的气泡与状态线、别席那句「模型选完了：……」一个字都找不到、`data-hushed=3`；切回上帝四家都回来、四句互不相同、状态线四句都在）；**新增第⑩程**（座位 0 的端点回一句 92 字的长理由 → 气泡截过、有三点号、挂招子；其余三席短理由 → **一枚招子都不许有**；同一手点开，面板里是**完整**那 92 字） | `?table=1` 默认坐在座位 0 上，不切的话这一道量的是闸门自己 |
| `verify-inbound`（第 14 趟） | 那条阳性对照的**期望值翻面**：气泡里必须是 `INBOUND-REASON-MARK`、**不许**出现 `INBOUND-THINKING-MARK`，而 thinking 全文改由**点开之后的面板**（`bubble-thinking`）钉着；**另加一条只有 thinking 没有 reason 的记录**（座位 2），钉「头一段 + 三点号 + 招子，后面那几段不许出现」 | 票 76 是 thinking 优先，那条断言的期望值被主人的裁决作废（判据 5：期望本身不再成立）。**条数 1 → 6，一条没放宽**；顺带让 `firstParagraph` 在**浏览器里**真的跑过（dotnet 侧那几条只证明 .NET 上对） |
| `verify-llm-seat` / `verify-custom-endpoint`（手验，不进 CI） | 打开页面后按一下 `table-view-god` | 它们默认把模型坐在**座位 1**（`--seats` / `--seat` 的默认值），而页面默认坐在座位 0 上——不切的话 `data-agent` 读到的是掩蔽后的那一份 |

---

## 4. 每条新断言先红一次（判据 1 的原始输出）

**十次，全部实跑**：改**产品代码**（不是改断言），跑同一条命令，抄红的原文，再 `diff` 对回备份。

**红-1｜视角不掩蔽**（`reveals` 恒 `true`，即这一票之前的样子）

```
（dotnet）
ThinkingBubbleTests.视角是一道信息闸门：坐座位 N 只看得见自家，上帝视角四家全开 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: 1 / Actual: 4
ThinkingBubbleTests.回放里终局也不放开：escape hatch 是上帝视角那一按，不是时间 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: 1 / Actual: 4
ThinkingBubbleTests.两根轴正交：视角那一根只读视角，ADR-0003 那一根只读对局配置与终局状态 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: False / Actual: True
ThinkingBubbleTests.点开气泡：全文面板给的是那一手的记录与当时的局面快照 [FAIL]
  Assert.True() Failure　Expected: True / Actual: False
TablePageTests.「在想」按席各记各的秒数：Waited 一秒一跳，回执到了就停 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: null / Actual: Some(Thinking (0, 12))

（verify-home）
首页少了该给访客的东西：
坐到座位 0 上，末帧上还有 4 个思考气泡（自家的 1 个）：该只剩自家那一个——视角与手牌同一条规则，回放里终局也不放开（票 81）
（座位 1 / 2 / 3 同）

（verify-bubbles）
坐到座位 3 上，页面上还有 4 个气泡（自家的 1 个）：该只剩自家那一个（票 81：视角与手牌同一条规则）
坐到座位 3 上，状态线里还写着座位 0 的理由：「座位 0 的模型选完了（5 ms）：假端点甲说：……」（气泡拦住了而状态线漏了，那闸门就只是个摆设）
坐到座位 3 上，状态线说被挡下的是 0 席（该是 3 席）
```

**红-2｜回放里终局就放开**（`bubbles` 里写成 `not (reveals …) && Option.isNone (Table.result table)`）

```
ThinkingBubbleTests.回放里终局也不放开：escape hatch 是上帝视角那一按，不是时间 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: 1 / Actual: 4
失败! - 失败: 1，通过: 19，总计: 20
```

**红-3｜气泡文字退回票 76 的优先级**（thinking 优先）

```
（dotnet）
ThinkingBubbleTests.说了什么：reason 优先，只有 thinking 时取它的头一段并三点号收尾 [FAIL]
  Expected: Some(第 7 手的一句话理由（座位 0）) / Actual: Some(第 7 手的思考原文（座位 0）)
ThinkingBubbleTests.气泡里的字来自那一手的决策记录：改一个字，气泡跟着变 [FAIL]
  Assert.NotEqual() Failure　Expected: Not Some(第 7 手的思考原文（座位 0）)
ThinkingBubbleTests.气泡只放一句话：真语料里的长理由截到上限并说一声，面板里仍是全文 [FAIL]

（verify-inbound）
牌谱从外面进来的两条路验收没过：
气泡里不是那句一句话理由：「说先数向听：这手牌 2 向听，切 9 万最不亏——INBOUND-THINKING-MARK…」
气泡里把 thinking 全文也塞进去了：「说先数向听：……」（票 81：气泡只放一句话，全文在点开那一屏）
```

**红-4｜截了不说**（`clipped` 恒 `false`）

```
（dotnet）
ThinkingBubbleTests.气泡只放一句话：真语料里的长理由截到上限并说一声，面板里仍是全文 [FAIL]
  真语料里最长那条理由在气泡里应当是截过的
ThinkingBubbleTests.说了什么：reason 优先，只有 thinking 时取它的头一段并三点号收尾 [FAIL]
  Assert.Equal() Failure: Values differ　Expected: Some(True) / Actual: Some(False)

（verify-bubbles）
座位 0 的理由共 92 字，气泡里写了 75 字（含「说」与招子），招子 0 枚
思考气泡这一道没过：
座位 0 的理由有 92 字（远过气泡那一句话的量），气泡上却没有「点开看全文」那枚招子
（[data-testid="seat-0-bubble-more"] 有 0 枚）：硬裁而不说正是票 81 要治的那条病（79 §8）
```

**红-5｜根本不截**（`clip` 原样返回）

```
（dotnet）
ThinkingBubbleTests.气泡只放一句话：真语料里的长理由截到上限并说一声，面板里仍是全文 [FAIL]
  真语料里最长那条理由在气泡里应当是截过的

（verify-bubbles）
座位 0 的理由共 92 字，气泡里写了 93 字（含「说」与招子），招子 0 枚
思考气泡这一道没过：
座位 0 的气泡里把 92 字的理由整句都写上了：那不是一句话
座位 0 的气泡截了却没有三点号收尾：「说假端点甲说：座位 0 这一手照它的算法只能这么打；本来还想说……」
```

**红-6｜全文面板不受闸门管**（去掉 `detail` 那一行 `Option.filter`）

```
ThinkingBubbleTests.点开气泡：全文面板给的是那一手的记录与当时的局面快照 [FAIL]
  Assert.True() Failure　Expected: True / Actual: False
失败! - 失败: 1，通过: 19，总计: 20
```

**红-7｜状态线不掩蔽**（`TableBoard` 传 `fun _ -> true`）

```
思考气泡这一道没过：
坐到座位 0 上，状态线里还写着座位 1 的理由：「座位 0 的模型选完了（5 ms）：……；座位 1 的模型选完了（4 ms）：假端点乙说：……；座位 2 的模型选完了（3 ms）：……；座位 3 的模型选完了（10 ms）：……」（气泡拦住了而状态线漏了，那闸门就只是个摆设）
坐到座位 0 上，状态线里还列着座位 1：「……」
（座位 1 / 2 / 3 各三条同形）
```

**红-8｜掩蔽了却不说**（`hushed` 恒 0）

```
思考气泡这一道没过：
坐到座位 0 上，状态线说被挡下的是 0 席（该是 3 席）
坐到座位 1 上，状态线说被挡下的是 0 席（该是 3 席）
坐到座位 2 上，状态线说被挡下的是 0 席（该是 3 席）
坐到座位 3 上，状态线说被挡下的是 0 席（该是 3 席）
```

**红-9｜什么都掩蔽（阳性对照的那一半）**（`reveals` 恒 `false`）

```
首页少了该给访客的东西：
首页那场四席都是模型，上帝视角下的末帧却只有 0 个思考气泡（该有 4 个）：…（票 79）
坐到座位 0 上，末帧上还有 0 个思考气泡（自家的 0 个）：该只剩自家那一个…（票 81）
（座位 1 / 2 / 3 同）
切回上帝视角只有 0 个气泡（该有 4 个）：上一条「坐座只剩一家」因此什么都没证明
```

**红-10｜四家说同一句话（阳性对照）**（`toDisplay` 恒 `"它想了想，选了这一张"`）

```
首页少了该给访客的东西：
上帝视角下四家的气泡里有重复的话（4 个气泡只有 1 句不同的话）：四家该各说各的
```

**红-11｜`firstParagraph` 不取头一段**（返回整段；**这一条专为浏览器侧那条支路**）

```
牌谱从外面进来的两条路验收没过：
只有 thinking 时气泡里把后面那几段也写上了：「说先数向听：这手牌 1 向听——INBOUND-HEAD-MARK
再看安全度：这一段在气泡里不允许出现——INBOUND-TA…」
只有 thinking 时气泡里取了头一段却没有三点号收尾：「……」
只有 thinking 时气泡上没挂「点开看全文」：后面那几段就这么无声无息地没了
```

---

## 5. 截图：我亲眼看到了什么（判据 7）

四张，**都自己打开看过**。

### 5.1 `docs/images/home.png`（重出，1088×1331）

上帝视角、第 26 手。四家各一个气泡，**四句都完整，没有一句被切**：

- 座位 2：`说 打出发，维持2向听。保留4m进张空间，同时索子这块123456789有不错的延伸可能性；发是无用的孤张字牌，先处理掉最合理。`（2 行）
- 座位 1（**79 §8 报被切的就是这一家**）：`说 保留万子45678的好形搭子与索子的进张，切掉孤张8筒，不向听倒退且有效牌最多（39枚），维持3向听。`
  ——3 行，**最后一行「维持3向听。」整整齐齐落在框里**，那条病没了。
- 座位 3：`说 切4筒保留6筒面子胚子，维持3向听且有效牌33枚较优，手牌朝断幺方向靠拢。`
- 座位 0：`说 打3万保持1向听，保留最多的17枚有效牌，包含34566索的面子结构，进张面最广。`

这一帧上四句都在 72 字以内，因此**一枚招子都没有**——这正好是「不该有的时候真的没有」那一半。

### 5.2 座位 1 视角（一次性探针，`/tmp/t81shots/81-seat1.png`；脚本跑完删了）

同一份牌谱、同一帧（第 21 手），只按了一下「座位 1」：

- **只有座位 1（自家，画在下方）有气泡**：`说 打1索，保留筒子顺子型（1p 3p 5p 8p）和万子块，维持3向听且有效牌种类较多，索子部分最弱无搭子，先弃掉。`
- 另外三家：手牌**整排扣着**（蓝背），**一个气泡都没有**——与手牌同一条规则，一眼看得出来。
- 视角那一排上「座位 1」是按下去的，自家那一格挂着「视角」标记。

同一帧的上帝视角（`81-god.png`）四家气泡俱全 —— 两张对着看就是这一票的全部。

### 5.3 长理由那一屏（`node scripts/verify-bubbles.mjs --shoot /tmp/t81shots`）

座位 0 的端点回一句 92 字的理由。气泡里是：

> 说 假端点甲说：座位 0 这一手照它的算法只能这么打；本来还想说一说这一手的安全度与巡目，
> 可惜一个气泡里只放得下一句话，剩下的得点开全文面板才看得**到……**
> _点开看全文_（另起一行、小字、点线下划线）

**三行 + 一行招子，没有被框切掉的半行**；牌桌下面摊开的面板里 `一句话理由` 那一格是**完整**的 92 字。
兜底那一屏（`bubbles-troubled.png`）照旧：朱红左边框 + `兜底 action_id=9999 不在这一手的合法动作集里（重试 2 次仍无结果）`。

`docs/images/table.png` **没重拍**：那一页是四家自带 bot，没有气泡、没有被掩蔽的状态（`hushed = 0`），
这一票在那张图上不改一个像素。

---

## 6. 闸门：谁在守什么

### 6.1 dotnet 侧（`ThinkingBubbleTests` 17 → **20** 条；`TablePageTests` 一条加硬）

| 用例 | 钉的是 |
|---|---|
| **视角是一道信息闸门：坐座位 N 只看得见自家，上帝视角四家全开**（新） | 上帝 4 个且**四句互不相同**；座位 N 恰好 1 个、且与上帝视角下它自己那一个**逐字相同**、其余三席是 `None` |
| **回放里终局也不放开：escape hatch 是上帝视角那一按，不是时间**（新） | 末帧（`Table.result` 是 `Some`，防空转）上座位视角仍只剩一家 |
| **两根轴正交：视角那一根只读视角，ADR-0003 那一根只读对局配置与终局状态**（新） | `TablePage.reveals` 的真值表（4×4）+ 上帝视角下四席全在 |
| 说了什么：**reason 优先**，只有 thinking 时取它的头一段并三点号收尾（改） | 四种记录各一句话 + `clipped` 三态（短 reason=false、多段 thinking=true、单段 thinking=**false**） |
| **气泡只放一句话：真语料里的长理由截到上限并说一声，面板里仍是全文**（新） | 语料取 `demo-paifu.json` 里**最长那条**（自带 `> 100 字` 的防空转断言）：截过、有「……」、`≤ 80 字`、开头 20 字一字不改；面板里 `detail.Record.Reason` 是全文 |
| 气泡里的字来自那一手的决策记录：改一个字，气泡跟着变（加硬） | 改 reason → 气泡变；**改 thinking → 气泡不变而记录仍旧跟着变**（数据源只有一处 + 新优先级） |
| 点开气泡：全文面板给的是那一手的记录与当时的局面快照（加硬） | 面板同受视角闸门管：第 8 手是座位 1 的，坐到座位 0 上就摊不开 |
| 在想那一态是按座位取的（加硬） | 先切上帝视角，否则「bot 那三席没有气泡」会因为掩蔽而恒真（判据 3） |
| `TablePageTests.「在想」按席各记各的秒数`（加硬） | 同上；并新增「坐回头一席那个视角，第二席的气泡是 `None`」 |
| 其余 12 条（三态、按座位取、Live/回放、没有记录、跨局边界、`moves`…） | **一字未改，全绿** |

### 6.2 浏览器侧

`verify-bubbles`：第①②③④⑦⑧程一条未改（各自先切到上帝视角），**新增⑨（视角闸门）与⑩（一句话 + 招子）**。
`verify-home`：⑦ 由 10 条变 16 条（§3）。`verify-inbound`：那条阳性对照翻面并由 1 条变 6 条（§3.3）。

**执行次数（判据 3）**：⑨ 每跑一次执行 4 轮座位 × 3 类断言 + 1 轮上帝；⑩ 每跑一次 1 席截断 + 3 席不截断；
`verify-home` ⑦b 每跑一次 4 轮 + 1 轮回切。全部在**真语料 / 真假端点**上，没有一条是构造不出来的支路。

---

## 7. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，49.5s；引擎 745 + 页面 **205** 条；浏览器十四趟全 ✓ |
| 气泡那一道单跑 | `node scripts/verify-bubbles.mjs` | 四段各印一行 ✓（4.2s） |
| 首页那一道单跑 | `node scripts/verify-home.mjs` | 八条各印一行 ✓（1.9s） |
| 导入那一道单跑 | `node scripts/verify-inbound.mjs` | ✓（2.2s） |
| 每条新断言先红 | §4 十一次 | 全部红过，输出抄在 §4 |
| 真语料的理由长度 | `node -e`（读 `web/public/demo-paifu.json`） | §2.2 那张表 |
| 截图 | `shoot-table.mjs --home`、一次性探针、`verify-bubbles --shoot` | §5，四张都打开看过 |
| 还原干净 | `jj diff` 逐文件读 | 破坏实验用的备份全部对回 |

`jj diff --stat`：15 个文件。**没碰**：引擎、`web/src/agent/**`、`Paifu` 格式、`Route`、`Share`、
`web/public/demo-paifu.json`、`docs/adr/*`、`unlocked`、`Table.Decisions` 的形状、
`TablePanel` 的配桌表单与档案库、`TablePage` 的页面块次序、`styles.css` 里表单那一片（票 83 的地盘）、
牌桌布局（票 82 的地盘）。testId **只增了一个**（`seat-{N}-bubble-more`），没有改名或删除。

---

## 8. code-review（Standards + Spec 两轴，fixed point `53edfe6a`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓**：全程 `jj st` / `jj diff` / `jj commit`，无远端操作、无交互式 flag。
- 工具强制的 `fantomas --check` / `check-style.sh` / Biome / tsc 全绿；引擎 `let mutable` 未新增（预算仍是 2）。
- **F# 风格**（`docs/agents/fsharp-style.md`）：新代码里没有规则 1/2/3 的形状——
  `live.Agent |> List.indexed |> List.choose …`、`bySeat |> List.choose … |> List.partition (fst >> reveals)`、
  `said bubble |> fst` 都是从左往右的数据流；`Awaiting.seat >> reveals` 与 `fst >> reveals` 是规则 2 的正例。
  规则 4.1 的「谓词套取值器」保留（`Option.isSome frame.Latest`、`Option.isNone turn`）。规则 5：没有新 `let mutable`。
- **注释写「为什么」✓**：两根轴为什么正交、为什么回放终局也不放开、为什么它不是权限、
  上限 72 从哪儿量出来的、为什么「写什么」与「截没截」是一个函数、为什么状态线收谓词而不是 `TableModel`
  ——都写在代码上。
- **术语 ✓**：`reveals` / `clipped` / `hushed` 是渲染层的名字，日麻术语（`Seat` / `Turn` / `DecisionRecord`）
  一个没自造。**`CONTEXT.md` 只改了授权的那一条词条**（见 §9 第 1 条的挂账）。
- **ADR-0003 ✓**：`unlocked` 一字未动；新那一根读的是视角不是身份（用例钉着）。`docs/adr/*` 未改。
- **blocking：0。**

### Spec（票面四条行为 + 四条闸门 + 五条边界 + 术语表授权）

逐条对照见票文件的勾选框。三处值得写下来：

- **「危险度那个开关与视角正交」是既成事实，这一票没有让它们缠在一起**：`ShowDanger` 与 `Viewpoint`
  是 `TableModel` 上两个互不相干的字段，`dangerPanels` 只读 `model.ShowDanger` 与 `board.Viewer`
  （票 25 起就是按观测者算的），我一行都没动。**没有为它新立断言**——它今天没有可失败的形态。
- **全文面板也上了闸门**（票面没点名，我判它属于同一条规则；理由见 §1.2，判据在 `DECISIONS.md` 81-2）。
- **`verify-inbound` 那条阳性对照翻了面**（判据 5：期望值本身在主人的新裁决下不成立），
  强度从 1 条到 6 条，写在 §3.3。

### 记录但没改的 nitpick

1. **状态线上那句理由不截**：座位视角下 `座位 N 的模型选完了（5 ms）：<整句理由>` 会把 92 字原样铺出来
   （截图 §5.3 的页面顶部）。它从票 74 起就是这样，票面只说了气泡；真要治该与气泡共用 `Bubble.said`，
   但那要先决定状态线要不要也点得开——另一张票。
2. `web/scripts/verify-setup.mjs` 的文件头仍写着「十一趟共用一个浏览器」（现在十四趟）；
   `verify-home.mjs` 头上写着「八条断言」而⑦现在是 16 条断言（条数不是断言数，没改文案）。
   前者是票 72 的文件、后者这一票动的是正文——留给集成时顺手统一措辞。
3. `Bubble.said` 里 `clip` 是内嵌函数。抽到模块级会更好测，但它只有这一个调用点，
   而且抽出去就会诱人再写一个「截没截」的入口（正是 §2.1 要避免的）。

---

## 9. 留给人的待审项

1. **术语表那一条我只改了授权范围内的字**：`Thinking Bubble` 现在写的是「气泡放一句话理由（reason 优先，
   只有 thinking 时取头一段并三点号收尾），thinking 全文在点开后的面板里」。
   **「坐座视角只看得见那一席的气泡」这句话我没往术语表里写**——票面把授权锁死在前一件事上，
   而这是另一件事。提案记在 `DECISIONS.md` 81-4，请主人裁要不要补进那一条词条
   （不补的话，术语表里关于气泡可见性的话只剩「有真人参与时终局前隐藏」，与实际行为不完整对应）。
2. **`?table=1` 默认坐在座位 0 上**（裁决 71-8：那一页牌还在打）。于是**主持人自己开的一桌，
   默认只看得见座位 0 的气泡与状态线**——把模型坐在座位 1–3 的人第一眼会觉得「模型没说话」。
   页面上那句「另 3 席的状态被这个视角挡着（切上帝视角看全场）」是我给的补偿，
   但要不要把那一页的默认视角改成上帝，是产品口味，**没有在这一票动**（那是 71-8 的地盘）。
3. **上限 72 字是按当前那份 Demo 量的**（中位 48、p90 77）。换资产、或者以后开思考档跑长局，
   这个数应当重量一遍——它写在 `Bubble.sentenceLimit` 一处，注释里带着量它的那组数。
4. **「头一段」= 第一个换行之前那一截**。真语料里 thinking 是 0 条（那一场关着思考预算），
   因此这条支路今天只在**闸门造的固件**上跑过（dotnet 3 条 + `verify-inbound` 1 席）。
   等真有一份带 thinking 的牌谱进来，值得回头看一眼「模型的 thinking 是不是按段分行的」。
