# `baseline/janpo-baseline.wasm` 放这里

强 AI 基线那份产物（约 6.0 MB，其中约 4.8 MB 是内嵌权重）**不入版本控制**
（ADR-0006 边界 6：6 MB 的二进制进 git 会永久胀掉 clone）。
这个目录里因此只有这份说明与一条 `.gitignore`。

## 谁会拉它

两处，**都是人先动了手才拉**（ADR-0006 边界 1）：

1. **配桌里拨到「强 AI 基线」那一席**（票 92）；
2. **复盘里按那一枚「让强 AI 把这 N 手也看一遍」**（票 93）——它拿**那一手当时喂给该席的
   那一份投影**逐手问一遍，每手多一行「〔强 AI〕打 X」。**它不需要这一桌坐过强 AI**：
   任何一份牌谱（包括首页那份回放）都问得出来。

两处共用同一份实例（`web/src/baseline/baseline.ts` 里那个模块级的 `loaded`），因此一页里至多拉一遍。

## 站点上没有它会怎样

**页面照常开，那一席退回自带 bot；复盘照常出，只是没有强 AI 那一行**
（票 93：**算不动就整行不出现**，不写「暂无」；向听 / 有效牌 / 危险度那几栏一条不少）。
拨到「强 AI 基线」的那一刻页面去拉它，
拉不到（404 / 离线 / 超时）时牌桌上会明说原因，那一席当场退回「有主见」的自带 bot，
**其余席照常打完这一局**（ADR-0006 边界 2：它是可选依赖，不是单点）。

CI 的常规趟就是这个形态——那里没有这份产物，因此**跑到的是降级那一路**。
真推理只在本机演习里跑：`web/scripts/verify-baseline.mjs --asset`（它真坐一席打一局）与
`web/scripts/verify-review.mjs`（它自己探得到这份产物；加 `--asset` 就不允许静静地走降级那一路）。

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
