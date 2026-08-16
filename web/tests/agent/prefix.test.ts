/**
 * **前缀字节稳定**（票 29b 的题眼）与**两种形态构造性一致**。
 *
 * 这两条是 CI 抓不到的那类回归：谁把一个「会被重算的量」（当前巡目、牌山剩余、序号、
 * 任何聚合数）写进了前缀，或者把历史那一段的措辞改了，**别的用例仍然一片绿**，
 * 只有账单会翻倍——provider 的前缀缓存全废。所以它们得有自己的用例。
 *
 * 用的是同一局里连续 12 手的真决策包（`decision-sequence.json`）。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import {
  cacheablePrefix,
  promptMessages,
  promptTail,
  rebuildMessages,
  renderPrompt,
} from "../../src/agent/prompt.ts";
import { DEFAULT_TEMPLATE, type PromptTemplate, readTemplate } from "../../src/agent/template.ts";
import type {
  DecisionPackage,
  KawaEntry,
  MaskedEvent,
  ScaffoldTier,
} from "../../src/agent/types.ts";
import { sequencePackages } from "./fixtures.ts";

const TIERS: ScaffoldTier[] = ["bare", "assisted"];

/**
 * 一份**换过人格与措辞**的模板（票 31）。前缀属性必须在它下面照样成立：
 * 人格与措辞都在可缓存前缀里，但它们**一局之内不变**，因此单调性不受影响。
 */
const STYLED: PromptTemplate = readTemplate(
  JSON.stringify({
    id: "styled",
    persona: "你是一位以防守见长的雀士。",
    labels: { history: "【战况回放】" },
    wording: { naki: { pon: "碰！" } },
  }),
);

const TEMPLATES: PromptTemplate[] = [DEFAULT_TEMPLATE, STYLED];

test("这一串确实是同一局、同一座位的连续几手", () => {
  assert.ok(sequencePackages.length >= 10, "太短的一串证明不了前缀在长");

  for (const decision of sequencePackages) {
    assert.equal(decision.seat, sequencePackages[0].seat);
    assert.equal(decision.observation.kyoku, sequencePackages[0].observation.kyoku);
  }

  const lengths = sequencePackages.map((decision) => decision.history.length);
  for (let hand = 1; hand < lengths.length; hand += 1) {
    assert.ok(lengths[hand] > lengths[hand - 1], "历史只会变长");
  }
});

// ---- 一、前缀字节稳定 ----

for (const tier of TIERS) {
  for (const template of TEMPLATES) {
    test(`前缀字节稳定（${tier} 档、${template.id} 模板）：第 n 手的可缓存前缀是第 n+1 手整份 prompt 的字节前缀`, () => {
      const prompts = sequencePackages.map((decision) =>
        renderPrompt(decision, tier, null, template),
      );
      const prefixes = sequencePackages.map((decision) =>
        cacheablePrefix(decision, tier, template),
      );

      for (let hand = 0; hand < sequencePackages.length; hand += 1) {
        assert.ok(
          prompts[hand].startsWith(prefixes[hand]),
          `第 ${hand} 手：整份 prompt 该以它自己的可缓存前缀开头`,
        );

        if (hand + 1 === sequencePackages.length) continue;

        assert.ok(
          prompts[hand + 1].startsWith(prefixes[hand]),
          `第 ${hand} 手的前缀该是第 ${hand + 1} 手 prompt 的字节前缀——前缀里出现了会被重算的量？`,
        );
        assert.ok(
          prefixes[hand + 1].startsWith(prefixes[hand]),
          `前缀是 append-only 的：第 ${hand} 手写下的行，第 ${hand + 1} 手不许改写`,
        );
        assert.ok(prefixes[hand + 1].length > prefixes[hand].length, "前缀只会长");
      }
    });
  }
}

test("换人格不破坏单调性：人格在 preamble 里，一局之内不变", () => {
  // 人格与措辞都在可缓存前缀里，因此**换它们就是换一份前缀**（跨版本不保证一致）；
  // 但同一次运行里它一字不变，因此同一局内的单调性照旧。
  const styled = sequencePackages.map((decision) => cacheablePrefix(decision, "bare", STYLED));
  const plain = sequencePackages.map((decision) => cacheablePrefix(decision, "bare"));

  assert.notEqual(styled[0], plain[0], "换了人格与措辞，前缀就不是同一份字节了");
  for (const prefix of styled) {
    assert.ok(prefix.startsWith(STYLED.persona), "人格排在最前面，逐手一样");
  }
});

