# 66 — 引擎性能第 6 轮实现：振听簿记与立直判定加向听前置闸（E2+E3）

**Status:** ready-for-human
**基点（code-review fixed point）:** commit `855511db`（change `qoykvlwl`）
**工作区:** `/home/xerxes2/janpo-ws-b`（jj workspace）
**改动面:** `src/Janpo.Engine/` 2 个文件（`AgariShape.fs`、`RiichiState.fs`）+ 测试 2 个文件
（`ShantenProperties.fs`、`RiichiTests.fs`）+ 登记册一行；清算（65 刚改过）、`web/`、prompt、
workflow 一律没动

---

## 0. 一句话

把票 64 预验证过的两条向听等价性捷径落进引擎本体：**E2** 在 `AgariShape.waits` 的 34 次
逐种试摸之前先花一次向听计算，不听牌（向听 > 0）直接返回空表——重放实测 83.6% 的打牌后
手牌不听牌，那些试摸全是白跑；**E3** 把 `RiichiState.canDeclare` 的第四条判据从
`tenpaiDahai` 逐张试打换成单次向听 `≤ 0` 前置。**引擎重放 2.29→0.74 ms/局（≈3.1×，
64 预验证给的是 2.06×，方向一致、本轮固件更大更快）**；2025 整年 178,888 场重扫差异输出
逐字节相同（59 处，一处不多一处不少）；`dotnet test` 用例 duration 之和 −30%、CI 墙钟 −13%。
公开签名与语义一字不动（55 号的先例）；两处都是纯 F# 现有 API 的组合，**两侧共编，
没有 `#if FABLE_COMPILER` 分叉**（Fable 产物里验过两处都在，见 §4.4）。

---

## 1. 两处修法逐条

### E2：`AgariShape.waits` 的向听前置闸

`ShantenScratch` 建好之后、34 次试摸之前，先 `Shanten.calculateWith` 问一次向听；
**向听 > 0 返回空表**，`≤ 0` 照常进批。剪枝判据取 `> 0` 不取 `<> 0`：最保守的一侧
（等摸形向听不会是 −1——`ShantenProperties.等摸的手牌不可能已经是和了型` 钉着——但这一条
不在闸里赌）。缓冲无冲突：闸用 `scratch.Search`，随后的批用 `scratch.Tsumo` + `Search`，
每次进搜索前都整段覆写。

### E3：`RiichiState.canDeclare` 的单次向听前置

第四条判据从 `tenpaiDahai ≠ []`（对每张手牌试打一次、每次一趟向听，~10 趟/手）换成新的
module 私有 `hasTenpaiDahai`：`HandShape.create` 成功 且 非等摸形 且 整手向听 `≤ 0`（一趟）。
**判据是 `≤ 0` 不是 `= 0`**——向听 −1（已和牌形）的手打一张必然回到听牌，规则上可以
放弃自摸宣立直（票 64 的 20 万手采样正是用这种手否掉了 `= 0` 的第一版命题）。
`tenpaiDahai` 本身原样保留：立直宣言后（`GameState` 的 `Declared` 分支）选宣言牌仍要它，
那是一局 0.76 次的事。`&&` 链的短路保持原样（fsharp-style 限制 B：非门清的手在第一子句
就断，向听算不到）。

## 2. 「被剪掉的路径本来就返回空/false」的证明

### E2（被剪：向听 > 0 的等摸手）

改前 `waits ≠ []` ⟺ ∃ 牌种补上成和了型（逐种 `classifyIn` 的定义；「手里已满 4 张」的
牌种两版同样排除——改前 `HandShape.add` 拒第五张，改后 `drawn.[index] >= 4` 拒，票 55 已对齐）。
等摸手上 **`向听 = 0 ⟺ ∃ 牌种补上成和了型`**（P1）：

- 票 64 的 20 万手采样（全池 / 限花色 / 强插四张同种打死听边界）**零反例**，右侧走
  `classify` 暴力逐种拼手、不经过被测的 `waits`，非循环论证；
