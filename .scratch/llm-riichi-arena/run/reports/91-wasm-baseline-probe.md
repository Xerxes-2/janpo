# 票 91：浏览器内强 AI 基线 —— 探路报告

**结论先说三句。**

1. Akagi v3 内置的那个网络**不是 Mortal**，也没有 libriichi / AGPL 血缘。它是 Shinkuan 自己写的
   `native_bot`，Apache-2.0，站得住。**票 92 的许可闸门放行。**
2. 它**编得成 WASM 并且在浏览器里跑得动**：一个自足的 6.0 MB `.wasm`（gzip 后 4.8 MB），
   冷缓存起 208 ms，单手推理**中位 0.37 ms / p95 0.64 ms**（17,260 个真实决策点），
   常驻内存 11 MB。**spec 的 Out of Scope「Mortal 的 WASM 化明确不做」在事实层面已被推翻。**
3. **权重的训练语料查不到名字。** 只查实到「人类天凤牌谱的行为克隆」，
   哪一份语料、什么条款，仓库内外都没有。**这是票 92 上线前唯一未消的风险。**

现场在 `probe/akagi-wasm/`，怎么跑与踩过的坑写在那里的 `README.md`，
署名义务逐条写在 `NOTICE-upstream.md`。本报告只讲**查实了什么**与**量出了什么**。

---

## ① 它是什么（证据链）

上游：`https://github.com/shinkuan/Akagi`，commit `394b329058e1b4d721dc40149658f9f9cfdd77ae`（2026-08-18）。
我们只取其中一个子目录 `native_bot/`（`fetch-upstream.sh` 的 sparse-checkout 钉死了这一点）。

**它不是 Mortal，逐条证据：**

| 问 | 答 | 出处（原文） |
| --- | --- | --- |
| 它自己说自己是什么 | 「无 libriichi 的内置 bot」 | `native_bot/Cargo.toml` 的 `description`：`Built-in, libriichi-free mahjong bot for Akagi: shared obs/action codec, BC-trained CNN inference (candle), and a training-data extractor.` |
| 有没有 copyleft 血缘 | 明确否认 | 同文件注释：`This crate is original work built on riichienv-core (Apache-2.0); it derives from no copyleft source.` |
| 上游自己怎么划这条线 | 把 Mortal 划在**进程外** | Akagi 根 `README.md`：`This is an intentional license boundary: an AGPL-licensed bot (e.g. Mortal, which links libriichi) stays inside its own process` |
| v3 仓库里还有没有 Mortal | **没有**。`mjai_bot/` 下只剩 `example/`（一个 Python 规则 bot） | `ls /tmp/akagi/mjai_bot` → `example  README.md` |
| 规则底座是谁 | riichienv-core 0.4.8（smly，Apache-2.0），不是 libriichi | `native_bot/Cargo.toml` 依赖表；Akagi `NOTICE` 的 RiichiEnv 条目 |
| 网络本身 | 自己训的小 CNN，660,050 参数（4p）/ 539,324（3p） | `native_bot/train/README.md` 的复现表 |

**注意一处容易误判的地方**：Akagi 的 GitHub 仓库描述至今仍写着
「Comes with Mortal AI as a built-in example」。那说的是 **v2 分支**的 `mjai_bot/mortal`
——一个跑在**独立进程**里、经 mjai over stdio 接进来的可选 bot。
v3 已经没有它了，而且从来不是 `native_bot`。**只看仓库描述会得出相反的结论。**

**它是什么**：把局面编码成固定形状的观测张量，喂一个卷积网络，在
riichienv 枚举出的合法动作集上取 argmax。推理用 candle（纯 Rust，无 Python、无 ONNX、无 BLAS）。
两份权重 `include_bytes!` 进二进制，所以产物自足。
上游 README 的原话是 “it imitates human play from Tenhou logs with a compact model”——
**它是模仿人类打法的行为克隆网络，不是自对弈 RL**，这一点与 Mortal / Suphx 有本质区别，
也意味着它的强度上限是「熟练人类」而不是「超人」。作为**基线锚点**这恰好够用。

---

