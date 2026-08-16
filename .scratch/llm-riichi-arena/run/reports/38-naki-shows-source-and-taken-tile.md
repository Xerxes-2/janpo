# 38 — 副露要能看出「被鸣的是哪一张、来自谁」

**状态**：done　**change**：`tyzmkmum`（本票一 commit）　**工作区**：`janpo-ws-c`
**fixed point**：`fe057f8f`（change `zwlwlrky`）

一句话：牌桌上被鸣的那张**横放**（`rotate(90deg)`），来源写成一枚**「来自上家」**的小标签，
加杠后添的那张前面摆一枚**「＋」**；三样都挂了 `data-`，默认视图那道无头闸门逐组核它们，
**两次反向自证都实跑过（EXIT=1，原文在 §4）**。`./scripts/ci.sh` **EXIT=0**（引擎 700 + 浏览器宿主 81）。

改到的文件：`src/Janpo.Web/Board.fs`（视图逻辑 + 两个新类型）、`src/Janpo.Web/TablePage.fs`（渲染）、
`web/src/styles.css`（三条规则）、`tests/Janpo.Web.Tests/BoardTests.fs`（6 条用例）、
`web/scripts/verify-tracer.mjs`（闸门 `checkNaki`）、`scripts/ci-web.sh`（一行回显）、
`docs/images/table.png`（重出）。

**没碰**：引擎（`src/Janpo.Engine/**` 一字未动）、`web/src/agent/`、`.github/workflows/`、
`README.md`、`CONTEXT.md`、`docs/adr/`、`web/index.html`、别人的票。

---

## 1. 记号：横放，以及为什么是它

三条信息、三个记号，每个都**一对一钉在一个引擎概念**上：

| 画出来的 | 意思 | 引擎里的真源 | DOM |
|---|---|---|---|
| **横放**（转 90°） | 这张是**从别人那儿来的** | `Naki.fromKawa` | `.tile.taken` + `data-naki-taken="true"` |
| **「来自上家」标签** | 那个人相对**副露方**是第几家 | `Naki.target` + `Seat.distanceFrom` | `data-naki-from` + `data-naki-from-seat` |
| **「＋」** | 加杠后**加上去**的那张（自家的） | `Naki.taken`（仅加杠） | `.naki-add` + `data-naki-added="true"` |

**横放挑的是「朝向」这个维度**，牌桌上已经被占的四条记号一条也不撞：

| 已有记号 | 维度 |
|---|---|
| 摸切 | 虚线 + 淡色（笔触 + 不透明度） |
| 牌背 | 45° 斜纹底 + 实线框（底纹） |
| 赤牌 | 红字（颜色） |
| 刚摸那张 | 额外间距（间距） |
| **被鸣的那张（本票）** | **转 90°（朝向）** |

被否决的两种：**底色高亮**（与牌背的斜纹底同一维度，两者在同一行里会互相说话）、
**加粗边框**（与摸切的虚线同一维度）。横放还有一条别的记号没有的好处：它是**牌谱的标准画法**，
认得牌谱的人不用学。

`transform` 不占布局，因此邻牌不会被推走；代价是转过来的那张会**顶出**副露组的虚线框
（「赤5万」三个字最宽，转过来约 2.4rem 高）。处置是给 `.naki` 一条 `min-height: 2.4rem`——
**抬的是副露组，不是副露那一行**：一家还门清时那一行是空的，空行不必跟着变高。

## 2. 来源：文字，参照系是副露方

### 2.1 措辞

`来自下家` / `来自对家` / `来自上家`，与 prompt 尾部（`web/src/agent/wording.ts` 的 `relative`）
逐字同一套词。**是「对家」不是「対面」**：票面写的是「対面」，但 `CONTEXT.md` 的
Shimocha / Toimen / Kamicha 词条、`Danger.Threat.who`、`wording.ts` 三处一致用「对家」，
措辞唯一权威在 `CONTEXT.md`，照它。座位数不是 4 时没有对家，退回「座位 N」（形状照 `Threat.who`）。

