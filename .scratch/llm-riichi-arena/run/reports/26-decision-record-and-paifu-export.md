# 26 — DecisionRecord 与 Paifu JSON 导出

**状态**：done　**change**：见 `jj log`（本票一个 commit）　**fixed point**：`64d68822`（`vntttwlu`）

每一手 LLM 决策的完整审计数据留下来了，一场对局导得出一个 JSON 文件，
**而那个文件里的事件流交回引擎能 fold 出逐条相同的事件流与同一份点数**。
浏览器里真下了一次（playwright 的 download 事件），下下来的字节又喂回浏览器里的引擎跑了一遍回放。

结构比功能重要，因此这份报告先说类型。

---

## 1. 类型（M2 的思考气泡 / URL 分享 / 导入回放全建在这上面）

```fsharp
// src/Janpo.Engine/Paifu.fs
type Paifu = {
    Version: int                      // 格式版本号，现在是 1
    Ruleset: Ruleset                  // 整份规则集，逐字段（不是预设名）
    Events: Event list                // mjai 事件流，含开头的 start_game
    Decisions: DecisionRecord list    // 逐手的决策记录；随机选手的手不在里面
}

type DecisionRecord = {
    Turn: int                 // 这一场里的第几手，0 起。**随机座位的手照样占号**
    Seat: Seat
    Prompt: string            // 最后一次问出去的 prompt 全文
    Tools: string             // 工具定义的 JSON 全文（choose_action 的 schema）
    Output: string            // 模型原始输出的 JSON 全文，**不含 thinking**
    Reason: string option     // 模型给的一句话理由
    Thinking: string option   // **可省略的那一段**
    Attempts: int             // 首问 + 重试
    LatencyMs: int            // 端到端，含重试
    Applied: int option       // 最终落定的动作在**那一手决策包**里的 id
    Fallback: string option   // 兜底代打的原因；None = 模型自己决的
}
```

`Prompt` / `Tools` / `Output` 三段是**字符串**，F# 一个字段都不解释——它们是 Agent 层那侧的形状
（ADR-0005：跨界只传字符串），牌谱只负责原样保存给人与分析脚本看。

**牌谱里没有动作、没有局面**：`Applied` 是 id 不是 `Action`（意图不上牌谱，见 DECISIONS 26-3），
局面由 fold 得出（ADR-0002）。M2 的气泡要显示「它选了什么」，就 fold 到第 `Turn` 手、
`DecisionPackage.forSeat` 重算那一包、`tryAction` 换回来。

### 落点一览

| 层 | 文件 | 做了什么 |
|---|---|---|
| 引擎 | `Paifu.fs`（新） | `Paifu` / `DecisionRecord` 两个类型 + 编解码 + `stripThinking` + `decisionAt` |
| 引擎 | `Replay.fs`（新） | `Replay.game : Ruleset -> Event list -> Result<Replayed, ReplayError>`；`Replayed = { Game; Current }` |
| 引擎 | `Ruleset.fs` | 20 个字段的 encoder / decoder（牌谱里的规则集那一段） |
| 引擎 | `GameLength.fs` | `toWire` / `ofWire`（此前只有中文的 `toDisplay`，牌谱不许消费渲染输出） |
| 引擎 | `DecisionPackage.fs` | `tryId`：`tryAction` 的反向，给 `DecisionRecord.Applied` 用 |
| 边界 | `Agent.fs` | `AgentAnswer` 加四个字段（`Prompt` / `Tools` / `Output` / `Thinking`）与解码 |
| 编排 | `Roster.fs` | `names`：`start_game` 的 names（`random` / `provider/model`，**没有 key**） |
| 牌桌 | `Table.fs` | `Turns`（手序）、`Decisions`（记录）、`applyRecorded`、`events`、`paifu`；`Fallbacks` 字段删了改成数出来 |
| 页面 | `TablePage.fs` | `settle` 组装 `DecisionRecord`；「导出牌谱」按钮 + `Exported` 消息 |
| 页面 | `Download.fs`（新） | 唯一碰浏览器下载 API 的地方（一段 `[<Emit>]`） |
| 页面 | `PaifuCheck.fs`（新） | 浏览器侧入口：一份牌谱原文进、一份回放报告 JSON 出（与 `Golden.check` 同形） |
| Agent 层 | `web/src/agent/tools.ts`（新） | `choose_action` 的定义，`piai` 与 `loop` 共读一份 |
| Agent 层 | `piai.ts` | `completeSimple` → `streamSimple`，收 `thinking_delta` |
| Agent 层 | `loop.ts` / `types.ts` / `ask.ts` / `decide.ts` | 审计四项随回执过界 |

