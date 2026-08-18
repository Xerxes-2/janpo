# 70 — 拆开 `TablePage.fs`（零行为改动）

**结论：`TablePage.fs` 1647 行 → 五个文件（58 / 472 / 744 / 364 / 83 行），
渲染出来的 DOM 一个字节都没动。** 判据不是「代码看着像一类」而是「哪张后续票会改它」，
拆法直接照票面那张表落。`./scripts/ci.sh` 37.2s 全绿，`web/scripts/**` 一个字没碰。

「没有惊喜」这句话在这份报告里被证了三遍，一遍比一遍硬：

1. **钩子全集**：`prop.testId` 与 `data-*` 三组 grep 的 diff 全空（§3）；
2. **源码逐行**：拆前拆后按行排序对照，**只少了 18 行**，每一行都在 §4 逐条列着；
3. **Fable 生成的 JS**：归一化掉 Fable 自己的 import 消歧后缀之后，生成的 JS **只多出那五个转出包装**
   （15 行），一行删改都没有（§5）。

## 1. 拆成哪五个文件，为什么是这五个

| 文件 | 行 | 装什么 | 会改它的票 |
|---|---|---|---|
| `TableState.fs`（`module TableState`） | 472 | `Awaiting` / `Rendering` / `AgentStatus` / `TableModel` / `TableMsg` 五个类型 + MVU 全套（`init` / `initial` / `update` / `step` / `settle` / `resume` / 那几个 `Cmd`） | 71、72、73、74、75 |
| `AgentLine.fs`（`module AgentLine`） | 83 | `fallenBack` / `agentLine` / `usageLine`——Agent 层那两行状态线 | 74、76 |
| `TableBoard.fs`（`module TableBoard`） | 744 | `Seating` 类型 + 牌与一家（`paiSpan`…`seatPanel`）、场况与结算（`tableCenter` / `settlementPanel` / `resultPanel`）、危险度面板、`tableBody` | 76（气泡挂座位格子）、75 |
| `TablePanel.fs`（`module TablePanel`） | 364 | 控件工厂（`button` / `picker` / `textField` / `areaField` / `selectField`）+ `controls` / `viewpoints` / `renderingLine` / `llmPanel` | 72、73 |
| `TablePage.fs`（`module TablePage`） | 58 | 五个公开入口的转出 + `Page ()` 外壳 | 71（首页与路由） |

票面那张表的四行**分别落在四个不同文件里**（`TableState` / `TablePanel` / `TableBoard` / `AgentLine`），
第五个文件是外壳。

`<Compile>` 的新顺序（**就是编译顺序，F# 只往前看**）：

```
App.fs → TableState.fs → AgentLine.fs → TableBoard.fs → TablePanel.fs → TablePage.fs → Footer.fs → Main.fs
```

排这个顺序的两条约束：

- `AgentLine` 要排在 `TableBoard` **前面**——`tableBody` 把那两行状态接在牌桌最前面（`agentLine` / `usageLine` /
  `fallenBack` 三处调用）。反过来 `AgentLine` 只用到 `TableModel` 与 `AgentStatus` 两个类型，
  于是这两个类型留在 `TableState`（模型的一格本来就属于状态那一块），不必为它们再开一个类型文件。
- `TablePage`（外壳）必须**最后**：它要同时用到 `TableState.init` / `TablePanel.*` / `TableBoard.tableBody`。

`Seating` 跟着 `seatPanel` 走进了 `TableBoard`：它只被那一个文件构造与消费（`tableBody` 拼、`seatPanel` 收）。

## 2. 那个绕不开的形状：`TablePage` 只能是外壳 + 转出

`Main.fs`（**不许动**）调的是 `TablePage.Page`，dotnet 侧用例调的是 `TablePage.initial` / `init` /
`update` / `rosterOf` / `renderingPending`。而 `Page ()` 要用到全部视图，只能排在最后；
`rosterOf` / `renderingPending` 又被 MVU 与面板用着，只能排在前面。**同一个模块能不能分在两个文件里？不能**——
当场试了一次：

