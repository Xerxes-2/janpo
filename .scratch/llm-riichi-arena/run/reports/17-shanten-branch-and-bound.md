# 17 — `Shanten.searchStandard` 加分支限界剪枝

**STATUS: done**

改动只有两个文件：`src/Janpo.Engine/Shanten.fs` 的 `searchStandard`（含注释，39+/17-）与
`docs/agents/fsharp-style.md` 规则 5 对照表里 `searchStandard` 那一行（外加一段把「写法实验」
和「算法改动」分开的说明）。公开签名、`standard` / `calculate` / `chiitoitsu` / `kokushi` /
`deadQuadKinds` 一字未动，`let mutable` 仍是 2 处。

研究报告 `docs/research/shanten-search-alternatives.md` 方向 1 指出了做法，本票**没有盲抄**：
下界的两条上界重新推了一遍、四组对拍重新跑了一遍、并用**两次变异对照**证明验证本身有牙
（见 §3）。原型的写法最后与我落地的一致，这是重做之后的结论，不是抄来的前提。

---

## 1. 剪枝是怎么做的

叶子的值是 `8 - 2*melds - partials - (hasHead ? 1 : 0) + (unpairable ? 1 : 0)`。
记 `current = 8 - 2*melds - partials - headBonus`，往下走只可能做三件事：加面子（+2，吃 3 张）、
加搭子（+1，吃 2 张）、加雀头（+1，吃 2 张）。于是「子树里还能长出的收益」有两条**独立**上界：

| 上界 | 来源 | 表达式 |
|---|---|---|
| 按组数 | 面子 + 搭子 ≤ 4 组（雀头另算，最多再一个），每组最多 +2 | `2 * (4 - melds - partials) + 1 - headBonus` |
| 按张数 | 面子 3 张换 2、搭子与雀头 2 张换 1，故每张最多贡献 2/3 | `2 * rem / 3`（整除即向下取整，仍是上界） |

`maxGain` 取两者的 min，仍是上界。`unpairable` 只会把叶子值**抬高**，忽略它对下界是安全的。
于是 `current - maxGain` 是整棵子树的下界，`>= best` 时剪掉——只剪「整棵子树都不可能优于 `best`」
的分支，最终 min 一字不变。

`rem` 是 `counts.[index..]` 剩余张数（不变式：等于该区间的计数和），顺着递归传：
面子 -3、顺子 -3（吃 index / index+1 / index+2）、雀头 -2、对子搭子 -2、两面 -2、嵌张 -2、孤张 -1，
跳过空牌种时不变（那里 `counts.[index] = 0`，区间和不变）。入口是 `Array.sum counts`。

**提前收敛支**：`maxGain = 0 && hasHead` 时子树里每片叶子都恰等于 `current`，直接 `best <- current`
收工，不必走到 `index = 34`。叶子数就是被这一支砍光的（§4 的实测：639 → 0.2）。

**`hasHead` 守卫不能省**（研究里踩过、我用变异对照复现了）：无雀头时 `rem <= 1` 同样让
`2 * rem / 3 = 0`，而剩下那张若是「4 张全在手」的牌种，叶子是 `current + 1`（孤张凑不出雀头，
`unpairable` 成立），少记这个 1 就算错。

## 2. 逐手对拍：604 661 手，0 差异

方法照 `scripts/fsi/README.md`：把整棵树复制到 `/tmp` 改副本，两份 DLL 各自 `dotnet fsi` 直调真实
API，**同一份输入喂两边、逐行 diff 输出文件**（不是校验和——校验和会掩盖成对抵消的错误）。
每行两个值：`standard` 与 `calculate`。

| 输入 | 手数 | 差异 |
|---|---|---|
| 真实牌谱结构化手牌 `structured-corpus.txt` | 204 661 | **0** |
| 均匀随机 `random-hands.txt` | 200 000 | **0** |
| 满张偏置 `quad-hands.txt`（2-3 个牌种握满 4 张） | 100 000 | **0** |
| 三麻牌种集合 `sanma-hands.txt`（缺 2m-8m，压 `legal` 分支） | 100 000 | **0** |
| **合计** | **604 661** | **0** |

