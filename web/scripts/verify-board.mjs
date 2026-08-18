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
// 它也是 `verify-browser.mjs` 里的一道（十道共用一个浏览器与一台服务器）。

import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { checkNakiGroups, readNakiGroups } from "./naki-marks.mjs";
import { hostPage } from "./serve.mjs";
import { stepOnce } from "./table-drive.mjs";

/** 这一局走哪颗种子。挑它的过程见报告 44：`有主见` 档下它第 35 手就把八项全摆出来了。 */
const SEED = 9;

/** 走到「八项都有东西可核」为止的手数上限。到不了就报错（防空转）。 */
const TURN_BUDGET = 90;

/** 取完快照再走几手，用来验「巡目真的在走」。 */
const JUNME_STEPS = 6;

/**
 * 上帝视角下再走几手，为的是**等到左右两家轮到刚摸那张**（票 82）：
 * 那张牌一次只在一家手里，而竖排那两家的「刚摸那张摆开了」是纵向的一段空。
 * 四家轮着摸，五手之内必然轮到——轮不到就说明这一条在空转，闸门自己会说。
 */
const DRAWN_HUNT = 5;

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

        const player = pick("player");

        return {
          seat,
          position: node.getAttribute("data-seat-position"),
          riichi: node.getAttribute("data-riichi"),
          // 名牌上「这一席是谁在打」（票 82）：人读的是那几个字，`data-player` 是同一件事
          // 给机器看的那一半。
          player: { text: text(player), data: attr(player, "data-player") },
          marks: [...node.querySelectorAll(".seat-head .mark")].map((mark) => mark.textContent),
          score: { text: text(pick("score")), data: attr(pick("score"), "data-score") },
          junme: { text: text(pick("junme")), data: attr(pick("junme"), "data-junme") },
          hand: {
            data: hand.getAttribute("data-hand-count"),
            hidden: hand.getAttribute("data-hand-hidden"),
            label: text(hand.querySelector(".row-label")),
            faces: hand.querySelectorAll("[data-pai]").length,
            backs: hand.querySelectorAll(".tile.back").length,
            // 牌背画得出来吗（票 32）：边框色与牌背图（票 80 起是 Back.svg，从前是斜纹底）缺一样就是一块看不见的牌。
            backInk: back === null ? null : getComputedStyle(back).borderTopColor,
            backPattern: back === null ? null : getComputedStyle(back).backgroundImage,
            // 这一排牌**每一张画在哪儿、多大**（票 82 起读真坐标）：一手牌是不是一条线、
            // 刚摸那张有没有摆开、左右两家的牌有没有真的转 90°，三条断言都读它。
            // 从前只读 `.tile.drawn` 的 `margin-left`——那个数在竖排那两家恒为 0，
            // 而「摆开了没有」本来就该按**画出来的间距**判，不该按某一条 CSS 属性判。
            tiles: [...hand.querySelectorAll(".tile")].map((tile) => {
              const rect = tile.getBoundingClientRect();
              return {
                x: rect.x + rect.width / 2,
                y: rect.y + rect.height / 2,
                w: rect.width,
                h: rect.height,
                drawn: tile.classList.contains("drawn"),
              };
            }),
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
    // 赤牌（票 22 定的记号；票 80 起牌面是 SVG）：牌面图与牌框色两个通道都读。
    // 对照组不再是「随便哪张普通牌」，而是**同花色同数的普通五**（data-pai 去掉尾缀 r），
    // 用一枚探针元素从同一份样式表里现取——「赤五与普通五一眼可分」对照的本来就该是那张牌。
    const aka = document.querySelector(".tile.aka");
    const akaStyle = aka === null ? null : getComputedStyle(aka);
    const plainOfAka = (() => {
      const code = aka === null ? null : aka.getAttribute("data-pai");
      if (code === null) return null;
      const probe = document.createElement("span");
      probe.className = "tile";
      probe.setAttribute("data-pai", code.replace(/r$/, ""));
      aka.parentElement.append(probe);
      const style = getComputedStyle(probe);
      const read = {
        pai: probe.getAttribute("data-pai"),
        border: style.borderTopColor,
        image: style.backgroundImage,
      };
      probe.remove();
      return read;
    })();

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
        pai: attr(aka, "data-pai"),
        border: akaStyle === null ? null : akaStyle.borderTopColor,
        image: akaStyle === null ? null : akaStyle.backgroundImage,
        plain: plainOfAka,
      },
      bound: attr(field("table-agent"), "data-seats"),
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
    drawn: shot.seats.some((seat) => seat.hand.tiles.some((tile) => tile.drawn)),
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
          problems.push(`${where}的牌背没有牌背图：与明牌一眼分不开`);
      }
    } else {
      if (seat.hand.faces !== count)
        problems.push(`${where}写着 ${count} 张，可牌桌上画出 ${seat.hand.faces} 张牌面`);
      if (seat.hand.backs !== 0)
        problems.push(`${where}是亮着的，却还有 ${seat.hand.backs} 张扣着`);
    }
  }

  // 刚摸那张摆得离手牌远一点（票 22）。它是七种记号里占「间距」那一维的那一个。
  // **量的是画出来的间距**（票 82）：它与「牌摆成一行还是一列」一起在下面那个函数里。
  for (const seat of shot.seats) checkHandLine(seat, problems);

  const revealed = shot.seats.filter((seat) => seat.hand.hidden === "false");
  if (revealed.length !== 1)
    problems.push(`坐着看时该有且只有一家亮着手牌，实际 ${revealed.length} 家`);
  else if (revealed[0].position !== "self")
    problems.push(
      `亮着手牌的是座位 ${revealed[0].seat}，可它画在「${revealed[0].position}」那个方位上`,
    );
}

