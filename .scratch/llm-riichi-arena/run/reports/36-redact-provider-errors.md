# 36 —— provider 报错原文会把 key 带进牌谱（票 34 挖出的那条真通道，堵上）

**状态**：done　**工作区**：`janpo-ws-a`　**fixed point**：`f7c1f641`（change `nwytrpxr`）

**改了 8 个文件**：

| 文件 | 干了什么 |
|---|---|
| `web/src/agent/redact.ts` | **新**：打码本身（字面量 → 替身，深走一份东西里的每个字符串） |
| `web/src/agent/loop.ts` | `decideWith` 拆成「出口打一道码」+ 原来的循环 `answered`（**唯一入口**在这里） |
| `web/src/agent/decide.ts` | 请求 JSON 读不动那条路不再抄原文（**第四处流向**，见 §6） |
| `web/tests/agent/redact.test.ts` | **新**：11 条用例（字面量层 + 回执层 + 三个出口逐个点名） |
| `web/scripts/fake-endpoint.mjs` | 加 `--echo-key`：**会原样回显 key 的那种自建网关** |
| `web/scripts/verify-redaction.mjs` | **新**：拿它真开一桌、真导出牌谱的闸门（带阳性对照） |
| `scripts/ci-web.sh` | 上面那道进 CI，成为第十道 |
| `docs/host/custom-endpoint.md` | §4 那张「页面会说什么」的表按打码后的措辞改，加 §4.1 讲为什么 |
| `web/package.json` | `verify:redaction` 一条脚本（好让人手工跑） |

**没碰**：引擎（`src/Janpo.Engine/`）、F# 页面（`src/Janpo.Web/`，含票 35 正在改的 `TablePage.fs`）、
`README.md`、`.github/workflows/`、`CONTEXT.md`、`docs/adr/`、**票 34 的 `verify-export.mjs`
与它在 `ci-web.sh` 里那两道**（一个字节都没动，见 §4）。

---

## 1. 打码的唯一入口在哪：`decideWith` 的**出口**

票面说「错误文本的唯一入口」。找下来，**错误文本没有唯一的入口，但留存物有唯一的出海口**：

```
piai.ts  message.errorMessage / String(error) / wire 失败的那句话
   │                                   （三个源头，还不算 loop.ts 自己拼的四类失败）
   ▼
loop.ts  judge → why ──┬─→ failure（→ 牌谱 decisions[].fallback，也是页面上那句话）
                       ├─→ note → 下一轮 promptSections → prompt_tail（→ 牌谱的 prompt）
                       └─→ rawOutput(last) → output（→ 牌谱 decisions[].output）
   ▼
DecideResponse ──(唯一的过界物，ADR-0005)──▶ Agent.fs → TablePage.settle → DecisionRecord / AgentStatus
```

于是打码放在 **`loop.ts` 的 `decideWith` 出口**：

```ts
export async function decideWith(ask: Ask, request: DecideRequest): Promise<DecideResponse> {
  return redactSecrets(await answered(ask, request), request.seat);
}
```

`answered` 是原来那个循环（一个字没改），**除了上面这一行没有第二个调用方**。选它的四条理由：

1. **牌谱与页面上的每一个字都从这份回执组装出来**（`TablePage.settle`）。票面点名的四个留存物
   （`fallback` / `output.error_message` / 重试 prompt / 页面提示）全在它下游，
   而「四个」是今天数出来的数 —— 明天多一个字段，它照样在下游。
2. **深走、不认字段名**：`redactSecrets` 递归每个字符串，不写「记得给新字段也打一遍」这种要人记的规矩。
3. **`missingConfig` 那条路也盖住了**：自定义端点 baseUrl 读不懂时那句话里带着你填的原文，
   它在发请求**之前**就返回了 —— 打在 `AskResult` 那一侧的话，这一条会漏（34-B 的一半在这里）。
4. **打在 `ask` 那一侧还要多想一件事**：谁调 `ask` 谁负责，而 `ask` 是注入的（用例喂录制响应、
   浏览器喂 `piai.ts`）—— 出口只有一个，入口有三个源头且会长。

**代价说清楚**：牌谱里存的 `prompt_tail` 是**打码后的**那一份，与真发出去的那一次差这几个字。
真发出去的那一份不打码是有意的 —— 那把 key 本来就是这个端点自己给回来的，抄回去它不多知道一个字节，
而牌谱是要发给别人的。（`PaifuCheck` 的 `rebuildable` 查的是「每手指得回一份 preamble」，
不是逐字节对照发出去的那一次，因此这条不影响它。）

