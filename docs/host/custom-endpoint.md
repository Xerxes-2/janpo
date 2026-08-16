# 接本地模型与自建网关（自定义端点）

**给谁看**：主持人（Host）—— 想让某个座位跑本地 Ollama / LM Studio / llama.cpp，
或跑公司内网的 OpenAI 兼容网关的人。

**一句话**：配置面板的 provider 选「自定义端点（OpenAI 兼容）」，填一个 **baseUrl**，
其余照旧。本地端点通常不用填 key。**唯一需要你动手的地方是端点那一侧的 CORS 放行**。

---

## 1. 三步

1. 起你的端点，确认它的 OpenAI 兼容地址（**填到含 `/v1` 那一层**）：

   | 端点 | baseUrl | 模型名怎么查 |
   |---|---|---|
   | Ollama | `http://localhost:11434/v1` | `ollama list`（例 `qwen3:8b`） |
   | LM Studio | `http://localhost:1234/v1` | 界面里那个模型 id，或 `lms ls` |
   | llama.cpp `llama-server` | `http://localhost:8080/v1` | 随便填，它只有一个模型 |
   | vLLM | `http://localhost:8000/v1` | 起服务时 `--served-model-name` 的值 |
   | 自建网关 | 网关给你的那个前缀，如 `https://gw.example.com/openai/v1` | 网关自己的模型目录 |

2. 牌桌页面的配置面板：**provider** 选「自定义端点（OpenAI 兼容）」→ 面板上会多出一格
   **baseUrl** → 填上表那个地址 → **模型**填端点里的实名（自由文本，没有下拉框）→
   **API key** 本地端点留空即可（网关要鉴权就填它给的 key）。

3. 按 2 节把端点的 **CORS 放行**配好。没配好会怎样：牌桌照样打得完（兜底代打），
   但页面上会一直红着说「连不上自定义端点 …」。

配置只落在这台浏览器的 localStorage（键名 `janpo.llm.base_url` 等），不经任何后端。

## 2. CORS：**这一步跳不过去**

页面在浏览器里直接向你的端点发请求，属于跨域；浏览器会先发一个 **preflight（OPTIONS）**，
端点不放行就一个字节都发不出去。

### 2.1 Ollama

Ollama 的默认放行名单**已经包含 `http://localhost:*` 与 `https://localhost:*`
（以及 `127.0.0.1:*`、`0.0.0.0:*`）**。所以：

- 页面开在 `http://localhost:5173`（`pnpm run dev`）或 `http://localhost:4173`（`vite preview`）
  —— **什么都不用配**。
- 页面开在别的 origin（部署到某个域名、或用局域网 IP 访问）—— 设 `OLLAMA_ORIGINS`，
  它是**逗号分隔的 origin 列表**（支持 `*` 通配）：

  ```sh
  # 前台跑
  OLLAMA_ORIGINS=https://janpo.example.com ollama serve

  # systemd（Linux 上装成服务时）
  sudo systemctl edit ollama.service
  #   [Service]
  #   Environment="OLLAMA_ORIGINS=https://janpo.example.com"
  sudo systemctl restart ollama

  # macOS 的 app 版
  launchctl setenv OLLAMA_ORIGINS "https://janpo.example.com"   # 之后重启 Ollama
  ```

  **写 origin，不是写 URL**：`https://janpo.example.com`（协议 + 主机 + 端口），
  后面不带路径、不带斜杠。放行面越窄越好，别图省事写 `*` —— 那等于让你打开的任何网页
  都能驱使你本机的模型。

**请求头不用你管**：Ollama 的放行名单里已经列了 OpenAI SDK 要带的
`Authorization`、`Content-Type` 与一批 `x-stainless-*`。老版本 Ollama 没有这一段，
症状是浏览器报「Request header field x-stainless-os is not allowed by
Access-Control-Allow-Headers」—— 那要升级 Ollama，改 `OLLAMA_ORIGINS` 没用。

### 2.2 LM Studio

**CORS 默认是关的**，开法二选一：

- 界面：Developer → Server Settings → 打开 **Enable CORS**（顺手确认端口是 1234）
- 命令行：`lms server start --cors`

