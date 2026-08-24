// **复盘：逐手对照标注**那一道闸门（票 90；spec 的 story 34）。
// 全程本机（页面 + 一份牌谱），**一个字节都不出网**，因此它进 CI。
//
// 第一程（真人坐座位 0、三家自带 bot，页面内驱动打完一整场东风战）：
//   ① **对局中一条标注都没有**（阴性对照，票面点名）：打到几十手时整页上
//      `table-review` 与 `table-review-hint` **一个都不在 DOM 里**——对局中给出
//      「换打会怎样」就是作弊（那属于 Assisted 档，票 89）；
//   ② 终局之后：面板在，主语是他（`data-review-seat=0`），**每一手都有一条**
//      ——条数与手序逐个等于**引擎另一条路走出来的那一份**（`ReviewCheck.expected`：
//      `Replay.traceOfPaifu` + `GameState.step`，而页面那一侧走的是 `Table.replay` 的帧）；
//   ③ **那几个数与引擎直接算的逐字相同**：向听（打之前 / 打之后 / 进退向）、
//      有效牌（枚数 / 种数）、危险度（档位 / 名次）——逐行逐项对拍；
//   ④ **更好的候选**：闸门照**规则**自己从引擎的逐张试打表里推一遍
//      （帕累托占优：向听不更差、有效牌不更少、危险度不更高，且至少一项更好），
//      页面列出来的那几张必须真的更好、数也对得上；说「这一手是当时的最优之一」的那几手，
//      引擎的试打表里必须真的一张更好的都没有；
//   ⑤ **点某一手**：牌桌摆出那一刻的快照（Live 那一侧没有时间轴），按「回到原处」回得来。
//
// 第二程（首页 `/` 那份 Demo 回放，**模型席也能看**）：
//   ⑥ 默认上帝视角：复盘**没有主语**——面板不在，只有那一句「坐到某一席就看得了」；
//   ⑦ 点一下座位 1 的视角：那一席的逐手复盘出来了，条数与那几个数同样与引擎对拍；
//   ⑧ **点某一手 → 游标跳过去 → 关掉回原处**（票 86 立的回程规矩）：轴只有票 75 那一根；
//   ⑨ **没按那一枚之前，强 AI 那一行一个都没有**（票 93）：`data-review-strong` 零个，
//      面板里也没有任何一行写着「暂无」或者「评分」。
//
// 第三程（票 93：**强 AI 的对照标注**，spec 的 story 36）：
//   ⑩ **懒加载**（ADR-0006 边界 1）：复盘面板摆在那儿、没人按那一枚时，
//      那份资产的网络请求计数为 **0**；按下去之后恰好 1 次（阳性对照）；
//   ⑪ **喂给它的必须是那一手当时喂给该席的那一份投影**（这一票的全部难点）：
//      闸门拿**另一条路**重建同一份投影（`ReviewCheck.asks`：`Replay.traceOfPaifu` +
//      `GameState.step`）自己去问同一份 wasm，逐手与页面上那一行对 id；
//   ⑫ **上帝视角会打 A、该席视角只能打 B**：闸门拿两份故意的上帝视角（同一席在
//      这一局最后一次出手时那一份流 / 一条不掩一张不隐的 `GodView.stream`）去问同一份 wasm，
//      构造出 A≠B 的那几手，逐手断言页面给的是 **B**；
//   ⑬ **同一手问两遍给同一答案**（可复现），分歧手数照**规则**再数一遍（判据 8）；
//      算不动时（CI 的常规趟：那 6 MB 不在场）**整行不出现**，而票 90 那几栏一条不少。
//
// 票 105 加的那一条**三程共用**（复盘画出来那一刻就量，判据 20）：
//   ⑧ **只看值得看的那几手，并在时间轴上标出它们**：默认筛选开着，这一列只摆
//      闸门**照规则自己数出来的**那几手（引擎的试打表里有帕累托占你那一张的换法，
//      或者强 AI 头一条的概率 ≥ 0.8 而你排到第三、或根本不在它那几条里）；
//      那一句「N 手里显示 M 手」里**两个数逐个溯源**（同票 107）；
//      时间轴上那几枚与这一列**逐手对齐**（手序与帧号），拨回「看全部」之后
//      条数回来而轴上一枚不少（票 90/93/103/107 那几条逐项对拍就在这一态下跑）；
//      上帝视角下一枚标记都没有（阴性对照）。**CI 里只量得到引擎那一半判据**：
//      强 AI 那一半要那份 6 MB 在场，因此只在本机演习那一档才开口
//      （那一半在 dotnet 那一侧另有执行者：`ReviewTests` 票 105 那几条）。
//
// 票 107 加的那一条**三程共用**（逐行对拍那一步里，因此两程各跑一遍）：
//   ⑯ **逐数溯源**：复盘画出来的那几句里的**每一个数**都指得回引擎那一份的一格
//      （手序 / 这一手本身 / 向听 / 有效牌枚数与种数 / 第几安全 / 换打那张与它多几枚，
//      加上面板抬头那一句的座位号与手数）——**多一个数就红**，不论它叫「总分」
//      「期望打点」还是行尾一个光秃秃的 `82`。旧那道逐项对拍是逐**字段**比，
//      它证明「这几个数没被改」而不证明「没有第五个数被加上去」
//      （报告 104 的红-7b：往每一行末尾拼一个「总分 82」，整套 `ci.sh` 全绿）。
//
// 票 103 在第三程上又加两条（**它有多确定**）：
//   ⑭ **候选分布两条路各自问一遍**：页面上那一行的 id 与概率，与闸门拿另一份重建的投影
//      问出来的逐条相同；上游那份分布的形状（降序、落在 [0,1]、**和 ≤ 1**、条数 ≤ 3）
//      逐手核一遍；**你打的那一手排第几**由闸门自己在上游那一列里找一遍（判据 8），
//      并断言一整场里真造得出「你打的是它第 2 候选」那种局面；
//   ⑮ **逐位对拍**：抽几手拿 `probe/akagi-wasm/probe.js` 那条 **node 路径**对同一份局面
//      再问一次（拿到的是 `probe_decide` 印出来的原文，中间没有 janpo 的任何一层）：
//      页面上那几个概率与它印出来的**严格相等**，且页面上那一串是最短往返表示（一位没舍）。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:review`
// 它也是 `verify-browser.mjs` 里的一趟（跑道上那几趟共用一个浏览器与一台服务器）。
//
// **CI 里第三程走的是「它用不了」那一路**（ADR-0006 边界 6：那 6 MB 不入版本控制）：
// 于是 ⑩ 的阳性对照量的是「请求真的发出去了」（回的是 404），而 ⑪⑫⑬⑭⑮ 量不到
// ——**真推理只在本机演习那一档**：先按 `web/public/baseline/README.md` 造一份产物放进去，
// 再跑这一趟（它自己探得到）。**CI 因此覆盖不到「它真出的那一手对不对」**，逐条写在报告 93 里。
//
// 选项：--budget ms（页面内驱动的时限）、--peek N（走多少手之后做①那一条）、
// --sample-every N（第⑮条每隔几手抽一手去 node 那侧重问）、
// --asset（本机演习：那份产物不在就当场报错，而不是静静地走降级那一路）。
//
// **强 AI 那一行上那几个数（概率 / 几条 / 排第几）不在这里溯源**：CI 里那一行根本画不出来
// （那份 6 MB 不入版本控制），写在这里等于一条永远执行不到的断言（判据 3）。
// 它的执行者在 dotnet 那一侧：`ReviewTests.强 AI 那一行同样一个自造的数都不出……`（票 107）。
//
// **把①按红的做法**（判据 1，票面点名）：把 `Review.settled` 那道判断去掉
// （例如 `let settled (_: TableModel) : bool = true`），重编 Fable 再跑这一趟。红的原文在报告 90 里。
// **把⑯按红的做法**：往 `Review.figures` 末尾拼一个 `总分 {82 + note.Turn % 17}`（报告 107 的红-1）。

import { readFileSync } from "node:fs";
import {
  decideText,
  feedLine,
  instantiate as instantiateProbe,
} from "../../probe/akagi-wasm/probe.js";
import { ASSET, assetPresent, PUBLIC_ASSET } from "./baseline-asset.mjs";
import { failure, isEntry, mark, markerSince, runStandalone, tick } from "./browser-lane.mjs";
import { plantSeating } from "./seating.mjs";
import { hostPage, retryOnReload } from "./serve.mjs";

/** 真人坐这一席（东 1 局的亲：页面一打开就轮到他）。 */
const ME = 0;

/** 第二程看的是这一席（**模型席也能看**：Demo 那份牌谱四席都是模型）。 */
const WATCHED = 1;

/**
 * 一个元素的 `data-*`，**没有就是 `null`**。
 *
 * 不用 `getByTestId(...).getAttribute(...)`：那一条在元素不存在时会**干等 30 秒再抛**，
 * 而这一道闸门的契约是交一份失败清单（合并跑那个入口要先关浏览器、再逐道汇报）——
 * 抛出去会把同一条跑道上其余那几趟一起搞挂（票 86/87/88 各写下过同一课）。
 */
function attr(page, testId, name) {
  return page.evaluate(
    ({ testId, name }) =>
      document.querySelector(`[data-testid="${testId}"]`)?.getAttribute(name) ?? null,
    { testId, name },
  );
}

/**
 * 点一枚按钮，**它不在就报一条失败而不是抛出去**。
 *
 * `getByTestId(...).click()` 在元素不存在时会**干等 30 秒再抛**，而这一道闸门的契约是
 * 交一份失败清单（合并跑那个入口要先关浏览器、再逐道汇报）——抛出去会把同一条跑道上其余那几趟一起搞挂。
 * **这一条是被破坏实验逃出来的**：把「点一条标注」改成 `CursorMoved` 那一次，
 * 「回到原处」那一枚根本没出现，于是这一趟抛了个 `TimeoutError`（票 86/87/88 各写下过同一课）。
 */
async function clicked(page, testId, problems, why) {
  if ((await page.getByTestId(testId).count()) === 0) {
    problems.push(`页面上没有 [data-testid="${testId}"]：${why}`);
    return false;
  }
  await page.getByTestId(testId).click();
  return true;
}

