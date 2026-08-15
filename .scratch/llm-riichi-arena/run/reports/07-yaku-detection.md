# 07 — 役种判定 · 实现报告

**Status:** done（票已标 `ready-for-human`）
**工作区:** `/home/xerxes2/janpo-ws-b`
**Fixed point:** `067b79e4`
**验证:** `./scripts/ci.sh` 全绿（269 个测试通过、`fantomas --check` 干净、引擎依赖白名单通过、0 警告）

---

## 公开签名

```fsharp
// Mentsu.fs —— 面子：和了形的一块
type MentsuKind = Shuntsu | Koutsu | Kantsu
type Mentsu                                                  // 私有记录
Mentsu.shuntsu : bool -> Tile -> Mentsu option                // 代表牌是最小的一张，只能 1-7
Mentsu.koutsu / kantsu : bool -> Tile -> Mentsu               // 第一个参数是「暗不暗」
Mentsu.withConcealed : bool -> Mentsu -> Mentsu
Mentsu.kind / tile / isConcealed / tiles / contains / toDisplay

// Naki.fs —— 副露：10（碰吃）与 11（三种杠）共用
[<RequireQualifiedAccess>] type NakiKind = Pon | Chi | Ankan | Minkan | Kakan
type NakiError = NakiConsumedCountMismatch | NakiTilesNotSameKind | NakiTilesNotRun | KakanBaseMismatch
type Naki                                                     // 私有记录，字段贴 mjai wire
Naki.pon    : Seat -> Tile -> Tile list -> Result<Naki, NakiError>   // target -> pai -> consumed
Naki.chi    : Seat -> Tile -> Tile list -> Result<Naki, NakiError>
Naki.ankan  : Tile list -> Result<Naki, NakiError>                   // 4 张，无 target
Naki.minkan : Seat -> Tile -> Tile list -> Result<Naki, NakiError>
Naki.kakan  : Tile -> Naki -> Result<Naki, NakiError>                // 加的那张 + 原本的碰
Naki.kind / taken / consumed / target / tiles / isKan / isConcealed / mentsu / toDisplay

// AgariHand.fs —— 和了的牌姿
type AgariHandError = AgariNakiCountOutOfRange | AgariConcealedCountMismatch
                    | AgariKindOverflow | AgariTileNotInHand
type AgariHand                                                // 私有记录
AgariHand.tsumo / ron : Naki list -> Tile list -> Tile -> Result<AgariHand, AgariHandError>
AgariHand.concealed / naki / winningTile / isTsumo / isMenzen / tiles / toHandShape / toDisplay

// MentsuBreakdown.fs —— 一种面子分解
type WaitKind = Ryanmen | Penchan | Kanchan | Shanpon | Tanki
type MentsuBreakdown                                          // 私有记录
MentsuBreakdown.enumerate : AgariHand -> MentsuBreakdown list  // 全部分解，去重；非一般型为空
MentsuBreakdown.mentsu / jantou / wait / toDisplay

// Dora.fs —— 宝牌只计番，不是役
Dora.ofMarker     : Tile -> Tile
Dora.count        : Tile list -> Tile list -> int             // 指示牌 -> 牌 -> 番
Dora.akadoraCount : Tile list -> int

// Yaku.fs —— 本票的主函数
[<RequireQualifiedAccess>] type RiichiDeclaration = None | Riichi | DoubleRiichi
[<RequireQualifiedAccess>] type Yaku = Riichi | ... | Chiihou          // 43 个 case
[<RequireQualifiedAccess>] type YakuValue = Han of int | Yakuman of int
type YakuContext = { Bakaze; Jikaze; Riichi; Ippatsu; Rinshan; Haitei; Houtei
                     Chankan; Tenhou; Chiihou; DoraMarkers; UraDoraMarkers }
type YakuTally   = { Shape: AgariShape; Breakdown: MentsuBreakdown option
                     Yaku: (Yaku * YakuValue) list; Dora: int; Uradora: int; Akadora: int }
type YakuError   = NotAgari | NoYaku

Yaku.detect     : Ruleset -> YakuContext -> AgariHand -> Result<YakuTally, YakuError>
Yaku.candidates : Ruleset -> YakuContext -> AgariHand -> YakuTally list   // 全部读法，高在前
Yaku.value      : bool -> Yaku -> YakuValue        // 第一个参数是门清与否（食い下がり）
Yaku.isYakuman / isMenzenOnly / name / toDisplay
YakuContext.create : Kaze -> Kaze -> YakuContext   // 标志全关，调用方用 with 打开
YakuTally.han / yakuman / doraTotal / yaku / toDisplay
```