**打的是字面量，不是「像 key 的正则」**：座位配置里的确切 key 与 baseUrl 各取三种写法
（原样 / 去空白 / 去末尾斜杠）再各加一份 JSON 转义形态，长的先抹。
空的那一格什么都不抹（空串当字面量会把记号插进每个字符之间）。

---

## 2. 反向自证 A：**打码前**，回显 key 的端点让闸门变红（原始输出）

`web/scripts/fake-endpoint.mjs --echo-key`：401，报错正文里把收到的 `Authorization`
与被请求的地址一字不改地抄回去 —— 这就是「用户自建网关」的最坏情形。
`verify-redaction.mjs` 拿它真开一桌（自定义端点 + 一把真交出去的假 key + 模型坐席 0），
走到兜底、导出牌谱、回头查那份字节。**下面这一趟是实现打码之前跑的**：

```
$ cd web && node scripts/verify-redaction.mjs
页面 http://localhost:5173　端点 http://127.0.0.1:36559/v1（会原样回显 key 的 401）　模型坐席 0
交给端点的那把假 key：sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8

端点收到的：
  假端点在 http://127.0.0.1:36559/v1（CORS：http://localhost:5173），固定回 401 并把收到的 key 原样抄进报错
  OPTIONS /v1/chat/completions  origin=http://localhost:5173
    预检要求放行的头：authorization,content-type,x-stainless-arch,…
  POST /v1/chat/completions  origin=http://localhost:5173
    收到 authorization：sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8　原样回显进 401
  POST /v1/chat/completions  origin=http://localhost:5173
    收到 authorization：sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8　原样回显进 401
  POST /v1/chat/completions  origin=http://localhost:5173
    收到 authorization：sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8　原样回显进 401

页面上那句话：座位 0 兜底代打：provider 报错：401: {"message":"invalid api key: sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8 for http://127.0.0.1:36559/v1/chat/completions","type":"invalid_request_error"}（重试 2 次仍无结果）　这一桌已兜底 1 手

下载文件名：janpo-paifu-2088.json　3643 字节
牌谱：事件 4 条　决策记录 1 条
  fallback：provider 报错：401: {"message":"invalid api key: sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8 for http://127.0.0.1:36559/v1/chat/completions","type":"invalid_request_error"}（重试 2 次仍无结果）
  output：{"stop_reason":"error","text":"","tool_call":null,"error_message":"401: {\"message\":\"invalid api key: sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8 for http://127.0.0.1:36559/v1/chat/completions\",\"type\":\"invalid_request_error\"}","usage":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0}}
  prompt_tail 的末一段：…valid api key: sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8 for http://127.0.0.1:36559/v1/chat/completions","type":"invalid_request_error"}
请重新从上面列出的 id 里选一个。

打码闸门没过：
导出物（文件名 + 字节）里出现了 API key：端点回显的那把，原样进了牌谱
导出物里出现了自定义端点的 baseUrl：http://127.0.0.1:36559/v1（那是主持人的内网地址）
牌谱里找不到打码记号 [API key 已打码]：要么端点没回显 key，要么回显没进牌谱 —— 这道闸门于是什么都没证明（它绿得没有意义）
牌谱里找不到打码记号 [端点地址已打码]：端点回显的那个地址没进牌谱
exit=1
```

**这一趟同时把票 34 §5.1 的探针结论在真浏览器里复现了一遍**：三处（`fallback` /
`output.error_message` / 牌谱里的 `prompt_tail`）全中，外加页面上那句话，外加 baseUrl。
34 号用的是一次性探针（不在仓库里），这一次是**真页面 → 真 HTTP → 真下载**。

## 3. 反向自证 B：**打码后**，同一条闸门变绿（原始输出）

代码只多了 §1 那一行（与 `redact.ts`）。同一个脚本、同一个端点，一字未改：

