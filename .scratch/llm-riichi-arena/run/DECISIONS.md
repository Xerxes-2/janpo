# 夜间自主决策记录

无人值守跑批期间，agent 在票没写清的地方自行做的决定。**每条都待人审**。

格式：票号 / 决定 / 被否决的选项 / 理由。三五行，不要长篇。

需要人改 `CONTEXT.md` 或 ADR 的，写进末尾的「提案」区，不要自己改。

---

<!-- agent 从这里往下追加 -->


> **本文件已由调度器整理**：去掉 8 个重复小节（三方合并的产物），并把提案分成
> 「已裁决」与「待裁决」两堆。决策记录部分原样保留。


## 待你裁决的东西，一共在三个地方

| 在哪 | 是什么 | 数量 |
|---|---|---|
| **本文件 §三.1** | 术语表增补——都是「某票用到但 `CONTEXT.md` 没有的罗马字词」 | **10 条，建议一次性批量处理** |
| **本文件 §三.2** | 其余待裁：`04-B`（ADR-0001 缺「wire 与标识符分离」一条，已核实）、`12-C`、`13-C` | 3 条 |
| **`reports/*.md` 的「留给人的待审项」一节** | 各票实现细节的取舍，agent 挑了最保守的一种并说明理由 | **17 份报告都有** |

第三处最容易漏——它们在 DECISIONS 里是以**决策**（已做了选择）的形式记录的，不是提案，
所以不在 §三。典型如 `16-B`（明杠后没打牌又杠一次时欠账 ≥2 的排法，语料零出现取最保守解）。

另有两条不在上表、但要你拍板的：
- **15 票该不该关掉** —— 它的立票依据（`tenpaiDahai` 2.1 ms）被 17 票的剪枝改成约 0.3 ms
- **`docs/research/` 的三份报告**（听牌边界、`searchStandard` 五方向、libriichi 对比）里各有结论建议

---

# 一、决策记录（agent 自主决定，供审阅）

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

### 08 / 符与点数是两个文件、两个纯函数模块：`Fu` 与 `Score`

`Fu.fs`（符：私有记录 + 分项，`total` 未切上 / `value` 切上）与 `Score.fs`（点数表与授受：
`HoraValue` = 符+番+役满倍数、`HoraTransfer` = 谁和了谁点的几本场几供托、`HoraScore` = 级别+和了点+
四家增减、`HoraReading` = 选中的读法+符番）。两者都排在 `Yaku.fs` 之后，只有 `GameState`
一处消费它们。
否决：把符与点数塞进 `Yaku.fs`（那文件已经 900 行，且符与役是两个概念）；
把授受直接写进 `GameState.applyHora`（点数表就没法单独对拍，13 票要的就是它）。

### 08 / 规则集新增 5 个字段：切上满贯、连风牌雀头符、岭上自摸符、本场点、立直棒点

`KiriageMangan = false`（**默认关**：天凤与雀魂段位战都不采用，提案 S-A）、
`DoubleKazeJantouFu = 4`（天凤）、`RinshanTsumoFu = true`（天凤加自摸符）、
`HonbaPoints = 300`、`RiichiStick = 1000`。开关组合子只加了 `Ruleset.withKiriageMangan`
（SPEC 点名的规则开关），其余三项是数值，用例直接 `{ ruleset with ... }`。
理由：票明确要求「符的规则集相关项显式可配、不写死」；`Kyotaku` 记的是**根数**，
换算点数必须有 `RiichiStick`，而 09（立直）此刻还没落地，由本票先立。

### 08 / 连风牌雀头的字段名取 `DoubleKazeJantouFu`（英汉混拼，待裁）

「連風牌」没有通行且无歧义的罗马字短词（れんふうはい / れんふぉんぱい 两读都在用），
按 02 的「结构性概念用英文」取 `DoubleKazeJantouFu`；`RiichiStick`（立直棒）同理。
否决：`RenfonpaiJantouFu`（拼法要靠猜）、`RenpuuJantouFu`。见提案 08-A。

### 08 / 和了点的三条通行规则照做，各自记在实现里

1. **平和自摸恒 20 符**（不加自摸符），**副露的平和形按 30 符算**（合计 20 符时补 10）；
2. **七对子恒 25 符**，门清荣和与自摸都不加、也不切上；
3. **每一笔支付各自切上到 100**（不是最后合计切上），子荣和 4 倍基本点、亲 6 倍，
   子自摸「亲两份子一份」、亲自摸「三家各两份」。
数え役满定在 13 番（天凤）。这三条 spec 与 CONTEXT.md 都没写，取最通行 / 天凤的一种。

### 08 / 供托进和了者的 `Deltas`，因此一次和了的增减之和 = 供托点数而不是 0

`Hora.Deltas` 含本场与供托（06 的字段注释说「和为 0」，本票改成「和 = 供托」）。
**双响时本场与供托只归排在最前的那一家**（头跳的顺序，天凤规则）。
`Hora.Scores` 逐条累加，最后一条就是这一局的最终点数。
**留给 05 的接口**：和了之后 `Kyotaku` 归零（点数已经发到和了者手里了），流局时结转——
05 只需读 `KyokuEnd`，不要再给和了者补一次立直棒。

### 08 / 役满的 `Hora.Fan` 记 13 × 倍数

mjai 的 `fan` 是个 int，役满没有番。记 13（数え役满同值，点数一致因此不丢信息），
而不是记 0（牌谱与 UI 上「役满 0 番」会误导）。点数一律由 `Score` 按 `Yakuman` 倍数算，
不从 `Fan` 反推。

### 08 / 高点法按**点数**排，不是按番数排；07 的近似符只用来兜同键

`Score.best` 把 `Yaku.candidates` 的每一种读法按真符算一遍，取 `(基本点, 番, 符)` 最大的那种。
排序键用基本点是因为番高不一定点数高（`3 番 70 符` 封顶满贯 8000 > `4 番 30 符` 7700）。
**顺带的发现**：07 的 `fuLikeness` 与真符的差只有两处——门清荣和 / 自摸这类**常数项**
（对同一手牌的所有读法一样，不影响排序）与**连风牌雀头**（同一手牌的不同读法不可能有不同雀头，
除非同一种字牌有 3 张以上，而那时它只能是刻子）。因此实践中两种排序不会分叉；
真正的分叉来自上面那条「番 vs 点数」的排序键。属性 `best 的点数 ≥ 每一种读法的点数` 钉住了它。

### 08 / 「无役不可和」接在 `GameState.horaOf` 这一处，自摸与荣和共用

新增公开函数 `GameState.horaOf : Seat -> GameState -> Result<HoraReading, YakuError>`：
和的是哪张、自摸还是荣和由**当前阶段**决定（摸牌后阶段 = 刚摸进那张的自摸，响应阶段 = 刚打出那张的荣和）。
合法动作集（`responsesTo` 的 Ron 与 `awaitingDahaiActions` 的自摸和）、`step` 的校验与和了结算
读的都是它，**判据只有这一份**。
否决：只在 `responsesTo` 里判（`step` 的自摸和分支会绕过去，04 的属性「接受一个动作当且仅当
它在合法动作集里」立刻破）。
连带新增 `IllegalAction.NoYaku of Seat * Tile`；按裁决 D-3，它与 `YakuError.NoYaku` 的
**全部使用点都显式限定**（含 07 的 `Yaku.fs` 与 `YakuTests.fs` 里原本不限定的 16 处）。

### 08 / 判役上下文由局面自己填：`GameState.yakuContextOf`

场风取自开局条件、自风由 `Seat.jikaze`（新增）推出、宝牌指示牌取自牌山、海底河底取自 `KyokuFlags`、
**天和 / 地和由「这家还没打过牌」从事件流推出**。09（立直 / 一发）与 11（岭上 / 抢杠）在这一个函数里接上。
否决：让调用方各拼一份（UI、CLI、结算三处会漂移）。
**留给 10 / 11**：地和还要求此前无人鸣牌，副露落地时这里要加一条；
构造 `AgariHand` 只有 `PlayerState.agari` 一处，副露也只需改那里。

### 08 / 06 的三个剧本里有两手牌「型成立但无役」，本票给了它们役

`ronFuritenScript` / `doubleRonScript` / `tripleRonScript` 里，座位 2 的
`1m1m1m 9m9m + 234s 678s 2p3p` 与座位 3 的 `1s1s1s 7s7s + 234m 678m 3p5p` 荣和时**一个役都没有**
（不成平和——有刻子 / 嵌张；不成断幺九——带幺九牌）。无役不可和一接上，06 的 17 条用例全红。
处置：把这两手改成平和形（座位 2 的 `111m` → `345m`；座位 3 的 `1s1s1s + 3p5p` → `3s4s5s + 5p6p`，
顺带成了断幺九），**听牌张、巡目、振听与头跳的断言一字未改**。
这不是「改期望值迎合实现」——那两手牌在真实规则下压根不能荣和，是固件本身不合法。

### 08 / 没做的：包牌与责任支付、跨局的点数推进

大明杠的责任支付（11 票）与大三元 / 大四喜的包牌（同）**不在本票**。
挂点已经留好：`Score.hora` 只认 `HoraTransfer`（谁和了、谁点的、亲是谁、本场与供托），
责任支付要做的是**在算出 `HoraScore` 之后改写 `Deltas`**——
即给 `HoraTransfer` 加一个 `Sekinin: Seat option`（或让 11 在 `Score` 里加一个
`Score.sekininBarai : HoraScore -> ... -> HoraScore`），
`Fu` / `basePoints` / `limit` 一行都不必动。跨局的 Honba 递增与 Kyotaku 结转仍归 05。

### 05 / 一整场对局是 `GameState` **之上**的一层：`Game`

