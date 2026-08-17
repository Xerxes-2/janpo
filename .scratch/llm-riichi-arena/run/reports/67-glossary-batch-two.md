# 67 — 第二批收词：四条攒齐的术语进 `CONTEXT.md`

**状态**：done　**工作区**：`janpo-ws-a`　**fixed point**：`41f19d63`（change `oxsunqkr`）

一句话：`CONTEXT.md` 改了**三处词条**（Sekinin Barai / Doujun + Riichi / Honba 的点数），
第四处「重试判据」**核过一致、一字未动**；顺带把 `KanProperties.fs` 那条自称执行体的注释改对
（DECISIONS 66-5）。`./scripts/ci.sh` **EXIT=0**。改动面只有 `CONTEXT.md`、
`tests/Janpo.Engine.Tests/KanProperties.fs` 的一段 doc 注释、票文件、本报告与 `DECISIONS.md`。

---

## 1. 重试判据（47-B）：核过一致，词条未动

**先纠一处票面事实**：词条不是票 48 收的——`jj log` 逐 commit 查过，票 48（`2556ef21`）收的是
PromptTemplate / Persona / RenderVersion / 打码四条；「重试判据」是**票 53** 随判据清单一起收的
（`85d09bf5`「判据清单 + 术语表收『重试判据』」），措辞取自 DECISIONS 47-B 的建议。
它本来就写在票 47 落地**之后**，因此与实际行为吻合不奇怪——但仍逐句对了 `retry.ts` 与 `loop.ts`：

| 词条声称 | 代码里的执行体 | 结论 |
|---|---|---|
| 「再问一遍可不可能不一样」是唯一判据 | `retry.ts` 的 `retryOf`（4xx→不值得、5xx→值得） | ✓ |
| 不可能的第一次就收手，直接 Fallback | `loop.ts:285` `if (!retry.worth) break;` → `refuse` → 引擎 `Fallback.action` | ✓ |
| 可能的（超时、限流、5xx、格式跑偏、动作 id 非法）才重试 | `loop.ts` 里这五类全部 `retry: WORTH` | ✓ |
| 408 / 425 / 429 是 4xx 的时机例外 | `retry.ts` 的 `TIMING_4XX` | ✓ |
| 判不出来一律当值得重试 | `retryOf`：`errorMessage === null`、`statusOf` 认不出 → `WORTH` | ✓ |
| 「没重试」与「少试了」一眼分得开 | `loop.ts` 的 `gaveUpBecause`：两种收尾（`没有重试：…` / `重试 2 次仍无结果`） | ✓ |
| _Avoid_「不是状态码码表」 | `NAMED_4XX` 只影响那句话怎么说，不影响分类（47-1 明写） | ✓ |

**谁在执行**：`web/src/agent/retry.ts`（判据本体）+ `loop.ts` 的提前 break；
闸门是 `web/tests/agent/retry.test.ts`（9 条，含两条真调 `piAsk` 钉接线措辞）、
`loop.test.ts` 的四条（401/404 只问一次、429/503 重试到上限）、`redact.test.ts` 的 401/429
出口用例，以及 CI 第十一道打码闸门实跑出的页面那句话（本次 CI 里它照常跑过）。

## 2. Sekinin Barai（59-3 + 65-4 的同一句）：最终文本

> **Sekinin Barai（责任支付）**：
> 由喂出关键那一副的家承担全部或部分点数的规则。大三元 / 大四喜由副露凑齐的包在任何规则集下都成立、
> 不可配：包牌家自摸付全额、荣和与放铳者各付一半，**包牌荣和时本场棒也整根由包牌家付**
> （见 Honba 的点数），供托照常归和了者。
> **大明杠→岭上开花那一支是 `Ruleset.MinkanRinshanSekinin` 开关，默认关**——它是开关而不是定死的行为，
> 理由是现实里两种口径都存在：天凤不采用（2025 整年牌谱里的每一处都按常规自摸三家分摊），
> 另一些规则由喂杠的那家独付。默认取天凤侧。

