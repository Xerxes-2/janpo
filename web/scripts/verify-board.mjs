// 「渲染给人」那一侧的闸门（票 44）。
//
// M1 留下的一处不对称：**渲染给模型**有黄金用例逐字段钉着（40 条用例、2069 个字段），
// **渲染给人**只有截图。两次信息缺失（票 32 的牌背隐形、票 38 的副露没来源）都是主人
// 肉眼发现的，CI 一次没抓到。这一道把牌桌上**人能看见的八项**各挂一条断言：
//
//   手牌（自家牌面 / 他家张数）、河（含手切摸切）、副露（含来源与被鸣那张）、点数、
//   供托与本场、宝牌指示牌、巡目、立直状态
//
// 外加**方位**：给定观测座位，四家必须画在正确的格子里（票 44 的布局那一半）。
//
// 判据的形状：`data-` 是同一件事**给机器看的那一半**，人看见的是中文标题、牌面与标记。
// 每条断言都把两头对上——只核 `data-` 的话，把牌一张不画、只留属性照样全绿；
// 只核文字的话，属性写错了没人知道。
//
// **语料是真对局**：种子 9、其余座位换成「有主见」那一档（票 42），一手一手走到
// **立直、副露、手切与摸切同时在场**为止再取快照。走不到就报错——一道在空局面上
// 全绿的闸门等于没有闸门（票 34 立的规矩）。
//
// 跑法：`cd web && pnpm run build && node scripts/verify-board.mjs`

import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright-core";
import { chromeExecutable, missingChrome } from "./chrome.mjs";
import { checkNakiGroups, readNakiGroups } from "./naki-marks.mjs";
import { pageUrl, startPreview } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** 这一局走哪颗种子。挑它的过程见报告 44：`有主见` 档下它第 35 手就把八项全摆出来了。 */
const SEED = 9;

/** 走到「八项都有东西可核」为止的手数上限。到不了就报错（防空转）。 */
const TURN_BUDGET = 90;

/** 取完快照再走几手，用来验「巡目真的在走」。 */
const JUNME_STEPS = 6;

/** 方位（`Board.position` 的输出），按「相对观测者第几家」排。 */
const POSITIONS = ["self", "shimocha", "toimen", "kamicha"];

/** 五个视角（四个座位 + 上帝视角）。 */
const VIEWPOINTS = ["0", "1", "2", "3", "god"];

/**
 * 票 51 的位置断言走哪几局：两颗种子把**九种槽位结果**摆齐（四家均匀随机）。
 * 挑它们的过程：`dotnet fsi` 直调引擎扫了 4000 颗种子（报告 51 §2）。
 * `wants` 同时是**防空转**的清单：走完预算还摆不出来就报错，
 * 而不是静静地少核几条（票 34 立的规矩）。
 */
const NAKI_CORPUS = [
  // 吃恒在最左、碰落中与右、大明杠的「左起第二格」，外加一组暗杠（无位置编码）。
  { seed: 237, wants: ["吃第0格", "碰第1格", "碰第2格", "大明杠第1格", "暗杠"] },
  // 大明杠的两个端格、碰落最左，外加一组加杠（叠在横放那张上）。
  { seed: 720, wants: ["碰第0格", "大明杠第0格", "大明杠第3格", "加杠第0格"] },
];

/** 走到覆盖清单齐为止的手数上限。 */
const NAKI_BUDGET = 110;

/** 走一手：点「单步」，等「上一手」那行变了。这一局打完了就返回 false。 */
async function stepOnce(page) {
  if (await page.getByTestId("table-step").isDisabled()) return false;
  const before = (await page.getByTestId("table-latest").textContent()).trim();
  await page.getByTestId("table-step").click();
  if (await page.getByTestId("table-step").isDisabled()) return false;
  await page.waitForFunction(
    (previous) =>
      document.querySelector('[data-testid="table-latest"]').textContent.trim() !== previous,
    before,
    { timeout: 30000 },
  );
  return true;
}

/**
 * 把牌桌上人能看见的东西**连同它的 `data-`** 一起读回来。
 *
 * 计算样式也读两处：牌背的边框色与摸切那张的边框样式。**它们是「画出来了没有」的唯一证据**
 * ——票 32 那次缺陷正是「DOM 里有、眼睛里没有」（`color: transparent` 把边框一起抹了）。
 */