`Game = private { Ruleset; Played: GameState list; Progress: GameProgress }`，
`GameProgress = NextKyoku of KyokuContext | Ended of GameResult`（限定名，`Phase` 里已有一个 `Ended`）。
`GameState` 一字未改语义，仍然只管一局。
否决：让 `GameState` 跨局（会毁掉「一局」这条清晰的边界，票明文禁止）；
在驱动里就地推进而不立类型（那样 M1 的 Host UI 与 14 的 soak 各要自己拼一份局与局之间的规则）。

### 05 / 「一局的结局 → 下一局的场况」做成纯函数 `Game.after`

`Game.after : Ruleset -> KyokuContext -> KyokuEnd -> int list -> GameProgress`，
`Game.advance : GameState -> Game -> Game` 只是它加一次记账。
理由：推进规则（连庄 / Honba / Kyotaku / 局数序列 / 终局）的黄金用例可以拿**合成的** `KyokuEnd`
直接验，不必去碰运气找「亲刚好听牌的种子」。`scores` 显式传（取 `GameState.scores`，
不取 `Hora.Scores`）：局面才是点数的权威，事件字段的填法归 08。

### 05 / 连庄判定不重写，收敛在 06 的 `KyokuEnd.isRenchan`（备注 N-2）

本层从头到尾没碰 `Ryuukyoku.Tenpais`，也没再比一次 Hora 的 `Actor` 与 Oya。
判定留在 `GameState.fs`（它只需要「结局 + 亲」，是 `KyokuEnd` 的拆解）；
消费它的规则（Honba/Kyotaku/序列/精算，都要 `Ruleset` 与跨局上下文）落在 `Game`。
否决：把 `isRenchan` 搬进 `Game`（会让 06 的 `HoraTests` 反过来依赖 05 的文件）。

### 05 / Honba：连庄 +1、流局 +1、**只有「和了进局」才归零**

东1局0本场荒牌流局且亲不听 ⇒ 东2局**1**本场，这是通行规则；票里「进局且非连庄时归零」
按此读作「Ko 和了才归零」。否决：流局进局也归零（与通行规则不符，13 票对拍必红）。

### 05 / Kyotaku：流局原样结转、和了清零；**把点数加给和了者是 08**

本层只管「场上还剩几根」。08 的授受把 `Kyotaku × RiichiBou` 计入和了者的 `Deltas`，
两边合起来总点数才守恒。M0 里 09 未落地、供托恒为 0，因此这条暂时是平凡的，用例用合成场况验。
**给 09 的接口债**（同样写在 `Game.after` 的文档注释里）：立直棒是局内产生的，
09 落地时要把结转的来源从 `context.Kyotaku`（局初）换成「这一局终了时场上实际还剩几根」。

### 05 / 局数序列 = `GameLength.bakazes × [1 .. SeatCount]`，终局条件是「序列走完」

新类型 `GameLength = Tonpuusen | Hanchan` 与 `Ruleset.Length` / `Ruleset.kyokus`；
四麻东风战 4 局、四麻半庄 8 局、三麻半庄 6 局全是推出来的。**连庄也不延长**：
东 4 局打完就终局（票明文不做西入 / 延长），因此天凤的アガリやめ / 西入这一族全部不实现。
`Ruleset.yonma` 的长度预设取 `Tonpuusen`（M0 的验收与 CLI 默认都是东风战），见报告的待审项 2。

### 05 / 终局精算：供托归头名，顺位同点时起家方向在前的靠前

`Game.settle : Ruleset -> int -> int list -> GameResult`，`GameResult = { Scores; Juni }`。
顺位按精算前的点数排（把供托给头名不可能改变名次顺序，两种算法同解）。
同点由座位号决定（起家是座位 0），这是通行做法。
新增 `Ruleset.RiichiBou = 1000`（供托记的是根数，换算成点数要它；09 扣立直棒也用它）。

### 05 / 不新增错误 DU：`Game.advance` 的退化输入定义为「原样返回」

`Game.run` / `Game.play` 复用 04 的 `KyokuError`。`advance` 收到一局还没打完、
或这场对局已终，都原样返回（没有事情发生，不是错误），并写进文档注释。
否决：为这两种情形立一个 `GameAdvanceError`（驱动路径永远走不到它；且裁决 D-3 之后
每多一个错误 DU 就多一次跨层同名的风险）。

### 05 / `end_kyoku` / `end_game` 两个事件，wire 上都不带字段

照 gimite/mjai 与 libriichi（`libriichi/src/mjai/event.rs` 里两者都是 unit variant，
往返就是 `{"type":"end_kyoku"}` / `{"type":"end_game"}`）。终局精算不进 wire：
它由事件流 fold 得出（ADR-0002），不另存一份。
`Game.events` **不含** `start_game`——它的 `names` 来自配桌不是引擎（02 的决定），仍由 CLI 拼在最前。

### 05 / 测试对 08 天然免疫：点数只以不变量入断言（裁决 D-4）

没有一条断言写死引擎算出的点数。三类断言：总和守恒（`Σ 点数 + 供托点数 = SeatCount × StartingScore`）、
结转与归属（下一局的局初点数 = 上一局终了时的点数；供托流局结转 / 和了清零；Honba 与 Oya 的变化）、
序关系（顺位是 `1..SeatCount` 的排列，点数高的靠前）。用例里出现的点数全是用例自己喂进去的输入。
「一局**之内**」的总和不变没有写进本票的属性：09 之前引擎里没有局内增加供托这回事，
硬写会在 09 落地时红给别人看；它是 08 的验收项与 14 的 soak 全集。

### 13-prep / 牌谱取样走 HTTP Range 局部取 zip，不下整包

上游按年打包（最小的 `2009.zip` 32MB，`2026.zip` 58MB）。取样脚本只 Range 取 zip 的中央目录 +
抽中的成员，58MB 的包实际下行 ~3.5MB，落到 `data/paifu/`（已 gitignore）。
否决：整包下载再抽（夜里无人值守，磁盘与带宽都不该按 GB 花）；用 `git clone` 上游仓库（那里只有转换器，
数据在 release 里）。样本：2026-01 的 60 局（66,264 事件行 / 652 kyoku）+ 2009 的 8 局（只为验 schema 是否同）。

### 13-prep / 额外配一份天凤官方 JSON 作 oracle，与 mjai 流按局对齐

上游 mjai 数据集是给 Mortal 训练用的瘦身版：`hora` 只有 `actor/target/deltas/ura_markers`，
`ryukyoku` 只有 `deltas`——**13 票要对拍的役种、符、番、和了点、流局形态全部缺失**。
因牌谱 id 就是文件名，同一局可从 `https://tenhou.net/5/mjlog2json.cgi?<id>` 取回天凤官方 JSON，
里面有 `["和了", deltas, [和了家, 放铳家, 包牌家, "40符2飜700-1300点", "立直(1飜)", …]]`。
两者按第 k 个 `start_kyoku` ↔ `log[k]` 对齐，177 局的 `(bakaze,kyoku,honba,kyotaku)` 与 deltas 全等。
否决：降级到 `fstqwq/mjlog2mjai` 自己转（读过其 `parse.py`，产出的 `hora` 同样瘦身，解决不了缺口）；
直接取 mjlog XML（`tenhou.net/0/log/?id=` 实测 404，要账号态）。

### 13-prep / 固件按「最小字节的集合覆盖」挑，18 局而不是 20 局

目标是覆盖样本里出现过的全部 75 项特征（事件类型 × 流局形态 × 役 × 符档 × 点数档 ×
双响/西入/本场/供托/里宝牌枚数）且总量 < 1MB。随机重启的贪心集合覆盖给出 11 局 697KB 的全覆盖，
再按最小字节补到 18 局 / 177 kyoku / 987,413 字节。第 19、20 局会超 1MB，故停在 18。
否决：随机抽 20 局（大明槓在 60 局样本里只出现 1 次，随机抽必漏）。

### 13-prep / 字牌记法映射放 13 票的适配器，不放宽 `Tile.parse`

样本用 mjai 生态原生记法 `E/S/W/N/P/F/C`（`bakaze` 亦然），我们只认 `1z`-`7z`。
决定：映射写在 13 票自己的牌谱读入侧，`Tile.parse` 一字不动。
否决：让 `Tile.parse` 兼收字母记法。理由：ADR-0001 与 01 票的决策都写死了「数据层只有一种记法」，
一旦宽松，第二种记法会从牌谱读入渗回事件流与固件。同理建议 13 另立只读的 `PaifuEvent`，
不要把 `Event` 的必填字段改成可选（那会毁掉「引擎产出的事件必然完整」这条不变量）。

### 10-A / 食替：现物与筋都禁，且不可配

`4m 5m` 吃 `3m` 之后既不能打 `3m`（现物）也不能打 `6m`（筋）；碰只有现物一种。
天凤与雀魂都是两者全禁，票里没写，按 RUNBOOK「取最贴近通行规则的那一种」。
否决：只禁现物（关西一些规则）、加 `Ruleset` 开关（M0 没有需要它的用例；真要加只改 `kuikaeKinds` 一处）。
判据只有一份 `GameState.kuikaeKinds`，被合法动作集、候选过滤与 `step` 三处共用。

### 10-B / 河底牌不能鸣

可摸区空了之后打出的那张（河底牌）只进 Ron，不进 Pon / Chi：鸣完没牌可摸，这一局到此为止。
通行规则如此，票里没写。实现：`responsesTo` 里 `Wall.remaining > 0` 才给鸣牌。
属性「响应阶段出现 Pon / Chi ⟹ 可摸区非空」钉住它。

### 10-C / 被鸣走的牌仍留在打牌者的河里，只加一个布尔记号

