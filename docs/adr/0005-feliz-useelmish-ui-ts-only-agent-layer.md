# ADR-0005：UI 形态取 Feliz + useElmish，TypeScript 只剩 Agent 层

**日期**：2026-08-16
**状态**：已接受

## 背景

spec 的「UI（React 底座，具体形态 M1 定夺）」一段留了两个候选，说好在 M1 实测后定夺：

- **A：原生 TS + React**。与 Agent 层同语言，生态摩擦最小。
- **B：Feliz + useElmish**（F# 的 React 封装）写牌桌核心，配置表单等杂活仍用普通 hooks 风格。

M0 结束时的事实是：**规则引擎的全部 8,000 行是 F#**，且它的对外接缝是「事件流 + 当前合法动作集」
（spec 的接缝决策）。18 票又实测了 pi-ai 在浏览器里可用，因此不存在「必须有个 TS 后端」的外力。
19 票把 A/B 的成本第一次量了出来。

## 决定

**取 B：牌桌核心组件用 Feliz + useElmish，TypeScript 只剩 Agent 层。**

边界具体是这样划的：

| 层 | 语言 | 内容 |
|---|---|---|
| 规则引擎 | F#（dotnet + Fable 双目标） | `GameState`、`step`、事件流、合法动作集、分析附件 |
| UI 与编排 | F#（Feliz + useElmish） | 牌桌、播放控制、配桌表单、MVU 的 update 循环 |
| Agent 层 | TypeScript | LLM provider 调用、prompt 渲染、重试与超时、DecisionRecord 的采集 |

跨界只有一个方向是廉价的，所以只往那个方向走：**F# 调 TS**。

## 核心论据：不对称性

**F# 调 TS 只需 `import` 一个返回 Promise 的函数**：

```fsharp
// Fable 侧：一行声明就把 TS 的一个异步函数接进来，无需任何类型翻译
[<Import("decide", "../agent/decide.ts")>]
let decide (packet: string) : JS.Promise<string> = jsNative
```

**TS 调 F# 要给每个跨界类型写 codec 或手写 `.d.ts`**。19 票实测了这条：Fable 的输出**不带**
`.d.ts`，`web/src/main.ts` 里那一行

```ts
import { mount } from "./generated/Main.js";
```

在 TS 看来是 `any`。今天它只是一个 `string -> unit`，无所谓；可一旦让 TS 侧去消费
`GameState` / `Event` / `LegalActions`，就得为每个类型手写声明或写一对 encoder/decoder——
而这些类型正是**会随规则长尾不断变形**的那批（M0 里跨票加 DU case 发生过四次）。
选 A 等于把这份翻译工作**按类型数量**长期承担下去；选 B 等于只承担一个函数签名。

顺带记一条 19 票量到的数：这也是为什么本决定要求跨界**只传字符串与 id**，
而不是把引擎类型摊平给 TS——见「后续票要遵守的边界」。

## 次要论据

1. **MVU 的 update 循环与引擎的事件溯源同构。** 牌桌状态 = `fold(事件前缀)`（ADR-0002 的
   Replay 就是这个），Elmish 的 `update: Msg -> Model -> Model` 与 `step: GameState -> Action ->
   GameState` 是同一个形状。回放时间轴因此近乎免费：把事件前缀换一个长度重 fold 即可，
   不需要第二套状态。
2. **合法性驱动 UI 在同一语言内成立。** 按钮由引擎给的合法动作集渲染（spec 的 UI 决策），
   而 `LegalActions` 是 F# 的 DU；在 F# 里 `match` 它是穷尽的，编译器会在加动作时把所有渲染点
   找出来。跨到 TS 就退化成字符串比较。
3. **19 票实测：Fable 编译引擎零源码改动。** 8,028 行引擎一行没改就编过了（`[<Struct>]`、
   `System.String.IsNullOrEmpty`、`uint32` 的位运算、34 长计数数组的原地循环都没踩雷），
   `#if FABLE_COMPILER` 一处没用。所以 B 的「双目标维护成本」这项顾虑，实测值是 0。