/** 页面上那几行复盘标注，连同它们身上的每一个数。 */
function panel(page) {
  return page.evaluate(() => {
    const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
    const section = at("table-review");
    if (section === null) {
      return { present: false, hint: at("table-review-hint") !== null };
    }

    const num = (node, name) => {
      const raw = node.getAttribute(name);
      return raw === null || raw === "" ? null : Number.parseInt(raw, 10);
    };

    const said = (testId) => at(testId)?.textContent?.trim() ?? "";

    const notes = [...section.querySelectorAll("[data-review-turn]")].map((row) => {
      const advice = row.querySelector("[data-review-advice]");
      const strong = row.querySelector("[data-review-strong]");
      const said = (selector) => row.querySelector(selector)?.textContent?.trim() ?? "";
      const list = (name) => {
        const raw = advice?.getAttribute(name) ?? "";
        return raw === "" ? [] : raw.split(" ");
      };
      const split = (node, name) => {
        const raw = node?.getAttribute(name) ?? "";
        return raw === "" ? [] : raw.split(" ");
      };

      return {
        turn: num(row, "data-review-turn"),
        frame: num(row, "data-review-frame"),
        kind: row.getAttribute("data-review-kind"),
        // 这一手为什么值得看（票 105）：better / strong / both / 空串。
        worth: row.getAttribute("data-review-worth"),
        shanten: num(row, "data-review-shanten"),
        after: num(row, "data-review-shanten-after"),
        delta: num(row, "data-review-delta"),
        ukeire: num(row, "data-review-ukeire"),
        kinds: num(row, "data-review-ukeire-kinds"),
        danger: row.getAttribute("data-review-danger"),
        rank: num(row, "data-review-danger-rank"),
        open: row.getAttribute("data-review-open"),
        advice: advice === null ? null : advice.getAttribute("data-review-advice"),
        candidates: list("data-review-candidates"),
        gains: list("data-review-gains").map((each) => Number.parseInt(each, 10)),
        headline: said(".review-jump"),
        figures: said(".review-figures"),
        adviceText: advice?.textContent?.trim() ?? "",
        // 强 AI 那一行（票 93）：**没问过 / 算不动时这一整行根本不存在**，因此是 null 而不是空串。
        strong: strong === null ? null : strong.getAttribute("data-review-strong"),
        strongId: strong === null ? null : num(strong, "data-review-strong-id"),
        strongDiff: strong === null ? null : strong.getAttribute("data-review-strong-diff"),
        strongText: strong?.textContent?.trim() ?? "",
        // 候选分布（票 103）。**`ps` 取的是那一串字符本身**：闸门要拿它与 wasm
        // 直接印出来的那一串对，先 `Number.parseFloat` 一遍就把「页面上到底写了什么」扔了。
        strongIds: split(strong, "data-review-strong-ids").map((each) => Number.parseInt(each, 10)),
        strongPs: split(strong, "data-review-strong-ps"),
        strongKeys: split(strong, "data-review-strong-keys"),
        strongTotal: strong === null ? null : num(strong, "data-review-strong-total"),
        strongRank: strong === null ? null : num(strong, "data-review-strong-rank"),
        strongYoursP:
          strong === null ? null : (strong.getAttribute("data-review-strong-yours-p") ?? ""),
      };
    });

    const head = at("review-strong");

    return {
      present: true,
      hint: at("table-review-hint") !== null,
      seat: num(section, "data-review-seat"),
      said: num(section, "data-review-notes"),
      // 只看值得看的那几手（票 105）：拨在哪边、值得看的有几手、那一句说了什么。
      // **`kept` 与画出来的行数是两件事**：拨回「看全部」只改后者。
      filter: section.getAttribute("data-review-filter"),
      kept: num(section, "data-review-kept"),
      filterLine: said("review-filter"),
      filterShown: num(at("review-filter"), "data-review-shown"),
      toggle: at("review-filter-toggle")?.textContent?.trim() ?? "",
      open: section.getAttribute("data-review-open"),
      strong: section.querySelectorAll("[data-review-strong]").length,
      strongState: head === null ? null : head.getAttribute("data-review-strong-state"),
      strongRows: head === null ? null : num(head, "data-review-strong-rows"),
      strongDiffs: head === null ? null : num(head, "data-review-strong-diffs"),
      strongMs: head === null ? null : num(head, "data-review-strong-ms"),
      strongText: head?.textContent?.trim() ?? "",
      // 抬头那一句与那一段说明（票 107 逐数溯源要量它们）。
      title: said("review-at"),
      intro: said("review-intro"),
      text: section.textContent ?? "",
      notes,
    };
  });
}

/**
 * 时间轴上那几枚复盘标记（票 105）：哪几手、各钉在第几帧。
 *
 * **读的是 `data-*` 而不是像素**：一枚标记在屏幕上实宽两个像素，拿坐标去对齐的闸门
 * 只会量到浏览器的滑块头有多宽；这一条要铉的是「标的是哪几手」。
 */
function timelineMarks(page) {
  return page.evaluate(() => {
    const rail = document.querySelector('[data-testid="table-timeline-marks"]');
    if (rail === null) return { present: false, said: null, marks: [] };
    return {
      present: true,
      said: Number.parseInt(rail.getAttribute("data-marks") ?? "", 10),
      marks: [...rail.querySelectorAll("[data-timeline-mark]")].map((each) => ({
        turn: Number.parseInt(each.getAttribute("data-timeline-mark"), 10),
        frame: Number.parseInt(each.getAttribute("data-timeline-mark-frame"), 10),
      })),
    };
  });
}

/**
 * 一句话里的**每一个数**（含小数与正负号）。
 *
 * **它认的是「数」不是「词」**：措辞怎么改都不动它，而凭空多印一个数
 * ——不论它叫「总分」「期望打点」还是根本没有名字（行尾一个光秃秃的 `82`）——就多出一项。
 */
function numerals(said) {
  return said.match(/[+-]?\d+(?:\.\d+)?/g) ?? [];
}

/** 一个向听数印出来那一句里的数：**听牌与和了一个数都没有**。 */
function shantenNumerals(value) {
  return value >= 1 ? [String(value)] : [];
}

/** 一张牌印出来那一句里的数（`8p` → 「8筒」有一个 8；字牌是「东」，一个数都没有）。 */
function tileNumerals(mjai) {
  const found = /^([1-9])[mps]/.exec(mjai);
  return found === null ? [] : [found[1]];
}

/**
 * 第二句上那几个数的来源：**逐格都是引擎那一份脚手架**（`ReviewCheck.expected`）。
 *
 * 它按的是渲染那一头的**分支**（形态读不出来一个数都不给 / 这一手没打牌只有向听 /
 * 打了牌那三样），**不是拿页面上那一句反推**——拿被检查那句话当期望值等于用同一份数据证明它自己（判据 8）。
 */
function figureSources(each) {
  if (each.shanten === null) return [];
  const before = ["打之前的向听（Scaffold.Shanten）", shantenNumerals(each.shanten)];
  if (each.after === null) return [before];

  const sources = [before, ["打之后的向听（DahaiScaffold.Shanten）", shantenNumerals(each.after)]];

  if (each.ukeire !== null) {
    sources.push(["有效牌枚数（Ukeire.total）", [String(each.ukeire)]]);
    sources.push(["有效牌种数（Ukeire.kindCount）", [String(each.kinds)]]);
  }
  if (each.danger !== null) {
    sources.push(["这一手第几安全（Danger.Rank）", [String(each.rank)]]);
  }

  return sources;
}

/**
 * 第三句上那几个数的来源：**每一条候选逐格都是引擎那一条试打**。
 *
 * 列的是哪几张上面那一段已经照规则核过（帕累托占优）；这里只问**它们各自印出了哪几个数**。
 * 试打表里找不着那一张时回 `null`：那一条上面已经报过一句，这里不再报第二遍。
 */
function adviceSources(note, each, yours) {
  if (note.advice === null) return [];
  const sources = [];

  for (const pai of note.candidates) {
    const trial = each.trials.find((trial) => trial.pai === pai);
    if (trial === undefined || yours === undefined) return null;

    sources.push(["换打的那一张（DahaiScaffold.Pai）", tileNumerals(pai)]);
    if (trial.ukeire !== null) {
      sources.push(["那一张的有效牌枚数（Ukeire.total）", [String(trial.ukeire)]]);
      // **多出来的枚数才写那个号**：一边算不出来、或者两边一样多时那一格根本不印。
      if (yours.ukeire !== null && trial.ukeire !== yours.ukeire) {
        const gain = trial.ukeire - yours.ukeire;
        sources.push(["比你打的那张多几枚（UkeireGain）", [gain > 0 ? `+${gain}` : String(gain)]]);
      }
    }
    if (trial.delta < yours.delta) {
      sources.push([
        "那一张打完之后的向听（DahaiScaffold.Shanten）",
        shantenNumerals(trial.shanten),
      ]);
    }
  }

  return sources;
}

/**
 * **逐数溯源**（票 107）：这一行画出来的每一个数，逐个指得回引擎那一份的一格。
 *
 * 旧那道逐项对拍是**逐字段**比（向听 / 进退向 / 有效牌 / 危险度各一条），它证明「这几个数没被改」，
 * **不证明「没有第五个数被凭空加上去」**——票 104 往每一行末尾拼了一个「总分 82」，
 * 整套 `ci.sh` 全绿（报告 104 的红-7b）。这一条换一个问法：**这个数是谁的**。
 *
 * 多一个数、少一个数、哪一格印错了值，都在这里当场红；**它不问那个数叫什么**。
 */
function traced(at, note, each) {
  const problems = [];
  const yours = each.trials.find((trial) => trial.pai === each.pai);

  const lines = [
    [
      "抬头",
      note.headline,
      [
        ["这一手的手序（Table.Turns）", [String(each.turn)]],
        ["这一手本身（Action.toDisplay）", numerals(each.label)],
      ],
    ],
    ["第二句", note.figures, figureSources(each)],
    ["第三句", note.adviceText, adviceSources(note, each, yours)],
  ];

  let numbers = 0;

  for (const [which, said, sources] of lines) {
    if (sources === null) continue;
    const expected = sources.flatMap(([, digits]) => digits);
    const printed = numerals(said);

    if (printed.join(" ") !== expected.join(" ")) {
      const table = sources
        .map(
          ([where, digits]) =>
            `${where} = ${digits.length === 0 ? "（没有数）" : digits.join("、")}`,
        )
        .join("；");
      problems.push(
        `${at}的${which}「${said}」印出来的数是 [${printed.join("，")}]，` +
          `而指得回引擎那一份的只有 [${expected.join("，")}]（${table}）`,
      );
      continue;
    }

    numbers += printed.length;
  }

  return { problems, numbers };
}

/**
 * 引擎那一份（**另一条路**）：把一份牌谱原文喂给 `ReviewCheck.expected`。
 *
 * 它走 `Replay.traceOfPaifu` + `GameState.step`，与页面那一侧的 `Table.replay` 各走各的
 * ——两条路各自到达同一手，再各自向同一个引擎问那份脚手架（判据 6：右侧不许是同一个实现）。
 */
function expectedFrom(page, text, seat) {
  return retryOnReload(() =>
    page.evaluate(
      async ({ text, seat }) => {
        // 相对页面地址 import：vite 的 base 可配（JANPO_BASE），写死 "/src/…" 一改 base 就 404。
        const check = await import("./src/generated/ReviewCheck.js");
        return JSON.parse(check.expected(text, seat));
      },
      { text, seat },
    ),
  );
}

/**
 * **这一张比你打的那张更好吗**——照规则写的那一份（判据 8：期望值取自规则，
 * 不取自被检查那句话的来源）。三项：向听不更差、有效牌不更少、危险度不更高，
 * 且至少一项严格更好；**算不出来的那一项不参与比较**（拿 null 当 0 就是编一个数）。
 */
function dominates(played, candidate) {
  const both = (left, right) => left !== null && right !== null;
  const notWorse =
    candidate.delta <= played.delta &&
    (!both(candidate.ukeire, played.ukeire) || candidate.ukeire >= played.ukeire) &&
    (!both(candidate.order, played.order) || candidate.order <= played.order);
  const better =
    candidate.delta < played.delta ||
    (both(candidate.ukeire, played.ukeire) && candidate.ukeire > played.ukeire) ||
    (both(candidate.order, played.order) && candidate.order < played.order);
  return notWorse && better;
}

