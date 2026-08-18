# 77 — 分享载荷的形状与编解码

**状态**：done　**change**：本票一个 commit（见 `jj log`）　**fixed point**：`af7ec21f`（`kmxvpvkk`）

URL 分享要带的那份东西做出来了：**只有棋谱**（规则集 + mjai 事件流），压一次、base64url 编一次，
放得进 hash 不必再转义一层；解回来交给引擎 fold，事件流逐条相同、终局点数与顺位相同。
**页面一行没碰**（那是票 78），三道闸门每条都先红过一次。

一句话的意外收获：**票面写的 `deflate-raw` 用不得**——它没有校验和，改坏一个字符有 76% 的概率
「解得开，但是另一场对局」。换成同样是 `CompressionStream` 自带的 `deflate`（zlib，带 Adler-32），
代价 8 个字符，换回「读不动就是读不动」。实测数在 §4。

---

## 1. 落点一览

| 层 | 文件 | 做了什么 |
|---|---|---|
| 引擎 | `src/Janpo.Engine/Paifu.fs` | **`Paifu.stripAudit`**：值上的一次变换（`Decisions = []`、`Prompting = empty`） |
| 引擎 | 同上（注释） | `stripThinking` / `Thinking` / `encoder` 三处「URL 分享走 stripThinking」的旧说法改成事实 |
| 边界 | `src/Janpo.Web/Share.fs`（新，一行 `<Compile>`） | `Share`：F# 调 TS 的第二处；`ShareCheck`：浏览器侧闸门入口（照 `PaifuCheck` 的形态） |
| Agent 层之外 | `web/src/share/payload.ts`（新） | 压缩 + base64url，**零依赖**（`CompressionStream` / `btoa` / `atob`） |
| 闸门 | `web/scripts/verify-share.mjs`（新） | 浏览器里真压真解：往返 / 字符集 / 逐位置腐蚀 / 审计三样 / 长度记账 |
| 闸门 | `web/scripts/verify-browser.mjs`、`scripts/ci-web.sh` | 七趟变九趟（正跑 + 反向自证），文档里的计数一并改 |
| 用例 | `tests/Janpo.Engine.Tests/PaifuTests.fs` | `stripAudit` 三条（字段一个不少 / wire 上没有审计三样 / 回放出同一场） |
| 用例 | `web/tests/share/payload.test.ts`（新） | 编解码那一层六条（含逐位置腐蚀），`node --test` 跑，不必开浏览器 |
| 配置 | `web/tsconfig.json` / `web/package.json` | `src/share/**` 进类型闸门；`pnpm run verify:share` |

全量：dotnet 740 + 101 条、node 160 条（新增 6）、`./scripts/ci.sh` 全绿。

## 2. 变换的形状与命名

```fsharp
let stripAudit (paifu: Paifu) : Paifu =
    { paifu with
        Decisions = []
        Prompting = Prompting.empty
    }
```

**名字取 `stripAudit` 而不是 `forUrl` / `kifuOnly`**，三个理由：

1. **说的是「抹掉什么」，不是「给谁用」**——与同一族的 `stripThinking` 同一个句式，
   两者摆在一起就看得出「一个抹一段、一个抹整摞」。取 `forUrl` 会把用途焊进引擎，
   而引擎不知道有 URL 这回事。
2. **「审计数据」是 `CONTEXT.md` 自己的话**：DecisionRecord 那条词条的头一句就是
   「一次决策的完整审计数据」。`Prompting` 是它的另一半（prompt 的前置），一起抹掉正好齐。
   **没有新词进术语表**（也没动 `CONTEXT.md`）。
3. **它是值上的一次变换，不是第二个编码器**（裁决 26-7）：`decisions` 写成空表、`prompting` 写成空的，
   那两处本来就允许缺省，**`Paifu.Version` 一动不动**，编码器与解码器仍旧各只有一份。

「URL 是棋谱不是审计数据」这句话与两个实测数写在函数上方的注释里（票面要求的那条）。

## 3. 接口（票 78 要用的就是这三行）

```fsharp
// src/Janpo.Web/Share.fs —— F# 调 TS 的第二处（ADR-0005），跨界只传字符串
Share.toPayload  : Paifu  -> JS.Promise<string>                    // 牌谱 → hash 里那一段
Share.ofPayload  : string -> JS.Promise<Result<Paifu, string>>     // 载荷 → 牌谱，读不动给中文原因
Share.paifuText  : string -> JS.Promise<Result<string, string>>    // 中间那一站：载荷 → 牌谱原文
```