## ② 许可与署名

完整版在 `probe/akagi-wasm/NOTICE-upstream.md`（含 Apache-2.0 §4 四条义务的逐条落地
与票 92 的上线清单）。这里只留结论：

| 谁的东西 | 什么许可 | 我们分发要放什么 |
| --- | --- | --- |
| `native_bot`（含两份权重） | Apache-2.0，© 2026 Shinkuan | `LICENSE.txt` 原样 + `NOTICE` 原样，页脚给出链接与一句归属 |
| `riichienv-core` 0.4.8 | Apache-2.0，© smly | 已被上游 `NOTICE` 覆盖，随上面那份一起走 |
| candle 0.9 | Apache-2.0 / MIT 双许可 | 静态链接进 `.wasm`，按 Apache 一支走，同上 |
| talc 5（我们自选的 wasm 分配器） | MIT | 加一行 MIT 归属 |

上游 `NOTICE` 里点名的 **mahjong-helper（MIT）与 mahgen（MIT）都在 Akagi 本体里，
`native_bot/` 一行没引**，我们的 sparse-checkout 也不拉它们，因此不构成我们的义务
（`NOTICE-upstream.md` 里照样列了，出于诚实）。

**我们没有改上游一行源码**，只是从外面链接它（`crate/src/lib.rs`），
所以 §4(b)「改过的文件要标注」不触发。

### 权重的来源 —— 查到哪一步（这条**没有**被许可覆盖）

**查实的**（三处独立出处互相印证）：

- `native_bot/README.md`：“it imitates human play from **Tenhou logs**”
- `native_bot/train/README.md`：“trained by **behavior cloning** (supervised imitation) of
  **human Tenhou logs**”；提取器吃的是 mjai `.json.gz`
- `native_bot/src/defaults.rs`：“4-player weights (**behavior-cloned from Tenhou logs**)”
- 规模：4p 40k 局 → 6.0M 样本、val top-1 75.9%；3p 40k 局 → 4.0M 样本、val top-1 77.8%

**查不到的（白纸黑字）**：

- **具体是哪一份天凤牌谱语料——仓库里没有名字、没有 URL、没有下载脚本。**
  `train/README.md` 里它只是命令行上的 `<dataset>/p4`，`src/bin/extract.rs` 只收一个目录路径。
- **那份语料自身的许可 / 使用条款没有任何声明**，`weights/` 目录里只有两份 `.safetensors`
  和两份 parity fixture，**没有 model card、没有 LICENSE**。
- 仓库外也查不到：搜过上游 README（含 v2 分支）、GitHub Releases、官网、DeepWiki 条目，
  **一律只说 “built-in AI / behavior cloning”，不点名数据集**。上游把模型贡献放在 Discord 上，
  那条线我们无从核。

**别把这条糊过去**：Apache-2.0 授权的是 Shinkuan 对**代码与权重文件本身**的权利，
**不代表天凤牌谱的权利人授权过什么**。主人的那句「允许连权重一起公开分发」承担了这个风险，
但风险是真实存在的，且**要归零只有两条路**：
① 找上游作者书面确认语料出处；
② 用我们自己说得清来源的牌谱重训（管线现成：`extract` + `train.py`，上游说 4070 Ti 上 5 分钟）。
**我倾向把 ② 记成票 92 之后的一张备选票**——它同时解决许可与「强度是否可控」两件事。

---

## ③ 量出来的数

**测量条件**：AMD x86_64 Linux，`nice -n 19`，机器上另有两个 agent 在跑 CI（所以下面的数
偏保守）。浏览器是 HeadlessChrome 151（`/usr/bin/google-chrome-stable`），
服务器是 `serve.mjs`（localhost，`.wasm` 走 gzip，与 GitHub Pages 同形）。
node 侧是 V8，`performance.now()` 纳秒分辨；**浏览器侧 Chromium 把 `performance.now()`
钳到 0.1 ms**，所以浏览器那几行是量化过的，别拿它们比小数点后两位。

### 体积 —— 6.0 MB 里有 4.8 MB 是权重

