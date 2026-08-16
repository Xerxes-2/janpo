# 30 — 自定义端点：接本地模型与自建兼容网关

**状态**：done　**change**：`nrkwlpuo`（`f5d382a0`）　**fixed point**：`9d4df416`（`qozkmuso`）　**工作区**：ws-b

配置面板多了一项「自定义端点（OpenAI 兼容）」与一格 baseUrl，一个座位因此能接本地 Ollama /
LM Studio / llama.cpp / 自建网关。**这一票的重量在实测**：CORS 与 mixed content 两条路径
各在真浏览器里走了一遍，其中「https 页面调 http 本地端点会被拦」这条**流传已久的说法被推翻了**
（第 3 节）。主持人文档 `docs/host/custom-endpoint.md` 里的每一格都来自下面这些跑。

---

## 1. 改了哪几处（基本都是「加一项」）

| 层 | 文件 | 做了什么 |
|---|---|---|
| 边界 | `src/Janpo.Web/Agent.fs` | `LlmSeat.BaseUrl` 字段、`LlmField.BaseUrl`（键名 `base_url`）、`LlmSeat.customProvider` / `isCustom` / `providerDisplay`、encoder 多一个字段 |
| 页面 | `src/Janpo.Web/TablePage.fs` | provider 下拉多一项；**baseUrl 那一格只在选了自定义端点时出现**；同条件多一段说明 |
| Agent 层 | `web/src/agent/endpoint.ts`（新） | 判读与话术：`isCustom` / `readBaseUrl` / `missingConfig` / `keyFor` / `customModel` / `explainFailure`。**纯的，不 import pi-ai** |
| Agent 层 | `web/src/agent/piai.ts` | `customProvider(baseUrl)`（`createProvider` + `openAICompletionsApi`）与 `wire()`：官方八家查目录，自定义端点当场造模型 |
| Agent 层 | `web/src/agent/loop.ts` | 那句「发不发得出去」的短路改成 `missingConfig(seat)`（**重试逻辑一行没动**） |
| Agent 层 | `web/src/agent/types.ts` | `SeatConfig.base_url` |
| 手验 | `web/scripts/fake-endpoint.mjs`（新） | 最小的 OpenAI 兼容假端点：固定回一条 tool call，CORS / https 可开可关 |
| 手验 | `web/scripts/verify-custom-endpoint.mjs`（新） | 六种模式，真浏览器真端点 |
| 文档 | `docs/host/custom-endpoint.md`（新） | **面向主持人**：怎么填、怎么放行、拦不拦、报错怎么读 |

`Store.fs` 一行没改——localStorage 遍历 `LlmField.all`，加字段就自动落盘（票 23 的接缝）。
`prompt.ts`、引擎、`loop.ts` 的重试与判读全部未动。

测试：node `endpoint.test.ts` 10 条 + `loop.test.ts` 新增 2 条（共 51 条）；
dotnet `AgentTests` 新增 4 条（Web 侧共 70 条）。**每一组都有一条「官方那侧原样」的断言**：
`missingConfig` 的文案、`explainFailure` 的透传、`isCustom` 对填了 baseUrl 的官方座位仍为 false。

## 2. CORS：实测记录

跑法：`node scripts/verify-custom-endpoint.mjs --mode blocked|allowed`。
页面是 `vite preview` 的产物，端点是假端点，座位 1 交给自定义端点、**没有 key**。

### 2.1 不放行（本地端点的出厂状态）

