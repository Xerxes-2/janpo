# 10 — Pon 与 Chi

**Status:** ready-for-human ｜ 全量测试 405 通过（基线 383，新增 22）｜ fantomas 干净 ｜ `./scripts/ci.sh` 全绿

## 做了什么

把响应阶段从「只有 Ron 与过」扩成「Ron / Pon / Chi / 过」，并接上副露之后的打牌阶段。
**不做杠**（11 票，含大明杠）、**不碰 Kyotaku 与跨局逻辑**（05 / 09 的地盘）、**不判役**（07 已做，
`Ruleset.Kuitan` 由它消费；本票只让副露发生）。

### 加动作的三处（备注 N-1 的契约）

| 处 | 位置 |
|---|---|
| ① `Action` 的 case | `Action.Pon of actor * target * pai * consumed`、`Action.Chi of 同形`（字段与 mjai wire 1:1） |
| ② 产出动作集 | `GameState.responsesTo` → 新增私有 `nakiActionsFor`（碰任何他家、吃只吃上家） |
| ③ `step` 的执行 match | `stepAwaitingResponse`（宣言）+ `applyResponse` 的裁决（`nakiWinner` → `applyNaki`） |

### 新增的公开接缝

```fsharp
Event.Pon / Event.Chi   : actor * target * pai * consumed   // mjai wire: {"type":"pon"|"chi",...}
PlayerState.naki        : PlayerState -> Naki list          // 公开信息：副露，按鸣的先后
PlayerState.nakiCount   : PlayerState -> int                // 形态判定要的副露数
PlayerState.kawaTaken   : PlayerState -> bool               // 河被鸣走过（12 票的 Nagashi Mangan 前提）
PlayerState.addNaki     : Naki -> PlayerState -> PlayerState
PlayerState.markKawaTaken : PlayerState -> PlayerState
GameState.junme         : Seat -> GameState -> int          // 该家摸过几次牌
IllegalAction.Kuikae / NakiTileMismatch / CannotNaki / RonWhileFuriten
```

`AwaitingDahai.Tsumo` 由 `Tile` 变成 `Tile option`——**鸣牌之后是 None**（没摸牌，那一手只能手切）。
这是本票唯一一处破坏性的类型改动，`GameState` 之外只有测试读它。

## 关键实现点（11 / 12 / 09 接手时看这几段）

### Pon / Chi 在动作集里的形状与优先级，11 的杠往哪挂

- **形状**：`responsesTo` 对每个非打牌者座位拼 `ron @ naki @ [过]`，同一座位内按
  **Ron → Pon → Chi →「过」**排，与裁决优先级同序；座位之间按打牌者下家优先排。
  一个座位可能有**多条 Pon**（`5s 5s 5sr` 碰 `5s` 有两种亮法，红 5 计番，亮哪两张是选手的决策），
  Chi 同理（低 / 中 / 高位 × 红 5）。
- **优先级**：裁决在 `applyResponse` 收齐全部答复之后，先 `ronWinners`（头跳仍归它管），
  没人荣和才 `nakiWinner`——`nakiWinner` 用 `nakiRank`（**Pon = 0，Chi = 1，小的优先**）
  加座位序（打牌者下家优先）排序取头一条。
- **11 的大明杠**：动作集里加在 `nakiActionsFor` 的 `pon` 旁边；裁决里在 `nakiRank` 里
  **与 Pon 同为 0**（规则上 Pon 与 Kan 平级，且同一张牌上碰与杠至多一家，撞不上）。
  执行路径复用 `applyNaki`（它已按 `Naki.taken` 判食替、按 `Naki` 记副露），
  杠只需在 `applyNaki` 之后改摸岭上牌那一段。

### 禁止食替在哪判

**判据只有一份**：`GameState.kuikaeKinds : Tile -> Tile list -> Tile list`（私有），
给出鸣完不能马上打的牌种（去红）——现物（鸣进来的那张）+ 筋（吃的两张是两面搭子时顺子的另一端）。
它被用在三处，都在 `GameState.fs`：