测试：引擎 `PaifuTests`（7 条）+ `ReplayTests`（8 条）；Web `PaifuExportTests`（10 条）+ `AgentTests` 补 1 条；
Agent 层 `web/tests/agent/record.test.ts`（6 条）。全量：dotnet 621 + 62 条、node 26 条、`./scripts/ci.sh` 全绿（Release，约 90 s）。

## 2. 导出入口

- **页面**：控制条上的「导出牌谱」按钮（`data-testid="table-export"`），随时可点——
  **不必等终局**，打到一半的事件流同样 fold 得回去。文件名 `janpo-paifu-<种子>.json`。
- **代码**：`Table.paifu roster table : Paifu` → `Paifu.encoder` → `Encode.toString 0` → `Download.json`。
- **回放**：`Replay.ofPaifu paifu : Result<Replayed, ReplayError>`；
  `Replayed.events` 与牌谱自己的事件流（去掉 `start_game`）逐条相同，`Replayed.result` 是终局精算。

## 3. 回放一致的证据

### 3.1 引擎侧：随机对局 200 场，逐条相同 200 场

```
$ dotnet fsi --exec /tmp/replay-soak.fsx     # Game.runRandom → Game.events → Replay.game
200 场：逐条相同 200，差异 0
事件种类出现在几场里：[(ankan,22); (chi,200); (dahai,200); (daiminkan,41); (dora,110);
 (end_game,200); (end_kyoku,200); (hora,4); (kakan,61); (pon,200); (reach,2);
 (reach_accepted,2); (ryukyoku:fanpai,200); (start_kyoku,200); (tsumo,200)]
```

「逐条相同」= `Game.events replayed = Game.events original` 且 `Game.result` 两侧相同。

### 3.2 随机取样碰不到的那几条路：七种流局形态各一条

`ReplayTests` 借 `GameStateFixtures` 的摊好剧本（与 `RyuukyokuProperties` 同一批）跑七种流局
——**九种九牌**（唯一由座位宣言的流局）与**三家和了**（那三家的荣和宣言根本不产出 `hora` 事件，
宣言者只记在 `ryukyoku` 的 `tenpais` 上）正是回放里两条特殊路径，各有一条用例钉住。
另有双响与自摸和各一条。

### 3.3 浏览器侧：下下来的**那份字节**再 fold 一遍

```
$ cd web && node scripts/verify-export.mjs --turns 40          # 进了 ci-web.sh，四家随机、零网络请求
模式：四家随机选手（不发任何请求）　先走 40 手
下载文件名：janpo-paifu-2088.json　4200 字节
牌谱：版本 1　事件 63 条　决策记录 0 条（其中带 thinking 0 条、兜底 0 条）　已打完 0 局
回放：事件流逐条相同 = true　点数 25000 / 25000 / 25000 / 25000
导出的牌谱下得下来、读得动、回放得回去 ✓
```

闸门还比了**牌桌上显示的四家点数**与**回放算出来的点数**：牌桌与 fold 出来的必须是同一场对局。

### 3.4 dotnet 侧：一桌带 LLM 座位的整场对局

`PaifuExportTests` 把一整场对局打完（模型每手都答话 / 每手都交不出来两种），
导出 → 编码 → 解码 → `Replay.ofPaifu`，断言事件流逐条相同、终局点数与顺位相同。
半场导出（走 40 手就导）另有一条。

## 4. 真导出的 Paifu 样例（脱敏）

真跑一次：座位 1 = DeepSeek `deepseek-v4-flash`，**思考预算 medium**，走 24 手后点导出。

```
$ JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-export.mjs --llm --thinking medium --turns 24
模式：一席交给 deepseek-v4-flash（思考预算 medium）　先走 24 手
走完之后：上一手：座位 2 手切8筒
Agent 状态：座位 1 兜底代打：模型超时（60001 ms 没答完）（重试 2 次仍无结果）　这一桌已兜底 1 手

下载文件名：janpo-paifu-2088.json　77506 字节
牌谱：版本 1　事件 45 条　决策记录 7 条（其中带 thinking 7 条、兜底 1 条）　已打完 0 局
回放：事件流逐条相同 = true　点数 25000 / 25000 / 25000 / 25000
导出的牌谱下得下来、读得动、回放得回去 ✓
```

