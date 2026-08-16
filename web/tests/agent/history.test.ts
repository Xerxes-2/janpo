/**
 * 座席历史那一段的渲染（票 29b）：**一条掩蔽事件一行中文短句**。
 *
 * 这里钉的是**票面点名要「在 prompt 里真的看得见」的那几样**：立直宣言牌与宣言巡目、
 * 加杠宣言与抢杠窗口、河与鸣的时序对齐（哪张牌在第几巡被谁鸣走）、见逃し（一条事件的缺席）。
 * 缺一样这一票就没做完，所以它们各有一条用例。
 *
 * 事件是手写的 wire 对象——**它们就是 `MaskedEvent.encoder` 出来的形状**（mjai 记法），
 * 引擎那边有 `MaskedEventTests` 钉着两侧一致。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { historyLines } from "../../src/agent/history.ts";
import type { DecisionPackage, MaskedEvent } from "../../src/agent/types.ts";
import { DEFAULT_WORDING, wordsFor } from "../../src/agent/wording.ts";
import { dahaiPackage, sequencePackages } from "./fixtures.ts";

/** 视角固定在座位 1：下家 = 2、对家 = 3、上家 = 0。 */
const naming = wordsFor(DEFAULT_WORDING, 1, [
  { seat: 2, relative: 1 },
  { seat: 3, relative: 2 },
  { seat: 0, relative: 3 },
]);

function render(...history: MaskedEvent[]): string[] {
  return historyLines({ history } as DecisionPackage, naming);
}

const startKyoku: MaskedEvent = {
  type: "start_kyoku",
  bakaze: "1z",
  dora_marker: "2s",
  kyoku: 1,
  honba: 0,
  kyotaku: 0,
  oya: 0,
  scores: [25000, 25000, 25000, 25000],
  tehai: ["2m", "3m", "5m", "9m", "3p", "4p", "5p", "8p", "3s", "7s", "8s", "2z", "7z"],
};

// ---- 开局与摸打 ----

test("开局那一条：场况、庄家、宝牌指示牌、点数与**自家**配牌都在一行里", () => {
  const [line] = render(startKyoku);

  assert.match(line, /东1局 0 本场/);
  assert.match(line, /庄家是上家/);
  assert.match(line, /宝牌指示牌：2s/);
  assert.match(line, /你的配牌：2m 3m 5m 9m/);
});

test("他家摸的牌面看不见，但「他摸了」看得见——巡目全靠这一条撑着", () => {
  const lines = render(
    { type: "tsumo", actor: 0, pai: "?" },
    { type: "tsumo", actor: 1, pai: "3m" },
  );

  assert.deepEqual(lines, ["上家 第1巡 摸牌", "你 第1巡 摸 3m"]);
  assert.equal(lines[0].includes("?"), false, "看不见的牌面不该把 mjai 的问号漏进 prompt");
});

test("手切摸切写在**它自己那一巡**上：早巡手切与终盘手切是两件事", () => {
  const lines = render(
    { type: "tsumo", actor: 1, pai: "3m" },
    { type: "dahai", actor: 1, pai: "2z", tsumogiri: false },
    { type: "tsumo", actor: 1, pai: "5s" },
    { type: "dahai", actor: 1, pai: "5s", tsumogiri: true },
  );

  assert.deepEqual(lines, [
    "你 第1巡 摸 3m",
    "你 第1巡 打 2z",
    "你 第2巡 摸 5s",
    "你 第2巡 打 5s*",
  ]);
});

// ---- 河与鸣的时序对齐 ----

test("鸣牌：谁、第几巡、鸣了谁打的哪一张、亮出什么，紧接着打了什么", () => {
  const lines = render(
    { type: "tsumo", actor: 1, pai: "3m" },
    { type: "dahai", actor: 1, pai: "1p", tsumogiri: false },
    { type: "pon", actor: 2, target: 1, pai: "1p", consumed: ["1p", "1p"] },
    { type: "dahai", actor: 2, pai: "9m", tsumogiri: false },
  );

  assert.equal(lines[2], "下家 第0巡 碰 你打的 1p（亮出 1p 1p）");
  assert.equal(lines[3], "下家 第0巡 打 9m", "鸣完打的那张必然手切，且鸣的那家巡不涨");
});

test("吃与大明杠同一个写法，来源写相对位置", () => {
  const lines = render(
    { type: "chi", actor: 0, target: 3, pai: "2p", consumed: ["3p", "4p"] },
    { type: "daiminkan", actor: 1, target: 3, pai: "9m", consumed: ["9m", "9m", "9m"] },
  );

  assert.equal(lines[0], "上家 第0巡 吃 对家打的 2p（亮出 3p 4p）");
  assert.equal(lines[1], "你 第0巡 大明杠 对家打的 9m（亮出 9m 9m 9m）");
});