/**
 * 「它很确定而你打的排在后面」那一条里的两个常数（票 105）。
 *
 * **它们是 `Review.telling` / `Review.trailing` 的另一份，故意的**（判据 6：
 * 右侧不许是同一个实现）：这一侧照**规则**自己数一遍，于是一旦有人只改了 F# 那一处的
 * 阈值，这一道当场红——那正是一道闸门该做的事。这两个数怎么来的写在报告 105 §1。
 */
const TELLING = 0.8;
const TRAILING = 3;

/**
 * 照**规则**自己数一遍「值得看的那几手」（票 105）。
 *
 * 两条，或的关系：① 引擎的试打表里真有一张帕累托占优你打的那一张（`dominates`）；
 * ② 强 AI 那一行里它头一条的概率 ≥ `TELLING`，而你打的排到第 `TRAILING` 或根本不在里面。
 *
 * `strong` 是**闸门自己问出来的那一叠**（`asked()` 的 `notes`，另一条路）；
 * CI 里那份 6 MB 不在场，传 `null`，于是只剩第①条——与页面那一侧那一刻的处境一样。
 */
function worthwhile(expected, strong) {
  const better = new Set();
  const notable = new Set();

  for (const each of expected.notes) {
    if (each.kind === "dahai") {
      const yours = each.trials.find((trial) => trial.pai === each.pai);
      if (yours !== undefined && each.trials.some((trial) => dominates(yours, trial))) {
        better.add(each.turn);
      }
    }
  }

  for (const each of strong ?? []) {
    const ps = each.candidates.map((choice) => choice.p);
    if (ps.length === 0) continue;
    const at = each.candidates.findIndex((choice) => choice.action_id === each.playedId);
    if (ps[0] >= TELLING && (at === -1 || at + 1 >= TRAILING)) notable.add(each.turn);
  }

  const turns = expected.notes
    .map((each) => each.turn)
    .filter((turn) => better.has(turn) || notable.has(turn));

  return { better, notable, turns, kept: new Set(turns) };
}

/**
 * **只看值得看的那几手**（票 105 的⑧）：这一列摆的那几条、那一句里的两个数、
 * 以及时间轴上那几枚标记，**逐手**与闸门照规则数出来的那一套对。
 *
 * `marks` 传 `null` 表示这一程根本没有时间轴（Live 那一页，票 75）。
 */
function focused(where, shown, marks, expected, rule) {
  const problems = [];
  const drawn = shown.notes.map((note) => note.turn);
  const on = shown.filter === "on";
  const total = expected.notes.length;

  if (shown.kept !== rule.kept.size) {
    problems.push(
      `${where}：面板说值得看的有 ${shown.kept} 手，照规则数出来的是 ${rule.kept.size} 手` +
        `（引擎那一半 ${rule.better.size} 手、强 AI 那一半 ${rule.notable.size} 手）`,
    );
  }

  const want = on ? rule.turns : expected.notes.map((each) => each.turn);

  if (drawn.join(" ") !== want.join(" ")) {
    const extra = drawn.filter((turn) => !want.includes(turn));
    const missing = want.filter((turn) => !drawn.includes(turn));
    problems.push(
      `${where}（筛选${on ? "开着" : "关掉"}）：这一列画了 ${drawn.length} 条，该是 ${want.length} 条` +
        `（多出来的：${extra.slice(0, 5).join("、") || "无"}；漏掉的：${missing.slice(0, 5).join("、") || "无"}）`,
    );
  }
  // **逐手核「是哪一条判据点亮了它」**：只比并集的话，一条判据被改坠而另一条
  // 刚好盖住同几手时，闸门会一声不响（实测：阈值 0.8 改 0.7，并集一数不变）。
  for (const note of shown.notes) {
    const wanted = rule.better.has(note.turn)
      ? rule.notable.has(note.turn)
        ? "both"
        : "better"
      : rule.notable.has(note.turn)
        ? "strong"
        : "";
    if (note.worth !== wanted) {
      problems.push(
        `${where} 第 ${note.turn} 手：页面说它值得看是因为「${note.worth || "（都不占）"}」，` +
          `照规则该是「${wanted || "（都不占）"}」`,
      );
    }
  }

  if (shown.filterShown !== drawn.length) {
    problems.push(
      `${where}：那一句说这一列摆了 ${shown.filterShown} 条，实际画了 ${drawn.length} 条`,
    );
  }

  // ⑥ 同一条判据（票 107）：那一句里**只该有两个数**——这一席落定了几手、
  // 其中值得看的有几手。阈值那个数不在页面上，因此这张表里也没有它的位置。
  const printed = numerals(shown.filterLine);
  const sources = [
    ["这一席落定了几手", [String(total)]],
    ["值得看的有几手（照规则数出来的）", [String(rule.kept.size)]],
  ];
  const wanted = sources.flatMap(([, digits]) => digits);

  if (printed.join(" ") !== wanted.join(" ")) {
    problems.push(
      `${where}：筛选那一句「${shown.filterLine}」印出来的数是 [${printed.join("，")}]，` +
        `而指得回去的只有 [${wanted.join("，")}]（${sources.map(([at, digits]) => `${at} = ${digits.join("")}`).join("；")}）`,
    );
  }
  if (shown.toggle === "" || /\d/.test(shown.toggle)) {
    problems.push(`${where}：那一枚开关上写的是「${shown.toggle}」（它该在，而且不该再摆一个数）`);
  }

  // 时间轴上那几枚：**与值得看的那几手逐手对齐**（手序与帧号两样都对），
  // **而且不跟着那一枚开关变**：拨回「看全部」只是多摆几条，值得看的仍旧是那几手。
  if (marks !== null) {
    if (!marks.present) {
      problems.push(`${where}：时间轴上根本没有那一条标记带（票 105 要把值得看的那几手标在轴上）`);
    } else {
      if (marks.said !== marks.marks.length) {
        problems.push(`${where}：轴上说有 ${marks.said} 枚标记，实际画了 ${marks.marks.length} 枚`);
      }
      if (marks.marks.map((mark) => mark.turn).join(" ") !== rule.turns.join(" ")) {
        problems.push(
          `${where}：轴上标的是第 [${marks.marks.map((mark) => mark.turn).join("，")}] 手，` +
            `照规则值得看的是第 [${rule.turns.join("，")}] 手——两处标的不是同一批手`,
        );
      }
      // 一枚标记钉在第几帧：与复盘那一条自己说的那一帧逐枚相同。
      // **帧号本身真不真另有人铉**：第⑧条点开一条标注时拿游标量过（而那一条正是
      // 从这几枚里挑的），因此这一句量的是「两处指的是同一帧」。
      for (const mark of marks.marks) {
        const note = shown.notes.find((note) => note.turn === mark.turn);
        if (note !== undefined && mark.frame !== note.frame) {
          problems.push(
            `${where}：第 ${mark.turn} 手那一枚标在第 ${mark.frame} 帧，而复盘那一条说它落定在第 ${note.frame} 帧`,
          );
        }
      }
    }
  }

  return problems;
}

/**
 * 页面那几行与引擎那一份逐项对拍。返回失败清单（空 = 绿）与执行次数。
 *
 * **两件事一起量**（判据 3）：对拍本身，以及「这几条断言各开口了几次」——
 * 一条永远执行不到的断言与一条从不失败的断言，危害相同。
 */
function compare(where, shown, expected) {
  const problems = [];
  const counts = {
    rows: 0,
    ukeire: 0,
    danger: 0,
    better: 0,
    best: 0,
    gain: 0,
    numbers: 0,
    untraced: 0,
  };

  if (shown.notes.length !== expected.notes.length) {
    problems.push(
      `${where}：页面上有 ${shown.notes.length} 条标注，而引擎说这一席落定了 ${expected.notes.length} 手` +
        "：每一手都该有一条（票 90 的第一条验收）",
    );
    return { problems, counts };
  }
  if (shown.said !== expected.notes.length) {
    problems.push(`${where}：面板说有 ${shown.said} 条，实际画了 ${shown.notes.length} 条`);
  }

  // ⑯ **逐数溯源**（票 107）先从面板自己那两句起：抬头上只得有座位号与手数，
  // 而那一段说明是一句定话：**一个数都不该有**（在这儿印一个「平均 82」同样是造分）。
  const titleNumbers = numerals(shown.title).join(" ");
  const titleSources = [String(expected.seat), String(expected.notes.length)].join(" ");

  if (titleNumbers !== titleSources) {
    problems.push(
      `${where}：面板抬头「${shown.title}」印出来的数是 [${titleNumbers}]，` +
        `而指得回引擎那一份的只有 [${titleSources}]（复盘对着哪一席、这一席落定了几手）`,
    );
  }
  if (numerals(shown.intro).length !== 0) {
    problems.push(
      `${where}：复盘那一段说明里出现了数（${numerals(shown.intro).join("，")}）：` +
        "它是一句定话，每一个数都得指得回引擎那一份的一格（票 107）",
    );
  }

  for (const [index, note] of shown.notes.entries()) {
    const each = expected.notes[index];
    const at = `${where} 第 ${each.turn} 手（${each.label}）`;

    if (note.turn !== each.turn) {
      problems.push(`${at}：页面上那一条写着第 ${note.turn} 手——手序对不上，两边说的不是同一手`);
      continue;
    }
    if (note.kind !== each.kind) {
      problems.push(`${at}：页面说这一手是 ${note.kind}，引擎说是 ${each.kind}`);
    }

    counts.rows += 1;

    const same = (name, mine, theirs) => {
      if (mine !== theirs) {
        problems.push(`${at}：${name} 页面写 ${mine}，引擎算的是 ${theirs}`);
      }
    };

    same("打之前的向听", note.shanten, each.shanten);
    same("打之后的向听", note.after, each.after);
    same("进退向", note.delta, each.delta);
    same("有效牌枚数", note.ukeire, each.ukeire);
    same("有效牌种数", note.kinds, each.kinds);
    same("危险度档位", note.danger === "" ? null : note.danger, each.danger);
    same("危险度名次", note.rank, each.rank);

    if (each.ukeire !== null) counts.ukeire += 1;
    if (each.danger !== null) counts.danger += 1;

    // ⑯ 这一行画出来的每一个数，逐个指回引擎那一份的一格（票 107）。
    const tracedRow = traced(at, note, each);
    problems.push(...tracedRow.problems);
    counts.numbers += tracedRow.numbers;
    counts.untraced += tracedRow.problems.length;

    // 「更好的候选」那一栏：照规则自己推一遍。
    if (each.kind !== "dahai") {
      if (note.advice !== null) {
        problems.push(`${at}：这一手没打牌，却摆出了「换打会怎样」那一栏`);
      }
      continue;
    }

    const yours = each.trials.find((trial) => trial.pai === each.pai);

    if (yours === undefined) {
      problems.push(`${at}：引擎的试打表里找不到他打的那一张`);
      continue;
    }

    const rule = each.trials.filter((trial) => dominates(yours, trial));

    if (rule.length === 0) {
      counts.best += 1;
      if (note.advice !== "best") {
        problems.push(
          `${at}：引擎的试打表里一张更好的都没有，页面却列了 ${note.candidates.join("、")}`,
        );
      }
      if (!note.adviceText.includes("最优之一")) {
        problems.push(`${at}：没有更好的就该明说，页面上那一栏写的是「${note.adviceText}」`);
      }
      continue;
    }

    counts.better += 1;
    if (note.advice !== "better") {
      problems.push(
        `${at}：引擎说还有 ${rule.length} 张更好的（${rule.map((trial) => trial.pai).join("、")}），` +
          `页面那一栏却写着「${note.adviceText}」`,
      );
      continue;
    }
    if (note.candidates.length !== Math.min(3, rule.length)) {
      problems.push(
        `${at}：更好的有 ${rule.length} 张，页面列了 ${note.candidates.length} 条（该是 ${Math.min(3, rule.length)} 条）`,
      );
    }

    const best = Math.max(...rule.map((trial) => trial.ukeire ?? 0));

    for (const [rank, pai] of note.candidates.entries()) {
      const trial = each.trials.find((trial) => trial.pai === pai);
      if (trial === undefined) {
        problems.push(`${at}：页面列的「${pai}」根本不在引擎的试打表里`);
        continue;
      }
      if (!dominates(yours, trial)) {
        problems.push(
          `${at}：页面把「${pai}」当成更好的，可它在引擎的数上并不更好` +
            `（向听 ${trial.delta} vs ${yours.delta}、有效牌 ${trial.ukeire} vs ${yours.ukeire}、危险度 ${trial.order} vs ${yours.order}）`,
        );
      }
      const gain = note.gains[rank];
      if (trial.ukeire !== null && yours.ukeire !== null && gain !== trial.ukeire - yours.ukeire) {
        problems.push(
          `${at}：「${pai}」页面说多 ${gain} 枚，引擎算的是 ${trial.ukeire - yours.ukeire} 枚`,
        );
      }
      if (rank === 0) {
        if ((trial.ukeire ?? 0) !== best) {
          problems.push(
            `${at}：头一条该是有效牌最多的那张（${best} 枚），页面摆的是「${pai}」（${trial.ukeire} 枚）`,
          );
        }
        if (gain >= 10) counts.gain += 1;
      }
    }
  }

  return { problems, counts };
}