### 2.2 为什么是文字标注，不是牌谱那套位置编码

牌谱的做法是**位置编码**：横放那张摆在组的左 / 中 / 右，分别表示上家 / 对家 / 下家。没取它，两条理由：

1. **这一页四家是竖排面板，没有方位可锚。** 位置编码在真牌桌上读的是「我左手边那家」；
   一列自上而下的卡片里，「摆最左」不指向任何人，只能靠背约定。M2 真把牌桌摆成四家围坐时
   再加不迟——`NakiView.Relative` 已经把「第几家」算好了，那时只是换个画法。
2. **挪位会打乱吃的升序。** 位置编码要求被鸣那张一律搬到某一端，于是吃 3p（手里 2p4p）
   会画成 `[3筒] 2筒 4筒`，「2 3 4 是个顺子」这件事就得读者自己在脑子里排。
   现在是**升序不挪位、就地横放**：`2筒 [3筒] 4筒` 与 `[2筒] 3筒 4筒` 摆的顺序一样，
   横的那张不一样——票面要的正是这个。

### 2.3 参照系是**副露方**，不是观测者

`Relative = Seat.distanceFrom ruleset owner target`，`owner` 是这一组副露的主人。
理由很硬：**「吃只能吃上家」是副露自己的属性**（`Naki.chi` 的不变量、`Action.fs:34` 写着）。
换成观测者的参照系，坐在座位 0 看座位 2 吃来的那一组会写成「来自上家」以外的说法——日麻里不成立。
闸门里因此有一条专门的交叉核对（§4 的第二次反向自证就是拿它证的）。

**prompt 尾部的 `words.who` 是另一回事**，两者不冲突：它给的是**观测者**对每个座位的称呼
（`self()` 里观测者就是副露方，两者重合；`other()` 里是观测者的参照系）。词表相同、参照系不同。
`prompt.ts` 那一处有个可议的后果，记在 §6 第 1 条，**没碰**。

## 3. 三种杠，与我亲眼看到的

图是自己打开看的。找局面用的是 CLI（`janpo kyoku <seed>` 的事件流里 grep `ankan` / `kakan` /
`daiminkan`，0.44 秒一个种子），比在浏览器里扫快两个数量级。

### 3.1 种子 1223 走 90 手：吃 + 碰 + **暗杠** + **加杠** 同框

整页 [`38-naki-all-kinds.png`](./38-naki-all-kinds.png)，副露那几行放大 [`38-naki-rows.png`](./38-naki-rows.png)。
我看到的：

- **座位 0**：`加杠 来自对家 [中]横 中 中 ＋中`　和　`吃 来自上家 6万 [7万]横 8万`
  —— 加杠横的是**当初碰来的**那张（来自座位 2 = 对家），最后那张前面一枚「＋」是**后加上去的**；
  吃横的是**中间**那张，三张仍是 6-7-8 升序。
- **座位 1**：`碰 来自上家 [9索]横 9索 9索`　和　`碰 来自对家 [4筒]横 4筒 4筒`
  —— 同一家的两副碰来自不同的人，标签分得开。
- **座位 2**：`吃 来自上家 [赤5万]横 6万 7万`（红字 + 横放**叠在一起仍然分得清**）
  和　`吃 来自上家 1筒 [2筒]横 3筒`。
- **座位 3**：`暗杠 [斜纹背] 3索 3索 [斜纹背]` —— **两端牌背没被弄坏，且整组没有来源标签**（它不是鸣来的）。

DOM 侧同一帧读出来的（`data-` 属性原样）：

