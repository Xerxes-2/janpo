/**
 * **ToolSearch 档**（票 94）：单步 what-if 查询工具。
 *
 * 这一份文件钉住这一档的四条硬判据，每一条都是可执行的等式而不是一句承诺：
 *
 * 1. **同一个函数**：工具答的那一行，与 Assisted 档印的那一行**逐字节相同**（§2）。
 * 2. **前缀不分叉**：ToolSearch 的可缓存前缀 = Assisted 的前缀 + **恰好那一段工具说明**，
 *    别处一个字节都不差；那一段的位置也钉着（③之后、④之前，§3）。
 * 3. **Bare / Assisted 一个字都没变**：两档在全部固件上渲出来的字节，摘要钉死（§1）。
 *    这个数是**在这一票动手之前**从 `@-` 那一版渲染器上算出来的（报告 §「怎么守的」）。
 * 4. **上限有理由、到了就停**：上限是 `WHAT_IF_LIMIT`，理由在 `what-if.ts` 的文件头；
 *    到上限之后工具**不再进 `tools`**（§4），因此那一手照常打完（闭环那几条在 `loop.test.ts`）。
 */

import assert from "node:assert/strict";
import { test } from "node:test";
import { fnv1a } from "../../src/agent/hash.ts";
import { cacheablePrefix, promptTail, renderPrompt } from "../../src/agent/prompt.ts";
import { DEFAULT_TEMPLATE, type PromptTemplate, readTemplate } from "../../src/agent/template.ts";
import { chooseAction, toolsFor, toolsJson, toolsShape, whatIf } from "../../src/agent/tools.ts";
import type { DecisionPackage, ScaffoldTier } from "../../src/agent/types.ts";
import {
  WHAT_IF,
  WHAT_IF_LIMIT,
  WHAT_IF_PREAMBLE,
  whatIfExhausted,
} from "../../src/agent/what-if.ts";
import {
  ankanPackage,
  dahaiPackage,
  dangerPackage,
  kakanPackage,
  responsePackage,
  riichiPackage,
  sequencePackages,
} from "./fixtures.ts";
import {
  askedFor,
  crosscheckWhatIf,
  SCAFFOLD_CROSSCHECK,
  SCAFFOLD_PROOFS,
  toolSearchTail,
} from "./scaffold-crosscheck.ts";

/** 全部 committed 固件，18 份（`janpo decide` 的产物，含 dotnet 算的 `scaffold`）。 */
const PACKAGES: DecisionPackage[] = [
  dahaiPackage,
  responsePackage,
  dangerPackage,
  ankanPackage,
  kakanPackage,
  riichiPackage,
  ...sequencePackages,
];

/** 换过人格与措辞的那一份（与 `prefix.test.ts` 里那一份逐字相同）。 */
const STYLED: PromptTemplate = readTemplate(
  JSON.stringify({
    id: "styled",
    persona: "你是一位以防守见长的雀士。",
    labels: { history: "【战况回放】" },
    wording: { naki: { pon: "碰！" } },
  }),
);

const TEMPLATES: PromptTemplate[] = [DEFAULT_TEMPLATE, STYLED];
const NOTES = [null, "action_id=99 不在这一手的合法动作集里"];

/** 这一手哪几条是打牌（工具的 enum 就是它们）。 */
function discardsOf(decision: DecisionPackage): number[] {
  return decision.actions
    .filter((option) => (option.action as { type?: string }).type === "dahai")
    .map((option) => option.id);
}

// ---- 一、Bare / Assisted 一个字都没变（对照实验的自变量只许有一个） ----

test("Bare 与 Assisted 渲出来的字节，与这一票动手之前逐字相同", () => {
  // **这个数不是从当前代码算出来钉上去的**：它是在改任何一行之前，拿 `@-`（soxmwnpp）
  // 那一版渲染器按下面这个顺序算出来的（`jj file show -r @- web/src/agent/*` 抽到 /tmp 里跑）。
  // 因此它证的是「这一票没碰那两档」，而不是「当前代码等于当前代码」。
  //
  // 它红了只有两种可能：真改到了那两档（那就是票 94 的边界破了），
  // 或者有人**有意**改了 prompt 的措辞（那时连同这个数一起改，并在报告里说明改了什么）。
  const parts: string[] = [];
  for (const tier of ["bare", "assisted"] as ScaffoldTier[]) {
    for (const template of TEMPLATES) {
      for (const note of NOTES) {
        for (const decision of PACKAGES) parts.push(renderPrompt(decision, tier, note, template));
      }
    }
  }

  assert.equal(parts.length, 144, "18 份包 × 2 档 × 2 模板 × 2 种重试原因");
  assert.equal(
    fnv1a(parts.join("\u0000")),
    "21bab6bf",
    "Bare / Assisted 两档渲出来的字节变了——票 94 只许动 ToolSearch 那一档",
  );
});

