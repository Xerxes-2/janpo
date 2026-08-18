/**
 * 强 AI 基线那层**薄翻译**的用例（票 92）。
 *
 * 它跑在 `node --test` 里、**一个字节的 wasm 都不加载**——因此它进 CI 的常规趟
 * （那几 MB 不入版本控制，CI 里根本没有那份产物）。真跑推理是本机演习那一档
 * （`web/scripts/verify-baseline.mjs --asset`），覆盖不到什么写在报告 92 里。
 *
 * 这里钉的是**票 91 报告第 ④ 节列出来的每一处差异**，外加两条它没列、
 * 票 92 自己撞上的**静默错译**（字牌记法与场风记法）——两条都不会报错，只会算错。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import {
  actionKey,
  canonicalBakaze,
  canonicalTile,
  historyLines,
  matchAction,
  numberedActions,
  TranslationError,
} from "../../src/baseline/mjai.ts";

/** 一条 janpo 侧的编号动作（`ActionOption.encoder` 印出来的形状）。 */
const option = (id: number, action: Record<string, unknown>) => ({ id, label: "", action });

const startKyoku = {
  type: "start_kyoku",
  bakaze: "2z",
  dora_marker: "2s",
  kyoku: 1,
  honba: 0,
  kyotaku: 0,
  oya: 0,
  scores: [25000, 25000, 25000, 25000],
  tehai: ["1m", "1m", "2m", "3m", "4m", "5m", "6m", "7m", "2p", "3p", "4p", "5p", "6p"],
};

test("字牌两侧写法不同：它印 E，janpo 写 1z——不归一就会静默地当成别的牌", () => {
  assert.equal(canonicalTile("E"), "1z");
  assert.equal(canonicalTile("N"), "4z");
  assert.equal(canonicalTile("C"), "7z");
  // 数牌与红 5 原样（两侧本来就同一种写法）。
  assert.equal(canonicalTile("5mr"), "5mr");
  assert.equal(canonicalTile("3p"), "3p");
  // 认不出来的原样交出去：比对时它自然对不上，于是这一手走兜底而不是被错当成别的牌。
  assert.equal(canonicalTile("??"), "??");
});

test("场风走的是另一张对照表：riichienv 认不出 2z 就当东场", () => {
  assert.equal(canonicalBakaze("1z"), "E");
  assert.equal(canonicalBakaze("2z"), "S");
  assert.equal(canonicalBakaze("3z"), "W");
  assert.equal(canonicalBakaze("4z"), "N");
});

test("掩蔽流的 start_kyoku 摊成四格 tehais，他家那三格是 ?", () => {
  const [line] = historyLines([startKyoku], 2);
  const event = JSON.parse(String(line));

  assert.equal(event.bakaze, "S");
  assert.equal(event.tehais.length, 4);
  assert.deepEqual(event.tehais[2], startKyoku.tehai);
  for (const seat of [0, 1, 3]) {
    assert.equal(event.tehais[seat].length, 13);
    assert.ok(event.tehais[seat].every((pai: string) => pai === "?"));
  }
  // 单数的那一格不许留下：留着的话读的人分不出哪一格是权威。
  assert.equal(event.tehai, undefined);
});

test("他家摸的那张原样带着 ?（掩蔽流里它本来就是这么写的）", () => {
  const lines = historyLines([startKyoku, { type: "tsumo", actor: 1, pai: "?" }], 0);
  assert.equal(JSON.parse(String(lines[1])).pai, "?");
});

test("历史不是从 start_kyoku 打头就当场抛：喂进去的话它会拿上一局的状态出手", () => {
  assert.throws(() => historyLines([{ type: "tsumo", actor: 0, pai: "1m" }], 0), TranslationError);
  assert.throws(() => historyLines([], 0), TranslationError);
});

test("dahai 只按牌面配：摸切与否是引擎算出来的事实，不是它的决策", () => {
  const options = numberedActions([
    option(0, { type: "dahai", actor: 1, pai: "1m", tsumogiri: true }),
    option(1, { type: "dahai", actor: 1, pai: "2p", tsumogiri: false }),
  ]);

  // 它印的 tsumogiri 与包里那一条相反，仍旧配得上。
  assert.equal(matchAction({ type: "dahai", pai: "2p", tsumogiri: true }, options), 1);
  assert.equal(matchAction({ type: "dahai", pai: "1m", tsumogiri: false }, options), 0);
});

