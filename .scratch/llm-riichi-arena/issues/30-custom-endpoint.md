# 30 — 自定义端点：接本地模型与自建兼容网关

**What to build:** 让主持人能填一个 **baseUrl**，把座位接到 OpenAI 兼容的任意端点上 ——
本地 Ollama、LM Studio、自建网关、公司内网代理。现在 provider 表里只有八家官方工厂，
想跑本地模型完全无路。

**Blocked by:** None — can start immediately.（纯 web 票，与 29a 的引擎改造零重叠）

**Status:** ready-for-human

## 一、配置与接线

- [x] provider 表多一项「OpenAI 兼容」，配一个 **baseUrl 字段**（座位级，进 `LlmField`，
      与其它字段一样只落 localStorage）
- [x] 模型名仍是自由文本（本地模型的名字五花八门，下拉框只会挡路）
- [x] baseUrl 空着或填错时，失败要变成**一条能读懂的错误**，与既有的兜底同一条路
      （`stopReason` 上的值，不抛异常），页面上说得清是「端点连不上」而不是「模型不肯选」
- [x] 选了官方那八家时行为**一个字节都不变**（新路径不许污染既有路径）

## 二、真验一次（这一票的重点其实在这）

spec 早就点名了这两个坑，别只写代码不验：

- [x] **CORS**：本地端点默认不许浏览器跨域。实测一次 Ollama 或 LM Studio，
      把「主持人需要怎么配」写成文档里的一段（环境变量名、要放行哪个 origin）
- [x] **mixed content**：https 页面调 http 本地端点会被浏览器拦。实测并写清对策
      （本地开发用 http 页面 / 端点上 https / 浏览器例外），别让用户自己撞
      —— **实测推翻了票面这条假设**（Chrome 151）：https 页面调 `http://127.0.0.1` 根本不算
      mixed content，真正拦人的是「本地网络访问」权限，而且只在**页面自己不在本地地址空间**时拦；
      给端点上自签 https 反而更早死（`ERR_CERT_AUTHORITY_INVALID`）。
      五种组合的原始输出与对策见 `run/reports/30-custom-endpoint.md` 第 3 节与
      `docs/host/custom-endpoint.md` 第 3 节。
- [x] 上述结论写进面向主持人的文档，不是只写在 DECISIONS 里
- [x] 若本机没有可跑的本地模型服务，用一个**最小的 OpenAI 兼容假端点**（几十行的 HTTP 服务）
      验 CORS 与 mixed content 两条路径 —— 目的是验通道，不是验模型质量

## 三、边界

- [x] **不碰 prompt**（29b 的活）、**不碰引擎**（29a 的活）
- [x] 采样参数（temperature / top_p / max_tokens）**不做**：主人裁定留给 M2，
      理由是「对照实验的自由变量越多，结论越难归因」
- [x] CI 里不许真连任何端点；假端点只在本地手验或作为可选的集成脚本
