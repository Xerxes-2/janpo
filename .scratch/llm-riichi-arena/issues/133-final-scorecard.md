# 133 — 终局记分卡：这一场谁打得怎么样

**What to build:** 终局那一屏给一张**四行的表**，四家逐列可比；再给一枚「复制记分卡」出一段纯文本。
来源是 2026-08-30 那份设计评审的 P0-A（`.scratch/llm-riichi-arena/run/reports/design-review-2026-08-30.md`）。

**Status:** ready-for-agent

## 现状（我核过的，不是评审那份稿子上的）

- 终局那一屏只有 `TableBoard.resultPanel`：一句 `GameResult.toDisplay`（顺位与终点）加一句供托归属。
  **没有任何逐席可比的东西。**
- `GameResult` 只有两项：`Scores`（按座位升序）与 `Juni`（名次，1 起）。
- 右轨那四行席位卡自票 124 起兼当记分板，**但它只有「风 · 名 · 点数」**，不含这一票要的那几列。

## 评审那份稿子有一句是错的，别照抄

稿子写「这几列全部已经在导出 JSON 里，一个新数都不用算」。**不成立**，我逐项核了：

| 列 | 真实来源 | 要不要新算 |
|---|---|---|
| 席位 / 风 | `Board.ofTable` | 否 |
| 顺位 / 终点 | `GameResult.Juni` / `.Scores` | 否 |
| 兜底手数 | `Paifu.Decisions` 里 `Fallback <> None` 的条数，按 `Seat` 分组 | **要**（纯聚合） |
| 重试次数 | 同上，`Attempts - 1` 求和 | **要**（纯聚合） |
| 输入 / 输出 tok | 同上，`Usage` 求和；**`Table.usage` 是整桌总额，没有按席分** | **要**（纯聚合） |
| 和了 / 放铳 | 只能走事件流（`Paifu.Events` 的和了事件：谁和、点谁的） | **要**（读规则才做得出，∴归引擎，判据 11） |
| 选手 · 档 | **牌谱里根本没有**：`Paifu` 只存 Ruleset / Events / Decisions / Prompting，模型名与 `ScaffoldTier` 都在 UI 侧（`SeatingPlan` / `TablePage.seatConfigOf` / `nameplates`） | 见下面那条边界 |

## 「选手 · 档」那一列的边界（这一票最容易做错的地方）

- **Live 那一桌有**：从 `TablePage.nameplates` / `seatConfigOf` 取，与名牌同源（不许第二份判据）。
- **回放那一屏没有**：首页的 Demo 与分享链接进来的牌谱里没有模型身份。
  那一列**写「牌谱没记」，不许猜、不许留白当成「随机选手」**——留白会被读成「这一席是 bot」。
- 别为了补这一列去改牌谱格式：那是另一张票，且要 ADR-0002 那一层点头。

## 要什么行为

- [ ] 终局那一屏（`table-result` 附近）多一张表，一席一行，列：席位·风 / 选手·档 / 顺位·终点 / 和·铳 / 兜底 / 重试 / 输入·输出 tok
- [ ] 那几个聚合是**引擎侧的纯函数**（吃一份 `Paifu`，出一条 per-seat 记录），dotnet 侧有用例钉住
- [ ] 一枚「复制记分卡」：把同一张表出成一段纯文本进剪贴板（贴 issue / 贴群那种）
- [ ] **纯文本里不许出现 key**：照抄票 34 那道检查的形状，闸门里要有一条「记分卡文本里搜不到 key」
- [ ] 无头闸门：终局之后表在、四行齐、每一格的 `data-*` 与引擎直接算的那份对拍（同 `verify-review.mjs` 的形状）
- [ ] 还没终局时**整块不在 DOM 里**（同 `ReviewShown.Hidden` 那条规矩）

## 边界

- [ ] 不动牌谱格式（`Paifu.Version` 一个字不涨）、不动 ADR-0002
- [ ] 不碰右轨那四行席位卡的形状（票 124/126 的地盘）
- [ ] 不做 PNG——那是票 134
- [ ] 不动 `Playback` 世代号、不加 localStorage 键
