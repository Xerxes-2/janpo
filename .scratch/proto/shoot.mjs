// 原型截图 + 数（不是闸门）。
// v2 换了量的东西：主人准许气泡覆盖，所以「气泡×牌 = 0」不再是判据。
// 顶上来的三条是：收起态有上限、点得开也点得收（两个方向都真点）、
// 牌不越出牌桌。外加白板描边与名字是否被截。
import { mkdirSync } from "node:fs";
import { resolve } from "node:path";
import { chromium } from "../../web/node_modules/playwright-core/index.mjs";
import { chromeExecutable, missingChrome } from "../../web/scripts/chrome.mjs";

const here = import.meta.dirname;
mkdirSync(resolve(here, "shots"), { recursive: true });
const executablePath = chromeExecutable();
if (!executablePath) { console.error(missingChrome); process.exit(1); }
const browser = await chromium.launch({ executablePath, headless: true });
const url = "file://" + resolve(here, "index.html");
const SEATS = ["self", "shimo", "toimen", "kami"];
const setPhase = (page, phase) =>
  page.evaluate((p) => { document.getElementById("stage").dataset.phase = p; }, phase);

async function geometry(page) {
  return await page.evaluate(() => {
    const r = (s) => document.querySelector(s).getBoundingClientRect();
    const stage = r("#stage"), board = r("#board");
    const tiles = [...document.querySelectorAll(".tile")]
      // 只算**桌上的**牌：中央盘的宝牌、顶条的宝牌、卡片里「有效牌」的小样都不算
      .filter((e) => !e.closest("#center, .topbar, .col"))
      .map((e) => e.getBoundingClientRect());
    const outside = (t, box) =>
      t.left < box.left - 1 || t.right > box.right + 1 ||
      t.top < box.top - 1 || t.bottom > box.bottom + 1;
    let outStage = 0, outBoard = 0;
    for (const t of tiles) { if (outside(t, stage)) outStage += 1; if (outside(t, board)) outBoard += 1; }
    // 名字有没有被截（scrollWidth > clientWidth 即被 ellipsis 吃了）
    // 选择器必须真抓到东西：v4 把 .nameplate 改成了 .roster，而这一条还指着旧名，
    // 于是扇了一个空集、永远报「无」——一条静静绣的断言。
    // 因此同时报**扇到几个**：数为 0 就是闸门自己坏了，不是页面好了。
    // scrollWidth 探不出来：Chrome 对 flex 子项里被 ellipsis 裁掉的文本不抬它
    // （实测四个名字 scrollWidth === clientWidth，而图上明明有省略号）。
    // 改用 canvas 按计算样式量文本真实宽度，并**报出最坏余量**——
    // 只报「有没有被截」会让 0.2px 的巧合看着像设计。
    const ctx = document.createElement("canvas").getContext("2d");
    const names = [...document.querySelectorAll(".roster .who")].map((e) => {
      const cs = getComputedStyle(e);
      ctx.font = `${cs.fontWeight} ${cs.fontSize} ${cs.fontFamily}`;
      return { text: e.textContent, need: ctx.measureText(e.textContent).width, have: e.clientWidth };
    });
    const clipped = names.filter((n) => n.need > n.have).map((n) => `${n.text}(超${(n.need - n.have).toFixed(1)}px)`);
    const headroom = names.length ? Math.min(...names.map((n) => n.have - n.need)) : null;
    const haku = [...document.querySelectorAll(".tile.haku")];
    const hit = (a, b) =>
      a.left < b.right - 1 && b.left + 1 < a.right && a.top < b.bottom - 1 && b.top + 1 < a.bottom;
    const cross = (sel) => {
      let n = 0;
      for (const e of document.querySelectorAll(sel)) {
        const rr = e.getBoundingClientRect();
        for (const t of tiles) if (hit(rr, t)) n += 1;
      }
      return n;
    };
    const drawer = document.querySelector(".drawer");
    const drawerOpen = drawer && getComputedStyle(drawer).display !== "none";
    return {
      // 气泡：**收起态**不得压牌（常驻）；展开态允许压（主人准的）——只报不断
      // 收起态 = 名册里那枚 pill（在左列，不占牌桌一寸）；展开态 = 浮层，准许盖牌
      shutBubbleTile: cross(".pop:not(.open)") + cross(".pill"),
      openBubbleTile: cross(".pop.open"),
      colTile: cross(".col"),
      // 横牌的**牌面**跟着盒子横了没有。
      // 不能直接拿屏幕坐标量：席区自身转了 0/90/180/270，
      // 左右两家的横牌在屏幕上本来就该是竖的（第一版就这么量错的，
      // 拆旋转与不拆都报 3 枚——一个对错两态同值的断言，彻底无效）。
      // 正确的不变量：牌面与它自己的盒子**同朝向**，与席区旋转无关。
      // 浮层的尾巴对没对上它那一行名册（尾巴在 pop 顶端 +12px 处）
      // 浮层之间互相压没压（相邻名册行只差 26px，而浮层高 ~45px）
      popOverlap: (() => {
        const ps = [...document.querySelectorAll(".pop.open")].map((e) => e.getBoundingClientRect());
        let n = 0;
        for (let i = 0; i < ps.length; i += 1)
          for (let j = i + 1; j < ps.length; j += 1) if (hit(ps[i], ps[j])) n += 1;
        return n;
      })(),
      popAligned: (() => {
        const out = { open: 0, off: 0 };
        for (const pop of document.querySelectorAll(".pop.open")) {
          const row = document.querySelector(`.roster .seat[data-pop="${pop.dataset.seat}"]`);
          if (!row) continue;
          out.open += 1;
          const pr = pop.getBoundingClientRect(), rr = row.getBoundingClientRect();
          const tail = pr.top + 12 * (pr.width / 320);
          if (tail < rr.top - 2 || tail > rr.bottom + 2) out.off += 1;
        }
        return out;
      })(),
      sideFaces: (() => {
        const out = { total: 0, mismatched: 0 };
        for (const box of document.querySelectorAll(".tile.called, .kawa .tile.side")) {
          const img = box.querySelector("img");
          if (!img) continue;
          const b = box.getBoundingClientRect(), i = img.getBoundingClientRect();
          out.total += 1;
          if (Math.sign(b.width - b.height) !== Math.sign(i.width - i.height)) out.mismatched += 1;
        }
        return out;
      })(),
      // 两列内部不许互相压（同一列里的卡片是 flex 排的，压了就是布局坏了）
      colOverflow: [...document.querySelectorAll(".col")].filter((c) => {
        const r = c.getBoundingClientRect();
        return [...c.children].some((k) => {
          const q = k.getBoundingClientRect();
          // display:none 的子元素矩形全零，不能当溢出算（第一版就这么误报 2 的）
          if (q.width === 0 && q.height === 0) return false;
          return q.bottom > r.bottom + 1 || q.top < r.top - 1;
        });
      }).length,
      drawerBoard: drawerOpen
        ? (hit(drawer.getBoundingClientRect(), board) ? "盖了" : "没盖")
        : "—",
      scale: +(stage.width / 1200).toFixed(3),
      tiles: tiles.length,
      outStage, outBoard,
      selfTilePx: +(document.querySelector('.zone[data-r="0"] .hand .tile')
        .getBoundingClientRect().width).toFixed(1),
      namesScanned: names.length,
      nameHeadroom: headroom === null ? null : +headroom.toFixed(1),
      clippedNames: clipped,
      hakuCount: haku.length,
      hakuRinged: haku.every((e) => getComputedStyle(e).boxShadow !== "none"),
      stageInViewport: stage.width <= innerWidth + 1 && stage.height <= innerHeight + 1,
      docScroll: document.documentElement.scrollHeight > innerHeight + 1,
    };
  });
}

