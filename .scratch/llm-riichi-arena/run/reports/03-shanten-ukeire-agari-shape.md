# 03 — Shanten、Ukeire 与和了型判定 · 实现报告

**Status:** done（票已标 `ready-for-human`）
**工作区:** `/home/xerxes2/janpo-ws-b`
**Fixed point:** `aa71bed2`
**验证:** `./scripts/ci.sh` 全绿（101 个测试通过、`fantomas --check` 干净、引擎依赖白名单通过）
+ oracle 现跑对拍 **80 万手，差异 0 处**

---

## 公开签名

```fsharp
// TileKindSet.fs —— 规则集里「哪些牌种存在」，形态判定的显式入参
type TileKindSet
TileKindSet.fourPlayer : TileKindSet                       // 全 34 种
TileKindSet.ofKinds    : Tile list -> TileKindSet
TileKindSet.contains   : Tile -> TileKindSet -> bool
TileKindSet.kinds      : TileKindSet -> Tile list
TileKindSet.count      : TileKindSet -> int

// HandShape.fs —— 形态判定的输入：暗牌 + 已成型的副露数
type HandShape
type HandShapeError = NakiCountOutOfRange | ConcealedCountMismatch | TileKindOverflow | TileNotInHand
HandShape.create      : int -> Tile list -> Result<HandShape, HandShapeError>   // nakiCount 在前
HandShape.ofConcealed : Tile list -> Result<HandShape, HandShapeError>
HandShape.add / remove: Tile -> HandShape -> Result<HandShape, HandShapeError>
HandShape.nakiCount / concealedCount / countOf / tiles / isAwaitingDraw / toDisplay

// Shanten.fs —— 0 = Tenpai，-1 = 和了型，13 张上界 8
type Shanten                                               // 私有 struct DU
Shanten.agari / tenpai : Shanten
Shanten.value / isAgari / isTenpai / toDisplay
Shanten.standard   : TileKindSet -> HandShape -> Shanten          // 一般型
Shanten.chiitoitsu : TileKindSet -> HandShape -> Shanten option   // 副露过 = None
Shanten.kokushi    : TileKindSet -> HandShape -> Shanten option   // 副露过 = None
Shanten.calculate  : TileKindSet -> HandShape -> Shanten          // 三者最小

// AgariShape.fs —— 只判型，不含役与点数
type AgariShape = Standard | Chiitoitsu | Kokushi
AgariShape.classify : TileKindSet -> HandShape -> AgariShape list  // 空 = 不是和了型
AgariShape.isAgari  : TileKindSet -> HandShape -> bool
AgariShape.toDisplay: AgariShape -> string

// Ukeire.fs —— 有效牌
type Ukeire = { Shanten: Shanten; Tiles: (Tile * int) list }       // 牌种 → 剩余枚数，mjai 升序
type UkeireError = HandNotAwaitingDraw | VisibleKindOverflow
Ukeire.calculate : TileKindSet -> Tile list -> HandShape -> Result<Ukeire, UkeireError>
//                                ^ visible：手牌之外的可见牌（牌河 / 副露 / 宝牌指示牌）
Ukeire.total / kindCount / live / toMjai / toDisplay
```

CLI：`janpo shanten [--naki N] <记法>...` 打印向听数、和了型与有效牌；
`janpo shanten --batch` 从 stdin 逐行读「`<副露数> <记法...>`」逐行打印向听数（对拍管道用）。
退出码沿用 0 / 1（数据错）/ 2（用法错）。

## oracle 对拍

**oracle：** PyPI 的 `mahjong==2.0.0`（MahjongRepository/mahjong）。版本钉死在
`scripts/oracle/shanten_oracle.py` 的 PEP 723 内联元数据里，`uv run` 会自己装好 Python 与依赖，
**从零一条命令可复现**：

