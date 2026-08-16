# 26 — DecisionRecord 与 Paifu JSON 导出

**What to build:** 让每一手 LLM 决策的完整审计数据留下来，并让一场对局能导出成一个 JSON 文件。
Paifu = 事件流 + 决策记录列表 + 规则集；它是本项目**唯一的可分享物**（ADR-0002），M2 的思考气泡、
URL 分享、导入回放全都建在这一票产出的结构上，所以结构比功能重要。

**Blocked by:** 23

**Status:** ready-for-agent

- [ ] `DecisionRecord` 记全：第几手、哪个座位、完整 prompt、工具定义、模型原始输出、thinking、
      延迟、重试次数、最终采用的动作、**是否兜底**
- [ ] 随机 Player 的手不必产生记录（没有可审计的推理），但牌谱的手序编号不能因此断裂
- [ ] Paifu 有版本号字段；编解码往返测试（encode → decode → 逐字段相同）
- [ ] 浏览器里一键把当前对局导出成 JSON 文件下载
- [ ] 导出的事件流能被引擎重新 fold 出同一个终局（回放不是另一套代码路径，就是引擎本身）
- [ ] thinking 是**可省略**的一段：URL 分享（M2）省掉它，JSON 全量带着，两条路径共用同一解码器

**倾向（可改，改了记一条）**：Paifu 的类型与编解码放 F# 侧，DecisionRecord 从 TS 过来时按 schema
decode 成 F# 类型。理由是 M2 的思考气泡在 Feliz 侧读它，往返测试与回放也都在 F# 一侧，
两处共用一份定义比双份对齐便宜。
