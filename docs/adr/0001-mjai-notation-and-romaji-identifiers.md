# 词汇对齐 mjai 生态，人类可读形式隔离在渲染层

牌的正式记法采用 mjai 的 `1m-9m/1p-9p/1s-9s/1z-7z`，代码标识符采用罗马字日麻术语（`Dahai`、`Furiten`、`Shanten`、`Ukeire`），两者贯穿引擎、事件流、牌谱与测试固件；给人和 LLM 看的中文形式（「东」「白」「振听」）只存在于一个独立的渲染层，不进入任何持久化数据。这样做是因为唯一的核心接缝是 mjai 事件协议：记法与命名照抄 mjai，Mortal 接入与生态牌谱工具零翻译，查 Mortal 源码与日麻文献也零翻译。

## Considered Options

- **`E,S,W,N,P,F,C` 字牌记法**：LLM 直接读到有语义的字母，prompt 不需要渲染层。否决原因是它把「给谁看」的问题焊进了数据层：字牌与数牌要两套 parser，与 mjai 事件流和 Mortal 互转要维护映射表，导出的牌谱喂不进生态工具。SPEC 原本把这两个记法当二选一，其实它们不在同一层——真正的选择是「数据层记法」加「渲染层映射」，而不是二者取一。
- **英文意译标识符**（`DiscardTile`、`SacredDiscard`、`AcceptanceTiles`）：对不懂日麻的读者更友好，但没有先例，且与 mjai 事件名全程需要翻译，查文献时术语对不上。

## Consequences

- `Tile.toDisplay` 一类的渲染函数是**单向出口**：改 prompt 措辞或换展示语言不碰引擎、不碰牌谱、不使已存牌谱失效。
- 不懂日麻的读者（含未来的 agent）读引擎代码需要术语表，因此 `CONTEXT.md` 的罗马字↔中文对照表是必读前置，不是可选装饰。
- **标识符照术语表，wire 照 mjai，两者不一致时在编解码处映射，不改标识符去迁就 wire。**
  这条是本决定的直接推论，M0 实施中反复用到，因此写进来免得每票再议一次：
  - `Event.Ryuukyoku` 的 wire 是 `"ryukyoku"`（mjai 的实际拼法，少一个 u）
  - `Minkan` 的 wire 是 `"daiminkan"`
  - 五种途中流局的 reason 是 `sufonrenta` / `sukaikan` / `suchareach` / `sanchaho` / `kyushukyuhai`
    （取值出自 mjai 参考实现 gimite `mjai` gem 的 `active_game.rb`，**不是照规则书拼的**——
    wiki 只举了 `fanpai`，Mortal 干脆丢了 `reason`）
  - 场风的 wire 写 `1z`-`4z`

  理由与「人类可读形式隔离在渲染层」是同一条：**外部协议的既有拼写是事实，不是我们的命名权**。
  隔离渲染层挡的是展示语言，隔离编解码层挡的是协议拼写；放任任何一边渗进来，
  内部就会出现两套写法并存。
- 罗马字用于**领域概念**，英文用于**修饰词**：`Furiten.Permanent` 的 `Permanent`、
  `Limit`（满贯档，日麻没有涵盖这一整档的单一名词）都保持英文。
  自造的复合词（如 `KawaTaken`）允许，但要在 `CONTEXT.md` 里明标是自造。
