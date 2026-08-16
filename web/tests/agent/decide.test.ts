/**
 * F# 唯一 import 的那个入口（票 23）：**一段 JSON 进、一段 JSON 出**。
 *
 * 这里只测「读不动的请求」那一条 —— 正常路径要发真请求，它归 `loop.test.ts`
 * （录制响应）与人工验收（浏览器里真跑一局）管。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { decide } from "../../src/agent/decide.ts";
import type { DecideResponse } from "../../src/agent/types.ts";

test("请求读不动时回一条「交不出来」，而不是 reject", async () => {
  const response = JSON.parse(await decide("这不是 JSON")) as DecideResponse;

  assert.equal(response.action_id, null);
  assert.equal(response.attempts, 0);
  assert.match(response.failure ?? "", /读不动这份请求/);
});

test("回执的形状就是 F# 侧 answerDecoder 认的那五个字段", async () => {
  const response = JSON.parse(await decide("{")) as DecideResponse;

  assert.deepEqual(Object.keys(response).sort(), [
    "action_id",
    "attempts",
    "failure",
    "latency_ms",
    "reason",
  ]);
});