```ts
// web/src/share/payload.ts —— 这一层不认识牌谱，只认字符串
export async function encodePayload(text: string): Promise<string>;   // 压 + base64url
export async function decodePayload(payload: string): Promise<string>; // 信封 JSON：{"text"} 或 {"error"}
```

**为什么是 Promise**：`CompressionStream` 是异步的，因此过界的形状与 `Agent.ask` 同类。
**为什么解那侧回信封而不是抛**：与 `decide.ts` 同一种做法——票面要求「不许抛异常、不许静静地空手而归」。

**两层诊断分得清**（票 78 那句提示按前缀分岔）：

| 前缀 | 谁写的 | 意思 | 该劝人做什么 |
|---|---|---|---|
| `载荷读不动：…` | `payload.ts` | 这段字符本身不对 | 链接是不是被截断 / 抄漏了 |
| `牌谱读不动：…` | `Share.ofPayload` | 载荷解得开、里面那份牌谱不合形状 | 这份牌谱太新或太旧（引擎的英文诊断跟在后面） |

实测四句（`node scripts/verify-share.mjs` 每次都印）：

```
改坏中间一个字符：载荷读不动：解压不开，多半是链接被截断或抄错了（载荷 4842 字符，浏览器说 TypeError: Failed to fetch）
末尾截掉八个字符：载荷读不动：解压不开，多半是链接被截断或抄错了（载荷 4834 字符，浏览器说 TypeError: Failed to fetch）
混进一个加号：载荷读不动：载荷里混进了 base64url 之外的字符「+」
空的：载荷读不动：分享链接里没有载荷
```

「浏览器说」那半截是故意标明出处的：Chrome 在解压失败时给的原话是 `TypeError: Failed to fetch`
（解压流是拿 `Response` 抽干的），**听起来像网络出了事，而这一趟一个请求都没发**——
错的诊断比没有诊断更贵，所以真正的毛病写在前头。

## 4. `deflate-raw` 不能用：票面那一条被实测推翻

票面写「压缩用 `CompressionStream('deflate-raw')` 一档就够」。它压得更小（少 6 字节），
但**没有校验和**，于是闸门②（「改坏一个字符必须当场红，且红在载荷读不动」）在它上面根本不成立。

一份 7,463 字符的半庄载荷，**逐个位置各改坏一个字符**（换成 base64url 字母表里的下一个字符，
不靠字符集那道判读接住），三档压缩各扫一遍（Node，与浏览器同一个 zlib）：

| 格式 | 压完 | 载荷 | 读不动 | 解得开 | 其中与原文逐字相同 |
|---|---|---|---|---|---|
| `deflate-raw` | 5,597 字节 | 7,463 字符 | 1,797 | **5,666** | 2 |
| `deflate`（zlib，Adler-32） | 5,603 字节 | 7,471 字符 | **7,468** | 3 | **3** |
| `gzip`（CRC32） | 5,615 字节 | 7,487 字符 | 7,477 | 10 | 10 |

**`deflate-raw` 那 5,666 次是「解得开，但内容不是原来那份」**——真在浏览器里跑同一趟看得更清楚
（Chrome，东风战载荷 4,834 字符）：

```
逐位置腐蚀 4834 次：读不动 1674，解得开但与原文逐字相同 2，解出另一份 5+
    第 82 位改坏之后解出了另一份（40296 字，头 48 字：{"verseon":3,"ruleset":{"seat_count":4,"length":）
    第 86 位改坏之后解出了另一份（40296 字，头 48 字：{"version"83,"ruleset":{"seat_count":4,"length":）
只有 1674/4834 个位置读不动：这道闸门几乎没开口
```

也就是说：**分享链接被聊天工具截断、被人手抄错一位，对面看到的会是一场悄悄不同的对局**，
而 `Paifu.decoder` 常常照样读得动（上面那两条只是恰好在 JSON 结构上炸了；改到牌的字符上就不炸）。

