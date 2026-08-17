# 43 — `render_version` 认不出渲染器代码的改动

**结论：走第 (a) 条——把渲染器代码也纳入版本号。**
`renderVersion` 从 `模板 id@内容哈希` 变成 **`模板 id@模板哈希.渲染器摘要`**，
后一截由「从 prompt 的渲染入口出发、沿值导入可达的那几个文件」算出来，
**有三道闸门逼着它跟着源码走**（用例、CI、构建）。牌谱格式版本 2 → 3。

---

## 1. 为什么是 (a) 而不是 (b)

派工单给的判据是「**能自动归因 > 手动记账，但假的自动归因比诚实的手动记账更糟**」。
(b)（改名 `template_version` + 实验记录里附提交号）是诚实的，但它把归因外包给一件
**没有任何东西执行**的纪律：M2 要跑三档 × 若干种子 × 若干模型的对照，
牌谱是唯一可分享物（ADR-0002），而「当时是哪个提交」不在牌谱里——
分享出去的那份数据自己说不清自己是怎么渲出来的。

(a) 立得住的前提是能同时满足票面那两条：**改渲染器一定让它变、改无关代码不该变**。
下面这三个选择就是为了这两条：

| 决定 | 为什么 | 被否决的 |
|---|---|---|
| 覆盖面＝**从 `prompt.ts` 出发沿值导入可达的文件**（今天是 `prompt` / `history` / `template` / `wording` 四个） | 手写名单会在有人把函数拆进新文件时静静漏掉；整个 `src/agent/` 或整个 bundle 会让改一句 `loop.ts` 的重试逻辑都换一个版本号 | ① 手写文件名单；② 整个 agent 目录；③ 整个 bundle / 整个仓库的哈希 |
| **纯类型导入不跟**（`import type … from "./types.ts"`） | 类型编译后一个字节都不剩，改它排不出一个不同的 prompt | 跟着所有 import 走（`types.ts` 一改就废掉全部缓存标记） |
| 摘要是**生成物常量**（`src/agent/renderer-digest.ts`），不是运行期现算 | 浏览器、`node --test`、离线脚本（`print-prompt.mjs`）、CI 读到的必须是**同一个字面量**。运行期现算只有两条路：把源码打进 bundle（`?raw` 在 node 里读出来的是模块不是文本，两边算出不同值），或 `Function.prototype.toString()`（被 esbuild 压缩改写，dev 与 prod 不同值） | ① Vite `define` 注入 + node 侧 `fs` 回退（两条代码路径，浏览器里还得防 `node:fs` 被打进去）；② `toString()` 哈希 |

**Fable 那侧不用拿它**：版本号在 Agent 层算，随决策回执过界（`Agent.fs` 的 `answerDecoder`），
F# 只当它是一串不透明的键——这条链票 31 就是这么定的，本票一字未改。

### 生成物要人手重算，这不是漏洞，是闸门

代价说清楚：改了渲染器要跑一次 `pnpm run render-digest`。**忘了会当场红**，三处各拦一次：

1. `web/tests/agent/render-version.test.ts` 的新鲜度用例（进 `pnpm test`，即 CI 第三道）；
2. CI 跑的就是同一道（`scripts/ci-web.sh`）；
3. **`vite build` 的插件**（`vite.config.ts` 的 `janpo-renderer-digest`）——它拦的是
   「有人绕过用例直接 `pnpm run build` 发出去」那条路：**发出去的产物不许带一个说谎的版本号**。

## 2. 反向自证：实测两例

### 例一：改渲染器 → 版本号变（而且不重算就先红）

改的是 `prompt.ts` 里自家那一节的一行抬头（`牌河：` → `牌河（从左到右按打出顺序）：`）：

```
--- 版本号（重算之前）: janpo-default@08fcaec3.4b9e57c0
✖ 生成的渲染器摘要与此刻的源码一致（改了渲染器就必须重算）
  AssertionError: 渲染器的源码变了而 src/agent/renderer-digest.ts 没重算：跑 `pnpm run render-digest`
  + actual - expected
  + '4b9e57c0'
  - 'edf6f703'

$ pnpm run render-digest
src/agent/renderer-digest.ts 重算好了：edf6f703
--- 版本号（重算之后）: janpo-default@08fcaec3.edf6f703
```

**模板哈希 `08fcaec3` 一个字符没动**——「措辞没换、换的是排版的代码」在版本号上直接读得出来。

构建期那道闸门单独自证过一次（改 `wording.ts` 的一个措辞、不重算，直接 `vite build`）：

```
[janpo-renderer-digest] 渲染器的源码变了（现在是 16579d67）而 src/agent/renderer-digest.ts
还写着 4b9e57c0：跑 `pnpm run render-digest` 重算，否则牌谱里的渲染版本号会指错人。
```

### 例二：改无关代码 → 版本号一个字符不动

同一把尺子量另一头：`src/agent/loop.ts` 加一行注释、`README.md` 加一行注释，两处一起改：

