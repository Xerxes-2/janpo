# 72 — 配桌上的三个规则开关，以及重定超时默认值

**结论：`?table=1` 上多了一排「对局长度 / 赤宝牌 / 食断」，拨完按「重开」才生效；
超时默认值 30 秒 → 240 秒（4 分钟）。** 三项落在 `janpo.rules.*` 三个键上，
牌桌真正在按的那份规则集只有 `Restarted` 动得了——**半场换规则在结构上就发生不了**。
`./scripts/ci.sh` **EXIT=0**，37.7s（基线 43.1s，同一台机器同一天）；dotnet 744 + **132** 条
（新增 11 条），浏览器闸门从十趟变**十一趟**（新增 `verify-setup`，0.9s）。

**引擎一行没改。** 开关早就在 `Ruleset` 上（`Akadora` / `Kuitan` / `Length`），这一票只是
把它们接到控件上：`jj diff --name-only` 里没有 `src/Janpo.Engine/**`、没有 `web/src/agent/**`、
没有 `docs/adr/*`、没有 `CONTEXT.md`、没有 `demo-paifu.json`、没有回放那一半。

---

## 1. 接缝：拨到的那一份与真在打的那一份是两个值

### 1.1 新类型（`src/Janpo.Web/RulesetDraft.fs`，99 行）

```fsharp
type RulesetDraft = { Length: GameLength; Akadora: bool; Kuitan: bool }   // 页面上拨到的三项

[<RequireQualifiedAccess>]
type RuleChoice =                       // 一个 case 一根轴（`TableMsg.RulePicked` 带一个它）
    | Length of length: GameLength | Akadora of akadora: bool | Kuitan of kuitan: bool

RulesetDraft.initial   : RulesetDraft            // = ofRuleset Ruleset.yonma（不另写字面量）
RulesetDraft.ofRuleset : Ruleset -> RulesetDraft // 逆向：拿来与「真在打的那一份」比
RulesetDraft.ruleset   : RulesetDraft -> Ruleset // 底子恒是 Ruleset.yonma，只动这三项
RulesetDraft.pick      : RuleChoice -> RulesetDraft -> RulesetDraft
RulesetDraft.switchToWire / switchOfWire / switchToDisplay / toWire
```

**为什么是一个三字段的记录，而不是直接把一份 `Ruleset` 当草稿存着**：

1. **它就是 spec story 13 的那三项**，一眼看得出 UI 上有几根轴；存整份 `Ruleset` 的话，
   「这一票到底能拨几样」要靠读页面代码才答得出。
2. **引擎只给了「关掉」的那一半**（`Ruleset.withoutAkadora` / `withoutKuitan`），
   要把赤宝牌**再打开**得知道那三张是哪三张——那是预设的知识，不该在 Web 层长第二份。
   记录里存 `bool`，`ruleset` 那一步统一按 `Ruleset.yonma` 的值还原（引擎因此一行不改）。
3. **不做预设选择器**（票面边界）：底子恒是 `yonma`，`Ruleset.majsoul` 进不了 UI。
   `八种拨法：三项之外一个字段都不动` 那条用例把这件事钉死了——它把三项按回预设之后
   与 `Ruleset.yonma` 逐字段比。

**名字**：`RulesetDraft`（「还没开桌的那一份规则集」）。`CONTEXT.md` 的 `Roster` 条目把
`Setup` 列进 _Avoid_，所以没叫 `TableSetup`；三个字段用的是术语表里的
`GameLength` / `Akadora` / `Kuitan`。这是 Web 层的新类型名，**提案已追加到 `DECISIONS.md`**（72-1）。

### 1.2 谁改得动「这一桌真在按的那一份」

```fsharp
type LiveTable = { SeedText: string; Rules: RulesetDraft; Table: Result<Table, string>; … }
//                             ↑ 拨到的（与种子同一条路：打字/拨钮都不重开一桌）

TableState.rulesPending : TableModel -> bool   // = RulesetDraft.ruleset live.Rules <> model.Ruleset
```