```bash
uv run scripts/oracle/shanten_oracle.py generate --count 4000 --seed 20260816   # 产出 TSV
./scripts/oracle/differential.sh 200000 20260816                                # 现跑对拍
./scripts/oracle/refresh-fixture.sh                                             # 重生成提交进仓库的固件
```

`differential.sh` 做三件事：Python 产随机手牌与 oracle 向听数 → `cut -f1` 喂给
`janpo shanten --batch` → `diff` 两列。手牌生成器有 6 种模式（均匀随机 / 单双花色 / 结构化
（先搭面子再扰动）/ 多对子 / 幺九牌为主 / 含四张同种），副露数按 60:15:12:8:5 加权覆盖 0-4，
张数在 `13-3n` 与 `14-3n` 之间各半。

**对拍量与差异：**

| 批次 | 手数 | 差异 |
|---|---|---|
| seed 20260816 / 424242 各一轮 | 400 000 | 0 |
| seed 1 / 2 / 3 各一轮 | 300 000 | 0 |
| 固定副露数 0/1/2/3/4 各一轮 | 300 000 | 0 |
| 提交进仓库的固件（`dotnet test` 每次都跑） | 4 000 | 0 |
| **合计** | **≈ 1 004 000** | **0** |

**oracle 不是运行时依赖**：`dotnet test` 只读提交进仓库的
`tests/Janpo.Engine.Tests/fixtures/shanten-oracle.tsv`（4000 手，含 -1..6 各档与副露手），
CI 不装 Python；`scripts/ci.sh` 的引擎依赖白名单没有变化。

### 对拍逼出来的三个真 bug

对拍不是走过场，第一轮 5000 手就打出 20 处差异，全是引擎错。逐条修完才归零：

1. **面子也受「最多 4 组」的约束**。原来只在拆搭子时判 `melds + partials < 4`，先拆搭子再拆面子
   就能凑出 5 组，把「4 面子 + 1 搭子」当成了和了型。
2. **孤张全是「四张全在手里」的牌种且无雀头 → +1**。`345m 678m 123p 6666s` 这类手：4 面子 +
   第 4 张 6s 单骑，第 5 张 6s 不存在，永远和不了，是一向听而不是听牌。
3. **死张下界**。手里握满 4 张、且进不了顺子的牌种（四麻即字牌），每种至少吃掉一次替换；
   3n+2 的手牌本来就要打一张，白送一次。例：`1z1z1z1z 2z2z2z2z 234m 5s5s` 是二向听不是一向听。

（顺带：「对子搭子」也要求该牌种手里不足 4 张——4 张全在手就永远变不成刻子。）

第 2、3 条不是实现细节而是向听数定义的一部分（「还需几次替换才能听牌」），
oracle 的实现同样这么算，两边是**同一个语义**而不是我去迎合它。

## 关键取舍

| 取舍 | 选了什么 | 为什么 |
|---|---|---|
| 输入类型 | `HandShape` = 34 长计数 + `nakiCount`，构造时验张数与「每种 ≤ 4」 | 三个函数共用一个已验过的输入；`Tile list * int` 两个参数容易传反，12 张这种非法手牌会漏进算法 |
| 牌种集合 | `TileKindSet` 显式入参，内部仍是 34 长 bool 数组（`internal` 快路径） | 票的硬要求。它在四处真正生效：搭子补不补得上、七对子要 ≥ 7 种、国士要 13 种幺九、死张判定 |
| Shanten 表示 | 私有 struct DU + `value`/`isTenpai`/`isAgari` | 裸 int 的下游极易写成 `shanten <= 0` 把和了当听牌 |
| 和了型 | `classify` 返回**一组**型 | 二盃口的手同时是一般型与七对子，07 判役要用得上这个区分 |
| Ukeire 的 0 枚牌种 | 保留在 `Tiles` 里，另给 `live` 过滤 | 形态信息不该丢：Assisted 档要能说「你听的牌已经绝张」 |
| 算法 | 朴素 DFS 面子分解（原地增删 34 长副本） | 13.7 µs/手（Shanten）、128 µs/手（Ukeire，含 34 次 Shanten）。够快，先要正确 |
| 纯度 | 可变只在函数内的副本上，公开函数纯且可并发 | 票要求「丑但快包在纯接口后面」 |

