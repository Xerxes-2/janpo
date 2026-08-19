# 票 100：把「宣言中的暗杠」那条术语裁决落进断言

**结论先说五句。**

1. **定义域写在判据的左边，不是右边**：`leaksTo` 判掩蔽流漏没漏之前，先把**还没成立的那条杠宣言**
   从流里摘掉（`ChankanFixtures.maskedWithoutUnestablishedKan`）。摘的是**那一条事件**，
   不是那几种记法——宣言一个 7z 的暗杠不会让别处漏出来的 7z 变得合法。
   `visibleTo`（引擎那侧「这家看得见的牌」）**一个字没改**。
2. **改完之后它守的局面比从前多两种**：国士抢暗杠那条轨迹票 99 时被显式排在锚点外
   （`kakanTraces`），这一票收了回来；另摊了一条**加杠加上去的那张是红宝牌**的轨迹
   （票 99 §5.4 那个同型漏），它**先红了一次**再修的判据。`kakanTraces` 连同它那段
   「本来就不成立」的旁白一起删掉了——术语裁完之后那段话是错的。
3. **「收窄定义域」与「把断言调松」在证据上分得开**，两条新锚点各守一头：
   `宣言中的那个杠：定义域放行的就是它亮出去、而引擎仍当成暗牌的那几张`（逐条轨迹写死放行了什么）
   与 `宣言窗口之外的他家暗牌，那条不变量照旧抓得住`（停在宣言那一刻塞一张宣言之外的暗牌）。
   把判据换成真·调松（实验 A）或换成「放行那几种记法」（实验 B），**这两条各当场报红一条**，
   而那条属性本身照样是绿的——这正是它们存在的理由。
4. **判据 20 在这一票上量得到**：把量的位置从「宣言中、还没结局」挪到终局（实验 E），
   四条轨迹的放行集当场全变成「（一张也没放行）」——阴性对照会退化成一张收据。
5. **`MaskedEvent.fs` / `Observation.fs` 零改动**（票面预期如此，99 已把 fold 修对）。
   `dotnet fsi scripts/fsi/chankan-trace.fsx` 那 54 条 **× 四条轨迹全绿**（改前 2 条红），
   引擎全量 **20 趟 × 759 条**全绿，`./scripts/ci.sh` 全绿。

---

## 0. 改了什么（文件一览）

| 文件 | 改动 |
| --- | --- |
| `tests/…/MaskedStreamProperties.fs` | `streamTiles` → `notationsIn` + **`leaksTo`（定义域在这里）**；那条属性改用它；两条新锚点（放行了什么 / 宣言窗口之外照旧抓得住）+ 伪造泄露的 `leaking` |
| `tests/…/ChankanFixtures.fs` | `withoutUnestablishedKan` 泛化成 `withoutUnestablishedKanBy`（事件流与掩蔽流**共用同一份判据**）+ 新出口 `maskedWithoutUnestablishedKan`；`traces` 收进第四条；新 `declarationWindows`（判据 20 的量点）；**删掉 `kakanTraces`** |
| `tests/…/GameStateGenerators.fs` | 新剧本 `chankanAkadoraScript`（碰 `5s 5s 5s`、加杠加 `5sr`） |
| `tests/…/{Kan,GameState,Riichi,Observation,DecisionPackage}Properties.fs` | 只改名字与注释里的「三条」（表已经四条，名字再说三条就是假话），**断言一字未动** |
| `scripts/fsi/chankan-trace.fsx` | 第四条轨迹进探针（打印 + 54 条 sweep + 观测漂移） |

**边界**：`src/Janpo.Engine/**`（含 `MaskedEvent.fs`、`Observation.fs`、`Shanten.fs`）、
`src/Janpo.Web/**`、`web/**`、`.github/**`、`scripts/ci*.sh`、`CONTEXT.md`、`docs/adr/*`
**一律零改动**；取值域权重表（`GameStateArbitraries.tracesFor`）一字未改——
新轨迹只挂锚点，`chankan` 那一行仍是 0。