test("见逃し是**一条事件的缺席**：打牌之后直接是摸牌，那张就谁都没要", () => {
  const lines = render(
    { type: "dahai", actor: 1, pai: "2z", tsumogiri: false },
    { type: "tsumo", actor: 2, pai: "?" },
  );

  // 不造新事件、不加一行「无人鸣牌」：读法写在固定 preamble 里，
  // 这里要保证的是**两条之间不许插进任何解释**（那会依赖后来发生的事）。
  assert.deepEqual(lines, ["你 第0巡 打 2z", "下家 第1巡 摸牌"]);
});

// ---- 立直、杠与宝牌 ----

test("立直：宣言巡目在宣言那一行上，**紧接着那条打牌就是宣言牌**", () => {
  const lines = render(
    { type: "tsumo", actor: 1, pai: "3m" },
    { type: "reach", actor: 1 },
    { type: "dahai", actor: 1, pai: "4z", tsumogiri: false },
    { type: "reach_accepted", actor: 1 },
  );

  assert.deepEqual(lines, [
    "你 第1巡 摸 3m",
    "你 第1巡 宣言立直",
    "你 第1巡 打 4z",
    "你的立直成立，放出立直棒",
  ]);
});

test("加杠的宣言看得见——抢杠的窗口就是它后面那一条", () => {
  const [line] = render({ type: "kakan", actor: 0, pai: "1m", consumed: ["1m", "1m", "1m"] });

  assert.equal(line, "上家 第0巡 加杠 1m（原来碰的 1m 1m 1m）");
});

test("暗杠亮着两张，牌种是公开信息（国士抢暗杠的前提）", () => {
  const [line] = render({ type: "ankan", actor: 2, consumed: ["3p", "3p", "3p", "3p"] });

  assert.equal(line, "下家 第0巡 暗杠 3p 3p 3p 3p");
});

test("杠宝牌的揭示时机就在流里：`dora` 那一条排在哪儿就是哪儿", () => {
  const lines = render(
    { type: "ankan", actor: 1, consumed: ["3p", "3p", "3p", "3p"] },
    { type: "dora", dora_marker: "1p" },
    { type: "tsumo", actor: 1, pai: "5m" },
  );

  assert.equal(lines[1], "翻开新的宝牌指示牌：1p");
  assert.equal(lines[2], "你 第1巡 摸 5m", "暗杠是先翻宝牌再补摸岭上");
});

// ---- 收尾那几条 ----

test("和了与流局也渲得出来（它们不进决策包的历史，但同一条渲染路径要走得通）", () => {
  const [hora] = render({
    type: "hora",
    actor: 2,
    target: 1,
    pai: "3s",
    fu: 40,
    fan: 3,
    hora_points: 5200,
    deltas: [0, -5200, 5200, 0],
    scores: [25000, 19800, 30200, 25000],
    uradora_markers: ["5m"],
  });
  assert.equal(hora, "下家 和了：荣和你打的 3s，3 番 40 符，5200 点，里宝牌指示牌 5m");

  const [ryuukyoku] = render({
    type: "ryukyoku",
    reason: "fanpai",
    tenpais: [true, false, false, true],
    deltas: [1500, -1500, -1500, 1500],
    scores: [26500, 23500, 23500, 26500],
  });
  assert.equal(ryuukyoku, "流局（荒牌流局）：听牌的是 上家、对家");
});

test("读不懂的事件也占一行：它不许从历史里静静消失", () => {
  const [line] = render({ type: "某种以后才有的事件" });

  assert.match(line, /读不懂的事件/);
});

// ---- 与真数据对上 ----

test("真决策包：一条事件一行，一行也不多一行也不少", () => {
  const naming = wordsFor(
    DEFAULT_WORDING,
    dahaiPackage.observation.self.seat,
    dahaiPackage.observation.others,
  );

  assert.equal(historyLines(dahaiPackage, naming).length, dahaiPackage.history.length);
});

test("29b 之前录下来的包（没有 history 那一段）照样渲得出来，不崩", () => {
  const legacy = { ...dahaiPackage, history: undefined } as unknown as DecisionPackage;

  assert.deepEqual(historyLines(legacy, naming), []);
});

test("真数据里每一行都只写得出那一条事件里有的东西", () => {
  // 反例是「巡目从尾部抄」：那样最后一手的行会跟前面几手对不上。
  // 这里的判据简单粗暴——同一条流，逐前缀渲染，前面那几行必须逐字节不变。
  const last = sequencePackages[sequencePackages.length - 1];
  const naming = wordsFor(DEFAULT_WORDING, last.observation.self.seat, last.observation.others);
  const full = historyLines(last, naming);

  for (const decision of sequencePackages) {
    const lines = historyLines(decision, naming);
    assert.deepEqual(lines, full.slice(0, lines.length));
  }
});
