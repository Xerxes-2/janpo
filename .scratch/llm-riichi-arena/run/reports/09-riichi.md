# 09 — 立直（报告）

**STATUS: done** ｜ 463 测试全绿（新增 26 条：19 具名 + 7 属性）｜ fantomas 干净 ｜ `./scripts/ci.sh` 全绿

---

## 一、做了什么

### 新概念：`RiichiState`（`src/Janpo.Engine/RiichiState.fs`，排在 `Furiten.fs` 之后）

立直在一局之内的**三段**状态，理由见 DECISIONS 09-A：

```
RiichiState = None | Declared of RiichiDeclaration | Accepted of RiichiDeclaration
```

`Declared` 是「宣言了、宣言牌还没落定」那一段——真实牌谱里 491 次 `reach` 只对应 479 次
`reach_accepted`，差的 12 次就是宣言牌被荣和、立直不成立。判役读 `RiichiState.declaration`
（**只有 `Accepted` 才算**），因此那一段里这家和不了，也不会拿到立直的番。

模块里除了状态迁移，还有三条**判据**（都是纯函数、都有单元测试）：

| 函数 | 干什么 | 谁读它 |
|---|---|---|
| `tenpaiDahai kindSet nakiCount hand` | 打出哪几种牌之后仍然听牌 | 宣言合法性 + 宣言牌的限制（同一份） |
| `canDeclare ruleset kindSet remaining score naki hand` | 门清 / 听牌 / 点数 / 牌山四条 | 合法动作集 |
| `allowsAnkan kindSet state naki hand drawn kind` | **立直后能不能暗杠**（裁决 D-8） | **11 票** |

### 摸打循环（`GameState`）

- **`Action.Riichi of actor`**：宣言是独立一手（DECISIONS 09-B），产出 `Event.Riichi`（wire `reach`）。
  宣言之后阶段不变，合法动作集收窄成「打完仍听牌的那几张」（手切 + 摸切都筛）。
- **宣言牌落定**：`acceptRiichi` 在「收齐响应且无人荣和」那一刻跑——扣 `Ruleset.RiichiBou`、
  `Kyotaku + 1`、一发亮起，产出 `Event.RiichiAccepted`（wire `reach_accepted`）。
  事件顺序 `dahai → reach_accepted → tsumo|pon|chi`，与真实牌谱同形。
- **宣言牌被荣和**：走 `applyHora`，`PlayerState.cancelRiichi` 把宣言作废——只有 `reach`、
  没有 `reach_accepted`、立直棒不出。
- **立直后只能摸切**：`Accepted` 的动作集 = 自摸和 + 摸切。暗杠**没有加**（D-8），判据留给 11。
- **立直中不能鸣牌**：`responsesTo` 里挡掉碰吃（11 的大明杠走同一处），但**荣和照给**。
- **`GameState.kyotaku`**：场上立直棒的根数（局初供托 + 本局成立的立直，和了后归零）。

### 一发（`PlayerState.Ippatsu`，逐座位）

三条解除路径各有唯一入口：**自家再打一张**（`PlayerState.discard`）、**任何人鸣牌**
（`GameState.interruptIppatsu`，10 票留的钩子，全局唯一调用点仍是 `applyNaki`）、
**这一局终了**。立直宣言牌被碰的那一手是「先亮后灭」：`acceptRiichi` 亮起，`applyNaki` 打掉。

### 振听

`PlayerState.minogashi` 在立直中把 `Permanent` 一并置起，`refreshFuriten` 对立直中的座位
**只置位不清除**（DECISIONS 09-D）。非立直的见逃仍然只是同巡振听，到自己下次摸牌解除。

### 跨局与事件

- `Game.advance` 结转的供托改读 `GameState.kyotaku`（DECISIONS 09-E）：一行改动，
  `Game.after` 的三条规则一字未动。不这么改的话，有人立直又流局时那几根立直棒会凭空蒸发。