```fsharp
// /tmp/modtest：同一 namespace 下两个文件各写一个 module Bar
error FS0248: 名为“Foo.Bar”的两个模块同时出现在此程序集的两个部分中
```

所以真实现落在 `TableState`，`TablePage` 那一层把五个入口原样转出去。为了让「公开签名不减」**同时**
不变成「公开签名膨胀」，跨文件用的助手一律 `internal`（见 §4 第 2 条）：

> `Janpo.Web` 这个程序集对外的公开面，与拆之前**逐字相同**：
> `TablePage.initial` / `init` / `update` / `rosterOf` / `renderingPending` / `Page`，一个不多一个不少。

被否决的两个写法：把 `Page ()` 挪进 `Main.fs`（票面明令不动 `Main.fs`，而且外壳与页面是两回事）；
把 MVU 那一块直接叫 `module TablePage` 放在最前、外壳另起名字（那就得改 `Main.fs` 的调用点）。

## 3. `prop.testId` 与 `data-*` 的全集：diff 是空的

三组 grep，各自 `sort | uniq -c` 之后对照（**范围是整个 `src/Janpo.Web/*.fs`**，
不只被拆的那个文件——这样连「把一个钩子搬去别的文件」也会露出来）：

```bash
grep -rhoE 'prop\.testId +(\$?"[^"]*"|[A-Za-z][A-Za-z0-9_.]*)' src/Janpo.Web/*.fs | sort | uniq -c   # 32 行
grep -rhoE '"data-[A-Za-z0-9-]+"'                              src/Janpo.Web/*.fs | sort | uniq -c   # 34 行
grep -rhoE '\$?"(table|seat|danger)-[^"]*"'                    src/Janpo.Web/*.fs | sort | uniq -c   # 59 行
```

（第三组是「传给 `tileRow` / `field` / `button` / `picker` 的那些 testId 字面量」——
它们不写在 `prop.testId` 后面，只查前两组会漏掉一多半钩子。）

```
--- testid
(空)
--- data
(空)
--- hooks
(空)
```

无头闸门那七趟因此一趟都不用改，也确实一趟都没改。

## 4. 每一处「不是纯搬家」的改动

拆前拆后逐行对照的做法：把五个新文件按编译顺序 `cat` 成一份，与拆前的 `TablePage.fs` **各自排序后 diff**
（排序是为了绕开「同样的行搬去了别处」这种纯位移）。结果：**只有 18 行消失**，
新增的都是 §4.1–§4.5 这几类。18 行原文如下，逐条对上号：

```
< /// 牌桌页面：MVU 三件套加视图。                                          ← 1 行，§4.4
<     let private canAdvance (model: TableModel) : bool =                    ← 8 行，§4.2
<     let private fallenBack (latest: Turn option) : string =
<     let private agentLine (model: TableModel) (table: Table) =
<     let private usageLine (table: Table) =
<     let private tableBody (model: TableModel) (table: Table) =
<     let private controls (model: TableModel) (dispatch: TableMsg -> unit) =
<     let private viewpoints (model: TableModel) (dispatch: TableMsg -> unit) =
<     let private llmPanel (model: TableModel) (dispatch: TableMsg -> unit) =
<         let running = canAdvance model                                     ← 9 行，§4.3
<         let pending = renderingPending model
<                         agentLine model table
<                     @ usageLine table
<                             prop.custom ("data-fallback", fallenBack table.Latest)
<                 controls model dispatch
<                 viewpoints model dispatch
<                 llmPanel model dispatch
<                 | Ok table -> tableBody model table
```

### 4.1 每个新文件的 `namespace` + `open` 头（5 处）