4. **产物尺寸不构成理由。** 引擎 + Feliz + React 打包后 279 kB（gzip 88 kB），
   与 18 票量到的 pi-ai provider chunk（171 kB / gzip 44 kB）同量级。

## 被否决的 A（原生 TS + React）

A 的卖点是「与 Agent 层同语言」。它成立的前提是**牌桌与引擎之间的数据量小**，
而实际情况相反：牌桌要渲染的东西（手牌、河、副露、宝牌、点数、合法动作集、每手的分析标注）
几乎就是 `GameState` 的全部投影。选 A 就是把这条最宽的边界放在语言分界线上。

spec 写过「先 A 后迁 B 的成本可控」。这句在**牌桌组件**上成立（两者底层同为 React），
但在**跨界 codec** 上不成立：A 期间写下的每个 codec 都是纯粹的沉没成本，迁 B 之后一行都不留。
既然 M1 一开始就要动牌桌，就没有理由先付这笔钱。

另一个被否掉的是**在 F# 侧也写 Agent 层**（即完全不用 TS）。否掉的理由是 provider 生态：
pi-ai 是 TS 包，各家官方 SDK 也都是 TS；用 Fable 去消费它们，等于把上面那份不对称性
原样搬到另一条边界上——只是这次是 F# 侧要写 `.d.ts` 的镜像。**边界该放在生态的分界处，
而不是放在数据最宽的地方。**

## 后续票要遵守的边界

1. **`GameState` 是不透明句柄，永不序列化跨界。** TS 侧拿到的是「决策包 JSON」与「动作 id」，
   **TS 永不构造 `Action`**——它只回一个 id，由 F# 侧翻回 `Action` 再交给引擎。
   这条使「非法状态不可表示」在跨界处不被绕开。
2. **prompt 在 TS 侧渲染**，F# 只出结构化决策包。渲染层的中文文案（ADR-0001 的 `toDisplay`）
   要进 prompt 时，由决策包携带**已经渲染好的字符串**，不是让 TS 去查术语表。
3. **不给 Fable 输出写 `.d.ts`。** 需要类型保证就把那段逻辑挪回 F#。
   `web/src/main.ts` 那一行 `mount` 是唯一允许的例外，因为它只有一个字符串参数。
4. **Fable 运行时后端属于 Web 工程**（`Thoth.Json.JavaScript`、`Feliz`、`Fable.Elmish`），
   不得进 `Janpo.Engine.fsproj`。`scripts/ci.sh` 的依赖白名单是这条的闸门。

## 后果

- **配置表单等杂活也在 F# 里写。** spec 原话是「杂活仍用普通 hooks 风格」，落地为
  Feliz 的 `React.useState` 等 hook——**同语言，不同风格**，不是另一个工程。
- **TS 侧没有类型闸门（暂时）。** 19 票没装 `tsc --noEmit`：今天 TS 的全部内容是一行
  `mount("janpo-root")`，装了也检不出东西，而它会把 Fable 的上万行输出拖进 tsc 的
  program。等 23 票的 Agent 层真的有 TS 代码了再装，届时把 `src/generated` 排除在外。
  在那之前 TS/JS 侧的闸门只有 Biome（格式 + lint）。
- **两侧的语义漂移要靠对拍守。** 同一份 F# 源码经两个编译器（Roslyn/FSC 与 Fable），
  数值与集合语义理论上可能分叉。19 票钉了第一颗曳光弹（同种子逐字对拍），
  21 票把它系统化成黄金用例。**不许为 Fable 分叉引擎逻辑**；真的必须分叉时用
  `#if FABLE_COMPILER` 并在 `DECISIONS.md` 记清语义差异。
- **人手要求变了**：改牌桌需要 F#，不再是「会 React 就行」。这是本决定最实的代价，
  接受它的理由是这个项目的复杂度**在规则里**，不在界面里。
