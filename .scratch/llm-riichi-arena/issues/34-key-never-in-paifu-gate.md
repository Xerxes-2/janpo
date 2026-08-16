# 34 — 「牌谱里不含 key」这道闸门要在 CI 里真跑

**What to build:** README 对用户承诺「导出的牌谱里不含 key——有一道自动检查专门守着这件事」。
守卫确实存在（`web/scripts/verify-export.mjs` 末尾那条 `text.includes(apiKey)`），
但它只在**手验模式**（`--with-llm`，要真 key）下执行；CI 不调真实 API，那一行在 CI 里被跳过。
**对外的安全声称不能只有一半为真。** 让这道闸门在 CI 里真跑。

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

## 怎么做（便宜且不发任何请求）

往 localStorage 灌一个**假 key**，但**不给模型坐席**（四家仍是随机选手）——于是一个请求都不会发出去，
照样能断言导出的字节里不含那把假 key。

- [ ] CI 的导出验收里加这一档：假 key 进 localStorage、无模型坐席、走若干手、导出、断言字节里没有它
- [ ] **反向自证**：故意把 key 写进导出物（临时改代码或造一份含 key 的牌谱）确认闸门真的会红。
      一道从不失败的闸门等于没有闸门——报告里要给出这次红的记录
- [ ] 手验模式（真 key）那条既有断言**保留**，两条不是替代关系
- [ ] 顺带核一遍：**其它可分享物**里也不该出现 key（页面导出的任何东西、URL、错误信息、
      牌谱的决策记录里那段 prompt——prompt 里会不会夹带 provider 配置？去看，别假设）

## 边界

- [ ] 改动只碰导出验收脚本与它的 CI 调用处。**票 31 正在改牌谱格式**（决策记录只存 prompt 尾部、
      版本号可能涨），你的改动要**尽量小且集中**，方便调度器 rebase
- [ ] 不碰引擎、`web/src/agent/`、`web/src/styles.css`、README 正文
- [ ] 假 key 用明显是假的字面量（例如带「假」字或 `-fake-`），**绝不能**从 `/tmp/deepseek_key` 读
