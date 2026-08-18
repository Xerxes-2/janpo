# 78 — 牌谱从外面进来的两条路：导入 JSON 与打开分享链接

**状态**：done　**change**：`kuupkqss`　**fixed point**：`3984a24f`（`main`）

回流那半边接上了：**`?table=1` 上一枚「复制分享链接」**（棋谱 → hash → 真剪贴板），
**带载荷的地址直接进回放并自动播**（与首页 Demo 同一条 `ReplayTable`，时间轴与气泡白拿），
**首页上一格「导入牌谱 JSON」**（全量牌谱 → 回放，气泡有话）。三层失法各一句中文、都不白屏。
`./scripts/ci.sh` **EXIT=0**；dotnet 744 + **196** 条（新增 15），浏览器闸门**十四趟**全 ✓。
新断言 **20 次改坏实验**全部红过（§5），三张截图都亲眼看过（§6）。

一件顺手修掉的真 bug：**「从头再放」在正播着时按下去会双倍速**——旧 `Playback.playing`
把世代号打回 0，在飞的定时器与新发的一起被认下。换成 `Playback.restart`（经 `reborn` 换世代），
导入与分享链接走的也是它（§4）。

---

## 1. 落点一览

| 层 | 文件 | 做了什么 |
|---|---|---|
| 地址 | `Route.fs` | `payload () : string option`（hash 里那段载荷）、`shareUrl : string -> string`（分享链接） |
| 状态 | `TableState.fs` | `ShareOutcome` 三态与阈值、`LiveTable.Shared`、`TableModel.ImportFault`、五条新 Msg、`replayStarted`（三来源共用的 fold+开播）、`shared` 入口、`init` 接 hash |
| 状态 | `Playback.fs` | `playing` 换成 `restart`（接着当前世代往下换；双倍速那个坑） |
| 视图 | `TablePanel.fs` | `hostControls` 加「复制分享链接」+ 分工说明与回执行；`replayControls` 加「导入牌谱 JSON」+ 失败那一行 |
| 视图 | `ThinkingBubble.fs` | `table-no-bubbles` 那句话指回「导入牌谱 JSON」（票 76 的说明接票 78 的入口） |
| 转出 | `TablePage.fs` | `shared` 转出（第十六个入口） |
| 用例 | `tests/.../ImportShareTests.fs`（新，284 行） | 15 条：两条路的入口、三层失法、阈值三态、世代作废、Live/回放互不越界 |
| 闸门 | `web/scripts/verify-inbound.mjs`(新) | 真往返（剪贴板）/ 打开落地 / 坏链接 / 导入两趟 / 坏输入三连 |
| 闸门 | `verify-browser.mjs` / `ci-web.sh` / `verify-home.mjs` | 十三趟变十四趟（十九道 → 二十道）；`table-share` 进 HOST_TEST_IDS（带阳性对照） |
| 文档 | `development.md` / `README.md` | `verify:inbound` 一行；README 两处「还没做」改成事实（§8） |

**没碰**：`Share.fs` / `payload.ts`（票 77 的编解码，只调用）、`Paifu` 格式、引擎、
`web/src/agent/**`、票 74 的地盘（`Awaiting` / `schedule` / `askCmd`、`AgentLine.fs`、
`TableState.bubbles`、`verify-bubbles.mjs`）、`TablePage.initial` / `Table.replay` / `Share.*` 的签名。

## 2. 两条路的形态

### 2.1 分享链接（出去与回来）

**出去**（`?table=1` 的 `hostControls`）：`Shared` 消息 → `Table.paifu roster table |> Share.toPayload`
（`toPayload` 内部走 `Paifu.stripAudit`，推理与 prompt 前置不上 URL）→ `Route.shareUrl payload`
→ `navigator.clipboard.writeText`。**写完才算数**：字符数经 `ShareSettled` 回来，
三态（`ShareOutcome.Copied / Oversized / Failed`）在 update 里判（纯的，dotnet 侧的用例够得着），
页面那一行（`table-share-note`，`data-share` / `data-share-chars`）把分工与下场都说出来：
静句是「地址只带棋谱——推理与 prompt 不上 URL；完整推理用『导出牌谱』的 JSON」，
点过之后追加「已复制（载荷 N 字符）」。