自主决策 7 条 + 1 条提案，记在 `run/DECISIONS.md`。

## 测试（101 个，全绿；本票新增 75 个）

- `ShantenTests.fs`（19）— 数值约定、四面子一雀头、单骑听牌、8 向听上界、七对子 / 国士的听牌与
  和了、三者取最小、副露 0-4、**四张全在手里的单骑不算听牌**、**四张字牌是死张**、
  **牌种集合抠掉 2m 后 `1m3m` 不再是搭子**、牌种不足 7 种没有七对子、缺幺九牌种没有国士。
- `ShantenProperties.fs`（15 条 FsCheck 属性，MaxTest=300）— 值域 -1..8；等摸手牌不可能是和了型；
  **摸 X 打 X / 打 X 摸 X 后向听不变**；**摸一张只会不变或减一、打一张只会不变或加一、
  一次摸打变化 ≤ 1**；搭出来的四面子一雀头一定被判成和了型（独立构造，不经分解搜索）；
  和了型 ⟺ 向听 -1；听牌必有有效牌；有效牌摸进来确实降一向听；不在有效牌里的摸进来不降；
  听牌时的有效牌就是和了牌；剩余枚数 = 4 − 可见张数。
- `AgariShapeTests.fs`（10）— 三种型、二盃口同时成立两种、副露手、差一张、四面子加搭子不是和了、
  副露过没有七对子与国士。
- `UkeireTests.fs`（16）— 单骑 / 边张 / 两面 / 七对子 / 国士十三面 / 副露手的有效牌、mjai 升序、
  剩余枚数扣可见牌、全部可见时仍列出但 0 枚、四张全在手里的牌不算有效牌、两类错误。
- `HandShapeTests.fs`（13）— 张数与副露数的约束、超过 4 张、红宝牌合并、摸打往返、两类拒绝、渲染。
- `ShantenOracleTests.fs`（2）— 固件够大且覆盖 -1..4 各档与副露手；与 oracle 差异为 0。
- `HandShapeGenerators.fs` — `HandShapeArbitraries`：任意手牌 / 等摸手牌 / 已摸手牌 / 一定成立的
  和了牌，均匀与结构化两种生成器按 1:2 混合；`ToString` 打 mjai 记法，失败时能直接看到手牌。

**属性与用例做过变异验证**（改实现 → 跑测试 → 确认变红 → 改回）：

| 变异 | 变红的测试 |
|---|---|
| 撤掉「孤张全是四张牌种」修正 | oracle 对拍、听牌必有有效牌、四张单骑不算听牌、四张不算有效牌 |
| 面子不受 4 组上限约束 | oracle 对拍、有效牌摸进来确实降一向听、两个有效牌用例 |
| 嵌张不看牌种集合 | 「牌种集合里没有 2m 时 1m3m 就不是搭子」 |
| 撤掉死张下界 | oracle 对拍、「四张全在手里的字牌是死张」 |
| Ukeire 不扣可见牌 | 两个剩余枚数用例 |

（前两次变异验证时死张那条只有一个具名用例接得住，于是给 oracle 生成器加了 `gen_quads`
模式并重生成固件——现在固件也接得住了。）

## Review 结论（两轴，fixed point `aa71bed2`）

无法派生 sub-agent，按 runbook 自己顺序跑了两轴。

### Standards（对照 CONTEXT.md、ADR-0001/2/3、01 票立的结构约定）

**已修（本轮自动修）**

1. **Duplicated Code** — 四个测试文件各抄了一份「记法 → HandShape」的构造助手，抽成
   `HandFixture.tiles` / `HandFixture.hand`。