- `RulePicked` **只**改 `live.Rules`，并把三项写进 localStorage；
- `Restarted` 是**唯一**写 `model.Ruleset` 的地方：`let ruleset = RulesetDraft.ruleset live.Rules`，
  牌桌用它开（`openTable (rosterFor ruleset live)`）；
- 牌谱里的规则集取自**牌桌自己**（`Table.paifu` → `Game.ruleset table.Game`），
  于是「同一份牌谱前后两套规则」在结构上就发生不了——`model.Ruleset` 就算被改坏，
  已经开着的那一桌导出的牌谱也不会跟着变（红-W2 的输出正是这个样子：页面变了、牌谱没变）。
  要让牌谱半场变规则，得**在拨的时候重开牌桌**——红-W2b 就是这么造出来的，闸门当场逮住。

`rosterOf` / `step` / `Exported` 读的仍是 `model.Ruleset`，一处没动。

### 1.3 页面（`TablePanel.setup`，只属于 Live）

摆在播放条与「视角 + 种子 + 重开」那一排之间，因此**「重开」就在它下面一行**：

```
对局长度 [东风战][半庄战]  赤宝牌 [有][无]  食断 [有][无]   这一桌就是按这三项开的。
```

末尾那一格（`testId=table-rules`）印两样给人和闸门看：
`data-rules`＝**这一桌真在按的**三项（`tonpuusen/on/on` 这种一行摘要），
`data-rules-pending`＝拨到的是不是已经与它不同（就是 `TableState.rulesPending`）。
拨过之后那句话变成「拨好了：按「重开」才开出新的一桌（不半场换规则）。」

**截图我打开看了**（判据 7）：`docs/images/table.png` 重出了一张（同一条命令、同一颗种子
1177、同 52 手），新那一排在播放条下面，选中的是 东风战 / 有 / 有；超时那一格显示 240000。

### 1.4 localStorage

**另起一个前缀**：`janpo.rules.length` / `janpo.rules.akadora` / `janpo.rules.kuitan`，
值分别是 `tonpuusen|hanchan` 与 `on|off`。理由：模型座位那几格（`janpo.llm.*`）是**座位配置**，
这三项是**这一桌按什么规则打**（ADR-0004 说它是规则事实，不是用户偏好）。
`Store` 里 `read` / `write` 多收一个前缀参数，原有的三个入口逐字未改语义。
读不懂的值按项退回默认，**不把另外两项一并丢掉**。

---

## 2. 超时默认值：30 秒 → 240 秒（4 分钟）

**旧值的来历**：票 23 定 30 秒时的实测依据是票 18 的「单轮 tool call 约 2.4 秒」——
那是**没有思考预算**的年代。

**新值的依据**：DeepSeek medium 思考实测**单手 17–180 秒**（DECISIONS 2026-08-16
「另记一条不用裁但 M2 必须知道的」）。240 秒 = 实测上界 180 秒 + 33% 余量。

**为什么不更大**：超时之后还要重试两次（`Agent.retryLimit = 2`），最坏情形下**一手要等
3 × 超时**才看得出「模型不说话了」——240 秒时是 12 分钟，再往上就没法在一场对局里用了。
（这个数字不是猜的：`loop.ts` 把超时归进「值得重问」那一类，与 provider 报错同路。）

**它仍是面板上拨得动的一格**：本地大模型第一次加载、或者 high 思考预算，主持人自己调大。

**跟着改的三处过期数字**：
1. `LlmSeat.initial` 那条注释（原文写着「30 秒：票 18 实测单轮 tool call 约 2.4 秒」）；
2. **面板上那段提示语**——新加的一句**把数字插值进去**（`LlmSeat.initial.TimeoutMs`），
   下一个改默认值的人不会再留下一句过期的话；
3. `docs/host/custom-endpoint.md` 的「超时默认 30 秒」。

