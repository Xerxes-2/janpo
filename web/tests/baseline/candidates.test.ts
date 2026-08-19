/**
 * 强 AI 基线那一次前向给出的**候选分布**怎么过这道口（票 103）。
 *
 * 它与 `mjai.test.ts` 一样跑在 `node --test` 里、**一个字节的 wasm 都不加载**，
 * 因此进 CI 的常规趟。真跑推理是本机演习那一档（`verify-review.mjs --asset`）。
 *
 * 这里钉的是**跨界那一层对分布做了什么**（上游给的形状本身由
 * `probe/akagi-wasm/candidates-shape.mjs` 在 69,318 个决策点上量过，写在报告 103 里）：
 *
 * 1. 上游给几条就带几条，**顺序照抄、概率逐位照抄**（不四舍五入、不重排、不补足三条）；
 * 2. 这一包里**认不出**的那一条不进表，但**上游一共给了几条照实报**；
 * 3. 老产物（`candidates` 这一格根本不存在）**不是错误**：分布为空，那一行退回票 93 的样子。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { candidatesOf } from "../../src/baseline/baseline.ts";
import { numberedActions } from "../../src/baseline/mjai.ts";

/** 一包动作（`ActionOption.encoder` 印出来的形状）：打 3 索 / 打 1 万 / 立直 / 过。 */
const options = numberedActions([
  { id: 0, label: "手切3索", action: { type: "dahai", pai: "3s", tsumogiri: false } },
  { id: 1, label: "摸切1万", action: { type: "dahai", pai: "1m", tsumogiri: true } },
  { id: 2, label: "立直", action: { type: "reach" } },
  { id: 3, label: "过", action: { type: "none" } },
]);

const dahai = (pai: string) => ({ type: "dahai", pai, tsumogiri: false });

test("上游给几条就带几条：顺序照抄，概率逐位照抄（不四舍五入）", () => {
  const decision = {
    action: dahai("3s"),
    candidates: [
      { action: dahai("3s"), p: 0.5961328 },
      { action: dahai("1m"), p: 0.40268 },
      // 真实语料里第三条常常小到这个量级（报告 103 §1：最末一条的 p05 是 0.0004）。
      { action: { type: "none" }, p: 0.0010278977 },
    ],
  };

  const said = candidatesOf(decision, options);

  assert.deepEqual(said.candidates, [
    { action_id: 0, p: 0.5961328 },
    { action_id: 1, p: 0.40268 },
    { action_id: 3, p: 0.0010278977 },
  ]);
  assert.equal(said.candidates_total, 3);
});

test("runner-up 的立直不带宣言牌（上游明说），照样配得上这一包里的那一条", () => {
  // 上游 `BotAction::Reach` 的注释：runner-up 那几行的 `pai` 是空串，
  // 「预测宣言牌要再跑一次模型，那几行只是给 HUD 看的」。
  const decision = {
    action: dahai("3s"),
    candidates: [
      { action: dahai("3s"), p: 0.7 },
      { action: { type: "reach", reach_dahai: "" }, p: 0.3 },
    ],
  };

  const said = candidatesOf(decision, options);

  assert.deepEqual(said.candidates, [
    { action_id: 0, p: 0.7 },
    { action_id: 2, p: 0.3 },
  ]);
  assert.equal(said.candidates_total, 2);
});

test("这一包里认不出的那一条不进表，但上游一共给了几条照实报", () => {
  const decision = {
    action: dahai("3s"),
    candidates: [
      { action: dahai("3s"), p: 0.8 },
      // 这一包里没有 9 筒可打：**绝不退而求其次挑一条看着像的**（同 `matchAction`）。
      { action: dahai("9p"), p: 0.15 },
      { action: dahai("1m"), p: 0.05 },
    ],
  };

  const said = candidatesOf(decision, options);

  assert.deepEqual(said.candidates, [
    { action_id: 0, p: 0.8 },
    { action_id: 1, p: 0.05 },
  ]);
  // **这一格就是「我们扔了什么」的账**：2 ≠ 3 时页面照实说得出来。
  assert.equal(said.candidates_total, 3);
});

test("老产物没有 candidates 这一格：分布为空、总数为 0——不是错误", () => {
  const said = candidatesOf({ action: dahai("3s") }, options);

  assert.deepEqual(said.candidates, []);
  assert.equal(said.candidates_total, 0);
});

test("p 不是数的那一条不要（形状不对宁可少一条，不许把它当 0）", () => {
  const decision = {
    action: dahai("3s"),
    candidates: [
      { action: dahai("3s"), p: "0.8" },
      { action: dahai("1m"), p: 0.2 },
    ],
  };

  const said = candidatesOf(decision, options);

  assert.deepEqual(said.candidates, [{ action_id: 1, p: 0.2 }]);
  assert.equal(said.candidates_total, 2);
});