function readBoard(page) {
  return page.evaluate(() => {
    const text = (node) => (node === null ? null : node.textContent.trim());
    const attr = (node, name) => (node === null ? null : node.getAttribute(name));

    const seats = [...document.querySelectorAll('[data-testid="table-seats"] > [data-seat]')].map(
      (node) => {
        const seat = Number(node.getAttribute("data-seat"));
        const pick = (suffix) => node.querySelector(`[data-testid="seat-${seat}-${suffix}"]`);
        const hand = pick("hand");
        const kawa = pick("kawa");
        const naki = pick("naki");
        const back = hand.querySelector(".tile.back");
        const tsumogiri = kawa.querySelector('[data-tsumogiri="true"]');
        const tegiri = kawa.querySelector('[data-tsumogiri="false"]');

        return {
          seat,
          position: node.getAttribute("data-seat-position"),
          riichi: node.getAttribute("data-riichi"),
          marks: [...node.querySelectorAll(".seat-head .mark")].map((mark) => mark.textContent),
          score: { text: text(pick("score")), data: attr(pick("score"), "data-score") },
          junme: { text: text(pick("junme")), data: attr(pick("junme"), "data-junme") },
          hand: {
            data: hand.getAttribute("data-hand-count"),
            hidden: hand.getAttribute("data-hand-hidden"),
            label: text(hand.querySelector(".row-label")),
            faces: hand.querySelectorAll("[data-pai]").length,
            backs: hand.querySelectorAll(".tile.back").length,
            // 牌背画得出来吗（票 32）：边框色与斜纹底缺一样就是一块看不见的牌。
            backInk: back === null ? null : getComputedStyle(back).borderTopColor,
            backPattern: back === null ? null : getComputedStyle(back).backgroundImage,
            // 刚摸那张的额外间距（票 22 定的记号）：摸切打的就是它，摆回手牌里就看不出来了。
            drawnGap:
              hand.querySelector(".tile.drawn") === null
                ? null
                : Number.parseFloat(getComputedStyle(hand.querySelector(".tile.drawn")).marginLeft),
          },
          kawa: {
            data: kawa.getAttribute("data-kawa-count"),
            label: text(kawa.querySelector(".row-label")),
            tiles: kawa.querySelectorAll("[data-pai]").length,
            tsumogiri: kawa.querySelectorAll('[data-tsumogiri="true"]').length,
            tegiri: kawa.querySelectorAll('[data-tsumogiri="false"]').length,
            marked: kawa.querySelectorAll("[data-tsumogiri]").length,
            // 摸切与手切画得不一样吗：虚线 vs 实线（两者都是公开信息）。
            tsumogiriStroke: tsumogiri === null ? null : getComputedStyle(tsumogiri).borderTopStyle,
            tegiriStroke: tegiri === null ? null : getComputedStyle(tegiri).borderTopStyle,
          },
          naki: {
            data: naki.getAttribute("data-naki-count"),
            groups: naki.querySelectorAll("[data-naki]").length,
          },
        };
      },
    );

    const field = (testId) => document.querySelector(`[data-testid="${testId}"]`);
    const dora = field("table-dora");
    // 赤牌的红字（票 22 定的记号）：它与普通牌的颜色必须不同，否则赤 5 就是普通 5。
    const aka = document.querySelector(".tile.aka");
    const plain = document.querySelector(".tile:not(.aka):not(.back)");

    return {
      seats,
      center: {
        anchor: attr(field("table-center"), "data-anchor"),
        frame: text(field("table-anchor")),
        kyokuText: text(field("table-kyoku")),
        bakaze: attr(field("table-kyoku"), "data-bakaze"),
        kyoku: attr(field("table-kyoku"), "data-kyoku"),
        honba: attr(field("table-kyoku"), "data-honba"),
        kyotakuText: text(field("table-kyotaku")),
        kyotaku: attr(field("table-kyotaku"), "data-kyotaku"),
        bou: field("table-bou") === null ? null : field("table-bou").children.length,
        dora: {
          data: attr(dora, "data-tile-count"),
          tiles: dora === null ? 0 : dora.querySelectorAll("[data-pai]").length,
          text: text(dora),
        },
      },
      aka: {
        text: text(aka),
        color: aka === null ? null : getComputedStyle(aka).color,
        plain: plain === null ? null : getComputedStyle(plain).color,
      },
      bot: attr(field("table-agent"), "data-bot"),
      agent: text(field("table-agent")),
    };
  });
}

