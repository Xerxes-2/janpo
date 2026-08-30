// 首页（票 71）：**访客打开 `/` 什么都不用配，第一眼就是一桌牌在走**
//（spec 的 story 1，ADR-0003 由 Demo Paifu 兑现）。
//
// 八条断言，各守一件这一票才成立的事：
//
//   ① 牌桌**在动**：隔一会儿采两次，手数必须不同（自动播是这一页的全部卖点）；
//   ② 页面上**没有配桌控件**：访客第一眼不该是一张表单（票 35 的「默认视图只该有牌桌」同一条标准）；
//   ③ 有一条**去 `?table=1`** 的路：Host 那一侧访客得摸得到，而且点过去真的是那一页；
//   ④ 页脚照旧（票 37）：回仓库的外链与许可；
//   ⑤ **默认上帝视角**（裁决 71-8，票 75 执行）：四家的手牌都摊着；切到座位视角之后
//      至少三家扣起来——**后半句是阳性对照**，没有它这条断言在「投影恒亮」时也会绿；
//   ⑥ **时间轴真的拖得动**（票 75，spec 的 story 17：这一桌牌拖得回去）：
//      在滑块上真点一下（不是设 value），牌桌跳到那一处；
//      拖回 0 是开局那一瞬；「下一步 → 上一步」走一个来回之后 DOM 逐字相同（幂等）；
//      点某一局的局号就落在那一局的开局帧；
//   ⑦ **这份牌谱带着推理**（票 76 写下、票 79 翻面、票 81 按视角分开数）：首页那一场现在是
//      真的四席 LLM 对局，因此**上帝视角下**末帧上四席各有一个气泡且里面真有话，
//      而那句「为什么没有思考气泡」**不许再在**——「边打边讲理由」就是这一页的卖点，
//      气泡没出来等于换资产白换。**而坐到座位 N 上只剩那一席**（票 81：视角是一道信息闸门，
//      与手牌同一条规则），切回上帝四家又都回来；四句话互不相同是**阳性对照**
//      （否则「四个同一句的空壳」也能让它绿）。
//      （那句指路话本身没死：`verify-inbound` 里分享链接与导入无记录牌谱那两程仍旧要求它在，
//      两头合起来才是完整的阳性对照）；
//   ⑧ **未结算不摆里宝牌指示牌**（票 76 顺手那条，裁决「71-8 的余波」）：里宝牌只在有人
//      立直和了的那一刻翻开、才算番，开局就摆在桌心会让人以为这一局有两个宝牌在生效
//      ——**问题不是剧透而是误导**。拖到末帧（结算那一屏）它必须回来：**这一条是阳性对照**，
//      没有它的话，「里宝牌整个不画了」同样能让上一句变绿。
//
// **它与 `verify-tracer` 不重**：那一道量的是「首页里没有开发向内容」（藏没藏住），
// 这一道量的是「首页本身像不像个门面」。两道都开 `/`，其余全开 `?table=1`。
//
// **资产是 `fetch` 拉的**（`web/public/demo-paifu.json`，不打进 bundle）：因此这一道顺带
// 是那条取用路径的唯一无头证据——404 了页面会说一句「Demo 牌谱拉不到」，而 ① 会当场红。
//
// 跑法：`cd web && pnpm run build && pnpm run verify:home`
// 它也是 `verify-browser.mjs` 里的一道（跑道上那几趟共用一个浏览器与一台服务器）。

import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { openBubble, openSetup, stepTurns } from "./table-drive.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** 首页那份 Demo 牌谱里那一列 `names`（名牌上写的就是它，票 82）。 */
function demoNames() {
  const paifu = JSON.parse(readFileSync(resolve(webRoot, "public/demo-paifu.json"), "utf8"));
  return paifu.events?.[0]?.names ?? [];
}

/**
 * 只属于主持人那一页的控件。首页上出现任何一个都算「第一眼是张表单」。
 *
 * **这份名单自带阳性对照**（判据 3）：点过去 `?table=1` 之后逐个查它们**必须都在**。
 * 少了这一步的话，写错一个 testId（或者某个控件被改名删掉）会让对应那条断言
 * 变成永远为真——一道从不失败的闸门等于没有闸门。
 */
const HOST_TEST_IDS = [
  "table-llm-panel",
  "table-seed",
  "table-step",
  "table-next",
  "table-export",
  // 复制分享链接（票 78）：只属于主持人那一页——首页本身就是回放，没有可分享的新对局。
  "table-share",
  // 四席绑定与模型档案库（票 73）：key 只出现在档案编辑处，座位那几行不重复填。
  "table-seat-0-random",
  "table-seat-0-opinionated",
  "table-seat-0-tier",
  "table-seat-3-persona",
  "table-profile-0",
  "table-profile-new",
  "table-profile-provider",
  "table-profile-key",
  // 配桌那三项规则开关（票 72）：回放的规则集是牌谱自带的那一份，首页上拨不得。
  "table-rules",
  "table-length-tonpuusen",
  "table-akadora-on",
  "table-kuitan-on",
  // 「配这一桌」那一整块与它里面新分出来的两块（票 83）。**`table-ops` 不在这份名单里**：
  // 操作控件两屏共用一套装配，首页上它**应当**在（而且就贴在牌桌上沿）。
  "table-setup",
  "table-seating",
  "table-profiles",
  "table-seat-0-detail",
  "table-panel-note",
  // 配桌收成默认收起之后新长出来的那两枚（票 116）：
  // 摘要行本体与它上面那串值。首页没有配桌，这两枚也就不该在。
  "table-setup-summary",
  "table-setup-digest",
];

/** 隔多久采第二次手数。1 秒够 2× 播三四手（`TableState.demoSpeed`），也不至于让 CI 变慢。 */
const SAMPLE_GAP_MS = 1000;

/**
 * 操作条与牌桌之间允许的空隔（票 83）。实测 **16 px**（两者只隔着 `.ops .controls`
 * 那条 0.4rem 的外边距）；40 px 留的是行高与边距微调的余地，**再塞进一行字就超**
 * （一行正文在这一页上是 27 px）。它守的就是票 83 要的那一件事：
 * **按一下、看结果不必把视线甩回页面顶部**。
 */
const OPS_TO_BOARD_MAX_PX = 40;

/**
 * **一屏**（票 83 把这条交给票 82）：在这个视口里打开 `?table=1`，
 * 「操作块顶边 → 牌桌底边」必须一次看得完，不用滚。
 *
 * 票 83 落地时这个数是 **912 px**（要滚约 112 px；它当时按四家横排量的是约 150 px），
 * 票 82 把左右两家竖起来、桌心改成横排之后是 **约 700 px**。
 * **视口写死在断言里**：一屏这件事离了「多大的屏」就没有意义。
 */
const ONE_SCREEN = { width: 1280, height: 800 };