---

## 1. 定义域现在怎么写的

```fsharp
let private leaksTo (seat: Seat) (state: GameState) (stream: MaskedEvent list) : Set<string> =
    let disclosed =
        stream |> ChankanFixtures.maskedWithoutUnestablishedKan |> notationsIn

    Set.difference disclosed (visibleTo seat state)
```

`maskedWithoutUnestablishedKan` 与 `withoutUnestablishedKan`（票 99 建的、`KanProperties` 在用的
那一个）是**同一个 `withoutUnestablishedKanBy` 的两个出口**，判据只有一份：

- 宣言之后紧接着荣和（或三家和了的途中流局）→ **原地作废**，丢掉宣言；
- 宣言就是流的最后一条 → **还挂在那一轮上**，同样丢掉；
- 其余每一条都意味着杠成立了 → 宣言留下（那几张已经进副露，`visibleTo` 里有）。

这三句就是 `CONTEXT.md` 里 `Ankan Declaration` 的「宣言的结局由**下一条事件**宣告」。

**为什么摘事件而不是加记法**：把那几种记法加进「看得见的牌」也能让属性转绿，但那样一来
「宣言中有一个 7z 的暗杠」就等于**全场的 7z 都合法**了。摘事件是紧的那一种，
实验 B 证明这两种读法在证据上分得开（§3.2）。

**词条措辞好用**，一处都没有卡住：「定义域不含宣言中的那几张」直接就是 `leaksTo` 那一行，
「宣言的结局由下一条事件宣告」直接就是 `withoutUnestablishedKanBy` 的两条 case。
唯一要补的一句在下面 §7 第 1 条（词条写「暗杠（与加杠加上去的那张）」，
而加杠那条事件在流里带的是**四张**，摘的时候摘的是整条事件）。

---

## 2. 先红（原文）

### 2.1 红-1：国士那条轨迹收进锚点（判据只改了扫描面，还没改定义域）

`MaskedStreamProperties` 里 `kakanTraces` → `traces`，别的一个字没动：

```
  失败 Janpo.Engine.Tests.MaskedStreamProperties.抢杠那个窗口：摊好牌山的轨迹逐步，掩蔽流的不变量都成立
  错误消息:
   国士抢暗杠（雀魂） 第 2 步破了「不出现他家暗牌」
失败!  - 失败:     1，通过:     6，已跳过:     0，总计:     7
```

### 2.2 红-2：摊上那条踩得到的红宝牌加杠（票 99 §5.4 的同型漏）

`chankanAkadoraScript` 进 `traces` 之后，同一条锚点多红一行：

```
  错误消息:
   国士抢暗杠（雀魂） 第 2 步破了「不出现他家暗牌」
加杠抢杠、加的那张是红宝牌（天凤） 第 7 步破了「不出现他家暗牌」
失败!  - 失败:     1，通过:   756，已跳过:     0，总计:   757
```

现场（fsi 直接量的，`Tile.toMjai` 的记法）：

```
赤宝牌加杠 第 7 步 座位 0 泄露 ["5sr"]      国士抢暗杠 第 2 步 座位 0 泄露 ["7z"]
赤宝牌加杠 第 7 步 座位 2 泄露 ["5sr"]      国士抢暗杠 第 2 步 座位 2 泄露 ["7z"]
赤宝牌加杠 第 7 步 座位 3 泄露 ["5sr"]      国士抢暗杠 第 2 步 座位 3 泄露 ["7z"]
第 7 步 座位 0 看得见的 5 索：["5s"]   流里的 5 索：["5s"; "5sr"]
座位 1 手牌：5sr 1z 2z 2z 3z 3z 5z 5z 6z 6z 7z    座位 1 副露：["5s 5s 5s"]
```

