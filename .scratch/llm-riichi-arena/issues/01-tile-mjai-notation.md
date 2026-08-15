# 01 — 骨架与 Tile 的 mjai 记法

**What to build:** 一个能被 dotnet 构建与测试的引擎骨架，加上第一个真实能力：Tile 的 mjai 记法解析与打印。开发者能在命令行把一串 mjai 记法读进来、打印回规范形，CI 在每次提交上跑通构建与测试。工程划分为三块：引擎库（限 Fable 兼容的 F# 子集与核心库，JSON 用 Thoth.Json）、测试工程（FsCheck）、CLI 工程（无头驱动入口）。

工具链用 nix dev shell 固定（dotnet SDK，以及后续票在测试期要用的 uv 入口），CI 与本地用同一个 shell。格式化用 Fantomas，以 dotnet local tool 钉住版本，并在 CI 设 `--check` 关卡——后续 13 张票都靠这一关保持风格一致。

**Blocked by:** None — can start immediately

**Status:** ready-for-agent

- [ ] Tile 覆盖 34 种正牌与 3 种红宝牌（`5mr` / `5pr` / `5sr`），记法为 ADR-0001 规定的 mjai 记法
- [ ] 解析非法记法返回错误值，不抛异常
- [ ] FsCheck 属性：任意 Tile 打印后再解析得回自身（往返不变）
- [ ] FsCheck 属性：任意合法记法字符串解析再打印得回规范形（幂等）
- [ ] CLI 能接收一串手牌记法，打印规范形与牌数
- [ ] 仓库提供 nix dev shell，`nix develop` 后 dotnet 与 uv 可用；版本以 flake 为准而非依赖宿主机
- [ ] Fantomas 以 dotnet local tool 引入（`dotnet tool restore` 即可用），仓库带 `.editorconfig` 定义 F# 格式规则
- [ ] CI 在同一个 dev shell 里跑 `fantomas --check`、构建三个工程、跑测试；引擎工程不引入 Fable 不兼容的依赖
- [ ] 标识符遵循 CONTEXT.md 与 ADR-0001 的罗马字约定；人类可读形式（「东」「白」）只出现在渲染函数里，不进入 Tile 本身