```
$ cd web && node scripts/verify-redaction.mjs
页面 http://localhost:5173　端点 http://127.0.0.1:36929/v1（会原样回显 key 的 401）　模型坐席 0
交给端点的那把假 key：sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8

端点收到的：
  假端点在 http://127.0.0.1:36929/v1（CORS：http://localhost:5173），固定回 401 并把收到的 key 原样抄进报错
  OPTIONS /v1/chat/completions  origin=http://localhost:5173
    预检要求放行的头：authorization,content-type,x-stainless-arch,…
  POST /v1/chat/completions  origin=http://localhost:5173
    收到 authorization：sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8　原样回显进 401
  POST /v1/chat/completions  origin=http://localhost:5173
    收到 authorization：sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8　原样回显进 401
  POST /v1/chat/completions  origin=http://localhost:5173
    收到 authorization：sk-janpo-fake-key-ECHOED-BY-ENDPOINT-yi-7d31c8　原样回显进 401

页面上那句话：座位 0 兜底代打：provider 报错：401: {"message":"invalid api key: [API key 已打码] for [端点地址已打码]/chat/completions","type":"invalid_request_error"}（重试 2 次仍无结果）　这一桌已兜底 1 手

下载文件名：janpo-paifu-2088.json　3496 字节
牌谱：事件 4 条　决策记录 1 条
  fallback：provider 报错：401: {"message":"invalid api key: [API key 已打码] for [端点地址已打码]/chat/completions","type":"invalid_request_error"}（重试 2 次仍无结果）
  output：{"stop_reason":"error","text":"","tool_call":null,"error_message":"401: {\"message\":\"invalid api key: [API key 已打码] for [端点地址已打码]/chat/completions\",\"type\":\"invalid_request_error\"}","usage":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0}}
  prompt_tail 的末一段：…赤5筒

【上一次的回答没有被采用】provider 报错：401: {"message":"invalid api key: [API key 已打码] for [端点地址已打码]/chat/completions","type":"invalid_request_error"}
请重新从上面列出的 id 里选一个。

回显 key 的端点跑了一手：牌谱里没有 key、没有 baseUrl，打码记号都在 ✓
```

**报错该留的都留着**：`401`、`invalid api key`、`invalid_request_error`、`/chat/completions`
一个字没少 —— 主持人照样看得出「端点说我的 key 不对」。

### 3.1 这道闸门为什么不会假绿

票 34 的教训是「一道从不失败的闸门等于没有闸门」。这一道**每次跑都自带阳性对照**，
三条断言互相咬住，任何一环断掉它都会红（因此不需要另配一个 `--poison`）：

| 断言 | 断掉时说明什么 |
|---|---|
| 端点日志里出现过那把 key | key 真的从浏览器交出去了（脚本里的字面量与页面里的那把是同一把 —— 键名写错、坐席没生效都在这里现形） |
| 牌谱里有 `[API key 已打码]` 与 `[端点地址已打码]` | 端点真回显了、回显真进了牌谱，只是进去之前被抹了（哪天端点不回显了，这条会红，提醒人这道闸门白给） |
| 导出物（文件名 + 字节）里没有 key、没有 baseUrl | 正题 |

外加：牌谱里必须有 ≥1 条决策记录，否则这一趟根本没走到通道上。

---

## 4. 票 34 的两道闸门：一字未改，仍绿

`./scripts/ci.sh` 本次实跑（全绿），第八、九、十道的输出：

```
== 浏览器内牌谱导出（下载事件 + 把下下来的字节 fold 回去）==
模式：四家随机选手（不发任何请求）　先走 40 手
localStorage 里躺着一把假 key：sk-janpo-fake-key-NOT-A-REAL-KEY-jia-4f2a91
…
牌谱：版本 2　事件 63 条　决策记录 0 条（其中带 thinking 0 条、兜底 0 条）　已打完 0 局
prompt：尾部共 0 字　preamble 0 份　逐手重建得回去 = true
回放：事件流逐条相同 = true　点数 25000 / 25000 / 25000 / 25000
导出的牌谱下得下来、读得动、回放得回去 ✓

== 反向自证：拌了 key 的导出物必须让那道闸门变红 ==
拌了 key 的导出物被闸门当场逮住：导出物（文件名 + 字节）里出现了 API key：灌进 localStorage 的那把假 key

== 回显 key 的自建网关：报错原文进牌谱前必须已打码 ==
…（§3 那一份）…
回显 key 的端点跑了一手：牌谱里没有 key、没有 baseUrl，打码记号都在 ✓
```