CLI：`janpo yaku --win <记法> [选项] <暗牌记法>...`，退出码 0（判定成功）/ 1（牌姿非法或不可和）
/ 2（参数错）。输出 `shape` / `yaku`（罗马字标识符，空格分隔）/ `han` / `yakuman` /
`dora ura aka` / `display`（中文）。08 与 13 票可以直接拿它对拍。

## 做了什么

1. **副露与面子的公共表示**（`Mentsu` / `Naki`）——票里最大的设计点。`HandShape` 只有副露**数**，
   而三色同刻、一気通貫、対々和、门清限定役与副露降番都要副露的**内容**，因此新增了这一层，
   并按「10 与 11 会复用」来设计：`Naki` 的字段与 mjai 的 `pai` / `consumed` / `target` 1:1，
   加杠由「原碰 + 加的那张」构造，因此它必然记得原碰的来源（11 的责任支付要用）。
2. **面子分解的全枚举**（`MentsuBreakdown`）——雀头 × 面子拆法 × 和了牌落点，去重后返回一组。
   和了牌落点决定听牌型，也决定**荣和补上的刻子按明刻算**（三暗刻 / 四暗刻 / 符都读这一位）。
3. **役种判定**（`Yaku`）——43 个役与役满，门清限定与副露降番、上下文标志、宝牌、食断开关、
   无役即不可和。多分解与「同时是七对子又是二盃口」都走 `candidates` 排序取高。
4. **食断开关**落在 `Ruleset.Kuitan`（唯一一处改到别票文件的地方，纯增字段）。
5. `janpo yaku` 子命令 + 6 个测试文件（黄金用例、副露/牌姿/宝牌用例、FsCheck 不变量）。

## 关键取舍

- **不动 `HandShape` / `AgariShape`**：形态判定仍然是 03 的那一份，本票在其**之上**加类型。
- **`YakuTally` 带着选中的 `Breakdown`**：08 算符要的面子、雀头、听牌型全在里面，不必重算一遍分解。
  同番时的排序用一个私有的「近似符」函数（只算随分解而变的三项），08 可以拿 `candidates` 自己再选。
- **不做双倍役满**（天凤官方手册明确「役满复合有、双倍役满无」，而 13 票的对拍源就是天凤牌谱），
  但十三面 / 四暗刻单骑 / 纯正九莲各留一个 case，因为天凤的役表把它们分开列。
- **`Yaku` 用 `[<RequireQualifiedAccess>]`**：43 个 case 里 `Chiitoitsu` / `Kokushi` / `Tenhou`
  都会与既有或将来的名字撞车。
- 详见 `run/DECISIONS.md` 的 07 段（10 条决策 + 提案 07-A）。

## 测试

269 个测试（本票新增 168 个断言块中的 40 个 `[<Fact>]` 与 7 条 FsCheck 属性）。

- **每个役至少一条黄金用例**：43 个 `Yaku` case 全部有具名用例。
- **互斥与升级**：一杯口 / 二杯口（且二盃口压过七对子）、三色同顺 / 一気通貫（在四面子里结构上不可能同时成立）、
  国士 / 十三面、四暗刻 / 四暗刻单骑 / 三暗刻（荣和降级）、九莲 / 纯正九莲、混老头 / 混全带幺九。
- **复合**：大四喜 + 四暗刻单骑、天和 + 国士十三面、小三元 + 混一色 + 役牌、对对和 + 三暗刻 + 混老头。
- **上下文**：一发要以立直为前提、海底要自摸、河底要荣和、岭上要自摸、抢杠要荣和、天和只能自摸。
- **食断**：同一副牌门清成立、副露且开关关掉时判 `NoYaku`。
- **属性**：能和必有一番或一倍役满、门清限定役不出现在副露手、`detect` 选中的读法番数最高、
  宝牌指示牌不影响役集合、役满时役表全是役满、每种分解都用光手上的牌、
  `Shape = Standard` 与 `Breakdown` 非空一一对应。

