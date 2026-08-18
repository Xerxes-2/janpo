# 探路件：把 Akagi v3 的内置 bot 编成 WASM，在浏览器里出一手

票 91。**这不是牌桌**——它不接 `SeatChoice`、不进首页、不进 CI 的常规趟，
只回答「这条路通不通、要多大、要多久、许可上站不站得住」。接成选手是票 92。

量出来的数与结论在 `.scratch/llm-riichi-arena/run/reports/91-wasm-baseline-probe.md`，
署名义务在 `NOTICE-upstream.md`。这份文件只讲**怎么跑**与**踩到的坑**。

## 怎么跑

```sh
cd probe/akagi-wasm

./fetch-upstream.sh                 # 拉上游 native_bot（约 5 MB，含权重）到 .upstream/

# 编 wasm。CARGO_TARGET_DIR 挪到 /tmp 是因为 target/ 有 800 MB+，不该躺在工作区里。
(cd crate && CARGO_TARGET_DIR=/tmp/janpo-probe-target \
   cargo build --release --target wasm32-unknown-unknown)
mkdir -p dist && cp /tmp/janpo-probe-target/wasm32-unknown-unknown/release/akagi_wasm_probe.wasm dist/

node run-node.mjs 300               # 不开浏览器的那趟：通不通、出的那手对不对、延迟多少
node bench.mjs 300                  # 浏览器那趟：冷缓存加载 + gzip 传输量 + 内存（无头 Chromium）
node serve.mjs                      # 手动看：http://localhost:4191/
node scan-corpus.mjs ../../tests/fixtures/paifu/mjai/*.mjson   # 扫语料：接受率 / 延迟 / 内存

(cd crate && CARGO_TARGET_DIR=/tmp/janpo-probe-target \
   cargo run --release --bin parity -- ../fixtures/tenpai-tsumogiri.jsonl 200)   # 原生对拍
```

**要 `wasm32-unknown-unknown` target**（`rustup target add wasm32-unknown-unknown`）。
不需要 `wasm-bindgen-cli`，也不需要 `wasm-pack`——见下面「为什么不用 wasm-bindgen」。
所以 **`flake.nix` 里现在什么都不用加**：探路件不进 CI，票 92 真要上线时才谈工具链。

## 那一手是可以人工核对的

`fixtures/tenpai-tsumogiri.jsonl` 只有三行：`start_game`、`start_kyoku`、一条 `tsumo`。
座位 0 的配牌是 `1m1m 2m3m4m 5m6m7m 2p3p4p 5p6p`——**已经听牌**：
后半是 `23456p`，所以听的是 **1p/4p/7p 三面**（引擎复核：`scripts/fsi/wait-check.fsx`），
摸进来的是孤张北。**唯一保持听牌的打法就是摸切北**，打任何别的牌都退回一向听。
正确答案因此不需要强度判断：出 `dahai 北` 或 `reach + 北` 都算对，切别的就是错。

它出的是 `{"type":"reach","reach_dahai":"N"}`，top-3 候选里前两条都是打北
（reach 0.596 / 摸切 0.403），第三条 `dahai 1m` 只有 0.001。

第二重证据是 `--bin parity`：**同一份 `native_bot` 源码**编成原生 x86_64 跑同一份 fixture，
决策 JSON 与 wasm 侧逐字段相同（唯一差别是第三候选的概率末位 `0.0010278977` vs
`0.0010278967`，相对差 1e-9 —— f32 在两个后端上的舍入）。
这证的正是唯一可疑的那件事：**wasm 后端的浮点没有把策略算歪。**

## 对外说的就是 mjai

- **输入**：`probe_feed` 收一行 mjai JSON，直接进 `native_bot::engine::Engine::feed_line`。
  实测 `tests/fixtures/paifu/mjai/` 的 111 份真实牌谱（112,777 行）**一行都没被拒**，
  janpo 自己 `janpo game` 打出来的三条事件流同样零拒绝。
- **输出**：`crate/src/lib.rs` 的 `action_json` 把 `BotAction` 印成 mjai 形状的对象。
  **有三处不是 wire mjai**，票 92 要翻译，逐条写在报告的第 ④ 节。

## 为什么不用 wasm-bindgen

ABI 是手写的几个 `extern "C"` 导出 + 线性内存里的 UTF-8 字节，于是产物是**一个自足的
`.wasm`**：`WebAssembly.instantiateStreaming(fetch(url), {})` 就能起，import 对象是空的。
代价是要自己管 `probe_alloc` / `probe_free`；收益是不进 JS 胶水包、不进构建链、
不用往 `flake.nix` 里塞 `wasm-bindgen-cli`，而且**版本对不上的胶水与 wasm 不会互相咬**。

