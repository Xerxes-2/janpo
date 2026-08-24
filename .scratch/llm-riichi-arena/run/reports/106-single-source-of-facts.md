# 票 106：同一事实写在多处 —— 实现报告

**结论先说五句。**

1. **趟数现在只有一处真源**：`web/scripts/verify-browser.mjs` 的 `gates` 数组。
   真加过一趟假闸门自证：**零处「几趟」需要手改**，跑起来两处总述自动从 18 变 19，撤掉后逐字复原。
2. **产物路径从 7 处收到 5 处，剩下 4 处收不动的逐处写明了理由**，并且**每一处都由
   `scripts/check-single-source.sh` 按字面钉在真源上**——四种改坏法各红过一次。
3. **`ci.sh` 里现在有许可闸门了**：删掉 `web/public/third-party/NOTICE-akagi`，
   `./scripts/ci.sh` **exit 1**，红的原文在 ③。源与分发件跑的是同一份 `check-third-party.sh`。
4. **那些写死的 `✓`**：扫了 **21 份**（20 个 `verify-*.mjs` + `browser-lane.mjs`）里的 **78 处**，
   **真无条件打印的只有 9 处、在 3 份文件里**（其余 69 处都在判决之后、在 `else` 分支里，或本来就由数据算）。
   9 处全改成由数据决定；约定与三个助手（`tick` / `mark` / `markerSince`）落在 `browser-lane.mjs`。
   **票面那一幕逐字复现过**（页面 0.7653265 / wasm 0.765），改前照旧打勾、改后打叉，**计数没动**。
5. `./scripts/ci.sh` **全绿**（`/tmp/106/ci-final.log`），十八趟一趟不少，
   闸门列表与改动前 `diff` 逐条相同。

改到的文件：

| 文件 | 改了什么 |
| --- | --- |
| `web/scripts/verify-browser.mjs` | 趟数改成从 `gates.length` / `results.length` 现算；删掉文件头那份手写的逐趟清单（它自己已经漂出两个 `17`）；`gates` 表补上 tracer / golden 两趟的票号；那个总述勾改用 `mark()` |
| `scripts/ci-web.sh` | 删掉 145 行逐道叙述（那是第三份抄件）+ 全部写死趟数；加两道静态闸门 |
| `web/scripts/browser-lane.mjs` | **新增约定 + `tick` / `mark` / `marker`**；去掉「十道」 |
| `web/scripts/baseline-asset.mjs` | **新增**。node 那三个脚本共用的一处产物路径 |
| `scripts/check-single-source.sh` | **新增**。趟数与产物路径两件的执行体 |
| `scripts/check-third-party.sh` | **新增**。许可件那一道，源与分发件共用 |
| `scripts/check-pages-dist.sh` | ① 那一段换成调共用脚本 |
| `.github/workflows/pages.yml` | 只动 `BASELINE_WASM` 上面那段注释（说清为什么收不动、谁在钉它） |
| `verify-review / setup / seats` | 9 处写死的勾改成由数据决定 |
| `verify-baseline / review / candidates-shape` | 三处 `ASSET` 常量改成读 `baseline-asset.mjs` |
| `verify-board / bubbles / export / golden / home / human / redaction / tracer`、`docs/development.md` | 只去掉写死的趟数（`verify-tracer` 那一行 `✓/✗` 顺手改用 `tick`） |

**没碰**：`.github/workflows/ci.yml`、`src/Janpo.Engine/**`、`src/Janpo.Web/Review*.fs`
（票 107/105 排在后面——**因此 `ReviewCheck.fs:152` 那句「会把十七趟一起搞挂」留着没动**，
见 §⑤ 的留给人 2）、`CONTEXT.md`、`docs/adr/*`、别人的票、别人的工作区。
**没改任何闸门在断言什么**：这一票只动了「几趟」「路径写在哪」「勾怎么打」。

---

## ① 第一件：趟数只许有一处真源

### 接手时的实况（五种说法并存）

`grep` 出来的写死趟数，同一个事实：

