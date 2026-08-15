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
### 03 / 形态判定的输入类型叫 `HandShape`（暗牌 + 副露数）

Shanten / Ukeire / 和了型共用一个输入：`HandShape`（34 长计数 + `nakiCount` 0-4，构造时验张数与
「每种 ≤ 4」）。副露只贡献「已成面子数」，牌面不进形态判定（役与符是别的票）；杠也算一个面子。
否决：直接传 `Tile list * int`（两个参数容易传反，且 12 张这种非法手牌会漏进算法）；叫它 `Hand`
（CONTEXT.md 的 `Hand` 专指暗牌，不含副露，占了名字）。见末尾提案 03-A。

### 03 / 合法牌种集合做成 `TileKindSet` 显式入参

票要求「34 种全存在」不写死。落地为 `TileKindSet`（34 长存在标志），四麻传 `TileKindSet.fourPlayer`。
它在三处真正生效：搭子补不补得上（`1m3m` 缺 2m 就不是搭子）、七对子要求牌种 ≥ 7、
国士要求 13 种幺九牌齐全；另加「死张」判定（见下）也要读它。
内部仍走 34 长数组，`TileKindSet.legalFlags` 是 `internal` 快路径。

### 03 / `Shanten` 是私有 struct DU，-1 表示和了

`[<Struct>] type Shanten = private Shanten of int`，配 `value` / `isTenpai` / `isAgari` / `agari` / `tenpai`。
0 = Tenpai 由 CONTEXT.md 定死；-1 = 和了型是通行约定（与对拍用的 oracle 一致），CONTEXT.md 没写。
否决：裸 `int`（下游极易写成 `shanten <= 0` 把和了当听牌）。

### 03 / 「摸不到第 5 张」的两条修正

纯组合的向听公式会把两类手牌算少一向听，两条都落进了实现（并被 oracle 对拍与具名用例锁住）：
1. **孤张全是「四张全在手里」的牌种且无雀头** → +1。例：4 面子 + 第 4 张 6s 单骑，第 5 张 6s 不存在，
   要先打掉它才谈得上听牌，是一向听不是听牌。
2. **死张下界**：手里握满 4 张、且在该规则集下进不了顺子的牌种（四麻即字牌），每种至少吃掉一次替换；
   3n+2 的手牌本来就要打一张，白送一次。
两条都不是「实现细节」而是向听数的定义问题（「还需几次替换才能听牌」），oracle 同样这么算。
另外，「对子搭子」要求该牌种手里不足 4 张——4 张全在手里就永远变不成刻子。

### 03 / Ukeire 保留剩余 0 枚的牌种

`Ukeire.Tiles` 列出**形态上**能让向听下降的全部牌种，含已经全部可见（剩余 0 枚）的；
另给 `Ukeire.live` 过滤、`Ukeire.total` 求和。
否决：直接滤掉 0 枚（形态信息丢了，Assisted 档想说「你听的牌已经绝张」就说不出口）。
`visible` 参数是**手牌之外**的可见牌（牌河、副露、宝牌指示牌），手牌自己的张数由 `HandShape` 扣。

### 03 / 一般型的 case 名叫 `Standard`

`AgariShape = Standard | Chiitoitsu | Kokushi`。七对子与国士有罗马字原词，「一般型 / 四面子一雀头」
没有通行的罗马字短词（`Mentsute` / `Futsuu` 都不通用），取英文 `Standard`。
`classify` 返回**一组**型而不是一个：二盃口的手同时是一般型与七对子，07 票判役要用得上这个区分。

### 03 / oracle 对拍：固件提交进仓库，大批量对拍走脚本

oracle 是 PyPI 的 `mahjong==2.0.0`，`uv run scripts/oracle/shanten_oracle.py` 从零可复现。
`dotnet test` 读的是提交进仓库的 `tests/Janpo.Engine.Tests/fixtures/shanten-oracle.tsv`（4000 手，
`refresh-fixture.sh` 生成），所以 CI 不需要 Python，引擎依赖白名单也不受影响；
十万量级的现跑对拍走 `scripts/oracle/differential.sh`（经 `janpo shanten --batch` 管道）。
否决：测试里直接 shell 出去调 Python（`dotnet test` 从此要联网 + 装包，CI 不可重现）。

## 提案（需人裁决，勿自行落地）

<!-- 涉及 CONTEXT.md / ADR 变更的提案写在这里 -->

