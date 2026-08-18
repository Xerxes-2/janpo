# 票 96：那条立直属性稀稀落落地红 —— 判决与凭据

**判决先说三句。**

1. **反例是真的，局面完全可达，但根因不在引擎，在属性自己那一行断言。**
   `PlayerState.isTenpai`（向听 **= 0**）自己写明只接 3n+1 的手牌；属性拿它去问
   **立直中的家刚摸进和了牌那一手的 14 张**——那 14 张已成和了型、向听是 **−1**，
   于是返回 false。那不是「它不听牌」，是**问错了牌**。引擎一行没改。
2. **`Ruleset` 根本不是生成出来的**：`GameStateArbitraries.GameState()` 全程用
   `Ruleset.yonma`。票 95 §9.1 记的那几处「非默认位」（`RiichiAnkanMentsuUnchanged = false`、
   `Atamahane = false`、`SanchaHoraRyuukyoku = true`、`DoubleKazeJantouFu = 4`、
   `RinshanTsumoFu = true`）**逐位就是 `Ruleset.yonma` 的默认值**（`Ruleset.fs:119-128`）。
   「不可达的 ruleset 组合」这条路从前提上就不存在，**不必收窄生成器**。
3. 修法：把断言问的那几张改对（见 §3），并**加一条定点锚点**——固定牌山的
   `[<Fact>]`，每趟都跑到那一手（判据 3）。随机属性一条没删、`MaxTest` 没动、没加 `Skip`。

**这条红以后再出现该怎么办**：先看是不是 `RiichiProperties` 那一族。这一族现在有定点锚点
`立直中的家摸进和了牌那一手：不变量仍然成立`；**锚点绿而随机属性红 = 撞上了新的一类局面，
按真反例查**，别当噪声重跑。

---

## 1. 定点复现（修之前先红）

票面的三元组挂上去就复现，**第 1 个样本就红**：

```
[xUnit.net 00:00:00.99]     Janpo.Engine.Tests.RiichiProperties.立直中的家永远听牌，且它的手牌自立直起不再变 [FAIL]
  失败 Janpo.Engine.Tests.RiichiProperties.立直中的家永远听牌，且它的手牌自立直起不再变 [4 ms]
  错误消息:
Falsifiable, after 1 test (0 shrinks) (8735905781990260625,1276793955506188385).
Last step was invoked with size of 18 and seed of (8735905781990260625,1276793955506188385):
Original:
{ Ruleset = { SeatCount = 4 …
---- System.Exception : Expected true, got false.
失败!  - 失败:     1，通过:     6，已跳过:     0，总计:     7，持续时间: 835 ms
```

挂法：给那条属性临时写成
`[<Property(Replay = "(8735905781990260625,1276793955506188385,18)")>]`。
FsCheck 打的 `Original` 是整个 `GameState` 的 500 行 record dump，看不出所以然；
给属性体临时插一句写文件的诊断，反例的形状是：

```
DIAG phase=AwaitingDahai actor={ Index = 1 } seat={ Index = 1 } riichi=立直
     hand=[1p 2p 3p 8p 8p 1s 2s 3s 6s 6s 7s 7s 8s 8s] naki=0 held=14
     isTenpai=false shanten=-1
```

**一眼就清楚**：座位 1 立直成立、刚摸进第 14 张，`123p + 88p + 123s + 678s + 678s`
是一副已经和了的手（下一步它就宣言自摸）。向听 **−1**，`isTenpai`（向听 = 0）说 false。

`0 shrinks` 不是巧合：`GameStateArbitraries` 用 `Arb.fromGen` 建的，**没有 shrinker**。

## 2. 这一手在规则上到不了吗？—— 到得了，而且能指名道姓

用 `dotnet fsi` 直调编译好的引擎与固件（`GameStateFixtures` 在测试 DLL 里，`#r` 进来即可），
把 `GameStateArbitraries.GameState()` 的**整个取值域**（400 颗种子 × 10 条轨迹）逐步扫一遍：

