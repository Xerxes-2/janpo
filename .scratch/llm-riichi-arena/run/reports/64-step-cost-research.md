# 64 票 —— 研究：重放路径上 `GameState.step` 的 37µs 花在哪（第 6 轮）

**STATUS: done**（只量不改：`src/` 零改动，全部实验在 `/tmp/janpo-64{,-clean}` 树副本；
产出 `docs/research/step-cost-on-replay-path.md` + 登记册第 6 轮一行 + 「未答」两条更新）

一句话结论：**37µs 里 55% 是打牌后的振听簿记（`waits` 34 次试摸，83.6% 的手不听牌、全是白跑）、
17% 是每手的立直合法性逐张试打；两条「先花一次向听计算挡掉」的等价性捷径在 /tmp 预验证通过
——引擎重放 2.06×、129,179 局重扫差异输出逐字节相同、721 测试全绿、20 万手采样零反例。
重放确实多付了「动作集只被 `List.contains` 消费」的 ≈30%，但重放专用道判不做：
两笔大头在对局路径上同样是白花的，修引擎本体两边都受益。基线诚实摆第一行：
32 核全开不改代码 18 包 ≈42 分钟，优化非必需、但便宜到值得一张小实现票（草案在研究文档 §11）。**

## 做了什么

1. `/tmp/janpo-64`（插桩副本）：`GameState.fs` 各段挂 `Stopwatch` 累加器、
   `Shanten`/`AgariShape`/`HandShape` 挂裸计数器、`PaifuReplay.submit` 按「阶段 × 动作类型」
   计时每次 `step`；`/tmp/janpo-64-clean`（干净副本）出未插桩锚点。全部 Release。
2. 87 场 / 893 局固件量拆解表；60.3 次 step/局复核票 62 的 61 次/局；
   打牌一种动作占 step 时间 92%。
3. **消费者分析**回答「重放与对局是不是同一份钱」：`refreshFuriten` 与 `responsesTo`
   语义承重（振听 / 见逃簿记），`awaitDahaiIn` 的动作集在重放侧只被 `List.contains` 消费。
4. **实验 E2**（`AgariShape.waits` 向听前置闸）与 **E3**（`RiichiState.canDeclare`
   单次向听 ≤ 0）在 /tmp 副本实装并三道闸验证（721 测试、固件对拍差异 0、
   2026 整包重扫差异与覆盖断点逐字节相同）。等价性命题 P1/P2′ 各 20 万手采样零反例
   ——P2 第一版（`= 0`）被采样当场否掉（反例全是向听 −1 的已和了形手），修正成 `≤ 0`。
5. **live-fire**（判据 1/3）：把 `completeKan` 的「杠成立先还欠账」故意弄坏，
   `KanProperties` 9 条**全绿**（生成器到不了连杠夹欠账），`KanTests.连着两次明杠` 与
   `PaifuDifferentialTests` **当场红**；据此给 59 号转来的 `pendingKanDora ≤ 1` nitpick
   下了「不值得单独加强」的结论。弄坏后已复原并重跑绿。
6. 测量窗口撞上 ws-a 的 4 分片扫描一次（判据 16），撞上的几趟全部弃掉、等安静后重跑。

## 关键取舍

- **重放专用 step / `Phase.Actions` 惰性化：判不做**。理由三条（顶撞「回放就是 step」的
  设计决定、Fable 下 `lazy` 要重新验证、只拿得到 ≈30% 且两笔大头拿不到），
  连同其余四条被否决项都在研究文档 §8。
- **票 55 手法不照搬**：34 长数组 882 个/局折合 ~60MB/s 分配率，.NET gen0 的零头；
  批内共用（`ShantenScratch`）早已就位。churn 不是 .NET 侧的杠杆。
- 预验证做到了「重扫逐字节相同」而不是只跑测试：登记册规矩 5 的口径，
  实现票落地时还要再扫 2025（可证伪清单第 3 条）。

## 复核（Standards + Spec 两轴，自查）

- Spec：票面六个问号逐条有数字或有判定；三条边界（不碰 63 地盘、语料只读、旧数字标出处）遵守；
  `src/` 与 ws-b 内除文档与票面外零改动。
- Standards：改动全是 Markdown，`dotnet fantomas .` 无事可做；`./scripts/ci.sh` 全绿（见简报）。
- 无 blocking 项；nitpick 无。

## 留给人的待审项

1. **E2+E3 实现票立不立**（研究文档 §11 草案，票号待分配）：不是全扫可行性的必需品
   （32 核 42 分钟已够 routine），但两行改动买 2.06×、CI 与产品路径顺带受益。
2. KanProperties「生成器到不了连杠」要不要按判据 4 在测试注释里写成显式事实（一行的事，
   可并进实现票）。
3. 登记册「未答」里第 5 轮那张 `forSeatWith` 实现票仍然悬着（与本轮无关，原样保留）。

## 附：实验脚本（/tmp 副本用，未进仓库）

等价性采样探针（E3 的核心证据，`step-equivalence-probe.fsx` 要点）：

```fsharp
// P1：13−3n 张手：Shanten=0 ⟺ ∃kind classify(hand+kind)≠[]（暴力侧不经过被测的 waits）
// P2′：3n+2 手：tenpaiDahai≠[] ⟺ Shanten≤0（naki 0–3）
// 生成：全池 / 限 2 花色 / 1/4 概率强插四张同种（死听边界）；System.Random 64；各 200,000 手
let bruteWaits nakiCount hand =
    TileKindSet.kinds kindSet |> List.exists (fun kind ->
        held kind hand < 4
        && (match HandShape.create nakiCount (kind :: hand) with
            | Ok shape -> AgariShape.classify kindSet shape |> List.isEmpty |> not
            | Error _ -> false))
```

段级插桩点（`/tmp/janpo-64/src/Janpo.Engine/GameState.fs`）：`step.total`、`refreshFuriten`
（`applyDahai` 里 `discard >> refreshFuriten` 拆开计时）、`awaitDahaiIn` 与其内部
`horaCheck`/`canRiichi`/`tenpaiDahai`/`ankan`/`kakan`/`kyuushu`、`responsesTo` 与其内部
`canRon`（再拆 `isAgariWith` 预判与全量 `Score.best`）、`yakuContext`/`firstTurnFor`/
`validate.*`/`acceptRiichi`/`ryuukyokuAfterDahai`/`applyHora`/`exhaustive.tenpai`/`step.logAppend`。
外层在 `PaifuReplay.submit` 按 `act.<D|Rmid|Rlast>.<动作>` 记账。复现命令见研究文档附节。