`PlayerState.KawaTaken: bool`（只置位不清除）。牌不从 `Kawa` 里拿掉——**振听看的是「自己打过什么」**，
与那张牌后来被谁拿走无关；拿掉它会静悄悄地放宽振听。
否决：把牌移出河（破坏振听）、给每张打出的牌一个标记（12 票的 Nagashi Mangan 只需要「有没有被鸣过」）。
连带：牌数守恒的表述改成「手牌 + 河 + 副露里自家亮出的 `consumed` + 山」，`Naki.taken` 已算在河里。

### 10-D / 鸣走一张本可荣和的牌算见逃；鸣牌不解除同巡振听

宣言荣和以外的任何答复（「过」或鸣牌）都让能荣和的那家进同巡振听。
且 `PlayerState.addNaki` **不清** `Furiten.Doujun`——同巡振听的定义是「到自己下次摸牌为止」，
鸣牌不摸牌。否决：把鸣牌当作「这家的一巡到了」而解除。
两条都往严的方向走：只会多挡荣和，绝不会放过非法的。若要改，改动点各一行。

### 10-E / 新增四个 `IllegalAction`，并让 `step` 对打牌也校验合法动作集成员

`Kuikae`（牌在手里但这一手不许打）、`NakiTileMismatch`（鸣的不是刚打出的那张）、
`CannotNaki`（这几张此刻鸣不成）、`RonWhileFuriten`（型成立有役但振听）。
`RonWhileFuriten` 是被迫的：**振听只挡荣和不挡鸣牌**，因此振听座位现在会因为能碰能吃而被问到，
它在那里宣言荣和时报 `NotAgari` 是错的。`stepAwaitingResponse` 的 Ron 拒绝理由因此按 `horaOf`
细分成 `NotAgari` / `NoYaku` / `RonWhileFuriten` 三种。
名字都查过没有跨层同名（裁决 D-3 的教训）：`Kuikae` 与私有函数 `kuikaeKinds` 不冲突。

### 10-F / 鸣完会没牌可打的亮法不进合法动作集

四副露只剩两张、且两张都被食替封住时（`4s 5s` 吃 `3s` 而手里只剩 `3s 6s`），这一鸣就不合法。
否则响应阶段会推进出一个**没有合法动作**的局面，把「合法动作集非空 ⟺ 这一局未终」直接推倒。
通行规则也是「鸣了会无牌可打就不许鸣」。实现：`nakiActionsFor` 的 `usable` 过滤。

### 10-G / `GameState.junme seat` = 该家自己摸过几次牌

有副露的局里「四家各打一张为一巡」不再齐步，只能按各家自己的摸牌数算：
**鸣的那家 Junme 不涨**（它只是把出手时机提前了），**被跳过的那几家更不涨**（连牌都没摸到）。
否决：按打牌数算（鸣完那一手会让它虚涨一巡）。09 的一发窗口若要用巡目，读这一个函数。

### 10-H / 一发的打断入口：`GameState.interruptIppatsu`（恒等变换，09 填）

任何鸣牌都打断全场一发。立直与一发是 09 的事，此刻没有标志可清，因此函数体是 `state`。
**全局唯一调用点是 `applyNaki`**（碰 / 吃现在走它，11 的三种杠也会走它）。
09 只需在函数体里把各家一发标志置 false，不必再找调用点。
否决：等 09 自己去找所有鸣牌路径（那正是这种标志漏置的典型来源）。

### 09-A / 立直棒在**宣言牌落定**时才出，不在宣言时出

票里写的是「宣言时的扣点与入供托」，同一张票又要求产出 `reach_accepted`。两者只能取一个时机：
真实牌谱里 491 次 `reach` 对 479 次 `reach_accepted`，差的 12 次正是**宣言牌被荣和**——
那时立直不成立、立直棒不出（天凤与 mjai 都如此）。因此扣点与供托 +1 都挂在 `acceptRiichi`。
否决：宣言即扣点（宣言牌被荣和时要退钱，多一条回滚路径，且与牌谱对不上）。
`RiichiState` 因此有三段（`None` / `Declared` / `Accepted`），`Declared` 就是这两步之间那一段。

### 09-B / 立直宣言是**独立动作**，宣言牌是紧接着的另一手 `Dahai`

`Action.Riichi of actor`，宣言之后阶段不变（还是那家的摸牌后阶段），只是合法动作集收窄成
「打完仍听牌的那几张」。这与 mjai 的消息序列一致（`reach` 之后才是 `dahai`）。
否决：`Action.Riichi(actor, pai, tsumogiri)` 一步到位（合法动作集会炸成「立直 × 每种打法」，
且与 mjai / 牌谱的两条事件对不上，13 票要为它写一层转换）。

### 09-C / 立直宣言与打出宣言牌之间，连自摸和也不许

摸到和了牌时自摸和与立直同时在动作集里（打掉它仍听牌）；**选了立直就不能反悔去和**。
`step` 对 `Declared` 状态的 `Action.Hora` 返回 `RiichiRestricted`。
否决：允许（那等于让「宣言」这一步可撤销，而 `reach` 事件已经产出去了）。

### 09-D / 立直后的振听：`refreshFuriten` 对立直中的座位**只置位不清除**

06 把这个选择留给了 09（加一位 还是 让重算跳过）。取后者：`Furiten` 记录不动，
`PlayerState.minogashi` 在立直中把 `Permanent` 也置起来，`refreshFuriten` 里写成
`hit || (立直中 && 原来就永久)`。理由：立直后手牌不再变，重算除了把「立直后见逃」冲掉之外
没有任何别的作用（见逃的那张在别人河里，重算看不到它）。
否决：给 `Furiten` 加第三位（06 的两位各有明确解除条件，第三位没有自己的解除条件，是伪装成状态的常量）。

### 09-E / 立直棒的跨局结转改读 `GameState.kyotaku`（`Game.advance` 一行）

`Game.after` 原本结转的是**局初**的 `KyokuContext.Kyotaku`，那里不含本局打出去的立直棒——
有人立直又流局时那几根会凭空蒸发。05 自己在注释里留了这一句给 09。改动只在 `Game.advance`：
把传进 `after` 的 context 的 `Kyotaku` 换成 `GameState.kyotaku state`，`after` 的三条规则一字未动。
和了收走的供托同样改读 `state.Kyotaku`（含刚打出去的那几根，也含立直者自己那一根）。

### 09-F / `Hora` 事件补 `uradora_markers`（04 在类型注释里把它记在 09 名下）

只在**和了者立了直**时非空（`Yaku` 也只在那时计里宝牌的番），否则空表——与 mjai 一致。
wire 名取 mjai 官方规格的 `uradora_markers`；13 票那批牌谱写的是 `ura_markers`，
它本来就要为字牌记法写一层适配器（备注 N-5 的 A1/A2），多映射一个键不增加工作量。
否决：跟数据集写 `ura_markers`（裁决 D-1 说的是「mjai 原拼」，官方规格才是那个原拼）。

### 09-G / 立直后的暗杠判据（裁决 D-8 切给 09 的那条）：三条全满足才许

`RiichiState.allowsAnkan kindSet state naki hand drawn kind`：① 杠的必须是刚摸进的那张（禁送り杠）；
② 杠掉那四张之后听的牌种与立直时完全一致；③ 立直时的手牌在**每一种**和了读法里都把它当暗刻用。
第三条不是摆设：搜索到的反例 `123m 66m 333s 44s 555s` 摸 `3s`——杠前杠后都听 `6m / 4s`（②过），
但和 `4s` 时索子能读成 `345s 345s 345s`，那读法里 `3s` 不是暗刻，所以杠不得。
**不许自己往动作集里加暗杠动作**（D-8）：这里只有判据与它的单元测试，动作是 11 票的。

### 09-H / 立直宣言的「牌山剩余 ≥ 4 张」按**座位数**读

`remaining >= ruleset.SeatCount` 而不是字面量 4：这一条的规则原意是「够全场再摸一圈」，
三麻是 3。`Ruleset` 是座位数的唯一出处（01 的约定），引擎里不再出现 4 这个字面量。

### 09-I / 新增两个 `IllegalAction`：`CannotRiichi` 与 `RiichiRestricted`

`CannotRiichi(actor)`（此刻立直不了）与 `RiichiRestricted(actor, pai)`（立直把这一手封住了：
只能摸切 / 宣言牌要保持听牌 / 宣言后不许自摸和）。后者是被迫的：立直后打错牌时**牌就在手里**，
落到原来的兜底分支会被报成 `Kuikae`（食替），那是错的诊断。
名字都 grep 过没有跨层同名（裁决 D-3 / 备注 N-7 的教训）。

### R-1 / 调研：03 的三条「听牌边界」改动全部正确，04/06/09/12 不需要改

完整调研见 `docs/research/shanten-tenpai-boundaries.md`（出处、引文、复现步骤都在里面）。
本条只记结论与它逼出来的三条建议。

**三条改动的裁定**

1. **面子也受 4 组上限约束 —— 正确。** 纯形态问题，与规则集无关。它是**唯一**能翻转「和了型」
   判定的一条（撤掉它，`2m2m2m 7m8m9m 4p6p 1s1s1s 6s6s6s` 会被判成和了）。
2. **孤张全是「四张全在手里」的牌种、无雀头时 +1 —— 正确，且天凤取的正是这一边。**
   天凤手册明文：「形式聴牌あり。5枚目を待つ聴牌あり(手牌＝純手牌＋副露手牌として
   **純手牌で４枚使用していなければ成立**)」。129 179 个 kyoku 的真实牌谱里找到 2 例
   暗牌握满四张的「等第 5 张」流局手，天凤都按不听收了罚符——与引擎一致。
3. **字牌死张是向听下界 —— 正确，但它根本不碰听牌边界。** 40 万手对照实验里它
   **一次**都没翻转过听牌或和了判定（只改 ≥1 向听的数值），另有结构性论证。
   **它被标成「需人确认，因为改变了听牌的边界」是误判，可以直接放行。**