```
模式 blocked：页面 http://localhost:4183　端点 http://127.0.0.1:4199/v1　CORS 不放行
配置面板：provider=custom　baseUrl 输入框 在

Agent 状态（data-agent=troubled，已兜底 1 手）：
  座位 1 兜底代打：provider 报错：连不上自定义端点 http://127.0.0.1:4199/v1：浏览器没能把请求发出去。
  常见原因：端点没起、地址写错、端点没放行本页面的跨域请求（CORS），或者本页面是 https 而端点是 http
  （mixed content 被拦）。配法见 docs/host/custom-endpoint.md。原始报错：Connection error.（重试 2 次仍无结果）

端点收到的请求：OPTIONS /v1/chat/completions  origin=http://localhost:4183   ← 三次预检，POST 一次没发出去
浏览器看到的响应：（一条都没有）
请求失败（requestfailed）：POST http://127.0.0.1:4199/v1/chat/completions → net::ERR_FAILED  ×3
console.error：
  Access to fetch at 'http://127.0.0.1:4199/v1/chat/completions' from origin 'http://localhost:4183'
  has been blocked by CORS policy: Response to preflight request doesn't pass access control check:
  No 'Access-Control-Allow-Origin' header is present on the requested resource.  ×3
```

**这就是票面要的那条**：浏览器控制台里是英文的 CORS 报错，页面上是一句中文的
「连不上自定义端点 …」，并且明说了「不是模型不肯选」——`data-agent=troubled`、这一手兜底摸切，
对局照打。

### 2.2 放行之后

```
模式 allowed：页面 http://localhost:4183　端点 http://127.0.0.1:4199/v1　CORS 放行 http://localhost:4183
Agent 状态（data-agent=spoke，已兜底 0 手）：
  座位 1 的模型选完了（21 ms）：假端点固定选第一条，只为验通道
上一手：座位 1 手切2万

端点收到的请求：OPTIONS /v1/chat/completions | POST /v1/chat/completions
浏览器看到的响应：POST http://127.0.0.1:4199/v1/chat/completions → 200
请求失败：无　console.error：无
```

### 2.3 一个只有真跑才会发现的坑：`x-stainless-*`

第一版假端点的 `Access-Control-Allow-Headers` 写死成
`authorization, content-type, x-stainless-*`，结果**放行了照样被拦**：

```
Access to fetch ... has been blocked by CORS policy: Request header field x-stainless-os
is not allowed by Access-Control-Allow-Headers in preflight response.
```

`Access-Control-Allow-Headers` **不支持通配符**。预检真正要放行的是（抄自端点侧日志）：

```
authorization, content-type,
x-stainless-arch, x-stainless-lang, x-stainless-os, x-stainless-package-version,
x-stainless-retry-count, x-stainless-runtime, x-stainless-runtime-version
```

那批是 OpenAI 官方 SDK 自己加的。对策与取舍见 DECISIONS 30-5：**我们不抹这些头**，
而是把清单写进主持人文档。已核对过两家最可能被接的端点：

- **Ollama** 的 `server/routes.go` 的 `corsConfig.AllowHeaders` 里逐条列了 `x-stainless-*`
  （还有 `OpenAI-Beta`），因此开箱即过；老版本没有这一段，症状就是上面那句话，**改 `OLLAMA_ORIGINS` 没用，得升级**。
- **Ollama 的默认 origin 名单**（`envconfig.AllowedOrigins`）已经包含
  `http://localhost:*` / `https://localhost:*` / `127.0.0.1:*` / `0.0.0.0:*`，
  所以页面开在 localhost 上时**连 `OLLAMA_ORIGINS` 都不用设**；换成别的 origin 才要设。
- **LM Studio** 的 CORS 默认关，Developer → Server Settings → Enable CORS，或 `lms server start --cors`。

## 3. mixed content：实测把老说法推翻了

同一个脚本的另外四种模式（`--mode mixed|https|lan|lna`），Chrome **151.0.7922.137**，2026-08-16：

| # | 页面 | 端点 | 结果 |
|---|---|---|---|
| 1 | `http://localhost:4183` | `http://127.0.0.1:4199/v1` | **通**（spoke，21 ms） |
| 2 | `https://localhost:4183` | `http://127.0.0.1:4199/v1` | **通**（spoke，23 ms）—— 没有任何 mixed content 拦截 |
| 3 | `https://localhost:4183` | `https://127.0.0.1:4199/v1`（自签，浏览器被要求忽略证书错误） | **通**（spoke，22 ms） |
| 4 | `https://localhost:4183` | `http://192.168.68.100:4199/v1`（非回环） | **通**（spoke，22 ms） |
| 5 | `https://example.com` | `http://127.0.0.1:4199/v1` | **被拦** |

