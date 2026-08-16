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

待修的真实例：

```fsharp
// 坏（KyokuStart.fs:75）：四层，从最内层读起
Error(NoDoraIndicator(List.length (Wall.deadWall dealt)))
// 好
dealt |> Wall.deadWall |> List.length |> NoDoraIndicator |> Error

// 坏（RiichiState.fs:121）：三层变换
&& not (List.isEmpty (tenpaiDahai kindSet (List.length naki) hand))
// 好：给中间值命名，顺带说明它是什么
let dahaiKeepingTenpai = tenpaiDahai kindSet (List.length naki) hand
&& not (List.isEmpty dahaiKeepingTenpai)
```

拆的手段三选一：管道、`>>`、**给中间值起个有意义的名字**。第三种常常最好——
名字能解释「这一步算出的是什么」，管道不能。

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

**不满足这两条的命令式代码按坏味道处理。** 新增 `let mutable` 要在 `DECISIONS.md` 留一条。

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

## 度量基线（2026-08-16，12 票落地前）

供将来对比，不是目标值：

| 指标 | 现值 |
|---|---|
| 引擎行数 / `\|>` 数 | 6915 行 / 339 个（4.9 per 100 行） |
| `let mutable` | 7（6 在 `Shanten.fs`、1 在 `Rng.fs`，全部有理由） |
| 真循环（非推导式） | 18（全部是 34 长计数数组或原地洗牌） |
| `fun x -> f (g x)` 可改 `>>` | 3（`Tile.fs:237`、`Kaze.fs:51`、`Event.fs:167`） |
| 深度 ≥3 嵌套 | 65（多数是管道内 lambda 体；已修 `AgariShape.fs:38`；待修 `KyokuStart.fs:75`、`RiichiState.fs:121`） |
| 连续中间 `let` 串接集合变换 | 1（`Wall.fs:42`，两行有先后依赖，保留） |