**核心问题的答案：纯空听（純カラ）在天凤算听牌、收听牌料、也能立直。**
天凤规则清单里「形式聴牌あり」之外没有任何「待ちが全部見えている場合は除く」的排除项。
实测：342 个纯空听座位在荒牌流局被判听牌，307 个收到正听牌料、35 个在全员听牌局，
**0 个被判不听**；93 838 次立直宣言里 13 次是在待牌已 0 枚时宣言的，天凤全部受理。
引擎因为 `Shanten` 的签名里根本没有「可见枚数」，必然判听牌 —— 与天凤一致。
**所以 04 的听牌料与 12 的流局判定都不是错的。**

**验证强度**（都在仓库外做，没碰任何 `.fs` / `scripts`）
- 把 `Shanten.fs` 逐行移植成带开关的 Python，用仓库固件校准到 0 差异，再逐条撤销开关做对照。
- 第三方独立复核：`xiangting`（理论正确性有证明的实现）+ `mahjong==2.0.0`，
  在 tomohxx 穷举出的全部 7 个病态型、三麻、6 万手随机上三方 0 差异。
- 真实牌谱：从仓库固件的同一上游取全量 `2026.zip`（12 188 局鳳南喰赤，129 179 kyoku），
  逐座位对拍 `PlayerState.isTenpai` 与天凤自己的清算，**72 204 个座位判定 0 处不一致**
  （需先剔掉 17 局流し満貫——它的 deltas 是满贯档而非罚符档，12 票会用到这条）。

**三条建议（都不是修 bug）**
- **CONTEXT.md 的 `Tenpai` 条目**补一句口径：只看手牌形态、不看场上剩余枚数（纯空听算听牌）；
  唯一例外是**暗牌**握满 4 张的牌种不能当待ち（副露与杠出去的牌不计入暗牌）。见提案 R-A。
- **ADR-0004 加一行已知规则分歧**：「5 枚目を待つ聴牌」天凤只看暗牌、EMA 竞技规则看整手牌含副露
  （规则书 §3.3.8 例 1 与天凤明说的形完全相反）。引擎取天凤口径，且因为 `HandShape` 只有
  `nakiCount`、没有副露牌面，**当前结构上无法配置成另一侧**。实测影响 4 / 72 204 座位。见提案 R-A。
- **13 票加一条独立锚点断言**：荒牌流局局逐座位对拍 `isTenpai` 与天凤清算结果。
  现有测试的期望值是照我们自己实现写的、oracle 固件也是同源的 `mahjong` 库，
  两者互相印证而非独立验证；真实牌谱是唯一的外部锚点（仓库那 18 局里有 15 局可判定、60 个座位）。

**顺带记两条给别的票**
- 11 票（杠）：天凤把**暗杠也算「副露手牌」**——杠掉 `8888m` 之后 `7m9m` 仍算听 `8m`。
  牌谱里 2 例，天凤都付了听牌料。引擎把杠牌移进 `Naki`、`HandShape` 只数暗牌，建模正好对。
- `Ukeire.live` 为空 **≠ 不听牌**（纯空听正是这种）。若有人图省事拿它当听牌判据，
  04 与 09 会一起错，而现有固件与用例里没有纯空听样本、抓不到。
  建议在 `Ukeire.live` 的注释里写死「不得用于听牌判定」，或在 `UkeireTests.fs` 补一条纯空听用例。

### 11-A / 三种杠的标识符照术语表，wire 照 mjai：`Minkan` ↔ `daiminkan`

`Action` / `Event` 的 case 是 `Ankan` / `Kakan` / `Minkan`（CONTEXT.md 的 Naki 条目就是这三个词，
`NakiKind` 也早就是这么拼的），JSON wire 上是 mjai 原拼 `ankan` / `kakan` / `daiminkan`，
另有独立的 `dora` 事件。
否决：case 名照 wire 写成 `Daiminkan`（仓库里会同时出现 `NakiKind.Minkan` 与 `Event.Daiminkan` 两种拼法）。
理由：裁决 D-1 已经定死，与 `Riichi`/`reach`、`Ryuukyoku`/`ryukyoku` 同一处理。

### 11-B / 新宝牌的翻开时机：暗杠先翻后摸，明杠先摸后翻（固件 18/18 实测）

固件 `tests/fixtures/paifu/mjai` 里 18 条杠**无一例外**：
`ankan → dora → tsumo`，`kakan`/`daiminkan → tsumo → dora`。实现照抄这个顺序。
**已知偏离**：教科书上「明槓のカンドラは打牌後にめくる」，据此大明杠后的岭上开花不该吃到新宝牌；
本实现里明杠的 `dora` 落在 `tsumo` 与 `dahai` 之间，因此那种和了**吃得到**新宝牌。
否决：为明杠加一个「待翻」标志把翻开推迟到打牌之后（多一处状态、且产出的事件流与我们唯一的
对拍源不同形）。这条要人裁：13 票对拍时若在明杠局上出现符点差，先查这里。

### 11-C / 杠后王牌恒 14 张、可摸区少一张（票里「王牌张数正确减少」的落实方式）

`Wall.drawRinshan` 取走一张岭上牌的同时**把可摸区的最后一张补进王牌**——这是日麻的实际摆法，
效果是「海底往前挪一张」。因此断言写成：王牌恒 `DeadWallSize` 张、可摸区每杠少一张、
可用的杠次数减少（上限 `RinshanCount`），而不是「王牌张数减少」。
否决：不补充、让王牌真的少一张（那样牌会凭空蒸发，牌数守恒这条最值钱的属性就废了）。

### 11-D / 抢杠做成响应阶段的一种「起因」，宣言杠这一步不改局面

`AwaitingResponse` 加一个 `Cause: ResponseCause`（`Dahai` / `Kan of Naki`）。宣言暗杠 / 加杠时
**只产出事件、不动手牌与副露**，能荣和那张的家先答复；没人抢，那个杠才在 `applyKan` 里成立。
否决：先把杠落地、被抢时回滚（多一条回滚路径）；另立一个 `AwaitingChankan` 阶段
（响应阶段的收答复、见逃振听、头跳裁决要整套重写一遍）。
mjai 的事件顺序也是「先 `kakan` 后 `hora`」，被抢的那个杠在牌谱里照样留一条记录。

### 11-E / 暗杠与加杠只在「自己刚摸完牌」那一手宣言得了

判据是 `PlayerState.drawn` 非 None：鸣完那一手没摸牌，杠不了。
证据：固件里 18/18 条 `ankan`/`kakan` 都紧跟在自家的 `tsumo` 之后。
（`RiichiState.allowsAnkan` 对没立直的家恒为 true，因此这一层必须由 11 自己守。）

### 11-F / 责任支付挂在 `HoraTransfer.Sekinin`，范围按天凤

选了 08 给的第一个挂点（给 `HoraTransfer` 加 `Sekinin: Seat option`），**不改 `Fu` / `basePoints` /
`limit`，也不改 `HoraPoints`**——包只改「谁付」。三条分担：自摸由责任者一家付光；荣和且放铳者
就是责任者照常付；荣和且另有其人则两家各半（切上到 100，役满的点数恒能整除）。
**本场恒由放铳者付**（自摸时由付和了点的那几家平摊），供托照旧归和了者。
范围：大明杠后的岭上开花、大三元 / 大四喜由副露凑齐；**四杠子没有**（天凤）。
判定在 `GameState.sekininOf`：前者读倒序事件流里最近那条杠事件（**不能读「最后一组副露」**——
加杠是原地换掉那组碰，排在它后面的副露仍旧靠后），后者读副露序列里第三组三元牌 / 第四组风牌的来源座位。

### 11-G / `Wall.revealIndicator` 换成 `Wall.reveal`，返回**新翻开的那张**

`dora` 是独立事件、要带 `dora_marker`，而原来的 `revealIndicator` 翻满之后静默不动，
调用方分不出「翻了」与「没翻」。新的 `reveal : Wall -> (Tile * Wall) option` 两者都说得清。
没有生产调用点，只改了 WallTests 的两处。

### 11-H / 一局最多杠几次 = `Ruleset.RinshanCount`；杠数暴露成拆解器给 12 票

`GameState.kanCount`（全场）与 `PlayerState.kanCount`（逐家）。合法动作集在
`kanCount < RinshanCount` 且可摸区非空时才给杠。**四杠散了不在本票**：12 票读这两个拆解器
（「四个杠且不是同一家」才流局）。

### 11-I / `Naki.fromKawa` / `Naki.fromHand`：加杠的 `consumed` 里含着原碰被鸣的那张

mjai 的 `kakan` 是 `pai`（加上去的那张）+ `consumed`（原碰的三张），而原碰被鸣的那张**仍在打牌者的河里**。
牌数守恒若照旧只数 `Naki.consumed`，那张会被数两遍、而加上去的那张一遍都不数（红 5 时连多重集都对不上）。
因此给 `Naki` 加两个互补的拆解器，并把 `Naki.kakan` 的「`Consumed` 第一张恒是原碰被鸣的那张」写成注释里的不变量。

### 11-J / 国士抢暗杠做成规则集字段 `KokushiAnkanChankan`，默认 false

天凤禁止、雀魂允许（提案 S-A、`smly/RiichiEnv` issue #43），默认值跟天凤（我们的默认规则集与
对拍源都是天凤）。开关组合子 `Ruleset.withKokushiAnkanChankan`。加杠不受它影响：加杠恒可抢。

### 11-K / 属性的取样里加了**摊好的杠剧本**，不只靠随机取样