**hash 的确切格式**：`origin + pathname + "#" + base64url载荷`。**`#` 后面直接是载荷**，
没有 `p=` 这类键名（35-1：hash 只装载荷；键名是在暗示「hash 里还会有别人」）。
**不带 `?table=1` 也不带 `?dev=1`**：给访客看的回放不摆配桌面板；`pathname` 照抄当前页，
子路径部署（Pages 的 `/janpo/`）不坏。

**回来**（`init`）：`Landing.Home` 时先问 `Route.payload ()`——有载荷走 `shared payload`
（**模型逐字段就是 `home ()` 那一份**，用例钉着相等；差别只在 Cmd 走 `Share.ofPayload`），
没有照旧 Demo。**三者正交没破**：`?table=1#载荷` 落在主持人那一页，hash 不当路由。
解回来 `SharedLoaded` → `replayStarted`：换上牌谱自带的规则集、自动播（2×）、上帝视角、
时间轴白拿；stripAudit 过的牌谱一条记录都没有 → `recordless` 真 → `table-no-bubbles` 那句话出现。

### 2.2 导入 JSON

首页 `replayControls` 末尾一排：`<input type="file">`（`table-import`，label 就叫「导入牌谱 JSON」）。
`ImportPicked file` → `importCmd`：`file.text()`（Emit，与 `Download` 同款理由）→
**解码也在 Cmd 这侧**（`Decode.fromString` 的 JS 后端只在浏览器里跑得动，而 update 要在 dotnet
上测；wire 层的事一律留在边界，与 `Share.ofPayload` / `Demo.paifu` 同一个分工）→
`ImportLoaded (Result<Paifu, string>)`——与 `DemoLoaded` / `SharedLoaded` **三个来源同一形状**，
落进同一个 `replayStarted`。全量牌谱带决策记录 → 帧上按手序切好（票 71 的 `recordedBy`）→
**气泡有话**（票 76 的取值器一行没动）。

**导入失败不轰掉正在播的那份回放**：原因落在 `TableModel.ImportFault`（`table-import-fault`，
一句红字），牌桌照旧——`ReplayTable.Failed` 只给「除这份牌谱没有别的可摆」的 Demo 与分享链接。
坏链接那一屏（`Failed`）上导入那一排也在：那正是人最需要换一份牌谱的时刻。

### 2.3 三层失法（前缀照票 77，没发明新的）

| 前缀 | 谁写的 | 两条路里的落点 |
|---|---|---|
| `载荷读不动：…` | `payload.ts`（票 77） | 分享链接：截断 / 抄错一位 → `ReplayTable.Failed` |
| `牌谱读不动：…` | `Share.ofPayload` / `importCmd` | 载荷里牌谱不合形状；导入的文件不是 JSON、缺字段（引擎英文诊断跟在后面） |
| `载荷里那份牌谱回放不动：…` / `牌谱回放不动：…` | `sharedLoaded` / `importLoaded` | 读得动、`Table.replay` 推不下去（`ReplayError.toDisplay` 的话） |
| `这个文件读不进来：…` | `importCmd` 的 catch | 浏览器读文件本身 reject |

## 3. 阈值：8,000 字符，依据写清楚

`ShareOutcome.threshold = 8000`（唯一真源，update 判、页面那句话与用例都引它）。
**这是判断不是实测**（票 77 §9 的建议原样采纳）：票 77 实测一整场半庄载荷 7,720 字符、
东风战 4,842——阈值取在半庄之上一点，**让「一整场标准对局」永远够发**；浏览器地址栏普遍收
32K 以上（IE 已死），先截断的是聊天工具，而哪一家在几千字符截断没实测过；超过 8,000 的场
（连庄爆长）本来就该走 JSON 文件。**超了仍然复制**（链接在地址栏里照样能用），只是当场把
两个数印出来劝人改用「导出牌谱」——拦着不给复制比多一句劝更霸道。边界钉在用例里：
`Ok 8000 → Copied`、`Ok 8001 → Oversized`。

## 4. 顺手修的真 bug：换牌谱/从头再放不换世代 → 双倍速

