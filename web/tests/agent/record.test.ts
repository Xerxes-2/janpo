/**
 * 决策记录要的那几项审计数据（票 26）：**问出去的 prompt 与工具定义、收回来的原始输出与
 * thinking**，随回执一起过界，由 F# 侧组装成牌谱里的一条 `DecisionRecord`。
 *
 * 这一层只验「回执里有没有、是不是那一次的」；组装与编解码在 dotnet 侧的
 * `PaifuExportTests` 里验。一个网络请求都不发。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import type { AskResult } from "../../src/agent/ask.ts";
import { decideWith } from "../../src/agent/loop.ts";
import { chooseAction, toolsJson } from "../../src/agent/tools.ts";
import { aborted, dahaiPackage, legal, replay, request, seat, textOnly } from "./fixtures.ts";

/** 带一段思考的合法输出（`thinking` 由 `piai.ts` 收 `thinking_delta` 拼出来）。 */
const thoughtful: AskResult = { ...legal, thinking: "先数向听：现在是 1 向听，打 9m 不退向听。" };

test("回执带着这一手的 prompt 全文与工具定义", async () => {
  const { ask, prompts } = replay(legal);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.prompt, prompts[0], "记的就是真问出去的那一段");
  assert.match(response.prompt, /【可选动作】/);

  // 工具定义与真发出去的是同一份：`tools.ts` 只有一个出处。
  const ids = dahaiPackage.actions.map((option) => String(option.id));
  assert.equal(response.tools, toolsJson(ids));
  assert.match(response.tools, /choose_action/);

  // enum 是这一手动态生成的：记录里那份 schema 列的就是这一包的 id。
  const [tool] = JSON.parse(response.tools);
  assert.equal(tool.name, chooseAction(ids).name);
  assert.deepEqual(tool.parameters.properties.action_id.enum, ids);
});

test("回执带着模型的原始输出，thinking 单独一段（分享时省得掉）", async () => {
  const { ask } = replay(thoughtful);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.thinking, thoughtful.thinking);

  const output = JSON.parse(response.output);
  assert.equal(output.stop_reason, "toolUse");
  assert.equal(output.tool_call.name, "choose_action");
  assert.equal(output.tool_call.arguments.action_id, "2");
  assert.deepEqual(output.usage, legal.usage);
  // thinking 不在原始输出里：省掉它那一段不该把别的也带走。
  assert.equal(output.thinking, undefined);
  assert.ok(!response.output.includes("先数向听"));
});

test("没开思考预算时 thinking 就是 null", async () => {
  const { ask } = replay(legal);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(seat.thinking, "off");
  assert.equal(response.thinking, null);
});

test("重试之后记的是最后那一轮：prompt 带着上一次的原因，输出是第二次的", async () => {
  const { ask, prompts } = replay(textOnly, thoughtful);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.attempts, 2);
  assert.equal(response.prompt, prompts[1]);
  assert.match(response.prompt, /上一次的回答没有被采用/);
  assert.equal(response.thinking, thoughtful.thinking);
  assert.equal(JSON.parse(response.output).tool_call.name, "choose_action");
});

test("一直交不出来的那一手照样记全：审计数据在失败时最值钱", async () => {
  const { ask } = replay(aborted);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.action_id, null);
  assert.match(response.failure ?? "", /模型超时/);
  assert.match(response.prompt, /【可选动作】/);
  assert.match(response.tools, /choose_action/);
  assert.equal(JSON.parse(response.output).stop_reason, "aborted");
  assert.equal(JSON.parse(response.output).error_message, "Request aborted");
});

test("连请求都没发出去时，审计那几项是空的而不是编的", async () => {
  const { ask } = replay(legal);
  const response = await decideWith(
    ask,
    request(dahaiPackage, { seat: { ...seat, api_key: "   " } }),
  );

  assert.equal(response.prompt, "");
  assert.equal(response.tools, "");
  assert.equal(response.output, "");
  assert.equal(response.thinking, null);
});