```
--- loop.ts 与 README 各改一行之后: janpo-default@08fcaec3.4b9e57c0
渲染器摘要 4b9e57c0
```

两例都是**真的改了工作区的文件**再跑出来的，改完已复原（`jj st` 里没有它们）。
另有五条同样的性质**钉在用例里**（每次 CI 都跑，不是一次性演示）：

- 渲染器那四个文件**逐个**改一行 → 摘要每一次都必须变；
- `loop.ts` / `types.ts` / `retry.ts` / `render-version.ts` / `README.md` 改了 → 摘要必须一字不变；
- 把渲染拆进一个新文件（多行 `import`）→ 它**自动**进摘要；
- 纯类型导入不跟；
- 走查读不懂的 `import`（动态 `import()`、没接完的多行 `import`）→ **当场抛**，
  不静静少读一个文件。

## 3. 牌谱兼容性：格式版本 2 → 3，怎么验的

`render_version` 的**含义**变了（同一个字段名，v2 的值回答不了「渲染器换没换」），
按裁决 26「改含义要涨版本」涨到 3；`Paifu.supported = [1; 2; 3]`。
**字段名与结构一字未改**——v2 与 v3 写出来的形状逐字相同，
区分两者的只有牌谱头那个 `version`，`recordEncoderFor` 因此仍只分 v1 与其余两支。

验的三件事（`tests/Janpo.Engine.Tests/PaifuTests.fs`，全部新增用例）：

1. **v2 读得动**，那一串老版本号**原样读进来**——不给它补一截假的渲染器摘要；
2. **v2 内部那把键仍然对得上**：同一份牌谱里 `DecisionRecord.RenderVersion` 与
   `Prompting.Preambles[].RenderVersion` 写的是同一串，`Prompting.preambleFor` 照样取得回
   那份 preamble（**票 31 定的那条链没断**，`rebuildMessages` 重建得出当时那两条消息）；
3. **v2 读进来再写出去仍是 v2**（沿用 31-5 的判据：不把当年那个版本号说成它懂渲染器）。
   v1 那两条老用例原样通过。

跨版本读**新**牌谱的老引擎会得到 `unsupported paifu version: 3` ——这正是涨版本要的效果，
那条用例也在（把版本改成 99 的那一条）。

## 4. `CONTEXT.md` 那条词条的最终文本

本票获准改的**只有这一个词条**（第四次授权，范围锁死）。改后的全文：

```
**RenderVersion（渲染版本）**：
`模板 id@模板哈希.渲染器摘要`，进 DecisionRecord，并与座位一起做 Paifu 里那几份 preamble 的键。
看见缓存命中率掉下来时，它是一眼归因到「换了渲染」的那个字段，而两截分得出是哪一种：
**前一截变＝换了措辞**（人格、规则说明、抬头、五张措辞表），**后一截变＝换了排版的那几行代码**
（摘要覆盖的是“从 prompt 的渲染入口出发、沿值导入可达”的那几个文件）。
**两截都是算出来的，不是手填的**：没有任何东西保证改措辞、改排版的人会去 +1。
**它不覆盖的三样**：工具定义的形状（整场逐字存在 Paifu 里，要归因直接 diff 那一段）、
决策包本身（那是引擎的事）、以及问了几次与选了哪一档（前者看 Attempts，后者看尾部有没有那一节）。
**含义跟着 Paifu 的格式版本走**：格式 2 那一版只有前一截，那些值回答不了「渲染器换没换」。
_Avoid_: 把它读成 Paifu 的格式版本（那是 `Paifu.Version`）；也别拿它当「缓存一定命中 / 一定没命中」的判据
```

体例照票 48 的 48-1：**不写票号**，局限（不覆盖哪三样）**写死在词条里**，
读者不会把它当成「prompt 逐字节是哪一份」的完备指纹。

## 5. 改了哪些文件

| 文件 | 改了什么 |
|---|---|
| `web/src/agent/render-version.ts` | **新**：`templateDigest` + `renderVersion`（从 `template.ts` 搬出来），拼上渲染器摘要 |
| `web/src/agent/hash.ts` | **新**：`fnv1a`（从 `template.ts` 搬出来）。单独一个文件是为了让走查用得上它而不必 import 自己的生成物 |
| `web/src/agent/renderer-digest.ts` | **新，生成物**：一个常量 + 覆盖了哪几个文件的清单 |
| `web/scripts/renderer-digest.ts` | **新**：走查 + 摘要 + `--write` 重算 |
| `web/src/agent/template.ts` | 删掉版本号那一节（搬走），文件头那段说明改成两截的说法 |
| `web/src/agent/loop.ts`、`web/scripts/print-prompt.mjs` | 换 import；`loop.ts` 那行字段注释改口径 |
| `web/vite.config.ts` | 加构建期的新鲜度插件 |
| `web/tests/agent/render-version.test.ts` | **新**：8 条（新鲜度、覆盖面、两向反向自证、走查的三种读不懂、两截分得开） |
| `web/tests/agent/{template,record}.test.ts` | 版本号形状的断言改成两截；模板那一半的钉子改钉 `templateDigest` |
| `src/Janpo.Engine/Paifu.fs` | `version = 3`、`supported = [1;2;3]`，三处注释写清 v2/v3 的差别 |
| `tests/Janpo.Engine.Tests/PaifuTests.fs` | 新增 v2 的两条用例；固件里的版本号串换成新形状 |
| `src/Janpo.Web/{Agent,TablePage}.fs`、`tests/Janpo.Web.Tests/*` | 注释口径 + 固件串；**外加 47-A**（见下） |
| `CONTEXT.md` | 只改 `RenderVersion` 一个词条 |
| `docs/development.md`、`scripts/ci-web.sh` | 各加一行：新命令与新闸门 |