`deflate` 的 Adler-32 把这条堵死：**每一位改坏都读不动，只有 3 个位置解得开而且解出来逐字相同**
（那几位落在末尾不承载信息的填充位上，改了也不改变字节）。代价是 6 字节 / **8 个字符（+0.1%）**。
两档都是 `CompressionStream` 自带的，一个依赖都不引；`deflate` 还比 `deflate-raw` 早进浏览器
（Chrome 80 vs 103）。**取 `deflate`。**

## 5. 长度记账（浏览器里真压的，不是先验）

`ShareCheck.sample` 让引擎现打一整场（四家随机，种子 2088），然后走完整条路。
每次跑闸门都会印这几行，因此这张表不会腐烂：

| 语料 | 事件 | 牌谱全量 | 只带棋谱 | deflate 后 | **base64url 后** |
|---|---|---|---|---|---|
| 东风战一整场 | 771 条 | 40,954 字符 | 40,296 字符 | 3,631 字节 | **4,842 字符** |
| 半庄一整场 | 1,401 条 | 73,123 字符 | 72,465 字符 | 5,790 字节 | **7,720 字符** |
| 票 26 那份真导出件 | 45 条 | **78,205 字符** | 3,305 字符 | 814 字节 | **1,086 字符** |

（前两行的「牌谱全量」是棋谱加上闸门拌进去的那一条审计标记，不代表真实审计数据的体量。）

**第三行才是「URL 只带棋谱」那条裁决的证据**：`reports/26-paifu-sample.json` 是真跑 DeepSeek
（medium 思考）导出的那一份，**才 45 条事件、七条决策记录**，全量就 78,205 字符——
其中棋谱只占 3,305，**96% 是审计数据**，而那一场连一局都没打完。
半庄打满是 1,401 条事件、四席都问模型的话，这个比例只会更悬殊。

与票面的先验数（python zlib level 9：半庄 1617 条事件 → base64url 7,444 字符）对照：
我这一场是 1,401 条事件、7,720 字符，**每条事件 5.5 字符**，与先验的 4.6 字符/条同量级
（差在事件条数、种子与压缩实现）。**以这张表为准。**

跑法：

```
cd web && pnpm run fable && pnpm run verify:share
node scripts/verify-share.mjs --paifu ../.scratch/llm-riichi-arena/run/reports/26-paifu-sample.json
```

## 6. 三道闸门，每条先红一次

闸门在 `web/scripts/verify-share.mjs`（进 CI，两趟：正跑 + 反向自证），
另有 `web/tests/share/payload.test.ts` 六条不开浏览器的用例。

### 6.1 真往返（编 → 解 → `Paifu.decoder` → `Replay`）

绿的时候：

```
东风战：事件 771 条　全量 40954 字符 → 棋谱 40296 字符 → 3631 字节 → **4842 字符**（压缩比 8.3:1）
　　往返：事件流逐条相同 = true　回放逐条相同 = true　终局点数 [27000,25000,25000,23000]　顺位 [1,2,3,4]
半庄：事件 1401 条　全量 73123 字符 → 棋谱 72465 字符 → 5790 字节 → **7720 字符**（压缩比 9.4:1）
　　往返：事件流逐条相同 = true　回放逐条相同 = true　终局点数 [25500,26500,23500,24500]　顺位 [2,1,4,3]
```

**先红一次**：把 `Paifu.stripAudit` 改成顺手丢一条事件（`Events = List.tail paifu.Events`），重编重跑——

```
分享载荷验收没过：
东风战：解出来的事件流与原牌谱不同：第 0 条：原牌谱 {"type":"start_game","names":["random",…]}，
                                     载荷里 {"type":"start_kyoku","bakaze":"1z",…}
半庄：解出来的事件流与原牌谱不同：第 0 条：…（同上）
```

顺带看清一件事：那一趟 `回放逐条相同 = true` **仍然是 true**——`start_game` 不属于任何一局，
回放本来就不产出它。**两条断言各守各的**：`same_events` 守牌谱里那条流，`same_replay` 守 fold 出来的那条。

### 6.2 改坏一个字符（反向自证）

正跑每次都扫**全部位置**，因此它不是「写了一条断言」而是每趟开口四千多次：

```
逐位置腐蚀 4842 次：读不动 4840，解得开但与原文逐字相同 2，解出另一份 0
```

断言有两条：① 解出另一份 = 0（**一次都不许**）；② 读不动的位置数不得低于九成
（否则这道闸门几乎没开口，判据 3）。

