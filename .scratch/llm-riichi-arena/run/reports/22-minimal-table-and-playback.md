# 报告 22 — 最小牌桌与播放控制

**状态**：done　**change**：`rvwmxlzo`（本票一 commit）　**fixed point**：`78116659`（change `ymkmmtxt`）
**工作区**：`janpo-ws-b`

## 做了什么

`src/Janpo.Web` 里加了四个文件，19 票的曳光弹页面原样留着（它是 CI 对拍的依赖），
牌桌与它同页共存：

| 文件 | 是什么 | 有测试吗 |
|---|---|---|
| `Playback.fs` | 播放控制的状态机（播 / 暂停 / 倍速 / 世代号），**纯值** | 9 条 |
| `Table.fs` | 牌桌的推进：决策 → 落子 → 一局终了收进 `Game` | 11 条 |
| `Board.fs` | 投影选择（`Observation` ↔ `GodView`）与结算显示 | 11 条 |
| `TablePage.fs` | Feliz 的 MVU 三件套与视图（无单测，靠无头验收） | — |

新工程 `tests/Janpo.Web.Tests`（已进 `janpo.slnx`）：31 条用例，0.4 秒。
前三个文件一行 Feliz 都不 `open`，所以在 dotnet 上跑得起来——**要 open Feliz 就说明那段逻辑放错文件了**。

引擎动了两处（都很小、都是加法）：

- `MaskedSeat.HandCount`：他家手牌**张数**（公开信息）。不加的话渲染层得自己按
  `13 - 3×副露数` 推，那是把规则搬到渲染层，且摸牌那一手会差一张。wire 上叫 `tehai_count`。
- `RyuukyokuReason.toDisplay`：流局形态的中文，照 `Tile` / `Kaze` / `Naki` / `Action` 的既有约定
  放在类型旁边。七种形态的措辞逐字照 `CONTEXT.md`。

## 关键取舍（完整清单在 `DECISIONS.md` 的「## 22」段）

1. **上帝视角开关切的是消费哪个投影**，不是渲染时判断要不要画手牌。他家那条路
   （`MaskedSeat` → `HandView.Concealed`）在类型上就产不出牌，因此「关掉后 DOM 里也不留他家的牌」
   是结构性的。M3 的真人坐席复用「坐在某个座位上看」这条路径。
2. **一步一 Msg**：`Advanced`（单步）与 `Ticked`（定时器）各推进一手，`update` 里没有第二个 loop；
   下一记定时器由 `schedule` 在这一手结束时续上。
3. **牌桌驱动一整场**，一局终了停下来等「下一局」——渲染项里的本场与供托在单局里恒为 0。
   连庄 / 本场 / 供托的结转全走 `Game.advance`，牌桌一条规则都不判。
4. **播放控制用世代号**，不存 timeout 句柄：过期的定时器回来时按世代号丢掉，
   于是「暂停再播」「连点加速」都不会让牌桌跑双份。这条有专门的用例。
5. **默认种子取 2088，不取曳光弹的 1177**：曳光弹把原始 mjai 事件打在同一张文档里，
   `start_kyoku` 带着四家配牌，同种子的话牌桌遮起来的牌就在下面躺着。

## 无头证据：真的能看完一个 Kyoku

跑法（脚本随报告一起放在本目录，**没有动 `web/scripts/`**——那是 21 票的地盘）：

```
cd web && pnpm run build
cd web && node ../.scratch/llm-riichi-arena/run/reports/22-evidence-dump.mjs      # 主证据
cd web && node ../.scratch/llm-riichi-arena/run/reports/22-evidence-features.mjs  # 杠与立直棒
```

三张截图：`22-table.png`（默认种子 2088 打完一局，上帝视角）、`22-table-kan.png`（暗杠与大明杠的形态）、
`22-table-riichi.png`（立直棒与供托）。DOM 摘要：`22-table-dom.json` / `22-table-features.json`。
两次跑，`pageerror` 与 `console.error` 都是**空的**。

DOM 摘要（`22-table-dom.json`，`handPai` = 该家手牌里有 `data-pai` 的元素数，即真的画出了牌面的张数）：

| 时刻 | 场况 | 座位 0 | 座位 1 | 座位 2 | 座位 3 |
|---|---|---|---|---|---|
| 开局（坐在座位 0） | 东1局 0 本场，剩 69 张 | 手牌 14 面 | 13 背 | 13 背 | 13 背 |
| 上帝视角 | 同上，剩 67 张 | 13 面 | 13 面 | 14 面 | 13 面 |
| 坐到座位 2 | 同上 | 13 背 | 13 背 | **14 面** | 13 背 |
| 这一局打完 | 剩 11 张，宝牌指示牌 2 张（有杠） | 副露 碰碰碰吃、河 17（摸切 5） | 副露 **加杠**吃碰 | 副露 吃吃 | 副露 吃 |
| 下一局 | **东1局 1 本场**，点数 27000/25000/25000/23000 | 手牌 14 | 13 | 13 | 13 |

- **他家的牌在 DOM 里真的不存在**：坐着看时非自家的 `handPai` 恒为 0（只有 `.back`，那些元素连
  `data-pai` 都没有）。切上帝视角四家全变 14/13 面。
