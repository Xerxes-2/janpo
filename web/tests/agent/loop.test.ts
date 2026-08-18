/**
 * 兜底闭环的确定性用例（票 23）：**四类路径 —— 合法输出、越界 id、格式跑偏、超时**，
 * 外加 provider 报错。喂进去的都是录制下来的真实响应，**一个网络请求都不发**。
 *
 * 验的是同一件事：这一层要么给出一个包里有的 id，要么给出一条「交不出来」的原因，
 * **绝不 reject，也绝不卡住**。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import type { Ask, AskResult } from "../../src/agent/ask.ts";
import { decideWith } from "../../src/agent/loop.ts";
import { WHAT_IF_LIMIT } from "../../src/agent/what-if.ts";
import {
  aborted,
  assistedAnswer,
  assistedSeat,
  badKey,
  dahaiPackage,
  dangerAnswer,
  dangerPackage,
  legal,
  localSeat,
  noModel,
  rateLimited,
  replay,
  request,
  responsePackage,
  seat,
  serverError,
  textOnly,
  toolSearchSeat,
  whatIfCall,
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

// 下面两条是票 47 的正题：**同一条路上的两类失败，分类器必须把它俩分开**。
// 两份固件都是录下来的真响应（401 来自 DeepSeek，429 来自本机假端点的真 HTTP 响应）。

test("不值得重试：401 只问一次就走兜底，不占额外请求", async () => {
  const { ask, prompts } = replay(badKey);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(prompts.length, 1, "第一次 401 就已经注定后两次也是，那两个请求是白烧的");
  assert.equal(response.attempts, 1);
  assert.equal(response.action_id, null, "傅底的行为一字未改：仍然是「我交不出来」");
  // 页面与牌谱上那句话要能看出「没重试是因为重试没意义」，而不是看着像少试了。
  assert.match(response.failure ?? "", /没有重试/);
  assert.match(response.failure ?? "", /不认这把 key/);
  assert.doesNotMatch(response.failure ?? "", /重试 \d+ 次仍无结果/);
  // 报错原文一个字不少（票 36 那道打码在出口，分类在它之前）。
  assert.match(response.failure ?? "", /Authentication Fails/);
});

test("值得重试：限流（429）照样重试到上限", async () => {
  const { ask, prompts } = replay(rateLimited);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(prompts.length, 3, "限流是「这一刻不行」，不是「你这份请求不对」");
  assert.equal(response.attempts, 3);
  assert.match(response.failure ?? "", /重试 2 次仍无结果/);
  assert.doesNotMatch(response.failure ?? "", /没有重试/);
});

test("值得重试：端点自己挂了（503）也重试", async () => {
  const { ask, prompts } = replay(serverError);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.equal(prompts.length, 3);
  assert.match(response.failure ?? "", /重试 2 次仍无结果/);
});

test("不值得重试：模型名不存在（404）也只问一次", async () => {
  // 这一份固件还多守一件事：自定义端点的报错被 `explainFailure` 在前面加了一句中文，
  // 因此状态码**不在句首**——分类器靠锚定开头的写法会在这里漏掉。
  const { ask, prompts } = replay(noModel);
  const response = await decideWith(ask, request(dahaiPackage, { seat: localSeat }));

  assert.equal(prompts.length, 1);
  assert.equal(response.attempts, 1);
  assert.match(response.failure ?? "", /没有重试/);
  assert.match(response.failure ?? "", /模型名或这个端点地址不存在/);
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

test("Assisted 档：危险度排序也进了问出去的那份 prompt（票 25）", async () => {
  // 录制的是模型对**带危险度那份 prompt** 的真实回答（`pnpm run record:agent ask-danger`）。
  const { ask, prompts } = replay(dangerAnswer);
  const response = await decideWith(ask, request(dangerPackage, { seat: assistedSeat }));

  assert.equal(response.action_id, 0);
  assert.equal(response.failure, null);
  assert.match(prompts[0], /危险度排序（有威胁的家：对家有副露）/);
  assert.match(prompts[0], /第1位 id=0（打 4m）：现物 —— 对家现物/);
  // 模型真的拿它当了理由（录下来的原话里引了「对家现物」）。
  assert.match(response.reason ?? "", /现物/);
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

test("自定义端点：没填 key 照发请求（本地端点不校验它）", async () => {
  // 票 30。换成官方那八家时这一句仍然拦住（上面那条用例），**两条路不互相污染**。
  const { ask, prompts } = replay(legal);
  const response = await decideWith(ask, request(dahaiPackage, { seat: localSeat }));

  assert.equal(prompts.length, 1, "请求真的发了出去");
  assert.equal(response.action_id, 2);
  assert.equal(response.failure, null);
});

test("自定义端点：baseUrl 没填就不发请求，说的是端点不是模型", async () => {
  const { ask, prompts } = replay(legal);
  const response = await decideWith(
    ask,
    request(dahaiPackage, { seat: { ...localSeat, base_url: "   " } }),
  );

  assert.equal(prompts.length, 0);
  assert.equal(response.attempts, 0);
  assert.match(response.failure ?? "", /没有填 baseUrl/);
  assert.doesNotMatch(response.failure ?? "", /API key/, "这不是 key 的错");
});

// ---- ToolSearch 档：问 → 拿到答案 → 出牌（票 94） ----

/** 这一手哪几条是打牌（工具的 enum 就是它们）。 */
const discards = (decision: typeof dahaiPackage) =>
  decision.actions
    .filter((option) => (option.action as { type?: string }).type === "dahai")
    .map((option) => String(option.id));