/**
 * 量一屏之前先走几手（票 82）。**开局那一刻量不出这件事**：河是空的，四家的面板都矮
 * （旧几何在开局那一刻也只跨 745 px，这一条那时是绿的）。牌桌真正变高是河长起来之后——
 * 32 手 ≈ 每家 8 巡：**旧几何在这里跨 912 px（红），新几何 723 px**。
 * 再往后每家的河每多一行，上下两家各高一截（36–48 手 785 px；河到 13 张之后仍旧要滚，
 * 报告 82 §4 有整张表）——这一条钉的是**中盘那一屏**，不是终盘。
 */
const ONE_SCREEN_TURNS = 32;

/**
 * **落地那一眼就看得见牌桌**（票 116）。在 `ONE_SCREEN` 里打开 `?table=1`、
 * 一手都还没走、一下都还没滚时，牌桌顶边必须落在这个数以内。
 *
 * 票 116 之前是 **810 px**——视口 800，于是**一像素牌桌都看不见**，
 * 而那 810 里 **528 px（65%）是配桌那一整块**。收成默认收起之后是 **307 px**。
 * 420 px 取的是「第一屏的上半」：再往下就等于要人先滚一段才看得见牌。
 *
 * **它与下面那条「一屏」不是一回事**，两条各答一个问题：
 * 那一条量的是「**走 32 手之后**，操作块顶边 → 牌桌底边」跨不跨得过一屏
 * （答的是「按一下与看结果在同一屏」），从 `ops.top` 起算，
 * **对人落地那一眼一个字也没说**——票 116 之前它绿着，而牌桌一像素也看不见。
 */
const BOARD_TOP_MAX_PX = 420;

/** 固定舞台（票 119）：要在这几个视口上都装得下，且**结构不因为缩放而改变**。
    1366×768 是 16:9 那一档（最矮），390×844 是手机竖屏（缩到能看不能读，主人已裁）。 */
const STAGE_VIEWPORTS = [
  { width: 1280, height: 800 },
  { width: 1440, height: 900 },
  { width: 1366, height: 768 },
  { width: 390, height: 844 },
];

/** 缩放对不对，看**根字号**：样式表里 204 个尺寸写的是 `rem`，全跟着它走。
    两个视口之间牌宽的比，必须与根字号的比对得上——**对不上就说明有尺寸没走 `rem`**，
    那正是「结构因为缩放而改变」的前兆。0.02 是取整与发丝线留的余量。 */
const SCALE_TOLERANCE = 0.02;

/**
 * 主持人那一页抬头那段说明的高度上限（票 82）。一行正文在这一页上是 27 px，
 * 40 px 是「一行 + 边距微调」，**写到两行就超**。
 * 票 82 之前它是四行 **82 px**——那一段的读者是主持人自己，他每开一次这一页都要从它头上跳过去。
 * **首页那两段不在这条断言里**：那一段是写给头一回来的访客的。
 */
const HOST_INTRO_MAX_PX = 40;

/** 在滑块的这个位置上点一下（0 = 最左 = 第 0 帧，1 = 最右 = 末帧）。 */
const DRAG_TO = 0.75;

/** 时间轴此刻的三个数（人读的是那句中文，机器读的是这三个）。 */
async function timelineAt(page) {
  return await page.evaluate(() => {
    const slider = document.querySelector('[data-testid="table-timeline"]');
    const at = document.querySelector('[data-testid="table-timeline-at"]');
    return {
      cursor: Number.parseInt(slider?.getAttribute("data-cursor") ?? "-1", 10),
      last: Number.parseInt(slider?.getAttribute("data-last") ?? "-1", 10),
      turns: Number.parseInt(at?.getAttribute("data-turns") ?? "-1", 10),
      kyoku: Number.parseInt(at?.getAttribute("data-kyoku") ?? "-1", 10),
    };
  });
}

/**
 * 牌桌此刻画出来的那一份摘要（四家的张数、点数与牌面，加上那两句话）。
 * **幂等那条断言比的就是它**：同一个游标来回到达两次，这份摘要必须逐字相同。
 */
async function boardDigest(page) {
  return await page.evaluate(() =>
    [0, 1, 2, 3]
      .map((index) => {
        const hand = document.querySelector(`[data-testid="seat-${index}-hand"]`);
        const kawa = document.querySelector(`[data-testid="seat-${index}-kawa"]`);
        const score = document.querySelector(`[data-testid="seat-${index}-score"]`);
        return [
          hand?.getAttribute("data-hand-count"),
          hand?.getAttribute("data-hand-hidden"),
          hand?.textContent,
          kawa?.getAttribute("data-kawa-count"),
          kawa?.textContent,
          score?.getAttribute("data-score"),
        ].join("|");
      })
      .concat([
        document.querySelector('[data-testid="table-latest"]')?.textContent ?? "",
        document.querySelector('[data-testid="table-timeline-at"]')?.textContent ?? "",
      ])
      .join("\n"),
  );
}

/**
 * 「操作这一桌」那一块与牌桌的位置关系（票 83）：两个矩形、中间的空隔，
 * 以及操作块里有没有人偷偷加了吸底（`position: fixed/sticky`）。
 * **读的是真坐标与计算样式**：「贴着牌桌」是个几何事实，DOM 里摆得相邻不算。
 */
async function opsShape(page) {
  return await page.evaluate(() => {
    const at = (testId) => {
      const node = document.querySelector(`[data-testid="${testId}"]`);
      if (node === null) return null;
      const rect = node.getBoundingClientRect();
      return {
        top: Math.round(rect.top + window.scrollY),
        bottom: Math.round(rect.bottom + window.scrollY),
        // 左右也要（票 118）：三列之后操作条与牌桌是**并排**的，
        // 把它们分开的那个轴变成了横轴。
        left: Math.round(rect.left + window.scrollX),
        right: Math.round(rect.right + window.scrollX),
      };
    };
    const ops = document.querySelector('[data-testid="table-ops"]');
    const stuck =
      ops === null
        ? []
        : [ops, ...ops.querySelectorAll("*")]
            .map((node) => getComputedStyle(node).position)
            .filter((position) => position === "fixed" || position === "sticky");
    return {
      ops: at("table-ops"),
      board: at("table-board"),
      setup: at("table-setup"),
      stuck: stuck.length,
    };
  });
}

/**
 * **这一页说了多少话**（票 120）。
 *
 * 主人量的那一屏：486 字，其中 345 字是解释性散文——**七成**。而在此之前
 * 我造的每一条闸门量的都是几何（牌越不越界、气泡压不压牌、缩放改不改结构、
 * 一屏装不装得下），**没有一条问「这一页说了多少话」**。
 *
 * ∴ 文字只会单调增加：每加一个功能就配一句解释，每句解释都有它当时的道理，
 * 而没有任何东西在反方向拉。这一条就是那个反方向的力。
 *
 * 量法：落地那一屏**看得见的**文字（`<details>` 收着的不算——那正是我们要它去的地方；
 * 牌面上的字不算，那是牌不是话）。上限按屏分开定：主持人那一页要摆四席绑定，
 * 天然比首页多。
 *
 * **报出量到了几处**：0 处就是这条闸门自己坏了（选择器扇空集），不是页面干净。
 */
