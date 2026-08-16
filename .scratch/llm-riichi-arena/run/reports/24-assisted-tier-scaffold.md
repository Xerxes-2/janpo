# 24 — Assisted 档脚手架

**状态**：done　**change**：`xkrnkzww`（本票一个 commit；commit id 随 amend 变，认 change id）　**fixed point**：`64d68822`（`vntttwlu`）

引擎算得出、LLM 算不明白的那几个数进了决策包，也进了 prompt：**向听数、有效牌（牌种 + 剩余枚数）、
逐张试打的进退向**。主持人在座位上拨一下就换档，同一局面两档的 prompt 肉眼可比——
脚手架强度从此是个实验变量。

---

## 1. 落在哪几处

| 层 | 文件 | 做了什么 |
|---|---|---|
| 引擎 | `src/Janpo.Engine/Scaffold.fs`（新） | `Scaffold` / `DahaiScaffold` 两个类型 + 计算 + encoder |
| 引擎 | `src/Janpo.Engine/DecisionPackage.fs` | 包上多一个 `Scaffold option`，`scaffold` 槽位不再是 `{}` |
| 引擎 | `src/Janpo.Engine/Fallback.fs` | `Assisted` 分支：不退向听的那一手 |
| 边界 | `src/Janpo.Web/Agent.fs` | `LlmField.Tier`（键名 `tier`）——配置项加一个 case，其余处编译器指出来 |
| 页面 | `src/Janpo.Web/TablePage.fs` | 档位从只读文字变成选择框，工具搜索灰着 |
| Agent 层 | `web/src/agent/prompt.ts` | `assisted` 渲染器 + `frame`（两档共用的骨架） |
| 调试 | `web/scripts/print-prompt.mjs`（新） | `pnpm run prompt` —— 两档 prompt 都导得出来 |
| 验收 | `web/scripts/verify-llm-seat.mjs` | `--tier`，真跑一局时能换档 |
| 录制 | `web/scripts/record-agent-fixtures.mjs` | 多录一份 Assisted 档的真实回答；支持只重录点名的那几份 |

测试：引擎 `ScaffoldTests`（10 条）+ `ScaffoldProperties`（4 条属性）+ `FallbackTests`（+4 条）；
dotnet 侧 `AgentTests` / `TablePageTests`（+2 条）；Agent 层 `web/tests/agent`（28 条，全部回放录制响应）。
**dotnet 676 条 + node 28 条 + `./scripts/ci.sh` 全绿。**

## 2. 决策包的 `scaffold` 长什么样（25 号票读这一段）

```json
"scaffold": {
  "shanten": { "value": 3, "display": "3 向听" },
  "ukeire": null,
  "dahai": [
    {
      "pai": "2m",
      "action_ids": [0],
      "shanten": { "value": 3, "display": "3 向听" },
      "shanten_delta": 0,
      "ukeire": { "total": 66, "tiles": [{ "pai": "3m", "remaining": 1 }, ...] }
    }
  ]
}
```

四条要害：

1. **包恒带脚手架**，`ScaffoldTier` 决定的是 prompt 渲不渲染它（DECISIONS 24-1）。
   因此 Bare 档的决策包里也有这些数——**Bare 与 Assisted 的差别只在 `prompt.ts` 那一个函数**。
2. **`ukeire` 只在等摸形非 null**：已摸进的手牌（3n+2 张）要先试打一张才谈得上有效牌，
   那些在 `dahai` 里逐条给（24-2）。`remaining` 可能是 0——牌种全可见了，形态上仍是有效牌。
3. **接头处是 `action_ids` 不是牌**：`5m` 与 `5mr` 是两条动作一个牌种，
   摸切与手切同种时同理（24-3）。渲染层按 id 配对，不必懂去红。
4. **向听数带 `display`**：0 叫「听牌」是术语表的事，不让 TS 查（ADR-0005 第 2 条，24-4）。
   牌相反，一律 mjai 记法。

**算的是遮蔽后的观测，不是 `GameState`**：`Scaffold.calculate` 收的是 `Observation`，
他家的暗牌在 `MaskedSeat` 里根本没有位置，因此「脚手架多看了一张牌」在结构上不可能。
剩余枚数扣的是：自家手牌 + 四家的河 + 全部副露里**自家亮出的**那几张（`Naki.fromHand`）+ 宝牌指示牌。
被鸣走的那张仍留在打牌者河里，**不能连 `Naki.taken` 一起数**（数重了 `Ukeire` 会判可见张数越界，
有效牌整个变 None）——`ScaffoldTests` 与 `ScaffoldProperties` 各有一条专门盯它，
临时把 `fromHand` 换成 `tiles` 两条都红。

