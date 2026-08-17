# 47 —— 给 provider 错误分类：不值得重试的别重试

**状态**：done　**工作区**：`janpo-ws-b`　**fixed point**：`b12ba476`（change `ynxxvomm`）

**一句话**：断电演习**同一场**（种子 2088、座位 1、坏 key、8× 倍速）的 provider 请求
**255 次 → 85 次**（-170，正是票面点名的那 170 次白烧），85 手兜底一手不少、事件流逐条相同、
整场用时 80.4 s → **42.8 s**。**没有改一行 F#**，也没有动兜底行为。

---

## 1. 分类表：判据是「重试有没有意义」

判据落到 HTTP 上只有一句话，而且用的是 **HTTP 自己的语义**，不是一张我们编出来的状态码清单：

> **4xx 说的是「你这份请求不对」** —— 而重试发的是**同一份请求**（重问那一轮只在 prompt 尾部多
> 一句「上一次没被采用」，key、模型名、端点地址、工具 schema 一个字节都没变），所以答案也一样。
> **5xx 说的是「端点这一刻不行」** —— 与我们这份请求无关，再问一遍完全可能就好了。
> **4xx 里点名三个例外**（408 / 425 / 429）：它们说的不是「请求不对」而是「这一刻不行」。

| 类 | 判据（为什么再问一遍还是一样 / 不一样） | 例子 | 判 |
|---|---|---|---|
| 认证失败 | 这个端点不认这把 key；这把 key 没有这个模型或这个地区的权限 | `401` `403` | **不值得** |
| 请求本身不合法 | 端点不接受这份请求（参数 / schema），重问发的还是同一份 | `400` `422`，其余未点名的 4xx | **不值得** |
| 模型或地址不存在 | 这个模型名或这个端点地址不存在 | `404`（模型名写错；baseUrl 漏 `/v1`） | **不值得** |
| 上下文超长 | 这份 prompt 超过了这个模型收得下的长度（重问只会更长） | `413` | **不值得** |
| 余额不足 | 得有人去充值，不是再问一遍的事 | `402` | **不值得** |
| 接线阶段就失败 | provider 不在名单里 / 那一家的模型目录里没有这个名字，重问读的还是同一份座位配置 | `不认识的 provider：…`、`deepseek 的模型目录里没有 …`（**连请求都没发**） | **不值得** |
| 限流 | 窗口滚过去就可能过 | `429` | 值得（**不退避**，见 §5） |
| 端点这一刻不行 | provider 那边的事 | `500` `502` `503` `504` `529` | 值得 |
| 端点等超时 / 来早了 | 时机问题，不是请求问题 | `408` `425` | 值得 |
| 模型超时 | 同一份请求再问一遍完全可能答得完 | `stopReason: "aborted"` | 值得 |
| 格式跑偏 | 把错在哪告诉它，下一轮常常就对了 | 没调 `choose_action`、调了别的工具、`action_id` 不是整数 | 值得 |
| 动作 id 非法 | 同上 | id 不在这一包里 | 值得 |
| 判不出来 | **不知道的时候「可能不一样」成立** | `Connection error.`、`Failed to fetch`、`Provider finish_reason: content_filter`、`errorMessage` 是 null、适配器真抛了异常 | 值得 |

**「判不出来就重试」是有意的**（决策 47-2）。最典型的一条是 **CORS 没放行** —— 它其实
「再问一遍还是一样」，但在浏览器里它与「端点刚起 / 网络抖了一下」长得**一模一样**
（都是 `Connection error.`）。**分不开就别猜**：错向这一边多花两个请求，错向另一边是把
救得回来的一手直接判死。

**判据不看 provider 是谁**：`retry.ts` 只读报错原文里的状态码与几个类别词，因此
新接一家 provider 不必回来改这张表 —— 这正是「不按状态码堆 if」要买到的东西。

### 1.1 状态码从哪来（这是分类器唯一的输入假设）

pi-ai 的 `utils/error-body.ts` 会把 provider 的报错拼成三种形态之一，**三种都认**：

| 形态 | 谁走这条 | 例子 |
|---|---|---|
| `<status>: <body>` | 它自己拼的（DeepSeek、自定义端点） | `401: {"message":"Authentication Fails…"}` |
| `<status> <body>` | SDK 的 message 自带正文（Anthropic / Google） | `401 {"type":"error",…}` |
| `<prefix> (<status>): <message>` | 带前缀的（openai-responses / azure） | `OpenAI API error (401): …` |