async function wordProblems(page, where, limit) {
  const seen = await page.evaluate(() => {
    let total = 0;
    let foot = 0;
    let blocks = 0;
    const walk = (el) => {
      for (const node of el.childNodes) {
        if (node.nodeType === 3) {
          const text = node.textContent.trim();
          if (text.length < 4) continue;
          const host = node.parentElement;
          if (host === null) continue;
          // 牌面上的字是牌，不是话（`.tile` 拿 `color: transparent` 藏着它）。
          if (host.closest(".tile") !== null) continue;
          // 页脚**另算**：那 118 字是法律要求的（MIT 外链 + Apache-2.0 §4(b) 的归属），
          // 不是废话，混进正文预算只会逼人去砍不该砍的。但它也不许野蛮生长，
          // ∴ 它有自己那一条上限（下面 `footer` 那一格）。
          if (host.closest('[data-testid="site-footer"]') !== null) {
            foot += text.length;
            continue;
          }
          // 收在抽屉里的不算：那正是我们要它去的地方。
          const folded = host.closest("details");
          if (folded !== null && !folded.open) continue;
          if (host.getBoundingClientRect().height < 2) continue;
          total += text.length;
          blocks += 1;
        } else if (node.nodeType === 1) walk(node);
      }
    };
    walk(document.body);
    return { total, foot, blocks };
  });
  if (seen.blocks === 0) return [`${where} 一处文字都没量到：这条字数闸门自己坏了（不是页面干净）`];
  const said = [];
  if (seen.total > limit)
    said.push(
      `${where} 落地那一屏摆了 ${seen.total} 字（上限 ${limit}）：` +
        "又开始说废话了——能收进 title / 抽屉的就别常驻（票 120）",
    );
  if (seen.foot > 200)
    said.push(`${where} 页脚摆了 ${seen.foot} 字（上限 200）：法律要求的那几句也不许野蛮生长`);
  if (said.length === 0)
    console.log(
      `${where} 说了 ${seen.total} 字 ✓（上限 ${limit}，页脚另计 ${seen.foot}，量到 ${seen.blocks} 处）`,
    );
  return said;
}

/**
 * **操作控件贴着牌桌**（票 83 的第一条）。四条断言，两屏各量一遍：
 *
 *   ① 那一块真的在（两屏共用同一套装配，不是只给主持人那一页做了一份）；
 *   ② 它**挨着**牌桌（票 118 之后是并排在左，从前是摞在上面）；
 *   ③ 两者之间不允许再塞东西（空隔 ≤ `OPS_TO_BOARD_MAX_PX`）——票 83 之前这一段是
 *      **1136 px** 的配桌表单，按一下单步要把视线甩回去；
 *   ④ **不做视口吸底**（调度器裁的）：吸底会盖住牌桌下沿，而那正是自家手牌那一排。
 *
 * 主持人那一页多一条：**配桌那一块在操作块之上**（分界线的方向）。
 */
function opsProblems(shape, where, wantsSetup) {
  const said = [];
  if (shape.ops === null) {
    said.push(`${where} 上没有「操作这一桌」那一块（[data-testid="table-ops"]）`);
    return said;
  }
  if (shape.board === null) return said; // 牌桌没摆出来是别的断言的事

  // **量的是「隔了多远」，不是「谁在谁上面」**（票 118）。
  //
  // 票 83 立这一条时操作条摞在牌桌上面，∴ 当时写成 `board.top - ops.bottom`。
  // 票 118 把它挪到牌桌左边并排——那一句 `gap < 0` 于是当场报「落到了牌桌下面」，
  // 而实际上**它挨得比从前更近**。旧写法把「摞在上面」这个当时的实现细节
  // 焊进了断言里，真正要守的是**视线要走多远**。
  //
  // ∴ 改成两个矩形的间距：竖着排就量竖的、并排就量横的，取那个真正把它们分开的轴。
  // 相交（并排时常有）算 0。
  const vertical = Math.max(shape.board.top - shape.ops.bottom, shape.ops.top - shape.board.bottom);
  const horizontal = Math.max(
    shape.board.left - shape.ops.right,
    shape.ops.left - shape.board.right,
  );
  // **取真正把它们分开的那段距离**。头一版写 `min(vertical, horizontal)`——
  // 并排时纵向是重叠的（负数），min 取到它，`max(0, 负)` 恒等于 0，
  // **这条断言当场变成空的**：把左轨和牌桌拉开 6rem 都不报。阴性对照逮到的。
  // 各轴先夹到非负（重叠算 0），再取斜边：竖排时等于竖的间距，并排时等于横的。
  const gap = Math.round(Math.hypot(Math.max(0, vertical), Math.max(0, horizontal)));
  if (gap > OPS_TO_BOARD_MAX_PX) {
    said.push(
      `${where} 的操作条与牌桌之间隔了 ${gap} px（上限 ${OPS_TO_BOARD_MAX_PX} px）：` +
        "中间又塞进了东西，按一下就看不见结果了（票 83）",
    );
  }
  if (shape.stuck > 0) {
    said.push(
      `${where} 的操作块里有 ${shape.stuck} 个吸底/吸顶的元素（position: fixed|sticky）：` +
        "吸底会盖住牌桌下沿，而那正是自家手牌那一排",
    );
  }
  if (wantsSetup) {
    if (shape.setup === null) {
      said.push(`${where} 上没有配桌那一块（[data-testid="table-setup"]）`);
    } else if (shape.setup.bottom > shape.ops.top) {
      said.push(
        `${where} 的配桌表单没收到操作条上面去（配桌 ${shape.setup.top}→${shape.setup.bottom}、` +
          `操作条从 ${shape.ops.top} 开始）：分界线反了`,
      );
    }
  }
  return said;
}

/**
 * **一屏那一条**（票 82 兑现票 83 交下来的那 150 px）：在 1280×800 里打开 `?table=1`，
 * 「按一下」的那一排与整张牌桌要一次看得完。
 *
 * 顺带量抬头那一段：主持人那一页的说明压到一行。
 * 两条都读**真坐标**，因此几何一退化当场就红——票 83 立那条 40 px 时用的是同一个办法。
 */
/**
 * 固定舞台（票 119）：**长卷死掉**这件事得有东西守着。
 *
 * 三条，都不是同义反复：
 *   ① 任何视口上都**没有文档滚动**（横纵都算）——这是整票的目的。
 *   ② 内容高不超过视口高——`body { overflow: hidden }` 会把溢出**裁掉**而不是让人滚，
 *      光看「有没有滚动条」会漏掉「看不见」这种更坏的情形。
 *   ③ **牌宽与根字号同比例缩放**。①② 只说「装得下」，装得下的办法可以是重排
 *      （媒体查询把三列变一列也装得下）——而主人要的是**结构不变**。
 *      ③ 才是那句话的执行者。
 */