「见杠就杠」的选手跑四个种子只有一局杠得成（杠要手里四张同种，概率就那么小）。
因此 `GameStateArbitraries` 里加了三条摊好的杠轨迹（一局三个暗杠、一局大明杠后岭上开花、
一局暗杠后岭上开花），并把「那些轨迹里真的有杠」钉成一条用例。
这是备注 N-8 的同一课：没断言过覆盖率的属性只证明了没崩，没证明跑到过。

### R-6 / `YakuError.NotAgari` → `NoAgariShape`，并给 `YakuError` 加 `RequireQualifiedAccess`

裁决 D-3 只把 8 处使用点显式限定，两个层仍各有一个 `NotAgari`。现在形态那条改名
`YakuError.NoAgariShape`（与 `AgariShape` 术语对齐），类型加 `[<RequireQualifiedAccess>]`。
`IllegalAction` **保持不限定**：它的 case 是「拒绝理由」，`NotAgari(actor, pai)` 与
`NotYourTurn` / `NotInHand` 同一风格。
否决：两边都限定（读拒绝理由时要多写一截前缀）、把 `IllegalAction.NotAgari` 也改名（它没歧义了）。
理由：不限定的 `NotAgari` / `NoYaku` 从此只可能是 `IllegalAction` 的，读代码不用停。零语义变化。

### R-1 / `Atamahane` 默认翻成关（双响成立），`withoutAtamahane` → `withAtamahane`

ADR-0004 决定 3：默认值对齐天凤鳳凰卓。`Ruleset.yonma.Atamahane` 从 `true` 改成 `false`；
开关组合子的语义随之反转（默认已经是关，留一个「关掉」的组合子没有意义）。
否决：留 `withoutAtamahane` 作 no-op 别名（读的人会以为默认是开的）。
`RulesetTests` 里新增两行把新默认值钉死（`Assert.False(...Atamahane)` + `withAtamahane` 打得开）。
测试期望值的改动见 `reports/morning-rulings-R1-R4-R5-R6.md`，一条用例都没删。

### R-4 / `Ruleset.TileKinds` 的类型换成 `TileKindSet`，并删掉 `GameState.KindSet`

ADR-0004 决定 4。字段名不变、类型从 `Tile list` 变成 `TileKindSet`，在规则集构造时派生一次。
`wallSize` / `wallTiles` 改用 `TileKindSet.count` / `TileKindSet.kinds`（两个 API 都已存在，没新增）。
顺带删掉 `GameState.KindSet`——它当初就是「每次 `ofKinds` 太贵」的缓解措施，规则集自己带了之后
它是同一份东西的第二个表示，正是 ADR 要消掉的那种重复。`awaitingDahaiActions` 的 `kindSet` 形参
也一并去掉（同一个函数已经收着 `ruleset`）。
否决：加一个 `Ruleset.kindSet` 派生函数而保留 `Tile list` 字段（那就是两份表示，ADR 明确否掉）。
封装未破：`TileKindSet` 仍是私有 record，`legalFlags` 仍是 `internal`。

### R-5 / `Seat` 换成 `[<Struct>]` 私有 record，构造经函数

`type Seat = int` 改成 `[<Struct>] type Seat = private { Index: int }`（与 `Tile` 同一形状）。
构造只有三条路：`Seat.ofIndex : int -> Seat option`（外来裸整数，**负数不是座位**）、
`Seat.first` / `Seat.all` / `Seat.orderFrom` 这类枚举、`Seat.shimocha` 这类相对位置。
另有 `internal ofIndexUnchecked` 给引擎内部与测试固件（经既有的 `InternalsVisibleTo`，
与 `Wall.ofOrdered` 同一道口子）。
否决：单 case DU（`Seat of int`，模式匹配会把裸 int 又漏出来）、公开的可变构造。
理由：`kyoku` / `honba` / 点数 / 番 / 符全是挨着的同类型标量，透明别名下传错位置编译器不吭声——
这正是 02 票给 `StartKyoku` 立记录载荷的理由（备注 N-4 也印证过 F# 类型级检查的价值）。

### R-5 / `Seat.next` 改名 `Seat.shimocha`，相对位置补齐三个

一个名字一件事（裁决 D-6 的同一条）：座位序上的「下一个」就是下家，两个名字不留。
新增 `kamicha`（上家）、`toimen`（**返回 option**：三麻没有对家）、`distanceFrom`
（相对第几家，「打牌者下家优先」这条裁决顺序就是它）、`orderAfter`（从打牌者下家起绕一圈）、
`wrap`（任意整数折进合法座位，属性测试取样用）、`first`（起家）。
另加「每家一项」列表的三个拆解 `tryItem` / `mapAt` / `indexed`——点数、Deltas、配牌、
`Players` 都是这个形状，过去散着写 `List.tryItem seat xs` / `List.mapi (fun seat -> ...)`。
`shift` 是私有的，**全模块只有它一处取模**。

### 12 / 流局的判据单独成文件 `Ryuukyoku.fs`，是纯函数

途中流局的四条判据、三家和了、流し満貫的成立与清算，全部放进新的 `Ryuukyoku.fs`
（`PlayerState.fs` 与 `GameState.fs` 之间，「一局之内的状态机」组），输入 `Ruleset` +
`PlayerState list`，输出 `RyuukyokuReason option` / `Seat list` / `int list`，一个字段都不改。
否决：塞进已经 1700 行的 `GameState.fs`（那几条判据能单独对拍，也能被黄金用例直接调）；
放进 `Event.fs` 的 `RyuukyokuReason` 模块（那个文件在 `PlayerState.fs` 之前，看不见家状态）。

**F# 的坑**：同名的 `type Ryuukyoku` 在 `Event.fs`，跨文件同名要手写
`[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]`（同文件才自动加）。
叫法不变，只是 IL 里多个后缀。

### 12 / `RyuukyokuReason` 的 wire 取值取自 mjai 的参考实现，不是它的 wiki

gimite 的 mjai wiki 页只举了 `fanpai` 一个例子，Mortal 的 `libriichi` 把 `reason` 整个丢了。
七个取值取自 gimite `mjai` gem 的 `lib/mjai/active_game.rb`（`process_ryukyoku` / `process_fanpai`）：
`fanpai` / `nagashimangan` / `kyushukyuhai` / `sufonrenta` / `sukaikan` / `suchareach` / `sanchaho`。
标识符按术语表拼作 `Fanpai` / `NagashiMangan` / `KyuushuKyuuhai` / `SuufonRenda` / `Suukaikan` /
`SuuchaRiichi` / `SanchaHora`——**七个里有五个与 wire 不一致**，全在 `toMjai` 一处映射（裁决 D-1）。

### 12 / 规则集新增 `SanchaHoraRyuukyoku`（默认 true）与 `KyuushuKinds`（默认 9）

三家和了是不是途中流局：天凤是（默认，ADR-0004 决定 3），雀魂三响成立不流局
（`Ruleset.withoutSanchaHoraRyuukyoku`）。九种九牌的种数做成字段是因为 ADR-0004 决定 1
禁止散落规则字面量，且十种十牌是真实存在的变体。
**四杠散了没有新字段**：它读 `Ruleset.RinshanCount`（一局最多能杠几次），
两个数在任何规则集里本来就相等——岭上牌用完就杠不动了。

### 12 / 途中流局一律连庄，靠 `RyuukyokuReason.isAbortive` 而不是 `Tenpais`

`KyokuEnd.isRenchan`（06 的）加了一支：途中流局直接 true。
理由：途中流局压根不验听牌，`Tenpais` 记的是「谁亮了手牌」（mjai 参考实现的语义），
拿它判连庄会把九种九牌判成进局。荒牌流局与流し満貫仍旧看 Oya 听不听牌。
否决：给途中流局的 `Tenpais` 填 true 去迁就 `isRenchan`（那是在 wire 上撒谎）。

### 12 / 流し満貫的清算直接调 `Score.hora`，不另写点数表

`HoraValue = { Fu = 0; Han = 5; Yakuman = 0 }`（5 番即满贯档）+ `Actor = Target`（自摸）+
本场供托都为 0 → Oya `+12000 / −4000 ×3`、Ko `+8000 / −4000(Oya) / −2000 ×2`，
与 mjai 参考实现逐项相同，且引擎里不出现 `4000` / `8000` 这类字面量。
多家同时成立逐家累加（和恒为 0）。它**替代**听牌料清算（`exhaustiveDraw` 里一个 match，不叠加）。

### 12 / `HoraTests` 的「三响都成立」改用雀魂规则集，断言一字未动

默认规则集把三家和了判成途中流局（天凤，ADR-0004），那条用例因此改成显式
`Ruleset.withoutSanchaHoraRyuukyoku` 并改了标题；天凤那一边（三响 → 流局）在 `RyuukyokuTests` 里另有一条。
**这是本票唯一改动既有断言语义的地方**，两边的行为都有用例钉着，一条用例都没删。
同批：`GameStateProperties` 的「听牌料」属性加了一个 `Fanpai` 的形态守卫——听牌料只是荒牌流局的事。

### 13 / 牌谱侧另立四个只读类型，全在测试工程，引擎一个字段没加

`PaifuEvent`（瘦身版事件）/ `OracleYaku`（日文役名）/ `PaifuDiff` + `KyokuReplay` / `OracleHora` 等
全部落在 `tests/Janpo.Engine.Tests/`，因为重放入口 `Wall.ofOrdered` 与 `GameState.startFrom`
是 `internal`（`InternalsVisibleTo Janpo.Engine.Tests`），本来就只能在那里调。
否决：把牌谱读入放进引擎（M1 要过 Fable，引擎不该背测试数据的格式）；改 `Event.decoder` 收瘦身版
（会让「引擎产出的事件必然完整」这条不变量失效，票里明确禁止）。

### 13 / 字牌字母记法只做「记法 → 记法」的改写，不碰 `Tile.parse`