/**
 * **页面内**把这一桌往前推（票 56 那条教训：每手一次 playwright 往返太贵）。
 *
 * 代点极简：有「过」就过，有牌可打就点手牌行里的**头一张**，否则点那一排头一枚，
 * 都没有就按「单步」（一局打完就点「下一局」）。**它故意不讲牌理**——正因为如此，
 * 一整场下来会有几十手「本来还有更好的」，第④条那几个数才有东西可核。
 *
 * **这一手落没落下去看的是一个签名**（票 88 那一课）：连着两次「过」时
 * 「上一手」那句话一个字不变，因此把真人那一行的几个计数一起算进去。
 */
function drive(page, { limit, budgetMs }) {
  return page.evaluate(
    async ({ limit, budgetMs }) => {
      const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
      const all = (selector) => [...document.querySelectorAll(selector)];
      const line = () => at("table-human");
      const signature = () =>
        [
          at("table-latest")?.textContent?.trim() ?? "",
          line()?.getAttribute("data-human") ?? "",
          line()?.getAttribute("data-human-passes") ?? "",
          all("[data-dahai-id]").length,
          all("[data-human-action-id]").length,
        ].join("|");
      const breathe = (attempt) =>
        attempt < 8
          ? Promise.resolve()
          : new Promise((done) => setTimeout(done, attempt < 64 ? 0 : 8));

      const changed = async (before) => {
        const deadline = performance.now() + budgetMs;
        let attempt = 0;
        while (signature() === before) {
          if (performance.now() > deadline) return false;
          await breathe(attempt);
          attempt += 1;
        }
        return true;
      };

      let steps = 0;
      let stuck = null;

      for (let move = 0; move < limit; move += 1) {
        if (at("table-result") !== null) break;

        const before = signature();
        const buttons = all("[data-human-action-id]");
        const pass = buttons.find((node) => node.dataset.humanAction === "none");
        const tiles = all("[data-dahai-id]");
        const step = at("table-step");
        const target = pass ?? tiles[0] ?? buttons[0] ?? step;

        if (target === null || target === undefined || target.disabled) {
          // 这一局打完了：接着开下一局（终局那一刻「下一局」也是灰的，上面那一句先跳出去）。
          const next = at("table-next");
          if (next === null || next.disabled) {
            stuck = "既点不了牌、也开不了下一局";
            break;
          }
          next.click();
        } else {
          target.click();
        }

        steps += 1;
        if (!(await changed(before))) {
          stuck = `第 ${steps} 步点下去之后牌桌没走动`;
          break;
        }
      }

      return { steps, stuck, ended: at("table-result") !== null };
    },
    { limit, budgetMs },
  );
}

/** 第一程：真人坐一席，打完一整场，终局之后才有复盘。 */
async function humanLeg(lane, { budgetMs, peek }) {
  const problems = [];
  const origin = await lane.devUrl();
  const context = await lane.newContext({ acceptDownloads: true });
  const page = await context.newPage();

  try {
    await plantSeating(page, { profiles: [], seats: [{ choice: "human" }, {}, {}, {}] });
    await page.goto(hostPage(origin), { waitUntil: "domcontentloaded" });
    await page.getByTestId("table-board").waitFor({ timeout: 15000 });

    // ① 对局中：先走几十步，再看整页上有没有复盘（**阴性对照**）。
    const midway = await drive(page, { limit: peek, budgetMs });
    const latest = (await page.getByTestId("table-latest").textContent()).trim();
    console.log(`对局中：走了 ${midway.steps} 步，${latest}`);

    if (midway.steps < peek || midway.ended) {
      problems.push(
        `对局中那一条什么都没证明：只走了 ${midway.steps} 步（卡在：${midway.stuck ?? "没卡"}）` +
          `${midway.ended ? "，而且已经终局了" : ""}`,
      );
    }
    if (latest === "") {
      problems.push("对局中那一刻牌桌上一手都没落定：阴性对照量的不能是一桌没开起来的空局");
    }

    const during = await panel(page);
    if (during.present) {
      problems.push(
        `对局还没打完，复盘面板就在 DOM 里了（${during.notes.length} 条标注）：` +
          "对局中给出「换打会怎样」就是作弊——那是 Assisted 档的事（票 89），复盘只在终局之后",
      );
    }
    if (during.hint) {
      problems.push("对局还没打完，页面上却已经在说「坐到某一席就看得到复盘」");
    }
    if (!during.present && !during.hint) {
      console.log("对局中：复盘那一块整个不在 DOM 里 ✓（阴性对照）");
    }

    // ② 打完一整场。
    const rest = await drive(page, { limit: 4000, budgetMs });
    if (!rest.ended) {
      problems.push(`这一场没走到终局（又走了 ${rest.steps} 步，卡在：${rest.stuck ?? "没卡"}）`);
      return problems;
    }
    console.log(`终局：又走了 ${rest.steps} 步`);

    const shown = await panel(page);
    if (!shown.present) {
      problems.push("这一场打完了，复盘面板却不在：终局之后立刻该有东西看（票 90 的零外部依赖）");
      return problems;
    }
    if (shown.seat !== ME) {
      problems.push(`复盘的主语该是真人那一席（座位 ${ME}），页面写的是座位 ${shown.seat}`);
    }

    // ③④ 与引擎另一条路算出来的逐项对拍。牌谱走「导出」那条真路（与票 26 同一份字节）。
    const [download] = await Promise.all([
      page.waitForEvent("download", { timeout: 30000 }),
      page.getByTestId("table-export").click(),
    ]);
    const text = readFileSync(await download.path(), "utf8");
    const expected = await expectedFrom(page, text, ME);

    if (expected.error) {
      problems.push(`引擎读不动这一桌导出的牌谱：${expected.error}`);
      return problems;
    }

    // ⑧ **只看值得看的那几手**（票 105）：默认就是筛选开着的，于是上面那一叠 `shown`
    // 量到的就是筛选后的那一列。Live 那一页没有时间轴（票 75），因此不量标记。
    //
    // **那一句的钩先取**（`markerSince`，票 106）：在这一项开始之前记下清单有多长，
    // 否则印那一句时现取就是一个恒真式。
    const focusMark = markerSince(problems);
    const rule = worthwhile(expected, null);
    problems.push(...focused("真人那一桌", shown, null, expected, rule));

    if (shown.filter !== "on") {
      problems.push(`复盘默认该只摆值得看的那几手，面板说的是「${shown.filter}」`);
    }
    if (rule.kept.size === 0 || rule.kept.size >= expected.notes.length) {
      problems.push(
        `真人那一桌：一整场 ${expected.notes.length} 手里值得看的数出 ${rule.kept.size} 手：` +
          "筛选那一条这一趟等于没跑（判据 3）",
      );
    }

    console.log(
      `真人那一桌：只看值得看的那几手 ${focusMark()}` +
        `（${expected.notes.length} 手里显示 ${shown.notes.length} 手；那一句：「${shown.filterLine}」）`,
    );

    // 拨回「看全部」：票 90/93/103/107 那几条逐项对拍在这一态下跑（**一条不少**）。
    if (
      !(await clicked(
        page,
        "review-filter-toggle",
        problems,
        "筛掉了就要有一枚拨得回去的开关（票 105）",
      ))
    ) {
      return problems;
    }

    const whole = await panel(page);
    problems.push(...focused("真人那一桌（看全部）", whole, null, expected, rule));

    const { problems: mismatches, counts } = compare("真人那一桌", whole, expected);
    problems.push(...mismatches);
    console.log(
      `真人那一桌：${counts.rows} 条逐项对拍 ${tick(mismatches.length === 0)}` +
        `（有效牌 ${counts.ukeire} 条、危险度 ${counts.danger} 条、` +
        `更好的候选 ${counts.better} 条、最优之一 ${counts.best} 条、差 10 枚以上 ${counts.gain} 条）`,
    );
    console.log(
      `真人那一桌：画出来的 ${counts.numbers} 个数逐个指得回引擎那一份的一格 ` +
        `${tick(counts.untraced === 0)}（一个自造的合成数都不出，票 107）`,
    );

    if (counts.numbers === 0) {
      problems.push("一整场下来一个数都没溯过源：逐数溯源那一条这一趟等于没跑（判据 3）");
    }
    if (counts.better === 0) {
      problems.push("一整场下来一条「更好的候选」都没出现：那一栏的断言这一趟等于没跑（判据 3）");
    }
    if (counts.best === 0) {
      problems.push("一整场下来一条「这一手是当时的最优之一」都没有：那一句的断言这一趟等于没跑");
    }
    if (counts.danger === 0) {
      problems.push("一整场下来没有一条标注带危险度：这一桌没人立直也没人副露？");
    }

    // ⑤ 点某一手：牌桌摆出那一刻的快照，按「回到原处」回得来（Live 侧没有时间轴）。
    // 拿的是拨回「看全部」之后那一叠（`whole`）：此刻 DOM 里摆着的就是它。
    const jump = whole.notes.find((note) => note.kind === "dahai");
    const before = (await page.getByTestId("table-latest").textContent()).trim();
    const why = "每一条标注都该点得开（票 90 的第三条验收）";

    if (await clicked(page, `review-turn-${jump.turn}`, problems, why)) {
      const snapshot = (await page.getByTestId("table-latest").textContent()).trim();
      const opened = await attr(page, "table-review", "data-review-open");

      if (snapshot === before) {
        problems.push(`点了第 ${jump.turn} 手，牌桌却纹丝不动：「${snapshot}」`);
      }
      if (opened !== String(jump.turn)) {
        problems.push(`点开的该是第 ${jump.turn} 手，面板说的是「${opened}」`);
      }

      if (
        await clicked(page, "review-return", problems, "跳走了就要回得来（票 86 立的回程规矩）")
      ) {
        const back = (await page.getByTestId("table-latest").textContent()).trim();
        if (back !== before) {
          problems.push(
            `按「回到原处」之后牌桌停在「${back}」，点开之前是「${before}」（票 86 的回程）`,
          );
        }
        if ((await page.getByTestId("review-return").count()) !== 0) {
          problems.push("回来了，「回到原处」那一枚却还在：它是「你正在看别处」的标记");
        }
      }
    }
  } finally {
    await context.close();
  }

  return problems;
}

