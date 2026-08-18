# `baseline/janpo-baseline.wasm` 放这里

强 AI 基线那份产物（约 6.0 MB，其中约 4.8 MB 是内嵌权重）**不入版本控制**
（ADR-0006 边界 6：6 MB 的二进制进 git 会永久胀掉 clone）。
这个目录里因此只有这份说明与一条 `.gitignore`。

## 站点上没有它会怎样

**页面照常开，那一席退回自带 bot。** 拨到「强 AI 基线」的那一刻页面去拉它，
拉不到（404 / 离线 / 超时）时牌桌上会明说原因，那一席当场退回「有主见」的自带 bot，
**其余席照常打完这一局**（ADR-0006 边界 2：它是可选依赖，不是单点）。

CI 的常规趟就是这个形态——那里没有这份产物，因此**跑到的是降级那一路**。
真推理只在本机演习里跑（`web/scripts/verify-baseline.mjs --asset`）。

## 怎么造一份放进来

```sh
cd probe/akagi-wasm
./fetch-upstream.sh                 # 拉上游 native_bot（约 5 MB，含权重）到 .upstream/

# CARGO_TARGET_DIR 挪到 /tmp 是因为 target/ 有 800 MB+，不该躺在工作区里。
(cd crate && CARGO_TARGET_DIR=/tmp/janpo-probe-target \
   cargo build --release --target wasm32-unknown-unknown)

cp /tmp/janpo-probe-target/wasm32-unknown-unknown/release/akagi_wasm_probe.wasm \
   ../../web/public/baseline/janpo-baseline.wasm
```

要 `wasm32-unknown-unknown` target（`rustup target add wasm32-unknown-unknown`），
不需要 wasm-bindgen 或 wasm-pack——产物是自足的（理由在 `probe/akagi-wasm/README.md`）。

## 上线（GitHub Pages）

同一条路：Pages 那条 workflow 在部署前跑上面三行，把产物放进 `web/public/baseline/`，
再 `pnpm run build`——Vite 把 `public/` 原样拷进 `dist/`。
**仓库里因此永远只有脚本与说明。**

## 许可

这份产物含第三方 Apache-2.0 代码与内嵌权重，署名义务已随站点上线：
`web/public/third-party/`（`LICENSE-akagi.txt`、`NOTICE-akagi`、`README.md`），
页脚有一条指过去的链接。逐条清单在 `probe/akagi-wasm/NOTICE-upstream.md`。