`PaifuNotation.toMjai`：`E S W N P F C` → `1z`-`7z`，改写完再交给引擎的 `Tile.parse` / `Kaze.parse`。
否决：给 `Tile.parse` 加一支认字母（ADR-0001 定的是内部单一规范形、边界处适配；放宽会让第二种记法
从边界渗回事件流与牌谱）。有一条断言钉着「`Tile.parse "P"` 仍然是 Error」。

### 13 / 样本量用环境变量 `JANPO_PAIFU_DIR` 扩大，默认是随工程分发的 18 局固件

票要求「样本量可通过参数扩大，不改代码」。语料目录下有 `mjai/` 就够跑，
`tenhou/<id>.json` 有就当 oracle 用、没有就只对拍动作序列与钱流——**独立锚点不依赖 oracle**
（它读的是 mjai 自己的 `ryukyoku.deltas`）。取样脚本 `scripts/paifu/fetch-corpus.py`。
理由（备注 N-19）：固件 4 秒，200 局 54 秒；CI 只跑固件，大样本手动开。

### 13 / 修了一处真 bug：立直宣言牌换了听牌，振听没被解除

`PlayerState.refreshFuriten` 的闩死判据 `RiichiState.isActive` → `isAccepted`（1 个标识符）。
`isActive` 对 `Declared` 也为真，于是「振听时立直、用宣言牌换掉听牌」这个常见手筋被闩死，
引擎从此不给那家荣和。真实牌谱实证 2/2110 kyoku（天凤让荣和成立）。
立直**成立之后**照旧只置位不清除（那之后手牌不再变），`minogashi` 一字未动，536 个测试全绿、
一条既有断言都没改。**修它是因为它不需要改任何既有期望值**——这条界线同时也是不修提案 13-B 的理由。

### 16 / 提案 13-B 落地：明杠的新宝牌欠到打牌那一刻才翻

`GameState` 加一个字段 `PendingKanDora: int`（明杠欠着还没翻的指示牌张数）：
`completeKan` 的明杠那一支只加一、不翻；`applyDahai` 开头 `revealPendingKanDora` 一次翻光并归零，
`dora` 排在 `dahai` 之前——**整条事件流的顺序与修改前一字不差**，变的只是哪一次 `step` 吐出它。
否决：（a）在 `KyokuFlags` 里挂标志（那份记录每次迁移重算，欠账要跨迁移留着）；
（b）改事件顺序把 `dora` 挪到 `dahai` 之后（牌谱里 87 次都是 `tsumo → dora → dahai`）。
理由：13 票 218 局实证；扩样本对拍 8 处差异归零，不引入新差异。

### 16 / 改了 `KanTests` 两条既有断言的期望值 + 一条测试改名（票明确授权）

`大明杠：先补摸岭上牌再翻新宝牌` → 改名 `……新宝牌欠到打牌那一刻才翻`，事件模式去掉 `Dora`
并**接着断言下一步打牌吐出它**（原名字说的就是被证伪的那条规则，留着名字会说谎）；
`加杠：原来那组碰**原地**升成杠` 的 `[ Tsumo; Dora ]` → `[ Tsumo ]`（那条测的是副露形态，
事件断言是顺带的）。**一条都没删**，其余 KanTests 只增断言不改期望值。
`KanProperties` 的「1 + 杠数」那条同理改成「杠数 − 欠着的」，欠账**从事件流独立数**（不读引擎的字段）。

### 16-B / 明杠之后没打牌就又杠一次：欠账累加，打牌那一刻一次翻光

两批语料 2110 局里**一次都没出现过**这个组合（牌谱作证不了它），因此取最保守的一种：
按杠的种类各算各的——暗杠仍当场翻，明杠的欠账累加，到打牌那一刻按欠的张数逐张翻。
否决：下一个杠成立时顺带把欠账翻掉（那会让「明杠 → 岭上 → 暗杠 → 岭上开花」多算一张宝牌）。
理由：与「明杠等打牌才翻」这条规则同形，且杠数与指示牌数的守恒在一局打完后仍然成立。
用例 `大明杠之后没打牌就又暗杠……` 把这个选择钉住了，将来有反例就改这一条。

### 研究 / `searchStandard` 的五个替代方向：只推荐「分支限界剪枝」，其余四个否决

报告 `docs/research/shanten-search-alternatives.md`（含全部实测区间与复现命令）。
引擎源码一行未改，原型全在 `/tmp/searchstandard-research/` 的树副本里（易失，数字已抄进报告）。

基线（真实牌谱抽的结构化手牌 5 万手，交错跑 4 轮）**8.67–9.36 µs/手**：

- **方向 1 剪枝：1.28–1.36 µs（6.6–7.0×），采纳** —— 42 行 diff，不新增 `let mutable`，
  fantomas 与 `check-style.sh` 干净，541 个测试绿且全量墙钟 43–50 s → 30.7–31.4 s。
  正确性：与基线逐手对拍 65.5 万手 0 差异（结构化 20 万 + 随机 25 万 + 满张偏置 10 万 + 三麻 10 万）、
  固件 4000 手 0 差异、`differential.sh 30000` 0 差异。
- 方向 2 花色分解：1.60–1.78 µs，**否决**（比方向 1 慢 25%，最难手牌上慢 5.8×，多 50 行）。
- 方向 3 预计算表：Fable 5.13 实测编译得出且 node 与 .NET 校验和一致，表 6.08 MB / gzip 251 KB，
  惰性表上界 0.64–0.82 µs，**否决**（只比方向 1 再快 2×，却要构建产物 + 按规则集分表 + 30 槽状态编码）。
- 方向 4 记忆化：一 kyoku 内命中率 11.4%/39.9%/20.7%，查一次 52–65 ns，**否决**
  （要给「纯的、可并发调用」的引擎加全局可变缓存，收益 0.1–0.5 µs）。
- 方向 5 微优化：最好 +3%（`skipEmpty`），`numberInSuit` 查表无可测差异，**否决**（不值单开一票）。

**若开票**：验收要点在报告 §7。两处连带改动是硬要求——`searchStandard` 里「它不参与剪枝」那句注释，
与 `docs/agents/fsharp-style.md` 规则 5 对照表里 `searchStandard` 那一行（`let mutable best`
的理由从「量过，函数式 +10%」变成「它是剪枝上界」，是更强的理由）。
另：15 票（`tenpaiDahai` 前置闸门）的前提数字会从 2.1 ms 掉到约 0.3 ms，收益要重算。

### 17 票 / `searchStandard` 剪枝落地：下界的两条上界、`hasHead` 守卫，与两次变异对照

报告 `.scratch/llm-riichi-arena/run/reports/17-shanten-branch-and-bound.md`。改动 = `Shanten.fs`
的 `searchStandard`（39+/17-，含注释）+ `docs/agents/fsharp-style.md` 规则 5 那一行。

- **下界**：`current - maxGain`，`maxGain = min (2*(4-melds-partials)+1-headBonus) (2*rem/3)`
  ——一条按组数（面子+搭子 ≤ 4，雀头另算），一条按张数（每张最多贡献 2/3）。`unpairable` 只抬高叶子，
  忽略它对下界安全。`rem` 是 `counts.[index..]` 的剩余张数，顺递归传。
- **实测**（结构化 5 万手，交错 4 轮）`standard` 8.58–8.77 → **1.26–1.30 µs**；全量测试
  runner 39–52 → **29–31 s**；节点 10 094 → **240**/手，叶子 639 → **0.2**/手。
- **正确性**：与改动前 DLL 逐手对拍 **604 661 手 0 差异**（结构化 204 661 + 随机 20 万 + 满张 10 万
  + 三麻 10 万，`standard` 与 `calculate` 两个值都比）；固件 4000 手 0 mismatch；
  `differential.sh 30000` 两个 seed 均 0；543 测试绿。
- **验证有没有牙（变异对照，两个方向各一个）**：删掉 `hasHead` 守卫 → 满张偏置 14 153 手差异、
  固件 112 mismatch、4 个既有测试红；把组数上界少算雀头那 +1 → 结构化 15 341 手差异。
  **顺带修正研究报告 §2.3 的一句话**：`hasHead` 那个坑「普通语料测不出」只对逐手对拍的
  结构化/随机/三麻三组成立（全 0），仓库固件、`differential.sh` 与 4 个既有测试都会响。
- **风格文档规则 5 那一行**：`let mutable best` 的理由从「量过，函数式 +10%」改成
  「它是分支限界的上界，跨子树活着」，并另加一段说清**两件事别混**——那个 +10% 是备注 N-24 量
  **无剪枝**版本的**写法**实验，剪枝改的是**算法**；剪枝之后纯函数写法是否仍 +10% **没人量过**，
  别沿用旧数。
- 范围守住：花色分解 / 预计算表 / 缓存 / `skipEmpty` 一个没做（否决理由在研究报告 §3–§6）。
- **提案（留给人裁决）**：15 票（`tenpaiDahai` 前置闸门）的前提数字随 `standard` 同比例下降
  （8.6 → 1.3 µs），那张票值不值得做要重判。本票未动 15 票任何文件。

### 研究 / 与 libriichi 的向听对比：**够用，不必优化**；查表只值 1.6–1.9×

报告 `docs/research/shanten-vs-libriichi.md`（语义对齐、两边的量法、全部实测区间与复现命令）。
引擎源码一行未改；harness、输入、原始日志、45 万手逐手数值归档在 `~/janpo-prototypes/libriichi-bench/`
（Rust 侧的树在 `/tmp/libriichi-bench/`，易失，数字已抄进报告）。

**语义先对齐再谈速度**：45 万手（结构化 5 万 + 满张偏置 10 万 + 随机 20 万 + 三麻 10 万）
逐手对拍一般型与三型取最小两列，**0 差异**；和了 = -1 两边一致，副露映射 `len_div3 = 4 - nakiCount`。

