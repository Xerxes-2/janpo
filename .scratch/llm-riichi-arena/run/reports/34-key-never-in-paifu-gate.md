# 34 —— 「牌谱里不含 key」这道闸门要在 CI 里真跑

**状态**：done　**工作区**：`janpo-ws-b`　**fixed point**：`f59b71c9`（change `ktlqsonq`）

**改了两个文件**：`web/scripts/verify-export.mjs`（导出验收脚本）与 `scripts/ci-web.sh`（它的 CI 调用处）。
**没碰**：引擎、`web/src/`（含 `agent/`、`styles.css`）、`src/Janpo.Web/`、README 正文、`CONTEXT.md`、`docs/adr/`。
（下面第 4 节记的那次**临时改 `TablePage.fs` 做反向自证**已经原样还回去了，`jj diff` 里没有它。）

---

## 1. 闸门现在怎么跑

`web/scripts/verify-export.mjs` 的 **CI 那一档**（四家随机选手，默认档，不带 `--llm`）多了一步：

```js
// 票 34 的那一档：**key 灌进去，坐席不给**。
await page.addInitScript((fakeKey) => {
  localStorage.setItem("janpo.llm.seat", "");        // 空串 = 四家都随机（Store.readSeat）
  localStorage.setItem("janpo.llm.api_key", fakeKey);
}, FAKE_KEY);
```

- 假 key 是**写死的 ASCII 字面量** `sk-janpo-fake-key-NOT-A-REAL-KEY-jia-4f2a91`
  （`-fake-` + `NOT-A-REAL-KEY` + `jia`）。**绝不从 `/tmp/deepseek_key` 读**。
- 全 ASCII 是故意的：断言按**字节**在导出物里找它，而 JSON 编码器可以把非 ASCII 写成 `\uXXXX`
  ——「假 key」三个汉字那样写就找不着了，闸门会假绿。
- **模型坐席不给**（座位存空串），因此四家仍是随机选手，**一个网络请求都没发**：
  页面照样把这把 key 从 localStorage 读进配置（`Store.readSeatConfig`），
  于是「浏览器里确实有一把 key 可以夹带」这个前提是真的，只是没有任何请求会用到它。

断言查的是**导出这条路给出去的两样东西**：那份字节 **与文件名**（文件名拼的是人随手填的种子）：

```js
const shareable = `${download.suggestedFilename()}\n${poison ? poisoned(text, plantedKey) : text}`;
if (!withLlm && shareable.includes(FAKE_KEY)) problems.push("导出物（文件名 + 字节）里出现了 API key：…");
if (apiKey !== null && shareable.includes(apiKey)) problems.push("导出的牌谱里出现了 API key");
```

**两条断言并存，不是替代关系**：第一条恒跑（CI 里也跑），守的是「key 只是躺在 localStorage 里」；
第二条是票 26 原有那条，只在 `--llm` 手验档跑，守的是「key 真交给过 provider、
决策记录里带着真 prompt 与真输出」——那是另一种夹带机会（见第 5 节）。

`scripts/ci-web.sh` 里这一道的日志（本次实跑）：

```
== 浏览器内牌谱导出（下载事件 + 把下下来的字节 fold 回去）==
模式：四家随机选手（不发任何请求）　先走 40 手
localStorage 里躺着一把假 key：sk-janpo-fake-key-NOT-A-REAL-KEY-jia-4f2a91
走完之后：上一手：座位 2 吃4筒（亮3筒 5筒）
Agent 状态：四家都是随机选手

下载文件名：janpo-paifu-2088.json　4200 字节
牌谱：版本 1　事件 63 条　决策记录 0 条（其中带 thinking 0 条、兜底 0 条）　已打完 0 局
回放：事件流逐条相同 = true　点数 25000 / 25000 / 25000 / 25000
导出的牌谱下得下来、读得动、回放得回去 ✓
```

那一行 `localStorage 里躺着一把假 key：…` 是故意打出来的：**日志里看得见这道闸门真的带着 key 跑过**，
而不是「代码里有那行断言」。

---

## 2. 反向自证也进了 CI（第九道）

一道从不失败的闸门等于没有闸门，所以自证不是一次性的记录，而是**每次 CI 都按红一次**。
`verify-export.mjs` 多了一个 `--poison`：

```js
const poisoned = (paifu, key) => JSON.stringify({ ...JSON.parse(paifu), leaked_api_key: key });
```