async function stageProblems(lane, url) {
  const said = [];
  const seen = [];
  for (const viewport of STAGE_VIEWPORTS) {
    const page = await lane.newPage({ viewport });
    try {
      await page.goto(`${url}/?table=1`, { waitUntil: "load" });
      await openSetup(page);
      await page.getByTestId("table-setup-summary").click();
      await stepTurns(page, { limit: ONE_SCREEN_TURNS }).catch(() => {});
      const shot = await page.evaluate(() => {
        const doc = document.documentElement;
        const tile = document.querySelector(".zone .tile");
        const board = document.querySelector('[data-testid="table-seats"]');
        // **只量宽度由 CSS 的 `rem` 定死的东西**。`.ops`、`.page` 那种盒子的宽
        // 由内容撑着（实测 `.page` 在 1280×800 上是 885 px，而它的 `max-width: 52rem`
        // 只有 676 px——min-content 顶开了上限），拿它们去问「缩放有没有改结构」
        // 会得到假红。头两版分别错在 `.ops` 与 `.page` 上。
        return {
          root: Number.parseFloat(getComputedStyle(doc).fontSize),
          tile: tile ? tile.getBoundingClientRect().width : 0,
          // **多量几处**：只量牌的话，「牌桌写死 px 而牌照旧缩放」这种坏法逃得掉
          //（阴性对照第一版就没打中——那说明这条断言比我声称的弱）。
          board: board ? board.getBoundingClientRect().width : 0,

          scrollY: doc.scrollHeight > window.innerHeight + 1,
          scrollX: doc.scrollWidth > window.innerWidth + 1,
          contentH: Math.ceil(document.body.getBoundingClientRect().height),
          // 横向也要量内容本身：`body { overflow: hidden }` 会把横向溢出一并藏起来，
          // 只看 `scrollWidth` 的话「被裁掉」这种更坏的情形查不出来。
          contentW: Math.ceil(
            Math.max(...[...document.body.children].map((el) => el.getBoundingClientRect().right)),
          ),
        };
      });
      const at = `${viewport.width}×${viewport.height}`;
      if (shot.scrollY) said.push(`固定舞台：${at} 里出现了纵向文档滚动——长卷没死`);
      if (shot.scrollX) said.push(`固定舞台：${at} 里出现了横向文档滚动`);
      if (shot.contentH > viewport.height)
        said.push(
          `固定舞台：${at} 里内容高 ${shot.contentH} px 超过视口 ${viewport.height} px——` +
            `body 上那条 overflow:hidden 会把超出的部分裁掉，人连滚都滚不到`,
        );
      if (shot.contentW > viewport.width)
        said.push(
          `固定舞台：${at} 里内容宽 ${shot.contentW} px 超过视口 ${viewport.width} px——` +
            `body 上那条 overflow:hidden 会把右边裁掉`,
        );
      if (shot.tile <= 0) said.push(`固定舞台：${at} 里一张牌都没量到（闸门空转了）`);
      seen.push({ at, ...shot });
    } finally {
      await page.close();
    }
  }

  // ③ 结构不变：任取两个视口，**每一处量到的尺寸**的比都要与根字号的比对得上。
  const MEASURED = [
    ["牌宽", "tile"],
    ["牌桌", "board"],
  ];
  for (let index = 1; index < seen.length; index += 1) {
    const a = seen[0];
    const b = seen[index];
    const wanted = b.root / a.root;
    for (const [said_, key] of MEASURED) {
      if (a[key] <= 0 || b[key] <= 0) {
        said.push(`固定舞台：${said_}在 ${a.at} 或 ${b.at} 上量不到（闸门空转了）`);
        continue;
      }
      const got = b[key] / a[key];
      if (Math.abs(got - wanted) > SCALE_TOLERANCE)
        said.push(
          `固定舞台：从 ${a.at} 到 ${b.at}，根字号缩了 ${wanted.toFixed(3)} 倍而${said_}缩了 ` +
            `${got.toFixed(3)} 倍——这一处没跟着根字号走，缩放改了结构`,
        );
    }
  }

  if (seen.length !== STAGE_VIEWPORTS.length)
    said.push(`固定舞台：只量到 ${seen.length} 个视口，该有 ${STAGE_VIEWPORTS.length} 个`);
  return { said, seen };
}

async function oneScreenProblems(lane, url) {
  const page = await lane.newPage({ viewport: ONE_SCREEN });
  const said = [];

  try {
    await page.goto(`${url}/?table=1`, { waitUntil: "load" });
    await page.getByTestId("table-board").waitFor({ timeout: 15000 });

    // 落地那一眼（票 116）：**走手之前、滚动之前**量，不能拿 `window.scrollY` 加回去——
    // 要的就是视口坐标里那一眼。
    const landing = await page.evaluate(() => {
      const node = document.querySelector('[data-testid="table-board"]');
      return node === null
        ? null
        : {
            top: Math.round(node.getBoundingClientRect().top),
            scrolled: Math.round(window.scrollY),
          };
    });
    if (landing === null) {
      said.push('?table=1 上没有牌桌（[data-testid="table-board"]）：落地那一眼无从量起');
    } else {
      if (landing.scrolled !== 0)
        said.push(`?table=1 一打开就已经滚了 ${landing.scrolled} px：落地那一眼这一条量不准了`);
      if (landing.top > BOARD_TOP_MAX_PX)
        said.push(
          `?table=1 打开那一刻牌桌顶边在 ${landing.top} px（上限 ${BOARD_TOP_MAX_PX} px），` +
            `视口高 ${ONE_SCREEN.height} px：` +
            (landing.top >= ONE_SCREEN.height
              ? "一像素牌桌都看不见"
              : `只露出 ${ONE_SCREEN.height - landing.top} px`) +
            "——要人先滚一段才看得见牌（票 116）",
        );
      console.log(
        `  落地那一眼：${ONE_SCREEN.width}×${ONE_SCREEN.height} 里牌桌顶边 ${landing.top} px` +
          `（上限 ${BOARD_TOP_MAX_PX}），露出 ${Math.max(0, ONE_SCREEN.height - landing.top)} px`,
      );
    }

    const { walked } = await stepTurns(page, { limit: ONE_SCREEN_TURNS });
    if (walked < ONE_SCREEN_TURNS)
      said.push(
        `量一屏之前只走得动 ${walked} 手（要 ${ONE_SCREEN_TURNS} 手）：` +
          "河还没长起来，这一条量的是开局那一刻——那时它恒为真（票 82）",
      );

    const shot = await page.evaluate(() => {
      const at = (testId) => {
        const node = document.querySelector(`[data-testid="${testId}"]`);
        if (node === null) return null;
        const rect = node.getBoundingClientRect();
        return {
          top: Math.round(rect.top + window.scrollY),
          bottom: Math.round(rect.bottom + window.scrollY),
          height: Math.round(rect.height),
        };
      };
      return { ops: at("table-ops"), board: at("table-board"), intro: at("table-intro") };
    });

    if (shot.ops === null || shot.board === null) {
      said.push("?table=1 上没有操作块或牌桌：一屏那一条无从量起");
      return { said, shot };
    }

    const span = shot.board.bottom - shot.ops.top;
    if (span > ONE_SCREEN.height)
      said.push(
        `?table=1 走 ${ONE_SCREEN_TURNS} 手之后，在 ${ONE_SCREEN.width}×${ONE_SCREEN.height} 里「操作块 + 整张牌桌」跨了 ${span} px：` +
          `还要滚 ${span - ONE_SCREEN.height} px 才看得全（票 82/83 的一屏目标）`,
      );

    if (shot.intro === null) {
      said.push('?table=1 上没有抬头那一段（[data-testid="table-intro"]）');
    } else if (shot.intro.height > HOST_INTRO_MAX_PX) {
      said.push(
        `?table=1 的抬头占了 ${shot.intro.height} px（上限 ${HOST_INTRO_MAX_PX} px = 一行）：` +
          "这一段的读者是主持人自己，不必每次读四行（票 82）",
      );
    }

    return { said, shot };
  } finally {
    await page.close();
  }
}