### 提案 01-A：把牌相关的几个罗马字词补进 CONTEXT.md

01 票落地时用到、但术语表里没有的词：`Manzu` / `Pinzu` / `Souzu` / `Jihai`（花色）、
`Akadora`（红宝牌，术语表现有条目只在 Dora 里提了一句「含里宝牌与红宝牌」）、
`deaka`（去红）、`kindIndex`（0-33 的牌种索引）。建议在「牌与手牌」一节补齐，
否则后续票各写各的（`Honor` / `Red` / `tileId`）就会散掉。

### 提案 01-B：渲染出口的统一命名 `toDisplay`
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

### 提案 S-C（调度器）：`Ruleset.TileKinds` 与 `TileKindSet` 是同一概念的两套表示

02 与 03 并行时各造了一份「本规则集里存在哪些牌种」：02 的 `Ruleset.TileKinds : Tile list`，
03 的 `TileKindSet`（内部 34 长存在标志数组，`internal` 快路径）。编译器不会抗议，但 04 起的每张票
都要同时面对两者。

集成时**没有**动它们——重新设计不是调度器的职权，而且 04 的 agent 需要一个不动的靶子。
已在派工时要求 04 用 `TileKindSet.ofTiles ruleset.TileKinds` 派生，**不许造第三份**。

早上可考虑：让 `Ruleset` 直接携带 `TileKindSet`（编译顺序已经为此留好——03 的模块排在 `Ruleset.fs` 之前）。

---

### 04 / `Ryuukyoku` 的拼法：标识符照术语表，wire 照 mjai

F# 标识符（`Event.Ryuukyoku`、载荷记录 `Ryuukyoku`、`GameState.ryuukyoku`）用 CONTEXT.md 的拼法
`Ryuukyoku`，JSON wire 上仍写 mjai 的 `"ryukyoku"`；荒牌流局的 `reason` 取 mjai 的 `"fanpai"`
（对应 DU case `Fanpai`）。
否决：case 名照 mjai 事件名逐字转 PascalCase（`Ryukyoku`）——那会让仓库里同时出现两种罗马字拼法。
理由：RUNBOOK 把 CONTEXT.md 定为「标识符命名的唯一权威」，而 mjai 只约束 wire；`Kaze` 已有先例
（wire 是 `1z`-`4z`，标识符是 `Ton`/`Nan`/…）。见提案 04-B。

### 04 / `Action` 与 `Event` 允许同名 case，靠 `[<RequireQualifiedAccess>]` 分开

`Action` 类型加 `[<RequireQualifiedAccess>]`：意图写 `Action.Dahai`，事实写 `Dahai`。
否决：给其中一边改名（`DahaiIntent`）——CONTEXT.md 明确「允许 case 同名」。
理由：不限定名的话后定义的那个会静默遮住先定义的，读代码时分不出「意图」还是「事实」。

### 04 / 摸切与手切是两个不同的动作，且会被校验

`Action.Dahai` 与 `Event.Dahai` 都带 `tsumogiri`（mjai 1:1）。合法动作集里，摸切一条（打刚摸进
那张）、手切每种牌各一条；`tsumogiri` 与实际不符返回 `TsumogiriMismatch`。
否决：由引擎从「打的牌是否等于摸进的牌」推断——两张同样的 5m 时推不出来，且 09 的「立直后只能
摸切」与 12 的 Nagashi Mangan 都要这个区分是**声明**而不是猜测。

### 04 / 合法动作集是 `LegalActions` 的列表，空列表 ⟺ Kyoku 已终

`LegalActions = { Seat: Seat; Actions: Action list }`，`GameState.legalActions : GameState ->
LegalActions list`（响应阶段会同时等多家，所以是列表）。每项的 `Actions` 非空；整个列表为空
当且仅当这一局已终（属性钉住）。
否决：`GameState -> Seat -> Action list`（调用方得先猜该问谁）。

### 04 / 阶段是类型，且自带自己的合法动作集

`Phase = AwaitingDahai | AwaitingResponse | Ended of Ryuukyoku`，前两者各带自己的动作集，
只能由 `GameState` 内部的私有构造器产出（算动作集与建阶段是同一处代码，不会漂移）。
**响应阶段本票恒为空**：`responsesTo` 返回 `[]`，引擎打完牌直接推进，不会停在那里；
06 / 10 / 11 往 `responsesTo` 里填东西，引擎自然就停得下来。
否决：本票干脆不建响应阶段的类型（票明确要求两个阶段用不同类型区分）。