- `Shanten.standardIn` 的 `deadQuadKinds` 修正正是它在「听自己抓完了的牌」上仍成立的原因；
- 本票把 P1 钉成常驻属性测试（§3），每次 CI 300 例。

因此向听 > 0 的手改前 34 次试摸全部返回空，剪掉后结果同为 `[]`。

### E3（换判据：`tenpaiDahai ≠ []` → 3n+2 且向听 ≤ 0）

逐条对上（P2′）：

| 情形 | 改前 `tenpaiDahai ≠ []` | 改后 `hasTenpaiDahai` | 依据 |
|---|---|---|---|
| `HandShape.create` 失败 / 等摸形 | `[]` → false | false | 两版同一分支形状 |
| 3n+2 且向听 > 0 | false | false | **既有钉住属性**「打一张之后向听数只会不变或加一」：每张打完向听仍 > 0，无一张到得了听牌 |
| 3n+2 且向听 −1 | true | true | 既有属性「等摸的手牌不可能已经是和了型」+「打一张只会不变或加一」：13 张余牌向听只能落在 0，**每张**都保持听牌 |
| 3n+2 且向听 0 | true | true | P2′ 的非平凡方向：票 64 的 20 万手采样（向听 ≤ 0 的 22,652 手）零反例 + 本票常驻属性（§3）+ §4 的全语料重扫 |

四行里三行是既有钉住属性的定理级推论，只有第四行靠采样 + 新属性 + 全语料重扫共同背书。

## 3. 新增的三条测试与它们的反向自证（判据 1 / 3）

1. **P1 属性**（`ShantenProperties.等摸的手牌有和了牌等价于向听数为 0`）：
   `AgariShape.waits ≠ [] ⟺ Shanten = 0`，左右两侧不同实现（试摸批 vs 向听公式），非恒真式。
   **live-fire**：把 E2 的闸临时弄坏成 `>= 0`（多拦听牌手），当场红：

   ```
   失败 Janpo.Engine.Tests.ShantenProperties.等摸的手牌有和了牌等价于向听数为 0 [2 ms]
   Falsifiable, after 2 tests (0 shrinks) …
   AwaitingHand { Counts = [|0;0;0;0;1;1;1;…|] … }
   ```

2. **P2′ 属性**（`ShantenProperties.已摸进的手牌打得出保持听牌的一张等价于向听数不超过 0`）：
   `tenpaiDahai ≠ [] ⟺ Shanten ≤ 0`（DrawnHand，naki 0–4）。`tenpaiDahai` 本票未改，
   左右两侧（逐张试打 vs 向听公式）天然是两份实现，它守的是 E3 换判据的等价性本身。
   **执行次数不是零**（判据 3）：把属性右侧临时收紧成 `= 0`，第 4 例就撞上向听 −1 的
   生成手（`Falsifiable, after 4 tests`）——`≤ 0` 与 `= 0` 的分界每轮都被生成器踩到。

3. **反例钉住**（`RiichiTests.已成和了形的手也立得了直：放弃自摸宣立直`）：
   向听 −1 的手（`123m 456m 789m 123p 5z5z`）`tenpaiDahai` 非空、`canDeclare` 为 true。
   **live-fire**：把 E3 判据临时弄坏成 `= 0`，它当场红，同时既有的
   `RiichiTests.宣言了立直就不能回头自摸和` 也红——同一伤害有两道闸咬得住：

   ```
   失败 Janpo.Engine.Tests.RiichiTests.已成和了形的手也立得了直：放弃自摸宣立直 [< 1 ms]
   Assert.True() Failure  Expected: True  Actual: False
   失败 Janpo.Engine.Tests.RiichiTests.宣言了立直就不能回头自摸和 [19 ms]
   ```

   三处弄坏都已复原并重跑绿。测试数 831 → 834，一条没删、一条没放宽。

## 4. 语义没改：四类证据（照 55 号）

### 4.1 黄金用例逐字段零 diff + 重录无变化