/** 收起态尺寸 + 真点两个方向。这是替掉「气泡不相交」的那条。 */
async function bubbles(page) {
  const box = async (seat) =>
    await page.locator(`.pop[data-seat="${seat}"]`).evaluate((e) => {
      const r = e.getBoundingClientRect();
      return { w: Math.round(r.width), h: Math.round(r.height), open: e.classList.contains("open") };
    });
  const out = [];
  for (const seat of SEATS) {
    const shut = await box(seat);
    await page.locator(`.roster .seat[data-pop="${seat}"]`).click();
    const open = await box(seat);
    await page.locator(`.roster .seat[data-pop="${seat}"]`).click();
    const shutAgain = await box(seat);
    out.push({
      seat,
      shut: `${shut.w}×${shut.h}`,
      open: `${open.w}×${open.h}`,
      opens: !shut.open && open.open && open.w > 0,
      shuts: !shutAgain.open && shutAgain.w === 0,
    });
  }
  return out;
}

for (const [name, w, h, prep] of [
  ["A-play", 1280, 800, null],
  ["B-play-pop", 1280, 800, "open"],
  ["B2-play-pop-adjacent", 1280, 800, "open-adjacent"],
  ["C-review", 1280, 800, "review"],
  ["D-review-fold", 1280, 800, "review-fold"],
  ["E-drawer", 1280, 800, "drawer"],
  ["F-390x844", 390, 844, null],
  ["G-1440x900", 1440, 900, null],
]) {
  const page = await browser.newPage({ viewport: { width: w, height: h } });
  page.on("pageerror", (e) => console.error(`  [pageerror] ${e.message}`));
  await page.goto(url, { waitUntil: "networkidle" });
  if (prep === "open")
    for (const s of ["toimen", "self"]) await page.locator(`.roster .seat[data-pop="${s}"]`).click();
  if (prep === "open-adjacent")
    for (const s of ["toimen", "kami"]) await page.locator(`.roster .seat[data-pop="${s}"]`).click();
  if (prep === "drawer") await page.evaluate(() => { document.getElementById("cfg").checked = true; });
  if (prep === "review") await setPhase(page, "review");
  if (prep === "review-fold") {
    await setPhase(page, "review");
    for (const f of await page.locator("[data-fold]").all()) await f.click();
  }
  const g = await geometry(page);
  console.log(
    `${name.padEnd(19)} scale=${g.scale} 牌${g.tiles}枚 越出牌桌${g.outBoard} 牌宽${g.selfTilePx}px ` +
      `一屏=${g.stageInViewport} 文档滚动=${g.docScroll} 白板${g.hakuCount}枚描边=${g.hakuRinged}\n` +
      `${" ".repeat(20)}相交：收起气泡×牌=${g.shutBubbleTile} 展开气泡×牌=${g.openBubbleTile}` +
      ` 列×牌=${g.colTile} 列内溢出=${g.colOverflow}` +
      ` 抽屉×牌桌=${g.drawerBoard} 名字（扇到 ${g.namesScanned} 个）被截=${g.clippedNames.length ? g.clippedNames.join("|") : "无"} 最坏余量=${g.nameHeadroom}px\n` +
      `${" ".repeat(20)}横牌 ${g.sideFaces.total} 枚，牌面与盒子朝向不一致 = ${g.sideFaces.mismatched}` +
      `　浮层开着 ${g.popAligned.open} 枚，尾巴没对上=${g.popAligned.off} 互相压=${g.popOverlap}`,
  );
  await page.screenshot({ path: resolve(here, "shots", `${name}.png`) });
  await page.close();
}

// 气泡开合只在 1280×800 上验一遍（四席各走「开→收」一个来回）
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
await page.goto(url, { waitUntil: "networkidle" });
console.log("\n气泡（收起 → 点开 → 再点收，四席各一个来回）：");
for (const b of await bubbles(page))
  console.log(`  ${b.seat.padEnd(7)} 收起 ${b.shut.padEnd(9)} 展开 ${b.open.padEnd(10)} 点得开=${b.opens} 点得收=${b.shuts}`);
await page.close();
await browser.close();