/**
 * 这个颜色等于没画吗。**两种写法都认**：浏览器把 `color-mix()` 算出来的是
 * `color(srgb 0 0 0 / 0)`，而普通颜色是 `rgba(0, 0, 0, 0)`——只认后一种的话，
 * 票 32 那个病根（牌背吃 `currentcolor`，而它是透明的）会从这道闸门下面滑过去。
 */
function invisible(color) {
  return color === null || color === "transparent" || /(,\s*0\)|\/\s*0\))$/.test(color.trim());
}

/** 四家画在哪个格子里（`data-`）以及**真的画在哪儿**（元素中心的坐标）。 */
function readLayout(page) {
  return page.evaluate(() => {
    const center = (node) => {
      const rect = node.getBoundingClientRect();
      return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 };
    };
    return {
      anchor: document.querySelector('[data-testid="table-center"]').getAttribute("data-anchor"),
      seats: [...document.querySelectorAll('[data-testid="table-seats"] > [data-seat]')].map(
        (node) => ({
          seat: Number(node.getAttribute("data-seat")),
          position: node.getAttribute("data-seat-position"),
          ...center(node),
        }),
      ),
    };
  });
}

/** 这一帧把八项里的哪几项真摆出来了（防空转用的覆盖清单）。 */
function coverage(shot) {
  return {
    riichi: shot.seats.some((seat) => seat.riichi === "accepted"),
    naki: shot.seats.some((seat) => seat.naki.groups > 0),
    tsumogiri: shot.seats.some((seat) => seat.kawa.tsumogiri > 0),
    tegiri: shot.seats.some((seat) => seat.kawa.tegiri > 0),
    junme: shot.seats.every((seat) => Number(seat.junme.data) >= 1),
    dora: Number(shot.center.dora.data) >= 1,
    // 票 38 那七种记号里剩下的两种（赤牌的红字、刚摸那张的间距）：不摆出来就没处可验。
    aka: shot.aka.text !== null,
    drawn: shot.seats.some((seat) => seat.hand.drawnGap !== null),
  };
}

// ---- 八项断言 ----

/** ① 手牌：自家的牌面亮着、他家只有张数，且**牌背真的画得出来**（票 32）。 */
function checkHands(shot, problems) {
  for (const seat of shot.seats) {
    const where = `座位 ${seat.seat} 的手牌`;
    const count = Number(seat.hand.data);

    if (seat.hand.data === null || Number.isNaN(count))
      problems.push(`${where}没写张数（data-hand-count）`);
    if (seat.hand.label !== `手牌 ${count}`)
      problems.push(`${where}标题写着「${seat.hand.label}」，data-hand-count 却是 ${count}`);

    if (seat.hand.hidden === "true") {
      if (seat.hand.backs !== count)
        problems.push(`${where}扣着 ${count} 张，可牌桌上只画出 ${seat.hand.backs} 张牌背`);
      if (seat.hand.faces !== 0)
        problems.push(`${where}是他家的，却露出了 ${seat.hand.faces} 张牌面`);
      // 「DOM 里有、眼睛里没有」——票 32 那次缺陷的形状。一张牌背也没画时不报这一条：
      // 那是上面那条的事（一件事只报一遍，红的时候才看得出到底丢了什么）。
      if (seat.hand.backs > 0) {
        if (invisible(seat.hand.backInk))
          problems.push(`${where}的牌背是透明的（边框色 ${seat.hand.backInk}）：画了等于没画`);
        if (seat.hand.backPattern === null || seat.hand.backPattern === "none")
          problems.push(`${where}的牌背没有斜纹底：与明牌一眼分不开`);
      }
    } else {
      if (seat.hand.faces !== count)
        problems.push(`${where}写着 ${count} 张，可牌桌上画出 ${seat.hand.faces} 张牌面`);
      if (seat.hand.backs !== 0)
        problems.push(`${where}是亮着的，却还有 ${seat.hand.backs} 张扣着`);
    }
  }

  // 刚摸那张摆得离手牌远一点（票 22）。它是七种记号里占「间距」那一维的那一个。
  for (const seat of shot.seats) {
    if (seat.hand.drawnGap !== null && !(seat.hand.drawnGap > 0))
      problems.push(
        `座位 ${seat.seat} 刚摸的那张没摆开（margin-left ${seat.hand.drawnGap}px）：它就混回手牌里了`,
      );
  }

  const revealed = shot.seats.filter((seat) => seat.hand.hidden === "false");
  if (revealed.length !== 1)
    problems.push(`坐着看时该有且只有一家亮着手牌，实际 ${revealed.length} 家`);
  else if (revealed[0].position !== "self")
    problems.push(
      `亮着手牌的是座位 ${revealed[0].seat}，可它画在「${revealed[0].position}」那个方位上`,
    );
}

