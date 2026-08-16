# 11 — 三种杠及其连带效果（报告）

**结论：done。** 493 个测试全绿（落地前 463），fantomas 干净，`./scripts/ci.sh` 全绿。
工作区 `/home/xerxes2/janpo-ws-a`。

## 做了什么

### 三种杠在动作集里的形状

| 杠 | 进哪个动作集 | 动作 | 条件 |
|---|---|---|---|
| Ankan（暗杠） | `awaitingDahaiActions`（自己摸完牌那一手） | `Action.Ankan(actor, consumed)`，四张 | 手里四张同种；`drawn` 非 None；`RiichiState.allowsAnkan`（09 的判据，本票只调用）；`canKan` |
| Kakan（加杠） | 同上 | `Action.Kakan(actor, pai, consumed)`，`consumed` 是原碰的三张 | 自己碰过那种牌又摸进第四张；`drawn` 非 None；**立直中不许**；`canKan` |
| Minkan（大明杠） | `nakiActionsFor`（他家打牌后的响应阶段），**排在碰旁边、吃前面** | `Action.Minkan(actor, target, pai, consumed)`，`consumed` 是自家三张 | 手里三张同种；立直中不许；`canKan` |

`canKan` = 可摸区非空（补摸要把最后一张顶进王牌）且 `kanCount < Ruleset.RinshanCount`。

**裁决顺序**：`nakiRank` 里 Minkan 与 Pon 同为 0、Chi 为 1，Ron 仍在最前 →
**Ron > Pon / Minkan > Chi**。同一张牌上碰与大明杠合起来至多一家，因此同级不会撞。
暗杠 / 加杠不在 `nakiRank` 里——它们不是对别家那张牌的响应。

### 岭上 / 抢杠 / 新宝牌的时机各在哪实现

- **补摸与新宝牌**：`GameState.completeKan`（三种杠共用）。`Wall.drawRinshan` 取岭上牌**并把可摸区
  的最后一张补进王牌**；`Wall.reveal` 翻一张新的表宝牌指示牌并给出那一张。
  顺序按固件实测（18/18）：暗杠 `ankan → dora → tsumo`，明杠 `daiminkan`/`kakan → tsumo → dora`。
- **岭上开花**：`KyokuFlags.Rinshan` 在 `completeKan` 里亮起，`yakuContextOf` 搬进 `YakuContext.Rinshan`，
  由 07 的 `Yaku` 判役。任何别的迁移都把它重算成 false。
- **抢杠**：`AwaitingResponse.Cause = ResponseCause.Kan naki`。`declareKan` 先产出杠事件、
  再问能荣和那张的家；`KyokuFlags.Chankan` 同时亮起（`responsesTo` 判「能不能荣和」与
  `applyHora` 算点读的是同一份标志）。没人抢 → `applyKan` 让杠成立；有人抢 → 走原来的
  `applyHora`，**那个杠没有发生**（宣言不改局面，因此不必回滚）。
- **国士抢暗杠**：`Ruleset.KokushiAnkanChankan`（默认 false = 天凤）。判据在 `responsesTo` 的
  `robbable`：暗杠只有 `reading.Tally.Shape = Kokushi` 且开关打开时才给 Ron。

### 责任支付挂在哪、怎么算

- 挂点：`HoraTransfer.Sekinin: Seat option`（08 给的第一个挂点）。`Score.hora` 只改「谁付」，
  **`Fu` / `basePoints` / `limit` / `HoraPoints` 一律不动**。
- 分担：自摸 → 责任者一家付光；荣和且放铳者就是责任者 → 照常；荣和且另有其人 → 两家各半。
  本场恒由放铳者付（自摸时由付家平摊），供托照旧归和了者。
- 判定：`GameState.sekininOf`。
  - 大明杠：`Flags.Rinshan` 亮着 + 倒序事件流里最近那条杠事件是**本人的 `Minkan`** → 喂杠者。
    （**不能读「最后一组副露」**：加杠是原地换掉那组碰，排在它后面的副露仍旧靠后。）
  - 大三元 / 大四喜：役里有 `Daisangen` / `Daisuushii` 时，取副露序列里第三组三元牌 /
    第四组风牌的 `Naki.target`。手里的暗刻不算（没亮出来），暗杠也不算（`target` 为 None）。
  - 四杠子**没有**责任支付（天凤）。

