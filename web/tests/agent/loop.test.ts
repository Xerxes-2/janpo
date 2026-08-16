/**
 * 兜底闭环的确定性用例（票 23）：**四类路径 —— 合法输出、越界 id、格式跑偏、超时**，
 * 外加 provider 报错。喂进去的都是录制下来的真实响应，**一个网络请求都不发**。
 *
 * 验的是同一件事：这一层要么给出一个包里有的 id，要么给出一条「交不出来」的原因，
 * **绝不 reject，也绝不卡住**。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import type { AskResult } from "../../src/agent/ask.ts";
import { decideWith } from "../../src/agent/loop.ts";
import {
  aborted,
  assistedAnswer,
  assistedSeat,
  badKey,
  dahaiPackage,
  legal,
  replay,
  request,
  responsePackage,
  seat,
  textOnly,
} from "./fixtures.ts";

test("合法输出：模型选的 id 原样回去，只问一次", async () => {
  const { ask, prompts } = replay(legal);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.action_id, 2);
  assert.equal(response.failure, null);
  assert.equal(response.attempts, 1);
  assert.ok(response.reason?.includes("9万"));
  assert.equal(prompts.length, 1);
  // 这一手的合法动作集里确实有 id=2。
  assert.ok(dahaiPackage.actions.some((option) => option.id === 2));
});

test("越界 id：重试到上限，最后交不出来", async () => {
  // 同一条录制响应（id=2）放到只有 id 0/1 的那一手上 —— 它就是一个越界 id。
  const { ask } = replay(legal);
  const response = await decideWith(ask, request(responsePackage));

  assert.equal(response.action_id, null);
  assert.equal(response.attempts, 3, "首问 + 重试 2 次");
  assert.match(response.failure ?? "", /action_id=2 不在这一手的合法动作集里/);
  assert.match(response.failure ?? "", /重试 2 次仍无结果/);
});

test("格式跑偏：模型没调工具，只回了一段话", async () => {
  const { ask } = replay(textOnly);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.action_id, null);
  assert.equal(response.attempts, 3);
  assert.match(response.failure ?? "", /没有调用 choose_action/);
});

test("超时：abort 是值不是异常，走同一条兜底路", async () => {
  const { ask } = replay(aborted);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.action_id, null);
  assert.equal(response.attempts, 3);
  assert.match(response.failure ?? "", /模型超时/);
  assert.equal(response.latency_ms, aborted.latencyMs * 3, "三次的耗时累加");
});

test("provider 报错：401 的原文留在 failure 里，人看得见", async () => {
  const { ask } = replay(badKey);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.action_id, null);
  assert.match(response.failure ?? "", /provider 报错/);
  assert.match(response.failure ?? "", /Authentication Fails/);
});

test("重试成功就不再重试：第二次答对了就用第二次的", async () => {
  const { ask, prompts } = replay(textOnly, legal);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.action_id, 2);
  assert.equal(response.failure, null);
  assert.equal(response.attempts, 2);
  assert.equal(prompts.length, 2);
  // 重问那一遍要把上一次错在哪告诉模型，否则它多半会照样再来一遍。
  assert.match(prompts[1], /上一次的回答没有被采用/);
  assert.match(prompts[1], /没有调用 choose_action/);
});

test("重试上限是请求给的：0 就只问一次", async () => {
  const { ask, prompts } = replay(aborted);
  const response = await decideWith(ask, request(dahaiPackage, { retry_limit: 0 }));

  assert.equal(response.attempts, 1);
  assert.equal(prompts.length, 1);
});

test("没填 key 就不发请求：白等一个 401 没意义", async () => {
  const { ask, prompts } = replay(legal);
  const response = await decideWith(
    ask,
    request(dahaiPackage, { seat: { ...seat, api_key: "   " } }),
  );

  assert.equal(prompts.length, 0);
  assert.equal(response.attempts, 0);
  assert.match(response.failure ?? "", /没有填 deepseek 的 API key/);
});

test("action_id 是句人话时也不认：只有严格的整数算数", async () => {
  const nonsense: AskResult = {
    ...legal,
    toolCall: { name: "choose_action", arguments: { action_id: "打9万", reason: "随便" } },
  };
  const { ask } = replay(nonsense);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(response.action_id, null);
  assert.match(response.failure ?? "", /action_id 不是一个 id/);
});

test("ask 真抛了异常也只是这一次失败，不是整局崩掉", async () => {
  let calls = 0;
  const ask = async () => {
    calls += 1;
    throw new Error("适配器炸了");
  };
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(calls, 3);
  assert.equal(response.action_id, null);
  assert.match(response.failure ?? "", /适配器炸了/);
});

test("Assisted 档：问出去的 prompt 带脚手架，回执还是那五个字段", async () => {
  // 录制的是模型对**带脚手架那份 prompt** 的真实回答（`pnpm run record:agent ask-assisted`）。
  const { ask, prompts } = replay(assistedAnswer);
  const response = await decideWith(ask, request(dahaiPackage, { seat: assistedSeat }));

  assert.equal(response.action_id, 2);
  assert.equal(response.failure, null);
  assert.equal(prompts.length, 1);
  assert.match(prompts[0], /【引擎算好的数】/, "档位随座位配置进来，不是写死的");
  assert.match(prompts[0], /当前向听数：3 向听/);
  // 模型真的拿它当了理由（录制下来的原话里引了有效牌与退向）。
  assert.match(response.reason ?? "", /有效牌|退向/);
});

test("Assisted 档：答不上话照样交不出来，兜底路径一模一样", async () => {
  // 兜底**打哪一手**是引擎的事（`Fallback.action tier package`），
  // 这一层只负责把「我交不出来」连同原因回回去——换了档位也一样。
  const { ask, prompts } = replay(aborted);
  const response = await decideWith(ask, request(dahaiPackage, { seat: assistedSeat }));

  assert.equal(response.action_id, null);
  assert.equal(response.attempts, 3);
  assert.match(response.failure ?? "", /模型超时/);
  assert.match(prompts[2], /【引擎算好的数】/, "重问的那几遍也是同一档");
  assert.match(prompts[2], /上一次的回答没有被采用/);
});

test("合法动作集是空的（不该发生）也不抛", async () => {
  const { ask, prompts } = replay(legal);
  const response = await decideWith(ask, request({ ...dahaiPackage, actions: [] }));

  assert.equal(prompts.length, 0);
  assert.equal(response.action_id, null);
  assert.match(response.failure ?? "", /没有合法动作/);
});
