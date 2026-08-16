# F# 风格约定

**这份文档从本仓库既有代码提取，不是通用 F# 建议。** 每条规则都附了实测数字与真实位置，
`code-review` 的 Standards 轴照它检查。规则的用意是消除**从里往外读**的表达式，
不是把管道当装饰。

## 规则 1（核心）：嵌套应用不许写成从里往外读

Python 的形状是 `sum(map(f, filter(p, xs)))`——要从最内层开始读。F# 有管道与函数组合，
不必这样写。这条是本文档存在的理由。

**canonical 例子**（`scripts/fsi/shanten-probe.fsx`，调度器写坏、用户改对）：

```fsharp
// 坏：命令式累加 + 从里往外读的 Shanten.value (Shanten.calculate ...)
let mutable acc = 0
for h in hands do
    acc <- acc + Shanten.value (Shanten.calculate kindSet h)

// 好：一条数据流，从左往右读
let acc = hands |> Array.sumBy (Shanten.calculate kindSet >> Shanten.value)
```

**注意「好」的版本不是把 `for` 换成 `Seq.map`。** 实测 `Seq` 管道与 `Array.sumBy` 无性能差异
（每元素几纳秒 vs 每手 6µs，差三个数量级），所以选择依据是**可读性**：`>>` 让两个变换成为一个，
读者不必在括号里跳。

## 规则 2：`fun x -> f (g x)` 写成 `g >> f`

lambda 包着一层调用，等于手写函数组合。现存 3 处同一形状（`Tile.fs:237`、`Kaze.fs:51`、
`Event.fs:167`）：

```fsharp
// 坏
let encoder: Encoder<Tile> = fun tile -> Encode.string (toMjai tile)
// 好
let encoder: Encoder<Tile> = toMjai >> Encode.string
```

**例外**：lambda 里捕获了外部变量、或参数不在最内层时，组合写不出来，保留 lambda。
例如 `fun hand -> Tile.sort (drawn :: hand)`（`KyokuStart.fs:90`）——`drawn` 是捕获的，
硬凑 `>>` 会更难读。

## 规则 3：三层以上的变换嵌套必须拆开

实测 66 处深度 ≥3，其中多数是「管道里的 lambda 体」，不算坏。真正要改的是**变换链被写成嵌套**。
注意：下面列出的位置是**眼看了 66 处里的前几处**得出的，不是穷尽清单——`AgariShape.fs:38`
就是用户读代码时另外发现的一处。要穷尽得逐处判断，检测器只能缩小范围。

已修的真实例（`AgariShape.fs:38`，用户手改）：

```fsharp
// 坏：三层，要从最内层 classify 读起
not (List.isEmpty (classify kindSet hand))
// 好：从左往右，一步一步
classify kindSet hand |> List.isEmpty |> not
```

```fsharp
// 已修（RiichiState.fs:121）：与上例同形
&& (tenpaiDahai kindSet (List.length naki) hand |> List.isEmpty |> not)
```

拆的手段三选一：管道、`>>`、给中间值起个有意义的名字。但下面两条**限制比手段更重要**。

### 限制 A：构造子嵌套不算深度，只有变换链算

```fsharp
// 这样就够了（KyokuStart.fs:75 的实际改法）
Error(NoDoraIndicator(dealt |> Wall.deadWall |> List.length))

// 不要为了「消灭括号」写成这样
dealt |> Wall.deadWall |> List.length |> NoDoraIndicator |> Error
```

`Error(Foo(x))` 是 ADT 构造的常规形状，谁都读得懂，而且同一个 `match` 里的邻居都写
`Error(LiveWallTooSmall(required, Wall.remaining wall))`——把其中一行改成 `... |> Error`
破坏的局部一致性比嵌套本身更伤。**只管道化真正的变换部分。**

### 限制 B：不许用命名中间值破坏 `&&` / `||` 的短路

这条是本文档第一版的错误，改这两处时才发现：

```fsharp
// 本文档第一版建议的写法——错的
let dahaiKeepingTenpai = tenpaiDahai kindSet (List.length naki) hand
List.forall Naki.isConcealed naki && ... && not (List.isEmpty dahaiKeepingTenpai)
```

`tenpaiDahai` 要对 14 张候选打牌各跑一次 `Shanten`（约 84µs）。原本非门清的手牌在第一个子句
就被 `&&` 短路掉；抽成 `let` 之后**每次都算**。那是性能回归，不是风格改进。

**布尔链里一律用管道或保持原样，不许抽 `let`**，除非你确认那个值无论如何都要算。

## 规则 4：以下三种情况**不许**强行管道

这条是防 cargo cult 的。`code-review` 不该因为这些报问题。

1. **两层的「谓词套取值器」**：`Option.isNone (PlayerState.drawn player)`、
   `RiichiState.isActive (PlayerState.riichi player)`。F# 里读得顺，改成
   `player |> PlayerState.drawn |> Option.isNone` 更啰嗦。**boolean 条件里保持两层是可以的。**
2. **算术与分支**：`Fu.fs` 183 行只有 3 个 `|>`，因为它算的是数不是变换链——
   `baseFu * (if concealed then 2 else 1) * (if yaochuu then 2 else 1)` 不该有管道。
3. **类型定义为主的文件**：`Action.fs`、`Furiten.fs`、`GameLength.fs` 管道数为 0，正确。