**这两条红是同一件事的两个面**：一张牌一经宣言就亮在牌桌上，而引擎那侧它仍在手里。
`chankanScript` 与真牌谱那两局不踩，只是因为它们的碰里**碰巧已经有 `5sr`**。

### 2.3 新轨迹长什么样（`chankanAkadoraScript`，与 `chankanScript` 同一座牌山，只换红的那一张）

```
== 加杠抢杠、加的那张是红宝牌（chankanAkadoraScript，默认规则集）：9 步，抢杠局面 1 个 ==
 1 等响应（座位 0 打出 5s）                  碰5s(1←0) 过(1)
 6 等打牌（座位 1）                          加杠5sr(1) 打1z(手切,1) … 打5sr(摸切,1)
 7 **抢杠那一轮**（座位 1 宣言加杠 5sr）      和5sr(2←1) 过(2)
 8 终局
和了：座位 2 ← 座位 1，5sr，2 番 40 符 2600 点
```

（抢杠 1 番 + 红宝牌 1 番 = 2 番 40 符 2600 点，引擎算的。`chankanScript` 那条是 1 番 1300 点，
差的正好是加上去那张红 5。）

### 2.4 再绿

`leaksTo` 落地之后：**759 条全绿**（757 + 两条新锚点），四条轨迹逐步都过。

---

## 3. 「收窄定义域 ≠ 把断言调松」的自证

**这一节是这一票的要害。** 判据改完之后那条属性是绿的——而**把闸门拆了它也是绿的**。
因此两条新锚点各守一头，逐个把判据弄坏验它们真的会开口。

### 3.1 实验 A：真·调松（宣言那一刻，宣言者整手牌都当成公开的）

```fsharp
// 把 leaksTo 换成：Set.difference (notationsIn stream) (visibleTo seat state ∪ 宣言者的整手牌)
```

```
  错误消息:
   加杠抢杠（天凤），座位 0 看座位 1 宣言的那个杠：漏了一张 5z（宣言之外的暗牌），而那条不变量没抓住它
失败!  - 失败:     1，通过:   758，已跳过:     0，总计:   759
```

**注意那 758**：`不出现他家暗牌` 那条属性、四条轨迹的 sweep、放行集那条锚点**全是绿的**。
调松与收窄在它们眼里一模一样，**只有阴性对照那一条分得开**。

### 3.2 实验 B：放行那几种**记法**，而不是宣言那一条**事件**

```fsharp
// 把 leaksTo 换成：Set.difference (notationsIn stream) (visibleTo seat state ∪ 宣言那个杠的四张记法)
```

```
  错误消息:
   国士抢暗杠（雀魂），座位 0 看座位 1 宣言的那个杠：7z 换一条事件漏出来就不算漏了——放行的应当只有宣言那一条
失败!  - 失败:     1，通过:   758，已跳过:     0，总计:   759
```

同样是 758 绿。**这一条量的是「宣言中的暗杠」这句话的边界**：亮出去的是那一条宣言，
不是那种牌——别家摸的 7z 照样看不见。

### 3.3 实验 C：把红宝牌那条轨迹撤掉（放行集是不是空话）

```
  错误消息:
   Assert.Equal() Failure: Strings differ
Expected: ···"抢的那家先立直（天凤）：（一张也没放行）\n加杠抢杠、加的那张是红宝牌（天凤）：5s"···
Actual:   ···"行）\n国士抢暗杠（雀魂）：7z\n加杠抢杠、抢的那家先立直（天凤）：（一张也没放行）"
失败!  - 失败:     1，通过:     8，已跳过:     0，总计:     9
```

**放行集那条锚点写死了逐条轨迹放行了什么**，因此「哪条轨迹在给这条定义域喂料」是可查的事实：

```
加杠抢杠（天凤）：（一张也没放行）
国士抢暗杠（雀魂）：7z
加杠抢杠、抢的那家先立直（天凤）：（一张也没放行）
加杠抢杠、加的那张是红宝牌（天凤）：5sr
```