| | |
| --- | --- |
| 扫到的局面 | **217,574**（含五条与种子无关的固定轨迹按 400 次权重计） |
| 其中「有家立直中」 | **104** |
| 旧断言的反例 | **2**（`seed=288` 的 `riichiSeeking` 轨迹，第 22、23 步） |
| 反例那一手 | `1p 2p 3p 8p 8p 1s 2s 3s 6s 6s 7s 7s 8s 8s` —— **与 FsCheck 打出来的逐张相同** |
| 耗时 | 3.3 秒（单线程） |

第 22 步是「摸进和了牌、还没宣言」，第 23 步是宣言之后的 `Ended`——**同一手牌被数了两次**。

**这就是那条属性的稀有度**（可以算出来，不必靠感觉）：一个样本落在这两步上的概率
p = **3.2 × 10⁻⁵**，一趟属性（100 个样本）报红的概率 **0.32%**，即约 **1/312 趟**。
票 95 说的「比 1/7 还稀」方向对，量级还要小一位。

## 3. 修了什么

**只动 `tests/Janpo.Engine.Tests/RiichiProperties.fs` 一个文件**（引擎 `src/**` 零改动）。
断言拆成三段，问的牌由**手牌形态 + 立直进到哪一步**决定：

| 局面 | 问哪几张 | 判据 |
| --- | --- | --- |
| 等摸形（3n+1） | 手上这几张 | 向听 **= 0** |
| 刚摸完（3n+2）且**立直已成立** | **去掉刚摸进那张**之后的 13 张 | 向听 **= 0** |
| 刚摸完（3n+2）而**宣言牌还没打出去** | 手上那 14 张 | 向听 **≤ 0** |

三段各有各的理由：

- 第二段**比原来更硬**。原来拿 14 张问「向听 = 0」只是说「**存在**一张打了还听牌」；
  立直成立之后只能摸切，**能打的就只有刚摸进那一张**，所以要指名道姓地问它。
  顺带它就不会把「摸进和了牌」误判成不听。
- 第三段**必须是 `≤ 0` 不是 `= 0`**：宣言牌可以手切，手牌那一刻还没冻住；而且
  **已成和了型时放弃自摸宣立直是合法的**（`RiichiState.canDeclare` 走的就是 `≤ 0`，
  票 64 有具名反例）。写成 `= 0` 会红在另一类局面上——**这不是猜的**：
  第一版补丁只改了第二段，全域扫描当场打出 **4 个新反例**
  （`seed=13/119/167/288` 的宣言那一手，例如 `2m3m4m 5m5m5m 8p9p 2s3s4s 4z 6z6z` 摸 `3m`
  ——它要手切 `4z` 才听 `7p`）。**先扫全域再定稿，省下一次「修完又红」。**
- 摸进那张**记不着**（`drawn = None` 而手牌是 3n+2）时整条判红：立直成立之后多出来的
  那一张只可能是自摸或岭上摸来的。

`held`（暗牌 + 3 × 副露数 ∈ {13, 14}）那一半一个字没改。

**定稿后再扫一遍全域：217,574 个局面，新断言反例 0。** 这条属性的取值域是有限的，
因此「零反例」不是采样结论，是**穷尽结论**。

## 4. 回归锚点（固定牌山，不是固定种子里碰运气）

```fsharp
let private riichiTsumoStates =
    traceFrom riichiSeeking (Rng.ofSeed 1) (startScripted tsumoHoraScript)

[<Fact>]
let ``立直中的家摸进和了牌那一手：不变量仍然成立`` () = …
```

`tsumoHoraScript` 的牌山 + 见立直就立直的选手 = 7 步的一局：Oya 听 `5z` 单骑，
第 1 巡摸 `1z` 立直摸切，第 2 巡摸进 `5z`。逐步实测：

| 步 | 阶段 | 座位 0 的手牌 | 旧断言 |
| --- | --- | --- | --- |
| 1 | AwaitingDahai(0) | `1m…9m 1p2p3p 1z 5z`（宣言那一手，14 张） | True |
| 2–4 | 三家各摸切一张 | `1m…9m 1p2p3p 5z`（13 张） | True |
| **5** | AwaitingDahai(0) | `1m…9m 1p2p3p 5z5z`（**14 张的和了型**） | **False** |
| **6** | Ended | 同上 | **False** |