| 说法 | 出处 |
| --- | --- |
| 十七趟 | `ci-web.sh`×6、`verify-browser.mjs`×4、`verify-review.mjs`×3 |
| 十八趟 | `ci-web.sh`×2（**同一份文件里与上一行矛盾**） |
| 十五趟 | `verify-human.mjs`×3、`ci-web.sh`×1 |
| 十四趟 | `verify-browser.mjs`×1、`verify-home.mjs`×1、`docs/development.md`×1 |
| 十三趟 | `verify-seats.mjs`、`verify-bubbles.mjs`、`ci-web.sh` |
| 十一趟 | `verify-setup.mjs` |
| 十道 | `browser-lane.mjs`×2、`verify-board/golden/redaction/tracer/home` |
| 二十三道 / 第七道…第二十三道 | `ci-web.sh` 的逐道叙述（**「第二十二道」被用了两次**） |

**真的那个数是 18。** 改动前那次基线跑批的原文（`/tmp/106/ci-baseline.log:916`）就是证据：

```
十七趟浏览器闸门（同一个浏览器进程、同一台服务器）：
  ✓ 1.2s　node scripts/verify-tracer.mjs
  …（下面列了 18 行）
```

### 做法

**真源 = `verify-browser.mjs` 的 `gates` 数组。** 两处总述改成现算：

- 开跑时 `浏览器闸门共 ${gates.length} 趟（…）。`
- 收尾时 `${results.length} 趟浏览器闸门（…）：`

`ci-web.sh` 里那 145 行逐道叙述**整段删掉**。删之前逐条核过：那段文字里出现的
**每一个票号 / ADR / story / 裁决号**（`票 34/35/36/37/39/44/56/71/72/73/74/76/77/78/81/87/89/90/92/94/102`、
`19/21/26 票`、`票 51/75/86/93/103`、`ADR-0002/0003/0006`、`story 1/13/29`、`裁决 71-8`、`挂账 22-A`）
**都还在 `verify-*.mjs` 那一族里**（脚本核的，不是眼看的）。**唯一一处不在的是 `story 17`**，
已经补进 `verify-home.mjs` 第 ⑥ 条（「时间轴真的拖得动」就是它）。
另外 `gates` 表里 tracer 与 golden 两趟原本没有票号注释，把删掉那两行的内容补了进去。

### 自证：真加一趟假闸门

往 `gates` 末尾加一项（`run: async () => []`），**别处一个字都不改**：

```
$ bash scripts/check-single-source.sh
同一事实只许一处真源：通过（趟数只在 gates 表里；那份产物的路径 baseline/janpo-baseline.wasm 每一处逐字相同）
$ cd web && node scripts/verify-browser.mjs
浏览器闸门共 19 趟（同一个浏览器进程、同一台服务器）。
…
19 趟浏览器闸门（同一个浏览器进程、同一台服务器）：
  ✓ 1.1s　node scripts/verify-tracer.mjs
  …
  ✓ 4.7s　node scripts/verify-review.mjs
  ✓ 0.0s　node scripts/verify-fake.mjs
```

**零处需要手改**，两处总述都自己变成了 19。撤掉之后 `diff` 与撤之前逐字相同。

### 那道闸门自己红得起来（判据 1）

`scripts/check-single-source.sh` 的覆盖面**由真源自己算出来**——它从
`verify-browser.mjs` 的 `import` 行推出跑道上有哪几份文件（写死一份名单的话，
这道闸门自己就成了第二处真源）。四次反向自证：

```
### A：在 ci-web.sh 里写死一个趟数
  ✗ scripts/ci-web.sh:11:十九趟 —— 趟数写死了。真源只有 verify-browser.mjs 的 `gates` 表（票 106）
exit=1

### B：把 verify-browser.mjs 的趟数改回常量
  ✗ web/scripts/verify-browser.mjs:281:18 趟 —— 趟数写死了。真源只有 verify-browser.mjs 的 `gates` 表（票 106）
  ✗ verify-browser.mjs 的收尾总述没有从 `results.length` 现算趟数
exit=1
```

（C、D 是第二件的，见下。）**「一趟」「两趟」「几趟」不算写死**（它们不是计数）；
不在这条跑道上的 `verify-invariants.mjs` / `verify-baseline.mjs` 不受这一条管——
它们文件头里的「几趟」说的是它们自己那条路。

