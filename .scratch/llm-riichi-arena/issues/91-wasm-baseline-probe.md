# 91 — 强 AI 探路：把它编成 WASM，在浏览器里出一手

**What to build:** 主人拿到了 [`shinkuan/Akagi`](https://github.com/shinkuan/Akagi) 的授权
（**Apache-2.0**，V3 是 Rust + Tauri 单二进制，内置的是「a pure-Rust neural net embedded in the binary」），
**且允许连权重一起公开分发到 GitHub Pages**。纯 Rust 的推理意味着 **WASM 这条路是通的**——
不必开后端，强 AI 直接进浏览器。

**这一票只探路，不接牌桌**：把它编成 WASM，在浏览器里对**一个固定局面**出一手，然后把数与许可查清楚。
接成选手是票 92。

**Blocked by:** None

**Status:** ready-for-human

**做完了**：现场在 `probe/akagi-wasm/`（怎么跑看那里的 `README.md`），
结论与数在 `run/reports/91-wasm-baseline-probe.md`，署名义务在 `probe/akagi-wasm/NOTICE-upstream.md`。
一句话：**不是 Mortal，没有 libriichi/AGPL 血缘，Apache-2.0 站得住；路是通的**
（6.0 MB wasm / gzip 4.8 MB，冷加载 208 ms，单手推理中位 0.37 ms、p95 0.64 ms @ n=17,260，常驻 11 MB）。
**剩下一条风险没消：权重的训练语料只查到「人类天凤牌谱的行为克隆」，具体是哪一份、什么条款，查不到。**

## 它推翻了 spec 里的一条，所以要先查实

spec 的 Out of Scope 明写「**Mortal 的 WASM 化明确不做**」，接入方式写的是「轻量容器 + WebSocket」。
主人的授权把这条翻了。**但翻案要有依据**——这一票的头等交付物是**事实**，不是代码：

- [x] **它到底是什么网络**：**不是 Mortal，也不是它的衍生。** 是 Shinkuan 自写的 `native_bot`
      （`Cargo.toml` 的 description：“libriichi-free … BC-trained CNN inference (candle)”，
      注释“derives from no copyleft source”），规则底座是 riichienv-core（Apache-2.0）而非 libriichi。
      上游自己把 Mortal 划在进程外（README：“an AGPL-licensed bot (e.g. Mortal, which links
      libriichi) stays inside its own process”），v3 的 `mjai_bot/` 里已经没有 Mortal 了。
- [x] **权重多大、什么格式**：两份 safetensors，**`include_bytes!` 直接编进二进制**——
      `akagi4p` 2,641,816 B（660,050 参数）/ `akagi3p` 2,158,912 B（539,324 参数）。
      6.0 MB 的 wasm 里 4.8 MB（79.5%）就是它俩；三麻那份四麻用不上，摘掉就少 42% 传输量。
- [x] **许可与署名的确切要求**：Apache-2.0 §4 四条的逐条落地写在
      `probe/akagi-wasm/NOTICE-upstream.md`（含票 92 直接照抄的上线清单）。
      权重**没有**单独的许可文件，随 crate 走 Apache-2.0。
      **但训练语料的出处查不到**（只查实「人类天凤牌谱 + 行为克隆」，无名字、无 URL、无条款）。
- [x] 它对外说的**就是 mjai**。**输入侧零差异**：111 份真实天凤牌谱 112,777 行 +
      janpo 自己打的三条事件流 3,558 行，**零拒绝**（16 种事件类型全覆盖）。
      输出侧差三处（`actor` 系统性缺失、`hora` 缺 `pai`、**立直的两步被融成一条**），
      逐字段对照表在报告第 ④ 节；**翻译层一个文件、两百行上下，没有算法**。

**查完把「术语怎么定」的建议写进报告**：若不是 Mortal，`CONTEXT.md` 的 `Mortal` 词条
与 spec 里那一排「Mortal 基线」都要改成通名（例如「强 AI 基线」）——
**你不许自己改术语表**（没授权），只出建议。

## 要什么行为（探路件，可以粗糙）

- [x] 一个能跑的最小路径：`probe/akagi-wasm/index.html` 在无头 Chromium 里加载
      自足的 `dist/akagi_wasm_probe.wasm`（权重内嵌）→ 喂 `fixtures/tenpai-tsumogiri.jsonl`
      → 拿到 `{"type":"reach","reach_dahai":"N"}`。不走 wasm-bindgen，import 对象为空。
- [x] 那一手**能人工核对，而且做了原生对拍**：局面是门清听牌（4p/7p 两面平和）摸进孤张北，
      **只有摸切北保住听牌**；它出的就是立直打北（top-3 前两条都是打北）0.596/0.403。
      另用 `--bin parity` 把**同一份上游源码**编成原生 x86_64 跑同一份 fixture，
      决策 JSON 逐字段相同（唯一差别是第三候选概率的末位，相对差 1e-9）——
      这证的正是唯一可疑的那件事：**wasm 后端的浮点没把策略算歪**。
      规模上另扫了 111 场真实牌谱、17,260 次推理，零 panic / 零 trap。
- [x] 放在独立小页面 `probe/akagi-wasm/`，**不进首页、不进 CI 的常规趟**
      （`ci.sh` 不扫 `probe/`：biome 只跑 `web/`、`check-style.sh` 只扫指定的 F# 目录；
      `?dev=1` 一个字没碰，那是票 87 的地盘）。

## 必须量的数（票 92 靠它决定形态）

- [x] **体积**：`.wasm` 原始 6,039,832 B（5.76 MiB）＝ 权重 4,800,728（79.5%）＋ 代码 1,239,104。
      gzip -9 **4,794,257 B**（线上实测 `encodedBodySize` 4,801,644），brotli -q11 4,600,271。
      **gzip 几乎压不动**：权重 93%、代码 27%——传输量的下限由权重决定。
- [x] **冷缓存首次加载 207.6 ms**（每趟新 `browser.newContext()`）＝ fetch 202.8 + instantiate 2.6
      + `probe_init` 2.2。**但这是 localhost 的地板**：Pages 上成本 100% 是下载，
      按 4.80 MB gzip 算 20 Mbit 约 2.0 s、摘掉三麻权重后约 1.2 s。
- [x] **单手推理延迟（说清 n 与局面，判据 13）**：
      主数——wasm/node、**111 场真实天凤牌谱全场重放的 17,260 个决策点**：
      **中位 0.368 ms / p95 ≤ 0.644 ms**（每场取中位再取中位；p95 取最差一场）。
      对照组：wasm/Chromium 实地一局东 1 的 24 个决策点 0.5 / 0.6（Chromium 把
      `performance.now()` 钳到 0.1 ms）；同一听牌局面反复问 300 次，node 0.663 / 0.732、
      Chromium 0.8 / 1.3；原生 x86_64 同局面 200 次 0.281 / 0.310（**wasm 税 2.4×**）。
      **反复问同一局面反而慢，是因为那局面的答案是立直**，而立直要跑第二次前向
      来预测宣言牌（`engine.rs:221` 的 `predict_reach_discard`）。票 92 排预算按 0.7 ms。
      尺度感：一整场半庄（1,259 行、1,259→181 个决策点）跑完 **89 ms**。
- [x] **内存峰值 11,468,800 B（10.94 MiB）常驻**，跑完 111 场 112,777 行**一个字节没涨——不漏**。
      注：`probe_init` 调两次会顶到 14,221,312——**wasm 线性内存永不归还**，
      票 92 换座 / 重开局**不要重建引擎**，否则每次永久多占约 2.75 MiB。
- [x] **与现有构建的关系**：`flake.nix` **现在什么都不用加**（探路件不进 CI）。
      `ci.sh` **实测全绿且没变慢**——它根本不扫 `probe/`。
      票 92 建议单独 workflow（只在 crate 变了时触发）+ artifact，**别往常规趟里塞 Rust**。

## 边界

- [x] **不接牌桌**：`SeatChoice` 与决策循环一个字没碰
- [x] 不碰引擎、不碰真人坐席那条线；`web/**` 只读用了 `node_modules` 里的 playwright-core
- [x] `CONTEXT.md` / `docs/adr/*` / `spec.md` **一个字都没改**——
      改名影响面清单（逐处路径行号、该改与不该改分开）在报告末节。
      **好消息：代码里没有任何叫 `Mortal` 的标识符，只有三处注释——改名不可能弄红 CI。**
- [x] 下载预算：上游 sparse-clone **4.9 MB**（含权重）；cargo 依赖走的是机器上已有的
      registry 缓存，本票**没有**新增大下载；cargo target 目录（863 MB）挂在
      `/tmp/janpo-probe-target`，**不在工作区、不入库**。

## 交付

除常规五件外，报告里要有**一节「给票 92 的形态建议」**：懒加载怎么做、体积能不能接受、
要不要放 CDN、CI 里跑不跑得动、失败时怎么降级。

- [x] 报告 `run/reports/91-wasm-baseline-probe.md`：① 它是什么 ② 许可与署名（含权重来源查到哪一步）
      ③ 量出来的数 ④ 给票 92 的形态建议 + 末节「改名影响面清单」
- [x] `probe/akagi-wasm/README.md`（怎么跑 + 踩过的五个坑）、`NOTICE-upstream.md`（署名义务逐条）
- [x] `fetch-upstream.sh`：上游 commit 钉死 `394b3290`，sparse-checkout **只拉 `native_bot/`**
      （不拉 Akagi 本体，于是 mahjong-helper / mahgen 那两条 MIT 义务根本不进我们的分发件）；
      实跑验过，拉下来的权重 sha256 与现场一致
- [x] `./scripts/ci.sh` 全绿