/** ② 河：张数对得上，且**手切与摸切画得不一样**（两者都是公开信息）。 */
function checkKawa(shot, problems) {
  for (const seat of shot.seats) {
    const where = `座位 ${seat.seat} 的河`;
    const count = Number(seat.kawa.data);

    if (seat.kawa.data === null || Number.isNaN(count))
      problems.push(`${where}没写张数（data-kawa-count）`);
    if (seat.kawa.label !== `河 ${count}`)
      problems.push(`${where}标题写着「${seat.kawa.label}」，data-kawa-count 却是 ${count}`);
    if (seat.kawa.tiles !== count)
      problems.push(`${where}写着 ${count} 张，可牌桌上画出 ${seat.kawa.tiles} 张`);
    if (seat.kawa.marked !== count)
      problems.push(`${where}里有 ${count - seat.kawa.marked} 张没标手切 / 摸切（data-tsumogiri）`);
    if (seat.kawa.tsumogiri > 0 && seat.kawa.tsumogiriStroke !== "dashed")
      problems.push(`${where}里的摸切画成了 ${seat.kawa.tsumogiriStroke} 边框，与手切分不开`);
    if (seat.kawa.tegiri > 0 && seat.kawa.tegiriStroke !== "solid")
      problems.push(`${where}里的手切画成了 ${seat.kawa.tegiriStroke} 边框，与摸切分不开`);
  }
}

/** ④ 点数：文字与 `data-` 一致，且**四家点数与供托之和守恒**（开局那一刻是多少就一直是多少）。 */
function checkScores(shot, total, problems) {
  for (const seat of shot.seats) {
    if (seat.score.data === null) problems.push(`座位 ${seat.seat} 没写点数（data-score）`);
    if (seat.score.text !== seat.score.data)
      problems.push(
        `座位 ${seat.seat} 牌桌上写着 ${seat.score.text} 点，data-score 却是 ${seat.score.data}`,
      );
  }

  const now = tally(shot);
  if (now !== total)
    problems.push(`四家点数与供托之和是 ${now}，开局那一刻是 ${total}：点数凭空多了或少了`);
}

/** 四家点数 + 供托折成的点。**恒等于起手总点**——这是引擎侧也钉着的那条守恒。 */
function tally(shot) {
  const scores = shot.seats.reduce((sum, seat) => sum + Number(seat.score.data), 0);
  return scores + Number(shot.center.kyotaku) * 1000;
}

/** ⑤ 供托与本场：立直棒的根数就是供托数，且**供托 = 这一局成立了的立直家数**。 */
function checkCenter(shot, problems) {
  const kyotaku = Number(shot.center.kyotaku);

  if (shot.center.kyotaku === null || Number.isNaN(kyotaku))
    problems.push("场况里没写供托（data-kyotaku）");
  if (shot.center.kyotakuText !== `${kyotaku} 根`)
    problems.push(`供托写着「${shot.center.kyotakuText}」，data-kyotaku 却是 ${kyotaku}`);
  if (kyotaku === 0 && shot.center.bou !== null) problems.push("供托是 0，桌上却还画着立直棒");
  if (kyotaku > 0 && shot.center.bou !== kyotaku)
    problems.push(`供托 ${kyotaku} 根，桌上却画着 ${shot.center.bou} 根立直棒`);

  if (shot.center.honba === null) problems.push("场况里没写本场（data-honba）");
  const said = `${shot.center.bakaze}${shot.center.kyoku}局 ${shot.center.honba} 本场`;
  if (shot.center.kyokuText !== said)
    problems.push(`场况写着「${shot.center.kyokuText}」，data- 上却是「${said}」`);

  // 立直棒是**立直成立**那一刻出的（`RiichiState.Accepted`）。这一局从头走到现在没换过局，
  // 因此桌上那几根恰好就是成立了的立直家数——供托与立直状态两项因此互为对方的对照。
  const accepted = shot.seats.filter((seat) => seat.riichi === "accepted").length;
  if (shot.center.kyoku === "1" && shot.center.honba === "0" && kyotaku !== accepted)
    problems.push(`这一局有 ${accepted} 家立直成立，桌上的供托却是 ${kyotaku} 根`);
}