---

## ② 第二件：产物路径

### 收进来的（3 → 1）

`verify-baseline.mjs`、`verify-review.mjs`、`probe/akagi-wasm/candidates-shape.mjs`
原本各写一份 `baseline/janpo-baseline.wasm`（前两份还各自带一份一模一样的 `assetPresent()`），
注释里互相叮嘱「与 `web/src/baseline/wasm.ts` 的 `ASSET_FILE` 逐字相同」——
**那句叮嘱没有执行体**（判据 2）。现在三处都读 `web/scripts/baseline-asset.mjs`。

### 收不动的四处，逐处的理由（判据 4）

| 处 | 为什么收不动 |
| --- | --- |
| `web/src/baseline/wasm.ts` 的 `ASSET_FILE`（**定为真源**） | 它是**浏览器自己**去取那 6 MB 用的地址，打进 bundle，运行时读不到仓库里任何文件。别处都是围着它转的，所以它当真源 |
| `scripts/build-baseline-wasm.sh` 的 `out=` | bash，**本机要能单跑**（`./scripts/build-baseline-wasm.sh`），拿不到 workflow 的 env；为一个常量去起一个 node 子进程，是拿一个新失败点换一行字面量 |
| `scripts/check-pages-dist.sh` 的 `wasm=` | 同上；而且它核的是 `web/dist/` 下的那一份 |
| `.github/workflows/pages.yml` 的 `BASELINE_WASM` | YAML 的 `env:` 段是 **workflow 解析期**就要的值，那时既没有 checkout 也没有 node。真要收，得多一步 `>> $GITHUB_ENV`，等于把「解析期常量」换成「运行期副作用」 |

**收不成就钉在一起**（票面「每一处都指向同一个字面量，用一条 grep 断言写进闸门」）：
`check-single-source.sh` 每趟 CI 从 `wasm.ts` 现取 `ASSET_FILE`，再逐处按字面对齐；
另外还扫一遍全仓库，**第六处冒出来时当场喊停**。反向自证：

```
### C：把产物路径改坏一处（build-baseline-wasm.sh）
  ✗ scripts/build-baseline-wasm.sh 里找不到「out="web/public/baseline/janpo-baseline.wasm"」：
    那份产物的路径漂了（造它的那一处；本机要能单跑，拿不到 workflow 的 env）
exit=1

### D：第六处冒出来（往 web/scripts/serve.mjs 里加一行）
  ✗ web/scripts/serve.mjs 里也写着「janpo-baseline.wasm」：名单之外又冒出一处产物路径
    ——把它钉进这份名单，或改成读 web/scripts/baseline-asset.mjs
exit=1
```

### 收尾时顺手抓到的同族第三件

review 的 Standards 轴（Duplicated Code）在同一族里翻出**页脚那条声明的落点**：
`src/Janpo.Web/Credit.fs` 的 `thirdPartyFile` 是真源，而 `verify-baseline.mjs` 的
`THIRD_PARTY` 与 `check-pages-dist.sh` 那串「打包产物里要找得到」的名单各抄一份，
两处注释都写着「与 `Credit.fs` 逐字相同」——**同样没有执行体**。
一并钉进 `check-single-source.sh` 的第 ③ 段（反向自证：把 `THIRD_PARTY` 的大小写改一个字母，
`✗ web/scripts/verify-baseline.mjs 的 THIRD_PARTY 与 Credit.fs 的「third-party/README.md」对不上`）。
第三处 `tests/Janpo.Web.Tests/BaselineCreditTests.fs:49` 本来就是钉那个常量的用例，不必再钉一遍。

**一处只提不用的**：`tests/Janpo.Web.Tests/BaselineCreditTests.fs:81` 那句
「取 baseline/janpo-baseline.wasm 时回了 HTTP 404」是用例**捏出来的一句失败理由**（测试数据），
不是取用路径，因此列在名单里放行、不按字面钉。`web/public/baseline/README.md` 与
`web/public/third-party/README.md` 同理（散文里提它一嘴）。