**手验脚本里那两个数**：`verify-export.mjs --llm` 那一档从 60000 提到 240000（它支持
`--thinking medium`，60 秒会把手验跑成一串兜底）。`verify-llm-seat.mjs` 的 30000 **故意留着**
——那一档自己就把 `thinking` 设成 `off`，30 秒对它够用，改它反而是动了一个无关的量。
`record-agent-fixtures.mjs` 同理（录固件，thinking off）。

**一条断言守着这个数**：`超时默认值够开着思考预算的模型用：实测单手上界 180 秒`
（`LlmSeat.initial.TimeoutMs >= 180_000`）。它不是「等于 240000」——那样只是把常量抄了两遍；
它钉的是**那条实测上界**，往下调就红。

---

## 3. 三个开关各自怎么核的

每一项都在**两个层面**各核一次：dotnet 侧核状态机与牌谱（纯的、快的），
浏览器侧核「页面上点出来的那一桌」（`web/scripts/verify-setup.mjs`，第十一趟）。

| 开关 | dotnet 侧（`tests/Janpo.Web.Tests/TableSetupTests.fs`） | 浏览器侧（`verify-setup.mjs`） |
|---|---|---|
| **赤宝牌** | 关掉后打完一整场：`paifu.Ruleset.Akadora = []`，且**事件流里赤牌 0 张**；另有阳性对照（开着时同一场数出 **30 张**） | 关掉后打完一整场半庄：`ruleset.akadora` 为空、事件流里 `5mr/5pr/5sr` 0 次；阳性对照在默认那一场（30 次） |
| **食断** | 关掉后 `paifu.Ruleset.Kuitan = false`，开着时 `true` | 半庄那一场 `ruleset.kuitan === false` |
| **对局长度** | ① `Ruleset.kyokus` 长度 4 / 8；② **真打完两场**：东风战每一局的场风都是东，半庄打得到南场，且局数 ≥ 4 / ≥ 8（连庄会把同一项再打一遍，所以是下界） | 两场都打到终局：东风战 `start_kyoku` 全是 `1z`（且有 `1z-4`），半庄有 `2z-4` |
| **不半场换规则** | 拨完三项**不重开**：`model.Ruleset` / 牌桌的规则集 / 牌谱里的规则集三样都还是 `Ruleset.yonma`，`rulesPending = true` | 拨完不重开就导出一份牌谱：`ruleset` 与开局那一份逐项相同，页面上 `data-rules` 不变、`data-rules-pending` 变 `true` |
| **按重开才生效** | `Restarted` 之后三样都变成拨到的那一份，`rulesPending = false`，`Turns = 0` | `data-rules` 变 `hanchan/off/off`、pending 变 `false`，之后那一场的牌谱逐项跟着变 |
| **落 localStorage** | 测不了（dotnet 上没有 localStorage，`Store` 是 `Browser.WebStorage`） | **把这一页重新打开一次**：三项还在（并把三个键的值印出来） |
| **回放那一侧没有配桌** | 首页那一屏 `rulesPending = false`，`RulePicked` 打过去**原样返回** | 首页那道闸门的「不该有的控件」名单加了 4 个 testId（`table-rules` / `table-length-tonpuusen` / `table-akadora-on` / `table-kuitan-on`） |

**赤牌扫描只扫事件流，不扫整份牌谱**——`ruleset.akadora` 那一段本来就列着三张赤牌，
连它一起扫的话「一张都没有」那条断言永远数得出东西来（判据 3 的同一族坑）。
两侧的实现互相独立：dotnet 侧扫 `Event.encoder` 出来的 JSON，浏览器侧扫下下来那份字节。
**两侧数出来的都是 30 张**（同一颗默认种子 2088 的那一整场东风战）。

**闸门这一趟的原始输出**（CI 里第十一趟）：