/** 第二程：首页那份 Demo 回放——模型席也能看，而且时间轴真的跟着跳。 */
async function replayLeg(lane) {
  const problems = [];
  const origin = await lane.devUrl();
  const page = await lane.newPage();

  try {
    await page.goto(`${origin}/`, { waitUntil: "domcontentloaded" });
    await page.getByTestId("table-timeline").waitFor({ timeout: 15000 });

    // 首页自动播（票 71）：先在滑块中间**真点一下**（一拖就暂停，票 75）。
    // 这一下同时把回程那一条的“原处”搬到牌谱中间：停在第 0 帧的话，
    // “回得来”与“什么都没发生”在数上分不开。
    const slider = page.getByTestId("table-timeline");
    const box = await slider.boundingBox();
    await slider.click({ position: { x: box.width * 0.5, y: box.height / 2 } });

    // ⑥ 默认上帝视角：复盘没有主语。
    const god = await panel(page);
    if (god.present) {
      problems.push("首页默认是上帝视角，复盘却已经有主语了：复盘的第一个字是「你」");
    }
    if (!god.hint) {
      problems.push("上帝视角下页面一句话都没说：人因此不知道坐下来就看得到复盘");
    }

    // ⑧ 的**阴性对照**（票 105）：上帝视角没有主语，时间轴上因此一枚标记都没有
    // ——标记是「**这一席**值得看的那几手」，没有主语就没有它们。
    const godMarks = await timelineMarks(page);
    if (godMarks.present && godMarks.marks.length !== 0) {
      problems.push(
        `上帝视角下时间轴上已经标了 ${godMarks.marks.length} 枚：这一屏还没有主语，` +
          "那几枚标的是哪一席值得看的手？",
      );
    }

    // ⑦ 坐到座位 1：那一席的逐手复盘出来了（**复盘不是真人专属**）。
    await page.getByTestId(`table-view-${WATCHED}`).click();
    await page.getByTestId("table-review").waitFor({ timeout: 20000 });
    const shown = await panel(page);

    if (shown.seat !== WATCHED) {
      problems.push(`坐到了座位 ${WATCHED}，复盘的主语却是座位 ${shown.seat}`);
    }

    const text = readFileSync(new URL("../public/demo-paifu.json", import.meta.url), "utf8");
    const expected = await expectedFrom(page, text, WATCHED);
    if (expected.error) {
      problems.push(`引擎读不动首页那份牌谱：${expected.error}`);
      return problems;
    }

    // ⑧ **只看值得看的那几手 + 时间轴上那几枚**（票 105）。
    // 这一程是 CI 里唐一量得到这一条的地方（那份 6 MB 不在场，因此只剩引擎那一半判据）。
    const focusMark = markerSince(problems);
    const rule = worthwhile(expected, null);
    const marks = await timelineMarks(page);

    problems.push(...focused(`首页座位 ${WATCHED}`, shown, marks, expected, rule));

    if (shown.filter !== "on") {
      problems.push(`复盘默认该只摆值得看的那几手，面板说的是「${shown.filter}」`);
    }
    if (rule.kept.size === 0 || rule.kept.size >= expected.notes.length) {
      problems.push(
        `首页座位 ${WATCHED}：一整场 ${expected.notes.length} 手里值得看的数出 ${rule.kept.size} 手：` +
          "筛选与标记那几条这一趟等于没跑（判据 3）",
      );
    }
    // **筛得有意义**：留下来的不到一半，否则「精选」只是把整列换个说法再摆一遍。
    if (rule.kept.size * 2 >= expected.notes.length) {
      problems.push(
        `首页座位 ${WATCHED}：${expected.notes.length} 手里留下了 ${rule.kept.size} 手——这不叫筛选`,
      );
    }

    console.log(
      `首页座位 ${WATCHED}：只看值得看的那几手、时间轴上逐手标着它们 ${focusMark()}` +
        `（${expected.notes.length} 手里显示 ${shown.notes.length} 手、轴上 ${marks.marks.length} 枚；` +
        `那一句：「${shown.filterLine}」）`,
    );

    // 拨回「看全部」：条数回来、**轴上那几枚一枚不少**（开关只改这一列摆几条），
    // 票 90/93/103/107 那几条逐项对拍在这一态下跑。
    if (
      !(await clicked(
        page,
        "review-filter-toggle",
        problems,
        "筛掉了就要有一枚拨得回去的开关（票 105）",
      ))
    ) {
      return problems;
    }

    const whole = await panel(page);
    const wholeMarks = await timelineMarks(page);
    problems.push(...focused(`首页座位 ${WATCHED}（看全部）`, whole, wholeMarks, expected, rule));

    if (
      wholeMarks.marks.map((mark) => mark.turn).join(" ") !==
      marks.marks.map((mark) => mark.turn).join(" ")
    ) {
      problems.push(
        "拨回「看全部」之后轴上那几枚变了：那一枚开关改的是「这一列摆几条」，不是「哪几手值得看」",
      );
    }

    const { problems: mismatches, counts } = compare("首页那份牌谱", whole, expected);
    problems.push(...mismatches);
    console.log(
      `首页座位 ${WATCHED}：${counts.rows} 条逐项对拍 ${tick(mismatches.length === 0)}` +
        `（有效牌 ${counts.ukeire} 条、危险度 ${counts.danger} 条、` +
        `更好的候选 ${counts.better} 条、最优之一 ${counts.best} 条）`,
    );
    console.log(
      `首页座位 ${WATCHED}：画出来的 ${counts.numbers} 个数逐个指得回引擎那一份的一格 ` +
        `${tick(counts.untraced === 0)}（一个自造的合成数都不出，票 107）`,
    );

    if (counts.numbers === 0) {
      problems.push("首页那一席一个数都没溯过源：逐数溯源那一条这一趟等于没跑（判据 3）");
    }

    // ⑧ 点某一手 → 游标跳过去 → 关掉回原处（票 86 立的回程规矩）。
    const cursor = () => attr(page, "table-timeline", "data-cursor");
    const origin0 = await cursor();
    // 挑一条落在游标**前面**的标注：跳过去才看得出游标真的动了（票 86 那一课：
    // 往前跑的回放早晚会路过原处，往回跳才分得出“回来了”与“又播到了”）。
    //
    // **优先挑轴上标着的那几手**（票 105）：那一条跳完之后游标停在第几帧是量得出来的，
    // 于是那一枚标记钉在哪一帧就有了一个与面板无关的锚点（否则帧号只是两处 DOM 互相印证）。
    const reachable = whole.notes.filter(
      (note) => note.kind === "dahai" && note.frame + 30 < Number(origin0),
    );
    const jump = reachable.filter((note) => rule.kept.has(note.turn)).at(-1) ?? reachable.at(-1);

    if (jump === undefined) {
      problems.push(`游标停在第 ${origin0} 帧，它前面一条可点的标注都没有：这一条什么都证不了`);
      return problems;
    }

    if (!(await clicked(page, `review-turn-${jump.turn}`, problems, "每一条标注都该点得开"))) {
      return problems;
    }
    const moved = await cursor();
    if (moved !== String(jump.frame)) {
      problems.push(
        `点了第 ${jump.turn} 手，游标却停在第 ${moved} 帧（该是第 ${jump.frame} 帧）：` +
          "轴只有票 75 那一根，点一条标注就是把它搬过去",
      );
    }
    if (moved === origin0) {
      problems.push(`点开之前游标就在第 ${origin0} 帧：这一条什么都没证明，换一条落在别处的标注`);
    }

    const still = await panel(page);
    if (!still.present || still.notes.length !== whole.notes.length) {
      problems.push("跳过去之后复盘面板变了样：复盘读的是整份牌谱，不是游标停在哪儿");
    }

    // 跳到那一帧之后，那一枚标记钉的就是游标停住的这一帧（票 105）。
    const marked = wholeMarks.marks.find((mark) => mark.turn === jump.turn);
    if (marked !== undefined && String(marked.frame) !== moved) {
      problems.push(
        `第 ${jump.turn} 手那一枚标在第 ${marked.frame} 帧，而点开它之后游标停在第 ${moved} 帧` +
          "——轴上那一枚与它自己那一手不在同一处",
      );
    }

    if (
      !(await clicked(page, "review-return", problems, "跳走了就要回得来（票 86 立的回程规矩）"))
    ) {
      return problems;
    }
    const returned = await cursor();
    if (returned !== origin0) {
      problems.push(
        `按「回到原处」之后游标停在第 ${returned} 帧，点开之前是第 ${origin0} 帧（票 86 立的回程规矩）`,
      );
    } else {
      console.log(
        `点开跳走了、关掉跳回来 ✓（第 ${origin0} 帧 → 第 ${jump.frame} 帧 → 第 ${returned} 帧）`,
      );
    }

    // ⑨ 没按那一枚之前，强 AI 那一行一个都没有（票 93；票 90 那条「一个都没有」翻的就是它）。
    if (shown.strong !== 0) {
      problems.push(
        `没人按那一枚，面板里已经有 ${shown.strong} 行强 AI 的标注：` +
          "那几 MB 只在有人按下去那一刻才拉（ADR-0006 边界 1）",
      );
    }
    if (shown.strongState !== "untouched") {
      problems.push(`没人按那一枚，强 AI 那一条的状态却是「${shown.strongState}」`);
    }
    for (const word of ["暂无", "评分"]) {
      if (shown.text.includes(word)) {
        problems.push(
          `复盘面板里出现了「${word}」：没有的东西不占位，也不造总分（票 90/93 的边界）`,
        );
      }
    }

    // 那一枚按钮那一句上只得有一个数：**这一席落定了几手**（票 107 同一条判据；
    // 没人按那一枚时那一段里既没有毫秒也没有字节数，因此它此刻是一个硬期望）。
    // **它说的是整场那几手，不是筛完剩下的那几手**（票 105）：按下去是逐手问一整场，
    // 写成筛完的数就是在说一件不会发生的事。期望值取引擎那一侧那份。
    const asking = numerals(shown.strongText).join(" ");
    if (asking !== String(expected.notes.length)) {
      problems.push(
        `没人按那一枚时，强 AI 那一段印出来的数是 [${asking}]，` +
          `而那一句只该说得出「这 ${expected.notes.length} 手」：「${shown.strongText}」`,
      );
    }
  } finally {
    await page.close();
  }

  return problems;
}

/**
 * 闸门自己那一侧的**另一条路**（判据 6）：`ReviewCheck.asks` 走 `Replay.traceOfPaifu` +
 * `GameState.step` 重建每一手的那一份投影，**再拿它去问同一份 wasm**，
 * 最后与页面上那一行对 id。页面那一侧走的是 `Table.replay` 的帧 + `Review`。
 *
 * **整个循环在页面内跑**（票 56 那条教训）：那一叠决策包有几 MB，
 * 每手一次 playwright 往返会把它们来回搬两遍；这里只把**结果**搬出来。
 */