### 04 / 海底 / 河底是 `GameState` 上的 `KyokuFlags`，不是阶段字段

`KyokuFlags = { Haitei: bool; Houtei: bool }`：摸到可摸区最后一张后 `Haitei`，把那之后的最后
一张打出去后 `Houtei`（也就是终局那一步）。09 的一发、11 的岭上与抢杠加在同一个记录里。
否决：挂在阶段上（每个阶段都要抄一份，且 07 判役时要按阶段分情况取）。

### 04 / 听牌料是规则集字段 `NotenBappu = 3000`，算法照 mjai

不听的家平摊付 `NotenBappu`，听牌的家平分收；全听或全不听不授受。整数除法与 gimite/mjai 的
`3000 / tenpai_ids.size` 一致（3000 对四麻的 1/2/3 与三麻的 1/2 都整除）。
否决：把 3000 写死在结算函数里（02 已立「规则开关进 `Ruleset`」）。

### 04 / 家状态叫 `PlayerState`，河叫 `Kawa`；`GameState` 自己带事件流

`PlayerState`（CONTEXT.md 的词）= 手牌 + 河 + 点数 + 「刚摸进的那张」；副露与立直 / 振听标志由
10 / 09 加字段。河用罗马字 `Kawa`（Genbutsu / Furiten / Nagashi Mangan 都以它为准，是规则性概念）。
`GameState` 内部保存本局事件流（倒序），`GameState.events` 取正序——它就是这一局的 Paifu，
回放确定性的属性直接比它。见提案 04-A。

### 04 / 选手抽象与随机选手放引擎，不放 CLI

`Player<'player> = 'player -> GameState -> LegalActions -> Action * 'player`（纯函数，选手状态显式
穿过去）、`Kyoku.run`、`Kyoku.randomPlayer` 都在引擎里。
否决：随机 bot 只写在 CLI（14 票的 soak 与本票的属性测试都要它，写 CLI 里就得抄第二份）。
LLM 与真人坐席是 Agent 层的事（它们的决策要 await），引擎侧只有这个同步形态。

### 04 / `KyokuStart` 加一个 `Tsumo` 字段

`GameState.start` 需要知道「Oya 摸进的是哪张」才能建摸牌后阶段；从 `Events` 里反解要多一条
不可能分支。否决：从 14 张手牌里减去 13 张配牌反推（更绕，且 `Hands` 已排序）。

### 提案 04-A：把摸打循环的几个词补进 CONTEXT.md

本票落地、术语表里没有的词：`Kawa`（河）、`NotenBappu`（听牌料 / ノーテン罰符）、
`Haitei` / `Houtei`（海底 / 河底，术语表只在 Ippatsu 条目里顺带提过）、`Phase`（阶段）、
`Tsumogiri`（摸切）/ `Tedashi`（手切）。建议分别补进「牌与牌河」与「引擎接缝」两节。

### 提案 04-B：把「标识符照术语表、wire 照 mjai」写进 ADR-0001

ADR-0001 只说了记法（`1z` vs `E`）。罗马字**拼法**同样会分叉：mjai 写 `ryukyoku`，术语表写
`Ryuukyoku`。建议在 Consequences 补一句：wire 上的字符串一律照抄 mjai，F# 标识符一律照 CONTEXT.md，
两者不一致时由编码器承担映射（`Kaze`、`Ryuukyoku` 已经是这样）。

### 07 / 「已分解的和了形」是三个新类型，`HandShape` 与 `AgariShape` 一字未动

`Naki`（副露的**内容**）→ `AgariHand`（暗牌 + 副露 + 和了牌 + 自摸/荣和）→
`MentsuBreakdown`（4 个 `Mentsu` + 雀头 + `WaitKind`）。形态判定仍然走
`AgariHand.toHandShape` + `AgariShape.classify`，不重复一份分解规则。
否决：往 `HandShape` 里塞副露内容（03 的产出正被 04 用，且 Shanten 不需要牌面）。

### 07 / `Naki` 是私有记录 + `NakiKind`，字段贴 mjai wire（10 / 11 复用）

