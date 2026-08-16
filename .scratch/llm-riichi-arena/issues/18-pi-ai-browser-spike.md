# 18 — pi-ai 浏览器可用性 spike

**What to build:** 在真实浏览器里验证 Agent 层能不能「用户自带 key、请求直发 provider」，也就是 spec
点名的**M1 前唯一技术前置作业**。结论只有两种：可用（Agent 层照原计划走），或不可用（需要一个薄后端
转发，进而影响 23 号票的写法与部署形态）。

**Blocked by:** None — can start immediately.

**Status:** ready-for-human

- [x] pi-ai 在浏览器 production bundle 里编得出、跑得起（无 `node:` 外部化、无 polyfill）
- [x] 从页面 origin 实发跨域请求成功（CORS 不拦）
- [x] 单轮 tool call 走通：合法动作集编号成 enum 注入参数 schema，模型回一个合法 id
- [x] 兜底闭环关心的三条路径逐条实测：超时中断、认证失败、reasoning 档位
- [x] 结论与全部约束写成报告

**结论：可用，不需要薄后端。** 全部实测数字、复现步骤与留给实现票的五条约束见
`docs/research/pi-ai-browser-usability.md`。三条会直接改写后续票的：

1. 包名是 `@earendil-works/pi-ai`（spec 里的 `@mariozechner/pi-ai` 是旧 scope，已过时）
2. **OAuth 登录是 Node-only** —— 浏览器里只能用 API key，订阅制登录不可用，配置面板不要给这个幻觉
3. 超时与错误都是**值**（`stopReason: "aborted" / "error"`）而不是异常，兜底写成对 `stopReason` 的
   match 即可，与引擎「错误是值不是异常」同风格
