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
./scripts/build-baseline-wasm.sh    # 拉上游 + cargo 编 wasm + 放进这个目录，一条命令
```

本机冷跑（空 target 目录）实测 **43 s**；已经编过一遍的话几秒。
`CARGO_TARGET_DIR` 默认挪到 `/tmp/janpo-baseline-target`（那个目录有 325 MB，不该躺在工作区里），
想用别处就自己传一个。要 `wasm32-unknown-unknown` target（脚本自己会 `rustup target add`），
不需要 wasm-bindgen 或 wasm-pack——产物是自足的（理由在 `probe/akagi-wasm/README.md`）。

## 上线（GitHub Pages，票 101）

**跑的是同一份脚本**：`.github/workflows/pages.yml` 在 `pnpm run build` 之前调
`scripts/build-baseline-wasm.sh`，Vite 再把 `public/` 原样拷进 `dist/`。
**仓库里因此永远只有脚本与说明。**

三件值得知道的事：

1. **产物进 Actions 缓存**，键跟着「造它的那几份输入」走（两个脚本 + crate 源码与锁文件）。
   它们不变时部署只多花几秒（取一份 4.8 MB 的缓存）；改了就重造一次（估 1.5–2.5 分钟）。
2. **造不出来不阻断部署**：站点照发，页面在浏览器里如实降级（上一节）。
   那一步失败时 Actions 里是红的，且跑批总结里会写一句「本次发布的站点上没有它」。
3. **发出去之前 `scripts/check-pages-dist.sh` 核一遍 `web/dist`**：
   三份许可在不在、产物（在的话）是不是一份像样的 wasm，并把体积与 sha256 写进跑批总结。
   本机同样跑得了：`cd web && JANPO_BASE=/janpo/ pnpm run build && cd .. && ./scripts/check-pages-dist.sh`。

**产物不是逐字可重现的**：同一份源码在不同目录下编出来的 `.wasm` 差几十个字节
（嵌进去的源码路径不同；实测 6,039,832 vs 6,039,960）。
所以「线上那份是不是这一次发的」要拿**那一次跑批总结里印的 sha256** 比，不是拿本机造的那份比。

## 许可

这份产物含第三方 Apache-2.0 代码与内嵌权重，署名义务已随站点上线：
`web/public/third-party/`（`LICENSE-akagi.txt`、`NOTICE-akagi`、`README.md`），
页脚有一条指过去的链接。逐条清单在 `probe/akagi-wasm/NOTICE-upstream.md`。
