/**
 * 打码的用例（票 36）：**provider 的报错原文里有什么，牌谱里就有什么** —— 除非在它
 * 变成留存物之前被抹掉。
 *
 * 票 34 实测出这条通道的三个出口（`fallback`、`output.error_message`、
 * **下一次重试的 prompt**，牌谱存最后一次）。这里验的是同一件事的两层：
 * 抹字面量那一层（`redact.ts`），与**回执整个过一遍**那一层（`decideWith` 的出口）。
 *
 * 一个网络请求都不发：喂进去的是一个「端点原样回显 key」的假响应。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import type { AskResult } from "../../src/agent/ask.ts";
import { decide } from "../../src/agent/decide.ts";
import { decideWith } from "../../src/agent/loop.ts";
import { ENDPOINT_MASK, KEY_MASK, redactSecrets } from "../../src/agent/redact.ts";
import type { DecideResponse, SeatConfig } from "../../src/agent/types.ts";
import { badKey, dahaiPackage, localSeat, replay, request, seat } from "./fixtures.ts";

/** 一个自建网关的座位：baseUrl 是内网地址，网关自己要 key。 */
const gateway: SeatConfig = {
  ...localSeat,
  api_key: "sk-echoed-by-the-gateway-9911",
  base_url: "http://192.168.1.5:11434/v1",
};

/** 那种会把收到的 key 原样抄回来的网关（`scripts/fake-endpoint.mjs --echo-key` 是它的实物）。 */
function echoed(config: SeatConfig): AskResult {
  return {
    ...badKey,
    errorMessage:
      `401: {"message":"invalid api key: ${config.api_key} ` +
      `for ${config.base_url}/chat/completions","type":"invalid_request_error"}`,
  };
}

test("报错原文里的 key 与 baseUrl 都抹掉，别的字一个不动", () => {
  const text = `provider 报错：401: invalid api key: ${gateway.api_key} for ${gateway.base_url}`;
  const scrubbed = redactSecrets(text, gateway);

  assert.equal(scrubbed.includes(gateway.api_key), false);
  assert.equal(scrubbed.includes(gateway.base_url), false);
  assert.equal(scrubbed, `provider 报错：401: invalid api key: ${KEY_MASK} for ${ENDPOINT_MASK}`);
});

test("被请求的完整地址也抹得掉：baseUrl 是它的前缀", () => {
  const scrubbed = redactSecrets(`连不上 ${gateway.base_url}/chat/completions`, gateway);

  assert.equal(scrubbed, `连不上 ${ENDPOINT_MASK}/chat/completions`);
});

test("填的时候多打了空白或末尾斜杠，抹的仍是同一个地址", () => {
  const typed = { ...gateway, base_url: "  http://192.168.1.5:11434/v1/  " };
  const scrubbed = redactSecrets("404 at http://192.168.1.5:11434/v1/chat/completions", typed);

  assert.equal(scrubbed.includes("192.168.1.5"), false);
});

test("key 里有引号或反斜杠时，JSON 转义之后的形态也抹得掉", () => {
  const odd: SeatConfig = { ...seat, api_key: 'sk-"weird"\\key' };
  const raw = JSON.stringify({ error_message: `bad key: ${odd.api_key}` });

  const scrubbed = redactSecrets(raw, odd);

  assert.equal(scrubbed.includes("weird"), false);
  assert.equal(JSON.parse(scrubbed).error_message, `bad key: ${KEY_MASK}`);
});

test("非 ASCII 的 key 一样抹", () => {
  const scrubbed = redactSecrets(`401: your key ${seat.api_key} is invalid`, seat);

  assert.equal(scrubbed.includes(seat.api_key), false);
  assert.equal(scrubbed, `401: your key ${KEY_MASK} is invalid`);
});

test("没填 key、没填 baseUrl 时一个字都不改（空串不是字面量）", () => {
  const empty: SeatConfig = { ...seat, api_key: "", base_url: "   " };
  const text = "自定义端点没有填 baseUrl（例：http://localhost:11434/v1）";

  assert.equal(redactSecrets(text, empty), text);
});

test("回执整个过一遍：数与结构原样，字符串里的 key 没了", () => {
  const answer: DecideResponse = {
    action_id: null,
    reason: `网关说 ${gateway.api_key}`,
    failure: `provider 报错：${gateway.api_key}`,
    attempts: 3,
    latency_ms: 12,
    prompt_tail: `【上一次的回答没有被采用】provider 报错：${gateway.api_key}`,
    preamble: "怎么读这份 prompt",
    render_version: "janpo-default@0badc0de",
    tools: "[]",
    action_ids: [0, 1, 2],
    output: JSON.stringify({ error_message: `401 ${gateway.api_key}` }),
    thinking: `我看到 ${gateway.api_key}`,
    usage: { input: 1, output: 2, cache_read: 3, cache_write: 4 },
  };

  const clean = redactSecrets(answer, gateway);

  assert.equal(JSON.stringify(clean).includes(gateway.api_key), false);
  assert.equal(clean.attempts, 3);
  assert.deepEqual(clean.action_ids, [0, 1, 2]);
  assert.deepEqual(clean.usage, answer.usage);
  assert.equal(clean.preamble, answer.preamble);
  assert.equal(clean.failure, `provider 报错：${KEY_MASK}`);
});

test("端点原样回显 key：三个出口（fallback / output / 牌谱里的 prompt）一个都没漏", async () => {
  const { ask } = replay(echoed(gateway));
  const response = await decideWith(ask, request(dahaiPackage, { seat: gateway }));

  // 决策记录是从这份回执整个组装出来的（`TablePage.settle`），因此按整份查。
  assert.equal(JSON.stringify(response).includes(gateway.api_key), false, "key 进了牌谱");
  assert.equal(JSON.stringify(response).includes(gateway.base_url), false, "baseUrl 进了牌谱");

  // 三个出口逐个点名：漏一个就是漏一个（票 34 §5.1 实测的就是这三处）。
  assert.match(response.failure ?? "", /provider 报错/);
  assert.ok(response.failure?.includes(KEY_MASK), "fallback");
  assert.ok(response.output.includes(KEY_MASK), "output.error_message");
  assert.ok(response.prompt_tail.includes(KEY_MASK), "重试那一句进了牌谱的 prompt");
  assert.ok(response.prompt_tail.includes(ENDPOINT_MASK), "baseUrl 也在那一句里");
});

test("没夹带的报错原文一字不改（官方八家那一档就是这样）", async () => {
  const { ask } = replay({ ...badKey, errorMessage: "401: 你的 key 不对" });
  const response = await decideWith(ask, request(dahaiPackage, { seat: gateway }));

  assert.equal(response.failure?.includes("你的 key 不对"), true, "报错原文该留的都留着");
  assert.equal(response.failure?.includes(KEY_MASK), false, "没夹带就没有记号");
});

test("请求 JSON 读不动时不抄原文：那一份原文里就带着 key", async () => {
  // 这条路的报错在坐位配置解析出来**之前**，`redactSecrets` 拿不到字面量（票 36）。
  // V8 实测会把出错处前后十几个字符抄进消息，而这条 failure 会进牌谱的 `fallback`。
  const secret = "sk-in-a-broken-request-4f2a91";
  const answer = JSON.parse(await decide(`{"seat":{"api_key": ${secret}}}`)) as DecideResponse;

  assert.equal(answer.failure?.includes(secret), false, "请求原文进了报错");
  assert.equal(answer.failure?.includes("sk-"), false);
  assert.match(answer.failure ?? "", /读不动这份请求（SyntaxError，\d+ 字节）/);
});
