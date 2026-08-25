// 浏览器里那几趟闸门，跑在**同一条跑道**上（票 56）：一个 Chrome 进程、
// 一台 `vite preview`（托管 dist/）、一台 `vite dev`（托管源码形态的 Fable 输出），
// 每一趟各开自己的 page / context。
//
// **跑几趟、各是哪一趟、各是哪张票的验收，唯一的真源是下面那张 `gates` 表**（票 106）。
// 从前这里、`scripts/ci-web.sh` 与各 `verify-*.mjs` 的文件头各拄一份「几趟」，于是它们各自漂：
// 票 92 / 90 / 89 各加一趟，每次两侧都要改同一行，而本票接手时这个数在仓库里
// 同时写着十一、十三、十五、十七与十八五种说法，**而真的那个只在这张表里**。
// 现在趟数一律从 `gates` 现算（它跑起来自己会印），加一趟就是往那张表里加一项，
// **别处一个字都不用改**——`scripts/check-single-source.sh` 盯着这一条。
//
// **地址不是随便开的**（票 71）：只有曳光弹与首页那两趟开 `/`（它俩量的就是首页），
// 其余全开 `?table=1`——首页从此自动播，而要点、要读牌桌的闸门靠的是
// 「默认暂停」那一页（`Playback.initial`）。`verify-tracer` 那一趟三个地址都开，
// 理由写在它自己的文件头上；`verify-inbound` 那一趟两个地址都开（分享链接从 `?table=1` 复制、
// 导入入口在 `/` 上），理由同样在它自己的文件头上。
//
// **一趟都没少、一条断言都没拆**：每一趟调的就是 `verify-*.mjs` 里那个同名函数，
// 单跑（`pnpm run verify:board` 等）与合并跑跑的是**同一段代码**，只是跑道不同。
//
// 单跑仍然是调试时的第一手段：这里每趟印的抬头就是它单跑时的命令。
//
// 跑法：`cd web && pnpm run fable && node node_modules/vite/bin/vite.js build && pnpm run verify:browser`

import { failure, mark, openLane, printFailures } from "./browser-lane.mjs";
import { verifyAssist } from "./verify-assist.mjs";
import { verifyBoard } from "./verify-board.mjs";
import { verifyBubbles } from "./verify-bubbles.mjs";
import { verifyExport } from "./verify-export.mjs";
import { verifyGolden } from "./verify-golden.mjs";
import { verifyHome } from "./verify-home.mjs";
import { verifyHuman } from "./verify-human.mjs";
import { verifyInbound } from "./verify-inbound.mjs";
import { verifyRedaction } from "./verify-redaction.mjs";
import { verifyReview } from "./verify-review.mjs";
import { verifySeats } from "./verify-seats.mjs";
import { verifySetup } from "./verify-setup.mjs";
import { verifyShare } from "./verify-share.mjs";
import { verifyStaleAsk } from "./verify-stale-ask.mjs";
import { verifyToolSearch } from "./verify-toolsearch.mjs";
import { verifyTracer } from "./verify-tracer.mjs";

/**
 * 反向自证那一趟（票 34）：`--poison` 往导出物里拌一把 key，那条断言**必须**当场红，
 * **且必须是因为那把 key**。两种失法各报各的话——从前它是 `ci-web.sh` 里的一段
 * shell（跑一趟、看退出码、再 grep 一次红的原因），这里逐字搬过来，一条没少。
 */
async function poisonProof(lane) {
  const failures = await verifyExport(lane, { turns: 12, poison: true });
  const lines = failures.flatMap((each) => each.lines);
  const caught = lines.find((line) => line.includes("里出现了 API key"));

  if (failures.length === 0)
    return failure("反向自证没过：拌了 key 的导出物竟然过了闸门——那条断言等于没有。", []);
  if (caught === undefined)
    return failure("反向自证没过：闸门是红了，但不是因为那把 key（红的原因见下）。", lines);

  console.log(`拌了 key 的导出物被闸门当场逮住：${caught}`);
  return [];
}

/**
 * 分享载荷那一道的反向自证（票 77）：`--poison` 拿**上路前**那份牌谱当解出来的那份
 * ——等于「`Paifu.stripAudit` 没抹干净」，于是「载荷里没有审计那三样」必须当场红，
 * **且必须是因为那三样**。与上面那一道同一种写法：两种失法各报各的话。
 */