**先红一次**：把 `FORMAT` 换回 `deflate-raw`（§4 那段输出），两条断言同时红，
而且单点腐蚀那一句**红在了别处**（`载荷里那份牌谱读不动：Given an invalid JSON…`），
正是票面要求「红在载荷读不动」要防的那种。

`node --test` 那一层同一条属性也钉着，红的样子是：

```
✖ 改坏一个字符：每个位置要么当场读不动，要么解出来与原文逐字相同
  AssertionError: 第 81 位改坏之后解出了另一份东西（11150 字符，头 48 字：{"3ersion":3,"events":[{"type":"dahai","actor":0）
```

### 6.3 载荷里没有 thinking / prompt 尾部 / 那把假 key

做法照抄票 34：**写死的假 key 字面量**（`sk-janpo-fake-key-SHARE-URL-bing-1c7e05`，全 ASCII，
一眼就知道是假的，绝不从 `/tmp/deepseek_key` 之类的地方读），外加两段只可能出现在审计数据里的标记。
闸门把它们拌进牌谱的决策记录（thinking、prompt 尾部、`output` 与 `fallback` 里的 key——
**那正是票 36 核出来的三条夹带通道**），再看解出来的载荷里还有没有。

**阳性对照**（票 34/36 的规矩）：上路前那份牌谱必须真的带着这三样，且决策记录数 ≥ 1、
带 thinking 的记录数 ≥ 1、preamble ≥ 1；否则这条断言什么都没证明。

**先红一次（两个方向都试了）**：

```
# ① 变换没抹干净（--poison 拿上路前那份当解出来的那份，CI 里每次都跑这一趟）
东风战：载荷里出现了thinking
东风战：载荷里出现了prompt 尾部
东风战：载荷里出现了那把假 key
半庄：（同上三条）

# ② 阳性对照自己失效（把拌进去的那条记录去掉）
东风战：上路前那份牌谱没带审计数据（决策记录 0 条、带 thinking 0 条、preamble 1 份）——「载荷里没有它们」于是什么都没证明
东风战：上路前那份牌谱里就找不到thinking——这条断言是空的
```

### 6.4 顺带钉住的两条

- **字符集**：`+` `/` `=` 一个都不许出现（`/^[A-Za-z0-9_-]+$/`）。反向自证：
  把 `toBase64Url` 里那三个 `replaceAll` 拿掉，`node --test` 当场红并把出现 `+/=` 的那串印出来。
- **载荷解出来必须是 UTF-8**（`TextDecoder(…, { fatal: true })`）：默认那档会把坏字节替换成 U+FFFD，
  于是坏载荷**静静地**变成一份读不动的牌谱，红在下一层——而它本来就该红在载荷这一层。

## 7. 闸门为什么不碰页面

`verify-share.mjs` 只 `goto` 一次首页（要一个能跑 `CompressionStream` 与 Fable 模块的浏览器上下文），
**一个 testid 都不点**：语料由 `ShareCheck.sample` 让引擎现打（同种子必然同一场）。三个好处：

1. 与票 70 正在拆的 `TablePage.fs` **零耦合**——它红了不会牵连这道闸门，反之亦然；
2. **打得完整场**，因此「终局点数与顺位相同」那两条断言真的开得了口
   （驱动 UI 走完半庄要几千次单步）；
3. 快：正跑 1.1s、反向自证 0.5s（九趟合计仍在 6 秒量级）。

代价：审计那三样是闸门自己拌进去的，不是真模型产出的。**这是有意的**——
`web/scripts/fake-endpoint.mjs` 那个假端点不产出 thinking，而真模型不进 CI（硬约束 4）；
而且这一票要验的是**变换**，不是记录怎么来的。真语料那一路由 `--paifu` 手跑
（§5 第三行就是拿票 26 那份真导出件跑出来的，它带着真 thinking、真 prompt）。

## 8. 关键取舍

全部见 `DECISIONS.md` 的「## 77」段。三条最要紧的：

1. **77-1 压缩取 `deflate` 而不是票面写的 `deflate-raw`**（§4）：闸门②在 raw 上不成立，
   而票面的闸门是硬要求。代价 8 个字符。
2. **77-2 `stripAudit` 抹的是「决策记录 + prompt 前置」整摞，`stripThinking` 原样留着**：
   后者现在没有生产调用方了（见 §9 待审）。
