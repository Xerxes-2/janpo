/**
 * prompt 渲染（票 23，Bare 档）。三件事：**该有的都在**、**不该有的一样都没有**
 * （他家暗牌、脚手架数值）、**重试时告诉模型上一次错在哪**。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { renderPrompt } from "../../src/agent/prompt.ts";
import { dahaiPackage, responsePackage } from "./fixtures.ts";

const bare = renderPrompt(dahaiPackage, "bare", null);

test("局面该有的都在：场况、自家手牌、他家、合法动作", () => {
  const observation = dahaiPackage.observation;

  assert.match(bare, /东1局 0 本场/);
  assert.match(bare, /宝牌指示牌：2s/);
  assert.match(bare, /牌山剩余可摸 66 张/);

  for (const pai of observation.self.tehai) {
    assert.ok(bare.includes(pai), `自家手牌里的 ${pai} 该出现在 prompt 里`);
  }

  for (const option of dahaiPackage.actions) {
    assert.ok(bare.includes(`id=${option.id}：${option.label}`), `动作 ${option.id} 该列出来`);
  }
});

test("副露与摸切标记：都是公开信息，都写出来", () => {
  // 自家有一组碰（3m），他家河里有摸切。
  assert.match(bare, /副露：pon 鸣3m\[3m 3m\]（来自座位 3）/);
  assert.match(bare, /5p\*/, "摸切的那张后面缀 `*`");
});

test("他家的暗牌在 prompt 里根本无从写起——决策包里就没有", () => {
  const others = dahaiPackage.observation.others;

  for (const other of others) {
    assert.equal(
      "tehai" in other,
      false,
      "MaskedSeat 类型里没有 tehai：隐藏信息的保护在结构上成立",
    );
    assert.ok(bare.includes(`手里 ${other.tehai_count} 张`), "他家只给得出张数");
  }
});

test("Bare 档不给任何算好的数：向听、有效牌、危险度一个都没有", () => {
  // 这一档存在的意义就是量「模型自己会不会数牌」。24 / 25 号票才往里加这些。
  for (const word of ["向听", "有效牌", "危险", "shanten", "ukeire"]) {
    assert.equal(bare.includes(word), false, `Bare 档的 prompt 不该出现「${word}」`);
  }
  assert.deepEqual(dahaiPackage.scaffold, {}, "Bare 档的 scaffold 槽位是空的");
});

test("响应那一手也渲染得出来：没摸牌，动作只有碰与过", () => {
  const prompt = renderPrompt(responsePackage, "bare", null);

  assert.match(prompt, /刚摸进：无（这一手不是你摸牌）/);
  assert.match(prompt, /id=1：过/);
});

test("重试时把上一次错在哪接在末尾", () => {
  const retry = renderPrompt(dahaiPackage, "bare", "action_id=99 不在这一手的合法动作集里");

  assert.ok(retry.startsWith(bare), "重试的 prompt 是原来那份加一段尾巴");
  assert.match(retry, /上一次的回答没有被采用/);
  assert.match(retry, /action_id=99/);
});

test("另外两档暂时照 Bare 渲染——24 号票在那两支上填 scaffold", () => {
  assert.equal(renderPrompt(dahaiPackage, "assisted", null), bare);
  assert.equal(renderPrompt(dahaiPackage, "tool_search", null), bare);
});