## 3. 同一手牌的两份 prompt（本票的验收）

决策包：`web/tests/fixtures/agent/decision-dahai.json`（`janpo decide 2088 --steps 6`，座位 1，11 条动作）。
两份都是 `pnpm run prompt -- --package … --tier bare|assisted` 打出来的原文。

### 3.1 Bare（591 字）

```text
你在打日本立直麻将（天凤规则，四人东）。现在轮到你做决策。

【场况】
东1局 0 本场，供托 0 根。
宝牌指示牌：2s　牌山剩余可摸 66 张。

【你的手牌】
你是座位 1（南家），第 1 巡，25000 点。
手牌：2m 5m 9m 3p 4p 5p 8p 3s 7s 8s 7z（11 张）
刚摸进：无（这一手不是你摸牌）
副露：pon 鸣3m[3m 3m]（来自座位 3）
牌河：2z
立直：无　振听：否

【其他三家】（`*` 表示摸切）
下家（座位 2・西家）：手里 13 张，第 1 巡，25000 点，立直：无
  副露：无
  牌河：5p*
对家（座位 3・北家）：手里 13 张，第 1 巡，25000 点，立直：无
  副露：无
  牌河：3m*
上家（座位 0・东家）：手里 13 张，第 1 巡，25000 点，立直：无
  副露：无
  牌河：9p

【可选动作】只能从下面这些 id 里选一个：
- id=0：手切2万
- id=1：手切5万
- id=2：手切9万
- id=3：手切3筒
- id=4：手切4筒
- id=5：手切5筒
- id=6：手切8筒
- id=7：手切3索
- id=8：手切7索
- id=9：手切8索
- id=10：手切中

调用 choose_action 工具，给出你选的 action_id 与一句话理由。
```

### 3.2 Assisted（2469 字，多出来的就是【引擎算好的数】那一节）

```text
你在打日本立直麻将（天凤规则，四人东）。现在轮到你做决策。

【场况】
东1局 0 本场，供托 0 根。
宝牌指示牌：2s　牌山剩余可摸 66 张。

【你的手牌】
你是座位 1（南家），第 1 巡，25000 点。
手牌：2m 5m 9m 3p 4p 5p 8p 3s 7s 8s 7z（11 张）
刚摸进：无（这一手不是你摸牌）
副露：pon 鸣3m[3m 3m]（来自座位 3）
牌河：2z
立直：无　振听：否

【其他三家】（`*` 表示摸切）
下家（座位 2・西家）：手里 13 张，第 1 巡，25000 点，立直：无
  副露：无
  牌河：5p*
对家（座位 3・北家）：手里 13 张，第 1 巡，25000 点，立直：无
  副露：无
  牌河：3m*
上家（座位 0・东家）：手里 13 张，第 1 巡，25000 点，立直：无
  副露：无
  牌河：9p

【引擎算好的数】（下面这几个数是引擎算出来的事实，不是建议）
当前向听数：3 向听
逐张试打（进退向 0 为不变，+1 为退向（向听戻し）；有效牌括号里是那张牌的剩余枚数）：
- id=0（手切2万）：打完 3 向听，进退向 0，有效牌 66 枚 19 种：3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 8m(4) 9m(3) 6p(4) 7p(4) 8p(3) 9p(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 9s(4) 7z(3)
- id=1（手切5万）：打完 3 向听，进退向 0，有效牌 66 枚 19 种：1m(4) 2m(3) 3m(1) 4m(4) 7m(4) 8m(4) 9m(3) 6p(4) 7p(4) 8p(3) 9p(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 9s(4) 7z(3)
- id=2（手切9万）：打完 3 向听，进退向 0，有效牌 66 枚 19 种：1m(4) 2m(3) 3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 6p(4) 7p(4) 8p(3) 9p(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 9s(4) 7z(3)
- id=3（手切3筒）：打完 4 向听，退向 +1，有效牌 76 枚 22 种：1m(4) 2m(3) 3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 8m(4) 9m(3) 3p(3) 6p(4) 7p(4) 8p(3) 9p(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 9s(4) 7z(3)
- id=4（手切4筒）：打完 4 向听，退向 +1，有效牌 76 枚 22 种：1m(4) 2m(3) 3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 8m(4) 9m(3) 4p(3) 6p(4) 7p(4) 8p(3) 9p(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 9s(4) 7z(3)
- id=5（手切5筒）：打完 4 向听，退向 +1，有效牌 79 枚 23 种：1m(4) 2m(3) 3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 8m(4) 9m(3) 2p(4) 5p(2) 6p(4) 7p(4) 8p(3) 9p(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 9s(4) 7z(3)
- id=6（手切8筒）：打完 3 向听，进退向 0，有效牌 59 枚 17 种：1m(4) 2m(3) 3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 8m(4) 9m(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 9s(4) 7z(3)
- id=7（手切3索）：打完 3 向听，进退向 0，有效牌 55 枚 16 种：1m(4) 2m(3) 3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 8m(4) 9m(3) 6p(4) 7p(4) 8p(3) 9p(3) 6s(4) 9s(4) 7z(3)
- id=8（手切7索）：打完 4 向听，退向 +1，有效牌 79 枚 23 种：1m(4) 2m(3) 3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 8m(4) 9m(3) 6p(4) 7p(4) 8p(3) 9p(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 7s(3) 8s(3) 9s(4) 7z(3)
- id=9（手切8索）：打完 4 向听，退向 +1，有效牌 79 枚 23 种：1m(4) 2m(3) 3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 8m(4) 9m(3) 6p(4) 7p(4) 8p(3) 9p(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 7s(3) 8s(3) 9s(4) 7z(3)
- id=10（手切中）：打完 3 向听，进退向 0，有效牌 70 枚 20 种：1m(4) 2m(3) 3m(1) 4m(4) 5m(3) 6m(4) 7m(4) 8m(4) 9m(3) 6p(4) 7p(4) 8p(3) 9p(3) 1s(4) 2s(3) 3s(3) 4s(4) 5s(4) 6s(4) 9s(4)

【可选动作】只能从下面这些 id 里选一个：
- id=0：手切2万
- id=1：手切5万
- id=2：手切9万
- id=3：手切3筒
- id=4：手切4筒
- id=5：手切5筒
- id=6：手切8筒
- id=7：手切3索
- id=8：手切7索
- id=9：手切8索
- id=10：手切中

调用 choose_action 工具，给出你选的 action_id 与一句话理由。
```

