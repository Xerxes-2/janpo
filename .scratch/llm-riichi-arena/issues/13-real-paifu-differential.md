# 13 — 真实牌谱对拍

**What to build:** 用真实牌谱重放来对拍引擎：以牌谱的动作序列驱动引擎，逐局比对和了的役种集合、符、点数与终局点数。样本量优先、可增量。

**Blocked by:** 08, 09, 11（10 经 11 传递满足）

**Status:** ready-for-agent

## 数据已经就位（D-7 前置任务的产出，别重做）

读 `run/reports/13-prep-paifu-data.md` 拿全部细节。要点：

- **固件已提交** `tests/fixtures/paifu/`：18 局 / 177 kyoku / 1.1MB，按「最小字节集合覆盖」从 60 局样本里挑，覆盖样本全部 75 项特征；同目录 `README.md` 写了来源、许可与每局的覆盖点。**只需把它挂进 `.fsproj`**（前置任务被禁止改 fsproj）。
- 更大的样本在 `data/paifu/`（已 gitignore，60 局 / 66,264 事件行）。
- 来源：`NikkeTryHard/tenhou-to-mjai` v2.0.0（CC BY 4.0），全部为天凤鳳凰卓 `鳳南喰赤`（`gm-00a9`）→ **规则集用 S-A 表的天凤列**。
- 降级路线**实测无用**，别再试：`mjlog2mjai` 输出同样瘦身，`tenhou.net/0/log/?id=` 已 404。

## 两个数据源，各管一半（前置任务实测的结论）

上游 mjai 牌谱是**瘦身版**：`hora` 只有 `actor`/`target`/`deltas`/`ura_markers`（**没有** yakus/fu/fan/hora_points），`ryukyoku` 只有 `deltas`（**没有** reason/tenpais）。所以：

- **动作序列**从 mjai 牌谱取（驱动引擎重放）
- **役 / 符 / 番 / 点数 / 流局形态**从天凤官方 JSON 取（`tenhou.net/5/mjlog2json.cgi`）作 oracle
- 两者按 `(bakaze, kyoku, honba, kyotaku)` + `deltas` 对齐，前置任务已在 177 局上实证**零误差**

## 验收

- [ ] 固件挂进测试工程，`dotnet test` 能离线跑对拍（网络只在扩样本时需要）
- [ ] **字牌记法适配**：牌谱写 `E/S/W/N/P/F/C`（bakaze 亦然），我们的 `Tile.parse` 只认 `1z`-`7z`。映射放在本票的适配器里，**不许放宽 `Tile.parse`**——ADR-0001 定的是内部只用 mjai 记法，边界处适配
- [ ] **另立只读 `PaifuEvent`**，不要改 `Event.decoder`：牌谱的 `hora`/`ryukyoku` 缺我们的必填字段，硬塞会把引擎的事件类型弄脏
- [ ] 重放入口用**已有的** `Wall.ofOrdered` + `GameState.startFrom`（引擎已支持注入摊好的牌山；`tests/.../GameStateGenerators.fs` 的 `scriptedWall` 是可照抄的样例），**不要新造牌山注入机制**
- [ ] 对拍每次和了的役种集合、符、点数，以及每局终局点数
- [ ] **日文役名 → `Yaku` 的对照表**：oracle 的役名是日文串（票里原先没写的隐藏工作量）
- [ ] 流局形态：oracle 有则用 oracle；`deltas` 全 0 时听牌不可判，须查 oracle
- [ ] 差异以可读形式报告：哪一局、哪一巡、期望与实际
- [ ] 首批样本量下差异为 0；样本量可通过参数扩大，**不改代码**
- [ ] 无法转换或含不支持规则变体的样本被**显式跳过并计数**，附原因，不静默丢弃
- [ ] 三麻牌谱不在本票范围；样本里若出现 `nukidora` 说明混进了三麻，要能识别并排除

## 样本实证的规则事实（可直接当断言用）

- **双响成立**（样本里 3 次 `hora`→`hora` 相邻）→ 对拍时 Atamahane 必须**关**
- **无切上满贯**（`30符4飜7700点` × 30、`60符3飜7700点` × 1）→ `KiriageMangan` 必须**关**
- 未被样本实证的：连风牌雀头符、三家和了、流し満貫、国士暗杠抢杠（样本零出现）——这些的正确性靠黄金用例，不靠对拍