---

## ③ 第三件：许可闸门从「不常跑的路」搬到 `ci.sh`

**先红。** 删掉 `web/public/third-party/NOTICE-akagi`，跑 `./scripts/ci.sh`：

```
== web ==
== 同一事实只许一处真源（趟数 / 产物路径）==
同一事实只许一处真源：通过（趟数只在 gates 表里；那份产物的路径 baseline/janpo-baseline.wasm 每一处逐字相同）
== 随站点上线的那几份许可（Apache-2.0 §4）==
许可闸门没过：web/public/third-party/NOTICE-akagi 不在或是空的（Apache-2.0 §4，ADR-0006 边界 4）
ci.sh exit=1
```

第二种失法（**页面上那条链接的落点没了**，票 102）：

```
$ rm web/public/third-party/README.md && bash scripts/check-third-party.sh web/public
许可闸门没过：web/public/third-party/README.md 不在或是空的（Apache-2.0 §4，ADR-0006 边界 4）
许可闸门没过：页脚那条「第三方组件声明」指着 third-party/README.md，而 web/public/third-party/README.md 不在或是空的（票 102）
exit=1
```

那条链接的路径**不是抄的**：从 `src/Janpo.Web/Credit.fs` 的 `thirdPartyFile` 现取
（页脚与配桌页那一句读的都是它），因此页面改了指向、闸门跟着改核哪一份。

**复原后两侧都绿，而且跑的是同一份脚本**（名单只写在它那里一处）：

```
$ bash scripts/check-third-party.sh web/public
许可件齐（web/public/third-party/：LICENSE-akagi.txt NOTICE-akagi README.md；页面那条链接指的 third-party/README.md 也在）
$ bash scripts/check-pages-dist.sh
许可件齐（web/dist/third-party/：LICENSE-akagi.txt NOTICE-akagi README.md；页面那条链接指的 third-party/README.md 也在）
强 AI 基线产物随站点上线：6039832 字节，sha256 53422161e8386e3b35095e2a83393f90c414e78ea2b7d6a81b6565ed3091254f
发布件闸门通过：web/dist
```

---

## ④ 第四件：那些写死的 `✓`

### 全扫的结果（21 份文件、78 处 `✓`）

| 形状 | 处数 | 判法 |
| --- | ---: | --- |
| **判决之后才印**（`if (…length > 0) return failure(…)` 之后） | 62 | 走到那里就说明清单是空的，勾是实话 |
| **在 `if` / `else` 分支里**（那一项自己成功才印） | 5 | `verify-inbound` 304/390/473、`verify-review` 485/666 |
| **本来就由数据算**（`same ? "✓" : "✗"`） | 2 | `verify-tracer` 242、`verify-browser` 283（顺手改用 `tick`/`mark`） |
| **收集完失败之后无条件打印** ← **就是它** | **9** | 见下 |

**改掉的 9 处，在 3 份文件里**：

| 文件:行（改前） | 那一句 | 现在勾由谁决定 |
| --- | --- | --- |
| `verify-review.mjs:521` | 真人那一桌：N 条逐项对拍 | `tick(mismatches.length === 0)` |
| `verify-review.mjs:617` | 首页座位 1：N 条逐项对拍 | `tick(mismatches.length === 0)` |
| `verify-review.mjs:1257` | 强 AI 逐手对照…同一条 id | `mark(problems)`（`strongLeg` 那一程） |
| `verify-review.mjs:1263` | 上帝视角会打 A、该席视角只能打 B | 同上 |
| `verify-review.mjs:1267` | 候选分布…逐条相同 | 同上 |
| `verify-review.mjs:1277` | **页面上那几个概率与 wasm 直接印的严格相等** | 同上（票面点名的那一句） |
| `verify-setup.mjs:161` | 拨完三项没按重开…都还是老规则 | `markerSince(problems)`（进这一项之前记一笔） |
| `verify-setup.mjs:214` | localStorage 里…重新打开还在 | `markerSince(problems)` |
| `verify-seats.mjs:215` | 四席在一屏里 | `mark(problems)`（这个函数只管这一件事） |