### 3.3 模型真的在用它

同一份决策包问同一个模型（DeepSeek `deepseek-v4-flash`），两档各录一次真实回答
（`web/tests/fixtures/agent/ask-legal.json` 与 `ask-assisted.json`，`pnpm run record:agent`）：

| 档位 | 输入 tok | 输出 tok | 选了 | 理由（原文） |
|---|---|---|---|---|
| Bare | 814 | 94 | id=2 | 「手牌有345p顺子、3s和78s两面搭子，9万是孤立幺九牌价值最低，戇9万保留更多搭子机会。」 |
| Assisted | 2183 | 119 | id=2 | 「打9万保留更多有效牌种（**66枚19种**），维持3向听，同时9万是孤张对搭子形成帮助最小，且**切3筒/4筒/5筒会退向**，切中虽有效牌70枚但中作为役牌价值高。」 |

Assisted 那句把**有效牌枚数、种数与退向**逐个引了出来，且数字与决策包逐字对得上。
输入 token **814 → 2183（2.7×）**：这一手是 3 向听、11 种可打之牌，
逐张试打各带 16-23 种有效牌，是这一档最贵的形态；听牌前后会短很多。

## 4. 兜底：Assisted 做了「不退向听」，「安全打」等 25 号票

`CONTEXT.md` 的 Fallback 说 Assisted 是「不退 Shanten 的安全打」。这一票只做得了前一半
（后一半要 `Danger`，那是 25 号票的模块）：

```fsharp
// 候选 = 进退向为 0 的那几条试打；同为不退向听时优先摸切；一条都没有就退回 Bare 的三级
| ScaffoldTier.Assisted -> assisted package |> Option.defaultWith (fun () -> bare options)
```

**这不是没差别的改动**：摸切并不必然不退向听——刚摸进那张让手牌进了一步时，把它扭头扔回去就是
退向（向听戻し）。`Fallback.fs` 原来的注释「摸切必不放铳新张，也必不退向听」后半句是错的，本票改掉了。
`TablePageTests` 用种子 42 的开局第一手钉住这个差别：Bare 摸切 7 筒（退向），Assisted 改打 5 索。

**25 号票的落点**：在 `Fallback.assisted` 里给这批候选按危险度排序即可，
`bare` 与 `action` 的档位分派都不用动（DECISIONS 24-5）。

## 5. 黄金用例：逐条核对只多了 `scaffold`

`decide` 那两条用例把决策包 JSON 按整行钉住，填 `scaffold` 必红。流程：