/**
 * 一个**看 `tools` 行事**的假模型：只要还给得到 what-if 就接着查，不给了就出牌。
 *
 * 它演的正是 constrained sampling 下的真模型：**工具不在 `tools` 里就调不出来**。
 * “它就是不听话”那一种异常端点另有用例（下面那条「问满了还硬调」）。
 */
function insatiable(answer: AskResult): {
  ask: Ask;
  offered: string[][];
  prompts: string[];
} {
  const offered: string[][] = [];
  const prompts: string[] = [];

  const ask: Ask = async (asked) => {
    offered.push(asked.whatIfIds);
    prompts.push(asked.prompt);
    if (asked.whatIfIds.length === 0) return answer;
    return whatIfCall(Number(asked.whatIfIds[0]));
  };

  return { ask, offered, prompts };
}

test("整条链路：发起工具调用 → 拿到答案 → 出牌", async () => {
  const { ask, prompts, offered } = replay(whatIfCall(0), whatIfCall(3), legal);
  const response = await decideWith(ask, request(dahaiPackage, { seat: toolSearchSeat }));

  // 三次请求：两次查 + 一次出牌。**查不吃重试额度**，因此 attempts 仍是 1。
  assert.equal(prompts.length, 3);
  assert.equal(response.attempts, 1);
  assert.equal(response.action_id, 2);
  assert.equal(response.failure, null);

  // 第一问的那一份里一行查询都没有；之后每一份多一条。
  assert.equal(prompts[0].includes("你查过"), false);
  assert.match(prompts[1], /^你查过 1 次，还可以再查 3 次：$/m);
  assert.match(prompts[2], /^你查过 2 次，还可以再查 2 次：$/m);

  // **这一档的可观测性就是它**：牌谱存的那份尾部里看得见查了什么、查了几次。
  assert.match(response.prompt_tail, /^你查过 2 次，还可以再查 2 次：$/m);
  assert.match(response.prompt_tail, /^- id=0（打 2m）：打完 3 向听/m);
  assert.match(response.prompt_tail, /^- id=3（打 3p）：打完 4 向听/m);

  // 工具定义的形状里也看得见这一场摆了哪几个工具。
  assert.match(response.tools, /"what_if"/);
  // 前两轮真的把工具给了它，而 enum 里只有打牌那几条。
  assert.deepEqual(offered[0], discards(dahaiPackage));
  assert.deepEqual(offered[2], discards(dahaiPackage));
});

