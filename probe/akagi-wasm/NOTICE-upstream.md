# 上游署名（票 91 查实，逐项附出处）

这份文件回答一个问题：**把这个探路件的产物公开分发到 GitHub Pages，我们必须放什么。**

产物是**一个 `.wasm`**，里面静态链接了上游 Akagi v3 的 `native_bot` 与它 `include_bytes!` 进来的
两份 `.safetensors` 权重。所以分发的是「含第三方 Apache-2.0 代码与权重的二进制形式」——
Apache-2.0 §4 的四项义务全都触发。

## 谁的东西 / 什么许可

| 东西 | 出处 | 许可 | 依据（文件 + 原文） |
| --- | --- | --- | --- |
| `native_bot` crate（obs / action codec / candle CNN 推理 / 内嵌权重） | [shinkuan/Akagi](https://github.com/shinkuan/Akagi) `native_bot/`，commit `394b3290` | **Apache-2.0** | `native_bot/Cargo.toml`：`license = "Apache-2.0"`，注释 `This crate is original work built on riichienv-core (Apache-2.0); it derives from no copyleft source.` |
| Akagi v3 本体（我们只取 `native_bot/`，没取别的） | 同上，`LICENSE.txt` / `NOTICE` | Apache-2.0，Copyright 2026 Shinkuan | `NOTICE` 首行：`Akagi v3 / Copyright 2026 Shinkuan` |
| `riichienv-core` 0.4.8（牌 / 向听 / 役 / 合法动作枚举 / 规范动作 id） | [smly/RiichiEnv](https://github.com/smly/RiichiEnv)，crates.io 依赖 | **Apache-2.0** | Akagi `NOTICE` 的 “RiichiEnv (riichienv-core) … Licensed under the Apache License, Version 2.0. Copyright (c) smly” |
| `candle-core` / `candle-nn` 0.9（纯 Rust 推理） | huggingface/candle | Apache-2.0 或 MIT（双许可） | `native_bot/Cargo.toml` 的依赖表 |
| `talc` 5（我们自己换的 wasm 分配器，不是上游的） | crates.io | MIT | 本探路件自选，见 `crate/Cargo.toml` |
| 权重 `akagi4p.safetensors` / `akagi3p.safetensors` | 上游 `native_bot/weights/` | 随 crate 走 Apache-2.0（**没有单独的权重许可文件**） | 目录里只有 4 个文件：两份 `.safetensors` + 两份 `parity_*.json`，**没有 LICENSE / README / model card** |

**上游 `NOTICE` 里被点名的另外两条与我们无关**：mahjong-helper（MIT，用在 Akagi 的
`src/analysis/`）与 mahgen（MIT，用在 `src/game_state/mahgen_view.rs` 与前端）——
两处都在 Akagi 本体里，`native_bot/` 一行都没引。**我们只克隆 `native_bot/`**（见
`fetch-upstream.sh` 的 sparse-checkout），所以它们不进我们的分发件。
出于诚实我们照样在下面提一句，但不作为义务项。

**没有 GPL / AGPL 血缘**：见 `README.md` 的「它到底是什么」一节。

## 我们分发要放什么（Apache-2.0 §4 的逐条落地）

§4(a) 分发件要带一份许可 → 站点上放 `LICENSE-akagi.txt`（上游 `LICENSE.txt` 原样）。
§4(b) 改过的文件要标注 → **我们一行都没改上游源码**，只是从外面链接它（`crate/src/lib.rs`）。
§4(c) 保留源码里的版权 / 专利 / 商标 / 归属声明 → 我们不复制上游源文件，自动满足。
§4(d) 上游有 `NOTICE` 就必须在分发件里带上 → 站点上放 `NOTICE-akagi`（上游 `NOTICE` 原样）。

**票 92 上线时的清单**（探路件不上线，所以这一段是给票 92 照抄的）：

1. `web/public/third-party/LICENSE-akagi.txt` ← 上游 `LICENSE.txt`
2. `web/public/third-party/NOTICE-akagi` ← 上游 `NOTICE`
3. 页脚那条许可链接旁边加一条「第三方组件」，指向上面两份，并写明：
   本站分发的 `.wasm` 含 Akagi v3 `native_bot`（Apache-2.0, © 2026 Shinkuan）
   与 riichienv-core（Apache-2.0, © smly），**内嵌其行为克隆权重**，来源 commit `394b3290`。
4. 仓库根 `README` / `CONTEXT.md` 不必动（那是我们自己代码的许可）。

## 权重的来源——查到哪一步

**代码的许可不自动覆盖训练数据的来源**，所以这条单列。查证顺序与结果：

- `native_bot/README.md`：“it imitates human play from **Tenhou logs** with a compact model”
- `native_bot/train/README.md` 第 3–4 行：“trained by **behavior cloning** (supervised imitation)
  of **human Tenhou logs**”；提取器读的是 mjai `.json.gz` 对局日志
- `native_bot/src/defaults.rs` 的 doc comment：“4-player weights (**behavior-cloned from Tenhou logs**)”
- `native_bot/train/README.md` 的复现表：4p 40k 局 → 6.0M 样本、10 epoch、val top-1 75.9%、
  660,050 参数；3p 40k 局 → 4.0M 样本、val top-1 77.8%、539,324 参数

**查不到的（白纸黑字记下来）**：

- **具体是哪一份天凤牌谱语料，仓库里没有名字、没有 URL、没有下载脚本。**
  `train/README.md` 里它只是命令行上的 `<dataset>/p4`，`src/bin/extract.rs` 也只收一个目录路径。
- **那份语料自身的许可 / 使用条款没有任何声明**，`weights/` 目录里没有 model card。
- 仓库外也没查到：搜过上游 README（含 v2 分支）、GitHub Releases 说明、官网
  `akagi.shinkuan.me`、DeepWiki 的条目，**都只说 “built-in AI / behavior cloning”，
  不点名数据集**。上游把模型贡献与 key 分发放在 Discord 上，那条线我们无从核。

**结论**：训练数据的**类型**是查实的（人类天凤对局牌谱，行为克隆），
**那份语料的出处与再分发条款是查不到的**。这不是「Apache-2.0 所以没事」——
Apache-2.0 覆盖的是 Shinkuan 对代码与权重文件本身的授权，不代表天凤牌谱的权利人授权过什么。
风险由主人的那句「允许连权重一起公开分发」承担；**如果要把这条风险归零，只有两条路**：
① 找上游作者书面确认语料出处；② 用我们自己能说清来源的牌谱重训一份权重
（管线是现成的：`extract` + `train.py`，上游 README 说 4070 Ti 上 5 分钟）。