### 2.3 llama.cpp / vLLM / 自建网关

`llama-server` 与 vLLM 默认都放行任意 origin（`Access-Control-Allow-Origin: *`），
通常开箱即用。自建网关（nginx / Traefik / 一段自己写的转发）要放行这些：

```
Access-Control-Allow-Origin: <你的页面 origin>
Access-Control-Allow-Methods: POST, OPTIONS
Access-Control-Allow-Headers: authorization, content-type,
  x-stainless-arch, x-stainless-lang, x-stainless-os, x-stainless-package-version,
  x-stainless-retry-count, x-stainless-runtime, x-stainless-runtime-version
```

最后那一串是 **OpenAI 官方 SDK 自动带上的**（本项目实测过一次预检请求，上面就是它要放行的全部）。
`Access-Control-Allow-Headers` **不支持 `x-stainless-*` 这种通配**，要么逐个列出，
要么把预检请求里的 `Access-Control-Request-Headers` 原样回回去。
另外 OPTIONS 要回 2xx（`204` 最省事），别回 405。

## 3. https 页面调 http 端点：拦不拦，看页面在哪儿

一句话结论：**页面开在 localhost 上就不会被拦；页面开在公网域名上，
浏览器会要一次「本地网络访问」授权。** 这与常说的 mixed content 不完全是一回事 ——
Chrome 不把 `http://localhost` / `http://127.0.0.1` 当作不安全内容。

本项目实测（Chrome 151，2026-08-16；复现命令见第 5 节）：

| 页面 | 端点 | 结果 |
|---|---|---|
| `http://localhost:4183` | `http://127.0.0.1:4199/v1` | ✅ 通 |
| `https://localhost:4183` | `http://127.0.0.1:4199/v1` | ✅ 通（loopback 不算 mixed content） |
| `https://localhost:4183` | `https://127.0.0.1:4199/v1`（自签证书） | ✅ 通（证书要浏览器信，见下） |
| `https://localhost:4183` | `http://192.168.x.x:4199/v1`（局域网另一台） | ✅ 通 |
| **`https://<公网域名>`** | `http://127.0.0.1:4199/v1` | ❌ 拦：`Permission was denied for this request to access the` `loopback` `address space`，授权之后就通 |

也就是说：

- **本机跑、页面开在 localhost —— 什么都不用管。** 这是推荐的用法。
- **页面部署在公网 https 上、模型在你本机** —— Chrome 会按「本地网络访问」规则拦一道；
  真人用浏览器时它会弹一个授权框（允许本站访问你本地网络里的设备），点允许就通。
  无头浏览器没有人点，因此一律被拒。
  不想依赖这个授权，就把页面也开在本机（`pnpm run build` 之后随便一个静态服务器 + localhost）。
- **端点上 https 是另一条路，但要浏览器信得过那张证书**：实测自签证书直接死在 TLS 那一步
  （`net::ERR_CERT_AUTHORITY_INVALID` → `TypeError: Failed to fetch`，页面上同样是「连不上」）。
  除非你已经有内网 CA，否则**不值得为本地模型折腾证书**。
- 端点在**局域网另一台机器**上时，那台机器的端点要用 `OLLAMA_HOST=0.0.0.0` 之类的方式对外监听，
  并把你的页面 origin 加进它的放行名单。

## 4. 接不上时页面会说什么

失败一律是**一条读得懂的中文**，落在牌桌的「模型坐席」那一行；对局不会卡住
（那一手由引擎兜底代打，页面上会记「兜底：…」）。

| 你看到的 | 多半是什么 | 怎么办 |
|---|---|---|
| `自定义端点没有填 baseUrl（例：http://localhost:11434/v1）` | 那一格是空的 | 填上 |
| `自定义端点的 baseUrl 读不懂：「[端点地址已打码]」` | 少了 `http://` | 补上协议（你填的原文不会回显，看配置面板那一格） |
| `连不上自定义端点 [端点地址已打码]：浏览器没能把请求发出去` | 端点没起 / 地址写错 / **CORS 没放行** / 公网页面没拿到本地网络授权 | 按 2、3 两节查；`curl` 一下端点确认它活着 |
| `自定义端点 [端点地址已打码] 回了 404：baseUrl 多半漏了 /v1` | baseUrl 少了路径前缀 | 补 `/v1` |
| `provider 报错：401 …` / `model not found …` | 端点自己的话（鉴权、模型名） | 照端点的报错改 key 或模型名 |
| `模型超时（… ms 没答完）` | 本地模型太慢 | 把配置面板的「超时 (ms)」调大；本地大模型首次加载几十秒很常见 |