**两条加杠轨迹一张也没放行**——这就是票 99 §5.4 那个同型漏躲过去的方式，
也是这一票非摊一条新轨迹不可的理由（判据 3：闸门要报执行次数，为 0 的当场喊停）。

### 3.4 实验 D：把 `MaskedEvent.forSeat` 弄坏（他家摸的那张也带牌面）

改完定义域之后那条属性还咬不咬得住真泄露：

```
[FAIL] 宣言窗口之外的他家暗牌，那条不变量照旧抓得住
[FAIL] 抢杠那个窗口：摊好牌山的轨迹逐步，掩蔽流的不变量都成立
[FAIL] 任意局面任意座位，掩蔽流里不出现他家暗牌中的任何一张      ← 随机取值域那一路
[FAIL] 任意局面任意座位，只有自家那几条摸牌带着牌面
失败!  - 失败:     7，通过:   752，总计:   759
```

探针那一侧同一次破坏（**注意宣言那一步本身也红**：定义域放行了那条宣言，没放行别的）：

```
== 国士抢暗杠（雀魂）：54 条属性 × 4 步 ==     == 加杠抢杠、加的那张是红宝牌：54 条属性 × 9 步 ==
Masked/不出现他家暗牌   1  false               Masked/不出现他家暗牌   0  false
Masked/不出现他家暗牌   2  true ←宣言那一步     Masked/不出现他家暗牌   3  false
                                              Masked/不出现他家暗牌   6  false
                                              Masked/不出现他家暗牌   7  true ←宣言那一步
```

### 3.5 实验 E（判据 20）：把量的位置从「宣言中」挪到终局

`declarationWindows` 改成 yield 终局局面，别的照旧：

```
  错误消息:
   Assert.Equal() Failure: Strings differ
Expected: ···"：（一张也没放行）\n国士抢暗杠（雀魂）：7z\n加杠抢杠、抢的那家先立直（天凤）：（"···
Actual:   ···"：（一张也没放行）\n国士抢暗杠（雀魂）：（一张也没放行）\n加杠抢杠、抢的那家先立直"···
```

**四条轨迹的放行集全变成「一张也没放行」**——因为到终局那个杠要么成立了（那几张进了副露）、
要么被抢了（被抢的那张写在 `hora` 事件上），**两种结局都公开**。
拿终局量出来的阴性对照会全程绿着，而它什么也没验。这与票 87 红-4 / 88 红-3 / 89-6 同族，
只是这一次踩在引擎侧而不是浏览器侧。

---

## 4. 探针那 54 条：改前改后

`dotnet fsi scripts/fsi/chankan-trace.fsx` 的第四节（全部吃 `GameState` 的 54 条属性 × 每一步）。

**改前**（票 99 落地后的原样，本票复现逐字相同；那时只有三条轨迹）：

```
== 加杠抢杠：54 条属性 × 9 步 ==            全绿
== 国士抢暗杠（雀魂）：54 条属性 × 4 步 ==   Masked/不出现他家暗牌  第 2 步  抢杠局面 true
== 加杠抢杠（抢的那家先立直）：54 条属性 × 10 步 ==  全绿
```

**摊上第四条轨迹、判据还没改**（本票中途，实验 F：把 `leaksTo` 退回票 99 那一版）：

```
== 加杠抢杠：54 条属性 × 9 步 ==                    全绿
== 国士抢暗杠（雀魂）：54 条属性 × 4 步 ==            Masked/不出现他家暗牌  第 2 步  true
== 加杠抢杠、加的那张是红宝牌：54 条属性 × 9 步 ==     Masked/不出现他家暗牌  第 7 步  true
```

**改后**（本票落地之后原样，四条轨迹）：

```
== 加杠抢杠：54 条属性 × 9 步 ==                    全绿
== 国士抢暗杠（雀魂）：54 条属性 × 4 步 ==            全绿
== 加杠抢杠、加的那张是红宝牌：54 条属性 × 9 步 ==     全绿
== 加杠抢杠（抢的那家先立直）：54 条属性 × 10 步 ==    全绿
四条轨迹的「观测 vs 引擎的权威状态」：逐步、逐座位、逐字段全对得上
```