`janpo golden check`：**40 条用例、2069 个字段、3378 行：全部一致 ✓**。
`janpo golden write` 重录一遍，`jj diff` 里 `dual-target.json` **零变化**。

### 4.2 真牌谱对拍：固件 111 场零差异；2025 + 2026 整年重扫逐字节相同

- **CI 固件 111 场 / 1128 局**（65 号扩到 111 后的全量）：`PaifuDifferentialTests` 全绿，
  `paifu-cost` 报「其中 111 场有 oracle；差异 0 处」。
- **2025 整年**（178,888 场 / 1,893,891 局，`paifu-scan-zip.fsx` 4 进程 `nice -n 19`，
  62 号的管道）：改前 59 处差异（分片 13/16/9/21，等于 65 号收官时的 D 名单），改后
  **同为 59 处，`diffs-*.tsv` 四个分片逐字节相同**；`progress-*.tsv` 的 CK 行除
  **耗时秒数那一列**（`renderCheckpoint` 的 `BaseElapsedS + elapsed`，按定义记墙钟）外
  逐字节相同（`cut -f1-11,13-` 后 `cmp` 静默）。
- **2026 整年**（12,188 场 / 129,179 局）：改前改后同为 6 处（64 号归的 D 类上游缺口），
  同一口径逐字节相同。

### 4.3 soak：四种选手 × 种子 1–500，事件流逐条相同

55 号的形状、种子扩到 500：`janpo game <seed>`（默认 / `--uniform` / `--covering` /
`--opinionated`）各 500 场，**每树 2000 场 / 1,288,194 行，`cmp` 静默**——事件流、
终局点数、顺位逐条相同。

### 4.4 双目标：Fable 侧照跑，剪枝两侧共编

`./scripts/ci.sh` 全绿（含浏览器内曳光弹对拍、浏览器内黄金用例逐字段逐行、导出牌谱
fold 回放、整场终局点数）。Fable 产物核过：`web/src/generated/Janpo.Engine/AgariShape.js`
里有 `ShantenModule_value(ShantenModule_calculateWith(…)) > 0` 的闸、`RiichiState.js` 里有
`RiichiStateModule_hasTenpaiDahai`——**同一份 F# 源编出的同一逻辑，没有 `#if` 分叉**。

## 5. 性能数字（32 逻辑核，交错跑，`nice -n 19`）

### 5.1 引擎重放（`paifu-cost.fsx`，固件 111 场 / 1128 局，预热 + 3 趟取最小 × 2 轮交错）

| 段 | 改前 | 改后 | 倍数 |
|---|---:|---:|---:|
| **引擎重放** | 2.294 / 2.314 ms/局 | 0.762 / 0.724 ms/局 | **≈3.1×** |
| 对拍全管道合计 | 2.571 / 2.615 ms/局 | 1.062 / 1.076 ms/局 | ≈2.4× |

64 号预验证给的是 2.06×（87 场旧固件、插桩期机器）；本轮固件多了 65 号的 5 场、机器更静，
方向一致、幅度更大。**2025 整包端到端同口径 1.90×**（下）——重放之外解压与解析不动。

### 5.2 整年扫描吞吐（4 进程 `nice -n 19`，2025 整包 1,893,891 局）

| | 墙钟 | 合计吞吐 | 每进程 |
|---|---:|---:|---:|
| 改前 | 1918 s（32.0 min） | 987 局/s | 247 局/s |
| 改后 | **1011 s（16.9 min）** | **1873 局/s** | 468 局/s |

（64 号 2 进程量到 326 → 673 局/s/进程；4 路共享同一 zip、本机有轻负载，每进程低一截，
改前改后之比 1.90× 与 64 的 2.06× 同量级。）

### 5.3 `dotnet test`（5 轮交错，`--no-build`）