async function strippedProof(lane) {
  const failures = await verifyShare(lane, { poison: true, withSweep: false });
  const lines = failures.flatMap((each) => each.lines);
  const caught = lines.find((line) => line.includes("载荷里出现了"));

  if (failures.length === 0)
    return failure("反向自证没过：没抹干净的载荷竟然过了闸门——那三条断言等于没有。", []);
  if (caught === undefined)
    return failure("反向自证没过：闸门是红了，但不是因为审计那三样（红的原因见下）。", lines);

  console.log(`没抹干净的载荷被闸门当场逮住：${caught}`);
  return [];
}

/**
 * 这条跑道上的那几趟。**这张表就是「几趟」的唯一真源**（票 106）：
 * 加一趟 = 往这里加一项，别处没有第二个地方写着趟数。
 * `how` 是它单跑时的命令——红了照抄就能只重跑这一趟。
 */
const gates = [
  // 票 19/35/37/38：同一种子在浏览器里跑出来的终局点数与顺位，必须与 `janpo kyoku` / `janpo game` 逐项相同；
  // 顺带：曳光弹藏在 `?dev=1` 后面、首页页脚有回仓库的外链与一句许可、副露看得出被鸣的那张与来源。
  {
    name: "浏览器内曳光弹对拍（与 dotnet 侧逐项对照；顺带验首页：没有曳光弹、有回仓库那一行、副露看得出来源）",
    how: "node scripts/verify-tracer.mjs",
    run: (lane) => verifyTracer(lane),
  },
  // 票 71：访客打开 `/` 什么都不用配，第一眼就是一桌牌在走（ADR-0003 的 Demo Paifu）。
  // 四条断言：牌桌在动（隔一会儿采两次，手数不同）、没有配桌控件、
  // 有一条去 `?table=1` 的路（真点过去，且那一页默认暂停）、页脚照旧（票 37）。
  // 它顺带是「资产用 fetch 拉、不打进 bundle」那条路径的唯一无头证据。
  // 票 75 又在它尾上接了两条：**默认上帝视角**（裁决 71-8，带阳性对照：切回座位
  // 视角后他家必须扣回去）与**时间轴真的拖得动**（在滑块上真点、步进一个来回后
  // DOM 逐字相同、拖回 0 是开局那一瞬、点局号落在那一局的开局帧）。
  {
    name: "首页就是一局回放：牌桌在动、没有配桌控件、上帝视角、时间轴拖得动",
    how: "node scripts/verify-home.mjs",
    run: (lane) => verifyHome(lane),
  },
  // 票 44：牌桌上**人能看见的八项**各一条断言，外加方位（切了视角布局要跟着转）。
  // 它把其余座位换成「有主见」那一档（票 42）才走得到立直与供托：均匀随机几乎不立直。
  // 票 51 又在它尾上接了一段：**副露里横放那张的位置就是来源**，另走两局把九种槽位结果
  // （吃 / 碰左中右 / 大明杠三格 / 加杠叠放 / 暗杠）摆齐，逐组与**绝对座位**对拍，
  // 并且五个视角看到的位置必须逐字相同——位置的参照系是**副露方自己**。
  {
    name: "牌桌上人看得见的八项 + 副露的位置就是来源（真对局）",
    how: "node scripts/verify-board.mjs",
    run: (lane) => verifyBoard(lane),
  },
  // 票 21：`tests/fixtures/golden/dual-target.json` 里每条用例的每个字段的每一行，
  // 在浏览器里跑出来都要与文件一致（跑的是 Fable 的输出本身）。
  // 同一份用例文件在 dotnet 侧由 `GoldenSuiteTests` 跑——**两侧读同一份数据**。
  {
    name: "浏览器内黄金用例（与 tests/fixtures/golden/ 逐字段逐行对照）",
    how: "node scripts/verify-golden.mjs",
    run: (lane) => verifyGolden(lane),
  },
  // 票 26 的验收：点一下真的下载得到一份牌谱，而那份字节回放出逐条相同的事件流。
  // 四家随机选手，**一个网络请求都不发**（M1 增量约束 6）；带真模型的那一档是 `--llm`，人工跑。
  // 票 34 把「导出物里没有 key」一并放进这一道：假 key 灌进 localStorage、模型坐席不给。
  {
    name: "浏览器内牌谱导出（下载事件 + 把下下来的字节 fold 回去）",
    how: "node scripts/verify-export.mjs --turns 40",
    run: (lane) => verifyExport(lane, { turns: 40 }),
  },
  // 同一道闸门再跑一趟**打完整场**的（票 39）。两趟不是重复：上面那趟停在局中
  // （牌桌与回放都读 `GameState`），这一趟走到终局那一屏（两边都改读 `Game` 的精算后点数）。
  // 种子 447：那一场终局时场上还剩着供托，因此「局末点数」与「精算后点数」真的不同
  // ——不同才验得出口径。仍旧一个请求都不发。
  {
    name: "浏览器内打完一整场：终局那一屏的点数只许有一种说法",
    how: "node scripts/verify-export.mjs --to-end --seed 447",
    run: (lane) => verifyExport(lane, { toEnd: true, seed: "447" }),
  },
  // 票 34 的反向自证（被它按红的是上面「走 40 手」那一趟）：拿同一份导出物拌一把 key 进去，
  // 上面那条断言**必须**当场红。
  // 没这一趟的话，「代码里有那行断言」与「那行断言真的拦得住东西」就又分不开了
  // （立这张票的起因就是上一次只核到了前者）。手数压到 12：它只需要导出得成。
  {
    name: "反向自证：拌了 key 的导出物必须让那道闸门变红",
    how: "node scripts/verify-export.mjs --turns 12 --poison（它单跑时**该**以 1 退出）",
    run: poisonProof,
  },
  // 票 36：上面那两趟守的是「key 只是躺在 localStorage 里」。这一趟守的是另一半：
  // key **真的交给了端点**，而端点把它原样抠在报错里回了回来。全程本机（本地假端点），
  // 不出网、不花 token。它自带阳性对照（打码记号必须在），因此不需要另一道 poison。
  {
    name: "回显 key 的自建网关：报错原文进牌谱前必须已打码",
    how: "node scripts/verify-redaction.mjs",
    run: (lane) => verifyRedaction(lane),
  },
  // 票 77：URL 分享的那一段载荷。语料由引擎现打（东风战与半庄各一整场），**不碰页面**
  // ——地址栏与按钮是票 78 的。三件事一趟做完：真往返（编→解→`Paifu.decoder`→`Replay`，
  // 事件流逐条相同、终局点数与顺位相同）、逐位置改坏一个字符（每次要么读不动、要么逐字相同，
  // **绝不许解出另一份牌谱**）、以及载荷里没有 thinking / prompt 尾部 / 那把假 key。
  {
    name: "URL 分享的载荷：往返、逐位置腐蚀、审计三样一个都不上路",
    how: "node scripts/verify-share.mjs",
    run: (lane) => verifyShare(lane),
  },
  // 票 77 的反向自证：把「载荷里没有审计那三样」按红一次。理由与拌 key 那一趟逐字相同——
  // 「代码里有那三行断言」与「那三行断言真的拦得住东西」是两件事。
  {
    name: "反向自证：抹不干净的载荷必须让那道闸门变红",
    how: "node scripts/verify-share.mjs --poison（它单跑时**该**以 1 退出）",
    run: strippedProof,
  },
  // 票 72：配桌上那三项规则开关（对局长度 / 赤宝牌 / 食断，spec 的 story 13）真的传到了引擎。
  // 每一条断言读的都是**页面上点出来的那一桌导出的牌谱**：打完一整场东风战（场风全是东、
  // 且赤牌真的进了事件流，是下面那条「一张都没有」的阳性对照）→ 拨三项而**不重开**（牌谱里
  // 一个字段都不许变：不许半场换规则）→ 重开后打完一整场半庄（南场真的打到了、
  // 赤宝牌一张不剩、食断跟着关）→ 重新打开这一页，三项还在（localStorage）。
  {
    name: "配桌那三项规则开关：拨得动、按重开才生效、牌谱里的 ruleset 跟着变",
    how: "node scripts/verify-setup.mjs",
    run: (lane) => verifySetup(lane),
  },
  // 票 73：**四 LLM 同桌**。两个本地假端点（一个答话、一个固定回 401），
  // 一份档案坐两席、两席人格各不同，第三席引用那份坏 key 的档案，座位 3 是 bot。
  // 一局打完之后逐条核导出的牌谱：`names` 三个 `provider/model`（**档案的名字与 key 都不在里面**）、
  // 两席的 preamble 正文不同而渲染版本相同（对照实验的自变量只许有一个）、
  // 兜底只涨在坏 key 那一席、删掉还被引用的档案时页面把这件事说出来。
  // 它还带第二程：老 `janpo.llm.*` 迁成一份档案 + 那一席的绑定，且**只迁一次**。
  {
    name: "四 LLM 同桌：一份档案坐两席（人格各不同）、断电演习只塌那一席、老配置迁得过来",
    how: "node scripts/verify-seats.mjs",
    run: (lane) => verifySeats(lane),
  },
  // 票 108（接票 92 §⑧ 第 2 条挂在报告里的那一条）：**模型席那条路上的过期问话**。
  // 竞态不靠时序碰运气：假端点先睡六秒，而人在这几秒里把那一席**拨给了自己**，
  // 于是那几手由真人打了出去（不经 `drain` 那条顺序），牌桌绕过在飞的那份问话往前走。
  // 五条：拨完那一下之后在飞的席当场清空、牌桌没停（定时器照续）、鬼回执回来时
  // 引擎没被塞进一条旧动作（没有 `table-fault`）、**账单上那几个 token 还在**、
  // 而**气泡一个都没长出来**——后两条合起来就是「花了钱、没落子」这件真实情形。
  {
    name: "过期问话：拨完当场剪掉、牌桌没停、旧包没落子，而那一笔 token 还在账上",
    how: "node scripts/verify-stale-ask.mjs",
    run: (lane) => verifyStaleAsk(lane),
  },
  // 票 76：思考气泡。**两个本机假端点**（一个好好答话、一个只回越界 id）真跑几手，
  // 于是三态里的两态都在真语料上走得到：气泡里的字必须一字不差是端点回的那句
  // （端点 → 决策记录 → 气泡这条链路的唯一证据）、bot 席上一个气泡都没有、
  // 气泡与四家的三排牌**矩形不相交**（「不许挡住牌与河」读的是真坐标，不是承诺）、
  // 点开之后九样都在且牌桌摆出那一手的快照、收起来逐字回到现在（只读），
  // 最后换成那个交不出来的端点：气泡变「兜底」态，原因与 `data-fallback` 同源。
  {
    name: "思考气泡：字来自那一手的记录、bot 席没有、挡不住牌、点得开、兜底那一态",
    how: "node scripts/verify-bubbles.mjs",
    run: (lane) => verifyBubbles(lane),
  },
  // 票 78：牌谱从外面进来的两条路。`?table=1` 真打几手 → 点「复制分享链接」→
  // 读**真剪贴板** → 打开那条地址：事件流与导出件逐条相同、末帧点数与主持人那一桌一致、
  // 自动播、没有配桌面板、那句「为什么没有气泡」指得回导入入口；把导出的那份 JSON
  // 从首页导回去：末帧逐项一致，带决策记录的那份**气泡有话**（与分享链接的关键差别）；
  // 坏链接与坏输入三连（不是 JSON / 缺字段 / 中间某局断掉）各有中文原因，页面活着。
  {
    name: "牌谱从外面进来的两条路：分享链接真往返、导入 JSON（气泡有话）、坏输入三连",
    how: "node scripts/verify-inbound.mjs",
    run: (lane) => verifyInbound(lane),
  },
  // 票 87：**桌边坐了个人**。真人坐座位 0、座位 1 交给一个本地假端点、座位 2/3 是 bot，
  // 页面内驱动把一整场东风战打完（有牌点得动就点，没有就按单步）。
  // 中途把**整页 HTML** 抓下来：里面每一个 `data-pai` 都得落在「自家手牌 + 四家的河 +
  // 四家的副露 + 宝牌指示牌」那份预算里——他家的手牌**一张都不许有，连 `data-*` 都不许有**。
  // 另外三件：上帝视角与别席视角的按钮不在 DOM 里（不是灰掉）、对局中一个气泡都没有
  // （而账单 > 0，证明真有东西该藏）、`?dev=1` 的曳光弹不给开（阴性对照：没真人时照旧开得了）。
  {
    name: "真人坐下把一局打完：视角按钮不在 DOM 里、整页 HTML 不泄他家手牌、?dev=1 不给开",
    how: "node scripts/verify-human.mjs",
    run: (lane) => verifyHuman(lane),
  },
  // 票 94：**ToolSearch 档整条链路**。两个本机假端点各演一种模型——一个先查两次再出牌、
  // 一个「能查就查」；本机第三席是 bare 档的对照，座位 3 是 bot。一局走 24 手之后读导出的牌谱：
  // 查询与答案真的落在那一手的尾部里（**这一档的可观测性就是它**）、到上限就停而这一手照常打完
  // （0 兜底）、账单是 (查询次数 + 1) 倍、bare 那一席一行查询都没有、preamble 里那一段只有
  // ToolSearch 席才有。它是这一票唯一一条**真过 pi-ai 适配器**的 `what_if` tool call。
  {
    name: "ToolSearch 档：真的一条 what_if tool call 走完来回、到上限就停、账单是几倍",
    how: "node scripts/verify-toolsearch.mjs",
    run: (lane) => verifyToolSearch(lane),
  },
  // 票 89：**真人的信息辅助与思考时限**。四程：裸奔档那一程是**阴性对照**
  // （整页 HTML 里一个 `data-scaffold-*` 都没有、整页文字里没有「向听 / 有效牌 / 进退向 /
  // 危险度」、危险度那一枚开关根本不在 DOM 里，阳性对照是同一页拨到信息辅助）；
  // 信息辅助那一程逐手核「辅助那几行 = 他点得动的那几张」，并与牌桌上那块危险度对拍
  // （两处渲染读的是同一份脚手架）；时限那一程等足够久证明默认不限时什么都不会发生，
  // 再拨成两秒不动手，到点自动摸切且牌局接着走；最后一程把票 94 那一档在**面板上**
  // 拨起来，用它把一局打完（导出的牌谱里那一席真去查了、0 兜底）。
  {
    name: "真人的信息辅助与思考时限：裸奔档整页没有一个算好的数、到点自动摸切、面板上选得到三档",
    how: "node scripts/verify-assist.mjs",
    run: (lane) => verifyAssist(lane),
  },
  // 票 90：**复盘的逐手对照标注**。第一程真人坐一席打完一整场：对局中复盘那一块
  // **整个不在 DOM 里**（阴性对照——对局中给出「换打会怎样」就是作弊，那是票 89 的事），
  // 终局之后**每一手都有一条**，而那几个数（向听 / 有效牌 / 危险度）与**引擎另一条路**
  // 算出来的逐字相同（`ReviewCheck`：`Replay` + `GameState.step`，页面那侧走的是 `Table.replay`）。
  // 「更好的候选」那一栏由闸门照**规则**自己推一遍（帕累托占优）。第二程在首页那份 Demo 上：
  // 上帝视角没有主语、坐到某一席就有（**模型席也能看**）、点某一手游标真的跳过去、
  // 按「回到原处」回得来（票 86 的回程），而强 AI 那一行今天一个占位都没有（票 93）。
  {
    name: "复盘：对局中一条都没有，终局后每一手都有，那几个数与引擎逐字相同",
    how: "node scripts/verify-review.mjs",
    run: (lane) => verifyReview(lane),
  },
];

const lane = await openLane();
const results = [];

console.log(`浏览器闸门共 ${gates.length} 趟（同一个浏览器进程、同一台服务器）。`);

try {
  for (const gate of gates) {
    console.log("");
    console.log(`== ${gate.name} ==`);
    console.log(`（单跑：${gate.how}）`);
    const started = Date.now();
    const failures = await gate.run(lane);
    results.push({ gate, failures, ms: Date.now() - started });
  }
} finally {
  await lane.close();
}

console.log("");
console.log(`${results.length} 趟浏览器闸门（同一个浏览器进程、同一台服务器）：`);
for (const { gate, failures, ms } of results) {
  console.log(`  ${mark(failures)} ${(ms / 1000).toFixed(1)}s　${gate.how}`);
}

const reds = results.filter((each) => each.failures.length > 0);
if (reds.length > 0) {
  console.error("");
  for (const { gate, failures } of reds) {
    console.error(`【${gate.name}】红了。单独重跑它：${gate.how}`);
    printFailures(failures);
  }
  process.exit(1);
}