拿**真下下来的那份牌谱**拌一把 key 进去（只脏「拿去做泄漏断言的那份字节」，回放读的仍是原样的
`text`，因此红的只会是泄漏那一条，不会混进别的原因）。它只认「这是一个 JSON 对象」、
不认牌谱的具体字段，**票 31 把牌谱格式改成什么样它都还在**。

`scripts/ci-web.sh` 紧跟着导出那一道跑它，并**核实红的原因就是那把 key**：

```bash
if (cd web && node scripts/verify-export.mjs --turns 12 --poison) >"$poison_log" 2>&1; then
  cat "$poison_log"; echo "反向自证没过：拌了 key 的导出物竟然过了闸门——那条断言等于没有。" >&2; exit 1
fi
if ! grep -q "里出现了 API key" "$poison_log"; then
  cat "$poison_log"; echo "反向自证没过：闸门是红了，但不是因为那把 key（红的原因见上）。" >&2; exit 1
fi
```

CI 里这一道的输出（本次实跑）：

```
== 反向自证：拌了 key 的导出物必须让那道闸门变红 ==
拌了 key 的导出物被闸门当场逮住：导出物（文件名 + 字节）里出现了 API key：灌进 localStorage 的那把假 key
```

这段 bash 的三种分支我单独试过（用桩命令模拟退出码与日志，不跑浏览器）：
「脏牌谱竟然过了闸门」→ 报错；「红了但不是因为 key」→ 报错；「红了且正是因为 key」→ 通过。

---

## 3. 反向自证 A：拌了 key 的牌谱喂给同一条断言（**闸门变红的原始输出**）

```
$ cd web && node scripts/verify-export.mjs --turns 12 --poison
模式：四家随机选手（不发任何请求）　先走 12 手
localStorage 里躺着一把假 key：sk-janpo-fake-key-NOT-A-REAL-KEY-jia-4f2a91
走完之后：上一手：座位 0 碰中（亮中 中）
Agent 状态：四家都是随机选手

下载文件名：janpo-paifu-2088.json　2065 字节
牌谱：版本 1　事件 21 条　决策记录 0 条（其中带 thinking 0 条、兜底 0 条）　已打完 0 局
回放：事件流逐条相同 = true　点数 25000 / 25000 / 25000 / 25000

导出验收没过：
导出物（文件名 + 字节）里出现了 API key：灌进 localStorage 的那把假 key
poison exit=1
```

---

## 4. 反向自证 B：**让页面自己把 key 漏出来**（临时改代码，已还原）

自证 A 证明的是「断言对字节有效」。它证明不了另一半——**那把假 key 真的到得了页面**。
如果 localStorage 的键名写错（比如写成 `janpo.llm.apikey`），页面根本读不到它，
断言就永远是绿的，那正是这张票要防的那种假绿。所以又做了一次真泄漏：

临时把 `src/Janpo.Web/TablePage.fs` 的导出那一句改成把配置里的 key 拼进**文件名**
（一行，`exportName model.SeedText` → `$"janpo-{model.Llm.ApiKey}.json"`），
`pnpm run fable` 重编后，**不带 `--poison`** 跑同一条闸门：

```
$ cd web && node scripts/verify-export.mjs --turns 12
模式：四家随机选手（不发任何请求）　先走 12 手
localStorage 里躺着一把假 key：sk-janpo-fake-key-NOT-A-REAL-KEY-jia-4f2a91
走完之后：上一手：座位 0 碰中（亮中 中）
Agent 状态：四家都是随机选手

下载文件名：janpo-sk-janpo-fake-key-NOT-A-REAL-KEY-jia-4f2a91.json　2065 字节
牌谱：版本 1　事件 21 条　决策记录 0 条（其中带 thinking 0 条、兜底 0 条）　已打完 0 局
回放：事件流逐条相同 = true　点数 25000 / 25000 / 25000 / 25000

导出验收没过：
导出物（文件名 + 字节）里出现了 API key：灌进 localStorage 的那把假 key
exit=1
```

这一次红的链路是**真的**：localStorage → `Store.readSeatConfig` → `model.Llm.ApiKey` → 导出物。
随后 `TablePage.fs` 已用备份原样还回去、重编、重跑，闸门恢复绿（`jj diff` 里只有两个文件）。

---

## 5. 其它可分享物：核过的路径清单

ADR-0002 说牌谱是**唯一**的可分享物，所以清单按「导出这条路给出去什么」+「还有什么会离开这台机器」列。