第 5 行的原文（这一跑不加载我们的页面，只在第三方 https 页面里裸 `fetch` 一次）：

```
https://example.com → http://127.0.0.1:4199/v1/chat/completions　本地网络访问权限：未授权
  结果：被拦：TypeError: Failed to fetch
  [error] Access to fetch at 'http://127.0.0.1:4199/v1/chat/completions' from origin 'https://example.com'
          has been blocked by CORS policy: Permission was denied for this request to access
          the `loopback` address space.
https://example.com → http://127.0.0.1:4199/v1/chat/completions　本地网络访问权限：已授权
  结果：通了（HTTP 200）
```

结论三条（写进了主持人文档的表）：

1. **拦人的不是 mixed content，是 Chrome 的「本地网络访问」权限。**
   Chrome 不把 `http://localhost` / `http://127.0.0.1` 当作不安全内容（这一点 Chrome 官方博客
   也是这么说的），所以第 2、4 行才会通。
2. **只有页面自己不在本地地址空间时才会被拦。** 我一开始猜「页面开在 `https://192.168.x.x`
   上就能复现」，实测**没拦**（那台页面本身就在 local 地址空间里）——所以脚本的 `lna` 模式
   最终用的是一个公网 https 页面。真人用的浏览器会弹一个授权框，headless 没人点，一律拒。
3. **对策不是给端点上 https。** 自签证书实测死在更前面：
   `net::ERR_CERT_AUTHORITY_INVALID` → `TypeError: Failed to fetch`（页面上同样是「连不上」）。
   推荐的是**页面开在 localhost**；部署在公网 https 上时，就靠那次「允许访问本地网络」的授权。

顺带把 baseUrl 写错那条也在浏览器里走了一遍（`--mode allowed --base-url http://127.0.0.1:4199`，
少了 `/v1`）：

```
上一手：座位 1 摸切3万（兜底：provider 报错：自定义端点 http://127.0.0.1:4199 回了 404：
baseUrl 多半漏了 /v1 之类的路径前缀。原始报错：404: {"message":"没有这个路径：/chat/completions",...}）
```

## 4. 官方八家没被碰到（这条要有证据）

- **静态**：`isCustom` 只看 provider；`missingConfig` / `keyFor` / `explainFailure` 三个出口
  都以它开门，官方那侧各有一条用例钉住原样（含「没有填 deepseek 的 API key」这句原文）。
- **动态**：`node scripts/verify-llm-seat.mjs --bad-key` 复跑（票 23 的断电演习），
  一局 18.4 s、20 手全兜底、60 次请求全 401，页面上那句仍是
  `provider 报错：401: {"message":"Authentication Fails, Your api key: ****-key is invalid",...}`
  ——**没有被自定义端点那套话术改写一个字**。
- 打包也没变形：`dist/assets/index-*.js` 370.85 kB / gzip 118.57 kB，provider chunk 仍各自懒加载
  （自定义端点用的 `openai-completions` chunk 本来就在，因为 deepseek 就用它）。

## 5. 关键取舍

全文见 `DECISIONS.md` 的「## 30」段（8 条）。三条最要紧的：

1. **自定义端点是 provider 表里多出来的一项，不是给每一家加 baseUrl 覆盖**（30-1）。
   后者会把新路径的判读接进既有路径，正是票面禁止的。
2. **判读与话术分出 `endpoint.ts`（纯的）**（30-2）：`loop.ts` 问得起「这一次发不发得出去」，
   而不必为此拖进 provider SDK；边界情况用例几十毫秒跑完。
3. **本地端点空 key 照发**，内部替换成占位串（30-3）。实测 pi-ai 空 key 直接抛
   `No API key for provider`，而 Ollama 根本不看这个头。

## 6. 留给人的待审项

- **`x-stainless-*` 没抹**（30-5）。若日后接到某个死板的自建网关放行不了这批头，
  再考虑用 pi-ai 的 `headers: { ...: null }` 逐个抹掉——那时要接受「名单随 SDK 版本漂」。
