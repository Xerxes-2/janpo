/**
 * 分享载荷编解码的**确定性用例**（票 77）。浏览器里那道真闸门在
 * `web/scripts/verify-share.mjs`（真牌谱、真回放、逐位置的反向自证）；这里只钉这一层
 * 自己的四件事：往返、字符集、四种读不动、以及「改坏一个字符必须读不动」。
 *
 * `CompressionStream` 在 Node 里也有（v18 起），因此这一层不必开浏览器。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { decodePayload, encodePayload } from "../../src/share/payload.ts";

/** 信封读出来的那两种：`{text}` 或 `{error}`。 */
function envelope(json: string): { text?: string; error?: string } {
  return JSON.parse(json) as { text?: string; error?: string };
}

/** 一段有重复、有中文、有转义的文本：牌谱的形状（JSON 一行）在这一层只是字节。 */
const SAMPLE = JSON.stringify({
  version: 3,
  events: Array.from({ length: 200 }, (_, index) => ({
    type: "dahai",
    actor: index % 4,
    pai: "3s",
    tsumogiri: index % 2 === 0,
  })),
  note: "东1局 0 本场，供托 0 根。",
});

test("往返：编出来再解回来逐字相同", async () => {
  const payload = await encodePayload(SAMPLE);
  const { text, error } = envelope(await decodePayload(payload));

  assert.equal(error, undefined);
  assert.ok(text === SAMPLE, `解出来的与原文不同（${text?.length} 字符，原文 ${SAMPLE.length}）`);
});

test("载荷放得进 URL hash：+ / = 一个都不出现", async () => {
  const payload = await encodePayload(SAMPLE);
  assert.match(payload, /^[A-Za-z0-9_-]+$/);
  assert.ok(!payload.includes("+"));
  assert.ok(!payload.includes("/"));
  assert.ok(!payload.includes("="));
});

test("压得动：这一段重复的文本压完不到原来的一成", async () => {
  const payload = await encodePayload(SAMPLE);
  assert.ok(
    payload.length < SAMPLE.length / 10,
    `载荷 ${payload.length} 字符，原文 ${SAMPLE.length}`,
  );
});

test("空的、混了别的字符的、解压不开的，各给各的中文原因，且都不抛", async () => {
  for (const [payload, expected] of [
    ["", "没有载荷"],
    ["abc+def", "base64url 之外的字符"],
    ["abcd", "解压不开"],
  ] as const) {
    const { text, error } = envelope(await decodePayload(payload));
    assert.equal(text, undefined);
    assert.ok(error?.startsWith("载荷读不动："), `原因要以「载荷读不动：」开头，得到 ${error}`);
    assert.ok(error?.includes(expected), `原因里该说得出 ${expected}，得到 ${error}`);
  }
});

test("改坏一个字符：每个位置要么当场读不动，要么解出来与原文逐字相同", async () => {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
  const payload = await encodePayload(SAMPLE);
  let unreadable = 0;
  let identical = 0;

  for (let index = 0; index < payload.length; index += 1) {
    const swapped = alphabet[(alphabet.indexOf(payload[index]) + 1) % alphabet.length];
    const broken = payload.slice(0, index) + swapped + payload.slice(index + 1);
    const { text, error } = envelope(await decodePayload(broken));

    if (error !== undefined) {
      assert.ok(error.startsWith("载荷读不动："), `第 ${index} 位改坏之后红在了别处：${error}`);
      unreadable += 1;
    } else {
      // 解得开的那几位只可能落在末尾不承载信息的填充位上：解出来必须**逐字相同**，
      // 绝不许是「另一份读得动的牌谱」（`deflate-raw` 会，`deflate` 的 Adler-32 不会）。
      //
      // 比的是布尔不是 `assert.equal`：后者红的时候会把两份几十 KB 的文本整个吐出来，
      // 而这条断言要说的只是「它变成了另一份」。
      assert.ok(
        text === SAMPLE,
        `第 ${index} 位改坏之后解出了另一份东西（${text?.length} 字符，头 48 字：${text?.slice(0, 48)}）`,
      );
      identical += 1;
    }
  }

  // 断言真的开过口（判据 3：一条永远执行不到的断言与一条从不失败的断言，危害相同）。
  assert.ok(unreadable > payload.length * 0.9, `只有 ${unreadable}/${payload.length} 位读不动`);
  assert.ok(identical <= 4, `${identical} 位改坏之后照样解得开，多得不像填充位`);
});

test("截断的载荷读不动（聊天工具截 URL 是最常见的那一种坏法）", async () => {
  const payload = await encodePayload(SAMPLE);
  const { text, error } = envelope(await decodePayload(payload.slice(0, payload.length - 8)));

  assert.equal(text, undefined);
  assert.ok(error?.startsWith("载荷读不动："), `得到 ${error}`);
});