/**
 * ①' 一手牌读得出是一手牌（票 82）：**它必须摆成一条线**，而且那条线的方向跟着方位走。
 *
 * 三件事一起核，都读真坐标：
 *
 *   1. **不换行 / 不换列**：上下两家横着一排（y 一样、x 递增），左右两家竖着一列（x 一样、y 递增）。
 *      主人试玩报的正是这一条——三分之一宽放不下 14 张，一手牌被折成两截就读不出是一手牌；
 *   2. **左右两家的牌真的转了 90°**：转过之后画出来的盒子是横的（宽 > 高），上下两家仍是竖的。
 *      `getBoundingClientRect` 给的是**变换之后**的盒子，因此这一条量的是眼睛看到的朝向，
 *      不是「样式表里写了 rotate」；
 *   3. **刚摸那张摆开了**（票 22 的记号）：它与前一张之间那段空，必须比手牌里其余相邻两张之间的都大。
 *      从前这一条读 `.tile.drawn` 的 `margin-left`——竖排那两家那个数恒为 0，
 *      而记号本身仍在（改成了纵向的那一段空）。
 */
function checkHandLine(seat, problems) {
  const tiles = seat.hand.tiles;
  if (tiles.length < 2) return;

  const sideways = seat.position === "kamicha" || seat.position === "shimocha";
  const along = sideways ? "y" : "x";
  const across = sideways ? "x" : "y";
  const line = sideways ? "列" : "行";
  const where = `座位 ${seat.seat}（${seat.position}）的手牌`;

  const spread =
    Math.max(...tiles.map((tile) => tile[across])) - Math.min(...tiles.map((tile) => tile[across]));
  if (spread > 1)
    problems.push(
      `${where} ${tiles.length} 张没摆成一${line}（${across} 差了 ${Math.round(spread)}px）：` +
        "折成两截就读不出是一手牌了（票 82）",
    );

  for (let index = 1; index < tiles.length; index += 1) {
    if (!(tiles[index][along] > tiles[index - 1][along]))
      problems.push(
        `${where}第 ${index} 张没画在第 ${index - 1} 张后面` +
          `（${along}=${Math.round(tiles[index][along])} vs ${Math.round(tiles[index - 1][along])}）：` +
          `这一家的牌该按${sideways ? "上→下" : "左→右"}排`,
      );
  }

  // 转没转 90°：左右两家画出来的盒子该是横的，上下两家该是竖的。
  const landscape = tiles.filter((tile) => tile.w > tile.h).length;
  if (sideways && landscape !== tiles.length)
    problems.push(
      `${where}只有 ${landscape}/${tiles.length} 张转了 90°` +
        `（第一张画出来是 ${Math.round(tiles[0].w)}×${Math.round(tiles[0].h)}）：左右两家的牌该侧着摆（票 82）`,
    );
  if (!sideways && landscape !== 0)
    problems.push(`${where}有 ${landscape}/${tiles.length} 张横过来了：上下两家的牌该正着摆`);

  // 刚摸那张与前一张之间那段空，比其余相邻两张之间的都大。
  const drawnIndex = tiles.findIndex((tile) => tile.drawn);
  if (drawnIndex > 0) {
    const size = sideways ? tiles[drawnIndex].h : tiles[drawnIndex].w;
    const gapAt = (index) => tiles[index][along] - tiles[index - 1][along] - size;
    const drawnGap = gapAt(drawnIndex);
    const others = [];
    for (let index = 1; index < tiles.length; index += 1)
      if (index !== drawnIndex) others.push(gapAt(index));
    const widest = others.length === 0 ? 0 : Math.max(...others);

    if (!(drawnGap > widest + 1))
      problems.push(
        `座位 ${seat.seat} 刚摸的那张没摆开（与前一张空 ${Math.round(drawnGap)}px，` +
          `手牌里其余相邻两张空 ${Math.round(widest)}px）：它就混回手牌里了`,
      );
  }
}

