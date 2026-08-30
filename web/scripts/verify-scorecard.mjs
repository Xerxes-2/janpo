// 终局记分卡的无头闸门（票 133）：**打完之后有没有一张四家可比的表，它说的数对不对，
// 复制出去那段纯文本与它说的是不是同一件事，以及那段文本里有没有 key。**
//
// 语料是首页那份 Demo 牌谱（`web/public/demo-paifu.json`，四席模型、6 局、464 条决策记录）：
// 它是仓库里唯一一份**打完了整场、而且每一手都有账单**的真语料，
// 拖一下时间轴就到终局——不必再打一整场（`verify-export --to-end` 那一趟已经在打了）。
//
// ## 阴性与阳性钉在同一个量点上（判据 20 / 21）
//
// 「还没终局时整块不在 DOM 里」这半句在**任何一张空页面上都成立**，它自己证明不了什么。
// 因此两句话钉在**同一条时间轴的同一次拖动**上：先把游标拖到第 0 帧（开局那一瞬）——
// 记分卡必须不在；再拖到末帧（终局那一屏）——记分卡必须在，且四行齐。
// 先红的必须是**阳性**那半句。
//
// ## 每一格的右侧是引擎，而不是页面自己
//
// 逐格对拍的右侧是 `ScorecardCheck.tally`（`Replay.ofPaifu` 一次 fold 到底），
// 页面那一侧走的是 `Table.replay` 摊出来的逐帧牌桌再取末帧——**顺位与终点**那两列
// 因此是两条路（判据 6）。
//
// **其余七列不是。** 和了 / 放铳 / 问过 / 兜底 / 重试 / 输入 / 输出这七列两侧共用的
// 就是引擎里同一段 `Scorecard.tally`（那一段本来就该只有一份，判据 11），
// 于是那种对拍是**恒真式**——本票真按过一次：把 `Attempts - 1` 改成 `Attempts`，
// 「逐格对拍 36 格」照样全绿。因此这一趟另加一个**第三锚点**：
// `tallyFromPaifu` 在 node 里照规则把那份 JSON 重数一遍（含「自摸时 mjai 把 `target`
// 写成和了者自己，那不是放铳」这一条），那七列各与它对一次。
//
// ## key 那一条照票 34 的形状
//
// 一把**看一眼就知道是假**的 key 灌进 localStorage（页面照样把它读进档案库），
// 回放那一页一个请求都不发；而「复制记分卡」出去的那段纯文本里绝不该出现它。
// `--poison` 把那把 key 拌进被检查的那段文本，于是那条断言**必须**当场红——
// 一道从不失败的闸门等于没有闸门。
//
// 跑法：`cd web && pnpm run fable && node scripts/verify-scorecard.mjs`
//       `node scripts/verify-scorecard.mjs --poison`   # 它单跑时**该**以 1 退出
// 它也是 `verify-browser.mjs` 那张 `gates` 表里的两项。

import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, markerSince, runStandalone } from "./browser-lane.mjs";
import { plantSeating } from "./seating.mjs";
import { retryOnReload } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/**
 * 灌进 localStorage 的那把 key（票 34 那一趟逐字同一个理由）。**它是假的，
 * 且看一眼就知道是假的**：这道闸门要的只是「浏览器里确实有一把 key 可以夹带」。
 * 全 ASCII 是故意的——断言是按字节在那段文本里找它。
 */
const FAKE_KEY = "sk-janpo-fake-key-NOT-A-REAL-KEY-jia-4f2a91";

/**
 * 回放那一屏「选手 · 档」那一格的 **wire 值**（F# 侧 `ScorecardPlayer.toWire`）：
 * **身份牌谱里有（`start_game.names`），档位牌谱没记**。
 *
 * **断言读的是它，不是那句中文**（判据 24）：措辞改一个字，视觉与保证一个字没变，
 * 而拿中文当断言的闸门会当场红——那是把今天的措辞焊死成明天的规格。
 * 那句中文照旧印出来给人看（`data-player`），只是不进断言。
 */
const TIER_UNRECORDED = "tier-unrecorded";

/** 档位那半格上回放该写的那句话（F# 侧 `ScorecardPlayer.tierSaid`）。 */
const TIER_SAID = "档位牌谱没记";