原词条「大明杠等情形下由特定放铳者承担…」两处不准（包的责任者不必是放铳者；「大明杠等」把
默认关的那一支说成了主体），一并改准。**谁在执行**：包的劈账在 `Score.hora`（`Sekinin` 有值时的
荣和劈半 / 自摸独付支）；大明杠那一支的开关在 `GameState.sekininOf`（`state.Ruleset.MinkanRinshanSekinin`
条件）+ `Ruleset.yonma` 默认 `false`。闸门：`KanTests.大明杠后的岭上开花` 两条（默认口径 / 开关口径，
绝对点数）、`KanProperties` 的两条包不变量、`PaifuDifferentialTests.默认规则集就是牌谱那一套天凤规则`
（断言该字段恒 false）与固件重放零差异（固件里含票 59 收的大明杠→岭上开花场，撤修法当场红过）。
词条里「任何规则集下都成立、不可配」说的是引擎结构（无开关可关），其中大四喜那一支无语料佐证
（65 号报告第三节），词条不声称语料验证。

## 3. Doujun + Riichi（63-A）：最终文本

> **Doujun（同巡）**：
> 从自己打牌到自家下一次**摸打**为止——摸牌解除，**自家鸣牌同样解除**（天凤实测：见逃之后自家鸣牌
> 接着打牌，荣和照样放行；引擎的清除点取在鸣牌那一步——鸣牌到打牌之间自家没有荣和机会，行为等价）。
> Furiten 的两种里较短的那一种就按它计。

> **Riichi（立直）**：
> 门清听牌时的宣言，附带立直棒与振听、一发等后续约束。
> **立直后暗杠的天凤判据是「禁送り杠 + 听不变」**：杠的只能是刚摸进的第四张，杠前杠后待ち逐张相同
> （也不许杠掉自己的待ち牌）——天凤手册明文「牌姿が変わるのは可」，**不附加面子构成的要求**
> （2025 整年 1,893,891 局实测：只因面子构成会变而被拦的暗杠 20 处，天凤 20 处全放行）。
> 竞技侧（M-League、日本プロ麻雀連盟、最高位戦、WRC）另加「每种和了读法都把那牌种当暗刻」的口径，
> 那一条是 `Ruleset.RiichiAnkanMentsuUnchanged` 开关，默认关＝天凤。

**谁在执行**：Doujun 的两个清除点是 `PlayerState.draw` 与 `PlayerState.addNaki`（后者天然排在
`GameState` settle 见逃之后，「鸣走能荣的那张」也被清）；暗杠判据整条在 `RiichiState.allowsAnkan`
（含「不许杠自己的待ち」的显式后半句与开关条件）。闸门：`HoraTests.见逃后自家鸣牌再打牌，同巡振听解除`、
`PaifuDifferentialTests.立直后的暗杠与鸣后的荣和各自在固件里走到几次`（≥25 / ≥54，从事件流自己数）、
固件重放零差异（E 族 6 场、F 族 4 场都在固件里，撤修法各自红过）、`RiichiTests` 的开关用例三条、
`默认规则集…` 断言（该字段恒 false）、`MinogashiStreamProperties` 三方闸门（掩蔽流与引擎共用
`addNaki`，两侧自动同步）。

## 4. Honba 的点数（65-4）：最终文本

> **Honba 的点数**：
> 一本场 300 点（`Ruleset.HonbaPoints`），荣和由放铳者付、自摸由付家分摊。
> **包牌荣和是例外：本场棒整根由包牌家付**（2025 整年语料的 4 场包牌荣和 4/4 如此——役满劈半
> 只劈和了点，本场不劈），供托照常归和了者、不受包影响。

原句「和了时由放铳者或三家分担」拆成荣和/自摸两支再挂例外；「三家」也改成「付家」——包牌自摸
只剩责任者一家付，原措辞在那一支上本来就不准。**谁在执行**：`Score.hora` 的 `honbaPayers`
（自摸取 `charges` 的付家、荣和有包取 `[liable]`、没包取 `[transfer.Target]`）。闸门：
`ScoreTests` 三条具名用例（包牌荣和 / 包牌家自己放铳 / 包牌自摸，绝对 deltas）、
`PaifuDifferentialTests.包牌的荣和与自摸在固件里各走到几次`（荣和 ≥4、自摸 ≥1）、
固件重放零差异（G 族 4 场 + 包牌自摸 1 场都在固件里，撤修法当场红过）。

## 5. 顺带一行活：`KanProperties` 的注释（66-5）