test("一次都没查的 ToolSearch，尾部与 Bare 逐字节相同", () => {
  // 这一档加的是**能力**，不是又一批算好的数值（`CONTEXT.md` 的 `ScaffoldTier`）：
  // 没开口问之前，它看到的正文与裸奔档一模一样。
  for (const template of TEMPLATES) {
    for (const note of NOTES) {
      for (const decision of PACKAGES) {
        assert.equal(
          promptTail(decision, "tool_search", note, template),
          promptTail(decision, "bare", note, template),
        );
      }
    }
  }
});

test("ToolSearch 的尾部里一个算好的数都没有——除非它自己问了", () => {
  // 看的是**尾部**：前缀里那一段工具说明当然要点名它答得出哪几个量，而那是**能力的描述**，
  // 不是那几个量本身——两件事分开的地方就在这一条断言上。
  const quiet = promptTail(dahaiPackage, "tool_search", null);
  for (const word of ["向听", "有效牌", "进退向", "危险度"]) {
    assert.equal(quiet.includes(word), false, `没问之前尾部不该出现「${word}」`);
  }

  const asked = renderPrompt(dahaiPackage, "tool_search", null, DEFAULT_TEMPLATE, [{ id: 0 }]);
  assert.match(asked, /打完 3 向听，进退向 0，有效牌 66 枚/);
});

// ---- 二、同一个函数：查出来的那一行 ≡ Assisted 印的那一行 ----

test("同一个 id：工具答的那一行与 Assisted 档那一行逐字节相同（全部固件、全部打牌）", () => {
  let compared = 0;

  for (const template of TEMPLATES) {
    for (const decision of PACKAGES) {
      const assisted = promptTail(decision, "assisted", null, template).split("\n");

      for (const id of discardsOf(decision)) {
        const mine = promptTail(decision, "tool_search", null, template, [{ id }])
          .split("\n")
          // 抬头与「你查过几次」那一行不是答案本身。
          .filter((line) => line.startsWith("- "));

        assert.ok(mine.length >= 1, `id=${id} 至少该答出试打那一行`);
        for (const line of mine) {
          assert.ok(
            assisted.includes(line),
            `工具答的「${line}」在 Assisted 档那一节里找不到逐字相同的一行`,
          );
          compared += 1;
        }
      }
    }
  }

  // 判据 3：这一条在固件上真的开过口，而且不是一两次。
  assert.ok(compared > 200, `只比了 ${compared} 行，太少`);
});

test("查两条就答两条，顺序与问的顺序一致；同一条问两遍就答两遍（也算两次）", () => {
  const tail = promptTail(dangerPackage, "tool_search", null, DEFAULT_TEMPLATE, [
    { id: 3 },
    { id: 0 },
    { id: 3 },
  ]);
  const answered = tail.split("\n").filter((line) => /^- id=\d+（.+）：打完 /.test(line));

  assert.deepEqual(
    answered.map((line) => /^- id=(\d+)/.exec(line)?.[1]),
    ["3", "0", "3"],
  );
  assert.match(tail, /^你查过 3 次，还可以再查 1 次：$/m);
});

test("查不出「打完之后」的那一条：说一句，而不是静静地当作没有", () => {
  // 这一条从 `loop.ts` 走不到（工具的 enum 只摆打牌那几条，`whatIfCall` 再校一道），
  // 但渲染器是公开入口，收到一个答不上来的 id 时不许把那一行吞掉。
  const tail = promptTail(responsePackage, "tool_search", null, DEFAULT_TEMPLATE, [
    { id: 1 },
    { id: 99 },
  ]);

  assert.match(tail, /^- id=1（过）：这一条查不出「打完之后」的数。$/m);
  assert.match(tail, /^- id=99：这一条查不出「打完之后」的数。$/m);
});

// ---- 二之二、查出来的数 ↔ 决策包里那一份（dotnet 侧算的） ----