| 项 | 字节 | 占比 / 说明 |
| --- | --- | --- |
| `dist/akagi_wasm_probe.wasm` 原始 | **6,039,832**（5.76 MiB） | 自足，import 对象为空 |
| ├ 内嵌权重 `akagi4p.safetensors` | 2,641,816 | `include_bytes!` |
| ├ 内嵌权重 `akagi3p.safetensors` | 2,158,912 | `include_bytes!`，**四麻用不上** |
| └ 代码 + candle + riichienv + 运行时 | 1,239,104（1.18 MiB） | 6,039,832 − 4,800,728 |
| gzip -9（文件） | **4,794,257**（4.57 MiB） | 压缩比仅 79% |
| gzip（线上实测，Resource Timing `encodedBodySize`） | 4,801,644 | 与上面差 7 KB 是流式压缩的分块开销 |
| brotli -q 11 | 4,600,271（4.39 MiB） | 比 gzip 再省 194 KB（4%） |

**为什么 gzip 几乎压不动**：分开压就一目了然——
权重 4,800,728 → 4,464,985（**93%**，f32 张量近似不可压），
代码那 1.18 MiB → 约 329 KB（**27%**）。
**结论：传输量的下限由权重决定，与我们怎么编译无关。**

**能立刻省下的**：`akagi3p.safetensors` 是三麻权重，四麻桌完全用不到。
把它从 `include_bytes!` 里摘掉，产物直接掉到 **约 3.88 MB 原始 / 约 2.79 MB gzip**——
**gzip 传输量少 42%**。这是票 92 最便宜的一刀。

### 冷缓存首次加载 —— 208 ms（但这是**地板**，不是真实值）

无头 Chromium，每趟 `browser.newContext()`（HTTP 缓存全空），localhost：

| 阶段 | 耗时 | 说明 |
| --- | --- | --- |
| `fetch` 到手（gzip 4.80 MB） | 202.8 ms | **localhost。这里量到的是 gzip 解压 + 落盘，不含任何真实网络** |
| `WebAssembly.instantiate` | 2.6 ms | 编译 + 实例化，6.0 MB 模块 |
| `probe_init`（解 safetensors + 建模型） | 2.2 ms | |
| **合计** | **207.6 ms** | |

**要老实说的**：GitHub Pages 上真正的耗时由下载主导。按 4.80 MB gzip 算，
20 Mbit/s 家宽约 **2.0 s**，5 Mbit/s 移动网约 **7.7 s**。
**摘掉三麻权重后分别是约 1.2 s / 4.5 s。**
编译与初始化那 4.8 ms 在任何网络下都可以忽略——**这条路的成本 100% 是下载，不是计算。**

### 单手推理延迟 —— 中位 0.37 ms / p95 0.64 ms

**测了多少次、什么局面，逐行写清楚**（判据 13：这个项目在「一个孤零零的延迟数」上栽过六次）：

| 跑在哪 | 什么局面 | n | 中位 | p95 | max |
| --- | --- | ---: | ---: | ---: | ---: |
| **wasm / node V8** | **111 份真实天凤牌谱（`tests/fixtures/paifu/mjai/`）全场重放，座位 0 被问到的每一手** | **17,260** | **0.368**\* | **≤0.644**\*\* | — |
| wasm / node V8 | 同一个听牌局面反复问 300 次 | 300 | 0.663 | 0.732 | 1.488 |
| wasm / Chromium | 一整局东 1（`fixtures/kyoku-e1.jsonl`）里的每个决策点 | 24 | 0.5 | 0.6 | 0.7 |
| wasm / Chromium | 同一个听牌局面反复问 300 次 | 300 | 0.8 | 1.3 | 2.7 |
| **原生 x86_64**（`--bin parity`，同一份 `native_bot` 源码） | 同一个听牌局面反复问 200 次 | 200 | 0.281 | 0.310 | — |

\* 每场取中位，再取 111 个中位的中位。
\*\* 111 场里**最差**那一场的 p95。

**两件要解释的事**：