**而且不许锚在开头**：自定义端点的报错会先被 `explainFailure`（票 30）加一句中文，
状态码因此躺在句子中间。`ask-error-no-model` 那份固件就是这个形状，用例点名守着它：

```
自定义端点 http://127.0.0.1:4294/v1 回了 404：baseUrl 多半漏了 /v1 之类的路径前缀。
原始报错：404: {"message":"The model `no-such-model` does not exist.","type":"invalid_request_error"}
```

反过来，**地址里的数不许被当成状态码**：只认「前后都是分隔符」的三位数且只收 400–599，
所以 `http://127.0.0.1:4199/v1` 里的 `127` / `419` 一个都不算数 —— 否则那条**最该重试**的
「连不上」会被误判成不值得重试。用例里有一条专门按着它。

## 2. 「没重试是因为重试没意义」在页面与牌谱上长什么样

**没有新增牌谱字段，也没有改一行 F#**（决策 47-4）。理由是页面与牌谱读的是**同一个字符串**：
`AgentStatus.Troubled` 与 `DecisionRecord.Fallback` 都取自回执的 `failure`。
于是把判据写进那句话的收尾，两处一起就有了：

| | 收尾 |
|---|---|
| 值得重试 | `…（重试 2 次仍无结果）`　**原样不动** |
| 不值得重试 | `…（没有重试：<判据>，再问一遍还是一样）` |

CI 第十一道（票 36 的打码闸门）本次实跑打出来的**真页面**那句话：

```
座位 0 兜底代打：provider 报错：401: {"message":"invalid api key: [API key 已打码] for
[端点地址已打码]/chat/completions","type":"invalid_request_error"}（没有重试：这个端点不认这把 key，
再问一遍还是一样）　这一桌已兜底 1 手
```

牌谱那一侧再加一个**机器读得出来的**证据：`attempts`。断电演习那 85 条决策记录
**全部是 `attempts: 1`**（改之前全部是 3），而 `fallback` 里 85 条全含「没有重试」、
0 条含「重试 2 次仍无结果」。

**不加新字段是决策，不是省事**（47-4）：`attempts` + `fallback` 已经把这件事说完了，
再加一个字段是同一件事的第二种说法，还要按票 26 的策略涨牌谱版本。

**兜底行为一字未动**：交不出动作仍由引擎 `Fallback.action tier package` 代打
（Bare 档摸切）。变的只有「交不出来之前问了几次」。

## 3. 断电演习前后对照（这一票要的那个数）

同一个脚本（`reports/27-run-game.mjs`，路径改指本工作区）、同一种子、同一座位、同一倍速、
同一把坏 key。**改之前那一列抄自 27 号报告 §4.3 / §5.1，没有重跑**（它不需要重跑：
兜底是确定性的摸切，两趟的事件流逐条相同，见下）。

| | 27 号（改之前） | 本票（改之后） | 差 |
|---|---:|---:|---|
| provider 请求 | **255** | **85** | **-170** |
| 其中 4xx/5xx | 255 | 85 | -170 |
| 每手请求数 | 3（首问 + 重试 2 次） | **1** | -2 |
| 兜底代打 | 85 手 | **85 手** | **0（一手不少）** |
| 局数 | 4 | 4 | 0 |
| 事件 | 607 | 607 | 0 |
| `same_events` | true | **true** | —— |
| 整场用时 | 80.4 s | **42.8 s** | -47% |
| 牌谱里的 prompt 尾部合计 | 70,982 字 | **54,780 字** | -23%（少的正是那句「上一次的回答没有被采用」） |
| 决策记录的 `attempts` | 全 3 | **全 1** | —— |

本次实跑的原始输出（尾部）：