**`strongLeg` 那四句为什么共用一程的清单而不是各算各的**：那一程四个题目的断言
推在**同一个逐手循环**里，拆不开「哪一条是哪个题目推的」。取了保守的一档——
**宁可四句一起打叉，也不许出现一条与失败矛盾的勾**。写在代码注释里。

### 约定落在 `browser-lane.mjs`（票面：不许只改一处）

这个形状是从 `browser-lane.mjs` 的约定长出来的（闸门不自己 `process.exit`，
而是**收集**失败最后交清单，于是飞行中印的叙述行永远早于判决）。因此三个助手
与「什么时候用哪个」写在那里一处：`tick(ok)` / `mark(failures)` / `markerSince(failures)`。
其余 69 处**没动**：它们的勾本来就在判决之后或在分支里，逼它们也走 `tick(true)`
反而会造出一堆恒真式（这个仓库刚栽过「属性测试是恒真式」那一课）。

### 先红后绿：票面那一幕逐字复现

破坏法**没碰票面禁止的 `Review*.fs`**：从 wasm 那一侧下手，
临时让 `probe/akagi-wasm/probe.js` 把 `"p"` 舍到三位小数（票面里页面 0.765 / wasm 0.7653265 的镜像）。

**改前**（把那四句的勾按老样子写死；`node scripts/verify-review.mjs` exit=1）：

```
强 AI 逐手对照：122 行与引擎另一条路问出来的同一条 id ✓（分歧 64 手、遮着他家摸牌的 120 行、问两遍同答案 12 手、它交不出来 0 手）
上帝视角会打 A、该席视角只能打 B：同一局后来那一份流 11 手、一条不掩一张不隐那一份 11 手——逐手断言页面给的是 B ✓
候选分布：122 行与闸门另一条路问出来的逐条相同 ✓（上游给了几条：2 条×25、3 条×97；…）
逐位对拍：抽了 8 手拿 probe/akagi-wasm 的 node 路径重问一次，页面上那几个概率与 wasm 直接印的严格相等 ✓（其中 0 个连字面都一样）

复盘那一道没过：
第 1 手第 1 条：页面上是 0.7653265，wasm 直接印的是 0.765——两边不是同一个数
第 1 手第 2 条：页面上是 0.18272197，wasm 直接印的是 0.183——两边不是同一个数
```

**改后**（同一次破坏，exit=1）：

```
强 AI 逐手对照：122 行与引擎另一条路问出来的同一条 id ✗（分歧 64 手、遮着他家摸牌的 120 行、问两遍同答案 12 手、它交不出来 0 手）
上帝视角会打 A、该席视角只能打 B：同一局后来那一份流 11 手、一条不掩一张不隐那一份 11 手——逐手断言页面给的是 B ✗
候选分布：122 行与闸门另一条路问出来的逐条相同 ✗（上游给了几条：2 条×25、3 条×97；…）
逐位对拍：抽了 8 手拿 probe/akagi-wasm 的 node 路径重问一次，页面上那几个概率与 wasm 直接印的严格相等 ✗（其中 0 个连字面都一样）
```

**三件事都核过**：

1. 与失败矛盾的勾**一个不剩**。
2. **计数没被改坏**（票面第三条）：`122 行` / `64 手` / `其中 0 个连字面都一样` 改前改后逐字相同。
3. **别的那几程照旧打勾**——那几程真的过了（这正是「由该项自己的成败决定」的意思）：
   `对局中：复盘那一块整个不在 DOM 里 ✓`、`真人那一桌：96 条逐项对拍 ✓`、
   `首页座位 1：122 条逐项对拍 ✓`、`点开跳走了、关掉跳回来 ✓`。

`markerSince()` 那条路另外验过一次（合成注入一条失败，`verify-setup` / `verify-seats` 各 exit=1）：

```
拨完三项没按重开：页面「tonpuusen/on/on」、牌谱「tonpuusen/on/on」，都还是老规则 ✗
localStorage 里：janpo.rules.{length=hanchan, akadora=off, kuitan=off}　重新打开还在 ✓   ← 这一项真的过了
四席在一屏里 ✗（视口 720 px，四行跨 263→557；…）
```