1. **为什么「反复问同一个局面」反而更慢（0.66 vs 0.37）？** 因为那个局面的答案是**立直**，
   而立直要跑**第二次前向**：`engine.rs:221` 在 top1 是 `Riichi` 时调 `predict_reach_discard`
   ——mjai 的 `reach` 必须同时说出宣言牌，说不出上游就把立直从候选里删掉。
   所以 0.66 ms ≈ 2 × 0.33 ms，与语料里 0.37 ms 的单次前向对得上。
   **票 92 排预算要按「立直手 0.7 ms」而不是「平均 0.37 ms」。**
2. **wasm 比原生慢多少**：同一局面、同一份源码，0.663 / 0.281 ≈ **2.4×**。
   对 SIMD-heavy 的卷积来说这是正常的 wasm 税（我们没开 `simd128`；开了大概率还能收窄，
   代价是要盯 target feature 的兼容面——**票 92 不必碰，因为 0.37 ms 已经远低于任何人能感知的阈值**）。

**放到牌桌的尺度上**：一整场半庄（1,259 行事件、181 个决策点）在 node 里跑完是 **89 ms**。
也就是说**「一个 AI 打完整场半庄」比「一次 LLM API 调用」还快两个数量级。**

### 内存峰值 —— 11.0 MiB 常驻，且不漏

wasm 线性内存（`memory.buffer.byteLength`，只增不减，所以它**就是**峰值）：

| 时刻 | 线性内存 | |
| --- | ---: | --- |
| `instantiate` 之后 | 5,963,776（5.69 MiB） | 模块的静态数据 |
| `probe_init` 之后 | **11,468,800（10.94 MiB）** | 解出来的模型张量 |
| 跑完 111 场（112,777 行、17,260 次推理）之后 | **11,468,800** | **一个字节都没涨——没有泄漏** |
| 浏览器那趟（`probe_init` 调了两次） | 14,221,312（13.56 MiB） | 见下 |

**那条 14.2 MB 要解释**：浏览器那一趟先后跑了两个场景，`probe_init` 调了两次。
**wasm 线性内存永不归还给宿主**，所以第二次初始化把峰值又顶高了 2.75 MiB。
**票 92 要记住这条**：换座位 / 重开局**不要重建引擎**，否则每重建一次就永久多占约 2.75 MiB。
JS 侧堆同时是 8.65 MB used / 9.91 MB total（跟 wasm 无关，是页面本身）。

### 那一手是对的 —— 两重证据

**第一重，人工可核对。** `fixtures/tenpai-tsumogiri.jsonl` 只有三行。
座位 0 的配牌是 `1m1m 2m3m4m 5m6m7m 2p3p4p 5p6p`——**已经听牌**（4p/7p 两面、门清平和），
摸进来的是孤张北。**唯一保持听牌的打法就是摸切北**，打别的一律退回一向听。
正确答案因此不依赖强度判断。它给的是：

```json
{"type":"reach","reach_dahai":"N"}
```

top-3 候选：`reach + 打北` 0.596、`摸切北` 0.403、`dahai 1m` 0.001。**对。**

**第二重，原生对拍。** `--bin parity` 把**同一份 `native_bot` 源码**编成原生 x86_64，
跑同一份 fixture：决策 JSON 与 wasm 侧**逐字段相同**，唯一差别是第三候选的概率末位
（`0.0010278977` vs `0.0010278967`，相对差 1e-9 —— f32 在两个后端上的舍入）。
**这证的正是唯一值得怀疑的那件事：wasm 后端的浮点没有把策略算歪。**

**第三重（规模）**：111 场真实牌谱、17,260 次推理，**零 panic、零 trap、零内存增长**。

---

## ④ 给票 92 的形态建议

### 对外是不是 mjai —— 输入是，输出差一层薄翻译

**输入侧：零差异，实测。**
`probe_feed` 收一行 mjai JSON 直接进 `Engine::feed_line`。扫过的语料：

| 语料 | 行数 | 被拒 |
| --- | ---: | --- |
| `tests/fixtures/paifu/mjai/` 111 份真实天凤牌谱 | 112,777 | **0** |
| `janpo game 91 --hanchan` / `--covering` / `--opinionated` 三条我们自己打出来的事件流 | 3,558 | **0** |