`Naki` 私有记录 `{ Kind; Taken: Tile option; Consumed: Tile list; Target: Seat option }`，
对应 mjai 的 `pai` / `consumed` / `target`；构造子 `pon` / `chi` / `ankan` / `minkan` /
`kakan`（`kakan` 吃一个已成的 `pon`，因此加杠必然记得原碰的来源，11 的责任支付要用）。
红宝牌在副露里**保留**（要计番），`Naki.mentsu` 出来的 `Mentsu` 代表牌才去红。
否决：公开 DU（不能带校验）、只存 `Tile list`（10/11 产事件时还得反推谁点的）。

### 07 / 多种面子分解：全枚举 + `candidates` 排序，`detect` 取第一

`MentsuBreakdown.enumerate` 穷举「雀头 × 面子拆法 × 和了牌落点」并去重
（`111222333m` 出三刻与三顺两种，`2m2m2m3m4m` 和 2m 出单骑与两面两种）。
`Yaku.candidates` 把每种读法（含七对子 / 国士）各判一遍，按
「役满倍数 → 番数（含宝牌）→ 近似符」降序排；`Yaku.detect` 取第一个。
否决：只取第一种分解（二盃口会被判成七对子、三暗刻会被判成一杯口）。

### 07 / 同番时的 tiebreak 用一个私有的「近似符」函数

高点法要求同番取高符，但符是 08 的。折中：`fuLikeness` 只算**随分解而变**的三项
（面子基本符、雀头役牌符、听牌型符），不含门清荣和 / 自摸这类常数项，也不含连风牌雀头
（规则集相关）。它是私有的、只用于排序；08 可以拿 `Yaku.candidates` 按自己的符规则再选一次。

### 07 / `Yaku` / `NakiKind` / `YakuValue` / `RiichiDeclaration` 一律 `[<RequireQualifiedAccess>]`

`Yaku` 有 43 个 case，`Chiitoitsu` / `Kokushi` 与 `AgariShape` 撞名、`Tenhou` 与将来的规则集
预设撞名、`NakiKind.Pon` 与 10/11 要加的 `Event.Pon` 撞名。全部限定访问，写 `Yaku.Chiitoitsu`。
否决：改名躲开（`YakuChiitoitsu` 这类前缀比限定访问更难读）。这是对 01「类型与同名 module 同文件」
约定的一个补充，不是偏离。

### 07 / 不做双倍役满；但十三面 / 单骑 / 纯正九莲各占一个 case

天凤官方手册与 riichi.wiki 的 Tenhou rules 都写明：役满**复合**有，**双倍役满没有**
（四暗刻单骑、国士十三面、纯正九莲、大四喜都是单倍）。13 票的对拍源就是天凤牌谱，
因此 `YakuValue.Yakuman` 的倍数恒为 1，复合靠多个役各占一倍。
`KokushiJuusanmen` / `SuuankouTanki` / `JunseiChuuren` 仍是独立 case——天凤的役表把它们
分开列，13 票比对役种集合时要对得上；将来要开双倍役满，只需改 `Yaku.value` 一处。

### 07 / 食断做成 `Ruleset.Kuitan`（动了 02 的 `Ruleset.fs`）

SPEC 的规则开关只有「红宝牌有无、食断有无」，02 的决策也写明「后面 6 张票要加规则开关」，
因此在 `Ruleset` 上加 `Kuitan: bool`（默认 true）与 `Ruleset.withoutKuitan`，不另立全局开关。
这是本票唯一一处改到别的票的文件，改动是纯增字段。

### 07 / 绿一色不要求发；混老头与混全带幺九互斥

绿一色按通行规则**不**要求必须有发（天凤同）。「每一块都带幺九」且**没有顺子**时只计混老头，
不再计混全带幺九 / 纯全带幺九（天凤的役表就是这么互斥的）。有字牌计混全带幺九，无字牌计纯全带幺九。

### 07 / `Yaku.name` 是稳定的罗马字标识符，不是渲染层

`Yaku.name : Yaku -> string`（`riichi` / `yakuhai:5z` / `bakaze:1z` / `junsei_chuuren`）供
`janpo yaku` 输出与 13 票对拍用；中文仍然只在 `Yaku.toDisplay`。它与 `Tile.toMjai` 同类：
是数据的稳定写法，不是给人看的形式。

### 07 / 没做的役与原因

流し満貫（12 票的流局范畴）、人和（非通行役，SPEC 的「古役与地方役」排除）、
双倍役满（见上）、役满的包牌与点数（08 / 11）。符与点数全部不在本票，`YakuTally`
只到「役 + 番 + 宝牌 + 选中的分解」为止。