**新的这一道与票 34 那两道分工不同，不是替代**：34 守的是「key 只是躺在 localStorage 里」
（零请求、零 token），36 守的是另一半 —— **key 真交给了端点，而端点把它抄了回来**。
两把假 key 也是两把（`…NOT-A-REAL-KEY-jia-4f2a91` / `…ECHOED-BY-ENDPOINT-yi-7d31c8`），
一眼看得出哪道闸门在说话。

---

## 5. 34-B：`base_url` 整段打掉（不是只留主机名）

**结论：与 key 同样对待，整段换成 `[端点地址已打码]`。**

- **留主机名等于没打码**：内网地址的敏感部分**就是**主机名（`192.168.1.5`、`gw.corp.internal`）。
  票面提的两个选项里，「只剩主机名」保下来的恰好是要藏的那一半。
- **留 scheme/端口没有诊断价值**：主持人要查的时候地址就在配置面板那一格里，
  而牌谱是发给别人的 —— 别人不需要知道你的模型跑在哪台机器的哪个端口上。
- **一份文本，不做两份**：页面上那句话与牌谱里那句话是**同一个字符串**
  （`AgentStatus.Troubled` 与 `DecisionRecord.Fallback` 都取自 `answer.Failure`）。
  为了页面好看而留一份不打码的，就等于给这条通道留了第二个出口 —— 那正是票面禁的「每个 sink 各打一遍」的反面。
- **代价**：`自定义端点的 baseUrl 读不懂：「localhost:11434」` 这句话不再回显你填错的原文。
  已在 `docs/host/custom-endpoint.md` §4 改掉措辞、§4.1 说明为什么，并指回配置面板那一格。

不打码的两处仍然存在，**都不是可分享物**（ADR-0002 说牌谱是唯一的那个），记在这里免得有人以为它们也归打码管：
浏览器自己的 console/DevTools（CORS 那条 `Access to fetch at 'http://127.0.0.1:4199/…'`
是浏览器打的，请求头里的 `Authorization` 更是原样躺在网络面板里），以及 localStorage 本身。

---

## 6. 核过的 sink 清单：票 34 那 12 条里，哪些经过错误文本

| # | 路径（票 34 §5 的编号） | 经过错误文本吗 | 这一票之后 |
|---|---|---|---|
| 1 | 牌谱字节 `version` / `ruleset` / `events` | 否 | 不变（编码器里没有座位配置） |
| 2 | `start_game.names` | 否 | 不变（`Roster.names` 是 `provider/model`，看过一遍，没有 key、没有 baseUrl） |
| 3 | 决策记录的 `prompt`（票 31 后是 `prompt_tail`） | **是** —— 重试那一句「【上一次的回答没有被采用】<原因>」在尾部 | 打码后才落盘（§3 实测） |
| 4 | 决策记录的 `tools` | 否 | 不变 |
| 5 | 决策记录的 `thinking` / `reason` | **有条件是**：34 说「模型看不到 key 所以复述不出」，但**网关能往这两个字段里塞任何东西**（`reason` 是 tool call 的实参，`thinking` 是 provider 给的块） | 一并盖住（深走每个字符串，不认字段名；`redact.test.ts` 里点名验了） |
| 6 | `usage` / `attempts` / `latency_ms` / `applied` / `turn` / `seat` | 否（全是数） | 不变（深走不碰数，用例断言了 `deepEqual`） |
| 7 | 导出文件名 | 否（种子拼的） | 不变，且新闸门也查它 |
| 8 | URL 分享 | 还不存在（M2） | 不变 |
| 9 | 页面自身与截图 | **是** —— 「模型坐席」那一行显示的就是 `failure` | 页面上那句话现在也是打码后的（§3 的「页面上那句话」） |
| 10 | 出网 / 上报 | 否 | 不变 |
| 11 | 仓库里的录制固件 | 否 | 不变（`ask-error-bad-key.json` 是 DeepSeek 打过码的 `****ture`） |
| 12 | `output.error_message` / `fallback` | **是**（这一票的正题） | 打码后才落盘（§3 实测） |

**第四处流向（34 没数到的）**：`decide.ts` 里「请求 JSON 读不动」那条路 ——
`Agent 层读不动这份请求：${String(error)}`，而 **V8 的 JSON 报错会把出错处前后十几个字符抄进消息**：

```
$ node -e 'try { JSON.parse("{\"api_key\": sk-probe-secret-key-1234}") } catch (e) { console.log(String(e)) }'
SyntaxError: Unexpected token 's', ..."api_key": sk-probe-s"... is not valid JSON
```