function asked(page, text, seat, godEvery, sampleEvery) {
  return retryOnReload(() =>
    page.evaluate(
      async ({ text, seat, godEvery, sampleEvery }) => {
        const check = await import("./src/generated/ReviewCheck.js");
        // **与页面用的是同一个模块实例**（Fable 那边 import 的也是它）：
        // 因此这里不会再拉一遍那几 MB，「恰好 1 次请求」那一条才成立。
        const baseline = await import("./src/baseline/baseline.ts");
        // 翻译层（票 103 的第⑥趟要拿它把同一份投影变成 node 那侧喂得进去的 mjai 行）。
        const mjai = await import("./src/baseline/mjai.ts");

        const plannedAt = performance.now();
        const plan = JSON.parse(check.asks(text, seat, godEvery));
        const planMs = performance.now() - plannedAt;
        if (plan.error) return { error: plan.error };

        const ask = async (decision) => {
          const reply = JSON.parse(await baseline.decide(JSON.stringify({ decision })));
          return {
            id: typeof reply.action_id === "number" ? reply.action_id : null,
            failure: reply.failure ?? null,
            // 候选分布（票 103）：**概率搬的是 `String(x)`**，与页面往 `data-*` 上搬的
            // 那一串是同一种写法（最短往返），于是两边比的是字符而不是四舍五入后的影子。
            candidates: Array.isArray(reply.candidates) ? reply.candidates : [],
            total: typeof reply.candidates_total === "number" ? reply.candidates_total : null,
          };
        };

        const askedAt = performance.now();
        const notes = [];
        const samples = [];
        for (const [order, note] of plan.notes.entries()) {
          const mine = await ask(note.decision);
          notes.push({
            turn: note.turn,
            kind: note.kind,
            playedId: note.played_id,
            hidden: note.hidden,
            options: note.options,
            id: mine.id,
            failure: mine.failure,
            candidates: mine.candidates,
            total: mine.total,
            // **同一手问两遍**（可复现）：只在抽到的那几手上再问一次，均摊下来不贵。
            again: note.god_later === null ? null : (await ask(note.decision)).id,
            // 两份故意的上帝视角（只在抽到的那几手上造）。
            godLater: note.god_later === null ? null : await ask(note.god_later),
            godAll: note.god_all === null ? null : await ask(note.god_all),
          });

          // 抽几手把**喂进去的那几行 mjai** 也搬出去：node 那侧拿同一份局面直接问那份 wasm，
          // 印出来的原文就是逐位对拍的左侧（票 103 面点名的那条闸门）。
          if (order % sampleEvery === 0) {
            samples.push({
              turn: note.turn,
              lines: mjai.historyLines(note.decision.history, seat),
            });
          }
        }

        // **秒表在这里就停**：下面那一叠 `decideAll` 是另一件事，
        // 把它算进「逐手问」里会凭空多出六十毫秒（这一条自己踩过一次）。
        const askMs = performance.now() - askedAt;

        // **跨界那一叠有多大**（票 103 要量的数）：走的就是页面那一枚按钮走的 `decideAll`，
        // 回来的信封里把分布那两格拆掉再量一遍，**于是「改前」与「改后」量的是同一叠数据**
        // （把两个版本各跑一遍再比，比的就多了一份随机性）。
        const turns = plan.notes.map((note) => ({ turn: note.turn, decision: note.decision }));
        const bulkAt = performance.now();
        const bulk = await baseline.decideAll(JSON.stringify({ turns }));
        const bulkMs = performance.now() - bulkAt;
        const parsed = JSON.parse(bulk);
        const stripped = JSON.stringify({
          ...parsed,
          answers: (parsed.answers ?? []).map(({ candidates, candidates_total, ...rest }) => rest),
        });

        return {
          notes,
          samples,
          planMs,
          askMs,
          bulk: {
            bytes: bulk.length,
            bytesWithout: stripped.length,
            wallMs: bulkMs,
            askMs: parsed.ask_ms ?? null,
            answers: (parsed.answers ?? []).length,
          },
        };
      },
      { text, seat, godEvery, sampleEvery },
    ),
  );
}

/**
 * **同一份局面，拿 `probe/akagi-wasm` 那条 node 路径再问一次**（票 103 的逐位对拍）。
 *
 * 这一步跑在 **node 里**（不在浏览器里），走的是探路件那份手写 ABI（`probe.js`），
 * 拿到的是 `probe_decide` 印出来的**那一段 JSON 原文**——中间没有 janpo 的任何一层。
 * 于是「页面上那几个概率是不是 wasm 印出来的那几个」有了一个与页面完全无关的左侧。
 */
async function askedInNode(samples, seat) {
  const bytes = readFileSync(PUBLIC_ASSET);
  const instance = await instantiateProbe(
    bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength),
  );

  if (instance.exports.probe_init(4, seat) !== 0) throw new Error("node 那侧 probe_init 失败");

  return samples.map((sample) => {
    for (const line of sample.lines) {
      if (feedLine(instance, line) !== 1) throw new Error(`node 那侧喂不进去：${line}`);
    }
    const text = decideText(instance);
    return {
      turn: sample.turn,
      // **不解析就取字符**：`"p":0.5961328` 里那一串就是 Rust 那侧 f32 的最短往返打印。
      literals: [...text.matchAll(/"p":(-?[0-9][0-9.eE+-]*)/g)].map((match) => match[1]),
      text,
    };
  });
}

/**
 * 第三程（票 93）：**强 AI 会怎么打**。
 *
 * 两档：站点上有那份 6 MB 产物时真问一遍（⑩–⑬ 全量），没有时量的是「算不动那一路」
 * （整行不出现，而票 90 那几栏一条不少）。**走的是哪一档印在总结里**
 * （票 92 那一课：别让读日志的人以为 CI 里量到的是前一档）。
 */