## 提案（需人裁决，勿自行落地）

### 提案 07-A：把和了形相关的罗马字词补进 CONTEXT.md

07 落地时用到、但术语表里没有的词：`Mentsu`（面子）与 `Shuntsu` / `Koutsu` / `Kantsu`
（顺子 / 刻子 / 杠子）、`Jantou`（雀头）、`Menzen`（门清）、`Yakuman`（役满）、
`Han`（番）、`Kuitan`（食断）、`WaitKind` 的五种听牌型（`Ryanmen` / `Penchan` / `Kanchan` /
`Shanpon` / `Tanki`）、`RiichiDeclaration`。术语表现在只有 `Yaku` 与 `Fu` 两条，
08（符）与 10/11（副露）会继续用这批词，建议在「规则判定」一节补齐。

### 提案 S-C（调度器）：`Ruleset.TileKinds` 与 `TileKindSet` 是同一概念的两套表示

02 与 03 并行开发，各自独立造了「这个规则集里存在哪些牌种」的表示：

- 02：`Ruleset.TileKinds : Tile list`（记录字段）
- 03：`TileKindSet`（私有类型，内部 34 长存在标志数组，`internal` 快路径；`ofTiles` / `fourPlayer`）

编译器不会抗议，两者也不冲突——但它们是同一个领域概念。集成时**没有合并**，因为
重新设计不是集成该做的事，且 04/07 票正在并行开发，接口不能变。

已给 04 票的指令：用 `TileKindSet.ofTiles ruleset.TileKinds` 派生，**不许造第三份**。

**提案**：把 `Ruleset` 的字段直接改成 `TileKindSet`（编译顺序已在集成时安排好——
`TileKindSet.fs` 排在 `Ruleset.fs` 之前，正是为这个可能性留的），或明确记录
「`Ruleset.TileKinds` 是配置面，`TileKindSet` 是计算面」这一分工。二选一，别放着不管。

这条是并行开发的可预期产物：无人值守下两个 agent 看不见对方，语义重复只能在集成时发现。

---

### 06 / 头跳裁决的是**实际宣言**，不是「谁有资格宣言」

Ron 进入**每一个**够格座位（不振听 + 和了型成立）的合法动作集；`Atamahane` 在**收齐全部答复之后**
才裁决，取打牌者下家优先的第一个**宣言者**。因此优先的那家见逃时，靠后的那家照样成立。
否决：只把 Ron 放进头跳赢家的动作集（那会把「见逃」这条真实规则做没了——真实牌桌上四家同时宣言，
头跳只在已宣言者之间裁决）。
出处：gimite/mjai `lib/mjai/active_game.rb` 的 `process_hora(actions)` 同样是
`actions.sort_by { distance(a.actor, a.target) }`，且遍历全部 hora 动作（双响 / 三响）。

### 06 / 响应阶段逐家答复、收齐再裁决；「过」是一个动作，且不产出事件

`AwaitingResponse` 多了两个字段：`Responses`（**还没答复**的座位，答一家少一项）与
`Declared`（已宣言的动作，按答复顺序）。`Responses` 空了才裁决。`Kyoku.run` 因此不必改：
它每次问第一个待答座位，答复累积在局面里。**先被问到不等于优先**。
`Action.None`（mjai 的 `none`）不产出任何事件——mjai 的 wire 上没有 `none` 事件，它只是一次答复；
收齐最后一份答复的那一步才由裁决产出事件（`hora` / `tsumo` / `ryukyoku`）。
04 的属性「合法动作集里的每个动作都推得动局面」相应改成「局面必变 + 事件流按返回值增长 +
**当且仅当这一步收齐了答复**才产出事件」——比原来强（原来只要求事件非空，没要求局面真的变）。

### 06 / 振听：永久是**重算**的，同巡是**置位 / 摸牌解除**的

`Furiten = { Permanent: bool; Doujun: bool }` 挂在 `PlayerState` 上。
`Permanent` 在每次自家打牌后按「当前听牌 × 自己的河」**重算**（换听到不含自己打过的牌上就解除，
这是通行规则）；`Doujun` 在见逃一次可以荣和的牌时置位，在自己下次摸牌时解除。
两者都只挡荣和，从不挡自摸。否决：合成一个 bool（两条解除条件完全不同）。
09 的立直振听（立直后见逃 = 闩死，不可被重算解除）由 09 自己决定是加一位还是让重算跳过立直座位——
06 不替它预设。