/** 赤牌的红字（票 22）：它与普通牌必须不是一个颜色。 */
function checkAka(shot, problems) {
  const { text, color, plain } = shot.aka;
  if (text !== null && color === plain)
    problems.push(`牌桌上的赤牌「${text}」与普通牌同色（${color}）：赤 5 看不出来是赤 5`);
}

/** ⑥ 宝牌指示牌：至少一张，且写着几张就画出几张。 */
function checkDora(shot, problems) {
  const count = Number(shot.center.dora.data);

  if (shot.center.dora.data === null || Number.isNaN(count))
    problems.push("场况里没写宝牌指示牌（data-tile-count）");
  if (count < 1) problems.push("一张宝牌指示牌都没画：开局就该翻开一张");
  if (shot.center.dora.tiles !== count)
    problems.push(`宝牌指示牌写着 ${count} 张，牌桌上却画出 ${shot.center.dora.tiles} 张`);
  if ((shot.center.dora.text ?? "") === "") problems.push("宝牌指示牌那一格是空的：牌面没写出来");
}

/** ⑦ 巡目：文字与 `data-` 一致；**再走几手它必须往前走**（写死一个数也能骗过静态断言）。 */
function checkJunme(before, after, stepped, problems) {
  // 快照之后这一局就打完了的话，下面那条「巡目得往前走」无处可验——那是空转，不是绿。
  if (stepped === 0)
    problems.push("快照之后一手也没再走（这一局当时就打完了）：「巡目在走」那条断言在空转");

  for (const seat of before.seats) {
    const junme = Number(seat.junme.data);
    if (seat.junme.data === null || Number.isNaN(junme))
      problems.push(`座位 ${seat.seat} 没写巡目（data-junme）`);
    if (seat.junme.text !== `第 ${junme} 巡`)
      problems.push(`座位 ${seat.seat} 写着「${seat.junme.text}」，data-junme 却是 ${junme}`);
  }

  const sum = (shot) => shot.seats.reduce((total, seat) => total + Number(seat.junme.data), 0);
  if (sum(after) <= sum(before))
    problems.push(`又走了 ${JUNME_STEPS} 手，四家的巡目之和仍是 ${sum(before)}：巡目没在走`);

  for (const seat of after.seats) {
    const was = Number(before.seats[seat.seat].junme.data);
    if (Number(seat.junme.data) < was)
      problems.push(`座位 ${seat.seat} 的巡目从 ${was} 退回了 ${seat.junme.data}`);
  }
}

/** ⑧ 立直状态：`data-riichi` 与头上那枚「立直」标签必须说同一件事。 */
function checkRiichi(shot, problems) {
  for (const seat of shot.seats) {
    const marked = seat.marks.some((mark) => mark.includes("立直"));

    if (!["none", "declared", "accepted"].includes(seat.riichi))
      problems.push(`座位 ${seat.seat} 的立直状态写着「${seat.riichi}」，不是那三档之一`);
    if (seat.riichi !== "none" && !marked)
      problems.push(`座位 ${seat.seat} 立直了（${seat.riichi}），牌桌上却没有那枚「立直」标签`);
    if (seat.riichi === "none" && marked)
      problems.push(`座位 ${seat.seat} 没立直，牌桌上却挂着「立直」标签`);
  }
}

// ---- 方位（票 44 的布局那一半） ----

/**
 * 给定观测座位，四家必须画在正确的方位槽里，**而且真的画在那个位置上**。
 *
 * 两头都核是有意的：`data-seat-position` 与样式表同源（CSS 按它选格子），
 * 因此属性错了坐标跟着错；但坐标还能抓住另一件事——格子本身摆错了
 * （`grid-template-areas` 写反、媒体查询把三列拍平）。
 */
