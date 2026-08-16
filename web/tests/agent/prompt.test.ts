/**
 * prompt 渲染（票 23 的 Bare 档 + 票 24 的 Assisted 档）。四件事：**该有的都在**、
 * **Bare 档不该有的一样都没有**（他家暗牌、脚手架数值）、**Assisted 档多出来的就是
 * 那一节数值**、**重试时告诉模型上一次错在哪**。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { renderPrompt } from "../../src/agent/prompt.ts";
import { dahaiPackage, dangerPackage, responsePackage } from "./fixtures.ts";

const bare = renderPrompt(dahaiPackage, "bare", null);

test("局面该有的都在：场况、自家手牌、他家、合法动作", () => {
  const observation = dahaiPackage.observation;

  assert.match(bare, /东1局 0 本场/);
  assert.match(bare, /宝牌指示牌：2s/);
  assert.match(bare, /牌山剩余可摸 66 张/);

  for (const pai of observation.self.tehai) {
    assert.ok(bare.includes(pai), `自家手牌里的 ${pai} 该出现在 prompt 里`);
  }

  for (const option of dahaiPackage.actions) {
    assert.ok(bare.includes(`id=${option.id}：${option.label}`), `动作 ${option.id} 该列出来`);
  }
});

test("副露与摸切标记：都是公开信息，都写出来", () => {
  // 自家有一组碰（3m），他家河里有摸切。
  assert.match(bare, /副露：pon 鸣3m\[3m 3m\]（来自座位 3）/);
  assert.match(bare, /5p\*/, "摸切的那张后面缀 `*`");
});

test("他家的暗牌在 prompt 里根本无从写起——决策包里就没有", () => {
  const others = dahaiPackage.observation.others;

  for (const other of others) {
    assert.equal(
      "tehai" in other,
      false,
      "MaskedSeat 类型里没有 tehai：隐藏信息的保护在结构上成立",
    );
    assert.ok(bare.includes(`手里 ${other.tehai_count} 张`), "他家只给得出张数");
  }
});

test("Bare 档不给任何算好的数：向听、有效牌、危险度一个都没有", () => {
  // 这一档存在的意义就是量「模型自己会不会数牌」。
  // **决策包里有这些数**（包恒带脚手架），档位决定的是写不写进 prompt。
  for (const word of ["向听", "有效牌", "进退向", "危险", "shanten", "ukeire"]) {
    assert.equal(bare.includes(word), false, `Bare 档的 prompt 不该出现「${word}」`);
  }
  assert.ok(
    Object.keys(dahaiPackage.scaffold).length > 0,
    "决策包本身带着脚手架，Bare 档只是不渲染它",
  );
});

test("响应那一手也渲染得出来：没摸牌，动作只有碰与过", () => {
  const prompt = renderPrompt(responsePackage, "bare", null);

  assert.match(prompt, /刚摸进：无（这一手不是你摸牌）/);
  assert.match(prompt, /id=1：过/);
});

test("重试时把上一次错在哪接在末尾", () => {
  const retry = renderPrompt(dahaiPackage, "bare", "action_id=99 不在这一手的合法动作集里");

  assert.ok(retry.startsWith(bare), "重试的 prompt 是原来那份加一段尾巴");
  assert.match(retry, /上一次的回答没有被采用/);
  assert.match(retry, /action_id=99/);
});

test("ToolSearch 暂时照 Bare 渲染：它的工具是 M3 的事", () => {
  assert.equal(renderPrompt(dahaiPackage, "tool_search", null), bare);
});

// ---- Assisted 档（票 24） ----

const assisted = renderPrompt(dahaiPackage, "assisted", null);

test("两档只差一节：局面、他家与可选动作逐字相同", () => {
  // 「同一局面两档 prompt 肉眼可比」是本票的验收：差异必须就是那一节数值，
  // 不能顺手把别的措辞也改了，否则两档的对照就不是一个变量。
  const extra = assisted.split("\n\n").filter((block) => !bare.includes(block));

  assert.equal(extra.length, 1, "Assisted 档只多出一段");
  assert.match(extra[0], /【引擎算好的数】/);
  assert.ok(assisted.length > bare.length);
});

test("Assisted 档：向听数与逐张试打的进退向都写出来", () => {
  // 决策包（`janpo decide 2088 --steps 6`）：3 向听，手切 3 筒（id=3）退向。
  assert.match(assisted, /当前向听数：3 向听/);
  assert.match(assisted, /id=0（手切2万）：打完 3 向听，进退向 0，有效牌 66 枚/);
  assert.match(assisted, /id=3（手切3筒）：打完 4 向听，退向 \+1，有效牌 76 枚/);

  // 有效牌是「牌种 + 剩余枚数」，牌照 mjai 记法（与手牌那一行同一种写法）。
  assert.match(assisted, /3m\(1\) 4m\(4\)/);
});