- **手切与摸切分得开**：`[data-tsumogiri="true"]` 逐家可数（上表「摸切 5」等），画面上是虚线加淡色。
- **杠看得出形态**：`22-table-features.json` 里暗杠是「2 张背 + 2 张面」，大明杠 4 张全亮，
  加杠 4 张一组；每组另有一枚中文标签（`NakiKind.toDisplay`）。
- **立直棒与供托**：种子 343 的东 2 局 1 本场，供托「1 根」、场况栏画出 1 根棒，
  那一家挂着「立直」「一发」两枚标记。
- **结算**：`和了 / 座位 0 荣和 座位 3 打出的 7筒 / 役：役牌 中 1 番 / 40 符 1 番 2000 点 /
  点数授受：座位 0 +2000　座位 1 0　座位 2 0　座位 3 -2000 / 亲连庄`。
  流局那一份（种子 106）是`荒牌流局 / 听牌：无 / 点数授受 全 0 / 亲流局，进下一局`。

## 闸门

`./scripts/ci.sh` 全绿：fantomas `--check` 干净、`scripts/check-style.sh` 通过、
dotnet 测试 589 + 31 条、Biome 干净、Fable + Vite 出包、**19 票的浏览器内曳光弹对拍照旧逐项相同**
（种子 1177：scores / juni / kyokus 三行都对得上——牌桌与它同页并没有影响它）。

## code-review 两轴（无法派生 sub-agent，顺序自跑；fixed point `78116659`）

### Standards

- **通过**：`docs/agents/fsharp-style.md` 的规则 1/2/3（无从里往外读的嵌套）、规则 8（多余括号）
  由 `check-style.sh` 锁零；ADR-0001（人类可读形式只在渲染层，`data-pai` 上仍是 mjai 记法）；
  ADR-0005（不写 `.d.ts`、不碰 TS、Fable 运行时后端只在 Web 工程——`ci.sh` 的引擎依赖闸门通过）。
- **判断题（记录，未改）**：
  1. `TablePage.fs` 约 600 行，MVU 与整个视图在一个文件里（App.fs 是同一形状的先例）。
     真要拆，缝在「视图函数」与「MVU」之间。
  2. `Board.ofHora` 里 `Option.map … |> Option.defaultValue` 连着五处，读法缺失时全部退默认值；
     可以先 `match reading with` 一次分两支。功能上等价。
  3. `Board.ofRevealed` / `ofMasked` 有 7 个字段逐字重复——这是两个源类型换同一个装的必然代价，
     合并就等于把「他家没有手牌字段」这条结构性保障拆掉。**不改**。

### Spec（票 22 + 调度器的「要害」）

- 八条验收框逐条对上（证据见上一节），术语与中文逐字照 `CONTEXT.md`。
- **越界候选（判断为合理，已记 DECISIONS）**：驱动一整场（票只要求一个 Kyoku）与终局精算面板；
  引擎的两处加法。前者是「本场 / 供托」这两条渲染项逼出来的，后者是「各家手牌数」与
  「流局的可读结算」逼出来的。
- **未做（票没要求）**：动画、Canvas、配桌表单、点击某家展开（视角切换用的是按钮）、
  URL 分享（ADR-0002 的分享是 M2）。
- 没有发现 blocking 项，因此**没有触发自动修复轮**。

## 留给人的待审项

1. **提案（22-7 的括号段）**：19 票的曳光弹页面会在 DOM 里印出它自己那一局的 `start_kyoku`，
   里面带四家配牌。它是开发用诊断件、且 CI 依赖它，本票不动；M2 把首页做成 Demo Paifu 时
   应当把它挪到只在 dev 构建里挂的路由。
2. `MaskedSeat.HandCount` 是往 20 票的投影上加字段（wire 多一个 `tehai_count`）。
   决策理由见 22-2，如果人更愿意让渲染层自己推，改回来只需删一个字段加一处减法。
3. 播放的四档间隔（600/300/150/60 ms）是拍脑袋定的：1× 看完一局约 40 秒。要改改 `Speed.interval`。

## 给 23 票（LLM 座位）的接口事实

- 牌桌的形状：`Table = { Game; State; Players: Rng; Readings; Latest; Fault }`，
  局面**只有** `State` 一份。
- 推进一手的缝在 `Table.decide`（问谁要动作）与 `Table.apply`（落子）之间，`Table.advance` 只是把两半接起来。
  换异步座位改前一半：按 `(Table.pending table).Value.Seat` 分派，随机座位仍走 `Kyoku.randomPlayer`，
  LLM 座位改成发一个请求、动作由一条新的 Msg 带回来再交给 `apply`。**`apply` 不用动。**
- 页面侧一步一 Msg：`Advanced`（单步）与 `Ticked generation`（定时器）。异步座位回来那条 Msg
  与 `Ticked` 共存的办法：等待期间 `Playback` 保持 `Playing`，但**不要续定时器**——
  收到动作、`apply` 完之后再 `schedule`，否则牌桌会在选手还没答复时空转。
- 决策包（20 票）从 `DecisionPackage` 出，不从 `Board` 出：`Board` 是给人看的换装，
  跨界给 TS 的仍是决策包 JSON + 动作 id（ADR-0005 的边界）。