1. `awaitingDahaiActions` 的 `forbidden` 参数：鸣完那一手的合法动作集里直接没有那几张；
2. `nakiActionsFor` 的 `usable`：**鸣完会没牌可打的那些亮法不进合法动作集**
   （四副露只剩两张时理论上碰得到；否则响应阶段会推出一个没有合法动作的局面）；
3. `stepAwaitingDahai`：牌在手里、摸切标志也对，但不在合法动作集里 ⟹ `IllegalAction.Kuikae`。
   （第 3 处是必须的：不然「step 接受一个动作当且仅当它在合法动作集里」这条属性会破。）

### 河被鸣走的标记存在哪（12 要用）

`PlayerState.KawaTaken: bool`，读取用 `PlayerState.kawaTaken`，只在 `applyNaki` 里
`markKawaTaken` 置位，**只置位不清除**。Nagashi Mangan 的前提就是它一局下来恒为 false。

**被鸣的那张仍留在打牌者的 `Kawa` 里**：振听看的是「自己打过什么」，与那张牌后来被谁拿走无关。
连带的，牌数守恒的表述改成「各家手牌 + 河 + 副露里**自家亮出的 `consumed`** + 山 = 完整一副」
（`Naki.taken` 已经算在打牌者的河里，不能重复数）。

### 一发打断的钩子在哪（09 要填）

```fsharp
// src/Janpo.Engine/GameState.fs
let private interruptIppatsu (state: GameState) : GameState = state
```

**全局唯一的调用点是 `applyNaki`**（碰 / 吃走它，11 的三种杠也会走它）。
此刻没有标志可清（`YakuContext.Ippatsu` 恒 false，没人置位），因此它是恒等变换。
**09 只需在这个函数体里把各家的一发标志置 false**，不必再找别的调用点。
票里的黄金用例「Naki 打断一发」要等 09 有了立直才写得出，本票只保证入口唯一。

## 顺带修正的两处（都在我改的路径上）

- `yakuContextOf` 的 `firstTurn`：**任何 Pon / Chi 都把天和 / 地和打掉**（它们要求「无人鸣牌的第一巡」）。
  这是 08 报告里明确留给 10 的一条。
- `stepAwaitingResponse` 里 Ron 被拒的理由细分：原本一律 `NotAgari`，现在按
  `horaOf` 的结果分成 `NotAgari` / `NoYaku` / `RonWhileFuriten`。
  必须细分的原因：**振听只挡荣和、不挡鸣牌**，因此振听座位现在会因为「能碰能吃」而被问到，
  它在那里宣言荣和时，报「不成和了型」是错的。

## 规则长尾上的取舍（详见 DECISIONS 10-A .. 10-H）

- 食替：**现物与筋都禁**，且不可配（天凤 / 雀魂的做法）。
- **河底牌不能鸣**：可摸区空了之后的那张只剩荣和（鸣完无牌可摸）。
- 鸣走一张本可荣和的牌 = 见逃 ⟹ 同巡振听；**鸣牌不解除同巡振听**（它的定义是「到自己下次摸牌为止」，
  鸣牌不摸牌）。两条都往严的方向走，不会放过任何非法荣和。
- `GameState.junme seat` = 该家自己摸过几次牌；鸣的那家与被跳过的那几家都不涨。

## 测试

- 新文件 `tests/Janpo.Engine.Tests/PonChiTests.fs`（16 条黄金用例，两个摊好的剧本）：
  能碰能吃的动作集形状与排序、吃只吃上家、碰任意他家、红 5 的两种亮法、
  **Ron 压过 Pon**、**Pon 压过 Chi**、碰完直接打牌（无 tsumo 事件 / `Tsumo = None` / 只有手切）、
  现物与筋两种食替被拒、连续鸣牌与被跳过的座位、河被鸣走的记号、四条拒绝理由 + 中文说明。