必须：一个文件一份。**只开这个文件真用得到的**，没有整份照抄原来那六行——
于是 `TableState` 不 `open Feliz`（MVU 里一行 Feliz 都没有），
`TableBoard` / `TablePanel` / `AgentLine` 不 `open Elmish`（它们只发消息不造 `Cmd`）。
这不是「顺手优化」而是编译器的要求：多开一个用不到的命名空间在这里没有语义，
但少开一个必需的就编不过——五份 `open` 都是按编译错误逼出来的最小集。

### 4.2 八处 `private` → `internal`

`canAdvance`（`TableState`）、`fallenBack` / `agentLine` / `usageLine`（`AgentLine`）、
`tableBody`（`TableBoard`）、`controls` / `viewpoints` / `llmPanel`（`TablePanel`）。

必须：这八个原来是同一个模块内部的调用，拆开之后跨了文件，`private` 就够不着了。
**用 `internal` 而不是公开**：`internal` 只到程序集边界为止，`tests/Janpo.Web.Tests`（另一个程序集）
看不见它们——公开面因此没有多出八个符号（§2 那句话就靠这一条成立）。

### 4.3 九处调用点加模块名

`TableState.canAdvance` / `TableState.renderingPending`（`TablePanel` 里两处）、
`AgentLine.agentLine` / `AgentLine.usageLine` / `AgentLine.fallenBack`（`TableBoard` 里三处）、
`TablePanel.controls` / `TablePanel.viewpoints` / `TablePanel.llmPanel` / `TableBoard.tableBody`
（`TablePage` 外壳里四处）。

必须：五个模块都带 `[<RequireQualifiedAccess>]`（照原来那一个模块的写法），跨模块只能限定调用。
**函数一个都没改名**——`AgentLine.agentLine` 读着确实有点重复，但改名会让「这是纯搬家」这件事
再也没法用一次 diff 证明，票面的价值判据压倒了这点观感。

### 4.4 五段新的模块文档 + fsproj 里那段注释

必须：原来那句模块注释是「牌桌页面：MVU 三件套加视图」——**拆完之后它对五个文件里的哪一个都不成立**。
五段新注释各写「这个文件装什么、旁边那几块在哪个文件、拆的判据是哪张票会改它」；
`Janpo.Web.fsproj` 里那段注释写清那五行的先后**就是编译顺序**以及排它的两条约束。
后面七张票要靠这几段话知道该往哪个文件里加东西。
**除这五段之外，原文的注释一行没删、一个字没改**（包括 `// ---- 视图：牌 ----` 这类分节标记，
虽然有两个文件里它现在与模块文档说的是同一件事——留着是因为删一行也算改动）。

### 4.5 `TablePage` 里五个转出入口（新代码）

```fsharp
let initial (llmAt: Seat option) (config: LlmSeat) : TableModel * Cmd<TableMsg> = TableState.initial llmAt config
let init () : TableModel * Cmd<TableMsg> = TableState.init ()
let update (message: TableMsg) (model: TableModel) : TableModel * Cmd<TableMsg> = TableState.update message model
let rosterOf (model: TableModel) : Roster = TableState.rosterOf model
let renderingPending (model: TableModel) : bool = TableState.renderingPending model
```

必须：理由整个在 §2（FS0248 + `Main.fs` 不动 + 用例的入口不许改名）。
签名与拆之前**逐字相同**，因此 `tests/Janpo.Web.Tests` 一个字没动、101 条用例一条没改就直接绿。

**除上面五类之外，`jj diff` 里没有第六类改动。**

## 5. 加证：Fable 生成的 JS 只多了那五个包装

DOM 没变这件事，除了七趟浏览器闸门全绿之外，另拿生成物对了一遍
（把工作区临时还原成拆前状态跑一次 `pnpm run fable`，与拆后的生成物对照）：

- 拆前：`web/src/generated/TablePage.js`
- 拆后：`TableState.js` + `AgentLine.js` + `TableBoard.js` + `TablePanel.js` + `TablePage.js`

