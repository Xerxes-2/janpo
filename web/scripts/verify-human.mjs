// **真人坐下，把一局打完**那道闸门（票 87 立、票 88 把响应动作接上；spec 的 story 28 / 29 / 30）。
// 全程本机（页面 + 一个本地假端点），**一个字节都不出网**，因此它进 CI。
//
// 第一程（真人坐座位 0、座位 1 交给一个本地假端点、座位 2/3 是自带 bot，**鸣得了就鸣**）：
//   ① **视角按钮不在 DOM 里**：上帝视角与别席视角**根本不存在**（不是灰掉——票 81 把视角
//      定成了信息闸门，而 `disabled` 一行 DevTools 就平了）；只剩自家那一枚，旁边一句为什么；
//   ② **点自己手里的一张就打出去**：能点的那几张挂着 `data-dahai-id`（引擎给的包内 id），
//      条数与真人那一行的 `data-human-playable` 逐个对得上；
//   ②' **每一枚按钮背后都是一条引擎给的 id**（票 88 的要害）：他每一次出手都核一遍
//      「手牌上那几张 + 牌桌下面那一排 + 「过」= `0 … data-human-options-1`，一条不多一条不少」，
//      并核**不合法就不在 DOM 里**（该他出牌那一手没有「过」也没有吃碰杠；响应那一手
//      一张牌都点不出去，也没有立直 / 暗杠 / 加杠 / 九种九牌），
//      以及两条**从规则来的**语义断言（吃只可能来自上家；吃 / 碰 / 大明杠 / 荣和的那张
//      必是上一手打出的那张）；
//   ③ **结构性不泄露**（story 29，**这一票最重要的一条**）：对局中把**整页 HTML** 抓下来，
//      里面每一个 `data-pai` 都必须落在「自家手牌 + 四家的河 + 四家的副露 + 宝牌指示牌」
//      这份预算里——他家的手牌**一张都不许有，连 `data-*` 都不许有**；**新按钮不许把牌带进来**
//      （整页只许有 `data-pai` 这一种带牌的属性）；
//      顺带核他家三席的手牌行：一个 `data-pai` 都没有、`data-hand-hidden=true`、
//      画出来的牌背数与 `data-hand-count` 对得上，且里宝牌指示牌不在场上；
//   ④ **气泡对局中一个都没有**（`humanSeated` 生效），而**阳性对照**是 token 账单
//      说那一席模型真被问过话——没有它，「0 个气泡」也可能只是模型压根没开口；
//   ⑤ **鸣牌是他自己点的**：一整场下来至少真点过一次吃 / 碰 / 杠（票 87 那时是自动过的）；
//   ⑥ **把一整场东风战打完**：终局那一屏在，四家点数之和恒为 100000；
//   ⑦ 终局之后**三样一起松开**：五枚视角按钮回来、座位 1 的气泡回来、那句「视角锁着」没了。
//
// 第二程（真人 + 三家 bot、**种子写死**、门清立直那一套代点：能和就和、能立直就立直、
// 其余一律「过」）：
//   ⑩ **「过」是他自己按的**：页面记的 `data-human-passes` 与他真按下去的次数**逐次对得上**，
//      而那句话里说得出放掉的是碰还是荣和；
//   ⑪ **立直是两段**：宣言那一枚点下去之后 `data-human=reach`，那一手**只剩打牌**
//      （一枚宣言按钮都没有、也没有「过」），且能点的张数比宣言前**少**——
//      「只有打完仍听牌的那几张」由引擎的合法动作集说了算，页面照它渲染；
//   ⑫ 他**真的和了一次**：终局那一屏上和了者是座位 0。
//
// 第三程（`?dev=1` 的阴阳对照，堵挂账 22-A）：
//   ⑧ 真人在座时开 `?dev=1`：曳光弹**不给开**（它把 `start_kyoku` 印在同一张文档里，
//      而 `start_kyoku` 带着四家配牌，且它的种子输入框是任填的）；牌桌照旧活着；
//   ⑨ **阴性对照**：同一条地址、四家没有真人时曳光弹照旧开得了。
//
// 跑法：`cd web && pnpm run fable && pnpm run verify:human`
// 它也是 `verify-browser.mjs` 里的一趟（十五趟共用一个浏览器与一台服务器）。
//
// 选项：--budget ms、--peek N（走多少手之后抓那一份整页 HTML）。
//
// **把第③条按红的做法**（判据 1，票面点名）：改**产品代码**把投影换回上帝视角
// （`TableState.viewpoint` 直接返回 `model.Viewpoint`，而 `?table=1` 默认就是上帝视角），
// 重编 Fable 后跑这一趟：第①条与第③条会一起红，红的原文在报告里。
// **把第②'条按红的做法**：让 `HumanLine.calls` 把不合法的也画出来（例如恒画一枚「过」），
// 或者让它少画一枚——两边的红都在报告 88 里。

import { spawn } from "node:child_process";
import { createServer } from "node:net";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { failure, isEntry, runStandalone } from "./browser-lane.mjs";
import { plantSeating, profileChoice } from "./seating.mjs";
import { hostPage } from "./serve.mjs";

const webRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/** 真人坐这一席（东 1 局的亲：页面一打开就轮到他）。 */
const ME = 0;

/** 模型坐这一席（气泡那两条断言要一个真会说话的对手）。 */
const MODEL = 1;

/** 那份档案在库里的叫法（本机的私人叫法，绝不该出现在牌谱里）。 */
const PROFILE = "真人闸门的对手";

/** 假端点回的那句话。**只可能从它那儿来**：页面里没有任何一处写着它。 */
const SAID = "假端点说：这一手照它的算法只能这么打";

/**
 * 一个元素的 `data-*`，**没有就是 `null`**。
 *
 * 不用 `getByTestId(...).getAttribute(...)`：那一条在元素不存在时会**干等 30 秒再抛**，
 * 而这一道闸门的契约是交一份失败清单（合并跑那个入口要先关浏览器、再逐道汇报）——
 * 抛出去会把十五趟一起搞挂。**这一条是被破坏实验逃出来的**：
 * 把 `humanSeated` 按回恒 false 那一次，它正好把自己抛掉了。
 */
function attr(page, testId, name) {
  return page.evaluate(
    ({ testId, name }) =>
      document.querySelector(`[data-testid="${testId}"]`)?.getAttribute(name) ?? null,
    { testId, name },
  );
}

/** 一个元素的文字，**没有就是 `null`**（理由同 `attr`）。 */
function text(page, testId) {
  return page.evaluate(
    (testId) => document.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? null,
    testId,
  );
}