用旧断言跑这条锚点，当场红：

```
失败 Janpo.Engine.Tests.RiichiProperties.立直中的家摸进和了牌那一手：不变量仍然成立
  错误消息:  第 5 步的局面破了立直的不变量
```

修完绿。锚点里还有一条**覆盖自证**（判据 3）：先 `Assert.NotEmpty` 那条轨迹里
「立直中且手牌已成和了型」的局面，**空了就红**——否则这条锚点会悄悄退化成空转。

它比「固定种子」更强的地方：种子锚点绑在 `GameStateArbitraries` 的实现细节上，
生成器一改就飘；这条锚点绑的是一座**写死的牌山**。

## 5. 闸门与耗时

| 闸门 | 结果 |
| --- | --- |
| 定点复现（`Replay` 三元组） | **先红**（§1），修完再挂同一个三元组跑 —— **绿**，8/8 |
| 那一族连跑 20 趟（`--filter FullyQualifiedName~RiichiProperties`，每趟换种子） | **20 绿 0 红，总耗时 33 秒**（约 1.7 秒/趟，每趟 8 条） |
| 全域穷尽扫描（fsi 探针） | 217,574 个局面、新断言 0 反例，3.3 秒 |
| 引擎全量 | **749 条全绿**（改前 748，本票 +1） |
| `dotnet fantomas .` | 172 个文件 Unchanged |
| `./scripts/ci.sh` | **全绿**（引擎 749 + 页面 228 + JS 侧二十道 + 浏览器闸门） |

**20 趟本身证据很弱，要说清楚**：一趟 100 个样本，而一个样本碰到「有家立直中」的概率只有
0.103%，20 趟合计 2000 个样本、期望只有 **2 个**样本真的执行到这几条断言。
**真正的凭据是穷尽扫描与那条定点锚点**，20 趟只是没有反证。

## 6. 裸奔清单（只列，不改）

票面问的是「依赖生成的 ruleset 又没有回归锚点」的成员。**前半个前提不成立**（§结论 2），
所以按真正的判据重列：**执行次数少到接近空转、且没有定点锚点**。

数字来自同一次全域扫描（10 条轨迹的逐条统计）：

| 轨迹 | 局面数 | 其中「有家立直中」 |
| --- | --- | --- |
| `riichiSeeking` | 34,597 | **104** |
| random / tenpaiSeeking / nakiSeeking / kanSeeking | 143,777 | **0** |
| 五条固定轨迹（threeKan / minkan / ankan / tsumoHora / doubleRon） | 39,200 | **0** |

于是：

1. **`RiichiProperties` 整族一趟只有约 10% 的概率开口**（单个样本 0.103%，100 个样本
   1 − 0.99897¹⁰⁰ ≈ 9.8%）。也就是说 **十趟 CI 有九趟这五条断言是空转的**（判据 3）。
   这一族的实际闸门是 `RiichiTests` 的具名用例。**建议**（票号由调度器分配，判据 17）：
   往 `GameStateArbitraries.tracesFor` 里补一条**摊好牌山的立直轨迹**
   （`RiichiTests` 里现成的 `ippatsuTsumoScript` / `minogashiScript` 就够），
   像 `kanTrace` 当年给杠做的那样。**本票没改**：那张表是全部 `GameState` 族属性共用的，
   改它会动到 `GameStateProperties` / `ObservationProperties` / `RyuukyokuProperties`
   等十来个模块的采样分布，不该在一张「钉反例」的票里顺手做。
2. **「立直 + 副露（暗杠）」在这个取值域里到不了**：`riichiSeeking` 从不杠、`kanSeeking`
   从不立直，所以 `stillTenpai` 里 `nakiCount > 0` 那一支**随机属性一次都执行不到**，
   靠的是 `KanTests` 里 `riichiAnkanScript` 的具名用例。**已按判据 4 写进模块头注释**，
   不留在报告里当旁白。