async function strongLeg(lane, { godEvery, sampleEvery }) {
  const problems = [];
  const origin = await lane.devUrl();
  const context = await lane.newContext();
  const requests = [];
  context.on("request", (request) => {
    if (request.url().includes(ASSET)) requests.push(request.url());
  });

  const page = await context.newPage();
  const real = assetPresent();

  try {
    await page.goto(`${origin}/`, { waitUntil: "domcontentloaded" });
    await page.getByTestId("table-timeline").waitFor({ timeout: 15000 });
    await page.getByTestId(`table-view-${WATCHED}`).click();
    await page.getByTestId("table-review").waitFor({ timeout: 20000 });

    // ⑩ 懒加载：面板摆在那儿、没人按那一枚时，一个字节都没拉。
    const before = await panel(page);
    if (requests.length !== 0) {
      problems.push(`没人按那一枚，复盘就拉了那份资产 ${requests.length} 次：${requests[0]}`);
    }
    if (before.strongState !== "untouched" || before.strong !== 0) {
      problems.push(
        `没人按那一枚，强 AI 那一条就已经是「${before.strongState}」（${before.strong} 行标注）`,
      );
    }

    if (
      !(await clicked(page, "review-strong-ask", problems, "复盘里该有一枚「让强 AI 也看一遍」"))
    ) {
      return problems;
    }

    // 墙钟：从按下去到有东西看为止。**它比页面自己报的那两个数大**：
    // 中间还夹着把一百多份决策包编成 JSON（F# 那一侧，同步）与两次重画。
    const clickedAt = Date.now();

    await page.waitForFunction(
      () => {
        const state = document
          .querySelector('[data-testid="review-strong"]')
          ?.getAttribute("data-review-strong-state");
        return state === "ready" || state === "unavailable";
      },
      undefined,
      { timeout: 120000 },
    );

    const wallMs = Date.now() - clickedAt;
    const shown = await panel(page);
    const fetched = requests.length;

    // 票 90 那几栏一条不少（两档都要）：强 AI 那一行叠上去之后，引擎自算那一层照旧对得上号。
    const text = readFileSync(new URL("../public/demo-paifu.json", import.meta.url), "utf8");
    const expected = await expectedFrom(page, text, WATCHED);
    if (expected.error) {
      problems.push(`引擎读不动首页那份牌谱：${expected.error}`);
      return problems;
    }
    // ⑧ 的后一半（票 105）：**问过之后，强 AI 那一半判据才真正开口**。
    // 这一段在 CI 里量到的仍旧只有引擎那一半（那份 6 MB 不在场），
    // 本机演习那一档才量得到完整的那一套（下面 `--asset` 那一段）。
    const filterMark = markerSince(problems);
    const beforeMarks = await timelineMarks(page);

    if (
      !(await clicked(
        page,
        "review-filter-toggle",
        problems,
        "筛掉了就要有一枚拨得回去的开关（票 105）",
      ))
    ) {
      return problems;
    }

    const whole = await panel(page);
    const { problems: mismatches } = compare("问过强 AI 之后", whole, expected);
    problems.push(...mismatches);

    if (shown.strongState !== "ready") {
      // **算不动那一路**（CI 的常规趟）：整行不出现，但页面明说原因。
      if (real) {
        problems.push(
          `站点上有那份产物，强 AI 那一条却是「${shown.strongState}」：${shown.strongText}`,
        );
      }
      if (shown.strong !== 0) {
        problems.push(`它用不了，页面上却摆了 ${shown.strong} 行强 AI 的标注`);
      }
      if (!shown.strongText.includes("强 AI 基线")) {
        problems.push(`它用不了，面板却没说清为什么：「${shown.strongText}」`);
      }
      if (shown.text.includes("暂无")) {
        problems.push("它用不了时页面写了「暂无」：没有的东西不占位（票 90/92/93 同一个规矩）");
      }
      // 算不动那一路里，值得看的那几手只剩引擎那一半判据（与第二程量到的一模一样）。
      const rule = worthwhile(expected, null);
      problems.push(...focused("算不动那一路（看全部）", whole, beforeMarks, expected, rule));

      console.log(
        `（这一趟站点上没有那份 6 MB 产物：量的是「算不动就整行不出现」那一路，` +
          `页面说的是「${shown.strongText}」；请求发出去了 ${fetched} 次）`,
      );
      console.log(
        `算不动那一路：值得看的那几手只剩引擎那一半判据 ${filterMark()}` +
          `（${expected.notes.length} 手里 ${rule.kept.size} 手、轴上 ${beforeMarks.marks.length} 枚）`,
      );
      return problems;
    }

    // ---- 以下只在本机演习那一档跑得到（那份产物真在场） ----

    if (fetched !== 1) {
      problems.push(`按下去之后那份资产被请求了 ${fetched} 次，该正好 1 次`);
    }

    const mine = await asked(page, text, WATCHED, godEvery, sampleEvery);
    if (mine.error) {
      problems.push(`闸门那一侧重建不出那几份投影：${mine.error}`);
      return problems;
    }

    // **逐行那几条读的是「看全部」那一叠**（票 105 拨过一次开关）：筛选开着时这一列
    // 只摆值得看的那几条，而 ⑪⑫⑬⑭⑮ 要逐手对拍一整场。
    const rows = new Map(whole.notes.map((note) => [note.turn, note]));
    const counts = {
      rows: 0,
      diffs: 0,
      hidden: 0,
      again: 0,
      later: 0,
      all: 0,
      missing: 0,
      // 票 103：候选分布那几条断言各开口了几次（判据 3）。
      dist: 0,
      ranked: 0,
      outside: 0,
      second: 0,
      shortlist: 0,
      unnamed: 0,
    };
    /** 上游给了几条的直方图（报告里要写「到底给了几条」）。 */
    const widths = new Map();
    /** 一整场里概率和的极值：**先量再断言**，不先写一条 `sum = 1` 再去调数据。 */
    let maxSum = 0;
    let minSum = 1;
    /** 抬出两个具体例子印在总结里：一句「K 手」看不出它到底造出了什么局面。 */
    const samples = [];

    for (const each of mine.notes) {
      const row = rows.get(each.turn);
      if (row === undefined) {
        problems.push(`第 ${each.turn} 手引擎说有，页面上却没有这一条标注`);
        continue;
      }
      if (each.hidden > 0) counts.hidden += 1;

      // ⑪ **喂给它的是同一份投影**：两条路各自重建、各自去问，出来的必须是同一条 id。
      if (each.id === null) {
        counts.missing += 1;
        if (row.strong !== null) {
          problems.push(
            `第 ${each.turn} 手引擎那一侧没问出来（${each.failure}），页面上却有一行「${row.strongText}」`,
          );
        }
        continue;
      }
      if (row.strongId === null) {
        problems.push(
          `第 ${each.turn} 手引擎那一侧问出来了（id=${each.id}），页面上却没有强 AI 那一行`,
        );
        continue;
      }

      counts.rows += 1;
      const option = each.options.find((option) => option.id === each.id);
      const named = each.candidates.map((choice) => choice.action_id);
      const ps = each.candidates.map((choice) => String(choice.p));

      // ⑲ **候选分布也要两条路各自问一遍**（票 103）：闸门这一侧重建投影、自己去问，
      // 拿回来的那几条必须与页面上那一行逐条相同（id 逐个、概率逐位）。
      if (each.candidates.length > 0) {
        counts.dist += 1;
        widths.set(each.total, (widths.get(each.total) ?? 0) + 1);

        if (row.strongIds.join(" ") !== named.join(" ")) {
          problems.push(
            `第 ${each.turn} 手：闸门问出来的候选是 id=[${named.join(",")}]，` +
              `页面上那一行写的是 [${row.strongIds.join(",")}]`,
          );
        }
        if (row.strongPs.join(" ") !== ps.join(" ")) {
          problems.push(
            `第 ${each.turn} 手：闸门问出来的概率是 [${ps.join(",")}]，` +
              `页面上写的是 [${row.strongPs.join(",")}]——两边不是同一次前向的那几个数`,
          );
        }
        if (row.strongTotal !== each.total) {
          problems.push(
            `第 ${each.turn} 手：上游给了 ${each.total} 条，页面说的是 ${row.strongTotal} 条`,
          );
        }
        if (named.length !== each.total) counts.unnamed += 1;

        // **上游那份分布的形状**（`probe/akagi-wasm/candidates-shape.mjs` 在 69,318 个
        // 决策点上量过的那几条）：降序、落在 [0,1] 里、**和 ≤ 1**（它是 top-3 切片，
        // 不是全分布，因此和不必等于 1），且条数不超过上游那个上限。
        // 它们变了就该有人重读报告 103（判据 15：前提被拆掉时要回头重问）。
        const values = each.candidates.map((choice) => choice.p);
        const sum = values.reduce((total, p) => total + p, 0);
        maxSum = Math.max(maxSum, sum);
        minSum = Math.min(minSum, sum);

        if (values.some((p) => !(p >= 0 && p <= 1))) {
          problems.push(`第 ${each.turn} 手：有一条候选的概率不在 [0,1] 里：[${values.join(",")}]`);
        }
        if (values.some((p, index) => index > 0 && p > values[index - 1])) {
          problems.push(`第 ${each.turn} 手：上游那一列不再是降序的：[${values.join(",")}]`);
        }
        // 1.5e-7 是实测的 f32 舍入上限（同上）：超过它就不是舍入了。
        if (sum > 1 + 2e-7) {
          problems.push(`第 ${each.turn} 手：那几条的和是 ${sum}（> 1）：它不再是一份概率分布了`);
        }
        if (each.total > 3) {
          problems.push(
            `第 ${each.turn} 手：上游给了 ${each.total} 条候选——报告 103 量的那一版只给 top-3，` +
              "它变了就该有人重新量一遍那份分布的形状",
          );
        }
        if (each.total < 3) counts.shortlist += 1;

        // **你打的那一手排第几**：闸门自己在上游那一列里找一遍（判据 8：
        // 期望值取自规则，不取自被检查那句话的来源）。
        const at = named.indexOf(each.playedId);
        if (at === -1) {
          counts.outside += 1;
          if (row.strongRank !== null || row.strongYoursP !== "") {
            problems.push(
              `第 ${each.turn} 手：你打的 id=${each.playedId} 不在它那几条里，` +
                `页面却说排第 ${row.strongRank}（${row.strongYoursP}）`,
            );
          }
          if (!row.strongText.includes(`不在这 ${each.total} 条里`)) {
            problems.push(
              `第 ${each.turn} 手：你打的那一手不在它那几条里，那一行却没说清楚：「${row.strongText}」`,
            );
          }
        } else {
          counts.ranked += 1;
          if (at === 1) counts.second += 1;
          if (row.strongRank !== at + 1) {
            problems.push(
              `第 ${each.turn} 手：你打的 id=${each.playedId} 在上游那一列里排第 ${at + 1}，` +
                `页面写的是第 ${row.strongRank}`,
            );
          }
          if (row.strongYoursP !== ps[at]) {
            problems.push(
              `第 ${each.turn} 手：你打的那一手它给的是 ${ps[at]}，页面写的是 ${row.strongYoursP}`,
            );
          }
          if (!row.strongText.includes(`（第 ${at + 1}）`)) {
            problems.push(
              `第 ${each.turn} 手：你打的那一手排第 ${at + 1}，而那一行写的是「${row.strongText}」`,
            );
          }
        }

        // **一个度量词都不允许**（票 103 的硬边界）：概率不是理由，
        // 把 `p=0.95` 写成「它很确定」就是替一个不会说话的网络编话。
        for (const word of ["确定", "犹豫", "认为", "建议", "把握", "理由", "因为"]) {
          if (row.strongText.includes(word)) {
            problems.push(
              `第 ${each.turn} 手那一行出现了「${word}」：概率不是理由，页面上只能照抄数字`,
            );
          }
        }
      } else if (row.strongPs.length > 0) {
        problems.push(
          `第 ${each.turn} 手：闸门那一侧一条候选都没问到，页面却摆了 ${row.strongPs.length} 个概率`,
        );
      }

      if (row.strongId !== each.id) {
        const said = each.options.find((option) => option.id === row.strongId);
        problems.push(
          `第 ${each.turn} 手：拿那一手当时的投影问出来的是 ${option?.key}（id=${each.id}），` +
            `页面写的是 ${said?.key ?? row.strong}（id=${row.strongId}）——两边喂的不是同一份观测`,
        );
      }
      if (option !== undefined && row.strong !== option.key) {
        problems.push(`第 ${each.turn} 手：那一手该是 ${option.key}，页面写的是 ${row.strong}`);
      }

      // ⑬ 分歧照**规则**再数一遍（判据 8）：它选的那一条与你打的那一条不是同一个 id。
      const differs = each.playedId !== each.id;
      if (differs) counts.diffs += 1;
      if (row.strongDiff !== (differs ? "1" : "")) {
        problems.push(
          `第 ${each.turn} 手：你打的是 id=${each.playedId}、它打的是 id=${each.id}，` +
            `页面却把它标成了「${row.strongDiff === "1" ? "不同" : "相同"}」`,
        );
      }
      if (differs !== row.strongText.includes("与你不同")) {
        problems.push(
          `第 ${each.turn} 手：那一行写的是「${row.strongText}」，而两边 id 说的是另一回事`,
        );
      }

      // ⑬ 可复现：同一手问两遍给同一答案。
      if (each.again !== null) {
        counts.again += 1;
        if (each.again !== each.id) {
          problems.push(`第 ${each.turn} 手问两遍给了两个答案（${each.id} 与 ${each.again}）`);
        }
      }

      // ⑫ **上帝视角会打 A、该席视角只能打 B**：造出 A≠B 的那几手，断言页面给的是 B。
      for (const [which, god] of [
        ["later", each.godLater],
        ["all", each.godAll],
      ]) {
        if (god === null || god.id === each.id) continue;
        counts[which] += 1;
        if (!samples.some((sample) => sample.startsWith(which))) {
          const a =
            god.id === null
              ? `答不上来（${god.failure}）`
              : (each.options.find((option) => option.id === god.id)?.key ?? `id=${god.id}`);
          samples.push(
            `${which}：第 ${each.turn} 手上帝视角打 A=${a}，该席视角只能打 B=${option?.key}，页面给的是 ${row.strong}`,
          );
        }
        if (row.strongId !== each.id) {
          const a = god.id === null ? `答不上来（${god.failure}）` : `id=${god.id}`;
          problems.push(
            `第 ${each.turn} 手：上帝视角（${which}）会给 ${a}、该席视角给的是 id=${each.id}，` +
              `而页面上那一行是 id=${row.strongId}`,
          );
        }
      }
    }

    // 页面自己抬头那两个数与逐行数出来的必须一致。
    if (whole.strongRows !== whole.notes.filter((note) => note.strong !== null).length) {
      problems.push(`抬头说有 ${whole.strongRows} 行，实际画了 ${whole.strong} 行`);
    }
    if (whole.strongDiffs !== counts.diffs) {
      problems.push(`抬头说 ${whole.strongDiffs} 手与你不同，照规则数出来的是 ${counts.diffs} 手`);
    }

    // ⑧（本机演习那一档才跑得到）：**强 AI 那一半判据真的开了口**（票 105）。
    //
    // 拨回「只看值得看的那几手」，拿闸门**自己问出来的那一叠**（`mine.notes`，另一条路）
    // 照规则数一遍：这一列摆哪几条、轴上标哪几枚、那一句里两个数多少，逐手对。
    // **由强 AI 点亮的那几枚必然落在分歧那几手里**（票面：逐手对齐，不是看着差不多）。
    const focusedMark = markerSince(problems);
    const withStrong = worthwhile(expected, mine.notes);

    if (await clicked(page, "review-filter-toggle", problems, "那一枚开关拨得回去（票 105）")) {
      const picked = await panel(page);
      const pickedMarks = await timelineMarks(page);
      problems.push(...focused("问过强 AI 之后", picked, pickedMarks, expected, withStrong));

      // 逐手对齐：不是「引擎那一半」点亮的每一枚，都得是分歧那几手里的一手，
      // **而且恰好是那几手里「它很确定而你排在后面」的那几手**（一枚不多、一枚不少）。
      const differing = new Set(
        mine.notes
          .filter((each) => each.id !== null && each.playedId !== each.id)
          .map((each) => each.turn),
      );
      const lit = pickedMarks.marks
        .map((mark) => mark.turn)
        .filter((turn) => !withStrong.better.has(turn));
      const wanted = [...withStrong.notable]
        .filter((turn) => !withStrong.better.has(turn))
        .sort((a, b) => a - b);

      for (const turn of lit) {
        if (!differing.has(turn)) {
          problems.push(
            `第 ${turn} 手被强 AI 那一半判据点亮在轴上，可它根本不在分歧那几手里` +
              "——「它很确定而你打了别的」蕴含「不同」",
          );
        }
      }
      if (lit.join(" ") !== wanted.join(" ")) {
        problems.push(
          `由强 AI 点亮的是第 [${lit.join("，")}] 手，照规则该是第 [${wanted.join("，")}] 手`,
        );
      }

      // 判据 3：这两半各真的开过几次？为 0 的那一半，这一趟等于没跑。
      if (withStrong.better.size === 0) {
        problems.push("一手「引擎的试打表里还有更好的换法」都没有：那一半判据这一趟等于没跑");
      }
      if (withStrong.notable.size === 0) {
        problems.push(
          "一手「它很确定而你排在后面」都没有：票 105 那条新判据这一趟等于没跑（判据 3）",
        );
      }
      // **收紧是这一票的全部意义**：分歧占一整场的一半，点亮的只能是其中少数。
      if (withStrong.notable.size * 2 >= differing.size) {
        problems.push(
          `分歧 ${differing.size} 手里点亮了 ${withStrong.notable.size} 手：那一条判据没收紧任何东西`,
        );
      }

      console.log(
        `只看值得看的那几手（问过强 AI）${focusedMark()}` +
          `：${expected.notes.length} 手里显示 ${picked.notes.length} 手、轴上 ${pickedMarks.marks.length} 枚` +
          `（引擎那一半 ${withStrong.better.size} 手、强 AI 那一半 ${withStrong.notable.size} 手，` +
          `其中只靠强 AI 的 ${wanted.length} 手；分歧一共 ${differing.size} 手）`,
      );
    }

    // ⑥ **逐位对拍**（票 103 面点名的那条）：抽到的那几手拿 `probe/akagi-wasm` 的 node
    // 路径再问一次，页面上那几个概率必须与 wasm 直接印出来的那几个**一个 bit 不差**。
    //
    // 为什么不直接比字符串：Rust 的 `{}` 从不用指数写法（它印 `0.0000001`），
    // 而 JS 在 1e-6 以下换成 `1e-7`——实测 69,318 个决策点里有 217 个落在这个区间
    // （`candidates-shape.mjs`）。拿字面相等当断言会在那几手上假红，而**假红比真红危险**
    // （判据 16）。因此这里断言的是两件事：两边解出来的双精度**严格相等**（`===`），
    // 且页面上那一串本身是**最短往返表示**（`String(Number(s)) === s`）——合起来就是
    // 「一位都没舍」，而不是「四舍五入之后看着差不多」。
    let literal = 0;

    try {
      const raw = await askedInNode(mine.samples, WATCHED);

      for (const each of raw) {
        const row = rows.get(each.turn);
        const asked = mine.notes.find((note) => note.turn === each.turn);

        if (row === undefined || asked === undefined) {
          problems.push(`第 ${each.turn} 手：node 那侧问到了，页面上却没有这一条`);
          continue;
        }
        if (each.literals.length !== row.strongTotal) {
          problems.push(
            `第 ${each.turn} 手：node 那侧的 wasm 印了 ${each.literals.length} 条候选，` +
              `页面说上游给了 ${row.strongTotal} 条：${each.text}`,
          );
          continue;
        }
        if (row.strongPs.length !== each.literals.length) {
          problems.push(
            `第 ${each.turn} 手：页面上摆了 ${row.strongPs.length} 个概率，` +
              `wasm 直接印出来的是 ${each.literals.length} 个`,
          );
          continue;
        }

        for (const [index, digits] of each.literals.entries()) {
          const shown = row.strongPs[index];
          if (Number(shown) !== Number(digits)) {
            problems.push(
              `第 ${each.turn} 手第 ${index + 1} 条：页面上是 ${shown}，wasm 直接印的是 ${digits}` +
                "——两边不是同一个数",
            );
          } else if (String(Number(shown)) !== shown) {
            problems.push(
              `第 ${each.turn} 手第 ${index + 1} 条：页面上那一串「${shown}」不是最短往返表示，` +
                "它被舍过一道（那道闸门就变成了「四舍五入看着差不多」）",
            );
          } else if (digits === shown) {
            literal += 1;
          }
        }
        counts.raw = (counts.raw ?? 0) + 1;
      }
    } catch (error) {
      problems.push(`node 那条路径问不了那份 wasm：${error}`);
    }

    // 判据 3：这几条断言各开口了几次。为 0 的那一种，这一趟等于没跑。
    if (counts.rows < 40) problems.push(`只对拍了 ${counts.rows} 行：一整场该有几十手`);
    if (counts.dist < 40) {
      problems.push(`只有 ${counts.dist} 行带着候选分布：票 103 那几条断言这一趟等于没跑`);
    }
    if (counts.ranked === 0) {
      problems.push("一整场下来没有一手「你打的就在它那几条里」：「排第几」那一栏等于没跑");
    }
    if (counts.second === 0) {
      problems.push(
        "一整场下来没有一手你打的是它的**第 2 候选**：票面点名的那个局面这一趟没造出来",
      );
    }
    if (counts.outside === 0) {
      problems.push("一整场下来没有一手你打的落在它那几条之外：「不在这几条里」那一句等于没跑");
    }
    if ((counts.raw ?? 0) === 0) {
      problems.push("一手都没拿 node 那条路径对过：逐位对拍那一条这一趟等于没跑");
    }
    if (counts.unnamed > 0) {
      problems.push(
        `有 ${counts.unnamed} 手上游给的候选没能全落进那一包：` +
          "页面上那句话还是实话（它会说「认得出 N 条」），但报告 103 量到的是 0，变了就该有人重读它",
      );
    }
    if (counts.diffs === 0)
      problems.push("一整场下来一手分歧都没有：「分歧点要跳出来」那一条等于没跑");
    if (counts.hidden === 0) {
      problems.push("没有一手的投影里遮着他家摸的牌：「喂的不是上帝视角」那一条等于没跑");
    }
    if (counts.again === 0) problems.push("一手都没问第二遍：可复现那一条等于没跑");
    if (counts.later + counts.all === 0) {
      problems.push(
        "一手「上帝视角会打 A、该席视角只能打 B」的局面都没造出来：这一票唯一真正难的那条断言等于没跑",
      );
    }

    // 那几个勾**由数据决定**（票 106）。这一程四个题目的断言推在**同一个逐手循环**里，
    // 拆不开“哪一条是哪个题目推的”，因此四句共用**这一程的**清单：
    // 宁可四句一起印 `✗`，也不许出现一条与失败矛盾的 `✓`。
    const strongMark = mark(problems);
    console.log(
      `强 AI 逐手对照：${counts.rows} 行与引擎另一条路问出来的同一条 id ${strongMark}` +
        `（分歧 ${counts.diffs} 手、遮着他家摸牌的 ${counts.hidden} 行、问两遍同答案 ${counts.again} 手、` +
        `它交不出来 ${counts.missing} 手）`,
    );
    console.log(
      `上帝视角会打 A、该席视角只能打 B：同一局后来那一份流 ${counts.later} 手、` +
        `一条不掩一张不隐那一份 ${counts.all} 手——逐手断言页面给的是 B ${strongMark}`,
    );
    for (const sample of samples) console.log(`　${sample}`);
    console.log(
      `候选分布：${counts.dist} 行与闸门另一条路问出来的逐条相同 ${strongMark}` +
        `（上游给了几条：${[...widths.entries()]
          .sort((a, b) => a[0] - b[0])
          .map(([k, v]) => `${k} 条×${v}`)
          .join("、")}；和落在 ${minSum.toFixed(4)}–${maxSum.toFixed(4)}，` +
        `你打的在里面 ${counts.ranked} 手（其中排第 2 的 ${counts.second} 手）、不在里面 ${counts.outside} 手，` +
        `上游给不足三条的 ${counts.shortlist} 手、认不出来的 ${counts.unnamed} 手）`,
    );
    console.log(
      `逐位对拍：抽了 ${counts.raw ?? 0} 手拿 probe/akagi-wasm 的 node 路径重问一次，` +
        `页面上那几个概率与 wasm 直接印的严格相等 ${strongMark}（其中 ${literal} 个连字面都一样）`,
    );
    console.log(
      `代价：按下去到有东西看共 ${wallMs} ms；页面那一边 ${whole.strongMs} ms / ${whole.strongRows} 行（它自己报的那一句：${whole.strongText.replace(/\s+/g, " ")}）；` +
        `闸门这一边重建投影 ${Math.round(mine.planMs)} ms、逐手问 ${Math.round(mine.askMs)} ms` +
        `（${mine.notes.length} 手，其中抽到的那几手多问了三遍）`,
    );
    console.log(
      `跨界那一叠（${mine.bulk.answers} 手）：${mine.bulk.bytes} 字节，` +
        `把分布那两格拆掉是 ${mine.bulk.bytesWithout} 字节（多 ` +
        `${(((mine.bulk.bytes - mine.bulk.bytesWithout) / mine.bulk.bytesWithout) * 100).toFixed(1)}%）；` +
        `逐手问 ${Math.round(mine.bulk.askMs)} ms、连编解码共 ${Math.round(mine.bulk.wallMs)} ms`,
    );
    // **把页面上那几句原文印出来**：这一票唯一真正危险的事是「替它编一句人话」，
    // 而那件事只有人读得出来。三种各挑一行（你打的排第一 / 排在后面 / 根本不在里面）。
    const quote = (why, pick) => {
      const found = whole.notes.find((note) => note.strongPs.length > 0 && pick(note));
      if (found !== undefined) console.log(`　页面上那一行（${why}）：${found.strongText}`);
    };

    quote("你打的就是它第 1 条", (note) => note.strongRank === 1);
    quote("你打的排在后面", (note) => note.strongRank !== null && note.strongRank > 1);
    quote("你打的不在它那几条里", (note) => note.strongRank === null);
  } finally {
    await context.close();
  }

  return problems;
}

