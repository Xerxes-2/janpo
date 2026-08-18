# F# 脚本探针（`dotnet fsi`）

**验证引擎行为时用这个，不要把引擎逻辑移植到别的语言。**

`dotnet fsi` 可以 `#r` 引用已编译的引擎 DLL，直接调真实 API：真代码、真不变量、原生速度、零移植风险。

```bash
nix develop --command dotnet build -c Release          # 先构建，脚本引用的是 bin 里的 DLL
nix develop --command dotnet fsi --exec scripts/fsi/shanten-probe.fsx
```

### 唯一的坑：DLL 要引 CLI 工程的 bin，不是引擎工程的

```
error FS0078: 无法找到 .../src/Janpo.Engine/bin/Release/net10.0/Thoth.Json.Core.dll
```

引擎是**库工程**，NuGet 依赖（Thoth.Json.Core 等）不会复制到它自己的 `bin`；
CLI 是可执行工程，依赖都在它的输出目录里。所以一律引 `src/Janpo.Cli/bin/Release/net10.0/`
（`load-engine.fsx` 已经封好了，`#load` 它就行）。

**这道坎值得写下来**：2026-08-16 一个研究 agent 需要「撤掉某条改动看会怎样」，
撞上 FS0078 后放弃了 F# 脚本，改把 `Shanten.fs` 逐行移植成 Python 挂开关——
移植本身它做得严谨（先用仓库固件校准到 0 差异才用），但慢 200-2000 倍，
于是开了 16 个进程扫语料，把并行跑 dotnet 的另一个 agent 挤慢 6.5 倍。
一个 FS0078 的代价是这些。

### 要「撤掉某条改动看会怎样」怎么办

不许改仓库代码时，把相关源码**复制**到 `/tmp` 打补丁，再用 fsi `#load` 复制品——
仍是 F#、仍是原生速度、仍带真实类型的不变量校验。比移植到别的语言近得多。
`#load` 的顺序照 `src/Janpo.Engine/Janpo.Engine.fsproj` 里的 `<Compile>` 列表。

## 为什么不移植到 Python

2026-08-16 实测（同一批 5 万手随机手牌，单线程）：

| 方式 | 单手耗时 |
|---|---|
| `dotnet fsi` 直调编译好的 API | **5.8 µs** |
| `janpo shanten --batch` 文本管道 | ~240 µs（文本解析占绝大部分） |
| 纯 Python 移植版 | ~1-10 ms（典型） |

除了慢 2-3 个数量级，移植版还有一个**证据强度**问题：
若移植版与外部 oracle 不符，你分不清是移植错了还是引擎错了。**移植版证明不了原实现。**

Python 仍然该用在两处：跑现成的第三方 oracle（`scripts/oracle/`，PyPI `mahjong` 库），
以及牌谱等数据的解析与统计。**别用它重写我们自己的逻辑。**

## 现成的探针

| 脚本 | 干什么 |
|---|---|
| `shanten-probe.fsx` | 向听计算的行为与吞吐量基准 |
| `paifu-scan.fsx` / `paifu-scan-zip.fsx` | 扫牌谱语料（目录 / 压缩包） |
| `paifu-cost.fsx` | 牌谱解析与重放的开销 |
| `wait-check.fsx` | **听什么牌、打哪张仍听牌**——牌理复核 |

`wait-check.fsx` 是为一次实事求是的更正写的：票 91 拿一手「摸切北」当人工可核对的证据，
把它的听牌写成「4p/7p 两面」，**主人一眼看出那是 `23456p`、听 1p/4p/7p 三面**。
结论没塌（引擎复核：摸北之后确实只有摸切北保持听牌），但**手算牌理与读 README 得来的印象是同一类东西**。
要在报告里写牌理，就用这个脚本过一遍——**它比人眼便宜得多**。
