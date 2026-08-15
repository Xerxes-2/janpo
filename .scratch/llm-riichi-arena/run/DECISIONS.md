# 夜间自主决策记录

无人值守跑批期间，agent 在票没写清的地方自行做的决定。**每条都待人审**。

格式：票号 / 决定 / 被否决的选项 / 理由。三五行，不要长篇。

需要人改 `CONTEXT.md` 或 ADR 的，写进末尾的「提案」区，不要自己改。

---

<!-- agent 从这里往下追加 -->

### 01 / Tile 的内部表示私有

`Tile` 是 `[<Struct>]` 私有 record（`Kind: int` 0-33 + `Akadora: bool`），只能经 `Tile.parse` /
`Tile.tryCreate` / `Tile.ofKindIndex` 构造。
否决：公开 DU（`Suupai of Suit * int * bool | Jihai of ...`）、裸 int。
理由：`8z`、`3mr` 这类不存在的牌在类型层就不可表示；Shanten 要的 34 长计数数组直接取
`kindIndex`；Fable 侧结构体与 record 等价，无额外成本。代价是下游不能对 Tile 直接模式匹配，
要经 `Tile.suit` / `Tile.number` 取出再匹配。

### 01 / 花色与红宝牌的罗马字命名

花色 `Manzu` / `Pinzu` / `Souzu` / `Jihai`；红宝牌相关一律 `Akadora`（`isAkadora`、`akadoraKinds`、
错误 case `AkadoraNotAllowed`）；去红的函数叫 `deaka`。
否决：`Man/Pin/Sou/Honor`、`Red`、`normalize`。
理由：CONTEXT.md 没定义花色词，按 ADR-0001 取罗马字日麻术语；`deaka` 是 mjai / Mortal 生态的原词，
照抄可省一次翻译。见末尾提案。

### 01 / 记法解析取最严格的一种

不接受大写（`5M`）、不接受首尾空白（`" 5m"`）、不接受天凤式红五（`0m`）、不接受紧凑手牌写法
（`123m456p`）。手牌记法 = 空白或逗号分隔的 mjai token 序列，规范形 = 升序 + 单空格分隔。
否决：宽松解析 + 别名。
理由：ADR-0001 的要点是「数据层只有一种记法」；一旦宽松，第二种记法就会从 CLI 边缘渗回事件流与固件。

### 01 / 红宝牌的排序与牌种索引

排序上红宝牌紧跟同种正牌（`5m` < `5mr` < `6m`），`kindIndex` 与对应正牌相同。
理由：手牌形态判定一律先 `deaka`，索引同一才对得上 34 长计数数组；排序相邻则规范形对人眼友好。

### 01 / Thoth 只在引擎引 Core，后端由 dotnet 侧工程提供

引擎工程只引 `Thoth.Json.Core`（Fable 兼容的抽象层），`Thoth.Json.Newtonsoft` 由 CLI 与测试工程引。
否决：`Thoth.Json.Net`（dotnet-only）、`Thoth.Json` 10.x（旧的 Fable-only API）。
理由：Thoth v10 之后拆成 Core + 运行时后端，Core 是双目标唯一正确的引用点；M1 接 Fable 时只需在
Fable 工程加 `Thoth.Json.JavaScript`，引擎代码零改动。

### 01 / 引擎内部诊断文案用英文，中文只在 `toDisplay`

Decoder 的失败信息等引擎内部诊断串用英文；中文只出现在 `Tile.toDisplay`、`TileParseError.toDisplay`
这类渲染出口。理由：ADR-0001 要求人类可读形式隔离在渲染层，诊断串同样不该混进引擎。

### 01 / 「引擎不引入 Fable 不兼容依赖」做成可执行关卡

`scripts/ci.sh` 对 `Janpo.Engine.fsproj` 的 `PackageReference` 做白名单校验
（`FSharp.Core` / `Fable.Core` / `Thoth.Json.Core`），并禁止它引用其它工程。
理由：验收项写在票里会随时间腐坏，写成关卡则每次提交都在验。加包要连同一条决策记录一起改名单。

### 01 / 全仓 TreatWarningsAsErrors