async function checkLayout(page, problems) {
  const bottoms = [];

  for (const viewpoint of ["0", "1", "2", "3", "god"]) {
    await page
      .getByTestId(viewpoint === "god" ? "table-view-god" : `table-view-${viewpoint}`)
      .click();
    const layout = await readLayout(page);
    const anchor = Number(layout.anchor);
    const where = viewpoint === "god" ? "上帝视角" : `坐在座位 ${viewpoint}`;

    // 上帝视角没有观测者，方位的参照系退回起家（座位 0）——这句话页面上也写着。
    const wanted = viewpoint === "god" ? 0 : Number(viewpoint);
    if (anchor !== wanted)
      problems.push(`${where} 时方位的参照系是座位 ${anchor}，该是座位 ${wanted}`);

    const at = {};
    for (const seat of layout.seats) {
      const expected = POSITIONS[(seat.seat - anchor + 4) % 4];
      if (seat.position !== expected)
        problems.push(`${where} 时座位 ${seat.seat} 画在「${seat.position}」，该在「${expected}」`);
      at[seat.position] = seat;
    }

    if (Object.keys(at).length !== 4) {
      problems.push(`${where} 时四家只占了 ${Object.keys(at).length} 个方位`);
      continue;
    }

    const { self, shimocha, toimen, kamicha } = at;
    if (!(self.y > kamicha.y && self.y > shimocha.y && self.y > toimen.y))
      problems.push(`${where} 时自家没画在最下面（自家 y=${Math.round(self.y)}）`);
    if (!(toimen.y < kamicha.y && toimen.y < shimocha.y))
      problems.push(`${where} 时对家没画在最上面（对家 y=${Math.round(toimen.y)}）`);
    if (!(kamicha.x < toimen.x && toimen.x < shimocha.x))
      problems.push(
        `${where} 时上家不在左、下家不在右（上家 x=${Math.round(kamicha.x)}、下家 x=${Math.round(shimocha.x)}）`,
      );

    bottoms.push({ where, seat: self.seat, seated: viewpoint !== "god" });
  }

  // 「切了视角但布局没转」：四个座位视角下画在下方的必须是四个不同的人。
  const said = bottoms.map((each) => `${each.where} → 座位 ${each.seat}`).join("；");
  const seated = bottoms.filter((each) => each.seated).map((each) => each.seat);
  if (new Set(seated).size !== seated.length)
    problems.push(`换了座位，画在下方的却还是那几家（${said}）`);

  return said;
}

// ---- 副露的位置编码（票 51） ----

/** 这一组副露摆出了哪一种槽位结果（覆盖清单的单位）。 */
function outcome(group) {
  if (group.kind === "暗杠") return "暗杠";
  return `${group.kind}第${group.slots.findIndex((slot) => slot.taken)}格`;
}

/**
 * **横放那张落在第几格就是它来自谁**，而那个“第几格”按**副露方自己的左右**算。
 *
 * 两件事各自报警：
 * 1. 逐组拿**绝对座位**（`data-naki-from-seat`）算出该落哪一格，与画出来的对拍
 *    （`checkNakiGroups`）——参照系漂到观测者就红；
 * 2. **五个视角看到的位置必须逐字相同**——位置不是屏幕左右，换个人坐不该把它翻过来。
 *    票 44 把牌桌改成四家围坐、方位跟视角转之后，这一条才是真能坏掉的：
 *    一旦拿 `Board.position` 的 anchor（观测者）去算槽位，四个视角就各说各的。
 */