七条记录的手序是 **1, 5, 9, 10, 13, 18, 22**——跳号正是「随机座位的手也占号」的证据
（9 与 10 相邻是「响应一手 + 打一手」）。`attempts` 依次 1/1/1/2/3/2/3，
`latency_ms` 依次 21.9 s / 17.1 s / 19.5 s / 112 s / 155 s / 79 s / 180 s（medium 思考很慢，见 §6）。

**整份文件原样留在 `reports/26-paifu-sample.json`**（77 KB，真下下来的那一份，
`grep` 过一遍确认没有 key——牌谱里根本没有那个字段）。27 号票可以直接拿它回放：
`Replay.ofPaifu`，或浏览器里 `PaifuCheck.check`。

**顺带一条双目标证据**：这份文件是**浏览器里 Fable 编出来的引擎**导出的，
拿到 **dotnet 侧**（`dotnet fsi` + Newtonsoft 后端）读进来回放，事件流同样逐条相同、
编解码往返逐字段相同——牌谱的 wire 形态两个目标看法一致。

下面是它的形状（长字符串截断，其余原样）：

```json
{
  "version": 1,
  "ruleset": {
    "seat_count": 4, "length": "tonpuusen",
    "tile_kinds": ["1m", "2m", "…（34 种）", "7z"],
    "copies_per_kind": 4, "akadora": ["5mr", "5pr", "5sr"], "kuitan": true,
    "haipai_size": 13, "dead_wall_size": 14, "rinshan_count": 4,
    "starting_score": 25000, "riichi_bou": 1000, "noten_bappu": 3000,
    "atamahane": false, "sancha_hora_ryuukyoku": true, "kyuushu_kinds": 9,
    "kiriage_mangan": false, "double_kaze_jantou_fu": 4, "rinshan_tsumo_fu": true,
    "kokushi_ankan_chankan": false, "honba_points": 300
  },
  "events": [
    { "type": "start_game", "names": ["random", "deepseek/deepseek-v4-flash", "random", "random"] },
    { "type": "start_kyoku", "bakaze": "1z", "dora_marker": "2s", "kyoku": 1, "honba": 0,
      "kyotaku": 0, "oya": 0, "scores": [25000, 25000, 25000, 25000],
      "tehais": [["6m","6m","7m","9p","3s","7s","8s","8s","1z","2z","2z","7z","7z"], "…（四家）"] },
    { "type": "tsumo", "actor": 0, "pai": "5pr" },
    { "type": "dahai", "actor": 0, "pai": "9p", "tsumogiri": false },
    { "type": "tsumo", "actor": 1, "pai": "3m" },
    "…（共 45 条）"
  ],
  "decisions": [
    {
      "turn": 1,
      "seat": 1,
      "prompt": "你在打日本立直麻将（天凤规则，四人东）。现在轮到你做决策。\n\n【场况】\n东1局 0 本场，供托 0 根。\n宝牌指示牌：2s　牌山剩余可摸 68 张。\n\n【你的手牌】\n你是座位 1（南家），第 1 巡，25000 点。\n手牌：2m 3m 3m 5m 9m 3p 4p 5p 8p 3s 7s 8s 2z 7z（14 张）\n刚摸进：3m\n…（略）",
      "tools": "[{\"name\":\"choose_action\",\"description\":\"从这一手的合法动作集中选择一个动作。只能选列出的 action_id。\",\"parameters\":{\"type\":\"object\",\"required\":[\"action_id\",\"reason\"],\"properties\":{\"action_id\":{\"type\":\"string\",\"enum\":[\"0\",\"1\",…（略）",
      "output": "{\"stop_reason\":\"toolUse\",\"text\":\"\",\"tool_call\":{\"name\":\"choose_action\",\"arguments\":{\"action_id\":\"3\",\"reason\":\"9m 是…\"}},\"error_message\":null,\"usage\":{\"input\":…}}",
      "reason": "9m 是与任何搭子都无连接的孤立端张，牌效最低，先切掉它，保留 2333m 的强形、345p、78s 以及两张役牌和中张的进张机会。",
      "thinking": "Let me analyze the hand.\n\nHand: 2m 3m 3m 5m 9m 3p 4p 5p 8p 3s 7s 8s 2z 7z, just drew 3m.\n\nSo we have:\n- Manzu: 2m 3m 3m 5m 9m (plus drew 3m…（略）",
      "attempts": 1,
      "latency_ms": 21916,
      "applied": 3
    },
    {
      "turn": 22,
      "seat": 1,
      "prompt": "…（同上形态，第 6 巡）",
      "tools": "…（同上，enum 换成这一手的 id）",
      "output": "{\"stop_reason\":\"aborted\",\"text\":\"\",\"tool_call\":null,\"error_message\":\"Request was aborted\",\"usage\":{\"input\":0,\"output\":0}}",
      "thinking": "Let me analyze this mahjong hand.\n\nHand: 1m 2m 2m 3m 3m 5m | 2p 2p 3p 4p 5p | 7s 8s | 2z\n\nWe just drew 2p…（略）",
      "attempts": 3,
      "latency_ms": 180016,
      "applied": 11,
      "fallback": "模型超时（60001 ms 没答完）（重试 2 次仍无结果）"
    },
    "…（共 7 条）"
  ]
}
```