**票 98 那 37 条红到今天清零**：99 收了 36 条，这一票收掉最后一条术语项，
并在同一次里把第四条轨迹的那 1 条一并收掉。

---

## 5. 闸门

| 闸门 | 结果 |
| --- | --- |
| 每条改动先红后绿 | 红的原文 §2.1 / §2.2；改后 759 条全绿 |
| 新锚点反向自证（判据 1） | **五个实验**（§3.1–§3.5）各按红它该守的那一条 |
| 阴性对照停在「宣言中、还没结局」（判据 20） | `ChankanFixtures.declarationWindows`；挪走就空转，§3.5 |
| 定义域非空转（判据 3） | 放行集逐条轨迹写死：`7z` / `5sr` / 两条「一张也没放行」，§3.3 |
| 探针 54 条改前改后 | §4：2 → 0（四条轨迹全绿） |
| 引擎全量连跑 20 趟 | **20 趟 × 759 条全绿，0 红**（每趟 8–11 s，FsCheck 自换种子） |
| `dotnet fantomas .` | 186 个文件，写盘那一遍 Formatted 0 / Unchanged 186；`--check` 干净 |
| `scripts/check-style.sh` | 通过（`let mutable` 预算未动，新增 0 处） |
| `./scripts/ci.sh` | **全绿**（含 Fable + 浏览器侧那一路） |

20 趟原样：

```
run 01..20: 已通过! - 失败: 0，通过: 759，已跳过: 0，总计: 759，持续时间 8–11 s
```

---

## 6. code-review（Standards + Spec 两轴，fixed point `uqlmotul` / `fe7856ea`）

派不出 sub-agent，两轴顺序自跑（`docs/agents/workbook.md` 允许）。
diff：9 个代码文件（+265 / −47 行），**全部在 `tests/**` 与 `scripts/fsi/**` 之内**。

### Standards（`docs/agents/fsharp-style.md` + Fowler 味道基线）

- **规则 1 / 2 / 3**：新代码里没有 `f (g (h x))` 形状。`leaksTo` 是
  `stream |> maskedWithoutUnestablishedKan |> notationsIn` 一条管道；
  属性体是 `Observation.stream seat state |> leaksTo seat state |> Set.isEmpty`；
  `Set.contains (Tile.toMjai pai) caught`、`visibleTo viewer state` 属规则 4 第 1 条
  （谓词套取值器，两层，不强行管道）。
- **规则 3 的 `match` 穷举**：`ResponseCause` / `option` / list 上的 match 逐个 case 写全。
  `withoutUnestablishedKanBy` 里两处 `| _ -> false` 是**逐字继承**它替换掉的那段既有代码
  （判的是「这条事件是不是那一类」，与 `minogashiOn`、`nakiOf` 同形）。
- **规则 5（`let mutable`）**：新增 0 处，风格闸门预算未动。
- **规则 6（`.fsx` 同受约束）**：探针那几处改动是加轨迹与打印，没有新的命令式累加。
- **术语**：`Akadora`（`CONTEXT.md` 的罗马字）用在标识符 `chankanAkadoraScript` 上，
  中文一律「红宝牌」（词条的译名，不是票面那个「赤宝牌」）；
  `declarationWindows` / `maskedWithoutUnestablishedKan` 对着的是词条 `Ankan Declaration`。