`Directory.Build.props` 打开 `TreatWarningsAsErrors` 并追加 `--warnon:1182`（未使用变量）。
理由：13 张后续票要在同一份代码上叠加，警告堆积到最后没人清。

### 01 / nix 的 nixpkgs 源用 flakehub 的 nixpkgs-weekly

`flake.nix` 的 nixpkgs 输入是 `https://flakehub.com/f/DeterminateSystems/nixpkgs-weekly/*.tar.gz`。
否决：`github:NixOS/nixpkgs/nixos-unstable`。
理由：宿主机是 Determinate Nix，registry 里的 nixpkgs 已经是这份快照，复用缓存不必再拉一份；
它是纯 tarball URL，任何现代 Nix 都能取，flake.lock 已把版本钉死（dotnet-sdk 10.0.302 / uv 0.12.1）。

### 02 / 洗牌自带 xorshift32，不用 System.Random

`Rng` 是 `[<Struct>]` 私有记录（一个 `uint32` 状态），只用 32 位的位移与异或；`ofSeed` 避开 0
这个不动点并空转三步，`nextBelow` 用拒绝采样消掉取模偏差，`shuffle` 是 Fisher-Yates。
否决：`System.Random`（dotnet 与 Fable 两侧实现不同，跨目标不可复现）、带乘法的 SplitMix32
（JS 里 32 位乘法要走 `Math.imul`，多一层 Fable 语义假设）。
理由：「同种子同牌山」要在 dotnet 与浏览器两侧都成立，位移与异或是两边语义完全相同的最大子集。
`RngTests` 里钉了固定种子的取数序列与牌山头 6 张，M1 编到 JS 后这几行必须一字不差。

### 02 / 规则集叫 `Ruleset`，是公开记录 + 预设，不做校验类型

`Ruleset` 是公开记录（`SeatCount` / `TileKinds` / `CopiesPerKind` / `Akadora` / `HaipaiSize` /
`DeadWallSize` / `RinshanCount` / `StartingScore`），预设是 `Ruleset.yonma`，开关是
`Ruleset.withoutAkadora`。牌山构成由 `Ruleset.wallTiles` 算出（红宝牌**换**而不是**加**）。
否决：私有记录 + 每个字段一个 `with` 组合子（后面 6 张票要加规则开关，每加一个就要加一个组合子）；
`validate : Ruleset -> Result<...>` 与「已校验规则集」类型（会把 Result 推给所有下游）。
理由：不自洽的规则集只可能来自手搓，且开局这条路径已经在 `KyokuStartError` 里把它变成了值。
将来从 JSON 读规则时再加 `Ruleset.create : ... -> Result<Ruleset, RulesetError>` 是纯增量。

### 02 / Event 的形状：字段少的内联、字段多的另立记录载荷

`Event` 用内联具名字段（`Tsumo of actor: Seat * pai: Tile`）；字段多、或含多个同类型标量
（`kyoku` / `honba` / `kyotaku` 三个 int 挨着，位置传错编译器抓不住）的 case 另立记录载荷，
`StartKyoku` 就是。加一个 case 的代价固定为三处：DU、`encoder`、`decoder`；漏哪处编译器都会报。
否决：每个 case 都套记录（`Tsumo` 这类两字段的会被记录淹掉）、每个 case 都内联（`start_kyoku`
的 8 个字段位置化太危险）。case 名一律取 mjai 事件名转 PascalCase，不自创。

### 02 / Event 不设 `toDisplay`

事件是 wire 数据，中文由 UI 按事件结构自己拼。否决：给 `Event` 加渲染出口。
理由：那会让「加一个 case」的代价从三处变四处，而后面还有 12 张票在加 case；
且事件的人话形态高度依赖上下文（谁打的、第几巡），不是一个纯函数能拼好的。
错误类型仍然有 `toDisplay`（`KyokuStartError`），这条不变。

### 02 / 场风是 `Kaze` 类型，wire 上写 `1z`-`4z`