```
$ node /tmp/janpo-47-game.mjs --bad-key --seed 2088 --seat 1 --speed 8 --keep /tmp/janpo-47-badkey.json
一整场东风战　种子 2088　模型坐席 1（deepseek-v4-flash，思考预算 off）　脚手架 bare　倍速 8×
开局状态：模型座位已就位，还没轮到它

第 1 局　东1局 0 本场　供托 0 根　用时 9.8 s　请求 20 次　累计兜底 20 手
第 2 局　东2局 1 本场　供托 0 根　用时 10.8 s　请求 22 次　累计兜底 42 手
第 3 局　东3局 2 本场　供托 0 根　用时 9.8 s　请求 18 次　累计兜底 60 手
第 4 局　东4局 3 本场　供托 0 根　用时 11.8 s　请求 25 次　累计兜底 85 手

终局精算1位 座位0 25000  2位 座位1 25000  3位 座位2 25000  4位 座位3 25000

整场用时 42.8 s，共 4 局
Agent 状态：座位 1 兜底代打：provider 报错：401: {"message":"Authentication Fails, Your api key:
****-key is invalid","type":"authentication_error","param":null,"code":"invalid_request_error"}
（没有重试：这个端点不认这把 key，再问一遍还是一样）　这一桌已兜底 85 手
provider 请求：85 次，其中 4xx/5xx 85 次
页面之外的请求去了哪：{"https://api.deepseek.com":85}
浏览器资源错误（请求失败）：85 条

下载文件名：janpo-paifu-2088.json　157530 字节
fold 回来：{"version":2,"events":607,"decisions":85,"kyokus":4,"same_events":true,"mismatch":null,
"scores":[25000,25000,25000,25000],"juni":[1,2,3,4],"thinking":0,"fallbacks":85,"preambles":1,
"tail_chars":54780,"rebuildable":true}
一整场东风战跑完 ✓
```

**每手 20/22/18/25 次请求 = 每局的手数**（27 号那一趟是 60/66/54/75）——一手一次，逐个对得上。

**没有真 key**：这台机器上 `/tmp/deepseek_key` 不存在，因此**只跑了断电演习**（它按设计用的就是
一把 `sk-deliberately-broken-key`，不读那个文件）。真跑一场的对照不在本票的验收里，
且真跑那两场 27 号实测**重试 0 次**，本票对它零影响。要真 key 的那六份录制固件也**一份都没重录**
（`jj diff` 里它们零改动）。

### 3.1 真浏览器里两类各一条（`--fail` 那个新开关）

`fake-endpoint.mjs` 新加的 `--fail <status>` 让本机假端点固定回一个失败状态，
`verify-custom-endpoint.mjs` 顺手把它透传出来并**数这一手发出去了几条 POST**：

```
$ node scripts/verify-custom-endpoint.mjs --mode allowed --fail 401
  座位 1 兜底代打：provider 报错：401: {…}（没有重试：这个端点不认这把 key，再问一遍还是一样）
  这一手发出去的 POST：1 条（首问 + 重试）

$ node scripts/verify-custom-endpoint.mjs --mode allowed --fail 429
  座位 1 兜底代打：provider 报错：429: {"message":"Rate limit reached for requests, please slow down.",
  "type":"rate_limit_error"}（重试 2 次仍无结果）
  这一手发出去的 POST：3 条（首问 + 重试）
```

**不带 `--fail` 那一跑仍是 `spoke`**（票 30 那道人工验收没被这个开关影响，实跑过）。

## 4. 先红后绿（红的原始输出）

### 4.1 不值得重试那一类：红

两条新用例在实现之前跑（固件已就位，`retry.ts` 还不存在）：

```
$ node --test tests/agent/loop.test.ts
✖ 不值得重试：401 只问一次就走兜底，不占额外请求 (0.534268ms)
✖ 不值得重试：模型名不存在（404）也只问一次 (0.293124ms)
ℹ tests 20  ℹ pass 18  ℹ fail 2

✖ failing tests:

test at tests/agent/loop.test.ts:88:1
✖ 不值得重试：401 只问一次就走兜底，不占额外请求 (0.534268ms)
  AssertionError [ERR_ASSERTION]: 第一次 401 就已经注定后两次也是，那两个请求是白烧的

  3 !== 1

    actual: 3,
    expected: 1,
    operator: 'strictEqual',

test at tests/agent/loop.test.ts:121:1
✖ 不值得重试：模型名不存在（404）也只问一次 (0.293124ms)
  AssertionError [ERR_ASSERTION]: Expected values to be strictly equal:

  3 !== 1
```

### 4.2 值得重试那一类：**它在改之前就是绿的**，因此另按红一次

这一类守的是**别把该重试的也一起砍掉**，所以它在旧行为下本来就过。
一条从不失败的用例等于没有用例（票 34 的教训），因此**把分类反过来**跑一遍：
删掉 `retry.ts` 里那半句 `|| status >= 500 || TIMING_4XX.has(status)`，
让每一个认得出的状态码都判成「不值得重试」：