**看第二条**（超时兜底的那一手）：`reason` 整个字段不在（模型没给），`fallback` 写着原因，
`applied: 11` 是 `Fallback.action` 代打的那条在包里的 id，**而 `thinking` 仍然有全文**
——超时之前已经流出来的思考留住了，这正是换 `streamSimple` 的意外收益。

## 5. thinking 怎么省（M2 的 URL 分享）

```fsharp
let shared = paifu |> Paifu.stripThinking |> Paifu.encoder |> Encode.toString 0
```

- **同一个类型、同一个编码器、同一个解码器**：省掉不是另一条路，是值上的一次变换。
- `None` 的字段**整个不写**（不是写 `null`）；解码那侧 `Optional.Field` 读得动「缺了」与「是 null」两种。
- 体积证据：上面那份 77,506 字节的牌谱里，thinking 合计 61,921 字符——**大头就是它**
  （ADR-0002 说的那句「省掉它就足够短」是实的）。
- `reason` 与 `fallback` 同样是可缺省字段。

## 6. 关键取舍

全部 15 条见 `DECISIONS.md` 的「## 26」段。四条最要紧的：

1. **26-2 哪个方向有 decoder**：出去的是决策包与 prompt，回来的是 **id 与事实**。
   事实（`Event` / `Paifu` / 回执）有 decoder，**意图没有**——`Action.decoder` 与
   `DecisionPackage.decoder` 仍然不存在，20 号票那条单向边界一分没破。
2. **26-3 记录里存 id 不存动作**：意图不上牌谱；那一手的包由 fold 重算得出。
3. **26-10 回放的产物是 `Replayed = { Game; Current }`**：ADR-0002 说 Replay 是对**前缀**做 fold，
   而前缀常停在某一局中间。事件流喂完还没打完不是错误，中间某局没走完才是。
4. **26-11 `streamSimple` 换掉 `completeSimple`**：`completeSimple` 本来就是
   `streamSimple(…).result()`，换过去只多一条 `thinking_delta` 流，M2 的气泡直接接它。

## 7. 留给人的待审项

- **26-15（提案）**：新词 `Replayed`（回放产物）没进 `CONTEXT.md`（23-9 的 `Roster` 还挂着，一起裁）。
- **medium 思考预算下 DeepSeek 很慢**：实测单手 17–180 s（默认超时 30 s 会大量兜底，
  验收里我把超时调到 60 s 仍超了一次）。**这不是本票的 bug**，但 M2 开思考气泡时
  默认超时要重定，或者给「思考中」一个更长的预算。真跑数据在 §4。
- **导出的 JSON 是紧凑一行**（26-13），要读用 `jq`。要不要给人一个「缩进导出」的选项，留给 M2 的分享页。
- **`verify-export.mjs --llm` 那一档调真实 provider，不进 CI**（CI 跑的是四家随机、零请求的那一档）。

## 8. code-review 结论

两轴顺序各跑一遍（fixed point `64d68822`；这个跑批里派不了 sub-agent，按 RUNBOOK 自己顺序跑）。