其余锚点：

- `tests/Janpo.Engine.Tests/fixtures/shanten-oracle.tsv` 4000 手：**0 mismatch**（改前改后都跑）
- `scripts/oracle/differential.sh 30000`（PyPI `mahjong` 2.0.0 第三方 oracle）：seed 20260816 与
  seed 424242 各 30 000 手，**均 0 差异**
- `janpo shanten --batch` 3 万手：改前改后**输出文件逐行相同**
- 基准脚本自带的校验和：结构化 5 万手上 `standard` 920110 / `calculate` 868560，两版一致
- `dotnet test -c Release`：**543 个测试全绿**

手牌语料沿用研究产出的那五份文件（`/tmp/searchstandard-research/`，纯解析脚本从 12 188 局天凤鳳凰卓
mjai 牌谱抽的，或 `gen-hands.py` 生成的合成手牌），已复制到 `/tmp/janpo17/hands/` 并记了 md5：

```
0b4a88b138a3107469a39cca4b80954b  structured-corpus.txt
4479323b68dfc36fbd23c7870605f316  random-hands.txt
62747783f55f11eb932dbed94ca4dcab  quad-hands.txt
b323b65aeac97c590c2b85375a04bab7  sanma-hands.txt
ca496fd1a27df6401157a27dcb1ffc4e  structured-hands.txt   # 基准用的 5 万手
```

## 3. 「0 差异」有没有牙：两次变异对照

**「对拍 0 差异」只有在能证明它会响的时候才是好消息**（备注 N-23 的原话）。所以我在 `/tmp` 里另做
两个**故意写错**的变体，跑同一套验证：

### 对照 A：删掉 `maxGain = 0 && hasHead` 里的 `hasHead`

| 验证手段 | 结果 |
|---|---|
| 结构化 204 661 手 | 0 差异 ← **测不出** |
| 均匀随机 200 000 手 | 0 差异 ← **测不出** |
| 三麻 100 000 手 | 0 差异 ← **测不出** |
| **满张偏置 100 000 手** | **14 153 手差异** |
| 仓库固件 4000 手 | 112 mismatch |
| `differential.sh 30000` | 报差异（例：`3 7m 7m 7m 7m`，oracle=1 engine=0） |
| `dotnet test -c Release` | **4 个测试红**：`UkeireTests.四张全在手里的牌不算有效牌`、`ShantenTests.四张全在手里的单骑不算听牌`、`ShantenOracleTests.与 oracle 的向听数差异为零`、`ShantenProperties.听牌的手牌一定有有效牌` |

最小反例（基线 `standard`=1，去掉守卫后=0）：`2 5z 5z 5z 5z 9s 9s 9s 9s`。

**对研究报告的一处补充**：研究说这个坑「普通语料测不出、只有满张偏置那 10 万手顶得出来」——
对**逐手对拍的语料**成立（结构化 / 随机 / 三麻三组全是 0），但仓库自己的固件、`differential.sh`
与 4 个既有测试**也会响**。也就是说这个坑不会漏网；满张偏置那 10 万手的价值在于**它是逐手对拍
这条通道里唯一会响的一组**，验收要求留着它是对的。

### 对照 B：组数上界少算雀头那 +1（`+ 1 - headBonus` → `- headBonus`，上界不再是上界）

| 输入 | 差异 |
|---|---|
| 结构化 204 661 手 | 15 341 |
| 均匀随机 200 000 手 | 1 484 |
| 满张偏置 100 000 手 | 42 |
| 三麻 100 000 手 | 629 |
| 仓库固件 4000 手 | 116 mismatch |

两次对照说明：**这套验证对「下界算松了会漏、算紧了会错」两个方向都有牙**，落地版本的 0 差异
不是因为语料没打到剪枝路径。