| 座位 | 种类 | `data-naki-from` / `-seat` | 逐张（横=横放、＋=加上去的） |
|---|---|---|---|
| 0 | 加杠 | 对家 / 2 | `7z(横) 7z 7z 7z(＋)` |
| 0 | 吃 | 上家 / 3 | `6m 7m(横) 8m` |
| 1 | 碰 | 上家 / 0 | `9s(横) 9s 9s` |
| 1 | 碰 | 对家 / 3 | `4p(横) 4p 4p` |
| 2 | 吃 | 上家 / 1 | `5mr(横) 6m 7m` |
| 2 | 吃 | 上家 / 1 | `1p 2p(横) 3p` |
| 3 | 暗杠 | —— / —— | `背 3s 3s 背` |

### 3.2 种子 237 走 86 手：**大明杠**（外加暗杠）

局部放大 [`38-naki-minkan-rows.png`](./38-naki-minkan-rows.png)。

- **座位 3**：`大明杠 来自对家 [5万]横 5万 5万 赤5万` —— 四张里横的是被鸣的那张，
  杠里那张赤 5 照旧是红字；同一家还有 `碰 来自下家 [3索]横 3索 3索` 与 `碰 来自对家 [西]横 西 西`。
- **座位 0**：`吃 来自上家 [4筒]横 5筒 6筒` 与 `暗杠 [背] 2筒 2筒 [背]`（同一行里横放与牌背并排，不打架）。
- **座位 1**：`吃 来自上家 7筒 [8筒]横 9筒`；**座位 2**：`碰 来自下家 [1索]横 1索 1索`。

三种杠因此各有一张实拍：**暗杠**（3.1 座位 3 / 3.2 座位 0，无来源、两端背面、无横放）、
**大明杠**（3.2 座位 3，有来源、一张横放）、**加杠**（3.1 座位 0，来源是当初碰的那家、
横的是当初碰来的那张、后添的那张带「＋」）。

### 3.3 深色模式

同一帧用 `prefers-color-scheme: dark` 又拍了一张对着看：横放、「＋」、来源标签三样都在，
颜色跟着 `canvastext` 走（本票没引入任何写死的颜色）。这张图没进仓库（省体积），
复现只要给无头浏览器加 `colorScheme: "dark"`。

### 3.4 README 首屏那张（`docs/images/table.png`，种子 1177 / 52 手，命令没变）

重出了。同一手牌局现在读起来是：座位 0 `吃 来自上家 2筒 [3筒]横 4筒` + `吃 来自上家 3筒 [4筒]横 赤5筒`、
座位 1 `碰 来自对家 [2万]横 2万 2万`、座位 2 `碰 来自上家 [中]横 中 中`、座位 3 `碰 来自下家 [3索]横 3索 3索`
——**上家 / 对家 / 下家三种来源在同一张图里各出现一次**，正好当门面。
README 正文与图注一个字没改（图注说的是河、副露与投影，仍然准确）。

## 4. 闸门与两次反向自证

闸门加在**默认视图那一道**（`web/scripts/verify-tracer.mjs` 的 `checkDefaultView` → 新增 `checkNaki`）。
它把种子填成 1223、点 90 次「单步」（约 3 秒），然后**逐组**核四件事：

1. 非暗杠的每一组**恰好一张** `data-naki-taken`，且写了 `data-naki-from` 与 `data-naki-from-seat`；
2. **暗杠两样都没有**——它不是鸣来的；
3. **中文说法与绝对座位对得上**：`(from-seat − 副露方座位 + 4) mod 4` 必须等于「下家/对家/上家」那一档，
   且**吃恒来自上家**；
4. 只有加杠有那一张 `data-naki-added`，别的种类一张也没有。

外加一条**防空转**的：一组鸣来的副露都没走出来时直接报错（否则上面四条全在空转全绿）。
绿的时候它把看到的东西打出来，人一眼能看见覆盖到了什么：

```
副露看得出被鸣的那张与来源 ✓（种子 1223 走 90 手：加杠←对家、吃←上家、碰←上家、碰←对家、吃←上家、吃←上家、暗杠）
```