那份「读不动的请求」正是**带着 key 的那一份**（`Agent.fs` 的编码器把整个座位配置写了进去），
而这句 failure 会一路进牌谱的 `fallback`。今天它触发不了（那份 JSON 由 F# 生成，恒合法），
但它与本票是同一类漏。这条路上**连座位配置都还没解析出来，没有字面量可打码**，
因此处置是**不抄原文**：改成 `Agent 层读不动这份请求（SyntaxError，1234 字节）`。
丢掉的诊断力有限 —— 走到那里就是 F#/TS 契约破了，错误类别与字节数够定位。用例见
`redact.test.ts` 最后一条。

---

## 7. 验证

```
$ ./scripts/ci.sh                  # 全绿（dotnet 侧 + JS 侧十道）
$ cd web && pnpm run test          # node --test，105 条（新增 11 条），全过
$ cd web && pnpm run check         # Biome，Checked 51 files，无 fix
$ cd web && pnpm run typecheck     # tsc --noEmit，干净
$ dotnet fantomas .                # Formatted 0 / Unchanged 141（本票没有 F# 改动）
$ cd web && node scripts/verify-custom-endpoint.mjs --mode blocked   # 手验：票 30 那道仍是 troubled，措辞已打码
```

`--mode blocked` 那一跑里页面上的话（**这就是 §5 里「代价」的样子**）：

```
上一手：座位 1 摸切3万（兜底：provider 报错：连不上自定义端点 [端点地址已打码]：浏览器没能把请求发出去。
常见原因：端点没起、地址写错、端点没放行本页面的跨域请求（CORS），或者本页面是 https 而端点是 http
（mixed content 被拦）。配法见 docs/host/custom-endpoint.md。原始报错：Connection error.（重试 2 次仍无结果））
实测过的是 troubled，这一跑是 troubled —— 一致
```

**没跑的**：`verify-export.mjs --llm`（要真 key、要真请求）与 `verify-custom-endpoint.mjs`
的另外四种模式（要 https / 局域网 / 外网）。对它们的改动：前者**零**；后者只多了一个
`--echo-key` 开关，原来那条 SSE 路径一行没动。

---

## 8. 收尾 review（两轴，fixed point `f7c1f641`）

无法派生 sub-agent，按 RUNBOOK 自己顺序跑两轴。diff 用 `jj diff --from f7c1f641`（**不用 git**）。

### 8.1 Standards 轴

标准来源：`AGENTS.md`、`docs/agents/fsharp-style.md`（**本票零 F# 改动**，不适用）、
`docs/agents/issue-tracker.md`、`docs/agents/triage-labels.md`、ADR-0002 / 0003 / 0005、`CONTEXT.md`，
外加 Fowler 味道基线。工具能管的（Biome 格式与 lint、tsc、fantomas）不重复看 —— 都干净。

**硬违反：0 条。** review 当场修了两条（blocking 那一轮）：

1. **Speculative Generality（已修）**：`secretsOf` 本来是 `export` 的，而模块外一个调用方都没有
   （用例只用 `redactSecrets` 与两个记号常量）。改回模块私有 —— 导出面越窄，
   「打码只在一处做」越难被绕过。
2. **文档序乱（已修）**：`ci-web.sh` 顶上那段按「第 N 道」序号排列，而新加的第十道
   插在了第五与第八之间。已移到第九道之后。

判断题（只记录，不改）：

3. **Duplicated Code**：`verify-redaction.mjs` 重复了 `verify-export.mjs` 的下载/导出管子（约 20 行）。
   **保留**：票面硬约束是「不放宽票 34 的任何断言」，而那个文件是一道活着的 CI 闸门；
   为了去重而去改它的参数面，风险比重复大。两道闸门守的也不是同一件事（§4）。
4. **Duplicated Code（故意的）**：两个打码记号在 `redact.ts` 与 `verify-redaction.mjs` 各写了一份。
   **保留**：闸门引用被验代码的常量就会跟着它一起错；各写一份才能在改措辞时逼人看一眼。两边都注了。
5. **参数顺序**：`redactSecrets(value, seat)` 与邻居 `endpoint.ts` 的 seat-first 惯例
   （`explainFailure(seat, message)`）不一致。**不改**：它是「把这东西变一下」的函数，
   被变的那个该在前面（泛型 `T` 也从它推）。记在这里给人判。