/** 这一道闸门（合并跑与单跑走的是同一段代码）。 */
export async function verifyReview(lane, options = {}) {
  const budgetMs = options.budgetMs ?? 20000;
  const peek = options.peek ?? 60;
  const godEvery = options.godEvery ?? 8;
  // 第⑮条抽多少手：每一手要在 node 里重放一整局的事件流再推理一次（约 1 ms），
  // 抽 16 分之一就有七八手——够一条断言开口，也不会把这一趟拖长。
  const sampleEvery = options.sampleEvery ?? 16;
  const problems = [];

  if (options.asset === true && !assetPresent()) {
    return failure("--asset 说资产在场，但 web/public/ 里没有它：", [
      `找不到 web/public/${ASSET}——造一份的做法见 web/public/baseline/README.md`,
    ]);
  }

  problems.push(...(await humanLeg(lane, { budgetMs, peek })));
  problems.push(...(await replayLeg(lane)));
  problems.push(...(await strongLeg(lane, { godEvery, sampleEvery })));

  return problems.length === 0 ? [] : failure("复盘那一道没过：", problems);
}

if (isEntry(import.meta.url)) {
  const args = process.argv.slice(2);
  const numberAt = (name, fallback) => {
    const at = args.indexOf(name);
    return at === -1 ? fallback : Number.parseInt(args[at + 1], 10);
  };

  await runStandalone((lane) =>
    verifyReview(lane, {
      budgetMs: numberAt("--budget", 20000),
      peek: numberAt("--peek", 60),
      godEvery: numberAt("--god-every", 8),
      sampleEvery: numberAt("--sample-every", 16),
      asset: args.includes("--asset"),
    }),
  );
}