覆盖面是完整的：语料里 16 种事件类型全都出现过（含 `ankan` 74、`kakan` 58、`daiminkan` 17、
`dora` 141、`reach_accepted` 852、`ryukyoku` 168）。**janpo 现在打出来的 mjai 可以直接灌进去，
一个字段都不用改。**

两处要知道但不必动的细节：
- riichienv 的 `kakan` 只读 `{actor, pai}`，janpo 多发的 `consumed` 被 serde 静默忽略——无害。
- 未知 `type` **不会**被静默吞掉：`mjai_compat::parse_line` 显式把 `MjaiEvent::Other` 映射成
  `None`，`feed_line` 于是返回 `false`（上游自己有测试守着这条）。
  **票 92 必须把 `feed_line` 的 false 当硬错误抛，不许 `continue`。**
  （`scan-corpus.mjs` 里是 `continue`，那是因为它在做统计。）

**输出侧：`BotAction` 是 mjai 的形状，但差三处。** 逐条对 `src/Janpo.Engine/Action.fs`：

| janpo 的 `Action`（wire） | 探路件 `action_json` 印出来的 | 差在哪 |
| --- | --- | --- |
| `dahai {actor,pai,tsumogiri}` | `{type,pai,tsumogiri}` | 缺 `actor` |
| `pon` / `chi` / `daiminkan` `{actor,target,pai,consumed}` | `{type,target,pai,consumed}` | 缺 `actor` |
| `ankan {actor,consumed}` | `{type,consumed}` | 缺 `actor` |
| `kakan {actor,pai,consumed}` | `{type,pai,consumed}` | 缺 `actor` |
| `none {actor}` | `{type}` | 缺 `actor` |
| `hora {actor,target,pai}` | `{type,target}` | 缺 `actor`、**缺 `pai`** |
| `reach {actor}`，宣言牌是**下一手**的 `dahai` | `{type,reach_dahai}` | **形态不同：把两步融成了一条** |
| `ryukyoku {actor}`（九种九牌） | `{type}` | 缺 `actor` |
| —（四麻没有拔北） | `{type:"nukidora"}` | 三麻才有，四麻用不上 |

**`actor` 全线缺失是系统性的、也是平凡的**：`BotAction` 根本不带座位，
座位在 `probe_init(num_players, seat)` 时就钉死了，翻译层补一个常量即可。

**只有两处需要真的写代码**：

1. **立直的两步 vs 一条。** janpo 的 `Action.Riichi actor` 只是宣言，引擎随后会再问一次宣言牌；
   `BotAction::Reach { pai }` 把两者融在一条里（上游注释说得很清楚：雀魂把宣言和打牌合成一次点击）。
   翻译层要**扣住 `reach_dahai`**，等引擎回头问的时候吐成
   `Dahai(actor, reach_dahai, tsumogiri)`，且 `tsumogiri` 得自己算（拿 `reach_dahai` 跟刚摸的那张比）。
2. **`hora` 缺 `pai`。** janpo 要那张牌（自摸=刚摸的、荣和=刚打出的）。
   两种情况翻译层都能从自己的观测里补出来，不必回头问 wasm。

**工作量估计：一个 F#/TS 文件，两百行上下，没有算法。** 这是票 92 里最不值得担心的部分。

### 体积能不能接受、要不要 CDN、怎么降级

**建议的形态（按优先级）**：

1. **摘掉三麻权重。** 一行 `include_bytes!` 的事，gzip 传输量 4.80 MB → 约 2.79 MB（**−42%**）。
   我们只有四麻桌，这份 2.16 MB 是纯浪费。**这是票 92 的第一刀，不做没道理。**
2. **懒加载，而且是「按需」不是「预取」。**
   AI 席不在场时（四家全 LLM / 全随机）**一个字节都不要下**。
   `dist/*.wasm` 从 `Roster` 里选中强 AI 席的那一刻才 `fetch`——冷启动 208 ms（本地）
   /约 1.2 s（20 Mbit）完全藏得进「开局」那次点击的等待里。