test(`「${SCAFFOLD_CROSSCHECK}」：全部固件逐字段对上`, () => {
  const judged: Record<string, number> = {};

  for (const decision of PACKAGES) {
    const tail = toolSearchTail(decision, WHAT_IF_LIMIT);
    const crosscheck = crosscheckWhatIf(decision, tail, "固件");

    assert.deepEqual(crosscheck.violations, []);
    for (const [what, times] of Object.entries(crosscheck.judged)) {
      judged[what] = (judged[what] ?? 0) + times;
    }
  }

  // 防空转：五类字段少一类，就是那一类这一趟没被对拍过。
  for (const what of ["向听", "进退向", "有效牌", "危险度名次", "危险度档位", "危险度理由"]) {
    assert.ok((judged[what] ?? 0) > 0, `「${what}」这一趟一次都没对拍到`);
  }
});

for (const proof of SCAFFOLD_PROOFS) {
  test(`反向自证「${SCAFFOLD_CROSSCHECK}」：${proof.note}`, () => {
    const hit = PACKAGES.map((decision) => ({
      decision,
      broken: proof.mutate(toolSearchTail(decision, WHAT_IF_LIMIT)),
    })).find((each) => each.broken !== null);

    assert.notEqual(hit, undefined, "固件里没有可改的地方——这个变异证明不了什么");
    const caught = crosscheckWhatIf(
      hit?.decision as DecisionPackage,
      hit?.broken as string,
      "反向自证",
    );
    assert.ok(caught.violations.length > 0, `${proof.note}之后这一道该红，实际一条都没红`);
  });
}

// ---- 三、可缓存前缀：分叉恰好是那一段，别处一个字节都不差 ----

test("Bare 与 Assisted 的前缀照旧逐字节相同（这一票没碰它）", () => {
  for (const template of TEMPLATES) {
    for (const decision of PACKAGES) {
      assert.equal(
        cacheablePrefix(decision, "bare", template),
        cacheablePrefix(decision, "assisted", template),
      );
    }
  }
});

test("ToolSearch 的前缀 = Assisted 的前缀 + 恰好那一段工具说明", () => {
  for (const template of TEMPLATES) {
    for (const decision of PACKAGES) {
      const assisted = cacheablePrefix(decision, "assisted", template);
      const mine = cacheablePrefix(decision, "tool_search", template);

      // 把那一段（连同它两边的空行）原样剪掉，剩下的必须逐字节等于 Assisted 那一份。
      // **这就是「不分叉」在第三档上还说得出口的那个形态**：分叉是一段可指名、可剪掉的常量，
      // 而不是「哪儿都可能不一样」。
      const block = `\n\n${WHAT_IF_PREAMBLE}\n`;
      assert.equal(mine.split(block).length, 2, "那一段在前缀里恰好出现一次");
      assert.equal(mine.replace(block, ""), assisted);
    }
  }
});

test("那一段的位置：【怎么读这份 prompt】之后、三段结构说明之前", () => {
  const prefix = cacheablePrefix(dahaiPackage, "tool_search", DEFAULT_TEMPLATE);

  const reading = prefix.indexOf("【怎么读这份 prompt】");
  const tools = prefix.indexOf(WHAT_IF_PREAMBLE);
  // 第④段的头一句就是历史那一节的抬头（模板给的），它同时是插入点的锚。
  const structure = prefix.indexOf(`\n${DEFAULT_TEMPLATE.labels.history}是你亲眼看见的`);

  assert.ok(reading >= 0 && tools > reading, "工具那一段排在【怎么读这份 prompt】之后");
  assert.ok(structure > tools, "工具那一段排在三段结构说明之前");
});

test("锚点不在的模板：那一段接在 system 末尾，绝不消失", () => {
  // 消失了模型就不知道自己能问，而工具又真的在 `tools` 里——那是最坏的一种沉默。
  const odd = readTemplate(JSON.stringify({ id: "odd", system: "只有一句规则说明。" }));
  const prefix = cacheablePrefix(dahaiPackage, "tool_search", odd);

  assert.ok(prefix.includes(WHAT_IF_PREAMBLE));
  assert.ok(prefix.startsWith(`只有一句规则说明。\n\n${WHAT_IF_PREAMBLE}`));
});

test("查过的那几条一条也不在前缀里：它们是这一手、这一轮的东西", () => {
  const asked = [{ id: 0 }, { id: 1 }];
  for (const decision of PACKAGES) {
    const prefix = cacheablePrefix(decision, "tool_search");
    const prompt = renderPrompt(decision, "tool_search", null, DEFAULT_TEMPLATE, asked);

    assert.ok(prompt.startsWith(prefix), "整份 prompt 仍以它自己的前缀开头");
    assert.equal(prefix.includes("你查过"), false);
  }
});