/** 借内核要一个空闲端口（跑批是并行的，写死端口迟早撞上另一个工作区；判据 16）。 */
function freePort() {
  return new Promise((done, fail) => {
    const probe = createServer();
    probe.on("error", fail);
    probe.listen(0, "127.0.0.1", () => {
      const { port } = probe.address();
      probe.close(() => done(port));
    });
  });
}

/** 起一个假端点，返回它的 baseUrl 与那个进程。 */
async function startEndpoint(origin) {
  const port = await freePort();
  const endpoint = spawn(
    "node",
    [
      "scripts/fake-endpoint.mjs",
      "--port",
      String(port),
      "--cors",
      origin,
      "--quiet",
      "--reason",
      SAID,
    ],
    { cwd: webRoot, stdio: ["ignore", "ignore", "inherit"] },
  );
  return { baseUrl: `http://127.0.0.1:${port}/v1`, endpoint };
}

/**
 * **页面内**把这一桌往前推（票 56 那条教训：每手一次 playwright 往返太贵）。
 *
 * 一步的判据：**他此刻做得了什么就点什么**——牌桌下面那一排按钮（票 88）优先按 `policy`
 * 给的偏好挑一枚，没有那一排就点手牌，两样都没有就按「单步」。
 * 于是「他家打牌→碰不碰→打一张→等三家→再来」这一整圈完全跑在页面里，
 * 一整场东风战一次 `evaluate` 走完。
 *
 * **它同时是闸门本身**：每一次轮到他，都就地核一遍「页面上点得到的 = 引擎给的那一包」
 * 与「不合法的不在 DOM 里」（票 88 的要害），违反的逐条收进 `violations`
 * ——出了页面就再也看不到那一刻的局面了。
 *
 * @param policy `naki` 鸣得了就鸣（第一程）／`closed` 一律「过」、能立直就立直（第二程）
 * @returns clicks 各种动作各点了几次、offers 各种动作各碰上过几次、steps 按了几次单步、
 *          kyokus 打了几局、checks 核过几次、violations 违反清单、stuck 卡在哪儿、ended 终局了没有
 */
