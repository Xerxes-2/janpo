/**
 * **渲出来的那句中文在日麻规则下成不成立**（票 41）。
 *
 * 判据在 `invariants.ts`，反向自证的变异在 `invariant-proofs.ts`，
 * 扫真实对局的那一趟在 `scripts/verify-invariants.mjs`（进 CI）。这一份文件把三件事钉在固件上：
 *
 * 1. committed 的那几份决策包渲出来，两档都没有一句不成立的话；
 * 2. **每条不变量各按红一次**——`PROOFS` 里的变异注进真 prompt，那一条必须当场点名；
 * 3. **票 40 那句原话逐字节喂进去必须被抓住**——那是这一票的合格线。
 *
 * 另有一条与文本无关的性质：**历史那一行只由那一条事件本身决定**（票 29b 可缓存前缀的前提）。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { historyLines } from "../../src/agent/history.ts";
import { renderPrompt } from "../../src/agent/prompt.ts";
import { DEFAULT_TEMPLATE, type PromptTemplate, readTemplate } from "../../src/agent/template.ts";
import type { DecisionPackage, ScaffoldTier } from "../../src/agent/types.ts";
import { DEFAULT_WORDING, wordsFor } from "../../src/agent/wording.ts";
import {
  ankanPackage,
  dahaiPackage,
  dangerPackage,
  kakanPackage,
  responsePackage,
  sequencePackages,
} from "./fixtures.ts";
import { PROOFS } from "./invariant-proofs.ts";
import { formatViolation, promptCoverage, promptViolations, RULES } from "./invariants.ts";

const TIERS: ScaffoldTier[] = ["bare", "assisted"];

const PACKAGES: { name: string; decision: DecisionPackage }[] = [
  { name: "decision-dahai", decision: dahaiPackage },
  { name: "decision-response", decision: responsePackage },
  { name: "decision-danger", decision: dangerPackage },
  // 桁上有杠的那两手（票 41）：三条不变量只有杠才验得到，而 29b 那一局里只有大明杠。
  { name: "decision-ankan", decision: ankanPackage },
  { name: "decision-kakan", decision: kakanPackage },
  ...sequencePackages.map((decision, hand) => ({ name: `decision-sequence[${hand}]`, decision })),
];

/** 固件语料：每一份包 × 两档。**档位只动尾部，而尾部也是话**，两档都得验。 */
const CORPUS = [
  ...PACKAGES.flatMap(({ name, decision }) =>
    TIERS.map((tier) => ({
      where: `${name}・${tier} 档`,
      prompt: renderPrompt(decision, tier, null),
    })),
  ),
  // 重试那一段接在最尾，它排在【可选动作】之后——解析器得知道动作那一节到哪里为止。
  {
    where: "decision-dahai・assisted 档・带重试原因",
    prompt: renderPrompt(dahaiPackage, "assisted", "action_id=99 不在这一手的合法动作集里"),
  },
];

test("固件上一句在日麻规则下不成立的话都没有", () => {
  const violations = CORPUS.flatMap((each) => promptViolations(each.prompt, { where: each.where }));

  assert.deepEqual(violations.map(formatViolation), [], "这几句话在日麻里不成立");
});

test("防空转：固件语料里真的有副露、有宝牌指示牌、有可选动作", () => {
  // 闸门绿有两种可能：话都成立，或者**一句都没解析出来**。这一条把后者堵死。
  const total: Record<string, number> = {};
  for (const each of CORPUS) {
    for (const [item, count] of Object.entries(promptCoverage(each.prompt))) {
      total[item] = (total[item] ?? 0) + count;
    }
  }

  assert.equal(total.seats, CORPUS.length * 4, "每份 prompt 都该解析出四家");
  assert.ok(
    total.chi > 0 && total.pon > 0,
    `语料里得有吃有碰，实为吃 ${total.chi}、碰 ${total.pon}`,
  );
  assert.ok(
    total.ankan > 0 && total.daiminkan > 0 && total.kakan > 0,
    `三种杠都得有，实为暗杠 ${total.ankan}、大明杠 ${total.daiminkan}、加杠 ${total.kakan}`,
  );
  assert.ok(total.dora > 0 && total.actions > 0 && total.historyLines > 0);
});

// ---- 逐条反向自证（M1 的规矩：闸门的证据是「它失败过」，不是「它存在」） ----

for (const proof of PROOFS) {
  test(`反向自证「${proof.rule}」：${proof.note}`, () => {
    const hit = CORPUS.map((each) => ({
      where: each.where,
      broken: proof.mutate(each.prompt),
    })).find((each) => each.broken !== null);

    assert.ok(hit !== undefined, `固件语料里注不进这种错：${proof.note}`);

    const caught = promptViolations(hit.broken as string, { where: hit.where });
    const mine = caught.filter((violation) => violation.rule === proof.rule);

    assert.ok(
      mine.length > 0,
      `这一条没咬住：${proof.note}之后它该红，实际红的是 ` +
        `${[...new Set(caught.map((violation) => violation.rule))].join("、") || "（一条都没红）"}`,
    );
    // 报错得点得出**哪一手、哪句话**——只说「有问题」的闸门查不动。
    assert.ok(mine[0].where.length > 0 && mine[0].sentence.length > 0);
  });
}