- **本地网络访问授权只在 headless 上验过被拒与手工授权两态**，真人浏览器里那个授权框长什么样
  没截图（这台机器没有有头浏览器）。文档里如实写了「真人用浏览器时会弹一个授权框」。
- **超时默认 30 秒对本地模型可能偏短**（7B 首次加载常常更久）。本票没改默认值，
  只在文档里写了「连着兜底就把超时调大」。真要改是配桌页（M2）的事。
- **`custom` 这个 id 与 pi-ai 未来某一天的 provider id 撞车的风险**：现在没有同名的一家，
  真撞了编译不会红，只会让那一家进不来。要更保险可以改成 `custom-openai`，本票没做。

## 7. code-review 结论

两轴各跑一遍（fixed point `9d4df416`），本工作区派不出 sub-agent，因此顺序自跑。

- **Standards**（README 的「加新模块时的约定」+ `docs/agents/fsharp-style.md` + ADR-0001/0005 +
  Fowler 味道基线）：**2 条已修，1 条判断题已修**。
  1. *违反文档化的命名约定*：README 明写「所有产出人类可读中文的函数一律叫 `toDisplay`」，
     而新加的显示名函数叫 `providerDisplay`（仓库里其余 15 处全是 `toDisplay`）。
     → 改名 `LlmSeat.providerToDisplay`，并在注释里写清前缀的理由（它渲的不是 `LlmSeat`，
     而是里面那个至今仍是 `string` 的 provider id）。
  2. *同一份改动里两种错误形状*（判断题，但改了）：`readBaseUrl` 回
     `{ ok, why }`，而 `wire` 起初回 `Model | string`，调用点靠 `typeof === "string"` 分辨。
     → `wire` 改回具名的 `Wired = { ok: true; model } | { ok: false; why }`，两处同形。
  3. *测试里第二个 `"custom"` 字面量*：`fixtures.ts` 的 `localSeat` 手写了 provider id。
     → 改成 import `CUSTOM_PROVIDER`。产品代码两侧本来就各只有一处常量，且互相指名。
  修完重跑：node 51 条、dotnet Web 侧 70 条、`check-style.sh` 与 `fantomas --check` 干净，
  并重新 build + 跑了一遍浏览器验收（`--mode allowed` 仍是 spoke）。
- **Spec**（票里的 12 个验收框 + `DECISIONS.md` 第五次裁决第 3 条 + ADR-0005 的四条边界）：无 blocking。
  12 条逐条对上（见票文件的勾选框）；ADR-0005 四条边界守住：跨界仍只传字符串、
  TS 仍不构造 `Action`、没给 Fable 输出写 `.d.ts`、Fable 依赖白名单未动（`ci.sh` 那道闸门照跑）。
  没有发现 scope creep：新增的 `providerToDisplay` 是下拉框要一句中文说清「自定义端点」是什么；
  `--mode lna` 那一跑要外网，但它是**手验脚本**且 CI 不调用。

nitpick（只记录，未改）：

- `TablePage.llmPanel` 已经是「一堆字段 + 两段说明文字」，`if isCustom then` 让它更长了；
  M2 的配桌页会把整块吃掉，现在拆组件是白拆（票 23 的报告里也记过这条）。
- `verify-custom-endpoint.mjs` 与 `verify-llm-seat.mjs` 又各起了一遍
  `vite preview` + playwright + 收 console 错误——票 23 记过的 `withPage()` 样板仍然没抽。
- `customModel` 里的 `contextWindow` / `maxTokens` 是拍的（32768 / 8192）：
  本地端点没有目录可查，而这两个值 pi-ai 只在计价与截断上用，对我们无影响。
- 自定义端点座位把**模型名留空**时，报的是端点自己的话（Ollama 会回 400）。
  加一条「没填模型名」的短路只要一行，但那对官方八家同样成立，属于另一票的活。
- CORS / mixed content 那段话在三处各有一份（面板提示、`explainFailure` 的错误文本、主持人文档），
  措辞可能漂。真要收敛就让面板与错误文本都只指文档。
