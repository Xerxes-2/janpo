# 06 — 和了的成立路径（Furiten 与 Atamahane）

**What to build:** 和了能被宣言并正确结束一个 Kyoku：Tsumo 后可宣言 Hora，他家 Dahai 后可宣言 Ron，Furiten 阻止 Ron，同巡多家荣和按 Atamahane 裁决。此票**不算点数**（事件里点数字段记 0，由 08 补正确），只保证合法动作集、事件流与进局/连庄正确——这样切片始终能独立跑绿。

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] Tsumo 后和了型成立时，Hora 进入合法动作集
- [ ] 他家 Dahai 后和了型成立时，Ron 进入对应座位的合法动作集
- [ ] Furiten 的永久与同巡两种分别维护；振听座位的 Ron 不出现在合法动作集
- [ ] 同巡多家可 Ron 时按 Atamahane（默认开）只成立打牌者下家优先的一家；开关关闭时双响/三响都成立
- [ ] 和了产出 Hora 事件并正确进局或连庄（Oya 和了连庄）
- [ ] 事件结构已含点数字段，此票填 0
- [ ] 黄金用例：构造牌山使指定和了在指定 Junme 发生；同巡双响的两种开关取值各一条