// ---- 四、上限：到了就停，而且是「调不出来」而不是「求它别调」 ----

test("上限是一个有理由的常数，不是随手写的 3", () => {
  // 理由在 `what-if.ts` 的文件头：手牌张数（问满 14 次就等于把 Assisted 那张表抄全）、
  // 账单（单手最坏 5 次请求）、延迟（票 79 的 1.7 s × 5 = 8.5 s，仍在思考档之下）。
  assert.equal(WHAT_IF_LIMIT, 4);
  assert.ok(WHAT_IF_LIMIT < 14, "问满一手的牌数就等于换条路拿全 Assisted 那张表");
});

test("还能问几次要写给模型看，问满那一次写的是 0", () => {
  for (let asked = 1; asked <= WHAT_IF_LIMIT; asked += 1) {
    const tail = promptTail(
      dahaiPackage,
      "tool_search",
      null,
      DEFAULT_TEMPLATE,
      askedFor(dahaiPackage, asked),
    );
    assert.match(
      tail,
      new RegExp(`^你查过 ${asked} 次，还可以再查 ${WHAT_IF_LIMIT - asked} 次：$`, "m"),
    );
  }
});

test("问满了就是问满了：`whatIfExhausted` 是上限的唯一判据", () => {
  assert.equal(whatIfExhausted([]), false);
  assert.equal(whatIfExhausted(askedFor(dahaiPackage, WHAT_IF_LIMIT - 1)), false);
  assert.equal(whatIfExhausted(askedFor(dahaiPackage, WHAT_IF_LIMIT)), true);
});

// ---- 五、工具定义 ----

test("what-if 工具的 enum 只摆打牌那几条：别的动作没有「打完之后」可算", () => {
  const tool = whatIf(["0", "3"]);

  assert.equal(tool.name, WHAT_IF);
  assert.equal(tool.name, "what_if");
  assert.match(JSON.stringify(tool.parameters), /"properties":\{"action_id":\{"type":"string"/);
  assert.match(JSON.stringify(tool), /"enum":\["0","3"\]/);
  assert.match(tool.description, new RegExp(`${WHAT_IF_LIMIT} 次`));
});

test("给不给这个工具，判据只有一条：这一轮还查得动的那几个 id 是不是空的", () => {
  const ids = ["0", "1"];

  assert.deepEqual(
    toolsFor(ids, []).map((tool) => tool.name),
    ["choose_action"],
  );
  assert.deepEqual(
    toolsFor(ids, ["0"]).map((tool) => tool.name),
    ["choose_action", WHAT_IF],
  );
  // 真发出去的那一份与写进牌谱的那一份读的是同一个函数（票 26 的那条性质没变）。
  assert.equal(toolsJson(ids, ["0"]), JSON.stringify([chooseAction(ids), whatIf(["0"])]));
});

test("牌谱里那一格看得出这一场把哪几个工具摆到了模型面前", () => {
  for (const tier of ["bare", "assisted"] as ScaffoldTier[]) {
    const shape = JSON.parse(toolsShape(tier)) as { name: string }[];
    assert.deepEqual(
      shape.map((tool) => tool.name),
      ["choose_action"],
    );
  }

  const search = JSON.parse(toolsShape("tool_search")) as { name: string }[];
  assert.deepEqual(
    search.map((tool) => tool.name),
    ["choose_action", WHAT_IF],
  );
  // 形状里两个 enum 都是空的（票 31：随手变的那一部分另记在 `ActionIds` 里）。
  assert.equal(toolsShape("tool_search").includes('"enum":[]'), true);
});

// ---- 六、这一段话本身不许把记法搞乱 ----

test("工具那一段里没有中文牌名、也没有 mjai 的动作 wire 名", () => {
  // 与第十二条不变量同一个判据（`RULES.mjaiOnly`）。这里单独钉一遍，是因为那一条扫的是
  // 渲出来的 prompt，而这一段是**常量**：写坏了应该在这里就红，不必等扫真语料那一趟。
  assert.doesNotMatch(WHAT_IF_PREAMBLE, /赤?[0-9一二三四五六七八九][万萬筒饼餅索]/);
  assert.doesNotMatch(
    WHAT_IF_PREAMBLE,
    /\b(dahai|tsumogiri|tsumo|pon|chi|ankan|daiminkan|kakan|reach|hora|ryukyoku|none)\b/,
  );
});
