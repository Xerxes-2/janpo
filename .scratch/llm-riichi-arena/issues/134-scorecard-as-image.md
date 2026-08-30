# 134 — 记分卡出图：一张能贴出去的 PNG

**What to build:** 票 133 那张记分卡再出一张 PNG（贴 X / 贴群那种）。评审 P0-A 的后半句。

**Blocked by:** 133

**Status:** ready-for-agent

## 为什么单独一票

- 133 是「有没有可比的表」，134 是「这张表能不能离开浏览器」。两件事各自验收得了。
- 出图这条路**整个工程里今天不存在**：`Download.fs` 只有 `Download.json` 一条（票 26 定的
  「唯一碰下载 API 的地方」）。要么在那一处加 `Download.png`，要么就别加——
  **不许在第二个文件里再写一段 `URL.createObjectURL`**。

## 要什么行为

- [ ] 终局那一屏「复制记分卡」旁边多一枚「存成图」，出一张 PNG
- [ ] 画法走 canvas，**不引第三方截图库**（`scripts/check-third-party.sh` 那条规矩）
- [ ] 图里逐格与 133 那张表同源：同一份 per-seat 记录喂两处，不许各画各的
- [ ] **图里不许出现 key**，也不许出现 baseUrl 与档案名之外的任何档案字段；
      闸门要真去读那张图（把 canvas 的像素或 dataURL 取回来核，不是核一句代码注释）
- [ ] `Download` 那一层仍是唯一碰下载 API 的地方

## 顺带做掉的一件（调度器 2026-08-30 裁，从票 145 转来）

**`SeatTally.HoraTargeted` 改名 `Houjuu`。** 133-2 当初起 `HoraTargeted` 这个名字的原话理由是
「术语表里没这个词条」，而票 145 已经把 `Houjuu（放铳）` 收进 `CONTEXT.md` ⇒ **那条理由过期了**
（判据 15）。`CONTEXT.md` 是标识符的唯一权威（硬约束 5）。

- [ ] `SeatTally.HoraTargeted` → `Houjuu`；wire `hora_targeted` → `houjuu`；
      `data-hora-targeted` → `data-houjuu`；闸门里读它的地方跟着改
- [ ] **纯改名，一个行为都不许变**：改完那趟 `verify-scorecard.mjs` 的逐格对拍**逐格结果相同**
- [ ] 单开一张纯改名的票不值一次上下文，∴ 并在这一票里——但**先做改名、单独一个 commit**，
      再做 PNG，别把两件事揉进一个 diff

## 边界

- [ ] 不改 133 的表结构与聚合函数（要改就回到 133 去改，别在这一票里分岔）
- [ ] 不动引擎、不动牌谱格式
- [ ] 出图失败时页面明说一句原因，**不许静默**（同 ADR-0006 边界 2 的形状）