| # | 路径 | 结论 | 依据 |
|---|---|---|---|
| 1 | 牌谱字节：`version` / `ruleset` / `events` | 干净 | `Paifu.encoder` 里没有任何座位配置字段 |
| 2 | 牌谱里的选手名（`start_game.names`） | 干净 | `Roster.names`：随机选手是 `random`，LLM 座位是 `provider/model`，**没有 key** |
| 3 | **决策记录里的 `prompt`** | **干净**（去看了代码，不是假设） | `renderPrompt(decision, tier, note)` 的入参**只有决策包、脚手架档位、重试原因**；`prompt.ts` 一共就 import 了 `history.ts` / `types.ts` / `wording.ts`，**`SeatConfig` 根本进不来**。因此 prompt 里没有 provider、没有模型名、没有 baseUrl、没有 key。`PREAMBLE` 与三段渲染逐段看过，全是牌局事实 |
| 4 | 决策记录的 `tools` | 干净 | `toolsJson(actionIds)`：只有 `choose_action` 的 schema 与这一手的合法 id |
| 5 | 决策记录的 `thinking` / `reason` | 干净（结构上） | 都是模型自己的话，而模型看到的 prompt 里没有 key（第 3 条），它无从复述 |
| 6 | 决策记录的 `usage` / `attempts` / `latency_ms` / `applied` / `turn` / `seat` | 干净 | 全是数 |
| 7 | **导出文件名** | 干净，**且现在也进了断言** | `exportName`：种子解析得出来才拼进去（`janpo-paifu-<seed>.json`），否则常量兜底 |
| 8 | URL 分享 | 还不存在 | 全仓库没有写 `location` / `history.pushState` / hash 的代码；`Paifu.stripThinking` 有，但没有调用方（M2 的事） |
| 9 | 页面自身与截图 | 干净 | API key 那个输入框是 `password`（`TablePage.fs:970`），截图里是圆点；页面上没有第二处显示 key |
| 10 | 出网/上报 | 没有 | `web/index.html` 只有一条 `<script src="/src/main.ts">`，没有 CDN、没有 analytics、没有 `sendBeacon`；除了模型请求本身，页面不向任何地方发东西 |
| 11 | 仓库里提交的录制固件 `web/tests/fixtures/agent/*.json` | 干净 | 全目录扫过 `sk-[A-Za-z0-9_-]{8,}` 与 `api_?key`：唯一命中是 README 的散文。**但见下面 §5.1** |
| 12 | **决策记录的 `output.error_message` / `fallback` / `prompt` 里那句重试原因** | ⚠ **有条件的夹带通道**（本票没修，见 §5.1） | 见下 |

### 5.1 唯一一处真发现：**provider 的报错原文会原样进牌谱**（三个字段）

`piai.ts` 把 provider 的 `errorMessage` 原样交出来 → `loop.ts` 的 `judge` 拼成
`provider 报错：<原文>` → 这句话同时落到**三处**：`DecisionRecord.fallback`、
`output.error_message`（`rawOutput`），以及**下一次重试的 prompt 尾部**那句
「【上一次的回答没有被采用】…」——而牌谱存的正是最后一次的 prompt。

也就是说：**provider 的报错文本里有什么，牌谱里就有什么**。用一次性探针（不在仓库里）
喂一条「端点原样回显 key」的 401 走真实的 `decideWith`，三处全中：

```
夹带了  failure（→ 牌谱的 decisions[].fallback）
      provider 报错：401: {"error":"invalid api key: sk-probe-fake-key-ECHOED-BY-ENDPOINT-9911"}（重试 2 次仍无结果）
夹带了  output（→ 牌谱的 decisions[].output）
      {"stop_reason":"error","text":"","tool_call":null,"error_message":"401: {\"error\":\"invalid api key: sk-probe-fake-key-ECHOED-BY-ENDPOINT-9911\"}","usage":null}
夹带了  prompt（→ 牌谱的 decisions[].prompt，重试那句原因在尾部）
      【上一次的回答没有被采用】provider 报错：401: {"error":"invalid api key: sk-probe-fake-key-ECHOED-BY-ENDPOINT-9911"}
```

**现实里有多严重**：官方那几家实测是**打码**的。仓库里那份真录制
（`web/tests/fixtures/agent/ask-error-bad-key.json`，DeepSeek 的真 401）写着：

```
"errorMessage": "401: {\"message\":\"Authentication Fails, Your api key: ****ture is invalid\",…}"
```

