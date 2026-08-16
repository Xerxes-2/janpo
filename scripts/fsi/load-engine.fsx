// 把编译好的引擎加载进 fsi。被其他探针脚本 #load。
// 必须引 CLI 工程的输出目录：引擎是库，NuGet 依赖不会复制到它自己的 bin。
#r @"../../src/Janpo.Cli/bin/Release/net10.0/Thoth.Json.Core.dll"
#r @"../../src/Janpo.Cli/bin/Release/net10.0/Janpo.Engine.dll"