## 两轴 review 结论（自跑，fixed point `067b79e4`）

**Standards**（AGENTS.md / CONTEXT.md / ADR-0001..0003 / 01-03 的 DECISIONS 约定 + Fowler 气味基线）

- 修掉 1 处：`NakiKind → 中文` 的 match 在 `Naki.toDisplay` 与 `NakiError.toDisplay` 里各写了一遍
  （Duplicated Code / Repeated Switches）→ 提成 `NakiKind.toDisplay`，10/11 的 UI 也能直接用。
- 记录不修（判断题）：
  - `Yaku.fs` 约 880 行、含 6 个类型 4 个模块，是全仓最大的文件（Divergent Change 的味道）。
    拆不开的硬原因：F# 不允许同一命名空间里出现两个同名模块，`Yaku.detect` 必须与 `type Yaku` 同文件。
    文件内按「番数表 / 谓词 / 场况役 / 一般型 / 七对子与国士 / 结果 / 标识符 / 渲染」分段。
  - CLI 的 `runYaku` 参数解析约 110 行（Long Function）。与既有 `runDeal` 同风格，只是选项多。
  - `Yaku.value` 用 `bool` 表示门清（Primitive Obsession），F# 里这是惯用写法，且 `AgariHand.isMenzen`
    是它唯一的来源。
- 合规检查：一概念一文件、`namespace Janpo`、fsproj 按依赖顺序、类型与同名 `[<RequireQualifiedAccess>]`
  module 同文件、渲染出口一律 `toDisplay` 且**中文只在那里**（已 grep 全部字符串字面量确认）、
  错误是值 `Result<_, 具名 DU>`、测试名中文且只测公开 API、CLI 子命令加在 `main` 的 match 上、
  引擎无新依赖 — 全部通过。

**Spec**（票 07 的 7 条验收 + `spec.md` 的范围段落）

- 7 条验收全部落地并有用例（见上）。
- 缺口（有意，已记 DECISIONS）：流し満貫（12 票）、人和（非通行役）、双倍役满（天凤无）、
  役满的包牌与点数（08 / 11）。
- 越界检查：`Naki` 带 `Taken` / `Target`（票里明确要求「按 10/11 会复用的公共类型设计」）、
  `janpo yaku`（派工简报明确建议）、`Dora`（票里的「宝牌计番」）、`fuLikeness`（高点法的必要条件，
  私有且不外露符值）——都不算 scope creep。**没有**碰符、点数、事件与状态机。

## 留给人的待审项

1. **提案 07-A**：`Mentsu` / `Shuntsu` / `Koutsu` / `Kantsu` / `Jantou` / `Menzen` / `Yakuman` /
   `Han` / `Kuitan` / 五种听牌型进 `CONTEXT.md`。术语表现在只有 `Yaku` 与 `Fu`，08 与 10/11 会继续用这批词。
2. **`Ruleset.Kuitan`**：本票动了 02 的 `Ruleset.fs`（纯增字段 + `withoutKuitan`）。
   若早上把规则集重做成命名预设（提案 S-A），这个字段要一起搬。
3. **双倍役满**：现在按天凤（全单倍）。若要支持雀魂段位战，`Yaku.value` 的役满分支改一处，
   外加一个 `Ruleset` 开关。
4. **同番时的高点法 tiebreak**：07 只能用近似符排序。08 落地真正的符之后，建议由 08 在
   `Yaku.candidates` 上按自己的符规则重选一次，并在那时决定是否把 `fuLikeness` 删掉。
5. **上下文标志的自洽性不由本票保证**：海底与岭上同时为真、一发与副露同时为真这类组合，
   引擎只做了「一发要以立直为前提」「海底/岭上只在自摸时成立」「河底/抢杠只在荣和时成立」
   三道防御，其余由 04 / 09 / 11 / 12 在产标志时保证。