- `GameStateProperties` 新增 3 条属性、改写 2 条：牌数守恒（含副露）、暗牌 + 3×副露数恒定、
  吃只吃上家 / 河底不能鸣 / 鸣完必有牌可打、鸣完不摸牌只手切且打不出被鸣的那张、
  被鸣的那张仍在对家河里且那家被标记。
- `GameStateGenerators` 加了 `nakiSeeking` 选手（见鸣就鸣）并把它的轨迹放进 `GameState` 生成器，
  否则随机可达局面里副露太稀。`Action` 与 `Event` 的生成器各加了 Pon / Chi。
- `EventTests` 加 mjai wire 的编解码两条（`{"type":"pon"|"chi","actor","target","pai","consumed"}`）。

### 改到的既有测试（都是「事实变了」，不是放宽断言）

| 用例 | 原来的事实 | 现在的事实 |
|---|---|---|
| `KyokuTests` 随机一局 | `dahai 条数 = tsumo 条数 = 70` | `tsumo = 70`（可摸区）且 `dahai = 70 + 鸣牌条数`，并断言随机选手确实鸣过牌 |
| `KyokuTests` 流局手牌 | 各家 13 张 | 各家 `暗牌 + 3 × 副露数 = 13`；河的总长 = dahai 条数 |
| `HoraTests` 的 8 条 | 「打出和了牌 ⟹ 只问能荣和的那家」 | 能碰能吃的也被问，因此加了 `inRonPhase`（响应阶段且有人能荣和）与 `othersPass`（其余待答座位先「过」）两个夹具；**点数、听牌张、头跳、振听的断言一字未改** |
| `HoraTests` 振听那条 | 振听座位「压根不在被问之列」，得 `NotYourTurn` | 振听座位能吃，因此照样被问，只是那份里没有 Ron；宣言荣和得 `RonWhileFuriten` |
| `NothingToRespond` 的中文 | 「无从『过』起」 | 「无从响应起」（这个错误现在也覆盖鸣牌） |

## 留给人的待审项

1. **`Kuikae`（食替）与 `KawaTaken`（河被鸣走）不在 `CONTEXT.md` 里**。前者是通行的日麻术语，
   按 ADR-0001 用罗马字拼；后者是我起的名（术语表没有对应词）。建议补进术语表 —— 见 DECISIONS 提案 10-I。
2. **食替不可配**。天凤 / 雀魂都是两者全禁，因此没加 `Ruleset` 开关。若早上决定要开关，
   加一个 `Ruleset.Kuikae`（或 `SujiKuikae`）字段，只改 `kuikaeKinds` 一处。
3. **同巡振听在鸣牌之后不解除**（取严）。若认为「鸣牌 = 这家的一巡到了」，
   改动点是 `PlayerState.addNaki` 里补一句清 `Doujun`，一行。
4. **`GameState.fs` 已近 1000 行**。响应阶段 + 鸣牌这一块（`kuikaeKinds` / `ponConsumed` /
   `chiConsumed` / `nakiActionsFor` / `nakiRank` / `nakiWinner` / `applyNaki`）够格独立成文件，
   但拆它会与正在排队的 09 / 11 正面冲突，因此本票没拆。建议 11 之后再拆。
5. **与 05（ws-b）集成时的两处摩擦**：
   - `AwaitingDahai.Tsumo` 变成了 `Tile option`（05 若 match 了它要跟一行）；
   - 我改了 `GameStateGenerators.fs` / `GameStateProperties.fs` / `KyokuTests.fs` / `HoraTests.fs`，
     05 大概率也动了同一批文件，文本冲突要人手合。语义上我没碰 Kyotaku、`applyHora` 与 `Kyoku.run`。
6. **09 落地时必须在 `responsesTo` 里把立直中的座位的鸣牌挡掉**（立直后不能碰不能吃）。
   代码里已经写了这条注释，但本票没有立直标志，挡不了。