test("system 那一条逐手逐字节相同：它是整份请求里最先被缓存的那一段", () => {
  for (const template of TEMPLATES) {
    const systems = sequencePackages.map(
      (decision) => promptMessages(decision, "bare", null, template).system,
    );

    assert.equal(new Set(systems).size, 1, `${template.id}：system 不该随手数变`);
  }
});

// ---- 一之二、尾部 + 重算出来的前缀 = 当时真发出去的那两条（票 31） ----

test("牌谱只存尾部：前缀重算一遍接回去，逐字节就是当时那两条消息", () => {
  // 重建的两个输入都不是“另存一份”：preamble 整场存一次，决策包由事件流 fold 重算。
  // **审计价值不打折**：尾部原样存着，渲染器当时出了什么怪都看得见。
  const notes = [null, "action_id=99 不在这一手的合法动作集里"];

  for (const template of TEMPLATES) {
    for (const tier of TIERS) {
      for (const note of notes) {
        for (const [hand, decision] of sequencePackages.entries()) {
          const sent = promptMessages(decision, tier, note, template);
          const tail = promptTail(decision, tier, note, template);
          const rebuilt = rebuildMessages(sent.system, decision, tail, template);

          assert.deepEqual(rebuilt, sent, `${template.id}・${tier}・第 ${hand} 手重建不回去`);
        }
      }
    }
  }
});

test("尾部真的只是尾部：历史那一段一行也不在里面", () => {
  const last = sequencePackages[sequencePackages.length - 1];
  const tail = promptTail(last, "assisted", null);
  const full = renderPrompt(last, "assisted", null);

  assert.ok(full.endsWith(tail), "尾部就是整份 prompt 的末尾");
  assert.equal(tail.includes("【到目前为止你看到的】"), false);
  assert.equal(tail.includes("摸牌"), false, "历史那几行一行都不该在尾部里");

  // 省下来的那一块**随手数变大**：前缀含前 n-1 手的历史，而尾部大小大致恒定
  // ——这正是票面说的「存全文在事件流式 prompt 下是 O(n²)」。
  const share = (decision: DecisionPackage) =>
    promptTail(decision, "bare", null).length / renderPrompt(decision, "bare", null).length;

  assert.ok(
    share(last) < share(sequencePackages[0]),
    `尾部占比该随手数下降，实为 ${share(sequencePackages[0]).toFixed(2)} → ${share(last).toFixed(2)}`,
  );
  assert.ok(tail.length < full.length);
});

test("重试不动前缀：多出来的那句话接在尾部，前面一个字节都不变", () => {
  const decision = sequencePackages[sequencePackages.length - 1];
  const plain = renderPrompt(decision, "bare", null);
  const retry = renderPrompt(decision, "bare", "action_id=99 不在这一手的合法动作集里");

  assert.ok(retry.startsWith(plain), "重试的 prompt 是原来那份加一段尾巴");
  assert.ok(retry.startsWith(cacheablePrefix(decision, "bare")));
});

test("两档共用同一段前缀：脚手架整个在尾部", () => {
  for (const decision of sequencePackages) {
    assert.equal(cacheablePrefix(decision, "bare"), cacheablePrefix(decision, "assisted"));
  }
});

test("前缀里没有任何「此刻的量」：那些只能待在尾部", () => {
  // 反例长这样：把「牌山剩余可摸 66 张」写进历史那一段——它每手都变，
  // 前缀因此每手都要重算，缓存一次也命不中。
  const prefix = cacheablePrefix(sequencePackages[sequencePackages.length - 1], "assisted");

  for (const banned of ["牌山剩余", "当前向听数", "危险度排序", "id=", "刚摸进："]) {
    assert.equal(prefix.includes(banned), false, `可缓存的前缀里不该出现「${banned}」`);
  }
});