/**
 * 四个风字（票 145）。**记分卡最左那一列里一个都不许出现**。
 *
 * 理由不是措辞好看：风**每一局都在转**，而记分卡是**整场**的结论。
 * 133 那一版取的是末局自风，东风战里座位 0 是起家（开局的东家），
 * 打完之后那一格却写着「座位 0 南」——而这张表是要被贴出去的，
 * 贴出去之后没人能纠正。
 *
 * **它断言的是「人看得见的那个保证」，不是某个措辞**（判据 24）：表头改叫别的、
 * 席位那一格换个说法，**这一条**都不该红；只要哪一天有人把一个转着的风塞回最左那一列，
 * 它必须当场红。（措辞由 ③ 那一条管——那一格的字面就是人看见的东西，
 * 它逐字钉着 `座位 N`。两条各管一半，别把它们的职责混起来读。）
 */
const KAZE_SAID = ["东", "南", "西", "北", "风"];

/** 这一段字里出现了哪几个风字（一个都没有时是空数组）。**「风」这个字自己也算**：
 * 表头上那半句（「席位 · 风」）与格子里那个转着的风是同一件事的两半，只改一处就会留着旧说法。 */
function windsIn(said) {
  return KAZE_SAID.filter((kaze) => said.includes(kaze));
}

/** 页面上那张表逐格要核的那几项（`data-*` 的名字就是这几个）。 */
const COLUMNS = [
  "juni",
  "score",
  "hora",
  "hora-targeted",
  "fallbacks",
  "retries",
  "asked",
  "input",
  "output",
];

/** 首页那份 Demo 牌谱的原文：页面 fetch 的就是这个文件。 */
function demoText() {
  return readFileSync(resolve(webRoot, "public/demo-paifu.json"), "utf8");
}

/**
 * **第三锚点**（判据 6）：闸门自己在 node 里，照**规则**把那份牌谱重数一遍。
 *
 * 没有它的话，「页面上那一格」与「`ScorecardCheck` 那一格」在这七列上共用的是引擎里
 * 同一段 `Scorecard.tally`——那种对拍是恒真式，把 `Attempts - 1` 改成 `Attempts`
 * 它照样全绿（本票真按过一次，见报告）。**顺位与终点不在这里**：那两列两侧本来就
 * 是两条路（`Replay.ofPaifu` 对 `Table.replay` 的帧）。
 *
 * 规则各是哪一条：
 *   - 和了 = `hora` 事件里 `actor` 指着这一席的条数；
 *   - 放铳 = `target` 指着这一席**而 `actor` 是别人**的条数
 *     （自摸时 mjai 把 `target` 写成和了者自己，照字面数会给每次自摸记一笔放铳）；
 *   - 问过 / 兜底 / 重试 = 这一席那几条决策记录的条数 / `fallback` 非空的条数 /
 *     `attempts - 1` 之和（首问不算重试）；
 *   - 输入 tok = `input + cache_read + cache_write`（付全价的 + 命中的 + 写缓存的），
 *     输出 tok = `output`；四个字段各自可缺省，缺了按 0 算。
 */
function tallyFromPaifu(text) {
  const paifu = JSON.parse(text);
  const counts = new Map();
  const row = (seat) => {
    const found = counts.get(seat) ?? {
      hora: 0,
      "hora-targeted": 0,
      asked: 0,
      fallbacks: 0,
      retries: 0,
      input: 0,
      output: 0,
    };
    counts.set(seat, found);
    return found;
  };

  let horas = 0;
  for (const event of paifu.events ?? []) {
    if (event.type !== "hora") continue;
    horas += 1;
    row(event.actor).hora += 1;
    if (event.target !== event.actor) row(event.target)["hora-targeted"] += 1;
  }

  for (const record of paifu.decisions ?? []) {
    const mine = row(record.seat);
    mine.asked += 1;
    if (record.fallback !== undefined && record.fallback !== null) mine.fallbacks += 1;
    mine.retries += (record.attempts ?? 1) - 1;
    const usage = record.usage ?? {};
    mine.input += (usage.input ?? 0) + (usage.cache_read ?? 0) + (usage.cache_write ?? 0);
    mine.output += usage.output ?? 0;
  }

  return { counts, horas, decisions: (paifu.decisions ?? []).length };
}