### Standards（`docs/agents/fsharp-style.md` + 仓库既有约定 + Fowler 味道基线）

**3 条 blocking，已修**：

1. *可控输入进了文件名*（真问题，不只是风格）：导出文件名初版是
   `$"janpo-paifu-{model.SeedText.Trim()}.json"`——种子输入框里是人随手填的文本（重开之后还可能漂），
   斜杠之类的东西会直接拼进 `download` 属性。→ 抽出 `exportName`，**只有解析得出来的种子才进文件名**，
   否则退回 `janpo-paifu.json`。
2. *Duplicated Code*：`PaifuCheck.check` 里「数有多少条记录满足某条件」的 `List.sumBy` 形状写了两遍，
   且两段管道被 fantomas 撑在 `Encode.object` 的字面里，读不动。→ 抽 `counted`，
   并把 `mismatch` / `juni` 变成命名中间值（风格规则 3 的「拆的手段三选一」）。
3. *Duplicated Code*：`Paifu.recordEncoder` 里 `optional` 只接字符串字段，`applied`（int option）
   另写了一段平行的 `Option.map … |> Option.toList`。→ `optional` 改成收一个 `Encoder<'a>`，
   四个可缺省字段一个形状。

**看过且判为不改的**：

- 风格规则 1/2/3（不许从里往外读、lambda 包一层调用、三层变换嵌套）：新代码里无命中
  （`check-style.sh` 锁的两条也干净）。规则 5：引擎 `let mutable` 一个未新增（预算仍是 2）。
- `Replay` 里对 `Event` 的四个分类函数（`isEngineProduced` / `isResponseTo` / `declarer` / `drawnTiles`）
  逐 case 穷举而不用 `| _ ->`：与 `GameState.junme`、`PaifuReplay.isEngineProduced` 一致，
  `Event` 加 case 时编译器会把这几处全部指出来。只有「是不是局边界」这种真总谓词用了通配。

### Spec（票的 6 条验收 + ADR-0002 / ADR-0001 / ADR-0005 + `CONTEXT.md` 的四个词条 + spec 的分享段落）

**无 blocking**。逐条对照见票文件的勾选框；三处值得写下来的：

- **「完整 prompt」记的是最后一轮**（不截断，但不是三轮各存一份）——判据与被否决的选项记在
  DECISIONS 26-16。重试的措辞在 `prompt.ts` 里看得到，而问了几次记在 `Attempts`。
- **TS 不发一整条 `DecisionRecord` 过来**：它不知道手序、座位与 id 换算。票里那句
  「按 schema decode 成 F# 类型」落在 `Agent.answerDecoder` 上，剩下四个字段由 `settle` 补齐（同 26-16）。
- **没有多做**：没做 URL 分享、没做导入 UI、没做思考气泡（都是 M2）。
  `PaifuCheck.fs` 与 `verify-export.mjs` 是调度器点名要的那次无头验收，不算超出。
  唯一一处动了别的票的代码：把 23 号票的 `Table.Fallbacks` 计数字段改成从记录数出来
  （行为不变，理由见 26-6）。

### nitpick（只记录，未改）

- `Replay.buildWall` 与测试侧 `PaifuReplay.buildWall` 算法同形（约 70 行）。两者吃的类型与用途不同
  （外部牌谱 vs 我们自己的事件流），合并的代价见 DECISIONS 26-9；真要收敛，该等 M3 有第三个调用方时再说。
- `DecisionRecord.Applied` 这个名字没说出它是个 **id**（`AppliedId` 更直白）。
  没改是因为 wire 名 `applied` 已经进了本报告里那份**真导出的样例**，改名就得重跑一次真模型。
  要改在 M2 改（那时牌谱版本号要跑到 2，正好一并）。
- `TablePage.fs` 现在 1000 行（23 号票已经记过一条：M2 该把视图拆成几个组件文件）。
- `PaifuCheck.check` 的报告字段是手拼的 JSON 对象（没有类型）。它是无头闸门的产物，
  与 `Golden.check` 同一形态，暂时照旧。
- `PaifuExportTests` 把 `TablePageTests` 的几个小固件（`step` / `tableOf` / `seat` / 座位配置）
  又写了一遍（仓库里有先例：`atTheTripleRon` 在两个测试模块里各一份）。