- `Hora` 事件补 `uradora_markers`（04 在类型注释里就把它记在 09 名下，DECISIONS 09-F）：
  只在和了者立了直时非空。

### 共用的一处提取

`AgariShape.waits kindSet hand`（「这手等摸的牌听什么」）：06 的 `PlayerState.waits` 与
09 的暗杠判据都要它，提到 `AgariShape` 一份，`PlayerState.waits` 改成调用它。
没有第二份和了型判定。

## 二、关键取舍（详见 DECISIONS 09-A .. 09-I）

1. **立直棒在落定时才出**，不是宣言时（票里写的是宣言时；两者冲突，取与 `reach_accepted`
   和真实牌谱一致的那个）。这是本票唯一一处「与票面文字不同」的决定，请人确认。
2. **宣言与宣言牌是两手**，不合成一个动作——mjai 与牌谱都是两条事件。
3. **宣言后不许反悔自摸和**（`RiichiRestricted`）。
4. **立直振听靠「重算跳过」而不是给 `Furiten` 加第三位**（06 把这个选择留给了 09）。
5. **「牌山剩余 ≥ 4」按座位数读**，不写字面量 4。
6. **暗杠判据三条全要**：禁送り杠 + 听牌不变 + 面子构成不变。第三条不是摆设，
   我用暴力搜索找到了「②过而③不过」的反例（`123m 66m 333s 44s 555s` 摸 `3s`，
   和 `4s` 时索子能读成 `345s×3`），已写成具名用例。

## 三、给 11 票的接口（裁决 D-8）

```fsharp
RiichiState.allowsAnkan
    (kindSet: TileKindSet)      // TileKindSet.ofKinds ruleset.TileKinds
    (state: RiichiState)        // PlayerState.riichi player
    (naki: Naki list)           // PlayerState.naki player
    (hand: Tile list)           // PlayerState.hand player（含刚摸进那张）
    (drawn: Tile option)        // PlayerState.drawn player
    (kind: Tile)                // 想杠的牌种
    : bool
```

- **没立直返回 `true`**：这条判据只回答立直独有的那一层，「手里真有四张」「岭上牌还剩」
  「四杠散了」等条件都在 11 票那边。
- **`Declared`（宣言了还没落定）返回 `false`**：那一手只能打宣言牌。
- 11 落地时还会撞到两处编译错（都是加 case 的常规代价，备注 N-7）：
  `GameState.firstTurnFor` 的 `Event` match（**暗杠要打断两立直与天和地和**，
  加 `| Ankan _ -> false`），以及 `GameState.junme` / 各测试文件里的 `Event` match。
- 一发：三种杠都走 `applyNaki` → `interruptIppatsu`，自然打断，11 不必再动。

## 四、review 结论（两轴，fixed point = `@-` = `ksqnllru`「集成 10」）

**Spec 轴**：票的 8 条验收全部落地（票文件已勾满）。与票面唯一的偏离是取舍 1（立直棒的时机），
理由与证据写在 DECISIONS 09-A。D-8 的边界守住了：**没有往合法动作集里加任何暗杠动作**，
只交了判据 + 6 条单元断言。

**Standards 轴**（自查，无 blocking 项）：

- 标识符走术语表罗马字（`RiichiState` / `Ippatsu` / `Kyotaku` / `tenpaiDahai`），
  wire 走 mjai 原拼（`reach` / `reach_accepted` / `uradora_markers`）——裁决 D-1。
- 中文只在 `toDisplay`（`RiichiState.toDisplay`、`IllegalAction.toDisplay`）；错误是值不是异常。
- 一概念一文件、namespace `Janpo`、按依赖顺序进 fsproj；测试是 `RiichiTests` + `RiichiProperties`
  两件（第三件 Generators 没有：立直的固件是往 `GameStateGenerators` 里加的一个选手 `riichiSeeking`
  与一条 `acceptedRiichiCount`，另起一个文件反而把局面生成器劈成两半）。