### 4.1 反向自证 A：把 `data-` 标记去掉 → 红

改动：`TablePage.nakiGroup` 里的 `data-naki-from` / `data-naki-from-seat` 那两条与
`data-naki-taken` 临时删掉（**只删标记，页面上的横放与文字标签照旧**），重跑 fable + vite +
`node scripts/verify-tracer.mjs`：

```
默认视图里少了该给访客的东西：
座位 0 的「加杠」里被鸣的那张有 0 处记号（该有且只有一处）
座位 0 的「加杠」没写来源（data-naki-from / data-naki-from-seat）
座位 0 的「吃」里被鸣的那张有 0 处记号（该有且只有一处）
座位 0 的「吃」没写来源（data-naki-from / data-naki-from-seat）
座位 1 的「碰」里被鸣的那张有 0 处记号（该有且只有一处）
座位 1 的「碰」没写来源（data-naki-from / data-naki-from-seat）
座位 1 的「碰」里被鸣的那张有 0 处记号（该有且只有一处）
座位 1 的「碰」没写来源（data-naki-from / data-naki-from-seat）
座位 2 的「吃」里被鸣的那张有 0 处记号（该有且只有一处）
座位 2 的「吃」没写来源（data-naki-from / data-naki-from-seat）
座位 2 的「吃」里被鸣的那张有 0 处记号（该有且只有一处）
座位 2 的「吃」没写来源（data-naki-from / data-naki-from-seat）
EXIT=1
```

12 条，六组副露各两条；暗杠那一组照旧不报（它本来就该两样都没有）。已还原。

### 4.2 反向自证 B：参照系漂到观测者 → 也红

第一道只能证明「标记在不在」。**参照系写错了标记照样在**，所以另证一次：把
`nakiGroup ruleset view.Seat` 临时改成 `nakiGroup ruleset Seat.first`（即所有副露都按座位 0 的
视角算相对位置，正是「用观测者当参照系」那个错误），重跑：

```
默认视图里少了该给访客的东西：
座位 1 的「碰」写着「来自座位 0」，可座位 0 相对副露方是「上家」
座位 1 的「碰」写着「来自上家」，可座位 3 相对副露方是「对家」
座位 2 的「吃」写着「来自下家」，可座位 1 相对副露方是「上家」
座位 2 的「吃」写着「来自下家」：吃只吃得了上家
座位 2 的「吃」写着「来自下家」，可座位 1 相对副露方是「上家」
座位 2 的「吃」写着「来自下家」：吃只吃得了上家
EXIT=1
```

座位 0 自己那两组不报（它就是参照系本身），这正好说明这条闸门核的是**每一家各自的**参照系。
「吃只吃得了上家」那条单独报了两次——它是同一件事的第二重保险，专抓「说法反了」。已还原。

## 5. 单元测试（视图逻辑，不含纯样式）

新逻辑落在 `Board.nakiView`（`src/Janpo.Web/Board.fs`）——**它一行 Feliz 都不 open**，
因此 `tests/Janpo.Web.Tests` 在 dotnet 上跑得了（那个 fsproj 顶上的规矩：要 open Feliz
就说明代码放错文件了）。6 条用例（`BoardTests.fs` 末尾）：

| 用例 | 钉的是 |
|---|---|
| 吃：被鸣的那张就地横放，三张仍按升序摆 | `2p [3p] 4p` 与 `[2p] 3p 4p` 的两份 `FromOther` 不同、`Pai` 序列相同；来源是上家 |
| 碰：来源是被鸣那家相对副露方的第几家 | 三个 target 各得 1 / 2 / 3，且横放的恰好一张 |
| 暗杠：没有来源，两端扣着，一张横放的也没有 | `[None; Some; Some; None]`、`Target`/`Relative` 都是 `None` |
| 大明杠：四张里横放一张，来源在 | 四张全亮、一张横放、相对位置对 |
| 加杠：横放的是当初碰来的那张，加上去的那张另有记号 | 组里顺序 = `Consumed` 原序 + 末尾 added；来源是**当初碰的**那家 |
| 一组副露里至多一张横放、至多一张是加上去的 | 五种副露一起过：横放数 = `isConcealed ? 0 : 1`，张数与 `Naki.tiles` 一致 |

