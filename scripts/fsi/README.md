# F# 脚本探针（`dotnet fsi`）

**验证引擎行为时用这个，不要把引擎逻辑移植到别的语言。**

`dotnet fsi` 可以 `#r` 引用已编译的引擎 DLL，直接调真实 API：真代码、真不变量、原生速度、零移植风险。

```bash
nix develop --command dotnet build -c Release          # 先构建，脚本引用的是 bin 里的 DLL
nix develop --command dotnet fsi --exec scripts/fsi/shanten-probe.fsx
```

DLL 要引 **CLI** 工程的输出目录（`src/Janpo.Cli/bin/Release/net10.0/`），
不是引擎工程的——引擎是库，NuGet 依赖（Thoth.Json.Core 等）不会复制到它自己的 `bin`。

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