async function checkNakiPositions(page, problems) {
  // 语料是**均匀随机**那一档（上面八项那一段把它切成了「有主见」）。
  await page.getByTestId("table-bot-random").click();
  const said = [];

  for (const corpus of NAKI_CORPUS) {
    await page.getByTestId("table-seed").fill(String(corpus.seed));
    await page.getByTestId("table-restart").click();

    let walked = 0;
    let groups = await readNakiGroups(page);
    const covered = () => {
      const seen = groups.map(outcome);
      return corpus.wants.every((want) => seen.includes(want));
    };

    while (walked < NAKI_BUDGET && !covered()) {
      if (!(await stepOnce(page))) break;
      walked += 1;
      groups = await readNakiGroups(page);
    }

    // 防空转：这一局没把该摆的摆出来的话，下面那些断言核的是别的东西（或者什么也没核）。
    const seen = groups.map(outcome);
    const uncovered = corpus.wants.filter((want) => !seen.includes(want));
    if (uncovered.length > 0)
      problems.push(
        `种子 ${corpus.seed} 走了 ${walked} 手，这几种位置一次都没摆出来：${uncovered.join("、")}——位置断言在空转`,
      );

    // 逐视角：位置本身对不对（对拍绝对座位），以及换了视角它变没变。
    let reference = null;
    for (const viewpoint of VIEWPOINTS) {
      await page
        .getByTestId(viewpoint === "god" ? "table-view-god" : `table-view-${viewpoint}`)
        .click();
      const where = viewpoint === "god" ? "上帝视角" : `坐在座位 ${viewpoint}`;
      const shot = await readNakiGroups(page);
      const { missing } = checkNakiGroups(shot);
      problems.push(...missing.map((each) => `种子 ${corpus.seed}・${where}：${each}`));

      const shape = shot
        .map((group) => `座位${group.seat}的${outcome(group)}`)
        .sort()
        .join("、");

      if (reference === null) reference = { where, shape };
      else if (shape !== reference.shape)
        problems.push(
          `种子 ${corpus.seed}：换了视角副露的位置跟着变了——${reference.where}看到「${reference.shape}」，${where}看到「${shape}」。位置的参照系是副露方自己，不是看牌桌的那个人`,
        );
    }

    said.push(`种子 ${corpus.seed} 走 ${walked} 手：${[...new Set(seen)].sort().join("、")}`);
  }

  // 参照系那句话得写在牌桌上（M1 第六条）：位置编码是一套约定，不写出来读者只能猜。
  const legend = await page.getByTestId("table-naki-legend").textContent();
  for (const word of ["最左", "上家", "中间", "对家", "最右", "下家", "副露方"]) {
    if (!legend.includes(word))
      problems.push(
        `牌桌上那句副露位置的说明里没有「${word}」：位置编码没声明参照系（读的是「${legend}」）`,
      );
  }

  return said;
}

// ---- 跑 ----

const executablePath = chromeExecutable();
if (!executablePath) {
  console.error(missingChrome);
  process.exit(1);
}

const server = await startPreview(webRoot);
const browser = await chromium.launch({ executablePath, headless: true });
const problems = [];
const pageProblems = [];

// try 里只收集，`finally` 关浏览器，报告与退出码放在最后——写法照 `verify-export.mjs`：
// 在 try 里 `process.exit` 会把 `finally` 整个跳过，浏览器与 preview 都关不掉。
let walked = 0;
let shot = null;
let later = null;
let total = 0;
let naki = { seen: "无" };
let bottoms = "";
let positions = [];