归一化两件与语义无关的东西——模块名前缀（`TablePage_agentLine` → `agentLine`）与
**Fable 自己给同名 import 加的消歧后缀**（`map_1` ↔ `map`、`singleton_1` ↔ `singleton`、
`Viewpoint_1` ↔ `Viewpoint`……拆文件会换一批），再排序对照：

```
归一化后：before 786 行 / after 801 行；差异 15 行
+function init() {            +return init();              +}
+function initial(llmAt, config) {   +return initial(llmAt, config);   +}
+function renderingPending(model) {  +return renderingPending(model);  +}
+function rosterOf(model) {   +return rosterOf(model);     +}
+function update(message, model) {   +return update(message, model);   +}
```

**零删除、零修改、只多出 §4.5 那五个包装函数。** 另外未归一化的那份 diff 里还有一批 `/** … */`：
那是 `private` → `internal` 之后 Fable 开始把这些函数的文档注释一并输出（导出的才带 JSDoc），
同样不改行为。

## 6. 验收与数字

```
== CI 全绿 ==   real 0m37.2s（基线 0m36.2s）
已通过! - 失败: 0，通过: 101，总计: 101 - Janpo.Web.Tests.dll     ← 与基线逐字相同
已通过! - 失败: 0，通过: 737，总计: 737 - Janpo.Engine.Tests.dll  ← 与基线逐字相同
七趟浏览器闸门：tracer / board / golden / export×3 / redaction 全 ✓
dotnet fantomas src/Janpo.Web/ → Formatted 0，Unchanged 18（新文件一处都没被重排）
```

`jj diff --stat`：

```
.../llm-riichi-arena/issues/70-split-table-page.md |   26 +-
src/Janpo.Web/AgentLine.fs                         |   83 +
src/Janpo.Web/Janpo.Web.fsproj                     |   10 +
src/Janpo.Web/TableBoard.fs                        |  744 +++++++++
src/Janpo.Web/TablePage.fs                         | 1645 +---------------------
src/Janpo.Web/TablePanel.fs                        |  364 ++++
src/Janpo.Web/TableState.fs                        |  472 ++++++
7 files changed, 1717 insertions(+), 1627 deletions(-)
```

`web/scripts/**`、`web/src/**`、`src/Janpo.Engine/**`、`App.fs`、`Main.fs`、`styles.css`、
`docs/adr/*`、`tests/**` 在 `jj diff` 里一个都不出现。

## 7. review 结论（Standards + Spec 两轴，fixed point `af7ec21f`）

- **Spec**：票面三条验收 + 三条边界逐条对上（§3 / §4 / §6），四个落点分别在四个文件里（§1）。
- **Standards**：`fantomas --check` 与 `scripts/check-style.sh` 全绿；
  `docs/agents/fsharp-style.md` 的规则 1–9 与本票无交集——除五个转出包装外没有一行新表达式，
  那五个也不是嵌套应用（规则 4.1 的形状：`f x` 一层）。
- **blocking：无。**
- **只记录不改的 nitpick 两条**：
  1. `AgentLine.agentLine` / `AgentLine.usageLine` 读着重复（模块名与函数名同词根）。
     没改是因为改名会毁掉「纯搬家」的可证性（§4.3）；票 74 / 76 真要动这块时顺手改最省。
  2. `TableBoard.fs` 744 行仍偏大（一家的三排牌 + 桌心 + 结算 + 危险度）。
     没再拆是因为票面的判据是那张表的四行分开，而这四块里只有票 76 会动它；
     真到票 76 觉得挤，再按「座位格子 / 结算 / 危险度」切一刀即可，那时有具体需求当判据。

## 8. 留给人的待审项

- **无阻塞项。** 唯一需要人点头的是 §2 那个形状（`TablePage` 从「一个大模块」变成
  「五个转出 + 外壳」）：它是 `Main.fs` 不动 + 用例入口不改名 + F# FS0248 三条约束的**唯一交点**，
  不是取舍。真要去掉这一层，代价是改 `Main.fs` 与 `tests/Janpo.Web.Tests` 的调用点。