管道密度不是指标。引擎实测 4.9 个 `|>`/100 行，这个数字本身不说明任何事。

## 规则 5：命令式代码的允许边界

`let mutable` 现存 7 处、真循环 18 处，**全部集中在同一件事上**：34 长计数数组
（`Shanten.fs` / `HandShape.fs` / `TileKindSet.fs` / `Ukeire.fs` / `MentsuBreakdown.fs`）
与 `Rng.fs` 的原地洗牌。

允许，条件有两条：

- **必须包在纯接口后面**（03 票的原话：允许「丑但快」，但外面看不见）
- **必须注明性能理由**。34 元素的直方图用 `Array.fold` 会分配，循环不会——这个理由要写在代码里

**不满足这两条的命令式代码按坏味道处理。** 新增 `let mutable` 要在 `DECISIONS.md` 留一条，
并且 `scripts/check-style.sh` 的预算会挡住你——改预算这个动作本身就是让你停下来想一想。

**判断某处 `mutable` 值不值，看它省下的开销有没有被紧邻的代码吃掉。** 真实例子：
`Shanten.fs` 的 `deadQuadKinds` 原本用可变累加器数「死张种数」，但它每次循环都要调 `canJoinRun`，
而后者每次分配一个小 list——省下的那点开销早被吃光。改成 `Seq.filter |> Seq.length` 后实测
11.83 µs → 12.00 µs，而**同版本三次跑的噪声带是 11.66–12.16 µs**，差异在带内，即无可测差异。
（另一处 `searchStandard` 的 `let mutable best` 留着：它是递归搜索里跨调用累积的，改成纯的要换算法形状。）

## 规则 6：`.fsx` 脚本同样受这些约束

这条为调度器自己写。`scripts/fsi/` 下的探针一无类型约束、二是一次性的，
所以最容易退回命令式手写惯性——事实上规则 1 的坏例子就出自那里。
`fantomas --check` 覆盖 `.fsx`，风格规则同样覆盖。

## 为什么会出现这些（给写代码的 agent 看）

引擎受一堆约束（`Result<_, 具名 DU>` 让错误必须 `match`、DU 建模让分支必须穷尽、
一概念一文件 + 段落注释给每个文件定了形），这些约束**顺带**把代码推向函数式。
所以引擎里的 Python 味比预期少——真正的坏例子出在**没有约束的一次性脚本**里。

规律：**约束写下来就守得住，没写下来就走训练分布里最常见的那条路。**
这份文档就是把它写下来。

## 度量基线（12 票落地后）

**这张表会腐烂**——所以能机械检查的都进了 `scripts/check-style.sh`，进不去的只是参考。
（写第一版时表里三行就已经是旧数：`let mutable` 写 7 实为 9（我把 CLI 也数进去了）、
规则 2 的 3 处我写成「现存」却从没改、规则 7 的阈值实测无意义。闸门一跑全暴露了。）

| 指标 | 现值 | 有闸门吗 |
|---|---|---|
| 引擎行数 / `\|>` | 7261 行 / 364 个 | 无（密度不是指标） |
| 引擎 `let mutable` | 6（`Shanten.fs` 5 + `Rng.fs` 1，全部有理由） | **有**，预算 6 |
| `fun x -> Encode.* (…)` | 0（原 3 处已改 `>>`） | **有**，锁零 |
| `f (atom)` 多余括号 | 0 | **有**，锁零 |
| `.NET` 方法调用的括号 | 10（`Assert.Empty(x)` 等，按规则 8 保留） | 无（惯例，不该管） |
| 带 `Parallelism` 的属性模块 | 4（实测慢的那四个） | 无（判据是运行时间，静态查不出） |

## 规则 9：F# 表达不了「长度 34 的数组」，用私有类型 + 智能构造子代替

**F# 没有 const 泛型**（Rust 的 `[u8; 34]` 那种），也没有依赖类型。实测：`[<InlineArray(34)>]`
（.NET 8+ 的定长内联数组）**在 F# 里声明得出来但构造不出来**——`error FS1133: No constructors
are available for the type`。所以类型签名里写不出长度，只能是 `int array`。

而且就算写得出也不能用：**引擎要经 Fable 编成 JS**，`InlineArray` / `Span<T>` /
`System.Runtime.CompilerServices` 那套一律不可用。这条约束比语言能力更早否掉这个方向。

**F# 的做法是把长度保证放进构造路径，而不是放进类型签名**——本仓库已经这么做了：

```fsharp
// TileKindSet / HandShape：类型私有，只能经校验过的构造子拿到
type HandShape = private { Counts: int array; NakiCount: int }   // Counts 恒为 34 长
// HandShape.create 校验张数与「同种 ≤4 张」，构造不出非法值
```

于是**「34 长」不是类型说的，是「唯一的构造入口保证的」**。模块内部的私有函数
（如 `Shanten.searchStandard` 收的 `legal` / `original` / `counts`）直接收裸数组，
因为它们只能被那些已验证类型的方法调用——边界在**模块**上，不在每个函数签名上。

想更进一步（私有单 case wrapper 包住 34 长数组）在热路径上要加一层间接，**没做，也不建议做**：
现有的模块边界已经够，收益不抵可读性与速度的代价。
