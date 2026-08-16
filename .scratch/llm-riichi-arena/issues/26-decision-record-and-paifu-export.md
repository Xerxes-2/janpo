# 26 — DecisionRecord 与 Paifu JSON 导出

**What to build:** 让每一手 LLM 决策的完整审计数据留下来，并让一场对局能导出成一个 JSON 文件。
Paifu = 事件流 + 决策记录列表 + 规则集；它是本项目**唯一的可分享物**（ADR-0002），M2 的思考气泡、
URL 分享、导入回放全都建在这一票产出的结构上，所以结构比功能重要。

**Blocked by:** 23

**Status:** ready-for-human

- [x] `DecisionRecord` 记全：第几手、哪个座位、完整 prompt、工具定义、模型原始输出、thinking、
      延迟、重试次数、最终采用的动作、**是否兜底**
      → `Paifu.fs` 的 11 个字段；真导出的样例见报告 §4。「最终采用的动作」存的是它在那一手
      决策包里的 id（意图不上牌谱，DECISIONS 26-3）；prompt 记最后一轮那次（26-16）。
- [x] 随机 Player 的手不必产生记录（没有可审计的推理），但牌谱的手序编号不能因此断裂
      → `Table.Turns` 是手序的唯一出处，随机座位的手照样占号；真样例里的手序是 1, 5, 9, 10, 13, 18, 22。
- [x] Paifu 有版本号字段；编解码往返测试（encode → decode → 逐字段相同）
      → `PaifuTests`（含「认不出的版本号是读不动」）与 `PaifuExportTests`。
- [x] 浏览器里一键把当前对局导出成 JSON 文件下载
      → 控制条上的「导出牌谱」（`table-export`）；`web/scripts/verify-export.mjs` 用 playwright 的
      download 事件真下了一次，已进 `ci-web.sh`。
- [x] 导出的事件流能被引擎重新 fold 出同一个终局（回放不是另一套代码路径，就是引擎本身）
      → `Replay.fs`（只做「摆回牌山 + 把动作交回 `GameState.step`」）；证据：随机 200 场逐条相同、
      七种流局形态各一条用例、浏览器里把下下来的字节 fold 一遍。
- [x] thinking 是**可省略**的一段：URL 分享（M2）省掉它，JSON 全量带着，两条路径共用同一解码器
      → `Paifu.stripThinking` 是值上的一次变换，编码器与解码器各只有一份（`None` 的字段整个不写）。

报告：`.scratch/llm-riichi-arena/run/reports/26-decision-record-and-paifu-export.md`

**倾向（可改，改了记一条）**：Paifu 的类型与编解码放 F# 侧，DecisionRecord 从 TS 过来时按 schema
decode 成 F# 类型。理由是 M2 的思考气泡在 Feliz 侧读它，往返测试与回放也都在 F# 一侧，
两处共用一份定义比双份对齐便宜。