test("查的那几轮的账单也算进这一手：否则那一档的账看着与裸奔档一模一样", async () => {
  const { ask } = replay(whatIfCall(0), whatIfCall(3), legal);
  const response = await decideWith(ask, request(dahaiPackage, { seat: toolSearchSeat }));

  const once = legal.usage;
  assert.notEqual(once, null);
  assert.equal(response.usage?.input, (once?.input ?? 0) * 3);
  assert.equal(response.usage?.output, (once?.output ?? 0) * 3);
  assert.equal(response.usage?.cache_read, (once?.cacheRead ?? 0) * 3);
  // 延迟同理：三个来回的墓钟全在里面（多轮往返的代价就是这个数）。
  assert.equal(response.latency_ms, legal.latencyMs * 3);
});

test("零查询零重试的那一手，账单与从前逐字相同", async () => {
  const { ask } = replay(legal);
  const response = await decideWith(ask, request(dahaiPackage));

  assert.deepEqual(response.usage, {
    input: legal.usage?.input,
    output: legal.usage?.output,
    cache_read: legal.usage?.cacheRead ?? 0,
    cache_write: legal.usage?.cacheWrite ?? 0,
  });
});

test("到上限就停，而且这一手照常打完（不卡死）", async () => {
  // 假模型有多少次查多少次。上限不是靠求它自觉：问满之后 `tools` 里就没有它了。
  const { ask, offered, prompts } = insatiable(legal);
  const response = await decideWith(ask, request(dahaiPackage, { seat: toolSearchSeat }));

  assert.equal(response.action_id, 2, "这一手照常打完了");
  assert.equal(response.failure, null);
  assert.equal(response.attempts, 1, "四次查一次一次也没吃掉重试额度");
  assert.equal(prompts.length, WHAT_IF_LIMIT + 1, "最坐四次查 + 一次出牌");
  assert.deepEqual(
    offered.map((ids) => ids.length > 0),
    [true, true, true, true, false],
    "前四轮给工具，第五轮不给",
  );
  assert.match(response.prompt_tail, /^你查过 4 次，还可以再查 0 次：$/m);
});

test("问满了还硬调：算「调了别的工具」去重问，而不是默默多给它一次额度", async () => {
  // 端点不听 schema 时的那一种：每一轮都拿出 `what_if`。
  // 开头四轮是真查（工具给了），之后三轮是首问 + 两次重试——总共 7 次，**有界**。
  const { ask, prompts } = replay(whatIfCall(0));
  const response = await decideWith(ask, request(dahaiPackage, { seat: toolSearchSeat }));

  assert.equal(prompts.length, WHAT_IF_LIMIT + 3);
  assert.equal(response.attempts, 3);
  assert.equal(response.action_id, null);
  assert.match(response.failure ?? "", /模型调了别的工具：what_if/);
});

test("Bare 与 Assisted 档一轮也不给这个工具：档位的自变量只许有一个", async () => {
  for (const config of [seat, assistedSeat]) {
    const { ask, offered, prompts } = replay(legal);
    const response = await decideWith(ask, request(dahaiPackage, { seat: config }));

    assert.equal(response.action_id, 2);
    assert.deepEqual(offered, [[]], `${config.tier} 档不该拿到 what-if`);
    assert.equal(prompts[0].includes("what_if"), false, `${config.tier} 档的 prompt 不该提到它`);
    assert.equal(response.tools.includes("what_if"), false);
  }
});

test("响应那一手没牌可打：工具一轮都不给（空 enum 等于邀它烧一次额度）", async () => {
  const { ask, offered } = replay(legal);
  await decideWith(ask, request(responsePackage, { seat: toolSearchSeat }));

  assert.deepEqual(discards(responsePackage), []);
  assert.ok(offered.length > 0);
  assert.ok(
    offered.every((ids) => ids.length === 0),
    "这一手没牌可打，哪一轮都不该把工具摆出去",
  );
});

test("合法动作集是空的（不该发生）也不抛", async () => {
  const { ask, prompts } = replay(legal);
  const response = await decideWith(ask, request({ ...dahaiPackage, actions: [] }));

  assert.equal(prompts.length, 0);
  assert.equal(response.action_id, null);
  assert.match(response.failure ?? "", /没有合法动作/);
});