| | 墙钟（中位 / 区间） | 用例 duration 之和（中位 / 区间） | 用例数 |
|---|---|---|---|
| 改前 | 13.55 s（12.49–14.02） | 110.6 s（101.5–112.5） | 831 |
| 改后 | **9.96 s**（9.58–11.81） | **77.1 s**（68.0–91.2） | 834 |
| 降幅 | **−26%** | **−30%** | +3 |

`user+sys` 的 CPU 记账在改前树上偶发只记到宿主进程（testhost 的 CPU 没被 wait 回来，
build-server 关掉也一样），因此以 duration 之和为准；CPU 记全的那几轮是
157.9 s → 102.8 / 100.0 s（≈ −35%），与 duration 之和同向。

**最吃向听的那几个模块（duration 之和中位，秒）**：

| 模块 | 改前 | 改后 | 降幅 |
|---|---:|---:|---:|
| PaifuDifferentialTests | 10.9 | 8.0 | −27% |
| MaskedStreamProperties | 8.5 | 5.5 | −35% |
| SoakTests | 7.9 | 5.5 | −31% |
| RareSoakTests | 7.5 | 3.3 | −55% |
| ObservationProperties | 7.0 | 3.3 | −53% |
| KanProperties | 6.3 | 3.8 | −40% |
| MinogashiStreamProperties | 6.1 | 4.1 | −32% |
| DecisionPackageProperties | 6.0 | 4.2 | −30% |
| RiichiProperties | 5.4 | 3.8 | −30% |

64 号可证伪清单第 5 条要求 `PaifuDifferentialTests` 缩 ≥30%，实测 −27%——差 3 个百分点，
原因是它的墙钟里解析、oracle 对拍与比对不走重放路径（`paifu-cost` 拆出它们占 ~11%），
实测值按清单要求写回登记册。

### 5.4 `./scripts/ci.sh` 墙钟（**同一文件系统交错跑**，ws-b 内用临时 change 切回基点量改前）

| | 各轮 | 中位 |
|---|---|---:|
| 改前 | 34.16 / 36.05 s | 35.1 s |
| 改后 | 29.71 / 30.01 / 30.69 / 32.23 s | **30.4 s** |

**−13%（−4.7 s）**：`dotnet test` 那段省下的 ~3.6 s 全数体现；其余 ~25 s 是 web 侧十三道
与构建的固定成本，不在本票射程。（一开始拿 /tmp 树副本比出「改后反而慢」，查明是 tmpfs
对 web 工具链不公平，弃掉重量——判据 16 的形状。）

### 5.5 18 包全扫预估（更新进登记册）

18 包 ≈ 2600 万局（64 号票面）。按本轮 2025 整包**实测吞吐**线性外推：

| 配置 | 改前 | 改后 |
|---|---:|---:|
| 4 进程礼貌模式（实测吞吐） | ≈ 7.3 h | **≈ 3.9 h** |
| 32 核全开（按每进程吞吐线性外推，未实测 32 路扩展性） | ≈ 55 min | **≈ 29 min** |

64 号预估的「改后 4 核 2.7 h / 32 核 20 min」用的是 2 进程安静机器的每进程吞吐（673 局/s）；
本轮 4 路共享 zip 实测每进程 468 局/s，外推数字相应上调。两套数的口径差写进登记册。

## 6. code-review（Standards 轴，fixed point `855511db`；派不出 sub-agent，按 workbook 自跑）

依据 `docs/agents/fsharp-style.md`、CONTEXT.md/ADR-0001、`scripts/check-style.sh`。