```
默认那一桌：tonpuusen/on/on　5 局（1z-1 1z-1 1z-2 1z-3 1z-4）　事件流里赤牌 30 张
拨完三项没按重开：页面「tonpuusen/on/on」、牌谱「tonpuusen/on/on」，都还是老规则 ✓
重开之后：hanchan/off/off　9 局（1z-1 1z-1 1z-2 1z-3 1z-4 2z-1 2z-2 2z-3 2z-4）　事件流里赤牌 0 张
localStorage 里：janpo.rules.{length=hanchan, akadora=off, kuitan=off}　重新打开还在 ✓
对局长度 / 赤宝牌 / 食断：拨得动、按重开才生效、牌谱里跟着变 ✓
```

（`1z-1` 出现两次是连庄，不是重复——局数序列是下界不是等号，这也是上面那张表里
「≥ 4 / ≥ 8」的由来。）

---

## 4. 每条新断言先红一次（判据 1 的原始输出）

**全部实跑过，跑完逐个 `diff` 对回备份**（`/tmp/72bak/`，七个文件全部 OK）。
dotnet 侧跑 `dotnet test tests/Janpo.Web.Tests`，浏览器侧每次重跑
`pnpm run fable && vite build && node scripts/verify-setup.mjs`。

### 4.1 dotnet 侧（11 条新用例）

**红-1｜超时默认值退回 30 秒**（`TimeoutMs = 240000` → `30000`）：

```
Janpo.Web.Tests.TableSetupTests.超时默认值够开着思考预算的模型用：实测单手上界 180 秒 [FAIL]
  错误消息:
   超时默认 30000 ms，接不住实测单手 180 秒的思考
失败! - 失败: 1，通过: 131，总计: 132
```

**红-2｜底子换成雀魂预设**（`RulesetDraft.ruleset` 的 `Ruleset.yonma` → `Ruleset.majsoul`）
——这是「不做预设选择器」那条边界的反面：

```
TableSetupTests.八种拨法：三项之外一个字段都不动（底子恒是天凤那份预设） [FAIL]
TableSetupTests.拨开关不动正在打的那一桌：半场不换规则 [FAIL]
TableSetupTests.按「重开」那一刻才换规则，换完就不再等着生效 [FAIL]
TableSetupTests.默认那一份就是 Ruleset.yonma：页面不悄悄换一套规则 [FAIL]
PaifuExportTests.导出的牌谱编码再解码，逐字段与原来相同 [FAIL]      ← 既有用例也跟着红
失败! - 失败: 5，通过: 127，总计: 132
```

**红-3｜回放那一侧也说「按重开才生效」**（`rulesPending` 的 `| None -> false` → `true`）：

```
TableSetupTests.回放那一侧没有配桌：三项拨不动，也永远不在等着生效 [FAIL]
  错误消息:  Assert.False() Failure
失败! - 失败: 1，通过: 131，总计: 132
```

**红-4｜重开时不换规则**（`Restarted` 里 `RulesetDraft.ruleset live.Rules` → `model.Ruleset`）：

```
TableSetupTests.按「重开」那一刻才换规则，换完就不再等着生效 [FAIL]
  错误消息:  Assert.Equal() Failure: Values differ
失败! - 失败: 1，通过: 131，总计: 132
```

**红-5｜拨完当场生效**（`RulePicked` 顺手把 `model.Ruleset` 也改了）：

```
TableSetupTests.拨开关不动正在打的那一桌：半场不换规则 [FAIL]
  错误消息:  Assert.Equal() Failure: Values differ
失败! - 失败: 1，通过: 131，总计: 132
```

**红-6｜赤宝牌开关不接线**（`unless draft.Akadora` → `unless true`）：

```
TableSetupTests.八种拨法：三项之外一个字段都不动（底子恒是天凤那份预设） [FAIL]
TableSetupTests.按「重开」那一刻才换规则，换完就不再等着生效 [FAIL]
TableSetupTests.关掉赤宝牌：牌谱的 ruleset.akadora 为空，事件流里一张赤牌都没有 [FAIL]
  错误消息:  Assert.Equal() Failure: Collections differ
失败! - 失败: 3，通过: 129，总计: 132
```