`i64` 返回值在 JS 侧是 `BigInt`（指针 << 32 | 字节数），`probe.js` 的 `decide()` 里那两行
`Number(packed >> 32n)` 就是拆它。

## 踩到的坑

**1. dlmalloc 在 candle/gemm 的大对齐分配上必崩。**
`dlmalloc-0.2.13/src/lib.rs:145` 的 `free` 丢掉了 `align` 参数就去 `validate_size`，
于是任何走 `memalign`（对齐 > 16）的分配在释放时必然撞
`assertion failed: psize <= size + max_overhead`。candle 的卷积走 `gemm`，
而 `gemm` 要 SIMD 对齐的缓冲——**默认分配器下第一次前向传播就 trap**。
换 `talc`（`crate/Cargo.toml` 的 `[target.'cfg(target_family = "wasm")'.dependencies]`）解决。
症状很容易误判成「模型加载坏了」：真正的错在释放路径上，离现场很远。

**2. wasm32 上 getrandom 没有默认后端**，不选就是 `compile_error!`。
选 `wasm_js` 后端会把 wasm-bindgen 胶水拖进来，产物就不再自足了。
所以 `crate/.cargo/config.toml` 里选 `custom`，钩子是 `src/lib.rs` 的 `getrandom_stub`
（确定性 xorshift）。**探路件从不洗牌**——牌山与手牌完全由喂进去的 mjai 事件决定——
所以这个桩不影响任何结果。**票 92 若要在浏览器里真开局（自己生成牌山），必须换成
`crypto.getRandomValues`，否则牌山可预测。**

**3. `probe_alloc` 一定要 `into_boxed_slice`。**
`Vec::with_capacity(n)` 只保证「至少」n，而 `probe_free` 是按 `len == capacity` 还回去的。
容量对不上就是堆损坏，症状是**几十次调用之后随机 trap**——现场当场踩到过，
排查代价远高于写对它的代价。`hand_out` 同理（`format!` 出来的 `String` 容量通常大于长度）。

**4. `bench.mjs` 里不许出现没有下界的 `await`。**
上一轮就死在这条：端口 4191 被残留的 `serve.mjs` 占着 → 新服务器报错退出 →
永远不吐 stdout → `await new Promise((r) => server.stdout.once("data", r))` 无限等 →
看门狗 45 分钟后把 agent 砍了。现在起服务器前先自己 `listen` 探一次端口，
子进程的 `error` / `exit` 都接上，每个等待配 `setTimeout`，`finally` 里收尸。

**5. panic 在 wasm32-unknown-unknown 上无处可打印**（没有 WASI、没有 stderr）。
`probe_install_panic_hook` / `probe_last_panic` 把最后一条信息留在线性内存里，
JS 侧 trap 之后再来取——否则 trap 只会给你一句 `unreachable`。
`probe_selftest` 是配套的：它不经过 JS 侧的任何内存进出，直接跑内嵌 fixture，
用来分清「是推理路径坏了」还是「是我的 ABI 坏了」。

## 目录

| 文件 | 干什么 |
| --- | --- |
| `fetch-upstream.sh` | 拉上游 `native_bot/` + `LICENSE.txt` + `NOTICE` 到 `.upstream/`（commit 钉死） |
| `crate/` | 探路件本体。`src/lib.rs` 是 wasm 的 C ABI，`src/bin/parity.rs` 是原生对拍 |
| `crate/Cargo.lock` | **入库**。报告里那一排数引的是一个具体的 6,039,832 B 产物；依赖不钉死就重现不出来 |
| `probe.js` | 量测逻辑。浏览器与 node 共用同一份，免得两侧各量各的 |
| `index.html` | 浏览器那一页（局面、判定、数表、原始 JSON） |
| `serve.mjs` | 自带的静态服务器，端口 4191（避开 `verify-*.mjs` 的 4179–4182）；`.wasm` 走 gzip |
| `bench.mjs` | 无头 Chromium 跑一趟并把 `window.__probeResults` 抄出来 |
| `run-node.mjs` | 不开浏览器的同一趟 |
| `scan-corpus.mjs` | 逐行扫一批牌谱：接受率、决策点数、延迟分位、线性内存 |
| `fixtures/` | 人工可核对的三行局面 + 一整局真实牌谱（东 1 局） |
| `.upstream/` `dist/` `target/` | 都不入库（`.gitignore`），随时重建 |