test("越往后打，可缓存的那一段占得越多", () => {
  const share = (decision: DecisionPackage) =>
    cacheablePrefix(decision, "bare").length / renderPrompt(decision, "bare", null).length;

  const first = share(sequencePackages[0]);
  const last = share(sequencePackages[sequencePackages.length - 1]);

  assert.ok(
    last > first,
    `可缓存的占比该随手数上升，实为 ${first.toFixed(2)} → ${last.toFixed(2)}`,
  );
});

// ---- 二、尾部就是前缀那条流的 fold ----
//
// 引擎那边这件事是**构造性**的（29a 的一条掩蔽法则：`Observation` 就是掩蔽流的 fold），
// 这里验的是「送过接缝之后仍然是同一件事」：拿前缀里那条流数一遍，
// 与尾部那份快照逐字段对上。**不许出现两个来源**——渲染的时候一个数也不许现算。

function actorOf(event: MaskedEvent): number {
  return typeof event.actor === "number" ? event.actor : -1;
}

/** 某家的河：它打出去的每一张，按顺序。**被鸣走的那张仍留在河里**（CONTEXT.md 的 Kawa）。 */
function kawaFromHistory(history: MaskedEvent[], seat: number): KawaEntry[] {
  return history
    .filter((event) => event.type === "dahai" && actorOf(event) === seat)
    .map((event) => ({ pai: String(event.pai), tsumogiri: event.tsumogiri === true }));
}

/** 巡目：这家自己摸过几次牌。 */
function junmeFromHistory(history: MaskedEvent[], seat: number): number {
  return history.filter((event) => event.type === "tsumo" && actorOf(event) === seat).length;
}

/** 副露组数：碰 / 吃 / 三种杠各算一组（加杠是把原来那组碰变成杠，不新增一组）。 */
function nakiFromHistory(history: MaskedEvent[], seat: number): number {
  const groups = ["pon", "chi", "ankan", "daiminkan"];
  return history.filter((event) => groups.includes(event.type) && actorOf(event) === seat).length;
}

/** 宝牌指示牌：开局那张 + 每一条 `dora` 事件。 */
function doraFromHistory(history: MaskedEvent[]): string[] {
  return history
    .filter((event) => event.type === "start_kyoku" || event.type === "dora")
    .map((event) => String(event.dora_marker));
}

test("尾部的场况 ≡ 前缀那条流数出来的场况（逐手、逐座位）", () => {
  for (const [hand, decision] of sequencePackages.entries()) {
    const history = decision.history;
    const observation = decision.observation;
    const where = `第 ${hand} 手`;

    assert.deepEqual(doraFromHistory(history), observation.dora_markers, `${where} 宝牌指示牌`);

    const self = observation.self;
    assert.deepEqual(kawaFromHistory(history, self.seat), self.kawa, `${where} 自家的河`);
    assert.equal(junmeFromHistory(history, self.seat), self.junme, `${where} 自家的巡目`);
    assert.equal(nakiFromHistory(history, self.seat), self.naki.length, `${where} 自家的副露组数`);

    for (const other of observation.others) {
      assert.deepEqual(
        kawaFromHistory(history, other.seat),
        other.kawa,
        `${where} 座位 ${other.seat} 的河`,
      );
      assert.equal(
        junmeFromHistory(history, other.seat),
        other.junme,
        `${where} 座位 ${other.seat} 的巡目`,
      );
      assert.equal(
        nakiFromHistory(history, other.seat),
        other.naki.length,
        `${where} 座位 ${other.seat} 的副露组数`,
      );
    }
  }
});

test("尾部**渲染出来**的河，与前缀那条流数出来的一模一样", () => {
  // 上一条比的是数据，这一条比的是**写进 prompt 的那几个字**：
  // 摸切的 `*`、牌的记法与顺序，两段用的是同一套写法。
  for (const decision of sequencePackages) {
    const prompt = renderPrompt(decision, "bare", null);
    const seats = [
      decision.observation.self.seat,
      ...decision.observation.others.map((s) => s.seat),
    ];

    for (const seat of seats) {
      const entries = kawaFromHistory(decision.history, seat);
      const rendered =
        entries.length === 0
          ? "空"
          : entries.map((entry) => (entry.tsumogiri ? `${entry.pai}*` : entry.pai)).join(" ");

      assert.ok(prompt.includes(`牌河：${rendered}`), `座位 ${seat} 的牌河该渲成「${rendered}」`);
    }
  }
});