function driveHuman(page, { limit, budgetMs, policy }) {
  return page.evaluate(
    async ({ limit, budgetMs, policy }) => {
      const at = (testId) => document.querySelector(`[data-testid="${testId}"]`);
      const all = (selector) => [...document.querySelectorAll(selector)];
      const latest = () => at("table-latest")?.textContent?.trim() ?? "";
      const attr = (node, name) => node?.getAttribute(name) ?? null;
      const num = (node, name) => Number.parseInt(attr(node, name) ?? "-1", 10);

      // 手牌上那几张、牌桌下面那一排、真人那一行此刻各是什么样。
      const surface = () => {
        const line = at("table-human");
        const buttons = all("[data-human-action-id]").map((node) => ({
          id: Number.parseInt(node.dataset.humanActionId, 10),
          kind: node.dataset.humanAction,
          label: node.textContent?.trim() ?? "",
          inRow: node.closest('[data-testid="table-human-calls"]') !== null,
        }));
        return {
          line,
          state: attr(line, "data-human"),
          options: num(line, "data-human-options"),
          playableSaid: num(line, "data-human-playable"),
          callsSaid: num(line, "data-human-calls"),
          passesSaid: num(line, "data-human-passes"),
          rowSaid: num(at("table-human-calls"), "data-human-calls"),
          dahai: all("[data-dahai-id]").map((node) => Number.parseInt(node.dataset.dahaiId, 10)),
          buttons,
        };
      };

      // 上一手是谁打了哪一张（`table-latest` 那句话）。**这是从规则来的那两条断言的锚**：
      // 吃只可能来自上家、吃 / 碰 / 大明杠 / 荣和的那张必是它。
      const discarded = () => {
        const said = latest();
        const found = /座位 (\d+) (?:手切|摸切)([^（(]+)/.exec(said);
        return found === null
          ? null
          : { seat: Number.parseInt(found[1], 10), pai: found[2].trim() };
      };

      const sorted = (values) => [...values].sort((left, right) => left - right);
      const same = (left, right) =>
        left.length === right.length && left.every((each, index) => each === right[index]);

      // **一一对应 + 不合法就不在 DOM 里**（票 88 的要害判据）。
      const audit = (view, violations) => {
        const ids = [...view.dahai, ...view.buttons.map((button) => button.id)];
        const distinct = sorted([...new Set(ids)]);
        const expected = [...Array(Math.max(view.options, 0)).keys()];
        const kinds = view.buttons.map((button) => button.kind);
        const passes = view.buttons.filter((button) => button.kind === "none");
        const calls = view.buttons.filter((button) => button.kind !== "none");

        if (!same(distinct, expected)) {
          violations.push(
            `点得到的 id 是 [${distinct}]，而引擎这一手给了 ${view.options} 条（该是 [${expected}]）：` +
              "多一枚是页面凭空造的，少一枚是引擎给了他却点不到（票 88 的要害判据）",
          );
        }
        const callIds = view.buttons.map((button) => button.id);
        if (new Set(callIds).size !== callIds.length) {
          violations.push(`牌桌下面那一排里有两枚点下去一样的按钮：[${callIds}]`);
        }
        if (view.buttons.some((button) => !button.inRow)) {
          violations.push("有按钮不在 table-human-calls 那一排里：闸门数得到、人却不知道去哪儿找");
        }
        if (view.buttons.length > 0 && view.rowSaid !== calls.length) {
          violations.push(`那一排说有 ${view.rowSaid} 枚（不含「过」），实际 ${calls.length} 枚`);
        }
        if (view.callsSaid !== calls.length) {
          violations.push(
            `真人那一行说这一手能宣言 ${view.callsSaid} 条，页面上有 ${calls.length} 枚`,
          );
        }
        if (new Set(view.dahai).size !== view.playableSaid) {
          violations.push(
            `页面上点得到 ${new Set(view.dahai).size} 条不同的打牌动作，而那一行说 ${view.playableSaid} 条`,
          );
        }

        if (view.state === "respond") {
          if (passes.length !== 1) {
            violations.push(
              `响应那一手上「过」有 ${passes.length} 枚（该正好 1 枚）：不点就卡住是最难受的死法`,
            );
          }
          if (view.dahai.length > 0) {
            violations.push(`他家打了牌等他响应，自家手牌上却有 ${view.dahai.length} 张点得动`);
          }
          const impossible = kinds.filter((kind) =>
            ["reach", "ankan", "kakan", "ryukyoku", "dahai"].includes(kind),
          );
          if (impossible.length > 0) {
            violations.push(`响应那一手上冒出了只有自己摸完牌才做得了的：${impossible.join("、")}`);
          }

          // 从规则来的那两条（判据 8：期望值取自规则，不取自被检查那句话的来源）。
          const from = discarded();
          if (from === null) {
            violations.push(`响应那一手上读不出上一手是谁打了什么：「${latest()}」`);
          } else {
            const named = { chi: "吃", pon: "碰", daiminkan: "大明杠", hora: "荣和" };
            for (const button of view.buttons) {
              const name = named[button.kind];
              if (name !== undefined && !button.label.startsWith(`${name}${from.pai}`)) {
                violations.push(
                  `「${button.label}」说的不是刚打出的那张（${from.pai}）：` +
                    "吃 / 碰 / 大明杠 / 荣和都只能对刚打出的那一张",
                );
              }
              if (button.kind === "chi" && (from.seat + 1) % 4 !== 0) {
                violations.push(
                  `座位 ${from.seat} 打的牌，座位 0 却能吃：**只有下家能吃**（Action.Chi 那段注释）`,
                );
              }
            }
          }
        } else if (view.state === "waiting" || view.state === "reach") {
          if (passes.length !== 0) {
            violations.push(
              `该他出牌的那一手上有 ${passes.length} 枚「过」：那一条只在响应阶段合法`,
            );
          }
          const impossible = kinds.filter((kind) => ["chi", "pon", "daiminkan"].includes(kind));
          if (impossible.length > 0) {
            violations.push(`该他出牌的那一手上冒出了鸣牌按钮：${impossible.join("、")}`);
          }
          if (view.dahai.length === 0) {
            violations.push("轮到他出牌，手里却一张点不动");
          }
          if (view.state === "reach" && calls.length > 0) {
            violations.push(
              `立直宣言之后那一手还摆着 ${calls.length} 枚宣言按钮：那一手只该选宣言牌`,
            );
          }
        } else {
          violations.push(`轮到他了，真人那一行却写着「${view.state}」`);
        }
      };

      // 先在微任务里抢答，拖久了退到宏任务（别把事件循环饿死：等模型那一档要它）。
      const breathe = (attempt) => {
        if (attempt < 8) return Promise.resolve();
        if (attempt < 64) return new Promise((done) => setTimeout(done, 0));
        return new Promise((done) => setTimeout(done, 8));
      };

      const until = async (done) => {
        const deadline = performance.now() + budgetMs;
        let attempt = 0;
        while (!done()) {
          if (performance.now() > deadline) return false;
          await breathe(attempt);
          attempt += 1;
        }
        return true;
      };

      // 一次点击有没有落下去。**光看「上一手」那句话不够**：连着两次「过」它一个字不变，
      // 因此把真人那一行的几个计数一起算进签名（按一次「过」，`data-human-passes` 就 +1）。
      const signature = () => {
        const view = surface();
        return [
          latest(),
          view.state,
          view.options,
          view.playableSaid,
          view.passesSaid,
          view.buttons.map((button) => button.id).join(","),
        ].join("|");
      };

      const clicks = { dahai: 0, pass: 0, calls: {} };
      const offers = {};
      // 立直宣言那一枚点下去之后，紧接着的那一手长什么样（票 88 的「立直是两段」）。
      const stages = [];
      // 响应阶段**替他按一次「单步」**：那一下不许把这一手推走（见下）。
      let probed = false;
      let steps = 0;
      let kyokus = 1;
      let checks = 0;
      let stuck = null;
      const violations = [];

      for (let move = 0; move < limit; move += 1) {
        if (at("table-result") !== null) break;

        const view = surface();
        const mine = view.buttons.length > 0 || view.dahai.length > 0;

        if (mine) {
          checks += 1;
          audit(view, violations);

          for (const kind of new Set(view.buttons.map((button) => button.kind))) {
            offers[kind] = (offers[kind] ?? 0) + 1;
          }

          // **他在想的时候，谁也替他做不了决定**（票 88 换掉了票 87 的自动过）：
          // 响应阶段头一次碰上时，先替他按一记「单步」——牌桌一手都不许动。
          // **这一条要按在响应阶段上**：票 87 那条只验过「他该出牌那一手」，
          // 而自动过恰恰只发生在响应阶段（红-3 就是从这个洞里逃出去的）。
          if (!probed && view.state === "respond") {
            probed = true;
            const step = at("table-step");
            const before = signature();

            if (step === null || step.disabled) {
              violations.push("轮到他响应时「单步」那一枚不在（或灰着）：这一条因此什么都没证明");
            } else {
              step.click();
              steps += 1;
              await new Promise((done) => setTimeout(done, 150));

              if (signature() !== before) {
                violations.push(
                  "他还在想，按一下「单步」牌桌就自己走了：吃碰杠与「过」都该由他自己点，" +
                    "平台不许替他做决定（票 88 换掉了票 87 的「响应阶段一律自动过」）",
                );
              }
            }
          }

          // 挑一枚：能和就和；第一程鸣得了就鸣；否则「过」；再否则点手牌。
          const byKind = (kind) => view.buttons.find((button) => button.kind === kind);
          const wanted =
            byKind("hora") ??
            (policy === "naki"
              ? (byKind("pon") ??
                byKind("daiminkan") ??
                byKind("chi") ??
                byKind("ankan") ??
                byKind("kakan"))
              : byKind("reach")) ??
            byKind("none");

          const before = signature();
          let picked = null;

          // **要点的那一枚必须还在**：刚才那一记「单步」若把这一手推走了，它就没了
          // ——那正是「平台替他做了决定」，报出来而不是让脚本自己炸掉（合并跑的那个入口
          // 要的是一份失败清单，抛出去会把十五趟一起搞挂）。
          const target =
            wanted !== undefined && wanted !== null
              ? document.querySelector(`[data-human-action-id="${wanted.id}"]`)
              : document.querySelector("[data-dahai-id]");

          if (target === null) {
            violations.push(
              `刚还轮到他（${view.state}，${view.options} 条），要点的那一枚转眼就没了：` +
                "这一手被别人推走了",
            );
            continue;
          }

          if (wanted !== undefined && wanted !== null) {
            picked = `第 ${wanted.id} 条「${wanted.label}」`;
            if (wanted.kind === "none") clicks.pass += 1;
            else clicks.calls[wanted.kind] = (clicks.calls[wanted.kind] ?? 0) + 1;
          } else {
            picked = `手里那张（包内 id ${target.getAttribute("data-dahai-id")}）`;
            clicks.dahai += 1;
          }

          target.click();

          if (!(await until(() => signature() !== before || at("table-fault") !== null))) {
            stuck = `点了${picked}之后牌桌没走动`;
            break;
          }

          // **立直是两段**：宣言之后仍旧是他这一手，而那一手只该选宣言牌。
          if (wanted !== undefined && wanted !== null && wanted.kind === "reach") {
            const second = surface();
            stages.push({
              state: second.state,
              before: new Set(view.dahai).size,
              after: new Set(second.dahai).size,
              calls: second.buttons.filter((button) => button.kind !== "none").length,
              pass: second.buttons.filter((button) => button.kind === "none").length,
            });
          }
          continue;
        }

        const step = at("table-step");
        if (step === null) {
          stuck = "页面上没有「单步」那一枚";
          break;
        }

        if (step.disabled) {
          const next = at("table-next");
          if (next === null || next.disabled) break; // 终局：「下一局」也灰了
          next.click();
          kyokus += 1;
          if (
            !(await until(
              () =>
                !at("table-step").disabled || document.querySelector("[data-dahai-id]") !== null,
            ))
          ) {
            stuck = `点了「下一局」（第 ${kyokus} 局）之后牌桌没开动`;
            break;
          }
          continue;
        }

        const before = signature();
        step.click();
        steps += 1;

        const landed = await until(
          () =>
            signature() !== before ||
            at("table-fault") !== null ||
            at("table-step").disabled ||
            document.querySelector("[data-dahai-id]") !== null ||
            document.querySelector("[data-human-action-id]") !== null,
        );
        if (!landed) {
          stuck = `按了「单步」之后 ${budgetMs}ms 里牌桌没走动（上一手：${before}）`;
          break;
        }
        if (at("table-fault") !== null) break;
      }

      return {
        clicks,
        offers,
        stages,
        steps,
        kyokus,
        checks,
        violations,
        stuck,
        ended: at("table-result") !== null,
      };
    },
    { limit, budgetMs, policy },
  );
}

/** 两本按种类记的账合成一本。 */
function merge(left, right) {
  const total = { ...left };
  for (const [kind, times] of Object.entries(right)) total[kind] = (total[kind] ?? 0) + times;
  return total;
}

/** 一本按种类记的账印出来（`碰 6 次、吃 3 次`）；空账是空串。 */
function count(tally) {
  const named = {
    chi: "吃",
    pon: "碰",
    daiminkan: "大明杠",
    ankan: "暗杠",
    kakan: "加杠",
    reach: "立直",
    hora: "和了",
    ryukyoku: "九种九牌",
    none: "过",
  };
  return Object.entries(tally)
    .map(([kind, times]) => `${named[kind] ?? kind} ${times} 次`)
    .join("、");
}

/** 一趟驱动点了些什么。 */
function tally(walked) {
  const said = count(walked.clicks.calls);
  return (
    `真人点了 ${walked.clicks.dahai} 张手牌、按了 ${walked.clicks.pass} 次「过」` +
    `${said === "" ? "" : `、宣言 ${said}`}`
  );
}

/** 一串重复的失败按原文归并（一整场里同一种错会刷几十遍）。 */
function tallyBy(lines) {
  const found = {};
  for (const line of lines) found[line] = (found[line] ?? 0) + 1;
  return found;
}

/**
 * **整页 HTML 里的每一个 `data-pai`**（票面原话：连 `data-*` 都不许有）。
 *
 * 读的是 `page.content()` 那一整份序列化文档，不是几个选择器捞出来的那几处
 * ——「他家的手牌一张都不在里面」这句话只有对整页说才算数。
 */
async function paiInDocument(page) {
  const html = await page.content();
  // **先把样式表挡掉**：`styles.css` 里每一张牌面都有一条 `.tile[data-pai="1m"]`
  // （牌面 SVG 就是按它贴的），vite 的 dev server 又把 CSS 内联成 `<style>`
  // ——不挡的话这一条会恒红 39 行，而那是样式不是牌。
  // **只挡这一种**：`<style>` 里的东西不是局面数据，其余整页一律算数。
  const body = html.replace(/<style[\s\S]*?<\/style>/g, "");
  return {
    pai: [...body.matchAll(/data-pai="([^"]*)"/g)].map((each) => each[1]).sort(),
    // **整页只许有 `data-pai` 这一种带牌的属性**（票 88）：新按钮若另起一个
    // `data-call-pai` 之类，上面那份预算就漏得一干二净而这一条不会红。
    attributes: [...new Set([...body.matchAll(/(data-[a-z-]*pai)="/g)].map((each) => each[1]))],
  };
}

/**
 * 这一屏上**观测者本来就看得见**的那些牌：自家手牌 + 四家的河 + 四家的副露 + 宝牌指示牌。
 *
 * 它是上面那一份的**预算**：两份逐个相同才算「没泄露」。多出来的每一张都要报出来
 * ——把投影换成上帝视角时，多出来的正好是他家那三手暗牌。
 */
function visiblePai(page, viewer) {
  return page.evaluate((viewer) => {
    const from = (selector) =>
      [...document.querySelectorAll(selector)].map((node) => node.getAttribute("data-pai"));

    const seen = [...from(`[data-testid="seat-${viewer}-hand"] [data-pai]`)];
    for (const seat of [0, 1, 2, 3]) {
      seen.push(...from(`[data-testid="seat-${seat}-kawa"] [data-pai]`));
      seen.push(...from(`[data-testid="seat-${seat}-naki"] [data-pai]`));
    }
    seen.push(...from('[data-testid="table-dora"] [data-pai]'));
    return seen.sort();
  }, viewer);
}

/** 他家那一席的手牌行此刻是什么样（张数、牌背数、露没露牌面）。 */
function handShape(page, seat) {
  return page.evaluate((seat) => {
    const row = document.querySelector(`[data-testid="seat-${seat}-hand"]`);
    if (row === null) return null;
    return {
      count: row.getAttribute("data-hand-count"),
      hidden: row.getAttribute("data-hand-hidden"),
      faces: row.querySelectorAll("[data-pai]").length,
      backs: row.querySelectorAll(".tile.back").length,
    };
  }, seat);
}

/** 一份多重集减法：`left` 里有而 `right` 里没有的那几项（重复也算）。 */
function extras(left, right) {
  const pool = [...right];
  const found = [];
  for (const each of left) {
    const index = pool.indexOf(each);
    if (index < 0) found.push(each);
    else pool.splice(index, 1);
  }
  return found;
}

/** 第一程：真人坐一席，把一整场东风战打完。返回提前中止的失败清单（null = 接着往下走）。 */
async function tableLane(lane, pageOrigin, options) {
  const { budgetMs, peek, missing, problems } = options;
  const model = await startEndpoint(pageOrigin);
  const context = await lane.newContext();

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror] ${error.message}`));

    // 坐法只从 localStorage 来（票 73 之后是**档案库 + 座位绑定**）：
    // 座位 0 是「我自己」，座位 1 引用那份自定义端点的档案（**不带 key**），座位 2/3 是 bot。
    await plantSeating(page, {
      profiles: [
        {
          name: PROFILE,
          provider: "custom-openai",
          model: "fake-model",
          base_url: model.baseUrl,
          timeout_ms: "10000",
        },
      ],
      seats: [{ choice: "human" }, { choice: profileChoice(PROFILE) }, {}, {}],
    });

    console.log(
      `页面 ${pageOrigin}　座位 ${ME} 是我自己，座位 ${MODEL} ← ${model.baseUrl}，座位 2/3 是 bot`,
    );
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });

    // ---- ① 视角按钮不在 DOM 里 ----
    const godCount = await page.getByTestId("table-view-god").count();
    const mineCount = await page.getByTestId(`table-view-${ME}`).count();
    const othersCount = await page.locator('[data-testid^="table-view-"]').count();
    const lockedAt = await attr(page, "table-view-locked", "data-view-locked");

    console.log("");
    console.log(
      `视角那一排：上帝 ${godCount} 枚、自家 ${mineCount} 枚、一共 ${othersCount} 枚（含那句「锁着」），锁在座位 ${lockedAt}`,
    );

    if (godCount !== 0) {
      missing.push(
        "真人在座、这一场还没打完，「上帝视角」那一枚却还在 DOM 里：" +
          "灰掉不算数——票 81 把视角定成了信息闸门，一行 DevTools 就能把 disabled 平掉",
      );
    }
    if (mineCount !== 1) {
      missing.push(
        `自家那一枚视角按钮有 ${mineCount} 枚（该正好 1 枚）：整排消失会让人以为页面坏了`,
      );
    }
    for (const seat of [1, 2, 3]) {
      const count = await page.getByTestId(`table-view-${seat}`).count();
      if (count !== 0) missing.push(`别席（座位 ${seat}）的视角按钮还在 DOM 里（${count} 枚）`);
    }
    if (lockedAt !== String(ME)) {
      missing.push(
        `页面没说清视角锁在哪一席（data-view-locked=「${lockedAt}」，该是 ${ME}）：` +
          "「那几枚按钮本来就没有」与「页面坏了」在屏幕上长得一模一样",
      );
    }

    // ---- ② 点自己手里的一张就打出去 ----
    const state = await attr(page, "table-human", "data-human");

    if (state === null) {
      // 真人那一行整行不在（`humanSeated` 没说真话）：底下每一条都会变成 null 对 null。
      return failure("真人坐席这一道没过：", [
        `座位 ${ME} 拨的是「我自己」，页面上却没有真人那一行（[data-testid="table-human"]）：` +
          "这一桌根本不知道桌边坐了人，底下那十几条都在空转",
      ]);
    }

    const playableSaid = Number.parseInt(
      (await attr(page, "table-human", "data-human-playable")) ?? "-1",
      10,
    );
    const dahai = await page.locator("[data-dahai-id]").count();
    const mineDahai = await page.locator(`[data-testid="seat-${ME}-hand"] [data-dahai-id]`).count();
    // **数的是不同的 id，不是点得动的牌数**：手里两张 3 索是两张牌、却只是
    // **一条**动作（手切 3 索），两张都挂同一个 id。这一条要铉的是
    // 「页面上点得到的动作集 = 引擎给的那一份」。
    const ids = await page.evaluate(
      () =>
        [
          ...new Set(
            [...document.querySelectorAll("[data-dahai-id]")].map((n) => n.dataset.dahaiId),
          ),
        ].length,
    );

    console.log(
      `真人那一行：${state}　它说点得出去 ${playableSaid} 条，页面上有 ${dahai} 张点得动（${ids} 条不同的动作）`,
    );
    console.log(`　「${await text(page, "table-human")}」`);

    if (state !== "waiting") {
      missing.push(`一打开这一页就该轮到真人出牌（他是东 1 局的亲），实际 data-human=「${state}」`);
    }
    if (playableSaid <= 0) {
      missing.push(`真人那一行说这一手点得出去 ${playableSaid} 条：那一条断言在空转`);
    }
    if (ids !== playableSaid) {
      missing.push(
        `页面上点得到 ${ids} 条不同的打牌动作，而合法动作集说该有 ${playableSaid} 条：` +
          "能点哪几张只许由引擎的合法动作集定（spec：合法性驱动 UI）",
      );
    }
    if (mineDahai !== dahai) {
      missing.push(`自家手牌之外还有 ${dahai - mineDahai} 张牌点得动`);
    }
    for (const seat of [1, 2, 3]) {
      const theirs = await page
        .locator(`[data-testid="seat-${seat}-hand"] [data-dahai-id]`)
        .count();
      if (theirs !== 0) missing.push(`他家（座位 ${seat}）的手牌上竟有 ${theirs} 张点得动`);
    }

    // **点下去的就是打出去的那一张**：上面只数了条数，而「id 与牌对不上」数得再准也看不出来。
    // 这一下把整条链走完：页面上那张牌 → 包内 id → 引擎落定的那一手 → 自家河里多出来的那一张。
    const picked = await page.evaluate(() => {
      const tile = document.querySelector(`[data-testid="seat-0-hand"] [data-dahai-id]`);
      return tile === null
        ? null
        : { pai: tile.getAttribute("data-pai"), id: tile.dataset.dahaiId };
    });
    const kawaBefore = await page.locator(`[data-testid="seat-${ME}-kawa"] [data-pai]`).count();
    await page.locator(`[data-testid="seat-${ME}-hand"] [data-dahai-id]`).first().click();
    const landed = await page.evaluate(
      (seat) =>
        [...document.querySelectorAll(`[data-testid="seat-${seat}-kawa"] [data-pai]`)]
          .map((node) => node.getAttribute("data-pai"))
          .at(-1) ?? null,
      ME,
    );
    const kawaAfter = await page.locator(`[data-testid="seat-${ME}-kawa"] [data-pai]`).count();

    console.log(
      `点了手里那张 ${picked?.pai}（包内 id ${picked?.id}）：自家河 ${kawaBefore} → ${kawaAfter} 张，末尾是 ${landed}`,
    );

    if (kawaAfter !== kawaBefore + 1) {
      missing.push(`点了一张手牌，自家的河却从 ${kawaBefore} 变成 ${kawaAfter} 张（该多一张）`);
    }
    if (landed !== picked?.pai) {
      missing.push(
        `点的是 ${picked?.pai}，打出去的却是 ${landed}：这一张牌上那个包内 id 指错了人` +
          "（数条数数得再准也看不出这一条）",
      );
    }

    // ---- 走一段，让模型开过口、也让鸣牌那几枚按钮有机会出现 ----
    const walked = await driveHuman(page, { limit: peek, budgetMs, policy: "naki" });
    console.log("");
    console.log(
      `走了一段：${tally(walked)}，按了 ${walked.steps} 次单步、打到第 ${walked.kyokus} 局` +
        `${walked.stuck === null ? "" : `　卡住：${walked.stuck}`}`,
    );
    if (walked.stuck !== null) {
      return failure("真人坐席这一道没过：", [`这一桌推不动了：${walked.stuck}`]);
    }
    if (walked.clicks.dahai === 0) {
      return failure("真人坐席这一道没过：", [
        "走了一段，真人一次都没点过手牌：下面每一条都在空转",
      ]);
    }

    // ---- ③ 结构性不泄露（story 29） ----
    const document = await paiInDocument(page);
    const inDocument = document.pai;
    const budget = await visiblePai(page, ME);
    const leaked = extras(inDocument, budget);

    const strange = document.attributes.filter((name) => name !== "data-pai");
    if (strange.length > 0) {
      missing.push(
        `整页上除了 data-pai 还有别的属性带着牌：${strange.join("、")}` +
          "——那份预算只数 data-pai，多一种就等于开了一条量不到的缝（票 88 的新按钮走的是 label，不带牌）",
      );
    }

    console.log("");
    console.log(
      `整页 HTML 里有 ${inDocument.length} 个 data-pai，观测者本来就看得见的有 ${budget.length} 个`,
    );

    if (leaked.length > 0) {
      missing.push(
        `对局中整页 HTML 里多出 ${leaked.length} 个他不该看得见的 data-pai：${leaked.join("、")}` +
          "——他家的手牌一张都不许在里面，连 data-* 都不许有（spec 的 story 29）",
      );
    }
    if (budget.length > inDocument.length) {
      missing.push(
        `预算（${budget.length}）比整页（${inDocument.length}）还多：这一条量错了，先查它自己`,
      );
    }

    const mine = await handShape(page, ME);
    console.log(`自家手牌行：${mine.count} 张，露着 ${mine.faces} 张、扣着 ${mine.backs} 张`);

    if (mine.hidden !== "false" || mine.faces === 0) {
      missing.push(
        `自家的手牌反而看不见了（data-hand-hidden=${mine.hidden}，露着 ${mine.faces} 张）`,
      );
    }

    for (const seat of [1, 2, 3]) {
      const shape = await handShape(page, seat);
      console.log(
        `座位 ${seat} 的手牌行：${shape.count} 张，露着 ${shape.faces} 张、扣着 ${shape.backs} 张`,
      );

      if (shape.hidden !== "true" || shape.faces !== 0) {
        missing.push(
          `座位 ${seat} 的手牌在页面上露了 ${shape.faces} 张（data-hand-hidden=${shape.hidden}）：` +
            "他家的暗牌在投影里根本不该存在（`MaskedSeat` 没有手牌字段）",
        );
      }
      if (shape.backs !== Number.parseInt(shape.count, 10)) {
        missing.push(`座位 ${seat} 说有 ${shape.count} 张手牌，却画了 ${shape.backs} 张牌背`);
      }
    }

    const ura = await page.getByTestId("table-uradora").count();
    if (ura !== 0) missing.push("对局中桌心摆着里宝牌指示牌：那是上帝视角才有的东西");

    // ---- ④ 气泡对局中一个都没有（阳性对照：模型真被问过） ----
    const bubbles = await page.locator('[data-testid$="-bubble"]').count();
    const agentSaid = (await text(page, "table-agent")) ?? "";
    // **阳性对照走账单而不是状态线**：真人坐在座位 0 上，而视角同样拦着状态线
    // （票 81：气泡与状态线同一条规则）——他本来就不该看见座位 1 说了什么。
    // 而 token 账单不按视角变：它 > 0 就证明那一席模型**真的被问过话**，
    // 于是「0 个气泡」量的不是一桌没人开口的空局。
    const tokens = Number.parseInt(
      (await attr(page, "table-usage", "data-prompt-tokens")) ?? "0",
      10,
    );

    console.log("");
    console.log(`对局中的气泡：${bubbles} 个　账单 ${tokens} tok　Agent 那一行：${agentSaid}`);

    if (!(tokens > 0)) {
      missing.push(
        `这一桌至今一个 prompt token 都没花（${tokens}）：座位 ${MODEL} 那一席模型根本没被问过，` +
          "下面那条「0 个气泡」因此什么都没证明",
      );
    }
    if (agentSaid.includes(SAID)) {
      missing.push(
        `坐在座位 ${ME} 上，Agent 那一行却写着座位 ${MODEL} 的理由：「${agentSaid}」` +
          "——气泡拦住了而状态线漏了，那闸门就只是个摆设（票 81）",
      );
    }
    if (bubbles !== 0) {
      missing.push(
        `有真人在座、对局还没打完，页面上却有 ${bubbles} 个思考气泡：` +
          "AI 的推理会向同桌的真人泄露它的手牌（spec 的 story 31）",
      );
    }

    // ---- ⑥ 把一整场东风战打完 ----
    const rest = await driveHuman(page, { limit: 4000, budgetMs, policy: "naki" });
    console.log("");
    console.log(
      `打到底：${tally(rest)}，按了 ${rest.steps} 次单步，共 ${rest.kyokus} 局，终局=${rest.ended}` +
        `${rest.stuck === null ? "" : `　卡住：${rest.stuck}`}`,
    );

    // ---- ②' 每一枚按钮背后都是一条引擎给的 id（每一次出手都核过一遍） ----
    const checks = walked.checks + rest.checks;
    const spotted = [...walked.violations, ...rest.violations];
    console.log(
      `他这一场出手 ${checks} 次，每一次都核过「按钮 = 引擎给的那一包」，违反 ${spotted.length} 条`,
    );
    console.log(`　碰上过：${count(merge(walked.offers, rest.offers))}`);

    // 逐条报出来（重复的只报一次，后面跟着次数：一整场里同一种错会刷几十遍）。
    for (const [line, times] of Object.entries(tallyBy(spotted))) {
      missing.push(times === 1 ? line : `${line}（这一场里 ${times} 次）`);
    }
    if (!(checks > 20)) {
      missing.push(`他这一场只出手 ${checks} 次：上面那些断言基本没开过口（判据 3）`);
    }

    if (rest.stuck !== null) missing.push(`这一桌推不动了：${rest.stuck}`);
    if (!rest.ended) {
      missing.push("这一场没走到终局：真人坐一席就该照样打得完（spec 的 story 28）");
      return null;
    }

    const scores = await page.evaluate(() =>
      [0, 1, 2, 3].map((seat) =>
        Number.parseInt(
          document
            .querySelector(`[data-testid="seat-${seat}-score"]`)
            ?.getAttribute("data-score") ?? "NaN",
          10,
        ),
      ),
    );
    const total = scores.reduce((sum, each) => sum + each, 0);

    console.log(`终局点数：${scores.join(" / ")}　合计 ${total}`);
    console.log(`终局精算：${await text(page, "table-result-ranking")}`);

    if (total !== 100000) {
      missing.push(`终局四家点数之和是 ${total}（该恒为 100000）：${scores.join(" / ")}`);
    }

    // ---- ⑤ 鸣牌是他自己点的（票 87 那时是平台替他过的，票 88 换成了真按钮） ----
    // **量在打完之后**（判据 3）：开局头几十手里可能一次鸣牌机会都没碰上，
    // 那时候量它就是一条永远执行不到的断言；一整场下来实测稳定在十几到二十几次机会。
    const naki = merge(walked.clicks.calls, rest.clicks.calls);
    const nakiTimes = Object.values(naki).reduce((sum, each) => sum + each, 0);
    const settledState = await attr(page, "table-human", "data-human");
    const saidNow = (await text(page, "table-human")) ?? "";

    console.log(
      `他这一场自己点的宣言：${count(naki) || "一次都没有"}（真人那一行：${settledState}）`,
    );
    console.log(`　「${saidNow}」`);

    if (settledState !== "settled") {
      missing.push(
        `终局那一屏上真人那一行还写着「${settledState}」：那一刻既不轮到他、也不轮到别人，` +
          "而那正是视角与气泡一起松开的那一刻——不说的话人不知道刚才藏着的现在看得了",
      );
    }

    if (!(nakiTimes > 0)) {
      missing.push(
        "一整场下来他一次吃 / 碰 / 杠都没点成（这一程的代点是「鸣得了就鸣」）：" +
          "要么那几枚按钮根本没出现过，要么点下去牌局没认——两种都是票 88 的本体断了",
      );
    }

    // ---- ⑦ 终局之后三样一起松开 ----
    const godBack = await page.getByTestId("table-view-god").count();
    const lockedStill = await page.getByTestId("table-view-locked").count();
    const bubbleBack = await page.getByTestId(`seat-${MODEL}-bubble`).count();

    console.log("");
    console.log(
      `终局之后：上帝视角 ${godBack} 枚、那句「锁着」${lockedStill} 句、座位 ${MODEL} 的气泡 ${bubbleBack} 个`,
    );

    if (godBack !== 1)
      missing.push(`终局之后「上帝视角」那一枚没回来（${godBack} 枚）：复盘本来就该看得见四家`);
    if (lockedStill !== 0) missing.push("终局之后页面还写着「视角锁着」");
    if (bubbleBack !== 1) {
      missing.push(
        `终局之后座位 ${MODEL} 的思考气泡没回来（${bubbleBack} 个）：` +
          "上面那条「对局中 0 个气泡」因此可能只是气泡整个坏了",
      );
    } else {
      // **并且里面写着它当时说的那句话**：这才把「对局中藏着、终局后放出来」
      // 铉成同一句话的两面——那句话只可能从那个假端点那儿来。
      const said = (await text(page, `seat-${MODEL}-bubble`)) ?? "";
      console.log(`　座位 ${MODEL} 的气泡：${said}`);

      if (!said.includes(SAID)) {
        missing.push(`终局后座位 ${MODEL} 的气泡里不是它端点回的那句：「${said}」`);
      }
    }

    for (const seat of [1, 2, 3]) {
      const count = await page.getByTestId(`table-view-${seat}`).count();
      if (count !== 1) missing.push(`终局之后座位 ${seat} 的视角按钮没回来（${count} 枚）`);
    }
  } finally {
    await context.close();
    model.endpoint.kill();
  }
  return null;
}

/**
 * 第二程：真人 + 三家 bot、**种子写死**、门清立直那一套代点（能和就和、能立直就立直、
 * 其余一律「过」）。
 *
 * **为什么要单开一程**：第一程「鸣得了就鸣」的那一桌手是开的，立直与「过」在那儿都碰不到；
 * 而立直两段与「过」的记账正是这一票的两条要害。**一整场也只多 1 秒上下**。
 *
 * **种子是探针扫出来的**（一次性脚本，没进仓库；扫法与结论写在报告 88 里）：
 * 同一颗种子 + 同一套代点必然跑出同一场，因此「这一场里他真的立过一次直、和过一次」
 * 是可复现的——碰不到就是红，而红了说明代点或引擎变了，正该有人来看。
 */
const CLOSED_SEED = 427;

async function closedLane(lane, pageOrigin, options) {
  const { budgetMs, missing, problems } = options;
  const context = await lane.newContext();

  try {
    const page = await context.newPage();
    page.on("pageerror", (error) => problems.push(`[pageerror 第二程] ${error.message}`));
    await plantSeating(page, { profiles: [], seats: [{ choice: "human" }, {}, {}, {}] });
    await page.goto(hostPage(pageOrigin), { waitUntil: "load" });

    // 种子写死：输入框 + 「重开」就是主持人自己开一桌的那条路（真人在座时曳光弹不给开，
    // 而那一块才是 `?dev=1` 的种子框——这里用的是配桌那一排上的）。
    await page.getByTestId("table-seed").fill(String(CLOSED_SEED));
    await page.getByTestId("table-restart").click();

    console.log("");
    console.log(
      `第二程：种子 ${CLOSED_SEED}，真人坐座位 ${ME}、其余三家 bot，一律「过」、能立直就立直`,
    );

    const walked = await driveHuman(page, { limit: 4000, budgetMs, policy: "closed" });
    console.log(
      `　${tally(walked)}，按了 ${walked.steps} 次单步，共 ${walked.kyokus} 局，终局=${walked.ended}`,
    );
    console.log(`　碰上过：${count(walked.offers)}`);

    if (walked.stuck !== null) {
      missing.push(`第二程推不动了：${walked.stuck}`);
      return;
    }

    for (const [line, times] of Object.entries(tallyBy(walked.violations))) {
      missing.push(times === 1 ? `第二程：${line}` : `第二程：${line}（这一场里 ${times} 次）`);
    }

    // ---- ⑩ 「过」是他自己按的，而页面记的次数与他按的次数逐次对得上 ----
    const said = (await text(page, "table-human")) ?? "";
    const passes = Number.parseInt(
      (await attr(page, "table-human", "data-human-passes")) ?? "-1",
      10,
    );

    console.log(
      `　页面记着他按了 ${passes} 次「过」，他真按了 ${walked.clicks.pass} 次：「${said}」`,
    );

    if (!(walked.clicks.pass > 0)) {
      missing.push(
        "一整场下来他一次「过」都没按成：要么响应阶段根本没停下来等他（票 87 那条自动过还在），" +
          "要么那一枚按钮不在（不点就卡住是最难受的死法）",
      );
    }
    if (passes !== walked.clicks.pass) {
      missing.push(
        `他按了 ${walked.clicks.pass} 次「过」，页面却记着 ${passes} 次：` +
          "这本账是复盘（票 90）第一件要问的事，记错了比不记更糟",
      );
    }
    if (walked.clicks.pass > 0 && !said.includes("你按了「过」")) {
      missing.push(`他按了 ${walked.clicks.pass} 次「过」，页面上却一个字都没说：「${said}」`);
    }

    // ---- ⑪ 立直是两段 ----
    const reached = walked.clicks.calls.reach ?? 0;
    console.log(
      `　立直：碰上 ${walked.offers.reach ?? 0} 次、宣言 ${reached} 次；两段那一手核了 ${walked.stages.length} 回`,
    );

    if (reached === 0) {
      missing.push(
        `种子 ${CLOSED_SEED} 这一场里他一次立直都没宣言（碰上 ${walked.offers.reach ?? 0} 次）：` +
          "「立直两段」那一条因此一次都没执行到（判据 3）——代点或引擎变了就该重扫种子",
      );
    }
    for (const stage of walked.stages) {
      if (stage.state !== "reach") {
        missing.push(
          `立直宣言之后那一手写着「${stage.state}」，该是 reach（引擎说他此刻是「宣言了还没落定」）`,
        );
      }
      if (stage.calls !== 0 || stage.pass !== 0) {
        missing.push(
          `立直宣言之后那一手还摆着 ${stage.calls} 枚宣言按钮、${stage.pass} 枚「过」：那一手只该选宣言牌`,
        );
      }
      if (!(stage.after > 0)) {
        missing.push("立直宣言之后一张牌都点不动：宣言牌选不出来，这一局就卡死了");
      }
      if (!(stage.after < stage.before)) {
        missing.push(
          `立直宣言前点得动 ${stage.before} 条、宣言后 ${stage.after} 条：那一集没收窄，` +
            "「只有打完仍听牌的那几张」这句话就没有执行者（判据 2）",
        );
      }
    }

    // ---- ⑫ 他真的和了一次 ----
    const horas = walked.clicks.calls.hora ?? 0;
    const ranking = (await text(page, "table-result-ranking")) ?? "";
    console.log(
      `　和了：碰上 ${walked.offers.hora ?? 0} 次、点了 ${horas} 次；终局精算：${ranking}`,
    );

    if (horas === 0) {
      missing.push(
        `种子 ${CLOSED_SEED} 这一场里他一次和了都没点成：` +
          "荣和 / 自摸那两枚按钮因此一次都没被真的按下去过（判据 3）",
      );
    }
    if (!walked.ended) missing.push("第二程没走到终局");
  } finally {
    await context.close();
  }
}

/** 第三程：`?dev=1` 的阴阳对照（挂账 22-A）。 */
async function devLane(lane, pageOrigin, options) {
  const { missing, problems } = options;
  const context = await lane.newContext();

  try {
    const shot = async (label, seats) => {
      const page = await context.newPage();
      page.on("pageerror", (error) => problems.push(`[pageerror ${label}] ${error.message}`));
      await plantSeating(page, { profiles: [], seats });
      await page.goto(`${hostPage(pageOrigin)}&dev=1`, { waitUntil: "load" });

      const found = {
        seed: await page.getByTestId("seed-input").count(),
        traces: await page.getByTestId("traces").count(),
        board: await page.getByTestId("table-board").count(),
      };
      console.log(
        `${label}：seed-input ${found.seed} 个、traces ${found.traces} 个、牌桌 ${found.board} 张`,
      );
      return found;
    };

    console.log("");
    const seated = await shot("真人在座 + ?dev=1", [{ choice: "human" }, {}, {}, {}]);
    const empty = await shot("没有真人 + ?dev=1（阴性对照）", [{}, {}, {}, {}]);

    if (seated.seed !== 0 || seated.traces !== 0) {
      missing.push(
        `真人在座时 ?dev=1 还是把曳光弹挂了出来（seed-input ${seated.seed} 个、traces ${seated.traces} 个）：` +
          "那一块把 start_kyoku（带着四家配牌）印在同一张文档里，挂账 22-A 说的就是它",
      );
    }
    if (seated.board !== 1) {
      missing.push(
        `真人在座 + ?dev=1 那一页上没有牌桌（${seated.board} 张）：不给开曳光弹不等于把页面弄坏`,
      );
    }
    if (empty.seed !== 1 || empty.traces !== 1) {
      missing.push(
        `没有真人时 ?dev=1 也开不出曳光弹了（seed-input ${empty.seed} 个、traces ${empty.traces} 个）：` +
          "上面那一条因此什么都没证明（可能只是曳光弹整个没了）",
      );
    }
  } finally {
    await context.close();
  }
}

/** 真人坐席那一道。返回的是失败清单（空 = 绿）。 */
export async function verifyHuman(lane, options = {}) {
  const { budgetMs = 120000, peek = 40 } = options;

  // dev server 而不是 preview：与 verify-bubbles 同一个理由（省掉一次 vite build）。
  const pageOrigin = await lane.devUrl();
  const problems = [];
  const missing = [];

  const early = await tableLane(lane, pageOrigin, { budgetMs, peek, missing, problems });
  if (early !== null) return early;

  await closedLane(lane, pageOrigin, { budgetMs, missing, problems });
  await devLane(lane, pageOrigin, { missing, problems });

  if (problems.length > 0) return failure("页面报了错：", problems);
  if (missing.length > 0) return failure("真人坐席这一道没过：", missing);

  console.log("");
  console.log("视角按钮不在 DOM 里、点手牌就打得出去、整页 HTML 里没有他家的一张手牌 ✓");
  console.log(
    "每一次出手都核过：手牌上那几张 + 那一排按钮 + 「过」= 引擎给的那一包，一条不多一条不少 ✓",
  );
  console.log("不合法就不在 DOM 里；吃只来自上家、吃碰杠荣和的那张就是刚打出的那张 ✓");
  console.log("对局中一个气泡都没有（而模型真说过话）、吃碰杠是他自己点的 ✓");
  console.log("「过」是他自己按的且页面记得住次数；立直两段、和了各真点过一次 ✓");
  console.log("真人坐一席把一整场东风战打完，四家点数之和 100000；终局后视角与气泡一起回来 ✓");
  console.log("真人在座时 ?dev=1 的曳光弹不给开，没有真人时照旧开得了 ✓");
  return [];
}

if (isEntry(import.meta.url)) {
  const argv = process.argv.slice(2);
  const flag = (name, fallback) => {
    const index = argv.indexOf(name);
    return index < 0 ? fallback : argv[index + 1];
  };

  await runStandalone((lane) =>
    verifyHuman(lane, {
      budgetMs: Number.parseInt(flag("--budget", "120000"), 10),
      peek: Number.parseInt(flag("--peek", "40"), 10),
    }),
  );
}