### 06 / 振听座位的 Ron 不进合法动作集，因此没有「振听」这个拒绝理由

振听在 `responsesTo` 里就被滤掉（票的明文要求）。于是振听座位提交 Ron 得到的是 `NotYourTurn`
（引擎确实不在等它答复），而不是一个专门的 `FuritenRon`。
否决：加一个 `FuritenRon` 的 `IllegalAction`——它在 `step` 里永远不可达，是死代码。

### 06 / 终局形态换成 `KyokuEnd` DU，连庄判定是它的一个函数

`Phase.Ended of KyokuEnd`，`KyokuEnd = Hora of Hora list | Ryuukyoku of Ryuukyoku`（限定名）。
`GameState.ryuukyoku` 语义不变（不是流局收尾就是 None），新增 `GameState.kyokuEnd` / `GameState.horas`。
连庄判定落成 `KyokuEnd.isRenchan : Seat -> KyokuEnd -> bool`（Oya 和了 / 流局时 Oya 听牌），
05 读这一个布尔值；Honba 递增、Kyotaku 结转、局数序列仍归 05。
否决：`GameState.isRenchan`（要多传一个 Oya，且与 `kyokuEnd` 是两个入口）。

### 06 / 荣和的那张牌留在放铳者的河里，不进和了者的手牌

牌数守恒（04 的属性）要求每张牌只在一处。因此荣和后和了者仍是 13 张，自摸和了者是 14 张；
04 的属性「等着打牌的那家 14 张」相应扩成「等着打牌的那家 + 自摸和了的那家 14 张」。
mjai 的 `hora_tehais`（亮手，含和了牌）是渲染 / 牌谱字段，07 / 08 要时再加。

### 06 / `hora` 事件带符 / 番 / 和了点三个字段，本票一律记 0

wire 形态照 gimite/mjai 的 `hora`：`actor` / `target` / `pai` / `fu` / `fan` / `hora_points` /
`deltas` / `scores`。**没有**带 `yakus`（07）、`uradora_markers`（09）、`hora_tehais`。
本票 `Fu = Fan = HoraPoints = 0`、`Deltas` 全 0、`Scores` = 和了前的点数；本场与供托的计入同属 08。
注：Mortal / libriichi 的 `hora` 事件更瘦（只有 actor / target / deltas / ura_markers），
13 票对拍时比对的是「役种集合 / 符 / 点数」而不是事件逐字段相等，不受影响。

### 06 / 黄金用例摊牌山：引擎给测试留两个 `internal` 构造器

`Wall.ofOrdered : Ruleset -> Tile list -> Wall`（不洗牌，按给定顺序摆）与
`GameState.startFrom : Ruleset -> KyokuContext -> Wall -> Result<GameState, KyokuStartError>`，
都是 `internal`，经 fsproj 的 `InternalsVisibleTo` 给测试工程。测试固件按 `Wall.deal` 的
4-4-4-1 手顺**反推**牌山，因此黄金用例走的仍是生产的发牌与开局路径。
否决：公开一个 `GameState.ofHands`（生产 API 从此多一个只有测试用的入口）；
碰运气找种子（构造不出「指定和了在指定 Junme 发生」）。

### 06 / 三响：头跳关掉时三家都成立

`ronWinners` 对宣言者数目不设上限，头跳关掉时几家宣言就成立几家（票的验收「关闭时双响/三响都成立」）。
天凤把三家和了判成途中流局——那是 12 票的规则集字段（提案 S-A 已记），06 不提前替它决定。

### 提案 06-A：把和了与振听的几个词补进 CONTEXT.md

本票落地、术语表里没有的词：`Hora`（和了，术语表只有 Yaku / Fu，没有和了本身）、
`Minogashi`（见逃，`PlayerState.minogashi`）、`Doujun`（同巡，术语表在 Furiten 与 Junme 条目里
提过「同巡」但没给罗马字）、`KyokuEnd`（终局形态）、`Renchan`（连庄，术语表在 Oya 条目里
提过中文「连庄」没给词）。另：振听「永久」那一位现在叫 `Furiten.Permanent`（英文），
因为「永久振听」没有通行的罗马字短词——若术语表想统一，请一并裁。