`Playback.playing`（世代号回到 0）只在「一记定时器都没发过」的初始时刻安全。回放正在自动播时
（世代恰好还是 0——整场不点任何键世代不动），「从头再放」拿它接手：在飞的那记 `Ticked 0`
与新发的一起被认下，**两条定时器链各自续命，牌桌从此双倍速走**。这是票 71 起就存在的现货 bug，
导入与 `SharedLoaded` 若照抄就是第三、第四个受害者。处置：删 `playing`、立 `Playback.restart`
（经 `reborn` 换世代），三处（Demo/分享/导入的 `replayStarted` 与「从头再放」）都走它。
两条用例钉着（旧世代的 `Ticked` 必须被丢、新世代的照推），红-1 就是没换世代的样子。
**越界说明**：`Playback.fs` 不在票面点名的文件里，但「回放载入那一支」是本票地盘，
且它是本票两条新路的必经处；记 DECISIONS 78-4。

## 5. 每条新断言先红一次（判据 1；20 次改坏，全部实跑、跑完 `diff` 对回备份）

### dotnet 侧（12 处改坏、四轮）

**红-1｜`Playback.restart` 不换世代**（= 本票之前 `Restarted` 的原样）：

```
ImportShareTests.「从头再放」也把在飞的那记定时器作废：正播着时再按不许双倍速 [FAIL]
   从头再放之后世代号必须往前走
ImportShareTests.导入把在飞的那记定时器作废：旧世代的 Ticked 不许再推新回放 [FAIL]
   导入之后世代号必须往前走
失败! - 失败: 2，通过: 13
```

**红-2｜六处独立单点改坏一轮按红六条**（sharedLoaded 前缀写错 / 不拦 Live / 导入成功不撤旧话 /
阈值边界 `>=` / 超阈那句话不带阈值数 / 点复制不撤上一次下场）：

```
导入一份牌谱：换上它、自动播、上一次失败的话撤掉 [FAIL]  Expected: null  Actual: Some(这个文件读不进来：NotReadableError)
点「复制分享链接」先把上一次的下场撤下来 [FAIL]        Expected: null  Actual: Some(Copied 42)
阈值：……8,000 以内算复制成 [FAIL]                      Expected: Some(Copied 8000)  Actual: Some(Oversized 8000)
SharedLoaded 在 Live 那一页一律无事发生 [FAIL]
分享下场那句话：字符数与「导出牌谱」都得在 [FAIL]       Assert.Contains() Failure
载荷里那份牌谱回放不动：一句中文，前缀说得清是第三层 [FAIL]  Expected start: "载荷里那份牌谱回放不动："
失败! - 失败: 6，通过: 9
```

**红-3｜导入失败轰掉回放 / `shared` 不复用 `home` 那一屏 / `Shared` 在回放擅自开桌**：

```
导入的三种失法各有中文原因，而正在播的那份回放不受影响 [FAIL]
打开带载荷的地址与首页同一屏起步 [FAIL]   Expected: { Ruleset = …  Actual: { Ruleset = …
回放那一页没有分享这回事 [FAIL]           Shared 不该把回放变成 Live
导入一份牌谱：换上它、自动播、上一次失败的话撤掉 [FAIL]
失败! - 失败: 4，通过: 11
```

**红-4｜`replayStarted` 不开播 + 顺手丢决策记录**（dotnet 4 条红，浏览器侧见下）。

### 浏览器侧（8 次改坏，每次单跑 `verify-inbound` / `verify-home`）

