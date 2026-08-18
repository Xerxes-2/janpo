/**
 * 重试判据的用例（票 47）：**这个错误再问一遍，可能不一样吗。**
 *
 * 这一份验的是分类本身；「分完之后循环怎么走」在 `loop.test.ts`（那边喂的是录制的响应）。
 *
 * **输入不是编的**：下面每条「真实形态」的字串都标了出处 —— 要么抄自 `tests/fixtures/agent/`
 * 里录下来的那几份，要么是 pi-ai 的 `utils/error-body.ts` 三种拼法之一。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { piAsk } from "../../src/agent/piai.ts";
import { retryOf } from "../../src/agent/retry.ts";
import { badKey, noModel, rateLimited, seat, serverError } from "./fixtures.ts";

/** 判据的两句话：值不值得，以及不值得时那句「为什么再问也一样」。 */
function verdict(message: string | null): string {
  const retry = retryOf(message);
  return retry.worth ? "值得" : `不值得：${retry.because}`;
}

test("录下来的那四份真响应，各归各的", () => {
  assert.equal(verdict(badKey.errorMessage), "不值得：这个端点不认这把 key");
  assert.equal(verdict(noModel.errorMessage), "不值得：这个模型名或这个端点地址不存在");
  assert.equal(verdict(rateLimited.errorMessage), "值得");
  assert.equal(verdict(serverError.errorMessage), "值得");
});

test("4xx 说的是「你这份请求不对」：重问发的还是同一份，因此不值得", () => {
  for (const status of [400, 401, 402, 403, 404, 409, 413, 415, 422, 451]) {
    assert.equal(
      retryOf(`${status}: {"message":"nope"}`).worth,
      false,
      `${status} 该判成不值得重试`,
    );
  }
});

test("5xx 说的是「端点这一刻不行」：与我们这份请求无关，因此值得", () => {
  for (const status of [500, 502, 503, 504, 529]) {
    assert.equal(retryOf(`${status}: {"message":"nope"}`).worth, true, `${status} 该判成值得重试`);
  }
});

test("4xx 的三个例外：它们说的不是「请求不对」而是「这一刻不行」", () => {
  for (const status of [408, 425, 429]) {
    assert.equal(retryOf(`${status}: {"message":"nope"}`).worth, true, `${status} 是时机问题`);
  }
});

test("状态码的三种写法都认得出来（pi-ai 的 formatProviderError 会拼成这三种）", () => {
  // ① 它自己拼的 `<status>: <body>`（DeepSeek / 自定义端点走这条，两份固件都是）
  assert.equal(retryOf('401: {"message":"Authentication Fails"}').worth, false);
  // ② SDK 的 message 自带正文（Anthropic / Google 走这条）
  assert.equal(
    retryOf('401 {"type":"error","error":{"type":"authentication_error"}}').worth,
    false,
  );
  // ③ 带前缀的那种（openai-responses / azure）
  assert.equal(retryOf("OpenAI API error (401): 401 Incorrect API key provided").worth, false);
});

test("状态码不在句首也认得出来：自定义端点的报错前面加了一句中文", () => {
  // 这一条正是 `ask-error-no-model` 那份固件的形状（`explainFailure` 加的那句话）。
  assert.match(noModel.errorMessage ?? "", /^自定义端点/);
  assert.equal(retryOf(noModel.errorMessage).worth, false);
});

test("地址里的数不是状态码：IP 段与端口号不许被当成 4xx", () => {
  // 若把 `127`、`404`……不加分辨地扫出来，这条连不上就会被误判成「不值得重试」，
  // 而它恰恰是最该重试的那一种（端点刚起、网络抖了一下）。
  const connection =
    "连不上自定义端点 http://127.0.0.1:4199/v1：浏览器没能把请求发出去。" +
    "常见原因：端点没起、地址写错、端点没放行本页面的跨域请求（CORS）。原始报错：Connection error.";
  assert.equal(retryOf(connection).worth, true);
  assert.equal(retryOf("Connection error.").worth, true);
  assert.equal(retryOf("TypeError: Failed to fetch").worth, true);
});

test("判不出来就当值得重试：「可能不一样」在不知道的时候成立", () => {
  assert.equal(retryOf(null).worth, true);
  assert.equal(retryOf("Provider finish_reason: content_filter").worth, true);
  assert.equal(retryOf("模型说了句谁也没见过的话").worth, true);
});

// 下面两条**真的调 `piAsk`**。这两条路在发请求之前就判定失败（provider 不在名单里 /
// 那一家的模型目录里没有这个名字），因此**一个网络请求都不发**，可以进 CI。
// 它们钉的是分类器与 `piai.ts` 之间那处措辞耦合：`retry.ts` 按特征词认这两句话，
// 而这两句话写在 `piai.ts` 里 —— 谁改了措辞，这里当场红。

test("接线阶段就失败的那两档：一个请求都没发，重问读的还是同一份座位配置", async () => {
  const asked = { system: "", prompt: "", actionIds: ["0"], whatIfIds: [] };

  const unknownProvider = await piAsk({ ...asked, seat: { ...seat, provider: "no-such-family" } });
  assert.equal(unknownProvider.stopReason, "error");
  assert.equal(
    verdict(unknownProvider.errorMessage),
    "不值得：这个 provider 不在名单里，重问读的还是同一份座位配置",
  );

  const unknownModel = await piAsk({ ...asked, seat: { ...seat, model: "no-such-model" } });
  assert.equal(unknownModel.stopReason, "error");
  assert.equal(
    verdict(unknownModel.errorMessage),
    "不值得：这一家的模型目录里没有这个模型名，重问还是没有",
  );
});