**这些用例咬得动**（试过）：把 `calledTiles` 里找位置那一步换成「恒取第 0 张」，
「吃：被鸣的那张就地横放……」当场红（`Assert.Equal() Failure: Collections differ`，`BoardTests.fs:231`）。已还原。

## 6. 留给人的待审项

1. **`prompt.ts` 的 `naki()` 对他家副露用的是观测者参照系**（`words.who(group.target)`）。
   于是「上家吃了**它的**上家」在 prompt 里会印成「来自对家」——日麻里吃只能吃上家，这句话读着是错的。
   **本票没碰**（票面禁改 `web/src/agent/`），而且它不影响牌桌。要修得连着 prompt 的黄金前缀一起动，
   建议单开一票。牌桌这边已经是副露方参照系（§2.3），两处一旦要对齐，以牌桌这边为准。
2. **`docs/images/table.png` 换了**（尺寸变高一点，因为副露组抬了行高）。README 正文与图注一个字没动，
   但**票 27 的验收正在另一个工作区核 README 与截图**，请在集成时让它重新核一遍。
3. **相对位置的中文映射现在有三份**（引擎 `Threat.who`、TS `wording.relative`、本票 `TablePage.nakiFrom`）。
   没收成一处：票面禁改引擎，而 ADR-0005 的跨界方向只往「F# 调 TS」走。
   闸门那条「中文说法要与 `data-naki-from-seat` 对得上」是它们跑偏时的报警器。
4. **横放的那张字是侧着的**（转 90° 的必然结果，「赤5万」侧着最高）。牌谱就是这么画的，
   但要认那张牌得歪一下头。真嫌它难读，替代方案是「不转、只把方框改成扁的」——那要给 `.tile`
   定死宽高，会牵动整页所有牌，本票没做。
5. **`.naki` 抬到 `min-height: 2.4rem`** 是唯一的尺寸改动（每家的副露行高了约 0.8rem）。
   M2 重做牌桌时这条大概率整条重写，不必守着。
6. 三张证据图共 247KB，照票 32 / 35 的先例放在 `reports/` 下，不进 `docs/`。

## 7. code-review — Standards 轴（fixed point `fe057f8f`）

派不出 sub-agent，自己顺序跑的。标准来源：`AGENTS.md`、`docs/agents/fsharp-style.md`、
`.scratch/llm-riichi-arena/run/RUNBOOK.md`、`docs/agents/issue-tracker.md` / `triage-labels.md`，
外加 code-review skill 那份坏味道基线。工具强制的（Fantomas `--check`、`check-style.sh`、Biome、tsc）本次全绿。

### Hard violation：0

- **jj-only ✓** 全程 `jj st` / `jj diff` / `jj commit`，一条 git 命令都没跑；无远端操作、无 `op restore`、
  没 abandon 别人的 change、没用交互式 flag。
- **禁改边界 ✓** 引擎、`web/src/agent/`、`.github/workflows/`、`README.md`、`CONTEXT.md`、`docs/adr/`、
  `web/index.html`、别人的票全部一字未动。
- **F# 风格 ✓** 规则 1/3：新代码没有从里往外读的嵌套，`Naki.fromKawa naki |> Option.bind (…)`、
  `Naki.taken naki |> Option.map (nakiTile false true) |> Option.toList` 都从左往右读；
  规则 2：没有 `fun x -> f (g x)` 形状（`(nakiTile takenTitle)` 是部分应用，不是包一层的 lambda）；
  规则 4：`nakiFrom` 是个 `match`、`extra` 是字符串拼接，都没有强行管道；
  规则 5：没有新 `let mutable`。