6. **Primitive Obsession**：`Secret` 只是 `{ literal, mask }`。这个体量下不值得再包一层。
7. **ADR-0005**：跨界仍是「F# 调 TS、只传字符串」一个方向，本票没新增接缝；打码全在 TS 侧。
8. **`CONTEXT.md` 里没有「打码」这个词**。本票不得改它（RUNBOOK 第 6 条），建议人裁：见 §9.5。

### 8.2 Spec 轴

对的是票 `36-redact-provider-errors.md` 的五条验收框 + 两条边界，以及它引的票 34 报告 §5.1 / §7。

**缺了的：0 条。** 逐条对应到交付物见票文件里勾上的那五个框。

**可能被当成 scope creep 的四处，逐条交代**：

1. `decide.ts` 那一行（第四处流向）—— 票面写的是「在报错**进入任何留存物之前**打码」，
   而那句 failure 直接进 `fallback`。同一类漏，不顺手堆下一票。改动 1 行 + 注释。
2. **新闸门进 CI**（第十道）—— 票面要求反向自证是硬要求且「票 34 那道每跑必红一次的自证必须继续绿」；
   一次性的手工自证会烂掉（这正是票 34 的教训）。全程本机、不出网、不花 token，符合 M1 增量约束 6。
3. `docs/host/custom-endpoint.md` —— 行为变了（那张表里的句子现在长不一样），不改就是把一份过时的说法留给主持人。
   不在禁改名单里（禁的是 README / `.github/` / `CONTEXT.md` / 引擎），也不是票 35 的地盘。
4. `web/package.json` 里一条 `verify:redaction` —— 与旁边四条 verify 脚本同形，纯发现性。

**实现看着对但得相信的一处**：票面说「打码要在一处做（错误文本的唯一入口）」，
而我放在了**出口**（`decideWith`）而不是入口（`ask` 那一侧）。理由写在 §1：
错误文本没有唯一入口（至少三个源头，还不算 `loop.ts` 自己拼的四类失败与 `missingConfig`），
但留存物只有一个出海口。代价（牌谱里的 prompt 是打码后的那份）已写进代码注释与 §9.2。

**两条边界都守住了**：`jj diff` 里没有任何 `.fs`（因此也没碰票 35 正在改的 `TablePage.fs`），
`verify-export.mjs` 与 `ci-web.sh` 里票 34 那两段一字未改。

**两轴合计**：Standards 8 条（硬违反 0，当场修 2，其余记录）；Spec 5 条（缺了 0，都是交代）。
各轴最重的一条：Standards 是那个多余的 `export`（已修）；Spec 是「入口还是出口」那一条（已交代理由）。

## 9. 留给人的待审项

1. **打码措辞是产品面的字**：`[API key 已打码]` / `[端点地址已打码]` 会出现在页面与牌谱里。
   要换措辞就改 `redact.ts` 顶上那两个常量 —— 但 `verify-redaction.mjs` 里也写死了一份
   （**故意的**：闸门与被验的代码各写各的，改实现就该有人看一眼闸门）。
2. **牌谱里的 `prompt_tail` 是打码后的那一份**（§1 末尾）。若将来要「牌谱里的 prompt 必须逐字节等于发出去的那次」，
   得改成在 `ask` 那一侧打码，代价是 `missingConfig` 那条路会漏，得另想。
3. **打了码逮不住的那一档没变**（票 34 §7.2）：provider 自己打成 `****ture` 那种残缺形态，
   闸门按完整串查，逮不到也不该逮。这一票没有改变这一点。
4. **M2 四家 LLM 同桌时**：每次 `decideWith` 只拿得到**这一席**的配置，因此打的是这一席的 key。
   端点只可能回显它自己收到的那把，今天成立；若将来出现「一个网关服务多席」的接法，要重新想一遍。
5. **`CONTEXT.md` 要不要收一条「打码」**（本票不得改它）。它现在是一个真的领域动作：
   可分享物在离开 Agent 层之前把坐位配置里的字面量抹掉。建议措辞：
   「**打码**：回执离开 Agent 层前，把这一席的 API key 与 baseUrl 的**确切字面量**从每个字符串里抹掉。
   不猜「像 key 的正则」；牌谱是唯一可分享物（ADR-0002），而 provider 的报错原文会流进它。」