**红-7｜食断开关不接线**：

```
TableSetupTests.关掉食断：牌谱的 ruleset.kuitan 跟着变 [FAIL]
  错误消息:  Assert.False() Failure
（另有 八种拨法 / 按「重开」两条跟着红）　失败! - 失败: 3，通过: 129，总计: 132
```

**红-8｜长度开关不接线**（`withLength draft.Length` → `withLength Tonpuusen`）：

```
TableSetupTests.长度是规则集的一根轴：局数序列四麻东风战 4 局、半庄 8 局 [FAIL]
TableSetupTests.东风战打不到南场，半庄打得到：局数序列真的按长度走 [FAIL]
  错误消息:  半庄打完了却一局南场都没有
（另有 八种拨法 / 按「重开」两条跟着红）　失败! - 失败: 4，通过: 128，总计: 132
```

**红-9｜阳性对照真的数得出东西来**（把门槛临时抬到 10000，看它数到几张）：

```
TableSetupTests.阳性对照：赤宝牌开着时，同一场里那三张赤牌真的出现在事件流里 [FAIL]
  错误消息:
   赤宝牌开着的一整场里只数出 30 张赤牌
失败! - 失败: 1，通过: 131，总计: 132
```

（**判据 3**：「关掉赤宝牌之后 0 张」这条断言只有在「开着时数得出来」时才有意义。
同一颗种子的同一场里它数到 30 张，与浏览器侧那一趟数出来的 30 次互为旁证。）

### 4.2 浏览器侧（`verify-setup.mjs` 那一趟）

**红-W1｜重开时不换规则**：

```
配桌那三项开关没过：
按了重开，页面上却还印着「tonpuusen/on/on」
按了重开，页面却还说「按重开才生效」
重开之后导出的牌谱写着「tonpuusen/on/on」，而页面上拨的是 hanchan/off/off
关掉了赤宝牌，牌谱的 ruleset.akadora 里却还有 5mr,5pr,5sr
关掉了赤宝牌，事件流里却出现了 30 张赤牌
关掉了食断，牌谱的 ruleset.kuitan 却是 true
半庄打完了却一局南场都没有（打的是 1z-1 / 1z-1 / 1z-2 / 1z-3 / 1z-4）
半庄没打到南 4 局（打的是 1z-1 / 1z-1 / 1z-2 / 1z-3 / 1z-4）
```

**红-W2｜拨完当场生效**（`RulePicked` 顺手改 `model.Ruleset`）：

```
三项都拨过了，页面却没说「按重开才生效」
还没按重开，页面上这一桌的规则就从「tonpuusen/on/on」变成了「hanchan/off/off」
```

**红-W2b｜拨完当场把牌桌重开了**（这才是能让**牌谱**半场变规则的那种写法）：

```
拨完开关（没重开）导出的牌谱写着「hanchan/off/off」，而这一场是按「tonpuusen/on/on」打的
```

**红-W3｜三项不落 localStorage**（`writeRules` 永远写默认值）：

```
重新打开这一页，拨到的三项没了：印着「tonpuusen/on/on」
```

**红-W4｜页面上印的是拨到的那一份**（`data-rules` 改印 `live.Rules`）：

```
还没按重开，页面上这一桌的规则就从「tonpuusen/on/on」变成了「hanchan/off/off」
```

**红-W5｜赤宝牌开关不接线**：

```
按了重开，页面上却还印着「hanchan/on/off」
重开之后导出的牌谱写着「hanchan/on/off」，而页面上拨的是 hanchan/off/off
关掉了赤宝牌，牌谱的 ruleset.akadora 里却还有 5mr,5pr,5sr
关掉了赤宝牌，事件流里却出现了 53 张赤牌
重新打开这一页，拨到的三项没了：印着「hanchan/on/off」
```