3. 同族另外四条（供托守恒、`reach`/`reach_accepted` 配对、一发、立直后只剩摸切）
   受同一条 0.103% 的稀释，**但它们都有具名用例在 `RiichiTests` 里逐条守着**，
   而且它们问的是事件流与动作集，不问手牌形态，**不会踩到本票这个坑**。
4. 同族的 `[<Fact>] 立直的轨迹里确实立起了直：不变量不是空转` 是**空转自证**，
   不是不变量锚点：它只证明 40 颗种子里有立直发生，不执行那五条断言中的任何一条。

## 7. code-review（Standards + Spec 两轴，fixed point `xkkwryoq` / `aa245e50`）

派不出 sub-agent，两轴顺序自跑（`docs/agents/workbook.md` 允许）。
diff 只有一个文件：`tests/Janpo.Engine.Tests/RiichiProperties.fs`（+89 / −4）。

**Standards**（`docs/agents/fsharp-style.md` + Fowler 味道基线）

- 规则 1（不许从里往外读）：第一版写了 `Shanten.value (Shanten.calculate kindSet shape)`
  ——与规则 1 的 canonical 坏例同形。**已改**成 `shape |> Shanten.calculate kindSet |> Shanten.value`
  并抽成 `shantenValue`。（`PlayerState.fs:114` 与 `RiichiState.fs` 里各有一处同形的旧代码，
  **不是本票的地盘，没动**。）
- 规则 4.1（两层谓词套取值器不必强行管道）：`RiichiState.isActive (PlayerState.riichi player)`
  照原样保留 —— 规则明文点名这一处不该报。
- 规则 5（`let mutable`）：本票零新增。
- 味道基线：`riichiHandsIntact` 与 `riichiHandsOf` 各自写了一遍
  「players |> filter isActive」——**Duplicated Code，判断题**。没抽：一个要座位号一个不要，
  抽出来得先造一个 `(Seat * PlayerState) list`，比重复那一行更绕。
- 术语：`stillTenpai` / `riichiTsumoStates` 用的是 `CONTEXT.md` 的罗马字（Tenpai / Tsumo / Riichi）。

**Spec**（票 `.scratch/llm-riichi-arena/issues/96-riichi-tenpai-flake.md`）

- 三条「要什么行为」逐条对上：定点复现（§1）、判决明写（结论 1，选的是**真反例**
  那一支，只是根因落在断言而不是引擎 —— 票面预设的另一支「不可达局面」在前提上就不成立，
  §结论 2 有逐位对照）、裸奔清单（§6，只列不改）。
- 三条闸门逐条对上：修前先红（§1、§4 两份红色原文）、修后 20 趟（§5）、`ci.sh` 全绿（§5）。
- 边界：`src/Janpo.Web/**`、`web/**`、`scripts/ci.sh`、`web/package.json` 零改动；
  `src/Janpo.Engine/**` **零改动**（含 `Shanten.fs` —— 主人那份未提交的改动没被碰到）。
- 未做（有意）：没收窄生成器（§结论 2 说明前提不成立）、没往 `tracesFor` 补立直轨迹
  （§6.1 说明为什么留给单独一票）、没重写这一族属性的语义。
- 潜在 scope creep 自查：模块头新增的两段注释（采样稀疏度、立直+暗杠不可达）不是票面要求，
  但判据 3 / 4 要求把「执行几次」与「谁也到不了」写在代码里而不是报告里，**留下**。

**两轴合计**：Standards 1 项已修（规则 1）+ 1 项判断题（记录不改）；Spec 0 项缺失、0 项越界。

## 8. 留给人的待审项

1. §6.1 的建议要不要立一票：往 `GameStateArbitraries.tracesFor` 补一条摊好的立直轨迹，
   让这一族从「十趟有九趟空转」变成每趟都开口。
2. 票 95 §9.1 那句「ruleset 本身也是生成出来的」是误读（那是 `Ruleset.yonma` 的默认值）。
   报告是历史文件，**本票没去改它**；这里留一条更正，`DECISIONS.md` 里也留了一条。