/**
 * ①'' 名牌上看得出**这一席是谁在打**（票 82 的意见⑤）。
 *
 * 这一道跑的是四家自带 bot 的一桌，因此名牌上该写那两档的中文（「均匀随机」/「有主见」），
 * 而且**与状态行那一份对得上**（`data-seats` 是 `Roster.names` 那一份 wire 名）：
 * 两头说的是同一件事，一头写死了另一头当场就红。
 * 模型席那一半（档案名 + 脚手架档位）由 `verify-seats` 核，回放那一半（`provider/model`）
 * 由 `verify-home` 核——**三种来源各有各的闸门**。
 */
function checkNameplates(shot, want, problems) {
  for (const seat of shot.seats) {
    const where = `座位 ${seat.seat} 的名牌`;
    if (seat.player.text === null || seat.player.text === "")
      problems.push(`${where}上没写这一席是谁在打（seat-${seat.seat}-player）`);
    else if (seat.player.text !== seat.player.data)
      problems.push(
        `${where}上写着「${seat.player.text}」，data-player 却是「${seat.player.data}」`,
      );
    else if (seat.player.text !== want)
      problems.push(`${where}上写着「${seat.player.text}」，可这一席坐的是「${want}」`);
  }
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

/**
 * 赤牌（票 22 定的记号，票 80 把牌面换成 SVG 后判据跟着换）。
 *
 * 从前牌是文字，「一眼可分」= 字色不同（赤 rgb(192,57,43) vs 普通 rgb(0,0,0)）；
 * 现在牌是图，同一条语义变成**两个通道**，一条没放宽反而多了一条：
 * ① 牌面图必须与同花色的普通五不同（`-Dora` 变体），且两张都真贴了图；
 * ② 牌框色必须与普通五不同（朱红框）。
 */
function checkAka(shot, problems) {
  const { text, pai, border, image, plain } = shot.aka;
  if (text === null) return; // 没摆出赤牌时由防空转那条（coverage.aka）去红，不在这里重报。

  if (plain === null) {
    problems.push(`牌桌上的赤牌「${text}」没有 data-pai：找不到该拿哪张普通五来对照`);
    return;
  }

  if (image === null || image === "none" || plain.image === "none")
    problems.push(
      `赤牌「${text}」（${pai}）或普通「${plain.pai}」没贴牌面图（赤 ${image}／普通 ${plain.image}）：牌不像牌了`,
    );
  else if (image === plain.image)
    problems.push(
      `赤牌「${text}」（${pai}）与普通「${plain.pai}」贴的是同一张牌面图：赤 5 看不出来是赤 5`,
    );

  if (border === plain.border)
    problems.push(
      `赤牌「${text}」的牌框与普通「${plain.pai}」同色（${border}）：一眼扫过去分不出赤 5`,
    );
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
  // 票 73 之后四席各拨各的，因此这里逐席拨回去。
  for (const index of [0, 1, 2, 3]) await page.getByTestId(`table-seat-${index}-random`).click();
  const said = [];
  // 防空转（票 82）：位置断言按方位取轴（左右两家竖着摆），因此**四个方位都得真核过**。
  // 五个视角轮一圈本来就会把同一组副露摆到四个方位上——没轮到就说明视角那一圈没生效。
  const seenPositions = new Set();

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

      for (const group of shot) seenPositions.add(group.position);

      if (reference === null) reference = { where, shape };
      else if (shape !== reference.shape)
        problems.push(
          `种子 ${corpus.seed}：换了视角副露的位置跟着变了——${reference.where}看到「${reference.shape}」，${where}看到「${shape}」。位置的参照系是副露方自己，不是看牌桌的那个人`,
        );
    }

    said.push(`种子 ${corpus.seed} 走 ${walked} 手：${[...new Set(seen)].sort().join("、")}`);
  }

  const missedPositions = POSITIONS.filter((position) => !seenPositions.has(position));
  if (missedPositions.length > 0)
    problems.push(
      `副露的位置断言没在这几个方位上执行过：${missedPositions.join("、")}` +
        "——左右两家竖着摆（票 82），那两个方位不核等于位置编码只验了一半",
    );

  // 参照系那句话得写在牌桌上（M1 第六条）：位置编码是一套约定，不写出来读者只能猜。
  // **左右两家竖排之后那句话要多说一件事**（票 82）：副露方自己的左→右在屏幕上是上→下。
  const legend = await page.getByTestId("table-naki-legend").textContent();
  for (const word of [
    "最左",
    "上家",
    "中间",
    "对家",
    "最右",
    "下家",
    "副露方",
    "左右两家",
    "上→下",
  ]) {
    if (!legend.includes(word))
      problems.push(
        `牌桌上那句副露位置的说明里没有「${word}」：位置编码没声明参照系（读的是「${legend}」）`,
      );
  }

  return said;
}