| # | 结论 | 说明 |
|---|---|---|
| 1 | 通过（规则 4.1） | `Shanten.value (Shanten.calculateWith …) > 0` 与 `Shanten.value (Shanten.calculate …) <= 0` 是两层「取值器进比较」，boolean 条件里保持两层是明写允许的（同形先例就在 `tenpaiDahai` 里） |
| 2 | 通过（限制 B：短路） | `canDeclare` 的 `&&` 链没抽 `let`：`hasTenpaiDahai` 是函数调用，非门清在第一子句就断，向听算不到 |
| 3 | 通过（规则 5） | **没有新增 `let mutable`**（预算仍 2，风格闸门绿）；E2 复用批内既有的原地读写，闸本身是纯组合 |
| 4 | 通过（ADR-0001 标识符） | 新名字只有 `hasTenpaiDahai`（module 私有），罗马字术语 `tenpai`/`dahai` 沿用现有 `tenpaiDahai` 的词面 |
| 5 | 通过（判据 2：谁执行等价性） | 两条等价性各有常驻属性测试执行（每 CI 300 例），且各 live-fire 红过一次（§3）；不是只活在注释里的声称 |
| 6 | **已在 review 中修** | `hasTenpaiDahai` 注释里「逐値等价」用了日文「値」，改「值」；修完重跑全量绿 |
| 7 | nitpick（只记录不修） | 两条：`waits` 的文档注释已有三段加粗要点、本票又加一段，偏长（权衡后保留——剪枝的正当性证明必须留在改动现场，55 号同样形状）；`hasTenpaiDahai` 的 `match HandShape.create` 形状与同文件 `tenpaiDahai` / `waitsOf` 是第三份同构（Duplicated Code 的 judgement call：三行 match 抽成组合子反而晦，不动） |

Spec 轴逐条即本报告 §1–§5（票面每个勾框都有对应小节），无 blocking 项。

## 7. 我没做的，与为什么

1. **重放专用 step / `Phase.Actions` 惰性化 / 段级缓存 / 缓冲上提**——票 64 研究 §8 的
   被否决项，随票引用，本票一格没碰。
2. **`KanProperties` 生成器「到不了连杠夹欠账」的显式注释**（64 报告待审项 2 说可并进本票）
   ——没做：`KanProperties.fs` 不在本票「判定路径与对应测试」的改动面里，登记册「未答」
   已经记着这条显式事实，留给调度器裁。
3. **降任何用例数、放宽任何断言**——一条没有；831 → 834。

## 8. 复现

```bash
# 改前树 = 基点 855511db 的 rsync 副本（/tmp/janpo-66-before），Release 构建
# 2025/2026 整年重扫（两树各跑，diffs 逐字节比对、progress 去耗时列比对）
python3 scripts/paifu/zip-index.py /home/xerxes2/janpo-corpus/2025.zip > /tmp/janpo-66/index-2025.tsv
for s in 0 1 2 3; do nice -n 19 dotnet fsi --exec scripts/fsi/paifu-scan-zip.fsx -- \
  /home/xerxes2/janpo-corpus/2025.zip /tmp/janpo-66/index-2025.tsv <输出目录> $s 4 & done
cmp <(cut -f1-11,13- before/progress-0-of-4.tsv) <(cut -f1-11,13- after/progress-0-of-4.tsv)
# soak 事件流（四种选手 × 种子 1–500，脚本原文在本报告同名 /tmp 目录，形状：逐种子 janpo game）
# 引擎重放成本
nice -n 19 dotnet fsi --exec scripts/fsi/paifu-cost.fsx -- \
  tests/Janpo.Engine.Tests/bin/Release/net10.0/fixtures/paifu 111 3
# dotnet test 计时
/usr/bin/time -v nice -n 19 dotnet test janpo.slnx -c Release --no-build --logger trx
```

探针与 /tmp 树副本都是易失的；仓库 diff 只有 `src/Janpo.Engine/` 2 文件 + 测试 2 文件 +
文档（票、报告、登记册、DECISIONS）。

## 9. 留给人的待审项

1. **`PaifuDifferentialTests` 缩 27% 差 3 个百分点到 64 号清单第 5 条的 ≥30% 线**——
   实测已按清单要求写回登记册；要不要就此把清单那条的外推口径修一句，留人裁。
2. **18 包全扫的两套预估口径**（64 的 2 进程外推 vs 本轮 4 进程实测）已并列写进登记册；
   真要跑 18 包时以实测为准。
3. `KanProperties` 注释那条一行活（§7.2）仍然悬着，归属未定。