test("票 40 那句原话逐字节喂进去：闸门当场点名（这一票的合格线）", () => {
  // `reports/40-prompt-naki-frame-of-reference.md` §1 抄下来的修前原文：座位 0 吃的确实是
  // 座位 3 打的，**决策包里的绝对座位完全正确**，前缀字节也单调——当时十道闸门全绿。
  // 错的是把它翻成中文时用了观测者的参照系，写出了一句日麻里不可能的话。
  const good = renderPrompt(sequencePackages[11], "bare", null);
  assert.ok(good.includes("吃 2p（来自他的上家，亮出 3p 4p）"), "修后那句话该在这一手里");

  const broken = good.replace("吃 2p（来自他的上家，亮出 3p 4p）", "吃 2p（来自对家，亮出 3p 4p）");
  const caught = promptViolations(broken, { where: "票 40 修前的 decision-sequence[11]" });
  const rules = caught.map((violation) => violation.rule);

  assert.ok(rules.includes(RULES.chiFromKamicha), "吃只吃得了上家：这句「来自对家」在日麻里不成立");
  assert.ok(rules.includes(RULES.nakiFrame), "他家那几组的参照系是副露方，写光秃秃的方位词就漂了");

  const named = caught.map(formatViolation).join("\n");
  assert.match(named, /吃 2p（来自对家，亮出 3p 4p）/, "报错要抄出那句原话");
});

test("换一份模板照样验得动：判据从模板现取，不写死默认那几个字", () => {
  // M2 的三档对照实验会换措辞（票 31 把 prompt 降成了数据）。抬头与副露的词一换，
  // 闸门若认不出来就会**静静地全绿**——那正是这一票要堵的那种绿。
  const styled: PromptTemplate = readTemplate(
    JSON.stringify({
      id: "styled",
      labels: { history: "【战况回放】", others: "【另外三家】" },
      wording: { naki: { pon: "碰！", chi: "吃！" }, relative: { "3": "上手" } },
    }),
  );

  const prompt = renderPrompt(sequencePackages[11], "bare", null, styled);
  assert.deepEqual(
    promptViolations(prompt, { where: "styled 模板", template: styled }).map(formatViolation),
    [],
  );
  assert.ok(promptCoverage(prompt, styled).chi > 0, "换了措辞之后还得数得出那几组吃");

  // 同一句错在新措辞下照样抓得住（「上手」是这份模板里的上家）。
  const broken = prompt.replace("吃！ 2p（来自他的上手，", "吃！ 2p（来自他的对家，");
  const caught = promptViolations(broken, { where: "styled 模板", template: styled });

  assert.deepEqual([...new Set(caught.map((violation) => violation.rule))], [RULES.chiFromKamicha]);
});

// ---- 历史那一行只由那一条事件本身决定（票 29b 可缓存前缀的前提） ----

test("历史那一行只由那一条事件本身决定：截到第 k 条渲出来的，与整条流的前 k 行逐字相同", () => {
  // `prefix.test.ts` 钉的是**跨手**的单调（第 n 手的前缀是第 n+1 手的字节前缀），
  // 而那只走得到「真出现过的那几个断点」。这一条把流**逐条截断**，因此每一个断点都验到：
  // 某一行若偷看了它之后发生的事（比如把「这张后来被碰走了」写进打牌那一行），
  // 截断之后它就会变——而那正是 append-only 破功的那一刻。
  let cuts = 0;

  for (const decision of [dahaiPackage, dangerPackage, sequencePackages[11]]) {
    const words = wordsFor(
      DEFAULT_WORDING,
      decision.observation.self.seat,
      decision.observation.others,
    );
    const full = historyLines(decision, words);
    assert.ok(full.length >= 10, "太短的一条流证明不了什么");
    cuts += full.length;

    for (let cut = 1; cut <= full.length; cut += 1) {
      const truncated = historyLines(
        { ...decision, history: decision.history.slice(0, cut) },
        words,
      );

      assert.deepEqual(
        truncated,
        full.slice(0, cut),
        `截到第 ${cut} 条时，前面某一行变了——它依赖了它之后发生的事`,
      );
    }
  }

  assert.ok(cuts >= 90, `验了 ${cuts} 个断点，太少了`);
});

test("每一条不变量都有反向自证：新加一条而不证明它咬得动，这一条会红", () => {
  // M1 传下来的规矩：**闸门的证据是「它失败过」，不是「它存在」**。
  assert.deepEqual(
    [...new Set(PROOFS.map((proof) => proof.rule))].sort(),
    Object.values(RULES).slice().sort(),
  );
});

test("默认模板的抬头与措辞没被这一票改动", () => {
  // 这一票**不许改 prompt 的措辞与结构**（那是 M2 三档对照实验的自由变量）。
  // 闸门认的是这几个字，因此把它们钉在这里：改渲染的人会同时看见闸门与这条用例。
  assert.equal(DEFAULT_TEMPLATE.labels.hand, "【你的手牌】");
  assert.equal(DEFAULT_TEMPLATE.labels.others, "【其他三家】");
  assert.deepEqual(DEFAULT_TEMPLATE.wording.relative, { "1": "下家", "2": "对家", "3": "上家" });
  assert.equal(Object.keys(RULES).length, 11, "十一条不变量，改了要连同报告一起改");
});