**引擎的规则逻辑一行未动**（票面边界「不碰引擎」）：`Paifu.fs` 动的是牌谱格式版本号与注释，
而涨版本正是票面第三条验收自己要求的。**prompt 的措辞与结构也一字未改**——
默认模板的模板哈希仍是 `08fcaec3`（票 31 那个数），前缀字节属性测试全绿。

## 6. 顺带收的 47-A

`TablePage.fs` 那段帮助文字仍写「重试两次仍不行就兜底代打」，而票 47 已把不值得重试的
那一类改成立刻兜底。按 DECISIONS 47-A 的建议与票 47 报告里那两句收尾句的口径改成：

> ……重试两次仍不行就兜底代打（裸奔档摸切，信息辅助档打一张不退向听的）；**认证失败这类
> 再问一遍还是一样的错不重试，直接兜底**。对局不会卡住。

**只改了这一句**，那一段的其余部分一字未动。

## 7. 验收

- `./scripts/ci.sh` 全绿（含浏览器内那六道：曳光弹对拍、牌桌八项、黄金用例、
  牌谱导出两趟、两道 key 闸门）；`dotnet fantomas .` 干净；
  `pnpm run check` / `typecheck` 干净；`dotnet test` 817 条全绿；`node --test` 154 条全绿。
- **前缀字节稳定性属性测试仍然绿**（`prefix.test.ts`，本票没动渲染出来的任何字节）。
- **版本号没有进 prompt**（裁决 31-6）：它只在回执与牌谱里，`promptSections` 一如既往
  不知道版本号这回事。

## 8. Standards 轴自评（无法派生 sub-agent，顺序自跑）

- `docs/agents/fsharp-style.md`：本票的 F# 改动是注释、一个常量与两条用例，
  没有新的嵌套应用、没有新的 `let mutable`；`scripts/check-style.sh` 绿。
- **Mysterious Name**：`rendererDigest` / `rendererFiles` / `RENDERER_ROOT` 直说它们是什么；
  生成物与生成它的脚本**同名**（`src/agent/renderer-digest.ts` 与 `scripts/renderer-digest.ts`），
  是有意的对仗，两边的文件头互相点名。判断题，留着。
- **Duplicated Code**：`fnv1a` 全仓只有一份（`hash.ts`），模板那一半与渲染器那一半共用它。
- **Speculative Generality**：`ReadSource` 这个注入点**不是**为了将来——
  两向反向自证（改渲染器 / 改无关代码）在内存里做才不会污染工作区，用例正用着它。
  曾经写过 `renderVersion(template, renderer?)` 这样一个只给用例用的默认参数，
  **删了**：它让 `.map(renderVersion)` 变成一个 `.map(parseInt)` 式的陷阱（tsc 当场逮到），
  而它能证的东西 `endsWith(RENDERER_DIGEST)` 就够。
- **Shotgun Surgery**：版本号形状一变，注释与固件串散在 8 个文件里要跟着改。
  这是「同一件事的说法有几处」的老问题；本票把**算的地方**收敛成了一处
  （`render-version.ts`），F# 那侧仍然只当它是不透明的键。

## 9. 留给人的待审项

1. **摘要的覆盖面止于「排 prompt 字节的那几个文件」**：`loop.ts` 决定问几次、
   把哪两段拼成 system / user，它改了版本号不变。这是有意的
   （它每周都在改，纳进来等于让版本号天天跳），代价是「谁调的渲染器」这一层没有指纹。
   今天 `messagesOf` 与三段的拼法都在 `prompt.ts` 里，因此这个口子很窄；
   **哪天有人把拼装挪进 `loop.ts`，这条就该重新判**。
2. **FNV-1a 32 位**：两截各 8 位十六进制，不是密码学用途（沿用 31-6 的判断）。
   碰撞概率对「版本号」这个用途够用，但它挡不住有意构造。
3. **CRLF**：`readSource` 把换行归一成 LF 再哈希，因此 Windows 上 checkout 出来的
   同一份代码算出同一个摘要。没有真在 Windows 上验过（本仓库只在 Linux/nix 上跑）。
4. **v2 牌谱的读法**：那一版的 `render_version` 只说得了模板。M2 若要把 M1 期间的牌谱
   一起进统计，得按 `version` 分开处理——`Paifu.Version` 就是那个判据。