2. `Ukeire.calculate` 在循环里反复取 `HandShape.counts hand`，且 `Shanten.calculate` 每次构一个
   `option list`。都在 34 次调用的热路径上，各自改成一次拷贝 / 无分配的折叠。

**只记录不修（nitpick / 判断题）**

3. `Ukeire` 的字段名 `Shanten` 与类型名 `Shanten` 同名。F# 合法，读起来也准确，但 `ukeire.Shanten`
   与 `Shanten.xxx` 混在一段里时要看两眼。
4. 新的五个类型都没有 JSON 编解码。理由：它们是分析结果，不是 mjai wire 上的东西，Paifu 里没有
   它们的位置。M1 的 Assisted 档 prompt 要序列化时，`toDisplay` 与 `toMjai` 够用。
5. `AgariShape.classify` 走的是三次完整向听计算（其中一般型是全量 DFS）。只判「是不是 -1」时
   略奢侈，但它保证了「和了型」与「Shanten」永远不会各说各话——没有第二份规则。
6. 引擎里有 `for` 循环与原地增删数组，风格上不算 F# 的样板。票明确允许「丑但快」，且可变全部
   困在函数内的副本里，公开面是纯的。
7. `scripts/oracle/*.sh` 用了 `mktemp -d`、`paste`、`awk`，POSIX 之外只依赖 GNU coreutils 的常见行为，
   与 `ci.sh` 无关（CI 不跑它们）。

**未发现**违反术语表或 ADR 的地方：标识符全是罗马字（唯一的例外 `Standard` 见 DECISIONS）；
中文只出现在五个 `toDisplay` 与 CLI 的打印里；引擎内部没有中文诊断串（错误全是结构化 DU）；
测试固件与对拍数据一律 mjai 记法。

### Spec（对照票 03 的 9 条验收 + spec.md 第 90 / 128 / 129 行）

- 9 条验收**全部满足**，逐条勾在票文件里。
- spec.md 第 128 行点名的两条不变量（「打 X 摸 X 向听不变」「任何单步摸打变化 ≤1」）都落成了属性。
- **超出票面的部分（scope creep，均为有意）**：
  - `HandShape` / `TileKindSet` 两个类型（票只说「三个纯函数」，但入参总得有个类型；
    `TileKindSet` 是票自己要求的接缝）。
  - `janpo shanten` 子命令（对拍管道需要 `--batch`；顺手把单手查询也接上，01 票的约定是每票
    在 CLI 上留一支）。
  - `Ukeire.live` / `kindCount` / `toMjai`、`HandShape.add` / `remove`（属性测试与 04 的摸打循环要用）。
- **未发现**实现与票面相悖的地方。

## 留给人的待审项

1. `run/DECISIONS.md` 的 7 条决策，重点两条：
   - **「摸不到第 5 张」的两条修正**——它改变了「听牌」的边界（四张全在手里的单骑不算听牌）。
     这会渗进 09 的立直合法性与 06 的听牌判定：**天凤 / 雀魂在形式听牌上是不是也这么判，
     值得人确认一次**。引擎现在的口径是「向听 = 还需几次替换才能听牌」，与 oracle 一致。
   - **提案 03-A**：把 `HandShape` / `TileKindSet` / `AgariShape` / 死张补进 CONTEXT.md，
     并在 `Shanten` 条目补上「-1 = 和了型」。
2. `Ukeire.Tiles` 保留剩余 0 枚的牌种是我定的口径。若 Assisted 档的 prompt 想直接用它排序，
   记得先 `Ukeire.live`。
3. 性能：Ukeire 128 µs/手。真人 UI 每手逐张试打（14 次）约 1.8 ms，够用；
   若 M1 的 ToolSearch 档要做深搜，这里得先换成按花色打表的实现。
4. oracle 固件 145 KB 进了仓库。若嫌大，可以调小 `refresh-fixture.sh` 的默认手数，
   代价是 `dotnet test` 的对拍覆盖变薄（大批量那份本来就在 `differential.sh` 里）。