结构化 5 万手、交错跑 4 轮 × 2 次：**janpo `calculate` 1.356–1.389 µs/手（720–737 K 手/秒）**，
**libriichi `calc_all` 0.091–0.093 µs/手（10.8–11.0 M 手/秒）**，**14.6–15.3×**。
均匀随机 15.6–16.9×，最难 2000 手 15.5–19.3×。

**倍率的分解**（前两项实测，第三项残差）：算法（查表 vs 剪枝搜索，在 .NET 上同侧换）只值 **1.6–1.9×**；
同一段闭式扫描的语言/运行时税 **1.6–2.3×**；残差 ~4–5× 里能直接指认的是**每次调用 0.20 µs 的
FSharp.Core 脚手架**（`Array.copy` 34 个 int = 90 ns + `deadQuadKinds` 的 `Seq.filter |> Seq.length` = 112 ns），
它本身已经是 libriichi 整个 `calc_normal`（0.040 µs）的 5 倍。**「它快是因为查表」这个解释是错的。**

**libriichi 的表**：tomohxx 的 C++ 程序**离线**生成，两个 `.bin.gz`（合计 200 KB）提交进仓库、
`include_bytes!` 编译期嵌入、`LazyLock` 首次使用时解压（实测 **15.5–16.5 ms**，常驻 **19.3 MiB**）；
不是 build.rs、不是编译期常量。构建耗时对它是 0。许可证血缘是 GPL→AGPL，抄表要连许可证一起抄。
**它不支持三麻**：牌种硬编码 34、表按 `5^9`/`5^7` 建、`calc_normal(&[u8;34], u8)` 没有牌种集合参数——
所以「按规则集分表」这个问题在它那里不存在，在我们这里仍在，它没有给出更便宜的答案。

**结论：不动向听。** 产品判据是 `RiichiState.tenpaiDahai` 实测 **13.0–13.6 µs/回合**，
夹在 1–30 秒的 LLM 调用之间占 10⁻⁶–10⁻⁵。将来 in-browser MCTS rollout 才会疼（外推：
一万次 rollout 光向听就要 20–30 秒），但那时该换的是执行基底（wasm 之类），
不是把 `Shanten.fs` 改成查表——查表只买到 1.6–1.9×，不解决问题。

### 14 / 随机 Player 的偏好做成一张权重表，均匀预设与 `Kyoku.randomPlayer` 逐手一致

`PlayerBias`（`RandomPlayer.fs`）给每类动作一个权重，取样时按权重摊开再掷一次骰子；
**每条动作的权重下界是 1**，因此「从完整合法动作集取样」这条性质不变，偏好只改相对粗细。
权重全 1 时它退化成按下标等概率取样，**与 `Kyoku.randomPlayer` 逐事件相同**（有用例钉着），
于是 `Kyoku.fs` 一个字没改、既有 543 个测试一条不受影响。
否决：（a）直接把 `Kyoku.randomPlayer` 改成带偏好的（要改 KyokuTests 的既有期望值）；
（b）写死的 if 优先级链（测试固件里已有六个那样的选手，它们是**确定性**的，跑批要的是随机探索）。

### 14 / 打牌偏好用「打完向听最小」而不是孤张启发式

`Tenpai` 旋钮把「打完向听数最小」的那几张的权重乘 20。100 场对比：向听挑牌 87 ms/场、
孤张启发式 110 ms/场，而覆盖率互有胜负（孤张的杠更多、向听的立直更多、**孤张的九种九牌是 0**）。
选向听：它更快，且与引擎自己的听牌判据同源（`Shanten`），不引入第二套手牌评价。
代价写在类型上：`Tenpai > 1` 时每次打牌决策要对每个牌种各跑一次 `Shanten`。

### 14 / 跑批自己写驱动循环，不走 `Kyoku.run`

`Soak.playKyoku` 复刻了 `Kyoku.run` 的十几行，因为跑批要在**每一手**上插手：验四条不变量、
记动作（回放要它）、数手数、防卡死。否决：给 `Kyoku.run` 加一个观察回调——那会让正常路径
为跑批背一层间接，而 M1 的 Agent 层还要在这条路上再包一层。

### 14 / 覆盖率分成 `Soak.required`（断言）与 `Soak.rare`（只印不断言）

`required` 16 项，默认规模（60 场 313 局）每项都有余量，最薄的九种九牌 7 次。
`rare` 五项：四杠散了（60 场 2 次、1500 场 9 次，**碰得到但两次不足以当闸门**）、
四风连打 / 四家立直 / 三家和了 / 流し満貫（1500 场 7536 局 0 次）。
`Soak.uncovered` 把这一批里没走到的**如实印出来**，因此「没覆盖」是显式的话而不是沉默；
测试断言的是「没覆盖到的 ⊆ `rare`」——将来真碰到了不算回归。
另有一条用例钉着「`RyuukyokuReason` 的每一种要么在 `required` 要么在 `rare`」，
免得将来加 case 时它静静地不在覆盖率里。

### 14 / `start_game` 不计入覆盖率

它的 `names` 来自配桌，不是引擎产出的事件（02 票的决定），跑批压根产不出它。
否决：在跑批里自己造一条 `StartGame` 事件计数——**数一条自己造的事件就是「假装覆盖了」**。

### 14 / 跑批规模默认 60 场，`JANPO_SOAK_GAMES` 放大

60 场 = 313 局 = 2.1 万手，跑批本身约 10 秒 CPU。本机 16 核实测 42.1s → 43.1s——
xunit 并行跑各模块，跑批落在属性测试的阴影里，`JANPO_SOAK_GAMES=1` 与 `=60` 同为 42–46s。
**GitHub 的 runner 只有 2–4 核，那里是实打实的 +10 秒（约 +25%）**，仍然没有翻倍。
CLI 收种子区间（`janpo soak 1 1500`，239 秒，0 问题），测试读环境变量（沿用 13 票的先例）。
**调到默认值以下时稀有的那几种可能走不到，那时该改的是这个变量而不是断言。**

### 14 / 点数守恒的基准取「起手总点」而不是「这一局开局时的总额」

`Σ scores + kyotaku × 1000` 恒等于 `Ruleset.startingTotal`（四麻 100000），一局之内每一手都验，
终局精算后再验一次。取局初总额当基准的话，**局与局之间漏一笔看不出来**。
变异测试（听牌料多付 100 点）在 3 场里报出 943 条，证明它会响。

### 14 / `janpo game` 加 `--covering`：种子要真的指得回那条事件流

票要求「失败时输出可复现的种子**与事件流**」。种子本身就是事件流的指针，但前提是有一条命令
真的跑得出同一场——而 `janpo game <种子>` 走的是 `Game.runRandom`（均匀选手、选手与牌山共用
一条随机流），与跑批**不是同一场**。于是给 `game` 加 `--covering`：换成跑批那个选手与那对
发生器（`Soak.playerRng` 因此从 private 提成公开）。有一条用例逐事件类型比对钉住这条等价。
否决：把整条事件流塞进 `SoakIssue`（一场 654 手，报告会被淹没）。

---

# 二、提案：已裁决（无需再看，仅存档）

### 提案 01-B：渲染出口的统一命名 `toDisplay`

**处置：✅ 已作为**裁决 D-1** 执行全程（`toDisplay` 统一命名），风格文档规则 3 也写了**


ADR-0001 只举了 `Tile.toDisplay` 一个例子。01 票把它扩成了约定：
**所有产出中文的函数一律叫 `toDisplay`，集中在文件末尾的渲染段**（`TileParseError.toDisplay`、
`TileListParseError.toDisplay` 已照此落地）。建议把这句写进 ADR-0001 的 Consequences，
让「渲染层是单向出口」有一个可 grep 的判据。

### 提案 13-B（需人裁）：明杠的新宝牌指示牌翻早了，岭上开花时多算宝牌

**处置：✅ **16 票已修**，扩样本对拍差异 8 → 0**


真实牌谱 218 局实证天凤的时机是「**暗杠立刻翻，明杠等到打牌那一刻才翻**」：
`ankan → dora → tsumo`（116 次，含岭上开花 6 次照翻）、`kakan/daiminkan → tsumo → dora → dahai`（87 次），
而**加杠后当场岭上开花的 2 次，牌谱里根本没有 `dora`**。我们的 `GameState.completeKan` 三种杠一律
补摸后立刻翻，于是那 2 局多翻一张，其中 1 局把 40符2飜(2700) 算成了满贯(8000)。
改法要给 `GameState` 加「欠着几张明杠宝牌」的状态并在 `applyDahai` 开头补翻，
**且要改 `KanTests` 两条断言的期望值**（整条事件流顺序不变，变的只是哪一次 `step` 吐出 `Dora`）——
那是改 11 票的既有断言，按 RUNBOOK 不自作主张。影响面 2/2110 kyoku（0.09%）。

### 提案 R-A：把「听牌的口径」写进 CONTEXT.md，并把已知的规则分歧记进 ADR-0004

**处置：✅ 已落成 **ADR-0004 决定 6**（按天凤口径，并明确记下「当前结构上不可配置」）**


调研（`docs/research/shanten-tenpai-boundaries.md`）证实引擎当前行为与天凤一致，但两件事只活在
代码注释里，别的票读不到，将来加雀魂 / 竞技预设时会直接踩：

**（一）CONTEXT.md 的 `Tenpai` 条目**现在只有「差一张即可和了的状态，即 Shanten 为 0」。建议补：