- **注释写「为什么」✓** 三处关键取舍（横放挑朝向、来源不走位置编码、加杠为什么不横放）都写在代码上，
  不只写在这份报告里。
- **票文件 ✓** 七条验收 + 三条边界逐条勾上并各注一句证据，`**Status:**` 按 triage-labels 改成
  `ready-for-human`；决策追加在 `DECISIONS.md` **文件末尾**新起的「## 38」段。

### 判断题（记录，未改）

1. **Duplicated Code：相对位置的中文映射第三份**（§6 第 3 条）。未收，理由见那一条，已有闸门盯着。
2. **`nakiTile` 这个名字在两个模块里各有一个**（`Board` 里是造视图值的，`TablePage` 里是造元素的）。
   两者都是 `private`、语义同源（「副露里的一张」），且各自在自己模块里唯一。改名成
   `nakiTileView` / `nakiTileSpan` 更啰嗦，没改。
3. **`seatPanel` 现在收四个位置参数**（ruleset / viewer / oya / view）。包成一个记录是纯加法，
   四个还在可读范围内，没做。
4. **`checkNaki` 让默认视图那道闸门多跑 3 秒**（90 次点击）。拆成独立脚本要多起一次 preview
   与一次无头浏览器（更贵），而「默认视图该有什么」本就是同一件事，没拆。
5. **闸门钉了一个种子（1223）**。引擎的随机序列一变它就可能不再出加杠——那时闸门会因为
   「一组鸣来的副露都没有」而红（防空转那条），而不是静默变空。修法只是换个种子，代价可接受；
   种类的穷尽覆盖在单元测试那边（§5），不靠这颗种子。
6. **Speculative Generality（轻微）**：`NakiView.Target` 与 `Relative` 两个字段都留着，
   眼下渲染只用 `Relative`、`Target` 只进了 tooltip 与 `data-`。留着是有意的——
   闸门那条交叉核对读的就是绝对座位。

其余基线项（Mysterious Name / Feature Envy / Data Clumps / Repeated Switches / Shotgun Surgery /
Middle Man / Refused Bequest / Primitive Obsession）逐条比过，本次 diff 不沾。

### Spec 轴（顺带自查）

票面七条验收全部落地（§1 §2 §3 §4 §5），三条边界全部遵守。**scope creep 两处**，都记在
DECISIONS「## 38」第 7 条：`verify-tracer.mjs` 的 `checkNaki`（票面明确要求的那道闸门）、
`ci-web.sh` 一行回显。没有缺失项，没有 blocking，因此没有触发自动修复轮。

## 8. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套闸门 | `./scripts/ci.sh` | **EXIT=0**（fantomas / 风格闸门 / 引擎 700 / 浏览器宿主 81 / 浏览器内六道全 ✓） |
| 新用例 | `dotnet test tests/Janpo.Web.Tests --filter BoardTests` | 17 通过（原 11 + 新 6） |
| 用例咬得动吗 | 临时把找位置那一步改成恒取第 0 张 | **红**（§5） |
| TS/JS 格式与 lint | `cd web && pnpm run check` | 51 个文件，无 fix |
| F# 格式 | `dotnet fantomas .` | Formatted 3 / Unchanged 139 / Errored 0 |
| 闸门单跑 | `cd web && node scripts/verify-tracer.mjs` | 五行对拍 ✓ + 默认视图三条 ✓ |
| 标记没了会红吗 | 临时删 `data-naki-*` | **红，EXIT=1**（§4.1） |
| 参照系漂了会红吗 | 临时改成观测者参照系 | **红，EXIT=1**（§4.2） |
| 页面长什么样 | 无头 Chrome，浅色 + 深色各一遍 | §3，亲眼看了 |
| README 那张 | `cd web && node scripts/shoot-table.mjs` | 重出，`pageerror` 为空（§3.4） |