**红-W6｜长度开关不接线**：

```
按了重开，页面上却还印着「tonpuusen/off/off」
重开之后导出的牌谱写着「tonpuusen/off/off」，而页面上拨的是 hanchan/off/off
半庄打完了却一局南场都没有（打的是 1z-1 / 1z-1 / 1z-2 / 1z-3 / 1z-4）
半庄没打到南 4 局（打的是 1z-1 / 1z-1 / 1z-2 / 1z-3 / 1z-4）
```

**红-W7｜食断开关不接线**：

```
按了重开，页面上却还印着「hanchan/off/on」
重开之后导出的牌谱写着「hanchan/off/on」，而页面上拨的是 hanchan/off/off
关掉了食断，牌谱的 ruleset.kuitan 却是 true
```

**红-W8｜把赤牌扫描换成一张永远不会出现的牌**（`'"5mr"'…` → `'"5zr"'`，阳性对照自证）：

```
赤宝牌开着，打完一整场却一张赤牌都没进事件流——那条「一张都没有」等于永远为真
```

---

## 5. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**，37.7s（同机基线 43.1s）；引擎 744 + 页面 **132** 条 |
| 浏览器十一趟 | `cd web && node scripts/verify-browser.mjs` | 全 ✓（新那一趟 0.9s；十趟合计没有变慢） |
| 新那一趟单跑 | `cd web && pnpm run verify:setup` | 1.2s，四行输出见 §3 |
| 每条新断言先红 | §4 的 9 + 8 次 | 全部红过，原文抄在 §4 |
| 截图 | `cd web && node scripts/shoot-table.mjs` | `docs/images/table.png` 重出，**打开看过**（§1.3） |
| 还原干净 | 逐个 `diff` 对回 `/tmp/72bak/` | 七个文件全 OK |

`jj diff --stat`：21 个文件，+820 / −56。**新增两个文件**（`src/Janpo.Web/RulesetDraft.fs`、
`tests/Janpo.Web.Tests/TableSetupTests.fs`）与一道闸门（`web/scripts/verify-setup.mjs`）。

**没碰**：`src/Janpo.Engine/**`、`web/src/agent/**`、`docs/adr/*`、`CONTEXT.md`、
`web/public/demo-paifu.json`、回放那一半（`replayTick` / `demoLoaded` / `replayControls` 一字未动）、
`.github/workflows/`、`web/index.html`、`web/src/styles.css`。

**与票 75 会撞的两处**（票面预告过，照现状改了）：`web/scripts/verify-browser.mjs` 的名单
（新增第 11 趟）与 `scripts/ci-web.sh` 的趟数措辞（十六道 → 十七道、后十趟 → 后十一趟）。
**新那一趟排在最后**，就是为了不把既有十趟的序号推着走——`ci-web.sh` 里「第七道……第十六道」
那几段一个字都不用改，只在末尾加了一段。

---

## 6. code-review（Standards + Spec 两轴，fixed point `5d0a64d7`）

派不出 sub-agent，按 workbook 自己顺序跑的两轴。

### Standards

- **jj-only ✓**：全程 `jj st` / `jj diff` / `jj commit`，无远端操作、无交互式 flag。
- **工具强制的**：`fantomas --check` / `scripts/check-style.sh` / Biome / tsc 全绿；
  `let mutable` 一处未新增（预算仍是 2）。
- **F# 风格**（`docs/agents/fsharp-style.md`）：
  - 规则 1/3：新代码里没有从里往外读的嵌套。`ofRuleset` 用
    `ruleset.Akadora |> List.isEmpty |> not`，`ruleset` 是一条 `|>` 链。
  - 规则 4.2（算术不硬管道）：自查时**改了一处**——测试里的
    `occurrences` 原本写成 `… |> Array.length |> (fun count -> count - 1)`，
    改回 `text.Split(…).Length - 1`（.NET 方法调用的括号按规则 8 保留）。
    同一轮把 `|> fun table -> …` 那个管道尾 lambda 拆成了具名中间值。
  - 规则 2：`RulesetDraft.ruleset` 里的 `unless (on: bool) (off: Ruleset -> Ruleset)` 返回
    `id` 或 `off`，是**函数值**不是 lambda 包调用，符合。
