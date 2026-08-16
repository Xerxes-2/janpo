/**
 * 自定义端点（票 30）：**接本地 Ollama / LM Studio / 自建 OpenAI 兼容网关的那条路**。
 *
 * 这一层是纯的（不 import pi-ai，一个请求都不发）：它只回答四个问题 ——
 * 这个座位走不走自定义端点、baseUrl 读不读得懂、要不要 key、失败那句话该怎么说给人听。
 * 真的接线（`createProvider` + `createModels`）在 `piai.ts`，通道本身由
 * `scripts/verify-custom-endpoint.mjs` 在真浏览器里验（CORS 与 mixed content）。
 *
 * **官方八家的行为一个字节都不许变**：下面每一组都有一条「官方那侧原样」的断言守着。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import {
  CUSTOM_PROVIDER,
  customModel,
  explainFailure,
  isCustom,
  keyFor,
  missingConfig,
  readBaseUrl,
} from "../../src/agent/endpoint.ts";
import type { SeatConfig } from "../../src/agent/types.ts";
import { seat } from "./fixtures.ts";

/** 一个接本地 Ollama 的座位。**没有 key**——本地端点通常根本不校验它。 */
const local: SeatConfig = {
  ...seat,
  provider: CUSTOM_PROVIDER,
  model: "qwen3:8b",
  api_key: "",
  base_url: "http://localhost:11434/v1",
};

// ---- 谁走哪条路 ----

test("provider 是 custom 才走自定义端点，官方八家一律不走", () => {
  assert.equal(isCustom(local), true);
  assert.equal(isCustom(seat), false);
  // 官方座位就算填了 baseUrl 也不走：新路径不许污染既有路径。
  assert.equal(isCustom({ ...seat, base_url: "http://localhost:11434/v1" }), false);
});

// ---- baseUrl 读得懂吗 ----

test("baseUrl 读得懂：末尾的斜杠与前后空白都去掉", () => {
  assert.deepEqual(readBaseUrl(" http://localhost:11434/v1/ "), {
    ok: true,
    baseUrl: "http://localhost:11434/v1",
  });
  assert.deepEqual(readBaseUrl("https://gateway.example.com/openai/v1"), {
    ok: true,
    baseUrl: "https://gateway.example.com/openai/v1",
  });
});

test("baseUrl 读不懂：一条**能读懂的**原因，带一个例子", () => {
  const blank = readBaseUrl("   ");
  assert.equal(blank.ok, false);
  assert.match(blank.ok ? "" : blank.why, /没有填 baseUrl/);
  assert.match(blank.ok ? "" : blank.why, /localhost:11434\/v1/, "错误里要带一个抄得走的例子");

  for (const bad of ["localhost:11434", "ws://localhost:11434/v1", "这不是地址", "/v1"]) {
    const result = readBaseUrl(bad);
    assert.equal(result.ok, false, `「${bad}」不该被当成合法 baseUrl`);
    assert.match(result.ok ? "" : result.why, /baseUrl/);
  }
});

// ---- 发不发得出这一次请求 ----

test("官方八家：没填 key 就不发请求，那句话一字未改", () => {
  assert.equal(missingConfig({ ...seat, api_key: "   " }), "没有填 deepseek 的 API key");
  assert.equal(missingConfig(seat), null);
});

test("自定义端点：**没有 key 也照发**，本地端点不校验它", () => {
  assert.equal(missingConfig(local), null);
  // 但 baseUrl 没填就别发了：那一次必然连不上，白等一个来回。
  assert.match(missingConfig({ ...local, base_url: "" }) ?? "", /没有填 baseUrl/);
  assert.match(missingConfig({ ...local, base_url: "localhost:11434" }) ?? "", /baseUrl/);
});

test("送出去的 key：官方原样，自定义端点没填时填一个占位的", () => {
  assert.equal(keyFor(seat), seat.api_key);
  assert.equal(keyFor({ ...local, api_key: "sk-网关的 key" }), "sk-网关的 key");
  // pi-ai 的 OpenAI 适配器空 key 会直接抛「No API key for provider」（实测），
  // 而 Ollama / LM Studio 根本不看这个头 —— 占位串让「无鉴权的本地端点」跑得起来。
  assert.equal(keyFor(local), "unused");
});

// ---- 当场造出来的那个模型 ----

test("自定义端点的模型是当场造的：名字自由文本，走 openai-completions", () => {
  const model = customModel("http://localhost:11434/v1", " qwen3:8b ");

  assert.equal(model.id, "qwen3:8b", "模型名两端的空白不该跟着进请求体");
  assert.equal(model.api, "openai-completions");
  assert.equal(model.provider, CUSTOM_PROVIDER);
  assert.equal(model.baseUrl, "http://localhost:11434/v1");
  // 本地模型的价目表不存在：成本一律 0，别在牌谱里编一个价出来。
  assert.deepEqual(model.cost, { input: 0, output: 0, cacheRead: 0, cacheWrite: 0 });
});

// ---- 失败那句话说得清吗 ----

test("连不上端点：说的是「端点连不上」，不是「模型不肯选」", () => {
  const why = explainFailure(local, "Connection error.");

  assert.match(why, /连不上/);
  assert.match(why, /http:\/\/localhost:11434\/v1/, "要把填的那个 baseUrl 摆出来");
  assert.match(why, /CORS|跨域/, "浏览器里最常见的原因就是它");
  assert.match(why, /Connection error\./, "原始报错留着给人查");
});

test("404：多半是 baseUrl 漏了 /v1，直接说出来", () => {
  const why = explainFailure(local, '404: {"message":"404 page not found"}');

  assert.match(why, /404/);
  assert.match(why, /\/v1/);
});

test("其余的原样透传，官方八家更是一个字都不改", () => {
  const authFailed = '401: {"message":"Authentication Fails"}';

  assert.equal(explainFailure(seat, "Connection error."), "Connection error.");
  assert.equal(explainFailure(seat, authFailed), authFailed);
  assert.equal(explainFailure(local, authFailed), authFailed);
});