### 没做的：一道防它回来的静态闸门

想过两种，两种都有假阳性，都没装，理由写在这里（判据 2：说不出执行体就别把它写成不变量）：

- **「`verify-*.mjs` 里不许出现裸 `✓`」**——要把 78 处全改成走 `tick()`，
  而判决之后那 65 处只能写 `tick(problems.length === 0)`，是恒真式。
- **「一趟红了，它印出来的行里不许有 `✓`」**——与票面自己偏好的「每一项各打各的勾」冲突：
  `verify-setup` 第 ⑥ 项真过了就该打勾，哪怕第 ③ 项红了。

于是这一条今天靠的是 `browser-lane.mjs` 上那段约定 + 这份报告里那张扫描表。
**它是一条「只活在文档里的不变量」，我把它挂在这里当留给人的第 1 项。**

---

## ⑤ 收尾

### `./scripts/ci.sh` 全绿

`/tmp/106/ci-final.log`：`== CI 全绿 ==`。**十八趟一趟不少**——把改动前后两次跑批
印出来的闸门列表 `diff` 过一遍，**逐条相同**：

```
$ diff <(grep -oP "node scripts/verify-\S+" ci-baseline.log | sort) \
       <(grep -oP "node scripts/verify-\S+" ci-after-edits.log | sort)
（无输出）
```

### review 结论（两轴，fixed point `ylsyqvwx` / `bc87be46`）

派不出 sub-agent，两轴自己顺序跑（workbook 允许）。**blocking 的三条当场修了并重跑了全量。**

**Standards 轴**（`AGENTS.md`、`docs/agents/*`、既有 `scripts/*.sh` 与 `verify-*.mjs` 的惯例，
外加 Fowler 那份味道基线；本票零 F# 改动，`fsharp-style.md` 不适用）

- **硬违规 0 条**：全程 jj、无远端操作；`ci.yml` 的六道结构、`src/Janpo.Engine/**`、
  `src/Janpo.Web/Review*.fs`、`CONTEXT.md`、`docs/adr/*`、别人的票与工作区一个都没碰；
  没有 key 进仓库；那 6 MB 一个字节都没进版本控制。
- **工具已经在管的不重复看**：`dotnet fantomas .`（写盘那一遍，188 份 Unchanged）、
  `biome ci --error-on-warnings`、`check-style.sh` 都绿。
- **Duplicated Code（blocking ×2，都改了）**
  ① 产物路径在 node 那三个脚本里各一份 → 收进 `web/scripts/baseline-asset.mjs`；
  ② **收尾时才翻出来的同族第三件**：`third-party/README.md` 写在 `Credit.fs`（真源）、
  `verify-baseline.mjs` 与 `check-pages-dist.sh` 三处，后两处注释都写着「与 `Credit.fs` 逐字相同」
  **而那句话没有执行体** —— 钉进 `check-single-source.sh` 第 ③ 段（反向自证过）。
- **Mysterious Name（blocking，改了）**：助手原名 `marker(failures)`，与返回字符串的
  `mark(failures)` 只差一个字母，**返回的却是一个函数**。手滑写成 `${marker(x)}`
  会把一个函数体静静印进日志、没有任何东西报错——**与这一票治的病是同一族**。
  改名 `markerSince`，并把这个理由写进它的 doc。
- **Shotgun Surgery（记录：这正是这一票要消灭的东西）**：这次改动为**一个事实**动了 14 份
  `.mjs` + 1 份 shell。改完之后同一个动作只碰 1 份——假闸门那一趟量过了（§①）。
- **Divergent Change（判断题，没改）**：`check-single-source.sh` 一份脚本盯三件事。
  它们是同一**类**事实（「一处真源、多处抄件」），拆成三份脚本会让 `ci-web.sh` 多两个调用点
  ——那正是这一票在减少的形状。记录，不改。
- **Speculative Generality（查过，不成立）**：`tick` / `mark` / `markerSince` 三个都有真调用点
  （`tick` 3 处、`mark` 3 处、`markerSince` 2 处），没有为将来预留的参数或钩子。