```sh
dotnet run --project src/Janpo.Cli -- golden write tests/fixtures/golden/dual-target.json
```

核对不是肉眼扫那 4 KB，是把新旧两份都 parse 出来对：

```python
assert pb.pop("scaffold") == {}     # 旧的是空对象
s = pa.pop("scaffold")              # 新的摘掉
assert pb == pa                     # 其余逐字段相等
```

结论：`decide-1177-step-8` 与 `decide-42-first-hand` 各只多了 `scaffold`（11 条试打），
**另外 37 条用例一个字节没动**（`note`、`ruleset`、`run` 与其余字段全等）。
`jj diff --stat` 上黄金文件是 `4 +-`（两条各一行）。

浏览器侧跑同一份用例也绿：**脚手架在 Fable 下与 dotnet 逐字节相同**——
`List.distinct` 的顺序语义两边一致这件事因此有了闸门（本票没为 Fable 分叉任何逻辑）。

## 6. 人工验收：同一种子两档各跑一局

```
$ JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-llm-seat.mjs --seed 1177 --tier bare
一局打完，用时 45.9 s　provider 请求 23 次（4xx/5xx 0）　兜底代打 0 手
流局　荒牌流局　听牌：座位 0　各家点数：28000 / 24000 / 24000 / 24000

$ JANPO_KEY_FILE=/tmp/deepseek_key node scripts/verify-llm-seat.mjs --seed 1177 --tier assisted
一局打完，用时 17.9 s　provider 请求 10 次（4xx/5xx 0）　兜底代打 0 手
和了　座位 1 自摸6筒　立直 1 番、门前清自摸和 1 番、宝牌 3 番　40 符 5 番 8000 点（满贯）
各家点数：21000 / 33000 / 23000 / 23000
```

页面 `pageerror` 0 条、资源错误 0 条，两档都没兜底过。
**这是一局，不是证据**：它只说明脚手架真的进了 prompt、模型在用它、闭环没坏。
两档谁强要 M2 的评测口径（spec 的对局跑批），不是这一票能回答的。

打包：`index-*.js` 348.77 → **354.32 kB**（gzip 111.15 → 113.10），多出来的是 `Scaffold` 与那个渲染器。

## 7. 调试出口

```sh
cd web
pnpm run prompt                                   # 默认种子 1177 第 8 手，两档都打
pnpm run prompt -- --seed 42 --steps 0            # 换局面（决策包现从 janpo decide 取）
pnpm run prompt -- --package tests/fixtures/agent/decision-dahai.json   # 直接读一份包，不跑 dotnet
pnpm run prompt -- --diff                         # 只打两档的差异
```

`--diff` 输出的就是【引擎算好的数】那一节——**换档看 prompt 不用改代码**（票里的验收）。

## 8. 关键取舍

全文见 `DECISIONS.md` 的「## 24」段（7 条）。三条最要紧的：

1. **包恒带脚手架，档位只管渲染**（24-1）。它让两档的 DecisionRecord 同形（26 号票要），
   也让 `janpo decide` 这类没有座位配置的离线出口不必编一个档位。代价是黄金用例那两行变长。
2. **有效牌挂在逐张试打上**（24-2）。3n+2 的手牌没有有效牌可言，给它编一个「最好那张打完的有效牌」
   是把选择塞进事实里。
3. **接头处是 `action_ids`**（24-3）。渲染层认识 id、不认识牌，这是 ADR-0005 的那条边界在脚手架上的延续。

## 9. 留给人的待审项

- **Assisted 的兜底还差「安全打」那一半**（24-5）。25 号票落地后请复核 `Fallback.assisted`：
  候选集合（不退向听）与排序（危险度）是两件事，我只做了前者。
- **Assisted 档的 prompt 在早巡很长**：3 向听 11 种可打之牌 → 输入 2183 tok（Bare 的 2.7×）。
  逐张试打各列 16-23 种有效牌，信息量是真的，但如果 M2 的跑批发现成本吃不消，
  第一个该砍的是「有效牌的牌种列表」（留总枚数与种数），不是砍试打的条数。
- **`【引擎算好的数】` 这个段落名与那句「是事实不是建议」是我拟的第一版。**
  23 号票的措辞待审项还开着，两处该一起定。
- **`selectField` 现在收三元组**（值、显示、选不选得了）。只有档位需要「灰掉」，
  provider 与思考预算那两个调用点各多写一个 `true`。要是 M2 的配桌页还这样，就该换个形状。

## 10. code-review 结论

两轴各跑一遍（fixed point `64d68822`）。

（这台机器上派不出 sub-agent，两轴由我顺序跑，各自只看自己那份材料。）