3. **77-3 解那侧回信封 JSON，中文原因由 TS 那侧写**：措辞只有一处，F# 原样带过来，
   靠「载荷读不动：」这个前缀分层。

## 9. 留给人的待审项

- **`Paifu.stripThinking` 现在没有生产调用方了**（URL 分享改走 `stripAudit`），只剩用例在钉它。
  **本票没删**：删它要动票 26 的裁决（26-7）与它的三条用例，而「测试只许往更硬的方向改」。
  要删的话它与 `PaifuExportTests` 里那条一起走，得有单票授权。
- **载荷长度还没有上限判据**：7,720 字符的 URL 在浏览器地址栏与主流聊天工具里没问题，
  但**没有实测过**「哪一家会截断」。票 78 接地址栏时若要一条阈值，我的建议是
  **超过 8,000 字符就劝人改用 JSON 文件**（判据：IE 之外的浏览器普遍支持 32 K 以上，
  而 8,000 已经能装一整场半庄，再长的对局本来就该走文件）。**这个数是建议，不是实测。**
- **`firstMismatch` 在 `PaifuCheck.fs` 与 `Share.fs` 里各一份**（约 10 行，措辞不同：
  「牌谱 / 回放」对「原牌谱 / 载荷里」）。没合并的理由与 26-9 同款：合并要把两个标签参数化，
  读起来更绕；真要收敛等第三个调用方出现。

## 10. code-review 结论

两轴顺序各跑一遍（fixed point `af7ec21f`；这个跑批派不了 sub-agent，按 RUNBOOK 自己顺序跑）。

### Standards（`docs/agents/fsharp-style.md` + 仓库既有约定）

**无 blocking。** 逐条过：

- 规则 1/2/3（不许从里往外读、lambda 包一层调用、三层变换嵌套）：新代码里无命中；
  `Share.toPayload` 与 `ShareCheck.sample` 都是一条从左往右的管道，
  `Option.map (Event.encoder >> Encode.toString 0)` 用的是 `>>`。
- 规则 5：一个 `let mutable` 都没新增（`check-style.sh` 的预算仍是 2）。
- 规则 8（`f (x)` 的多余括号）与规则 2 的窄形状：`check-style.sh` 锁的两条干净。
- **改动期间修的两处**（都在写的时候就地改了，不算 blocking）：
  ① 腐蚀扫描的「解出另一份」原本用样本数组的长度当计数，红的时候永远印 5——分成计数与样本两个字段；
  ② `payload.ts` 解压失败那句原本只抄浏览器的 `TypeError: Failed to fetch`，误导性强（§3）。

### Spec（票的验收框 + ADR-0002 / ADR-0005 + `CONTEXT.md` 的三条词条）

**无 blocking**，一处**有意偏离**（§4 的 `deflate`，已记进 `DECISIONS.md`）。三处值得写下来：

- **没碰 `Paifu.Version`**：字段含义与形状一个没动，只是少写几段（那几段本来就允许缺省）。
  `same_version` 那条断言顺手钉住了「读进去是 3、解出来还是 3」。
- **没碰页面、没碰 `web/src/agent/**`、没碰 `docs/adr/*`、没碰 `CONTEXT.md`**。
  动到的共享文件只有三处：`Janpo.Web.fsproj`（**只加一行 `<Compile Include="Share.fs" />`**，
  排在 `PaifuCheck.fs` 之后）、`verify-browser.mjs` / `ci-web.sh`（加两趟闸门与计数）、
  `web/tsconfig.json` / `package.json`（新目录进类型闸门、加一条 `verify:share`）。
- **没做前瞻设计**：没有载荷版本号前缀、没有「压缩算法可选」。压缩格式是文件里的一个常量，
  换它要改代码——ADR-0002 明写「真要更短是渲染侧的优化，不需要新格式」。

### nitpick（只记录，未改）

- `ShareCheck.check` 有 50 行、三层 `match`（读不动 / 回放不动 / 都好），形态与 `PaifuCheck.check` 同款。
  它是闸门的报告拼装，不在任何热路径上。
- 报告 JSON 的字段是手拼的（没有类型），与 `Golden.check` / `PaifuCheck.check` 一致，暂时照旧。
- `verify-share.mjs` 里那三段 `page.evaluate` 各自 `import` 一次模块（浏览器会缓存），
  没抽成一个帮手——抽了反而要把函数序列化过界。