- **注释写「为什么」✓**：新类型、新出口、新那一排控件、新闸门各写了「为什么是这个形状」
  与「别写成什么」。
- **blocking：0。**

### Spec（票面 6 条行为 + 4 条闸门 + 5 条边界）

逐条对照见票文件的勾选框，三处值得写下来：

- **「拨完按重开才生效」落成了「`Restarted` 是唯一写 `model.Ruleset` 的地方」**，
  而不是「拨的时候记个标志位」——后者要靠纪律，前者靠形状。牌谱那一层还多一道
  结构性保险（`Table.paifu` 读牌桌自己的规则集，见 §1.2）。
- **`TablePage.initial` 的签名多了一个参数**（`RulesetDraft -> Seat option -> LlmSeat -> …`）：
  配桌那三项与模型配置一样是「从 localStorage 读进来的输入」，让 `initial` 保持纯的
  就得从外面给。三个测试文件的 7 处调用点跟着改，**断言一条没动**。
- **`verify-home` 那道闸门变硬了**（不该有的控件从 9 个查到 13 个），没有放宽任何东西。

### 记录但没改的 nitpick

1. `TablePanel.setup` 与 `viewpoints` 里的「种子 + 重开」在**两排**上：语义上它们是一组
   （都要按「重开」才生效）。没合并是因为 `viewpoints` 是 Live/Replay 共用的函数，
   把 Live 那半挪出来会动到票 75 正在改的那一支（派工单明令不许顺手重排）。
   票 73 若要重排配桌那一屏，这是第一处该合的。
2. `RulesetDraft.toWire` 出的是 `长度/赤/食断` 这样一行摘要，只给 `data-rules` 与闸门用；
   它与 localStorage 的三个键**是两套写法**（后者一项一个键）。合成一套会让 localStorage
   变成「一个键存一段结构化文本」，与 `Store` 现在「一个字段一个键」的做法冲突，因此没合。
3. 新那一排的中文标签（「对局长度 / 赤宝牌 / 食断」「有 / 无」）直接写在 `TablePanel` 里，
   而不是走 `toDisplay`。`GameLength.toDisplay` 用上了；「有 / 无」那两个字做成了
   `RulesetDraft.switchToDisplay`；只有三个轴名是字面量——与面板里其他标签
   （「模型坐席」「其余座位」）一致。

---

## 7. 留给人的待审项

1. **超时默认值 240 秒是产品口味的一半**：它有实测上界撑着（17–180 秒），
   但「最坏一手等 12 分钟才看得出模型死了」这件事值得主人点头。改它一行
   （`LlmSeat.initial.TimeoutMs`），面板提示语与那条 `>= 180_000` 的断言会自己跟上。
2. **`RulesetDraft` 这个类型名**（`CONTEXT.md` 里没有）。提案在 `DECISIONS.md` 的 72-1，
   要改名的话现在最便宜——它只被 5 个文件引用。
3. **scope creep 三处，都跑过全套 CI**：① `README.md` 的「怎么玩」加了一步（配桌那三项，
   原来的 5/6 顺延成 6/7）；② `docs/images/table.png` 重出（不重出的话 README 里那张图
   与页面对不上）；③ `verify-export.mjs --llm` 那一档的超时 60000 → 240000（§2）。
4. **一局制仍然不在 `GameLength` 里**（票面「顺手记着」那一条）：真要做对照实验嫌东风战长，
   那是给 `GameLength` 加一个 case 的事，F# 会把该改的 match 全指出来。