/**
 * **名牌上看得出这一席是谁在打**（票 82 的意见⑤）——这一道核的是**回放那一半**：
 * 牌谱里那一列 `names`（`provider/model`）。
 *
 * 两头对上：页面上那四枚名牌 ↔ **资产文件里**那条 `start_game` 的 `names`。
 * 拿资产当期望值而不是拿页面自己的另一处，是因为回放的名字只有一个真源；
 * **档案名不许出现**（那是本机的私人叫法，牌谱里根本没有，编一个出来会被人当真）。
 */
async function nameplateProblems(page, names) {
  const said = [];
  const plates = await page.evaluate(() =>
    [0, 1, 2, 3].map(
      (seat) =>
        document.querySelector(`[data-testid="seat-${seat}-player"]`)?.textContent?.trim() ?? null,
    ),
  );

  for (const seat of [0, 1, 2, 3]) {
    if (plates[seat] !== names[seat])
      said.push(
        `座位 ${seat} 的名牌上写着「${plates[seat]}」，牌谱里那一列写的是「${names[seat]}」：` +
          "回放的名牌只有牌谱这一个真源（票 82）",
      );
  }

  // 阳性对照：那一列真是 `provider/model` 的形状（四个空串也能让上面那一圈全绿）。
  if (!names.every((name) => /^[^/\s]+\/[^/\s]+$/.test(name)))
    said.push(
      `这份牌谱里的 names 是「${names.join("，")}」，不是 provider/model 的形状：` +
        "上面那一圈因此什么都没证明",
    );

  return { said, plates };
}

/** 四家的手牌扣起来几份（`data-hand-hidden`，投影的形状，不是渲染纪律）。 */
async function hiddenHands(page) {
  return await page.evaluate(
    () =>
      [0, 1, 2, 3]
        .map(
          (index) =>
            document
              .querySelector(`[data-testid="seat-${index}-hand"]`)
              ?.getAttribute("data-hand-hidden") ?? "?",
        )
        .filter((each) => each !== "false").length,
  );
}

/**
 * 等页面安静下来。**超时不扔异常而是返回 false**：这一道闸门的契约是交一份失败清单
 * （合并跑的那个入口要先关浏览器、再逐道汇报），在 try 里抛会把同一条跑道上其余那几趟一起搞挂。
 */
async function settles(page, predicate, argument) {
  try {
    await page.waitForFunction(predicate, argument, { timeout: 5000 });
    return true;
  } catch (_error) {
    return false;
  }
}

/** 等牌桌真的停下来（拖动一定暂停，因此这是拖动落定的信号）。 */
async function paused(page) {
  return await settles(
    page,
    () => document.querySelector('[data-testid="table-play"]')?.textContent?.trim() === "播放",
  );
}

/** 等游标真的落到 `frame`（React 重渲染是异步的）。 */
async function settledAt(page, frame) {
  return await settles(
    page,
    (expected) =>
      document.querySelector('[data-testid="table-timeline"]')?.getAttribute("data-cursor") ===
      String(expected),
    frame,
  );
}

/** 这一屏此刻走到第几手（牌桌上那句「上一手：……」旁边没有数字，就数四家的河）。 */
async function progress(page) {
  return await page.evaluate(() =>
    [0, 1, 2, 3]
      .map((index) => {
        const label = document.querySelector(`[data-testid="seat-${index}-kawa"]`);
        return Number.parseInt(label?.getAttribute("data-kawa-count") ?? "0", 10);
      })
      .reduce((sum, each) => sum + each, 0),
  );
}