新增 `Kaze = Ton | Nan | Shaa | Pei`，`start_kyoku` 的 `bakaze` 用它。记法由序号推出
（`toMjai = string (index + 1) + "z"`），不另立对照表，因此不会与 `Tile.toMjai` 漂移。
否决：`bakaze: Tile`（`5m` 这种非风牌在类型层就能表示）、mjai 生态原生的 `E`/`S`/`W`/`N`
（ADR-0001 已明确否决那套记法，`EventTests` 里有一条测试钉住「不接受 E」）。
代价：与 mjai 生态的牌谱互转时，风牌记法要过一次映射——但牌本身已经是这个情况了。

### 02 / `Seat` 是 int 的透明别名

`type Seat = int` 加一个 `Seat` 模块（`all` / `orderFrom` / `next` / `isValid`，座位数都从
规则集读）。否决：私有包装类型（0-3 的索引要拿来 `List.item`、要直接进 mjai 的 `actor` 字段，
包装之后每处都要拆；13 张后续票天天用）。
理由：CONTEXT.md 把 Seat 定义成「0-3 的固定索引」，mjai wire 上它就是个 int。
将来真要收紧，改成私有 struct 是一次机械替换，别名让调用点全都已经写成了 `Seat`。

### 02 / 王牌的分法：岭上在前，之后每叠「表 + 里」

王牌 14 张按实际摆法拆成：前 `RinshanCount`（4）张岭上牌，其余每两张一叠
（上表宝牌指示牌 / 下里宝牌指示牌），共 5 叠。开局翻第一叠，杠一次多翻一叠（`revealIndicator`）。
位置在 `Wall.build` 时就定死，之后的访问器不必再带着规则集跑。
理由：这是日麻的实际摆法，也是「表里成对」这条在 08/09 算里宝牌时唯一需要的结构。

### 02 / 配牌按 4-4-4-1 的取牌手顺，且发完就排序

从 Oya 起按下家方向，三轮各取 4 张、最后一轮各取 1 张；每家发完的手牌按 mjai 顺序排序。
否决：每家取连续 13 张（统计上等价，但复盘时对不上真实手顺，要多一句解释）；保留发牌顺序
（顺序不携带任何信息，排序让固件与 diff 稳定）。摸切判定要的「刚摸进那张」由 04 的
GameState 单独记，不靠手牌顺序。

### 02 / `start_game` 由调用方产出，引擎只产 `start_kyoku` + `tsumo`

`KyokuStart.create` 产出的是一局的开局事件；`start_game` 是对局级事件，`names` 来自配桌，
由 CLI（将来是 05 的 Game 层）直接构造 `Event.StartGame names`。
理由：不想在 02 就替 05 定 Game 层的形状。构造一个无字段逻辑的事件不算「逻辑跑进 CLI」。

### 02 / 结构性概念用英文，规则性概念用罗马字

`Wall` / `DeadWall` / `Ruleset` / `Seat` 用英文，`Rinshan` / `Haipai` / `Kaze` / `Oya` /
`Honba` / `Kyotaku` / `Tehais` 用罗马字。
理由：CONTEXT.md 自己就是这么分的——它用 `Tile`（并明确 _Avoid_ `Pai`）、`Hand`（不是 Tehai）、
`Seat`、`Event`，而规则长尾一律罗马字。ADR-0001 反对的是把**日麻术语**意译（`AcceptanceTiles`），
不是禁止英文结构词。`Event` 的字段名另当别论：它跟 mjai wire 1:1，所以是 `tehais` 不是 `hands`。

## 提案（需人裁决，勿自行落地）

<!-- 涉及 CONTEXT.md / ADR 变更的提案写在这里 -->

### 提案 01-A：把牌相关的几个罗马字词补进 CONTEXT.md

01 票落地时用到、但术语表里没有的词：`Manzu` / `Pinzu` / `Souzu` / `Jihai`（花色）、
`Akadora`（红宝牌，术语表现有条目只在 Dora 里提了一句「含里宝牌与红宝牌」）、
`deaka`（去红）、`kindIndex`（0-33 的牌种索引）。建议在「牌与手牌」一节补齐，
否则后续票各写各的（`Honor` / `Red` / `tileId`）就会散掉。

### 提案 01-B：渲染出口的统一命名 `toDisplay`

