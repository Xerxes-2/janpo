# pi-ai 浏览器可用性 —— 实测结论

**调研日期**：2026-08-16
**结论**：**可用，不需要薄后端。** Agent 层按 spec 原计划走「浏览器直发 provider」。
**被测版本**：`@earendil-works/pi-ai` **0.84.2**（spec 里写的 `@mariozechner/pi-ai` 是旧包名，
包已迁到 `@earendil-works` scope，作者仍是 Mario Zechner）
**被测环境**：Chrome stable（headless）+ Vite 7.3.6 production build，页面 origin `http://localhost:5199`
**被测 provider**：DeepSeek（`deepseek-v4-flash`，OpenAI 兼容协议，`api-key` 直填）

---

## 0. 一句话

`createModels()` + `deepseekProvider()` 在浏览器里打包干净、跨域请求 200、单轮 tool call 拿到
合法 `action_id`、超时与坏 key 都是**值**而不是异常 —— M1 的 Agent 层四条路径全部有实测支撑。

## 1. 打包（Vite 7，production，`target: es2022`）

```
dist/assets/index-*.js                 26.65 kB │ gzip:  9.39 kB   ← 核心 + 我们的代码
dist/assets/openai-completions-*.js   170.88 kB │ gzip: 44.36 kB   ← provider SDK，懒加载 chunk
```

零 `node:` 外部化告警，零 polyfill。provider SDK 落在**独立的懒加载 chunk** 里（pi-ai 的
`lazyApi` 设计），首屏只付 9 KB gzip 的代价。按 provider 分入口导入
（`@earendil-works/pi-ai/providers/deepseek`）是拿到这个分块的前提，**不要**导入 `providers/all`
或 `/compat`。

## 2. CORS（这是真正的风险项，已排除）

浏览器实发的跨域请求，`page.on("requestfinished")` 抓到的：

```
POST https://api.deepseek.com/chat/completions -> 200
```

pi-ai 的 OpenAI/Anthropic 适配器**已经内建了浏览器直连所需的开关**（dist 里可见）：
`dangerouslyAllowBrowser: true`，以及 Anthropic 的 `anthropic-dangerous-direct-browser-access: true`
请求头。也就是说三家主力 provider 都是设计上支持浏览器直连的，我们不必自己拼请求。

**未实测**：Anthropic 与 OpenAI 的实际跨域（手上只有 DeepSeek key）。风险低（上述请求头/开关就是
两家官方给浏览器直连留的门），但第一次接上时仍要看一眼 Network 面板。
**已知会出问题的**：本地 Ollama / LM Studio 需要自己开 CORS 白名单，https 页面调 http 本地端点还有
mixed-content 拦截 —— spec 已经要求在文档里写明，这条实测未变。

## 3. 单轮 tool call（M1 决策边界的原型）

按 M1 的真实形态测：把合法动作集编号成 `StringEnum(["0","1","2"])` 塞进 `choose_action` 的参数 schema，
提示词是中文局面 + Assisted 档的向听/有效牌数值。

```ts
const chooseAction: Tool = {
  name: "choose_action",
  description: "从合法动作集中选择一个动作。只能选列出的 action_id。",
  parameters: Type.Object(
    {
      action_id: StringEnum(actionIds, { description: "所选动作的 id" }),
      reason: Type.String({ description: "一句话理由（中文）" }),
    },
    { additionalProperties: false },
  ),
  constrainedSampling: { type: "json_schema", strict: "prefer" },
};
```

结果：`stopReason=toolUse`，2446 ms，输入 469 tok / 输出 169 tok，

```json
{ "action_id": "2", "reason": "手牌已经听牌，而且有断幺九+平和+三色同顺的复合机会…立直是当前最优选择。" }
```

- **`StringEnum` 而不是 `Type.Enum`**：pi-ai README 明说 `Type.Enum` 生成的 `anyOf/const` Google 不吃。
- `constrainedSampling: { type: "json_schema", strict: "prefer" }`：支持的 provider 走服务端强制
  schema，不支持的自动退回普通 tool call —— 正好是「结构性抑制非法输出」那条决策要的语义。

## 4. 兜底闭环关心的三条路径（都不抛异常）

| 路径 | 怎么触发 | 结果 |
|---|---|---|
| **超时/中断** | `AbortController`，300 ms 后 abort | `stopReason: "aborted"`、`errorMessage: "Request aborted"`，**不抛** |
| **认证失败** | 故意填坏 key | `stopReason: "error"`、`errorMessage: "401: Authentication Fails…"`，**不抛** |
| **reasoning 档位** | `streamSimple(..., { reasoning: "medium" })` | `model.reasoning === true`，收到 **4062 个 `thinking_delta`**、1 个 `thinking` 块 |

前两条意味着 M1 的兜底逻辑可以写成对 `stopReason` 的 match（与引擎「错误是值不是异常」的风格一致），
不需要 try/catch 缠绕。第三条意味着 M2 的思考气泡有现成的流式数据源，`reasoning` 是
`'minimal'|'low'|'medium'|'high'|'xhigh'|'max'` 的座位级参数（spec Story 12 的「扩展思考」开关）。

## 5. 留给实现票的约束

1. 包名是 **`@earendil-works/pi-ai`**（spec 的 `@mariozechner/pi-ai` 已过时）。
2. 按 provider 分入口导入，别碰 `providers/all` 与 `/compat`，否则丢掉懒加载分块。
3. **OAuth 登录流程是 Node-only**（pi-ai README 明说）。因此浏览器里只能用 **API key**，
   Claude / ChatGPT 订阅制的 OAuth 登录在本项目里不可用 —— 配置面板不要给用户这个幻觉。
4. Amazon Bedrock 是 Node-only，模型列表里会出现但调用必失败；provider 列表要过滤掉它。
5. 环境变量在浏览器里不存在，key 一律显式传 `{ apiKey }`，或注入 localStorage 版
   `CredentialStore`（spec 要求 key 只落浏览器本地，两种都满足）。

## 6. 复现

spike 代码在 `/tmp/piai-browser-spike`（易失，关键片段已抄进本文）：
`pnpm add @earendil-works/pi-ai@0.84.2 vite@7 playwright-core` → `vite build` →
playwright-core 用 `/usr/bin/google-chrome-stable` 打开 `vite preview` 的页面 →
`page.evaluate(k => window.runSpike(k), key)`。key 由页面外注入，不进 bundle。