| 改坏 | 红出来的话（节选） |
|---|---|
| `replayStarted` 不开播 + 丢记录 | `打开分享链接后牌桌没在动（800 ms 前后河合计都是 0 张）`、`导入之后该自动播…却写着「播放」`、`导入的全量牌谱带着决策记录，座位 1 却一个气泡都没有——导入丢了推理`、`带记录的牌谱导进来了，页面上却还挂着「这一局没有思考气泡」` |
| `shareUrl` 带上 `?table=1` | `分享链接带上了 ?table=1：访客会被摆上一张配桌面板`（外加落地那一屏连环红：摆出配桌面板 / 没有 no-bubbles / 时间轴拖不到末帧） |
| `shareUrl` 用 `#p=` 键名 | `hash 里不是一段光秃秃的 base64url 载荷：「#p=eJy1…」`、`载荷读不动：载荷里混进了 base64url 之外的字符「=」` |
| 复制时悄悄丢一条事件 | `载荷解出的事件流与导出的那份不同：第 0 条：原牌谱 {"type":"start_game"…}，载荷里 {"type":"start_kyoku"…}` |
| 根本不写剪贴板、直接报「已复制」 | `剪贴板里不是一条地址：`（**闸门读的是真剪贴板**，DOM 里塞一份副本骗不过它） |
| `Route.payload` 恒 None（= 票 78 之前的现状：带 hash 退回 Demo） | `分享链接回放到末帧与主持人那一桌不同：…东1 vs 东4 [32000,15000,34000,19000]`、`改坏一个字符的链接该红在「载荷读不动」，页面上却是：（页面上什么也没说）` |
| no-bubbles 那句话退回票 76 原文 + 导入前缀自己发明 | `那句「为什么没有气泡」没指回「导入牌谱 JSON」：人不知道完整版从哪儿进来`、`导入「不是 JSON」该红在「牌谱读不动：」，页面上却是：读不动：Given an invalid JSON…` |
| 把「复制分享链接」从主持人那一页拿掉 | `verify-home`：`?table=1 上没有 [data-testid="table-share"]：那么「首页上没有它」那一条永远为真（空转）` |

**一次空演习被自己的规矩抓住**：no-bubbles 那句话的第一版改坏 sed 没匹配上（fantomas 换过行），
闸门照绿——照 W4 入册那条「注入式演习必须 assert 注入真的落了地」，改成 python `assert new != s`
重做才见红。原文留在这里：**改坏实验也要验改坏落了地**。

## 6. 截图：我亲眼看到了什么（判据 7）

**带载荷打开的那一屏**（`/tmp/shared-open.png`，一次性脚本拍的，没进仓库）：从上到下
h1 → 访客向 intro →「自己开一桌 →」→ 控制条**「暂停」**（正在自动播）/ 从头再放 / 倍速 2× 选中
→ 时间轴（滑块在约 1/3 处，「第 8 手・东1局」）→ 跳到 东1 →**「导入牌谱 JSON　选择文件」**
→ 视角排（上帝视角选中）→「上一手：座位 2 手切9索」→ **那句 no-bubbles**（原文含
「再从上面控制条的『导入牌谱 JSON』挑那个文件」，入口就在它上方五行）→ 四家全摊着的牌桌
（东1局、四家 25000、座位 3 一组碰 3万）。**没有配桌面板、没有种子框**——就是给访客的那一屏。

**`docs/images/table.png` 重出**：控制条第五枚**「复制分享链接」**在「导出牌谱」旁边，
下面那行分工说明（「出的地址只带棋谱——…（对方从首页的『导入牌谱 JSON』能看）」）。
其余与票 73 那版一致（配桌三项、四席绑定、档案编辑处、种子 1177 的牌桌）。
另拍了一张点过复制的（`/tmp/host-share.png`）：那行末尾追着「已复制（载荷 1251 字符）」。

**`docs/images/home.png` 重出**：与票 75 那版的差别就是时间轴下多了「导入牌谱 JSON」一排、
no-bubbles 那句话换成指路版。四家仍全摊、无牌背异常。

## 7. 验证记录

| 验的什么 | 命令 | 结果 |
|---|---|---|
| 全套 | `./scripts/ci.sh` | **EXIT=0**；dotnet 744 + 196 条，浏览器十四趟全 ✓（inbound 2.0s） |
| 新闸门单跑 | `cd web && pnpm run verify:inbound` | 绿；30 手载荷 1160 字符、事件流逐条相同、末帧点数一致、气泡有话、三连各红对前缀 |
| 每条新断言先红 | §5 的 20 次 | 全部红过，输出抄在 §5 |
| 还原干净 | 逐个 `diff` 对回 `/tmp/t78bak/*` | 五个 .fs 全 OK，再跑全量 CI 绿 |
| 截图 | `shoot-table.mjs`（`--home` / 默认）+ 一次性脚本 | §6，三张都打开看过 |

`jj diff --stat`：17 个文件，+1220 / −74。