——只回了末 4 位（`sk-invalid-key-for-fixture` 的 `ture`）。所以**今天的八家 provider 是安全的**，
而且这种打码形态**不会**被 `text.includes(apiKey)` 逮到（它只逮完整串）。
风险落在**自定义端点**（票 30）那条路：那是用户自建的网关 / 本地推理服务，
回显什么完全由它决定，原样回显 key 是完全可能的。

**本票没修**，理由是硬边界：修点在 `web/src/agent/`（票 31 正在改）或 `TablePage.settle`，
而这张票明确「只碰导出验收脚本与它的 CI 调用处」。已作为提案写进 `DECISIONS.md`，
连同修点建议与验收方式（见 §7）。

**另记一条非 key 的**：自定义端点报错时 `explainFailure` 会把 `seat.base_url` 拼进那句话，
于是**你的本地端点地址（可能是内网 IP）会进牌谱**。不是 key，但把牌谱发给别人时会带出去，
一并提案。

---

## 6. 验证

```
$ ./scripts/ci.sh                  # 全绿（dotnet 侧 + JS 侧九道）
$ cd web && pnpm run check         # Biome，Checked 46 files，无 fix
$ cd web && pnpm run typecheck     # tsc --noEmit，干净
```

导出那一道与反向自证那一道的原始输出见 §1 / §2 / §3。

**没跑的**：`--llm` 手验档（要真 key、要真请求）。对它的改动只有两处，都是**只增强不改变**：
① `shareable` 现在多含一个文件名（原来只查字节），② `--poison` 在这一档拌的是真 key（只在内存里，
不打印、不落盘）。原来那条 `apiKey !== null && …includes(apiKey)` 一字未动。

---

## 7. 留给人的待审项

1. **§5.1 那条通道要不要修**（`DECISIONS.md` 的提案 34-A）。建议修点是
   `TablePage.settle`——`DecisionRecord` **只在那一处组装**，而那里 `awaiting.Config.ApiKey` 在手，
   一处替换就盖住 `prompt` / `output` / `fallback` 三个字段；agent 层修则要改两三处。
   验收方式现成：让 `fake-endpoint.mjs` 回一条原样回显 key 的 401，
   `verify-export.mjs` 开一个「自定义端点 + 假 key + 真坐席」的档（**全程本机，不出网**），
   断言导出物里没有它——那会是这条通道的第一道真闸门。
2. **打码形态逮不住**（`****ture` 这种）。要不要把断言从「完整串」放宽到「key 的末 N 位」？
   建议**不要**：末 4 位的熵太低，会误伤（牌谱里出现 4 个字符的巧合概率不低），
   而且打码本来就是 provider 在替用户挡。记在这里是为了别有人以为闸门管这一档。
3. **`base_url` 进牌谱**（§5.1 末尾），提案 34-B。
</content>

---

## 8. 收尾 review（Standards 轴，fixed point `f59b71c9`）

标准来源：`AGENTS.md`、`docs/agents/fsharp-style.md`（本次没有 F# 改动）、`docs/agents/issue-tracker.md`、
`docs/agents/triage-labels.md`、`scripts/check-style.sh`（只查 F#）、ADR-0001/0002/0003、`CONTEXT.md`，
外加 Fowler 味道基线。工具能管的（Biome 格式与 lint、tsc）不重复看——`pnpm run check` 与
`pnpm run typecheck` 都干净。

**硬违反：0 条。**

判断题（各一条，已处理 / 已记录）：

1. **Mysterious Name（已修）**：那个「拿去做泄漏断言的东西」原来叫 `shared`，
   改成 `shareable`——ADR-0002 与 `CONTEXT.md` 的词是**可分享物**，变量名该贴着它。
2. **Duplicated Code（判断：保留）**：两条 `includes` 断言形状相同。合并成一条
   参数化的写法反而丢掉了票面点名要的那件事——**两条不是替代关系**，
   分开写才能一眼看出「CI 那条」与「手验那条」都在。注释里写明了。
3. **Speculative Generality（判断：不算）**：`--poison` 不是为将来准备的钩子，
   `ci-web.sh` 第九道每次 CI 都在用它。
4. `poisoned` 只依赖「导出物是一个 JSON 对象」，不依赖牌谱字段——这是**为票 31 让路**的选择
   （它正在改牌谱格式），不是过度抽象。
5. bash 那段：`trap 'rm -f "$poison_log"' EXIT` 是脚本里唯一的 trap（`grep -n trap scripts/*.sh` 核过），
   不会盖掉别人的。三种分支用桩命令试过（见 §2）。

留给人的判断题在 §7（提案 34-A / 34-B）。