/** 首页那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyHome(lane) {
  const url = await lane.previewUrl();
  const page = await lane.newPage();
  const problems = [];
  const missing = [];
  const leaks = [];
  let dragCount = 0;
  let hostOps = null;
  let oneScreen = null;
  let stage = null;

  try {
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") problems.push(`[console.error] ${message.text()}`);
    });

    await page.goto(`${url}/`, { waitUntil: "load" });

    // 牌桌得先真的摆出来：那份 Demo 牌谱是 `fetch` 回来的，拉不到时这里就等不到。
    // 等不到的话下面几条断言会各说各的话，而真正的原因是页面上那句「Demo 牌谱拉不到：……」。
    try {
      await page.getByTestId("table-board").waitFor({ timeout: 15000 });
    } catch (_error) {
      const said = await page.evaluate(
        () =>
          document.querySelector('[data-testid="table-error"]')?.textContent ??
          "（页面上什么也没说）",
      );
      return failure("首页没摆出牌桌（那份 Demo 牌谱多半没拉到）：", [said]);
    }

    // ① 在动：隔一会儿采两次。**手数不同**才算数——静止的牌桌与「自动播坏了」看上去一样。
    const before = await progress(page);
    await page.waitForTimeout(SAMPLE_GAP_MS);
    const after = await progress(page);

    if (after <= before) {
      missing.push(
        `牌桌没在动：${SAMPLE_GAP_MS} ms 前后四家的河合计都是 ${before} 张（自动播没跑起来）`,
      );
    }

    // ② 没有配桌控件。
    for (const testId of HOST_TEST_IDS) {
      const count = await page.getByTestId(testId).count();
      if (count !== 0) leaks.push(`首页上还挂着 [data-testid="${testId}"]（${count} 个）`);
    }

    // ⑨ 操作控件贴着牌桌（票 83）。首页这一遍；主持人那一页在 ③ 点过去之后再量一遍
    // ——**两屏同一条规则**，只量一屏的话另一屏随时可以退化成票 83 之前那副样子。
    const homeOps = await opsShape(page);
    missing.push(...opsProblems(homeOps, "首页", false));

    // ⑩ 名牌上看得出这一席是谁在打（票 82）：回放那一半，写的是牌谱里的 `provider/model`。
    const nameplates = await nameplateProblems(page, demoNames());
    missing.push(...nameplates.said);

    // ⑤ 默认上帝视角（裁决 71-8）：四家的手牌都摊着。
    // **后面那一下是阳性对照**：切到坐位 0 之后他家必须扣回去——
    // 没有它的话，「投影恒亮」这种坏法同样能让上一句变绿（票 32 那次就是这么滑过去的）。
    const hiddenAtGod = await hiddenHands(page);
    if (hiddenAtGod !== 0) {
      missing.push(`首页不是上帝视角：四家里有 ${hiddenAtGod} 家的手牌扣着（裁决 71-8）`);
    }

    await page.getByTestId("table-view-0").click();
    const hiddenAtSeat = await hiddenHands(page);
    if (hiddenAtSeat < 3) {
      missing.push(
        `切到座位 0 视角后只有 ${hiddenAtSeat} 家扣着（该是 3 家）——上一条断言因此不算数`,
      );
    }
    await page.getByTestId("table-view-god").click();

    // ⑥ 时间轴（票 75）。**在滑块上真点**：设 `value` 不能证明人拖得动它。
    // 一拖就暂停（`TableState.moveCursor`），因此下面几条量的都是静止的牌桌。
    const slider = page.getByTestId("table-timeline");
    if ((await slider.count()) === 0) {
      missing.push('首页上没有时间轴（[data-testid="table-timeline"]）：回放拖不动（票 75）');
    } else {
      const box = await slider.boundingBox();
      await slider.click({ position: { x: box.width * DRAG_TO, y: box.height / 2 } });
      // 一拖就暂停，因此「播放键上写着『播放』」就是这一次拖动落定了的信号；
      // 不等它的话下面读到的可能还是自动播那一帧（React 重渲染是异步的）。
      if (!(await paused(page))) missing.push("在时间轴上点了一下，牌桌却没停下来（拖动该暂停）");
      const dragged = await timelineAt(page);
      const kawaAtDrag = await progress(page);

      // 点在四分之三处：拇指宽度会让它偏几个百分点，因此只要求落在那一带里。
      if (!(dragged.cursor > dragged.last * 0.5 && dragged.cursor < dragged.last * 0.95)) {
        missing.push(
          `在时间轴 ${Math.round(DRAG_TO * 100)}% 处点一下，游标却停在 ${dragged.cursor}/${dragged.last}`,
        );
      }

      // 幂等：「下一步 → 上一步」走一个来回，DOM 摘要必须逐字相同。
      const digestBefore = await boardDigest(page);
      await page.getByTestId("table-forward").click();
      const stepped = await settledAt(page, dragged.cursor + 1);
      await page.getByTestId("table-back").click();
      const stepBack = await settledAt(page, dragged.cursor);
      if (!stepped || !stepBack) {
        missing.push(
          `「下一步 / 上一步」没把游标挪到第 ${dragged.cursor + 1} / ${dragged.cursor} 帧（逐事件步进坏了）`,
        );
      }
      const digestAfter = await boardDigest(page);
      if (digestBefore !== digestAfter) {
        missing.push(
          `同一个游标（第 ${dragged.cursor} 帧）来回到达两次，牌桌却不一样了：\n${digestBefore}\n——\n${digestAfter}`,
        );
      }

      // 拖回 0：开局那一瞬（四家的河都是空的，且「还没走一手」）。
      await slider.click({ position: { x: 0, y: box.height / 2 } });
      if (!(await settledAt(page, 0))) missing.push("拖到滑块最左端，游标却没回到第 0 帧");
      const kawaAtHead = await progress(page);
      const latest = (await page.getByTestId("table-latest").textContent()).trim();
      if (kawaAtHead !== 0 || !latest.includes("还没走一手")) {
        missing.push(
          `拖回第 0 帧却不是开局那一瞬：四家的河共 ${kawaAtHead} 张，上一手那句写着「${latest}」`,
        );
      }
      if (kawaAtDrag <= kawaAtHead) {
        missing.push(
          `拖到后面与拖回开头看到的是同一屏（河各 ${kawaAtDrag} / ${kawaAtHead} 张）：牌桌没跟着游标走`,
        );
      }

      // 局边界：点第二局的局号，落在那一局的**开局帧**上。
      const second = page.getByTestId("table-kyoku-1");
      if ((await second.count()) === 0) {
        missing.push('时间轴上没有局边界可跳（[data-testid="table-kyoku-1"]）（票 75）');
      } else {
        await second.click();
        // 刚刚才拖回第 0 帧，因此「游标不再是 0」就是跳局落定的信号。
        await settles(
          page,
          () =>
            document
              .querySelector('[data-testid="table-timeline"]')
              ?.getAttribute("data-cursor") !== "0",
        );
        const jumped = await timelineAt(page);
        const said = (await page.getByTestId("table-latest").textContent()).trim();
        if (jumped.kyoku !== 1 || jumped.turns <= 0 || !said.includes("还没走一手")) {
          missing.push(
            `跳到第二局落在了第 ${jumped.kyoku} 局、第 ${jumped.turns} 手，上一手那句写着「${said}」`,
          );
        }
      }

      // ⑧ 里宝牌（票 76）：刚跳到第二局的**开局帧**，这一局还没结算——桌心不许摆里宝牌。
      const uraAtOpening = await page.getByTestId("table-uradora").count();
      if (uraAtOpening !== 0) {
        missing.push(
          "一局刚开就把「里宝牌指示牌」摆在桌心了：它只在有人立直和了的那一刻才翻开、才算番",
        );
      }

      // 拖到末帧（结算那一屏）：**阳性对照**——那一刻它必须在，否则上一条断言什么都没证明。
      await slider.click({ position: { x: box.width - 1, y: box.height / 2 } });
      await settledAt(page, dragged.last);
      const settled = await page.getByTestId("table-settlement").count();
      const uraAtSettlement = await page.getByTestId("table-uradora").count();
      if (settled === 0) {
        missing.push("拖到末帧却没有结算面板：那一屏不是这一场打完的样子（票 71）");
      }
      if (uraAtSettlement === 0) {
        missing.push(
          "结算那一屏上没有「里宝牌指示牌」：上一条「未结算不摆」因此什么都没证明（它可能是整个不画了）",
        );
      }

      dragCount = 7;
    }

    // ⑦ 这份牌谱带着推理（票 79 把票 76 那一条翻了面）：四席各有一个气泡、里面真有话，
    // 而那句「为什么没有气泡」不许再在。**这一段量的是末帧**（上面刚拖到结算那一屏）：
    // 四家都开过口了，因此「四个」是死数而不是「至少一个」——少一席就是有一席的记录没落下来。
    //
    // **数之前先说清这是哪个视角**（票 81）：气泡从此按视角掩蔽，「四个」是上帝视角下的死数。
    // 首页默认就是上帝视角（裁决 71-8，⑤ 刚核过），这里再按一下是为了不依赖 ⑥ 拖拽那一段的残留状态。
    await page.getByTestId("table-view-god").click();
    const bubbles = await page.locator('[data-testid$="-bubble"]').count();
    const said = await page.getByTestId("table-no-bubbles").count();
    if (bubbles !== 4) {
      missing.push(
        `首页那场四席都是模型，上帝视角下的末帧却只有 ${bubbles} 个思考气泡（该有 4 个）：` +
          "「边打边讲理由」是这一页的卖点，气泡没出来就是换资产白换了（票 79）",
      );
    }
    if (said !== 0) {
      missing.push(
        '首页那份牌谱带着决策记录，页面上却还挂着「这一局没有思考气泡」（[data-testid="table-no-bubbles"]）',
      );
    }

    // 气泡里真有话吗：空气泡与「没有气泡」在截图上分不开，而卡成「在想」的气泡在回放里压根儿不该有。
    const saidBySeat = [];
    for (const seat of [0, 1, 2, 3]) {
      const bubble = page.getByTestId(`seat-${seat}-bubble`);
      if ((await bubble.count()) === 0) continue;
      const state = await bubble.getAttribute("data-bubble");
      const text = ((await bubble.textContent()) ?? "").replace("说", "").trim();
      saidBySeat.push(text);
      if (state !== "spoke" && state !== "troubled") {
        missing.push(`座位 ${seat} 的气泡停在「${state}」态：回放里没有谁还在等回话`);
      }
      if (text.length < 8) {
        missing.push(`座位 ${seat} 的气泡里只有「${text}」：没话的气泡与没有气泡一个样`);
      }
    }

    // **阳性对照**：四句话互不相同。没有这一条的话，「四个写着同一句话的空壳」
    // 与「四家各说各的」在上面那几条断言下长得一模一样（票 81）。
    if (new Set(saidBySeat).size !== saidBySeat.length) {
      missing.push(
        `上帝视角下四家的气泡里有重复的话（${saidBySeat.length} 个气泡只有 ${new Set(saidBySeat).size} 句不同的话）：` +
          "四家该各说各的",
      );
    }

    // ⑦b 视角是一道信息闸门（票 81）：坐到座位 N 上，**DOM 上只剩那一席的气泡**
    // （不是拿 CSS 藏起来：这里数的是元素个数）；切回上帝，四家必须都回来
    // ——后半句是阳性对照：没有它，「气泡整个不画了」同样能让前半句变绿。
    // **回放里终局也不放开**（现在就停在末帧）：escape hatch 是上帝视角那一按，不是时间。
    for (const seat of [0, 1, 2, 3]) {
      await page.getByTestId(`table-view-${seat}`).click();
      const only = await page.locator('[data-testid$="-bubble"]').count();
      const mine = await page.getByTestId(`seat-${seat}-bubble`).count();
      if (only !== 1 || mine !== 1) {
        missing.push(
          `坐到座位 ${seat} 上，末帧上还有 ${only} 个思考气泡（自家的 ${mine} 个）：` +
            "该只剩自家那一个——视角与手牌同一条规则，回放里终局也不放开（票 81）",
        );
      }
    }

    await page.getByTestId("table-view-god").click();
    const backAtGod = await page.locator('[data-testid$="-bubble"]').count();
    if (backAtGod !== 4) {
      missing.push(
        `切回上帝视角只有 ${backAtGod} 个气泡（该有 4 个）：上一条「坐座只剩一家」因此什么都没证明`,
      );
    }

    // ⑩ 点开跳走了，关掉要跳回来（票 86，主人试玩时报的那一条）。
    // 回放里点气泡会把游标搬到那一手（票 76：轴只有一根，不另开第二根），
    // 而票 76 只做了去程——于是「读一条理由」变成了「时间轴被搬走」。
    // **两条路各走一遍**：按「收起」与按「播放」（后者今天同样会把面板收起来）。
    // dotnet 那一侧钉的是 `RecordOpened` 的状态迁移，**这一条钉的是真点下去那一下**。
    const originAt = await timelineAt(page);
    // 现在停在末帧（⑧ 拖过去的），因此挑**手序最小那一席**的气泡：
    // 它落定那一帧肯定不是末帧，下面「游标真的挪了」那一句才验得到东西。
    let oldest = { seat: 0, turn: Number.POSITIVE_INFINITY };
    for (const seat of [0, 1, 2, 3]) {
      const turn = Number.parseInt(
        (await page.getByTestId(`seat-${seat}-bubble`).getAttribute("data-bubble-turn")) ?? "-1",
        10,
      );
      if (turn >= 0 && turn < oldest.turn) oldest = { seat, turn };
    }

    for (const how of ["bubble-close", "table-play"]) {
      await openBubble(page, oldest.seat);
      await page.getByTestId(`seat-${oldest.seat}-bubble`).click();
      const opened = await settles(
        page,
        () => document.querySelector('[data-testid="table-bubble-detail"]') !== null,
      );
      const jumped = await timelineAt(page);
      if (!opened) {
        missing.push(`点了座位 ${oldest.seat} 的气泡却没摊开全文面板`);
        break;
      }
      // 去程：游标真的被搬走了（没这一句的话，下面「回得去」那一条可以靠「压根儿没跳过」混过去）。
      if (jumped.cursor >= originAt.cursor) {
        missing.push(
          `点开第 ${oldest.turn} 手之后游标停在 ${jumped.cursor}（点开之前是 ${originAt.cursor}）：它该往回跳才对`,
        );
      }
      // 面板上要人读得到自己被搬到了哪一手（从前只有 `data-bubble-turn` 给机器看）。
      // **不用 `locator.textContent()`**：那一句没画出来时它抛 `TimeoutError`，
      // 而这一道闸门的契约是交一份失败清单（报错会把合并跑的其余那几趟一起搞挂）。
      const viewing = await page.evaluate(
        () =>
          document.querySelector('[data-testid="bubble-viewing"]')?.textContent?.trim() ??
          "（面板上根本没有那一句）",
      );
      if (!viewing.startsWith(`正在看第 ${oldest.turn} 手`) || !viewing.includes("时间轴")) {
        missing.push(
          `回放里摊开的面板没说清它把人搬到了哪一手（该是第 ${oldest.turn} 手）：「${viewing}」`,
        );
      }

      // 回程：按「收起」或按「播放」，两条路都得先回到原处。
      //
      // **不能拿「等到游标等于原处」当判据**（判据 3：一条永远等得到的断言等于没断言）：
      // 没回程时按「播放」也会一帧一帧往前走，几百毫秒后同样路过原处——第一版就是这么维的。
      // 改成：**面板一消失就当场读游标**（两件事在同一次 update 里，因此读到的就是落定值）。
      // 原处正是末帧，因此按「播放」回去之后 `replayTick` 当场停下（`cursor >= last`），
      // 游标恒等于原处，不必与定时器赛跑。
      await page.getByTestId(how).click();
      const closed = await settles(
        page,
        () => document.querySelector('[data-testid="table-bubble-detail"]') === null,
      );
      const now = await timelineAt(page);
      if (!closed) missing.push(`按了 ${how} 之后全文面板还摊着`);
      if (now.cursor !== originAt.cursor) {
        missing.push(
          `按 ${how} 关掉面板之后游标停在第 ${now.cursor} 帧，没回到点开之前那一处（第 ${originAt.cursor} 帧）：` +
            "看完一条理由，时间轴不许被搬走（票 86）",
        );
      }
    }

    // ③ 去 `?table=1` 的路：**真点过去**，落地那一页必须是主持人那一页。
    const link = page.getByTestId("home-host-link");
    if ((await link.count()) === 0) {
      missing.push("首页上没有一条去 `?table=1` 的路（访客摸不到 Host 那一侧）");
    } else {
      await link.click();
      await openSetup(page);
      await page.getByTestId("table-llm-panel").waitFor({ timeout: 15000 });

      // **阳性对照**：上面那份名单里的每一个都得在主持人这一页上真的存在，
      // 否则「首页上没有它」这条断言是空转的（判据 3）。
      for (const testId of HOST_TEST_IDS) {
        if ((await page.getByTestId(testId).count()) === 0) {
          missing.push(
            `?table=1 上没有 [data-testid="${testId}"]：那么「首页上没有它」那一条永远为真（空转）`,
          );
        }
      }

      const landed = new URL(page.url());
      if (landed.searchParams.get("table") !== "1") {
        missing.push(`那条路点过去落在 ${page.url()}，不是 ?table=1`);
      }

      // ⑨ 的后一半（票 83）：主持人那一页同样要「配桌在上、操作贴着牌桌」。
      // 它才是票 83 真正要治的那一页（改之前这一段是 1136 px 的配桌表单）。
      hostOps = await opsShape(page);
      missing.push(...opsProblems(hostOps, "?table=1", true));
      // 主持人那一页**默认暂停**：那几道要点、要读牌桌的闸门全靠这一条。
      const playing = (await page.getByTestId("table-play").textContent()).trim();
      if (playing !== "播放") {
        missing.push(`?table=1 落地那一刻不是暂停着的（播放键上写着「${playing}」）`);
      }
    }

    // ⑪ 一屏（票 82 兑现票 83 那 150 px）：另开一页量，因为它要一个写死的视口。
    oneScreen = await oneScreenProblems(lane, url);
    missing.push(...oneScreen.said);

    // ⑬ 这一页说了多少话（票 120）——落地那一屏，抽屉里的不算。
    {
      const at = await lane.newPage({ viewport: ONE_SCREEN });
      await at.goto(`${url}/`, { waitUntil: "load" });
      missing.push(...(await wordProblems(at, "首页", 120)));
      await at.goto(`${url}/?table=1`, { waitUntil: "load" });
      missing.push(...(await wordProblems(at, "?table=1", 185)));
      await at.close();
    }

    // ⑫ 固定舞台（票 119）：缩放不重排、长卷死掉。
    stage = await stageProblems(lane, url);
    missing.push(...stage.said);

    // ④ 页脚照旧（票 37）：地址本身不在这里复述（真源在 `src/Janpo.Web/Footer.fs` 一处）。
    await page.goto(`${url}/`, { waitUntil: "load" });
    const footerLinks = await page
      .getByTestId("site-footer")
      .locator('a[href^="https://"]')
      .count();
    if (footerLinks === 0) missing.push("首页的页脚里没有一条指回仓库的外链（票 37）");
    const text = await page.evaluate(() => document.body.innerText);
    if (!text.includes("MIT")) missing.push("首页的正文里没提许可（MIT）（票 37）");

    if (problems.length > 0) return failure("页面报了错：", problems);
    if (leaks.length > 0) return failure("首页上漏出了只属于主持人那一页的控件：", leaks);
    if (missing.length > 0) return failure("首页少了该给访客的东西：", missing);

    console.log(`牌桌在动 ✓（${SAMPLE_GAP_MS} ms 里四家的河从 ${before} 张长到 ${after} 张）`);
    console.log(
      `首页上没有配桌控件 ✓（查了 ${HOST_TEST_IDS.length} 个，且每一个在 ?table=1 上都真的存在）`,
    );
    console.log(
      `默认上帝视角 ✓（四家全摊着；切到座位 0 后 ${hiddenAtSeat} 家扣回去，阳性对照成立）`,
    );
    console.log(
      `时间轴拖得动 ✓（真点了 ${dragCount} 下：拖到 3/4 处、步进一个来回、拖回 0、跳第二局、拖到末帧）`,
    );
    console.log(
      "这份牌谱带着推理 ✓（上帝视角四席各一个气泡、里面真有话且四句互不相同、那句指路话不在了；" +
        "未结算不摆里宝牌、结算那一屏摆）",
    );
    console.log("视角是一道信息闸门 ✓（坐到四个座位上各只剩自家那一个气泡，切回上帝四家都回来）");
    console.log(
      "点开跳走了、关掉跳回来 ✓（按「收起」与按「播放」两条路各走一遍：" +
        `游标 ${originAt.cursor} → 第 ${oldest.turn} 手那一帧 → ${originAt.cursor}）`,
    );
    console.log("「自己开一桌」点过去就是 ?table=1，且那一页默认暂停 ✓");
    console.log(
      `操作控件贴着牌桌 ✓（首页隔 ${homeOps.board.top - homeOps.ops.bottom} px、` +
        `?table=1 隔 ${hostOps.board.top - hostOps.ops.bottom} px，上限 ${OPS_TO_BOARD_MAX_PX} px；` +
        "两屏都没有吸底元素；?table=1 的配桌表单在操作条之上）",
    );
    console.log(
      `名牌上看得出这一席是谁在打 ✓（四家：${nameplates.plates.join(" / ")}，与牌谱里那一列 names 逐字相同）`,
    );
    console.log(
      `一屏 ✓（${ONE_SCREEN.width}×${ONE_SCREEN.height} 里 ?table=1 走 ${ONE_SCREEN_TURNS} 手之后，「操作块 + 整张牌桌」跨 ` +
        `${oneScreen.shot.board.bottom - oneScreen.shot.ops.top} px；抬头一段 ${oneScreen.shot.intro.height} px）`,
    );
    console.log(
      `固定舞台 ✓（缩放不重排、无文档滚动）：${stage.seen
        .map((one) => `${one.at} 根字号 ${one.root.toFixed(2)}px 牌宽 ${one.tile.toFixed(1)}px`)
        .join("　")}`,
    );
    console.log("页脚里有回仓库的外链与许可（MIT）✓");
    return [];
  } finally {
    await page.close();
  }
}

if (isEntry(import.meta.url)) {
  await runStandalone((lane) => verifyHome(lane));
}