## 4. 搜索树规模（自己量的，不是抄研究的）

给两个 `/tmp` 副本各加节点/叶子计数器（`nodes.fsx`），结构化 5 万手：

| | nodes/hand | leaves/hand |
|---|---|---|
| 改动前 | mean 10 094.4 / p50 5 238 / p90 24 683 / p99 67 434 / max 351 872 | mean 639.3 / max 13 568 |
| 剪枝后 | **mean 239.6 / p50 135 / p90 552 / p99 1 483 / max 5 593** | **mean 0.2 / p90 1 / max 3** |

节点降 42×，时间只降 6.8×——被砍掉的多是廉价的空牌种跳跃；叶子降到 0.2 是提前收敛支的功劳。

## 5. 性能（交错跑，区间不是单值）

方法：`dotnet fsi` 直调各自的 Release DLL；**每个进程先整跑 3 遍预热**，每轮再跑 N×reps；
base / bnb **交错跑**（base → bnb → base → …）。机器另有 agent 在跑 14 票的 soak，
load average 12–29，绝对值会偏高，比值可信。

### 主基准：真实牌谱结构化手牌 5 万手（不是均匀随机手牌），4 轮交错

| | 改动前 | 剪枝后 | 倍数 |
|---|---|---|---|
| `standard` | 8.576 / 8.700 / 8.698 / 8.767 µs | **1.262 / 1.281 / 1.288 / 1.296 µs** | **6.7–6.9×** |
| `calculate` | 8.651 / 8.810 / 8.838 / 8.909 µs | **1.371 / 1.374 / 1.380 / 1.394 µs** | **6.3–6.4×** |

### 最难的 2000 手 / 备注 N-19 那一手（3 轮交错，`standard`）

| 输入 | 改动前（2000×10） | 剪枝后（2000×200） |
|---|---|---|
| `hard-hands.txt`（真实牌谱里搜索树最大的 2000 手） | 60.652 / 61.092 / 61.671 µs | **1.018 / 1.022 / 1.033 µs（约 60×）** |
| `1m1m1m 2m3m4m5m6m7m8m9m 9p9p 5z` | 72.545 / 73.105 / 73.189 µs | **0.748 / 0.759 / 0.799 µs（约 93×）** |

**这里我自己踩了研究警告的分层 JIT 坑，记下来**：这两组输入只有 2000 手，`reps=10` 时剪枝版一轮
只跑 0.05 s，量出 3.1 µs / 2.4 µs——比稳态高 3 倍。把快版本的 `reps` 提到 200（让被测区间同样
落在 0.3–0.4 s）之后，三轮落在 0.02 µs 以内，才是上表的数。**慢版本 1.2 s 一轮没有这个问题，
所以两边 reps 不同是有意的：对齐的是被测时长，不是循环次数。**

### 端到端

| 场景 | 改动前 | 剪枝后 |
|---|---|---|
| `dotnet test -c Release` 全量（543 测试，交错 5 轮） | runner 42 / 46 / 39 / 51 / 52 s（后三轮 wall 41.3 / 53.3 / 53.5 s） | **runner 40 / 29 / 31 / 29 / 30 s**（后三轮 wall 32.4 / 31.0 / 31.7 s） |
| 工作区最终一次全量 | — | **wall 34.5 s / runner 30 s，543 绿** |
| `janpo shanten --batch` 3 万手（含进程启动，2 轮交错） | 943 / 945 ms | **628 / 638 ms** |

剪枝版第 1 轮的 40 s 是离群值（前两轮没记 load，后四轮稳定在 29–31 s），按纪律原样列出。

免费自检通过：全量墙钟 43–50 s → 31 s 上下（runner 口径 39–52 → 29–31 s），说明补丁确实生效。

## 6. 风格与闸门