ADR-0001 只举了 `Tile.toDisplay` 一个例子。01 票把它扩成了约定：
**所有产出中文的函数一律叫 `toDisplay`，集中在文件末尾的渲染段**（`TileParseError.toDisplay`、
`TileListParseError.toDisplay` 已照此落地）。建议把这句写进 ADR-0001 的 Consequences，
让「渲染层是单向出口」有一个可 grep 的判据。

### 提案 S-A（调度器）：引入 Ruleset（规则集），并重裁 Atamahane 的默认值

调研牌谱来源时发现，spec 与 CONTEXT.md 的两处规则假设与两大平台不符：

| 项 | 天凤 | 雀魂段位战 | 我们现在 |
|---|---|---|---|
| 双响 | あり | あり | 头跳**默认开** ← 与两家都相反 |
| 三响 | 三家和了 → 途中流局 | 三响成立、不流局 | 未覆盖 |
| 切上满贯 | なし | なし | 开关（原以为雀魂有，错） |
| 流し満貫 | あり | あり | **完全没提** |
| 连风牌雀头 | 4 符 | 未核 | 未定 |
| 国士暗杠抢杠 | 禁止 | 允许 | 未覆盖 |

出处：`tenhou.net/man/` 官方手册（「ダブロンあり」、途中流局列表含三家和了、「流し満貫あり」）、
riichi.wiki Tenhou rules、mahjongsoul.info 段位战规则表（「切り上げ満貫なし」「トリプルロンは流局なし」）、
天鳳雑スレ Wiki（「連風牌は4符」）、`smly/RiichiEnv` issue #43（国士暗杠抢杠）。
切上满贯那条是次级源，落地前再核一次。

**提案**：把这些开关打成命名规则集 `Ruleset`（`Tenhou` / `MahjongSoul` / 项目默认），
新增术语进 CONTEXT.md，并重裁 Atamahane 默认值（建议默认双响，与两大平台一致，头跳保留为开关）。
ADR 三条标准都过（难以回退：渗透点数与流局判定；无上下文时费解：为何默认双响；真实取舍：
对拍可行性 vs 竞技规则纯度）→ 建议立 **ADR-0004**。

已按此调整的票：08（切上默认关、符的规则集项）、11（国士暗杠抢杠）、12（三家和了 + Nagashi Mangan）、
13（对拍前按来源选规则集）。**CONTEXT.md 与 ADR 未动，留人裁决。**

### 提案 S-B（调度器）：三麻这道门今晚不做，但已按几处零成本参数化留缝

用户提出三麻是 bonus。调研结论：三麻不是一个开关，是整套规则集变体——
3 家 / 108 张（无 2-8m）/ 无红 5m / 禁吃 / 拔北（mjai 需 `nukidora` 扩展事件）/ 自摸损 /
半庄 6 局 / Mortal 官方不支持需 `libriichi3p` fork（证据：`Mateces/mortal-sanma` README 差异表）。

最要命一条：**三麻的向听数在 corner case 下与四麻不同**（`smly/RiichiEnv` issue #30）——
`1m3m` 在四麻是嵌张搭子，三麻永远补不上。这打的是 03 票，全引擎风险最高的模块。

已加的三处零成本参数化：02（座位数与牌山构成不写死字面量）、03（合法牌种集合作显式入参）、
05（局数序列从规则配置读）。**没有**预留 Nukidora 事件或抽象点数表——F# DU 加 case 时编译器
会把所有 match 点找出来，那是优点，提前抽象反而是投机性泛化。

若早上决定把三麻纳入范围，CONTEXT.md 的 `Seat`（现定义为固定索引 0-3）需要改。

### 提案 02-A：把开局这几个词补进 CONTEXT.md

02 票落地时用到、术语表里没有的词：`Ruleset`（规则集，提案 S-A 已在提它）、
`Wall`（牌山）与 `DeadWall`（王牌）、`Rinshan`（岭上牌）、`Haipai`（配牌）、
`Kaze`（风，含 `Ton`/`Nan`/`Shaa`/`Pei`）、`DoraIndicator`（宝牌指示牌，术语表现在只有 Dora）、
`Seed`/`Rng`（种子化发生器）。建议在「牌与手牌」与「引擎接缝」两节补齐。
另：CONTEXT.md 的 Seat 条目写死「0-3」，若将来纳入三麻需要改成「0 起的固定索引，上界由规则集给」。