test("字牌那一手配得上（这一条按红过：不归一时它配不到，整手掉进兜底）", () => {
  const options = numberedActions([
    option(0, { type: "dahai", actor: 0, pai: "4z", tsumogiri: true }),
    option(1, { type: "dahai", actor: 0, pai: "1m", tsumogiri: false }),
  ]);

  assert.equal(matchAction({ type: "dahai", pai: "N", tsumogiri: true }, options), 0);
});

test("reach 只按类型配，reach_dahai 扔掉——宣言牌那一手 janpo 会再问一遍", () => {
  const options = numberedActions([
    option(0, { type: "dahai", actor: 0, pai: "4z", tsumogiri: true }),
    option(1, { type: "reach", actor: 0 }),
  ]);

  assert.equal(matchAction({ type: "reach", reach_dahai: "N" }, options), 1);
  // 它宣言的那张牌**不参与匹配**：宣言与宣言牌在 janpo 里是两步（`Action.Riichi` 那段注释）。
  assert.equal(matchAction({ type: "reach", reach_dahai: "9s" }, options), 1);
});

test("hora 缺 pai 也配得上：和的是哪张由引擎说了算", () => {
  const options = numberedActions([
    option(0, { type: "hora", actor: 2, target: 1, pai: "5p" }),
    option(1, { type: "none", actor: 2 }),
  ]);

  assert.equal(matchAction({ type: "hora", target: 1 }, options), 0);
  assert.equal(matchAction({ type: "none" }, options), 1);
});

test("碰的两种亮法分得开：红 5 亮不亮是它的决策", () => {
  const options = numberedActions([
    option(0, { type: "pon", actor: 0, target: 3, pai: "5s", consumed: ["5s", "5s"] }),
    option(1, { type: "pon", actor: 0, target: 3, pai: "5s", consumed: ["5s", "5sr"] }),
    option(2, { type: "none", actor: 0 }),
  ]);

  assert.equal(
    matchAction({ type: "pon", target: 3, pai: "5s", consumed: ["5sr", "5s"] }, options),
    1,
  );
  assert.equal(
    matchAction({ type: "pon", target: 3, pai: "5s", consumed: ["5s", "5s"] }, options),
    0,
  );
});

test("亮出来那几张的顺序不是决策：两侧排法不同也配得上", () => {
  const mine = actionKey({ type: "chi", actor: 1, target: 0, pai: "3m", consumed: ["4m", "5m"] });
  const theirs = actionKey({ type: "chi", target: 0, pai: "3m", consumed: ["5m", "4m"] });
  assert.equal(mine, theirs);
});

test("暗杠按四张配、加杠按那一张配", () => {
  const options = numberedActions([
    option(0, { type: "ankan", actor: 0, consumed: ["1z", "1z", "1z", "1z"] }),
    option(1, { type: "kakan", actor: 0, pai: "2p", consumed: ["2p", "2p", "2p"] }),
    option(2, { type: "dahai", actor: 0, pai: "9s", tsumogiri: true }),
  ]);

  assert.equal(matchAction({ type: "ankan", consumed: ["E", "E", "E", "E"] }, options), 0);
  assert.equal(matchAction({ type: "kakan", pai: "2p", consumed: ["2p", "2p", "2p"] }, options), 1);
});

test("九种九牌与「过」各按类型配", () => {
  const options = numberedActions([
    option(0, { type: "ryukyoku", actor: 0 }),
    option(1, { type: "dahai", actor: 0, pai: "1m", tsumogiri: true }),
  ]);

  assert.equal(matchAction({ type: "ryukyoku" }, options), 0);
});

test("配不上就是 null——绝不退而求其次挑一条看着可行的", () => {
  const options = numberedActions([
    option(0, { type: "dahai", actor: 0, pai: "1m", tsumogiri: true }),
    option(1, { type: "dahai", actor: 0, pai: "2m", tsumogiri: false }),
  ]);

  // 三麻才有的拔北：四麻桌上它一条都配不到。
  assert.equal(matchAction({ type: "nukidora" }, options), null);
  // 包里没有的那张牌同理。
  assert.equal(matchAction({ type: "dahai", pai: "9p", tsumogiri: true }, options), null);
});

test("形状不对的那几条一律不要（宁可兜底，不许猜）", () => {
  assert.deepEqual(numberedActions(undefined), []);
  assert.deepEqual(numberedActions([{ id: "0", action: { type: "none" } }]), []);
  assert.deepEqual(numberedActions([{ id: 0 }]), []);
});