test("Assisted 档：每一条打牌动作都有它自己那一行", () => {
  for (const option of dahaiPackage.actions) {
    assert.ok(
      assisted.includes(`- id=${option.id}（${option.label}）：打完 `),
      `动作 ${option.id} 该有一条试打`,
    );
  }
});

test("Assisted 档：打不了牌的那一手直接给有效牌", () => {
  // 响应那一手是等摸形（没摸牌），有效牌算得出来，也没有逐张试打。
  const prompt = renderPrompt(responsePackage, "assisted", null);

  assert.match(prompt, /当前向听数：3 向听/);
  assert.match(prompt, /有效牌 28 枚 8 种：1m\(4\)/);
  assert.equal(prompt.includes("逐张试打"), false, "这一手没牌可打");
});

test("Assisted 档：重试的尾巴照样接得上", () => {
  const retry = renderPrompt(dahaiPackage, "assisted", "action_id=99 不在这一手的合法动作集里");

  assert.ok(retry.startsWith(assisted));
  assert.match(retry, /上一次的回答没有被采用/);
});

// ---- 危险度（票 25） ----

const danger = renderPrompt(dangerPackage, "assisted", null);

test("Assisted 档：危险度排序逐张写出来，理由标签照术语表", () => {
  // 决策包（`janpo decide 99 --steps 6 --seat 3`）：对家有副露、河里有 4m。
  assert.match(danger, /危险度排序（有威胁的家：对家有副露）/);
  assert.match(danger, /- 第1位 id=0（手切4万）：现物 —— 对家现物/);
  assert.match(danger, /- 第2位 id=1（手切7万）：筋 —— 对家 4m 筋/);
  assert.match(danger, /- 第3位 id=2（手切3筒）：无依据$/m);
  assert.match(danger, /- 第11位 id=3（手切5筒）：无依据 —— 宝牌 7p 周边/);

  // 赤 5 与正 5 是两条动作、一个牌种：两条各占一行，名次相同。
  assert.match(danger, /- 第11位 id=4（手切赤5筒）：无依据 —— 宝牌 7p 周边/);

  // **它是启发式不是概率**（票里明写）：说清楚这一点，且不写任何会被读成统计结论的数。
  assert.match(danger, /不是概率/);
  for (const banned of ["放铳率", "%", "百分"]) {
    assert.equal(danger.includes(banned), false, `危险度那一节不该出现「${banned}」`);
  }
});

test("Assisted 档：名次跟着引擎走，排序从安全到危险", () => {
  const lines = danger.split("\n").filter((line) => /^- 第\d+位 id=/.test(line));
  const ranks = lines.map((line) => Number(/^- 第(\d+)位/.exec(line)?.[1]));

  assert.equal(lines.length, dangerPackage.actions.length, "每一条打牌动作各一行");
  assert.deepEqual(
    ranks,
    [...ranks].sort((left, right) => left - right),
    "名次不下降",
  );
  assert.equal(ranks[0], 1);
});

test("没人立直也没人副露时不写危险度", () => {
  // 开局第几巡那份包（`decide 2088 --steps 6`）里 `threats` 是空的，
  // 引擎本来就不给排序（DECISIONS 25-1）。
  assert.equal(assisted.includes("危险度排序"), false);
  assert.match(assisted, /逐张试打/, "其余那几项照旧");
});

test("24 号票那份形状的包仍然渲染得出来：少一节不该让整包读不动", () => {
  // 把 `threats` 与逐条 `danger` 摸掉（就是 24 号票当时的形状）。
  const scaffold = dangerPackage.scaffold as { dahai: Record<string, unknown>[] };
  const legacy = {
    ...dangerPackage,
    scaffold: {
      ...dangerPackage.scaffold,
      threats: undefined,
      dahai: scaffold.dahai.map((entry) => ({ ...entry, danger: undefined })),
    },
  };

  const rendered = renderPrompt(legacy, "assisted", null);

  assert.equal(rendered.includes("危险度排序"), false);
  assert.match(rendered, /逐张试打/);
});

test("脚手架读不动时 Assisted 退回 Bare，而不是崩", () => {
  // 23 号票时代录下来的包（`scaffold` 是空对象）仍然渲染得出来：
  // 宁可少给几个数，不能把这一手卡死。
  const legacyPackage = { ...dahaiPackage, scaffold: {} };

  assert.equal(
    renderPrompt(legacyPackage, "assisted", null),
    renderPrompt(legacyPackage, "bare", null),
  );
});