- **味道（判断题，逐条记录）**：
  1. **Duplicated Code（已消）**：掩蔽流那一侧本可以再抄一遍那个 `loop`——
     改成 `withoutUnestablishedKanBy` 的两个出口，**判据只有一份**（两侧同飘或同不飘）。
  2. **Data Clumps（记录，未改）**：`viewsAtDeclaration` 返回 5 元组
     `(label, declarer, declared, viewer, state)`，`List.map` 那一处出现 `_, _`。
     **没有抽成记录**：这一族固件里同形的东西全是元组（`traces` 是 3 元组、
     `sweep` 的判据表是 2 元组、`declarationWindows` 是 3 元组），
     为一个私有的两处调用单开一个记录会与邻居不一致。留给人裁（§7 第 3 条）。
  3. **Speculative Generality（轻）**：`ChankanFixtures.declarationWindows` 收 `traces` 参数
     而只有一个调用点传 `traces`——与紧邻的 `sweep` 同形（它也收 traces），不是为将来准备的。
  4. **Shotgun Surgery（表象，非本票造成）**：五个属性文件各改一行名字。
     那是「表从三条变四条」的连带，断言一字未动。

### Spec（票 `.scratch/llm-riichi-arena/issues/100-masked-ankan-domain.md`）

- **要什么行为四条**：①判据改成「他家暗牌减去宣言中的那个暗杠 / 加杠那张」→ §1；
  ②国士那条轨迹收进锚点 → `traces` 四条全扫，`kakanTraces` 连同旁白删除；
  ③同型漏 → `chankanAkadoraScript`，**先红**（§2.2）后修；
  ④`MaskedEvent.fs` / `Observation.fs` 零改动 → **确认零改动**（`jj st` 里没有 `src/`）。
- **闸门五条**：先红后绿（§2）、判据 20（§3.5）、收窄自证（§3.1–§3.2）、
  探针 54 条全绿（§4）、20 趟 + `ci.sh`（§5）。
- **边界**：`src/Janpo.Web/**`、`web/**`、`.github/**`、`CONTEXT.md`、`docs/adr/*`、
  `Shanten.fs` 零改动；**没有放宽任何别的断言**——改的只有「本来就不管的那一段」，
  且同一次把守的局面从三条轨迹扩到四条。
- **超出票面的两处（都为交付物服务）**：
  ①五个属性文件的测试名从「三条摊好牌山的轨迹」改成「摊好牌山的那几条轨迹」——
  表已经四条，名字再说三条就是**记录声称了一件不存在的事**（判据 2 的同族）；
  ②探针加了第四条轨迹的打印与 sweep（票面把 `scripts/fsi/**` 划进地盘，
  且「54 条全绿」这个验收要它扫得到新轨迹才说得出口）。
- **与票面的一处措辞偏差**：票面写「赤宝牌」，代码与本报告一律用 `CONTEXT.md` 的译名
  **红宝牌**（`Akadora（红宝牌）`）。

---

## 7. 留给人的待审项

1. **词条要不要补半句**：`Ankan Declaration` 写的是「暗杠（与加杠加上去的那张）」，
   而掩蔽流里那条 `kakan` 事件带的是**四张**（加上去的那张 + 底下那组碰的三张）。
   本票的判据摘的是**整条宣言事件**——底下那三张本来就在副露里看得见，摘不摘都一样，
   因此行为上没有差别。**要不要在词条里点明「那条事件整条落在定义域外」，请主人裁。**
   （我没改 `CONTEXT.md`。）
2. **`chankanAkadoraScript` 要不要挂进取值域表**：与票 98 的账一样，这是权重账不是正确性账，
   本票只挂锚点（每趟 100%）。`chankan` 那一行仍是 0。
3. **`viewsAtDeclaration` 那个 5 元组**（Standards 味道 2）：现在与邻居一致，
   若往后再多一个消费者，就该抽成记录（同 `RobbedKan` / `Divergence` 的形状）。
4. **牌桌那一层仍未画「宣言中的杠」**（票 99 §10 第 4 条原样留着）：术语裁完之后
   「谁在宣言杠」明确是**读掩蔽流里那条事件**的事，`Observation` 不该为它加字段
   （词条的 _Avoid_ 写着）。本票不碰 `src/Janpo.Web/**`。