- `dotnet fantomas --check .`：干净（exit 0）；`fantomas src/Janpo.Engine/Shanten.fs` 报 unchanged
- `scripts/check-style.sh`：通过（引擎 `let mutable` 仍是 2 处，预算未动）
- 新增的都是算术与分支（`min a b`、`current - maxGain`），照风格规则 4 的例外 2 **不强行管道化**
- 注释按验收要求重写：那句「它不参与剪枝，只在叶子取 min」已删；新注释写清了（a）两条上界怎么来、
  （b）`hasHead` 不能省的理由、（c）`rem` 的不变式与每个分支减多少

## 7. 收尾 review（两轴，自己顺序跑的）

**Standards 轴**（`docs/agents/fsharp-style.md`）：

- 规则 1/3：没有新增从里往外读的嵌套；`min (…) (…)` 两个实参都是算术表达式，不是「变换链」
- 规则 5：`let mutable best` 的理由**升级**了（从「量过 +10%」到「它是分支限界的上界，跨子树活着」），
  代码注释与对照表两处都改了；预算 2 未动
- 规则 8：`f (atom)` 0 处（闸门锁零，通过）
- 命名：`rem` / `maxGain` / `current` / `headBonus` 是搜索内部量，不是领域术语，不受 CONTEXT.md
  的罗马字术语约束；与既有的 `melds` / `partials` / `hasHead` 同一风格

**Spec 轴**（票 17 的验收）：8 条逐条对照，全部满足（对拍手数超出下限：结构化 204 661 ≥ 20 万、
随机 200 000 ≥ 20 万、满张 100 000 ≥ 10 万、三麻 100 000 ≥ 10 万）。范围也守住了——
花色分解 / 预计算表 / 缓存 / `skipEmpty` 一个没做。

blocking 问题：无。

## 8. 留给人的待审项

1. **15 票的前提变了**（研究已经提过，这里给出落地后的实测口径）：`tenpaiDahai` 枚举 14 张打法在
   结构化手牌上的成本，随 `standard` 从 8.6–8.8 µs 降到 1.26–1.30 µs 同比例下降。15 票那个
   「前置闸门」值不值得做，要拿新数字重判——本票没有动 15 票的任何文件。
2. **「剪枝之后纯函数写法是不是仍然 +10%」仍未量**（形状变了：上界要当参数传下去再传回来）。
   我在风格文档里把这句话写进去了，免得下一个人拿旧数当结论。
3. 研究报告 §2.3 那句「普通语料测不出」建议按 §3 的对照 A 收窄为「逐手对拍的三组普通语料测不出，
   固件与 `differential.sh` 会响」。我没有改研究报告（不是本票的文件），只在这里记一笔。

## 9. 复现

```bash
# 两棵树：base = 改动前，bnb = 改动后
rsync -a --exclude=.jj --exclude=.git --exclude=bin --exclude=obj ~/janpo-ws-b/ /tmp/janpo17/bnb/
cp ~/janpo-prototypes/searchstandard/{bench.fsx,nodes.fsx} /tmp/janpo17/<tree>/scripts/fsi/
cd /tmp/janpo17/<tree> && nice -n 19 dotnet build src/Janpo.Cli -c Release

nice -n 19 dotnet fsi --exec scripts/fsi/bench.fsx verify  /tmp/janpo17/hands/<in>.txt <out>.txt [sanma]
nice -n 19 dotnet fsi --exec scripts/fsi/bench.fsx bench   /tmp/janpo17/hands/structured-hands.txt 1 50000 10
nice -n 19 dotnet fsi --exec scripts/fsi/bench.fsx fixture tests/Janpo.Engine.Tests/fixtures/shanten-oracle.tsv
nice -n 19 dotnet fsi --exec scripts/fsi/nodes.fsx        /tmp/janpo17/hands/structured-hands.txt 50000
nice -n 19 ./scripts/oracle/differential.sh 30000
```

辅助脚本全程 `nice -n 19`、串行（并行进程数 1），只有 `dotnet test` 让 xunit 自己吃核心。
`/tmp/janpo17/` 是易失的，本报告已把全部数字与两次变异对照的补丁内容抄进来了。