### Standards（`docs/agents/fsharp-style.md` + `CONTEXT.md`/ADR-0001 的命名约定 + Fowler 味道基线）

**2 条 blocking，已修**：

1. *中文标识符*（ADR-0001 / `CONTEXT.md` 开头那条："标识符一律用罗马字日麻术语，
   中文只出现在文档、UI 与 prompt 里"）。`prompt.test.ts` 里我写了两个中文变量名
   （`只多了一节` / `老包`）。→ 改成 `extra` / `legacyPackage`。**测试的名字**是中文，那是仓库
   一直的做法（F# 的反引号名、node 的 test 标题），但变量名不是。
2. *错别字「胉眼」*（`ScaffoldTier.fs` / `prompt.ts` / `prompt.test.ts` 各一处）。
   20 号票的报告里记过同一个错字被修过一次——它又出现了。→ 全改「肉眼」。

**nitpick（只记录，未改）**：

- *Duplicated Code*：`Scaffold` 里那个 `optional` 编码助手与 `Observation.fs` 的
  `SeatProjection.optional` 一模一样（5 行）。后者在 `module private` 里，够不着。
  要去重得给 JSON 助手开一个共用模块——为 5 行开一个文件不划算，等第三处出现再说。
- *Data Clumps*：`selectField` 现在收 `(值, 显示, 选不选得了)` 三元组，
  而只有档位那一个调用点需要第三项。它想成为一个「选项行」类型，但只有一个消费者。
- *Primitive Obsession*：`ShantenDelta: int`。术语表里它是个词条，但给它造类型之后
  `Shanten.value a - Shanten.value b` 这类算术会更啰嗦（风格规则 4.2）。
- `Scaffold.encoder` 是 public 但只被 `slotEncoder` 用。留着是随仓库惯例
  （每个上 wire 的类型都在自己模块里出一个 `encoder`），26 号票记 DecisionRecord 时大概率要它。
- `TablePage.fs` 又长了十几行（已 800+）。23 号票记过的那条 nitpick 没变：视图该在 M2 拆开。
- `print-prompt.mjs` 的 `parseArguments` 按字符串键写回一个对象，`.mjs` 不进 `tsc`，
  写错 flag 名只有运行时才知道。它是调试脚本，够用。

引擎的 `let mutable` 预算未动（仍是 2），`scripts/check-style.sh` 与 `dotnet fantomas --check` 干净。

### Spec（票 24 的 6 条验收 + `CONTEXT.md` 的 ScaffoldTier / Fallback 词条 + ADR-0005 的边界）

**1 条 blocking，已修**：

- 票里写着「数值一律**复用引擎既有实现**，不在 Agent 层重算」。初版渲染器的「19 种」是
  `view.tiles.length` 数出来的——而「数不数剩余 0 枚的那几种」是规则问题
  （`CONTEXT.md`：有效牌为空不等于不听牌；引擎有 `Ukeire.kindCount` 与 `Ukeire.live` 两个口径）。
  → wire 上加 `kinds`，取 `Ukeire.kindCount`；渲染层不再自己挑口径。
  （渲染出来的文字一字未变，因此第 3 节那两份 prompt 与录制的固件仍然逐字节成立。）

**部分实现，有意为之**：

- `CONTEXT.md` 的 Fallback 说 Assisted 是「不退 Shanten 的**安全打**」。这一票只做了
  「不退 Shanten」，安全那一半要 25 号票的 `Danger`（DECISIONS 24-5，报告第 4 节）。
  票 24 自己的验收框里没有这条——它来自术语表，因此在这里点名。

**scope creep 自查**（都不在验收框里，逐条给理由）：

- `shanten` 带 `display` 上 wire → ADR-0005 第 2 条要求的（渲染层要中文由包携带）。
- `verify-llm-seat.mjs --tier` → 人工验收要在两档各跑一局，否则「换档」这件事只有单测。
- 录制器支持只重录点名的那几份 → 不这么做就得把另外四份真实响应一起重录，
  那会动到 23 号票钉住的期望值（`ask-legal` 的 id=2、reason 里的「9万」）。
- `selectField` 收三元组 → 「ToolSearch 在配置面板里灰掉」这条验收要的。

ADR-0005 的四条边界逐条守住：`GameState` 不越界（脚手架收的是 `Observation`）、
prompt 在 TS 侧渲染（引擎只出结构化数值 + 术语表那一个 `display`）、
没给 Fable 输出写 `.d.ts`、Fable 运行时后端没进引擎工程（`ci.sh` 的白名单闸门绿）。