```
$ node --test tests/agent/loop.test.ts tests/agent/retry.test.ts   # 分类被反过来的那一版
✖ 值得重试：限流（429）照样重试到上限 (0.501816ms)
  AssertionError [ERR_ASSERTION]: 限流是「这一刻不行」，不是「你这份请求不对」

  1 !== 3

test at tests/agent/loop.test.ts:113:1
✖ 值得重试：端点自己挂了（503）也重试 (0.207323ms)
  AssertionError [ERR_ASSERTION]: Expected values to be strictly equal:

  1 !== 3

test at tests/agent/retry.test.ts:22:1
✖ 录下来的那四份真响应，各归各的 (0.956612ms)
  + actual - expected

  + '不值得：端点说这份请求它不接受，而重问发的是同一份请求'
  - '值得'

test at tests/agent/retry.test.ts:39:1
✖ 5xx 说的是「端点这一刻不行」：与我们这份请求无关，因此值得 (0.102731ms)
  AssertionError [ERR_ASSERTION]: 500 该判成值得重试

  false !== true
```

**11 条红**（`loop.test.ts` 2 条 + `retry.test.ts` 4 条各自的多个断言）。改回去之后 29 条全绿。

### 4.3 顺带按红的一条：票 36 的打码闸门

401 不再重试之后，**「重试那一轮的 prompt」这个出口在 401 这条路上根本不存在了**，
于是 `redact.test.ts` 里「三个出口一个都没漏」那条当场红：

```
✖ 端点原样回显 key：三个出口（fallback / output / 牌谱里的 prompt）一个都没漏
  AssertionError [ERR_ASSERTION]: 重试那一句进了牌谱的 prompt
    actual: false, expected: true
```

**没有放宽它，而是补强**（做法记在 §7）：那条用例改喂一个**回显 key 的 429**
（回显是网关写报错的习惯，与它回哪个状态码无关，同一段代码 401 与 429 都走），
于是三个出口照旧全在，并加了一条 `attempts === 3` 钉住「这个出口只有值得重试的那一类才有」；
另新增一条 401 的用例，断言另外两个出口照样打码、而第三个**确实不存在**。
少一个出口不等于少守一个出口，但**得有人盯着它真的没了**，否则上面那条的覆盖会静静地缩水。

## 5. 429：值得重试，**不做退避**，理由

**结论**：429 归「值得重试」（它与 401 的区别正是这一票的判据：再问一遍**可能**不一样），
但**不在重试之间等**。三条理由，按份量排：

1. **退避的参数没有依据 —— `Retry-After` 我们拿不到。** pi-ai 的 `AssistantMessage` 只有
   `stopReason` / `errorMessage` / `usage`，**不透传响应头**（核过 `dist/types.d.ts`：
   响应头只出现在 `onResponse` 回调里，那是另一条接缝，且要改 `piai.ts` 的调用形状）。
   没有 `Retry-After`，1 秒还是 2 秒都是编的 —— 而票面要的是「做退避就说清参数依据」。
2. **要为此新增一个时钟接缝。** 循环里 sleep 就得让用例拨得动表（否则 `pnpm test` 会真睡几秒），
   那是往这一层塞一个新的可注入依赖 —— 为一件 **M1 一次都没观测到**的事付结构成本。
   票面自己写了「别过度设计」。
3. **代价上限已知且小。** 429 时最坏是 2 个额外请求，随即兜底，对局不卡（断电演习证明了
   全程兜底也打得完）。而限流真来的时候，退避 1–2 秒多半也不够 —— 各家的窗口是分钟级。

**翻转这条的条件写在明处**（与 23-6 被翻转的方式一样）：真遇上限流，牌谱里就是 `attempts=3`
+ `fallback` 带着 429 原文，页面上「这一桌已兜底 N 手」。**拿那个实测数来翻**，
就像 27 号拿 255 次 401 翻掉 23-6 —— 不是有人想通了，是有人量了。

**被否决**：固定 1s/2s 退避（数字编的）、读 `Retry-After`（拿不到）、
把 429 归到「不值得重试」（它是**时机**问题，不是**请求**问题 —— 那会把一整类可救的失败判死）。

## 6. 改了哪几个文件