3. **不要 CDN。** GitHub Pages 自己就上 CDN 且自动 gzip，我们实测的 `content-encoding: gzip`
   就是同一形态。引第三方 CDN 只会多一个跨域、多一条隐私说明、多一个可用性单点。
   **真要再省，用 brotli**——GitHub Pages 支持 `br`，比 gzip 再省 4%（194 KB），代价为零。
4. **`.wasm` 不入库。** 6 MB 的二进制进 git 会永久胀掉 clone。
   走 CI 里构建 + 作为 Pages 资产发布这条路；仓库里只留 `fetch-upstream.sh` 与 crate 源码
   （探路件已经是这个形态，`.gitignore` 里 `dist/ target/ .upstream/`）。

**CI 里跑不跑得动**：

- **常规趟：不跑，也不该跑。** 一次干净构建要拉 candle 全家（cargo 缓存 863 MB），
  CI 现在完全没有 Rust 工具链，`flake.nix` 也不含 `wasm32-unknown-unknown`。
  为了一个可选选手把每一趟 CI 都拖上几分钟，不划算。
- **建议：单独的 workflow，只在 `probe/` 或那个 crate 变了时触发**，产物走 artifact →
  Pages 部署那一步取用。浏览器侧的闸门（「强 AI 席能出一手且那手合法」）用**预先构建好的
  `.wasm` artifact**跑，不在 CI 里现编。
- **本地开发**：`fetch-upstream.sh` + 一条 `cargo build` 就够，见 `probe/akagi-wasm/README.md`。

**资产拉不动时怎么降级**（这条要在票 92 里写死，不能等它自己炸）：

- `fetch` 失败 / 超时 / `instantiate` 抛 → **那一席自动退回 `OpinionatedPlayer`**，
  并在牌桌上把该席标成「强 AI 不可用，已退回规则 bot」。
  spec 早就要求过同一件事（`spec.md:75`「后端不可用时仍能正常开桌」），只是当初想的是容器。
- **绝不要卡在开局那一步等 wasm**。给 `fetch` 配 `AbortSignal.timeout`——
  本票就是被一个没有下界的 `await` 干掉的，别在产品代码里重演。
- 权重是**确定性**的：同样的事件流必然给同样的一手（探路件的 `getrandom` 桩根本不参与推理）。
  所以牌谱里只要记下「这一席是强 AI + 权重版本」就能完整复现，**不必把它每一手的
  概率分布都写进牌谱**（那会让牌谱膨胀，且对分享没用）。

**一件顺带查实的、票 92 会撞上的事**：探路件的 `getrandom` 用的是确定性桩
（`crate/src/lib.rs` 的 `getrandom_stub`），因为它**从不洗牌**——牌山完全由喂进去的事件决定。
**票 92 如果让浏览器自己生成牌山，必须换成 `crypto.getRandomValues`，否则牌山可预测。**
这条写在 `probe/akagi-wasm/README.md` 的「踩到的坑」第 2 条，别漏掉。

---

## 改名影响面清单（**本票一个字都没改，只出清单**）

票 91 查实了「它不是 Mortal」，于是 `CONTEXT.md` 的用法与 spec 里那一排「Mortal 基线」
都成了事实错误。**改它要主人另开一次术语表授权**（AGENTS.md 硬约束 5）。下面是逐处清单。

**先说两条让决策变简单的事实**：

- **代码里没有任何叫 `Mortal` 的标识符。** 全仓库 grep 下来，
  `.fs` / `.ts` 里出现的 `Mortal` **全部在注释里**（三处）。
  **⇒ 改名是纯文档动作，不动一行可执行代码，不可能让 CI 变红。**
- **建议的通名：「强 AI 基线」**——`spec.md:11` 自己已经用过这个词组
  （「Mortal 这类强 AI 基线」），沿用它，术语表不必新造词。

### A. 该改（把 Mortal 当作「我们要接的那个东西」，现在是事实错误）