- 新增错误 case 前 grep 过跨层同名（裁决 D-3）：`CannotRiichi` / `RiichiRestricted` 都只有一处。
- 自查时改掉的两处（review 自动修的那一轮）：
  1. **宣言后仍能自摸和**——`step` 的 Hora 分支没有查合法动作集成员，能绕过收窄后的动作集。
     已加 `RiichiRestricted` 拒绝，并补了具名用例。
  2. **立直后打错牌被报成 `Kuikae`（食替）**——诊断错误。已分出 `RiichiRestricted`。
- 记录但不修（nitpick）：`awaitingDahaiActions` 现在有 7 个参数（本票加了 `kindSet` 与 `remaining`）。
  已把 `awaitDahai` 改成读整份局面的 `awaitDahaiIn` 止住扩散，但那一层本身没重构——
  11 票再往里加东西时值得把「合法动作集」整体提成一个读 `GameState` 的模块。

## 五、留给人的待审项

1. **立直棒的时机**（DECISIONS 09-A）：与票面文字「宣言时的扣点」不同，取的是「落定时」。
2. **`Hora.uradora_markers` 的 wire 名**（09-F）：用了 mjai 官方规格的 `uradora_markers`，
   13 票手上那批牌谱写的是 `ura_markers`，适配放 13 的读入层。
3. **`Game.advance` 一行改动**（09-E）：碰了 05 的文件，但那正是 05 在注释里留给 09 的位置。
4. **`AgariShape.waits`**：往 03 的文件里加了一个公开函数（为了不写第二份「听什么」）。
5. **一发的窗口按「自家下一次打牌」结束**，没有用 `junme`：立直宣言那一手打完才亮一发，
   下一次自家打牌清掉，中间的自摸（一发自摸）与他家打牌（一发荣和）都在窗口内。
   这与「一巡」的直觉一致，但没有显式读 `GameState.junme`——若人想统一到巡目口径，改动点只有
   `PlayerState.discard` 与 `PlayerState.acceptRiichi` 两处。

## 六、测试清单

**`RiichiTests`（19 条具名）**：宣言进动作集且排在自摸和之后打牌之前 / `reach` 事件与「此时还没扣点」/
宣言后的动作集只剩保持听牌的打法 / `reach_accepted` 与扣点入供托 / 宣言后不能反悔自摸和 /
四条宣言合法性（门清·暗杠不破门清·点数·牌山·听牌）/ `tenpaiDahai` / **一发自摸（含两立直）** / **一发荣和** /
**一发被碰打断**（同一座牌山、同一次和了，只差一发）/ **宣言牌被 Ron**（只有 `reach`、立直棒不出）/
立直后只能摸切 / 立直中不进鸣牌动作集但仍能荣和 / **立直后见逃是永久振听且再摸打也解不开** /
立直棒随和了归和了者、流局时留在场上 / **暗杠判据 4 条**（正例、送り杠、听牌变、面子构成变、
没立直放行、宣言中不许）。

**`RiichiProperties`（7 条）**：场上立直棒 = 局初供托 + `reach_accepted` 条数（和了后归零）/
每条 `reach_accepted` 都对得上一条 `reach` / 一发只可能亮在立直成立的那家头上且鸣牌之后全场无一发 /
立直后的动作集形状 / 立直中鸣不了牌 / 立直中的家永远听牌且手牌张数不变 /
**「不变量不是空转」**（40 局立直轨迹里确实立起了直、且有带里宝牌的和了）。

改到的既有测试：`ScoreProperties` 与 `GameStateProperties` 里三条供托守恒的表述从「局初供托」
改成「场上供托 / 局初供托 + `reach_accepted` 条数」——立直棒是局内产生的，原表述在有人立直时
本来就不成立（它们此前恒真只是因为没人立得了直）。`GameStateArbitraries` 加了一条立直密集的轨迹，
因此全部既有不变量现在也跑在有立直的局面上。