/** 第三锚点管得着的那几列（顺位与终点不在其列，理由见 `tallyFromPaifu`）。 */
const ANCHORED = ["hora", "hora-targeted", "asked", "fallbacks", "retries", "input", "output"];

/** 页面上那张表此刻的样子：每一行的 `data-*` 与那几格看得见的字。 */
function shownRows(page) {
  return page.evaluate(() => {
    const section = document.querySelector('[data-testid="table-scorecard"]');
    if (section === null) return { present: false, rows: [] };

    const rows = [...document.querySelectorAll('[data-testid^="scorecard-"]')].map((row) => ({
      data: Object.fromEntries(
        [...row.attributes]
          .filter(
            (attribute) => attribute.name.startsWith("data-") && attribute.name !== "data-testid",
          )
          .map((attribute) => [attribute.name.slice("data-".length), attribute.value]),
      ),
      cells: [...row.querySelectorAll("td")].map((cell) => cell.textContent.trim()),
    }));

    return {
      present: true,
      declared: Number.parseInt(section.getAttribute("data-rows") ?? "-1", 10),
      headers: [...section.querySelectorAll("thead th")].map((cell) => cell.textContent.trim()),
      rows,
    };
  });
}

/** 时间轴此刻停在第几帧、一共几帧。 */
function timelineAt(page) {
  return page.evaluate(() => {
    const slider = document.querySelector('[data-testid="table-timeline"]');
    return {
      cursor: Number.parseInt(slider?.getAttribute("data-cursor") ?? "-1", 10),
      last: Number.parseInt(slider?.getAttribute("data-last") ?? "-1", 10),
    };
  });
}

/** 把游标拖到轴上的某一端（`ratio` 0 = 开局那一瞬，1 = 末帧），拖完等它落定。 */
async function dragTo(page, ratio) {
  const slider = page.getByTestId("table-timeline");
  const box = await slider.boundingBox();
  if (box === null) throw new Error("首页上没有时间轴：这一趟没法把游标挪到终局");

  await slider.click({ position: { x: ratio * (box.width - 1), y: box.height / 2 } });
  await page.waitForFunction(
    (wanted) => {
      const at = document.querySelector('[data-testid="table-timeline"]');
      const cursor = Number.parseInt(at?.getAttribute("data-cursor") ?? "-1", 10);
      const last = Number.parseInt(at?.getAttribute("data-last") ?? "-1", 10);
      return wanted === 0 ? cursor === 0 : cursor === last;
    },
    ratio,
    { timeout: 15000 },
  );

  return timelineAt(page);
}

/** 引擎那一份（**另一条路**）：把牌谱原文喂给 `ScorecardCheck.tally`。 */
function expectedFrom(page, text) {
  return retryOnReload(() =>
    page.evaluate(async (paifu) => {
      // 相对页面地址 import：vite 的 base 可配（JANPO_BASE），写死 "/src/…" 一改 base 就 404。
      const check = await import("./src/generated/ScorecardCheck.js");
      return JSON.parse(check.tally(paifu));
    }, text),
  );
}

