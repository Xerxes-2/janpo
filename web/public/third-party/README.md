# 第三方组件声明

本站自己的代码按 MIT 放出（仓库根的 `LICENSE`）。**这一页说的是随本站一起分发的第三方东西。**

## 强 AI 基线（`baseline/janpo-baseline.wasm`）

配桌时把某一席拨到「强 AI 基线」，浏览器才会去下这份产物；首页与不选它的对局**一个字节都不下**。
它在浏览器里推理，本站没有后端。

| 项 | 内容 |
| --- | --- |
| 上游 | [shinkuan/Akagi](https://github.com/shinkuan/Akagi) 的 `native_bot/` 子目录 |
| 来源 commit | `394b329058e1b4d721dc40149658f9f9cfdd77ae`（`394b3290`） |
| 许可 | Apache License 2.0，Copyright 2026 Shinkuan |
| 许可正文 | [LICENSE-akagi.txt](./LICENSE-akagi.txt)（上游 `LICENSE.txt` 原样） |
| 上游 NOTICE | [NOTICE-akagi](./NOTICE-akagi)（上游 `NOTICE` 原样） |
| 我们改过上游源码吗 | **没有。** 一行都没改，只是从外面链接它（Apache-2.0 §4(b)/(c) 因此不触发） |
| 产物体积 | 约 6.0 MB（gzip 后约 4.8 MB），其中约 4.8 MB 是**内嵌的模型权重** |

它静态链接进来的还有：

- **riichienv-core**（[smly/RiichiEnv](https://github.com/smly/RiichiEnv)）——Apache-2.0，Copyright (c) smly。
  牌、向听、役、合法动作枚举。上游 `NOTICE` 里已经点名，随那份一起走。
- **candle**（huggingface/candle）——Apache-2.0 或 MIT 双许可。纯 Rust 推理。按 Apache 那一支走。
- **talc**——MIT。我们自己换上去的 wasm 分配器（不是上游的）。

上游 `NOTICE` 里点名的 mahjong-helper（MIT）与 mahgen（MIT）用在 Akagi 本体里，
`native_bot/` 一行没引，我们也不克隆它们，因此不构成本站的分发义务。出于诚实在此一并提及。

### 权重的训练语料

**上游声明其行为克隆自天凤牌谱；具体语料出处上游未公开，我们无从核。**

查得到的只有类型（人类天凤对局牌谱、监督式行为克隆），
上游仓库里**没有数据集名、没有 URL、没有下载脚本、没有 model card**，仓库外也查不到。
Apache-2.0 授权的是上游对**代码与权重文件本身**的权利，
**不代表天凤牌谱的权利人授权过什么**。我们照原样分发，并把这条风险明写在这里。

查证过程与逐条出处见仓库里的 `probe/akagi-wasm/NOTICE-upstream.md`
与 `.scratch/llm-riichi-arena/run/reports/91-wasm-baseline-probe.md`。