### 12 票读杠数的拆解器

- `GameState.kanCount : GameState -> int`（本局全场的杠数，三种杠合计）
- `PlayerState.kanCount : PlayerState -> int`（逐家的那一份，「单人四杠不流局」要它）

## 关键取舍（详见 DECISIONS 11-A…11-K）

1. **`Minkan` ↔ `daiminkan`**：标识符照术语表，wire 照 mjai（裁决 D-1）。
2. **新宝牌时机**：照固件的事件顺序实现；已知偏离教科书的「明槓は打牌後にめくる」，
   后果是明杠后的岭上开花**吃得到**新宝牌。这一条留给人裁（DECISIONS 11-B）。
3. **王牌恒 14 张**：杠取一张岭上、可摸区补一张进王牌（真实摆法）。票里「王牌张数正确减少」
   落实成「可用杠次数减少 + 可摸区每杠少一张」，否则牌会凭空蒸发。
4. **抢杠不回滚**：宣言杠只产出事件，副露与手牌在没人抢之后才动。
5. **杠只在自己摸完牌那一手**：固件 18/18 的形状。
6. **属性取样加了摊好的杠剧本**：随机取样杠太稀（四个种子只有一局杠得成），
   否则杠的不变量是空跑（备注 N-8 的同一课）。

## 测试

- `KanTests.fs`（19 条黄金用例）：三种杠的动作集、事件顺序、立直后暗杠与禁送り杠、
  连着三个杠的牌山账、岭上开花、加杠被抢杠、国士抢暗杠的两种规则集、
  大明杠后岭上开花的责任支付、大三元包、Ron > Minkan > Chi、拒绝理由与中文说明、
  以及**属性取样的覆盖率证据**。
- `KanProperties.fs`（9 条）：王牌恒定、可摸区随自摸条数递减、杠数 = `dora` 事件数 =
  指示牌数 − 1、杠后牌数守恒、杠副露的形状、动作集里杠的出现条件、抢杠那一轮只有 Ron、
  包的两条（不改和了点、自摸时一家付光）。
- 既有用例的改动：`PonChiTests` 两条动作集断言加上并列的大明杠（行为真的变了）；
  `KyokuTests` 的 `isNaki` 改名 `isPonOrChi`（**杠不让 dahai 与 tsumo 错开**：它自带一条补摸）；
  `GameStateProperties` 的牌数守恒改读 `Naki.fromHand`（加杠的 `consumed` 含着河里那张）、
  「谁握 14 张」加上抢杠窗口里的宣言者；`RiichiProperties` 的「立直后只剩摸切」加上暗杠这一例外。
- `Event` 加 4 个 case 的三处 + 测试第四处都补齐了，**没有用通配符**（备注 N-4 / N-7）。
  编译器一次点出 40 余处 match，逐个补完。

## review 结论（两轴，自己顺序跑的）

- **Standards**：一概念一文件（本票没有新类型要单开文件，`ResponseCause` 与既有的 `Phase`
  同处 `GameState.fs`）、错误是值（`CannotKan`，grep 过没有跨层同名）、`toDisplay` 在文件末尾、
  中文只在渲染出口与测试名、测试三件套齐、fantomas 干净。
  一处主动收紧：把两处 `| _ ->` 兜底改成显式枚举（N-4 的理由同样适用于 `NakiKind`）。
- **Spec**：票里 9 条验收全部落地，其中「王牌张数正确减少」按真实规则解释（见取舍 3），
  「新宝牌翻开时机」按固件实测（见取舍 2）。两条都写进了 DECISIONS 待人裁。

## 留给人的待审项

1. **DECISIONS 11-B**：明杠的新宝牌该不该推迟到打牌之后。影响 13 票对拍时明杠局的符点。
2. **DECISIONS 11-F**：包 + 荣和时的本场归谁付（本实现：恒由放铳者付），样本里没有实证。
3. **提案 11-L**：`CONTEXT.md` 补 `Kan` / `Kantsu` / `Rinshan` / `Chankan` 四个词条。
4. 属性测试在 Debug 下跑一次约 14 分钟（Release 下 1 分 43 秒）。本票加了 4 条轨迹到
   `GameStateArbitraries`，若 14 票 soak 之后 CI 变慢，这里是可调的旋钮。