/** 记分卡那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyScorecard(lane, options = {}) {
  const { poison = false } = options;

  // 用 dev server 而不是 preview：闸门要在页面里点名 `import` Fable 输出的
  // `ScorecardCheck.js`，而 `dist/` 里的模块被打成一坨、文件名带哈希。
  const url = await lane.devUrl();
  const context = await lane.newContext({ permissions: ["clipboard-read", "clipboard-write"] });
  const problems = [];

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));
    page.on("console", (message) => {
      if (message.type() !== "error") return;
      if (message.text().includes("Failed to load resource")) return;
      problems.push(`[console.error] ${message.text()}`);
    });

    // 票 34 那一档：**key 灌进去，坐席不给**。回放这一页连配桌都没有，一个请求都发不出去。
    await plantSeating(page, {
      profiles: [{ name: "档案 1", api_key: FAKE_KEY }],
      seats: [{}, {}, {}, {}],
    });

    // 首页就是那份 Demo 回放（ADR-0003）。它**自动播**，因此先按暂停再动游标。
    await page.goto(`${url}/`, { waitUntil: "load" });
    await page.getByTestId("table-timeline").waitFor({ timeout: 30000 });
    console.log(`localStorage 里躺着一把假 key：${FAKE_KEY}`);

    const play = page.getByTestId("table-play");
    if ((await play.textContent()).trim() === "暂停") await play.click();

    // ---- ① 阴性与阳性，同一条轴上的同一次拖动（判据 20 / 21） ----

    const opening = await dragTo(page, 0);
    const beforeMark = markerSince(problems);
    const before = await shownRows(page);
    if (before.present) {
      problems.push(
        `游标停在第 ${opening.cursor} 帧（共 ${opening.last + 1} 帧，这一场还没打完），` +
          `记分卡却已经在 DOM 里了：它那时每一格都还没有结论`,
      );
    }
    console.log(`${beforeMark()} 第 0 帧（开局那一瞬）：记分卡不在 DOM 里`);

    const ended = await dragTo(page, 1);
    const shown = await shownRows(page);
    if (!shown.present) {
      problems.push(
        `拖到末帧（第 ${ended.cursor} 帧）却没有记分卡：上面那条「还没终局时不在」因此什么都没证明`,
      );
      return failure("记分卡验收没过：", problems);
    }

    console.log(`拖到末帧（第 ${ended.cursor} 帧，共 ${ended.last + 1} 帧）：记分卡在`);

    // ---- ② 四行齐（**报出它扇到了几个元素**） ----

    const rowsMark = markerSince(problems);
    console.log(`记分卡上扇到 ${shown.rows.length} 行（section 自己报 ${shown.declared} 行）`);
    if (shown.rows.length !== 4) {
      problems.push(`记分卡上只扇到 ${shown.rows.length} 行，四家该有 4 行`);
    }
    if (shown.declared !== shown.rows.length) {
      problems.push(`section 报着 ${shown.declared} 行，DOM 里却是 ${shown.rows.length} 行`);
    }
    console.log(`${rowsMark()} 四家各一行`);

    // ---- ③ 逐格与引擎直接算的那一份对拍 ----

    const text = demoText();
    const expected = await expectedFrom(page, text);
    if (expected.error) {
      problems.push(`引擎算不出这份牌谱的记分卡：${expected.error}`);
      return failure("记分卡验收没过：", problems);
    }

    const cellsMark = markerSince(problems);
    let compared = 0;
    for (const row of shown.rows) {
      const seat = Number.parseInt(row.data.seat ?? "-1", 10);
      const mine = expected.seats.find((each) => each.seat === seat);
      if (mine === undefined) {
        problems.push(`页面上有座位 ${seat} 那一行，引擎那一份里却没有`);
        continue;
      }
      for (const column of COLUMNS) {
        compared += 1;
        if (row.data[column] !== String(mine[column.replace("-", "_")])) {
          problems.push(
            `座位 ${seat} 的 ${column}：页面上是「${row.data[column]}」，引擎算的是「${mine[column.replace("-", "_")]}」`,
          );
        }
      }
    }
    console.log(
      `${cellsMark()} 逐格对拍了 ${compared} 格（${shown.rows.length} 行 × ${COLUMNS.length} 列）`,
    );
    if (compared === 0) problems.push("一格都没对拍上：这一趟的核心断言在空转");

    // ---- ③b 看得见的那几格与同一行的 `data-*` 对得上 ----

    // **不然那七列可以整体错位而没有任何东西开口**：`data-*` 与屏幕上那几格是两份渲染，
    // 逐格对拍（③）只核前者，文本对拍（⑥）两边读的又是同一份 `cells`。
    // 这一条把**看得见的那一格**钉回它自己那个数——判据 24 意义上的「用户可见保证」。
    const visibleMark = markerSince(problems);
    const visible = [
      // **逐字相等，不是前缀**（票 145）：这一格如今只写席位，
      // 前缀比法会放过任何跟在后面的东西——而 133 跟在后面的正是那个转着的风。
      { at: 0, said: "席位", want: (data) => `座位 ${data.seat}` },
      { at: 1, said: "选手 · 档", want: (data) => data.player },
      // 那一格看得见的字就是两半拼起来的（档位那半为空时只有身份）。

      { at: 3, said: "和 · 铳", want: (data) => `${data.hora} · ${data["hora-targeted"]}` },
      { at: 4, said: "兜底", want: (data) => data.fallbacks },
      { at: 5, said: "重试", want: (data) => data.retries },
      { at: 6, said: "输入 · 输出 tok", want: (data) => `${data.input} · ${data.output}` },
    ];

    let looked = 0;
    for (const row of shown.rows) {
      // 「顺位 · 终点」那一格里两个数各占一截，单独核。
      const place = row.cells[2] ?? "";
      looked += 1;
      if (!place.includes(row.data.juni) || !place.includes(row.data.score)) {
        problems.push(
          `座位 ${row.data.seat} 的「顺位 · 终点」看着是「${place}」，而这一行的数是 ${row.data.juni} 位 / ${row.data.score}`,
        );
      }

      for (const column of visible) {
        looked += 1;
        const shownCell = row.cells[column.at] ?? "";
        const wanted = column.want(row.data);
        const ok = shownCell === wanted;
        if (!ok) {
          problems.push(
            `座位 ${row.data.seat} 第 ${column.at} 格（${column.said}）看着是「${shownCell}」，而这一行的数是「${wanted}」`,
          );
        }
      }
    }
    console.log(`${visibleMark()} 看得见的那几格与 data-* 对得上（核过 ${looked} 格）`);
    if (looked === 0) problems.push("一格都没核上：看得见的那几格与 data-* 的对拍在空转");

    // ---- ③c 最左那一列里一个风字都没有（票 145） ----

    // 风每一局都在转，而这张表是**整场**的结论：写一个瞬时的风是误导，
    // 而它正好摆在最左那一列、是人读这张表的第一眼。
    // 表头与四行各核一遍——只改一处的话另一处会留着旧说法。
    const windMark = markerSince(problems);
    console.log(`表头那几格：${shown.headers.join(" / ")}`);
    if (shown.headers.length !== (shown.rows[0]?.cells.length ?? 0)) {
      problems.push(
        `表头有 ${shown.headers.length} 格，而一行是 ${shown.rows[0]?.cells.length ?? 0} 格：对不上号就核不了「哪一格是最左那一列」`,
      );
    }

    const noWind = (said, at) => {
      const winds = windsIn(at);
      if (winds.length === 0) return;
      problems.push(
        `记分卡最左那一列 ${said} 那一格写着「${at}」，里面有风字（${winds.join("")}）：` +
          "风每一局都在转，而这张表是整场的结论",
      );
    };

    // 表头那一格**恒有一格**，∴ 它不进下面那个「扇到 0 个就红」的计数——
    // 把一个恒 ≥ 1 的数拿去与 0 比，兜底就成了走不到的死代码（判据 12）。
    noWind("表头", shown.headers[0] ?? "");

    let windless = 0;
    for (const row of shown.rows) {
      windless += 1;
      noWind(`座位 ${row.data.seat}`, row.cells[0] ?? "");
    }
    console.log(`${windMark()} 最左那一列（表头 + ${shown.rows.length} 行）一个风字都没有`);
    // 这一条**真走得到**：`shown.rows` 空掉时它就是 0（那时表头那一格照样核过了）。
    if (windless === 0) problems.push("最左那一列一行都没核上：那条「不许写风」在空转");

    // ---- ④ 第三锚点：那七列由闸门照规则自己数一遍（判据 6） ----

    const anchorMark = markerSince(problems);
    const { counts, horas, decisions } = tallyFromPaifu(text);
    console.log(`那份牌谱里有 ${horas} 条 hora 事件、${decisions} 条决策记录`);
    if (horas === 0) problems.push("那份牌谱里一条 hora 事件都没有：和了 / 放铳那两列在空转");
    if (decisions === 0)
      problems.push("那份牌谱里一条决策记录都没有：兜底 / 重试 / tok 那几列在空转");

    let anchored = 0;
    for (const mine of expected.seats) {
      const own = counts.get(mine.seat);
      if (own === undefined) {
        problems.push(`闸门自己数那一份里没有座位 ${mine.seat}`);
        continue;
      }
      for (const column of ANCHORED) {
        anchored += 1;
        if (own[column] !== mine[column.replace("-", "_")]) {
          problems.push(
            `座位 ${mine.seat} 的 ${column}：闸门照规则数出 ${own[column]}，引擎算的是 ${mine[column.replace("-", "_")]}`,
          );
        }
      }
    }
    console.log(
      `${anchorMark()} 第三锚点核过 ${anchored} 格（${expected.seats.length} 行 × ${ANCHORED.length} 列）`,
    );
    if (anchored === 0) problems.push("第三锚点一格都没核上：那七列的对拍退回成了恒真式");

    // ---- ⑤ 回放那一屏「选手 · 档」：身份与名牌逐字相同，档位写「档位牌谱没记」 ----

    // **同一屏上不许两个说法**：名牌画的是 `start_game` 那一列 `names`，
    // 记分卡的身份格必须逐字相同——名牌写着 `deepseek/deepseek-v4-flash`、
    // 记分卡写着「牌谱没记」正是这一条要防的那一幕。
    const playerMark = markerSince(problems);
    console.log(`「选手 · 档」那一列：${shown.rows.map((row) => row.data.player).join(" / ")}`);

    const plates = await page.evaluate(() =>
      Object.fromEntries(
        [...document.querySelectorAll('[data-testid$="-player"]')]
          .filter((each) => /^seat-\d+-player$/.test(each.getAttribute("data-testid")))
          .map((each) => [
            each.getAttribute("data-testid").replace(/^seat-|-player$/g, ""),
            each.getAttribute("data-player"),
          ]),
      ),
    );
    console.log(
      `名牌那一句：${Object.entries(plates)
        .map(([seat, said]) => `座位 ${seat} ${said}`)
        .join(" / ")}`,
    );
    if (Object.keys(plates).length !== shown.rows.length) {
      problems.push(
        `牌桌上扇到 ${Object.keys(plates).length} 张名牌，而记分卡有 ${shown.rows.length} 行：对不上号就核不了「同一屏一个说法」`,
      );
    }

    let paired = 0;
    for (const row of shown.rows) {
      const seat = row.data.seat;
      paired += 1;

      if (row.data["player-source"] !== TIER_UNRECORDED) {
        problems.push(
          `座位 ${seat} 的 data-player-source 是「${row.data["player-source"]}」，回放那一屏该是「${TIER_UNRECORDED}」：` +
            "牌谱记得下身份、记不下档位",
        );
      }
      if (row.data["player-tier"] !== TIER_SAID) {
        problems.push(
          `座位 ${seat} 的档位那半格写着「${row.data["player-tier"]}」，回放那一屏该是「${TIER_SAID}」`,
        );
      }
      if (plates[seat] === undefined) {
        problems.push(`座位 ${seat} 的名牌不在 DOM 里：这一行的身份格没有右侧可比`);
        continue;
      }
      if (row.data["player-name"] !== plates[seat]) {
        problems.push(
          `座位 ${seat} 的记分卡身份格写着「${row.data["player-name"]}」，而名牌上写着「${plates[seat]}」：` +
            "同一屏上不许两个说法（两处画的都该是 start_game 那一列 names）",
        );
      }
      if (row.data["player-name"] === "") {
        problems.push(`座位 ${seat} 的身份格是空的：这份牌谱的 names 该是有的`);
      }
    }
    console.log(
      `${playerMark()} 四行的身份格与名牌逐字相同，档位那半段都是「${TIER_SAID}」（核过 ${paired} 行）`,
    );
    if (paired === 0) problems.push("一行都没核上：身份格与名牌的对拍在空转");

    // ---- ⑥ 复制记分卡：剪贴板里那段纯文本 ----

    const copyMark = markerSince(problems);
    await page.getByTestId("table-scorecard-copy").click();
    await page.getByTestId("table-scorecard-note").waitFor({ timeout: 15000 });
    const wire = await page.getByTestId("table-scorecard-note").getAttribute("data-scorecard-copy");
    if (wire !== "copied") {
      problems.push(`点了「复制记分卡」，下场却是「${wire}」`);
    }

    const copied = await page.evaluate(() => navigator.clipboard.readText());
    console.log(`剪贴板里 ${copied.length} 字符、${copied.split("\n").length} 行`);
    console.log(copied);

    // 那段文本与屏幕上那张表说的是同一件事。**逐列切开比，不是「这一行里含这个字串」**：
    // 「0」那种格在任何一行里都命中得了，那样的比法逮不住串列与错位。
    const split = (line) =>
      line
        .split("|")
        .map((cell) => cell.trim())
        .filter((cell) => cell !== "");

    let checked = 0;
    const lines = copied.split("\n");
    for (const row of shown.rows) {
      const seat = Number.parseInt(row.data.seat ?? "-1", 10);
      const line = lines.find((each) => each.startsWith(`| 座位 ${seat} `));
      if (line === undefined) {
        problems.push(`复制出来的那段文本里没有座位 ${seat} 那一行`);
        continue;
      }
      const written = split(line);
      if (written.length !== row.cells.length) {
        problems.push(
          `座位 ${seat} 那一行文本里有 ${written.length} 格，屏幕上那一行是 ${row.cells.length} 格`,
        );
        continue;
      }
      for (const [column, cell] of row.cells.entries()) {
        checked += 1;
        if (written[column] !== cell) {
          problems.push(
            `座位 ${seat} 第 ${column} 格：文本里是「${written[column]}」，屏幕上是「${cell}」`,
          );
        }
      }
    }
    console.log(`${copyMark()} 文本里逐格核过 ${checked} 格`);
    if (checked === 0) problems.push("文本里一格都没核上：这条断言在空转");

    // ---- ⑥b 复制出去那段文本的最左一列，同样一个风字都没有（票 145） ----

    // **两处同源，但两处都要核**：贴出去的是这一段文本，而它一旦贴出去就再也改不了。
    // 上面那条逐格对拍只保证「文本与屏幕一致」——两处一起写错时它一声不响。
    const textWindMark = markerSince(problems);
    let textCells = 0;
    for (const line of lines) {
      const written = split(line);
      // 抬头那一行（「janpo 记分卡」）与分隔那一行不是表格的格子。
      if (written.length !== shown.headers.length) continue;
      if (written[0] === "---") continue;
      textCells += 1;
      const winds = windsIn(written[0]);
      if (winds.length > 0) {
        problems.push(
          `复制出去那段文本最左一列的「${written[0]}」里有风字（${winds.join("")}）：` +
            "贴出去之后没人能纠正它",
        );
      }
    }
    console.log(`${textWindMark()} 复制出去那段文本最左一列核过 ${textCells} 格，一个风字都没有`);
    // 表头一行 + 四行 = 5 格。少了就说明**文本的列数与屏幕上那张表对不上**，
    // 上面那个 `continue` 会把整段跳空——**把真因说出来**，别只报一句「在空转」。
    if (textCells !== 1 + shown.rows.length) {
      problems.push(
        `文本最左一列只核到 ${textCells} 格，该是 ${1 + shown.rows.length} 格（表头 + ${shown.rows.length} 行）：` +
          `切出来的格数与屏幕上那张表的 ${shown.headers.length} 列对不上，整段被跳过了`,
      );
    }

    // ---- ⑦ 那段文本里绝不能出现 API key（票 34 的形状） ----

    const keyMark = markerSince(problems);
    const shareable = poison ? `${copied}\n${FAKE_KEY}` : copied;
    if (shareable.includes(FAKE_KEY)) {
      problems.push("记分卡文本里出现了 API key：灌进 localStorage 的那把假 key");
    }
    console.log(`${keyMark()} 记分卡那 ${shareable.length} 字符里搜不到那把 key`);
  } finally {
    await context.close();
  }

  if (problems.length > 0) return failure("记分卡验收没过：", problems);

  console.log("终局那一屏有一张四家可比的表，每一格与引擎算的相同，复制出去那段文本里没有 key ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  await runStandalone((lane) => verifyScorecard(lane, { poison: argv.includes("--poison") }));
}
