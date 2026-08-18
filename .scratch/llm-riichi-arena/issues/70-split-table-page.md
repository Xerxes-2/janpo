# 70 — 拆开 `TablePage.fs`（零行为改动的前置）

**What to build:** 什么行为都不变。`src/Janpo.Web/TablePage.fs` 现在 1647 行，
里面挤着页面状态与 MVU、配桌与模型面板、牌桌与结算的视图、危险度面板、Agent 层状态线。
**M2 剩下的八张票全撞在这一个文件上**，不拆就只能一路串行、且每次集成都是一场 rebase。
按职责拆成几个文件，DOM、testId、CSS 类名与闸门一个字都不改。

**Blocked by:** None — can start immediately.

**Status:** ready-for-human

## 怎么拆

分文件的判据是**「哪张后续票会改它」**，不是「代码看起来像一类」：

| 大致的落点 | 里面装什么 | 会改它的票 |
|---|---|---|
| 页面状态与 MVU | `TableModel` / `TableMsg` / `init` / `update` / 那几个 `Cmd` | 71、72、73、74 |
| 配桌与模型面板 | `llmPanel` / `controls` / `viewpoints` / `textField` 那几个控件工厂 | 72、73 |
| 牌桌与结算的视图 | `tableBody` / `seatPanel` / `tableCenter` / `settlementPanel` / `resultPanel` / 危险度面板 | 76（气泡挂座位格子） |
| Agent 层的状态线 | `agentLine` / `usageLine` / `fallenBack` | 74、76 |

具体几个文件、叫什么名字由你定（`Janpo.Web.fsproj` 的 `<Compile>` 顺序是编译顺序，F# 只往前看）。
**判据是：拆完之后上表那四行分别落在不同文件里。**

- [x] 拆完 `TablePage.fs` 不再超过 ~400 行（或者干脆只剩一个 `Page ()` 外壳）
      —— **58 行**：五个转出入口 + `Page ()`
- [x] 公开签名不减：`TablePage.initial` / `rosterOf` / `renderingPending` 是 dotnet 侧用例的入口
      （`tests/Janpo.Web.Tests`），一个都不许变私有或改名
      —— 连 `init` / `update` / `Page` 一共五个原样在；跨文件的助手一律 `internal`，**程序集的公开面一个符号不多**
- [x] `prop.testId` 与 `data-*` 的**全集逐字不变**（拆前拆后各 `grep -o` 一遍排序对照，贴进报告）
      —— 三组 grep（testId / `data-*` / `table-*`・`seat-*` 字面量）的 diff 全空，报告 §3

## 验收

- [x] `./scripts/ci.sh` 全绿，**web 那七趟一道都没改**（`web/scripts/*.mjs` 在 `jj diff` 里不出现）
      —— 37.2s 全绿；`jj diff --summary` 只有 `src/Janpo.Web/` 那六个文件
- [x] `jj diff --stat` 只有文件搬家、`namespace`/`open`/`<Compile>` 的必要调整，**没有一行逻辑改动**；
      报告里逐条说明每一处「不是纯搬家」的改动为什么必须
      —— 拆前拆后逐行对照：**只少了 18 行**（8 处 `private`→`internal` + 9 处调用点加模块名 + 1 行旧模块注释），报告 §4
- [x] 截图不必重出（渲染结果按定义未变）；真要变了就是这一票做错了
      —— 未重出；另拿 **Fable 生成的 JS** 对照了一遍（归一化后只多那五个转出包装，报告 §5）

## 边界

- [x] 不碰 `src/Janpo.Engine/**`、不碰 `web/src/agent/**`、不碰 `web/scripts/**`、不碰 `web/src/styles.css`
- [x] 不顺手改任何行为、不顺手改文案、不顺手「优化」——这一票的价值全在「diff 里没有惊喜」
- [x] `App.fs`（曳光弹）与 `Main.fs`（外壳）不动；地址与路由是票 71 的事

## 为什么先做它

M2 的九张票里有七张要改这个文件的不同区域。M1 的集成经验（`DECISIONS.md` 的 W2 一节）是
**语义撞车比文本冲突危险**：两票各自绿、合流即红。先把「谁改哪一块」变成「谁改哪个文件」，
后面才谈得上编波。