## 8. 关键取舍（详见 DECISIONS「## 78」）

1. **78-1 hash 里没有键名**：`#` 后面直接是载荷。
2. **78-2 阈值 8,000、超了仍复制只是劝**（§3）。
3. **78-3 导入失败不进 `ReplayTable.Failed`**，新立 `TableModel.ImportFault`（§2.2）。
4. **78-4 `Playback.playing` → `restart`**（§4，顺手修的双倍速 bug，越界已声明）。
5. **78-5 导入的解码在 Cmd 这侧**（JS 后端跑不了 dotnet；`ImportLoaded` 与另两个来源同形）。
6. **78-6 README 三处「还没做」改成事实**（scope creep，71-6 的先例）：分享/导入两处是本票
   falsify 的；顺手把同一句里已被 71/76 falsify 的「首页 demo 自动播」「思考气泡」也改了——
   同一行里留一半假话更糟。措辞值得主人过一眼。

## 9. code-review（Standards + Spec 两轴，fixed point `3984a24f`；派不出 sub-agent，自己顺序跑）

### Standards

- **jj-only ✓**；`fantomas --check` / `check-style.sh` / Biome / tsc 全绿；引擎 `let mutable` 未新增（预算 2）。
- **规则 1/2/3**：新代码无命中——`importLoaded` / `sharedLoaded` 是 `Result.bind` 从左往右的链；
  `prop.onChange (ImportPicked >> dispatch)` 用 `>>`（写第一版是 lambda，review 时照
  `SeedEdited >> dispatch` 的先例改掉了）；`Option.map ShareOutcome.toWire |> Option.defaultValue ""` 顺读。
- **规则 4**：`Result.isError live.Table` 这类两层谓词保留，正确。
- **判据 12**：三层失法各有各的 case 与前缀；`ShareOutcome` 三态是三个 case 不是 option 堆。
- **blocking：0。**

### Spec（票面六条行为 + 四条闸门 + 四条边界，全勾）

- **签名冻结守住**：`TablePage.initial` / `Table.replay` / `Share.*` 一字未动；新入口
  `TableState.shared` + `TablePage.shared` 两处都加（照派工单）。
- **票 74 的地盘没碰**：`Awaiting` / `schedule` / `askCmd` / `AgentLine.fs` /
  `TableState.bubbles` / `verify-bubbles.mjs` 零改动。`verify-browser.mjs` 与 `ci-web.sh`
  照现状改（74 若同改，冲突调度器解）。
- **一处解释性的落法**：「no-bubbles 那句话要接得上导入入口——从那里点得到」落成
  **同屏点名**（那句话点名「导入牌谱 JSON」，控件就在它上方的控制条里），不是句中超链接
  ——file input 要人点在控件自己身上，段落里放个假链接更误导。闸门核两头的名字对得上。

### nitpick（只记录，未改）

1. `ImportShareTests.decoded` 与 `importCmd` 各有一份「牌谱读不动：」映射（一个 Newtonsoft
   一个 JS 后端，共 2 行）：真源在 importCmd，浏览器闸门核它；dotnet 那份只为让真解码器诊断过一遍 update。
2. 同一文件再挑同一个文件不触发 `change`（file input 的原生行为）：换个文件或刷新就好，没拦。
3. `verify-inbound` 的 `settles` 与 `verify-home` 的同名帮手形状相同（约 8 行）：第三个消费方出现再抽。

## 10. 留给人的待审项

1. **阈值 8,000 是判断不是实测**（§3）：真要钉死「哪家聊天工具在几千字符截断」得实测，
   现有依据只支持「半庄整场够发」。
2. **README 的措辞**（78-6）：三处改动是「不改就是假的」，但话怎么说值得主人过目。
3. **导入把倍速拨回 2×**：三来源统一走 `replayStarted`（新牌谱从默认节奏起播）。
   人已拨到 8× 再导入会被拉回 2×——要保留人的档速是 `replayStarted` 一行的事，产品口味留人裁。
4. **`?table=1#载荷` 的 hash 被静默忽略**（三者正交的推论）：主持人那一页用不上载荷。
   若想「带载荷就永远看回放」，得推翻 35-1 的正交裁决，本票没动。