| 文件:行 | 现在写的 | 建议 |
| --- | --- | --- |
| `CONTEXT.md:3` | 「让多个 LLM、Mortal 与真人同桌」 | 「多个 LLM、强 AI 基线与真人」 |
| `CONTEXT.md:146` | Agent 词条举例「…、Mortal 客户端或本地真人坐席」 | 「…、内置强 AI 或本地真人坐席」 |
| `spec.md:11` | 「Mortal 这类强 AI 基线」（×1）、「现有工具（Mortal 本地部署）安装门槛极高」（×1） | 前者删「Mortal 这类」；后者是**事实陈述，保留** |
| `spec.md:15` | 「随机 bot / Mortal 基线 / 本地真人」「Mortal 通过一个可选的轻后端容器…接入」 | 「强 AI 基线」；末句整句重写为浏览器内 WASM |
| `spec.md:31` | 座位类型枚举里的「Mortal」 | 「强 AI」 |
| `spec.md:63` | 「与 LLM/Mortal 同桌对局」 | 「与 LLM / 强 AI 同桌」 |
| `spec.md:69` | 「含 Mortal 对照标注，Mortal 可用时」 | 「含强 AI 对照标注」 |
| `spec.md:71` | **节标题**「### Mortal 基线」 | 「### 强 AI 基线」 |
| `spec.md:73,74,75` | 三条用户故事里的 Mortal（共 5 处） | 同上 |
| `spec.md:81` | 「Mortal 客户端」「Mortal 免翻译接入」 | 「强 AI 客户端」「强 AI 免翻译接入」 |
| `spec.md:103` | **节标题**「### Mortal 接入（可选后端）」 | 「### 强 AI 基线接入（浏览器内）」 |
| `spec.md:105,106` | 「轻量容器承载 Mortal，经 WebSocket…」「Mortal 的 WASM 化明确不做」 | **整节推翻重写**——票 91 已证 WASM 化可行，形态是浏览器内 |
| `spec.md:123` | 「M3：…+ Mortal 容器接入 + …」 | 「+ 强 AI 基线接入」 |
| `spec.md:132` | 「对拍脚本风格以 Mortal 生态的牌谱工具为参考」 | **事实陈述，保留** |
| `spec.md:138` | Out of Scope 的「Mortal 的 WASM 化/浏览器内推理」 | **删掉这一条**（票 91 推翻了它） |
| `spec.md:148` | 「可解释推理 + Mortal 强度锚点 + 脚手架对照实验」 | 「+ 强 AI 强度锚点 +」 |
| `src/Janpo.Engine/OpinionatedPlayer.fs:18` | 注释「（Mortal 基线）」 | 「（强 AI 基线）」 |
| `src/Janpo.Engine/RandomPlayer.fs:108` | 注释「LLM 适配器、Mortal 与真人坐席」 | 「…、强 AI 与真人坐席」 |
| `src/Janpo.Web/Roster.fs:22` | 注释「Mortal 与真人坐席是 M3 的 case」 | 「强 AI 与真人坐席…」 |
| `docs/adr/0003:7` | 「作为与 Mortal 容器同级的**可选**依赖」 | **ADR 不改**（历史记录）；若要动须单独授权 |

### B. 不该改（这些地方 Mortal 就是在说 Mortal，事实正确）

- `docs/research/shanten-vs-libriichi.md`（全篇，7 处）——对拍的对方就是 `Equim-chan/Mortal`
- `docs/research/shanten-tenpai-boundaries.md:326,646`——引的是 libriichi 的实现
- `docs/research/engine-perf-caller-and-browser.md:362`——性能对照的对方
- `docs/adr/0001:3,7,20`——讲 mjai 记法与 Mortal 生态互通，成立
- `tests/fixtures/paifu/README.md:25`——上游数据集确实是给 Mortal 训练用的
- `run/DECISIONS.md`（9 处）、`run/reports/*`（5 处）、已关的票 42/73——**历史记录不追改**
- `run/M3-SCHEDULE.md:9,15,16`、票 87:50、票 91 本身——描述的是「当时还没查清」的状态，属实

**总计需要改的：`CONTEXT.md` 2 处、`spec.md` 约 20 处（含 2 个节标题与 1 整节重写）、
F# 注释 3 处。零个标识符。**