try {
  const page = await browser.newPage({ viewport: { width: 1180, height: 1200 } });
  page.on("pageerror", (error) => pageProblems.push(`[pageerror] ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") pageProblems.push(`[console.error] ${message.text()}`);
  });

  await page.goto(`${pageUrl(server)}/`, { waitUntil: "load" });
  await page.getByTestId("table-board").waitFor();

  // 其余座位换成「有主见」那一档（票 42）：均匀随机几乎不立直（1996 场里 15 次），
  // 而立直、供托与立直棒这三项要立直才走得到。
  await page.getByTestId("table-bot-opinionated").click();
  await page.getByTestId("table-seed").fill(String(SEED));
  await page.getByTestId("table-restart").click();

  // 开局那一刻的总点：下面拿它验守恒（不写死起手总点——那是规则集的事，不是闸门的事）。
  const opening = await readBoard(page);
  total = tally(opening);
  shot = opening;
  while (walked < TURN_BUDGET) {
    if (!(await stepOnce(page))) break;
    walked += 1;
    shot = await readBoard(page);
    const seen = coverage(shot);
    if (Object.values(seen).every((each) => each)) break;
  }

  const seen = coverage(shot);
  const uncovered = Object.entries(seen)
    .filter(([, ok]) => !ok)
    .map(([name]) => name);
  if (uncovered.length > 0)
    problems.push(
      `种子 ${SEED} 走了 ${walked} 手，这几项一次都没摆出来：${uncovered.join("、")}——断言在空转`,
    );

  if (shot.bot !== "opinionated")
    problems.push(`其余座位该是「有主见」那一档，data-bot 却是「${shot.bot}」`);
  if (!shot.agent.includes("有主见"))
    problems.push(`状态行写着「${shot.agent}」，可其余座位坐的是「有主见」那一档`);

  checkHands(shot, problems);
  checkKawa(shot, problems);

  const groups = await readNakiGroups(page);
  naki = checkNakiGroups(groups);
  problems.push(...naki.missing);
  if (groups.length === 0)
    problems.push(`种子 ${SEED} 走了 ${walked} 手，一组副露都没有：副露那几条断言在空转`);

  checkScores(shot, total, problems);
  checkCenter(shot, problems);
  checkDora(shot, problems);
  checkRiichi(shot, problems);
  checkAka(shot, problems);

  let stepped = 0;
  for (let step = 0; step < JUNME_STEPS; step += 1) if (await stepOnce(page)) stepped += 1;
  later = await readBoard(page);
  checkJunme(shot, later, stepped, problems);
  checkScores(later, total, problems);

  bottoms = await checkLayout(page, problems);
  // 票 51：横放那张的**位置**就是来源。它另走两局均匀随机的牌（把九种槽位结果摆齐），
  // 因此一定要摆在最后：上面八项读的是种子 9「有主见」那一局的快照。
  positions = await checkNakiPositions(page, problems);
} finally {
  await browser.close();
  await server.close();
}

if (pageProblems.length > 0) {
  console.error("页面报了错：");
  console.error(pageProblems.join("\n"));
  process.exit(1);
}

if (problems.length > 0) {
  console.error("牌桌上少了该给人看的东西：");
  console.error(problems.join("\n"));
  process.exit(1);
}

const kawa = shot.seats.reduce(
  (sum, seat) => ({
    tsumogiri: sum.tsumogiri + seat.kawa.tsumogiri,
    tegiri: sum.tegiri + seat.kawa.tegiri,
  }),
  { tsumogiri: 0, tegiri: 0 },
);
const riichi = shot.seats.filter((seat) => seat.riichi !== "none").map((seat) => seat.seat);

console.log(`种子 ${SEED}（有主见档）走 ${walked} 手，再走 ${JUNME_STEPS} 手验巡目`);
console.log(
  `  手牌 ✓（自家 ${shot.seats.find((seat) => seat.hand.hidden === "false").hand.faces} 张牌面，` +
    `他家 ${shot.seats
      .filter((seat) => seat.hand.hidden === "true")
      .map((seat) => seat.hand.backs)
      .join("/")} 张牌背，牌背画得出来）`,
);
console.log(
  `  河 ✓（共 ${kawa.tsumogiri + kawa.tegiri} 张：摸切 ${kawa.tsumogiri} 虚线、手切 ${kawa.tegiri} 实线）`,
);
console.log(`  副露 ✓（${naki.seen}）`);
console.log(
  `  点数 ✓（四家 ${shot.seats.map((seat) => seat.score.data).join("/")}，与供托之和恒为 ${total}）`,
);
console.log(
  `  供托与本场 ✓（${shot.center.kyokuText}・供托 ${shot.center.kyotaku} 根 = 立直棒 ${shot.center.bou ?? 0} 根）`,
);
console.log(`  宝牌指示牌 ✓（${shot.center.dora.data} 张：${shot.center.dora.text}）`);
console.log(
  `  巡目 ✓（${shot.seats.map((seat) => seat.junme.data).join("/")} → ${later.seats.map((seat) => seat.junme.data).join("/")}）`,
);
console.log(`  立直状态 ✓（座位 ${riichi.join("、")} 立直，头上有标签）`);
console.log(
  `  （顺带）赤牌红字 ${shot.aka.text}=${shot.aka.color} ≠ 普通牌 ${shot.aka.plain}、` +
    `刚摸那张的间距 ${shot.seats.find((seat) => seat.hand.drawnGap !== null).hand.drawnGap}px ✓`,
);
console.log(`  方位 ✓（${bottoms}）`);
console.log("  副露的位置就是来源 ✓（五个视角逐个对拍绝对座位，且五个视角看到的位置逐字相同）");
for (const line of positions) console.log(`    ${line}`);