| 文件 | 干了什么 |
|---|---|
| `web/src/agent/retry.ts` | **新**：判据本身（`Retry` 类型、`retryOf`、状态码三种写法的识别） |
| `web/src/agent/loop.ts` | `Verdict` 的失败支带上 `retry`；不值得重试的 `break`；收尾那句话分两种（`gaveUpBecause`） |
| `web/tests/agent/retry.test.ts` | **新**：9 条（含两条**真的调 `piAsk`**、零网络的接线失败） |
| `web/tests/agent/loop.test.ts` | +4 条（两类各两条） |
| `web/tests/agent/redact.test.ts` | 票 36 那条改喂 429 并加 `attempts` 断言；**新增**一条 401 的（§4.3） |
| `web/tests/agent/fixtures.ts` | 三份新固件的入口 |
| `web/tests/fixtures/agent/ask-error-{rate-limited,server,no-model}.json` | **新**：录制的 429 / 503 / 404 |
| `web/scripts/fake-endpoint.mjs` | `--fail <status>`：固定回一个失败状态（录 429/5xx 的靶子） |
| `web/scripts/record-agent-fixtures.mjs` | 三份新录制；要真 key 的那几份在没有 key 时跳过而不是崩 |
| `web/scripts/verify-custom-endpoint.mjs` | `--fail` 透传 + **数这一手发出去几条 POST**（§3.1） |
| `docs/host/custom-endpoint.md` | §4 加三行：那个括号里的收尾在说什么 |

**没碰**：任何 `.fs`（含 `TablePage.fs`、`Paifu.fs`）、`web/src/styles.css`、
`web/src/agent/template.ts`、`CONTEXT.md`、`docs/adr/`、`README.md`、`.github/`、
票 34/36 的那三道闸门脚本与 `ci-web.sh`。

### 6.1 新固件为什么算「录制」而不是手编

429 与 5xx **问真 provider 是要不来的**（限流不听人调度，5xx 更不会点单）。
于是给假端点加了 `--fail`，由 **真的 `piAsk` + 真的 pi-ai `openai-completions` 适配器 +
真的 HTTP 响应**走完整条链录下来。这件事有实质意义：分类器唯一的输入假设是
「pi-ai 把状态码写成 `401: {…}`」，**这三份固件就是那个假设的活证据** —— 手编一份 JSON
等于用我以为的格式证明我自己。（`ask-error-no-model` 那份还顺带把
`explainFailure` 会在前面加一句中文这件事钉住了，见 §1.1。）

## 7. 验证

```
$ ./scripts/ci.sh                  # 全绿（dotnet 侧 + JS 侧十二道）
$ cd web && pnpm run test          # node --test，146 条（新增 13 条），全过
$ cd web && pnpm run typecheck     # tsc --noEmit，干净
$ cd web && pnpm run check         # Biome ci --error-on-warnings，Checked 64 files，无 fix
$ dotnet fantomas .                # Formatted 0 / Unchanged 148（本票零 F# 改动）
$ node scripts/verify-custom-endpoint.mjs --mode allowed              # 手验：票 30 那道仍是 spoke
$ node scripts/verify-custom-endpoint.mjs --mode allowed --fail 401   # 手验：1 条 POST
$ node scripts/verify-custom-endpoint.mjs --mode allowed --fail 429   # 手验：3 条 POST
$ node /tmp/janpo-47-game.mjs --bad-key --seed 2088 --seat 1 --speed 8  # 断电演习：85 次
```

CI 里那三道与本票相关的闸门本次实跑都绿，且**其中一道自己就是对照**：
第十一道（票 36）的假端点日志从 3 条 POST 变成 1 条 POST，页面那句话变成「没有重试：…」。

## 8. code-review（Standards 一轴，fixed point `b12ba476`）

无法派生 sub-agent，按 RUNBOOK 自己顺序跑。diff 用 `jj diff --from b12ba476`（**不用 git**）。
标准来源：`AGENTS.md`、`docs/agents/fsharp-style.md`（**本票零 F# 改动**，不适用）、
`CONTEXT.md`、ADR-0002 / 0005、`docs/agents/issue-tracker.md` 与 `triage-labels.md`，
外加 Fowler 味道基线。工具管得住的（Biome 格式与 lint、tsc、fantomas）不重复看 —— 都干净。

**硬违反：0 条。** review 当场修了三条：

1. **Mysterious Name（已修）**：`TIMING` / `NAMED` 两个表名说不出它们只装 4xx，而它们与
   `OTHER_4XX` 是一组。→ `TIMING_4XX` / `NAMED_4XX` / `OTHER_4XX`，一眼看得出是同一张表的三块。