### 4.1 为什么报错里看不到你的地址与 key（票 36）

这些句子不只出现在页面上，它们会**进牌谱**（`fallback`、`output.error_message`，
以及下一次重试的 prompt），而牌谱是拿去分享的。因此在它们变成留存物之前，
**坐位配置里的 API key 与 baseUrl 一律被换成 `[API key 已打码]` / `[端点地址已打码]`**。

- **key**：官方那八家的报错本来就打码（DeepSeek 只回末 4 位），
  但**你自建的网关回显什么是你自己写的** —— 原样把 `Authorization` 抄回来的网关完全存在。
- **baseUrl**：它不是密钥，但往往是 `192.168.x.x` 这种内网地址；把牌谱发给别人时没必要一并送出去。

你自己要查的时候不依赖这句话：地址就在配置面板那一格里。
这道闸门每次 CI 都真跑一遍（`web/scripts/verify-redaction.mjs`：
一个会原样回显 key 的本机假端点，真开一桌、真导出牌谱，再回头查那份字节）。

自己先验一遍端点活不活（不经浏览器，因此**不受 CORS 影响**）：

```sh
curl http://localhost:11434/v1/chat/completions \
  -H 'content-type: application/json' \
  -d '{"model":"qwen3:8b","messages":[{"role":"user","content":"你好"}],"stream":false}'
```

这条通、页面上却「连不上」，那就是 CORS 或本地网络授权的问题，不是端点的问题。

## 5. 想自己复现第 3 节那张表

仓库里带了一个**最小的 OpenAI 兼容假端点**（几十行，固定回一条 tool call），
以及一个把两个坑各走一遍的验收脚本。都在 `web/` 下跑，**都不进 CI**：

```sh
cd web && pnpm run build            # 先出产物，脚本跑的是打包后的页面

node scripts/verify-custom-endpoint.mjs --mode blocked   # 端点不放行 CORS：页面上红着说连不上
node scripts/verify-custom-endpoint.mjs --mode allowed   # 放行之后：模型座位真的答上话
node scripts/verify-custom-endpoint.mjs --mode mixed     # https 页面 + http 端点
node scripts/verify-custom-endpoint.mjs --mode https     # https 页面 + https 端点
node scripts/verify-custom-endpoint.mjs --mode lan --endpoint-host 192.168.1.5
node scripts/verify-custom-endpoint.mjs --mode lna       # 公网 https 页面 → 本地端点（要外网）

# 只想手工点：起假端点，然后自己开页面填 http://127.0.0.1:4199/v1
node scripts/fake-endpoint.mjs --cors http://localhost:4173
```

假端点的用处是**把模型这个变量摘掉**：它固定选第一条合法动作，因此「模型答上话了」
等价于「请求真的通到端点又回来了」。

## 6. 几条提醒

- **key 只存在这台浏览器**（localStorage），不经本平台——本平台没有后端。自定义端点也一样。
  导出的牌谱里也没有它：就算你的网关把 key 原样回显在报错里，那句话也是打码后才落进牌谱（见 4.1）。
- **放行 origin 越窄越好。** `OLLAMA_ORIGINS=*` 意味着你访问的任何网页都能调你本机的模型。
- **别把 baseUrl 指向公网上的明文 http 网关**：那条链路上 key 与整局 prompt 都是明文，
  而且公网 http 目标会被浏览器按 mixed content 拦掉。
- **本地模型慢是常态**：超时默认 30 秒，本地 7B 起步的模型第一次加载可能就要这么久。
  连着兜底几手就把超时调大，或先用 `curl` 把模型预热一遍。
- 采样参数（temperature 一类）本版**不提供**：对照实验的自由变量越多，结论越难归因。