- **Primitive Obsession（nitpick，不改）**：产物路径是一个裸字符串，跨 TS / bash / YAML；
  类型跨不过 bash 与 YAML，闸门是这里唯一能上的约束。
- **测试只许改硬**：这一票**加**了两道闸门（许可 / 同一真源），**一条断言都没删没放宽**；
  三份被改过的 `verify-*.mjs` 只动了「那一行的勾从哪儿取」。

**Spec 轴**（票 106 票面四件 + 闸门四条 + 边界）

| 票面要的 | 落在哪 | 判 |
| --- | --- | --- |
| ① 趟数只许一处真源；加一趟时零处手改 | §① | 做到，真加过一趟假闸门 |
| ② 产物路径能收几处收几处 + 收不动的写明为什么 | §② | 7→5，四处理由逐条写了 |
| ② 的闸门：收敛之后每一处指向同一字面量，写进闸门 | `check-single-source.sh` ② | 做到，四种改坏法各红一次 |
| ③ 往 `ci-web.sh` 加许可闸门 | `check-third-party.sh` | 做到，源与分发件共用一份 |
| ③ 的闸门：真删一次让 `./scripts/ci.sh` 红 | §③ | 做到，红的原文抄进来了 |
| ④ 勾由该项自己的成败决定 | §④ | 9 处全改 |
| ④ 不许只改 `verify-review.mjs` 一处 | §④ 扫描表 | 扫了 21 份 78 处，改动落在 3 份；其余 69 处为什么不动写了 |
| ④ 别把那一行里的计数改坏 | §④ | 改前改后计数逐字相同 |
| 十八趟一趟不少 | §⑤ 的 `diff` | 逐条相同 |
| `./scripts/ci.sh` 全绿 | §⑤ | 绿 |

**票面之外做了的四件（各有理由，不算跑偏）**：

1. `docs/development.md` 那句「浏览器里那十四趟」——同一个事实的又一处抄件，去掉了数字。
2. `third-party/README.md` 那三处（Standards 轴翻出来的，见上）。
3. `markerSince` 改名（Standards 轴的 blocking）。
4. `pages.yml` 只动了 `BASELINE_WASM` 上面那段注释——**票面明许**
   （「`pages.yml` 只许动与产物路径有关的那几行」），说清了为什么收不动、现在谁在钉它。

**没触发 park**：第一件没有要动 `browser-lane.mjs` 的结构——趟数的真源本来就在
`verify-browser.mjs` 的 `gates` 数组里，只是从前没人从它现算。

### 留给人的待审项

1. **「叙述行里的勾必须由数据决定」今天没有执行体**（只有 `browser-lane.mjs` 上的约定）。
   两种候选闸门与各自的假阳性写在 ④ 末节；要装的话得先决定「一项过、另一项红」时那一行该长什么样。
2. **`src/Janpo.Web/ReviewCheck.fs:152` 还写着「会把十七趟一起搞挂」**——
   票面明令不碰 `Review*.fs`（票 107 / 105 排在后面），因此留着。
   它不在 `check-single-source.sh` 的覆盖面里（那道闸门只扫跑道上那几份 `.mjs` 与 `ci-web.sh`），
   **顺手的改法是删掉那三个字**：「会把同一条跑道上其余那几趟一起搞挂」。
3. **`ci-web.sh` 少了 145 行逐道叙述**。删之前用脚本核过「每一个票号都还在 `verify-*.mjs` 那一族里」，
   但那段文字里有些**行文**（例如第八道那一整段对首页六条断言的复述）比 `verify-home.mjs`
   的文件头更顺口。若主人觉得那份「一页读完 CI 在验什么」的总览有价值，
   正确的落点是 `docs/development.md` 而不是一份 shell 脚本的注释——**但那样又会是第二份抄件**。
4. **`verify-invariants.mjs` 的「十三条不变量 + 3 道对拍」是同一族的下一处**：
   `582` 行印的是 `${Object.keys(RULES).length} 条不变量 + 3 道对拍`——
   左边现算、右边写死。不在这一票的边界里（它不在浏览器那条跑道上）。