> 判定只看手牌形态与规则集的合法牌种，**不看牌河 / 副露 / 宝牌指示牌上还剩几枚**——
> 纯空听（純カラ）算听牌（天凤 `tenhou.net/man/`「形式聴牌あり」）。唯一的例外是「等第 5 张」：
> **暗牌**里已经握满 4 张的牌种不能当待ち（天凤「純手牌で４枚使用していなければ成立」；
> 副露与杠出去的牌不计入暗牌）。

**（二）ADR-0004 的规则差异清单**建议加一行（它已经写了「规则变体……甚至向听数的合法牌种」，
这条同属一类，但严重程度更高——引擎结构上无法表达另一侧）：

> **「5 枚目を待つ聴牌」的口径**：天凤只看暗牌（碰了 `999m` 再单骑 `9m` = 听牌，手册明文），
> EMA 竞技规则看整手牌含副露（同一手 = 不听，规则书 §3.3.8 例 1）。引擎取天凤口径；
> 因为 `HandShape` 只有 `nakiCount`、没有副露牌面，**当前无法配置成 EMA 口径**。
> 实测影响 4 / 72 204 座位（0.006%）。要支持竞技预设得给 `HandShape` 带上副露牌面，
> 或给 `Ruleset` 加 `TenpaiRule` 开关。雀魂取哪一边**未找到一手资料**。

两条都只是把已经落地的事实显式化，不改任何代码行为。

### 提案 S-A（调度器）：引入 Ruleset（规则集），并重裁 Atamahane 的默认值

**处置：✅ 用户裁决「默认值对齐天凤」→ 落成 **ADR-0004** 决定 1-3 + R-1 代码化（`Atamahane=false`）**


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

**处置：✅ 用户未纳入范围 → ADR-0004「取舍」记了三处零成本参数化；12 票的三麻测试守着**


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

**处置：✅ 用户裁决「Ruleset 该带这个」→ **R-4 已落地**，`Ruleset.TileKinds : TileKindSet`**


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


---

# 三、提案：待裁决

## 3.1 术语表增补（10 条，**建议一次性批量处理**）

这 10 条都是同一件事：某票落地时用到、但 `CONTEXT.md` 里没有的罗马字词。
它们不是 10 个独立决策，是一个批量任务。

### 提案 01-A：把牌相关的几个罗马字词补进 CONTEXT.md

01 票落地时用到、但术语表里没有的词：`Manzu` / `Pinzu` / `Souzu` / `Jihai`（花色）、
`Akadora`（红宝牌，术语表现有条目只在 Dora 里提了一句「含里宝牌与红宝牌」）、
`deaka`（去红）、`kindIndex`（0-33 的牌种索引）。建议在「牌与手牌」一节补齐，
否则后续票各写各的（`Honor` / `Red` / `tileId`）就会散掉。

### 提案 04-A：把摸打循环的几个词补进 CONTEXT.md

本票落地、术语表里没有的词：`Kawa`（河）、`NotenBappu`（听牌料 / ノーテン罰符）、
`Haitei` / `Houtei`（海底 / 河底，术语表只在 Ippatsu 条目里顺带提过）、`Phase`（阶段）、
`Tsumogiri`（摸切）/ `Tedashi`（手切）。建议分别补进「牌与牌河」与「引擎接缝」两节。

### 提案 05-A：把对局层的几个词补进 CONTEXT.md

本票落地、术语表里没有的词：`Juni`（顺位，`GameResult.Juni`——术语表没有「顺位」这个条目，
按 ADR-0001 取了罗马字，但它与 `Junme`（巡）只差一个字母，读起来容易晃眼，
若要换成英文 `Rank` 改一个字段名即可）、`GameLength`（对局长度，术语表有 `Tonpuusen` / `Hanchan`
两个取值却没有这个上位词）、`GameResult`（终局精算）、`GameProgress`（对局进程）、
`RiichiBou`（立直棒，术语表的 `Kyotaku` 条目提了「立直棒」没给罗马字）。

---

### 提案 06-A：把和了与振听的几个词补进 CONTEXT.md

本票落地、术语表里没有的词：`Hora`（和了，术语表只有 Yaku / Fu，没有和了本身）、
`Minogashi`（见逃，`PlayerState.minogashi`）、`Doujun`（同巡，术语表在 Furiten 与 Junme 条目里
提过「同巡」但没给罗马字）、`KyokuEnd`（终局形态）、`Renchan`（连庄，术语表在 Oya 条目里
提过中文「连庄」没给词）。另：振听「永久」那一位现在叫 `Furiten.Permanent`（英文），
因为「永久振听」没有通行的罗马字短词——若术语表想统一，请一并裁。

---

### 提案 07-A：把和了形相关的罗马字词补进 CONTEXT.md

07 落地时用到、但术语表里没有的词：`Mentsu`（面子）与 `Shuntsu` / `Koutsu` / `Kantsu`
（顺子 / 刻子 / 杠子）、`Jantou`（雀头）、`Menzen`（门清）、`Yakuman`（役满）、
`Han`（番）、`Kuitan`（食断）、`WaitKind` 的五种听牌型（`Ryanmen` / `Penchan` / `Kanchan` /
`Shanpon` / `Tanki`）、`RiichiDeclaration`。术语表现在只有 `Yaku` 与 `Fu` 两条，
08（符）与 10/11（副露）会继续用这批词，建议在「规则判定」一节补齐。

### 提案 08-A：把符与点数的几个词补进 CONTEXT.md

08 落地时用到、术语表里没有的词：`Fu` 的分项（底符 / 面子符 / 雀头符 / 听牌型符 /
门清荣和符 / 自摸符）、`Honba` 与 `Kyotaku` 的**点数换算**（术语表只说了它们是什么，
没说一本场 300 点、一根立直棒 1000 点）、`Limit`（满贯 / 跳满 / 倍满 / 三倍满 / 数え役满
这一档的统称，本票取英文 `Limit`）、`HoraPoints`（和了点，mjai wire 的 `hora_points`）。

另外两个**英汉混拼**的字段名请一并裁：`DoubleKazeJantouFu`（连风牌雀头符）与
`RiichiStick`（立直棒点数）。「連風牌」没有通行且无歧义的罗马字短词，
「立直棒」的罗马字 `RiichiBou` 又不像这仓库里别的名字。

### 提案 10-I：把「食替」与「河被鸣走」补进 CONTEXT.md

`Kuikae`（食替：鸣完不能马上打回鸣进来的那张，两面搭子吃时另一端也不能打）是通行的日麻术语，
术语表里没有；`KawaTaken`（这家的河被人鸣走过，Nagashi Mangan 的前提）没有通行的罗马字短词，
是我按术语表的构词法起的（Kawa + 英文动词），请一并裁。

### 提案 11-L：术语表里没有「杠」与「岭上」的词条

`CONTEXT.md` 的 Naki 条目列了 Ankan / Minkan / Kakan，但没有 **Kan**（杠这个动作 / `NakiKind` 的上位词）、
**Kantsu**（杠子这个面子，`Mentsu` 里已经在用）、**Rinshan**（岭上牌与岭上开花，Ippatsu 条目里
提过中文「岭上」没给罗马字）、**Chankan**（同上）。本票这四个词都进了标识符。请一并裁。

### 提案 12-A：`CONTEXT.md` 的 Ryuukyoku 条目缺五个罗马字

术语表的 Ryuukyoku 条目只写了中文「四风连打、四杠散了、九种九牌、四家立直」，三家和了没进条目，
流し満貫另有条目（`Nagashi Mangan`，有罗马字）。本票取的是
`SuufonRenda` / `Suukaikan` / `SuuchaRiichi` / `KyuushuKyuuhai` / `SanchaHora`
（`Suukaikan` 取 riichi.wiki 的「四開槓」读法，中文的「四杠散了」是同一件事的另一个说法）。
与 11-L、R-5-A 同批待裁。

### 提案 R-5-A：术语表缺「下家 / 对家 / 上家」的罗马字

`CONTEXT.md` 的 Seat 条目写了「『下家 / 对家 / 上家』是相对座位的标准说法，照用」，但只给了中文。
按 ADR-0001（标识符用罗马字日麻术语）我取了 `Shimocha` / `Toimen` / `Kamicha`，
并把「三麻没有 Toimen」写进签名（返回 `Seat option`）。请一并裁，与 11-L 那条同批。

---


## 3.2 其余待裁（3 条）

### 提案 04-B：把「标识符照术语表、wire 照 mjai」写进 ADR-0001

⚠️ **仍待办**：ADR-0001 正文只写了「标识符用罗马字」，**没有写 wire 与标识符分离**这条。已核查。


ADR-0001 只说了记法（`1z` vs `E`）。罗马字**拼法**同样会分叉：mjai 写 `ryukyoku`，术语表写
`Ryuukyoku`。建议在 Consequences 补一句：wire 上的字符串一律照抄 mjai，F# 标识符一律照 CONTEXT.md，
两者不一致时由编码器承担映射（`Kaze`、`Ryuukyoku` 已经是这样）。

### 提案 12-C：`Ryuukyoku` 记录没有 `actor`，九种九牌的宣言者不上 wire

mjai 参考实现在九种九牌那条 `ryukyoku` 上带 `actor`，本票没加：`Ryuukyoku` 的形状因此保持稳定，
且宣言者可以从前一条 `tsumo` 的 `actor` 读出来（13-prep 报告的 A5 用的正是这条判据）。
若 13 票的对拍需要它，加一个 `Actor: Seat option` 是小改动。

### 提案 13-C（需人裁）：上游牌谱把双响的 `ura_markers` 抄在两条 `hora` 上

6/2110 kyoku，全是双响，后一家根本没立直，而两边**钱流一字不差**——我们的处理是对的。
对拍时按牌谱**自己的** `reach_accepted` 把这份噪声抹掉（`PaifuReplay.denoiseUra`），
判据取自牌谱而不是引擎。若将来换数据源，这一处可以删。