// ---- 跑 ----

/** 牌桌那一道（票 44 + 票 51）。返回的是失败清单（空 = 绿）。 */
export async function verifyBoard(lane) {
  const url = await lane.previewUrl();
  const page = await lane.newPage({ viewport: { width: 1180, height: 1200 } });
  const problems = [];
  const pageProblems = [];

  // try 里只收集，`finally` 关页面，报告放在最后——写法照 `verify-export.mjs`：
  // 在 try 里 `process.exit` 会把 `finally` 整个跳过，页面与服务器都关不掉。
  let walked = 0;
  let shot = null;
  let later = null;
  let total = 0;
  let naki = { seen: "无" };
  let bottoms = "";
  let positions = [];
  let sidewaysDrawn = 0;

  try {
    page.on("pageerror", (error) => pageProblems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() === "error") pageProblems.push(`[console.error] ${message.text()}`);
    });

    await page.goto(hostPage(url), { waitUntil: "load" });
    await page.getByTestId("table-board").waitFor();

    // **先坐到座位 0**（票 82）：这一页的默认视角从此是上帝视角（票 81 交办的那一件），
    // 而下面八项里的「有且只有一家亮着手牌」量的是**坐着看**那一份投影。
    // 不点这一下的话它量的是闸门自己——票 81 给 `verify-bubbles` 补的是同一句话（那边补的是上帝）。
    // **断言一条没放宽**：只是把它依赖的那个视角显式说出来。
    await page.getByTestId("table-view-0").click();

    // 四家都换成「有主见」那一档（票 42）：均匀随机几乎不立直（1996 场里 15 次），
    // 而立直、供托与立直棒这三项要立直才走得到。票 73 之后四席各拨各的，因此拨四次。
    for (const index of [0, 1, 2, 3])
      await page.getByTestId(`table-seat-${index}-opinionated`).click();
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

    // 四席逐个核（票 73：从前那个全局开关只报一个名字，现在四席各报各的）。
    if (shot.bound !== "opinionated,opinionated,opinionated,opinionated")
      problems.push(`四席都该是「有主见」那一档，data-seats 却是「${shot.bound}」`);
    if (!shot.agent.includes("有主见"))
      problems.push(`状态行写着「${shot.agent}」，可其余座位坐的是「有主见」那一档`);

    checkHands(shot, problems);
    checkNameplates(shot, "有主见", problems);
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

    // **竖排那两家的牌面只有上帝视角摆得出来**（坐着看时他家整排是牌背）：
    // 「一列不换列」「转了 90°」「刚摸那张纵向摆开」这三条因此在上帝视角上再核一遍。
    // 走几手是为了让**左右两家真的轮到手**——刚摸那张一次只在一家手里，
    // 碰不上的话那一条在竖排这一侧就从没开过口（判据 3）。
    await page.getByTestId("table-view-god").click();
    for (let step = 0; step < DRAWN_HUNT && sidewaysDrawn === 0; step += 1) {
      const godShot = await readBoard(page);
      for (const seat of godShot.seats) checkHandLine(seat, problems);
      sidewaysDrawn += godShot.seats.filter(
        (seat) =>
          (seat.position === "kamicha" || seat.position === "shimocha") &&
          seat.hand.tiles.some((tile) => tile.drawn),
      ).length;
      if (sidewaysDrawn === 0 && !(await stepOnce(page))) break;
    }
    if (sidewaysDrawn === 0)
      problems.push(
        `上帝视角下走了 ${DRAWN_HUNT} 手，左右两家一次都没轮到刚摸那张：` +
          "「竖排那两家刚摸的牌摆开了」那一条在空转",
      );
    await page.getByTestId("table-view-0").click();

    bottoms = await checkLayout(page, problems);
    // 票 51：横放那张的**位置**就是来源。它另走两局均匀随机的牌（把九种槽位结果摆齐），
    // 因此一定要摆在最后：上面八项读的是种子 9「有主见」那一局的快照。
    positions = await checkNakiPositions(page, problems);
  } finally {
    await page.close();
  }

  if (pageProblems.length > 0) return failure("页面报了错：", pageProblems);
  if (problems.length > 0) return failure("牌桌上少了该给人看的东西：", problems);

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
    `  手牌摆成一条线 ✓（${shot.seats
      .map(
        (seat) =>
          `${seat.position} ${seat.hand.tiles.length} 张${
            seat.position === "kamicha" || seat.position === "shimocha" ? "竖着一列" : "横着一行"
          }`,
      )
      .join("、")}）`,
  );
  console.log(`  （上帝视角里）竖排那两家刚摸的牌也摆开了 ✓（轮到 ${sidewaysDrawn} 家）`);
  console.log(
    `  名牌 ✓（${shot.seats.map((seat) => `座位 ${seat.seat} ${seat.player.text}`).join("、")}）`,
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
    `  （顺带）赤牌 ${shot.aka.text}（${shot.aka.pai}）牌框 ${shot.aka.border} ≠ 普通 ${shot.aka.plain.pai} ${shot.aka.plain.border}、牌面图也不同、` +
      `刚摸那张摆开了 ✓`,
  );
  console.log(`  方位 ✓（${bottoms}）`);
  console.log("  副露的位置就是来源 ✓（五个视角逐个对拍绝对座位，且五个视角看到的位置逐字相同）");
  for (const line of positions) console.log(`    ${line}`);
  return [];
}

if (isEntry(import.meta.url)) {
  await runStandalone(verifyBoard);
}