2. **Mysterious Name（已修）**：`gaveUp(...)` 返回的是一句话，却和返回整份回执的 `refuse(...)`
   并排站着，读起来像两个同类。→ `gaveUpBecause(...)`，`refuse(gaveUpBecause(...))` 读成
   「因为……而交不出来」。
3. **Speculative Generality（已修）**：`fake-endpoint.mjs` 的 `FAIL_BODIES` 里写了 `500`，
   而没有任何调用方用它，且通用兜底那一句本来就覆盖得了。→ 删掉。

判断题（只记录，不改）：

4. **Duplicated Code**：`record-agent-fixtures.mjs` 的 `fromFakeEndpoint` 与
   `verify-custom-endpoint.mjs` 的 `startEndpoint` 都 spawn 那个假端点（各约 8 行）。
   **保留**：两者等待方式不同（一个等第一行 stdout，一个 sleep 800ms 并把日志收进数组），
   合并要去改票 30 那道活着的人工闸门的参数面，风险比重复大（同票 36 §8.1 第 3 条的判法）。
5. **重复写了 5 遍 `retry: WORTH`**（`judge` 里那四类 + catch 那条）。
   可以用一个默认参数的 `no(why, retry = WORTH)` 收掉。**保留**：这一票的正题就是
   「哪一类失败值不值得重问」是**逐类做的判断**，把四类做成隐式默认，等于把要给人看的那件事藏起来。
6. **`retry.ts` 按特征词认 `piai.ts` 那两句中文**（`不认识的 provider…` / `…的模型目录里没有…`）。
   耦合是真的，但**钉在用例里**：`retry.test.ts` 有一条真调 `piAsk`（零网络），措辞漂了当场红。
   不 import 是因为一 import 就把 pi-ai 的 SDK 拖进这一层（同 `endpoint.ts` 分家的理由）。
7. **`Retry.because` 是裸 string（Primitive Obsession）**。它是一句给人看的话，
   直接进页面与牌谱，这个体量下不值得包类型。
8. **ADR-0005**：跨界仍是「F# 调 TS、只传字符串」一个方向，**本票没新增接缝**，
   回执的字段一个没加没改（因此 `Agent.fs` 的 `answerDecoder` 一字未动）。
9. **ADR-0002**：牌谱是唯一可分享物 —— 本票**没有改牌谱格式**（决策 47-4），版本仍是 2。
10. **`CONTEXT.md` 里没有「重试判据」这个词**。本票不得改它（RUNBOOK 第 6 条），
    建议措辞见 DECISIONS 的 47-B。

**Spec 一轴**（票面 6 条验收 + 3 条边界）：缺了 0 条，逐条对应见票文件里勾上的框。
两处可能被当成 scope creep 的，逐条交代：

- `verify-custom-endpoint.mjs` 的 `--fail` 透传与 POST 计数 —— 票面要「页面上要能看出没重试」，
  而**能看出**得有人在真浏览器里数一遍。改动 6 行，且不带这个开关时那道闸门一字未变（实跑过）。
- `docs/host/custom-endpoint.md` —— 行为变了，那张「页面会说什么」的表下面多了个括号。
  不改就是把一份过时的说法留给主持人（同票 36 的判法）。它不在禁改名单里。

## 9. 留给人的待审项

1. **47-A：`TablePage.fs` 那段帮助文字还写着「重试两次仍不行就兜底代打」**，对不值得重试的那一类
   已经不成立。**本票不得碰 `TablePage.fs`**（票 44 在那），因此没改。建议票 44 顺手带走，
   措辞见 DECISIONS 47-A。
2. **47-B：「重试判据」没进 `CONTEXT.md`**（本票不得改它）。建议措辞见 DECISIONS 47-B。
3. **429 的退避留着没做**（§5）。翻转它的判据与数据来源已经写死在那一节里。
4. **`Connection error.` 这一档仍然重试 2 次**，其中「CORS 没放行」那一半是白烧的。
   要分开得让 `piai.ts` 把「浏览器压根没发出去」与「发出去了但连不上」分开报，
   而 OpenAI SDK 把两者统一成同一句话（票 30 §4 实测）。**今天分不开就别猜**（47-2）。
5. **`ask-error-rate-limited` / `ask-error-server` 是本机假端点录的**，不是某一家真 provider 的
   429/503 原文。措辞可能与真家不同，但**分类只看状态码**，因此不影响判据；
   哪天真撞上限流，值得把真原文录回来替换。