旧注释声称「下面那条属性就是『欠账恒不过 1 张』的执行体……当场报红」——票 64 的 live-fire
恰恰证明了相反（把 `completeKan` 的还账弄坏，9 条属性照样全绿；生成器到不了连杠夹欠账）。
这是判据 2 点名的那个形状：**记录声称执行体存在而它不存在**。已改成显式事实：

> **这个 fold 结构上只吐得出 0 与 1**，`pendingKanDora ≤ 1` 那条合取因此是恒真式，
> 这份属性也当不成「欠账恒不过 1 张」的执行体——生成器到不了连杠夹着欠账的局面
> （票 64 把还账故意弄坏实证：这里 9 条照样全绿）；真正守着它的是 KanTests 的
> 连杠具名用例与真牌谱对拍，两处都红过。

属性与断言本身一字未动（测试只许改硬；注释改的是「谁在守」的归属，不是行为）。

## 6. 体例上的斟酌

1. **不写票号**：词条里的证据一律写成语料数字（「2025 整年 1,893,891 局实测」「4/4」），
   照纯空听词条的先例；票号只出现在本报告与代码注释里。
2. **两个开关不单开词条**：`MinkanRinshanSekinin` 挂在 Sekinin Barai 下、
   `RiichiAnkanMentsuUnchanged` 挂在 Riichi 下——照 `KokushiAnkanChankan` 写在 Kan 词条里、
   `SanchaHoraRyuukyoku` 写在途中流局词条里的既有体例；59-3 / 63-A 的提案给的也是这个选项。
3. **没有强加 _Avoid_**：被改的四处词条（规则判定节）原本都没有 _Avoid_ 列表，邻近词条多数也没有；
   _Avoid_ 是新开词条的体例（票 48 那批），扩写既有词条不硬加。
4. **Sekinin Barai 里那半句本场归属**：票面第四条只点名 Honba 词条，但它引用的 65-4 提案明写
   「Sekinin Barai 词条宜补同一句」，且本票第二条本就要动这个词条——写成一个指回
   「（见 Honba 的点数）」的短句，两处不重复展开。判断记在 DECISIONS 67-2。

## 7. 验证

- `./scripts/ci.sh` **EXIT=0**（fantomas --check、风格闸门、dotnet 全量、Biome、tsc、
  Agent 层用例、七趟浏览器闸门、打码闸门全过）；`dotnet fantomas .` Formatted 0 / Unchanged 152。
- 词条里出现的每个标识符（`Ruleset.MinkanRinshanSekinin`、`Ruleset.RiichiAnkanMentsuUnchanged`、
  `Ruleset.HonbaPoints`）都 grep 过、存在且默认值与词条一致（`yonma` 里两个开关都是 false）。
- `jj diff --from 41f19d63 --stat`：只有 `CONTEXT.md`、`KanProperties.fs`、票文件、本报告、
  `DECISIONS.md` 五个文件。

## 8. review 记录（Standards + Spec，fixed point `41f19d63`，顺序自跑）

**Standards**：F# 改动只有 doc 注释，fantomas 与风格闸门过；`CONTEXT.md` 体例照既有词条
（扩写不换格式、不加小标题）。收工前重过判据 2 / 4 / 5：三处词条的每一句声称都在 §2–§4
指得出执行体；恒真式那条注释按判据 4 写成显式事实；测试断言零改动。无 blocking。

**Spec**（票面四条 + 三条纪律 + 两条边界）：四条逐条落地（§1 核对、§2–§4 改词条）；
判据 2 的自问四条都有落点；体例三条守住（§6）；只碰了授权的文件；CI 全绿。
**一处票面事实修正**：「重试判据」是票 53 收的，不是票 48（§1，`jj log` 为证）——
不影响任何验收内容，票文件那行已括注。

## 9. 留给人的待审项

1. Sekinin Barai 词条那半句本场归属（§6.4）：若认为超出「四处」的字面授权，删那半句
   （「**包牌荣和时本场棒也整根由包牌家付**（见 Honba 的点数）」）即可，Honba 词条独立成立。
2. 「重试判据」词条一字未动——若下次有人改 `retry.ts` 的分类（比如 429 加退避），
   记得回头看这条词条（47-3 的翻转条件在 DECISIONS）。
